using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Validación V6 (contabilidad/modelos-tributarios, «Derived from contabilidad» del 2026-11-15 21:10, por las incidencias con la
/// gestoría): cada línea de IVA llega una sola vez al 303, sin omisiones ni dobles (una línea no puede estar a la vez en
/// importación y en régimen general), y la prorrata o la no deducción de una actividad no reduce el IVA devengado autorrepercutido.
/// </summary>
public sealed class Iva303CasillasTests
{
    private const SentidoIva R = SentidoIva.Repercutido, S = SentidoIva.Soportado;
    private const OperacionIva Nac = OperacionIva.Nacional, Intra = OperacionIva.Intracomunitaria, Imp = OperacionIva.Importacion;

    private static LineaIva L(int trim, SentidoIva sentido, DeduccionIva ded, OperacionIva op, bool isp, decimal tipo, decimal b, decimal cuota) =>
        new(new DateTime(2026, trim * 3, 1), trim, 2000 + trim, sentido, op, isp, tipo, "Raíz", "Centro", "Actividad", ded, "Tercero sintético", null, null, b, cuota,
            sentido == SentidoIva.Soportado && (op != OperacionIva.Nacional || isp) ? cuota : 0m, null, null);

    /// <summary>Todas las combinaciones de operación, ISP y deducción en un trimestre, con cuotas distintas para que un doble se note.</summary>
    private static IReadOnlyList<LineaIva> Combinaciones()
    {
        var l = new List<LineaIva> { L(1, R, DeduccionIva.Deducible100, Nac, false, 21, 1000m, 210m), L(1, R, DeduccionIva.NoDeducible, Nac, false, 10, 500m, 50m),
            L(1, R, DeduccionIva.NoDeducible, Nac, false, 0, 800m, 0m) };
        var cuota = 1m;
        foreach (var op in new[] { Nac, Intra, Imp })
        foreach (var isp in new[] { false, true })
        foreach (var ded in Enum.GetValues<DeduccionIva>())
        {
            cuota += 1.37m;
            l.Add(L(1, S, ded, op, isp, 21, Math.Round(cuota / 0.21m, 2), cuota));
        }
        return l;
    }

    [Fact]
    public void CadaLineaDevengadaLlegaUnaSolaVezAlDevengado()
    {
        var lineas = Combinaciones();
        var r = Calculo303.Calcular(lineas, 1m);
        // Se devenga el repercutido y lo autorrepercutido (intracomunitarias e ISP); la importación con DUA no.
        var esperado = lineas.Where(Calculo303.EsRepercutido).Sum(x => x.Cuota) + lineas.Where(Calculo303.EsAutorrepercutida).Sum(x => x.Cuota);
        Assert.DoesNotContain(lineas, x => Calculo303.EsImportacion(x) && Calculo303.EsAutorrepercutida(x));
        Assert.Equal(esperado, r.TotalDevengado(1));
        // Ninguna línea de soportado está a la vez en dos casillas de devengado (importación, ISP e intracomunitarias se excluyen entre sí).
        foreach (var x in lineas.Where(Calculo303.EsSoportado))
            Assert.True(new[] { Calculo303.EsIntracomunitaria(x), Calculo303.EsIspNoIntracomunitaria(x), Calculo303.EsImportacion(x) }.Count(b => b) <= 1, $"{x.Operacion} ISP={x.Isp}");
    }

    [Fact]
    public void CadaLineaSoportadaLlegaUnaSolaVezAlDeducibleOAlInformativo()
    {
        var lineas = Combinaciones();
        // Con prorrata del 100 %, la parte en prorrata se deduce entera: el deducible más lo no deducible y lo sin clasificar es todo el soportado.
        var r = Calculo303.Calcular(lineas, 1m);
        var soportado = lineas.Where(Calculo303.EsSoportado).Sum(x => x.Cuota);
        var fuera = r.Linea(Calculo303.ClaveNoDeducible).En(1).Cuota + r.Linea(Calculo303.ClaveSinClasificar).En(1).Cuota;
        Assert.Equal(soportado, r.TotalDeducible(1) + fuera);
    }

    [Theory]
    [InlineData(DeduccionIva.NoDeducible)]
    [InlineData(DeduccionIva.Prorrata)]
    [InlineData(DeduccionIva.SinCentro)]
    public void LaDeduccionDeLaActividadNoReduceElIvaAutorrepercutidoDevengado(DeduccionIva deduccion)
    {
        var lineas = new[] { L(2, S, deduccion, Nac, true, 21, 1000m, 210m), L(2, S, deduccion, Intra, false, 21, 500m, 105m), L(2, S, deduccion, Imp, false, 21, 100m, 21m) };
        var r = Calculo303.Calcular(lineas, 0.15m);
        Assert.Equal(210m, r.Linea(Calculo303.ClaveIsp).En(2).Cuota);
        Assert.Equal(105m, r.Linea(Calculo303.ClaveIntracomunitarias).En(2).Cuota);
        // La importación con DUA no se devenga (el IVA se pagó en la aduana): solo se deduce.
        Assert.Equal(315m, r.TotalDevengado(2));
        // Lo que limita la actividad es solo la deducción.
        Assert.True(r.TotalDeducible(2) < 336m);
    }
}
