using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Norma43.Importacion;

namespace ProConta.Inicio.Salud;

/// <summary>
/// Lecturas de la salud de la contabilidad en Access (solo SELECT). Asientos: los contabilizados (EstadoAsiento = 1) sin el cierre,
/// salvo en el cuadre del Diario y en las cuentas de los apuntes, donde cuentan también los borradores (todos menos los anulados).
/// </summary>
public sealed class FuenteSaludAccess(IBaseDatos bd) : IFuenteSalud
{
    private const string Contabilizados = "a.EstadoAsiento = 1 AND a.TipoAsiento <> 3";
    private const string NoAnulados = "a.EstadoAsiento <> 2";
    private readonly FuenteInicioAccess _inicio = new(bd);

    private static string Ids(IEnumerable<EjercicioSociedad> e) => string.Join(", ", e.Select(x => x.IdSociedadEjercicio.ToString(CultureInfo.InvariantCulture)));

    public Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken c = default) => _inicio.EjerciciosAsync(idSociedad, c);

    public Task<ModelosInicio> ModelosAsync(string idSociedad, CancellationToken c = default) => ModelosInicio.LeerAsync(bd, idSociedad, c);

    public Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken c = default) => _inicio.ActividadesAsync(idSociedad, c);

    public Task<VentasPendientes> VentasPendientesAsync(string idSociedad, DateTime antesDe, CancellationToken c = default) =>
        bd.EjecutarAsync(async s =>
        {
            // Mismo criterio que Ventas por facturar: sin anular ni facturada fuera; pendiente sin factura o con la factura en borrador (3).
            var filas = await s.ConsultarAsync(
                "SELECT v.IdFactura, v.Importe, f.EstadoFactura FROM VentasPorFacturar AS v LEFT JOIN Facturas AS f ON v.IdFactura = f.IdFactura " +
                "WHERE v.IdSociedad = ? AND v.Anulada = False AND v.FacturadaFuera = False AND v.FechaReferencia < ?",
                f => (Factura: f.EnteroNulo("IdFactura"), Importe: f.DecimalNulo("Importe") ?? 0m, Estado: f.EsNulo("EstadoFactura") ? (int?)null : Convert.ToInt32(f.Valor("EstadoFactura"), CultureInfo.InvariantCulture)),
                idSociedad, antesDe.Date);
            var pendientes = filas.Where(x => x.Factura is null || x.Estado is null).ToList();
            var borrador = filas.Where(x => x.Factura is not null && x.Estado == 3).ToList();
            return new VentasPendientes(pendientes.Count, pendientes.Sum(x => x.Importe), borrador.Count, borrador.Sum(x => x.Importe));
        }, c);

    public Task<IReadOnlyList<ApunteIva>> IvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<ApunteIva>>(async s => await s.ConsultarAsync(
            "SELECT a.FechaContable, p.CuentaContable, p.ImporteDebe, p.ImporteHaber FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
            $"WHERE a.IdSociedadEjercicio = ? AND {Contabilizados} AND a.TipoAsiento <> 2 AND LEFT(p.CuentaContable, 3) IN ('472', '477') " +
            "AND NOT EXISTS (SELECT q.IdApunte FROM Apuntes AS q WHERE q.IdAsiento = a.IdAsiento AND LEFT(q.CuentaContable, 4) IN ('4700', '4750'))",
            f => new ApunteIva(f.Fecha("FechaContable").Date, (f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m),
            ejercicio.IdSociedadEjercicio), c);

    public Task<IReadOnlyList<LiquidacionIva>> LiquidacionesIvaAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) =>
        ejercicios.Count == 0 ? Task.FromResult<IReadOnlyList<LiquidacionIva>>([]) :
        bd.EjecutarAsync<IReadOnlyList<LiquidacionIva>>(async s =>
        {
            var anio = ejercicios.ToDictionary(e => e.IdSociedadEjercicio, e => e.Ejercicio);
            return (await s.ConsultarAsync(
                    $"SELECT DISTINCT a.IdSociedadEjercicio, a.FechaContable FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
                    $"WHERE a.IdSociedadEjercicio IN ({Ids(ejercicios)}) AND {Contabilizados} AND a.TipoAsiento NOT IN (1, 2) " +
                    // Liquidación = deja el resultado del 303: abona la 4750 (a ingresar) o carga la 4700 (a compensar o devolver).
                    // El pago (4750 al debe contra el banco) y las compensaciones (4700 al haber) no lo son.
                    "AND ((LEFT(p.CuentaContable, 4) = '4750' AND p.ImporteHaber > 0) OR (LEFT(p.CuentaContable, 4) = '4700' AND p.ImporteDebe > 0))",
                    f => new LiquidacionIva(anio.GetValueOrDefault(f.Entero("IdSociedadEjercicio")), f.Fecha("FechaContable").Date)))
                .Distinct().ToList();
        }, c);

    public async Task<IReadOnlyList<BancoSalud>> BancosAsync(string idSociedad, ContextoSalud contexto, CancellationToken c = default)
    {
        var e = contexto.Ejercicio;
        var cuentas = await new ConsultaBancos(bd).CuentasAsync(new ContextoSociedadEjercicio(idSociedad, e.IdSociedadEjercicio, e.Ejercicio));
        return await bd.EjecutarAsync<IReadOnlyList<BancoSalud>>(async s =>
        {
            var r = new List<BancoSalud>();
            foreach (var b in cuentas)
            {
                var nombre = string.IsNullOrWhiteSpace(b.Nombre) ? b.NombreContable ?? b.Ccc : b.Nombre!.Trim();
                var cuenta = b.CodigoContable?.Trim();
                var ultimaLinea = await FechaAsync(s,
                    "SELECT MAX(l.FechaOperacion) FROM (LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS x ON l.IdExtractoBancario = x.IdExtractoBancario) " +
                    "INNER JOIN LotesImportacionBancaria AS lo ON x.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria WHERE x.IdCuentaBancaria = ? AND lo.Estado <> ?",
                    b.IdCuentaBancaria, ServicioImportacionN43.EstadoRevertido);
                if (cuenta is not { Length: > 0 })
                {
                    r.Add(new BancoSalud(nombre, null, b.Activa, b.FechaSaldoBanco, b.SaldoBanco, null, 0m, false, await ExtractoCuadradoAsync(s, b.IdCuentaBancaria), ultimaLinea));
                    continue;
                }
                var ultimoApunte = contexto.Ejercicios.Count == 0 ? null : await FechaAsync(s,
                    $"SELECT MAX(a.FechaContable) FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio IN ({Ids(contexto.Ejercicios)}) " +
                    // Sin apertura (1), regularización (2) ni cierre (3): arrastran el saldo de una cuenta parada, no son movimientos.
                    "AND a.EstadoAsiento = 1 AND a.TipoAsiento NOT IN (1, 2, 3) AND p.CuentaContable = ?", cuenta);
                var ultimoMovimiento = ultimaLinea is null ? ultimoApunte : ultimoApunte is null ? ultimaLinea : ultimaLinea > ultimoApunte ? ultimaLinea : ultimoApunte;
                var corte = await SaldoAsync(s, e.IdSociedadEjercicio, cuenta, contexto.Corte);
                var apuntes = Convert.ToInt32(await s.EscalarAsync(
                    $"SELECT COUNT(*) FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND {Contabilizados} AND p.CuentaContable = ?",
                    e.IdSociedadEjercicio, cuenta) ?? 0, CultureInfo.InvariantCulture);
                decimal? alExtracto = null;
                if (b.FechaSaldoBanco is DateTime f && contexto.Ejercicios.FirstOrDefault(x => x.Ejercicio == f.Year) is { } ef)
                    alExtracto = await SaldoAsync(s, ef.IdSociedadEjercicio, cuenta, f.Date);
                r.Add(new BancoSalud(nombre, cuenta, b.Activa, b.FechaSaldoBanco, b.SaldoBanco, alExtracto, corte, apuntes > 0, await ExtractoCuadradoAsync(s, b.IdCuentaBancaria), ultimoMovimiento));
            }
            return r;
        }, c);
    }

    private static async Task<DateTime?> FechaAsync(ISesionDatos s, string sql, params object?[] parametros) =>
        await s.EscalarAsync(sql, parametros) is DateTime d ? d.Date : null;

    /// <summary>El último extracto (no revertido) de la cuenta cuadra por dentro: sus totales declarados son los calculados.</summary>
    private static async Task<bool> ExtractoCuadradoAsync(ISesionDatos s, int idCuentaBancaria)
    {
        var v = await s.EscalarAsync(
            "SELECT TOP 1 e.EstadoCuadre FROM ExtractosBancarios AS e INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria " +
            "WHERE e.IdCuentaBancaria = ? AND lo.Estado <> ? ORDER BY e.FechaFin DESC, e.IdExtractoBancario DESC", idCuentaBancaria, ServicioImportacionN43.EstadoRevertido);
        return v is not string estado || !string.Equals(estado.Trim(), "DESCUADRADO", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<decimal> SaldoAsync(ISesionDatos s, int idSociedadEjercicio, string cuenta, DateTime hasta)
    {
        var v = await s.EscalarAsync(
            "SELECT SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
            $"WHERE a.IdSociedadEjercicio = ? AND {Contabilizados} AND p.CuentaContable = ? AND a.FechaContable < ?",
            idSociedadEjercicio, cuenta, hasta.Date.AddDays(1));
        return v is null or DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    }

    public Task<IReadOnlyList<MesBanco>> LineasBancoAsync(string idSociedad, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<MesBanco>>(async s =>
        {
            var bancos = (await s.ConsultarAsync("SELECT IdCuentaBancaria, Nombre, EntidadOriginal, Cuenta FROM CuentasBancarias WHERE IdSociedad = ?",
                    f => (Id: f.Entero("IdCuentaBancaria"), Nombre: string.IsNullOrWhiteSpace(f.Texto("Nombre")) ? $"{f.Texto("EntidadOriginal")} {f.Texto("Cuenta")}" : f.Texto("Nombre")!.Trim()), idSociedad))
                .ToDictionary(x => x.Id, x => x.Nombre);
            const string desde = "(LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
                                 "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria";
            var todas = await s.ConsultarAsync(
                $"SELECT e.IdCuentaBancaria, YEAR(l.FechaOperacion) AS A, MONTH(l.FechaOperacion) AS M, COUNT(*) AS N FROM {desde} " +
                "WHERE lo.IdSociedad = ? AND lo.Estado <> ? GROUP BY e.IdCuentaBancaria, YEAR(l.FechaOperacion), MONTH(l.FechaOperacion)",
                f => (Id: f.EnteroNulo("IdCuentaBancaria") ?? 0, A: f.Entero("A"), M: f.Entero("M"), N: f.Entero("N")), idSociedad, ServicioImportacionN43.EstadoRevertido);
            var sin = (await s.ConsultarAsync(
                    $"SELECT e.IdCuentaBancaria, YEAR(l.FechaOperacion) AS A, MONTH(l.FechaOperacion) AS M, COUNT(*) AS N, MIN(l.FechaOperacion) AS Primera FROM {desde} " +
                    "WHERE lo.IdSociedad = ? AND lo.Estado <> ? AND NOT EXISTS (SELECT c.IdLineaExtractoBancario FROM ContabilizacionesLineaExtracto AS c WHERE c.IdLineaExtractoBancario = l.IdLineaExtractoBancario) " +
                    "GROUP BY e.IdCuentaBancaria, YEAR(l.FechaOperacion), MONTH(l.FechaOperacion)",
                    f => (Id: f.EnteroNulo("IdCuentaBancaria") ?? 0, A: f.Entero("A"), M: f.Entero("M"), N: f.Entero("N"), Primera: f.FechaNula("Primera")), idSociedad, ServicioImportacionN43.EstadoRevertido))
                .ToDictionary(x => (x.Id, x.A, x.M));
            return todas.Select(t =>
            {
                var x = sin.TryGetValue((t.Id, t.A, t.M), out var v) ? v : default;
                return new MesBanco(bancos.GetValueOrDefault(t.Id) ?? $"Cuenta {t.Id}", null, t.A, t.M, t.N, x.N, x.Primera?.Date);
            }).ToList();
        }, c);

    public Task<ComprasPendientes> ComprasPendientesAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync(async s =>
        {
            // Mismo filtro de ejercicio que Compras › Facturas recibidas.
            var filas = await s.ConsultarAsync(
                "SELECT fc.ImporteTotal, fc.Fecha FROM FacturasCompra AS fc WHERE (fc.IdSociedadEjercicio = ? OR (fc.IdSociedadEjercicio IS NULL AND fc.Sociedad = ? AND fc.Ejercicio = ?)) " +
                "AND fc.Exportada = False AND fc.Duplicada = False",
                f => (Total: f.DecimalNulo("ImporteTotal") ?? 0m, Fecha: f.FechaNula("Fecha")),
                ejercicio.IdSociedadEjercicio, idSociedad, ejercicio.Ejercicio.ToString(CultureInfo.InvariantCulture));
            return new ComprasPendientes(filas.Count, filas.Sum(x => x.Total), filas.Where(x => x.Fecha is not null).Select(x => x.Fecha!.Value.Date).DefaultIfEmpty().Min() is var m && m != default ? m : null);
        }, c);

    public Task<IReadOnlyList<DateTime>> BorradoresVentaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<DateTime>>(async s => await s.ConsultarAsync(
            "SELECT FechaCreacion FROM Facturas WHERE IdSociedadEjercicio = ? AND EstadoFactura = 3 AND FechaCreacion IS NOT NULL",
            f => f.Fecha("FechaCreacion"), ejercicio.IdSociedadEjercicio), c);

    public Task<IReadOnlyDictionary<string, decimal>> SaldosAsync(EjercicioSociedad ejercicio, string prefijoDesde, string prefijoHasta, DateTime hasta, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyDictionary<string, decimal>>(async s =>
        {
            var n = prefijoDesde.Length;
            var filas = await s.ConsultarAsync(
                "SELECT p.CuentaContable, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS S " +
                $"FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND {Contabilizados} AND a.FechaContable < ? " +
                $"AND LEFT(p.CuentaContable, {n}) BETWEEN ? AND ? GROUP BY p.CuentaContable",
                f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Saldo: f.DecimalNulo("S") ?? 0m), ejercicio.IdSociedadEjercicio, hasta.Date.AddDays(1), prefijoDesde, prefijoHasta);
            return filas.GroupBy(x => x.Cuenta, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(x => x.Saldo), StringComparer.Ordinal);
        }, c);

    public Task<(int Asientos, IReadOnlyList<AsientoDescuadrado> Descuadrados)> CuadreDiarioAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync(async s =>
        {
            var total = Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM Asientos AS a WHERE a.IdSociedadEjercicio = ? AND {NoAnulados}", ejercicio.IdSociedadEjercicio) ?? 0,
                CultureInfo.InvariantCulture);
            var filas = await s.ConsultarAsync(
                "SELECT a.IdAsiento, a.FechaContable, a.Descripcion, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) AS D, SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS H " +
                $"FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND {NoAnulados} " +
                "GROUP BY a.IdAsiento, a.FechaContable, a.Descripcion " +
                "HAVING SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) <> SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber))",
                f => new AsientoDescuadrado(f.Entero("IdAsiento"), f.Fecha("FechaContable").Date, f.Texto("Descripcion"), f.DecimalNulo("D") ?? 0m, f.DecimalNulo("H") ?? 0m),
                ejercicio.IdSociedadEjercicio);
            return (total, (IReadOnlyList<AsientoDescuadrado>)filas);
        }, c);

    public Task<IReadOnlyList<NumeroFactura>> NumerosFacturasAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<NumeroFactura>>(async s => await s.ConsultarAsync(
            "SELECT f.IdSerie, s.Prefijo, f.NumeroFactura FROM Facturas AS f LEFT JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie " +
            "WHERE f.IdSociedadEjercicio = ? AND f.EstadoFactura IN (1, 2, 4) AND f.NumeroFactura IS NOT NULL",
            f => new NumeroFactura(f.EnteroNulo("IdSerie") ?? 0, (f.Texto("Prefijo") ?? "").Trim(), (f.Texto("NumeroFactura") ?? "").Trim()), ejercicio.IdSociedadEjercicio), c);

    public Task<IReadOnlyList<IvaLibrosTrimestre>> LibrosIvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<IvaLibrosTrimestre>>(async s =>
        {
            var id = ejercicio.IdSociedadEjercicio;
            // Libros, por la fecha de su asiento contabilizado (así se comparan con las cuentas en el mismo trimestre).
            var rep = await s.ConsultarAsync(
                "SELECT a.FechaContable, r.CuotaRepercutidaTotal AS C FROM IVARepercutido AS r INNER JOIN Asientos AS a ON r.IdAsiento = a.IdAsiento " +
                $"WHERE r.IdSociedadEjercicio = ? AND {Contabilizados}",
                f => (Fecha: f.Fecha("FechaContable"), Cuota: f.DecimalNulo("C") ?? 0m), id);
            var sop = await s.ConsultarAsync(
                "SELECT a.FechaContable, r.CuotaSoportadaTotal AS C FROM IVASoportado AS r INNER JOIN Asientos AS a ON r.IdAsiento = a.IdAsiento " +
                $"WHERE r.IdSociedadEjercicio = ? AND {Contabilizados}",
                f => (Fecha: f.Fecha("FechaContable"), Cuota: f.DecimalNulo("C") ?? 0m), id);
            // Apuntes de las 472 y 477 (como IvaAsync), marcando los de asientos con registro en el libro de soportado: una factura
            // recibida no lleva IVA repercutido, así que su 477 es el autorrepercutido (inversión del sujeto pasivo, intracomunitarias).
            var apuntes = await s.ConsultarAsync(
                "SELECT a.FechaContable, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, " +
                "(SELECT COUNT(*) FROM IVASoportado AS v WHERE v.IdAsiento = a.IdAsiento) AS NS FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
                $"WHERE a.IdSociedadEjercicio = ? AND {Contabilizados} AND a.TipoAsiento <> 2 AND LEFT(p.CuentaContable, 3) IN ('472', '477') " +
                "AND NOT EXISTS (SELECT q.IdApunte FROM Apuntes AS q WHERE q.IdAsiento = a.IdAsiento AND LEFT(q.CuentaContable, 4) IN ('4700', '4750'))",
                f => new ApunteLibroIva(f.Fecha("FechaContable").Date, (f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m,
                    f.Entero("NS") > 0), id);
            return CuadreLibrosIva.Trimestres(rep.Select(x => (x.Fecha, x.Cuota)).ToList(), sop.Select(x => (x.Fecha, x.Cuota)).ToList(), apuntes);
        }, c);

    public Task<IReadOnlyList<TerceroSinNif>> TercerosSinNifAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<TerceroSinNif>>(async s => (await TercerosConApuntesAsync(s, idSociedad, ejercicio))
            .Where(x => string.IsNullOrWhiteSpace(x.Nif)).Select(x => new TerceroSinNif(x.Tercero, x.Nombre, x.Codigo, x.EsCliente)).ToList(), c);

    public Task<IReadOnlyList<TerceroNif>> TercerosConNifAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<TerceroNif>>(async s => (await TercerosConApuntesAsync(s, idSociedad, ejercicio))
            .Where(x => !string.IsNullOrWhiteSpace(x.Nif)).Select(x => new TerceroNif(x.Tercero, x.Nombre, x.Codigo, x.EsCliente, x.Nif!.Trim(), x.Tipo, x.Pais)).ToList(), c);

    /// <summary>Clientes y proveedores cuya subcuenta (por su identidad contable) tiene apuntes contabilizados en el ejercicio, con su NIF.</summary>
    private static async Task<List<(int Tercero, string Nombre, string Codigo, bool EsCliente, string? Nif, string? Tipo, string? Pais)>> TercerosConApuntesAsync(ISesionDatos s, string idSociedad, EjercicioSociedad ejercicio)
    {
        var id = ejercicio.IdSociedadEjercicio;
        // Subcuentas de clientes y proveedores con apuntes contabilizados en el ejercicio, con su identidad contable.
        var cuentas = await s.ConsultarAsync(
            "SELECT DISTINCT cc.Codigo, cc.IdIdentidadContable FROM (Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento) " +
            "INNER JOIN CuentasContables AS cc ON cc.Codigo = p.CuentaContable " +
            $"WHERE a.IdSociedadEjercicio = ? AND cc.IdSociedadEjercicio = ? AND {Contabilizados} AND (LEFT(p.CuentaContable, 3) BETWEEN '430' AND '436' OR LEFT(p.CuentaContable, 3) BETWEEN '400' AND '410')",
            f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Identidad: f.EnteroNulo("IdIdentidadContable")), id, id);
        var porIdentidad = cuentas.Where(x => x.Identidad is not null).GroupBy(x => x.Identidad!.Value).ToDictionary(g => g.Key, g => g.First().Codigo);
        var r = new List<(int Tercero, string Nombre, string Codigo, bool EsCliente, string? Nif, string? Tipo, string? Pais)>();
        if (porIdentidad.Count == 0) return r;
        var clientes = await s.ConsultarAsync(
            "SELECT cs.IdIdentidadContable, t.IdTercero, t.Nombre, t.RazonSocial, t.NIF, t.TipoIdentificacion, t.Pais FROM (CuentasClientesSociedad AS cs INNER JOIN Clientes AS cl ON cs.IdCliente = cl.IdCliente) " +
            "INNER JOIN Terceros AS t ON cl.IdTercero = t.IdTercero WHERE cs.IdSociedad = ?",
            f => (Identidad: f.EnteroNulo("IdIdentidadContable"), Tercero: f.Entero("IdTercero"), Nombre: f.Texto("RazonSocial") is { Length: > 0 } rs ? rs : f.Texto("Nombre") ?? "", Nif: f.Texto("NIF"), Tipo: f.Texto("TipoIdentificacion"), Pais: f.Texto("Pais")), idSociedad);
        var proveedores = await s.ConsultarAsync(
            "SELECT cs.IdIdentidadContable, t.IdTercero, t.Nombre, t.RazonSocial, t.NIF, t.TipoIdentificacion, t.Pais FROM (CuentasProveedoresSociedad AS cs INNER JOIN Proveedores AS pr ON cs.IdProveedor = pr.IdProveedor) " +
            "INNER JOIN Terceros AS t ON pr.IdTercero = t.IdTercero WHERE cs.IdSociedad = ?",
            f => (Identidad: f.EnteroNulo("IdIdentidadContable"), Tercero: f.Entero("IdTercero"), Nombre: f.Texto("RazonSocial") is { Length: > 0 } rs ? rs : f.Texto("Nombre") ?? "", Nif: f.Texto("NIF"), Tipo: f.Texto("TipoIdentificacion"), Pais: f.Texto("Pais")), idSociedad);
        void Anadir(IEnumerable<(int? Identidad, int Tercero, string Nombre, string? Nif, string? Tipo, string? Pais)> filas, bool esCliente)
        {
            foreach (var x in filas)
                if (x.Identidad is int i && porIdentidad.TryGetValue(i, out var codigo))
                    r.Add((x.Tercero, x.Nombre.Trim(), codigo, esCliente, x.Nif, x.Tipo, x.Pais));
        }
        Anadir(clientes, true);
        Anadir(proveedores, false);
        return r;
    }

    public Task<IReadOnlyList<CuentaConProblema>> CuentasConProblemaAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<CuentaConProblema>>(async s =>
        {
            var id = ejercicio.IdSociedadEjercicio;
            var usadas = await s.ConsultarAsync(
                $"SELECT p.CuentaContable, COUNT(*) AS N FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND {NoAnulados} GROUP BY p.CuentaContable",
                f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), N: f.Entero("N")), id);
            var plan = (await s.ConsultarAsync("SELECT Codigo, EstaActiva FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                    f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Activa: f.Logico("EstaActiva")), id))
                .GroupBy(x => x.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Any(x => x.Activa), StringComparer.Ordinal);
            return usadas.GroupBy(u => u.Cuenta, StringComparer.Ordinal).Select(g => (Cuenta: g.Key, N: g.Sum(x => x.N)))
                .Where(u => u.Cuenta.Length > 0 && (!plan.TryGetValue(u.Cuenta, out var activa) || !activa))
                .Select(u => new CuentaConProblema(u.Cuenta, u.N, plan.ContainsKey(u.Cuenta))).OrderBy(u => u.Cuenta, StringComparer.Ordinal).ToList();
        }, c);

    public Task<IReadOnlyList<NominaMes>> NominasAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) =>
        ejercicios.Count == 0 ? Task.FromResult<IReadOnlyList<NominaMes>>([]) :
        bd.EjecutarAsync<IReadOnlyList<NominaMes>>(async s => (await s.ConsultarAsync(
                $"SELECT PeriodoDesde, FechaNomina, Estado, TipoPaga FROM ImportacionesNomina WHERE IdSociedadEjercicio IN ({Ids(ejercicios)}) AND Estado NOT IN ('REEMPLAZADA', 'DESHECHA')",
                f => (Desde: f.FechaNula("PeriodoDesde"), Fecha: f.FechaNula("FechaNomina"), Estado: (f.Texto("Estado") ?? "").Trim().ToUpperInvariant(), Tipo: f.Texto("TipoPaga")?.Trim())))
            .Where(x => (x.Desde ?? x.Fecha) is not null)
            .Select(x => { var m = (x.Desde ?? x.Fecha)!.Value; return new NominaMes(m.Year, m.Month, x.Estado, x.Tipo); }).ToList(), c);

    public Task<IReadOnlyList<AsientoAmortizacion>> AmortizacionesAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) =>
        ejercicios.Count == 0 ? Task.FromResult<IReadOnlyList<AsientoAmortizacion>>([]) :
        bd.EjecutarAsync<IReadOnlyList<AsientoAmortizacion>>(async s =>
        {
            var anio = ejercicios.ToDictionary(e => e.IdSociedadEjercicio, e => e.Ejercicio);
            // Asiento de amortización: contabilizado, ni apertura (1) ni regularización (2) ni cierre (3), con una 68 al debe y una 28 al haber.
            return await s.ConsultarAsync(
                "SELECT a.IdAsiento, a.IdSociedadEjercicio, a.FechaContable, SUM(p.ImporteDebe) AS I FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
                $"WHERE a.IdSociedadEjercicio IN ({Ids(ejercicios)}) AND a.EstadoAsiento = 1 AND a.TipoAsiento NOT IN (1, 2, 3) AND LEFT(p.CuentaContable, 2) = '68' AND p.ImporteDebe > 0 " +
                "AND EXISTS (SELECT q.IdApunte FROM Apuntes AS q WHERE q.IdAsiento = a.IdAsiento AND LEFT(q.CuentaContable, 2) = '28' AND q.ImporteHaber > 0) " +
                "GROUP BY a.IdAsiento, a.IdSociedadEjercicio, a.FechaContable",
                f => new AsientoAmortizacion(anio.GetValueOrDefault(f.Entero("IdSociedadEjercicio")), f.Fecha("FechaContable").Date, f.DecimalNulo("I") ?? 0m));
        }, c);

    public Task<string?> PeriodicidadAmortizacionAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync(async s => !s.LeerEsquema().ExisteColumna("SociedadesEjercicios", "PeriodicidadAmortizacion") ? null
            : (await s.EscalarAsync("SELECT PeriodicidadAmortizacion FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ejercicio.IdSociedadEjercicio) as string)?.Trim() is { Length: > 0 } p ? p : null, c);

    public Task<decimal?> UmbralCentimosResidualesAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync(async s => !s.LeerEsquema().ExisteColumna("SociedadesEjercicios", "UmbralCentimosResiduales") ? null
            : await s.EscalarAsync("SELECT UmbralCentimosResiduales FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ejercicio.IdSociedadEjercicio) is { } v and not DBNull
                ? Convert.ToDecimal(v, System.Globalization.CultureInfo.InvariantCulture) : (decimal?)null, c);

    public Task<int> ActivosAmortizablesAsync(string idSociedad, CancellationToken c = default) =>
        bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(
            // Amortizables, propios (no ajenos) y sin dar de baja.
            "SELECT COUNT(*) FROM Activos AS x WHERE x.IdSociedad = ? AND x.EsAmortizable = True AND x.EsAjeno = False AND x.FechaBaja IS NULL", idSociedad) ?? 0,
            CultureInfo.InvariantCulture), c);

    // ------------------------------------------------ incidencias con la gestoría (contabilidad 2026-11-15 21:10)

    public Task<IReadOnlyList<ApunteCuenta>> ApuntesCuentasAsync(IReadOnlyList<EjercicioSociedad> ejercicios, IReadOnlyList<string> prefijos, CancellationToken c = default)
    {
        var limpios = prefijos.Where(x => x.Length > 0 && x.All(char.IsAsciiDigit)).Distinct().ToList();
        if (ejercicios.Count == 0 || limpios.Count == 0) return Task.FromResult<IReadOnlyList<ApunteCuenta>>([]);
        var anio = ejercicios.ToDictionary(e => e.IdSociedadEjercicio, e => e.Ejercicio);
        // Los prefijos son solo cifras (se comprueba arriba), así que se escriben en la consulta.
        var filtro = string.Join(" OR ", limpios.Select(x => $"LEFT(p.CuentaContable, {x.Length}) = '{x}'"));
        return bd.EjecutarAsync<IReadOnlyList<ApunteCuenta>>(async s => await s.ConsultarAsync(
            "SELECT a.IdSociedadEjercicio, a.IdAsiento, a.FechaContable, a.TipoAsiento, p.CuentaContable, p.ImporteDebe, p.ImporteHaber FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
            $"WHERE a.IdSociedadEjercicio IN ({Ids(ejercicios)}) AND {Contabilizados} AND ({filtro})",
            f => new ApunteCuenta(anio.GetValueOrDefault(f.Entero("IdSociedadEjercicio")), f.Entero("IdAsiento"), f.Fecha("FechaContable").Date,
                Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture), (f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m)), c);
    }

    public Task<IReadOnlyDictionary<string, decimal?>> CuadradasAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyDictionary<string, decimal?>>(async s =>
        {
            var filas = await s.ConsultarAsync("SELECT CuentaContable, Saldo FROM CuadresCuentas WHERE IdSociedadEjercicio = ? AND Cuadrada = True",
                f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Saldo: f.DecimalNulo("Saldo")), ejercicio.IdSociedadEjercicio);
            var d = new Dictionary<string, decimal?>(StringComparer.Ordinal);
            foreach (var x in filas) if (x.Cuenta.Length > 0) d[x.Cuenta] = x.Saldo;
            return d;
        }, c);

    public Task<IReadOnlyList<CuentaSinCentro>> CuentasSinCentroAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<CuentaSinCentro>>(async s => await s.ConsultarAsync(
            "SELECT p.CuentaContable, COUNT(*) AS N, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS I " +
            $"FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND {Contabilizados} AND a.TipoAsiento NOT IN (1, 2) " +
            "AND LEFT(p.CuentaContable, 1) IN ('6', '7') AND p.IdCentroCoste IS NULL GROUP BY p.CuentaContable",
            f => new CuentaSinCentro((f.Texto("CuentaContable") ?? "").Trim(), f.Entero("N"), f.DecimalNulo("I") ?? 0m), ejercicio.IdSociedadEjercicio), c);

    public Task<IReadOnlyList<ApunteCartera>> CarteraAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<ApunteCartera>>(async s => await s.ConsultarAsync(
            "SELECT p.CuentaContable, a.FechaContable, p.FechaVencimiento, p.ImporteDebe, p.ImporteHaber FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
            $"WHERE a.IdSociedadEjercicio = ? AND {Contabilizados} AND LEFT(p.CuentaContable, 3) BETWEEN '430' AND '436'",
            f => new ApunteCartera((f.Texto("CuentaContable") ?? "").Trim(), f.Fecha("FechaContable").Date, f.FechaNula("FechaVencimiento")?.Date,
                f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m), ejercicio.IdSociedadEjercicio), c);

    public Task<IReadOnlyList<ProConta.Compras.Facturas.FilaFacturaCompra>> FacturasCompraAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        ProConta.Compras.Facturas.ValidacionesCompra.ListarAsync(bd, new ContextoSociedadEjercicio(idSociedad, ejercicio.IdSociedadEjercicio, ejercicio.Ejercicio), c);

    public Task<IReadOnlyList<ProConta.Compras.Recurrentes.FacturaRecurrentePendiente>> FacturasRecurrentesPendientesAsync(string idSociedad, DateTime hoy, CancellationToken c = default) =>
        new ProConta.Compras.Recurrentes.ServicioFacturasRecurrentes(bd, () => hoy).PendientesAsync(idSociedad, false, c);

    public Task<IReadOnlyList<ProConta.Compras.Facturas.ComisionPendiente>> ComisionesSinCompensarAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        new ProConta.Compras.Facturas.CompensacionComision(bd, new ProConta.Contabilidad.Asientos.MotorAsientosAccess(bd)).PendientesAsync(idSociedad, ejercicio.IdSociedadEjercicio);
}

