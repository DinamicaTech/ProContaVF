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

    internal static IReadOnlyList<GrupoCampos> GruposEmpresa =>
        [new("ident", "Identificación"), new("pred", "Valores predeterminados"), new("impuestos", "Impuestos y retenciones"), new("cuentas", "Cuentas"), new("rutas", "Rutas y bases de datos")];

    private static CampoMantenimiento Cuenta(string clave, string etiqueta, int longitud = 10, string? ayuda = null, params string[] sinonimos) =>
        new(clave, etiqueta, TipoCampo.Cuenta) { Grupo = "cuentas", Longitud = longitud, Ancho = "s", Ayuda = ayuda, Sinonimos = sinonimos };

    private static CampoMantenimiento Ruta(string clave, string etiqueta) => new(clave, etiqueta, TipoCampo.Ruta) { Grupo = "rutas", Longitud = 255, Ancho = "l" };

    internal static IEnumerable<CampoMantenimiento> Impuestos() =>
    [
        new("PorcentajeRetencion", "Porcentaje de retención", TipoCampo.Entero) { Grupo = "impuestos", Minimo = 0, Maximo = 100, Ancho = "xs", Ayuda = "Se guarda como número entero.", Sinonimos = ["irpf", "retencion"] },
        new("TipoIVA1", "Tipo de IVA 1 (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Ayuda = "Su cuenta de IVA repercutido es «Cuenta del tipo de IVA 1»." },
        new("TipoIVA2", "Tipo de IVA 2 (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs" },
        new("TipoIVA3", "Tipo de IVA 3 (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs" },
        new("TipoIVA4", "Tipo de IVA 4 (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs" },
    ];

    /// <summary>Solo en Ejercicios: el tipo general es del ejercicio (compras/pantallas 2026-11-06 12:10).</summary>
    internal static CampoMantenimiento TipoGeneral() =>
        new("TipoIVAGeneral", "Tipo de IVA general (%)", TipoCampo.Porcentaje) { Grupo = "impuestos", Ancho = "xs", Sinonimos = ["iva general", "21"],
            Ayuda = "El de la autorrepercusión (inversión del sujeto pasivo e intracomunitarias) y el de la importación en las compras. Vacío: el más alto de los tipos de IVA." };

    /// <summary>Cuentas de compras que solo tiene el ejercicio (compras/factura-compra/contabilizacion).</summary>
    internal static IEnumerable<CampoMantenimiento> CuentasCompras() =>
    [
        Cuenta("CuentaIVAImportacion", "Cuenta de IVA de importación", ayuda: "IVA soportado en importaciones (4724001 en el ERP): el importe de la factura se desglosa al tipo general."),
        Cuenta("CuentaRedondeoGastos", "Cuenta de diferencias de redondeo · gastos", ayuda: "Cuando a un asiento de compra le falta Debe (hasta 0,03 €, o más con motivo). Vacía: 6590000.", sinonimos: ["redondeo"]),
        Cuenta("CuentaRedondeoIngresos", "Cuenta de diferencias de redondeo · ingresos", ayuda: "Cuando a un asiento de compra le sobra Debe. Vacía: 7590000. Si no existe, se crea al usarla.", sinonimos: ["redondeo"]),
    ];

    internal static IEnumerable<CampoMantenimiento> Cuentas() =>
    [
        Cuenta("CuentaTipoIVA0", "Cuenta de IVA soportado del tipo 0", ayuda: "IVA soportado (472) de las compras al 0 %.", sinonimos: ["iva soportado"]),
        Cuenta("CuentaTipoIVA1", "Cuenta de IVA soportado del tipo 1", ayuda: "La usa la exportación de compras para el porcentaje del tipo de IVA 1.", sinonimos: ["iva soportado"]),
        Cuenta("CuentaTipoIVA2", "Cuenta de IVA soportado del tipo 2", sinonimos: ["iva soportado"]),
        Cuenta("CuentaTipoIVA3", "Cuenta de IVA soportado del tipo 3", sinonimos: ["iva soportado"]),
        Cuenta("CuentaTipoIVA4", "Cuenta de IVA soportado del tipo 4", sinonimos: ["iva soportado"]),
        Cuenta("CuentaIVARepercutido0", "Cuenta de IVA repercutido del tipo 0", ayuda: "Ventas al 0 %. Vacía: la 477 con la terminación de la de soportado (4720001 → 4770001).", sinonimos: ["iva repercutido"]),
        Cuenta("CuentaIVARepercutido1", "Cuenta de IVA repercutido del tipo 1", ayuda: "La usa la contabilización de ventas. Vacía: la 477 con la terminación de la de soportado.", sinonimos: ["iva repercutido"]),
        Cuenta("CuentaIVARepercutido2", "Cuenta de IVA repercutido del tipo 2", ayuda: "Vacía: la 477 con la terminación de la de soportado.", sinonimos: ["iva repercutido"]),
        Cuenta("CuentaIVARepercutido3", "Cuenta de IVA repercutido del tipo 3", ayuda: "Vacía: la 477 con la terminación de la de soportado.", sinonimos: ["iva repercutido"]),
        Cuenta("CuentaIVARepercutido4", "Cuenta de IVA repercutido del tipo 4", ayuda: "Vacía: la 477 con la terminación de la de soportado.", sinonimos: ["iva repercutido"]),
        Cuenta("CuentaSuplido", "Cuenta de suplido", 255, "La contabilización de ventas la usa en las líneas suplidas sin cuenta propia."),
        Cuenta("CuentaRetencionServicios", "Cuenta de retención de servicios"),
        Cuenta("CuentaRetencionAlquileres", "Cuenta de retención de alquileres"),
        Cuenta("CuentaIVAIntracomunitarioRepercutidoServicios", "Cuenta de IVA intracomunitario repercutido · servicios"),
        Cuenta("CuentaIVAIntracomunitarioRepercutidoBienes", "Cuenta de IVA intracomunitario repercutido · bienes"),
        Cuenta("CuentaIVAIntracomunitarioSoportadoServicios", "Cuenta de IVA intracomunitario soportado · servicios"),
        Cuenta("CuentaIVAIntracomunitarioSoportadoBienes", "Cuenta de IVA intracomunitario soportado · bienes"),
        Cuenta("CuentaIVAInversionSujetoPasivoRepercutido", "Cuenta de IVA por inversión del sujeto pasivo · repercutido"),
        Cuenta("CuentaIVAInversionSujetoPasivoSoportado", "Cuenta de IVA por inversión del sujeto pasivo · soportado"),
    ];

    private static IEnumerable<CampoMantenimiento> Predeterminados() =>
    [
        new("FormaPagoDefecto", "Forma de pago predeterminada (código)", TipoCampo.Entero) { Grupo = "pred", Ancho = "xs", Minimo = 0, Maximo = 255, Ayuda = "Código de Formas de pago que se propone en las facturas nuevas." },
    ];

    public static ProveedorTablaAccess Empresas(IBaseDatos bd) => new(bd, "sociedades", "Empresas", "Empresas y ejercicios",
        "Datos de cada empresa: identificación, valores predeterminados, impuestos, cuentas y rutas.", AmbitoMantenimiento.Compartido, "empresa",
        "Sociedades", "IdSociedad", ClaveNueva.Escrita,
        [
            new("IdSociedad", "Código", TipoCampo.Codigo) { Grupo = "ident", Obligatorio = true, SoloAlta = true, Longitud = 3, Ancho = "xs", Ayuda = "Tres caracteres. Se fija al crear la empresa." },
            new("Nombre", "Nombre") { Grupo = "ident", Obligatorio = true, Longitud = 255, Ancho = "l" },
            new("CIF", "CIF", TipoCampo.Codigo) { Grupo = "ident", Obligatorio = true, Longitud = 10, Ancho = "s", Sinonimos = ["nif"], Ayuda = "Es el NIF que figura como emisor en las facturas si el ejercicio no tiene uno propio." },
            new("DireccionFiscal", "Domicilio fiscal") { Grupo = "ident", Longitud = 255, Ancho = "l", Sinonimos = ["direccion", "domicilio"], Ayuda = "Sale en las facturas como domicilio del emisor y es obligatorio para validarlas. Se congela en cada factura al validarla." },
            new("CodigoPostalFiscal", "Código postal") { Grupo = "ident", Longitud = 20, Ancho = "xs" },
            new("PoblacionFiscal", "Población") { Grupo = "ident", Longitud = 100 },
            new("ProvinciaFiscal", "Provincia") { Grupo = "ident", Longitud = 100 },
            .. Predeterminados(), .. Impuestos(), .. Cuentas(),
            Ruta("RutaDocumentos", "Carpeta de documentos") with
            {
                Ayuda = "Ruta base de los documentos de la empresa (en pruebas, por ejemplo, D:\\SynologyBox\\SynologyDrive). Los PDF de las facturas de venta van a " +
                    "<carpeta>\\<ejercicio>\\FacturasVenta\\<número>-<razón social>.pdf y los de compra se buscan en <carpeta>\\<carpeta de facturas de compra del ejercicio>.",
                Sinonimos = ["ruta base", "raiz", "pdf", "proveedores"],
            },
            // Los campos del ERP que no usa ningún proceso no se muestran; sus columnas siguen en la BD (propietario 2026-11-11 17:34 y 18:08).
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
            new("Clase", "Clase", TipoCampo.Lista) { Obligatorio = true, Ancho = "m",
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
        "Ejercicios de la empresa de trabajo con sus valores, impuestos, cuentas y rutas.", AmbitoMantenimiento.Sociedad, "ejercicio",
        "SociedadesEjercicios", "IdSociedadEjercicio", ClaveNueva.Autonumerica,
        [
            new("Ejercicio", "Ejercicio", TipoCampo.Codigo) { Grupo = "ident", Obligatorio = true, Longitud = 4, Ancho = "xs", Ayuda = "Cuatro cifras, por ejemplo 2026.", Sinonimos = ["año"] },
            new("Nombre", "Nombre") { Grupo = "ident", Longitud = 255, Ancho = "l", Ayuda = "Nombre del emisor en las facturas de este ejercicio; vacío, el de la empresa." },
            new("CIF", "CIF", TipoCampo.Codigo) { Grupo = "ident", Longitud = 10, Ancho = "s", Ayuda = "NIF del emisor en las facturas de este ejercicio; vacío, el de la empresa." },
            new("TrimestreActivo", "Trimestre activo", TipoCampo.Entero) { Grupo = "ident", Minimo = 1, Maximo = 4, Ancho = "xs", Sinonimos = ["trimestre"], SoloLectura = true,
                Ayuda = "Se cambia desde el aviso del trimestre de Compras, con el permiso «Cambiar el trimestre activo»." },
            new("Activa", "Activo", TipoCampo.Logico) { Grupo = "ident", Ayuda = "Se ofrece al elegir el ejercicio de trabajo. No indica si está cerrado." },
            new("EjercicioCerrado", "Cerrado", TipoCampo.Logico) { Grupo = "ident", SoloLectura = true, Ayuda = "Lo cambia el proceso de cierre de contabilidad, no este mantenimiento." },
            .. Predeterminados(),
            .. Impuestos(),
            TipoGeneral(),
            .. Cuentas(), .. CuentasCompras(),
            // Los campos del ERP que no usa ningún proceso no se muestran; sus columnas siguen en la BD hasta terminar la migración
            // del ERP (propietario 2026-11-11 17:34 y 18:08).
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
        GruposCampos = GruposEmpresa,
        Alias = ["ejercicio", "año", "trimestre", "prorrata", "cuentas de iva"],
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
        new("Descripcion", "Descripción") { Obligatorio = true, Longitud = CatalogoFormasPagoAccess.LongitudDescripcion, Ancho = "l" },
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
    public override string Finalidad => "Subcuenta de tesorería o de efectos que usa cada forma de pago en la empresa de trabajo.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "forma de pago";
    public override IReadOnlyList<string> Sinonimos => ["forma de pago", "cuenta de pago", "banco", "caja", "572", "tesoreria"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Descripcion", "Forma de pago") { SoloLectura = true, Ancho = "l" },
        new("Cuenta", "Subcuenta", TipoCampo.Cuenta) { Longitud = 30, Ancho = "s", Sinonimos = ["cuenta", "572", "banco"],
            Ayuda = "Código en el ejercicio de trabajo. Se guarda su identidad contable, así vale en los demás ejercicios aunque cambie el código. Vacía, la forma de pago no tiene subcuenta en esta empresa." },
        new("NombreCuenta", "Nombre de la cuenta") { SoloLectura = true, Ancho = "l" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Descripcion", "Forma de pago"), new("Cuenta", "Subcuenta", 90), new("NombreCuenta", "Nombre de la cuenta")];

    public override string TituloRegistro(RegistroMantenimiento r) => r.Texto("Descripcion") ?? $"Forma de pago {r.Clave}";
    public override string? SubtituloRegistro(RegistroMantenimiento r) => $"Código {r.Clave}";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) =>
        (await catalogo.ListarAsync(c.Trabajo)).Select(AsRegistro).ToList();

    private static RegistroMantenimiento AsRegistro(FormaPagoSociedad x) => new(x.IdFormaPago, new Dictionary<string, object?>
    {
        ["Descripcion"] = x.Descripcion, ["Cuenta"] = x.Cuenta,
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
        var guardado = (await catalogo.ListarAsync(c.Trabajo)).First(x => x.IdFormaPago == id);
        return Resultado<RegistroMantenimiento>.Correcto(AsRegistro(guardado));
    }
}
