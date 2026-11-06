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
            c.AddHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(AlPulsar), handledEventsToo: true);
        }
        else
        {
            c.RemoveHandler(TextBoxBase.TextChangedEvent, new TextChangedEventHandler(AlEscribir));
            c.SelectionChanged -= AlElegir;
            c.LostKeyboardFocus -= AlSalir;
            c.DropDownClosed -= AlCerrar;
            c.RemoveHandler(UIElement.PreviewKeyDownEvent, new KeyEventHandler(AlPulsar));
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
    /// ↑ ↓ desde el texto pasan el foco a la lista filtrada: ↓ al primer elemento, ↑ al último. Desde ahí el propio combo
    /// mueve el resaltado, Enter elige y Esc o Tab salen sin cambiar la elección. El combo editable trata estas teclas en su
    /// manejador de clase, antes que los de instancia, y su resaltado no sirve con la lista filtrada; por eso este manejador
    /// se registra también para eventos ya tratados.
    /// </summary>
    private static void AlPulsar(object sender, KeyEventArgs e)
    {
        var c = (ComboBox)sender;
        // Tab desde la lista sale sin elegir (el combo, por sí solo, elegiría el resaltado y dejaría la lista abierta).
        if (e.Key == Key.Tab && c.IsDropDownOpen && e.OriginalSource is ComboBoxItem)
        {
            c.IsDropDownOpen = false;
            var sentido = Keyboard.Modifiers == ModifierKeys.Shift ? FocusNavigationDirection.Previous : FocusNavigationDirection.Next;
            (Keyboard.FocusedElement as UIElement)?.MoveFocus(new TraversalRequest(sentido));
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Down or Key.Up) || !c.IsDropDownOpen || Keyboard.Modifiers != ModifierKeys.None
            || e.OriginalSource is not TextBox || Vista(c) is not { } vista) return;
        var visibles = vista.Cast<object>().ToList();
        e.Handled = true;
        if (visibles.Count == 0) return;
        var destino = e.Key == Key.Down ? visibles[0] : visibles[^1];
        if (c.ItemContainerGenerator.ContainerFromItem(destino) is not ComboBoxItem)
            c.UpdateLayout();
        if (c.ItemContainerGenerator.ContainerFromItem(destino) is ComboBoxItem item)
        {
            item.BringIntoView();
            item.Focus();
        }
    }

    /// <summary>Último elemento elegido, para volver a él si lo escrito no coincide con ninguno (Esc, Tab, clic fuera).</summary>
    private static readonly DependencyProperty AnteriorProperty =
        DependencyProperty.RegisterAttached("Anterior", typeof(object), typeof(ComboFiltro), new PropertyMetadata(null));

    private static void AlElegir(object sender, SelectionChangedEventArgs e)
    {
        var c = (ComboBox)sender;
        if (c.SelectedItem is { } nuevo) c.SetValue(AnteriorProperty, nuevo);
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
            // El combo editable deja de tener elección al escribir un texto que no es de ningún elemento: se vuelve al anterior.
            var elegido = c.SelectedItem ?? c.GetValue(AnteriorProperty);
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
