using System.Globalization;
using System.Text;

namespace ProConta.Norma43.Tests;

/// <summary>Movimiento inventado para un fichero de prueba; <c>cargo</c> = clave 1 (debe).</summary>
public sealed record Mov(DateTime Fecha, decimal Importe, bool Cargo, string Concepto, string Documento = "0000000000", string Ref1 = "", string Ref2 = "", DateTime? Valor = null);

/// <summary>
/// Genera ficheros Norma 43 sintéticos (cuaderno 43, registros de 80 posiciones) con datos inventados, cuadrados por defecto.
/// </summary>
public sealed class FicheroSintetico
{
    private readonly List<(string Entidad, string Oficina, string Cuenta, DateTime Desde, DateTime Hasta, decimal SaldoInicial, IReadOnlyList<Mov> Movs, string Nombre)> _cuentas = [];

    public FicheroSintetico Cuenta(string entidad, string oficina, string cuenta, DateTime desde, DateTime hasta, decimal saldoInicial, string nombre, params Mov[] movs)
    {
        _cuentas.Add((entidad, oficina, cuenta, desde, hasta, saldoInicial, movs, nombre));
        return this;
    }

    /// <summary>Ajustes para romper el cuadre a propósito.</summary>
    public decimal AjusteSaldoFinal { get; set; }
    public int AjusteNumeroCargos { get; set; }
    public bool SinCierre { get; set; }
    public string FinDeLinea { get; set; } = "\r\n";

    public IReadOnlyList<string> Registros()
    {
        var r = new List<string>();
        foreach (var c in _cuentas)
        {
            r.Add(Linea($"11{c.Entidad}{c.Oficina}{c.Cuenta}{F(c.Desde)}{F(c.Hasta)}{(c.SaldoInicial < 0 ? "1" : "2")}{I(c.SaldoInicial)}9783{c.Nombre,-26}   "));
            foreach (var m in c.Movs)
            {
                r.Add(Linea($"22    {c.Oficina}{F(m.Fecha)}{F(m.Valor ?? m.Fecha)}02099{(m.Cargo ? "1" : "2")}{I(m.Importe)}{m.Documento,10}{m.Ref1,-12}{m.Ref2,-16}"));
                var concepto = m.Concepto.PadRight(76);
                r.Add(Linea($"2301{concepto[..38]}{concepto.Substring(38, 38)}"));
            }
            if (SinCierre) continue;
            var cargos = c.Movs.Where(m => m.Cargo).ToList();
            var abonos = c.Movs.Where(m => !m.Cargo).ToList();
            var final = c.SaldoInicial + abonos.Sum(m => m.Importe) - cargos.Sum(m => m.Importe) + AjusteSaldoFinal;
            r.Add(Linea($"33{c.Entidad}{c.Oficina}{c.Cuenta}{cargos.Count + AjusteNumeroCargos:00000}{I(cargos.Sum(m => m.Importe))}{abonos.Count:00000}{I(abonos.Sum(m => m.Importe))}" +
                $"{(final < 0 ? "1" : "2")}{I(final)}978    "));
        }
        r.Add(Linea($"88{new string('9', 18)}{r.Count:000000}"));
        return r;
    }

    public byte[] Bytes(Encoding? codificacion = null) => (codificacion ?? Encoding.GetEncoding("ISO-8859-1")).GetBytes(string.Join(FinDeLinea, Registros()) + FinDeLinea);

    private static string Linea(string t) => t.Length > 80 ? throw new InvalidOperationException($"Registro de {t.Length} posiciones: {t}") : t.PadRight(80);
    private static string F(DateTime d) => d.ToString("yyMMdd", CultureInfo.InvariantCulture);
    private static string I(decimal v) => ((long)Math.Round(Math.Abs(v) * 100m)).ToString("00000000000000", CultureInfo.InvariantCulture);
}
