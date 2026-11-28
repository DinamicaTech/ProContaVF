namespace ProConta.Activos.Gestion;

/// <summary>
/// Rentabilidad de un activo en el listado (activos 2026-11-28): la del ejercicio de trabajo y la anual proyectada. Todo en tanto por uno y sin
/// redondear: el redondeo es solo de presentación (un decimal del porcentaje).
/// </summary>
public static class RentabilidadActivo
{
    /// <summary>
    /// Rentabilidad del ejercicio de un activo físico: resultado del ejercicio (ingresos − gastos, sin la amortización) entre su coste de compra.
    /// Null sin coste.
    /// </summary>
    public static decimal? DelEjercicio(decimal resultado, decimal coste) => coste > 0m ? resultado / coste : null;

    /// <summary>
    /// Meses con los que se proyecta el ejercicio: el último mes (1 a 12) con ingresos del activo; un ejercicio ya terminado cuenta entero (12).
    /// Null si el ejercicio en curso no tiene ingresos.
    /// </summary>
    public static int? MesesTranscurridos(int ejercicio, int? ultimoMesConIngresos, DateTime hoy) =>
        ejercicio < hoy.Year ? 12 : ultimoMesConIngresos is int m and >= 1 and <= 12 ? m : null;

    /// <summary>
    /// Proyección a doce meses de una cifra (resultado o rentabilidad) del ejercicio: × 12 / meses transcurridos (propietario 2026-11-28, A: la media
    /// mensual por doce). Null si falta la cifra o los meses.
    /// </summary>
    public static decimal? Proyectar(decimal? valor, int? meses) => valor is decimal v && meses is int m and > 0 ? v * 12m / m : null;

    /// <summary>
    /// Rentabilidad de un grupo de activos físicos: suma de resultados entre suma de costes; la proyectada, suma de los resultados proyectados entre
    /// suma de costes (un activo sin proyección entra con su resultado real). Null sin coste.
    /// </summary>
    public static (decimal? Real, decimal? Proyectada) DeGrupo(IEnumerable<(decimal Resultado, decimal Coste, int? Meses)> activos)
    {
        var lista = activos.ToList();
        var coste = lista.Sum(a => a.Coste);
        if (coste <= 0m) return (null, null);
        var real = lista.Sum(a => a.Resultado);
        var proyectado = lista.Sum(a => Proyectar(a.Resultado, a.Meses) ?? a.Resultado);
        return (real / coste, proyectado / coste);
    }

    /// <summary>
    /// Proyección anual lineal de una rentabilidad que va desde <paramref name="desde"/> hasta <paramref name="hasta"/>: × 365 / días (activos
    /// financieros, con la fecha de la última cotización). Null sin rentabilidad o sin días.
    /// </summary>
    public static decimal? ProyectarPorDias(decimal? valor, DateTime desde, DateTime hasta)
    {
        var dias = (hasta.Date - desde.Date).Days;
        return valor is decimal v && dias > 0 ? v * 365m / dias : null;
    }
}
