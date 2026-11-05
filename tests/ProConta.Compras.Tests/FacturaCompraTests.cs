using ProConta.Compras.Facturas;
using ProConta.Compras.Reconocimiento;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Tests;

/// <summary>Ente factura de compra, carpeta del proveedor y búsqueda de PDF, sin BD.</summary>
public sealed class FacturaCompraTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);
    private const string Md5 = "0123456789abcdef0123456789abcdef";

    private static FacturaReconocida Reconocida(ResultadoReconocimientoFacturaCompra r, string ruta = @"X:\Datos\Soc\2026\Proveedores\Ficticio\f.pdf") => new()
    {
        RutaPdf = ruta, Md5 = Md5, Lectura = r, NumeroFactura = "FV1", Concepto = "Ficticio Fact: FV1", Confianza = 0.95m,
        RequiereRevision = false, Fuente = "pdf-local", Log = "{}"
    };

    [Fact]
    public void Desde_reconocimiento_en_euros_copia_importes_y_ordena_los_desgloses_por_tipo()
    {
        var r = new ResultadoReconocimientoFacturaCompra
        {
            BaseImponible = 150m, CuotaIva = 26m, ImporteTotal = 176m, FechaFactura = new DateTime(2026, 2, 10), NifProveedor = "B61465332",
            DesglosesIva = [new() { Tipo = 21m, Base = 100m, Cuota = 21m }, new() { Tipo = 10m, Base = 50m, Cuota = 5m }]
        };
        var f = FacturaCompra.DesdeReconocimiento(Reconocida(r), Ctx, new ProveedorCarpeta(7, "4000007", "B61465332", "Ficticio"));
        Assert.Equal((150m, 26m, 176m), (f.BaseImponible!.Value, f.CuotaIva!.Value, f.ImporteTotal!.Value));
        Assert.Equal([10m, 21m], f.Desgloses.Select(d => d.TipoIva));
        Assert.Equal([1, 2], f.Desgloses.Select(d => (int)d.Orden));
        Assert.Equal(f.BaseImponible, f.Desgloses.Sum(d => d.BaseImponible));
        Assert.Equal(f.CuotaIva, f.Desgloses.Sum(d => d.CuotaIva));
        Assert.True(f.Cuadra);
        Assert.Equal((7, "4000007", "Ficticio"), (f.IdProveedor!.Value, f.CuentaContableProveedor!, f.Carpeta));
        Assert.True(f.Validar().EsCorrecto);
    }

    [Fact]
    public void En_divisa_guarda_el_original_y_los_euros_cuadran_tras_redondear()
    {
        var r = new ResultadoReconocimientoFacturaCompra
        {
            Divisa = "USD", RequiereConversionDivisa = true, ImporteTotal = 35m, ImporteTotalDivisa = 35m, CuotaIva = 0m, BaseImponible = 35m,
            TipoCambioAplicado = 0.92115m, FechaTipoCambio = new DateTime(2026, 2, 10), ImporteTotalEuros = 32.24m, FechaFactura = new DateTime(2026, 2, 10)
        };
        var f = FacturaCompra.DesdeReconocimiento(Reconocida(r), Ctx, null);
        Assert.True(f.EsDivisaExtranjera);
        Assert.Equal(35m, f.ImporteTotalDivisa);
        Assert.Equal(32.24m, f.ImporteTotal);
        Assert.Equal(f.ImporteTotal, f.BaseImponible + f.CuotaIva);
        Assert.Equal("Ficticio", f.Carpeta);
    }

    [Fact]
    public void En_divisa_sin_cambio_los_euros_quedan_vacios()
    {
        var r = new ResultadoReconocimientoFacturaCompra { Divisa = "USD", RequiereConversionDivisa = true, ImporteTotal = 35m, ImporteTotalDivisa = 35m };
        var f = FacturaCompra.DesdeReconocimiento(Reconocida(r), Ctx, null);
        Assert.Null(f.ImporteTotal);
        Assert.Equal(35m, f.ImporteTotalDivisa);
        Assert.False(f.Cuadra);
    }

    [Fact]
    public void Con_retencion_cuadra_base_mas_cuota_menos_retencion()
    {
        var r = new ResultadoReconocimientoFacturaCompra { BaseImponible = 100m, CuotaIva = 21m, ImporteTotal = 106m, TipoRetencion = 15m, ImporteRetencion = 15m };
        Assert.True(FacturaCompra.DesdeReconocimiento(Reconocida(r), Ctx, null).Cuadra);
    }

    [Fact]
    public void Validar_rechaza_md5_ausente_moneda_rara_y_desgloses_con_orden_repetido()
    {
        var f = new FacturaCompra { Ejercicio = Ctx, RutaPdf = @"X:\f.pdf", Md5 = "", Divisa = "dolar" };
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 21m, 10m, 2.1m));
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 10m, 10m, 1m));
        var v = f.Validar();
        Assert.Equal(["Compras.SinMd5", "Compras.DesgloseRepetido", "Compras.Divisa"], v.Errores.Select(e => e.Codigo));
    }

    [Theory]
    [InlineData(@"X:\Datos\Soc\2026\Proveedores\Ficticio\f.pdf", "Ficticio")]
    [InlineData(@"X:\Datos\Soc\2026\Proveedores\Ficticio\Enero\f.pdf", "Ficticio")]
    [InlineData(@"X:\Otra\Carpeta\f.pdf", "Carpeta")]
    public void La_carpeta_del_proveedor_es_el_primer_nivel_bajo_proveedores(string ruta, string esperada)
        => Assert.Equal(esperada, FacturaCompra.CarpetaDe(ruta));

    [Fact]
    public void Las_carpetas_historicas_toman_el_nif_del_tercero_por_proveedor_o_por_cuenta()
    {
        var proveedores = new[]
        {
            new ProveedorEmpresa(7, "B61465332", ["4000007"], new HashSet<string>(["4000007"], StringComparer.OrdinalIgnoreCase)),
            new ProveedorEmpresa(8, "A58818501", ["4100008"], new HashSet<string>(["4100008"], StringComparer.OrdinalIgnoreCase))
        };
        var alias = ServicioFacturasCompra.ResolverCarpetas(
            [("Ficticio", "4000007", null), ("Otro", null, 8), ("Huérfana", "4009999", null)], proveedores);
        Assert.Equal(["B61465332", "A58818501", null], alias.Select(a => a.Nif));
        Assert.Equal(alias[0], ServicioFacturasCompra.ElegirCarpeta(@"X:\D\2026\Proveedores\Ficticio\f.pdf", alias));
        Assert.Null(ServicioFacturasCompra.ElegirCarpeta(@"X:\D\2026\Proveedores\Desconocido\f.pdf", alias));
        Assert.Equal("4000007", proveedores[0].CuentaPara("4009999"));
    }

    [Fact]
    public void Carpeta_con_dos_proveedores_distintos_no_se_asigna()
    {
        var alias = ServicioFacturasCompra.ResolverCarpetas([("Ficticio", "4000007", null), ("Ficticio", "4000008", null)], []);
        Assert.Null(ServicioFacturasCompra.ElegirCarpeta(@"X:\D\2026\Proveedores\Ficticio\f.pdf", alias));
    }

    [Fact]
    public void Enumera_los_pdf_de_cada_carpeta_de_proveedor_incluidas_subcarpetas()
    {
        var raiz = Path.Combine(Path.GetTempPath(), $"compras-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(Path.Combine(raiz, "B", "Enero"));
            Directory.CreateDirectory(Path.Combine(raiz, "A"));
            File.WriteAllText(Path.Combine(raiz, "suelto.pdf"), "x");
            File.WriteAllText(Path.Combine(raiz, "A", "1.PDF"), "x");
            File.WriteAllText(Path.Combine(raiz, "A", "nota.txt"), "x");
            File.WriteAllText(Path.Combine(raiz, "B", "Enero", "2.pdf"), "x");
            var rutas = ServicioFacturasCompra.EnumerarPdf(raiz).Select(r => Path.GetRelativePath(raiz, r).Replace('\\', '/')).ToList();
            Assert.Equal(["A/1.PDF", "B/Enero/2.pdf"], rutas);
        }
        finally
        {
            Directory.Delete(raiz, true);
        }
    }
}
