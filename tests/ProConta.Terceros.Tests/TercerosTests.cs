using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Roles;

namespace ProConta.Terceros.Tests;

public class ReglasTerceroTests
{
    [Fact]
    public void TerceroNuevoNecesitaTipoYNombreSegunElTipo()
    {
        Assert.Equal("Tercero.SinTipo", new Tercero { Nombre = "x" }.Validar(null).Error!.Codigo);
        Assert.Equal("Tercero.SinNombre", new Tercero { TipoPersona = TipoPersona.Fisica, PrimerApellido = "Pérez" }.Validar(null).Error!.Codigo);
        Assert.Equal("Tercero.SinRazonSocial", new Tercero { TipoPersona = TipoPersona.Juridica }.Validar(null).Error!.Codigo);
        Assert.True(new Tercero { TipoPersona = TipoPersona.EntidadSinPersonalidad, RazonSocial = "Asociación sintética" }.Validar(null).EsCorrecto);
    }

    [Fact]
    public void NombreVisibleSeCalcula()
    {
        Assert.Equal("Ana Pérez Ruiz", new Tercero { TipoPersona = TipoPersona.Fisica, NombrePersona = " Ana ", PrimerApellido = "Pérez", SegundoApellido = "Ruiz" }.NombreCalculado());
        Assert.Equal("Sintética SL", new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Sintética SL ", NombreComercial = "Marca" }.NombreCalculado());
    }

    [Fact]
    public void NifEspanolSeValidaSoloSiEsNuevoOCambia()
    {
        var t = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Sintética SL", Nif = "B12345675" };
        Assert.Equal("NIF.Control", t.Validar(null).Error!.Codigo);
        t.IdTercero = 7;
        Assert.True(t.Validar("B12345675").EsCorrecto); // NIF antiguo sin cambios: no bloquea
        t.TipoIdentificacion = "VAT";
        Assert.True(t.Validar("OTRO").EsCorrecto); // identificación extranjera: libre
    }

    private static DatoContacto Email(string valor, bool activo = true, bool facturas = false) =>
        new() { Tipo = TipoDatoContacto.Email, Valor = valor, Activo = activo, EnvioFacturas = facturas };

    [Fact]
    public void EmailUnicoActivoResuelveSinMarcas()
    {
        var r = ResolutorDestinatarios.Resolver([Email("a@ejemplo.test"), Email("b@ejemplo.test", activo: false)], FinalidadEnvio.Facturas);
        Assert.Equal(["a@ejemplo.test"], r.Emails);
        Assert.Null(r.Incidencia);
    }

    [Fact]
    public void VariosActivosSinMarcarProducenIncidencia()
    {
        var r = ResolutorDestinatarios.Resolver([Email("a@ejemplo.test"), Email("b@ejemplo.test")], FinalidadEnvio.Facturas, "Tercero 1");
        Assert.Empty(r.Emails);
        Assert.Equal("Contactos.EmailAmbiguo", r.Incidencia!.Codigo);
    }

    [Fact]
    public void SeUsanTodosLosMarcadosParaLaFinalidad()
    {
        var datos = new[] { Email("a@ejemplo.test", facturas: true), Email("b@ejemplo.test", facturas: true), Email("c@ejemplo.test") };
        Assert.Equal(["a@ejemplo.test", "b@ejemplo.test"], ResolutorDestinatarios.Resolver(datos, FinalidadEnvio.Facturas).Emails);
        Assert.Equal("Contactos.EmailAmbiguo", ResolutorDestinatarios.Resolver(datos, FinalidadEnvio.Administrativos).Incidencia!.Codigo);
    }
}

[Trait("Categoria", "Access")]
public sealed class TercerosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private RepositorioTercerosAccess Repositorio() => new(_copia.Bd());

    private static Tercero Juridica(string razon, string? nif = "B12345674") =>
        new() { TipoPersona = TipoPersona.Juridica, RazonSocial = razon, Nif = nif, CodigoPostal = "28001", Provincia = "Madrid", Poblacion = "Madrid", Pais = "ESP" };

    [Fact]
    public async Task DosTercerosConIgualNifSiguenSiendoDistintos()
    {
        var repo = Repositorio();
        var a = await repo.GuardarAsync(Juridica("Sintética Uno SL"), _actor);
        var b = await repo.GuardarAsync(Juridica("Sintética Dos SL"), _actor);
        Assert.True(a.EsCorrecto && b.EsCorrecto);
        Assert.NotEqual(a.Valor.IdTercero, b.Valor.IdTercero);
        Assert.Equal(2, (await repo.BuscarAsync("B12345674")).Count);
    }

    [Fact]
    public async Task FichaCompletaSeGuardaEnUnaTransaccionConHistorial()
    {
        var repo = Repositorio();
        var t = Juridica("Sintética SL");
        var grupo = new ContactoLibre { Nombre = "Persona de contacto", Departamento = "Administración" };
        t.ContactosLibres.Add(grupo);
        t.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Email, Valor = "admin@ejemplo.test", EnvioFacturas = true, ContactoLibre = grupo });
        t.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Telefono, Valor = "600000000" });
        t.Domicilios.Add(new Domicilio(0, "ENVIO", "Calle Sintética 1", "28001", "Madrid", "Madrid", "ESP"));

        var guardado = await repo.GuardarAsync(t, _actor);
        Assert.True(guardado.EsCorrecto, guardado.Error?.ToString());
        var leido = (await repo.ObtenerAsync(guardado.Valor.IdTercero))!;
        Assert.Equal("Sintética SL", leido.Nombre);
        Assert.Equal(1, leido.Version);
        Assert.Single(leido.Domicilios);
        Assert.Equal("Persona de contacto", Assert.Single(leido.ContactosLibres).Nombre);
        Assert.Same(leido.ContactosLibres[0], leido.DatosContacto.Single(d => d.Tipo == TipoDatoContacto.Email).ContactoLibre);

        // Contacto libre no crea tercero.
        Assert.Equal(1, Convert.ToInt32(await _copia.Bd().EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM Terceros"))));

        leido.NombreComercial = "Marca sintética";
        var modificado = await repo.GuardarAsync(leido, _actor);
        Assert.True(modificado.EsCorrecto, modificado.Error?.ToString());
        Assert.Equal(2, modificado.Valor.Version);
        var historial = await _copia.Bd().EjecutarAsync(s => s.ConsultarAsync(
            "SELECT Operacion, Campo, ValorNuevo, Operador FROM HistorialTerceros WHERE IdTercero = ?", f => $"{f.Texto("Operacion")}|{f.Texto("Campo")}|{f.Texto("ValorNuevo")}|{f.Texto("Operador")}", leido.IdTercero));
        Assert.Contains("MODIFICACION|NombreComercial|Marca sintética|operador.prueba", historial);
    }

    [Fact]
    public async Task ConflictoDeVersionYErroresNoDejanNadaAMedias()
    {
        var repo = Repositorio();
        var id = (await repo.GuardarAsync(Juridica("Sintética SL"), _actor)).Valor.IdTercero;
        var primero = (await repo.ObtenerAsync(id))!;
        var segundo = (await repo.ObtenerAsync(id))!;
        primero.Telefono = "911111111";
        Assert.True((await repo.GuardarAsync(primero, _actor)).EsCorrecto);
        segundo.Telefono = "922222222";
        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(() => repo.GuardarAsync(segundo, _actor));
        Assert.Equal("911111111", (await repo.ObtenerAsync(id))!.Telefono);

        var malo = Juridica("Otra SL");
        malo.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Email, Valor = "no-es-un-email" });
        Assert.Equal("Contactos.EmailNoValido", (await repo.GuardarAsync(malo, _actor)).Error!.Codigo);
        Assert.Equal(1, Convert.ToInt32(await _copia.Bd().EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM Terceros"))));
    }

    [Fact]
    public async Task RolesIdempotentesYCuentasPorEjercicio()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(bd, 9002, "T01", 2026);
        var c2025 = new ContextoSociedadEjercicio("T01", 9001, 2025);
        var c2026 = new ContextoSociedadEjercicio("T01", 9002, 2026);
        var cuentas = new CatalogoCuentasAccess(bd);
        var cuenta2025 = (await cuentas.AltaAsync(c2025, new AltaCuenta("4300001", "Cliente sintético"))).Valor;
        var cuenta2026 = await cuentas.AltaAsync(c2026, new AltaCuenta("4300002", "Cliente sintético 2026"));
        Assert.True(cuenta2026.EsCorrecto, cuenta2026.Error?.ToString());

        var idTercero = (await Repositorio().GuardarAsync(Juridica("Sintética SL"), _actor)).Valor.IdTercero;
        var roles = new ServicioRoles(bd, cuentas);
        var cliente = (await roles.AsegurarClienteAsync(idTercero)).Valor;
        Assert.Equal(cliente, (await roles.AsegurarClienteAsync(idTercero)).Valor);
        var proveedor = (await roles.AsegurarProveedorAsync(idTercero)).Valor;
        var leidos = await roles.ObtenerAsync(idTercero);
        Assert.True(leidos.EsCliente && leidos.EsProveedor && !leidos.EsContacto);
        Assert.Equal(1, Convert.ToInt32(await bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM Terceros"))));

        Assert.Equal("Roles.CuentaDeOtroEjercicio", (await roles.AsignarCuentaClienteAsync(cliente, c2026, cuenta2025.IdCuentaContable)).Error!.Codigo);
        Assert.True((await roles.AsignarCuentaClienteAsync(cliente, c2025, cuenta2025.IdCuentaContable)).EsCorrecto);
        Assert.True((await roles.AsignarCuentaClienteAsync(cliente, c2026, cuenta2026.Valor.IdCuentaContable)).EsCorrecto);
        Assert.Equal("4300001", (await roles.CuentaClienteAsync(cliente, c2025))!.Codigo);
        Assert.Equal("4300002", (await roles.CuentaClienteAsync(cliente, c2026))!.Codigo);
        Assert.Null(await roles.CuentaProveedorAsync(proveedor, c2026));
        Assert.Equal("Roles.SinTercero", (await roles.AsegurarClienteAsync(999999)).Error!.Codigo);
    }

    [Fact]
    public async Task LasFilasExistentesRecibenVersionCeroAlMigrar()
    {
        using var version1 = BdPruebas.NuevaVersion1();
        await version1.Bd().EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo) VALUES (?, ?)", "Tercero sintético antiguo", true));
        var carpeta = BdPruebas.CarpetaTemporal();
        var parametros = new Dictionary<string, string> { [Datos.Esquema.Migraciones.MigracionesOrganizacion.ParametroOrigenFormasPago] = BdPruebas.CrearOrigenFormasPago(carpeta) };
        var r = await new Datos.Esquema.ActualizadorEsquema(_actor, parametros: parametros).ActualizarAsync(version1.Ruta, Path.Combine(carpeta, "copias"));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        var antiguo = (await new RepositorioTercerosAccess(version1.Bd()).BuscarAsync("antiguo")).Single();
        var ficha = (await new RepositorioTercerosAccess(version1.Bd()).ObtenerAsync(antiguo.IdTercero))!;
        Assert.Equal(0, ficha.Version);
        Assert.Null(ficha.TipoPersona); // sin clasificar: no se inventa
        ficha.Telefono = "900000000";
        Assert.True((await new RepositorioTercerosAccess(version1.Bd()).GuardarAsync(ficha, _actor)).EsCorrecto);
    }
}
