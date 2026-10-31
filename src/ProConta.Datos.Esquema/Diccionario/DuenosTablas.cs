namespace ProConta.Datos.Esquema.Diccionario;

/// <summary>Dueño funcional de una tabla: el área del diccionario y el nodo de docs que la escribe.</summary>
public sealed record DuenoTabla(string Area, string Nodo);

/// <summary>
/// Matriz de dueño funcional por tabla (spec § 30 paso 1). Solo el dueño escribe en su tabla; los
/// demás la usan a través de su contrato. Una tabla que no esté aquí sale en «sin-dueno.md» del
/// diccionario, para que se le asigne uno.
/// </summary>
public static class DuenosTablas
{
    private static readonly Dictionary<string, DuenoTabla> _duenos = new(StringComparer.OrdinalIgnoreCase);

    static DuenosTablas()
    {
        Asignar("datos", "datos/esquema", "VersionesEsquema", "LogMigracionesEsquema");
        Asignar("plataforma", "plataforma/mantenimiento", "Configuracion", "AliasCamposConfiguracion");
        Asignar("organizacion", "organizacion/sociedades-ejercicios", "Sociedades", "SociedadesEjercicios");
        Asignar("organizacion", "organizacion/formas-pago", "FormasPago");
        Asignar("seguridad", "seguridad/permisos", "GruposOperadores", "OperadoresGrupos", "AccesoSociedadesGrupo", "RestriccionesReportsGrupo");
        Asignar("seguridad", "seguridad/acceso", "OperadoresSeguridad", "AuditoriaSeguridad", "PreferenciasAccesoOperador", "PreferenciasAccesoEjercicio");
        Asignar("terceros", "terceros/tercero", "Terceros", "HistorialTerceros", "MapasOrigenTerceros", "DomiciliosTercero");
        Asignar("terceros", "terceros/roles", "Clientes", "Proveedores", "CuentasClientesEjercicio", "CuentasProveedoresEjercicio");
        Asignar("terceros", "terceros/contactos", "Contactos", "ContactosLibresTercero", "DatosContacto", "RelacionesContactos");
        Asignar("terceros", "terceros/perfiles-notas", "Perfiles", "PerfilesTerceros");
        Asignar("contabilidad", "contabilidad/cuenta-contable", "CuentasContables", "IdentidadesContables", "CodigosContablesReservados",
            "CuentasContablesLegacy", "CuentasPlanContable", "PlanesContables", "TiposCuentaContable");
        Asignar("contabilidad", "contabilidad/centros-coste", "CentrosCoste", "F_CDP", "F_DEP");
        Asignar("contabilidad", "contabilidad/asientos", "Asientos", "Apuntes", "RelacionesAsientos", "RevisionesAsientos");
        Asignar("contabilidad", "contabilidad/procesos", "ConfiguracionesContables", "ConfiguracionesContablesCuentaBancaria", "PeriodosIVA", "IVARepercutido", "IVASoportado");
        Asignar("contabilidad", "contabilidad/modelos-tributarios", "PresentacionesTributarias");
        Asignar("contabilidad", "contabilidad/balances", "ModelosBalance", "NodosBalance", "ReglasCuentasBalance", "RelacionesCalculoBalance");
        Asignar("facturacion", "facturacion/factura-venta", "Facturas", "LineasFactura", "DesglosesIVARepercutido", "SeriesFacturacion",
            "TiposFactura", "EstadosFactura", "TiposOperacion", "TiposDeIVA", "LotesExportacionContable");
        Asignar("facturacion", "facturacion/conceptos", "Conceptos", "TiposConcepto");
        Asignar("facturacion", "facturacion/contratos", "Contratos", "LineasContrato", "EstadoContratoTerceros");
        Asignar("facturacion", "facturacion/programadas", "FacturasProgramadas", "FacturasProgramadasBak", "LineasFacturasProgramadas", "LotesFacturasProgramadas");
        Asignar("facturacion", "facturacion/verifactu", "VeriFactuConfiguracion", "VeriFactuEnvios", "VeriFactuLotesEnvio", "VeriFactuRespuestas");
        Asignar("compras", "compras/factura-compra", "FacturasCompra", "DesglosesIVASoportado");
        Asignar("norma43", "norma43", "Bancos", "EntidadesFinancieras", "CuentasBancarias", "ExtractosBancarios", "LineasExtractoBancario",
            "ComplementosLineaExtractoBancario", "ContabilizacionesLineaExtracto", "ImputacionesLineaExtractoBancario",
            "IncidenciasImportacionBancaria", "LotesImportacionBancaria");
        Asignar("correo", "correo", "ConfiguracionCorreo", "PlantillasCorreo", "TiposCorreo", "EnviosCorreo",
            "ConfiguracionMailDispatcher", "ProcesamientosMailDispatcher", "AdjuntosMailDispatcher");
        Asignar("implantacion", "implantacion/importador-contasol", "ImportacionesContaSol", "FasesImportacionContaSol",
            "IncidenciasImportacionContaSol", "MapasIdentidadContaSol");
        Asignar("creative-lab", "creative-lab/activos", "Activos", "ActivosInversiones", "CotizacionesInstrumentoFinanciero", "CuentasActivo",
            "DetallesLoteAmortizacionActivo", "ImportacionesActivos", "InstrumentosFinancieros", "LotesAmortizacionActivo",
            "OperacionesInversion", "PlanesAmortizacionActivo", "PlantillasCuentasActivo", "TiposActivo", "Inmuebles");
        Asignar("creative-lab", "creative-lab/asistente-ia", "ConversacionesIA", "MensajesConversacionIA");
    }

    private static void Asignar(string area, string nodo, params string[] tablas)
    {
        foreach (var tabla in tablas) _duenos.Add(tabla, new DuenoTabla(area, nodo));
    }

    public static DuenoTabla? De(string tabla) => _duenos.GetValueOrDefault(tabla);

    public static IReadOnlyDictionary<string, DuenoTabla> Todos => _duenos;
}
