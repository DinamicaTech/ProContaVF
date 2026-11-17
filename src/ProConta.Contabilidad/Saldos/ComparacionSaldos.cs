namespace ProConta.Contabilidad.Saldos;

/// <summary>Caché de una cuenta del ejercicio tal como está guardada (CuentasContables).</summary>
public sealed record CacheCuenta(string Codigo, string Nombre, decimal SaldoInicial, decimal SaldoActual, int NumeroApuntes);

/// <summary>Suma de los apuntes de un código de cuenta en los asientos del ejercicio (Debe menos Haber).</summary>
public sealed record MovimientosCuenta(string Codigo, decimal Saldo, int NumeroApuntes);

/// <summary>Subcuenta cuya caché no coincide con sus apuntes.</summary>
public sealed record DiferenciaSaldo(string Codigo, string Nombre, decimal SaldoGuardado, decimal SaldoApuntes, int ApuntesGuardados, int ApuntesReales)
{
    public decimal Diferencia => SaldoGuardado - SaldoApuntes;
}

/// <summary>Código de cuenta con apuntes en el ejercicio que no está en su plan de cuentas: su saldo no cabe en ninguna caché.</summary>
public sealed record CuentaSinAlta(string Codigo, decimal Saldo, int NumeroApuntes);

/// <summary>Resultado de comparar la caché de saldos de un ejercicio con sus apuntes.</summary>
public sealed record ComparacionSaldosEjercicio(int CuentasRevisadas, IReadOnlyList<DiferenciaSaldo> Diferencias, IReadOnlyList<CuentaSinAlta> SinAlta)
{
    public bool Cuadra => Diferencias.Count == 0;
}

/// <summary>
/// Comparación sin base de datos de la caché de saldos (CuentasContables.SaldoActual y NumeroApuntes) con los apuntes del ejercicio
/// (contabilidad/procesos/recalcular-saldos): el saldo correcto es SaldoInicial más Debe menos Haber de sus apuntes, y el número de
/// apuntes, cuántos tiene. Los códigos se comparan sin espacios a los lados; si un código aparece varias veces, se suma.
/// </summary>
public static class ComparacionSaldos
{
    /// <summary>Agrupa los movimientos por código sin espacios (el mismo código con espacios distintos es la misma cuenta).</summary>
    public static IReadOnlyDictionary<string, (decimal Saldo, int N)> Agrupar(IEnumerable<MovimientosCuenta> movimientos) =>
        movimientos.Where(m => !string.IsNullOrWhiteSpace(m.Codigo))
            .GroupBy(m => m.Codigo.Trim(), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (g.Sum(m => m.Saldo), g.Sum(m => m.NumeroApuntes)), StringComparer.Ordinal);

    public static ComparacionSaldosEjercicio Comparar(IEnumerable<CacheCuenta> cuentas, IEnumerable<MovimientosCuenta> movimientos)
    {
        var suma = Agrupar(movimientos);
        var lista = cuentas.ToList();
        var conAlta = new HashSet<string>(lista.Select(c => c.Codigo.Trim()), StringComparer.Ordinal);
        var diferencias = lista
            .Select(c =>
            {
                var (saldo, n) = suma.GetValueOrDefault(c.Codigo.Trim());
                return new DiferenciaSaldo(c.Codigo.Trim(), c.Nombre, c.SaldoActual, c.SaldoInicial + saldo, c.NumeroApuntes, n);
            })
            .Where(d => d.SaldoGuardado != d.SaldoApuntes || d.ApuntesGuardados != d.ApuntesReales)
            .OrderBy(d => d.Codigo, StringComparer.Ordinal)
            .ToList();
        var sinAlta = suma.Where(kv => !conAlta.Contains(kv.Key))
            .Select(kv => new CuentaSinAlta(kv.Key, kv.Value.Saldo, kv.Value.N))
            .OrderBy(c => c.Codigo, StringComparer.Ordinal)
            .ToList();
        return new ComparacionSaldosEjercicio(lista.Count, diferencias, sinAlta);
    }
}
