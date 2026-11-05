using ProConta.Compras.Reconocimiento;
using static ProConta.Compras.Tests.Documentos;

namespace ProConta.Compras.Tests;

/// <summary>Plantillas por proveedor: aprender dónde está cada campo al guardar y leerlo así en la siguiente factura.</summary>
public sealed class PlantillasReconocimientoTests
{
    private const string Abril = """
        Estación de servicio Ficticia
        CIF: B61465332
        Factura simplificada nº T-0001234
        Fecha emisión: 23/04/2026
        Vencimiento: 23/05/2026
        Gasóleo 40,00 litros
        Base imponible 41,32
        IVA 21% 8,68
        TOTAL A PAGAR 50,00 €
        """;

    private const string Mayo = """
        Estación de servicio Ficticia
        CIF: B61465332
        Factura simplificada nº T-0001301
        Fecha emisión: 15/05/2026
        Vencimiento: 15/06/2026
        Gasóleo 80,00 litros
        Base imponible 1.033,06
        IVA 21% 216,94
        TOTAL A PAGAR 1.250,00 €
        """;

    [Theory]
    [InlineData(PlantillasReconocimiento.Fecha, "2026-04-23", "fecha emision", "2026-05-15")]
    [InlineData(PlantillasReconocimiento.Total, "50.00", "total a pagar", "1250.00")]
    [InlineData(PlantillasReconocimiento.Numero, "T0001234", "factura simplificada nº", "T0001301")]
    [InlineData(PlantillasReconocimiento.Nif, "B61465332", "cif", "B61465332")]
    public void Aprende_el_rotulo_de_la_misma_linea_y_lo_lee_en_la_siguiente(string campo, string valor, string ancla, string esperado)
    {
        var p = PlantillasReconocimiento.Aprender(Maquetado(Abril), campo, valor);
        Assert.NotNull(p);
        Assert.Equal((ancla, PlantillasReconocimiento.Derecha), (p!.Ancla, p.Posicion));
        Assert.Equal(esperado, PlantillasReconocimiento.Leer(Maquetado(Mayo), p));
    }

    [Fact]
    public void No_toma_la_fecha_de_vencimiento_aunque_tambien_sea_una_fecha()
    {
        var p = PlantillasReconocimiento.Aprender(Maquetado(Abril), PlantillasReconocimiento.Fecha, "2026-05-23")!;
        Assert.Equal("vencimiento", p.Ancla);
        Assert.Equal("2026-06-15", PlantillasReconocimiento.Leer(Maquetado(Mayo), p));
    }

    [Fact]
    public void Aprende_el_rotulo_de_encima_en_una_tabla()
    {
        const string a = """
            Proveedor Ficticio
            Factura      Fecha        Cliente
            FV-26-001    02/03/2026   C001
            """;
        const string b = """
            Proveedor Ficticio
            Factura      Fecha        Cliente
            FV-26-077    19/04/2026   C001
            """;
        var fecha = PlantillasReconocimiento.Aprender(Maquetado(a), PlantillasReconocimiento.Fecha, "2026-03-02")!;
        Assert.Equal(("fecha", PlantillasReconocimiento.Debajo), (fecha.Ancla, fecha.Posicion));
        Assert.Equal("2026-04-19", PlantillasReconocimiento.Leer(Maquetado(b), fecha));
        var numero = PlantillasReconocimiento.Aprender(Maquetado(a), PlantillasReconocimiento.Numero, "FV26001")!;
        Assert.Equal("FV26077", PlantillasReconocimiento.Leer(Maquetado(b), numero));
    }

    [Fact]
    public void Sin_el_valor_en_el_PDF_o_sin_el_rotulo_no_hay_plantilla_ni_lectura()
    {
        Assert.Null(PlantillasReconocimiento.Aprender(Maquetado(Abril), PlantillasReconocimiento.Fecha, "2026-01-01"));
        var p = PlantillasReconocimiento.Aprender(Maquetado(Abril), PlantillasReconocimiento.Fecha, "2026-04-23")!;
        Assert.Null(PlantillasReconocimiento.Leer(Maquetado("Otro formato\nDate 15/05/2026"), p));
        Assert.Null(PlantillasReconocimiento.Leer(Maquetado("Fecha emisión: pendiente"), p));
    }

    [Theory]
    [InlineData(PlantillasReconocimiento.Fecha, "23.04.2026", "2026-04-23", true)]
    [InlineData(PlantillasReconocimiento.Fecha, "23 de abril de 2026", "2026-04-23", true)]
    [InlineData(PlantillasReconocimiento.Total, "1.250,00 €", "1250.00", true)]
    [InlineData(PlantillasReconocimiento.Total, "1.250,01", "1250.00", false)]
    [InlineData(PlantillasReconocimiento.Nif, "ES-B61465332", "B61465332", true)]
    [InlineData(PlantillasReconocimiento.Numero, "FV-26/001", "FV26001", true)]
    [InlineData(PlantillasReconocimiento.Numero, "FV-26/002", "FV26001", false)]
    public void Coincide_compara_valores_normalizados(string campo, string leido, string guardado, bool igual) =>
        Assert.Equal(igual, PlantillasReconocimiento.Coincide(campo, leido, guardado));

    [Fact]
    public void Aplicar_pone_el_campo_al_100_y_quita_su_aviso()
    {
        var p = PlantillasReconocimiento.Aprender(Maquetado(Abril), PlantillasReconocimiento.Fecha, "2026-04-23")!;
        var r = new ResultadoReconocimientoFacturaCompra
        {
            FechaFactura = new DateTime(2026, 6, 15), ConfianzaFecha = 0.85m,
            Advertencias = ["No se ha identificado la fecha de factura.", "Otro aviso."]
        };
        ServicioReconocimientoFacturaCompra.AplicarPlantillas(Maquetado(Mayo), r, [p]);
        Assert.Equal((new DateTime(2026, 5, 15), 1m), (r.FechaFactura!.Value, r.ConfianzaFecha));
        Assert.Equal(["fecha"], r.CamposPlantilla);
        Assert.Equal(["Otro aviso."], r.Advertencias);
    }

    [Fact]
    public void Aplicar_un_total_sube_los_importes_al_100_solo_si_cuadran()
    {
        var p = PlantillasReconocimiento.Aprender(Maquetado(Abril), PlantillasReconocimiento.Total, "50.00")!;
        var cuadra = new ResultadoReconocimientoFacturaCompra { BaseImponible = 1033.06m, CuotaIva = 216.94m, ImporteTotal = 1033.06m, ConfianzaImportes = 0.7m };
        ServicioReconocimientoFacturaCompra.AplicarPlantillas(Maquetado(Mayo), cuadra, [p]);
        Assert.Equal((1250m, 1m), (cuadra.ImporteTotal!.Value, cuadra.ConfianzaImportes));
        var noCuadra = new ResultadoReconocimientoFacturaCompra { BaseImponible = 1000m, CuotaIva = 210m, ConfianzaImportes = 0.7m };
        ServicioReconocimientoFacturaCompra.AplicarPlantillas(Maquetado(Mayo), noCuadra, [p]);
        Assert.Equal((1250m, 0.7m), (noCuadra.ImporteTotal!.Value, noCuadra.ConfianzaImportes));
    }
}
