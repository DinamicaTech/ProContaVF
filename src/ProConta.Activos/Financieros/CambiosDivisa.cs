using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Financieros;

/// <summary>
/// Cliente del portal de datos del Banco Central Europeo: cambios de referencia diarios del euro (serie EXR, D.{moneda}.EUR.SP00.A), publicados los días
/// hábiles hacia las 16:00 CET. El valor es cuántas unidades de la moneda vale un euro.
/// </summary>
public sealed class ClienteBce(HttpClient http)
{
    public const string Direccion = "https://data-api.ecb.europa.eu/service/data/EXR/";

    public async Task<Resultado<IReadOnlyList<(DateTime Fecha, decimal UnidadesPorEuro)>>> CambiosAsync(string moneda, DateTime desde, DateTime hasta,
        CancellationToken cancelacion = default)
    {
        var url = $"{Direccion}D.{Uri.EscapeDataString(moneda.ToUpperInvariant())}.EUR.SP00.A?startPeriod={desde:yyyy-MM-dd}&endPeriod={hasta:yyyy-MM-dd}&format=csvdata";
        try
        {
            using var resp = await http.GetAsync(url, cancelacion);
            var cuerpo = await resp.Content.ReadAsStringAsync(cancelacion);
            // Sin datos en el periodo, el BCE responde 404 (o 200 vacío).
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return Resultado<IReadOnlyList<(DateTime, decimal)>>.Correcto([]);
            if (!resp.IsSuccessStatusCode)
                return Resultado<IReadOnlyList<(DateTime, decimal)>>.Fallo(new Error("Bce.Http", $"El Banco Central Europeo no responde bien (HTTP {(int)resp.StatusCode}).", null, null,
                    "Vuelve a intentarlo en unos minutos; si no, escribe el precio en euros a mano."));
            return Resultado<IReadOnlyList<(DateTime, decimal)>>.Correcto(LeerCsv(cuerpo));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Resultado<IReadOnlyList<(DateTime, decimal)>>.Fallo(new Error("Bce.SinConexion", "No he podido conectar con el Banco Central Europeo para el cambio de divisa.", null,
                ex.Message, "Comprueba la conexión a internet y vuelve a intentarlo; si no, escribe el precio en euros a mano."));
        }
    }

    /// <summary>Lee el CSV del BCE por el nombre de las columnas TIME_PERIOD y OBS_VALUE.</summary>
    public static IReadOnlyList<(DateTime Fecha, decimal UnidadesPorEuro)> LeerCsv(string csv)
    {
        var lineas = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lineas.Length < 2) return [];
        var cabecera = lineas[0].Split(',');
        var iFecha = Array.IndexOf(cabecera, "TIME_PERIOD");
        var iValor = Array.IndexOf(cabecera, "OBS_VALUE");
        if (iFecha < 0 || iValor < 0) return [];
        var r = new List<(DateTime, decimal)>();
        foreach (var l in lineas.Skip(1))
        {
            var c = l.Split(',');
            if (c.Length <= Math.Max(iFecha, iValor)) continue;
            if (DateTime.TryParseExact(c[iFecha], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)
                && decimal.TryParse(c[iValor], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0m)
                r.Add((f, v));
        }
        return r.OrderBy(x => x.Item1).ToList();
    }
}

/// <summary>
/// Cambios de divisa del BCE guardados en la tabla CambiosDivisa (activos/activos-financieros 2026-11-23 20:24): se piden al BCE la primera vez que hacen
/// falta y se reutilizan. Para una fecha sin cambio publicado (fin de semana, festivo o el día en curso antes de las 16:00) vale el último anterior, de
/// como mucho <see cref="DiasHaciaAtras"/> días.
/// </summary>
public sealed class ServicioCambiosDivisa(IBaseDatos bd, ClienteBce bce)
{
    public const int DiasHaciaAtras = 10;
    public const string Origen = "BCE";

    /// <summary>Unidades de la moneda por un euro en la fecha (o el último día publicado antes) y el día del cambio. EUR siempre vale 1.</summary>
    public async Task<Resultado<(DateTime Fecha, decimal UnidadesPorEuro)>> CambioAsync(string moneda, DateTime fecha, CancellationToken cancelacion = default)
    {
        moneda = moneda.Trim().ToUpperInvariant();
        fecha = fecha.Date;
        if (moneda == "EUR") return Resultado<(DateTime, decimal)>.Correcto((fecha, 1m));
        var guardado = await LeerAsync(moneda, fecha);
        if (guardado is { } g && g.Fecha == fecha) return Resultado<(DateTime, decimal)>.Correcto(g);
        var r = await bce.CambiosAsync(moneda, fecha.AddDays(-DiasHaciaAtras), fecha, cancelacion);
        if (r.EsCorrecto && r.Valor.Count > 0)
        {
            await GuardarAsync(moneda, r.Valor);
            var ultimo = r.Valor.Where(x => x.Fecha <= fecha).OrderBy(x => x.Fecha).LastOrDefault();
            if (ultimo.UnidadesPorEuro > 0m) return Resultado<(DateTime, decimal)>.Correcto(ultimo);
        }
        if (guardado is { } anterior) return Resultado<(DateTime, decimal)>.Correcto(anterior);
        return r.EsCorrecto
            ? Resultado<(DateTime, decimal)>.Fallo(new Error("Bce.SinCambio", $"El Banco Central Europeo no publica el cambio del euro con {moneda} cerca del {fecha:dd/MM/yyyy}.", null,
                "Puede que el BCE no publique esa moneda o que la fecha sea futura.", "Escribe el precio en euros a mano."))
            : Resultado<(DateTime, decimal)>.Fallo(r.Errores);
    }

    private Task<(DateTime Fecha, decimal UnidadesPorEuro)?> LeerAsync(string moneda, DateTime fecha) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync<(DateTime, decimal)?>(
        "SELECT TOP 1 Fecha, UnidadesPorEuro FROM CambiosDivisa WHERE Moneda = ? AND Fecha <= ? AND Fecha >= ? ORDER BY Fecha DESC",
        f => (f.Fecha("Fecha").Date, f.Decimal("UnidadesPorEuro")), moneda, fecha, fecha.AddDays(-DiasHaciaAtras)));

    private Task GuardarAsync(string moneda, IReadOnlyList<(DateTime Fecha, decimal UnidadesPorEuro)> cambios) => bd.EnTransaccionAsync(async s =>
    {
        foreach (var (f, v) in cambios)
        {
            var existe = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM CambiosDivisa WHERE Moneda = ? AND Fecha = ?", moneda, f.Date), CultureInfo.InvariantCulture) > 0;
            if (!existe)
                await s.EjecutarAsync("INSERT INTO CambiosDivisa (Moneda, Fecha, UnidadesPorEuro, Origen, FechaHoraImportacion) VALUES (?, ?, ?, ?, ?)",
                    moneda, f.Date, v, Origen, DateTime.Now);
        }
        return true;
    });
}
