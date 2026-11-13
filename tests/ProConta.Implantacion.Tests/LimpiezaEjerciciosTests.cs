using ProConta.Implantacion.ContaSol;
using ProConta.Implantacion.ERP;

namespace ProConta.Implantacion.Tests;

/// <summary>
/// Limpieza de SociedadesEjercicios (organizacion/sociedades-ejercicios 2026-11-13 20:55), sin BD: subcuentas del IVA del 0 %
/// al importar de ContaSol y configuración del ejercicio que rellena la migración del ERP. Datos sintéticos.
/// </summary>
public sealed class LimpiezaEjerciciosTests
{
    private static ApunteContaSol Ap(int asiento, int orden, string cuenta, decimal importe, bool debe) =>
        new(1, asiento, orden, new DateTime(2024, 3, 1), cuenta, importe, debe, "Concepto", null, 0, 0, null, 0);

    private static OrigenContaSol Origen(IReadOnlyList<CuentaContaSol> cuentas, IReadOnlyList<ApunteContaSol> apuntes, IReadOnlyDictionary<string, decimal>? saldos = null) =>
        new("sintetico.accdb", "00", new EmpresaContaSol("T01", "B12345674", "Sociedad sintética", 2024), [], [], cuentas, apuntes, [], [], [],
            saldos ?? new Dictionary<string, decimal>());

    private static CuentaContaSol C(string codigo) => new(codigo, $"Cuenta {codigo}", null, 0, 0);

    [Theory]
    [InlineData("4720001", "4720000")]
    [InlineData("4770001", "4770000")]
    [InlineData("4725001", "4725000")]
    [InlineData("4728001", "4728000")]
    [InlineData("4775001", "4775000")]
    [InlineData("4778001", "4778000")]
    [InlineData(" 4777001 ", "4777000")]
    // No son del 0 %: importación, regularización de prorrata, exportación y otras series.
    [InlineData("4724001", "4724001")]
    [InlineData("4729001", "4729001")]
    [InlineData("4774001", "4774001")]
    [InlineData("4721001", "4721001")]
    [InlineData("4720021", "4720021")]
    [InlineData("4770000", "4770000")]
    [InlineData("4300001", "4300001")]
    public void Las_subcuentas_del_IVA_del_0_pasan_a_terminar_en_000(string contasol, string esperada) =>
        Assert.Equal(esperada, IvaCeroContaSol.Renombrar(contasol));

    [Fact]
    public void El_0_se_renombra_en_cuentas_apuntes_y_saldos_y_la_antigua_del_16_sin_apuntes_no_se_importa()
    {
        var origen = Origen([C("4720000"), C("4720001"), C("4770000"), C("4770001"), C("4778001"), C("4724001"), C("4300001"), C("7000001")],
            [Ap(1, 1, "4300001", 100m, true), Ap(1, 2, "7000001", 100m, false), Ap(1, 3, "4778001", 0m, false), Ap(2, 1, "4724001", 5m, true), Ap(2, 2, "4300001", 5m, false)],
            new Dictionary<string, decimal> { ["4778001"] = 0m, ["4720000"] = 0m, ["4724001"] = 5m });
        var (r, incidencias) = IvaCeroContaSol.Aplicar(origen);
        Assert.Equal(["4720000", "4770000", "4778000", "4724001", "4300001", "7000001"], r.Cuentas.Select(c => c.Codigo));
        Assert.Contains(r.Apuntes, a => a.Cuenta == "4778000");
        Assert.Contains(r.Apuntes, a => a.Cuenta == "4724001");
        Assert.DoesNotContain(r.Apuntes, a => a.Cuenta.EndsWith("8001", StringComparison.Ordinal));
        Assert.Equal(new Dictionary<string, decimal> { ["4778000"] = 0m, ["4724001"] = 5m }, r.SaldosControl);
        Assert.DoesNotContain(incidencias, i => i.Severidad == Severidad.Bloqueante);
        Assert.Equal(2, incidencias.Count(i => i.Codigo == "ContaSol.Iva16Descartada"));
        Assert.Contains(incidencias, i => i.Codigo == "ContaSol.IvaCeroRenombrada" && i.Explicacion.Contains("4778001 → 4778000"));

        // El planificador lo aplica antes de planificar.
        var plan = PlanificadorContaSol.Planificar(origen, 2022);
        Assert.Contains(plan.Cuentas, c => c.Codigo == "4778000");
        Assert.DoesNotContain(plan.Cuentas, c => c.Codigo is "4778001" or "4720001");
        Assert.All(plan.Asientos.SelectMany(a => a.Apuntes), a => Assert.NotEqual("4778001", a.Cuenta));
    }

    [Fact]
    public void Apuntes_en_la_antigua_del_16_detienen_la_importacion_con_un_error_que_dice_ejercicio_cuenta_y_apuntes()
    {
        var origen = Origen([C("4770000"), C("4300001"), C("7000001")],
            [Ap(1, 1, "4300001", 116m, true), Ap(1, 2, "7000001", 100m, false), Ap(1, 3, "4770000", 16m, false), Ap(2, 1, "4300001", 1m, true), Ap(2, 2, "4770000", 1m, false)]);
        var plan = PlanificadorContaSol.Planificar(origen, 2022);
        var i = Assert.Single(plan.Incidencias, i => i.Codigo == IvaCeroContaSol.CodigoIva16ConApuntes);
        Assert.Equal((Severidad.Bloqueante, "destino", "4770000"), (i.Severidad, i.Fase, i.Clave));
        Assert.Contains("ejercicio 2024", i.Explicacion);
        Assert.Contains("4770000 tiene 2 apuntes", i.Explicacion);
        Assert.Contains("4770021", i.Explicacion);
        Assert.Contains("ContaSol", i.Explicacion);
        // Mientras tanto la cuenta se deja como está (no se mezcla con la del 0 %).
        Assert.Contains(plan.Cuentas, c => c.Codigo == "4770000");
        Assert.StartsWith("No importo ningún ejercicio", IvaCeroContaSol.MotivoParada(plan.Incidencias));
    }

    [Theory]
    [InlineData("0.14", "14")]
    [InlineData("0.155", "15.5")]
    [InlineData("1", "100")]
    [InlineData("0", "0")]
    [InlineData("85", "85")]
    public void La_prorrata_del_ERP_pasa_de_tanto_por_uno_a_porcentaje(string erp, string esperada) =>
        Assert.Equal(decimal.Parse(esperada, System.Globalization.CultureInfo.InvariantCulture),
            ReglasErp.ProrrataEnPorcentaje(decimal.Parse(erp, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void El_emisor_de_cada_factura_migrada_es_la_razon_social_de_su_anio_o_la_del_mas_cercano()
    {
        var razones = new Dictionary<int, string> { [2018] = "Antigua SL", [2022] = "Antigua SL", [2023] = "Nueva SL", [2026] = "Nueva SL" };
        Assert.Equal("Antigua SL", ReglasErp.RazonSocialDelAnio(razones, 2018));
        Assert.Equal("Antigua SL", ReglasErp.RazonSocialDelAnio(razones, 2010));
        Assert.Equal("Nueva SL", ReglasErp.RazonSocialDelAnio(razones, 2023));
        Assert.Equal("Antigua SL", ReglasErp.RazonSocialDelAnio(razones, 2020));
        Assert.Null(ReglasErp.RazonSocialDelAnio(new Dictionary<int, string>(), 2020));
    }

    [Fact]
    public void El_ejercicio_que_no_esta_en_el_ERP_copia_la_configuracion_del_mas_cercano()
    {
        var porAnio = new Dictionary<int, Fila> { [2018] = Fila.De(("Ejercicio", 2018)), [2019] = Fila.De(("Ejercicio", 2019)), [2026] = Fila.De(("Ejercicio", 2026)) };
        Assert.Equal(2019, ReglasErp.ConfiguracionDeEjercicio(porAnio, 2019)!.Value.Anio);
        Assert.Equal(2018, ReglasErp.ConfiguracionDeEjercicio(porAnio, 2008)!.Value.Anio);
        Assert.Equal(2018, ReglasErp.ConfiguracionDeEjercicio(porAnio, 2017)!.Value.Anio);
        Assert.Equal(2026, ReglasErp.ConfiguracionDeEjercicio(porAnio, 2027)!.Value.Anio);
        Assert.Equal(2026, ReglasErp.ConfiguracionDeEjercicio(porAnio, 2023)!.Value.Anio);
        Assert.Null(ReglasErp.ConfiguracionDeEjercicio(new Dictionary<int, Fila>(), 2020));
    }

    [Fact]
    public void La_migracion_rellena_la_configuracion_que_queda_y_usa_el_suplido_con_IVA_si_no_hay_suplido()
    {
        var erp = Fila.De(("FormaPagoDefecto", 1), ("TrimestreActivo", 3), ("CuentaSuplido", null), ("CuentaSuplidoIVA", "5550002"),
            ("CuentaRetencionServicios", "4751000"), ("CuentaRetencionAlquileres", "4751000"),
            ("CuentaIVAIntracomunitarioRepercutidoServicios", "4770121"), ("CuentaIVAIntracomunitarioRepercutidoBienes", "4770221"),
            ("CuentaIVAIntracomunitarioSoportadoServicios", "4720221"), ("CuentaIVAIntracomunitarioSoportadoBienes", "4720121"),
            ("CuentaIVAInversionSujetoPasivoRepercutido", "4771021"), ("CuentaIVAInversionSujetoPasivoSoportado", "4721021"),
            ("PorcentajeProrrataAñoAnterior", 0.15f), ("CIF", "B12345674"), ("TipoIVA1", 21f), ("CuentaTipoIVA1", "4720021"));
        var v = ReglasErp.ValoresConfiguracionEjercicio(erp).ToDictionary(x => x.Campo, x => x.Valor);
        Assert.Equal("5550002", v["CuentaSuplido"]);
        Assert.Equal(1, v["FormaPagoDefecto"]);
        Assert.Equal(3, v["TrimestreActivo"]);
        Assert.Equal(15f, v["PorcentajeProrrataAñoAnterior"]);
        Assert.Equal("4721021", v["CuentaIVAInversionSujetoPasivoSoportado"]);
        Assert.Equal("4720121", v["CuentaIVAIntracomunitarioSoportadoBienes"]);
        // Lo que el ERP no tiene se queda vacío (no se escribe), y las columnas eliminadas no se migran.
        Assert.False(v.ContainsKey("TipoIVAGeneral"));
        Assert.False(v.ContainsKey("CuentaIVAImportacion"));
        Assert.False(v.ContainsKey("CuentaRedondeoGastos"));
        Assert.DoesNotContain(v.Keys, k => k is "CIF" or "TipoIVA1" or "CuentaTipoIVA1" or "CuentaSuplidoIVA");
        Assert.Equal(12, v.Count);

        var conSuplido = ReglasErp.ValoresConfiguracionEjercicio(Fila.De(("CuentaSuplido", "5550001"), ("CuentaSuplidoIVA", "5550002"))).ToDictionary(x => x.Campo, x => x.Valor);
        Assert.Equal("5550001", conSuplido["CuentaSuplido"]);
    }
}
