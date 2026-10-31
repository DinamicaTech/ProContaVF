using System.Windows;
using ProConta.UI.Base.Preferencias;

namespace ProConta.UI.Muestra;

/// <summary>
/// Muestra de la biblioteca. Argumentos:
///   --captura &lt;ruta.png&gt; [ancho alto] [pestaña]   abre la ventana a ese tamaño, guarda una captura y se cierra.
///   --operador &lt;nombre&gt;                          preferencias de ese operador (por defecto, «muestra»).
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
        w.Show();
    }

    private static string? Arg(string[] a, string n)
    {
        var i = Array.IndexOf(a, n);
        return i >= 0 && i + 1 < a.Length ? a[i + 1] : null;
    }
}
