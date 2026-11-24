using System.Security.Cryptography;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Seguridad.Permisos;

/// <summary>Credencial guardada: hash PBKDF2-HMAC-SHA256, salt e iteraciones (nunca la contraseña).</summary>
public sealed record Credencial(byte[] Hash, byte[] Salt, int Iteraciones);

/// <summary>
/// Operador: el rol «Operador ProConta» de un tercero persona física (tabla OperadoresSeguridad, seguridad/pantallas 2026-11-24).
/// <see cref="Nombre"/> es su nombre de acceso, calculado del nombre de pila del tercero (ver <see cref="NombresAcceso"/>);
/// el correo es el del tercero. Sin credencial = operador sin contraseña.
/// </summary>
public sealed record Operador(int IdOperador, string Nombre, string? Email, bool Activo, Credencial? Credencial, bool ContrasenaTemporal, int IdTercero = 0)
{
    public bool TieneContrasena => Credencial is not null;
}

/// <summary>Nombre y apellidos del tercero de un operador, de los que sale su nombre de acceso.</summary>
public sealed record IdentidadOperador(int IdOperador, int IdTercero, string? NombrePersona, string? PrimerApellido, string? SegundoApellido, string? NombreTercero);

/// <summary>
/// Nombre con el que cada operador entra (seguridad/pantallas 2026-11-24): el nombre de pila de su tercero; si otro operador se
/// llama igual (sin distinguir mayúsculas), se añade el primer apellido y, si aún coinciden, el segundo. Se compara con todos los
/// operadores, activos o no, para que el nombre de cada uno no cambie al activar o desactivar a otro. Si aun así coinciden, se
/// añade el número del tercero entre paréntesis.
/// </summary>
public static class NombresAcceso
{
    public static IReadOnlyDictionary<int, string> Calcular(IEnumerable<IdentidadOperador> operadores)
    {
        var lista = operadores.ToList();
        var opciones = lista.ToDictionary(o => o.IdOperador, Opciones);
        var nivel = lista.ToDictionary(o => o.IdOperador, _ => 0);
        string Nombre(int id) => opciones[id][nivel[id]];
        while (true)
        {
            var subido = false;
            foreach (var grupo in lista.GroupBy(o => Nombre(o.IdOperador), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                foreach (var o in grupo)
                    if (nivel[o.IdOperador] < opciones[o.IdOperador].Count - 1) { nivel[o.IdOperador]++; subido = true; }
            if (!subido) break;
        }
        var repetidos = lista.GroupBy(o => Nombre(o.IdOperador), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).SelectMany(g => g)
            .Select(o => o.IdOperador).ToHashSet();
        return lista.ToDictionary(o => o.IdOperador, o => repetidos.Contains(o.IdOperador) ? $"{Nombre(o.IdOperador)} ({o.IdTercero})" : Nombre(o.IdOperador));
    }

    /// <summary>Nombre de pila, con el primer apellido y con los dos (sin repetir una opción si falta un apellido).</summary>
    private static List<string> Opciones(IdentidadOperador o)
    {
        static string? Limpio(string? t) => string.IsNullOrWhiteSpace(t) ? null : string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var pila = Limpio(o.NombrePersona) ?? Limpio(o.NombreTercero) ?? $"Operador {o.IdOperador}";
        var r = new List<string> { pila };
        if (Limpio(o.NombrePersona) is null) return r;
        if (Limpio(o.PrimerApellido) is { } a1) r.Add($"{pila} {a1}");
        if (Limpio(o.SegundoApellido) is { } a2) r.Add($"{r[^1]} {a2}");
        return r;
    }
}

/// <summary>Grupo de operadores (tabla GruposOperadores).</summary>
public sealed record GrupoOperadores(int IdGrupo, string Codigo, string Nombre, string? Descripcion);

/// <summary>El rol «Operador ProConta» de un tercero, tal como lo muestra su ficha.</summary>
public sealed record RolOperadorTercero(int IdOperador, string NombreAcceso, bool Activo, bool TieneContrasena, IReadOnlyList<int> Grupos);

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
/// Operadores y grupos. Cada operador es el rol «Operador ProConta» de un tercero persona física (seguridad/pantallas
/// 2026-11-24): su nombre de acceso y su correo salen del tercero; la tabla OperadoresSeguridad solo guarda el tercero, si está
/// activo y la contraseña. Lo usan el acceso (buscar operador, lista), el asistente del primer administrador y la ficha del tercero.
/// </summary>
public sealed class ServicioOperadores(IBaseDatos bd, IReloj? reloj = null)
{
    public const string GrupoAdministradores = "ADMINISTRADORES";

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public async Task<bool> HayOperadoresAsync() =>
        Convert.ToInt32(await bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM OperadoresSeguridad"))) > 0;

    /// <summary>Busca por nombre de acceso, sin distinguir mayúsculas ni espacios de los extremos.</summary>
    public Task<Operador?> BuscarPorNombreAsync(string nombre) =>
        bd.EjecutarAsync<Operador?>(async s =>
        {
            var texto = (nombre ?? "").Trim();
            var encontrados = (await TodosAsync(s)).Where(o => string.Equals(o.Nombre, texto, StringComparison.OrdinalIgnoreCase)).ToList();
            return encontrados.Count == 1 ? encontrados[0] : null;
        });

    public Task<Operador?> ObtenerAsync(int idOperador) =>
        bd.EjecutarAsync<Operador?>(async s => (await TodosAsync(s)).FirstOrDefault(o => o.IdOperador == idOperador));

    /// <summary>Nombres de acceso de los operadores activos, para la lista del acceso.</summary>
    public Task<IReadOnlyList<string>> ListarNombresActivosAsync() =>
        bd.EjecutarAsync<IReadOnlyList<string>>(async s => (await TodosAsync(s)).Where(o => o.Activo).Select(o => o.Nombre)
            .OrderBy(n => n, StringComparer.Create(System.Globalization.CultureInfo.GetCultureInfo("es-ES"), true)).ToList());

    /// <summary>
    /// Parámetro general «Mostrar la lista de operadores en el acceso» (Configuracion.OcultarListaOperadoresAcceso,
    /// guardado en negativo). Sin fila de configuración, se muestra.
    /// </summary>
    public async Task<bool> MostrarListaOperadoresAsync()
    {
        var ocultar = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT TOP 1 OcultarListaOperadoresAcceso FROM Configuracion ORDER BY IdConfiguracion", f => (bool?)f.Logico("OcultarListaOperadoresAcceso")));
        return ocultar != true;
    }

    /// <summary>Grupos activos del operador.</summary>
    public Task<IReadOnlyList<int>> GruposActivosAsync(int idOperador) =>
        bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT g.IdGrupoOperadores FROM OperadoresGrupos AS og INNER JOIN GruposOperadores AS g ON og.IdGrupoOperadores = g.IdGrupoOperadores WHERE og.IdOperador = ? AND g.Activo = True",
            f => f.Entero("IdGrupoOperadores"), idOperador));

    /// <summary>Grupos activos, por nombre: los que se pueden marcar en el rol de un operador.</summary>
    public Task<IReadOnlyList<GrupoOperadores>> ListarGruposAsync() =>
        bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdGrupoOperadores, Codigo, Nombre, Descripcion FROM GruposOperadores WHERE Activo = True ORDER BY Nombre, Codigo",
            f => new GrupoOperadores(f.Entero("IdGrupoOperadores"), f.Texto("Codigo") ?? "", f.Texto("Nombre") ?? f.Texto("Codigo") ?? "", f.Texto("Descripcion"))));

    /// <summary>El operador está activo y es miembro del grupo ADMINISTRADORES (activo).</summary>
    public Task<bool> EsAdministradorAsync(int idOperador) => bd.EjecutarAsync(s => EsAdministradorAsync(s, idOperador));

    /// <summary>
    /// Puede dar y quitar el rol de operador, sus grupos y su contraseña (acción SEGURIDAD.OPERADORES): si la acción tiene grupos
    /// asignados, los miembros de esos grupos; si no, solo los administradores (seguridad/pantallas 2026-11-24).
    /// </summary>
    public Task<bool> PuedeGestionarAsync(int idOperador) => bd.EjecutarAsync(s => PuedeGestionarAsync(s, idOperador));

    /// <summary>Rol de operador de un tercero, o null si nunca lo ha tenido.</summary>
    public Task<RolOperadorTercero?> RolDeTerceroAsync(int idTercero) =>
        bd.EjecutarAsync<RolOperadorTercero?>(async s =>
        {
            var o = (await TodosAsync(s)).FirstOrDefault(x => x.IdTercero == idTercero);
            if (o is null) return null;
            var grupos = await s.ConsultarAsync("SELECT IdGrupoOperadores FROM OperadoresGrupos WHERE IdOperador = ?", f => f.Entero("IdGrupoOperadores"), o.IdOperador);
            return new RolOperadorTercero(o.IdOperador, o.Nombre, o.Activo, o.TieneContrasena, grupos);
        });

    /// <summary>
    /// Da, cambia o quita el rol «Operador ProConta» de un tercero con sus grupos (los activos; la pertenencia a grupos inactivos no se
    /// toca). Quitarlo desactiva el operador, nunca lo borra. Reglas: solo quien puede gestionar operadores; el tercero es una persona
    /// física con nombre; nadie se quita el rol ni el grupo ADMINISTRADORES a sí mismo; no se puede dejar sin ningún administrador
    /// activo. Con <paramref name="soloComprobar"/>, solo dice si se podría (la ficha lo comprueba antes de guardar).
    /// </summary>
    public Task<Resultado> GuardarRolAsync(int idTercero, bool activo, IReadOnlyCollection<int> grupos, int idOperadorActor, Actor actor, bool soloComprobar = false) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (!await PuedeGestionarAsync(s, idOperadorActor))
                return Resultado.Fallo(SinPermiso());
            var existente = await s.ConsultarPrimeroAsync("SELECT IdOperador, Activo FROM OperadoresSeguridad WHERE IdTercero = ?",
                f => (Id: (int?)f.Entero("IdOperador"), Activo: f.Logico("Activo")), idTercero);
            if (existente.Id is null && !activo) return Resultado.Correcto();
            if (activo)
            {
                var tercero = await s.ConsultarPrimeroAsync("SELECT TipoPersona, NombrePersona, Nombre FROM Terceros WHERE IdTercero = ?",
                    f => (Tipo: f.Texto("TipoPersona"), Pila: f.Texto("NombrePersona"), Nombre: f.Texto("Nombre")), idTercero);
                if (tercero.Nombre is null)
                    return Resultado.Fallo(new Error("Seguridad.TerceroInexistente", "No encuentro el tercero al que quieres dar el rol de operador.", $"Tercero {idTercero}",
                        "Puede que lo hayan borrado.", "Vuelve a abrir su ficha."));
                if (!string.Equals(tercero.Tipo?.Trim(), "FISICA", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(tercero.Pila))
                    return Resultado.Fallo(new Error("Seguridad.OperadorNoPersona", "Solo una persona física con nombre puede ser operador de ProConta.", tercero.Nombre,
                        "El nombre con el que se entra sale del nombre de pila de la persona.", "Pon el tipo «Persona física» y su nombre en la pestaña Identidad, o da el rol a la ficha de la persona."));
            }

            var activos = (await ListarGruposAsync(s)).Select(g => g.IdGrupo).ToHashSet();
            var nuevos = grupos.Where(activos.Contains).ToHashSet();
            var admin = await s.ConsultarPrimeroAsync("SELECT IdGrupoOperadores FROM GruposOperadores WHERE Codigo = ? AND Activo = True",
                f => (int?)f.Entero("IdGrupoOperadores"), GrupoAdministradores);
            var actuales = existente.Id is int id0
                ? (await s.ConsultarAsync("SELECT IdGrupoOperadores FROM OperadoresGrupos WHERE IdOperador = ?", f => f.Entero("IdGrupoOperadores"), id0)).ToHashSet()
                : new HashSet<int>();

            if (existente.Id == idOperadorActor)
            {
                if (!activo)
                    return Resultado.Fallo(new Error("Seguridad.QuitarseRol", "No puedes quitarte a ti mismo el rol de operador.", null,
                        "Te quedarías sin poder entrar en ProContaVF.", "Pídeselo a otro administrador."));
                if (admin is int a && actuales.Contains(a) && !nuevos.Contains(a))
                    return Resultado.Fallo(new Error("Seguridad.QuitarseAdministrador", "No puedes quitarte a ti mismo del grupo de administradores.", null,
                        "Perderías el permiso para gestionar operadores.", "Pídeselo a otro administrador."));
            }
            if (admin is int ga)
            {
                var administradores = (await s.ConsultarAsync(
                    "SELECT og.IdOperador FROM OperadoresGrupos AS og INNER JOIN OperadoresSeguridad AS o ON og.IdOperador = o.IdOperador WHERE og.IdGrupoOperadores = ? AND o.Activo = True",
                    f => f.Entero("IdOperador"), ga)).ToHashSet();
                var despues = new HashSet<int>(administradores);
                if (existente.Id is int idE)
                {
                    despues.Remove(idE);
                    if (activo && nuevos.Contains(ga)) despues.Add(idE);
                }
                else if (activo && nuevos.Contains(ga)) despues.Add(-1);
                if (administradores.Count > 0 && despues.Count == 0)
                    return Resultado.Fallo(new Error("Seguridad.UltimoAdministrador", "No puedo dejar ProContaVF sin ningún administrador activo.", null,
                        "Es el único operador activo del grupo de administradores, que es quien da de alta a los demás.",
                        "Da antes el rol de operador con el grupo de administradores a otra persona."));
            }
            if (soloComprobar) return Resultado.Correcto();

            int idOperador;
            string evento;
            if (existente.Id is int idEx)
            {
                idOperador = idEx;
                await s.EjecutarAsync("UPDATE OperadoresSeguridad SET Activo = ? WHERE IdOperador = ?", activo, idEx);
                evento = activo == existente.Activo ? "OPERADOR_GRUPOS" : activo ? "OPERADOR_REACTIVADO" : "OPERADOR_DESACTIVADO";
            }
            else
            {
                await s.EjecutarAsync("INSERT INTO OperadoresSeguridad (IdTercero, Activo, PasswordTemporal) VALUES (?, ?, ?)", idTercero, true, false);
                idOperador = await s.UltimoIdAsync();
                evento = "OPERADOR_ALTA";
            }
            if (activo)
            {
                foreach (var g in actuales.Where(g => activos.Contains(g) && !nuevos.Contains(g)))
                    await s.EjecutarAsync("DELETE FROM OperadoresGrupos WHERE IdOperador = ? AND IdGrupoOperadores = ?", idOperador, g);
                foreach (var g in nuevos.Where(g => !actuales.Contains(g)))
                    await s.EjecutarAsync("INSERT INTO OperadoresGrupos (IdOperador, IdGrupoOperadores) VALUES (?, ?)", idOperador, g);
            }
            var cambiaGrupos = activo && !nuevos.SetEquals(actuales.Where(activos.Contains));
            if (evento != "OPERADOR_GRUPOS" || cambiaGrupos)
            {
                var nombre = (await TodosAsync(s)).FirstOrDefault(o => o.IdOperador == idOperador)?.Nombre;
                await AuditoriaSeguridad.RegistrarAsync(s, _reloj, evento, idOperador.ToString(), actor.Nombre, "CORRECTO",
                    $"Tercero {idTercero} ({nombre}); grupos: {string.Join(", ", nuevos.OrderBy(g => g))}");
            }
            return Resultado.Correcto();
        });

    /// <summary>
    /// Crea el primer administrador cuando no hay ningún operador (la maestra no trae operadores por defecto): da de alta su tercero
    /// persona física con el nombre y los apellidos, le da el rol de operador y lo pone en el grupo ADMINISTRADORES (lo crea si falta).
    /// Si ya hay operadores, no hace nada. La contraseña es opcional y sin requisitos de longitud ni complejidad.
    /// </summary>
    public Task<Resultado<Operador>> CrearPrimerAdministradorAsync(string nombre, string? primerApellido, string? segundoApellido, string? contrasena) =>
        bd.EnTransaccionAsync(async s =>
        {
            var pila = Limpio(nombre) ?? "";
            var apellido1 = Limpio(primerApellido);
            var apellido2 = Limpio(segundoApellido);
            var error = ValidarNombre(pila, apellido1, apellido2);
            if (error is not null) return Resultado<Operador>.Fallo(error);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM OperadoresSeguridad")) > 0)
                return Resultado<Operador>.Fallo(new Error("Seguridad.YaHayOperadores",
                    "Ya hay operadores dados de alta; el asistente del primer administrador no se puede usar.", null,
                    "Solo se ofrece cuando la base de datos no tiene ningún operador.",
                    "Entra con un operador existente; un administrador puede dar de alta otros desde la ficha del tercero (pestaña Roles)."));

            var completo = string.Join(' ', new[] { pila, apellido1, apellido2 }.OfType<string>());
            var ahora = _reloj.Ahora;
            var idTercero = await CrearTerceroPersonaAsync(s, pila, apellido1, apellido2, null, ahora, "Asistente del primer administrador");
            var credencial = CredencialDe(contrasena);
            await s.EjecutarAsync(
                "INSERT INTO OperadoresSeguridad (IdTercero, Activo, HashPassword, SaltPassword, IteracionesPassword, PasswordTemporal, FechaHoraPassword) VALUES (?, ?, ?, ?, ?, ?, ?)",
                idTercero, true, (object?)credencial?.Hash ?? DBNull.Value, (object?)credencial?.Salt ?? DBNull.Value, (object?)credencial?.Iteraciones ?? DBNull.Value, false, credencial is null ? DBNull.Value : ahora);
            var id = await s.UltimoIdAsync();

            var grupo = await s.ConsultarPrimeroAsync("SELECT IdGrupoOperadores FROM GruposOperadores WHERE Codigo = ?", f => (int?)f.Entero("IdGrupoOperadores"), GrupoAdministradores);
            if (grupo is null)
            {
                await s.EjecutarAsync("INSERT INTO GruposOperadores (Codigo, Nombre, Descripcion, Activo) VALUES (?, ?, ?, ?)",
                    GrupoAdministradores, "Administradores", "Administración de ProContaVF", true);
                grupo = await s.UltimoIdAsync();
            }
            await s.EjecutarAsync("INSERT INTO OperadoresGrupos (IdOperador, IdGrupoOperadores) VALUES (?, ?)", id, grupo.Value);
            await AuditoriaSeguridad.RegistrarAsync(s, _reloj, "PRIMER_ADMINISTRADOR", id.ToString(), pila, "CORRECTO", $"Tercero {idTercero} ({completo})");
            return Resultado<Operador>.Correcto(new Operador(id, pila, null, true, credencial, false, idTercero));
        });

    /// <summary>Primer administrador solo con el nombre (sin apellidos).</summary>
    public Task<Resultado<Operador>> CrearPrimerAdministradorAsync(string nombre, string? contrasena) =>
        CrearPrimerAdministradorAsync(nombre, null, null, contrasena);

    /// <summary>
    /// Da de alta un tercero persona física con su historial de alta (lo usan el asistente del primer administrador y la migración
    /// que vincula los operadores antiguos a sus terceros). Devuelve su IdTercero.
    /// </summary>
    private static async Task<int> CrearTerceroPersonaAsync(ISesionDatos s, string nombre, string? primerApellido, string? segundoApellido, string? email, DateTime ahora, string actor)
    {
        var completo = string.Join(' ', new[] { nombre, primerApellido, segundoApellido }.Where(x => !string.IsNullOrWhiteSpace(x)));
        await s.EjecutarAsync(
            "INSERT INTO Terceros (TipoPersona, Nombre, NombrePersona, PrimerApellido, SegundoApellido, Email, TipoIdentificacion, Pais, Activo, FechaAlta, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "FISICA", completo, nombre, (object?)primerApellido ?? DBNull.Value, (object?)segundoApellido ?? DBNull.Value, (object?)email ?? DBNull.Value, "NIF", "724", true, ahora.Date, 1);
        var id = await s.UltimoIdAsync();
        await s.EjecutarAsync(
            "INSERT INTO HistorialTerceros (IdTercero, IdOperacion, FechaHora, Operador, Entidad, IdRegistro, Operacion, Campo, ValorAntiguo, ValorNuevo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            id, Guid.NewGuid().ToString(), ahora, actor, "Tercero", id, "ALTA", "Nombre", DBNull.Value, completo);
        return id;
    }

    /// <summary>
    /// Cambia la contraseña de un operador (la guarda como hash). Sin requisitos de longitud ni complejidad;
    /// vacía, le quita la contraseña y el operador entra con la contraseña vacía.
    /// </summary>
    public Task<Resultado> EstablecerContrasenaAsync(int idOperador, string? contrasena, Actor actor) =>
        bd.EnTransaccionAsync(s => EstablecerAsync(s, idOperador, contrasena, actor));

    /// <summary>
    /// Contraseña desde la ficha del tercero: la cambia quien puede gestionar operadores, o el propio operador la suya.
    /// </summary>
    public Task<Resultado> EstablecerContrasenaAsync(int idOperador, string? contrasena, int idOperadorActor, Actor actor) =>
        bd.EnTransaccionAsync(async s =>
            idOperador != idOperadorActor && !await PuedeGestionarAsync(s, idOperadorActor)
                ? Resultado.Fallo(SinPermiso())
                : await EstablecerAsync(s, idOperador, contrasena, actor));

    private async Task<Resultado> EstablecerAsync(ISesionDatos s, int idOperador, string? contrasena, Actor actor)
    {
        var credencial = CredencialDe(contrasena);
        var filas = await s.EjecutarAsync(
            "UPDATE OperadoresSeguridad SET HashPassword = ?, SaltPassword = ?, IteracionesPassword = ?, PasswordTemporal = ?, FechaHoraPassword = ? WHERE IdOperador = ?",
            (object?)credencial?.Hash ?? DBNull.Value, (object?)credencial?.Salt ?? DBNull.Value, (object?)credencial?.Iteraciones ?? DBNull.Value, false, credencial is null ? DBNull.Value : _reloj.Ahora, idOperador);
        if (filas == 0)
            return Resultado.Fallo(new Error("Seguridad.OperadorInexistente", "El operador ya no existe.", $"Operador {idOperador}",
                "Se ha borrado mientras se editaba.", "Vuelve a abrir la ficha del tercero."));
        await AuditoriaSeguridad.RegistrarAsync(s, _reloj, credencial is null ? "CONTRASENA_QUITADA" : "CONTRASENA_CAMBIADA", idOperador.ToString(), actor.Nombre, "CORRECTO");
        return Resultado.Correcto();
    }

    /// <summary>Todos los operadores con su nombre de acceso calculado (ver <see cref="NombresAcceso"/>).</summary>
    internal static async Task<IReadOnlyList<Operador>> TodosAsync(ISesionDatos s)
    {
        var filas = await s.ConsultarAsync(
            "SELECT o.IdOperador, o.IdTercero, o.Activo, o.HashPassword, o.SaltPassword, o.IteracionesPassword, o.PasswordTemporal, " +
            "t.NombrePersona, t.PrimerApellido, t.SegundoApellido, t.Nombre AS NombreTercero, t.Email AS EmailTercero " +
            "FROM OperadoresSeguridad AS o INNER JOIN Terceros AS t ON o.IdTercero = t.IdTercero",
            f =>
            {
                var hash = f.Binario("HashPassword");
                var salt = f.Binario("SaltPassword");
                var iteraciones = f.EnteroNulo("IteracionesPassword");
                var credencial = hash is { Length: > 0 } && salt is not null && iteraciones is > 0 ? new Credencial(hash, salt, iteraciones.Value) : null;
                var identidad = new IdentidadOperador(f.Entero("IdOperador"), f.Entero("IdTercero"), f.Texto("NombrePersona"), f.Texto("PrimerApellido"),
                    f.Texto("SegundoApellido"), f.Texto("NombreTercero"));
                return (Identidad: identidad, Activo: f.Logico("Activo"), Credencial: credencial, Temporal: f.Logico("PasswordTemporal"), Email: f.Texto("EmailTercero"));
            });
        var nombres = NombresAcceso.Calcular(filas.Select(f => f.Identidad));
        return filas.Select(f => new Operador(f.Identidad.IdOperador, nombres[f.Identidad.IdOperador], string.IsNullOrWhiteSpace(f.Email) ? null : f.Email.Trim(),
            f.Activo, f.Credencial, f.Temporal, f.Identidad.IdTercero)).ToList();
    }

    private static Task<IReadOnlyList<GrupoOperadores>> ListarGruposAsync(ISesionDatos s) =>
        s.ConsultarAsync("SELECT IdGrupoOperadores, Codigo, Nombre, Descripcion FROM GruposOperadores WHERE Activo = True",
            f => new GrupoOperadores(f.Entero("IdGrupoOperadores"), f.Texto("Codigo") ?? "", f.Texto("Nombre") ?? "", f.Texto("Descripcion")));

    private static async Task<bool> EsAdministradorAsync(ISesionDatos s, int idOperador) =>
        Convert.ToInt32(await s.EscalarAsync(
            "SELECT COUNT(*) FROM (OperadoresGrupos AS og INNER JOIN GruposOperadores AS g ON og.IdGrupoOperadores = g.IdGrupoOperadores) " +
            "INNER JOIN OperadoresSeguridad AS o ON og.IdOperador = o.IdOperador WHERE og.IdOperador = ? AND g.Codigo = ? AND g.Activo = True AND o.Activo = True",
            idOperador, GrupoAdministradores)) > 0;

    private static async Task<bool> PuedeGestionarAsync(ISesionDatos s, int idOperador)
    {
        var asignaciones = await s.ConsultarAsync("SELECT IdGrupoOperadores, CodigoAccion FROM PermisosAccionesGrupo WHERE CodigoAccion = ?",
            f => (Grupo: f.Entero("IdGrupoOperadores"), Recurso: f.Texto("CodigoAccion") ?? ""), Acciones.Operadores);
        if (asignaciones.Count == 0) return await EsAdministradorAsync(s, idOperador);
        var grupos = await s.ConsultarAsync(
            "SELECT g.IdGrupoOperadores FROM (OperadoresGrupos AS og INNER JOIN GruposOperadores AS g ON og.IdGrupoOperadores = g.IdGrupoOperadores) " +
            "INNER JOIN OperadoresSeguridad AS o ON og.IdOperador = o.IdOperador WHERE og.IdOperador = ? AND g.Activo = True AND o.Activo = True",
            f => f.Entero("IdGrupoOperadores"), idOperador);
        return PoliticaAmbito.Permite(asignaciones, grupos, Acciones.Operadores, StringComparer.OrdinalIgnoreCase);
    }

    private static Error SinPermiso() =>
        new("Seguridad.SinPermisoOperadores", "No tienes permiso para gestionar operadores.", null,
            "Dar o quitar el rol de operador, sus grupos y su contraseña es cosa de los administradores.",
            "Pídeselo a un miembro del grupo de administradores.");

    private static string? Limpio(string? t) => string.IsNullOrWhiteSpace(t) ? null : string.Join(' ', t.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static Error? ValidarNombre(string nombre, string? apellido1, string? apellido2) =>
        nombre.Length is 0 or > 150 || apellido1?.Length > 100 || apellido2?.Length > 100
            ? new Error("Seguridad.NombreNoValido", "El nombre del administrador no es válido.", null,
                "El nombre es obligatorio (hasta 150 caracteres) y cada apellido puede tener hasta 100.", "Escribe el nombre de pila y, si quieres, los apellidos.")
            : null;

    /// <summary>
    /// Criterio de seguridad/permisos (2026-11-14): la contraseña no es obligatoria ni tiene requisitos de
    /// longitud o complejidad («a» vale). Vacía = sin credencial.
    /// </summary>
    private static Credencial? CredencialDe(string? contrasena) =>
        string.IsNullOrEmpty(contrasena) ? null : HashContrasena.Crear(contrasena);
}
