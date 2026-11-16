using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Datos.Persistencia;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.UI.Logica;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Terceros.UI.VistaModelos;

/// <summary>Fila del buscador con los textos que pinta la tabla.</summary>
public sealed class FilaBuscador(TerceroEncontrado t, bool fichaAbierta)
{
    public TerceroEncontrado Datos { get; } = t;
    public int IdTercero => Datos.IdTercero;
    public string Nombre => Datos.Nombre;
    public string? NombreComercial => string.Equals(Datos.NombreComercial?.Trim(), Datos.Nombre.Trim(), StringComparison.OrdinalIgnoreCase) ? null : Datos.NombreComercial;
    public string Iniciales => FichaTerceroVM.Iniciales(Datos.Nombre);
    public string Tipo => TiposPersona.Texto(Datos.Tipo);
    public string? Nif => Datos.Nif;
    public string? Poblacion => Datos.Poblacion;
    public string? Telefono => Datos.Telefono;
    public string? Email => Datos.Email;
    public bool Activo => Datos.Activo;
    public bool FichaAbierta { get; } = fichaAbierta;
    public IReadOnlyList<RolResultado> Roles => Datos.Roles;
    public string RolesTexto => string.Join(" ", Datos.Roles.Where(r => r.Activo).Select(r => $"{r.Nombre} {r.Id}"));
    public string RolesAyuda => Datos.Roles.Count == 0 ? "Sin roles" : string.Join(Environment.NewLine, Datos.Roles.Select(r => $"{r.Nombre} {r.Id}{(r.Activo ? "" : " (inactivo)")}"));
    public string Perfiles => string.Join(", ", Datos.Perfiles);

    /// <summary>Dónde ha coincidido la búsqueda, sin repetir el nombre (que ya se ve).</summary>
    public string Coincidencia => string.Join(" · ", Datos.Coincidencias.Where(c => c.Fuente is not ("Nombre" or "Razón social" or "Nombre y apellidos")).Select(c => c.ToString()));

    public string CoincidenciaFuente => Datos.Coincidencias.FirstOrDefault(c => c.Fuente is not ("Nombre" or "Razón social" or "Nombre y apellidos"))?.Fuente ?? "";
    public string CoincidenciaValor => Datos.Coincidencias.FirstOrDefault(c => c.Fuente is not ("Nombre" or "Razón social" or "Nombre y apellidos"))?.Valor ?? "";
}

/// <summary>Opción de tipo de persona del selector segmentado, con su recuento.</summary>
public sealed class OpcionTipo(BuscadorTercerosVM buscador, string clave, string texto, string ayuda) : Observable
{
    public string Clave { get; } = clave;
    public string Texto { get; } = texto;
    public string Ayuda { get; } = ayuda;
    private int _recuento;
    public int Recuento { get => _recuento; set => Fijar(ref _recuento, value); }

    public bool Elegida
    {
        get => buscador.TipoElegido == Clave;
        set { if (value) buscador.TipoElegido = Clave; }
    }

    internal void Refrescar() => Avisar(nameof(Elegida));
}

/// <summary>
/// Buscador multicriterio de terceros (pestaña Terceros, RF-TER-01): un mismo texto en todos los datos, filtros por tipo,
/// rol, perfil e inactivos, y resultados con roles, coincidencia y población. También sirve, en modo selección, para
/// elegir un tercero en una ventana modal (relaciones).
/// </summary>
public sealed class BuscadorTercerosVM : Observable, IPestanaTrabajo
{
    private readonly ServiciosTerceros _s;
    private readonly ModuloTerceros? _modulo;
    private readonly IDialogos? _dialogos;
    private CancellationTokenSource? _busqueda;
    private object? _vista;

    public BuscadorTercerosVM(ServiciosTerceros servicios, ModuloTerceros? modulo, IDialogos? dialogos, bool modoSeleccion = false, FiltroTerceros? filtro = null)
    {
        _s = servicios;
        _modulo = modulo;
        _dialogos = dialogos;
        ModoSeleccion = modoSeleccion;
        Tipos =
        [
            new(this, "todos", "Todos", "Todos los tipos de persona"),
            new(this, "fisica", "Particulares", "Solo personas físicas"),
            new(this, "juridica", "Empresas", "Solo personas jurídicas (sociedades)"),
            new(this, "entidad", "Entidades", "Solo entidades sin personalidad (comunidades, asociaciones…)"),
        ];
        if (filtro is not null)
        {
            _texto = filtro.Texto ?? "";
            if (filtro.Tipo == TipoPersona.Fisica) _tipo = "fisica";
            else if (filtro.Tipo == TipoPersona.Juridica) _tipo = "juridica";
        }
        BuscarCommand = new ComandoDelegado(() => _ = BuscarAsync());
        AbrirCommand = new ComandoDelegado(Abrir, () => Seleccionada is not null);
        NuevoCommand = new ComandoDelegado(p => _modulo?.NuevaFicha(p as string));
        QuitarFiltrosCommand = new ComandoDelegado(QuitarFiltros, () => HayFiltros);
        ReintentarCommand = new ComandoDelegado(() => _ = BuscarAsync());
    }

    public bool ModoSeleccion { get; }

    // ---------- IPestanaTrabajo ----------
    public string Titulo => "Terceros";
    public string Ayuda => "Buscador de terceros: clientes, proveedores, contactos y demás personas y organizaciones";
    public string Icono => "user";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaBuscador { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _modulo?.BuscadorCerrado();

    // ---------- filtros ----------
    private string _texto = "";
    public string Texto
    {
        get => _texto;
        set { if (Fijar(ref _texto, value)) { Avisar(nameof(HayFiltros)); _ = BuscarAsync(conRetraso: true); } }
    }

    public IReadOnlyList<OpcionTipo> Tipos { get; }

    private string _tipo = "todos";
    public string TipoElegido
    {
        get => _tipo;
        set { if (Fijar(ref _tipo, value)) { foreach (var t in Tipos) t.Refrescar(); Avisar(nameof(HayFiltros)); _ = BuscarAsync(); } }
    }

    private bool _clientes, _proveedores, _contactos, _empleados, _inactivos;
    public bool Clientes { get => _clientes; set { if (Fijar(ref _clientes, value)) FiltroCambiado(); } }
    public bool Empleados { get => _empleados; set { if (Fijar(ref _empleados, value)) FiltroCambiado(); } }
    public bool Proveedores { get => _proveedores; set { if (Fijar(ref _proveedores, value)) FiltroCambiado(); } }
    public bool Contactos { get => _contactos; set { if (Fijar(ref _contactos, value)) FiltroCambiado(); } }
    public bool IncluirInactivos { get => _inactivos; set { if (Fijar(ref _inactivos, value)) FiltroCambiado(); } }

    /// <summary>Opción «Todos los perfiles» (no filtra).</summary>
    public static readonly OpcionPerfil TodosLosPerfiles = new(null);

    public ObservableCollection<OpcionPerfil> PerfilesDisponibles { get; } = [TodosLosPerfiles];
    private OpcionPerfil? _perfil;
    public OpcionPerfil PerfilElegido { get => _perfil ?? TodosLosPerfiles; set { if (Fijar(ref _perfil, value?.Perfil is null ? null : value)) FiltroCambiado(); } }

    private int _recClientes, _recProveedores, _recContactos, _recEmpleados;
    public int RecuentoClientes { get => _recClientes; private set => Fijar(ref _recClientes, value); }
    public int RecuentoEmpleados { get => _recEmpleados; private set => Fijar(ref _recEmpleados, value); }
    public int RecuentoProveedores { get => _recProveedores; private set => Fijar(ref _recProveedores, value); }
    public int RecuentoContactos { get => _recContactos; private set => Fijar(ref _recContactos, value); }

    public bool HayFiltros => _texto.Trim().Length > 0 || _tipo != "todos" || _clientes || _proveedores || _contactos || _empleados || _inactivos || _perfil is not null;

    private void FiltroCambiado()
    {
        Avisar(nameof(HayFiltros));
        _ = BuscarAsync();
    }

    private void QuitarFiltros()
    {
        _texto = ""; _tipo = "todos"; _clientes = _proveedores = _contactos = _empleados = _inactivos = false; _perfil = null;
        Avisar(nameof(Texto), nameof(Clientes), nameof(Proveedores), nameof(Contactos), nameof(Empleados), nameof(IncluirInactivos), nameof(PerfilElegido), nameof(HayFiltros));
        foreach (var t in Tipos) t.Refrescar();
        _ = BuscarAsync();
    }

    public FiltroTerceros Filtro() => new(
        _texto,
        _tipo switch { "fisica" => TipoPersona.Fisica, "juridica" => TipoPersona.Juridica, "entidad" => TipoPersona.EntidadSinPersonalidad, _ => null },
        false,
        new HashSet<RolBusqueda>((_clientes ? [RolBusqueda.Cliente] : Array.Empty<RolBusqueda>())
            .Concat(_proveedores ? [RolBusqueda.Proveedor] : []).Concat(_contactos ? [RolBusqueda.Contacto] : []).Concat(_empleados ? [RolBusqueda.Empleado] : [])),
        _perfil?.Perfil?.IdPerfil,
        _inactivos);

    // ---------- resultados ----------
    public ObservableCollection<FilaBuscador> Resultados { get; } = [];

    private FilaBuscador? _seleccionada;
    public FilaBuscador? Seleccionada { get => _seleccionada; set => Fijar(ref _seleccionada, value); }

    private EstadoVista _estado = EstadoVista.Cargando;
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }

    private string _tituloEstado = "Buscando…", _mensajeEstado = "";
    public string TituloEstado { get => _tituloEstado; private set => Fijar(ref _tituloEstado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    private string? _accionEstado;
    public string? AccionEstado { get => _accionEstado; private set => Fijar(ref _accionEstado, value); }
    private ICommand? _comandoEstado;
    public ICommand? ComandoEstado { get => _comandoEstado; private set => Fijar(ref _comandoEstado, value); }

    private string _pie = "";
    public string Pie { get => _pie; private set => Fijar(ref _pie, value); }

    private string _fichasAbiertas = "";
    public string FichasAbiertas { get => _fichasAbiertas; private set => Fijar(ref _fichasAbiertas, value); }

    public ICommand BuscarCommand { get; }
    public ICommand AbrirCommand { get; }
    public ICommand NuevoCommand { get; }
    public ICommand QuitarFiltrosCommand { get; }
    public ICommand ReintentarCommand { get; }

    /// <summary>En modo selección, el tercero elegido con doble clic o Intro.</summary>
    public event EventHandler<TerceroEncontrado>? Elegido;

    private bool _perfilesCargados;

    public async Task BuscarAsync(bool conRetraso = false)
    {
        _busqueda?.Cancel();
        var cancelar = _busqueda = new CancellationTokenSource();
        try
        {
            if (conRetraso) await Task.Delay(250, cancelar.Token);
            if (!_perfilesCargados)
            {
                _perfilesCargados = true;
                foreach (var p in await _s.Perfiles.ListarAsync()) PerfilesDisponibles.Add(new OpcionPerfil(p));
            }
            if (Resultados.Count == 0) { Estado = EstadoVista.Cargando; TituloEstado = "Buscando terceros…"; MensajeEstado = ""; AccionEstado = null; }
            var filtro = Filtro();
            var r = await Task.Run(() => _s.Buscador.BuscarAsync(filtro, cancelar.Token), cancelar.Token);
            if (cancelar.IsCancellationRequested) return;
            Mostrar(r);
        }
        catch (OperationCanceledException) { }
        catch (ExcepcionDatos ex)
        {
            if (cancelar.IsCancellationRequested) return;
            Resultados.Clear();
            Estado = EstadoVista.Error;
            TituloEstado = "No he podido buscar los terceros";
            MensajeEstado = ex.Error.TextoOperador();
            AccionEstado = "Reintentar";
            ComandoEstado = ReintentarCommand;
            Pie = "";
        }
    }

    private void Mostrar(ResultadoBusquedaTerceros r)
    {
        var anterior = Seleccionada?.IdTercero;
        var abiertas = _modulo?.IdsAbiertos() ?? new HashSet<int>();
        Resultados.Clear();
        foreach (var f in r.Filas) Resultados.Add(new FilaBuscador(f, abiertas.Contains(f.IdTercero)));
        Seleccionada = Resultados.FirstOrDefault(f => f.IdTercero == anterior) ?? Resultados.FirstOrDefault();
        Tipos[0].Recuento = r.Recuentos.Todos;
        Tipos[1].Recuento = r.Recuentos.Fisicas;
        Tipos[2].Recuento = r.Recuentos.Juridicas;
        Tipos[3].Recuento = r.Recuentos.Entidades;
        RecuentoClientes = r.Recuentos.Clientes;
        RecuentoProveedores = r.Recuentos.Proveedores;
        RecuentoContactos = r.Recuentos.Contactos;
        RecuentoEmpleados = r.Recuentos.Empleados;
        FichasAbiertas = _modulo is null ? "" : $"{_modulo.NumeroFichas} de {GestorFichas.Maximo} fichas abiertas";

        if (r.Filas.Count == 0)
        {
            Estado = EstadoVista.Vacio;
            if (HayFiltros)
            {
                TituloEstado = _texto.Trim().Length > 0 ? $"Ningún tercero coincide con «{_texto.Trim()}»" : "Ningún tercero cumple los filtros";
                MensajeEstado = "Busco en nombres, NIF, Ids, teléfonos, emails, domicilios, personas de contacto, perfiles y cuentas. Prueba con menos palabras o quita los filtros"
                    + (_inactivos ? "." : "; los terceros inactivos no salen si no marcas «Incluir inactivos».");
                AccionEstado = "Quitar búsqueda y filtros";
                ComandoEstado = QuitarFiltrosCommand;
            }
            else
            {
                TituloEstado = "Aún no hay terceros";
                MensajeEstado = ModoSeleccion ? "No hay ningún tercero que elegir." : "Crea el primero con «Nuevo».";
                AccionEstado = ModoSeleccion ? null : "Nuevo tercero";
                ComandoEstado = ModoSeleccion ? null : NuevoCommand;
            }
            Pie = "";
            return;
        }
        Estado = EstadoVista.Contenido;
        Pie = r.Recortado
            ? $"Muestro {Formatos.Recuento(r.Filas.Count)} de {Formatos.Plural(r.Total, "tercero", "terceros")}: afina la búsqueda para ver el resto"
            : Formatos.Plural(r.Total, "tercero", "terceros");
    }

    private void Abrir()
    {
        if (Seleccionada is not { } f) return;
        if (ModoSeleccion) Elegido?.Invoke(this, f.Datos);
        else _modulo?.AbrirFicha(f.IdTercero);
    }

    /// <summary>La pestaña se ha vuelto a mostrar o se ha guardado una ficha: repite la búsqueda para ver los cambios.</summary>
    internal void Refrescar() => _ = BuscarAsync();
}

/// <summary>Opción del filtro de perfil: un perfil del catálogo o «Todos los perfiles». Se pinta con su nombre.</summary>
public sealed class OpcionPerfil(Perfil? perfil)
{
    public Perfil? Perfil { get; } = perfil;
    public string Nombre => Perfil is null ? "Todos los perfiles" : Perfil.Activo ? Perfil.Nombre : $"{Perfil.Nombre} (retirado)";
    public override string ToString() => Nombre;
}
