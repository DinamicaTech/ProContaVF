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

    /// <summary>Ajusta el ancho de todas las columnas visibles de ancho fijo a su contenido y lo guarda.</summary>
    public void AjustarTodasAlContenido()
    {
        var fijas = Columns.Where(c => c.Visibility == Visibility.Visible && !c.Width.IsStar).ToList();
        foreach (var c in fijas) c.Width = new DataGridLength(1, DataGridLengthUnitType.SizeToCells);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            foreach (var c in fijas) c.Width = new DataGridLength(Math.Clamp(c.ActualWidth, 48, 640));
            GuardarAnchos();
        }));
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
