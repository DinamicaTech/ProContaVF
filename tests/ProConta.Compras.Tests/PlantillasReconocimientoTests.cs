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

    private const string ConceptoMarzo = """
        Proveedor Ficticio
        Concepto                         Importe    IVA
        Cuota Fusion Total Plus 600Mb    45,00 €    21 %
        """;
    private const string ConceptoAbril = """
        Proveedor Ficticio
        Concepto                         Importe    IVA
        Cuota Fusion Total Plus 1Gb      55,00 €    21 %
        """;

    [Theory]
    [InlineData("Cuota Fusion Total Plus 600Mb")]
    [InlineData("Cuota Fusion Total Plus 600Mb 45,00 € 21 %")] // la línea entera marcada en el visor
    public void El_concepto_se_lee_hasta_el_primer_importe_de_la_linea(string valor)
    {
        var p = PlantillasReconocimiento.Aprender(Maquetado(ConceptoMarzo), PlantillasReconocimiento.Concepto, valor);
        Assert.NotNull(p);
        Assert.Equal(("concepto", PlantillasReconocimiento.Debajo), (p!.Ancla, p.Posicion));
        Assert.Equal("Cuota Fusion Total Plus 1Gb", PlantillasReconocimiento.Leer(Maquetado(ConceptoAbril), p));
    }

    [Fact]
    public void El_concepto_se_corta_en_un_hueco_grande_y_conserva_el_numero_final()
    {
        var p = PlantillasReconocimiento.Aprender(Maquetado("Concepto\nLinea movil 2        Datos 10 GB"), PlantillasReconocimiento.Concepto, "Linea movil 2");
        Assert.NotNull(p);
        Assert.Equal("Linea movil 3", PlantillasReconocimiento.Leer(Maquetado("Concepto\nLinea movil 3        Datos 20 GB"), p));
    }

    [Fact]
    public void El_concepto_de_varias_lineas_se_lee_entero_bajo_su_rotulo()
    {
        const string julio = """
            Asesoria Ficticia
            CONCEPTO DESCRIPCION                                          CARGOS   ABONOS
            CUOFIS   Asesoramiento Contable-Fiscal del mes de Julio de 2026   363,00
                     s/contrato profesional formado por ambas partes-
            TOTAL FACTURA 363,00
            """;
        const string agosto = """
            Asesoria Ficticia
            CONCEPTO DESCRIPCION                                          CARGOS   ABONOS
            CUOFIS   Asesoramiento Contable-Fiscal del mes de Agosto de 2026  363,00
                     s/contrato profesional formado por ambas partes-
            TOTAL FACTURA 363,00
            """;
        var p = PlantillasReconocimiento.Aprender(Maquetado(julio), PlantillasReconocimiento.Concepto,
            "Asesoramiento Contable-Fiscal del mes de Julio de 2026 s/contrato profesional formado por ambas partes");
        Assert.NotNull(p);
        Assert.Equal("Asesoramiento Contable-Fiscal del mes de Agosto de 2026 s/contrato profesional formado por ambas partes",
            PlantillasReconocimiento.Leer(Maquetado(agosto), p!));
    }

    /// <summary>Como una factura de Amazon: interlineado de 13 o 14 puntos con letra de 5 de alto, y líneas sueltas entre el rótulo y el concepto.</summary>
    private static DocumentoFacturaExtraido Interlineado(params (double X, double Y, string Texto)[] lineas)
    {
        var palabras = new List<PalabraDocumento>();
        foreach (var (x, y, texto) in lineas)
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(texto, @"\S+"))
                palabras.Add(new PalabraDocumento { Pagina = 1, Texto = m.Value, X = x + m.Index * 4, Y = y, Ancho = m.Length * 4, Alto = 5 });
        return new() { Ruta = @"X:\Datos\Soc\2026\Proveedores\Ficticio\factura.pdf", Paginas = [new PaginaDocumento { Numero = 1, Texto = string.Join("\n", lineas.Select(l => l.Texto)), Palabras = palabras }] };
    }

    [Fact]
    public void El_concepto_de_varias_lineas_con_interlineado_amplio_y_lineas_sueltas_bajo_el_rotulo()
    {
        var julio = Interlineado(
            (38, 398, "Descripción                                                          Cant.   P. Unitario    IVA %      Precio total"),
            (378, 385, "(IVA excluido)                         (IVA incluido)"),
            (464, 370, "(1)"),
            (38, 367, "C Llave de Gancho para Llave Inglesa, Herramienta de Llave de Gancho de          1   17,17 €  0%   17,17 €"),
            (38, 353, "10 Piezas, Juego de Llaves de Tensor de Amortiguador, Herramienta de"),
            (38, 340, "Reparación de Llaves Roscadas"),
            (38, 326, "ASIN: B0FK25R5Z9"),
            (38, 300, "Total 17,17 €"));
        var agosto = Interlineado(
            (38, 399, "Descripción                                                          Cant.   P. Unitario    IVA %      Precio total"),
            (378, 385, "(IVA excluido)                         (IVA incluido)"),
            (464, 370, "(1)"),
            (38, 367, "SWAWIS 4 Piezas Juego de Cabeza Cuadrada C Llave de Gancho          1   35,53 €  0%   35,53 €"),
            (38, 354, "Ajustable 19-170mm, Acero al Cromo-Vanadio, para Amortiguadores de"),
            (38, 341, "Motocicleta"),
            (38, 327, "ASIN: B0FCMD8XXX"),
            (38, 300, "Total 35,53 €"));
        var p = PlantillasReconocimiento.Aprender(julio, PlantillasReconocimiento.Concepto,
            "C Llave de Gancho para Llave Inglesa, Herramienta de Llave de Gancho de 10 Piezas, Juego de Llaves de Tensor de Amortiguador, Herramienta de Reparación de Llaves Roscadas");
        Assert.NotNull(p);
        Assert.Equal("SWAWIS 4 Piezas Juego de Cabeza Cuadrada C Llave de Gancho Ajustable 19-170mm, Acero al Cromo-Vanadio, para Amortiguadores de Motocicleta",
            PlantillasReconocimiento.Leer(agosto, p!));
    }

    [Fact]
    public void Un_concepto_sin_rotulo_que_se_repite_como_titulo_se_aprende_como_texto_fijo()
    {
        const string julio = """
                         Factura de llum
            Electrica Ficticia SA
            Periodo 11/07/2026 - 11/08/2026
            Total 84,12 €
            """;
        const string agosto = """
                         Factura de llum
            Electrica Ficticia SA
            Periodo 11/08/2026 - 08/09/2026
            Total 91,40 €
            """;
        var p = PlantillasReconocimiento.Aprender(Maquetado(julio), PlantillasReconocimiento.Concepto, "Factura de llum");
        Assert.NotNull(p);
        Assert.Equal(PlantillasReconocimiento.Texto, p!.Posicion);
        Assert.Equal("Factura de llum", PlantillasReconocimiento.Leer(Maquetado(agosto), p));
        Assert.Null(PlantillasReconocimiento.Leer(Maquetado("Otra factura\nTotal 10,00"), p));
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
    [Fact]
    public void Si_el_operador_elige_donde_esta_la_fecha_aprende_de_ese_sitio_aunque_aparezca_en_otro()
    {
        // La fecha de cargo coincide con la de la factura: sin elegir, cualquiera de las dos valdría hoy.
        const string doble = """
            Proveedor Ficticio
            Fecha de cargo: 23/04/2026
            Fecha emisión: 23/04/2026
            TOTAL 50,00
            """;
        var documento = Maquetado(doble);
        // «23/04/2026» empieza en la columna 15 (X = 40 + 15 × 5) en las líneas 1 y 2 (Y = 800 − línea × 12).
        var cargo = PlantillasReconocimiento.Aprender(documento, PlantillasReconocimiento.Fecha, "2026-04-23", new PuntoDocumento(1, 130, 788 + 4), soloAhi: true);
        var emision = PlantillasReconocimiento.Aprender(documento, PlantillasReconocimiento.Fecha, "2026-04-23", new PuntoDocumento(1, 130, 776 + 4), soloAhi: true);
        Assert.Equal("fecha de cargo", cargo!.Ancla);
        Assert.Equal("fecha emision", emision!.Ancla);
        Assert.False(PlantillasReconocimiento.MismoSitio(cargo, emision));
        // Un punto donde no está el valor: con soloAhi no aprende; sin él, aprende de la mejor aparición.
        Assert.Null(PlantillasReconocimiento.Aprender(documento, PlantillasReconocimiento.Fecha, "2026-04-23", new PuntoDocumento(1, 300, 700), soloAhi: true));
        Assert.NotNull(PlantillasReconocimiento.Aprender(documento, PlantillasReconocimiento.Fecha, "2026-04-23", new PuntoDocumento(1, 300, 700)));
    }
}
