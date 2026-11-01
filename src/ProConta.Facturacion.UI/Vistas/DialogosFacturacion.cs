using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.UI.VistaModelos;
using ProConta.Terceros.Roles;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Facturacion.UI.Vistas;

/// <summary>Diálogos de facturación con <see cref="DialogoProConta"/>: elegir cliente, elegir concepto y asignar cuenta.</summary>
public sealed class DialogosFacturacion(ApoyoEditorFacturas apoyo, Func<Window?> propietario) : IDialogosFacturacion
{
    private Window? Propietario => propietario() ?? Application.Current?.MainWindow;

    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(Propietario, titulo, mensaje, detalles, opciones);

    public ClienteElegible? ElegirCliente()
    {
        var dialogo = new DialogoProConta { Title = "Buscar cliente", Owner = Propietario, Width = 900, Height = 620, SizeToContent = SizeToContent.Manual };
        var busqueda = new TextBox { ToolTip = "Escribe el nombre, la razón social, el NIF o el IdCliente; Intro o ↓ para ir a la lista" };
        var estado = new TextBlock { Margin = new Thickness(0, 8, 0, 8), Foreground = (Brush)dialogo.FindResource("Pincel.Apagado"), Text = "Escribe al menos dos letras o el número de cliente." };
        var lista = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch };
        lista.ItemTemplate = PlantillaCodigo.Crear(c =>
        {
            var cli = (ClienteElegible)c!;
            var p = new StackPanel { Margin = new Thickness(0, 3, 0, 3), Opacity = cli.Activo ? 1 : 0.55 };
            p.Children.Add(new TextBlock { Text = cli.Nombre + (cli.Activo ? "" : " (baja)"), FontWeight = FontWeights.SemiBold });
            p.Children.Add(new TextBlock { Text = $"{cli.Nif ?? "Sin NIF"} · Id {cli.IdCliente} · {cli.Domicilio}", Foreground = (Brush)dialogo.FindResource("Pincel.Apagado") });
            return p;
        });
        var cuerpo = new DockPanel();
        DockPanel.SetDock(busqueda, Dock.Top);
        DockPanel.SetDock(estado, Dock.Top);
        cuerpo.Children.Add(busqueda);
        cuerpo.Children.Add(estado);
        cuerpo.Children.Add(lista);
        dialogo.Content = cuerpo;

        ClienteElegible? elegido = null;
        var elegir = new Button { Content = "Elegir", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Elige el cliente seleccionado (también doble clic o Intro)" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin elegir" };
        void Aceptar() { if (lista.SelectedItem is ClienteElegible c) { elegido = c; dialogo.DialogResult = true; } }
        elegir.Click += (_, _) => Aceptar();
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        lista.MouseDoubleClick += (_, _) => Aceptar();
        lista.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; } };
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, elegir } };

        var version = 0;
        busqueda.TextChanged += async (_, _) =>
        {
            var v = ++version;
            var texto = busqueda.Text.Trim();
            if (texto.Length < 2 && !texto.All(char.IsAsciiDigit) || texto.Length == 0) { lista.ItemsSource = null; estado.Text = "Escribe al menos dos letras o el número de cliente."; return; }
            await Task.Delay(200);
            if (v != version) return;
            var r = await apoyo.BuscarClientesAsync(texto);
            if (v != version) return;
            lista.ItemsSource = r;
            if (r.Count > 0) lista.SelectedIndex = 0;
            estado.Text = r.Count == 0 ? "Ningún cliente coincide. Si es nuevo, dalo de alta en Terceros con el rol Cliente." : r.Count == 50 ? "Se muestran los 50 primeros: afina la búsqueda." : Formatos.Plural(r.Count, "cliente", "clientes");
        };
        busqueda.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && lista.Items.Count > 0) { lista.Focus(); (lista.ItemContainerGenerator.ContainerFromIndex(lista.SelectedIndex) as ListBoxItem)?.Focus(); e.Handled = true; }
            else if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; }
        };
        dialogo.Loaded += (_, _) => busqueda.Focus();
        return dialogo.ShowDialog() == true ? elegido : null;
    }

    public Concepto? ElegirConcepto(IReadOnlyList<Concepto> conceptos)
    {
        if (conceptos.Count == 0)
        {
            Preguntar("Sin conceptos", "La sociedad no tiene conceptos activos, así que añado una línea vacía para que escribas su nombre, precio y cuenta. Puedes crear conceptos en Parámetros y tablas › Conceptos.",
                null, new OpcionMensaje("Entendido", "Añade la línea vacía", Principal: true, Cancelar: true));
            return null;
        }
        var dialogo = new DialogoProConta { Title = "Añadir línea", Owner = Propietario, Width = 1000, Height = 680, SizeToContent = SizeToContent.Manual };
        var filtro = new TextBox { ToolTip = "Filtra por nombre, descripción o cuenta" };
        var lista = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemsSource = conceptos };
        lista.ItemTemplate = PlantillaCodigo.Crear(o =>
        {
            var c = (Concepto)o!;
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            var n = new StackPanel();
            n.Children.Add(new TextBlock { Text = c.Nombre, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            if (!string.IsNullOrWhiteSpace(c.Descripcion)) n.Children.Add(new TextBlock { Text = c.Descripcion, Foreground = (Brush)dialogo.FindResource("Pincel.Apagado"), TextTrimming = TextTrimming.CharacterEllipsis });
            var cuenta = new TextBlock { Text = c.CuentaVenta ?? "Sin cuenta", FontFamily = (FontFamily)dialogo.FindResource("Fuente.Mono"), ToolTip = c.NombreCuentaVenta };
            var precio = new TextBlock { Text = $"{Formatos.Importe(c.PrecioUnitario)} · IVA {(c.TipoIva is decimal t ? t.ToString("0.##", Formatos.Cultura) + " %" : "—")}", HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetColumn(cuenta, 1);
            Grid.SetColumn(precio, 2);
            g.Children.Add(n);
            g.Children.Add(cuenta);
            g.Children.Add(precio);
            return g;
        });
        filtro.TextChanged += (_, _) =>
        {
            var t = filtro.Text.Trim();
            lista.ItemsSource = t.Length == 0 ? conceptos : conceptos.Where(c => new[] { c.Nombre, c.Descripcion, c.CuentaVenta }
                .Any(x => x?.Contains(t, StringComparison.CurrentCultureIgnoreCase) == true)).ToList();
            if (lista.Items.Count > 0) lista.SelectedIndex = 0;
        };
        var cuerpo = new DockPanel();
        var explicacion = new TextBlock
        {
            Text = "Conceptos activos de la sociedad. Elegir uno copia su nombre, descripción, precio, IVA y cuenta en la línea; cambiar después el concepto no toca esta línea.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(explicacion, Dock.Top);
        DockPanel.SetDock(filtro, Dock.Top);
        cuerpo.Children.Add(explicacion);
        cuerpo.Children.Add(filtro);
        lista.Margin = new Thickness(0, 8, 0, 0);
        cuerpo.Children.Add(lista);
        dialogo.Content = cuerpo;

        Concepto? elegido = null;
        var elegir = new Button { Content = "Añadir", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Añade una línea con el concepto seleccionado" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No añade ninguna línea" };
        void Aceptar() { if (lista.SelectedItem is Concepto c) { elegido = c; dialogo.DialogResult = true; } }
        elegir.Click += (_, _) => Aceptar();
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        lista.MouseDoubleClick += (_, _) => Aceptar();
        lista.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; } };
        filtro.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; } else if (e.Key == Key.Down) { lista.Focus(); e.Handled = true; } };
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, elegir } };
        dialogo.Loaded += (_, _) => { lista.SelectedIndex = 0; filtro.Focus(); };
        return dialogo.ShowDialog() == true ? elegido : null;
    }

    public string? PedirCuenta(ClaseCuentaRol clase, string nombreCliente, string propuesta, string razonSocial)
    {
        var retencion = clase == ClaseCuentaRol.Retencion;
        var dialogo = new DialogoProConta { Title = $"{(retencion ? "Cuenta de retención" : "Cuenta del cliente")} de {nombreCliente}", Owner = Propietario, Width = 620 };
        var codigo = new TextBox { Text = propuesta, Width = 180, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)dialogo.FindResource("Campo.Mono"),
            ToolTip = "Escribe una cuenta existente o una nueva; si no existe en el ejercicio, se crea" };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"Se guarda en el rol Cliente para {razonSocial} y vale en todos sus ejercicios. Si la cuenta no existe en este ejercicio, la creo con el nombre «{(retencion ? "Retención IRPF " : "")}{nombreCliente}».",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock { Text = "Cuenta", Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(codigo);
        dialogo.Content = panel;
        var aceptar = new Button { Content = "Asignar", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Asigna la cuenta (y la crea si hace falta)" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No cambia nada" };
        aceptar.Click += (_, _) => { if (codigo.Text.Trim().Length == 0) { codigo.Focus(); return; } dialogo.DialogResult = true; };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => { codigo.Focus(); codigo.SelectAll(); };
        return dialogo.ShowDialog() == true ? codigo.Text.Trim() : null;
    }
}
