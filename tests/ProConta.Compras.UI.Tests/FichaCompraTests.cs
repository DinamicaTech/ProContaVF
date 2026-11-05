using ProConta.Compras.Facturas;
using ProConta.Compras.UI.VistaModelos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.UI.Tests;

/// <summary>Ficha de la factura de compra sin ventanas: cálculos, confianza «Manual», importación y lo que se guarda.</summary>
public sealed class FichaCompraTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);
    private static readonly ProveedorFicha Proveedor = new(7, 70, "B61465332", "Ficticio S.L.");
    private const string Log = """{"version":1,"confianza":0.95,"candidato":{"ConfianzaNif":1.0,"ConfianzaNumero":0.7,"ConfianzaFecha":0.4,"ConfianzaImportes":0.95},"advertencias":[]}""";

    private static FacturaCompra Factura()
    {
        var f = new FacturaCompra
        {
            IdRegistro = 5, Ejercicio = Ctx, RutaPdf = @"X:\D\2026\Proveedores\Ficticio\f.pdf", Md5 = "0123456789abcdef0123456789abcdef", Carpeta = "Ficticio",
            IdProveedor = 7, CuentaContableProveedor = "4000007", NifProveedor = "B61465332", NumeroFactura = "FV1", Fecha = new DateTime(2026, 2, 10),
            Concepto = "Ficticio S.L. Fact: FV1", BaseImponible = 150m, CuotaIva = 26m, ImporteTotal = 176m, Log = Log, Version = 3
        };
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 10m, 50m, 5m));
        f.Desgloses.Add(new DesgloseFacturaCompra(2, 21m, 100m, 21m));
        return f;
    }

    private static FichaCompraVM Ficha(FacturaCompra? f = null, bool soloLectura = false) =>
        new(f ?? Factura(), [Proveedor], [new("6000000 · Compras", "6000000")], [new CentroCoste(3, "T01", null, "3", "COCHES", true, true, 0, "3")], [], soloLectura);

    [Fact]
    public void Al_abrir_no_hay_cambios_y_la_confianza_sale_del_log()
    {
        var f = Ficha();
        Assert.False(f.TieneCambios);
        Assert.Equal(("100 %", "alta"), (f.ConfianzaNif.Texto, f.ConfianzaNif.Nivel));
        Assert.Equal("media", f.ConfianzaNumero.Nivel);
        Assert.Equal("baja", f.ConfianzaFecha.Nivel);
        Assert.Equal("Ficticio S.L.", f.NombreProveedor);
        Assert.Null(f.Descuadre);
    }

    [Fact]
    public void Cambiar_un_campo_lo_marca_como_manual_y_deja_cambios()
    {
        var f = Ficha();
        f.Numero = "FV-2";
        Assert.True(f.TieneCambios);
        Assert.Equal("Manual", f.ConfianzaNumero.Texto);
        Assert.Equal("FV2", f.Construir().NumeroFactura);
    }

    [Fact]
    public void Los_importes_se_suman_por_tipo_con_retencion_y_suplido_y_se_avisa_si_no_cuadran()
    {
        var f = Ficha();
        f.ConRetencion = true;
        f.PorcentajeRetencion = "15";
        f.Suplido = "10";
        Assert.Equal(150m, f.BaseCalculada);
        Assert.Equal(26m, f.CuotaCalculada);
        Assert.Equal(22.5m, f.RetencionCalculada);
        Assert.Equal(163.5m, f.TotalCalculado);
        Assert.NotNull(f.Descuadre);
        f.Total = "163,50";
        Assert.Null(f.Descuadre);
        var g = f.Construir();
        Assert.Equal((150m, 26m, 22.5m, 10m, 163.5m), (g.BaseImponible!.Value, g.CuotaIva!.Value, g.ImporteRetencion!.Value, g.Suplido!.Value, g.ImporteTotal!.Value));
        Assert.Equal(ClaseRetencion.Profesionales, g.ClaseRetencion);
        Assert.True(g.Cuadra);
    }

    [Fact]
    public void En_importacion_hay_un_solo_importe_sin_iva_y_se_marca_la_inversion()
    {
        var f = Ficha();
        f.Operacion = f.Operaciones.Single(o => o.Valor == OperacionCompra.Importacion);
        f.ImporteUnico = "300";
        f.Total = "300";
        Assert.True(f.Isp);
        var g = f.Construir();
        Assert.Equal((300m, 0m), (g.BaseImponible!.Value, g.CuotaIva!.Value));
        Assert.Equal([(0m, 300m, 0m)], g.Desgloses.Select(d => (d.TipoIva, d.BaseImponible, d.CuotaIva)));
    }

    [Fact]
    public void La_cuota_se_propone_al_escribir_tipo_y_base()
    {
        var f = Ficha();
        f.AnadirTipo();
        var d = f.Desgloses[^1];
        d.Base = "33,33";
        Assert.Equal("7,00", d.Cuota);
    }

    [Fact]
    public void Un_nif_que_no_es_de_ningun_proveedor_lo_avisa_y_quita_el_proveedor()
    {
        var f = Ficha();
        f.Nif = "a58818501";
        Assert.Equal("A58818501", f.Nif);
        Assert.False(f.HayProveedor);
        Assert.NotNull(f.AvisoProveedor);
        Assert.Null(f.Construir().IdProveedor);
    }

    [Fact]
    public void Lo_construido_conserva_la_version_y_la_imputacion_elegida()
    {
        var f = Ficha();
        f.CuentaGasto = f.CuentasGasto.Single(c => c.Valor == "6000000");
        f.CentroCoste = f.CentrosCoste.Single(c => c.Valor == 3);
        var g = f.Construir();
        Assert.Equal((3, "6000000", 3, "4000007"), (g.Version, g.CuentaGasto!, g.IdCentroCoste!.Value, g.CuentaContableProveedor!));
        Assert.Equal([10m, 21m], g.Desgloses.Select(x => x.TipoIva));
    }

    [Fact]
    public void Una_ficha_de_solo_lectura_no_cambia()
    {
        var f = Ficha(soloLectura: true);
        f.Numero = "OTRO";
        Assert.Equal("FV1", f.Numero);
        Assert.False(f.TieneCambios);
    }
}
