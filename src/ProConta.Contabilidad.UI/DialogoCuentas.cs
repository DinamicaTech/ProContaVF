using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI;

/// <summary>
/// Ventana del buscador de cuentas (contabilidad/cuenta-contable 2026-11-06) con <see cref="DialogoProConta"/>: búsqueda,
/// casilla del filtro de grupos, lista «código · nombre» y «Crear subcuenta…» en dos pasos (cuenta de tres cifras y,
/// después, código y nombre).
/// </summary>
public static class DialogoCuentas
{
    /// <summary>Abre el buscador y devuelve la cuenta elegida (o la creada), o null si se cancela.</summary>
    public static CuentaContable? Elegir(Window? propietario, BuscadorCuentasVM vm, string titulo = "Buscar cuenta")
    {
        var dialogo = new DialogoProConta { Title = titulo, Owner = propietario, Width = 820, Height = 600, SizeToContent = SizeToContent.Manual };
        var apagado = (Brush)dialogo.FindResource("Pincel.Apagado");
        var mono = (FontFamily)dialogo.FindResource("Fuente.Mono");
        var busqueda = new TextBox { Text = vm.Texto, ToolTip = "Código (628.11 = 6280011) o parte del nombre; ↓ para ir a la lista, Intro para elegir" };
        var soloGrupos = new CheckBox
        {
            Content = vm.TextoGrupos, IsChecked = vm.SoloGrupos, Margin = new Thickness(0, 8, 0, 0),
            Visibility = vm.HayGrupos ? Visibility.Visible : Visibility.Collapsed,
            ToolTip = "Quita la marca para buscar en todo el plan de cuentas del ejercicio",
        };
        var estado = new TextBlock { Margin = new Thickness(0, 8, 0, 8), Foreground = apagado, Text = vm.Estado, TextWrapping = TextWrapping.Wrap };
        var lista = new ListBox { HorizontalContentAlignment = HorizontalAlignment.Stretch, ItemsSource = vm.Resultados };
        lista.ItemTemplate = PlantillaCodigo.Crear(o =>
        {
            var c = (CuentaContable)o!;
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            p.Children.Add(new TextBlock { Text = c.Codigo, FontFamily = mono, MinWidth = 110 });
            p.Children.Add(new TextBlock { Text = c.Nombre, TextTrimming = TextTrimming.CharacterEllipsis });
            return p;
        });
        var cuerpo = new DockPanel();
        DockPanel.SetDock(busqueda, Dock.Top);
        DockPanel.SetDock(soloGrupos, Dock.Top);
        DockPanel.SetDock(estado, Dock.Top);
        cuerpo.Children.Add(busqueda);
        cuerpo.Children.Add(soloGrupos);
        cuerpo.Children.Add(estado);
        cuerpo.Children.Add(lista);
        dialogo.Content = cuerpo;

        CuentaContable? elegida = null;
        var elegir = new Button { Content = "Elegir", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Elige la cuenta seleccionada (también doble clic o Intro)" };
        var crear = new Button { Content = "Crear subcuenta…", Style = (Style)dialogo.FindResource("Boton.Fantasma"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Da de alta una subcuenta nueva en el ejercicio de trabajo" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin elegir" };
        void Aceptar() { if (lista.SelectedItem is CuentaContable c) { elegida = c; dialogo.DialogResult = true; } }
        elegir.Click += (_, _) => Aceptar();
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        crear.Click += (_, _) =>
        {
            if (CrearSubcuenta(dialogo, vm) is { } nueva) { elegida = nueva; dialogo.DialogResult = true; }
        };
        lista.MouseDoubleClick += (_, _) => Aceptar();
        lista.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; } };
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, crear, elegir } };

        void AlCambiar(object? _, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(BuscadorCuentasVM.Resultados): lista.ItemsSource = vm.Resultados; break;
                case nameof(BuscadorCuentasVM.Seleccionada):
                    lista.SelectedItem = vm.Seleccionada;
                    if (vm.Seleccionada is { } s) lista.ScrollIntoView(s);
                    break;
                case nameof(BuscadorCuentasVM.Estado): estado.Text = vm.Estado; break;
                case nameof(BuscadorCuentasVM.Texto): if (busqueda.Text != vm.Texto) busqueda.Text = vm.Texto; break;
            }
        }
        vm.PropertyChanged += AlCambiar;
        lista.SelectionChanged += (_, _) => { if (lista.SelectedItem is CuentaContable c) vm.Seleccionada = c; };
        busqueda.TextChanged += (_, _) => vm.Texto = busqueda.Text;
        soloGrupos.Click += (_, _) => vm.SoloGrupos = soloGrupos.IsChecked == true;
        busqueda.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && lista.Items.Count > 0)
            {
                if (lista.SelectedIndex < 0) lista.SelectedIndex = 0;
                lista.Focus();
                (lista.ItemContainerGenerator.ContainerFromIndex(lista.SelectedIndex) as ListBoxItem)?.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; }
        };
        dialogo.Loaded += async (_, _) =>
        {
            busqueda.Focus();
            busqueda.CaretIndex = busqueda.Text.Length;
            try { await vm.CargarAsync(); }
            catch (Exception ex) { estado.Text = "No he podido leer el plan de cuentas: " + ex.Message; }
        };
        try
        {
            return dialogo.ShowDialog() == true ? elegida : null;
        }
        finally
        {
            vm.PropertyChanged -= AlCambiar;
        }
    }

    /// <summary>
    /// «Crear subcuenta» en dos pasos: primero la cuenta de tres cifras (propuesta: la de la seleccionada) y después el
    /// código (el siguiente libre, editable) y el nombre. Devuelve la subcuenta creada o null.
    /// </summary>
    private static CuentaContable? CrearSubcuenta(Window propietario, BuscadorCuentasVM vm)
    {
        var dialogo = new DialogoProConta { Title = "Crear subcuenta", Owner = propietario, Width = 560, SizeToContent = SizeToContent.Height };
        var apagado = (Brush)dialogo.FindResource("Pincel.Apagado");
        var rojo = (Brush)dialogo.FindResource("Pincel.RojoError");
        var estiloMono = (Style)dialogo.FindResource("Campo.Mono");

        // Paso 1: la cuenta.
        var explicacionCuenta = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Text = $"¿En qué cuenta de tres cifras va la subcuenta? Se dará de alta en el ejercicio {vm.Ejercicio}." };
        var cuenta = new TextBox { Text = vm.CuentaPropuesta, Style = estiloMono, MaxLength = 3, Width = 90, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Tres cifras, por ejemplo 629" };
        var paso1 = new StackPanel { Children = { explicacionCuenta, new TextBlock { Text = "Cuenta", Foreground = apagado }, cuenta } };

        // Paso 2: código y nombre.
        var explicacionAlta = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
        var codigo = new TextBox { Style = estiloMono, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8), ToolTip = "Propongo el siguiente código libre; puedes cambiarlo" };
        var nombre = new TextBox { ToolTip = "Nombre de la subcuenta, por ejemplo «Suministros: agua»" };
        var paso2 = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Children = { explicacionAlta, new TextBlock { Text = "Código", Foreground = apagado }, codigo, new TextBlock { Text = "Nombre", Foreground = apagado }, nombre },
        };
        var error = new TextBlock { Foreground = rojo, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        dialogo.Content = new StackPanel { Children = { paso1, paso2, error } };

        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Vuelve al buscador sin crear nada" };
        var atras = new Button { Content = "Atrás", Style = (Style)dialogo.FindResource("Boton.Fantasma"), Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, ToolTip = "Vuelve a elegir la cuenta de tres cifras" };
        var siguiente = new Button { Content = "Siguiente", Style = (Style)dialogo.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true };
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, atras, siguiente } };

        void Mostrar(Error? e)
        {
            error.Text = e is null ? "" : e.AccionCorrectora is { Length: > 0 } a ? $"{e.Mensaje} {a}" : e.Mensaje;
            error.Visibility = e is null ? Visibility.Collapsed : Visibility.Visible;
        }
        void IrAPaso(int paso)
        {
            paso1.Visibility = paso == 1 ? Visibility.Visible : Visibility.Collapsed;
            paso2.Visibility = paso == 2 ? Visibility.Visible : Visibility.Collapsed;
            atras.Visibility = paso == 2 ? Visibility.Visible : Visibility.Collapsed;
            siguiente.Content = paso == 1 ? "Siguiente" : "Crear";
            siguiente.ToolTip = paso == 1 ? "Propone el siguiente código libre de la cuenta" : "Da de alta la subcuenta y la elige";
            Mostrar(null);
            (paso == 1 ? cuenta : nombre).Focus();
        }

        CuentaContable? creada = null;
        var paso = 1;
        var ocupado = false;
        siguiente.Click += async (_, _) =>
        {
            if (ocupado) return;
            ocupado = true;
            siguiente.IsEnabled = false;
            try
            {
                if (paso == 1)
                {
                    var r = await vm.SiguienteCodigoAsync(cuenta.Text);
                    if (!r.EsCorrecto) { Mostrar(r.Errores[0]); return; }
                    codigo.Text = r.Valor;
                    explicacionAlta.Text = $"Subcuenta de la {cuenta.Text.Trim()} en el ejercicio {vm.Ejercicio}. El código es el siguiente libre; puedes cambiarlo.";
                    paso = 2;
                    IrAPaso(2);
                }
                else
                {
                    var r = await vm.CrearAsync(cuenta.Text, codigo.Text, nombre.Text);
                    if (!r.EsCorrecto) { Mostrar(r.Errores[0]); return; }
                    creada = r.Valor;
                    dialogo.DialogResult = true;
                }
            }
            catch (Exception ex)
            {
                Mostrar(new Error("Cuentas.Fallo", "No he podido crear la subcuenta: " + ex.Message));
            }
            finally
            {
                ocupado = false;
                siguiente.IsEnabled = true;
            }
        };
        atras.Click += (_, _) => { paso = 1; IrAPaso(1); };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Loaded += (_, _) => { cuenta.Focus(); cuenta.SelectAll(); };
        return dialogo.ShowDialog() == true ? creada : null;
    }
}
