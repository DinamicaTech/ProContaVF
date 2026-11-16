using ProConta.Contabilidad.Plantillas;

namespace ProConta.Norma43.Conciliacion;

/// <summary>
/// Plantilla de sistema «Movimiento bancario (Norma 43)» (norma43/conciliacion): la misma que da de alta la migración 20261113_0900_CONCILIACION_N43
/// en PlantillasAsiento. Recibe un registro por imputación de la línea, con los campos CuentaBanco, NombreBanco, Contrapartida,
/// NombreContrapartida, Importe (positivo: cargo, la contrapartida al Debe; negativo: abono), Sentido, Concepto (la descripción corta del
/// movimiento, que llevan los dos apuntes desde la migración 20261113_1100_CONCEPTOS_N43), Centro y Documento, y genera un solo asiento agrupando por cuenta: la cuenta del banco por el importe del movimiento y cada contrapartida por su parte.
/// Se usa si la BD aún no tiene la plantilla (o la han borrado).
/// </summary>
public static class PlantillaConciliacionN43
{
    public const string Nombre = "Movimiento bancario (Norma 43)";

    public const string Descripcion =
        "Contabiliza un movimiento de un extracto bancario Norma 43: la cuenta 572 del banco contra la cuenta asignada al movimiento (o las cuentas, " +
        "si está repartido). Un cargo va al Debe de la contrapartida y al Haber del banco; un abono, al revés. Es una plantilla de sistema: la " +
        "ejecuta la conciliación bancaria y no se usa a mano.";

    /// <summary>
    /// Concepto de los dos apuntes (norma43/conciliacion 2026-11-13 10:50, migración 20261113_1100_CONCEPTOS_N43): la descripción corta del
    /// movimiento que calcula la conciliación (campo Concepto del registro: «Recibo de …», con la factura enlazada si la hay).
    /// </summary>
    public const string ConceptoBanco = "Concepto";

    /// <inheritdoc cref="ConceptoBanco"/>
    public const string ConceptoContrapartida = "Concepto";

    public static PlantillaAsiento Predeterminada { get; } = new(
        0, Nombre, Descripcion, TipoPlantilla.Sistema, OrigenPlantilla.Proceso, [],
        [
            new LineaPlantilla(1, 1, "CuentaBanco", "H", "Importe", ConceptoBanco),
            new LineaPlantilla(1, 2, "Contrapartida", "D", "Importe", ConceptoContrapartida, "Centro"),
        ],
        ClaveProceso: ServicioConciliacionN43.ClaveProceso, ReglaFecha: ReglaFecha.FechaOrigen, AgruparPorCuenta: true, Orden: 101, Clase: ClasePlantilla.Proceso);
}
