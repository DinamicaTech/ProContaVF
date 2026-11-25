using ProConta.Compras.Facturas;
using ProConta.Compras.UI.VistaModelos;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.UI.Tests;

/// <summary>Buscador de proveedores por nombre de la ficha de compras (compras/pantallas 2026-11-25), con datos inventados.</summary>
public sealed class BuscadorProveedoresCompraTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);
    private static readonly ProveedorFicha Electrica = new(1, 10, "A11111111", "Eléctrica Inventada S.A.", RazonSocial: "ELÉCTRICA INVENTADA S.A.", NombreComercial: "Luz Fácil");
    private static readonly ProveedorFicha Gestoria = new(2, 20, "B22222222", "Gestoría Prueba S.L.", 4, ProductoCompra.Servicios, RazonSocial: "GESTORÍA PRUEBA S.L.");
    private static readonly ProveedorFicha GestoriaGemela = new(3, 30, "B22222222", "Gestoría Prueba Norte S.L.", RazonSocial: "GESTORÍA PRUEBA NORTE S.L.");

    private static FacturaCompra SinProveedor() => new()
    {
        IdRegistro = 9, Ejercicio = Ctx, RutaPdf = @"X:\D\2026\Proveedores\f.pdf", Md5 = "0123456789abcdef0123456789abcdef", Version = 1,
    };

    private static FichaCompraVM Ficha(Func<IReadOnlyList<ProveedorFicha>, string, ProveedorFicha?> elegir, FacturaCompra? f = null) =>
        new(f ?? SinProveedor(), [Electrica, Gestoria, GestoriaGemela], [new("6000000 · Compras", "6000000")], [], [], false,
            formasPago: [(3, "Transferencia"), (4, "Recibo domiciliado")]) { ElegirProveedor = elegir };

    [Theory]
    [InlineData("electrica", 1)]          // sin acento ni mayúsculas
    [InlineData("luz facil", 1)]          // nombre comercial, varias palabras
    [InlineData("gestoria norte", 3)]     // todas las palabras
    [InlineData("b-22222222", 3)]         // NIF con guion: los dos de ese NIF, por nombre («NORTE» antes)
    public void Filtra_por_razon_social_nombre_comercial_o_nif(string texto, int primero)
    {
        var r = BuscadorProveedoresCompra.Filtrar([Electrica, Gestoria, GestoriaGemela], texto);
        Assert.Equal(primero, r[0].IdProveedor);
        Assert.Empty(BuscadorProveedoresCompra.Filtrar([Electrica, Gestoria], "fontanería"));
        Assert.Equal(3, BuscadorProveedoresCompra.Filtrar([GestoriaGemela, Electrica, Gestoria], "").Count);
    }

    [Fact]
    public async Task Elegir_un_proveedor_pone_su_nif_su_nombre_y_su_forma_de_pago()
    {
        string? recibido = null;
        var f = Ficha((_, t) => { recibido = t; return Gestoria; });
        await f.BuscarProveedorAsync("gestoria", diferir: false);
        Assert.Equal("gestoria", recibido);
        Assert.Equal(("B22222222", true, "GESTORÍA PRUEBA S.L."), (f.Nif, f.HayProveedor, f.NombreProveedor));
        Assert.Equal((byte?)4, f.FormaPago.Valor);
        Assert.True(f.TieneCambios);
        Assert.Equal(2, f.Construir().IdProveedor);
    }

    [Fact]
    public async Task Con_dos_proveedores_del_mismo_nif_queda_el_elegido()
    {
        var f = Ficha((_, _) => GestoriaGemela);
        f.Nif = "B22222222";
        Assert.Equal(2, f.Construir().IdProveedor);
        await f.BuscarProveedorAsync("norte", diferir: false);
        Assert.Equal(3, f.Construir().IdProveedor);
        Assert.Equal("GESTORÍA PRUEBA NORTE S.L.", f.NombreProveedor);
    }

    [Fact]
    public async Task Un_nombre_escrito_en_el_nif_abre_el_buscador_y_no_cambia_el_nif()
    {
        var abierto = new TaskCompletionSource<string>();
        var f = Ficha((_, t) => { abierto.TrySetResult(t); return null; });
        f.Nif = "luz fácil";
        Assert.Equal("luz fácil", await abierto.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal("", f.Nif);
        Assert.False(f.HayProveedor);
        // Un NIF (con cifras) se escribe tal cual, sin buscador.
        f.Nif = "a11111111";
        Assert.Equal(("A11111111", 1), (f.Nif, f.Construir().IdProveedor));
    }

    [Fact]
    public async Task En_solo_lectura_no_se_busca()
    {
        var llamado = false;
        var f = new FichaCompraVM(SinProveedor(), [Electrica], [], [], [], soloLectura: true) { ElegirProveedor = (_, _) => { llamado = true; return Electrica; } };
        await f.BuscarProveedorAsync("luz", diferir: false);
        Assert.False(llamado);
        Assert.False(f.BuscarProveedorCommand.CanExecute(null));
    }
}
