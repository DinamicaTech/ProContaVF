using System.Windows.Input;
using ProConta.Activos.Financieros;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Activos.UI;

/// <summary>Una fila del listado con su casilla: un clic en la fila la marca o la desmarca.</summary>
public sealed class FilaActivoVM(ActivoListado datos, Action alMarcar, int ejercicio = 0) : ModeloVista
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
    public int Ejercicio => ejercicio;

    /// <summary>Grupo de la tabla: el tipo de activo (Parámetros › Tipos de activo).</summary>
    public string Grupo => Datos.Tipo.Length > 0 ? Datos.Tipo : "Sin tipo";
    // Los financieros no enseñan ingresos ni gastos contables: su resultado es lo ganado o perdido en el año (propietario 2026-11-27).
    public string Ingresos => Propio && !EsFinanciero ? Formatos.Importe(Datos.Ingresos) : "";
    public decimal IngresosOrden => EsFinanciero ? 0m : Datos.Ingresos;
    public string Gastos => Propio && !EsFinanciero ? Formatos.Importe(Datos.Gastos) : "";
    public decimal GastosOrden => EsFinanciero ? 0m : Datos.Gastos;

    /// <summary>
    /// Resultado del ejercicio: ingresos menos gastos sin la amortización; en un financiero, el resultado de sus ventas del año más la variación de su
    /// plusvalía latente (<see cref="PosicionFinanciera.ResultadoEjercicio"/>), en cuanto se ha leído su posición.
    /// </summary>
    public decimal ResultadoValor => EsFinanciero ? _posicion?.ResultadoEjercicio(ejercicio) ?? 0m : Datos.Resultado;
    public string Resultado => !Propio ? "" : EsFinanciero && _posicion is null ? "…" : Formatos.Importe(ResultadoValor);
    public decimal ResultadoOrden => ResultadoValor;
    public bool ResultadoNegativo => Propio && ResultadoValor < 0m;

    // ---------- rentabilidad (activos 2026-11-28) ----------
    private RentabilidadPeriodo? _periodo;

    /// <summary>Meses con los que se proyecta el resultado del físico: hasta su último mes con ingresos; el ejercicio terminado, entero.</summary>
    public int? MesesProyeccion => EsFinanciero || !Propio ? null : RentabilidadActivo.MesesTranscurridos(ejercicio, Datos.UltimoMesConIngresos, DateTime.Today);

    /// <summary>Físico: resultado entre coste. Financiero: la ponderada en el tiempo del año hasta la última cotización.</summary>
    public decimal? RentabilidadValor => !Propio ? null : EsFinanciero ? _periodo?.Rentabilidad : RentabilidadActivo.DelEjercicio(Datos.Resultado, Datos.Coste);

    /// <summary>Físico: × 12 / último mes con ingresos. Financiero: × 365 / días hasta la última cotización.</summary>
    public decimal? ProyectadaValor => !Propio ? null : EsFinanciero ? _periodo?.Proyectada : RentabilidadActivo.Proyectar(RentabilidadValor, MesesProyeccion);

    public string Rentabilidad => !Propio ? "" : EsFinanciero && _posicion is null ? "…" : TextosCartera.Pct(RentabilidadValor);
    public decimal RentabilidadOrden => RentabilidadValor ?? decimal.MinValue;
    public string Proyectada => !Propio ? "" : EsFinanciero && _posicion is null ? "…" : TextosCartera.Pct(ProyectadaValor);
    public decimal ProyectadaOrden => ProyectadaValor ?? decimal.MinValue;
    public string TonoRentabilidad => TextosCartera.Tono(RentabilidadValor);
    public string TonoProyectada => TextosCartera.Tono(ProyectadaValor);

    public string AyudaProyectada => !Propio ? "" : EsFinanciero ? AyudaProyectadaFinanciero
        : ProyectadaValor is null ? (RentabilidadValor is null ? "Sin coste de compra no hay rentabilidad" : $"Sin ingresos en {ejercicio}: no hay meses con los que proyectar")
        : MesesProyeccion == 12 ? "Ejercicio completo: la proyectada es la real"
        : $"Resultado de enero a {Mes(MesesProyeccion!.Value)} (último mes con ingresos) llevado a doce meses: × 12/{MesesProyeccion}";

    private string AyudaProyectadaFinanciero => _posicion is null ? "Leyendo su posición…"
        : _periodo is not { } p ? $"Sin cotización o sin participaciones en {ejercicio}: no hay rentabilidad del año"
        : $"Rentabilidad ponderada en el tiempo del {Formatos.Fecha(p.Desde)} al {Formatos.Fecha(p.Hasta)} (última cotización), sin contar compras ni ventas: "
          + $"{TextosCartera.Pct(p.Rentabilidad)} en {p.Dias} días; × 365/{p.Dias} = {TextosCartera.Pct(p.Proyectada)} al año";

    private static string Mes(int m) => Formatos.Cultura.DateTimeFormat.GetMonthName(m);

    public string Estado => Datos.SubcuentasQueFaltan > 1 ? $"Faltan {Datos.SubcuentasQueFaltan} subcuentas" : Datos.Estado;

    /// <summary>EN_USO, BAJA, AJENO o FALTA: el color de la pastilla del estado.</summary>
    public string ClaseEstado => Datos.EsAjeno ? "AJENO" : Datos.DeBaja ? "BAJA" : Datos.SubcuentasQueFaltan > 0 ? "FALTA" : "EN_USO";

    public string AyudaEstado => Datos.EsAjeno ? "No es de la sociedad: solo se guarda su información (sin subcuentas, centro ni amortización)."
        : Datos.DeBaja ? $"Inactivo desde el {Formatos.Fecha(Datos.FechaBaja!.Value)}: se conserva su información y su documentación."
        : Datos.SubcuentasQueFaltan > 0 ? $"Le {(Datos.SubcuentasQueFaltan == 1 ? "falta 1 subcuenta" : $"faltan {Datos.SubcuentasQueFaltan} subcuentas")} de la plantilla de su tipo en el ejercicio de trabajo. Clic derecho › «Generar las subcuentas que le falten…»."
        : "En uso: tiene todas las subcuentas de su plantilla.";

    public string AyudaFila => !Propio ? $"{Datos.Nombre}. Activo ajeno a la sociedad, solo informativo. Doble clic: abrir la ficha."
        : EsFinanciero ? $"{Datos.Nombre}. Resultado de {ejercicio}: {Resultado}, lo ganado o perdido en el año (resultado de las ventas del año más lo que ha subido o bajado su valor; las compras y las ventas no cuentan como ganancia). Doble clic: abrir la ficha."
        : $"{Datos.Nombre}. Ingresos {Ingresos} − gastos {Gastos} = {Resultado} (sin la amortización). Amortizado {Formatos.Importe(Datos.Amortizado)} de {Formatos.Importe(Datos.Amortizable)}. Doble clic: abrir la ficha.";

    public bool Marcado { get => _marcado; set { if (Fijar(ref _marcado, value)) alMarcar(); } }

    // ---------- activos financieros (filtro «Financieros», ergonomía de activos 2026-11-26) ----------
    private PosicionFinanciera? _posicion;

    public bool EsFinanciero => TiposActivoPgc.EsInversion(Datos.CodigoTipo);

    /// <summary>Su posición con la última cotización guardada (solo los financieros, cuando se ven con el filtro «Financieros»).</summary>
    public PosicionFinanciera? Posicion
    {
        get => _posicion;
        set
        {
            _posicion = value;
            _periodo = value?.RentabilidadEjercicio(ejercicio);
            Avisar(nameof(Rentabilidad), nameof(RentabilidadOrden), nameof(Proyectada), nameof(ProyectadaOrden), nameof(TonoRentabilidad), nameof(TonoProyectada),
                nameof(AyudaProyectada));
            Avisar(nameof(Participaciones), nameof(Cotizacion), nameof(Valor), nameof(Plusvalia), nameof(Porcentaje), nameof(Anual), nameof(Tono), nameof(TonoAnual),
                nameof(Resultado), nameof(ResultadoOrden), nameof(ResultadoNegativo), nameof(AyudaFila));
        }
    }

    public decimal ParticipacionesOrden => _posicion?.Participaciones ?? 0m;
    public string Participaciones => _posicion is { } p ? TextosCartera.Participaciones(p.Participaciones) : "";
    public decimal CotizacionOrden => _posicion?.Precio ?? -1m;
    public string Cotizacion => _posicion is null ? "" : _posicion.Precio is decimal c ? TextosCartera.Num(c, c < 20 ? 3 : 2) : "no cotiza";
    public string AyudaCotizacion => _posicion?.FechaPrecio is DateTime f ? $"Precio en euros del {Formatos.Fecha(f)} ({_posicion.OrigenPrecio}). Se actualiza al abrir la cartera o la ficha" : "Sin cotización: se valora a coste";
    public decimal ValorOrden => _posicion?.Valor ?? 0m;
    public string Valor => _posicion is { Viva: true } p ? Formatos.Importe(p.Valor) : "";
    public decimal PlusvaliaOrden => _posicion is { } p ? (p.Viva ? p.Plusvalia : p.ResultadoVentas) : 0m;
    public string Plusvalia => _posicion is null ? "" : _posicion.Viva ? TextosCartera.Signo(_posicion.Plusvalia) : $"vendido · {TextosCartera.Signo(_posicion.ResultadoVentas)}";
    public decimal PorcentajeOrden => _posicion?.PlusvaliaPorcentaje ?? decimal.MinValue;
    public string Porcentaje => _posicion is { Viva: true } p ? TextosCartera.Pct(p.PlusvaliaPorcentaje) : "";
    public decimal AnualOrden => _posicion?.RentabilidadAnual ?? decimal.MinValue;
    public string Anual => _posicion is { Viva: true } p ? TextosCartera.Pct(p.RentabilidadAnual) : "";
    public string Tono => TextosCartera.Tono(_posicion is null ? null : PlusvaliaOrden);
    public string TonoAnual => TextosCartera.Tono(_posicion?.RentabilidadAnual);
}

/// <summary>
/// Datos maestros › Activos (activos 2026-11-17 18:50): los activos de la sociedad de trabajo con sus cifras del ejercicio, búsqueda y filtros por clase,
/// estado y ajenos, fila de totales de los propios y selección múltiple con casilla y un clic. Acciones generales en la cabecera; el menú contextual,
/// solo sobre lo marcado; doble clic abre la ficha.
/// </summary>
public sealed class ListadoActivosVM : ModeloVista, IPestanaTrabajo
{
    public static readonly IReadOnlyList<string> Clases = ["Todos", "Inmuebles", "Vehículos", "Financieros", "Otros"];
    public static readonly IReadOnlyList<string> Estados = ["Activos", "Inactivos", "Todos"];

    private readonly ModuloActivos _modulo;
    private object? _vista;
    private IReadOnlyList<FilaActivoVM> _todas = [];
    private IReadOnlyList<FilaActivoVM> _filas = [];
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private string _busqueda = "";
    private string _clase = "Todos";
    private string _filtroEstado = "Activos";
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
        AbrirCommand = new ComandoDelegado(p => Abrir(p, null), p => Unico(p) is not null);
        CuentaResultadosCommand = new ComandoDelegado(p => Abrir(p, ApartadoFicha.CuentaResultados), p => Unico(p) is { Propio: true });
        MayorCommand = new ComandoDelegado(_ => _ = MayorAsync(), _ => _modulo.Contexto.AbrirMayor is not null && Marcadas.Count > 0 && Marcadas.All(f => f.Propio));
        GenerarMarcadosCommand = new ComandoDelegado(_ => _ = GenerarAsync(Marcadas, Marcadas.Count == 1 ? $"Subcuentas de {Marcadas[0].Codigo}" : $"Subcuentas de {Marcadas.Count} activos"),
            _ => !_ocupado && Marcadas.Count > 0 && Marcadas.All(f => f.Propio && !f.Datos.DeBaja));
        NuevaCompraCommand = new ComandoDelegado(p => { if (Unico(p) is { } f) _ = _modulo.NuevaCompraAsync(f.Id, f.Codigo); },
            p => !_ocupado && Unico(p) is { Propio: true, Datos.DeBaja: false } f && TiposActivoPgc.EsInversion(f.Datos.CodigoTipo));
        VenderCommand = new ComandoDelegado(p => { if (Unico(p) is { } f) _ = _modulo.VenderAsync(f.Id, f.Codigo); },
            p => !_ocupado && Unico(p) is { Propio: true, Datos.DeBaja: false } f && TiposActivoPgc.EsInversion(f.Datos.CodigoTipo));
        QuitarCentroCommand = new ComandoDelegado(() => FiltrarCentro(null, null));
        AnalisisFinancieroCommand = new ComandoDelegado(() => _modulo.AbrirCartera(), () => _modulo.Contexto.Cartera is not null);
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
    public ICommand NuevaCompraCommand { get; }
    public ICommand VenderCommand { get; }
    public ICommand QuitarCentroCommand { get; }
    public ICommand AnalisisFinancieroCommand { get; }
    public ICommand MarcarTodosCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_modulo.Contexto.RazonSocial} · ejercicio {_modulo.Contexto.Trabajo.Ejercicio}";
    public int Ejercicio => _modulo.Contexto.Trabajo.Ejercicio;
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    // ---------- filtros ----------
    public string Busqueda { get => _busqueda; set { if (Fijar(ref _busqueda, value)) Filtrar(); } }
    public string Clase
    {
        get => _clase;
        set
        {
            if (!Fijar(ref _clase, value ?? "Todos")) return;
            Avisar(nameof(EnFinancieros), nameof(EnGeneral));
            Filtrar();
        }
    }
    public string FiltroEstado { get => _filtroEstado; set { if (Fijar(ref _filtroEstado, value ?? "Activos")) Filtrar(); } }
    public bool MostrarAjenos { get => _ajenos; set { if (Fijar(ref _ajenos, value)) Filtrar(); } }

    /// <summary>Con el filtro «Financieros» la tabla enseña sus columnas propias: participaciones, valor, plusvalía y rentabilidad.</summary>
    public bool EnFinancieros => _clase == "Financieros";
    public bool EnGeneral => !EnFinancieros;
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

    /// <summary>El coste del inmovilizado y el de las inversiones financieras van por separado: no se suman pisos y ETF.</summary>
    public string TotalInmovilizado => Formatos.Importe(Propios.Where(f => !f.EsFinanciero).Sum(f => f.Datos.Coste));
    public string TotalInversiones => Formatos.Importe(Propios.Where(f => f.EsFinanciero).Sum(f => f.Datos.Coste));
    public bool HayInmovilizado => Propios.Any(f => !f.EsFinanciero);
    public bool HayInversiones => Propios.Any(f => f.EsFinanciero);
    public bool HayResultados => EnGeneral && HayTotales;

    // Totales del filtro «Financieros»: los vivos.
    private IEnumerable<FilaActivoVM> Vivos => Propios.Where(f => f.Posicion is { Viva: true });
    public string TotalValor => Formatos.Importe(Vivos.Sum(f => f.Posicion!.Valor));
    public string TotalPlusvalia
    {
        get
        {
            var coste = Vivos.Sum(f => f.Posicion!.Coste);
            var plus = Vivos.Sum(f => f.Posicion!.Plusvalia);
            return TextosCartera.Signo(plus) + " · " + TextosCartera.Pct(coste > 0m ? plus / coste : null);
        }
    }
    public string TonoTotalPlusvalia => TextosCartera.Tono(Vivos.Sum(f => f.Posicion!.Plusvalia));

    // ---------- menú contextual: solo lo que vale para lo marcado ----------
    public bool VerMenuCuenta => Marcadas.Count == 0 || Marcadas.All(f => !f.EsFinanciero);
    public bool VerMenuInversion => Marcadas is { Count: 1 } m && m[0].EsFinanciero;

    private bool _posicionesLeidas;

    /// <summary>Las posiciones de los activos financieros con la última cotización guardada (sin consultar a los proveedores).</summary>
    private async Task LeerPosicionesAsync()
    {
        if (_modulo.Contexto.Cartera is not { } cartera) return;
        _posicionesLeidas = true;
        try
        {
            var c = await cartera.CargarAsync(_modulo.Contexto.Trabajo.IdSociedad, conRed: false);
            var porId = c.Activos.ToDictionary(a => a.IdActivo, a => a.Posicion);
            foreach (var f in _todas) f.Posicion = porId.GetValueOrDefault(f.Id);
            Avisar(nameof(TotalValor), nameof(TotalPlusvalia), nameof(TonoTotalPlusvalia), nameof(TotalResultado), nameof(TotalNegativo));
            GruposCambiados?.Invoke();
        }
        catch (Exception ex)
        {
            _posicionesLeidas = false;
            _modulo.Contexto.Avisar($"No he podido leer el valor de los activos financieros: {ex.Message}");
        }
    }
    public string TotalIngresos => Formatos.Importe(Propios.Where(f => !f.EsFinanciero).Sum(f => f.Datos.Ingresos));
    public string TotalGastos => Formatos.Importe(Propios.Where(f => !f.EsFinanciero).Sum(f => f.Datos.Gastos));
    public string TotalResultado => Formatos.Importe(Propios.Sum(f => f.ResultadoValor));
    public bool TotalNegativo => Propios.Sum(f => f.ResultadoValor) < 0m;

    /// <summary>Las posiciones de los financieros ya están leídas: la vista vuelve a calcular los subtotales de cada grupo.</summary>
    public event Action? GruposCambiados;

    /// <summary>Resumen de un grupo (un tipo de activo) para su cabecera: cuántos, coste y resultado del ejercicio de los propios.</summary>
    public static string ResumenGrupo(IEnumerable<FilaActivoVM> filas)
    {
        var lista = filas.ToList();
        var propios = lista.Where(f => f.Propio).ToList();
        var texto = Formatos.Plural(lista.Count, "activo", "activos");
        if (propios.Count == 0) return texto;
        var resumen = $"{texto} · coste {Formatos.Importe(propios.Sum(f => f.Datos.Coste))} · resultado {propios[0].Ejercicio} {Formatos.Importe(propios.Sum(f => f.ResultadoValor))}";
        var fisicos = propios.Where(f => !f.EsFinanciero).ToList();
        if (fisicos.Count > 0)
        {
            var (real, proyectada) = RentabilidadActivo.DeGrupo(fisicos.Select(f => (f.Datos.Resultado, f.Datos.Coste, f.MesesProyeccion)));
            if (real is not null) resumen += $" · rentabilidad {TextosCartera.Pct(real)} (proyectada {TextosCartera.Pct(proyectada)})";
        }
        var posiciones = propios.Where(f => f.EsFinanciero && f.Posicion is not null).Select(f => f.Posicion!).ToList();
        if (posiciones.Count > 0 && RentabilidadPeriodo.De(posiciones, propios[0].Ejercicio) is { } p)
            resumen += $" · rentabilidad {TextosCartera.Pct(p.Rentabilidad)} (proyectada {TextosCartera.Pct(p.Proyectada)})";
        return resumen;
    }

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
            _todas = lista.Select(a => new FilaActivoVM(a, AlMarcar, Ejercicio) { Marcado = marcados.Contains(a.IdActivo) }).ToList();
            _posicionesLeidas = false;
            Filtrar();
            if (_todas.Any(f => f.EsFinanciero)) _ = LeerPosicionesAsync();
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
                && (_filtroEstado == "Todos" || (_filtroEstado == "Inactivos") == f.Datos.DeBaja)
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
            nameof(SinResultados), nameof(TotalInmovilizado), nameof(TotalInversiones), nameof(HayInmovilizado), nameof(HayInversiones), nameof(HayResultados),
            nameof(TotalValor), nameof(TotalPlusvalia), nameof(TonoTotalPlusvalia));
    }

    /// <summary>Hay activos pero ninguno cumple los filtros.</summary>
    public bool SinResultados => _todas.Count > 0 && _filas.Count == 0;

    private void AlMarcar()
    {
        if (_marcando) return;
        Avisar(nameof(Recuento), nameof(TodosMarcados), nameof(VerMenuCuenta), nameof(VerMenuInversion));
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

    private void Abrir(object? p, ApartadoFicha? apartado)
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
