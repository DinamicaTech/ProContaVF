using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Centimos;
using ProConta.Contabilidad.Cierre;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Céntimos residuales contra Access (contabilidad/procesos/centimos-residuales 2026-11-17 19:05): confirmar graba un asiento por
/// subcuenta a 31/12 que la deja a cero contra las cuentas de redondeo (creadas si faltan); repetir no duplica; las exclusiones se
/// guardan; el umbral del ejercicio manda; deshacer borra el lote; y el cierre enseña su estado. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class CentimosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private static readonly DateTime Enero = new(2027, 1, 10);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas() => new(Bd);
    private ServicioCentimos Servicio(DateTime? hoy = null) => new(Bd, new MotorAsientosAccess(Bd), Cuentas(), null, new RelojFijo(hoy ?? Enero));

    private Task<object?> LeerAsync(string sql, params object?[] p) => Bd.EjecutarAsync(s => s.EscalarAsync(sql, p));
    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await LeerAsync(sql, p));
    private Task<int> DelProcesoAsync() => ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ?", ServicioCentimos.Motor);

    private async Task<decimal> SaldoAsync(string cuenta) => Convert.ToDecimal(await LeerAsync(
        "SELECT SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
        "WHERE a.IdSociedadEjercicio = 9001 AND p.CuentaContable = ?", cuenta));

    /// <summary>
    /// 4300001: factura de 121,00 cobrada con 120,99 → 0,01 deudor. 4000001: factura de 50,00 pagada con 49,60 → 0,40 acreedor.
    /// 4300002: 30,00 pendiente de cobro (no entra). 5720001: banco con céntimos (57, no entra).
    /// </summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        foreach (var (codigo, nombre) in new[] { ("100", "Capital"), ("129", "Resultado"), ("400", "Proveedores"), ("430", "Clientes"), ("572", "Bancos"),
                     ("600", "Compras"), ("700", "Ventas") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital social"), ("5720001", "Banco sintético"), ("4300001", "Cliente uno"), ("4300002", "Cliente dos"),
                     ("4000001", "Proveedor uno"), ("6000000", "Compras"), ("7000000", "Ventas") })
            Assert.True((await Cuentas().AltaSubcuentaAsync(_ctx, codigo, nombre)).EsCorrecto);

        var motor = new MotorAsientosAccess(Bd);
        Asiento A(DateTime f, string d, params Apunte[] apuntes) { var a = new Asiento(_ctx, f, d); a.Apuntes.AddRange(apuntes); return a; }
        var r = await motor.GuardarManualesAsync([
            A(new DateTime(2026, 1, 2), "Aportación sintética", Apunte.AlDebe("5720001", 1_000.07m), Apunte.AlHaber("1000000", 1_000.07m)),
            A(new DateTime(2026, 3, 1), "Venta uno", Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7000000", 121m)),
            A(new DateTime(2026, 3, 9), "Cobro uno", Apunte.AlDebe("5720001", 120.99m), Apunte.AlHaber("4300001", 120.99m)),
            A(new DateTime(2026, 4, 1), "Venta dos", Apunte.AlDebe("4300002", 30m), Apunte.AlHaber("7000000", 30m)),
            A(new DateTime(2026, 5, 1), "Compra uno", Apunte.AlDebe("6000000", 50m), Apunte.AlHaber("4000001", 50m)),
            A(new DateTime(2026, 5, 9), "Pago uno", Apunte.AlDebe("4000001", 49.60m), Apunte.AlHaber("5720001", 49.60m)),
        ], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
    }

    [Fact]
    public async Task Confirmar_deja_a_cero_las_subcuentas_y_repetir_no_duplica()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var situacion = await servicio.SituacionAsync(_ctx);
        Assert.Equal(EstadoCentimos.Pendiente, situacion.Estado);
        var p = situacion.Propuesta;
        Assert.Equal(["4000001", "4300001"], p.Filas.Select(f => f.Cuenta));
        Assert.Equal((0.01m, 0.40m), (p.TotalGastos, p.TotalIngresos));

        var r = await servicio.ConfirmarAsync(_ctx, new HashSet<string>(), null, _actor, p);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Equal(2, r.Valor.Asientos);
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("6590000"));
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("7590000"));
        Assert.Equal(2, await DelProcesoAsync());
        Assert.Equal(0m, await SaldoAsync("4300001"));
        Assert.Equal(0m, await SaldoAsync("4000001"));
        Assert.Equal(0.01m, await SaldoAsync("6590000"));
        Assert.Equal(-0.40m, await SaldoAsync("7590000"));
        Assert.Equal(new DateTime(2026, 12, 31), Convert.ToDateTime(await LeerAsync("SELECT MIN(FechaContable) FROM Asientos WHERE MotorPropietario = ?", ServicioCentimos.Motor)));
        var hecho = await servicio.SituacionAsync(_ctx);
        Assert.Equal(EstadoCentimos.Hecho, hecho.Estado);
        Assert.False(hecho.FaltaAlgo);

        // Repetir: la misma propuesta (los saldos se leen sin el propio lote), pide motivo y no duplica.
        var otra = await servicio.ProponerAsync(_ctx);
        Assert.True(p.Igual(otra));
        Assert.Equal("Centimos.SinMotivo", (await servicio.ConfirmarAsync(_ctx, new HashSet<string>(), null, _actor, otra)).Error!.Codigo);
        var repetido = await servicio.ConfirmarAsync(_ctx, new HashSet<string>(), "Repetición de prueba", _actor, otra);
        Assert.True(repetido.EsCorrecto, repetido.Error?.Mensaje);
        Assert.Empty(repetido.Valor.CuentasCreadas);
        Assert.Equal(2, await DelProcesoAsync());
        Assert.Equal(0m, await SaldoAsync("4300001"));
    }

    [Fact]
    public async Task Las_exclusiones_se_guardan_el_umbral_manda_y_un_apunte_nuevo_lo_desactualiza()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = await servicio.ProponerAsync(_ctx);
        var sinProveedor = CalculoCentimos.Aplicar(p, new HashSet<string> { "4000001" });
        Assert.Equal("Centimos.Cambiada", (await servicio.ConfirmarAsync(_ctx, new HashSet<string>(), null, _actor, sinProveedor)).Error!.Codigo);
        Assert.True((await servicio.ConfirmarAsync(_ctx, new HashSet<string> { "4000001" }, null, _actor, sinProveedor)).EsCorrecto);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CentimosResidualesExclusiones WHERE IdSociedadEjercicio = 9001"));
        Assert.Equal(1, await DelProcesoAsync());
        var guardada = await servicio.ProponerAsync(_ctx);
        Assert.False(guardada.Filas.Single(f => f.Cuenta == "4000001").Incluido);
        Assert.Equal(EstadoCentimos.Hecho, (await servicio.SituacionAsync(_ctx)).Estado);

        // Con un umbral de 0,10 € el proveedor (0,40) ya no es un céntimo residual.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET UmbralCentimosResiduales = 0.10 WHERE IdSociedadEjercicio = 9001"));
        Assert.Equal(["4300001"], (await servicio.ProponerAsync(_ctx)).Filas.Select(f => f.Cuenta));

        // Otro cobro de un céntimo de más al cliente: su saldo cambia y lo grabado queda desactualizado.
        var cobro = new Asiento(_ctx, new DateTime(2026, 12, 20), "Cobro de más sintético");
        cobro.Apuntes.AddRange([Apunte.AlDebe("5720001", 0.03m), Apunte.AlHaber("4300001", 0.03m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([cobro], _actor, "Datos sintéticos")).EsCorrecto);
        var s = await servicio.SituacionAsync(_ctx);
        Assert.Equal(EstadoCentimos.Desactualizado, s.Estado);
        Assert.Equal(-0.02m, s.Propuesta.Filas.Single(f => f.Cuenta == "4300001").Saldo);
    }

    [Fact]
    public async Task Deshacer_borra_el_lote_y_antes_de_acabar_el_ano_pide_motivo()
    {
        await PrepararAsync();
        var noviembre = Servicio(new DateTime(2026, 11, 20));
        var p = await noviembre.ProponerAsync(_ctx);
        Assert.Contains(p.Avisos, a => a.Contains("aún no ha terminado"));
        Assert.Equal("Centimos.SinMotivo", (await noviembre.ConfirmarAsync(_ctx, new HashSet<string>(), null, _actor, p)).Error!.Codigo);
        Assert.True((await noviembre.ConfirmarAsync(_ctx, new HashSet<string>(), "Prueba antes de acabar el año", _actor, p)).EsCorrecto);
        Assert.Equal(2, await DelProcesoAsync());

        var servicio = Servicio();
        Assert.Equal("Centimos.SinMotivo", (await servicio.DeshacerAsync(_ctx, " ", _actor)).Error!.Codigo);
        var d = await servicio.DeshacerAsync(_ctx, "Prueba de deshacer", _actor);
        Assert.True(d.EsCorrecto, d.Error?.Mensaje);
        Assert.Equal(0, await DelProcesoAsync());
        Assert.Equal(EstadoCentimos.Pendiente, (await servicio.SituacionAsync(_ctx)).Estado);
        Assert.Equal(0.01m, await SaldoAsync("4300001"));
    }

    [Fact]
    public async Task El_cierre_ensena_su_estado()
    {
        await PrepararAsync();
        var cierre = new ServicioCierreEjercicio(Bd, new MotorAsientosAccess(Bd), Cuentas(), new ServicioNuevoEjercicio(Bd), null, new RelojFijo(Enero),
            _ => Task.FromResult<IReadOnlyList<string>>([]));
        var antes = (await cierre.ComprobarAsync(_ctx)).Single(c => c.Clave == "centimos");
        Assert.Equal((ResultadoComprobacion.Aviso, DestinoComprobacion.CentimosResiduales), (antes.Resultado, antes.Destino));
        Assert.Contains("2 subcuentas", antes.Detalle);

        var servicio = Servicio();
        Assert.True((await servicio.ConfirmarAsync(_ctx, new HashSet<string>(), null, _actor, await servicio.ProponerAsync(_ctx))).EsCorrecto);
        var despues = (await cierre.ComprobarAsync(_ctx)).Single(c => c.Clave == "centimos");
        Assert.Equal(ResultadoComprobacion.Correcta, despues.Resultado);
        Assert.Contains("2 subcuentas", despues.Detalle);
    }
}
