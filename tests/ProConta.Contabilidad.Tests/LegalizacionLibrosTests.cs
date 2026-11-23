using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.CuentasAnuales;
using ProConta.Contabilidad.Diario;
using ProConta.Contabilidad.Legalizacion;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Legalización de libros sin BD (contabilidad/procesos/legalizacion-libros): orden y numeración del Diario, «suma y sigue» por página, balance
/// inicial, inventario de cierre, plazo, huellas y comprobaciones. Invariante contable: el Diario cuadra y la última «suma y sigue» es su total.
/// </summary>
public sealed class LegalizacionLibrosTests
{
    private static readonly EjercicioDiario E2025 = new(9201, 2025, true, 4);
    private static int _apunte;

    private static ApunteDiario Ap(string cuenta, decimal debe, decimal haber, string nombre = "") =>
        new() { IdApunte = ++_apunte, Orden = _apunte, Cuenta = cuenta, Debe = debe, Haber = haber, NombreCuenta = nombre };

    private static AsientoDiario As(int id, DateTime fecha, string texto, byte tipo, params ApunteDiario[] apuntes) =>
        new AsientoDiario { IdAsiento = id, Ejercicio = E2025, Fecha = fecha, Descripcion = texto, TipoAsiento = tipo }.Con(apuntes);

    /// <summary>Un ejercicio sintético: apertura, dos facturas, un cobro, regularización y cierre, dados de alta en desorden y con huecos.</summary>
    private static List<AsientoDiario> Ejercicio() =>
    [
        As(50, new(2025, 12, 31), "Cierre", LegalizacionLibros.TipoCierre,
            Ap("5720001", 0m, 2210m), Ap("4300001", 0m, 605m), Ap("1000000", 1000m, 0m), Ap("1290000", 1500m, 0m), Ap("4770021", 315m, 0m)),
        As(7, new(2025, 2, 15), "Factura 1", 0, Ap("4300001", 1210m, 0m), Ap("7000001", 0m, 1000m), Ap("4770021", 0m, 210m)),
        As(40, new(2025, 12, 31), "Regularización", LegalizacionLibros.TipoRegularizacion, Ap("7000001", 1500m, 0m), Ap("1290000", 0m, 1500m)),
        As(3, new(2025, 1, 1), "Apertura", LegalizacionLibros.TipoApertura, Ap("5720001", 1000m, 0m, "Banco"), Ap("1000000", 0m, 1000m, "Capital")),
        As(12, new(2025, 3, 31), "Cobro 1", 0, Ap("5720001", 1210m, 0m), Ap("4300001", 0m, 1210m)),
        As(9, new(2025, 6, 10), "Factura 2", 0, Ap("4300001", 605m, 0m), Ap("7000001", 0m, 500m), Ap("4770021", 0m, 105m)),
        As(60, new(2025, 12, 31), "Ajuste del 31/12", 0, Ap("6290001", 10m, 0m), Ap("5720001", 0m, 10m)),
    ];

    [Fact]
    public void El_Diario_va_por_fecha_con_la_apertura_primero_y_la_regularizacion_y_el_cierre_al_final_numerado_sin_huecos()
    {
        var libro = LegalizacionLibros.Diario("Sociedad", "B00000000", 2025, Ejercicio());

        Assert.Equal([3, 7, 12, 9, 60, 40, 50], libro.Asientos.Select(a => a.IdAsiento));
        Assert.Equal(Enumerable.Range(1, 7), libro.Asientos.Select(a => a.Numero));
        Assert.True(libro.Cuadra);
        Assert.Equal(libro.TotalDebe, libro.TotalHaber);
        Assert.Equal(19, libro.NumeroApuntes);
    }

    [Fact]
    public void Cada_pagina_trae_la_suma_anterior_y_pasa_la_suma_y_sigue_hasta_el_total_del_ejercicio()
    {
        var libro = LegalizacionLibros.Diario("Sociedad", "B00000000", 2025, Ejercicio());
        var paginas = LegalizacionLibros.Paginar(libro, 5);

        Assert.Equal(4, paginas.Count);
        Assert.All(paginas.Take(3), p => Assert.Equal(5, p.Lineas.Count));
        Assert.Equal(0m, paginas[0].AnteriorDebe);
        for (var i = 1; i < paginas.Count; i++)
        {
            Assert.Equal(paginas[i - 1].SigueDebe, paginas[i].AnteriorDebe);
            Assert.Equal(paginas[i - 1].SigueHaber, paginas[i].AnteriorHaber);
            Assert.Equal(paginas[i].AnteriorDebe + paginas[i].Lineas.Sum(l => l.Apunte.Debe), paginas[i].SigueDebe);
        }
        Assert.True(paginas[^1].EsUltima);
        Assert.False(paginas[0].EsUltima);
        Assert.Equal(libro.TotalDebe, paginas[^1].SigueDebe);
        Assert.Equal(libro.TotalHaber, paginas[^1].SigueHaber);
        Assert.Equal(4, paginas[^1].Lineas.Count);
        // La página 2 empieza con el cobro; el cierre empieza al final de la 3 y sigue en la 4.
        Assert.True(paginas[1].Lineas[0].PrimeraDelAsiento);
        Assert.False(paginas[1].Lineas[0].SigueDeLaAnterior);
        Assert.True(paginas[3].Lineas[0].SigueDeLaAnterior);
        Assert.Equal(50, paginas[3].Lineas[0].Asiento.IdAsiento);
    }

    [Fact]
    public void Un_Diario_vacio_tiene_una_pagina_con_total_cero()
    {
        var paginas = LegalizacionLibros.Paginar(LegalizacionLibros.Diario("S", "N", 2025, []), 34);
        var p = Assert.Single(paginas);
        Assert.Empty(p.Lineas);
        Assert.True(p.EsUltima);
        Assert.Equal(0m, p.SigueDebe);
    }

    [Fact]
    public void El_balance_inicial_son_los_saldos_de_la_apertura()
    {
        var libro = LegalizacionLibros.Diario("Sociedad", "B00000000", 2025, Ejercicio());
        var inicial = LegalizacionLibros.BalanceInicial(libro);

        Assert.Equal([new SaldoInventario("1000000", "Capital", 0m, 1000m), new SaldoInventario("5720001", "Banco", 1000m, 0m)], inicial);
        Assert.Equal(inicial.Sum(s => s.Deudor), inicial.Sum(s => s.Acreedor));
    }

    [Fact]
    public void El_inventario_de_cierre_lleva_solo_las_subcuentas_de_los_grupos_1_a_5_con_saldo()
    {
        var filas = new List<ProConta.Contabilidad.Balances.MovimientoSubcuenta>
        {
            new("5720001", 2210m, 10m), new("4300001", 1815m, 1210m), new("1000000", 0m, 1000m), new("7000001", 1500m, 1500m),
            new("1290000", 0m, 1500m), new("4770021", 0m, 315m), new("6290001", 10m, 0m), new("4100001", 50m, 50m),
        };
        var r = CalculadoraSumasYSaldos.Calcular("S", 2025, LegalizacionLibros.ParametrosTrimestre(2025, new(2025, 12, 31)), filas,
            new Dictionary<string, string> { ["5720001"] = "Banco" }, DateTime.Now);

        var inv = LegalizacionLibros.InventarioCierre(r);

        Assert.Equal(["1000000", "1290000", "4300001", "4770021", "5720001"], inv.Select(s => s.Cuenta));
        Assert.Equal(new SaldoInventario("5720001", "Banco", 2200m, 0m), inv[^1]);
        Assert.Equal(new SaldoInventario("4300001", CalculadoraSumasYSaldos.FueraDelPlan, 605m, 0m), inv[2]);
    }

    [Fact]
    public void El_plazo_es_cuatro_meses_despues_del_cierre_y_los_trimestres_acaban_en_su_ultimo_dia()
    {
        Assert.Equal(new DateTime(2027, 4, 30), LegalizacionLibros.Plazo(2026));
        Assert.Equal([new DateTime(2026, 3, 31), new DateTime(2026, 6, 30), new DateTime(2026, 9, 30), new DateTime(2026, 12, 31)],
            LegalizacionLibros.FinesDeTrimestre(2026));
        var p = LegalizacionLibros.ParametrosTrimestre(2026, new DateTime(2026, 6, 30));
        Assert.Equal(new DateTime(2026, 1, 1), p.Desde);
        Assert.False(p.IncluirCierre);
        Assert.Null(p.Error(2026));
    }

    [Fact]
    public void La_huella_es_el_SHA256_en_base64_y_en_hexadecimal()
    {
        var contenido = "abc"u8.ToArray();
        Assert.Equal("ungWv48Bz+pBQUDeXa4iI7ADYaOWF3qctBD/YfIAFa0=", LegalizacionLibros.HuellaBase64(contenido));
        Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", LegalizacionLibros.HuellaHex(contenido));
        var f = new FicheroLegalizacion(LegalizacionLibros.FicheroDiario, "Libro Diario", contenido, 1);
        Assert.Equal(64, f.HuellaHex.Length);
        Assert.Equal("DIARIO_001.pdf", f.Nombre);
        Assert.Equal("INV_CUEN_001.pdf", LegalizacionLibros.FicheroInventarios);
    }

    private static DatosLegalizacion Datos(bool cerrado, IReadOnlyList<AsientoDiario> asientos, IReadOnlyList<ComprobacionCuentas>? cuentas = null)
    {
        var libro = LegalizacionLibros.Diario("Sociedad", "B00000000", 2025, asientos);
        var vacio = new CifrasEjercicio(2025, new Dictionary<string, ImportePartida>(), [], true);
        var ca = new CuentasAnualesPymes("Sociedad", "B00000000", vacio, null, cuentas ?? []);
        var sumas = CalculadoraSumasYSaldos.Calcular("Sociedad", 2025, LegalizacionLibros.ParametrosTrimestre(2025, new(2025, 12, 31)), [], new Dictionary<string, string>(), DateTime.Now);
        return new DatosLegalizacion(9201, cerrado, libro, LegalizacionLibros.BalanceInicial(libro), [sumas], [], [], ca, [], DateTime.Now);
    }

    [Fact]
    public void Sin_cerrar_el_ejercicio_es_un_error_que_dice_donde_cerrarlo()
    {
        var c = LegalizacionLibros.Comprobar(Datos(false, Ejercicio()), new DateTime(2026, 2, 1));
        var error = Assert.Single(c, x => x.Gravedad == GravedadLegalizacion.Error);
        Assert.Contains("no está cerrado", error.Titulo);
        Assert.Contains("Cierre del ejercicio", error.QueHacer);
        Assert.Contains(c, x => x.Gravedad == GravedadLegalizacion.Correcta && x.Titulo == "El Diario cuadra");
        Assert.Contains(c, x => x.Titulo == "Plazo de presentación: hasta el 30/04/2026");
    }

    [Fact]
    public void Un_asiento_descuadrado_o_las_cuentas_con_errores_impiden_presentar_y_el_plazo_vencido_avisa()
    {
        var asientos = Ejercicio();
        asientos.Add(As(70, new(2025, 5, 5), "Descuadrado", 0, Ap("6290001", 10m, 0m), Ap("5720001", 0m, 9m)));
        var d = Datos(true, asientos, [new ComprobacionCuentas(GravedadComprobacion.Error, "El activo no cuadra", "…")]);

        var c = LegalizacionLibros.Comprobar(d, new DateTime(2026, 5, 2));

        Assert.Contains(c, x => x.Gravedad == GravedadLegalizacion.Error && x.Titulo == "El Diario no cuadra" && x.Detalle.Contains("asiento 70"));
        Assert.Contains(c, x => x.Gravedad == GravedadLegalizacion.Error && x.Titulo == "Las cuentas anuales tienen errores");
        Assert.Contains(c, x => x.Gravedad == GravedadLegalizacion.Aviso && x.Titulo == "Fuera de plazo desde el 30/04/2026");
    }

    [Fact]
    public void El_ejercicio_propuesto_es_el_ultimo_cerrado_en_plazo()
    {
        var ejercicios = new List<EjercicioCuentas> { new(9, 2026, false), new(8, 2025, true), new(7, 2024, true) };
        Assert.Equal(2025, ServicioLegalizacion.EjercicioPropuesto(ejercicios, 2026, new DateTime(2026, 2, 1)));
        // Pasado el plazo de 2025 sin cerrar 2026, se propone el anterior al de trabajo.
        Assert.Equal(2025, ServicioLegalizacion.EjercicioPropuesto(ejercicios, 2026, new DateTime(2026, 11, 23)));
        Assert.Equal(2026, ServicioLegalizacion.EjercicioPropuesto([new(9, 2026, true), new(8, 2025, true)], 2027, new DateTime(2027, 3, 1)));
    }
}
