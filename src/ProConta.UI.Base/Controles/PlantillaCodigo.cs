using System.Windows;
using System.Windows.Controls;

namespace ProConta.UI.Base.Controles;

/// <summary>Plantillas de datos creadas en código: cada elemento de la lista construye su vista con la función dada.</summary>
public static class PlantillaCodigo
{
    public static DataTemplate Crear(Func<object?, FrameworkElement> crear)
    {
        var fabrica = new FrameworkElementFactory(typeof(PresentadorCodigo));
        fabrica.SetValue(PresentadorCodigo.FabricaProperty, crear);
        return new DataTemplate { VisualTree = fabrica };
    }
}

/// <summary>Muestra la vista que construye <see cref="Fabrica"/> para su DataContext, y la rehace si cambia.</summary>
public sealed class PresentadorCodigo : ContentControl
{
    public static readonly DependencyProperty FabricaProperty = DependencyProperty.Register(
        nameof(Fabrica), typeof(Func<object?, FrameworkElement>), typeof(PresentadorCodigo), new PropertyMetadata(null, (d, _) => ((PresentadorCodigo)d).Construir()));

    public PresentadorCodigo()
    {
        Focusable = false;
        IsTabStop = false;
        DataContextChanged += (_, _) => Construir();
    }

    public Func<object?, FrameworkElement>? Fabrica
    {
        get => (Func<object?, FrameworkElement>?)GetValue(FabricaProperty);
        set => SetValue(FabricaProperty, value);
    }

    private void Construir() => Content = Fabrica is { } f && DataContext is { } dato ? f(dato) : null;
}
