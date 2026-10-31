using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Organizacion.Sociedades;

/// <summary>Sociedad permanente (tabla Sociedades). El NIF se guarda en la columna heredada CIF.</summary>
public sealed record Sociedad(string IdSociedad, string RazonSocial, string Nif);

/// <summary>Instancia anual de una sociedad (tabla SociedadesEjercicios).</summary>
public sealed record SociedadEjercicio(int IdSociedadEjercicio, string IdSociedad, int Ejercicio, string RazonSocial, bool Activa, bool Cerrado)
{
    public ContextoSociedadEjercicio Contexto => new(IdSociedad, IdSociedadEjercicio, Ejercicio);
}

/// <summary>
/// Contrato de sociedades y ejercicios: las demás áreas obtienen de aquí el contexto validado sin
/// leer las tablas. Abrir y cerrar ejercicios lo hará contabilidad/procesos.
/// </summary>
public interface IServicioSociedades
{
    Task<IReadOnlyList<Sociedad>> ListarSociedadesAsync();

    /// <summary>Ejercicios de la sociedad, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<SociedadEjercicio>> ListarEjerciciosAsync(string idSociedad);

    Task<SociedadEjercicio?> ObtenerEjercicioAsync(int idSociedadEjercicio);

    /// <summary>Contexto validado: el ejercicio existe y pertenece a la sociedad indicada.</summary>
    Task<Resultado<ContextoSociedadEjercicio>> ObtenerContextoAsync(string idSociedad, int idSociedadEjercicio);

    /// <summary>Correcto si el ejercicio admite cambios; error explicado si está cerrado.</summary>
    Task<Resultado> ComprobarEjercicioAbiertoAsync(ContextoSociedadEjercicio contexto);
}

public sealed class ServicioSociedadesAccess(IBaseDatos bd) : IServicioSociedades
{
    private const string ColumnasEjercicio = "IdSociedadEjercicio, IdSociedad, Ejercicio, Nombre, Activa, EjercicioCerrado";

    public Task<IReadOnlyList<Sociedad>> ListarSociedadesAsync() =>
        bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdSociedad, Nombre, CIF FROM Sociedades ORDER BY Nombre",
            f => new Sociedad(f.Texto("IdSociedad")!, f.Texto("Nombre") ?? "", f.Texto("CIF") ?? "")));

    public async Task<IReadOnlyList<SociedadEjercicio>> ListarEjerciciosAsync(string idSociedad)
    {
        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync(
            $"SELECT {ColumnasEjercicio} FROM SociedadesEjercicios WHERE IdSociedad = ?", Mapear, idSociedad));
        return filas.OfType<SociedadEjercicio>().OrderByDescending(e => e.Ejercicio).ToList();
    }

    public Task<SociedadEjercicio?> ObtenerEjercicioAsync(int idSociedadEjercicio) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            $"SELECT {ColumnasEjercicio} FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", Mapear, idSociedadEjercicio));

    public async Task<Resultado<ContextoSociedadEjercicio>> ObtenerContextoAsync(string idSociedad, int idSociedadEjercicio)
    {
        var ejercicio = await ObtenerEjercicioAsync(idSociedadEjercicio);
        if (ejercicio is null || !string.Equals(ejercicio.IdSociedad, idSociedad, StringComparison.OrdinalIgnoreCase))
            return Resultado<ContextoSociedadEjercicio>.Fallo(new Error("Organizacion.ContextoNoValido",
                "La empresa y el ejercicio indicados no forman un contexto de trabajo válido.",
                $"Sociedad {idSociedad}, ejercicio {idSociedadEjercicio}",
                "El ejercicio no existe, no tiene año o pertenece a otra sociedad.",
                "Vuelve a elegir la empresa y el ejercicio."));
        return Resultado<ContextoSociedadEjercicio>.Correcto(ejercicio.Contexto);
    }

    public async Task<Resultado> ComprobarEjercicioAbiertoAsync(ContextoSociedadEjercicio contexto)
    {
        var ejercicio = await ObtenerEjercicioAsync(contexto.IdSociedadEjercicio);
        if (ejercicio is null)
            return Resultado.Fallo(new Error("Organizacion.EjercicioInexistente", "El ejercicio de trabajo ya no existe.",
                contexto.ToString(), "Se ha borrado o cambiado mientras trabajabas.", "Vuelve a elegir la empresa y el ejercicio."));
        return ejercicio.Cerrado
            ? Resultado.Fallo(new Error("Organizacion.EjercicioCerrado",
                $"El ejercicio {ejercicio.Ejercicio} de {ejercicio.RazonSocial} está cerrado y no admite cambios.",
                contexto.ToString(), "Un ejercicio cerrado solo se puede consultar.",
                "Si hay que corregirlo, reabre el ejercicio con el proceso contable autorizado y repite la operación."))
            : Resultado.Correcto();
    }

    /// <summary>Filas sin sociedad o sin año válido no forman un ejercicio: se descartan (null).</summary>
    private static SociedadEjercicio? Mapear(IFilaDatos f)
    {
        var idSociedad = f.Texto("IdSociedad");
        if (string.IsNullOrWhiteSpace(idSociedad) ||
            !int.TryParse(f.Texto("Ejercicio"), NumberStyles.None, CultureInfo.InvariantCulture, out var anio))
            return null;
        return new SociedadEjercicio(f.Entero("IdSociedadEjercicio"), idSociedad, anio, f.Texto("Nombre") ?? "",
            f.Logico("Activa"), f.Logico("EjercicioCerrado"));
    }
}
