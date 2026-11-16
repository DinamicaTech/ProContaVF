using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.UI.Vistas;

/// <summary>Diálogos de remesas y devoluciones de recibos (norma43/remesas).</summary>
public sealed partial class DialogosLineasN43
{
    /// <summary>«Explotar remesa»: facturas con su casilla y su importe cobrado; «Explotar» solo se activa si cuadra.</summary>
    public bool ExplotarRemesa(RemesaN43VM vm) => CrearExplotarRemesa(vm, Activa()).ShowDialog() == true;

    /// <summary>«Conciliar facturas devueltas»: recibos de la remesa elegida; «Conciliar» solo se activa si cuadra.</summary>
    public bool ConciliarDevolucion(DevolucionN43VM vm) => CrearConciliarDevolucion(vm, Activa()).ShowDialog() == true;

    /// <summary>Construye el diálogo «Explotar remesa» sin abrirlo (también para capturas fuera de pantalla).</summary>
    public static DialogoProConta CrearExplotarRemesa(RemesaN43VM vm, Window? propietario)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var d = Dialogo("Explotar remesa", propietario, vm);
        var (panel, apagado) = Cabecera(d, vm.Titulo, vm.Concepto, vm.Explicacion);
        double[] anchos = [24, 80, 84, 40, 200, double.NaN, 96, 110];
        string[] nombres = ["", "Factura", "Fecha", "Día", "Cliente", "Descripción", "Importe", "Cobrado"];
        panel.Children.Add(Titulos(d, anchos, nombres, apagado, [6, 7]));
        foreach (var f in vm.Filas)
        {
            var g = Rejilla(anchos);
            Casilla(g, f, nameof(FilaRemesaN43VM.Marcada), f.Ayuda);
            Celda(d, g, 1, f.Numero, mono: true);
            Celda(d, g, 2, f.Fecha, mono: true);
            Celda(d, g, 3, f.Dia, numero: true, mono: true);
            Celda(d, g, 4, f.Cliente);
            Celda(d, g, 5, f.Descripcion);
            Celda(d, g, 6, f.Importe, numero: true, mono: true);
            var cobrado = new TextBox
            {
                Style = (Style)d.FindResource("Campo"), TextAlignment = TextAlignment.Right, FontFamily = (FontFamily)d.FindResource("Fuente.Mono"), DataContext = f,
                ToolTip = "Lo que cobró el banco por esta factura. Si es distinto del importe, la diferencia queda pendiente en la cuenta del cliente", Margin = new Thickness(0, 1, 0, 1),
            };
            cobrado.SetBinding(TextBox.TextProperty, new Binding(nameof(FilaRemesaN43VM.CobradoTexto)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            Grid.SetColumn(cobrado, 7);
            g.Children.Add(cobrado);
            g.ToolTip = f.Ayuda;
            if (f.PuedeAbrir) g.MouseLeftButtonDown += (_, e) => { if (e.ClickCount == 2) { f.Abrir(); e.Handled = true; } };
            panel.Children.Add(g);
        }
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Botones(d, nameof(RemesaN43VM.Cuadre), nameof(RemesaN43VM.Cuadra), "Explotar",
            "Crea un recibo por factura marcada, llevado a la cuenta de su cliente, y deja las facturas cobradas", "Cierra sin explotar nada");
        return d;
    }

    /// <summary>Construye el diálogo «Conciliar facturas devueltas» sin abrirlo.</summary>
    public static DialogoProConta CrearConciliarDevolucion(DevolucionN43VM vm, Window? propietario)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var d = Dialogo("Conciliar facturas devueltas", propietario, vm);
        var (panel, apagado) = Cabecera(d, vm.Titulo, vm.Concepto, null);
        var explicacion = new TextBlock { Foreground = apagado, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) };
        explicacion.SetBinding(TextBlock.TextProperty, nameof(DevolucionN43VM.Explicacion));
        panel.Children.Add(explicacion);
        var remesas = new ComboBox { Width = 420, HorizontalAlignment = HorizontalAlignment.Left, DisplayMemberPath = "Texto", Margin = new Thickness(0, 0, 0, 8),
            ToolTip = "Remesa de la que salen los recibos devueltos: por defecto, la más cercana anterior a la devolución" };
        remesas.SetBinding(ItemsControl.ItemsSourceProperty, nameof(DevolucionN43VM.Remesas));
        remesas.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty, new Binding(nameof(DevolucionN43VM.Remesa)) { Mode = BindingMode.TwoWay });
        panel.Children.Add(remesas);
        var error = new TextBlock { Foreground = (Brush)d.FindResource("Pincel.RojoError"), TextWrapping = TextWrapping.Wrap };
        error.SetBinding(TextBlock.TextProperty, nameof(DevolucionN43VM.Error));
        panel.Children.Add(error);
        double[] anchos = [24, 90, 240, double.NaN, 110];
        panel.Children.Add(Titulos(d, anchos, ["", "Factura", "Cliente", "Descripción", "Importe"], apagado, [4]));
        // Las filas se regeneran al cambiar de remesa: se construyen en código cada vez.
        var contenedor = new ContentControl();
        panel.Children.Add(contenedor);
        void Rehacer()
        {
            var lista = new StackPanel();
            foreach (var f in vm.Filas)
            {
                var g = Rejilla(anchos);
                Casilla(g, f, nameof(FilaDevolucionN43VM.Marcada), f.Ayuda);
                Celda(d, g, 1, f.Numero, mono: true);
                Celda(d, g, 2, f.Cliente);
                Celda(d, g, 3, f.Descripcion);
                Celda(d, g, 4, f.Importe, numero: true, mono: true);
                g.ToolTip = f.Ayuda;
                lista.Children.Add(g);
            }
            contenedor.Content = lista;
        }
        vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(DevolucionN43VM.Filas)) d.Dispatcher.Invoke(Rehacer); };
        Rehacer();
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Botones(d, nameof(DevolucionN43VM.Cuadre), nameof(DevolucionN43VM.Cuadra), "Conciliar",
            "Lleva los recibos marcados a la cuenta de su cliente (con varios, un recibo devuelto por factura) y deja las facturas pendientes de cobro", "Cierra sin conciliar nada");
        return d;
    }

    /// <summary>«Pago de dividendos»: socios activos con su casilla, su % y el importe; «Pagar» solo se activa si cuadra.</summary>
    public bool PagarDividendos(PagoDividendosN43VM vm) => CrearPagarDividendos(vm, Activa()).ShowDialog() == true;

    /// <summary>Construye el diálogo «Pago de dividendos» sin abrirlo (también para capturas fuera de pantalla).</summary>
    public static DialogoProConta CrearPagarDividendos(PagoDividendosN43VM vm, Window? propietario)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var d = Dialogo("Pago de dividendos", propietario, vm);
        d.Width = 820;
        d.Height = 460;
        var (panel, apagado) = Cabecera(d, vm.Titulo, vm.Concepto, vm.Explicacion);
        double[] anchos = [24, double.NaN, 80, 90, 130];
        panel.Children.Add(Titulos(d, anchos, ["", "Socio", "%", "Cuenta", "Importe"], apagado, [2, 4]));
        foreach (var f in vm.Filas)
        {
            var g = Rejilla(anchos);
            Casilla(g, f, nameof(FilaPagoDividendosN43VM.Marcada), f.Ayuda);
            if (!f.Elegible) { g.Children[^1].IsEnabled = false; ToolTipService.SetShowOnDisabled(g.Children[^1], true); }
            Celda(d, g, 1, f.Nombre);
            Celda(d, g, 2, f.Participacion, numero: true, mono: true);
            Celda(d, g, 3, f.Cuenta, mono: true);
            var importe = new TextBox
            {
                Style = (Style)d.FindResource("Campo"), TextAlignment = TextAlignment.Right, FontFamily = (FontFamily)d.FindResource("Fuente.Mono"), DataContext = f,
                ToolTip = "Lo que cobra este socio con el cargo. Por defecto, el cargo repartido entre los marcados según su participación", Margin = new Thickness(0, 1, 0, 1),
            };
            importe.SetBinding(TextBox.TextProperty, new Binding(nameof(FilaPagoDividendosN43VM.ImporteTexto)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            importe.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(FilaPagoDividendosN43VM.Marcada)));
            Grid.SetColumn(importe, 4);
            g.Children.Add(importe);
            g.ToolTip = f.Ayuda;
            panel.Children.Add(g);
        }
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Botones(d, nameof(PagoDividendosN43VM.Cuadre), nameof(PagoDividendosN43VM.Cuadra), "Pagar",
            "Con un socio, lleva la línea a su 526; con varios, la desglosa en una línea por socio con su importe y su 526", "Cierra sin cambiar nada");
        return d;
    }

    /// <summary>«Ajustes de importe» tras explotar una remesa con facturas cobradas por otro importe.</summary>
    public bool AjustarImportes(AjustesRemesaN43VM vm) => CrearAjustarImportes(vm, Activa()).ShowDialog() == true;

    /// <summary>Construye el diálogo «Ajustes de importe» sin abrirlo.</summary>
    public static DialogoProConta CrearAjustarImportes(AjustesRemesaN43VM vm, Window? propietario)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var d = Dialogo("Ajustes de importe", propietario, vm);
        d.Height = 520;
        var (panel, apagado) = Cabecera(d, "Facturas cobradas por otro importe", "", vm.Explicacion);
        foreach (var f in vm.Filas)
        {
            var bloque = new StackPanel { Margin = new Thickness(0, 8, 0, 4) };
            bloque.Children.Add(new TextBlock
            {
                Text = $"{f.Numero} · factura {f.Importe} € · cobrado {f.Cobrado} € · diferencia {f.Diferencia} €",
                Style = (Style)d.FindResource("Texto.Seccion"),
            });
            if (f.Datos.CobraMas)
            {
                bloque.Children.Add(Opcion(f, nameof(FilaAjusteN43VM.ActualizarProgramada), f.TextoProgramada, f.PuedeProgramada,
                    "Las facturas que genere a partir de ahora saldrán por el importe que cobró el banco"));
                bloque.Children.Add(Opcion(f, nameof(FilaAjusteN43VM.EmitirDiferencia), f.TextoDiferencia, f.PuedeDiferencia,
                    "Se emite y valida con la fecha de hoy, el mismo cliente, IVA y retención, y queda cobrada con el mismo recibo"));
            }
            else
            {
                var cliente = new RadioButton { Content = "Cobrársela al cliente: la diferencia queda pendiente en su cuenta", GroupName = $"menor{f.Datos.IdFactura}", IsChecked = !f.Asumir, DataContext = f,
                    ToolTip = "No se emite nada: la diferencia se cobra después (otra remesa o «Buscar por importe»)", Margin = new Thickness(0, 4, 0, 0) };
                var asumir = new RadioButton { Content = f.TextoAsumir, GroupName = $"menor{f.Datos.IdFactura}", IsChecked = f.Asumir, DataContext = f, IsEnabled = f.PuedeAsumir,
                    ToolTip = "La empresa asume la diferencia: la factura se anula con su rectificativa completa y se emite otra igual por lo que cobró el banco, las dos con el mismo recibo",
                    Margin = new Thickness(0, 2, 0, 0) };
                ToolTipService.SetShowOnDisabled(asumir, true);
                asumir.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(FilaAjusteN43VM.Asumir)) { Mode = BindingMode.TwoWay });
                bloque.Children.Add(cliente);
                bloque.Children.Add(asumir);
            }
            panel.Children.Add(bloque);
        }
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var aceptar = new Button { Content = "Aplicar", Style = (Style)d.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Hace lo marcado factura a factura y enseña un resumen con lo hecho y lo que falte" };
        var cancelar = new Button { Content = "Ahora no", IsCancel = true, ToolTip = "No hace nada más: las diferencias quedan en la cuenta de cada cliente" };
        aceptar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        _ = apagado;
        return d;
    }

    private static CheckBox Opcion(object fila, string propiedad, string texto, bool habilitada, string ayuda)
    {
        var c = new CheckBox { Content = new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap }, DataContext = fila, IsEnabled = habilitada, ToolTip = ayuda, Margin = new Thickness(0, 4, 0, 0) };
        c.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(propiedad) { Mode = BindingMode.TwoWay });
        if (!habilitada) ToolTipService.SetShowOnDisabled(c, true);
        return c;
    }

    // ---------- piezas comunes ----------

    private static DialogoProConta Dialogo(string titulo, Window? propietario, object vm) => new()
    {
        Title = titulo, Owner = propietario, Width = 1050, Height = 620, MinWidth = 720, MinHeight = 340,
        SizeToContent = SizeToContent.Manual, Redimensionable = true, DataContext = vm,
    };

    private static (StackPanel Panel, Brush Apagado) Cabecera(DialogoProConta d, string titulo, string concepto, string? explicacion)
    {
        var apagado = (Brush)d.FindResource("Pincel.Apagado");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = titulo, Style = (Style)d.FindResource("Texto.Seccion") });
        panel.Children.Add(new TextBlock { Text = concepto, Foreground = apagado, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = concepto });
        if (explicacion is not null)
            panel.Children.Add(new TextBlock { Text = explicacion, Foreground = apagado, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) });
        return (panel, apagado);
    }

    private static Grid Rejilla(double[] anchos)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        foreach (var a in anchos)
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(a) ? new GridLength(1, GridUnitType.Star) : new GridLength(a) });
        return g;
    }

    private static Grid Titulos(DialogoProConta d, double[] anchos, string[] nombres, Brush apagado, int[] numericas)
    {
        var g = Rejilla(anchos);
        g.Margin = new Thickness(0, 6, 0, 2);
        for (var i = 0; i < nombres.Length; i++)
            Celda(d, g, i, nombres[i], numero: numericas.Contains(i), color: apagado).FontSize = (double)d.FindResource("Letra.Pequena");
        return g;
    }

    private static TextBlock Celda(DialogoProConta d, Grid g, int col, string texto, bool numero = false, bool mono = false, Brush? color = null)
    {
        var t = new TextBlock
        {
            Text = texto, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0),
            TextAlignment = numero ? TextAlignment.Right : TextAlignment.Left,
        };
        if (mono) t.FontFamily = (FontFamily)d.FindResource("Fuente.Mono");
        if (color is not null) t.Foreground = color;
        Grid.SetColumn(t, col);
        g.Children.Add(t);
        return t;
    }

    private static void Casilla(Grid g, object fila, string propiedad, string ayuda)
    {
        var c = new CheckBox { DataContext = fila, VerticalAlignment = VerticalAlignment.Center, ToolTip = ayuda };
        c.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(propiedad) { Mode = BindingMode.TwoWay });
        Grid.SetColumn(c, 0);
        g.Children.Add(c);
    }

    private static void Botones(DialogoProConta d, string cuadre, string cuadra, string textoAceptar, string ayudaAceptar, string ayudaCancelar)
    {
        var resumen = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Foreground = (Brush)d.FindResource("Pincel.Apagado"), TextWrapping = TextWrapping.Wrap, MaxWidth = 640 };
        resumen.SetBinding(TextBlock.TextProperty, cuadre);
        var aceptar = new Button { Content = textoAceptar, Style = (Style)d.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, ToolTip = ayudaAceptar };
        aceptar.SetBinding(UIElement.IsEnabledProperty, cuadra);
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = ayudaCancelar };
        aceptar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { resumen, cancelar, aceptar } };
    }
}
