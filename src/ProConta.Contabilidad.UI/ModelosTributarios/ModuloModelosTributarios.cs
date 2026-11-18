using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.ModelosTributarios;

/// <summary>Vista de la pantalla Modelos tributarios; la lógica está en <see cref="ModelosTributariosVM"/>. Aquí solo el menú contextual y el clic en la fila.</summary>
public partial class PantallaModelosTributarios : UserControl
{
    public PantallaModelosTributarios() => InitializeComponent();

    private ModelosTributariosVM? Vm => DataContext as ModelosTributariosVM;

    private static FilaPeriodoVM? FilaDe(DependencyObject? o)
    {
        while (o is not null and not DataGridRow) o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);
        return (o as DataGridRow)?.Item as FilaPeriodoVM;
    }

    private void Tabla_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Vm is not { } vm || sender is not DataGrid tabla) return;
        var fila = FilaDe(e.OriginalSource as DependencyObject);
        if (fila is null && !vm.HayMarcados) { e.Handled = true; return; }
        vm.PrepararMenu(fila);
        tabla.ContextMenu = Menu(vm);
    }

    private void BotonMenu_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || sender is not Button b) return;
        vm.PrepararMenu(FilaDe(b));
        var menu = Menu(vm);
        menu.PlacementTarget = b;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>Un clic en la fila (fuera de la casilla y de «⋯») la marca o la desmarca, sin Ctrl ni Mayúsculas.</summary>
    private void Tabla_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var o = e.OriginalSource as DependencyObject;
        for (var x = o; x is not null and not DataGridRow; x = x is Visual ? VisualTreeHelper.GetParent(x) : LogicalTreeHelper.GetParent(x))
            if (x is ButtonBase) return;
        if (FilaDe(o) is { } fila) fila.Marcado = !fila.Marcado;
    }

    private ContextMenu Menu(ModelosTributariosVM vm)
    {
        var menu = new ContextMenu();
        var marcados = vm.Marcados;
        menu.Items.Add(new MenuItem
        {
            Header = marcados.Count == 1 ? $"{marcados[0].Id} · {marcados[0].Periodo} de {marcados[0].Datos.Ejercicio}" : $"{marcados.Count} periodos marcados",
            IsEnabled = false, FontWeight = FontWeights.SemiBold,
        });
        foreach (var o in vm.OpcionesMenu())
        {
            if (o.Separador) { menu.Items.Add(new Separator()); continue; }
            var cabecera = new StackPanel();
            cabecera.Children.Add(new TextBlock { Text = o.Texto });
            if (!string.IsNullOrWhiteSpace(o.Detalle))
                cabecera.Children.Add(new TextBlock { Text = o.Detalle, FontSize = 11, Foreground = (Brush)FindResource("Pincel.Apagado"), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 });
            menu.Items.Add(new MenuItem { Header = cabecera, IsEnabled = o.Habilitada, Command = o.Comando, ToolTip = o.Detalle });
        }
        return menu;
    }
}

/// <summary>Vista del detalle de casillas de un periodo.</summary>
public partial class PantallaCasillasModelo : UserControl
{
    public PantallaCasillasModelo() => InitializeComponent();
}

/// <summary>
/// Diálogos de la pantalla con <see cref="DialogoProConta"/>: la conformidad antes de presentar (contabilidad/modelos-tributarios/presentacion:
/// PDF validado o vista previa, NIF del declarante, importe, forma de pago y cuenta, presentador y entorno, y la casilla «Conforme»), el NRC
/// del pago, «Modelos de la sociedad…», dónde guardar el fichero y los mensajes.
/// </summary>
public sealed class DialogosModelosTributarios(Func<Window?> propietario) : IDialogosModelosTributarios
{
    public bool Conformidad(ConformidadPresentacion c, Action verPdf)
    {
        var dlg = new DialogoProConta
        {
            Title = c.Titulo, Owner = propietario(), Width = 760, Height = 640, MinWidth = 560, MinHeight = 420, SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        var apagado = (Brush)dlg.FindResource("Pincel.Apagado");
        var cuerpo = new StackPanel();
        cuerpo.Children.Add(new Nota
        {
            Tipo = c.Pruebas ? TipoNota.Aviso : TipoNota.Info, Margin = new Thickness(0, 0, 0, 10),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
                Text = c.Pruebas
                    ? "Entorno de PRUEBAS de la AEAT: la presentación no tiene efectos (esta BD es la copia de pruebas)."
                    : "Presentación REAL en la AEAT: una declaración presentada no se puede anular, solo rectificar.",
            },
        });
        var tabla = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < c.Filas.Count; i++)
        {
            tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var f = c.Filas[i];
            var e = new TextBlock { Text = f.Etiqueta, Foreground = apagado, Margin = new Thickness(0, 2, 8, 2) };
            var v = new TextBox
            {
                Text = f.Valor, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
                FontWeight = FontWeights.SemiBold, ToolTip = "Se puede seleccionar y copiar",
            };
            if (f.Revisar) v.Foreground = (Brush)dlg.FindResource("Pincel.RojoError");
            Grid.SetRow(e, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            tabla.Children.Add(e); tabla.Children.Add(v);
        }
        cuerpo.Children.Add(tabla);
        void Lista(IReadOnlyList<string> textos, TipoNota tipo)
        {
            if (textos.Count == 0) return;
            var t = new TextBox
            {
                Text = string.Join(Environment.NewLine, textos), IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap,
                ToolTip = "Se puede seleccionar y copiar",
            };
            cuerpo.Children.Add(new Nota { Tipo = tipo, Margin = new Thickness(0, 0, 0, 10), Content = t });
        }
        Lista(c.Bloqueos, TipoNota.Error);
        Lista(c.Avisos, TipoNota.Aviso);
        var ver = new Button
        {
            Content = "Ver el PDF", Style = (Style)dlg.FindResource("Boton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10),
            IsEnabled = c.Pdf is not null, ToolTip = "Abre el PDF que ha devuelto la AEAT al validar: es lo que se va a presentar",
        };
        cuerpo.Children.Add(ver);
        var conforme = new CheckBox
        {
            Content = "Conforme: he revisado el PDF y los datos, y quiero presentar esta declaración", IsEnabled = c.SePuedePresentar, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "La AEAT entiende que el propio envío acepta los datos de la declaración",
        };
        cuerpo.Children.Add(conforme);
        var vistoPdf = false;
        ver.Click += (_, _) => { verPdf(); vistoPdf = true; };
        dlg.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = cuerpo };
        var presentar = new Button
        {
            Content = c.Pruebas ? "Presentar en pruebas" : "Presentar", Style = (Style)dlg.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false,
            ToolTip = "Envía la declaración a la AEAT con el certificado de la sociedad. Se activa al marcar «Conforme»",
        };
        conforme.Checked += (_, _) => presentar.IsEnabled = c.SePuedePresentar;
        conforme.Unchecked += (_, _) => presentar.IsEnabled = false;
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin presentar nada" };
        var resultado = false;
        presentar.Click += (_, _) =>
        {
            if (!vistoPdf && c.Pdf is not null && ServicioMensajes.Preguntar(dlg, "No has abierto el PDF", "¿Presentar sin haber visto el PDF de la declaración?", null,
                    new OpcionMensaje("Ver el PDF", "Abre el PDF antes de presentar", Principal: true), new OpcionMensaje("Presentar igualmente", "Presenta sin verlo")) == 0)
            {
                verPdf();
                vistoPdf = true;
                return;
            }
            resultado = true;
            dlg.DialogResult = true;
        };
        cancelar.Click += (_, _) => dlg.DialogResult = false;
        dlg.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, presentar } };
        dlg.ShowDialog();
        return resultado;
    }

    public string? PedirNrc(string titulo, string explicacion)
    {
        var dlg = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 620, SizeToContent = SizeToContent.Height };
        var cuerpo = new StackPanel();
        cuerpo.Children.Add(new TextBox { Text = explicacion, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var campo = new TextBox { MaxLength = 22, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, FontFamily = new FontFamily("Consolas"), ToolTip = "NRC del justificante del pago: 22 letras o cifras" };
        cuerpo.Children.Add(campo);
        var error = new TextBlock { Foreground = (Brush)dlg.FindResource("Pincel.RojoError"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        cuerpo.Children.Add(error);
        dlg.Content = cuerpo;
        string? nrc = null;
        var aceptar = new Button { Content = "Continuar", Style = (Style)dlg.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Sigue con la validación y la conformidad con este NRC" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No presenta nada" };
        aceptar.Click += (_, _) =>
        {
            if (DeclaracionAeat.ErrorNrc(campo.Text) is { } e) { error.Text = e; return; }
            nrc = campo.Text.Trim().ToUpperInvariant();
            dlg.DialogResult = true;
        };
        cancelar.Click += (_, _) => dlg.DialogResult = false;
        dlg.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dlg.Loaded += (_, _) => campo.Focus();
        return dlg.ShowDialog() == true ? nrc : null;
    }

    public IReadOnlyCollection<string>? ElegirModelos(IReadOnlyList<(DefinicionModelo Modelo, bool Marcado)> modelos)
    {
        var dlg = new DialogoProConta { Title = "Modelos de la sociedad", Owner = propietario(), Width = 660, SizeToContent = SizeToContent.Height };
        var cuerpo = new StackPanel();
        cuerpo.Children.Add(new TextBlock
        {
            Text = "Solo los marcados salen en la lista, en los plazos y en los avisos de Inicio.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            Foreground = (Brush)dlg.FindResource("Pincel.Apagado"),
        });
        var casillas = new List<(string Id, CheckBox Casilla)>();
        foreach (var (m, marcado) in modelos)
        {
            var c = new CheckBox { IsChecked = marcado, Margin = new Thickness(0, 3, 0, 3), ToolTip = m.Corto };
            var t = new TextBlock();
            t.Inlines.Add(new System.Windows.Documents.Run(m.Id) { FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.SemiBold });
            t.Inlines.Add(new System.Windows.Documents.Run($"  {m.Nombre}"));
            t.Inlines.Add(new System.Windows.Documents.Run($" · {m.Periodicidad}") { Foreground = (Brush)dlg.FindResource("Pincel.Apagado") });
            c.Content = t;
            casillas.Add((m.Id, c));
            cuerpo.Children.Add(c);
        }
        dlg.Content = cuerpo;
        IReadOnlyCollection<string>? r = null;
        var guardar = new Button { Content = "Guardar", Style = (Style)dlg.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Guarda los modelos que presenta esta sociedad" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        guardar.Click += (_, _) => { r = casillas.Where(x => x.Casilla.IsChecked == true).Select(x => x.Id).ToList(); dlg.DialogResult = true; };
        cancelar.Click += (_, _) => dlg.DialogResult = false;
        dlg.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, guardar } };
        return dlg.ShowDialog() == true ? r : null;
    }

    public string? GuardarComo(string carpeta, string nombre)
    {
        try { System.IO.Directory.CreateDirectory(carpeta); } catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
        var d = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar el fichero del modelo", InitialDirectory = carpeta, FileName = nombre, DefaultExt = ".txt", Filter = "Fichero del modelo (*.txt)|*.txt|Todos los ficheros (*.*)|*.*",
        };
        return d.ShowDialog(propietario()) == true ? d.FileName : null;
    }

    public void Informar(string titulo, string mensaje, IReadOnlyList<string>? detalles = null) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, new OpcionMensaje("Aceptar", "Cierra el mensaje", Principal: true));

    public void Error(string titulo, string mensaje) => new Mayor.DialogosMayor(propietario).Error(titulo, mensaje);
}

/// <summary>Modelos tributarios dentro del contenedor (contabilidad/modelos-tributarios): una sola pestaña por sesión, y una por detalle de casillas.</summary>
public sealed class ModuloModelosTributarios(ContextoModelosTributarios contexto, IAnfitrionPestanas anfitrion, IDialogosModelosTributarios dialogos)
{
    private ModelosTributariosVM? _vm;

    /// <summary>Abre la pantalla; con <paramref name="modelo"/> y <paramref name="periodo"/>, en ese periodo marcado (enlaces de Liquidación de IVA, Inicio y Fin de ejercicio).</summary>
    public void Abrir(string? modelo = null, string? periodo = null)
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            if (modelo is not null) { _vm.PorPlazo = false; _ = _vm.CargarAsync(modelo, periodo); }
            return;
        }
        _vm = new ModelosTributariosVM(contexto, dialogos, (d, corto) => anfitrion.Abrir(new CasillasModeloVM(d, corto, contexto.RazonSocial)), () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync(modelo, periodo, consultaDelDia: true);
    }
}
