using System.Globalization;

namespace ProConta.Contabilidad.Mayor;

/// <summary>Filtros del selector de cuentas del Mayor (los del antiguo, calculados sobre los ejercicios elegidos).</summary>
/// <param name="SoloSubcuentas">Solo las cuentas que admiten apuntes.</param>
/// <param name="ConMovimientos">Solo las que tienen algún apunte en el periodo (de verdad, no por tener fila de saldos).</param>
/// <param name="ConSaldo">Saldo distinto de cero, con la apertura.</param>
/// <param name="ConSaldoSinApertura">Saldo de los movimientos del periodo distinto de cero, sin la apertura.</param>
/// <param name="Texto">Código que empieza por el texto, o descripción que lo contiene (sin distinguir mayúsculas ni acentos).</param>
/// <param name="NoCuadradas">Solo las que tienen movimientos y no están cuadradas en algún ejercicio elegido.</param>
public sealed record FiltroCuentasMayor(bool SoloSubcuentas = true, bool ConMovimientos = true, bool ConSaldo = false, bool ConSaldoSinApertura = false, string? Texto = null,
    bool NoCuadradas = false);

/// <summary>
/// Catálogo del selector: cada cuenta del plan de los ejercicios elegidos (y las que tienen apuntes sin estar en el plan) con
/// su saldo sin apertura, saldo y apuntes en esos ejercicios. La apertura de un ejercicio cuyo anterior también está elegido no
/// suma (su saldo ya viene del anterior), igual que en la tabla del Mayor. Los cierres no se leen.
/// </summary>
public static class SelectorCuentasMayor
{
    /// <summary>Diferencia de saldo a partir de la cual una cuenta marcada deja de estar cuadrada.</summary>
    public const decimal ToleranciaCuadre = 0.005m;

    /// <summary>Saldo de cada cuenta en cada ejercicio, con su propia apertura y sin cierres: el que se guarda al marcarla como cuadrada.</summary>
    public static IReadOnlyDictionary<(string Cuenta, int IdSociedadEjercicio), (decimal Saldo, int Apuntes)> SaldosPorEjercicio(IEnumerable<SumaCuenta> sumas)
    {
        var d = new Dictionary<(string, int), (decimal, int)>();
        foreach (var s in sumas.Where(s => s.TipoAsiento != TiposAsientoMayor.Cierre))
        {
            var k = (s.Cuenta, s.IdSociedadEjercicio);
            var x = d.GetValueOrDefault(k);
            d[k] = (x.Item1 + s.Debe - s.Haber, x.Item2 + s.Apuntes);
        }
        return d;
    }

    /// <summary>
    /// Estado del cuadre de una subcuenta: cuadrada si en cada ejercicio elegido con movimientos está marcada con el mismo saldo
    /// que tiene ahora; la ayuda dice de cada ejercicio cuándo y quién la marcó, o por qué ya no cuadra.
    /// </summary>
    public static (EstadoCuadre Estado, string? Ayuda) Cuadre(string cuenta, IReadOnlyList<EjercicioMayor> elegidos,
        IReadOnlyDictionary<(string Cuenta, int IdSociedadEjercicio), (decimal Saldo, int Apuntes)> saldos,
        IReadOnlyDictionary<(string Cuenta, int IdSociedadEjercicio), CuadreCuenta> cuadres)
    {
        var lineas = new List<string>();
        var hay = false;
        var todas = true;
        foreach (var e in elegidos.OrderBy(e => e.Ejercicio))
        {
            if (!saldos.TryGetValue((cuenta, e.IdSociedadEjercicio), out var x) || x.Apuntes == 0) continue;
            hay = true;
            if (!cuadres.TryGetValue((cuenta, e.IdSociedadEjercicio), out var c)) { todas = false; lineas.Add($"{e.Ejercicio}: sin cuadrar."); continue; }
            var quien = $"marcada el {c.FechaHora:dd/MM/yyyy} por {c.Operador}" + (string.IsNullOrWhiteSpace(c.Comentario) ? "" : $" («{c.Comentario}»)");
            if (Math.Abs(x.Saldo - c.Saldo) < ToleranciaCuadre) lineas.Add($"{e.Ejercicio}: cuadrada, {quien}.");
            else
            {
                todas = false;
                lineas.Add($"{e.Ejercicio}: ya no cuadra, {quien} con saldo {CalculadoraMayor.Importe(c.Saldo)}; ahora es {CalculadoraMayor.Importe(x.Saldo)}.");
            }
        }
        return hay ? (todas ? EstadoCuadre.Cuadrada : EstadoCuadre.NoCuadrada, string.Join(Environment.NewLine, lineas)) : (EstadoCuadre.SinMovimientos, null);
    }

    public static IReadOnlyList<CuentaMayor> Catalogo(IEnumerable<SumaCuenta> sumas, IEnumerable<PlanCuenta> plan, IReadOnlyList<EjercicioMayor> elegidos,
        IEnumerable<CuadreCuenta>? cuadres = null)
    {
        sumas = sumas as IReadOnlyCollection<SumaCuenta> ?? sumas.ToList();
        var porEjercicio = SaldosPorEjercicio(sumas);
        var marcas = (cuadres ?? []).GroupBy(c => (c.Cuenta, c.IdSociedadEjercicio)).ToDictionary(g => g.Key, g => g.First());
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
                // Una cuenta de grupo resume sus subcuentas; está cuadrada si lo están todas las que tienen movimientos.
                var hijas = saldos.Where(h => h.Key.Length > c.Key.Length && h.Key.StartsWith(c.Key, StringComparison.Ordinal)).ToList();
                x = (hijas.Sum(h => h.Value.SinApertura), hijas.Sum(h => h.Value.Saldo), hijas.Sum(h => h.Value.Apuntes));
                var estados = hijas.Select(h => Cuadre(h.Key, elegidos, porEjercicio, marcas).Estado).Where(e => e != EstadoCuadre.SinMovimientos).ToList();
                var estado = estados.Count == 0 ? EstadoCuadre.SinMovimientos : estados.All(e => e == EstadoCuadre.Cuadrada) ? EstadoCuadre.Cuadrada : EstadoCuadre.NoCuadrada;
                var sin = estados.Count(e => e == EstadoCuadre.NoCuadrada);
                return new CuentaMayor(c.Key, c.Value.Nombre, x.SinApertura, x.Saldo, x.Apuntes, false, estado,
                    estados.Count == 0 ? null : sin == 0 ? "Todas sus subcuentas con movimientos están cuadradas." : $"{sin} de {estados.Count} subcuentas con movimientos sin cuadrar.");
            }
            var (cuadre, ayuda) = Cuadre(c.Key, elegidos, porEjercicio, marcas);
            return new CuentaMayor(c.Key, c.Value.Nombre, x.SinApertura, x.Saldo, x.Apuntes, true, cuadre, ayuda);
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
            (!f.NoCuadradas || c.Cuadre == EstadoCuadre.NoCuadrada) &&
            (t.Length == 0 || c.Codigo.StartsWith(t, StringComparison.Ordinal) || ci.IndexOf(c.Nombre, t, op) >= 0)).ToList();
    }

    /// <summary>
    /// Qué cuentas entran al pedir un código: si es una subcuenta del catálogo, solo ella; si no (código corto o de grupo),
    /// todas las que empiezan por él.
    /// </summary>
    public static bool EsExacta(string codigo, IReadOnlyList<CuentaMayor> catalogo) =>
        catalogo.Any(c => c.EsSubcuenta && string.Equals(c.Codigo, codigo, StringComparison.Ordinal));
}
