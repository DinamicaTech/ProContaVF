using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Mantenimiento.UI;

/// <summary>Entrada de la navegación: el rótulo de una categoría o un mantenimiento.</summary>
public sealed class EntradaNavegacion(string texto, IProveedorMantenimiento? proveedor)
{
    public string Texto { get; } = texto;
    public IProveedorMantenimiento? Proveedor { get; } = proveedor;
    public bool EsCategoria => Proveedor is null;
    public string Ayuda => Proveedor?.Finalidad ?? Texto;
}

/// <summary>Fila de la lista de registros con los textos de las columnas ya formateados.</summary>
public sealed class FilaRegistro(RegistroMantenimiento registro, IProveedorMantenimiento proveedor)
{
    public RegistroMantenimiento Registro { get; } = registro;
    public IReadOnlyList<string> Celdas { get; } = proveedor.Columnas.Select(c => PantallaMantenimientoVM.Formatear(proveedor.Campos.FirstOrDefault(x => x.Clave == c.Clave), registro[c.Clave])).ToList();
    public string Titulo { get; } = proveedor.TituloRegistro(registro);
    public bool Coincide(string filtro) => string.IsNullOrWhiteSpace(filtro) || Celdas.Append(Titulo).Any(c => ServicioMantenimiento.Plano(c).Contains(ServicioMantenimiento.Plano(filtro)));
}

/// <summary>Grupo de campos del editor (una sección plegable si hay varios).</summary>
public sealed class GrupoEdicion(string titulo, IReadOnlyList<CampoEdicionVM> campos)
{
    public string Titulo { get; } = titulo;
    public IReadOnlyList<CampoEdicionVM> Campos { get; } = campos;
}

/// <summary>
/// Campo del editor: guarda el texto que escribe el operador, lo convierte al tipo del campo y muestra su error en el
/// propio campo (borde rojo, icono y tooltip) a través de <see cref="INotifyDataErrorInfo"/>.
/// </summary>
public sealed class CampoEdicionVM : ModeloVista, INotifyDataErrorInfo
{
    private readonly PantallaMantenimientoVM _pantalla;
    private string _texto = "";
    private bool _marcado;
    private OpcionCampo? _opcion;
    private string? _errorFormato;

    internal CampoEdicionVM(PantallaMantenimientoVM pantalla, CampoMantenimiento definicion, RegistroMantenimiento registro)
    {
        _pantalla = pantalla;
        Definicion = definicion;
        var valor = registro[definicion.Clave];
        Bloqueo = registro.Bloqueos.TryGetValue(definicion.Clave, out var b) ? b
            : definicion.SoloAlta && !registro.EsNuevo ? "Se fija al crear el registro y ya no se cambia." : null;
        if (definicion.Tipo == TipoCampo.Logico) _marcado = valor is true;
        else if (definicion.Tipo == TipoCampo.Lista) _opcion = Opciones.FirstOrDefault(o => Equals(Normalizar(o.Valor), Normalizar(valor))) ?? Opciones.FirstOrDefault(o => o.Valor is null);
        else _texto = PantallaMantenimientoVM.Formatear(definicion, valor);
    }

    public CampoMantenimiento Definicion { get; }
    public string Clave => Definicion.Clave;
    public string Etiqueta => Definicion.Etiqueta + (Definicion.Obligatorio ? " *" : "");
    public TipoCampo Tipo => Definicion.Tipo;
    public IReadOnlyList<OpcionCampo> Opciones => Definicion.Opciones ?? [];
    public string? Bloqueo { get; }
    public bool SoloLectura => Definicion.SoloLectura || Bloqueo is not null || _pantalla.SoloLectura;
    public bool Pendiente => Definicion.Pendiente;

    public string Ayuda => string.Join(Environment.NewLine, new[]
    {
        Error, Bloqueo, Definicion.Ayuda, Definicion.Pendiente ? "Significado pendiente de confirmar." : null,
        Definicion.Longitud is int l && Tipo is not (TipoCampo.Logico or TipoCampo.Lista) ? $"Hasta {l} caracteres." : null
    }.Where(x => !string.IsNullOrWhiteSpace(x)));

    public string Texto
    {
        get => _texto;
        set
        {
            if (!Fijar(ref _texto, value ?? "")) return;
            _errorFormato = null;
            var t = _texto.Trim();
            object? v = t.Length == 0 ? null : t;
            if (t.Length > 0 && Tipo is TipoCampo.Entero or TipoCampo.Decimal or TipoCampo.Porcentaje or TipoCampo.Importe)
            {
                if (LeerNumero(t, out var n)) v = Tipo == TipoCampo.Entero && n == Math.Truncate(n) ? (int)n : n;
                else { _errorFormato = $"{Definicion.Etiqueta}: escribe un número (por ejemplo 1.234,56)."; v = t; }
            }
            else if (t.Length > 0 && Tipo == TipoCampo.Fecha)
            {
                if (DateTime.TryParseExact(t, "dd/MM/yyyy", Formatos.Cultura, DateTimeStyles.None, out var f)) v = f;
                else { _errorFormato = $"{Definicion.Etiqueta}: escribe la fecha como dd/mm/aaaa."; v = t; }
            }
            else if (Tipo is TipoCampo.TextoLargo) v = string.IsNullOrWhiteSpace(_texto) ? null : _texto;
            _pantalla.Cambiar(Clave, v);
        }
    }

    public bool Marcado
    {
        get => _marcado;
        set { if (Fijar(ref _marcado, value)) _pantalla.Cambiar(Clave, value); }
    }

    public OpcionCampo? Opcion
    {
        get => _opcion;
        set { if (Fijar(ref _opcion, value)) _pantalla.Cambiar(Clave, value?.Valor); }
    }

    public string? Error => _errorFormato ?? _pantalla.ErrorDe(Clave);
    public bool TieneError => Error is not null;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => TieneError;
    public IEnumerable GetErrors(string? propertyName) => Error is { } e ? new[] { e } : Array.Empty<string>();

    internal bool ErrorDeFormato => _errorFormato is not null;

    internal void RefrescarError()
    {
        foreach (var p in new[] { nameof(Texto), nameof(Marcado), nameof(Opcion) }) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(p));
        Avisar(nameof(Error), nameof(TieneError), nameof(Ayuda));
    }

    private static object? Normalizar(object? v) => v switch { null => null, IConvertible c and not string => Convert.ToDecimal(c, CultureInfo.InvariantCulture), _ => v };

    internal static bool LeerNumero(string texto, out decimal n)
    {
        var t = texto.Replace("%", "").Replace("€", "").Replace(" ", "").Replace("\u00A0", "").Replace(Formatos.Menos, "-").Trim();
        // Un único punto con una o dos cifras detrás es el decimal del teclado numérico (7.5 → 7,5); si no, separa miles.
        if (!t.Contains(',') && t.Count(ch => ch == '.') == 1 && t.Length - t.IndexOf('.') - 1 is 1 or 2) t = t.Replace('.', ',');
        return decimal.TryParse(t, NumberStyles.Number, Formatos.Cultura, out n);
    }
}

/// <summary>
/// Pantalla genérica de mantenimiento (plataforma/mantenimiento, prototipo validado el 31/10): navegación por
/// categorías, búsqueda Ctrl+K, lista y editor con divisor, editores por tipo, Guardar y Descartar, solo lectura por
/// permiso o por proveedor, y el ámbito (empresa o ejercicio) que fija el contenedor.
/// </summary>
public sealed class PantallaMantenimientoVM : ModeloVista, IPestanaTrabajo
{
    private readonly ServicioMantenimiento _servicio;
    private readonly ContextoMantenimiento _contexto;
    private readonly Func<string, string, IReadOnlyList<string>?, OpcionMensaje[], int> _preguntar;
    private readonly Action<string> _avisar;
    private readonly Func<PantallaMantenimientoVM, object> _crearVista;
    private object? _vista;
    private IProveedorMantenimiento? _proveedor;
    private List<FilaRegistro> _todas = [];
    private RegistroMantenimiento? _original;
    private RegistroMantenimiento? _edicion;
    private Dictionary<string, string> _errores = new(StringComparer.OrdinalIgnoreCase);
    private string? _errorGeneral;
    private bool _puedeEditar;

    public PantallaMantenimientoVM(ServicioMantenimiento servicio, ContextoMantenimiento contexto, Func<string, string, IReadOnlyList<string>?, OpcionMensaje[], int> preguntar,
        Action<string> avisar, Func<PantallaMantenimientoVM, object> crearVista)
    {
        _servicio = servicio;
        _contexto = contexto;
        _preguntar = preguntar;
        _avisar = avisar;
        _crearVista = crearVista;
        foreach (var categoria in ServicioMantenimiento.Categorias)
        {
            var deCategoria = servicio.Proveedores.Where(p => p.Categoria == categoria).ToList();
            if (deCategoria.Count == 0) continue;
            Navegacion.Add(new EntradaNavegacion(categoria, null));
            foreach (var p in deCategoria) Navegacion.Add(new EntradaNavegacion(p.Titulo, p));
        }
        NuevoCommand = new ComandoDelegado(() => _ = NuevoAsync(), () => PuedeCrear);
        EliminarCommand = new ComandoDelegado(() => _ = EliminarAsync(), () => PuedeBorrar);
        GuardarCommand = new ComandoDelegado(() => _ = GuardarAsync(), () => TieneCambios && !SoloLectura);
        DescartarCommand = new ComandoDelegado(Descartar, () => TieneCambios);
        AccionCommand = new ComandoDelegado(p => _ = AccionAsync(p as string), _ => _edicion is { EsNuevo: false });
        AbrirResultadoCommand = new ComandoDelegado(p => { if (p is ResultadoBusqueda r) _ = AbrirAsync(r.Proveedor, r.Campo?.Clave); });
        ReintentarCommand = new ComandoDelegado(() => { if (_proveedor is not null) _ = CargarAsync(_proveedor, null); });
    }

    public async Task IniciarAsync(string? clave = null)
    {
        _puedeEditar = await _servicio.PuedeEditarAsync();
        var p = clave is null ? _servicio.Proveedores.FirstOrDefault() : _servicio.Proveedor(clave);
        if (p is not null) await AbrirAsync(p, null);
    }

    // ---------- IPestanaTrabajo ----------
    public string Titulo => "Parámetros y tablas";
    public string Ayuda => TieneCambios ? $"Parámetros y tablas auxiliares · cambios sin guardar en {_proveedor?.Titulo}" : "Parámetros y tablas auxiliares: configuración, empresas, ejercicios, series, conceptos y catálogos";
    public string Icono => "gear";
    private bool _guardadoFallido;
    public bool GuardadoFallido { get => _guardadoFallido; private set => Fijar(ref _guardadoFallido, value); }
    public object Vista => _vista ??= _crearVista(this);
    public void AlCerrar() { }

    public IReadOnlyList<string> DescribirCambios() =>
        TieneCambios && _proveedor is not null && _edicion is not null ? [$"{_proveedor.Titulo}: {_proveedor.TituloRegistro(_edicion)} ({CamposCambiados().Count} cambios)"] : [];

    // ---------- navegación y búsqueda ----------
    public ObservableCollection<EntradaNavegacion> Navegacion { get; } = [];
    public ObservableCollection<ResultadoBusqueda> ResultadosBusqueda { get; } = [];
    private string _busqueda = "";
    public string Busqueda
    {
        get => _busqueda;
        set
        {
            if (!Fijar(ref _busqueda, value)) return;
            ResultadosBusqueda.Clear();
            foreach (var r in _servicio.Buscar(value)) ResultadosBusqueda.Add(r);
            Avisar(nameof(HayBusqueda));
        }
    }
    public bool HayBusqueda => !string.IsNullOrWhiteSpace(_busqueda);

    private EntradaNavegacion? _entrada;
    public EntradaNavegacion? EntradaElegida
    {
        get => _entrada;
        set
        {
            if (value is null || value.EsCategoria || value.Proveedor == _proveedor) { Avisar(); return; }
            _ = AbrirAsync(value.Proveedor!, null);
        }
    }

    public IProveedorMantenimiento? Proveedor => _proveedor;
    public string TituloProveedor => _proveedor?.Titulo ?? "";
    public string FinalidadProveedor => _proveedor?.Finalidad ?? "";
    public bool EsLista => _proveedor?.Cardinalidad == Cardinalidad.Lista;

    public string TextoAmbito => _proveedor?.Ambito switch
    {
        AmbitoMantenimiento.Sociedad => $"Por empresa · {_contexto.RazonSocial}",
        AmbitoMantenimiento.Ejercicio => $"Por ejercicio · {_contexto.RazonSocial} · {_contexto.Trabajo.Ejercicio}",
        AmbitoMantenimiento.Equipo => "Este equipo",
        _ => "Compartido: afecta a todas las empresas"
    };

    public string AyudaAmbito => _proveedor?.Ambito switch
    {
        AmbitoMantenimiento.Sociedad => "Cada registro pertenece a una empresa: se ven y se crean los de la empresa de trabajo. Cámbiala en la cabecera del programa.",
        AmbitoMantenimiento.Ejercicio => "Cada registro pertenece a un ejercicio: se ven y se crean los del ejercicio de trabajo. Cámbialo en la cabecera del programa.",
        AmbitoMantenimiento.Equipo => "Se guarda en la configuración local de este puesto.",
        _ => "Se guarda en la base de datos y lo ven todas las empresas y todos los puestos."
    };

    /// <summary>Motivo de solo lectura de la pantalla, del proveedor o del registro activo (null si se puede editar).</summary>
    public string? MotivoSoloLectura =>
        _proveedor?.MotivoSoloConsulta ?? (!_puedeEditar ? "Tu perfil es de consulta: puedes ver los datos pero no cambiarlos." : _edicion?.MotivoSoloLectura);
    public bool SoloLectura => MotivoSoloLectura is not null;

    public async Task AbrirAsync(IProveedorMantenimiento proveedor, string? campo)
    {
        if (proveedor != _proveedor && !await ResolverCambiosAsync($"Cambiar a {proveedor.Titulo}")) { Avisar(nameof(EntradaElegida)); return; }
        Busqueda = "";
        await CargarAsync(proveedor, null);
        if (campo is not null) CampoEnfocado = campo;
    }

    private string? _campoEnfocado;
    /// <summary>Campo que la vista debe enfocar (resultado de la búsqueda o primer error).</summary>
    public string? CampoEnfocado { get => _campoEnfocado; set { _campoEnfocado = value; Avisar(); } }

    // ---------- lista ----------
    public ObservableCollection<FilaRegistro> Registros { get; } = [];
    private string _filtro = "";
    public string Filtro { get => _filtro; set { if (Fijar(ref _filtro, value)) Filtrar(); } }
    public string Recuento => _todas.Count == Registros.Count ? Formatos.Plural(_todas.Count, "registro", "registros") : $"{Registros.Count} de {_todas.Count}";

    private EstadoVista _estado = EstadoVista.Cargando;
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    private string _mensajeEstado = "";
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }

    private FilaRegistro? _seleccionada;
    public FilaRegistro? Seleccionada
    {
        get => _seleccionada;
        set
        {
            if (value == _seleccionada) return;
            if (value is not null && TieneCambios)
            {
                // La lista vuelve a la fila en edición mientras el operador decide qué hacer con sus cambios.
                Avisar();
                _ = SeleccionarConCambiosAsync(value);
                return;
            }
            _seleccionada = value;
            Avisar();
            Editar(value?.Registro);
        }
    }

    private async Task SeleccionarConCambiosAsync(FilaRegistro fila)
    {
        var clave = fila.Registro.Clave;
        if (!await ResolverCambiosAsync($"Abrir {fila.Titulo}")) return;
        var nueva = Registros.FirstOrDefault(f => Equals(f.Registro.Clave, clave)) ?? fila;
        _seleccionada = nueva;
        Avisar(nameof(Seleccionada));
        Editar(nueva.Registro);
    }

    private async Task CargarAsync(IProveedorMantenimiento proveedor, object? claveActiva)
    {
        _proveedor = proveedor;
        _entrada = Navegacion.FirstOrDefault(e => e.Proveedor == proveedor);
        Estado = EstadoVista.Cargando;
        Avisar(nameof(Proveedor), nameof(TituloProveedor), nameof(FinalidadProveedor), nameof(EsLista), nameof(TextoAmbito), nameof(AyudaAmbito), nameof(EntradaElegida),
            nameof(Columnas), nameof(Acciones), nameof(PuedeCrear), nameof(PuedeBorrar));
        try
        {
            var registros = await proveedor.ListarAsync(_contexto);
            if (proveedor != _proveedor) return;
            _todas = registros.Select(r => new FilaRegistro(r, proveedor)).ToList();
            Avisar(nameof(Columnas));
            Filtrar();
            Estado = EstadoVista.Contenido;
            var activa = _todas.FirstOrDefault(f => claveActiva is not null && Equals(Convert.ToString(f.Registro.Clave, CultureInfo.InvariantCulture), Convert.ToString(claveActiva, CultureInfo.InvariantCulture)))
                ?? (proveedor.Cardinalidad == Cardinalidad.Unico ? _todas.FirstOrDefault() : Registros.FirstOrDefault());
            _seleccionada = activa;
            Avisar(nameof(Seleccionada));
            Editar(activa?.Registro);
        }
        catch (Exception ex) when (ex is Datos.Persistencia.ExcepcionDatos or InvalidOperationException)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = ex is Datos.Persistencia.ExcepcionDatos d ? d.Error.TextoOperador() : ex.Message;
        }
    }

    private void Filtrar()
    {
        Registros.Clear();
        foreach (var f in _todas.Where(f => f.Coincide(_filtro))) Registros.Add(f);
        Avisar(nameof(Recuento));
    }

    public IReadOnlyList<ColumnaLista> Columnas => _proveedor?.Columnas ?? [];
    public IReadOnlyList<AccionMantenimiento> Acciones => _proveedor?.Acciones ?? [];
    public bool PuedeCrear => _proveedor is { PermiteAlta: true } && _puedeEditar && _proveedor.MotivoSoloConsulta is null;
    public bool PuedeBorrar => _proveedor is { PermiteBaja: true } && _puedeEditar && _edicion is { EsNuevo: false } && _edicion.MotivoSoloLectura is null;

    // ---------- editor ----------
    public ObservableCollection<GrupoEdicion> Grupos { get; } = [];
    public bool HayRegistro => _edicion is not null;
    public string TituloRegistro => _edicion is null || _proveedor is null ? "" : _proveedor.TituloRegistro(_edicion);
    public string? SubtituloRegistro => _edicion is null || _proveedor is null ? null : _proveedor.SubtituloRegistro(_edicion);
    public IReadOnlyList<string> ResumenErrores => [.. (_errorGeneral is null ? [] : new[] { _errorGeneral }), .. _errores.Values];
    public bool HayErrores => ResumenErrores.Count > 0;

    private void Editar(RegistroMantenimiento? registro)
    {
        _original = registro;
        _edicion = registro?.Copia();
        _errores = new(StringComparer.OrdinalIgnoreCase);
        _errorGeneral = null;
        ConstruirCampos();
        Avisar(nameof(HayRegistro), nameof(TituloRegistro), nameof(SubtituloRegistro), nameof(TieneCambios), nameof(ResumenErrores), nameof(HayErrores),
            nameof(MotivoSoloLectura), nameof(SoloLectura), nameof(PuedeBorrar), nameof(Ayuda), nameof(TextoCambios));
    }

    private void ConstruirCampos()
    {
        Grupos.Clear();
        if (_edicion is null || _proveedor is null) return;
        var campos = _proveedor.Campos.Select(c => new CampoEdicionVM(this, c, _edicion)).ToList();
        if (_proveedor.Grupos.Count == 0)
        {
            Grupos.Add(new GrupoEdicion("", campos));
            return;
        }
        foreach (var g in _proveedor.Grupos)
        {
            var deGrupo = campos.Where(c => c.Definicion.Grupo == g.Clave).ToList();
            if (deGrupo.Count > 0) Grupos.Add(new GrupoEdicion(g.Titulo, deGrupo));
        }
        var sueltos = campos.Where(c => _proveedor.Grupos.All(g => g.Clave != c.Definicion.Grupo)).ToList();
        if (sueltos.Count > 0) Grupos.Add(new GrupoEdicion("Otros datos", sueltos));
    }

    internal void Cambiar(string clave, object? valor)
    {
        if (_edicion is null) return;
        _edicion[clave] = valor;
        if (_errores.Remove(clave)) RefrescarErrores();
        GuardadoFallido = false;
        Avisar(nameof(TieneCambios), nameof(Ayuda), nameof(TextoCambios));
    }

    internal string? ErrorDe(string clave) => _errores.TryGetValue(clave, out var e) ? e : null;

    private List<string> CamposCambiados() =>
        _edicion is null || _original is null || _proveedor is null ? []
            : _proveedor.Campos.Where(c => !Igual(_original[c.Clave], _edicion[c.Clave])).Select(c => c.Etiqueta).ToList();

    private static bool Igual(object? a, object? b)
    {
        if (a is null || (a is string sa && sa.Length == 0)) return b is null || (b is string sb && sb.Trim().Length == 0);
        if (b is null) return false;
        if (a is IConvertible && b is IConvertible && a is not string && b is not string && a is not bool && b is not bool && a is not DateTime)
            return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
        return string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture)?.Trim(), Convert.ToString(b, CultureInfo.InvariantCulture)?.Trim(), StringComparison.Ordinal);
    }

    public bool TieneCambios => _edicion is not null && (_edicion.EsNuevo || CamposCambiados().Count > 0);
    public string TextoCambios => !TieneCambios ? "Sin cambios" : _edicion!.EsNuevo ? "Registro nuevo sin guardar" : Formatos.Plural(CamposCambiados().Count, "cambio sin guardar", "cambios sin guardar");

    public ICommand NuevoCommand { get; }
    public ICommand EliminarCommand { get; }
    public ICommand GuardarCommand { get; }
    public ICommand DescartarCommand { get; }
    public ICommand AccionCommand { get; }
    public ICommand AbrirResultadoCommand { get; }
    public ICommand ReintentarCommand { get; }

    private async Task NuevoAsync()
    {
        if (_proveedor is null || !await ResolverCambiosAsync($"Crear {_proveedor.NombreRegistro}")) return;
        var nuevo = await _proveedor.NuevoAsync(_contexto);
        _seleccionada = null;
        Avisar(nameof(Seleccionada));
        _original = nuevo.Copia();
        _edicion = nuevo;
        _errores = new(StringComparer.OrdinalIgnoreCase);
        _errorGeneral = null;
        ConstruirCampos();
        Avisar(nameof(HayRegistro), nameof(TituloRegistro), nameof(SubtituloRegistro), nameof(TieneCambios), nameof(ResumenErrores), nameof(HayErrores),
            nameof(MotivoSoloLectura), nameof(SoloLectura), nameof(PuedeBorrar), nameof(TextoCambios));
        CampoEnfocado = _proveedor.Campos.FirstOrDefault(c => !c.SoloLectura)?.Clave;
    }

    public async Task<bool> GuardarAsync()
    {
        if (_proveedor is null || _edicion is null) return true;
        var formato = Grupos.SelectMany(g => g.Campos).Where(c => c.ErrorDeFormato).ToList();
        if (formato.Count > 0)
        {
            MostrarErrores([], $"Corrige {Formatos.Plural(formato.Count, "campo", "campos")} con un valor que no se entiende.");
            CampoEnfocado = formato[0].Clave;
            GuardadoFallido = true;
            return false;
        }
        var r = await _servicio.GuardarAsync(_proveedor, _edicion, _contexto);
        if (!r.EsCorrecto)
        {
            MostrarErrores(r.Errores, null);
            GuardadoFallido = true;
            return false;
        }
        GuardadoFallido = false;
        var aviso = r.Valor["_Aviso"] as string;
        _avisar($"Hecho: he guardado {_proveedor.TituloRegistro(r.Valor)}.{(aviso is null ? "" : " " + aviso)}");
        await CargarAsync(_proveedor, r.Valor.Clave);
        return true;
    }

    private void MostrarErrores(IReadOnlyList<Error> errores, string? general)
    {
        var campos = _proveedor!.Campos.Select(c => c.Clave).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _errores = new(StringComparer.OrdinalIgnoreCase);
        var sueltos = new List<string>();
        foreach (var e in errores)
        {
            if (e.RegistroAfectado is string c && campos.Contains(c)) _errores.TryAdd(c, e.Mensaje);
            else sueltos.Add(e.TextoOperador());
        }
        _errorGeneral = general ?? (sueltos.Count > 0 ? string.Join(" ", sueltos) : null);
        RefrescarErrores();
        if (_errores.Count > 0) CampoEnfocado = _proveedor.Campos.First(c => _errores.ContainsKey(c.Clave)).Clave;
    }

    private void RefrescarErrores()
    {
        foreach (var c in Grupos.SelectMany(g => g.Campos)) c.RefrescarError();
        Avisar(nameof(ResumenErrores), nameof(HayErrores));
    }

    public void Descartar()
    {
        if (_edicion is { EsNuevo: true })
        {
            var primera = Registros.FirstOrDefault();
            _seleccionada = primera;
            Avisar(nameof(Seleccionada));
            Editar(primera?.Registro);
            return;
        }
        Editar(_original);
        GuardadoFallido = false;
    }

    private async Task EliminarAsync()
    {
        if (_proveedor is null || _edicion is null || _edicion.EsNuevo) return;
        var titulo = _proveedor.TituloRegistro(_edicion);
        var opcion = _preguntar($"Borrar {_proveedor.NombreRegistro}", $"Borro «{titulo}» de {_proveedor.Titulo}. No se puede deshacer.", null,
            [new OpcionMensaje("Borrar", $"Borra «{titulo}»", Peligro: true), new OpcionMensaje("No borrar", "Vuelve sin cambiar nada", Principal: true, Cancelar: true)]);
        if (opcion != 0) return;
        var r = await _servicio.EliminarAsync(_proveedor, _edicion, _contexto);
        if (!r.EsCorrecto)
        {
            _preguntar("No se ha borrado", r.Error!.TextoOperador(), r.Errores.Skip(1).Select(e => e.TextoOperador()).ToList(), [new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true)]);
            return;
        }
        _avisar($"Hecho: he borrado «{titulo}».");
        _edicion = null;
        await CargarAsync(_proveedor, null);
    }

    private async Task AccionAsync(string? clave)
    {
        if (_proveedor is null || _edicion is null || clave is null) return;
        var accion = _proveedor.Acciones.FirstOrDefault(a => a.Clave == clave);
        if (accion is null) return;
        if (accion.Modifica && TieneCambios && !await ResolverCambiosAsync(accion.Texto)) return;
        var r = await _servicio.EjecutarAsync(_proveedor, clave, _edicion, _contexto);
        if (!r.EsCorrecto)
        {
            _preguntar(accion.Texto, r.Error!.TextoOperador(), null, [new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true)]);
            return;
        }
        if (r.Valor.ClaveAbrir is not null)
        {
            _avisar(r.Valor.Mensaje);
            await CargarAsync(_proveedor, r.Valor.ClaveAbrir);
        }
        else
            _preguntar(accion.Texto, r.Valor.Mensaje, r.Valor.Detalle, [new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true)]);
    }

    /// <summary>Antes de salir del registro con cambios: guardar, descartar o seguir editando. true si se puede seguir.</summary>
    private async Task<bool> ResolverCambiosAsync(string queHara)
    {
        if (!TieneCambios || _proveedor is null) return true;
        var opcion = _preguntar("Cambios sin guardar", $"«{TituloRegistro}» tiene {TextoCambios.ToLowerInvariant()}. ¿Qué hago antes de: {queHara.ToLowerInvariant()}?", CamposCambiados(),
            [new OpcionMensaje("Guardar", "Guarda los cambios y sigue", Principal: true), new OpcionMensaje("Descartar", "Pierde los cambios y sigue", Peligro: true),
             new OpcionMensaje("Seguir editando", "Vuelve al registro sin hacer nada", Cancelar: true)]);
        switch (opcion)
        {
            case 0:
                return await GuardarAsync();
            case 1:
                Editar(_original is { EsNuevo: true } ? null : _original);
                return true;
            default:
                return false;
        }
    }

    // ---------- formato ----------
    internal static string Formatear(CampoMantenimiento? c, object? v)
    {
        if (v is null) return "";
        if (c?.Tipo == TipoCampo.Lista) return c.Opciones?.FirstOrDefault(o => Equals(Convert.ToString(o.Valor, CultureInfo.InvariantCulture), Convert.ToString(v, CultureInfo.InvariantCulture)))?.Texto ?? Convert.ToString(v, Formatos.Cultura) ?? "";
        return v switch
        {
            bool b => b ? "Sí" : "",
            DateTime f => f.TimeOfDay == TimeSpan.Zero ? Formatos.Fecha(f) : f.ToString("dd/MM/yyyy HH:mm", Formatos.Cultura),
            _ when c?.Tipo == TipoCampo.Importe => Formatos.Numero(Convert.ToDecimal(v, CultureInfo.InvariantCulture)),
            decimal or double or float when c?.Tipo != TipoCampo.Entero => Convert.ToDecimal(v, CultureInfo.InvariantCulture).ToString("#,##0.####", Formatos.Cultura),
            IConvertible n and not string when c?.Tipo is TipoCampo.Entero => Convert.ToDecimal(n, CultureInfo.InvariantCulture).ToString("0", CultureInfo.InvariantCulture),
            _ => Convert.ToString(v, Formatos.Cultura) ?? ""
        };
    }
}
