using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using ProConta.UI.Base.Controles;

namespace ProConta.Documentos.UI.Vistas;

/// <summary>Diálogos del gestor documental con <see cref="DialogoProConta"/> y <see cref="ServicioMensajes"/>.</summary>
public sealed class DialogosDocumentos(Func<Window?> propietario) : IDialogosDocumentos
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, opciones);

    public void Error(string titulo, string mensaje, IReadOnlyList<string>? detalles = null) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, new OpcionMensaje("Entendido", "Cierra este aviso", Principal: true, Cancelar: true));

    public IReadOnlyList<string>? ElegirFicheros(string titulo, bool varios)
    {
        var d = new OpenFileDialog
        {
            Title = titulo, Multiselect = varios, CheckFileExists = true,
            Filter = "Documentos (PDF, imágenes, Office, HTML…)|*.pdf;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.doc;*.docx;*.xls;*.xlsx;*.htm;*.html;*.txt;*.xml;*.eml;*.zip|Todos los ficheros|*.*",
        };
        return d.ShowDialog(propietario()) == true ? d.FileNames : null;
    }

    public string? ElegirCarpeta(string titulo)
    {
        var d = new OpenFolderDialog { Title = titulo };
        return d.ShowDialog(propietario()) == true ? d.FolderName : null;
    }

    public bool Vincular(VincularVM vincular)
    {
        var d = new DialogoProConta { Title = vincular.Titulo, Owner = propietario(), SizeToContent = SizeToContent.WidthAndHeight, Redimensionable = true };
        d.Content = new VistaVincular { DataContext = vincular };
        var aceptar = new Button { Content = vincular.TextoAceptar, Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = vincular.Reemplaza ? "Guarda los vínculos tal como están arriba: añade los nuevos y quita los que has quitado" : "Añade lo elegido a todos los documentos marcados" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aceptar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        return d.ShowDialog() == true;
    }

    public IReadOnlyList<int>? ElegirDocumentos(string titulo, string explicacion, IReadOnlyList<DocumentoListado> documentos)
    {
        var d = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 680, Height = 560, Redimensionable = true };
        var panel = new DockPanel();
        var ayuda = new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(ayuda, Dock.Top);
        panel.Children.Add(ayuda);
        var buscar = new TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = "Escribe parte del título, el tipo o un vínculo para filtrar la lista" };
        DockPanel.SetDock(buscar, Dock.Top);
        panel.Children.Add(buscar);
        var filas = documentos.Select(x => new FilaElegible(x)).ToList();
        var lista = new ListBox { ToolTip = "Marca los documentos que quieres vincular aquí" };
        lista.ItemTemplate = (DataTemplate)System.Windows.Markup.XamlReader.Parse(
            "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'>" +
            "<CheckBox IsChecked='{Binding Elegido}'><TextBlock><Run Text='{Binding Texto, Mode=OneWay}' /><Run Text='  ' /><Run Text='{Binding Detalle, Mode=OneWay}' Foreground='Gray' /></TextBlock></CheckBox>" +
            "</DataTemplate>");
        void Filtrar()
        {
            var q = ServicioDocumentos.Palabras(buscar.Text);
            lista.ItemsSource = q.Count == 0 ? filas : filas.Where(f => q.All(p => string.Join(' ', ServicioDocumentos.Palabras(f.Texto + " " + f.Detalle)).Contains(p))).ToList();
        }
        Filtrar();
        buscar.TextChanged += (_, _) => Filtrar();
        panel.Children.Add(lista);
        d.Content = panel;
        var aceptar = new Button { Content = "Vincular", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Vincula los marcados" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin vincular nada" };
        aceptar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => buscar.Focus();
        return d.ShowDialog() == true ? filas.Where(f => f.Elegido).Select(f => f.Documento.IdDocumento).ToList() : null;
    }

    private sealed class FilaElegible(DocumentoListado documento)
    {
        public DocumentoListado Documento { get; } = documento;
        public bool Elegido { get; set; }
        public string Texto => Documento.Titulo;
        public string Detalle => $"{Documento.NombreTipo} · {Documento.FechaDocumento:dd/MM/yyyy} · {string.Join(" · ", Documento.Vinculos.Select(v => v.Etiqueta))}";
    }

    public TipoDocumento? ElegirTipo(IReadOnlyList<TipoDocumento> tipos, string explicacion)
    {
        var d = new DialogoProConta { Title = "Poner tipo", Owner = propietario(), Width = 420, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var combo = new ComboBox { ItemsSource = tipos, DisplayMemberPath = nameof(TipoDocumento.Nombre), SelectedIndex = 0, ToolTip = "Tipo que se pone a todos" };
        panel.Children.Add(combo);
        d.Content = panel;
        var aceptar = new Button { Content = "Poner", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Pone este tipo" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aceptar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        return d.ShowDialog() == true ? combo.SelectedItem as TipoDocumento : null;
    }

    public string? PedirTexto(string titulo, string explicacion, string propuesto)
    {
        var d = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 480, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var texto = new TextBox { Text = propuesto, ToolTip = "Escribe el texto" };
        panel.Children.Add(texto);
        d.Content = panel;
        var aceptar = new Button { Content = "Aceptar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Continúa con este texto" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin hacer nada" };
        aceptar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => { texto.Focus(); texto.SelectAll(); };
        return d.ShowDialog() == true ? texto.Text.Trim() : null;
    }

    public (string Tipo, DateTime Fecha, string Titulo)? Clasificar(IReadOnlyList<TipoDocumento> tipos, string? tipo, DateTime? fecha, string titulo, string explicacion)
    {
        var d = new DialogoProConta { Title = "Clasificar y archivar", Owner = propietario(), Width = 480, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Tipo", Margin = new Thickness(0, 0, 0, 3) });
        var combo = new ComboBox { ItemsSource = tipos, DisplayMemberPath = nameof(TipoDocumento.Nombre), Margin = new Thickness(0, 0, 0, 10),
            ToolTip = "Tipo del documento: decide la carpeta donde se archiva. Los tipos se mantienen en Parámetros y tablas › Tipos de documento" };
        combo.SelectedItem = tipos.FirstOrDefault(t => t.Codigo == tipo);
        panel.Children.Add(combo);
        panel.Children.Add(new TextBlock { Text = "Fecha del documento", Margin = new Thickness(0, 0, 0, 3) });
        var dia = new DatePicker { SelectedDate = fecha, Margin = new Thickness(0, 0, 0, 10), Width = 160, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Fecha del propio documento (firma, notificación, presentación…); decide el año de la carpeta" };
        panel.Children.Add(dia);
        panel.Children.Add(new TextBlock { Text = "Título", Margin = new Thickness(0, 0, 0, 3) });
        var texto = new TextBox { Text = titulo, ToolTip = "Título del documento: será el nombre del fichero archivado" };
        panel.Children.Add(texto);
        var aviso = new TextBlock { Foreground = System.Windows.Media.Brushes.Firebrick, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        panel.Children.Add(aviso);
        d.Content = panel;
        var aceptar = new Button { Content = "Archivar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Guarda el tipo, la fecha y el título y archiva el documento en su carpeta" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aceptar.Click += (_, _) =>
        {
            if (combo.SelectedItem is null || dia.SelectedDate is null || texto.Text.Trim().Length == 0)
            {
                aviso.Text = "Falta " + string.Join(", ", new[] { combo.SelectedItem is null ? "el tipo" : null, dia.SelectedDate is null ? "la fecha" : null,
                    texto.Text.Trim().Length == 0 ? "el título" : null }.Where(x => x is not null)) + ".";
                return;
            }
            d.DialogResult = true;
        };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => combo.Focus();
        return d.ShowDialog() == true && combo.SelectedItem is TipoDocumento t && dia.SelectedDate is DateTime f ? (t.Codigo, f.Date, texto.Text.Trim()) : null;
    }

    public bool Copiar(string texto)
    {
        try { Clipboard.SetText(texto); return true; }
        catch (System.Runtime.InteropServices.ExternalException) { return false; }
    }

    public void AbrirFichero(string ruta)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Error("No se ha podido abrir", $"No he podido abrir {ruta}: {ex.Message} Puede que no haya ningún programa para este tipo de fichero; ábrelo desde el Explorador con «Mostrar en la carpeta».");
        }
    }

    public void MostrarEnCarpeta(string ruta)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{ruta}\"") { UseShellExecute = true })?.Dispose(); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Error("No se ha podido abrir la carpeta", $"{ex.Message} El fichero está en {ruta}.");
        }
    }
}
