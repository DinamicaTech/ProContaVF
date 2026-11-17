using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.FormasPago;

namespace ProConta.Organizacion.Mantenimiento;

/// <summary>
/// Proveedores de organizacion/sociedades-ejercicios y organizacion/formas-pago para la pantalla genérica de
/// mantenimiento: Empresas, Ejercicios (de la empresa de trabajo) y Formas de pago.
/// </summary>
public static class ProveedoresOrganizacion
{
    public static IEnumerable<IProveedorMantenimiento> Todos(IBaseDatos bd) =>
        [Empresas(bd), Ejercicios(bd), new ProveedorFormasPago(bd, new CatalogoFormasPagoAccess(bd)), new ProveedorFormasPagoSociedad(new CatalogoFormasPagoSociedad(bd)), TiposRetencion(bd)];

    internal static IReadOnlyList<GrupoCampos> GruposEmpresa => [new("ident", "Identificación"), new("cuentas", "Cuentas"), new("rutas", "Rutas")];

    internal static IReadOnlyList<GrupoCampos> GruposEjercicio =>
        [new("ident", "Identificación"), new("pred", "Valores predeterminados"), new("impuestos", "Impuestos"), new("cuentas", "Cuentas"), new("rutas", "Rutas")];

    private static CampoMantenimiento Cuenta(string clave, string etiqueta, int longitud = 10, string? ayuda = null, params string[] sinonimos) =>
        new(clave, etiqueta, TipoCampo.Cuenta) { Grupo = "cuentas", Longitud = longitud, Ancho = "s", Ayuda = ayuda, Sinonimos = sinonimos, GruposCuenta = GruposDe(clave) };

    /// <summary>Grupos a los que se limita el buscador de cada cuenta (plataforma/mantenimiento 2026-11-12); se puede quitar el filtro.</summary>
    private static IReadOnlyList<string> GruposDe(string clave) => clave switch
    {
        _ when clave.Contains("Repercutido") => ["477"],
        _ when clave.Contains("Soportado") || clave == "CuentaIVAImportacion" => ["472"],
        _ when clave.StartsWith("CuentaRetencion") => ["475"],
        "Cuenta113ParaCuadres" => ["113"],
        "Cuenta570Tesoreria" => ["57"],
        "CuentaRedondeoGastos" => ["6"],
        "CuentaRedondeoIngresos" => ["7"],
        "CuentaHaciendaAcreedoraIva" => ["475"],
        "CuentaHaciendaDeudoraIva" => ["470"],
        "CuentaAjustesNegativosIva" => ["634"],
        "CuentaAjustesPositivosIva" => ["639"],
        "CuentaReservasDividendos" => ["113"],
        "CuentaRegularizacionProrrata" => ["472"],
        "CuentaImpuestoSociedades" or "CuentaImpuestoDiferidoIS" => ["630"],
        "CuentaHaciendaAcreedoraIS" => ["475"],
        "CuentaHaciendaDeudoraIS" => ["470"],
        "CuentaCreditoPerdidasIS" => ["474"],
        "CuentaResultadoEjercicio" => ["129"],
        _ => [],
    };

    private static CampoMantenimiento Ruta(string clave, string etiqueta) => new(clave, etiqueta, TipoCampo.Ruta) { Grupo = "rutas", Longitud = 255, Ancho = "l" };

    /// <summary>Solo en Ejercicios: el tipo general es del ejercicio (compras/pantallas 2026-11-06 12:10).</summary>
    internal static CampoMantenimiento TipoGeneral() =>
        new("TipoIVAGeneral", "Tipo de IVA general (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["iva general", "21"],
            Ayuda = "El de la autorrepercusión (inversión del sujeto pasivo e intracomunitarias) y el de la importación en las compras. Vacío: el más alto de los tipos de IVA activos (Catálogos › Tipos de IVA)." };

    /// <summary>Prorrata del año anterior, que usará el cálculo de la prorrata (organizacion/sociedades-ejercicios 2026-11-13 20:55).</summary>
    internal static CampoMantenimiento ProrrataAnterior() =>
        new("PorcentajeProrrataAñoAnterior", "Prorrata del año anterior (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["prorrata"],
            Ayuda = "Porcentaje de prorrata definitivo del año anterior, que se aplica de forma provisional en este ejercicio, en porcentaje: 14 es el 14 %. " +
                "Lo rellena solo la regularización de la prorrata del año anterior (Contabilidad › Prorrata de IVA)." };

    /// <summary>Prorrata definitiva del ejercicio (contabilidad/procesos/prorrata, esquema 63).</summary>
    internal static CampoMantenimiento ProrrataDefinitiva() =>
        new("PorcentajeProrrataDefinitiva", "Prorrata definitiva (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["prorrata", "definitiva"],
            Ayuda = "Prorrata definitiva de este ejercicio, en porcentaje: 20 es el 20 %. Vacía mientras no se fije. Con ella, el 303 del 4T lleva la regularización de la prorrata. " +
                "Lo normal es fijarla en Contabilidad › Prorrata de IVA, que propone la legal, graba la regularización y la copia como prorrata del año anterior del ejercicio siguiente." };

    /// <summary>Cuentas de compras que solo tiene el ejercicio (compras/factura-compra/contabilizacion).</summary>
    internal static IEnumerable<CampoMantenimiento> CuentasCompras() =>
    [
        Cuenta("CuentaIVAImportacion", "IVA de importación", ayuda: "IVA soportado en importaciones (4724001 en el ERP): el importe de la factura se desglosa al tipo general."),
        Cuenta("CuentaRedondeoGastos", "Diferencias de redondeo · gastos", ayuda: "Cuando a un asiento de compra le falta Debe (hasta 0,03 €, o más con motivo). Vacía: 6590000.", sinonimos: ["redondeo"]),
        Cuenta("CuentaRedondeoIngresos", "Diferencias de redondeo · ingresos", ayuda: "Cuando a un asiento de compra le sobra Debe. Vacía: 7590000. Si no existe, se crea al usarla.", sinonimos: ["redondeo"]),
    ];

    /// <summary>Cuentas de la liquidación del IVA del ejercicio (contabilidad/procesos/liquidacion-iva, esquema 56); vacías, las de siempre.</summary>
    internal static IEnumerable<CampoMantenimiento> CuentasLiquidacionIva() =>
    [
        Cuenta("CuentaHaciendaAcreedoraIva", "Liquidación del IVA · a ingresar", ayuda: "Hacienda acreedora por IVA: recibe el resultado a ingresar de cada trimestre. Vacía: 4750000. Si no existe, se crea al liquidar.",
            sinonimos: ["4750", "liquidacion", "iva a ingresar"]),
        Cuenta("CuentaHaciendaDeudoraIva", "Liquidación del IVA · a compensar", ayuda: "Hacienda deudora por IVA: recibe el resultado a compensar o devolver, y su saldo se compensa en la siguiente que salga a ingresar. Vacía: 4700000.",
            sinonimos: ["4700", "liquidacion", "iva a compensar"]),
        Cuenta("CuentaAjustesNegativosIva", "Liquidación del IVA · no deducible y ajustes a pagar más", ayuda: "Recibe el IVA soportado que no se deduce (prorrata y actividades no deducibles) y el ajuste cuando se liquida más de lo que dicen las cuentas. Vacía: 6341000.",
            sinonimos: ["6341", "prorrata", "no deducible"]),
        Cuenta("CuentaAjustesPositivosIva", "Liquidación del IVA · ajustes a pagar menos", ayuda: "Recibe el ajuste cuando se liquida menos de lo que dicen las cuentas (por ejemplo, para cuadrar con lo presentado). Vacía: 6391000. Si no existe, se crea al usarla.",
            sinonimos: ["6391", "ajuste iva"]),
        Cuenta("CuentaRegularizacionProrrata", "Regularización de la prorrata", ayuda: "Recibe el 31/12 la regularización anual de la prorrata (Contabilidad › Prorrata de IVA), y la liquidación del 4T la salda. Vacía: 4729001. Si no existe, se crea al regularizar.",
            sinonimos: ["4729", "prorrata", "regularizacion"]),
    ];

    /// <summary>Retención y cuenta de reservas de los dividendos del ejercicio (contabilidad/procesos/aplicacion-resultado, esquema 62); vacías, 19 % y 1130000.</summary>
    internal static IEnumerable<CampoMantenimiento> Dividendos() =>
    [
        new("PorcentajeRetencionDividendos", "Retención de dividendos (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["dividendos", "retencion", "socios"],
            Ayuda = "Retención que se aplica a los dividendos que se reparten en este ejercicio, en porcentaje: 19 es el 19 %. Vacío: 19 %. El reparto calcula el bruto de cada socio como neto / (1 − retención)." },
        Cuenta("CuentaReservasDividendos", "Reparto de dividendos · reservas", ayuda: "Cuenta de reservas que se carga por el bruto de los dividendos en Contabilidad › Reparto de dividendos. Vacía: 1130000 (reservas voluntarias). Si no existe, se crea al repartir.",
            sinonimos: ["1130", "reservas", "dividendos"]),
    ];

    /// <summary>
    /// Parámetros y cuentas del Impuesto sobre Sociedades del ejercicio (contabilidad/procesos/impuesto-sociedades, esquema 64); vacíos:
    /// 25 % sin tramo ni bonificación, prefijos 678, 669, 778, 760, 766 y 666, y cuentas 6300000, 4752000, 4709000, 4745000 y 6301000.
    /// </summary>
    internal static IEnumerable<CampoMantenimiento> ImpuestoSociedades() =>
    [
        new("TipoImpuestoSociedades", "Impuesto sobre Sociedades · tipo general (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["sociedades", "is", "tipo"],
            Ayuda = "Tipo del IS para la base que pasa del tramo reducido (o para toda, si no hay tramo), en porcentaje: 25 es el 25 %. Vacío: 25 %. Lo confirma la gestoría cada año." },
        new("LimiteTramoReducidoIS", "Impuesto sobre Sociedades · base del tramo reducido (€)", TipoCampo.Importe) { Grupo = "impuestos", Ancho = "s", Sinonimos = ["sociedades", "tramo", "microempresa"],
            Ayuda = "Base imponible hasta la que se aplica el tipo reducido (microempresas: los primeros 50.000 €). Vacía: sin tramo, toda la base al tipo general." },
        new("TipoTramoReducidoIS", "Impuesto sobre Sociedades · tipo del tramo reducido (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["sociedades", "tramo", "microempresa"],
            Ayuda = "Tipo de la base hasta el límite del tramo reducido, en porcentaje (en 2025, el 21 % de microempresa). Vacío: sin tramo." },
        new("BonificacionViviendasIS", "Impuesto sobre Sociedades · bonificación por alquiler de viviendas (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["sociedades", "bonificacion", "viviendas"],
            Ayuda = "Porcentaje de la cuota que corresponde a las rentas del alquiler de viviendas que se bonifica (40 es el 40 %). Las actividades que la tienen se marcan en Catálogos › Actividades (IVA). Vacío: sin bonificación." },
        new("PrefijosAjustesIS", "Impuesto sobre Sociedades · cuentas de posibles ajustes", TipoCampo.Codigo) { Grupo = "impuestos", Longitud = 255, Ancho = "m", Sinonimos = ["sociedades", "ajustes", "sanciones"],
            Ayuda = "Prefijos de las cuentas que se listan como posibles ajustes fiscales para validarlos con la gestoría, separados por comas. Vacío: 678, 669, 778, 760, 766, 666 " +
                "(gastos e ingresos excepcionales, intereses de demora y dividendos o plusvalías de participaciones)." },
        Cuenta("CuentaImpuestoSociedades", "Impuesto sobre Sociedades · impuesto corriente", ayuda: "Gasto por el impuesto del ejercicio (cuota líquida). Vacía: 6300000. Si no existe, se crea al contabilizar.",
            sinonimos: ["6300", "sociedades", "impuesto"]),
        Cuenta("CuentaHaciendaAcreedoraIS", "Impuesto sobre Sociedades · a ingresar", ayuda: "Recibe lo que queda a pagar después de las retenciones y pagos a cuenta. Vacía: 4752000. Si no existe, se crea al contabilizar.",
            sinonimos: ["4752", "sociedades", "a ingresar"]),
        Cuenta("CuentaHaciendaDeudoraIS", "Impuesto sobre Sociedades · a devolver", ayuda: "Recibe lo que Hacienda tiene que devolver cuando las retenciones y pagos a cuenta superan la cuota. Vacía: 4709000. Si no existe, se crea al contabilizar.",
            sinonimos: ["4709", "sociedades", "a devolver"]),
        Cuenta("CuentaCreditoPerdidasIS", "Impuesto sobre Sociedades · crédito por pérdidas", ayuda: "Crédito por pérdidas a compensar activado; al aplicarlo se abona. Vacía: 4745000.",
            sinonimos: ["4745", "sociedades", "bases negativas"]),
        Cuenta("CuentaImpuestoDiferidoIS", "Impuesto sobre Sociedades · impuesto diferido", ayuda: "Gasto que recibe el crédito por pérdidas aplicado. Vacía: 6301000. Si no existe, se crea al contabilizar.",
            sinonimos: ["6301", "sociedades", "diferido"]),
    ];

    /// <summary>Cuenta de resultados del ejercicio (contabilidad/procesos/cierre-ejercicio, esquema 65); vacía, 1290000.</summary>
    internal static IEnumerable<CampoMantenimiento> CuentasCierre() =>
    [
        Cuenta("CuentaResultadoEjercicio", "Cierre · cuenta de resultados", ayuda: "Subcuenta del resultado del ejercicio: la regularización del cierre (Contabilidad › Cierre del ejercicio) salda contra ella los grupos 6 y 7. Vacía: 1290000. Si no existe, se crea al cerrar.",
            sinonimos: ["129", "resultado", "cierre", "regularizacion"]),
    ];

    /// <summary>
    /// Cuentas de la empresa que usan las plantillas de corrección de asientos (contabilidad/plantillas-asiento 2026-11-12 11:15;
    /// organizacion/sociedades-ejercicios 2026-11-12): solo en Empresas.
    /// </summary>
    internal static IEnumerable<CampoMantenimiento> CuentasCorreccion() =>
    [
        Cuenta("Cuenta113ParaCuadres", "Cuadres (113)", ayuda: "La usa la plantilla «Ajustar saldo inicial contra la 113» como contrapartida del ajuste (reservas voluntarias).",
            sinonimos: ["113", "cuadres", "reservas"]),
        Cuenta("Cuenta570Tesoreria", "Tesorería (570)", ayuda: "La usa la plantilla «Pago por tesorería» como cuenta de caja contra la que se paga.",
            sinonimos: ["570", "caja", "tesoreria"]),
    ];

    internal static IEnumerable<CampoMantenimiento> Cuentas() =>
    [
        Cuenta("CuentaSuplido", "Suplido", 255, "La usan las ventas en las líneas suplidas sin cuenta propia y las compras para el importe suplido (por ejemplo 5550002)."),
        Cuenta("CuentaRetencionServicios", "Retención de servicios", ayuda: "Retenciones de IRPF practicadas a profesionales (4751, modelo 111)."),
        Cuenta("CuentaRetencionAlquileres", "Retención de alquileres", ayuda: "Retenciones de IRPF practicadas en los alquileres (4751, modelo 115)."),
        Cuenta("CuentaIVAIntracomunitarioRepercutidoServicios", "IVA intracomunitario repercutido · servicios", ayuda: "IVA que se autorrepercute (477) al comprar servicios a otro país de la UE."),
        Cuenta("CuentaIVAIntracomunitarioRepercutidoBienes", "IVA intracomunitario repercutido · bienes", ayuda: "IVA que se autorrepercute (477) al comprar bienes a otro país de la UE."),
        Cuenta("CuentaIVAIntracomunitarioSoportadoServicios", "IVA intracomunitario soportado · servicios", ayuda: "IVA soportado deducible (472) de las compras de servicios a otro país de la UE."),
        Cuenta("CuentaIVAIntracomunitarioSoportadoBienes", "IVA intracomunitario soportado · bienes", ayuda: "IVA soportado deducible (472) de las compras de bienes a otro país de la UE."),
        Cuenta("CuentaIVAInversionSujetoPasivoRepercutido", "IVA por inversión del sujeto pasivo · repercutido", ayuda: "IVA que se autorrepercute (477) en las compras con inversión del sujeto pasivo."),
        Cuenta("CuentaIVAInversionSujetoPasivoSoportado", "IVA por inversión del sujeto pasivo · soportado", ayuda: "IVA soportado deducible (472) de las compras con inversión del sujeto pasivo."),
    ];

    private static IEnumerable<CampoMantenimiento> Predeterminados() =>
    [
        new("FormaPagoDefecto", "Forma de pago predeterminada", TipoCampo.Referencia) { Grupo = "pred", Ancho = "m", Referencia = "formaspago", Ayuda = "Forma de pago que se propone en las facturas nuevas." },
    ];

    public static ProveedorTablaAccess Empresas(IBaseDatos bd) => new(bd, "sociedades", "Empresas", "Empresas y ejercicios",
        "Datos de cada empresa: identificación, domicilio y teléfono del emisor de las facturas, logo, cuentas de las plantillas de asiento y carpeta de documentos.", AmbitoMantenimiento.Compartido, "empresa",
        "Sociedades", "IdSociedad", ClaveNueva.Escrita,
        [
            new("IdSociedad", "Código", TipoCampo.Codigo) { Grupo = "ident", Obligatorio = true, SoloAlta = true, Longitud = 3, Ancho = "xs", Ayuda = "Tres caracteres. Se fija al crear la empresa." },
            new("Nombre", "Nombre") { Ayuda = "Razón social de la empresa: es el nombre del emisor en todas sus facturas.", Grupo = "ident", Obligatorio = true, Longitud = 255, Ancho = "l" },
            new("CIF", "CIF", TipoCampo.Codigo) { Grupo = "ident", Obligatorio = true, Longitud = 10, Ancho = "s", Sinonimos = ["nif"], Ayuda = "NIF de la empresa: es el NIF del emisor en todas sus facturas." },
            new("DireccionFiscal", "Domicilio fiscal") { Grupo = "ident", Longitud = 255, Ancho = "l", Sinonimos = ["direccion", "domicilio"], Ayuda = "Sale en las facturas como domicilio del emisor y es obligatorio para validarlas. Se congela en cada factura al validarla." },
            new("CodigoPostalFiscal", "Código postal") { Ayuda = "Código postal del domicilio fiscal; sale en las facturas como parte de la dirección del emisor.", Grupo = "ident", Longitud = 20, Ancho = "xs" },
            new("PoblacionFiscal", "Población") { Ayuda = "Población del domicilio fiscal; sale en las facturas como parte de la dirección del emisor.", Grupo = "ident", Longitud = 100 },
            new("ProvinciaFiscal", "Provincia") { Ayuda = "Provincia del domicilio fiscal; sale en las facturas como parte de la dirección del emisor.", Grupo = "ident", Longitud = 100 },
            new("Telefono", "Teléfono") { Ayuda = "Teléfono de la empresa; sale en sus facturas debajo del domicilio. Vacío: no sale.", Grupo = "ident", Longitud = 30, Ancho = "s", Sinonimos = ["telefono", "tel"] },
            .. CuentasCorreccion(),
            Ruta("RutaDocumentos", "Carpeta de documentos") with
            {
                Ayuda = "Ruta base de los documentos de la empresa (en pruebas, por ejemplo, D:\\SynologyBox\\SynologyDrive). Los PDF de las facturas de venta van a " +
                    "<carpeta>\\<ejercicio>\\FacturasVenta\\<número>-<razón social>.pdf y los de compra se buscan en <carpeta>\\<carpeta de facturas de compra del ejercicio>.",
                Sinonimos = ["ruta base", "raiz", "pdf", "proveedores"],
            },
            Ruta("Logo", "Logo de las facturas") with
            {
                Ayuda = "Fichero JPG o PNG con el logo que sale arriba a la izquierda en las facturas y proformas. Si la ruta es relativa (por ejemplo, Logos\\logo.jpg), " +
                    "se busca dentro de la carpeta de documentos. Vacío o si no se encuentra: en su lugar sale el nombre de la empresa.",
                Sinonimos = ["logotipo", "imagen", "marca"],
            },
        ],
        [new("IdSociedad", "Código", 64), new("Nombre", "Nombre"), new("CIF", "CIF", 96)])
    {
        Orden = "Nombre",
        GruposCampos = GruposEmpresa,
        Alias = ["sociedad", "sociedades", "cif", "compania"],
        Usos = [new("SociedadesEjercicios", "IdSociedad", "ejercicios"), new("Conceptos", "IdSociedad", "conceptos"), new("CuentasClientesSociedad", "IdSociedad", "cuentas de clientes")],
        Titular = r => r.EsNuevo ? "Nueva empresa" : r.Texto("Nombre") ?? "Empresa sin nombre",
        Subtitular = r => r.Texto("IdSociedad") is string c ? $"Código {c}" : null,
        Reglas = r => r.Texto("IdSociedad") is string c && c.Length != 3 ? [new ErrorCampo("IdSociedad", "El código de la empresa tiene tres caracteres.")] : [],
    };

    /// <summary>Tipos de retención que se ofrecen en las facturas recibidas (organizacion/sociedades-ejercicios 2026-11-06).</summary>
    public static ProveedorTablaAccess TiposRetencion(IBaseDatos bd) => new(bd, "tiposretencion", "Tipos de retención", "Catálogos",
        "Porcentajes de retención de IRPF que se ofrecen en las facturas recibidas, con su clase y vigencia.", AmbitoMantenimiento.Compartido, "tipo de retención",
        "TiposRetencion", "IdTipoRetencion", ClaveNueva.Autonumerica,
        [
            new("Porcentaje", "Porcentaje", TipoCampo.Porcentaje) { Obligatorio = true, Ancho = "xs", Ayuda = "Por ejemplo 15 para el 15 %." },
            new("Descripcion", "Descripción") { Obligatorio = true, Longitud = 100, Ancho = "l", Ayuda = "Sale junto al porcentaje en el desplegable de la factura." },
            new("Clase", "Clase", TipoCampo.Lista) { Ayuda = "Modelo en el que se declara la retención: 111 para actividades y profesionales, 115 para arrendamientos.", Obligatorio = true, Ancho = "m",
                Opciones = [new OpcionCampo((byte)1, "Actividades y profesionales (modelo 111)"), new OpcionCampo((byte)2, "Arrendamientos (modelo 115)")] },
            new("VigenteDesde", "Vigente desde", TipoCampo.Fecha) { Ancho = "s", Ayuda = "Vacía: sin límite. Los tipos cambian por ley; da de alta el nuevo con su fecha en vez de cambiar el antiguo." },
            new("VigenteHasta", "Vigente hasta", TipoCampo.Fecha) { Ancho = "s", Ayuda = "Vacía: sin límite." },
            new("Activo", "Activo", TipoCampo.Logico) { Ayuda = "Solo los activos y vigentes se ofrecen en las facturas." },
        ],
        [new("Porcentaje", "%", 60, Derecha: true), new("Descripcion", "Descripción"), new("VigenteDesde", "Desde", 90), new("VigenteHasta", "Hasta", 90), new("Activo", "Activo", 60)])
    {
        Orden = "Porcentaje DESC",
        Alias = ["retencion", "irpf", "111", "115", "alquiler"],
        Titular = r => r.EsNuevo ? "Nuevo tipo de retención" : $"{r.Decimal("Porcentaje"):0.##} % · {r.Texto("Descripcion")}",
    };

    public static ProveedorTablaAccess Ejercicios(IBaseDatos bd) => new(bd, "ejercicios", "Ejercicios", "Empresas y ejercicios",
        "Ejercicios de la empresa de trabajo con su forma de pago predeterminada, tipo de IVA general, prorrata, Impuesto sobre Sociedades, cuentas y carpeta de compras.", AmbitoMantenimiento.Sociedad, "ejercicio",
        "SociedadesEjercicios", "IdSociedadEjercicio", ClaveNueva.Autonumerica,
        [
            new("Ejercicio", "Ejercicio", TipoCampo.Codigo) { Grupo = "ident", Obligatorio = true, Longitud = 4, Ancho = "xs", Ayuda = "Cuatro cifras, por ejemplo 2026.", Sinonimos = ["año"] },
            new("Nombre", "Nombre") { Grupo = "ident", Longitud = 255, Ancho = "l",
                Ayuda = "Etiqueta del ejercicio en el selector (por ejemplo «DINAMICA 2026»). No sale en las facturas: el emisor es siempre la empresa (Empresas › Nombre y CIF)." },
            new("TrimestreActivo", "Trimestre activo", TipoCampo.Entero) { Grupo = "ident", Minimo = 1, Maximo = 4, Ancho = "xs", Sinonimos = ["trimestre"], SoloLectura = true,
                Ayuda = "Se cambia desde el aviso del trimestre de Compras, con el permiso «Cambiar el trimestre activo»." },
            new("Activa", "Activo", TipoCampo.Logico) { Grupo = "ident", Ayuda = "Se ofrece al elegir el ejercicio de trabajo. No indica si está cerrado." },
            new("EjercicioCerrado", "Cerrado", TipoCampo.Logico) { Grupo = "ident", SoloLectura = true, Ayuda = "Lo cambia el proceso de cierre de contabilidad, no este mantenimiento." },
            .. Predeterminados(),
            TipoGeneral(),
            ProrrataAnterior(),
            ProrrataDefinitiva(),
            .. Cuentas(), .. CuentasCompras(), .. CuentasLiquidacionIva(), .. Dividendos(), .. ImpuestoSociedades(), .. CuentasCierre(),
            Ruta("CarpetaFacturasCompra", "Carpeta de facturas de compra") with
            {
                Ayuda = "Dónde se buscan los PDF de compra de este ejercicio, dentro de la carpeta de documentos de la empresa (Empresas › Carpeta de documentos, la ruta base), por ejemplo 2026\\Proveedores. " +
                    "Vacía: <año>\\Proveedores. Una ruta completa (con unidad) se usa tal cual.",
                Sinonimos = ["proveedores", "pdf", "compras"],
            },
        ],
        [new("Ejercicio", "Ejercicio", 76), new("Nombre", "Nombre"), new("Activa", "Activo", 60), new("EjercicioCerrado", "Cerrado", 66)])
    {
        ColumnaAmbito = "IdSociedad",
        Orden = "Ejercicio DESC",
        GruposCampos = GruposEjercicio,
        Alias = ["ejercicio", "año", "trimestre", "prorrata", "suplido", "retencion", "dividendos", "reservas", "sociedades", "impuesto"],
        Usos = [new("SeriesFacturacion", "IdSociedadEjercicio", "series"), new("Facturas", "IdSociedadEjercicio", "facturas"),
            new("CuentasContables", "IdSociedadEjercicio", "cuentas contables"), new("Asientos", "IdSociedadEjercicio", "asientos")],
        Titular = r => r.EsNuevo ? "Nuevo ejercicio" : r.Texto("Nombre") ?? $"Ejercicio {r.Texto("Ejercicio")}",
        Subtitular = r => r.Texto("Ejercicio") is string e ? $"Ejercicio {e}{(r.Logico("EjercicioCerrado") ? " · cerrado" : "")}" : null,
        Reglas = r => r.Texto("Ejercicio") is string e && !(e.Length == 4 && e.All(char.IsAsciiDigit)) ? [new ErrorCampo("Ejercicio", "El ejercicio es un año de cuatro cifras, por ejemplo 2026.")] : [],
        AntesDeGuardar = async (s, r, c) =>
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ? AND IdSociedadEjercicio <> ?",
                c.Trabajo.IdSociedad, r.Texto("Ejercicio"), r.Clave is null ? -1 : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture) > 0
                ? new Error("Mantenimiento.EjercicioRepetido", $"La empresa ya tiene el ejercicio {r.Texto("Ejercicio")}.", null, null, "Abre ese ejercicio en la lista.")
                : null,
    };
}

/// <summary>Formas de pago a través de su catálogo, que es la única vía de escritura de la tabla (organizacion/formas-pago).</summary>
public sealed class ProveedorFormasPago(IBaseDatos bd, ICatalogoFormasPago catalogo) : ProveedorTablaAccess(bd, "formaspago", "Formas de pago", "Catálogos",
    "Formas de cobro y pago que se ofrecen en facturas, contratos y empresas.", AmbitoMantenimiento.Compartido, "forma de pago",
    "FormasPago", "Id", ClaveNueva.Siguiente,
    [
        new("Id", "Código", TipoCampo.Entero) { SoloLectura = true, Ancho = "xs", Ayuda = "Se asigna al crear la forma de pago." },
        new("Descripcion", "Descripción") { Ayuda = "Nombre de la forma de pago tal como sale en las facturas y en los desplegables (por ejemplo «Transferencia a 30 días»).", Obligatorio = true, Longitud = CatalogoFormasPagoAccess.LongitudDescripcion, Ancho = "l" },
        new("SEPA", "Domiciliación SEPA", TipoCampo.Logico) { Ayuda = "Se cobra por recibo domiciliado." },
    ],
    [new("Id", "Código", 64, Derecha: true), new("Descripcion", "Descripción"), new("SEPA", "SEPA", 60)])
{
    public override IReadOnlyList<string> Sinonimos => ["cobro", "pago", "transferencia", "recibo", "sepa"];
    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nueva forma de pago" : r.Texto("Descripcion") ?? $"Forma de pago {r.Clave}";
    public override string? SubtituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? null : $"Código {r.Clave}";

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto)
    {
        Resultado r;
        object? clave = registro.Clave;
        if (registro.EsNuevo)
        {
            var alta = await catalogo.AltaAsync(registro.Texto("Descripcion") ?? "", registro.Logico("SEPA"));
            r = alta;
            if (alta.EsCorrecto) clave = alta.Valor.Id;
        }
        else
            r = await catalogo.ModificarAsync(new FormaPago(Convert.ToByte(registro.Clave, CultureInfo.InvariantCulture), registro.Texto("Descripcion") ?? "", registro.Logico("SEPA")));
        if (!r.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(r.Errores);
        var guardado = (await ListarAsync(contexto)).First(x => MismaClave(x.Clave, clave));
        return Resultado<RegistroMantenimiento>.Correcto(guardado);
    }

    public override Task<Resultado> EliminarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
        catalogo.BajaAsync(Convert.ToByte(registro.Clave, CultureInfo.InvariantCulture));
}

/// <summary>
/// Subcuenta de cada forma de pago en la empresa de trabajo (FormasPagoSociedad, organizacion/formas-pago 2026-11-06). Lista
/// todas las formas de pago; solo se cambia la cuenta. Las formas de pago se crean y borran en «Formas de pago».
/// </summary>
public sealed class ProveedorFormasPagoSociedad(CatalogoFormasPagoSociedad catalogo) : ProveedorBase
{
    public override string Clave => "formaspagosociedad";
    public override string Titulo => "Cuentas de las formas de pago";
    public override string Categoria => "Empresas y ejercicios";
    public override string Finalidad => "Subcuenta de tesorería o de efectos y cuenta bancaria de cobro de cada forma de pago en la empresa de trabajo.";

    /// <summary>Cuentas bancarias de la empresa de trabajo, leídas al listar: son las opciones del campo «Cuenta bancaria».</summary>
    private IReadOnlyList<CuentaBancariaEmpresa> _bancos = [];
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "forma de pago";
    public override IReadOnlyList<string> Sinonimos => ["forma de pago", "cuenta de pago", "banco", "caja", "572", "tesoreria", "iban", "transferencia"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Descripcion", "Forma de pago") { Ayuda = "La forma de pago a la que se asigna la subcuenta. Se crea y se renombra en «Formas de pago».", SoloLectura = true, Ancho = "l" },
        new("Cuenta", "Subcuenta", TipoCampo.Cuenta) { Longitud = 30, Ancho = "s", Sinonimos = ["cuenta", "572", "banco"], GruposCuenta = ["57", "431"],
            Ayuda = "Código en el ejercicio de trabajo. Se guarda su identidad contable, así vale en los demás ejercicios aunque cambie el código. Vacía, la forma de pago no tiene subcuenta en esta empresa." },
        new("NombreCuenta", "Nombre de la cuenta") { Ayuda = "Nombre de la subcuenta en el ejercicio de trabajo, para comprobar que es la buena. Se rellena solo.", SoloLectura = true, Ancho = "l" },
        new("CuentaBancaria", "Cuenta bancaria de cobro", TipoCampo.Lista) { Ancho = "l", Sinonimos = ["iban", "transferencia", "banco"],
            Opciones = [new OpcionCampo(null, "(sin cuenta bancaria)"), .. _bancos.Select(b => new OpcionCampo(b.IdCuentaBancaria, b.Activa ? b.Texto : b.Texto + " (inactiva)"))],
            Ayuda = "Cuenta de la empresa en la que se cobra esta forma de pago: su IBAN sale entero en las facturas para que el cliente pague por transferencia. " +
                "Vacía en las que no se cobran en una cuenta (por ejemplo, metálico o recibo domiciliado). Las cuentas se dan de alta en Bancos." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Descripcion", "Forma de pago"), new("Cuenta", "Subcuenta", 90), new("NombreCuenta", "Nombre de la cuenta"), new("CuentaBancaria", "Cuenta bancaria de cobro")];

    public override string TituloRegistro(RegistroMantenimiento r) => r.Texto("Descripcion") ?? $"Forma de pago {r.Clave}";
    public override string? SubtituloRegistro(RegistroMantenimiento r) => $"Código {r.Clave}";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        _bancos = await catalogo.CuentasBancariasAsync(c.Trabajo.IdSociedad);
        return (await catalogo.ListarAsync(c.Trabajo)).Select(AsRegistro).ToList();
    }

    private static RegistroMantenimiento AsRegistro(FormaPagoSociedad x) => new(x.IdFormaPago, new Dictionary<string, object?>
    {
        ["Descripcion"] = x.Descripcion, ["Cuenta"] = x.Cuenta, ["CuentaBancaria"] = x.IdCuentaBancaria, ["_CuentaBancariaOriginal"] = x.IdCuentaBancaria,
        ["NombreCuenta"] = x.Cuenta is null ? null : x.CuentaEnEjercicio ? x.NombreCuenta : $"{x.NombreCuenta} (no existe en este ejercicio)",
        ["_CuentaOriginal"] = x.Cuenta,
    });

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = Convert.ToByte(r.Clave, CultureInfo.InvariantCulture);
        var cuenta = r.Texto("Cuenta");
        // Si no se tocó una cuenta que no existe en el ejercicio, se conserva su identidad.
        if (cuenta is null || cuenta != r["_CuentaOriginal"] as string)
        {
            var g = await catalogo.AsignarAsync(id, cuenta, c.Trabajo);
            if (!g.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(g.Errores);
        }
        var banco = r["CuentaBancaria"] is null ? (int?)null : Convert.ToInt32(r["CuentaBancaria"], CultureInfo.InvariantCulture);
        var bancoOriginal = r["_CuentaBancariaOriginal"] is null ? (int?)null : Convert.ToInt32(r["_CuentaBancariaOriginal"], CultureInfo.InvariantCulture);
        if (banco != bancoOriginal)
        {
            var b = await catalogo.AsignarCuentaBancariaAsync(id, banco, c.Trabajo);
            if (!b.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(b.Errores);
        }
        var guardado = (await catalogo.ListarAsync(c.Trabajo)).First(x => x.IdFormaPago == id);
        return Resultado<RegistroMantenimiento>.Correcto(AsRegistro(guardado));
    }
}
