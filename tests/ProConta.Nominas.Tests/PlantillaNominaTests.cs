using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Plantillas;
using ProConta.Nominas.Contabilizacion;
using ProConta.Nucleo.Contexto;

namespace ProConta.Nominas.Tests;

/// <summary>
/// Plantilla «Nómina» sin BD: conversión de las fórmulas de PlantillasApuntes, asientos que propone con una plantilla inventada y
/// contra-asiento de la anulación. Las líneas son inventadas (las de la empresa no están en el repositorio).
/// </summary>
public sealed class PlantillaNominaTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);

    /// <summary>Plantilla inventada con la forma de la de ProConta antiguo: la 465 primero, para completar las cuentas con «?».</summary>
    internal static readonly PlantillaNomina.LineaAntigua[] LineasInventadas =
    [
        new(0, "[Trabajador] & \" \" & [Fecha]", "[CuentaContable]", "H", "[Neto]", null),
        new(1, "\"Sueldo \" & [Trabajador]", "\"6400000\"", "D", "[Bruto]", null),
        new(2, "\"Seguridad Social \" & [Trabajador]", "\"6420000\"", "D", "[SS EMP]", "IsNull([SS EMP])=False"),
        new(3, "\"IRPF \" & [Trabajador]", "\"4751000\"", "H", "[IRPF111]", null),
        new(4, "\"TC1 \" & [Trabajador]", "\"4760000\"", "H", "[TC1]", null),
        new(5, "\"Autónomo \" & [Trabajador]", "\"476000?\"", "H", "ABS([Autonomo])", "[Autonomo] <> 0 OR [Autonomo] < 0"),
        new(6, "\"Especie \" & [Trabajador]", "\"7550000\"", "H", "IIf([DtoReEsp] < 0, -[DtoReEsp], 0)", "IsNull([DtoReEsp])=False"),
    ];

    [Theory]
    [InlineData("[Neto]", "Neto")]
    [InlineData("[SS EMP] + [TC 1]", "SSEMP + TC1")]
    [InlineData("IsNull([Autonomo])=True", "Autonomo = 0")]
    [InlineData("IsNull( Autonomo ) = False", "Autonomo <> 0")]
    [InlineData("ABS([DtoReEsp])", "Abs(DtoReEsp)")]
    [InlineData("[A] > 0 OR [B] > 0 AND NOT [C] = 0", "A > 0 O B > 0 Y No C = 0")]
    [InlineData("IIf([Neto] < 0, \"D\", \"H\")", "Si(Neto < 0; \"D\"; \"H\")")]
    [InlineData("\"OR, AND\" & [Trabajador]", "\"OR, AND\" & Trabajador")]
    public void Convierte_las_formulas_de_Access_a_la_sintaxis_del_motor(string access, string motor) =>
        Assert.Equal(motor, PlantillaNomina.FormulaAcceso(access));

    [Fact]
    public void Una_formula_vacia_queda_vacia() => Assert.Null(PlantillaNomina.FormulaAcceso("  "));

    [Fact]
    public void Convierte_las_lineas_en_un_asiento_y_dice_que_no_ha_podido_convertir()
    {
        var (lineas, errores) = PlantillaNomina.Convertir(LineasInventadas);
        Assert.Empty(errores);
        Assert.Equal(7, lineas.Count);
        Assert.All(lineas, l => Assert.Equal(1, l.NumeroAsiento));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], lineas.Select(l => l.Orden));
        Assert.Equal("CuentaContable", lineas[0].Cuenta);

        var (malas, motivos) = PlantillaNomina.Convertir(
        [
            new(0, null, "[CuentaContable]", "X", "[Neto]", null),
            new(1, null, null, "D", "[Bruto]", null),
            new(2, null, "\"6400000\"", "D", "[Bruto] +", null),
        ]);
        Assert.Empty(malas);
        Assert.Equal(3, motivos.Count);
        Assert.Contains("no es D ni H", motivos[0]);
        Assert.Contains("cuenta: está vacía", motivos[1]);
        Assert.StartsWith("Línea 2, importe:", motivos[2]);
    }

    private static PlantillaAsiento Plantilla() =>
        new(1, PlantillaNomina.Nombre, "", TipoPlantilla.Sistema, OrigenPlantilla.Proceso, [], PlantillaNomina.Convertir(LineasInventadas).Lineas, "T01",
            PlantillaNomina.ClaveProceso, ReglaFecha.FechaOrigen, Clase: ClasePlantilla.Proceso);

    private static NominaContable Nomina(string nombre, string cuenta, params (string Campo, decimal Importe)[] importes) =>
        new("1", nombre, cuenta, new DateTime(2026, 10, 31), importes.ToDictionary(x => x.Campo, x => x.Importe), "000901", null, "MENSUAL");

    [Fact]
    public void Propone_un_asiento_cuadrado_por_nomina_y_completa_la_cuenta_con_interrogante()
    {
        var cuentas = new Dictionary<string, string>
        {
            ["4650001"] = "FICTICIO UNO, ANA", ["6400000"] = "Sueldos", ["6420000"] = "Seguridad Social", ["4751000"] = "HP retenciones",
            ["4760000"] = "Organismos SS", ["7550000"] = "Especie",
        };
        // Ana: bruto 2.300, especie −40, autónomo 1.300 → neto 960.
        var ana = Nomina("FICTICIO UNO, ANA", "4650001", ("Bruto", 2300m), ("DtoReEsp", -40m), ("Autonomo", 1300m), ("Neto", 960m));
        var p = MotorPlantillas.Proponer(Plantilla(), new OrigenProceso(PlantillaNomina.ClaveProceso, [PlantillaNomina.Registro(ana)]), null,
            new EntornoSociedad(Ctx, Cuentas: cuentas), new DateTime(2026, 10, 31));
        Assert.True(p.EsCorrecto, p.Error?.ToString());
        var a = Assert.Single(p.Valor.Asientos);
        Assert.True(a.Cuadrado);
        Assert.Equal(2300m, a.TotalDebe);
        Assert.Equal("FICTICIO UNO, ANA 31/10/2026", a.Descripcion);
        Assert.Equal(["4650001", "6400000", "4760001", "7550000"], a.Apuntes.Select(x => x.Cuenta));
        Assert.Equal(("4760001", "FICTICIO UNO, ANA"), Assert.Single(p.Valor.CuentasNuevas));
        Assert.DoesNotContain(a.Apuntes, x => x.Cuenta == "6420000");
    }

    [Fact]
    public void Un_importe_que_no_viene_vale_cero()
    {
        var r = PlantillaNomina.Registro(Nomina("INVENTADA TRES, EVA", "4650003", ("Bruto", 1450m)));
        Assert.Equal(0m, r.Campos["Neto"]);
        Assert.Equal(1450m, r.Campos["bruto"]);
        Assert.Equal("4650003", r.Campos["CuentaContable"]);
        Assert.Equal("000901", r.Campos["IdTrabajador"]);
    }

    [Fact]
    public void La_anulacion_cambia_el_Debe_y_el_Haber_y_conserva_cuentas_y_conceptos()
    {
        var original = new Asiento(Ctx, new DateTime(2026, 9, 30), "FICTICIO DOS, LUIS 30/09/2026") { IdPlantilla = 7 };
        original.Apuntes.Add(Apunte.AlDebe("6400000", 2400m, "Sueldo"));
        original.Apuntes.Add(Apunte.AlHaber("4650002", 2400m, "Neto"));
        var a = PlantillaNomina.Anulacion(original, new DateTime(2026, 10, 1));
        Assert.Equal(new DateTime(2026, 10, 1), a.FechaContable);
        Assert.Equal("Anulación: FICTICIO DOS, LUIS 30/09/2026", a.Descripcion);
        Assert.Equal(7, a.IdPlantilla);
        Assert.Equal(2400m, a.Apuntes.Single(x => x.CodigoCuenta == "6400000").Haber.Valor);
        Assert.Equal(2400m, a.Apuntes.Single(x => x.CodigoCuenta == "4650002").Debe.Valor);
        Assert.Equal("Sueldo", a.Apuntes.Single(x => x.CodigoCuenta == "6400000").Descripcion);
    }
}
