using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Legalizacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Legalización de libros contra Access (contabilidad/procesos/legalizacion-libros): sobre una copia de la maestra con un ejercicio sintético,
/// el Diario del libro tiene todos los asientos y cuadra, hay un balance de sumas y saldos por trimestre, el balance inicial es la apertura
/// y el inventario de cierre son los saldos del balance a 31/12 sin el cierre.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class LegalizacionAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _e2025 = new("S01", 9201, 2025);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "S01", "Sociedad sintética de legalización");
        await DatosSinteticos.EjercicioAsync(Bd, 9201, "S01", 2025);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital"), ("5720001", "Banco sintético"), ("4300001", "Cliente sintético"), ("7000001", "Ventas"), ("4770021", "IVA repercutido 21") })
            Assert.True((await cuentas.AltaAsync(_e2025, new AltaCuenta(codigo, nombre))).EsCorrecto);
        var motor = new MotorAsientosAccess(Bd);
        async Task Asiento(DateTime fecha, string texto, TipoAsiento tipo, params Apunte[] apuntes)
        {
            var a = new Asiento(_e2025, fecha, texto) { Tipo = tipo };
            a.Apuntes.AddRange(apuntes);
            var r = await motor.GuardarManualAsync(a, _actor);
            Assert.True(r.EsCorrecto, r.Error?.ToString());
        }
        await Asiento(new(2025, 6, 10), "Factura 2", TipoAsiento.Ordinario, Apunte.AlDebe("4300001", 605m), Apunte.AlHaber("7000001", 500m), Apunte.AlHaber("4770021", 105m));
        await Asiento(new(2025, 1, 1), "Apertura", TipoAsiento.Apertura, Apunte.AlDebe("5720001", 1000m), Apunte.AlHaber("1000000", 1000m));
        await Asiento(new(2025, 2, 15), "Factura 1", TipoAsiento.Ordinario, Apunte.AlDebe("4300001", 1210m), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m));
        await Asiento(new(2025, 3, 31), "Cobro 1", TipoAsiento.Ordinario, Apunte.AlDebe("5720001", 1210m), Apunte.AlHaber("4300001", 1210m));
        await Asiento(new(2025, 12, 31), "Cierre", TipoAsiento.Cierre,
            Apunte.AlHaber("5720001", 2210m), Apunte.AlHaber("4300001", 605m), Apunte.AlDebe("1000000", 1000m), Apunte.AlDebe("7000001", 1500m), Apunte.AlDebe("4770021", 315m));
    }

    [Fact]
    public async Task Reune_el_Diario_los_trimestres_el_balance_inicial_y_el_inventario_de_cierre()
    {
        await PrepararAsync();
        var activos = new List<ActivoInventario> { new("A1", "Piso sintético", "Inmueble", new DateTime(2020, 1, 1), 1000m, 200m) };
        var d = await new ServicioLegalizacion(Bd, _ => Task.FromResult<IReadOnlyList<ActivoInventario>>(activos)).ReunirAsync(_e2025);

        Assert.Equal(5, d.Diario.Asientos.Count);
        Assert.Equal(["Apertura", "Factura 1", "Cobro 1", "Factura 2", "Cierre"], d.Diario.Asientos.Select(a => a.Descripcion));
        Assert.Equal([1, 2, 3, 4, 5], d.Diario.Asientos.Select(a => a.Numero));
        Assert.True(d.Diario.Cuadra);
        Assert.Equal(1000m + 1815m + 1210m + 2815m, d.Diario.TotalDebe);
        Assert.Equal("Banco sintético", d.Diario.Asientos[0].Apuntes.Single(p => p.Cuenta == "5720001").Nombre);

        Assert.Equal(4, d.Trimestres.Count);
        Assert.Equal(new DateTime(2025, 3, 31), d.Trimestres[0].Parametros.Hasta);
        Assert.All(d.Trimestres, t => Assert.Equal(t.TotalDebe, t.TotalHaber));
        Assert.Equal(1000m + 1210m + 1210m, d.Trimestres[0].TotalDebe);

        Assert.Equal(["1000000", "5720001"], d.BalanceInicial.Select(s => s.Cuenta));
        // A 31/12 sin el cierre: banco 2.210 deudor, cliente 605 deudor, capital 1.000 y IVA 315 acreedores; las ventas (grupo 7) no van.
        Assert.Equal(["1000000", "4300001", "4770021", "5720001"], d.InventarioCierre.Select(s => s.Cuenta));
        Assert.Equal(2210m, d.InventarioCierre.Single(s => s.Cuenta == "5720001").Deudor);
        Assert.Equal(315m, d.InventarioCierre.Single(s => s.Cuenta == "4770021").Acreedor);
        Assert.Same(activos, d.Activos);
        Assert.False(d.Cerrado);
        Assert.Contains(d.Comprobaciones, c => c.Gravedad == GravedadLegalizacion.Error && c.Titulo.Contains("no está cerrado"));
    }
}
