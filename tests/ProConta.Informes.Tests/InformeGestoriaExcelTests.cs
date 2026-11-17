using ClosedXML.Excel;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Ratios;
using ProConta.Informes.Excel;

namespace ProConta.Informes.Tests;

/// <summary>Libro del informe para la gestoría con datos inventados: hojas, tablas, nombres, sello y fórmulas que dan lo mismo que el programa.</summary>
public sealed class InformeGestoriaExcelTests : IDisposable
{
    private readonly string _carpeta = Directory.CreateTempSubdirectory("ProContaVF-gestoria-").FullName;

    public void Dispose()
    {
        if (Environment.GetEnvironmentVariable("PROCONTA_CONSERVAR_TEMP") is { Length: > 0 }) return;
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private static LineaIva L(int trim, SentidoIva s, DeduccionIva d, OperacionIva op, bool isp, decimal tipo, decimal b, decimal c, string actividad, int asiento) =>
        new(new DateTime(2026, trim * 3 - 1, 10), trim, asiento, s, op, isp, tipo, actividad == "Viviendas" || actividad == "Locales" ? "INMUEBLES" : "EMPRESA",
            $"Centro {actividad}", d == DeduccionIva.SinCentro ? null : actividad, d, "Tercero sintético", "B00000000", $"F-{asiento}", b, c,
            s == SentidoIva.Soportado && (op != OperacionIva.Nacional || isp) ? c : 0m, "4720021", d == DeduccionIva.SinCentro ? TextosIva.AvisoSinCentro : null);

    private static readonly IReadOnlyList<LineaIva> Lineas =
    [
        L(1, SentidoIva.Repercutido, DeduccionIva.Deducible100, OperacionIva.Nacional, false, 21, 1000m, 210m, "Locales", 1),
        L(1, SentidoIva.Repercutido, DeduccionIva.NoDeducible, OperacionIva.Nacional, false, 0, 3000m, 0m, "Viviendas", 2),
        L(1, SentidoIva.Soportado, DeduccionIva.Deducible100, OperacionIva.Nacional, false, 21, 200m, 42m, "Locales", 3),
        L(1, SentidoIva.Soportado, DeduccionIva.Prorrata, OperacionIva.Nacional, false, 21, 333.33m, 70m, "General", 4),
        L(1, SentidoIva.Soportado, DeduccionIva.Prorrata, OperacionIva.Nacional, false, 0, 10m, 0m, "General", 5),
        L(1, SentidoIva.Soportado, DeduccionIva.Prorrata, OperacionIva.Nacional, true, 21, 100m, 21m, "General", 6),
        L(2, SentidoIva.Repercutido, DeduccionIva.Deducible100, OperacionIva.Nacional, false, 10, 500m, 50m, "Locales", 7),
        L(2, SentidoIva.Soportado, DeduccionIva.NoDeducible, OperacionIva.Intracomunitaria, true, 21, 300m, 63m, "Viviendas", 8),
        L(2, SentidoIva.Soportado, DeduccionIva.Prorrata, OperacionIva.Importacion, false, 21, 52m, 10.92m, "General", 9),
        L(3, SentidoIva.Soportado, DeduccionIva.SinCentro, OperacionIva.Nacional, false, 21, 119.72m, 25.14m, "", 10),
        L(3, SentidoIva.Soportado, DeduccionIva.Prorrata, OperacionIva.Nacional, false, 21, 100m, 21m, "General", 11),
    ];

    private static readonly ModeloBalance Situacion = new("SIT", "Situación sintética", TipoBalance.Situacion,
    [
        new("572", 1, 3, "ACTIVO", 0, "B. ACTIVO CORRIENTE", 201, "VI. Efectivo y otros activos líquidos equivalentes", 217, null, "ACT.B", "ACT.B.6.017"),
        new("430", 1, 3, "ACTIVO", 0, "B. ACTIVO CORRIENTE", 201, "II. Deudores comerciales y otras cuentas a cobrar.", 203, "1. Clientes", "ACT.B", "ACT.B.3.010"),
        new("100", -1, 3, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 301, "I. Capital", "PNP.A", "PNP.A.1.001"),
        new("6", -1, 1, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 308, "VII. Resultado del ejercicio", "PNP.A", "PNP.A.1.008"),
        new("7", -1, 1, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 308, "VII. Resultado del ejercicio", "PNP.A", "PNP.A.1.008"),
        new("555", -1, 3, "PATRIMONIO NETO Y PASIVO", 10000, "C. PASIVO CORRIENTE", 501, "II. Deudas a corto plazo.", 505, null, "PNP.C", "PNP.C.2.001"),
    ]);

    private static readonly ModeloBalance Pyg = new("PYG", "PyG sintética", TipoBalance.PerdidasYGanancias,
    [
        new("705", -1, 3, "CUENTA DE PÉRDIDAS Y GANANCIAS", 0, "A. RESULTADO DE EXPLOTACIÓN", 701, "1. Importe neto de la cifra de negocios.", 701, null),
        new("62", -1, 3, "CUENTA DE PÉRDIDAS Y GANANCIAS", 0, "A. RESULTADO DE EXPLOTACIÓN", 701, "7. Otros gastos de explotación.", 707, null),
    ]);

    private static IReadOnlyList<ApunteBalance> Apuntes =>
    [
        new(new DateTime(2026, 1, 2), 1, 1, 1, "5720001", 5000m, 0m, "Apertura", null), new(new DateTime(2026, 1, 2), 1, 1, 2, "1000000", 0m, 5000m, "Apertura", null),
        new(new DateTime(2026, 2, 3), 2, 0, 1, "4300001", 1000m, 0m, "Venta", null), new(new DateTime(2026, 2, 3), 2, 0, 2, "7050001", 0m, 1000m, "Venta", 1),
        new(new DateTime(2026, 2, 5), 3, 0, 1, "6290001", 300m, 0m, "Gasto", 2), new(new DateTime(2026, 2, 5), 3, 0, 2, "5720001", 0m, 300m, "Gasto", null),
        new(new DateTime(2026, 3, 9), 4, 0, 1, "5720001", 50m, 0m, "Pendiente", null), new(new DateTime(2026, 3, 9), 4, 0, 2, "5550001", 0m, 100m, "Pendiente", null),
        new(new DateTime(2026, 3, 9), 4, 0, 3, "5550002", 50m, 0m, "Pendiente", null),
    ];

    private static readonly Dictionary<int, CentroBalance> Centros = new() { [1] = new("INMUEBLES", "3/300", "Local sintético", "Alquiler Locales"), [2] = new("EMPRESA", "1/101", "Gastos generales", "General") };

    private static InformeGestoria Informe(decimal? provisional = 0.15m, decimal? definitiva = null)
    {
        var iva = CalculoIvaEjercicio.Calcular(Lineas, provisional, definitiva);
        var (r303, prorrata) = (iva.Modelo303, iva.Prorrata);
        var nombres = new Dictionary<string, string> { ["705"] = "PRESTACIONES", ["7050001"] = "Alquiler local sintético" };
        var pyg = Clasificador.Clasificar(Pyg, "Sociedad sintética", 2026, Apuntes, nombres, Centros);
        var sit = Clasificador.Clasificar(Situacion, "Sociedad sintética", 2026, Apuntes, nombres, Centros);
        var mov = Apuntes.Select(a => new MovimientoRatio(a.Fecha, a.Cuenta, a.TipoAsiento, a.Debe, a.Haber)).ToList();
        var ratios = CalculoRatios.Calcular(Situacion, mov, mov, new DateTime(2026, 3, 31), sinResultado: "Faltan los datos de 2025.");
        return new InformeGestoria("Sociedad sintética", 2026, 2, new DateTime(2026, 7, 10, 18, 30, 5), new DateTime(2026, 6, 30), provisional,
            Lineas, r303, prorrata, pyg, sit, ratios, null,
            [
                new("A-1", new DateTime(2026, 2, 3), 1, "B00000001", "Cliente sintético", "Alquiler", 1000m, 210m, 190m, 1020m, "INMUEBLES", "Local sintético", true),
                new("A-2", new DateTime(2026, 5, 3), 2, "B00000001", "Cliente sintético", "Alquiler", 500m, 50m, 0m, 550m, "INMUEBLES", "Local sintético", false),
            ],
            [
                new(new DateTime(2026, 2, 5), 1, 3, "B00000002", "Proveedor sintético", "F-3", "Nacional", false, 200m, 42m, 0m, "Gastos generales", "EMPRESA",
                    Path.Combine(Path.GetTempPath(), "no-existe.pdf"), false),
            ],
            [new(1, 210m, 210m, 133m, 133m), new(2, 50m, 49m, 73.92m, 73.92m)], 0, 3);
    }

    private string Exportar(InformeGestoria inf)
    {
        var ruta = Path.Combine(_carpeta, InformeGestoria.NombreFichero(inf.Ejercicio, inf.Trimestre, inf.Generado));
        ExportadorInformeGestoriaExcel.Exportar(inf, ruta);
        return ruta;
    }

    [Fact]
    public void ElLibroTieneLasHojasTablasNombresYSello()
    {
        var inf = Informe();
        var ruta = Exportar(inf);
        Assert.EndsWith("InformeGestoria_2026-2T_20260710-183005.xlsx", ruta);
        using var libro = new XLWorkbook(ruta);
        Assert.Equal(ExportadorInformeGestoriaExcel.Hojas, libro.Worksheets.Select(w => w.Name));
        Assert.Equal(ExportadorInformeGestoriaExcel.ColumnasIva, libro.Worksheet("Datos IVA").Table("tIVA").Fields.Select(f => f.Name));
        Assert.Equal(Lineas.Count, libro.Worksheet("Datos IVA").Table("tIVA").DataRange.RowCount());
        foreach (var (hoja, tabla) in new[] { ("Emitidas", "tEmitidas"), ("Recibidas", "tRecibidas"), ("Datos PyG", "tPyG"), ("Datos Situación", "tSit") })
            Assert.NotNull(libro.Worksheet(hoja).Table(tabla));
        foreach (var nombre in new[] { "ProrrataProv", "ProrrataReal", "IVA_Base", "IVA_Cuota", "IVA_Trim", "Emi_Base", "Rec_Base", "PyG_Importe", "Sit_Importe" })
            Assert.True(libro.DefinedNames.Contains(nombre), nombre);
        var resumen = libro.Worksheet("Resumen");
        var textos = resumen.CellsUsed().Select(c => c.HasFormula ? "" : c.GetString()).ToList();
        Assert.Contains("Sociedad sintética", textos);
        Assert.Contains("10/07/2026 18:30:05", textos);
        Assert.Contains("30/06/2026", textos);
        Assert.Contains(textos, t => t.StartsWith("2.º", StringComparison.Ordinal));
        // El índice enlaza con cada hoja.
        Assert.Equal(13, resumen.CellsUsed().Count(c => c.HasHyperlink));
        // Las cifras principales son fórmulas sobre las tablas.
        Assert.StartsWith("SUMIFS(IVA_Cuota,IVA_Trim,1,IVA_Sentido,\"Repercutido\",IVA_Tipo,0.21", libro.Worksheet("IVA 303").Cell(9, 4).FormulaA1);
        Assert.Equal("ROUNDUP(ROUND(C9*100,6),0)/100", libro.Worksheet("Prorrata").Cell(10, 3).FormulaA1);
        Assert.All(libro.Worksheet("Controles").Range("G6:G10").Cells(), c => Assert.True(c.HasFormula));
        // PyG por actividad: una columna por actividad con importes, y la tabla de datos con un apunte por fila.
        var pyg = libro.Worksheet("PyG");
        Assert.Equal(["Alquiler Locales", "General", "Total"], pyg.Row(5).CellsUsed().Skip(1).Select(c => c.GetString()));
        Assert.Equal(2, libro.Worksheet("Datos PyG").Table("tPyG").DataRange.RowCount());
        Assert.Equal(ExportadorInformeGestoriaExcel.ColumnasSituacion, libro.Worksheet("Datos Situación").Table("tSit").Fields.Select(f => f.Name));
        // Tablas dinámicas preconfiguradas como las del libro anterior.
        var tdPyg = libro.Worksheet("TDPyG").PivotTables.Single();
        Assert.Equal(["Apartado", "Título", "Cuenta", "Subcuenta"], tdPyg.RowLabels.Select(f => f.SourceName));
        Assert.Equal(["Actividad"], tdPyg.ColumnLabels.Select(f => f.SourceName));
        var tdSit = libro.Worksheet("TDSituación").PivotTables.Single();
        Assert.Equal(["Lado", "Apartado", "Título", "Cuenta", "Subcuenta"], tdSit.RowLabels.Select(f => f.SourceName));
        Assert.Equal(["Mes"], tdSit.ColumnLabels.Select(f => f.SourceName));
        Assert.Equal(["Actividad", "Departamento"], tdSit.ReportFilters.Select(f => f.SourceName));
    }

    [Fact]
    public void LasFormulasDanLoMismoQueElPrograma()
    {
        var inf = Informe();
        var ruta = Exportar(inf);
        using var libro = new XLWorkbook(ruta);
        var iva = libro.Worksheet("IVA 303");
        var filaResultado = iva.Column(2).CellsUsed().First(c => c.GetString().StartsWith("Resultado del trimestre", StringComparison.Ordinal)).Address.RowNumber;
        for (var q = 0; q <= 4; q++)
            Assert.Equal(inf.Iva303.Resultado(q), Valor(iva.Cell(filaResultado, ExportadorInformeGestoriaExcel.ColumnaCuota(q))));
        foreach (var linea in inf.Iva303.Devengado.Concat(inf.Iva303.Deducible).Concat(inf.Iva303.Informativo))
        {
            var fila = iva.Column(2).CellsUsed().First(c => c.GetString() == linea.Concepto).Address.RowNumber;
            for (var q = 1; q <= 4; q++)
            {
                if (linea.ConCuota) Assert.True(linea.En(q).Cuota == Valor(iva.Cell(fila, ExportadorInformeGestoriaExcel.ColumnaCuota(q))), $"{linea.Concepto} {q}T cuota");
                if (linea.ConBase) Assert.True(linea.En(q).Base == Valor(iva.Cell(fila, ExportadorInformeGestoriaExcel.ColumnaCuota(q) - 1)), $"{linea.Concepto} {q}T base");
            }
        }
        var prorrata = libro.Worksheet("Prorrata");
        Assert.Equal(inf.Prorrata.Real, Valor(prorrata.Cell(10, 3)));
        var filaReg = prorrata.Column(2).CellsUsed().First(c => c.GetString().StartsWith("Regularización estimada", StringComparison.Ordinal)).Address.RowNumber;
        Assert.Equal(inf.Prorrata.Regularizacion(0), Valor(prorrata.Cell(filaReg, 7)));

        var controles = ControlesGestoria.Calcular(inf);
        var hoja = libro.Worksheet("Controles");
        for (var i = 0; i < controles.Count; i++)
        {
            var c = controles[i];
            Assert.True(c.Valor == Valor(hoja.Cell(6 + i, 4)), $"Valor de «{c.Nombre}»");
            if (c.Referencia is decimal r) Assert.True(r == Valor(hoja.Cell(6 + i, 5)), $"Referencia de «{c.Nombre}»");
            Assert.True(c.Estado.ToString() == hoja.Cell(6 + i, 7).GetString(), $"Estado de «{c.Nombre}»: {c.Estado} frente a {hoja.Cell(6 + i, 7).GetString()}");
        }
        // Ninguna fórmula del libro da error.
        var errores = libro.Worksheets.SelectMany(w => w.CellsUsed(c => c.HasFormula)).Where(c => c.Value.IsError).Select(c => $"{c.Worksheet.Name}!{c.Address}: {c.FormulaA1}").ToList();
        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
        Assert.Equal("15% → 34%", libro.Worksheet("Resumen").Cell(6, 4).GetString());
    }

    [Fact]
    public void LosControlesMarcanLoQueHayQueRevisar()
    {
        var inf = Informe(provisional: null);
        var c = ControlesGestoria.Calcular(inf).ToDictionary(x => x.Nombre);
        Assert.Equal(EstadoControl.Revisar, c["Líneas de IVA sin centro de coste o con un centro sin actividad"].Estado);
        Assert.Contains("10", c["Líneas de IVA sin centro de coste o con un centro sin actividad"].Nota);
        Assert.Equal(EstadoControl.Revisar, c["Prorrata provisional sin informar"].Estado);
        Assert.Equal(EstadoControl.Revisar, c["Libro de IVA repercutido frente a las 477 · 2T"].Estado);
        Assert.Equal(EstadoControl.OK, c["Libro de IVA repercutido frente a las 477 · 1T"].Estado);
        Assert.Equal(EstadoControl.Aviso, c["Facturas emitidas sin contabilizar"].Estado);
        Assert.Contains("A-2", c["Facturas emitidas sin contabilizar"].Nota);
        Assert.Equal(EstadoControl.Revisar, c["Pasivo corriente con saldo deudor (subcuentas en negativo)"].Estado);
        Assert.Equal(-50m, c["Pasivo corriente con saldo deudor (subcuentas en negativo)"].Valor);
        Assert.Equal(EstadoControl.Revisar, c["Amortizaciones contabilizadas (referencia: activos amortizables)"].Estado);
        Assert.Equal(EstadoControl.OK, c["Balance cuadrado: activo frente a patrimonio neto y pasivo"].Estado);
        Assert.Equal(EstadoControl.OK, c["Resultado: pérdidas y ganancias frente al balance sin la 129"].Estado);
        Assert.Equal(EstadoControl.Revisar, c["Ingresos de «Viviendas» en el 2T frente al 1T"].Estado);
        Assert.Equal(EstadoControl.OK, c["Ingresos de «Locales» en el 2T frente al 1T"].Estado);
        // Toda nota de un control que no está bien dice qué hacer (un sitio del programa o una acción).
        Assert.All(c.Values.Where(x => x.Estado != EstadoControl.OK), x => Assert.Matches("›|Contabilíza|Corrige|corríg|Comprueba|compara|Compara|Escríbela|revísalos|mira", x.Nota));
    }

    [Fact]
    public void ElTrimestrePropuestoEsElUltimoTerminado()
    {
        Assert.Equal(3, InformeGestoria.TrimestrePropuesto(2026, new DateTime(2026, 11, 15)));
        Assert.Equal(1, InformeGestoria.TrimestrePropuesto(2026, new DateTime(2026, 2, 1)));
        Assert.Equal(4, InformeGestoria.TrimestrePropuesto(2025, new DateTime(2026, 1, 10)));
        Assert.Equal(new DateTime(2026, 9, 30), InformeGestoria.FinDe(2026, 3));
    }

    /// <summary>Valor calculado por ClosedXML, redondeado a 6 decimales para quitar el ruido del doble.</summary>
    private static decimal Valor(IXLCell c) => Math.Round((decimal)c.Value.GetNumber(), 6);

    /// <summary>Con la prorrata definitiva fijada (contabilidad/procesos/prorrata), el 303 del 4T lleva la regularización y la hoja Prorrata la calcula con ella.</summary>
    [Fact]
    public void ConLaDefinitivaFijadaEl4TLlevaLaRegularizacion()
    {
        var inf = Informe(0.15m, 0.30m);
        Assert.True(inf.Prorrata.DefinitivaFijada);
        var regularizacion = inf.Iva303.Linea(Calculo303.ClaveRectificacion).En(4).Cuota;
        Assert.Equal(inf.Prorrata.Regularizacion(0), regularizacion);
        Assert.NotEqual(0m, regularizacion);
        var ruta = Exportar(inf);
        using var libro = new XLWorkbook(ruta);
        var iva = libro.Worksheet("IVA 303");
        var fila = iva.Column(2).CellsUsed().First(c => c.GetString() == "Regularización de la prorrata definitiva").Address.RowNumber;
        Assert.Equal(regularizacion, Valor(iva.Cell(fila, ExportadorInformeGestoriaExcel.ColumnaCuota(4))));
        Assert.Equal(0m, Valor(iva.Cell(fila, ExportadorInformeGestoriaExcel.ColumnaCuota(1))));
        var filaResultado = iva.Column(2).CellsUsed().First(c => c.GetString().StartsWith("Resultado del trimestre", StringComparison.Ordinal)).Address.RowNumber;
        Assert.Equal(inf.Iva303.Resultado(4), Valor(iva.Cell(filaResultado, ExportadorInformeGestoriaExcel.ColumnaCuota(4))));
        var prorrata = libro.Worksheet("Prorrata");
        Assert.Equal(0.30m, Valor(prorrata.Cell(11, 3)));
        var filaReg = prorrata.Column(2).CellsUsed().First(c => c.GetString().StartsWith("Regularización a favor", StringComparison.Ordinal)).Address.RowNumber;
        Assert.Equal(regularizacion, Valor(prorrata.Cell(filaReg, 7)));
        var errores = libro.Worksheets.SelectMany(w => w.CellsUsed(c => c.HasFormula)).Where(c => c.Value.IsError).Select(c => $"{c.Worksheet.Name}!{c.Address}: {c.FormulaA1}").ToList();
        Assert.True(errores.Count == 0, string.Join(Environment.NewLine, errores));
    }
}
