using System.Windows;
using System.Windows.Input;
using ProConta.UI.Base.Comandos;

namespace ProConta.UI.Base.Controles;

/// <summary>
/// Diálogo modal con el aspecto de las maquetas: título, contenido y botones al pie. Esc o el aspa cierran sin cambios.
/// Los cuadros de mensaje se reservan para errores que bloquean y piden una decisión (docs/plataforma/ui-base.md).
/// </summary>
public class DialogoProConta : Window
{
    static DialogoProConta()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogoProConta), new FrameworkPropertyMetadata(typeof(DialogoProConta)));
    }

    public static readonly DependencyProperty BotonesProperty = DependencyProperty.Register(nameof(Botones), typeof(object), typeof(DialogoProConta), new PropertyMetadata(null));

    /// <summary>Botones del pie (normalmente un StackPanel horizontal con Cancelar y el botón principal).</summary>
    public object? Botones { get => GetValue(BotonesProperty); set => SetValue(BotonesProperty, value); }

    /// <summary>Cierra el diálogo sin aceptar.</summary>
    public ICommand CerrarCommand { get; }

    /// <summary>Cierra el diálogo aceptando.</summary>
    public ICommand AceptarCommand { get; }

    public DialogoProConta()
    {
        // WindowStartupLocation no es propiedad de dependencia: no puede ir en el estilo.
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CerrarCommand = new ComandoDelegado(() => { DialogResult = false; });
        AceptarCommand = new ComandoDelegado(() => { DialogResult = true; });
    }

    /// <summary>
    /// Esc cierra sin aceptar aunque ningún botón tenga IsCancel o el foco esté en un campo que no lo deja pasar; un
    /// control que usa Esc (un desplegable abierto) lo marca como atendido y el diálogo no se cierra.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || e.Key != Key.Escape || Keyboard.Modifiers != ModifierKeys.None) return;
        e.Handled = true;
        DialogResult = false;
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Cabecera") is UIElement cab)
            cab.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
    }
}
