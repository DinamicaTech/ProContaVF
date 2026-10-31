using System.Security.Cryptography;
using ProConta.Nucleo.Auditoria;
using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;
using ProConta.Seguridad.Acceso;
using ProConta.Seguridad.Permisos;

namespace ProConta.Seguridad.Tests;

public class HashYPoliticaTests
{
    [Fact]
    public void ContrasenaSeVerificaYNoSeGuardaEnClaro()
    {
        var credencial = HashContrasena.Crear("frase de prueba larga");
        Assert.Equal(HashContrasena.BytesHash, credencial.Hash.Length);
        Assert.Equal(HashContrasena.BytesSalt, credencial.Salt.Length);
        Assert.Equal(210_000, credencial.Iteraciones);
        Assert.True(HashContrasena.Verificar("frase de prueba larga", credencial));
        Assert.False(HashContrasena.Verificar("Frase de prueba larga", credencial));
        Assert.False(HashContrasena.Verificar(null, credencial));
    }

    [Fact]
    public void CompatibleConLasCredencialesDelProgramaAnterior()
    {
        // Misma derivación que el ServicioSeguridad antiguo: PBKDF2-HMAC-SHA256, 32 bytes, iteraciones guardadas.
        var salt = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
        var hash = Rfc2898DeriveBytes.Pbkdf2("clave-antigua", salt, 1000, HashAlgorithmName.SHA256, 32);
        Assert.True(HashContrasena.Verificar("clave-antigua", new Credencial(hash, salt, 1000)));
    }

    [Fact]
    public void AmbitoSinConfigurarPermiteATodos()
    {
        Assert.True(PoliticaAmbito.Permite(Array.Empty<(int, string)>(), [], "001"));
    }

    [Fact]
    public void AmbitoConfiguradoSoloPermiteLasAsignaciones()
    {
        (int, string)[] asignaciones = [(1, "001"), (2, "002")];
        Assert.True(PoliticaAmbito.Permite(asignaciones, [1], "001"));
        Assert.False(PoliticaAmbito.Permite(asignaciones, [1], "002"));
        Assert.False(PoliticaAmbito.Permite(asignaciones, [3], "001")); // grupo sin filas no hereda acceso
        Assert.False(PoliticaAmbito.Permite(asignaciones, [], "001"));
    }
}

[Trait("Categoria", "Access")]
public sealed class AccesoAccessTests : IDisposable
{
    private sealed class RelojFijo : IReloj
    {
        public DateTime Ahora { get; set; } = new(2026, 10, 31, 20, 0, 0);
    }

    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly RelojFijo _reloj = new();

    public void Dispose() => _copia.Dispose();

    private ServicioAcceso Servicio() => new(_copia.Bd(), new ServicioSociedadesAccess(_copia.Bd()), _reloj);

    private async Task PrepararSociedadesAsync()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética uno");
        await DatosSinteticos.SociedadAsync(bd, "T02", "Sociedad sintética dos", "Q1234567D");
        await DatosSinteticos.EjercicioAsync(bd, 9001, "T01", 2025, cerrado: true);
        await DatosSinteticos.EjercicioAsync(bd, 9002, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(bd, 9003, "T02", 2026);
    }

    [Fact]
    public async Task PrimerAdministradorSoloSinOperadores()
    {
        var acceso = Servicio();
        Assert.False(await acceso.Operadores.HayOperadoresAsync());
        Assert.Equal("Seguridad.ContrasenaCorta", (await acceso.Operadores.CrearPrimerAdministradorAsync("admin", "corta")).Error!.Codigo);

        var admin = await acceso.Operadores.CrearPrimerAdministradorAsync("admin.prueba", "frase de prueba larga");
        Assert.True(admin.EsCorrecto, admin.Error?.ToString());
        Assert.True(await acceso.Operadores.HayOperadoresAsync());
        Assert.Single(await acceso.Operadores.GruposActivosAsync(admin.Valor.IdOperador));
        Assert.Equal("Seguridad.YaHayOperadores", (await acceso.Operadores.CrearPrimerAdministradorAsync("otro", "frase de prueba larga")).Error!.Codigo);

        Assert.True((await acceso.AutenticarAsync(" ADMIN.prueba ", "frase de prueba larga")).EsCorrecto);
    }

    [Fact]
    public async Task FallosDeAccesoNoDicenQueCredencialFallo()
    {
        var acceso = Servicio();
        await acceso.Operadores.CrearPrimerAdministradorAsync("admin.prueba", "frase de prueba larga");
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "inactivo.prueba", activo: false);

        var malaClave = await acceso.AutenticarAsync("admin.prueba", "otra frase cualquiera");
        var noExiste = await acceso.AutenticarAsync("nadie.prueba", "frase de prueba larga");
        var inactivo = await acceso.AutenticarAsync("inactivo.prueba", "");
        Assert.All(new[] { malaClave, noExiste, inactivo }, r => Assert.Equal("Acceso.CredencialesNoValidas", r.Error!.Codigo));
        Assert.Equal(malaClave.Error!.Mensaje, noExiste.Error!.Mensaje);

        var detalles = await _copia.Bd().EjecutarAsync(s => s.ConsultarAsync(
            "SELECT TipoEvento, Resultado, Detalle, Actor FROM AuditoriaSeguridad", f => $"{f.Texto("TipoEvento")}|{f.Texto("Resultado")}|{f.Texto("Detalle")}|{f.Texto("Actor")}"));
        Assert.Contains(detalles, d => d.StartsWith("LOGIN_FALLIDO|INACTIVO"));
        Assert.DoesNotContain(detalles, d => d.Contains("frase", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task OperadorHeredadoSinContrasenaEntraConContrasenaVacia()
    {
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "heredado.prueba");
        var acceso = Servicio();
        var sesion = await acceso.AutenticarAsync("heredado.prueba", "");
        Assert.True(sesion.EsCorrecto);
        Assert.True(sesion.Valor.SinContrasena);
        Assert.False((await acceso.AutenticarAsync("heredado.prueba", "algo")).EsCorrecto);
    }

    [Fact]
    public async Task CuatroFasesConEmpresasAutorizadasYPreferencias()
    {
        await PrepararSociedadesAsync();
        var bd = _copia.Bd();
        var acceso = Servicio();
        var idOperador = await DatosSinteticos.OperadorSinContrasenaAsync(bd, "operador.prueba");
        await acceso.Operadores.EstablecerContrasenaAsync(idOperador, "frase de prueba larga", new Actor("prueba"));
        var sesion = (await acceso.AutenticarAsync("operador.prueba", "frase de prueba larga")).Valor;
        Assert.Equal(new DateTimeOffset(_reloj.Ahora), sesion.FechaHoraAutenticacion);
        Assert.False(sesion.SinContrasena);

        // Sin configurar el ámbito, ve todas las empresas.
        Assert.Equal(["T01", "T02"], (await acceso.ListarSociedadesAsync(sesion)).Valor.Sociedades.Select(x => x.IdSociedad).Order());

        // Ámbito configurado: un grupo con T01 y otro sin filas.
        var conAcceso = await DatosSinteticos.GrupoAsync(bd, "CON_T01");
        var sinFilas = await DatosSinteticos.GrupoAsync(bd, "SIN_FILAS");
        await DatosSinteticos.AccesoSociedadAsync(bd, conAcceso, "T01");
        await DatosSinteticos.MiembroAsync(bd, idOperador, sinFilas);
        Assert.Empty((await acceso.ListarSociedadesAsync(sesion)).Valor.Sociedades);
        Assert.Equal("Acceso.SociedadNoDisponible", (await acceso.ListarEjerciciosAsync(sesion, "T01")).Error!.Codigo);

        await DatosSinteticos.MiembroAsync(bd, idOperador, conAcceso);
        var opciones = (await acceso.ListarSociedadesAsync(sesion)).Valor;
        Assert.Equal(["T01"], opciones.Sociedades.Select(x => x.IdSociedad));
        Assert.Equal("T01", opciones.IdPropuesta);

        // Empresa no autorizada e inexistente dan el mismo error.
        var noAutorizada = await acceso.ListarEjerciciosAsync(sesion, "T02");
        var inexistente = await acceso.ListarEjerciciosAsync(sesion, "ZZZ");
        Assert.Equal(noAutorizada.Error!.Mensaje, inexistente.Error!.Mensaje);

        var ejercicios = (await acceso.ListarEjerciciosAsync(sesion, "T01")).Valor;
        Assert.Equal(9002, ejercicios.IdPropuesto);

        // Ejercicio cerrado permite entrar, y queda como preferencia.
        var completo = await acceso.CompletarAsync(sesion, "T01", 9001);
        Assert.True(completo.EsCorrecto, completo.Error?.ToString());
        Assert.True(completo.Valor.EjercicioCerrado);
        Assert.Equal(9001, (await acceso.ListarEjerciciosAsync(sesion, "T01")).Valor.IdPropuesto);
        Assert.Equal("Organizacion.ContextoNoValido", (await acceso.CompletarAsync(sesion, "T01", 9003)).Error!.Codigo);
        Assert.True((await acceso.ValidarContextoAsync(completo.Valor)).EsCorrecto);
    }

    [Fact]
    public async Task LaSesionSeRevalidaEnCadaOperacion()
    {
        await PrepararSociedadesAsync();
        var bd = _copia.Bd();
        var acceso = Servicio();
        var id = await DatosSinteticos.OperadorSinContrasenaAsync(bd, "operador.prueba");
        var sesion = (await acceso.AutenticarAsync("operador.prueba", "")).Valor;
        var contexto = (await acceso.CompletarAsync(sesion, "T02", 9003)).Valor;

        // Otra instancia del servicio no reconoce la sesión.
        Assert.Equal("Acceso.SesionNoValida", (await Servicio().ValidarAsync(sesion)).Error!.Codigo);

        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE OperadoresSeguridad SET Activo = False WHERE IdOperador = ?", id));
        Assert.Equal("Acceso.SesionNoValida", (await acceso.ValidarContextoAsync(contexto)).Error!.Codigo);

        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE OperadoresSeguridad SET Activo = True WHERE IdOperador = ?", id));
        var nueva = (await acceso.AutenticarAsync("operador.prueba", "")).Valor;
        Assert.True((await acceso.ValidarAsync(nueva)).EsCorrecto);
        await acceso.CerrarSesionAsync(nueva);
        Assert.False((await acceso.ValidarAsync(nueva)).EsCorrecto);
    }
}
