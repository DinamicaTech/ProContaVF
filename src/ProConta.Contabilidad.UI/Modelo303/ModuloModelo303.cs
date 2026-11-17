using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.Contabilidad.Presentacion;
using ProConta.Nucleo.Identificacion;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;

namespace ProConta.Contabilidad.UI.Modelo303;

/// <summary>Vista de la pantalla Modelo 303; toda la lógica está en <see cref="Modelo303VM"/>.</summary>
public partial class PantallaModelo303 : UserControl
{
    public PantallaModelo303() => InitializeComponent();
}

/// <summary>
/// Diálogos de la pantalla con <see cref="DialogoProConta"/>: la conformidad antes de presentar (contabilidad/modelos-tributarios/presentacion:
/// PDF validado, NIF del declarante leído del fichero, importe, forma de pago y cuenta, presentador y entorno, y la casilla «Conforme»),
/// el NRC del pago y los mensajes.
/// </summary>
public sealed class DialogosModelo303(Func<Window?> propietario) : IDialogosModelo303
{
    public bool Conformidad(BorradorAeat b, Action verPdf)
    {
        var d = b.Declaracion;
        var pruebas = b.Entorno == EntornoAeat.Pruebas;
        var dlg = new DialogoProConta
        {
            Title = $"Presentar {d.Titulo}", Owner = propietario(), Width = 760, Height = 640, MinWidth = 560, MinHeight = 420,
            SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        var apagado = (Brush)dlg.FindResource("Pincel.Apagado");
        var cuerpo = new StackPanel();
        cuerpo.Children.Add(new Nota
        {
            Tipo = pruebas ? TipoNota.Aviso : TipoNota.Info, Margin = new Thickness(0, 0, 0, 10),
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold,
                Text = pruebas
                    ? "Entorno de PRUEBAS de la AEAT: la presentación no tiene efectos (esta BD es la copia de pruebas)."
                    : "Presentación REAL en la AEAT: una declaración grabada no se puede anular, solo rectificar.",
            },
        });
        var nifCorrecto = string.Equals(b.NifLeido, d.NifSociedad, StringComparison.OrdinalIgnoreCase);
        var filas = new List<(string, string, bool)>
        {
            ("Modelo y periodo", d.Titulo, false),
            ("Declarante (NIF leído del fichero)", $"{b.NifLeido ?? "ninguno"} · {d.RazonSocial}" + (nifCorrecto ? "" : $"  ⚠ no es el de la sociedad ({d.NifSociedad})"), !nifCorrecto),
        };
        foreach (var (etiqueta, valor) in d.Resumen) filas.Add((etiqueta, valor, false));
        filas.Add(("Forma de pago", d.TextoFormaPago, false));
        if (d.FormaPago is 'U' or 'I') filas.Add(("Importe a ingresar", Formatos.Importe(d.ImporteAIngresar), false));
        if (d.FormaPago == 'U') filas.Add(("Cuenta de domiciliación", Ibanes.Agrupar(d.Iban) ?? "sin cuenta", d.Iban is null));
        if (d.FormaPago == 'I') filas.Add(("NRC del pago", d.Nrc ?? "sin NRC", d.Nrc is null));
        if (d.ComplementariaDe is { } ant) filas.Add(("Rectifica la declaración", $"justificante {ant}", false));
        filas.Add(("Presentador (titular del certificado)", b.Presentador?.Descripcion ?? "sin certificado", b.Presentador is null));
        var tabla = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        tabla.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var i = 0; i < filas.Count; i++)
        {
            tabla.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var (etiqueta, valor, mal) = filas[i];
            var e = new TextBlock { Text = etiqueta, Foreground = apagado, Margin = new Thickness(0, 2, 8, 2) };
            var v = new TextBox
            {
                Text = valor, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2),
                FontWeight = FontWeights.SemiBold, ToolTip = "Se puede seleccionar y copiar",
            };
            if (mal) v.Foreground = (Brush)dlg.FindResource("Pincel.RojoError");
            Grid.SetRow(e, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1);
            tabla.Children.Add(e); tabla.Children.Add(v);
        }
        cuerpo.Children.Add(tabla);
        void Lista(IReadOnlyList<string> textos, TipoNota tipo)
        {
            if (textos.Count == 0) return;
            var t = new TextBox
            {
                Text = string.Join(Environment.NewLine, textos), IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap,
                ToolTip = "Se puede seleccionar y copiar",
            };
            cuerpo.Children.Add(new Nota { Tipo = tipo, Margin = new Thickness(0, 0, 0, 10), Content = t });
        }
        Lista(b.Bloqueos, TipoNota.Error);
        Lista(b.Avisos, TipoNota.Aviso);

        var ver = new Button
        {
            Content = "Ver el PDF", Style = (Style)dlg.FindResource("Boton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10),
            IsEnabled = b.Validacion.Pdf is not null, ToolTip = "Abre el PDF oficial que ha devuelto la AEAT al validar el fichero: es lo que se va a presentar",
        };
        cuerpo.Children.Add(ver);
        var conforme = new CheckBox
        {
            Content = "Conforme: he revisado el PDF y los datos, y quiero presentar esta declaración", IsEnabled = b.SePuedePresentar, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "La AEAT entiende que el propio envío acepta los datos de la declaración",
        };
        cuerpo.Children.Add(conforme);
        var vistoPdf = false;
        ver.Click += (_, _) => { verPdf(); vistoPdf = true; };
        dlg.Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = cuerpo };

        var presentar = new Button
        {
            Content = pruebas ? "Presentar en pruebas" : "Presentar", Style = (Style)dlg.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsEnabled = false,
            ToolTip = "Envía la declaración a la AEAT con el certificado de la sociedad. Se activa al marcar «Conforme»",
        };
        conforme.Checked += (_, _) => presentar.IsEnabled = b.SePuedePresentar;
        conforme.Unchecked += (_, _) => presentar.IsEnabled = false;
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin presentar nada" };
        var resultado = false;
        presentar.Click += (_, _) =>
        {
            if (!vistoPdf && ServicioMensajes.Preguntar(dlg, "No has abierto el PDF", "¿Presentar sin haber visto el PDF de la declaración?", null,
                    new OpcionMensaje("Ver el PDF", "Abre el PDF antes de presentar", Principal: true), new OpcionMensaje("Presentar igualmente", "Presenta sin verlo")) == 0)
            {
                verPdf();
                vistoPdf = true;
                return;
            }
            resultado = true;
            dlg.DialogResult = true;
        };
        cancelar.Click += (_, _) => dlg.DialogResult = false;
        dlg.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, presentar } };
        dlg.ShowDialog();
        return resultado;
    }

    public string? PedirNrc(string titulo, string explicacion)
    {
        var dlg = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 620, SizeToContent = SizeToContent.Height };
        var cuerpo = new StackPanel();
        cuerpo.Children.Add(new TextBox { Text = explicacion, IsReadOnly = true, BorderThickness = new Thickness(0), Background = Brushes.Transparent, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
        var campo = new TextBox { MaxLength = 22, Width = 260, HorizontalAlignment = HorizontalAlignment.Left, FontFamily = new FontFamily("Consolas"), ToolTip = "NRC del justificante del pago: 22 letras o cifras" };
        cuerpo.Children.Add(campo);
        var error = new TextBlock { Foreground = (Brush)dlg.FindResource("Pincel.RojoError"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
        cuerpo.Children.Add(error);
        dlg.Content = cuerpo;
        string? nrc = null;
        var aceptar = new Button { Content = "Continuar", Style = (Style)dlg.FindResource("Boton.Primario"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true,
            ToolTip = "Sigue con la validación y la conformidad con este NRC" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "No presenta nada" };
        aceptar.Click += (_, _) =>
        {
            if (DeclaracionAeat.ErrorNrc(campo.Text) is { } e) { error.Text = e; return; }
            nrc = campo.Text.Trim().ToUpperInvariant();
            dlg.DialogResult = true;
        };
        cancelar.Click += (_, _) => dlg.DialogResult = false;
        dlg.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        dlg.Loaded += (_, _) => campo.Focus();
        return dlg.ShowDialog() == true ? nrc : null;
    }

    public void Informar(string titulo, string mensaje, IReadOnlyList<string>? detalles = null) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, new OpcionMensaje("Aceptar", "Cierra el mensaje", Principal: true));

    public void Error(string titulo, string mensaje) => new Mayor.DialogosMayor(propietario).Error(titulo, mensaje);
}

/// <summary>Modelo 303 dentro del contenedor (contabilidad/modelos-tributarios/modelo-303): una sola pestaña por sesión.</summary>
public sealed class ModuloModelo303(ContextoModelo303 contexto, IAnfitrionPestanas anfitrion, IDialogosModelo303 dialogos)
{
    private Modelo303VM? _vm;

    public void Abrir(int? trimestre = null)
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync(trimestre);
            return;
        }
        _vm = new Modelo303VM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync(trimestre);
    }
}
