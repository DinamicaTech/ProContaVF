using ProConta.UI.Base.Tablas;

namespace ProConta.UI.Base.Tests;

/// <summary>«Ajustar todas al contenido»: reparto del espacio entre las columnas de contenido variable (plataforma/ui-base 2026-11-12).</summary>
public class AjusteColumnasTests
{
    [Fact]
    public void SiSobraEspacioCadaVariableRecibeLoQuePideMasSuParteDelSobrante()
    {
        // Concepto pide 300 y Notas 100; quedan 600: sobran 200, que se reparten 150 y 50.
        var anchos = TablaProConta.RepartirVariables([(300, 90), (100, 70)], 600);
        Assert.Equal([450d, 150d], anchos);
    }

    [Fact]
    public void SiNoCabenLaQuePideMenosSeQuedaConLoQuePideYElRestoParaLasDemas()
    {
        // El concepto pide 400 y las notas 1.200; hay 1.000: el concepto entero y 600 para las notas.
        Assert.Equal([400d, 600d], TablaProConta.RepartirVariables([(400, 90), (1200, 70)], 1000));
        // Con 500, a partes iguales: 250 y 250.
        Assert.Equal([250d, 250d], TablaProConta.RepartirVariables([(400, 90), (1200, 70)], 500));
    }

    [Fact]
    public void SinEspacioSeQuedanEnElAnchoDeSuTitulo()
    {
        var anchos = TablaProConta.RepartirVariables([(500, 90), (300, 70)], 0);
        Assert.Equal([90d, 70d], anchos);
    }
}
