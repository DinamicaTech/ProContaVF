using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProConta.UI.Base.Controles;

/// <summary>Etiqueta de filtro que se quita con su aspa (los filtros activos se ven siempre como etiquetas).</summary>
public class Etiqueta : ContentControl
{
    static Etiqueta()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Etiqueta), new FrameworkPropertyMetadata(typeof(Etiqueta)));
    }

    public static readonly DependencyProperty ComandoQuitarProperty = DependencyProperty.Register(nameof(ComandoQuitar), typeof(ICommand), typeof(Etiqueta), new PropertyMetadata(null));
    public static readonly DependencyProperty ParametroQuitarProperty = DependencyProperty.Register(nameof(ParametroQuitar), typeof(object), typeof(Etiqueta), new PropertyMetadata(null));
    public static readonly DependencyProperty AyudaQuitarProperty = DependencyProperty.Register(nameof(AyudaQuitar), typeof(string), typeof(Etiqueta), new PropertyMetadata("Quitar este filtro"));

    public ICommand? ComandoQuitar { get => (ICommand?)GetValue(ComandoQuitarProperty); set => SetValue(ComandoQuitarProperty, value); }
    public object? ParametroQuitar { get => GetValue(ParametroQuitarProperty); set => SetValue(ParametroQuitarProperty, value); }
    public string AyudaQuitar { get => (string)GetValue(AyudaQuitarProperty); set => SetValue(AyudaQuitarProperty, value); }
}
