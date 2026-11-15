using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Inicio.Bloques;
using ProConta.Inicio.Salud;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Graficos;
using ProConta.UI.Base.Modelos;

namespace ProConta.Inicio.UI;

/// <summary>Fila de la tabla de ocupación por alojamiento.</summary>
public sealed record FilaOcupacionVM(string Alojamiento, string Noches, string Ocupacion, string Comparado, string Variacion, Tono Tono, string Ayuda,
    decimal? Porcentaje, decimal? PorcentajeComparado, bool Total = false);

/// <summary>Una fecha de la agenda.</summary>
public sealed record EntradaAgendaVM(string Dia, string Texto, string? Importe, string Ayuda, bool PuedeAbrir, ICommand? Abrir, DateTime Fecha, decimal? ImporteValor)
{
    public bool HayImporte => Importe is not null;
}

/// <summary>
/// Bloques «Ocupación turística» y «Agenda» de Inicio (pantalla-de-inicio, fase 4). Se cargan aparte de los indicadores; un fallo
/// en uno se queda en su bloque con la causa, sin tapar el resto de la pantalla.
/// </summary>
public sealed class BloquesFase4VM : ModeloVista
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static readonly string[] Meses = ["ene", "feb", "mar", "abr", "may", "jun", "jul", "ago", "sep", "oct", "nov", "dic"];
    private static readonly string[] MesesLargos = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];
    private static readonly string[] DiasSemana = ["dom", "lun", "mar", "mié", "jue", "vie", "sáb"];

    private readonly ServicioBloques _servicio;
    private readonly ContextoInicio _ctx;
    private readonly AccionesInicio _acciones;
    private int _carga;

    public BloquesFase4VM(ServicioBloques servicio, ContextoInicio contexto, AccionesInicio acciones)
    {
        _servicio = servicio;
        _ctx = contexto;
        _acciones = acciones;
    }

    // ------------------------------------------------------------ ocupación

    private bool _hayOcupacion;
    /// <summary>La sociedad tiene reservas: sin ninguna, el bloque no aparece.</summary>
    public bool HayOcupacion { get => _hayOcupacion; private set => Fijar(ref _hayOcupacion, value); }

    private DatosGrafico? _datosOcupacion;
    public DatosGrafico? DatosOcupacion { get => _datosOcupacion; private set => Fijar(ref _datosOcupacion, value); }
    public ObservableCollection<ElementoLeyenda> LeyendaOcupacion { get; } = [];
    public ObservableCollection<FilaOcupacionVM> FilasOcupacion { get; } = [];
    private string _textoOcupacion = "";
    public string TextoOcupacion { get => _textoOcupacion; private set => Fijar(ref _textoOcupacion, value); }
    private string? _notaOcupacion;
    public string? NotaOcupacion { get => _notaOcupacion; private set => Fijar(ref _notaOcupacion, value); }
    private string? _errorOcupacion;
    public string? ErrorOcupacion { get => _errorOcupacion; private set => Fijar(ref _errorOcupacion, value); }
    private string _cabeceraComparado = "Comparado";
    public string CabeceraComparado { get => _cabeceraComparado; private set => Fijar(ref _cabeceraComparado, value); }
    public PanelOcupacion? Ocupacion { get; private set; }

    // ------------------------------------------------------------ agenda

    public ObservableCollection<EntradaAgendaVM> Agenda { get; } = [];
    private string? _sinAgenda;
    public string? SinAgenda { get => _sinAgenda; private set => Fijar(ref _sinAgenda, value); }
    private string _textoAgenda = $"próximos {CalculoAgenda.Dias} días";
    public string TextoAgenda { get => _textoAgenda; private set => Fijar(ref _textoAgenda, value); }
    private bool _cargandoAgenda = true;
    public bool CargandoAgenda { get => _cargandoAgenda; private set => Fijar(ref _cargandoAgenda, value); }

    // ------------------------------------------------------------ carga

    /// <summary>Carga los dos bloques (al abrir Inicio y con «Actualizar», que antes olvida lo leído).</summary>
    public async Task CargarAsync(int? comparado, bool releer = false)
    {
        var mia = ++_carga;
        if (releer) _servicio.Olvidar();
        var ocupacion = OcupacionAsync(comparado, mia);
        await AgendaAsync(mia);
        await ocupacion;
    }

    /// <summary>Cambia el ejercicio comparado de la ocupación (no vuelve a leer la BD).</summary>
    public Task CompararAsync(int? comparado) => OcupacionAsync(comparado, _carga);

    private async Task OcupacionAsync(int? comparado, int mia)
    {
        try
        {
            var p = await Task.Run(() => _servicio.OcupacionAsync(_ctx.IdSociedad, _ctx.Ejercicio, comparado));
            if (mia != _carga) return;
            ErrorOcupacion = null;
            Aplicar(p);
        }
        catch (Exception ex) when (mia == _carga)
        {
            HayOcupacion = true;
            DatosOcupacion = null;
            FilasOcupacion.Clear();
            ErrorOcupacion = $"No he podido calcular la ocupación: {ex.Message}. Pulsa «Actualizar» para reintentarlo; el resto de Inicio no se ve afectado.";
        }
    }

    private async Task AgendaAsync(int mia)
    {
        CargandoAgenda = true;
        try
        {
            var entradas = await Task.Run(() => _servicio.AgendaAsync(_ctx.IdSociedad));
            if (mia != _carga) return;
            AplicarAgenda(entradas);
        }
        catch (Exception ex) when (mia == _carga)
        {
            Agenda.Clear();
            SinAgenda = $"No he podido leer la agenda: {ex.Message}. Pulsa «Actualizar» para reintentarlo.";
        }
        finally
        {
            if (mia == _carga) CargandoAgenda = false;
        }
    }

    private static string Pct(decimal? v) => v is decimal x ? x.ToString("0.0", Es) + " %" : "—";

    internal void Aplicar(PanelOcupacion? p)
    {
        Ocupacion = p;
        HayOcupacion = p is not null;
        FilasOcupacion.Clear();
        LeyendaOcupacion.Clear();
        if (p is null)
        {
            DatosOcupacion = null;
            return;
        }
        var anio = p.Actual.Ejercicio.ToString(Es);
        var corteMes = p.Corte.Month;
        var actual = Enumerable.Range(1, 12).Select(m => m <= p.UltimoMes ? p.Actual.Porcentaje(m) : null).ToList();
        var series = new List<SerieGrafico>();
        if (p.Comparado is { } c)
        {
            series.Add(new SerieGrafico(c.Ejercicio.ToString(Es), PaletaGraficos.Serie2, Enumerable.Range(1, 12).Select(c.Porcentaje).ToList()));
            LeyendaOcupacion.Add(new ElementoLeyenda(c.Ejercicio.ToString(Es), PaletaGraficos.Serie2));
        }
        series.Add(new SerieGrafico(anio, PaletaGraficos.Serie1, actual));
        LeyendaOcupacion.Insert(0, new ElementoLeyenda(anio, PaletaGraficos.Serie1));
        DatosOcupacion = new DatosGrafico(Meses, series, i =>
        {
            var m = i + 1;
            var filas = new List<FilaTooltip>
            {
                new(anio, actual[i] is decimal a ? $"{Pct(a)} ({p.Actual.Ocupadas[i]} de {p.Actual.Disponibles[i]} noches)" : "—", PaletaGraficos.Serie1),
            };
            if (p.Comparado is { } cc)
                filas.Add(new FilaTooltip(cc.Ejercicio.ToString(Es), cc.Porcentaje(m) is decimal b ? $"{Pct(b)} ({cc.Ocupadas[i]} de {cc.Disponibles[i]} noches)" : "—", PaletaGraficos.Serie2));
            var titulo = MesesLargos[i] + (p.Corte.Year == p.Actual.Ejercicio && m == corteMes ? " (con las reservas ya hechas hasta fin de mes)"
                : p.Corte.Year == p.Actual.Ejercicio && m > corteMes ? " (reservas ya hechas)" : "");
            return new ContenidoTooltip(titulo, filas);
        }, Minimo: 0, Maximo: 100, Descripcion: "Ocupación turística por mes");

        CabeceraComparado = p.Comparado is { } cmp ? cmp.Ejercicio.ToString(Es) : "Comparado";
        var hasta = $"{p.Corte:dd/MM}";
        foreach (var a in p.Alojamientos.Append(p.Total))
        {
            var total = ReferenceEquals(a, p.Total);
            var sinPiso = a.Nombre == CalculoOcupacion.SinAlojamiento;
            var dif = a.Porcentaje is decimal x && a.PorcentajeComparado is decimal y ? x - y : (decimal?)null;
            FilasOcupacion.Add(new FilaOcupacionVM(a.Nombre, a.Ocupadas.ToString("#,##0", Es), sinPiso ? "—" : Pct(a.Porcentaje),
                p.Comparado is null ? "—" : sinPiso ? (a.OcupadasComparado ?? 0).ToString("#,##0", Es) + " noches" : Pct(a.PorcentajeComparado),
                dif is decimal d ? (d >= 0 ? "▲ " : "▼ ") + Math.Abs(d).ToString("0.0", Es) + " p." : "", dif is null or 0 ? Tono.Neutro : dif > 0 ? Tono.Bueno : Tono.Malo,
                sinPiso
                    ? $"Noches de reservas sin piso asignado (las del ERP, donde el piso es texto libre en las notas): suman al total, pero no se pueden repartir por alojamiento. Del 1/1 al {hasta}."
                    : total
                        ? $"Todas las noches ocupadas del 1/1 al {hasta} entre las noches disponibles de los {p.Alojados} alojamientos" + (p.Comparado is null ? "." : ", frente al mismo periodo del comparado.")
                        : $"{a.Nombre}: {a.Ocupadas} noches ocupadas de {a.Disponibles} disponibles del 1/1 al {hasta}" + (a.PorcentajeComparado is decimal pc ? $"; {Pct(pc)} en el comparado a la misma fecha."
                            : p.Comparado is null ? "." : ". El comparado tiene reservas sin piso asignado, así que no se puede dar su ocupación por piso (sí la del total)."),
                a.Porcentaje, a.PorcentajeComparado, total));
        }
        TextoOcupacion = $"noches ocupadas / disponibles, del 1/1 al {hasta} y por mes";
        var sinAloj = p.Alojamientos.FirstOrDefault(a => a.Nombre == CalculoOcupacion.SinAlojamiento);
        NotaOcupacion = sinAloj is null ? null
            : "Las reservas del ERP no tienen piso asignado (está en sus notas): cuentan en el total y en el gráfico, pero no en la ocupación de cada piso, que sale más baja; " +
              "si el comparado las tiene, su ocupación por piso no se da.";
    }

    internal void AplicarAgenda(IReadOnlyList<EntradaAgenda> entradas)
    {
        Agenda.Clear();
        foreach (var e in entradas)
        {
            var accion = Accion(e.Destino);
            var dia = $"{DiasSemana[(int)e.Fecha.DayOfWeek]} {e.Fecha:dd/MM}";
            Agenda.Add(new EntradaAgendaVM(dia, e.Texto, e.Importe is decimal i ? CalendarioFiscal.Imp(i) : null,
                $"{e.Fecha:dd/MM/yyyy}: {e.Detalle} " + (accion is null ? $"La pantalla ({e.Destino.Nombre}) aún no existe en ProContaVF." : $"Doble clic: abre {e.Destino.Nombre}."),
                accion is not null, accion is null ? null : new ComandoDelegado(accion), e.Fecha, e.Importe));
        }
        var programadas = entradas.Where(e => e.Tipo == TipoAgenda.Programadas).Sum(e => e.Importe ?? 0);
        SinAgenda = entradas.Count == 0 ? $"Nada en los próximos {CalculoAgenda.Dias} días." : null;
        TextoAgenda = $"próximos {CalculoAgenda.Dias} días" + (programadas != 0 ? $" · programadas {CalendarioFiscal.Imp(programadas)}" : "");
    }

    private Action? Accion(DestinoSalud d) => d.Pantalla switch
    {
        PantallaSalud.Programadas => _acciones.AbrirProgramadas,
        PantallaSalud.VentasPorFacturar => _acciones.AbrirVentas,
        PantallaSalud.Ejercicios => _acciones.AbrirParametros is { } p ? () => p("ejercicios") : null,
        _ => null,
    };
}
