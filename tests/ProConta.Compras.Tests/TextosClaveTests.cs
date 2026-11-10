using ProConta.Compras.Facturas;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Tests;

/// <summary>Reglas de los textos clave de compras (compras/pantallas 2026-11-06 17:02).</summary>
public sealed class TextosClaveTests
{
    private static TextoClaveCompra Texto(string texto, string? cuenta = null, int? centro = null, bool enEjercicio = true) =>
        new(0, texto, cuenta is null ? null : 1, cuenta, null, enEjercicio && cuenta is not null, centro, null);

    [Fact]
    public void Se_busca_en_el_contenido_y_en_la_ruta_sin_mayusculas_ni_acentos()
    {
        var textos = new[] { Texto("Electricidad", "6280001"), Texto("Oficina Málaga", centro: 7) };
        var a = ReglasTextosClave.Elegir(textos, "Factura de ELECTRICIDAD   febrero", @"D:\Docs\2026\Proveedores\Iberdrola\Oficina Malaga\f.pdf");
        Assert.Equal(("6280001", (int?)7), (a.Cuenta, a.IdCentroCoste));
        Assert.Empty(a.Avisos);
    }

    [Fact]
    public void Gana_el_texto_mas_largo_y_cuenta_y_centro_se_eligen_por_separado()
    {
        var textos = new[] { Texto("luz", "6280001", 3), Texto("luz oficina", "6280002"), Texto("nada", "6000001") };
        var a = ReglasTextosClave.Elegir(textos, "consumo de luz oficina centro", null);
        Assert.Equal(("6280002", (int?)3), (a.Cuenta, a.IdCentroCoste));
    }

    [Fact]
    public void Con_empate_en_longitud_y_valores_distintos_no_se_asigna_y_se_avisa()
    {
        var textos = new[] { Texto("agua", "6280003", 4), Texto("gas.", "6280004", 4) };
        var a = ReglasTextosClave.Elegir(textos, "agua y gas. del local", null);
        Assert.Null(a.Cuenta);
        Assert.True(a.EmpateCuenta);
        Assert.Equal((int?)4, a.IdCentroCoste);
        Assert.False(a.EmpateCentro);
        Assert.Single(a.Avisos);
        Assert.Contains("«agua» y «gas.»", a.Avisos[0]);
    }

    [Fact]
    public void Una_cuenta_que_no_existe_en_el_ejercicio_no_se_asigna()
    {
        var a = ReglasTextosClave.Elegir([Texto("seguro", "6250001", enEjercicio: false)], "póliza de seguro", null);
        Assert.Null(a.Cuenta);
        Assert.False(a.EmpateCuenta);
    }

    [Fact]
    public void Sin_coincidencias_no_asigna_nada()
    {
        var a = ReglasTextosClave.Elegir([Texto("seguro", "6250001")], "otra cosa", "ruta");
        Assert.Equal((null, null, false, false), (a.Cuenta, a.IdCentroCoste, a.EmpateCuenta, a.EmpateCentro));
    }

    [Fact]
    public void Los_avisos_se_anaden_al_log_y_la_factura_pide_revision()
    {
        var f = new FacturaCompra { Ejercicio = new ContextoSociedadEjercicio("T01", 1, 2026), RutaPdf = "x.pdf", Md5 = new string('0', 32),
            Log = """{"version":1,"advertencias":["uno"]}""" };
        f.AnadirAvisos(["dos"]);
        Assert.True(f.RequiereRevision);
        Assert.Equal(["uno", "dos"], ConfianzaCampos.DesdeLog(f.Log).Avisos);
    }
}
