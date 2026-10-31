namespace ProConta.UI.Base.Formato;

/// <summary>Cómo se ve un saldo para la sociedad.</summary>
public enum SentidoSaldo
{
    /// <summary>Sin color: saldo cero, grupos 1 a 3 o cuentas de varios grupos.</summary>
    Neutro,
    /// <summary>A favor de la sociedad: verde.</summary>
    AFavor,
    /// <summary>En contra de la sociedad: rojo.</summary>
    EnContra
}

/// <summary>
/// Color del saldo según el grupo del PGC (docs/plataforma/ui-base.md, decisión 2026-10-31 09:58):
/// grupos 4 y 5, deudor a favor y acreedor en contra; grupos 6 a 9, deudor en contra (gasto) y acreedor a favor (ingreso);
/// grupos 1, 2 y 3, sin color. Si se mezclan cuentas de varios grupos, sin color.
/// </summary>
public static class ReglaSaldo
{
    /// <summary>Sentido de un saldo (deudor positivo, acreedor negativo) de una cuenta del grupo indicado.</summary>
    public static SentidoSaldo Sentido(char grupo, decimal saldo)
    {
        var s = Math.Round(saldo, 2);
        if (s == 0) return SentidoSaldo.Neutro;
        return grupo switch
        {
            '4' or '5' => s > 0 ? SentidoSaldo.AFavor : SentidoSaldo.EnContra,
            '6' or '7' or '8' or '9' => s > 0 ? SentidoSaldo.EnContra : SentidoSaldo.AFavor,
            _ => SentidoSaldo.Neutro
        };
    }

    /// <summary>Sentido del saldo de una cuenta por su código (el grupo es su primer dígito).</summary>
    public static SentidoSaldo Sentido(string? cuenta, decimal saldo) =>
        string.IsNullOrEmpty(cuenta) ? SentidoSaldo.Neutro : Sentido(cuenta[0], saldo);

    /// <summary>Grupo común de un conjunto de cuentas, o null si hay cuentas de varios grupos.</summary>
    public static char? GrupoComun(IEnumerable<string> cuentas)
    {
        char? g = null;
        foreach (var c in cuentas)
        {
            if (string.IsNullOrEmpty(c)) continue;
            if (g is null) g = c[0];
            else if (g != c[0]) return null;
        }
        return g;
    }

    /// <summary>Texto de ayuda del saldo: «Saldo deudor», «Saldo acreedor» o «Saldo cero».</summary>
    public static string Descripcion(decimal saldo)
    {
        var s = Math.Round(saldo, 2);
        return s > 0 ? "Saldo deudor" : s < 0 ? "Saldo acreedor" : "Saldo cero";
    }
}
