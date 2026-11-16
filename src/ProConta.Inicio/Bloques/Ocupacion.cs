namespace ProConta.Inicio.Bloques;

/// <summary>
/// Reserva (venta por facturar sin anular) para la ocupación: alojamiento (activo; null si no lo tiene, como las del ERP), fecha de
/// entrada, fecha de salida y noches (Cantidad), que solo se usan si falta la salida.
/// </summary>
public sealed record ReservaOcupacion(int? IdActivo, DateTime Entrada, DateTime? Salida, decimal Noches);

/// <summary>Alojamiento turístico (activo): nombre corto y desde cuándo y hasta cuándo está disponible (alta o adquisición y baja).</summary>
public sealed record AlojamientoOcupacion(int IdActivo, string Nombre, DateTime? Alta, DateTime? Baja);

/// <summary>Ocupación de un ejercicio: noches ocupadas y disponibles por mes (12) y su porcentaje (null sin noches disponibles).</summary>
public sealed record OcupacionAnual(int Ejercicio, IReadOnlyList<int> Ocupadas, IReadOnlyList<int> Disponibles)
{
    public decimal? Porcentaje(int mes) => Disponibles[mes - 1] == 0 ? null : Math.Round(Ocupadas[mes - 1] * 100m / Disponibles[mes - 1], 1, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Ocupación de un alojamiento (o de las reservas sin alojamiento, con <see cref="IdActivo"/> null) del 1 de enero a la fecha de corte
/// del ejercicio y del comparado a la misma fecha.
/// </summary>
public sealed record OcupacionAlojamiento(int? IdActivo, string Nombre, int Ocupadas, int Disponibles, int? OcupadasComparado, int? DisponiblesComparado)
{
    public decimal? Porcentaje => Disponibles == 0 ? null : Math.Round(Ocupadas * 100m / Disponibles, 1, MidpointRounding.AwayFromZero);
    public decimal? PorcentajeComparado => DisponiblesComparado is int d && d > 0 && OcupadasComparado is int o ? Math.Round(o * 100m / d, 1, MidpointRounding.AwayFromZero) : null;
}

/// <summary>
/// Bloque «Ocupación turística» de Inicio (pantalla-de-inicio 2026-11-15 16:37): ocupación por mes del ejercicio y del comparado y
/// por alojamiento hasta la fecha de corte. <see cref="UltimoMes"/>: el último mes que se dibuja del ejercicio (el de corte o, si hay
/// reservas ya hechas para después, el último con reservas). <see cref="Total"/>: todas las noches ocupadas (también las de las
/// reservas sin piso) entre las disponibles de todos los alojamientos, del ejercicio y del comparado a la misma fecha.
/// </summary>
public sealed record PanelOcupacion(OcupacionAnual Actual, OcupacionAnual? Comparado, IReadOnlyList<OcupacionAlojamiento> Alojamientos, DateTime Corte, int UltimoMes,
    int Alojados, OcupacionAlojamiento Total);

/// <summary>
/// Cálculo de la ocupación turística (facturacion/reservas): noches ocupadas / noches disponibles. Cada noche de una reserva es un día
/// desde la entrada hasta el anterior a la salida (si falta la salida, tantas noches como su cantidad) y cuenta en el mes de ese día.
/// Un alojamiento está disponible cada día desde su alta (o adquisición) hasta su baja. Las reservas sin alojamiento (las del ERP, cuyo
/// piso es texto libre) suman noches ocupadas al total, pero no tienen noches disponibles propias. Sin las anuladas (se quitan al
/// leerlas). Porcentajes con un decimal, redondeados solo al mostrar.
/// </summary>
public static class CalculoOcupacion
{
    public const string SinAlojamiento = "Sin piso asignado";

    /// <summary>Días ocupados de una reserva (sus noches).</summary>
    public static IEnumerable<DateTime> Noches(ReservaOcupacion r)
    {
        var entrada = r.Entrada.Date;
        var noches = r.Salida is DateTime s && s.Date > entrada ? (s.Date - entrada).Days : (int)Math.Round(r.Noches, MidpointRounding.AwayFromZero);
        for (var i = 0; i < noches; i++) yield return entrada.AddDays(i);
    }

    private static bool Disponible(AlojamientoOcupacion a, DateTime dia) => (a.Alta is not DateTime alta || alta.Date <= dia) && (a.Baja is not DateTime baja || dia < baja.Date);

    private static IEnumerable<DateTime> Dias(DateTime desde, DateTime hastaIncluido)
    {
        for (var d = desde.Date; d <= hastaIncluido.Date; d = d.AddDays(1)) yield return d;
    }

    public static OcupacionAnual Anual(int ejercicio, IReadOnlyList<ReservaOcupacion> reservas, IReadOnlyList<AlojamientoOcupacion> alojamientos)
    {
        var ocupadas = new int[12];
        var disponibles = new int[12];
        foreach (var d in reservas.SelectMany(Noches).Where(d => d.Year == ejercicio)) ocupadas[d.Month - 1]++;
        foreach (var d in Dias(new DateTime(ejercicio, 1, 1), new DateTime(ejercicio, 12, 31)))
            disponibles[d.Month - 1] += alojamientos.Count(a => Disponible(a, d));
        return new OcupacionAnual(ejercicio, ocupadas, disponibles);
    }

    /// <summary>Calcula el bloque; null si la sociedad no tiene ninguna reserva (entonces el bloque no aparece).</summary>
    public static PanelOcupacion? Calcular(int ejercicio, int? comparado, DateTime corte, IReadOnlyList<ReservaOcupacion> reservas, IReadOnlyList<AlojamientoOcupacion> alojamientos)
    {
        if (reservas.Count == 0) return null;
        corte = corte.Date;
        var actual = Anual(ejercicio, reservas, alojamientos);
        var comp = comparado is int c ? Anual(c, reservas, alojamientos) : null;
        var ultimoConReservas = Enumerable.Range(1, 12).LastOrDefault(m => actual.Ocupadas[m - 1] > 0);
        var ultimoMes = corte.Year > ejercicio ? 12 : Math.Max(corte.Year == ejercicio ? corte.Month : 0, ultimoConReservas);
        var mismaFecha = comparado is int cc ? CalculoInicio.MismaFecha(corte, cc) : (DateTime?)null;

        (int Ocupadas, int Disponibles) Hasta(int? idActivo, int anio, DateTime hasta)
        {
            var desde = new DateTime(anio, 1, 1);
            var ocupadas = reservas.Where(r => r.IdActivo == idActivo).SelectMany(Noches).Count(d => d >= desde && d <= hasta);
            var aloj = idActivo is int id ? alojamientos.FirstOrDefault(a => a.IdActivo == id) : null;
            var disponibles = aloj is null ? 0 : Dias(desde, hasta).Count(d => Disponible(aloj, d));
            return (ocupadas, disponibles);
        }
        var hastaActual = corte.Year > ejercicio ? new DateTime(ejercicio, 12, 31) : corte;
        var (so, _) = Hasta(null, ejercicio, hastaActual);
        int? sc = mismaFecha is DateTime fs ? Hasta(null, fs.Year, fs).Ocupadas : null;
        var filas = new List<OcupacionAlojamiento>();
        foreach (var a in alojamientos.OrderBy(a => a.Nombre, StringComparer.CurrentCulture))
        {
            var (o, d) = Hasta(a.IdActivo, ejercicio, hastaActual);
            // Si el comparado tiene reservas sin piso, la ocupación de cada piso en el comparado no es comparable: no se da.
            (int, int)? x = mismaFecha is DateTime f && sc is 0 ? Hasta(a.IdActivo, f.Year, f) : null;
            filas.Add(new OcupacionAlojamiento(a.IdActivo, a.Nombre, o, d, x?.Item1, x?.Item2));
        }
        if (so > 0 || sc > 0) filas.Add(new OcupacionAlojamiento(null, SinAlojamiento, so, 0, sc, mismaFecha is null ? null : 0));
        int TotalOcupadas(int anio, DateTime hasta) => reservas.SelectMany(Noches).Count(d => d >= new DateTime(anio, 1, 1) && d <= hasta);
        int TotalDisponibles(int anio, DateTime hasta) => Dias(new DateTime(anio, 1, 1), hasta).Sum(d => alojamientos.Count(a => Disponible(a, d)));
        var total = new OcupacionAlojamiento(null, "Total", TotalOcupadas(ejercicio, hastaActual), TotalDisponibles(ejercicio, hastaActual),
            mismaFecha is DateTime t ? TotalOcupadas(t.Year, t) : null, mismaFecha is DateTime t2 ? TotalDisponibles(t2.Year, t2) : null);
        return new PanelOcupacion(actual, comp, filas, hastaActual, ultimoMes, alojamientos.Count, total);
    }
}
