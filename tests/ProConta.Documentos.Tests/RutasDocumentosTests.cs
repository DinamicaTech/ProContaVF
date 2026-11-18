using ProConta.Documentos;

namespace ProConta.Documentos.Tests;

/// <summary>Nombres y rutas: el gestor limpia, no sobrescribe y guarda rutas relativas a la carpeta de documentos.</summary>
public sealed class RutasDocumentosTests
{
    [Theory]
    [InlineData("303 2T 2026 · justificante.pdf", "303 2T 2026 · justificante.pdf")]
    [InlineData("Factura: F/2026/001 <cliente>?.PDF", "Factura F 2026 001 cliente.pdf")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("   .pdf", "Documento.pdf")]
    [InlineData("acaba en punto. .pdf", "acaba en punto.pdf")]
    public void Nombre_limpio(string entrada, string esperado) => Assert.Equal(esperado, RutasDocumentos.NombreLimpio(entrada));

    [Fact]
    public void Nombre_largo_conserva_la_extension()
    {
        var n = RutasDocumentos.NombreLimpio(new string('a', 300) + ".pdf", 50);
        Assert.Equal(50, n.Length);
        Assert.EndsWith(".pdf", n);
    }

    [Fact]
    public void Ruta_libre_no_pisa_nada()
    {
        var ocupadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { @"C:\D\x.pdf", @"C:\D\x (2).pdf" };
        Assert.Equal(@"C:\D\x (3).pdf", RutasDocumentos.RutaLibre(@"C:\D", "x.pdf", ocupadas.Contains));
        Assert.Equal(@"C:\D\y.pdf", RutasDocumentos.RutaLibre(@"C:\D", "y.pdf", ocupadas.Contains));
    }

    [Fact]
    public void Ruta_relativa_dentro_de_la_raiz_y_completa_fuera()
    {
        Assert.Equal(@"Documentos\2026\Modelos\a.pdf", RutasDocumentos.ParaGuardar(@"D:\Docs\Empresa", @"D:\Docs\Empresa\Documentos\2026\Modelos\a.pdf"));
        Assert.Equal(@"E:\Otra\a.pdf", RutasDocumentos.ParaGuardar(@"D:\Docs\Empresa", @"E:\Otra\a.pdf"));
        Assert.Equal(@"D:\Docs\Empresa\Documentos\a.pdf", RutasDocumentos.Completa(@"D:\Docs\Empresa", @"Documentos\a.pdf"));
        Assert.Equal(@"E:\Otra\a.pdf", RutasDocumentos.Completa(@"D:\Docs\Empresa", @"E:\Otra\a.pdf"));
    }

    [Fact]
    public void Raiz_relativa_junto_a_la_bd() =>
        Assert.Equal(@"D:\Datos\Docs", RutasDocumentos.Raiz("Docs", @"D:\Datos\ProConta.accdb"));

    [Fact]
    public void Subcarpeta_de_los_manuales()
    {
        Assert.Equal(@"Documentos\2026\Modelos", RutasDocumentos.SubcarpetaManual(new TipoDocumento("MODELO", "Modelo presentado", "Modelos", 1, true, true), 2026));
        Assert.Equal(@"Documentos\Empresa", RutasDocumentos.SubcarpetaManual(new TipoDocumento("ACTA", "Acta", "Empresa", 4, false, true), 2026));
        Assert.Equal(@"2026\Modelos", RutasDocumentos.SubcarpetaLimpia(@"..\2026\.\Modelos"));
    }

    [Fact]
    public void Buscador_exige_todas_las_palabras()
    {
        var d = new DestinoVinculo(Vinculo.Modelo("303", 2026, "2T"), "Presentado el 20/07/2026");
        Assert.True(ServicioDocumentos.Coincide(d, ServicioDocumentos.Palabras("303 2t 2026")));
        Assert.True(ServicioDocumentos.Coincide(d, ServicioDocumentos.Palabras("modelo 303")));
        Assert.False(ServicioDocumentos.Coincide(d, ServicioDocumentos.Palabras("303 1t")));
        var t = new DestinoVinculo(new Vinculo(TiposEntidad.Tercero, 1, null, "Inmobiliaria Prado SL"), "B11111111");
        Assert.True(ServicioDocumentos.Coincide(t, ServicioDocumentos.Palabras("prado")));
        Assert.True(ServicioDocumentos.Coincide(t, ServicioDocumentos.Palabras("tercero prado")));
    }

    [Fact]
    public void Ejercicio_del_modelo_manda_sobre_la_fecha() =>
        Assert.Equal(2025, ServicioDocumentos.EjercicioDe([Vinculo.Modelo("303", 2025, "4T")], new DateTime(2026, 1, 30)));
}
