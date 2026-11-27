using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Coordinates = ScottPlot.Coordinates;
using Pixel = ScottPlot.Pixel;
using Plot = ScottPlot.Plot;
using ScottPlot.Interactivity.UserActions;
using ScottPlot.WPF;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Base de los controles de gráficos (informes/graficos): un gráfico de ScottPlot que dibuja cada control con el estilo común
/// (<see cref="EstiloPlano"/>), más el tooltip flotante propio que sigue al ratón con el contenido que da cada gráfico. El ratón lo recibe este
/// control, no ScottPlot: así los gráficos dentro de un botón no le quitan el clic y la rueda desplaza la pantalla salvo en los gráficos con zoom.
/// </summary>
public abstract class GraficoBase : ContentControl
{
    private static readonly Dictionary<string, SolidColorBrush> Pinceles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Popup _popup;
    private readonly StackPanel _contenidoTooltip = new();
    private int _indiceTooltip = -2;
    private bool _pendiente;

    protected GraficoBase()
    {
        Lienzo = new WpfPlot { IsHitTestVisible = false, Focusable = false };
        Lienzo.UserInputProcessor.UserActionResponses.Clear();
        Lienzo.Menu = null;
        // El ScottPlot no recibe el ratón (IsHitTestVisible = false) y la plantilla de ContentControl no pinta su fondo: sin este borde con fondo
        // transparente nada del gráfico recibía el ratón y no había tooltip, rueda ni arrastre.
        Content = new Border { Background = Brushes.Transparent, Child = Lienzo };
        Focusable = false;
        IsTabStop = false;
        Background = Brushes.Transparent;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        _popup = new Popup
        {
            AllowsTransparency = true, Placement = PlacementMode.Relative, PlacementTarget = this, IsHitTestVisible = false, StaysOpen = true,
            Child = new Border
            {
                Background = Brushes.White, BorderBrush = Pincel("#E1DDD2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8), MinWidth = 170, MaxWidth = 320, Child = _contenidoTooltip,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(0x14, 0x21, 0x3A), Opacity = 0.16, BlurRadius = 24, ShadowDepth = 8, Direction = 270 },
            },
        };
        Loaded += (_, _) => { AjustarEscala(); Redibujar(); };
        SizeChanged += (_, _) => Redibujar();
        MouseMove += AlMoverRaton;
        MouseLeave += (_, _) => OcultarTooltip();
        IsVisibleChanged += (_, _) =>
        {
            if (!IsVisible) OcultarTooltip();
            else if (_pendiente) Redibujar();
        };
    }

    /// <summary>El control de ScottPlot que dibuja el gráfico.</summary>
    protected WpfPlot Lienzo { get; }

    protected Plot Plot => Lienzo.Plot;

    /// <summary>El gráfico responde al ratón (tooltip y resaltado); si no, el ratón pasa al control de debajo (por ejemplo, un botón).</summary>
    protected bool Interactivo
    {
        get => IsHitTestVisible;
        set => IsHitTestVisible = value;
    }

    /// <summary>La rueda y el arrastre hacen zoom (la rueda ya no desplaza la pantalla sobre el gráfico).</summary>
    protected bool ConZoom { get; set; }

    public static SolidColorBrush Pincel(string color)
    {
        lock (Pinceles)
        {
            if (Pinceles.TryGetValue(color, out var p)) return p;
            p = new SolidColorBrush((System.Windows.Media.Color)ColorConverter.ConvertFromString(color));
            p.Freeze();
            Pinceles[color] = p;
            return p;
        }
    }

    protected static SolidColorBrush Tinta { get; } = Pincel("#16213A");

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        AjustarEscala();
        Redibujar();
    }

    private void AjustarEscala()
    {
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        Lienzo.DisplayScale = (float)dpi;
        Plot.ScaleFactor = dpi;
    }

    /// <summary>Vuelve a construir el gráfico con los datos y el tamaño actuales (si el control no se ve, en cuanto se vea).</summary>
    protected void Redibujar()
    {
        if (!IsLoaded || !IsVisible || ActualWidth < 1 || ActualHeight < 1)
        {
            _pendiente = true;
            return;
        }
        _pendiente = false;
        Construir(ActualWidth, ActualHeight);
        if (Resaltado >= 0) Resaltar(Resaltado);
        Lienzo.Refresh();
    }

    /// <summary>Construye el gráfico en <see cref="Plot"/> para un control de <paramref name="ancho"/> por <paramref name="alto"/> píxeles lógicos.</summary>
    protected abstract void Construir(double ancho, double alto);

    /// <summary>Índice del elemento bajo el ratón (categoría, día, tramo…), o −1; <paramref name="c"/> son las coordenadas de los datos.</summary>
    protected virtual int IndiceEn(Coordinates c, Point posicion) => -1;

    /// <summary>Contenido del tooltip del elemento <paramref name="indice"/>; null para no mostrar nada.</summary>
    protected virtual ContenidoTooltip? TooltipDe(int indice) => null;

    /// <summary>Dibuja el resaltado del elemento <paramref name="indice"/> (−1 lo quita), sin refrescar.</summary>
    protected virtual void Resaltar(int indice) { }

    /// <summary>Elemento resaltado por el ratón (−1 ninguno).</summary>
    protected int Resaltado { get; private set; } = -1;

    /// <summary>Píxel físico (el que usa ScottPlot) de una posición del ratón sobre el control.</summary>
    protected Pixel PixelDe(Point p)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Pixel((float)(p.X * dpi.DpiScaleX), (float)(p.Y * dpi.DpiScaleY));
    }

    private bool Dibujado => Plot.RenderManager.LastRender.DataRect.Width > 0;

    private void AlMoverRaton(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (ConZoom) Lienzo.UserInputProcessor.Process(new ScottPlot.Interactivity.UserActions.MouseMove(PixelDe(p)));
        var i = Dibujado && !IsMouseCaptured ? IndiceEn(Plot.GetCoordinates(PixelDe(p)), p) : -1;
        if (i != Resaltado)
        {
            Resaltado = i;
            Resaltar(i);
            Lienzo.Refresh();
        }
        if (i < 0)
        {
            OcultarTooltip();
            return;
        }
        if (i != _indiceTooltip)
        {
            _indiceTooltip = i;
            var c = TooltipDe(i);
            if (c is null)
            {
                _popup.IsOpen = false;
                return;
            }
            Rellenar(c);
        }
        _popup.HorizontalOffset = p.X + 14;
        _popup.VerticalOffset = p.Y + 14;
        _popup.IsOpen = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!ConZoom) return;
        OcultarTooltip();
        CaptureMouse();
        Lienzo.UserInputProcessor.Process(new LeftMouseDown(PixelDe(e.GetPosition(this))));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!ConZoom) return;
        Lienzo.UserInputProcessor.Process(new LeftMouseUp(PixelDe(e.GetPosition(this))));
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!ConZoom || !Dibujado) return;
        var px = PixelDe(e.GetPosition(this));
        Lienzo.UserInputProcessor.Process(e.Delta > 0 ? new MouseWheelUp(px) : new MouseWheelDown(px));
        OcultarTooltip();
        e.Handled = true;
    }

    private void OcultarTooltip()
    {
        _popup.IsOpen = false;
        _indiceTooltip = -2;
        if (Resaltado != -1)
        {
            Resaltado = -1;
            Resaltar(-1);
            Lienzo.Refresh();
        }
    }

    private void Rellenar(ContenidoTooltip c)
    {
        var familia = TryFindResource("Fuente.Texto") as FontFamily ?? new FontFamily("Segoe UI");
        _contenidoTooltip.Children.Clear();
        _contenidoTooltip.Children.Add(new TextBlock { Text = c.Titulo, FontFamily = familia, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Tinta, Margin = new Thickness(0, 0, 0, 3), TextWrapping = TextWrapping.Wrap });
        foreach (var f in c.Filas)
        {
            var fila = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 0, 1) };
            var valor = new TextBlock { Text = f.Valor, FontFamily = familia, FontSize = 12.5, Foreground = Tinta, Margin = new Thickness(12, 0, 0, 0), FontWeight = f.Destacada ? FontWeights.SemiBold : FontWeights.Normal };
            valor.SetValue(System.Windows.Documents.Typography.NumeralAlignmentProperty, FontNumeralAlignment.Tabular);
            DockPanel.SetDock(valor, Dock.Right);
            fila.Children.Add(valor);
            var etiqueta = new StackPanel { Orientation = Orientation.Horizontal };
            if (f.Color is { } color)
                etiqueta.Children.Add(new Border { Width = 9, Height = 9, CornerRadius = new CornerRadius(2), Background = Pincel(color), Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            etiqueta.Children.Add(new TextBlock { Text = f.Etiqueta, FontFamily = familia, FontSize = 12.5, Foreground = Pincel("#3B4252"), FontWeight = f.Destacada ? FontWeights.SemiBold : FontWeights.Normal });
            fila.Children.Add(etiqueta);
            _contenidoTooltip.Children.Add(fila);
        }
    }

    /// <summary>Pincel rayado (tramo de proyección) del color de la serie, como el patrón de la maqueta (leyendas fuera del gráfico).</summary>
    public static Brush Rayado(string color)
    {
        var c = Pincel(color);
        var g = new DrawingGroup();
        g.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 5, 5))));
        g.Children.Add(new GeometryDrawing(c, null, new RectangleGeometry(new Rect(0, 0, 2.2, 5))));
        var b = new DrawingBrush(g)
        {
            TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 5, 5), ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 5, 5), ViewboxUnits = BrushMappingMode.Absolute, Transform = new RotateTransform(135),
        };
        b.Freeze();
        return b;
    }
}
