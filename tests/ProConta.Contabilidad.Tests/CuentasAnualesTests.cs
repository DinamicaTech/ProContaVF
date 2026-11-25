using ProConta.Contabilidad.CuentasAnuales;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cuentas anuales del modelo pymes sin BD (contabilidad/procesos/cuentas-anuales 2026-11-23): correspondencia oficial, totales con las fórmulas
/// del modelo, invariantes (Debe = Haber → activo = patrimonio neto y pasivo; resultado de pérdidas y ganancias = el del balance) y comprobaciones.
/// Datos inventados.
/// </summary>
public sealed class CuentasAnualesTests
{
    private static (string, byte, decimal, decimal) A(string cuenta, decimal debe, decimal haber, byte tipo = 0) => (cuenta, tipo, debe, haber);

    private static readonly Dictionary<string, string> Nombres = new() { ["5720001"] = "Banco", ["4300001"] = "Cliente" };

    /// <summary>Un ejercicio pequeño que cuadra: apertura, ventas, gastos, amortización, impuesto, IVA y banco.</summary>
    private static List<(string, byte, decimal, decimal)> Ejercicio(bool regularizado)
    {
        var l = new List<(string, byte, decimal, decimal)>
        {
            // Apertura (TipoAsiento 1).
            A("1000000", 0, 3000m, 1), A("1130000", 0, 20000m, 1), A("2110000", 150000m, 0, 1), A("2811000", 0, 30000m, 1),
            A("5720001", 13000m, 0, 1), A("1700000", 0, 100000m, 1), A("4100001", 0, 10000m, 1),
            // Ventas con IVA y su cobro.
            A("4300001", 60500m, 0), A("7050000", 0, 50000m), A("4770000", 0, 10500m), A("5720001", 55000m, 0), A("4300001", 0, 55000m),
            // Gastos, amortización, intereses e impuesto.
            A("6290000", 12000m, 0), A("4720000", 2520m, 0), A("5720001", 0, 14520m),
            A("6810000", 6000m, 0), A("2811000", 0, 6000m),
            A("6623000", 3000m, 0), A("5720001", 0, 3000m),
            A("6300000", 7250m, 0), A("4752000", 0, 7250m),
            // Préstamo a corto y banco en positivo.
            A("1700000", 20000m, 0), A("5200000", 0, 20000m),
        };
        if (regularizado)
            l.AddRange([A("7050000", 50000m, 0, 2), A("6290000", 0, 12000m, 2), A("6810000", 0, 6000m, 2), A("6623000", 0, 3000m, 2),
                A("6300000", 0, 7250m, 2), A("1290000", 0, 21750m, 2)]);
        return l;
    }

    private static CifrasEjercicio Cifras(bool regularizado = false, int anio = 2025) => ServicioCuentasAnuales.Cifras(anio, Ejercicio(regularizado), Nombres);

    [Fact]
    public void Claves_y_sumas_del_modelo_como_las_del_BOE()
    {
        Assert.Equal(["11000", "12000"], ModeloPymes.PorClave["10000"].Sumandos);
        Assert.Equal(["20000", "31000", "32000"], ModeloPymes.PorClave["30000"].Sumandos);
        Assert.Equal(["12380", "12370", "12390"], ModeloPymes.PorClave["12300"].Sumandos);
        Assert.Equal(["49300", "41900"], ModeloPymes.PorClave["49500"].Sumandos);
        Assert.Equal("110009", ModeloPymes.PorClave["11000"].ClaveAnterior);
        // Todas las claves que suman existen y ninguna regla apunta a un total.
        Assert.All(ModeloPymes.PorClave.Values.SelectMany(p => p.Sumandos), c => Assert.True(ModeloPymes.PorClave.ContainsKey(c), c));
        Assert.All(ModeloPymes.CorrespondenciaBalance.Concat(ModeloPymes.CorrespondenciaPyG), r => Assert.False(ModeloPymes.PorClave[r.Clave].EsTotal, r.Prefijo));
    }

    [Theory]
    [InlineData("2811000", 0, "11200")]
    [InlineData("4300001", 1, "12382")]
    [InlineData("4720000", 1, "12390")]
    [InlineData("4730000", 1, "12390")]
    [InlineData("4752000", -1, "32590")]
    [InlineData("4100001", -1, "32590")]
    [InlineData("4000001", -1, "32582")]
    [InlineData("1030000", 1, "21120")]
    [InlineData("1034000", -1, "32390")]
    [InlineData("5510001", 1, "12500")]
    [InlineData("5510001", -1, "32390")]
    [InlineData("5523000", -1, "32400")]
    [InlineData("5200000", -1, "32320")]
    [InlineData("1700000", -1, "31220")]
    [InlineData("1290000", -1, "21700")]
    [InlineData("5570000", 1, "21800")]
    public void Correspondencia_del_balance(string cuenta, int signo, string clave) =>
        Assert.Equal(clave, ModeloPymes.Regla(ModeloPymes.CorrespondenciaBalance, cuenta, signo)?.Clave);

    [Theory]
    [InlineData("7050000", "40100")]
    [InlineData("7520000", "40500")]
    [InlineData("6290000", "40700")]
    [InlineData("6341000", "40700")]
    [InlineData("6400000", "40600")]
    [InlineData("6810000", "40800")]
    [InlineData("6623000", "41500")]
    [InlineData("7690000", "41490")]
    [InlineData("6300000", "41900")]
    [InlineData("6780000", "41300")]
    public void Correspondencia_de_perdidas_y_ganancias(string cuenta, string clave) =>
        Assert.Equal(clave, ModeloPymes.Regla(ModeloPymes.CorrespondenciaPyG, cuenta, 1)?.Clave);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Balance_y_perdidas_y_ganancias_cuadran_regularizado_o_no(bool regularizado)
    {
        var c = Cifras(regularizado);
        Assert.True(c.SinPartida.Count == 0, string.Join(", ", c.SinPartida.Select(s => s.Cuenta)));
        // Resultado: 50.000 − 12.000 − 6.000 − 3.000 − 7.250 = 21.750.
        Assert.Equal(50000m, c["40100"]);
        Assert.Equal(-12000m, c["40700"]);
        Assert.Equal(-6000m, c["40800"]);
        Assert.Equal(32000m, c["49100"]);
        Assert.Equal(-3000m, c["49200"]);
        Assert.Equal(29000m, c["49300"]);
        Assert.Equal(21750m, c.Resultado);
        Assert.Equal(21750m, c["21700"]);
        // Activo: material 150.000 − 36.000; clientes 5.500; IVA soportado 2.520; banco 13.000 + 55.000 − 14.520 − 3.000.
        Assert.Equal(114000m, c["11200"]);
        Assert.Equal(5500m, c["12382"]);
        Assert.Equal(5500m, c["12380"]);
        Assert.Equal(2520m, c["12390"]);
        Assert.Equal(50480m, c["12700"]);
        Assert.Equal(172500m, c.TotalActivo);
        // Patrimonio neto y pasivo.
        Assert.Equal(3000m, c["21110"]);
        Assert.Equal(20000m, c["21360"]);
        Assert.Equal(44750m, c["20000"]);
        Assert.Equal(80000m, c["31220"]);
        Assert.Equal(20000m, c["32320"]);
        Assert.Equal(10000m + 10500m + 7250m, c["32590"]);
        Assert.Equal(c.TotalActivo, c.TotalPatrimonioNetoYPasivo);
        Assert.Equal(regularizado, c.Regularizado);
    }

    [Fact]
    public void El_cierre_no_entra_y_la_regularizacion_no_cambia_las_cifras()
    {
        var sin = Cifras(false);
        var con = ServicioCuentasAnuales.Cifras(2025,
            Ejercicio(true).Concat([A("1290000", 21750m, 0, 3), A("5720001", 0, 50480m, 3), A("1000000", 3000m, 0, 3)]), Nombres);
        Assert.All(ModeloPymes.PorClave.Keys, k => Assert.Equal(sin[k], con[k]));
    }

    [Fact]
    public void Componer_con_el_anterior_sin_errores_y_filas_en_orden_del_modelo()
    {
        var r = CalculoCuentasAnuales.Componer("Sociedad de prueba SL", "B00000000", Cifras(true, 2025), Cifras(true, 2024), cerrado: true, resultadoBalances: 21750m);
        Assert.False(r.TieneErrores, string.Join(" | ", r.Comprobaciones.Where(c => c.Gravedad != GravedadComprobacion.Correcta).Select(c => c.Titulo)));
        Assert.DoesNotContain(r.Comprobaciones, c => c.Gravedad == GravedadComprobacion.Aviso);
        Assert.Equal("11000", r.Activo[0].Partida.Clave);
        Assert.Equal("10000", r.Activo[^1].Partida.Clave);
        Assert.Equal("49500", r.PerdidasYGanancias[^1].Partida.Clave);
        Assert.Equal(21750m, r.PerdidasYGanancias[^1].Anterior);
        Assert.Contains(r.Activo, f => f.Partida.Clave == "12700" && f.CuentasActual.Single().Cuenta == "5720001" && f.CuentasActual.Single().Nombre == "Banco");
    }

    [Fact]
    public void Una_subcuenta_sin_partida_es_un_error_que_dice_que_hacer_y_descuadra()
    {
        var c = ServicioCuentasAnuales.Cifras(2025, Ejercicio(false).Concat([A("8000000", 100m, 0), A("1130000", 0, 100m)]), Nombres);
        var s = Assert.Single(c.SinPartida);
        Assert.Equal("8000000", s.Cuenta);
        var r = CalculoCuentasAnuales.Componer("S", "B0", c, null, cerrado: false);
        Assert.True(r.TieneErrores);
        Assert.Contains(r.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Error && x.Titulo.Contains("8000000") && x.QueHacer is { Length: > 0 });
        Assert.Contains(r.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Error && x.Titulo.Contains("activo no es igual"));
        Assert.Contains(r.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Aviso && x.Titulo.Contains("no está cerrado"));
        Assert.Contains(r.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Aviso && x.Titulo.Contains("Sin datos de 2024"));
    }

    [Fact]
    public void Los_saldos_al_reves_solo_pasan_al_otro_lado_si_su_partida_queda_en_negativo()
    {
        // Banco en descubierto (500), cliente con anticipo (300 acreedor), socio con dividendo pagado de más (200 deudor en la 526) y proveedor con saldo deudor (100).
        var c = ServicioCuentasAnuales.Cifras(2025,
        [
            A("5720002", 0, 500m), A("4300002", 0, 300m), A("5260001", 200m, 0), A("4000001", 100m, 0),
            A("4300001", 1500m, 0), A("1000000", 0, 1000m),
        ], Nombres);
        Assert.Equal(0m, c["12700"]);
        Assert.Equal(500m, c["32320"]);
        // El cliente con anticipo se compensa con los demás clientes: la partida sigue en positivo.
        Assert.Equal(0m, c["32590"]);
        Assert.Equal(1200m, c["12382"]);
        Assert.Equal(100m, c["12390"]);
        Assert.Equal(200m, c["12500"]);
        Assert.Equal(0m, c["32390"]);
        Assert.Equal(c.TotalActivo, c.TotalPatrimonioNetoYPasivo);
        Assert.Equal(["4000001", "5260001", "5720002"], c.Reclasificadas.Select(x => x.Cuenta));
        var r = CalculoCuentasAnuales.Componer("S", "B0", c, c with { Ejercicio = 2024 }, cerrado: true);
        Assert.False(r.TieneErrores, string.Join(" | ", r.Comprobaciones.Where(x => x.Gravedad == GravedadComprobacion.Error).Select(x => x.Titulo)));
        Assert.Contains(r.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Aviso && x.Titulo.Contains("saldo al revés"));
        // Las correctoras no se mueven: la amortización acumulada resta en su partida.
        Assert.Null(CalculoCuentasAnuales.Reclasificacion("11200", "2811000", -10m));
        Assert.Null(CalculoCuentasAnuales.Reclasificacion("12382", "4900000", -10m));
    }

    [Fact]
    public void Una_partida_en_negativo_que_no_se_puede_reclasificar_es_un_error()
    {
        var c = ServicioCuentasAnuales.Cifras(2025, [A("2811000", 0, 500m), A("1000000", 500m, 0)], Nombres);
        Assert.Equal(-500m, c["11200"]);
        var r = CalculoCuentasAnuales.Componer("S", "B0", c, c with { Ejercicio = 2024 }, cerrado: true);
        Assert.Contains(r.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Error && x.Titulo.Contains("Inmovilizado material"));
        Assert.Throws<InvalidOperationException>(() => XbrlCuentasAnuales.Generar(r));
    }

    [Fact]
    public void El_XBRL_lleva_cada_partida_de_los_dos_ejercicios_con_su_elemento_de_la_taxonomia()
    {
        var r = CalculoCuentasAnuales.Componer("Sociedad de prueba SL", "B00000000", Cifras(true, 2025), Cifras(true, 2024), cerrado: true);
        var xml = System.Xml.Linq.XDocument.Parse(System.Text.Encoding.UTF8.GetString(XbrlCuentasAnuales.Generar(r)));
        System.Xml.Linq.XNamespace xbrli = "http://www.xbrl.org/2003/instance";
        Assert.Equal(4, xml.Root!.Elements(xbrli + "context").Count());
        var hechos = xml.Root.Elements().Where(e => e.Name.NamespaceName.StartsWith("http://www.icac", StringComparison.Ordinal)).ToList();
        Assert.Equal(2 * XbrlCuentasAnuales.Elementos.Count, hechos.Count);
        string Valor(string clave, int anio)
        {
            var (pre, el) = XbrlCuentasAnuales.Elementos[clave];
            return hechos.Single(h => h.Name == XbrlCuentasAnuales.Prefijos[pre] + el && ((string)h.Attribute("contextRef")!).StartsWith($"Y{anio}_", StringComparison.Ordinal)).Value;
        }
        Assert.Equal("172500.00", Valor("10000", 2025));
        Assert.Equal("172500.00", Valor("30000", 2025));
        Assert.Equal("-6000.00", Valor("40800", 2025));
        Assert.Equal("21750.00", Valor("49500", 2024));
        // Cada clave del modelo tiene su elemento, salvo la reserva de capitalización (la taxonomía v1.7.1 no la tiene).
        Assert.Equal(["21350"], ModeloPymes.PorClave.Keys.Where(k => !XbrlCuentasAnuales.Elementos.ContainsKey(k)));
    }

    [Fact]
    public void El_resultado_distinto_del_de_Balances_avisa()
    {
        var r = CalculoCuentasAnuales.Componer("S", "B0", Cifras(), Cifras(anio: 2024), cerrado: true, resultadoBalances: 1m);
        Assert.Contains(r.Comprobaciones, x => x.Gravedad == GravedadComprobacion.Aviso && x.Titulo.Contains("Balances"));
    }

    [Fact]
    public void Memoria_con_las_tablas_calculadas_y_los_textos_guardados()
    {
        var c = CalculoCuentasAnuales.Componer("Sociedad de prueba SL", "B00000000", Cifras(true, 2025), Cifras(true, 2024), cerrado: true);
        var d = new DatosMemoria("Calle Inventada 1, 08000 Barcelona", 3000, 1m,
            [new DividendoSocio("Socio Uno", new DateTime(2025, 6, 30), 1000m, 190m)], new AplicacionMemoria(21750m, 0m, 600m, 0m, 21150m));
        var m = MemoriaPymes.Componer(c, d, new Dictionary<string, string> { ["2"] = "Texto escrito." });
        Assert.Equal(MemoriaPymes.Apartados.Count, m.Count);
        Assert.Contains("Calle Inventada 1", m[0].Texto);
        Assert.True(m[1].Guardado);
        Assert.Equal("Texto escrito.", m[1].Texto);
        Assert.False(m[0].Guardado);

        // Inmovilizado material: 150.000 brutos sin movimiento; amortización 30.000 + 6.000.
        var inm = Assert.Single(m.Single(a => a.Numero == "4").Tablas);
        decimal? Celda(string concepto, int col) => inm.Filas.Single(f => f.Concepto.StartsWith(concepto)).Importes[col];
        Assert.Equal(150000m, Celda("A)", 1));
        Assert.Equal(150000m, Celda("B)", 1));
        Assert.Equal(30000m, Celda("C)", 1));
        Assert.Equal(6000m, Celda("(+) Dotación", 1));
        Assert.Equal(36000m, Celda("D)", 1));
        Assert.Equal(114000m, Celda("Valor neto", 1));
        Assert.Equal(c.Actual["11200"], Celda("Valor neto", 1));

        Assert.Contains("3.000 participaciones", m.Single(a => a.Numero == "7").Texto);
        Assert.Equal(1000m, m.Single(a => a.Numero == "9").Tablas.Single().Filas[^1].Importes[0]);
        var ap = m.Single(a => a.Numero == "11").Tablas.Single();
        Assert.Equal(ap.Filas[0].Importes[0], ap.Filas[^1].Importes[0]);
        // Las tablas de pasivos solo llevan las partidas con importe y su total cuadra con el balance.
        var pas = m.Single(a => a.Numero == "6").Tablas.Single();
        Assert.Equal(c.Actual["31200"] + c.Actual["32300"] + c.Actual["32500"], pas.Filas[^1].Importes[0]);
        Assert.DoesNotContain(pas.Filas, f => f.Concepto.Contains("Proveedores"));
    }
}
