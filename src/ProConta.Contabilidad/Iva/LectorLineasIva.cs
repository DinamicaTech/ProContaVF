using System.Globalization;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Iva;

/// <summary>
/// Lee de Access las líneas de IVA del ejercicio (solo SELECT): los registros de IVASoportado e IVARepercutido cuyo asiento está
/// contabilizado (EstadoAsiento = 1, sin el cierre), con sus desgloses, los apuntes de su asiento, los centros de coste y la
/// actividad de cada centro, y los clasifica con <see cref="ClasificadorLineasIva"/>.
/// </summary>
public sealed class LectorLineasIva(IBaseDatos bd)
{
    private const string Contabilizado = "a.EstadoAsiento = 1 AND a.TipoAsiento <> 3";

    public async Task<IReadOnlyList<LineaIva>> LeerAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default)
    {
        var actividades = await new LectorActividadesIva(bd).PorCentroAsync(ctx.IdSociedad);
        var (registros, centros) = await bd.EjecutarAsync(async s =>
        {
            var id = ctx.IdSociedadEjercicio;
            var cabSop = await s.ConsultarAsync(
                "SELECT r.IdIVASoportado AS Id, r.IdAsiento, a.FechaContable, r.FechaExpedicion, t.Codigo AS CodigoTipo, r.InversionSujetoPasivo, r.NombreProveedor AS Tercero, " +
                "r.IdentificacionFiscalProveedor AS Nif, r.NumeroFactura, r.BienInversion FROM (IVASoportado AS r INNER JOIN Asientos AS a ON r.IdAsiento = a.IdAsiento) " +
                $"LEFT JOIN TiposOperacion AS t ON r.IdTipoOperacion = t.IdTipoOperacion WHERE r.IdSociedadEjercicio = ? AND {Contabilizado}",
                f => (Id: f.Entero("Id"), Asiento: f.Entero("IdAsiento"), Fecha: f.Fecha("FechaContable"), Expedicion: f.FechaNula("FechaExpedicion"), Codigo: f.Texto("CodigoTipo")?.Trim(),
                    Isp: !f.EsNulo("InversionSujetoPasivo") && f.Logico("InversionSujetoPasivo"), Tercero: (f.Texto("Tercero") ?? "").Trim(),
                    Nif: f.Texto("Nif")?.Trim(), Factura: f.Texto("NumeroFactura")?.Trim(), Bien: !f.EsNulo("BienInversion") && f.Logico("BienInversion")), id);
            var desSop = await s.ConsultarAsync(
                "SELECT d.IdIVASoportado AS Id, d.BaseImponible, d.TipoIVA, d.CuotaSoportada, d.CuotaDevengadaAutorrepercutida FROM DesglosesIVASoportado AS d " +
                $"INNER JOIN (IVASoportado AS r INNER JOIN Asientos AS a ON r.IdAsiento = a.IdAsiento) ON d.IdIVASoportado = r.IdIVASoportado WHERE r.IdSociedadEjercicio = ? AND {Contabilizado} ORDER BY d.IdIVASoportado, d.Orden",
                f => (Id: f.Entero("Id"), D: new DesgloseIvaLeido(f.DecimalNulo("BaseImponible") ?? 0m, f.DecimalNulo("TipoIVA") ?? 0m, f.DecimalNulo("CuotaSoportada") ?? 0m,
                    f.DecimalNulo("CuotaDevengadaAutorrepercutida") ?? 0m)), id);
            var cabRep = await s.ConsultarAsync(
                "SELECT r.IdIVARepercutido AS Id, r.IdAsiento, a.FechaContable, r.FechaExpedicion, r.NombreCliente AS Tercero, r.IdentificacionFiscalCliente AS Nif, r.NumeroFactura, " +
                "tf.EsRectificativa FROM ((IVARepercutido AS r INNER JOIN Asientos AS a ON r.IdAsiento = a.IdAsiento) LEFT JOIN Facturas AS fa ON r.IdFactura = fa.IdFactura) " +
                $"LEFT JOIN TiposFactura AS tf ON fa.TipoFactura = tf.TipoFactura WHERE r.IdSociedadEjercicio = ? AND {Contabilizado}",
                f => (Id: f.Entero("Id"), Asiento: f.Entero("IdAsiento"), Fecha: f.Fecha("FechaContable"), Expedicion: f.FechaNula("FechaExpedicion"), Tercero: (f.Texto("Tercero") ?? "").Trim(),
                    Nif: f.Texto("Nif")?.Trim(), Factura: f.Texto("NumeroFactura")?.Trim(), Rect: !f.EsNulo("EsRectificativa") && f.Logico("EsRectificativa")), id);
            var desRep = await s.ConsultarAsync(
                "SELECT d.IdIVARepercutido AS Id, d.BaseImponible, d.TipoIVA, d.CuotaRepercutida, d.CalificacionOperacion, d.CausaExencion FROM DesglosesIVARepercutido AS d " +
                $"INNER JOIN (IVARepercutido AS r INNER JOIN Asientos AS a ON r.IdAsiento = a.IdAsiento) ON d.IdIVARepercutido = r.IdIVARepercutido WHERE r.IdSociedadEjercicio = ? AND {Contabilizado} ORDER BY d.IdIVARepercutido, d.Orden",
                f => (Id: f.Entero("Id"), D: new DesgloseIvaLeido(f.DecimalNulo("BaseImponible") ?? 0m, f.DecimalNulo("TipoIVA") ?? 0m, f.DecimalNulo("CuotaRepercutida") ?? 0m, 0m,
                    f.Texto("CalificacionOperacion"), f.Texto("CausaExencion"))), id);
            var apuntes = (await s.ConsultarAsync(
                    "SELECT p.IdAsiento, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.IdCentroCoste FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                    $"WHERE a.IdSociedadEjercicio = ? AND {Contabilizado} AND (EXISTS (SELECT r.IdIVASoportado FROM IVASoportado AS r WHERE r.IdAsiento = a.IdAsiento) " +
                    "OR EXISTS (SELECT r.IdIVARepercutido FROM IVARepercutido AS r WHERE r.IdAsiento = a.IdAsiento)) ORDER BY p.IdAsiento, p.Orden",
                    f => (Asiento: f.Entero("IdAsiento"), A: new ApunteIvaLeido((f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("ImporteDebe") ?? 0m,
                        f.DecimalNulo("ImporteHaber") ?? 0m, f.EnteroNulo("IdCentroCoste"))), id))
                .GroupBy(x => x.Asiento).ToDictionary(g => g.Key, g => (IReadOnlyList<ApunteIvaLeido>)g.Select(x => x.A).ToList());
            var dSop = desSop.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<DesgloseIvaLeido>)g.Select(x => x.D).ToList());
            var dRep = desRep.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<DesgloseIvaLeido>)g.Select(x => x.D).ToList());
            IReadOnlyList<ApunteIvaLeido> Apuntes(int asiento) => apuntes.TryGetValue(asiento, out var l) ? l : [];
            var regs = new List<RegistroIvaLeido>();
            regs.AddRange(cabSop.Select(c => new RegistroIvaLeido(SentidoIva.Soportado, c.Asiento, c.Fecha, c.Codigo, c.Isp, c.Tercero, c.Nif, c.Factura,
                dSop.TryGetValue(c.Id, out var d) ? d : [], Apuntes(c.Asiento), c.Expedicion) { BienInversion = c.Bien }));
            regs.AddRange(cabRep.Select(c => new RegistroIvaLeido(SentidoIva.Repercutido, c.Asiento, c.Fecha, null, false, c.Tercero, c.Nif, c.Factura,
                dRep.TryGetValue(c.Id, out var d) ? d : [], Apuntes(c.Asiento), c.Expedicion) { Rectificativa = c.Rect }));
            return ((IReadOnlyList<RegistroIvaLeido>)regs, await CentrosAsync(s, ctx.IdSociedad));
        }, cancelacion);
        return ClasificadorLineasIva.Clasificar(registros, ctx.Ejercicio, actividades, centros);
    }

    /// <summary>Centros de coste de la sociedad con el nombre de su raíz (el centro de nivel 1 del que cuelgan).</summary>
    public static async Task<IReadOnlyDictionary<int, CentroIva>> CentrosAsync(ISesionDatos s, string idSociedad)
    {
        var filas = await s.ConsultarAsync("SELECT IdCentroCoste, IdCentroCosteRaiz, Nombre FROM CentrosCoste WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdCentroCoste"), Raiz: f.EnteroNulo("IdCentroCosteRaiz"), Nombre: (f.Texto("Nombre") ?? "").Trim()), idSociedad);
        var porId = filas.ToDictionary(c => c.Id);
        return filas.ToDictionary(c => c.Id, c => new CentroIva(c.Raiz is int r && porId.TryGetValue(r, out var raiz) ? raiz.Nombre : c.Nombre, c.Nombre));
    }

    /// <summary>
    /// Prorrata provisional del ejercicio en tanto por uno: SociedadesEjercicios.PorcentajeProrrataAñoAnterior, que se guarda en
    /// porcentaje (15 = 15 %). Null si está vacía.
    /// </summary>
    public Task<decimal?> ProrrataProvisionalAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s => await s.EscalarAsync("SELECT [PorcentajeProrrataAñoAnterior] FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio) is { } v and not DBNull
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) / 100m
            : (decimal?)null, cancelacion);

    /// <summary>
    /// Prorrata definitiva del ejercicio en tanto por uno (contabilidad/procesos/prorrata): SociedadesEjercicios.PorcentajeProrrataDefinitiva,
    /// que se guarda en porcentaje (20 = 20 %). Null mientras no se fije.
    /// </summary>
    public Task<decimal?> ProrrataDefinitivaAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s => await s.EscalarAsync("SELECT PorcentajeProrrataDefinitiva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio) is { } v and not DBNull
            ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) / 100m
            : (decimal?)null, cancelacion);

    /// <summary>Las líneas de IVA del ejercicio con su 303 y su prorrata (provisional y definitiva del ejercicio).</summary>
    public async Task<(IReadOnlyList<LineaIva> Lineas, decimal? Provisional, decimal? Definitiva, IvaEjercicio Iva)> CalcularAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default)
    {
        var lineas = await LeerAsync(ctx, cancelacion);
        var provisional = await ProrrataProvisionalAsync(ctx, cancelacion);
        var definitiva = await ProrrataDefinitivaAsync(ctx, cancelacion);
        return (lineas, provisional, definitiva, CalculoIvaEjercicio.Calcular(lineas, provisional, definitiva));
    }
}
