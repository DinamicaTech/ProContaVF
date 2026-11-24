using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Plataforma.Configuracion;

namespace ProConta.App;

/// <summary>
/// Paso antes del acceso (plataforma/entorno-pruebas): elegir el entorno. Mismo diseño que el acceso (franja de marca con
/// «Vas a trabajar en» y la lista a la derecha); una tarjeta por entorno, con su color, su etiqueta y la ruta de su BD.
/// Producción va elegida al abrir; se cambia con el ratón, las flechas o el número; Intro o doble clic entra y Esc sale. Sin marco, se
/// mueve arrastrando.
/// </summary>
public partial class VentanaEntorno : Window
{
    private sealed record Tono(Color Fuerte, Color Suave);

    private static readonly Dictionary<TonoEntorno, Tono> Tonos = new()
    {
        [TonoEntorno.Produccion] = new(Color.FromRgb(0x1E, 0x6B, 0x45), Color.FromRgb(0xE2, 0xF0, 0xE8)),
        [TonoEntorno.Pruebas] = new(Color.FromRgb(0xD9, 0x62, 0x2B), Color.FromRgb(0xFF, 0xF3, 0xE6)),
    };

    private readonly IReadOnlyList<EntradaEntorno> _entradas;
    private readonly List<Button> _tarjetas = [];
    private int _elegida;

    private VentanaEntorno(IReadOnlyList<EntradaEntorno> entradas)
    {
        InitializeComponent();
        _entradas = entradas;
        TextoVersion.Text = App.Version;
        for (var i = 0; i < entradas.Count; i++)
        {
            var indice = i;
            var tarjeta = Tarjeta(entradas[i], i + 1);
            tarjeta.Click += (_, _) => Elegir(indice);
            tarjeta.MouseDoubleClick += (_, _) => Entrar();
            _tarjetas.Add(tarjeta);
            ListaEntornos.Children.Add(tarjeta);
        }
        Elegir(0);
        Loaded += (_, _) => _tarjetas[_elegida].Focus();
    }

    public UbicacionDatos? Elegida { get; private set; }

    /// <summary>La ubicación elegida, o null si el operador sale.</summary>
    public static UbicacionDatos? Elegir(OpcionesEntorno opciones, InfoEntorno? pruebas)
    {
        var v = new VentanaEntorno(opciones.Entradas(pruebas));
        return v.ShowDialog() == true ? v.Elegida : null;
    }

    private Button Tarjeta(EntradaEntorno e, int numero)
    {
        var tono = Tonos[e.Tono];
        var fuerte = new SolidColorBrush(tono.Fuerte);
        var suave = new SolidColorBrush(tono.Suave);

        var icono = new Border
        {
            Width = 38, Height = 38, CornerRadius = new CornerRadius(8), Background = suave, Margin = new Thickness(0, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = e.Nombre.Length >= 2 && e.Tono == TonoEntorno.Pruebas ? e.Nombre[..2] : e.Nombre[..1],
                FontFamily = (FontFamily)FindResource("Fuente.Titulo"), FontWeight = FontWeights.Bold, FontSize = 14, Foreground = fuerte,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };

        var cabecera = new StackPanel { Orientation = Orientation.Horizontal };
        cabecera.Children.Add(new TextBlock { Text = e.Nombre, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Pincel.Tinta"), VerticalAlignment = VerticalAlignment.Center });
        cabecera.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(10), Background = suave, Padding = new Thickness(7, 1, 7, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = e.Etiqueta, FontSize = 11, FontWeight = FontWeights.Medium, Foreground = fuerte },
        });

        var textos = new StackPanel();
        textos.Children.Add(cabecera);
        textos.Children.Add(new TextBlock { Text = e.Descripcion, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Pincel.Texto2"), Margin = new Thickness(0, 3, 0, 0) });
        textos.Children.Add(new TextBlock
        {
            Text = e.Ubicacion.RutaBaseDatos, FontFamily = (FontFamily)FindResource("Fuente.Mono"), FontSize = 11, Foreground = (Brush)FindResource("Pincel.Apagado2"),
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0), ToolTip = e.Ubicacion.RutaBaseDatos,
        });

        var tecla = new Border
        {
            BorderBrush = (Brush)FindResource("Pincel.Linea3"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4),
            Background = (Brush)FindResource("Pincel.CampoFondo"), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = numero.ToString(), FontFamily = (FontFamily)FindResource("Fuente.Mono"), FontSize = 11, Foreground = (Brush)FindResource("Pincel.Apagado") },
            ToolTip = $"Pulsa {numero} para elegir {e.Nombre}",
        };

        var fila = new DockPanel();
        DockPanel.SetDock(icono, Dock.Left);
        DockPanel.SetDock(tecla, Dock.Right);
        fila.Children.Add(icono);
        fila.Children.Add(tecla);
        fila.Children.Add(textos);

        return new Button
        {
            Style = (Style)FindResource("Tarjeta.Entorno"),
            Content = fila,
            Tag = suave,
            ToolTip = e.Tono == TonoEntorno.Produccion
                ? "Entra en la base de datos de producción, la contabilidad de verdad"
                : $"Entra en {e.Nombre.ToLowerInvariant()}: puedes probar lo que quieras sin tocar la contabilidad real",
        };
    }

    private void Elegir(int indice)
    {
        _elegida = indice;
        for (var i = 0; i < _tarjetas.Count; i++)
        {
            var activa = i == indice;
            _tarjetas[i].IsDefault = activa;
            _tarjetas[i].BorderBrush = activa ? new SolidColorBrush(Tonos[_entradas[i].Tono].Fuerte) : (Brush)FindResource("Pincel.Linea");
        }
        var e = _entradas[indice];
        var color = new SolidColorBrush(Tonos[e.Tono].Fuerte);
        PuntoResumen.Fill = e.Tono == TonoEntorno.Produccion ? (Brush)FindResource("Pincel.Acento") : color;
        CuadroColor.Fill = PuntoResumen.Fill;
        ResumenEntorno.Text = e.Nombre;
        ResumenRuta.Text = e.Ubicacion.RutaBaseDatos;
        ResumenRuta.ToolTip = e.Ubicacion.RutaBaseDatos;
        BotonEntrar.Content = $"Entrar en {e.Nombre}";
        BotonEntrar.ToolTip = $"Sigue al acceso de operador en {e.Nombre} (Intro)";
        _tarjetas[indice].BringIntoView();
    }

    private void Entrar()
    {
        Elegida = _entradas[_elegida].Ubicacion;
        DialogResult = true;
    }

    private void Entrar_Click(object sender, RoutedEventArgs e) => Entrar();

    private void Salir_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Ventana_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                DialogResult = false;
                break;
            case Key.Enter:
                Entrar();
                break;
            case Key.Down or Key.Right:
                Mover(_elegida + 1);
                break;
            case Key.Up or Key.Left:
                Mover(_elegida - 1);
                break;
            case >= Key.D1 and <= Key.D9 when e.Key - Key.D1 < _entradas.Count:
                Mover(e.Key - Key.D1);
                break;
            case >= Key.NumPad1 and <= Key.NumPad9 when e.Key - Key.NumPad1 < _entradas.Count:
                Mover(e.Key - Key.NumPad1);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    private void Mover(int indice)
    {
        if (indice < 0 || indice >= _entradas.Count) return;
        Elegir(indice);
        _tarjetas[indice].Focus();
    }

    private void Ventana_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Solo se arrastra desde el fondo: un clic en una tarjeta o un botón es para elegir o pulsar.
        for (var d = e.OriginalSource as DependencyObject; d is not null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase or ScrollBar) return;
        if (e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }
}
