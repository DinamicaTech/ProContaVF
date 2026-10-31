using System.Security.Cryptography;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Seguridad.Permisos;

/// <summary>Credencial guardada: hash PBKDF2-HMAC-SHA256, salt e iteraciones (nunca la contraseña).</summary>
public sealed record Credencial(byte[] Hash, byte[] Salt, int Iteraciones);

/// <summary>Operador (tabla OperadoresSeguridad). Sin credencial = operador sin contraseña.</summary>
public sealed record Operador(int IdOperador, string Nombre, string? Email, bool Activo, Credencial? Credencial, bool ContrasenaTemporal)
{
    public bool TieneContrasena => Credencial is not null;
}

/// <summary>Derivación y comprobación de contraseñas, compatible con las credenciales ya guardadas.</summary>
public static class HashContrasena
{
    public const int Iteraciones = 210_000;
    public const int BytesSalt = 16;
    public const int BytesHash = 32;

    public static Credencial Crear(string contrasena)
    {
        var salt = RandomNumberGenerator.GetBytes(BytesSalt);
        return new Credencial(Rfc2898DeriveBytes.Pbkdf2(contrasena, salt, Iteraciones, HashAlgorithmName.SHA256, BytesHash), salt, Iteraciones);
    }

    /// <summary>Comparación en tiempo constante.</summary>
    public static bool Verificar(string? contrasena, Credencial credencial)
    {
        if (credencial.Iteraciones <= 0 || credencial.Hash.Length == 0) return false;
        var candidata = Rfc2898DeriveBytes.Pbkdf2(contrasena ?? "", credencial.Salt, credencial.Iteraciones, HashAlgorithmName.SHA256, credencial.Hash.Length);
        return CryptographicOperations.FixedTimeEquals(candidata, credencial.Hash);
    }
}

/// <summary>Registro de eventos de seguridad (tabla AuditoriaSeguridad). Nunca recibe secretos.</summary>
internal static class AuditoriaSeguridad
{
    public static Task RegistrarAsync(ISesionDatos s, IReloj reloj, string tipoEvento, string? codigoOperador, string? actor, string resultado, string? detalle = null) =>
        s.EjecutarAsync("INSERT INTO AuditoriaSeguridad (FechaHora, TipoEvento, CodigoOperador, Actor, Resultado, Detalle) VALUES (?, ?, ?, ?, ?, ?)",
            reloj.Ahora, tipoEvento, Recortar(codigoOperador, 50), Recortar(actor, 50), resultado, Recortar(detalle, 255));

    private static string? Recortar(string? texto, int longitud) => texto is null || texto.Length <= longitud ? texto : texto[..longitud];
}

/// <summary>
/// Operadores y grupos: lo que necesita el acceso (buscar operador, saber si hay alguno) y el
/// asistente del primer administrador (P-03). El mantenimiento completo llegará con seguridad/pantallas.
/// </summary>
public sealed class ServicioOperadores(IBaseDatos bd, IReloj? reloj = null)
{
    public const string GrupoAdministradores = "ADMINISTRADORES";
    public const int LongitudMinimaContrasena = 8;

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public async Task<bool> HayOperadoresAsync() =>
        Convert.ToInt32(await bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM OperadoresSeguridad"))) > 0;

    /// <summary>Busca por nombre exacto sin distinguir mayúsculas ni espacios de los extremos.</summary>
    public Task<Operador?> BuscarPorNombreAsync(string nombre) =>
        bd.EjecutarAsync(s => BuscarAsync(s, "UCASE(TRIM(Nombre)) = UCASE(?)", (nombre ?? "").Trim()));

    public Task<Operador?> ObtenerAsync(int idOperador) =>
        bd.EjecutarAsync(s => BuscarAsync(s, "IdOperador = ?", idOperador));

    /// <summary>Grupos activos del operador.</summary>
    public Task<IReadOnlyList<int>> GruposActivosAsync(int idOperador) =>
        bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT g.IdGrupoOperadores FROM OperadoresGrupos AS og INNER JOIN GruposOperadores AS g ON og.IdGrupoOperadores = g.IdGrupoOperadores WHERE og.IdOperador = ? AND g.Activo = True",
            f => f.Entero("IdGrupoOperadores"), idOperador));

    /// <summary>
    /// Crea el primer administrador cuando no hay ningún operador (la maestra no trae operadores por
    /// defecto). Crea el grupo ADMINISTRADORES si falta. Si ya hay operadores, no hace nada.
    /// </summary>
    public Task<Resultado<Operador>> CrearPrimerAdministradorAsync(string nombre, string contrasena) =>
        bd.EnTransaccionAsync(async s =>
        {
            var texto = (nombre ?? "").Trim();
            var error = ValidarNombre(texto) ?? ValidarContrasena(contrasena);
            if (error is not null) return Resultado<Operador>.Fallo(error);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM OperadoresSeguridad")) > 0)
                return Resultado<Operador>.Fallo(new Error("Seguridad.YaHayOperadores",
                    "Ya hay operadores dados de alta; el asistente del primer administrador no se puede usar.", null,
                    "Solo se ofrece cuando la base de datos no tiene ningún operador.",
                    "Entra con un operador existente; un administrador puede dar de alta otros."));

            var credencial = HashContrasena.Crear(contrasena);
            await s.EjecutarAsync(
                "INSERT INTO OperadoresSeguridad (Nombre, Activo, HashPassword, SaltPassword, IteracionesPassword, PasswordTemporal, FechaHoraPassword) VALUES (?, ?, ?, ?, ?, ?, ?)",
                texto, true, credencial.Hash, credencial.Salt, credencial.Iteraciones, false, _reloj.Ahora);
            var id = await s.UltimoIdAsync();

            var grupo = await s.ConsultarPrimeroAsync("SELECT IdGrupoOperadores FROM GruposOperadores WHERE Codigo = ?", f => (int?)f.Entero("IdGrupoOperadores"), GrupoAdministradores);
            if (grupo is null)
            {
                await s.EjecutarAsync("INSERT INTO GruposOperadores (Codigo, Nombre, Descripcion, Activo) VALUES (?, ?, ?, ?)",
                    GrupoAdministradores, "Administradores", "Administración de ProContaVF", true);
                grupo = await s.UltimoIdAsync();
            }
            await s.EjecutarAsync("INSERT INTO OperadoresGrupos (IdOperador, IdGrupoOperadores) VALUES (?, ?)", id, grupo.Value);
            await AuditoriaSeguridad.RegistrarAsync(s, _reloj, "PRIMER_ADMINISTRADOR", id.ToString(), texto, "CORRECTO");
            return Resultado<Operador>.Correcto(new Operador(id, texto, null, true, credencial, false));
        });

    /// <summary>Cambia la contraseña de un operador (la guarda como hash).</summary>
    public Task<Resultado> EstablecerContrasenaAsync(int idOperador, string contrasena, Actor actor) =>
        bd.EnTransaccionAsync(async s =>
        {
            var error = ValidarContrasena(contrasena);
            if (error is not null) return Resultado.Fallo(error);
            var credencial = HashContrasena.Crear(contrasena);
            var filas = await s.EjecutarAsync(
                "UPDATE OperadoresSeguridad SET HashPassword = ?, SaltPassword = ?, IteracionesPassword = ?, PasswordTemporal = ?, FechaHoraPassword = ? WHERE IdOperador = ?",
                credencial.Hash, credencial.Salt, credencial.Iteraciones, false, _reloj.Ahora, idOperador);
            if (filas == 0)
                return Resultado.Fallo(new Error("Seguridad.OperadorInexistente", "El operador ya no existe.", $"Operador {idOperador}",
                    "Se ha borrado mientras se editaba.", "Vuelve a cargar la lista de operadores."));
            await AuditoriaSeguridad.RegistrarAsync(s, _reloj, "CONTRASENA_CAMBIADA", idOperador.ToString(), actor.Nombre, "CORRECTO");
            return Resultado.Correcto();
        });

    internal static async Task<Operador?> BuscarAsync(ISesionDatos s, string filtro, object valor)
    {
        var encontrados = await s.ConsultarAsync(
            $"SELECT IdOperador, Nombre, Email, Activo, HashPassword, SaltPassword, IteracionesPassword, PasswordTemporal FROM OperadoresSeguridad WHERE {filtro}",
            f =>
            {
                var hash = f.Binario("HashPassword");
                var salt = f.Binario("SaltPassword");
                var iteraciones = f.EnteroNulo("IteracionesPassword");
                var credencial = hash is { Length: > 0 } && salt is not null && iteraciones is > 0 ? new Credencial(hash, salt, iteraciones.Value) : null;
                return new Operador(f.Entero("IdOperador"), f.Texto("Nombre") ?? "", f.Texto("Email"), f.Logico("Activo"), credencial, f.Logico("PasswordTemporal"));
            }, valor);
        // Dos operadores con el mismo nombre harían ambiguo el acceso: no se elige ninguno.
        return encontrados.Count == 1 ? encontrados[0] : null;
    }

    private static Error? ValidarNombre(string nombre) =>
        nombre.Length is 0 or > 100
            ? new Error("Seguridad.NombreNoValido", "El nombre del operador no es válido.", null,
                "Debe tener entre 1 y 100 caracteres.", "Escribe el nombre con el que entrará el administrador.")
            : null;

    private static Error? ValidarContrasena(string? contrasena) =>
        (contrasena ?? "").Length < LongitudMinimaContrasena
            ? new Error("Seguridad.ContrasenaCorta", $"La contraseña debe tener al menos {LongitudMinimaContrasena} caracteres.", null,
                "Es demasiado corta.", "Elige una contraseña más larga; una frase corta es fácil de recordar.")
            : null;
}
