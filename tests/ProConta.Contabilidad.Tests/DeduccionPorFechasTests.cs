using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cambio de deducción de una actividad desde una fecha (contabilidad/procesos/cambio-deduccion-actividad), sin BD. Caso que lo
/// origina: el alquiler turístico, no deducible hasta el 30/11/2026 y 100 % deducible desde el 01/12/2026; cada factura se
/// clasifica por su fecha de expedición, no por la del asiento. Datos sintéticos.
/// </summary>
public sealed class DeduccionPorFechasTests
{
    private static readonly DateTime Corte = new(2026, 12, 1);

    private static readonly ActividadIva Turistico = new("ALQTUR", "Alquiler turístico", 0, "4728", [new CambioDeduccion(Corte, 2)]);

    private static readonly IReadOnlyDictionary<int, ActividadIva> Actividades = new Dictionary<int, ActividadIva>
    {
        [1] = new("GEN", "General", 1, null), [2] = Turistico,
    };

    private static readonly IReadOnlyDictionary<int, CentroIva> Centros = new Dictionary<int, CentroIva>
    {
        [1] = new("EMPRESA", "Suministros"), [2] = new("INMUEBLES", "Piso turístico sintético"),
    };

    private const SentidoIva R = SentidoIva.Repercutido, S = SentidoIva.Soportado;

    /// <summary>Un registro con un desglose y su apunte de IVA en el centro dado.</summary>
    private static RegistroIvaLeido Reg(SentidoIva s, DateTime contable, DateTime? factura, int centro, decimal baseImponible, decimal tipo)
    {
        var cuota = Math.Round(baseImponible * tipo / 100m, 2, MidpointRounding.AwayFromZero);
        var cuenta = s == S ? "4720021" : "4770021";
        ApunteIvaLeido[] apuntes = cuota == 0m
            ? [new ApunteIvaLeido(s == S ? "6220000" : "7520000", s == S ? baseImponible : 0m, s == S ? 0m : baseImponible, centro)]
            : [new ApunteIvaLeido(cuenta, s == S ? cuota : 0m, s == S ? 0m : cuota, centro)];
        return new RegistroIvaLeido(s, 1, contable, s == S ? "SOPORTADO_0" : null, false, "Tercero sintético", "B00000000", "F-1",
            [new DesgloseIvaLeido(baseImponible, tipo, cuota, 0m)], apuntes, factura);
    }

    [Theory]
    [InlineData(2026, 11, 30, 0)]
    [InlineData(2026, 12, 1, 2)]
    [InlineData(2026, 12, 31, 2)]
    [InlineData(2027, 3, 1, 2)]
    public void LaDeduccionEsLaVigenteEnLaFecha(int a, int m, int d, byte esperada) =>
        Assert.Equal(esperada, Turistico.DeducibleEn(new DateTime(a, m, d, 18, 30, 0)));

    [Fact]
    public void ConVariosCambiosGanaElUltimoAnteriorOIgualYSinCambiosLaDeLaFicha()
    {
        IReadOnlyList<CambioDeduccion> cambios = [new(new DateTime(2026, 6, 1), 1), new(new DateTime(2026, 12, 1), 2)];
        Assert.Equal(0, CambioDeduccion.Vigente(0, cambios, new DateTime(2026, 5, 31)));
        Assert.Equal(1, CambioDeduccion.Vigente(0, cambios, new DateTime(2026, 6, 1)));
        Assert.Equal(1, CambioDeduccion.Vigente(0, cambios, new DateTime(2026, 11, 30)));
        Assert.Equal(2, CambioDeduccion.Vigente(0, cambios, new DateTime(2026, 12, 1)));
        Assert.Equal(1, new ActividadIva("GEN", "General", 1, null).DeducibleEn(new DateTime(2026, 12, 1)));
    }

    [Fact]
    public void ElCalendarioSeLeeComoLoDiceElNodo()
    {
        Assert.Equal("No deducible hasta el 30/11/2026 · 100 % deducible desde el 01/12/2026", LectorActividadesIva.Calendario(0, Turistico.Cambios!));
        Assert.Equal("Por prorrata siempre.", LectorActividadesIva.Calendario(1, []));
    }

    [Fact]
    public void CadaLineaSeClasificaPorLaFechaDeLaFacturaNoPorLaDelAsiento()
    {
        var noviembreTarde = Reg(S, new DateTime(2026, 12, 3), new DateTime(2026, 11, 28), 2, 100m, 21m);
        var diciembre = Reg(S, new DateTime(2026, 12, 5), new DateTime(2026, 12, 1), 2, 200m, 21m);
        var sinFechaFactura = Reg(S, new DateTime(2026, 12, 6), null, 2, 300m, 21m);
        var l = ClasificadorLineasIva.Clasificar([noviembreTarde, diciembre, sinFechaFactura], 2026, Actividades, Centros);
        Assert.Equal(DeduccionIva.NoDeducible, l.Single(x => x.Base == 100m).Deduccion);
        Assert.Equal(DeduccionIva.Deducible100, l.Single(x => x.Base == 200m).Deduccion);
        // Sin fecha de factura en el libro, la contable.
        Assert.Equal(DeduccionIva.Deducible100, l.Single(x => x.Base == 300m).Deduccion);
        // El trimestre sigue siendo el de la fecha contable.
        Assert.All(l, x => Assert.Equal(4, x.Trimestre));
    }

    /// <summary>Criterio de aceptación del nodo con el 4T de 2026.</summary>
    [Fact]
    public void El303Del4TDeduceDiciembreDelTuristicoYNadaDeOctubreYNoviembre()
    {
        RegistroIvaLeido[] registros =
        [
            // Compras del turístico: octubre y noviembre no deducibles, diciembre al 100 %.
            Reg(S, new DateTime(2026, 10, 10), new DateTime(2026, 10, 10), 2, 1000m, 21m),
            Reg(S, new DateTime(2026, 11, 10), new DateTime(2026, 11, 10), 2, 500m, 21m),
            Reg(S, new DateTime(2026, 12, 10), new DateTime(2026, 12, 10), 2, 400m, 21m),
            // Compra común: sigue en prorrata.
            Reg(S, new DateTime(2026, 12, 15), new DateTime(2026, 12, 15), 1, 100m, 21m),
            // Ventas del turístico: exentas hasta noviembre, al 10 % desde diciembre.
            Reg(R, new DateTime(2026, 10, 31), new DateTime(2026, 10, 31), 2, 3000m, 0m),
            Reg(R, new DateTime(2026, 12, 31), new DateTime(2026, 12, 31), 2, 2000m, 10m),
        ];
        var lineas = ClasificadorLineasIva.Clasificar(registros, 2026, Actividades, Centros);
        var r = Calculo303.Calcular(lineas, 0.15m);

        Assert.Equal(84m, r.Linea(Calculo303.ClaveInteriores100).En(4).Cuota);              // 400 × 21 % de diciembre, entera
        Assert.Equal(315m, r.Linea(Calculo303.ClaveNoDeducible).En(4).Cuota);               // 210 + 105 de octubre y noviembre
        Assert.Equal(3.15m, r.Linea(Calculo303.ClaveInterioresProrrata).En(4).Cuota);       // común: 21 × 15 %
        Assert.Equal(84m + 3.15m, r.TotalDeducible(4));
        Assert.Equal(200m, r.Linea(Calculo303.ClaveRepercutido(10m)).En(4).Cuota);
        Assert.Equal(200m - 87.15m, r.Resultado(4));

        // Prorrata real: la base de diciembre del turístico tiene derecho a deducción; la de octubre no.
        var p = CalculoProrrata.Calcular(lineas, 0.15m, r);
        Assert.Equal(2000m, p.Numerador);
        Assert.Equal(5000m, p.Denominador);
        Assert.Equal(0.40m, p.Real);
        // El turístico sale en dos filas del volumen: con derecho (diciembre) y sin él (octubre).
        Assert.Equal(2, p.Volumen.Count(v => v.Actividad == "Alquiler turístico"));
        Assert.Equal(DeduccionIva.Deducible100, p.Volumen[0].Deduccion);
        Assert.Equal(21m, p.CuotaSujeta[3]);
    }
}
