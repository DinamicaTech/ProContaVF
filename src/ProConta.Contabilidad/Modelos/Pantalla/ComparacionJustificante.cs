using System.Globalization;
using ProConta.Contabilidad.Presentacion;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Una casilla en la que lo presentado (leído del justificante) no coincide con lo que calcula el programa.</summary>
public sealed record DiferenciaCasilla(string Casilla, string Concepto, decimal Presentado, decimal Calculado)
{
    public string Texto => $"casilla {Casilla} ({Concepto}): presentado {PeriodoModelo.Euros(Presentado)}, calculado {PeriodoModelo.Euros(Calculado)}";
}

/// <summary>
/// Qué casillas del PDF justificante de la AEAT se comparan con lo calculado en cada modelo (contabilidad/modelos-tributarios/presentacion
/// 2026-11-25) y cuál es el resultado que se guarda en PresentacionesAeat.Resultado (el mismo que guarda una presentación desde el programa).
/// Se comparan las casillas que no dependen de declaraciones anteriores: en el 303 la 69 en lugar de la 71 (que resta lo ingresado en una
/// complementaria) y en el 111 y el 115 el total retenido, no el resultado a ingresar.
/// </summary>
public static class ComparacionJustificante
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<(string Casilla, string Concepto)>> _casillas = new Dictionary<string, IReadOnlyList<(string, string)>>
    {
        ["303"] = [("27", "total cuota devengada"), ("45", "total a deducir"), ("46", "resultado régimen general"), ("66", "atribuible a la Administración del Estado"),
            ("69", "resultado de la autoliquidación")],
        ["111"] = [("03", "retenciones del trabajo"), ("09", "retenciones de profesionales"), ("28", "suma de retenciones")],
        ["115"] = [("02", "base de las retenciones"), ("03", "retenciones")],
        ["349"] = [("01", "número de operadores"), ("02", "importe de las operaciones")],
        ["390"] = [("34", "total cuotas devengadas"), ("64", "suma de deducciones"), ("65", "resultado régimen general"), ("86", "resultado de la liquidación anual"),
            ("108", "volumen de operaciones")],
        ["200"] = [("01586", "resultado de la declaración")],
    };

    /// <summary>Modelos cuyo justificante lee el programa: 303, 111, 115, 349, 390 y 200.</summary>
    public static IReadOnlyCollection<string> Modelos => _casillas.Keys.ToList();

    public static bool Lee(string modelo) => _casillas.ContainsKey(modelo);

    /// <summary>Las casillas que se comparan del modelo (vacío si no se lee su justificante).</summary>
    public static IReadOnlyList<(string Casilla, string Concepto)> Casillas(string modelo) => _casillas.TryGetValue(modelo, out var c) ? c : [];

    /// <summary>Casilla del justificante que se guarda como resultado (la que compara cada servicio con su <c>DistintoDe</c>).</summary>
    public static string? CasillaResultado(string modelo) => modelo switch
    {
        "303" => "66",
        "111" => "28",
        "115" => "03",
        "349" => "02",
        "390" => "86",
        "200" => "01586",
        _ => null,
    };

    /// <summary>El resultado leído del justificante (0 si la casilla está vacía).</summary>
    public static decimal Resultado(JustificanteAeat j) => CasillaResultado(j.Modelo) is { } c ? j.Casilla(c) ?? 0m : 0m;

    /// <summary>
    /// Las casillas en las que difieren lo presentado y lo calculado. Una casilla vacía en el justificante cuenta como 0; una que el programa no
    /// calcula no se compara.
    /// </summary>
    public static IReadOnlyList<DiferenciaCasilla> Diferencias(string modelo, IReadOnlyDictionary<string, decimal> presentadas, IReadOnlyDictionary<string, decimal> calculadas)
    {
        var r = new List<DiferenciaCasilla>();
        foreach (var (casilla, concepto) in Casillas(modelo))
        {
            if (!Buscar(calculadas, casilla, out var calculado)) continue;
            var presentado = Buscar(presentadas, casilla, out var p) ? p : 0m;
            if (presentado != calculado) r.Add(new DiferenciaCasilla(casilla, concepto, presentado, calculado));
        }
        return r;
    }

    private static bool Buscar(IReadOnlyDictionary<string, decimal> d, string casilla, out decimal v)
    {
        if (d.TryGetValue(casilla, out v)) return true;
        var n = casilla.TrimStart('0');
        foreach (var kv in d)
            if (kv.Key.TrimStart('0') == n) { v = kv.Value; return true; }
        v = 0m;
        return false;
    }

    /// <summary>Casillas con código de dos cifras («03») a partir de una función por número.</summary>
    internal static IReadOnlyDictionary<string, decimal> DeNumeros(string modelo, Func<int, decimal> casilla) =>
        Casillas(modelo).ToDictionary(c => c.Casilla, c => casilla(int.Parse(c.Casilla, CultureInfo.InvariantCulture)), StringComparer.Ordinal);
}
