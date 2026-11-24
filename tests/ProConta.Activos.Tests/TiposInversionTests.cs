using ProConta.Activos;
using Xunit;

namespace ProConta.Activos.Tests;

/// <summary>Un tipo de activo es inversión financiera por su categoría, no solo el código INVERSION (propietario 2026-11-24 19:12).</summary>
[Collection(nameof(TiposInversionTests))]
public sealed class TiposInversionTests : IDisposable
{
    public void Dispose() => TiposActivoPgc.RegistrarCategorias([]);

    [Fact]
    public void Inversion_lo_es_siempre_y_un_tipo_nuevo_lo_es_por_su_categoria()
    {
        TiposActivoPgc.RegistrarCategorias([]);
        Assert.True(TiposActivoPgc.EsInversion("INVERSION"));
        Assert.False(TiposActivoPgc.EsInversion("FCR"));

        TiposActivoPgc.RegistrarCategorias([("FCR", "INVERSION"), ("VEHICULO", "MATERIAL"), ("OTRO", null)]);
        Assert.True(TiposActivoPgc.EsInversion("FCR"));
        Assert.True(TiposActivoPgc.EsInversion("INVERSION"));
        Assert.False(TiposActivoPgc.EsInversion("VEHICULO"));
        Assert.False(TiposActivoPgc.EsInversion("OTRO"));
        Assert.Equal("Financieros", ProConta.Activos.Gestion.ReglasFichaActivo.Clase("FCR"));
    }
}
