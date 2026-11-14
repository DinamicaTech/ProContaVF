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
using ProConta.Correo;
using ProConta.Correo.Mantenimiento;
using ProConta.Correo.UI.VistaModelos;
using ProConta.Correo.UI.Vistas;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Documentos;
using ProConta.Facturacion.Facturas;
using ProConta.Informes;
using ProConta.Informes.Excel;
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
    private readonly ProConta.Contabilidad.UI.Mayor.ModuloMayor _mayor;
    private readonly ProConta.Contabilidad.UI.Asientos.ModuloAsientos _asientos;
    private readonly ModuloFacturacion _facturacion;
    private readonly ModuloCompras _compras;
    private readonly ModuloCorreo _correo;
    private readonly ProConta.Norma43.UI.VistaModelos.ModuloBancos _bancos;
    private readonly ProConta.Norma43.UI.VistaModelos.ModuloLineasN43 _lineasN43;
    private readonly ProConta.Nominas.UI.VistaModelos.ModuloNominas _nominas;
    private readonly CoordinadorCorreo _coordinadorCorreo;
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly Func<PantallaMantenimientoVM> _crearMantenimiento;
    private PantallaMantenimientoVM? _mantenimiento;
    private readonly Func<PestanaImportarContaSol> _crearImportacionContaSol;
    private PestanaImportarContaSol? _importacionContaSol;
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

        // Mayor (contabilidad/mayor): pestaña Contabilidad › Mayor y el mismo control embebido en la ficha de terceros.
        _mayor = new ProConta.Contabilidad.UI.Mayor.ModuloMayor(
            new ProConta.Contabilidad.UI.Mayor.ContextoMayor(new ProConta.Contabilidad.Mayor.FuenteMayorAccess(bd), contexto.Contexto.IdSociedad, ejercicio, contexto.RazonSocial,
                new MotorAsientosAccess(bd), new Actor(sesion.NombreOperador)),
            _area, new ProConta.Contabilidad.UI.Mayor.DialogosMayor(() => this));
        _terceros.MayorEmbebido = cuentas => _mayor.CrearEmbebido(cuentas);
        // Asiento manual (contabilidad/pantallas y contabilidad/plantillas-asiento): nuevo, asistente de corrección y modificar, desde el menú y el Mayor.
        _asientos = new ProConta.Contabilidad.UI.Asientos.ModuloAsientos(
            () => new ProConta.Contabilidad.UI.Asientos.FuenteAsientoManualAccess(bd, contexto.Contexto, new Actor(sesion.NombreOperador)), _area,
            new ProConta.Contabilidad.UI.Asientos.DialogosAsiento(cuentas, new CatalogoCentrosCosteAccess(bd), contexto.Contexto, () => this));
        _mayor.Asientos = _asientos;

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
        // Ventas por facturar (facturacion/reservas): reservas de Avaibook y altas manuales que se facturan por mes, cliente y piso.
        var ventas = new ProConta.Facturacion.Ventas.ServicioVentas(bd, sociedades, facturas, serviciosFacturacion.Conceptos, autorizador);
        var clienteAvaibook = new ProConta.Facturacion.Ventas.ClienteAvaibookHttp(Http);
        _facturacion.Ventas = ventas;
        _facturacion.Avaibook = new ProConta.Facturacion.Ventas.ImportadorAvaibook(bd, ventas, clienteAvaibook, autorizador);

        // Compras (compras/pantallas): facturas recibidas y su modo «Probar».
        // OCR con Textract si el puesto tiene el perfil de AWS (compras/reconocimiento 2026-11-07); sin él, los escaneados se rellenan a mano.
        var (ocr, sinOcr) = ExtractorOcrTextract.Crear();
        var reconocimiento = new ServicioReconocimientoFacturaCompra(new ExtractorPdfPig(), ocr, new TiposCambioFrankfurter(Http), sinOcr, ExtractorOcrTextract.ConCredenciales);
        var altaProveedor = new ProConta.Compras.Proveedores.ServicioAltaProveedorCompra(bd, new ProConta.Terceros.RepositorioTercerosAccess(bd), new ProConta.Terceros.Roles.ServicioRoles(bd, cuentas), cuentas,
            new ProConta.Compras.Integraciones.ConsultaNifAeatVNif(bd), new Actor(sesion.NombreOperador), new ProConta.Compras.Integraciones.ConsultaViesRest(Http));
        var facturasCompra = new ServicioFacturasCompra(bd, reconocimiento) { AltaProveedor = altaProveedor };
        var contabilizadorCompras = new ContabilizadorCompras(bd, new MotorAsientosAccess(bd), cuentas, roles);
        var serviciosCompras = new ServiciosCompras(facturasCompra, cuentas, new CatalogoCentrosCosteAccess(bd), contexto.Contexto,
            contexto.RazonSocial, () => autorizador.PuedeAsync(Acciones.FacturasEditar),
            () => ProConta.Contabilidad.CentrosCoste.CatalogoTiposIva.ActivosAsync(bd),
            async () => (await new ProConta.Organizacion.FormasPago.CatalogoFormasPagoAccess(bd).ListarAsync()).Select(f => (f.Id, f.Descripcion)).ToList(),
            async () => (await new ProConta.Organizacion.Sociedades.CatalogoTiposRetencion(bd).VigentesAsync(DateTime.Today))
                .Select(t => new TipoRetencionCompra(t.Porcentaje, t.Descripcion, t.Clase == 2 ? ClaseRetencion.Arrendamientos : ClaseRetencion.Profesionales)).ToList(),
            new CatalogoTextosClaveCompra(bd),
            altaProveedor,
            contabilizadorCompras,
            new Actor(sesion.NombreOperador),
            ComprobarTrimestreAlEntrarAsync);
        _compras = new ModuloCompras(serviciosCompras, _area, new DialogosCompras(() => this), _terceros.AbrirFicha);

        // Correo (correo/envio y correo/pantallas): cola durable procesada mientras la aplicación está abierta, compositor común,
        // lotes con resumen y registro de correo enviado. Facturas y Terceros lo usan sin duplicar transporte ni historial.
        var servicioCorreo = new ServicioCorreo(bd, new TransporteSmtpMailKit());
        _coordinadorCorreo = new CoordinadorCorreo(servicioCorreo, t => Dispatcher.BeginInvoke(() => Avisar(t)));
        _correo = new ModuloCorreo(servicioCorreo, _coordinadorCorreo, new ContextoCorreo(contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar),
            _area, new DialogosCorreo(() => this), () => this)
        {
            AbrirOrigen = (tipo, id) =>
            {
                if (tipo == "Factura") _facturacion.AbrirFactura(id);
                else if (tipo == "Tercero") _terceros.AbrirFicha(id);
            },
        };
        _facturacion.Correo = _correo;
        _facturacion.CorreoFacturas = new PreparadorCorreoFacturas(facturas, serviciosFacturacion.Pdf, new ServicioPlantillasCorreo(bd), sociedades);
        _terceros.EnviarEmail = (direccion, idTercero, nombre) => _correo.ComponerAsync(new MensajeNuevo
        {
            IdSociedadEjercicio = contexto.Contexto.IdSociedadEjercicio, TipoCorreo = TiposCorreo.Otro, TipoOrigen = idTercero > 0 ? "Tercero" : null,
            IdOrigen = idTercero > 0 ? idTercero : null, ReferenciaOrigen = nombre, Destinatarios = [new Destinatario(TipoDestinatario.Para, direccion, nombre)],
        });
        // Recepción (correo/recepcion): el buzón POP3 de cada cuenta activa se mira mientras la aplicación está abierta.
        // Bancos (norma43 y norma43/bancos): cuentas, saldos y extractos Norma 43; los recibidos por correo usan el mismo motor.
        var importacionN43 = new ProConta.Norma43.Importacion.ServicioImportacionN43(bd);
        _bancos = new ProConta.Norma43.UI.VistaModelos.ModuloBancos(importacionN43, new ProConta.Norma43.Importacion.ConsultaBancos(bd),
            new ProConta.Norma43.UI.VistaModelos.ContextoBancos(contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar), _area,
            new ProConta.Norma43.UI.Vistas.DialogosBancos(() => this));
        // Líneas N43 (norma43/pantallas): rejilla de movimientos con su cuenta, asiento y palabras clave; abre el Mayor y el asiento.
        _lineasN43 = new ProConta.Norma43.UI.VistaModelos.ModuloLineasN43(new ProConta.Norma43.Lineas.ConsultaLineasN43(bd), new ProConta.Norma43.Lineas.ServicioLineasN43(bd),
            new ProConta.Norma43.Conciliacion.ServicioConciliacionN43(bd), new ProConta.Norma43.Asignacion.AsignadorCuentasN43(bd),
            new ProConta.Norma43.UI.VistaModelos.ContextoLineasN43(contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                AbrirMayor = (cuentasMayor, anios) => _mayor.Abrir(cuentasMayor, anios),
                VerAsiento = id => _mayor.VerAsientoAsync(id),
            },
            _area, new ProConta.Norma43.UI.Vistas.DialogosLineasN43(cuentas, () => this));
        // Nóminas (nominas y nominas/pantallas): pantalla por periodo, pestaña Nóminas de la ficha del empleado y Excel recibidos por correo.
        var importacionNominas = new ProConta.Nominas.Importacion.ServicioImportacionNominas(bd);
        var contabilizacionNominas = new ProConta.Nominas.Contabilizacion.ServicioContabilizacionNominas(bd);
        var consultaNominas = new ProConta.Nominas.Consulta.ConsultaNominas(bd);
        var contextoNominas = new ProConta.Nominas.UI.VistaModelos.ContextoNominas(contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
        {
            AbrirFicha = id => _terceros.AbrirFicha(id, ProConta.Terceros.UI.Logica.PestanaFicha.Nominas),
            VerAsiento = id => _mayor.VerAsientoAsync(id),
            AbrirCorreoRecibido = () => _correo.AbrirRecibido(),
        };
        _nominas = new ProConta.Nominas.UI.VistaModelos.ModuloNominas(
            new ProConta.Nominas.UI.VistaModelos.ServiciosNominas(consultaNominas, importacionNominas, contabilizacionNominas,
                new ProConta.Terceros.Roles.ServicioEmpleados(bd, cuentas, new RepositorioTercerosAccess(bd), new Actor(sesion.NombreOperador))),
            contextoNominas, _area, new ProConta.Nominas.UI.Vistas.DialogosNominas(() => this));
        _terceros.NominasEmbebidas = idTercero => ProConta.Nominas.UI.VistaModelos.NominasEmpleadoVM.CrearAsync(consultaNominas, contextoNominas, idTercero, id => _nominas.Abrir(id));
        _coordinadorCorreo.Recepcion = new ServicioRecepcionCorreo(bd, new ClientePop3MailKit(), servicioCorreo, new ProcesadorFacturasCompraCorreo(bd, facturasCompra, contabilizadorCompras),
            extractos: new ProConta.Norma43.Importacion.ProcesadorExtractosCorreo(importacionN43),
            nominas: new ProConta.Nominas.Correo.ProcesadorNominasCorreo(importacionNominas, contabilizacionNominas));
        _coordinadorCorreo.Iniciar();
        Closed += async (_, _) => await _coordinadorCorreo.DisposeAsync();
        _ = AvisarDesvioCorreoAsync(servicioCorreo);

        var mantenimiento = new ServicioMantenimiento(
            new IProveedorMantenimiento[] { new ProveedorConfiguracionGeneral(bd) }.Concat(ProveedoresOrganizacion.Todos(bd)).Concat(ProveedoresFacturacion.Todos(bd, cuentas)).Concat(ProConta.Facturacion.Ventas.ProveedoresVentas.Todos(bd, ventas, clienteAvaibook))
                .Concat(ProConta.Compras.Mantenimiento.ProveedoresCompras.Todos(bd)).Concat(ProveedoresCorreo.Todos(bd))
                .Concat(ProConta.Activos.Mantenimiento.ProveedoresActivos.Todos(bd, cuentas)),
            () => autorizador.PuedeAsync(Acciones.MantenimientoEditar));
        var contextoMantenimiento = new ContextoMantenimiento(contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador));
        _crearMantenimiento = () => new PantallaMantenimientoVM(mantenimiento, contextoMantenimiento,
            (titulo, mensaje, detalles, opciones) => ServicioMensajes.Preguntar(this, titulo, mensaje, detalles, opciones),
            Avisar, vm => new VistaMantenimiento(vm))
        { Cuentas = new SelectorCuentasMantenimiento(cuentas, new CatalogoCentrosCosteAccess(bd), () => this) };

        // Importar ContaSol (implantacion/importador-contasol): una pestaña, en Configuración.
        var importadorContaSol = new ProConta.Implantacion.ContaSol.ImportadorContaSol(bd, new MotorAsientosAccess(bd), new CatalogoCentrosCosteAccess(bd),
            new RepositorioTercerosAccess(bd), roles);
        _crearImportacionContaSol = () => new PestanaImportarContaSol(importadorContaSol, new Actor(sesion.NombreOperador),
            () => autorizador.PuedeAsync(Acciones.MantenimientoEditar), () => this, Avisar);

        Geometry? Icono(string clave) => TryFindResource("Icono." + clave) as Geometry;
        var irAInicio = new ComandoDelegado(() => _area.Activar(_area.Lista[0].Pantalla));
        var abrirTerceros = new ComandoDelegado(_terceros.AbrirBuscador);
        var abrirFacturas = new ComandoDelegado(_facturacion.AbrirListado);
        var abrirProgramadas = new ComandoDelegado(_facturacion.AbrirProgramadas);
        var abrirVentas = new ComandoDelegado(_facturacion.AbrirVentas);
        var abrirParametros = new ComandoDelegado(AbrirParametros);
        var abrirRecibidas = new ComandoDelegado(_compras.AbrirRecibidas);
        var abrirContaSol = new ComandoDelegado(AbrirImportacionContaSol);
        var abrirCorreo = new ComandoDelegado(() => _correo.AbrirRegistro());
        var abrirMayor = new ComandoDelegado(() => _mayor.Abrir());
        // Informes (contabilidad/balances e informes/excel): cada opción genera el libro Excel con su tabla dinámica y lo abre.
        _balances = new ProConta.Contabilidad.Balances.GeneradorBalances(bd);
        _razonSocial = contexto.RazonSocial;
        var balanceSituacion = new ComandoDelegado(() => _ = GenerarBalanceAsync(ProConta.Contabilidad.Balances.TipoBalance.Situacion));
        var balancePyG = new ComandoDelegado(() => _ = GenerarBalanceAsync(ProConta.Contabilidad.Balances.TipoBalance.PerdidasYGanancias));
        Navegacion.ItemsSource = new[]
        {
            new OpcionNavegacion("Inicio", Icono("layout"), true, false, "Página de inicio", irAInicio),
            new OpcionNavegacion("Datos maestros", Icono("user"), true, false, "Terceros: clientes, proveedores, contactos y demás personas y organizaciones", abrirTerceros),
            new OpcionNavegacion("Terceros", null, true, false, "Buscador y fichas de terceros (clientes, proveedores, contactos…)", abrirTerceros, EsHija: true),
            new OpcionNavegacion("Ventas", Icono("doc"), true, false, "Facturas de venta: emitidas y borradores", abrirFacturas),
            new OpcionNavegacion("Facturas programadas", null, true, false, "Facturas que se generan solas en cada vencimiento: su programación, sus líneas y la generación por lotes", abrirProgramadas, EsHija: true),
            new OpcionNavegacion("Facturas emitidas", null, true, false, "Listado de facturas y borradores del ejercicio, con su detalle y acciones", abrirFacturas, EsHija: true),
            new OpcionNavegacion("Ventas por facturar", null, true, false, "Reservas de Avaibook y otras ventas registradas antes de facturarlas: importación, ficha y «Facturar ventas…» por mes, cliente y piso", abrirVentas, EsHija: true),
            new OpcionNavegacion("Compras", Icono("scan"), true, false, "Facturas de compra: los PDF de la carpeta de proveedores, su revisión y el modo prueba del reconocimiento", abrirRecibidas),
            new OpcionNavegacion("Facturas recibidas", null, true, false, "Facturas de compra del ejercicio reconocidas de sus PDF: revisión, corrección y registro (y el modo prueba, en el menú contextual)", abrirRecibidas, EsHija: true),
            new OpcionNavegacion("Correo", Icono("mail"), true, false, "Correo enviado por ProContaVF: estado de cada mensaje, contenido, adjuntos e intentos", abrirCorreo),
            new OpcionNavegacion("Correo enviado", null, true, false, "Registro común del correo enviado (facturas y demás), con reintentos y reenvíos en el menú contextual", abrirCorreo, EsHija: true),
            new OpcionNavegacion("Correo recibido", null, true, false, "Correo que llega a la cuenta del ejercicio: facturas de compra importadas, documentos en espera y remitentes no autorizados", new ComandoDelegado(() => _correo.AbrirRecibido()), EsHija: true),
            new OpcionNavegacion("Contabilidad", Icono("book"), true, false, "Contabilidad: el Mayor de cualquier cuenta, con varios ejercicios, vistas y filtros", abrirMayor),
            new OpcionNavegacion("Mayor", null, true, false,
                "Mayor de una o varias cuentas (o de un código corto) en los ejercicios que elijas: vistas Normal, Compacto, Solo pendientes y Agrupar 700, filtros, asiento e historial de cada apunte",
                abrirMayor, EsHija: true),
            new OpcionNavegacion("Asiento manual", null, true, false,
                "Asiento nuevo hecho a mano o desde una plantilla, con su vista previa; para corregir un apunte, ábrelo desde el Mayor con «Asistente de corrección…»",
                new ComandoDelegado(() => _ = _asientos.AbrirNuevoAsync()), EsHija: true),
            new OpcionNavegacion("Bancos", Icono("bank"), true, false,
                "Cuentas bancarias de la sociedad: saldo según el banco y según su 572, movimientos, extractos Norma 43 importados, «Importar N43» y reversión de lotes",
                new ComandoDelegado(() => _bancos.Abrir())),
            new OpcionNavegacion("Cuentas bancarias", null, true, false,
                "Cuentas bancarias de la sociedad: saldos según el banco y según su 572, extractos importados, «Importar N43» y reversión de lotes",
                new ComandoDelegado(() => _bancos.Abrir()), EsHija: true),
            new OpcionNavegacion("Líneas N43", null, true, false,
                "Movimientos bancarios importados: su cuenta de destino, asiento y palabras clave, con filtros y todas las acciones en el menú contextual",
                new ComandoDelegado(() => _lineasN43.Abrir()), EsHija: true),
            new OpcionNavegacion("Nóminas", Icono("coin"), true, false,
                "Nóminas del ejercicio por mes y paga: importar el Excel de la gestoría (o el que llega por correo), resolver trabajadores, contabilizar, deshacer y reemplazar",
                new ComandoDelegado(() => _nominas.Abrir())),
            new OpcionNavegacion("Informes", Icono("xls"), true, false, "Informes del ejercicio de trabajo en Excel, con tabla dinámica", balanceSituacion),
            new OpcionNavegacion("Balance de situación", null, true, false,
                "Genera en Excel el balance de situación del ejercicio de trabajo (PGC Pymes 2008): tabla dinámica por bloque, apartado, título, cuenta y subcuenta, con los meses en columnas y filtros por centro de coste y departamento, y lo abre",
                balanceSituacion, EsHija: true),
            new OpcionNavegacion("Pérdidas y ganancias", null, true, false,
                "Genera en Excel la cuenta de pérdidas y ganancias del ejercicio de trabajo (PGC Pymes 2008), sin la regularización ni el cierre: tabla dinámica por apartado, título, cuenta y subcuenta, con los meses en columnas y filtros por centro de coste y departamento, y la abre",
                balancePyG, EsHija: true),
            new OpcionNavegacion("Configuración", Icono("gear"), true, false, "Parámetros y tablas: empresas, ejercicios, series, conceptos, formas de pago…", abrirParametros),
            new OpcionNavegacion("Parámetros y tablas", null, true, false, "Mantenimiento de las tablas y los parámetros de la aplicación, con buscador", abrirParametros, EsHija: true),
            new OpcionNavegacion("Importar ContaSol", null, true, false, "Importa la contabilidad de ContaSol por ejercicios (una BD por ejercicio), con simulación previa", abrirContaSol, EsHija: true),
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

    /// <summary>Abre (o activa) la pestaña Importar ContaSol.</summary>
    private void AbrirImportacionContaSol()
    {
        if (_importacionContaSol is not null && _area.Pestanas.Contains(_importacionContaSol))
        {
            _area.Activar(_importacionContaSol);
            return;
        }
        _importacionContaSol = _crearImportacionContaSol();
        _area.Abrir(_importacionContaSol);
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

    /// <summary>Con el desvío de pruebas activo, la barra de estado lo dice siempre (correo/envio 2026-11-10 19:40).</summary>
    private async Task AvisarDesvioCorreoAsync(ServicioCorreo correo)
    {
        try
        {
            if (await correo.DesvioAsync() is not { } desvio) return;
            TextoDatos.Text += $" · correo desviado a {desvio}";
            TextoDatos.ToolTip = $"{TextoDatos.ToolTip}{Environment.NewLine}Correo de pruebas: todo lo que se envía sale solo a {desvio} (Parámetros y tablas › Configuración general).";
        }
        catch (Exception) { /* sin la columna o sin configuración: no hay desvío que avisar */ }
    }

    private readonly ProConta.Contabilidad.Balances.GeneradorBalances _balances;
    private readonly string _razonSocial;
    private bool _generandoBalance;

    /// <summary>
    /// Genera el balance del ejercicio de trabajo, lo guarda en Documentos\ProContaVF\Informes y lo abre con Excel. En la barra de
    /// estado dice los totales (activo y patrimonio neto y pasivo, o el resultado) y si hay cuentas sin clasificar.
    /// </summary>
    private async Task GenerarBalanceAsync(ProConta.Contabilidad.Balances.TipoBalance tipo)
    {
        if (_generandoBalance) { Avisar("Ya estoy generando un balance: espera a que se abra en Excel."); return; }
        _generandoBalance = true;
        var nombre = tipo == ProConta.Contabilidad.Balances.TipoBalance.Situacion ? "balance de situación" : "balance de pérdidas y ganancias";
        Avisar($"Generando el {nombre} del ejercicio {_ejercicioTrabajo.Ejercicio}…");
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        try
        {
            var balance = await Task.Run(() => _balances.GenerarAsync(tipo, _ejercicioTrabajo, _razonSocial));
            var carpeta = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ProContaVF", "Informes");
            System.IO.Directory.CreateDirectory(carpeta);
            var ruta = System.IO.Path.Combine(carpeta, ExportadorBalanceExcel.NombreFichero(tipo, _ejercicioTrabajo.Ejercicio, DateTime.Now));
            await Task.Run(() => ExportadorBalanceExcel.Exportar(balance, ruta));
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            var totales = tipo == ProConta.Contabilidad.Balances.TipoBalance.Situacion
                ? string.Join(" · ", balance.TotalesPorBloque.Select(t => $"{t.Bloque.ToLower(es)} {t.Importe.ToString("N2", es)} €"))
                : $"resultado {balance.Total.ToString("N2", es)} €";
            var sin = balance.SinClasificar.Count == 0 ? "" :
                $" · {balance.SinClasificar.Count} cuentas sin clasificar ({string.Join(", ", balance.SinClasificar.Take(5).Select(c => c.Cuenta))}{(balance.SinClasificar.Count > 5 ? "…" : "")}): mira la hoja SinClasificar";
            Avisar($"{char.ToUpper(nombre[0], es)}{nombre[1..]} {_ejercicioTrabajo.Ejercicio} abierto en Excel: {totales}{sin}.");
        }
        catch (Exception ex)
        {
            ServicioMensajes.Preguntar(this, $"No he podido generar el {nombre}", ex.Message,
                ex is InvalidOperationException ? null : [$"Detalle técnico: {ex.GetType().Name}"],
                new OpcionMensaje("Cerrar", "Vuelve a la pantalla anterior", Principal: true, Cancelar: true));
        }
        finally
        {
            Cursor = anterior;
            _generandoBalance = false;
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
