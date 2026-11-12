using ProConta.Activos.Subcuentas;

namespace ProConta.Activos.Tests;

/// <summary>Nombre por plantilla, parecido y planificación de «Generar subcuentas» (sin BD).</summary>
public sealed class ReglasSubcuentasTests
{
    [Theory]
    [InlineData("Amort. acumulada [TOKEN]", "Calle Mayor 3", "Amort. acumulada Calle Mayor 3")]
    [InlineData("[INMUEBLE] - seguro", "Local 2", "Local 2 - seguro")]
    [InlineData("Seguro  [TOKEN]  hogar", "Piso A", "Seguro Piso A hogar")]
    [InlineData("Sin token", "Piso A", "Sin token")]
    public void El_nombre_sustituye_el_token_por_el_nombre_del_activo(string plantilla, string activo, string esperado) =>
        Assert.Equal(esperado, ReglasSubcuentaActivo.Nombre(plantilla, activo));

    [Fact]
    public void El_nombre_se_recorta_a_la_longitud_de_una_cuenta() =>
        Assert.Equal(ReglasSubcuentaActivo.LongitudMaximaNombre, ReglasSubcuentaActivo.Nombre("[TOKEN]", new string('x', 400)).Length);

    [Fact]
    public void El_parecido_no_distingue_mayusculas_ni_acentos()
    {
        Assert.Equal(1.0, ReglasSubcuentaActivo.Parecido("Amortización Piso", "AMORTIZACION piso"));
        Assert.False(ReglasSubcuentaActivo.MuyDistinto("Amort. acumulada Piso 3", "Amort. acumulada Piso 3B"));
        Assert.True(ReglasSubcuentaActivo.MuyDistinto("Amort. acumulada Piso 3", "Seguro del local"));
    }

    private static readonly ActivoParaSubcuentas Piso = new(1, 10, "Piso A", 7);
    private static readonly ActivoParaSubcuentas Ajeno = new(2, 10, "Piso particular", null, EsAjeno: true);
    private static readonly ActivoParaSubcuentas Coche = new(3, 20, "Coche", 8);
    private static readonly LineaParaSubcuentas Valor = new(100, 10, "2210", 1, "Construcción [TOKEN]", "Valor de la construcción");
    private static readonly LineaParaSubcuentas Amort = new(101, 10, "2820", 2, "Amort. acumulada [TOKEN]", null);
    private static readonly LineaParaSubcuentas Quitada = new(102, 10, "6250", 3, "Seguro [TOKEN]", null, EstaActiva: false);

    [Fact]
    public void Crea_las_que_faltan_en_orden_y_no_toca_ajenos_ni_lineas_inactivas_ni_otros_tipos()
    {
        var ops = GeneradorSubcuentasActivo.Planificar([Piso, Ajeno, Coche], [Valor, Amort, Quitada], []);
        Assert.Equal([(AccionSubcuenta.Crear, 1, 100, "Construcción Piso A"), (AccionSubcuenta.Crear, 1, 101, "Amort. acumulada Piso A")],
            ops.Select(o => (o.Accion, o.Activo.IdActivo, o.Linea.IdPlantilla, o.NombreNuevo)));
    }

    [Fact]
    public void Con_todo_generado_y_el_mismo_nombre_no_hay_nada_que_hacer()
    {
        var actuales = new[]
        {
            new SubcuentaActual(1, 100, 500, 900, "2210001", "Construcción Piso A"),
            new SubcuentaActual(1, 101, 501, 901, "2820001", "Amort.  acumulada Piso A "),
        };
        Assert.Empty(GeneradorSubcuentasActivo.Planificar([Piso, Ajeno], [Valor, Amort], actuales));
    }

    [Fact]
    public void Renombra_si_cambia_el_nombre_marca_las_muy_distintas_y_da_de_alta_las_de_otro_ejercicio()
    {
        var actuales = new[]
        {
            new SubcuentaActual(1, 100, 500, 900, "2210001", "Construccion Piso A"),
            new SubcuentaActual(1, 101, 501, null, "2820001", null),
        };
        var ops = GeneradorSubcuentasActivo.Planificar([Piso], [Valor, Amort], actuales);
        Assert.Equal([AccionSubcuenta.Renombrar, AccionSubcuenta.AltaEnEjercicio], ops.Select(o => o.Accion));
        Assert.False(ops[0].MuyDistinto);

        var distinta = GeneradorSubcuentasActivo.Planificar([Piso], [Valor], [new SubcuentaActual(1, 100, 500, 900, "2210001", "Seguro del local")]);
        Assert.True(distinta.Single().MuyDistinto);
        Assert.StartsWith("⚠", distinta.Single().Texto);
    }
}
