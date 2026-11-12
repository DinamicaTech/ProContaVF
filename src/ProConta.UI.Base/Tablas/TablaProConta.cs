using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Preferencias;

namespace ProConta.UI.Base.Tablas;

/// <summary>
/// Tabla común de ProConta: el criterio global de columnas (docs/plataforma/ui-base.md, 2026-10-31 09:58).
/// <list type="bullet">
/// <item>Redimensionar arrastrando el borde de la cabecera; doble clic en el borde: ajustar al contenido.</item>
/// <item>Ordenar con clic en el título: ascendente, descendente y vuelta al orden de fábrica.</item>
/// <item>Mover arrastrando el título o con Alt+← → en una celda de la columna.</item>
/// <item>Clic derecho en la cabecera: mover u ocultar la columna, columnas visibles, ajustar todas, restablecer y exportar.</item>
/// <item>Orden, visibles y anchos se guardan por operador y tabla (<see cref="ClavePreferencias"/>).</item>
/// <item>Exportar a Excel: todas las filas de la vista con las columnas visibles en su orden.</item>
/// </list>
/// Ordenar o mover columnas no recalcula datos: un saldo acumulado conserva en cada fila su valor por fecha.
/// </summary>
public class TablaProConta : DataGrid
{
    // ---------- propiedades adjuntas de columna ----------

    /// <summary>Clave estable de la columna para guardar sus preferencias (por defecto, SortMemberPath o el título).</summary>
    public static readonly DependencyProperty ClaveProperty =
        DependencyProperty.RegisterAttached("Clave", typeof(string), typeof(TablaProConta), new PropertyMetadata(null));
    public static string? GetClave(DependencyObject o) => (string?)o.GetValue(ClaveProperty);
    public static void SetClave(DependencyObject o, string? v) => o.SetValue(ClaveProperty, v);

    /// <summary>La columna viene oculta de fábrica; el operador la muestra desde el menú de la cabecera.</summary>
    public static readonly DependencyProperty OcultaDeFabricaProperty =
        DependencyProperty.RegisterAttached("OcultaDeFabrica", typeof(bool), typeof(TablaProConta), new PropertyMetadata(false));
    public static bool GetOcultaDeFabrica(DependencyObject o) => (bool)o.GetValue(OcultaDeFabricaProperty);
    public static void SetOcultaDeFabrica(DependencyObject o, bool v) => o.SetValue(OcultaDeFabricaProperty, v);

    /// <summary>La columna no se puede ocultar (por ejemplo, el concepto).</summary>
    public static readonly DependencyProperty SiempreVisibleProperty =
        DependencyProperty.RegisterAttached("SiempreVisible", typeof(bool), typeof(TablaProConta), new PropertyMetadata(false));
    public static bool GetSiempreVisible(DependencyObject o) => (bool)o.GetValue(SiempreVisibleProperty);
    public static void SetSiempreVisible(DependencyObject o, bool v) => o.SetValue(SiempreVisibleProperty, v);

    /// <summary>Ruta de la propiedad que se exporta a Excel (por defecto, la del Binding o SortMemberPath). Mejor el valor sin formato.</summary>
    public static readonly DependencyProperty RutaExportarProperty =
        DependencyProperty.RegisterAttached("RutaExportar", typeof(string), typeof(TablaProConta), new PropertyMetadata(null));
    public static string? GetRutaExportar(DependencyObject o) => (string?)o.GetValue(RutaExportarProperty);
    public static void SetRutaExportar(DependencyObject o, string? v) => o.SetValue(RutaExportarProperty, v);

    /// <summary>Ayuda adicional de la cabecera (se suma a la de ordenar y mover).</summary>
    public static readonly DependencyProperty AyudaProperty =
        DependencyProperty.RegisterAttached("Ayuda", typeof(string), typeof(TablaProConta), new PropertyMetadata(null));
    public static string? GetAyuda(DependencyObject o) => (string?)o.GetValue(AyudaProperty);
    public static void SetAyuda(DependencyObject o, string? v) => o.SetValue(AyudaProperty, v);

    // ---------- propiedades de la tabla ----------

    /// <summary>Clave con la que se guardan las columnas de esta tabla para el operador (p. ej. «mayor»). Sin clave, no se guardan.</summary>
    public string? ClavePreferencias
    {
        get => (string?)GetValue(ClavePreferenciasProperty);
        set => SetValue(ClavePreferenciasProperty, value);
    }
    public static readonly DependencyProperty ClavePreferenciasProperty =
        DependencyProperty.Register(nameof(ClavePreferencias), typeof(string), typeof(TablaProConta), new PropertyMetadata(null));

    /// <summary>Título de la exportación a Excel (primera fila del libro y nombre de fichero propuesto).</summary>
    public string? TituloExportacion
    {
        get => (string?)GetValue(TituloExportacionProperty);
        set => SetValue(TituloExportacionProperty, value);
    }
    public static readonly DependencyProperty TituloExportacionProperty =
        DependencyProperty.Register(nameof(TituloExportacion), typeof(string), typeof(TablaProConta), new PropertyMetadata(null));

    /// <summary>Preferencias donde se guardan las columnas (por defecto, las del operador activo).</summary>
    public IPreferenciasOperador? Preferencias { get; set; }

    /// <summary>Exporta todas las filas de la vista a Excel (pide dónde guardar).</summary>
    public ICommand ExportarExcelCommand { get; }

    /// <summary>Restablece orden, visibles y anchos de fábrica.</summary>
    public ICommand RestablecerColumnasCommand { get; }

    /// <summary>Avisos para la barra de estado de la pantalla (columna movida, exportación hecha…).</summary>
    public event EventHandler<string>? Aviso;

    /// <summary>Se produce al cambiar el orden de las filas (incluida la vuelta al orden de fábrica).</summary>
    public event EventHandler? OrdenFilasCambiado;

    private readonly Dictionary<DataGridColumn, DataGridLength> _anchosFabrica = new();
    private readonly Dictionary<DataGridColumn, double> _minimosFabrica = new();
    private List<DataGridColumn> _ordenFabrica = new();
    private ConfiguracionColumnas? _cfg;
    private bool _aplicando;
    private bool _iniciada;
    private readonly DispatcherTimer _guardarAnchos;

    public TablaProConta()
    {
        CanUserReorderColumns = true;
        CanUserResizeColumns = true;
        CanUserSortColumns = true;
        AutoGenerateColumns = false;
        EnableRowVirtualization = true;
        EnableColumnVirtualization = true;
        VirtualizingPanel.SetVirtualizationMode(this, VirtualizationMode.Recycling);
        ExportarExcelCommand = new ComandoDelegado(() => ExportarAExcel(), () => Items.Count > 0);
        RestablecerColumnasCommand = new ComandoDelegado(RestablecerColumnas);
        _guardarAnchos = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, (_, _) => { _guardarAnchos!.Stop(); GuardarAnchos(); }, Dispatcher);
        _guardarAnchos.Stop();
        Loaded += (_, _) => Iniciar();
        // Separadores de la cabecera propios (plataforma/ui-base 2026-11-12): arrastrar cambia solo esa columna y desplaza las
        // demás (si no caben, barra horizontal), sin anchos mínimos; doble clic ajusta la columna a su contenido.
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(EmpezarArrastre));
        AddHandler(Thumb.DragDeltaEvent, new DragDeltaEventHandler(ArrastrarSeparador));
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, e) => { if (ColumnaDelSeparador(e.OriginalSource) is not null) _arrastre = null; }));
        AddHandler(Control.MouseDoubleClickEvent, new MouseButtonEventHandler(DobleClicSeparador));
    }

    /// <summary>Ancho más pequeño al que se puede llevar una columna (para que no desaparezca: ocultarla es otra opción).</summary>
    public const double AnchoMinimo = 8;

    /// <summary>La columna cuyo borde derecho es el separador (el izquierdo de una cabecera es el derecho de la anterior visible).</summary>
    private DataGridColumn? ColumnaDelSeparador(object origen)
    {
        if (origen is not Thumb { TemplatedParent: DataGridColumnHeader { Column: { } col } } pinza) return null;
        if (pinza.Name == "PinzaDerecha") return col;
        if (pinza.Name != "PinzaIzquierda") return null;
        return Columns.Where(c => c.Visibility == Visibility.Visible && c.DisplayIndex < col.DisplayIndex).OrderByDescending(c => c.DisplayIndex).FirstOrDefault();
    }

    private (DataGridColumn Columna, double Ancho, double X)? _arrastre;

    private void EmpezarArrastre(object sender, DragStartedEventArgs e)
    {
        if (ColumnaDelSeparador(e.OriginalSource) is not { } col) return;
        e.Handled = true;
        _arrastre = (col, col.ActualWidth, Mouse.GetPosition(this).X);
    }

    /// <summary>El ancho sigue al ratón desde donde empezó el arrastre (no a la suma de cambios, que se adelanta al dibujo).</summary>
    private void ArrastrarSeparador(object sender, DragDeltaEventArgs e)
    {
        if (ColumnaDelSeparador(e.OriginalSource) is null || _arrastre is not { } a) return;
        e.Handled = true;
        var nuevo = Math.Max(AnchoMinimo, a.Ancho + Mouse.GetPosition(this).X - a.X);
        a.Columna.MinWidth = Math.Min(a.Columna.MinWidth, nuevo);
        a.Columna.Width = new DataGridLength(nuevo);
    }

    private void DobleClicSeparador(object sender, MouseButtonEventArgs e)
    {
        if (ColumnaDelSeparador(e.OriginalSource) is not { } col) return;
        e.Handled = true;
        var ancho = MedirColumna(col);
        col.MinWidth = Math.Min(col.MinWidth, ancho);
        col.Width = new DataGridLength(ancho);
    }

    private IPreferenciasOperador Prefs => Preferencias ?? PreferenciasOperador.Actual;
    private string? ClaveGuardado => string.IsNullOrWhiteSpace(ClavePreferencias) ? null : "tabla." + ClavePreferencias;

    /// <summary>Clave efectiva de una columna.</summary>
    public static string ClaveDe(DataGridColumn c) =>
        GetClave(c) ?? (string.IsNullOrEmpty(c.SortMemberPath) ? c.Header?.ToString() ?? "col" : c.SortMemberPath);

    private static string TituloDe(DataGridColumn c) => c.Header?.ToString() ?? ClaveDe(c);

    private IReadOnlyList<DefinicionColumna> Definiciones() =>
        _ordenFabrica.Select(c => new DefinicionColumna(ClaveDe(c), TituloDe(c), GetOcultaDeFabrica(c), GetSiempreVisible(c))).ToList();

    private void Iniciar()
    {
        if (_iniciada) return;
        _iniciada = true;
        _ordenFabrica = Columns.ToList();
        foreach (var c in Columns)
        {
            _anchosFabrica[c] = c.Width;
            _minimosFabrica[c] = c.MinWidth;
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn))
                .AddValueChanged(c, (_, _) => { if (!_aplicando) { _guardarAnchos.Stop(); _guardarAnchos.Start(); } });
        }
        var k = ClaveGuardado;
        _cfg = k is null ? null : Prefs.Leer<ConfiguracionColumnas>(k);
        Aplicar();
    }

    /// <summary>Aplica la configuración guardada (o la de fábrica) a las columnas.</summary>
    private void Aplicar()
    {
        _aplicando = true;
        try
        {
            var res = DisposicionColumnas.Resolver(Definiciones(), _cfg);
            var porClave = _ordenFabrica.ToDictionary(ClaveDe, StringComparer.Ordinal);
            foreach (var r in res)
            {
                var col = porClave[r.Definicion.Clave];
                col.DisplayIndex = r.Posicion;
                col.Visibility = r.Visible ? Visibility.Visible : Visibility.Collapsed;
                col.Width = r.Ancho is { } w ? new DataGridLength(w) : _anchosFabrica[col];
                // Sin mínimos impuestos: el de fábrica de la columna, salvo que el operador la haya dejado más estrecha.
                col.MinWidth = Math.Min(_minimosFabrica.GetValueOrDefault(col, col.MinWidth), col.Width.IsAbsolute ? col.Width.Value : double.MaxValue);
            }
        }
        finally { _aplicando = false; }
    }

    private void Guardar(ConfiguracionColumnas? cfg)
    {
        _cfg = cfg;
        var k = ClaveGuardado;
        if (k is null) return;
        if (cfg is null || cfg.EsDeFabrica) Prefs.Borrar(k); else Prefs.Guardar(k, cfg);
    }

    private void GuardarAnchos()
    {
        var cfg = _cfg;
        foreach (var c in _ordenFabrica)
        {
            var fab = _anchosFabrica[c];
            var cambiado = c.Width.IsAbsolute && (!fab.IsAbsolute || Math.Abs(fab.Value - c.Width.Value) > 0.5);
            cfg = DisposicionColumnas.CambiarAncho(cfg, ClaveDe(c), cambiado ? c.Width.Value : null);
        }
        Guardar(cfg);
    }

    protected override void OnColumnReordered(DataGridColumnEventArgs e)
    {
        base.OnColumnReordered(e);
        if (_aplicando) return;
        var orden = Columns.OrderBy(c => c.DisplayIndex).Select(ClaveDe).ToList();
        var cfg = _cfg is null ? new ConfiguracionColumnas() : _cfg;
        cfg.Orden = orden.SequenceEqual(_ordenFabrica.Select(ClaveDe)) ? null : orden;
        Guardar(cfg);
        Aviso?.Invoke(this, "Columna «" + TituloDe(e.Column) + "» movida. Se guarda para ti; «Restablecer columnas» vuelve al orden de fábrica.");
    }

    // ---------- ordenar: ascendente, descendente y vuelta al orden de fábrica ----------

    protected override void OnSorting(DataGridSortingEventArgs e)
    {
        if (e.Column.SortDirection == ListSortDirection.Descending && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
        {
            e.Handled = true;
            e.Column.SortDirection = null;
            Items.SortDescriptions.Clear();
            foreach (var c in Columns) c.SortDirection = null;
            Items.Refresh();
            OrdenFilasCambiado?.Invoke(this, EventArgs.Empty);
            return;
        }
        base.OnSorting(e);
        OrdenFilasCambiado?.Invoke(this, EventArgs.Empty);
    }

    // ---------- teclado: Alt+← → mueve la columna actual ----------

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var k = e.Key == Key.System ? e.SystemKey : e.Key;
        if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0 && (k == Key.Left || k == Key.Right) && CurrentColumn is { } col)
        {
            e.Handled = true;
            MoverJunto(col, k == Key.Left ? -1 : 1);
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    /// <summary>Mueve una columna junto a su vecina visible.</summary>
    public bool MoverJunto(DataGridColumn col, int sentido)
    {
        var cfg = DisposicionColumnas.MoverJuntoAVisible(Definiciones(), ConfigActual(), ClaveDe(col), sentido);
        if (cfg is null) return false;
        Guardar(cfg);
        Aplicar();
        Aviso?.Invoke(this, "Columna «" + TituloDe(col) + "» movida a la " + (sentido < 0 ? "izquierda." : "derecha."));
        return true;
    }

    /// <summary>Configuración que refleja el estado real (incluido el orden tras arrastrar).</summary>
    private ConfiguracionColumnas ConfigActual()
    {
        var cfg = _cfg ?? new ConfiguracionColumnas();
        var orden = Columns.OrderBy(c => c.DisplayIndex).Select(ClaveDe).ToList();
        cfg.Orden = orden.SequenceEqual(_ordenFabrica.Select(ClaveDe)) ? null : orden;
        return cfg;
    }

    /// <summary>Muestra u oculta una columna (respeta las siempre visibles y el mínimo de visibles).</summary>
    public bool CambiarVisible(DataGridColumn col, bool visible)
    {
        var cfg = DisposicionColumnas.CambiarVisible(Definiciones(), ConfigActual(), ClaveDe(col), visible);
        if (cfg is null)
        {
            Aviso?.Invoke(this, GetSiempreVisible(col) ? "«" + TituloDe(col) + "» siempre se muestra." : "Deja al menos " + DisposicionColumnas.MinimoVisibles + " columnas visibles.");
            return false;
        }
        Guardar(cfg);
        Aplicar();
        return true;
    }

    /// <summary>Vuelve al orden, las columnas visibles y los anchos de fábrica (solo para este operador).</summary>
    public void RestablecerColumnas()
    {
        Guardar(null);
        Aplicar();
        Aviso?.Invoke(this, "Columnas restablecidas: orden, visibles y anchos de fábrica.");
    }

    /// <summary>
    /// «Ajustar todas al contenido» (plataforma/ui-base 2026-11-12): primero las columnas de contenido fijo (fechas, códigos,
    /// importes, textos cortos) a su contenido, midiendo todas las filas y no solo las pintadas; después el espacio que sobra
    /// se reparte entre las de contenido variable (las de ancho proporcional, como el concepto, y las de textos largos, como
    /// las notas), en proporción a lo que piden. Si no caben, las variables se quedan en el ancho de su título y sale la barra.
    /// </summary>
    public void AjustarTodasAlContenido()
    {
        var visibles = Columns.Where(c => c.Visibility == Visibility.Visible).ToList();
        if (visibles.Count == 0) return;
        var pedido = visibles.ToDictionary(c => c, MedirColumna);
        bool Proporcional(DataGridColumn c) => c.Width.IsStar || _anchosFabrica.GetValueOrDefault(c).IsStar;
        var variables = visibles.Where(c => Proporcional(c) || pedido[c] > AnchoTextoLargo).ToList();
        var fijas = visibles.Except(variables).ToList();
        foreach (var c in fijas)
        {
            c.MinWidth = Math.Min(c.MinWidth, pedido[c]);
            c.Width = new DataGridLength(pedido[c]);
        }
        var filas = HeadersVisibility.HasFlag(DataGridHeadersVisibility.Row) ? RowHeaderActualWidth : 0;
        var disponible = Math.Max(0, (ActualWidth > 0 ? ActualWidth : 1200) - 24 - filas - fijas.Sum(c => pedido[c]));
        var anchos = RepartirVariables(variables.Select(c => (pedido[c], MedirTitulo(c))).ToList(), disponible);
        for (var i = 0; i < variables.Count; i++)
        {
            var c = variables[i];
            var ancho = anchos[i];
            c.MinWidth = Math.Min(c.MinWidth, ancho);
            // Las proporcionales siguen siéndolo, con ese peso, para seguir llenando la ventana al cambiar su tamaño.
            c.Width = Proporcional(c) ? new DataGridLength(ancho, DataGridLengthUnitType.Star) : new DataGridLength(ancho);
        }
        GuardarAnchos();
        Aviso?.Invoke(this, "Columnas ajustadas al contenido. «Restablecer columnas» vuelve a los anchos de fábrica.");
    }

    /// <summary>
    /// Reparto del espacio que dejan las columnas fijas entre las variables. Si sobra, cada una recibe lo que pide más una parte
    /// del sobrante en proporción a lo que pide. Si no cabe, a partes iguales: la que pide menos que su parte se queda con lo
    /// que pide y lo que deja se reparte entre las demás (así un concepto corto no se queda recortado porque unas notas sean muy
    /// largas); nunca menos que el título.
    /// </summary>
    public static IReadOnlyList<double> RepartirVariables(IReadOnlyList<(double Pedido, double Titulo)> variables, double disponible)
    {
        var total = variables.Sum(v => v.Pedido);
        if (total <= disponible)
            return variables.Select(v => Math.Floor(total <= 0 ? v.Titulo : v.Pedido + (disponible - total) * v.Pedido / total)).ToList();
        var anchos = new double[variables.Count];
        var resto = Math.Max(0, disponible);
        var orden = Enumerable.Range(0, variables.Count).OrderBy(i => variables[i].Pedido).ToList();
        for (var k = 0; k < orden.Count; k++)
        {
            var i = orden[k];
            var parte = resto / (orden.Count - k);
            var ancho = Math.Max(variables[i].Titulo, Math.Min(variables[i].Pedido, parte));
            anchos[i] = Math.Floor(ancho);
            resto = Math.Max(0, resto - ancho);
        }
        return anchos;
    }

    /// <summary>A partir de este ancho de contenido una columna se trata como de texto variable al ajustar.</summary>
    private const double AnchoTextoLargo = 320;

    /// <summary>Filas que se miden como mucho al ajustar (con más, las primeras).</summary>
    private const int FilasMedidas = 5000;

    /// <summary>Ancho que pide una columna: el mayor entre su título y el texto de sus celdas en todas las filas (hasta <see cref="FilasMedidas"/>).</summary>
    public double MedirColumna(DataGridColumn c)
    {
        var texto = TextoDe(c);
        var (familia, tamano, peso) = LetraDe(c);
        var tipo = new Typeface(familia, FontStyles.Normal, peso, FontStretches.Normal);
        var ppp = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var medidos = new Dictionary<string, double>(StringComparer.Ordinal);
        double maximo = 0;
        foreach (var fila in Items.Cast<object>().Where(o => o != CollectionView.NewItemPlaceholder).Take(FilasMedidas))
        {
            string? s;
            try { s = texto(fila); } catch (Exception) { s = null; }
            if (string.IsNullOrEmpty(s)) continue;
            if (!medidos.TryGetValue(s, out var w))
                medidos[s] = w = new FormattedText(s, Formato.Formatos.Cultura, FlowDirection.LeftToRight, tipo, tamano, Brushes.Black, ppp).WidthIncludingTrailingWhitespace;
            if (w > maximo) maximo = w;
        }
        return Math.Ceiling(Math.Max(MedirTitulo(c), maximo + 24));
    }

    /// <summary>Ancho del título de la columna con su relleno y el sitio de la flecha de orden.</summary>
    private double MedirTitulo(DataGridColumn c)
    {
        var tamano = TryFindResource("Letra.Secundaria") is double d ? d : FontSize;
        var ft = new FormattedText(TituloDe(c), Formato.Formatos.Cultura, FlowDirection.LeftToRight,
            new Typeface(FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), tamano, Brushes.Black, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return Math.Ceiling(ft.WidthIncludingTrailingWhitespace + 34);
    }

    /// <summary>Letra de las celdas de la columna: la de su estilo de celda (o los estilos en que se basa), o la de la tabla.</summary>
    private (FontFamily Familia, double Tamano, FontWeight Peso) LetraDe(DataGridColumn c)
    {
        FontFamily? familia = null;
        double? tamano = null;
        FontWeight? peso = null;
        for (var estilo = (c as DataGridBoundColumn)?.ElementStyle; estilo is not null; estilo = estilo.BasedOn)
            foreach (var s in estilo.Setters.OfType<Setter>())
            {
                if (s.Property == TextBlock.FontFamilyProperty && s.Value is FontFamily f) familia ??= f;
                else if (s.Property == TextBlock.FontSizeProperty && s.Value is double t) tamano ??= t;
                else if (s.Property == TextBlock.FontWeightProperty && s.Value is FontWeight w) peso ??= w;
            }
        return (familia ?? FontFamily, tamano ?? FontSize, peso ?? FontWeights.Normal);
    }

    /// <summary>Texto que pinta la celda de una fila: el valor de su Binding con su conversor, o el valor exportable con el formato de ProConta.</summary>
    private static Func<object, string?> TextoDe(DataGridColumn c)
    {
        if (c is DataGridBoundColumn { Binding: Binding b } && !string.IsNullOrEmpty(b.Path?.Path))
        {
            var leer = Lector(b.Path.Path);
            return fila =>
            {
                var v = leer(fila);
                if (b.Converter is { } conv) v = conv.Convert(v!, typeof(string), b.ConverterParameter, Formato.Formatos.Cultura);
                else if (!string.IsNullOrEmpty(b.StringFormat)) v = string.Format(Formato.Formatos.Cultura, b.StringFormat.Contains('{') ? b.StringFormat : "{0:" + b.StringFormat + "}", v);
                return Formatear(v);
            };
        }
        var lector = LectorDe(c);
        return fila => Formatear(lector(fila));
    }

    private static string? Formatear(object? v) => v switch
    {
        null => null,
        decimal d => Formato.Formatos.Importe(d),
        DateTime f => Formato.Formatos.Fecha(f),
        DateOnly f => Formato.Formatos.Fecha(f),
        _ => v.ToString(),
    };

    private static Func<object, object?> Lector(string ruta)
    {
        var partes = ruta.Split('.');
        return fila =>
        {
            object? v = fila;
            foreach (var p in partes)
            {
                if (v is null) return null;
                v = v.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance)?.GetValue(v);
            }
            return v;
        };
    }

    // ---------- menú contextual de la cabecera ----------

    protected override void OnPreviewMouseRightButtonUp(MouseButtonEventArgs e)
    {
        var cab = Ascendiente<DataGridColumnHeader>(e.OriginalSource as DependencyObject);
        if (cab is not null)
        {
            e.Handled = true;
            var menu = MenuCabecera(cab.Column);
            menu.PlacementTarget = cab;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
            return;
        }
        base.OnPreviewMouseRightButtonUp(e);
    }

    /// <summary>Construye el menú de la cabecera para una columna (null: zona libre de la cabecera).</summary>
    public ContextMenu MenuCabecera(DataGridColumn? col)
    {
        var m = new ContextMenu();
        if (col is not null)
        {
            var t = TituloDe(col);
            m.Items.Add(Opcion("Mover «" + t + "» a la izquierda", "Icono.prev", () => MoverJunto(col, -1), "Alt+←"));
            m.Items.Add(Opcion("Mover «" + t + "» a la derecha", "Icono.next", () => MoverJunto(col, 1), "Alt+→"));
            var ocultar = Opcion("Ocultar «" + t + "»", "Icono.hide", () => CambiarVisible(col, false));
            ocultar.IsEnabled = !GetSiempreVisible(col);
            if (!ocultar.IsEnabled) ToolTipService.SetShowOnDisabled(ocultar, true);
            ocultar.ToolTip = GetSiempreVisible(col) ? "Esta columna siempre se muestra." : "Oculta la columna (solo para ti). Se vuelve a mostrar desde «Columnas visibles».";
            m.Items.Add(ocultar);
            m.Items.Add(new Separator());
        }
        var vis = new MenuItem { Header = "Columnas visibles", ToolTip = "Elige qué columnas se muestran; algunas vienen ocultas de fábrica" };
        foreach (var c in _ordenFabrica.Where(c => !GetSiempreVisible(c)))
        {
            var it = new MenuItem { Header = TituloDe(c), IsCheckable = true, IsChecked = c.Visibility == Visibility.Visible, StaysOpenOnClick = true };
            var cc = c;
            it.Click += (_, _) => { if (!CambiarVisible(cc, it.IsChecked)) it.IsChecked = cc.Visibility == Visibility.Visible; };
            vis.Items.Add(it);
        }
        m.Items.Add(vis);
        m.Items.Add(Opcion("Ajustar todas al contenido", "Icono.funnel", AjustarTodasAlContenido));
        var rest = Opcion("Restablecer columnas", "Icono.undo", RestablecerColumnas);
        rest.ToolTip = "Vuelve al orden, las columnas visibles y los anchos de fábrica (solo para ti)";
        m.Items.Add(rest);
        m.Items.Add(new Separator());
        var exp = Opcion("Exportar a Excel", "Icono.xls", () => ExportarAExcel());
        exp.IsEnabled = Items.Count > 0;
        exp.ToolTip = Items.Count > 0 ? "Exporta todas las filas de la vista con las columnas visibles, en el mismo orden" : "No hay filas que exportar.";
        ToolTipService.SetShowOnDisabled(exp, true);
        m.Items.Add(exp);
        return m;
    }

    private MenuItem Opcion(string texto, string icono, Action accion, string? atajo = null)
    {
        var it = new MenuItem { Header = texto, InputGestureText = atajo ?? string.Empty };
        if (TryFindResource(icono) is Geometry g)
            it.Icon = new System.Windows.Shapes.Path { Data = g, Style = TryFindResource("Icono.Trazo14") as Style };
        it.Click += (_, _) => accion();
        return it;
    }

    // ---------- exportar a Excel ----------

    /// <summary>Columnas visibles en su orden, preparadas para exportar.</summary>
    public IReadOnlyList<ColumnaExportable> ColumnasExportables() =>
        Columns.Where(c => c.Visibility == Visibility.Visible && c.Header is not null)
               .OrderBy(c => c.DisplayIndex)
               .Select(c => new ColumnaExportable(TituloDe(c), LectorDe(c)))
               .ToList();

    private static Func<object, object?> LectorDe(DataGridColumn c)
    {
        var ruta = GetRutaExportar(c)
                   ?? ((c as DataGridBoundColumn)?.Binding as Binding)?.Path?.Path
                   ?? c.SortMemberPath;
        if (string.IsNullOrEmpty(ruta)) return _ => null;
        var partes = ruta.Split('.');
        return fila =>
        {
            object? v = fila;
            foreach (var p in partes)
            {
                if (v is null) return null;
                v = v.GetType().GetProperty(p, BindingFlags.Public | BindingFlags.Instance)?.GetValue(v);
            }
            return v;
        };
    }

    /// <summary>Exporta todas las filas de la vista (no solo las pintadas). Sin ruta, pregunta dónde guardar.</summary>
    public bool ExportarAExcel(string? ruta = null)
    {
        if (ruta is null)
        {
            var nombre = string.Concat((TituloExportacion ?? "ProConta").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
            var dlg = new SaveFileDialog { Title = "Exportar a Excel", Filter = "Libro de Excel (*.xlsx)|*.xlsx", FileName = nombre + ".xlsx", AddExtension = true };
            if (dlg.ShowDialog(Window.GetWindow(this)) != true) return false;
            ruta = dlg.FileName;
        }
        try
        {
            var filas = Items.Cast<object>().Where(o => o != CollectionView.NewItemPlaceholder);
            var n = ExportadorExcel.Exportar(ruta, ColumnasExportables(), filas, TituloExportacion ?? "Datos", TituloExportacion);
            Aviso?.Invoke(this, "Exportadas " + Formato.Formatos.Plural(n, "fila", "filas") + " a " + System.IO.Path.GetFileName(ruta) + ".");
            return true;
        }
        catch (IOException ex)
        {
            Aviso?.Invoke(this, "No se ha podido guardar " + System.IO.Path.GetFileName(ruta) + ": " + ex.Message + " Si el libro está abierto en Excel, ciérralo y vuelve a exportar.");
            return false;
        }
    }

    private static T? Ascendiente<T>(DependencyObject? o) where T : DependencyObject
    {
        while (o is not null && o is not T)
            o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);
        return o as T;
    }
}
