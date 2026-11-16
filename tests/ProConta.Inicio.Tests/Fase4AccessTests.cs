using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Programadas;
using ProConta.Inicio.Bloques;
using ProConta.Inicio.Salud;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Inicio.Tests;

/// <summary>
/// Lecturas de la fase 4 de Inicio y el histórico de la salud (tabla HistoricoSalud, migración 20261115_1640_HISTORICO_SALUD) sobre
/// una copia de la maestra actualizada al esquema del programa, con datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class Fase4AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _c2025 = new("T01", 9001, 2025);
    private readonly ContextoSociedadEjercicio _c2026 = new("T01", 9002, 2026);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task SociedadAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2026);
    }

    private async Task<int> ActivoAsync(string codigo, bool amortizable = true, bool ajeno = false, DateTime? baja = null)
    {
        var tipo = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT MIN(IdTipoActivo) FROM TiposActivo")));
        return await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, FechaAlta, FechaAdquisicion, FechaBaja, Estado, EsAmortizable, CosteAdquisicionActual, AmortizacionAcumulada, " +
                "DeterioroAcumulado, ValorContable, EstadoCalculo, IdCentroCoste, EsAjeno, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", tipo, codigo, "Activo " + codigo, new DateTime(2025, 1, 1), new DateTime(2025, 1, 1), baja, (byte)1, amortizable, 0m, 0m, 0m, 0m, (byte)0, null, ajeno, 0);
            return await s.UltimoIdAsync();
        });
    }

    [Fact]
    public async Task LeeLosAsientosDeAmortizacionYLosActivosAmortizables()
    {
        await SociedadAsync();
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("6810001", "Amortización inmovilizado material"), ("2810001", "Amortización acumulada"), ("5720001", "Banco"), ("6810002", "Otra amortización") })
        {
            var alta = await cuentas.AltaAsync(_c2025, new AltaCuenta(codigo, nombre));
            Assert.True(alta.EsCorrecto, alta.Error?.ToString());
            Assert.True((await cuentas.AltaEnEjercicioAsync(_c2026, alta.Valor.IdIdentidadContable)).EsCorrecto);
        }
        var motor = new MotorAsientosAccess(Bd);
        async Task Asiento(ContextoSociedadEjercicio ctx, DateTime fecha, params Apunte[] apuntes)
        {
            var a = new Asiento(ctx, fecha, "Sintético");
            a.Apuntes.AddRange(apuntes);
            var r = await motor.GuardarManualAsync(a, _actor);
            Assert.True(r.EsCorrecto, r.Error?.ToString());
        }
        await Asiento(_c2025, new DateTime(2025, 12, 31), Apunte.AlDebe("6810001", 1000m), Apunte.AlDebe("6810002", 200m), Apunte.AlHaber("2810001", 1200m));
        // Un 68 contra el banco no es un asiento de amortización.
        await Asiento(_c2026, new DateTime(2026, 3, 31), Apunte.AlDebe("6810001", 50m), Apunte.AlHaber("5720001", 50m));
        await ActivoAsync("A1");
        await ActivoAsync("A2");
        await ActivoAsync("AJENO", ajeno: true);
        await ActivoAsync("NOAMORT", amortizable: false);
        await ActivoAsync("BAJA", baja: new DateTime(2025, 6, 30));

        var fuente = new FuenteSaludAccess(Bd);
        var ejercicios = await fuente.EjerciciosAsync("T01");
        var amortizaciones = await fuente.AmortizacionesAsync(ejercicios);
        var a = Assert.Single(amortizaciones);
        Assert.Equal(2025, a.Ejercicio);
        Assert.Equal(new DateTime(2025, 12, 31), a.Fecha);
        Assert.Equal(1200m, a.Importe);
        Assert.Equal(2, await fuente.ActivosAmortizablesAsync("T01"));

        // La revisión completa sale sin errores, con la fila de amortizaciones y sin aviso (2026 aún no ha terminado).
        var informe = await new ServicioSalud(fuente, hoy: () => new DateTime(2026, 11, 15)).RevisarAsync("T01", 2026);
        Assert.All(informe.Revisiones, r => Assert.Null(r.Error));
        Assert.Contains(informe.Periodos, f => f.Nombre == "Amortizaciones" && f.Celdas.Count == 12);
        Assert.DoesNotContain(informe.Elementos, e => e.Clave == "amortizaciones");
    }

    [Fact]
    public async Task ElHistoricoGuardaUnaFilaPorDiaYElementoYLaUltimaSustituye()
    {
        await SociedadAsync();
        var historico = new HistoricoSaludAccess(Bd);
        var lunes = new DateTime(2026, 11, 9);
        await historico.GuardarAsync("T01", 2026, lunes, [new FilaHistoricoSalud("compras-pendientes", "compras-pendientes", 6, 100.5m), new FilaHistoricoSalud("bancos-sin-asiento", "bancos-sin-asiento", 40, null)]);
        var hoy = new DateTime(2026, 11, 15);
        await historico.GuardarAsync("T01", 2026, hoy, [new FilaHistoricoSalud("compras-pendientes", "compras-pendientes", 9, 150m), new FilaHistoricoSalud("x", "desaparece", 1, null)]);
        // La última revisión del día sustituye: «desaparece» ya no está.
        await historico.GuardarAsync("T01", 2026, hoy.AddHours(18), [new FilaHistoricoSalud("compras-pendientes", "compras-pendientes", 8, 120m)]);

        var deHoy = await historico.LeerAsync("T01", 2026, hoy);
        var f = Assert.Single(deHoy);
        Assert.Equal(8, f.Elementos);
        Assert.Equal(120m, f.Importe);
        var delLunes = await historico.LeerAsync("T01", 2026, lunes);
        Assert.Equal(2, delLunes.Count);
        Assert.Null(delLunes.Single(x => x.ClaveElemento == "bancos-sin-asiento").Importe);
        Assert.Empty(await historico.LeerAsync("T01", 2025, lunes));
        Assert.Equal(3, Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM HistoricoSalud"))));
        Assert.Equal(2, TendenciaSalud.Variaciones(deHoy, delLunes)["compras-pendientes"]);

        // Con la BD real: la revisión guarda el día y calcula la tendencia sin errores.
        var servicio = new ServicioSalud(new FuenteSaludAccess(Bd), hoy: () => hoy, historico: historico);
        await servicio.RevisarAsync("T01", 2026);
        Assert.Null(servicio.ErrorHistorico);
        Assert.Contains(await historico.LeerAsync("T01", 2026, hoy), x => x.ClaveElemento == "ejercicio-2027");
    }

    [Fact]
    public async Task LeeLasReservasLosAlojamientosYLasProgramadas()
    {
        await SociedadAsync();
        var piso = await ActivoAsync("PISO1");
        var otro = await ActivoAsync("PISO2");
        await ActivoAsync("OFICINA");
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO OrigenesVenta (IdSociedad, Codigo, Nombre, TextosImportacion, EsDirecta, Activo) VALUES ('T01', 'AIRBNB', 'Airbnb', 'Airbnb', False, True)");
            var origen = await s.UltimoIdAsync();
            async Task Venta(int? activo, DateTime entrada, DateTime? salida, decimal noches, bool anulada, string fuente)
            {
                await s.EjecutarAsync(
                    "INSERT INTO VentasPorFacturar (IdSociedad, IdOrigenVenta, IdActivo, FechaReferencia, FechaFin, Cantidad, Importe, Anulada, FacturadaFuera, Fuente, FechaAlta, Version) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)", "T01", origen, activo, entrada, salida, noches, 100m, anulada, fuente == "ERP", fuente, new DateTime(2026, 1, 1));
            }
            await Venta(piso, new DateTime(2026, 3, 1), new DateTime(2026, 3, 4), 3m, false, "AVAIBOOK");
            await Venta(piso, new DateTime(2026, 4, 1), new DateTime(2026, 4, 4), 3m, true, "AVAIBOOK"); // anulada
            await Venta(null, new DateTime(2025, 7, 1), new DateTime(2025, 7, 8), 7m, false, "ERP");
            await s.EjecutarAsync("INSERT INTO AlojamientosAvaibook (IdSociedad, IdExterno, Nombre, IdActivo, Activo) VALUES ('T01', '377814', 'Piso 2', ?, True)", otro);
            return 0;
        });
        var fuente = new FuenteBloquesAccess(Bd);
        var reservas = await fuente.ReservasAsync("T01");
        Assert.Equal(2, reservas.Count);
        Assert.Contains(reservas, r => r.IdActivo is null && r.Salida == new DateTime(2025, 7, 8));
        var alojamientos = await fuente.AlojamientosAsync("T01");
        Assert.Equal(["PISO1", "PISO2"], alojamientos.Select(a => a.Nombre).OrderBy(x => x));
        Assert.All(alojamientos, a => Assert.Equal(new DateTime(2025, 1, 1), a.Alta));
        Assert.Equal([2025, 2026], (await fuente.EjerciciosAsync("T01")).OrderBy(x => x));
        Assert.Empty(await AgendaProgramadas.VencimientosAsync(Bd, "T01", new DateTime(2027, 2, 13)));

        var servicio = new ServicioBloques(fuente, () => new DateTime(2026, 11, 15));
        var p = (await servicio.OcupacionAsync("T01", 2026, 2025))!;
        Assert.Equal(3, p.Actual.Ocupadas[2]);
        Assert.Equal(62, p.Actual.Disponibles[2]);
        Assert.Equal(7, p.Comparado!.Ocupadas[6]);
        Assert.NotEmpty(await servicio.AgendaAsync("T01"));
    }
}
