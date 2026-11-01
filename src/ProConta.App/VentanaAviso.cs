using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.Nucleo.Resultados;

namespace ProConta.App;

/// <summary>
/// Aviso que bloquea el arranque: dice qué ha pasado, por qué y qué hacer, con el detalle técnico
/// copiable aparte y, cuando se puede, el botón que lo corrige.
/// </summary>
internal sealed class VentanaAviso : Window
{
    private VentanaAviso(string titulo, Error error, string? accion)
    {
        Title = "ProContaVF · " + titulo;
        Style = (Style)Application.Current.FindResource("Ventana.ProConta");
        SizeToContent = SizeToContent.Height;
        Width = 560;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = titulo, Style = (Style)FindResource("Texto.Titulo"), Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock { Text = App.Explicar(error), TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        if (!string.IsNullOrWhiteSpace(error.DetalleTecnico))
            panel.Children.Add(new Expander
            {
                Header = "Detalle técnico",
                Margin = new Thickness(0, 12, 0, 0),
                ToolTip = "Información para el responsable técnico; se puede seleccionar y copiar",
                Content = new TextBox { Text = error.DetalleTecnico, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas") }
            });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var salir = new Button { Content = "Salir", MinWidth = 90, IsCancel = true, ToolTip = "Cierra ProContaVF sin cambiar nada" };
        salir.Click += (_, _) => { DialogResult = false; };
        botones.Children.Add(salir);
        if (accion is not null)
        {
            var corregir = new Button { Content = accion, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, Style = (Style)FindResource("Boton.Primario"), ToolTip = "Elige el fichero accdb de ProContaVF en el servidor" };
            corregir.Click += (_, _) => { DialogResult = true; };
            botones.Children.Add(corregir);
        }
        panel.Children.Add(botones);
        Content = panel;
    }

    public static void Mostrar(string titulo, Error error) => new VentanaAviso(titulo, error, null).ShowDialog();

    /// <summary>true si el operador elige la acción que corrige el problema.</summary>
    public static bool Preguntar(string titulo, Error error, string accion) => new VentanaAviso(titulo, error, accion).ShowDialog() == true;
}
