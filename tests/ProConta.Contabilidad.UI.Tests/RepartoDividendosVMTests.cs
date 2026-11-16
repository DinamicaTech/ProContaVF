using System.Windows;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Dividendos;
using ProConta.Contabilidad.UI.Dividendos;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.Tests;

/// <summary>
/// Pantalla Contabilidad › Reparto de dividendos (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55): humo de la vista y, contra Access, la
/// ficha de un reparto nuevo con el neto total repartido por participación y su confirmación. Datos sintéticos.
/// </summary>
public sealed class RepartoDividendosVMTests
{
    private sealed class Dialogos : IDialogosRepartoDividendos
    {
        public List<string> Errores { get; } = [];
        public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) => 0;
        public string? PedirMotivo(string titulo, string explicacion) => "Prueba";
        public void Error(string titulo, string mensaje) => Errores.Add($"{titulo}: {mensaje}");
    }

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    [Fact]
    public void La_pantalla_se_construye_con_los_recursos_de_la_aplicacion() => EnSta(() =>
    {
        if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Application.Current!.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/ProConta.UI.Base;component/Themes/ProConta.xaml", UriKind.Absolute),
        });
        var servicio = new ServicioDividendos(null!, null!, null!);
        var vm = new RepartoDividendosVM(new ContextoRepartoDividendos(servicio, new ContextoSociedadEjercicio("T01", 1, 2026), "Demo", new Actor("Prueba"), _ => { }), new Dialogos());
        var vista = (FrameworkElement)vm.Vista;
        vista.Measure(new Size(1200, 700));
        Assert.Equal("Confirmar el reparto", vm.TextoConfirmar);
        Assert.NotNull(vm.PorQueNoConfirmar);
    });

    [Fact]
    [Trait("Categoria", "Access")]
    public async Task Nuevo_reparto_reparte_el_neto_total_por_participacion_y_se_confirma()
    {
        using var copia = BdPruebas.Nueva();
        var bd = copia.Bd();
        var ctx = new ContextoSociedadEjercicio("T01", 9001, 2026);
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(bd, 9001, "T01", 2026);
        var cuentas = new CatalogoCuentasAccess(bd);
        var ids = new Dictionary<string, int>();
        foreach (var codigo in new[] { "1130000", "5260001", "5260002", "4751011", "4751012" })
            ids[codigo] = (await cuentas.AltaAsync(ctx, new AltaCuenta(codigo, $"Cuenta {codigo}"))).Valor.IdIdentidadContable;
        await bd.EjecutarAsync(async s =>
        {
            foreach (var (nombre, c526, c4751) in new[] { ("Socia Uno", "5260001", "4751011"), ("Socio Dos", "5260002", "4751012") })
            {
                await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", nombre, true, 0);
                var t = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO Socios (IdTercero, Activo, FechaAlta) VALUES (?, ?, ?)", t, true, new DateTime(2020, 1, 1));
                var socio = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO SociosSociedad (IdSocio, IdSociedad, Participacion, IdIdentidadDividendos, IdIdentidadRetencion, Activo) VALUES (?, ?, ?, ?, ?, ?)",
                    socio, "T01", 50m, ids[c526], ids[c4751], true);
            }
            return true;
        });
        var servicio = new ServicioDividendos(bd, new MotorAsientosAccess(bd), cuentas, null, new RelojFijo(new DateTime(2026, 12, 15, 10, 0, 0)));
        var avisos = new List<string>();
        var dialogos = new Dialogos();
        var vm = new RepartoDividendosVM(new ContextoRepartoDividendos(servicio, ctx, "Demo", new Actor("Prueba"), avisos.Add), dialogos);
        await vm.CargarAsync();
        Assert.False(vm.FichaAbierta);
        await vm.NuevoAsync();
        Assert.True(vm.FichaAbierta);
        Assert.Equal("15/12/2026", vm.FechaTexto);
        Assert.Equal(2, vm.Socios.Count);

        vm.NetoTotalTexto = "100.000";
        vm.RepartirCommand.Execute(null);
        await vm.ProponerAsync();
        Assert.Equal(["50.000,00", "50.000,00"], vm.Socios.Select(s => s.NetoTexto));
        Assert.Equal(ProConta.UI.Base.Formato.Formatos.Importe(123456.80m), vm.Lineas[0].Debe);
        Assert.Equal(vm.TotalDebe, vm.TotalHaber);
        Assert.Null(vm.PorQueNoConfirmar);

        vm.ConfirmarCommand.Execute(null);
        for (var i = 0; i < 100 && vm.Repartos.Count == 0; i++) await Task.Delay(100);
        Assert.Empty(dialogos.Errores);
        var fila = Assert.Single(vm.Repartos);
        Assert.Equal(ProConta.UI.Base.Formato.Formatos.Importe(123456.80m), fila.Bruto);
        Assert.Contains(avisos, a => a.StartsWith("Reparto de dividendos", StringComparison.Ordinal));
    }

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
