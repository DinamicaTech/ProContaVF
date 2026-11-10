using System.Net;
using System.Net.Http;
using ProConta.Compras.Facturas;
using ProConta.Compras.Integraciones;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Tests;

/// <summary>Lógica sin BD que usa la pantalla de facturas recibidas: comparación del modo «Probar», confianza del log, listas y cambio.</summary>
public sealed class PantallaComprasTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);

    private static FacturaCompra Factura(string numero = "FV1", string? nif = "B61465332", DateTime? fecha = null, decimal? total = 121m) => new()
    {
        Ejercicio = Ctx, RutaPdf = @"X:\f.pdf", Md5 = "0123456789abcdef0123456789abcdef", NumeroFactura = numero, NifProveedor = nif,
        Fecha = fecha ?? new DateTime(2026, 2, 10), BaseImponible = 100m, CuotaIva = 21m, ImporteTotal = total
    };

    [Fact]
    public void Probar_compara_el_numero_limpio_el_nif_compacto_la_fecha_por_dia_y_los_importes_al_centimo()
    {
        var grabada = Factura("FV-2026/1", "B-61465332", new DateTime(2026, 2, 10, 0, 0, 0));
        var leida = Factura("FV20261", "B61465332", new DateTime(2026, 2, 10, 15, 0, 0), 121.01m);
        var c = ServicioFacturasCompra.Comparar(grabada, leida).ToDictionary(x => x.Campo);
        Assert.True(c["Número"].Igual);
        Assert.True(c["NIF"].Igual);
        Assert.True(c["Fecha"].Igual);
        Assert.True(c["Base"].Igual);
        Assert.False(c["Total"].Igual);
        Assert.Equal(("121,00", "121,01"), (c["Total"].Grabado, c["Total"].Leido));
    }

    [Fact]
    public void Probar_marca_los_campos_que_no_estaban_grabados()
    {
        var c = ServicioFacturasCompra.Comparar(Factura(nif: null), Factura()).Single(x => x.Campo == "NIF");
        Assert.True(c.SinGrabar);
        Assert.False(c.Igual);
    }

    [Fact]
    public void La_confianza_de_cada_campo_sale_del_log_del_reconocimiento()
    {
        const string log = """{"version":1,"confianza":0.87,"fuenteElegida":"pdf-local","candidato":{"ConfianzaNif":1.0,"ConfianzaNumero":0.6,"ConfianzaFecha":0.0,"ConfianzaImportes":0.95},"advertencias":["No he encontrado la fecha."]}""";
        var c = ConfianzaCampos.DesdeLog(log);
        Assert.Equal((1.0m, 0.6m, 0.0m, 0.95m, 0.87m), (c.Nif!.Value, c.Numero!.Value, c.Fecha!.Value, c.Importes!.Value, c.Global!.Value));
        Assert.Equal(["No he encontrado la fecha."], c.Avisos);
        Assert.Equal("pdf-local", c.Fuente);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[1,2]")]
    public void Sin_log_o_con_un_log_que_no_se_entiende_no_hay_confianza(string? log) =>
        Assert.Equal(ConfianzaCampos.Ninguna, ConfianzaCampos.DesdeLog(log));

    [Fact]
    public void Con_suplido_cuadra_base_mas_cuota_menos_retencion_mas_suplido()
    {
        var f = Factura(total: 125m);
        f.Suplido = 4m;
        Assert.True(f.Cuadra);
        Assert.True(f.Lista);
        f.ImporteTotal = 121m;
        Assert.Equal(["importes que cuadren"], f.Pendiente());
    }

    [Fact]
    public void Lo_que_falta_para_estar_lista()
    {
        var f = new FacturaCompra { Ejercicio = Ctx, RutaPdf = @"X:\f.pdf", Md5 = "0123456789abcdef0123456789abcdef" };
        Assert.Equal(["proveedor", "número", "fecha", "total"], f.Pendiente());
    }

    [Fact]
    public void Validar_rechaza_fechas_lejos_del_ejercicio_y_signos_distintos()
    {
        var f = Factura(fecha: new DateTime(2019, 1, 1));
        f.BaseImponible = -100m;
        Assert.Equal(["Compras.FechaFuera", "Compras.Signos"], f.Validar().Errores.Select(e => e.Codigo));
    }

    [Fact]
    public void La_fila_esta_lista_con_proveedor_numero_fecha_e_importes_que_cuadran()
    {
        var fila = new FilaFacturaCompra { Nif = "B61465332", NumeroFactura = "FV1", Fecha = new DateTime(2026, 1, 1), BaseImponible = 100m, CuotaIva = 21m, ImporteTotal = 121m };
        Assert.True(fila.Lista);
        Assert.False(fila.Vacia);
        Assert.True(new FilaFacturaCompra().Vacia);
        Assert.True(new FilaFacturaCompra { ImporteTotal = -5m }.EsRectificativa);
    }

    private sealed class Respuesta(HttpStatusCode codigo, string cuerpo) : HttpMessageHandler
    {
        public Uri? Pedida { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedida = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(codigo) { Content = new StringContent(cuerpo) });
        }
    }

    [Fact]
    public async Task Frankfurter_da_el_cambio_del_ultimo_dia_habil()
    {
        var h = new Respuesta(HttpStatusCode.OK, """{"amount":1.0,"base":"USD","date":"2026-02-06","rates":{"EUR":0.92115}}""");
        var c = await new TiposCambioFrankfurter(new HttpClient(h)).ObtenerAsync("usd", "EUR", new DateTime(2026, 2, 8), default);
        Assert.Equal(new DateTime(2026, 2, 6), c!.Fecha);
        Assert.Equal(0.92115m, c.Tipo);
        Assert.Equal("https://api.frankfurter.app/2026-02-08?from=USD&to=EUR", h.Pedida!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"amount":1.0,"base":"USD","date":"2026-01-20","rates":{"EUR":0.9}}""")]
    [InlineData(HttpStatusCode.OK, """{"amount":1.0,"base":"USD","date":"2026-02-08","rates":{}}""")]
    [InlineData(HttpStatusCode.OK, "no es json")]
    [InlineData(HttpStatusCode.NotFound, "{}")]
    public async Task Sin_cambio_valido_o_cercano_no_inventa_ninguno(HttpStatusCode codigo, string cuerpo) =>
        Assert.Null(await new TiposCambioFrankfurter(new HttpClient(new Respuesta(codigo, cuerpo))).ObtenerAsync("USD", "EUR", new DateTime(2026, 2, 8), default));

    [Theory]
    [InlineData(@"D:\SynologyBox\2025\Proveedores\Repsol Benzinera\20250630Sportage.pdf", "2025", "Repsol Benzinera", "20250630Sportage.pdf")]
    [InlineData(@"D:\SYNOLOGYBOX\2022\PROVEEDORES\Varias\sub\f.pdf", "2022", "Varias", "sub", "f.pdf")]
    [InlineData(@"\\s-oficina\oficina\Administracion\Systemtronic\2021\Proveedores\Varias\F.pdf", "2021", "Varias", "F.pdf")]
    public void Reubicar_conserva_desde_el_ejercicio(string guardada, params string[] resto)
    {
        var raiz = Path.Combine(Path.GetTempPath(), "raiz");
        Assert.Equal(Path.Combine([raiz, resto[0], "Proveedores", .. resto[1..]]), ServicioFacturasCompra.Reubicar(guardada, raiz));
    }

    [Theory]
    [InlineData(@"D:\Facturas\f.pdf")]
    [InlineData(@"D:\2025\Otros\f.pdf")]
    [InlineData(@"D:\2025\Proveedores")]
    public void Reubicar_sin_estructura_de_ejercicio_es_null(string guardada) =>
        Assert.Null(ServicioFacturasCompra.Reubicar(guardada, Path.GetTempPath()));

    [Fact]
    public void LocalizarPdf_prefiere_la_grabada_y_si_no_la_reubicada()
    {
        var raiz = Directory.CreateTempSubdirectory("compras-raiz").FullName;
        try
        {
            var carpeta = Directory.CreateDirectory(Path.Combine(raiz, "2026", "Proveedores", "Acme")).FullName;
            var reubicada = Path.Combine(carpeta, "f.pdf");
            File.WriteAllText(reubicada, "%PDF");
            var antigua = @"Z:\NoExiste\2026\Proveedores\Acme\f.pdf";
            Assert.Equal(reubicada, ServicioFacturasCompra.LocalizarPdf([antigua, @"Z:\Otra\f.pdf"], raiz));
            Assert.Equal(reubicada, ServicioFacturasCompra.LocalizarPdf([@"Z:\Otra\f.pdf", antigua], raiz));
            Assert.Equal(antigua, ServicioFacturasCompra.LocalizarPdf([antigua], null));
            Assert.Equal(antigua, ServicioFacturasCompra.LocalizarPdf([antigua], Path.Combine(raiz, "vacia")));
            Assert.Equal(reubicada, ServicioFacturasCompra.LocalizarPdf([reubicada, antigua], null));
            Assert.Equal("", ServicioFacturasCompra.LocalizarPdf([], raiz));
        }
        finally { Directory.Delete(raiz, recursive: true); }
    }

    [Fact]
    public void Comparar_los_importes_al_centimo()
    {
        var grabada = Factura();
        grabada.CuotaIva = 14.0154m;
        grabada.ImporteTotal = 80.7554m;
        var leida = Factura();
        leida.CuotaIva = 14.02m;
        leida.ImporteTotal = 80.76m;
        var c = ServicioFacturasCompra.Comparar(grabada, leida).ToDictionary(x => x.Campo);
        Assert.True(c["IVA"].Igual);
        Assert.True(c["Total"].Igual);
    }

    /// <summary>PDF de prueba con una línea de texto por página.</summary>
    internal static byte[] PdfDePaginas(params string[] textos)
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var fuente = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        foreach (var t in textos)
            builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText(t, 12, new UglyToad.PdfPig.Core.PdfPoint(50, 750), fuente);
        return builder.Build();
    }

    [Fact]
    public void Dividir_un_PDF_deja_las_paginas_anteriores_y_las_siguientes()
    {
        var pdf = PdfDePaginas("Factura uno", "Factura uno sigue", "Factura dos");
        var (primera, segunda) = DivisorPdf.Dividir(pdf, 3);
        Assert.Equal(2, DivisorPdf.ContarPaginas(primera));
        Assert.Equal(1, DivisorPdf.ContarPaginas(segunda));
        using var doc = UglyToad.PdfPig.PdfDocument.Open(segunda);
        Assert.Contains("dos", doc.GetPage(1).Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => DivisorPdf.Dividir(pdf, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DivisorPdf.Dividir(pdf, 4));
    }

    [Fact]
    public void La_confianza_minima_es_la_del_peor_campo_del_log()
    {
        Assert.Equal(0.4m, RepositorioFacturasCompra.ConfianzaMinima("""{"candidato":{"ConfianzaNif":1.0,"ConfianzaNumero":0.7,"ConfianzaFecha":0.4,"ConfianzaImportes":0.95}}"""));
        Assert.Null(RepositorioFacturasCompra.ConfianzaMinima(null));
        Assert.Null(RepositorioFacturasCompra.ConfianzaMinima("no es json"));
    }
}
