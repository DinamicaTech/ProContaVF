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

    public ProConta.Contabilidad.Cuentas.CuentaContable? ElegirCuenta(ProConta.Contabilidad.UI.BuscadorCuentasVM buscador, string titulo) =>
        ProConta.Contabilidad.UI.DialogoCuentas.Elegir(ventana(), buscador, titulo);

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

    public bool PedirTextoClave(string? cuenta, string? centro, Func<string, bool, bool, Task<string?>> guardar)
    {
        var dialogo = new DialogoProConta { Title = "Añadir texto clave", Owner = ventana(), Width = 560 };
        var texto = new TextBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = ProConta.Compras.Facturas.ReglasTextosClave.LongitudMaxima,
            ToolTip = "Palabra o frase del PDF (o de su carpeta) que identifica el gasto; no distingue mayúsculas ni acentos" };
        var conCuenta = new CheckBox { Content = $"Asignar la cuenta de gasto {cuenta}", IsChecked = cuenta is not null, IsEnabled = cuenta is not null, Margin = new Thickness(0, 12, 0, 0),
            Visibility = cuenta is null ? Visibility.Collapsed : Visibility.Visible };
        var conCentro = new CheckBox { Content = $"Asignar el centro de coste {centro}", IsChecked = centro is not null, IsEnabled = centro is not null, Margin = new Thickness(0, 6, 0, 0),
            Visibility = centro is null ? Visibility.Collapsed : Visibility.Visible };
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "Al leer un PDF, si este texto aparece en su contenido o en su ruta, la factura toma lo que marques. Si coinciden varios textos, gana el más largo. No cambia esta factura.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock { Text = "Texto clave *", Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(texto);
        panel.Children.Add(conCuenta);
        panel.Children.Add(conCentro);
        panel.Children.Add(error);
        dialogo.Content = panel;
        var aceptar = new Button { Content = "Guardar", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Guarda el texto clave en la empresa" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No guarda nada" };
        void Error(string mensaje) { error.Text = mensaje; error.Visibility = Visibility.Visible; }
        aceptar.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(texto.Text)) { Error("Escribe el texto clave."); texto.Focus(); return; }
            if (conCuenta.IsChecked != true && conCentro.IsChecked != true) { Error("Marca la cuenta de gasto, el centro de coste o los dos."); return; }
            aceptar.IsEnabled = false;
            try
            {
                var fallo = await guardar(texto.Text.Trim(), conCuenta.IsChecked == true, conCentro.IsChecked == true);
                if (fallo is null) dialogo.DialogResult = true;
                else Error(fallo);
            }
            finally
            {
                aceptar.IsEnabled = true;
            }
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => texto.Focus();
        return dialogo.ShowDialog() == true;
    }

    public bool PedirDatosProveedor(ProConta.Compras.Proveedores.DatosAltaProveedor propuesta, string? motivo, string? urlEInforma,
        Func<ProConta.Compras.Proveedores.DatosAltaProveedor, Task<string?>> guardar)
    {
        var dialogo = new DialogoProConta { Title = "Dar de alta el proveedor", Owner = ventana(), Width = 600 };
        TextBox Campo(string texto, int max, string ayuda) => new() { Text = texto, MaxLength = max, HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = ayuda };
        var nif = Campo(propuesta.Nif, 20, "NIF de la factura; un NIF extranjero lleva delante el código del país (FR…, DE…)");
        var razon = Campo(propuesta.RazonSocial, 150, "Tal como figura en la factura o en el registro mercantil");
        var comercial = Campo(propuesta.NombreComercial ?? "", 150, "Opcional");
        var direccion = Campo(propuesta.Direccion ?? "", 255, "Opcional");
        var telefono = Campo(propuesta.Telefono ?? "", 30, "Opcional");
        var email = Campo(propuesta.Email ?? "", 254, "Opcional");
        var error = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0), Visibility = Visibility.Collapsed };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = (motivo is null ? "" : motivo + " ") + "Se crea como acreedor, con la siguiente subcuenta libre de la 410; la primera vez que exportes una factura suya podrás cambiarlo a proveedor (400).",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        void Fila(string etiqueta, UIElement campo)
        {
            panel.Children.Add(new TextBlock { Text = etiqueta, Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 8, 0, 4) });
            panel.Children.Add(campo);
        }
        Fila("NIF *", nif);
        if (urlEInforma is not null)
        {
            var fila = new DockPanel();
            var buscar = new Button { Content = "Buscar en eInforma", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Abre en el navegador la búsqueda del NIF en eInforma para copiar la razón social" };
            buscar.Click += (_, _) => Abrir(urlEInforma);
            DockPanel.SetDock(buscar, Dock.Right);
            fila.Children.Add(buscar);
            fila.Children.Add(razon);
            Fila("Razón social *", fila);
        }
        else Fila("Razón social *", razon);
        Fila("Nombre comercial", comercial);
        Fila("Dirección", direccion);
        Fila("Teléfono", telefono);
        Fila("Email", email);
        panel.Children.Add(error);
        dialogo.Content = panel;
        var aceptar = new Button { Content = "Dar de alta", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Crea el proveedor y su cuenta de acreedor" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No crea nada" };
        void Error(string mensaje) { error.Text = mensaje; error.Visibility = Visibility.Visible; }
        aceptar.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nif.Text)) { Error("Escribe el NIF."); nif.Focus(); return; }
            if (string.IsNullOrWhiteSpace(razon.Text)) { Error("Escribe la razón social."); razon.Focus(); return; }
            aceptar.IsEnabled = false;
            try
            {
                var fallo = await guardar(new ProConta.Compras.Proveedores.DatosAltaProveedor(nif.Text.Trim(), razon.Text.Trim(), comercial.Text, direccion.Text, telefono.Text, email.Text));
                if (fallo is null) dialogo.DialogResult = true;
                else Error(fallo);
            }
            finally
            {
                aceptar.IsEnabled = true;
            }
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => (string.IsNullOrWhiteSpace(razon.Text) ? razon : (UIElement)aceptar).Focus();
        return dialogo.ShowDialog() == true;
    }
}
