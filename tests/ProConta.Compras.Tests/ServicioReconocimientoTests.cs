using System.Text;
using ProConta.Compras.Reconocimiento;
using static ProConta.Compras.Tests.Documentos;

namespace ProConta.Compras.Tests;

/// <summary>Orquestación: OCR solo sin texto nativo, divisas, confianza global, revisión, MD5 y concepto.</summary>
public sealed class ServicioReconocimientoTests : IDisposable
{
    private readonly string _pdf = Path.Combine(Path.GetTempPath(), $"compra-{Guid.NewGuid():N}.pdf");

    public ServicioReconocimientoTests() => File.WriteAllBytes(_pdf, Encoding.ASCII.GetBytes("%PDF-1.4 prueba inventada"));

    public void Dispose() => File.Delete(_pdf);

    private sealed class ExtractorFijo(DocumentoFacturaExtraido d) : IExtractorDocumentoFacturaCompra
    {
        public DocumentoFacturaExtraido Extraer(string rutaPdf) => d;
    }

    private sealed class OcrFijo(DocumentoFacturaExtraido d) : IExtractorOcrFacturaCompra
    {
        public int Llamadas { get; private set; }
        public Task<DocumentoFacturaExtraido> ExtraerAsync(string rutaPdf, CancellationToken cancelacion) { Llamadas++; return Task.FromResult(d); }
    }

    private sealed class CambioFijo(TipoCambioHistorico? c) : IProveedorTiposCambioHistoricos
    {
        public Task<TipoCambioHistorico?> ObtenerAsync(string o, string d, DateTime f, CancellationToken c2) => Task.FromResult(c);
    }

    private static readonly string Completa = $"""
        Emisor Ficticio S.L. NIF: {NifProveedor}
        Número de factura: FV260123
        Fecha de factura: 10/02/2026
        Base imponible 100,00
        Cuota IVA 21,00
        Total factura 121,00
        """;

    private static ContextoProcesamiento Ctx(bool ocr = false, int aceptadas = 0)
        => new() { Configuracion = new ConfiguracionReconocimiento { UsarOcr = ocr }, Reconocimiento = Contexto(), FacturasAceptadasProveedor = aceptadas };

    [Fact]
    public async Task No_llama_al_ocr_con_un_pdf_nativo_aunque_este_activo()
    {
        var ocr = new OcrFijo(Texto(Completa));
        var r = await new ServicioReconocimientoFacturaCompra(new ExtractorFijo(Texto(Completa)), ocr).ReconocerAsync(_pdf, Ctx(ocr: true));
        Assert.Equal(0, ocr.Llamadas);
        Assert.Equal("pdf-local", r.Fuente);
    }

    [Fact]
    public async Task Usa_el_ocr_solo_si_el_pdf_no_tiene_texto_nativo()
    {
        var ocr = new OcrFijo(Texto(Completa));
        var r = await new ServicioReconocimientoFacturaCompra(new ExtractorFijo(Texto(" ")), ocr).ReconocerAsync(_pdf, Ctx(ocr: true));
        Assert.Equal(1, ocr.Llamadas);
        Assert.Equal("ocr", r.Fuente);
        Assert.Equal(121m, r.Lectura.ImporteTotal);
    }

    [Fact]
    public async Task Sin_ocr_activo_un_escaneado_queda_para_revision_sin_inventar_datos()
    {
        var r = await new ServicioReconocimientoFacturaCompra(new ExtractorFijo(Texto(" "))).ReconocerAsync(_pdf, Ctx());
        Assert.True(r.RequiereRevision);
        Assert.Null(r.Lectura.ImporteTotal);
        Assert.Equal("", r.NumeroFactura);
    }

    [Fact]
    public async Task Factura_completa_y_cuadrada_no_pide_revision()
    {
        var r = await new ServicioReconocimientoFacturaCompra(new ExtractorFijo(Texto(Completa))).ReconocerAsync(_pdf, Ctx(aceptadas: 3));
        Assert.False(r.RequiereRevision);
        Assert.True(r.Confianza >= 0.9m);
        Assert.Equal("FV260123", r.NumeroFactura);
    }

    [Fact]
    public async Task Convierte_la_divisa_con_el_cambio_de_la_fecha()
    {
        var doc = Texto("Invoice # 991\nInvoice date 2026-02-10\nGrand total 35.00 USD");
        var cambio = new CambioFijo(new TipoCambioHistorico(new DateTime(2026, 2, 9), "USD", "EUR", 0.9215m));
        var r = await new ServicioReconocimientoFacturaCompra(new ExtractorFijo(doc), tiposCambio: cambio).ReconocerAsync(_pdf, Ctx());
        Assert.Equal(32.25m, r.Lectura.ImporteTotalEuros); // 35 × 0,9215 = 32,2525 → 32,25
        Assert.Equal(new DateTime(2026, 2, 9), r.Lectura.FechaTipoCambio);
    }

    [Fact]
    public async Task Sin_cambio_de_divisa_pide_revision_y_lo_explica()
    {
        var doc = Texto("Invoice # 991\nInvoice date 2026-02-10\nGrand total 35.00 USD");
        var r = await new ServicioReconocimientoFacturaCompra(new ExtractorFijo(doc), tiposCambio: new CambioFijo(null)).ReconocerAsync(_pdf, Ctx());
        Assert.True(r.RequiereRevision);
        Assert.Contains(r.Lectura.Advertencias, a => a.Contains("cambio oficial", StringComparison.Ordinal));
    }

    [Fact]
    public void Historial_aceptado_refuerza_la_confianza_con_limite()
    {
        var r = new ResultadoReconocimientoFacturaCompra { ConfianzaImportes = 0.8m, ConfianzaFecha = 0.8m, ConfianzaNumero = 0.8m, ConfianzaNif = 0.8m };
        Assert.Equal(0.8m, ServicioReconocimientoFacturaCompra.ConfianzaGlobal(r, Ctx(aceptadas: 1)));
        Assert.Equal(0.83m, ServicioReconocimientoFacturaCompra.ConfianzaGlobal(r, Ctx(aceptadas: 3)));
        Assert.Equal(0.88m, ServicioReconocimientoFacturaCompra.ConfianzaGlobal(r, Ctx(aceptadas: 50)));
    }

    [Fact]
    public void Md5_en_hexadecimal_minusculo_de_32_caracteres()
    {
        var md5 = ServicioReconocimientoFacturaCompra.CalcularMd5(_pdf);
        Assert.Matches("^[0-9a-f]{32}$", md5);
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(_pdf))).ToLowerInvariant(), md5);
    }

    [Theory]
    [InlineData("Ficticio", "A1", "Ficticio Fact: A1")]
    [InlineData("", "A1", "Fact: A1")]
    [InlineData("Ficticio", "", "Ficticio")]
    public void Concepto_contable(string proveedor, string numero, string esperado)
        => Assert.Equal(esperado, ServicioReconocimientoFacturaCompra.ComponerConcepto(proveedor, numero));

    [Fact]
    public void El_numero_registrado_solo_lleva_letras_y_digitos()
        => Assert.Equal("C26042692", ServicioReconocimientoFacturaCompra.LimpiarNumeroParaRegistro("C/26-042.692 "));

    [Theory]
    [InlineData(21, MagnitudColumnaIva.BaseImponible, 100, 100, 21, 121)]
    [InlineData(21, MagnitudColumnaIva.Total, 121, 100, 21, 121)]
    [InlineData(10, MagnitudColumnaIva.Cuota, 5, 50, 5, 55)]
    public void Calculadora_de_columna_de_iva(double tipo, MagnitudColumnaIva m, double valor, double b, double c, double t)
        => Assert.Equal(new ResultadoColumnaIva((decimal)b, (decimal)c, (decimal)t), CalculadoraColumnaIva.Calcular((decimal)tipo, m, (decimal)valor));
}
