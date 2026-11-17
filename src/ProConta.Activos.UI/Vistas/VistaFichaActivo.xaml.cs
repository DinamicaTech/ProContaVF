using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProConta.Activos.UI.Vistas;

/// <summary>Ficha del activo: el doble clic en una subcuenta (de la cuenta de resultados o del apartado Subcuentas) abre su Mayor, y en una factura de Facturas o Ventas, la factura.</summary>
public partial class VistaFichaActivo : UserControl
{
    public VistaFichaActivo()
    {
        InitializeComponent();
        // Los títulos de las columnas del año y del anterior llevan el ejercicio elegido («2026», «2025»).
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is FichaActivoVM viejo) viejo.PropertyChanged -= Cambio;
            if (e.NewValue is FichaActivoVM nuevo) { nuevo.PropertyChanged += Cambio; Anios(nuevo); }
        };
    }

    private void Cambio(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is FichaActivoVM vm && e.PropertyName is nameof(FichaActivoVM.EjercicioCuenta) or nameof(FichaActivoVM.FilasCuenta) or nameof(FichaActivoVM.VerAnterior)) Anios(vm);
    }

    private void Anios(FichaActivoVM vm)
    {
        ColAnio.Header = vm.EjercicioCuenta.ToString(System.Globalization.CultureInfo.InvariantCulture);
        ColAnioMeses.Header = ColAnio.Header;
        ColAnterior.Header = vm.EjercicioAnterior.ToString(System.Globalization.CultureInfo.InvariantCulture);
        // Sin comparación (o sin ejercicio anterior en la sociedad) no se ven sus columnas.
        ColAnterior.Visibility = ColDiferencia.Visibility = vm.VerAnterior ? Visibility.Visible : Visibility.Collapsed;
    }

    private static object? Fila(object sender, MouseButtonEventArgs e) =>
        sender is DataGrid tabla && e.OriginalSource is DependencyObject o && ItemsControl.ContainerFromElement(tabla, o) is DataGridRow fila ? fila.Item : null;

    private void Cuenta_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Fila(sender, e) is FilaCuentaVM { MayorCommand: { } c } && c.CanExecute(null)) { c.Execute(null); e.Handled = true; }
    }

    private void Abrible_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Fila(sender, e) is IFilaAbrible { AbrirCommand: { } c } && c.CanExecute(null)) { c.Execute(null); e.Handled = true; }
    }

    private void Subcuentas_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Fila(sender, e) is FilaSubcuentaVM { MayorCommand: { } c } && c.CanExecute(null)) { c.Execute(null); e.Handled = true; }
    }
}
