using ProConta.Documentos;

namespace ProConta.Documentos.Tests;

/// <summary>Lo que se propone al leer un fichero (documentos 2026-11-18, casos de la maqueta validada), sin BD ni disco.</summary>
public sealed class LectorPropuestasTests
{
    private static readonly Vinculo Empresa = new(TiposEntidad.Empresa, null, "T01", "Sociedad sintética SL");

    private static DatosPropuesta Datos() => new("B12345674", Empresa,
        [
            new PresentacionConocida("303", 2026, "3T", "3036000234567", "ABCDEFGH12345678", new DateTime(2026, 10, 20, 10, 30, 0)),
            new PresentacionConocida("111", 2026, "3T", "1116000111111", null, new DateTime(2026, 10, 19)),
        ],
        [new TerceroConocido(7, "A22222222", "Seguros Atalaya SA"), new TerceroConocido(8, "B12345674", "La propia sociedad como tercero")]);

    [Fact]
    public void Justificante_por_su_numero_es_seguro_y_va_al_modelo()
    {
        var p = LectorPropuestas.Proponer("Agencia Tributaria. Justificante de presentación. Modelo 303. Número de justificante: 3036000234567. Fecha 20/10/2026",
            "Justificante_303_3T_2026.pdf", Datos());
        Assert.Equal(Confianza.Alta, p.Confianza);
        Assert.Equal("MODELO", p.Tipo);
        Assert.Equal(new DateTime(2026, 10, 20), p.Fecha);
        Assert.Equal("303 3T 2026 · justificante", p.Titulo);
        var v = Assert.Single(p.Vinculos);
        Assert.Equal("303|2026|3T", v.Clave);
        Assert.Equal("303 · 3T 2026", v.Etiqueta);
        Assert.Contains("3036000234567", p.Motivo);
    }

    [Fact]
    public void Justificante_por_su_csv()
    {
        var p = LectorPropuestas.Proponer("Código seguro de verificación ABCDEFGH12345678", "x.pdf", Datos());
        Assert.Equal(Confianza.Alta, p.Confianza);
        Assert.Equal("303|2026|3T", Assert.Single(p.Vinculos).Clave);
    }

    [Fact]
    public void Notificacion_que_cita_un_modelo_se_revisa()
    {
        var p = LectorPropuestas.Proponer("AGENCIA TRIBUTARIA. Notificación de propuesta de liquidación provisional. Modelo 303, ejercicio 2026, periodo 1T. " +
                                          "Madrid, 3 de noviembre de 2026", "notificacion_AEAT_8812.pdf", Datos());
        Assert.Equal("NOTIFICACION", p.Tipo);
        Assert.Equal(Confianza.Media, p.Confianza);
        Assert.Equal(new DateTime(2026, 11, 3), p.Fecha);
        Assert.Equal("Propuesta de liquidación 303 1T 2026", p.Titulo);
        Assert.Equal("303|2026|1T", Assert.Single(p.Vinculos).Clave);
    }

    [Fact]
    public void Una_notificacion_que_cita_el_justificante_no_es_el_justificante()
    {
        var p = LectorPropuestas.Proponer("Agencia Tributaria. Requerimiento. Modelo 303 ejercicio 2026 periodo 3T, justificante 3036000234567", "req.pdf", Datos());
        Assert.Equal("NOTIFICACION", p.Tipo);
    }

    [Fact]
    public void Poliza_con_el_nif_de_un_tercero_es_segura()
    {
        var p = LectorPropuestas.Proponer("Póliza de seguro multirriesgo del local. Asegurador: SEGUROS ATALAYA SA, NIF A-22.222.222. Fecha de efecto 10/01/2026",
            "Poliza_seguro_local.pdf", Datos());
        Assert.Equal("CONTRATO", p.Tipo);
        Assert.Equal(Confianza.Alta, p.Confianza);
        var v = Assert.Single(p.Vinculos);
        Assert.Equal((TiposEntidad.Tercero, 7), (v.TipoEntidad, v.IdEntidad!.Value));
        Assert.Equal(new DateTime(2026, 1, 10), p.Fecha);
    }

    [Fact]
    public void El_nif_de_la_propia_sociedad_no_es_un_tercero()
    {
        var p = LectorPropuestas.Proponer("Contrato de prestación de servicios. Cliente: B12345674", "c.pdf", Datos());
        Assert.DoesNotContain(p.Vinculos, v => v.TipoEntidad == TiposEntidad.Tercero);
    }

    [Fact]
    public void Escritura_va_a_la_empresa_con_su_titulo()
    {
        var p = LectorPropuestas.Proponer("ESCRITURA DE AMPLIACIÓN DE CAPITAL otorgada por SOCIEDAD SINTÉTICA SL. En Madrid, a 15 de diciembre de 2025, ante mí, notario",
            "Escritura_ampliacion_capital.pdf", Datos());
        Assert.Equal("ESCRITURA", p.Tipo);
        Assert.Equal("Escritura de ampliación de capital", p.Titulo);
        Assert.Equal(new DateTime(2025, 12, 15), p.Fecha);
        Assert.Contains(p.Vinculos, v => v.TipoEntidad == TiposEntidad.Empresa);
        Assert.Equal(Confianza.Media, p.Confianza);
    }

    [Fact]
    public void Sin_texto_no_propone_nada()
    {
        var p = LectorPropuestas.Proponer("", "IMG_2201.jpg", Datos());
        Assert.Equal(Confianza.Ninguna, p.Confianza);
        Assert.Null(p.Tipo);
        Assert.Null(p.Fecha);
        Assert.Equal("IMG 2201", p.Titulo);
        Assert.Empty(p.Vinculos);
        Assert.Contains("imagen", p.Motivo);
    }

    [Fact]
    public void Una_factura_de_compra_se_manda_a_compras()
    {
        var p = LectorPropuestas.Proponer("FACTURA nº FAC-2026-0412. Proveedor A22222222. Cliente: Sociedad sintética SL, B-12345674. Base imponible 100,00 IVA 21% 21,00 Total 121,00",
            "FAC-2026-0412.pdf", Datos());
        Assert.Equal(Confianza.FacturaCompra, p.Confianza);
        Assert.Contains("Compras", p.Motivo);
    }

    [Theory]
    [InlineData("1T", "1T"), InlineData("0A", ""), InlineData("01", "enero"), InlineData("2P", "2P")]
    public void Texto_del_periodo(string periodo, string esperado) => Assert.Equal(esperado, Vinculo.PeriodoTexto(periodo));

    [Fact]
    public void Etiquetas_de_modelos()
    {
        Assert.Equal("390 · 2025", Vinculo.EtiquetaModelo("390", 2025, "0A"));
        Assert.Equal("111 · octubre 2026", Vinculo.EtiquetaModelo("111", 2026, "10"));
    }

    [Fact]
    public void Nifs_con_separadores()
    {
        Assert.Contains("A22222222", LectorPropuestas.NifsDe("NIF: A-22.222.222"));
        Assert.Contains("12345678Z", LectorPropuestas.NifsDe("DNI 12345678-Z"));
    }
}
