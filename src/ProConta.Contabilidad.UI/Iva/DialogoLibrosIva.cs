using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.Contabilidad.Iva;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.Iva;

/// <summary>Formato de salida de los libros registro de IVA.</summary>
public enum FormatoLibrosIva { Pdf, Excel }

/// <summary>
/// Diálogo de Informes › Libros registro de IVA… (informes/excel/libros-registro-iva): periodo del ejercicio de trabajo (trimestre, mes o
/// fechas), libros y formato. Valida antes de cerrar y dice qué corregir. Recuerda lo último elegido mientras el programa está abierto.
/// </summary>
public static class DialogoLibrosIva
{
    private static (ParametrosLibrosIva Parametros, FormatoLibrosIva Formato, int Modo)? _ultimo;

    public static (ParametrosLibrosIva Parametros, FormatoLibrosIva Formato)? Pedir(Window? propietario, int ejercicio, string razonSocial, DateTime hoy)
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var previo = _ultimo is { } u && u.Parametros.Desde.Year == ejercicio
            ? u
            : (ParametrosLibrosIva.Trimestre(ejercicio, ParametrosLibrosIva.TrimestrePropuesto(ejercicio, hoy)), FormatoLibrosIva.Pdf, 0);
        var d = new DialogoProConta { Title = "Libros registro de IVA", Owner = propietario, Width = 560, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{razonSocial} · ejercicio {ejercicio}. Libro de facturas expedidas y de facturas recibidas a partir de los registros de IVA de ventas y compras, " +
                   "por la fecha contable del asiento (como el 303), con los totales por tipo y el cuadre con el 303 del periodo. " +
                   "El fichero se guarda en la carpeta de documentos de la empresa (Informes del año), vinculado al ejercicio, y se abre.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });

        panel.Children.Add(new TextBlock { Text = "Periodo", Margin = new Thickness(0, 0, 0, 3) });
        var rejilla = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        rejilla.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < 3; i++) rejilla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        void Poner(UIElement e, int fila, int col)
        {
            Grid.SetRow(e, fila);
            Grid.SetColumn(e, col);
            rejilla.Children.Add(e);
        }

        var porTrimestre = new RadioButton { Content = "Trimestre", GroupName = "periodo", Margin = new Thickness(0, 4, 16, 4), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Un trimestre entero: es el periodo del 303 y el que suele pedir la gestoría" };
        var trimestres = new ComboBox { Width = 210, Margin = new Thickness(0, 2, 0, 2),
            ToolTip = "Trimestre del ejercicio de trabajo. Por defecto, el último terminado (el que toca presentar)" };
        for (var t = 1; t <= 4; t++)
        {
            var p = ParametrosLibrosIva.Trimestre(ejercicio, t);
            trimestres.Items.Add($"{t}T · {p.Desde:dd/MM} a {p.Hasta:dd/MM}");
        }
        var porMes = new RadioButton { Content = "Mes", GroupName = "periodo", Margin = new Thickness(0, 4, 16, 4), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Un mes entero del ejercicio de trabajo" };
        var meses = new ComboBox { Width = 210, Margin = new Thickness(0, 2, 0, 2), ToolTip = "Mes del ejercicio de trabajo" };
        for (var m = 1; m <= 12; m++) meses.Items.Add(es.TextInfo.ToTitleCase(new DateTime(ejercicio, m, 1).ToString("MMMM", es)));
        var porFechas = new RadioButton { Content = "Entre fechas", GroupName = "periodo", Margin = new Thickness(0, 4, 16, 4), VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Cualquier periodo del ejercicio de trabajo, con las dos fechas incluidas" };
        var fechas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        DatePicker Fecha(DateTime valor, string ayuda)
        {
            var dp = new DatePicker { SelectedDate = valor, Width = 130, Margin = new Thickness(0, 0, 8, 0), ToolTip = ayuda,
                DisplayDateStart = new DateTime(ejercicio, 1, 1), DisplayDateEnd = new DateTime(ejercicio, 12, 31) };
            fechas.Children.Add(dp);
            return dp;
        }
        var desde = Fecha(previo.Item1.Desde, "Primer día que entra (fecha contable del asiento)");
        var hasta = Fecha(previo.Item1.Hasta, "Último día que entra, incluido");
        Poner(porTrimestre, 0, 0); Poner(trimestres, 0, 1);
        Poner(porMes, 1, 0); Poner(meses, 1, 1);
        Poner(porFechas, 2, 0); Poner(fechas, 2, 1);
        panel.Children.Add(rejilla);

        trimestres.SelectedIndex = (previo.Item1.Desde.Month - 1) / 3;
        meses.SelectedIndex = previo.Item1.Desde.Month - 1;
        (previo.Item3 switch { 1 => porMes, 2 => porFechas, _ => porTrimestre }).IsChecked = true;
        trimestres.SelectionChanged += (_, _) => porTrimestre.IsChecked = true;
        meses.SelectionChanged += (_, _) => porMes.IsChecked = true;
        desde.SelectedDateChanged += (_, _) => porFechas.IsChecked = true;
        hasta.SelectedDateChanged += (_, _) => porFechas.IsChecked = true;

        panel.Children.Add(new TextBlock { Text = "Libros", Margin = new Thickness(0, 0, 0, 3) });
        var libros = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        var expedidas = new CheckBox { Content = "Facturas expedidas", IsChecked = previo.Item1.Expedidas, Margin = new Thickness(0, 0, 16, 0),
            ToolTip = "Ventas: una fila por factura y tipo de IVA con fecha, número, cliente, NIF, base, tipo, cuota y clave de exención; las rectificativas, marcadas" };
        var recibidas = new CheckBox { Content = "Facturas recibidas", IsChecked = previo.Item1.Recibidas,
            ToolTip = "Compras: una fila por factura y tipo de IVA con fechas, número, proveedor, NIF, operación (nacional, intracomunitaria, importación, ISP), " +
                      "base, tipo, cuota, cuota autorrepercutida y deducción" };
        libros.Children.Add(expedidas);
        libros.Children.Add(recibidas);
        panel.Children.Add(libros);

        panel.Children.Add(new TextBlock { Text = "Formato", Margin = new Thickness(0, 0, 0, 3) });
        var formatos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var pdf = new RadioButton { Content = "PDF", GroupName = "formato", IsChecked = previo.Item2 == FormatoLibrosIva.Pdf, Margin = new Thickness(0, 0, 16, 0),
            ToolTip = "Para imprimir, archivar o enviar a la gestoría: A4 apaisado, cada libro con sus totales y una página de resumen con el cuadre con el 303" };
        var excel = new RadioButton { Content = "Excel", GroupName = "formato", IsChecked = previo.Item2 == FormatoLibrosIva.Excel,
            ToolTip = "Para trabajar los datos: una hoja por libro con una tabla con filtros y fila de totales, y una hoja Resumen" };
        formatos.Children.Add(pdf);
        formatos.Children.Add(excel);
        panel.Children.Add(formatos);

        var aviso = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(aviso);
        d.Content = panel;

        int Modo() => porMes.IsChecked == true ? 1 : porFechas.IsChecked == true ? 2 : 0;
        ParametrosLibrosIva? Leer()
        {
            bool e = expedidas.IsChecked == true, r = recibidas.IsChecked == true;
            return Modo() switch
            {
                1 => ParametrosLibrosIva.Mes(ejercicio, Math.Max(0, meses.SelectedIndex) + 1, e, r),
                2 => desde.SelectedDate is { } a && hasta.SelectedDate is { } b ? new ParametrosLibrosIva(a, b, e, r) : null,
                _ => ParametrosLibrosIva.Trimestre(ejercicio, Math.Max(0, trimestres.SelectedIndex) + 1, e, r),
            };
        }

        var generar = new Button { Content = "Generar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Calcula los libros del periodo, los guarda en la carpeta de documentos de la empresa y los abre" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin generar nada" };
        generar.Click += (_, _) =>
        {
            if (Leer() is not { } p) { aviso.Text = "Falta " + (desde.SelectedDate is null ? "la fecha «Desde»." : "la fecha «Hasta».") + " Escríbela o elige trimestre o mes."; return; }
            if (p.Error(ejercicio) is { } error) { aviso.Text = error; return; }
            d.DialogResult = true;
        };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, generar } };
        if (d.ShowDialog() != true) return null;
        var formato = excel.IsChecked == true ? FormatoLibrosIva.Excel : FormatoLibrosIva.Pdf;
        var parametros = Leer()!;
        _ultimo = (parametros, formato, Modo());
        return (parametros, formato);
    }
}
