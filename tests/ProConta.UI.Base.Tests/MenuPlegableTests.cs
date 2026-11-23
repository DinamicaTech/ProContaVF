using ProConta.UI.Base.Navegacion;

namespace ProConta.UI.Base.Tests;

public class MenuPlegableTests
{
    private sealed class Opcion(string titulo, int nivel) : OpcionPlegable
    {
        public string Titulo { get; } = titulo;
        public override int Nivel { get; } = nivel;
        public override string Ayuda => Titulo;
    }

    // Menú como el de la ventana principal: Ventas con 3 pantallas, Contabilidad con 5 (una es el submenú
    // «Fin de ejercicio» con 4 opciones) e Informes con 2.
    private static List<Opcion> Menu() =>
    [
        new("Inicio", 0),
        new("Ventas", 0), new("Programadas", 1), new("Facturas", 1), new("Por facturar", 1),
        new("Contabilidad", 0), new("Mayor", 1), new("Diario", 1), new("Fin de ejercicio", 1),
        new("Amortización", 2), new("Pendientes", 2), new("Prorrata", 2), new("Cierre", 2),
        new("Recalcular", 1), new("Asiento manual", 1),
        new("Informes", 0), new("Balance", 1), new("PyG", 1),
    ];

    private static Opcion O(List<Opcion> m, string t) => m.Single(o => o.Titulo == t);

    private static string[] Visibles(List<Opcion> m) => m.Where(o => o.Visible).Select(o => o.Titulo).ToArray();

    [Fact]
    public void Solo_los_grupos_de_mas_de_tres_opciones_son_plegables()
    {
        var m = Menu();
        MenuPlegable.Organizar(m);
        Assert.Equal(["Contabilidad", "Fin de ejercicio"], m.Where(o => o.EsPlegable).Select(o => o.Titulo));
    }

    [Fact]
    public void Al_entrar_los_grupos_plegables_estan_plegados()
    {
        var m = Menu();
        MenuPlegable.Organizar(m);
        Assert.Equal(["Inicio", "Ventas", "Programadas", "Facturas", "Por facturar", "Contabilidad", "Informes", "Balance", "PyG"], Visibles(m));
        Assert.Equal("▸", O(m, "Contabilidad").Flecha);
        Assert.Equal("", O(m, "Ventas").Flecha);
    }

    [Fact]
    public void Desplegar_un_grupo_muestra_sus_opciones_y_deja_plegado_el_submenu()
    {
        var m = Menu();
        MenuPlegable.Organizar(m);
        MenuPlegable.Alternar(m, O(m, "Contabilidad"));
        Assert.Equal("▾", O(m, "Contabilidad").Flecha);
        Assert.Contains("Mayor", Visibles(m));
        Assert.Contains("Asiento manual", Visibles(m));
        Assert.DoesNotContain("Amortización", Visibles(m));

        MenuPlegable.Alternar(m, O(m, "Fin de ejercicio"));
        Assert.Contains("Cierre", Visibles(m));
    }

    [Fact]
    public void Plegar_el_area_oculta_tambien_el_submenu_y_al_volver_lo_recuerda()
    {
        var m = Menu();
        MenuPlegable.Organizar(m);
        MenuPlegable.Alternar(m, O(m, "Contabilidad"));
        MenuPlegable.Alternar(m, O(m, "Fin de ejercicio"));
        MenuPlegable.Alternar(m, O(m, "Contabilidad"));
        Assert.DoesNotContain("Mayor", Visibles(m));
        Assert.DoesNotContain("Cierre", Visibles(m));

        MenuPlegable.Alternar(m, O(m, "Contabilidad"));
        Assert.Contains("Cierre", Visibles(m));
    }

    [Fact]
    public void Un_grupo_no_plegable_no_cambia_al_alternarlo()
    {
        var m = Menu();
        MenuPlegable.Organizar(m);
        MenuPlegable.Alternar(m, O(m, "Ventas"));
        Assert.False(O(m, "Ventas").Expandida);
        Assert.Contains("Programadas", Visibles(m));
    }
}
