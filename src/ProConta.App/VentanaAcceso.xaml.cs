using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Acceso;

namespace ProConta.App;

/// <summary>
/// Pantalla de acceso (seguridad/acceso, maqueta «Dividida»): operador y contraseña primero; las
/// sociedades autorizadas solo se consultan después de autenticar. Sin marco, se mueve arrastrando
/// las zonas libres, Intro avanza y Esc sale.
/// </summary>
public partial class VentanaAcceso : Window
{
    private sealed record Opcion<T>(T Valor, string Texto)
    {
        // El cuadro cerrado muestra ToString(): el texto, no el registro.
        public override string ToString() => Texto;
    }

    private readonly ServicioAcceso _acceso;
    private SesionAcceso? _sesion;
    private bool _ocupado;

    public VentanaAcceso(ServicioAcceso acceso, IServicioSociedades _)
    {
        InitializeComponent();
        _acceso = acceso;
        TextoVersion.Text = App.Version;
        Loaded += (_, _) => CampoOperador.Focus();
    }

    /// <summary>Contexto validado al completar el acceso; null si se sale.</summary>
    public ContextoAcceso? Resultado { get; private set; }

    private async void Principal_Click(object sender, RoutedEventArgs e)
    {
        if (_ocupado) return;
        if (_sesion is null) await AutenticarAsync();
        else await EntrarAsync();
    }

    private async Task AutenticarAsync()
    {
        await Ocupado(async () =>
        {
            var r = await _acceso.AutenticarAsync(CampoOperador.Text, CampoContrasena.Password);
            CampoContrasena.Clear();
            if (!r.EsCorrecto) { Mostrar(r.Error!); CampoContrasena.Focus(); return; }
            _sesion = r.Valor;
            ResumenOperador.Text = "Operador: " + _sesion.NombreOperador;
            CampoOperador.IsEnabled = CampoContrasena.IsEnabled = false;
            BotonCambiarOperador.Visibility = Visibility.Visible;

            var opciones = await _acceso.ListarSociedadesAsync(_sesion);
            if (!opciones.EsCorrecto) { Mostrar(opciones.Error!); return; }
            if (opciones.Valor.Sociedades.Count == 0)
            {
                Mostrar(new Error("Acceso.SinSociedades", "No tienes acceso a ninguna sociedad.", null,
                    "Tu operador no está en ningún grupo con acceso a sociedades.", "Pide a un administrador que te dé acceso."));
                return;
            }
            ComboSociedad.ItemsSource = opciones.Valor.Sociedades.Select(s => new Opcion<Sociedad>(s, $"{s.RazonSocial}  ({s.IdSociedad})")).ToList();
            ComboSociedad.IsEnabled = true;
            BotonPrincipal.Content = "Entrar";
            BotonPrincipal.ToolTip = "Entra en la sociedad y el ejercicio elegidos (Intro)";
            Indicacion.Text = "Elige la sociedad y el ejercicio en los que vas a trabajar.";
            ComboSociedad.SelectedItem = ((IEnumerable<Opcion<Sociedad>>)ComboSociedad.ItemsSource)
                .FirstOrDefault(o => o.Valor.IdSociedad == opciones.Valor.IdPropuesta);
            ComboSociedad.Focus();
        });
    }

    private async void Sociedad_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ComboEjercicio.ItemsSource = null;
        ComboEjercicio.IsEnabled = false;
        ResumenEjercicio.Text = "Sin elegir";
        if (_sesion is null || ComboSociedad.SelectedItem is not Opcion<Sociedad> sociedad) { ResumenSociedad.Text = "Sin elegir"; return; }
        ResumenSociedad.Text = sociedad.Valor.RazonSocial;
        await Ocupado(async () =>
        {
            var r = await _acceso.ListarEjerciciosAsync(_sesion, sociedad.Valor.IdSociedad);
            if (!r.EsCorrecto) { Mostrar(r.Error!); return; }
            if (r.Valor.Ejercicios.Count == 0)
            {
                Mostrar(new Error("Acceso.SinEjercicios", "Esta sociedad no tiene ejercicios.", sociedad.Valor.RazonSocial,
                    "No hay ningún ejercicio dado de alta para ella.", "Elige otra sociedad o pide que abran su ejercicio."));
                return;
            }
            var opciones = r.Valor.Ejercicios.Select(x => new Opcion<SociedadEjercicio>(x, x.Cerrado ? $"{x.Ejercicio} · cerrado" : x.Ejercicio.ToString())).ToList();
            ComboEjercicio.ItemsSource = opciones;
            ComboEjercicio.IsEnabled = true;
            ComboEjercicio.SelectedItem = opciones.FirstOrDefault(o => o.Valor.IdSociedadEjercicio == r.Valor.IdPropuesto);
            OcultarMensaje();
        });
    }

    private void Ejercicio_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ResumenEjercicio.Text = ComboEjercicio.SelectedItem is Opcion<SociedadEjercicio> o
            ? $"{o.Valor.Ejercicio} ({(o.Valor.Cerrado ? "cerrado: solo consulta" : "abierto")})"
            : "Sin elegir";

    private async Task EntrarAsync()
    {
        if (ComboSociedad.SelectedItem is not Opcion<Sociedad> sociedad || ComboEjercicio.SelectedItem is not Opcion<SociedadEjercicio> ejercicio)
        {
            Mostrar(new Error("Acceso.SinContexto", "Elige la sociedad y el ejercicio.", null, "Faltan por elegir.", "Elígelos en las listas de la derecha."));
            return;
        }
        await Ocupado(async () =>
        {
            var r = await _acceso.CompletarAsync(_sesion!, sociedad.Valor.IdSociedad, ejercicio.Valor.IdSociedadEjercicio);
            if (!r.EsCorrecto) { Mostrar(r.Error!); return; }
            Resultado = r.Valor;
            DialogResult = true;
        });
    }

    private async void CambiarOperador_Click(object sender, RoutedEventArgs e)
    {
        if (_sesion is not null) await _acceso.CerrarSesionAsync(_sesion);
        _sesion = null;
        ComboSociedad.ItemsSource = null;
        ComboSociedad.IsEnabled = false;
        CampoOperador.IsEnabled = CampoContrasena.IsEnabled = true;
        BotonCambiarOperador.Visibility = Visibility.Collapsed;
        BotonPrincipal.Content = "Continuar";
        BotonPrincipal.ToolTip = "Comprueba el operador y la contraseña (Intro)";
        Indicacion.Text = "Identifícate con tu operador y tu contraseña.";
        ResumenOperador.Text = "Operador: sin identificar";
        OcultarMensaje();
        CampoOperador.SelectAll();
        CampoOperador.Focus();
    }

    private void Salir_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Ventana_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !(ComboSociedad.IsDropDownOpen || ComboEjercicio.IsDropDownOpen))
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !(ComboSociedad.IsDropDownOpen || ComboEjercicio.IsDropDownOpen))
        {
            if (_sesion is null && Keyboard.FocusedElement == CampoOperador && CampoContrasena.IsEnabled)
                CampoContrasena.Focus();
            else
                Principal_Click(sender, e);
            e.Handled = true;
        }
    }

    /// <summary>La ventana no tiene marco: se arrastra desde cualquier zona que no sea un control.</summary>
    private void Ventana_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not (TextBox or PasswordBox or ComboBox or Button) && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    private async Task Ocupado(Func<Task> trabajo)
    {
        _ocupado = true;
        Cursor = Cursors.Wait;
        try
        {
            OcultarMensaje();
            await trabajo();
        }
        catch (ExcepcionDatos ex)
        {
            Mostrar(ex.Error);
        }
        finally
        {
            _ocupado = false;
            Cursor = null;
        }
    }

    private void Mostrar(Error error)
    {
        TextoMensaje.Text = App.Explicar(error);
        CajaMensaje.Visibility = Visibility.Visible;
    }

    private void OcultarMensaje() => CajaMensaje.Visibility = Visibility.Collapsed;
}
