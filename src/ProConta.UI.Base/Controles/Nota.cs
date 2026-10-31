using System.Windows;
using System.Windows.Controls;

namespace ProConta.UI.Base.Controles;

/// <summary>Tipo de nota.</summary>
public enum TipoNota
{
    Info,
    Aviso,
    Ok,
    Error
}

/// <summary>Nota en línea con icono: información, aviso, confirmación o error que no bloquea.</summary>
public class Nota : ContentControl
{
    static Nota()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(Nota), new FrameworkPropertyMetadata(typeof(Nota)));
    }

    public static readonly DependencyProperty TipoProperty = DependencyProperty.Register(nameof(Tipo), typeof(TipoNota), typeof(Nota), new PropertyMetadata(TipoNota.Info));

    public TipoNota Tipo { get => (TipoNota)GetValue(TipoProperty); set => SetValue(TipoProperty, value); }
}
