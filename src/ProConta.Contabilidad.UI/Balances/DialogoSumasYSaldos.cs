using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.Contabilidad.Balances;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.Balances;

/// <summary>Formato de salida del balance de sumas y saldos.</summary>
public enum FormatoSumasYSaldos { Pdf, Excel }

/// <summary>
/// Diálogo de Informes › Sumas y saldos… (contabilidad/balances/sumas-y-saldos): fechas del ejercicio de trabajo, niveles, si entra el asiento
/// de cierre y el formato. Valida antes de cerrar y dice qué corregir. Recuerda lo último elegido mientras el programa está abierto.
/// </summary>
public static class DialogoSumasYSaldos
{
    private static (ParametrosSumasYSaldos Parametros, FormatoSumasYSaldos Formato)? _ultimo;

    public static (ParametrosSumasYSaldos Parametros, FormatoSumasYSaldos Formato)? Pedir(Window? propietario, int ejercicio, string razonSocial, DateTime hoy)
    {
        var previo = _ultimo is { } u && u.Parametros.Desde.Year == ejercicio ? u : (ParametrosSumasYSaldos.PorDefecto(ejercicio, hoy), FormatoSumasYSaldos.Pdf);
        var d = new DialogoProConta { Title = "Balance de sumas y saldos", Owner = propietario, Width = 520, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{razonSocial} · ejercicio {ejercicio}. Sumas del Debe y del Haber y saldo de cada cuenta con los apuntes contabilizados entre las dos fechas. " +
                   "El fichero se guarda en la carpeta de documentos de la empresa (Informes del año), vinculado al ejercicio, y se abre.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });

        var fechas = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
        DatePicker Fecha(string etiqueta, DateTime valor, string ayuda)
        {
            var bloque = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            bloque.Children.Add(new TextBlock { Text = etiqueta, Margin = new Thickness(0, 0, 0, 3) });
            var dp = new DatePicker { SelectedDate = valor, Width = 140, ToolTip = ayuda,
                DisplayDateStart = new DateTime(ejercicio, 1, 1), DisplayDateEnd = new DateTime(ejercicio, 12, 31) };
            bloque.Children.Add(dp);
            fechas.Children.Add(bloque);
            return dp;
        }
        var desde = Fecha("Desde", previo.Item1.Desde, $"Primer día que entra (fecha contable del asiento). El 01/01/{ejercicio} incluye la apertura");
        var hasta = Fecha("Hasta", previo.Item1.Hasta, "Último día que entra, incluido");
        panel.Children.Add(fechas);

        panel.Children.Add(new TextBlock { Text = "Niveles", Margin = new Thickness(0, 0, 0, 3) });
        var niveles = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        CheckBox Nivel(NivelSumasYSaldos n, string texto, string ayuda)
        {
            var c = new CheckBox { Content = texto, IsChecked = previo.Item1.Niveles.Contains(n), Margin = new Thickness(0, 0, 16, 0), ToolTip = ayuda, Tag = n };
            niveles.Children.Add(c);
            return c;
        }
        var casillas = new[]
        {
            Nivel(NivelSumasYSaldos.Grupo, "Grupo", "Una fila por grupo del plan (1 dígito: 1 Financiación básica, 2 Activo no corriente…), en negrita"),
            Nivel(NivelSumasYSaldos.Subgrupo, "Subgrupo", "Una fila por subgrupo (2 dígitos: 43 Clientes, 57 Tesorería…), en negrita"),
            Nivel(NivelSumasYSaldos.Cuenta, "Cuenta", "Una fila por cuenta de 3 dígitos (430 Clientes, 572 Bancos…), con la suma de sus subcuentas"),
            Nivel(NivelSumasYSaldos.Subcuenta, "Subcuenta", "Una fila por subcuenta con apuntes (4300001…); los totales suman el nivel más detallado que marques"),
        };
        panel.Children.Add(niveles);

        var cierre = new CheckBox
        {
            Content = "Incluir el asiento de cierre", IsChecked = previo.Item1.IncluirCierre, Margin = new Thickness(0, 0, 0, 12),
            ToolTip = "Normalmente no: con el cierre todos los saldos quedan a cero. La apertura y la regularización entran siempre que caigan en las fechas",
        };
        panel.Children.Add(cierre);

        panel.Children.Add(new TextBlock { Text = "Formato", Margin = new Thickness(0, 0, 0, 3) });
        var formatos = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var pdf = new RadioButton { Content = "PDF", GroupName = "formato", IsChecked = previo.Item2 == FormatoSumasYSaldos.Pdf, Margin = new Thickness(0, 0, 16, 0),
            ToolTip = "Para imprimir o enviar a la gestoría: A4 vertical, con los niveles en negrita y los totales al final" };
        var excel = new RadioButton { Content = "Excel", GroupName = "formato", IsChecked = previo.Item2 == FormatoSumasYSaldos.Excel,
            ToolTip = "Para trabajar los datos: una tabla con filtros (con una columna Nivel para filtrar) y los totales debajo" };
        formatos.Children.Add(pdf);
        formatos.Children.Add(excel);
        panel.Children.Add(formatos);

        var aviso = new TextBlock { Foreground = Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(aviso);
        d.Content = panel;

        ParametrosSumasYSaldos Leer() => new(
            desde.SelectedDate ?? DateTime.MinValue, hasta.SelectedDate ?? DateTime.MinValue,
            casillas.Where(c => c.IsChecked == true).Select(c => (NivelSumasYSaldos)c.Tag).ToList(), cierre.IsChecked == true);

        var generar = new Button { Content = "Generar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Calcula el balance, lo guarda en la carpeta de documentos de la empresa y lo abre" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin generar nada" };
        generar.Click += (_, _) =>
        {
            if (desde.SelectedDate is null || hasta.SelectedDate is null) { aviso.Text = "Falta " + (desde.SelectedDate is null ? "la fecha «Desde»." : "la fecha «Hasta»."); return; }
            if (Leer().Error(ejercicio) is { } error) { aviso.Text = error; return; }
            d.DialogResult = true;
        };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, generar } };
        if (d.ShowDialog() != true) return null;
        var resultado = (Leer(), excel.IsChecked == true ? FormatoSumasYSaldos.Excel : FormatoSumasYSaldos.Pdf);
        _ultimo = resultado;
        return resultado;
    }
}
