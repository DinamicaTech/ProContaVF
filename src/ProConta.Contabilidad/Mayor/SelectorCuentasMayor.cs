using System.Globalization;

namespace ProConta.Contabilidad.Mayor;

/// <summary>Filtros del selector de cuentas del Mayor (los del antiguo, calculados sobre los ejercicios elegidos).</summary>
/// <param name="SoloSubcuentas">Solo las cuentas que admiten apuntes.</param>
/// <param name="ConMovimientos">Solo las que tienen algún apunte en el periodo (de verdad, no por tener fila de saldos).</param>
/// <param name="ConSaldo">Saldo distinto de cero, con la apertura.</param>
/// <param name="ConSaldoSinApertura">Saldo de los movimientos del periodo distinto de cero, sin la apertura.</param>
/// <param name="Texto">Código que empieza por el texto, o descripción que lo contiene (sin distinguir mayúsculas ni acentos).</param>
public sealed record FiltroCuentasMayor(bool SoloSubcuentas = true, bool ConMovimientos = true, bool ConSaldo = false, bool ConSaldoSinApertura = false, string? Texto = null);

/// <summary>
/// Catálogo del selector: cada cuenta del plan de los ejercicios elegidos (y las que tienen apuntes sin estar en el plan) con
/// su saldo sin apertura, saldo y apuntes en esos ejercicios. La apertura de un ejercicio cuyo anterior también está elegido no
/// suma (su saldo ya viene del anterior), igual que en la tabla del Mayor. Los cierres no se leen.
/// </summary>
public static class SelectorCuentasMayor
{
    public static IReadOnlyList<CuentaMayor> Catalogo(IEnumerable<SumaCuenta> sumas, IEnumerable<PlanCuenta> plan, IReadOnlyList<EjercicioMayor> elegidos)
    {
        var anioDe = elegidos.ToDictionary(e => e.IdSociedadEjercicio, e => e.Ejercicio);
        var anios = elegidos.Select(e => e.Ejercicio).ToHashSet();
        var cuentas = new Dictionary<string, (string Nombre, bool Imputable, int Anio)>(StringComparer.Ordinal);
        foreach (var p in plan)
        {
            if (p.Codigo.Length == 0 || !anioDe.TryGetValue(p.IdSociedadEjercicio, out var anio)) continue;
            // El nombre y el tipo de la cuenta, los del ejercicio más reciente de la vista.
            if (!cuentas.TryGetValue(p.Codigo, out var c) || c.Anio < anio) cuentas[p.Codigo] = (p.Nombre, p.EsImputable, anio);
        }
        var saldos = new Dictionary<string, (decimal SinApertura, decimal Saldo, int Apuntes)>(StringComparer.Ordinal);
        foreach (var s in sumas)
        {
            if (s.TipoAsiento == TiposAsientoMayor.Cierre || !anioDe.TryGetValue(s.IdSociedadEjercicio, out var anio)) continue;
            var apertura = s.TipoAsiento == TiposAsientoMayor.Apertura;
            if (apertura && anios.Contains(anio - 1)) continue;
            var x = saldos.GetValueOrDefault(s.Cuenta);
            var importe = s.Debe - s.Haber;
            saldos[s.Cuenta] = (x.SinApertura + (apertura ? 0m : importe), x.Saldo + importe, x.Apuntes + s.Apuntes);
            if (!cuentas.ContainsKey(s.Cuenta)) cuentas[s.Cuenta] = ("", true, anio);
        }
        return cuentas.OrderBy(c => c.Key, StringComparer.Ordinal).Select(c =>
        {
            var x = saldos.GetValueOrDefault(c.Key);
            if (!c.Value.Imputable)
            {
                // Una cuenta de grupo resume sus subcuentas.
                var hijas = saldos.Where(h => h.Key.Length > c.Key.Length && h.Key.StartsWith(c.Key, StringComparison.Ordinal)).Select(h => h.Value).ToList();
                x = (hijas.Sum(h => h.SinApertura), hijas.Sum(h => h.Saldo), hijas.Sum(h => h.Apuntes));
            }
            return new CuentaMayor(c.Key, c.Value.Nombre, x.SinApertura, x.Saldo, x.Apuntes, c.Value.Imputable);
        }).ToList();
    }

    /// <summary>Las cuentas del catálogo que pasan los filtros, en orden de código.</summary>
    public static IReadOnlyList<CuentaMayor> Filtrar(IReadOnlyList<CuentaMayor> catalogo, FiltroCuentasMayor f)
    {
        var ci = CultureInfo.GetCultureInfo("es-ES").CompareInfo;
        const CompareOptions op = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        var t = f.Texto?.Trim() ?? "";
        return catalogo.Where(c =>
            (!f.SoloSubcuentas || c.EsSubcuenta) &&
            (!f.ConMovimientos || c.Apuntes > 0) &&
            (!f.ConSaldo || Math.Round(c.Saldo, 2) != 0) &&
            (!f.ConSaldoSinApertura || Math.Round(c.SaldoSinApertura, 2) != 0) &&
            (t.Length == 0 || c.Codigo.StartsWith(t, StringComparison.Ordinal) || ci.IndexOf(c.Nombre, t, op) >= 0)).ToList();
    }

    /// <summary>
    /// Qué cuentas entran al pedir un código: si es una subcuenta del catálogo, solo ella; si no (código corto o de grupo),
    /// todas las que empiezan por él.
    /// </summary>
    public static bool EsExacta(string codigo, IReadOnlyList<CuentaMayor> catalogo) =>
        catalogo.Any(c => c.EsSubcuenta && string.Equals(c.Codigo, codigo, StringComparison.Ordinal));
}
