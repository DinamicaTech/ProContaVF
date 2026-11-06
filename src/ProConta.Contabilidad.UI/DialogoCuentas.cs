using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI;

/// <summary>
/// Ventana del buscador de cuentas (contabilidad/cuenta-contable 2026-11-06) con <see cref="DialogoProConta"/>: búsqueda,
/// casilla del filtro de grupos, lista con código, nombre y descripción, y «Crear subcuenta…» en dos pasos (cuenta de
/// tres cifras y, después, código, nombre, descripción y centro de coste).
/// </summary>
public static class DialogoCuentas
{
    /// <summary>Abre el buscador y devuelve la cuenta elegida (o la creada), o null si se cancela.</summary>
    public static CuentaContable? Elegir(Window? propietario, BuscadorCuentasVM vm, string titulo = "Buscar cuenta")
    {
        var dialogo = new DialogoProConta { Title = titulo, Owner = propietario, Width = 820, Height = 600, SizeToContent = SizeToContent.Manual };
        var apagado = (Brush)dialogo.FindResource("Pincel.Apagado");
        var mono = (FontFamily)dialogo.FindResource("Fuente.Mono");
        var busqueda = new TextBox { Text = vm.Texto, ToolTip = "Código (628.11 = 6280011) o parte del nombre o de la descripción; ↓ para ir a la lista, Intro para elegir" };
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
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var texto = new StackPanel();
            texto.Children.Add(new TextBlock { Text = c.Nombre, TextTrimming = TextTrimming.CharacterEllipsis });
            if (c.Descripcion is { Length: > 0 } d) texto.Children.Add(new TextBlock { Text = d, Foreground = apagado, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = d });
            Grid.SetColumn(texto, 1);
            g.Children.Add(new TextBlock { Text = c.Codigo, FontFamily = mono });
            g.Children.Add(texto);
            return g;
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

    /// <summary>Elemento de los desplegables del asistente: el texto (también para filtrar) y lo que se muestra.</summary>
    private sealed record Opcion<T>(T? Valor, string Texto, string? Detalle = null)
    {
        public override string ToString() => Detalle is { Length: > 0 } d ? $"{Texto} · {d}" : Texto;
    }

    private static DataTemplate PlantillaOpcion<T>(Brush apagado) => PlantillaCodigo.Crear(o =>
    {
        var op = (Opcion<T>)o!;
        var p = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        p.Children.Add(new TextBlock { Text = op.Texto, TextTrimming = TextTrimming.CharacterEllipsis });
        if (op.Detalle is { Length: > 0 } d) p.Children.Add(new TextBlock { Text = d, Foreground = apagado, TextTrimming = TextTrimming.CharacterEllipsis });
        return p;
    });

    /// <summary>
    /// «Crear subcuenta» en dos pasos: primero la cuenta de tres cifras en un desplegable con código, nombre y descripción
    /// (propuesta: la de la seleccionada) y después el código (el siguiente libre, editable), el nombre, la descripción y el
    /// centro de coste por defecto. Devuelve la subcuenta creada o null.
    /// </summary>
    private static CuentaContable? CrearSubcuenta(Window propietario, BuscadorCuentasVM vm)
    {
        var dialogo = new DialogoProConta { Title = "Crear subcuenta", Owner = propietario, Width = 620, SizeToContent = SizeToContent.Height };
        var apagado = (Brush)dialogo.FindResource("Pincel.Apagado");
        var rojo = (Brush)dialogo.FindResource("Pincel.RojoError");
        var estiloMono = (Style)dialogo.FindResource("Campo.Mono");
        TextBlock Etiqueta(string texto) => new() { Text = texto, Foreground = apagado, Margin = new Thickness(0, 8, 0, 2) };

        // Paso 1: la cuenta.
        var explicacionCuenta = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = $"¿En qué cuenta va la subcuenta? Se dará de alta en el ejercicio {vm.Ejercicio}." };
        var padres = vm.CuentasPadre.Select(c => new Opcion<CuentaContable>(c, $"{c.Codigo} · {c.Nombre}", c.Descripcion)).ToList();
        var cuenta = new ComboBox
        {
            ItemsSource = padres, ItemTemplate = PlantillaOpcion<CuentaContable>(apagado), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Cuenta de tres cifras del ejercicio; escribe parte del código, del nombre o de la descripción para filtrar la lista",
        };
        ComboFiltro.SetActivo(cuenta, true);
        cuenta.SelectedItem = padres.FirstOrDefault(p => p.Valor!.Codigo == vm.CuentaPropuesta);
        var sinCuentas = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Foreground = apagado, Margin = new Thickness(0, 6, 0, 0),
            Text = vm.HayGrupos && vm.SoloGrupos ? $"No hay cuentas de tres cifras del {vm.TextoGrupos.ToLowerInvariant().Replace("solo ", "")} en el ejercicio. Quita «{vm.TextoGrupos}» en el buscador o da de alta la cuenta en el plan."
                : "No hay cuentas de tres cifras en el ejercicio. Da de alta la cuenta en el plan.",
            Visibility = padres.Count == 0 ? Visibility.Visible : Visibility.Collapsed,
        };
        var paso1 = new StackPanel { Children = { explicacionCuenta, Etiqueta("Cuenta"), cuenta, sinCuentas } };

        // Paso 2: código, nombre, descripción y centro de coste.
        var explicacionAlta = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var codigo = new TextBox { Style = estiloMono, Width = 160, HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Propongo el siguiente código libre; puedes cambiarlo" };
        var nombre = new TextBox { MaxLength = ReglasCuenta.LongitudMaximaNombre, ToolTip = "Nombre de la subcuenta, por ejemplo «Suministros: agua»" };
        var descripcion = new TextBox
        {
            MaxLength = ReglasCuenta.LongitudMaximaDescripcion, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap, MinHeight = 48,
            ToolTip = "Para qué se usa la subcuenta (opcional); el buscador también busca en ella",
        };
        var centros = new List<Opcion<CentroCoste>> { new(null, "Sin centro de coste") };
        centros.AddRange(vm.CentrosCoste.Select(c => new Opcion<CentroCoste>(c, $"{c.Codigo} · {c.Nombre}")));
        var centro = new ComboBox
        {
            ItemsSource = centros, SelectedIndex = 0, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            ToolTip = "Centro de coste (departamento o subdepartamento) que se propone en los apuntes de esta subcuenta (opcional)",
        };
        ComboFiltro.SetActivo(centro, true);
        var paso2 = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Children = { explicacionAlta, Etiqueta("Código"), codigo, Etiqueta("Nombre"), nombre, Etiqueta("Descripción"), descripcion, Etiqueta("Centro de coste por defecto"), centro },
        };
        var error = new TextBlock { Foreground = rojo, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        dialogo.Content = new StackPanel { Children = { paso1, paso2, error } };

        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Vuelve al buscador sin crear nada" };
        var atras = new Button { Content = "Atrás", Style = (Style)dialogo.FindResource("Boton.Fantasma"), Margin = new Thickness(8, 0, 0, 0), Visibility = Visibility.Collapsed, ToolTip = "Vuelve a elegir la cuenta" };
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
            (paso == 1 ? (Control)cuenta : nombre).Focus();
        }
        string CodigoCuenta() => (cuenta.SelectedItem as Opcion<CuentaContable>)?.Valor?.Codigo ?? "";

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
                    var r = await vm.SiguienteCodigoAsync(CodigoCuenta());
                    if (!r.EsCorrecto) { Mostrar(r.Errores[0]); return; }
                    codigo.Text = r.Valor;
                    var padre = (Opcion<CuentaContable>)cuenta.SelectedItem!;
                    explicacionAlta.Text = $"Subcuenta de la {padre.Texto} en el ejercicio {vm.Ejercicio}. El código es el siguiente libre; puedes cambiarlo.";
                    if (padre.Valor!.IdCentroCostePredeterminado is int cc && centros.FirstOrDefault(o => o.Valor?.IdCentroCoste == cc) is { } propuesto)
                        centro.SelectedItem = propuesto;
                    paso = 2;
                    IrAPaso(2);
                }
                else
                {
                    var r = await vm.CrearAsync(CodigoCuenta(), codigo.Text, nombre.Text, descripcion.Text, (centro.SelectedItem as Opcion<CentroCoste>)?.Valor?.IdCentroCoste);
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
        dialogo.Loaded += (_, _) => cuenta.Focus();
        return dialogo.ShowDialog() == true ? creada : null;
    }
}
