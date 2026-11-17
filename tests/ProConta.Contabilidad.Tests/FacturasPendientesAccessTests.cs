using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cierre;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.FacturasPendientes;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Facturas pendientes de recibir contra Access (contabilidad/procesos/facturas-pendientes-recibir 2026-11-17 16:40): entran los
/// proveedores cuadrados con saldo deudor; confirmar graba la provisión del 31/12 y la reversión del 1/1 del siguiente como dos lotes;
/// repetir no duplica ni realimenta al proveedor; lo excluido y las bases a mano se guardan; deshacer borra los dos lotes; sin el
/// ejercicio siguiente no se graba; y el cierre enseña su estado y al reabrirlo no los toca. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class FacturasPendientesAccessTests : IDisposable
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
    private ServicioFacturasPendientes Servicio(DateTime? hoy = null) => new(Bd, new MotorAsientosAccess(Bd), Cuentas(), null, new RelojFijo(hoy ?? Enero));

    private Task<object?> LeerAsync(string sql, params object?[] p) => Bd.EjecutarAsync(s => s.EscalarAsync(sql, p));
    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await LeerAsync(sql, p));
    private Task<int> DelProcesoAsync() => ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ?", ServicioFacturasPendientes.Motor);

    /// <summary>
    /// 4000001 (proveedor con gasto 6230000 por defecto y una factura de 100 + 21): pagado 121 sin factura, cuadrado → base 100.
    /// 4100001 (acreedor sin facturas ni defectos): pagado 242, cuadrado → base 200 a la 6299000.
    /// 4000002: pagado 50, sin cuadrar → no entra. 4000003: saldo acreedor y cuadrado → no entra.
    /// </summary>
    private async Task PrepararAsync(bool conSiguiente = true)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        if (conSiguiente) await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2027);
        foreach (var (codigo, nombre) in new[] { ("100", "Capital"), ("129", "Resultado"), ("400", "Proveedores"), ("410", "Acreedores"), ("572", "Bancos"),
                     ("623", "Servicios de profesionales"), ("629", "Otros servicios") })
        {
            var alta = await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false));
            Assert.True(alta.EsCorrecto, alta.Error?.TextoOperador());
            // El ejercicio siguiente tiene los grupos (en la realidad, copiados al crearlo), con la misma identidad.
            if (conSiguiente) Assert.True((await Cuentas().AltaEnEjercicioAsync(_siguiente, alta.Valor.IdIdentidadContable)).EsCorrecto);
        }
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital social"), ("5720001", "Banco sintético"), ("6230000", "Servicios de profesionales"),
                     ("4000002", "Proveedor sin cuadrar"), ("4000003", "Proveedor acreedor"), ("4100001", "Acreedor sin facturas") })
            Assert.True((await Cuentas().AltaSubcuentaAsync(_ctx, codigo, nombre)).EsCorrecto);

        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, NIF) VALUES (?, ?, ?)", "Proveedor Ficticio S.L.", true, "B00000000");
            var tercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Proveedores (IdTercero, Activo) VALUES (?, ?)", tercero, true);
            var proveedor = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, Naturaleza, FechaAlta) VALUES (?, ?, ?)", "T01", (byte)0, DateTime.Today);
            var identidad = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, SaldoInicial, SaldoActual, NumeroApuntes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                identidad, 9001, "4000001", "Proveedor Ficticio S.L.", true, false, true, 0m, 0m, 0);
            var gasto = Convert.ToInt32(await s.EscalarAsync("SELECT IdIdentidadContable FROM CuentasContables WHERE IdSociedadEjercicio = 9001 AND Codigo = '6230000'"));
            await s.EjecutarAsync("INSERT INTO CuentasProveedoresSociedad (IdProveedor, IdSociedad, Clase, IdIdentidadContable, IdIdentidadGastoDefecto) VALUES (?, ?, ?, ?, ?)",
                proveedor, "T01", "PROVEEDOR", identidad, gasto);
            // La última factura contabilizada del proveedor (otra cuenta de gasto: manda la del proveedor).
            await s.EjecutarAsync(
                "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, CuentaContableProveedor, NumeroFactura, Fecha, ImporteBI, ImporteIVA, ImporteTotal, ConceptoGasto, " +
                "Pagada, Exportada, Duplicada, InversionSujetoPasivo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", "2026", 9001, "4000001", "F-SINT-1", new DateTime(2026, 3, 1), 100m, 21m, 121m, "6290000", true, true, false, false);
            return 0;
        });

        var motor = new MotorAsientosAccess(Bd);
        var capital = new Asiento(_ctx, new DateTime(2026, 1, 2), "Aportación sintética");
        capital.Apuntes.AddRange([Apunte.AlDebe("5720001", 3_000m), Apunte.AlHaber("1000000", 3_000m)]);
        var pagos = new Asiento(_ctx, new DateTime(2026, 11, 3), "Pagos sintéticos sin factura");
        pagos.Apuntes.AddRange([Apunte.AlDebe("4000001", 121m), Apunte.AlDebe("4100001", 242m), Apunte.AlDebe("4000002", 50m), Apunte.AlHaber("4000003", 30m),
            Apunte.AlHaber("5720001", 383m)]);
        var r = await motor.GuardarManualesAsync([capital, pagos], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        await CuadrarAsync("4000001", 121m);
        await CuadrarAsync("4100001", 242m);
        await CuadrarAsync("4000003", -30m);
    }

    private Task CuadrarAsync(string cuenta, decimal saldo) => Bd.EjecutarAsync(s => s.EjecutarAsync(
        "INSERT INTO CuadresCuentas (IdSociedadEjercicio, CuentaContable, Cuadrada, Saldo, Notas, Operador, FechaHora) VALUES (?, ?, ?, ?, ?, ?, ?)",
        9001, cuenta, true, saldo, null, "operador.prueba", new DateTime(2027, 1, 5, 10, 0, 0)));

    private async Task<(int Id, DateTime Fecha, decimal Debe, decimal Haber, int Apuntes)> AsientoAsync(string proceso, int idEjercicio) =>
        await Bd.EjecutarAsync(async s => await s.ConsultarPrimeroAsync(
            "SELECT a.IdAsiento, a.FechaContable, SUM(p.ImporteDebe) AS Debe, SUM(p.ImporteHaber) AS Haber, COUNT(*) AS N FROM Asientos AS a INNER JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.ClaveEjecucion = '2026' AND a.IdSociedadEjercicio = ? GROUP BY a.IdAsiento, a.FechaContable",
            f => (f.Entero("IdAsiento"), f.Fecha("FechaContable"), f.DecimalNulo("Debe") ?? 0m, f.DecimalNulo("Haber") ?? 0m, f.Entero("N")),
            ServicioFacturasPendientes.Motor, proceso, idEjercicio));

    [Fact]
    public async Task Confirmar_graba_la_provision_y_su_reversion_y_repetir_no_duplica_ni_realimenta()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var situacion = await servicio.SituacionAsync(_ctx);
        Assert.Equal(EstadoFacturasPendientes.Pendiente, situacion.Estado);
        var p = situacion.Propuesta;
        Assert.Equal(["4000001", "4100001"], p.Filas.Select(f => f.Cuenta));
        Assert.Equal(("6230000", 100m, OrigenBase.UltimaFactura), (p.Filas[0].CuentaGasto, p.Filas[0].Base, p.Filas[0].Origen));
        Assert.Equal(("6299000", 200m, OrigenBase.TipoSupuesto), (p.Filas[1].CuentaGasto, p.Filas[1].Base, p.Filas[1].Origen));
        Assert.Equal(300m, p.Total);

        var r = await servicio.ConfirmarAsync(_ctx, p.Ajustes, null, _actor, p);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.NotNull(r.Valor.IdProvision);
        Assert.NotNull(r.Valor.IdReversion);
        // Se crean la 4009000 y la 6299000 en los dos ejercicios; la 6230000 de 2027 también, porque la reversión la usa.
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("4009000") && c.EndsWith("2026"));
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("6299000") && c.EndsWith("2027"));
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("6230000") && c.EndsWith("2027"));

        var provision = await AsientoAsync(ServicioFacturasPendientes.ProcesoProvision, 9001);
        Assert.Equal((new DateTime(2026, 12, 31), 300m, 300m, 4), (provision.Fecha, provision.Debe, provision.Haber, provision.Apuntes));
        var reversion = await AsientoAsync(ServicioFacturasPendientes.ProcesoReversion, 9002);
        Assert.Equal((new DateTime(2027, 1, 1), 300m, 300m, 4), (reversion.Fecha, reversion.Debe, reversion.Haber, reversion.Apuntes));
        Assert.Equal(300m, Convert.ToDecimal(await LeerAsync(
            "SELECT SUM(p.ImporteHaber) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = 9001 AND p.CuentaContable = '4009000'")));
        Assert.Equal(300m, Convert.ToDecimal(await LeerAsync(
            "SELECT SUM(p.ImporteDebe) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = 9002 AND p.CuentaContable = '4009000'")));
        Assert.Equal(EstadoFacturasPendientes.Hecho, (await servicio.SituacionAsync(_ctx)).Estado);

        // Repetir: la misma propuesta (el proveedor no se ha tocado), pide motivo y no duplica.
        var otra = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.True(p.Igual(otra));
        Assert.Equal("FacturasPendientes.SinMotivo", (await servicio.ConfirmarAsync(_ctx, otra.Ajustes, null, _actor, otra)).Error!.Codigo);
        var repetido = await servicio.ConfirmarAsync(_ctx, otra.Ajustes, "Repetición de prueba", _actor, otra);
        Assert.True(repetido.EsCorrecto, repetido.Error?.Mensaje);
        Assert.Empty(repetido.Valor.CuentasCreadas);
        Assert.Equal(2, await DelProcesoAsync());
        Assert.Equal(300m, (await servicio.ProponerAsync(_ctx)).Valor.Total);
    }

    [Fact]
    public async Task Las_exclusiones_y_bases_a_mano_se_guardan_y_un_cambio_de_saldo_lo_desactualiza()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = (await servicio.ProponerAsync(_ctx)).Valor;
        var cambiada = CalculoFacturasPendientes.Aplicar(p, [new AjusteFacturaPendiente("4000001", false, 90m), new AjusteFacturaPendiente("4100001", true, null)]);
        // La propuesta vista no es la que se confirma con otros ajustes: no graba.
        Assert.Equal("FacturasPendientes.Cambiada", (await servicio.ConfirmarAsync(_ctx, [], null, _actor, cambiada)).Error!.Codigo);
        Assert.Equal("FacturasPendientes.Base", (await servicio.ConfirmarAsync(_ctx, [new AjusteFacturaPendiente("4000001", false, 0m)], null, _actor)).Error!.Codigo);
        var r = await servicio.ConfirmarAsync(_ctx, cambiada.Ajustes, null, _actor, cambiada);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM FacturasPendientesAjustes WHERE IdSociedadEjercicio = 9001"));

        // Al volver a proponer se respetan.
        var guardada = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.Equal((90m, false), (guardada.Filas[0].Base, guardada.Filas[1].Incluido));
        Assert.True(cambiada.Igual(guardada));
        Assert.Equal(EstadoFacturasPendientes.Hecho, (await servicio.SituacionAsync(_ctx)).Estado);

        // Otro pago al proveedor: su saldo ya no es el cuadrado, sale de la propuesta y lo grabado queda desactualizado.
        var motor = new MotorAsientosAccess(Bd);
        var pago = new Asiento(_ctx, new DateTime(2026, 12, 20), "Otro pago sintético");
        pago.Apuntes.AddRange([Apunte.AlDebe("4000001", 10m), Apunte.AlHaber("5720001", 10m)]);
        Assert.True((await motor.GuardarManualesAsync([pago], _actor, "Datos sintéticos")).EsCorrecto);
        var s = await servicio.SituacionAsync(_ctx);
        Assert.Equal(EstadoFacturasPendientes.Desactualizado, s.Estado);
        Assert.Contains(s.Propuesta.Avisos, a => a.Contains("4000001"));
    }

    [Fact]
    public async Task Deshacer_borra_los_dos_lotes_y_antes_de_acabar_el_ano_pide_motivo()
    {
        await PrepararAsync();
        var noviembre = Servicio(new DateTime(2026, 11, 20));
        var p = (await noviembre.ProponerAsync(_ctx)).Valor;
        Assert.Contains(p.Avisos, a => a.Contains("aún no ha terminado"));
        Assert.Equal("FacturasPendientes.SinMotivo", (await noviembre.ConfirmarAsync(_ctx, [], null, _actor, p)).Error!.Codigo);
        Assert.True((await noviembre.ConfirmarAsync(_ctx, [], "Prueba antes de acabar el año", _actor, p)).EsCorrecto);
        Assert.Equal(2, await DelProcesoAsync());

        var servicio = Servicio();
        Assert.Equal("FacturasPendientes.SinMotivo", (await servicio.DeshacerAsync(_ctx, " ", _actor)).Error!.Codigo);
        var d = await servicio.DeshacerAsync(_ctx, "Prueba de deshacer", _actor);
        Assert.True(d.EsCorrecto, d.Error?.Mensaje);
        Assert.Equal(2, d.Valor);
        Assert.Equal(0, await DelProcesoAsync());
        Assert.Equal(EstadoFacturasPendientes.Pendiente, (await servicio.SituacionAsync(_ctx)).Estado);
        Assert.Equal(0, (await servicio.DeshacerAsync(_ctx, "Otra vez", _actor)).Valor);
    }

    [Fact]
    public async Task Sin_el_ejercicio_siguiente_no_se_graba_y_el_error_dice_como_crearlo()
    {
        await PrepararAsync(conSiguiente: false);
        var servicio = Servicio();
        var s = await servicio.SituacionAsync(_ctx);
        Assert.False(s.ExisteSiguiente);
        Assert.Contains(s.Propuesta.Avisos, a => a.Contains("2027"));
        var r = await servicio.ConfirmarAsync(_ctx, [], null, _actor, s.Propuesta);
        Assert.Equal("FacturasPendientes.SinSiguiente", r.Error!.Codigo);
        Assert.Contains("Crea el ejercicio 2027", r.Error.TextoOperador());
        Assert.Equal(0, await DelProcesoAsync());
    }

    [Fact]
    public async Task El_cierre_ensena_su_estado_y_reabrirlo_no_toca_la_provision_ni_la_reversion()
    {
        await PrepararAsync();
        var cierre = new ServicioCierreEjercicio(Bd, new MotorAsientosAccess(Bd), Cuentas(), new ServicioNuevoEjercicio(Bd), null, new RelojFijo(Enero),
            _ => Task.FromResult<IReadOnlyList<string>>([]));
        var antes = (await cierre.ComprobarAsync(_ctx)).Single(c => c.Clave == "facturas-pendientes");
        Assert.Equal((ResultadoComprobacion.Aviso, DestinoComprobacion.FacturasPendientes), (antes.Resultado, antes.Destino));
        Assert.Contains("2 proveedores", antes.Detalle);

        var servicio = Servicio();
        var p = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.True((await servicio.ConfirmarAsync(_ctx, [], null, _actor, p)).EsCorrecto);
        var despues = (await cierre.ComprobarAsync(_ctx)).Single(c => c.Clave == "facturas-pendientes");
        Assert.Equal(ResultadoComprobacion.Correcta, despues.Resultado);

        // La provisión entra en la regularización del cierre (la 6299 y la 6230 van a la 129).
        var propuesta = (await cierre.ProponerAsync(_ctx)).Valor;
        var cerrado = await cierre.ConfirmarAsync(_ctx, "Prueba de cierre", _actor, propuesta);
        Assert.True(cerrado.EsCorrecto, cerrado.Error?.Mensaje);
        Assert.Equal(-300m, propuesta.Resultado);
        Assert.Equal(EstadoFacturasPendientes.Hecho, (await servicio.SituacionAsync(_ctx)).Estado);
        // Cerrado el ejercicio, no se puede repetir (el motor protege el ejercicio cerrado).
        Assert.False((await servicio.ConfirmarAsync(_ctx, [], "Con el ejercicio cerrado", _actor)).EsCorrecto);

        var reabierto = await cierre.ReabrirAsync(_ctx, "Prueba de reapertura", _actor);
        Assert.True(reabierto.EsCorrecto, reabierto.Error?.Mensaje);
        Assert.Equal(2, await DelProcesoAsync());
        Assert.Equal(EstadoFacturasPendientes.Hecho, (await servicio.SituacionAsync(_ctx)).Estado);
    }
}
