using System.Collections;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.UI.Muestra;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<CuentaReciente> _recientes = new(DatosMuestra.Recientes);
    private readonly List<ApunteMuestra> _apuntes = DatosMuestra.Apuntes(5242);
    private string? _captura;
    private int _pestana;

    public MainWindow()
    {
        InitializeComponent();
        ListaRecientes.ItemsSource = _recientes;
        ListaRecientes.SelectedIndex = 0;
        Tabla.ItemsSource = _apuntes;
        Tabla.Aviso += (_, t) => Estado.Text = t;
        Tabla.OrdenFilasCambiado += (_, _) => Estado.Text = "Orden de las filas cambiado. El saldo de cada fila sigue siendo el acumulado por fecha.";
        Vista.ComandoAccion = new ComandoDelegado(() => Vista.Estado = EstadoVista.Contenido);
        Demo.ComandoAccion = new ComandoDelegado(() => Estado.Text = "Acción del estado pulsada.");
        Totales();
        Ejemplos();
        Galeria.ItemsSource = Application.Current.Resources.MergedDictionaries
            .SelectMany(Todos)
            .Select(kv => (Clave: kv.Key as string, Geometria: kv.Value as Geometry))
            .Where(x => x.Clave is not null && x.Clave.StartsWith("Icono.", StringComparison.Ordinal) && x.Geometria is not null)
            .Select(x => new KeyValuePair<string, Geometry>(x.Clave!["Icono.".Length..], x.Geometria!))
            .GroupBy(kv => kv.Key).Select(g => g.First())
            .OrderBy(kv => kv.Key)
            .ToList();
        // Error de validación de muestra: borde rojo, icono y mensaje en el tooltip.
        var b = new Binding("Text") { Source = CampoError, Mode = BindingMode.OneWay };
        var expr = BindingOperations.SetBinding(CampoError, TagProperty, b);
        Validation.MarkInvalid(expr, new ValidationError(new ExceptionValidationRule(), b, "El código postal tiene 5 cifras: «4600» tiene 4. Escribe, por ejemplo, 46001.", null));
    }

    private static IEnumerable<DictionaryEntry> Todos(ResourceDictionary d) =>
        d.Cast<DictionaryEntry>().Concat(d.MergedDictionaries.SelectMany(Todos));

    private void Totales()
    {
        var d = _apuntes.Sum(a => a.Debe);
        var h = _apuntes.Sum(a => a.Haber);
        var s = d - h;
        Recuento.Text = Formatos.Plural(_apuntes.Count, "apunte", "apuntes");
        TotalDebe.Text = Formatos.Importe(d);
        TotalHaber.Text = Formatos.Importe(h);
        TotalSaldo.Text = Formatos.Saldo(s);
        TotalSaldo.Foreground = Pincel(ReglaSaldo.Sentido("43000012", s));
        TextoSaldo.ToolTip = ReglaSaldo.Descripcion(s);
    }

    private Brush Pincel(SentidoSaldo s) => (Brush)FindResource(s switch
    {
        SentidoSaldo.AFavor => "Pincel.Verde",
        SentidoSaldo.EnContra => "Pincel.RojoSaldo",
        _ => "Pincel.Tinta"
    });

    /// <summary>Ejemplos de la regla de color del saldo por grupo del PGC.</summary>
    private void Ejemplos()
    {
        (string cta, string d, decimal s)[] ej =
        {
            ("5720001", "Banco con dinero", 12500m),
            ("43000012", "Cliente que nos debe", 5672.94m),
            ("40000031", "Proveedor al que debemos", -741m),
            ("4750001", "Hacienda acreedora", -2310.4m),
            ("6290001", "Gasto del ejercicio", 980m),
            ("7050001", "Ingreso del ejercicio", -48211.3m),
            ("1000000", "Capital social (grupo 1, sin color)", -3000m)
        };
        foreach (var (cta, d, s) in ej)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            var c = new TextBlock { Text = cta, Style = (Style)FindResource("Texto.Mono") };
            var t = new TextBlock { Text = d };
            var v = new TextBlock { Text = Formatos.Saldo(s), Style = (Style)FindResource("Texto.Saldo"), Foreground = Pincel(ReglaSaldo.Sentido(cta, s)), ToolTip = ReglaSaldo.Descripcion(s) };
            Grid.SetColumn(t, 1); Grid.SetColumn(v, 2);
            g.Children.Add(c); g.Children.Add(t); g.Children.Add(v);
            Saldos.Children.Add(g);
        }
    }

    private void Vaciar_Click(object sender, RoutedEventArgs e)
    {
        _recientes.Clear();
        Estado.Text = "Lista de cuentas recientes vaciada.";
    }

    private void Estado_Checked(object sender, RoutedEventArgs e)
    {
        if (Demo is null || sender is not RadioButton { Tag: string t }) return;
        Demo.Estado = Enum.Parse<EstadoVista>(t);
        (Demo.Titulo, Demo.Mensaje, Demo.TextoAccion) = Demo.Estado switch
        {
            EstadoVista.Cargando => ("Cargando el mayor…", "Leyendo los apuntes de los ejercicios elegidos.", (string?)null),
            EstadoVista.Error => ("No se ha podido cargar el mayor", "El servidor de contabilidad no ha respondido (tiempo de espera agotado). Tus filtros se conservan.", "Reintentar"),
            EstadoVista.SinPermiso => ("No tienes permiso para el Mayor en Ribera Gestión", "Tu perfil no incluye la consulta del Mayor. Pídeselo a un administrador.", null),
            _ => ("Sin movimientos en el ejercicio 2026", "La cuenta existe en el plan pero no tiene apuntes. Puedes elegir otros ejercicios o crear un asiento.", "Marcar todos los ejercicios")
        };
    }

    private void Dialogo_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new DialogoProConta { Owner = this, Title = "Asignar número de factura" };
        var pie = new StackPanel { Orientation = Orientation.Horizontal };
        pie.Children.Add(new Button { Content = "Cancelar", Margin = new Thickness(0, 0, 8, 0), Command = dlg.CerrarCommand, ToolTip = "Cierra sin cambiar nada (Esc)" });
        pie.Children.Add(new Button { Content = "Asignar", Style = (Style)FindResource("Boton.Primario"), Command = dlg.AceptarCommand, IsDefault = true, ToolTip = "Escribe el número en los 2 apuntes elegidos" });
        dlg.Botones = pie;
        var cont = new StackPanel();
        cont.Children.Add(new Nota { Content = "Se escribe el número en los apuntes elegidos y, si los hay, en sus líneas de Norma 43. El cambio queda en el historial.", Margin = new Thickness(0, 0, 0, 12) });
        cont.Children.Add(new TextBlock { Text = "Número de factura", Style = (Style)FindResource("Texto.Seccion") });
        cont.Children.Add(new TextBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Text = "F-2026-0106", ToolTip = "Número de la factura" });
        dlg.Content = cont;
        Estado.Text = dlg.ShowDialog() == true ? "Número asignado (muestra)." : "Diálogo cerrado sin cambios.";
    }

    // ---------- captura para comparar con las maquetas ----------

    /// <summary>Abre la ventana a un tamaño fijo, guarda una captura PNG y cierra la aplicación.</summary>
    public void PrepararCaptura(string ruta, int ancho, int alto, int pestana)
    {
        _captura = ruta;
        _pestana = pestana;
        WindowState = WindowState.Normal;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0; Top = 0; Width = ancho; Height = alto;
        ContentRendered += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(Capturar));
    }

    private void Capturar()
    {
        if (_captura is null) return;
        Pestanas.SelectedIndex = _pestana;
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            var raiz = (FrameworkElement)Content;
            var dpi = VisualTreeHelper.GetDpi(this);
            var bmp = new RenderTargetBitmap((int)(raiz.ActualWidth * dpi.DpiScaleX), (int)(raiz.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            var fondo = new DrawingVisual();
            using (var dc = fondo.RenderOpen()) dc.DrawRectangle(Background, null, new Rect(0, 0, raiz.ActualWidth, raiz.ActualHeight));
            bmp.Render(fondo);
            bmp.Render(raiz);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_captura))!);
            using (var f = File.Create(_captura)) enc.Save(f);
            Application.Current.Shutdown();
        }));
    }
}
