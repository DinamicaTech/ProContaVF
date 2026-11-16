using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por la conciliación bancaria asistida (docs/norma43/conciliacion.md).</summary>
public static class MigracionesConciliacionN43
{
    /// <summary>Clave de proceso de la plantilla de sistema de la conciliación (la misma que ServicioConciliacionN43.ClaveProceso).</summary>
    public const string ClaveProceso = "norma43";

    public const string NombrePlantilla = "Movimiento bancario (Norma 43)";

    private const string DescripcionPlantilla =
        "Contabiliza un movimiento de un extracto bancario Norma 43: la cuenta 572 del banco contra la cuenta asignada al movimiento (o las cuentas, " +
        "si está repartido). Un cargo va al Debe de la contrapartida y al Haber del banco; un abono, al revés. Es una plantilla de sistema: la " +
        "ejecuta la conciliación bancaria y no se usa a mano.";

    private const string EjemploPlantilla =
        "{\"ejercicio\":2026,\"cuentas\":{\"5720001\":\"Banco Ejemplo\",\"6280001\":\"Suministros\",\"4300001\":\"Cliente Ejemplo, S.L.\"}," +
        "\"registros\":[{\"clave\":\"1.1\",\"fecha\":\"2026-03-02\",\"campos\":{\"CuentaBanco\":\"5720001\",\"NombreBanco\":\"Banco Ejemplo\",\"Contrapartida\":\"6280001\"," +
        "\"NombreContrapartida\":\"Suministros\",\"Importe\":120.50,\"Sentido\":\"D\",\"Concepto\":\"RECIBO LUZ EJEMPLO\",\"Centro\":null,\"Documento\":null}}]}";

    /// <summary>Líneas de la plantilla: (orden, cuenta, lado, importe, concepto, centro).</summary>
    private static readonly (short Orden, string Cuenta, string Lado, string Importe, string Concepto, string? Centro)[] Lineas =
    [
        (1, "CuentaBanco", "H", "Importe", "Si(Importe > 0; \"Pago a \"; \"Cobro de \") & NombreContrapartida", null),
        (2, "Contrapartida", "D", "Importe", "Si(Importe > 0; \"Pago desde \"; \"Ingreso en \") & NombreBanco", "Centro"),
    ];

    /// <summary>
    /// Conciliación bancaria (norma43/conciliacion 2026-11-13): la plantilla de sistema «Movimiento bancario (Norma 43)», que da los apuntes del
    /// asiento de cada línea, y un índice por asiento en ContabilizacionesLineaExtracto para saber de qué línea es un asiento.
    /// </summary>
    public static Migracion Conciliacion { get; } = new(
        "20261113_0900_CONCILIACION_N43", 261113001,
        "Norma 43: plantilla de sistema del asiento de cada movimiento bancario y búsqueda de la línea de un asiento.",
        "norma43/conciliacion",
        new CrearIndice("ContabilizacionesLineaExtracto", "IX_Contabilizaciones_Asiento",
            "CREATE INDEX IX_Contabilizaciones_Asiento ON ContabilizacionesLineaExtracto (IdAsiento)"),
        new PasoPersonalizado(
            "Dar de alta la plantilla de sistema «Movimiento bancario (Norma 43)»",
            "INSERT PlantillasAsiento (" + NombrePlantilla + ", SISTEMA, PROCESO " + ClaveProceso + ", FECHA_ORIGEN, agrupar) + PlantillasAsientoLineas " +
            string.Join("; ", Lineas.Select(l => $"{l.Orden} {l.Cuenta} {l.Lado} {l.Importe} {l.Concepto} {l.Centro}")) + "; ejemplo " + EjemploPlantilla,
            async (s, e) => e.Tabla("PlantillasAsiento") is not null && await IdPlantillaAsync(s) is not null,
            async (s, _) => await CargarPlantillaAsync(s)))
    {
        Descripciones =
        [
            new("ContabilizacionesLineaExtracto", null, "Asiento de cada movimiento bancario (línea N43) contabilizado: uno por línea."),
            new("ContabilizacionesLineaExtracto", "IdLineaExtractoBancario", "Movimiento bancario contabilizado (LineasExtractoBancario)."),
            new("ContabilizacionesLineaExtracto", "IdAsiento", "Asiento del movimiento: de ProContaVF (lote norma43/conciliacion, LINEA_N43, Id de la línea) o, en lo migrado del ERP, el de ContaSol."),
            new("ContabilizacionesLineaExtracto", "Estado", "CONTABILIZADA mientras el asiento existe. Deshacer la contabilización borra la fila."),
            new("ContabilizacionesLineaExtracto", "VersionProceso", "Versión de las reglas que generaron el asiento: 1 migrado del ERP, 2 conciliación de ProContaVF."),
            new("ContabilizacionesLineaExtracto", "FechaHora", "Cuándo se contabilizó (o se migró)."),
            new("ContabilizacionesLineaExtracto", "Operador", "Quién lo contabilizó (ERP en lo migrado)."),
        ],
    };

    /// <summary>
    /// Regla de ConceptosN43: los criterios (null = cualquiera) y lo que da. <c>Patron</c> es el texto del banco con * como comodín;
    /// <c>NombreDe</c> dice qué nombre sigue a la frase: CONTRAPARTIDA (el tercero o la cuenta) o BANCO (la cuenta bancaria).
    /// </summary>
    public sealed record ReglaConceptoN43(string? Codigo, string? Sentido, string? Entidad, string? Patron, string? PrefijoCuenta, short Prioridad,
        string Concepto, string ConceptoReducido, string Frase, string NombreDe = "CONTRAPARTIDA");

    // Conceptos comunes del cuaderno 43 de la AEB (nombre oficial), con el tipo y la frase de cada sentido.
    private static readonly (string Codigo, string Concepto, string TipoCargo, string FraseCargo, string TipoAbono, string FraseAbono)[] Aeb =
    [
        ("01", "Talones - Reintegros", "Reintegro", "Reintegro a", "Talón", "Ingreso de talón de"),
        ("02", "Abonarés - Entregas - Ingresos", "Entrega", "Entrega a", "Ingreso", "Ingreso de"),
        ("03", "Domiciliados - Recibos - Letras - Pagos por su cuenta", "Recibo", "Recibo de", "Domiciliación", "Domiciliación de"),
        ("04", "Giros - Transferencias - Traspasos - Cheques", "Transferencia", "Transferencia a", "Transferencia", "Transferencia de"),
        ("05", "Amortizaciones préstamos, créditos, etc.", "Préstamo", "Cuota de préstamo a", "Préstamo", "Préstamo de"),
        ("06", "Remesas efectos", "Remesa", "Remesa de efectos a", "Remesa", "Remesa de efectos de"),
        ("07", "Suscripciones - Dividendos pasivos - Canjes", "Suscripción", "Suscripción de valores a", "Canje", "Canje de valores de"),
        ("08", "Dividendos - Cupones - Amortizaciones", "Dividendo", "Pago de dividendo a", "Dividendo", "Dividendo de"),
        ("09", "Operaciones de bolsa y/o compra/venta valores", "Valores", "Compra de valores a", "Valores", "Venta de valores de"),
        ("10", "Cheques gasolina", "Gasolina", "Pago con cheque gasolina a", "Gasolina", "Abono de cheque gasolina de"),
        ("11", "Cajero automático", "Cajero", "Reintegro en cajero a", "Cajero", "Ingreso en cajero de"),
        ("12", "Tarjetas de crédito - Tarjetas débito", "Tarjeta", "Pago con tarjeta a", "Tarjeta", "Devolución de compra con tarjeta de"),
        ("13", "Operaciones extranjero", "Extranjero", "Pago al extranjero a", "Extranjero", "Cobro del extranjero de"),
        ("14", "Devoluciones e impagados", "Devolución", "Devolución de recibo de", "Devolución", "Devolución de"),
        ("15", "Nóminas - Seguros sociales", "Nómina o seguro", "Pago a", "Domiciliación", "Domiciliación de"),
        ("16", "Timbres - Corretaje - Póliza", "Timbres", "Gastos de póliza de", "Timbres", "Abono de"),
        ("17", "Intereses - Comisiones - Custodia - Gastos e impuestos", "Gastos", "Cargo de", "Intereses", "Abono de"),
        ("98", "Anulaciones - Correcciones asiento", "Anulación", "Anulación de", "Anulación", "Anulación de"),
        ("99", "Varios", "Varios", "Pago a", "Varios", "Cobro de"),
    ];

    /// <summary>
    /// Semilla de ConceptosN43 (norma43/conciliacion 2026-11-13 10:50): el estándar AEB (prioridad 100), las excepciones vistas en los
    /// extractos (200: el Santander pone 00 en tarjetas, transferencias y recibos y 11 en las compras con tarjeta; el 17 con «.COR» es un
    /// recibo) y las reglas por cuenta de destino (300: nóminas, traspasos y comisiones). El 00 sin regla no tiene tipo.
    /// </summary>
    public static IReadOnlyList<ReglaConceptoN43> SemillaConceptos { get; } =
    [
        .. Aeb.SelectMany(a => new[]
        {
            new ReglaConceptoN43(a.Codigo, "D", null, null, null, 100, a.Concepto, a.TipoCargo, a.FraseCargo),
            new ReglaConceptoN43(a.Codigo, "H", null, null, null, 100, a.Concepto, a.TipoAbono, a.FraseAbono),
        }),
        new("00", "D", "0049", "*TARJ*", null, 200, "Tarjetas de crédito - Tarjetas débito", "Tarjeta", "Pago con tarjeta a"),
        new("00", "H", "0049", "*DEVOLUCION COMPRA*", null, 200, "Tarjetas de crédito - Tarjetas débito", "Tarjeta", "Devolución de compra con tarjeta de"),
        new("00", "D", "0049", "*TRANSFERENCIA*", null, 200, "Giros - Transferencias - Traspasos - Cheques", "Transferencia", "Transferencia a"),
        new("00", "H", "0049", "*TRANSFERENCIA*", null, 200, "Giros - Transferencias - Traspasos - Cheques", "Transferencia", "Transferencia de"),
        new("00", "D", "0049", "*RECIBO*", null, 200, "Domiciliados - Recibos - Letras - Pagos por su cuenta", "Recibo", "Recibo de"),
        new(null, "D", null, "*DOMICILIACION IMPUESTO*", null, 210, "Domiciliados - Recibos - Letras - Pagos por su cuenta", "Impuesto", "Pago de impuestos a"),
        new("11", "D", "0049", "*TARJETA*", null, 200, "Tarjetas de crédito - Tarjetas débito", "Tarjeta", "Pago con tarjeta a"),
        new("11", "H", "0049", "*DEVOLUCION*", null, 200, "Tarjetas de crédito - Tarjetas débito", "Tarjeta", "Devolución de compra con tarjeta de"),
        new("17", "D", null, "*.COR*", null, 150, "Domiciliados - Recibos - Letras - Pagos por su cuenta", "Recibo", "Recibo de"),
        new(null, "D", null, null, "465", 300, "Nóminas - Seguros sociales", "Nómina", "Pago nómina a"),
        new(null, "D", null, null, "640", 300, "Nóminas - Seguros sociales", "Nómina", "Pago nómina a"),
        new(null, "D", null, null, "57", 300, "Giros - Transferencias - Traspasos - Cheques", "Traspaso", "Traspaso a"),
        new(null, "H", null, null, "57", 300, "Giros - Transferencias - Traspasos - Cheques", "Traspaso", "Traspaso de"),
        new(null, "D", null, null, "626", 300, "Intereses - Comisiones - Custodia - Gastos e impuestos", "Comisión", "Comisión bancaria", "BANCO"),
    ];

    /// <summary>Concepto del apunte en la plantilla de sistema: la descripción corta que calcula la conciliación (campo Concepto del registro).</summary>
    public const string FormulaConceptoApunte = "Concepto";

    /// <summary>
    /// Tipos de movimiento bancario (norma43/conciliacion 2026-11-13 10:50): la tabla ConceptosN43 con su semilla, y los dos apuntes de la
    /// plantilla de sistema pasan a llevar la descripción corta del movimiento (campo Concepto) en vez del «Pago a / Pago desde» del ERP.
    /// </summary>
    public static Migracion Conceptos { get; } = new(
        "20261113_1100_CONCEPTOS_N43", 261113002,
        "Norma 43: tipos de movimiento (conceptos comunes de la AEB y reglas) y concepto corto de los apuntes de la conciliación.",
        "norma43/conciliacion",
        new CrearTabla("ConceptosN43",
            "CREATE TABLE ConceptosN43 (IdConceptoN43 COUNTER CONSTRAINT PK_ConceptosN43 PRIMARY KEY, CodigoConcepto TEXT(2), Sentido TEXT(1), " +
            "Entidad TEXT(4), PatronTexto TEXT(100), PrefijoCuenta TEXT(12), Prioridad SHORT NOT NULL, Concepto TEXT(120) NOT NULL, " +
            "ConceptoReducido TEXT(40) NOT NULL, Frase TEXT(60) NOT NULL, NombreDe TEXT(15) NOT NULL, Activo YESNO NOT NULL)"),
        new PasoPersonalizado(
            "Cargar los conceptos comunes de la AEB y las reglas de los tipos de movimiento",
            "INSERT ConceptosN43 si está vacía: " + string.Join("; ", SemillaConceptos.Select(r =>
                $"{r.Codigo}/{r.Sentido}/{r.Entidad}/{r.Patron}/{r.PrefijoCuenta}/{r.Prioridad}/{r.ConceptoReducido}/{r.Frase}/{r.NombreDe}")),
            async (s, e) => e.Tabla("ConceptosN43") is not null && Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM ConceptosN43"),
                System.Globalization.CultureInfo.InvariantCulture) > 0,
            async (s, _) =>
            {
                foreach (var r in SemillaConceptos)
                    await s.EjecutarAsync(
                        "INSERT INTO ConceptosN43 (CodigoConcepto, Sentido, Entidad, PatronTexto, PrefijoCuenta, Prioridad, Concepto, ConceptoReducido, Frase, NombreDe, Activo) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        (object?)r.Codigo ?? DBNull.Value, (object?)r.Sentido ?? DBNull.Value, (object?)r.Entidad ?? DBNull.Value, (object?)r.Patron ?? DBNull.Value,
                        (object?)r.PrefijoCuenta ?? DBNull.Value, r.Prioridad, r.Concepto, r.ConceptoReducido, r.Frase, r.NombreDe, true);
            }),
        new PasoPersonalizado(
            "Poner la descripción corta en los dos apuntes de la plantilla «Movimiento bancario (Norma 43)»",
            "UPDATE PlantillasAsientoLineas SET FormulaConcepto = " + FormulaConceptoApunte + " (plantilla de proceso " + ClaveProceso + ")",
            async (s, _) => await IdPlantillaAsync(s) is not int id || Convert.ToInt32(await s.EscalarAsync(
                "SELECT COUNT(*) FROM PlantillasAsientoLineas WHERE IdPlantillaAsiento = ? AND (FormulaConcepto IS NULL OR FormulaConcepto <> ?)", id, FormulaConceptoApunte),
                System.Globalization.CultureInfo.InvariantCulture) == 0,
            async (s, _) =>
            {
                if (await IdPlantillaAsync(s) is int id)
                    await s.EjecutarAsync("UPDATE PlantillasAsientoLineas SET FormulaConcepto = ? WHERE IdPlantillaAsiento = ?", FormulaConceptoApunte, id);
            }))
    {
        Descripciones =
        [
            new("ConceptosN43", null, "Tipos de movimiento bancario: traduce cada línea N43 (concepto común, sentido, texto, entidad y cuenta de destino) a su tipo y a la frase de la descripción del asiento."),
            new("ConceptosN43", "CodigoConcepto", "Concepto común de la AEB (registro 22 del cuaderno 43, 00-99); vacío = cualquiera."),
            new("ConceptosN43", "Sentido", "D cargo, H abono; vacío = los dos."),
            new("ConceptosN43", "Entidad", "Código de la entidad bancaria del extracto (0049 Santander, 2100 La Caixa…); vacío = cualquiera."),
            new("ConceptosN43", "PatronTexto", "Texto que debe contener el concepto del banco, con * como comodín (sin distinguir mayúsculas); vacío = cualquiera."),
            new("ConceptosN43", "PrefijoCuenta", "Prefijo de la cuenta de destino (la imputación), p. ej. 465 para las nóminas; vacío = cualquiera."),
            new("ConceptosN43", "Prioridad", "Gana la regla aplicable de mayor prioridad (AEB 100, excepciones 150-210, por cuenta 300)."),
            new("ConceptosN43", "Concepto", "Nombre oficial del concepto común de la AEB."),
            new("ConceptosN43", "ConceptoReducido", "Tipo de movimiento que se muestra en la rejilla (Recibo, Transferencia, Tarjeta…)."),
            new("ConceptosN43", "Frase", "Principio de la descripción del asiento («Recibo de», «Transferencia a»…), seguido del nombre."),
            new("ConceptosN43", "NombreDe", "Qué nombre sigue a la frase: CONTRAPARTIDA (el tercero de la cuenta o la cuenta) o BANCO (la cuenta bancaria)."),
            new("ConceptosN43", "Activo", "Si la regla se aplica."),
        ],
    };

    private static async Task<int?> IdPlantillaAsync(ISesionDatos s) =>
        await s.EscalarAsync("SELECT TOP 1 IdPlantillaAsiento FROM PlantillasAsiento WHERE ClaveProceso = ? AND IdSociedad IS NULL", ClaveProceso) is { } v and not DBNull
            ? Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture) : null;

    private static async Task CargarPlantillaAsync(ISesionDatos s)
    {
        if (await IdPlantillaAsync(s) is not null) return;
        await s.EjecutarAsync(
            "INSERT INTO PlantillasAsiento (Nombre, Descripcion, Tipo, IdSociedad, Origen, ClaveProceso, ReglaFecha, AgruparPorCuenta, Activa, MotivoPropuesto, Ejemplo, Orden, Clase) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            NombrePlantilla, DescripcionPlantilla, "SISTEMA", DBNull.Value, "PROCESO", ClaveProceso, "FECHA_ORIGEN", true, true, DBNull.Value, EjemploPlantilla, (short)101, "PROCESO");
        var id = await s.UltimoIdAsync();
        foreach (var l in Lineas)
            await s.EjecutarAsync(
                "INSERT INTO PlantillasAsientoLineas (IdPlantillaAsiento, NumeroAsiento, Orden, PorCadaApunte, FormulaCuenta, Lado, FormulaImporte, FormulaConcepto, FormulaCentro, Condicion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                id, (short)1, l.Orden, false, l.Cuenta, l.Lado, l.Importe, l.Concepto, (object?)l.Centro ?? DBNull.Value, DBNull.Value);
    }
}
