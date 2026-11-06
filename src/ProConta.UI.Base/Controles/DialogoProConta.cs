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

    /// <summary>
    /// Diálogo que se puede maximizar (botón o doble clic en el título) y cambiar de tamaño con el asa de la esquina. Lo
    /// fija quien crea el diálogo, antes de mostrarlo. Respeta su SizeToContent hasta que el operador cambia el tamaño.
    /// </summary>
    public bool Redimensionable { get; init; }

    /// <summary>Maximiza o restaura un diálogo redimensionable.</summary>
    public ICommand MaximizarCommand { get; }

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
        MaximizarCommand = new ComandoDelegado(Maximizar, () => Redimensionable);
    }


    private SizeToContent _ajusteAntes = SizeToContent.Manual;

    private void Maximizar()
    {
        if (!Redimensionable) return;
        // Al restaurar vuelve el ajuste al contenido que tenía (salvo que el operador lo cambiara con el asa).
        if (WindowState == WindowState.Maximized) { WindowState = WindowState.Normal; SizeToContent = _ajusteAntes; return; }
        _ajusteAntes = SizeToContent;
        SizeToContent = SizeToContent.Manual;
        // Sin borde de Windows, maximizar taparía la barra de tareas: se limita al área de trabajo.
        var area = SystemParameters.WorkArea;
        MaxWidth = area.Width;
        MaxHeight = area.Height;
        WindowState = WindowState.Maximized;
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
        {
            // DragMove se queda con el ratón: llamarlo al pulsar impedía que llegara el doble clic. Se arrastra al mover.
            Point? pulsado = null;
            cab.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ClickCount == 2 && Redimensionable) { pulsado = null; Maximizar(); e.Handled = true; return; }
                pulsado = WindowState == WindowState.Normal ? e.GetPosition(this) : null;
            };
            cab.MouseLeftButtonUp += (_, _) => pulsado = null;
            cab.MouseMove += (_, e) =>
            {
                if (pulsado is not { } p || e.LeftButton != MouseButtonState.Pressed) return;
                var v = e.GetPosition(this) - p;
                if (Math.Abs(v.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(v.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                pulsado = null;
                DragMove();
            };
        }
        if (!Redimensionable) return;
        if (GetTemplateChild("PART_Maximizar") is UIElement max) max.Visibility = Visibility.Visible;
        if (GetTemplateChild("PART_Asa") is System.Windows.Controls.Primitives.Thumb asa)
        {
            asa.Visibility = Visibility.Visible;
            asa.DragDelta += (_, e) =>
            {
                if (WindowState != WindowState.Normal) return;
                SizeToContent = SizeToContent.Manual;
                Width = Math.Max(MinWidth > 0 ? MinWidth : 360, ActualWidth + e.HorizontalChange);
                Height = Math.Max(MinHeight > 0 ? MinHeight : 260, ActualHeight + e.VerticalChange);
            };
        }
    }
}
