using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;

namespace ProConta.UI.Base.Controles;

/// <summary>
/// Desplegable en el que se escribe para filtrar: la lista se queda con los elementos que contienen lo escrito en
/// cualquier parte (sin distinguir mayúsculas ni acentos) y se abre sola. Al elegir un elemento o salir del campo, el
/// filtro se quita y el texto vuelve a ser el del elemento elegido (si lo escrito no coincide con ninguno, no cambia la
/// elección). Para listas cortas, como los centros de coste; las largas, como las cuentas, usan el buscador.
/// Uso: <c>&lt;ComboBox pc:ComboFiltro.Activo="True" ItemsSource="…" SelectedItem="…" /&gt;</c>.
/// </summary>
public static class ComboFiltro
{
    public static readonly DependencyProperty ActivoProperty =
        DependencyProperty.RegisterAttached("Activo", typeof(bool), typeof(ComboFiltro), new PropertyMetadata(false, AlCambiar));

    public static bool GetActivo(DependencyObject o) => (bool)o.GetValue(ActivoProperty);
    public static void SetActivo(DependencyObject o, bool v) => o.SetValue(ActivoProperty, v);

    private static readonly DependencyProperty AplicandoProperty =
        DependencyProperty.RegisterAttached("Aplicando", typeof(bool), typeof(ComboFiltro), new PropertyMetadata(false));

    private static void AlCambiar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ComboBox c) return;
        if ((bool)e.NewValue)
        {
            c.IsEditable = true;
            c.IsTextSearchEnabled = false;
            c.StaysOpenOnEdit = true;
            c.AddHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(AlEscribir));
            c.SelectionChanged += AlElegir;
            c.LostKeyboardFocus += AlSalir;
            c.DropDownClosed += AlCerrar;
            c.PreviewKeyDown += AlPulsar;
        }
        else
        {
            c.RemoveHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(AlEscribir));
            c.SelectionChanged -= AlElegir;
            c.LostKeyboardFocus -= AlSalir;
            c.DropDownClosed -= AlCerrar;
            c.PreviewKeyDown -= AlPulsar;
        }
    }

    private static ICollectionView? Vista(ComboBox c) => c.ItemsSource is null ? null : CollectionViewSource.GetDefaultView(c.ItemsSource);

    private static void AlEscribir(object sender, TextChangedEventArgs e)
    {
        var c = (ComboBox)sender;
        if ((bool)c.GetValue(AplicandoProperty) || !c.IsKeyboardFocusWithin || Vista(c) is not { } vista) return;
        var texto = c.Text;
        // El texto del elemento elegido no cambia el filtro: al moverse con ↑ ↓ por la lista filtrada, el combo pone el texto
        // del elemento resaltado, y quitar el filtro entonces hacía saltar la lista entera. Al cerrar se quita (Quitar).
        if (c.SelectedItem is { } elegido && string.Equals(elegido.ToString(), texto, StringComparison.CurrentCulture)) return;
        var buscado = Plano(texto);
        c.SetValue(AplicandoProperty, true);
        try
        {
            vista.Filter = buscado.Length == 0 ? null : o => Plano(o?.ToString()).Contains(buscado, StringComparison.Ordinal);
            if (!c.IsDropDownOpen) c.IsDropDownOpen = true;
            // Filtrar puede mover el cursor o cambiar el texto: se deja como lo escribió el operador.
            if (c.Text != texto) c.Text = texto;
            if (c.Template.FindName("PART_EditableTextBox", c) is TextBox caja) { caja.SelectionLength = 0; caja.CaretIndex = texto.Length; }
        }
        finally { c.SetValue(AplicandoProperty, false); }
    }

    /// <summary>
    /// ↑ ↓ con la lista abierta recorren los elementos que deja el filtro: el combo editable, por sí solo, parte del elemento
    /// elegido aunque el filtro lo haya ocultado y no avanza. Enter cierra la lista con el resaltado elegido.
    /// </summary>
    private static void AlPulsar(object sender, KeyEventArgs e)
    {
        var c = (ComboBox)sender;
        if (e.Key is not (Key.Down or Key.Up) || !c.IsDropDownOpen || Keyboard.Modifiers != ModifierKeys.None || Vista(c) is not { } vista) return;
        var visibles = vista.Cast<object>().ToList();
        if (visibles.Count == 0) { e.Handled = true; return; }
        var actual = c.SelectedItem is { } sel ? visibles.IndexOf(sel) : -1;
        var siguiente = actual < 0 ? (e.Key == Key.Down ? 0 : visibles.Count - 1)
            : Math.Clamp(actual + (e.Key == Key.Down ? 1 : -1), 0, visibles.Count - 1);
        c.SetValue(AplicandoProperty, true);
        try
        {
            c.SelectedItem = visibles[siguiente];
            // Elegir pone el texto del elemento; el filtro sigue siendo lo que escribió el operador hasta cerrar la lista.
            if (c.Template.FindName("PART_EditableTextBox", c) is TextBox caja) caja.SelectAll();
        }
        finally { c.SetValue(AplicandoProperty, false); }
        if (c.ItemContainerGenerator.ContainerFromItem(visibles[siguiente]) is ComboBoxItem item) item.BringIntoView();
        e.Handled = true;
    }

    private static void AlElegir(object sender, SelectionChangedEventArgs e)
    {
        var c = (ComboBox)sender;
        if ((bool)c.GetValue(AplicandoProperty) || c.SelectedItem is null || c.IsDropDownOpen) return;
        Quitar(c);
    }

    private static void AlCerrar(object? sender, EventArgs e) => Quitar((ComboBox)sender!);

    private static void AlSalir(object sender, KeyboardFocusChangedEventArgs e)
    {
        var c = (ComboBox)sender;
        if (c.IsKeyboardFocusWithin || c.IsDropDownOpen) return;
        Quitar(c);
    }

    /// <summary>Quita el filtro y deja en el campo el texto del elemento elegido.</summary>
    private static void Quitar(ComboBox c)
    {
        c.SetValue(AplicandoProperty, true);
        try
        {
            var elegido = c.SelectedItem;
            if (Vista(c) is { Filter: not null } vista) vista.Filter = null;
            if (elegido is not null && !ReferenceEquals(c.SelectedItem, elegido)) c.SelectedItem = elegido;
            var texto = c.SelectedItem?.ToString() ?? "";
            if (c.Text != texto) c.Text = texto;
        }
        finally { c.SetValue(AplicandoProperty, false); }
    }

    /// <summary>Minúsculas y sin acentos, para comparar.</summary>
    internal static string Plano(string? texto)
    {
        if (string.IsNullOrEmpty(texto)) return "";
        var sb = new StringBuilder(texto.Length);
        foreach (var ch in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString();
    }
}
