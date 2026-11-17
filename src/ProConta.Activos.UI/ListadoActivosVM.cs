using System.Windows.Input;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Activos.UI;

/// <summary>Una fila del listado con su casilla: un clic en la fila la marca o la desmarca.</summary>
public sealed class FilaActivoVM(ActivoListado datos, Action alMarcar) : ModeloVista
{
    private bool _marcado;

    public ActivoListado Datos { get; } = datos;
    public int Id => Datos.IdActivo;
    public string Codigo => Datos.Codigo;
    public string Nombre => Datos.Nombre;
    public string Tipo => Datos.Tipo;
    public string Centro => Datos.EsAjeno ? "" : Datos.Centro.Length > 0 ? Datos.Centro : "sin centro";
    public string Compra => Datos.FechaCompra is DateTime f ? Formatos.Fecha(f) : "";
    public DateTime FechaOrden => Datos.FechaCompra ?? DateTime.MinValue;
    public bool Propio => !Datos.EsAjeno;
    public string Coste => Propio ? Formatos.Importe(Datos.Coste) : "solo informativo";
    public decimal CosteOrden => Datos.Coste;
    public string Amortizado => Propio && Datos.PorcentajeAmortizado is decimal p ? Math.Round(p * 100m, 0).ToString("0", Formatos.Cultura) + " %" : "";
    public decimal AmortizadoOrden => Datos.PorcentajeAmortizado ?? -1m;
    public string Ingresos => Propio ? Formatos.Importe(Datos.Ingresos) : "";
    public decimal IngresosOrden => Datos.Ingresos;
    public string Gastos => Propio ? Formatos.Importe(Datos.Gastos) : "";
    public decimal GastosOrden => Datos.Gastos;
    public string Resultado => Propio ? Formatos.Importe(Datos.Resultado) : "";
    public decimal ResultadoOrden => Datos.Resultado;
    public bool ResultadoNegativo => Propio && Datos.Resultado < 0m;
    public string Estado => Datos.SubcuentasQueFaltan > 1 ? $"Faltan {Datos.SubcuentasQueFaltan} subcuentas" : Datos.Estado;

    /// <summary>EN_USO, BAJA, AJENO o FALTA: el color de la pastilla del estado.</summary>
    public string ClaseEstado => Datos.EsAjeno ? "AJENO" : Datos.DeBaja ? "BAJA" : Datos.SubcuentasQueFaltan > 0 ? "FALTA" : "EN_USO";

    public string AyudaEstado => Datos.EsAjeno ? "No es de la sociedad: solo se guarda su información (sin subcuentas, centro ni amortización)."
        : Datos.DeBaja ? $"Dado de baja el {Formatos.Fecha(Datos.FechaBaja!.Value)}."
        : Datos.SubcuentasQueFaltan > 0 ? $"Le {(Datos.SubcuentasQueFaltan == 1 ? "falta 1 subcuenta" : $"faltan {Datos.SubcuentasQueFaltan} subcuentas")} de la plantilla de su tipo en el ejercicio de trabajo. Clic derecho › «Generar las subcuentas que le falten…»."
        : "En uso: tiene todas las subcuentas de su plantilla.";

    public string AyudaFila => Propio
        ? $"{Datos.Nombre}. Ingresos {Ingresos} − gastos {Gastos} (con la amortización prevista del año) = {Resultado}. Amortizado {Formatos.Importe(Datos.Amortizado)} de {Formatos.Importe(Datos.Amortizable)}. Doble clic: abrir la ficha."
        : $"{Datos.Nombre}. Activo ajeno a la sociedad, solo informativo. Doble clic: abrir la ficha.";

    public bool Marcado { get => _marcado; set { if (Fijar(ref _marcado, value)) alMarcar(); } }
}

/// <summary>
/// Datos maestros › Activos (activos 2026-11-17 18:50): los activos de la sociedad de trabajo con sus cifras del ejercicio, búsqueda y filtros por clase,
/// estado y ajenos, fila de totales de los propios y selección múltiple con casilla y un clic. Acciones generales en la cabecera; el menú contextual,
/// solo sobre lo marcado; doble clic abre la ficha.
/// </summary>
public sealed class ListadoActivosVM : ModeloVista, IPestanaTrabajo
{
    public static readonly IReadOnlyList<string> Clases = ["Todos", "Inmuebles", "Vehículos", "Otros"];
    public static readonly IReadOnlyList<string> Estados = ["En uso", "De baja", "Todos"];

    private readonly ModuloActivos _modulo;
    private object? _vista;
    private IReadOnlyList<FilaActivoVM> _todas = [];
    private IReadOnlyList<FilaActivoVM> _filas = [];
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private string _busqueda = "";
    private string _clase = "Todos";
    private string _filtroEstado = "En uso";
    private bool _ajenos;
    private int? _centro;
    private string? _textoCentro;
    private bool _ocupado;
    private bool _marcando;

    internal ListadoActivosVM(ModuloActivos modulo)
    {
        _modulo = modulo;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        NuevoCommand = new ComandoDelegado(() => _ = _modulo.NuevoAsync(), () => !_ocupado);
        GenerarTodasCommand = new ComandoDelegado(() => _ = GenerarAsync(_todas.Where(f => f.Propio && !f.Datos.DeBaja).ToList(), "Generar subcuentas de todos los activos"),
            () => !_ocupado && _todas.Count > 0);
        AbrirCommand = new ComandoDelegado(p => Abrir(p, ApartadoFicha.Datos), p => Unico(p) is not null);
        CuentaResultadosCommand = new ComandoDelegado(p => Abrir(p, ApartadoFicha.CuentaResultados), p => Unico(p) is { Propio: true });
        MayorCommand = new ComandoDelegado(_ => _ = MayorAsync(), _ => _modulo.Contexto.AbrirMayor is not null && Marcadas.Count > 0 && Marcadas.All(f => f.Propio));
        GenerarMarcadosCommand = new ComandoDelegado(_ => _ = GenerarAsync(Marcadas, Marcadas.Count == 1 ? $"Subcuentas de {Marcadas[0].Codigo}" : $"Subcuentas de {Marcadas.Count} activos"),
            _ => !_ocupado && Marcadas.Count > 0 && Marcadas.All(f => f.Propio && !f.Datos.DeBaja));
        QuitarCentroCommand = new ComandoDelegado(() => FiltrarCentro(null, null));
        MarcarTodosCommand = new ComandoDelegado(() => MarcarTodos(!TodosMarcados));
    }

    // ---------- pestaña ----------
    public string Titulo => "Activos";
    public string Ayuda => $"Activos de {_modulo.Contexto.RazonSocial}";
    public string Icono => "layers";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaListadoActivos { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _modulo.ListadoCerrado();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand NuevoCommand { get; }
    public ICommand GenerarTodasCommand { get; }
    public ICommand AbrirCommand { get; }
    public ICommand CuentaResultadosCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand GenerarMarcadosCommand { get; }
    public ICommand QuitarCentroCommand { get; }
    public ICommand MarcarTodosCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_modulo.Contexto.RazonSocial} · ejercicio {_modulo.Contexto.Trabajo.Ejercicio}";
    public int Ejercicio => _modulo.Contexto.Trabajo.Ejercicio;
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    // ---------- filtros ----------
    public string Busqueda { get => _busqueda; set { if (Fijar(ref _busqueda, value)) Filtrar(); } }
    public string Clase { get => _clase; set { if (Fijar(ref _clase, value ?? "Todos")) Filtrar(); } }
    public string FiltroEstado { get => _filtroEstado; set { if (Fijar(ref _filtroEstado, value ?? "En uso")) Filtrar(); } }
    public bool MostrarAjenos { get => _ajenos; set { if (Fijar(ref _ajenos, value)) Filtrar(); } }
    public bool HayFiltroCentro => _centro is not null;
    public string TextoFiltroCentro => _textoCentro is null ? "" : $"Centro: {_textoCentro}";

    internal void FiltrarCentro(int? idCentro, string? texto)
    {
        _centro = idCentro;
        _textoCentro = idCentro is null ? null : texto;
        Avisar(nameof(HayFiltroCentro), nameof(TextoFiltroCentro));
        Filtrar();
    }

    // ---------- filas ----------
    public IReadOnlyList<FilaActivoVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas)); } }
    public IReadOnlyList<FilaActivoVM> Marcadas => _filas.Where(f => f.Marcado).ToList();
    public bool TodosMarcados { get => _filas.Count > 0 && _filas.All(f => f.Marcado); set => MarcarTodos(value); }
    public string Recuento => Formatos.Plural(_filas.Count, "activo", "activos") + (Marcadas.Count > 0 ? $" · {Formatos.Plural(Marcadas.Count, "marcado", "marcados")}" : "");

    private IEnumerable<FilaActivoVM> Propios => _filas.Where(f => f.Propio);
    public bool HayTotales => Propios.Any();
    public string TextoTotales => $"Total de {Formatos.Plural(Propios.Count(), "activo", "activos")} de la sociedad";
    public string TotalCoste => Formatos.Importe(Propios.Sum(f => f.Datos.Coste));
    public string TotalIngresos => Formatos.Importe(Propios.Sum(f => f.Datos.Ingresos));
    public string TotalGastos => Formatos.Importe(Propios.Sum(f => f.Datos.Gastos));
    public string TotalResultado => Formatos.Importe(Propios.Sum(f => f.Datos.Resultado));
    public bool TotalNegativo => Propios.Sum(f => f.Datos.Resultado) < 0m;

    public async Task CargarAsync()
    {
        if (Ocupado) return;
        try
        {
            if (_todas.Count == 0) Estado = EstadoVista.Cargando;
            Ocupado = true;
            if (!await _modulo.Contexto.Servicio.PuedeAsync())
            {
                Estado = EstadoVista.SinPermiso;
                MensajeEstado = "No tienes permiso para ver ni modificar los activos. Pídeselo a quien administra los permisos (acción «Activos»).";
                return;
            }
            var marcados = _todas.Where(f => f.Marcado).Select(f => f.Id).ToHashSet();
            var lista = await _modulo.Contexto.Servicio.ListarAsync(_modulo.Contexto.Trabajo);
            _todas = lista.Select(a => new FilaActivoVM(a, AlMarcar) { Marcado = marcados.Contains(a.IdActivo) }).ToList();
            Filtrar();
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los activos: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private void Filtrar()
    {
        var q = (_busqueda ?? "").Trim();
        Filas = _todas.Where(f =>
                (_clase == "Todos" || f.Datos.Clase == _clase)
                && (_filtroEstado == "Todos" || (_filtroEstado == "De baja") == f.Datos.DeBaja)
                && (_ajenos || f.Propio)
                && (_centro is null || f.Datos.IdCentroCoste == _centro)
                && (q.Length == 0 || $"{f.Codigo} {f.Nombre} {f.Datos.NombreCorto} {f.Datos.Centro}".Contains(q, StringComparison.CurrentCultureIgnoreCase)))
            .ToList();
        foreach (var f in _todas.Except(_filas)) f.Marcado = false; // lo que no se ve no queda marcado
        if (Estado != EstadoVista.SinPermiso)
        {
            Estado = _todas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
            MensajeEstado = _todas.Count == 0 ? "Esta sociedad aún no tiene activos. Pulsa «Nuevo activo…» para dar de alta el primero." : "";
        }
        AlMarcar();
        Avisar(nameof(HayTotales), nameof(TextoTotales), nameof(TotalCoste), nameof(TotalIngresos), nameof(TotalGastos), nameof(TotalResultado), nameof(TotalNegativo),
            nameof(SinResultados));
    }

    /// <summary>Hay activos pero ninguno cumple los filtros.</summary>
    public bool SinResultados => _todas.Count > 0 && _filas.Count == 0;

    private void AlMarcar()
    {
        if (_marcando) return;
        Avisar(nameof(Recuento), nameof(TodosMarcados));
        CommandManager.InvalidateRequerySuggested();
    }

    private void MarcarTodos(bool marcar)
    {
        _marcando = true;
        foreach (var f in _filas) f.Marcado = marcar;
        _marcando = false;
        AlMarcar();
    }

    /// <summary>Clic derecho en una fila sin marcar: se queda marcada solo ella (como en la maqueta).</summary>
    public void MarcarSolo(FilaActivoVM fila)
    {
        if (fila.Marcado) return;
        _marcando = true;
        foreach (var f in _filas) f.Marcado = ReferenceEquals(f, fila);
        _marcando = false;
        AlMarcar();
    }

    private FilaActivoVM? Unico(object? p) => p as FilaActivoVM ?? (Marcadas is { Count: 1 } m ? m[0] : null);

    private void Abrir(object? p, ApartadoFicha apartado)
    {
        if (Unico(p) is { } f) _modulo.AbrirFicha(f.Id, f.Propio ? apartado : ApartadoFicha.Datos);
    }

    private async Task MayorAsync()
    {
        try
        {
            var cuentas = await _modulo.Contexto.Servicio.CuentasDeActivosAsync(_modulo.Contexto.Trabajo, Marcadas.Select(f => f.Id).ToList());
            if (cuentas.Count == 0)
            {
                _modulo.Dialogos.Error("Sin subcuentas", "Los activos marcados no tienen subcuentas en el ejercicio de trabajo. Usa «Generar las subcuentas que le falten…» para crearlas.");
                return;
            }
            _modulo.Contexto.AbrirMayor?.Invoke(cuentas, []);
        }
        catch (Exception ex) { _modulo.Dialogos.Error("No he podido abrir el Mayor", $"{ex.Message} Vuelve a intentarlo."); }
    }

    private async Task GenerarAsync(IReadOnlyList<FilaActivoVM> filas, string titulo)
    {
        if (filas.Count == 0) return;
        if (!await _modulo.Contexto.Servicio.PuedeAsync()) { _modulo.SinPermiso(); return; }
        Ocupado = true;
        bool cambiado;
        try { cambiado = await _modulo.GenerarSubcuentasAsync(filas.Select(f => f.Id).ToList(), titulo); }
        finally { Ocupado = false; }
        if (cambiado) await CargarAsync();
    }
}
