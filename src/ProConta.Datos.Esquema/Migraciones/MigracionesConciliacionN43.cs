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
