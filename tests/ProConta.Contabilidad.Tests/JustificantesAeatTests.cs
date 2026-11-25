using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using static ProConta.Contabilidad.Tests.JustificanteSintetico;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Lectura del PDF justificante de la AEAT y comparación con lo calculado (contabilidad/modelos-tributarios/presentacion 2026-11-25), sin BD:
/// el lector con justificantes inventados (los reales de la carpeta del proyecto no van al repositorio), las casillas que se comparan, el
/// justificante de otro periodo, las casillas guardadas en PresentacionesAeat y el menú del periodo. Datos sintéticos.
/// </summary>
public sealed class JustificantesAeatTests
{
    private static readonly DateTime Hoy = new(2026, 11, 25);

    [Fact]
    public void Lee_la_cabecera_y_las_casillas_del_justificante_de_un_303()
    {
        var pdf = Pdf(Cabecera("303", "3030000000123", "CSVSINTETICO0001", importe: 1234.56m),
            Hoja(2026, "3T", "B12345674", ("27", "2.384,87"), ("45", "1.150,31"), ("46", "1.234,56"), ("66", "1.234,56"), ("71", "-27,02")));
        var j = LectorJustificanteAeat.Leer(pdf);
        Assert.NotNull(j);
        Assert.Equal(("303", 2026, "3T", "B12345674"), (j.Modelo, j.Ejercicio, j.Periodo, j.Nif));
        Assert.Equal(("3030000000123", "CSVSINTETICO0001", "2026303" + "99999999X"), (j.Justificante, j.Csv, j.Expediente));
        Assert.Equal(new DateTime(2026, 10, 20, 10, 15, 0), j.FechaHora);
        Assert.Equal(("A00000000", "GESTORIA SINTETICA SL", "A ingresar", 1234.56m), (j.NifPresentador, j.Presentador, j.TipoResultado, j.ImporteAIngresar));
        Assert.Equal(2384.87m, j.Casilla("27"));
        Assert.Equal(-27.02m, j.Casilla("71"));
        Assert.Null(j.Casilla("69"));                                      // vacía en el justificante
        Assert.Equal(1234.56m, ComparacionJustificante.Resultado(j));      // el 303 guarda la 66
    }

    [Fact]
    public void Lee_el_formato_antiguo_y_los_importes_partidos_por_un_espacio()
    {
        // Justificantes de antes de 2023: «MODELO 390», «realizada el» sin dos puntos y los miles con espacio («6 642,80» son dos palabras).
        var cabecera = new List<Texto>
        {
            D(397, 726, "MODELO 390"), D(77, 680, "Presentación realizada el 30-01-2026 a las 10:54:25"),
            D(77, 639, "Código Seguro de Verificación: CSVSINTETICO0390"), D(77, 414, "Número de justificante: 3900000000001"),
        };
        var hoja = new List<Texto> { D(422, 721, "2025"), D(67, 627, "B12345674"), C(460, 501.5, "86"), D(521, 500, "6"), D(527, 500, "642,80"), C(460, 481.5, "34"), D(521, 480, "15.000,00") };
        var j = LectorJustificanteAeat.Leer(Pdf(cabecera, hoja));
        Assert.NotNull(j);
        Assert.Equal(("390", 2025, "0A"), (j.Modelo, j.Ejercicio, j.Periodo));   // anual: periodo 0A
        Assert.Equal(new DateTime(2026, 1, 30, 10, 54, 25), j.FechaHora);
        Assert.Equal(6642.80m, j.Casilla("86"));
        Assert.Equal(15000m, j.Casilla("34"));
        Assert.Equal(0m, j.ImporteAIngresar);
    }

    [Fact]
    public void Un_PDF_que_no_es_un_justificante_no_se_lee()
    {
        Assert.Null(LectorJustificanteAeat.Leer(Pdf([D(60, 700, "Requerimiento de información"), D(60, 680, "Modelo 303 ejercicio 2026")])));
        Assert.Null(LectorJustificanteAeat.Leer("no es un pdf"u8.ToArray()));
        Assert.Equal(-1234.5m, LectorJustificanteAeat.Importe("-1.234,5"));
        Assert.Null(LectorJustificanteAeat.Importe("12,345"));
        Assert.Null(LectorJustificanteAeat.Importe("B12345674"));
    }

    [Fact]
    public void Las_casillas_que_difieren_dicen_lo_presentado_y_lo_calculado()
    {
        var presentadas = new Dictionary<string, decimal> { ["27"] = 2384.87m, ["45"] = 1150.31m, ["46"] = 1234.56m, ["66"] = 1234.56m, ["69"] = 1234.56m, ["71"] = 0m };
        var iguales = new Dictionary<string, decimal> { ["27"] = 2384.87m, ["45"] = 1150.31m, ["46"] = 1234.56m, ["66"] = 1234.56m, ["69"] = 1234.56m };
        Assert.Empty(ComparacionJustificante.Diferencias("303", presentadas, iguales));
        // La 71 no se compara (resta lo ingresado en una complementaria); una casilla vacía en el justificante cuenta como 0.
        var otras = new Dictionary<string, decimal>(iguales) { ["45"] = 1100.31m, ["46"] = 1284.56m, ["66"] = 1284.56m, ["69"] = 1284.56m, ["71"] = 99m };
        var d = ComparacionJustificante.Diferencias("303", presentadas, otras);
        Assert.Equal(["45", "46", "66", "69"], d.Select(x => x.Casilla));
        Assert.Equal((1150.31m, 1100.31m), (d[0].Presentado, d[0].Calculado));
        Assert.Contains("casilla 45 (total a deducir): presentado 1.150,31 €, calculado 1.100,31 €", d[0].Texto);
        Assert.Single(ComparacionJustificante.Diferencias("111", new Dictionary<string, decimal> { ["28"] = 10m }, new Dictionary<string, decimal> { ["03"] = 0m, ["09"] = 0m, ["28"] = 12m }));
        Assert.Empty(ComparacionJustificante.Diferencias("349", new Dictionary<string, decimal> { ["1"] = 2m, ["02"] = 66.75m }, new Dictionary<string, decimal> { ["01"] = 2m, ["02"] = 66.75m }));
        Assert.Equal(["303", "111", "115", "349", "390", "200"], ComparacionJustificante.Modelos);
        Assert.False(ComparacionJustificante.Lee("347"));
    }

    [Fact]
    public void El_justificante_de_otro_periodo_o_de_otra_empresa_no_se_registra()
    {
        var j = LectorJustificanteAeat.Leer(Pdf(Cabecera("303", "3030000000123", "CSVSINTETICO0001"), Hoja(2026, "3T", "B12345674", ("27", "1,00"))))!;
        Assert.Null(ServicioModelosTributarios.ErrorJustificante(j, "303", 2026, "3T", "B12345674"));
        Assert.Contains("aquí va el del 303 del 2T de 2026", ServicioModelosTributarios.ErrorJustificante(j, "303", 2026, "2T", "B12345674"));
        Assert.Contains("del 303 del 3T de 2026", ServicioModelosTributarios.ErrorJustificante(j, "111", 2026, "3T", "B12345674"));
        Assert.Contains("otra empresa", ServicioModelosTributarios.ErrorJustificante(j, "303", 2026, "3T", "B99999999"));
    }

    [Fact]
    public void Las_casillas_del_justificante_se_guardan_en_texto_y_permiten_comparar_lo_de_la_gestoria()
    {
        var casillas = new Dictionary<string, decimal> { ["27"] = 2384.87m, ["71"] = -27.02m, ["01586"] = 3838.5m };
        var texto = PresentacionGuardada.EscribirCasillas(casillas);
        Assert.Equal("27=2384.87;71=-27.02;01586=3838.5", texto);
        Assert.Equal(casillas, PresentacionGuardada.LeerCasillas(texto));
        Assert.Empty(PresentacionGuardada.LeerCasillas(null));

        var sinJustificante = Presentada("111", 0m, PresentacionGuardada.OrigenAeat, null);
        Assert.False(sinJustificante.DistintoDe(100m));                    // la consulta no da el importe: no se compara
        var conJustificante = Presentada("111", 2860.09m, PresentacionGuardada.OrigenAeat, "28=2860.09");
        Assert.True(conJustificante.ConImporte);
        Assert.False(conJustificante.DistintoDe(2860.09m));
        Assert.True(conJustificante.DistintoDe(2900m));
    }

    [Fact]
    public void El_menu_ofrece_registrar_o_leer_el_justificante_segun_lo_presentado()
    {
        AccionPeriodo? R(PeriodoModelo p) => AccionesPeriodos.De([p], Hoy, true).SingleOrDefault(x => x.Accion == AccionModelo.RegistrarExterna);
        Assert.Equal("Registrar presentado fuera del programa…", R(Periodo("303", "3T"))!.Texto);
        Assert.True(R(Periodo("303", "3T"))!.Disponible);
        var deLaGestoria = R(Periodo("111", "3T", Presentada("111", 0m, PresentacionGuardada.OrigenAeat, null)))!;
        Assert.Equal(("Leer el justificante (PDF)…", true), (deLaGestoria.Texto, deLaGestoria.Disponible));
        Assert.Equal("Ya consta presentado", R(Periodo("111", "3T", Presentada("111", 10m, PresentacionGuardada.OrigenAeat, "28=10")))!.NoSePuede);
        Assert.Equal("Ya consta presentado", R(Periodo("303", "3T", Presentada("303", 10m, PresentacionGuardada.OrigenPrograma, null)))!.NoSePuede);
        Assert.Equal("Registrar presentado fuera del programa…", R(Periodo("347", "0A"))!.Texto);   // informativa sin lectura: a mano, como antes
        Assert.Null(R(Periodo("202", "1P")));
    }

    [Fact]
    public void El_periodo_desactualizado_dice_que_casillas_difieren()
    {
        var u = Presentada("303", 1234.56m, PresentacionGuardada.OrigenAeat, "27=2384.87;45=1150.31");
        var p = Periodo("303", "3T", u, Estado303.Desactualizado) with { Diferencias = [new DiferenciaCasilla("45", "total a deducir", 1150.31m, 1100.31m)] };
        Assert.Equal(EstadoPeriodoModelo.Desactualizado, p.Estado(Hoy));
        Assert.Contains("casilla 45 (total a deducir): presentado 1.150,31 €, calculado 1.100,31 €", p.AyudaEstado(Hoy));
        var bien = Periodo("303", "3T", u, Estado303.Presentado) with { Diferencias = [] };
        Assert.Contains("su justificante coincide", bien.AyudaEstado(Hoy));
    }

    private static PresentacionGuardada Presentada(string modelo, decimal resultado, string origen, string? casillas) =>
        new(1, "T01", modelo, 2026, "3T", "PRUEBAS", new DateTime(2026, 10, 15, 10, 0, 0), "CSVSINTETICO0001", null, "1110000000003", null, null, resultado, resultado,
            null, null, "operador", null, null, null, origen, casillas);

    private static PeriodoModelo Periodo(string modelo, string periodo, PresentacionGuardada? ultima = null, Estado303 estado = Estado303.SinPresentar)
    {
        var (desde, hasta) = PlazosModelos.Plazo(modelo, 2026, periodo);
        return new PeriodoModelo(modelo, 2026, periodo, desde, hasta, ultima is null ? estado : estado == Estado303.SinPresentar ? Estado303.Presentado : estado, 100m, null, ultima,
            ultima is null ? [] : [ultima], [], []);
    }
}
