using ProConta.Contabilidad.Centimos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cálculo de los céntimos residuales sin base de datos (contabilidad/procesos/centimos-residuales 2026-11-17 19:05): entran las
/// subcuentas de 7 dígitos de los grupos 1 a 5 (sin 47 ni 57) con un saldo redondeado distinto de cero y menor que el umbral; un asiento
/// por subcuenta que cuadra y la deja a cero, contra la 659 si es deudora y contra la 759 si es acreedora; las excluidas no generan
/// asiento y cambiar las casillas no vuelve a leer nada. Datos inventados.
/// </summary>
public sealed class CentimosTests
{
    private static readonly ConfiguracionCentimos Cfg = ConfiguracionCentimos.Defecto;

    private static readonly SaldoSubcuenta[] Saldos =
    [
        new("4300001", "Cliente uno", 0.01m),
        new("4000001", "Proveedor uno", -0.40m),
        new("4300002", "Cliente dos", 120m),
        new("4100001", "Acreedor uno", -0.999m),     // redondea a −1,00: no es menor de 1 €
        new("4100002", "Acreedor dos", 0.004m),      // redondea a 0,00: no cuenta
        new("4751000", "HP acreedora retenciones", 0.02m), // 47: la salda la liquidación
        new("5720001", "Banco", 0.03m),              // 57: saldo real
        new("6290000", "Otros servicios", 0.05m),    // grupo 6: lo regulariza el cierre
        new("430", "Clientes", 0.01m),               // no es subcuenta
        new("5550001", "Partidas pendientes", 0.50m),
    ];

    private static void CuadraYDejaACero(PropuestaCentimos p)
    {
        foreach (var a in p.Asientos)
        {
            Assert.Equal(2, a.Lineas.Count);
            Assert.Equal(a.Lineas.Sum(l => l.Debe), a.Lineas.Sum(l => l.Haber));
            var fila = p.Filas.Single(f => f.Cuenta == a.Subcuenta);
            var movimiento = a.Lineas.Where(l => l.Cuenta == a.Subcuenta).Sum(l => l.Debe - l.Haber);
            Assert.Equal(0m, fila.Saldo + movimiento);
        }
    }

    [Fact]
    public void Entran_solo_las_subcuentas_de_balance_dentro_del_umbral()
    {
        var p = CalculoCentimos.Calcular(2026, Cfg, Saldos, new HashSet<string>());
        Assert.Equal(["4000001", "4300001", "5550001"], p.Filas.Select(f => f.Cuenta));
        Assert.Equal(3, p.Asientos.Count);
        Assert.Equal(new DateTime(2026, 12, 31), p.Fecha);
        Assert.Equal(0.91m, p.Total);
        Assert.Equal(0.51m, p.TotalGastos);
        Assert.Equal(0.40m, p.TotalIngresos);
        CuadraYDejaACero(p);
    }

    [Fact]
    public void Deudora_contra_la_659_y_acreedora_contra_la_759()
    {
        var p = CalculoCentimos.Calcular(2026, Cfg, Saldos, new HashSet<string>());
        var cliente = p.Asientos.Single(a => a.Subcuenta == "4300001").Lineas;
        Assert.Equal(new LineaCentimos("6590000", 0.01m, 0m, "Céntimos residuales · 4300001 Cliente uno"), cliente[0]);
        Assert.Equal(new LineaCentimos("4300001", 0m, 0.01m, "Céntimos residuales · 4300001 Cliente uno"), cliente[1]);
        var proveedor = p.Asientos.Single(a => a.Subcuenta == "4000001").Lineas;
        Assert.Equal(("4000001", 0.40m, 0m), (proveedor[0].Cuenta, proveedor[0].Debe, proveedor[0].Haber));
        Assert.Equal(("7590000", 0m, 0.40m), (proveedor[1].Cuenta, proveedor[1].Debe, proveedor[1].Haber));
    }

    [Fact]
    public void Umbral_y_cuentas_del_ejercicio()
    {
        var cfg = ConfiguracionCentimos.Con(0.10m, "6690000", "7690000");
        var p = CalculoCentimos.Calcular(2026, cfg, Saldos, new HashSet<string>());
        Assert.Equal(["4300001"], p.Filas.Select(f => f.Cuenta));
        Assert.Equal("6690000", p.Asientos[0].Lineas[0].Cuenta);
        // Vacíos o no válidos: los de siempre.
        Assert.Equal(ConfiguracionCentimos.Defecto, ConfiguracionCentimos.Con(null, " ", null));
        Assert.Equal(1m, ConfiguracionCentimos.Con(0m, null, null).Umbral);
        // Las propias cuentas de redondeo nunca entran.
        Assert.False(CalculoCentimos.EsCandidata("6590000", Cfg));
        Assert.False(CalculoCentimos.EsCandidata("4709000", Cfg));
        Assert.True(CalculoCentimos.EsCandidata("1000000", Cfg));
    }

    [Fact]
    public void Excluir_quita_su_asiento_y_aplicar_no_vuelve_a_leer()
    {
        var p = CalculoCentimos.Calcular(2026, Cfg, Saldos, new HashSet<string> { "5550001" });
        Assert.Equal(3, p.Filas.Count);
        Assert.False(p.Filas.Single(f => f.Cuenta == "5550001").Incluido);
        Assert.Equal(2, p.Asientos.Count);
        Assert.Equal(["5550001"], p.Excluidas);

        var todas = CalculoCentimos.Aplicar(p, new HashSet<string>());
        Assert.Equal(3, todas.Asientos.Count);
        Assert.False(p.Igual(todas));
        Assert.True(todas.Igual(CalculoCentimos.Calcular(2026, Cfg, Saldos, new HashSet<string>())));
        CuadraYDejaACero(todas);
    }

    [Fact]
    public void Sin_saldos_residuales_no_hay_asientos()
    {
        var p = CalculoCentimos.Calcular(2026, Cfg, [new("4300001", "Cliente", 5m), new("4300002", "Cliente", 0m)], new HashSet<string>());
        Assert.Empty(p.Filas);
        Assert.Empty(p.Asientos);
        Assert.Equal(0m, p.Total);
    }
}
