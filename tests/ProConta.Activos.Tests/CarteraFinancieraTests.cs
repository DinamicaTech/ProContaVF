using ProConta.Activos.Financieros;
using ProConta.Activos.Gestion;

namespace ProConta.Activos.Tests;

/// <summary>
/// Cálculos de la vista de análisis de activos financieros sin base de datos (activos/activos-financieros, maqueta v4 validada 2026-11-23 20:11):
/// lotes FIFO, coste de lo vendido, TIR, revalorización por lotes según su fecha de compra, variación relativa e indicadores de la proyección.
/// </summary>
public sealed class CarteraFinancieraTests
{
    private static readonly DateTime Hoy = new(2026, 11, 23);
    private static readonly DatosInstrumento Fondo = new("IE00BK5BQT80", "Vanguard FTSE All-World", "VWCE.DE", "VWCE.XETRA", "XETRA", "EUR", "ETF", FuenteCotizacion.Yahoo);

    private static OperacionDeInversion Compra(int id, DateTime f, decimal u, decimal importe) => new(id, OperacionDeInversion.TipoCompra, f, u, importe / u, "EUR", 1m, importe, null);
    private static OperacionDeInversion Venta(int id, DateTime f, decimal u, decimal importe) => new(id, OperacionDeInversion.TipoVenta, f, u, importe / u, "EUR", 1m, importe, null);

    private static InversionDeActivo Inversion(params OperacionDeInversion[] ops) => new(1, 1, Fondo, 0m, ops);

    [Fact]
    public void Fifo_la_venta_consume_primero_el_lote_mas_antiguo()
    {
        var (lotes, ventas) = LotesFifo.Calcular([
            Compra(2, new(2024, 3, 1), 10m, 1200m),
            Compra(1, new(2023, 1, 10), 10m, 1000m),
            Venta(3, new(2025, 6, 2), 15m, 1950m)]);

        Assert.Equal(2, lotes.Count);
        Assert.Equal(new DateTime(2023, 1, 10), lotes[0].Fecha);
        Assert.Equal(0m, lotes[0].Vivas);
        Assert.Equal(5m, lotes[1].Vivas);
        Assert.Equal(600m, lotes[1].CosteVivo);
        var v = Assert.Single(ventas);
        Assert.Equal(1600m, v.CosteVendido); // 10 × 100 + 5 × 120
        Assert.Equal(350m, v.Resultado);
    }

    [Fact]
    public void Fifo_compra_y_venta_el_mismo_dia_cuenta_primero_la_compra()
    {
        var d = new DateTime(2025, 1, 2);
        var (lotes, ventas) = LotesFifo.Calcular([Venta(1, d, 4m, 500m), Compra(2, d, 4m, 400m)]);
        Assert.Equal(0m, lotes[0].Vivas);
        Assert.Equal(400m, ventas[0].CosteVendido);
    }

    [Fact]
    public void Coste_de_lo_vendido_se_redondea_a_centimos()
    {
        var (_, ventas) = LotesFifo.Calcular([Compra(1, new(2025, 1, 2), 3m, 100m), Venta(2, new(2025, 2, 3), 1m, 40m)]);
        Assert.Equal(33.33m, ventas[0].CosteVendido);
    }

    [Fact]
    public void Participaciones_de_un_lote_en_cada_fecha()
    {
        var (lotes, _) = LotesFifo.Calcular([Compra(1, new(2025, 1, 2), 10m, 1000m), Venta(2, new(2025, 3, 3), 4m, 500m)]);
        var l = lotes[0];
        Assert.Equal(0m, l.ParticipacionesEn(new(2025, 1, 1)));
        Assert.Equal(10m, l.ParticipacionesEn(new(2025, 1, 2)));
        Assert.Equal(10m, l.ParticipacionesEn(new(2025, 3, 2)));
        Assert.Equal(6m, l.ParticipacionesEn(new(2025, 3, 3)));
    }

    [Fact]
    public void Tir_de_una_compra_que_sube_un_diez_por_ciento_en_un_anio()
    {
        var t = Tir.Calcular([(new(2025, 1, 1), -1000m), (new(2026, 1, 1), 1100m)]);
        Assert.NotNull(t);
        Assert.Equal(0.1m, Math.Round(t!.Value, 4));
    }

    [Fact]
    public void Tir_no_se_calcula_con_menos_de_un_anio()
    {
        Assert.Null(Tir.Calcular([(new(2026, 1, 1), -1000m), (new(2026, 11, 1), 1100m)]));
        Assert.Null(Tir.Calcular([(new(2020, 1, 1), -1000m)]));
    }

    [Fact]
    public void Tir_con_aportaciones_en_fechas_distintas()
    {
        // 1000 hace dos años y 1000 hace uno; hoy valen 2310 → 10 % anual (1000·1,1² + 1000·1,1 = 2310).
        var t = Tir.Calcular([(new(2024, 1, 1), -1000m), (new(2025, 1, 1), -1000m), (new(2026, 1, 1), 2310m)]);
        Assert.Equal(0.1m, Math.Round(t!.Value, 3));
    }

    [Fact]
    public void Serie_de_precios_da_el_ultimo_conocido()
    {
        var s = new SeriePrecios([(new(2026, 11, 20), 10m), (new(2026, 11, 18), 9m), (new(2026, 11, 20), 11m)]);
        Assert.Equal(2, s.Count);
        Assert.Null(s.En(new(2026, 11, 17)));
        Assert.Equal(9m, s.En(new(2026, 11, 19)));
        Assert.Equal(11m, s.En(new(2026, 11, 22)));
        Assert.Equal((new DateTime(2026, 11, 20), 11m), s.Ultimo);
    }

    [Fact]
    public void Posicion_sin_precio_de_hoy_usa_el_ultimo_guardado()
    {
        var hist = new SeriePrecios([(new(2026, 11, 20), 120m)]);
        var p = PosicionFinanciera.Crear(1, "VWCE", Inversion(Compra(1, new(2024, 1, 2), 10m, 1000m)), hist, null, Hoy);
        Assert.Equal(120m, p.Precio);
        Assert.Equal("histórico guardado", p.OrigenPrecio);
        Assert.Equal(1200m, p.Valor);
        Assert.Equal(200m, p.Plusvalia);
        Assert.Equal(0.2m, p.PlusvaliaPorcentaje);
        Assert.NotNull(p.RentabilidadAnual);
    }

    [Fact]
    public void Posicion_sin_cotizacion_se_valora_a_coste()
    {
        var p = PosicionFinanciera.Crear(1, "PART", Inversion(Compra(1, new(2024, 1, 2), 100m, 3000m)), new SeriePrecios([]), null, Hoy);
        Assert.False(p.TieneCotizacion);
        Assert.Equal(3000m, p.Valor);
        Assert.Equal(0m, p.Plusvalia);
        Assert.Null(p.RentabilidadAnual);
    }

    [Fact]
    public void Revalorizacion_cuenta_cada_lote_desde_su_compra()
    {
        var hist = new SeriePrecios([(new(2026, 1, 1), 100m), (new(2026, 2, 2), 110m), (new(2026, 3, 2), 120m)]);
        var inv = Inversion(Compra(1, new(2026, 1, 1), 10m, 1000m), Compra(2, new(2026, 2, 2), 10m, 1100m), Venta(3, new(2026, 3, 2), 5m, 600m));
        var p = PosicionFinanciera.Crear(1, "VWCE", inv, hist, null, Hoy);
        var r = SeriesCartera.Revalorizacion([p], [new(2026, 1, 15), new(2026, 2, 2), new(2026, 3, 2)]);

        Assert.Equal((1000m, 1000m), (r[0].Invertido, r[0].Valor));     // solo el primer lote, a 100
        Assert.Equal((2100m, 2200m), (r[1].Invertido, r[1].Valor));     // los dos lotes, a 110
        Assert.Equal((1600m, 1800m), (r[2].Invertido, r[2].Valor));     // FIFO: quedan 5 del primero (500) y 10 del segundo (1100), a 120
    }

    [Fact]
    public void Revalorizacion_deja_fuera_las_posiciones_sin_cotizacion()
    {
        var sin = PosicionFinanciera.Crear(2, "PART", Inversion(Compra(1, new(2024, 1, 2), 1m, 3000m)), new SeriePrecios([]), null, Hoy);
        var r = SeriesCartera.Revalorizacion([sin], [new(2026, 1, 2)]);
        Assert.Equal(0m, r[0].Invertido);
        Assert.Null(r[0].PlusvaliaPorcentaje);
    }

    [Fact]
    public void Variacion_relativa_desde_el_primer_dia_con_precio()
    {
        var s = new SeriePrecios([(new(2026, 1, 5), 50m), (new(2026, 1, 7), 55m)]);
        var v = SeriesCartera.Variacion(s, [new(2026, 1, 2), new(2026, 1, 5), new(2026, 1, 6), new(2026, 1, 7)]);
        Assert.Equal([null, 0m, 0m, 0.1m], v);
    }

    [Fact]
    public void Dias_habiles_con_paso_conservan_el_ultimo()
    {
        var d = SeriesCartera.Dias(new(2026, 11, 16), new(2026, 11, 23), 2);
        Assert.Equal([new(2026, 11, 16), new(2026, 11, 18), new(2026, 11, 20), new DateTime(2026, 11, 23)], d);
        Assert.Equal(1, SeriesCartera.Paso(new(2026, 1, 1), new(2026, 12, 31)));
        Assert.True(SeriesCartera.Paso(new(2016, 1, 1), new(2026, 12, 31)) > 1);
    }

    // ---------- proyección ----------

    private static SeriePrecios Serie(IEnumerable<double> precios)
    {
        var d = new DateTime(2020, 1, 1);
        return new SeriePrecios(precios.Select((p, i) => (d.AddDays(i), (decimal)Math.Round(p, 4))));
    }

    [Fact]
    public void Rsi_de_una_serie_que_solo_sube_es_cien()
    {
        var c = Enumerable.Range(0, 30).Select(i => 100.0 + i).ToArray();
        Assert.Equal(100, AnalisisTecnico.Rsi(c)[^1]);
        Assert.True(double.IsNaN(AnalisisTecnico.Rsi(c)[13]));
    }

    [Fact]
    public void Ema_sembrada_con_la_media_simple()
    {
        var e = AnalisisTecnico.Ema([1, 2, 3, 4], 3);
        Assert.Equal(2, e[2]);
        Assert.Equal(0.5 * 4 + 0.5 * 2, e[3]);
    }

    [Fact]
    public void Pivotes_con_el_mes_anterior()
    {
        var f = new List<DateTime> { new(2026, 10, 1), new(2026, 10, 15), new(2026, 10, 31), new(2026, 11, 2) };
        var p = AnalisisTecnico.Pivotes(f, [100, 130, 110, 120])!;
        Assert.Equal(new DateTime(2026, 10, 1), p.Mes);
        Assert.Equal(113.3333m, p.Pivote);     // (130 + 100 + 110) / 3
        Assert.Equal(126.6667m, p.R1);         // 2P − L
        Assert.Equal(96.6667m, p.S1);          // 2P − H
    }

    [Fact]
    public void Una_subida_constante_es_favorable_y_proyecta_su_rentabilidad()
    {
        // 3 años subiendo un 10 % anual compuesto, con un pequeño vaivén para que haya volatilidad.
        var serie = Serie(Enumerable.Range(0, 3 * 252 + 1).Select(i => 100 * Math.Pow(1.1, i / 252.0) * (1 + 0.003 * Math.Sin(i))));
        var a = AnalisisTecnico.Analizar(serie, 10_000m);

        Assert.Equal("Favorable", a.Valoracion);
        Assert.Equal("Al alza", a.Senales.Single(s => s.Clave == "TENDENCIA").Lectura);
        Assert.InRange(a.RentabilidadHistorica!.Value, 0.09m, 0.11m);
        Assert.Equal(3.0m, a.AniosHistorico);
        Assert.Equal(3, a.Escenarios.Count);
        var medio = a.Escenarios.Single(e => e.Desviaciones == 0).Valores;
        Assert.Equal([1, 3, 5], medio.Select(v => v.Anios));
        Assert.InRange(medio[0].Valor, 10_900m, 11_100m);
        var prudente = a.Escenarios.Single(e => e.Desviaciones == -1).Valores[0].Valor;
        var optimista = a.Escenarios.Single(e => e.Desviaciones == 1).Valores[0].Valor;
        Assert.True(prudente < medio[0].Valor && medio[0].Valor < optimista);
    }

    [Fact]
    public void Una_bajada_constante_es_desfavorable()
    {
        var serie = Serie(Enumerable.Range(0, 300).Select(i => 200 * Math.Pow(0.8, i / 252.0) * (1 + 0.003 * Math.Sin(i))));
        var a = AnalisisTecnico.Analizar(serie, 1000m);
        Assert.True(a.Valoracion == "Desfavorable", string.Join(" | ", a.Senales.Select(s => $"{s.Clave} {s.Puntuacion} {s.Detalle}")));
    }

    [Fact]
    public void Con_pocos_datos_no_hay_senales_ni_escenarios()
    {
        var a = AnalisisTecnico.Analizar(Serie(Enumerable.Range(0, 20).Select(i => 10.0 + i)), 1000m);
        Assert.Empty(a.Senales);
        Assert.Equal("Sin datos", a.Valoracion);
        Assert.False(a.TieneProyeccion);

        var b = AnalisisTecnico.Analizar(Serie(Enumerable.Range(0, 100).Select(i => 10.0 + i % 7)), 1000m);
        Assert.NotEmpty(b.Senales);
        Assert.Null(b.Senales.Single(s => s.Clave == "TENDENCIA").Puntuacion); // sin 200 sesiones
        Assert.False(b.TieneProyeccion);                                       // sin un año
    }

    // ---------- histórico de cotizaciones ----------

    [Fact]
    public void Historico_en_dolares_se_pasa_a_euros_con_el_cambio_del_dia_o_el_anterior()
    {
        var g = new List<CotizacionGuardada>
        {
            new(new(2026, 11, 2), 110m, "USD", "Yahoo Finance · cierre"),
            new(new(2026, 11, 3), 121m, "USD", "Yahoo Finance · cierre"),     // sin cambio ese día: vale el del 2
            new(new(2026, 11, 3), 0m, "EUR", null),
        };
        var cambios = new Dictionary<string, IReadOnlyList<(DateTime, decimal)>> { ["USD"] = [(new DateTime(2026, 11, 2), 1.1m)] };
        var s = ServicioCarteraFinanciera.HistoricoEnEuros(g, cambios);
        Assert.Equal(100m, s.En(new(2026, 11, 2)));
        Assert.Equal(110m, s.En(new(2026, 11, 3)));
    }

    [Fact]
    public void Historico_sin_cambio_publicado_deja_el_dia_fuera()
    {
        var g = new List<CotizacionGuardada> { new(new(2026, 11, 20), 50m, "GBP", null) };
        var cambios = new Dictionary<string, IReadOnlyList<(DateTime, decimal)>> { ["GBP"] = [(new DateTime(2026, 10, 1), 0.85m)] };
        Assert.Equal(0, ServicioCarteraFinanciera.HistoricoEnEuros(g, cambios).Count);
    }

    [Fact]
    public void Cierres_a_guardar_no_incluyen_hoy_y_sustituyen_los_del_momento()
    {
        var guardadas = new List<CotizacionGuardada>
        {
            new(new(2026, 11, 19), 10m, "EUR", "Yahoo Finance · cierre"),
            new(new(2026, 11, 20), 11m, "EUR", "EODHD · del momento"),
        };
        var (nuevos, sustituyen) = ServicioCarteraFinanciera.CierresAGuardar(guardadas,
            [(new DateTime(2026, 11, 19), 10.5m), (new DateTime(2026, 11, 20), 11.2m), (new DateTime(2026, 11, 21), 11.4m), (Hoy, 12m)], Hoy);
        Assert.Equal([(new DateTime(2026, 11, 21), 11.4m)], nuevos);
        Assert.Equal([(new DateTime(2026, 11, 20), 11.2m)], sustituyen);
    }

    [Fact]
    public void Desde_que_pedir_el_historico()
    {
        var desde = new DateTime(2021, 11, 13);
        Assert.Equal(desde, ServicioCarteraFinanciera.DesdeQuePedir([], desde));
        Assert.Equal(desde, ServicioCarteraFinanciera.DesdeQuePedir([new(new(2026, 1, 2), 1m, "EUR", null)], desde));
        var completo = new List<CotizacionGuardada> { new(desde, 1m, "EUR", null), new(new(2026, 11, 20), 1m, "EUR", "Yahoo Finance · cierre"), new(Hoy, 1m, "EUR", "EODHD · del momento") };
        Assert.Equal(new DateTime(2026, 11, 10), ServicioCarteraFinanciera.DesdeQuePedir(completo, desde));
        Assert.Equal(new DateTime(2026, 11, 20), ServicioCarteraFinanciera.UltimoDiaHabil(new(2026, 11, 22)));
        Assert.Equal(Hoy, ServicioCarteraFinanciera.UltimoDiaHabil(Hoy));
    }

    [Fact]
    public async Task El_grafico_de_yahoo_acepta_fechas_locales()
    {
        // DateTime.Today es de tipo Local: en un equipo con UTC+1 no debe fallar al pasarlo a segundos Unix.
        var http = new HttpFijo(_ => (System.Net.HttpStatusCode.OK, "{}"));
        var hoy = DateTime.SpecifyKind(new DateTime(2026, 11, 23), DateTimeKind.Local);
        var r = await new ClienteYahooFinance(new HttpClient(http)).GraficoAsync("IWDA.AS", hoy.AddDays(-10), hoy);
        Assert.False(r.EsCorrecto); // sin datos, pero sin excepción
        Assert.Contains($"period1={new DateTimeOffset(new DateTime(2026, 11, 13), TimeSpan.Zero).ToUnixTimeSeconds()}", http.Pedidas[0]);
    }
}
