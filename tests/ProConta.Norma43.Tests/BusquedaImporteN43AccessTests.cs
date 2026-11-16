using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Busqueda;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>Búsqueda por importe (norma43/busqueda-por-importe) contra una copia de pruebas con datos inventados: candidatas, enlace, cuenta del tercero y desenlace.</summary>
[Trait("Categoria", "Access")]
public sealed class BusquedaImporteN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private BusquedaPorImporteN43 Busqueda => new(Bd);
    private ConsultaLineasN43 Consulta => new(Bd);

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private async Task<LineaN43> LineaAsync(int id) => Assert.Single(await Consulta.LineasAsync("T01", [2026], [id]));

    /// <summary>
    /// Sociedad T01 (y otra, T02, con una compra del mismo importe que no debe salir), cuentas, un banco con un extracto de tres movimientos
    /// (cargo de 121 €, abono de 250 € y abono de 99 €), un proveedor y un cliente con sus facturas. Devuelve los Id de las líneas.
    /// </summary>
    private async Task<int[]> PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.SociedadAsync(Bd, "T02", "Otra sociedad sintética", "B87654321");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.EjercicioAsync(Bd, 9101, "T02", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco uno ficticio"), ("4000001", "Proveedor ficticio"), ("4100001", "Acreedor ficticio"), ("4300001", "Cliente ficticio"), ("6280001", "Suministros") })
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "1500", "0012345678", "EUR", "Banco uno ficticio", true);
            var id = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                id, _identidades["5720001"], new DateTime(2000, 1, 1));

            // Cliente con su cuenta 430 en T01.
            await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", "Cliente Ficticio S.L.", true, 0);
            var tercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", tercero, tercero, true);
            await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable, Clase) VALUES (?, ?, ?, ?)", tercero, "T01", _identidades["4300001"], "CLIENTE");
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Facturas 2026", 1, 99999, "S", false, true);
            var serie = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdSerie) FROM SeriesFacturacion WHERE IdSociedadEjercicio = 9001"), CultureInfo.InvariantCulture);
            // Ventas de 250 €: una buena (11 días antes), un borrador sin número y otra 8 días después del cobro (fuera de plazo).
            foreach (var (numero, fecha) in new[] { ("0001", new DateTime(2026, 3, 1)), (null, new DateTime(2026, 3, 10)), ("0003", new DateTime(2026, 3, 20)) })
                await s.EjecutarAsync(
                    "INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, IdCliente, Nombre, Descripcion, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, BaseImponible, ImporteTotal) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    9001, serie, (object?)numero ?? DBNull.Value, tercero, "Cliente Ficticio S.L.", "Alquiler ficticio", numero is null ? (byte)3 : (byte)1, "F1", fecha, fecha, 206.61m, 250m);

            // Compras de 121 €: la buena (de T01, 4000001), una ya enlazada con otra línea y otra de T02.
            foreach (var (numero, ejercicio, sociedad, linea) in new[] { ("C-1", 9001, "T01", (int?)null), ("C-2", 9001, "T01", 999999), ("C-3", 9101, "T02", null) })
                await s.EjecutarAsync(
                    "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, CuentaContableProveedor, NumeroFactura, Fecha, ImporteTotal, Carpeta, NIF, MD5, Pagada, Exportada, Duplicada, InversionSujetoPasivo, LineaN43) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    sociedad, "2026", ejercicio, "4000001", numero, new DateTime(2026, 3, 9), 121m, "Ficticio", "A00000000", numero, false, false, false, false, (object?)linea ?? DBNull.Value);
            // Otra de 99 € de otro proveedor (4100001), para el cargo de varias facturas de terceros distintos.
            await s.EjecutarAsync(
                "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, CuentaContableProveedor, NumeroFactura, Fecha, ImporteTotal, Carpeta, NIF, MD5, Pagada, Exportada, Duplicada, InversionSujetoPasivo) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", "2026", 9001, "4100001", "C-4", new DateTime(2026, 3, 9), 99m, "Ficticio", "A11111111", "C-4", false, false, false, false);
            return 0;
        });
        var movs = new[]
        {
            new Mov(new DateTime(2026, 3, 10), 121m, true, "PAGO A PROVEEDOR FICTICIO"),
            new Mov(new DateTime(2026, 3, 12), 250m, false, "TRANSFERENCIA DE CLIENTE FICTICIO"),
            new Mov(new DateTime(2026, 3, 13), 99m, true, "PAGO A ACREEDOR FICTICIO"),
        };
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 1000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "uno.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario")))).ToArray();
    }

    private Task<int> IdCompraAsync(string numero) => ContarAsync("SELECT IdRegistro FROM FacturasCompra WHERE NumeroFactura = ?", numero);
    private Task<int> IdVentaAsync(string numero) => ContarAsync("SELECT IdFactura FROM Facturas WHERE NumeroFactura = ?", numero);

    [Fact]
    public async Task Busca_solo_las_facturas_de_la_sociedad_con_numero_y_en_plazo_y_enlazar_pone_la_cuenta_del_tercero()
    {
        var ids = await PrepararAsync();
        var lineas = (await Consulta.LineasAsync("T01", [2026], ids)).OrderBy(l => l.Id).ToList();
        var b = await Busqueda.BuscarAsync("T01", lineas);

        // Cargo de 121 €: solo C-1 (C-2 ya está con otra línea y C-3 es de otra sociedad). Abono de 250 €: solo la 0001.
        var compra = Assert.Single(b[0].Candidatos);
        Assert.Equal(("C-1", 1, false), (compra.Documento.Numero, compra.Dias, compra.Enlazada));
        var venta = Assert.Single(b[1].Candidatos);
        Assert.Equal(("S0001", TipoDocumentoN43.Venta, 11), (venta.Documento.Numero, venta.Documento.Tipo, venta.Dias));
        Assert.Equal("Cliente Ficticio S.L.", venta.Documento.Tercero);
        Assert.Equal("C-4", Assert.Single(b[2].Candidatos).Documento.Numero);

        var r = await Busqueda.EnlazarAsync("T01",
            [new(ids[0], TipoDocumentoN43.Compra, compra.Documento.Id, true), new(ids[1], TipoDocumentoN43.Venta, venta.Documento.Id, true)], _actor);
        Assert.Empty(r.Rechazadas);
        Assert.Empty(r.Avisos);
        Assert.Equal(2, r.Enlazadas);
        Assert.Equal([ids[0], ids[1]], r.ConCuenta);
        Assert.Equal(ids[0], await ContarAsync("SELECT LineaN43 FROM FacturasCompra WHERE IdRegistro = ?", compra.Documento.Id));
        Assert.Equal(ids[1], await ContarAsync("SELECT LineaN43 FROM Facturas WHERE IdFactura = ?", venta.Documento.Id));
        // La factura de venta sigue sin asiento propio: enlazar no la «exporta».
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Facturas WHERE IdFactura = ? AND IdAsientoContable IS NOT NULL", venta.Documento.Id));

        var pago = await LineaAsync(ids[0]);
        Assert.Equal(("4000001", BusquedaPorImporteN43.OrigenFactura, "C-1"), (pago.CuentaDestino, pago.Destinos[0].Origen, pago.Facturas));
        var cobro = await LineaAsync(ids[1]);
        Assert.Equal(("4300001", -250m, "S0001"), (cobro.CuentaDestino, cobro.Destinos[0].ImporteFirmado, cobro.Facturas));

        // Ya enlazada, sale marcada como tal y otra búsqueda del mismo movimiento la sigue ofreciendo.
        Assert.True(Assert.Single((await Busqueda.BuscarAsync("T01", [pago]))[0].Candidatos).Enlazada);

        // Desmarcarla quita el enlace; la cuenta se queda.
        var d = await Busqueda.EnlazarAsync("T01", [new(ids[0], TipoDocumentoN43.Compra, compra.Documento.Id, false)], _actor);
        Assert.Equal(1, d.Desenlazadas);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM FacturasCompra WHERE IdRegistro = ? AND LineaN43 IS NOT NULL", compra.Documento.Id));
        var sinFactura = await LineaAsync(ids[0]);
        Assert.Null(sinFactura.Facturas);
        Assert.Equal("4000001", sinFactura.CuentaDestino);
    }

    [Fact]
    public async Task Una_factura_enlazada_con_otra_linea_se_rechaza_y_la_linea_queda_como_estaba()
    {
        var ids = await PrepararAsync();
        var c1 = await IdCompraAsync("C-1");
        Assert.Empty((await Busqueda.EnlazarAsync("T01", [new(ids[2], TipoDocumentoN43.Compra, c1, true)], _actor)).Rechazadas);

        // Otra búsqueda (abierta antes) intenta llevarse C-1 al cargo de 121 € junto con C-4: se rechaza la línea entera.
        var c4 = await IdCompraAsync("C-4");
        var r = await Busqueda.EnlazarAsync("T01", [new(ids[0], TipoDocumentoN43.Compra, c4, true), new(ids[0], TipoDocumentoN43.Compra, c1, true)], _actor);
        Assert.Equal("BusquedaImporte.FacturaEnlazada", r.Rechazadas[ids[0]].Codigo);
        Assert.Equal(0, r.Enlazadas);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM FacturasCompra WHERE LineaN43 = ?", ids[0]));

        // La misma factura marcada en dos líneas a la vez tampoco.
        var dos = await Busqueda.EnlazarAsync("T01", [new(ids[0], TipoDocumentoN43.Compra, c4, true), new(ids[1], TipoDocumentoN43.Compra, c4, true)], _actor);
        Assert.Equal(["BusquedaImporte.FacturaRepetida", "BusquedaImporte.FacturaRepetida"], dos.Rechazadas.Values.Select(e => e.Codigo));

        // Una factura de otra sociedad no se encuentra.
        var c3 = await IdCompraAsync("C-3");
        Assert.Equal("BusquedaImporte.FacturaNoExiste", (await Busqueda.EnlazarAsync("T01", [new(ids[0], TipoDocumentoN43.Compra, c3, true)], _actor)).Rechazadas[ids[0]].Codigo);
    }

    [Fact]
    public async Task Con_facturas_de_terceros_distintos_o_ya_contabilizada_la_linea_se_enlaza_sin_cambiar_su_cuenta()
    {
        var ids = await PrepararAsync();
        var c1 = await IdCompraAsync("C-1");
        var c4 = await IdCompraAsync("C-4");
        var r = await Busqueda.EnlazarAsync("T01", [new(ids[0], TipoDocumentoN43.Compra, c1, true), new(ids[0], TipoDocumentoN43.Compra, c4, true)], _actor);
        Assert.Equal(2, r.Enlazadas);
        Assert.Empty(r.ConCuenta);
        Assert.Contains("terceros distintos", Assert.Single(r.Avisos));
        Assert.Equal("C-1, C-4", (await LineaAsync(ids[0])).Facturas);
        Assert.Equal(EstadoLineaN43.SinCuenta, (await LineaAsync(ids[0])).Estado);

        // Línea contabilizada con otra cuenta: se enlaza, pero ni su cuenta ni su asiento cambian.
        Assert.Empty((await new ServicioLineasN43(Bd).AsignarCuentaAsync("T01", [ids[1]], _identidades["6280001"], _actor)).Rechazadas);
        var conciliacion = new ServicioConciliacionN43(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd));
        Assert.Empty((await conciliacion.ContabilizarLineasAsync("T01", [ids[1]], _actor)).Rechazadas);
        var v1 = await IdVentaAsync("0001");
        var e = await Busqueda.EnlazarAsync("T01", [new(ids[1], TipoDocumentoN43.Venta, v1, true)], _actor);
        Assert.Equal((1, 0), (e.Enlazadas, e.ConCuenta.Count));
        var l = await LineaAsync(ids[1]);
        Assert.Equal(("6280001", EstadoLineaN43.Contabilizada, "S0001"), (l.CuentaDestino, l.Estado, l.Facturas));
    }
}
