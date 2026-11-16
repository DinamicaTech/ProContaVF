using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.Nucleo.Resultados;

namespace ProConta.App;

/// <summary>
/// Aviso que bloquea el arranque: dice qué ha pasado, por qué y qué hacer, con el detalle técnico
/// copiable aparte y, cuando se puede, el botón que lo corrige.
/// </summary>
internal sealed class VentanaAviso : Window
{
    private VentanaAviso(string titulo, Error error, string? accion, string? ayudaAccion = null, string? tituloLista = null, IReadOnlyList<string>? lista = null)
    {
        Title = "ProContaVF · " + titulo;
        Style = (Style)Application.Current.FindResource("Ventana.ProConta");
        SizeToContent = SizeToContent.Height;
        Width = 620;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // Los botones quedan siempre a la vista: si el texto es largo, se desplaza (la ventana no pasa del 85 % de la pantalla).
        MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        var raiz = new DockPanel { Margin = new Thickness(24) };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = titulo, Style = (Style)FindResource("Texto.Titulo"), Margin = new Thickness(0, 0, 0, 12), TextWrapping = TextWrapping.Wrap });
        // Un párrafo por parte, separados, con su etiqueta en negrita: qué pasa, a qué afecta, por qué, la lista y qué hacer (datos/esquema 2026-11-16 19:09).
        TextBlock Parrafo(string? etiqueta, string texto, double arriba = 10)
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 20, Margin = new Thickness(0, arriba, 0, 0) };
            if (etiqueta is not null) t.Inlines.Add(new System.Windows.Documents.Run(etiqueta + " ") { FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new System.Windows.Documents.Run(texto));
            return t;
        }
        panel.Children.Add(Parrafo(null, error.Mensaje, 0));
        if (error.RegistroAfectado is { } afectado)
        {
            var t = Parrafo("Afecta a:", afectado, 6);
            t.Foreground = (Brush)FindResource("Pincel.Apagado");
            panel.Children.Add(t);
        }
        if (error.Causa is { } causa) panel.Children.Add(Parrafo("Causa:", causa));
        // Lista larga (p. ej. los cambios de esquema pendientes): un elemento por línea en un cuadro de solo lectura con su barra de desplazamiento
        // vertical y altura limitada, para que los botones del pie se vean siempre.
        if (lista is { Count: > 0 })
        {
            if (tituloLista is not null)
                panel.Children.Add(new TextBlock { Text = tituloLista, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4), TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(new TextBox
            {
                Text = string.Join(Environment.NewLine, lista), IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                Height = Math.Min(240, 26 + 19 * lista.Count), VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                ToolTip = "Se puede seleccionar y copiar",
            });
        }
        if (error.AccionCorrectora is { } accionCorrectora) panel.Children.Add(Parrafo("Qué hacer:", accionCorrectora, 12));
        if (!string.IsNullOrWhiteSpace(error.DetalleTecnico))
            panel.Children.Add(new Expander
            {
                Header = "Detalle técnico",
                Margin = new Thickness(0, 12, 0, 0),
                ToolTip = "Información para el responsable técnico; se puede seleccionar y copiar",
                Content = new TextBox { Text = error.DetalleTecnico, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 220, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = new FontFamily("Consolas") }
            });

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        DockPanel.SetDock(botones, Dock.Bottom);
        var salir = new Button { Content = "Salir", MinWidth = 90, IsCancel = true, ToolTip = "Cierra ProContaVF sin cambiar nada" };
        salir.Click += (_, _) => { DialogResult = false; };
        botones.Children.Add(salir);
        if (accion is not null)
        {
            var corregir = new Button { Content = accion, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, Style = (Style)FindResource("Boton.Primario"), ToolTip = ayudaAccion ?? "Elige el fichero accdb de ProContaVF" };
            corregir.Click += (_, _) => { DialogResult = true; };
            botones.Children.Add(corregir);
        }
        raiz.Children.Add(botones);
        raiz.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Content = raiz;
    }

    public static void Mostrar(string titulo, Error error) => new VentanaAviso(titulo, error, null).ShowDialog();

    /// <summary>true si el operador elige la acción que corrige el problema.</summary>
    public static bool Preguntar(string titulo, Error error, string accion, string? ayudaAccion = null, string? tituloLista = null, IReadOnlyList<string>? lista = null) =>
        Crear(titulo, error, accion, ayudaAccion, tituloLista, lista).ShowDialog() == true;

    /// <summary>La ventana sin abrirla (también para capturas fuera de pantalla).</summary>
    public static Window Crear(string titulo, Error error, string? accion, string? ayudaAccion = null, string? tituloLista = null, IReadOnlyList<string>? lista = null) =>
        new VentanaAviso(titulo, error, accion, ayudaAccion, tituloLista, lista);
}
