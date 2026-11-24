using ProConta.Nucleo.Ficheros;
using Xunit;

namespace ProConta.Nucleo.Tests;

/// <summary>Protección de los ficheros de producción desde la copia de pruebas (plataforma/entorno-pruebas 2026-11-24).</summary>
[Collection(nameof(EntornoFicherosTests))]
public sealed class EntornoFicherosTests : IDisposable
{
    private readonly string _raiz = Path.Combine(Path.GetTempPath(), "entorno-ficheros-" + Guid.NewGuid().ToString("N"));
    private readonly string _pruebas;
    private readonly string _nas;

    public EntornoFicherosTests()
    {
        _pruebas = Path.Combine(_raiz, "datospruebas");
        _nas = Path.Combine(_raiz, "nas", "2026", "Proveedores");
        Directory.CreateDirectory(_pruebas);
        Directory.CreateDirectory(_nas);
    }

    public void Dispose()
    {
        EntornoFicheros.Configurar(null);
        foreach (var f in Directory.EnumerateFiles(_raiz, "*", SearchOption.AllDirectories)) File.SetAttributes(f, FileAttributes.Normal);
        Directory.Delete(_raiz, recursive: true);
    }

    [Fact]
    public void Sin_configurar_no_cambia_nada()
    {
        EntornoFicheros.Configurar(null);
        var ruta = Path.Combine(_nas, "a.pdf");
        Assert.False(EntornoFicheros.Protegida(ruta));
        Assert.Equal(ruta, EntornoFicheros.ParaEscribir(ruta));
    }

    [Fact]
    public void En_pruebas_lo_de_fuera_va_a_externas_sin_la_unidad_y_lo_de_dentro_no_cambia()
    {
        Assert.Null(EntornoFicheros.Desviar(_pruebas, Path.Combine(_pruebas, "2026", "x.pdf")));
        Assert.Equal(Path.Combine(@"C:\datospruebas", "externas", "D", "SynologyBox", "2026", "Proveedores", "f.pdf"),
            EntornoFicheros.Desviar(@"C:\datospruebas", @"D:\SynologyBox\2026\Proveedores\f.pdf"));
        Assert.Equal(Path.Combine(@"C:\datospruebas", "externas", "UNC", "nas", "docs", "f.pdf"),
            EntornoFicheros.Desviar(@"C:\datospruebas", @"\\nas\docs\f.pdf"));
        // Una carpeta hermana con el mismo prefijo no está dentro.
        Assert.NotNull(EntornoFicheros.Desviar(@"C:\datospruebas", @"C:\datospruebas2\f.pdf"));
    }

    [Fact]
    public void Mover_desde_el_nas_en_pruebas_copia_y_deja_el_original()
    {
        EntornoFicheros.Configurar(_pruebas);
        var origen = Path.Combine(_nas, "a.pdf");
        File.WriteAllText(origen, "pdf");
        var destino = Path.Combine(_nas, "Proveedor", "a.pdf");

        EntornoFicheros.Mover(origen, destino);

        Assert.True(File.Exists(origen));
        Assert.False(File.Exists(destino));
        Assert.True(File.Exists(EntornoFicheros.ParaEscribir(destino)));
        Assert.StartsWith(_pruebas, EntornoFicheros.ParaEscribir(destino), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Copia_editable_copia_una_vez_y_el_original_no_cambia()
    {
        EntornoFicheros.Configurar(_pruebas);
        var origen = Path.Combine(_nas, "b.pdf");
        File.WriteAllText(origen, "original");
        File.SetAttributes(origen, FileAttributes.ReadOnly);

        var copia = EntornoFicheros.CopiaEditable(origen);
        File.WriteAllText(copia, "cambiado");

        Assert.NotEqual(origen, copia);
        Assert.Equal("original", File.ReadAllText(origen));
        Assert.Equal(copia, EntornoFicheros.CopiaEditable(origen));
        Assert.Equal("cambiado", File.ReadAllText(copia));
    }

    [Fact]
    public void En_produccion_mover_mueve()
    {
        EntornoFicheros.Configurar(null);
        var origen = Path.Combine(_nas, "c.pdf");
        File.WriteAllText(origen, "pdf");
        var destino = Path.Combine(_nas, "Proveedor", "c.pdf");

        EntornoFicheros.Mover(origen, destino);

        Assert.False(File.Exists(origen));
        Assert.True(File.Exists(destino));
    }
}
