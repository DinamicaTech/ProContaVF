using Amazon.Textract;
using Amazon.Textract.Model;
using ProConta.Compras.Integraciones;

namespace ProConta.Compras.Tests;

/// <summary>Paso de los bloques de Textract a palabras en puntos del PDF (sin llamar a AWS).</summary>
public sealed class ExtractorOcrTextractTests
{
    private static Block Palabra(string texto, float izq, float arriba, float ancho, float alto) => new()
    {
        BlockType = BlockType.WORD, Text = texto,
        Geometry = new Geometry { BoundingBox = new BoundingBox { Left = izq, Top = arriba, Width = ancho, Height = alto } }
    };

    [Fact]
    public void Las_palabras_pasan_a_puntos_desde_abajo_a_la_izquierda_y_el_texto_va_por_lineas()
    {
        var p = ExtractorOcrTextract.Pagina(1, 600, 800, [
            new Block { BlockType = BlockType.LINE, Text = "Factura FV1" },
            new Block { BlockType = BlockType.LINE, Text = "Total 121,00" },
            Palabra("Factura", 0.1f, 0.1f, 0.2f, 0.02f)
        ]);
        Assert.Equal($"Factura FV1{Environment.NewLine}Total 121,00", p.Texto);
        var w = Assert.Single(p.Palabras);
        Assert.Equal(60, w.X, 3);
        Assert.Equal(704, w.Y, 3);
        Assert.Equal(120, w.Ancho, 3);
        Assert.Equal(16, w.Alto, 3);
    }

    [Fact]
    public void Sin_perfil_de_aws_no_hay_ocr_y_se_dice_por_que()
    {
        var (extractor, motivo) = ExtractorOcrTextract.Crear(v => v == "PROCONTA_AWS_PERFIL" ? "perfil-que-no-existe-" + Guid.NewGuid().ToString("N") : null);
        Assert.Null(extractor);
        Assert.Contains("perfil", motivo);
    }
}
