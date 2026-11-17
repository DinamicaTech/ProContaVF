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
