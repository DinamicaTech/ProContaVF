using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ProConta.Documentos.UI.Vistas;

/// <summary>
/// Vista previa de un documento: los PDF con el visor de WebView2 (el de Edge), las imágenes con su imagen y el resto con un aviso que invita a
/// abrirlo. El WebView2 se crea solo cuando hace falta un PDF.
/// </summary>
public sealed class VistaPrevia : ContentControl
{
    private WebView2? _web;
    private bool _iniciando;
    private readonly TextBlock _aviso = new()
    {
        TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(16),
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Image _imagen = new() { Stretch = Stretch.Uniform };
    private readonly Grid _rejilla = new();

    public VistaPrevia()
    {
        _rejilla.Children.Add(_imagen);
        _rejilla.Children.Add(_aviso);
        Content = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = _rejilla, ClipToBounds = true };
        Loaded += (_, _) =>
        {
            if (Content is Border b)
            {
                b.BorderBrush = TryFindResource("Pincel.Linea") as Brush ?? Brushes.LightGray;
                b.Background = TryFindResource("Pincel.Fondo2") as Brush ?? Brushes.WhiteSmoke;
            }
            _aviso.Foreground = TryFindResource("Pincel.Apagado") as Brush ?? Brushes.Gray;
            Mostrar();
        };
    }

    public static readonly DependencyProperty RutaProperty = DependencyProperty.Register(nameof(Ruta), typeof(string), typeof(VistaPrevia),
        new PropertyMetadata(null, (d, _) => ((VistaPrevia)d).Mostrar()));

    /// <summary>Ruta del fichero que se ve; null deja la vista vacía.</summary>
    public string? Ruta
    {
        get => (string?)GetValue(RutaProperty);
        set => SetValue(RutaProperty, value);
    }

    private void Mostrar()
    {
        if (!IsLoaded) return;
        var ruta = Ruta;
        _imagen.Source = null;
        _imagen.Visibility = Visibility.Collapsed;
        if (_web is not null) _web.Visibility = Visibility.Collapsed;
        _aviso.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(ruta)) { _aviso.Text = ""; return; }
        if (!File.Exists(ruta)) { _aviso.Text = "No encuentro el fichero en la carpeta de documentos."; return; }
        switch (Path.GetExtension(ruta).ToLowerInvariant())
        {
            case ".pdf":
                _aviso.Text = "Cargando la vista previa…";
                _ = MostrarPdfAsync(ruta);
                break;
            case ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".tif" or ".tiff":
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad; // no deja el fichero bloqueado
                    bmp.UriSource = new Uri(ruta);
                    bmp.DecodePixelWidth = 900;
                    bmp.EndInit();
                    bmp.Freeze();
                    _imagen.Source = bmp;
                    _imagen.Visibility = Visibility.Visible;
                    _aviso.Visibility = Visibility.Collapsed;
                }
                catch (Exception) { _aviso.Text = "No puedo enseñar esta imagen. Ábrela con «Abrir»."; }
                break;
            default:
                _aviso.Text = $"Sin vista previa para los ficheros {Path.GetExtension(ruta)}. Ábrelo con «Abrir».";
                break;
        }
    }

    private async Task MostrarPdfAsync(string ruta)
    {
        try
        {
            if (_web is null)
            {
                if (_iniciando) return;
                _iniciando = true;
                var web = new WebView2();
                _rejilla.Children.Insert(0, web);
                var datos = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProContaVF", "WebView2");
                var entorno = await CoreWebView2Environment.CreateAsync(null, datos);
                await web.EnsureCoreWebView2Async(entorno);
                web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _web = web;
                _iniciando = false;
            }
            if (Ruta != ruta) { Mostrar(); return; }
            _web.Source = new Uri(ruta + "#toolbar=0&navpanes=0&view=FitH");
            _web.Visibility = Visibility.Visible;
            _aviso.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            _iniciando = false;
            _aviso.Text = $"No puedo enseñar la vista previa ({ex.Message}). Ábrelo con «Abrir».";
        }
    }
}
