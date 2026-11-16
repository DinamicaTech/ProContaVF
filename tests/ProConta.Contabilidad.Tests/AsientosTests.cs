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

public class CorreccionAutomaticoTests
{
    private static readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2025);

    private static Asiento Asiento(DateTime fecha, params Apunte[] apuntes)
    {
        var a = new Asiento(_ctx, fecha, "Asiento sintético");
        a.Apuntes.AddRange(apuntes);
        Assert.True(ReglasAsiento.ValidarYCuadrar(a, PoliticaCuadre.Estricta).EsCorrecto);
        return a;
    }

    private static readonly DateTime _dia = new(2025, 3, 1);
    private static Asiento Nomina(string gasto = "6400001", decimal bruto = 1000m, decimal banco = 850m) =>
        Asiento(_dia, Apunte.AlDebe(gasto, bruto), Apunte.AlHaber("4760001", bruto - banco), Apunte.AlHaber("5720001", banco));
    private static Asiento Venta(string cliente = "4300001", string ingreso = "7050001", decimal baseImponible = 100m, string iva = "4770021") =>
        Asiento(_dia, Apunte.AlDebe(cliente, baseImponible * 1.21m), Apunte.AlHaber(ingreso, baseImponible), Apunte.AlHaber(iva, baseImponible * 0.21m));

    [Fact]
    public void SinLibroDeIvaSeCambianCuentasEImportesSalvoLa572()
    {
        Assert.True(ReglasAsiento.ValidarCorreccionAutomatico(Nomina(), Nomina("6400002", 1100m), conLibroIva: false).EsCorrecto);
        // Añadir una línea también vale si la 572 queda como estaba.
        var conLinea = Asiento(_dia, Apunte.AlDebe("6400001", 900m), Apunte.AlDebe("6420001", 100m), Apunte.AlHaber("4760001", 150m), Apunte.AlHaber("5720001", 850m));
        Assert.True(ReglasAsiento.ValidarCorreccionAutomatico(Nomina(), conLinea, conLibroIva: false).EsCorrecto);

        Assert.Equal("Asientos.BancoAutomatico", ReglasAsiento.ValidarCorreccionAutomatico(Nomina(), Nomina(banco: 800m), false).Error!.Codigo);
        var otroBanco = Asiento(_dia, Apunte.AlDebe("6400001", 1000m), Apunte.AlHaber("4760001", 150m), Apunte.AlHaber("5720002", 850m));
        Assert.Equal("Asientos.BancoAutomatico", ReglasAsiento.ValidarCorreccionAutomatico(Nomina(), otroBanco, false).Error!.Codigo);
        var sinBanco = Asiento(_dia, Apunte.AlDebe("6400001", 1000m), Apunte.AlHaber("4760001", 1000m));
        Assert.Equal("Asientos.BancoAutomatico", ReglasAsiento.ValidarCorreccionAutomatico(Nomina(), sinBanco, false).Error!.Codigo);
        var bancoNuevo = Asiento(_dia, Apunte.AlDebe("6400001", 1000m), Apunte.AlHaber("5720001", 150m), Apunte.AlHaber("5720001", 850m));
        Assert.Equal("Asientos.BancoAutomatico", ReglasAsiento.ValidarCorreccionAutomatico(Nomina(), bancoNuevo, false).Error!.Codigo);
    }

    [Fact]
    public void ConLibroDeIvaSoloCambianLasSubcuentasQueNoSonDeIva()
    {
        Assert.True(ReglasAsiento.ValidarCorreccionAutomatico(Venta(), Venta("4300002", "7050002"), conLibroIva: true).EsCorrecto);
        Assert.Equal("Asientos.FacturaConLibroIva", ReglasAsiento.ValidarCorreccionAutomatico(Venta(), Venta(baseImponible: 200m), true).Error!.Codigo);
        Assert.Equal("Asientos.FacturaConLibroIva", ReglasAsiento.ValidarCorreccionAutomatico(Venta(), Venta(iva: "4770010"), true).Error!.Codigo);
        Assert.Equal("Asientos.FacturaConLibroIva", ReglasAsiento.ValidarCorreccionAutomatico(Venta(), Venta(ingreso: "4770021"), true).Error!.Codigo);
        var otraFecha = Venta();
        otraFecha.FechaContable = _dia.AddDays(1);
        Assert.Equal("Asientos.FacturaConLibroIva", ReglasAsiento.ValidarCorreccionAutomatico(Venta(), otraFecha, true).Error!.Codigo);
        var masLineas = Asiento(_dia, Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7050001", 60m), Apunte.AlHaber("7050002", 40m), Apunte.AlHaber("4770021", 21m));
        Assert.Equal("Asientos.FacturaConLibroIva", ReglasAsiento.ValidarCorreccionAutomatico(Venta(), masLineas, true).Error!.Codigo);
        // Sin libro de IVA (una nómina, un asiento de banco) los importes sí se corrigen.
        Assert.True(ReglasAsiento.ValidarCorreccionAutomatico(Venta(), Venta(baseImponible: 200m), conLibroIva: false).EsCorrecto);
    }

    [Fact]
    public void ElBloqueoDeCadaApunteLoDiceLaMismaRegla()
    {
        Assert.NotNull(ReglasAsiento.BloqueoCuentaAutomatico("5720001", conLibroIva: false));
        Assert.Null(ReglasAsiento.BloqueoCuentaAutomatico("4770021", conLibroIva: false));
        Assert.NotNull(ReglasAsiento.BloqueoCuentaAutomatico("4770021", conLibroIva: true));
        Assert.NotNull(ReglasAsiento.BloqueoCuentaAutomatico("4751001", conLibroIva: true));
        Assert.Null(ReglasAsiento.BloqueoCuentaAutomatico("7050001", conLibroIva: true));
    }

    [Fact]
    public void ElCentroDeCosteNoTieneEfectoFiscal()
    {
        var antes = Venta();
        var despues = Venta();
        despues.Apuntes[1].IdCentroCoste = 7;
        despues.Apuntes[0].FechaVencimiento = _dia.AddDays(30);
        Assert.True(ReglasAsiento.EsCambioSinEfectoFiscal(antes, despues));
        Assert.False(ReglasAsiento.EsCambioSinEfectoFiscal(antes, Venta(baseImponible: 200m)));
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
        Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta("4770001", "IVA repercutido sintético"))).EsCorrecto);
        var motor = Motor();
        // Asientos con una cuenta fiscal del trimestre (477): los protege el trimestre cerrado.
        var primero = await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "4300001", "4770001")], _actor, "Contabilizar factura");
        Assert.True(primero.EsCorrecto);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));

        // Ni regenerarlo quitándole la cuenta fiscal: se mira también el asiento anterior.
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(150m, 150m, "4300001")], _actor, "Regenerar")).Error!.Codigo);
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.EliminarLoteAsync(_ctx, _lote, _actor, "Deshacer")).Error!.Codigo);
        var manual = await motor.GuardarManualAsync(Nuevo(1m, 1m, "5720001", "4770001"), _actor);
        Assert.Equal("Asientos.TrimestreCerrado", manual.Error!.Codigo);
        Assert.Contains("4770001", manual.Error.Mensaje);
        Assert.Equal(1, await ContarAsync("Asientos"));

        var enElActivo = Nuevo(1m, 1m, "5720001", "4770001");
        enElActivo.FechaContable = new DateTime(2025, 4, 1);
        Assert.True((await motor.GuardarManualAsync(enElActivo, _actor)).EsCorrecto);
        Assert.Equal(2, await ContarAsync("Asientos"));
    }

    [Fact]
    public async Task TrimestreCerradoAdmiteCambiarTextosYLaSubcuentaDelTerceroSinEfectoFiscal()
    {
        await PrepararAsync();
        foreach (var (codigo, nombre) in new[] { ("4770001", "IVA repercutido sintético"), ("4300002", "Otro cliente sintético"), ("4751001", "Retenciones sintéticas"), ("4100001", "Acreedor sintético"), ("4100002", "Otro acreedor sintético") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        var motor = Motor();
        // Factura con IVA en marzo (430 al Debe; 700 y 477 al Haber) y otra con retención; luego se cierra el primer trimestre.
        var factura = new Asiento(_ctx, new DateTime(2025, 3, 1), "Factura sintética");
        factura.Apuntes.AddRange([Apunte.AlDebe("4300001", 121m, "Factura 1"), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m)]);
        var conRetencion = new Asiento(_ctx, new DateTime(2025, 3, 2), "Factura con retención sintética");
        conRetencion.Apuntes.AddRange([Apunte.AlDebe("6290001", 100m), Apunte.AlHaber("4100001", 85m), Apunte.AlHaber("4751001", 15m)]);
        var idFactura = (await motor.GuardarManualAsync(factura, _actor)).Valor.IdAsiento;
        var idRetencion = (await motor.GuardarManualAsync(conRetencion, _actor)).Valor.IdAsiento;
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));

        // (a) Descripción, notas, concepto y documento: se cambian aunque el asiento lleve IVA.
        var a = (await motor.ObtenerAsync(idFactura))!;
        a.Descripcion = "Factura sintética corregida";
        a.Notas = "Texto largo de prueba";
        a.Apuntes[0].Descripcion = "Factura 1 · alquiler marzo";
        a.Apuntes[0].NumeroDocumento = "F-1";
        var textos = await motor.GuardarManualAsync(a, _actor, "Corregir textos");
        Assert.True(textos.EsCorrecto, textos.Error?.ToString());
        Assert.True((await motor.CambiarConceptoApuntesAsync([textos.Valor.Apuntes[1].IdApunte], "Ventas marzo", _actor, "Concepto")).EsCorrecto);

        // (b) Subcuenta del cliente por otra del mismo grupo 430: se admite, con el Mayor y en el asiento.
        var b = (await motor.ObtenerAsync(idFactura))!;
        Assert.True((await motor.CambiarCuentaApuntesAsync(_ctx, [b.Apuntes[0].IdApunte], "4300002", _actor, "Otro cliente")).EsCorrecto);
        b = (await motor.ObtenerAsync(idFactura))!;
        Assert.Equal("4300002", b.Apuntes[0].CodigoCuenta);
        b.Apuntes[0].CodigoCuenta = "4300001";
        Assert.True((await motor.GuardarManualAsync(b, _actor, "Volver al cliente")).EsCorrecto);

        // Lo demás sigue bloqueado: importes, otra cuenta que no es del grupo, la cuenta de IVA, la fecha y el borrado.
        var c = (await motor.ObtenerAsync(idFactura))!;
        c.Apuntes[0].Debe = Nucleo.Importes.Importe.Redondear(122m);
        c.Apuntes[1].Haber = Nucleo.Importes.Importe.Redondear(101m);
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.GuardarManualAsync(c, _actor, "Importe")).Error!.Codigo);
        c = (await motor.ObtenerAsync(idFactura))!;
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.CambiarCuentaApuntesAsync(_ctx, [c.Apuntes[0].IdApunte], "5720001", _actor, "Otro grupo")).Error!.Codigo);
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.CambiarCuentaApuntesAsync(_ctx, [c.Apuntes[1].IdApunte], "4300002", _actor, "Otro grupo")).Error!.Codigo);
        c.FechaContable = new DateTime(2025, 3, 5);
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.GuardarManualAsync(c, _actor, "Fecha")).Error!.Codigo);
        c = (await motor.ObtenerAsync(idFactura))!;
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.EliminarManualAsync(c.IdAsiento, c.Version, _actor, "Sobra")).Error!.Codigo);

        // Con la 4751 (retenciones) ni siquiera la subcuenta del acreedor; los textos sí.
        var r = (await motor.ObtenerAsync(idRetencion))!;
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.CambiarCuentaApuntesAsync(_ctx, [r.Apuntes[1].IdApunte], "4100002", _actor, "Otro acreedor")).Error!.Codigo);
        r.Apuntes[1].Descripcion = "Profesional · marzo";
        Assert.True((await motor.GuardarManualAsync(r, _actor, "Concepto")).EsCorrecto);
    }

    [Fact]
    public void ElCambioSinEfectoFiscalSoloAdmiteTextosYSubcuentaDelMismoGrupo()
    {
        static Asiento A(params Apunte[] apuntes)
        {
            var a = new Asiento(new ContextoSociedadEjercicio("T01", 9001, 2025), new DateTime(2025, 3, 1), "x");
            a.Apuntes.AddRange(apuntes);
            ReglasAsiento.ValidarYCuadrar(a, PoliticaCuadre.Estricta);
            return a;
        }
        var original = A(Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m));
        Assert.True(ReglasAsiento.EsCambioSinEfectoFiscal(original, A(Apunte.AlDebe("4300001", 121m, "otro concepto"), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m))));
        Assert.True(ReglasAsiento.EsCambioSinEfectoFiscal(original, A(Apunte.AlDebe("4300099", 121m), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m))));
        Assert.False(ReglasAsiento.EsCambioSinEfectoFiscal(original, A(Apunte.AlDebe("4100001", 121m), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m))));
        Assert.False(ReglasAsiento.EsCambioSinEfectoFiscal(original, A(Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7000002", 100m), Apunte.AlHaber("4770001", 21m))));
        Assert.False(ReglasAsiento.EsCambioSinEfectoFiscal(original, A(Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770002", 21m))));
        Assert.False(ReglasAsiento.EsCambioSinEfectoFiscal(original, A(Apunte.AlDebe("4300001", 122m), Apunte.AlHaber("7000001", 101m), Apunte.AlHaber("4770001", 21m))));
        var conRetencion = A(Apunte.AlDebe("6230001", 100m), Apunte.AlHaber("4100001", 85m), Apunte.AlHaber("4751001", 15m));
        Assert.False(ReglasAsiento.EsCambioSinEfectoFiscal(conRetencion, A(Apunte.AlDebe("6230001", 100m), Apunte.AlHaber("4100002", 85m), Apunte.AlHaber("4751001", 15m))));
        Assert.True(ReglasAsiento.EsCambioSinEfectoFiscal(conRetencion, A(Apunte.AlDebe("6230001", 100m, "Notas"), Apunte.AlHaber("4100001", 85m), Apunte.AlHaber("4751001", 15m))));
    }

    [Fact]
    public async Task TrimestreCerradoAdmiteAsientosSinCuentasFiscalesPeroNoPonerlesUna()
    {
        await PrepararAsync();
        Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta("4720001", "IVA soportado sintético"))).EsCorrecto);
        var motor = Motor();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));

        // Banco contra cliente en marzo (trimestre cerrado): se crea, se regenera y se borra.
        Assert.True((await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "5720001", "4300001")], _actor, "Cobro")).EsCorrecto);
        Assert.True((await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(150m, 150m, "5720001", "4300001")], _actor, "Regenerar")).EsCorrecto);
        Assert.True((await motor.EliminarLoteAsync(_ctx, _lote, _actor, "Deshacer")).EsCorrecto);

        var alta = await motor.GuardarManualAsync(Nuevo(10m, 10m, "6290001", "5720001"), _actor);
        Assert.True(alta.EsCorrecto, alta.Error?.ToString());
        var asiento = alta.Valor;
        asiento.Apuntes[0].Debe = Nucleo.Importes.Importe.Redondear(12m);
        asiento.Apuntes[1].Haber = Nucleo.Importes.Importe.Redondear(12m);
        var cambiado = await motor.GuardarManualAsync(asiento, _actor, "Corregir importe");
        Assert.True(cambiado.EsCorrecto, cambiado.Error?.ToString());

        // Ponerle una cuenta fiscal en un trimestre cerrado, no: ni cambiando la cuenta de un apunte ni guardándolo.
        var gasto = cambiado.Valor.Apuntes[0];
        var cambioCuenta = await motor.CambiarCuentaApuntesAsync(_ctx, [gasto.IdApunte], "4720001", _actor, "Reclasificar");
        Assert.Equal("Asientos.TrimestreCerrado", cambioCuenta.Error!.Codigo);
        Assert.Contains("4720001", cambioCuenta.Error.Mensaje);
        Assert.Equal("6290001", (await motor.ObtenerAsync(asiento.IdAsiento))!.Apuntes[0].CodigoCuenta);
        var conIva = (await motor.ObtenerAsync(asiento.IdAsiento))!;
        conIva.Apuntes[0].CodigoCuenta = "4720001";
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.GuardarManualAsync(conIva, _actor, "Reclasificar")).Error!.Codigo);

        var vigente = (await motor.ObtenerAsync(asiento.IdAsiento))!;
        Assert.True((await motor.EliminarManualAsync(vigente.IdAsiento, vigente.Version, _actor, "Sobra")).EsCorrecto);
        Assert.Equal(0, await ContarAsync("Asientos"));
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
        // El concepto también se cambia en un automático (contabilidad/diario 2026-11-14 21:50); regenerar lo sustituye.
        Assert.True((await motor.CambiarConceptoApuntesAsync([ap[0].IdApunte], "Otro", _actor, "Concepto más claro")).EsCorrecto);

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
        // El apunte de banco de un automático no se cambia de cuenta, ni otro apunte pasa a la 572 (contabilidad/diario 2026-11-14 21:50).
        var deBanco = automatico.Apuntes.FirstOrDefault(x => x.CodigoCuenta.StartsWith("572", StringComparison.Ordinal));
        if (deBanco is not null)
            Assert.Equal("Asientos.ApunteBloqueado", (await motor.CambiarCuentaApuntesAsync(_ctx, [deBanco.IdApunte], "6290001", _actor, "intento")).Error!.Codigo);
        Assert.Equal("Asientos.ApunteBloqueado",
            (await motor.CambiarCuentaApuntesAsync(_ctx, [automatico.Apuntes.First(x => !x.CodigoCuenta.StartsWith("572", StringComparison.Ordinal)).IdApunte], "5720001", _actor, "intento")).Error!.Codigo);

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

    [Fact]
    public async Task BorrarUnManualDejaSuCopiaConElMotivoYNoReutilizaElNumero()
    {
        await PrepararAsync();
        var motor = Motor();
        var a = Nuevo(10m, 10m);
        a.Apuntes[0].NumeroDocumento = "DOC-1";
        a.Apuntes[1].Descripcion = "Concepto sintético";
        var guardado = (await motor.GuardarManualAsync(a, _actor)).Valor;
        Assert.Empty(await motor.AsientosBorradosAsync([_ctx.IdSociedadEjercicio]));
        Assert.True((await motor.EliminarManualAsync(guardado.IdAsiento, guardado.Version, _actor, "Grabado dos veces")).EsCorrecto);

        var copia = Assert.Single(await motor.AsientosBorradosAsync([_ctx.IdSociedadEjercicio]));
        Assert.Equal(guardado.IdAsiento, copia.IdAsiento);
        Assert.Equal("Grabado dos veces", copia.Motivo);
        Assert.Equal(_actor.ToString(), copia.Operador);
        Assert.Equal(guardado.FechaContable, copia.Fecha);
        Assert.Equal(2, copia.Apuntes.Count);
        Assert.Equal(copia.Apuntes.Sum(x => x.Debe), copia.Apuntes.Sum(x => x.Haber));   // Debe = Haber también en la copia
        Assert.Equal(10m, copia.TotalDebe);
        Assert.Equal("DOC-1", copia.Apuntes[0].Documento);
        Assert.Equal("Concepto sintético", copia.Apuntes[1].Concepto);
        Assert.Equal(guardado.Apuntes.Select(x => x.IdApunte), copia.Apuntes.Select(x => x.IdApunte));
        Assert.Empty(await motor.AsientosBorradosAsync([9000]));

        // El número no se reutiliza: el siguiente asiento es mayor que el borrado.
        var otro = (await motor.GuardarManualAsync(Nuevo(5m, 5m), _actor)).Valor;
        Assert.True(otro.IdAsiento > guardado.IdAsiento);

        // Un borrado que falla (trimestre cerrado con IVA) no deja copia.
        await Cuentas().AltaAsync(_ctx, new AltaCuenta("4770001", "IVA repercutido sintético"));
        var conIva = new Asiento(_ctx, new DateTime(2025, 2, 1), "Con IVA");
        conIva.Apuntes.AddRange([Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m)]);
        var fiscal = (await motor.GuardarManualAsync(conIva, _actor)).Valor;
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));
        Assert.Equal("Asientos.TrimestreCerrado", (await motor.EliminarManualAsync(fiscal.IdAsiento, fiscal.Version, _actor, "Sobra")).Error!.Codigo);
        Assert.Single(await motor.AsientosBorradosAsync([_ctx.IdSociedadEjercicio]));
    }

    [Fact]
    public async Task LosDatosDelApunteSeCambianTambienEnAutomaticosYTrimestresCerrados()
    {
        await PrepararAsync();
        var motor = Motor();
        await Cuentas().AltaAsync(_ctx, new AltaCuenta("4770001", "IVA repercutido sintético"));
        var factura = new Asiento(_ctx, new DateTime(2025, 2, 1), "Factura sintética");
        factura.Apuntes.AddRange([Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m)]);
        var id = (await motor.ReemplazarLoteAsync(_ctx, _lote, [factura], _actor, "Contabilizar factura")).Valor[0];
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));
        var centro = (await new CatalogoCentrosCosteAccess(Bd).AltaAsync("T01", "C1", "Centro sintético")).Valor.IdCentroCoste;
        var antes = (await motor.ObtenerAsync(id))!;
        var apunte = antes.Apuntes[1];

        var cambio = new CambioDatosApunte
        {
            CambiaConcepto = true, Concepto = "Alquiler de febrero", CambiaDocumento = true, Documento = "F-7",
            CambiaVencimiento = true, Vencimiento = new DateTime(2025, 3, 15), CambiaCentro = true, IdCentroCoste = centro, Punteado = true,
        };
        Assert.Equal("Asientos.SinMotivo", (await motor.CambiarDatosApuntesAsync([apunte.IdApunte], cambio, _actor, " ")).Error!.Codigo);
        var r = await motor.CambiarDatosApuntesAsync([apunte.IdApunte], cambio, _actor, "Completar la ficha");
        Assert.True(r.EsCorrecto, r.Error?.ToString());

        var tras = (await motor.ObtenerAsync(id))!;
        var cambiado = tras.Apuntes[1];
        Assert.Equal(("Alquiler de febrero", "F-7", new DateTime(2025, 3, 15), centro, true),
            (cambiado.Descripcion, cambiado.NumeroDocumento, cambiado.FechaVencimiento!.Value, cambiado.IdCentroCoste!.Value, cambiado.Punteado));
        Assert.Equal(antes.Version + 1, tras.Version);
        Assert.False(tras.CorregidoAMano);            // no cambia cuentas ni importes
        Assert.Equal(tras.TotalDebe, tras.TotalHaber);
        var registro = (await motor.RegistroCambiosAsync(id)).Where(c => c.IdLote == r.Valor).ToList();
        Assert.Equal(["Descripcion", "FechaVencimiento", "IdCentroCoste", "NumeroDocumento", "Punteado"], registro.Select(c => c.Campo).Order());
        Assert.All(registro, c => Assert.Equal("Completar la ficha", c.Motivo));

        // Solo punteo: sin motivo y sin nueva versión.
        Assert.True((await motor.CambiarDatosApuntesAsync([apunte.IdApunte], new CambioDatosApunte { Punteado = false }, _actor)).EsCorrecto);
        Assert.Equal(tras.Version, (await motor.ObtenerAsync(id))!.Version);
        // Un centro que no existe no se admite.
        Assert.Equal("Asientos.CentroNoValido",
            (await motor.CambiarDatosApuntesAsync([apunte.IdApunte], new CambioDatosApunte { CambiaCentro = true, IdCentroCoste = 999999 }, _actor, "x")).Error!.Codigo);
        // Quitar el centro y el vencimiento.
        Assert.True((await motor.CambiarDatosApuntesAsync([apunte.IdApunte], new CambioDatosApunte { CambiaCentro = true, CambiaVencimiento = true }, _actor, "Quitar")).EsCorrecto);
        var sin = (await motor.ObtenerAsync(id))!.Apuntes[1];
        Assert.Null(sin.IdCentroCoste);
        Assert.Null(sin.FechaVencimiento);
    }

    [Fact]
    public async Task UnAutomaticoSeCorrigeAManoSalvoLa572YRegenerarQuitaLaMarca()
    {
        await PrepararAsync();
        var motor = Motor();
        var id = (await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "5720001", "4300001")], _actor, "Cobro")).Valor[0];
        var a = (await motor.ObtenerAsync(id))!;
        Assert.False(a.CorregidoAMano);

        // La vía manual sigue rechazando el automático (aceptación del nodo).
        Assert.Equal("Asientos.Automatico", (await motor.GuardarManualAsync(a, _actor, "intento")).Error!.Codigo);

        // Corregir la contrapartida: vale, con motivo, y el asiento sigue siendo de su lote.
        a.Apuntes[1].CodigoCuenta = "7000001";
        Assert.Equal("Asientos.SinMotivo", (await motor.CorregirAutomaticoAsync(a, _actor, " ")).Error!.Codigo);
        var corregido = await motor.CorregirAutomaticoAsync(a, _actor, "El cobro era una venta al contado");
        Assert.True(corregido.EsCorrecto, corregido.Error?.ToString());
        Assert.True(corregido.Valor.CorregidoAMano);
        Assert.Equal(_lote, corregido.Valor.Lote);
        Assert.Equal(id, corregido.Valor.IdAsiento);
        Assert.Equal(corregido.Valor.TotalDebe, corregido.Valor.TotalHaber);
        Assert.Equal(0m, (await Cuentas().ResolverAsync(_ctx, "4300001")).Valor.SaldoActual);
        Assert.Equal(-121m, (await Cuentas().ResolverAsync(_ctx, "7000001")).Valor.SaldoActual);
        Assert.Contains(await motor.RegistroCambiosAsync(id), c => c.Campo == "CuentaContable" && c.ValorNuevo == "7000001" && c.Motivo == "El cobro era una venta al contado");

        // El apunte de la 572 no se toca, ni descuadrado se guarda.
        var b = corregido.Valor;
        b.Apuntes[0].Debe = Nucleo.Importes.Importe.Redondear(100m);
        b.Apuntes[1].Haber = Nucleo.Importes.Importe.Redondear(100m);
        Assert.Equal("Asientos.BancoAutomatico", (await motor.CorregirAutomaticoAsync(b, _actor, "Importe")).Error!.Codigo);
        var c = (await motor.ObtenerAsync(id))!;
        c.Apuntes[1].Haber = Nucleo.Importes.Importe.Redondear(100m);
        Assert.Equal("Asientos.Descuadrado", (await motor.CorregirAutomaticoAsync(c, _actor, "Importe")).Error!.Codigo);
        Assert.Equal(121m, (await motor.ObtenerAsync(id))!.TotalHaber.Valor);

        // Un manual no pasa por la corrección.
        var manual = (await motor.GuardarManualAsync(Nuevo(5m, 5m), _actor)).Valor;
        Assert.Equal("Asientos.NoEsAutomatico", (await motor.CorregirAutomaticoAsync(manual, _actor, "x")).Error!.Codigo);

        // Regenerar el lote sustituye la corrección sin avisar y quita la marca.
        Assert.True((await motor.ReemplazarLoteAsync(_ctx, _lote, [Nuevo(121m, 121m, "5720001", "4300001")], _actor, "Regenerar")).EsCorrecto);
        var regenerado = (await motor.ObtenerAsync(id))!;
        Assert.False(regenerado.CorregidoAMano);
        Assert.Equal("4300001", regenerado.Apuntes[1].CodigoCuenta);

        // El cambio de cuenta de un apunte también marca el automático.
        Assert.True((await motor.CambiarCuentaApuntesAsync(_ctx, [regenerado.Apuntes[1].IdApunte], "7000001", _actor, "Reclasificar")).EsCorrecto);
        Assert.True((await motor.ObtenerAsync(id))!.CorregidoAMano);
    }

    [Fact]
    public async Task EnUnAsientoConLibroDeIvaNoSeTocanElIvaNiLosImportes()
    {
        await PrepararAsync();
        var motor = Motor();
        foreach (var (codigo, nombre) in new[] { ("4770001", "IVA repercutido sintético"), ("7000002", "Otras ventas sintéticas") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        var factura = new Asiento(_ctx, new DateTime(2025, 3, 1), "Factura sintética");
        factura.Apuntes.AddRange([Apunte.AlDebe("4300001", 121m), Apunte.AlHaber("7000001", 100m), Apunte.AlHaber("4770001", 21m)]);
        var id = (await motor.ReemplazarLoteAsync(_ctx, _lote, [factura], _actor, "Contabilizar factura")).Valor[0];
        Assert.False(await motor.TieneLibroIvaAsync(id));
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO PeriodosIVA (IdSociedadEjercicio, Numero, FechaInicio, FechaFin, Estado) VALUES (?, ?, ?, ?, ?)",
                9001, (byte)1, new DateTime(2025, 1, 1), new DateTime(2025, 3, 31), "ABIERTO");
            var periodo = await s.UltimoIdAsync();
            var tipo = await s.EscalarAsync("SELECT MIN(IdTipoOperacion) FROM TiposOperacion");
            Assert.False(tipo is null or DBNull, "La maestra no tiene tipos de operación.");
            return await s.EjecutarAsync(
                "INSERT INTO IVARepercutido (IdSociedadEjercicio, IdAsiento, FechaExpedicion, FechaOperacion, FechaHoraRegistroContable, IdPeriodoIVA, NumeroFactura, IdTipoOperacion, " +
                "ImporteTotalFactura, BaseImponibleTotal, CuotaRepercutidaTotal, RecargoEquivalenciaTotal, IncluidoModelo347, Estado, FechaHoraCreacion) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                9001, id, new DateTime(2025, 3, 1), new DateTime(2025, 3, 1), new DateTime(2025, 3, 1), periodo, "F-SINT-1", Convert.ToInt32(tipo), 121m, 100m, 21m, 0m, true, "REGISTRADO", new DateTime(2025, 3, 1));
        });
        Assert.True(await motor.TieneLibroIvaAsync(id));

        // La subcuenta de ingreso sí; el importe y la línea de IVA, no.
        var a = (await motor.ObtenerAsync(id))!;
        a.Apuntes[1].CodigoCuenta = "7000002";
        var ok = await motor.CorregirAutomaticoAsync(a, _actor, "Ingreso en otra subcuenta");
        Assert.True(ok.EsCorrecto, ok.Error?.ToString());
        var b = ok.Valor;
        b.Apuntes[0].Debe = Nucleo.Importes.Importe.Redondear(242m);
        b.Apuntes[1].Haber = Nucleo.Importes.Importe.Redondear(221m);
        Assert.Equal("Asientos.FacturaConLibroIva", (await motor.CorregirAutomaticoAsync(b, _actor, "Importe")).Error!.Codigo);
        var iva = (await motor.ObtenerAsync(id))!.Apuntes[2];
        Assert.Equal("Asientos.ApunteBloqueado", (await motor.CambiarCuentaApuntesAsync(_ctx, [iva.IdApunte], "7000002", _actor, "IVA")).Error!.Codigo);
        var tras = (await motor.ObtenerAsync(id))!;
        Assert.Equal(("7000002", 121m, 121m), (tras.Apuntes[1].CodigoCuenta, tras.TotalDebe.Valor, tras.TotalHaber.Valor));
    }
}
