using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ProConta.Compras.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Compras.UI.Vistas;

/// <summary>Diálogos de compras con los mensajes comunes de ProConta.UI.Base.</summary>
public sealed class DialogosCompras(Func<Window?> ventana) : IDialogosCompras
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(ventana(), titulo, mensaje, detalles, opciones);

    public void Abrir(string ruta)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.FileNotFoundException)
        {
            ServicioMensajes.Preguntar(ventana(), "No he podido abrirlo", $"Windows no ha podido abrir {ruta}: {ex.Message}", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
        }
    }

    public bool Copiar(string texto)
    {
        try
        {
            Clipboard.SetText(texto);
            return true;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return false;
        }
    }

    public (int Pagina, string Nombre)? PedirSeparacion(string fichero, string nombrePropuesto)
    {
        var dialogo = new DialogoProConta { Title = "Separar facturas", Owner = ventana(), Width = 560 };
        var pagina = new TextBox { Text = "2", Width = 80, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 4,
            ToolTip = "Primera página de la segunda factura; las anteriores se quedan en el fichero original" };
        var nombre = new TextBox { Text = nombrePropuesto, Width = 420, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = 200,
            ToolTip = "Nombre del fichero de la segunda factura, en la misma carpeta" };
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"{fichero} pasa a .BAK. La primera factura se queda con su nombre y en este mismo registro (se reconoce de nuevo y se conservan la cuenta de gasto, el centro de coste y las notas); la segunda se registra como factura nueva.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock { Text = "La segunda factura empieza en la página *", Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(pagina);
        panel.Children.Add(new TextBlock { Text = "Nombre del fichero de la segunda factura *", Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(nombre);
        panel.Children.Add(error);
        dialogo.Content = panel;
        var separar = new Button { Content = "Separar", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Crea los dos ficheros y deja el original como .BAK" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No cambia nada" };
        var n = 0;
        separar.Click += (_, _) =>
        {
            if (!int.TryParse(pagina.Text.Trim(), out n) || n < 2) { error.Text = "Escribe un número de página, de 2 en adelante."; error.Visibility = Visibility.Visible; pagina.Focus(); return; }
            if (string.IsNullOrWhiteSpace(nombre.Text)) { error.Text = "Escribe el nombre del fichero de la segunda factura."; error.Visibility = Visibility.Visible; nombre.Focus(); return; }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, separar } };
        dialogo.Loaded += (_, _) => { pagina.Focus(); pagina.SelectAll(); };
        return dialogo.ShowDialog() == true ? (n, nombre.Text.Trim()) : null;
    }
}
