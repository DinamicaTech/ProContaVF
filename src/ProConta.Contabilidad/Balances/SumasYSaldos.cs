using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Balances;

/// <summary>Nivel de las filas del balance de sumas y saldos: grupo (1 dígito), subgrupo (2), cuenta (3) o subcuenta (el código del apunte).</summary>
public enum NivelSumasYSaldos : byte
{
    Grupo = 1,
    Subgrupo = 2,
    Cuenta = 3,
    Subcuenta = 9,
}

/// <summary>
/// Parámetros del balance de sumas y saldos (contabilidad/balances/sumas-y-saldos): los apuntes contabilizados del ejercicio con fecha contable
/// entre <paramref name="Desde"/> y <paramref name="Hasta"/>, ambos incluidos, en los niveles pedidos. El asiento de cierre solo entra si
/// <paramref name="IncluirCierre"/> (con él todos los saldos quedan a cero); la apertura y la regularización entran siempre que caigan en las fechas.
/// </summary>
public sealed record ParametrosSumasYSaldos(DateTime Desde, DateTime Hasta, IReadOnlyCollection<NivelSumasYSaldos> Niveles, bool IncluirCierre = false)
{
    /// <summary>Por defecto: todo el ejercicio (hasta hoy si está en curso), cuentas y subcuentas, sin el cierre.</summary>
    public static ParametrosSumasYSaldos PorDefecto(int ejercicio, DateTime hoy)
    {
        var fin = new DateTime(ejercicio, 12, 31);
        return new(new DateTime(ejercicio, 1, 1), hoy.Year == ejercicio && hoy.Date < fin ? hoy.Date : fin, [NivelSumasYSaldos.Cuenta, NivelSumasYSaldos.Subcuenta]);
    }

    /// <summary>Qué falla en los parámetros para el ejercicio dado, con cómo arreglarlo; null si son válidos.</summary>
    public string? Error(int ejercicio)
    {
        if (Niveles.Count == 0) return "Marca al menos un nivel (grupo, subgrupo, cuenta o subcuenta).";
        if (Desde.Date > Hasta.Date) return $"La fecha «Desde» ({Desde:dd/MM/yyyy}) es posterior a «Hasta» ({Hasta:dd/MM/yyyy}): cámbialas de orden.";
        if (Desde.Year != ejercicio || Hasta.Year != ejercicio)
            return $"Las dos fechas tienen que ser del ejercicio de trabajo ({ejercicio}): del 01/01/{ejercicio} al 31/12/{ejercicio}. Para otro año, cambia antes el ejercicio de trabajo.";
        return null;
    }
}

/// <summary>Sumas del Debe y del Haber de una subcuenta entre las fechas pedidas (lo que se lee de los apuntes).</summary>
public sealed record MovimientoSubcuenta(string Cuenta, decimal Debe, decimal Haber);

/// <summary>Fila del balance: el código, su nombre, su nivel, las sumas y el saldo (deudor si Debe &gt; Haber, acreedor si al revés).</summary>
public sealed record FilaSumasYSaldos(string Cuenta, string Nombre, NivelSumasYSaldos Nivel, decimal Debe, decimal Haber)
{
    public decimal SaldoDeudor => Debe > Haber ? Debe - Haber : 0m;
    public decimal SaldoAcreedor => Haber > Debe ? Haber - Debe : 0m;
}

/// <summary>
/// Balance de sumas y saldos calculado. Las filas van en orden de código, cada nivel antes de sus hijos. Los totales son la suma de las filas del
/// nivel más detallado que se muestra, así que cuadran con lo que se ve; las sumas del Debe y del Haber son además las de todos los apuntes.
/// </summary>
public sealed record ResultadoSumasYSaldos(
    string RazonSocial, int Ejercicio, ParametrosSumasYSaldos Parametros, IReadOnlyList<FilaSumasYSaldos> Filas,
    decimal TotalDebe, decimal TotalHaber, decimal TotalSaldoDeudor, decimal TotalSaldoAcreedor, int NumeroSubcuentas, DateTime Generado)
{
    /// <summary>Nivel más detallado de los pedidos (el que suman los totales).</summary>
    public NivelSumasYSaldos NivelDetalle => Parametros.Niveles.Max();

    /// <summary>Texto de los niveles pedidos («cuentas y subcuentas»).</summary>
    public string TextoNiveles
    {
        get
        {
            var n = Parametros.Niveles.Distinct().Order().Select(x => x switch
            {
                NivelSumasYSaldos.Grupo => "grupos",
                NivelSumasYSaldos.Subgrupo => "subgrupos",
                NivelSumasYSaldos.Cuenta => "cuentas",
                _ => "subcuentas",
            }).ToList();
            return n.Count == 1 ? n[0] : string.Join(", ", n.Take(n.Count - 1)) + " y " + n[^1];
        }
    }

    /// <summary>Línea de cabecera: periodo, niveles y si entra el cierre.</summary>
    public string Descripcion =>
        $"Ejercicio {Ejercicio} · del {Parametros.Desde:dd/MM/yyyy} al {Parametros.Hasta:dd/MM/yyyy} · {TextoNiveles}" +
        (Parametros.IncluirCierre ? " · con el asiento de cierre" : " · sin el asiento de cierre");
}

/// <summary>Cálculo del balance de sumas y saldos a partir de las sumas de cada subcuenta (sin BD; lo prueban las pruebas unitarias).</summary>
public static class CalculadoraSumasYSaldos
{
    /// <summary>Nombres de los grupos y subgrupos del PGC de pymes (2008), para cuando el plan del ejercicio no los tiene (el plan empieza en 3 dígitos).</summary>
    public static readonly IReadOnlyDictionary<string, string> NombresPgc = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["1"] = "FINANCIACIÓN BÁSICA",
        ["10"] = "CAPITAL",
        ["11"] = "RESERVAS Y OTROS INSTRUMENTOS DE PATRIMONIO",
        ["12"] = "RESULTADOS PENDIENTES DE APLICACIÓN",
        ["13"] = "SUBVENCIONES, DONACIONES Y AJUSTES POR CAMBIOS DE VALOR",
        ["14"] = "PROVISIONES",
        ["15"] = "DEUDAS A LARGO PLAZO CON CARACTERÍSTICAS ESPECIALES",
        ["16"] = "DEUDAS A LARGO PLAZO CON PARTES VINCULADAS",
        ["17"] = "DEUDAS A LARGO PLAZO POR PRÉSTAMOS RECIBIDOS, EMPRÉSTITOS Y OTROS CONCEPTOS",
        ["18"] = "PASIVOS POR FIANZAS, GARANTÍAS Y OTROS CONCEPTOS A LARGO PLAZO",
        ["19"] = "SITUACIONES TRANSITORIAS DE FINANCIACIÓN",
        ["2"] = "ACTIVO NO CORRIENTE",
        ["20"] = "INMOVILIZACIONES INTANGIBLES",
        ["21"] = "INMOVILIZACIONES MATERIALES",
        ["22"] = "INVERSIONES INMOBILIARIAS",
        ["23"] = "INMOVILIZACIONES MATERIALES EN CURSO",
        ["24"] = "INVERSIONES FINANCIERAS A LARGO PLAZO EN PARTES VINCULADAS",
        ["25"] = "OTRAS INVERSIONES FINANCIERAS A LARGO PLAZO",
        ["26"] = "FIANZAS Y DEPÓSITOS CONSTITUIDOS A LARGO PLAZO",
        ["28"] = "AMORTIZACIÓN ACUMULADA DEL INMOVILIZADO",
        ["29"] = "DETERIORO DE VALOR DE ACTIVOS NO CORRIENTES",
        ["3"] = "EXISTENCIAS",
        ["30"] = "COMERCIALES",
        ["31"] = "MATERIAS PRIMAS",
        ["32"] = "OTROS APROVISIONAMIENTOS",
        ["33"] = "PRODUCTOS EN CURSO",
        ["34"] = "PRODUCTOS SEMITERMINADOS",
        ["35"] = "PRODUCTOS TERMINADOS",
        ["36"] = "SUBPRODUCTOS, RESIDUOS Y MATERIALES RECUPERADOS",
        ["39"] = "DETERIORO DE VALOR DE LAS EXISTENCIAS",
        ["4"] = "ACREEDORES Y DEUDORES POR OPERACIONES COMERCIALES",
        ["40"] = "PROVEEDORES",
        ["41"] = "ACREEDORES VARIOS",
        ["43"] = "CLIENTES",
        ["44"] = "DEUDORES VARIOS",
        ["46"] = "PERSONAL",
        ["47"] = "ADMINISTRACIONES PÚBLICAS",
        ["48"] = "AJUSTES POR PERIODIFICACIÓN",
        ["49"] = "DETERIORO DE VALOR DE CRÉDITOS COMERCIALES Y PROVISIONES A CORTO PLAZO",
        ["5"] = "CUENTAS FINANCIERAS",
        ["50"] = "EMPRÉSTITOS, DEUDAS CON CARACTERÍSTICAS ESPECIALES Y OTRAS EMISIONES ANÁLOGAS A CORTO PLAZO",
        ["51"] = "DEUDAS A CORTO PLAZO CON PARTES VINCULADAS",
        ["52"] = "DEUDAS A CORTO PLAZO POR PRÉSTAMOS RECIBIDOS Y OTROS CONCEPTOS",
        ["53"] = "INVERSIONES FINANCIERAS A CORTO PLAZO EN PARTES VINCULADAS",
        ["54"] = "OTRAS INVERSIONES FINANCIERAS A CORTO PLAZO",
        ["55"] = "OTRAS CUENTAS NO BANCARIAS",
        ["56"] = "FIANZAS Y DEPÓSITOS RECIBIDOS Y CONSTITUIDOS A CORTO PLAZO Y AJUSTES POR PERIODIFICACIÓN",
        ["57"] = "TESORERÍA",
        ["59"] = "DETERIORO DEL VALOR DE INVERSIONES FINANCIERAS A CORTO PLAZO Y DE ACTIVOS NO CORRIENTES MANTENIDOS PARA LA VENTA",
        ["6"] = "COMPRAS Y GASTOS",
        ["60"] = "COMPRAS",
        ["61"] = "VARIACIÓN DE EXISTENCIAS",
        ["62"] = "SERVICIOS EXTERIORES",
        ["63"] = "TRIBUTOS",
        ["64"] = "GASTOS DE PERSONAL",
        ["65"] = "OTROS GASTOS DE GESTIÓN",
        ["66"] = "GASTOS FINANCIEROS",
        ["67"] = "PÉRDIDAS PROCEDENTES DE ACTIVOS NO CORRIENTES Y GASTOS EXCEPCIONALES",
        ["68"] = "DOTACIONES PARA AMORTIZACIONES",
        ["69"] = "PÉRDIDAS POR DETERIORO Y OTRAS DOTACIONES",
        ["7"] = "VENTAS E INGRESOS",
        ["70"] = "VENTAS DE MERCADERÍAS, DE PRODUCCIÓN PROPIA, DE SERVICIOS, ETC.",
        ["71"] = "VARIACIÓN DE EXISTENCIAS",
        ["73"] = "TRABAJOS REALIZADOS PARA LA EMPRESA",
        ["74"] = "SUBVENCIONES, DONACIONES Y LEGADOS",
        ["75"] = "OTROS INGRESOS DE GESTIÓN",
        ["76"] = "INGRESOS FINANCIEROS",
        ["77"] = "BENEFICIOS PROCEDENTES DE ACTIVOS NO CORRIENTES E INGRESOS EXCEPCIONALES",
        ["79"] = "EXCESOS Y APLICACIONES DE PROVISIONES Y DE PÉRDIDAS POR DETERIORO",
    };

    /// <summary>Texto del nombre de una subcuenta con apuntes que no está en el plan del ejercicio.</summary>
    public const string FueraDelPlan = "(no está en el plan del ejercicio)";

    /// <summary>
    /// Agrupa las sumas de cada subcuenta en los niveles pedidos. Las subcuentas sin movimiento (Debe y Haber a cero) no salen. Un nivel solo
    /// se forma con los códigos más largos que él (una subcuenta de 3 dígitos no crea fila de cuenta aparte de sí misma).
    /// </summary>
    public static ResultadoSumasYSaldos Calcular(string razonSocial, int ejercicio, ParametrosSumasYSaldos parametros, IEnumerable<MovimientoSubcuenta> movimientos,
        IReadOnlyDictionary<string, string> nombresPlan, DateTime generado)
    {
        var subcuentas = movimientos
            .Where(m => m.Cuenta.Length > 0)
            .GroupBy(m => m.Cuenta, StringComparer.Ordinal)
            .Select(g => (Cuenta: g.Key, Debe: g.Sum(x => x.Debe), Haber: g.Sum(x => x.Haber)))
            .Where(s => s.Debe != 0m || s.Haber != 0m)
            .ToList();

        string Nombre(string codigo, bool esSubcuenta) =>
            nombresPlan.TryGetValue(codigo, out var n) && n.Length > 0 ? n
            : !esSubcuenta && NombresPgc.TryGetValue(codigo, out var p) ? p
            : esSubcuenta ? FueraDelPlan : "";

        var niveles = parametros.Niveles.ToHashSet();
        var filas = new List<FilaSumasYSaldos>();
        foreach (var nivel in new[] { NivelSumasYSaldos.Grupo, NivelSumasYSaldos.Subgrupo, NivelSumasYSaldos.Cuenta })
        {
            if (!niveles.Contains(nivel)) continue;
            var largo = (int)nivel;
            filas.AddRange(subcuentas
                .Where(s => s.Cuenta.Length > largo || (s.Cuenta.Length == largo && !niveles.Contains(NivelSumasYSaldos.Subcuenta)))
                .GroupBy(s => s.Cuenta[..largo], StringComparer.Ordinal)
                .Select(g => new FilaSumasYSaldos(g.Key, Nombre(g.Key, false), nivel, g.Sum(x => x.Debe), g.Sum(x => x.Haber))));
        }
        if (niveles.Contains(NivelSumasYSaldos.Subcuenta))
            filas.AddRange(subcuentas.Select(s => new FilaSumasYSaldos(s.Cuenta, Nombre(s.Cuenta, true), NivelSumasYSaldos.Subcuenta, s.Debe, s.Haber)));

        // Orden ordinal del código: «4» < «40» < «400» < «4000001», así que cada nivel sale antes de sus hijos.
        filas.Sort((a, b) => string.CompareOrdinal(a.Cuenta, b.Cuenta) is var c && c != 0 ? c : ((byte)a.Nivel).CompareTo((byte)b.Nivel));

        var detalle = parametros.Niveles.Max();
        var deDetalle = filas.Where(f => f.Nivel == detalle).ToList();
        return new ResultadoSumasYSaldos(razonSocial, ejercicio, parametros, filas,
            subcuentas.Sum(s => s.Debe), subcuentas.Sum(s => s.Haber),
            deDetalle.Sum(f => f.SaldoDeudor), deDetalle.Sum(f => f.SaldoAcreedor), subcuentas.Count, generado);
    }

    /// <summary>Nombre del fichero, con el periodo y la hora de generación: «Sumas y saldos 2026 01-01 a 31-12 (20261118-1930).pdf».</summary>
    public static string NombreFichero(ResultadoSumasYSaldos r, string extension) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Sumas y saldos {r.Ejercicio} {r.Parametros.Desde:dd-MM} a {r.Parametros.Hasta:dd-MM} ({r.Generado:yyyyMMdd-HHmm}){extension}");
}

/// <summary>Lee de la BD las sumas de cada subcuenta entre fechas y los nombres del plan del ejercicio, y calcula el balance de sumas y saldos.</summary>
public sealed class GeneradorSumasYSaldos(IBaseDatos bd)
{
    /// <summary>Tipo de asiento de cierre (Asientos.TipoAsiento), como en el Mayor y los balances.</summary>
    private const byte TipoCierre = 3;

    public async Task<ResultadoSumasYSaldos> GenerarAsync(ContextoSociedadEjercicio contexto, string razonSocial, ParametrosSumasYSaldos parametros,
        CancellationToken cancelacion = default)
    {
        if (parametros.Error(contexto.Ejercicio) is { } error) throw new InvalidOperationException(error);
        var (movimientos, nombres) = await bd.EjecutarAsync(async s =>
        {
            var m = await s.ConsultarAsync(
                "SELECT p.CuentaContable, SUM(p.ImporteDebe) AS D, SUM(p.ImporteHaber) AS H " +
                "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.FechaContable >= ? AND a.FechaContable < ?" +
                (parametros.IncluirCierre ? "" : $" AND a.TipoAsiento <> {TipoCierre}") +
                " GROUP BY p.CuentaContable",
                f => new MovimientoSubcuenta((f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("D") ?? 0m, f.DecimalNulo("H") ?? 0m),
                contexto.IdSociedadEjercicio, parametros.Desde.Date, parametros.Hasta.Date.AddDays(1));
            var n = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (codigo, nombre) in await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                         f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), contexto.IdSociedadEjercicio))
                if (codigo.Length > 0) n.TryAdd(codigo, nombre);
            // Cuentas de hasta 4 dígitos que el plan de este ejercicio no tiene (en 2018 faltan muchas de 3): el nombre del ejercicio más reciente que la tenga.
            foreach (var (codigo, nombre) in await s.ConsultarAsync(
                         "SELECT cc.Codigo, cc.Nombre FROM CuentasContables AS cc INNER JOIN SociedadesEjercicios AS se ON cc.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                         "WHERE se.IdSociedad = ? AND Len(cc.Codigo) <= 4 ORDER BY se.Ejercicio DESC",
                         f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), contexto.IdSociedad))
                if (codigo.Length > 0 && nombre.Length > 0 && !(n.TryGetValue(codigo, out var actual) && actual.Length > 0)) n[codigo] = nombre;
            return (m, (IReadOnlyDictionary<string, string>)n);
        }, cancelacion);
        return CalculadoraSumasYSaldos.Calcular(razonSocial, contexto.Ejercicio, parametros, movimientos, nombres, DateTime.Now);
    }
}
