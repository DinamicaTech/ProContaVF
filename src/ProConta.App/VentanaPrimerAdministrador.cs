using System.Windows;
using System.Windows.Controls;
using ProConta.Seguridad.Permisos;

namespace ProConta.App;

/// <summary>Asistente del primer administrador: solo aparece si la BD no tiene ningún operador (P-03).</summary>
internal sealed class VentanaPrimerAdministrador : Window
{
    private readonly ServicioOperadores _operadores;
    private readonly TextBox _nombre = new() { Width = 280, ToolTip = "Nombre con el que entrará el administrador" };
    private readonly PasswordBox _clave = new() { Width = 280, ToolTip = $"Al menos {ServicioOperadores.LongitudMinimaContrasena} caracteres" };
    private readonly PasswordBox _repetida = new() { Width = 280, ToolTip = "Escribe otra vez la misma contraseña" };
    private readonly TextBlock _error = new() { Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Pincel.RojoError"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) };

    private VentanaPrimerAdministrador(ServicioOperadores operadores)
    {
        _operadores = operadores;
        Title = "ProContaVF · Primer administrador";
        Style = (Style)Application.Current.FindResource("Ventana.ProConta");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(new TextBlock { Text = "Crea el primer administrador", Style = (Style)FindResource("Texto.Titulo") });
        panel.Children.Add(new TextBlock { Text = "La base de datos no tiene ningún operador. El que crees ahora podrá dar de alta a los demás.", TextWrapping = TextWrapping.Wrap, MaxWidth = 380, Margin = new Thickness(0, 8, 0, 16), Foreground = (System.Windows.Media.Brush)FindResource("Pincel.Apagado") });
        Campo(panel, "Nombre", _nombre);
        Campo(panel, "Contraseña", _clave);
        Campo(panel, "Repite la contraseña", _repetida);
        panel.Children.Add(_error);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var salir = new Button { Content = "Salir", MinWidth = 90, IsCancel = true, ToolTip = "Cierra ProContaVF sin crear nada" };
        var crear = new Button { Content = "Crear y continuar", Margin = new Thickness(8, 0, 0, 0), IsDefault = true, Style = (Style)FindResource("Boton.Primario"), ToolTip = "Crea el administrador y pasa a la pantalla de acceso" };
        crear.Click += async (_, _) =>
        {
            if (_clave.Password != _repetida.Password) { _error.Text = "Las dos contraseñas no coinciden. Escríbelas otra vez."; return; }
            crear.IsEnabled = false;
            var r = await _operadores.CrearPrimerAdministradorAsync(_nombre.Text, _clave.Password);
            crear.IsEnabled = true;
            if (r.EsCorrecto) DialogResult = true;
            else _error.Text = App.Explicar(r.Error!);
        };
        botones.Children.Add(salir);
        botones.Children.Add(crear);
        panel.Children.Add(botones);
        Content = panel;
        Loaded += (_, _) => _nombre.Focus();
    }

    private void Campo(Panel panel, string etiqueta, Control control)
    {
        panel.Children.Add(new TextBlock { Text = etiqueta, Style = (Style)FindResource("Etiqueta.Campo") });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(control);
    }

    public static bool Crear(ServicioOperadores operadores) => new VentanaPrimerAdministrador(operadores).ShowDialog() == true;
}
