using System.Globalization;

namespace ProConta.Implantacion.ERP;

/// <summary>
/// Un registro leído del ERP, por nombre de columna (sin distinguir mayúsculas). La lectura es tolerante: el ERP guarda
/// números como texto o número según la tabla, y «sin fecha» como 30/12/1899.
/// </summary>
public sealed class Fila(IReadOnlyDictionary<string, object?> valores)
{
    private readonly IReadOnlyDictionary<string, object?> _valores = valores;

    public static Fila De(params (string Campo, object? Valor)[] valores) =>
        new(valores.ToDictionary(v => v.Campo, v => v.Valor, StringComparer.OrdinalIgnoreCase));

    public IEnumerable<string> Campos => _valores.Keys;

    public object? Valor(string campo) => _valores.TryGetValue(campo, out var v) && v is not DBNull ? v : null;

    /// <summary>Texto recortado, o null si está vacío.</summary>
    public string? T(string campo) => Valor(campo) is { } v && Convert.ToString(v, CultureInfo.InvariantCulture)!.Trim() is { Length: > 0 } t ? t : null;

    public int? E(string campo) => Valor(campo) switch
    {
        null => null,
        bool b => b ? 1 : 0,
        string s => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n
            : decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? (int)d : null,
        IConvertible c => Convert.ToInt32(Convert.ToDecimal(c, CultureInfo.InvariantCulture)),
        _ => null,
    };

    public decimal? D(string campo) => Valor(campo) switch
    {
        null => null,
        string s => decimal.TryParse(s.Trim().Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? d : null,
        double x => (decimal)Math.Round(x, 6),
        float x => (decimal)Math.Round(x, 6),
        IConvertible c => Convert.ToDecimal(c, CultureInfo.InvariantCulture),
        _ => null,
    };

    public decimal Dec(string campo) => D(campo) ?? 0m;

    /// <summary>Fecha sin hora; las fechas «cero» de Access (anteriores a 1901) se leen como null.</summary>
    public DateTime? F(string campo) => Valor(campo) is DateTime d && d.Year > 1900 ? d.Date : null;

    /// <summary>Fecha con hora (o solo la hora de un campo Hora de Access, con la fecha 30/12/1899).</summary>
    public DateTime? FH(string campo) => Valor(campo) is DateTime d ? d : null;

    public bool B(string campo) => Valor(campo) switch
    {
        null => false,
        bool b => b,
        string s => s.Trim() is "1" or "-1" or "S" or "s" or "True" or "true",
        IConvertible c => Convert.ToDecimal(c, CultureInfo.InvariantCulture) != 0m,
        _ => false,
    };
}
