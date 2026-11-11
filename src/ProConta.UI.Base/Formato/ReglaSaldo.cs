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
/// Color del saldo según el grupo del PGC (docs/plataforma/ui-base.md, decisiones 2026-10-31 09:58 y 2026-11-11 19:20):
/// verde es bueno para la sociedad y rojo malo. Clientes y deudores (43 y 44): deudor en contra (nos deben, pendiente de cobro)
/// y acreedor a favor. Resto de los grupos 4 y 5: deudor a favor y acreedor en contra (proveedor o Hacienda a los que debemos).
/// Grupos 6 a 9: deudor en contra (gasto) y acreedor a favor (ingreso). Grupos 1, 2 y 3: sin color.
/// Si se mezclan cuentas de varios grupos, o clientes con otras cuentas del grupo 4, sin color.
/// </summary>
public static class ReglaSaldo
{
    /// <summary>Clave de clientes y deudores (subgrupos 43 y 44), con el sentido invertido respecto al resto del grupo 4.</summary>
    public const string ClaveCobro = "43";

    /// <summary>True si la cuenta es de clientes o deudores (subgrupos 43 y 44).</summary>
    public static bool EsDeCobro(string? cuenta) =>
        cuenta is not null && (cuenta.StartsWith("43", StringComparison.Ordinal) || cuenta.StartsWith("44", StringComparison.Ordinal));

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

    /// <summary>
    /// Sentido del saldo de una cuenta por su código o por una clave de ClaveComun
    /// («43» para clientes y deudores; si no, el grupo es su primer dígito).
    /// </summary>
    public static SentidoSaldo Sentido(string? cuenta, decimal saldo)
    {
        if (string.IsNullOrEmpty(cuenta)) return SentidoSaldo.Neutro;
        var sentido = Sentido(cuenta[0], saldo);
        if (!EsDeCobro(cuenta)) return sentido;
        return sentido switch
        {
            SentidoSaldo.AFavor => SentidoSaldo.EnContra,
            SentidoSaldo.EnContra => SentidoSaldo.AFavor,
            _ => sentido
        };
    }

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

    /// <summary>
    /// Clave del color del saldo de un conjunto de cuentas, para Sentido(string, saldo): «43» si todas son de clientes
    /// o deudores, el grupo común si no, y null si se mezclan grupos o clientes con otras cuentas del grupo 4.
    /// </summary>
    public static string? ClaveComun(IEnumerable<string> cuentas)
    {
        char? g = null;
        bool? cobro = null;
        foreach (var c in cuentas)
        {
            if (string.IsNullOrEmpty(c)) continue;
            var esCobro = EsDeCobro(c);
            if (g is null) { g = c[0]; cobro = esCobro; }
            else if (g != c[0] || cobro != esCobro) return null;
        }
        return g is null ? null : cobro == true ? ClaveCobro : g.Value.ToString();
    }

    /// <summary>Texto de ayuda del saldo: «Saldo deudor», «Saldo acreedor» o «Saldo cero».</summary>
    public static string Descripcion(decimal saldo)
    {
        var s = Math.Round(saldo, 2);
        return s > 0 ? "Saldo deudor" : s < 0 ? "Saldo acreedor" : "Saldo cero";
    }
}
