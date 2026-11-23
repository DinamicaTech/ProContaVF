using ProConta.Inicio.Bloques;

namespace ProConta.Inicio.Tests;

/// <summary>Etiqueta del certificado de la empresa en la cabecera de Inicio (contabilidad/modelos-tributarios/presentacion/aviso-certificado).</summary>
public sealed class CertificadoTests
{
    private static readonly DateTime Hoy = new(2026, 11, 23);

    private static EstadoCertificado Con(int dias) =>
        EstadoCertificado.Calcular(new DatosCertificado(true, true, "EMPRESA DE PRUEBA SL", "B00000000", Hoy.AddDays(dias).AddHours(13)), Hoy);

    [Theory]
    [InlineData(61, SemaforoCertificado.Verde)]
    [InlineData(365, SemaforoCertificado.Verde)]
    [InlineData(60, SemaforoCertificado.Amarillo)]
    [InlineData(45, SemaforoCertificado.Amarillo)]
    [InlineData(30, SemaforoCertificado.Amarillo)]
    [InlineData(29, SemaforoCertificado.Rojo)]
    [InlineData(20, SemaforoCertificado.Rojo)]
    [InlineData(0, SemaforoCertificado.Rojo)]
    [InlineData(-5, SemaforoCertificado.Rojo)]
    public void ElColorDependeDeLosDiasQueFaltan(int dias, SemaforoCertificado esperado)
    {
        var e = Con(dias);
        Assert.Equal(esperado, e.Semaforo);
        Assert.Equal(dias, e.Dias);
    }

    [Fact]
    public void ElTextoLlevaElNombreYLaFechaDeCaducidad()
    {
        var e = Con(20);
        Assert.Equal("EMPRESA DE PRUEBA SL · caduca el 13/12/2026 (20 días)", e.Texto);
        Assert.Contains("NIF B00000000", e.Ayuda);
        Assert.Contains("Certificado de la empresa", e.Ayuda);
        Assert.Contains("renuévalo", e.Ayuda);
    }

    [Fact]
    public void UnCertificadoCaducadoLoDice()
    {
        var e = Con(-3);
        Assert.Equal("EMPRESA DE PRUEBA SL · caducó el 20/11/2026", e.Texto);
        Assert.Contains("Ya ha caducado", e.Ayuda);
    }

    [Fact]
    public void SinCertificadoElegidoSaleEnGris()
    {
        var e = EstadoCertificado.Calcular(DatosCertificado.SinElegir, Hoy);
        Assert.Equal(SemaforoCertificado.Gris, e.Semaforo);
        Assert.Equal("Sin certificado de la empresa", e.Texto);
        Assert.Null(e.Dias);
    }

    [Fact]
    public void UnCertificadoQueNoEstaEnEsteEquipoSaleEnRojo()
    {
        var e = EstadoCertificado.Calcular(DatosCertificado.NoInstalado, Hoy);
        Assert.Equal(SemaforoCertificado.Rojo, e.Semaforo);
        Assert.Equal("Certificado no instalado en este equipo", e.Texto);
    }

    [Fact]
    public async Task ElServicioCalculaConLaFechaDeHoy()
    {
        var servicio = new ServicioBloques(new FuenteCertificado(new DatosCertificado(true, true, "X", null, Hoy.AddDays(100))), () => Hoy);
        var e = await servicio.CertificadoAsync("S1");
        Assert.Equal(SemaforoCertificado.Verde, e.Semaforo);
        Assert.Equal(100, e.Dias);
    }

    private sealed class FuenteCertificado(DatosCertificado datos) : IFuenteBloques
    {
        public Task<IReadOnlyList<int>> EjerciciosAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<int>>([]);
        public Task<IReadOnlyList<ReservaOcupacion>> ReservasAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<ReservaOcupacion>>([]);
        public Task<IReadOnlyList<AlojamientoOcupacion>> AlojamientosAsync(string idSociedad, CancellationToken c = default) => Task.FromResult<IReadOnlyList<AlojamientoOcupacion>>([]);
        public Task<IReadOnlyList<ProConta.Facturacion.Programadas.VencimientoProgramado>> ProgramadasAsync(string idSociedad, DateTime hasta, CancellationToken c = default) =>
            Task.FromResult<IReadOnlyList<ProConta.Facturacion.Programadas.VencimientoProgramado>>([]);
        public Task<DatosCertificado> CertificadoAsync(string idSociedad, CancellationToken c = default) => Task.FromResult(datos);
    }
}
