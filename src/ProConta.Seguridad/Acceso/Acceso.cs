using System.Collections.Concurrent;
using System.Security.Cryptography;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Permisos;

namespace ProConta.Seguridad.Acceso;

/// <summary>
/// Sesión autenticada: identidad sin credenciales e instante de autenticación (RF-SEG-05). Solo la
/// crea <see cref="ServicioAcceso"/>, que la revalida en cada operación.
/// </summary>
public sealed class SesionAcceso
{
    internal SesionAcceso(int idOperador, string nombreOperador, DateTimeOffset fechaHoraAutenticacion, bool sinContrasena)
    {
        IdOperador = idOperador;
        NombreOperador = nombreOperador;
        FechaHoraAutenticacion = fechaHoraAutenticacion;
        SinContrasena = sinContrasena;
        Token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    public int IdOperador { get; }
    public string NombreOperador { get; }
    public DateTimeOffset FechaHoraAutenticacion { get; }

    /// <summary>El operador entró sin contraseña (operador heredado sin credencial): conviene que la defina.</summary>
    public bool SinContrasena { get; }

    internal string Token { get; }

    public Actor Actor => new(NombreOperador);

    public override string ToString() => $"{NombreOperador} ({IdOperador})";
}

/// <summary>Empresas que el operador puede elegir (fase 3) y la propuesta de su última selección válida.</summary>
public sealed record OpcionesSociedad(IReadOnlyList<Sociedad> Sociedades, string? IdPropuesta);

/// <summary>Ejercicios de la empresa elegida (fase 4) y la propuesta de su última selección válida.</summary>
public sealed record OpcionesEjercicio(IReadOnlyList<SociedadEjercicio> Ejercicios, int? IdPropuesto);

/// <summary>Resultado del acceso completo: sesión y contexto validado.</summary>
public sealed record ContextoAcceso(SesionAcceso Sesion, ContextoSociedadEjercicio Contexto, string RazonSocial, bool EjercicioCerrado);

/// <summary>
/// Acceso en cuatro fases (RF-SEG-01): usuario, contraseña, empresa y ejercicio. Las empresas solo se
/// consultan tras autenticar y solo las autorizadas; los fallos no dicen qué credencial falló ni si
/// existe una empresa no autorizada. Un ejercicio cerrado permite entrar (para consultar).
/// </summary>
public sealed class ServicioAcceso
{
    private readonly IBaseDatos _bd;
    private readonly IServicioSociedades _sociedades;
    private readonly ServicioOperadores _operadores;
    private readonly AutorizadorSociedades _autorizador;
    private readonly IReloj _reloj;
    private readonly ConcurrentDictionary<string, SesionAcceso> _sesiones = new(StringComparer.Ordinal);

    public ServicioAcceso(IBaseDatos bd, IServicioSociedades sociedades, IReloj? reloj = null)
    {
        _bd = bd;
        _sociedades = sociedades;
        _reloj = reloj ?? RelojSistema.Instancia;
        _operadores = new ServicioOperadores(bd, _reloj);
        _autorizador = new AutorizadorSociedades(bd, _operadores);
    }

    public ServicioOperadores Operadores => _operadores;

    /// <summary>Fases 1 y 2: usuario y contraseña. Una contraseña vacía solo vale para operadores sin contraseña.</summary>
    public async Task<Resultado<SesionAcceso>> AutenticarAsync(string usuario, string? contrasena)
    {
        var nombre = (usuario ?? "").Trim();
        var operador = nombre.Length == 0 ? null : await _operadores.BuscarPorNombreAsync(nombre);
        string? motivo = null;
        if (operador is null) motivo = "NO_VALIDO";
        else if (!operador.Activo) motivo = "INACTIVO";
        else if (operador.Credencial is { } credencial ? !HashContrasena.Verificar(contrasena, credencial) : !string.IsNullOrEmpty(contrasena))
            motivo = "NO_VALIDO";

        if (motivo is not null)
        {
            await _bd.EjecutarAsync(async s =>
            {
                await AuditoriaSeguridad.RegistrarAsync(s, _reloj, "LOGIN_FALLIDO", operador?.IdOperador.ToString(), nombre, motivo);
                return true;
            });
            return Resultado<SesionAcceso>.Fallo(CredencialesNoValidas());
        }

        var sesion = new SesionAcceso(operador!.IdOperador, operador.Nombre, new DateTimeOffset(_reloj.Ahora), !operador.TieneContrasena);
        _sesiones[sesion.Token] = sesion;
        await _bd.EjecutarAsync(async s =>
        {
            await AuditoriaSeguridad.RegistrarAsync(s, _reloj, "LOGIN_CORRECTO", operador.IdOperador.ToString(), operador.Nombre, "CORRECTO",
                sesion.SinContrasena ? "Operador sin contraseña." : null);
            return true;
        });
        return Resultado<SesionAcceso>.Correcto(sesion);
    }

    /// <summary>La sesión sigue siendo válida: la emitió este servicio, no se ha cerrado y el operador sigue activo.</summary>
    public async Task<Resultado> ValidarAsync(SesionAcceso sesion)
    {
        if (!_sesiones.TryGetValue(sesion.Token, out var emitida) || !ReferenceEquals(emitida, sesion))
            return Resultado.Fallo(SesionNoValida());
        var operador = await _operadores.ObtenerAsync(sesion.IdOperador);
        if (operador is not { Activo: true })
        {
            _sesiones.TryRemove(sesion.Token, out _);
            return Resultado.Fallo(SesionNoValida());
        }
        return Resultado.Correcto();
    }

    /// <summary>Fase 3: empresas autorizadas, con la última elegida si sigue siendo accesible.</summary>
    public async Task<Resultado<OpcionesSociedad>> ListarSociedadesAsync(SesionAcceso sesion)
    {
        var valida = await ValidarAsync(sesion);
        if (!valida.EsCorrecto) return Resultado<OpcionesSociedad>.Fallo(valida.Errores);
        var todas = await _sociedades.ListarSociedadesAsync();
        var permitidas = (await _autorizador.FiltrarAsync(sesion.IdOperador, todas.Select(t => t.IdSociedad))).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var lista = todas.Where(t => permitidas.Contains(t.IdSociedad)).ToList();
        var preferida = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT IdSociedad FROM PreferenciasAccesoOperador WHERE IdOperador = ?", f => f.Texto("IdSociedad"), sesion.IdOperador));
        var propuesta = lista.FirstOrDefault(t => string.Equals(t.IdSociedad, preferida, StringComparison.OrdinalIgnoreCase))?.IdSociedad
            ?? (lista.Count == 1 ? lista[0].IdSociedad : null);
        return Resultado<OpcionesSociedad>.Correcto(new OpcionesSociedad(lista, propuesta));
    }

    /// <summary>Fase 4: ejercicios de una empresa autorizada, con el último elegido si sigue existiendo.</summary>
    public async Task<Resultado<OpcionesEjercicio>> ListarEjerciciosAsync(SesionAcceso sesion, string idSociedad)
    {
        var autorizada = await ComprobarSociedadAsync(sesion, idSociedad);
        if (!autorizada.EsCorrecto) return Resultado<OpcionesEjercicio>.Fallo(autorizada.Errores);
        var ejercicios = await _sociedades.ListarEjerciciosAsync(idSociedad);
        var preferido = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT IdSociedadEjercicio FROM PreferenciasAccesoEjercicio WHERE IdOperador = ? AND IdSociedad = ?",
            f => (int?)f.Entero("IdSociedadEjercicio"), sesion.IdOperador, idSociedad));
        var propuesto = ejercicios.FirstOrDefault(e => e.IdSociedadEjercicio == preferido)?.IdSociedadEjercicio
            ?? ejercicios.FirstOrDefault(e => e.Activa)?.IdSociedadEjercicio
            ?? ejercicios.FirstOrDefault()?.IdSociedadEjercicio;
        return Resultado<OpcionesEjercicio>.Correcto(new OpcionesEjercicio(ejercicios, propuesto));
    }

    /// <summary>Completa el acceso: valida empresa y ejercicio, guarda las preferencias y devuelve el contexto.</summary>
    public async Task<Resultado<ContextoAcceso>> CompletarAsync(SesionAcceso sesion, string idSociedad, int idSociedadEjercicio)
    {
        var autorizada = await ComprobarSociedadAsync(sesion, idSociedad);
        if (!autorizada.EsCorrecto) return Resultado<ContextoAcceso>.Fallo(autorizada.Errores);
        var contexto = await _sociedades.ObtenerContextoAsync(idSociedad, idSociedadEjercicio);
        if (!contexto.EsCorrecto) return Resultado<ContextoAcceso>.Fallo(contexto.Errores);
        var ejercicio = (await _sociedades.ObtenerEjercicioAsync(idSociedadEjercicio))!;

        await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("DELETE FROM PreferenciasAccesoOperador WHERE IdOperador = ?", sesion.IdOperador);
            await s.EjecutarAsync("INSERT INTO PreferenciasAccesoOperador (IdOperador, IdSociedad) VALUES (?, ?)", sesion.IdOperador, idSociedad);
            await s.EjecutarAsync("DELETE FROM PreferenciasAccesoEjercicio WHERE IdOperador = ? AND IdSociedad = ?", sesion.IdOperador, idSociedad);
            await s.EjecutarAsync("INSERT INTO PreferenciasAccesoEjercicio (IdOperador, IdSociedad, IdSociedadEjercicio) VALUES (?, ?, ?)",
                sesion.IdOperador, idSociedad, idSociedadEjercicio);
            await AuditoriaSeguridad.RegistrarAsync(s, _reloj, "CONTEXTO_SELECCIONADO", sesion.IdOperador.ToString(), sesion.NombreOperador, "CORRECTO",
                contexto.Valor.ToString());
            return true;
        });
        return Resultado<ContextoAcceso>.Correcto(new ContextoAcceso(sesion, contexto.Valor, ejercicio.RazonSocial, ejercicio.Cerrado));
    }

    /// <summary>Revalida un contexto antes de una operación: sesión viva y empresa todavía autorizada.</summary>
    public async Task<Resultado> ValidarContextoAsync(ContextoAcceso contexto)
    {
        var autorizada = await ComprobarSociedadAsync(contexto.Sesion, contexto.Contexto.IdSociedad);
        if (!autorizada.EsCorrecto) return autorizada;
        var vigente = await _sociedades.ObtenerContextoAsync(contexto.Contexto.IdSociedad, contexto.Contexto.IdSociedadEjercicio);
        return vigente.EsCorrecto ? Resultado.Correcto() : Resultado.Fallo(vigente.Errores);
    }

    /// <summary>Cierra la sesión: su token deja de valer.</summary>
    public async Task CerrarSesionAsync(SesionAcceso sesion)
    {
        if (!_sesiones.TryRemove(sesion.Token, out _)) return;
        await _bd.EjecutarAsync(async s =>
        {
            await AuditoriaSeguridad.RegistrarAsync(s, _reloj, "LOGOUT", sesion.IdOperador.ToString(), sesion.NombreOperador, "CORRECTO");
            return true;
        });
    }

    private async Task<Resultado> ComprobarSociedadAsync(SesionAcceso sesion, string idSociedad)
    {
        var valida = await ValidarAsync(sesion);
        if (!valida.EsCorrecto) return valida;
        var existe = (await _sociedades.ListarSociedadesAsync()).Any(t => string.Equals(t.IdSociedad, idSociedad, StringComparison.OrdinalIgnoreCase));
        var permitida = existe && (await _autorizador.FiltrarAsync(sesion.IdOperador, [idSociedad])).Count == 1;
        // Mismo mensaje si no existe o no está autorizada: no se descubren otras empresas.
        return permitida
            ? Resultado.Correcto()
            : Resultado.Fallo(new Error("Acceso.SociedadNoDisponible", "No puedes trabajar con esa empresa.", null,
                "No está entre las empresas a las que tienes acceso.", "Elige una de las empresas de la lista."));
    }

    private static Error CredencialesNoValidas() =>
        new("Acceso.CredencialesNoValidas", "El usuario o la contraseña no son correctos.", null,
            "Los datos no coinciden con un operador activo.",
            "Revisa el usuario y la contraseña (mayúsculas incluidas) y vuelve a intentarlo; si has olvidado la contraseña, pídele a un administrador que te ponga una nueva.");

    private static Error SesionNoValida() =>
        new("Acceso.SesionNoValida", "Tu sesión ya no es válida.", null,
            "Se ha cerrado o tu operador se ha desactivado.", "Vuelve a identificarte.");
}
