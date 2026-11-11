namespace ProConta.Contabilidad.Mayor;

/// <summary>
/// Cuadre de cuentas del Mayor (contabilidad/mayor 2026-11-11 20:05), sin BD: qué marcas se guardan al marcar unas cuentas como
/// cuadradas y al reiniciar el cuadre. Una marca es de una subcuenta y un ejercicio, y guarda el saldo que tenía la cuenta en
/// ese ejercicio (con su apertura, sin cierres): si después cambia, la cuenta vuelve a estar sin cuadrar.
/// </summary>
public static class CuadresMayor
{
    /// <summary>Umbral de «Reiniciar el cuadre» cuando la configuración no tiene otro (el del ProConta antiguo).</summary>
    public const decimal UmbralPredeterminado = 0.05m;

    /// <summary>Marcas de unas subcuentas en cada ejercicio elegido en que tienen movimientos.</summary>
    public static IReadOnlyList<CuadreCuenta> Marcas(IEnumerable<string> cuentas, IReadOnlyList<EjercicioMayor> ejercicios,
        IReadOnlyDictionary<(string Cuenta, int IdSociedadEjercicio), (decimal Saldo, int Apuntes)> saldos, string? comentario, string operador, DateTime ahora) =>
        cuentas.Distinct(StringComparer.Ordinal)
            .SelectMany(c => ejercicios.Where(e => saldos.TryGetValue((c, e.IdSociedadEjercicio), out var x) && x.Apuntes > 0)
                .Select(e => new CuadreCuenta(e.IdSociedadEjercicio, c, Math.Round(saldos[(c, e.IdSociedadEjercicio)].Saldo, 2), comentario, operador, ahora)))
            .ToList();

    /// <summary>
    /// Reiniciar el cuadre (el «Reinicia cuadre del Mayor» del antiguo, con el umbral parametrizado): en cada ejercicio, las
    /// subcuentas con movimientos y saldo de hasta ±<paramref name="umbral"/> que no estén ya cuadradas se marcan; con
    /// <paramref name="desmarcarResto"/>, se quitan antes las marcas de las demás cuentas de esos ejercicios.
    /// </summary>
    public static (IReadOnlyList<CuadreCuenta> Marcar, IReadOnlyList<(int IdSociedadEjercicio, string Cuenta)> Quitar) Reiniciar(
        IReadOnlyList<EjercicioMayor> ejercicios, IReadOnlyDictionary<(string Cuenta, int IdSociedadEjercicio), (decimal Saldo, int Apuntes)> saldos,
        IReadOnlyCollection<string> subcuentas, IEnumerable<CuadreCuenta> actuales, decimal umbral, bool desmarcarResto, string operador, DateTime ahora)
    {
        umbral = Math.Abs(umbral);
        var ids = ejercicios.Select(e => e.IdSociedadEjercicio).ToHashSet();
        var marcas = actuales.Where(c => ids.Contains(c.IdSociedadEjercicio)).GroupBy(c => (c.Cuenta, c.IdSociedadEjercicio)).ToDictionary(g => g.Key, g => g.First());
        var esSubcuenta = subcuentas.ToHashSet(StringComparer.Ordinal);
        var marcar = new List<CuadreCuenta>();
        var quitar = new List<(int, string)>();
        foreach (var ((cuenta, ej), (saldo, apuntes)) in saldos)
        {
            if (!ids.Contains(ej) || apuntes == 0 || !esSubcuenta.Contains(cuenta)) continue;
            var dentro = Math.Abs(Math.Round(saldo, 2)) <= umbral;
            var vigente = marcas.TryGetValue((cuenta, ej), out var m) && Math.Abs(m.Saldo - saldo) < SelectorCuentasMayor.ToleranciaCuadre;
            if (dentro && !vigente)
                marcar.Add(new CuadreCuenta(ej, cuenta, Math.Round(saldo, 2), $"Reinicio del cuadre (saldo de hasta ±{CalculadoraMayor.Importe(umbral)})", operador, ahora));
        }
        if (desmarcarResto)
        {
            var marcadas = marcar.Select(m => (m.Cuenta, m.IdSociedadEjercicio)).ToHashSet();
            foreach (var (k, m) in marcas)
            {
                var saldo = saldos.TryGetValue(k, out var x) ? x.Saldo : 0m;
                if (!marcadas.Contains(k) && Math.Abs(Math.Round(saldo, 2)) > umbral) quitar.Add((m.IdSociedadEjercicio, m.Cuenta));
            }
        }
        return (marcar, quitar);
    }
}
