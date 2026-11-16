using ProConta.Nominas.Importacion;

namespace ProConta.Nominas.Tests;

public class HuellaNominasTests
{
    [Fact]
    public void El_mismo_contenido_guardado_otra_vez_da_la_misma_huella()
    {
        var primero = ResumenSintetico.Octubre().Xlsx();
        Thread.Sleep(1100); // ClosedXML guarda la fecha de creación: los bytes cambian, el contenido no
        var segundo = ResumenSintetico.Octubre().Xlsx();
        Assert.Equal(ServicioImportacionNominas.Huella(primero), ServicioImportacionNominas.Huella(segundo));
    }

    [Fact]
    public void Otro_periodo_da_otra_huella()
    {
        var octubre = ResumenSintetico.Octubre();
        var otro = ResumenSintetico.Octubre();
        otro.Periodo = "DEL 01/11/2026 AL 30/11/2026";
        Assert.NotEqual(ServicioImportacionNominas.Huella(octubre.Xlsx()), ServicioImportacionNominas.Huella(otro.Xlsx()));
    }

    [Fact]
    public void Un_fichero_que_no_es_Excel_usa_la_huella_de_sus_bytes()
    {
        Assert.Equal(64, ServicioImportacionNominas.Huella([1, 2, 3]).Length);
        Assert.NotEqual(ServicioImportacionNominas.Huella([1, 2, 3]), ServicioImportacionNominas.Huella([1, 2, 4]));
    }
}
