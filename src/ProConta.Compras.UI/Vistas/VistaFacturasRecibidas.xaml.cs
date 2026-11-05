using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Compras.UI.VistaModelos;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Preferencias;

namespace ProConta.Compras.UI.Vistas;

/// <summary>Anchos de la lista y de la ficha guardados por operador (null: ancho de fábrica).</summary>
public sealed record AnchosCompras(double? Lista, double? Ficha);

/// <summary>
/// Vista de Facturas recibidas (prototipo sources/maquetas/Contenedor, escenario «compras», versión 26): lista, ficha y
/// visor con dos separadores que dan o quitan ancho al visor, visor desacoplable a otra ventana y captura de campos
/// seleccionando texto en el PDF.
/// </summary>
public partial class VistaFacturasRecibidas : UserControl
{
    private const string ClaveAnchos = "compras.recibidas.anchos";
    private const double MinLista = 260, MinFicha = 420, MinVisor = 320, Paso = 40;
    private TextBox? _campoCaptura;
    private FichaCompraVM? _fichaObservada;
    private VentanaVisor? _ventanaVisor;
    private bool _listaOculta;
    private double? _lista, _ficha;

    public VistaFacturasRecibidas()
    {
        EnfocarBusquedaCommand = new ComandoDelegado(() => { CajaBusqueda.Focus(); CajaBusqueda.SelectAll(); });
        InitializeComponent();
        var guardados = Leer();
        (_lista, _ficha) = (guardados?.Lista, guardados?.Ficha);
        Loaded += (_, _) => AplicarAnchos();
        Zonas.SizeChanged += (_, _) => AplicarAnchos();
        Visor.TextoSeleccionado += (_, texto) => Capturar(texto);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is FacturasRecibidasVM viejo) viejo.PropertyChanged -= AlCambiarModelo;
            if (e.NewValue is FacturasRecibidasVM nuevo) nuevo.PropertyChanged += AlCambiarModelo;
            ObservarFicha();
        };
        Unloaded += (_, _) => _ventanaVisor?.Close();
    }

    public ICommand EnfocarBusquedaCommand { get; }

    private FacturasRecibidasVM? Vm => DataContext as FacturasRecibidasVM;

    // ---------- tabla ----------

    private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Vm?.CambiarSeleccion(Tabla.SelectedItems.OfType<FilaCompraVM>().ToList());

    /// <summary>El clic derecho sobre una fila no seleccionada la selecciona a ella sola; sobre una ya seleccionada conserva la selección.</summary>
    private void Tabla_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = VisualTreeHelper.GetParent(x))
        {
            if (x is not DataGridRow fila) continue;
            if (!fila.IsSelected) Tabla.SelectedItem = fila.Item;
            return;
        }
    }

    /// <summary>Doble clic en una fila: abre su PDF con el programa asociado de Windows.</summary>
    private void Tabla_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        for (var x = e.OriginalSource as DependencyObject; x is not null; x = VisualTreeHelper.GetParent(x))
        {
            if (x is not DataGridRow) continue;
            if (Vm?.AbrirPdfCommand is { } abrir && abrir.CanExecute(null)) abrir.Execute(null);
            e.Handled = true;
            return;
        }
    }

    private void BotonFiltro_Click(object sender, RoutedEventArgs e) => PopupFiltro.IsOpen = !PopupFiltro.IsOpen;

    // ---------- captura y resaltado ----------

    /// <summary>El último campo capturable con el foco recibe el texto que se selecciona en el PDF.</summary>
    private void Campo_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is not TextBox t) return;
        _campoCaptura = t;
        Resaltar();
    }

    private void Capturar(string texto)
    {
        if (_campoCaptura is not { IsReadOnly: false, IsLoaded: true } campo || Vm?.Ficha is not { Editable: true }) return;
        var tipo = campo.Tag as string;
        var valor = tipo switch
        {
            "nif" => new string(texto.Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant(),
            "base" or "cuota" or "total" => new string(texto.Where(c => char.IsDigit(c) || c is ',' or '.' or '-').ToArray()),
            _ => texto.Trim()
        };
        if (valor.Length == 0) return;
        campo.Text = valor;
        campo.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        Resaltar();
    }

    private void AlCambiarModelo(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FacturasRecibidasVM.Ficha)) ObservarFicha();
    }

    private void ObservarFicha()
    {
        if (_fichaObservada is not null) _fichaObservada.PropertyChanged -= AlCambiarFicha;
        _fichaObservada = Vm?.Ficha;
        if (_fichaObservada is not null) _fichaObservada.PropertyChanged += AlCambiarFicha;
        _campoCaptura = null;
        Resaltar();
    }

    private void AlCambiarFicha(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FichaCompraVM.Nif) or nameof(FichaCompraVM.Numero) or nameof(FichaCompraVM.Fecha) or nameof(FichaCompraVM.Total)) Resaltar();
    }

    /// <summary>Envía al visor (o a la ventana desacoplada) los valores de la ficha para resaltar su origen en el PDF.</summary>
    private void Resaltar()
    {
        var f = Vm?.Ficha;
        IReadOnlyList<ValorResaltado> valores = f is null ? [] :
        [
            new("nif", f.Nif), new("numero", f.Numero), .. VariantesFecha(f.Fecha).Select(t => new ValorResaltado("fecha", t)), new("total", f.Total)
        ];
        var activo = _campoCaptura?.Tag as string;
        Visor.Resaltar(valores, activo);
        _ventanaVisor?.Visor.Resaltar(valores, activo);
    }

    private static readonly System.Globalization.CultureInfo[] CulturasFecha =
        [System.Globalization.CultureInfo.GetCultureInfo("es-ES"), System.Globalization.CultureInfo.GetCultureInfo("ca-ES"), System.Globalization.CultureInfo.GetCultureInfo("en-GB")];

    private static readonly string[] FormatosFecha =
        ["dd/MM/yyyy", "d/M/yyyy", "dd/MM/yy", "dd-MMM-yyyy", "d-MMM-yyyy", "d 'de' MMMM 'de' yyyy", "d MMMM yyyy", "MMMM d, yyyy", "yyyy-MM-dd"];

    /// <summary>
    /// La fecha de la ficha escrita como suele aparecer en las facturas (10/07/2026, 10-jul-2026, 10 de julio de 2026…),
    /// para resaltarla en el PDF aunque no esté con barras.
    /// </summary>
    internal static IReadOnlyList<string> VariantesFecha(string? fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha)) return [];
        if (!DateTime.TryParse(fecha, System.Globalization.CultureInfo.GetCultureInfo("es-ES"), System.Globalization.DateTimeStyles.None, out var d)) return [fecha];
        return CulturasFecha.SelectMany(c => FormatosFecha.Select(f => d.ToString(f, c))).Prepend(fecha).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    // ---------- anchos de la lista y la ficha ----------

    private static AnchosCompras? Leer()
    {
        try { return PreferenciasOperador.Actual.Leer<AnchosCompras>(ClaveAnchos); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    private void Guardar()
    {
        if (_lista is null && _ficha is null) PreferenciasOperador.Actual.Borrar(ClaveAnchos);
        else PreferenciasOperador.Actual.Guardar(ClaveAnchos, new AnchosCompras(_lista, _ficha));
    }

    private bool VisorAcoplado => _ventanaVisor is null;

    /// <summary>De fábrica: a partir de 2000 px de ancho, ficha en dos columnas; por debajo, en una.</summary>
    private double FichaFabrica => Zonas.ActualWidth >= 2000 ? 860 : 440;
    private double ListaFabrica => Zonas.ActualWidth >= 2000 ? 560 : 420;

    private void AplicarAnchos()
    {
        if (!IsLoaded || Zonas.ActualWidth <= 0) return;
        var total = Zonas.ActualWidth - SepLista.ActualWidth - SepFicha.ActualWidth;
        var ficha = Math.Max(MinFicha, _ficha ?? FichaFabrica);
        ColFicha.Width = new GridLength(ficha);
        if (_listaOculta)
        {
            ColLista.Width = new GridLength(0);
            ColSepLista.Width = new GridLength(0);
        }
        else
        {
            ColSepLista.Width = GridLength.Auto;
            if (VisorAcoplado)
            {
                var lista = Math.Max(MinLista, Math.Min(_lista ?? ListaFabrica, total - ficha - MinVisor));
                ColLista.Width = new GridLength(lista);
            }
            else ColLista.Width = new GridLength(1, GridUnitType.Star);
        }
        ColVisor.Width = VisorAcoplado ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        ColSepFicha.Width = VisorAcoplado ? GridLength.Auto : new GridLength(0);
        ZonaVisor.Visibility = VisorAcoplado ? Visibility.Visible : Visibility.Collapsed;
        SepFicha.Visibility = VisorAcoplado ? Visibility.Visible : Visibility.Collapsed;
        SepLista.Visibility = _listaOculta || !VisorAcoplado ? Visibility.Collapsed : Visibility.Visible;
    }

    private double Limite(double ancho, bool esLista)
    {
        var total = Zonas.ActualWidth - SepLista.ActualWidth - SepFicha.ActualWidth;
        var otro = esLista ? ColFicha.ActualWidth : (_listaOculta ? 0 : ColLista.ActualWidth);
        var max = total - otro - (VisorAcoplado ? MinVisor : 0);
        return Math.Round(Math.Max(esLista ? MinLista : MinFicha, Math.Min(max, ancho)));
    }

    private void SepLista_DragDelta(object sender, DragDeltaEventArgs e)
    {
        _lista = Limite(ColLista.ActualWidth + e.HorizontalChange, esLista: true);
        AplicarAnchos();
    }

    private void SepFicha_DragDelta(object sender, DragDeltaEventArgs e)
    {
        _ficha = Limite(ColFicha.ActualWidth + e.HorizontalChange, esLista: false);
        AplicarAnchos();
    }

    private void Separador_DragCompleted(object sender, DragCompletedEventArgs e) => Guardar();

    private void SepLista_MouseDoubleClick(object sender, MouseButtonEventArgs e) { _lista = null; AplicarAnchos(); Guardar(); }
    private void SepFicha_MouseDoubleClick(object sender, MouseButtonEventArgs e) { _ficha = null; AplicarAnchos(); Guardar(); }

    private void SepLista_KeyDown(object sender, KeyEventArgs e) => Teclear(e, esLista: true);
    private void SepFicha_KeyDown(object sender, KeyEventArgs e) => Teclear(e, esLista: false);

    /// <summary>Con el foco en un separador: ← y → cambian el ancho 40 px; Inicio vuelve al de fábrica.</summary>
    private void Teclear(KeyEventArgs e, bool esLista)
    {
        double? nuevo = e.Key switch
        {
            Key.Left => Limite((esLista ? ColLista : ColFicha).ActualWidth - Paso, esLista),
            Key.Right => Limite((esLista ? ColLista : ColFicha).ActualWidth + Paso, esLista),
            Key.Home => null,
            _ => -1
        };
        if (nuevo == -1) return;
        if (esLista) _lista = nuevo; else _ficha = nuevo;
        AplicarAnchos();
        Guardar();
        e.Handled = true;
    }

    private void BotonLista_Click(object sender, RoutedEventArgs e)
    {
        _listaOculta = !_listaOculta;
        BotonLista.Content = _listaOculta ? "Mostrar lista" : "Ocultar lista";
        AplicarAnchos();
    }

    // ---------- visor desacoplado ----------

    /// <summary>Lleva el visor a una ventana aparte: todo su hueco pasa a la lista; al cerrarla, vuelve a su sitio.</summary>
    private void Desacoplar_Click(object sender, RoutedEventArgs e)
    {
        if (_ventanaVisor is not null) { _ventanaVisor.Activate(); return; }
        _ventanaVisor = new VentanaVisor { DataContext = DataContext, Owner = Window.GetWindow(this) };
        _ventanaVisor.Visor.TextoSeleccionado += (_, texto) => Capturar(texto);
        _ventanaVisor.Closed += (_, _) => { _ventanaVisor = null; AplicarAnchos(); Resaltar(); };
        _ventanaVisor.Show();
        AplicarAnchos();
        Resaltar();
    }
}
