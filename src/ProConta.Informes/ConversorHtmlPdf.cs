using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace ProConta.Informes;

/// <summary>El PDF no se ha podido generar por algo del equipo (falta WebView2 o no arranca); el mensaje dice qué hacer.</summary>
public sealed class ErrorMotorPdf(string mensaje, Exception? interna = null) : Exception(mensaje, interna);

/// <summary>Tamaño y márgenes de la página en milímetros, tomados de la página de la plantilla.</summary>
public readonly record struct PaginaPdf(double Ancho, double Alto, double Arriba, double Derecha, double Abajo, double Izquierda);

/// <summary>
/// Convierte a PDF el HTML que exporta FastReport (informes/reporting, 2026-11-12 15:19) con WebView2, sin mostrar ninguna
/// ventana: el control cuelga de una ventana de solo mensajes (HWND_MESSAGE) en un hilo STA propio, que se crea al primer
/// PDF y se reutiliza. Funciona igual desde la aplicación, desde un hilo de fondo o desde las pruebas, sin interfaz abierta.
/// </summary>
public sealed class ConversorHtmlPdf
{
    /// <summary>Página oficial del instalador del componente WebView2 de Microsoft Edge.</summary>
    public const string UrlInstalador = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    private static readonly Lazy<ConversorHtmlPdf> _compartido = new(() => new ConversorHtmlPdf());
    private static readonly Regex _reglaPage = new(@"@page\s*\{[^}]*\}", RegexOptions.Compiled);

    private readonly string? _carpetaNavegador;
    private readonly string _carpetaDatos;
    private readonly object _cerrojo = new();
    private Dispatcher? _hilo;
    private Task<CoreWebView2Controller>? _controlador;
    private HwndSource? _ventana;
    private readonly SemaphoreSlim _turno = new(1, 1);

    /// <summary>El conversor de todo el programa (un solo navegador para todos los PDF).</summary>
    public static ConversorHtmlPdf Compartido => _compartido.Value;

    /// <param name="carpetaNavegador">Carpeta de un WebView2 fijo; null usa el que trae Windows (las pruebas pasan una vacía para simular un equipo sin él).</param>
    /// <param name="carpetaDatos">Carpeta de datos de WebView2; por omisión la misma del visor de PDF de compras.</param>
    public ConversorHtmlPdf(string? carpetaNavegador = null, string? carpetaDatos = null)
    {
        _carpetaNavegador = carpetaNavegador;
        _carpetaDatos = carpetaDatos ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProContaVF", "WebView2");
    }

    /// <summary>
    /// Pone el tamaño y los márgenes de la página en la regla @page del HTML (FastReport la exporta con margen 0 y su
    /// contenido no incluye los márgenes de la plantilla). Hay una regla por página: se cambian todas.
    /// </summary>
    public static string AjustarPagina(string html, PaginaPdf p)
    {
        var c = CultureInfo.InvariantCulture;
        var regla = string.Format(c, "@page {{ size: {0}mm {1}mm; margin: {2}mm {3}mm {4}mm {5}mm; }} body {{ margin: 0; }}",
            p.Ancho, p.Alto, p.Arriba, p.Derecha, p.Abajo, p.Izquierda);
        return _reglaPage.IsMatch(html)
            ? _reglaPage.Replace(html, regla.Replace("$", "$$"))
            : html.Replace("<head>", $"<head><style type=\"text/css\" media=\"print\">{regla}</style>");
    }

    /// <summary>Convierte el HTML en PDF. Bloquea hasta tenerlo; se puede llamar desde cualquier hilo.</summary>
    public byte[] Convertir(string html, PaginaPdf pagina)
    {
        ComprobarComponente();
        var hilo = Hilo();
        var tarea = hilo.InvokeAsync(() => ConvertirEnHiloAsync(AjustarPagina(html, pagina), pagina)).Task.Unwrap();
        try
        {
            return tarea.GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            throw new ErrorMotorPdf($"No he podido generar el PDF: el componente WebView2 de Microsoft Edge no ha arrancado ({ex.Message}). " +
                "Cierra y vuelve a abrir el programa; si se repite, reinstala WebView2 desde " + UrlInstalador + ".", ex);
        }
    }

    private void ComprobarComponente()
    {
        try
        {
            if (string.IsNullOrEmpty(CoreWebView2Environment.GetAvailableBrowserVersionString(_carpetaNavegador)))
                throw new WebView2RuntimeNotFoundException();
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or FileNotFoundException or DirectoryNotFoundException or COMException or ArgumentException)
        {
            throw new ErrorMotorPdf("No puedo generar el PDF porque en este equipo falta el componente WebView2 de Microsoft Edge (lo traen Windows 10 y 11 " +
                "y se puede haber desinstalado). Instálalo gratis desde " + UrlInstalador + " (Evergreen Bootstrapper) y vuelve a generar el PDF.", ex);
        }
    }

    private Dispatcher Hilo()
    {
        lock (_cerrojo)
        {
            if (_hilo is not null) return _hilo;
            using var listo = new ManualResetEventSlim();
            Dispatcher? dispatcher = null;
            var hilo = new Thread(() =>
            {
                dispatcher = Dispatcher.CurrentDispatcher;
                listo.Set();
                Dispatcher.Run();
            }) { IsBackground = true, Name = "ProContaVF PDF (WebView2)" };
            hilo.SetApartmentState(ApartmentState.STA);
            hilo.Start();
            listo.Wait();
            return _hilo = dispatcher!;
        }
    }

    private async Task<byte[]> ConvertirEnHiloAsync(string html, PaginaPdf pagina)
    {
        await _turno.WaitAsync();
        var temporal = Path.Combine(Path.GetTempPath(), "ProContaVF", "Pdf", $"{Guid.NewGuid():N}.html");
        try
        {
            var controlador = await Controlador();
            var web = controlador.CoreWebView2;
            // A un archivo y no con NavigateToString: esta corta en 2 MB y el HTML lleva las imágenes incrustadas.
            Directory.CreateDirectory(Path.GetDirectoryName(temporal)!);
            await File.WriteAllTextAsync(temporal, html, System.Text.Encoding.UTF8);
            var direccion = new Uri(temporal).AbsoluteUri;
            ulong? navegacion = null;
            var cargada = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
            void AlEmpezar(object? s, CoreWebView2NavigationStartingEventArgs e)
            {
                if (e.Uri == direccion) navegacion = e.NavigationId;
            }
            // Solo cuenta el final de esta navegación, no el de una anterior que se haya quedado a medias.
            void AlCargar(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (e.NavigationId == navegacion) cargada.TrySetResult(e);
            }
            web.NavigationStarting += AlEmpezar;
            web.NavigationCompleted += AlCargar;
            try
            {
                web.Navigate(direccion);
                var resultado = await cargada.Task.WaitAsync(TimeSpan.FromSeconds(60));
                if (!resultado.IsSuccess) throw new InvalidOperationException($"no se ha podido cargar la página del informe ({resultado.WebErrorStatus})");
            }
            catch (TimeoutException ex)
            {
                throw new InvalidOperationException("la página del informe no ha terminado de cargar en 60 segundos", ex);
            }
            finally
            {
                web.NavigationStarting -= AlEmpezar;
                web.NavigationCompleted -= AlCargar;
            }

            var ajustes = controlador.CoreWebView2.Environment.CreatePrintSettings();
            ajustes.Orientation = CoreWebView2PrintOrientation.Portrait;
            ajustes.PageWidth = pagina.Ancho / 25.4;
            ajustes.PageHeight = pagina.Alto / 25.4;
            // Los márgenes van en la regla @page del HTML, que es la que manda; aquí, iguales por si el motor la ignorara.
            ajustes.MarginTop = pagina.Arriba / 25.4;
            ajustes.MarginRight = pagina.Derecha / 25.4;
            ajustes.MarginBottom = pagina.Abajo / 25.4;
            ajustes.MarginLeft = pagina.Izquierda / 25.4;
            ajustes.ScaleFactor = 1;
            ajustes.ShouldPrintBackgrounds = true;
            ajustes.ShouldPrintHeaderAndFooter = false;
            ajustes.ShouldPrintSelectionOnly = false;
            using var flujo = await web.PrintToPdfStreamAsync(ajustes);
            using var salida = new MemoryStream();
            await flujo.CopyToAsync(salida);
            return salida.ToArray();
        }
        finally
        {
            try { File.Delete(temporal); } catch (IOException) { /* se queda en la carpeta temporal */ }
            _turno.Release();
        }
    }

    private Task<CoreWebView2Controller> Controlador()
    {
        // Solo se toca en el hilo del conversor, así que no hace falta cerrojo.
        if (_controlador is { IsFaulted: false, IsCanceled: false }) return _controlador;
        return _controlador = CrearControladorAsync();
    }

    private async Task<CoreWebView2Controller> CrearControladorAsync()
    {
        Directory.CreateDirectory(_carpetaDatos);
        var entorno = await CoreWebView2Environment.CreateAsync(_carpetaNavegador, _carpetaDatos);
        // Ventana de solo mensajes: no se ve, no sale en la barra de tareas y WebView2 la acepta como padre de un control invisible.
        _ventana?.Dispose();
        var ventana = _ventana = new HwndSource(new HwndSourceParameters("ProContaVF PDF") { ParentWindow = new IntPtr(-3), WindowStyle = 0, Width = 1, Height = 1 });
        var controlador = await entorno.CreateCoreWebView2ControllerAsync(ventana.Handle);
        controlador.IsVisible = false;
        var ajustes = controlador.CoreWebView2.Settings;
        ajustes.IsScriptEnabled = false;
        ajustes.AreDefaultContextMenusEnabled = false;
        ajustes.AreDevToolsEnabled = false;
        ajustes.IsStatusBarEnabled = false;
        return controlador;
    }
}
