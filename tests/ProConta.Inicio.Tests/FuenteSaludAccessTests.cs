using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Inicio.Salud;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Inicio.Tests;

/// <summary>Lecturas de la salud de la contabilidad sobre una copia de la maestra con datos sintéticos (2025 y 2026).</summary>
[Trait("Categoria", "Access")]
public sealed class FuenteSaludAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _c2025 = new("T01", 9001, 2025);
    private readonly ContextoSociedadEjercicio _c2026 = new("T01", 9002, 2026);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2026);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[]
                 {
                     ("1000000", "Capital sintético"), ("5720001", "Banco sintético"), ("7000001", "Ventas sintéticas"), ("4300001", "Cliente sintético"),
                     ("4300002", "Cliente con anticipo"), ("4770021", "IVA repercutido 21"), ("4720021", "IVA soportado 21"), ("4750001", "Hacienda acreedora por IVA"),
                     ("5550001", "Partidas pendientes de aplicación"), ("6290001", "Otros servicios"),
                 })
        {
            var alta = await cuentas.AltaAsync(_c2025, new AltaCuenta(codigo, nombre));
            Assert.True(alta.EsCorrecto, $"{codigo}: {alta.Error}");
            var copia = await cuentas.AltaEnEjercicioAsync(_c2026, alta.Valor.IdIdentidadContable);
            Assert.True(copia.EsCorrecto, $"{codigo} en 2026: {copia.Error}");
        }
        var motor = new MotorAsientosAccess(Bd);
        async Task Asiento(ContextoSociedadEjercicio ctx, DateTime fecha, string texto, params Apunte[] apuntes)
        {
            var a = new Asiento(ctx, fecha, texto);
            a.Apuntes.AddRange(apuntes);
            var r = await motor.GuardarManualAsync(a, _actor);
            Assert.True(r.EsCorrecto, r.Error?.ToString());
        }
        await Asiento(_c2026, new DateTime(2026, 1, 2), "Aportación", Apunte.AlDebe("5720001", 5000m), Apunte.AlHaber("1000000", 5000m));
        await Asiento(_c2026, new DateTime(2026, 2, 15), "Venta", Apunte.AlDebe("4300001", 1210m), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m));
        await Asiento(_c2026, new DateTime(2026, 2, 20), "Gasto", Apunte.AlDebe("6290001", 100m), Apunte.AlDebe("4720021", 21m), Apunte.AlHaber("5720001", 121m));
        // Liquidación del 1T (abona la 4750) y su pago (carga la 4750 contra el banco: no es una liquidación).
        await Asiento(_c2026, new DateTime(2026, 3, 31), "Liquidación IVA 1T", Apunte.AlDebe("4770021", 210m), Apunte.AlHaber("4720021", 21m), Apunte.AlHaber("4750001", 189m));
        await Asiento(_c2026, new DateTime(2026, 4, 20), "Pago 303 1T", Apunte.AlDebe("4750001", 189m), Apunte.AlHaber("5720001", 189m));
        await Asiento(_c2026, new DateTime(2026, 7, 20), "Pago 303 2T", Apunte.AlDebe("4750001", 10m), Apunte.AlHaber("5720001", 10m));
        // Un anticipo de cliente (saldo acreedor) y una partida pendiente de aplicación.
        await Asiento(_c2026, new DateTime(2026, 5, 3), "Anticipo", Apunte.AlDebe("5720001", 300m), Apunte.AlHaber("4300002", 300m));
        await Asiento(_c2026, new DateTime(2026, 6, 3), "Pendiente de aplicar", Apunte.AlDebe("5550001", 75m), Apunte.AlHaber("5720001", 75m));
        await Bd.EjecutarAsync(async s =>
        {
            // Un asiento descuadrado y un apunte a una cuenta que no existe (datos antiguos que el motor no dejaría grabar).
            await s.EjecutarAsync("INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, TotalDebe, TotalHaber, OperadorCreacion, FechaHoraCreacion, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                9002, new DateTime(2026, 8, 1), "Descuadrado", (byte)0, (byte)1, 50m, 40m, "operador.prueba", DateTime.Now, 1);
            var id = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
                id, (short)1, "6290099", 50m, null, (byte)0, false);
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
                id, (short)2, "5720001", null, 40m, (byte)0, false);
            // Una cuenta inactiva con apuntes.
            await s.EjecutarAsync("UPDATE CuentasContables SET EstaActiva = False WHERE IdSociedadEjercicio = 9002 AND Codigo = '6290001'");
            // Facturas: validadas 1, 2 y 4 (falta la 3) y un borrador creado hace 10 días.
            foreach (var (idEstado, nombre) in new[] { ((byte)1, "Validada"), ((byte)3, "Borrador") })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM EstadosFactura WHERE IdEstado = ?", idEstado)) == 0)
                    await s.EjecutarAsync("INSERT INTO EstadosFactura (IdEstado, Nombre, EsModificable, EsTerminal) VALUES (?, ?, ?, ?)", idEstado, nombre, idEstado == 3, false);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = 'F1'")) == 0)
                await s.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES ('F1', 'Factura', False)");
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9002, "Facturas 2026", 1, 99999, "S2026", false, true);
            var serie = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdSerie) FROM SeriesFacturacion WHERE IdSociedadEjercicio = 9002"));
            foreach (var (numero, estado, creada) in new[] { ("S20260001", (byte)1, new DateTime(2026, 2, 1)), ("S20260002", (byte)1, new DateTime(2026, 2, 2)),
                         ("S20260004", (byte)1, new DateTime(2026, 2, 4)), ((string?)null, (byte)3, new DateTime(2026, 11, 5)) })
                await s.EjecutarAsync("INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, Descripcion, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, BaseImponible) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    9002, serie, (object?)numero ?? DBNull.Value, "Sintética", estado, "F1", creada, creada, 100m);
            // Una factura de compra sin contabilizar y otra marcada como duplicada (no cuenta).
            foreach (var (numero, duplicada) in new[] { ("C-1", false), ("C-2", true) })
                await s.EjecutarAsync(
                    "INSERT INTO FacturasCompra (Sociedad, Ejercicio, CuentaContableProveedor, NumeroFactura, Fecha, ImporteTotal, Carpeta, NIF, MD5, Pagada, Exportada, Duplicada, InversionSujetoPasivo) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", "T01", "2026", "4000001", numero, new DateTime(2026, 9, 1), 121m, "Ficticio", "A00000000", numero, false, false, duplicada, false);
            return 0;
        });
    }

    [Fact]
    public async Task LeeLoQueNecesitaCadaRevision()
    {
        await PrepararAsync();
        var fuente = new FuenteSaludAccess(Bd);
        var ejercicios = await fuente.EjerciciosAsync("T01");
        var e2026 = ejercicios.Single(e => e.Ejercicio == 2026);

        // Solo el asiento que abona la 4750 es una liquidación; los pagos del 303 no.
        Assert.Equal([new DateTime(2026, 3, 31)], (await fuente.LiquidacionesIvaAsync(ejercicios)).Select(l => l.Fecha));

        var clientes = await fuente.SaldosAsync(e2026, "430", "436", new DateTime(2026, 11, 15));
        Assert.Equal(1210m, clientes["4300001"]);
        Assert.Equal(-300m, clientes["4300002"]);
        Assert.Equal(75m, (await fuente.SaldosAsync(e2026, "555", "555", new DateTime(2026, 11, 15)))["5550001"]);
        Assert.False((await fuente.SaldosAsync(e2026, "555", "555", new DateTime(2026, 6, 2))).ContainsKey("5550001"));

        var (asientos, descuadrados) = await fuente.CuadreDiarioAsync(e2026);
        Assert.Equal(9, asientos);
        Assert.Equal(10m, descuadrados.Single().Diferencia);

        Assert.Equal(["S20260001", "S20260002", "S20260004"], (await fuente.NumerosFacturasAsync(e2026)).Select(n => n.Numero).OrderBy(x => x));
        Assert.Single(await fuente.BorradoresVentaAsync(e2026));

        var compras = await fuente.ComprasPendientesAsync("T01", e2026);
        Assert.Equal(1, compras.Numero);
        Assert.Equal(121m, compras.Total);

        var malas = await fuente.CuentasConProblemaAsync(e2026);
        Assert.Contains(malas, c => c.Cuenta == "6290099" && !c.Existe);
        Assert.Contains(malas, c => c.Cuenta == "6290001" && c.Existe);

        // El IVA del 1T: la venta y el gasto, sin la liquidación.
        var iva = await fuente.IvaAsync(e2026);
        Assert.Equal(210m, iva.Where(a => a.Cuenta.StartsWith("477")).Sum(a => a.Haber - a.Debe));
        Assert.Equal(new VentasPendientes(0, 0, 0, 0), await fuente.VentasPendientesAsync("T01", new DateTime(2026, 12, 1)));
        Assert.Empty(await fuente.LineasBancoAsync("T01"));
        Assert.Empty(await fuente.NominasAsync(ejercicios));
        Assert.Empty(await fuente.TercerosSinNifAsync("T01", e2026));
        Assert.All(await fuente.LibrosIvaAsync(e2026), t => Assert.Equal(0, t.ApuntesLibros));
    }

    [Fact]
    public async Task LosClientesSinNifDeUna430CompartidaNoAvisan()
    {
        // facturacion/reservas 2026-11-25 14:13: los huéspedes comparten la 430 de «clientes varios» y el NIF va en cada factura.
        await PrepararAsync();
        var fuente = new FuenteSaludAccess(Bd);
        var e2026 = (await fuente.EjerciciosAsync("T01")).Single(e => e.Ejercicio == 2026);
        async Task<int> ClienteAsync(int id, string nombre, string cuenta) => await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Terceros (TipoPersona, Nombre, NombrePersona, Activo, FechaAlta, Version) VALUES ('FISICA', ?, ?, True, ?, 1)", nombre, nombre, new DateTime(2026, 1, 1));
            var tercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo, FechaAlta) VALUES (?, ?, True, ?)", id, tercero, new DateTime(2026, 1, 1));
            var identidad = Convert.ToInt32(await s.EscalarAsync("SELECT TOP 1 IdIdentidadContable FROM CuentasContables WHERE Codigo = ?", cuenta));
            await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, Clase, IdIdentidadContable) VALUES (?, 'T01', 'CLIENTE', ?)", id, identidad);
            return tercero;
        });
        await ClienteAsync(90001, "Huésped Uno", "4300001");
        await ClienteAsync(90002, "Huésped Dos", "4300001");
        var solo = await ClienteAsync(90003, "Cliente Solo", "4300002");
        var sinNif = await fuente.TercerosSinNifAsync("T01", e2026);
        Assert.Equal([solo], sinNif.Select(t => t.IdTercero).Distinct());
    }

    [Fact]
    public async Task LaRevisionCompletaNoFallaYSaleLoQueToca()
    {
        await PrepararAsync();
        var informe = await new ServicioSalud(new FuenteSaludAccess(Bd), hoy: () => new DateTime(2026, 11, 15)).RevisarAsync("T01", 2026);
        Assert.All(informe.Revisiones, r => Assert.Null(r.Error));
        var claves = informe.Elementos.Select(e => e.Clave).ToHashSet();
        Assert.Contains("303-2026-2", claves);            // sin liquidación (solo el pago): vencido
        Assert.DoesNotContain("303-2026-1", claves);      // liquidado el 31/03
        Assert.Contains("ejercicio-2027", claves);
        Assert.Contains("clientes-acreedores", claves);
        Assert.Contains("partidas-555", claves);
        Assert.Contains("diario-cuadra", claves);
        Assert.Contains("numeracion-1", informe.Elementos.Select(e => e.Clave.StartsWith("numeracion-") ? "numeracion-1" : e.Clave));
        Assert.Contains("ventas-borradores", claves);
        Assert.Contains("compras-pendientes", claves);
        Assert.Contains("cuentas-baja", claves);
    }

    [Fact]
    public async Task LeeLoQueNecesitanLosAvisosDeLasIncidenciasConLaGestoria()
    {
        await PrepararAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO CuadresCuentas (IdSociedadEjercicio, CuentaContable, Cuadrada, Saldo, Notas, Operador, FechaHora) VALUES (?, ?, ?, ?, ?, ?, ?)",
            9002, "5550001", true, 75m, "Saldo explicado", "operador.prueba", DateTime.Now));
        var fuente = new FuenteSaludAccess(Bd);
        var ejercicios = await fuente.EjerciciosAsync("T01");
        var e2026 = ejercicios.Single(e => e.Ejercicio == 2026);

        // Apuntes por prefijo, con su asiento, tipo y ejercicio.
        var apuntes = await fuente.ApuntesCuentasAsync(ejercicios, ["555", "4750"]);
        Assert.Equal(["4750001", "4750001", "4750001", "5550001"], apuntes.Select(a => a.Cuenta).Order());
        Assert.All(apuntes, a => Assert.Equal(2026, a.Ejercicio));
        Assert.Equal(-10m, apuntes.Where(a => a.Cuenta == "4750001").Sum(a => a.Haber - a.Debe));
        Assert.Empty(await fuente.ApuntesCuentasAsync(ejercicios, ["1' OR '1'='1"]));

        Assert.Equal(75m, (await fuente.CuadradasAsync(e2026))["5550001"]);

        // Gastos e ingresos sin centro (los asientos manuales de la prueba no lo llevan).
        var sinCentro = (await fuente.CuentasSinCentroAsync(e2026)).ToDictionary(x => x.Cuenta);
        Assert.Equal(-1000m, sinCentro["7000001"].Importe);
        Assert.Equal(100m, sinCentro["6290001"].Importe);

        var cartera = await fuente.CarteraAsync(e2026);
        Assert.Equal(910m, cartera.Sum(a => a.Debe - a.Haber));

        var compras = await fuente.FacturasCompraAsync("T01", e2026);
        Assert.Equal(2, compras.Count);
        Assert.Single(compras, f => f.Duplicada);

        // Con todo leído, la revisión completa no falla en ninguna pieza.
        var informe = await new ServicioSalud(fuente, hoy: () => new DateTime(2026, 11, 15)).RevisarAsync("T01", 2026);
        Assert.DoesNotContain(informe.Revisiones, r => r.Error is not null);
        // La 555 está cuadrada en el Mayor con el mismo saldo: no avisa.
        Assert.DoesNotContain(informe.Elementos, e => e.Clave == "partidas-555");
        Assert.Contains(informe.Elementos, e => e.Clave == "sin-centro");
    }
}
