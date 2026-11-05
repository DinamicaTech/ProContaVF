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
