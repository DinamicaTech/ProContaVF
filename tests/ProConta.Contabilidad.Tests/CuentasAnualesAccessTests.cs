using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cierre;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.CuentasAnuales;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cuentas anuales contra Access (contabilidad/procesos/cuentas-anuales 2026-11-23): las cifras salen de los apuntes contabilizados del
/// ejercicio y del anterior (cerrado con el proceso de cierre, que copia las cuentas y hace la apertura), cuadran (Debe = Haber → activo =
/// patrimonio neto y pasivo), el resultado del anterior sin aplicar se explica, y los textos de la memoria se guardan por ejercicio y apartado;
/// al borrarlos vuelve el propuesto. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class CuentasAnualesAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _anterior = new("T01", 9001, 2026);
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9002, 2027);
    private readonly Actor _actor = new("operador.prueba");

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas() => new(Bd);

    private static Asiento A(ContextoSociedadEjercicio c, DateTime f, string d, params Apunte[] apuntes)
    {
        var a = new Asiento(c, f, d);
        a.Apuntes.AddRange(apuntes);
        return a;
    }

    /// <summary>2026: aportación de 3.000 y un servicio de 1.000 a crédito; se cierra. 2027: servicio de 2.500 a crédito y un gasto de 400 pagado.</summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2027);
        foreach (var (codigo, nombre) in new[] { ("100", "Capital"), ("129", "Resultado"), ("430", "Clientes"), ("572", "Bancos"), ("629", "Otros servicios"), ("705", "Prestaciones") })
            Assert.True((await Cuentas().AltaAsync(_anterior, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital social"), ("4300001", "Cliente sintético"), ("5720001", "Banco sintético"),
                     ("6290000", "Otros servicios"), ("7050000", "Prestaciones de servicios") })
            Assert.True((await Cuentas().AltaSubcuentaAsync(_anterior, codigo, nombre)).EsCorrecto);
        var motor = new MotorAsientosAccess(Bd);
        var r = await motor.GuardarManualesAsync([
            A(_anterior, new DateTime(2026, 1, 2), "Aportación", Apunte.AlDebe("5720001", 3000m), Apunte.AlHaber("1000000", 3000m)),
            A(_anterior, new DateTime(2026, 6, 1), "Servicio", Apunte.AlDebe("4300001", 1000m), Apunte.AlHaber("7050000", 1000m)),
        ], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);

        var cierre = new ServicioCierreEjercicio(Bd, motor, Cuentas(), new ServicioNuevoEjercicio(Bd), null, new RelojFijo(new DateTime(2027, 1, 10)),
            _ => Task.FromResult<IReadOnlyList<string>>([]));
        var p = (await cierre.ProponerAsync(_anterior)).Valor;
        Assert.True(p.SePuedeCerrar, string.Join(" ", p.Bloqueos));
        var c = await cierre.ConfirmarAsync(_anterior, "Prueba de cuentas anuales", _actor, p);
        Assert.True(c.EsCorrecto, c.Error?.Mensaje);

        r = await motor.GuardarManualesAsync([
            A(_ctx, new DateTime(2027, 3, 1), "Servicio", Apunte.AlDebe("4300001", 2500m), Apunte.AlHaber("7050000", 2500m)),
            A(_ctx, new DateTime(2027, 4, 1), "Gasto", Apunte.AlDebe("6290000", 400m), Apunte.AlHaber("5720001", 400m)),
        ], _actor, "Datos sintéticos");
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
    }

    [Fact]
    public async Task Las_cifras_del_ejercicio_cerrado_salen_de_los_apuntes_y_cuadran()
    {
        await PrepararAsync();
        var s = new ServicioCuentasAnuales(Bd);
        var c = await s.GenerarAsync(_anterior);
        Assert.Equal("Sociedad sintética", c.RazonSocial);
        Assert.True(c.Actual.Regularizado);
        Assert.Equal(1000m, c.Actual.Resultado);
        Assert.Equal(1000m, c.Actual["21700"]);
        Assert.Equal(3000m, c.Actual["12700"]);
        Assert.Equal(1000m, c.Actual["12382"]);
        Assert.Equal(4000m, c.Actual.TotalActivo);
        Assert.Equal(c.Actual.TotalActivo, c.Actual.TotalPatrimonioNetoYPasivo);
        Assert.Null(c.Anterior);
        Assert.False(c.TieneErrores, string.Join(" | ", c.Comprobaciones.Where(x => x.Gravedad == GravedadComprobacion.Error).Select(x => x.Titulo)));
        Assert.DoesNotContain(c.Comprobaciones, x => x.Titulo.Contains("no está cerrado"));

        var ejercicios = await s.EjerciciosAsync("T01");
        Assert.Equal([2027, 2026], ejercicios.Select(e => e.Ejercicio));
        Assert.True(ejercicios[1].Cerrado);
        Assert.Equal(2026, ServicioCuentasAnuales.EjercicioPropuesto(ejercicios, 2027));
    }

    [Fact]
    public async Task El_resultado_anterior_sin_aplicar_es_un_error_que_dice_donde_aplicarlo()
    {
        await PrepararAsync();
        var c = await new ServicioCuentasAnuales(Bd).GenerarAsync(_ctx);
        // 2027 sin regularizar: su resultado sale de las 6 y 7 (2.100) y la 129 de la apertura (1.000 de 2026) sigue en «VII. Resultado del ejercicio».
        Assert.Equal(2100m, c.Actual.Resultado);
        Assert.Equal(3100m, c.Actual["21700"]);
        Assert.Equal(6100m, c.Actual.TotalActivo);
        Assert.Equal(c.Actual.TotalActivo, c.Actual.TotalPatrimonioNetoYPasivo);
        Assert.Equal(1000m, c.Anterior!.Resultado);
        var error = Assert.Single(c.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Error);
        Assert.Contains("Aplicación del resultado", error.QueHacer);
    }

    [Fact]
    public async Task El_texto_de_la_memoria_se_guarda_por_ejercicio_y_al_borrarlo_vuelve_el_propuesto()
    {
        await PrepararAsync();
        var s = new ServicioCuentasAnuales(Bd);
        var c = await s.GenerarAsync(_anterior);
        var m = await s.MemoriaAsync("T01", 9001, c);
        Assert.Equal(MemoriaPymes.Apartados.Count, m.Count);
        Assert.All(m, a => Assert.False(a.Guardado));

        await s.GuardarTextoMemoriaAsync(9001, "1", "La Sociedad se dedica al alquiler sintético.", "operador.prueba");
        await s.GuardarTextoMemoriaAsync(9001, "1", "La Sociedad se dedica a servicios sintéticos.", "operador.prueba");
        m = await s.MemoriaAsync("T01", 9001, c);
        Assert.True(m[0].Guardado);
        Assert.Equal("La Sociedad se dedica a servicios sintéticos.", m[0].Texto);
        Assert.False((await s.MemoriaAsync("T01", 9002, await s.GenerarAsync(_ctx)))[0].Guardado);

        await s.GuardarTextoMemoriaAsync(9001, "1", "  ", "operador.prueba");
        m = await s.MemoriaAsync("T01", 9001, c);
        Assert.False(m[0].Guardado);
        Assert.Equal(m[0].TextoPropuesto, m[0].Texto);
    }
}
