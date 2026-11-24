using System.Globalization;
using ProConta.Activos;
using ProConta.Activos.Financieros;
using ProConta.Activos.Gestion;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>
/// «Compra de activo…» y «Venta de activo…» de Líneas N43 (activos/activos-financieros 2026-11-24 09:14) contra una copia de pruebas con datos inventados:
/// la compra va a la 54001 del activo con el importe del banco; la venta lleva el coste medio a la 54001 y el beneficio a la 76600; la operación queda
/// conciliada y «Deshacer el asiento…» la deja sin conciliar con su importe anterior y quita las cuentas del activo de la línea.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ActivosN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioActivos Activos => new(Bd, new CatalogoCuentasAccess(Bd));
    private ServicioConciliacionN43 Conciliacion => new(Bd);
    private ServicioActivosN43 Servicio => new(Bd, Conciliacion);
    private async Task<LineaN43> LineaAsync(int id) => Assert.Single(await new ConsultaLineasN43(Bd).LineasAsync("T01", null, [id]));
    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)), CultureInfo.InvariantCulture);

    /// <summary>
    /// Sociedad T01 con su banco, las cuentas de la plantilla de inversiones y un ETF con sus subcuentas: compra de 10 participaciones (1.000 €) y venta
    /// estimada de 4 (520 €). El extracto trae el cargo de la compra (1.002,50 €, con la comisión) y el abono de la venta (530 €).
    /// </summary>
    private async Task<(int Activo, int[] Lineas)> PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        var banco = await catalogo.AltaAsync(_ctx, new AltaCuenta("5720001", "Banco uno ficticio"));
        Assert.True(banco.EsCorrecto, banco.Error?.ToString());
        foreach (var (codigo, nombre) in new[] { ("54001", "Inversiones financieras"), ("66901", "Gastos financieros"), ("76001", "Dividendos"), ("76301", "Incremento de valor"),
                     ("66301", "Decremento de valor"), ("47301", "Retenciones"), ("76600", "Beneficios por venta"), ("66600", "Pérdidas por venta") })
            Assert.True((await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        var (tipo, centro) = await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "1500", "0012345678", "EUR", "Banco uno ficticio", true);
            var idBanco = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                idBanco, banco.Valor.IdIdentidadContable, new DateTime(2000, 1, 1));
            await s.EjecutarAsync("INSERT INTO CentrosCoste (IdSociedad, IdCentroCostePadre, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", null, "4", "INVERSIONES ETFS", true, true, (short)1, (byte)1, "4", "0004");
            var c = await s.UltimoIdAsync();
            var t = Convert.ToInt32(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'INVERSION'"), CultureInfo.InvariantCulture);
            return (t, c);
        });
        Assert.Equal(8, await ContarAsync("SELECT COUNT(*) FROM PlantillasSubcuentasActivo WHERE IdTipoActivo = ?", tipo));
        var dia = new DateTime(2026, 3, 2);
        var instrumento = new DatosInstrumento("IE00B4L5Y983", "iShares Core MSCI World", "IWDA.AS", "IWDA.AS", "Amsterdam", "EUR", "ETF", FuenteCotizacion.Yahoo);
        var datos = new DatosActivo
        {
            Codigo = "IWDA", Nombre = "iShares Core MSCI World", NombreCorto = "IWDA", IdTipoActivo = tipo, CodigoTipo = TiposActivoPgc.Inversion, FechaCompra = dia, Coste = 1000m,
            IdCentroCoste = centro,
        };
        var alta = await Activos.AltaAsync(_ctx, new AltaActivo(datos, null, instrumento, new CompraInversion(dia, 10m, 100m, "EUR", 1m, 1000m, null)));
        Assert.True(alta.EsCorrecto, alta.Error?.ToString());
        var g = await Activos.Generador.GenerarAsync(_ctx, [tipo], [alta.Valor]);
        Assert.Empty(g.Errores);
        var venta = await Activos.VenderAsync(_ctx, alta.Valor, new CompraInversion(new DateTime(2026, 6, 1), 4m, 130m, "EUR", 1m, 520m, null), importeEscrito: false);
        Assert.True(venta.EsCorrecto, venta.Error?.ToString());

        var movs = new[]
        {
            new Mov(new DateTime(2026, 3, 3), 1002.50m, true, "COMPRA VALORES IWDA"),
            new Mov(new DateTime(2026, 6, 2), 530m, false, "VENTA VALORES IWDA"),
        };
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 5000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "uno.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        var lineas = (await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario")))).ToArray();
        return (alta.Valor, lineas);
    }

    [Fact]
    public async Task Concilia_la_compra_y_la_venta_con_su_asiento_y_deshacer_la_deja_sin_conciliar()
    {
        var (activo, ids) = await PrepararAsync();

        // Cargo: solo se ofrecen compras.
        var pc = await Servicio.ProponerAsync("T01", ids[0]);
        Assert.Null(pc!.Bloqueo);
        Assert.True(pc.EsCompra);
        var compra = Assert.Single(pc.Operaciones);
        Assert.Equal((1000m, 2.50m), (compra.Operacion.ImporteTotalEur, compra.Diferencia));

        var rc = await Servicio.ConciliarAsync("T01", ids[0], compra.Operacion.IdOperacion, _actor);
        Assert.True(rc.EsCorrecto, string.Join(" ", rc.Errores.Select(e => e.TextoOperador())));
        var asientoCompra = await new MotorAsientosAccess(Bd).ObtenerAsync(rc.IdAsiento!.Value);
        Assert.Equal([("5400101", 1002.50m, 0m), ("5720001", 0m, 1002.50m)],
            asientoCompra!.Apuntes.Select(a => (a.CodigoCuenta.Trim(), a.Debe.Valor, a.Haber.Valor)).OrderBy(a => a.Item1));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM OperacionesInversion WHERE IdOperacionInversion = ? AND LineaN43 = ? AND ImporteTotalEur = 1002.5 AND ImporteAntesConciliar = 1000",
            compra.Operacion.IdOperacion, ids[0]));
        // Ya conciliada no se vuelve a ofrecer, y su importe no se cambia a mano.
        Assert.Equal("ActivoN43.Contabilizada", (await Servicio.ProponerAsync("T01", ids[0]))!.Bloqueo?.Codigo);
        Assert.False((await Activos.CambiarCosteLoteAsync(_ctx, compra.Operacion.IdOperacion, 999m)).EsCorrecto);

        // Abono: la venta de 4 participaciones (coste medio 4 × 100,25 = 401 €) cobrada por 530 €: 129 € de beneficio a la 76600.
        var pv = await Servicio.ProponerAsync("T01", ids[1]);
        var venta = Assert.Single(pv!.Operaciones);
        Assert.Equal(401m, venta.Operacion.CosteVendido);
        var rv = await Servicio.ConciliarAsync("T01", ids[1], venta.Operacion.IdOperacion, _actor);
        Assert.True(rv.EsCorrecto, string.Join(" ", rv.Errores.Select(e => e.TextoOperador())));
        var asientoVenta = await new MotorAsientosAccess(Bd).ObtenerAsync(rv.IdAsiento!.Value);
        Assert.Equal([("5400101", 0m, 401m), ("5720001", 530m, 0m), ("7660001", 0m, 129m)],
            asientoVenta!.Apuntes.Select(a => (a.CodigoCuenta.Trim(), a.Debe.Valor, a.Haber.Valor)).OrderBy(a => a.Item1));
        var inv = (await Activos.InversionAsync(activo))!;
        Assert.Equal((6m, 601.50m), (inv.Participaciones, inv.Coste));

        // Deshacer el asiento de la venta: la venta vuelve a estar sin conciliar con su importe estimado, y se vuelve a ofrecer.
        var d = await Conciliacion.DeshacerAsync("T01", [ids[1]], _actor, "Prueba");
        Assert.Equal(1, d.Hechas);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM OperacionesInversion WHERE IdOperacionInversion = ? AND LineaN43 IS NULL AND ImporteTotalEur = 520 AND ImporteAntesConciliar IS NULL",
            venta.Operacion.IdOperacion));
        Assert.Single((await Servicio.ProponerAsync("T01", ids[1]))!.Operaciones);
        var deshecha = await LineaAsync(ids[1]);
        Assert.Null(deshecha.IdAsiento);
        Assert.Empty(deshecha.Destinos);
    }
}
