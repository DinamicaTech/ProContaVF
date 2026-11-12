using ProConta.Mantenimiento;
using ProConta.Organizacion.Mantenimiento;
using ProConta.Facturacion.Mantenimiento;

namespace ProConta.Facturacion.Tests;

/// <summary>
/// Regla «Useful tooltips» en la pantalla de Parámetros (plataforma/mantenimiento 2026-11-12): todo campo de todo proveedor
/// explica para qué sirve; «Hasta N caracteres» no es una ayuda.
/// </summary>
public class AyudasMantenimientoTests
{
    /// <summary>Los mismos proveedores que registra la aplicación (VentanaPrincipal), sin BD.</summary>
    internal static IReadOnlyList<IProveedorMantenimiento> Todos() =>
    [
        new ProveedorConfiguracionGeneral(null!), .. ProveedoresOrganizacion.Todos(null!), .. ProveedoresFacturacion.Todos(null!, null!),
        .. ProConta.Compras.Mantenimiento.ProveedoresCompras.Todos(null!), .. ProConta.Correo.Mantenimiento.ProveedoresCorreo.Todos(null!),
        .. ProConta.Activos.Mantenimiento.ProveedoresActivos.Todos(null!, null!),
    ];

    [Fact]
    public void TodosLosCamposTienenAyuda()
    {
        var sinAyuda = Todos().SelectMany(p => p.Campos.Where(c => string.IsNullOrWhiteSpace(c.Ayuda)).Select(c => $"{p.Titulo} › {c.Etiqueta} ({c.Clave})")).ToList();
        Assert.True(sinAyuda.Count == 0, $"{sinAyuda.Count} campos sin ayuda:\n" + string.Join("\n", sinAyuda));
    }

    [Fact]
    public void LaAyudaNoRepiteLaEtiqueta()
    {
        var repetidas = Todos().SelectMany(p => p.Campos.Where(c => c.Ayuda is string a && Normal(a) == Normal(c.Etiqueta)).Select(c => $"{p.Titulo} › {c.Etiqueta}")).ToList();
        Assert.True(repetidas.Count == 0, "La ayuda solo repite la etiqueta:\n" + string.Join("\n", repetidas));
        static string Normal(string s) => s.Trim().TrimEnd('.').ToLowerInvariant();
    }
}
