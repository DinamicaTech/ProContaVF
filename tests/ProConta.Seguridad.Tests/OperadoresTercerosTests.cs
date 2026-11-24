using ProConta.Nucleo.Auditoria;
using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;
using ProConta.Seguridad.Acceso;
using ProConta.Seguridad.Permisos;

namespace ProConta.Seguridad.Tests;

/// <summary>Nombre de acceso de los operadores (seguridad/pantallas 2026-11-24), sin BD.</summary>
public class NombresAccesoTests
{
    private static IdentidadOperador Op(int id, string pila, string? a1 = null, string? a2 = null) => new(id, 100 + id, pila, a1, a2, null);

    [Fact]
    public void SinCoincidenciasBastaElNombreDePila()
    {
        var n = NombresAcceso.Calcular([Op(1, "Ana", "López"), Op(2, "Luis", "Pérez")]);
        Assert.Equal("Ana", n[1]);
        Assert.Equal("Luis", n[2]);
    }

    [Fact]
    public void ConElMismoNombreSeAnadeElPrimerApellidoYLuegoElSegundo()
    {
        var n = NombresAcceso.Calcular([Op(1, "Ana", "López", "Gil"), Op(2, "ana", "Pérez"), Op(3, "Ana", "López", "Mas"), Op(4, "Luis")]);
        Assert.Equal("Ana López Gil", n[1]);
        Assert.Equal("ana Pérez", n[2]);
        Assert.Equal("Ana López Mas", n[3]);
        Assert.Equal("Luis", n[4]);
    }

    [Fact]
    public void SiSiguenIgualesSeAnadeElNumeroDelTercero()
    {
        var n = NombresAcceso.Calcular([Op(1, "Ana", "López"), Op(2, "Ana", "López")]);
        Assert.Equal("Ana López (101)", n[1]);
        Assert.Equal("Ana López (102)", n[2]);
    }

    [Fact]
    public void SinNombreDePilaUsaElNombreDelTercero()
    {
        var n = NombresAcceso.Calcular([new IdentidadOperador(1, 7, null, null, null, "Ana López  Gil")]);
        Assert.Equal("Ana López Gil", n[1]);
    }
}

/// <summary>Rol «Operador ProConta» de los terceros contra Access (seguridad/pantallas 2026-11-24).</summary>
[Trait("Categoria", "Access")]
public sealed class OperadoresTercerosTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private static readonly Actor _actor = new("admin.prueba");

    public void Dispose() => _copia.Dispose();

    private ServicioOperadores Operadores() => new(_copia.Bd());

    private async Task<(int IdOperador, int Grupo)> AdministradorAsync()
    {
        var r = await Operadores().CrearPrimerAdministradorAsync("Admin", "Prueba", null, "");
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        var grupo = await _copia.Bd().EjecutarAsync(s => s.EscalarAsync("SELECT IdGrupoOperadores FROM GruposOperadores WHERE Codigo = 'ADMINISTRADORES'"));
        return (r.Valor.IdOperador, Convert.ToInt32(grupo));
    }

    [Fact]
    public async Task ElPrimerAdministradorEsUnTerceroPersonaFisicaConElRol()
    {
        var (id, _) = await AdministradorAsync();
        var tercero = await _copia.Bd().EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT t.TipoPersona, t.NombrePersona, t.PrimerApellido, t.Nombre FROM Terceros AS t INNER JOIN OperadoresSeguridad AS o ON t.IdTercero = o.IdTercero WHERE o.IdOperador = ?",
            f => (Tipo: f.Texto("TipoPersona"), Pila: f.Texto("NombrePersona"), Apellido: f.Texto("PrimerApellido"), Nombre: f.Texto("Nombre")), id));
        Assert.Equal(("FISICA", "Admin", "Prueba", "Admin Prueba"), tercero);
        Assert.Equal(["Admin"], await Operadores().ListarNombresActivosAsync());
        Assert.True(await Operadores().EsAdministradorAsync(id));
        Assert.True(await Operadores().PuedeGestionarAsync(id));
        var acceso = new ServicioAcceso(_copia.Bd(), new ServicioSociedadesAccess(_copia.Bd()));
        Assert.True((await acceso.AutenticarAsync("admin", "")).EsCorrecto);
    }

    [Fact]
    public async Task ElAdministradorDaElRolYElOperadorEntraConSuNombreDePila()
    {
        var (admin, grupo) = await AdministradorAsync();
        var marta = await DatosSinteticos.TerceroPersonaAsync(_copia.Bd(), "Marta", "Sintética");
        var r = await Operadores().GuardarRolAsync(marta, true, [grupo], admin, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());

        var rol = await Operadores().RolDeTerceroAsync(marta);
        Assert.NotNull(rol);
        Assert.Equal("Marta", rol.NombreAcceso);
        Assert.Equal([grupo], rol.Grupos);
        var acceso = new ServicioAcceso(_copia.Bd(), new ServicioSociedadesAccess(_copia.Bd()));
        Assert.True((await acceso.AutenticarAsync("marta", "")).EsCorrecto);

        // Quitar el rol lo desactiva (no lo borra) y ya no sale en la lista.
        Assert.True((await Operadores().GuardarRolAsync(marta, false, [], admin, _actor)).EsCorrecto);
        Assert.False((await Operadores().RolDeTerceroAsync(marta))!.Activo);
        Assert.Equal(["Admin"], await Operadores().ListarNombresActivosAsync());
        Assert.False((await acceso.AutenticarAsync("Marta", "")).EsCorrecto);
    }

    [Fact]
    public async Task DosOperadoresConElMismoNombreSeDistinguenPorElApellido()
    {
        await AdministradorAsync();
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "Ana", primerApellido: "López");
        await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "Ana", primerApellido: "Pérez");
        Assert.Equal(["Admin", "Ana López", "Ana Pérez"], await Operadores().ListarNombresActivosAsync());
        Assert.Null(await Operadores().BuscarPorNombreAsync("Ana"));
        Assert.NotNull(await Operadores().BuscarPorNombreAsync("ana pérez"));
    }

    [Fact]
    public async Task SoloLosAdministradoresGestionanOperadores()
    {
        var (_, grupo) = await AdministradorAsync();
        var luis = await DatosSinteticos.OperadorSinContrasenaAsync(_copia.Bd(), "Luis");
        var tercero = await DatosSinteticos.TerceroPersonaAsync(_copia.Bd(), "Pepa");
        Assert.False(await Operadores().PuedeGestionarAsync(luis));
        var r = await Operadores().GuardarRolAsync(tercero, true, [grupo], luis, new Actor("Luis"));
        Assert.Equal("Seguridad.SinPermisoOperadores", r.Error!.Codigo);
        Assert.Null(await Operadores().RolDeTerceroAsync(tercero));
        Assert.Equal("Seguridad.SinPermisoOperadores", (await Operadores().EstablecerContrasenaAsync(1, "x", luis, new Actor("Luis"))).Error!.Codigo);
        Assert.True((await Operadores().EstablecerContrasenaAsync(luis, "propia", luis, new Actor("Luis"))).EsCorrecto); // la suya, sí
    }

    [Fact]
    public async Task NadieSeQuitaElRolNiElGrupoDeAdministradoresNiSeQuedaSinAdministrador()
    {
        var (admin, grupo) = await AdministradorAsync();
        var idTerceroAdmin = (await Operadores().ObtenerAsync(admin))!.IdTercero;
        Assert.Equal("Seguridad.QuitarseRol", (await Operadores().GuardarRolAsync(idTerceroAdmin, false, [], admin, _actor)).Error!.Codigo);
        Assert.Equal("Seguridad.QuitarseAdministrador", (await Operadores().GuardarRolAsync(idTerceroAdmin, true, [], admin, _actor)).Error!.Codigo);

        // Con otro administrador, el primero puede quitarle el grupo al segundo pero no dejar el programa sin ninguno.
        var (otro, terceroOtro) = await DatosSinteticos.OperadorConTerceroAsync(_copia.Bd(), "Otra");
        await DatosSinteticos.MiembroAsync(_copia.Bd(), otro, grupo);
        Assert.True((await Operadores().GuardarRolAsync(terceroOtro, true, [], admin, _actor)).EsCorrecto);
        Assert.False(await Operadores().EsAdministradorAsync(otro));
        await _copia.Bd().EjecutarAsync(s => s.EjecutarAsync("UPDATE OperadoresSeguridad SET Activo = False WHERE IdOperador = ?", admin));
        await DatosSinteticos.MiembroAsync(_copia.Bd(), otro, grupo);
        // «Otra» es ahora la única administradora activa: no puede salir del grupo (ni nadie quitárselo).
        Assert.Equal("Seguridad.QuitarseAdministrador", (await Operadores().GuardarRolAsync(terceroOtro, true, [], otro, _actor)).Error!.Codigo);
    }

    [Fact]
    public async Task UnaPersonaJuridicaNoPuedeSerOperador()
    {
        var (admin, grupo) = await AdministradorAsync();
        var empresa = await DatosSinteticos.TerceroPersonaAsync(_copia.Bd(), "Empresa Sintética SL", tipo: "JURIDICA");
        Assert.Equal("Seguridad.OperadorNoPersona", (await Operadores().GuardarRolAsync(empresa, true, [grupo], admin, _actor)).Error!.Codigo);
        // Comprobar sin guardar no escribe nada.
        var marta = await DatosSinteticos.TerceroPersonaAsync(_copia.Bd(), "Marta");
        Assert.True((await Operadores().GuardarRolAsync(marta, true, [grupo], admin, _actor, soloComprobar: true)).EsCorrecto);
        Assert.Null(await Operadores().RolDeTerceroAsync(marta));
    }

    [Fact]
    public async Task UnOperadorPasaAOtraFichaConSuNumeroYSusGrupos()
    {
        var (admin, grupo) = await AdministradorAsync();
        var (luis, fichaVieja) = await DatosSinteticos.OperadorConTerceroAsync(_copia.Bd(), "Luis");
        await DatosSinteticos.MiembroAsync(_copia.Bd(), luis, grupo);
        var fichaBuena = await DatosSinteticos.TerceroPersonaAsync(_copia.Bd(), "Luis", "Sintético");
        var empresa = await DatosSinteticos.TerceroPersonaAsync(_copia.Bd(), "Empresa Sintética SL", tipo: "JURIDICA");

        Assert.Equal("Seguridad.SinPermisoOperadores", (await Operadores().PasarATerceroAsync(admin, fichaBuena, luis + 1000, _actor)).Error!.Codigo);
        Assert.Equal("Seguridad.OperadorNoPersona", (await Operadores().PasarATerceroAsync(luis, empresa, admin, _actor)).Error!.Codigo);
        var idTerceroAdmin = (await Operadores().ObtenerAsync(admin))!.IdTercero;
        Assert.Equal("Seguridad.TerceroYaOperador", (await Operadores().PasarATerceroAsync(luis, idTerceroAdmin, admin, _actor)).Error!.Codigo);

        Assert.True((await Operadores().PasarATerceroAsync(luis, fichaBuena, admin, _actor)).EsCorrecto);
        Assert.Null(await Operadores().RolDeTerceroAsync(fichaVieja));
        var rol = await Operadores().RolDeTerceroAsync(fichaBuena);
        Assert.Equal(luis, rol!.IdOperador);
        Assert.Equal([grupo], rol.Grupos);
        Assert.Contains(await Operadores().ListarConTerceroAsync(), x => x.Operador.IdOperador == luis && x.NombreTercero == "Luis Sintético");
    }
}
