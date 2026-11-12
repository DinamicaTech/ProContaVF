using System.IO;
using ClosedXML.Excel;
using ProConta.Contabilidad.Balances;

namespace ProConta.Informes.Excel;

/// <summary>
/// Libro Excel de un balance (informes/excel), sin automatizar Excel: la hoja del informe con la tabla dinámica y la hoja de
/// datos con un apunte por fila en la tabla «TablaDatosBalance», con las mismas columnas que los libros del ProConta anterior
/// (BalancePyG2026_Nuevo.xlsx y BalanceSituacion2026_Nuevo.xlsx) más «Bloque». Si hay cuentas que el modelo no clasifica,
/// van en la hoja «SinClasificar». Excel recalcula la tabla dinámica al abrir el libro.
/// </summary>
public static class ExportadorBalanceExcel
{
    public const string FormatoImporte = "#,##0.00;[Red]-#,##0.00";
    public const string NombreTabla = "TablaDatosBalance";

    /// <summary>Columnas de la hoja de datos, en orden.</summary>
    public static readonly IReadOnlyList<string> Columnas =
    [
        "Tipo", "Fecha", "Año", "Mes", "Dia", "Apartado", "Titulo", "SubTitulo", "Cuenta", "SubCuenta", "Importe", "CuentaContable", "Signo",
        "ASIAPU", "ORDAPU", "CONAPU", "CentroDeCoste", "SubDepartamento", "NombreDepartamento", "Bloque",
    ];

    /// <summary>Nombre de la hoja del informe: «Situacion» o «PyG», como en los libros anteriores.</summary>
    public static string NombreInforme(TipoBalance tipo) => tipo == TipoBalance.Situacion ? "Situacion" : "PyG";

    /// <summary>Nombre de fichero propuesto, por ejemplo «BalanceSituacion_2025_20261111-1530.xlsx».</summary>
    public static string NombreFichero(TipoBalance tipo, int ejercicio, DateTime ahora) =>
        $"Balance{NombreInforme(tipo)}_{ejercicio}_{ahora:yyyyMMdd-HHmmss}.xlsx";

    public static void Exportar(ResultadoBalance balance, string ruta)
    {
        ArgumentNullException.ThrowIfNull(balance);
        if (balance.Filas.Count == 0)
            throw new InvalidOperationException(
                $"El ejercicio {balance.Ejercicio} no tiene apuntes contabilizados que entren en el {Titulo(balance.Modelo.Tipo)}: no hay nada que llevar a Excel. " +
                "Comprueba que estás en el ejercicio correcto (cabecera de la ventana) y que sus asientos están importados o contabilizados.");
        using var libro = new XLWorkbook();
        var nombre = NombreInforme(balance.Modelo.Tipo);
        var informe = libro.Worksheets.Add(nombre);
        var datos = libro.Worksheets.Add($"Datos{nombre}");
        var tabla = EscribirDatos(datos, balance);
        CrearTablaDinamica(informe, tabla, balance);
        if (balance.SinClasificar.Count > 0) EscribirSinClasificar(libro.Worksheets.Add("SinClasificar"), balance);
        informe.SetTabActive();
        libro.SaveAs(ruta);
    }

    private static string Titulo(TipoBalance tipo) => tipo == TipoBalance.Situacion ? "balance de situación" : "balance de pérdidas y ganancias";

    private static IXLTable EscribirDatos(IXLWorksheet ws, ResultadoBalance balance)
    {
        for (var c = 0; c < Columnas.Count; c++) ws.Cell(1, c + 1).Value = Columnas[c];
        var f = 1;
        foreach (var x in balance.Filas)
        {
            f++;
            var fila = ws.Row(f);
            fila.Cell(1).Value = balance.Modelo.Nombre;
            fila.Cell(2).Value = x.Fecha;
            fila.Cell(3).Value = x.Fecha.Year;
            fila.Cell(4).Value = x.Fecha.Month;
            fila.Cell(5).Value = x.Fecha.Day;
            fila.Cell(6).Value = x.Apartado;
            fila.Cell(7).Value = x.Titulo;
            fila.Cell(8).Value = x.Subtitulo ?? "";
            fila.Cell(9).Value = x.Cuenta;
            fila.Cell(10).Value = x.Subcuenta;
            fila.Cell(11).Value = x.Importe;
            fila.Cell(12).Value = long.TryParse(x.CuentaContable, out var codigo) ? codigo : x.CuentaContable;
            fila.Cell(13).Value = x.Importe < 0 ? -1 : 1;
            fila.Cell(14).Value = x.IdAsiento;
            fila.Cell(15).Value = x.Orden;
            fila.Cell(16).Value = x.Concepto;
            fila.Cell(17).Value = x.CentroCoste ?? "";
            fila.Cell(18).Value = x.SubDepartamento ?? "";
            fila.Cell(19).Value = x.NombreDepartamento ?? "";
            fila.Cell(20).Value = x.Bloque;
        }
        var tabla = ws.Range(1, 1, f, Columnas.Count).CreateTable(NombreTabla);
        tabla.Theme = XLTableTheme.TableStyleMedium2;
        ws.Column(2).Style.NumberFormat.Format = "dd/mm/yyyy";
        ws.Column(11).Style.NumberFormat.Format = FormatoImporte;
        ws.SheetView.FreezeRows(1);
        ws.Columns(1, Columnas.Count).AdjustToContents(1, Math.Min(f, 500), 6, 45);
        return tabla;
    }

    /// <summary>
    /// Bloque (solo en situación) › Apartado › Título › Cuenta › Subcuenta en filas, los meses en columnas, el centro de coste y
    /// el departamento como filtros y la suma de importes como valor. Los apartados y títulos van en el orden oficial del modelo
    /// (el de la hoja de datos), no en orden alfabético.
    /// </summary>
    private static void CrearTablaDinamica(IXLWorksheet ws, IXLTable tabla, ResultadoBalance balance)
    {
        ws.Cell(1, 1).Value = $"{balance.Modelo.Nombre} · {balance.RazonSocial} · ejercicio {balance.Ejercicio}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 13;
        var pt = ws.PivotTables.Add($"TablaDinamica{NombreInforme(balance.Modelo.Tipo)}", ws.Cell(5, 1), tabla);
        pt.ReportFilters.Add("CentroDeCoste");
        pt.ReportFilters.Add("NombreDepartamento");
        if (balance.Modelo.Tipo == TipoBalance.Situacion) Fila(pt.RowLabels.Add("Bloque"));
        Fila(pt.RowLabels.Add("Apartado"));
        Fila(pt.RowLabels.Add("Titulo"));
        Fila(pt.RowLabels.Add("Cuenta"));
        pt.RowLabels.Add("SubCuenta");
        pt.ColumnLabels.Add("Mes").SetSort(XLPivotSortType.Ascending);
        pt.Values.Add("Importe", "Suma de Importe").SetSummaryFormula(XLPivotSummary.Sum).NumberFormat.Format = FormatoImporte;
        pt.Layout = XLPivotLayout.Compact;
        pt.SetSubtotals(XLPivotSubtotals.AtTop);
        pt.SetShowGrandTotalsRows(true).SetShowGrandTotalsColumns(true);
        pt.SetRowHeaderCaption("Etiquetas de fila").SetColumnHeaderCaption("Mes");
        ws.Column(1).Width = 70;
        ws.Columns(2, 15).Width = 15; // 12 meses + total general: caben importes de 7 cifras sin «########».
    }

    /// <summary>Los niveles de fila con su subtotal arriba, como en el libro anterior: cada apartado, título y cuenta muestra su suma.</summary>
    private static void Fila(IXLPivotField campo) => campo.AddSubtotal(XLSubtotalFunction.Automatic);

    private static void EscribirSinClasificar(IXLWorksheet ws, ResultadoBalance balance)
    {
        ws.Cell(1, 1).Value = $"Cuentas con apuntes que el modelo «{balance.Modelo.Nombre}» no clasifica: no están en el balance.";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "Para incluirlas hay que añadir su prefijo a las reglas del modelo (tabla ReglasCuentasBalance) o corregir la cuenta de esos apuntes.";
        string[] cab = ["Cuenta", "Nombre", "Apuntes", "Debe", "Haber", "Saldo (Debe − Haber)"];
        for (var c = 0; c < cab.Length; c++) ws.Cell(4, c + 1).Value = cab[c];
        var f = 4;
        foreach (var s in balance.SinClasificar)
        {
            f++;
            ws.Cell(f, 1).Value = s.Cuenta;
            ws.Cell(f, 2).Value = s.Nombre;
            ws.Cell(f, 3).Value = s.Apuntes;
            ws.Cell(f, 4).Value = s.Debe;
            ws.Cell(f, 5).Value = s.Haber;
            ws.Cell(f, 6).Value = s.Saldo;
        }
        ws.Range(4, 1, f, cab.Length).CreateTable("TablaSinClasificar");
        ws.Range(5, 4, f, 6).Style.NumberFormat.Format = FormatoImporte;
        ws.Columns(1, cab.Length).AdjustToContents(4, f, 8, 60);
    }
}
