using ProConta.Datos.Persistencia;

namespace ProConta.Pruebas.Access;

/// <summary>Altas de datos sintéticos para las pruebas (nunca datos de clientes reales).</summary>
public static class DatosSinteticos
{
    public static Task SociedadAsync(IBaseDatos bd, string id, string nombre, string nif = "B12345674") =>
        bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Sociedades (IdSociedad, Nombre, CIF) VALUES (?, ?, ?)", id, nombre, nif));

    public static Task EjercicioAsync(IBaseDatos bd, int idSociedadEjercicio, string idSociedad, int anio, bool cerrado = false, bool activa = true) =>
        bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio, Nombre, Activa, EjercicioCerrado) VALUES (?, ?, ?, ?, ?, ?)",
            idSociedadEjercicio, idSociedad, anio.ToString(), $"Sociedad {idSociedad} {anio}", activa, cerrado));

    public static async Task<int> GrupoAsync(IBaseDatos bd, string codigo, bool activo = true) =>
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO GruposOperadores (Codigo, Nombre, Activo) VALUES (?, ?, ?)", codigo, codigo, activo);
            return await s.UltimoIdAsync();
        });

    public static async Task<int> OperadorSinContrasenaAsync(IBaseDatos bd, string nombre, bool activo = true) =>
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO OperadoresSeguridad (Nombre, Activo, PasswordTemporal) VALUES (?, ?, ?)", nombre, activo, false);
            return await s.UltimoIdAsync();
        });

    public static Task MiembroAsync(IBaseDatos bd, int idOperador, int idGrupo) =>
        bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO OperadoresGrupos (IdOperador, IdGrupoOperadores) VALUES (?, ?)", idOperador, idGrupo));

    public static Task AccesoSociedadAsync(IBaseDatos bd, int idGrupo, string idSociedad) =>
        bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO AccesoSociedadesGrupo (IdGrupoOperadores, IdSociedad) VALUES (?, ?)", idGrupo, idSociedad));
}
