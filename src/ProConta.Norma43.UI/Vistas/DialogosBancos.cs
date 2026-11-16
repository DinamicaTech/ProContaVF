using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.UI.Vistas;

/// <summary>Diálogos de la pantalla Bancos sobre la ventana activa de la aplicación.</summary>
public sealed class DialogosBancos(Func<Window?> ventana) : IDialogosBancos
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(Activa(), titulo, mensaje, detalles, opciones);

    public string? ElegirFichero()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Importar un extracto Norma 43",
            Filter = "Extractos Norma 43 (*.n43;*.q43;*.aeb;*.csb;*.csb43;*.txt;*.dat)|*.n43;*.q43;*.aeb;*.csb;*.csb43;*.txt;*.dat|Todos los ficheros (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialogo.ShowDialog(Activa()) == true ? dialogo.FileName : null;
    }

    public void AbrirArchivo(string ruta)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{ruta}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Preguntar("No he podido abrir el fichero", $"Windows no ha podido abrir {System.IO.Path.GetFileName(ruta)}: {ex.Message}", [ruta],
                new OpcionMensaje("Entendido", "Ábrelo desde el Explorador de Windows con el Bloc de notas", Principal: true, Cancelar: true));
        }
    }

    public string? PedirTexto(string titulo, string explicacion, string etiqueta, string textoBoton)
    {
        var dialogo = new DialogoProConta { Title = titulo, Owner = Activa(), Width = 560 };
        var caja = new TextBox { MaxLength = 500, Style = (Style)dialogo.FindResource("Campo"), ToolTip = "Queda anotado en el lote con tu nombre y la hora" };
        var error = new TextBlock { Foreground = (System.Windows.Media.Brush)dialogo.FindResource("Pincel.RojoError"), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = etiqueta, Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(caja);
        panel.Children.Add(error);
        dialogo.Content = panel;
        var aceptar = new Button { Content = textoBoton, Style = (Style)dialogo.FindResource("Boton.Peligro"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, ToolTip = titulo };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No cambia nada" };
        aceptar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(caja.Text)) { error.Text = "Escribe el motivo."; error.Visibility = Visibility.Visible; caja.Focus(); return; }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => caja.Focus();
        return dialogo.ShowDialog() == true ? caja.Text.Trim() : null;
    }

    /// <summary>
    /// Alta de las cuentas bancarias desconocidas del fichero: la cuenta (fija, la del fichero), el nombre, el IBAN propuesto y,
    /// opcionalmente, su 572 del ejercicio. Aceptar da de alta las cuentas e importa el fichero en la misma operación.
    /// </summary>
    public bool DarDeAlta(IReadOnlyList<AltaCuentaVM> altas)
    {
        var dialogo = new DialogoProConta { Title = altas.Count == 1 ? "Cuenta bancaria nueva" : "Cuentas bancarias nuevas", Owner = Activa(), Width = 640 };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = (altas.Count == 1 ? "El fichero trae una cuenta que no está dada de alta en la sociedad." : $"El fichero trae {altas.Count} cuentas que no están dadas de alta en la sociedad.") +
                " Revisa sus datos y pulsa «Dar de alta e importar» para continuar; si cancelas, no se importa nada.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12),
        });
        var error = new TextBlock { Foreground = (System.Windows.Media.Brush)dialogo.FindResource("Pincel.RojoError"), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed, TextWrapping = TextWrapping.Wrap };
        TextBox? primero = null;
        foreach (var a in altas)
        {
            var tarjeta = new Border { Style = (Style)dialogo.FindResource("Tarjeta"), Padding = new Thickness(14, 12, 14, 12), Margin = new Thickness(0, 0, 0, 10), DataContext = a };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            void Fila(string etiqueta, UIElement control, string ayuda)
            {
                var fila = g.RowDefinitions.Count;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var e = new TextBlock { Text = etiqueta, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), ToolTip = ayuda };
                Grid.SetRow(e, fila);
                Grid.SetRow(control, fila);
                Grid.SetColumn(control, 1);
                if (control is FrameworkElement fe) { fe.Margin = new Thickness(0, 4, 0, 4); fe.ToolTip = ayuda; }
                g.Children.Add(e);
                g.Children.Add(control);
            }
            Fila("Cuenta", new TextBlock { Text = $"{a.Ccc} · {a.Divisa}", Style = (Style)dialogo.FindResource("Texto.Mono"), VerticalAlignment = VerticalAlignment.Center },
                "Entidad, oficina y cuenta tal como vienen en el fichero; no se pueden cambiar");
            var nombre = new TextBox { MaxLength = 150, Style = (Style)dialogo.FindResource("Campo") };
            nombre.SetBinding(TextBox.TextProperty, new Binding(nameof(AltaCuentaVM.Nombre)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            Fila("Nombre", nombre, "Cómo verás esta cuenta en Bancos (por ejemplo, el banco y el uso de la cuenta)");
            primero ??= nombre;
            var iban = new TextBox { MaxLength = 34, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)dialogo.FindResource("Campo.Mono") };
            iban.SetBinding(TextBox.TextProperty, new Binding(nameof(AltaCuentaVM.Iban)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            Fila("IBAN", iban, "Calculado desde la cuenta del fichero; compruébalo con el banco si quieres");
            var combo = new ComboBox { DisplayMemberPath = nameof(Importacion.CuentaContableBanco.Texto), ItemsSource = a.Opciones, Width = 360, HorizontalAlignment = HorizontalAlignment.Left };
            combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(AltaCuentaVM.Contable)));
            Fila("Cuenta 572", a.Opciones.Count == 0
                    ? new TextBlock { Text = "El ejercicio no tiene cuentas 572: podrás asociarla más adelante.", Style = (Style)dialogo.FindResource("Texto.Apagado"), TextWrapping = TextWrapping.Wrap }
                    : combo,
                "Cuenta contable del banco en este ejercicio (opcional): la usará la contabilización de las líneas y el saldo contable de Bancos");
            tarjeta.Child = g;
            panel.Children.Add(tarjeta);
        }
        panel.Children.Add(error);
        dialogo.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 };
        var aceptar = new Button { Content = "Dar de alta e importar", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Da de alta las cuentas con estos datos e importa el fichero en la misma operación" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No da de alta nada ni importa el fichero" };
        aceptar.Click += (_, _) =>
        {
            if (altas.FirstOrDefault(a => string.IsNullOrWhiteSpace(a.Nombre)) is { } sin)
            {
                error.Text = $"Escribe el nombre de la cuenta {sin.Ccc}.";
                error.Visibility = Visibility.Visible;
                return;
            }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => primero?.Focus();
        return dialogo.ShowDialog() == true;
    }

    private Window? Activa() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? ventana();
}
