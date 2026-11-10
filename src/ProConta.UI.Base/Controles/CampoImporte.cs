using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ProConta.UI.Base.Formato;

namespace ProConta.UI.Base.Controles;

/// <summary>
/// Criterio global: un campo de importe siempre se ve en moneda (1.234,56 €). El operador puede escribir solo el
/// número, con o sin «€»; al salir del campo se vuelve a formatear. El estilo Campo.Importe lo activa.
/// </summary>
public static class CampoImporte
{
    public static readonly DependencyProperty ActivoProperty =
        DependencyProperty.RegisterAttached("Activo", typeof(bool), typeof(CampoImporte), new PropertyMetadata(false, AlCambiar));

    public static bool GetActivo(DependencyObject o) => (bool)o.GetValue(ActivoProperty);
    public static void SetActivo(DependencyObject o, bool v) => o.SetValue(ActivoProperty, v);

    private static void AlCambiar(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox t) return;
        if ((bool)e.NewValue)
        {
            t.Loaded += AlCargar;
            t.LostKeyboardFocus += AlSalir;
            t.GotKeyboardFocus += AlEntrar;
        }
        else
        {
            t.Loaded -= AlCargar;
            t.LostKeyboardFocus -= AlSalir;
            t.GotKeyboardFocus -= AlEntrar;
        }
    }

    /// <summary>Solo selecciona todo el texto al entrar, sin formatear en moneda (estilo Campo.Numero).</summary>
    public static readonly DependencyProperty SeleccionarAlEntrarProperty =
        DependencyProperty.RegisterAttached("SeleccionarAlEntrar", typeof(bool), typeof(CampoImporte), new PropertyMetadata(false, AlCambiarSeleccion));

    public static bool GetSeleccionarAlEntrar(DependencyObject o) => (bool)o.GetValue(SeleccionarAlEntrarProperty);
    public static void SetSeleccionarAlEntrar(DependencyObject o, bool v) => o.SetValue(SeleccionarAlEntrarProperty, v);

    private static void AlCambiarSeleccion(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBox t) return;
        if ((bool)e.NewValue) t.GotKeyboardFocus += AlEntrar;
        else t.GotKeyboardFocus -= AlEntrar;
    }

    private static void AlCargar(object sender, RoutedEventArgs e) => Formatear((TextBox)sender);

    private static void AlSalir(object sender, KeyboardFocusChangedEventArgs e) => Formatear((TextBox)sender);

    private static void AlEntrar(object sender, KeyboardFocusChangedEventArgs e)
    {
        var t = (TextBox)sender;
        t.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(t.SelectAll));
    }

    /// <summary>Reescribe el texto en moneda si es un importe válido; si no lo es, lo deja para que se vea el error.</summary>
    internal static void Formatear(TextBox t)
    {
        if (t.IsReadOnly && t.Text.Length == 0) return;
        if (Formatos.LeerImporte(t.Text, out var v))
        {
            var f = Formatos.Importe(v);
            if (t.Text != f) t.Text = f;
        }
    }
}
