using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cierre;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cierre del ejercicio contra Access (contabilidad/procesos/cierre-ejercicio y nuevo-ejercicio): crear el ejercicio siguiente copia
/// parámetros, cuentas y series sin duplicar; cerrar graba la regularización y el cierre en el ejercicio y la apertura en el siguiente,
/// marca el ejercicio como cerrado y no se repite; reabrir borra solo esos asientos; los asientos de cierre hechos fuera del proceso y un
/// ejercicio siguiente cerrado lo impiden. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class CierreEjercicioAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly ContextoSociedadEjercicio _siguiente = new("T01", 9002, 2027);
    private readonly Actor _actor = new("operador.prueba");
    private static readonly DateTime Enero = new(2027, 1, 10);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas() => new(Bd);
    private ServicioNuevoEjercicio Nuevo() => new(Bd);
    private ServicioCierreEjercicio Servicio(DateTime? hoy = null) =>
        new(Bd, new MotorAsientosAccess(Bd), Cuentas(), Nuevo(), null, new RelojFijo(hoy ?? Enero), _ => Task.FromResult<IReadOnlyList<string>>([]));

    private async Task PrepararAsync(bool conSiguiente = true)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        if (conSiguiente) await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2027);
        foreach (var (codigo, nombre) in new[] { ("100", "Capital"), ("129", "Resultado"), ("430", "Clientes"), ("572", "Bancos"), ("621", "Arrendamientos"), ("705", "Prestaciones") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital social"), ("4300001", "Cliente sintético"), ("5720001", "Banco sintético"),
                     ("6210000", "Arrendamientos"), ("7050000", "Prestaciones de servicios") })
            Assert.True((await Cuentas().AltaSubcuentaAsync(_ctx, codigo, nombre)).EsCorrecto);

        var motor = new MotorAsientosAccess(Bd);
        var capital = new Asiento(_ctx, new DateTime(2026, 1, 2), "Aportación sintética");
        capital.Apuntes.AddRange([Apunte.AlDebe("5720001", 3_000m), Apunte.AlHaber("1000000", 3_000m)]);
        var venta = new Asiento(_ctx, new DateTime(2026, 5, 10), "Venta sintética");
        venta.Apuntes.AddRange([Apunte.AlDebe("4300001", 10_000m), Apunte.AlHaber("7050000", 10_000m)]);
        var gasto = new Asiento(_ctx, new DateTime(2026, 6, 1), "Alquiler sintético");
        gasto.Apuntes.AddRange([Apunte.AlDebe("6210000", 6_000m), Apunte.AlHaber("5720001", 6_000m)]);
        var r = await motor.GuardarManualesAsync([capital, venta, gasto], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
    }

    private Task<object?> LeerAsync(string sql, params object?[] p) => Bd.EjecutarAsync(s => s.EscalarAsync(sql, p));
    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await LeerAsync(sql, p));
    private async Task<bool> CerradoAsync(int id) => Convert.ToBoolean(await LeerAsync("SELECT EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", id));
    private Task<int> DelProcesoAsync() => ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ?", ServicioCierreEjercicio.Motor);

    [Fact]
    public async Task CerrarGrabaLosTresAsientosYLaMarcaYReabrirLosBorra()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.True(p.SePuedeCerrar, string.Join(" ", p.Bloqueos));
        Assert.Equal(4_000m, p.Resultado);

        // Hay avisos en la comprobación previa (sin impuesto ni liquidación): pide motivo.
        Assert.Equal("Cierre.SinMotivo", (await servicio.ConfirmarAsync(_ctx, null, _actor, p)).Error!.Codigo);
        var r = await servicio.ConfirmarAsync(_ctx, "Prueba de cierre", _actor, p);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.NotNull(r.Valor.IdRegularizacion);
        Assert.NotNull(r.Valor.IdCierre);
        Assert.NotNull(r.Valor.IdApertura);
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("1290000"));
        Assert.True(await CerradoAsync(9001));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = 9001 AND TipoAsiento = 2"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = 9001 AND TipoAsiento = 3"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = 9002 AND TipoAsiento = 1"));
        // La apertura lleva el beneficio a la 1290000 de 2027, que se ha copiado con las demás cuentas.
        Assert.Equal(4_000m, Convert.ToDecimal(await LeerAsync(
            "SELECT SUM(p.ImporteHaber) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = 9002 AND p.CuentaContable = '1290000'")));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = 9002 AND Codigo = '1290000'"));

        var situacion = await servicio.SituacionAsync(_ctx);
        Assert.True(situacion.Cerrado);
        Assert.Equal(r.Valor.IdApertura, situacion.IdApertura);
        // Cerrado, no se vuelve a cerrar.
        Assert.Equal("Cierre.NoSePuede", (await servicio.ConfirmarAsync(_ctx, "Otra vez", _actor)).Error!.Codigo);

        Assert.Equal("Cierre.SinMotivo", (await servicio.ReabrirAsync(_ctx, " ", _actor)).Error!.Codigo);
        var d = await servicio.ReabrirAsync(_ctx, "Prueba de reapertura", _actor);
        Assert.True(d.EsCorrecto, d.Error?.Mensaje);
        Assert.Equal(3, d.Valor);
        Assert.False(await CerradoAsync(9001));
        Assert.Equal(0, await DelProcesoAsync());
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = 9001"));

        // Volver a cerrar da los mismos asientos.
        var otra = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.True(p.Igual(otra));
        Assert.True((await servicio.ConfirmarAsync(_ctx, "Segundo cierre", _actor, otra)).EsCorrecto);
        Assert.Equal(3, await DelProcesoAsync());
    }

    [Fact]
    public async Task SinElEjercicioSiguienteNoSeCierraYCrearloCopiaSinDuplicar()
    {
        await PrepararAsync(conSiguiente: false);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 4, [PorcentajeProrrataAñoAnterior] = 15, PorcentajeProrrataDefinitiva = 20 WHERE IdSociedadEjercicio = 9001"));
        var servicio = Servicio();
        Assert.Contains((await servicio.ComprobarAsync(_ctx)), c => c.Clave == "ejercicio-siguiente" && c.Resultado == ResultadoComprobacion.Bloqueo);
        Assert.False((await servicio.ProponerAsync(_ctx)).Valor.SePuedeCerrar);

        var prevision = await Nuevo().PrevisionAsync(_ctx);
        Assert.False(prevision.Existe);
        Assert.Equal(2027, prevision.Ejercicio);
        var r = await Nuevo().CrearSiguienteAsync(_ctx);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.True(r.Valor.Creado);
        Assert.Equal(2027, r.Valor.Ejercicio.Ejercicio);
        var id = r.Valor.Ejercicio.IdSociedadEjercicio;
        var cuentas2026 = await ContarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = 9001");
        Assert.Equal(cuentas2026, await ContarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = ?", id));
        Assert.Equal(1, Convert.ToInt32(await LeerAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", id)));
        Assert.False(await CerradoAsync(id));
        Assert.Equal(20m, Convert.ToDecimal(await LeerAsync("SELECT [PorcentajeProrrataAñoAnterior] FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", id)));
        Assert.True(await LeerAsync("SELECT PorcentajeProrrataDefinitiva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", id) is null or DBNull);
        // Las cuentas comparten identidad contable con las de 2026.
        Assert.Equal(0, await ContarAsync(
            "SELECT COUNT(*) FROM CuentasContables AS n LEFT JOIN CuentasContables AS v ON (n.IdIdentidadContable = v.IdIdentidadContable AND v.IdSociedadEjercicio = 9001) " +
            "WHERE n.IdSociedadEjercicio = ? AND v.IdCuentaContable IS NULL", id));

        var otra = await Nuevo().CrearSiguienteAsync(_ctx);
        Assert.True(otra.EsCorrecto, otra.Error?.Mensaje);
        Assert.False(otra.Valor.Creado);
        Assert.Equal(0, otra.Valor.CuentasCopiadas);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM SociedadesEjercicios WHERE IdSociedad = 'T01' AND Ejercicio = '2027'"));
        Assert.True((await servicio.ProponerAsync(_ctx)).Valor.SePuedeCerrar);
    }

    [Fact]
    public async Task ConElSiguienteCerradoNoSeReabre()
    {
        await PrepararAsync();
        var servicio = Servicio();
        Assert.True((await servicio.ConfirmarAsync(_ctx, "Prueba de cierre", _actor)).EsCorrecto);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9002"));

        Assert.Equal("Cierre.SiguienteCerrado", (await servicio.ReabrirAsync(_ctx, "Prueba", _actor)).Error!.Codigo);
        Assert.True(await CerradoAsync(9001));
        Assert.Equal(3, await DelProcesoAsync());
    }

    [Fact]
    public async Task LosAsientosDeCierreHechosFueraDelProcesoImpidenCerrarYReabrirNoLosToca()
    {
        await PrepararAsync();
        var fuera = new Asiento(_ctx, new DateTime(2026, 12, 31), "Regularización importada") { Tipo = TipoAsiento.Regularizacion };
        fuera.Apuntes.AddRange([Apunte.AlDebe("7050000", 1m), Apunte.AlHaber("6210000", 1m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([fuera], _actor, "Datos sintéticos")).EsCorrecto);

        var servicio = Servicio();
        Assert.NotEmpty((await servicio.SituacionAsync(_ctx)).Ajenos);
        var p = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.False(p.SePuedeCerrar);
        Assert.Equal("Cierre.NoSePuede", (await servicio.ConfirmarAsync(_ctx, "Prueba", _actor)).Error!.Codigo);
        Assert.False(await CerradoAsync(9001));
    }

    [Fact]
    public async Task ElMotorNoDejaAOtrosProcesosGrabarComoCierre()
    {
        await PrepararAsync();
        var a = new Asiento(_ctx, new DateTime(2026, 12, 31), "Otro proceso");
        a.Apuntes.AddRange([Apunte.AlDebe("5720001", 1m), Apunte.AlHaber("4300001", 1m)]);
        var r = await new MotorAsientosAccess(Bd).ReemplazarLotesAsync([new LoteAsientos(_ctx, new IdentidadLote("VENTAS", "X", "1"), [a])], _actor, "Prueba",
            opciones: new OpcionesLote(CierreEjercicio: true));
        Assert.False(r.EsCorrecto);
        Assert.Equal("Asientos.CierreNoPermitido", r.Error!.Codigo);
    }
}
