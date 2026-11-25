using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;

namespace ProConta.Norma43.Conciliacion;

/// <summary>
/// Actualiza los asientos de banco de los movimientos N43 enlazados cuando ventas, compras o terceros cambian algo que sale en ellos
/// (norma43/conciliacion/actualizar-textos): la descripción abreviada de una factura de venta, el número o el concepto abreviado de una
/// de compra, o el nombre del tercero. Busca los movimientos afectados que contabilizó ProContaVF (los del ERP no se tocan) y les vuelve a
/// generar los textos con <see cref="ServicioConciliacionN43.ActualizarTextosAsync"/>: mismo asiento, mismas cuentas e importes, con
/// registro de cambios, también en un trimestre cerrado. En un ejercicio cerrado no cambia nada y lo dice.
/// </summary>
public sealed class AsientosBancoN43(IBaseDatos bd, ServicioConciliacionN43 conciliacion) : IAsientosBancoEnlazados
{
    public AsientosBancoN43(IBaseDatos bd) : this(bd, new ServicioConciliacionN43(bd)) { }

    public async Task<ResultadoAsientosBanco> FacturaVentaCambiadaAsync(int idFactura, Actor actor)
    {
        var lineas = await bd.EjecutarAsync(async s =>
        {
            var ids = new List<int?>();
            foreach (var tabla in new[] { "Facturas", "ReintentosSEPA" })
                foreach (var f in await s.ConsultarAsync($"SELECT LineaN43, LineaN43Devolucion FROM {tabla} WHERE IdFactura = ?",
                             x => (x.EnteroNulo("LineaN43"), x.EnteroNulo("LineaN43Devolucion")), idFactura))
                    ids.AddRange([f.Item1, f.Item2]);
            ids.AddRange(await s.ConsultarAsync("SELECT LineaN43 FROM CobrosParcialesN43 WHERE IdFactura = ?", x => x.EnteroNulo("LineaN43"), idFactura));
            return ids.OfType<int>().ToList();
        });
        return await ActualizarAsync(lineas, actor, "Cambio en la factura de venta enlazada");
    }

    public async Task<ResultadoAsientosBanco> FacturaCompraCambiadaAsync(int idRegistro, Actor actor)
    {
        var lineas = await bd.EjecutarAsync(async s => (await s.ConsultarAsync("SELECT LineaN43 FROM FacturasCompra WHERE IdRegistro = ?",
            x => x.EnteroNulo("LineaN43"), idRegistro)).OfType<int>().ToList());
        return await ActualizarAsync(lineas, actor, "Cambio en la factura de compra enlazada");
    }

    /// <summary>
    /// El nombre del tercero sale en la descripción de los asientos de banco imputados a la cuenta de su cliente o su proveedor
    /// (norma43/conciliacion 2026-11-13 10:50): se actualizan los de todas sus sociedades.
    /// </summary>
    public async Task<ResultadoAsientosBanco> TerceroCambiadoAsync(int idTercero, Actor actor)
    {
        var lineas = await bd.EjecutarAsync(async s =>
        {
            var identidades = new HashSet<int>();
            identidades.UnionWith(await s.ConsultarAsync(
                "SELECT c.IdIdentidadContable FROM CuentasClientesSociedad AS c INNER JOIN Clientes AS x ON c.IdCliente = x.IdCliente WHERE x.IdTercero = ?",
                f => f.Entero("IdIdentidadContable"), idTercero));
            identidades.UnionWith(await s.ConsultarAsync(
                "SELECT c.IdIdentidadContable FROM CuentasProveedoresSociedad AS c INNER JOIN Proveedores AS x ON c.IdProveedor = x.IdProveedor WHERE x.IdTercero = ?",
                f => f.Entero("IdIdentidadContable"), idTercero));
            if (identidades.Count == 0) return [];
            var lista = string.Join(",", identidades.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            return await s.ConsultarAsync(
                $"SELECT DISTINCT IdLineaExtractoBancario FROM ImputacionesLineaExtractoBancario WHERE Estado <> 'DESCARTADA' AND IdIdentidadContable IN ({lista})",
                f => f.Entero("IdLineaExtractoBancario"));
        });
        return await ActualizarAsync(lineas, actor, "Cambio del nombre del tercero");
    }

    public async Task<IReadOnlyList<MovimientoBancoTercero>> MovimientosDeSubcuentaAsync(int idIdentidadAntigua, IReadOnlyCollection<int>? idsFacturaVenta,
        IReadOnlyCollection<int>? idsRegistroCompra) => await bd.EjecutarAsync(async s =>
    {
        var imputadas = (await s.ConsultarAsync(
            "SELECT DISTINCT IdLineaExtractoBancario FROM ImputacionesLineaExtractoBancario WHERE Estado <> 'DESCARTADA' AND IdIdentidadContable = ?",
            f => f.Entero("IdLineaExtractoBancario"), idIdentidadAntigua)).ToHashSet();
        HashSet<int> lineas;
        if (idsFacturaVenta is null && idsRegistroCompra is null) lineas = imputadas;
        else
        {
            lineas = [];
            foreach (var bloque in (idsFacturaVenta ?? []).Distinct().Chunk(200))
            {
                var lista = Lista(bloque);
                foreach (var tabla in new[] { "Facturas", "ReintentosSEPA" })
                    foreach (var f in await s.ConsultarAsync($"SELECT LineaN43, LineaN43Devolucion FROM {tabla} WHERE IdFactura IN ({lista})",
                                 x => (x.EnteroNulo("LineaN43"), x.EnteroNulo("LineaN43Devolucion"))))
                        lineas.UnionWith(new[] { f.Item1, f.Item2 }.OfType<int>());
                lineas.UnionWith(await s.ConsultarAsync($"SELECT LineaN43 FROM CobrosParcialesN43 WHERE IdFactura IN ({lista})", x => x.Entero("LineaN43")));
            }
            foreach (var bloque in (idsRegistroCompra ?? []).Distinct().Chunk(200))
                lineas.UnionWith((await s.ConsultarAsync($"SELECT LineaN43 FROM FacturasCompra WHERE IdRegistro IN ({Lista(bloque)})",
                    x => x.EnteroNulo("LineaN43"))).OfType<int>());
        }
        var asientos = new Dictionary<int, List<int>>();
        foreach (var bloque in lineas.Chunk(200))
            foreach (var f in await s.ConsultarAsync(
                         "SELECT c.IdLineaExtractoBancario, c.IdAsiento FROM ContabilizacionesLineaExtracto AS c INNER JOIN Asientos AS a ON c.IdAsiento = a.IdAsiento " +
                         $"WHERE c.IdLineaExtractoBancario IN ({Lista(bloque)})",
                         x => (Linea: x.Entero("IdLineaExtractoBancario"), Asiento: x.Entero("IdAsiento"))))
            {
                if (!asientos.TryGetValue(f.Linea, out var l)) asientos[f.Linea] = l = [];
                if (!l.Contains(f.Asiento)) l.Add(f.Asiento);
            }
        return (IReadOnlyList<MovimientoBancoTercero>)lineas.OrderBy(l => l)
            .Select(l => new MovimientoBancoTercero(l, asientos.GetValueOrDefault(l) ?? [], imputadas.Contains(l))).ToList();
    });

    public async Task<int> ReimputarAsync(ISesionDatos s, IReadOnlyCollection<int> lineas, int idIdentidadAntigua, int idIdentidadNueva, bool palabrasClave)
    {
        if (palabrasClave)
            await s.EjecutarAsync("UPDATE PalabrasClaveBancarias SET IdIdentidadContable = ? WHERE IdIdentidadContable = ?", idIdentidadNueva, idIdentidadAntigua);
        var n = 0;
        foreach (var bloque in lineas.Distinct().Chunk(200))
            n += await s.EjecutarAsync(
                "UPDATE ImputacionesLineaExtractoBancario SET IdIdentidadContable = ? WHERE Estado <> 'DESCARTADA' AND IdIdentidadContable = ? " +
                $"AND IdLineaExtractoBancario IN ({Lista(bloque)})",
                idIdentidadNueva, idIdentidadAntigua);
        return n;
    }

    private static string Lista(IEnumerable<int> ids) => string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));

    /// <summary>
    /// Vuelve a generar los textos de los asientos que la conciliación generó para esas líneas, sociedad a sociedad. Las líneas sin
    /// asiento o con un asiento del ERP no cuentan; las que ya tenían los textos que tocan tampoco (no se graba nada).
    /// </summary>
    private async Task<ResultadoAsientosBanco> ActualizarAsync(IReadOnlyCollection<int> lineas, Actor actor, string motivo)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (lineas.Count == 0) return ResultadoAsientosBanco.Nada;
        var propias = await bd.EjecutarAsync(async s =>
        {
            var r = new List<(int Linea, string Sociedad)>();
            foreach (var bloque in lineas.Distinct().Chunk(200))
            {
                var claves = string.Join(",", bloque.Select(i => $"'{i.ToString(CultureInfo.InvariantCulture)}'"));
                r.AddRange(await s.ConsultarAsync(
                    "SELECT a.ClaveEjecucion, se.IdSociedad FROM Asientos AS a INNER JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                    $"WHERE a.EsAutomatico = True AND a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.ClaveEjecucion IN ({claves})",
                    f => (int.Parse((f.Texto("ClaveEjecucion") ?? "0").Trim(), CultureInfo.InvariantCulture), (f.Texto("IdSociedad") ?? "").Trim()),
                    ServicioConciliacionN43.MotorPropietario, ServicioConciliacionN43.CodigoProceso));
            }
            return r;
        });
        var actualizados = 0;
        var errores = new List<Nucleo.Resultados.Error>();
        foreach (var grupo in propias.GroupBy(p => p.Sociedad, StringComparer.OrdinalIgnoreCase))
        {
            var r = await conciliacion.ActualizarTextosAsync(grupo.Key, grupo.Select(p => p.Linea).Distinct().ToList(), actor, motivo);
            actualizados += r.Hechas - r.SinCambios.Count;
            errores.AddRange(r.Rechazadas.Values.Select(e => e[0]));
        }
        return new ResultadoAsientosBanco(actualizados, errores.Count, Resumir(errores));
    }

    /// <summary>Un motivo por clase de rechazo, con cuántas líneas lo tienen, para que el aviso no repita cada asiento.</summary>
    internal static IReadOnlyList<string> Resumir(IReadOnlyList<Nucleo.Resultados.Error> errores) =>
        errores.GroupBy(e => e.Codigo).Select(g => g.Key switch
        {
            "Conciliacion.EjercicioCerrado" => $"ejercicio cerrado ({g.Count()}), se queda con el texto anterior.",
            "Conciliacion.AsientoDistinto" => $"sus cuentas han cambiado ({g.Count()}), vuelve a contabilizar el movimiento desde Líneas N43.",
            _ => g.Count() == 1 ? g.First().Mensaje : $"{g.First().Mensaje} ({g.Count()})",
        }).ToList();
}
