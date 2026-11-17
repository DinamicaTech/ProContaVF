using ProConta.Contabilidad.Dividendos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Aplicación del resultado y acta del reparto de dividendos sin BD (contabilidad/procesos/aplicacion-resultado 2026-11-17). Datos inventados.
/// </summary>
public sealed class AplicacionResultadoTests
{
    private static readonly DateTime Fecha = new(2027, 6, 30);
    private static readonly ConfiguracionAplicacion Config = ConfiguracionAplicacion.Con(null, null, null, null);

    private static PropuestaAplicacion Calcular(decimal resultado, decimal perdidas = 0m, decimal legal = 0m, decimal capital = 20000m, ImportesAplicacion? importes = null,
        decimal dividendos = 0m) =>
        CalculoAplicacionResultado.Calcular(2026, Fecha, Config, new SaldosAplicacion(resultado, perdidas, legal, capital), importes ?? new(), dividendos);

    [Fact]
    public void Cuentas_por_defecto()
    {
        Assert.Equal(("1290000", "1120000", "1130000", "1210000"), (Config.CuentaResultado, Config.CuentaReservaLegal, Config.CuentaVoluntarias, Config.CuentaResultadosNegativos));
    }

    [Fact]
    public void Beneficio_con_la_reserva_legal_por_debajo_del_tope_propone_el_10_por_ciento_y_el_resto_a_voluntarias()
    {
        var p = Calcular(30000m, legal: 1000m);
        Assert.Equal(3000m, p.MinimoLegal);
        Assert.Equal(3000m, p.ReservaLegal);
        Assert.Equal(27000m, p.Voluntarias);
        Assert.Equal(
        [
            new LineaAplicacion("1290000", 30000m, 0m, "Aplicación del resultado 2026"),
            new LineaAplicacion("1120000", 0m, 3000m, "Reserva legal 2026"),
            new LineaAplicacion("1130000", 0m, 27000m, "Reservas voluntarias 2026"),
        ], p.Lineas);
        Assert.Equal(p.TotalDebe, p.TotalHaber);
        Assert.True(p.SePuedeConfirmar);
    }

    [Fact]
    public void La_reserva_legal_no_pasa_del_20_por_ciento_del_capital()
    {
        // Capital 20.000: tope 4.000. Ya hay 3.500: el mínimo es 500 aunque el 10 % sean 10.000.
        Assert.Equal(500m, Calcular(100000m, legal: 3500m).MinimoLegal);
        // Ya llega al tope: todo a voluntarias.
        var p = Calcular(100000m, legal: 4000m);
        Assert.Equal(0m, p.ReservaLegal);
        Assert.Equal(100000m, p.Voluntarias);
        Assert.DoesNotContain(p.Lineas, l => l.Cuenta == "1120000");
    }

    [Fact]
    public void Compensa_antes_las_perdidas_de_ejercicios_anteriores()
    {
        var p = Calcular(10000m, perdidas: 4000m, legal: 4000m);
        Assert.Equal(4000m, p.Compensacion);
        Assert.Equal(6000m, p.Voluntarias);
        Assert.Contains(new LineaAplicacion("1210000", 0m, 4000m, "Compensación de pérdidas anteriores 2026"), p.Lineas);
        Assert.Equal(p.TotalDebe, p.TotalHaber);
    }

    [Fact]
    public void Perdida_va_entera_a_resultados_negativos()
    {
        var p = Calcular(-2500.55m);
        Assert.Equal([new LineaAplicacion("1210000", 2500.55m, 0m, "Aplicación del resultado 2026"), new LineaAplicacion("1290000", 0m, 2500.55m, "Aplicación del resultado 2026")], p.Lineas);
        Assert.True(p.EsPerdida);
        Assert.True(p.SePuedeConfirmar);
    }

    [Fact]
    public void Con_dividendos_la_aplicacion_carga_la_129_por_el_resto_y_el_reparto_por_el_bruto()
    {
        var p = Calcular(150000m, legal: 4000m, dividendos: 123456.80m);
        Assert.Equal(150000m - 123456.80m, p.Voluntarias);
        Assert.Equal(new LineaAplicacion("1290000", 150000m - 123456.80m, 0m, "Aplicación del resultado 2026"), p.Lineas[0]);
        Assert.Equal(p.TotalDebe, p.TotalHaber);
    }

    [Fact]
    public void No_deja_repartir_mas_que_el_beneficio_ni_dividendos_con_perdida()
    {
        Assert.False(Calcular(1000m, legal: 4000m, dividendos: 1234.57m).SePuedeConfirmar);
        Assert.False(Calcular(-1000m, dividendos: 100m).SePuedeConfirmar);
        Assert.False(Calcular(1000m, perdidas: 100m, importes: new(Compensacion: 200m)).SePuedeConfirmar);
    }

    [Fact]
    public void Avisa_si_la_reserva_legal_escrita_es_menor_que_el_minimo_y_sin_resultado_no_hay_nada_que_aplicar()
    {
        var p = Calcular(30000m, importes: new(ReservaLegal: 0m));
        Assert.Contains(p.Avisos, a => a.Contains("mínimo de la ley"));
        Assert.True(p.SePuedeConfirmar);
        Assert.False(Calcular(0m).SePuedeConfirmar);
    }

    [Theory]
    [InlineData(123456.80, "ciento veintitrés mil cuatrocientos cincuenta y seis euros con ochenta céntimos")]
    [InlineData(261800, "doscientos sesenta y un mil ochocientos euros")]
    [InlineData(1, "un euro")]
    [InlineData(21, "veintiún euros")]
    [InlineData(100, "cien euros")]
    [InlineData(1000000, "un millón euros")]
    [InlineData(2000000.01, "dos millones euros con un céntimo")]
    [InlineData(31031, "treinta y un mil treinta y un euros")]
    public void Importe_en_letras(double importe, string texto) => Assert.Equal(texto, ImporteEnLetras.Euros((decimal)importe));

    [Fact]
    public void El_acta_lleva_el_reparto_con_su_importe_en_letras_los_socios_y_las_firmas()
    {
        var d = new DatosActa(1, "Empresa de Pruebas, S.L.", "B00000000", 10m, new DateTime(2025, 4, 30), "11:00", "reservas voluntarias", 19m,
            [
                new SocioActa("Socia Uno", 1000, "Del 1 al 1.000, ambos inclusive", 50m, 61728.40m, 11728.40m, 50000m, "SECRETARIO"),
                new SocioActa("Socio Dos", 1000, "Del 1.001 al 2.000, ambos inclusive", 50m, 61728.40m, 11728.40m, 50000m, "PRESIDENTE"),
            ],
            new AdministradorActa("Socio Dos", "Administrador único", "española", "directivo", "soltero", "Calle Inventada 1", "Ciudad", "00000000T"), "Socio Dos", "Socia Uno", []);
        var html = ActaDividendos.Html(d);
        Assert.Contains("A las 11,00 horas del día 30 de abril de 2025", html);
        Assert.Contains("123.456,80 euros (ciento veintitrés mil cuatrocientos cincuenta y seis euros con ochenta céntimos)", html);
        Assert.Contains("CAPITAL SUSCRITO Y DESEMBOLSADO", html);
        Assert.Contains("10.000 €", html);
        Assert.Contains("B-00000000", html);
        Assert.Contains("SOCIO DOS, de nacionalidad española, mayor de edad, directivo, soltero, con vecindad en Ciudad, con domicilio en Calle Inventada 1 y con DNI núm. 00000000T.", html);
        Assert.Contains("-23.456,80", html);
        Assert.Contains("100.000,00", html);
        Assert.Contains("VºBº EL PRESIDENTE", html);
        Assert.Equal("ActaDividendosEmpresa20250430.pdf", ActaDividendos.NombreFichero(d.Sociedad, d.Fecha));
    }

    [Theory]
    [InlineData("11:00", true, "11,00")]
    [InlineData("9:30", true, "9,30")]
    [InlineData("10.15", true, "10,15")]
    [InlineData("25:00", false, "25:00")]
    [InlineData("once", false, "once")]
    public void Hora_del_acta(string hora, bool valida, string enActa)
    {
        Assert.Equal(valida, ActaDividendos.HoraValida(hora));
        Assert.Equal(enActa, ActaDividendos.Hora(hora));
    }
}
