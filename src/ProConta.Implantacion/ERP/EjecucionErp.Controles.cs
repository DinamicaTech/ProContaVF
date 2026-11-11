using System.Globalization;

namespace ProConta.Implantacion.ERP;

// Controles de la ejecución contra el ERP, para el informe.
internal sealed partial class EjecucionErp
{
    private static string Eur(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES"));

    private async Task ControlesAsync()
    {
        await GrabarMapasAsync();
        string Desde(string tipo, string alias, string campo) =>
            $"INNER JOIN MapasIdentidadERP AS m ON m.IdDestino = {alias}.{campo} WHERE m.IdMigracionERP = {_idMigracion} AND m.TipoEntidad = '{tipo}'";

        // Ventas por año.
        var lineasDe = o.Tabla("LineasFacturaVenta").ToLookup(l => l.E("Id") ?? -1);
        var erp = o.Tabla("FacturasVenta").Where(f => f.F("Fecha") is not null && f.E("Id") != ReglasErp.FacturaFicticia)
            .GroupBy(f => f.F("Fecha")!.Value.Year).ToDictionary(g => g.Key, g => (N: g.Count(), Total: g.Sum(f => f.Dec("ImporteTotal")),
                Lineas: g.Sum(f => ReglasErp.TotalLineas(lineasDe[f.E("Id") ?? -2].Select(LineaErp)))));
        var destino = (await ConsultarAsync($"SELECT Year(f.FechaFactura) AS A, COUNT(*) AS N, SUM(f.ImporteTotal) AS T, SUM(IIF(f.IdAsientoContable IS NULL, 1, 0)) AS SinAsiento, " +
                $"SUM(IIF(f.FechaFactura < #2026-10-01# AND f.IdAsientoContable IS NULL, 1, 0)) AS SinAsientoDebido FROM Facturas AS f {Desde(TiposErp.Factura, "f", "IdFactura")} GROUP BY Year(f.FechaFactura)",
                f => (A: Convert.ToInt32(f.Valor("A"), CultureInfo.InvariantCulture), N: f.Entero("N"), T: f.DecimalNulo("T") ?? 0m, Sin: f.Entero("SinAsiento"), SinDebido: f.Entero("SinAsientoDebido"))))
            .ToDictionary(x => x.A);
        var conLibro = (await ConsultarAsync($"SELECT Year(f.FechaFactura) AS A, COUNT(*) AS N FROM (Facturas AS f INNER JOIN IVARepercutido AS r ON r.IdFactura = f.IdFactura) {Desde(TiposErp.Factura, "f", "IdFactura")} GROUP BY Year(f.FechaFactura)",
            f => (A: Convert.ToInt32(f.Valor("A"), CultureInfo.InvariantCulture), N: f.Entero("N")))).ToDictionary(x => x.A, x => x.N);
        var libroAnio = (await ConsultarAsync("SELECT se.Ejercicio, COUNT(*) AS N, SUM(IIF(r.IdFactura IS NULL, 1, 0)) AS SinFactura FROM IVARepercutido AS r INNER JOIN SociedadesEjercicios AS se ON r.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE se.IdSociedad = ? GROUP BY se.Ejercicio",
            f => (A: int.Parse(f.Texto("Ejercicio") ?? "0", CultureInfo.InvariantCulture), N: f.Entero("N"), Sin: f.Entero("SinFactura")), Sociedad)).ToDictionary(x => x.A);
        _r.Controles.Add(new SeccionControl("Facturas de venta por año (ERP frente a ProContaVF)",
            ["Año", "Nº ERP", "Total ERP", "Σ líneas ERP", "Nº migradas", "Total migrado", "Diferencia", "Sin asiento", "De ellas debían tenerlo", "Con registro del libro", "Libro sin factura"],
            erp.Keys.Union(destino.Keys).Order().Select(a =>
            {
                var e = erp.GetValueOrDefault(a);
                var d = destino.GetValueOrDefault(a);
                var l = libroAnio.GetValueOrDefault(a);
                return (IReadOnlyList<string>)[Txt(a), Txt(e.N), Eur(e.Total), Eur(e.Lineas), Txt(d.N), Eur(d.T), Eur(d.T - e.Total), Txt(d.Sin), Txt(d.SinDebido), Txt(conLibro.GetValueOrDefault(a)), Txt(l.Sin)];
            }).ToList(),
            "«Σ líneas ERP» es Σ Cantidad × Precio de las líneas (M-12); «Total migrado» sale de las líneas, con la retención. Las facturas desde el 01/10/2026 entran sin asiento a propósito. " +
            "«Libro sin factura»: registros de F_IVR importados de ContaSol que no se han enlazado con ninguna factura."));

        // Compras por ejercicio.
        var erpCompras = o.Tabla("FacturasCompra").GroupBy(c => c.E("Ejercicio") ?? 0).ToDictionary(g => g.Key, g => (N: g.Count(), Total: g.Sum(c => c.Dec("ImporteTotal")), SinAsiento: g.Count(c => c.E("ASIAPU") is not > 0)));
        var destCompras = (await ConsultarAsync($"SELECT c.Ejercicio AS A, COUNT(*) AS N, SUM(c.ImporteTotal) AS T, SUM(IIF(c.IdAsientoContable IS NULL, 1, 0)) AS SinAsiento, SUM(IIF(c.IdProveedor IS NULL, 1, 0)) AS SinProveedor, " +
                $"SUM(IIF(c.IdRegistroIVA IS NULL, 1, 0)) AS SinLibro FROM FacturasCompra AS c {Desde(TiposErp.Compra, "c", "IdRegistro")} GROUP BY c.Ejercicio",
                f => (A: int.TryParse(f.Texto("A"), out var a) ? a : 0, N: f.Entero("N"), T: f.DecimalNulo("T") ?? 0m, Sin: f.Entero("SinAsiento"), SinProv: f.Entero("SinProveedor"), SinLibro: f.Entero("SinLibro"))))
            .GroupBy(x => x.A).ToDictionary(g => g.Key, g => g.First());
        _r.Controles.Add(new SeccionControl("Facturas de compra por ejercicio",
            ["Ejercicio", "Nº ERP", "Total ERP", "Sin asiento en el ERP", "Nº migradas", "Total migrado", "Sin asiento", "Sin proveedor", "Sin registro del libro"],
            erpCompras.Keys.Union(destCompras.Keys).Order().Select(a =>
            {
                var e = erpCompras.GetValueOrDefault(a);
                var d = destCompras.GetValueOrDefault(a);
                return (IReadOnlyList<string>)[Txt(a), Txt(e.N), Eur(e.Total), Txt(e.SinAsiento), Txt(d.N), Eur(d.T), Txt(d.Sin), Txt(d.SinProv), Txt(d.SinLibro)];
            }).ToList()));

        // Norma 43.
        var n43 = (await ConsultarAsync($"SELECT Year(l.FechaOperacion) AS A, COUNT(*) AS N, SUM(IIF(l.Sentido = 'D', l.Importe, 0)) AS Debe, SUM(IIF(l.Sentido = 'H', l.Importe, 0)) AS Haber, " +
                $"SUM(IIF(c.IdLineaExtractoBancario IS NULL, 1, 0)) AS SinAsiento, SUM(IIF(i.IdLineaExtractoBancario IS NULL, 1, 0)) AS SinCuenta " +
                $"FROM ((LineasExtractoBancario AS l LEFT JOIN (SELECT DISTINCT IdLineaExtractoBancario FROM ContabilizacionesLineaExtracto) AS c ON l.IdLineaExtractoBancario = c.IdLineaExtractoBancario) " +
                $"LEFT JOIN (SELECT DISTINCT IdLineaExtractoBancario FROM ImputacionesLineaExtractoBancario) AS i ON l.IdLineaExtractoBancario = i.IdLineaExtractoBancario) " +
                $"{Desde(TiposErp.LineaN43, "l", "IdLineaExtractoBancario")} GROUP BY Year(l.FechaOperacion)",
                f => (A: Convert.ToInt32(f.Valor("A"), CultureInfo.InvariantCulture), N: f.Entero("N"), Debe: f.DecimalNulo("Debe") ?? 0m, Haber: f.DecimalNulo("Haber") ?? 0m, Sin: f.Entero("SinAsiento"), SinCuenta: f.Entero("SinCuenta"))))
            .OrderBy(x => x.A).ToList();
        _r.Controles.Add(new SeccionControl("Líneas de Norma 43 por año", ["Año", "Líneas", "Cargos", "Abonos", "Sin asiento", "Sin cuenta imputada"],
            n43.Select(x => (IReadOnlyList<string>)[Txt(x.A), Txt(x.N), Eur(x.Debe), Eur(x.Haber), Txt(x.Sin), Txt(x.SinCuenta)]).ToList(),
            $"En el ERP: {o.Tabla("N43Lineas").Count} líneas, {o.Tabla("N43Lineas").Count(l => l.E("ASIAPU") is not > 0)} sin asiento y {o.Tabla("N43Lineas").Count(l => l.B("Eliminar"))} marcadas para eliminar."));

        // Recuentos en el destino.
        var recuentos = new List<IReadOnlyList<string>>();
        foreach (var (tipo, tabla, campo) in new[]
                 {
                     (TiposErp.Persona, "Terceros", "IdTercero"), (TiposErp.Cliente, "Clientes", "IdCliente"), (TiposErp.Concepto, "Conceptos", "IdConcepto"),
                     (TiposErp.Factura, "Facturas", "IdFactura"), (TiposErp.Programada, "FacturasProgramadas", "IdFacturaProgramada"), (TiposErp.Compra, "FacturasCompra", "IdRegistro"),
                     (TiposErp.ExtractoN43, "ExtractosBancarios", "IdExtractoBancario"), (TiposErp.LineaN43, "LineasExtractoBancario", "IdLineaExtractoBancario"), (TiposErp.Activo, "Activos", "IdActivo"),
                 })
        {
            var n = Convert.ToInt32(await _bd.EjecutarAsync(s => s.EscalarAsync($"SELECT COUNT(*) FROM {tabla} AS t WHERE t.{campo} IN (SELECT IdDestino FROM MapasIdentidadERP WHERE IdMigracionERP = ? AND TipoEntidad = ?)", _idMigracion, tipo)), CultureInfo.InvariantCulture);
            var total = Convert.ToInt32(await _bd.EjecutarAsync(s => s.EscalarAsync($"SELECT COUNT(*) FROM {tabla}")), CultureInfo.InvariantCulture);
            recuentos.Add([tabla, Txt(n), Txt(total)]);
        }
        _r.Controles.Add(new SeccionControl("Filas en ProContaVF", ["Tabla", "De esta migración", "Total en la tabla"], recuentos));

        _r.Controles.Add(new SeccionControl("Tablas que no se migran (decisión del propietario)", ["Tabla", "Filas en el ERP"],
            o.NoMigradas.Select(x => (IReadOnlyList<string>)[x.Key, Txt(x.Value)]).ToList(),
            "Nóminas (F_NOM, alias y PlantillasApuntes) no se migran: quedan para el módulo de nóminas (decisión del propietario 2026-11-11)."));
    }
}
