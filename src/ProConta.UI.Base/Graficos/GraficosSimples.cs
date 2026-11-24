using System.Windows;
using Coordinates = ScottPlot.Coordinates;

namespace ProConta.UI.Base.Graficos;

/// <summary>Base de los gráficos pequeños sin ejes: cualquier cambio de una propiedad vuelve a dibujar.</summary>
public abstract class GraficoPequeno : GraficoBase
{
    protected static DependencyProperty Registrar<T>(string nombre, Type tipo, object? defecto) where T : GraficoPequeno =>
        DependencyProperty.Register(nombre, tipo, typeof(T), new PropertyMetadata(defecto, (d, _) => ((GraficoPequeno)d).Redibujar()));

    /// <summary>Alto cuando el contenedor no lo fija.</summary>
    protected abstract double AltoPorDefecto { get; }

    protected override Size MeasureOverride(Size disponible)
    {
        base.MeasureOverride(disponible);
        return new(double.IsInfinity(disponible.Width) ? 120 : disponible.Width, double.IsInfinity(disponible.Height) ? AltoPorDefecto : Math.Min(disponible.Height, AltoPorDefecto));
    }
}

/// <summary>
/// Minigráfico (informes/graficos): la evolución de un indicador en una línea fina, sin ejes, con un punto en el último valor. Los nulos cortan la
/// línea. No recibe el ratón: su tooltip y su clic son los del control que lo contiene.
/// </summary>
public sealed class Minigrafico : GraficoPequeno
{
    public static readonly DependencyProperty ValoresProperty = Registrar<Minigrafico>(nameof(Valores), typeof(IReadOnlyList<decimal?>), null);
    public static readonly DependencyProperty ColorProperty = Registrar<Minigrafico>(nameof(Color), typeof(string), "#1D5C8C");

    public Minigrafico() => Interactivo = false;

    public IReadOnlyList<decimal?>? Valores { get => (IReadOnlyList<decimal?>?)GetValue(ValoresProperty); set => SetValue(ValoresProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    protected override double AltoPorDefecto => 30;

    protected override void Construir(double ancho, double alto) => PlanoSimples.Minigrafico(Plot, Valores, Color, ancho, alto);
}

/// <summary>
/// Barra apilada horizontal (informes/graficos), por ejemplo la antigüedad de lo pendiente de cobro: un segmento por tramo, proporcional a su
/// importe, con su color; al pasar el ratón, el tooltip del tramo con su importe y su peso.
/// </summary>
public sealed class BarraApilada : GraficoPequeno
{
    public static readonly DependencyProperty TramosProperty = Registrar<BarraApilada>(nameof(Tramos), typeof(IReadOnlyList<TramoApilado>), null);

    public IReadOnlyList<TramoApilado>? Tramos { get => (IReadOnlyList<TramoApilado>?)GetValue(TramosProperty); set => SetValue(TramosProperty, value); }

    public Func<decimal, string> FormatoValor { get; set; } = v => Formato.Formatos.Importe(Math.Round(v, 2));

    protected override double AltoPorDefecto => 22;

    protected override void Construir(double ancho, double alto) => PlanoSimples.Apilada(Plot, Tramos, ancho, alto, Resaltado);

    protected override void Resaltar(int indice)
    {
        if (ActualWidth >= 1) PlanoSimples.Apilada(Plot, Tramos, ActualWidth, ActualHeight, indice);
    }

    protected override int IndiceEn(Coordinates c, Point posicion) => PlanoSimples.TramoEn(Tramos, ActualWidth, posicion.X);

    protected override ContenidoTooltip? TooltipDe(int indice)
    {
        if (Tramos is not { } tramos || indice >= tramos.Count) return null;
        var t = tramos[indice];
        var total = tramos.Where(x => x.Valor > 0).Sum(x => x.Valor);
        var filas = new List<FilaTooltip> { new("Importe", FormatoValor(t.Valor), t.Color) };
        if (total > 0) filas.Add(new FilaTooltip("Peso", (t.Valor / total * 100m).ToString("0.0", Formato.Formatos.Cultura) + " %"));
        if (t.Ayuda is { } a) filas.Add(new FilaTooltip(a, ""));
        return new ContenidoTooltip(t.Etiqueta, filas);
    }
}

/// <summary>
/// Barra horizontal con marca de referencia (informes/graficos): la pista gris, la barra del valor en su color y, si hay referencia (por ejemplo el
/// ejercicio comparado), una raya oscura en su posición. Valor y referencia se miden contra <see cref="Maximo"/>, el mismo para todas las filas de
/// una lista. No recibe el ratón.
/// </summary>
public sealed class BarraReferencia : GraficoPequeno
{
    public static readonly DependencyProperty ValorProperty = Registrar<BarraReferencia>(nameof(Valor), typeof(decimal), 0m);
    public static readonly DependencyProperty ReferenciaProperty = Registrar<BarraReferencia>(nameof(Referencia), typeof(decimal?), null);
    public static readonly DependencyProperty MaximoProperty = Registrar<BarraReferencia>(nameof(Maximo), typeof(decimal), 0m);
    public static readonly DependencyProperty ColorProperty = Registrar<BarraReferencia>(nameof(Color), typeof(string), PaletaGraficos.Serie1);

    public BarraReferencia() => Interactivo = false;

    public decimal Valor { get => (decimal)GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
    public decimal? Referencia { get => (decimal?)GetValue(ReferenciaProperty); set => SetValue(ReferenciaProperty, value); }
    public decimal Maximo { get => (decimal)GetValue(MaximoProperty); set => SetValue(MaximoProperty, value); }
    public string Color { get => (string)GetValue(ColorProperty); set => SetValue(ColorProperty, value); }

    protected override double AltoPorDefecto => 8;

    protected override void Construir(double ancho, double alto) => PlanoSimples.Referencia(Plot, Valor, Referencia, Maximo, Color, ancho, alto);
}

/// <summary>
/// Franja de rango de un ratio (informes/graficos): la pista gris, la banda verde del rango de referencia, una marca oscura en el valor actual y una
/// raya gris en el del ejercicio comparado. Las posiciones las calcula <see cref="GeometriaGrafico.Franja"/>. No recibe el ratón: su tooltip es el
/// del control que lo contiene.
/// </summary>
public sealed class FranjaRango : GraficoPequeno
{
    public static readonly DependencyProperty ValorProperty = Registrar<FranjaRango>(nameof(Valor), typeof(decimal?), null);
    public static readonly DependencyProperty ReferenciaProperty = Registrar<FranjaRango>(nameof(Referencia), typeof(decimal?), null);
    public static readonly DependencyProperty MinimoProperty = Registrar<FranjaRango>(nameof(Minimo), typeof(decimal?), null);
    public static readonly DependencyProperty MaximoProperty = Registrar<FranjaRango>(nameof(Maximo), typeof(decimal?), null);
    public static readonly DependencyProperty MenosEsMejorProperty = Registrar<FranjaRango>(nameof(MenosEsMejor), typeof(bool), false);

    public FranjaRango() => Interactivo = false;

    public decimal? Valor { get => (decimal?)GetValue(ValorProperty); set => SetValue(ValorProperty, value); }
    public decimal? Referencia { get => (decimal?)GetValue(ReferenciaProperty); set => SetValue(ReferenciaProperty, value); }
    public decimal? Minimo { get => (decimal?)GetValue(MinimoProperty); set => SetValue(MinimoProperty, value); }
    public decimal? Maximo { get => (decimal?)GetValue(MaximoProperty); set => SetValue(MaximoProperty, value); }
    public bool MenosEsMejor { get => (bool)GetValue(MenosEsMejorProperty); set => SetValue(MenosEsMejorProperty, value); }

    protected override double AltoPorDefecto => 14;

    protected override void Construir(double ancho, double alto) =>
        PlanoSimples.Franja(Plot, Valor, Referencia, Minimo, Maximo, MenosEsMejor, ancho, alto);
}
