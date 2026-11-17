using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cierre;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Cálculo del cierre del ejercicio (contabilidad/procesos/cierre-ejercicio), sin BD: regularización de los grupos 6 y 7 a la cuenta de
/// resultados, cierre de los grupos 1 a 5 con ella y apertura como el cierre al revés; bloqueos por saldos en 8 y 9 y por descuadre.
/// Ejemplo sintético: ingresos 10.000 (7050000), gastos 6.000 (6210000) + 1.000 (6300000), banco 8.000, capital −3.000, proveedor −1.000,
/// 1290000 de un ejercicio anterior sin aplicar −1.000 (beneficio de 3.000).
/// </summary>
public sealed class CierreEjercicioTests
{
    private static SaldoCuentaCierre S(string cuenta, decimal saldo, string nombre = "") => new(cuenta, nombre, saldo);

    private static readonly IReadOnlyList<SaldoCuentaCierre> Ejemplo =
    [
        S("7050000", -10_000m, "Prestaciones de servicios"),
        S("6210000", 6_000m, "Arrendamientos"),
        S("6300000", 1_000m, "Impuesto corriente"),
        S("5720001", 8_000m, "Banco"),
        S("1000000", -3_000m, "Capital"),
        S("4100001", -1_000m, "Proveedor"),
        S("1290000", -1_000m, "Resultado del ejercicio"),
    ];

    [Fact]
    public void Regulariza_los_grupos_6_y_7_contra_la_129()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, null, null, Ejemplo);

        Assert.True(p.SePuedeCerrar, string.Join(" ", p.Bloqueos));
        Assert.Equal(10_000m, p.Ingresos);
        Assert.Equal(7_000m, p.Gastos);
        Assert.Equal(3_000m, p.Resultado);
        var r = p.Regularizacion!;
        Assert.Equal(TipoAsiento.Regularizacion, r.Tipo);
        Assert.Equal(new DateTime(2026, 12, 31), r.Fecha);
        Assert.True(r.Cuadra);
        Assert.Equal(["6210000", "6300000", "7050000", "1290000"], r.Lineas.Select(l => l.Cuenta));
        Assert.Equal(new LineaCierre("6210000", "Arrendamientos", 0m, 6_000m), r.Lineas[0]);
        Assert.Equal(new LineaCierre("7050000", "Prestaciones de servicios", 10_000m, 0m), r.Lineas[2]);
        Assert.Equal(new LineaCierre("1290000", "Resultado del ejercicio", 0m, 3_000m), r.Lineas[3]);
    }

    [Fact]
    public void Cierra_el_balance_con_la_129_ya_regularizada_y_la_apertura_es_el_reverso()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, null, null, Ejemplo);

        var c = p.Cierre!;
        Assert.Equal(TipoAsiento.Cierre, c.Tipo);
        Assert.True(c.Cuadra);
        Assert.Equal(["1000000", "1290000", "4100001", "5720001"], c.Lineas.Select(l => l.Cuenta));
        // 1290000: −1.000 anterior − 3.000 del ejercicio = −4.000, se salda al Debe.
        Assert.Equal(new LineaCierre("1290000", "Resultado del ejercicio", 4_000m, 0m), c.Lineas[1]);
        Assert.Equal(new LineaCierre("5720001", "Banco", 0m, 8_000m), c.Lineas[3]);
        Assert.Equal(8_000m, c.TotalDebe);

        var a = p.Apertura!;
        Assert.Equal(TipoAsiento.Apertura, a.Tipo);
        Assert.Equal(new DateTime(2027, 1, 1), a.Fecha);
        Assert.Equal("Asiento de apertura 2027", a.Descripcion);
        Assert.True(CalculoCierreEjercicio.EsReverso(c, a));
        Assert.True(p.Cuadra);
    }

    [Fact]
    public void Una_perdida_va_al_Debe_de_la_cuenta_de_resultados_configurada()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, " 1290001 ", "Pérdidas y ganancias", [S("6210000", 500m), S("5720001", -500m)]);

        Assert.Equal(-500m, p.Resultado);
        Assert.Equal("1290001", p.CuentaResultado);
        Assert.Equal(new LineaCierre("1290001", "Pérdidas y ganancias", 500m, 0m), p.Regularizacion!.Lineas[^1]);
        Assert.Contains(p.Cierre!.Lineas, l => l is { Cuenta: "1290001", Debe: 0m, Haber: 500m });
        Assert.True(p.Cuadra);
    }

    [Fact]
    public void Agrupa_por_subcuenta_redondea_y_quita_las_que_quedan_a_cero()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, null, null,
            [S("6210000", 100.004m), S("6210000 ", 0.001m, "Arrendamientos"), S("4300001", 50m), S("4300001", -50m), S("5720001", -100.01m)]);

        Assert.True(p.SePuedeCerrar, string.Join(" ", p.Bloqueos));
        Assert.Equal(new LineaCierre("6210000", "Arrendamientos", 0m, 100.01m), p.Regularizacion!.Lineas[0]);
        Assert.DoesNotContain(p.Cierre!.Lineas, l => l.Cuenta == "4300001");
    }

    [Fact]
    public void Saldos_en_los_grupos_8_y_9_impiden_cerrar()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, null, null, [.. Ejemplo, S("8000000", 10m), S("5720001", -10m)]);

        Assert.False(p.SePuedeCerrar);
        Assert.Contains(p.Bloqueos, b => b.Contains("8000000"));
    }

    [Fact]
    public void Un_descuadre_impide_cerrar()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, null, null, [.. Ejemplo, S("5720001", 0.02m)]);

        Assert.False(p.SePuedeCerrar);
        Assert.Contains(p.Bloqueos, b => b.Contains("no suman cero"));
    }

    [Fact]
    public void Sin_saldos_no_hay_nada_que_cerrar()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, null, null, []);

        Assert.Null(p.Regularizacion);
        Assert.Null(p.Cierre);
        Assert.Null(p.Apertura);
        Assert.False(p.SePuedeCerrar);
    }

    [Fact]
    public void Igual_detecta_cambios_en_la_propuesta()
    {
        var a = CalculoCierreEjercicio.Calcular(2026, null, null, Ejemplo);
        var b = CalculoCierreEjercicio.Calcular(2026, null, null, Ejemplo);
        var c = CalculoCierreEjercicio.Calcular(2026, null, null, [.. Ejemplo, S("6210000", 1m), S("5720001", -1m)]);

        Assert.True(a.Igual(b));
        Assert.False(a.Igual(c));
    }

    [Fact]
    public void El_reverso_exige_las_mismas_cuentas_e_importes_cambiados_de_lado()
    {
        var p = CalculoCierreEjercicio.Calcular(2026, null, null, Ejemplo);
        var mal = p.Apertura! with { Lineas = [.. p.Apertura!.Lineas.Take(p.Apertura.Lineas.Count - 1), p.Apertura.Lineas[^1] with { Cuenta = "5720002" }] };

        Assert.False(CalculoCierreEjercicio.EsReverso(p.Cierre, mal));
        Assert.True(CalculoCierreEjercicio.EsReverso(null, null));
        Assert.False(CalculoCierreEjercicio.EsReverso(p.Cierre, null));
    }

    [Theory]
    [InlineData("Ejercicio 2026", 2026, "Ejercicio 2027")]
    [InlineData("2026\\Proveedores", 2026, "2027\\Proveedores")]
    [InlineData("Sin año", 2026, "Sin año")]
    public void El_nuevo_ejercicio_cambia_el_año_de_los_textos(string texto, int ejercicio, string esperado) =>
        Assert.Equal(esperado, ServicioNuevoEjercicio.NuevoTexto(texto, ejercicio));
}
