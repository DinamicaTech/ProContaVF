using System.Globalization;

namespace ProConta.Inicio.Salud;

/// <summary>Cambio de tipo de IVA con fecha: hay que facturar lo pendiente antes de que entre en vigor.</summary>
/// <param name="Desde">Primer día con el tipo nuevo.</param>
/// <param name="Que">A qué ventas afecta, en minúscula («el alquiler turístico»).</param>
/// <param name="TipoNuevo">El tipo nuevo, como texto («10 %»).</param>
/// <param name="Origen">De dónde sale el dato (decisión de los docs).</param>
public sealed record CambioTipoIva(DateTime Desde, string Que, string TipoNuevo, string Origen);

/// <summary>
/// Fechas fiscales que usa la salud de la contabilidad. Los tipos de IVA de la BD (TiposDeIVA, ReglasVenta) no tienen fechas de
/// vigencia, así que los cambios de tipo con fecha son un dato único aquí, en un solo sitio (pantalla-de-inicio 2026-11-15 13:58).
/// Los plazos son los de la pantalla de modelos tributarios (<see cref="ProConta.Contabilidad.Modelos.PlazosModelos"/>): el último día
/// pasa al siguiente hábil si cae en sábado, domingo o festivo nacional (contabilidad/modelos-tributarios 2026-11-18).
/// </summary>
public static class CalendarioFiscal
{
    /// <summary>Cambios de tipo de IVA conocidos. Para añadir uno, una línea aquí.</summary>
    public static IReadOnlyList<CambioTipoIva> CambiosTipoIva { get; } =
    [
        new(new DateTime(2026, 12, 1), "el alquiler turístico", "10 %", "docs/README.md, decisión del MVP del 2026-11-14 14:02"),
    ];

    /// <summary>Las tareas de plazos fiscales se muestran en los 90 días anteriores al fin del plazo.</summary>
    public const int DiasAntesDelPlazo = 90;

    /// <summary>Inicio del plazo del 303 de un trimestre: el día 1 del mes siguiente al trimestre.</summary>
    public static DateTime InicioPlazo303(int anio, int trimestre) => ProConta.Contabilidad.Modelos.PlazosModelos.Plazo("303", anio, $"{trimestre}T").Desde;

    /// <summary>Fin del plazo del 303: el 20 de abril, julio y octubre; el del 4T, el 30 de enero (o el siguiente día hábil).</summary>
    public static DateTime FinPlazo303(int anio, int trimestre) => ProConta.Contabilidad.Modelos.PlazosModelos.Plazo("303", anio, $"{trimestre}T").Hasta;

    /// <summary>Fin del plazo del resumen anual 390: el 30 de enero del año siguiente (o el siguiente día hábil).</summary>
    public static DateTime FinPlazo390(int anio) => ProConta.Contabilidad.Modelos.PlazosModelos.Plazo("390", anio, "0A").Hasta;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Importe en euros con separador de miles y dos decimales (1.234,56 €; negativos con «−»), como el resto de ProContaVF.</summary>
    public static string Imp(decimal v)
    {
        v = Math.Round(v, 2, MidpointRounding.AwayFromZero);
        var nf = (NumberFormatInfo)Es.NumberFormat.Clone();
        nf.NumberGroupSeparator = ".";
        nf.NumberDecimalSeparator = ",";
        nf.NumberGroupSizes = [3];
        var abs = Math.Abs(v).ToString("#,##0.00", nf);
        return (v < 0 ? "−" : "") + abs + " €";
    }

    /// <summary>«1 factura» / «3 facturas».</summary>
    public static string N(int n, string singular, string plural) => $"{n.ToString("#,##0", Es)} {(n == 1 ? singular : plural)}";

    public static string Fecha(DateTime d) => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
