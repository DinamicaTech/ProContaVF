using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;
using ProConta.Seguridad.Acceso;

namespace ProConta.Seguridad.Tests;

[Trait("Categoria", "Access")]
public sealed class ListaOperadoresTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();

    public void Dispose() => _copia.Dispose();

    private ServicioAcceso Servicio() => new(_copia.Bd(), new ServicioSociedadesAccess(_copia.Bd()));

    [Fact]
    public async Task LaListaSeMuestraSalvoQueElParametroLaOculte()
    {
        var acceso = Servicio();
        Assert.True(await acceso.Operadores.MostrarListaOperadoresAsync()); // sin fila de configuración
        await _copia.Bd().EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion) VALUES (?)", (byte)1));
        Assert.True(await acceso.Operadores.MostrarListaOperadoresAsync()); // por defecto, no se oculta
        await _copia.Bd().EjecutarAsync(s => s.EjecutarAsync("UPDATE Configuracion SET OcultarListaOperadoresAcceso = True"));
        Assert.False(await acceso.Operadores.MostrarListaOperadoresAsync());
    }

    [Fact]
    public async Task SoloSeListanLosOperadoresActivos()
    {
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "zeta.prueba");
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "alfa.prueba");
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "inactivo.prueba", activo: false);
        Assert.Equal(["alfa.prueba", "zeta.prueba"], await Servicio().Operadores.ListarNombresActivosAsync());
    }

    [Fact]
    public async Task ElegirUnOperadorSinContrasenaLoAutenticaYConContrasenaNoRegistraFallo()
    {
        var acceso = Servicio();
        await acceso.Operadores.CrearPrimerAdministradorAsync("admin.prueba", "frase de prueba larga");
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "libre.prueba");
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "inactivo.prueba", activo: false);

        Assert.NotNull(await acceso.AutenticarSinContrasenaAsync("libre.prueba"));
        Assert.Null(await acceso.AutenticarSinContrasenaAsync("admin.prueba"));
        Assert.Null(await acceso.AutenticarSinContrasenaAsync("inactivo.prueba"));
        var fallidos = await _copia.Bd().EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM AuditoriaSeguridad WHERE TipoEvento = 'LOGIN_FALLIDO'"));
        Assert.Equal(0, Convert.ToInt32(fallidos));
    }
}
