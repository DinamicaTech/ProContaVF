using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Contabilidad.UI;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Norma43.UI.Vistas;

/// <summary>Diálogos de la pantalla de líneas N43 sobre la ventana activa de la aplicación.</summary>
public sealed class DialogosLineasN43(ICatalogoCuentas cuentas, Func<Window?> ventana) : IDialogosLineasN43
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(Activa(), titulo, mensaje, detalles, opciones);

    public string? PedirMotivo(string titulo, string explicacion, string textoBoton)
    {
        var dialogo = new DialogoProConta { Title = titulo, Owner = Activa(), Width = 560 };
        var caja = new TextBox { MaxLength = 250, Style = (Style)dialogo.FindResource("Campo"), ToolTip = "Queda registrado en el historial del asiento con tu nombre y la hora" };
        var error = new TextBlock { Foreground = (Brush)dialogo.FindResource("Pincel.RojoError"), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Motivo", Style = (Style)dialogo.FindResource("Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(caja);
        panel.Children.Add(error);
        dialogo.Content = panel;
        var aceptar = new Button { Content = textoBoton, Style = (Style)dialogo.FindResource("Boton.Peligro"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, ToolTip = titulo };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No cambia nada" };
        aceptar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(caja.Text)) { error.Text = "Escribe el motivo: queda registrado con el cambio."; error.Visibility = Visibility.Visible; caja.Focus(); return; }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => caja.Focus();
        return dialogo.ShowDialog() == true ? caja.Text.Trim() : null;
    }

    public CuentaElegidaN43? ElegirCuenta(ContextoSociedadEjercicio ejercicio, string titulo, string? texto)
    {
        var vm = new BuscadorCuentasVM(cuentas, ejercicio, [], texto);
        var c = DialogoCuentas.Elegir(Activa(), vm, titulo);
        return c is null ? null : new CuentaElegidaN43(c.IdIdentidadContable, c.Codigo, c.Nombre);
    }

    /// <summary>El asiento que generaría contabilizar la línea, o por qué no se puede (con cómo resolverlo).</summary>
    public void VerPropuesta(PropuestaLineaN43 p)
    {
        var d = new DialogoProConta
        {
            Title = $"Asiento propuesto · {p.Rotulo}", Owner = Activa(), Width = 1000, Height = 480, MinWidth = 600, MinHeight = 280,
            SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        var cab = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        cab.Children.Add(new TextBlock
        {
            Text = p.Asiento is { } a ? $"{a.Descripcion} · {Formatos.Fecha(a.Fecha)}" : "No se puede contabilizar todavía",
            FontWeight = FontWeights.SemiBold, FontSize = (double)d.FindResource("Letra.Destacada"), TextWrapping = TextWrapping.Wrap,
        });
        cab.Children.Add(new TextBlock
        {
            Text = "Aún no está contabilizada: esto es lo que grabaría «Contabilizar» (Ctrl+E). No se ha grabado nada.",
            Foreground = (Brush)d.FindResource("Pincel.Apagado"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
        });
        foreach (var e in p.Errores)
            cab.Children.Add(new Nota { Tipo = TipoNota.Error, Margin = new Thickness(0, 8, 0, 0), Content = new TextBlock { Text = e.TextoOperador(), TextWrapping = TextWrapping.Wrap } });
        foreach (var aviso in p.Avisos)
            cab.Children.Add(new Nota { Tipo = TipoNota.Aviso, Margin = new Thickness(0, 8, 0, 0), Content = new TextBlock { Text = aviso, TextWrapping = TextWrapping.Wrap } });
        var cuerpo = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        cuerpo.Children.Add(cab);
        if (p.Asiento is { } asiento)
        {
            var tabla = new ProConta.UI.Base.Tablas.TablaProConta
            {
                ClavePreferencias = "norma43.lineas.propuesta", TituloExportacion = "Asiento propuesto", IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = asiento.Apuntes.Select(x => new
                {
                    x.Cuenta, x.Concepto, Documento = x.Documento ?? "", Debe = Formatos.ImporteOVacio(x.Debe), Haber = Formatos.ImporteOVacio(x.Haber),
                }).ToList(),
            };
            DataGridTextColumn Col(string titulo, string ruta, double ancho, string estilo)
            {
                var c = new DataGridTextColumn
                {
                    Header = titulo, Binding = new Binding(ruta), ElementStyle = (Style)d.FindResource(estilo),
                    Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star),
                };
                if (estilo == "Texto.Celda.Importe") c.HeaderStyle = (Style)d.FindResource("Tabla.Cabecera.Derecha");
                ProConta.UI.Base.Tablas.TablaProConta.SetClave(c, ruta);
                return c;
            }
            tabla.Columns.Add(Col("Cuenta", "Cuenta", 100, "Texto.Celda.Mono"));
            tabla.Columns.Add(Col("Concepto", "Concepto", 0, "Texto.Celda"));
            tabla.Columns.Add(Col("Documento", "Documento", 120, "Texto.Celda.Mono"));
            tabla.Columns.Add(Col("Debe", "Debe", 120, "Texto.Celda.Importe"));
            tabla.Columns.Add(Col("Haber", "Haber", 120, "Texto.Celda.Importe"));
            var pie = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0), FontWeight = FontWeights.SemiBold,
                Text = $"Debe {Formatos.Importe(asiento.TotalDebe)} · Haber {Formatos.Importe(asiento.TotalHaber)}" + (asiento.Cuadrado ? " · cuadra" : " · no cuadra"),
            };
            DockPanel.SetDock(pie, Dock.Bottom);
            cuerpo.Children.Add(pie);
            cuerpo.Children.Add(tabla);
        }
        d.Content = cuerpo;
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, IsDefault = true, ToolTip = "Cierra la vista previa sin grabar nada" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        d.Botones = cerrar;
        d.ShowDialog();
    }

    /// <summary>
    /// Palabras clave de la línea: el concepto completo (se selecciona un trozo con el ratón para usarlo como criterio), las palabras clave
    /// que ya encajan, para corregirlas o borrarlas, y el formulario de la nueva o la que se corrige.
    /// </summary>
    public void PalabrasClave(PalabrasClaveN43VM vm)
    {
        var d = new DialogoProConta
        {
            Title = "Palabras clave de la línea", Owner = Activa(), Width = 900, Height = 720, MinWidth = 640, MinHeight = 480,
            SizeToContent = SizeToContent.Manual, Redimensionable = true, DataContext = vm,
        };
        Style S(string k) => (Style)d.FindResource(k);
        var apagado = (Brush)d.FindResource("Pincel.Apagado");
        var panel = new StackPanel();
        TextBlock Seccion(string t) => new() { Text = t, Style = S("Texto.Seccion"), Margin = new Thickness(0, 12, 0, 4) };

        panel.Children.Add(new TextBlock { Text = vm.Cabecera, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(Seccion("Concepto completo"));
        var concepto = new TextBox
        {
            Text = vm.Concepto, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, Style = S("Campo.Multilinea"), FontFamily = (FontFamily)d.FindResource("Fuente.Mono"),
            ToolTip = "Selecciona con el ratón el trozo que identifica estos movimientos (el que se repite cada mes) y pulsa «Usar como palabra clave»",
        };
        panel.Children.Add(concepto);
        var botonesSel = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        var usar1 = new Button { Content = "Usar como palabra clave", Style = S("Boton.Pequeno"), Margin = new Thickness(0, 0, 8, 0), ToolTip = "El texto seleccionado en el concepto pasa a ser la palabra clave" };
        var usar2 = new Button { Content = "Usar como segunda palabra", Style = S("Boton.Pequeno"), Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "El texto seleccionado pasa a ser la segunda palabra: la palabra clave solo encaja si el concepto contiene las dos" };
        var quitar2 = new Button { Content = "Quitar la segunda", Style = S("Boton.Enlace"), ToolTip = "La palabra clave encajará solo con la primera" };
        usar1.Click += (_, _) => vm.UsarSeleccion(concepto.SelectedText, segunda: false);
        usar2.Click += (_, _) => vm.UsarSeleccion(concepto.SelectedText, segunda: true);
        quitar2.Click += (_, _) => vm.QuitarSegunda();
        botonesSel.Children.Add(usar1);
        botonesSel.Children.Add(usar2);
        botonesSel.Children.Add(quitar2);
        panel.Children.Add(botonesSel);

        // Formulario
        var tarjeta = new Border { Style = S("Tarjeta"), Padding = new Thickness(14, 10, 14, 12), Margin = new Thickness(0, 12, 0, 0) };
        var form = new StackPanel();
        var tituloForm = new TextBlock { Style = S("Texto.Seccion") };
        tituloForm.SetBinding(TextBlock.TextProperty, nameof(PalabrasClaveN43VM.TituloFormulario));
        form.Children.Add(tituloForm);
        var g = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void Fila(string etiqueta, UIElement control, string ayuda)
        {
            var n = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var e = new TextBlock { Text = etiqueta, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 4), ToolTip = ayuda };
            Grid.SetRow(e, n);
            Grid.SetRow(control, n);
            Grid.SetColumn(control, 1);
            if (control is FrameworkElement fe) { fe.Margin = new Thickness(0, 4, 0, 4); fe.ToolTip ??= ayuda; }
            g.Children.Add(e);
            g.Children.Add(control);
        }
        TextBlock Valor(string ruta)
        {
            var t = new TextBlock { FontFamily = (FontFamily)d.FindResource("Fuente.Mono"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            t.SetBinding(TextBlock.TextProperty, ruta);
            return t;
        }
        Fila("Palabra clave", Valor(nameof(PalabrasClaveN43VM.Palabra)), "Trozo literal del concepto; se elige seleccionándolo arriba");
        Fila("Segunda palabra", Valor(nameof(PalabrasClaveN43VM.Palabra2)), "Opcional: si la hay, el concepto tiene que contener las dos");
        var cuentaPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var textoCuenta = Valor(nameof(PalabrasClaveN43VM.TextoCuenta));
        textoCuenta.Margin = new Thickness(0, 0, 10, 0);
        var elegir = new Button { Content = "Elegir cuenta…", Style = S("Boton.Pequeno"), ToolTip = "Abre el buscador de subcuentas del ejercicio de la línea" };
        elegir.Click += (_, _) => vm.ElegirCuenta();
        cuentaPanel.Children.Add(textoCuenta);
        cuentaPanel.Children.Add(elegir);
        Fila("Cuenta", cuentaPanel, "Cuenta de destino que pondrá a las líneas en que encaje");
        CheckBox Casilla(string ruta, string texto, string ayuda)
        {
            var c = new CheckBox { Content = texto, ToolTip = ayuda, VerticalAlignment = VerticalAlignment.Center };
            c.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(ruta) { Mode = BindingMode.TwoWay });
            return c;
        }
        var alcance = new WrapPanel();
        var soloBanco = Casilla(nameof(PalabrasClaveN43VM.SoloBanco), vm.TextoSoloBanco, "Si la quitas, la palabra clave vale para los movimientos de cualquier banco");
        soloBanco.Margin = new Thickness(0, 0, 16, 0);
        var soloSentido = Casilla(nameof(PalabrasClaveN43VM.SoloSentido), vm.TextoSoloSentido, "Si la quitas, vale para cargos y abonos");
        soloSentido.Margin = new Thickness(0, 0, 16, 0);
        alcance.Children.Add(soloBanco);
        alcance.Children.Add(soloSentido);
        alcance.Children.Add(Casilla(nameof(PalabrasClaveN43VM.Remesa), "Es una remesa de recibos", "Las remesas se explotarán en recibos (norma43/remesas); la línea toma igualmente su cuenta"));
        Fila("Alcance", alcance, "A qué movimientos se aplica");
        form.Children.Add(g);
        var error = new TextBlock { Foreground = (Brush)d.FindResource("Pincel.RojoError"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        error.SetBinding(TextBlock.TextProperty, nameof(PalabrasClaveN43VM.Error));
        form.Children.Add(error);
        var mensaje = new TextBlock { Foreground = (Brush)d.FindResource("Pincel.Verde"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        mensaje.SetBinding(TextBlock.TextProperty, nameof(PalabrasClaveN43VM.Mensaje));
        form.Children.Add(mensaje);
        var botonesForm = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var guardar = new Button { Content = "Guardar la palabra clave", Style = S("Boton.Primario"), Margin = new Thickness(0, 0, 8, 0),
            ToolTip = "Guarda la palabra clave; las líneas no cambian hasta reasignarlas (al cerrar te lo ofrezco)" };
        var nueva = new Button { Content = "Empezar una nueva", Style = S("Boton.Fantasma"), ToolTip = "Vacía el formulario para crear otra palabra clave" };
        guardar.Click += async (_, _) => await vm.GuardarAsync();
        nueva.Click += (_, _) => vm.Nueva();
        botonesForm.Children.Add(guardar);
        botonesForm.Children.Add(nueva);
        form.Children.Add(botonesForm);
        tarjeta.Child = form;
        panel.Children.Add(tarjeta);

        // Existentes
        panel.Children.Add(Seccion("Palabras clave que ya encajan con esta línea"));
        var vacias = new TextBlock { Text = "Ninguna: crea una arriba.", Foreground = apagado };
        vacias.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(PalabrasClaveN43VM.SinExistentes)) { Converter = new BooleanToVisibilityConverter() });
        panel.Children.Add(vacias);
        var lista = new ItemsControl { ItemsSource = vm.Existentes };
        lista.ItemTemplate = PlantillaCodigo.Crear(o =>
        {
            var p = (PalabraExistenteN43VM)o!;
            var fila = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var editar = new Button { Content = "Corregir", Style = S("Boton.Pequeno"), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Carga esta palabra clave en el formulario para corregirla" };
            var borrar = new Button { Content = "Borrar", Style = S("Boton.Pequeno"), Margin = new Thickness(6, 0, 0, 0),
                ToolTip = "La borra; si ya asignó cuentas, la desactiva (las líneas conservan su cuenta)" };
            editar.Click += (_, _) => vm.Editar(p);
            borrar.Click += async (_, _) => await vm.BorrarAsync(p);
            DockPanel.SetDock(borrar, Dock.Right);
            DockPanel.SetDock(editar, Dock.Right);
            fila.Children.Add(borrar);
            fila.Children.Add(editar);
            var textos = new StackPanel();
            textos.Children.Add(new TextBlock { Text = $"{p.Texto} → {p.Cuenta}", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
            textos.Children.Add(new TextBlock { Text = p.Alcance, Foreground = apagado, TextWrapping = TextWrapping.Wrap });
            fila.Children.Add(textos);
            return fila;
        });
        panel.Children.Add(lista);

        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, ToolTip = "Cierra el diálogo; si has cambiado algo, te ofrezco aplicarlo a las líneas" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        d.Botones = cerrar;
        d.Loaded += (_, _) => concepto.Focus();
        d.ShowDialog();
    }

    private Window? Activa() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? ventana();
}
