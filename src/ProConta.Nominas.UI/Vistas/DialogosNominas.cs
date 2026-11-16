using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.Nominas.Importacion;
using ProConta.Nominas.Lectura;
using ProConta.Nominas.UI.VistaModelos;
using ProConta.Terceros.Roles;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Nominas.UI.Vistas;

/// <summary>Diálogos de la pantalla Nóminas sobre la ventana activa de la aplicación (maqueta nominas/pantallas).</summary>
public sealed class DialogosNominas(Func<Window?> ventana) : IDialogosNominas
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(Activa(), titulo, mensaje, detalles, opciones);

    public string? ElegirFichero(string titulo)
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = titulo,
            Filter = "Excel de nóminas (*.xls;*.xlsx)|*.xls;*.xlsx|Todos los ficheros (*.*)|*.*",
            CheckFileExists = true,
        };
        return dialogo.ShowDialog(Activa()) == true ? dialogo.FileName : null;
    }

    public IReadOnlyList<AliasNuevo>? DecidirCabeceras(string nombreFichero, IReadOnlyList<CabeceraSinAlias> cabeceras, IReadOnlyList<CampoNomina> campos)
    {
        var dialogo = Nuevo("Columnas nuevas en el fichero", 640);
        var panel = new StackPanel();
        panel.Children.Add(Parrafo($"«{nombreFichero}» trae {Formatos.Plural(cabeceras.Count, "columna que no se reconoce", "columnas que no se reconocen")}. " +
            "Elige a qué campo de la nómina corresponde cada una, o ignórala. Se recuerda para esta sociedad y no se volverá a preguntar."));
        const string ignorar = "Ignorar esta columna";
        var opciones = new List<string> { ignorar };
        opciones.AddRange(campos.Select(c => c.Descripcion));
        var combos = new List<(CabeceraSinAlias Cabecera, ComboBox Combo)>();
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        foreach (var c in cabeceras)
        {
            var fila = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var texto = new StackPanel { Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center };
            texto.Children.Add(new TextBlock { Text = $"«{c.Cabecera}»", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            texto.Children.Add(new TextBlock { Text = $"Columna {c.Columna + 1}" + (c.Ejemplo is { Length: > 0 } e ? $" · p. ej. {e}" : ""), Style = Estilo(dialogo, "Texto.Apagado") });
            var combo = new ComboBox { ItemsSource = opciones, SelectedIndex = 0, Margin = new Thickness(0, 4, 0, 4), VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Campo de la nómina al que corresponde la columna; «Ignorar» la deja fuera de la importación" };
            Grid.SetRow(texto, fila);
            Grid.SetRow(combo, fila);
            Grid.SetColumn(combo, 1);
            g.Children.Add(texto);
            g.Children.Add(combo);
            combos.Add((c, combo));
        }
        panel.Children.Add(new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 420 });
        dialogo.Content = panel;
        if (!Mostrar(dialogo, "Continuar", "Guarda las columnas para la sociedad y sigue con la importación", "No importa nada")) return null;
        return combos.Select(x => new AliasNuevo(x.Cabecera.Cabecera, x.Combo.SelectedIndex <= 0 ? null : campos[x.Combo.SelectedIndex - 1].Nombre)).ToList();
    }

    public DecisionImportacion ConfirmarImportacion(string titulo, IReadOnlyList<(string Etiqueta, string Valor)> datos, IReadOnlyList<string> avisos, int sinResolver)
    {
        var dialogo = Nuevo(titulo, 620);
        var panel = new StackPanel();
        panel.Children.Add(Parrafo("Leído sin preguntas: la sociedad por el NIF, y el periodo y la paga por la cabecera del fichero."));
        var g = new RejillaDatos(dialogo, datos);
        panel.Children.Add(g.Panel);
        foreach (var a in avisos)
            panel.Children.Add(new Nota { Tipo = TipoNota.Aviso, Margin = new Thickness(0, 8, 0, 0), Content = new TextBlock { Text = a } });
        if (sinResolver > 0)
            panel.Children.Add(new Nota { Tipo = TipoNota.Info, Margin = new Thickness(0, 8, 0, 0),
                Content = new TextBlock { Text = "Puedes importar ahora y resolver después (clic derecho en la nómina › Resolver trabajador…); no se contabiliza hasta resolverlo." } });
        dialogo.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 };
        var resultado = DecisionImportacion.Cancelar;
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No importa nada" };
        var solo = new Button { Content = "Solo importar", Margin = new Thickness(8, 0, 0, 0), ToolTip = "Guarda las nóminas sin generar asientos; quedan pendientes en la pantalla" };
        var contabilizar = new Button
        {
            Content = sinResolver > 0 ? "Resolver y contabilizar…" : "Importar y contabilizar", Style = Estilo<Style>(dialogo, "Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = sinResolver > 0 ? "Importa, te pide cómo resolver cada trabajador sin reconocer y contabiliza todas las nóminas" : "Importa y genera los asientos con la plantilla «Nómina» en una sola operación",
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        solo.Click += (_, _) => { resultado = DecisionImportacion.SoloImportar; dialogo.DialogResult = true; };
        contabilizar.Click += (_, _) => { resultado = DecisionImportacion.ImportarYContabilizar; dialogo.DialogResult = true; };
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, solo, contabilizar } };
        dialogo.Loaded += (_, _) => contabilizar.Focus();
        return dialogo.ShowDialog() == true ? resultado : DecisionImportacion.Cancelar;
    }

    public EleccionTrabajador? ResolverTrabajador(string titulo, string explicacion, IReadOnlyList<CandidatoEmpleado> candidatos, string textoAlta)
    {
        var dialogo = Nuevo(titulo, 600);
        var panel = new StackPanel();
        panel.Children.Add(Parrafo(explicacion));
        var radios = new List<(RadioButton Radio, CandidatoEmpleado? Candidato)>();
        RadioButton Opcion(string nombre, string detalle, CandidatoEmpleado? c)
        {
            var contenido = new StackPanel();
            contenido.Children.Add(new TextBlock { Text = nombre, FontWeight = FontWeights.SemiBold });
            contenido.Children.Add(new TextBlock { Text = detalle, Style = Estilo(dialogo, "Texto.Apagado"), TextWrapping = TextWrapping.Wrap });
            var r = new RadioButton { Content = contenido, GroupName = "resolver", Margin = new Thickness(0, 6, 0, 6), VerticalContentAlignment = VerticalAlignment.Top };
            radios.Add((r, c));
            panel.Children.Add(r);
            return r;
        }
        foreach (var c in candidatos)
            Opcion(c.Nombre + (c.Nif is { } nif ? $" · NIF {nif}" : ""), c.Motivo + (c.IdEmpleado is null ? ". Se le activa el rol Empleado." : ". Ya es empleado; se le asigna el código."), c);
        var alta = Opcion("Alta como empleado nuevo", textoAlta, null);
        (radios.Count > 1 ? radios[0].Radio : alta).IsChecked = true;
        dialogo.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 520 };
        if (!Mostrar(dialogo, "Resolver", "Asigna esta nómina (y las del mismo código) al trabajador elegido", "Deja la nómina sin resolver")) return null;
        var elegido = radios.First(r => r.Radio.IsChecked == true);
        return new EleccionTrabajador(elegido.Candidato, elegido.Candidato is null);
    }

    public string? ConfirmarReemplazo(string titulo, string explicacion, IReadOnlyList<CambioReemplazo> cambios, string? sinCambios, string aviso)
    {
        var dialogo = Nuevo(titulo, 680);
        var panel = new StackPanel();
        panel.Children.Add(Parrafo(explicacion));
        var g = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var w in new[] { 1.6, 1.2, 1.0, 1.0 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w, GridUnitType.Star) });
        void Celda(int fila, int col, string texto, bool cabecera = false, Brush? color = null, bool derecha = false)
        {
            var t = new TextBlock { Text = texto, Margin = new Thickness(0, 2, 8, 2), TextAlignment = derecha ? TextAlignment.Right : TextAlignment.Left };
            if (cabecera) t.Style = Estilo(dialogo, "Texto.Seccion");
            if (color is not null) t.Foreground = color;
            Grid.SetRow(t, fila);
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Celda(0, 0, "Trabajador", true); Celda(0, 1, "Campo", true); Celda(0, 2, "Antes", true, derecha: true); Celda(0, 3, "Ahora", true, derecha: true);
        string? ultimo = null;
        foreach (var c in cambios)
        {
            var fila = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Celda(fila, 0, c.Trabajador == ultimo ? "" : c.Trabajador);
            Celda(fila, 1, c.Campo);
            Celda(fila, 2, Formatos.Importe(c.Antes), color: Estilo<Brush>(dialogo, "Pincel.Rojo"), derecha: true);
            Celda(fila, 3, Formatos.Importe(c.Ahora), color: Estilo<Brush>(dialogo, "Pincel.Verde"), derecha: true);
            ultimo = c.Trabajador;
        }
        panel.Children.Add(new ScrollViewer { Content = g, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 300 });
        if (cambios.Count == 0) panel.Children.Add(Parrafo("Ningún importe cambia por trabajador (cambia otra parte del fichero)."));
        if (sinCambios is not null) panel.Children.Add(new TextBlock { Text = sinCambios, Style = Estilo(dialogo, "Texto.Apagado"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        panel.Children.Add(new Nota { Tipo = TipoNota.Aviso, Margin = new Thickness(0, 0, 0, 10), Content = new TextBlock { Text = aviso } });
        var caja = CajaMotivo(dialogo, panel, "Motivo (obligatorio, queda en la auditoría de los asientos)");
        dialogo.Content = panel;
        return PedirConMotivo(dialogo, caja, "Reemplazar y contabilizar", "Deshace la versión anterior, importa la nueva y la contabiliza", Peligro: false);
    }

    public string? PedirMotivo(string titulo, string explicacion, string textoBoton)
    {
        var dialogo = Nuevo(titulo, 580);
        var panel = new StackPanel();
        panel.Children.Add(new Nota { Tipo = TipoNota.Info, Margin = new Thickness(0, 0, 0, 12), Content = new TextBlock { Text = explicacion } });
        var caja = CajaMotivo(dialogo, panel, "Motivo (obligatorio)");
        dialogo.Content = panel;
        return PedirConMotivo(dialogo, caja, textoBoton, titulo, Peligro: true);
    }

    // ---------- piezas comunes ----------

    private DialogoProConta Nuevo(string titulo, double ancho) => new() { Title = titulo, Owner = Activa(), Width = ancho };

    private static TextBlock Parrafo(string texto) => new() { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) };

    private static Style Estilo(FrameworkElement e, string clave) => Estilo<Style>(e, clave);

    private static T Estilo<T>(FrameworkElement e, string clave) => (T)e.FindResource(clave);

    private static TextBox CajaMotivo(DialogoProConta dialogo, Panel panel, string etiqueta)
    {
        panel.Children.Add(new TextBlock { Text = etiqueta, Style = Estilo(dialogo, "Texto.Seccion"), Margin = new Thickness(0, 0, 0, 4) });
        var caja = new TextBox { MaxLength = 500, Style = Estilo(dialogo, "Campo.Multilinea"), MinHeight = 56, AcceptsReturn = false, TextWrapping = TextWrapping.Wrap,
            ToolTip = "Queda anotado con tu nombre y la hora" };
        panel.Children.Add(caja);
        return caja;
    }

    private static string? PedirConMotivo(DialogoProConta dialogo, TextBox caja, string textoBoton, string ayuda, bool Peligro)
    {
        var error = new TextBlock { Text = "Escribe el motivo.", Foreground = Estilo<Brush>(dialogo, "Pincel.RojoError"), Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
        if (dialogo.Content is Panel p) p.Children.Add(error);
        var aceptar = new Button { Content = textoBoton, Style = Estilo(dialogo, Peligro ? "Boton.Peligro" : "Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, ToolTip = ayuda };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No cambia nada" };
        aceptar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(caja.Text)) { error.Visibility = Visibility.Visible; caja.Focus(); return; }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => caja.Focus();
        return dialogo.ShowDialog() == true ? caja.Text.Trim() : null;
    }

    private static bool Mostrar(DialogoProConta dialogo, string textoBoton, string ayuda, string ayudaCancelar)
    {
        var aceptar = new Button { Content = textoBoton, Style = Estilo(dialogo, "Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, ToolTip = ayuda };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = ayudaCancelar };
        aceptar.Click += (_, _) => dialogo.DialogResult = true;
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dialogo.Loaded += (_, _) => aceptar.Focus();
        return dialogo.ShowDialog() == true;
    }

    /// <summary>Datos del fichero en dos columnas de etiqueta y valor.</summary>
    private sealed class RejillaDatos
    {
        public Grid Panel { get; } = new() { Margin = new Thickness(0, 0, 0, 4) };

        public RejillaDatos(FrameworkElement e, IReadOnlyList<(string Etiqueta, string Valor)> datos)
        {
            Panel.ColumnDefinitions.Add(new ColumnDefinition());
            Panel.ColumnDefinitions.Add(new ColumnDefinition());
            for (var i = 0; i < datos.Count; i++)
            {
                if (i % 2 == 0) Panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var celda = new StackPanel { Margin = new Thickness(0, 0, 12, 10) };
                celda.Children.Add(new TextBlock { Text = datos[i].Etiqueta, Style = Estilo(e, "Texto.Apagado") });
                celda.Children.Add(new TextBlock { Text = datos[i].Valor, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                Grid.SetRow(celda, i / 2);
                Grid.SetColumn(celda, i % 2);
                Panel.Children.Add(celda);
            }
        }
    }

    private Window? Activa() => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? ventana();
}
