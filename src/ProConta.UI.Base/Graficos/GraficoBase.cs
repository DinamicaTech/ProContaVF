using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Base de los controles del motor de gráficos (informes/graficos): dibujo propio con <see cref="DrawingContext"/>, sin
/// bibliotecas externas; textos con la fuente de la biblioteca; tooltip flotante que sigue al ratón con el contenido que da cada
/// gráfico. La geometría y las escalas están en clases puras (<see cref="GeometriaGrafico"/>, <see cref="EscalasGrafico"/>).
/// </summary>
public abstract class GraficoBase : FrameworkElement
{
    private static readonly Dictionary<string, SolidColorBrush> Pinceles = new(StringComparer.OrdinalIgnoreCase);
    private readonly Popup _popup;
    private readonly StackPanel _contenidoTooltip = new();
    private int _indiceTooltip = -2;

    protected GraficoBase()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        _popup = new Popup
        {
            AllowsTransparency = true, Placement = PlacementMode.Relative, PlacementTarget = this, IsHitTestVisible = false, StaysOpen = true,
            Child = new Border
            {
                Background = Brushes.White, BorderBrush = Pincel("#E1DDD2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8, 10, 8), MinWidth = 170, MaxWidth = 320, Child = _contenidoTooltip,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Color.FromRgb(0x14, 0x21, 0x3A), Opacity = 0.16, BlurRadius = 24, ShadowDepth = 8, Direction = 270 },
            },
        };
        MouseMove += AlMoverRaton;
        MouseLeave += (_, _) => OcultarTooltip();
        IsVisibleChanged += (_, _) => { if (!IsVisible) OcultarTooltip(); };
    }

    /// <summary>Fuente de los textos: la de la biblioteca (IBM Plex Sans) si está cargada.</summary>
    protected Typeface Letra => new(TryFindResource("Fuente.Texto") as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    protected Typeface LetraNegrita => new(TryFindResource("Fuente.Texto") as FontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

    public static SolidColorBrush Pincel(string color)
    {
        lock (Pinceles)
        {
            if (Pinceles.TryGetValue(color, out var p)) return p;
            p = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            p.Freeze();
            Pinceles[color] = p;
            return p;
        }
    }

    protected static SolidColorBrush Tinta { get; } = Pincel("#16213A");
    protected static SolidColorBrush ApagadoPincel { get; } = Pincel("#646A78");
    protected static Pen LapizRejilla { get; } = Congelar(new Pen(Pincel(PaletaGraficos.Rejilla), 1));

    protected static Pen Congelar(Pen p)
    {
        p.Freeze();
        return p;
    }

    /// <summary>Texto preparado para dibujar con la fuente de la biblioteca.</summary>
    protected FormattedText Texto(string texto, double tamano = 11, Brush? color = null, bool negrita = false) =>
        new(texto, CultureInfo.GetCultureInfo("es-ES"), FlowDirection.LeftToRight, negrita ? LetraNegrita : Letra, tamano, color ?? ApagadoPincel,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Dibuja un texto alineado (−1 izquierda, 0 centro, 1 derecha) con su línea base aproximada en <paramref name="y"/>.</summary>
    protected void Escribir(DrawingContext dc, string texto, double x, double y, int alineacion = 0, double tamano = 11, Brush? color = null, bool negrita = false)
    {
        var t = Texto(texto, tamano, color, negrita);
        var dx = alineacion switch { < 0 => 0, 0 => -t.Width / 2, _ => -t.Width };
        dc.DrawText(t, new Point(x + dx, y - t.Baseline));
    }

    /// <summary>Fondo transparente para recibir el ratón en toda la superficie.</summary>
    protected void FondoSensible(DrawingContext dc) => dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));

    /// <summary>Índice del elemento bajo el ratón (categoría, tramo…), o −1 si no hay ninguno.</summary>
    protected abstract int IndiceEn(Point posicion);

    /// <summary>Contenido del tooltip del elemento <paramref name="indice"/>; null para no mostrar nada.</summary>
    protected abstract ContenidoTooltip? TooltipDe(int indice);

    /// <summary>Elemento resaltado por el ratón (−1 ninguno); el gráfico lo usa al dibujar.</summary>
    protected int Resaltado { get; private set; } = -1;

    private void AlMoverRaton(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        var i = IndiceEn(p);
        if (i != Resaltado)
        {
            Resaltado = i;
            InvalidateVisual();
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
                OcultarTooltip();
                return;
            }
            Rellenar(c);
        }
        _popup.HorizontalOffset = p.X + 14;
        _popup.VerticalOffset = p.Y + 14;
        _popup.IsOpen = true;
    }

    private void OcultarTooltip()
    {
        _popup.IsOpen = false;
        _indiceTooltip = -2;
        if (Resaltado != -1)
        {
            Resaltado = -1;
            InvalidateVisual();
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

    /// <summary>Pincel rayado (tramo de proyección) del color de la serie, como el patrón de la maqueta.</summary>
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

    /// <summary>Geometría de una barra con las esquinas superiores redondeadas (o las inferiores, si es negativa).</summary>
    protected static Geometry BarraRedondeada(Caja c, double radio, bool haciaArriba = true)
    {
        var r = Math.Min(radio, Math.Min(c.Ancho / 2, c.Alto));
        if (r <= 0.5) return new RectangleGeometry(new Rect(c.X, c.Y, c.Ancho, c.Alto));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            if (haciaArriba)
            {
                ctx.BeginFigure(new Point(c.X, c.Abajo), true, true);
                ctx.LineTo(new Point(c.X, c.Y + r), true, false);
                ctx.QuadraticBezierTo(new Point(c.X, c.Y), new Point(c.X + r, c.Y), true, false);
                ctx.LineTo(new Point(c.Derecha - r, c.Y), true, false);
                ctx.QuadraticBezierTo(new Point(c.Derecha, c.Y), new Point(c.Derecha, c.Y + r), true, false);
                ctx.LineTo(new Point(c.Derecha, c.Abajo), true, false);
            }
            else
            {
                ctx.BeginFigure(new Point(c.X, c.Y), true, true);
                ctx.LineTo(new Point(c.X, c.Abajo - r), true, false);
                ctx.QuadraticBezierTo(new Point(c.X, c.Abajo), new Point(c.X + r, c.Abajo), true, false);
                ctx.LineTo(new Point(c.Derecha - r, c.Abajo), true, false);
                ctx.QuadraticBezierTo(new Point(c.Derecha, c.Abajo), new Point(c.Derecha, c.Abajo - r), true, false);
                ctx.LineTo(new Point(c.Derecha, c.Y), true, false);
            }
        }
        g.Freeze();
        return g;
    }
}
