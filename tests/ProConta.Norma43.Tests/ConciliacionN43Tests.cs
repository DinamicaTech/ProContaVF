using ProConta.Contabilidad.Plantillas;
using ProConta.Norma43.Conciliacion;
using ProConta.Nucleo.Contexto;

namespace ProConta.Norma43.Tests;

/// <summary>Reglas de la conciliación que no necesitan la BD: fecha del asiento, documento y apuntes de la plantilla de sistema.</summary>
public sealed class ConciliacionN43Tests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);

    [Theory]
    [InlineData("2026-03-13", "2026-03-13", "2026-03-13")]
    [InlineData("2026-04-17", "2026-04-10", "2026-04-10")] // como el ERP: la fecha valor
    [InlineData("2026-01-05", "2025-12-29", "2026-01-05")] // valor del año anterior: se queda en el ejercicio del extracto
    public void La_fecha_del_movimiento_es_la_fecha_valor_salvo_que_sea_de_un_ano_anterior(string operacion, string valor, string esperada) =>
        Assert.Equal(DateTime.Parse(esperada), ServicioConciliacionN43.FechaMovimiento(DateTime.Parse(operacion), DateTime.Parse(valor)));

    [Theory]
    [InlineData(null, null)]
    [InlineData("0000000000", null)]
    [InlineData(" 83012 ", "83012")]
    public void El_documento_del_banco_solo_se_copia_si_no_viene_a_ceros(string? documento, string? esperado) =>
        Assert.Equal(esperado, ServicioConciliacionN43.DocumentoBanco(documento));

    private static RegistroProceso Registro(string contrapartida, string nombre, decimal importe, int? centro = null) =>
        new("1", new DateTime(2026, 3, 2), new Dictionary<string, object?>
        {
            ["CuentaBanco"] = "5720017", ["NombreBanco"] = "Banco Ejemplo", ["Contrapartida"] = contrapartida, ["NombreContrapartida"] = nombre,
            ["Importe"] = importe, ["Concepto"] = "Recibo de Telefonía Ejemplo", ["Centro"] = centro,
        });

    private static AsientoPropuesto Proponer(params RegistroProceso[] registros)
    {
        var r = MotorPlantillas.Proponer(PlantillaConciliacionN43.Predeterminada, new OrigenProceso(ServicioConciliacionN43.ClaveProceso, registros), null,
            new EntornoSociedad(Ctx), new DateTime(2026, 3, 2));
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        return Assert.Single(r.Valor.Asientos);
    }

    [Fact]
    public void Un_cargo_va_al_haber_del_banco_y_al_debe_de_la_contrapartida_con_la_descripcion_corta_en_los_dos()
    {
        var a = Proponer(Registro("4100220", "Telefonía Ejemplo", 109.35m, 20));
        Assert.True(a.Cuadrado);
        Assert.Equal([("5720017", 0m, 109.35m, "Recibo de Telefonía Ejemplo", (int?)null), ("4100220", 109.35m, 0m, "Recibo de Telefonía Ejemplo", (int?)20)],
            a.Apuntes.Select(x => (x.Cuenta, x.Debe, x.Haber, x.Concepto, x.IdCentroCoste)));
    }

    [Fact]
    public void Un_abono_va_al_debe_del_banco_y_al_haber_de_la_contrapartida()
    {
        var a = Proponer(Registro("4300003", "Cliente Ejemplo", -1887.60m));
        Assert.Equal([("5720017", 1887.60m, 0m, "Recibo de Telefonía Ejemplo"), ("4300003", 0m, 1887.60m, "Recibo de Telefonía Ejemplo")],
            a.Apuntes.Select(x => (x.Cuenta, x.Debe, x.Haber, x.Concepto)));
    }

    [Fact]
    public void Un_movimiento_repartido_lleva_un_solo_apunte_del_banco_por_el_total()
    {
        var a = Proponer(Registro("6280001", "Suministros", 60m), Registro("6290001", "Otros servicios", 40m), Registro("6280001", "Suministros", 10m));
        Assert.True(a.Cuadrado);
        Assert.Equal([("5720017", 0m, 110m), ("6280001", 70m, 0m), ("6290001", 40m, 0m)], a.Apuntes.Select(x => (x.Cuenta, x.Debe, x.Haber)));
    }
}
