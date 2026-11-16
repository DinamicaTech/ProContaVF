using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Busqueda;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Remesas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>
/// Explosión de remesas y conciliación de devoluciones (norma43/remesas) contra una copia de pruebas con datos inventados: propuesta,
/// explosión, cuadre de la contabilidad, devoluciones de uno y de varios recibos, reintento de cobro y deshacer.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class RemesasN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];
    private readonly Dictionary<string, int> _facturas = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioRemesasN43 Remesas => new(Bd);
    private ConsultaLineasN43 Consulta => new(Bd);

    private Task<int> EnteroAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p) is { } v and not DBNull ? v : 0, CultureInfo.InvariantCulture));

    private async Task<LineaN43> LineaAsync(int id) => Assert.Single(await Consulta.LineasAsync("T01", null, [id]));

    /// <summary>
    /// Sociedad T01 con su banco y las dos cuentas ficticias (remesas y devueltos), dos clientes con su 430 y cinco facturas de septiembre:
    /// S0001 (500, día 1), S0002 (800, día 5) y S0003 (300, día 5) domiciliadas; S0004 (900, día 20) domiciliada pero lejos; S0005 (700) por
    /// transferencia. El extracto trae la remesa del 4/9 (1.600 €), una devolución de 800 € el 10/9 y otra de 800 € (500 + 300) el 12/9.
    /// </summary>
    private async Task<int[]> PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco uno ficticio"), ("5550025", "Remesas ficticias"), ("5550035", "Devueltos ficticios"),
                     ("4300001", "Cliente uno"), ("4300002", "Cliente dos") })
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        await Bd.EjecutarAsync(async s =>
        {
            foreach (var (cuenta, nombre, contable) in new[] { ("0012345678", "Banco uno ficticio", "5720001"), (ServicioRemesasN43.CuentaRemesas, "Banco uno REMESAS", "5550025"),
                         (ServicioRemesasN43.CuentaDevueltos, "Banco uno RECIBOS DEVUELTOS", "5550035") })
            {
                await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                    "T01", "0049", "1500", cuenta, "EUR", nombre, true);
                var id = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                    id, _identidades[contable], new DateTime(2000, 1, 1));
            }
            foreach (var (id, nombre, sepa) in new[] { ((byte)110, "Recibo domiciliado", true), ((byte)129, "Transferencia", false) })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM FormasPago WHERE Id = ?", id), CultureInfo.InvariantCulture) == 0)
                    await s.EjecutarAsync("INSERT INTO FormasPago (Id, Descripcion, SEPA) VALUES (?, ?, ?)", id, nombre, sepa);
            var clientes = new Dictionary<string, int>();
            foreach (var (nombre, cuenta) in new[] { ("Cliente Uno S.L.", "4300001"), ("Cliente Dos S.L.", "4300002") })
            {
                await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", nombre, true, 0);
                var t = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", t, t, true);
                await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable, Clase) VALUES (?, ?, ?, ?)", t, "T01", _identidades[cuenta], "CLIENTE");
                clientes[cuenta] = t;
            }
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Facturas 2026", 1, 99999, "S", false, true);
            var serie = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdSerie) FROM SeriesFacturacion WHERE IdSociedadEjercicio = 9001"), CultureInfo.InvariantCulture);
            foreach (var (numero, importe, dia, forma, cliente) in new[] { ("0001", 500m, 1, 110, "4300001"), ("0002", 800m, 5, 110, "4300002"), ("0003", 300m, 5, 110, "4300002"),
                         ("0004", 900m, 20, 110, "4300001"), ("0005", 700m, 1, 129, "4300001") })
            {
                await s.EjecutarAsync(
                    "INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, IdCliente, Nombre, Descripcion, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, BaseImponible, ImporteTotal, FormaDePago, DiaDePago) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    9001, serie, numero, clientes[cliente], "Cliente ficticio", $"Alquiler ficticio {numero}", (byte)1, "F1", new DateTime(2026, 9, 1), new DateTime(2026, 9, 1),
                    importe, importe, (byte)forma, (byte)dia);
                _facturas[numero] = await s.UltimoIdAsync();
            }
            // Palabras clave: remesa (abonos, Especial 5) y recibo devuelto (cargos, Especial 6), con su 555.
            await s.EjecutarAsync("INSERT INTO PalabrasClaveBancarias (IdSociedad, Entidad, Sentido, PalabraClave, IdIdentidadContable, Especial, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "D", "REMESA RECIBOS", _identidades["5550025"], ReglasPalabrasClave.EspecialRemesa, true);
            await s.EjecutarAsync("INSERT INTO PalabrasClaveBancarias (IdSociedad, Entidad, Sentido, PalabraClave, IdIdentidadContable, Especial, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "H", "RECIBO DEVUELTO", _identidades["5550035"], ReglasPalabrasClave.EspecialDevolucion, true);
            return 0;
        });
        var movs = new[]
        {
            new Mov(new DateTime(2026, 9, 4), 1600m, false, "REMESA RECIBOS SEPA", Ref2: "23978661"),
            new Mov(new DateTime(2026, 9, 10), 800m, true, "RECIBO DEVUELTO CLIENTE DOS"),
            new Mov(new DateTime(2026, 9, 12), 800m, true, "RECIBO DEVUELTO VARIOS"),
        };
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 1000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "uno.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario")))).ToArray();
    }

    private Task<int> CobroAsync(string numero) => EnteroAsync("SELECT LineaN43 FROM Facturas WHERE IdFactura = ?", _facturas[numero]);
    private Task<int> DevolucionAsync(string numero) => EnteroAsync("SELECT LineaN43Devolucion FROM Facturas WHERE IdFactura = ?", _facturas[numero]);

    [Fact]
    public async Task Explota_la_remesa_en_un_recibo_por_factura_y_cuadra_la_contabilidad()
    {
        var ids = await PrepararAsync();
        var remesa = await LineaAsync(ids[0]);
        Assert.Equal(ReglasPalabrasClave.EspecialRemesa, remesa.Especial);
        Assert.Equal(ReglasPalabrasClave.EspecialDevolucion, (await LineaAsync(ids[1])).Especial);

        var p = await Remesas.ProponerRemesaAsync("T01", ids[0]);
        Assert.NotNull(p);
        Assert.Null(p.Bloqueo);
        // Las domiciliadas de septiembre (no la de transferencia); marcadas las de día 1 y 5, que suman 1.600.
        Assert.Equal(["S0001", "S0002", "S0003", "S0004"], p.Candidatas.Select(c => c.Factura.Numero));
        Assert.Equal([true, true, true, false], p.Candidatas.Select(c => c.Marcada));

        // Que no cuadre no se graba.
        var mal = await Remesas.ExplotarRemesaAsync("T01", ids[0], [new(_facturas["0001"], 500m), new(_facturas["0002"], 800m)], _actor);
        Assert.Equal("Remesas.NoCuadra", mal.Error?.Codigo);
        Assert.Equal(0, await EnteroAsync("SELECT COUNT(*) FROM ExtractosBancarios WHERE IdLineaExtractoOrigen IS NOT NULL"));

        var r = await Remesas.ExplotarRemesaAsync("T01", ids[0], p.Candidatas.Where(c => c.Marcada).Select(c => new ReciboRemesa(c.Factura.Id, c.Factura.Importe)).ToList(), _actor);
        Assert.True(r.Hecho, r.Error?.TextoOperador());
        Assert.Equal(3, r.Recibos.Count);
        var recibos = (await Consulta.LineasAsync("T01", null, r.Recibos)).OrderBy(l => l.Id).ToList();
        Assert.All(recibos, l => Assert.Equal((ids[0], "H", "5550025"), (l.IdLineaOrigen!.Value, l.Sentido, l.CuentaOrigen)));
        Assert.Equal(["4300001", "4300002", "4300002"], recibos.Select(l => l.CuentaDestino));
        Assert.Equal(["S0001", "S0002", "S0003"], recibos.Select(l => l.Documento));
        Assert.Equal(1600m, recibos.Sum(l => l.Importe));
        Assert.Equal([r.Recibos[0], r.Recibos[1], r.Recibos[2]], [await CobroAsync("0001"), await CobroAsync("0002"), await CobroAsync("0003")]);
        Assert.True((await LineaAsync(ids[0])).Explotada);

        // La remesa ya explotada no se vuelve a explotar, y ya no hay pendientes.
        Assert.Equal("Remesas.YaExplotada", (await Remesas.ProponerRemesaAsync("T01", ids[0]))!.Bloqueo?.Codigo);
        Assert.Empty((await Remesas.ExplotarPendientesAsync("T01", [2026], _actor)).Explotadas);

        // Contabilidad: la remesa va de la 572 a la 555 y cada recibo de la 555 a la 430; la 555 queda a cero y debe = haber.
        var conciliacion = new ServicioConciliacionN43(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd));
        var c = await conciliacion.ContabilizarLineasAsync("T01", [ids[0], .. r.Recibos], _actor);
        Assert.Empty(c.Rechazadas);
        const string saldo = "SELECT SUM(IIF(ImporteDebe IS NULL, 0, ImporteDebe)) - SUM(IIF(ImporteHaber IS NULL, 0, ImporteHaber)) FROM Apuntes";
        Assert.Equal(0m, await Bd.EjecutarAsync(async s => Convert.ToDecimal(await s.EscalarAsync(saldo + " WHERE CuentaContable = ?", "5550025"), CultureInfo.InvariantCulture)));
        Assert.Equal(1600m, await Bd.EjecutarAsync(async s => Convert.ToDecimal(await s.EscalarAsync(saldo + " WHERE CuentaContable = ?", "5720001"), CultureInfo.InvariantCulture)));
        Assert.Equal(0m, await Bd.EjecutarAsync(async s => Convert.ToDecimal(await s.EscalarAsync(saldo), CultureInfo.InvariantCulture)));

        // Con recibos contabilizados no se deshace.
        Assert.Equal("Remesas.RecibosContabilizados", (await Remesas.DeshacerAsync("T01", ids[0], _actor)).Error?.Codigo);
    }

    [Fact]
    public async Task Con_importe_cobrado_distinto_cuadra_con_el_banco_y_la_diferencia_queda_en_la_430()
    {
        var ids = await PrepararAsync();
        // El banco cobró 790 de la S0002 (800) y 310 de la S0003 (300).
        var r = await Remesas.ExplotarRemesaAsync("T01", ids[0], [new(_facturas["0001"], 500m), new(_facturas["0002"], 790m), new(_facturas["0003"], 310m)], _actor);
        Assert.True(r.Hecho, r.Error?.TextoOperador());
        Assert.Equal([500m, 790m, 310m], (await Consulta.LineasAsync("T01", null, r.Recibos)).OrderBy(l => l.Id).Select(l => l.Importe));
    }

    [Fact]
    public async Task Una_devolucion_de_un_recibo_vuelve_a_la_430_y_la_factura_se_puede_volver_a_cobrar()
    {
        var ids = await PrepararAsync();
        var remesa = await Remesas.ExplotarPendientesAsync("T01", [2026], _actor);
        Assert.Equal([ids[0]], remesa.Explotadas);
        var reciboS0002 = await CobroAsync("0002");

        var p = await Remesas.ProponerDevolucionAsync("T01", ids[1]);
        Assert.NotNull(p);
        Assert.Null(p.Bloqueo);
        Assert.Equal(ids[0], p.Remesa!.IdLineaRemesa);
        Assert.Equal(["S0002"], p.Candidatas.Where(c => c.Marcada).Select(c => c.Recibo.Factura.Numero));

        var d = await Remesas.ConciliarDevolucionAsync("T01", ids[1], [reciboS0002], _actor);
        Assert.True(d.Hecho, d.Error?.TextoOperador());
        Assert.Empty(d.Recibos); // un solo recibo: no se explota
        var linea = await LineaAsync(ids[1]);
        Assert.Equal(("4300002", ServicioRemesasN43.OrigenDevolucion, "S0002"), (linea.CuentaDestino, linea.Destinos[0].Origen, linea.Facturas));
        Assert.Equal(ids[1], await DevolucionAsync("0002"));

        // La S0002 vuelve a estar pendiente: «Buscar por importe» la ofrece y un nuevo cobro es el intento 2.
        Assert.True(await Bd.EjecutarAsync(s => CobrosFacturasN43.PendienteAsync(s, _facturas["0002"])));
        Assert.True(await Bd.EjecutarAsync(s => CobrosFacturasN43.RegistrarCobroAsync(s, _facturas["0002"], 999001)));
        Assert.Equal((2, 999001), await Bd.EjecutarAsync(async s => { var u = await CobrosFacturasN43.UltimoAsync(s, _facturas["0002"]); return (u!.Intento, u.LineaCobro); }));
        // Con el nuevo cobro, la devolución ya no se deshace.
        Assert.Equal("Remesas.DevueltaRecobrada", (await Remesas.DeshacerAsync("T01", ids[1], _actor)).Error?.Codigo);
        Assert.True(await Bd.EjecutarAsync(s => CobrosFacturasN43.QuitarCobroAsync(s, _facturas["0002"], 999001)));

        // Deshacer la devolución deja la factura cobrada por su recibo y la línea sin cuenta.
        var u = await Remesas.DeshacerAsync("T01", ids[1], _actor);
        Assert.True(u.Hecho, u.Error?.TextoOperador());
        Assert.Equal(0, await DevolucionAsync("0002"));
        Assert.Equal(reciboS0002, await CobroAsync("0002"));
        Assert.Empty((await LineaAsync(ids[1])).Destinos);
    }

    [Fact]
    public async Task Una_devolucion_de_varios_recibos_se_explota_y_deshacer_la_remesa_exige_deshacerla_antes()
    {
        var ids = await PrepararAsync();
        Assert.Single((await Remesas.ExplotarPendientesAsync("T01", [2026], _actor)).Explotadas);

        // 800 € del 12/9: la combinación de menos recibos es la S0002 sola; el operador marca la S0001 y la S0003 (500 + 300).
        var p = await Remesas.ProponerDevolucionAsync("T01", ids[2]);
        Assert.Equal(["S0002"], p!.Candidatas.Where(c => c.Marcada).Select(c => c.Recibo.Factura.Numero));
        var elegidos = p.Candidatas.Where(c => c.Recibo.Factura.Numero is "S0001" or "S0003").Select(c => c.Recibo.IdLineaRecibo).ToList();
        var d = await Remesas.ConciliarDevolucionAsync("T01", ids[2], elegidos, _actor);
        Assert.True(d.Hecho, d.Error?.TextoOperador());
        Assert.Equal(2, d.Recibos.Count);
        var hijas = (await Consulta.LineasAsync("T01", null, d.Recibos)).OrderBy(l => l.Id).ToList();
        Assert.All(hijas, h => Assert.Equal(("D", "5550035", ids[2]), (h.Sentido, h.CuentaOrigen, h.IdLineaOrigen!.Value)));
        Assert.Equal(["4300001", "4300002"], hijas.Select(h => h.CuentaDestino));
        Assert.Equal([d.Recibos[0], d.Recibos[1]], [await DevolucionAsync("0001"), await DevolucionAsync("0003")]);

        // Esos recibos ya no salen como cobrados en otra devolución.
        var otra = await Remesas.ProponerDevolucionAsync("T01", ids[1]);
        Assert.Equal(["S0002"], otra!.Candidatas.Select(c => c.Recibo.Factura.Numero));

        // La remesa no se deshace mientras tenga recibos devueltos; deshecha la devolución, sí.
        Assert.Equal("Remesas.ReciboDevuelto", (await Remesas.DeshacerAsync("T01", ids[0], _actor)).Error?.Codigo);
        Assert.True((await Remesas.DeshacerAsync("T01", ids[2], _actor)).Hecho);
        Assert.Equal(0, await DevolucionAsync("0001"));
        var u = await Remesas.DeshacerAsync("T01", ids[0], _actor);
        Assert.True(u.Hecho, u.Error?.TextoOperador());
        Assert.Equal((0, 0, 0), (await CobroAsync("0001"), await CobroAsync("0002"), await CobroAsync("0003")));
        Assert.False((await LineaAsync(ids[0])).Explotada);
        // Sus recibos desaparecen de la pantalla (lote revertido) y se puede volver a explotar.
        Assert.Empty(await Consulta.LineasAsync("T01", null, u.Recibos));
        Assert.Single((await Remesas.ExplotarPendientesAsync("T01", [2026], _actor)).Explotadas);
    }

    [Fact]
    public async Task Sin_cuenta_ficticia_de_remesas_se_explica_como_darla_de_alta()
    {
        var ids = await PrepararAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CuentasBancarias SET Cuenta = ? WHERE Cuenta = ?", "0000000099", ServicioRemesasN43.CuentaRemesas));
        var r = await Remesas.ExplotarRemesaAsync("T01", ids[0], [new(_facturas["0001"], 500m), new(_facturas["0002"], 800m), new(_facturas["0003"], 300m)], _actor);
        Assert.Equal("Remesas.SinCuentaFicticia", r.Error?.Codigo);
        Assert.Contains("cuenta 0000000000", r.Error!.AccionCorrectora);
        Assert.Equal(0, await CobroAsync("0001"));
    }

    [Fact]
    public async Task Buscar_por_importe_vuelve_a_ofrecer_una_factura_devuelta_y_su_nuevo_cobro_es_un_reintento()
    {
        var ids = await PrepararAsync();
        Assert.Single((await Remesas.ExplotarPendientesAsync("T01", [2026], _actor)).Explotadas);
        var recibo = await CobroAsync("0002");
        Assert.True((await Remesas.ConciliarDevolucionAsync("T01", ids[1], [recibo], _actor)).Hecho);

        // Un abono de 800 € posterior (la transferencia del cliente) encuentra la S0002 aunque tenga LineaN43.
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", new DateTime(2026, 9, 20), new DateTime(2026, 9, 20), 1000m, "EMPRESA FICTICIA",
            new Mov(new DateTime(2026, 9, 20), 800m, false, "TRANSFERENCIA CLIENTE DOS")).Bytes();
        Assert.Equal(VeredictoImportacion.Importable, (await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "dos.n43", null, _actor)).Veredicto);
        var cobro = await EnteroAsync("SELECT MAX(IdLineaExtractoBancario) FROM LineasExtractoBancario");
        var busqueda = new BusquedaPorImporteN43(Bd);
        var b = await busqueda.BuscarAsync("T01", [await LineaAsync(cobro)]);
        var s0002 = Assert.Single(b[0].Candidatos, c => c.Documento.Numero == "S0002");
        Assert.False(s0002.Enlazada);

        var e = await busqueda.EnlazarAsync("T01", [new(cobro, TipoDocumentoN43.Venta, s0002.Documento.Id, true)], _actor);
        Assert.Equal(1, e.Enlazadas);
        Assert.Equal(recibo, await CobroAsync("0002")); // el primer recibo se conserva
        Assert.Equal((2, cobro), await Bd.EjecutarAsync(async s => { var u = await CobrosFacturasN43.UltimoAsync(s, _facturas["0002"]); return (u!.Intento, u.LineaCobro); }));
        Assert.Equal("S0002", (await LineaAsync(cobro)).Facturas);
        Assert.True(Assert.Single((await busqueda.BuscarAsync("T01", [await LineaAsync(cobro)]))[0].Candidatos, c => c.Documento.Numero == "S0002").Enlazada);

        // Desenlazar quita el reintento y la factura vuelve a estar devuelta (pendiente).
        Assert.Equal(1, (await busqueda.EnlazarAsync("T01", [new(cobro, TipoDocumentoN43.Venta, s0002.Documento.Id, false)], _actor)).Desenlazadas);
        Assert.Equal(0, await EnteroAsync("SELECT COUNT(*) FROM ReintentosSEPA"));
        Assert.True(await Bd.EjecutarAsync(s => CobrosFacturasN43.PendienteAsync(s, _facturas["0002"])));
    }
}
