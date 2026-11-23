namespace ProConta.Activos;

/// <summary>
/// Códigos de los tipos de activo según el PGC (activos, decisión 2026-11-12 09:47). Los da de alta la migración
/// 20261112_1030_ACTIVOS_ESTRUCTURA del ActualizadorEsquema.
/// </summary>
public static class TiposActivoPgc
{
    public const string Vivienda = "VIVIENDA";
    public const string Local = "LOCAL";
    public const string Parking = "PARKING";
    public const string Turistico = "TURISTICO";
    public const string Vehiculo = "VEHICULO";
    public const string Instalacion = "INSTALACION";
    public const string Aplicacion = "APLICACION";

    /// <summary>Inversión financiera (ETF, fondo, acciones): se da de alta por ISIN y no se amortiza (activos/activos-financieros).</summary>
    public const string Inversion = "INVERSION";

    public static IReadOnlyList<string> Todos { get; } = [Vivienda, Local, Parking, Turistico, Vehiculo, Instalacion, Aplicacion];

    /// <summary>Tipos de las inversiones financieras.</summary>
    public static bool EsInversion(string codigo) => codigo == Inversion;

    /// <summary>Tipos con ficha de inmueble (ActivosInmuebles).</summary>
    public static bool EsInmueble(string codigo) => codigo is Vivienda or Local or Parking or Turistico;
}
