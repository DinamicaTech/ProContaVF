namespace ProConta.Correo.Tests;

/// <summary>Reglas sin BD: direcciones, mensaje, reintentos, desvío y plantillas.</summary>
public sealed class ReglasTests
{
    [Theory]
    [InlineData("cliente@example.es", true)]
    [InlineData(" cliente@example.es; ", true)]
    [InlineData("cliente@example", false)]
    [InlineData("cliente.example.es", false)]
    [InlineData("a@@example.es", false)]
    [InlineData("con espacio@example.es", false)]
    [InlineData("", false)]
    public void Direcciones(string texto, bool valida)
    {
        var (d, motivo) = ReglasMensaje.Normalizar(texto);
        Assert.Equal(valida, d is not null);
        if (!valida) Assert.False(string.IsNullOrWhiteSpace(motivo));
    }

    [Fact]
    public void Separa_varias_direcciones() =>
        Assert.Equal(["a@x.es", "b@x.es", "c@x.es"], ReglasMensaje.Separar("a@x.es; b@x.es,c@x.es\n"));

    private static MensajeNuevo Mensaje(params Destinatario[] d) => new() { IdSociedadEjercicio = 1, Asunto = "Factura", Destinatarios = d };

    [Fact]
    public void Mensaje_correcto() => Assert.Empty(ReglasMensaje.Validar(Mensaje(new Destinatario(TipoDestinatario.Para, "a@x.es"))));

    [Fact]
    public void Sin_para_sin_asunto_y_direccion_mal_escrita()
    {
        var errores = ReglasMensaje.Validar(new MensajeNuevo { IdSociedadEjercicio = 1, Destinatarios = [new(TipoDestinatario.Cc, "a@x")] });
        Assert.Contains(errores, e => e.Codigo == "Correo.SinDestinatario");
        Assert.Contains(errores, e => e.Codigo == "Correo.SinAsunto");
        Assert.Contains(errores, e => e.Codigo == "Correo.DireccionNoValida");
        Assert.All(errores, e => Assert.False(string.IsNullOrWhiteSpace(e.AccionCorrectora)));
    }

    [Fact]
    public void Direccion_repetida_en_para_y_cc() =>
        Assert.Contains(ReglasMensaje.Validar(Mensaje(new Destinatario(TipoDestinatario.Para, "a@x.es"), new Destinatario(TipoDestinatario.Cc, "A@x.es"))), e => e.Codigo == "Correo.DireccionRepetida");

    [Fact]
    public void Reintentos_a_los_5_30_120_minutos_y_16_horas()
    {
        var t = new DateTime(2026, 11, 10, 10, 0, 0);
        Assert.Equal(t.AddMinutes(5), PoliticaReintentos.Siguiente(1, t));
        Assert.Equal(t.AddMinutes(30), PoliticaReintentos.Siguiente(2, t));
        Assert.Equal(t.AddHours(2), PoliticaReintentos.Siguiente(3, t));
        Assert.Equal(t.AddHours(16), PoliticaReintentos.Siguiente(4, t));
        Assert.Null(PoliticaReintentos.Siguiente(5, t));
    }

    [Fact]
    public void El_desvio_anota_los_destinatarios_reales()
    {
        var reales = new[] { new Destinatario(TipoDestinatario.Para, "cliente@x.es"), new Destinatario(TipoDestinatario.Cco, "oculto@x.es") };
        Assert.StartsWith("[PRUEBAS]", DesvioPruebas.Asunto("Factura"));
        var html = DesvioPruebas.CuerpoHtml("<p>Hola</p>", reales)!;
        Assert.Contains("cliente@x.es", html);
        Assert.Contains("CCO oculto@x.es", html);
        Assert.EndsWith("<p>Hola</p>", html);
    }

    [Fact]
    public void Plantilla_rellena_lo_que_conoce_y_deja_ver_lo_demas()
    {
        var texto = ServicioPlantillasCorreo.Rellenar("Factura {NumeroFactura} de {Empresa} para {Desconocida}",
            new Dictionary<string, string?> { ["NumeroFactura"] = "F-1", ["Empresa"] = "A & B" }, html: true);
        Assert.Equal("Factura F-1 de A &amp; B para {Desconocida}", texto);
        Assert.Equal(["{Desconocida}"], ServicioPlantillasCorreo.SinRellenar(texto));
    }

    [Fact]
    public void Texto_plano_de_html() =>
        Assert.Equal("Hola:\nTe enviamos la factura.\n• uno", TextoPlano.DeHtml("<p>Hola:</p><p>Te enviamos la factura.</p><ul><li>uno</li></ul>"));
}
