using ProConta.Datos.Esquema.Migraciones;
using ProConta.Pruebas.Access;

namespace ProConta.Plataforma.Configuracion.Tests;

public sealed class ResolutorTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF-pruebas", $"cfg-{Guid.NewGuid():N}");
    private readonly string _bd;
    private readonly string _otraBd;
    private readonly AlmacenConfiguracionLocalJson _almacen;
    private readonly Dictionary<string, string?> _entorno = new();

    public ResolutorTests()
    {
        Directory.CreateDirectory(_carpeta);
        // Ficheros vacíos: el resolutor solo comprueba rutas, nunca abre la BD.
        _bd = Path.Combine(_carpeta, "ProContaVF.accdb");
        _otraBd = Path.Combine(_carpeta, "Otra.accdb");
        File.WriteAllText(_bd, "");
        File.WriteAllText(_otraBd, "");
        _almacen = new AlmacenConfiguracionLocalJson(Path.Combine(_carpeta, "perfil"));
    }

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private ResolutorUbicacionDatos Resolutor() => new(_almacen, n => _entorno.GetValueOrDefault(n));

    [Fact]
    public void SinConfiguracionPideElegirLaBd()
    {
        var r = Resolutor().Resolver([]);
        Assert.Equal("Configuracion.NoConfigurada", r.Error!.Codigo);
        Assert.False(File.Exists(_almacen.Ruta));
    }

    [Fact]
    public void PrecedenciaArgumentoEntornoConfiguracionLocal()
    {
        _almacen.Guardar(new ConfiguracionLocal { RutaBaseDatos = _otraBd });
        Assert.Equal(OrigenUbicacion.ConfiguracionLocal, Resolutor().Resolver([]).Valor.Origen);

        _entorno[ResolutorUbicacionDatos.VariableEntorno] = _bd;
        var deEntorno = Resolutor().Resolver([]).Valor;
        Assert.Equal(OrigenUbicacion.Entorno, deEntorno.Origen);
        Assert.Equal(_bd, deEntorno.RutaBaseDatos);

        var deArgumento = Resolutor().Resolver(["--bd", _otraBd]).Valor;
        Assert.Equal(OrigenUbicacion.Argumento, deArgumento.Origen);
        Assert.Equal(_otraBd, deArgumento.RutaBaseDatos);
    }

    [Fact]
    public void FuentePrioritariaInvalidaNoPasaALaSiguiente()
    {
        _almacen.Guardar(new ConfiguracionLocal { RutaBaseDatos = _bd });
        _entorno[ResolutorUbicacionDatos.VariableEntorno] = Path.Combine(_carpeta, "NoExiste.accdb");
        var r = Resolutor().Resolver([]);
        Assert.Equal("Configuracion.FicheroAusente", r.Error!.Codigo);
        Assert.Contains("PROCONTA_BD", r.Error.Mensaje);
        Assert.Equal(Path.Combine(_carpeta, "NoExiste.accdb"), r.Error.RegistroAfectado);
    }

    [Fact]
    public void ArgumentosIncompatiblesSonError()
    {
        Assert.Equal("Configuracion.ArgumentosIncompatibles", Resolutor().Resolver(["--bd", _bd, "--bd", _otraBd]).Error!.Codigo);
        Assert.True(Resolutor().Resolver(["--bd", _bd, "--bd", _bd]).EsCorrecto);
        Assert.Equal("Configuracion.ArgumentoIncompleto", Resolutor().Resolver(["--bd"]).Error!.Codigo);
    }

    [Theory]
    [InlineData("relativa.accdb", "Configuracion.RutaRelativa")]
    [InlineData(@"C:\ProContaVF-carpeta-que-no-existe\ProContaVF.accdb", "Configuracion.CarpetaInexistente")]
    public void RutasInvalidasExplicanElMotivo(string ruta, string codigo)
    {
        var r = Resolutor().ValidarSeleccion(ruta);
        Assert.Equal(codigo, r.Error!.Codigo);
        Assert.False(string.IsNullOrWhiteSpace(r.Error.AccionCorrectora));
    }

    [Fact]
    public void NoEsAccdbSeRechaza()
    {
        var txt = Path.Combine(_carpeta, "datos.txt");
        File.WriteAllText(txt, "");
        Assert.Equal("Configuracion.NoEsAccdb", Resolutor().ValidarSeleccion(txt).Error!.Codigo);
    }

    [Fact]
    public void SeleccionCanceladaNoAlteraLaConfiguracionYLaConfirmadaSeGuarda()
    {
        _almacen.Guardar(new ConfiguracionLocal { RutaBaseDatos = _bd });
        var resolutor = Resolutor();

        var seleccion = resolutor.ValidarSeleccion(_otraBd); // el operador elige… y cancela
        Assert.True(seleccion.EsCorrecto);
        Assert.Equal(_bd, _almacen.Leer()!.RutaBaseDatos);

        Assert.True(resolutor.GuardarSeleccion(seleccion.Valor).EsCorrecto); // ahora confirma
        Assert.Equal(_otraBd, _almacen.Leer()!.RutaBaseDatos);
        Assert.False(File.Exists(_almacen.Ruta + ".tmp"));
    }

    [Fact]
    public void ConfiguracionDanadaSeInformaSinPerderla()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_almacen.Ruta)!);
        File.WriteAllText(_almacen.Ruta, "{ no es json");
        var r = Resolutor().Resolver([]);
        Assert.Equal("Configuracion.Danada", r.Error!.Codigo);
        Assert.Equal("{ no es json", File.ReadAllText(_almacen.Ruta));
    }
}

[Trait("Categoria", "Access")]
public sealed class ComprobadorArranqueTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task BdCompatibleSeAcepta()
    {
        var r = await ComprobadorArranque.ComprobarAsync(new UbicacionDatos(_copia.Ruta, OrigenUbicacion.Argumento));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(CatalogoMigraciones.VersionPrograma, r.Valor.VersionBd);
    }

    [Fact]
    public async Task BdNoProContaSeRechazaSinModificarla()
    {
        await _copia.Bd().EjecutarAsync(s => s.EjecutarAsync("DROP TABLE VersionesEsquema"));
        var huella = Datos.Esquema.ActualizadorEsquema.Sha256(_copia.Ruta);
        var r = await ComprobadorArranque.ComprobarAsync(new UbicacionDatos(_copia.Ruta, OrigenUbicacion.Argumento));
        Assert.Equal("Esquema.NoEsProConta", r.Error!.Codigo);
        Assert.Equal(huella, Datos.Esquema.ActualizadorEsquema.Sha256(_copia.Ruta));
    }
}
