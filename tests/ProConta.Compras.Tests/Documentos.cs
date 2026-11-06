using ProConta.Compras.Reconocimiento;

namespace ProConta.Compras.Tests;

/// <summary>Documentos inventados para las pruebas: texto por líneas y, si hace falta, palabras posicionadas.</summary>
internal static class Documentos
{
    public const string NifSociedad = "B62344502";   // NIF de prueba de la sociedad receptora
    public const string NifProveedor = "B61465332";  // NIF de prueba válido del proveedor

    public static DocumentoFacturaExtraido Texto(string texto, string ruta = @"X:\Datos\Soc\2026\Proveedores\Ficticio\factura.pdf")
        => new()
        {
            Ruta = ruta,
            Paginas = [new PaginaDocumento { Numero = 1, Texto = texto.Replace("\r\n", "\n") }]
        };

    /// <summary>Texto con geometría simulada: cada línea a 12 puntos de la anterior y cada carácter de 5 puntos de ancho.</summary>
    public static DocumentoFacturaExtraido Maquetado(string texto, string ruta = @"X:\Datos\Soc\2026\Proveedores\Ficticio\factura.pdf")
    {
        var lineas = texto.Replace("\r\n", "\n").Split('\n');
        var palabras = new List<PalabraDocumento>();
        for (var l = 0; l < lineas.Length; l++)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(lineas[l], @"\S+"))
                palabras.Add(new PalabraDocumento { Pagina = 1, Texto = m.Value, X = 40 + m.Index * 5, Y = 800 - l * 12, Ancho = m.Length * 5, Alto = 8 });
        return new() { Ruta = ruta, Paginas = [new PaginaDocumento { Numero = 1, Texto = string.Join("\n", lineas), Palabras = palabras }] };
    }

    public static DocumentoFacturaExtraido ConPalabras(string ruta, params (string Texto, double X, double Y)[] palabras)
        => new()
        {
            Ruta = ruta,
            Paginas =
            [
                new PaginaDocumento
                {
                    Numero = 1,
                    Texto = string.Join("\n", palabras.Select(p => p.Texto)),
                    Palabras = palabras.Select(p => new PalabraDocumento { Pagina = 1, Texto = p.Texto, X = p.X, Y = p.Y, Ancho = 30, Alto = 8 }).ToArray()
                }
            ]
        };

    public static ContextoReconocimientoFacturaCompra Contexto(bool vies = false, string[]? numeros = null, Dictionary<string, string>? alias = null)
        => new()
        {
            NifsSociedades = [NifSociedad],
            NumerosFacturaProveedor = numeros ?? [],
            NifsPorAliasProveedor = alias ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            SociedadTieneVies = vies
        };

    public static ResultadoReconocimientoFacturaCompra Reconocer(DocumentoFacturaExtraido d, ContextoReconocimientoFacturaCompra? c = null)
        => new MotorReconocimientoFacturaCompra().Reconocer(d, c ?? Contexto());
}
