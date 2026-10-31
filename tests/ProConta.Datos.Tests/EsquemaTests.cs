using ProConta.Datos.Esquema;
using ProConta.Datos.Esquema.Diccionario;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Pruebas.Access;

namespace ProConta.Datos.Tests;

public class MigracionTests
{
    [Fact]
    public void IdentificadorConFechaYHoraObligatorio()
    {
        Assert.Throws<ArgumentException>(() => new Migracion("20261031_PRUEBA", 261031901, "x", "datos/esquema", new AnadirColumna("T", "C", "LONG")));
        var m = new Migracion("20261031_1910_PRUEBA", 261031901, "x", "datos/esquema", new AnadirColumna("T", "C", "LONG"));
        Assert.Equal(64, m.Hash.Length);
    }

    [Fact]
    public void ElHashDependeDeLaDefinicionNoDeLaDescripcion()
    {
        var a = new Migracion("20261031_1910_PRUEBA", 261031901, "uno", "n", new AnadirColumna("T", "C", "LONG"));
        var b = new Migracion("20261031_1910_PRUEBA", 261031901, "otro texto", "n", new AnadirColumna("T", "C", "LONG"));
        var c = new Migracion("20261031_1910_PRUEBA", 261031901, "uno", "n", new AnadirColumna("T", "C", "TEXT(5)"));
        Assert.Equal(a.Hash, b.Hash);
        Assert.NotEqual(a.Hash, c.Hash);
    }

    [Fact]
    public void ElCatalogoExigeOrdenCrecientePosteriorALasHeredadas()
    {
        var actor = new Actor("prueba");
        var vieja = new Migracion("20261031_1910_A", 261001001, "a", "n", new AnadirColumna("T", "C", "LONG"));
        Assert.Throws<InvalidOperationException>(() => new ActualizadorEsquema(actor, [vieja]));
        var m1 = new Migracion("20261031_1910_A", 261031902, "a", "n", new AnadirColumna("T", "C", "LONG"));
        var m2 = new Migracion("20261031_1911_B", 261031901, "b", "n", new AnadirColumna("T", "D", "LONG"));
        Assert.Throws<InvalidOperationException>(() => new ActualizadorEsquema(actor, [m1, m2]));
    }

    [Fact]
    public void CatalogoRealValidoYConNodoSolicitante()
    {
        _ = new ActualizadorEsquema(new Actor("prueba"));
        Assert.Equal(LineaBase.Version + CatalogoMigraciones.Todas.Count, CatalogoMigraciones.VersionPrograma);
        Assert.All(CatalogoMigraciones.Todas, m => Assert.False(string.IsNullOrWhiteSpace(m.NodoSolicitante)));
    }
}

[Trait("Categoria", "Access")]
public sealed class EsquemaAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private static Migracion[] Catalogo(params Migracion[] extra) => [.. CatalogoMigraciones.Todas, .. extra];

    private static Migracion Primera => new("20261031_2320_PRUEBA_TABLA", 261031901, "Tabla de prueba", "datos/esquema",
        new CrearTabla("PruebaEsquema", "CREATE TABLE PruebaEsquema (Id COUNTER CONSTRAINT PK_PruebaEsquema PRIMARY KEY, Nombre TEXT(50) NOT NULL)"),
        new CrearIndice("PruebaEsquema", "UX_PruebaEsquema_Nombre", "CREATE UNIQUE INDEX UX_PruebaEsquema_Nombre ON PruebaEsquema (Nombre)"));

    private static Migracion Segunda => new("20261031_2321_PRUEBA_COLUMNA", 261031902, "Columna de prueba", "datos/esquema",
        new AnadirColumna("PruebaEsquema", "Activa", "YESNO"));

    private static Migracion Rota => new("20261031_2322_PRUEBA_ROTA", 261031903, "Falla a mitad", "datos/esquema",
        new AnadirColumna("PruebaEsquema", "Antes", "LONG"),
        new PasoPersonalizado("Paso que falla", "SQL INVÁLIDO", (_, _) => Task.FromResult(false),
            (s, _) => s.EjecutarAsync("ALTER TABLE TablaQueNoExiste ADD COLUMN X LONG")));

    [Fact]
    public async Task LaMaestraEsLaVersion1YLaPlantillaEstaActualizada()
    {
        using var version1 = BdPruebas.NuevaVersion1();
        var d1 = await DiagnosticadorEsquema.DiagnosticarAsync(version1.Bd(ModoApertura.SoloLectura));
        Assert.Equal(EstadoEsquema.ConPendientes, d1.Estado);
        Assert.Equal(1, d1.VersionBd);
        Assert.Empty(d1.HeredadasAusentes);
        Assert.Equal(CatalogoMigraciones.Todas.Count, d1.Pendientes.Count);

        var d = await DiagnosticadorEsquema.DiagnosticarAsync(_copia.Bd(ModoApertura.SoloLectura));
        Assert.Equal(EstadoEsquema.Actualizada, d.Estado);
        Assert.Equal(CatalogoMigraciones.VersionPrograma, d.VersionBd);
    }

    [Fact]
    public async Task ActualizaConCopiaYLaSegundaEjecucionNoCambiaNada()
    {
        var copias = BdPruebas.CarpetaTemporal();
        var huellaPrevia = ActualizadorEsquema.Sha256(_copia.Ruta);
        var catalogo = Catalogo(Primera, Segunda);
        var actualizador = new ActualizadorEsquema(_actor, catalogo);

        var antes = await DiagnosticadorEsquema.DiagnosticarAsync(_copia.Bd(), catalogo);
        Assert.Equal(EstadoEsquema.ConPendientes, antes.Estado);
        Assert.Equal(2, antes.Pendientes.Count);

        var r = await actualizador.ActualizarAsync(_copia.Ruta, copias);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(CatalogoMigraciones.VersionPrograma, r.Valor.VersionInicial);
        Assert.Equal(CatalogoMigraciones.VersionPrograma + 2, r.Valor.VersionFinal);
        Assert.True(File.Exists(r.Valor.CopiaSeguridad));
        Assert.Equal(huellaPrevia, ActualizadorEsquema.Sha256(r.Valor.CopiaSeguridad!));

        var segunda = await actualizador.ActualizarAsync(_copia.Ruta, copias);
        Assert.True(segunda.EsCorrecto);
        Assert.Empty(segunda.Valor.Aplicadas);
        Assert.Null(segunda.Valor.CopiaSeguridad);
        Assert.Single(Directory.GetFiles(copias));

        var registradas = await _copia.Bd().EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdMigracion, HashMigracion FROM VersionesEsquema WHERE IdMigracion = ?", f => f.Texto("HashMigracion"), Primera.Id));
        Assert.Equal(Primera.Hash, Assert.Single(registradas));
    }

    [Fact]
    public async Task PasosYaHechosNoSeRepiten()
    {
        await _copia.Bd().EjecutarAsync(s => s.EjecutarAsync("CREATE TABLE PruebaEsquema (Id COUNTER CONSTRAINT PK_PruebaEsquema PRIMARY KEY, Nombre TEXT(50) NOT NULL)"));
        var r = await new ActualizadorEsquema(_actor, Catalogo(Primera)).ActualizarAsync(_copia.Ruta, BdPruebas.CarpetaTemporal());
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        var esquema = await _copia.Bd().EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        Assert.True(esquema.ExisteIndice("PruebaEsquema", "UX_PruebaEsquema_Nombre"));
    }

    [Fact]
    public async Task MigracionParcialSeDetectaYNoSeReejecuta()
    {
        var catalogo = Catalogo(Primera, Rota);
        var actualizador = new ActualizadorEsquema(_actor, catalogo);
        var r = await actualizador.ActualizarAsync(_copia.Ruta, BdPruebas.CarpetaTemporal());
        Assert.False(r.EsCorrecto);
        Assert.Equal("Esquema.MigracionFallida", r.Error!.Codigo);
        Assert.Contains("Restaura la copia", r.Error.AccionCorrectora);

        var d = await DiagnosticadorEsquema.DiagnosticarAsync(_copia.Bd(), catalogo);
        Assert.Equal(EstadoEsquema.MigracionParcial, d.Estado);
        Assert.Contains(d.Parciales, p => p.Contains(Rota.Id) && p.Contains("ERROR"));

        var otra = await actualizador.ActualizarAsync(_copia.Ruta, BdPruebas.CarpetaTemporal());
        Assert.Equal("Esquema.Parcial", otra.Error!.Codigo);
    }

    [Fact]
    public async Task BdMasNuevaQueElProgramaSeDetecta()
    {
        await _copia.Bd().EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO VersionesEsquema (IdMigracion, OrdenMigracion, Descripcion, FechaHoraAplicacion) VALUES (?, ?, ?, ?)",
            "20991231_2359_FUTURA", 991231001, "De una versión futura", DateTime.Now));
        var d = await DiagnosticadorEsquema.DiagnosticarAsync(_copia.Bd());
        Assert.Equal(EstadoEsquema.MasNuevaQueElPrograma, d.Estado);
        Assert.Equal("Esquema.MasNueva", d.ComoError()!.Codigo);
    }

    [Fact]
    public async Task BdSinControlDeVersionesNoEsProConta()
    {
        await _copia.Bd().EjecutarAsync(s => s.EjecutarAsync("DROP TABLE VersionesEsquema"));
        var d = await DiagnosticadorEsquema.DiagnosticarAsync(_copia.Bd());
        Assert.Equal(EstadoEsquema.NoEsProConta, d.Estado);
    }

    [Fact]
    public async Task NoActualizaSiOtraSesionTieneLaBdAbierta()
    {
        var copias = BdPruebas.CarpetaTemporal();
        var resultado = await _copia.Bd().EjecutarAsync(_ =>
            new ActualizadorEsquema(_actor, Catalogo(Primera)).ActualizarAsync(_copia.Ruta, copias));
        Assert.False(resultado.EsCorrecto);
        Assert.Contains("cierren ProContaVF", resultado.Error!.AccionCorrectora);
        Assert.Empty(Directory.GetFiles(copias));
    }

    [Fact]
    public async Task FormasPagoSinOrigenNoCambiaNada()
    {
        using var version1 = BdPruebas.NuevaVersion1();
        var huella = ActualizadorEsquema.Sha256(version1.Ruta);
        var copias = BdPruebas.CarpetaTemporal();
        var r = await new ActualizadorEsquema(_actor).ActualizarAsync(version1.Ruta, copias);
        Assert.Equal("Esquema.OrigenFormasPago", r.Error!.Codigo);
        Assert.Contains("--origen-formas-pago", r.Error.AccionCorrectora);
        Assert.Empty(Directory.GetFiles(copias));
        Assert.Equal(huella, ActualizadorEsquema.Sha256(version1.Ruta));
    }

    [Fact]
    public async Task FormasPagoPasaATablaPropiaConservandoCodigosYEjercicioCerradoSeAnade()
    {
        using var version1 = BdPruebas.NuevaVersion1();
        var carpeta = BdPruebas.CarpetaTemporal();
        var origen = BdPruebas.CrearOrigenFormasPago(carpeta);
        var parametros = new Dictionary<string, string> { [MigracionesOrganizacion.ParametroOrigenFormasPago] = origen };
        var r = await new ActualizadorEsquema(_actor, parametros: parametros).ActualizarAsync(version1.Ruta, Path.Combine(carpeta, "copias"));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(CatalogoMigraciones.VersionPrograma, r.Valor.VersionFinal);

        var bd = version1.Bd();
        var esquema = await bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        var tabla = esquema.Tabla("FormasPago")!;
        Assert.False(tabla.EsVinculada);
        Assert.Equal(["Id", "Descripcion", "SEPA"], tabla.Columnas.Select(c => c.Nombre));
        Assert.Equal("BYTE", tabla.Columna("Id")!.Tipo);
        Assert.False(esquema.ExisteTabla("FormasPagoMigracion"));
        Assert.True(esquema.ExisteColumna("SociedadesEjercicios", "EjercicioCerrado"));

        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT Id, Descripcion, SEPA FROM FormasPago ORDER BY Id",
            f => (Id: f.Entero("Id"), Descripcion: f.Texto("Descripcion"), Sepa: f.Logico("SEPA"))));
        Assert.Equal(BdPruebas.FormasPagoSinteticas.Select(x => (int)x.Codigo).Append(1).Order(), filas.Select(f => f.Id));
        Assert.Contains(esquema.Tabla("Facturas")!.Relaciones, x => x.Nombre == "FK_FormasPago_Facturas" && x.TablaReferenciada == "FormasPago");
        Assert.Contains(esquema.Tabla("Contratos")!.Relaciones, x => x.Nombre == "FK_FormasPago_Contratos");
        Assert.Equal([110], filas.Where(f => f.Sepa).Select(f => f.Id));

        var segunda = await new ActualizadorEsquema(_actor, parametros: parametros).ActualizarAsync(version1.Ruta, Path.Combine(carpeta, "copias"));
        Assert.True(segunda.EsCorrecto);
        Assert.Empty(segunda.Valor.Aplicadas);
    }

    [Fact]
    public async Task EjercicioCerradoEsNoPorDefecto()
    {
        await _copia.Bd().EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Sociedades (IdSociedad, Nombre, CIF) VALUES (?, ?, ?)", "T01", "Sociedad sintética", "B12345674");
            await s.EjecutarAsync("INSERT INTO SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio, Nombre, Activa) VALUES (?, ?, ?, ?, ?)",
                9001, "T01", "2026", "Sociedad sintética", true);
            return true;
        });
        var cerrado = await _copia.Bd().EjecutarAsync(s => s.EscalarAsync("SELECT EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", 9001));
        Assert.False(Convert.ToBoolean(cerrado));
    }

    [Fact]
    public async Task DiccionarioUnMdPorAreaConDueno()
    {
        var carpeta = BdPruebas.CarpetaTemporal();
        var escritos = await GeneradorDiccionario.GenerarAsync(_copia.Bd(ModoApertura.SoloLectura), carpeta);
        Assert.Contains(escritos, e => e.EndsWith("organizacion.md"));
        var organizacion = await File.ReadAllTextAsync(Path.Combine(carpeta, "organizacion.md"));
        Assert.StartsWith(GeneradorDiccionario.Cabecera, organizacion);
        Assert.Contains("## SociedadesEjercicios", organizacion);
        Assert.Contains("`organizacion/sociedades-ejercicios`", organizacion);
        Assert.Contains("| SEPA | YESNO |", organizacion);
        Assert.False(File.Exists(Path.Combine(carpeta, "sin-dueno.md")), "Hay tablas de la maestra sin dueño funcional en DuenosTablas.");
    }
}
