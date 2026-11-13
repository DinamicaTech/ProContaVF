using ProConta.Norma43.Asignacion;

namespace ProConta.Norma43.Tests;

/// <summary>Reglas de las palabras clave bancarias, sin BD (norma43/asignacion-cuentas).</summary>
public sealed class ReglasPalabrasClaveTests
{
    private static LineaN43ParaAsignar Cargo(string concepto, string entidad = "0049", string? ref1 = null) => new(1, entidad, "D", 10m, concepto, null, ref1, null);
    private static LineaN43ParaAsignar Abono(string concepto, string entidad = "0049") => new(2, entidad, "H", 10m, concepto, null, null, null);
    // Sentido de la palabra clave desde la 572: H = cargos de la línea, D = abonos.
    private static PalabraClaveN43 P(int id, string palabra, string? palabra2 = null, string? entidad = "0049", string? sentido = "H", int? cuenta = 100, int? especial = null) =>
        new(id, entidad, sentido, palabra, palabra2, cuenta, especial);

    [Theory]
    [InlineData("H", "D")]
    [InlineData("D", "H")]
    [InlineData("1", "D")]
    [InlineData("2", "H")]
    [InlineData(null, null)]
    [InlineData("", null)]
    public void El_sentido_de_la_palabra_clave_es_el_de_la_572(string? palabra, string? linea) => Assert.Equal(linea, ReglasPalabrasClave.SentidoDeLinea(palabra));

    [Fact]
    public void Normaliza_mayusculas_acentos_espacios_signos_y_mojibake()
    {
        Assert.Equal("RECIBOCAMIONFICTICIOSL", ReglasPalabrasClave.Normalizar("Recibo  camión ficticio, S.L."));
        Assert.Equal("CAMION", ReglasPalabrasClave.Normalizar("CAMIÃ“N"));
        Assert.Equal("", ReglasPalabrasClave.Normalizar("  .-  "));
    }

    [Fact]
    public void Encaja_por_entidad_sentido_y_texto()
    {
        var p = P(1, "luz ficticia");
        Assert.True(ReglasPalabrasClave.Encaja(p, Cargo("RECIBO LUZ   FICTICIA SA")));
        Assert.False(ReglasPalabrasClave.Encaja(p, Abono("RECIBO LUZ FICTICIA SA")));
        Assert.False(ReglasPalabrasClave.Encaja(p, Cargo("RECIBO LUZ FICTICIA SA", entidad: "2100")));
        Assert.True(ReglasPalabrasClave.Encaja(P(1, "luz ficticia", entidad: null, sentido: null), Abono("RECIBO LUZ FICTICIA SA", entidad: "2100")));
        Assert.True(ReglasPalabrasClave.Encaja(P(1, "REF998877"), Cargo("RECIBO", ref1: "REF998877")));
    }

    [Fact]
    public void La_segunda_palabra_es_un_Y_logico()
    {
        var p = P(1, "transferencia", "inventado");
        Assert.True(ReglasPalabrasClave.Encaja(p, Cargo("TRANSFERENCIA A PROVEEDOR INVENTADO")));
        Assert.False(ReglasPalabrasClave.Encaja(p, Cargo("TRANSFERENCIA A OTRO PROVEEDOR")));
    }

    [Fact]
    public void Gana_la_mas_concreta()
    {
        var linea = Cargo("TRANSFERENCIA A PROVEEDOR INVENTADO SL");
        var generica = P(1, "transferencia", cuenta: 100);
        var conSegunda = P(2, "transferencia", "inventado", cuenta: 200);
        var larga = P(3, "proveedor inventado", cuenta: 300);
        var sinEntidad = P(4, "transferencia", "inventado", entidad: null, cuenta: 400);
        Assert.Equal(2, ReglasPalabrasClave.Elegir(linea, [generica, conSegunda, larga, sinEntidad]).Palabra!.Id);
        Assert.Equal(3, ReglasPalabrasClave.Elegir(linea, [generica, larga]).Palabra!.Id);
        Assert.Equal(ResultadoBusqueda.SinPalabra, ReglasPalabrasClave.Elegir(Cargo("OTRA COSA"), [generica]).Resultado);
    }

    [Fact]
    public void Un_empate_con_cuentas_distintas_es_ambiguo_y_con_la_misma_cuenta_no()
    {
        var linea = Cargo("RECIBO ALFA BETA");
        var e = ReglasPalabrasClave.Elegir(linea, [P(1, "alfa", cuenta: 100), P(2, "beta", cuenta: 200)]);
        Assert.Equal(ResultadoBusqueda.Ambigua, e.Resultado);
        Assert.Null(e.Palabra);
        Assert.Equal(2, e.Empatadas.Count);
        var misma = ReglasPalabrasClave.Elegir(linea, [P(2, "beta", cuenta: 100), P(1, "alfa", cuenta: 100)]);
        Assert.Equal(ResultadoBusqueda.Encontrada, misma.Resultado);
        Assert.Equal(1, misma.Palabra!.Id);
    }

    [Fact]
    public void Una_palabra_clave_vacia_o_solo_con_signos_no_encaja_nunca() =>
        Assert.False(ReglasPalabrasClave.Encaja(P(1, " - "), Cargo("CUALQUIER COSA")));

    [Fact]
    public void Las_remesas_y_las_palabras_sin_cuenta_no_ponen_cuenta()
    {
        var remesa = new AsignacionLineaN43(1, ReglasPalabrasClave.Elegir(Abono("REMESA RECIBOS"), [P(1, "remesa", sentido: "D", especial: ReglasPalabrasClave.EspecialRemesa)]));
        var sinCuenta = new AsignacionLineaN43(2, ReglasPalabrasClave.Elegir(Cargo("CUOTA NUEVA"), [P(2, "cuota nueva", cuenta: null)]));
        var normal = new AsignacionLineaN43(3, ReglasPalabrasClave.Elegir(Cargo("CUOTA NUEVA"), [P(3, "cuota nueva", cuenta: 100)]));
        var r = new ResultadoAsignacionN43([remesa, sinCuenta, normal]);
        Assert.Equal((1, 1, 1, 3), (r.Asignadas, r.Remesas, r.PalabraSinCuenta, r.Revisadas));
    }

    [Fact]
    public void El_importe_firmado_es_positivo_en_los_cargos_y_negativo_en_los_abonos()
    {
        Assert.Equal(10m, AsignadorCuentasN43.ImporteFirmado(Cargo("X")));
        Assert.Equal(-10m, AsignadorCuentasN43.ImporteFirmado(Abono("X")));
    }
}
