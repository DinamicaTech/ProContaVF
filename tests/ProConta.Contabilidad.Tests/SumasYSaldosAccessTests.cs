using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Mayor;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Balance de sumas y saldos contra Access (contabilidad/balances/sumas-y-saldos): sobre una copia de la maestra con un ejercicio sintético,
/// las sumas cuadran, el saldo de cada subcuenta es el del Mayor entre las mismas fechas y el cierre solo entra si se pide.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class SumasYSaldosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _e2025 = new("S01", 9201, 2025);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private static readonly string[] Subcuentas = ["1000000", "5720001", "4300001", "7000001", "4770021"];

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "S01", "Sociedad sintética de sumas y saldos");
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
        await Asiento(new(2025, 1, 1), "Apertura", TipoAsiento.Apertura, Apunte.AlDebe("5720001", 1000m), Apunte.AlHaber("1000000", 1000m));
        await Asiento(new(2025, 2, 15), "Factura 1", TipoAsiento.Ordinario, Apunte.AlDebe("4300001", 1210m), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m));
        await Asiento(new(2025, 3, 31), "Cobro 1", TipoAsiento.Ordinario, Apunte.AlDebe("5720001", 1210m), Apunte.AlHaber("4300001", 1210m));
        await Asiento(new(2025, 6, 10), "Factura 2", TipoAsiento.Ordinario, Apunte.AlDebe("4300001", 605m), Apunte.AlHaber("7000001", 500m), Apunte.AlHaber("4770021", 105m));
        await Asiento(new(2025, 12, 31), "Cierre", TipoAsiento.Cierre,
            Apunte.AlHaber("5720001", 2210m), Apunte.AlHaber("4300001", 605m), Apunte.AlDebe("1000000", 1000m), Apunte.AlDebe("7000001", 1500m), Apunte.AlDebe("4770021", 315m));
    }

    private static ParametrosSumasYSaldos Entre(DateTime desde, DateTime hasta, bool cierre = false) =>
        new(desde, hasta, [NivelSumasYSaldos.Grupo, NivelSumasYSaldos.Subgrupo, NivelSumasYSaldos.Cuenta, NivelSumasYSaldos.Subcuenta], cierre);

    [Fact]
    public async Task Cuadra_y_cada_subcuenta_tiene_el_saldo_del_Mayor_entre_las_mismas_fechas()
    {
        await PrepararAsync();
        var generador = new GeneradorSumasYSaldos(Bd);
        var mayor = await new FuenteMayorAccess(Bd).ApuntesAsync(
            new SolicitudMayor(Subcuentas, Subcuentas.ToHashSet(StringComparer.Ordinal), [new EjercicioMayor(9201, 2025, false)]));
        foreach (var (desde, hasta) in new[] { (new DateTime(2025, 1, 1), new DateTime(2025, 12, 31)), (new DateTime(2025, 2, 1), new DateTime(2025, 3, 31)), (new DateTime(2025, 4, 1), new DateTime(2025, 6, 30)) })
        {
            var r = await generador.GenerarAsync(_e2025, "S", Entre(desde, hasta));
            Assert.Equal(r.TotalDebe, r.TotalHaber);
            Assert.Equal(r.TotalSaldoDeudor, r.TotalSaldoAcreedor);
            foreach (var cuenta in Subcuentas)
            {
                var delMayor = mayor.Apuntes.Where(a => a.Cuenta == cuenta && a.Fecha >= desde && a.Fecha <= hasta).Sum(a => a.Debe - a.Haber);
                var fila = r.Filas.SingleOrDefault(f => f.Cuenta == cuenta && f.Nivel == NivelSumasYSaldos.Subcuenta);
                Assert.Equal(delMayor, fila is null ? 0m : fila.SaldoDeudor - fila.SaldoAcreedor);
            }
            foreach (var f in r.Filas.Where(f => f.Nivel != NivelSumasYSaldos.Subcuenta))
                Assert.Equal(r.Filas.Where(s => s.Nivel == NivelSumasYSaldos.Subcuenta && s.Cuenta.StartsWith(f.Cuenta, StringComparison.Ordinal)).Sum(s => s.Debe - s.Haber), f.Debe - f.Haber);
        }
        var anio = await generador.GenerarAsync(_e2025, "S", Entre(new(2025, 1, 1), new(2025, 12, 31)));
        Assert.Equal(4025m, anio.TotalDebe);
        Assert.Equal("Banco sintético", anio.Filas.Single(f => f.Cuenta == "5720001").Nombre);
    }

    [Fact]
    public async Task El_cierre_solo_entra_si_se_pide_y_deja_los_saldos_a_cero()
    {
        await PrepararAsync();
        var r = await new GeneradorSumasYSaldos(Bd).GenerarAsync(_e2025, "S", Entre(new(2025, 1, 1), new(2025, 12, 31), cierre: true));
        Assert.Equal(4025m + 2815m, r.TotalDebe);
        Assert.Equal(r.TotalDebe, r.TotalHaber);
        Assert.All(r.Filas, f => Assert.Equal(f.Debe, f.Haber));
        Assert.Equal(0m, r.TotalSaldoDeudor);
    }

    [Fact]
    public async Task Unas_fechas_de_otro_ejercicio_dan_un_error_que_dice_que_hacer()
    {
        await PrepararAsync();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new GeneradorSumasYSaldos(Bd).GenerarAsync(_e2025, "S", Entre(new(2024, 1, 1), new(2025, 12, 31))));
        Assert.Contains("cambia antes el ejercicio de trabajo", ex.Message);
    }
}
