using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Plataformas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>
/// Conciliación de los cobros de las plataformas (norma43/conciliacion/plataformas) contra una copia de pruebas con datos inventados: un cobro
/// de Booking de dos reservas y una reserva de Airbnb pagada en tres transferencias quedan enlazados con sus facturas, con la 430 de la
/// plataforma en las líneas, y deshacer los deja como estaban.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class CobrosPlataformasN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioCobrosPlataformasN43 Servicio => new(Bd);

    private Task<int> EnteroAsync(string sql, params object?[] p) =>
        Bd.EjecutarAsync(async s => await s.EscalarAsync(sql, p) is { } v and not DBNull ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : 0);

    /// <summary>Facturas de las reservas, por nombre: dos de Booking (B1, B2), una de Airbnb (A1) y otra de Airbnb sin cobro (A2).</summary>
    private readonly Dictionary<string, int> _facturas = [];

    /// <summary>Prepara la sociedad, la 430 compartida de los huéspedes, las reservas facturadas y el extracto; devuelve las líneas por orden.</summary>
    private async Task<int[]> PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco uno ficticio"), ("4300080", "Huéspedes Airbnb"), ("4300081", "Huéspedes Booking") })
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "1500", "0012345678", "EUR", "Banco uno ficticio", true);
            var banco = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                banco, _identidades["5720001"], new DateTime(2000, 1, 1));
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Facturas 2026", 1, 99999, "S", false, true);
            var serie = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdSerie) FROM SeriesFacturacion WHERE IdSociedadEjercicio = 9001"), CultureInfo.InvariantCulture);

            // Orígenes sin plazos configurados: salen de lo conocido de Airbnb y Booking por su código.
            var origenes = new Dictionary<string, int>();
            foreach (var (codigo, nombre, textos) in new[] { ("AIRBNB", "Airbnb", "Airbnb"), ("BOOKING", "Booking", "Booking.com;Booking") })
            {
                await s.EjecutarAsync("DELETE FROM OrigenesVenta WHERE IdSociedad = ? AND Codigo = ?", "T01", codigo);
                await s.EjecutarAsync("INSERT INTO OrigenesVenta (IdSociedad, Codigo, Nombre, TextosImportacion, EsDirecta, Activo) VALUES (?, ?, ?, ?, False, True)",
                    "T01", codigo, nombre, textos);
                origenes[codigo] = await s.UltimoIdAsync();
            }

            // Cada plataforma, un cliente con su 430 que comparten las facturas nominales de sus huéspedes.
            var clientes = new Dictionary<string, int>();
            foreach (var (codigo, cuenta) in new[] { ("AIRBNB", "4300080"), ("BOOKING", "4300081") })
            {
                await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", $"Huéspedes {codigo}", true, 0);
                var t = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", t, t, true);
                await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable, Clase) VALUES (?, ?, ?, ?)", t, "T01", _identidades[cuenta], "CLIENTE");
                clientes[codigo] = t;
            }

            var numero = 0;
            foreach (var (nombre, codigo, entrada, noches, total, comision) in new[]
                     {
                         ("B1", "BOOKING", new DateTime(2026, 5, 1), 3, 529.20m, 79.38m), // espera 442,94
                         ("B2", "BOOKING", new DateTime(2026, 5, 2), 4, 600m, 90m),       // espera 502,20
                         ("A1", "AIRBNB", new DateTime(2026, 5, 2), 14, 1200m, 186m),     // espera 1.014,00
                         ("A2", "AIRBNB", new DateTime(2026, 5, 20), 3, 500m, 77.5m),
                     })
            {
                numero++;
                await s.EjecutarAsync(
                    "INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, IdCliente, Nombre, Descripcion, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, BaseImponible, ImporteTotal) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    9001, serie, numero.ToString("0000", CultureInfo.InvariantCulture), clientes[codigo], $"Huésped ficticio {nombre}", "Alquiler ficticio", (byte)1, "F2",
                    entrada, entrada, total, total);
                var factura = await s.UltimoIdAsync();
                _facturas[nombre] = factura;
                await s.EjecutarAsync(
                    "INSERT INTO VentasPorFacturar (IdSociedad, IdOrigenVenta, FechaReferencia, FechaFin, Cantidad, Importe, Comision, Anulada, FacturadaFuera, IdFactura, Fuente, FechaAlta, Version) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, False, False, ?, 'PRUEBAS', ?, 0)",
                    "T01", origenes[codigo], entrada, entrada.AddDays(noches), noches, total, comision, factura, entrada);
            }
            return 0;
        });
        var movs = new[]
        {
            new Mov(new DateTime(2026, 5, 5), 400m, false, "AIRBNB PAYMENTS LUXEMBOURG S.A. Airbnb"),
            new Mov(new DateTime(2026, 5, 9), 314m, false, "AIRBNB PAYMENTS LUXEMBOURG S.A. Airbnb"),
            new Mov(new DateTime(2026, 5, 10), 945.14m, false, "Booking.com B.V. NO.ficticio/ID.1"),
            new Mov(new DateTime(2026, 5, 12), 300m, false, "AIRBNB PAYMENTS LUXEMBOURG S.A. Airbnb"),
            new Mov(new DateTime(2026, 5, 12), 77.77m, false, "AIRBNB PAYMENTS LUXEMBOURG S.A. Airbnb"),
            new Mov(new DateTime(2026, 5, 13), 300m, false, "TRANSFERENCIA DE CLIENTE FICTICIO"),
        };
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 1000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "uno.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario")))).ToArray();
    }

    [Fact]
    public async Task Concilia_un_cobro_agrupado_de_Booking_y_uno_fraccionado_de_Airbnb_y_deshacer_lo_deja_como_estaba()
    {
        var ids = await PrepararAsync();
        var (a1, a2, booking, a3, suelto, otro) = (ids[0], ids[1], ids[2], ids[3], ids[4], ids[5]);
        var desde = new DateTime(2026, 5, 1);
        var hasta = new DateTime(2026, 5, 31);

        var p = await Servicio.ProponerAsync("T01", desde, hasta);

        // La transferencia de un cliente cualquiera no es de ninguna plataforma.
        Assert.False(p.Lineas.ContainsKey(otro));
        Assert.Equal(2, p.Cruces.Count);
        var agrupado = Assert.Single(p.Cruces, c => c.Tipo == TipoCrucePlataforma.Agrupado);
        Assert.Equal([booking], agrupado.Lineas);
        Assert.Equal(new[] { _facturas["B1"], _facturas["B2"] }.Order(), agrupado.Facturas.Order());
        var fraccionado = Assert.Single(p.Cruces, c => c.Tipo == TipoCrucePlataforma.Fraccionado);
        Assert.Equal([a1, a2, a3], fraccionado.Lineas);
        Assert.Equal(suelto, Assert.Single(p.SinCruzar).IdLinea);
        Assert.Equal(442.94m, p.Facturas[_facturas["B1"]].Esperado);
        Assert.Equal([_facturas["A2"]], p.SinCobro.Select(f => f.Id));

        var r = await Servicio.ConciliarAsync("T01", p.Cruces, _actor);
        Assert.Empty(r.Rechazados);
        Assert.Empty(r.Avisos);
        Assert.Equal(2, r.Cruces);

        // Booking: las dos facturas cobradas por la misma línea. Airbnb: la última transferencia completa la factura; las otras dos, trozos.
        Assert.Equal(booking, await EnteroAsync("SELECT LineaN43 FROM Facturas WHERE IdFactura = ?", _facturas["B1"]));
        Assert.Equal(booking, await EnteroAsync("SELECT LineaN43 FROM Facturas WHERE IdFactura = ?", _facturas["B2"]));
        Assert.Equal(a3, await EnteroAsync("SELECT LineaN43 FROM Facturas WHERE IdFactura = ?", _facturas["A1"]));
        Assert.Equal(2, await EnteroAsync("SELECT COUNT(*) FROM CobrosParcialesN43 WHERE IdFactura = ?", _facturas["A1"]));
        Assert.Equal(714, await EnteroAsync("SELECT SUM(Importe) FROM CobrosParcialesN43 WHERE IdFactura = ?", _facturas["A1"]));

        // Las líneas llevan la 430 de su plataforma y la rejilla enseña la factura también en los trozos.
        var lineas = (await new ConsultaLineasN43(Bd).LineasAsync("T01", [2026], [a1, a2, booking, a3])).ToDictionary(l => l.Id);
        Assert.Equal("4300081", lineas[booking].CuentaDestino);
        Assert.Equal("S0001, S0002", lineas[booking].Facturas);
        foreach (var id in new[] { a1, a2, a3 })
        {
            Assert.Equal("4300080", lineas[id].CuentaDestino);
            Assert.Equal("S0003", lineas[id].Facturas);
        }

        // Una segunda propuesta ya no los ofrece.
        var otra = await Servicio.ProponerAsync("T01", desde, hasta);
        Assert.Empty(otra.Cruces);
        Assert.Equal(suelto, Assert.Single(otra.SinCruzar).IdLinea);

        // Deshacer desde un trozo quita todo el cobro de la reserva; el de Booking sigue.
        var d = await Servicio.DeshacerAsync("T01", [a2], _actor);
        Assert.Empty(d.Rechazados);
        Assert.Equal([_facturas["A1"]], d.Facturas);
        Assert.Equal(0, await EnteroAsync("SELECT COUNT(*) FROM CobrosParcialesN43"));
        Assert.Equal(0, await EnteroAsync("SELECT COUNT(*) FROM Facturas WHERE IdFactura = ? AND LineaN43 IS NOT NULL", _facturas["A1"]));
        Assert.Equal(booking, await EnteroAsync("SELECT LineaN43 FROM Facturas WHERE IdFactura = ?", _facturas["B1"]));
        Assert.Single((await Servicio.ProponerAsync("T01", desde, hasta)).Cruces);
    }

    [Fact]
    public async Task Un_cruce_cuya_linea_se_enlazo_mientras_tanto_se_rechaza_entero()
    {
        var ids = await PrepararAsync();
        var p = await Servicio.ProponerAsync("T01", new DateTime(2026, 5, 1), new DateTime(2026, 5, 31));
        var fraccionado = p.Cruces.Single(c => c.Tipo == TipoCrucePlataforma.Fraccionado);
        // Otro puesto enlaza el segundo trozo con la factura que queda sin cobro.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Facturas SET LineaN43 = ? WHERE IdFactura = ?", ids[1], _facturas["A2"]));

        var r = await Servicio.ConciliarAsync("T01", p.Cruces, _actor);

        Assert.Equal(1, r.Cruces);
        Assert.Equal("Plataformas.LineaEnlazada", Assert.Single(r.Rechazados).Codigo);
        Assert.Equal(0, await EnteroAsync("SELECT COUNT(*) FROM CobrosParcialesN43"));
        Assert.Equal(0, await EnteroAsync("SELECT COUNT(*) FROM Facturas WHERE IdFactura = ? AND LineaN43 IS NOT NULL", fraccionado.Facturas[0]));
    }
}
