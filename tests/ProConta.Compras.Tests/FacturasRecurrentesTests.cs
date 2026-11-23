using ProConta.Compras.Recurrentes;
using ProConta.Terceros.Roles;

namespace ProConta.Compras.Tests;

/// <summary>
/// Detector de facturas recurrentes pendientes (compras/facturas-recurrentes-pendientes 2026-11-23 18:55), sin BD: periodicidad deducida desde la
/// factura más reciente con 3 seguidas, la fijada en la ficha, «No controlar», descartes, varias en un mes, exclusiones y cambio de año.
/// </summary>
public sealed class FacturasRecurrentesTests
{
    private static readonly DateTime Hoy = new(2026, 11, 23);
    private static readonly IReadOnlySet<(int, DateTime)> SinDescartes = new HashSet<(int, DateTime)>();
    private int _registro = 1;

    private FacturaParaRecurrencia F(int proveedor, int anio, int mes, int dia, decimal total = 100m, bool duplicada = false, bool rectificativa = false) =>
        new(_registro++, proveedor, new DateTime(anio, mes, dia), total, duplicada, rectificativa);

    private static Dictionary<int, ProveedorParaRecurrencia> Proveedores(params (int Id, PeriodicidadFacturas? P)[] p) =>
        p.ToDictionary(x => x.Id, x => new ProveedorParaRecurrencia(x.Id, 1000 + x.Id, $"Proveedor {x.Id}", x.P));

    private static IReadOnlyList<FacturaRecurrentePendiente> Detectar(IEnumerable<FacturaParaRecurrencia> f, Dictionary<int, ProveedorParaRecurrencia> p,
        IReadOnlySet<(int, DateTime)>? descartes = null, bool mostrar = false, DateTime? hoy = null) =>
        DetectorFacturasRecurrentes.Detectar(f, p, descartes ?? SinDescartes, hoy ?? Hoy, mostrar);

    private static DateTime Mes(int anio, int mes) => new(anio, mes, 1);

    [Fact]
    public void MensualSacaUnaFilaPorCadaMesQueFalta()
    {
        var filas = Detectar([F(1, 2026, 6, 10), F(1, 2026, 7, 10), F(1, 2026, 8, 10, 120.50m)], Proveedores((1, null)));
        Assert.Equal([Mes(2026, 9), Mes(2026, 10), Mes(2026, 11)], filas.Select(f => f.Periodo));
        Assert.All(filas, f =>
        {
            Assert.Equal(PeriodicidadFacturas.Mensual, f.Periodicidad);
            Assert.False(f.PeriodicidadFijada);
            Assert.Equal(120.50m, f.ImporteEstimado);
            Assert.Equal(new DateTime(2026, 8, 10), f.FechaUltima);
            Assert.Equal(1001, f.IdTercero);
        });
        Assert.Equal(new DateTime(2026, 11, 10), filas[^1].FechaEsperada);
    }

    [Fact]
    public void AlDiaNoSaleNadaYElDiaEsperadoTodaviaNoCuenta()
    {
        // Última el 23/10: se espera el 23/11, que es hoy; aún no ha pasado.
        Assert.Empty(Detectar([F(1, 2026, 8, 23), F(1, 2026, 9, 23), F(1, 2026, 10, 23)], Proveedores((1, null))));
        // Al día siguiente sí.
        var filas = Detectar([F(1, 2026, 8, 23), F(1, 2026, 9, 23), F(1, 2026, 10, 23)], Proveedores((1, null)), hoy: new DateTime(2026, 11, 24));
        Assert.Equal(Mes(2026, 11), Assert.Single(filas).Periodo);
    }

    [Fact]
    public void Bimestral()
    {
        var filas = Detectar([F(2, 2026, 3, 5), F(2, 2026, 5, 5), F(2, 2026, 7, 5)], Proveedores((2, null)));
        Assert.Equal([Mes(2026, 9), Mes(2026, 11)], filas.Select(f => f.Periodo));
        Assert.All(filas, f => Assert.Equal(PeriodicidadFacturas.Bimestral, f.Periodicidad));
    }

    [Fact]
    public void Trimestral()
    {
        var filas = Detectar([F(3, 2026, 1, 15), F(3, 2026, 4, 15), F(3, 2026, 7, 15)], Proveedores((3, null)));
        Assert.Equal(Mes(2026, 10), Assert.Single(filas).Periodo);
        Assert.Equal(PeriodicidadFacturas.Trimestral, filas[0].Periodicidad);
    }

    [Fact]
    public void ConSoloDosFacturasNoEsRecurrente()
    {
        Assert.Empty(Detectar([F(1, 2026, 7, 1), F(1, 2026, 8, 1)], Proveedores((1, null))));
    }

    [Fact]
    public void LaRachaSeCuentaDesdeLaMasReciente()
    {
        var p = Proveedores((1, null));
        // Mensual hasta abril, hueco en mayo y retoma junio, julio y agosto: las tres últimas seguidas bastan.
        var retomada = Detectar([F(1, 2026, 1, 3), F(1, 2026, 2, 3), F(1, 2026, 3, 3), F(1, 2026, 4, 3), F(1, 2026, 6, 3), F(1, 2026, 7, 3), F(1, 2026, 8, 3)], p);
        Assert.Equal([Mes(2026, 9), Mes(2026, 10), Mes(2026, 11)], retomada.Select(f => f.Periodo));
        // Mensual hasta mayo y luego una suelta en agosto: desde la más reciente no hay tres seguidas (3 meses y luego 1).
        Assert.Empty(Detectar([F(1, 2026, 3, 3), F(1, 2026, 4, 3), F(1, 2026, 5, 3), F(1, 2026, 8, 3)], p));
    }

    [Fact]
    public void LaPeriodicidadFijadaManda()
    {
        // Una sola factura con periodicidad fijada: basta.
        var fijada = Detectar([F(1, 2026, 9, 1, 50m)], Proveedores((1, PeriodicidadFacturas.Mensual)));
        Assert.Equal([Mes(2026, 10), Mes(2026, 11)], fijada.Select(f => f.Periodo));
        Assert.All(fijada, f => Assert.True(f.PeriodicidadFijada));
        // Deduciría mensual, pero la ficha dice trimestral.
        var trimestral = Detectar([F(1, 2026, 6, 1), F(1, 2026, 7, 1), F(1, 2026, 8, 1)], Proveedores((1, PeriodicidadFacturas.Trimestral)));
        Assert.Equal(Mes(2026, 11), Assert.Single(trimestral).Periodo);
    }

    [Fact]
    public void NoControlarLoDejaFuera()
    {
        Assert.Empty(Detectar([F(1, 2026, 6, 1), F(1, 2026, 7, 1), F(1, 2026, 8, 1)], Proveedores((1, PeriodicidadFacturas.NoControlar))));
    }

    [Fact]
    public void ElPeriodoDescartadoNoSaleSalvoQueSePida()
    {
        var f = new[] { F(1, 2026, 6, 10), F(1, 2026, 7, 10), F(1, 2026, 8, 10) };
        var descartes = new HashSet<(int, DateTime)> { (1, Mes(2026, 10)) };
        Assert.Equal([Mes(2026, 9), Mes(2026, 11)], Detectar(f, Proveedores((1, null)), descartes).Select(x => x.Periodo));
        var todas = Detectar(f, Proveedores((1, null)), descartes, mostrar: true);
        Assert.Equal(3, todas.Count);
        Assert.True(todas.Single(x => x.Periodo == Mes(2026, 10)).Descartada);
        Assert.Equal(2, todas.Count(x => !x.Descartada));
    }

    [Fact]
    public void VariasEnUnMesCuentanComoUnaYLaUltimaEsLaMasReciente()
    {
        var filas = Detectar([F(1, 2026, 7, 2, 10m), F(1, 2026, 7, 20, 20m), F(1, 2026, 8, 5, 30m), F(1, 2026, 9, 1, 40m), F(1, 2026, 9, 12, 45m)], Proveedores((1, null)));
        Assert.Equal([Mes(2026, 10), Mes(2026, 11)], filas.Select(f => f.Periodo));
        Assert.All(filas, f => Assert.Equal(45m, f.ImporteEstimado));
        Assert.Equal(new DateTime(2026, 9, 12), filas[0].FechaUltima);
        // Dos en el mismo mes no hacen racha: julio, julio, agosto son solo dos meses.
        Assert.Empty(Detectar([F(2, 2026, 7, 2), F(2, 2026, 7, 20), F(2, 2026, 8, 5)], Proveedores((2, null))));
    }

    [Fact]
    public void RectificativasDuplicadasYSinProveedorNoCuentan()
    {
        var p = Proveedores((1, null));
        // Con la rectificativa (o la duplicada, o la negativa) de septiembre, la última buena es la de agosto.
        var filas = Detectar([F(1, 2026, 6, 1), F(1, 2026, 7, 1), F(1, 2026, 8, 1, 80m), F(1, 2026, 9, 1, 80m, rectificativa: true),
            F(1, 2026, 9, 2, 80m, duplicada: true), F(1, 2026, 9, 3, -80m), new FacturaParaRecurrencia(99, null, new DateTime(2026, 9, 1), 80m)], p);
        Assert.Equal([Mes(2026, 9), Mes(2026, 10), Mes(2026, 11)], filas.Select(f => f.Periodo));
        Assert.All(filas, f => Assert.Equal(80m, f.ImporteEstimado));
        // Sin ellas no hay tres seguidas.
        Assert.Empty(Detectar([F(1, 2026, 7, 1), F(1, 2026, 8, 1, duplicada: true), F(1, 2026, 9, 1, rectificativa: true), F(1, 2026, 10, 1)], p));
    }

    [Fact]
    public void CambioDeAnio()
    {
        var hoy = new DateTime(2027, 2, 10);
        var filas = Detectar([F(1, 2026, 10, 5), F(1, 2026, 11, 5), F(1, 2026, 12, 5)], Proveedores((1, null)), hoy: hoy);
        Assert.Equal([Mes(2027, 1), Mes(2027, 2)], filas.Select(f => f.Periodo));
        // Trimestral de noviembre: se espera el 20/02/2027; el 10/02 aún no falta, el 25/02 sí.
        var trimestrales = new[] { F(2, 2026, 5, 20), F(2, 2026, 8, 20), F(2, 2026, 11, 20) };
        Assert.Empty(Detectar(trimestrales, Proveedores((2, null)), hoy: hoy));
        var trimestral = Detectar(trimestrales, Proveedores((2, null)), hoy: new DateTime(2027, 2, 25));
        Assert.Equal(Mes(2027, 2), Assert.Single(trimestral).Periodo);
        Assert.Equal(new DateTime(2027, 2, 20), trimestral[0].FechaEsperada);
    }

    [Fact]
    public void SoloMiraLosUltimosDoceMeses()
    {
        // Las facturas de hace más de 12 meses no cuentan: quedan dos y no hay racha.
        Assert.Empty(Detectar([F(1, 2025, 9, 1), F(1, 2025, 10, 1), F(1, 2025, 11, 1), F(1, 2025, 12, 1)], Proveedores((1, null))));
        // Una factura con fecha futura tampoco.
        Assert.Empty(Detectar([F(1, 2026, 12, 1)], Proveedores((1, PeriodicidadFacturas.Mensual))));
    }

    [Fact]
    public void DeducirDevuelveNullSiElIntervaloPasaDeTresMeses()
    {
        Assert.Null(DetectorFacturasRecurrentes.Deducir([new(2026, 1, 1), new(2026, 5, 1), new(2026, 9, 1)]));
        Assert.Equal(PeriodicidadFacturas.Bimestral, DetectorFacturasRecurrentes.Deducir([new(2026, 1, 1), new(2026, 3, 1), new(2026, 5, 1)]));
    }
}
