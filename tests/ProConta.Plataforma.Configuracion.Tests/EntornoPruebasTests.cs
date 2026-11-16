using ProConta.Datos.Esquema;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Pruebas.Access;

namespace ProConta.Plataforma.Configuracion.Tests;

/// <summary>Elección de entorno antes del acceso (plataforma/entorno-pruebas), sin BD: solo rutas y configuración local.</summary>
public sealed class SelectorEntornoTests : IDisposable
{
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF-pruebas", $"entorno-{Guid.NewGuid():N}");
    private readonly string _produccion;
    private readonly string _pruebas;

    public SelectorEntornoTests()
    {
        Directory.CreateDirectory(Path.Combine(_carpeta, "datos"));
        Directory.CreateDirectory(Path.Combine(_carpeta, "datospruebas"));
        _produccion = Path.Combine(_carpeta, "datos", "ProConta.accdb");
        _pruebas = Path.Combine(_carpeta, "datospruebas", "ProConta.accdb");
        File.WriteAllText(_produccion, "");
        File.WriteAllText(_pruebas, "");
    }

    public void Dispose() => Directory.Delete(_carpeta, recursive: true);

    private UbicacionDatos Prod => new(_produccion, OrigenUbicacion.Argumento);
    private ConfiguracionLocal ConPruebas => new() { RutaBaseDatos = _produccion, Pruebas = new EntornoPruebasLocal(_pruebas, _produccion, "yo@example.invalid") };

    [Fact]
    public void Sin_copia_de_pruebas_entra_en_produccion_sin_preguntar()
    {
        var r = SelectorEntorno.Resolver(Prod, [], new ConfiguracionLocal { RutaBaseDatos = _produccion });
        Assert.Equal(EleccionEntorno.Produccion, r.Valor.Eleccion);
        Assert.Null(r.Valor.Pruebas);
        Assert.Equal(_produccion, r.Valor.Elegida.RutaBaseDatos);
    }

    [Fact]
    public void Con_copia_de_pruebas_pregunta_y_por_defecto_es_produccion()
    {
        var r = SelectorEntorno.Resolver(Prod, [], ConPruebas).Valor;
        Assert.Equal(EleccionEntorno.Preguntar, r.Eleccion);
        Assert.Equal(_pruebas, r.Pruebas!.RutaBaseDatos);
        Assert.Equal(_produccion, r.Elegida.RutaBaseDatos);
    }

    [Fact]
    public void El_argumento_entorno_decide_sin_preguntar()
    {
        Assert.Equal(_pruebas, SelectorEntorno.Resolver(Prod, ["--entorno", "pruebas"], ConPruebas).Valor.Elegida.RutaBaseDatos);
        Assert.Equal(EleccionEntorno.Produccion, SelectorEntorno.Resolver(Prod, ["--entorno", "produccion"], ConPruebas).Valor.Eleccion);
        Assert.Equal("Configuracion.EntornoDesconocido", SelectorEntorno.Resolver(Prod, ["--entorno", "otro"], ConPruebas).Error!.Codigo);
    }

    [Fact]
    public void Pedir_pruebas_sin_copia_explica_como_crearla()
    {
        var r = SelectorEntorno.Resolver(Prod, ["--entorno", "pruebas"], null);
        Assert.Equal("Configuracion.SinEntornoPruebas", r.Error!.Codigo);
        Assert.Contains("Actualizar la copia de pruebas", r.Error.AccionCorrectora);
    }

    [Fact]
    public void Una_copia_de_otra_produccion_no_se_ofrece()
    {
        var otra = new ConfiguracionLocal { Pruebas = new EntornoPruebasLocal(_pruebas, Path.Combine(_carpeta, "otra", "X.accdb")) };
        Assert.Equal(EleccionEntorno.Produccion, SelectorEntorno.Resolver(Prod, [], otra).Valor.Eleccion);
    }

    [Fact]
    public void Abrir_directamente_la_copia_de_pruebas_entra_en_pruebas()
    {
        var r = SelectorEntorno.Resolver(new UbicacionDatos(_pruebas, OrigenUbicacion.Argumento), [], ConPruebas);
        Assert.Equal(EleccionEntorno.Pruebas, r.Valor.Eleccion);
        Assert.Equal("Configuracion.EntornoContradictorio",
            SelectorEntorno.Resolver(new UbicacionDatos(_pruebas, OrigenUbicacion.Argumento), ["--entorno", "produccion"], ConPruebas).Error!.Codigo);
    }

    [Fact]
    public void Registrar_conserva_la_ubicacion_y_el_operador()
    {
        var almacen = new AlmacenConfiguracionLocalJson(Path.Combine(_carpeta, "perfil"));
        almacen.Guardar(new ConfiguracionLocal { RutaBaseDatos = _produccion, UltimoOperador = "ronald" });
        Assert.True(SelectorEntorno.Registrar(almacen, new EntornoPruebasLocal(_pruebas, _produccion, "yo@example.invalid")).EsCorrecto);
        var leida = almacen.Leer()!;
        Assert.Equal(_produccion, leida.RutaBaseDatos);
        Assert.Equal("ronald", leida.UltimoOperador);
        Assert.Equal("yo@example.invalid", leida.Pruebas!.DesviarCorreoA);
    }

    [Fact]
    public void Cambio_de_ruta_respeta_el_limite_de_carpeta()
    {
        var c = new CarpetaReplicada(@"C:\Docs\ProConta\datos", @"C:\Docs\ProConta\datospruebas");
        Assert.Equal(@"C:\Docs\ProConta\datospruebas\Nominas\a.xlsx", RefrescoPruebas.CambiarRuta(@"c:\docs\proconta\DATOS\Nominas\a.xlsx", c));
        Assert.Equal(@"C:\Docs\ProConta\datospruebas", RefrescoPruebas.CambiarRuta(@"C:\Docs\ProConta\datos", c));
        Assert.Null(RefrescoPruebas.CambiarRuta(@"C:\Docs\ProConta\datosviejos\a.txt", c));
        Assert.Equal(@"C:\Docs\ProConta\datospruebas", RefrescoPruebas.CarpetaPruebasPorDefecto(@"C:\Docs\ProConta\datos\ProConta.accdb"));
    }

    [Fact]
    public void Conector_de_pruebas_es_una_entrada_aparte()
    {
        var config = Path.Combine(_carpeta, "claude_desktop_config.json");
        new ConectorClaudeDesktop(config).Configurar(@"C:\ProConta\Conector\ProContaConector.exe", _produccion, "ronald");
        var pruebas = new ConectorClaudeDesktop(config, ConectorClaudeDesktop.ServidorDe(pruebas: true));
        Assert.Equal(EstadoConectorClaude.SinConector, pruebas.Comprobar(@"C:\ProConta\Conector\ProContaConector.exe", _pruebas).Estado);
        pruebas.Configurar(@"C:\ProConta\Conector\ProContaConector.exe", _pruebas, "ronald");
        Assert.Equal(EstadoConectorClaude.Correcto, pruebas.Comprobar(@"C:\ProConta\Conector\ProContaConector.exe", _pruebas).Estado);
        Assert.Equal(EstadoConectorClaude.Correcto, new ConectorClaudeDesktop(config).Comprobar(@"C:\ProConta\Conector\ProContaConector.exe", _produccion).Estado);
    }
}

/// <summary>Refresco de la copia de pruebas contra Access (plataforma/entorno-pruebas): copia, rutas, correo, VeriFactu y protecciones.</summary>
[Trait("Categoria", "Access")]
public sealed class RefrescoPruebasAccessTests : IDisposable
{
    private const int Ejercicio = 9201;
    private readonly string _raiz = BdPruebas.CarpetaTemporal();
    private readonly string _datos;
    private readonly string _produccion;
    private readonly string _documentos;
    private readonly string _carpetaPruebas;

    public RefrescoPruebasAccessTests()
    {
        _datos = Path.Combine(_raiz, "ProConta", "datos");
        _documentos = Path.Combine(_raiz, "Documentos", "Dinamica");
        _carpetaPruebas = Path.Combine(_raiz, "ProConta", RefrescoPruebas.NombreCarpetaPruebas);
        Directory.CreateDirectory(_datos);
        Directory.CreateDirectory(Path.Combine(_documentos, "2026"));
        Directory.CreateDirectory(Path.Combine(_datos, "Nominas"));
        Directory.CreateDirectory(Path.Combine(_datos, ComprobadorArranque.CarpetaCopiasEsquema));
        File.WriteAllText(Path.Combine(_documentos, "2026", "F-1.pdf"), "%PDF factura");
        File.WriteAllText(Path.Combine(_datos, "Nominas", "octubre.xlsx"), "nómina");
        File.WriteAllText(Path.Combine(_datos, ComprobadorArranque.CarpetaCopiasEsquema, "vieja.accdb"), "copia");
        _produccion = Path.Combine(_datos, "ProConta.accdb");
        using (var copia = BdPruebas.Nueva()) File.Copy(copia.Ruta, _produccion);

        var bd = new BaseDatosAccess(_produccion);
        DatosSinteticos.SociedadAsync(bd, "P01", "Pruebas Demo SL").GetAwaiter().GetResult();
        DatosSinteticos.EjercicioAsync(bd, Ejercicio, "P01", 2026).GetAwaiter().GetResult();
        bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ?, Logo = ? WHERE IdSociedad = 'P01'", _documentos, Path.Combine(_datos, "logo.png"));
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0)
                await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, OcultarListaOperadoresAcceso) VALUES (1, False)");
            await s.EjecutarAsync("INSERT INTO CuentasCorreo (IdSociedadEjercicio, Activa, Direccion, PuertoSmtp, SeguridadSmtp, ServidorPop3, PuertoPop3, SeguridadPop3, " +
                "EliminarTrasProcesar, IntervaloRecepcionMinutos, TiempoEsperaSegundos, Version) VALUES (?, True, 'real@example.invalid', 587, 'STARTTLS', 'pop.real.invalid', 110, 'STARTTLS', True, 5, 30, 1)", Ejercicio);
            await s.EjecutarAsync("INSERT INTO VeriFactuConfiguracion (IdVeriFactuConfiguracion, IdSociedad, Activo, Entorno, CertificateThumbprint) VALUES (1, 'P01', True, 'PRODUCCION', 'ABC')");
            return true;
        }).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        try { Directory.Delete(_raiz, recursive: true); } catch (IOException) { }
    }

    private static Task<T?> LeerAsync<T>(string bd, string sql, Func<IFilaDatos, T> f) =>
        new BaseDatosAccess(bd, ModoApertura.SoloLectura).EjecutarAsync(s => s.ConsultarPrimeroAsync(sql, f));

    private async Task<InformeRefresco> RefrescarAsync()
    {
        var refresco = new RefrescoPruebas(() => new DateTime(2026, 11, 16, 14, 0, 0));
        var plan = await refresco.PlanificarAsync(_produccion, RefrescoPruebas.CarpetaPruebasPorDefecto(_produccion));
        Assert.True(plan.EsCorrecto, plan.Error?.ToString());
        var r = await refresco.EjecutarAsync(plan.Valor, "yo@example.invalid");
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        return r.Valor;
    }

    [Fact]
    public async Task Crea_la_copia_marcada_sin_efectos_fuera_y_sin_tocar_produccion()
    {
        var huella = ActualizadorEsquema.Sha256(_produccion);
        var informe = await RefrescarAsync();
        var pruebas = Path.Combine(_carpetaPruebas, "ProConta.accdb");

        Assert.Equal(huella, ActualizadorEsquema.Sha256(_produccion));
        Assert.False((await InfoEntorno.LeerAsync(new BaseDatosAccess(_produccion, ModoApertura.SoloLectura))).EsPruebas);
        var entorno = await InfoEntorno.LeerAsync(new BaseDatosAccess(pruebas, ModoApertura.SoloLectura));
        Assert.True(entorno.EsPruebas);
        Assert.Equal(new DateTime(2026, 11, 16, 14, 0, 0), entorno.CopiaFecha);
        Assert.Equal(_produccion, entorno.CopiaOrigen);
        Assert.Equal("yo@example.invalid", await LeerAsync(pruebas, "SELECT TOP 1 CorreoDesviarA FROM Configuracion", f => f.Texto("CorreoDesviarA")));

        var (docs, logo) = (await LeerAsync(pruebas, "SELECT RutaDocumentos, Logo FROM Sociedades WHERE IdSociedad = 'P01'", f => (f.Texto("RutaDocumentos"), f.Texto("Logo"))))!;
        Assert.Equal(Path.Combine(_carpetaPruebas, RefrescoPruebas.CarpetaExternas, "Dinamica"), docs);
        Assert.Equal(Path.Combine(_carpetaPruebas, "logo.png"), logo);
        Assert.True(informe.RutasCambiadas >= 2);

        Assert.True(File.Exists(Path.Combine(_carpetaPruebas, "Nominas", "octubre.xlsx")));
        Assert.True(File.Exists(Path.Combine(_carpetaPruebas, RefrescoPruebas.CarpetaExternas, "Dinamica", "2026", "F-1.pdf")));
        Assert.False(File.Exists(Path.Combine(_carpetaPruebas, ComprobadorArranque.CarpetaCopiasEsquema, "vieja.accdb")));
        Assert.False(File.Exists(Path.Combine(_carpetaPruebas, "ProConta.nueva.accdb")));

        var (pop, borrar) = (await LeerAsync(pruebas, "SELECT ServidorPop3, EliminarTrasProcesar FROM CuentasCorreo", f => (f.Texto("ServidorPop3"), f.Logico("EliminarTrasProcesar"))))!;
        Assert.Null(pop);
        Assert.False(borrar);
        var (activo, ent, huellaCert) = (await LeerAsync(pruebas, "SELECT Activo, Entorno, CertificateThumbprint FROM VeriFactuConfiguracion", f => (f.Logico("Activo"), f.Texto("Entorno"), f.Texto("CertificateThumbprint"))))!;
        Assert.False(activo);
        Assert.Equal("PRUEBAS", ent);
        Assert.Equal("ABC", huellaCert);
        Assert.Equal("pop.real.invalid", await LeerAsync(_produccion, "SELECT ServidorPop3 FROM CuentasCorreo", f => f.Texto("ServidorPop3")));
    }

    [Fact]
    public async Task Un_segundo_refresco_conserva_la_cuenta_de_pruebas_y_quita_lo_que_ya_no_esta()
    {
        await RefrescarAsync();
        var pruebas = Path.Combine(_carpetaPruebas, "ProConta.accdb");
        await new BaseDatosAccess(pruebas).EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE CuentasCorreo SET Direccion = 'pruebas@example.invalid', ServidorPop3 = 'pop.pruebas.invalid', SecretoProtegido = 'x'"));
        File.WriteAllText(Path.Combine(_carpetaPruebas, "Nominas", "solo-en-pruebas.xlsx"), "x");

        var informe = await RefrescarAsync();
        Assert.Equal(["pruebas@example.invalid"], informe.CuentasPruebasConservadas);
        var (dir, pop, secreto) = (await LeerAsync(pruebas, "SELECT Direccion, ServidorPop3, SecretoProtegido FROM CuentasCorreo", f => (f.Texto("Direccion"), f.Texto("ServidorPop3"), f.Texto("SecretoProtegido"))))!;
        Assert.Equal("pruebas@example.invalid", dir);
        Assert.Equal("pop.pruebas.invalid", pop);
        Assert.Equal("x", secreto);
        Assert.False(File.Exists(Path.Combine(_carpetaPruebas, "Nominas", "solo-en-pruebas.xlsx")));
        Assert.True(File.Exists(informe.CopiaAnterior));
    }

    [Fact]
    public async Task No_refresca_desde_una_copia_de_pruebas_ni_sobre_una_bd_sin_marca_ni_dentro_de_produccion()
    {
        var refresco = new RefrescoPruebas();
        Assert.Equal("Pruebas.CarpetaSolapada", (await refresco.PlanificarAsync(_produccion, Path.Combine(_datos, "pruebas"))).Error!.Codigo);

        var ajena = Path.Combine(_raiz, "ajena");
        Directory.CreateDirectory(ajena);
        File.Copy(_produccion, Path.Combine(ajena, "ProConta.accdb"));
        Assert.Equal("Pruebas.DestinoNoEsPruebas", (await refresco.PlanificarAsync(_produccion, ajena)).Error!.Codigo);

        await RefrescarAsync();
        Assert.Equal("Pruebas.OrigenEsPruebas", (await refresco.PlanificarAsync(Path.Combine(_carpetaPruebas, "ProConta.accdb"), Path.Combine(_raiz, "otra"))).Error!.Codigo);
    }
}
