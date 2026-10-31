namespace ProConta.Nucleo.Contexto;

/// <summary>
/// Contexto de trabajo: sociedad permanente y su instancia anual (SociedadEjercicio).
/// Sociedad y SociedadEjercicio son entidades distintas (RF-SEG-03); el ejercicio nunca
/// se deduce de la sociedad. Lo valida y entrega organizacion/sociedades-ejercicios.
/// </summary>
/// <param name="IdSociedad">Código permanente de la sociedad (Sociedades.IdSociedad).</param>
/// <param name="IdSociedadEjercicio">Clave de la instancia anual (SociedadesEjercicios.IdSociedadEjercicio).</param>
/// <param name="Ejercicio">Año del ejercicio (p. ej. 2026).</param>
public sealed record ContextoSociedadEjercicio(string IdSociedad, int IdSociedadEjercicio, int Ejercicio)
{
    public override string ToString() => $"{IdSociedad}/{Ejercicio}";
}
