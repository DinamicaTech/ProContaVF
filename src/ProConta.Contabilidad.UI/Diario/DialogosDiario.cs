using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Diario;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Tablas;

namespace ProConta.Contabilidad.UI.Diario;

/// <summary>Diálogos de la pantalla Diario con <see cref="DialogoProConta"/>; los de texto y motivo son los mismos del Mayor.</summary>
public sealed class DialogosDiario(ICatalogoCuentas cuentas, ICatalogoCentrosCoste centros, Func<Window?> propietario) : IDialogosDiario
{
    private readonly Mayor.DialogosMayor _comunes = new(propietario);

    public void Error(string titulo, string mensaje) => _comunes.Error(titulo, mensaje);

    public (string Valor, string Motivo)? PedirTexto(string titulo, string explicacion, string etiqueta, string? inicial, int longitud, bool pideMotivo, bool valorObligatorio) =>
        _comunes.PedirTexto(titulo, explicacion, etiqueta, inicial, longitud, pideMotivo, valorObligatorio);

    public string? PedirMotivo(string titulo, string explicacion) => _comunes.PedirMotivo(titulo, explicacion);

    /// <summary>El buscador común: solo subcuentas, con el nombre en la segunda columna, «Crear subcuenta…» y ventana redimensionable.</summary>
    public (string Codigo, string Nombre)? ElegirCuenta(ContextoSociedadEjercicio ejercicio, string titulo, string? texto)
    {
        var vm = new BuscadorCuentasVM(cuentas, ejercicio, [], texto, centros);
        return DialogoCuentas.Elegir(propietario(), vm, titulo) is { } c ? (c.Codigo, c.Nombre) : null;
    }

    public (CentroDiario Centro, string Motivo)? ElegirCentro(IReadOnlyList<CentroDiario> lista, string explicacion)
    {
        var d = new DialogoProConta { Title = "Asignar centro de coste", Owner = propietario(), Width = 620, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Centro de coste", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var combo = new ComboBox { ItemsSource = lista, DisplayMemberPath = nameof(CentroDiario.Texto), SelectedIndex = 0, ToolTip = "Centros de coste activos e imputables; «Sin centro» lo quita" };
        panel.Children.Add(combo);
        panel.Children.Add(new TextBlock { Text = "Motivo", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var motivo = new TextBox { MaxLength = 255, ToolTip = "Por qué haces el cambio: queda en el historial del asiento con tu nombre y la fecha" };
        panel.Children.Add(motivo);
        var aviso = new TextBlock { Foreground = (Brush)d.FindResource("Pincel.RojoError"), Text = "Escribe el motivo: queda en el historial del asiento.", Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(aviso);
        d.Content = panel;
        var aceptar = new Button { Content = "Asignar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Asigna el centro elegido a los apuntes seleccionados" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aceptar.Click += (_, _) => { if (string.IsNullOrWhiteSpace(motivo.Text)) { aviso.Visibility = Visibility.Visible; motivo.Focus(); } else d.DialogResult = true; };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => combo.Focus();
        return d.ShowDialog() == true && combo.SelectedItem is CentroDiario c ? (c, motivo.Text.Trim()) : null;
    }

    public string? ConfirmarBorrado(IReadOnlyList<AsientoDiario> asientos)
    {
        var uno = asientos.Count == 1;
        var d = new DialogoProConta { Title = uno ? $"Eliminar el asiento {asientos[0].IdAsiento}" : $"Eliminar {asientos.Count} asientos", Owner = propietario(), Width = 680, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = (uno ? "Se borra completo, con todos sus apuntes." : "Se borran completos, con todos sus apuntes.") +
                   " Queda una copia en «Asientos borrados…», con el motivo, quién y cuándo. Los números no se reutilizan.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        });
        var lista = new TextBox
        {
            Text = string.Join(Environment.NewLine, asientos.Select(a => $"Asiento {a.IdAsiento} · {Formatos.Fecha(a.Fecha)} · {a.Descripcion} · {Formatos.Importe(a.TotalDebe)}")),
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 180, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = double.NaN, Padding = new Thickness(8, 6, 8, 6),
            Background = (Brush)d.FindResource("Pincel.RojoSuave"), BorderBrush = (Brush)d.FindResource("Pincel.RojoBorde"), ToolTip = "Lo que se va a borrar; se puede seleccionar y copiar",
        };
        panel.Children.Add(lista);
        panel.Children.Add(new TextBlock { Text = "Motivo (obligatorio)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 4) });
        var motivo = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 64, Height = double.NaN, VerticalContentAlignment = VerticalAlignment.Top, Padding = new Thickness(8, 6, 8, 6), MaxLength = 1000,
            ToolTip = "Por qué se borra. Queda en el registro de asientos borrados",
        };
        panel.Children.Add(motivo);
        d.Content = panel;
        var eliminar = new Button { Content = "Eliminar", Style = (Style)d.FindResource("Boton.Peligro"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false, ToolTip = "Escribe el motivo para poder eliminar" };
        ToolTipService.SetShowOnDisabled(eliminar, true);
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin borrar nada" };
        motivo.TextChanged += (_, _) =>
        {
            eliminar.IsEnabled = !string.IsNullOrWhiteSpace(motivo.Text);
            eliminar.ToolTip = eliminar.IsEnabled ? (uno ? "Borra el asiento y guarda su copia" : "Borra los asientos indicados y guarda su copia") : "Escribe el motivo para poder eliminar";
        };
        eliminar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, eliminar } };
        d.Loaded += (_, _) => motivo.Focus();
        return d.ShowDialog() == true ? motivo.Text.Trim() : null;
    }

    private sealed record FilaBorrado(AsientoBorrado Asiento)
    {
        public int Numero => Asiento.IdAsiento;
        public DateTime Fecha => Asiento.Fecha;
        public string Descripcion => Asiento.Descripcion;
        public decimal Importe => Asiento.TotalDebe;
        public DateTime BorradoEl => Asiento.FechaHoraBorrado;
        public string Operador => Asiento.Operador;
        public string Motivo => Asiento.Motivo;
    }

    private sealed record FilaApunteBorrado(ApunteBorrado Apunte, string Nombre)
    {
        public int IdApunte => Apunte.IdApunte;
        public string Cuenta => Apunte.Cuenta;
        public string Concepto => Apunte.Concepto ?? "";
        public string Documento => Apunte.Documento ?? "";
        public decimal Debe => Apunte.Debe;
        public decimal Haber => Apunte.Haber;
    }

    public void VerBorrados(IReadOnlyList<AsientoBorrado> borrados, Func<AsientoBorrado, ApunteBorrado, string> nombreCuenta, string ejercicios)
    {
        var d = new DialogoProConta
        {
            Title = "Asientos borrados", Owner = propietario(), Width = 1180, Height = 640, MinWidth = 640, MinHeight = 360, SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        var cab = new TextBlock
        {
            Text = $"Copia de los asientos manuales eliminados de {ejercicios} (AsientosBorrados y ApuntesBorrados). Solo se consultan. Elige uno para ver sus apuntes.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        };
        var cuerpo = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        cuerpo.Children.Add(cab);
        if (borrados.Count == 0)
            cuerpo.Children.Add(new VistaEstado { Estado = EstadoVista.Vacio, Titulo = "No se ha borrado ningún asiento", Mensaje = $"En {ejercicios} no se ha eliminado ningún asiento manual desde el Diario." });
        else
        {
            var importe = new ImporteConverter();
            var fecha = new FechaConverter();
            DataGridTextColumn Col(string titulo, string ruta, double ancho, string estilo, string clave, string ayuda, IValueConverter? conv = null, object? parametro = null, bool derecha = false)
            {
                var c = new DataGridTextColumn
                {
                    Header = titulo, Binding = new Binding(ruta) { Converter = conv, ConverterParameter = parametro }, SortMemberPath = ruta,
                    Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = (Style)d.FindResource(estilo),
                };
                if (derecha) c.HeaderStyle = (Style)d.FindResource("Tabla.Cabecera.Derecha");
                TablaProConta.SetClave(c, clave);
                TablaProConta.SetAyuda(c, ayuda);
                return c;
            }
            var tabla = new TablaProConta { ClavePreferencias = "diario.borrados", TituloExportacion = "Asientos borrados", IsReadOnly = true, SelectionMode = DataGridSelectionMode.Single,
                ItemsSource = borrados.Select(b => new FilaBorrado(b)).ToList() };
            tabla.Columns.Add(Col("Asiento", nameof(FilaBorrado.Numero), 80, "Texto.Celda.Importe", "asiento", "Número que tenía el asiento; no se reutiliza", derecha: true));
            tabla.Columns.Add(Col("Fecha", nameof(FilaBorrado.Fecha), 96, "Texto.Celda.Mono", "fecha", "Fecha contable que tenía", fecha));
            tabla.Columns.Add(Col("Descripción", nameof(FilaBorrado.Descripcion), 300, "Texto.Celda", "descripcion", "Descripción que tenía"));
            tabla.Columns.Add(Col("Importe", nameof(FilaBorrado.Importe), 116, "Texto.Celda.Importe", "importe", "Total del Debe", importe, derecha: true));
            tabla.Columns.Add(Col("Borrado el", nameof(FilaBorrado.BorradoEl), 140, "Texto.Celda.Mono", "borrado", "Cuándo se borró", new FechaHoraConverter()));
            tabla.Columns.Add(Col("Operador", nameof(FilaBorrado.Operador), 120, "Texto.Celda", "operador", "Quién lo borró"));
            var motivo = Col("Motivo", nameof(FilaBorrado.Motivo), 0, "Texto.Celda", "motivo", "Motivo que se dio al borrarlo");
            motivo.MinWidth = 220;
            tabla.Columns.Add(motivo);

            var titulo = new TextBlock { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 6), Text = "Apuntes del asiento" };
            var motivoCompleto = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0),
                Height = double.NaN, Margin = new Thickness(0, 0, 0, 6), ToolTip = "Motivo del borrado; se puede seleccionar y copiar" };
            var apuntes = new TablaProConta { ClavePreferencias = "diario.borrados.apuntes", TituloExportacion = "Apuntes del asiento borrado", IsReadOnly = true, Height = 190 };
            apuntes.Columns.Add(Col("Id apunte", nameof(FilaApunteBorrado.IdApunte), 84, "Texto.Celda.Importe", "id", "Identificador que tenía el apunte", derecha: true));
            apuntes.Columns.Add(Col("Subcuenta", nameof(FilaApunteBorrado.Cuenta), 96, "Texto.Celda.Mono", "cuenta", "Subcuenta del apunte"));
            apuntes.Columns.Add(Col("Descripción", nameof(FilaApunteBorrado.Nombre), 240, "Texto.Celda", "nombre", "Descripción de la subcuenta"));
            var concepto = Col("Concepto", nameof(FilaApunteBorrado.Concepto), 0, "Texto.Celda", "concepto", "Concepto del apunte");
            concepto.MinWidth = 200;
            apuntes.Columns.Add(concepto);
            apuntes.Columns.Add(Col("Documento", nameof(FilaApunteBorrado.Documento), 120, "Texto.Celda.Mono", "documento", "Número de factura o documento"));
            apuntes.Columns.Add(Col("Debe", nameof(FilaApunteBorrado.Debe), 116, "Texto.Celda.Importe", "debe", "Importe al Debe", importe, "vacio", derecha: true));
            apuntes.Columns.Add(Col("Haber", nameof(FilaApunteBorrado.Haber), 116, "Texto.Celda.Importe", "haber", "Importe al Haber", importe, "vacio", derecha: true));
            tabla.SelectionChanged += (_, _) =>
            {
                if (tabla.SelectedItem is not FilaBorrado f) return;
                titulo.Text = $"Apuntes del asiento {f.Numero} · Debe {Formatos.Importe(f.Asiento.Apuntes.Sum(a => a.Debe))} · Haber {Formatos.Importe(f.Asiento.Apuntes.Sum(a => a.Haber))}";
                motivoCompleto.Text = $"Motivo: {f.Motivo}";
                apuntes.ItemsSource = f.Asiento.Apuntes.Select(a => new FilaApunteBorrado(a, nombreCuenta(f.Asiento, a))).ToList();
            };
            var abajo = new DockPanel();
            DockPanel.SetDock(titulo, Dock.Top);
            DockPanel.SetDock(motivoCompleto, Dock.Top);
            abajo.Children.Add(titulo);
            abajo.Children.Add(motivoCompleto);
            abajo.Children.Add(apuntes);
            DockPanel.SetDock(abajo, Dock.Bottom);
            cuerpo.Children.Add(abajo);
            cuerpo.Children.Add(tabla);
            d.Loaded += (_, _) => tabla.SelectedIndex = 0;
        }
        d.Content = cuerpo;
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, IsDefault = true, ToolTip = "Cierra la consulta (aquí no se cambia nada)" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        d.Botones = cerrar;
        d.ShowDialog();
    }

    public FormatoExportacionDiario? ElegirExportacion(string explicacion, bool hayPdf)
    {
        var d = new DialogoProConta { Title = "Exportar el Diario", Owner = propietario(), Width = 600, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        RadioButton Opcion(string titulo, string detalle, bool marcada, bool activa)
        {
            var texto = new StackPanel();
            texto.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold });
            texto.Children.Add(new TextBlock { Text = detalle, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)d.FindResource("Pincel.Apagado") });
            var r = new RadioButton { Content = texto, IsChecked = marcada, IsEnabled = activa, Margin = new Thickness(0, 0, 0, 10), GroupName = "formato", ToolTip = detalle };
            ToolTipService.SetShowOnDisabled(r, true);
            panel.Children.Add(r);
            return r;
        }
        var pdf = Opcion("PDF", hayPdf ? "Para imprimir o enviar a la gestoría, en horizontal. En la vista «Por asiento» sale cada asiento completo."
            : "No disponible: el generador de PDF no está conectado en esta instalación.", hayPdf, hayPdf);
        var excel = Opcion("Excel", "Una fila por apunte, con las columnas que se ven, para trabajar los datos.", !hayPdf, true);
        d.Content = panel;
        var generar = new Button { Content = "Generar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Pregunta dónde guardarlo, genera el fichero con lo filtrado y lo abre" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin generar nada" };
        generar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, generar } };
        if (d.ShowDialog() != true) return null;
        return pdf.IsChecked == true ? FormatoExportacionDiario.Pdf : excel.IsChecked == true ? FormatoExportacionDiario.Excel : null;
    }

    public string? PedirRuta(string titulo, string nombrePropuesto, string filtro)
    {
        var dlg = new SaveFileDialog { Title = titulo, Filter = filtro, FileName = nombrePropuesto, AddExtension = true };
        return dlg.ShowDialog(propietario()) == true ? dlg.FileName : null;
    }

    private sealed record FilaDiferencia(DiferenciaApertura D)
    {
        public string Cuenta => D.Cuenta;
        public string Nombre => D.Nombre;
        public decimal Cierre => D.SaldoCierre;
        public decimal Apertura => D.SaldoApertura;
        public decimal Diferencia => D.Diferencia;
    }

    public void VerDiferenciasApertura(IReadOnlyList<DiferenciaApertura> diferencias, int anterior, int trabajo)
    {
        var d = new DialogoProConta
        {
            Title = $"Apertura de {trabajo} desactualizada", Owner = propietario(), Width = 900, Height = 560, MinWidth = 560, MinHeight = 320, SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        var cab = new TextBlock
        {
            Text = $"En estas cuentas el asiento de apertura de {trabajo} no coincide con el saldo con que acabaron {anterior} (sin contar el asiento de cierre). " +
                   $"Suele pasar al cambiar apuntes de {anterior} después de hacer la apertura. Qué hacer: corrige la apertura de {trabajo} con «Modificar el asiento…» o con un asiento manual por la diferencia. " +
                   "Cuando exista el proceso de apertura, se podrá regenerar desde aquí. Clic derecho en la cabecera de la tabla: exportar a Excel.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10),
        };
        var importe = new ImporteConverter();
        var tabla = new TablaProConta { ClavePreferencias = "diario.apertura", TituloExportacion = $"Diferencias de la apertura de {trabajo}", IsReadOnly = true, SelectionMode = DataGridSelectionMode.Extended,
            ItemsSource = diferencias.Select(x => new FilaDiferencia(x)).ToList() };
        DataGridTextColumn Col(string titulo, string ruta, double ancho, string estilo, string ayuda, bool esImporte = false)
        {
            var c = new DataGridTextColumn
            {
                Header = titulo, Binding = new Binding(ruta) { Converter = esImporte ? importe : null }, SortMemberPath = ruta,
                Width = ancho > 0 ? new DataGridLength(ancho) : new DataGridLength(1, DataGridLengthUnitType.Star), ElementStyle = (Style)d.FindResource(estilo),
            };
            if (esImporte) c.HeaderStyle = (Style)d.FindResource("Tabla.Cabecera.Derecha");
            TablaProConta.SetClave(c, ruta);
            TablaProConta.SetAyuda(c, ayuda);
            return c;
        }
        tabla.Columns.Add(Col("Subcuenta", nameof(FilaDiferencia.Cuenta), 100, "Texto.Celda.Mono", "Subcuenta con diferencia"));
        var nombre = Col("Descripción", nameof(FilaDiferencia.Nombre), 0, "Texto.Celda", "Descripción de la subcuenta");
        nombre.MinWidth = 200;
        tabla.Columns.Add(nombre);
        tabla.Columns.Add(Col($"Saldo al acabar {anterior}", nameof(FilaDiferencia.Cierre), 170, "Texto.Celda.Importe", $"Debe menos Haber de la cuenta en {anterior}, sin el asiento de cierre", true));
        tabla.Columns.Add(Col($"Apertura de {trabajo}", nameof(FilaDiferencia.Apertura), 150, "Texto.Celda.Importe", $"Debe menos Haber de la cuenta en el asiento de apertura de {trabajo}", true));
        tabla.Columns.Add(Col("Diferencia", nameof(FilaDiferencia.Diferencia), 130, "Texto.Celda.Importe", "Apertura menos saldo anterior: lo que le sobra (positivo) o le falta (negativo) a la apertura", true));
        var cuerpo = new DockPanel();
        DockPanel.SetDock(cab, Dock.Top);
        cuerpo.Children.Add(cab);
        cuerpo.Children.Add(tabla);
        d.Content = cuerpo;
        var copiar = new Button { Content = "Copiar la lista", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Copia todas las cuentas con sus importes, separados por tabuladores, para pegarlos en Excel o en un correo" };
        copiar.Click += (_, _) => Copiar(string.Join(Environment.NewLine, new[] { $"Subcuenta\tDescripción\tSaldo al acabar {anterior}\tApertura de {trabajo}\tDiferencia" }
            .Concat(diferencias.Select(x => $"{x.Cuenta}\t{x.Nombre}\t{Formatos.Numero(x.SaldoCierre)}\t{Formatos.Numero(x.SaldoApertura)}\t{Formatos.Numero(x.Diferencia)}"))));
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, IsDefault = true, ToolTip = "Cierra la lista" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { copiar, cerrar } };
        d.ShowDialog();
    }

    public void Copiar(string texto)
    {
        try { Clipboard.SetText(texto); }
        catch (System.Runtime.InteropServices.COMException) { /* El portapapeles está ocupado por otro programa: no es un error de la aplicación. */ }
    }

    /// <summary>
    /// «Columnas…»: lista de todas las columnas con su casilla, «(oculta de fábrica)» y el botón «De fábrica». La elección se guarda por
    /// operador con las preferencias de la tabla, como en el Mayor.
    /// </summary>
    public static void Columnas(Window? propietario, TablaProConta tabla, IReadOnlyList<ColumnaDiario> definiciones)
    {
        var d = new DialogoProConta { Title = "Columnas", Owner = propietario, Width = 440, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Elige las columnas que se ven. Se guardan para ti, como en el Mayor.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var casillas = new List<(CheckBox Casilla, DataGridColumn Columna)>();
        foreach (var col in tabla.Columns.OrderBy(c => c.DisplayIndex))
        {
            var clave = TablaProConta.ClaveDe(col);
            var def = definiciones.FirstOrDefault(x => x.Clave == clave);
            var fija = TablaProConta.GetSiempreVisible(col);
            var texto = new TextBlock();
            texto.Inlines.Add(col.Header?.ToString() ?? clave);
            if (TablaProConta.GetOcultaDeFabrica(col)) texto.Inlines.Add(new System.Windows.Documents.Run("  (oculta de fábrica)") { Foreground = (Brush)d.FindResource("Pincel.Apagado") });
            if (fija) texto.Inlines.Add(new System.Windows.Documents.Run("  (siempre se ve)") { Foreground = (Brush)d.FindResource("Pincel.Apagado") });
            var casilla = new CheckBox { Content = texto, IsChecked = col.Visibility == Visibility.Visible, IsEnabled = !fija, Margin = new Thickness(0, 0, 0, 6), ToolTip = def?.Ayuda ?? TablaProConta.GetAyuda(col) };
            ToolTipService.SetShowOnDisabled(casilla, true);
            casillas.Add((casilla, col));
            panel.Children.Add(casilla);
        }
        var aviso = new TextBlock { Foreground = (Brush)d.FindResource("Pincel.RojoError"), Text = $"Deja al menos {DisposicionColumnas.MinimoVisibles} columnas marcadas.", Visibility = Visibility.Collapsed, Margin = new Thickness(0, 4, 0, 0) };
        panel.Children.Add(aviso);
        d.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560, Content = panel };
        var deFabrica = new Button { Content = "De fábrica", Margin = new Thickness(0, 0, 24, 0), ToolTip = "Marca las columnas de fábrica (al aplicar, vuelven también su orden y sus anchos)" };
        var aplicar = new Button { Content = "Aplicar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Aplica la elección y la guarda para ti" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar las columnas" };
        var restablecer = false;
        deFabrica.Click += (_, _) =>
        {
            foreach (var (casilla, col) in casillas) casilla.IsChecked = !TablaProConta.GetOcultaDeFabrica(col);
            restablecer = true;
        };
        foreach (var (casilla, _) in casillas) casilla.Click += (_, _) => restablecer = false;
        aplicar.Click += (_, _) =>
        {
            if (casillas.Count(c => c.Casilla.IsChecked == true) < DisposicionColumnas.MinimoVisibles) { aviso.Visibility = Visibility.Visible; return; }
            d.DialogResult = true;
        };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { deFabrica, cancelar, aplicar } };
        if (d.ShowDialog() != true) return;
        if (restablecer) { tabla.RestablecerColumnas(); return; }
        // Primero las que se muestran, para no bajar del mínimo de visibles por el camino.
        foreach (var (casilla, col) in casillas.Where(c => c.Casilla.IsChecked == true && c.Columna.Visibility != Visibility.Visible)) tabla.CambiarVisible(col, true);
        foreach (var (casilla, col) in casillas.Where(c => c.Casilla.IsChecked != true && c.Columna.Visibility == Visibility.Visible)) tabla.CambiarVisible(col, false);
    }

    private sealed class FechaHoraConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is DateTime f ? f.ToString("dd/MM/yyyy HH:mm", Formatos.Cultura) : "";
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => Binding.DoNothing;
    }
}
