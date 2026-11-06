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

    private static readonly ProveedorFicha ConFormaPago = new(8, 80, "B12345674", "Otro S.L.", 4, ProductoCompra.Servicios);
    private static readonly TipoRetencionCompra[] Retenciones =
        [new(19m, "Alquiler de locales comerciales", ClaseRetencion.Arrendamientos), new(15m, "Tipo general", ClaseRetencion.Profesionales), new(7m, "Tipo reducido por inicio de actividad", ClaseRetencion.Profesionales)];

    private static FichaCompraVM Ficha(FacturaCompra? f = null, bool soloLectura = false, IReadOnlyList<FilaFacturaCompra>? otras = null) =>
        new(f ?? Factura(), [Proveedor, ConFormaPago], [new("6000000 · Compras", "6000000")], [new CentroCoste(3, "T01", null, "3", "COCHES", true, true, 0, "3")], otras ?? [], soloLectura,
            tiposIva: [21m, 10m, 4m], formasPago: [(3, "Transferencia"), (4, "Recibo domiciliado")], tiposRetencion: Retenciones);

    [Fact]
    public void La_retencion_se_elige_de_los_tipos_y_rellena_porcentaje_y_clase()
    {
        var f = Ficha();
        Assert.Equal(["Sin retención", "19 % · Alquiler de locales comerciales", "15 % · Tipo general", "7 % · Tipo reducido por inicio de actividad"], f.TiposRetencion.Select(t => t.Texto));
        Assert.Equal("Sin retención", f.TipoRetencion.Texto);
        f.TipoRetencion = f.TiposRetencion[1];
        Assert.True(f.TieneCambios);
        Assert.Equal(28.5m, f.RetencionCalculada);
        var g = f.Construir();
        Assert.Equal((19m, ClaseRetencion.Arrendamientos, 28.5m), (g.PorcentajeRetencion!.Value, g.ClaseRetencion!.Value, g.ImporteRetencion!.Value));
        f.TipoRetencion = f.TiposRetencion[0];
        Assert.Null(f.Construir().ImporteRetencion);

        // Un porcentaje que no está en la tabla se sigue viendo.
        var vieja = Factura();
        vieja.PorcentajeRetencion = 12m;
        vieja.ImporteRetencion = 18m;
        vieja.ClaseRetencion = ClaseRetencion.Profesionales;
        Assert.Equal("12 % · no está en la tabla", Ficha(vieja).TipoRetencion.Texto);
    }

    [Fact]
    public void El_producto_es_bienes_por_defecto_o_el_del_proveedor()
    {
        var f = Ficha();
        Assert.Equal("Bienes", f.Producto.Texto);
        Assert.False(f.TieneCambios);
        Assert.Equal(ProductoCompra.Bienes, f.Construir().Producto);
        f.Nif = "B12345674";
        Assert.Equal("Servicios", f.Producto.Texto);

        // Si el operador ya lo eligió, cambiar de proveedor no lo pisa.
        var g = Ficha();
        g.Producto = g.Productos.First(p => p.Valor == ProductoCompra.Servicios);
        g.Producto = g.Productos.First(p => p.Valor == ProductoCompra.Bienes);
        g.Nif = "B12345674";
        Assert.Equal("Bienes", g.Producto.Texto);
        // En solo consulta se ve lo grabado.
        Assert.Equal("Sin indicar", Ficha(soloLectura: true).Producto.Texto);
    }

    [Fact]
    public void La_forma_de_pago_se_elige_del_catalogo_y_se_propone_la_del_proveedor()
    {
        var f = Ficha();
        Assert.Equal(["Sin forma de pago", "Transferencia", "Recibo domiciliado"], f.FormasPago.Select(x => x.Texto));
        Assert.Null(f.FormaPago.Valor);
        f.FormaPago = f.FormasPago[1];
        Assert.True(f.TieneCambios);
        Assert.Equal((byte)3, f.Construir().IdFormaPago);

        var otra = Ficha();
        otra.Nif = "B12345674";
        Assert.Equal("Recibo domiciliado", otra.FormaPago.Texto);
        Assert.Equal((byte)4, otra.Construir().IdFormaPago);

        // Ya elegida, cambiar de proveedor no la pisa.
        f.Nif = "B12345674";
        Assert.Equal("Transferencia", f.FormaPago.Texto);

        // Al abrir una factura del proveedor sin forma de pago, se propone la suya sin dejar cambios.
        var suya = Factura();
        suya.IdProveedor = 8;
        suya.NifProveedor = "B12345674";
        var abierta = Ficha(suya);
        Assert.Equal("Recibo domiciliado", abierta.FormaPago.Texto);
        Assert.False(abierta.TieneCambios);
        Assert.Equal("Sin forma de pago", Ficha(suya, soloLectura: true).FormaPago.Texto);

        var vieja = Factura();
        vieja.IdFormaPago = 9;
        Assert.Equal("Forma de pago 9 (ya no existe)", Ficha(vieja).FormaPago.Texto);
    }

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
    public void Volver_a_reconocer_deja_cambios_con_la_confianza_de_la_nueva_lectura_no_manual()
    {
        var f = Ficha();
        f.Numero = "FV-2";
        var leida = Factura();
        leida.Log = """{"version":1,"confianza":1.0,"candidato":{"ConfianzaNif":1.0,"ConfianzaNumero":1.0,"ConfianzaFecha":1.0,"ConfianzaImportes":0.97,"CamposPlantilla":["fecha"]},"advertencias":[]}""";
        f.AplicarLectura(leida);
        Assert.True(f.TieneCambios);
        Assert.Equal("100 % plantilla", f.ConfianzaFecha.Texto);
        Assert.Equal("100 %", f.ConfianzaNumero.Texto);
        Assert.Equal("97 %", f.ConfianzaImportes.Texto);
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
        Assert.Equal("15 % · Tipo general", f.TipoRetencion.Texto);
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
        Assert.Equal("7,00 €", d.Cuota);
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
    public void Al_cambiar_a_un_nif_desconocido_la_tarjeta_deja_de_mostrar_la_cuenta_del_proveedor_anterior()
    {
        var f = Ficha();
        Assert.Equal("4000007", f.Cuenta);
        var avisadas = new List<string?>();
        f.PropertyChanged += (_, e) => avisadas.Add(e.PropertyName);
        f.Nif = "B76543214";
        Assert.Equal("", f.Cuenta);
        Assert.Contains(nameof(FichaCompraVM.Cuenta), avisadas);
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
    [Fact]
    public void Los_importes_se_ven_en_moneda_y_el_tipo_se_elige_de_los_del_ejercicio()
    {
        var f = Ficha();
        var d = f.Desgloses[^1];
        Assert.Equal(("100,00 €", "21,00 €"), (d.Base, d.Cuota));
        Assert.Equal(["21 %", "10 %", "4 %", "0 %"], d.Tipos.Select(t => t.Texto));
        Assert.Equal("21 %", d.TipoElegido!.Texto);
        d.Base = "200";
        Assert.Equal("200,00 €", d.Base);
        Assert.NotNull(f.Descuadre);
        Assert.Equal("176,00 €", f.Total);
    }

    [Fact]
    public void Con_cuota_de_iva_no_se_marca_la_inversion_y_al_marcarla_la_cuota_es_cero()
    {
        var f = Ficha();
        Assert.False(f.PuedeMarcarIsp);
        f.Isp = true;
        Assert.False(f.Isp);
        foreach (var d in f.Desgloses) d.Cuota = "0";
        Assert.True(f.PuedeMarcarIsp);
        f.Isp = true;
        Assert.True(f.Isp);
        Assert.True(f.CuotaBloqueada);
        f.Desgloses[0].Cuota = "5";
        Assert.Equal(0m, f.CuotaCalculada);
        f.Desgloses[0].Base = "60";
        Assert.Equal(0m, f.Desgloses[0].CuotaValor);
    }

    [Fact]
    public void Una_factura_con_inversion_y_cuota_no_esta_lista()
    {
        var f = Factura();
        f.InversionSujetoPasivo = true;
        Assert.Contains(f.Pendiente(), p => p.Contains("cuota", StringComparison.Ordinal));
    }

    [Fact]
    public void Solo_se_puede_rectificar_una_factura_del_mismo_proveedor()
    {
        var f = Factura();
        f.IdProveedor = null;
        var otras = new List<FilaFacturaCompra>
        {
            new() { IdRegistro = 1, Nif = "B-61465332", NumeroFactura = "A1" },
            new() { IdRegistro = 2, Nif = "A58818501", NumeroFactura = "B1" },
            new() { IdRegistro = 3, IdProveedor = 9, NumeroFactura = "C1" },
        };
        var ficha = Ficha(f, otras: otras);
        Assert.Equal([null, 1], ficha.Rectificables.Select(r => r.Valor));
    }

    [Fact]
    public void Elegir_en_el_visor_deja_cambios_para_guardar_y_se_olvida_al_guardar()
    {
        var f = Ficha();
        f.Elegir("fecha", new ProConta.Compras.Reconocimiento.PuntoDocumento(1, 100, 700));
        f.Elegir("otro", new ProConta.Compras.Reconocimiento.PuntoDocumento(1, 100, 700));
        Assert.True(f.TieneCambios);
        Assert.Equal(["fecha"], f.Elegidos.Keys);
        f.Guardada(f.Construir());
        Assert.Empty(f.Elegidos);
    }
}
