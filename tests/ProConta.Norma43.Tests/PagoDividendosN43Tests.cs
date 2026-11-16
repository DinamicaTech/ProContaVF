using System.Windows;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Remesas;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.Norma43.UI.Vistas;

namespace ProConta.Norma43.Tests;

/// <summary>
/// Diálogo «Pago de dividendos» sin BD (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55): reparto del cargo por participación entre los
/// marcados, cuadre al céntimo y humo de la vista con los recursos de la aplicación. Datos inventados.
/// </summary>
public sealed class PagoDividendosN43Tests
{
    private static LineaN43 Cargo(decimal importe) => new(1, 1, 1, "Banco ficticio", "00490000000000000000", "0049", new DateTime(2026, 9, 5), new DateTime(2026, 9, 5), 2026, false,
        "D", importe, "02", "DIVIDENDOS SOCIOS", null, null, null, "5720001", "Banco ficticio", [], null, false, null);

    private static PagoDividendosN43VM Vm(decimal importe, params SocioPagoDividendos[] socios) => new(new PropuestaPagoDividendosN43(Cargo(importe), socios, null));

    private static SocioPagoDividendos Socio(int id, decimal pct, string? error = null) =>
        new(id, $"Socio {id}", pct, error is null ? 100 + id : null, error is null ? $"526000{id}" : null, 0m, error);

    [Fact]
    public void Reparte_el_cargo_por_participacion_y_vuelve_a_repartir_al_desmarcar()
    {
        var vm = Vm(100000m, Socio(1, 50m), Socio(2, 50m));
        Assert.Equal(["50.000,00", "50.000,00"], vm.Filas.Select(f => f.ImporteTexto));
        Assert.True(vm.Cuadra);
        Assert.Equal([new PagoSocio(1, 50000m), new PagoSocio(2, 50000m)], vm.Pagos());

        vm.Filas[1].Marcada = false;
        Assert.Equal(["100.000,00", ""], vm.Filas.Select(f => f.ImporteTexto));
        Assert.True(vm.Cuadra);
        Assert.Contains("526 del socio", vm.Cuadre);

        vm.Filas[1].Marcada = true;
        vm.Filas[0].ImporteTexto = "60.000,00";
        Assert.False(vm.Cuadra);
        vm.Filas[1].ImporteTexto = "40.000";
        Assert.True(vm.Cuadra);
    }

    [Fact]
    public void Un_socio_sin_526_no_se_puede_marcar_y_el_resto_mayor_cuadra_al_centimo()
    {
        var vm = Vm(100m, Socio(1, 33.3333m), Socio(2, 33.3333m), Socio(3, 33.3334m), Socio(4, 10m, "Sin 526"));
        Assert.False(vm.Filas[3].Marcada);
        vm.Filas[3].Marcada = true;
        Assert.False(vm.Filas[3].Marcada);
        Assert.Equal(100m, vm.Pagos().Sum(p => p.Importe));
        Assert.True(vm.Cuadra);
    }

    [Fact]
    public void El_dialogo_se_construye_con_los_recursos_de_la_aplicacion() => EnSta(() =>
    {
        if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ProConta.UI.Base;component/Themes/ProConta.xaml", UriKind.Absolute),
        });
        var d = DialogosLineasN43.CrearPagarDividendos(Vm(100000m, Socio(1, 50m), Socio(2, 50m), Socio(3, 0m, "Sin 526")), null);
        Assert.NotNull(d.Botones);
        d.Measure(new Size(820, 460));
    });

    private static void EnSta(Action accion)
    {
        Exception? error = null;
        var hilo = new Thread(() => { try { accion(); } catch (Exception ex) { error = ex; } });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}
