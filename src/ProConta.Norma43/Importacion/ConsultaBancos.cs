using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Norma43.Importacion;

/// <summary>
/// Cuenta bancaria de la sociedad con su situación (norma43/bancos): saldo según el banco (saldo final del último extracto),
/// saldo contable de su 572 en el ejercicio activo, movimientos importados y fecha del último. Sin contar lotes revertidos.
/// </summary>
public sealed record CuentaBancariaN43(
    int IdCuentaBancaria, string Entidad, string Oficina, string Cuenta, string Divisa, string? Nombre, string? Iban, bool Activa,
    string? CodigoContable, string? NombreContable, decimal? SaldoBanco, DateTime? FechaSaldoBanco, decimal? SaldoContable,
    int Movimientos, DateTime? UltimoMovimiento, int Lotes)
{
    public string Ccc => $"{Entidad} {Oficina} {Cuenta}";
    public decimal? Diferencia => SaldoBanco is decimal b && SaldoContable is decimal c ? b - c : null;
}

/// <summary>Lote importado que contiene extractos de una cuenta, con lo que aporta a esa cuenta.</summary>
public sealed record LoteN43(
    int IdLote, string NombreFichero, string Formato, string Origen, string Estado, DateTime FechaImportacion, DateTime Desde, DateTime Hasta,
    int Movimientos, int MovimientosLote, int Advertencias, string? RutaOrigen, DateTime? FechaReversion, string? OperadorReversion, string? MotivoReversion)
{
    public bool Revertido => Estado == ServicioImportacionN43.EstadoRevertido;
}

/// <summary>Incidencia guardada con un lote (las advertencias de la lectura).</summary>
public sealed record IncidenciaLoteN43(int? Linea, string Codigo, string Severidad, string Mensaje, string? Registro);

/// <summary>Cuenta contable 572 del ejercicio que se puede asociar a una cuenta bancaria nueva.</summary>
public sealed record CuentaContableBanco(int IdIdentidadContable, string Codigo, string Nombre)
{
    public string Texto => $"{Codigo} · {Nombre}";
}

/// <summary>Lecturas de la pantalla Bancos.</summary>
public sealed class ConsultaBancos(IBaseDatos bd)
{
    public Task<IReadOnlyList<CuentaBancariaN43>> CuentasAsync(ContextoSociedadEjercicio contexto) => bd.EjecutarAsync<IReadOnlyList<CuentaBancariaN43>>(async s =>
    {
        var cuentas = await s.ConsultarAsync(
            "SELECT IdCuentaBancaria, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Iban, Activa FROM CuentasBancarias WHERE IdSociedad = ? ORDER BY EntidadOriginal, Oficina, Cuenta",
            f => (Id: f.Entero("IdCuentaBancaria"), Entidad: f.Texto("EntidadOriginal") ?? "", Oficina: f.Texto("Oficina") ?? "", Cuenta: f.Texto("Cuenta") ?? "",
                Divisa: f.Texto("Divisa") ?? "EUR", Nombre: f.Texto("Nombre"), Iban: f.Texto("Iban"), Activa: f.Logico("Activa")), contexto.IdSociedad);
        var contables = (await s.ConsultarAsync(
            "SELECT cfg.IdCuentaBancaria, cc.Codigo, cc.Nombre FROM (ConfiguracionesContablesCuentaBancaria AS cfg INNER JOIN CuentasBancarias AS cb ON cfg.IdCuentaBancaria = cb.IdCuentaBancaria) " +
            "INNER JOIN CuentasContables AS cc ON cfg.IdIdentidadContableBanco = cc.IdIdentidadContable WHERE cb.IdSociedad = ? AND cfg.FechaHasta IS NULL AND cc.IdSociedadEjercicio = ?",
            f => (Id: f.Entero("IdCuentaBancaria"), Codigo: f.Texto("Codigo") ?? "", Nombre: f.Texto("Nombre") ?? ""), contexto.IdSociedad, contexto.IdSociedadEjercicio))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First());
        const string vivos = "(ExtractosBancarios AS e INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria)";
        var movimientos = (await s.ConsultarAsync(
            $"SELECT e.IdCuentaBancaria, COUNT(*) AS N, MAX(l.FechaOperacion) AS Ultimo FROM LineasExtractoBancario AS l INNER JOIN {vivos} ON l.IdExtractoBancario = e.IdExtractoBancario " +
            "WHERE lo.IdSociedad = ? AND lo.Estado <> ? GROUP BY e.IdCuentaBancaria",
            f => (Id: f.EnteroNulo("IdCuentaBancaria") ?? 0, N: f.Entero("N"), Ultimo: f.FechaNula("Ultimo")), contexto.IdSociedad, ServicioImportacionN43.EstadoRevertido))
            .ToDictionary(x => x.Id);
        var extractos = await s.ConsultarAsync(
            $"SELECT e.IdCuentaBancaria, e.IdExtractoBancario, e.FechaFin, e.SaldoFinal, lo.IdLoteImportacionBancaria FROM {vivos} WHERE lo.IdSociedad = ? AND lo.Estado <> ?",
            f => (Id: f.EnteroNulo("IdCuentaBancaria") ?? 0, Ext: f.Entero("IdExtractoBancario"), Fin: f.Fecha("FechaFin"), Saldo: f.Decimal("SaldoFinal"), Lote: f.Entero("IdLoteImportacionBancaria")),
            contexto.IdSociedad, ServicioImportacionN43.EstadoRevertido);
        var porCuenta = extractos.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.ToList());
        var saldosContables = new Dictionary<string, decimal>();
        foreach (var codigo in contables.Values.Select(c => c.Codigo).Distinct())
        {
            var v = await s.EscalarAsync("SELECT SUM(IIF(a.ImporteDebe IS NULL, 0, a.ImporteDebe)) - SUM(IIF(a.ImporteHaber IS NULL, 0, a.ImporteHaber)) FROM Apuntes AS a " +
                "INNER JOIN Asientos AS x ON a.IdAsiento = x.IdAsiento WHERE x.IdSociedadEjercicio = ? AND a.CuentaContable = ?", contexto.IdSociedadEjercicio, codigo);
            saldosContables[codigo] = v is null or DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
        }
        return cuentas.Select(c =>
        {
            var ext = porCuenta.GetValueOrDefault(c.Id) ?? [];
            var ultimo = ext.OrderByDescending(x => x.Fin).ThenByDescending(x => x.Ext).Cast<(int Id, int Ext, DateTime Fin, decimal Saldo, int Lote)?>().FirstOrDefault();
            var contable = contables.TryGetValue(c.Id, out var cc) ? cc : default;
            var mov = movimientos.TryGetValue(c.Id, out var m) ? m : default;
            return new CuentaBancariaN43(c.Id, c.Entidad, c.Oficina, c.Cuenta, c.Divisa, c.Nombre, c.Iban, c.Activa,
                contable.Codigo, contable.Nombre, ultimo?.Saldo, ultimo?.Fin, contable.Codigo is { } cod ? saldosContables[cod] : null,
                mov.N, mov.Ultimo, ext.Select(x => x.Lote).Distinct().Count());
        }).ToList();
    });

    /// <summary>Lotes con extractos de la cuenta (también los revertidos), del más reciente al más antiguo.</summary>
    public Task<IReadOnlyList<LoteN43>> LotesAsync(ContextoSociedadEjercicio contexto, int idCuentaBancaria) => bd.EjecutarAsync<IReadOnlyList<LoteN43>>(async s =>
    {
        var movs = (await s.ConsultarAsync(
            "SELECT e.IdLoteImportacionBancaria, COUNT(*) AS N FROM LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario " +
            "WHERE e.IdCuentaBancaria = ? GROUP BY e.IdLoteImportacionBancaria",
            f => (Lote: f.Entero("IdLoteImportacionBancaria"), N: f.Entero("N")), idCuentaBancaria)).ToDictionary(x => x.Lote, x => x.N);
        var filas = await s.ConsultarAsync(
            "SELECT lo.IdLoteImportacionBancaria, lo.NombreFichero, lo.Formato, lo.VersionEsquema, lo.Estado, lo.FechaHoraInicio, lo.NumeroMovimientos, lo.NumeroAdvertencias, lo.RutaOrigen, " +
            "lo.FechaHoraReversion, lo.OperadorReversion, lo.MotivoReversion, MIN(e.FechaInicio) AS Desde, MAX(e.FechaFin) AS Hasta " +
            "FROM ExtractosBancarios AS e INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria " +
            "WHERE lo.IdSociedad = ? AND e.IdCuentaBancaria = ? " +
            "GROUP BY lo.IdLoteImportacionBancaria, lo.NombreFichero, lo.Formato, lo.VersionEsquema, lo.Estado, lo.FechaHoraInicio, lo.NumeroMovimientos, lo.NumeroAdvertencias, lo.RutaOrigen, " +
            "lo.FechaHoraReversion, lo.OperadorReversion, lo.MotivoReversion",
            f => new LoteN43(f.Entero("IdLoteImportacionBancaria"), f.Texto("NombreFichero") ?? "", f.Texto("Formato") ?? "", Origen(f.Texto("Formato"), f.Texto("VersionEsquema")),
                f.Texto("Estado") ?? "", f.Fecha("FechaHoraInicio"), f.Fecha("Desde"), f.Fecha("Hasta"), movs.GetValueOrDefault(f.Entero("IdLoteImportacionBancaria")),
                f.EnteroNulo("NumeroMovimientos") ?? 0, f.EnteroNulo("NumeroAdvertencias") ?? 0, f.Texto("RutaOrigen"), f.FechaNula("FechaHoraReversion"), f.Texto("OperadorReversion"),
                f.Texto("MotivoReversion")),
            contexto.IdSociedad, idCuentaBancaria);
        return filas.OrderByDescending(l => l.Hasta).ThenByDescending(l => l.IdLote).ToList();
    });

    public Task<IReadOnlyList<IncidenciaLoteN43>> IncidenciasAsync(int idLote) => bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT NumeroLineaFisica, Codigo, Severidad, Mensaje, RegistroOriginal FROM IncidenciasImportacionBancaria WHERE IdLoteImportacionBancaria = ? ORDER BY IdIncidenciaImportacionBancaria",
        f => new IncidenciaLoteN43(f.EnteroNulo("NumeroLineaFisica"), f.Texto("Codigo") ?? "", f.Texto("Severidad") ?? "", f.Texto("Mensaje") ?? "", f.Texto("RegistroOriginal")), idLote));

    /// <summary>Cuentas 572 imputables del ejercicio, para asociarlas a una cuenta bancaria nueva.</summary>
    public Task<IReadOnlyList<CuentaContableBanco>> CuentasContablesBancoAsync(ContextoSociedadEjercicio contexto) => bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT IdIdentidadContable, Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo LIKE '572%' AND EsImputable = True ORDER BY Codigo",
        f => new CuentaContableBanco(f.Entero("IdIdentidadContable"), f.Texto("Codigo") ?? "", f.Texto("Nombre") ?? ""), contexto.IdSociedadEjercicio));

    private static string Origen(string? formato, string? version) => version == "ERP" ? "Migrado del ERP" : formato == ServicioImportacionN43.Formato ? "Fichero Norma 43" : formato ?? "";
}
