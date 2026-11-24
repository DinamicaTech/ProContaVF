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

    /// <summary>Categoría de los tipos que son inversiones financieras (TiposActivo.Categoria).</summary>
    public const string CategoriaInversion = "INVERSION";

    private static volatile HashSet<string> _inversiones = new(StringComparer.OrdinalIgnoreCase) { Inversion };

    /// <summary>
    /// Tipos de las inversiones financieras: INVERSION y cualquier otro de categoría «Inversión financiera», como un tipo para los fondos
    /// de capital riesgo con su propia plantilla (propietario 2026-11-24 19:12). Los demás tipos los conoce después de <see cref="RegistrarCategorias"/>.
    /// </summary>
    public static bool EsInversion(string codigo) => _inversiones.Contains(codigo ?? "");

    /// <summary>Apunta qué tipos son de categoría «Inversión financiera» (lo llama el servicio de activos al leer los tipos).</summary>
    public static void RegistrarCategorias(IEnumerable<(string Codigo, string? Categoria)> tipos)
    {
        var nuevos = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Inversion };
        foreach (var (codigo, categoria) in tipos)
            if (string.Equals(categoria?.Trim(), CategoriaInversion, StringComparison.OrdinalIgnoreCase)) nuevos.Add(codigo.Trim());
        _inversiones = nuevos;
    }

    /// <summary>Tipos con ficha de inmueble (ActivosInmuebles).</summary>
    public static bool EsInmueble(string codigo) => codigo is Vivienda or Local or Parking or Turistico;
}
