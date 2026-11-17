using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Ratios;
using N = ProConta.Informes.Excel.NombresGestoria;

namespace ProConta.Informes.Excel;

/// <summary>
/// Libro Excel del informe para la gestoría (informes/informe-gestoria e informes/excel), con el aspecto de la maqueta validada
/// (InformeGestoria-2026-3T-maqueta.xlsx): Resumen, IVA 303, Prorrata, Controles, PyG, Situación, Ratios, Emitidas, Recibidas,
/// Datos IVA, Datos PyG y Datos Situación. Las hojas IVA 303, Prorrata y Controles (y PyG y Situación) llevan fórmulas vivas
/// sobre las tablas de datos, con un nombre definido por columna (IVA_Base, IVA_Trim…) en lugar de referencias estructuradas,
/// para que se lean solas y para que cualquier motor de cálculo las entienda; dan lo mismo que el programa. ClosedXML no crea
/// gráficos: el Resumen usa barras de datos. Excel recalcula todo al abrir el libro.
/// </summary>
public static class ExportadorInformeGestoriaExcel
{
    public const string FormatoImporte = "#,##0.00;[Red]-#,##0.00;\"–\"";
    public const string FormatoEuros = "#,##0.00 €;[Red]-#,##0.00 €";

    public const string HojaResumen = "Resumen", HojaIva = "IVA 303", HojaProrrata = "Prorrata", HojaControles = "Controles", HojaPyG = "PyG",
        HojaSituacion = "Situación", HojaTDPyG = "TDPyG", HojaTDSituacion = "TDSituación", HojaRatios = "Ratios", HojaEmitidas = "Emitidas", HojaRecibidas = "Recibidas", HojaDatosIva = "Datos IVA",
        HojaDatosPyG = "Datos PyG", HojaDatosSituacion = "Datos Situación";

    /// <summary>Hojas del libro, en orden.</summary>
    public static readonly IReadOnlyList<string> Hojas =
        [HojaResumen, HojaIva, HojaProrrata, HojaControles, HojaPyG, HojaSituacion, HojaTDPyG, HojaTDSituacion, HojaRatios, HojaEmitidas, HojaRecibidas, HojaDatosIva, HojaDatosPyG, HojaDatosSituacion];

    /// <summary>Columnas de la tabla «tIVA» de la hoja «Datos IVA».</summary>
    public static readonly IReadOnlyList<string> ColumnasIva =
        ["Fecha", "Trim", "Asiento", "Sentido", "Operación", "ISP", "Tipo", "Centro", "Departamento", "Actividad", "Deducción", "Tercero", "NIF", "Factura",
         "Base", "Cuota", "Autorrepercutida", "Cuenta IVA", "Aviso"];

    private static readonly XLColor Azul = XLColor.FromHtml("#1F3A5F"), AzulClaro = XLColor.FromHtml("#C9D6E8"), Verde = XLColor.FromHtml("#2A9D8F"),
        Gris = XLColor.FromHtml("#6B7785"), Fondo = XLColor.FromHtml("#F3F6FA"), Total = XLColor.FromHtml("#E8F4F2"), Resultado = XLColor.FromHtml("#DCEBFA"),
        Enlace = XLColor.FromHtml("#1D4ED8"), Rojo = XLColor.FromHtml("#E76F51"), Ok = XLColor.FromHtml("#2A9D8F"), Ambar = XLColor.FromHtml("#E9A23B");

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string Inv(decimal v) => v.ToString(CultureInfo.InvariantCulture);
    private static string Ref(string hoja, int fila, int columna) => $"'{hoja}'!{XLHelper.GetColumnLetterFromNumber(columna)}{fila}";

    /// <summary>Posiciones de las cifras que otras hojas referencian.</summary>
    private sealed class Mapa
    {
        public Dictionary<string, int> FilaIva { get; } = [];
        public int FilaTotalDevengado, FilaTotalDeducible, FilaResultado, FilaRegularizacion, FilaResultadoPyG, ColumnaTotalPyG, PrimeraControl, UltimaControl;
    }

    /// <summary>Columna de la cuota de un trimestre (1 a 4) o del año (0) en la hoja «IVA 303»: D, F, H, J y L.</summary>
    public static int ColumnaCuota(int trimestre) => trimestre == 0 ? 12 : 2 + 2 * trimestre;

    public static void Exportar(InformeGestoria inf, string ruta)
    {
        ArgumentNullException.ThrowIfNull(inf);
        using var libro = new XLWorkbook();
        foreach (var h in Hojas) libro.Worksheets.Add(h);
        var mapa = new Mapa();
        DatosIva(libro, inf);
        DatosPyG(libro, inf.PyG);
        DatosSituacion(libro, inf.Situacion);
        Emitidas(libro, inf);
        Recibidas(libro, inf);
        Iva303(libro, inf, mapa);
        Prorrata(libro, inf, mapa);
        Controles(libro, inf, mapa);
        PyG(libro, inf, mapa);
        Situacion(libro, inf);
        TablasDinamicas(libro, inf);
        Ratios(libro, inf);
        Resumen(libro, inf, mapa);
        foreach (var ws in libro.Worksheets) ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        libro.Worksheet(HojaResumen).SetTabActive();
        libro.CalculateMode = XLCalculateMode.Auto;
        libro.SaveAs(ruta);
    }

    // ---------- estilo ----------

    private static void Cabecera(IXLWorksheet ws, string titulo, string subtitulo, int ultimaColumna)
    {
        ws.ShowGridLines = false;
        ws.Column(1).Width = 2;
        var t = ws.Range(2, 2, 2, ultimaColumna);
        t.Style.Fill.BackgroundColor = Azul;
        ws.Cell(2, 2).Value = titulo;
        ws.Cell(2, 2).Style.Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(XLColor.White);
        ws.Row(2).Height = 24;
        var s = ws.Range(3, 2, 3, ultimaColumna);
        s.Style.Fill.BackgroundColor = Azul;
        ws.Cell(3, 2).Value = subtitulo;
        ws.Cell(3, 2).Style.Font.SetFontColor(AzulClaro);
    }

    private static void Seccion(IXLWorksheet ws, int fila, int desde, int hasta, string texto, XLColor? color = null)
    {
        ws.Range(fila, desde, fila, hasta).Style.Fill.BackgroundColor = color ?? Verde;
        ws.Cell(fila, desde).Value = texto;
        ws.Cell(fila, desde).Style.Font.SetBold().Font.SetFontColor(XLColor.White);
    }

    private static void Nota(IXLCell c, string texto)
    {
        c.Value = texto;
        c.Style.Font.SetFontColor(Gris);
    }

    private static void Importe(IXLCell c) => c.Style.NumberFormat.Format = FormatoImporte;

    private static IXLCell Formula(IXLWorksheet ws, int fila, int col, string formula, bool importe = true)
    {
        var c = ws.Cell(fila, col);
        c.FormulaA1 = formula;
        if (importe) Importe(c);
        return c;
    }

    /// <summary>Nombre definido para una columna de una tabla de datos (filas 2 a la última, al menos una).</summary>
    private static void Nombre(XLWorkbook libro, IXLWorksheet ws, string nombre, int columna, int ultimaFila) =>
        libro.DefinedNames.Add(nombre, ws.Range(2, columna, Math.Max(2, ultimaFila), columna));

    private static IXLTable Tabla(IXLWorksheet ws, string nombre, IReadOnlyList<string> columnas, int ultimaFila)
    {
        for (var c = 0; c < columnas.Count; c++) ws.Cell(1, c + 1).Value = columnas[c];
        var tabla = ws.Range(1, 1, Math.Max(2, ultimaFila), columnas.Count).CreateTable(nombre);
        tabla.Theme = XLTableTheme.TableStyleLight9;
        ws.SheetView.FreezeRows(1);
        return tabla;
    }

    // ---------- tablas de datos ----------

    private static void DatosIva(XLWorkbook libro, InformeGestoria inf)
    {
        var ws = libro.Worksheet(HojaDatosIva);
        var f = 1;
        foreach (var l in inf.LineasIva)
        {
            f++;
            var r = ws.Row(f);
            r.Cell(1).Value = l.Fecha;
            r.Cell(2).Value = l.Trimestre;
            r.Cell(3).Value = l.IdAsiento;
            r.Cell(4).Value = l.Sentido.Texto();
            r.Cell(5).Value = l.Operacion.Texto();
            r.Cell(6).Value = l.Isp ? TextosIva.Si : TextosIva.No;
            r.Cell(7).Value = l.TipoIva / 100m;
            r.Cell(8).Value = l.Centro ?? N.SinCentro;
            r.Cell(9).Value = l.Departamento ?? "";
            r.Cell(10).Value = l.Actividad ?? N.SinActividad;
            r.Cell(11).Value = l.Deduccion.Texto();
            r.Cell(12).Value = l.Tercero;
            r.Cell(13).Value = l.Nif ?? "";
            r.Cell(14).Value = l.Factura ?? "";
            r.Cell(15).Value = l.Base;
            r.Cell(16).Value = l.Cuota;
            r.Cell(17).Value = l.CuotaAutorrepercutida;
            r.Cell(18).Value = l.CuentaIva ?? "";
            r.Cell(19).Value = l.Aviso ?? "";
        }
        var tabla = Tabla(ws, "tIVA", ColumnasIva, f);
        ws.Column(1).Style.NumberFormat.Format = "dd/mm/yyyy";
        ws.Column(7).Style.NumberFormat.Format = "0%";
        ws.Columns(15, 17).Style.NumberFormat.Format = FormatoImporte;
        var ultima = Math.Max(2, f);
        foreach (var (nombre, col) in new[] { (N.IvaTrim, 2), (N.IvaAsiento, 3), (N.IvaSentido, 4), (N.IvaOperacion, 5), (N.IvaIsp, 6), (N.IvaTipo, 7), (N.IvaCentro, 8),
                     (N.IvaActividad, 10), (N.IvaDeduccion, 11), (N.IvaBase, 15), (N.IvaCuota, 16), (N.IvaAviso, 19) })
            Nombre(libro, ws, nombre, col, ultima);
        ws.Range(2, 19, ultima, 19).AddConditionalFormat().WhenNotBlank().Fill.SetBackgroundColor(XLColor.FromHtml("#FDE7E1"));
        ws.Range(2, 11, ultima, 11).AddConditionalFormat().WhenEquals($"\"{TextosIva.SinCentro}\"").Font.SetFontColor(Rojo);
        Anchos(ws, [11, 6, 9, 12, 15, 6, 7, 18, 30, 20, 16, 34, 13, 18, 12, 11, 12, 11, 26]);
        _ = tabla;
    }

    private static void Anchos(IXLWorksheet ws, IReadOnlyList<double> anchos)
    {
        for (var i = 0; i < anchos.Count; i++) ws.Column(i + 1).Width = anchos[i];
    }

    private static string Centro(string? c) => string.IsNullOrEmpty(c) ? N.SinCentro : c;

    /// <summary>Columnas de la tabla «tPyG» (un apunte por fila, como los libros de balances).</summary>
    public static readonly IReadOnlyList<string> ColumnasPyG =
        ["Fecha", "Mes", "Apartado", "Título", "Subtítulo", "Cuenta", "Subcuenta", "Importe", "Asiento", "Orden", "Concepto", "Actividad", "Centro", "Departamento"];

    /// <summary>Columnas de la tabla «tSit»: las de «tPyG» con el lado del balance (su bloque) delante.</summary>
    public static readonly IReadOnlyList<string> ColumnasSituacion = ["Lado", .. ColumnasPyG];

    private static string Actividad(string? a) => string.IsNullOrEmpty(a) ? N.SinActividad : a;

    /// <summary>Un apunte por fila (los de importe 0 no), desde la columna <paramref name="desde"/> (2 si delante va el lado); devuelve la última fila.</summary>
    private static int Apuntes(IXLWorksheet ws, ResultadoBalance balance, int desde)
    {
        var f = 1;
        foreach (var x in balance.Filas)
        {
            if (x.Importe == 0m) continue;
            f++;
            var c = desde;
            if (desde == 2) ws.Cell(f, 1).Value = x.Bloque;
            ws.Cell(f, c++).Value = x.Fecha;
            ws.Cell(f, c++).Value = x.Fecha.Month;
            ws.Cell(f, c++).Value = x.Apartado;
            ws.Cell(f, c++).Value = x.Titulo;
            ws.Cell(f, c++).Value = x.Subtitulo ?? "";
            ws.Cell(f, c++).Value = x.Cuenta;
            ws.Cell(f, c++).Value = x.Subcuenta;
            ws.Cell(f, c++).Value = x.Importe;
            ws.Cell(f, c++).Value = x.IdAsiento;
            ws.Cell(f, c++).Value = x.Orden;
            ws.Cell(f, c++).Value = x.Concepto;
            ws.Cell(f, c++).Value = Actividad(x.Actividad);
            ws.Cell(f, c++).Value = Centro(x.CentroCoste);
            ws.Cell(f, c).Value = x.NombreDepartamento ?? "";
        }
        ws.Column(desde).Style.NumberFormat.Format = "dd/mm/yyyy";
        ws.Column(desde + 7).Style.NumberFormat.Format = FormatoImporte;
        return f;
    }

    private static void DatosPyG(XLWorkbook libro, ResultadoBalance pyg)
    {
        var ws = libro.Worksheet(HojaDatosPyG);
        var f = Apuntes(ws, pyg, 1);
        Tabla(ws, "tPyG", ColumnasPyG, f);
        var ultima = Math.Max(2, f);
        foreach (var (nombre, col) in new[] { (N.PygApartado, 3), (N.PygTitulo, 4), (N.PygCuenta, 6), (N.PygImporte, 8), (N.PygActividad, 12), (N.PygCentro, 13) })
            Nombre(libro, ws, nombre, col, ultima);
        Anchos(ws, [11, 6, 30, 44, 24, 36, 44, 13, 9, 7, 44, 20, 18, 30]);
    }

    private static void DatosSituacion(XLWorkbook libro, ResultadoBalance sit)
    {
        var ws = libro.Worksheet(HojaDatosSituacion);
        var f = Apuntes(ws, sit, 2);
        Tabla(ws, "tSit", ColumnasSituacion, f);
        var ultima = Math.Max(2, f);
        foreach (var (nombre, col) in new[] { (N.SitLado, 1), (N.SitApartado, 4), (N.SitTitulo, 5), (N.SitSubtitulo, 6), (N.SitCuenta, 7), (N.SitImporte, 9) })
            Nombre(libro, ws, nombre, col, ultima);
        Anchos(ws, [24, 11, 6, 30, 44, 30, 36, 44, 13, 9, 7, 44, 20, 18, 30]);
    }

    /// <summary>
    /// Hojas TDPyG y TDSituación: tablas dinámicas preconfiguradas como las de las hojas PyG y Situacion del libro anterior
    /// (AsientosIVA2026-3Tv1), con lo que ya hace ExportadorBalanceExcel. PyG: Apartado › Título › Cuenta › Subcuenta en filas y las
    /// actividades en columnas (en el libro anterior, «CentroDeCoste», que era la actividad). Situación: Lado › Apartado › Título ›
    /// Cuenta › Subcuenta en filas, los meses en columnas y la actividad y el departamento como filtros. Subtotales arriba; Excel
    /// las calcula al abrir.
    /// </summary>
    private static void TablasDinamicas(XLWorkbook libro, InformeGestoria inf)
    {
        void Hoja(string hoja, string tabla, string titulo, string valor, Action<IXLPivotTable> configurar)
        {
            var ws = libro.Worksheet(hoja);
            ws.Cell(1, 1).Value = titulo;
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Azul);
            Nota(ws.Cell(2, 1), "Tabla dinámica sobre la hoja de datos: se puede filtrar, mover campos o pulsar dos veces en un importe para ver sus apuntes.");
            var pt = ws.PivotTables.Add("TablaDinamica" + tabla, ws.Cell(5, 1), libro.Table(tabla));
            configurar(pt);
            pt.Values.Add("Importe", valor).SetSummaryFormula(XLPivotSummary.Sum).NumberFormat.Format = FormatoImporte;
            pt.Layout = XLPivotLayout.Compact;
            pt.SetSubtotals(XLPivotSubtotals.AtTop);
            pt.SetShowGrandTotalsRows(true).SetShowGrandTotalsColumns(true);
            pt.SetRowHeaderCaption("Etiquetas de fila");
            ws.Column(1).Width = 70;
            ws.Columns(2, 16).Width = 15;
        }
        Hoja(HojaTDPyG, "tPyG", $"Pérdidas y ganancias por actividad · {inf.RazonSocial} · {inf.Ejercicio} hasta el {inf.FinTrimestre:dd/MM/yyyy}", "Suma de Importe", pt =>
        {
            foreach (var campo in new[] { "Apartado", "Título", "Cuenta" }) pt.RowLabels.Add(campo).AddSubtotal(XLSubtotalFunction.Automatic);
            pt.RowLabels.Add("Subcuenta");
            pt.ColumnLabels.Add("Actividad");
            pt.SetColumnHeaderCaption("Actividad");
        });
        Hoja(HojaTDSituacion, "tSit", $"Balance de situación por meses · {inf.RazonSocial} · {inf.Ejercicio} hasta el {inf.FinTrimestre:dd/MM/yyyy}", "Total", pt =>
        {
            pt.ReportFilters.Add("Actividad");
            pt.ReportFilters.Add("Departamento");
            foreach (var campo in new[] { "Lado", "Apartado", "Título", "Cuenta" }) pt.RowLabels.Add(campo).AddSubtotal(XLSubtotalFunction.Automatic);
            pt.RowLabels.Add("Subcuenta");
            pt.ColumnLabels.Add("Mes").SetSort(XLPivotSortType.Ascending);
            pt.SetColumnHeaderCaption("Mes");
        });
    }

    private static void Emitidas(XLWorkbook libro, InformeGestoria inf)
    {
        var ws = libro.Worksheet(HojaEmitidas);
        string[] cols = ["Nº", "Fecha", "Trim", "NIF", "Cliente", "Descripción", "Base", "IVA", "Retención", "Total", "Centro", "Departamento", "Contabilizada"];
        var f = 1;
        foreach (var e in inf.Emitidas)
        {
            f++;
            ws.Cell(f, 1).Value = e.Numero;
            ws.Cell(f, 2).Value = e.Fecha;
            ws.Cell(f, 3).Value = e.Trimestre;
            ws.Cell(f, 4).Value = e.Nif ?? "";
            ws.Cell(f, 5).Value = e.Cliente;
            ws.Cell(f, 6).Value = e.Descripcion ?? "";
            ws.Cell(f, 7).Value = e.Base;
            ws.Cell(f, 8).Value = e.Iva;
            ws.Cell(f, 9).Value = e.Retencion;
            ws.Cell(f, 10).Value = e.Total;
            ws.Cell(f, 11).Value = e.Centro ?? N.SinCentro;
            ws.Cell(f, 12).Value = e.Departamento ?? "";
            ws.Cell(f, 13).Value = e.Contabilizada ? TextosIva.Si : TextosIva.No;
        }
        Tabla(ws, "tEmitidas", cols, f).ShowTotalsRow = false;
        ws.Column(2).Style.NumberFormat.Format = "dd/mm/yyyy";
        ws.Columns(7, 10).Style.NumberFormat.Format = FormatoImporte;
        var ultima = Math.Max(2, f);
        Nombre(libro, ws, N.EmiTrim, 3, ultima);
        Nombre(libro, ws, N.EmiBase, 7, ultima);
        Nombre(libro, ws, N.EmiContabilizada, 13, ultima);
        ws.Range(2, 13, ultima, 13).AddConditionalFormat().WhenEquals($"\"{TextosIva.No}\"").Font.SetFontColor(Rojo);
        Anchos(ws, [10, 11, 6, 13, 34, 60, 13, 11, 12, 13, 20, 30, 13]);
    }

    private static void Recibidas(XLWorkbook libro, InformeGestoria inf)
    {
        var ws = libro.Worksheet(HojaRecibidas);
        string[] cols = ["Fecha", "Trim", "Asiento", "NIF", "Proveedor", "Factura", "Operación", "ISP", "Base", "IVA", "Retención", "Centro", "Área", "Documento"];
        var f = 1;
        foreach (var r in inf.Recibidas)
        {
            f++;
            ws.Cell(f, 1).Value = r.Fecha;
            ws.Cell(f, 2).Value = r.Trimestre;
            if (r.Asiento is int a) ws.Cell(f, 3).Value = a;
            ws.Cell(f, 4).Value = r.Nif ?? "";
            ws.Cell(f, 5).Value = r.Proveedor;
            ws.Cell(f, 6).Value = r.Numero;
            ws.Cell(f, 7).Value = r.Operacion;
            ws.Cell(f, 8).Value = r.Isp ? TextosIva.Si : TextosIva.No;
            ws.Cell(f, 9).Value = r.Base;
            ws.Cell(f, 10).Value = r.Iva;
            ws.Cell(f, 11).Value = r.Retencion;
            ws.Cell(f, 12).Value = r.Centro ?? "";
            ws.Cell(f, 13).Value = r.Area ?? "";
            var doc = ws.Cell(f, 14);
            doc.Value = r.RutaPdf ?? "";
            if (r.PdfExiste && r.RutaPdf is { } ruta && Uri.TryCreate(ruta, UriKind.Absolute, out var uri))
            {
                doc.SetHyperlink(new XLHyperlink(uri));
                doc.Style.Font.SetFontColor(Enlace).Font.SetUnderline();
            }
            else if (!string.IsNullOrEmpty(r.RutaPdf)) doc.Style.Font.SetFontColor(Gris);
        }
        Tabla(ws, "tRecibidas", cols, f);
        ws.Column(1).Style.NumberFormat.Format = "dd/mm/yyyy";
        ws.Columns(9, 11).Style.NumberFormat.Format = FormatoImporte;
        var ultima = Math.Max(2, f);
        Nombre(libro, ws, N.RecTrim, 2, ultima);
        Nombre(libro, ws, N.RecBase, 9, ultima);
        Anchos(ws, [11, 6, 9, 13, 30, 20, 15, 6, 12, 11, 11, 28, 18, 60]);
    }

    // ---------- IVA 303 ----------

    private static string Cond(params (string Nombre, string Criterio)[] c) => string.Concat(c.Select(x => $",{x.Nombre},{x.Criterio}"));
    private static string Txt(string s) => $"\"{s}\"";

    /// <summary>Criterios de cada línea del modelo (los mismos que <see cref="Calculo303"/>): lista de sumandos, cada uno con sus condiciones.</summary>
    private static IReadOnlyList<(string Nombre, string Criterio)[]> Criterios(string clave)
    {
        var sop = (N.IvaSentido, Txt(TextosIva.Soportado));
        var rep = (N.IvaSentido, Txt(TextosIva.Repercutido));
        var intra = new[] { sop, (N.IvaOperacion, Txt(TextosIva.Intracomunitaria)) };
        var isp = new[] { sop, (N.IvaIsp, Txt(TextosIva.Si)), (N.IvaOperacion, Txt("<>" + TextosIva.Intracomunitaria)) };
        var imp = new[] { sop, (N.IvaOperacion, Txt(TextosIva.Importacion)), (N.IvaIsp, Txt(TextosIva.No)) };
        (string, string)[] Con(IEnumerable<(string, string)> a, string deduccion) => [.. a, (N.IvaDeduccion, Txt(deduccion))];
        var interior = new[] { sop, (N.IvaOperacion, Txt(TextosIva.Nacional)), (N.IvaIsp, Txt(TextosIva.No)) };
        return clave switch
        {
            Calculo303.ClaveIntracomunitarias => [intra],
            Calculo303.ClaveIsp => [isp],
            Calculo303.ClaveImportaciones => [imp],
            Calculo303.ClaveInteriores100 => [Con(interior, TextosIva.Deducible100)],
            Calculo303.ClaveInterioresProrrata => [Con(interior, TextosIva.Prorrata)],
            Calculo303.ClaveAutorrepercutidas100 => [Con(intra, TextosIva.Deducible100), Con(isp, TextosIva.Deducible100), Con(imp, TextosIva.Deducible100)],
            Calculo303.ClaveAutorrepercutidasProrrata => [Con(intra, TextosIva.Prorrata), Con(isp, TextosIva.Prorrata), Con(imp, TextosIva.Prorrata)],
            Calculo303.ClaveNoDeducible => [[sop, (N.IvaDeduccion, Txt(TextosIva.NoDeducible))]],
            Calculo303.ClaveSujetaProrrata => [[sop, (N.IvaDeduccion, Txt(TextosIva.Prorrata))]],
            Calculo303.ClaveSinClasificar => [[sop, (N.IvaDeduccion, Txt(TextosIva.SinCentro))]],
            Calculo303.ClaveExentas => [[rep, (N.IvaTipo, "0")]],
            _ when clave.StartsWith("dev.rep.", StringComparison.Ordinal) =>
                [[rep, (N.IvaTipo, Inv(decimal.Parse(clave["dev.rep.".Length..], CultureInfo.InvariantCulture) / 100m))]],
            _ => [],
        };
    }

    private static bool EsProrrata(string clave) => clave is Calculo303.ClaveInterioresProrrata or Calculo303.ClaveAutorrepercutidasProrrata;
    private static bool BaseConTipo(string clave) => clave is Calculo303.ClaveInteriores100 or Calculo303.ClaveInterioresProrrata;

    private static string FormulaLinea(string clave, int trimestre, bool cuota)
    {
        var sumandos = Criterios(clave);
        if (sumandos.Count == 0) return "0";
        var columna = cuota ? N.IvaCuota : N.IvaBase;
        var extra = !cuota && BaseConTipo(clave) ? Cond((N.IvaTipo, Txt(">0"))) : "";
        var suma = string.Join("+", sumandos.Select(s => $"SUMIFS({columna},{N.IvaTrim},{trimestre}{Cond(s)}{extra})"));
        if (cuota && EsProrrata(clave)) return $"ROUND(({suma})*{N.ProrrataProv},2)";
        return suma;
    }

    private static void Iva303(XLWorkbook libro, InformeGestoria inf, Mapa mapa)
    {
        var ws = libro.Worksheet(HojaIva);
        Cabecera(ws, "Liquidación de IVA por trimestres (modelo 303)", $"{inf.RazonSocial} · ejercicio {inf.Ejercicio} · calculada desde las líneas de IVA de la contabilidad (hoja «Datos IVA»)", 12);
        Anchos(ws, [2, 52, 13, 13, 13, 13, 13, 13, 13, 13, 13, 13, 3, 70]);
        Nota(ws.Cell(5, 2), "Prorrata provisional aplicada");
        ws.Cell(5, 3).FormulaA1 = N.ProrrataProv;
        ws.Cell(5, 3).Style.NumberFormat.Format = "0%";
        ws.Cell(5, 3).Style.Font.SetBold();
        for (var q = 1; q <= 5; q++)
        {
            var col = 1 + 2 * q;
            ws.Range(6, col, 6, col + 1).Merge().Value = q == 5 ? "Año" : q > inf.Trimestre ? $"{q}T (parcial)" : $"{q}T";
            ws.Range(6, col, 6, col + 1).Style.Fill.SetBackgroundColor(Azul).Font.SetBold().Font.SetFontColor(XLColor.White).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(7, col).Value = "Base";
            ws.Cell(7, col + 1).Value = "Cuota";
            ws.Range(7, col, 7, col + 1).Style.Font.SetBold().Font.SetFontColor(Azul).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        }
        var f = 8;
        void Lineas(IEnumerable<Linea303> lineas)
        {
            foreach (var l in lineas)
            {
                f++;
                mapa.FilaIva[l.Clave] = f;
                ws.Cell(f, 2).Value = l.Concepto;
                for (var q = 1; q <= 4; q++)
                {
                    var cb = 1 + 2 * q;
                    // La regularización de la prorrata (solo 4T, con la definitiva fijada) es un dato del programa, no una fórmula.
                    if (l.Clave == Calculo303.ClaveRectificacion) { ws.Cell(f, cb + 1).Value = l.En(q).Cuota; Importe(ws.Cell(f, cb + 1)); continue; }
                    if (l.ConBase) Formula(ws, f, cb, FormulaLinea(l.Clave, q, cuota: false));
                    if (l.ConCuota) Formula(ws, f, cb + 1, FormulaLinea(l.Clave, q, cuota: true));
                }
                if (l.ConBase) Formula(ws, f, 11, $"C{f}+E{f}+G{f}+I{f}").Style.Fill.SetBackgroundColor(Fondo);
                if (l.ConCuota) Formula(ws, f, 12, $"D{f}+F{f}+H{f}+J{f}").Style.Fill.SetBackgroundColor(Fondo);
                if (l.Nota.Length > 0) Nota(ws.Cell(f, 14), l.Nota);
            }
        }
        int Totales(int desde, string texto)
        {
            f++;
            var fila = f;
            ws.Cell(fila, 2).Value = texto;
            foreach (var col in new[] { 4, 6, 8, 10, 12 })
                Formula(ws, fila, col, $"SUM({XLHelper.GetColumnLetterFromNumber(col)}{desde}:{XLHelper.GetColumnLetterFromNumber(col)}{fila - 1})");
            ws.Range(fila, 2, fila, 12).Style.Fill.SetBackgroundColor(Total).Font.SetBold();
            return fila;
        }
        Seccion(ws, f, 2, 12, "IVA devengado");
        var inicio = f + 1;
        Lineas(inf.Iva303.Devengado);
        mapa.FilaTotalDevengado = Totales(inicio, "Total IVA devengado");
        f += 2;
        Seccion(ws, f, 2, 12, "IVA deducible");
        inicio = f + 1;
        Lineas(inf.Iva303.Deducible);
        mapa.FilaTotalDeducible = Totales(inicio, "Total IVA deducible");
        f += 2;
        mapa.FilaResultado = f;
        ws.Cell(f, 2).Value = "Resultado del trimestre (a ingresar + / a compensar −)";
        foreach (var col in new[] { 4, 6, 8, 10, 12 })
        {
            var l = XLHelper.GetColumnLetterFromNumber(col);
            Formula(ws, f, col, $"{l}{mapa.FilaTotalDevengado}-{l}{mapa.FilaTotalDeducible}");
        }
        ws.Range(f, 2, f, 12).Style.Fill.SetBackgroundColor(Resultado).Font.SetBold();
        var conDefinitiva = inf.Prorrata.Definitiva is decimal def
            ? $"el 4T incluye la regularización anual de la prorrata con la definitiva del {def * 100m:0.##} % (hoja «Prorrata»)."
            : null;
        Nota(ws.Cell(f, 14), inf.Trimestre < 4
            ? "Los trimestres posteriores al del informe están incompletos; " + (conDefinitiva ?? "el 4T no incluye todavía la regularización anual de la prorrata (hoja «Prorrata»).")
            : conDefinitiva is not null ? char.ToUpperInvariant(conDefinitiva[0]) + conDefinitiva[1..]
            : "El 4T no incluye la regularización anual de la prorrata porque el ejercicio no tiene la definitiva: está estimada en la hoja «Prorrata» (Contabilidad › Prorrata de IVA).");
        f += 2;
        Seccion(ws, f, 2, 12, "Informativo (no entra en el resultado)", Gris);
        Lineas(inf.Iva303.Informativo);
        ws.SheetView.FreezeRows(7);
    }

    // ---------- Prorrata ----------

    private static string EtiquetaDeduccion(DeduccionIva d) => d switch
    {
        DeduccionIva.Deducible100 => "con derecho",
        DeduccionIva.Prorrata => "en prorrata",
        DeduccionIva.NoDeducible => "sin derecho (exento)",
        _ => "sin centro",
    };

    private static void Prorrata(XLWorkbook libro, InformeGestoria inf, Mapa mapa)
    {
        var ws = libro.Worksheet(HojaProrrata);
        var p = inf.Prorrata;
        Cabecera(ws, "Prorrata de IVA", p.DefinitivaFijada
            ? "Porcentaje provisional, real acumulado del año, definitiva fijada y regularización del 4T"
            : "Porcentaje provisional, porcentaje real acumulado del año y regularización estimada (sin asiento)", 7);
        Anchos(ws, [2, 50, 16, 16, 16, 16, 16, 3, 70]);
        Seccion(ws, 5, 2, 7, "1 · Porcentajes");
        ws.Cell(6, 2).Value = $"Prorrata provisional (definitiva de {inf.Ejercicio - 1})";
        ws.Cell(6, 3).Value = p.Provisional;
        ws.Cell(6, 3).Style.NumberFormat.Format = "0.##%";
        ws.Cell(6, 3).Style.Font.SetBold().Font.SetFontColor(Enlace).Fill.SetBackgroundColor(Total);
        libro.DefinedNames.Add(N.ProrrataProv, ws.Cell(6, 3).AsRange());
        Nota(ws.Cell(6, 9), p.ProvisionalInformada
            ? "Dato de entrada (azul), de Configuración › Parámetros y tablas › Ejercicios («Prorrata del año anterior (%)»). Si lo cambias aquí, se recalcula todo el libro."
            : "Sin informar en el ejercicio: se usa el 100 %. Escríbela en Configuración › Parámetros y tablas › Ejercicios y vuelve a generar el informe.");
        if (!p.ProvisionalInformada) ws.Cell(6, 9).Style.Font.SetFontColor(Rojo);
        var rep = Txt(TextosIva.Repercutido);
        ws.Cell(7, 2).Value = "Volumen de operaciones con derecho a deducción";
        Formula(ws, 7, 3, $"SUMIFS({N.IvaBase},{N.IvaSentido},{rep},{N.IvaDeduccion},{Txt(TextosIva.Deducible100)},{N.IvaTrim},\">0\")");
        Nota(ws.Cell(7, 9), "Bases repercutidas de actividades 100 % deducibles.");
        ws.Cell(8, 2).Value = "Volumen total de operaciones";
        Formula(ws, 8, 3, $"SUMIFS({N.IvaBase},{N.IvaSentido},{rep},{N.IvaTrim},\">0\")");
        Nota(ws.Cell(8, 9), "Todas las bases repercutidas, exentas incluidas.");
        ws.Cell(9, 2).Value = "Cociente sin redondear";
        Formula(ws, 9, 3, "IF(C8=0,0,C7/C8)", importe: false).Style.NumberFormat.Format = "0.00%";
        ws.Cell(10, 2).Value = "Prorrata real acumulada (redondeo al entero superior, art. 104 LIVA)";
        Formula(ws, 10, 3, "ROUNDUP(ROUND(C9*100,6),0)/100", importe: false).Style.NumberFormat.Format = "0%";
        ws.Range(10, 2, 10, 3).Style.Font.SetBold();
        ws.Cell(10, 3).Style.Fill.SetBackgroundColor(Total);
        libro.DefinedNames.Add(N.ProrrataReal, ws.Cell(10, 3).AsRange());
        Nota(ws.Cell(10, 9), "El cociente se redondea antes a 6 decimales para que el ruido decimal no suba un punto (19,00 % → 19 %; 19,001 % → 20 %).");
        ws.Cell(11, 2).Value = "Prorrata definitiva del año (fijada)";
        if (p.Definitiva is decimal definitiva) ws.Cell(11, 3).Value = definitiva; else ws.Cell(11, 3).Value = "sin fijar";
        ws.Cell(11, 3).Style.NumberFormat.Format = "0.##%";
        ws.Cell(11, 3).Style.Font.SetBold().Font.SetFontColor(Enlace).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        Nota(ws.Cell(11, 9), p.DefinitivaFijada
            ? "La que marca la gestoría, fijada en Contabilidad › Prorrata de IVA: con ella se calcula la regularización, que va en el 303 del 4T."
            : "Sin fijar: la regularización se estima con la real acumulada. Se fija en Contabilidad › Prorrata de IVA al cerrar el año.");

        Seccion(ws, 12, 2, 7, "2 · Volumen de operaciones por actividad (bases repercutidas)");
        string[] cab = ["Actividad", "1T", "2T", "3T", "4T", "Total"];
        for (var i = 0; i < cab.Length; i++) ws.Cell(13, 2 + i).Value = cab[i];
        ws.Range(13, 2, 13, 7).Style.Font.SetBold().Font.SetFontColor(Azul);
        var f = 13;
        foreach (var v in p.Volumen)
        {
            f++;
            var actividad = v.Actividad.Length == 0 ? N.SinActividad : v.Actividad;
            ws.Cell(f, 2).Value = $"{actividad} · {EtiquetaDeduccion(v.Deduccion)}";
            for (var q = 1; q <= 4; q++)
                Formula(ws, f, 2 + q, $"SUMIFS({N.IvaBase},{N.IvaSentido},{rep},{N.IvaActividad},{N.Literal(actividad)},{N.IvaDeduccion},{Txt(v.Deduccion.Texto())},{N.IvaTrim},{q})");
            Formula(ws, f, 7, $"SUM(C{f}:F{f})").Style.Font.SetBold();
        }
        f++;
        ws.Cell(f, 2).Value = "Total";
        for (var col = 3; col <= 7; col++)
        {
            var l = XLHelper.GetColumnLetterFromNumber(col);
            Formula(ws, f, col, $"SUM({l}14:{l}{f - 1})");
        }
        ws.Range(f, 2, f, 7).Style.Font.SetBold().Fill.SetBackgroundColor(Fondo);

        f += 2;
        Seccion(ws, f, 2, 7, p.DefinitivaFijada ? "3 · IVA soportado sujeto a prorrata y regularización" : "3 · IVA soportado sujeto a prorrata y regularización estimada");
        f++;
        for (var i = 1; i < cab.Length; i++) ws.Cell(f, 2 + i).Value = cab[i];
        ws.Range(f, 3, f, 7).Style.Font.SetBold().Font.SetFontColor(Azul);
        var fs = f + 1;
        ws.Cell(fs, 2).Value = "Cuota soportada sujeta a prorrata";
        ws.Cell(fs + 1, 2).Value = "Deducido con la prorrata provisional";
        ws.Cell(fs + 2, 2).Value = p.DefinitivaFijada ? "Deducible con la prorrata definitiva" : "Deducible con la prorrata real acumulada";
        ws.Cell(fs + 3, 2).Value = p.DefinitivaFijada ? "Regularización a favor (+) o en contra (−)" : "Regularización estimada a favor (+) o en contra (−)";
        var aplicada = p.DefinitivaFijada ? "$C$11" : N.ProrrataReal;
        for (var q = 1; q <= 4; q++)
        {
            var col = 2 + q;
            var l = XLHelper.GetColumnLetterFromNumber(col);
            var colIva = XLHelper.GetColumnLetterFromNumber(ColumnaCuota(q));
            Formula(ws, fs, col, $"SUMIFS({N.IvaCuota},{N.IvaSentido},{Txt(TextosIva.Soportado)},{N.IvaDeduccion},{Txt(TextosIva.Prorrata)},{N.IvaTrim},{q})");
            Formula(ws, fs + 1, col, $"'{HojaIva}'!{colIva}{mapa.FilaIva[Calculo303.ClaveInterioresProrrata]}+'{HojaIva}'!{colIva}{mapa.FilaIva[Calculo303.ClaveAutorrepercutidasProrrata]}");
            Formula(ws, fs + 2, col, $"ROUND({l}{fs}*{aplicada},2)");
            Formula(ws, fs + 3, col, $"{l}{fs + 2}-{l}{fs + 1}");
        }
        for (var r = fs; r <= fs + 3; r++) Formula(ws, r, 7, $"SUM(C{r}:F{r})").Style.Font.SetBold();
        ws.Range(fs + 3, 2, fs + 3, 7).Style.Font.SetBold().Fill.SetBackgroundColor(Fondo);
        mapa.FilaRegularizacion = fs + 3;
        Nota(ws.Cell(fs + 5, 2), p.DefinitivaFijada
            ? "La regularización va en el 303 del 4T (línea «Regularización de la prorrata definitiva») y en el asiento del 31/12 a la 4729."
            : "La regularización se aplica en el 303 del 4T con la prorrata definitiva del año; esta cifra es una estimación con lo contabilizado hasta la fecha de los datos.");
    }

    // ---------- Controles ----------

    private static void Controles(XLWorkbook libro, InformeGestoria inf, Mapa mapa)
    {
        var ws = libro.Worksheet(HojaControles);
        Cabecera(ws, "Controles automáticos", "Comprobaciones antes de enviar el informe a la gestoría: cada una dice qué hacer si no está bien", 7);
        Anchos(ws, [2, 5, 62, 16, 16, 16, 12, 3, 110]);
        string[] cab = ["#", "Control", "Valor", "Referencia", "Diferencia", "Estado"];
        for (var i = 0; i < cab.Length; i++) ws.Cell(5, 2 + i).Value = cab[i];
        ws.Cell(5, 9).Value = "Qué hacer";
        ws.Range(5, 2, 5, 9).Style.Fill.SetBackgroundColor(Azul).Font.SetBold().Font.SetFontColor(XLColor.White);
        var f = 5;
        var n = 0;
        foreach (var c in ControlesGestoria.Calcular(inf))
        {
            f++;
            n++;
            ws.Cell(f, 2).Value = n;
            ws.Cell(f, 2).Style.Font.SetFontColor(Gris);
            ws.Cell(f, 3).Value = c.Nombre;
            var formato = c.EsImporte ? FormatoImporte : "#,##0;[Red]-#,##0;0";
            if (c.FormulaValor is { } fv) ws.Cell(f, 4).FormulaA1 = fv; else ws.Cell(f, 4).Value = c.Valor;
            if (c.Referencia is decimal r)
            {
                if (c.FormulaReferencia is { } fr) ws.Cell(f, 5).FormulaA1 = fr; else ws.Cell(f, 5).Value = r;
            }
            if (c.Diferencia is not null) ws.Cell(f, 6).FormulaA1 = $"D{f}-E{f}";
            ws.Range(f, 4, f, 6).Style.NumberFormat.Format = formato;
            if (c.Regla == ReglaControl.ConActivos) ws.Cell(f, 5).Style.NumberFormat.Format = "0";
            ws.Cell(f, 7).FormulaA1 = c.FormulaEstado(f);
            ws.Cell(f, 7).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(f, 9).Value = c.Nota;
            ws.Cell(f, 9).Style.Font.SetFontColor(Gris).Alignment.SetWrapText();
        }
        mapa.PrimeraControl = 6;
        mapa.UltimaControl = Math.Max(6, f);
        var estados = ws.Range(6, 7, mapa.UltimaControl, 7);
        estados.AddConditionalFormat().WhenEquals("\"OK\"").Fill.SetBackgroundColor(Ok);
        estados.AddConditionalFormat().WhenEquals("\"Aviso\"").Fill.SetBackgroundColor(Ambar);
        estados.AddConditionalFormat().WhenEquals("\"Revisar\"").Fill.SetBackgroundColor(Rojo);
        ws.SheetView.FreezeRows(5);
    }

    // ---------- PyG y Situación ----------

    private static void PyG(XLWorkbook libro, InformeGestoria inf, Mapa mapa)
    {
        var ws = libro.Worksheet(HojaPyG);
        var raices = inf.PyG.Filas.Where(f => f.Importe != 0m).Select(f => Actividad(f.Actividad)).Distinct().OrderBy(c => c == N.SinActividad).ThenBy(c => c, StringComparer.CurrentCulture).ToList();
        var total = 3 + raices.Count;
        Cabecera(ws, "Cuenta de pérdidas y ganancias", $"Acumulado del ejercicio {inf.Ejercicio} hasta el {inf.FinTrimestre:dd/MM/yyyy}, por actividad (sin la regularización ni el cierre)", total);
        ws.Column(2).Width = 62;
        for (var c = 3; c <= total; c++) ws.Column(c).Width = 17;
        ws.Cell(5, 2).Value = "Concepto";
        for (var i = 0; i < raices.Count; i++) ws.Cell(5, 3 + i).Value = raices[i];
        ws.Cell(5, total).Value = "Total";
        ws.Range(5, 2, 5, total).Style.Fill.SetBackgroundColor(Azul).Font.SetBold().Font.SetFontColor(XLColor.White).Alignment.SetWrapText();
        var f = 5;
        foreach (var ap in inf.PyG.Filas.GroupBy(x => (x.OrdenApartado, x.Apartado)).OrderBy(g => g.Key.OrdenApartado))
        {
            f++;
            ws.Cell(f, 2).Value = ap.Key.Apartado;
            var crit = $"{N.PygApartado},{N.Literal(ap.Key.Apartado)}";
            for (var i = 0; i < raices.Count; i++) Formula(ws, f, 3 + i, $"SUMIFS({N.PygImporte},{crit},{N.PygActividad},{N.Literal(raices[i])})");
            Formula(ws, f, total, $"SUMIFS({N.PygImporte},{crit})");
            ws.Range(f, 2, f, total).Style.Fill.SetBackgroundColor(Fondo).Font.SetBold().Font.SetFontColor(Azul);
            foreach (var ti in ap.GroupBy(x => (x.OrdenTitulo, x.Titulo)).OrderBy(g => g.Key.OrdenTitulo))
            {
                f++;
                ws.Cell(f, 2).Value = "   " + ti.Key.Titulo;
                var c2 = $"{crit},{N.PygTitulo},{N.Literal(ti.Key.Titulo)}";
                for (var i = 0; i < raices.Count; i++) Formula(ws, f, 3 + i, $"SUMIFS({N.PygImporte},{c2},{N.PygActividad},{N.Literal(raices[i])})");
                Formula(ws, f, total, $"SUMIFS({N.PygImporte},{c2})").Style.Font.SetBold();
            }
        }
        f += 2;
        mapa.FilaResultadoPyG = f;
        mapa.ColumnaTotalPyG = total;
        ws.Cell(f, 2).Value = "Resultado del periodo";
        for (var i = 0; i < raices.Count; i++) Formula(ws, f, 3 + i, $"SUMIFS({N.PygImporte},{N.PygActividad},{N.Literal(raices[i])})");
        Formula(ws, f, total, $"SUM({N.PygImporte})");
        ws.Range(f, 2, f, total).Style.Fill.SetBackgroundColor(Resultado).Font.SetBold();
        var impuesto = inf.PyG.Filas.Any(x => x.CuentaContable.StartsWith("630", StringComparison.Ordinal));
        Nota(ws.Cell(f + 2, 2), (impuesto ? "Incluye el impuesto sobre beneficios contabilizado (630). " : "Antes de impuestos: no hay impuesto sobre beneficios (630) contabilizado. ") +
            "El detalle, un apunte por fila, está en «Datos PyG» y en la tabla dinámica de «TDPyG».");
        ws.SheetView.FreezeRows(5);
    }

    private static void Situacion(XLWorkbook libro, InformeGestoria inf)
    {
        var ws = libro.Worksheet(HojaSituacion);
        Cabecera(ws, "Balance de situación", $"Saldos a {inf.FinTrimestre:dd/MM/yyyy} (sin el asiento de cierre)", 6);
        Anchos(ws, [2, 64, 18, 3, 64, 18]);
        var bloques = inf.Situacion.Filas.GroupBy(x => (x.OrdenBloque, x.Bloque)).OrderBy(g => g.Key.OrdenBloque).ToList();
        if (bloques.Count == 0) { Nota(ws.Cell(5, 2), "No hay saldos en el balance a esa fecha."); return; }
        void Lado(int col, IReadOnlyList<IGrouping<(int OrdenBloque, string Bloque), FilaBalance>> lado, string titulo)
        {
            ws.Cell(5, col).Value = titulo;
            ws.Cell(5, col + 1).Value = "Importe";
            ws.Range(5, col, 5, col + 1).Style.Fill.SetBackgroundColor(Azul).Font.SetBold().Font.SetFontColor(XLColor.White);
            var f = 5;
            foreach (var bl in lado)
            {
                var critLado = $"{N.SitLado},{N.Literal(bl.Key.Bloque)}";
                foreach (var ap in bl.GroupBy(x => (x.OrdenApartado, x.Apartado)).OrderBy(g => g.Key.OrdenApartado))
                {
                    f++;
                    ws.Cell(f, col).Value = ap.Key.Apartado;
                    var critAp = $"{critLado},{N.SitApartado},{N.Literal(ap.Key.Apartado)}";
                    Formula(ws, f, col + 1, $"SUMIFS({N.SitImporte},{critAp})");
                    ws.Range(f, col, f, col + 1).Style.Fill.SetBackgroundColor(Fondo).Font.SetBold().Font.SetFontColor(Azul);
                    foreach (var ti in ap.GroupBy(x => (x.OrdenTitulo, x.Titulo)).OrderBy(g => g.Key.OrdenTitulo))
                    {
                        f++;
                        ws.Cell(f, col).Value = "   " + ti.Key.Titulo;
                        var critTi = $"{critAp},{N.SitTitulo},{N.Literal(ti.Key.Titulo)}";
                        Formula(ws, f, col + 1, $"SUMIFS({N.SitImporte},{critTi})");
                        foreach (var sub in ti.Where(x => x.Subtitulo is { Length: > 0 }).Select(x => x.Subtitulo!).Distinct())
                        {
                            f++;
                            ws.Cell(f, col).Value = "      " + sub;
                            ws.Cell(f, col).Style.Font.SetFontColor(Gris);
                            Formula(ws, f, col + 1, $"SUMIFS({N.SitImporte},{critTi},{N.SitSubtitulo},{N.Literal(sub)})").Style.Font.SetFontColor(Gris);
                        }
                    }
                }
            }
            f += 2;
            ws.Cell(f, col).Value = "TOTAL " + titulo;
            Formula(ws, f, col + 1, string.Join("+", lado.Select(b => $"SUMIFS({N.SitImporte},{N.SitLado},{N.Literal(b.Key.Bloque)})")));
            ws.Range(f, col, f, col + 1).Style.Fill.SetBackgroundColor(Resultado).Font.SetBold();
        }
        Lado(2, bloques.Take(1).ToList(), bloques[0].Key.Bloque);
        if (bloques.Count > 1) Lado(5, bloques.Skip(1).ToList(), string.Join(" · ", bloques.Skip(1).Select(b => b.Key.Bloque)));
        ws.SheetView.FreezeRows(5);
    }

    // ---------- Ratios ----------

    private static string Valor(ValorRatio r) => r.Valor is not decimal v ? "sin datos" : r.Definicion.Unidad switch
    {
        UnidadRatio.Porcentaje => Math.Round(v, 1, MidpointRounding.AwayFromZero).ToString("0.0", Es) + " %",
        UnidadRatio.Dias => Math.Round(v, 0, MidpointRounding.AwayFromZero).ToString("0", Es) + " días",
        _ => Math.Round(v, 2, MidpointRounding.AwayFromZero).ToString("0.00", Es),
    };

    private static string Estado(EstadoRatio e) => e switch
    {
        EstadoRatio.Bien => "Bien",
        EstadoRatio.Vigilar => "Vigilar",
        EstadoRatio.Mal => "Mal",
        _ => "Sin datos",
    };

    private static void Ratios(XLWorkbook libro, InformeGestoria inf)
    {
        var ws = libro.Worksheet(HojaRatios);
        var r = inf.Ratios;
        Cabecera(ws, "Ratios financieros", r is null
            ? "Sin ratios: " + (inf.MotivoSinRatios ?? "no hay modelo de balance de situación.")
            : $"Balance a {r.FechaBalance:dd/MM/yyyy} y resultado de los 12 meses desde el {r.DesdeResultado:dd/MM/yyyy}. Mismas definiciones y rangos que la pantalla de inicio.", 8);
        Anchos(ws, [2, 34, 26, 14, 30, 11, 50, 3, 70]);
        if (r is null) return;
        string[] cab = ["Ratio", "Familia", "Valor", "Referencia", "Estado", "Fórmula"];
        for (var i = 0; i < cab.Length; i++) ws.Cell(5, 2 + i).Value = cab[i];
        ws.Cell(5, 9).Value = "Motivo (si no hay datos)";
        ws.Range(5, 2, 5, 9).Style.Fill.SetBackgroundColor(Azul).Font.SetBold().Font.SetFontColor(XLColor.White);
        var f = 5;
        foreach (var v in r.Ratios)
        {
            f++;
            ws.Cell(f, 2).Value = v.Definicion.Nombre;
            ws.Cell(f, 2).Style.Font.SetBold();
            ws.Cell(f, 3).Value = v.Definicion.Familia switch
            {
                FamiliaRatio.Liquidez => "Liquidez",
                FamiliaRatio.SolvenciaEndeudamiento => "Solvencia y endeudamiento",
                FamiliaRatio.RentabilidadMargenes => "Rentabilidad y márgenes",
                _ => "Gestión",
            };
            ws.Cell(f, 4).Value = Valor(v);
            ws.Cell(f, 4).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right).Font.SetBold();
            ws.Cell(f, 5).Value = v.Definicion.Referencia;
            ws.Cell(f, 6).Value = Estado(v.Estado);
            ws.Cell(f, 6).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            ws.Cell(f, 7).Value = v.Definicion.Formula;
            ws.Cell(f, 7).Style.Font.SetFontColor(Gris);
            if (v.Motivo is { } m) Nota(ws.Cell(f, 9), m);
        }
        var estados = ws.Range(6, 6, Math.Max(6, f), 6);
        estados.AddConditionalFormat().WhenEquals("\"Bien\"").Fill.SetBackgroundColor(Ok);
        estados.AddConditionalFormat().WhenEquals("\"Vigilar\"").Fill.SetBackgroundColor(Ambar);
        estados.AddConditionalFormat().WhenEquals("\"Mal\"").Fill.SetBackgroundColor(Rojo);
        estados.AddConditionalFormat().WhenEquals("\"Sin datos\"").Fill.SetBackgroundColor(Gris);
        f += 2;
        Seccion(ws, f, 2, 7, "Partidas de las que salen");
        f++;
        ws.Cell(f, 2).Value = "Partida";
        ws.Cell(f, 3).Value = "Importe";
        ws.Cell(f, 5).Value = "Cuentas";
        ws.Range(f, 2, f, 7).Style.Font.SetBold().Font.SetFontColor(Azul);
        foreach (var p in r.Ratios.SelectMany(x => x.Partidas).Where(p => p.Cuentas.Length > 0).GroupBy(p => p.Nombre).Select(g => g.First()))
        {
            f++;
            ws.Cell(f, 2).Value = p.Nombre;
            ws.Cell(f, 3).Value = p.Importe;
            Importe(ws.Cell(f, 3));
            ws.Cell(f, 5).Value = p.Cuentas;
            ws.Range(f, 5, f, 9).Merge();
            ws.Cell(f, 5).Style.Font.SetFontColor(Gris);
        }
        ws.Column(3).Width = 26;
    }

    // ---------- Resumen ----------

    private static void Resumen(XLWorkbook libro, InformeGestoria inf, Mapa mapa)
    {
        var ws = libro.Worksheet(HojaResumen);
        var t = inf.Trimestre;
        var datosHasta = inf.UltimoAsiento is DateTime u ? $"datos contables hasta el {u:dd/MM/yyyy}" : "sin asientos contabilizados";
        Cabecera(ws, "Informe trimestral para la gestoría", $"{inf.RazonSocial} · ejercicio {inf.Ejercicio} · {t}.º trimestre · {datosHasta}", 11);
        Anchos(ws, [2, 30, 16, 16, 16, 16, 16, 3, 22, 34, 16]);
        void Kpi(int col, int ancho, string etiqueta, string formula, string formato, string pie, bool pieFormula = false)
        {
            var bloque = ws.Range(5, col, 7, col + ancho - 1);
            bloque.Style.Fill.SetBackgroundColor(Fondo);
            ws.Cell(5, col).Value = etiqueta;
            ws.Cell(5, col).Style.Font.SetFontColor(Gris).Font.SetFontSize(9);
            var valor = ws.Range(6, col, 6, col + ancho - 1);
            if (ancho > 1) valor.Merge();
            ws.Cell(6, col).FormulaA1 = formula;
            ws.Cell(6, col).Style.NumberFormat.Format = formato;
            ws.Cell(6, col).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(Azul).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            if (pieFormula) ws.Cell(7, col).FormulaA1 = pie; else ws.Cell(7, col).Value = pie;
            ws.Cell(7, col).Style.Font.SetFontColor(Gris).Font.SetFontSize(9);
        }
        var resIva = Ref(HojaIva, mapa.FilaResultado, ColumnaCuota(t));
        Kpi(2, 2, $"IVA {t}T · RESULTADO", resIva, FormatoEuros, $"IF({resIva}>=0,\"a ingresar\",\"a compensar\")", pieFormula: true);
        Kpi(4, 2, "PRORRATA", $"TEXT({N.ProrrataProv},\"0%\")&\" → \"&TEXT({N.ProrrataReal},\"0%\")", "General", "provisional → real acumulada");
        Kpi(6, 2, inf.Prorrata.DefinitivaFijada ? "REGULARIZACIÓN PRORRATA" : "REGULARIZACIÓN ESTIMADA", Ref(HojaProrrata, mapa.FilaRegularizacion, 7), FormatoEuros,
            inf.Prorrata.DefinitivaFijada ? "con la definitiva, en el 303 del 4T" : "a favor (+) o en contra (−), en el 303 del 4T");
        Kpi(9, 2, "RESULTADO ACUMULADO", Ref(HojaPyG, mapa.FilaResultadoPyG, mapa.ColumnaTotalPyG), FormatoEuros, $"pérdidas y ganancias hasta el {inf.FinTrimestre:dd/MM/yyyy}");
        Kpi(11, 1, "CONTROLES", $"COUNTIF('{HojaControles}'!G{mapa.PrimeraControl}:G{mapa.UltimaControl},\"Revisar\")", "0", "a revisar");
        ws.Cell(6, 11).Style.Font.SetFontColor(Rojo);

        Seccion(ws, 9, 2, 7, "IVA por trimestres");
        for (var q = 1; q <= 5; q++)
            ws.Cell(10, 2 + q).Value = q == 5 ? "Año" : q > t ? $"{q}T (parcial)" : $"{q}T";
        ws.Range(10, 3, 10, 7).Style.Font.SetBold().Font.SetFontColor(Azul).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        (string Texto, int Fila)[] filas = [("IVA devengado", mapa.FilaTotalDevengado), ("IVA deducible", mapa.FilaTotalDeducible), ("Resultado", mapa.FilaResultado)];
        for (var i = 0; i < filas.Length; i++)
        {
            ws.Cell(11 + i, 2).Value = filas[i].Texto;
            for (var q = 1; q <= 5; q++) Formula(ws, 11 + i, 2 + q, Ref(HojaIva, filas[i].Fila, ColumnaCuota(q == 5 ? 0 : q)));
        }
        ws.Range(13, 2, 13, 7).Style.Font.SetBold();
        ws.Range(13, 3, 13, 6).AddConditionalFormat().DataBar(XLColor.FromHtml("#8FB8DE")).LowestValue().HighestValue();

        Seccion(ws, 15, 2, 7, "IVA soportado del año según su deducción");
        string L(string clave) => Ref(HojaIva, mapa.FilaIva[clave], ColumnaCuota(0));
        (string, string)[] reparto =
        [
            ("Deducible al 100 %", $"{L(Calculo303.ClaveInteriores100)}+{L(Calculo303.ClaveAutorrepercutidas100)}"),
            ("Deducido por prorrata", $"{L(Calculo303.ClaveInterioresProrrata)}+{L(Calculo303.ClaveAutorrepercutidasProrrata)}"),
            ("Prorrata no deducida", $"{L(Calculo303.ClaveSujetaProrrata)}-C17"),
            ("No deducible (actividades exentas)", L(Calculo303.ClaveNoDeducible)),
            ("Sin clasificar (sin centro)", L(Calculo303.ClaveSinClasificar)),
        ];
        for (var i = 0; i < reparto.Length; i++)
        {
            ws.Cell(16 + i, 2).Value = reparto[i].Item1;
            Formula(ws, 16 + i, 3, reparto[i].Item2);
        }
        ws.Range(16, 3, 20, 3).AddConditionalFormat().DataBar(XLColor.FromHtml("#2A9D8F")).LowestValue().HighestValue();
        Nota(ws.Cell(21, 2), "Barras de datos en lugar de gráficos: la librería con la que se genera el libro no crea gráficos.");
        ws.Cell(21, 2).Style.Font.SetFontSize(8);

        Seccion(ws, 9, 9, 11, "Contenido");
        (string Hoja, string Texto)[] indice =
        [
            (HojaIva, "Liquidación por trimestres"), (HojaProrrata, "Porcentajes y regularización"), (HojaControles, "Avisos antes de presentar"),
            (HojaPyG, "Pérdidas y ganancias por actividad"), (HojaSituacion, "Balance de situación"),
            (HojaTDPyG, "Tabla dinámica de pérdidas y ganancias"), (HojaTDSituacion, "Tabla dinámica del balance por meses"), (HojaRatios, "Ratios con semáforo"),
            (HojaEmitidas, "Facturas emitidas"), (HojaRecibidas, "Facturas recibidas"), (HojaDatosIva, "Líneas de IVA, una por desglose"),
            (HojaDatosPyG, "Pérdidas y ganancias, un apunte por fila"), (HojaDatosSituacion, "Balance, un apunte por fila"),
        ];
        for (var i = 0; i < indice.Length; i++)
        {
            var c = ws.Cell(10 + i, 9);
            c.Value = indice[i].Hoja;
            c.SetHyperlink(new XLHyperlink($"'{indice[i].Hoja}'!A1"));
            c.Style.Font.SetFontColor(Enlace).Font.SetUnderline();
            Nota(ws.Cell(10 + i, 10), indice[i].Texto);
        }

        Seccion(ws, 23, 2, 7, "Sello");
        (string, string)[] sello =
        [
            ("Sociedad", inf.RazonSocial), ("Ejercicio", inf.Ejercicio.ToString(CultureInfo.InvariantCulture)),
            ("Trimestre", $"{t}.º (del {new DateTime(inf.Ejercicio, 3 * t - 2, 1):dd/MM/yyyy} al {inf.FinTrimestre:dd/MM/yyyy})"),
            ("Generado", inf.Generado.ToString("dd/MM/yyyy HH:mm:ss", Es)),
            ("Último asiento incluido", inf.UltimoAsiento?.ToString("dd/MM/yyyy", Es) ?? "ninguno"),
            ("Origen", "ProContaVF · Informes › Informe para la gestoría"),
        ];
        for (var i = 0; i < sello.Length; i++)
        {
            Nota(ws.Cell(24 + i, 2), sello[i].Item1);
            ws.Cell(24 + i, 3).Value = sello[i].Item2;
        }
    }
}
