using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Acceso;

namespace ProConta.App;

/// <summary>
/// Pantalla de acceso (seguridad/acceso y seguridad/pantallas, maqueta «Dividida»). Operador y contraseña
/// se comprueban al salir de la contraseña (Tab), con Intro o con «Entrar»; un operador sin contraseña se
/// comprueba al elegirlo de la lista. Solo entonces aparece «Dónde quieres trabajar», con la última sociedad
/// y el último ejercicio propuestos y el foco en «Entrar». Sin marco, se mueve arrastrando; Esc sale.
/// </summary>
public partial class VentanaAcceso : Window
{
    private sealed record Opcion<T>(T Valor, string Texto)
    {
        // El cuadro cerrado muestra ToString(): el texto, no el registro.
        public override string ToString() => Texto;
    }

    private readonly ServicioAcceso _acceso;
    private readonly bool _conLista;
    private SesionAcceso? _sesion;
    private bool _ocupado;
    private bool _cargando;

    /// <param name="operadores">Operadores activos si el parámetro muestra la lista; null para escribir el nombre.</param>
    /// <param name="ultimoOperador">Último operador que entró en este puesto, para proponerlo.</param>
    public VentanaAcceso(ServicioAcceso acceso, IReadOnlyList<string>? operadores, string? ultimoOperador)
    {
        InitializeComponent();
        _acceso = acceso;
        _conLista = operadores is not null;
        TextoVersion.Text = App.Version;
        if (_conLista)
        {
            CampoOperador.Visibility = Visibility.Collapsed;
            ComboOperador.Visibility = Visibility.Visible;
            ComboOperador.ItemsSource = operadores;
        }
        else
        {
            CampoOperador.Text = ultimoOperador ?? "";
        }
        Loaded += (_, _) =>
        {
            if (_conLista && ultimoOperador is not null && operadores!.Contains(ultimoOperador, StringComparer.OrdinalIgnoreCase))
            {
                // Proponer el último operador: si no tiene contraseña, entra solo (Operador_SelectionChanged).
                CampoContrasena.Focus();
                ComboOperador.SelectedItem = operadores!.First(o => string.Equals(o, ultimoOperador, StringComparison.OrdinalIgnoreCase));
            }
            else if (_conLista) ComboOperador.Focus();
            else if (CampoOperador.Text.Length > 0) CampoContrasena.Focus();
            else CampoOperador.Focus();
        };
    }

    /// <summary>Contexto validado al completar el acceso; null si se sale.</summary>
    public ContextoAcceso? Resultado { get; private set; }

    private string NombreOperador => _conLista ? ComboOperador.SelectedItem as string ?? "" : CampoOperador.Text;

    private async void Principal_Click(object sender, RoutedEventArgs e)
    {
        if (_ocupado) return;
        if (_sesion is null) await AutenticarAsync(avisarSiVacio: true);
        else await EntrarAsync();
    }

    /// <summary>Al salir de la contraseña con Tab se comprueba, salvo que se vaya a «Salir» o a «Entrar» (que ya lo hace).</summary>
    private async void Contrasena_PreviewLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_sesion is not null || _ocupado || e.NewFocus == BotonSalir || e.NewFocus == BotonPrincipal || !IsActive) return;
        await AutenticarAsync(avisarSiVacio: false);
    }

    /// <summary>Un operador sin contraseña entra al elegirlo; con contraseña, el foco pasa a la contraseña.</summary>
    private async void Operador_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_sesion is not null || _ocupado || ComboOperador.SelectedItem is not string nombre) return;
        await Ocupado(async () =>
        {
            var sesion = await _acceso.AutenticarSinContrasenaAsync(nombre);
            if (sesion is null) { CampoContrasena.Focus(); return; }
            await IdentificadoAsync(sesion);
        });
    }

    private async Task AutenticarAsync(bool avisarSiVacio)
    {
        if (string.IsNullOrWhiteSpace(NombreOperador))
        {
            if (avisarSiVacio)
                Mostrar(new Error("Acceso.SinOperador", "Indica tu operador.", null, "El operador está vacío.",
                    _conLista ? "Elígelo en la lista." : "Escribe tu nombre de operador."));
            return;
        }
        await Ocupado(async () =>
        {
            var r = await _acceso.AutenticarAsync(NombreOperador, CampoContrasena.Password);
            CampoContrasena.Clear();
            if (!r.EsCorrecto)
            {
                Mostrar(r.Error!);
                CampoContrasena.Focus();
                return;
            }
            await IdentificadoAsync(r.Valor);
        });
    }

    /// <summary>Operador correcto: aparece «Dónde quieres trabajar» con lo último elegido y el foco en «Entrar».</summary>
    private async Task IdentificadoAsync(SesionAcceso sesion)
    {
        _sesion = sesion;
        ResumenOperador.Text = "Operador: " + sesion.NombreOperador;
        CampoOperador.IsEnabled = ComboOperador.IsEnabled = CampoContrasena.IsEnabled = false;
        BotonCambiarOperador.Visibility = Visibility.Visible;

        var opciones = await _acceso.ListarSociedadesAsync(sesion);
        if (!opciones.EsCorrecto) { Mostrar(opciones.Error!); return; }
        if (opciones.Valor.Sociedades.Count == 0)
        {
            Mostrar(new Error("Acceso.SinSociedades", "No tienes acceso a ninguna sociedad.", null,
                "Tu operador no está en ningún grupo con acceso a sociedades.", "Pide a un administrador que te dé acceso."));
            return;
        }
        Indicacion.Text = "Elige la sociedad y el ejercicio en los que vas a trabajar.";
        BloqueDonde.Visibility = Visibility.Visible;
        var lista = opciones.Valor.Sociedades.Select(s => new Opcion<Sociedad>(s, $"{s.RazonSocial}  ({s.IdSociedad})")).ToList();
        ComboSociedad.ItemsSource = lista;
        _cargando = true;
        ComboSociedad.SelectedItem = lista.FirstOrDefault(o => o.Valor.IdSociedad == opciones.Valor.IdPropuesta);
        _cargando = false;
        await CargarEjerciciosAsync();
        if (ComboSociedad.SelectedItem is not null && ComboEjercicio.SelectedItem is not null) BotonPrincipal.Focus();
        else ComboSociedad.Focus();
    }

    private async void Sociedad_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_cargando) return;
        await Ocupado(CargarEjerciciosAsync);
    }

    private async Task CargarEjerciciosAsync()
    {
        ComboEjercicio.ItemsSource = null;
        ResumenEjercicio.Text = "Sin elegir";
        if (_sesion is null || ComboSociedad.SelectedItem is not Opcion<Sociedad> sociedad) { ResumenSociedad.Text = "Sin elegir"; return; }
        ResumenSociedad.Text = sociedad.Valor.RazonSocial;
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
        ComboEjercicio.SelectedItem = opciones.FirstOrDefault(o => o.Valor.IdSociedadEjercicio == r.Valor.IdPropuesto);
    }

    private void Ejercicio_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ResumenEjercicio.Text = ComboEjercicio.SelectedItem is Opcion<SociedadEjercicio> o
            ? $"{o.Valor.Ejercicio} ({(o.Valor.Cerrado ? "cerrado: solo consulta" : "abierto")})"
            : "Sin elegir";

    private async Task EntrarAsync()
    {
        if (ComboSociedad.SelectedItem is not Opcion<Sociedad> sociedad || ComboEjercicio.SelectedItem is not Opcion<SociedadEjercicio> ejercicio)
        {
            Mostrar(new Error("Acceso.SinContexto", "Elige la sociedad y el ejercicio.", null, "Faltan por elegir.", "Elígelos en las listas."));
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
        BloqueDonde.Visibility = Visibility.Collapsed;
        ComboSociedad.ItemsSource = null;
        ComboEjercicio.ItemsSource = null;
        ResumenSociedad.Text = ResumenEjercicio.Text = "Sin elegir";
        CampoOperador.IsEnabled = ComboOperador.IsEnabled = CampoContrasena.IsEnabled = true;
        BotonCambiarOperador.Visibility = Visibility.Collapsed;
        Indicacion.Text = "Identifícate con tu operador y tu contraseña.";
        ResumenOperador.Text = "Operador: sin identificar";
        OcultarMensaje();
        if (_conLista) { ComboOperador.SelectedItem = null; ComboOperador.Focus(); }
        else { CampoOperador.SelectAll(); CampoOperador.Focus(); }
    }

    private void Salir_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Ventana_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var desplegado = ComboSociedad.IsDropDownOpen || ComboEjercicio.IsDropDownOpen || ComboOperador.IsDropDownOpen;
        if (e.Key == Key.Escape && !desplegado)
        {
            DialogResult = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !desplegado)
        {
            if (_sesion is null && (Keyboard.FocusedElement == CampoOperador || Keyboard.FocusedElement == ComboOperador))
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
