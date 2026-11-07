using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Organizacion.Sociedades;

/// <summary>
/// Trimestre contable activo del ejercicio (SociedadesEjercicios.TrimestreActivo, compras/pantallas 2026-11-06 12:10):
/// el que se está contabilizando, que no es siempre el de hoy (el 10 de octubre aún se puede estar con el tercero).
/// Cerrarlo activa el siguiente; el cuarto no se cierra aquí (lo cierra el cierre del ejercicio). El anterior solo se
/// puede reactivar si el trimestre natural de hoy + 10 días es el activo, y con aviso.
/// </summary>
public sealed class ServicioTrimestreActivo(IBaseDatos bd, IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static int Trimestre(DateTime fecha) => Nucleo.Contexto.TrimestreContable.De(fecha);

    /// <summary>«ene-mar», «abr-jun», «jul-sep» u «oct-dic».</summary>
    public static string Nombre(int trimestre) => trimestre switch { 1 => "ene-mar", 2 => "abr-jun", 3 => "jul-sep", 4 => "oct-dic", _ => "sin trimestre" };

    /// <summary>Hoy cae fuera del trimestre activo del ejercicio de este año (el aviso al entrar en compras).</summary>
    public static bool FueraDelActivo(int? activo, int ejercicio, DateTime hoy) => activo is int t && hoy.Year == ejercicio && Trimestre(hoy) != t;

    /// <summary>Se puede reactivar el trimestre anterior: el natural de hoy + 10 días es el activo y no es el primero.</summary>
    public static bool PuedeReactivar(int? activo, int ejercicio, DateTime hoy) =>
        activo is > 1 and int t && hoy.AddDays(10).Year == ejercicio && Trimestre(hoy.AddDays(10)) == t;

    public DateTime Hoy => _reloj.Ahora.Date;

    public Task<int?> ObtenerAsync(int idSociedadEjercicio) => bd.EjecutarAsync(async s =>
    {
        var v = await s.EscalarAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", idSociedadEjercicio);
        return v is null or DBNull ? (int?)null : Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
    });

    /// <summary>Cierra el trimestre activo y activa el siguiente (sin trimestre, activa el de hoy).</summary>
    public async Task<Resultado<int>> ActivarSiguienteAsync(int idSociedadEjercicio)
    {
        var actual = await ObtenerAsync(idSociedadEjercicio);
        if (actual is 4)
            return Resultado<int>.Fallo(new Error("Trimestre.Cuarto", "El cuarto trimestre no se cierra aquí.", "Trimestre activo",
                "Se cierra con el cierre del ejercicio.", "Cuando toque, haz el cierre del ejercicio."));
        var nuevo = actual is int t ? t + 1 : Trimestre(Hoy);
        await Fijar(idSociedadEjercicio, nuevo);
        return Resultado<int>.Correcto(nuevo);
    }

    /// <summary>Reactiva el trimestre anterior al activo, solo en los días que lo permite <see cref="PuedeReactivar"/>.</summary>
    public async Task<Resultado<int>> ReactivarAnteriorAsync(int idSociedadEjercicio, int ejercicio)
    {
        var actual = await ObtenerAsync(idSociedadEjercicio);
        if (!PuedeReactivar(actual, ejercicio, Hoy))
            return Resultado<int>.Fallo(new Error("Trimestre.NoReactivable", "Ya no se puede reactivar el trimestre anterior.", "Trimestre activo",
                "Solo se reactiva el anterior cuando el trimestre natural de hoy + 10 días es el activo.", null));
        var nuevo = actual!.Value - 1;
        await Fijar(idSociedadEjercicio, nuevo);
        return Resultado<int>.Correcto(nuevo);
    }

    private Task Fijar(int idSociedadEjercicio, int trimestre) =>
        bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = ?", trimestre, idSociedadEjercicio));
}
