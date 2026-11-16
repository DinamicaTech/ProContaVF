using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Iva;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Liquidación del IVA contra Access (contabilidad/procesos/liquidacion-iva): lote regenerable, cuentas que se crean, trimestre
/// activo, grabación en el trimestre cerrado que liquida, motivo obligatorio y deshacer. Sin libros de IVA: el 303 da cero y el
/// importe se fija a mano, como cuando se ajusta a lo presentado. Datos sintéticos.
/// </summary>
public sealed class LiquidacionIvaAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas() => new(Bd);
    private ServicioLiquidacionIva Servicio(DateTime hoy, Func<Task<bool>>? puede = null) => new(Bd, new MotorAsientosAccess(Bd), Cuentas(), puede, new RelojFijo(hoy));

    private async Task PrepararAsync(int? trimestreActivo = 1)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        foreach (var (codigo, nombre) in new[] { ("470", "HP deudora"), ("475", "HP acreedora"), ("634", "Ajustes negativos"), ("639", "Ajustes positivos") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("4300001", "Cliente sintético"), ("7000001", "Ventas sintéticas"), ("4770021", "IVA repercutido 21 %"),
                     ("4000001", "Proveedor sintético"), ("6290001", "Otros servicios"), ("4725021", "IVA soportado 21 %"), ("5720001", "Banco sintético") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        var motor = new MotorAsientosAccess(Bd);
        var venta = new Asiento(_ctx, new DateTime(2026, 2, 15), "Venta sintética");
        venta.Apuntes.AddRange([Apunte.AlDebe("4300001", 1210m), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m)]);
        var compra = new Asiento(_ctx, new DateTime(2026, 3, 10), "Compra sintética");
        compra.Apuntes.AddRange([Apunte.AlDebe("6290001", 100m), Apunte.AlDebe("4725021", 21m), Apunte.AlHaber("4000001", 121m)]);
        Assert.True((await motor.GuardarManualesAsync([venta, compra], _actor, "Datos sintéticos")).EsCorrecto);
        // El trimestre activo, después: con el 1T cerrado no se podrían grabar los asientos de la venta y la compra.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ?, [PorcentajeProrrataAñoAnterior] = 15 WHERE IdSociedadEjercicio = 9001", trimestreActivo));
    }

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p)));

    private Task<int> AsientosDelLoteAsync() => ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ? AND ClaveEjecucion = ?", OpcionesLote.MotorLiquidacionIva, "2026-T1");

    [Fact]
    public async Task ConfirmarSaldaLasCuentasCreaLasQueFaltanYActivaElSiguiente()
    {
        await PrepararAsync();
        var servicio = Servicio(new DateTime(2026, 4, 5));
        var p = (await servicio.ProponerAsync(_ctx, 1, importeFijado: 189m)).Valor;
        Assert.Equal([("4770021", 210m, 0m), ("4725021", 0m, 21m), ("4750000", 0m, 189m)], p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Assert.Equal(0m, p.Ajuste);
        Assert.Equal(new DateTime(2026, 3, 31), p.Fecha);

        var r = await servicio.ConfirmarAsync(_ctx, 1, 189m, null, _actor, p);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Equal(2, r.Valor.TrimestreActivo);
        Assert.Single(r.Valor.CuentasCreadas, c => c.StartsWith("4750000"));
        Assert.Equal(1, await AsientosDelLoteAsync());
        Assert.Equal(2, await ContarAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9001"));
        // Las 472 y 477 quedan a cero el último día del trimestre.
        var saldos = await servicio.SaldosAsync(_ctx, new DateTime(2026, 3, 31), null);
        Assert.DoesNotContain(saldos, s => CalculoLiquidacionIva.SeLiquida(s.Cuenta));
        var t = await servicio.TrimestresAsync(_ctx);
        Assert.Equal(EstadoLiquidacionIva.Liquidado, t[0].Estado);
        Assert.Equal(189m, t[0].Guardada!.ImporteLiquidado);
        Assert.True(t[0].Cerrado);
        Assert.Equal(EstadoLiquidacionIva.EnCurso, t[1].Estado);
    }

    [Fact]
    public async Task RepetirEnElTrimestreCerradoPideMotivoYNoDuplica()
    {
        await PrepararAsync(trimestreActivo: 3);
        var servicio = Servicio(new DateTime(2026, 11, 16));
        var sinMotivo = await servicio.ConfirmarAsync(_ctx, 1, 189m, null, _actor);
        Assert.Equal("LiquidacionIva.SinMotivo", sinMotivo.Error!.Codigo);
        Assert.True((await servicio.ConfirmarAsync(_ctx, 1, 189m, "Liquidación de un trimestre ya presentado", _actor)).EsCorrecto);
        // El trimestre activo no cambia: no era el liquidado.
        Assert.Equal(3, await ContarAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9001"));
        // Repetirla con otro importe regenera el mismo lote: un solo asiento, con ajuste.
        var otra = await servicio.ConfirmarAsync(_ctx, 1, 180m, "Ajuste a lo presentado por la gestoría", _actor);
        Assert.True(otra.EsCorrecto, otra.Error?.Mensaje);
        Assert.Equal(1, await AsientosDelLoteAsync());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM LiquidacionesIva WHERE IdSociedadEjercicio = 9001"));
        Assert.Contains(otra.Valor.Propuesta.Lineas, l => l.Cuenta == "6391000" && l.Haber == 9m);
        Assert.Contains(otra.Valor.CuentasCreadas, c => c.StartsWith("6391000"));
    }

    [Fact]
    public async Task OtroLoteNoPuedeUsarLaExcepcionDelTrimestreCerrado()
    {
        await PrepararAsync(trimestreActivo: 2);
        var a = new Asiento(_ctx, new DateTime(2026, 3, 31), "Otro proceso");
        a.Apuntes.AddRange([Apunte.AlDebe("4770021", 1m), Apunte.AlHaber("5720001", 1m)]);
        var r = await new MotorAsientosAccess(Bd).ReemplazarLoteAsync(_ctx, new IdentidadLote("VENTAS", "X", "1"), [a], _actor, "Prueba",
            opciones: new OpcionesLote(TrimestreLiquidado: 1));
        Assert.Equal("Asientos.TrimestreLiquidadoNoPermitido", r.Error!.Codigo);
        // Y la liquidación solo se admite en el trimestre que liquida: el 1T no puede grabar un asiento del 2T cerrado... ni de otro.
        var fuera = new Asiento(_ctx, new DateTime(2026, 2, 28), "Liquidación mal fechada");
        fuera.Apuntes.AddRange([Apunte.AlDebe("4770021", 1m), Apunte.AlHaber("5720001", 1m)]);
        var mal = await new MotorAsientosAccess(Bd).ReemplazarLoteAsync(_ctx, ServicioLiquidacionIva.Lote(2026, 1), [fuera], _actor, "Prueba",
            opciones: new OpcionesLote(TrimestreLiquidado: 2));
        Assert.Equal("Asientos.TrimestreCerrado", mal.Error!.Codigo);
    }

    [Fact]
    public async Task DeshacerBorraElAsientoYLaFilaSinReactivarElTrimestre()
    {
        await PrepararAsync();
        var servicio = Servicio(new DateTime(2026, 4, 5));
        Assert.True((await servicio.ConfirmarAsync(_ctx, 1, 189m, null, _actor)).EsCorrecto);
        Assert.Equal("LiquidacionIva.SinMotivo", (await servicio.DeshacerAsync(_ctx, 1, " ", _actor)).Error!.Codigo);
        var r = await servicio.DeshacerAsync(_ctx, 1, "Prueba de deshacer", _actor);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Equal(0, await AsientosDelLoteAsync());
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM LiquidacionesIva"));
        Assert.Equal(2, await ContarAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9001"));
        Assert.Equal(EstadoLiquidacionIva.Pendiente, (await servicio.TrimestresAsync(_ctx))[0].Estado);
    }

    [Fact]
    public async Task UnTrimestreSinTerminarNoSeLiquidaYSinPermisoTampoco()
    {
        await PrepararAsync();
        Assert.Equal("LiquidacionIva.EnCurso", (await Servicio(new DateTime(2026, 3, 31)).ProponerAsync(_ctx, 1)).Error!.Codigo);
        var sinPermiso = await Servicio(new DateTime(2026, 4, 5), () => Task.FromResult(false)).ConfirmarAsync(_ctx, 1, 189m, null, _actor);
        Assert.Equal("LiquidacionIva.SinPermiso", sinPermiso.Error!.Codigo);
        Assert.Equal(0, await AsientosDelLoteAsync());
    }

    [Fact]
    public async Task UnaPropuestaQueHaCambiadoNoSeConfirma()
    {
        await PrepararAsync();
        var servicio = Servicio(new DateTime(2026, 4, 5));
        var vista = (await servicio.ProponerAsync(_ctx, 1, 189m)).Valor;
        var otra = new Asiento(_ctx, new DateTime(2026, 3, 20), "Otra compra");
        otra.Apuntes.AddRange([Apunte.AlDebe("6290001", 10m), Apunte.AlDebe("4725021", 2.1m), Apunte.AlHaber("4000001", 12.1m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualAsync(otra, _actor)).EsCorrecto);
        Assert.Equal("LiquidacionIva.Cambiada", (await servicio.ConfirmarAsync(_ctx, 1, 189m, null, _actor, vista)).Error!.Codigo);
    }
}
