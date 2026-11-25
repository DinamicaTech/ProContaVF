using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Ventas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Tests;

/// <summary>Ventas por facturar contra una copia de pruebas con datos sintéticos (facturacion/reservas 2026-11-14).</summary>
[Trait("Categoria", "Access")]
public sealed class VentasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly RelojFijo _reloj = new(new DateTime(2026, 2, 10, 10, 0, 0));
    private int _idPlataforma;
    private int _idHuesped;
    private int _idConcepto;

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private ServicioRoles Roles => new(Bd, Cuentas);

    private ServicioFacturas Facturas()
    {
        var sociedades = new ServicioSociedadesAccess(Bd);
        var contabilizador = new ContabilizadorVentas(Bd, new MotorAsientosAccess(Bd, reloj: _reloj), Cuentas, Roles, sociedades, _reloj);
        return new ServicioFacturas(Bd, sociedades, new RepositorioTercerosAccess(Bd, _reloj), Roles, Cuentas, contabilizador, reloj: _reloj);
    }

    private ServicioVentas Servicio() => new(Bd, new ServicioSociedadesAccess(Bd), Facturas(), new CatalogoConceptos(Bd, Cuentas), reloj: _reloj);

    private async Task<int> Contar(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)));

    private async Task<int> ClienteAsync(string nombre, string nif, string cuenta)
    {
        var t = await new RepositorioTercerosAccess(Bd, _reloj).GuardarAsync(new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = nombre, Nif = nif, Direccion = "Calle 1", CodigoPostal = "08001", Poblacion = "Barcelona", Pais = "724" }, _actor);
        Assert.True(t.EsCorrecto, t.Error?.ToString());
        var id = (await Roles.AsegurarClienteAsync(t.Valor.IdTercero)).Valor;
        if (!(await Cuentas.ResolverAsync(_ctx, cuenta)).EsCorrecto) Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta(cuenta, nombre))).EsCorrecto);
        Assert.True((await Roles.AsignarCuentaAsync(ClaseCuentaRol.Cliente, id, "T01", (await Cuentas.ResolverAsync(_ctx, cuenta)).Valor.IdIdentidadContable)).EsCorrecto);
        return id;
    }

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética SL");
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET DireccionFiscal = ?, CodigoPostalFiscal = ?, PoblacionFiscal = ? WHERE IdSociedad = ?", "Calle Sintética 1", "08001", "Barcelona", "T01"));
        await Bd.EjecutarAsync(async s =>
        {
            foreach (var (tipo, descripcion, rect) in new[] { ("F1", "Factura", false), ("R4", "Rectificativa (resto)", true) })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = ?", tipo)) == 0)
                    await s.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES (?, ?, ?)", tipo, descripcion, rect);
            foreach (var (id, nombre) in new[] { ((byte)0, "Pendiente de validar"), ((byte)1, "Validada"), ((byte)2, "Rectificada"), ((byte)3, "Borrador"), ((byte)4, "Rectificación pendiente") })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM EstadosFactura WHERE IdEstado = ?", id)) == 0)
                    await s.EjecutarAsync("INSERT INTO EstadosFactura (IdEstado, Nombre, EsModificable, EsTerminal) VALUES (?, ?, ?, ?)", id, nombre, id == 3, id == 2);
            return 0;
        });
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET Nombre = ?, CuentaSuplido = ? WHERE IdSociedadEjercicio = ?", "Sociedad sintética SL (2026)", "5550001", 9001));
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
            9001, "Facturas 2026", 1, 99999, "F2026", false, true));
        Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta("7520002", "Ingresos por alquiler turístico"))).EsCorrecto);
        _idPlataforma = await ClienteAsync("Plataforma Sintética SL", "B87654323", "4300080");
        _idHuesped = await ClienteAsync("Huésped Sintético SL", "B12345674", "4300090");
        var c = await new CatalogoConceptos(Bd, Cuentas).GuardarAsync(new Concepto(0, "T01", null, "Alquiler turístico", null, null, null, null, false, null, null, 0m, 0m, true, null), "7520002", _ctx);
        Assert.True(c.EsCorrecto, c.Error?.ToString());
        _idConcepto = c.Valor.IdConcepto;
    }

    private async Task<OrigenVenta> OrigenAsync(ServicioVentas servicio, string codigo, bool conCliente = true)
    {
        var origen = (await servicio.OrigenesAsync("T01")).Single(o => o.Codigo == codigo);
        // La regla general del origen: concepto, exenta E1 y la plataforma como cliente.
        if (conCliente)
            await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO ReglasVenta (IdSociedad, IdOrigenVenta, IdConcepto, CausaExencion, IdCliente, Activa, Agrupar, SepararPorActivo, AlFacturar) VALUES ('T01', ?, ?, 'E1', ?, True, 'MENSUAL', True, 'BORRADOR')",
                origen.IdOrigenVenta, _idConcepto, _idPlataforma));
        return origen;
    }

    private async Task<VentaPorFacturar> VentaAsync(ServicioVentas servicio, OrigenVenta origen, DateTime entrada, decimal importe, int noches = 3, int? cliente = null)
    {
        var v = servicio.Nueva("T01", origen);
        v.FechaReferencia = entrada;
        v.FechaFin = entrada.AddDays(noches);
        v.Cantidad = noches;
        v.Importe = importe;
        v.Comision = Math.Round(importe * 0.155m, 2);
        v.IdCliente = cliente;
        var r = await servicio.GuardarAsync(v, _actor);
        Assert.True(r.Guardada, string.Join(" | ", r.Incidencias.Select(i => i.Mensaje)));
        return r.Venta!;
    }

    [Fact]
    public async Task CreaLosOrigenesHabitualesUnaSolaVez()
    {
        await PrepararAsync();
        var servicio = Servicio();
        Assert.Equal(3, (await servicio.OrigenesAsync("T01")).Count);
        Assert.Equal(3, (await servicio.OrigenesAsync("T01")).Count);
        Assert.Contains(await servicio.OrigenesAsync("T01"), o => o.EsDirecta);
    }

    [Fact]
    public async Task FacturarCreaUnaFacturaPorClienteMesYPisoYNoDuplica()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var airbnb = await OrigenAsync(servicio, "AIRBNB");
        await VentaAsync(servicio, airbnb, new DateTime(2026, 1, 2), 1391m, 11);
        await VentaAsync(servicio, airbnb, new DateTime(2026, 1, 13), 822m, 7);
        await VentaAsync(servicio, airbnb, new DateTime(2026, 1, 30), 238m, 2);
        await VentaAsync(servicio, airbnb, new DateTime(2026, 1, 25), 450m, 2, cliente: _idHuesped);
        await VentaAsync(servicio, airbnb, new DateTime(2026, 2, 3), 500m);   // mes en curso: no entra hasta el 31/01

        var enero = new DateTime(2026, 1, 31);
        var previa = await servicio.PrevisualizarAsync("T01", hasta: enero);
        Assert.Equal(2, previa.Count);
        Assert.Equal(3, (await servicio.PrevisualizarAsync("T01")).Count);

        var r = await servicio.FacturarAsync("T01", _actor, hasta: enero);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.True(r.Valor.SinIncidencias, string.Join(" | ", r.Valor.Incidencias));
        Assert.Equal(2, r.Valor.Facturas.Count);
        var plataforma = r.Valor.Facturas.Single(f => f.Ventas == 3);
        Assert.Equal(2451m, plataforma.Total);

        var f = (await Facturas().ObtenerAsync(plataforma.IdFactura))!;
        Assert.Equal(EstadoFactura.Borrador, f.Estado);
        Assert.Equal(_idPlataforma, f.IdCliente);
        Assert.Equal(new DateTime(2026, 2, 10), f.FechaFactura);
        Assert.Equal(new DateTime(2026, 1, 31), f.FechaOperacion);
        Assert.Equal(new DateTime(2026, 1, 1), f.FechaInicioValidez);
        Assert.Equal(3, f.Lineas.Count);
        Assert.Equal(2451m, f.Totales.ImporteTotal.Valor);
        Assert.All(f.Lineas, l => Assert.Equal("E1", l.CausaExencion));
        Assert.All(f.Lineas, l => Assert.Equal(_idConcepto, l.IdConcepto));
        Assert.Equal(4, await Contar("SELECT COUNT(*) FROM VentasPorFacturar WHERE IdFactura IS NOT NULL"));

        // Repetir no crea nada más.
        var otra = await servicio.FacturarAsync("T01", _actor, hasta: enero);
        Assert.Empty(otra.Valor.Facturas);
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM Facturas"));
        Assert.Equal(EstadoVenta.EnBorrador, (await servicio.ListarAsync("T01")).First(v => v.Importe == 1391m).Estado);
    }

    [Fact]
    public async Task ElListadoDeFacturasMarcaComoVentaLaQueVieneDeVentasPorFacturar()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var airbnb = await OrigenAsync(servicio, "AIRBNB");
        await VentaAsync(servicio, airbnb, new DateTime(2026, 1, 2), 1391m, 11);
        var r = await servicio.FacturarAsync("T01", _actor);
        var idDeVentas = Assert.Single(r.Valor.Facturas).IdFactura;

        // Una factura manual del mismo ejercicio, para ver que no se marca como venta.
        var manual = (await Facturas().NuevoBorradorAsync(_ctx)).Valor;
        manual.IdCliente = _idHuesped;
        manual.Descripcion = "Factura manual sintética";
        var guardada = await Facturas().GuardarBorradorAsync(manual, _actor);
        Assert.True(guardada.EsCorrecto, guardada.Error?.ToString());

        var filas = await new ConsultaFacturas(Bd).ListarAsync(9001);
        Assert.Equal("Venta", filas.Single(f => f.IdFactura == idDeVentas).Origen);
        Assert.Equal("Manual", filas.Single(f => f.IdFactura != idDeVentas).Origen);
        Assert.True((await new ConsultaFacturas(Bd).FilaAsync(idDeVentas))!.DeVentas);
    }

    [Fact]
    public async Task BorrarElBorradorDevuelveLasVentasAPendientes()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var airbnb = await OrigenAsync(servicio, "AIRBNB");
        await VentaAsync(servicio, airbnb, new DateTime(2026, 1, 2), 1391m, 11);
        var r = await servicio.FacturarAsync("T01", _actor);
        var factura = (await Facturas().ObtenerAsync(r.Valor.Facturas[0].IdFactura))!;
        Assert.True((await Facturas().EliminarBorradorAsync(factura.IdFactura, factura.Version, _actor)).EsCorrecto);
        var venta = (await servicio.ListarAsync("T01")).Single();
        Assert.Equal(EstadoVenta.Pendiente, venta.Estado);
        Assert.Single((await servicio.FacturarAsync("T01", _actor)).Valor.Facturas);
    }

    [Fact]
    public async Task ValidarNumeraYContabilizaYLaVentaQuedaFacturada()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var airbnb = await OrigenAsync(servicio, "AIRBNB");
        var v = await VentaAsync(servicio, airbnb, new DateTime(2026, 1, 2), 1391m, 11);
        // La regla pide «facturar y exportar»: «Facturar todo» valida y contabiliza sin preguntar.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ReglasVenta SET AlFacturar = 'VALIDAR' WHERE IdOrigenVenta = ?", airbnb.IdOrigenVenta));
        var r = await servicio.FacturarAsync("T01", _actor);
        Assert.True(r.Valor.Facturas[0].Validada, string.Join(" | ", r.Valor.Incidencias));
        Assert.True(r.Valor.SinIncidencias, string.Join(" | ", r.Valor.Incidencias));
        Assert.NotNull((await Facturas().ObtenerAsync(r.Valor.Facturas[0].IdFactura))!.IdAsientoContable);
        Assert.StartsWith("F2026", r.Valor.Facturas[0].Rotulo);
        var leida = (await servicio.ObtenerAsync(v.IdVentaPorFacturar))!;
        Assert.Equal(EstadoVenta.Facturada, leida.Estado);
        // Una venta facturada ya no se edita ni se anula.
        leida.Importe = 1.0m;
        Assert.False((await servicio.GuardarAsync(leida, _actor)).Guardada);
        Assert.False((await servicio.AnularAsync(leida.IdVentaPorFacturar, leida.Version, null, _actor)).EsCorrecto);
    }

    [Fact]
    public async Task SinClienteSeQuedaPendienteYLoExplica()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var directa = await OrigenAsync(servicio, "DIRECTA", conCliente: false);
        var v = servicio.Nueva("T01", directa);
        v.FechaReferencia = new DateTime(2026, 1, 10);
        v.Importe = 2000m;
        var r = await servicio.GuardarAsync(v, _actor);
        Assert.False(r.Guardada);
        Assert.Contains(r.Incidencias, i => i.Codigo == "Ventas.SinCliente");
        Assert.Contains(r.Incidencias, i => i.Codigo == "Ventas.SinConcepto");
        // Con cliente y concepto propios se guarda aunque su origen no tenga regla.
        v.IdCliente = _idHuesped;
        v.IdConcepto = _idConcepto;
        Assert.True((await servicio.GuardarAsync(v, _actor)).Guardada);
    }

    private static ReservaAvaibook Reserva(string clave, string partner, DateTime entrada, int noches, decimal total, string estado = "CONFIRMED", string alojamiento = "377813") =>
        new(clave, clave, estado, "PARTNER", partner, entrada, entrada.AddDays(noches), alojamiento, "Apartamento sintético", total, Math.Round(total * 0.155m, 2), "Huésped");

    private sealed class SinRed : IClienteAvaibook
    {
        public Task<Resultado<IReadOnlyList<ReservaAvaibook>>> ReservasAsync(string direccion, string token, DateTime d, DateTime h, CancellationToken c = default) =>
            throw new InvalidOperationException("La prueba no usa la red.");
    }

    [Fact]
    public async Task ImportarDeAvaibookNoDuplicaAnulaCanceladasYProtegeLasFacturadas()
    {
        await PrepararAsync();
        var servicio = Servicio();
        await OrigenAsync(servicio, "AIRBNB");
        var importador = new ImportadorAvaibook(Bd, servicio, new SinRed(), reloj: _reloj);

        // Primera vez: el alojamiento no tiene piso; sus reservas se quedan fuera y el alojamiento queda dado de alta.
        var primera = await importador.AplicarAsync("T01", [Reserva("A1", "Airbnb", new DateTime(2026, 1, 2), 11, 1391m)], _actor);
        Assert.Equal(0, primera.Nuevas);
        Assert.Contains(primera.Incidencias, i => i.Contains("no tiene activo relacionado"));
        Assert.True(primera.FaltanAlojamientos);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM AlojamientosAvaibook WHERE IdExterno = '377813'"));

        // Con el alojamiento asignado (activo sintético) entra pendiente.
        var idActivo = await ActivoAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE AlojamientosAvaibook SET IdActivo = ? WHERE IdExterno = '377813'", idActivo));
        var reservas = new[] { Reserva("A1", "Airbnb", new DateTime(2026, 1, 2), 11, 1391m), Reserva("A2", "Airbnb", new DateTime(2026, 1, 13), 7, 822m) };
        var segunda = await importador.AplicarAsync("T01", reservas, _actor);
        Assert.Equal(2, segunda.Nuevas);
        Assert.False(segunda.FaltanAlojamientos);
        Assert.False(segunda.FaltanReglas);
        Assert.Empty(segunda.Incidencias);
        var tercera = await importador.AplicarAsync("T01", reservas, _actor);
        Assert.Equal(0, tercera.Nuevas);
        Assert.Equal(2, tercera.SinCambios);
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM VentasPorFacturar"));

        // Cancelada en Avaibook y pendiente: se anula.
        var cancelada = await importador.AplicarAsync("T01", [reservas[1] with { Status = "CANCELLED" }], _actor);
        Assert.Equal(1, cancelada.Anuladas);
        Assert.Equal(EstadoVenta.Anulada, (await servicio.ListarAsync("T01")).Single(v => v.IdExterno == "A2").Estado);

        // Facturada y cambia en Avaibook: no se toca, queda la incidencia.
        var f = await servicio.FacturarAsync("T01", _actor);
        Assert.Single(f.Valor.Facturas);
        var cambio = await importador.AplicarAsync("T01", [reservas[0] with { TotalAmount = 1500m }], _actor);
        Assert.Single(cambio.Incidencias);
        var venta = (await servicio.ListarAsync("T01")).Single(v => v.IdExterno == "A1");
        Assert.Equal(1391m, venta.Importe);
        Assert.NotNull(venta.Incidencia);
    }

    [Fact]
    public async Task CambiarElIvaDeLaReglaAfectaALoImportadoYPendiente()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var airbnb = await OrigenAsync(servicio, "AIRBNB");
        var idActivo = await ActivoAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO AlojamientosAvaibook (IdSociedad, IdExterno, IdActivo, Activo) VALUES ('T01', '377813', ?, True)", idActivo));
        await DatosSinteticos.TiposIvaAsync(Bd, 10m);
        if (!(await Cuentas.ResolverAsync(_ctx, "4770010")).EsCorrecto) Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta("4770010", "IVA repercutido 10"))).EsCorrecto);
        var importador = new ImportadorAvaibook(Bd, servicio, new SinRed(), reloj: _reloj);
        var r = await importador.AplicarAsync("T01", [Reserva("A1", "Airbnb", new DateTime(2026, 1, 2), 4, 1100m)], _actor);
        Assert.Equal(1, r.Nuevas);
        var venta = (await servicio.ListarAsync("T01")).Single();
        Assert.Null(venta.IdConcepto);
        Assert.Null(venta.CausaExencion);
        Assert.Equal(idActivo, venta.IdActivo);

        // La regla pasa de exenta al 10 %: la venta ya importada se factura al 10 % sin tocarla.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ReglasVenta SET CausaExencion = NULL, TipoIva = 10 WHERE IdOrigenVenta = ?", airbnb.IdOrigenVenta));
        var f = await servicio.FacturarAsync("T01", _actor);
        Assert.True(f.Valor.SinIncidencias, string.Join(" | ", f.Valor.Incidencias));
        var factura = (await Facturas().ObtenerAsync(f.Valor.Facturas[0].IdFactura))!;
        Assert.Equal(1100m, factura.Totales.ImporteTotal.Valor);
        Assert.Equal(1000m, factura.Totales.BaseImponible.Valor);
        Assert.Equal(100m, factura.Totales.Iva.Valor);
        Assert.Null(factura.Lineas[0].CausaExencion);
        // «Facturar ventas…» pasa el activo de la venta a la línea (activos 2026-11-17 20:35), y se guarda y se lee.
        Assert.Equal(idActivo, factura.Lineas[0].IdActivo);
    }

    [Fact]
    public async Task UnPortalNuevoEntraSoloYQuedaAFaltaDeSuRegla()
    {
        await PrepararAsync();
        var servicio = Servicio();
        await OrigenAsync(servicio, "AIRBNB");
        var idActivo = await ActivoAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO AlojamientosAvaibook (IdSociedad, IdExterno, IdActivo, Activo) VALUES ('T01', '377813', ?, True)", idActivo));
        var importador = new ImportadorAvaibook(Bd, servicio, new SinRed(), reloj: _reloj);
        var reservas = new[]
        {
            Reserva("V1", "Vrbo [XML]", new DateTime(2026, 1, 12), 3, 400m), Reserva("V2", "Vrbo [XML]", new DateTime(2026, 1, 22), 2, 250m),
            Reserva("A1", "Airbnb", new DateTime(2026, 1, 2), 11, 1391m), Reserva("P1", "Airbnb", new DateTime(2026, 3, 2), 2, 100m, estado: "PENDING_PAYMENT"),
        };
        var r = await importador.AplicarAsync("T01", reservas, _actor);
        Assert.Equal(3, r.Nuevas);
        Assert.Equal(["Vrbo"], r.OrigenesNuevos);
        Assert.Empty(r.Incidencias);
        Assert.True(r.FaltanReglas);
        Assert.Contains(r.SinRegla, t => t.StartsWith("Vrbo · Piso sintético: 2 ventas pendientes sin cliente y concepto"));
        Assert.Contains(r.Informacion, t => t.Contains("aún no está confirmada"));
        Assert.Contains(await servicio.OrigenesAsync("T01"), o => o.Codigo == "VRBO" && o.TextosImportacion == "Vrbo [XML]");

        // Repetir no crea otro origen ni duplica; sin regla, las de Vrbo se quedan pendientes y las de Airbnb se facturan.
        var otra = await importador.AplicarAsync("T01", reservas, _actor);
        Assert.Empty(otra.OrigenesNuevos);
        Assert.Equal(0, otra.Nuevas);
        var f = await servicio.FacturarAsync("T01", _actor);
        Assert.Single(f.Valor.Facturas);
        Assert.Equal(2, f.Valor.Incidencias.Count);
        Assert.All(f.Valor.Incidencias, i => Assert.Contains("Reglas de venta", i));
    }

    [Fact]
    public async Task UnaReservaYaCargadaDelErpNoSeVuelveAImportar()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var airbnb = await OrigenAsync(servicio, "AIRBNB");
        var idActivo = await ActivoAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO VentasPorFacturar (IdSociedad, IdOrigenVenta, FechaReferencia, Cantidad, Importe, Anulada, FacturadaFuera, Fuente, IdExterno, FechaAlta, Version) VALUES (?, ?, ?, 11, 1391, False, True, 'ERP', '900', ?, 0)",
            "T01", airbnb.IdOrigenVenta, new DateTime(2026, 1, 2), _reloj.Ahora));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO AlojamientosAvaibook (IdSociedad, IdExterno, IdActivo, Activo) VALUES ('T01', '377813', ?, True)", idActivo));
        var r = await new ImportadorAvaibook(Bd, servicio, new SinRed(), reloj: _reloj).AplicarAsync("T01", [Reserva("A1", "Airbnb", new DateTime(2026, 1, 2), 11, 1391m)], _actor);
        Assert.Equal(0, r.Nuevas);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM VentasPorFacturar"));
    }

    private async Task<int> ActivoAsync()
    {
        var tipo = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT MIN(IdTipoActivo) FROM TiposActivo")));
        return await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, AmortizacionAcumulada, DeterioroAcumulado, " +
                "ValorContable, EstadoCalculo, IdCentroCoste, EsAjeno, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", tipo, "PISO1", "Piso sintético", new DateTime(2026, 1, 1), (byte)1, true, 0m, 0m, 0m, 0m, (byte)0, null, false, 0);
            return await s.UltimoIdAsync();
        });
    }

    // ---------- factura nominal al huésped (facturacion/reservas 2026-11-25 14:13) ----------

    private ServicioVentas ServicioConHuespedes() => new(Bd, new ServicioSociedadesAccess(Bd), Facturas(), new CatalogoConceptos(Bd, Cuentas), reloj: _reloj,
        terceros: new RepositorioTercerosAccess(Bd, _reloj), roles: Roles);

    private async Task<int?> IdentidadClienteAsync(int idCliente) => await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT IdIdentidadContable FROM CuentasClientesSociedad WHERE IdCliente = ? AND IdSociedad = 'T01' AND Clase = 'CLIENTE'", f => (int?)f.Entero("IdIdentidadContable"), idCliente));

    [Fact]
    public async Task FacturaNominalAlHuespedDaDeAltaAlClienteConLa430DeLaReglaYEligeCompletaOSimplificada()
    {
        await PrepararAsync();
        await Bd.EjecutarAsync(async s =>
        {
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = 'F2'")) == 0)
                await s.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES ('F2', 'Factura simplificada', False)");
            return 0;
        });
        var servicio = ServicioConHuespedes();
        var airbnb = await OrigenAsync(servicio, "AIRBNB");
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ReglasVenta SET FacturaAlHuesped = True, AlFacturar = 'VALIDAR' WHERE IdOrigenVenta = ?", airbnb.IdOrigenVenta));
        var importador = new ImportadorAvaibook(Bd, servicio, new SinRed(), reloj: _reloj);
        await importador.AplicarAsync("T01", [Reserva("H0", "Airbnb", new DateTime(2026, 1, 2), 2, 100m)], _actor);
        var idActivo = await ActivoAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE AlojamientosAvaibook SET IdActivo = ? WHERE IdExterno = '377813'", idActivo));

        // Dos reservas de la misma huésped (mismo email) y una de otro huésped que pasa de 400 €.
        var reservas = new[]
        {
            Reserva("H1", "Airbnb", new DateTime(2026, 1, 5), 2, 300m) with { TravellerName = "Ana Sintética", TravellerEmail = "ana@ejemplo.test", TravellerPhone = "+34 600 000 001" },
            Reserva("H2", "Airbnb", new DateTime(2026, 1, 12), 3, 350m) with { TravellerName = "Ana Sintética", TravellerEmail = "ana@ejemplo.test" },
            Reserva("H3", "Airbnb", new DateTime(2026, 1, 20), 6, 900m) with { TravellerName = "Luis Sintético", TravellerEmail = "luis@ejemplo.test" },
        };
        var imp = await importador.AplicarAsync("T01", reservas, _actor);
        Assert.Equal(3, imp.Nuevas);
        Assert.Contains(imp.Informacion, i => i.Contains("2 huéspedes"));
        var ventas = (await servicio.ListarAsync("T01")).Where(v => v.IdExterno is "H1" or "H2" or "H3").OrderBy(v => v.IdExterno).ToList();
        Assert.All(ventas, v => Assert.NotNull(v.IdCliente));
        Assert.Equal(ventas[0].IdCliente, ventas[1].IdCliente);
        Assert.NotEqual(ventas[0].IdCliente, ventas[2].IdCliente);
        Assert.Equal("ana@ejemplo.test", ventas[0].EmailTitular);
        // Los huéspedes comparten la 430 del cliente de la regla (la plataforma) y no tienen NIF.
        var identidadPlataforma = await IdentidadClienteAsync(_idPlataforma);
        Assert.Equal(identidadPlataforma, await IdentidadClienteAsync(ventas[0].IdCliente!.Value));
        Assert.Equal(identidadPlataforma, await IdentidadClienteAsync(ventas[2].IdCliente!.Value));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM Terceros WHERE Email IN ('ana@ejemplo.test', 'luis@ejemplo.test') AND NIF IS NULL"));
        Assert.Equal("+34 600 000 001", await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT Telefono FROM Terceros WHERE Email = 'ana@ejemplo.test'")));
        // Repetir la importación no da de alta a nadie más.
        await importador.AplicarAsync("T01", reservas, _actor);
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM Terceros WHERE Email IN ('ana@ejemplo.test', 'luis@ejemplo.test')"));

        // Una factura por reserva: sin NIF y hasta 400 €, simplificada y validada; la de 900 €, completa y en borrador a falta del NIF.
        var r = await servicio.FacturarAsync("T01", _actor, ventas.Select(v => v.IdVentaPorFacturar).ToHashSet());
        Assert.Equal(3, r.Valor.Facturas.Count);
        var tipos = await Bd.EjecutarAsync(s => s.ConsultarAsync("SELECT f.TipoFactura, f.EstadoFactura, f.ImporteTotal FROM Facturas AS f ORDER BY f.ImporteTotal",
            x => (Tipo: x.Texto("TipoFactura")!.Trim(), Estado: x.Entero("EstadoFactura"), Total: x.Decimal("ImporteTotal"))));
        Assert.Equal([("F2", 300m), ("F2", 350m), ("F1", 900m)], tipos.Select(x => (x.Tipo, x.Total)).ToArray());
        Assert.Equal(2, r.Valor.Facturas.Count(x => x.Validada));
        Assert.Contains(r.Valor.Incidencias, i => i.Contains("NIF"));

        // Con el NIF en la ficha del huésped, el borrador se valida como factura completa con su NIF.
        var borrador = r.Valor.Facturas.Single(x => !x.Validada);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Terceros SET NIF = '12345678Z' WHERE Email = 'luis@ejemplo.test'"));
        var leida = await Facturas().ObtenerAsync(borrador.IdFactura);
        var validada = await Facturas().ValidarAsync(borrador.IdFactura, leida!.Version, _actor, aceptarAvisos: true);
        Assert.True(validada.Validada, string.Join(" | ", validada.Incidencias.Select(i => i.Mensaje)));
    }}
