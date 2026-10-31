using System.IO;
using System.Windows;
using System.Windows.Threading;
using ProConta.UI.Base.Preferencias;

namespace ProConta.UI.Muestra;

/// <summary>
/// Muestra de la biblioteca. Argumentos:
///   --captura &lt;ruta.png&gt; [ancho alto] [pestaña]   abre la ventana a ese tamaño, guarda una captura y se cierra.
///   --operador &lt;nombre&gt;                          preferencias de ese operador (por defecto, «muestra»).
///   --probar-dialogo                             abre el diálogo de muestra, lo cierra solo y sale (código 0 si todo fue bien).
/// Cualquier error no controlado se escribe en %LocalAppData%\ProConta\muestra-errores.log.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ev) =>
        {
            Registrar(ev.Exception);
            ev.Handled = true;
            MessageBox.Show(ev.Exception.Message, "Error en la muestra", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ev) => Registrar(ev.ExceptionObject as Exception);
        var a = e.Args;
        var op = Arg(a, "--operador") ?? "muestra";
        PreferenciasOperador.Actual = new PreferenciasJson(op);
        var w = new MainWindow();
        var i = Array.IndexOf(a, "--captura");
        if (i >= 0 && i + 1 < a.Length)
        {
            var ancho = i + 2 < a.Length && int.TryParse(a[i + 2], out var x) ? x : 2560;
            var alto = i + 3 < a.Length && int.TryParse(a[i + 3], out var y) ? y : 1440;
            var pest = i + 4 < a.Length && int.TryParse(a[i + 4], out var p) ? p : 0;
            w.PrepararCaptura(a[i + 1], ancho, alto, pest);
        }
        if (a.Contains("--probar-dialogo")) w.ProbarDialogo();
        w.Show();
    }

    public static readonly string RutaErrores = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProConta", "muestra-errores.log");

    public static void Registrar(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaErrores)!);
            File.AppendAllText(RutaErrores, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException) { }
    }

    private static string? Arg(string[] a, string n)
    {
        var i = Array.IndexOf(a, n);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
}
