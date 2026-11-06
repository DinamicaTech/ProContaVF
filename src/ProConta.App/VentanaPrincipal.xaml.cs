using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ProConta.Compras.Facturas;
using ProConta.Compras.Integraciones;
using ProConta.Compras.Reconocimiento;
using ProConta.Compras.UI.VistaModelos;
using ProConta.Compras.UI.Vistas;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Documentos;
using ProConta.Facturacion.Facturas;
using ProConta.Informes;
using ProConta.Facturacion.Programadas;
using ProConta.Facturacion.Mantenimiento;
using ProConta.Facturacion.UI.VistaModelos;
using ProConta.Facturacion.UI.Vistas;
using ProConta.Mantenimiento;
using ProConta.Mantenimiento.UI;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Organizacion.FormasPago;
using ProConta.Organizacion.Geografia;
using ProConta.Organizacion.Mantenimiento;
using ProConta.Organizacion.Sociedades;
using ProConta.Plataforma.Configuracion;
using ProConta.Seguridad.Acceso;
using ProConta.Seguridad.Permisos;
using ProConta.Terceros;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;
using ProConta.Terceros.UI.Vistas;
using ProConta.Terceros.UI.VistaModelos;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Controles;

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
    private readonly ModuloFacturacion _facturacion;
    private readonly ModuloCompras _compras;
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Func<PantallaMantenimientoVM> _crearMantenimiento;
    private PantallaMantenimientoVM? _mantenimiento;
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

        // Facturación de venta (facturacion/pantallas) y Parámetros y tablas (plataforma/mantenimiento).
        var roles = new ServicioRoles(bd, cuentas);
        var sociedades = new ServicioSociedadesAccess(bd);
        var autorizador = new AutorizadorAccionesAccess(bd, new ServicioOperadores(bd), sesion.IdOperador);
        var contabilizador = new ContabilizadorVentas(bd, new MotorAsientosAccess(bd), cuentas, roles, sociedades);
        var facturas = new ServicioFacturas(bd, sociedades, new RepositorioTercerosAccess(bd), roles, cuentas, contabilizador, autorizador);
        var apoyo = new ApoyoEditorFacturas(bd, roles);
        var serviciosFacturacion = new ServiciosFacturacion(facturas, new ConsultaFacturas(bd), new CatalogoConceptos(bd, cuentas), apoyo, new CatalogoFormasPagoAccess(bd),
            new LectorConfiguracionEjercicio(bd), contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), new CatalogoCentrosCosteAccess(bd),
            new ServicioProgramadas(bd, sociedades, facturas, autorizador),
            new ServicioPdfFacturas(bd, facturas, sociedades, new GeneradorPdfFastReport(), autorizador));
        _facturacion = new ModuloFacturacion(serviciosFacturacion, _area, new DialogosFacturacion(apoyo, () => this), _terceros.AbrirFicha);

        // Compras (compras/pantallas): facturas recibidas y su modo «Probar».
        var reconocimiento = new ServicioReconocimientoFacturaCompra(new ExtractorPdfPig(), null, new TiposCambioFrankfurter(Http));
        var serviciosCompras = new ServiciosCompras(new ServicioFacturasCompra(bd, reconocimiento), cuentas, new CatalogoCentrosCosteAccess(bd), contexto.Contexto,
            contexto.RazonSocial, () => autorizador.PuedeAsync(Acciones.FacturasEditar));
        _compras = new ModuloCompras(serviciosCompras, _area, new DialogosCompras(() => this), _terceros.AbrirFicha);

        var mantenimiento = new ServicioMantenimiento(
            new IProveedorMantenimiento[] { new ProveedorConfiguracionGeneral(bd) }.Concat(ProveedoresOrganizacion.Todos(bd)).Concat(ProveedoresFacturacion.Todos(bd, cuentas)),
            () => autorizador.PuedeAsync(Acciones.MantenimientoEditar));
        var contextoMantenimiento = new ContextoMantenimiento(contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador));
        _crearMantenimiento = () => new PantallaMantenimientoVM(mantenimiento, contextoMantenimiento,
            (titulo, mensaje, detalles, opciones) => ServicioMensajes.Preguntar(this, titulo, mensaje, detalles, opciones),
            Avisar, vm => new VistaMantenimiento(vm));

        const string proxima = "Aún no está disponible en esta versión: llegará con las pantallas de su área.";
        Geometry? Icono(string clave) => TryFindResource("Icono." + clave) as Geometry;
        var irAInicio = new ComandoDelegado(() => _area.Activar(_area.Lista[0].Pantalla));
        var abrirTerceros = new ComandoDelegado(_terceros.AbrirBuscador);
        var abrirFacturas = new ComandoDelegado(_facturacion.AbrirListado);
        var abrirProgramadas = new ComandoDelegado(_facturacion.AbrirProgramadas);
        var abrirParametros = new ComandoDelegado(AbrirParametros);
        var abrirRecibidas = new ComandoDelegado(_compras.AbrirRecibidas);
        Navegacion.ItemsSource = new[]
        {
            new OpcionNavegacion("Inicio", Icono("layout"), true, false, "Página de inicio", irAInicio),
            new OpcionNavegacion("Datos maestros", Icono("user"), true, false, "Terceros: clientes, proveedores, contactos y demás personas y organizaciones", abrirTerceros),
            new OpcionNavegacion("Terceros", null, true, false, "Buscador y fichas de terceros (clientes, proveedores, contactos…)", abrirTerceros, EsHija: true),
            new OpcionNavegacion("Facturación", Icono("doc"), true, false, "Facturas de venta: emitidas y borradores", abrirFacturas),
            new OpcionNavegacion("Facturas programadas", null, true, false, "Facturas que se generan solas en cada vencimiento: su programación, sus líneas y la generación por lotes", abrirProgramadas, EsHija: true),
            new OpcionNavegacion("Facturas", null, true, false, "Listado de facturas y borradores del ejercicio, con su detalle y acciones", abrirFacturas, EsHija: true),
            new OpcionNavegacion("Compras", Icono("scan"), true, false, "Facturas de compra: los PDF de la carpeta de proveedores, su revisión y el modo prueba del reconocimiento", abrirRecibidas),
            new OpcionNavegacion("Facturas recibidas", null, true, false, "Facturas de compra del ejercicio reconocidas de sus PDF: revisión, corrección y registro (y el modo prueba, en el menú contextual)", abrirRecibidas, EsHija: true),
            new OpcionNavegacion("Contabilidad", Icono("book"), false, false, proxima),
            new OpcionNavegacion("Bancos", Icono("bank"), false, false, proxima),
            new OpcionNavegacion("Nóminas", Icono("coin"), false, false, proxima),
            new OpcionNavegacion("Informes", Icono("xls"), false, false, proxima),
            new OpcionNavegacion("Configuración", Icono("gear"), true, false, "Parámetros y tablas: empresas, ejercicios, series, conceptos, formas de pago…", abrirParametros),
            new OpcionNavegacion("Parámetros y tablas", null, true, false, "Mantenimiento de las tablas y los parámetros de la aplicación, con buscador", abrirParametros, EsHija: true),
        };
        RefrescarEstado();
    }

    public ICommand AlternarNavegacionCommand { get; }
    public ICommand GuardarActivaCommand { get; }
    public ICommand CerrarActivaCommand { get; }

    /// <summary>Abre (o activa) la pestaña Parámetros y tablas.</summary>
    private void AbrirParametros()
    {
        if (_mantenimiento is not null && _area.Pestanas.Contains(_mantenimiento))
        {
            _area.Activar(_mantenimiento);
            return;
        }
        _mantenimiento = _crearMantenimiento();
        _area.Abrir(_mantenimiento);
        _ = _mantenimiento.IniciarAsync();
    }

    internal ModuloFacturacion Facturacion => _facturacion;

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
    /// <remarks>
    /// La pregunta y el cierre van después de terminar este Closing: llamar a Close() (o mostrar un diálogo) mientras la
    /// ventana se está cerrando lanza InvalidOperationException, y «Salir sin guardar» responde sin esperar.
    /// </remarks>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_saliendo || !_area.HayCambios) return;
        e.Cancel = true;
        if (_preguntandoSalida) return;
        _preguntandoSalida = true;
        Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!await _area.PuedeSalirAsync()) return;
                _saliendo = true;
                Close();
            }
            finally
            {
                _preguntandoSalida = false;
            }
        });
    }

    private bool _preguntandoSalida;

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
