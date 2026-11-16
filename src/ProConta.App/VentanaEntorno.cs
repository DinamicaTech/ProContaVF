using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.Plataforma.Configuracion;

namespace ProConta.App;

/// <summary>
/// Paso antes del acceso (plataforma/entorno-pruebas): elegir entre producción y la copia de pruebas. Producción es la opción
/// por defecto (Intro); pruebas va en naranja, el color que lleva todo el entorno de pruebas.
/// </summary>
internal sealed class VentanaEntorno : Window
{
    public static readonly Color Naranja = Color.FromRgb(0xE0, 0x6A, 0x00);

    private VentanaEntorno(OpcionesEntorno opciones, InfoEntorno? pruebas)
    {
        Title = "ProContaVF · Elige el entorno";
        Style = (Style)Application.Current.FindResource("Ventana.ProConta");
        SizeToContent = SizeToContent.Height;
        Width = 520;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "¿En qué entorno quieres trabajar?", Style = (Style)FindResource("Texto.Titulo"), Margin = new Thickness(0, 0, 0, 16) });

        var produccion = Opcion("Producción", "La contabilidad real.", opciones.Produccion.RutaBaseDatos, null);
        produccion.IsDefault = true;
        produccion.ToolTip = "Entra en la base de datos de producción, la de verdad (Intro)";
        produccion.Click += (_, _) => { Elegida = opciones.Produccion; DialogResult = true; };
        panel.Children.Add(produccion);

        var copia = pruebas?.CopiaFecha is DateTime f ? $"Copia de producción del {f:dd/MM/yyyy HH:mm}. " : "";
        var botonPruebas = Opcion("Pruebas", copia + "Lo que hagas aquí no llega a producción.", opciones.Pruebas!.RutaBaseDatos, Naranja);
        botonPruebas.ToolTip = "Entra en la copia de pruebas: puedes probar lo que quieras sin tocar la contabilidad real";
        botonPruebas.Click += (_, _) => { Elegida = opciones.Pruebas; DialogResult = true; };
        panel.Children.Add(botonPruebas);

        var salir = new Button { Content = "Salir", MinWidth = 90, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0), ToolTip = "Cierra ProContaVF sin entrar" };
        salir.Click += (_, _) => DialogResult = false;
        panel.Children.Add(salir);
        Content = panel;
        Loaded += (_, _) => produccion.Focus();
    }

    public UbicacionDatos? Elegida { get; private set; }

    private static Button Opcion(string titulo, string texto, string ruta, Color? color)
    {
        var contenido = new StackPanel { Margin = new Thickness(6, 4, 6, 4) };
        contenido.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.Bold, FontSize = 16, Foreground = color is { } c ? new SolidColorBrush(c) : null });
        contenido.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        contenido.Children.Add(new TextBlock { Text = ruta, TextWrapping = TextWrapping.Wrap, Opacity = 0.65, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
        var boton = new Button
        {
            Content = contenido,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 10),
            Padding = new Thickness(8),
        };
        if (color is { } b)
        {
            boton.BorderThickness = new Thickness(2);
            boton.BorderBrush = new SolidColorBrush(b);
        }
        return boton;
    }

    /// <summary>La ubicación elegida, o null si el operador sale.</summary>
    public static UbicacionDatos? Elegir(OpcionesEntorno opciones, InfoEntorno? pruebas)
    {
        var v = new VentanaEntorno(opciones, pruebas);
        return v.ShowDialog() == true ? v.Elegida : null;
    }
}
