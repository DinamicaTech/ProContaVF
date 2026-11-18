using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Tablas;

namespace ProConta.Mantenimiento.UI;

/// <summary>
/// Vista de la pantalla genérica: navegación con búsqueda (Ctrl+K), cabecera con ámbito, lista y editor con divisor.
/// El editor se compone con los metadatos del proveedor (un grupo: formulario directo; varios: secciones plegables).
/// </summary>
public sealed class VistaMantenimiento : UserControl
{
    private static readonly Dictionary<string, double> Anchos = new() { ["xs"] = 96, ["s"] = 170, ["m"] = 280, ["l"] = 440, ["f"] = 900 };

    private readonly PantallaMantenimientoVM _vm;
    private readonly TextBox _busqueda;
    private readonly TablaProConta _tabla;
    private readonly StackPanel _editor = new() { Margin = new Thickness(18, 4, 18, 18) };
    private readonly StackPanel _acciones = new() { Orientation = Orientation.Horizontal };
    private readonly ColumnDefinition _columnaLista = new() { Width = new GridLength(520), MinWidth = 260 };
    private readonly ColumnDefinition _columnaDivisor = new() { Width = new GridLength(6) };
    private readonly Dictionary<string, Control> _controles = new(StringComparer.OrdinalIgnoreCase);
    private bool _reconstruirPendiente;

    public VistaMantenimiento(PantallaMantenimientoVM vm)
    {
        _vm = vm;
        DataContext = vm;
        Background = Brushes.White;

        _busqueda = new TextBox { ToolTip = "Busca un mantenimiento o un ajuste por su nombre (Ctrl+K). Intro abre el primero.", Margin = new Thickness(12, 12, 12, 8) };
        AutomationPropertiesNombre(_busqueda, "Buscar un ajuste o mantenimiento");
        _busqueda.SetBinding(TextBox.TextProperty, new Binding(nameof(vm.Busqueda)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        _busqueda.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && vm.ResultadosBusqueda.FirstOrDefault() is { } r) { vm.AbrirResultadoCommand.Execute(r); e.Handled = true; }
            if (e.Key == Key.Escape) { vm.Busqueda = ""; e.Handled = true; }
        };

        _tabla = new TablaProConta
        {
            IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single, AutoGenerateColumns = false, ClavePreferencias = null, TituloExportacion = "Mantenimiento"
        };
        _tabla.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(vm.Registros)));
        _tabla.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(vm.Seleccionada)) { Mode = BindingMode.TwoWay });
        _tabla.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { if (vm.ModoSeleccion) FilaActivada?.Invoke(this, EventArgs.Empty); else EnfocarPrimerCampo(); e.Handled = true; } };
        _tabla.MouseDoubleClick += (_, e) => { if (vm.ModoSeleccion && e.OriginalSource is FrameworkElement { DataContext: FilaRegistro }) FilaActivada?.Invoke(this, EventArgs.Empty); };
        vm.ElegirRegistro ??= ElegirRegistroAsync;

        Content = Componer();
        InputBindings.Add(new KeyBinding(new ComandoDelegado(() => { _busqueda.Focus(); _busqueda.SelectAll(); }), Key.K, ModifierKeys.Control));

        vm.PropertyChanged += AlCambiar;
        vm.Grupos.CollectionChanged += (_, _) => ProgramarReconstruccion();
        ReconstruirColumnas();
        ReconstruirAcciones();
    }

    private static void AutomationPropertiesNombre(DependencyObject o, string nombre) => System.Windows.Automation.AutomationProperties.SetName(o, nombre);

    private UIElement Componer()
    {
        var raiz = new DockPanel();

        // ----- navegación -----
        var nav = new DockPanel { Width = 250, Background = (Brush)FindResource("Pincel.Fondo") };
        nav.Children.Add(_busqueda);
        DockPanel.SetDock(_busqueda, Dock.Top);
        var resultados = new ListBox { ToolTip = "Resultados de la búsqueda: Intro o clic abre el mantenimiento y enfoca el ajuste" };
        resultados.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(_vm.ResultadosBusqueda)));
        resultados.SetBinding(VisibilityProperty, new Binding(nameof(_vm.HayBusqueda)) { Converter = new VisibleSiConverterLocal() });
        resultados.ItemTemplate = Plantilla(f =>
        {
            var p = new StackPanel();
            var t = new TextBlock { FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetBinding(TextBlock.TextProperty, nameof(ResultadoBusqueda.Texto));
            var r = new TextBlock { Foreground = (Brush)FindResource("Pincel.Apagado"), FontSize = (double)FindResource("Letra.Pequena"), TextTrimming = TextTrimming.CharacterEllipsis };
            r.SetBinding(TextBlock.TextProperty, nameof(ResultadoBusqueda.Ruta));
            p.Children.Add(t);
            p.Children.Add(r);
            return p;
        });
        resultados.SelectionChanged += (_, _) => { if (resultados.SelectedItem is ResultadoBusqueda r) { _vm.AbrirResultadoCommand.Execute(r); resultados.SelectedItem = null; } };
        var lista = new ListBox();
        lista.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(_vm.Navegacion)));
        lista.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(_vm.EntradaElegida)) { Mode = BindingMode.TwoWay });
        lista.SetBinding(VisibilityProperty, new Binding(nameof(_vm.HayBusqueda)) { Converter = new VisibleSiConverterLocal(invertir: true) });
        lista.ItemTemplate = Plantilla(_ =>
        {
            var t = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetBinding(TextBlock.TextProperty, nameof(EntradaNavegacion.Texto));
            t.SetBinding(ToolTipProperty, nameof(EntradaNavegacion.Ayuda));
            var estilo = new Style(typeof(TextBlock));
            var categoria = new DataTrigger { Binding = new Binding(nameof(EntradaNavegacion.EsCategoria)), Value = true };
            categoria.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            categoria.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("Pincel.Apagado")));
            categoria.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(0, 8, 0, 0)));
            estilo.Triggers.Add(categoria);
            var hija = new DataTrigger { Binding = new Binding(nameof(EntradaNavegacion.EsCategoria)), Value = false };
            hija.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(12, 0, 0, 0)));
            estilo.Triggers.Add(hija);
            t.Style = estilo;
            return t;
        });
        var contenedorNav = new Grid();
        contenedorNav.Children.Add(lista);
        contenedorNav.Children.Add(resultados);
        nav.Children.Add(contenedorNav);
        var bordeNav = new Border { BorderBrush = (Brush)FindResource("Pincel.Linea"), BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };
        DockPanel.SetDock(bordeNav, Dock.Left);
        if (_vm.ModoSeleccion) bordeNav.Visibility = Visibility.Collapsed;
        raiz.Children.Add(bordeNav);

        // ----- cabecera -----
        var cabecera = new StackPanel { Margin = new Thickness(18, 12, 18, 8) };
        var fila = new StackPanel { Orientation = Orientation.Horizontal };
        var titulo = new TextBlock { Style = (Style)FindResource("Texto.TituloPantalla"), VerticalAlignment = VerticalAlignment.Center };
        titulo.SetBinding(TextBlock.TextProperty, nameof(_vm.TituloProveedor));
        var ambito = new Border
        {
            Margin = new Thickness(12, 0, 0, 0), Padding = new Thickness(8, 2, 8, 2), CornerRadius = (CornerRadius)FindResource("Radio.Pastilla"), VerticalAlignment = VerticalAlignment.Center,
            Background = (Brush)FindResource("Pincel.AzulSuave"), BorderBrush = (Brush)FindResource("Pincel.Linea"), BorderThickness = new Thickness(1)
        };
        var textoAmbito = new TextBlock { FontSize = (double)FindResource("Letra.Pequena") };
        textoAmbito.SetBinding(TextBlock.TextProperty, nameof(_vm.TextoAmbito));
        ambito.Child = textoAmbito;
        ambito.SetBinding(ToolTipProperty, nameof(_vm.AyudaAmbito));
        fila.Children.Add(titulo);
        fila.Children.Add(ambito);
        cabecera.Children.Add(fila);
        var finalidad = new TextBlock { Style = (Style)FindResource("Texto.Apagado"), Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap };
        finalidad.SetBinding(TextBlock.TextProperty, nameof(_vm.FinalidadProveedor));
        cabecera.Children.Add(finalidad);
        var bandaLectura = new Border
        {
            Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(10, 6, 10, 6), CornerRadius = (CornerRadius)FindResource("Radio.Panel"),
            Background = (Brush)FindResource("Pincel.AvisoSuave"), BorderBrush = (Brush)FindResource("Pincel.AvisoBorde"), BorderThickness = new Thickness(1)
        };
        var textoLectura = new TextBlock { TextWrapping = TextWrapping.Wrap };
        textoLectura.SetBinding(TextBlock.TextProperty, new Binding(nameof(_vm.MotivoSoloLectura)) { StringFormat = "Solo consulta: {0}" });
        bandaLectura.Child = textoLectura;
        bandaLectura.SetBinding(VisibilityProperty, new Binding(nameof(_vm.SoloLectura)) { Converter = new VisibleSiConverterLocal() });
        cabecera.Children.Add(bandaLectura);
        var bordeCabecera = new Border { BorderBrush = (Brush)FindResource("Pincel.Linea"), BorderThickness = new Thickness(0, 0, 0, 1), Child = cabecera };
        DockPanel.SetDock(bordeCabecera, Dock.Top);
        raiz.Children.Add(bordeCabecera);

        // ----- lista y editor -----
        var cuerpo = new Grid();
        cuerpo.ColumnDefinitions.Add(_columnaLista);
        cuerpo.ColumnDefinitions.Add(_columnaDivisor);
        cuerpo.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });

        var panelLista = new DockPanel();
        var barraLista = new DockPanel { Margin = new Thickness(12, 10, 12, 8) };
        var nuevo = new Button { Content = "Nuevo", Style = (Style)FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Crea un registro nuevo en este mantenimiento" };
        nuevo.SetBinding(Button.CommandProperty, nameof(_vm.NuevoCommand));
        nuevo.SetBinding(VisibilityProperty, new Binding(nameof(_vm.PuedeCrear)) { Converter = new VisibleSiConverterLocal() });
        DockPanel.SetDock(nuevo, Dock.Right);
        barraLista.Children.Add(nuevo);
        var recuento = new TextBlock { Style = (Style)FindResource("Texto.Apagado"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        recuento.SetBinding(TextBlock.TextProperty, nameof(_vm.Recuento));
        DockPanel.SetDock(recuento, Dock.Right);
        barraLista.Children.Add(recuento);
        var filtro = new TextBox { ToolTip = "Filtra la lista por cualquiera de sus columnas" };
        filtro.SetBinding(TextBox.TextProperty, new Binding(nameof(_vm.Filtro)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
        barraLista.Children.Add(filtro);
        DockPanel.SetDock(barraLista, Dock.Top);
        panelLista.Children.Add(barraLista);
        var estado = new VistaEstado { Content = _tabla, Titulo = "No se han podido leer los datos" };
        estado.SetBinding(VistaEstado.EstadoProperty, nameof(_vm.Estado));
        estado.SetBinding(VistaEstado.MensajeProperty, nameof(_vm.MensajeEstado));
        estado.TextoAccion = "Reintentar";
        estado.SetBinding(VistaEstado.ComandoAccionProperty, nameof(_vm.ReintentarCommand));
        panelLista.Children.Add(estado);
        var bordeLista = new Border { BorderBrush = (Brush)FindResource("Pincel.Linea"), BorderThickness = new Thickness(0, 0, 1, 0), Child = panelLista };
        Grid.SetColumn(bordeLista, 0);
        cuerpo.Children.Add(bordeLista);
        var divisor = new GridSplitter { Width = 6, HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Transparent, ToolTip = "Arrastra para repartir el ancho entre la lista y el editor (← → con el teclado)" };
        Grid.SetColumn(divisor, 1);
        cuerpo.Children.Add(divisor);

        var panelEditor = new DockPanel();
        var cabEditor = new DockPanel { Margin = new Thickness(18, 12, 18, 6) };
        var eliminar = new Button { Content = "Borrar", Style = (Style)FindResource("Boton.Peligro"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Borra el registro si nada lo usa" };
        eliminar.SetBinding(Button.CommandProperty, nameof(_vm.EliminarCommand));
        eliminar.SetBinding(VisibilityProperty, new Binding(nameof(_vm.PuedeBorrar)) { Converter = new VisibleSiConverterLocal() });
        DockPanel.SetDock(eliminar, Dock.Right);
        cabEditor.Children.Add(eliminar);
        DockPanel.SetDock(_acciones, Dock.Right);
        cabEditor.Children.Add(_acciones);
        var titulos = new StackPanel();
        var tituloRegistro = new TextBlock { Style = (Style)FindResource("Texto.Titulo"), TextTrimming = TextTrimming.CharacterEllipsis };
        tituloRegistro.SetBinding(TextBlock.TextProperty, nameof(_vm.TituloRegistro));
        var subtitulo = new TextBlock { Style = (Style)FindResource("Texto.Apagado") };
        subtitulo.SetBinding(TextBlock.TextProperty, nameof(_vm.SubtituloRegistro));
        titulos.Children.Add(tituloRegistro);
        titulos.Children.Add(subtitulo);
        cabEditor.Children.Add(titulos);
        DockPanel.SetDock(cabEditor, Dock.Top);
        panelEditor.Children.Add(cabEditor);

        var resumen = new Border
        {
            Margin = new Thickness(18, 4, 18, 4), Padding = new Thickness(10, 6, 10, 6), CornerRadius = (CornerRadius)FindResource("Radio.Panel"),
            Background = (Brush)FindResource("Pincel.RojoSuave"), BorderBrush = (Brush)FindResource("Pincel.RojoBorde"), BorderThickness = new Thickness(1)
        };
        var erroresLista = new ItemsControl();
        erroresLista.SetBinding(ItemsControl.ItemsSourceProperty, nameof(_vm.ResumenErrores));
        erroresLista.ItemTemplate = Plantilla(_ =>
        {
            var t = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("Pincel.RojoError") };
            t.SetBinding(TextBlock.TextProperty, new Binding());
            return t;
        });
        resumen.Child = erroresLista;
        resumen.SetBinding(VisibilityProperty, new Binding(nameof(_vm.HayErrores)) { Converter = new VisibleSiConverterLocal() });
        DockPanel.SetDock(resumen, Dock.Top);
        panelEditor.Children.Add(resumen);

        var pie = new Border { Style = (Style)FindResource("Barra.Pie") };
        var piePanel = new DockPanel();
        var guardar = new Button { Content = "Guardar", Style = (Style)FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Guarda los cambios del registro (Ctrl+G)" };
        guardar.SetBinding(Button.CommandProperty, nameof(_vm.GuardarCommand));
        var descartar = new Button { Content = "Descartar", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Vuelve a los valores guardados" };
        descartar.SetBinding(Button.CommandProperty, nameof(_vm.DescartarCommand));
        DockPanel.SetDock(guardar, Dock.Right);
        DockPanel.SetDock(descartar, Dock.Right);
        piePanel.Children.Add(guardar);
        piePanel.Children.Add(descartar);
        // «Auto» actúa sobre los campos en edición: va en el pie con Guardar (criterio de UI del propietario; plataforma/mantenimiento 2026-11-18).
        var auto = new Button { Content = "Auto", Margin = new Thickness(0, 0, 12, 0) };
        auto.SetBinding(Button.CommandProperty, nameof(_vm.AutoCommand));
        auto.SetBinding(ToolTipProperty, nameof(_vm.AyudaAuto));
        ToolTipService.SetShowOnDisabled(auto, true);
        auto.SetBinding(VisibilityProperty, new Binding(nameof(_vm.HayAuto)) { Converter = new VisibleSiConverterLocal() });
        DockPanel.SetDock(auto, Dock.Left);
        piePanel.Children.Add(auto);
        var cambios = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold };
        cambios.SetBinding(TextBlock.TextProperty, nameof(_vm.TextoCambios));
        piePanel.Children.Add(cambios);
        pie.Child = piePanel;
        DockPanel.SetDock(pie, Dock.Bottom);
        panelEditor.Children.Add(pie);
        panelEditor.Children.Add(new ScrollViewer { Content = _editor, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        panelEditor.SetBinding(VisibilityProperty, new Binding(nameof(_vm.HayRegistro)) { Converter = new VisibleSiConverterLocal() });
        Grid.SetColumn(panelEditor, 2);
        cuerpo.Children.Add(panelEditor);
        raiz.Children.Add(cuerpo);
        return raiz;
    }

    private static DataTemplate Plantilla(Func<object?, FrameworkElement> crear) => PlantillaCodigo.Crear(crear);

    private void AlCambiar(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(PantallaMantenimientoVM.Columnas):
                ReconstruirColumnas();
                break;
            case nameof(PantallaMantenimientoVM.Acciones):
            case nameof(PantallaMantenimientoVM.HayRegistro):
                ReconstruirAcciones();
                break;
            case nameof(PantallaMantenimientoVM.EsLista):
                _columnaLista.Width = _vm.EsLista ? new GridLength(Math.Max(_columnaLista.Width.Value, 420)) : new GridLength(0);
                _columnaLista.MinWidth = _vm.EsLista ? 260 : 0;
                _columnaDivisor.Width = new GridLength(_vm.EsLista ? 6 : 0);
                break;
            case nameof(PantallaMantenimientoVM.CampoEnfocado):
                if (_vm.CampoEnfocado is string c) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => { if (_controles.TryGetValue(c, out var ctl)) ctl.Focus(); });
                break;
            case nameof(PantallaMantenimientoVM.SoloLectura):
                ProgramarReconstruccion();
                break;
            case nameof(PantallaMantenimientoVM.CuentasSinAsignar):
                RefrescarRotulosPestana();
                break;
        }
    }

    private void ReconstruirColumnas()
    {
        _tabla.Columns.Clear();
        for (var i = 0; i < _vm.Columnas.Count; i++)
        {
            var c = _vm.Columnas[i];
            var col = new DataGridTextColumn
            {
                Header = c.Titulo,
                Binding = new Binding($"{nameof(FilaRegistro.Celdas)}[{i}]"),
                Width = c.Ancho > 0 ? new DataGridLength(c.Ancho) : new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = (Style)FindResource(c.Derecha ? "Texto.Celda.Importe" : "Texto.Celda"),
                SortMemberPath = $"{nameof(FilaRegistro.Celdas)}[{i}]"
            };
            TablaProConta.SetClave(col, c.Clave);
            _tabla.Columns.Add(col);
        }
    }

    private void ReconstruirAcciones()
    {
        _acciones.Children.Clear();
        foreach (var a in _vm.Acciones)
        {
            var b = new Button { Content = a.Texto, ToolTip = a.Ayuda, Margin = new Thickness(8, 0, 0, 0), Command = _vm.AccionCommand, CommandParameter = a.Clave, Style = (Style)FindResource("Boton.Pequeno") };
            _acciones.Children.Add(b);
        }
        // Las acciones sobre el registro también van en el menú contextual de la lista (criterio de UI del propietario 2026-11-05).
        if (_vm.Acciones.Count == 0) { _tabla.ContextMenu = null; return; }
        var menu = new ContextMenu();
        foreach (var a in _vm.Acciones)
            menu.Items.Add(new MenuItem { Header = a.Texto, ToolTip = a.Ayuda, Command = _vm.AccionCommand, CommandParameter = a.Clave });
        _tabla.ContextMenu = menu;
    }

    private void ProgramarReconstruccion()
    {
        if (_reconstruirPendiente) return;
        _reconstruirPendiente = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _reconstruirPendiente = false; ReconstruirEditor(); });
    }

    private readonly List<(TextBlock Rotulo, GrupoEdicion Grupo)> _rotulosPestana = [];

    /// <summary>
    /// Compone el editor (plataforma/mantenimiento 2026-11-18): un grupo es el formulario directo; de dos a tres, secciones plegables; con
    /// cuatro o más, pestañas. En cada grupo, los parámetros arriba y las cuentas en una tabla con su nombre y su estado.
    /// </summary>
    private void ReconstruirEditor()
    {
        _editor.Children.Clear();
        _controles.Clear();
        _rotulosPestana.Clear();
        var varios = _vm.Grupos.Count > 1;
        TabControl? pestanas = null;
        if (_vm.UsaPestanas)
        {
            pestanas = new TabControl { Margin = new Thickness(0, 6, 0, 0), BorderThickness = new Thickness(0, 1, 0, 0), Template = PlantillaPestanas() };
            _editor.Children.Add(pestanas);
        }
        foreach (var g in _vm.Grupos)
        {
            var contenido = Contenido(g);
            if (pestanas is not null)
            {
                var rotulo = new TextBlock { Text = RotuloPestana(g) };
                _rotulosPestana.Add((rotulo, g));
                pestanas.Items.Add(new TabItem { Header = rotulo, Content = contenido, ToolTip = AyudaPestana(g) });
            }
            else if (varios)
                _editor.Children.Add(new Expander { Header = new TextBlock { Text = g.Titulo, Style = (Style)FindResource("Texto.Seccion") }, IsExpanded = true, Content = contenido, Margin = new Thickness(0, 6, 0, 0) });
            else
                _editor.Children.Add(contenido);
        }
        if (pestanas is not null)
        {
            pestanas.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedIndexProperty, new Binding(nameof(_vm.Pestana)) { Mode = BindingMode.TwoWay });
            if (pestanas.SelectedIndex < 0) pestanas.SelectedIndex = 0;
        }
        if (_vm.CampoEnfocado is string enfocar && _controles.TryGetValue(enfocar, out var ctl)) Dispatcher.BeginInvoke(DispatcherPriority.Input, () => ctl.Focus());
    }

    /// <summary>
    /// Como la del tema, pero los rótulos pasan a otra fila si no caben: con el contador «(n)» la última pestaña se cortaba en un panel
    /// estrecho (ensayo del 2026-11-18).
    /// </summary>
    private static ControlTemplate PlantillaPestanas() => (ControlTemplate)System.Windows.Markup.XamlReader.Parse(
        "<ControlTemplate TargetType=\"TabControl\" xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
        "<DockPanel><Border DockPanel.Dock=\"Top\" BorderBrush=\"{DynamicResource Pincel.Linea}\" BorderThickness=\"0,0,0,1\">" +
        "<WrapPanel IsItemsHost=\"True\" Margin=\"0\" /></Border>" +
        "<ContentPresenter ContentSource=\"SelectedContent\" Margin=\"{TemplateBinding Padding}\" /></DockPanel></ControlTemplate>");

    private static string RotuloPestana(GrupoEdicion g) => g.CuentasSinAsignar > 0 ? $"{g.Titulo} ({g.CuentasSinAsignar})" : g.Titulo;
    private static string AyudaPestana(GrupoEdicion g) => g.CuentasSinAsignar > 0
        ? $"{g.Titulo}: {(g.CuentasSinAsignar == 1 ? "1 cuenta sin asignar" : $"{g.CuentasSinAsignar} cuentas sin asignar")}. «Auto» propone las habituales."
        : $"Parámetros y cuentas de {g.Titulo.ToLowerInvariant()}";

    private void RefrescarRotulosPestana()
    {
        foreach (var (rotulo, g) in _rotulosPestana) { rotulo.Text = RotuloPestana(g); if (rotulo.Parent is TabItem t) t.ToolTip = AyudaPestana(g); }
    }

    /// <summary>Un grupo: sus parámetros (campos que se escriben) y, debajo, la tabla de sus cuentas.</summary>
    private FrameworkElement Contenido(GrupoEdicion g)
    {
        var panel = new StackPanel();
        var parametros = g.Parametros;
        var cuentas = g.Cuentas;
        var ambos = parametros.Count > 0 && cuentas.Count > 0;
        if (parametros.Count > 0)
        {
            if (ambos) panel.Children.Add(Rotulo("Parámetros"));
            var campos = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
            var etiquetas = new List<TextBlock>();
            foreach (var c in parametros) campos.Children.Add(Campo(c, etiquetas));
            // Tras cada colocación (con la letra del tema ya aplicada y al cambiar el ancho, que reparte los campos en otras filas).
            campos.SizeChanged += (_, e) => { if (e.WidthChanged) IgualarAlturaEtiquetas(campos, etiquetas); };
            panel.Children.Add(campos);
        }
        if (cuentas.Count > 0)
        {
            if (ambos) panel.Children.Add(Rotulo("Cuentas"));
            panel.Children.Add(TablaCuentas(cuentas));
        }
        return panel;
    }

    private TextBlock Rotulo(string texto) => new()
    {
        Text = texto.ToUpperInvariant(), FontSize = (double)FindResource("Letra.Pequena"), FontWeight = FontWeights.SemiBold,
        Foreground = (Brush)FindResource("Pincel.Apagado"), Margin = new Thickness(0, 10, 0, 2),
    };

    /// <summary>
    /// Las cuentas en tabla (propietario 2026-11-18 04:59): concepto (clic: explicación), código con su lupa, nombre de la subcuenta en el
    /// plan del ejercicio y estado. Los subgrupos (<see cref="CampoMantenimiento.Subgrupo"/>) son filas de cabecera.
    /// </summary>
    private FrameworkElement TablaCuentas(IReadOnlyList<CampoEdicionVM> cuentas)
    {
        var grid = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        // Concepto con ancho fijo (igual en todas las pestañas, en dos líneas si hace falta), Estado con el ancho de su pastilla más larga
        // y Nombre con todo lo que sobra: así el estado nunca se corta en un panel estrecho (ensayo del 2026-11-18).
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 60 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
        var fila = 0;
        void Fila(UIElement e, int columna, int span = 1)
        {
            Grid.SetRow(e, fila);
            Grid.SetColumn(e, columna);
            Grid.SetColumnSpan(e, span);
            grid.Children.Add(e);
        }
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        foreach (var (titulo, col) in new[] { ("Concepto", 0), ("Subcuenta", 1), ("Nombre", 2), ("Estado", 3) })
            Fila(new TextBlock { Text = titulo, FontSize = (double)FindResource("Letra.Pequena"), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Pincel.Apagado"), Margin = new Thickness(0, 0, 12, 4) }, col);
        fila++;
        string? subgrupo = null;
        foreach (var c in cuentas)
        {
            if (c.Definicion.Subgrupo is string sg && sg != subgrupo)
            {
                subgrupo = sg;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Fila(new Border
                {
                    Background = (Brush)FindResource("Pincel.Fondo"), Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 6, 0, 2),
                    Child = new TextBlock { Text = sg, FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Pincel.Texto2") },
                }, 0, 4);
                fila++;
            }
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var concepto = Concepto(c);
            Fila(concepto, 0);

            var tb = new TextBox { Style = (Style)FindResource("Campo.Mono"), IsReadOnly = c.SoloLectura, DataContext = c };
            if (c.Definicion.Longitud is int l) tb.MaxLength = l;
            tb.SetBinding(TextBox.TextProperty, new Binding(nameof(CampoEdicionVM.Texto)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnNotifyDataErrors = true });
            tb.SetBinding(ToolTipProperty, nameof(CampoEdicionVM.Ayuda));
            System.Windows.Automation.AutomationProperties.SetName(tb, c.Definicion.Etiqueta);
            _controles[c.Clave] = tb;
            var selector = CampoSelector(c, tb, c.BuscarCuentaCommand, nameof(CampoEdicionVM.AyudaBuscarCuenta), c.PuedeBuscarCuenta, c.BuscarCuentaAsync, c.VaciarCuenta);
            if (selector is FrameworkElement fe) { fe.Margin = new Thickness(0, 2, 12, 2); fe.DataContext = c; }
            Fila(selector, 1);

            var nombre = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 12, 0), DataContext = c };
            nombre.SetBinding(TextBlock.TextProperty, nameof(CampoEdicionVM.NombreCuenta));
            nombre.SetBinding(ToolTipProperty, nameof(CampoEdicionVM.NombreCuenta));
            var estiloNombre = new Style(typeof(TextBlock));
            var vacia = new DataTrigger { Binding = new Binding(nameof(CampoEdicionVM.EstadoCorrecto)), Value = false };
            vacia.Setters.Add(new Setter(TextBlock.ForegroundProperty, FindResource("Pincel.Apagado")));
            vacia.Setters.Add(new Setter(TextBlock.FontStyleProperty, FontStyles.Italic));
            estiloNombre.Triggers.Add(vacia);
            nombre.Style = estiloNombre;
            Fila(nombre, 2);

            Fila(Estado(c), 3);
            fila++;
        }
        return grid;
    }

    /// <summary>Etiqueta de la cuenta: tooltip con la ayuda y, al hacer clic, la explicación detallada (propietario 2026-11-18 05:13).</summary>
    private FrameworkElement Concepto(CampoEdicionVM c)
    {
        var texto = new TextBlock { Text = c.Definicion.Etiqueta, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 12, 2), TextWrapping = TextWrapping.Wrap, DataContext = c };
        texto.SetBinding(ToolTipProperty, nameof(CampoEdicionVM.AyudaEtiqueta));
        if (c.TieneExplicacion) HacerExplicable(texto, c);
        return texto;
    }

    private void HacerExplicable(TextBlock texto, CampoEdicionVM c)
    {
        texto.TextDecorations = new TextDecorationCollection([new TextDecoration { Location = TextDecorationLocation.Underline, Pen = new Pen((Brush)FindResource("Pincel.Apagado2"), 1) { DashStyle = DashStyles.Dot }, PenOffset = 2 }]);
        texto.Cursor = Cursors.Help;
        texto.MouseLeftButtonUp += (_, e) => { e.Handled = true; MostrarExplicacion(texto, c); };
    }

    private void MostrarExplicacion(FrameworkElement destino, CampoEdicionVM c)
    {
        // El Popup no está en el árbol de la ventana: no hereda letra ni color, así que se fijan aquí (ensayo del 2026-11-18).
        var panel = new StackPanel { MaxWidth = 520 };
        TextElement.SetFontFamily(panel, (FontFamily)FindResource("Fuente.Texto"));
        TextElement.SetFontSize(panel, (double)FindResource("Letra.Normal"));
        TextElement.SetFontWeight(panel, FontWeights.Normal);
        TextElement.SetForeground(panel, (Brush)FindResource("Pincel.Tinta"));
        panel.Children.Add(new TextBlock { Text = c.Definicion.Etiqueta, Style = (Style)FindResource("Texto.Titulo"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        foreach (var a in c.Explicacion)
        {
            panel.Children.Add(new TextBlock { Text = a.Titulo.ToUpperInvariant(), FontSize = (double)FindResource("Letra.Pequena"), FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("Pincel.Texto2"), Margin = new Thickness(0, 10, 0, 2) });
            panel.Children.Add(new TextBlock { Text = a.Texto, TextWrapping = TextWrapping.Wrap });
        }
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = destino, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            Child = new Border
            {
                Background = (Brush)FindResource("Pincel.Blanco"), BorderBrush = (Brush)FindResource("Pincel.Linea"), BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("Radio.Panel"), Padding = new Thickness(16, 12, 16, 14), Margin = new Thickness(0, 4, 8, 8), Child = panel,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = 0.18 },
            },
        };
        popup.Child.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) popup.IsOpen = false; };
        popup.IsOpen = true;
    }

    private Border Estado(CampoEdicionVM c)
    {
        var t = new TextBlock { FontSize = (double)FindResource("Letra.Pequena"), FontWeight = FontWeights.SemiBold };
        t.SetBinding(TextBlock.TextProperty, nameof(CampoEdicionVM.EstadoCuenta));
        var b = new Border { CornerRadius = (CornerRadius)FindResource("Radio.Pastilla"), Padding = new Thickness(8, 1, 8, 1), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = t, DataContext = c };
        b.SetBinding(ToolTipProperty, nameof(CampoEdicionVM.AyudaEstadoCuenta));
        var estilo = new Style(typeof(Border));
        estilo.Setters.Add(new Setter(Border.BackgroundProperty, FindResource("Pincel.VerdeSuave")));
        estilo.Setters.Add(new Setter(TextElement.ForegroundProperty, FindResource("Pincel.Verde")));
        var mal = new DataTrigger { Binding = new Binding(nameof(CampoEdicionVM.EstadoCorrecto)), Value = false };
        mal.Setters.Add(new Setter(Border.BackgroundProperty, FindResource("Pincel.AmbarSuave")));
        mal.Setters.Add(new Setter(TextElement.ForegroundProperty, FindResource("Pincel.Ambar")));
        estilo.Triggers.Add(mal);
        b.Style = estilo;
        return b;
    }

    /// <summary>
    /// Las etiquetas largas pasan a dos líneas en vez de cortarse (plataforma/mantenimiento 2026-11-12). Todas las del grupo
    /// de una misma fila se bajan hasta la altura de la más alta, así sus editores quedan alineados.
    /// </summary>
    private static void IgualarAlturaEtiquetas(WrapPanel panel, List<TextBlock> etiquetas)
    {
        var medidas = new List<(TextBlock Etiqueta, double Fila, double Alto)>();
        foreach (var e in etiquetas)
        {
            if (e.Parent is not FrameworkElement bloque) continue;
            e.MaxHeight = Math.Ceiling(e.FontFamily.LineSpacing * e.FontSize * 2) + 1;
            e.Padding = new Thickness(0);
            e.Measure(new Size(e.MaxWidth, double.PositiveInfinity));
            medidas.Add((e, Math.Round(bloque.TranslatePoint(new Point(0, 0), panel).Y), e.DesiredSize.Height));
        }
        foreach (var fila in medidas.GroupBy(m => m.Fila))
        {
            var alto = fila.Max(m => m.Alto);
            foreach (var m in fila) m.Etiqueta.Padding = new Thickness(0, alto - m.Alto, 0, 0);
        }
    }

    private FrameworkElement Campo(CampoEdicionVM c, List<TextBlock> etiquetas)
    {
        var ancho = Anchos.TryGetValue(c.Definicion.Ancho, out var a) ? a : Anchos["m"];
        var bloque = new StackPanel { Width = ancho, Margin = new Thickness(0, 0, 16, 10), DataContext = c };
        // Hasta dos líneas: más allá se corta, y la ayuda (al pasar el ratón) la muestra entera.
        var etiqueta = new TextBlock
        {
            Text = c.Etiqueta + (c.Pendiente ? " · por confirmar" : ""), Foreground = (Brush)FindResource("Pincel.Texto2"), Margin = new Thickness(0, 0, 0, 3),
            TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = ancho, VerticalAlignment = VerticalAlignment.Bottom,
        };
        etiqueta.SetBinding(ToolTipProperty, nameof(CampoEdicionVM.AyudaEtiqueta));
        if (c.TieneExplicacion) HacerExplicable(etiqueta, c);
        etiquetas.Add(etiqueta);
        Control control;
        switch (c.Tipo)
        {
            case TipoCampo.Logico:
                var cb = new CheckBox { Content = new TextBlock { Text = c.Definicion.Etiqueta, TextWrapping = TextWrapping.Wrap }, IsEnabled = !c.SoloLectura };
                cb.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(CampoEdicionVM.Marcado)) { Mode = BindingMode.TwoWay, ValidatesOnNotifyDataErrors = true });
                etiqueta.Text = " ";
                control = cb;
                break;
            case TipoCampo.Secreto:
                // Sin enlace al texto: lo escrito pasa al modelo y lo guardado nunca vuelve a la pantalla.
                var pb = new PasswordBox { IsEnabled = !c.SoloLectura };
                if (c.Definicion.Longitud is int lp) pb.MaxLength = lp;
                pb.PasswordChanged += (_, _) => c.Texto = pb.Password;
                control = pb;
                break;
            case TipoCampo.Lista:
                var combo = new ComboBox { ItemsSource = c.Opciones, DisplayMemberPath = nameof(OpcionCampo.Texto), IsEnabled = !c.SoloLectura };
                combo.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(CampoEdicionVM.Opcion)) { Mode = BindingMode.TwoWay, ValidatesOnNotifyDataErrors = true });
                control = combo;
                break;
            case TipoCampo.ListaAbierta:
                // Se elige una opción o se escribe una nueva: el texto pasa al modelo, que lo convierte en la opción que coincide.
                var abierta = new ComboBox { ItemsSource = c.Opciones, DisplayMemberPath = nameof(OpcionCampo.Texto), IsEditable = true, IsTextSearchEnabled = true, IsEnabled = !c.SoloLectura };
                abierta.SetBinding(ComboBox.TextProperty, new Binding(nameof(CampoEdicionVM.Texto)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnNotifyDataErrors = true });
                control = abierta;
                break;
            default:
                var estilo = c.Tipo switch
                {
                    TipoCampo.TextoLargo => "Campo.Multilinea",
                    TipoCampo.Codigo or TipoCampo.Cuenta => "Campo.Mono",
                    TipoCampo.Importe => "Campo.Importe",
                    // Campo.Importe formatea en euros: los demás números van a la derecha sin moneda.
                    TipoCampo.Entero or TipoCampo.Decimal or TipoCampo.Porcentaje => "Campo.Numero",
                    _ => "Campo"
                };
                var tb = new TextBox { Style = (Style)FindResource(estilo), IsReadOnly = c.SoloLectura };
                if (c.Tipo == TipoCampo.TextoLargo) { tb.AcceptsReturn = true; tb.TextWrapping = TextWrapping.Wrap; }
                if (c.Definicion.Longitud is int l && c.Tipo is not (TipoCampo.Entero or TipoCampo.Decimal or TipoCampo.Porcentaje or TipoCampo.Importe)) tb.MaxLength = l;
                if (c.SoloLectura) tb.Background = (Brush)FindResource("Pincel.Fondo2");
                tb.SetBinding(TextBox.TextProperty, new Binding(nameof(CampoEdicionVM.Texto)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, ValidatesOnNotifyDataErrors = true });
                control = tb;
                break;
        }
        control.SetBinding(ToolTipProperty, nameof(CampoEdicionVM.Ayuda));
        System.Windows.Automation.AutomationProperties.SetName(control, c.Definicion.Etiqueta);
        _controles[c.Clave] = control;
        bloque.Children.Add(etiqueta);
        if (c.EsCuenta && control is TextBox cuenta)
            bloque.Children.Add(CampoSelector(c, cuenta, c.BuscarCuentaCommand, nameof(CampoEdicionVM.AyudaBuscarCuenta), c.PuedeBuscarCuenta, c.BuscarCuentaAsync, c.VaciarCuenta));
        else if (c.EsReferencia && control is TextBox referencia)
            bloque.Children.Add(CampoSelector(c, referencia, c.ElegirReferenciaCommand, nameof(CampoEdicionVM.AyudaElegirReferencia), c.PuedeElegirReferencia, c.ElegirReferenciaAsync, c.VaciarReferencia));
        else
            bloque.Children.Add(control);
        if (c.Bloqueo is string motivo)
            bloque.Children.Add(new TextBlock { Text = motivo, FontSize = (double)FindResource("Letra.Pequena"), Foreground = (Brush)FindResource("Pincel.Apagado"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        return bloque;
    }

    /// <summary>
    /// Campo que no se escribe y se elige en un selector (plataforma/mantenimiento 2026-11-12, propietario 12:16): las cuentas
    /// en el buscador de cuentas del ejercicio (con «Crear subcuenta») y las referencias a otra tabla 1:N en esa tabla (con
    /// su alta). Lo abren la lupa, el clic en el campo, Intro, Espacio y F4; Supr lo vacía.
    /// </summary>
    private FrameworkElement CampoSelector(CampoEdicionVM c, TextBox texto, ICommand abrir, string propiedadAyuda, bool editable, Func<Task> abrirAsync, Action vaciar)
    {
        var buscar = new Button { Style = (Style)FindResource("Boton.Icono"), Margin = new Thickness(4, 0, 0, 0), Command = abrir, Focusable = false };
        Iconos.SetIcono(buscar, (Geometry)FindResource("Icono.search"));
        buscar.SetBinding(ToolTipProperty, propiedadAyuda);
        System.Windows.Automation.AutomationProperties.SetName(buscar, $"Elegir {c.Definicion.Etiqueta}");
        DockPanel.SetDock(buscar, Dock.Right);
        texto.IsReadOnly = true;
        if (editable)
        {
            texto.Cursor = Cursors.Hand;
            texto.PreviewMouseLeftButtonUp += (_, e) => { e.Handled = true; _ = abrirAsync(); };
            texto.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.F4 or Key.Enter or Key.Space) { e.Handled = true; _ = abrirAsync(); }
                else if (e.Key is Key.Delete or Key.Back) { e.Handled = true; vaciar(); }
            };
        }
        var fila = new DockPanel { LastChildFill = true };
        fila.Children.Add(buscar);
        fila.Children.Add(texto);
        return fila;
    }

    /// <summary>En modo selección: doble clic o Intro en una fila de la lista.</summary>
    public event EventHandler? FilaActivada;

    /// <summary>
    /// Selector de un registro de otra tabla 1:N (plataforma/mantenimiento 2026-11-12): la misma pantalla con solo ese
    /// mantenimiento, donde también se crea o se corrige el registro, y «Elegir» (o doble clic) para llevarlo al campo.
    /// </summary>
    private async Task<RegistroMantenimiento?> ElegirRegistroAsync(IProveedorMantenimiento proveedor, object? clave)
    {
        var vm = new PantallaMantenimientoVM(new ServicioMantenimiento([proveedor], _vm.Servicio.PuedeEditarAsync), _vm.Contexto, _vm.Preguntar, _vm.AvisarOperador, x => new VistaMantenimiento(x))
            { ModoSeleccion = true, Cuentas = _vm.Cuentas };
        var vista = new VistaMantenimiento(vm);
        var dialogo = new DialogoProConta
        {
            Title = $"Elegir en «{proveedor.Titulo}»", Owner = Window.GetWindow(this), Width = 1180, Height = 700, MinWidth = 700, MinHeight = 400,
            SizeToContent = SizeToContent.Manual, Redimensionable = true, Content = vista,
        };
        RegistroMantenimiento? elegido = null;
        async Task Elegir()
        {
            if (await vm.RegistroElegidoAsync() is not { } r) return;
            elegido = r;
            dialogo.DialogResult = true;
        }
        var elegir = new Button { Content = "Elegir", Style = (Style)FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0),
            ToolTip = $"Lleva al campo el registro seleccionado (también doble clic o Intro en la lista). Para uno nuevo, «Nuevo», rellénalo y «Guardar»" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar el campo" };
        elegir.Click += async (_, _) => await Elegir();
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        vista.FilaActivada += async (_, _) => await Elegir();
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, elegir } };
        dialogo.Loaded += async (_, _) => await vm.IniciarSeleccionAsync(clave);
        var aceptado = dialogo.ShowDialog() == true;
        _vm.OlvidarReferencias();
        return aceptado ? elegido : null;
    }

    private void EnfocarPrimerCampo()
    {
        var primero = _controles.Values.FirstOrDefault(c => c is TextBox { IsReadOnly: false } || c.IsEnabled);
        primero?.Focus();
    }
}

/// <summary>Visible si el valor es true (o, con <c>invertir</c>, si es false); también acepta texto o colección no vacíos.</summary>
internal sealed class VisibleSiConverterLocal(bool invertir = false) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var si = value switch { bool b => b, string s => s.Length > 0, null => false, _ => true };
        return si ^ invertir ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
