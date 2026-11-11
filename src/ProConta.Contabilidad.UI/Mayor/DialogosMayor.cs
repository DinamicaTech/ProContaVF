using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Contabilidad.Mayor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Tablas;

namespace ProConta.Contabilidad.UI.Mayor;

/// <summary>
/// Diálogos del Mayor con <see cref="DialogoProConta"/>: selector de cuentas (con saldos y filtros), asiento en solo lectura
/// e historial del apunte (RegistroCambiosApuntes).
/// </summary>
public sealed class DialogosMayor(Func<Window?> propietario) : IDialogosMayor
{
    public string? ElegirCuenta(SelectorCuentasVM vm, string titulo)
    {
        var d = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 1000, Height = 680, MinWidth = 640, MinHeight = 360, SizeToContent = SizeToContent.Manual, Redimensionable = true };
        var busqueda = new TextBox { ToolTip = "Código (empieza por) o parte de la descripción. Un código corto, como 572, se puede elegir tal cual para ver todas sus subcuentas. ↓: ir a la lista · Intro: elegir" };
        busqueda.SetBinding(TextBox.TextProperty, new Binding(nameof(SelectorCuentasVM.Texto)) { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged, Delay = 150 });
        CheckBox Casilla(string texto, string propiedad, string ayuda)
        {
            var c = new CheckBox { Content = texto, Margin = new Thickness(0, 0, 16, 0), ToolTip = ayuda, VerticalAlignment = VerticalAlignment.Center };
            c.SetBinding(ToggleButtonIsChecked, new Binding(propiedad));
            return c;
        }
        var filtros = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        filtros.Children.Add(Casilla("Solo subcuentas", nameof(SelectorCuentasVM.SoloSubcuentas), "Solo las cuentas que admiten apuntes; sin marcar, también las cuentas de grupo (que muestran todas sus subcuentas)"));
        filtros.Children.Add(Casilla("Con movimientos", nameof(SelectorCuentasVM.ConMovimientos), "Solo las cuentas con algún apunte en los ejercicios elegidos"));
        filtros.Children.Add(Casilla("Con saldo", nameof(SelectorCuentasVM.ConSaldo), "Saldo distinto de cero en los ejercicios elegidos, con la apertura"));
        filtros.Children.Add(Casilla("Con saldo sin la apertura", nameof(SelectorCuentasVM.ConSaldoSinApertura), "Los movimientos del periodo no netean a cero, sin contar la apertura"));
        var noCuadradas = new CheckBox { Content = "No cuadradas", IsEnabled = false, VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "Llega con el cuadre, en la próxima entrega del Mayor: ocultará las cuentas marcadas como cuadradas" };
        ToolTipService.SetShowOnDisabled(noCuadradas, true);
        filtros.Children.Add(noCuadradas);
        var estado = new TextBlock { Margin = new Thickness(0, 8, 0, 8), Foreground = (Brush)d.FindResource("Pincel.Apagado"), TextWrapping = TextWrapping.Wrap };
        estado.SetBinding(TextBlock.TextProperty, new Binding(nameof(SelectorCuentasVM.Estado)));

        var tabla = new TablaProConta { ClavePreferencias = "mayor.selector", TituloExportacion = "Cuentas", IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single };
        tabla.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SelectorCuentasVM.Resultados)));
        var importe = new ImporteConverter();
        DataGridTextColumn Col(string titulo, string ruta, double ancho, string clave, string estilo, IValueConverter? conv = null, bool derecha = false)
        {
            var c = new DataGridTextColumn
            {
                Header = titulo, Binding = new Binding(ruta) { Converter = conv }, SortMemberPath = ruta, Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = (Style)d.FindResource(estilo),
            };
            if (derecha) c.HeaderStyle = (Style)d.FindResource("Tabla.Cabecera.Derecha");
            TablaProConta.SetClave(c, clave);
            TablaProConta.SetRutaExportar(c, ruta);
            return c;
        }
        tabla.Columns.Add(Col("Cuenta", nameof(CuentaSelectorVM.Codigo), 100, "cta", "Texto.Celda.Mono"));
        var desc = Col("Descripción", nameof(CuentaSelectorVM.Nombre), 0, "nom", "Texto.Celda");
        desc.MinWidth = 260;
        TablaProConta.SetSiempreVisible(desc, true);
        tabla.Columns.Add(desc);
        tabla.Columns.Add(Col("Saldo sin apertura", nameof(CuentaSelectorVM.SaldoSinApertura), 140, "ssa", "Texto.Celda.Importe", importe, true));
        tabla.Columns.Add(Col("Saldo", nameof(CuentaSelectorVM.Saldo), 130, "sal", "Texto.Celda.Importe", importe, true));
        tabla.Columns.Add(Col("Apuntes", nameof(CuentaSelectorVM.Apuntes), 80, "apu", "Texto.Celda.Importe", null, true));
        tabla.Columns.Add(Col("Tipo", nameof(CuentaSelectorVM.Tipo), 70, "tipo", "Texto.Celda"));

        var corto = new Button { Style = (Style)d.FindResource("Boton.Enlace"), Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Muestra juntas todas las subcuentas que empiezan por ese código (mayor de grupo)" };
        corto.SetBinding(ContentControl.ContentProperty, new Binding(nameof(SelectorCuentasVM.TextoCodigoCorto)));
        corto.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(SelectorCuentasVM.CodigoCorto)) { Converter = new VisibleSiConverter() });

        var cuerpo = new DockPanel { DataContext = vm };
        foreach (var e in new UIElement[] { busqueda, filtros, estado, corto }) { DockPanel.SetDock(e, Dock.Top); cuerpo.Children.Add(e); }
        cuerpo.Children.Add(tabla);
        d.Content = cuerpo;

        string? elegida = null;
        void Aceptar()
        {
            if (tabla.SelectedItem is CuentaSelectorVM c) { elegida = c.Codigo; d.DialogResult = true; }
            else if (vm.CodigoCorto is { } cc) { elegida = cc; d.DialogResult = true; }
            else if (vm.Resultados.Count == 1) { elegida = vm.Resultados[0].Codigo; d.DialogResult = true; }
        }
        corto.Click += (_, _) => { elegida = vm.CodigoCorto; d.DialogResult = elegida is not null; };
        tabla.MouseDoubleClick += (_, e) => { if (tabla.SelectedItem is not null) { Aceptar(); e.Handled = true; } };
        tabla.PreviewKeyDown += (_, e) => { if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; } };
        busqueda.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && tabla.Items.Count > 0)
            {
                tabla.SelectedIndex = 0;
                tabla.UpdateLayout();
                (tabla.ItemContainerGenerator.ContainerFromIndex(0) as DataGridRow)?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
            else if (e.Key == Key.Enter) { Aceptar(); e.Handled = true; }
        };
        var elegir = new Button { Content = "Ver su mayor", Style = (Style)d.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Abre el mayor de la cuenta seleccionada (también doble clic o Intro)" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar la vista" };
        elegir.Click += (_, _) => Aceptar();
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, elegir } };
        d.Loaded += (_, _) => busqueda.Focus();
        return d.ShowDialog() == true ? elegida : null;
    }

    private static readonly DependencyProperty ToggleButtonIsChecked = System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty;

    public void VerAsiento(AsientoMayor a, Action<string> abrirMayor)
    {
        var d = new DialogoProConta
        {
            Title = $"Asiento {a.IdAsiento} · {Formatos.Fecha(a.Fecha)}", Owner = propietario(), Width = 1100, Height = 560, MinWidth = 640, MinHeight = 320,
            SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        var apagado = (Brush)d.FindResource("Pincel.Apagado");
        var cab = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        cab.Children.Add(new TextBlock { Text = a.Descripcion, FontWeight = FontWeights.SemiBold, FontSize = (double)d.FindResource("Letra.Destacada"), TextWrapping = TextWrapping.Wrap });
        var datos = new List<string> { $"Ejercicio {a.Ejercicio}", a.TextoTipo };
        if (a.EsAutomatico) datos.Add("generado por " + (a.Origen ?? "un proceso automático") + " (se cambia desde su origen)");
        if (a.GrabadoEl is { } g) datos.Add($"grabado el {Formatos.Fecha(g)}" + (string.IsNullOrWhiteSpace(a.GrabadoPor) ? "" : $" por {a.GrabadoPor}"));
        if (a.ModificadoEl is { } m) datos.Add($"modificado el {Formatos.Fecha(m)}" + (string.IsNullOrWhiteSpace(a.ModificadoPor) ? "" : $" por {a.ModificadoPor}"));
        cab.Children.Add(new TextBlock { Text = string.Join(" · ", datos), Foreground = apagado, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        if (!string.IsNullOrWhiteSpace(a.Notas))
            cab.Children.Add(new Nota { Tipo = TipoNota.Info, Margin = new Thickness(0, 8, 0, 0), Content = new TextBlock { Text = a.Notas, TextWrapping = TextWrapping.Wrap } });

        var tabla = new TablaProConta { ClavePreferencias = "mayor.asiento", TituloExportacion = $"Asiento {a.IdAsiento}", IsReadOnly = true, ItemsSource = a.Lineas, SelectionMode = DataGridSelectionMode.Single };
        var importe = new ImporteConverter();
        DataGridTextColumn Col(string titulo, string ruta, double ancho, string estilo, bool importeVacio = false)
        {
            var c = new DataGridTextColumn
            {
                Header = titulo, Binding = new Binding(ruta) { Converter = importeVacio ? importe : null, ConverterParameter = importeVacio ? "vacio" : null },
                Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = (Style)d.FindResource(estilo), SortMemberPath = ruta,
            };
            if (importeVacio) c.HeaderStyle = (Style)d.FindResource("Tabla.Cabecera.Derecha");
            TablaProConta.SetClave(c, ruta);
            return c;
        }
        tabla.Columns.Add(Col("Orden", nameof(LineaAsientoMayor.Orden), 60, "Texto.Celda"));
        tabla.Columns.Add(Col("Cuenta", nameof(LineaAsientoMayor.Cuenta), 100, "Texto.Celda.Mono"));
        tabla.Columns.Add(Col("Descripción de la cuenta", nameof(LineaAsientoMayor.NombreCuenta), 220, "Texto.Celda"));
        var concepto = Col("Concepto", nameof(LineaAsientoMayor.Concepto), 0, "Texto.Celda");
        concepto.MinWidth = 200;
        TablaProConta.SetSiempreVisible(concepto, true);
        tabla.Columns.Add(concepto);
        tabla.Columns.Add(Col("Documento", nameof(LineaAsientoMayor.Documento), 130, "Texto.Celda.Mono"));
        tabla.Columns.Add(Col("Debe", nameof(LineaAsientoMayor.Debe), 120, "Texto.Celda.Importe", true));
        tabla.Columns.Add(Col("Haber", nameof(LineaAsientoMayor.Haber), 120, "Texto.Celda.Importe", true));
        tabla.ToolTip = "Doble clic en una línea: abre el mayor de su cuenta";
        tabla.MouseDoubleClick += (_, _) =>
        {
            if (tabla.SelectedItem is LineaAsientoMayor l) { d.DialogResult = true; abrirMayor(l.Cuenta); }
        };
        var cuadra = a.TotalDebe == a.TotalHaber;
        var pie = new TextBlock
        {
            Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold,
            Text = $"Debe {Formatos.Importe(a.TotalDebe)} · Haber {Formatos.Importe(a.TotalHaber)}" + (cuadra ? " · cuadra" : $" · descuadre de {Formatos.Importe(a.TotalDebe - a.TotalHaber)}"),
            Foreground = cuadra ? (Brush)d.FindResource("Pincel.Tinta") : (Brush)d.FindResource("Pincel.RojoError"),
        };
        var cuerpo = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        DockPanel.SetDock(pie, Dock.Bottom);
        cuerpo.Children.Add(cab);
        cuerpo.Children.Add(pie);
        cuerpo.Children.Add(tabla);
        d.Content = cuerpo;
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, IsDefault = true, ToolTip = "Cierra el asiento (solo lectura: aquí no se cambia nada)" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        d.Botones = cerrar;
        d.ShowDialog();
    }

    public void VerHistorial(FilaMayor fila, IReadOnlyList<CambioApunteMayor> cambios)
    {
        var d = new DialogoProConta
        {
            Title = $"Historial del apunte · asiento {fila.IdAsiento}, línea {fila.Orden}", Owner = propietario(), Width = 1100, Height = 520, MinWidth = 600, MinHeight = 280,
            SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        var cab = new TextBlock
        {
            Text = $"{fila.Cuenta} · {fila.Concepto} · {Formatos.Importe(fila.Debe - fila.Haber)}", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), FontWeight = FontWeights.SemiBold,
        };
        UIElement contenido;
        if (cambios.Count == 0)
            contenido = new VistaEstado
            {
                Estado = EstadoVista.Vacio, Titulo = "Sin cambios guardados",
                Mensaje = "El apunte está como se grabó" + (fila.GrabadoEl is { } g ? $" el {Formatos.Fecha(g)}" : "") + ": ningún operador ni proceso lo ha cambiado desde entonces.",
            };
        else
        {
            var tabla = new TablaProConta { ClavePreferencias = "mayor.historial", TituloExportacion = "Historial del apunte", IsReadOnly = true, ItemsSource = cambios };
            DataGridTextColumn Col(string titulo, string ruta, double ancho, IValueConverter? conv = null)
            {
                var c = new DataGridTextColumn
                {
                    Header = titulo, Binding = new Binding(ruta) { Converter = conv }, SortMemberPath = ruta,
                    Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = (Style)d.FindResource("Texto.Celda"),
                };
                TablaProConta.SetClave(c, ruta);
                return c;
            }
            tabla.Columns.Add(Col("Fecha", nameof(CambioApunteMayor.FechaHora), 140, new FechaHoraConverter()));
            tabla.Columns.Add(Col("Operador", nameof(CambioApunteMayor.Operador), 130));
            tabla.Columns.Add(Col("Operación", nameof(CambioApunteMayor.Operacion), 100));
            tabla.Columns.Add(Col("Campo", nameof(CambioApunteMayor.Campo), 120));
            tabla.Columns.Add(Col("Antes", nameof(CambioApunteMayor.Antes), 150));
            tabla.Columns.Add(Col("Después", nameof(CambioApunteMayor.Despues), 150));
            var motivo = Col("Motivo", nameof(CambioApunteMayor.Motivo), 0);
            motivo.MinWidth = 200;
            tabla.Columns.Add(motivo);
            tabla.Columns.Add(Col("Lote", nameof(CambioApunteMayor.Lote), 120));
            TablaProConta.SetOcultaDeFabrica(tabla.Columns[^1], true);
            contenido = tabla;
        }
        var cuerpo = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        cuerpo.Children.Add(cab);
        cuerpo.Children.Add(contenido);
        d.Content = cuerpo;
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, IsDefault = true, ToolTip = "Cierra el historial" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        d.Botones = cerrar;
        d.ShowDialog();
    }

    public void Error(string titulo, string mensaje) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, null, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));

    private sealed class FechaHoraConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is DateTime f ? f.ToString("dd/MM/yyyy HH:mm", Formatos.Cultura) : "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
