using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Libros registro de IVA (informes/excel/libros-registro-iva), sin BD: filtro por fecha contable, numeración por factura y cuadre con el 303
/// del mismo periodo (el juego de paridad del 303 del libro anterior de la gestoría).
/// </summary>
public sealed class LibrosRegistroIvaTests
{
    private static LineaIva L(DateTime fecha, int asiento, SentidoIva sentido, string factura, decimal tipo, decimal b, decimal cuota,
        OperacionIva op = OperacionIva.Nacional, DeduccionIva ded = DeduccionIva.Deducible100, int? ejercicio = 2026) =>
        new(fecha, fecha.Year == ejercicio ? (fecha.Month - 1) / 3 + 1 : 0, asiento, sentido, op, false, tipo, "R", "C", "A", ded,
            sentido == SentidoIva.Repercutido ? "Cliente sintético" : "Proveedor sintético", "B00000000", factura, b, cuota,
            sentido == SentidoIva.Soportado && op != OperacionIva.Nacional ? cuota : 0m, null, null)
        { FechaExpedicion = fecha };

    private const SentidoIva R = SentidoIva.Repercutido, S = SentidoIva.Soportado;

    private static readonly IReadOnlyList<LineaIva> Lineas =
    [
        L(new(2026, 1, 10), 1, R, "F-1", 21m, 100m, 21m),
        L(new(2026, 1, 10), 1, R, "F-1", 10m, 50m, 5m),
        L(new(2026, 2, 3), 2, R, "F-2", 0m, 300m, 0m),
        L(new(2026, 3, 31), 3, S, "P-77", 21m, 200m, 42m),
        L(new(2026, 4, 1), 4, R, "F-3", 21m, 1000m, 210m),
        L(new(2026, 2, 20), 5, S, "AIRBNB-1", 21m, 10m, 2.10m, OperacionIva.Intracomunitaria, DeduccionIva.NoDeducible),
        L(new(2025, 12, 31), 6, S, "Fuera", 21m, 999m, 209.79m),
    ];

    [Fact]
    public void Cada_libro_lleva_las_facturas_del_periodo_por_fecha_contable_numeradas_por_factura()
    {
        var r = CalculadoraLibrosIva.Calcular("S", 2026, ParametrosLibrosIva.Trimestre(2026, 1), Lineas, 0.15m, new(2026, 11, 23, 15, 0, 0));
        Assert.Equal([1, 1, 2], r.Expedidas.Select(f => f.Orden));
        Assert.Equal(["F-1", "F-1", "F-2"], r.Expedidas.Select(f => f.Factura));
        Assert.Equal(2, r.FacturasExpedidas);
        Assert.Equal(["AIRBNB-1", "P-77"], r.Recibidas.Select(f => f.Factura));
        Assert.Equal([1, 2], r.Recibidas.Select(f => f.Orden));
        Assert.Equal("Intracomunitaria", r.Recibidas[0].TextoOperacion);
        Assert.Equal(2.10m, r.Recibidas[0].CuotaAutorrepercutida);
        // Totales por tipo, de mayor a menor.
        Assert.Equal([21m, 10m, 0m], r.TotalesExpedidas.Select(t => t.TipoIva));
        Assert.Equal(450m, r.TotalesExpedidas.Sum(t => t.Base));
    }

    [Fact]
    public void Se_puede_pedir_un_solo_libro_y_el_cuadre_sigue_siendo_el_303_entero_del_periodo()
    {
        var r = CalculadoraLibrosIva.Calcular("S", 2026, ParametrosLibrosIva.Trimestre(2026, 1, expedidas: false), Lineas, 0.15m, DateTime.Now);
        Assert.Empty(r.Expedidas);
        Assert.Equal(2, r.Recibidas.Count);
        Assert.Equal(26m + 2.10m, r.Cuadre303.TotalDevengado(0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Las_cuotas_de_cada_trimestre_cuadran_con_el_303_del_libro_anterior(int trimestre)
    {
        var lineas = Iva303Tests.Paridad2026;
        var r = CalculadoraLibrosIva.Calcular("S", 2026, ParametrosLibrosIva.Trimestre(2026, trimestre), lineas, 0.15m, DateTime.Now);
        var del303 = Calculo303.Calcular(lineas, 0.15m);
        Assert.Equal(del303.Resultado(trimestre), r.Cuadre303.Resultado(0));
        Assert.Equal(del303.TotalDevengado(trimestre), r.Cuadre303.TotalDevengado(0));
        Assert.Equal(del303.TotalDeducible(trimestre), r.Cuadre303.TotalDeducible(0));
        // Expedidas: su cuota es el IVA repercutido del 303 (régimen general y rectificativas).
        var repercutido = del303.Devengado.Where(l => l.Clave.StartsWith("dev.rep.", StringComparison.Ordinal) || l.Clave == Calculo303.ClaveModificacion).Sum(l => l.En(trimestre).Cuota);
        Assert.Equal(repercutido, r.Expedidas.Sum(f => f.Cuota));
        // Recibidas: la cuota íntegra es la 100 % deducible más la sujeta a prorrata, la no deducible y la sin clasificar.
        var integra = lineas.Where(l => l.Trimestre == trimestre && l.Sentido == S && l.Deduccion == DeduccionIva.Deducible100).Sum(l => l.Cuota)
            + del303.Linea(Calculo303.ClaveSujetaProrrata).En(trimestre).Cuota + del303.Linea(Calculo303.ClaveNoDeducible).En(trimestre).Cuota
            + del303.Linea(Calculo303.ClaveSinClasificar).En(trimestre).Cuota;
        Assert.Equal(integra, r.Recibidas.Sum(f => f.Cuota));
    }

    [Fact]
    public void El_periodo_se_dice_en_palabras_y_da_nombre_al_fichero()
    {
        Assert.Equal("1T 2026", ParametrosLibrosIva.Trimestre(2026, 1).Periodo);
        Assert.Equal("marzo de 2026", ParametrosLibrosIva.Mes(2026, 3).Periodo);
        Assert.Equal("año 2026", new ParametrosLibrosIva(new(2026, 1, 1), new(2026, 12, 31)).Periodo);
        Assert.Equal("del 05/01/2026 al 20/02/2026", new ParametrosLibrosIva(new(2026, 1, 5), new(2026, 2, 20)).Periodo);
        var r = CalculadoraLibrosIva.Calcular("S", 2026, ParametrosLibrosIva.Trimestre(2026, 3, recibidas: false), [], null, new(2026, 11, 23, 15, 30, 0));
        Assert.Equal("Libro IVA expedidas 3T 2026 (20261123-1530).pdf", CalculadoraLibrosIva.NombreFichero(r, ".pdf"));
        Assert.False(r.ProrrataInformada);
        var fechas = CalculadoraLibrosIva.Calcular("S", 2026, new ParametrosLibrosIva(new(2026, 1, 5), new(2026, 2, 20)), [], null, new(2026, 11, 23, 15, 30, 0));
        Assert.Equal("Libros IVA del 05-01-2026 al 20-02-2026 (20261123-1530).xlsx", CalculadoraLibrosIva.NombreFichero(fechas, ".xlsx"));
    }

    [Theory]
    [InlineData(2026, 11, 23, 3)]
    [InlineData(2026, 2, 10, 1)]
    [InlineData(2027, 1, 20, 4)]
    [InlineData(2026, 4, 1, 1)]
    public void El_trimestre_propuesto_es_el_ultimo_terminado(int anio, int mes, int dia, int esperado) =>
        Assert.Equal(esperado, ParametrosLibrosIva.TrimestrePropuesto(2026, new DateTime(anio, mes, dia)));

    [Fact]
    public void Los_errores_dicen_que_corregir()
    {
        Assert.Contains("al menos un libro", new ParametrosLibrosIva(new(2026, 1, 1), new(2026, 3, 31), false, false).Error(2026));
        Assert.Contains("cámbialas de orden", new ParametrosLibrosIva(new(2026, 3, 1), new(2026, 1, 31)).Error(2026));
        Assert.Contains("cambia antes el ejercicio de trabajo", new ParametrosLibrosIva(new(2025, 12, 1), new(2026, 1, 31)).Error(2026));
        Assert.Null(ParametrosLibrosIva.Trimestre(2026, 4).Error(2026));
    }
}
