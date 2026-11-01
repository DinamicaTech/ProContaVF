using System.Windows;
using System.Windows.Controls;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Terceros.UI.Vistas;

/// <summary>Diálogos de Terceros con los componentes de la biblioteca visual.</summary>
public sealed class DialogosTerceros(ServiciosTerceros servicios) : IDialogos
{
    private static Window? Propietario => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;

    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(Propietario, titulo, mensaje, detalles, opciones);

    /// <summary>Buscador de terceros en una ventana: reutiliza el de la pestaña Terceros en modo selección.</summary>
    public TerceroEncontrado? ElegirTercero(string titulo, string explicacion, FiltroTerceros filtro)
    {
        TerceroEncontrado? elegido = null;
        var vm = new BuscadorTercerosVM(servicios, null, this, modoSeleccion: true, filtro);
        var dialogo = new DialogoProConta { Title = titulo, Owner = Propietario, Width = 1500, SizeToContent = SizeToContent.Manual, Height = 860 };
        var cuerpo = new DockPanel();
        var texto = new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(texto, Dock.Top);
        cuerpo.Children.Add(texto);
        cuerpo.Children.Add(new Border
        {
            Child = (UIElement)vm.Vista, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
            BorderBrush = (System.Windows.Media.Brush)dialogo.FindResource("Pincel.Linea")
        });
        dialogo.Content = cuerpo;
        var elegir = new Button { Content = "Elegir", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = false,
            ToolTip = "Elige el tercero seleccionado (también con doble clic o Intro)" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin elegir ningún tercero" };
        elegir.Click += (_, _) => { if (vm.Seleccionada is { } s) { elegido = s.Datos; dialogo.DialogResult = true; } };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, elegir } };
        vm.Elegido += (_, t) => { elegido = t; dialogo.DialogResult = true; };
        _ = vm.BuscarAsync();
        return dialogo.ShowDialog() == true ? elegido : null;
    }

    public (string Nombre, string? Descripcion)? PedirPerfilNuevo(string? nombrePropuesto)
    {
        var dialogo = new DialogoProConta { Title = "Nuevo perfil", Owner = Propietario, Width = 560 };
        var nombre = new TextBox { Text = nombrePropuesto ?? "", Width = 340, HorizontalAlignment = HorizontalAlignment.Left, MaxLength = CatalogoPerfilesAccess.LongitudMaximaNombre,
            ToolTip = "Nombre del perfil, único en el catálogo (por ejemplo «Proveedor homologado»)" };
        var descripcion = new TextBox { Width = 480, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Para qué sirve el perfil (opcional)" };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "El perfil se crea al momento en el catálogo, para todos los terceros, y se asigna a esta ficha.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Nombre *", Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(nombre);
        panel.Children.Add(new TextBlock { Text = "Descripción", Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 12, 0, 4) });
        panel.Children.Add(descripcion);
        dialogo.Content = panel;
        var crear = new Button { Content = "Crear y asignar", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Crea el perfil en el catálogo y lo asigna a esta ficha" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No crea ningún perfil" };
        crear.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nombre.Text)) { nombre.Focus(); nombre.ToolTip = "Escribe el nombre del perfil"; return; }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, crear } };
        dialogo.Loaded += (_, _) => nombre.Focus();
        return dialogo.ShowDialog() == true ? (nombre.Text.Trim(), string.IsNullOrWhiteSpace(descripcion.Text) ? null : descripcion.Text.Trim()) : null;
    }
}
