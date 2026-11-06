using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ProConta.Compras.UI.Vistas;

/// <summary>Valor reconocido que el visor resalta en el PDF.</summary>
public sealed record ValorResaltado(string Campo, string Texto);

/// <summary>
/// Lo que el operador hace en el PDF: seleccionar texto (<see cref="Texto"/>) o hacer clic en un valor resaltado
/// (<see cref="Campos"/>: los campos que se resaltan ahí). <see cref="Punto"/> es dónde, en coordenadas del PDF.
/// </summary>
public sealed record SeleccionPdf(string Texto, IReadOnlyList<string> Campos, ProConta.Compras.Reconocimiento.PuntoDocumento? Punto);

/// <summary>
/// Visor del PDF de compras (compras/pantallas): WebView2 con PDF.js, servido desde la carpeta Visor del programa. Pinta
/// el PDF, resalta de dónde sale cada campo reconocido y avisa del texto que el operador selecciona. El PDF se pasa por
/// mensaje (sus bytes), así que el visor no necesita acceso a la carpeta compartida.
/// </summary>
public sealed class VisorPdf : ContentControl
{
    private const string Host = "visor.proconta.local";
    private readonly WebView2 _web = new();
    private readonly TextBlock _aviso = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private bool _listo;
    private string? _pendiente;
    private IReadOnlyList<ValorResaltado> _valores = [];
    private string? _activo;

    public VisorPdf()
    {
        var rejilla = new Grid();
        rejilla.Children.Add(_web);
        rejilla.Children.Add(_aviso);
        Content = rejilla;
        Loaded += async (_, _) => await IniciarAsync();
    }

    /// <summary>Texto seleccionado en el PDF (para rellenar el campo marcado de la ficha) o valor resaltado elegido con un clic.</summary>
    public event EventHandler<SeleccionPdf>? TextoSeleccionado;

    public static readonly DependencyProperty RutaProperty = DependencyProperty.Register(nameof(Ruta), typeof(string), typeof(VisorPdf),
        new PropertyMetadata(null, (d, _) => ((VisorPdf)d).Abrir()));

    /// <summary>Ruta del PDF que se ve; null deja el visor vacío.</summary>
    public string? Ruta
    {
        get => (string?)GetValue(RutaProperty);
        set => SetValue(RutaProperty, value);
    }

    /// <summary>Marca en el PDF dónde aparecen los valores reconocidos; el del campo activo, más intenso y a la vista.</summary>
    public void Resaltar(IReadOnlyList<ValorResaltado> valores, string? activo)
    {
        _valores = valores;
        _activo = activo;
        if (_listo) Enviar(new { tipo = "resaltar", valores = valores.Select(v => new { campo = v.Campo, texto = v.Texto }), activo });
    }

    private async Task IniciarAsync()
    {
        if (_web.CoreWebView2 is not null) return;
        try
        {
            var carpeta = Path.Combine(AppContext.BaseDirectory, "Visor");
            var datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProContaVF", "WebView2");
            var entorno = await CoreWebView2Environment.CreateAsync(null, datos);
            await _web.EnsureCoreWebView2Async(entorno);
            var core = _web.CoreWebView2!;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.SetVirtualHostNameToFolderMapping(Host, carpeta, CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += AlRecibirMensaje;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.Navigate($"https://{Host}/visor.html");
            _aviso.Text = "";
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException or InvalidOperationException or IOException)
        {
            _aviso.Text = $"No puedo mostrar el PDF: falta el componente WebView2 de Microsoft Edge o no se ha podido iniciar ({ex.Message}). " +
                          "Puedes abrir el PDF con «Abrir el PDF» del menú contextual.";
        }
    }

    private void AlRecibirMensaje(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var tipo = doc.RootElement.TryGetProperty("tipo", out var t) ? t.GetString() : null;
            if (tipo == "listo")
            {
                _listo = true;
                Abrir();
            }
            else if (tipo == "seleccion" && doc.RootElement.TryGetProperty("texto", out var texto) && texto.GetString() is { Length: > 0 } s)
                TextoSeleccionado?.Invoke(this, new SeleccionPdf(s, [], Punto(doc.RootElement)));
            else if (tipo == "elegir" && doc.RootElement.TryGetProperty("campos", out var campos) && campos.ValueKind == JsonValueKind.Array && Punto(doc.RootElement) is { } p)
                TextoSeleccionado?.Invoke(this, new SeleccionPdf("", campos.EnumerateArray().Select(c => c.GetString() ?? "").Where(c => c.Length > 0).ToList(), p));
        }
        catch (JsonException)
        {
            // Un mensaje que no se entiende se ignora: el visor solo envía «listo», «seleccion» y «elegir».
        }
    }

    private static ProConta.Compras.Reconocimiento.PuntoDocumento? Punto(JsonElement m) =>
        m.TryGetProperty("punto", out var p) && p.ValueKind == JsonValueKind.Object
        && p.TryGetProperty("pagina", out var n) && n.TryGetInt32(out var pagina)
        && p.TryGetProperty("x", out var x) && x.TryGetDouble(out var vx) && p.TryGetProperty("y", out var y) && y.TryGetDouble(out var vy)
            ? new ProConta.Compras.Reconocimiento.PuntoDocumento(pagina, vx, vy) : null;

    private async void Abrir()
    {
        if (!_listo) return;
        var ruta = Ruta;
        _pendiente = ruta;
        string? datos = null;
        if (ruta is not null)
        {
            try
            {
                var bytes = await File.ReadAllBytesAsync(ruta);
                datos = Convert.ToBase64String(bytes);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _aviso.Text = $"No puedo leer el PDF. Ruta intentada: {ruta}. Comprueba que la carpeta compartida esté conectada.";
            }
        }
        if (!ReferenceEquals(_pendiente, ruta)) return;
        if (datos is not null) _aviso.Text = "";
        Enviar(new { tipo = "abrir", datos, nombre = ruta is null ? null : Path.GetFileName(ruta) });
        if (_valores.Count > 0) Resaltar(_valores, _activo);
    }

    private void Enviar(object mensaje)
    {
        if (_web.CoreWebView2 is null) return;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(mensaje));
    }
}
