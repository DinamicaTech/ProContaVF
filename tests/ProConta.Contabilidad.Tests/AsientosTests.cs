using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

public class ReglasAsientoTests
{
    private static readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2025);

    private static Asiento Asiento(params Apunte[] apuntes)
    {
        var a = new Asiento(_ctx, new DateTime(2025, 3, 1), "Asiento sintético");
        a.Apuntes.AddRange(apuntes);
        return a;
    }

    [Fact]
    public void AsientoCuadradoEsValidoYNumeraLosApuntes()
    {
        var a = Asiento(Apunte.AlDebe("5720001", 100.005m), Apunte.AlHaber("7000001", 100.01m));
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, PoliticaCuadre.Estricta).EsCorrecto);
        Assert.Equal([1, 2], a.Apuntes.Select(x => x.Orden));
        Assert.Equal(100.01m, a.TotalDebe.Valor);
    }

    [Fact]
    public void DescuadreSinPoliticaSeRechaza()
    {
        var r = ReglasAsiento.ValidarYCuadrar(Asiento(Apunte.AlDebe("5720001", 100m), Apunte.AlHaber("7000001", 99.99m)), PoliticaCuadre.Estricta);
        Assert.Equal("Asientos.Descuadrado", r.Error!.Codigo);
        Assert.Contains("0.01", r.Error.Causa);
    }

    [Fact]
    public void AjusteDeRedondeoSoloConPoliticaYVisible()
    {
        var politica = new PoliticaCuadre(0.02m, "6690001", "7690001");
        var a = Asiento(Apunte.AlDebe("5720001", 100m), Apunte.AlHaber("7000001", 99.99m));
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, politica).EsCorrecto);
        var ajuste = a.Apuntes.Last();
        Assert.Equal(TipoApunte.AjusteRedondeo, ajuste.Tipo);
        Assert.Equal("7690001", ajuste.CodigoCuenta);
        Assert.Equal(0.01m, ajuste.Haber.Valor);
        Assert.Contains("redondeo", ajuste.MotivoGeneracion);
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, politica).EsCorrecto); // revalidar no duplica el ajuste
        Assert.Single(a.Apuntes, x => x.Tipo == TipoApunte.AjusteRedondeo);

        var grande = Asiento(Apunte.AlDebe("5720001", 100m), Apunte.AlHaber("7000001", 99.90m));
        Assert.Equal("Asientos.Descuadrado", ReglasAsiento.ValidarYCuadrar(grande, politica).Error!.Codigo);
        Assert.Equal("Asientos.SinCuentasRedondeo", new PoliticaCuadre(0.01m).Validar().Error!.Codigo);
    }

    [Fact]
    public void ApuntesYFechaDebenSerCoherentes()
    {
        var dosLados = Asiento(new Apunte { CodigoCuenta = "5720001", Debe = Nucleo.Importes.Importe.Redondear(1m), Haber = Nucleo.Importes.Importe.Redondear(1m) }, Apunte.AlHaber("7000001", 0m));
        var r = ReglasAsiento.ValidarYCuadrar(dosLados, PoliticaCuadre.Estricta);
        Assert.Equal(2, r.Errores.Count(e => e.Codigo == "Asientos.ApunteSinLado"));

        var fuera = new Asiento(_ctx, new DateTime(2026, 1, 1), "Fuera de ejercicio");
        fuera.Apuntes.AddRange([Apunte.AlDebe("5720001", 1m), Apunte.AlHaber("7000001", 1m)]);
        Assert.Equal("Asientos.FechaFueraDeEjercicio", ReglasAsiento.ValidarYCuadrar(fuera, PoliticaCuadre.Estricta).Error!.Codigo);
        Assert.Equal("Asientos.PocosApuntes", ReglasAsiento.ValidarYCuadrar(Asiento(Apunte.AlDebe("5720001", 1m)), PoliticaCuadre.Estricta).Errores.Last().Codigo);
        Assert.Equal("Asientos.ImporteNegativo", ReglasAsiento.ValidarYCuadrar(Asiento(Apunte.AlDebe("5720001", -1m), Apunte.AlHaber("7000001", -1m)), PoliticaCuadre.Estricta).Error!.Codigo);
    }
}

[Trait("Categoria", "Access")]
public sealed class MotorAsientosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2025);
    private readonly Actor _actor = new("operador.prueba");
    private readonly IdentidadLote _lote = new("VENTAS", "CONTABILIZAR_FACTURA", "FV-2025-0001");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private MotorAsientosAccess Motor() => new(Bd);
    private CatalogoCuentasAccess Cuentas() => new(Bd);

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2024, cerrado: true);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco sintético"), ("7000001", "Ventas sintéticas"), ("4300001", "Cliente sintético"), ("6290001", "Otros servicios") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        await Cuentas().AltaAsync(_ctx, new AltaCuenta("572", "Bancos", EsImputable: false));
    }

    private Asiento Nuevo(decimal debe, decimal haber, string cuentaDebe = "5720001", string cuentaHaber = "7000001", ContextoSociedadEjercicio? ctx = null)
    {
        var c = ctx ?? _ctx;
        var a = new Asiento(c, new DateTime(c.Ejercicio, 3, 1), "Venta sintética");
        a.Apuntes.AddRange([Apunte.AlDebe(cuentaDebe, debe), Apunte.AlHaber(cuentaHaber, haber)]);
        return a;
    }

    private Task<int> ContarAsync(string tabla) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM {tabla}")));

    [Fact]
    public async Task AsientoDesequilibradoNoPersiste()
    {
        await PrepararAsync();
        var r = await Motor().GuardarManualAsync(Nuevo(100m, 90m), _actor);
        Assert.Equal("Asientos.Descuadrado", r.Error!.Codigo);
        Assert.Equal(0, await ContarAsync("Asientos"));
        Assert.Equal(0, await ContarAsync("Apuntes"));
    }

    [Fact]
    public async Task AltaYModificacionConRevisionRegistroYSaldos()
    {
        await PrepararAsync();
        var motor = Motor();
        var alta = await motor.GuardarManualAsync(Nuevo(121m, 121m), _actor);
        Assert.True(alta.EsCorrecto, alta.Error?.ToString());
        var asiento = alta.Valor;
        Assert.Equal(1, asiento.Version);
        Assert.False(asiento.EsAutomatico);
        Assert.Equal(121m, (await Cuentas().ResolverAsync(_ctx, "5720001")).Valor.SaldoActual);

        asiento.Apuntes[0].Debe = Nucleo.Importes.Importe.Redondear(150m);
        Assert.Equal("Asientos.SinMotivo", (await motor.GuardarManualAsync(asiento, _actor)).Error!.Codigo);
        Assert.Equal("Asientos.Descuadrado", (await motor.GuardarManualAsync(asiento, _actor, "corregir importe")).Error!.Codigo);
        asiento.Apuntes[1].Haber = Nucleo.Importes.Importe.Redondear(150m);
        var modificado = await motor.GuardarManualAsync(asiento, _actor, "corregir importe");
        Assert.True(modificado.EsCorrecto, modificado.Error?.ToString());
        Assert.Equal(2, modificado.Valor.Version);
        Assert.Equal(150m, (await Cuentas().ResolverAsync(_ctx, "5720001")).Valor.SaldoActual);
        Assert.Equal(-150m, (await Cuentas().ResolverAsync(_ctx, "7000001")).Valor.SaldoActual);

        var registro = await motor.RegistroCambiosAsync(asiento.IdAsiento);
        Assert.Equal(2, registro.Count(c => c.Operacion == "ALTA"));
        var cambio = Assert.Single(registro, c => c.Campo == "ImporteDebe");
        Assert.Equal(("121.00", "150.00", "corregir importe", "operador.prueba"), (cambio.ValorAntiguo, cambio.ValorNuevo, cambio.Motivo, cambio.Operador));
        Assert.Single(registro.Where(c => c.Operacion == "MODIFICACION").Select(c => c.IdLote).Distinct());
        Assert.Equal(2, await ContarAsync("RevisionesAsientos"));

        // Conflicto de versión: guardar sobre la versión 1 leída antes.
        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(() => motor.GuardarManualAsync(asiento, _actor, "otro cambio"));
    }

    [Fact]
    public async Task EjercicioCerradoCuentasYCentrosNoValidosSeRechazan()
    {
        await PrepararAsync();
        var motor = Motor();
        var cerrado = new ContextoSociedadEjercicio("T01", 9000, 2024);
        Assert.Equal("Asientos.EjercicioCerrado", (await motor.GuardarManualAsync(Nuevo(1m, 1m, ctx: cerrado), _actor)).Error!.Codigo);
        Assert.Equal("Cuentas.NoExiste", (await motor.GuardarManualAsync(Nuevo(1m, 1m, cuentaDebe: "5729999"), _actor)).Error!.Codigo);
        Assert.Equal("Cuentas.NoAdmiteApuntes", (await motor.GuardarManualAsync(Nuevo(1m, 1m, cuentaDebe: "572"), _actor)).Error!.Codigo);

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CuentasContables SET EstaBloqueada = True WHERE Codigo = ?", "6290001"));
        Assert.Contains("bloqueada", (await motor.GuardarManualAsync(Nuevo(1m, 1m, cuentaDebe: "6290001"), _actor)).Error!.Causa);

        var raiz = (await new CatalogoCentrosCosteAccess(Bd).AltaAsync("T01", "EMP", "Empresa", esImputable: false)).Valor;
        var conCentro = Nuevo(1m, 1m);
        conCentro.Apuntes[0].IdCentroCoste = raiz.IdCentroCoste;
        Assert.Equal("Asientos.CentroNoValido", (await motor.GuardarManualAsync(conCentro, _actor)).Error!.Codigo);
        Assert.Equal(0, await ContarAsync("Asientos"));
    }

    [Fact]
    public async Task LibroExternoSeAdmiteEnEjercicioYTrimestreCerradosSoloParaElImportador()
    {
        await PrepararAsync();
        var motor = Motor();
        var cerrado = new ContextoSociedadEjercicio("T01", 9000, 2024);
        // Las cuentas se dan de alta con el ejercicio abierto y luego se cierra.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = False WHERE IdSociedadEjercicio = 9000"));
        // Códigos propios: la reserva histórica no deja dar de alta en 2024 los de 2025 como cuentas nuevas.
        foreach (var (codigo, nombre) in new[] { ("5720002", "Banco sintético 2024"), ("7000002", "Ventas sintéticas 2024") })
            Assert.True((await Cuentas().AltaAsync(cerrado, new AltaCuenta(codigo, nombre))).EsCorrecto);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9000"));
        Asiento EnCerrado() => Nuevo(1m, 1m, "5720002", "7000002", cerrado);
        var externo = new OpcionesLote(LibroExterno: true);
        var importador = new IdentidadLote(OpcionesLote.MotorImportadorContaSol, "CONTASOL", "T01/2024/1:5");
        Assert.Equal("Asientos.EjercicioCerrado", (await motor.ReemplazarLoteAsync(cerrado, importador, [EnCerrado()], _actor, "Importar")).Error!.Codigo);
        Assert.Equal("Asientos.LibroExternoNoPermitido", (await motor.ReemplazarLoteAsync(cerrado, _lote, [EnCerrado()], _actor, "Importar", opciones: externo)).Error!.Codigo);
        Assert.True((await motor.ReemplazarLoteAsync(cerrado, importador, [EnCerrado()], _actor, "Importar", opciones: externo)).EsCorrecto);

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));
        var trimestre = importador with { ClaveEjecucion = "T01/2025/1:5" };
        Assert.True((await motor.ReemplazarLoteAsync(_ctx, trimestre, [Nuevo(2m, 2m)], _actor, "Importar", opciones: externo)).EsCorrecto);
        Assert.True((await motor.EliminarLoteAsync(_ctx, trimestre, _actor, "Ya no está en ContaSol", opciones: externo)).EsCorrecto);
        Assert.Equal(1, await ContarAsync("Asientos"));
    }

    [Fact]
    public async Task TrimestreCerradoNoSeCreaCambiaNiBorra()
    {
        await PrepararAsync();
        var motor = Motor();
        var primero = await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "4300001")], _actor, "Contabilizar factura");
        Assert.True(primero.EsCorrecto);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));

        Assert.Equal("Asientos.TrimestreCerrado", (await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(150m, 150m, "4300001")], _actor, "Regenerar")).Error!.Codigo);
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.EliminarLoteAsync(_ctx, _lote, _actor, "Deshacer")).Error!.Codigo);
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.GuardarManualAsync(Nuevo(1m, 1m), _actor)).Error!.Codigo);
        Assert.Equal(1, await ContarAsync("Asientos"));

        var enElActivo = Nuevo(1m, 1m);
        enElActivo.FechaContable = new DateTime(2025, 4, 1);
        Assert.True((await motor.GuardarManualAsync(enElActivo, _actor)).EsCorrecto);
        Assert.Equal(2, await ContarAsync("Asientos"));
    }

    [Fact]
    public async Task ElPunteoYElNumeroDeFacturaDelMayorValenEnAutomaticosYSeConservanAlRegenerar()
    {
        await PrepararAsync();
        var motor = Motor();
        var id = (await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "4300001")], _actor, "Contabilizar factura")).Valor[0];
        var ap = (await motor.ObtenerAsync(id))!.Apuntes;
        Assert.True((await motor.PuntearApuntesAsync([ap[0].IdApunte], true, _actor)).EsCorrecto);
        Assert.True((await motor.AsignarNumeroDocumentoAsync([ap[1].IdApunte], "F2025-7", _actor, "Factura del cobro")).EsCorrecto);
        Assert.Equal("Asientos.Automatico", (await motor.CambiarConceptoApuntesAsync([ap[0].IdApunte], "Otro", _actor, "intento")).Error!.Codigo);

        // Regenerar con los mismos importes: punteo y número se conservan (el proceso no trae número).
        await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "4300001")], _actor, "Regenerar factura");
        var tras = (await motor.ObtenerAsync(id))!.Apuntes;
        Assert.True(tras[0].Punteado);
        Assert.Equal("F2025-7", tras[1].NumeroDocumento);

        // Con otro importe el apunte es otro: el punteo se pierde y lo dice el registro de cambios.
        await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(150m, 150m, "4300001")], _actor, "Regenerar con otro importe");
        Assert.False((await motor.ObtenerAsync(id))!.Apuntes[0].Punteado);
        var registro = await motor.RegistroCambiosAsync(id);
        Assert.Contains(registro, c => c.Campo == "Punteado" && c.ValorAntiguo == "No" && c.ValorNuevo == "Sí");
        Assert.Contains(registro, c => c.Campo == "Punteado" && c.ValorAntiguo == "Sí" && c.ValorNuevo == "No" && c.Motivo == "Regenerar con otro importe");
        Assert.Contains(registro, c => c.Campo == "NumeroDocumento" && c.ValorNuevo == "F2025-7" && c.Motivo == "Factura del cobro");
    }

    [Fact]
    public async Task ElConceptoSeCambiaEnManualesConMotivoYElPunteoSeHaceTambienEnCerrados()
    {
        await PrepararAsync();
        var motor = Motor();
        var asiento = (await motor.GuardarManualAsync(Nuevo(10m, 10m), _actor)).Valor;
        var idApunte = asiento.Apuntes[0].IdApunte;
        Assert.Equal("Asientos.SinMotivo", (await motor.CambiarConceptoApuntesAsync([idApunte], "Cobro en efectivo", _actor, " ")).Error!.Codigo);
        Assert.True((await motor.CambiarConceptoApuntesAsync([idApunte], "Cobro en efectivo", _actor, "Concepto más claro")).EsCorrecto);
        var leido = (await motor.ObtenerAsync(asiento.IdAsiento))!;
        Assert.Equal("Cobro en efectivo", leido.Apuntes[0].Descripcion);
        Assert.Equal(2, leido.Version);

        // Guardar el asiento a mano conserva el punteo del apunte que no cambia.
        Assert.True((await motor.PuntearApuntesAsync([idApunte], true, _actor)).EsCorrecto);
        leido.Notas = "Nota nueva";
        leido.Apuntes[0].Punteado = false;
        Assert.True((await motor.GuardarManualAsync(leido, _actor, "Añadir nota")).EsCorrecto);
        Assert.True((await motor.ObtenerAsync(asiento.IdAsiento))!.Apuntes[0].Punteado);

        // El punteo no cambia la contabilidad: vale con el ejercicio cerrado, el concepto no.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9001"));
        var otro = (await motor.ObtenerAsync(asiento.IdAsiento))!.Apuntes[0].IdApunte;
        Assert.True((await motor.PuntearApuntesAsync([otro], false, _actor)).EsCorrecto);
        Assert.Equal("Asientos.EjercicioCerrado", (await motor.CambiarConceptoApuntesAsync([otro], "x", _actor, "motivo")).Error!.Codigo);
        Assert.Equal("Asientos.ApunteNoEncontrado", (await motor.PuntearApuntesAsync([999999], true, _actor)).Error!.Codigo);
    }

    [Fact]
    public async Task LoteAutomaticoSeRegeneraSinDuplicarYRechazaEdicionManual()
    {
        await PrepararAsync();
        var motor = Motor();
        var primero = await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "4300001")], _actor, "Contabilizar factura");
        Assert.True(primero.EsCorrecto, primero.Error?.ToString());
        var segundo = await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(242m, 242m, "4300001")], _actor, "Regenerar factura");
        Assert.True(segundo.EsCorrecto, segundo.Error?.ToString());
        Assert.Equal(1, await ContarAsync("Asientos"));
        Assert.Equal(242m, (await Cuentas().ResolverAsync(_ctx, "4300001")).Valor.SaldoActual);

        var automatico = (await motor.ObtenerAsync(segundo.Valor[0]))!;
        Assert.Equal(_lote, automatico.Lote);
        automatico.Descripcion = "Cambio manual";
        Assert.Equal("Asientos.Automatico", (await motor.GuardarManualAsync(automatico, _actor, "intento manual")).Error!.Codigo);
        Assert.Equal("Asientos.Automatico", (await motor.EliminarManualAsync(automatico.IdAsiento, automatico.Version, _actor, "intento")).Error!.Codigo);
        Assert.Equal("Asientos.Automatico", (await motor.CambiarCuentaApuntesAsync(_ctx, [automatico.Apuntes[0].IdApunte], "6290001", _actor, "intento")).Error!.Codigo);

        // Un lote con un asiento descuadrado no guarda ninguno y deja el anterior intacto.
        var roto = await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(1m, 1m, "4300001"), Nuevo(5m, 4m, "4300001")], _actor, "Regenerar con error");
        Assert.Equal("Asientos.Descuadrado", roto.Error!.Codigo);
        Assert.Equal(segundo.Valor, (await Bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdAsiento FROM Asientos", f => f.Entero("IdAsiento")))).ToArray());

        var borrados = await motor.EliminarLoteAsync(_ctx, _lote, _actor, "Factura anulada");
        Assert.Equal(1, borrados.Valor);
        Assert.Equal(0, await ContarAsync("Asientos"));
        Assert.Equal(0m, (await Cuentas().ResolverAsync(_ctx, "4300001")).Valor.SaldoActual);
        Assert.Contains(await motor.RegistroCambiosAsync(automatico.IdAsiento), c => c.Operacion == "BAJA" && c.Motivo == "Factura anulada");
    }

    [Fact]
    public async Task CambioDeCuentaMasivoConUnSoloIdLote()
    {
        await PrepararAsync();
        var motor = Motor();
        var a1 = (await motor.GuardarManualAsync(Nuevo(10m, 10m), _actor)).Valor;
        var a2 = (await motor.GuardarManualAsync(Nuevo(20m, 20m), _actor)).Valor;
        var r = await motor.CambiarCuentaApuntesAsync(_ctx, [a1.Apuntes[1].IdApunte, a2.Apuntes[1].IdApunte], "6290001", _actor, "Reclasificación");
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(0m, (await Cuentas().ResolverAsync(_ctx, "7000001")).Valor.SaldoActual);
        Assert.Equal(-30m, (await Cuentas().ResolverAsync(_ctx, "6290001")).Valor.SaldoActual);
        var cambios = (await motor.RegistroCambiosAsync(a1.IdAsiento)).Concat(await motor.RegistroCambiosAsync(a2.IdAsiento)).Where(c => c.Campo == "CuentaContable").ToList();
        Assert.Equal(2, cambios.Count);
        Assert.All(cambios, c => Assert.Equal((r.Valor, "7000001", "6290001"), (c.IdLote, c.ValorAntiguo, c.ValorNuevo)));
        Assert.Equal(2, (await motor.ObtenerAsync(a1.IdAsiento))!.Version);
        Assert.Equal("Cuentas.NoAdmiteApuntes", (await motor.CambiarCuentaApuntesAsync(_ctx, [a1.Apuntes[0].IdApunte], "572", _actor, "x")).Error!.Codigo);
    }

    [Fact]
    public async Task BorradoManualConVersionYMotivo()
    {
        await PrepararAsync();
        var motor = Motor();
        var a = (await motor.GuardarManualAsync(Nuevo(10m, 10m), _actor)).Valor;
        Assert.Equal("Asientos.SinMotivo", (await motor.EliminarManualAsync(a.IdAsiento, a.Version, _actor, " ")).Error!.Codigo);
        await Assert.ThrowsAsync<ConflictoConcurrenciaException>(() => motor.EliminarManualAsync(a.IdAsiento, a.Version + 1, _actor, "duplicado"));
        Assert.True((await motor.EliminarManualAsync(a.IdAsiento, a.Version, _actor, "duplicado")).EsCorrecto);
        Assert.Null(await motor.ObtenerAsync(a.IdAsiento));
        Assert.Equal(0m, (await Cuentas().ResolverAsync(_ctx, "5720001")).Valor.SaldoActual);
    }
}
