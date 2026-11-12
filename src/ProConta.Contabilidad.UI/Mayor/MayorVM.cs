using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Contabilidad.Mayor;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;
using ProConta.UI.Base.Preferencias;

namespace ProConta.Contabilidad.UI.Mayor;

/// <summary>Lo que necesita una pantalla del Mayor: la fuente, la sociedad y el ejercicio de trabajo.</summary>
/// <param name="Motor">Motor de asientos para las acciones que cambian apuntes (punteo, número de factura, concepto y cuenta); sin él, el Mayor es de solo consulta.</param>
/// <param name="Actor">Operador que firma los cambios y las marcas de cuadre.</param>
public sealed record ContextoMayor(IFuenteMayor Fuente, string IdSociedad, int EjercicioTrabajo, string RazonSocial,
    ProConta.Contabilidad.Asientos.IMotorAsientos? Motor = null, ProConta.Nucleo.Auditoria.Actor? Actor = null);

/// <summary>Diálogos del Mayor (los implementa <see cref="DialogosMayor"/>; las pruebas pueden sustituirlos).</summary>
public interface IDialogosMayor
{
    /// <summary>Selector de cuentas: devuelve el código elegido (subcuenta o código corto) o null.</summary>
    string? ElegirCuenta(SelectorCuentasVM selector, string titulo);
    void VerAsiento(AsientoMayor asiento, Action<string> abrirMayor);
    void VerHistorial(FilaMayor fila, IReadOnlyList<CambioApunteMayor> cambios);
    void Error(string titulo, string mensaje);

    /// <summary>Pide un texto (número de factura, concepto o comentario) y, si <paramref name="pideMotivo"/>, el motivo. Null si se cancela.</summary>
    (string Valor, string Motivo)? PedirTexto(string titulo, string explicacion, string etiqueta, string? inicial, int longitud, bool pideMotivo, bool valorObligatorio);

    /// <summary>Pide el motivo de un cambio; null si se cancela.</summary>
    string? PedirMotivo(string titulo, string explicacion);

    /// <summary>Cuadre de una cuenta: casilla «Cuenta cuadrada» y notas. Null si se cancela.</summary>
    (bool Cuadrada, string Notas)? EditarCuadre(string cuenta, bool cuadrada, string? notas, string informacion);
}

/// <summary>Cuenta de la lista de recientes del operador.</summary>
public sealed record CuentaReciente(string Codigo, string Nombre)
{
    public string Ayuda => $"{Codigo} {Nombre} · clic: ver solo esta · Ctrl+clic: añadirla o quitarla del mayor combinado · Supr: quitarla de las recientes";
}

/// <summary>Un ejercicio del selector de ejercicios (selección múltiple).</summary>
public sealed class OpcionEjercicioVM(EjercicioMayor e, bool marcado) : ModeloVista
{
    private bool _marcado = marcado;
    public EjercicioMayor Ejercicio { get; } = e;
    public string Texto => Ejercicio.Cerrado ? $"{Ejercicio.Ejercicio} · cerrado" : Ejercicio.Ejercicio.ToString();
    public string Ayuda => Ejercicio.Cerrado ? $"Ejercicio {Ejercicio.Ejercicio} cerrado: sus apuntes se ven en solo consulta" : $"Ejercicio {Ejercicio.Ejercicio}";
    public bool Marcado { get => _marcado; set => Fijar(ref _marcado, value); }
}

/// <summary>Etiqueta de filtro con su comando de quitar.</summary>
public sealed record EtiquetaVM(EtiquetaMayor Etiqueta, ICommand Quitar)
{
    public string Texto => Etiqueta.Texto;
    public string Ayuda => Etiqueta.Ayuda;
}

/// <summary>
/// Modelo de vista del Mayor (contabilidad/mayor): el mismo para la pestaña Contabilidad › Mayor y para el Mayor embebido en la
/// ficha de terceros (sin recientes; lo que sale de sus cuentas se abre en una pestaña del Mayor). Toda la lógica de cálculo
/// está en <see cref="SesionMayor"/>, así que el embebido y el completo coinciden bajo iguales filtros.
/// </summary>
public sealed class MayorVM : ModeloVista
{
    public const int MaximoRecientes = 30;
    private const string ClaveRecientes = "mayor.recientes";
    private const string ClaveEjercicios = "mayor.ejercicios";

    private readonly ContextoMayor _ctx;
    private readonly IDialogosMayor _dialogos;
    private readonly Action<string> _avisar;
    private readonly SesionMayor _sesion;
    private readonly IPreferenciasOperador _prefs;
    private readonly IReadOnlyList<string>? _cuentasFijas;
    private bool _iniciado;
    private EstadoVista _estado = EstadoVista.Vacio;
    private string _tituloEstado = "Elige una cuenta";
    private string _mensajeEstado = "Escribe un código o parte del nombre en el selector de cuentas (Ctrl+F), o elige una de tus recientes. Un código corto, como 572, muestra todas sus subcuentas juntas.";
    private string? _accionEstado = "Elegir cuenta…";
    private ICommand? _comandoEstado;
    private IReadOnlyList<FilaMayorVM> _filas = [];
    private string? _texto;
    private string _textoSeleccion = "";
    private FiltroCuentasMayor _filtroSelector = new();
    private bool _ocupado;

    public MayorVM(ContextoMayor contexto, IDialogosMayor dialogos, Action<string> avisar, IReadOnlyList<string>? cuentasFijas = null, IPreferenciasOperador? preferencias = null)
    {
        _ctx = contexto;
        _dialogos = dialogos;
        _avisar = avisar;
        _cuentasFijas = cuentasFijas;
        _prefs = preferencias ?? PreferenciasOperador.Actual;
        _sesion = new SesionMayor(contexto.Fuente, contexto.IdSociedad);
        ElegirCuentaCommand = new ComandoDelegado(() => _ = ElegirCuentaAsync(anadir: false), () => !Embebido);
        AnadirCuentaCommand = new ComandoDelegado(() => _ = ElegirCuentaAsync(anadir: true), () => !Embebido && _sesion.Cuentas.Count > 0);
        AnteriorCommand = new ComandoDelegado(() => _ = MoverAsync(-1), () => !Embebido && _sesion.Cuentas.Count == 1);
        SiguienteCommand = new ComandoDelegado(() => _ = MoverAsync(1), () => !Embebido && _sesion.Cuentas.Count == 1);
        RefrescarCommand = new ComandoDelegado(() => _ = RefrescarAsync());
        QuitarFiltrosCommand = new ComandoDelegado(() => _ = Ejecutar(() => _sesion.QuitarFiltrosAsync()), () => HayFiltros);
        VaciarRecientesCommand = new ComandoDelegado(VaciarRecientes, () => Recientes.Count > 0);
        QuitarRecienteCommand = new ComandoDelegado(p => { if (p is CuentaReciente r) QuitarReciente(r); });
        VistaCommand = new ComandoDelegado(p => { if (p is string s && Enum.TryParse<VistaMayor>(s, out var v)) CambiarVista(v); });
        AplicarEjerciciosCommand = new ComandoDelegado(() => _ = AplicarEjerciciosAsync());
        TodosLosEjerciciosCommand = new ComandoDelegado(() => { foreach (var e in Ejercicios) e.Marcado = true; _ = AplicarEjerciciosAsync(); });
        VerCuentaFijaCommand = new ComandoDelegado(p => _ = VerCuentasAsync(p is string c ? [c] : _cuentasFijas ?? []));
        AbrirEnMayorCommand = new ComandoDelegado(() => AbrirEnMayor?.Invoke(_sesion.Cuentas), () => Embebido && _sesion.Cuentas.Count > 0);
        _comandoEstado = ElegirCuentaCommand;
        if (!Embebido) CargarRecientes();
    }

    /// <summary>Embebido en otra pantalla (ficha de terceros): sin recientes ni selector; lo que sale de sus cuentas va a la pestaña del Mayor.</summary>
    public bool Embebido => _cuentasFijas is not null;
    public bool MostrarRecientes => !Embebido;

    /// <summary>Abre la pestaña del Mayor con unas cuentas (lo fija el contenedor; en el embebido, para salir de las cuentas del tercero).</summary>
    public Action<IReadOnlyList<string>>? AbrirEnMayor { get; set; }

    /// <summary>Abre la pestaña del Mayor buscando unas facturas en todas las cuentas (embebido).</summary>
    public Action<IReadOnlyList<string>>? BuscarEnMayor { get; set; }

    /// <summary>
    /// Abre la pantalla de asiento manual con una cuenta y el importe (Debe − Haber) de la selección (contabilidad/plantillas-asiento, PR 2);
    /// lo fija el contenedor. Sin él, «Nuevo asiento manual…» sale desactivado.
    /// </summary>
    public Action<string?, decimal>? NuevoAsiento { get; set; }

    /// <summary>
    /// Abre el asistente de corrección con un apunte (y su cuenta) como origen, o solo con una subcuenta si la selección es de varias filas de una
    /// misma cuenta: las correcciones de asiento y de apunte necesitan el apunte; las de subcuenta, solo la cuenta.
    /// </summary>
    public Action<int?, string?>? AsistenteCorreccion { get; set; }

    /// <summary>Abre un asiento manual del ejercicio de trabajo para modificarlo.</summary>
    public Action<int>? ModificarAsiento { get; set; }

    /// <summary>Ejercicio de trabajo (el único en que se crean y cambian asientos desde el Mayor).</summary>
    public int EjercicioTrabajo => _ctx.EjercicioTrabajo;

    public SesionMayor Sesion => _sesion;

    /// <summary>Aviso breve en la barra de estado del contenedor.</summary>
    public void Aviso(string texto) => _avisar(texto);

    // ---------- comandos ----------
    public ICommand ElegirCuentaCommand { get; }
    public ICommand AnadirCuentaCommand { get; }
    public ICommand AnteriorCommand { get; }
    public ICommand SiguienteCommand { get; }
    public ICommand RefrescarCommand { get; }
    public ICommand QuitarFiltrosCommand { get; }
    public ICommand VaciarRecientesCommand { get; }
    public ICommand QuitarRecienteCommand { get; }
    public ICommand VistaCommand { get; }
    public ICommand AplicarEjerciciosCommand { get; }
    public ICommand TodosLosEjerciciosCommand { get; }
    public ICommand VerCuentaFijaCommand { get; }
    public ICommand AbrirEnMayorCommand { get; }

    // ---------- estado ----------
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string TituloEstado { get => _tituloEstado; private set => Fijar(ref _tituloEstado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string? AccionEstado { get => _accionEstado; private set => Fijar(ref _accionEstado, value); }
    public ICommand? ComandoEstado { get => _comandoEstado; private set => Fijar(ref _comandoEstado, value); }
    public bool Ocupado { get => _ocupado; private set => Fijar(ref _ocupado, value); }

    public ObservableCollection<CuentaReciente> Recientes { get; } = [];
    public ObservableCollection<OpcionEjercicioVM> Ejercicios { get; } = [];
    public ObservableCollection<EtiquetaVM> Etiquetas { get; } = [];

    /// <summary>Cuentas del tercero en el Mayor embebido, para verlas juntas o una a una.</summary>
    public IReadOnlyList<CuentaReciente> CuentasFijas => _cuentasFijas?.Select(c => new CuentaReciente(c, _sesion.Nombre(c))).ToList() ?? [];

    public IReadOnlyList<FilaMayorVM> Filas { get => _filas; private set => Fijar(ref _filas, value); }

    public string Titulo => _sesion.Cuentas.Count switch
    {
        0 => "Mayor",
        1 => "Mayor " + _sesion.Cuentas[0],
        _ => $"Mayor combinado ({_sesion.Cuentas.Count})",
    };

    public string TextoCuenta => _sesion.Cuentas.Count switch
    {
        0 => "Elige una cuenta",
        1 => SelectorCuentasMayor.EsExacta(_sesion.Cuentas[0], _sesion.Catalogo)
            ? _sesion.Cuentas[0] + "  " + _sesion.Nombre(_sesion.Cuentas[0])
            : $"{_sesion.Cuentas[0]}…  {Formatos.Plural(_sesion.CuentasDeLaVista().Count, "subcuenta", "subcuentas")}",
        _ => string.Join(" + ", _sesion.Cuentas.Take(4)) + (_sesion.Cuentas.Count > 4 ? $" y {_sesion.Cuentas.Count - 4} más" : ""),
    };

    public string AyudaCuenta => _sesion.Cuentas.Count == 0
        ? "Elige la cuenta del mayor (Ctrl+F): un código, parte del nombre o un código corto para ver todas sus subcuentas"
        : "Cuentas de la vista: " + string.Join(" · ", _sesion.CuentasDeLaVista().Take(20).Select(c => c.Etiqueta)) + " · Ctrl+F: elegir otra";

    public string TextoEjercicios => _sesion.Elegidos.Count switch
    {
        0 => "Sin ejercicios",
        1 => "Ejercicio " + _sesion.Elegidos[0].Ejercicio,
        _ => "Ejercicios " + TextoAnios(_sesion.Elegidos.Select(e => e.Ejercicio).ToList()),
    };

    /// <summary>Años en tramos: «2018–2026», «2019, 2021–2023».</summary>
    internal static string TextoAnios(IReadOnlyList<int> anios)
    {
        var partes = new List<string>();
        for (var i = 0; i < anios.Count;)
        {
            var j = i;
            while (j + 1 < anios.Count && anios[j + 1] == anios[j] + 1) j++;
            partes.Add(j - i >= 2 ? $"{anios[i]}–{anios[j]}" : string.Join(", ", anios.Skip(i).Take(j - i + 1)));
            i = j + 1;
        }
        return string.Join(", ", partes);
    }

    public string TextoCerrados => string.Join(", ", _sesion.Elegidos.Where(e => e.Cerrado).Select(e => e.Ejercicio)) is { Length: > 0 } c
        ? (c.Contains(',') ? $"Los ejercicios {c} están cerrados: sus apuntes son de solo consulta." : $"El ejercicio {c} está cerrado: sus apuntes son de solo consulta.")
        : "";

    public string? Texto
    {
        get => _texto;
        set { if (Fijar(ref _texto, value)) { _sesion.FiltrarTexto(value); Refrescar(); } }
    }

    public VistaMayor Vista => _sesion.Vista;
    public bool EsNormal => Vista == VistaMayor.Normal;
    public bool EsCompacto => Vista == VistaMayor.Compacto;
    public bool EsSoloPendientes => Vista == VistaMayor.SoloPendientes;
    public bool EsAgrupar700 => Vista == VistaMayor.Agrupar700;

    public bool HayFiltros => _sesion.Etiquetas.Count > 0 || !string.IsNullOrWhiteSpace(_texto);
    public string? AvisoParcial => _sesion.Resultado.AvisoParcial;
    public string TextoRecuento => _sesion.Vista == VistaMayor.Normal
        ? Formatos.Plural(_sesion.Resultado.NumeroApuntes, "apunte", "apuntes")
        : $"{Formatos.Plural(_sesion.Resultado.Filas.Count, "fila", "filas")} · {Formatos.Plural(_sesion.Resultado.NumeroApuntes, "apunte", "apuntes")}";
    public string TotalDebe => Formatos.Importe(_sesion.Resultado.TotalDebe);
    public string TotalHaber => Formatos.Importe(_sesion.Resultado.TotalHaber);
    public decimal Saldo => _sesion.Resultado.Saldo;
    public string TotalSaldo => Formatos.Saldo(Saldo);
    public string GrupoSaldo => ReglaSaldo.ClaveComun(_sesion.Resultado.Filas.Select(f => f.Cuenta)) ?? "0";
    public string AyudaSaldo => ReglaSaldo.Descripcion(Saldo) + (_sesion.Resultado.EsParcial ? " de lo que se ve (saldo parcial)" : " de todas las filas de la vista");
    public string TextoSeleccion { get => _textoSeleccion; private set => Fijar(ref _textoSeleccion, value); }
    public bool HayCerrados => _sesion.HayCerrados;

    /// <summary>Cabecera de la pantalla: sociedad y ejercicios.</summary>
    public string Subtitulo => $"{_ctx.RazonSocial} · ejercicio de trabajo {_ctx.EjercicioTrabajo}";

    // ---------- carga ----------

    /// <summary>Primera carga (al mostrarse la pantalla): ejercicios, selector y, si las hay, las cuentas iniciales.</summary>
    public async Task IniciarAsync(IReadOnlyList<string>? cuentas = null)
    {
        if (_iniciado)
        {
            if (cuentas is { Count: > 0 }) await VerCuentasAsync(cuentas);
            return;
        }
        _iniciado = true;
        await Ejecutar(async () =>
        {
            var guardados = Embebido ? null : _prefs.Leer<List<int>>(ClaveEjercicios);
            await _sesion.IniciarAsync(_ctx.EjercicioTrabajo, guardados);
            Ejercicios.Clear();
            foreach (var e in _sesion.Ejercicios.OrderByDescending(e => e.Ejercicio)) Ejercicios.Add(new OpcionEjercicioVM(e, _sesion.Elegidos.Contains(e)));
            var iniciales = cuentas is { Count: > 0 } ? cuentas : _cuentasFijas;
            if (iniciales is { Count: > 0 }) await _sesion.VerCuentasAsync(iniciales);
        });
        if (cuentas is { Count: > 0 } && !Embebido) foreach (var c in cuentas) RecordarReciente(c);
        Avisar(nameof(CuentasFijas));
    }

    public Task VerCuentasAsync(IReadOnlyList<string> cuentas)
    {
        if (!_iniciado) return IniciarAsync(cuentas);
        if (!Embebido) foreach (var c in cuentas) RecordarReciente(c);
        _texto = null;
        Avisar(nameof(Texto));
        return Ejecutar(() => _sesion.VerCuentasAsync(cuentas));
    }

    /// <summary>Busca facturas en todas las cuentas; en el embebido, en una pestaña del Mayor.</summary>
    public Task BuscarFacturasAsync(IReadOnlyList<string> documentos)
    {
        if (Embebido && BuscarEnMayor is { } b) { b(documentos); return Task.CompletedTask; }
        return Ejecutar(() => _sesion.BuscarFacturasAsync(documentos));
    }

    public void FiltrarFacturas(IReadOnlyList<string> documentos)
    {
        _sesion.FiltrarFacturas(documentos);
        Refrescar();
    }

    public void CambiarVista(VistaMayor v)
    {
        _sesion.CambiarVista(v);
        Refrescar();
    }

    public void Quitar(IEnumerable<FilaMayorVM> filas)
    {
        var claves = filas.Select(f => f.Fila.Clave).ToList();
        if (claves.Count == 0) return;
        _sesion.Quitar(claves);
        Refrescar();
        _avisar($"{Formatos.Plural(claves.Count, "fila quitada", "filas quitadas")} de la vista (no se borra nada). Quita la etiqueta para volver a verlas.");
    }

    public Task RefrescarAsync() => Ejecutar(async () =>
    {
        await _sesion.ElegirEjerciciosAsync(_sesion.Elegidos.Select(e => e.Ejercicio).ToList());
    });

    private async Task AplicarEjerciciosAsync()
    {
        var anios = Ejercicios.Where(e => e.Marcado).Select(e => e.Ejercicio.Ejercicio).ToList();
        if (anios.Count == 0)
        {
            foreach (var e in Ejercicios) e.Marcado = _sesion.Elegidos.Contains(e.Ejercicio);
            _avisar("Marca al menos un ejercicio: el Mayor necesita uno para mostrar apuntes.");
            return;
        }
        if (anios.ToHashSet().SetEquals(_sesion.Elegidos.Select(e => e.Ejercicio))) return;
        if (!Embebido) _prefs.Guardar(ClaveEjercicios, anios);
        await Ejecutar(() => _sesion.ElegirEjerciciosAsync(anios));
    }

    /// <summary>Ejecuta una carga con el estado de carga y, si falla, el de error con «Reintentar».</summary>
    private async Task Ejecutar(Func<Task> trabajo)
    {
        Ocupado = true;
        if (_sesion.Resultado.Filas.Count == 0)
        {
            Estado = EstadoVista.Cargando;
            TituloEstado = "Cargando el mayor…";
            MensajeEstado = "Leo los apuntes de las cuentas y de los ejercicios elegidos.";
            AccionEstado = null;
        }
        try
        {
            await trabajo();
            Refrescar();
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            TituloEstado = "No he podido cargar el mayor";
            MensajeEstado = $"La base de datos ha respondido: {ex.Message} Si estaba ocupada (otro operador haciendo una copia o una importación), espera un momento y reintenta; si sigue fallando, comprueba la conexión a la carpeta de datos.";
            AccionEstado = "Reintentar";
            ComandoEstado = new ComandoDelegado(() => _ = Ejecutar(() => _sesion.CargarAsync()));
        }
        finally { Ocupado = false; }
    }

    /// <summary>Pasa el resultado de la sesión a la pantalla.</summary>
    private void Refrescar()
    {
        var r = _sesion.Resultado;
        var cerrados = _sesion.Elegidos.Where(e => e.Cerrado).Select(e => e.Ejercicio).ToHashSet();
        Filas = r.Filas.Select(f => new FilaMayorVM(f, CabeceraDe(f.Ejercicio, cerrados.Contains(f.Ejercicio))) { NombreDe = _sesion.Nombre }).ToList();
        Etiquetas.Clear();
        foreach (var e in _sesion.Etiquetas)
        {
            var tipo = e.Tipo;
            Etiquetas.Add(new EtiquetaVM(e, new ComandoDelegado(() => _ = Ejecutar(() => _sesion.QuitarEtiquetaAsync(tipo)))));
        }
        if (_sesion.Cuentas.Count == 0 && _sesion.Busqueda is null)
        {
            Estado = EstadoVista.Vacio;
            TituloEstado = Embebido ? "Este tercero no tiene cuentas contables" : "Elige una cuenta";
            MensajeEstado = Embebido
                ? "Asígnale su cuenta de cliente, proveedor o acreedor en la pestaña Roles y aquí verás su mayor."
                : "Escribe un código o parte del nombre en el selector de cuentas (Ctrl+F), o elige una de tus recientes. Un código corto, como 572, muestra todas sus subcuentas juntas.";
            AccionEstado = Embebido ? null : "Elegir cuenta…";
            ComandoEstado = ElegirCuentaCommand;
        }
        else if (r.Filas.Count == 0 && (HayFiltros || r.Ocultas > 0))
        {
            Estado = EstadoVista.Vacio;
            TituloEstado = _sesion.Vista == VistaMayor.SoloPendientes && !HayFiltros ? "No hay facturas pendientes" : "Ningún apunte cumple los filtros";
            MensajeEstado = _sesion.Vista == VistaMayor.SoloPendientes && !HayFiltros
                ? "Todas las facturas de la vista netean a cero (±0,05 €). Cambia a la vista Normal o Compacto para verlas."
                : "Hay apuntes en las cuentas de la vista, pero los filtros los ocultan todos. Revisa las etiquetas o quítalas.";
            AccionEstado = _sesion.Vista == VistaMayor.SoloPendientes && !HayFiltros ? "Ver la vista Normal" : "Quitar filtros";
            ComandoEstado = _sesion.Vista == VistaMayor.SoloPendientes && !HayFiltros ? new ComandoDelegado(() => CambiarVista(VistaMayor.Normal)) : QuitarFiltrosCommand;
        }
        else if (r.Filas.Count == 0)
        {
            Estado = EstadoVista.Vacio;
            TituloEstado = _sesion.Busqueda is { } b ? $"La factura {string.Join(", ", b)} no tiene apuntes" : $"Sin apuntes en {TextoEjercicios.ToLowerInvariant()}";
            MensajeEstado = _sesion.Busqueda is not null
                ? "No hay ningún apunte con ese número de factura (con o sin «F») en los ejercicios elegidos. Elige más ejercicios o quita la búsqueda."
                : "La cuenta no tiene movimientos en los ejercicios elegidos (los asientos de cierre no se muestran). Elige más ejercicios para ver su historia.";
            AccionEstado = Ejercicios.All(e => e.Marcado) ? null : "Ver todos los ejercicios";
            ComandoEstado = TodosLosEjerciciosCommand;
        }
        else Estado = EstadoVista.Contenido;
        TextoSeleccion = "";
        Avisar(nameof(Titulo), nameof(TextoCuenta), nameof(AyudaCuenta), nameof(TextoEjercicios), nameof(TextoCerrados), nameof(HayCerrados),
            nameof(Vista), nameof(EsNormal), nameof(EsCompacto), nameof(EsSoloPendientes), nameof(EsAgrupar700), nameof(HayFiltros), nameof(AvisoParcial),
            nameof(TextoRecuento), nameof(TotalDebe), nameof(TotalHaber), nameof(Saldo), nameof(TotalSaldo), nameof(GrupoSaldo), nameof(AyudaSaldo), nameof(CuentasFijas),
            nameof(TextoCuadre), nameof(AyudaCuadre), nameof(ColorCuadre), nameof(PuedeEditarCuadre), nameof(SoloNoPunteados));
        CommandManager.InvalidateRequerySuggested();
    }

    private static string CabeceraDe(int ejercicio, bool cerrado) => cerrado ? $"Ejercicio {ejercicio} · cerrado, solo consulta" : $"Ejercicio {ejercicio}";

    /// <summary>Total de la selección, en vivo.</summary>
    public void Seleccion(IReadOnlyCollection<FilaMayorVM> filas)
    {
        if (filas.Count == 0) { TextoSeleccion = ""; return; }
        var d = filas.Sum(f => f.Debe);
        var h = filas.Sum(f => f.Haber);
        TextoSeleccion = $"{Formatos.Plural(filas.Count, "fila seleccionada", "filas seleccionadas")} · debe {Formatos.Importe(d)} · haber {Formatos.Importe(h)} · diferencia {Formatos.Importe(d - h)}";
    }

    // ---------- selector y navegación ----------

    public SelectorCuentasVM NuevoSelector() => new(_sesion.Catalogo, _filtroSelector, TextoEjercicios);

    private async Task ElegirCuentaAsync(bool anadir)
    {
        if (!_iniciado) await IniciarAsync();
        var selector = NuevoSelector();
        var codigo = _dialogos.ElegirCuenta(selector, anadir ? "Añadir una cuenta a la vista" : "Elegir la cuenta");
        _filtroSelector = selector.Filtro;
        if (codigo is null) return;
        await VerCuentasAsync(anadir ? _sesion.Cuentas.Append(codigo).ToList() : [codigo]);
    }

    /// <summary>Cuenta anterior o siguiente de la lista del selector (con sus filtros), hasta la primera y la última.</summary>
    private async Task MoverAsync(int sentido)
    {
        var lista = SelectorCuentasMayor.Filtrar(_sesion.Catalogo, _filtroSelector);
        if (lista.Count == 0 || _sesion.Cuentas.Count != 1) return;
        var actual = _sesion.Cuentas[0];
        var i = -1;
        for (var k = 0; k < lista.Count; k++) if (lista[k].Codigo == actual) { i = k; break; }
        int destino;
        if (i >= 0) destino = i + sentido;
        else
        {
            // La actual no está en la lista (un código corto, o filtrada): la vecina por código.
            destino = sentido > 0 ? lista.ToList().FindIndex(c => string.CompareOrdinal(c.Codigo, actual) > 0) : lista.ToList().FindLastIndex(c => string.CompareOrdinal(c.Codigo, actual) < 0);
        }
        if (destino < 0 || destino >= lista.Count)
        {
            _avisar(sentido > 0 ? "Ya estás en la última cuenta de la lista del selector." : "Ya estás en la primera cuenta de la lista del selector.");
            return;
        }
        await VerCuentasAsync([lista[destino].Codigo]);
    }

    /// <summary>Ir a una cuenta (contrapartida): en el completo se carga aquí; en el embebido, si no es del tercero, en una pestaña del Mayor.</summary>
    public Task IrACuentaAsync(string cuenta)
    {
        if (Embebido && !(_cuentasFijas?.Contains(cuenta) ?? false))
        {
            AbrirEnMayor?.Invoke([cuenta]);
            return Task.CompletedTask;
        }
        return VerCuentasAsync([cuenta]);
    }

    // ---------- recientes ----------

    private void CargarRecientes()
    {
        Recientes.Clear();
        foreach (var r in _prefs.Leer<List<CuentaReciente>>(ClaveRecientes) ?? []) Recientes.Add(r);
    }

    private void GuardarRecientes()
    {
        if (Recientes.Count == 0) _prefs.Borrar(ClaveRecientes); else _prefs.Guardar(ClaveRecientes, Recientes.ToList());
    }

    private void RecordarReciente(string codigo)
    {
        var existente = Recientes.FirstOrDefault(r => r.Codigo == codigo);
        var nombre = _sesion.Nombre(codigo);
        if (nombre.Length == 0 && !SelectorCuentasMayor.EsExacta(codigo, _sesion.Catalogo)) nombre = "Todas las que empiezan por " + codigo;
        if (existente is not null) Recientes.Remove(existente);
        Recientes.Insert(0, new CuentaReciente(codigo, nombre.Length > 0 ? nombre : existente?.Nombre ?? ""));
        while (Recientes.Count > MaximoRecientes) Recientes.RemoveAt(Recientes.Count - 1);
        GuardarRecientes();
    }

    /// <summary>Clic en recientes: las seleccionadas forman la vista (una, o varias = mayor combinado).</summary>
    public Task VerRecientesAsync(IReadOnlyList<CuentaReciente> seleccionadas)
    {
        if (seleccionadas.Count == 0) return Task.CompletedTask;
        var codigos = seleccionadas.Select(r => r.Codigo).ToList();
        if (codigos.SequenceEqual(_sesion.Cuentas)) return Task.CompletedTask;
        _texto = null;
        Avisar(nameof(Texto));
        return Ejecutar(() => _sesion.VerCuentasAsync(codigos));
    }

    public void QuitarReciente(CuentaReciente r)
    {
        Recientes.Remove(r);
        GuardarRecientes();
    }

    private void VaciarRecientes()
    {
        Recientes.Clear();
        GuardarRecientes();
        _avisar("Lista de recientes vaciada. La vista actual sigue abierta.");
    }

    // ---------- acciones de fila ----------

    public async Task VerAsientoAsync(FilaMayorVM fila)
    {
        try
        {
            var a = await _ctx.Fuente.AsientoAsync(fila.Fila.IdAsiento);
            if (a is null)
            {
                _dialogos.Error("El asiento ya no existe", $"El asiento {fila.Fila.IdAsiento} se ha borrado o regenerado desde que cargaste el mayor. Actualiza la vista (F5) para verlo como está ahora.");
                return;
            }
            _dialogos.VerAsiento(a, c => _ = IrACuentaAsync(c));
        }
        catch (Exception ex)
        {
            _dialogos.Error("No he podido abrir el asiento", $"La base de datos ha respondido: {ex.Message} Vuelve a intentarlo en un momento.");
        }
    }

    public async Task VerHistorialAsync(FilaMayorVM fila)
    {
        try
        {
            var cambios = await _ctx.Fuente.HistorialAsync(fila.Fila.IdAsiento, fila.Fila.IdsApunte[0], fila.Fila.Orden);
            _dialogos.VerHistorial(fila.Fila, cambios);
        }
        catch (Exception ex)
        {
            _dialogos.Error("No he podido leer el historial", $"La base de datos ha respondido: {ex.Message} Vuelve a intentarlo en un momento.");
        }
    }

    // ---------- acciones que cambian apuntes (por el motor de asientos) y cuadre ----------

    /// <summary>Hay motor y operador: las acciones de punteo, numeración, concepto, cuenta y cuadre están disponibles.</summary>
    public bool PuedeEscribir => _ctx.Motor is not null && _ctx.Actor is not null;
    public string PorQueSoloConsulta => "Este Mayor es de solo consulta: se ha abierto sin el motor de asientos.";

    /// <summary>«Solo no punteados»: oculta las filas punteadas.</summary>
    public bool SoloNoPunteados
    {
        get => _sesion.SoloNoPunteados;
        set { if (value != _sesion.SoloNoPunteados) { _sesion.FiltrarNoPunteados(value); Avisar(nameof(SoloNoPunteados)); Refrescar(); } }
    }

    private static IReadOnlyList<int> Apuntes(IEnumerable<FilaMayorVM> filas) => filas.SelectMany(f => f.Fila.IdsApunte).Distinct().ToList();

    /// <summary>Puntea (o despuntea) los apuntes de las filas; también en automáticos y en ejercicios cerrados.</summary>
    public async Task PuntearAsync(IReadOnlyList<FilaMayorVM> filas, bool punteado)
    {
        if (!PuedeEscribir || filas.Count == 0) return;
        var ids = Apuntes(filas);
        if (await Escribir(punteado ? "No he podido puntear" : "No he podido quitar el punteo", () => _ctx.Motor!.PuntearApuntesAsync(ids, punteado, _ctx.Actor!), catalogo: false,
                recargar: false))
        {
            // El punteo no cambia importes: se aplica sobre lo ya leído, sin volver a cargar la vista.
            _sesion.AplicarPunteo(ids, punteado);
            Refrescar();
            _avisar($"{Formatos.Plural(ids.Count, "apunte", "apuntes")} {(punteado ? "punteado" : "sin puntear")}{(ids.Count == 1 ? "" : "s")}.");
        }
    }

    /// <summary>Pone el número de factura a los apuntes de las filas (también en automáticos y cerrados; se conserva al regenerar el lote).</summary>
    public async Task AsignarNumeroAsync(IReadOnlyList<FilaMayorVM> filas)
    {
        if (!PuedeEscribir || filas.Count == 0) return;
        var ids = Apuntes(filas);
        // Si solo una fila tiene número, ese número pasa a las demás sin preguntar (propietario, 2026-11-12).
        var conNumero = filas.Where(f => !string.IsNullOrWhiteSpace(f.Fila.Documento)).ToList();
        var numeros = conNumero.Select(f => f.Fila.Documento!.Trim()).Distinct(StringComparer.Ordinal).ToList();
        if (filas.Count > 1 && conNumero.Count == 1)
        {
            var origen = conNumero[0];
            var motivoCopia = $"Mismo número que el apunte {origen.Fila.Orden} del asiento {origen.Fila.IdAsiento}";
            if (await Escribir("No he podido poner el número de factura", () => _ctx.Motor!.AsignarNumeroDocumentoAsync(ids, numeros[0], _ctx.Actor!, motivoCopia), catalogo: false))
                _avisar($"Factura {numeros[0]} en {Formatos.Plural(ids.Count, "apunte", "apuntes")}.");
            return;
        }
        var propuesto = numeros.FirstOrDefault(n => !n.StartsWith("0000", StringComparison.Ordinal));
        var actuales = filas.Select(f => f.Fila.Documento?.Trim() ?? "").Distinct().ToList();
        var asientos = filas.SelectMany(f => f.Fila.IdsAsiento).Distinct().Count();
        var explicacion = $"Pondrás este número de factura en {Formatos.Plural(ids.Count, "apunte", "apuntes")} de {Formatos.Plural(asientos, "asiento", "asientos")}" +
                          (actuales.Count > 1 ? $", que ahora tienen {actuales.Count} números distintos." : actuales[0].Length > 0 ? $", que ahora tienen el {actuales[0]}." : ", que ahora no tienen número.") +
                          " Así se agrupan con su factura en las vistas Compacto y Solo pendientes. En los asientos automáticos también se puede, y se conserva si se regenera el asiento.";
        if (_dialogos.PedirTexto("Asignar número de factura", explicacion, "Número de factura", propuesto, 100, pideMotivo: true, valorObligatorio: false) is not { } r) return;
        if (await Escribir("No he podido poner el número de factura", () => _ctx.Motor!.AsignarNumeroDocumentoAsync(ids, r.Valor, _ctx.Actor!, r.Motivo), catalogo: false))
            _avisar(r.Valor.Length == 0 ? $"Número de factura quitado de {Formatos.Plural(ids.Count, "apunte", "apuntes")}." : $"Factura {r.Valor} en {Formatos.Plural(ids.Count, "apunte", "apuntes")}.");
    }

    /// <summary>Cambia el concepto de los apuntes de las filas (solo manuales y en periodos abiertos).</summary>
    public async Task CambiarConceptoAsync(IReadOnlyList<FilaMayorVM> filas)
    {
        if (!PuedeEscribir || filas.Count == 0) return;
        if (filas.Any(f => f.Fila.EsAutomatico)) { _dialogos.Error("No se puede cambiar el concepto", MotivoAutomatico); return; }
        var ids = Apuntes(filas);
        var actuales = filas.Select(f => f.Fila.Concepto).Distinct().ToList();
        if (_dialogos.PedirTexto("Cambiar el concepto", $"Nuevo concepto para {Formatos.Plural(ids.Count, "apunte", "apuntes")}. Vacío: el apunte usará el concepto de su asiento.",
                "Concepto", actuales.Count == 1 ? actuales[0] : null, 255, pideMotivo: true, valorObligatorio: false) is not { } r) return;
        if (await Escribir("No he podido cambiar el concepto", () => _ctx.Motor!.CambiarConceptoApuntesAsync(ids, r.Valor, _ctx.Actor!, r.Motivo), catalogo: false))
            _avisar($"Concepto cambiado en {Formatos.Plural(ids.Count, "apunte", "apuntes")}.");
    }

    /// <summary>Cambia la subcuenta de los apuntes de las filas (solo manuales), eligiéndola en el selector.</summary>
    public Task CambiarCuentaAsync(IReadOnlyList<FilaMayorVM> filas) =>
        CambiarCuentaDe(filas, f => f.Fila.IdsApunte, "Cambiar la subcuenta del apunte", "la subcuenta");

    /// <summary>Cambia la cuenta de la contrapartida de una fila (sus líneas de esa cuenta), eligiéndola en el selector.</summary>
    public Task CambiarContrapartidaAsync(FilaMayorVM fila) =>
        CambiarCuentaDe([fila], f => f.Fila.IdsApunteContrapartida, "Cambiar la contrapartida", "la contrapartida");

    public const string MotivoAutomatico = "Hay apuntes de asientos automáticos (facturas, importaciones…): su concepto y su cuenta se cambian en el documento de origen, " +
                                           "porque al regenerar su asiento el cambio se perdería. Sí puedes puntearlos y ponerles número de factura.";

    private async Task CambiarCuentaDe(IReadOnlyList<FilaMayorVM> filas, Func<FilaMayorVM, IReadOnlyList<int>> ids, string titulo, string que)
    {
        if (!PuedeEscribir || filas.Count == 0) return;
        if (filas.Any(f => f.Fila.EsAutomatico)) { _dialogos.Error($"No se puede cambiar {que}", MotivoAutomatico); return; }
        var porEjercicio = filas.GroupBy(f => _sesion.Elegidos.First(e => e.IdSociedadEjercicio == f.Fila.IdSociedadEjercicio))
            .Select(g => (Ejercicio: g.Key, Ids: g.SelectMany(ids).Distinct().ToList())).Where(x => x.Ids.Count > 0).ToList();
        if (porEjercicio.Count == 0) { _dialogos.Error($"No se puede cambiar {que}", "La fila no tiene una contrapartida única: ábrela con «Ver asiento» y cambia la línea que quieras desde el diario."); return; }
        var selector = NuevoSelector();
        selector.SoloSubcuentas = true;
        var codigo = _dialogos.ElegirCuenta(selector, $"{titulo}: elige la subcuenta nueva");
        if (codigo is null) return;
        if (!SelectorCuentasMayor.EsExacta(codigo, _sesion.Catalogo)) { _dialogos.Error("Elige una subcuenta", $"{codigo} es un código de grupo: los apuntes van siempre a una subcuenta que admite apuntes."); return; }
        var n = porEjercicio.Sum(x => x.Ids.Count);
        var motivo = _dialogos.PedirMotivo(titulo, $"Pasarás {Formatos.Plural(n, "apunte", "apuntes")} a la cuenta {codigo} {_sesion.Nombre(codigo)}. El cambio queda en el historial del apunte con su motivo.");
        if (motivo is null) return;
        var bien = true;
        foreach (var (ej, lista) in porEjercicio)
        {
            var contexto = new ProConta.Nucleo.Contexto.ContextoSociedadEjercicio(_ctx.IdSociedad, ej.IdSociedadEjercicio, ej.Ejercicio);
            bien &= await Escribir($"No he podido cambiar {que}", () => _ctx.Motor!.CambiarCuentaApuntesAsync(contexto, lista, codigo, _ctx.Actor!, motivo), catalogo: true);
            if (!bien) break;
        }
        if (bien) _avisar($"{Formatos.Plural(n, "apunte pasado", "apuntes pasados")} a la cuenta {codigo}.");
    }

    // ---------- cuadre (pie de la pantalla, solo el ejercicio de trabajo) ----------

    private EstadoCuadre EstadoCuadreActual => _sesion.Cuentas.Count == 0 ? EstadoCuadre.SinMovimientos : _sesion.CuadreDeLaVista().Estado;

    /// <summary>Estado para el color del pie: «verde», «ambar», «rojo» o vacío.</summary>
    public string ColorCuadre => EstadoCuadreActual switch { EstadoCuadre.Cuadrada => "verde", EstadoCuadre.EnCurso => "ambar", EstadoCuadre.SinCuadrar => "rojo", _ => "" };

    /// <summary>Texto del pie: la primera línea de las notas del cuadre, «Sin cuadrar», o el resumen de un mayor combinado.</summary>
    public string TextoCuadre
    {
        get
        {
            if (_sesion.Cuentas.Count == 0 || _sesion.Trabajo is null) return "";
            var (cuenta, estado, resumen) = _sesion.CuadreDeLaVista();
            if (cuenta is null) return estado == EstadoCuadre.SinMovimientos ? "" : $"Cuadre {_sesion.Trabajo.Ejercicio}: {resumen}";
            var primera = cuenta.Notas?.Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0);
            var prefijo = $"Cuadre {_sesion.Trabajo.Ejercicio} · {CuadresMayor.Texto(estado == EstadoCuadre.SinMovimientos ? EstadoCuadre.SinCuadrar : estado).ToLowerInvariant()}";
            return primera is null ? prefijo : $"{prefijo}: {primera}" + (cuenta.Aviso is null ? "" : " · el saldo ha cambiado");
        }
    }

    public string? AyudaCuadre
    {
        get
        {
            if (_sesion.Cuentas.Count == 0) return null;
            var (cuenta, _, resumen) = _sesion.CuadreDeLaVista();
            var accion = PuedeEditarCuadre ? "Clic: ver o cambiar el cuadre de la cuenta" : "Para cambiar el cuadre, abre una sola subcuenta";
            return (resumen ?? (cuenta is null ? "" : $"Sin cuadrar en {_sesion.Trabajo?.Ejercicio}")) + Environment.NewLine + accion;
        }
    }

    /// <summary>El cuadre se edita con una sola subcuenta en la vista.</summary>
    public bool PuedeEditarCuadre => PuedeEscribir && _sesion.Trabajo is not null && _sesion.CuadreDeLaVista().Cuenta is not null;

    /// <summary>Abre el diálogo de cuadre de la cuenta de la vista (casilla «Cuenta cuadrada» y notas) y lo guarda.</summary>
    public async Task EditarCuadreAsync()
    {
        if (!PuedeEditarCuadre || _sesion.CuadreDeLaVista().Cuenta is not { } v) return;
        var cuenta = v.Cuenta;
        var info = new List<string> { $"Saldo en {_sesion.Trabajo!.Ejercicio}: {Formatos.Importe(v.Saldo)}" };
        if (v.Aviso is not null) info.Add(v.Aviso);
        if (v.Registro is { } r) info.Add($"Última modificación: {r.FechaHora:dd/MM/yyyy HH:mm} por {r.Operador}");
        if (v.Heredadas) info.Add("Las notas vienen del ejercicio anterior: se guardan en este al aceptar.");
        var resultado = _dialogos.EditarCuadre($"{cuenta} {_sesion.Nombre(cuenta)}", v.Registro?.Cuadrada == true && v.Aviso is null, v.Notas, string.Join(Environment.NewLine, info));
        if (resultado is null) return;
        try
        {
            await _sesion.GuardarCuadreAsync(cuenta, resultado.Value.Cuadrada, resultado.Value.Notas, _ctx.Actor!.Nombre, DateTime.Now);
            _avisar(resultado.Value.Cuadrada ? $"{cuenta} cuadrada en {_sesion.Trabajo.Ejercicio}." : $"Cuadre de {cuenta} guardado.");
            AvisarCuadre();
        }
        catch (Exception ex) { _dialogos.Error("No he podido guardar el cuadre", $"La base de datos ha respondido: {ex.Message} Vuelve a intentarlo en un momento."); }
    }

    private void AvisarCuadre() => Avisar(nameof(TextoCuadre), nameof(AyudaCuadre), nameof(ColorCuadre), nameof(PuedeEditarCuadre));

    /// <summary>Hace un cambio por el motor y, si sale bien, vuelve a leer la vista (y el selector si cambian saldos); si no, explica por qué.</summary>
    private async Task<bool> Escribir(string titulo, Func<Task<ProConta.Nucleo.Resultados.Resultado<Guid>>> cambio, bool catalogo, bool recargar = true)
    {
        try
        {
            var r = await cambio();
            if (!r.EsCorrecto)
            {
                _dialogos.Error(titulo, string.Join(Environment.NewLine + Environment.NewLine, r.Errores.Select(e => e.TextoOperador())));
                return false;
            }
        }
        catch (Exception ex)
        {
            _dialogos.Error(titulo, $"La base de datos ha respondido: {ex.Message} No se ha guardado nada. Vuelve a cargar la vista (F5) y repite el cambio.");
            return false;
        }
        if (!recargar) return true;
        await Ejecutar(async () =>
        {
            if (catalogo) await _sesion.RefrescarCatalogoAsync();
            await _sesion.CargarAsync();
        });
        return true;
    }

    /// <summary>Texto que describe la vista para imprimir o exportar: cuentas, ejercicios, vista y filtros.</summary>
    public string DescripcionVista()
    {
        var vista = Vista switch { VistaMayor.Compacto => "Compacto", VistaMayor.SoloPendientes => "Solo pendientes", VistaMayor.Agrupar700 => "Agrupar 700", _ => "Normal" };
        var partes = new List<string> { TextoCuenta.Replace("  ", " "), TextoEjercicios, "vista " + vista };
        partes.AddRange(_sesion.Etiquetas.Select(e => e.Texto));
        if (!string.IsNullOrWhiteSpace(_texto)) partes.Add($"texto «{_texto}»");
        return string.Join(" · ", partes);
    }
}