using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.UI.Asientos;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

// Las pruebas contra Access se ejecutan de una en una: una sola copia de ACE por proceso.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ProConta.Contabilidad.UI.Tests;

/// <summary>La pantalla de asiento manual con su fuente real contra una copia de la BD de pruebas.</summary>
[Trait("Categoria", "Access")]
public sealed class AsientoManualAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private sealed class SinDialogos : IDialogosAsiento
    {
        public readonly List<string> Errores = [];
        public (string Codigo, string Nombre)? ElegirCuenta(string? texto, IReadOnlyList<string> grupos, string titulo) => null;
        public decimal? PedirImporte(string titulo, string explicacion) => null;
        public void Error(string titulo, string mensaje) => Errores.Add(mensaje);
    }

    private async Task<Asiento> PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET Cuenta113ParaCuadres = '1130000', Cuenta570Tesoreria = '5700000' WHERE IdSociedad = 'T01'"));
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (c, n) in new[] { ("6220001", "Reparaciones sintéticas"), ("4000001", "Proveedor sintético"), ("5700000", "Caja"), ("5720001", "Banco sintético") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(c, n))).EsCorrecto);
        var a = new Asiento(_ctx, new DateTime(2026, 2, 10), "Factura sintética de reparación");
        a.Apuntes.AddRange([Apunte.AlDebe("6220001", 121m), Apunte.AlHaber("4000001", 121m)]);
        var r = await new MotorAsientosAccess(Bd).GuardarManualAsync(a, _actor);
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 2 WHERE IdSociedadEjercicio = 9001"));
        return r.Valor;
    }

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private AsientoManualVM Vm(SinDialogos? d = null) => new(new FuenteAsientoManualAccess(Bd, _ctx, _actor), d ?? new SinDialogos());

    [Fact]
    public async Task Asistente_de_correccion_pago_por_tesoreria_graba_relacionado_y_punteado()
    {
        var origen = await PrepararAsync();
        var vm = Vm();
        Assert.True((await vm.IniciarCorreccionAsync(origen.Apuntes[1].IdApunte)).EsCorrecto);
        vm.Plantilla = vm.Plantillas.Single(p => p.Texto == "Pago por tesorería");
        await Task.Delay(50);
        Assert.Equal(["4000001", "5700000"], vm.VistaPrevia.Single().Apuntes.Select(a => a.Cuenta));
        vm.Aplicar();
        Assert.Equal("Pasamos pago a caja/tesorería", vm.Motivo);
        Assert.True(vm.PuedeGuardar, vm.AyudaGuardar);

        // Cancelar (no guardar) no escribe nada.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));

        var guardados = new List<int>();
        vm.Guardado += ids => guardados.AddRange(ids);
        Assert.True(await vm.GuardarAsync(), vm.ErrorGuardado);
        var id = Assert.Single(guardados);
        var nuevo = (await new MotorAsientosAccess(Bd).ObtenerAsync(id))!;
        Assert.Equal(new DateTime(2026, 4, 1), nuevo.FechaContable);
        Assert.Equal([("4000001", 121m, 0m), ("5700000", 0m, 121m)], nuevo.Apuntes.Select(a => (a.CodigoCuenta, a.Debe.Valor, a.Haber.Valor)));
        Assert.NotNull(nuevo.IdPlantilla);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM RelacionesAsientos WHERE IdAsientoOrigen = ? AND IdAsientoDestino = ?", origen.IdAsiento, id));
        Assert.True((await new MotorAsientosAccess(Bd).ObtenerAsync(origen.IdAsiento))!.Apuntes[1].Punteado);
    }

    [Fact]
    public async Task Asiento_nuevo_a_mano_y_su_modificacion_con_motivo()
    {
        await PrepararAsync();
        var vm = Vm();
        await vm.IniciarNuevoAsync("5720001", -50m);
        var b = vm.Bloques[0];
        b.Fecha = "05/05/2026";
        b.Descripcion = "Ingreso sintético en caja";
        b.Lineas[1].Cuenta = "5700000";
        vm.CuadrarCon(b.Lineas[1]);
        Assert.True(vm.PuedeGuardar, vm.AyudaGuardar);
        var ids = new List<int>();
        vm.Guardado += x => ids.AddRange(x);
        Assert.True(await vm.GuardarAsync(), vm.ErrorGuardado);
        var id = Assert.Single(ids);

        var mod = Vm();
        Assert.True((await mod.IniciarModificarAsync(id)).EsCorrecto);
        Assert.False(mod.SoloLectura);
        mod.Bloques[0].Lineas[0].Debe = "60";
        mod.CuadrarCon(mod.Bloques[0].Lineas[1]);
        Assert.False(mod.PuedeGuardar); // falta el motivo
        mod.Motivo = "Importe real del ingreso";
        Assert.True(await mod.GuardarAsync(), mod.ErrorGuardado);
        var a = (await new MotorAsientosAccess(Bd).ObtenerAsync(id))!;
        Assert.Equal((60m, 2), (a.TotalDebe.Valor, a.Version));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM RevisionesAsientos WHERE IdAsiento = ? AND Motivo = 'Importe real del ingreso'", id));
    }

    [Fact]
    public async Task Si_el_motor_rechaza_el_asiento_no_se_graba_y_se_explica()
    {
        await PrepararAsync();
        var d = new SinDialogos();
        var vm = Vm(d);
        await vm.IniciarNuevoAsync();
        var b = vm.Bloques[0];
        b.Descripcion = "Con una cuenta bloqueada";
        b.Lineas[0].Cuenta = "5700000";
        b.Lineas[0].Debe = "10";
        b.Lineas[1].Cuenta = "5720001";
        vm.CuadrarCon(b.Lineas[1]);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CuentasContables SET EstaBloqueada = True WHERE Codigo = '5720001'"));
        Assert.False(await vm.GuardarAsync());
        Assert.NotNull(vm.ErrorGuardado);
        Assert.Single(d.Errores);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
        Assert.True(vm.TieneCambios);
    }
}
