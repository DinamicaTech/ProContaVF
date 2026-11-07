using ProConta.UI.Base.Preferencias;
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
        _trimestres = new ServicioTrimestreActivo(bd);
        _ejercicioTrabajo = contexto.Contexto;
        _puedeCambiarTrimestre = () => autorizador.PuedeAsync(Acciones.TrimestreCambiar);
        _ = RefrescarTrimestreAsync();
        var contabilizador = new ContabilizadorVentas(bd, new MotorAsientosAccess(bd), cuentas, roles, sociedades);
        var facturas = new ServicioFacturas(bd, sociedades, new RepositorioTercerosAccess(bd), roles, cuentas, contabilizador, autorizador);
        var apoyo = new ApoyoEditorFacturas(bd, roles);
        var serviciosFacturacion = new ServiciosFacturacion(facturas, new ConsultaFacturas(bd), new CatalogoConceptos(bd, cuentas), apoyo, new CatalogoFormasPagoAccess(bd),
            new LectorConfiguracionEjercicio(bd), contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), new CatalogoCentrosCosteAccess(bd),
            new ServicioProgramadas(bd, sociedades, facturas, autorizador),
            new ServicioPdfFacturas(bd, facturas, sociedades, new GeneradorPdfFastReport(), autorizador));
        _facturacion = new ModuloFacturacion(serviciosFacturacion, _area, new DialogosFacturacion(apoyo, () => this), _terceros.AbrirFicha);

        // Compras (compras/pantallas): facturas recibidas y su modo «Probar».
        // OCR con Textract si el puesto tiene el perfil de AWS (compras/reconocimiento 2026-11-07); sin él, los escaneados se rellenan a mano.
        var (ocr, sinOcr) = ExtractorOcrTextract.Crear();
        var reconocimiento = new ServicioReconocimientoFacturaCompra(new ExtractorPdfPig(), ocr, new TiposCambioFrankfurter(Http), sinOcr, ExtractorOcrTextract.ConCredenciales);
        var altaProveedor = new ProConta.Compras.Proveedores.ServicioAltaProveedorCompra(bd, new ProConta.Terceros.RepositorioTercerosAccess(bd), new ProConta.Terceros.Roles.ServicioRoles(bd, cuentas), cuentas,
            new ProConta.Compras.Integraciones.ConsultaNifAeatVNif(bd), new Actor(sesion.NombreOperador));
        var serviciosCompras = new ServiciosCompras(new ServicioFacturasCompra(bd, reconocimiento) { AltaProveedor = altaProveedor }, cuentas, new CatalogoCentrosCosteAccess(bd), contexto.Contexto,
            contexto.RazonSocial, () => autorizador.PuedeAsync(Acciones.FacturasEditar),
            async () => (await new LectorConfiguracionEjercicio(bd).ObtenerAsync(contexto.Contexto.IdSociedadEjercicio))?.TiposIva.Select(t => t.Porcentaje).ToList() ?? [],
            async () => (await new ProConta.Organizacion.FormasPago.CatalogoFormasPagoAccess(bd).ListarAsync()).Select(f => (f.Id, f.Descripcion)).ToList(),
            async () => (await new ProConta.Organizacion.Sociedades.CatalogoTiposRetencion(bd).VigentesAsync(DateTime.Today))
                .Select(t => new TipoRetencionCompra(t.Porcentaje, t.Descripcion, t.Clase == 2 ? ClaseRetencion.Arrendamientos : ClaseRetencion.Profesionales)).ToList(),
            new CatalogoTextosClaveCompra(bd),
            altaProveedor,
            new ContabilizadorCompras(bd, new MotorAsientosAccess(bd), cuentas, roles),
            new Actor(sesion.NombreOperador),
            ComprobarTrimestreAlEntrarAsync);
        _compras = new ModuloCompras(serviciosCompras, _area, new DialogosCompras(() => this), _terceros.AbrirFicha);

        var mantenimiento = new ServicioMantenimiento(
            new IProveedorMantenimiento[] { new ProveedorConfiguracionGeneral(bd) }.Concat(ProveedoresOrganizacion.Todos(bd)).Concat(ProveedoresFacturacion.Todos(bd, cuentas))
                .Concat(ProConta.Compras.Mantenimiento.ProveedoresCompras.Todos(bd)),
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
            new OpcionNavegacion("Ventas", Icono("doc"), true, false, "Facturas de venta: emitidas y borradores", abrirFacturas),
            new OpcionNavegacion("Facturas programadas", null, true, false, "Facturas que se generan solas en cada vencimiento: su programación, sus líneas y la generación por lotes", abrirProgramadas, EsHija: true),
            new OpcionNavegacion("Facturas emitidas", null, true, false, "Listado de facturas y borradores del ejercicio, con su detalle y acciones", abrirFacturas, EsHija: true),
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
    // ---------- trimestre contable activo (compras/pantallas 2026-11-06 12:10) ----------

    private readonly ServicioTrimestreActivo _trimestres;
    private readonly ProConta.Nucleo.Contexto.ContextoSociedadEjercicio _ejercicioTrabajo;
    private readonly Func<Task<bool>> _puedeCambiarTrimestre;
    private const string ClaveAvisoTrimestre = "compras.trimestre.avisarDesde";

    private async Task<int?> RefrescarTrimestreAsync()
    {
        try
        {
            var t = await _trimestres.ObtenerAsync(_ejercicioTrabajo.IdSociedadEjercicio);
            TextoTrimestre.Text = t is int n ? $"Trimestre activo: {ServicioTrimestreActivo.Nombre(n)}" : "Sin trimestre activo";
            return t;
        }
        catch (Exception ex)
        {
            TextoTrimestre.Text = "Trimestre activo: ¿?";
            TextoTrimestre.ToolTip = $"No he podido leer el trimestre activo: {ex.Message}";
            return null;
        }
    }

    private async void AlPulsarTrimestre(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        try
        {
            await GestionarTrimestreAsync(alEntrar: false);
        }
        catch (Exception ex)
        {
            Avisar($"No he podido cambiar el trimestre activo: {ex.Message}");
        }
    }

    /// <summary>
    /// Cerrar el trimestre activo y activar el siguiente, o reactivar el anterior con aviso si hoy + 10 días cae en el activo.
    /// Al entrar en facturas de compra fuera del trimestre activo, ofrece además recordarlo en 1, 3 o 7 días (por operador).
    /// </summary>
    private async Task GestionarTrimestreAsync(bool alEntrar)
    {
        var activo = await RefrescarTrimestreAsync();
        var hoy = _trimestres.Hoy;
        var ejercicio = _ejercicioTrabajo.Ejercicio;
        if (!await _puedeCambiarTrimestre())
        {
            if (!alEntrar) Avisar("No tienes permiso para cambiar el trimestre activo.");
            return;
        }
        var opciones = new List<(OpcionMensaje Opcion, Func<Task>? Accion)>();
        if (activo is not 4)
        {
            var siguiente = activo is int a ? a + 1 : ServicioTrimestreActivo.Trimestre(hoy);
            opciones.Add((new OpcionMensaje(activo is int x ? (alEntrar ? $"Activar {ServicioTrimestreActivo.Nombre(siguiente)}" : $"Cerrar {ServicioTrimestreActivo.Nombre(x)} y activar {ServicioTrimestreActivo.Nombre(siguiente)}") : $"Activar {ServicioTrimestreActivo.Nombre(siguiente)}",
                "Un trimestre cerrado ya no se modifica: los cambios de sus facturas irán con contra-asiento al trimestre activo", Principal: true),
                async () =>
                {
                    var r = await _trimestres.ActivarSiguienteAsync(_ejercicioTrabajo.IdSociedadEjercicio);
                    Avisar(r.EsCorrecto ? $"Trimestre activo: {ServicioTrimestreActivo.Nombre(r.Valor)}." : r.Error!.Mensaje + " " + r.Error.Causa);
                }));
        }
        if (!alEntrar && ServicioTrimestreActivo.PuedeReactivar(activo, ejercicio, hoy))
            opciones.Add((new OpcionMensaje($"Reactivar {ServicioTrimestreActivo.Nombre(activo!.Value - 1)}", "Vuelve a abrir el trimestre anterior", Peligro: true),
                async () =>
                {
                    var seguro = ServicioMensajes.Preguntar(this, "Reactivar el trimestre anterior",
                        $"Vas a reactivar {ServicioTrimestreActivo.Nombre(activo.Value - 1)}. Si ya presentaste sus modelos de IVA, lo que cambies no coincidirá con lo presentado.", null,
                        new OpcionMensaje("Reactivar", "Vuelve a abrir el trimestre anterior", Peligro: true), new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true));
                    if (seguro != 0) return;
                    var r = await _trimestres.ReactivarAnteriorAsync(_ejercicioTrabajo.IdSociedadEjercicio, ejercicio);
                    Avisar(r.EsCorrecto ? $"Trimestre activo: {ServicioTrimestreActivo.Nombre(r.Valor)}." : r.Error!.Mensaje);
                }));
        if (alEntrar)
            foreach (var dias in new[] { 1, 3, 7 })
                opciones.Add((new OpcionMensaje($"En {dias} {(dias == 1 ? "día" : "días")}", $"Me lo recuerdas dentro de {dias} {(dias == 1 ? "día" : "días")}"),
                    () => { PreferenciasOperador.Actual.Guardar(ClaveAvisoTrimestre, hoy.AddDays(dias)); return Task.CompletedTask; }));
        opciones.Add((new OpcionMensaje(alEntrar ? "Ahora no" : "Cancelar", alEntrar ? "Vuelve a avisar la próxima vez" : "No cambia nada", Cancelar: true), null));
        if (opciones.Count == 1)
        {
            Avisar("El cuarto trimestre no se cierra aquí: se cierra con el cierre del ejercicio.");
            return;
        }
        var texto = alEntrar
            ? $"Hoy es {hoy:dd/MM/yyyy} ({ServicioTrimestreActivo.Nombre(ServicioTrimestreActivo.Trimestre(hoy))}) y el trimestre contable activo es {ServicioTrimestreActivo.Nombre(activo ?? 0)}. Las facturas de {ServicioTrimestreActivo.Nombre(activo ?? 0)} se seguirán contabilizando en ese trimestre, lo cual afecta a su cierre de IVA. Si ya has cerrado ese trimestre (presentado la declaración del IVA), activa el trimestre siguiente para evitar descuadres con el IVA presentado. También puedo recordártelo en 1, 3 o 7 días."
            : activo is int t ? $"El trimestre contable activo es {ServicioTrimestreActivo.Nombre(t)}." : "El ejercicio no tiene trimestre activo.";
        var eleccion = ServicioMensajes.Preguntar(this, alEntrar ? "Hoy no cae en el trimestre activo" : "Trimestre activo", texto, null, [.. opciones.Select(o => o.Opcion)]);
        if (eleccion >= 0 && eleccion < opciones.Count && opciones[eleccion].Accion is { } accion) await accion();
        // El foco vuelve a la pantalla activa: si no, sus atajos (p. ej. Ctrl+L) no responden hasta hacer clic en ella.
        if (_area.Activa?.Vista is UIElement vista) vista.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        await RefrescarTrimestreAsync();
    }

    /// <summary>Al entrar en facturas de compra: avisa si hoy no cae en el trimestre activo, salvo que el operador lo pospusiera.</summary>
    private async Task ComprobarTrimestreAlEntrarAsync()
    {
        try
        {
            var activo = await _trimestres.ObtenerAsync(_ejercicioTrabajo.IdSociedadEjercicio);
            var hoy = _trimestres.Hoy;
            if (!ServicioTrimestreActivo.FueraDelActivo(activo, _ejercicioTrabajo.Ejercicio, hoy)) return;
            if (PreferenciasOperador.Actual.Leer<DateTime?>(ClaveAvisoTrimestre) is DateTime desde && hoy < desde) return;
            await GestionarTrimestreAsync(alEntrar: true);
        }
        catch (Exception ex)
        {
            Avisar($"No he podido comprobar el trimestre activo: {ex.Message}");
        }
    }

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
