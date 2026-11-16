namespace ProConta.Implantacion.ContaSol;

/// <summary>
/// Subcuentas de IVA del 0 % al importar de ContaSol (organizacion/sociedades-ejercicios 2026-11-13 20:55): en ProContaVF la
/// cuenta de IVA es la serie más el porcentaje con tres cifras, así que el 0 % termina en 000 y no en 001.
/// - 4720001 → 4720000, 4770001 → 4770000, 4725001..4728001 → 4725000..4728000 y 4775001..4778001 → 4775000..4778000, en el
///   plan, los apuntes, los libros de IVA y los saldos de control.
/// - Las antiguas 4720000 y 4770000 de ContaSol (IVA al 16 %) no se importan. Si en el ejercicio tienen apuntes, la importación
///   se detiene: hay que moverlos en ContaSol a la 4720021 o 4770021.
/// - No son del 0 % y no cambian: 4721001..4724001 (la 4724001 es la de importación), 4729001 (regularización de prorrata),
///   4771001..4774001 (la 4774001 es la de exportación) y 4779001.
/// </summary>
public static class IvaCeroContaSol
{
    /// <summary>Código de la incidencia que detiene la importación de todos los ejercicios.</summary>
    public const string CodigoIva16ConApuntes = "ContaSol.Iva16ConApuntes";

    /// <summary>Las antiguas subcuentas del IVA al 16 % de ContaSol, que pasan a ser las del 0 %.</summary>
    public static readonly IReadOnlyList<string> CuentasIva16 = ["4720000", "4770000"];

    /// <summary>El código nuevo de una subcuenta del IVA del 0 %, o el mismo código si no lo es.</summary>
    public static string Renombrar(string codigo)
    {
        var c = codigo.Trim();
        return c.Length == 7 && c.All(char.IsAsciiDigit) && c.StartsWith("47", StringComparison.Ordinal) && c[2] is '2' or '7'
            && c[3] is '0' or '5' or '6' or '7' or '8' && c.EndsWith("001", StringComparison.Ordinal)
            ? c[..4] + "000"
            : c;
    }

    /// <summary>A qué cuenta del 21 % hay que mover en ContaSol los apuntes de una antigua del 16 %.</summary>
    public static string CuentaDestino21(string cuenta16) => cuenta16[..4] + "021";

    /// <summary>
    /// Aplica el renombrado a lo leído de un ejercicio. Las incidencias que devuelve son las que hay que anotar: una informativa
    /// por cada cuenta del 16 % que se descarta o del 0 % que se renombra y una bloqueante (fase «destino», que impide importar)
    /// por cada cuenta del 16 % con apuntes.
    /// </summary>
    public static (OrigenContaSol Origen, IReadOnlyList<Incidencia> Incidencias) Aplicar(OrigenContaSol origen)
    {
        var incidencias = new List<Incidencia>();
        var ejercicio = origen.Empresa.Ejercicio;
        var viejas = CuentasIva16.ToHashSet(StringComparer.Ordinal);
        foreach (var cuenta in CuentasIva16)
        {
            var apuntes = origen.Apuntes.Count(a => a.Cuenta.Trim() == cuenta);
            if (apuntes > 0)
                incidencias.Add(new Incidencia("destino", "F_APU", cuenta, "CUEAPU", CodigoIva16ConApuntes, Severidad.Bloqueante,
                    $"En el ejercicio {ejercicio}, la cuenta {cuenta} tiene {apuntes} {(apuntes == 1 ? "apunte" : "apuntes")}. En ContaSol era la del IVA al 16 %, y en " +
                    $"ProContaVF la {cuenta} es la del IVA al 0 % (la terminación es el porcentaje con tres cifras), así que no la importo con apuntes y detengo la importación. " +
                    $"Mueve esos apuntes en ContaSol a la {CuentaDestino21(cuenta)} (si son del 21 %) o a la cuenta de su tipo, y vuelve a importar.", cuenta));
            else if (origen.Cuentas.Any(c => c.Codigo.Trim() == cuenta))
                incidencias.Add(new Incidencia(PlanificadorContaSol.FaseInspeccion, "F_MAE", cuenta, "CODMAE", "ContaSol.Iva16Descartada", Severidad.Informativa,
                    $"La cuenta {cuenta} (IVA al 16 % en ContaSol) no tiene apuntes en {ejercicio}: no la importo, porque en ProContaVF la {cuenta} es la del IVA al 0 %."));
        }
        var renombradas = origen.Cuentas.Select(c => c.Codigo.Trim()).Where(c => Renombrar(c) != c).Distinct().Order(StringComparer.Ordinal).ToList();
        if (renombradas.Count > 0)
            incidencias.Add(new Incidencia(PlanificadorContaSol.FaseInspeccion, "F_MAE", null, "CODMAE", "ContaSol.IvaCeroRenombrada", Severidad.Informativa,
                $"Las subcuentas del IVA al 0 % pasan a terminar en 000: {string.Join(", ", renombradas.Select(c => $"{c} → {Renombrar(c)}"))}."));

        // Si la del 16 % tiene apuntes, la importación se detiene: se deja tal cual para que los apuntes no se mezclen con los del 0 %.
        var conApuntes = incidencias.Where(i => i.Codigo == CodigoIva16ConApuntes).Select(i => i.Clave!).ToHashSet(StringComparer.Ordinal);
        string Nuevo(string codigo) => viejas.Contains(codigo.Trim()) ? codigo : Renombrar(codigo);
        var cuentas = origen.Cuentas.Where(c => !viejas.Contains(c.Codigo.Trim()) || conApuntes.Contains(c.Codigo.Trim()))
            .Select(c => Renombrar(c.Codigo) is var n && n != c.Codigo.Trim() ? c with { Codigo = n } : c).ToList();
        var saldos = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var (cuenta, saldo) in origen.SaldosControl)
        {
            if (viejas.Contains(cuenta.Trim()) && !conApuntes.Contains(cuenta.Trim())) continue;
            var n = Nuevo(cuenta);
            saldos[n] = saldos.GetValueOrDefault(n) + saldo;
        }
        return (origen with
        {
            Cuentas = cuentas,
            Apuntes = [.. origen.Apuntes.Select(a => Nuevo(a.Cuenta) is var n && n != a.Cuenta ? a with { Cuenta = n } : a)],
            RegistrosIva = [.. origen.RegistrosIva.Select(r => Nuevo(r.Cuenta) is var n && n != r.Cuenta ? r with { Cuenta = n } : r)],
            SaldosControl = saldos,
        }, incidencias);
    }

    /// <summary>Texto para el operador cuando algún ejercicio tiene apuntes en una antigua cuenta del 16 %.</summary>
    public static string MotivoParada(IEnumerable<Incidencia> incidencias) =>
        "No importo ningún ejercicio: " + string.Join(" ", incidencias.Where(i => i.Codigo == CodigoIva16ConApuntes).Select(i => i.Explicacion));
}
