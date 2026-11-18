using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ProConta.Plataforma.Configuracion;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Tablas;

namespace ProConta.App;

/// <summary>Fila de la tabla del registro de errores.</summary>
internal sealed record FilaRegistroError(EntradaError Entrada)
{
    public string Fecha => Entrada.Fecha.ToString("dd/MM/yyyy HH:mm:ss");
    public DateTime FechaOrden => Entrada.Fecha;
    public string Programa => Entrada.Programa;
    public string Sociedad => Entrada.Sociedad ?? "";
    public string Pantalla => Entrada.Pantalla ?? Entrada.Donde ?? "";
    public string Error => Entrada.Error;
    public string Version => Entrada.Version ?? "";
}

/// <summary>
/// Configuración › «Registro de errores» (plataforma/registro-errores): consulta de solo lectura de los errores inesperados de
/// ProContaVF y del conector guardados en los ficheros de los últimos 12 meses, con el detalle técnico de la fila seleccionada.
/// Las acciones generales («Actualizar», «Abrir la carpeta») van en la cabecera; «Copiar el detalle», en el menú contextual.
/// </summary>
internal sealed class PestanaRegistroErrores : IPestanaTrabajo
{
    private readonly RegistroErrores _registro;
    private readonly Action<string> _avisar;
    private readonly ObservableCollection<FilaRegistroError> _filas = [];
    private readonly TablaProConta _tabla;
    private readonly TextBox _detalle;
    private readonly TextBlock _estado = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private readonly Grid _vista;

    public PestanaRegistroErrores(RegistroErrores registro, Action<string> avisar)
    {
        (_registro, _avisar) = (registro, avisar);
        _vista = new Grid { Margin = new Thickness(20) };
        _vista.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _vista.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star), MinHeight = 120 });
        _vista.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _vista.RowDefinitions.Add(new RowDefinition { Height = new GridLength(3, GridUnitType.Star), MinHeight = 140 });

        var cabecera = new DockPanel { LastChildFill = true };
        var botones = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        DockPanel.SetDock(botones, Dock.Right);
        var actualizar = new Button { Content = "Actualizar", Style = Estilo("Boton"), ToolTip = "Vuelve a leer los ficheros del registro (incluye los errores nuevos del conector)" };
        actualizar.Click += (_, _) => Cargar();
        var carpeta = new Button { Content = "Abrir la carpeta", Style = Estilo("Boton"), Margin = new Thickness(8, 0, 0, 0), ToolTip = $"Abre en el Explorador la carpeta del registro de errores ({_registro.Carpeta}), para enviar un fichero a soporte" };
        carpeta.Click += (_, _) => AbrirCarpeta();
        botones.Children.Add(actualizar);
        botones.Children.Add(carpeta);
        cabecera.Children.Add(botones);
        var textos = new StackPanel();
        textos.Children.Add(new TextBlock { Text = "Registro de errores", Style = Estilo("Texto.TituloPantalla") });
        textos.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10), Style = Estilo("Texto.Apagado"),
            Text = "Errores inesperados de ProContaVF y del conector de Claude Desktop en este equipo, de los últimos 12 meses. " +
                   "Selecciona uno para ver su detalle técnico; clic derecho › «Copiar el detalle» para enviarlo a soporte.",
        });
        textos.Children.Add(_estado);
        cabecera.Children.Add(textos);
        _vista.Children.Add(cabecera);

        _tabla = new TablaProConta
        {
            ClavePreferencias = "plataforma.registro-errores", TituloExportacion = "Registro de errores", IsReadOnly = true, AutoGenerateColumns = false,
            SelectionMode = DataGridSelectionMode.Single, ItemsSource = _filas, ToolTip = "Clic derecho: copiar el detalle del error seleccionado",
        };
        foreach (var (titulo, campo, ancho, orden) in new[]
        {
            ("Fecha", nameof(FilaRegistroError.Fecha), 140.0, nameof(FilaRegistroError.FechaOrden)),
            ("Programa", nameof(FilaRegistroError.Programa), 210.0, nameof(FilaRegistroError.Programa)),
            ("Sociedad", nameof(FilaRegistroError.Sociedad), 320.0, nameof(FilaRegistroError.Sociedad)),
            ("Pantalla", nameof(FilaRegistroError.Pantalla), 200.0, nameof(FilaRegistroError.Pantalla)),
            ("Error", nameof(FilaRegistroError.Error), 560.0, nameof(FilaRegistroError.Error)),
            ("Versión", nameof(FilaRegistroError.Version), 230.0, nameof(FilaRegistroError.Version)),
        })
            _tabla.Columns.Add(new DataGridTextColumn { Header = titulo, Binding = new Binding(campo), Width = ancho, SortMemberPath = orden });
        var copiar = new MenuItem { Header = "Copiar el detalle", ToolTip = "Copia al portapapeles el error seleccionado completo, con su detalle técnico" };
        copiar.Click += (_, _) => CopiarDetalle();
        _tabla.ContextMenu = new ContextMenu { Items = { copiar } };
        _tabla.ContextMenu.Opened += (_, _) => copiar.IsEnabled = _tabla.SelectedItem is FilaRegistroError;
        _tabla.SelectionChanged += (_, _) => _detalle!.Text = (_tabla.SelectedItem as FilaRegistroError)?.Entrada.Texto ?? "";
        Grid.SetRow(_tabla, 1);
        _vista.Children.Add(_tabla);

        var titDetalle = new TextBlock { Text = "Detalle técnico", Style = Estilo("Texto.Seccion"), Margin = new Thickness(0, 12, 0, 6) };
        Grid.SetRow(titDetalle, 2);
        _vista.Children.Add(titDetalle);
        _detalle = new TextBox
        {
            IsReadOnly = true, TextWrapping = TextWrapping.NoWrap, AcceptsReturn = true, FontFamily = new FontFamily("Consolas"),
            Height = double.NaN, VerticalAlignment = VerticalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            ToolTip = "Detalle técnico del error seleccionado: se puede seleccionar y copiar",
        };
        Grid.SetRow(_detalle, 3);
        _vista.Children.Add(_detalle);
        Cargar();
    }

    public string Titulo => "Registro de errores";
    public string Ayuda => "Errores inesperados de ProContaVF y del conector guardados en este equipo";
    public string Icono => "gear";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista;
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() { }
#pragma warning disable CS0067 // la pestaña no tiene cambios pendientes
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    private static Style Estilo(string clave) => (Style)Application.Current.FindResource(clave);

    /// <summary>Lee los ficheros y rellena la tabla, seleccionando el error más reciente.</summary>
    internal void Cargar()
    {
        var entradas = _registro.Leer(out var ilegibles);
        _filas.Clear();
        foreach (var e in entradas) _filas.Add(new FilaRegistroError(e));
        _estado.Text = (entradas.Count switch
        {
            0 => "No hay ningún error registrado en este equipo.",
            1 => "1 error registrado.",
            _ => $"{entradas.Count} errores registrados; el más reciente, el {RegistroErrores.FechaCorta(entradas[0].Fecha)}.",
        }) + (ilegibles == 0 ? "" : $" No he podido leer {(ilegibles == 1 ? "un fichero" : $"{ilegibles} ficheros")} de la carpeta: puede que otro programa lo tenga bloqueado; pulsa «Actualizar» en un momento.");
        if (_filas.Count > 0) _tabla.SelectedIndex = 0; else _detalle.Text = "";
    }

    private void CopiarDetalle()
    {
        if (_tabla.SelectedItem is not FilaRegistroError f) return;
        try
        {
            Clipboard.SetText(f.Entrada.Texto);
            _avisar("Detalle del error copiado al portapapeles.");
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            _avisar("No he podido copiar: otro programa tiene ocupado el portapapeles. Selecciona el texto del detalle y pulsa Ctrl+C.");
        }
    }

    private void AbrirCarpeta()
    {
        try { System.IO.Directory.CreateDirectory(_registro.Carpeta); }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { }
        var reciente = _filas.FirstOrDefault()?.Entrada.Fichero;
        if (reciente is not null) ErroresInesperados.AbrirCarpeta(reciente);
        else
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{_registro.Carpeta}\"") { UseShellExecute = true }); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { _avisar($"No he podido abrir la carpeta {_registro.Carpeta}: ábrela desde el Explorador."); }
        }
    }
}
