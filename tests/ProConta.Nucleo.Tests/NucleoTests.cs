using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Importes;
using ProConta.Nucleo.Resultados;

namespace ProConta.Nucleo.Tests;

public class ImporteTests
{
    [Theory]
    [InlineData("0.005", "0.01")]
    [InlineData("-0.005", "-0.01")]
    [InlineData("2.675", "2.68")]
    [InlineData("1.004999", "1.00")]
    [InlineData("10.125", "10.13")]
    [InlineData("-10.125", "-10.13")]
    public void RedondeaADosDecimalesAwayFromZero(string entrada, string esperado)
    {
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture),
            Importe.Redondear(decimal.Parse(entrada, System.Globalization.CultureInfo.InvariantCulture)).Valor);
    }

    [Fact]
    public void RedondeaPorLineaYDespuesSuma()
    {
        // Tres líneas de 0,335 €: por línea 0,34 × 3 = 1,02 (no 1,01 del total redondeado).
        var lineas = Enumerable.Repeat(Importe.Redondear(0.335m), 3);
        Assert.Equal(1.02m, Importe.Sumar(lineas).Valor);
    }

    [Fact]
    public void PorcentajeRedondeado()
    {
        Assert.Equal(2.10m, Importe.Redondear(10m).Porcentaje(21m).Valor);
        Assert.Equal(0.04m, Importe.Redondear(0.19m).Porcentaje(21m).Valor); // 0,0399
        Assert.Equal(0.11m, Importe.Redondear(0.50m).Porcentaje(21m).Valor); // 0,105
    }

    [Fact]
    public void OperadoresYComparaciones()
    {
        var a = Importe.Redondear(10.10m);
        var b = Importe.Redondear(0.20m);
        Assert.Equal(10.30m, (a + b).Valor);
        Assert.Equal(9.90m, (a - b).Valor);
        Assert.True(a > b);
        Assert.Equal(-10.10m, (-a).Valor);
        Assert.Equal("10.10", a.ToString());
        Assert.True(Importe.Cero.EsCero);
    }
}

public class ResultadoTests
{
    [Fact]
    public void ResultadoCorrectoTieneValor()
    {
        var r = Resultado<int>.Correcto(5);
        Assert.True(r.EsCorrecto);
        Assert.Equal(5, r.Valor);
        Assert.Equal(10, r.Map(v => v * 2).Valor);
    }

    [Fact]
    public void ResultadoFallidoNoDaValorYConservaElError()
    {
        var error = new Error("X.Prueba", "No he podido guardar.", "Tercero 1", "La BD está bloqueada.", "Vuelve a intentarlo.");
        var r = Resultado<int>.Fallo(error);
        Assert.False(r.EsCorrecto);
        Assert.Throws<InvalidOperationException>(() => r.Valor);
        Assert.Same(error, r.Map(v => v.ToString()).Error);
        Assert.Contains("Qué hacer: Vuelve a intentarlo.", error.TextoOperador());
        Assert.Contains("Registro: Tercero 1.", error.TextoOperador());
    }

    [Fact]
    public void CombinarReuneTodosLosErrores()
    {
        var r = Resultado.Combinar(Resultado.Correcto(),
            Resultado.Fallo(new Error("A", "a")), Resultado.Fallo(new Error("B", "b")));
        Assert.Equal(new[] { "A", "B" }, r.Errores.Select(e => e.Codigo));
    }
}

public class AuditoriaTests
{
    private sealed class RelojFijo(DateTime ahora) : IReloj
    {
        public DateTime Ahora { get; } = ahora;
    }

    [Fact]
    public void MarcaRegistraActorInstanteYOperacion()
    {
        var instante = new DateTime(2026, 10, 31, 18, 0, 0);
        var marca = MarcaAuditoria.Crear(new Actor("operador.prueba"), "Tercero.Alta", new RelojFijo(instante), "001/2026");
        Assert.Equal("operador.prueba", marca.Actor.Nombre);
        Assert.Equal(instante, marca.Instante);
        Assert.Equal("Tercero.Alta", marca.Operacion);
        Assert.NotNull(marca.Correlacion);
        Assert.Equal("proceso:Programadas", Actor.Proceso("Programadas").ToString());
    }
}
