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

/// <summary>
/// Opción de la navegación lateral (un área o, sangrada, una de sus pantallas). Los grupos con más de tres opciones
/// empiezan plegados y su título los despliega o pliega en lugar de abrir una pantalla (MenuPlegable).
/// </summary>
public sealed class OpcionNavegacion(string titulo, Geometry? icono, bool disponible, bool activa, string ayuda, ICommand? comando = null, bool EsHija = false, bool EsNieta = false)
    : ProConta.UI.Base.Navegacion.OpcionPlegable
{
    private ICommand? _plegar;

    public string Titulo { get; } = titulo;
    public Geometry? Icono { get; } = icono;
    public bool Disponible { get; } = disponible;
    public bool Activa { get; } = activa;
    public override string Ayuda { get; } = ayuda;
    public bool EsHija { get; } = EsHija;
    /// <summary>Tercer nivel del menú: una opción dentro de un submenú (p. ej. Contabilidad › Fin de ejercicio › Amortización de activos).</summary>
    public bool EsNieta { get; } = EsNieta;
    public override int Nivel => EsNieta ? 2 : EsHija ? 1 : 0;

    /// <summary>En un grupo plegable, despliega o pliega; en los demás, abre su pantalla.</summary>
    public ICommand? Comando => EsPlegable ? _plegar : comando;

    /// <summary>Conecta el título de un grupo plegable con el menú al que pertenece.</summary>
    public void ConectarPlegado(Action alternar) => _plegar = new ComandoDelegado(alternar);

    public Thickness Sangria => EsNieta ? new Thickness(28, 0, 0, 0) : EsHija ? new Thickness(14, 0, 0, 0) : new Thickness(0);
    public FontWeight Peso => EsHija && !EsPlegable || EsNieta ? FontWeights.Normal : FontWeights.SemiBold;
    public bool ConPunto => EsHija || EsNieta;
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
    private readonly ProConta.Contabilidad.UI.Diario.ModuloDiario _diario;
    private readonly ProConta.Contabilidad.UI.Liquidacion.ModuloLiquidacionIva _liquidacionIva;
    private readonly ProConta.Contabilidad.UI.ModelosTributarios.ModuloModelosTributarios _modelos;
    private readonly ProConta.Contabilidad.UI.Dividendos.ModuloRepartoDividendos _dividendos;
    private readonly ProConta.Contabilidad.UI.Dividendos.ModuloAplicacionResultado _aplicacionResultado;
    private readonly ProConta.Contabilidad.UI.Prorrata.ModuloRegularizacionProrrata _prorrata;
    private readonly ProConta.Contabilidad.UI.ImpuestoSociedades.ModuloImpuestoSociedades _impuestoSociedades;
    private readonly ProConta.Contabilidad.UI.Cierre.ModuloCierreEjercicio _cierreEjercicio;
    private readonly ProConta.Contabilidad.UI.CuentasAnuales.ModuloCuentasAnuales _cuentasAnuales;
    private readonly ProConta.Contabilidad.UI.Legalizacion.ModuloLegalizacion _legalizacion;
    private readonly ProConta.Contabilidad.UI.FacturasPendientes.ModuloFacturasPendientes _facturasPendientes;
    private readonly ProConta.Contabilidad.UI.Amortizacion.ModuloAmortizacion _amortizacion;
    private readonly ProConta.Contabilidad.UI.Saldos.ModuloRecalcularSaldos _recalcularSaldos;
    private readonly ProConta.Contabilidad.UI.ReinicioCuadre.ModuloReinicioCuadre _reinicioCuadre;
    private readonly ProConta.Contabilidad.UI.Centimos.ModuloCentimos _centimos;
    private readonly ProConta.Activos.UI.ModuloActivos _activos;
    private readonly ProConta.Documentos.UI.ModuloDocumentos _documentos;
    private readonly ProConta.Documentos.ServicioDocumentos _servicioDocumentos;
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

    public VentanaPrincipal(ContextoAcceso contexto, UbicacionDatos ubicacion, int versionEsquema, IBaseDatos bd, InfoEntorno entorno)
    {
        _entorno = entorno;
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
        if (entorno.EsPruebas)
        {
            Title = "PRUEBAS · " + Title;
            FranjaPruebas.Visibility = Visibility.Visible;
            TextoFranjaPruebas.Text = "ENTORNO DE PRUEBAS" + (entorno.CopiaFecha is DateTime f ? $" · copia de producción del {f:dd/MM/yyyy HH:mm}" : "") + " · lo que hagas aquí no afecta a producción";
            TextoDatos.Text = "PRUEBAS · " + TextoDatos.Text;
            TextoDatos.ToolTip = $"{ubicacion.RutaBaseDatos}{Environment.NewLine}Copia de pruebas{(entorno.CopiaOrigen is { } o ? $" de {o}" : "")}.";
        }

        _borrarAviso = new DispatcherTimer(TimeSpan.FromSeconds(12), DispatcherPriority.Background, (_, _) => { TextoAviso.Text = ""; _borrarAviso!.Stop(); }, Dispatcher);
        _borrarAviso.Stop();
        _area = new AreaTrabajo(AreaVistas, Avisar);
        _area.PropertyChanged += (_, _) => RefrescarEstado();
        TiraPestanas.ItemsSource = _area.Lista;
        // Inicio (pantalla-de-inicio): indicadores y gráficos del ejercicio de trabajo; cada indicador abre su pantalla si ya existe.
        _area.AbrirFija(ProConta.Inicio.UI.ModuloInicio.Crear(
            new ProConta.Inicio.ServicioInicio(new ProConta.Inicio.FuenteInicioAccess(bd)),
            new ProConta.Inicio.UI.ContextoInicio(contexto.Contexto.IdSociedad, ejercicio, contexto.RazonSocial, Avisar),
            new ProConta.Inicio.UI.AccionesInicio(
                AbrirFacturas: () => _facturacion!.AbrirListado(),
                AbrirPerdidasYGanancias: () => _ = GenerarBalanceAsync(ProConta.Contabilidad.Balances.TipoBalance.PerdidasYGanancias),
                AbrirBancos: () => _bancos!.Abrir(),
                AbrirMayor: cuentasMayor => _mayor!.Abrir(cuentasMayor),
                AbrirBalanceSituacion: () => _ = GenerarBalanceAsync(ProConta.Contabilidad.Balances.TipoBalance.Situacion))
            {
                // Pantallas donde se resuelve cada elemento de la salud de la contabilidad (fase 3).
                AbrirVentas = () => _facturacion!.AbrirVentas(),
                AbrirLineasN43 = () => _lineasN43!.Abrir(),
                AbrirFacturasCompra = () => _compras!.AbrirRecibidas(),
                AbrirFacturasRecurrentes = () => _compras!.AbrirRecurrentes(),
                AbrirDiario = () => _diario!.Abrir(),
                AbrirLiquidacionIva = () => _liquidacionIva!.Abrir(),
                AbrirAmortizacion = () => _amortizacion!.Abrir(),
                AbrirCentimosResiduales = () => _centimos!.Abrir(),
                AbrirTerceros = () => _terceros!.AbrirBuscador(),
                AbrirNominas = () => _nominas!.Abrir(null),
                AbrirParametros = clave => AbrirParametros(clave),
                // Agenda (fase 4).
                AbrirProgramadas = () => _facturacion!.AbrirProgramadas(),
                // Plazos de los modelos (contabilidad/modelos-tributarios): abren Modelos tributarios en su periodo.
                AbrirModelosTributarios = (modelo, periodo) => _ = AbrirModelosAsync(modelo, periodo),
                // Aviso «Comisiones sin compensar» (facturacion/reservas/compensacion-comision).
                CompensarComisiones = () => CompensarComisionesAsync(bd, new Actor(sesion.NombreOperador)),
            },
            () => this,
            // La salud guarda su recuento diario (HistoricoSalud) para la tendencia «desde el lunes» (fase 4).
            new ProConta.Inicio.Salud.ServicioSalud(new ProConta.Inicio.Salud.FuenteSaludAccess(bd), historico: new ProConta.Inicio.Salud.HistoricoSaludAccess(bd)),
            new ProConta.Inicio.Bloques.ServicioBloques(new ProConta.Inicio.Bloques.FuenteBloquesAccess(bd))));

        var cuentas = new CatalogoCuentasAccess(bd);
        // Ventas, compras y terceros actualizan los textos de los asientos de banco enlazados (norma43/conciliacion/actualizar-textos).
        var asientosBanco = new ProConta.Norma43.Conciliacion.AsientosBancoN43(bd);
        var servicios = new ServiciosTerceros(
            new RepositorioTercerosAccess(bd), new BuscadorTerceros(bd), new CatalogoPerfilesAccess(bd), new ServicioRoles(bd, cuentas), cuentas,
            () => new FuenteGeografiaAccess(bd).CargarAsync(), contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador),
            new SubcuentasRoles(bd, cuentas), asientosBanco,
            // Al cambiar la subcuenta de un rol se ofrece pasar sus apuntes (terceros/roles/cambio-subcuenta).
            new CambioSubcuentaTercero(bd, cuentas, new MotorAsientosAccess(bd), asientosBanco),
            // Mandato SEPA del cliente: el acreedor es la sociedad activa (terceros/roles/mandato-sepa).
            new ProConta.Terceros.Roles.LectorAcreedorSepa(bd));
        _terceros = new ModuloTerceros(servicios, _area, new DialogosTerceros(servicios));

        // Mayor (contabilidad/mayor): pestaña Contabilidad › Mayor y el mismo control embebido en la ficha de terceros.
        _mayor = new ProConta.Contabilidad.UI.Mayor.ModuloMayor(
            new ProConta.Contabilidad.UI.Mayor.ContextoMayor(new ProConta.Contabilidad.Mayor.FuenteMayorAccess(bd), contexto.Contexto.IdSociedad, ejercicio, contexto.RazonSocial,
                new MotorAsientosAccess(bd), new Actor(sesion.NombreOperador)),
            _area, new ProConta.Contabilidad.UI.Mayor.DialogosMayor(() => this));
        _terceros.MayorEmbebido = cuentas => _mayor.CrearEmbebido(cuentas);
        // Mandato SEPA (terceros/roles/mandato-sepa): HTML › PDF con el motor de informes, A4 vertical, y se abre para imprimirlo.
        _terceros.HtmlAPdf = html => ConversorHtmlPdf.Compartido.Convertir(html, new PaginaPdf(210, 297, 18, 18, 18, 18));
        _terceros.AbrirFichero = ruta =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Avisar($"Mandato guardado en {ruta} (no he podido abrirlo: {ex.Message})"); }
        };
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
        _puedeVerModelos = () => autorizador.PuedeAsync(Acciones.ModelosVer);
        _ = RefrescarTrimestreAsync();
        var contabilizador = new ContabilizadorVentas(bd, new MotorAsientosAccess(bd), cuentas, roles, sociedades);
        var facturas = new ServicioFacturas(bd, sociedades, new RepositorioTercerosAccess(bd), roles, cuentas, contabilizador, autorizador);
        var apoyo = new ApoyoEditorFacturas(bd, roles);
        var serviciosFacturacion = new ServiciosFacturacion(facturas, new ConsultaFacturas(bd), new CatalogoConceptos(bd, cuentas), apoyo, new CatalogoFormasPagoAccess(bd),
            new LectorConfiguracionEjercicio(bd), contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), new CatalogoCentrosCosteAccess(bd),
            new ServicioProgramadas(bd, sociedades, facturas, autorizador),
            new ServicioPdfFacturas(bd, facturas, sociedades, new GeneradorPdfFastReport(), autorizador), asientosBanco);
        _facturacion = new ModuloFacturacion(serviciosFacturacion, _area, new DialogosFacturacion(apoyo, cuentas, new CatalogoCentrosCosteAccess(bd), () => this), _terceros.AbrirFicha);
        // Ventas por facturar (facturacion/reservas): reservas de Avaibook y altas manuales que se facturan por mes, cliente y activo.
        var ventas = new ProConta.Facturacion.Ventas.ServicioVentas(bd, sociedades, facturas, serviciosFacturacion.Conceptos, autorizador);
        var clienteAvaibook = new ProConta.Facturacion.Ventas.ClienteAvaibookHttp(Http);
        // Cotizaciones de los activos financieros (activos/activos-financieros 2026-11-23): Yahoo Finance, EODHD con la clave de Integraciones y cambio del BCE.
        var clienteEodhd = new ProConta.Activos.Financieros.ClienteEodhd(Http);
        var clienteYahoo = new ProConta.Activos.Financieros.ClienteYahooFinance(Http);
        var cambiosDivisa = new ProConta.Activos.Financieros.ServicioCambiosDivisa(bd, new ProConta.Activos.Financieros.ClienteBce(Http));
        var cotizaciones = new ProConta.Activos.Financieros.ServicioCotizaciones(clienteYahoo, clienteEodhd, cambiosDivisa,
            () => ProConta.Activos.Mantenimiento.ProveedorConexionCotizaciones.ClaveAsync(bd, contexto.Contexto.IdSociedad));
        _facturacion.Ventas = ventas;
        _facturacion.Avaibook = new ProConta.Facturacion.Ventas.ImportadorAvaibook(bd, ventas, clienteAvaibook, autorizador);
        _facturacion.AbrirParametros = clave => AbrirParametros(clave);

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
            ComprobarTrimestreAlEntrarAsync,
            asientosBanco,
            // Compras › Facturas recurrentes pendientes (compras/facturas-recurrentes-pendientes).
            new ProConta.Compras.Recurrentes.ServicioFacturasRecurrentes(bd));
        _compras = new ModuloCompras(serviciosCompras, _area, new DialogosCompras(() => this), _terceros.AbrirFicha);

        // Gestor documental (documentos 2026-11-18): pantalla Datos maestros › Documentos, «Vincular a…» y el panel Documentos de las fichas (tercero,
        // activo y periodo de un modelo). Cada vínculo abre su registro; una factura de compra que se cuela en la bandeja va a la carpeta de Compras.
        var servicioDocumentos = new ProConta.Documentos.ServicioDocumentos(bd, sesion.NombreOperador, puede: () => autorizador.PuedeAsync(Acciones.Documentos));
        _servicioDocumentos = servicioDocumentos;
        _documentos = new ProConta.Documentos.UI.ModuloDocumentos(
            new ProConta.Documentos.UI.ContextoDocumentos(servicioDocumentos, contexto.Contexto.IdSociedad, contexto.RazonSocial, Avisar)
            {
                AbrirDestino = AbrirDestinoDocumento,
                EnviarACompras = async ruta =>
                {
                    var carpeta = await facturasCompra.CarpetaComprasAsync(contexto.Contexto);
                    if (!carpeta.EsCorrecto) return carpeta.Error!.TextoOperador();
                    try
                    {
                        var destino = ProConta.Documentos.RutasDocumentos.RutaLibre(carpeta.Valor, System.IO.Path.GetFileName(ruta));
                        System.IO.File.Copy(ruta, destino);
                        _compras.AbrirRecibidas();
                        return null;
                    }
                    catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException) { return $"{ex.Message} Copia el PDF a mano a {carpeta.Valor}."; }
                },
            },
            _area, new ProConta.Documentos.UI.Vistas.DialogosDocumentos(() => this));
        _terceros.DocumentosEmbebidos = (id, nombre) => _documentos.CrearPanel(new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Tercero, id, null, nombre));
        var abrirDocumentos = new ComandoDelegado(() => _documentos.Abrir());

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
        var conciliacionN43 = new ProConta.Norma43.Conciliacion.ServicioConciliacionN43(bd);
        _lineasN43 = new ProConta.Norma43.UI.VistaModelos.ModuloLineasN43(new ProConta.Norma43.Lineas.ConsultaLineasN43(bd), new ProConta.Norma43.Lineas.ServicioLineasN43(bd),
            conciliacionN43, new ProConta.Norma43.Asignacion.AsignadorCuentasN43(bd),
            new ProConta.Norma43.UI.VistaModelos.ContextoLineasN43(contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                AbrirMayor = (cuentasMayor, anios) => _mayor.Abrir(cuentasMayor, anios),
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirFacturaVenta = id => _facturacion.AbrirFactura(id),
                AbrirFacturaCompra = id => _compras.AbrirFactura(id),
            },
            _area, new ProConta.Norma43.UI.Vistas.DialogosLineasN43(cuentas, () => this), new ProConta.Norma43.Busqueda.BusquedaPorImporteN43(bd),
            new ProConta.Norma43.Remesas.ServicioRemesasN43(bd),
            new AjustesRemesaFacturacion(new AjustesCobroFacturas(facturas, serviciosFacturacion.Programadas), bd),
            new ProConta.Norma43.Similares.ConsultaSimilaresN43(bd), new ProConta.Norma43.Conciliacion.ServicioActivosN43(bd, conciliacionN43));
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

        // Parámetros y tablas › Ejercicios lleva también «Crear el ejercicio siguiente…» (contabilidad/procesos/nuevo-ejercicio 2026-11-17 15:38).
        var proveedoresOrganizacion = ProveedoresOrganizacion.Todos(bd).ToList();
        if (proveedoresOrganizacion.OfType<ProveedorTablaAccess>().FirstOrDefault(p => p.Clave == "ejercicios") is { } ejerciciosMantenimiento)
            ProConta.Contabilidad.Cierre.AccionCrearEjercicio.Conectar(ejerciciosMantenimiento,
                new ProConta.Contabilidad.Cierre.ServicioNuevoEjercicio(bd, () => autorizador.PuedeAsync(Acciones.CierreEjercicio)));
        var mantenimiento = new ServicioMantenimiento(
            new IProveedorMantenimiento[] { new ProveedorConfiguracionGeneral(bd) }.Concat(proveedoresOrganizacion).Concat(ProveedoresFacturacion.Todos(bd, cuentas)).Concat(ProConta.Facturacion.Ventas.ProveedoresVentas.Todos(bd, ventas, clienteAvaibook))
                .Concat(ProConta.Compras.Mantenimiento.ProveedoresCompras.Todos(bd)).Concat(ProveedoresCorreo.Todos(bd))
                .Concat(ProConta.Activos.Mantenimiento.ProveedoresActivos.Todos(bd, cuentas, clienteEodhd)).Concat(ProConta.Documentos.Mantenimiento.ProveedoresDocumentos.Todos(bd)),
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
        var abrirParametros = new ComandoDelegado(() => AbrirParametros());
        var abrirRecibidas = new ComandoDelegado(_compras.AbrirRecibidas);
        var abrirRecurrentes = new ComandoDelegado(_compras.AbrirRecurrentes);
        var abrirContaSol = new ComandoDelegado(AbrirImportacionContaSol);
        var abrirCorreo = new ComandoDelegado(() => _correo.AbrirRegistro());
        var abrirMayor = new ComandoDelegado(() => _mayor.Abrir());
        // Diario (contabilidad/diario): todos los apuntes del ejercicio de trabajo y del anterior; abre la pantalla de asiento, el Mayor y el
        // origen de los asientos automáticos, y exporta a PDF por el motor de informes (HTML › PDF con WebView2) y a Excel.
        _asientos.ParaEjercicio = ejercicioAsiento => (
            new ProConta.Contabilidad.UI.Asientos.FuenteAsientoManualAccess(bd, ejercicioAsiento, new Actor(sesion.NombreOperador)),
            new ProConta.Contabilidad.UI.Asientos.DialogosAsiento(cuentas, new CatalogoCentrosCosteAccess(bd), ejercicioAsiento, () => this));
        _diario = new ProConta.Contabilidad.UI.Diario.ModuloDiario(
            new ProConta.Contabilidad.UI.Diario.ContextoDiario(new ProConta.Contabilidad.Diario.FuenteDiarioAccess(bd), new MotorAsientosAccess(bd), new CatalogoCentrosCosteAccess(bd),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar),
            _area, new ProConta.Contabilidad.UI.Diario.DialogosDiario(cuentas, new CatalogoCentrosCosteAccess(bd), () => this))
        {
            Asientos = _asientos,
            Mayor = _mayor,
            HtmlAPdf = html => ConversorHtmlPdf.Compartido.Convertir(html, new PaginaPdf(297, 210, 10, 10, 10, 10)),
            AbrirFichero = ruta =>
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true }); }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Avisar($"Fichero guardado en {ruta} (no he podido abrirlo: {ex.Message})"); }
            },
            PorQueNoAbrirOrigen = asiento => OrigenDelDiario(asiento) is null ? PorQueNoSeAbreElOrigen(asiento) : null,
            AbrirOrigen = asiento => OrigenDelDiario(asiento)?.Invoke(),
        };
        var abrirDiario = new ComandoDelegado(() => _diario.Abrir());
        // Liquidación de IVA (contabilidad/procesos/liquidacion-iva): un asiento por trimestre, como lote regenerable; activa el trimestre siguiente.
        _liquidacionIva = new ProConta.Contabilidad.UI.Liquidacion.ModuloLiquidacionIva(
            new ProConta.Contabilidad.UI.Liquidacion.ContextoLiquidacionIva(
                new ProConta.Contabilidad.Iva.ServicioLiquidacionIva(bd, new MotorAsientosAccess(bd), cuentas, () => autorizador.PuedeAsync(Acciones.LiquidacionIva)),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                Cambiada = () => _ = RefrescarTrimestreAsync(),
                AbrirModelo = (modelo, periodo) => _ = AbrirModelosAsync(modelo, periodo),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirLiquidacionIva = new ComandoDelegado(() => _liquidacionIva.Abrir());
        // Modelos tributarios (contabilidad/modelos-tributarios): pantalla única de los modelos de la sociedad con sus plazos, borradores, ficheros,
        // presentación con la pieza común (contabilidad/modelos-tributarios/presentacion) y consulta a la AEAT de lo presentado fuera del programa.
        // Sustituye a la antigua opción «Modelo 303» (decisión del propietario del 2026-11-18 05:34).
        var presentacionAeat = new ProConta.Contabilidad.Presentacion.ServicioPresentacionAeat(bd, new ProConta.Contabilidad.Presentacion.ClienteAeatHttp());
        _modelos = new ProConta.Contabilidad.UI.ModelosTributarios.ModuloModelosTributarios(
            new ProConta.Contabilidad.UI.ModelosTributarios.ContextoModelosTributarios(
                new ProConta.Contabilidad.Modelos.ServicioModelosTributarios(bd, presentacionAeat,
                    new ProConta.Contabilidad.Presentacion.ServicioTgvi(bd, new ProConta.Contabilidad.Presentacion.ClienteTgviHttp()),
                    new ProConta.Contabilidad.Impuestos.ServicioImpuestoSociedades(bd, new MotorAsientosAccess(bd), cuentas, () => autorizador.PuedeAsync(Acciones.ImpuestoSociedades))),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                PuedePresentar = () => autorizador.PuedeAsync(Acciones.ModelosPresentar),
                AbrirFichero = ruta =>
                {
                    try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true })?.Dispose(); }
                    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Avisar($"Fichero guardado en {ruta} (no he podido abrirlo: {ex.Message})"); }
                },
                Copiar = texto => { try { Clipboard.SetText(texto); } catch (System.Runtime.InteropServices.ExternalException) { Avisar("No he podido copiar al portapapeles: vuelve a intentarlo."); } },
                AbrirDocumentos = AbrirDocumentosModelo,
                CarpetaEmpresa = async () => ProConta.Contabilidad.Presentacion.ServicioPresentacionAeat.CarpetaEmpresa(bd.Ruta,
                    await bd.EjecutarAsync(s => s.EscalarAsync("SELECT RutaDocumentos FROM Sociedades WHERE IdSociedad = ?", contexto.Contexto.IdSociedad)) as string),
                AbrirProceso = (proceso, modelo, periodo) =>
                {
                    switch (proceso)
                    {
                        case ProConta.Contabilidad.Modelos.ProcesoModelo.LiquidacionIva:
                            if (periodo is { Length: 2 } && periodo[1] == 'T' && char.IsAsciiDigit(periodo[0])) _liquidacionIva!.Abrir(periodo[0] - '0'); else _liquidacionIva!.Abrir();
                            break;
                        case ProConta.Contabilidad.Modelos.ProcesoModelo.Nominas: _nominas!.Abrir(null); break;
                        case ProConta.Contabilidad.Modelos.ProcesoModelo.ImpuestoSociedades: _impuestoSociedades!.Abrir(); break;
                        case ProConta.Contabilidad.Modelos.ProcesoModelo.Prorrata: _prorrata!.Abrir(); break;
                        case ProConta.Contabilidad.Modelos.ProcesoModelo.AplicacionResultado: _dividendos!.Abrir(); break;
                        case ProConta.Contabilidad.Modelos.ProcesoModelo.FacturasCompra: _compras!.AbrirRecibidas(); break;
                    }
                },
            },
            _area, new ProConta.Contabilidad.UI.ModelosTributarios.DialogosModelosTributarios(() => this));
        var abrirModelos = new ComandoDelegado(() => _ = AbrirModelosAsync(null, null));
        // Prorrata de IVA (contabilidad/procesos/prorrata): definitiva del año (por defecto, la legal) y su regularización el 31/12 en la 4729.
        _prorrata = new ProConta.Contabilidad.UI.Prorrata.ModuloRegularizacionProrrata(
            new ProConta.Contabilidad.UI.Prorrata.ContextoRegularizacionProrrata(
                new ProConta.Contabilidad.Iva.ServicioRegularizacionProrrata(bd, new MotorAsientosAccess(bd), cuentas, () => autorizador.PuedeAsync(Acciones.LiquidacionIva)),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                AbrirLiquidacion4T = () => _liquidacionIva.Abrir(4),
                GenerarInforme = () => _ = GenerarInformeGestoriaAsync(4),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirProrrata = new ComandoDelegado(() => _prorrata.Abrir());
        // Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades): cálculo como la hoja de la gestoría y asiento del 31/12 antes de la regularización.
        _impuestoSociedades = new ProConta.Contabilidad.UI.ImpuestoSociedades.ModuloImpuestoSociedades(
            new ProConta.Contabilidad.UI.ImpuestoSociedades.ContextoImpuestoSociedades(
                new ProConta.Contabilidad.Impuestos.ServicioImpuestoSociedades(bd, new MotorAsientosAccess(bd), cuentas, () => autorizador.PuedeAsync(Acciones.ImpuestoSociedades)),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                ElegirDatosFiscales = () =>
                {
                    var dialogo = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Datos fiscales del Impuesto sobre Sociedades (HTML guardado desde la Sede)",
                        Filter = "Página web (*.htm;*.html)|*.htm;*.html|Todos los ficheros (*.*)|*.*",
                    };
                    return dialogo.ShowDialog(this) == true ? dialogo.FileName : null;
                },
                AbrirEnlace = url => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true })?.Dispose(),
                // Doble clic en un piso del paso «Alquiler de viviendas»: su activo con su cuenta de resultados (activos 2026-11-17 18:50).
                AbrirActivoDeCentro = centro => _ = _activos!.AbrirDeCentroAsync(centro),
                AbrirModelo = (modelo, periodo) => _ = AbrirModelosAsync(modelo, periodo),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirImpuestoSociedades = new ComandoDelegado(() => _impuestoSociedades.Abrir());
        // Reparto de dividendos (contabilidad/procesos/aplicacion-resultado): reservas a la 526 de cada socio con su retención, como lote regenerable,
        // y el acta de la junta en PDF (motor de informes, HTML › PDF con WebView2) en la carpeta de documentos de la empresa.
        var servicioDividendos = new ProConta.Contabilidad.Dividendos.ServicioDividendos(bd, new MotorAsientosAccess(bd), cuentas, () => autorizador.PuedeAsync(Acciones.Dividendos));
        Action<string> abrirFichero = ruta =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true })?.Dispose(); }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Avisar($"Fichero guardado en {ruta} (no he podido abrirlo: {ex.Message})"); }
        };
        _dividendos = new ProConta.Contabilidad.UI.Dividendos.ModuloRepartoDividendos(
            new ProConta.Contabilidad.UI.Dividendos.ContextoRepartoDividendos(servicioDividendos, contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                Actas = new ProConta.Contabilidad.Dividendos.ServicioActaDividendos(bd, servicioDividendos),
                HtmlAPdf = html => ConversorHtmlPdf.Compartido.Convertir(html, new PaginaPdf(210, 297, 22, 22, 20, 22)),
                AbrirFichero = abrirFichero,
            },
            _area, new ProConta.Contabilidad.UI.Dividendos.DialogosRepartoDividendos(() => this));
        var abrirDividendos = new ComandoDelegado(() => _dividendos.Abrir());
        // Aplicación del resultado (contabilidad/procesos/aplicacion-resultado, fase 2): el resultado del ejercicio anterior (129) a la 121, a reserva
        // legal, a dividendos (un reparto con cargo a la 129) y a voluntarias, como lote regenerable.
        _aplicacionResultado = new ProConta.Contabilidad.UI.Dividendos.ModuloAplicacionResultado(
            new ProConta.Contabilidad.UI.Dividendos.ContextoAplicacionResultado(
                new ProConta.Contabilidad.Dividendos.ServicioAplicacionResultado(bd, new MotorAsientosAccess(bd), cuentas, servicioDividendos, () => autorizador.PuedeAsync(Acciones.Dividendos)),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                AbrirRepartos = () => _dividendos.Abrir(),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirAplicacion = new ComandoDelegado(() => _aplicacionResultado.Abrir());
        // Cierre del ejercicio (contabilidad/procesos/cierre-ejercicio): regularización, cierre y apertura del siguiente en una transacción, con la
        // comprobación previa (V5) enlazada a donde se resuelve; reabrir borra solo esos asientos. Crea también el ejercicio siguiente.
        var puedeCerrar = () => autorizador.PuedeAsync(Acciones.CierreEjercicio);
        _cierreEjercicio = new ProConta.Contabilidad.UI.Cierre.ModuloCierreEjercicio(
            new ProConta.Contabilidad.UI.Cierre.ContextoCierreEjercicio(
                new ProConta.Contabilidad.Cierre.ServicioCierreEjercicio(bd, new MotorAsientosAccess(bd), cuentas,
                    new ProConta.Contabilidad.Cierre.ServicioNuevoEjercicio(bd, puedeCerrar), puedeCerrar,
                    avisosGraves: async ctx => (await new ProConta.Inicio.Salud.ServicioSalud(new ProConta.Inicio.Salud.FuenteSaludAccess(bd)).RevisarAsync(ctx.IdSociedad, ctx.Ejercicio))
                        .Elementos.Where(e => e.Tipo == ProConta.Inicio.Salud.TipoSalud.Aviso && e.Gravedad == ProConta.Inicio.Salud.GravedadSalud.Grave).Select(e => e.Titulo).ToList()),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                Destino = destino => destino switch
                {
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.ImpuestoSociedades => () => _impuestoSociedades.Abrir(),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.Amortizacion => () => _amortizacion!.Abrir(),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.LiquidacionIva => () => _liquidacionIva.Abrir(4),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.Prorrata => () => _prorrata.Abrir(),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.FacturasPendientes => () => _facturasPendientes!.Abrir(),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.CentimosResiduales => () => _centimos!.Abrir(),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.AplicacionResultado => () => _aplicacionResultado.Abrir(),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.Diario => () => _diario.Abrir(),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.Salud => () => irAInicio.Execute(null),
                    ProConta.Contabilidad.Cierre.DestinoComprobacion.ModelosTributarios => () => _ = AbrirModelosAsync("390", "0A"),
                    _ => null,
                },
                Cambiada = () => _ = RefrescarTrimestreAsync(),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirCierreEjercicio = new ComandoDelegado(() => _cierreEjercicio.Abrir());
        // Cuentas anuales (contabilidad/procesos/cuentas-anuales): balance y pérdidas y ganancias del modelo pymes del Registro Mercantil con el ejercicio
        // anterior, sus comprobaciones y el PDF de las páginas del modelo. Solo lee.
        _cuentasAnuales = new ProConta.Contabilidad.UI.CuentasAnuales.ModuloCuentasAnuales(
            new ProConta.Contabilidad.UI.CuentasAnuales.ContextoCuentasAnuales(
                new ProConta.Contabilidad.CuentasAnuales.ServicioCuentasAnuales(bd), contexto.Contexto, contexto.RazonSocial, sesion.NombreOperador, Avisar)
            {
                GenerarPdf = GenerarCuentasAnualesPdfAsync,
                ExportarD2 = GenerarDepositoD2Async,
                AbrirMayor = (cuentasMayor, anio) => _mayor.Abrir(cuentasMayor, [anio]),
                AbrirCierre = () => _cierreEjercicio.Abrir(),
            },
            _area);
        var abrirCuentasAnuales = new ComandoDelegado(() => _cuentasAnuales.Abrir());
        // Legalización de libros (contabilidad/procesos/legalizacion-libros): DIARIO_001.pdf e INV_CUEN_001.pdf del ejercicio cerrado para el Registro
        // Mercantil, con sus huellas e instrucciones; toma el Diario, sumas y saldos y las cuentas anuales sin recalcular. Solo lee.
        _legalizacion = new ProConta.Contabilidad.UI.Legalizacion.ModuloLegalizacion(
            new ProConta.Contabilidad.UI.Legalizacion.ContextoLegalizacion(
                new ProConta.Contabilidad.Legalizacion.ServicioLegalizacion(bd, async ctx =>
                    (await new ProConta.Activos.Gestion.ServicioActivos(bd, cuentas).ListarAsync(ctx))
                        .Where(a => !a.EsAjeno && (a.FechaBaja is null || a.FechaBaja.Value.Year >= ctx.Ejercicio))
                        .Select(a => new ProConta.Contabilidad.Legalizacion.ActivoInventario(a.Codigo, a.Nombre, a.Tipo, a.FechaCompra, a.Coste, a.Amortizado)).ToList()),
                contexto.Contexto, contexto.RazonSocial, Avisar)
            {
                Generar = GenerarLibrosLegalizacionAsync,
                Abrir = ruta => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true }),
                AbrirCierre = () => _cierreEjercicio.Abrir(),
                AbrirCuentasAnuales = () => _cuentasAnuales.Abrir(),
            },
            _area);
        var abrirLegalizacion = new ComandoDelegado(() => _legalizacion.Abrir());
        // Facturas pendientes de recibir (contabilidad/procesos/facturas-pendientes-recibir): gasto supuesto de los proveedores cuadrados con saldo deudor contra la
        // 4009 a 31/12 y su reversión el 1/1 del siguiente, como dos lotes propios; el cierre solo mira su estado.
        _facturasPendientes = new ProConta.Contabilidad.UI.FacturasPendientes.ModuloFacturasPendientes(
            new ProConta.Contabilidad.UI.FacturasPendientes.ContextoFacturasPendientes(
                new ProConta.Contabilidad.FacturasPendientes.ServicioFacturasPendientes(bd, new MotorAsientosAccess(bd), cuentas, () => autorizador.PuedeAsync(Acciones.FacturasPendientes)),
                new ProConta.Contabilidad.Cierre.ServicioNuevoEjercicio(bd, puedeCerrar),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                Cambiada = () => _ = RefrescarTrimestreAsync(),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirFacturasPendientes = new ComandoDelegado(() => _facturasPendientes.Abrir());
        // Amortización de activos (contabilidad/procesos/amortizacion-de-activos): cuota de cada activo por días reales con tope en lo pendiente, un asiento por
        // periodo (anual, trimestral o mensual según el ejercicio) como lote propio; la ficha de activos solo consulta.
        _amortizacion = new ProConta.Contabilidad.UI.Amortizacion.ModuloAmortizacion(
            new ProConta.Contabilidad.UI.Amortizacion.ContextoAmortizacion(
                new ProConta.Contabilidad.Amortizacion.ServicioAmortizacion(bd, new MotorAsientosAccess(bd), () => autorizador.PuedeAsync(Acciones.Amortizacion)),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                AbrirParametros = clave => AbrirParametros(clave),
                Cambiada = () => _ = RefrescarTrimestreAsync(),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirAmortizacion = new ComandoDelegado(() => _amortizacion.Abrir());
        // Recalcular saldos (contabilidad/procesos/recalcular-saldos): compara la caché de saldos de las cuentas del ejercicio con sus apuntes y la rehace;
        // no genera asientos, así que no pide permiso propio.
        _recalcularSaldos = new ProConta.Contabilidad.UI.Saldos.ModuloRecalcularSaldos(
            new ProConta.Contabilidad.UI.Saldos.ContextoRecalcularSaldos(
                new ProConta.Contabilidad.Saldos.ServicioRecalcularSaldos(bd, new MotorAsientosAccess(bd)), contexto.Contexto, contexto.RazonSocial, Avisar)
            {
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
            },
            _area);
        var abrirRecalcularSaldos = new ComandoDelegado(() => _recalcularSaldos.Abrir());
        // Reiniciar el cuadre del Mayor (contabilidad/procesos/reiniciar-cuadre): en el ejercicio de trabajo, cuadradas las subcuentas con saldo dentro del umbral
        // y sin la casilla las demás, sin tocar las notas ni hacer asientos; refresca el Mayor abierto.
        _reinicioCuadre = new ProConta.Contabilidad.UI.ReinicioCuadre.ModuloReinicioCuadre(
            new ProConta.Contabilidad.UI.ReinicioCuadre.ContextoReinicioCuadre(new ProConta.Contabilidad.Mayor.FuenteMayorAccess(bd), contexto.Contexto.IdSociedad, ejercicio,
                contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                AbrirParametros = clave => AbrirParametros(clave),
                Cambiada = () => _mayor.RefrescarSiAbierto(),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirReinicioCuadre = new ComandoDelegado(() => _reinicioCuadre.Abrir());
        // Céntimos residuales (contabilidad/procesos/centimos-residuales): saldos de menos del umbral en las subcuentas de los grupos 1 a 5 (sin 47 ni 57),
        // saldados a 31/12 contra las cuentas de redondeo, un asiento por subcuenta, como lote propio; el cierre solo mira su estado.
        _centimos = new ProConta.Contabilidad.UI.Centimos.ModuloCentimos(
            new ProConta.Contabilidad.UI.Centimos.ContextoCentimos(
                new ProConta.Contabilidad.Centimos.ServicioCentimos(bd, new MotorAsientosAccess(bd), cuentas, () => autorizador.PuedeAsync(Acciones.CentimosResiduales)),
                contexto.Contexto, contexto.RazonSocial, new Actor(sesion.NombreOperador), Avisar)
            {
                VerAsiento = id => _mayor.VerAsientoAsync(id),
                AbrirMayor = cuentasMayor => _mayor.Abrir(cuentasMayor),
                AbrirParametros = clave => AbrirParametros(clave),
                Cambiada = () => _ = RefrescarTrimestreAsync(),
            },
            _area, new ProConta.Contabilidad.UI.Liquidacion.DialogosLiquidacionIva(() => this));
        var abrirCentimos = new ComandoDelegado(() => _centimos.Abrir());
        // Activos (activos 2026-11-17 18:50): listado, fichas en pestañas y alta paso a paso, en Datos maestros; abre el Mayor y la amortización.
        var servicioActivos = new ProConta.Activos.Gestion.ServicioActivos(bd, cuentas, () => autorizador.PuedeAsync(Acciones.Activos));
        _activos = new ProConta.Activos.UI.ModuloActivos(
            new ProConta.Activos.UI.ContextoActivos(servicioActivos, contexto.Contexto, contexto.RazonSocial, Avisar)
            {
                AbrirMayor = (cuentasMayor, anios) => { if (anios.Count == 0) _mayor.Abrir(cuentasMayor); else _mayor.Abrir(cuentasMayor, anios); },
                AbrirAmortizacion = () => _amortizacion.Abrir(),
                AbrirFacturaCompra = id => _compras.AbrirFactura(id),
                AbrirFacturaVenta = id => _facturacion.AbrirFactura(id),
                AbrirProgramada = id => _facturacion.AbrirProgramada(id),
                CrearPanelDocumentos = (id, nombre) => _documentos.CrearPanel(new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Activo, id, null, nombre)),
                Cotizaciones = cotizaciones,
                // Análisis de los activos financieros (activos/activos-financieros 2026-11-23): histórico de Yahoo, precio del día de EODHD y cambio del BCE.
                Cartera = new ProConta.Activos.Gestion.ServicioCarteraFinanciera(bd, servicioActivos, cotizaciones, clienteYahoo, cambiosDivisa),
            },
            _area, new ProConta.Activos.UI.Vistas.DialogosActivos(() => this));
        var abrirActivos = new ComandoDelegado(() => _activos.AbrirListado());
        var abrirCartera = new ComandoDelegado(() => _activos.AbrirCartera());
        // Informes (contabilidad/balances e informes/excel): cada opción genera el libro Excel con su tabla dinámica y lo abre.
        _balances = new ProConta.Contabilidad.Balances.GeneradorBalances(bd);
        _razonSocial = contexto.RazonSocial;
        var balanceSituacion = new ComandoDelegado(() => _ = GenerarBalanceAsync(ProConta.Contabilidad.Balances.TipoBalance.Situacion));
        var balancePyG = new ComandoDelegado(() => _ = GenerarBalanceAsync(ProConta.Contabilidad.Balances.TipoBalance.PerdidasYGanancias));
        _gestoria = new ProConta.Inicio.Gestoria.ComposicionInformeGestoria(bd);
        var informeGestoria = new ComandoDelegado(() => _ = GenerarInformeGestoriaAsync());
        _sumasYSaldos = new ProConta.Contabilidad.Balances.GeneradorSumasYSaldos(bd);
        var sumasYSaldos = new ComandoDelegado(() => _ = GenerarSumasYSaldosAsync());
        _librosIva = new ProConta.Contabilidad.Iva.GeneradorLibrosIva(bd);
        var librosIva = new ComandoDelegado(() => _ = GenerarLibrosIvaAsync());
        _rutaBd = ubicacion.RutaBaseDatos;
        _operador = sesion.NombreOperador;
        var abrirClaude = new ComandoDelegado(AbrirClaude);
        var opciones = new[]
        {
            new OpcionNavegacion("Inicio", Icono("layout"), true, false, "Página de inicio", irAInicio),
            new OpcionNavegacion("Datos maestros", Icono("user"), true, false, "Terceros (clientes, proveedores, contactos…), activos de la sociedad (inmuebles, vehículos, instalaciones…) y documentos", abrirTerceros),
            new OpcionNavegacion("Terceros", null, true, false, "Buscador y fichas de terceros (clientes, proveedores, contactos…)", abrirTerceros, EsHija: true),
            new OpcionNavegacion("Activos", null, true, false,
                "Inmuebles, vehículos, instalaciones y aplicaciones de la sociedad: listado con sus cifras del año, ficha con su cuenta de resultados, subcuentas y amortización, y alta paso a paso",
                abrirActivos, EsHija: true),
            new OpcionNavegacion("Activos financieros", null, true, false,
                "Análisis de los ETFs, fondos y participaciones: valor de la cartera, revalorización, comparativa, rentabilidad por activo y proyección, con las cotizaciones al día",
                abrirCartera, EsHija: true),
            new OpcionNavegacion("Documentos", null, true, false,
                "Gestor documental de la sociedad: añade ficheros (o arrástralos) a «Por clasificar», revisa lo que el programa propone (tipo, fecha, título y vínculo), " +
                "archívalos de una vez y vincúlalos a modelos, ejercicios, terceros, activos, facturas o asientos; vista previa, versiones y búsqueda",
                abrirDocumentos, EsHija: true),
            new OpcionNavegacion("Ventas", Icono("doc"), true, false, "Facturas de venta: emitidas y borradores", abrirFacturas),
            new OpcionNavegacion("Facturas programadas", null, true, false, "Facturas que se generan solas en cada vencimiento: su programación, sus líneas y la generación por lotes", abrirProgramadas, EsHija: true),
            new OpcionNavegacion("Facturas", null, true, false, "Listado de facturas y borradores del ejercicio, con su detalle y acciones", abrirFacturas, EsHija: true),
            new OpcionNavegacion("Ventas por facturar", null, true, false, "Reservas de Avaibook y otras ventas registradas antes de facturarlas: importación, ficha y «Facturar ventas…» por mes, cliente y activo", abrirVentas, EsHija: true),
            new OpcionNavegacion("Compras", Icono("scan"), true, false, "Facturas de compra: los PDF de la carpeta de proveedores, su revisión y el modo prueba del reconocimiento", abrirRecibidas),
            new OpcionNavegacion("Facturas recibidas", null, true, false, "Facturas de compra del ejercicio reconocidas de sus PDF: revisión, corrección y registro (y el modo prueba, en el menú contextual)", abrirRecibidas, EsHija: true),
            new OpcionNavegacion("Facturas recurrentes pendientes", null, true, false,
                "Facturas de proveedores mensuales, bimestrales o trimestrales que deberían haber llegado y no están registradas, con el importe de la última; «No llegará» las descarta. Solo para revisar: no genera asientos",
                abrirRecurrentes, EsHija: true),
            new OpcionNavegacion("Correo", Icono("mail"), true, false, "Correo enviado por ProContaVF: estado de cada mensaje, contenido, adjuntos e intentos", abrirCorreo),
            new OpcionNavegacion("Correo enviado", null, true, false, "Registro común del correo enviado (facturas y demás), con reintentos y reenvíos en el menú contextual", abrirCorreo, EsHija: true),
            new OpcionNavegacion("Correo recibido", null, true, false, "Correo que llega a la cuenta del ejercicio: facturas de compra importadas, documentos en espera y remitentes no autorizados", new ComandoDelegado(() => _correo.AbrirRecibido()), EsHija: true),
            new OpcionNavegacion("Contabilidad", Icono("book"), true, false, "Contabilidad: Mayor, Diario, liquidación de IVA, modelos tributarios, procesos contables, fin de ejercicio y asientos manuales", abrirMayor),
            new OpcionNavegacion("Mayor", null, true, false,
                "Mayor de una o varias cuentas (o de un código corto) en los ejercicios que elijas: vistas Normal, Compacto, Solo pendientes y Agrupar 700, filtros, asiento e historial de cada apunte",
                abrirMayor, EsHija: true),
            new OpcionNavegacion("Diario", null, true, false,
                "Todos los apuntes del ejercicio, por asiento o en lista: filtros, sumas, ficha del asiento con edición rápida, asientos borrados y exportación a PDF o Excel",
                abrirDiario, EsHija: true),
            new OpcionNavegacion("Liquidación de IVA", null, true, false,
                "Procesos contables › Liquidación de IVA: un asiento por trimestre que salda las 472 y 477, lleva lo no deducible a la 6341 y el resultado a la 4750 o la 4700; importe ajustable a lo presentado y activación del trimestre siguiente",
                abrirLiquidacionIva, EsHija: true),
            new OpcionNavegacion("Modelos tributarios", null, true, false,
                "Modelos tributarios: los modelos de la sociedad (303, 111, 115, 123, 349, 202, 390, 190, 180, 193, 347 y 200) con sus plazos y su estado; borrador de la AEAT, fichero, " +
                "«Presentar…» con el certificado de la sociedad (en la copia de pruebas, en el entorno de pruebas de la AEAT), complementarias, justificantes y «Consultar a la AEAT»",
                abrirModelos, EsHija: true),
            new OpcionNavegacion("Reparto de dividendos", null, true, false,
                "Procesos contables › Reparto de dividendos: del neto de cada socio (por defecto, lo ya pagado en su 526) calcula el bruto y la retención del ejercicio y graba el asiento de reservas a dividendos; corregir o deshacer con motivo",
                abrirDividendos, EsHija: true),
            new OpcionNavegacion("Reiniciar el cuadre del Mayor", null, true, false,
                "Procesos contables › Reiniciar el cuadre del Mayor: en el ejercicio de trabajo, marca como cuadradas las subcuentas con saldo dentro del umbral (Configuración general, vacío 0,05 €) " +
                "y quita la casilla a las demás; enseña antes lo que cambia, conserva las notas y no hace asientos",
                abrirReinicioCuadre, EsHija: true),
            new OpcionNavegacion("Fin de ejercicio", null, true, false,
                "Procesos contables que se hacen al terminar el año, en este orden: amortización, facturas pendientes de recibir, céntimos residuales, prorrata, liquidación del IVA del 4T, Impuesto sobre Sociedades y cierre; y, en el ejercicio siguiente, la aplicación del resultado, las cuentas anuales y la legalización de los libros. " +
                "El Cierre del ejercicio tiene una comprobación previa que dice qué falta de cada paso",
                abrirCierreEjercicio, EsHija: true),
            new OpcionNavegacion("Amortización de activos", null, true, false,
                "Procesos contables › Fin de ejercicio › Amortización de activos: cuota de cada activo según su plan (días reales, tope en lo pendiente, descontando lo ya contabilizado), " +
                "un asiento por periodo (68 contra 28, con el centro del activo) con casilla para excluir activos; repetir o deshacer con motivo",
                abrirAmortizacion, EsNieta: true),
            new OpcionNavegacion("Facturas pendientes de recibir", null, true, false,
                "Procesos contables › Fin de ejercicio › Facturas pendientes de recibir: proveedores y acreedores cuadrados en el Mayor con saldo deudor a 31/12 (pagado sin factura), su base supuesta y cuenta de gasto; " +
                "provisión a 31/12 contra la 4009 y su reversión el 1/1 del ejercicio siguiente, con casilla para excluir y base editable",
                abrirFacturasPendientes, EsNieta: true),
            new OpcionNavegacion("Céntimos residuales", null, true, false,
                "Procesos contables › Fin de ejercicio › Céntimos residuales: subcuentas de los grupos 1 a 5 (salvo 47 y 57) con un saldo a 31/12 menor que el umbral del ejercicio, " +
                "saldadas contra las cuentas de redondeo (659 y 759) con un asiento por subcuenta y casilla para excluir; repetir o deshacer con motivo",
                abrirCentimos, EsNieta: true),
            new OpcionNavegacion("Prorrata de IVA", null, true, false,
                "Procesos contables › Fin de ejercicio › Prorrata de IVA: provisional, legal del año y definitiva (la de la gestoría), su regularización por trimestre y el asiento del 31/12 a la 4729 que deduce el 303 del 4T",
                abrirProrrata, EsNieta: true),
            new OpcionNavegacion("Liquidación de IVA (4T)", null, true, false,
                "Procesos contables › Fin de ejercicio › Liquidación de IVA del cuarto trimestre: la misma pantalla de la liquidación, abierta en el 4T, después de regularizar la prorrata",
                new ComandoDelegado(() => _liquidacionIva.Abrir(4)), EsNieta: true),
            new OpcionNavegacion("Impuesto sobre Sociedades", null, true, false,
                "Procesos contables › Fin de ejercicio › Impuesto sobre Sociedades: resultado contable, ajustes que confirma la gestoría, cuota por tramos con la bonificación por alquiler de viviendas, retenciones de las 473 y el asiento del 31/12 (6300 contra 473 y 4752 o 4709) antes de la regularización",
                abrirImpuestoSociedades, EsNieta: true),
            new OpcionNavegacion("Cierre del ejercicio", null, true, false,
                "Procesos contables › Fin de ejercicio › Cierre del ejercicio: comprobación previa, regularización de los grupos 6 y 7 a la cuenta de resultados, cierre de los grupos 1 a 5 y apertura del ejercicio siguiente, grabados de una vez con la marca de cerrado; reabrir con motivo y crear el ejercicio siguiente",
                abrirCierreEjercicio, EsNieta: true),
            new OpcionNavegacion("Aplicación del resultado", null, true, false,
                "Procesos contables › Fin de ejercicio › Aplicación del resultado: en el ejercicio siguiente al cerrado, el resultado que la apertura deja en la 129 " +
                "a la 121 si es pérdida o, si es beneficio, a compensar pérdidas, a reserva legal (el mínimo de la ley), a dividendos (un reparto con cargo a la 129) y el resto a voluntarias; repetir o deshacer con motivo",
                abrirAplicacion, EsNieta: true),
            new OpcionNavegacion("Cuentas anuales", null, true, false,
                "Procesos contables › Fin de ejercicio › Cuentas anuales: balance y pérdidas y ganancias del modelo pymes del Registro Mercantil, con el ejercicio anterior y la clave oficial de cada partida, " +
                "las comprobaciones del Registro (activo igual a patrimonio neto y pasivo, mismo resultado, subcuentas sin partida), las subcuentas de cada partida y el PDF de las páginas del modelo",
                abrirCuentasAnuales, EsNieta: true),
            new OpcionNavegacion("Legalización de libros", null, true, false,
                "Procesos contables › Fin de ejercicio › Legalización de libros: genera DIARIO_001.pdf (libro Diario con suma y sigue) e INV_CUEN_001.pdf (balance inicial, sumas y saldos " +
                "trimestrales, inventario de cierre y cuentas anuales) del ejercicio cerrado, con sus huellas SHA-256, y explica cómo presentarlos en el Registro Mercantil antes del 30 de abril",
                abrirLegalizacion, EsNieta: true),
            new OpcionNavegacion("Recalcular saldos", null, true, false,
                "Procesos contables › Recalcular saldos: compara el saldo y el número de apuntes guardados de cada cuenta del ejercicio con sus apuntes, lista las diferencias " +
                "y los códigos con apuntes que no están en el plan, y «Recalcular» los rehace. No crea ni cambia asientos",
                abrirRecalcularSaldos, EsHija: true),
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
            new OpcionNavegacion("Informes", Icono("xls"), true, false, "Informes del ejercicio de trabajo en Excel y en PDF", balanceSituacion),
            new OpcionNavegacion("Balance de situación", null, true, false,
                "Genera en Excel el balance de situación del ejercicio de trabajo (PGC Pymes 2008): tabla dinámica por bloque, apartado, título, cuenta y subcuenta, con los meses en columnas y filtros por centro de coste y departamento, y lo abre",
                balanceSituacion, EsHija: true),
            new OpcionNavegacion("Pérdidas y ganancias", null, true, false,
                "Genera en Excel la cuenta de pérdidas y ganancias del ejercicio de trabajo (PGC Pymes 2008), sin la regularización ni el cierre: tabla dinámica por apartado, título, cuenta y subcuenta, con los meses en columnas y filtros por centro de coste y departamento, y la abre",
                balancePyG, EsHija: true),
            new OpcionNavegacion("Sumas y saldos…", null, true, false,
                "Genera en PDF o en Excel el balance de sumas y saldos del ejercicio de trabajo entre dos fechas: sumas del Debe y del Haber y saldo deudor o acreedor " +
                "por grupo, subgrupo, cuenta y subcuenta (eliges los niveles). Lo guarda en la carpeta de documentos de la empresa, vinculado al ejercicio, y lo abre",
                sumasYSaldos, EsHija: true),
            new OpcionNavegacion("Libros registro de IVA…", null, true, false,
                "Genera en PDF o en Excel los libros registro de facturas expedidas y de facturas recibidas de un trimestre, un mes o entre fechas del ejercicio de trabajo, " +
                "con los totales por tipo y el cuadre con el 303 del periodo. Lo guarda en la carpeta de documentos de la empresa, vinculado al ejercicio, y lo abre",
                librosIva, EsHija: true),
            new OpcionNavegacion("Informe para la gestoría…", null, true, false,
                "Genera en Excel el informe trimestral para la gestoría del ejercicio de trabajo: liquidación del IVA (303) por trimestres, prorrata y su regularización estimada, " +
                "controles antes de presentar, pérdidas y ganancias, balance, ratios y facturas emitidas y recibidas. Pregunta el trimestre (por defecto, el último terminado) y lo abre",
                informeGestoria, EsHija: true),
            // IA (plataforma/conector-claude): Claude Desktop con el conector de ProContaVF sobre la BD abierta.
            new OpcionNavegacion("IA", Icono("wand"), true, false, "Claude Desktop con el conector de ProContaVF: consultas y propuestas de asientos sobre esta base de datos", abrirClaude),
            new OpcionNavegacion("Abrir Claude", null, true, false,
                "Comprueba que Claude Desktop tiene el conector de ProContaVF configurado con esta misma base de datos (si no, lo configura con tu permiso) y abre Claude Desktop",
                abrirClaude, EsHija: true),
            new OpcionNavegacion("Configuración", Icono("gear"), true, false, "Parámetros y tablas: empresas, ejercicios, series, conceptos, formas de pago…", abrirParametros),
            new OpcionNavegacion("Parámetros y tablas", null, true, false, "Mantenimiento de las tablas y los parámetros de la aplicación, con buscador", abrirParametros, EsHija: true),
            new OpcionNavegacion("Importar ContaSol", null, true, false, "Importa la contabilidad de ContaSol por ejercicios (una BD por ejercicio), con simulación previa", abrirContaSol, EsHija: true),
            // Registro de errores (plataforma/registro-errores): consulta de los errores inesperados guardados en este equipo.
            new OpcionNavegacion("Registro de errores", null, true, false,
                "Errores inesperados de ProContaVF y del conector de Claude Desktop guardados en este equipo (últimos 12 meses), con su detalle técnico para enviarlo a soporte",
                new ComandoDelegado(AbrirRegistroErrores), EsHija: true),
        }.Concat(entorno.EsPruebas ? [] : new[]
        {
            // Entorno de pruebas (plataforma/entorno-pruebas): el refresco se lanza desde producción.
            new OpcionNavegacion("Actualizar la copia de pruebas…", null, true, false,
                "Sustituye la copia de pruebas por una imagen de producción tal como está ahora (la BD y sus carpetas), con el correo desviado y sin VeriFactu. En producción no cambia nada",
                new ComandoDelegado(() => _ = ActualizarCopiaPruebasAsync()), EsHija: true),
        }).ToArray();
        // Grupos con más de tres opciones: plegados al entrar, se despliegan con un clic en su título (2026-11-23).
        ProConta.UI.Base.Navegacion.MenuPlegable.Organizar(opciones);
        foreach (var o in opciones.Where(o => o.EsPlegable))
            o.ConectarPlegado(() => ProConta.UI.Base.Navegacion.MenuPlegable.Alternar(opciones, o));
        Navegacion.ItemsSource = opciones;
        RefrescarEstado();
    }

    public ICommand AlternarNavegacionCommand { get; }
    public ICommand GuardarActivaCommand { get; }
    public ICommand CerrarActivaCommand { get; }

    /// <summary>Abre (o activa) la pestaña Parámetros y tablas.</summary>
    /// <param name="clave">Tabla que se abre dentro (por ejemplo «reglasventa»); null, la primera o la que estuviera.</param>
    private void AbrirParametros(string? clave = null)
    {
        if (_mantenimiento is not null && _area.Pestanas.Contains(_mantenimiento))
        {
            _area.Activar(_mantenimiento);
            if (clave is not null) _ = _mantenimiento.IrAAsync(clave);
            return;
        }
        _mantenimiento = _crearMantenimiento();
        _area.Abrir(_mantenimiento);
        _ = _mantenimiento.IniciarAsync(clave);
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
    private readonly ProConta.Inicio.Gestoria.ComposicionInformeGestoria _gestoria;
    private bool _generandoGestoria;
    private readonly string _razonSocial;
    private bool _generandoBalance;
    private readonly ProConta.Contabilidad.Balances.GeneradorSumasYSaldos _sumasYSaldos;
    private bool _generandoSumasYSaldos;
    private readonly ProConta.Contabilidad.Iva.GeneradorLibrosIva _librosIva;
    private bool _generandoLibrosIva;

    /// <summary>
    /// Genera el balance del ejercicio de trabajo, lo guarda en Documentos\ProContaVF\Informes y lo abre con Excel. En la barra de
    /// estado dice los totales (activo y patrimonio neto y pasivo, o el resultado) y si hay cuentas sin clasificar.
    /// </summary>
    /// <summary>
    /// Cómo abrir el documento que generó un asiento automático desde el Diario, o null si su pantalla aún no sabe abrir uno concreto:
    /// la factura de venta (la clave del lote es su IdFactura) y la importación de nóminas (la clave empieza por su Id).
    /// </summary>
    private Action? OrigenDelDiario(ProConta.Contabilidad.Diario.AsientoDiario asiento)
    {
        var clave = (asiento.ClaveEjecucion ?? "").Split('.')[0];
        if (!int.TryParse(clave, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0) return null;
        return asiento.MotorPropietario switch
        {
            AsientoVenta.MotorPropietario => () => _facturacion.AbrirFactura(id),
            ProConta.Nominas.Contabilizacion.ServicioContabilizacionNominas.MotorPropietario => () => _nominas.Abrir(id),
            _ => null,
        };
    }

    private static string PorQueNoSeAbreElOrigen(ProConta.Contabilidad.Diario.AsientoDiario asiento) => asiento.MotorPropietario switch
    {
        "compras/factura-compra" => $"Ábrela en Compras › Facturas recibidas (registro {asiento.ClaveEjecucion}): esa pantalla aún no abre una factura concreta desde aquí",
        "norma43/conciliacion" => $"Búscala en Bancos › Líneas N43 (línea {asiento.ClaveEjecucion}): esa pantalla aún no abre una línea concreta desde aquí",
        OpcionesLote.MotorImportadorContaSol => "Viene de la importación de ContaSol: no tiene documento de origen en ProContaVF",
        _ => "Este origen no tiene una pantalla que abrir",
    };

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

    /// <summary>
    /// Informe para la gestoría (informes/informe-gestoria): pide el trimestre (por defecto, el último terminado del ejercicio de
    /// trabajo), compone el informe, lo guarda en Documentos\ProContaVF\Informes y lo abre con Excel. En la barra de estado dice el
    /// resultado del 303, la prorrata y cuántos controles hay que revisar.
    /// </summary>
    private readonly Func<Task<bool>> _puedeVerModelos;

    /// <summary>Abre Contabilidad › Modelos tributarios (permiso MODELOS.VER), con un periodo marcado si se da (enlaces de otras pantallas e Inicio).</summary>
    /// <summary>Abre el registro de un vínculo de un documento (documentos 2026-11-18); false si no tiene pantalla que abrir.</summary>
    private bool AbrirDestinoDocumento(ProConta.Documentos.Vinculo v)
    {
        switch (v.TipoEntidad)
        {
            case ProConta.Documentos.TiposEntidad.Tercero when v.IdEntidad is int id: _terceros.AbrirFicha(id, ProConta.Terceros.UI.Logica.PestanaFicha.Documentos); return true;
            case ProConta.Documentos.TiposEntidad.Activo when v.IdEntidad is int id: _activos.AbrirFicha(id, ProConta.Activos.UI.ApartadoFicha.Documentos); return true;
            case ProConta.Documentos.TiposEntidad.FacturaVenta when v.IdEntidad is int id: _facturacion.AbrirFactura(id); return true;
            case ProConta.Documentos.TiposEntidad.FacturaCompra when v.IdEntidad is int id: _compras.AbrirFactura(id); return true;
            case ProConta.Documentos.TiposEntidad.Asiento when v.IdEntidad is int id: _ = _mayor.VerAsientoAsync(id); return true;
            case ProConta.Documentos.TiposEntidad.Modelo when v.Clave?.Split('|') is [var modelo, var anio, var periodo]:
                if (anio != _ejercicioTrabajo.Ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    Avisar($"El {v.Etiqueta} es de otro ejercicio: en Modelos tributarios se ve el de trabajo ({_ejercicioTrabajo.Ejercicio}).");
                _ = AbrirModelosAsync(modelo, periodo);
                return true;
            case ProConta.Documentos.TiposEntidad.Ejercicio:
                AbrirParametros("ejercicios");
                return true;
            case ProConta.Documentos.TiposEntidad.Empresa:
                AbrirParametros("sociedades");
                return true;
            default: return false;
        }
    }

    /// <summary>Documentos de un periodo de un modelo: el panel del gestor documental en una ventana (desde el menú de Modelos tributarios).</summary>
    private void AbrirDocumentosModelo(string modelo, int ejercicio, string periodo)
    {
        var destino = ProConta.Documentos.Vinculo.Modelo(modelo, ejercicio, periodo);
        var d = new DialogoProConta { Title = $"Documentos del {destino.Etiqueta}", Owner = this, Width = 900, Height = 560, Redimensionable = true };
        d.Content = _documentos.CrearPanel(destino);
        var cerrar = new System.Windows.Controls.Button { Content = "Cerrar", IsCancel = true, IsDefault = true, ToolTip = "Cierra esta ventana" };
        cerrar.Click += (_, _) => d.Close();
        d.Botones = cerrar;
        d.ShowDialog();
    }

    private async Task AbrirModelosAsync(string? modelo, string? periodo)
    {
        if (!await _puedeVerModelos())
        {
            Avisar("No tienes permiso para ver los modelos tributarios (MODELOS.VER): pídeselo a quien administra la seguridad.");
            return;
        }
        _modelos.Abrir(modelo, periodo);
    }

    /// <summary>
    /// Informes › Sumas y saldos… (contabilidad/balances/sumas-y-saldos): pide fechas, niveles y formato, calcula el balance del ejercicio de trabajo,
    /// lo guarda por el gestor documental (tipo INFORME_CONTABLE, en «&lt;año&gt;\Informes», vinculado al ejercicio) y abre la copia guardada.
    /// En la barra de estado dice las sumas y los saldos totales.
    /// </summary>
    private async Task GenerarSumasYSaldosAsync(ProConta.Contabilidad.Balances.ParametrosSumasYSaldos? repetir = null,
        ProConta.Contabilidad.UI.Balances.FormatoSumasYSaldos formatoRepetir = default)
    {
        if (_generandoSumasYSaldos) { Avisar("Ya estoy generando el balance de sumas y saldos: espera a que se abra."); return; }
        var ejercicio = _ejercicioTrabajo.Ejercicio;
        var (parametros, formato) = repetir is not null ? (repetir, formatoRepetir)
            : ProConta.Contabilidad.UI.Balances.DialogoSumasYSaldos.Pedir(this, ejercicio, _razonSocial, DateTime.Today) is { } elegido ? elegido : default;
        if (parametros is null) return;
        var pdf = formato == ProConta.Contabilidad.UI.Balances.FormatoSumasYSaldos.Pdf;
        _generandoSumasYSaldos = true;
        Avisar($"Generando el balance de sumas y saldos de {ejercicio} en {(pdf ? "PDF" : "Excel")}…");
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        string? ruta = null;
        try
        {
            var r = await Task.Run(() => _sumasYSaldos.GenerarAsync(_ejercicioTrabajo, _razonSocial, parametros));
            var nombre = ProConta.Contabilidad.Balances.CalculadoraSumasYSaldos.NombreFichero(r, pdf ? ".pdf" : ".xlsx");
            var temporales = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProContaVF", "Informes");
            System.IO.Directory.CreateDirectory(temporales);
            byte[] contenido;
            if (pdf)
            {
                var html = InformeSumasYSaldos.Html(r);
                contenido = await Task.Run(() => ConversorHtmlPdf.Compartido.Convertir(html, InformeSumasYSaldos.Pagina));
            }
            else
            {
                var temporal = System.IO.Path.Combine(temporales, nombre);
                await Task.Run(() => InformeSumasYSaldos.Excel(r, temporal));
                contenido = await System.IO.File.ReadAllBytesAsync(temporal);
                try { System.IO.File.Delete(temporal); } catch (System.IO.IOException) { }
            }
            // Por el gestor documental (documentos 2026-11-18): «<año>\Informes» de la carpeta de documentos, vinculado al ejercicio.
            var guardado = await _servicioDocumentos.GuardarAsync(new ProConta.Documentos.SolicitudDocumento(
                _ejercicioTrabajo.IdSociedad, "INFORME_CONTABLE",
                $"Sumas y saldos {ejercicio} del {parametros.Desde:dd/MM} al {parametros.Hasta:dd/MM}",
                System.IO.Path.Combine(ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture), "Informes"), nombre, r.Generado.Date, ejercicio, "INFORME_CONTABLE",
                [new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Ejercicio, _ejercicioTrabajo.IdSociedadEjercicio, null, $"Ejercicio {ejercicio}")]),
                contenido);
            if (guardado.EsCorrecto) ruta = guardado.Valor.RutaCompleta;
            else
            {
                ruta = System.IO.Path.Combine(temporales, nombre);
                await System.IO.File.WriteAllBytesAsync(ruta, contenido);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            if (!guardado.EsCorrecto)
                Avisar($"El balance no se ha guardado en la carpeta de documentos ({guardado.Error!.Mensaje}); lo abro desde {ruta}.");
            else if (r.Filas.Count == 0)
                Avisar($"Sumas y saldos {ejercicio}: no hay apuntes contabilizados del {parametros.Desde:dd/MM/yyyy} al {parametros.Hasta:dd/MM/yyyy}.");
            else
                Avisar($"Sumas y saldos {ejercicio} abierto ({r.NumeroSubcuentas} subcuentas con movimiento): sumas {r.TotalDebe.ToString("N2", es)} € al Debe y " +
                       $"{r.TotalHaber.ToString("N2", es)} € al Haber · saldos {r.TotalSaldoDeudor.ToString("N2", es)} € deudores y {r.TotalSaldoAcreedor.ToString("N2", es)} € acreedores" +
                       (r.TotalDebe == r.TotalHaber ? "." : ": el Debe y el Haber no cuadran, revisa los asientos en Contabilidad › Diario."));
        }
        catch (Exception ex)
        {
            var detalles = new List<string>();
            if (ex is System.IO.IOException && ruta is not null) detalles.Add($"Fichero: {ruta}");
            if (ex is not InvalidOperationException and not System.IO.IOException) detalles.Add($"Detalle técnico: {ex.GetType().Name}");
            var eleccion = ServicioMensajes.Preguntar(this, "No he podido generar el balance de sumas y saldos", ex.Message, detalles,
                new OpcionMensaje("Reintentar", "Vuelve a generarlo con las mismas fechas, niveles y formato", Principal: true),
                new OpcionMensaje("Cerrar", "Vuelve a la pantalla anterior", Cancelar: true));
            if (eleccion == 0) { _generandoSumasYSaldos = false; Cursor = anterior; await GenerarSumasYSaldosAsync(parametros, formato); return; }
        }
        finally
        {
            Cursor = anterior;
            _generandoSumasYSaldos = false;
        }
    }

    /// <summary>
    /// PDF de las cuentas anuales (contabilidad/procesos/cuentas-anuales): las páginas del modelo pymes, guardado por el gestor documental en
    /// «&lt;año&gt;\Cuentas anuales» de la carpeta de documentos, vinculado al ejercicio de las cuentas, y abierto.
    /// </summary>
    private async Task GenerarCuentasAnualesPdfAsync(ProConta.Contabilidad.CuentasAnuales.CuentasAnualesPymes c,
        IReadOnlyList<ProConta.Contabilidad.CuentasAnuales.ApartadoMemoria> memoria, int idEjercicio)
    {
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        string? ruta = null;
        try
        {
            Avisar($"Generando el PDF de las cuentas anuales de {c.Ejercicio}…");
            var html = ProConta.Informes.InformeCuentasAnuales.Html(c, memoria);
            var contenido = await Task.Run(() => ConversorHtmlPdf.Compartido.Convertir(html, ProConta.Informes.InformeCuentasAnuales.Pagina));
            var nombre = ProConta.Informes.InformeCuentasAnuales.NombreFichero(c);
            var guardado = await _servicioDocumentos.GuardarAsync(new ProConta.Documentos.SolicitudDocumento(
                _ejercicioTrabajo.IdSociedad, "INFORME_CONTABLE", $"Cuentas anuales {c.Ejercicio}" + (c.TieneErrores ? " (borrador con errores)" : ""),
                System.IO.Path.Combine(c.Ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture), "Cuentas anuales"), nombre, DateTime.Today, c.Ejercicio, "INFORME_CONTABLE",
                [new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Ejercicio, idEjercicio, null, $"Ejercicio {c.Ejercicio}")]),
                contenido);
            if (guardado.EsCorrecto) ruta = guardado.Valor.RutaCompleta;
            else
            {
                var temporales = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProContaVF", "Informes");
                System.IO.Directory.CreateDirectory(temporales);
                ruta = System.IO.Path.Combine(temporales, nombre);
                await System.IO.File.WriteAllBytesAsync(ruta, contenido);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            Avisar(!guardado.EsCorrecto
                ? $"Las cuentas anuales no se han guardado en la carpeta de documentos ({guardado.Error!.Mensaje}); las abro desde {ruta}."
                : c.TieneErrores
                    ? $"Cuentas anuales {c.Ejercicio} abiertas como borrador: tienen errores que el Registro no admitiría (se explican en la pantalla)."
                    : $"Cuentas anuales {c.Ejercicio} abiertas y guardadas en {ruta}.");
        }
        catch (Exception ex)
        {
            var detalles = new List<string>();
            if (ex is System.IO.IOException && ruta is not null) detalles.Add($"Fichero: {ruta}");
            if (ex is not InvalidOperationException and not System.IO.IOException) detalles.Add($"Detalle técnico: {ex.GetType().Name}");
            ServicioMensajes.Preguntar(this, "No he podido generar el PDF de las cuentas anuales", ex.Message, detalles,
                new OpcionMensaje("Cerrar", "Vuelve a la pantalla; pulsa otra vez «Generar PDF…» para reintentarlo", Principal: true, Cancelar: true));
        }
        finally
        {
            Cursor = anterior;
        }
    }

    /// <summary>
    /// Los dos libros de la legalización (contabilidad/procesos/legalizacion-libros): DIARIO_001.pdf e INV_CUEN_001.pdf, guardados por el gestor
    /// documental en «&lt;año&gt;\Legalización\&lt;fecha-hora&gt;» (una carpeta por generación, para que los nombres oficiales no cambien),
    /// vinculados al ejercicio. Devuelve dónde han quedado; null si no se han podido generar (ya lo ha explicado).
    /// </summary>
    private async Task<IReadOnlyList<ProConta.Contabilidad.UI.Legalizacion.FicheroGuardado>?> GenerarLibrosLegalizacionAsync(ProConta.Contabilidad.Legalizacion.DatosLegalizacion d)
    {
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        try
        {
            Avisar($"Generando los libros de {d.Ejercicio} para legalizar (el Diario tiene {d.Diario.NumeroApuntes:N0} apuntes: tarda unos segundos)…");
            var diario = await Task.Run(() => ProConta.Informes.InformeLegalizacion.Diario(d.Diario, d.Generado, ConversorHtmlPdf.Compartido.Convertir));
            var inventarios = await Task.Run(() => ProConta.Informes.InformeLegalizacion.Inventarios(d, ConversorHtmlPdf.Compartido.Convertir));
            var anio = d.Ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var carpeta = System.IO.Path.Combine(anio, "Legalización", DateTime.Now.ToString("yyyyMMdd-HHmm", System.Globalization.CultureInfo.InvariantCulture));
            var vinculo = new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Ejercicio, d.IdSociedadEjercicio, null, $"Ejercicio {d.Ejercicio}");
            var guardados = new List<ProConta.Contabilidad.UI.Legalizacion.FicheroGuardado>();
            var borrador = d.TieneErrores ? " (borrador con errores)" : "";
            foreach (var f in new[] { diario, inventarios })
            {
                var g = await _servicioDocumentos.GuardarAsync(new ProConta.Documentos.SolicitudDocumento(_ejercicioTrabajo.IdSociedad, "INFORME_CONTABLE",
                    $"{f.Libro} {d.Ejercicio} para legalizar{borrador}", carpeta, f.Nombre, DateTime.Today, d.Ejercicio, "INFORME_CONTABLE", [vinculo]), f.Contenido);
                string ruta;
                if (g.EsCorrecto) ruta = g.Valor.RutaCompleta;
                else
                {
                    // Sin carpeta de documentos: a una carpeta temporal propia, con el nombre oficial intacto.
                    var temporal = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProContaVF", "Legalizacion", anio, DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture));
                    System.IO.Directory.CreateDirectory(temporal);
                    ruta = System.IO.Path.Combine(temporal, f.Nombre);
                    await System.IO.File.WriteAllBytesAsync(ruta, f.Contenido);
                    Avisar($"{f.Nombre} no se ha guardado en la carpeta de documentos ({g.Error!.Mensaje}); está en {ruta}.");
                }
                if (!string.Equals(System.IO.Path.GetFileName(ruta), f.Nombre, StringComparison.OrdinalIgnoreCase))
                    Avisar($"Ojo: el fichero ha quedado como {System.IO.Path.GetFileName(ruta)}; para presentarlo tiene que llamarse {f.Nombre}.");
                guardados.Add(new(f, ruta));
            }
            if (guardados.All(x => string.Equals(System.IO.Path.GetFileName(x.Ruta), x.Fichero.Nombre, StringComparison.OrdinalIgnoreCase)))
                Avisar($"Libros de {d.Ejercicio} generados{borrador}: Diario {diario.Paginas} páginas e Inventarios y Cuentas Anuales {inventarios.Paginas} folios, en {System.IO.Path.GetDirectoryName(guardados[0].Ruta)}.");
            return guardados;
        }
        catch (Exception ex)
        {
            var detalles = new List<string>();
            if (ex is not InvalidOperationException and not System.IO.IOException and not ErrorMotorPdf) detalles.Add($"Detalle técnico: {ex.GetType().Name}");
            ServicioMensajes.Preguntar(this, "No he podido generar los libros para legalizar", ex.Message, detalles,
                new OpcionMensaje("Cerrar", "Vuelve a la pantalla; pulsa otra vez «Generar los libros» para reintentarlo", Principal: true, Cancelar: true));
            return null;
        }
        finally
        {
            Cursor = anterior;
        }
    }

    /// <summary>
    /// Ficheros del depósito de las cuentas anuales para el programa D2 (contabilidad/procesos/cuentas-anuales): DEPOSITO.XBRL y MEMORIA.PDF,
    /// guardados por el gestor documental en «&lt;año&gt;\Cuentas anuales\D2» vinculados al ejercicio; abre su carpeta.
    /// </summary>
    private async Task GenerarDepositoD2Async(ProConta.Contabilidad.CuentasAnuales.CuentasAnualesPymes c,
        IReadOnlyList<ProConta.Contabilidad.CuentasAnuales.ApartadoMemoria> memoria, int idEjercicio)
    {
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        try
        {
            Avisar($"Generando los ficheros para D2 de las cuentas anuales de {c.Ejercicio}…");
            var xbrl = ProConta.Contabilidad.CuentasAnuales.XbrlCuentasAnuales.Generar(c);
            var pdf = await Task.Run(() => ConversorHtmlPdf.Compartido.Convertir(ProConta.Informes.InformeCuentasAnuales.Html(c, memoria, estados: false),
                ProConta.Informes.InformeCuentasAnuales.Pagina));
            var anio = c.Ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var carpeta = System.IO.Path.Combine(anio, "Cuentas anuales", "D2");
            var vinculo = new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Ejercicio, idEjercicio, null, $"Ejercicio {c.Ejercicio}");
            string? ruta = null;
            var errores = new List<string>();
            foreach (var (nombre, titulo, contenido) in new[]
                     {
                         (ProConta.Contabilidad.CuentasAnuales.XbrlCuentasAnuales.NombreFichero, $"Cuentas anuales {c.Ejercicio} · DEPOSITO.XBRL para D2", xbrl),
                         ("MEMORIA.PDF", $"Cuentas anuales {c.Ejercicio} · memoria para D2", pdf),
                     })
            {
                var g = await _servicioDocumentos.GuardarAsync(new ProConta.Documentos.SolicitudDocumento(_ejercicioTrabajo.IdSociedad, "INFORME_CONTABLE", titulo,
                    carpeta, nombre, DateTime.Today, c.Ejercicio, "INFORME_CONTABLE", [vinculo]), contenido);
                if (g.EsCorrecto) ruta = g.Valor.RutaCompleta;
                else
                {
                    var temporales = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProContaVF", "D2", anio);
                    System.IO.Directory.CreateDirectory(temporales);
                    ruta = System.IO.Path.Combine(temporales, nombre);
                    await System.IO.File.WriteAllBytesAsync(ruta, contenido);
                    errores.Add($"{nombre}: {g.Error!.Mensaje}");
                }
            }
            if (ruta is not null) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + ruta + "\"") { UseShellExecute = true });
            Avisar(errores.Count > 0
                ? $"Ficheros para D2 generados, pero no se han guardado en la carpeta de documentos ({string.Join("; ", errores)}); te abro la carpeta donde están."
                : $"Ficheros para D2 de {c.Ejercicio} guardados: en D2, importa el DEPOSITO.XBRL y adjunta MEMORIA.PDF como memoria. Te abro su carpeta.");
        }
        catch (Exception ex)
        {
            var detalles = new List<string>();
            if (ex is not InvalidOperationException and not System.IO.IOException) detalles.Add($"Detalle técnico: {ex.GetType().Name}");
            ServicioMensajes.Preguntar(this, "No he podido generar los ficheros para D2", ex.Message, detalles,
                new OpcionMensaje("Cerrar", "Vuelve a la pantalla; pulsa otra vez «Ficheros para D2…» para reintentarlo", Principal: true, Cancelar: true));
        }
        finally
        {
            Cursor = anterior;
        }
    }

    /// <summary>
    /// Informes › Libros registro de IVA… (informes/excel/libros-registro-iva): pide periodo, libros y formato, calcula los libros del ejercicio de trabajo
    /// desde las líneas de IVA del 303, los guarda por el gestor documental (INFORME_CONTABLE, en «&lt;año&gt;\Informes», vinculado al ejercicio) y abre la copia.
    /// </summary>
    private async Task GenerarLibrosIvaAsync(ProConta.Contabilidad.Iva.ParametrosLibrosIva? repetir = null,
        ProConta.Contabilidad.UI.Iva.FormatoLibrosIva formatoRepetir = default)
    {
        if (_generandoLibrosIva) { Avisar("Ya estoy generando los libros registro de IVA: espera a que se abran."); return; }
        var ejercicio = _ejercicioTrabajo.Ejercicio;
        var (parametros, formato) = repetir is not null ? (repetir, formatoRepetir)
            : ProConta.Contabilidad.UI.Iva.DialogoLibrosIva.Pedir(this, ejercicio, _razonSocial, DateTime.Today) is { } elegido ? elegido : default;
        if (parametros is null) return;
        var pdf = formato == ProConta.Contabilidad.UI.Iva.FormatoLibrosIva.Pdf;
        _generandoLibrosIva = true;
        Avisar($"Generando los libros registro de IVA ({parametros.Periodo}) en {(pdf ? "PDF" : "Excel")}…");
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        string? ruta = null;
        try
        {
            var r = await Task.Run(() => _librosIva.GenerarAsync(_ejercicioTrabajo, _razonSocial, parametros));
            var nombre = ProConta.Contabilidad.Iva.CalculadoraLibrosIva.NombreFichero(r, pdf ? ".pdf" : ".xlsx");
            var temporales = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProContaVF", "Informes");
            System.IO.Directory.CreateDirectory(temporales);
            byte[] contenido;
            if (pdf)
            {
                var html = InformeLibrosIva.Html(r);
                contenido = await Task.Run(() => ConversorHtmlPdf.Compartido.Convertir(html, InformeLibrosIva.Pagina));
            }
            else
            {
                var temporal = System.IO.Path.Combine(temporales, nombre);
                await Task.Run(() => InformeLibrosIva.Excel(r, temporal));
                contenido = await System.IO.File.ReadAllBytesAsync(temporal);
                try { System.IO.File.Delete(temporal); } catch (System.IO.IOException) { }
            }
            var guardado = await _servicioDocumentos.GuardarAsync(new ProConta.Documentos.SolicitudDocumento(
                _ejercicioTrabajo.IdSociedad, "INFORME_CONTABLE", $"{InformeLibrosIva.Titulo(r)} {parametros.Periodo}",
                System.IO.Path.Combine(ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture), "Informes"), nombre, r.Generado.Date, ejercicio, "INFORME_CONTABLE",
                [new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Ejercicio, _ejercicioTrabajo.IdSociedadEjercicio, null, $"Ejercicio {ejercicio}")]),
                contenido);
            if (guardado.EsCorrecto) ruta = guardado.Valor.RutaCompleta;
            else
            {
                ruta = System.IO.Path.Combine(temporales, nombre);
                await System.IO.File.WriteAllBytesAsync(ruta, contenido);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            var partes = new List<string>();
            if (parametros.Expedidas) partes.Add($"{r.FacturasExpedidas} facturas expedidas ({r.Expedidas.Sum(f => f.Cuota).ToString("N2", es)} € de cuota)");
            if (parametros.Recibidas) partes.Add($"{r.FacturasRecibidas} recibidas ({r.Recibidas.Sum(f => f.Cuota).ToString("N2", es)} € de cuota)");
            if (!guardado.EsCorrecto)
                Avisar($"Los libros no se han guardado en la carpeta de documentos ({guardado.Error!.Mensaje}); los abro desde {ruta}.");
            else
                Avisar($"Libros registro de IVA {parametros.Periodo} abiertos: {string.Join(" y ", partes)} · resultado del 303 del periodo {r.Cuadre303.Resultado(0).ToString("N2", es)} €.");
        }
        catch (Exception ex)
        {
            var detalles = new List<string>();
            if (ex is System.IO.IOException && ruta is not null) detalles.Add($"Fichero: {ruta}");
            if (ex is not InvalidOperationException and not System.IO.IOException) detalles.Add($"Detalle técnico: {ex.GetType().Name}");
            var eleccion = ServicioMensajes.Preguntar(this, "No he podido generar los libros registro de IVA", ex.Message, detalles,
                new OpcionMensaje("Reintentar", "Vuelve a generarlos con el mismo periodo, libros y formato", Principal: true),
                new OpcionMensaje("Cerrar", "Vuelve a la pantalla anterior", Cancelar: true));
            if (eleccion == 0) { _generandoLibrosIva = false; Cursor = anterior; await GenerarLibrosIvaAsync(parametros, formato); return; }
        }
        finally
        {
            Cursor = anterior;
            _generandoLibrosIva = false;
        }
    }

    private async Task GenerarInformeGestoriaAsync(int? trimestre = null)
    {
        if (_generandoGestoria) { Avisar("Ya estoy generando el informe para la gestoría: espera a que se abra en Excel."); return; }
        var ejercicio = _ejercicioTrabajo.Ejercicio;
        if (trimestre is null)
        {
            var propuesto = ProConta.Informes.Excel.InformeGestoria.TrimestrePropuesto(ejercicio, DateTime.Today);
            var opciones = Enumerable.Range(1, 4).Select(q => new OpcionMensaje($"{q}T",
                    $"Del {new DateTime(ejercicio, 3 * q - 2, 1):dd/MM/yyyy} al {ProConta.Informes.Excel.InformeGestoria.FinDe(ejercicio, q):dd/MM/yyyy}" +
                    (q == propuesto ? " (el último trimestre terminado)" : ""), Principal: q == propuesto))
                .Append(new OpcionMensaje("Cancelar", "No genera nada", Cancelar: true)).ToArray();
            var eleccion = ServicioMensajes.Preguntar(this, "Informe para la gestoría",
                $"¿De qué trimestre de {ejercicio} genero el informe? El IVA sale de todo el ejercicio contabilizado hasta hoy; pérdidas y ganancias, balance, ratios y " +
                "listados de facturas, hasta el último día del trimestre que elijas.", null, opciones);
            if (eleccion is < 0 or > 3) return;
            trimestre = eleccion + 1;
        }
        _generandoGestoria = true;
        Avisar($"Generando el informe para la gestoría del {trimestre}T de {ejercicio}…");
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        string? ruta = null;
        try
        {
            var inversiones = (ProConta.UI.Base.Preferencias.PreferenciasOperador.Actual.Leer<List<string>>(ProConta.Inicio.UI.InicioVM.ClaveInversiones(_ejercicioTrabajo.IdSociedad)) ?? [])
                .ToHashSet(StringComparer.Ordinal);
            var t = trimestre.Value;
            var informe = await Task.Run(() => _gestoria.ComponerAsync(_ejercicioTrabajo, _razonSocial, t, DateTime.Now, inversiones));
            // Se genera en una carpeta temporal y se guarda por el gestor documental (documentos 2026-11-18, parte d) en «<año>\Gestoria» de la
            // carpeta de documentos, vinculado al ejercicio; se abre la copia guardada.
            var carpeta = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ProContaVF", "Informes");
            System.IO.Directory.CreateDirectory(carpeta);
            var nombre = ProConta.Informes.Excel.InformeGestoria.NombreFichero(ejercicio, t, informe.Generado);
            ruta = System.IO.Path.Combine(carpeta, nombre);
            var destino = ruta;
            await Task.Run(() => ProConta.Informes.Excel.ExportadorInformeGestoriaExcel.Exportar(informe, destino));
            var guardado = await _servicioDocumentos.GuardarAsync(new ProConta.Documentos.SolicitudDocumento(
                _ejercicioTrabajo.IdSociedad, "INFORME_GESTORIA", $"Informe para la gestoría {ejercicio} {t}T",
                System.IO.Path.Combine(ejercicio.ToString(System.Globalization.CultureInfo.InvariantCulture), "Gestoria"), nombre, informe.Generado.Date, ejercicio, "INFORME_GESTORIA",
                [new ProConta.Documentos.Vinculo(ProConta.Documentos.TiposEntidad.Ejercicio, _ejercicioTrabajo.IdSociedadEjercicio, null, $"Ejercicio {ejercicio}")]),
                await System.IO.File.ReadAllBytesAsync(ruta));
            if (guardado.EsCorrecto)
            {
                try { System.IO.File.Delete(ruta); } catch (System.IO.IOException) { }
                ruta = guardado.Valor.RutaCompleta;
            }
            else Avisar($"El informe no se ha guardado en la carpeta de documentos ({guardado.Error!.Mensaje}); lo abro desde {ruta}.");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ruta) { UseShellExecute = true });
            var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
            var resultado = informe.Iva303.Resultado(t);
            var revisar = ProConta.Informes.Excel.ControlesGestoria.Calcular(informe).Count(c => c.Estado == ProConta.Informes.Excel.EstadoControl.Revisar);
            Avisar($"Informe para la gestoría {ejercicio}-{t}T abierto en Excel: 303 del {t}T {Math.Abs(resultado).ToString("N2", es)} € {(resultado >= 0 ? "a ingresar" : "a compensar")} · " +
                   $"prorrata {informe.Prorrata.Provisional:P0} → {informe.Prorrata.Real:P0} · " +
                   (revisar == 0 ? "ningún control a revisar." : $"{revisar} controles a revisar (hoja Controles)."));
        }
        catch (Exception ex)
        {
            var bloqueado = ex is System.IO.IOException;
            var detalles = new List<string>();
            if (bloqueado && ruta is not null) detalles.Add($"Fichero: {ruta}");
            if (ex is not InvalidOperationException and not System.IO.IOException) detalles.Add($"Detalle técnico: {ex.GetType().Name}");
            var mensaje = bloqueado
                ? $"No he podido escribir el libro: {ex.Message} Si tienes abierto en Excel otro informe con el mismo nombre, ciérralo y pulsa «Reintentar»."
                : ex.Message;
            var eleccion = ServicioMensajes.Preguntar(this, "No he podido generar el informe para la gestoría", mensaje, detalles,
                new OpcionMensaje("Reintentar", "Vuelve a generar el informe del mismo trimestre", Principal: true),
                new OpcionMensaje("Cerrar", "Vuelve a la pantalla anterior", Cancelar: true));
            if (eleccion == 0) { _generandoGestoria = false; Cursor = anterior; await GenerarInformeGestoriaAsync(trimestre); return; }
        }
        finally
        {
            Cursor = anterior;
            _generandoGestoria = false;
        }
    }

    /// <summary>
    /// Botón del aviso «Comisiones sin compensar» de Inicio (facturacion/reservas/compensacion-comision): enseña las facturas de comisión
    /// pendientes del ejercicio de trabajo, pide confirmación y hace un asiento de compensación por factura.
    /// </summary>
    private async Task<bool> CompensarComisionesAsync(IBaseDatos bd, Actor actor)
    {
        var es = System.Globalization.CultureInfo.GetCultureInfo("es-ES");
        var servicio = new ProConta.Compras.Facturas.CompensacionComision(bd, new MotorAsientosAccess(bd));
        var ctx = _ejercicioTrabajo;
        var pendientes = await Task.Run(() => servicio.PendientesAsync(ctx.IdSociedad, ctx.IdSociedadEjercicio));
        if (pendientes.Count == 0)
        {
            Avisar("No hay comisiones de plataformas por compensar.");
            return true;
        }
        var total = pendientes.Sum(p => p.Importe);
        var detalles = pendientes.Select(p => $"{p.Plataforma} · {(p.NumeroFactura ?? "sin número")} · {p.Fecha:dd/MM/yyyy} · {p.Importe.ToString("N2", es)} €").ToList();
        var eleccion = ServicioMensajes.Preguntar(this, "Compensar las comisiones",
            $"Voy a compensar {pendientes.Count} {(pendientes.Count == 1 ? "factura de comisión" : "facturas de comisión")} de {ctx.Ejercicio} por {total.ToString("N2", es)} €: " +
            "un asiento por factura, con su 410 al Debe y la 430 de la plataforma al Haber, con la fecha de su asiento (o el día 1 del trimestre activo si es anterior).",
            detalles,
            new OpcionMensaje("Compensar", "Hace los asientos de compensación", Principal: true),
            new OpcionMensaje("Cancelar", "No hace nada", Cancelar: true));
        if (eleccion != 0) return false;
        var anterior = Cursor;
        Cursor = Cursors.Wait;
        try
        {
            var r = await Task.Run(() => servicio.CompensarPendientesAsync(ctx.IdSociedad, ctx.IdSociedadEjercicio, actor));
            if (r.Problemas.Count == 0) Avisar(r.Texto);
            else
                ServicioMensajes.Preguntar(this, "Compensar las comisiones", r.Texto, r.Problemas,
                    new OpcionMensaje("Cerrar", "Vuelve a Inicio; el aviso sigue con las que faltan", Principal: true, Cancelar: true));
            return true;
        }
        finally { Cursor = anterior; }
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
