using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>
/// Relaciones que faltaban en la BD (datos/esquema 2026-11-24): columnas Id* que apuntaban a otra tabla sin que Access lo hiciera
/// cumplir. Las de los activos financieros venían de la maestra del ProConta antiguo sin claves foráneas; el resto se fueron quedando
/// sin relación al añadir columnas. No se crean las que apuntan a Sociedades, Facturas o SociedadesEjercicios (Access no admite más
/// de 32 índices y relaciones por tabla y esas tres ya están en el máximo), ni las de tablas vinculadas, copias históricas o columnas
/// que apuntan a tablas distintas según el caso.
/// </summary>
public static class MigracionesIntegridad
{
    /// <summary>Qué hace Access con la fila hija al borrar la fila padre.</summary>
    public enum AlBorrar
    {
        /// <summary>No deja borrar el padre mientras tenga hijas (la regla habitual).</summary>
        Impedir,

        /// <summary>
        /// Deja vacía la referencia. Para los asientos que genera un proceso: el motor de asientos borra el asiento al deshacer o
        /// regenerar el lote y después el proceso limpia su propia fila (TrabajoLote.Despues), así que impedirlo rompería el proceso.
        /// </summary>
        Vaciar,

        /// <summary>Borra también la fila hija. Solo cuando la referencia es obligatoria y la fila no tiene sentido sin el padre.</summary>
        Cascada,
    }

    /// <summary>Una relación: tabla y columna hija, tabla y columna padre, y qué pasa al borrar el padre.</summary>
    public sealed record RelacionPendiente(string Nombre, string Tabla, string Columna, string TablaPadre, string ColumnaPadre, AlBorrar AlBorrar = AlBorrar.Impedir)
    {
        public string Sql => $"ALTER TABLE [{Tabla}] ADD CONSTRAINT [{Nombre}] FOREIGN KEY ([{Columna}]) REFERENCES [{TablaPadre}] ([{ColumnaPadre}])" +
            AlBorrar switch { AlBorrar.Vaciar => " ON DELETE SET NULL", AlBorrar.Cascada => " ON DELETE CASCADE", _ => "" };

        /// <summary>Filas hijas que apuntan a un padre que no existe (el 0 cuenta como huérfano: Access no lo admitiría).</summary>
        public string SqlHuerfanos => $"SELECT h.[{Columna}] FROM [{Tabla}] AS h WHERE h.[{Columna}] IS NOT NULL AND NOT EXISTS " +
            $"(SELECT 1 FROM [{TablaPadre}] AS p WHERE p.[{ColumnaPadre}] = h.[{Columna}])";
    }

    private const string A = "Activos";
    private const string As = "Asientos";
    private const string Cc = "CentrosCoste";
    private const string Ic = "IdentidadesContables";
    private const string Fc = "FacturasCompra";

    /// <summary>Las relaciones, en el orden en que se crean.</summary>
    public static IReadOnlyList<RelacionPendiente> Relaciones { get; } =
    [
        // A. Activos e inversiones.
        new("FK_OperacionesInversion_Activo", "OperacionesInversion", "IdActivo", A, "IdActivo"),
        new("FK_ActivosInversiones_Activo", "ActivosInversiones", "IdActivo", A, "IdActivo"),
        new("FK_ActivosInversiones_Instrumento", "ActivosInversiones", "IdInstrumentoFinanciero", "InstrumentosFinancieros", "IdInstrumentoFinanciero"),
        new("FK_CotizacionesInstrumento_Instrumento", "CotizacionesInstrumentoFinanciero", "IdInstrumentoFinanciero", "InstrumentosFinancieros", "IdInstrumentoFinanciero"),
        new("FK_Activos_TipoActivo", A, "IdTipoActivo", "TiposActivo", "IdTipoActivo"),
        new("FK_Activos_FacturaCompra", A, "IdFacturaCompra", Fc, "IdRegistro"),
        new("FK_PlanesAmortizacionActivo_Activo", "PlanesAmortizacionActivo", "IdActivo", A, "IdActivo"),
        new("FK_DetallesLoteAmortizacion_Activo", "DetallesLoteAmortizacionActivo", "IdActivo", A, "IdActivo"),
        new("FK_DetallesLoteAmortizacion_Lote", "DetallesLoteAmortizacionActivo", "IdLoteAmortizacion", "LotesAmortizacionActivo", "IdLoteAmortizacion"),

        // B. Asientos generados por procesos: al borrar el asiento, la referencia queda vacía (ver AlBorrar.Vaciar).
        new("FK_DetallesLoteAmortizacion_Asiento", "DetallesLoteAmortizacionActivo", "IdAsiento", As, "IdAsiento", AlBorrar.Vaciar),
        new("FK_ImpuestosSociedades_Asiento", "ImpuestosSociedades", "IdAsiento", As, "IdAsiento", AlBorrar.Vaciar),
        new("FK_LiquidacionesIva_Asiento", "LiquidacionesIva", "IdAsiento", As, "IdAsiento", AlBorrar.Vaciar),
        new("FK_RepartosDividendos_Asiento", "RepartosDividendos", "IdAsiento", As, "IdAsiento", AlBorrar.Vaciar),
        new("FK_AplicacionesResultado_Asiento", "AplicacionesResultado", "IdAsiento", As, "IdAsiento", AlBorrar.Vaciar),
        new("FK_AplicacionesResultado_Reparto", "AplicacionesResultado", "IdRepartoDividendos", "RepartosDividendos", "IdRepartoDividendos", AlBorrar.Vaciar),
        // IdAsiento es obligatorio: sin su asiento, la contabilización del movimiento no existe (el movimiento vuelve a estar pendiente).
        new("FK_ContabilizacionesLinea_Asiento", "ContabilizacionesLineaExtracto", "IdAsiento", As, "IdAsiento", AlBorrar.Cascada),
        new("FK_AdjuntosRecibidosCorreo_Asiento", "AdjuntosRecibidosCorreo", "IdAsiento", As, "IdAsiento", AlBorrar.Vaciar),
        new("FK_AdjuntosRecibidosCorreo_FacturaCompra", "AdjuntosRecibidosCorreo", "IdFacturaCompra", Fc, "IdRegistro", AlBorrar.Vaciar),
        new("FK_FacturasCompra_AsientoContable", Fc, "IdAsientoContable", As, "IdAsiento", AlBorrar.Vaciar),
        new("FK_FacturasCompra_AsientoAnulado", Fc, "IdAsientoAnulado", As, "IdAsiento", AlBorrar.Vaciar),

        // C. Contabilidad, compras, bancos y ventas.
        new("FK_Apuntes_Cliente", "Apuntes", "IdCliente", "Clientes", "IdCliente"),
        new("FK_Apuntes_Proveedor", "Apuntes", "IdProveedor", "Proveedores", "IdProveedor"),
        new("FK_CentrosCoste_Raiz", Cc, "IdCentroCosteRaiz", Cc, "IdCentroCoste"),
        new("FK_CodigosReservados_Identidad", "CodigosContablesReservados", "IdIdentidadContable", Ic, "IdIdentidadContable"),
        new("FK_Conceptos_CentroCoste", "Conceptos", "IdCentroCoste", Cc, "IdCentroCoste"),
        new("FK_Conceptos_Identidad", "Conceptos", "IdIdentidadContable", Ic, "IdIdentidadContable"),
        new("FK_CCCB_IdentidadCuentaPuente", "ConfiguracionesContablesCuentaBancaria", "IdIdentidadCuentaPuente", Ic, "IdIdentidadContable"),
        new("FK_CCCB_IdentidadGastosDivisa", "ConfiguracionesContablesCuentaBancaria", "IdIdentidadGastosDivisa", Ic, "IdIdentidadContable"),
        new("FK_CCCB_IdentidadSplit", "ConfiguracionesContablesCuentaBancaria", "IdIdentidadSplit", Ic, "IdIdentidadContable"),
        new("FK_CuentasContables_CentroCoste", "CuentasContables", "IdCentroCostePredeterminado", Cc, "IdCentroCoste"),
        new("FK_CuentasContables_Padre", "CuentasContables", "IdCuentaPadre", "CuentasContables", "IdCuentaContable"),
        new("FK_CuentasContables_Identidad", "CuentasContables", "IdIdentidadContable", Ic, "IdIdentidadContable"),
        new("FK_CuentasPlanContable_Padre", "CuentasPlanContable", "IdCuentaPadre", "CuentasPlanContable", "IdCuentaPlanContable"),
        new("FK_CuentasPlanContable_Plan", "CuentasPlanContable", "IdPlanContable", "PlanesContables", "IdPlanContable"),
        new("FK_CuentasPlanContable_Tipo", "CuentasPlanContable", "IdTipoCuentaContable", "TiposCuentaContable", "IdTipoCuentaContable"),
        new("FK_CPS_CentroCosteDefecto", "CuentasProveedoresSociedad", "IdCentroCosteDefecto", Cc, "IdCentroCoste"),
        new("FK_CPS_IdentidadGastoDefecto", "CuentasProveedoresSociedad", "IdIdentidadGastoDefecto", Ic, "IdIdentidadContable"),
        new("FK_IdentidadesContables_Tipo", Ic, "IdTipoCuentaContable", "TiposCuentaContable", "IdTipoCuentaContable"),
        new("FK_Extractos_LineaOrigen", "ExtractosBancarios", "IdLineaExtractoOrigen", "LineasExtractoBancario", "IdLineaExtractoBancario"),
        new("FK_ImputacionesLinea_CentroCoste", "ImputacionesLineaExtractoBancario", "IdCentroCoste", Cc, "IdCentroCoste"),
        new("FK_ImputacionesLinea_PalabraClave", "ImputacionesLineaExtractoBancario", "IdPalabraClaveBancaria", "PalabrasClaveBancarias", "IdPalabraClaveBancaria"),
        new("FK_FacturasCompra_CentroCoste", Fc, "IdCentroCoste", Cc, "IdCentroCoste"),
        new("FK_FacturasCompra_Proveedor", Fc, "IdProveedor", "Proveedores", "IdProveedor"),
        new("FK_FacturasCompra_Rectificada", Fc, "IdRegistroFacturaRectificada", Fc, "IdRegistro"),
        new("FK_LineasFactura_CentroCoste", "LineasFactura", "IdCentroCoste", Cc, "IdCentroCoste"),
        new("FK_LineasFacturasProgramadas_CentroCoste", "LineasFacturasProgramadas", "IdCentroCoste", Cc, "IdCentroCoste"),
        new("FK_ReglasVenta_Concepto", "ReglasVenta", "IdConcepto", "Conceptos", "IdConcepto"),
        new("FK_VentasPorFacturar_Concepto", "VentasPorFacturar", "IdConcepto", "Conceptos", "IdConcepto"),
        new("FK_MensajesConversacionIA_Conversacion", "MensajesConversacionIA", "IdConversacion", "ConversacionesIA", "IdConversacion"),
        // El justificante es un documento del gestor: si se borra, la presentación se conserva sin él.
        new("FK_PresentacionesAeat_Documento", "PresentacionesAeat", "IdDocumento", "Documentos", "IdDocumento", AlBorrar.Vaciar),
        new("FK_MensajesCorreo_Original", "MensajesCorreo", "IdMensajeOriginal", "MensajesCorreo", "IdMensajeCorreo"),
    ];

    /// <summary>Relaciones que faltaban (datos/esquema 2026-11-24 18:21), con la comprobación previa de huérfanos.</summary>
    public static Migracion RelacionesPendientes { get; } = new(
        "20261124_1900_RELACIONES_PENDIENTES", 261124190,
        "Relaciones que faltaban entre tablas (activos financieros, asientos de los procesos, cuentas, centros de coste, compras, bancos y ventas).",
        "datos/esquema",
        Relaciones.Select(Paso).ToArray())
    {
        Precondicion = ComprobarHuerfanosAsync,
    };

    private static PasoPersonalizado Paso(RelacionPendiente r) =>
        new($"Relacionar {r.Tabla}.{r.Columna} con {r.TablaPadre} ({r.Nombre})", r.Sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, r.Tabla, r.Nombre)),
            (s, _) => s.EjecutarAsync(r.Sql));

    /// <summary>
    /// Antes de cambiar nada: si alguna relación pendiente tiene filas huérfanas, no se crea ninguna y se listan (no se borra nada).
    /// </summary>
    internal static async Task<Error?> ComprobarHuerfanosAsync(ISesionDatos s, ContextoMigracion _)
    {
        var esquema = s.LeerEsquema();
        var lineas = new List<string>();
        var total = 0;
        foreach (var r in Relaciones)
        {
            if (MigracionesOrganizacion.TieneRelacion(esquema, r.Tabla, r.Nombre)) continue;
            // La comprobación previa corre antes de todas las migraciones pendientes: una columna que aún no existe la crea vacía una
            // migración anterior del catálogo, así que no puede tener huérfanos.
            if (!esquema.ExisteColumna(r.Tabla, r.Columna) || !esquema.ExisteColumna(r.TablaPadre, r.ColumnaPadre)) continue;
            var valores = await s.ConsultarAsync(r.SqlHuerfanos, f => Convert.ToString(f.Valor(r.Columna), CultureInfo.InvariantCulture) ?? "");
            if (valores.Count == 0) continue;
            total += valores.Count;
            var distintos = valores.Distinct().ToList();
            lineas.Add($"{r.Tabla}.{r.Columna} → {r.TablaPadre}: {valores.Count} fila(s) con {string.Join(", ", distintos.Take(10))}" +
                (distintos.Count > 10 ? $" y {distintos.Count - 10} valor(es) más" : ""));
        }
        if (lineas.Count == 0) return null;
        return new Error("Esquema.RelacionesConHuerfanos",
            $"No puedo crear las relaciones que faltan: hay {total} fila(s) que apuntan a registros que no existen. No he cambiado nada.",
            string.Join("; ", lineas),
            "Esas filas guardan un Id que no está en la tabla a la que apuntan (se borró el registro o vino así de una importación).",
            "Corrige o vacía esas referencias en la BD (o pide ayuda al responsable) y vuelve a ejecutar el ActualizadorEsquema.");
    }
}
