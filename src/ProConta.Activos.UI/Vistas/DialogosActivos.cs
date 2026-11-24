using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Controles;

namespace ProConta.Activos.UI.Vistas;

/// <summary>Diálogos de la pantalla de activos con <see cref="DialogoProConta"/> y <see cref="ServicioMensajes"/>.</summary>
public sealed class DialogosActivos(Func<Window?> propietario) : IDialogosActivos
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, opciones);

    public void Error(string titulo, string mensaje) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, null, new OpcionMensaje("Entendido", "Cierra este aviso", Principal: true, Cancelar: true));

    public string? GuardarExcel(string nombrePropuesto)
    {
        var limpio = string.Concat(nombrePropuesto.Split(System.IO.Path.GetInvalidFileNameChars()));
        var d = new SaveFileDialog { Title = "Guardar en Excel", Filter = "Libro de Excel (*.xlsx)|*.xlsx", FileName = limpio, AddExtension = true, DefaultExt = ".xlsx" };
        return d.ShowDialog(propietario()) == true ? d.FileName : null;
    }

    public void AbrirFichero(string ruta)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Error("Excel guardado", $"Lo he guardado en {ruta}, pero no he podido abrirlo: {ex.Message} Ábrelo desde el Explorador.");
        }
    }

    public void AbrirEnNavegador(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Error("No he podido abrir el navegador", $"{ex.Message} Copia esta dirección en el navegador: {url}");
        }
    }

    /// <summary>«Nueva compra…» o «Vender…» de un activo financiero: Cancelar y «Guardar la compra» (o la venta).</summary>
    public bool NuevaCompra(NuevaCompraVM compra)
    {
        var d = new DialogoProConta { Title = compra.Titulo, Owner = propietario(), SizeToContent = SizeToContent.WidthAndHeight };
        var panel = new StackPanel { Width = 720 };
        panel.Children.Add(new TextBlock { Text = compra.Cabecera, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(new TextBlock { Text = compra.Situacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        if (compra.SinSimbolo)
            panel.Children.Add(new TextBlock
            {
                Text = "Este activo no tiene símbolo de Yahoo ni de EODHD, así que el precio no se puede consultar: escríbelo a mano.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
            });
        panel.Children.Add(new VistaFormularioCompra { DataContext = compra.Compra });
        var errores = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent,
            Margin = new Thickness(0, 10, 0, 0), ToolTip = "Lo que falta o está mal escrito. Puedes seleccionarlo y copiarlo" };
        errores.SetBinding(TextBox.TextProperty, new System.Windows.Data.Binding(nameof(NuevaCompraVM.Errores)) { Source = compra, Mode = System.Windows.Data.BindingMode.OneWay });
        errores.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(NuevaCompraVM.HayErrores))
            { Source = compra, Converter = new ProConta.UI.Base.Formato.VisibleSiConverter() });
        errores.Foreground = (System.Windows.Media.Brush)d.FindResource("Pincel.RojoSaldo");
        panel.Children.Add(errores);
        d.Content = panel;
        var guardar = new Button { Content = compra.TextoGuardar, Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0),
            Command = compra.GuardarCommand, ToolTip = compra.EsVenta
                ? "Anota la venta en el activo: resta sus participaciones y su coste medio; si no le quedan, pasa a inactivo. No graba ningún asiento (sale del movimiento del banco)"
                : "Anota la compra en el activo: suma sus participaciones y su coste. No graba ningún asiento" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin anotar nada" };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, guardar } };
        compra.Guardada += (_, _) => d.DialogResult = true;
        return d.ShowDialog() == true;
    }

    /// <summary>Un importe en euros con su explicación; Aceptar solo si se lee como importe mayor que cero.</summary>
    public decimal? PedirImporte(string titulo, string explicacion, decimal actual)
    {
        var d = new DialogoProConta { Title = titulo, Owner = propietario(), SizeToContent = SizeToContent.WidthAndHeight };
        var panel = new StackPanel { Width = 480 };
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var caja = new TextBox { Text = ProConta.UI.Base.Formato.Formatos.Numero(actual), Width = 140, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Importe total en euros, con dos decimales (por ejemplo 1.250,40)" };
        panel.Children.Add(caja);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed,
            Foreground = (System.Windows.Media.Brush)d.FindResource("Pincel.RojoSaldo") };
        panel.Children.Add(error);
        d.Content = panel;
        decimal? valor = null;
        var aceptar = new Button { Content = "Guardar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Guarda este importe" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aceptar.Click += (_, _) =>
        {
            if (ProConta.UI.Base.Formato.Formatos.LeerImporte(caja.Text, out var v) && v > 0m) { valor = v; d.DialogResult = true; return; }
            error.Text = "Escribe un importe mayor que cero, por ejemplo 1.250,40.";
            error.Visibility = Visibility.Visible;
        };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => { caja.Focus(); caja.SelectAll(); };
        return d.ShowDialog() == true ? valor : null;
    }

    /// <summary>Lista de centros con buscador, sangrados por nivel; doble clic o Intro eligen.</summary>
    public CentroParaActivo? ElegirCentro(string titulo, string explicacion, IReadOnlyList<CentroParaActivo> centros, int? actual, bool soloImputables)
    {
        var d = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 560, Height = 560, Redimensionable = true };
        var panel = new DockPanel();
        var ayuda = new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(ayuda, Dock.Top);
        panel.Children.Add(ayuda);
        var buscar = new TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = "Escribe parte del código o del nombre del centro para filtrar la lista" };
        DockPanel.SetDock(buscar, Dock.Top);
        panel.Children.Add(buscar);
        var minimo = centros.Count == 0 ? 1 : centros.Min(c => c.Nivel);
        var lista = new ListBox
        {
            DisplayMemberPath = nameof(FilaCentro.Texto), ToolTip = soloImputables ? "Centros activos e imputables de la sociedad. Doble clic o Intro: elegirlo" : "Centros activos de la sociedad. Doble clic o Intro: elegirlo",
        };
        var filas = centros.Select(c => new FilaCentro(c, new string(' ', Math.Max(0, c.Nivel - minimo) * 4) + c.Texto)).ToList();
        void Filtrar()
        {
            var q = buscar.Text.Trim();
            lista.ItemsSource = q.Length == 0 ? filas : filas.Where(f => f.Centro.Texto.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
        Filtrar();
        lista.SelectedItem = filas.FirstOrDefault(f => f.Centro.IdCentroCoste == actual);
        if (lista.SelectedItem is not null) lista.ScrollIntoView(lista.SelectedItem);
        buscar.TextChanged += (_, _) => Filtrar();
        panel.Children.Add(lista);
        d.Content = panel;
        var aceptar = new Button { Content = "Elegir", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Elige el centro marcado" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aceptar.Click += (_, _) => { if (lista.SelectedItem is not null) d.DialogResult = true; };
        cancelar.Click += (_, _) => d.DialogResult = false;
        lista.MouseDoubleClick += (_, _) => { if (lista.SelectedItem is not null) d.DialogResult = true; };
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => buscar.Focus();
        return d.ShowDialog() == true && lista.SelectedItem is FilaCentro f ? f.Centro : null;
    }

    private sealed record FilaCentro(CentroParaActivo Centro, string Texto);

    /// <summary>El alta paso a paso: Cancelar, Atrás y Siguiente (en el último paso, «Crear el activo»).</summary>
    public bool Alta(AltaActivoVM alta)
    {
        var d = new DialogoProConta { Title = "Nuevo activo", Owner = propietario(), SizeToContent = SizeToContent.WidthAndHeight };
        d.Content = new VistaAltaActivo { DataContext = alta };
        var siguiente = new Button { Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), MinWidth = 90, Command = alta.SiguienteCommand };
        siguiente.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding(nameof(AltaActivoVM.TextoSiguiente)) { Source = alta });
        siguiente.SetBinding(FrameworkElement.ToolTipProperty, new System.Windows.Data.Binding(nameof(AltaActivoVM.AyudaSiguiente)) { Source = alta });
        var atras = new Button { Content = "Atrás", Margin = new Thickness(8, 0, 0, 0), Command = alta.AtrasCommand, ToolTip = "Vuelve al paso anterior sin perder lo escrito" };
        atras.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding(nameof(AltaActivoVM.PuedeAtras)) { Source = alta, Converter = new ProConta.UI.Base.Formato.VisibleSiConverter() });
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin crear nada" };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, atras, siguiente } };
        alta.Creado += (_, _) => d.DialogResult = true;
        return d.ShowDialog() == true;
    }
}
