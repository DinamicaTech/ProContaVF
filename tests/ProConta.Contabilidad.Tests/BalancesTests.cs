using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>Modelo sintético reducido, con la estructura del PGC Pymes 2008 (bloque › apartado › título › subtítulo).</summary>
internal static class ModelosSinteticos
{
    public static ModeloBalance Situacion { get; } = new("SIT", "Balance de situación sintético", TipoBalance.Situacion,
    [
        new("21", 1, 2, "ACTIVO", 0, "A. ACTIVO NO CORRIENTE", 101, "II. Inmovilizado material.", 103, null),
        new("430", 1, 3, "ACTIVO", 0, "B. ACTIVO CORRIENTE", 201, "II. Deudores comerciales y otras cuentas a cobrar.", 203, "1. Clientes por ventas y Prestaciones de servicios"),
        new("472", 1, 3, "ACTIVO", 0, "B. ACTIVO CORRIENTE", 201, "II. Deudores comerciales y otras cuentas a cobrar.", 203, "3. Otros deudores"),
        new("572", 1, 3, "ACTIVO", 0, "B. ACTIVO CORRIENTE", 201, "VI. Efectivo y otros activos líquidos equivalentes", 217, null),
        new("100", -1, 3, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 301, "I. Capital"),
        new("129", -1, 3, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 308, "VII. Resultado del ejercicio"),
        new("6", -1, 1, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 308, "VII. Resultado del ejercicio"),
        new("7", -1, 1, "PATRIMONIO NETO Y PASIVO", 10000, "A. PATRIMONIO NETO", 301, "A.1 Fondos propios", 308, "VII. Resultado del ejercicio"),
        new("400", -1, 3, "PATRIMONIO NETO Y PASIVO", 10000, "C. PASIVO CORRIENTE", 501, "IV. Acreedores comerciales y otras cuentas a pagar.", 509, "1. Proveedores"),
        new("477", -1, 3, "PATRIMONIO NETO Y PASIVO", 10000, "C. PASIVO CORRIENTE", 501, "IV. Acreedores comerciales y otras cuentas a pagar.", 509, "2. Otros acreedores"),
    ]);

    public static ModeloBalance PyG { get; } = new("PYG", "Pérdidas y ganancias sintética", TipoBalance.PerdidasYGanancias,
    [
        new("700", -1, 3, "CUENTA DE PÉRDIDAS Y GANANCIAS", 0, "A. RESULTADO DE EXPLOTACIÓN", 701, "1. Importe neto de la cifra de negocios.", 701, null),
        new("600", -1, 3, "CUENTA DE PÉRDIDAS Y GANANCIAS", 0, "A. RESULTADO DE EXPLOTACIÓN", 701, "4. Aprovisionamientos.", 704, null),
        new("62", -1, 2, "CUENTA DE PÉRDIDAS Y GANANCIAS", 0, "A. RESULTADO DE EXPLOTACIÓN", 701, "7. Otros gastos de explotación.", 712, null),
        new("629", -1, 3, "CUENTA DE PÉRDIDAS Y GANANCIAS", 0, "A. RESULTADO DE EXPLOTACIÓN", 701, "7. Otros gastos de explotación.", 712, null),
    ]);

    public static ApunteBalance A(int asiento, string cuenta, decimal debe, decimal haber, byte tipo = 0, int mes = 3, int? centro = null, int orden = 1) =>
        new(new DateTime(2025, mes, 10), asiento, tipo, orden, cuenta, debe, haber, $"Asiento {asiento}", centro);

    /// <summary>Un ejercicio completo y cuadrado: apertura, una compra, una venta, un servicio, la regularización y el cierre.</summary>
    public static IReadOnlyList<ApunteBalance> Ejercicio { get; } =
    [
        A(1, "5720000", 1000m, 0m, tipo: 1, mes: 1), A(1, "1000000", 0m, 1000m, tipo: 1, mes: 1, orden: 2),
        A(2, "6000000", 200m, 0m), A(2, "4720021", 42m, 0m, orden: 2), A(2, "4000001", 0m, 242m, orden: 3),
        A(3, "4300001", 605m, 0m, mes: 5, centro: 7), A(3, "7000000", 0m, 500m, mes: 5, centro: 7, orden: 2), A(3, "4770021", 0m, 105m, mes: 5, orden: 3),
        A(4, "6290000", 50m, 0m, mes: 6), A(4, "5720000", 0m, 50m, mes: 6, orden: 2),
        A(5, "7000000", 500m, 0m, tipo: 2, mes: 12), A(5, "6000000", 0m, 200m, tipo: 2, mes: 12, orden: 2),
        A(5, "6290000", 0m, 50m, tipo: 2, mes: 12, orden: 3), A(5, "1290000", 0m, 250m, tipo: 2, mes: 12, orden: 4),
        A(6, "1290000", 250m, 0m, tipo: 3, mes: 12), A(6, "1000000", 1000m, 0m, tipo: 3, mes: 12, orden: 2), A(6, "4000001", 242m, 0m, tipo: 3, mes: 12, orden: 3),
        A(6, "4770021", 105m, 0m, tipo: 3, mes: 12, orden: 4), A(6, "5720000", 0m, 950m, tipo: 3, mes: 12, orden: 5), A(6, "4300001", 0m, 605m, tipo: 3, mes: 12, orden: 6),
        A(6, "4720021", 0m, 42m, tipo: 3, mes: 12, orden: 7),
    ];

    public static IReadOnlyDictionary<string, string> Nombres { get; } = new Dictionary<string, string>
    {
        ["572"] = "BANCOS", ["5720000"] = "Banco sintético", ["430"] = "CLIENTES", ["4300001"] = "Cliente sintético", ["600"] = "COMPRAS DE MERCADERÍAS",
    };

    public static IReadOnlyDictionary<int, CentroBalance> Centros { get; } = new Dictionary<int, CentroBalance>
    {
        [7] = new("INMUEBLES", "3/304", "Local sintético"),
    };
}

public sealed class ClasificadorBalancesTests
{
    [Fact]
    public void GanaLaReglaDeMasPrioridadYPrefijoMasLargo()
    {
        Assert.Equal("629", ModelosSinteticos.PyG.Regla("6290000")!.Prefijo);
        Assert.Equal("62", ModelosSinteticos.PyG.Regla("6230000")!.Prefijo);
        Assert.Null(ModelosSinteticos.PyG.Regla("6660014"));
        Assert.Equal("129", ModelosSinteticos.Situacion.Regla("1290000")!.Prefijo);
    }

    [Fact]
    public void PerdidasYGananciasSinRegularizacionNiCierreYConSignoHaberMenosDebe()
    {
        var r = Clasificador.Clasificar(ModelosSinteticos.PyG, "Sociedad sintética", 2025, ModelosSinteticos.Ejercicio, ModelosSinteticos.Nombres, ModelosSinteticos.Centros);

        Assert.All(r.Filas, f => Assert.True(f.CuentaContable[0] is '6' or '7'));
        Assert.DoesNotContain(r.Filas, f => f.IdAsiento is 5 or 6);
        Assert.Equal(500m, r.Filas.Single(f => f.CuentaContable == "7000000").Importe);
        Assert.Equal(-200m, r.Filas.Single(f => f.CuentaContable == "6000000").Importe);
        // Resultado = lo que la regularización lleva a la 129 (Haber 250).
        Assert.Equal(250m, r.Total);
        Assert.Empty(r.SinClasificar);
        // Orden oficial: la cifra de negocios (701) antes que los aprovisionamientos (704) y los otros gastos (712).
        Assert.Equal(["1. Importe neto de la cifra de negocios.", "4. Aprovisionamientos.", "7. Otros gastos de explotación."], r.Filas.Select(f => f.Titulo).Distinct());
        var venta = r.Filas.Single(f => f.CuentaContable == "7000000");
        Assert.Equal(("INMUEBLES", "3/304", "Local sintético"), (venta.CentroCoste, venta.SubDepartamento, venta.NombreDepartamento));
        Assert.Equal("600 COMPRAS DE MERCADERÍAS", r.Filas.Single(f => f.CuentaContable == "6000000").Cuenta);
        Assert.Equal("6000000", r.Filas.Single(f => f.CuentaContable == "6000000").Subcuenta);
    }

    [Fact]
    public void SituacionSinCierreCuadraActivoConPatrimonioNetoYPasivo()
    {
        var r = Clasificador.Clasificar(ModelosSinteticos.Situacion, "Sociedad sintética", 2025, ModelosSinteticos.Ejercicio, ModelosSinteticos.Nombres, ModelosSinteticos.Centros);

        Assert.DoesNotContain(r.Filas, f => f.IdAsiento == 6);
        Assert.Contains(r.Filas, f => f.IdAsiento == 1);
        var totales = r.TotalesPorBloque;
        Assert.Equal(["ACTIVO", "PATRIMONIO NETO Y PASIVO"], totales.Select(t => t.Bloque));
        // Activo: banco 950 + cliente 605 + IVA soportado 42.
        Assert.Equal(1597m, totales[0].Importe);
        Assert.Equal(totales[0].Importe, totales[1].Importe);
        // El resultado del ejercicio (VII) suma 250 tanto si se mira antes como después de la regularización.
        Assert.Equal(250m, r.Filas.Where(f => f.Subtitulo == "VII. Resultado del ejercicio").Sum(f => f.Importe));
        Assert.Equal("572 BANCOS", r.Filas.First(f => f.CuentaContable == "5720000").Cuenta);
        Assert.Equal("5720000 Banco sintético", r.Filas.First(f => f.CuentaContable == "5720000").Subcuenta);
        Assert.Equal("ACTIVO", r.Filas[0].Bloque);
    }

    [Fact]
    public void LasCuentasSinReglaSeListanAparteConSuSaldo()
    {
        ApunteBalance[] apuntes =
        [
            ModelosSinteticos.A(1, "6660014", 668.05m, 0m), ModelosSinteticos.A(1, "5720000", 0m, 668.05m, orden: 2),
            ModelosSinteticos.A(2, "6660014", 10m, 0m), ModelosSinteticos.A(2, "5720000", 0m, 10m, orden: 2),
        ];
        var r = Clasificador.Clasificar(ModelosSinteticos.PyG, "S", 2025, apuntes, ModelosSinteticos.Nombres, ModelosSinteticos.Centros);

        Assert.Empty(r.Filas);
        var sin = Assert.Single(r.SinClasificar);
        Assert.Equal(("6660014", 2, 678.05m), (sin.Cuenta, sin.Apuntes, sin.Saldo));
    }

    [Fact]
    public void LaRutaDelNodoDaBloqueApartadoTituloYNumeroOficialEnPyG()
    {
        var nodos = new Dictionary<int, GeneradorBalances.NodoLeido>
        {
            [1] = new(1, null, "PYG", "CUENTA DE PÉRDIDAS Y GANANCIAS", 0, 0),
            [62] = new(62, 1, "PYG.A", "A. RESULTADO DE EXPLOTACIÓN", 0, 1),
            [66] = new(66, 62, "PYG.A.4.062", "Aprovisionamientos.", 704, 2),
            [63] = new(63, 62, "PYG.A.1.059", "Importe neto de la cifra de negocios.", 701, 2),
            [77] = new(77, 62, "PYG.B.13.073", "Ingresos financieros.", 801, 2),
            [79] = new(79, 77, "PYG.B.13.b.075", "b. Otros Ingresos financieros.", 802, 3),
        };
        var ruta = GeneradorBalances.Ruta(nodos, 66, TipoBalance.PerdidasYGanancias);
        Assert.Equal(("CUENTA DE PÉRDIDAS Y GANANCIAS", "A. RESULTADO DE EXPLOTACIÓN", 701, "4. Aprovisionamientos.", 704, (string?)null),
            (ruta.Bloque, ruta.Apartado, ruta.OrdenApartado, ruta.Titulo, ruta.OrdenTitulo, ruta.Subtitulo));
        var sub = GeneradorBalances.Ruta(nodos, 79, TipoBalance.PerdidasYGanancias);
        Assert.Equal(("13. Ingresos financieros.", "b. Otros Ingresos financieros."), (sub.Titulo, sub.Subtitulo));
        // En situación los títulos ya llevan su numeración romana: no se toca el nombre.
        Assert.Equal("Aprovisionamientos.", GeneradorBalances.Ruta(nodos, 66, TipoBalance.Situacion).Titulo);
    }
}

/// <summary>Balances sobre una copia de la maestra: el modelo PGC Pymes 2008 cargado y los apuntes de un ejercicio sintético.</summary>
[Trait("Categoria", "Access")]
public sealed class GeneradorBalancesAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2025);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[]
                 {
                     ("1000000", "Capital sintético"), ("5720001", "Banco sintético"), ("7000001", "Ventas sintéticas"), ("4300001", "Cliente sintético"),
                     ("6290001", "Otros servicios"), ("4770021", "IVA repercutido 21"), ("6660014", "Pérdidas en participaciones sintéticas"),
                 })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta("572", "Bancos", EsImputable: false))).EsCorrecto);
        var motor = new MotorAsientosAccess(Bd);
        async Task Asiento(int mes, string texto, params Apunte[] apuntes)
        {
            var a = new Asiento(_ctx, new DateTime(2025, mes, 15), texto);
            a.Apuntes.AddRange(apuntes);
            var r = await motor.GuardarManualAsync(a, _actor);
            Assert.True(r.EsCorrecto, r.Error?.ToString());
        }
        await Asiento(1, "Aportación", Apunte.AlDebe("5720001", 1000m), Apunte.AlHaber("1000000", 1000m));
        await Asiento(4, "Venta", Apunte.AlDebe("4300001", 1210m), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m));
        await Asiento(6, "Servicio", Apunte.AlDebe("6290001", 100m), Apunte.AlHaber("5720001", 100m));
        await Asiento(7, "Pérdida en participaciones", Apunte.AlDebe("6660014", 30m), Apunte.AlHaber("5720001", 30m));
    }

    [Fact]
    public async Task GeneraSituacionYPerdidasYGananciasConElModeloPgcPymes2008()
    {
        await PrepararAsync();
        var generador = new GeneradorBalances(Bd);

        var pyg = await generador.GenerarAsync(TipoBalance.PerdidasYGanancias, _ctx, "Sociedad sintética");
        Assert.Equal("Pérdidas y ganancias PGC Pymes 2008", pyg.Modelo.Nombre);
        Assert.Equal(900m, pyg.Total);
        Assert.Equal("1. Importe neto de la cifra de negocios.", pyg.Filas.Single(f => f.CuentaContable == "7000001").Titulo);
        Assert.Equal("7000001 Ventas sintéticas", pyg.Filas.Single(f => f.CuentaContable == "7000001").Subcuenta);
        // La 666 no está en el modelo de pérdidas y ganancias de la maestra: se avisa, no se pierde en silencio.
        Assert.Equal(("6660014", 30m), (pyg.SinClasificar.Single().Cuenta, pyg.SinClasificar.Single().Saldo));

        var situacion = await generador.GenerarAsync(TipoBalance.Situacion, _ctx, "Sociedad sintética");
        var totales = situacion.TotalesPorBloque;
        Assert.Equal(["ACTIVO", "PATRIMONIO NETO Y PASIVO"], totales.Select(t => t.Bloque));
        Assert.Equal(2080m, totales[0].Importe);
        // Debe = Haber en todo el ejercicio: lo que no cuadra entre los dos bloques es exactamente el saldo sin clasificar (la 666).
        Assert.Equal(30m, situacion.SinClasificar.Single(s => s.Cuenta == "6660014").Saldo);
        Assert.Equal(totales[0].Importe + situacion.SinClasificar.Sum(s => s.Saldo), totales[1].Importe);
        Assert.Equal("572 Bancos", situacion.Filas.First(f => f.CuentaContable == "5720001").Cuenta);
    }
}
