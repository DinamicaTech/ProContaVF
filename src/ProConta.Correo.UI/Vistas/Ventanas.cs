using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProConta.Correo.UI.VistaModelos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Correo.UI.Vistas;

/// <summary>
/// Compositor modal respecto de la ventana que lo abre, movible y redimensionable, sin TopMost (spec § 22 RF-COR-01). Enviar
/// y Cancelar van al pie; Ctrl+Intro envía y Esc cancela. Cerrar con texto sin enviar pide confirmación; cerrar después de
/// enviar no cancela nada.
/// </summary>
public sealed class VentanaCompositorCorreo : DialogoProConta
{
    private readonly CompositorCorreoVM _vm;
    private readonly IDialogosCorreo _dialogos;
    private bool _confirmado;

    public VentanaCompositorCorreo(CompositorCorreoVM vm, IDialogosCorreo dialogos)
    {
        _vm = vm;
        _dialogos = dialogos;
        Redimensionable = true;
        Title = vm.Titulo;
        Width = 820;
        Height = 680;
        MinWidth = 560;
        MinHeight = 460;
        SizeToContent = SizeToContent.Manual;
        DataContext = vm;
        var vista = new VistaCompositorCorreo { DataContext = vm };
        Content = vista;

        var cancelar = new Button { Margin = new Thickness(0, 0, 8, 0), ToolTip = "Cierra la ventana. Antes de enviar, no envía nada; después de enviar, no cancela el envío (Esc)" };
        cancelar.SetBinding(ContentProperty, nameof(CompositorCorreoVM.TextoCerrar));
        cancelar.Click += (_, _) => Close();
        var enviar = new Button
        {
            Content = "Enviar", Style = (Style)FindResource("Boton.Primario"), Command = vm.EnviarCommand,
            ToolTip = "Guarda el mensaje en el registro de correo y lo envía ya con la cuenta del ejercicio (Ctrl+Intro)",
        };
        Iconos.SetIcono(enviar, (System.Windows.Media.Geometry)FindResource("Icono.send"));
        enviar.SetBinding(VisibilityProperty, new System.Windows.Data.Binding(nameof(CompositorCorreoVM.Terminado)) { Converter = new VisibleSiConverter(), ConverterParameter = "invertir" });
        Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, enviar } };

        InputBindings.Add(new KeyBinding(vm.EnviarCommand, Key.Enter, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(vm.MostrarCcCommand, Key.C, ModifierKeys.Control | ModifierKeys.Shift));
        InputBindings.Add(new KeyBinding(vm.MostrarCcoCommand, Key.B, ModifierKeys.Control | ModifierKeys.Shift));
        Loaded += (_, _) => vista.Enfocar();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_vm.Enviando) { e.Cancel = true; return; }
        if (_confirmado || _vm.PuedeCerrarSinPreguntar) return;
        var opcion = _dialogos.Preguntar("Descartar el mensaje", "Has escrito un mensaje que aún no se ha enviado. Si cierras, se pierde.", null,
            new OpcionMensaje("Seguir editando", "Vuelve al mensaje", Principal: true, Cancelar: true),
            new OpcionMensaje("Descartar", "Cierra sin enviar nada", Peligro: true));
        if (opcion == 1) _confirmado = true;
        else e.Cancel = true;
    }
}

/// <summary>Registro de correo en ventana modal: resumen de un lote o el correo de un origen. Cerrarla no cancela envíos.</summary>
public sealed class VentanaResumenCorreo : DialogoProConta
{
    public VentanaResumenCorreo(RegistroCorreoVM vm)
    {
        Redimensionable = true;
        Title = vm.TituloFiltro ?? vm.Titulo;
        Width = 1180;
        Height = 720;
        MinWidth = 760;
        MinHeight = 420;
        SizeToContent = SizeToContent.Manual;
        DataContext = vm;
        Content = new VistaRegistroCorreo { DataContext = vm };
        var cerrar = new Button
        {
            Content = "Cerrar", IsCancel = true, IsDefault = true,
            ToolTip = "Cierra el resumen. Los mensajes que sigan en la cola se enviarán igualmente; los verás en Correo › Correo enviado",
        };
        cerrar.Click += (_, _) => Close();
        Botones = cerrar;
    }
}

/// <summary>Diálogos de correo sobre la ventana activa de la aplicación.</summary>
public sealed class DialogosCorreo(Func<Window?> ventana) : IDialogosCorreo
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(Activa(), titulo, mensaje, detalles, opciones);

    public IReadOnlyList<string> ElegirFicheros()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog { Title = "Adjuntar ficheros", Multiselect = true, CheckFileExists = true };
        return dialogo.ShowDialog(Activa()) == true ? dialogo.FileNames : [];
    }

    public void AbrirArchivo(string ruta)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Preguntar("No he podido abrir el fichero", $"Windows no ha podido abrir {System.IO.Path.GetFileName(ruta)}: {ex.Message}", [ruta],
                new OpcionMensaje("Entendido", "Comprueba que hay un programa asociado a ese tipo de fichero", Principal: true, Cancelar: true));
        }
    }

    public string? PedirTexto(string titulo, string explicacion, string etiqueta, string textoBoton)
    {
        var dialogo = new DialogoProConta { Title = titulo, Owner = Activa(), Width = 540 };
        var caja = new TextBox { MaxLength = 200, Style = (Style)dialogo.FindResource("Campo"), ToolTip = "Queda anotado en el mensaje con tu nombre y la hora" };
        var error = new TextBlock { Foreground = (System.Windows.Media.Brush)dialogo.FindResource("Pincel.RojoError"), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = etiqueta, Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(caja);
        panel.Children.Add(error);
        dialogo.Content = panel;
        var aceptar = new Button { Content = textoBoton, Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, ToolTip = titulo };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No cambia nada" };
        aceptar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(caja.Text)) { error.Text = "Escribe el motivo."; error.Visibility = Visibility.Visible; caja.Focus(); return; }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => caja.Focus();
        return dialogo.ShowDialog() == true ? caja.Text.Trim() : null;
    }

    /// <summary>La ventana activa de la aplicación (el compositor o el resumen si están abiertos), o la principal.</summary>
    private Window? Activa() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? ventana();
}
