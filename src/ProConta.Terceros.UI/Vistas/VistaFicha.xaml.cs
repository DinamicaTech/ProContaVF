using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.Terceros.UI.Logica;
using ProConta.Terceros.UI.VistaModelos;

namespace ProConta.Terceros.UI.Vistas;

/// <summary>Vista de la ficha del tercero. La lógica está en <see cref="FichaTerceroVM"/>; aquí solo el foco, el teclado y el arrastre.</summary>
public partial class VistaFicha : UserControl
{
    private FichaTerceroVM? _vm;

    public VistaFicha()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (_vm is not null)
            {
                _vm.NavegarAError -= IrAError;
                _vm.RolEnfocado -= IrARol;
                _vm.FilaAnadida -= EnfocarFila;
            }
            _vm = e.NewValue as FichaTerceroVM;
            if (_vm is not null)
            {
                _vm.NavegarAError += IrAError;
                _vm.RolEnfocado += IrARol;
                _vm.FilaAnadida += EnfocarFila;
            }
        };
    }

    // ---------- navegación al campo ----------

    private void IrAError(object? sender, ErrorFicha e) => DespuesDeDibujar(() =>
    {
        var campo = Descendientes<FrameworkElement>(Pestanas).FirstOrDefault(f => f.Tag as string == e.Campo && Coincide(f.DataContext, e.Objeto) && f.IsVisible);
        if (campo is null) return;
        campo.BringIntoView();
        campo.Focus();
        if (campo is TextBox t) t.SelectAll();
    });

    private static bool Coincide(object? contexto, object objeto) =>
        ReferenceEquals(contexto, objeto) || contexto is ParteFicha p && ReferenceEquals(p.Modelo, objeto) ||
        contexto is FichaTerceroVM f && ReferenceEquals(f.T, objeto);

    private void IrARol(object? sender, string rol) => DespuesDeDibujar(() =>
        Descendientes<FrameworkElement>(Pestanas).FirstOrDefault(f => f.Tag as string == "rol:" + rol)?.BringIntoView());

    private void EnfocarFila(object? sender, object fila) => DespuesDeDibujar(() =>
    {
        var caja = Descendientes<TextBox>(Pestanas).FirstOrDefault(t => ReferenceEquals(t.DataContext, fila) && t.IsVisible);
        caja?.BringIntoView();
        caja?.Focus();
    });

    private void DespuesDeDibujar(Action accion) => Dispatcher.BeginInvoke(accion, DispatcherPriority.ContextIdle);

    private static IEnumerable<T> Descendientes<T>(DependencyObject raiz) where T : DependencyObject
    {
        var pendientes = new Queue<DependencyObject>([raiz]);
        while (pendientes.Count > 0)
        {
            var actual = pendientes.Dequeue();
            if (actual is T t) yield return t;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(actual); i++) pendientes.Enqueue(VisualTreeHelper.GetChild(actual, i));
        }
    }

    // ---------- domicilio: CP → provincia → población → vía ----------

    private void CodigoPostal_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Solo cuando escribe el operador: al cargar una ficha no se mueve el foco.
        if (sender is not TextBox { IsKeyboardFocused: true } caja || caja.DataContext is not DomicilioVM d) return;
        if (caja.Text.Trim().Length != 5) return;
        var siguiente = d.ResolverCodigoPostal();
        var tarjeta = Ascendiente<ContentPresenter>(caja);
        if (tarjeta is null) return;
        switch (siguiente)
        {
            case SiguienteFoco.Via:
                if (Descendientes<TextBox>(tarjeta).FirstOrDefault(t => t.Name == "Via") is { } via) DespuesDeDibujar(() => via.Focus());
                break;
            case SiguienteFoco.Poblacion:
                if (Descendientes<ComboBox>(tarjeta).FirstOrDefault(c => c.Name == "Poblacion") is { } poblacion)
                    DespuesDeDibujar(() => { poblacion.Focus(); poblacion.IsDropDownOpen = true; });
                break;
        }
    }

    private void Poblacion_DropDownClosed(object? sender, EventArgs e)
    {
        if (sender is not ComboBox { SelectedItem: not null } combo) return;
        combo.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
        if (Ascendiente<ContentPresenter>(combo) is { } tarjeta && Descendientes<TextBox>(tarjeta).FirstOrDefault(t => t.Name == "Via") is { } via)
            DespuesDeDibujar(() => via.Focus());
    }

    private static T? Ascendiente<T>(DependencyObject? o) where T : DependencyObject
    {
        // El ContentPresenter de la plantilla de un elemento de la lista (la tarjeta del domicilio).
        while (o is not null)
        {
            o = VisualTreeHelper.GetParent(o);
            if (o is T t && t is ContentPresenter { Content: DomicilioVM }) return t;
        }
        return null;
    }

    // ---------- cuentas de los roles ----------

    private void Cuenta_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CuentaRolVM c }) _ = c.ConfirmarAsync();
    }

    private void Cuenta_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement { DataContext: CuentaRolVM c })
        {
            _ = c.ConfirmarAsync();
            e.Handled = true;
        }
    }

    // ---------- perfiles: doble clic, teclado y arrastre ----------

    private void ListaDisponibles_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ElementoBajo(ListaDisponibles, e) is PerfilItemVM p) _vm?.Asignar([p]);
    }

    private void ListaAsignados_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ElementoBajo(ListaAsignados, e) is PerfilItemVM p) _vm?.Retirar([p]);
    }

    private void ListaDisponibles_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Right && ListaDisponibles.SelectedItems.Count > 0)
        {
            _vm?.Asignar(ListaDisponibles.SelectedItems.OfType<PerfilItemVM>().ToList());
            e.Handled = true;
        }
    }

    private void ListaAsignados_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Delete or Key.Left && ListaAsignados.SelectedItems.Count > 0)
        {
            _vm?.Retirar(ListaAsignados.SelectedItems.OfType<PerfilItemVM>().ToList());
            e.Handled = true;
        }
    }

    private Point? _inicioArrastre;

    private void Lista_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e) => _inicioArrastre = e.GetPosition(null);

    private void Lista_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _inicioArrastre is not { } inicio || sender is not ListBox lista) return;
        var d = e.GetPosition(null) - inicio;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if (ElementoBajo(lista, e) is not PerfilItemVM bajo) return;
        var elegidos = lista.SelectedItems.OfType<PerfilItemVM>().Contains(bajo) ? lista.SelectedItems.OfType<PerfilItemVM>().ToList() : [bajo];
        _inicioArrastre = null;
        DragDrop.DoDragDrop(lista, new DataObject(typeof(List<PerfilItemVM>), elegidos), DragDropEffects.Move);
    }

    private void ListaDisponibles_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(List<PerfilItemVM>)) is List<PerfilItemVM> perfiles && perfiles.All(p => _vm?.PerfilesAsignados.Contains(p) == true)) _vm?.Retirar(perfiles);
    }

    private void ListaAsignados_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(List<PerfilItemVM>)) is List<PerfilItemVM> perfiles && perfiles.All(p => _vm?.PerfilesDisponibles.Contains(p) == true)) _vm?.Asignar(perfiles);
    }

    private static object? ElementoBajo(ListBox lista, MouseEventArgs e) =>
        e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(lista, o) is ListBoxItem item && item.DataContext is PerfilItemVM p
            && Ascendiente2<Button>(o) is null ? p : null;

    private static T? Ascendiente2<T>(DependencyObject? o) where T : DependencyObject
    {
        while (o is not null and not ListBoxItem)
        {
            if (o is T t) return t;
            o = VisualTreeHelper.GetParent(o);
        }
        return null;
    }
}
