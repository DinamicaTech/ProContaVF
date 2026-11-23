using System.Globalization;
using ProConta.Contabilidad.Balances;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.CuentasAnuales;

/// <summary>Un ejercicio de la sociedad para elegir de cuál se generan las cuentas.</summary>
public sealed record EjercicioCuentas(int IdSociedadEjercicio, int Ejercicio, bool Cerrado)
{
    public string Texto => Cerrado ? $"{Ejercicio} (cerrado)" : $"{Ejercicio}";
}

/// <summary>
/// Cuentas anuales del modelo pymes (contabilidad/procesos/cuentas-anuales): lee de la BD las sumas por subcuenta del ejercicio y del anterior
/// y las compone con <see cref="CalculoCuentasAnuales"/>. Es el único sitio que calcula las cuentas anuales: la legalización de libros
/// (contabilidad/procesos/legalizacion-libros) las toma de aquí para el libro de Inventarios y Cuentas Anuales. Solo lee; no graba nada.
/// </summary>
public sealed class ServicioCuentasAnuales(IBaseDatos bd)
{
    /// <summary>Las cuentas anuales del ejercicio del contexto, con la columna del ejercicio anterior si existe en la BD.</summary>
    public async Task<CuentasAnualesPymes> GenerarAsync(ContextoSociedadEjercicio contexto, CancellationToken cancelacion = default)
    {
        var (razon, nif, cerrado, actual, anterior) = await bd.EjecutarAsync(async s =>
        {
            var soc = await s.ConsultarPrimeroAsync("SELECT Nombre, CIF FROM Sociedades WHERE IdSociedad = ?",
                f => (Nombre: (f.Texto("Nombre") ?? "").Trim(), Nif: (f.Texto("CIF") ?? "").Trim()), contexto.IdSociedad);
            var cerrado = await s.ConsultarPrimeroAsync("SELECT EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
                f => f.Logico("EjercicioCerrado"), contexto.IdSociedadEjercicio);
            var a = await CifrasAsync(s, contexto.IdSociedadEjercicio, contexto.Ejercicio);
            var idAnterior = await Dividendos.ConsultaSocios.EjercicioAsync(s, contexto.IdSociedad, contexto.Ejercicio - 1);
            var b = idAnterior is int id ? await CifrasAsync(s, id, contexto.Ejercicio - 1) : null;
            return (soc.Nombre ?? "", soc.Nif ?? "", cerrado, a, b);
        }, cancelacion);
        decimal? resultadoBalances = null;
        try
        {
            resultadoBalances = (await new GeneradorBalances(bd).GenerarAsync(TipoBalance.PerdidasYGanancias, contexto, razon, cancelacion)).Total;
        }
        catch (InvalidOperationException)
        {
            // Sin modelo de balances en la BD: la comparación con Informes › Pérdidas y ganancias no se hace.
        }
        return CalculoCuentasAnuales.Componer(razon, nif, actual, anterior, cerrado, resultadoBalances);
    }

    /// <summary>
    /// La memoria de unas cuentas ya calculadas: los textos guardados del ejercicio (o los propuestos) y las tablas, con los datos de la sociedad,
    /// los dividendos repartidos en el ejercicio y la aplicación de su resultado si ya se grabó en el siguiente.
    /// </summary>
    public async Task<IReadOnlyList<ApartadoMemoria>> MemoriaAsync(string idSociedad, int idSociedadEjercicio, CuentasAnualesPymes cuentas, CancellationToken cancelacion = default)
    {
        var (datos, textos) = await bd.EjecutarAsync(async s =>
        {
            var soc = await s.ConsultarPrimeroAsync(
                "SELECT DireccionFiscal, CodigoPostalFiscal, PoblacionFiscal, ProvinciaFiscal, ValorNominalParticipacion FROM Sociedades WHERE IdSociedad = ?",
                f => (Domicilio: string.Join(", ", new[] { f.Texto("DireccionFiscal"), string.Join(" ", new[] { f.Texto("CodigoPostalFiscal"), f.Texto("PoblacionFiscal") }
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())), f.Texto("ProvinciaFiscal") }
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())),
                    Nominal: f.DecimalNulo("ValorNominalParticipacion")), idSociedad);
            var participaciones = await s.ConsultarPrimeroAsync(
                "SELECT SUM(Participaciones) AS Total FROM SociosSociedad WHERE IdSociedad = ? AND Activo = True",
                f => f.DecimalNulo("Total"), idSociedad);
            var dividendos = await s.ConsultarAsync(
                "SELECT t.Nombre, r.Fecha, rs.Bruto, rs.Retencion FROM ((RepartosDividendos AS r INNER JOIN RepartosDividendosSocios AS rs ON r.IdRepartoDividendos = rs.IdRepartoDividendos) " +
                "INNER JOIN Socios AS so ON rs.IdSocio = so.IdSocio) INNER JOIN Terceros AS t ON so.IdTercero = t.IdTercero WHERE r.IdSociedadEjercicio = ? ORDER BY r.Fecha, t.Nombre",
                f => new DividendoSocio((f.Texto("Nombre") ?? "").Trim(), f.Fecha("Fecha"), f.DecimalNulo("Bruto") ?? 0m, f.DecimalNulo("Retencion") ?? 0m), idSociedadEjercicio);
            var aplicacion = await s.ConsultarPrimeroAsync(
                "SELECT a.Resultado, a.Compensacion, a.ReservaLegal, a.Dividendos, a.Voluntarias FROM AplicacionesResultado AS a " +
                "INNER JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE se.IdSociedad = ? AND a.EjercicioResultado = ?",
                f => new AplicacionMemoria(f.DecimalNulo("Resultado") ?? 0m, f.DecimalNulo("Compensacion") ?? 0m, f.DecimalNulo("ReservaLegal") ?? 0m,
                    f.DecimalNulo("Dividendos") ?? 0m, f.DecimalNulo("Voluntarias") ?? 0m), idSociedad, cuentas.Ejercicio);
            var t = await s.ConsultarAsync("SELECT Apartado, Texto FROM CuentasAnualesMemoria WHERE IdSociedadEjercicio = ?",
                f => ((f.Texto("Apartado") ?? "").Trim(), f.Texto("Texto") ?? ""), idSociedadEjercicio);
            var datos = new DatosMemoria(soc.Domicilio is { Length: > 0 } dom ? dom : null,
                participaciones is decimal p && p > 0m ? (long)p : null, soc.Nominal is decimal v && v > 0m ? v : null, dividendos, aplicacion);
            return (datos, (IReadOnlyDictionary<string, string>)t.ToDictionary(x => x.Item1, x => x.Item2, StringComparer.Ordinal));
        }, cancelacion);
        return MemoriaPymes.Componer(cuentas, datos, textos);
    }

    /// <summary>Guarda el texto de un apartado de la memoria; vacío lo borra y el apartado vuelve al texto propuesto.</summary>
    public Task GuardarTextoMemoriaAsync(int idSociedadEjercicio, string apartado, string? texto, string operador, CancellationToken cancelacion = default) =>
        bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("DELETE FROM CuentasAnualesMemoria WHERE IdSociedadEjercicio = ? AND Apartado = ?", idSociedadEjercicio, apartado);
            if (!string.IsNullOrWhiteSpace(texto))
                await s.EjecutarAsync("INSERT INTO CuentasAnualesMemoria (IdSociedadEjercicio, Apartado, Texto, Operador, FechaHora) VALUES (?, ?, ?, ?, ?)",
                    idSociedadEjercicio, apartado, texto.Trim(), operador, DateTime.Now);
            return true;
        }, cancelacion);

    /// <summary>Los ejercicios de la sociedad, del más reciente al más antiguo, con su marca de cerrado.</summary>
    public async Task<IReadOnlyList<EjercicioCuentas>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default)
    {
        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdSociedadEjercicio, Ejercicio, EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdSociedadEjercicio"), Anio: (f.Texto("Ejercicio") ?? "").Trim(), Cerrado: f.Logico("EjercicioCerrado")), idSociedad), cancelacion);
        return filas.Where(f => int.TryParse(f.Anio, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            .Select(f => new EjercicioCuentas(f.Id, int.Parse(f.Anio, CultureInfo.InvariantCulture), f.Cerrado))
            .OrderByDescending(e => e.Ejercicio).ToList();
    }

    /// <summary>
    /// El ejercicio que se propone al abrir la pantalla: el anterior al de trabajo si existe (las cuentas de un año se formulan en el
    /// siguiente); si no, el de trabajo.
    /// </summary>
    public static int EjercicioPropuesto(IReadOnlyList<EjercicioCuentas> ejercicios, int trabajo) =>
        ejercicios.Any(e => e.Ejercicio == trabajo - 1) ? trabajo - 1 : trabajo;

    /// <summary>Las cifras de un ejercicio desde sus apuntes contabilizados (EstadoAsiento 1), sin la caché de saldos.</summary>
    internal static async Task<CifrasEjercicio> CifrasAsync(ISesionDatos s, int idSociedadEjercicio, int ejercicio)
    {
        var sumas = await s.ConsultarAsync(
            "SELECT p.CuentaContable, a.TipoAsiento, SUM(p.ImporteDebe) AS Debe, SUM(p.ImporteHaber) AS Haber " +
            "FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 GROUP BY p.CuentaContable, a.TipoAsiento",
            f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Tipo: Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture),
                Debe: f.DecimalNulo("Debe") ?? 0m, Haber: f.DecimalNulo("Haber") ?? 0m),
            idSociedadEjercicio);
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (codigo, nombre) in await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                     f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), idSociedadEjercicio))
            if (codigo.Length > 0) nombres.TryAdd(codigo, nombre);
        return Cifras(ejercicio, sumas.Select(x => (x.Cuenta, x.Tipo, x.Debe, x.Haber)), nombres);
    }

    /// <summary>
    /// Reparte las sumas por subcuenta y tipo de asiento entre los dos estados: el balance lleva todo salvo el cierre (TipoAsiento 3); pérdidas
    /// y ganancias, las cuentas 6 y 7 sin la regularización (2) ni el cierre, como contabilidad/balances.
    /// </summary>
    public static CifrasEjercicio Cifras(int ejercicio, IEnumerable<(string Cuenta, byte Tipo, decimal Debe, decimal Haber)> sumas, IReadOnlyDictionary<string, string> nombres)
    {
        var balance = new Dictionary<string, (decimal D, decimal H)>(StringComparer.Ordinal);
        var pyg = new Dictionary<string, (decimal D, decimal H)>(StringComparer.Ordinal);
        var regularizado = false;
        var movimientos = new Dictionary<string, MovimientoSubcuenta>(StringComparer.Ordinal);
        foreach (var (cuenta, tipo, debe, haber) in sumas)
        {
            if (cuenta.Length == 0 || tipo == Clasificador.TipoCierre) continue;
            if (tipo != Clasificador.TipoRegularizacion)
            {
                var m = movimientos.GetValueOrDefault(cuenta) ?? new MovimientoSubcuenta(cuenta, 0m, 0m, 0m);
                movimientos[cuenta] = tipo == TipoApertura ? m with { Apertura = m.Apertura + debe - haber } : m with { Debe = m.Debe + debe, Haber = m.Haber + haber };
            }
            if (tipo == Clasificador.TipoRegularizacion) regularizado = true;
            Sumar(balance, cuenta, debe, haber);
            if (tipo != Clasificador.TipoRegularizacion && cuenta[0] is '6' or '7') Sumar(pyg, cuenta, debe, haber);
        }
        SaldoSubcuenta S(KeyValuePair<string, (decimal D, decimal H)> kv) => new(kv.Key, nombres.GetValueOrDefault(kv.Key, ""), kv.Value.D, kv.Value.H);
        return CalculoCuentasAnuales.Cifras(ejercicio, balance.Select(S), pyg.Select(S), regularizado) with { Movimientos = movimientos };
    }

    /// <summary>TipoAsiento de la apertura.</summary>
    public const byte TipoApertura = 1;

    private static void Sumar(Dictionary<string, (decimal D, decimal H)> d, string cuenta, decimal debe, decimal haber)
    {
        var (x, y) = d.GetValueOrDefault(cuenta);
        d[cuenta] = (x + debe, y + haber);
    }
}
