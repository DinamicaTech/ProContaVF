using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros.Roles;

namespace ProConta.Terceros.Tests;

/// <summary>Subcuentas automáticas de los roles (terceros/roles 2026-11-16): alta al dar el rol y borrado al quitarlo.</summary>
[Trait("Categoria", "Access")]
public sealed class SubcuentasRolesAccessTests : IDisposable
{
    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");
    private readonly ContextoSociedadEjercicio _c2019 = new("T01", 9000, 2019);
    private readonly ContextoSociedadEjercicio _c2025 = new("T01", 9001, 2025);
    private readonly ContextoSociedadEjercicio _c2026 = new("T01", 9002, 2026);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private RepositorioTercerosAccess Repo => new(Bd);
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private SubcuentasRoles Sub => new(Bd, Cuentas, new RelojFijo(new DateTime(2026, 11, 16, 10, 0, 0)));

    private async Task<int> Contar(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)));

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2019, cerrado: true);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2026);
        await Cuentas.AltaAsync(_c2026, new AltaCuenta("430", "Clientes", EsImputable: false));
    }

    private async Task<Tercero> Guardar(Tercero t)
    {
        var r = await Repo.GuardarAsync(t.Clonar(), _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        return r.Valor;
    }

    private Task Apunte(int idSociedadEjercicio, DateTime fecha, string cuenta) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync(
            "INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, TotalDebe, TotalHaber, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idSociedadEjercicio, fecha, "Asiento sintético", (byte)1, (byte)1, "prueba", DateTime.Now, 100m, 100m, 1);
        var asiento = await s.UltimoIdAsync();
        await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
            asiento, (short)1, cuenta, 100m, null, (byte)0, false);
        await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
            asiento, (short)2, "5720001", null, 100m, (byte)0, false);
        return true;
    });

    /// <summary>Lo que hace la ficha: al guardar con el rol Cliente activo y sin cuenta, crea la siguiente 430 y la asigna.</summary>
    private async Task<Tercero> ClienteConCuentaAutomaticaAsync(string razon)
    {
        var t = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = razon, CuentasRoles = new CuentasRolesFicha("T01") };
        t.Roles.Cliente = new RolCliente();
        var crear = Assert.Single(SubcuentasRoles.QueCrear(t.Roles, t.CuentasRoles));
        Assert.Equal(ClaseCuentaRol.Cliente, crear);
        var cuenta = await Sub.CrearAsync(crear, razon, _c2026);
        Assert.True(cuenta.EsCorrecto, cuenta.Error?.ToString());
        t.CuentasRoles.Cliente = cuenta.Valor.IdIdentidadContable;
        return await Guardar(t);
    }

    private static Tercero Quitar(Tercero t)
    {
        var copia = t.Clonar();
        copia.Roles.Cliente!.Activo = false;
        return copia;
    }

    [Fact]
    public void QueCrearDaLaCuentaDeCadaRolActivoSinCuenta()
    {
        var roles = new RolesFicha { Cliente = new RolCliente(), Proveedor = new RolProveedor(), Contacto = new RolContacto(), Empleado = new RolEmpleado("T01") };
        Assert.Equal([ClaseCuentaRol.Cliente, ClaseCuentaRol.Acreedor, ClaseCuentaRol.Empleado], SubcuentasRoles.QueCrear(roles, new CuentasRolesFicha("T01")));
        // Con cuenta de proveedor (400) ya no se crea la de acreedor; un rol quitado tampoco recibe cuenta; la retención nunca se crea sola.
        roles.Cliente.Activo = false;
        Assert.Equal([ClaseCuentaRol.Empleado], SubcuentasRoles.QueCrear(roles, new CuentasRolesFicha("T01") { Proveedor = 1 }));
        // El empleado de otra sociedad no recibe cuenta en esta.
        roles.Empleado = new RolEmpleado("T02");
        Assert.Empty(SubcuentasRoles.QueCrear(roles, new CuentasRolesFicha("T01") { Proveedor = 1 }));
        Assert.Equal("473", SubcuentasRoles.Raiz(ClaseCuentaRol.Retencion));
        Assert.Equal("Retención IRPF Sintética SL", SubcuentasRoles.NombreCuenta(ClaseCuentaRol.Retencion, "Sintética SL"));
    }

    [Fact]
    public async Task AlDarElRolSeCreaLaSiguienteSubcuentaYAlQuitarloSinMovimientosSeBorra()
    {
        await PrepararAsync();
        await Cuentas.AltaAsync(_c2026, new AltaCuenta("4300001", "Cliente sintético existente"));
        var t = await ClienteConCuentaAutomaticaAsync("Sintética SL");
        var cuenta = (await Cuentas.CuentaDeIdentidadAsync(_c2026, t.CuentasRoles!.Cliente!.Value))!;
        Assert.Equal("4300002", cuenta.Codigo);
        Assert.Equal("Sintética SL", cuenta.Nombre);
        Assert.NotNull(cuenta.IdCuentaPadre);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE IdTercero = ? AND Campo = 'CuentaCliente T01'", t.IdTercero));

        var guardado = await Guardar(Quitar(t));
        var q = Assert.Single(SubcuentasRoles.QueEliminar(t, guardado));
        var cambio = await Sub.EliminarAsync(guardado.IdTercero, q, _c2026, _actor);
        Assert.Equal(TipoCambioSubcuenta.Eliminada, cambio.Tipo);
        Assert.Equal("4300002", cambio.Codigo);
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM CuentasContables WHERE Codigo = '4300002'"));
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM CodigosContablesReservados WHERE Codigo = '4300002'"));
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM CuentasClientesSociedad"));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM HistorialTerceros WHERE IdTercero = ? AND Campo = 'CuentaCliente T01'", t.IdTercero));
        // El código vuelve a quedar libre para el siguiente cliente.
        Assert.Equal("4300002", await Cuentas.SiguienteSubcuentaAsync(_c2026, "430"));
        // Quitar un rol que ya estaba quitado no vuelve a intentarlo.
        Assert.Empty(SubcuentasRoles.QueEliminar(guardado, guardado));
    }

    [Fact]
    public async Task ConMovimientosEnLosUltimosCincoAnosNoSeBorra()
    {
        await PrepararAsync();
        var t = await ClienteConCuentaAutomaticaAsync("Sintética SL");
        var identidad = t.CuentasRoles!.Cliente!.Value;
        var codigo = (await Cuentas.CuentaDeIdentidadAsync(_c2026, identidad))!.Codigo;
        await Apunte(9002, new DateTime(2026, 3, 1), codigo);

        var guardado = await Guardar(Quitar(t));
        var cambio = await Sub.EliminarAsync(guardado.IdTercero, Assert.Single(SubcuentasRoles.QueEliminar(t, guardado)), _c2026, _actor);
        Assert.Equal(TipoCambioSubcuenta.Conservada, cambio.Tipo);
        Assert.Contains("16/11/2021", cambio.Texto);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM CuentasContables WHERE IdIdentidadContable = ?", identidad));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM CuentasClientesSociedad"));
    }

    [Fact]
    public async Task ConApuntesDeHaceMasDeCincoAnosSoloSeBorraDondeEstaVacia()
    {
        await PrepararAsync();
        var vieja = (await Cuentas.AltaAsync(_c2019, new AltaCuenta("4300007", "Cliente antiguo sintético"))).Valor;
        await Cuentas.AltaEnEjercicioAsync(_c2025, vieja.IdIdentidadContable);
        await Cuentas.AltaEnEjercicioAsync(_c2026, vieja.IdIdentidadContable);
        await Apunte(9000, new DateTime(2019, 5, 1), "4300007");
        var t = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Antigua SL", CuentasRoles = new CuentasRolesFicha("T01") { Cliente = vieja.IdIdentidadContable } };
        t.Roles.Cliente = new RolCliente();
        t = await Guardar(t);

        var guardado = await Guardar(Quitar(t));
        var cambio = await Sub.EliminarAsync(guardado.IdTercero, Assert.Single(SubcuentasRoles.QueEliminar(t, guardado)), _c2026, _actor);
        Assert.Equal(TipoCambioSubcuenta.EliminadaEnRecientes, cambio.Tipo);
        Assert.Contains("2025, 2026", cambio.Texto);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM CuentasContables WHERE IdIdentidadContable = ?", vieja.IdIdentidadContable));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM CuentasContables WHERE IdIdentidadContable = ? AND IdSociedadEjercicio = 9000", vieja.IdIdentidadContable));
        // La identidad y la asignación siguen: si se vuelve a dar el rol, la ficha ofrece crearla con el mismo código.
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM CuentasClientesSociedad"));
    }

    [Fact]
    public async Task UnaSubcuentaQueUsaOtroTerceroNoSeBorra()
    {
        await PrepararAsync();
        var t = await ClienteConCuentaAutomaticaAsync("Sintética SL");
        var otro = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Clientes varios SL", CuentasRoles = new CuentasRolesFicha("T01") { Cliente = t.CuentasRoles!.Cliente } };
        otro.Roles.Cliente = new RolCliente();
        await Guardar(otro);

        var guardado = await Guardar(Quitar(t));
        var cambio = await Sub.EliminarAsync(guardado.IdTercero, Assert.Single(SubcuentasRoles.QueEliminar(t, guardado)), _c2026, _actor);
        Assert.Equal(TipoCambioSubcuenta.Conservada, cambio.Tipo);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM CuentasContables WHERE IdIdentidadContable = ?", t.CuentasRoles.Cliente!.Value));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM CuentasClientesSociedad"));
    }

    [Fact]
    public async Task ElProveedorRecibeUna410YAutoCreaTambienLa400ConSuNombre()
    {
        await PrepararAsync();
        var acreedor = await Sub.CrearAsync(ClaseCuentaRol.Acreedor, "Suministros Sintéticos SL", _c2026);
        Assert.StartsWith("410", acreedor.Valor.Codigo);
        var proveedor = await Sub.CrearAsync(ClaseCuentaRol.Proveedor, "Suministros Sintéticos SL", _c2026);
        Assert.StartsWith("400", proveedor.Valor.Codigo);
        var retencion = await Sub.CrearAsync(ClaseCuentaRol.Retencion, "Cliente Sintético SL", _c2026);
        Assert.StartsWith("473", retencion.Valor.Codigo);
        Assert.Equal("Retención IRPF Cliente Sintético SL", retencion.Valor.Nombre);
        Assert.Equal("Roles.SinNombreParaCuenta", (await Sub.CrearAsync(ClaseCuentaRol.Cliente, " ", _c2026)).Error!.Codigo);
    }
}
