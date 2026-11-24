using ProConta.Organizacion.Mantenimiento;
using ProConta.Datos.Persistencia;

namespace ProConta.Pruebas.Access;

/// <summary>Altas de datos sintéticos para las pruebas (nunca datos de clientes reales).</summary>
public static class DatosSinteticos
{
    public static Task SociedadAsync(IBaseDatos bd, string id, string nombre, string nif = "B12345674") =>
        bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Sociedades (IdSociedad, Nombre, CIF) VALUES (?, ?, ?)", id, nombre, nif));

    /// <summary>
    /// Ejercicio con las cuentas de los procesos ya asignadas a la habitual de cada una, como lo deja «Auto»: los procesos ya no tienen
    /// cuenta por defecto implícita (organizacion/sociedades-ejercicios 2026-11-18).
    /// </summary>
    public static Task EjercicioAsync(IBaseDatos bd, int idSociedadEjercicio, string idSociedad, int anio, bool cerrado = false, bool activa = true) =>
        bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio, Nombre, Activa, EjercicioCerrado) VALUES (?, ?, ?, ?, ?, ?)",
                idSociedadEjercicio, idSociedad, anio.ToString(), $"Sociedad {idSociedad} {anio}", activa, cerrado);
            var esquema = s.LeerEsquema();
            foreach (var c in CuentasEjercicio.Todas.Where(c => c.Habitual is not null && esquema.ExisteColumna("SociedadesEjercicios", c.Columna)))
                await s.EjecutarAsync($"UPDATE SociedadesEjercicios SET {c.Columna} = ? WHERE IdSociedadEjercicio = ?", c.Habitual, idSociedadEjercicio);
            return 0;
        });

    /// <summary>
    /// Deja activos en el catálogo TiposDeIVA exactamente estos porcentajes (los demás se desactivan; los que faltan se dan de
    /// alta). Compras y ventas toman de ahí los tipos (organizacion/sociedades-ejercicios 2026-11-13 20:55).
    /// </summary>
    public static Task TiposIvaAsync(IBaseDatos bd, params decimal[] tipos) =>
        bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE TiposDeIVA SET Activo = False");
            foreach (var t in tipos)
                if (await s.EjecutarAsync("UPDATE TiposDeIVA SET Activo = True WHERE Valor = ?", (double)t) == 0)
                    await s.EjecutarAsync("INSERT INTO TiposDeIVA (Valor, Descripcion, Activo) VALUES (?, ?, ?)", (double)t, $"IVA {t:0.##} %", true);
            return tipos.Length;
        });

    public static async Task<int> GrupoAsync(IBaseDatos bd, string codigo, bool activo = true) =>
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO GruposOperadores (Codigo, Nombre, Activo) VALUES (?, ?, ?)", codigo, codigo, activo);
            return await s.UltimoIdAsync();
        });

    /// <summary>
    /// Operador sin contraseña: un tercero persona física con ese nombre de pila (y los apellidos que se den) con el rol de operador
    /// (seguridad/pantallas 2026-11-24). Devuelve el IdOperador.
    /// </summary>
    public static async Task<int> OperadorSinContrasenaAsync(IBaseDatos bd, string nombre, bool activo = true, string? primerApellido = null, string? segundoApellido = null) =>
        (await OperadorConTerceroAsync(bd, nombre, activo, primerApellido, segundoApellido)).IdOperador;

    /// <summary>Como <see cref="OperadorSinContrasenaAsync"/>, devolviendo también el IdTercero.</summary>
    public static async Task<(int IdOperador, int IdTercero)> OperadorConTerceroAsync(IBaseDatos bd, string nombre, bool activo = true, string? primerApellido = null, string? segundoApellido = null) =>
        await bd.EjecutarAsync(async s =>
        {
            var idTercero = await TerceroPersonaAsync(s, nombre, primerApellido, segundoApellido);
            await s.EjecutarAsync("INSERT INTO OperadoresSeguridad (IdTercero, Activo, PasswordTemporal) VALUES (?, ?, ?)", idTercero, activo, false);
            return (await s.UltimoIdAsync(), idTercero);
        });

    /// <summary>Tercero persona física activo con nombre y apellidos sintéticos.</summary>
    public static Task<int> TerceroPersonaAsync(IBaseDatos bd, string nombre, string? primerApellido = null, string? segundoApellido = null, string tipo = "FISICA") =>
        bd.EjecutarAsync(s => TerceroPersonaAsync(s, nombre, primerApellido, segundoApellido, tipo));

    private static async Task<int> TerceroPersonaAsync(ISesionDatos s, string nombre, string? primerApellido, string? segundoApellido, string tipo = "FISICA")
    {
        var completo = string.Join(' ', new[] { nombre, primerApellido, segundoApellido }.Where(x => !string.IsNullOrWhiteSpace(x)));
        await s.EjecutarAsync("INSERT INTO Terceros (TipoPersona, Nombre, NombrePersona, PrimerApellido, SegundoApellido, RazonSocial, Activo, FechaAlta, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            tipo, completo, tipo == "FISICA" ? (object)nombre : DBNull.Value, (object?)primerApellido ?? DBNull.Value, (object?)segundoApellido ?? DBNull.Value,
            tipo == "FISICA" ? DBNull.Value : (object)completo, true, new DateTime(2026, 1, 1), 1);
        return await s.UltimoIdAsync();
    }

    public static Task MiembroAsync(IBaseDatos bd, int idOperador, int idGrupo) =>
        bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO OperadoresGrupos (IdOperador, IdGrupoOperadores) VALUES (?, ?)", idOperador, idGrupo));

    public static Task AccesoSociedadAsync(IBaseDatos bd, int idGrupo, string idSociedad) =>
        bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO AccesoSociedadesGrupo (IdGrupoOperadores, IdSociedad) VALUES (?, ?)", idGrupo, idSociedad));
}
