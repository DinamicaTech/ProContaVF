using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Contabilidad.Mayor;
using ProConta.UI.Base.Formato;

namespace ProConta.Contabilidad.UI.Mayor;

/// <summary>
/// Pantalla del Mayor (contabilidad/mayor), fiel al prototipo validado: recientes plegables, cabecera con la cuenta y los
/// ejercicios, vistas, filtros como etiquetas, tabla con el criterio global de columnas y separadores de ejercicio, totales
/// y selección en el pie, y todas las acciones en el menú contextual (regla del proyecto).
/// </summary>
public partial class PantallaMayor : UserControl
{
    private MayorVM? _vm;

    public PantallaMayor()
    {
        InitializeComponent();
        DataContextChanged += (_, e) => Conectar(e.NewValue as MayorVM);
        Loaded += (_, _) => { if (_vm is not null) _ = _vm.IniciarAsync(); };
        Tabla.Aviso += (_, t) => _vm?.Aviso(t);
        Tabla.OrdenFilasCambiado += (_, _) => AjustarAgrupacion();
        Tabla.PreviewMouseRightButtonDown += Tabla_ClicDerecho;
        InputBindings.Add(new KeyBinding(new RelayCommand(Imprimir), Key.P, ModifierKeys.Control));
    }

    private void Conectar(MayorVM? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= CambioVM;
        _vm = vm;
        if (vm is null) return;
        vm.PropertyChanged += CambioVM;
        Tabla.TituloExportacion = vm.Titulo;
        PonerFilas();
    }

    private void CambioVM(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MayorVM.Filas)) PonerFilas();
        else if (e.PropertyName == nameof(MayorVM.Titulo) && _vm is not null) Tabla.TituloExportacion = _vm.Titulo;
    }

    /// <summary>
    /// Pone las filas conservando el orden elegido por el operador. En orden de fecha (el de fábrica) se agrupan por ejercicio
    /// con su separador; al ordenar por otra columna los separadores desaparecen y cada fila conserva su saldo por fecha.
    /// </summary>
    private void PonerFilas()
    {
        if (_vm is null) return;
        var anterior = Tabla.ItemsSource is ICollectionView v ? v.SortDescriptions.ToList() : [];
        // Tras puntear o numerar se vuelve a leer la vista: la selección se conserva por la clave de la fila.
        var seleccion = Seleccionadas().Select(f => f.Fila.Clave).ToHashSet(StringComparer.Ordinal);
        var vista = new ListCollectionView(_vm.Filas.ToList());
        foreach (var s in anterior) vista.SortDescriptions.Add(s);
        if (anterior.Count == 0) vista.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FilaMayorVM.CabeceraEjercicio)));
        Tabla.ItemsSource = vista;
        foreach (var c in Tabla.Columns)
        {
            var s = anterior.FirstOrDefault(x => x.PropertyName == c.SortMemberPath);
            c.SortDirection = s.PropertyName is null ? null : s.Direction;
        }
        if (seleccion.Count == 0) return;
        var volver = _vm.Filas.Where(f => seleccion.Contains(f.Fila.Clave)).ToList();
        foreach (var f in volver) Tabla.SelectedItems.Add(f);
        if (volver.Count > 0) Tabla.ScrollIntoView(volver[0]);
    }

    private void AjustarAgrupacion()
    {
        if (Tabla.ItemsSource is not ListCollectionView v) return;
        using (v.DeferRefresh())
        {
            v.GroupDescriptions.Clear();
            if (v.SortDescriptions.Count == 0) v.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FilaMayorVM.CabeceraEjercicio)));
        }
    }

    private List<FilaMayorVM> Seleccionadas() => Tabla.SelectedItems.OfType<FilaMayorVM>().ToList();

    private void Tabla_SelectionChanged(object sender, SelectionChangedEventArgs e) => _vm?.Seleccion(Seleccionadas());

    private void Tabla_ClicDerecho(object sender, MouseButtonEventArgs e)
    {
        if (Ascendiente<DataGridRow>(e.OriginalSource as DependencyObject) is { } fila && !fila.IsSelected)
        {
            Tabla.SelectedItems.Clear();
            fila.IsSelected = true;
        }
    }

    private void Tabla_DobleClic(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null) return;
        var celda = Ascendiente<DataGridCell>(e.OriginalSource as DependencyObject);
        if (celda?.DataContext is not FilaMayorVM fila) return;
        var clave = celda.Column is null ? null : ProConta.UI.Base.Tablas.TablaProConta.ClaveDe(celda.Column);
        if (clave is "cp" or "cpd")
        {
            if (fila.Fila.CuentasContrapartida.Count == 1) { _ = _vm.IrACuentaAsync(fila.Fila.CuentasContrapartida[0]); e.Handled = true; return; }
            if (fila.Fila.CuentasContrapartida.Count > 1)
            {
                _vm.Aviso("Esta fila tiene varias contrapartidas: elige a cuál ir con el clic derecho, «Ir a la contrapartida».");
                return;
            }
        }
        _ = _vm.VerAsientoAsync(fila);
        e.Handled = true;
    }

    private void Tabla_Tecla(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Delete && Seleccionadas() is { Count: > 0 } quitar)
        {
            _vm.Quitar(quitar);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && Tabla.SelectedItem is FilaMayorVM f)
        {
            _ = _vm.VerAsientoAsync(f);
            e.Handled = true;
        }
        else if (e.Key == Key.F4 && _vm.PuedeEscribir && Seleccionadas() is { Count: > 0 } puntear)
        {
            _ = _vm.PuntearAsync(puntear, !puntear.All(x => x.Punteado));
            e.Handled = true;
        }
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control && _vm.PuedeEscribir && Seleccionadas() is { Count: > 0 } numerar)
        {
            _ = _vm.AsignarNumeroAsync(numerar);
            e.Handled = true;
        }
    }

    // ---------- menú contextual de las filas ----------

    private void Tabla_MenuContextual(object sender, ContextMenuEventArgs e)
    {
        if (_vm is null || Tabla.ContextMenu is not { } menu) return;
        var sel = Seleccionadas();
        if (sel.Count == 0) { e.Handled = true; return; }
        menu.Items.Clear();
        var una = sel.Count == 1 ? sel[0] : null;
        var cabecera = una is not null
            ? $"Asiento {una.Asiento} · {Formatos.Importe(una.Debe - una.Haber)}"
            : $"{sel.Count} filas · {Formatos.Importe(sel.Sum(f => f.Debe - f.Haber))}";
        menu.Items.Add(new MenuItem { Header = cabecera, IsEnabled = false, FontWeight = FontWeights.SemiBold });

        menu.Items.Add(Opcion("Ver asiento", "Icono.eye", () => _ = _vm.VerAsientoAsync(sel[0]), "Intro",
            una is not null ? "Abre el asiento completo en solo lectura" : "Abre el asiento de la primera fila seleccionada en solo lectura"));
        menu.Items.Add(Proxima("Ver línea Norma 43", "Icono.bank", "Llega con las pantallas de Norma 43: abrirá la línea del extracto bancario de este apunte"));
        menu.Items.Add(Opcion("Ver historial del apunte", "Icono.hist", () => _ = _vm.VerHistorialAsync(sel[0]), null,
            "Cambios guardados del apunte: fecha, operador, motivo, campo, antes y después", una is not null && una.Fila.NumeroApuntes == 1,
            "Elige una sola fila de la vista Normal: el historial es de un apunte"));
        menu.Items.Add(IrAContrapartida(una));
        menu.Items.Add(new Separator());

        var facturas = sel.Select(f => f.Fila.FacturaNormalizada).Where(x => x.Length > 0).Distinct().ToList();
        var sinFactura = "Las filas seleccionadas no tienen número de factura";
        menu.Items.Add(Opcion(facturas.Count == 1 ? $"Buscar la factura {facturas[0]} en todas las cuentas" : "Buscar estas facturas en todas las cuentas", "Icono.search",
            () => _ = _vm.BuscarFacturasAsync(facturas), null,
            "Muestra los apuntes de la factura en todas las cuentas de los ejercicios elegidos (F2024001 y 2024001 son la misma); se vuelve quitando la etiqueta",
            facturas.Count > 0, sinFactura));
        menu.Items.Add(Opcion(facturas.Count == 1 ? $"Filtrar por la factura {facturas[0]}" : "Filtrar por estas facturas", "Icono.funnel",
            () => _vm.FiltrarFacturas(facturas), null, "Deja solo las filas de la factura en las cuentas de la vista; el saldo pasa a ser parcial", facturas.Count > 0, sinFactura));
        menu.Items.Add(Proxima($"Buscar {Formatos.Importe(Math.Abs(sel.Sum(f => f.Debe - f.Haber)))} en Norma 43", "Icono.bank",
            "Llega con las pantallas de Norma 43: buscará el importe de la selección en las líneas del extracto de la cuenta"));
        menu.Items.Add(new Separator());

        var escribe = _vm.PuedeEscribir;
        var soloConsulta = _vm.PorQueSoloConsulta;
        var todosPunteados = sel.All(f => f.Punteado);
        menu.Items.Add(Opcion(todosPunteados ? "Quitar el punteo" : "Puntear", "Icono.check", () => _ = _vm.PuntearAsync(sel, !todosPunteados), "F4",
            todosPunteados ? "Quita el punteo de los apuntes de la selección" : "Puntea los apuntes de la selección (también en asientos automáticos y en ejercicios cerrados); queda en su historial",
            escribe, soloConsulta));
        menu.Items.Add(Opcion("Asignar número de factura…", "Icono.doc", () => _ = _vm.AsignarNumeroAsync(sel), "Ctrl+N",
            "Pone el número de factura a los apuntes de la selección, para que se agrupen con su factura; también en automáticos y cerrados", escribe, soloConsulta));
        var automaticos = sel.Any(f => f.Fila.EsAutomatico);
        var cerrados = sel.Any(f => f.Fila.EjercicioCerrado);
        var porQueNoCambiar = !escribe ? soloConsulta : automaticos ? MayorVM.MotivoAutomatico : cerrados ? "Hay filas de un ejercicio cerrado: sus apuntes son de solo consulta" : null;
        menu.Items.Add(Opcion("Cambiar el concepto…", "Icono.edit", () => _ = _vm.CambiarConceptoAsync(sel), null,
            "Cambia el concepto de los apuntes manuales de la selección, con su motivo en el historial", porQueNoCambiar is null, porQueNoCambiar));
        menu.Items.Add(Opcion("Cambiar la subcuenta del apunte…", "Icono.swap", () => _ = _vm.CambiarCuentaAsync(sel), null,
            "Pasa los apuntes manuales de la selección a otra subcuenta, con su motivo en el historial", porQueNoCambiar is null, porQueNoCambiar));
        var contrapartida = una is not null && una.Fila.IdsApunteContrapartida.Count > 0;
        menu.Items.Add(Opcion(contrapartida ? $"Cambiar la contrapartida {una!.Contrapartida}…" : "Cambiar la contrapartida…", "Icono.swap",
            () => _ = _vm.CambiarContrapartidaAsync(una!), null, "Pasa la línea de la contrapartida de este asiento a otra subcuenta, con su motivo en el historial",
            porQueNoCambiar is null && contrapartida,
            porQueNoCambiar ?? (una is null ? "Elige una sola fila" : "Esta fila no tiene una contrapartida única: ábrela con «Ver asiento»")));
        menu.Items.Add(new Separator());
        menu.Items.Add(Proxima("Nuevo asiento manual…", "Icono.plus", "Llega con el diario de asientos (pantallas de contabilidad): abrirá un asiento nuevo con la cuenta y el importe de la selección"));
        menu.Items.Add(Proxima("Asistente de corrección…", "Icono.wand", "Llega con las plantillas de asiento: propondrá el asiento de corrección (inverso, contra-apunte, cambio de cuenta…) con su IVA"));
        var cuentasSel = sel.Select(f => f.Cuenta).Distinct().ToList();
        var deCuentas = cuentasSel.Count == 1 ? $"la cuenta {cuentasSel[0]}" : $"las {cuentasSel.Count} cuentas de la selección";
        menu.Items.Add(Opcion("Marcar como cuadrada…", "Icono.check", () => _ = _vm.MarcarCuadradaAsync(sel), null,
            $"Marca {deCuentas} como cuadrada en los ejercicios de la vista, con el saldo de cada ejercicio y un comentario", escribe, soloConsulta));
        menu.Items.Add(Opcion("Quitar la marca de cuadrada", "Icono.undo", () => _ = _vm.DesmarcarCuadradaAsync(sel), null,
            $"Quita la marca de cuadrada de {deCuentas} en los ejercicios de la vista", escribe, soloConsulta));
        menu.Items.Add(Opcion("Reiniciar el cuadre…", "Icono.undo", () => _ = _vm.ReiniciarCuadreAsync(), null,
            "Marca como cuadradas todas las subcuentas con saldo dentro del umbral (Parámetros y tablas) en los ejercicios de la vista", escribe, soloConsulta));
        menu.Items.Add(new Separator());
        menu.Items.Add(Opcion("Quitar de la vista", "Icono.hide", () => _vm.Quitar(sel), "Supr",
            "Quita las filas solo de la vista (no se borra nada) y recalcula el saldo; se recuperan quitando la etiqueta"));
        menu.Items.Add(Opcion("Copiar las filas", "Icono.copy", () => ApplicationCommands.Copy.Execute(null, Tabla), "Ctrl+C", "Copia las filas seleccionadas para pegarlas en Excel"));
        menu.Items.Add(Opcion("Exportar a Excel", "Icono.xls", () => Tabla.ExportarAExcel(), null, "Exporta todas las filas de la vista con las columnas visibles, en el mismo orden"));
        menu.Items.Add(Opcion("Imprimir la vista…", "Icono.print", Imprimir, "Ctrl+P", "Imprime la vista con sus cuentas, ejercicios, filtros, columnas visibles y totales"));
    }

    private MenuItem IrAContrapartida(FilaMayorVM? una)
    {
        var cuentas = una?.Fila.CuentasContrapartida ?? [];
        if (cuentas.Count <= 1)
            return Opcion(cuentas.Count == 1 ? $"Ir a la contrapartida {cuentas[0]}" : "Ir a la contrapartida", "Icono.go",
                () => _ = _vm!.IrACuentaAsync(cuentas[0]), null, "Abre el mayor de la subcuenta de destino (también con doble clic en la contrapartida)",
                cuentas.Count == 1, una is null ? "Elige una sola fila" : "Esta fila no tiene contrapartida (es una apertura o solo tiene IVA)");
        var it = new MenuItem { Header = "Ir a la contrapartida", ToolTip = "Esta fila tiene varias contrapartidas: elige cuál" };
        PonerIcono(it, "Icono.go");
        foreach (var c in cuentas)
        {
            var cuenta = c;
            var nombre = _vm!.Sesion.Nombre(cuenta);
            it.Items.Add(Opcion(nombre.Length > 0 ? $"{cuenta} {nombre}" : cuenta, "Icono.go", () => _ = _vm.IrACuentaAsync(cuenta), null, "Abre el mayor de " + cuenta));
        }
        return it;
    }

    private MenuItem Opcion(string texto, string icono, Action accion, string? atajo, string ayuda, bool activa = true, string? porQueNo = null)
    {
        var it = new MenuItem { Header = texto, InputGestureText = atajo ?? "", ToolTip = activa ? ayuda : porQueNo ?? ayuda, IsEnabled = activa };
        ToolTipService.SetShowOnDisabled(it, true);
        PonerIcono(it, icono);
        it.Click += (_, _) => accion();
        return it;
    }

    /// <summary>Acción que llega con un nodo aún no construido: se ve desactivada y su ayuda dice cuándo llega.</summary>
    private MenuItem Proxima(string texto, string icono, string cuando, string? atajo = null)
    {
        var it = new MenuItem { Header = texto, InputGestureText = atajo ?? "", ToolTip = cuando, IsEnabled = false };
        ToolTipService.SetShowOnDisabled(it, true);
        PonerIcono(it, icono);
        return it;
    }

    private void PonerIcono(MenuItem it, string icono)
    {
        if (TryFindResource(icono) is Geometry g)
            it.Icon = new System.Windows.Shapes.Path { Data = g, Style = TryFindResource("Icono.Trazo14") as Style };
    }

    private void Imprimir()
    {
        if (_vm is null) return;
        if (Tabla.Items.Count == 0) { _vm.Aviso("No hay filas que imprimir: elige una cuenta o quita los filtros."); return; }
        ImpresionMayor.Imprimir(Tabla, _vm.Titulo, _vm.DescripcionVista(),
            $"{_vm.TextoRecuento} · Debe {_vm.TotalDebe} · Haber {_vm.TotalHaber} · Saldo {_vm.TotalSaldo}" + (_vm.AvisoParcial is { } p ? " · " + p : ""), _vm.Aviso);
    }

    // ---------- recientes ----------

    private void Recientes_Clic(object sender, MouseButtonEventArgs e)
    {
        if (_vm is null || Ascendiente<ButtonBase>(e.OriginalSource as DependencyObject) is not null) return;
        _ = _vm.VerRecientesAsync(ListaRecientes.SelectedItems.OfType<CuentaReciente>().ToList());
    }

    private void Recientes_Tecla(object sender, KeyEventArgs e)
    {
        if (_vm is null) return;
        if (e.Key == Key.Delete)
        {
            foreach (var r in ListaRecientes.SelectedItems.OfType<CuentaReciente>().ToList()) _vm.QuitarReciente(r);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            _ = _vm.VerRecientesAsync(ListaRecientes.SelectedItems.OfType<CuentaReciente>().ToList());
            e.Handled = true;
        }
    }

    // ---------- ejercicios ----------

    private void BotonEjercicios_Click(object sender, RoutedEventArgs e) => PopupEjercicios.IsOpen = true;

    private void PopupEjercicios_Closed(object? sender, EventArgs e) => _vm?.AplicarEjerciciosCommand.Execute(null);

    private static T? Ascendiente<T>(DependencyObject? o) where T : DependencyObject
    {
        while (o is not null && o is not T)
            o = o is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(o) : LogicalTreeHelper.GetParent(o);
        return o as T;
    }

    private sealed class RelayCommand(Action accion) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => accion();
    }
}
