using ProConta.Compras.Reconocimiento;
using static ProConta.Compras.Tests.Documentos;

namespace ProConta.Compras.Tests;

/// <summary>Pruebas unitarias del motor (lista «Pruebas obligatorias · Unitarias» de la especificación del reconocimiento).</summary>
public sealed class MotorReconocimientoTests
{
    [Fact]
    public void Pdf_sin_texto_pide_ocr()
    {
        var r = Reconocer(Texto("   \n  ·  "));
        Assert.True(r.RequiereOcr);
        Assert.Contains(r.Advertencias, a => a.Contains("OCR", StringComparison.Ordinal));
    }

    [Fact]
    public void Pdf_con_texto_nativo_no_pide_ocr()
        => Assert.False(Reconocer(Texto("Factura 2026/1/15 Total 121,00 €")).RequiereOcr);

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1,234.56", 1234.56)]
    [InlineData("-12,30", -12.30)]
    [InlineData("12,30-", -12.30)]
    [InlineData("€ 99.95", 99.95)]
    [InlineData("10,123456", 10.123456)]
    public void Interpreta_importes_europeos_anglosajones_negativos_y_con_simbolo(string texto, double esperado)
        => Assert.Equal((decimal)esperado, MotorReconocimientoFacturaCompra.InterpretarImporte(texto, permitirEntero: true));

    [Fact]
    public void Importe_entero_solo_en_contexto_monetario_y_fecha_con_puntos_no_es_importe()
    {
        Assert.Equal(150m, MotorReconocimientoFacturaCompra.InterpretarImporte("150 €", permitirEntero: true));
        Assert.Null(MotorReconocimientoFacturaCompra.InterpretarImporte("150"));
        Assert.Null(MotorReconocimientoFacturaCompra.InterpretarImporte("12.03.2026", permitirEntero: true));
    }

    [Fact]
    public void Desglose_con_dos_tipos_de_iva_cuadra_con_el_total()
    {
        var r = Reconocer(Texto("""
            FACTURA
            Fecha de factura: 10/02/2026
            IVA 21% 100,00 21,00
            IVA 10% 50,00 5,00
            Total factura 176,00
            """));
        Assert.Equal(150m, r.BaseImponible);
        Assert.Equal(26m, r.CuotaIva);
        Assert.Equal(176m, r.ImporteTotal);
        Assert.Equal([10m, 21m], r.DesglosesIva.Select(d => d.Tipo).ToArray());
        Assert.Equal(r.ImporteTotal, r.BaseImponible + r.CuotaIva);
    }

    [Fact]
    public void Reconciliacion_aritmetica_eleva_la_confianza_de_importes()
    {
        var r = Reconocer(Texto("Base 200,00\nIVA 42,00\nA pagar 242,00"));
        Assert.Equal(200m, r.BaseImponible);
        Assert.Equal(242m, r.ImporteTotal);
        Assert.Equal(0.97m, r.ConfianzaImportes);
    }

    [Fact]
    public void Retencion_de_irpf_se_reconoce_con_su_porcentaje_e_importe()
    {
        var r = Reconocer(Texto("Base imponible 100,00\nIVA 21% 21,00\nRetención IRPF 15% 15,00\nTotal factura 106,00"));
        Assert.Equal(15m, r.TipoRetencion);
        Assert.Equal(15m, r.ImporteRetencion);
    }

    [Fact]
    public void Inversion_del_sujeto_pasivo_con_cuota_cero()
    {
        var r = Reconocer(Texto("Operación con inversión del sujeto pasivo\nBase imponible 500,00\nTotal factura 500,00"));
        Assert.True(r.InversionSujetoPasivo);
        Assert.Equal(0m, r.CuotaIva);
    }

    [Theory]
    [InlineData("10/03/26", 2026, 3, 10)]
    [InlineData("10.03.2026", 2026, 3, 10)]
    [InlineData("10-03-2026", 2026, 3, 10)]
    [InlineData("2026-03-10", 2026, 3, 10)]
    [InlineData("03-feb-2026", 2026, 2, 3)]
    [InlineData("9 de enero de 2026", 2026, 1, 9)]
    public void Interpreta_formatos_de_fecha(string texto, int anio, int mes, int dia)
        => Assert.Equal(new DateTime(anio, mes, dia), MotorReconocimientoFacturaCompra.InterpretarFecha(texto));

    [Fact]
    public void La_fecha_de_factura_gana_al_vencimiento()
    {
        var r = Reconocer(Texto("Fecha de vencimiento: 30/04/2026\nFecha de factura: 15/03/2026\nTotal 121,00"));
        Assert.Equal(new DateTime(2026, 3, 15), r.FechaFactura);
    }

    [Fact]
    public void Numero_tras_etiqueta_partido_en_dos_tokens_y_limpio()
    {
        var r = Reconocer(Texto("Número de factura: 25F 02355\nFecha factura 01/02/2026\nTotal 10,00"));
        Assert.Equal("25F02355", r.NumeroFactura);
    }

    [Fact]
    public void Numero_por_geometria_junto_a_la_etiqueta()
    {
        var d = ConPalabras(@"X:\Datos\Soc\2026\Proveedores\Ficticio\x.pdf",
            ("Factura", 50, 700), ("Nº", 100, 700), ("C/26042692", 140, 700));
        var r = Reconocer(d);
        Assert.Equal("C26042692", ServicioReconocimientoFacturaCompra.LimpiarNumeroParaRegistro(r.NumeroFactura));
    }

    [Fact]
    public void Comparacion_con_el_historico_equivale_a_cadenas_parecidas()
    {
        Assert.True(MotorReconocimientoFacturaCompra.CadenasParecidasLegacy("FV260123", "FV260098"));
        Assert.False(MotorReconocimientoFacturaCompra.CadenasParecidasLegacy("ABC", "FV2600981"));
        var r = Reconocer(Texto("Albarán 77\nRef FV260123\nTotal 50,00"), Contexto(numeros: ["FV260098", "FV260045"]));
        Assert.Equal("FV260123", r.NumeroFactura);
    }

    [Fact]
    public void Excluye_el_nif_de_la_sociedad_receptora()
    {
        var r = Reconocer(Texto($"Cliente: Sociedad de prueba NIF: {NifSociedad}\nEmisor Ficticio S.L. NIF: {NifProveedor}\nTotal 10,00"));
        Assert.Equal(NifProveedor, r.NifProveedor);
    }

    [Fact]
    public void Valida_nif_espanol_y_variante_es()
    {
        Assert.True(MotorReconocimientoFacturaCompra.EsNifEspanolValido(NifProveedor));
        Assert.True(MotorReconocimientoFacturaCompra.EsNifEspanolValido("ES" + NifProveedor));
        Assert.True(MotorReconocimientoFacturaCompra.EsNifPlausible("ES" + NifProveedor));
        Assert.False(MotorReconocimientoFacturaCompra.EsNifPlausible("12345678"));
        Assert.Equal(NifProveedor, MotorReconocimientoFacturaCompra.CompactarNif("ES-" + NifProveedor)[2..]);
    }

    [Fact]
    public void Sin_nif_fiable_se_asigna_el_del_proveedor_de_la_carpeta()
    {
        var alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Ficticio"] = NifProveedor };
        var r = Reconocer(Texto("Factura sin identificador\nTotal 10,00"), Contexto(alias: alias));
        Assert.Equal(NifProveedor, r.NifProveedor);
        Assert.True(r.NifAsignadoDesdeProveedor);
        Assert.Equal(0.68m, r.ConfianzaNif);
    }

    [Fact]
    public void Nif_documental_distinto_del_registrado_se_conserva_y_avisa()
    {
        var alias = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Ficticio"] = "A64728918" };
        var r = Reconocer(Texto($"Emisor Ficticio S.L. NIF: {NifProveedor}\nTotal 10,00"), Contexto(alias: alias));
        Assert.Equal(NifProveedor, r.NifProveedor);
        Assert.Contains(r.Advertencias, a => a.Contains("difiere", StringComparison.Ordinal));
    }

    [Fact]
    public void Pdf_con_dos_facturas_se_limita_a_la_primera_y_avisa()
    {
        var r = Reconocer(Texto("Número de factura: AA1001\nTotal 10,00\nNúmero de factura: AA1002\nTotal 20,00"));
        Assert.True(r.PosiblesMultiplesFacturas);
        Assert.Equal("AA1001", r.NumeroFactura);
    }

    [Fact]
    public void Vies_fuerza_base_igual_a_total_iva_cero_y_aviso()
    {
        var d = Texto("Invoice number: EU12345\nVAT 21% 100,00 21,00\nTotal factura 121,00", @"X:\Datos\Soc\2026\Proveedores\Eminza\f.pdf");
        var r = Reconocer(d, Contexto(vies: true));
        Assert.Equal(121m, r.BaseImponible);
        Assert.Equal(0m, r.CuotaIva);
        Assert.Contains(r.Advertencias, a => a.Contains("VIES", StringComparison.Ordinal));
    }

    [Fact]
    public void Detecta_divisa_extranjera_en_las_lineas_de_total()
    {
        var r = Reconocer(Texto("Invoice # 991\nTotal USD 35.00"));
        Assert.Equal("USD", r.Divisa);
        Assert.True(r.RequiereConversionDivisa);
    }
}
