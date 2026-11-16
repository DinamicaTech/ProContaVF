using ProConta.Norma43.Lineas;
using ProConta.Norma43.Remesas;

namespace ProConta.Norma43.Tests;

/// <summary>Reglas de la explosión de remesas y devoluciones (norma43/remesas), sin BD.</summary>
public sealed class RemesasN43Tests
{
    private static LineaN43 Abono(DateTime fecha, decimal importe) => new(1, 1, 1, "Banco", "0049 1500 0012345678", "0049", fecha, fecha, fecha.Year, false,
        "H", importe, "02", "REMESA RECIBOS", null, null, null, "5720001", "Banco", [], null, false, null, Especial: 5);

    private static FacturaRemesa F(int id, decimal importe, int? dia, bool sepa = true, int mes = 9) =>
        new(id, $"S{id:0000}", new DateTime(2026, mes, 1), importe, dia, sepa, id, $"Cliente {id}", $"Alquiler {id}");

    [Fact]
    public void Propone_las_SEPA_del_mes_y_marca_las_de_dia_de_cargo_cercano()
    {
        // Como la remesa del ERP del 4/9: las de día 1 y día 5 (a menos de 3 días del 4) cuadran; la de día 20 y la de transferencia no.
        var linea = Abono(new DateTime(2026, 9, 4), 1300m);
        var p = ReglasRemesasN43.ProponerRemesa(linea, [F(1, 500m, 1), F(2, 800m, 5), F(3, 900m, 20), F(4, 700m, 1, sepa: false), F(5, 400m, 4, mes: 8)]);
        Assert.Equal([1, 2, 3], p.Select(c => c.Factura.Id));
        Assert.Equal([true, true, false], p.Select(c => c.Marcada));
    }

    [Fact]
    public void Si_las_cercanas_no_cuadran_y_todas_si_vienen_todas_marcadas()
    {
        // Una sola remesa al mes: si lleva todas las del mes, vienen todas marcadas.
        var p = ReglasRemesasN43.ProponerRemesa(Abono(new DateTime(2026, 9, 4), 2200m), [F(1, 500m, 1), F(2, 800m, 5), F(3, 900m, 20)]);
        Assert.All(p, c => Assert.True(c.Marcada));
        // Si no cuadran ni unas ni otras, solo las cercanas.
        var q = ReglasRemesasN43.ProponerRemesa(Abono(new DateTime(2026, 9, 4), 2000m), [F(1, 500m, 1), F(2, 800m, 5), F(3, 900m, 20)]);
        Assert.Equal([true, true, false], q.Select(c => c.Marcada));
    }

    [Fact]
    public void La_combinacion_de_una_devolucion_es_la_de_menos_recibos_que_suma_exacto()
    {
        Assert.Equal(new HashSet<int> { 2 }, ReglasRemesasN43.Combinacion(900m, [500m, 400m, 900m]));
        Assert.Equal(new HashSet<int> { 0, 3 }, ReglasRemesasN43.Combinacion(1250.50m, [500m, 400m, 900m, 750.50m]));
        Assert.Empty(ReglasRemesasN43.Combinacion(123.45m, [500m, 400m]));
        Assert.Empty(ReglasRemesasN43.Combinacion(0m, [500m]));
        // Cada recibo cuenta una vez: 1.000 no es 500 + 500 si solo hay uno de 500.
        Assert.Empty(ReglasRemesasN43.Combinacion(1000m, [500m, 400m]));
        Assert.Equal(new HashSet<int> { 0, 1 }, ReglasRemesasN43.Combinacion(1000m, [500m, 500m, 400m]));
    }

    [Fact]
    public void La_devolucion_marca_su_combinacion()
    {
        var recibos = new[] { 500m, 800m, 900m }.Select((i, n) => new ReciboCobrado(10 + n, new DateTime(2026, 9, 4), i, F(n + 1, i, 1))).ToList();
        var p = ReglasRemesasN43.ProponerDevolucion(1400m, recibos);
        Assert.Equal([true, false, true], p.Select(c => c.Marcada));
    }

    [Fact]
    public void El_cuadre_explica_la_diferencia()
    {
        Assert.Equal("2 recibo(s) por 1.300,00 € frente a 1.300,00 € del banco: cuadra.", ReglasRemesasN43.TextoCuadre(1300m, [500m, 800m]));
        Assert.EndsWith("diferencia de -10,00 €.", ReglasRemesasN43.TextoCuadre(1310m, [500m, 800m]));
    }

    [Fact]
    public void Los_recibos_tienen_que_cuadrar_al_centimo_sin_repetir_factura()
    {
        var l = Abono(new DateTime(2026, 9, 4), 1300m);
        Assert.Null(ServicioRemesasN43.ErrorRecibos(l, [(1, 500m), (2, 800m)]));
        Assert.Equal("Remesas.NoCuadra", ServicioRemesasN43.ErrorRecibos(l, [(1, 500m), (2, 799.99m)])!.Codigo);
        Assert.Equal("Remesas.FacturaRepetida", ServicioRemesasN43.ErrorRecibos(l, [(1, 500m), (1, 800m)])!.Codigo);
        Assert.Equal("Remesas.ImporteNoValido", ServicioRemesasN43.ErrorRecibos(l, [(1, 1300.001m)])!.Codigo);
        Assert.Equal("Remesas.SinRecibos", ServicioRemesasN43.ErrorRecibos(l, [])!.Codigo);
        // Un importe cobrado distinto del de la factura vale si la suma cuadra (la diferencia queda en la 430).
        Assert.Null(ServicioRemesasN43.ErrorRecibos(l, [(1, 510m), (2, 790m)]));
    }

    [Fact]
    public void El_menu_ofrece_cada_accion_de_remesas_sobre_una_sola_linea_que_la_admite()
    {
        var remesa = Abono(new DateTime(2026, 9, 4), 1300m);
        var a = AccionesLineasN43.Evaluar([remesa]);
        Assert.Null(a.ExplotarRemesa);
        Assert.NotNull(a.ConciliarDevueltas);
        Assert.NotNull(a.DeshacerExplosion);
        var explotada = AccionesLineasN43.Evaluar([remesa with { IdExtractoDesglose = 9 }]);
        Assert.NotNull(explotada.ExplotarRemesa);
        Assert.Null(explotada.DeshacerExplosion);
        var devolucion = AccionesLineasN43.Evaluar([remesa with { Sentido = "D" }]);
        Assert.Null(devolucion.ConciliarDevueltas);
        Assert.NotNull(devolucion.ExplotarRemesa);
        var conciliada = remesa with { Sentido = "D", Destinos = [new DestinoLineaN43(1, "4300001", "Cliente", 50m, ServicioRemesasN43.OrigenDevolucion, null, null)] };
        Assert.Null(AccionesLineasN43.Evaluar([conciliada]).DeshacerExplosion);
        Assert.NotNull(AccionesLineasN43.Evaluar([conciliada]).ConciliarDevueltas);
        Assert.Contains("una sola", AccionesLineasN43.Evaluar([remesa, remesa with { Id = 2 }]).ExplotarRemesa);
        Assert.Contains("cerrado", AccionesLineasN43.Evaluar([remesa with { EjercicioCerrado = true }]).ExplotarRemesa);
    }

    [Fact]
    public void Los_bloqueos_dicen_que_hacer()
    {
        var remesa = Abono(new DateTime(2026, 9, 4), 1300m);
        Assert.Null(ServicioRemesasN43.BloqueoRemesa(remesa));
        Assert.Equal("Remesas.YaExplotada", ServicioRemesasN43.BloqueoRemesa(remesa with { IdExtractoDesglose = 7 })!.Codigo);
        Assert.Equal("Remesas.EsRecibo", ServicioRemesasN43.BloqueoRemesa(remesa with { IdLineaOrigen = 3 })!.Codigo);
        Assert.Equal("Remesas.NoEsAbono", ServicioRemesasN43.BloqueoRemesa(remesa with { Sentido = "D" })!.Codigo);
        Assert.Equal("Remesas.NoEsCargo", ServicioRemesasN43.BloqueoDevolucion(remesa)!.Codigo);
        Assert.Null(ServicioRemesasN43.BloqueoDevolucion(remesa with { Sentido = "D" }));
    }
}
