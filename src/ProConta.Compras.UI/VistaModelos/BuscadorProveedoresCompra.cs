using System.Globalization;
using ProConta.Compras.Facturas;

namespace ProConta.Compras.UI.VistaModelos;

/// <summary>
/// Filtro del buscador de proveedores de la ficha de compras (compras/pantallas 2026-11-25): cada palabra escrita tiene que
/// aparecer en la razón social, el nombre comercial o el NIF, sin distinguir mayúsculas ni acentos; el NIF también vale
/// escrito con guiones o espacios. Ordenados por nombre.
/// </summary>
public static class BuscadorProveedoresCompra
{
    private static readonly CompareInfo Comparador = CultureInfo.GetCultureInfo("es-ES").CompareInfo;
    private const CompareOptions Opciones = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public static IReadOnlyList<ProveedorFicha> Filtrar(IEnumerable<ProveedorFicha> proveedores, string? texto)
    {
        var palabras = (texto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return proveedores.Where(p => palabras.All(w => Coincide(p, w)))
            .OrderBy(p => p.NombreCompleto, StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), ignoreCase: true)).ToList();
    }

    private static bool Coincide(ProveedorFicha p, string palabra)
    {
        if (Contiene(p.RazonSocial ?? p.Nombre, palabra) || Contiene(p.NombreComercial, palabra) || Contiene(p.Nombre, palabra)) return true;
        var nif = Compacto(palabra);
        return nif.Length > 0 && Compacto(p.Nif).Contains(nif, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contiene(string? campo, string palabra) => !string.IsNullOrEmpty(campo) && Comparador.IndexOf(campo, palabra, Opciones) >= 0;

    private static string Compacto(string? t) => new((t ?? "").Where(char.IsAsciiLetterOrDigit).ToArray());
}
