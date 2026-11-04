using System.Globalization;
using ProConta.Facturacion.Facturas;

namespace ProConta.Facturacion.Programadas;

/// <summary>
/// Cadencia de una programada (RF-PRO-01): cada 1, 2, 3, 4, 6 o 12 meses, o una sola vez. El código es el texto que se
/// guarda en FacturasProgramadas.Periodicidad.
/// </summary>
public sealed record Periodicidad(string Codigo, string Texto, int Meses)
{
    public static readonly Periodicidad Mensual = new("MENSUAL", "Mensual", 1);
    public static readonly Periodicidad Bimestral = new("BIMESTRAL", "Bimestral (cada 2 meses)", 2);
    public static readonly Periodicidad Trimestral = new("TRIMESTRAL", "Trimestral (cada 3 meses)", 3);
    public static readonly Periodicidad Cuatrimestral = new("CUATRIMESTRAL", "Cuatrimestral (cada 4 meses)", 4);
    public static readonly Periodicidad Semestral = new("SEMESTRAL", "Semestral (cada 6 meses)", 6);
    public static readonly Periodicidad Anual = new("ANUAL", "Anual", 12);
    public static readonly Periodicidad Puntual = new("PUNTUAL", "Una sola vez", 0);

    public static IReadOnlyList<Periodicidad> Todas { get; } = [Mensual, Bimestral, Trimestral, Cuatrimestral, Semestral, Anual, Puntual];

    public bool EsPuntual => Meses == 0;

    /// <summary>La de ese código; mensual si el código no se conoce (o falta).</summary>
    public static Periodicidad De(string? codigo) =>
        Todas.FirstOrDefault(p => string.Equals(p.Codigo, codigo?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? Mensual;

    public override string ToString() => Texto;
}

/// <summary>Qué se crea en cada vencimiento: un borrador sin número o una factura validada (numerada y contabilizada).</summary>
public enum AlGenerar
{
    Borrador,
    Validar
}

/// <summary>
/// Fechas de una programada (RF-PRO-02). Un día de 1 a 28 es fijo; 29, 30 y 31 son la posición desde el final del mes
/// (31 el último día, 30 el penúltimo y 29 el antepenúltimo): 31 ene → 28 feb → 31 mar; 29 ene → 26 feb → 29 mar.
/// Cada fecha se calcula desde la primera (nunca desde la anterior), para que un febrero no arrastre el día a los
/// meses siguientes.
/// </summary>
public static class CalendarioProgramacion
{
    /// <summary>El día <paramref name="dia"/> (1 a 31) del mes de <paramref name="mes"/> según el ancla.</summary>
    public static DateTime Anclar(DateTime mes, int dia)
    {
        if (dia is < 1 or > 31) throw new ArgumentOutOfRangeException(nameof(dia), dia, "El día de emisión va de 1 a 31.");
        var ultimo = DateTime.DaysInMonth(mes.Year, mes.Month);
        var d = dia <= 28 ? dia : ultimo - (31 - dia);
        return new DateTime(mes.Year, mes.Month, d);
    }

    /// <summary>
    /// <paramref name="origen"/> desplazada <paramref name="meses"/> meses con el ancla de su propio día (31/01 + 1 mes
    /// = 28/02; + 2 meses = 31/03).
    /// </summary>
    public static DateTime Desplazar(DateTime origen, int meses) => Anclar(origen.AddDays(1 - origen.Day).AddMonths(meses), origen.Day);

    /// <summary>
    /// Primera emisión: el día de emisión en el mes de la fecha de inicio o, si ya ha pasado, en el siguiente. Sin día
    /// de emisión, la propia fecha de inicio.
    /// </summary>
    public static DateTime PrimeraEmision(DateTime inicio, int? diaEmision)
    {
        if (diaEmision is not int dia) return inicio.Date;
        var enMes = Anclar(inicio, dia);
        return enMes >= inicio.Date ? enMes : Anclar(inicio.AddDays(1 - inicio.Day).AddMonths(1), dia);
    }

    /// <summary>
    /// Emisión número <paramref name="indice"/> (0 = la primera): la primera desplazada <c>indice × meses</c> con el ancla
    /// del día de emisión (o del día de la primera, si no hay).
    /// </summary>
    public static DateTime Emision(DateTime inicio, int? diaEmision, Periodicidad periodicidad, int indice)
    {
        var primera = PrimeraEmision(inicio, diaEmision);
        if (indice == 0 || periodicidad.EsPuntual) return primera;
        return Anclar(primera.AddDays(1 - primera.Day).AddMonths(indice * periodicidad.Meses), diaEmision ?? primera.Day);
    }
}

/// <summary>Línea de una programada: la de una factura (se copia tal cual a cada factura generada).</summary>
/// <remarks>LineasFacturasProgramadas no tiene fecha ni importes calculados: se recalculan desde cantidad y precio.</remarks>
public static class LineasProgramada
{
    public static LineaFacturaVenta Copiar(LineaFacturaVenta l)
    {
        var c = l.Copiar();
        c.Fecha = null;
        c.Recalcular();
        return c;
    }
}

/// <summary>
/// Ente FacturaProgramada (facturacion/programadas): una plantilla sin número (cliente, condiciones y líneas, como una
/// factura) más su programación. Cada vencimiento crea una factura de la sociedad con el periodo de ese vencimiento.
/// <see cref="Plantilla"/> reutiliza el ente FacturaVenta para que el editor, el cálculo y las reglas sean los mismos;
/// sus FechaInicioValidez y FechaFinValidez son el periodo de la primera factura.
/// </summary>
public sealed class FacturaProgramada
{
    public int IdFacturaProgramada { get; internal set; }

    /// <summary>Ejercicio en que se creó; da la sociedad. Las facturas van al ejercicio de su fecha de emisión.</summary>
    public int IdSociedadEjercicio { get; internal set; }
    public int? IdContrato { get; internal set; }
    public FacturaVenta Plantilla { get; internal set; } = new();

    public Periodicidad Periodicidad { get; set; } = Periodicidad.Mensual;
    public DateTime? FechaInicio { get; set; }
    public byte? DiaEmision { get; set; }

    /// <summary>Final por fecha: después de ella no se emite ninguna factura (null = sin fecha final).</summary>
    public DateTime? FechaFin { get; set; }

    /// <summary>Final por número de facturas (null = sin límite). Una puntual es siempre una.</summary>
    public int? NumeroRepeticiones { get; set; }

    /// <summary>Facturas ya generadas: es el índice del próximo vencimiento.</summary>
    public int FacturasGeneradas { get; internal set; }
    public DateTime? ProximaFechaEmision { get; internal set; }
    public DateTime? FechaUltimaGeneracion { get; internal set; }
    public bool Activa { get; set; }
    public AlGenerar AlGenerar { get; set; } = AlGenerar.Borrador;
    public bool EnviarPorEmail { get; set; }
    public DateTime FechaAlta { get; internal set; }
    public string? OperadorAlta { get; internal set; }
    public string? Historial { get; internal set; }
    public int Version { get; internal set; }

    public bool EsNueva => IdFacturaProgramada == 0;

    /// <summary>Código visible: «P-0041» (o «Programada nueva»).</summary>
    public string Rotulo => RotuloDe(IdFacturaProgramada);

    public static string RotuloDe(int id) => id > 0 ? $"P-{id:0000}" : "Programada nueva";

    /// <summary>Repeticiones efectivas: 1 en una puntual; si no, las indicadas.</summary>
    public int? Limite => Periodicidad.EsPuntual ? 1 : NumeroRepeticiones;

    /// <summary>Fecha del vencimiento <paramref name="indice"/> (0 = la primera factura), o null si falta el inicio.</summary>
    public DateTime? Emision(int indice) =>
        FechaInicio is { } inicio ? CalendarioProgramacion.Emision(inicio, DiaEmision, Periodicidad, indice) : null;

    /// <summary>Periodo facturado del vencimiento <paramref name="indice"/>: el primero desplazado <c>indice × meses</c>.</summary>
    public (DateTime? Desde, DateTime? Hasta) Periodo(int indice)
    {
        var meses = indice * Periodicidad.Meses;
        return (Plantilla.FechaInicioValidez is { } d ? CalendarioProgramacion.Desplazar(d, meses) : null,
            Plantilla.FechaFinValidez is { } h ? CalendarioProgramacion.Desplazar(h, meses) : null);
    }

    /// <summary>
    /// Si el vencimiento <paramref name="indice"/> todavía existe: no ha alcanzado el número de repeticiones ni pasa de
    /// la fecha final.
    /// </summary>
    public bool ExisteVencimiento(int indice) =>
        Emision(indice) is { } e && (Limite is not int l || indice < l) && (FechaFin is not { } fin || e <= fin.Date);

    /// <summary>Finalizada: ya no le queda ningún vencimiento.</summary>
    public bool Finalizada => FechaInicio is not null && !ExisteVencimiento(FacturasGeneradas);

    /// <summary>La próxima fecha de emisión según la programación (null si está finalizada o le falta el inicio).</summary>
    public DateTime? ProximaCalculada => ExisteVencimiento(FacturasGeneradas) ? Emision(FacturasGeneradas) : null;

    /// <summary>Activa, Pausada (inactiva con vencimientos pendientes) o Finalizada.</summary>
    public string TextoEstado => Finalizada ? "Finalizada" : Activa ? "Activa" : "Pausada";

    /// <summary>Las próximas <paramref name="cuantas"/> fechas de emisión, para comprobar el ancla en pantalla.</summary>
    public IReadOnlyList<DateTime> ProximasEmisiones(int cuantas)
    {
        var r = new List<DateTime>();
        for (var i = FacturasGeneradas; r.Count < cuantas && ExisteVencimiento(i); i++) r.Add(Emision(i)!.Value);
        return r;
    }

    internal void Anotar(DateTime ahora, string operador, string accion)
    {
        var linea = $"{ahora:dd/MM/yyyy HH:mm} · {operador} · {accion}";
        Historial = string.IsNullOrEmpty(Historial) ? linea : Historial + Environment.NewLine + linea;
    }

    public IReadOnlyList<string> LineasHistorial =>
        (Historial ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

    public FacturaProgramada Clonar()
    {
        var c = (FacturaProgramada)MemberwiseClone();
        c.Plantilla = Plantilla.Clonar();
        return c;
    }
}

/// <summary>Reglas sin BD de la programada.</summary>
public static class ReglasProgramada
{
    /// <summary>
    /// Lo que impide que una programada esté activa (y por tanto genere facturas): los datos de la factura que exige la
    /// validación (cliente, descripción, líneas con nombre y cuenta…) y la programación (inicio, día de emisión, final).
    /// Una programada pausada se puede guardar incompleta.
    /// </summary>
    public static IReadOnlyList<Incidencia> ValidarParaActivar(FacturaProgramada p)
    {
        var r = new List<Incidencia>();
        var plantilla = p.Plantilla.Clonar();
        plantilla.Estado = EstadoFactura.Borrador;
        r.AddRange(ReglasFactura.ValidarParaEmision(plantilla, null).Where(i => !i.EsAviso && i.Codigo != "Facturas.NoEsBorrador"));
        r.AddRange(ValidarProgramacion(p));
        return r;
    }

    /// <summary>Coherencia de la programación (también en una pausada: lo que se guarda tiene que tener sentido).</summary>
    public static IReadOnlyList<Incidencia> ValidarProgramacion(FacturaProgramada p)
    {
        var r = new List<Incidencia>();
        if (p.FechaInicio is null)
            r.Add(new("Programadas.SinInicio", "Falta la fecha de inicio: desde ella se emite la primera factura.", "FechaInicio"));
        if (p.DiaEmision is byte d && d is < 1 or > 31)
            r.Add(new("Programadas.DiaEmision", $"El día de emisión va de 1 a 31 y es {d}. Del 29 al 31 cuentan desde el final del mes (31 = último día).", "DiaEmision"));
        if (p.NumeroRepeticiones is int n && n < 1)
            r.Add(new("Programadas.Repeticiones", "El número de facturas debe ser al menos 1. Déjalo vacío si no tiene final.", "NumeroRepeticiones"));
        if (p.NumeroRepeticiones is int nr && nr < p.FacturasGeneradas)
            r.Add(new("Programadas.RepeticionesGeneradas", $"Ya se han generado {p.FacturasGeneradas} facturas: el número de facturas no puede ser menor.", "NumeroRepeticiones"));
        if (p.FechaFin is { } fin && p.FechaInicio is { } ini && fin.Date < ini.Date)
            r.Add(new("Programadas.FinAntesDeInicio", $"La fecha final ({fin:dd/MM/yyyy}) es anterior a la de inicio ({ini:dd/MM/yyyy}).", "FechaFin"));
        if (p.FechaInicio is not null && p.Emision(0) is { } primera && p.FechaFin is { } f && primera > f.Date)
            r.Add(new("Programadas.SinVencimientos", $"Con esta fecha final no se emitiría ninguna factura: la primera sería el {primera:dd/MM/yyyy}.", "FechaFin"));
        return r;
    }

    /// <summary>Texto del final para listados: «Sin final», «12 facturas», «Hasta 31/12/2027».</summary>
    public static string TextoFinal(FacturaProgramada p) =>
        p.Periodicidad.EsPuntual ? "Una sola factura"
        : p.NumeroRepeticiones is int n ? $"{n.ToString(CultureInfo.InvariantCulture)} {(n == 1 ? "factura" : "facturas")}"
        : p.FechaFin is { } f ? $"Hasta {f:dd/MM/yyyy}"
        : "Sin final";
}
