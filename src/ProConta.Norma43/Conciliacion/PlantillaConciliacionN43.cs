using ProConta.Contabilidad.Plantillas;

namespace ProConta.Norma43.Conciliacion;

/// <summary>
/// Plantilla de sistema «Movimiento bancario (Norma 43)» (norma43/conciliacion): la misma que da de alta la migración 20261113_0900_CONCILIACION_N43
/// en PlantillasAsiento. Recibe un registro por imputación de la línea, con los campos CuentaBanco, NombreBanco, Contrapartida,
/// NombreContrapartida, Importe (positivo: cargo, la contrapartida al Debe; negativo: abono), Sentido, Concepto (el texto del banco), Centro y
/// Documento, y genera un solo asiento agrupando por cuenta: la cuenta del banco por el importe del movimiento y cada contrapartida por su parte.
/// Se usa si la BD aún no tiene la plantilla (o la han borrado).
/// </summary>
public static class PlantillaConciliacionN43
{
    public const string Nombre = "Movimiento bancario (Norma 43)";

    public const string Descripcion =
        "Contabiliza un movimiento de un extracto bancario Norma 43: la cuenta 572 del banco contra la cuenta asignada al movimiento (o las cuentas, " +
        "si está repartido). Un cargo va al Debe de la contrapartida y al Haber del banco; un abono, al revés. Es una plantilla de sistema: la " +
        "ejecuta la conciliación bancaria y no se usa a mano.";

    /// <summary>Concepto del apunte del banco, como en el ERP: a quién se paga o de quién se cobra.</summary>
    public const string ConceptoBanco = "Si(Importe > 0; \"Pago a \"; \"Cobro de \") & NombreContrapartida";

    /// <summary>Concepto del apunte de la contrapartida, como en el ERP: desde qué banco se paga o en cuál se ingresa.</summary>
    public const string ConceptoContrapartida = "Si(Importe > 0; \"Pago desde \"; \"Ingreso en \") & NombreBanco";

    public static PlantillaAsiento Predeterminada { get; } = new(
        0, Nombre, Descripcion, TipoPlantilla.Sistema, OrigenPlantilla.Proceso, [],
        [
            new LineaPlantilla(1, 1, "CuentaBanco", "H", "Importe", ConceptoBanco),
            new LineaPlantilla(1, 2, "Contrapartida", "D", "Importe", ConceptoContrapartida, "Centro"),
        ],
        ClaveProceso: ServicioConciliacionN43.ClaveProceso, ReglaFecha: ReglaFecha.FechaOrigen, AgruparPorCuenta: true, Orden: 101, Clase: ClasePlantilla.Proceso);
}
