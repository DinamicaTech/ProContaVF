using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Plataforma.Configuracion;
using ProConta.Seguridad.Acceso;
using ProConta.UI.Base.Comandos;

namespace ProConta.App;

/// <summary>Opción de la navegación lateral.</summary>
public sealed record OpcionNavegacion(string Titulo, Geometry? Icono, bool Disponible, bool Activa, string Ayuda);

/// <summary>
/// Ventana principal (plataforma/contenedor): cabecera con sociedad, ejercicio y operador, navegación
/// lateral con separador, área de trabajo con pestañas y barra de estado con la ubicación de datos,
/// la versión del esquema y la versión del programa.
/// </summary>
public partial class VentanaPrincipal : Window
{
    private double _anchoNavegacion = 228;

    public VentanaPrincipal(ContextoAcceso contexto, UbicacionDatos ubicacion, int versionEsquema)
    {
        AlternarNavegacionCommand = new ComandoDelegado(_ => AlternarNavegacion());
        InitializeComponent();

        var sesion = contexto.Sesion;
        var ejercicio = contexto.Contexto.Ejercicio;
        Title = $"ProContaVF · {contexto.RazonSocial} · {ejercicio}";
        TextoSociedad.Text = contexto.RazonSocial;
        TextoEjercicio.Text = $"Ejercicio {ejercicio}";
        TextoOperador.Text = sesion.NombreOperador;
        TextoIniciales.Text = Iniciales(sesion.NombreOperador);
        TextoSaludo.Text = $"Hola, {sesion.NombreOperador.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}";
        TextoContexto.Text = $"Trabajas en {contexto.RazonSocial} · Ejercicio {ejercicio} ({(contexto.EjercicioCerrado ? "cerrado" : "abierto")}).";
        AvisoCerrado.Visibility = contexto.EjercicioCerrado ? Visibility.Visible : Visibility.Collapsed;
        TextoDatos.Text = $"Datos: {System.IO.Path.GetFileName(ubicacion.RutaBaseDatos)} · conectado";
        TextoDatos.ToolTip = ubicacion.RutaBaseDatos;
        TextoEsquema.Text = $"Esquema versión {versionEsquema}";
        TextoVersion.Text = App.Version;

        const string proxima = "Aún no está disponible en esta versión: llegará con las pantallas de su área.";
        Geometry? Icono(string clave) => TryFindResource("Icono." + clave) as Geometry;
        Navegacion.ItemsSource = new[]
        {
            new OpcionNavegacion("Inicio", Icono("layout"), true, true, "Página de inicio"),
            new OpcionNavegacion("Datos maestros", Icono("user"), false, false, proxima),
            new OpcionNavegacion("Facturación", Icono("doc"), false, false, proxima),
            new OpcionNavegacion("Compras", Icono("scan"), false, false, proxima),
            new OpcionNavegacion("Contabilidad", Icono("book"), false, false, proxima),
            new OpcionNavegacion("Bancos", Icono("bank"), false, false, proxima),
            new OpcionNavegacion("Nóminas", Icono("coin"), false, false, proxima),
            new OpcionNavegacion("Informes", Icono("xls"), false, false, proxima),
            new OpcionNavegacion("Configuración", Icono("gear"), false, false, proxima),
        };
    }

    public ICommand AlternarNavegacionCommand { get; }

    private void Separador_MouseDoubleClick(object sender, MouseButtonEventArgs e) => AlternarNavegacion();

    /// <summary>Oculta o muestra el menú; oculto, el separador sigue visible para recuperarlo.</summary>
    private void AlternarNavegacion()
    {
        if (ColumnaNavegacion.ActualWidth > 0)
        {
            _anchoNavegacion = ColumnaNavegacion.ActualWidth;
            ColumnaNavegacion.Width = new GridLength(0);
        }
        else
        {
            ColumnaNavegacion.Width = new GridLength(_anchoNavegacion < 150 ? 228 : _anchoNavegacion);
        }
    }

    private static string Iniciales(string nombre) =>
        string.Concat(nombre.Split([' ', '.', '_'], StringSplitOptions.RemoveEmptyEntries).Take(2).Select(p => char.ToUpperInvariant(p[0])));
}
