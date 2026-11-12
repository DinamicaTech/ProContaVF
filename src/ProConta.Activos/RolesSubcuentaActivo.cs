namespace ProConta.Activos;

/// <summary>
/// Rol de un tipo de subcuenta de activo (activos, decisiones 2026-11-12 09:47 y 10:00): los fijos los usan los procesos
/// (amortización, baja, facturación) para saber qué subcuenta del activo tocar; cada tipo de activo tiene como mucho una
/// línea de plantilla por rol fijo. El operador ve siempre el texto, nunca el código.
/// </summary>
public static class RolesSubcuentaActivo
{
    public const string Activo = "ACTIVO";
    public const string Terreno = "TERRENO";
    public const string AmortizacionAcumulada = "AMORTIZACION_ACUMULADA";
    public const string GastoAmortizacion = "GASTO_AMORTIZACION";
    public const string IngresoAlquiler = "INGRESO_ALQUILER";
    public const string Libre = "LIBRE";

    /// <summary>Código → texto que ve el operador.</summary>
    public static IReadOnlyDictionary<string, string> Textos { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Activo] = "Valor del bien (se amortiza)",
        [Terreno] = "Terreno (no se amortiza)",
        [AmortizacionAcumulada] = "Amortización acumulada",
        [GastoAmortizacion] = "Gasto por amortización",
        [IngresoAlquiler] = "Ingreso por alquiler",
        [Libre] = "Otra cuenta del activo (no la usa ningún proceso)",
    };

    /// <summary>Los roles que solo pueden aparecer una vez en la plantilla de un tipo de activo.</summary>
    public static IReadOnlyList<string> Fijos { get; } = [Activo, Terreno, AmortizacionAcumulada, GastoAmortizacion, IngresoAlquiler];

    public static bool EsFijo(string rol) => Fijos.Contains(rol, StringComparer.Ordinal);
}
