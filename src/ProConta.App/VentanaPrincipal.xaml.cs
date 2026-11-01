using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Organizacion.Geografia;
using ProConta.Plataforma.Configuracion;
using ProConta.Seguridad.Acceso;
using ProConta.Terceros;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;
using ProConta.Terceros.UI.Vistas;
using ProConta.Terceros.UI.VistaModelos;
using ProConta.UI.Base.Comandos;

namespace ProConta.App;

/// <summary>Opción de la navegación lateral (un área o, sangrada, una de sus pantallas).</summary>
public sealed record OpcionNavegacion(string Titulo, Geometry? Icono, bool Disponible, bool Activa, string Ayuda, ICommand? Comando = null, bool EsHija = false)
{
    public Thickness Sangria => EsHija ? new Thickness(14, 0, 0, 0) : new Thickness(0);
    public FontWeight Peso => EsHija ? FontWeights.Normal : FontWeights.SemiBold;
}

/// <summary>
/// Ventana principal (plataforma/contenedor): cabecera con sociedad, ejercicio y operador, navegación lateral con
/// separador, área de trabajo con pestañas (cambios pendientes, guardar con Ctrl+G o Ctrl+S, aviso al cerrar o salir) y
/// barra de estado con la ubicación de datos, la versión del esquema, las pantallas abiertas y los avisos.
/// </summary>
public partial class VentanaPrincipal : Window
{
    private double _anchoNavegacion = 228;
    private readonly AreaTrabajo _area;
    private readonly ModuloTerceros _terceros;
    private readonly DispatcherTimer _borrarAviso;
    private bool _saliendo;

    public VentanaPrincipal(ContextoAcceso contexto, UbicacionDatos ubicacion, int versionEsquema, IBaseDatos bd)
    {
        AlternarNavegacionCommand = new ComandoDelegado(_ => AlternarNavegacion());
        GuardarActivaCommand = new ComandoDelegado(() => { if (_area?.Activa is { TieneCambios: true } p) _ = p.GuardarAsync(); });
        CerrarActivaCommand = new ComandoDelegado(() => { if (_area?.Lista.FirstOrDefault(p => p.Activa) is { Cerrable: true } p) p.CerrarCommand.Execute(null); });
        InitializeComponent();

        var sesion = contexto.Sesion;
        var ejercicio = contexto.Contexto.Ejercicio;
        Title = $"ProContaVF · {contexto.RazonSocial} · {ejercicio}";
        TextoSociedad.Text = contexto.RazonSocial;
        TextoEjercicio.Text = $"Ejercicio {ejercicio}";
        TextoOperador.Text = sesion.NombreOperador;
        TextoIniciales.Text = Iniciales(sesion.NombreOperador);
        TextoDatos.Text = $"Datos: {System.IO.Path.GetFileName(ubicacion.RutaBaseDatos)} · conectado";
        TextoDatos.ToolTip = ubicacion.RutaBaseDatos;
        TextoEsquema.Text = $"Esquema versión {versionEsquema}";
        TextoVersion.Text = App.Version;

        _borrarAviso = new DispatcherTimer(TimeSpan.FromSeconds(12), DispatcherPriority.Background, (_, _) => { TextoAviso.Text = ""; _borrarAviso!.Stop(); }, Dispatcher);
        _borrarAviso.Stop();
        _area = new AreaTrabajo(AreaVistas, Avisar);
        _area.PropertyChanged += (_, _) => RefrescarEstado();
        TiraPestanas.ItemsSource = _area.Lista;
        _area.AbrirFija(new PestanaInicio(new VistaInicio(contexto)));

        var cuentas = new CatalogoCuentasAccess(bd);
        var servicios = new ServiciosTerceros(
            new RepositorioTercerosAccess(bd), new BuscadorTerceros(bd), new CatalogoPerfilesAccess(bd), new ServicioRoles(bd, cuentas), cuentas,
            () => new FuenteGeografiaAccess(bd).CargarAsync(), contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador));
        _terceros = new ModuloTerceros(servicios, _area, new DialogosTerceros(servicios));

        const string proxima = "Aún no está disponible en esta versión: llegará con las pantallas de su área.";
        Geometry? Icono(string clave) => TryFindResource("Icono." + clave) as Geometry;
        var irAInicio = new ComandoDelegado(() => _area.Activar(_area.Lista[0].Pantalla));
        var abrirTerceros = new ComandoDelegado(_terceros.AbrirBuscador);
        Navegacion.ItemsSource = new[]
        {
            new OpcionNavegacion("Inicio", Icono("layout"), true, false, "Página de inicio", irAInicio),
            new OpcionNavegacion("Datos maestros", Icono("user"), true, false, "Terceros: clientes, proveedores, contactos y demás personas y organizaciones", abrirTerceros),
            new OpcionNavegacion("Terceros", null, true, false, "Buscador y fichas de terceros (clientes, proveedores, contactos…)", abrirTerceros, EsHija: true),
            new OpcionNavegacion("Facturación", Icono("doc"), false, false, proxima),
            new OpcionNavegacion("Compras", Icono("scan"), false, false, proxima),
            new OpcionNavegacion("Contabilidad", Icono("book"), false, false, proxima),
            new OpcionNavegacion("Bancos", Icono("bank"), false, false, proxima),
            new OpcionNavegacion("Nóminas", Icono("coin"), false, false, proxima),
            new OpcionNavegacion("Informes", Icono("xls"), false, false, proxima),
            new OpcionNavegacion("Configuración", Icono("gear"), false, false, proxima),
        };
        RefrescarEstado();
    }

    public ICommand AlternarNavegacionCommand { get; }
    public ICommand GuardarActivaCommand { get; }
    public ICommand CerrarActivaCommand { get; }

    /// <summary>Abre la pestaña de Terceros (lo usan la navegación y el modo de capturas).</summary>
    internal ModuloTerceros Terceros => _terceros;

    private void RefrescarEstado()
    {
        TextoPantallas.Text = _area.Resumen;
        PuntoCambios.Visibility = _area.HayCambios ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Aviso breve en la barra de estado (no bloquea); se borra solo al cabo de unos segundos.</summary>
    private void Avisar(string texto)
    {
        TextoAviso.Text = texto;
        TextoAviso.ToolTip = texto;
        _borrarAviso.Stop();
        _borrarAviso.Start();
    }

    private void Pestana_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PestanaContenedor p }) p.ActivarCommand.Execute(null);
    }

    private void Pestana_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && sender is FrameworkElement { DataContext: PestanaContenedor p })
        {
            p.CerrarCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>Al salir con cambios pendientes, se ofrece guardarlos todos, descartarlos o seguir trabajando.</summary>
    protected override async void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_saliendo || !_area.HayCambios) return;
        e.Cancel = true;
        if (!await _area.PuedeSalirAsync()) return;
        _saliendo = true;
        Close();
    }

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
