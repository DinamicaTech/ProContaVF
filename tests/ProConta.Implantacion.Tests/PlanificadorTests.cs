using ProConta.Contabilidad.Asientos;
using ProConta.Implantacion.ContaSol;
using ProConta.Terceros.Roles;

namespace ProConta.Implantacion.Tests;

/// <summary>Reglas del planificador de ContaSol (sin BD): datos sintéticos, nunca datos reales.</summary>
public sealed class PlanificadorTests
{
    private static ApunteContaSol Ap(int asiento, int orden, string cuenta, decimal importe, bool debe, string? concepto = "Concepto", int mes = 3,
        int dep = 0, int sub = 0, string? tipoIva = null, int codigoIva = 0, int anio = 2024) =>
        new(1, asiento, orden, new DateTime(anio, mes, 1), cuenta, importe, debe, concepto, null, dep, sub, tipoIva, codigoIva);

    private static OrigenContaSol Origen(IReadOnlyList<ApunteContaSol> apuntes, IReadOnlyList<CuentaContaSol>? cuentas = null, IReadOnlyList<RegistroIvaContaSol>? iva = null,
        IReadOnlyList<TerceroContaSol>? terceros = null, IReadOnlyDictionary<string, decimal>? saldos = null, IReadOnlyList<TextoAsientoContaSol>? textos = null,
        IReadOnlyList<DepartamentoContaSol>? departamentos = null, int anio = 2024) =>
        new("sintetico.accdb", "00", new EmpresaContaSol("T01", "B12345674", "Sociedad sintética", anio), departamentos ?? [], [],
            cuentas ?? [new("5720001", "Banco", null, 0, 0), new("7000001", "Ventas", null, 0, 0), new("4300001", "Cliente uno", null, 0, 0), new("4000001", "Proveedor uno", null, 0, 0),
                new("4100001", "Acreedor uno", null, 0, 0), new("6000001", "Compras", null, 0, 0), new("430", "Clientes", null, 0, 0)],
            apuntes, textos ?? [], iva ?? [], terceros ?? [], saldos ?? new Dictionary<string, decimal>());

    [Fact]
    public void NegativoPasaAlLadoContrarioYCeroNoSeImporta()
    {
        var plan = PlanificadorContaSol.Planificar(Origen([Ap(5, 1, "5720001", -100m, debe: false), Ap(5, 2, "7000001", 100m, debe: false), Ap(5, 3, "6000001", 0m, debe: true)]), 2022);
        var asiento = Assert.Single(plan.Asientos);
        Assert.Equal(2, asiento.Apuntes.Count);
        Assert.Equal(100m, asiento.Apuntes[0].Debe);
        Assert.Equal(100m, asiento.Apuntes[1].Haber);
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.ImporteNegativo");
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.ApunteACero" && i.Severidad == Severidad.Informativa);
    }

    [Fact]
    public void AsientoDescuadradoOFueraDeEjercicioNoSeImporta()
    {
        var plan = PlanificadorContaSol.Planificar(Origen([
            Ap(5, 1, "5720001", 100m, true), Ap(5, 2, "7000001", 90m, false),
            Ap(6, 1, "5720001", 10m, true, anio: 2023), Ap(6, 2, "7000001", 10m, false, anio: 2023),
            Ap(7, 1, "5720001", 10m, true), Ap(7, 2, "7000001", 10m, false)]), 2022);
        Assert.Equal("1:7", Assert.Single(plan.Asientos).Clave);
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.AsientoDescuadrado" && i.Severidad == Severidad.Bloqueante);
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.FechaFueraDeEjercicio" && i.Severidad == Severidad.Bloqueante);
    }

    [Fact]
    public void CuentaSinPlanSeCreaYLasDeGrupoNoSonImputables()
    {
        var plan = PlanificadorContaSol.Planificar(Origen([Ap(5, 1, "5720009", 10m, true), Ap(5, 2, "7000001", 10m, false)]), 2022);
        Assert.Contains(plan.Cuentas, c => c.Codigo == "5720009" && c.EsImputable);
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.CuentaNoEnPlan" && i.Clave == "5720009");
        Assert.False(plan.Cuentas.Single(c => c.Codigo == "430").EsImputable);
    }

    [Fact]
    public void NotasDeFOasYTipoDeAsiento()
    {
        var plan = PlanificadorContaSol.Planificar(Origen([
                Ap(1, 1, "5720001", 10m, true, "Asiento de apertura"), Ap(1, 2, "7000001", 10m, false),
                Ap(20000, 1, "5720001", 10m, false, "Asiento de cierre del ejercicio"), Ap(20000, 2, "7000001", 10m, true),
                Ap(9, 1, "5720001", 10m, true, "Regularización IVA 4T"), Ap(9, 2, "7000001", 10m, false)],
            textos: [new TextoAsientoContaSol(1, 9, "Nota sintética")]), 2022);
        Assert.Equal(TipoAsiento.Apertura, plan.Asientos.Single(a => a.Numero == 1).Tipo);
        Assert.Equal(TipoAsiento.Cierre, plan.Asientos.Single(a => a.Numero == 20000).Tipo);
        var nueve = plan.Asientos.Single(a => a.Numero == 9);
        Assert.Equal(TipoAsiento.Ordinario, nueve.Tipo);
        Assert.Equal("Nota sintética", nueve.Notas);
        Assert.Equal(TipoAsiento.Regularizacion, PlanificadorContaSol.TipoDe(500, "REGULARIZACIÓN DE CUENTAS"));
    }

    [Fact]
    public void RegistroDeIvaSeEnlazaConSuAsiento()
    {
        var apuntes = new[]
        {
            Ap(5, 1, "6000001", 100m, true), Ap(5, 2, "4720021", 21m, true, tipoIva: "S", codigoIva: 3), Ap(5, 3, "4000001", 121m, false),
        };
        var iva = new RegistroIvaContaSol(true, 3, new DateTime(2024, 5, 2), new DateTime(2024, 4, 30), null, "4000001", "F-1", "Proveedor uno", "B12345674", 0m,
            [new TramoIvaContaSol(100m, 21m, 21m)], 121m, 0, true, 50m, null);
        var plan = PlanificadorContaSol.Planificar(Origen(apuntes, iva: [iva]), 2022);
        var r = Assert.Single(plan.Registros);
        Assert.Equal("1:5", r.ClaveAsiento);
        Assert.Equal(2, r.Trimestre);
        Assert.Equal("S1", Assert.Single(r.Tramos).Calificacion);
        Assert.Equal(10.5m, r.Deducible);
        Assert.Equal("ES", r.Pais);
    }

    [Fact]
    public void IntracomunitariaSeAutorrepercute()
    {
        var iva = new RegistroIvaContaSol(true, 4, new DateTime(2024, 5, 2), null, null, "4000001", "F-2", "Proveedor UE", "FR12345678901", 0m,
            [new TramoIvaContaSol(100m, 21m, 21m)], 100m, 1, false, 100m, null);
        var r = Assert.Single(PlanificadorContaSol.Planificar(Origen([], iva: [iva]), 2022).Registros);
        Assert.Equal("S2", r.Tramos[0].Calificacion);
        Assert.Equal(21m, r.Autorrepercutida);
        Assert.Equal("FR", r.Pais);
        Assert.Equal("09", r.ClaveRegimen);
        Assert.Null(r.ClaveAsiento);
    }

    [Fact]
    public void SoloPasanATercerosLosConActividadYDeLosUltimosEjercicios()
    {
        var terceros = new[]
        {
            new TerceroContaSol(true, "4300001", "Cliente uno", null, "B12345674", null, null, null, null, null, null, null),
            new TerceroContaSol(false, "4000001", "Proveedor uno", null, "A00000000", null, null, null, null, null, null, null),
            new TerceroContaSol(false, "4100001", "Acreedor uno", null, "A11111111", null, null, null, null, null, null, null),
        };
        var apuntes = new[]
        {
            Ap(1, 1, "4000001", 10m, true, "Asiento de apertura"), Ap(1, 2, "5720001", 10m, false),
            Ap(5, 1, "4300001", 10m, true), Ap(5, 2, "7000001", 10m, false),
            Ap(6, 1, "6000001", 10m, true), Ap(6, 2, "4100001", 10m, false),
        };
        var plan = PlanificadorContaSol.Planificar(Origen(apuntes, terceros: terceros), 2022);
        Assert.Equal(["4300001", "4100001"], plan.Terceros.Select(t => t.Origen.Codigo));
        Assert.Equal(ClaseCuentaRol.Acreedor, plan.Terceros.Single(t => t.Origen.Codigo == "4100001").Clase);
        Assert.Empty(PlanificadorContaSol.Planificar(Origen(apuntes, terceros: terceros), 2025).Terceros);
    }

    [Fact]
    public void CentroDesconocidoSeCrea()
    {
        var plan = PlanificadorContaSol.Planificar(Origen([Ap(5, 1, "5720001", 10m, true, dep: 3, sub: 318), Ap(5, 2, "7000001", 10m, false)],
            departamentos: [new DepartamentoContaSol(3, "Inmuebles", null)]), 2022);
        Assert.Contains(plan.Centros, c => c.Clave == "3/318" && c.ClavePadre == "3");
        Assert.Equal("3/318", plan.Asientos[0].Apuntes[0].ClaveCentro);
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.CentroDesconocido");
    }

    [Fact]
    public void SaldosSeComparanEnLasCuentasDeUltimoNivel()
    {
        var calculado = new Dictionary<string, decimal> { ["5720001"] = 10m, ["7000001"] = -10m };
        var control = new Dictionary<string, decimal> { ["572"] = 10m, ["5720001"] = 10m, ["7000001"] = -9m };
        var d = Assert.Single(PlanificadorContaSol.CompararSaldos(calculado, control));
        Assert.Equal(("7000001", -10m, -9m), (d.Cuenta, d.Calculado, d.Control));
    }

    [Theory]
    [InlineData("B12345674", "ESB12345674", true)]
    [InlineData("1234567", "12345678", true)]
    [InlineData("12345678", "12345678Z", true)]
    [InlineData("", "B12345674", true)]
    [InlineData("B12345674", "A87654321", false)]
    public void MismoTitular(string a, string b, bool esperado) => Assert.Equal(esperado, PlanificadorContaSol.MismoTitular(a, b));

    [Fact]
    public void NifSeNormaliza() => Assert.Equal("B12345674", PlanificadorContaSol.NormalizarNif(" b-12.345.674 "));
}

public sealed class PlanificadorIvaFechasTests
{
    [Fact]
    public void RegistroConFechaDeOtroAnoTomaLaDeSuAsiento()
    {
        var apuntes = new[]
        {
            new ApunteContaSol(1, 5, 1, new DateTime(2026, 1, 15), "6000001", 100m, true, "Compra", null, 0, 0, null, 0),
            new ApunteContaSol(1, 5, 2, new DateTime(2026, 1, 15), "4000001", 100m, false, "Compra", null, 0, 0, "S", 9),
        };
        var iva = new RegistroIvaContaSol(true, 9, new DateTime(2025, 12, 30), new DateTime(2025, 12, 28), null, "4000001", "F-9", "Proveedor", "B12345674", 100m, [], 100m, 0, false, 100m, null);
        var sinAsiento = iva with { Codigo = 10 };
        var origen = new OrigenContaSol("s.accdb", "00", new EmpresaContaSol("T01", "", "S", 2026), [], [],
            [new("6000001", "Compras", null, 0, 0), new("4000001", "Proveedor", null, 0, 0)], apuntes, [], [iva, sinAsiento], [], new Dictionary<string, decimal>());
        var plan = PlanificadorContaSol.Planificar(origen, 2022);
        var r = Assert.Single(plan.Registros);
        Assert.Equal(new DateTime(2026, 1, 15), r.FechaRegistro);
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.IvaFechaCorregida" && i.Clave == "9");
        Assert.Contains(plan.Incidencias, i => i.Codigo == "ContaSol.IvaFueraDeEjercicio" && i.Clave == "10");
    }
}
