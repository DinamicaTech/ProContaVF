using System.Windows.Input;
using ProConta.Activos.Financieros;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Graficos;
using ProConta.UI.Base.Modelos;

namespace ProConta.Activos.UI;

/// <summary>Textos de la cartera: importes con signo, porcentajes y tono (verde si gana, rojo si pierde).</summary>
public static class TextosCartera
{
    /// <summary>Colores fijos de los activos en la comparativa y en su casilla (la paleta de gráficos y cinco más).</summary>
    public static readonly IReadOnlyList<string> Colores =
        [PaletaGraficos.Serie1, PaletaGraficos.Serie2, PaletaGraficos.Serie3, PaletaGraficos.Serie4, PaletaGraficos.Serie5, "#6a4fc9", "#0f8b8d", "#b5532f", "#5d7a1f", "#8a5a9e"];

    public static string Color(int i) => Colores[((i % Colores.Count) + Colores.Count) % Colores.Count];

    /// <summary>+1.234,56 € o −1.234,56 €.</summary>
    public static string Signo(decimal v) => (v > 0 ? "+" : "") + Formatos.Importe(v);

    /// <summary>Tanto por uno en porcentaje con signo y un decimal: +12,3 %; «—» sin valor.</summary>
    public static string Pct(decimal? v, int decimales = 1) => v is not decimal d ? "—"
        : (d > 0 ? "+" : d < 0 ? Formatos.Menos : "") + Math.Abs(Math.Round(d * 100m, decimales, MidpointRounding.AwayFromZero)).ToString("N" + decimales, Formatos.Cultura) + " %";

    /// <summary>Número con los decimales pedidos y separador de miles.</summary>
    public static string Num(decimal v, int decimales) => (v < 0 ? Formatos.Menos : "") + Math.Abs(Math.Round(v, decimales, MidpointRounding.AwayFromZero)).ToString("N" + decimales, Formatos.Cultura);

    /// <summary>Participaciones: sin decimales si son enteras; si no, con los que tengan (hasta 6).</summary>
    public static string Participaciones(decimal v) => v == Math.Truncate(v) ? Num(v, 0) : Num(v, Math.Min(6, (int)(v / 1.000000000000000000000000000000000m).Scale));

    /// <summary>«Bueno», «Malo» o «» según el signo (para colorear).</summary>
    public static string Tono(decimal? v) => v is > 0m ? "Bueno" : v is < 0m ? "Malo" : "";

    /// <summary>«3 años y 2 meses» en cartera.</summary>
    public static string Tiempo(DateTime desde, DateTime hasta)
    {
        var meses = (hasta.Year - desde.Year) * 12 + hasta.Month - desde.Month - (hasta.Day < desde.Day ? 1 : 0);
        meses = Math.Max(0, meses);
        int a = meses / 12, m = meses % 12;
        var ta = a == 0 ? "" : a == 1 ? "1 año" : $"{a} años";
        var tm = m == 0 ? "" : m == 1 ? "1 mes" : $"{m} meses";
        return a == 0 && m == 0 ? $"{Math.Max(0, (hasta - desde).Days)} días" : ta.Length > 0 && tm.Length > 0 ? $"{ta} y {tm}" : ta + tm;
    }

    /// <summary>ETF, Fondo, Acción o Participaciones (sin cotización) según el tipo del instrumento.</summary>
    public static string Clase(PosicionFinanciera p) => !p.TieneCotizacion && p.Historico.Count == 0 && string.IsNullOrEmpty(p.Instrumento.TickerYahoo) && string.IsNullOrEmpty(p.Instrumento.TickerEodhd)
        ? "Participaciones"
        : p.Instrumento.TipoInstrumento.ToUpperInvariant() switch { "ETF" => "ETF", "FONDO" => "Fondo", "ACCION" => "Acción", _ => "Otro" };
}

/// <summary>Una cifra del resumen: título, valor, nota y ayuda.</summary>
public sealed record CifraCartera(string Titulo, string Valor, string Nota, string Ayuda, string Tono = "");

/// <summary>Un elemento de la leyenda de la comparativa (con su variación al final del periodo).</summary>
public sealed record LeyendaComparativa(int Serie, string Codigo, string Nombre, string Color, string Variacion, string Tono);

/// <summary>
/// Una fila de la tabla «Rentabilidad por activo»: un activo (con sus lotes sumados) o, si está desplegado, uno de sus lotes vivos debajo.
/// </summary>
public sealed class FilaCarteraVM : ModeloVista
{
    private readonly CarteraFinancieraVM _cartera;
    private bool _marcado = true;
    private bool _abierto;

    internal FilaCarteraVM(CarteraFinancieraVM cartera, ActivoDeCartera activo, int indiceColor, decimal valorTotal)
    {
        _cartera = cartera;
        Activo = activo;
        Color = TextosCartera.Color(indiceColor);
        Peso = valorTotal > 0m && P.Viva ? TextosCartera.Num(P.Valor / valorTotal * 100m, 1) + " %" : "";
    }

    internal FilaCarteraVM(CarteraFinancieraVM cartera, ActivoDeCartera activo, LoteInversion lote, int numeroVivo, string color)
    {
        _cartera = cartera;
        Activo = activo;
        Lote = lote;
        Color = color;
        NumeroLote = numeroVivo;
        Peso = "";
    }

    public ActivoDeCartera Activo { get; }
    private PosicionFinanciera P => Activo.Posicion;
    public LoteInversion? Lote { get; }
    public int NumeroLote { get; }
    public bool EsLote => Lote is not null;
    public bool EsActivo => Lote is null;
    public string Color { get; }

    /// <summary>Marcado: se ve en los gráficos (solo los que cotizan).</summary>
    public bool Marcado { get => _marcado; set { if (Fijar(ref _marcado, value) && EsActivo) _cartera.MarcasCambiadas(); } }
    public bool PuedeMarcarse => EsActivo && P.Historico.Count > 0;
    public string AyudaMarca => !PuedeMarcarse ? "No cotiza: no entra en los gráficos" : P.Viva ? "Marcado: se ve en los gráficos de arriba" : "Marcado: se ve en los gráficos mientras se tuvo";

    public bool Abierto { get => _abierto; set { if (Fijar(ref _abierto, value)) { Avisar(nameof(Flecha)); _cartera.Desplegar(this); } } }
    public bool Desplegable => EsActivo && P.Viva && P.LotesVivos > 1;
    public string Flecha => Abierto ? "▾" : "▸";
    public string AyudaFlecha => Abierto ? "Ocultar sus lotes" : $"Ver sus {P.LotesVivos} lotes";

    public bool Vendido => EsActivo && !P.Viva;
    public string Codigo => Activo.Codigo;
    public string Nombre => EsLote
        ? $"Lote {NumeroLote} · comprado el {Formatos.Fecha(Lote!.Fecha)}" + (Lote.Vivas < Lote.Compradas ? $" (quedan {TextosCartera.Participaciones(Lote.Vivas)} de {TextosCartera.Participaciones(Lote.Compradas)})" : "")
        : P.Instrumento.Nombre.Length > 0 ? P.Instrumento.Nombre : Activo.Nombre;
    public string Detalle => EsLote ? $"{TextosCartera.Tiempo(Lote!.Fecha, P.Hoy)} en cartera" : $"{Activo.Codigo} · {(P.Instrumento.Isin.Length > 0 ? P.Instrumento.Isin : "sin ISIN")} · {P.Instrumento.TickerYahoo ?? P.Instrumento.TickerEodhd ?? "sin símbolo"}";
    public string Clase => EsLote ? "" : TextosCartera.Clase(P);

    public string Lotes => EsActivo && P.Viva ? P.LotesVivos.ToString(Formatos.Cultura) : "";
    public string Participaciones => EsLote ? TextosCartera.Participaciones(Lote!.Vivas) : TextosCartera.Participaciones(P.Participaciones);
    public string PrecioMedio => EsLote ? TextosCartera.Num(Lote!.CosteUnitario, 3) : P.PrecioMedio is decimal pm ? TextosCartera.Num(pm, 3) : "";
    public decimal CosteValor => EsLote ? Lote!.CosteVivo : P.Coste;
    public string Coste => Vendido ? "" : Formatos.Numero(CosteValor);
    public string Cotizacion => EsLote ? "" : P.Precio is decimal p ? TextosCartera.Num(p, p < 20 ? 3 : 2) : "no cotiza";
    public string AyudaCotizacion => EsLote ? "" : P.Precio is null ? "Sin cotización: se valora a coste"
        : $"Precio en euros del {Formatos.Fecha(P.FechaPrecio!.Value)} ({P.OrigenPrecio})" + (P.Instrumento.Moneda.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? "" : $", pasado de {P.Instrumento.Moneda} con el cambio del BCE");
    public decimal ValorNum => EsLote ? P.ValorLote(Lote!) : P.Valor;
    public string Valor => Vendido ? "" : Formatos.Numero(ValorNum);
    public decimal PlusvaliaNum => Vendido ? P.ResultadoVentas : ValorNum - CosteValor;
    public string Plusvalia => Vendido ? $"vendido · resultado {TextosCartera.Signo(P.ResultadoVentas)}" : (PlusvaliaNum > 0 ? "+" : "") + Formatos.Numero(PlusvaliaNum);
    public string Porcentaje => Vendido ? "" : TextosCartera.Pct(CosteValor > 0m ? PlusvaliaNum / CosteValor : null);
    public decimal? AnualNum => EsLote ? P.RentabilidadAnualLote(Lote!) : P.RentabilidadAnual;
    public string Anual => Vendido ? "" : TextosCartera.Pct(AnualNum);
    public string AyudaAnual => AnualNum is null
        ? (P.TieneCotizacion ? "Menos de un año desde la compra: la rentabilidad anual aún no es representativa" : "Sin cotización no hay rentabilidad")
        : "Rentabilidad media por año (TIR), teniendo en cuenta cuándo entró y salió cada euro";
    public string Peso { get; }
    public string Tono => TextosCartera.Tono(PlusvaliaNum);
    public string TonoAnual => TextosCartera.Tono(AnualNum);
    public string AyudaFila => EsLote ? "Lote: cada compra es un lote; cada venta reduce todos los lotes en la misma proporción (coste medio ponderado)"
        : $"{Nombre}. Clic: marcar o desmarcar para los gráficos · Doble clic: abrir su ficha (cotización, lotes, operaciones y proyección)";
}

/// <summary>
/// Análisis de los activos financieros (activos/activos-financieros, maqueta v4 validada 2026-11-23 20:11): cifras de la cartera, revalorización sumando
/// cada lote desde su compra, comparativa de la variación relativa de la cotización, rentabilidad por activo con sus lotes plegados y el análisis de cada
/// activo. Solo consulta: no graba nada salvo las cotizaciones que trae.
/// </summary>
public sealed class CarteraFinancieraVM : ModeloVista, IPestanaTrabajo
{
    public static readonly IReadOnlyList<string> PeriodosRevalorizacion = ["1A", "3A", "5A", "Máx."];
    public static readonly IReadOnlyList<string> PeriodosComparativa = ["1M", "3M", "6M", "Año", "1A", "3A", "5A"];
    public static readonly IReadOnlyList<string> Tipos = ["Todos", "ETF", "Fondos", "Otros"];
    public static readonly IReadOnlyList<string> Estados = ["Activos", "Inactivos", "Todos"];

    private readonly ModuloActivos _modulo;
    private object? _vista;
    private CarteraCargada? _cartera;
    private List<FilaCarteraVM> _activos = [];
    private IReadOnlyList<FilaCarteraVM> _filas = [];
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _ocupado;
    private string _estadoCotizaciones = "";
    private IReadOnlyList<string> _avisos = [];
    private IReadOnlyList<CifraCartera> _cifras = [];
    private int _pestanaGrafico;
    private bool _modoPorcentaje;
    private string _periodo = "Máx.";
    private string _periodoComparativa = "1A";
    private string _tipo = "Todos";
    private string _filtroEstado = "Activos";
    private DatosTemporales? _datosInvertido;
    private DatosTemporales? _datosRevalorizacion;
    private DatosTemporales? _datosComparativa;
    private IReadOnlyList<LeyendaComparativa> _leyenda = [];
    private int _serieResaltada = -1;
    private string _alcance = "";
    private string _orden = "";
    private bool _descendente;
    private bool _marcando;
    private CancellationTokenSource? _cancelacion;

    internal CarteraFinancieraVM(ModuloActivos modulo)
    {
        _modulo = modulo;
        ActualizarCommand = new ComandoDelegado(() => _ = CargarAsync(conRed: true), () => !_ocupado && _modulo.Contexto.Cartera is not null);
        AbrirFichaCommand = new ComandoDelegado(p => { if (p is FilaCarteraVM f) _modulo.AbrirFicha(f.Activo.IdActivo, f.PuedeMarcarse ? ApartadoFicha.Cotizacion : ApartadoFicha.Lotes); });
        NuevaCompraCommand = new ComandoDelegado(p => { if (p is FilaCarteraVM { EsActivo: true } f) _ = _modulo.NuevaCompraAsync(f.Activo.IdActivo, f.Codigo); },
            p => !_ocupado && p is FilaCarteraVM { EsActivo: true } f && f.Activo.FechaBaja is null);
        VenderCommand = new ComandoDelegado(p => { if (p is FilaCarteraVM { EsActivo: true } f) _ = _modulo.VenderAsync(f.Activo.IdActivo, f.Codigo); },
            p => !_ocupado && p is FilaCarteraVM { EsActivo: true, Vendido: false } f && f.Activo.FechaBaja is null);
        NuevoActivoCommand = new ComandoDelegado(() => _ = _modulo.NuevoAsync(financiero: true), () => !_ocupado);
        AbrirListadoCommand = new ComandoDelegado(() => _modulo.AbrirListado());
    }

    // ---------- pestaña ----------
    public string Titulo => "Activos financieros";
    public string Ayuda => $"Análisis de los activos financieros de {_modulo.Contexto.RazonSocial}";
    public string Icono => "coin";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaCarteraFinanciera { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar()
    {
        _cancelacion?.Cancel();
        _modulo.CarteraCerrada();
    }

    // ---------- comandos ----------
    public ICommand ActualizarCommand { get; }
    public ICommand AbrirFichaCommand { get; }
    public ICommand NuevaCompraCommand { get; }
    public ICommand VenderCommand { get; }
    public ICommand NuevoActivoCommand { get; }
    public ICommand AbrirListadoCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_modulo.Contexto.RazonSocial} · ETFs, fondos y participaciones";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    /// <summary>De cuándo y de dónde son las cotizaciones, o que se están actualizando.</summary>
    public string EstadoCotizaciones { get => _estadoCotizaciones; private set => Fijar(ref _estadoCotizaciones, value); }

    public IReadOnlyList<string> Avisos { get => _avisos; private set { _avisos = value; Avisar(nameof(Avisos), nameof(HayAvisos), nameof(TextoAvisos)); } }
    public bool HayAvisos => _avisos.Count > 0;
    public string TextoAvisos => _avisos.Count == 1 ? _avisos[0] : $"{_avisos.Count} avisos al actualizar las cotizaciones (pasa el ratón para verlos).";
    public string AyudaAvisos => string.Join(Environment.NewLine, _avisos);

    public IReadOnlyList<CifraCartera> Cifras { get => _cifras; private set => Fijar(ref _cifras, value); }

    // ---------- gráficos ----------
    public int PestanaGrafico { get => _pestanaGrafico; set { if (Fijar(ref _pestanaGrafico, value)) PintarGraficos(); } }
    public bool ModoPorcentaje { get => _modoPorcentaje; set { if (Fijar(ref _modoPorcentaje, value)) { Avisar(nameof(ModoEuros), nameof(FormatoEjeRevalorizacion)); PintarRevalorizacion(); } } }
    public bool ModoEuros { get => !_modoPorcentaje; set => ModoPorcentaje = !value; }
    public string Periodo { get => _periodo; set { if (Fijar(ref _periodo, value ?? "Máx.")) PintarGraficos(); } }
    public string PeriodoComparativa { get => _periodoComparativa; set { if (Fijar(ref _periodoComparativa, value ?? "1A")) PintarComparativa(); } }
    public DatosTemporales? DatosRevalorizacion { get => _datosRevalorizacion; private set => Fijar(ref _datosRevalorizacion, value); }
    public DatosTemporales? DatosComparativa { get => _datosComparativa; private set => Fijar(ref _datosComparativa, value); }
    public DatosTemporales? DatosInvertido { get => _datosInvertido; private set => Fijar(ref _datosInvertido, value); }
    public IReadOnlyList<LeyendaComparativa> Leyenda { get => _leyenda; private set => Fijar(ref _leyenda, value); }
    public int SerieResaltada { get => _serieResaltada; set => Fijar(ref _serieResaltada, value); }
    public string Alcance { get => _alcance; private set => Fijar(ref _alcance, value); }
    public Func<double, string> FormatoEjeRevalorizacion => _modoPorcentaje ? v => TextosCartera.Num((decimal)v, Math.Abs(v) < 10 && v % 1 != 0 ? 1 : 0) + " %" : EjeEuros;
    public Func<double, string> FormatoEjeComparativa { get; } = v => TextosCartera.Num((decimal)v, Math.Abs(v) < 10 && Math.Abs(v % 1) > 1e-9 ? 1 : 0) + " %";
    public Func<double, string> FormatoEjeInvertido { get; } = EjeEuros;
    private static string EjeEuros(double v) => FormatoGrafico.Compacto(v) + " €";

    // ---------- tabla ----------
    public string Tipo { get => _tipo; set { if (Fijar(ref _tipo, value ?? "Todos")) Filtrar(); } }
    /// <summary>Activos (sin fecha de baja, por defecto), Inactivos (vendidos del todo o dados de baja) o Todos.</summary>
    public string FiltroEstado { get => _filtroEstado; set { if (Fijar(ref _filtroEstado, value ?? "Activos")) Filtrar(); } }
    public IReadOnlyList<FilaCarteraVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas)); } }
    private IEnumerable<FilaCarteraVM> Visibles => _filas.Where(f => f.EsActivo);
    public bool TodosMarcados { get => Visibles.Where(f => f.PuedeMarcarse).All(f => f.Marcado); set => MarcarTodos(value); }
    private IEnumerable<FilaCarteraVM> VivosVisibles => Visibles.Where(f => !f.Vendido);
    public string TextoTotales => $"Total de {Formatos.Plural(VivosVisibles.Count(), "activo", "activos")} · {VivosVisibles.Sum(f => f.Activo.Posicion.LotesVivos)} lotes";
    public string TotalCoste => Formatos.Importe(VivosVisibles.Sum(f => f.CosteValor));
    public string TotalValor => Formatos.Importe(VivosVisibles.Sum(f => f.ValorNum));
    public decimal TotalPlusvaliaNum => VivosVisibles.Sum(f => f.ValorNum - f.CosteValor);
    public string TotalPlusvalia => TextosCartera.Signo(TotalPlusvaliaNum) + " · " + TextosCartera.Pct(VivosVisibles.Sum(f => f.CosteValor) is > 0m and var c ? TotalPlusvaliaNum / c : null);
    public string TonoTotal => TextosCartera.Tono(TotalPlusvaliaNum);
    public bool SinResultados => _activos.Count > 0 && !Visibles.Any();

    // ---------- carga ----------

    /// <summary>Primero lo guardado (rápido); después, si hay servicio, las cotizaciones al día.</summary>
    public async Task CargarAsync(bool conRed = true)
    {
        if (Ocupado) return;
        var servicio = _modulo.Contexto.Cartera;
        if (servicio is null)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = "El análisis de activos financieros no está disponible en esta instalación (falta el servicio de cotizaciones).";
            return;
        }
        _cancelacion?.Cancel();
        _cancelacion = new CancellationTokenSource();
        var ct = _cancelacion.Token;
        try
        {
            Ocupado = true;
            if (!await _modulo.Contexto.Servicio.PuedeAsync())
            {
                Estado = EstadoVista.SinPermiso;
                MensajeEstado = "No tienes permiso para ver los activos. Pídeselo a quien administra los permisos (acción «Activos»).";
                return;
            }
            if (_cartera is null)
            {
                Estado = EstadoVista.Cargando;
                Mostrar(await servicio.CargarAsync(_modulo.Contexto.Trabajo.IdSociedad, conRed: false, cancelacion: ct));
            }
            if (!conRed) return;
            EstadoCotizaciones = "Actualizando las cotizaciones…";
            var progreso = new Progress<string>(t => EstadoCotizaciones = "Actualizando las cotizaciones: " + t);
            Mostrar(await servicio.CargarAsync(_modulo.Contexto.Trabajo.IdSociedad, conRed: true, progreso, ct));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_cartera is null)
            {
                Estado = EstadoVista.Error;
                MensajeEstado = $"No he podido leer los activos financieros: {ex.Message} Pulsa «Actualizar cotizaciones» para volver a intentarlo.";
            }
            else Avisos = [$"No he podido actualizar las cotizaciones: {ex.Message} Se ven las guardadas; vuelve a pulsar «Actualizar cotizaciones»."];
            EstadoCotizaciones = "";
        }
        finally { Ocupado = false; }
    }

    private void Mostrar(CarteraCargada c)
    {
        var marcados = _activos.Count == 0 ? null : _activos.Where(f => !f.Marcado).Select(f => f.Activo.IdActivo).ToHashSet();
        var abiertos = _activos.Where(f => f.Abierto).Select(f => f.Activo.IdActivo).ToHashSet();
        _cartera = c;
        var total = c.Activos.Where(a => a.Posicion.Viva).Sum(a => a.Posicion.Valor);
        _marcando = true;
        _activos = c.Activos.Select((a, i) => new FilaCarteraVM(this, a, i, total)).ToList();
        foreach (var f in _activos)
        {
            f.Marcado = f.PuedeMarcarse && (marcados is null || !marcados.Contains(f.Activo.IdActivo));
            if (abiertos.Contains(f.Activo.IdActivo) && f.Desplegable) f.Abierto = true;
        }
        _marcando = false;
        Estado = c.Activos.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        MensajeEstado = c.Activos.Count == 0
            ? "Esta sociedad aún no tiene activos financieros. Dalos de alta en Datos maestros › Activos con «Nuevo activo…» y el tipo «Inversión financiera» (pide el ISIN)."
            : "";
        Avisos = c.Avisos;
        EstadoCotizaciones = c.ConRed ? $"Cotizaciones actualizadas a las {DateTime.Now:HH:mm}" : "Cotizaciones guardadas; actualizando…";
        PintarCifras();
        Filtrar();
    }

    private void PintarCifras()
    {
        if (_cartera is null) return;
        var vivos = _cartera.Activos.Where(a => a.Posicion.Viva).Select(a => a.Posicion).ToList();
        var coste = vivos.Sum(p => p.Coste);
        var valor = vivos.Sum(p => p.Valor);
        var plus = valor - coste;
        var ejercicio = _modulo.Contexto.Trabajo.Ejercicio;
        var rEj = _cartera.Activos.Sum(a => a.Posicion.ResultadoVentasEjercicio(ejercicio));
        var rTot = _cartera.Activos.Sum(a => a.Posicion.ResultadoVentas);
        var flujos = _cartera.Activos.SelectMany(a => a.Posicion.Flujos).ToList();
        var tir = Tir.Calcular(flujos);
        var fechas = vivos.Where(p => p.FechaPrecio is not null).Select(p => p.FechaPrecio!.Value).ToList();
        var notaValor = fechas.Count == 0 ? "sin cotizaciones" : fechas.Min() == fechas.Max() ? $"cotizaciones del {Formatos.Fecha(fechas.Max())}" : $"cotizaciones del {Formatos.Fecha(fechas.Min())} al {Formatos.Fecha(fechas.Max())}";
        Cifras =
        [
            new("Coste de la cartera", Formatos.Importe(coste), $"{Formatos.Plural(vivos.Count, "activo", "activos")} · {vivos.Sum(p => p.LotesVivos)} lotes vivos",
                "Lo que costó lo que hay hoy en cartera, comisiones incluidas"),
            new("Valor de mercado", Formatos.Importe(valor), notaValor, "Participaciones por su última cotización en euros. Las que no cotizan, a coste"),
            new("Plusvalía latente", TextosCartera.Signo(plus), $"{TextosCartera.Pct(coste > 0m ? plus / coste : null)} sobre el coste",
                "Lo que se ganaría si se vendiera todo hoy, antes de impuestos y comisiones de venta", TextosCartera.Tono(plus)),
            new("Resultado de ventas", TextosCartera.Signo(rEj), $"en {ejercicio} · desde el inicio {TextosCartera.Signo(rTot)}",
                "Beneficio o pérdida de lo vendido: importe cobrado menos el coste medio ponderado de lo vendido. Al conciliar la venta con el banco va a 766 o 666", TextosCartera.Tono(rEj)),
            new("Rentabilidad anual", TextosCartera.Pct(tir), tir is null ? "con menos de un año o sin cotización no se calcula" : "media desde la primera compra (TIR)",
                "Lo que ha rendido cada año, de media, el dinero invertido, teniendo en cuenta cuándo entró y salió cada euro", TextosCartera.Tono(tir)),
        ];
    }

    private void Filtrar()
    {
        var abiertos = _activos.Where(f => f.Abierto).ToHashSet();
        var filas = new List<FilaCarteraVM>();
        foreach (var f in Ordenadas(_activos).Where(f => (_filtroEstado == "Todos" || (_filtroEstado == "Inactivos") == (f.Activo.FechaBaja is not null)) && (_tipo == "Todos" || f.Clase == _tipo.TrimEnd('s') || (_tipo == "Otros" && f.Clase is not ("ETF" or "Fondo")))))
        {
            filas.Add(f);
            if (abiertos.Contains(f))
                filas.AddRange(f.Activo.Posicion.Lotes.Where(l => l.Vivas > 0m).Select((l, i) => new FilaCarteraVM(this, f.Activo, l, i + 1, f.Color)));
        }
        Filas = filas;
        Avisar(nameof(TextoTotales), nameof(TotalCoste), nameof(TotalValor), nameof(TotalPlusvalia), nameof(TonoTotal), nameof(TodosMarcados), nameof(SinResultados));
        PintarGraficos();
    }

    internal void Desplegar(FilaCarteraVM fila)
    {
        if (!_marcando) Filtrar();
    }

    internal void MarcasCambiadas()
    {
        if (_marcando) return;
        Avisar(nameof(TodosMarcados));
        PintarGraficos();
    }

    private void MarcarTodos(bool marcar)
    {
        _marcando = true;
        foreach (var f in Visibles.Where(f => f.PuedeMarcarse)) f.Marcado = marcar;
        _marcando = false;
        MarcasCambiadas();
    }

    private IReadOnlyList<FilaCarteraVM> EnGrafico => Visibles.Where(f => f.Marcado && f.PuedeMarcarse).ToList();

    private void PintarGraficos()
    {
        switch (_pestanaGrafico)
        {
            case 0: PintarRevalorizacion(); break;
            case 1: PintarComparativa(); break;
            default: PintarInvertido(); break;
        }
    }

    private static DateTime Restar(DateTime hoy, string periodo) => periodo switch
    {
        "1M" => hoy.AddMonths(-1),
        "3M" => hoy.AddMonths(-3),
        "6M" => hoy.AddMonths(-6),
        "Año" => new DateTime(hoy.Year, 1, 1),
        "1A" => hoy.AddYears(-1),
        "3A" => hoy.AddYears(-3),
        "5A" => hoy.AddYears(-5),
        _ => DateTime.MinValue,
    };

    /// <summary>Días, puntos y compras y ventas del periodo elegido para los activos marcados (null si no hay nada que dibujar).</summary>
    private (IReadOnlyList<DateTime> Dias, IReadOnlyList<PuntoCartera> Puntos, Func<int, ContenidoTooltip> Tip, IReadOnlyList<MarcaTemporal> Marcas)? SerieCartera()
    {
        if (_cartera is null) return null;
        var filas = EnGrafico;
        var cotizables = Visibles.Count(f => f.PuedeMarcarse);
        Alcance = filas.Count == 0 ? "Marca algún activo en la tabla para verlo aquí"
            : filas.Count == cotizables ? "Todos los activos cotizados que se ven en la tabla, mientras se tuvieron"
            : $"{Formatos.Plural(filas.Count, "activo marcado", "activos marcados")}";
        if (filas.Count == 0) return null;
        var hoy = _cartera.Hoy;
        var posiciones = filas.Select(f => f.Activo.Posicion).ToList();
        var ops = posiciones.SelectMany(p => p.Inversion.Operaciones.Where(o => o.EsCompra || o.EsVenta).Select(o => (p, o))).ToList();
        if (ops.Count == 0) return null;
        var primera = ops.Min(x => x.o.Fecha);
        var desde = Restar(hoy, _periodo) is var r && r > primera ? r : primera;
        var dias = SeriesCartera.Dias(desde, hoy, SeriesCartera.Paso(desde, hoy));
        var puntos = SeriesCartera.Revalorizacion(posiciones, dias);
        var marcas = ops.Where(x => x.o.Fecha >= desde && x.o.Fecha <= hoy)
            .Select(x => (x.p, x.o, Indice: IndiceDe(dias, x.o.Fecha))).Where(x => x.Indice >= 0).ToList();
        ContenidoTooltip Tip(int i)
        {
            var p = puntos[i];
            var filasT = new List<FilaTooltip>
            {
                new("Valor de mercado", Formatos.Importe(p.Valor), PaletaGraficos.Serie1),
                new("Invertido", Formatos.Importe(p.Invertido), PaletaGraficos.Apagado),
                new("Plusvalía", $"{TextosCartera.Signo(p.Plusvalia)} ({TextosCartera.Pct(p.PlusvaliaPorcentaje)})", null, true),
            };
            foreach (var m in marcas.Where(m => m.Indice == i))
                filasT.Add(new FilaTooltip($"{(m.o.EsCompra ? "▲ Compra" : "▼ Venta")} {m.p.Codigo}",
                    $"{TextosCartera.Participaciones(m.o.Participaciones)} part. · {Formatos.Importe(m.o.ImporteTotalEur)}"));
            return new ContenidoTooltip(p.Fecha.ToString("d 'de' MMMM 'de' yyyy", Formatos.Cultura), filasT);
        }
        return (dias, puntos, Tip, marcas.Select(m => new MarcaTemporal(m.Indice, m.o.EsCompra)).ToList());
    }

    /// <summary>
    /// Revalorización: en euros, solo el valor de mercado (sin la línea del capital invertido, que salta con cada compra; activos/activos-financieros
    /// 2026-11-24 09:14); en porcentaje, la plusvalía sobre lo invertido cada día.
    /// </summary>
    private void PintarRevalorizacion()
    {
        if (_pestanaGrafico != 0) return;
        if (SerieCartera() is not { } c) { DatosRevalorizacion = null; return; }
        DatosRevalorizacion = _modoPorcentaje
            ? new DatosTemporales(c.Dias, [new SerieTemporal("Plusvalía sobre lo invertido", PaletaGraficos.Serie1,
                    c.Puntos.Select(p => p.PlusvaliaPorcentaje is decimal x ? (double?)(double)(x * 100m) : null).ToList())],
                c.Tip, Marcas: c.Marcas, IncluirCero: true, LineaCero: true, Descripcion: "Plusvalía de la cartera en porcentaje sobre lo invertido cada día")
            : new DatosTemporales(c.Dias, [new SerieTemporal("Valor de mercado", PaletaGraficos.Serie1, c.Puntos.Select(p => (double?)(double)p.Valor).ToList())],
                c.Tip, Marcas: c.Marcas, Descripcion: "Valor de mercado de la cartera cada día");
    }

    /// <summary>Invertido y valor: el capital invertido en escalones frente al valor total (invertido más plusvalía), con la plusvalía sombreada.</summary>
    private void PintarInvertido()
    {
        if (_pestanaGrafico != 2) return;
        if (SerieCartera() is not { } c) { DatosInvertido = null; return; }
        DatosInvertido = new DatosTemporales(c.Dias,
            [new SerieTemporal("Valor total", PaletaGraficos.Serie1, c.Puntos.Select(p => (double?)(double)p.Valor).ToList()),
             new SerieTemporal("Total invertido", PaletaGraficos.Apagado, c.Puntos.Select(p => (double?)(double)p.Invertido).ToList(), Escalon: true)],
            c.Tip, AreaEntre: (0, 1), Marcas: c.Marcas, IncluirCero: true, Descripcion: "Total invertido frente al valor total de la cartera cada día");
    }

    private static int IndiceDe(IReadOnlyList<DateTime> dias, DateTime fecha)
    {
        for (var i = 0; i < dias.Count; i++) if (dias[i] >= fecha.Date) return i;
        return -1;
    }

    private void PintarComparativa()
    {
        if (_cartera is null || _pestanaGrafico != 1) return;
        var filas = EnGrafico;
        if (filas.Count == 0) { DatosComparativa = null; Leyenda = []; return; }
        var hoy = _cartera.Hoy;
        var desde = Restar(hoy, _periodoComparativa);
        var dias = SeriesCartera.Dias(desde, hoy, SeriesCartera.Paso(desde, hoy));
        // Los días sin participaciones, la línea va punteada y tenue (activos/activos-financieros 2026-11-24 09:14).
        var series = filas.Select(f => new SerieTemporal(f.Codigo, f.Color,
            SeriesCartera.Variacion(f.Activo.Posicion.Historico, dias).Select(v => v is decimal d ? (double?)(double)(d * 100m) : null).ToList(),
            Sutil: dias.Select(d => f.Activo.Posicion.ParticipacionesEn(d) <= 0m).ToList())).ToList();
        decimal? Final(SerieTemporal s) => s.Valores.LastOrDefault(v => v is not null) is double d ? (decimal)d / 100m : null;
        Leyenda = series.Select((s, i) => new LeyendaComparativa(i, s.Nombre, filas[i].Nombre, s.Color, TextosCartera.Pct(Final(s)), TextosCartera.Tono(Final(s))))
            .OrderByDescending(l => Final(series[l.Serie]) ?? decimal.MinValue).ToList();
        ContenidoTooltip Tip(int i) => new(dias[i].ToString("d 'de' MMMM 'de' yyyy", Formatos.Cultura),
            series.Select(s => (s, v: i < s.Valores.Count ? s.Valores[i] : null)).Where(x => x.v is not null).OrderByDescending(x => x.v)
                .Select(x => new FilaTooltip(x.s.Nombre, TextosCartera.Pct((decimal)x.v!.Value / 100m, 2), x.s.Color)).ToList());
        DatosComparativa = new DatosTemporales(dias, series, Tip, IncluirCero: true, LineaCero: true,
            Descripcion: "Variación acumulada de la cotización de cada activo desde el primer día del periodo");
    }

    // ---------- orden de la tabla ----------

    /// <summary>
    /// Ordena por una columna (clic en su título: ascendente, descendente y sin orden, como en las demás tablas). Lo hace el VM y no la tabla para que los
    /// lotes desplegados sigan debajo de su activo. Sin <paramref name="clave"/>, el orden de siempre (por código).
    /// </summary>
    internal void Ordenar(string? clave, bool descendente)
    {
        _orden = clave ?? "";
        _descendente = descendente;
        Filtrar();
    }

    private IEnumerable<FilaCarteraVM> Ordenadas(IEnumerable<FilaCarteraVM> filas)
    {
        Func<FilaCarteraVM, IComparable?>? clave = _orden switch
        {
            "activo" => f => f.Nombre,
            "tipo" => f => f.Clase,
            "lotes" => f => f.Activo.Posicion.LotesVivos,
            "participaciones" => f => f.Activo.Posicion.Participaciones,
            "precio_medio" => f => f.Activo.Posicion.PrecioMedio ?? -1m,
            "coste" => f => f.Vendido ? -1m : f.CosteValor,
            "cotizacion" => f => f.Activo.Posicion.Precio ?? -1m,
            "valor" => f => f.Vendido ? -1m : f.ValorNum,
            "plusvalia" => f => f.PlusvaliaNum,
            "porcentaje" => f => f.CosteValor > 0m && !f.Vendido ? f.PlusvaliaNum / f.CosteValor : decimal.MinValue,
            "anual" => f => f.AnualNum ?? decimal.MinValue,
            "peso" => f => f.Vendido ? -1m : f.ValorNum,
            _ => null,
        };
        if (clave is null) return filas;
        return _descendente ? filas.OrderByDescending(clave) : filas.OrderBy(clave);
    }

    /// <summary>Recarga lo guardado (tras una compra en el listado o en la ficha).</summary>
    internal void Cambiado()
    {
        _cartera = null;
        _ = CargarAsync(conRed: false);
    }
}
