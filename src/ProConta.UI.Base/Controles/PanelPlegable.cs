using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Preferencias;

namespace ProConta.UI.Base.Controles;

/// <summary>
/// Panel lateral plegable con separador arrastrable (como el de recientes del Mayor o la lista de Compras).
/// Arrastrar el separador cambia el ancho; doble clic vuelve al de fábrica; con el separador enfocado, ← → ajustan e Inicio restablece.
/// Ancho y plegado se guardan por operador (<see cref="ClavePreferencias"/>).
/// </summary>
[TemplatePart(Name = "PART_Separador", Type = typeof(Thumb))]
public class PanelPlegable : ContentControl
{
    static PanelPlegable()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(PanelPlegable), new FrameworkPropertyMetadata(typeof(PanelPlegable)));
    }

    public static readonly DependencyProperty TituloProperty = DependencyProperty.Register(nameof(Titulo), typeof(string), typeof(PanelPlegable), new PropertyMetadata(""));
    public static readonly DependencyProperty AccionesProperty = DependencyProperty.Register(nameof(Acciones), typeof(object), typeof(PanelPlegable), new PropertyMetadata(null));
    public static readonly DependencyProperty PieProperty = DependencyProperty.Register(nameof(Pie), typeof(object), typeof(PanelPlegable), new PropertyMetadata(null));
    public static readonly DependencyProperty AnchoFabricaProperty = DependencyProperty.Register(nameof(AnchoFabrica), typeof(double), typeof(PanelPlegable), new PropertyMetadata(252.0));
    public static readonly DependencyProperty AnchoMinimoProperty = DependencyProperty.Register(nameof(AnchoMinimo), typeof(double), typeof(PanelPlegable), new PropertyMetadata(180.0));
    public static readonly DependencyProperty AnchoMaximoProperty = DependencyProperty.Register(nameof(AnchoMaximo), typeof(double), typeof(PanelPlegable), new PropertyMetadata(440.0));
    public static readonly DependencyProperty AnchoPanelProperty = DependencyProperty.Register(nameof(AnchoPanel), typeof(double), typeof(PanelPlegable), new PropertyMetadata(252.0));
    public static readonly DependencyProperty PlegadoProperty = DependencyProperty.Register(nameof(Plegado), typeof(bool), typeof(PanelPlegable), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((PanelPlegable)d).Guardar()));
    public static readonly DependencyProperty ClavePreferenciasProperty = DependencyProperty.Register(nameof(ClavePreferencias), typeof(string), typeof(PanelPlegable), new PropertyMetadata(null));

    /// <summary>Título del panel (también es el texto de la pestaña vertical cuando está plegado).</summary>
    public string Titulo { get => (string)GetValue(TituloProperty); set => SetValue(TituloProperty, value); }
    /// <summary>Botones de la cabecera, a la izquierda del de plegar.</summary>
    public object? Acciones { get => GetValue(AccionesProperty); set => SetValue(AccionesProperty, value); }
    /// <summary>Contenido fijo al pie del panel.</summary>
    public object? Pie { get => GetValue(PieProperty); set => SetValue(PieProperty, value); }
    public double AnchoFabrica { get => (double)GetValue(AnchoFabricaProperty); set => SetValue(AnchoFabricaProperty, value); }
    public double AnchoMinimo { get => (double)GetValue(AnchoMinimoProperty); set => SetValue(AnchoMinimoProperty, value); }
    public double AnchoMaximo { get => (double)GetValue(AnchoMaximoProperty); set => SetValue(AnchoMaximoProperty, value); }
    /// <summary>Ancho actual del panel desplegado.</summary>
    public double AnchoPanel { get => (double)GetValue(AnchoPanelProperty); set => SetValue(AnchoPanelProperty, value); }
    public bool Plegado { get => (bool)GetValue(PlegadoProperty); set => SetValue(PlegadoProperty, value); }
    /// <summary>Clave con la que se guardan ancho y plegado para el operador (p. ej. «mayor.recientes»).</summary>
    public string? ClavePreferencias { get => (string?)GetValue(ClavePreferenciasProperty); set => SetValue(ClavePreferenciasProperty, value); }

    /// <summary>Preferencias (por defecto, las del operador activo).</summary>
    public IPreferenciasOperador? Preferencias { get; set; }

    public ICommand PlegarCommand { get; }
    public ICommand DesplegarCommand { get; }

    private sealed record Estado(double Ancho, bool Plegado);
    private bool _cargando;
    private Thumb? _sep;

    public PanelPlegable()
    {
        PlegarCommand = new ComandoDelegado(() => Plegado = true);
        DesplegarCommand = new ComandoDelegado(() => Plegado = false);
        Loaded += (_, _) => Cargar();
    }

    private IPreferenciasOperador Prefs => Preferencias ?? PreferenciasOperador.Actual;
    private string? Clave => string.IsNullOrWhiteSpace(ClavePreferencias) ? null : "panel." + ClavePreferencias;

    private void Cargar()
    {
        _cargando = true;
        try
        {
            var e = Clave is null ? null : Prefs.Leer<Estado>(Clave);
            AnchoPanel = Limitar(e?.Ancho > 0 ? e.Ancho : AnchoFabrica);
            if (e is not null) Plegado = e.Plegado;
        }
        finally { _cargando = false; }
    }

    private double Limitar(double w) => Math.Round(Math.Clamp(w, AnchoMinimo, Math.Max(AnchoMinimo, AnchoMaximo)));

    private void Guardar()
    {
        if (_cargando || Clave is null) return;
        if (!Plegado && Math.Abs(AnchoPanel - AnchoFabrica) < 0.5) Prefs.Borrar(Clave);
        else Prefs.Guardar(Clave, new Estado(Math.Abs(AnchoPanel - AnchoFabrica) < 0.5 ? 0 : AnchoPanel, Plegado));
    }

    public override void OnApplyTemplate()
    {
        if (_sep is not null)
        {
            _sep.DragDelta -= Arrastrar; _sep.DragCompleted -= Soltar; _sep.MouseDoubleClick -= Restablecer; _sep.KeyDown -= Tecla;
        }
        base.OnApplyTemplate();
        _sep = GetTemplateChild("PART_Separador") as Thumb;
        if (_sep is not null)
        {
            _sep.DragDelta += Arrastrar; _sep.DragCompleted += Soltar; _sep.MouseDoubleClick += Restablecer; _sep.KeyDown += Tecla;
        }
    }

    private void Arrastrar(object? s, DragDeltaEventArgs e) => AnchoPanel = Limitar(AnchoPanel + e.HorizontalChange);
    private void Soltar(object? s, DragCompletedEventArgs e) => Guardar();
    private void Restablecer(object? s, MouseButtonEventArgs e) { AnchoPanel = Limitar(AnchoFabrica); Guardar(); e.Handled = true; }

    private void Tecla(object? s, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: AnchoPanel = Limitar(AnchoPanel - 30); break;
            case Key.Right: AnchoPanel = Limitar(AnchoPanel + 30); break;
            case Key.Home: AnchoPanel = Limitar(AnchoFabrica); break;
            default: return;
        }
        e.Handled = true;
        Guardar();
    }
}
