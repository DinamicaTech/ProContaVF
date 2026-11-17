using System.Text;
using ProConta.Contabilidad.Presentacion;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Respuestas de los servicios de la AEAT (contabilidad/modelos-tributarios/presentacion), con los ejemplos de la especificación versión 30
/// y los que ha devuelto el entorno de pruebas, y la comprobación de que una BD de pruebas nunca envía a producción. Sin red.
/// </summary>
public sealed class ClienteAeatTests
{
    [Fact]
    public void LaValidacionDevuelveElPdfOLosErrores()
    {
        var pdf = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 prueba"));
        var ok = ClienteAeatHttp.LeerValidacion("{\"respuesta\":{\"avisos\":[\"31190 - Revise la información adicional.\"],\"pdf\":[\"" + pdf + "\"]}}");
        Assert.True(ok.Correcta);
        Assert.Equal("%PDF-1.4 prueba", Encoding.ASCII.GetString(ok.Pdf!));
        Assert.Single(ok.Avisos);
        var mayusculas = ClienteAeatHttp.LeerValidacion("{\"RESPUESTA\":{\"PDF\":[\"" + pdf + "\"]}}");
        Assert.True(mayusculas.Correcta);
        var mal = ClienteAeatHttp.LeerValidacion("""{"respuesta":{"errores":["E010063 - Se encuentra fuera de plazo para la domiciliación."]}}""");
        Assert.False(mal.Correcta);
        Assert.Null(mal.Pdf);
        Assert.Equal(["E010063 - Se encuentra fuera de plazo para la domiciliación."], mal.Errores);
        Assert.False(ClienteAeatHttp.LeerValidacion("<html>caído</html>").Correcta);
    }

    [Fact]
    public void LaPresentacionCorrectaTraeElCsvYLaDeErrorNo()
    {
        // Ejemplo de la especificación, con el espacio sobrante en el nombre del CSV.
        var r = ClienteAeatHttp.LeerPresentacion("""
            {"respuesta":{"correcta":{"CodigoSeguroVerificacion ":"AAAAAAAAAAAAAAAA","Fecha":"2020-09-03","Hora":"14:04","Expediente":"2020111111111111",
            "NIFPresentador":"999999999","ApellidosNombrePresentador":"XXXX","NIFDeclarante":"88888888","Modelo":"111","Ejercicio":"2020","Periodo":"01",
            "Justificante":"9999999999999","FormaPago":"U","ImporteAIngresar":"123.45","urlPdf":"https://www2.agenciatributaria.gob.es/x",
            "avisos":["20932 - Verifique"],"advertencias":["16412 - No puede aplicarse"]}}}
            """);
        Assert.True(r.Correcta);
        Assert.Equal("AAAAAAAAAAAAAAAA", r.Csv);
        Assert.Equal("9999999999999", r.Justificante);
        Assert.Equal(123.45m, r.ImporteAIngresar);
        Assert.Equal(new DateTime(2020, 9, 3, 14, 4, 0), r.FechaHora);
        Assert.Single(r.Avisos);
        Assert.Single(r.Advertencias);
        var e = ClienteAeatHttp.LeerPresentacion("""{"respuesta":{"errores":["E01 - NRC no válido para el modelo [303]."]}}""");
        Assert.False(e.Correcta);
        Assert.Null(e.Csv);
        Assert.Single(e.Errores);
    }

    [Fact]
    public void LaConsultaDevuelveLoPresentadoYNadaSiNoHayDeclaraciones()
    {
        var c = ClienteAeatHttp.LeerConsulta("""
            <servicioConsultasDirectas><respuestaCorrecta><ejercicio>2026</ejercicio><modelo>303</modelo><periodo>3T</periodo><nif>B00000000</nif>
            <csv>CCECGDY73P9D4G9G</csv><expediente>202630334450001P</expediente><justificante>3037339500045</justificante>
            <fechaYHoraPresentacion>2026-11-17T15:46:00</fechaYHoraPresentacion></respuestaCorrecta></servicioConsultasDirectas>
            """);
        Assert.True(c.Correcta);
        var p = Assert.Single(c.Presentadas);
        Assert.Equal(("3T", "3037339500045", (string?)null), (p.Periodo, p.Justificante, p.JustificanteAnterior));
        var vacia = ClienteAeatHttp.LeerConsulta("<servicioConsultasDirectas><error><descripcionError>Usted no ha presentado la declaración solicitada</descripcionError></error></servicioConsultasDirectas>");
        Assert.True(vacia.Correcta);
        Assert.Empty(vacia.Presentadas);
        var error = ClienteAeatHttp.LeerConsulta("<servicioConsultasDirectas><error><descripcionError>Certificado no válido</descripcionError></error></servicioConsultasDirectas>");
        Assert.False(error.Correcta);
    }

    [Fact]
    public void UnaBdDePruebasNuncaEnviaAProduccionNiAlReves()
    {
        Assert.True(ServiciosAeat.EsDelEntorno(ServiciosAeat.Presentacion(EntornoAeat.Pruebas), EntornoAeat.Pruebas));
        Assert.True(ServiciosAeat.EsDelEntorno(ServiciosAeat.Presentacion(EntornoAeat.Produccion), EntornoAeat.Produccion));
        Assert.False(ServiciosAeat.EsDelEntorno(ServiciosAeat.Presentacion(EntornoAeat.Produccion), EntornoAeat.Pruebas));
        Assert.False(ServiciosAeat.EsDelEntorno(ServiciosAeat.Presentacion(EntornoAeat.Pruebas), EntornoAeat.Produccion));
        Assert.False(ServiciosAeat.EsDelEntorno(ServiciosAeat.Consulta(EntornoAeat.Produccion), EntornoAeat.Pruebas));
        Assert.StartsWith("https://prewww1.aeat.es/", ServiciosAeat.Consulta(EntornoAeat.Pruebas));
        Assert.False(ServiciosAeat.EsDelEntorno("http://prewww1.aeat.es/x", EntornoAeat.Pruebas));
    }

    [Fact]
    public void ElTextoDeLaAeatSeLeeEnUtf8OLatin1()
    {
        Assert.Equal("presentación", ClienteAeatHttp.Texto(Encoding.UTF8.GetBytes("presentación")));
        Assert.Equal("presentación", ClienteAeatHttp.Texto(Encoding.Latin1.GetBytes("presentación")));
    }
}
