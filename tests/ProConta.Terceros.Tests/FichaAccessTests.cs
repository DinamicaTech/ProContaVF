using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Esquema;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;

namespace ProConta.Terceros.Tests;

[Trait("Categoria", "Access")]
public sealed class FichaAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private RepositorioTercerosAccess Repo => new(Bd);

    private static Tercero Juridica(string razon) => new() { TipoPersona = TipoPersona.Juridica, RazonSocial = razon };

    private async Task<int> Contar(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)));

    private async Task<Tercero> Guardar(Tercero t)
    {
        var r = await Repo.GuardarAsync(t, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        return r.Valor;
    }

    [Fact]
    public async Task PerfilesIdempotentesConHistorial()
    {
        var catalogo = new CatalogoPerfilesAccess(Bd);
        var homologado = (await catalogo.AltaAsync("Proveedor homologado sintético", "Prueba")).Valor;
        Assert.Equal("Perfiles.Repetido", (await catalogo.AltaAsync("Proveedor homologado sintético", null)).Error!.Codigo);

        var t = Juridica("Sintética SL");
        t.AsignarPerfil(homologado);
        t = await Guardar(t);
        var asignacion = Assert.Single(t.Perfiles);
        Assert.True(asignacion.Activo);
        var historial = await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE IdTercero = ? AND Entidad = 'Perfil'", t.IdTercero);
        Assert.Equal(1, historial);

        // Repetir el estado actual no escribe nada.
        t.AsignarPerfil(homologado);
        t = await Guardar(t);
        Assert.Equal(historial, await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE IdTercero = ? AND Entidad = 'Perfil'", t.IdTercero));

        // Retirar conserva la fila con su fecha; reasignar reactiva la misma fila.
        t.RetirarPerfil(homologado.IdPerfil);
        t = await Guardar(t);
        Assert.False(t.Perfiles.Single().Activo);
        Assert.NotNull(t.Perfiles.Single().FechaRetirada);
        t.AsignarPerfil(homologado);
        t = await Guardar(t);
        Assert.True(t.Perfiles.Single().Activo);
        Assert.Null(t.Perfiles.Single().FechaRetirada);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM PerfilesTerceros WHERE IdTercero = ?", t.IdTercero));
        Assert.Equal(3, await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE IdTercero = ? AND Entidad = 'Perfil'", t.IdTercero));

        // Un perfil retirado del catálogo conserva la asignación, pero no admite asignaciones nuevas.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Perfiles SET Activo = False WHERE IdPerfil = ?", homologado.IdPerfil));
        t.NotasInternas = "sigue asignado";
        t = await Guardar(t);
        Assert.True(t.Perfiles.Single().Activo);
        var otro = Juridica("Otra SL");
        otro.AsignarPerfil(homologado with { Activo = false });
        var fallo = await Repo.GuardarAsync(otro, _actor);
        Assert.Equal("Perfiles.Inactivo", fallo.Error!.Codigo);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Terceros"));
    }

    [Fact]
    public async Task RelacionDaElRolContactoDeFormaIdempotente()
    {
        var persona = await Guardar(new Tercero { TipoPersona = TipoPersona.Fisica, NombrePersona = "Lucía", PrimerApellido = "Ferrer" });
        Assert.Null(persona.Roles.Contacto);
        var org = Juridica("Talleres Sintéticos SL");
        org.Relaciones.Add(new RelacionContacto { IdTerceroPersona = persona.IdTercero, NombrePersona = persona.Nombre, NombreOrganizacion = "Talleres Sintéticos SL", FechaInicio = new DateTime(2021, 3, 1) });
        org = await Guardar(org);
        var relacion = Assert.Single(org.Relaciones);
        Assert.False(relacion.EsNueva);
        Assert.Equal(org.IdTercero, relacion.IdTerceroOrganizacion);

        var otra = Juridica("Segunda Sintética SL");
        otra.Relaciones.Add(new RelacionContacto { IdTerceroPersona = persona.IdTercero, NombrePersona = persona.Nombre, NombreOrganizacion = "Segunda", TipoRelacion = "Administrador" });
        await Guardar(otra);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Contactos WHERE IdTercero = ?", persona.IdTercero));
        var deLaPersona = (await Repo.ObtenerAsync(persona.IdTercero))!;
        Assert.True(deLaPersona.Roles.EsContacto);
        Assert.Equal(2, deLaPersona.Relaciones.Count);

        // Cerrar la vigencia actualiza la relación y deja historial en los dos terceros.
        org.Relaciones[0].FechaFin = new DateTime(2026, 10, 31);
        org = await Guardar(org);
        Assert.False(org.Relaciones[0].EsVigente(new DateTime(2026, 11, 1)));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE Entidad = 'Relacion' AND Campo = 'FechaFin'"));
    }

    [Fact]
    public async Task RolesSeActivanYDesactivanConLaFichaSinDuplicarIdentidad()
    {
        var t = Juridica("Sintética SL");
        t.Roles.Cliente = new RolCliente { Notas = "Paga a 30 días" };
        t.Roles.Proveedor = new RolProveedor();
        t = await Guardar(t);
        var idCliente = t.Roles.Cliente!.IdCliente;
        Assert.NotNull(idCliente);
        Assert.True(t.Roles.EsProveedor);

        t.Roles.Cliente.Activo = false;
        t.Roles.Contacto = new RolContacto { Cargo = "Gerente", Departamento = "Dirección" };
        t = await Guardar(t);
        Assert.False(t.Roles.EsCliente);
        Assert.Equal("Gerente", t.Roles.Contacto!.Cargo);
        t.Roles.Cliente!.Activo = true;
        t = await Guardar(t);
        Assert.Equal(idCliente, t.Roles.Cliente!.IdCliente);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Clientes WHERE IdTercero = ?", t.IdTercero));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Terceros"));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE Entidad = 'Cliente' AND Campo = 'Activo'"));
    }

    [Fact]
    public async Task UnErrorNoDejaNadaAMedias()
    {
        var catalogo = new CatalogoPerfilesAccess(Bd);
        var retirado = (await catalogo.AltaAsync("Perfil retirado sintético", null)).Valor;
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Perfiles SET Activo = False WHERE IdPerfil = ?", retirado.IdPerfil));
        var persona = await Guardar(new Tercero { TipoPersona = TipoPersona.Fisica, NombrePersona = "Ana" });
        var historialPrevio = await Contar("SELECT COUNT(*) FROM HistorialTerceros");

        var t = Juridica("Sintética SL");
        t.Roles.Cliente = new RolCliente();
        t.Roles.Proveedor = new RolProveedor();
        t.Relaciones.Add(new RelacionContacto { IdTerceroPersona = persona.IdTercero, NombrePersona = "Ana", NombreOrganizacion = "Sintética SL" });
        t.AsignarPerfil(retirado with { Activo = false });
        Assert.Equal("Perfiles.Inactivo", (await Repo.GuardarAsync(t, _actor)).Error!.Codigo);
        foreach (var tabla in new[] { "Clientes", "Proveedores", "Contactos", "RelacionesContactos", "PerfilesTerceros" })
            Assert.Equal(0, await Contar($"SELECT COUNT(*) FROM {tabla}"));
        Assert.Equal(historialPrevio, await Contar("SELECT COUNT(*) FROM HistorialTerceros"));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Terceros"));
    }

    private async Task<(ContextoSociedadEjercicio C2025, ContextoSociedadEjercicio C2026, ContextoSociedadEjercicio Otra)> SociedadesAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.SociedadAsync(Bd, "T02", "Otra sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(Bd, 9003, "T02", 2026);
        return (new("T01", 9001, 2025), new("T01", 9002, 2026), new("T02", 9003, 2026));
    }

    [Fact]
    public async Task LaCuentaDelRolEsDeLaSociedadYValeEnTodosSusEjercicios()
    {
        var (c2025, c2026, otra) = await SociedadesAsync();
        var cuentas = new CatalogoCuentasAccess(Bd);
        var roles = new ServicioRoles(Bd, cuentas);
        var cuentaCliente = (await cuentas.AltaAsync(c2025, new AltaCuenta("4300012", "Cliente sintético"))).Valor;
        var cuentaProveedor = (await cuentas.AltaAsync(c2025, new AltaCuenta("4000031", "Proveedor sintético"))).Valor;
        var cuentaAcreedor = (await cuentas.AltaAsync(c2025, new AltaCuenta("4100031", "Acreedor sintético"))).Valor;

        var t = Juridica("Sintética SL");
        t.Roles.Cliente = new RolCliente();
        t.Roles.Proveedor = new RolProveedor();
        t.CuentasRoles = new CuentasRolesFicha("T01") { Cliente = cuentaCliente.IdIdentidadContable, Proveedor = cuentaProveedor.IdIdentidadContable, Acreedor = cuentaAcreedor.IdIdentidadContable };
        t = await Guardar(t);
        Assert.Equal(cuentaAcreedor.IdIdentidadContable, t.CuentasRoles!.Acreedor);

        // En 2025 existen; en 2026 la identidad aún no tiene cuenta: se dice cuál es y se crea con el mismo código.
        var en2025 = await roles.CuentasAsync(t.IdTercero, c2025);
        Assert.Equal("4300012", en2025.Cliente!.EnEjercicio!.Codigo);
        Assert.Equal("4100031", en2025.Acreedor!.Codigo);
        var en2026 = await roles.CuentasAsync(t.IdTercero, c2026);
        Assert.False(en2026.Cliente!.ExisteEnEjercicio);
        Assert.Equal("4300012", en2026.Cliente.Codigo);
        var creada = await cuentas.AltaEnEjercicioAsync(c2026, en2026.Cliente.IdIdentidadContable);
        Assert.True(creada.EsCorrecto, creada.Error?.ToString());
        Assert.Equal("4300012", (await roles.CuentasAsync(t.IdTercero, c2026)).Cliente!.EnEjercicio!.Codigo);

        // Cambiar el código en un ejercicio no rompe el vínculo: la identidad sigue siendo la misma.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CuentasContables SET Codigo = '4300099' WHERE IdCuentaContable = ?", creada.Valor.IdCuentaContable));
        Assert.Equal("4300099", (await roles.CuentasAsync(t.IdTercero, c2026)).Cliente!.EnEjercicio!.Codigo);
        Assert.Equal("4300012", (await roles.CuentasAsync(t.IdTercero, c2025)).Cliente!.EnEjercicio!.Codigo);

        // Otra sociedad no la ve; y no admite una identidad de otra sociedad.
        var enOtra = await roles.CuentasAsync(t.IdTercero, otra);
        Assert.Null(enOtra.Cliente);
        t.CuentasRoles = new CuentasRolesFicha("T02") { Cliente = cuentaCliente.IdIdentidadContable };
        Assert.Equal("Roles.CuentaDeOtraSociedad", (await Repo.GuardarAsync(t, _actor)).Error!.Codigo);

        // Un código que no existe en el ejercicio se explica; reservado para otra identidad, no se puede reutilizar.
        Assert.Equal("Cuentas.NoExiste", (await roles.BuscarPorCodigoAsync(ClaseCuentaRol.Cliente, "4309999", c2026)).Error!.Codigo);
        Assert.Equal("Cuentas.CodigoReservado", (await cuentas.AltaEnEjercicioAsync(c2026, cuentaProveedor.IdIdentidadContable, "4100031")).Error!.Codigo);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE Campo = 'CuentaAcreedor T01'"));
    }

    [Fact]
    public async Task LaMigracionTrasladaLasCuentasPorEjercicioYEsIdempotente()
    {
        using var v1 = BdPruebas.NuevaVersion1();
        var bd = v1.Bd();
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Sociedades (IdSociedad, Nombre, CIF) VALUES (?, ?, ?)", "T01", "Sociedad sintética", "B12345674");
            await s.EjecutarAsync("INSERT INTO SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio, Nombre) VALUES (?, ?, ?, ?)", 9001, "T01", "2025", "T01 2025");
            await s.EjecutarAsync("INSERT INTO SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio, Nombre) VALUES (?, ?, ?, ?)", 9002, "T01", "2026", "T01 2026");
            await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo) VALUES (?, ?)", "Cliente sintético antiguo", true);
            var idTercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", 777, idTercero, true);
            await s.EjecutarAsync("INSERT INTO Proveedores (IdTercero, Activo) VALUES (?, ?)", idTercero, true);
            var idProveedor = await s.UltimoIdAsync();
            var identidades = new List<int>();
            for (var i = 0; i < 3; i++)
            {
                await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, Naturaleza, FechaAlta) VALUES (?, ?, ?)", "T01", (byte)0, DateTime.Today);
                identidades.Add(await s.UltimoIdAsync());
            }
            async Task<int> AltaCuenta(int identidad, int ejercicio, string codigo)
            {
                await s.EjecutarAsync("INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, SaldoInicial, SaldoActual, NumeroApuntes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    identidad, ejercicio, codigo, "Cuenta " + codigo, true, false, true, 0m, 0m, 0);
                return await s.UltimoIdAsync();
            }
            var a2025 = await AltaCuenta(identidades[0], 9001, "4300001");
            var b2026 = await AltaCuenta(identidades[1], 9002, "4300002");
            var c2026 = await AltaCuenta(identidades[2], 9002, "4300003");
            var p2025 = await AltaCuenta(identidades[0], 9001, "4000001");
            await s.EjecutarAsync("INSERT INTO CuentasClientesEjercicio (IdCliente, IdSociedadEjercicio, IdCuentaContable, EsPredeterminada) VALUES (?, ?, ?, ?)", 777, 9001, a2025, true);
            await s.EjecutarAsync("INSERT INTO CuentasClientesEjercicio (IdCliente, IdSociedadEjercicio, IdCuentaContable, EsPredeterminada) VALUES (?, ?, ?, ?)", 777, 9002, b2026, false);
            await s.EjecutarAsync("INSERT INTO CuentasClientesEjercicio (IdCliente, IdSociedadEjercicio, IdCuentaContable, EsPredeterminada) VALUES (?, ?, ?, ?)", 777, 9002, c2026, true);
            await s.EjecutarAsync("INSERT INTO CuentasProveedoresEjercicio (IdProveedor, IdSociedadEjercicio, IdCuentaContable, EsPredeterminada) VALUES (?, ?, ?, ?)", idProveedor, 9001, p2025, true);
            return identidades;
        });

        var carpeta = BdPruebas.CarpetaTemporal();
        var parametros = new Dictionary<string, string> { [MigracionesOrganizacion.ParametroOrigenFormasPago] = BdPruebas.CrearOrigenFormasPago(carpeta) };
        var r = await new ActualizadorEsquema(_actor, parametros: parametros).ActualizarAsync(v1.Ruta, Path.Combine(carpeta, "copias"));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        var esquema = await bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        Assert.False(esquema.ExisteTabla(MigracionesTerceros.AntiguaClientes));
        Assert.False(esquema.ExisteTabla(MigracionesTerceros.AntiguaProveedores));
        var cliente = await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT x.IdSociedad, c.Codigo FROM CuentasClientesSociedad AS x INNER JOIN CuentasContables AS c ON x.IdIdentidadContable = c.IdIdentidadContable WHERE x.IdCliente = 777",
            f => $"{f.Texto("IdSociedad")}:{f.Texto("Codigo")}"));
        Assert.Equal(["T01:4300003"], cliente); // la predeterminada del ejercicio más reciente
        Assert.Equal(1, Convert.ToInt32(await bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM CuentasProveedoresSociedad WHERE Clase = 'PROVEEDOR'"))));

        var otraVez = await new ActualizadorEsquema(_actor, parametros: parametros).ActualizarAsync(v1.Ruta, Path.Combine(carpeta, "copias2"));
        Assert.True(otraVez.EsCorrecto);
        Assert.Empty(otraVez.Valor.Aplicadas);
    }

    [Fact]
    public async Task LaMigracionNoCambiaNadaSiHayFilasQueNoSePuedenTrasladar()
    {
        using var v1 = BdPruebas.NuevaVersion1();
        var bd = v1.Bd();
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Sociedades (IdSociedad, Nombre, CIF) VALUES (?, ?, ?)", "T01", "Sociedad sintética", "B12345674");
            await s.EjecutarAsync("INSERT INTO SociedadesEjercicios (IdSociedadEjercicio, IdSociedad, Ejercicio, Nombre) VALUES (?, ?, ?, ?)", 9001, null, "2025", "Ejercicio sin sociedad");
            await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo) VALUES (?, ?)", "Cliente sintético antiguo", true);
            var idTercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", 777, idTercero, true);
            await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, Naturaleza, FechaAlta) VALUES (?, ?, ?)", "T01", (byte)0, DateTime.Today);
            var identidad = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, SaldoInicial, SaldoActual, NumeroApuntes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                identidad, 9001, "4300001", "Cuenta", true, false, true, 0m, 0m, 0);
            var cuenta = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CuentasClientesEjercicio (IdCliente, IdSociedadEjercicio, IdCuentaContable, EsPredeterminada) VALUES (?, ?, ?, ?)", 777, 9001, cuenta, true);
            return true;
        });
        var carpeta = BdPruebas.CarpetaTemporal();
        var parametros = new Dictionary<string, string> { [MigracionesOrganizacion.ParametroOrigenFormasPago] = BdPruebas.CrearOrigenFormasPago(carpeta) };
        var r = await new ActualizadorEsquema(_actor, parametros: parametros).ActualizarAsync(v1.Ruta, Path.Combine(carpeta, "copias"));
        Assert.Equal("Esquema.CuentasRolesSinTrasladar", r.Error!.Codigo);
        var esquema = await bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        Assert.True(esquema.ExisteTabla(MigracionesTerceros.AntiguaClientes));
        Assert.False(esquema.ExisteTabla("CuentasClientesSociedad"));
        Assert.False(Directory.Exists(Path.Combine(carpeta, "copias")) && Directory.EnumerateFiles(Path.Combine(carpeta, "copias")).Any());
    }

    [Fact]
    public async Task LaMigracionPasaElEmailYElTelefonoAntiguosALaListaSinDuplicar()
    {
        using var v1 = BdPruebas.NuevaVersion1();
        var bd = v1.Bd();
        var (conTodo, yaEstaba, raro) = await bd.EjecutarAsync(async s =>
        {
            async Task<int> Alta(string nombre, string? email, string? telefono)
            {
                await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Email, Telefono) VALUES (?, ?, ?, ?)", nombre, true, email, telefono);
                return await s.UltimoIdAsync();
            }
            var a = await Alta("Sintética A", "Ana@Ejemplo.test; otra@ejemplo.test", "961 23 45 67");
            var b = await Alta("Sintética B", "b@ejemplo.test", "961-234-567");
            await s.EjecutarAsync("INSERT INTO DatosContacto (IdTercero, Tipo, Valor, Activo, EnvioFacturas, EnviosAdministrativos) VALUES (?, ?, ?, ?, ?, ?)", b, "EMAIL", " B@EJEMPLO.test ", true, true, false);
            await s.EjecutarAsync("INSERT INTO DatosContacto (IdTercero, Tipo, Valor, Activo, EnvioFacturas, EnviosAdministrativos) VALUES (?, ?, ?, ?, ?, ?)", b, "TELEFONO", "961234567", true, false, false);
            var c = await Alta("Sintética C", "sin arroba", "Calle Mayor 3");
            return (a, b, c);
        });

        var carpeta = BdPruebas.CarpetaTemporal();
        var parametros = new Dictionary<string, string> { [MigracionesOrganizacion.ParametroOrigenFormasPago] = BdPruebas.CrearOrigenFormasPago(carpeta) };
        var r = await new ActualizadorEsquema(_actor, parametros: parametros).ActualizarAsync(v1.Ruta, Path.Combine(carpeta, "copias"));
        Assert.True(r.EsCorrecto, r.Error?.ToString());

        async Task<string[]> Datos(int id) => (await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT Tipo, Valor, Etiqueta, EnvioFacturas FROM DatosContacto WHERE IdTercero = ? ORDER BY IdDatoContacto", f => $"{f.Texto("Tipo")}|{f.Texto("Valor")}|{f.Texto("Etiqueta")}|{f.Logico("EnvioFacturas")}", id))).ToArray();
        Assert.Equal(["EMAIL|Ana@Ejemplo.test|Principal|False", "EMAIL|otra@ejemplo.test|Principal|False", "TELEFONO|961 23 45 67|Principal|False"], await Datos(conTodo));
        Assert.Equal(2, (await Datos(yaEstaba)).Length); // ya estaban: nada nuevo
        Assert.Empty(await Datos(raro)); // sin «@» ni 6 cifras: se queda en la ficha antigua
        Assert.Equal(1, Convert.ToInt32(await bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM HistorialTerceros WHERE IdTercero = ? AND Operador LIKE 'proceso:migracion%'", conTodo))));
        Assert.Equal("Ana@Ejemplo.test; otra@ejemplo.test", await bd.EjecutarAsync(s => s.EscalarAsync("SELECT Email FROM Terceros WHERE IdTercero = ?", conTodo)));

        Assert.Empty(await bd.EjecutarAsync(MigracionesTerceros.PlanContactoFichaAntiguaAsync)); // repetirla no añade nada
    }
}
