using ProConta.Contabilidad.Impuestos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cálculo del Impuesto sobre Sociedades sin BD (contabilidad/procesos/impuesto-sociedades): el orden de la hoja de la gestoría, los
/// tramos, la bonificación por alquiler de viviendas, el asiento (a ingresar, a devolver y crédito por pérdidas) y que cuadra.
/// El caso «gestoría 2025» reproduce con cifras de la hoja de cálculo de la gestoría el asiento 11241 de 2025 (paridad).
/// </summary>
public sealed class ImpuestoSociedadesTests
{
    private static readonly IReadOnlyList<SaldoCuentaImpuesto> Retenciones2025 =
    [
        new("4730001", "RETENCIONES ALQUILER", 5852.00m, 10),
        new("4730002", "PAGO A CTA. IMP.SOCIEDADES", 5061.08m, 2),
        new("4730004", "RETENCIONES RENTA 4", 2655.25m, 1),
        new("4735058", "RETENCIONES LOCAL", 2232.75m, 10),
        new("4735079", "RETENCIONES LOCAL 2", 1668.33m, 10),
    ];

    private static ParametrosImpuestoSociedades Microempresa2025 => ParametrosImpuestoSociedades.Defecto with
    {
        TipoGeneral = 0.22m, LimiteTramo = 50000m, TipoTramo = 0.21m, Bonificacion = 0.40m,
    };

    private static DatosImpuestoSociedades Datos(decimal resultado, ParametrosImpuestoSociedades? p = null, IReadOnlyList<SaldoCuentaImpuesto>? ret = null,
        decimal viviendas = 0m, decimal credito = 0m, IReadOnlyList<SaldoCuentaImpuesto>? posibles = null) =>
        new(2025, resultado, viviendas, viviendas != 0m, ret ?? [], posibles ?? [], credito, p ?? ParametrosImpuestoSociedades.Defecto, []);

    private static void Cuadra(PropuestaImpuestoSociedades p) => Assert.Equal(p.TotalDebe, p.TotalHaber);

    [Fact]
    public void ReproduceLaHojaDeLaGestoria2025()
    {
        var entrada = new EntradaImpuestoSociedades([new(false, false, "Ajuste art. 21.3", 51727.50m)], Bonificaciones: 2148.29m);
        var p = CalculoImpuestoSociedades.Calcular(Datos(116430.95m, Microempresa2025, Retenciones2025), entrada);

        Assert.Equal(64703.45m, p.ResultadoAjustado);
        Assert.Equal(64703.45m, p.BaseImponible);
        Assert.Equal(13734.76m, p.CuotaIntegra);
        Assert.Equal(11586.47m, p.CuotaLiquida);
        Assert.Equal(17469.41m, p.TotalRetenciones);
        Assert.Equal(-5882.94m, p.Resultado);
        Assert.Equal(new DateTime(2025, 12, 31), p.Fecha);
        Assert.Equal(
            [("6300000", 11586.47m, 0m), ("4709000", 5882.94m, 0m), ("4730001", 0m, 5852.00m), ("4730002", 0m, 5061.08m), ("4730004", 0m, 2655.25m),
                ("4735058", 0m, 2232.75m), ("4735079", 0m, 1668.33m)],
            p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Assert.Equal(17469.41m, p.TotalDebe);
        Cuadra(p);
    }

    [Fact]
    public void CuotaIntegraPorTramosRedondeaCadaTramo()
    {
        var p = Microempresa2025;
        Assert.Equal(13734.76m, CalculoImpuestoSociedades.CuotaIntegra(64703.45m, p));
        Assert.Equal(4200m, CalculoImpuestoSociedades.CuotaIntegra(20000m, p));
        Assert.Equal(0m, CalculoImpuestoSociedades.CuotaIntegra(0m, p));
        Assert.Equal(0m, CalculoImpuestoSociedades.CuotaIntegra(-1000m, p));
        // Sin tramo, todo al general (vacío: 25 %).
        Assert.Equal(25250m, CalculoImpuestoSociedades.CuotaIntegra(101000m, ParametrosImpuestoSociedades.Defecto));
    }

    [Fact]
    public void EjemploDeAceptacionAIngresar()
    {
        // Resultado 100.000, sanción de 1.000 (678) y tipo 25 %: cuota 25.250; con 8.000 en la 473, 17.250 a la 4752.
        var d = Datos(100000m, ret: [new("4730001", "Retenciones", 8000m, 2)], posibles: [new("6780000", "Gastos excepcionales", 1000m, 1)]);
        var p = CalculoImpuestoSociedades.Calcular(d, new([new(false, true, "Multas y sanciones", 1000m, "6780000")]));
        Assert.Equal(101000m, p.BaseImponible);
        Assert.Equal(25250m, p.CuotaLiquida);
        Assert.Equal([("6300000", 25250m, 0m), ("4730001", 0m, 8000m), ("4752000", 0m, 17250m)], p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Assert.DoesNotContain(p.Avisos, a => a.Contains("sin ajustar"));
        Cuadra(p);
    }

    [Fact]
    public void BonificacionPropuestaConLaRentaDeViviendas()
    {
        // Cuota íntegra 25.000 de una base de 100.000; 40.000 de renta de viviendas → 40 % de 40 % de la cuota = 4.000.
        var parametros = ParametrosImpuestoSociedades.Defecto with { Bonificacion = 0.40m };
        var p = CalculoImpuestoSociedades.Calcular(Datos(100000m, parametros, viviendas: 40000m), EntradaImpuestoSociedades.Vacia);
        Assert.Equal(25000m, p.CuotaIntegra);
        Assert.Equal(4000m, p.BonificacionPropuesta);
        Assert.Equal(4000m, p.Bonificaciones);
        Assert.Equal(21000m, p.CuotaLiquida);
        // Si la renta de viviendas pasa de la base, se acota a toda la cuota.
        var toda = CalculoImpuestoSociedades.Calcular(Datos(100000m, parametros, viviendas: 150000m), EntradaImpuestoSociedades.Vacia);
        Assert.Equal(10000m, toda.BonificacionPropuesta);
        // Escrita a mano manda, con aviso.
        var amano = CalculoImpuestoSociedades.Calcular(Datos(100000m, parametros, viviendas: 40000m), new([], Bonificaciones: 3000m));
        Assert.Equal(22000m, amano.CuotaLiquida);
        Assert.Contains(amano.Avisos, a => a.Contains("bonificación de 3.000,00 €"));
    }

    [Fact]
    public void ADevolverCuandoLasRetencionesSuperanLaCuota()
    {
        var p = CalculoImpuestoSociedades.Calcular(Datos(10000m, ret: [new("4730002", "Pagos a cuenta", 3000m, 2)]), EntradaImpuestoSociedades.Vacia);
        Assert.Equal(2500m, p.CuotaLiquida);
        Assert.Equal([("6300000", 2500m, 0m), ("4709000", 500m, 0m), ("4730002", 0m, 3000m)], p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Cuadra(p);
    }

    [Fact]
    public void BaseNegativaSinCuotaDevuelveTodasLasRetenciones()
    {
        var p = CalculoImpuestoSociedades.Calcular(Datos(-5000m, ret: [new("4730004", "Renta 4", 120m, 1)]), EntradaImpuestoSociedades.Vacia);
        Assert.Equal(0m, p.CuotaIntegra);
        Assert.Equal(0m, p.CuotaLiquida);
        Assert.Equal([("4709000", 120m, 0m), ("4730004", 0m, 120m)], p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Assert.Contains(p.Avisos, a => a.Contains("base imponible es negativa"));
        Cuadra(p);
    }

    [Fact]
    public void BasesNegativasYCreditoPorPerdidas()
    {
        // Compensa 10.000 de bases negativas con un crédito activado de 4.000 en la 4745: propone 2.500 (10.000 × 25 %).
        var p = CalculoImpuestoSociedades.Calcular(Datos(50000m, credito: 4000m), new([], BasesNegativas: 10000m));
        Assert.Equal(40000m, p.BaseImponible);
        Assert.Equal(10000m, p.CuotaLiquida);
        Assert.Equal(2500m, p.CreditoPropuesto);
        Assert.Equal([("6300000", 10000m, 0m), ("4752000", 0m, 10000m), ("6301000", 2500m, 0m), ("4745000", 0m, 2500m)],
            p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Cuadra(p);
        // Sin saldo en la 4745 no se propone crédito.
        Assert.Equal(0m, CalculoImpuestoSociedades.Calcular(Datos(50000m), new([], BasesNegativas: 10000m)).CreditoPerdidas);
    }

    [Fact]
    public void CuotaFijadaAManoYAjustesTemporales()
    {
        var entrada = new EntradaImpuestoSociedades([new(true, true, "Amortización no deducible", 2000m), new(true, false, "Reversión", 500m)], CuotaLiquida: 3000m);
        var p = CalculoImpuestoSociedades.Calcular(Datos(10000m), entrada);
        Assert.Equal(10000m, p.ResultadoAjustado);
        Assert.Equal(11500m, p.BasePrevia);
        Assert.Equal(2875m, p.CuotaCalculada);
        Assert.Equal(3000m, p.CuotaLiquida);
        Assert.True(p.CuotaFijada);
        Assert.Contains(p.Avisos, a => a.Contains("Hace falta motivo"));
        Cuadra(p);
    }

    [Fact]
    public void ValidaLoEscrito()
    {
        Assert.NotNull(CalculoImpuestoSociedades.Validar(new([new(false, true, "", 10m)])));
        Assert.NotNull(CalculoImpuestoSociedades.Validar(new([new(false, true, "Multa", 0m)])));
        Assert.NotNull(CalculoImpuestoSociedades.Validar(new([], BasesNegativas: -1m)));
        Assert.NotNull(CalculoImpuestoSociedades.Validar(new([], CuotaLiquida: -1m)));
        Assert.Null(CalculoImpuestoSociedades.Validar(new([new(false, true, "Multa", 10m)], Deducciones: 5m)));
    }

    [Fact]
    public void AvisosDePosiblesAjustesYTramos()
    {
        var p = CalculoImpuestoSociedades.Calcular(Datos(1000m, posibles: [new("6690000", "Intereses de demora", 30m, 1)]), EntradaImpuestoSociedades.Vacia);
        Assert.Contains(p.Avisos, a => a.Contains("6690000"));
        Assert.Contains(p.Avisos, a => a.Contains("no tiene tramo reducido"));
        Assert.DoesNotContain(CalculoImpuestoSociedades.Calcular(Datos(1000m, Microempresa2025), EntradaImpuestoSociedades.Vacia).Avisos, a => a.Contains("tramo reducido"));
    }

    [Fact]
    public void CuentasYPrefijos()
    {
        Assert.True(CalculoImpuestoSociedades.EntraEnResultado("6290001"));
        Assert.True(CalculoImpuestoSociedades.EntraEnResultado("7520000"));
        Assert.False(CalculoImpuestoSociedades.EntraEnResultado("6300000"));
        Assert.False(CalculoImpuestoSociedades.EntraEnResultado("6301000"));
        Assert.False(CalculoImpuestoSociedades.EntraEnResultado("6330000"));
        Assert.False(CalculoImpuestoSociedades.EntraEnResultado("4730001"));
        Assert.Equal(["678", "669"], ParametrosImpuestoSociedades.LeerPrefijos("678, 669;678"));
        Assert.Equal(ParametrosImpuestoSociedades.PrefijosAjustesDefecto, ParametrosImpuestoSociedades.LeerPrefijos(" "));
        Assert.Equal(CuentasImpuestoSociedades.Defecto, CuentasImpuestoSociedades.Con(null, " ", null, "", null));
    }
}
