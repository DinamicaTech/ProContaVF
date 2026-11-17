using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Iva;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Prorrata definitiva contra Access (contabilidad/procesos/prorrata): se guarda en el ejercicio (en porcentaje) y se copia como
/// provisional del siguiente; repetirla o hacerla antes de acabar el año pide motivo; deshacer la quita; y la liquidación del 4T no
/// deja pasar una 4729 que no coincide con la regularización del 303. Sin libros de IVA (la regularización es cero, sin asiento: el
/// asiento lo cubren las pruebas sin BD). Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class RegularizacionProrrataAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private static readonly DateTime Enero = new(2027, 1, 10);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas() => new(Bd);
    private ServicioRegularizacionProrrata Servicio(DateTime hoy) => new(Bd, new MotorAsientosAccess(Bd), Cuentas(), null, new RelojFijo(hoy));

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2027);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 4, [PorcentajeProrrataAñoAnterior] = 15 WHERE IdSociedadEjercicio = 9001"));
    }

    private Task<object?> LeerAsync(string sql, params object?[] p) => Bd.EjecutarAsync(s => s.EscalarAsync(sql, p));

    [Fact]
    public async Task ConfirmarGuardaLaDefinitivaYLaCopiaComoProvisionalDelSiguiente()
    {
        await PrepararAsync();
        var servicio = Servicio(Enero);
        Assert.Equal(EstadoRegularizacionProrrata.Pendiente, (await servicio.SituacionAsync(_ctx)).Estado);
        var p = (await servicio.ProponerAsync(_ctx, 0.20m)).Valor;
        Assert.Equal(0.20m, p.Definitiva);
        Assert.Empty(p.Lineas);

        var r = await servicio.ConfirmarAsync(_ctx, 0.20m, null, _actor, p);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.Null(r.Valor.IdAsiento);
        Assert.Equal(2027, r.Valor.EjercicioSiguiente);
        Assert.Equal(20m, Convert.ToDecimal(await LeerAsync("SELECT PorcentajeProrrataDefinitiva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9001")));
        Assert.Equal(20m, Convert.ToDecimal(await LeerAsync("SELECT [PorcentajeProrrataAñoAnterior] FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9002")));
        Assert.Equal(0.20m, await new LectorLineasIva(Bd).ProrrataDefinitivaAsync(_ctx));
        var situacion = await servicio.SituacionAsync(_ctx);
        Assert.Equal((EstadoRegularizacionProrrata.Regularizada, 0.20m, (int?)null), (situacion.Estado, situacion.Definitiva, situacion.IdAsiento));
        // Sin la definitiva en la propuesta, se usa la guardada.
        Assert.Equal(0.20m, (await servicio.ProponerAsync(_ctx)).Valor.Definitiva);
    }

    [Fact]
    public async Task RepetirOHacerlaAntesDeAcabarElAnoPideMotivoYDeshacerQuitaLaDefinitiva()
    {
        await PrepararAsync();
        var noviembre = Servicio(new DateTime(2026, 11, 20));
        Assert.Equal("Prorrata.SinMotivo", (await noviembre.ConfirmarAsync(_ctx, 0.20m, null, _actor)).Error!.Codigo);
        Assert.True((await noviembre.ConfirmarAsync(_ctx, 0.20m, "Prueba antes de acabar el año", _actor)).EsCorrecto);
        var servicio = Servicio(Enero);
        Assert.Equal("Prorrata.SinMotivo", (await servicio.ConfirmarAsync(_ctx, 0.18m, " ", _actor)).Error!.Codigo);
        Assert.True((await servicio.ConfirmarAsync(_ctx, 0.18m, "La que da la gestoría", _actor)).EsCorrecto);
        Assert.Equal(18m, Convert.ToDecimal(await LeerAsync("SELECT PorcentajeProrrataDefinitiva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9001")));
        Assert.Equal("Prorrata.Porcentaje", (await servicio.ProponerAsync(_ctx, 1.5m)).Error!.Codigo);

        var d = await servicio.DeshacerAsync(_ctx, "Prueba de deshacer", _actor);
        Assert.True(d.EsCorrecto, d.Error?.Mensaje);
        Assert.Null(await LeerAsync("SELECT PorcentajeProrrataDefinitiva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9001"));
        Assert.Equal(EstadoRegularizacionProrrata.Pendiente, (await servicio.SituacionAsync(_ctx)).Estado);
        // La provisional del ejercicio siguiente no se toca al deshacer.
        Assert.Equal(18m, Convert.ToDecimal(await LeerAsync("SELECT [PorcentajeProrrataAñoAnterior] FROM SociedadesEjercicios WHERE IdSociedadEjercicio = 9002")));
    }

    [Fact]
    public async Task LaLiquidacionDelCuartoTrimestrePideRegularizarSiLaCuatroSieteDosNueveNoCuadra()
    {
        await PrepararAsync();
        foreach (var (codigo, nombre) in new[] { ("4729001", "Regularización de la prorrata"), ("5720001", "Banco sintético") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        var a = new Asiento(_ctx, new DateTime(2026, 12, 31), "Regularización hecha a mano");
        a.Apuntes.AddRange([Apunte.AlDebe("4729001", 10m), Apunte.AlHaber("5720001", 10m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([a], _actor, "Datos sintéticos")).EsCorrecto);
        var liquidacion = new ServicioLiquidacionIva(Bd, new MotorAsientosAccess(Bd), Cuentas(), null, new RelojFijo(Enero));
        Assert.Equal("LiquidacionIva.ProrrataSinRegularizar", (await liquidacion.ProponerAsync(_ctx, 4)).Error!.Codigo);
        // En los tres primeros trimestres la 4729 no cuenta.
        Assert.True((await liquidacion.ProponerAsync(_ctx, 3)).EsCorrecto);
    }
}
