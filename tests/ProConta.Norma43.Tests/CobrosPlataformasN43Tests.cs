using ProConta.Norma43.Plataformas;

namespace ProConta.Norma43.Tests;

/// <summary>Cruce de los cobros de las plataformas con sus reservas (norma43/conciliacion/plataformas), sin BD y con datos inventados.</summary>
public sealed class CobrosPlataformasN43Tests
{
    private const int Airbnb = 1;
    private const int Booking = 2;

    private static readonly Dictionary<int, PlazosCobroPlataforma> Plazos = new()
    {
        [Airbnb] = new PlazosCobroPlataforma(0m, false, 7, 2, 4),
        [Booking] = new PlazosCobroPlataforma(1.3m, true, 15, 2, 12),
    };

    private static DateTime D(int mes, int dia) => new(2026, mes, dia);

    private static ReservaFacturada R(int id, int origen, DateTime entrada, int noches, decimal total, decimal comision) =>
        new(id, origen, entrada, entrada.AddDays(noches), total, comision);

    private static CobroPlataforma C(int id, int origen, DateTime fecha, decimal importe) => new(id, origen, fecha, importe);

    [Fact]
    public void El_cobro_esperado_descuenta_comision_y_cargo_de_pago()
    {
        // Booking: 529,20 de total, 79,38 de comisión y 1,3 % de cargo (6,88): cobra 442,94.
        Assert.Equal(442.94m, ReglasCobrosPlataformas.CobroEsperado(R(1, Booking, D(3, 1), 3, 529.20m, 79.38m), Plazos[Booking]));
        Assert.Equal(845.00m, ReglasCobrosPlataformas.CobroEsperado(R(2, Airbnb, D(3, 1), 3, 1000m, 155m), Plazos[Airbnb]));
    }

    [Fact]
    public void Un_cobro_de_Booking_paga_dos_reservas()
    {
        var r1 = R(10, Booking, D(5, 1), 3, 529.20m, 79.38m);  // espera 442,94
        var r2 = R(11, Booking, D(5, 2), 4, 600m, 90m);        // espera 502,20
        var otra = R(12, Booking, D(6, 20), 2, 300m, 45m);
        var cobro = C(100, Booking, D(5, 10), 945.14m);

        var r = ReglasCobrosPlataformas.Cruzar([cobro], [r1, r2, otra], Plazos);

        var cruce = Assert.Single(r.Cruces);
        Assert.Equal(TipoCrucePlataforma.Agrupado, cruce.Tipo);
        Assert.Equal([10, 11], cruce.Facturas.Order());
        Assert.All(cruce.Reparto, a => Assert.True(a.Completa));
        Assert.Equal(945.14m, cruce.Reparto.Sum(a => a.Importe));
        Assert.Empty(r.SinCruzar);
    }

    [Fact]
    public void Una_reserva_de_Airbnb_pagada_en_tres_transferencias()
    {
        var reserva = R(20, Airbnb, D(1, 2), 14, 1200m, 186m); // espera 1.014,00
        var cobros = new[] { C(201, Airbnb, D(1, 5), 400m), C(202, Airbnb, D(1, 9), 314m), C(203, Airbnb, D(1, 12), 300m), C(204, Airbnb, D(1, 12), 77.77m) };

        var r = ReglasCobrosPlataformas.Cruzar(cobros, [reserva], Plazos);

        var cruce = Assert.Single(r.Cruces);
        Assert.Equal(TipoCrucePlataforma.Fraccionado, cruce.Tipo);
        Assert.Equal([201, 202, 203], cruce.Lineas);
        // Los dos primeros trozos no completan la factura; el último sí.
        Assert.Equal([false, false, true], cruce.Reparto.Select(a => a.Completa));
        Assert.Equal(1014m, cruce.Reparto.Sum(a => a.Importe));
        var sin = Assert.Single(r.SinCruzar);
        Assert.Equal(204, sin.IdLinea);
        Assert.Contains("No hay ninguna reserva", sin.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public void Dos_cobros_que_pagan_entre_los_dos_dos_reservas()
    {
        var r1 = R(30, Airbnb, D(6, 29), 3, 600m, 93m);  // espera 507
        var r2 = R(31, Airbnb, D(7, 1), 2, 300m, 46.5m); // espera 253,50
        var cobros = new[] { C(301, Airbnb, D(7, 2), 460.50m), C(302, Airbnb, D(7, 3), 300m) };

        var r = ReglasCobrosPlataformas.Cruzar(cobros, [r1, r2], Plazos);

        var cruce = Assert.Single(r.Cruces);
        Assert.Equal(TipoCrucePlataforma.Cruzado, cruce.Tipo);
        // Reparto por orden: el primer cobro paga 460,50 de la primera; el segundo, los 46,50 que faltan y la segunda entera.
        Assert.Equal(
            [new AplicacionCobro(30, 301, 460.50m, false), new AplicacionCobro(30, 302, 46.50m, true), new AplicacionCobro(31, 302, 253.50m, true)],
            cruce.Reparto);
    }

    [Fact]
    public void Dos_reservas_iguales_se_desempatan_por_el_plazo_habitual()
    {
        var r1 = R(40, Airbnb, D(9, 11), 4, 1000m, 155m);
        var r2 = R(41, Airbnb, D(9, 14), 3, 1000m, 155m);
        var cobro = C(400, Airbnb, D(9, 16), 845m); // 5 días tras la primera entrada, 2 tras la segunda

        var r = ReglasCobrosPlataformas.Cruzar([cobro], [r1, r2], Plazos);

        Assert.Equal([41], Assert.Single(r.Cruces).Facturas);
    }

    [Fact]
    public void Si_varias_combinaciones_cuadran_no_concilia()
    {
        var r1 = R(50, Airbnb, D(9, 13), 4, 1000m, 155m);
        var r2 = R(51, Airbnb, D(9, 14), 3, 1000m, 155m);
        var cobro = C(500, Airbnb, D(9, 16), 845m); // 3 y 2 días: las dos en el plazo habitual

        var r = ReglasCobrosPlataformas.Cruzar([cobro], [r1, r2], Plazos);

        Assert.Empty(r.Cruces);
        Assert.Contains("varias reservas", Assert.Single(r.SinCruzar).Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public void No_cruza_fuera_de_plazo_ni_con_otra_plataforma()
    {
        var booking = R(60, Booking, D(4, 1), 2, 500m, 75m); // espera 418,50; cobra desde la salida (3/4)
        var r = ReglasCobrosPlataformas.Cruzar(
            [C(600, Booking, D(4, 2), 418.50m), C(601, Airbnb, D(4, 5), 418.50m), C(602, Booking, D(4, 19), 418.50m)], [booking], Plazos);

        Assert.Empty(r.Cruces);
        Assert.Equal(3, r.SinCruzar.Count);
        Assert.All(r.SinCruzar, s => Assert.Contains("No hay ninguna reserva", s.Motivo, StringComparison.Ordinal));
    }

    [Fact]
    public void Una_plataforma_sin_configurar_usa_los_plazos_predeterminados()
    {
        var vrbo = R(70, 9, D(8, 1), 7, 900m, 100m);
        var r = ReglasCobrosPlataformas.Cruzar([C(700, 9, D(8, 20), 800m)], [vrbo], Plazos);
        Assert.Equal([70], Assert.Single(r.Cruces).Facturas);
    }

    [Fact]
    public void El_reparto_suma_lo_de_cada_cobro_aunque_haya_un_centimo_de_redondeo()
    {
        var r1 = R(80, Booking, D(2, 1), 2, 100m, 15m);   // 83,70
        var r2 = R(81, Booking, D(2, 1), 3, 100.5m, 15m); // 84,19
        var esperado = new Dictionary<int, decimal> { [80] = 83.70m, [81] = 84.19m };
        var reparto = ReglasCobrosPlataformas.Repartir([C(800, Booking, D(2, 8), 167.90m)], [r1, r2], esperado);
        Assert.Equal(167.90m, reparto.Sum(a => a.Importe));
        Assert.Equal(84.20m, reparto.Single(a => a.IdFactura == 81).Importe);
    }
}
