using ProConta.UI.Base.Tablas;

namespace ProConta.UI.Base.Tests;

public class ColumnasTests
{
    private static readonly DefinicionColumna[] Defs =
    {
        new("fe", "Fecha"), new("as", "Asiento"), new("con", "Concepto", SiempreVisible: true),
        new("debe", "Debe"), new("haber", "Haber"), new("sal", "Saldo"), new("cre", "Grabado el", OcultaDeFabrica: true)
    };

    private static string[] Visibles(ConfiguracionColumnas? c) =>
        DisposicionColumnas.Resolver(Defs, c).Where(x => x.Visible).Select(x => x.Definicion.Clave).ToArray();

    [Fact]
    public void De_fabrica_respeta_orden_y_ocultas() =>
        Assert.Equal(new[] { "fe", "as", "con", "debe", "haber", "sal" }, Visibles(null));

    [Fact]
    public void Mover_junto_a_la_vecina_visible()
    {
        var c = DisposicionColumnas.MoverJuntoAVisible(Defs, null, "debe", -1);
        Assert.NotNull(c);
        Assert.Equal(new[] { "fe", "as", "debe", "con", "haber", "sal" }, Visibles(c));
        Assert.Null(DisposicionColumnas.MoverJuntoAVisible(Defs, null, "fe", -1));
    }

    [Fact]
    public void Volver_al_orden_de_fabrica_no_guarda_orden()
    {
        var c = DisposicionColumnas.MoverJuntoAVisible(Defs, null, "debe", -1)!;
        c = DisposicionColumnas.MoverJuntoAVisible(Defs, c, "debe", 1)!;
        Assert.Null(c.Orden);
        Assert.True(c.EsDeFabrica);
    }

    [Fact]
    public void Mostrar_una_oculta_de_fabrica_y_volver_a_ocultarla()
    {
        var c = DisposicionColumnas.CambiarVisible(Defs, null, "cre", true)!;
        Assert.Contains("cre", Visibles(c));
        c = DisposicionColumnas.CambiarVisible(Defs, c, "cre", false)!;
        Assert.True(c.EsDeFabrica);
    }

    [Fact]
    public void No_deja_ocultar_la_siempre_visible_ni_bajar_del_minimo()
    {
        Assert.Null(DisposicionColumnas.CambiarVisible(Defs, null, "con", false));
        var c = DisposicionColumnas.CambiarVisible(Defs, null, "fe", false)!;
        c = DisposicionColumnas.CambiarVisible(Defs, c, "as", false)!;
        c = DisposicionColumnas.CambiarVisible(Defs, c, "debe", false)!;
        Assert.Equal(3, Visibles(c).Length);
        Assert.Null(DisposicionColumnas.CambiarVisible(Defs, c, "haber", false));
    }

    [Fact]
    public void Columnas_nuevas_se_insertan_tras_su_vecina_de_fabrica()
    {
        var guardada = new ConfiguracionColumnas { Orden = new() { "sal", "fe", "as", "con", "debe", "haber" } };
        var orden = DisposicionColumnas.Resolver(Defs, guardada).Select(x => x.Definicion.Clave).ToArray();
        Assert.Equal(new[] { "sal", "cre", "fe", "as", "con", "debe", "haber" }, orden);
    }

    [Fact]
    public void Claves_desconocidas_del_orden_guardado_se_ignoran()
    {
        var guardada = new ConfiguracionColumnas { Orden = new() { "vieja", "as", "fe" } };
        var orden = DisposicionColumnas.Resolver(Defs, guardada).Select(x => x.Definicion.Clave).ToArray();
        Assert.Equal(Defs.Length, orden.Length);
        Assert.Equal("as", orden[0]);
    }

    [Fact]
    public void Anchos_se_guardan_y_se_borran()
    {
        var c = DisposicionColumnas.CambiarAncho(null, "con", 300.4);
        Assert.Equal(300, c.Anchos!["con"]);
        c = DisposicionColumnas.CambiarAncho(c, "con", null);
        Assert.True(c.EsDeFabrica);
    }
}
