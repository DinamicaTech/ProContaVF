using ProConta.Contabilidad.Cuentas;

namespace ProConta.Contabilidad.Tests;

public class BuscadorCuentasTests
{
    private static CuentaContable C(string codigo, string nombre, bool imputable = true, bool activa = true, bool bloqueada = false) =>
        new(0, 0, 9001, null, codigo, nombre, activa, bloqueada, imputable, null, 0m, 0m, 0);

    private static readonly CuentaContable[] Plan =
    [
        C("600", "Compras de mercaderías", imputable: false), C("6000000", "Compras de mercaderías"), C("6280011", "Suministros: electricidad"),
        C("6290001", "Gastos de viaje"), C("6290002", "Teléfono móvil", activa: false), C("5720017", "Banco Santander"), C("6310133", "Tasas municipales", bloqueada: true),
    ];

    [Theory]
    [InlineData("628.11", 7, "6280011")]
    [InlineData("572.17", 7, "5720017")]
    [InlineData("6.1", 7, "6000001")]
    [InlineData("628", 7, null)]
    [InlineData("62.8.1", 7, null)]
    [InlineData("1234.5678", 7, null)]
    public void El_punto_rellena_con_ceros_hasta_la_longitud(string texto, int longitud, string? esperado) =>
        Assert.Equal(esperado, BuscadorCuentas.ExpandirPunto(texto, longitud));

    [Fact]
    public void Busca_por_codigo_por_punto_o_por_nombre_sin_acentos_y_solo_las_que_admiten_apuntes()
    {
        Assert.Equal(["6000000", "6280011", "6290001"], BuscadorCuentas.Filtrar(Plan, "", ["6"]).Select(c => c.Codigo));
        Assert.Equal(["6280011", "6290001"], BuscadorCuentas.Filtrar(Plan, "62", ["6"]).Select(c => c.Codigo));
        Assert.Equal(["6280011"], BuscadorCuentas.Filtrar(Plan, "628.11", ["6"]).Select(c => c.Codigo));
        Assert.Equal(["6280011"], BuscadorCuentas.Filtrar(Plan, "ELECTRICIDÁD", ["6"]).Select(c => c.Codigo));
        Assert.Empty(BuscadorCuentas.Filtrar(Plan, "santander", ["6"]));
        // Sin filtro de grupo sale la del 57.
        Assert.Equal(["5720017"], BuscadorCuentas.Filtrar(Plan, "santander", null).Select(c => c.Codigo));
    }

    [Fact]
    public void El_siguiente_codigo_es_uno_mas_que_el_mas_alto_y_salta_los_reservados()
    {
        Assert.Equal("6290003", BuscadorCuentas.SiguienteCodigo("629", ["629", "6290001", "6290002"]));
        Assert.Equal("6290004", BuscadorCuentas.SiguienteCodigo("629", ["6290001", "6290002", "6290003"]));
        Assert.Equal("6230001", BuscadorCuentas.SiguienteCodigo("623", ["623"]));
        // Respeta la longitud de las subcuentas que ya tiene la cuenta.
        Assert.Equal("62300000004", BuscadorCuentas.SiguienteCodigo("623", ["62300000003"]));
        // Sin hueco al final, el primero libre.
        Assert.Equal("6290005", BuscadorCuentas.SiguienteCodigo("629", ["6299999", "6290001", "6290002", "6290003", "6290004"]));
        // Lo que cuelga de una cuenta intermedia (6299) es de ella: la 629 no lo cuenta ni lo propone.
        Assert.Equal("6290003", BuscadorCuentas.SiguienteCodigo("629", ["629", "6299", "6290001", "6290002", "6299001", "6299002"]));
        Assert.Equal("6299003", BuscadorCuentas.SiguienteCodigo("6299", ["629", "6299", "6290001", "6299001", "6299002"]));
    }

    [Fact]
    public void Las_subcuentas_tienen_siete_cifras_o_mas()
    {
        Assert.True(BuscadorCuentas.EsSubcuenta("6290001"));
        Assert.False(BuscadorCuentas.EsSubcuenta("629001"));
        var plan = Plan.Append(C("62900", "Cuenta imputable de cinco cifras")).ToList();
        Assert.Contains(BuscadorCuentas.Filtrar(plan, "629", ["6"]), c => c.Codigo == "62900");
        Assert.Equal(["6290001"], BuscadorCuentas.Filtrar(plan, "629", ["6"], soloSubcuentas: true).Select(c => c.Codigo));
    }
}
