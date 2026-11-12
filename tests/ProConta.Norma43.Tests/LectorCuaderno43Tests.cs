using System.Text;
using ProConta.Norma43.Cuaderno43;
using ProConta.Norma43.Importacion;

namespace ProConta.Norma43.Tests;

/// <summary>Lector y validación del cuaderno 43, sin BD, con ficheros inventados.</summary>
public sealed class LectorCuaderno43Tests
{
    private static readonly DateTime D1 = new(2026, 11, 2);
    private static readonly DateTime D2 = new(2026, 11, 3);

    private static FicheroSintetico Basico() => new FicheroSintetico().Cuenta("0049", "1500", "0012345678", new(2026, 11, 1), new(2026, 11, 30), 1000m, "EMPRESA FICTICIA",
        new Mov(D1, 120.50m, true, "RECIBO LUZ ELECTRICA FICTICIA SA"),
        new Mov(D2, 2500m, false, "TRANSFERENCIA DE CLIENTE INVENTADO SL CON UN CONCEPTO LARGO"));

    [Fact]
    public void Lee_un_extracto_cuadrado_con_sus_movimientos_y_conceptos()
    {
        var f = LectorCuaderno43.Leer(Basico().Bytes());
        Assert.True(f.EsValido, string.Join(" ", f.Incidencias.Select(i => i.Texto)));
        var e = Assert.Single(f.Extractos);
        Assert.Equal(("0049", "1500", "0012345678", "EUR"), (e.Entidad, e.Oficina, e.Cuenta, e.Divisa));
        Assert.Equal(new DateTime(2026, 11, 1), e.FechaInicio);
        Assert.Equal(1000m, e.SaldoInicialFirmado);
        Assert.Equal(3379.50m, e.SaldoFinal);
        Assert.Equal(2, e.Movimientos.Count);
        var cargo = e.Movimientos[0];
        Assert.Equal(("D", 120.50m, -120.50m), (cargo.Sentido, cargo.Importe, cargo.ImporteFirmado));
        Assert.Equal("RECIBO LUZ ELECTRICA FICTICIA SA", cargo.ConceptoCompleto);
        Assert.Equal("TRANSFERENCIA DE CLIENTE INVENTADO SL CON UN CONCEPTO LARGO", e.Movimientos[1].ConceptoCompleto);
        Assert.Equal(7, f.RegistrosLeidos);
        Assert.Equal(6, f.RegistrosDeclarados);
        Assert.Empty(f.Incidencias);
    }

    [Fact]
    public void Un_saldo_final_que_no_cuadra_rechaza_el_fichero()
    {
        var f = LectorCuaderno43.Leer(new FicheroSintetico { AjusteSaldoFinal = 0.01m }.Cuenta("0049", "1500", "0012345678", D1, D2, 10m, "X", new Mov(D1, 5m, true, "A")).Bytes());
        Assert.False(f.EsValido);
        Assert.Contains(f.Errores, i => i.Codigo == "N43.DescuadreSaldo");
    }

    [Fact]
    public void Un_numero_de_cargos_que_no_cuadra_rechaza_el_fichero()
    {
        var f = LectorCuaderno43.Leer(new FicheroSintetico { AjusteNumeroCargos = 1 }.Cuenta("0049", "1500", "0012345678", D1, D2, 10m, "X", new Mov(D1, 5m, true, "A")).Bytes());
        Assert.Contains(f.Errores, i => i.Codigo == "N43.DescuadreNumero");
    }

    [Fact]
    public void Un_fichero_cortado_sin_final_de_cuenta_se_rechaza()
    {
        var f = LectorCuaderno43.Leer(new FicheroSintetico { SinCierre = true }.Cuenta("0049", "1500", "0012345678", D1, D2, 10m, "X", new Mov(D1, 5m, true, "A")).Bytes());
        Assert.Contains(f.Errores, i => i.Codigo == "N43.SinCierre");
    }

    [Fact]
    public void Un_fichero_que_no_es_norma43_se_rechaza_con_explicacion()
    {
        var f = LectorCuaderno43.Leer(Encoding.ASCII.GetBytes("Fecha;Concepto;Importe\r\n02/11/2026;Recibo;-12,50\r\n"));
        Assert.False(f.EsValido);
        Assert.Contains(f.Errores, i => i.Codigo is "N43.TipoRegistro" or "N43.Vacio");
        Assert.False(LectorCuaderno43.Parece(Encoding.ASCII.GetBytes("Fecha;Concepto;Importe")));
        Assert.True(LectorCuaderno43.Parece(Basico().Bytes()));
    }

    [Fact]
    public void Acepta_lineas_sin_espacios_finales_y_saltos_de_linea_unix()
    {
        var texto = string.Join("\n", Basico().Registros().Select(r => r.TrimEnd()));
        var f = LectorCuaderno43.Leer(Encoding.ASCII.GetBytes(texto));
        Assert.True(f.EsValido, string.Join(" ", f.Incidencias.Select(i => i.Texto)));
        Assert.Equal(2, f.NumeroMovimientos);
    }

    [Fact]
    public void Lee_windows1252_y_utf8()
    {
        var fichero = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", D1, D2, 0m, "CAÑAS Y ÑU", new Mov(D1, 1m, false, "ABONO DE MUÑOZ"));
        var latin = LectorCuaderno43.Leer(fichero.Bytes(Encoding.Latin1));
        Assert.Equal("WINDOWS-1252", latin.Codificacion);
        Assert.Equal("ABONO DE MUÑOZ", latin.Extractos[0].Movimientos[0].ConceptoCompleto);
        var utf8 = LectorCuaderno43.Leer(fichero.Bytes(new UTF8Encoding(false)));
        Assert.Equal("UTF-8", utf8.Codificacion);
        Assert.Equal("CAÑAS Y ÑU", utf8.Extractos[0].Nombre);
    }

    [Fact]
    public void Dos_movimientos_identicos_del_mismo_fichero_se_conservan_los_dos_con_la_misma_clave()
    {
        var f = LectorCuaderno43.Leer(new FicheroSintetico().Cuenta("0049", "1500", "0012345678", D1, D2, 0m, "X",
            new Mov(D1, 3.20m, true, "CAFETERIA"), new Mov(D1, 3.20m, true, "CAFETERIA")).Bytes());
        Assert.True(f.EsValido);
        var m = f.Extractos[0].Movimientos;
        Assert.Equal(2, m.Count);
        Assert.Equal(ServicioImportacionN43.ClaveMovimiento(m[0]), ServicioImportacionN43.ClaveMovimiento(m[1]));
    }

    [Fact]
    public void Un_saldo_inicial_deudor_va_en_negativo()
    {
        var f = LectorCuaderno43.Leer(new FicheroSintetico().Cuenta("0049", "1500", "0012345678", D1, D2, -50m, "X", new Mov(D1, 80m, false, "ABONO")).Bytes());
        Assert.True(f.EsValido, string.Join(" ", f.Incidencias.Select(i => i.Texto)));
        Assert.Equal(("D", -50m, 30m), (f.Extractos[0].SentidoSaldoInicial, f.Extractos[0].SaldoInicialFirmado, f.Extractos[0].SaldoFinal!.Value));
    }

    [Theory]
    [InlineData("2100", "0418", "0200051332", "ES9121000418450200051332")]
    [InlineData("0049", "1500", "0012345678", null)]
    public void Calcula_el_iban_espanol_desde_la_cuenta_del_fichero(string entidad, string oficina, string cuenta, string? esperado)
    {
        var iban = LectorCuaderno43.Iban(entidad, oficina, cuenta);
        Assert.NotNull(iban);
        Assert.Equal(24, iban!.Length);
        Assert.StartsWith("ES", iban);
        if (esperado is not null) Assert.Equal(esperado, iban);
        // El control del IBAN es correcto: el número reordenado da resto 1 módulo 97.
        var reordenado = iban[4..] + "1428" + iban[2..4];
        var resto = reordenado.Aggregate(0, (r, c) => (r * 10 + (c - '0')) % 97);
        Assert.Equal(1, resto);
    }

    [Fact]
    public void La_clave_de_contenido_ignora_espacios_y_ceros_a_la_izquierda()
    {
        Assert.Equal(ServicioImportacionN43.ClaveMovimiento(D1, D1, "D", 1m, "02", "099", "0000000123", " AB", null),
            ServicioImportacionN43.ClaveMovimiento(D1, D1, "D", 1.00m, "2", "99", "123", "AB", ""));
    }
}
