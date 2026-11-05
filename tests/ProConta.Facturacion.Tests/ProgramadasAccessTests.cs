using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;
using ProConta.Facturacion.Programadas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Tests;

/// <summary>Programadas y lotes contra una copia de pruebas con datos sintéticos (RF-PRO-01..06).</summary>
[Trait("Categoria", "Access")]
public sealed class ProgramadasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly RelojFijo _reloj = new(new DateTime(2026, 3, 10, 10, 0, 0));
    private int _idCliente;

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

    private ServicioProgramadas Servicio() => new(Bd, new ServicioSociedadesAccess(Bd), Facturas(), reloj: _reloj);

    private async Task<int> Contar(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)));

    private async Task EjercicioAsync(int id, int anio)
    {
        await DatosSinteticos.EjercicioAsync(Bd, id, "T01", anio);
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE SociedadesEjercicios SET Nombre = ?, CIF = ?, TipoIVA1 = ?, CuentaTipoIVA1 = ?, CuentaSuplido = ? WHERE IdSociedadEjercicio = ?",
            $"Sociedad sintética SL ({anio})", "B12345674", 21f, "4770021", "5550001", id));
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
            id, $"Facturas {anio}", 1, 99999, $"F{anio}", false, true));
        var ctx = new ContextoSociedadEjercicio("T01", id, anio);
        foreach (var (codigo, nombre) in new[] { ("4300001", "Cliente sintético"), ("7520001", "Ingresos por arrendamientos"), ("4770021", "IVA repercutido 21") })
            if (!(await Cuentas.ResolverAsync(ctx, codigo)).EsCorrecto)
            {
                // Una cuenta que ya existe en otro ejercicio se da de alta en este con su misma identidad.
                var previa = await Cuentas.ResolverAsync(_ctx, codigo);
                var alta = previa.EsCorrecto ? await Cuentas.AltaEnEjercicioAsync(ctx, previa.Valor.IdIdentidadContable) : await Cuentas.AltaAsync(ctx, new AltaCuenta(codigo, nombre));
                Assert.True(alta.EsCorrecto, alta.Error?.ToString());
            }
    }

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética SL");
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
        await EjercicioAsync(9001, 2026);

        var t = await new RepositorioTercerosAccess(Bd, _reloj).GuardarAsync(new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Inquilina Sintética SL", Nif = "B87654323", Direccion = "Calle Mayor 1", CodigoPostal = "08001", Poblacion = "Barcelona", Pais = "724" }, _actor);
        Assert.True(t.EsCorrecto, t.Error?.ToString());
        _idCliente = (await Roles.AsegurarClienteAsync(t.Valor.IdTercero)).Valor;
        var cuenta = (await Cuentas.ResolverAsync(_ctx, "4300001")).Valor;
        Assert.True((await Roles.AsignarCuentaAsync(ClaseCuentaRol.Cliente, _idCliente, "T01", cuenta.IdIdentidadContable)).EsCorrecto);
    }

    /// <summary>Programada mensual de alquiler (1.000 + IVA 21 %) desde el 1 de enero, emisión el 31 (último día del mes).</summary>
    private async Task<FacturaProgramada> ProgramadaAsync(ServicioProgramadas servicio, AlGenerar alGenerar = AlGenerar.Borrador, int? repeticiones = null)
    {
        var p = (await servicio.NuevaAsync(_ctx)).Valor;
        p.FechaInicio = new DateTime(2026, 1, 1);
        p.DiaEmision = 31;
        p.NumeroRepeticiones = repeticiones;
        p.AlGenerar = alGenerar;
        p.Plantilla.IdCliente = _idCliente;
        p.Plantilla.Descripcion = "Alquiler mensual del local %FECHAINICIO% - %FECHAFIN%";
        p.Plantilla.FechaInicioValidez = new DateTime(2026, 1, 1);
        p.Plantilla.FechaFinValidez = new DateTime(2026, 1, 31);
        var l = new LineaFacturaVenta { Nombre = "Renta", PrecioUnitario = 1000m, CuentaContable = "7520001" };
        l.Recalcular();
        p.Plantilla.AnadirLinea(l);
        var r = await servicio.GuardarAsync(p, _actor);
        Assert.True(r.Guardada, string.Join(" | ", r.Incidencias.Select(i => i.Mensaje)));
        return r.Programada!;
    }

    [Fact]
    public async Task GuardarYLeerConservaPlantillaYProgramacion()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = await ProgramadaAsync(servicio, AlGenerar.Validar, repeticiones: 12);
        var leida = (await servicio.ObtenerAsync(p.IdFacturaProgramada))!;
        Assert.Equal(Periodicidad.Mensual, leida.Periodicidad);
        Assert.Equal((byte)31, leida.DiaEmision);
        Assert.Equal(12, leida.NumeroRepeticiones);
        Assert.Equal(AlGenerar.Validar, leida.AlGenerar);
        Assert.Equal(new DateTime(2026, 1, 31), leida.ProximaFechaEmision);
        Assert.Equal(_idCliente, leida.Plantilla.IdCliente);
        Assert.Single(leida.Plantilla.Lineas);
        Assert.Equal(1210m, leida.Plantilla.Totales.ImporteTotal.Valor);
        Assert.Contains(leida.LineasHistorial, h => h.Contains("Programada creada"));

        leida.Periodicidad = Periodicidad.Cuatrimestral;
        var cambio = await servicio.GuardarAsync(leida, _actor);
        Assert.True(cambio.Guardada, string.Join(" | ", cambio.Incidencias.Select(i => i.Codigo + ": " + i.Mensaje)));
        Assert.Equal(Periodicidad.Cuatrimestral, (await servicio.ObtenerAsync(p.IdFacturaProgramada))!.Periodicidad);
        // La versión leída ya no vale: otro guardado con ella es un conflicto.
        Assert.False((await servicio.GuardarAsync(leida, _actor)).Guardada);
    }

    [Fact]
    public async Task ActivaIncompletaNoSeGuardaPeroPausadaSi()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = (await servicio.NuevaAsync(_ctx)).Valor;
        var r = await servicio.GuardarAsync(p, _actor);
        Assert.False(r.Guardada);
        Assert.Contains(r.Incidencias, i => i.Codigo == "Facturas.SinCliente");
        p.Activa = false;
        Assert.True((await servicio.GuardarAsync(p, _actor)).Guardada);
    }

    [Fact]
    public async Task GenerarCreaUnaFacturaPorVencimientoAvanzaYNoDuplica()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = await ProgramadaAsync(servicio);

        // Hoy es 10/03/2026: vencen el 31/01 y el 28/02 (31 = último día del mes).
        var pendientes = await servicio.PendientesAsync("T01", _reloj.Ahora.Date);
        Assert.Equal([new DateTime(2026, 1, 31), new DateTime(2026, 2, 28)], Assert.Single(pendientes).Vencimientos);

        var r = await servicio.GenerarAsync(_ctx, _reloj.Ahora.Date, [p.IdFacturaProgramada], _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.True(r.Valor.SinIncidencias, string.Join(" | ", r.Valor.Incidencias));
        Assert.Equal(2, r.Valor.Facturas.Count);

        var facturas = await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdFactura, FechaProgramadaOrigen, IdLoteFacturaProgramada FROM Facturas WHERE IdFacturaProgramada = ? ORDER BY FechaProgramadaOrigen",
            f => (Id: f.Entero("IdFactura"), Origen: f.Fecha("FechaProgramadaOrigen"), Lote: f.Entero("IdLoteFacturaProgramada")), p.IdFacturaProgramada));
        Assert.Equal([new DateTime(2026, 1, 31), new DateTime(2026, 2, 28)], facturas.Select(f => f.Origen));
        Assert.All(facturas, f => Assert.Equal(r.Valor.IdLote, f.Lote));

        var facturas2 = Facturas();
        var febrero = (await facturas2.ObtenerAsync(facturas[1].Id))!;
        Assert.Equal(EstadoFactura.Borrador, febrero.Estado);
        Assert.Null(febrero.NumeroFactura);
        Assert.Equal(p.IdFacturaProgramada, febrero.IdFacturaProgramada);
        Assert.Equal(new DateTime(2026, 3, 10), febrero.FechaFactura); // vencida: se expide hoy
        Assert.Equal(new DateTime(2026, 2, 1), febrero.FechaInicioValidez);
        Assert.Equal(new DateTime(2026, 2, 28), febrero.FechaFinValidez);
        Assert.Equal(1210m, febrero.Totales.ImporteTotal.Valor);
        Assert.Equal("Alquiler mensual del local 01/02/2026 - 28/02/2026", febrero.Descripcion);
        Invariantes.Factura(febrero);

        var avanzada = (await servicio.ObtenerAsync(p.IdFacturaProgramada))!;
        Assert.Equal(2, avanzada.FacturasGeneradas);
        Assert.Equal(new DateTime(2026, 3, 31), avanzada.ProximaFechaEmision);
        Assert.Equal("COMPLETADO", await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT Estado FROM LotesFacturasProgramadas WHERE IdLoteFacturaProgramada = ?", r.Valor.IdLote)));

        // Repetir el lote no crea nada: no le queda ningún vencimiento hasta hoy.
        var otra = await servicio.GenerarAsync(_ctx, _reloj.Ahora.Date, [p.IdFacturaProgramada], _actor);
        Assert.Empty(otra.Valor.Facturas);
        Assert.Single(otra.Valor.Incidencias);
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM Facturas WHERE IdFacturaProgramada = ?", p.IdFacturaProgramada));
        Assert.Empty(await servicio.PendientesAsync("T01", _reloj.Ahora.Date));
    }

    [Fact]
    public async Task GenerarValidandoNumeraContabilizaYRespetaLasRepeticiones()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = await ProgramadaAsync(servicio, AlGenerar.Validar, repeticiones: 1);
        var r = await servicio.GenerarAsync(_ctx, _reloj.Ahora.Date, [p.IdFacturaProgramada], _actor);
        Assert.True(r.Valor.SinIncidencias, string.Join(" | ", r.Valor.Incidencias));
        var generada = Assert.Single(r.Valor.Facturas);
        Assert.True(generada.Validada);
        var f = (await Facturas().ObtenerAsync(generada.IdFactura))!;
        Assert.Equal("F20260001", f.NumeroFactura);
        Assert.Equal(EstadoFactura.Validada, f.Estado);
        Assert.NotNull(f.IdAsientoContable);
        Assert.True((await servicio.ObtenerAsync(p.IdFacturaProgramada))!.Finalizada);
        await Invariantes.EnBaseDeDatosAsync(Bd, Facturas());
    }

    [Fact]
    public async Task DosLotesALaVezNoDuplicanFacturas()
    {
        await PrepararAsync();
        var p = await ProgramadaAsync(Servicio());
        var resultados = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(() => Servicio().GenerarAsync(_ctx, _reloj.Ahora.Date, [p.IdFacturaProgramada], _actor))));
        Assert.All(resultados, r => Assert.True(r.EsCorrecto));
        Assert.Equal(2, resultados.Sum(r => r.Valor.Facturas.Count));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM Facturas WHERE IdFacturaProgramada = ?", p.IdFacturaProgramada));
        Assert.Equal(2, await Contar("SELECT COUNT(*) FROM (SELECT DISTINCT FechaProgramadaOrigen FROM Facturas WHERE IdFacturaProgramada = ?)", p.IdFacturaProgramada));
        Assert.Equal(2, (await Servicio().ObtenerAsync(p.IdFacturaProgramada))!.FacturasGeneradas);
    }

    [Fact]
    public async Task ElVencimientoDeOtroAnioVaASuEjercicioOSeInforma()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = await ProgramadaAsync(servicio);
        _reloj.Ahora = new DateTime(2027, 1, 5, 9, 0, 0);

        // Sin el ejercicio 2027, las de 2026 se generan (con fecha de hoy van a 2027: se informa) y nada queda a medias.
        var r = await servicio.GenerarAsync(_ctx, new DateTime(2026, 12, 31), [p.IdFacturaProgramada], _actor);
        Assert.Empty(r.Valor.Facturas);
        Assert.Contains(r.Valor.Incidencias, i => i.Contains("no tiene el ejercicio 2027"));
        Assert.Equal(0, (await servicio.ObtenerAsync(p.IdFacturaProgramada))!.FacturasGeneradas);

        await EjercicioAsync(9002, 2027);
        r = await Servicio().GenerarAsync(_ctx, new DateTime(2027, 1, 5), [p.IdFacturaProgramada], _actor);
        Assert.True(r.Valor.SinIncidencias, string.Join(" | ", r.Valor.Incidencias));
        Assert.Equal(12, r.Valor.Facturas.Count);
        Assert.Equal(12, await Contar("SELECT COUNT(*) FROM Facturas WHERE IdFacturaProgramada = ? AND IdSociedadEjercicio = 9002", p.IdFacturaProgramada));
        Assert.Equal(12, await Contar("SELECT COUNT(*) FROM Facturas AS f INNER JOIN SeriesFacturacion AS s ON f.IdSerie = s.IdSerie WHERE f.IdFacturaProgramada = ? AND s.Prefijo = 'F2027'", p.IdFacturaProgramada));
    }

    [Fact]
    public async Task PausadaNoGeneraDuplicarQuedaPausadaYEliminarConFacturasNo()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var p = await ProgramadaAsync(servicio);
        var pausa = await servicio.CambiarActivaAsync(p.IdFacturaProgramada, false, p.Version, _actor);
        Assert.True(pausa.Guardada);
        Assert.Equal("Pausada", pausa.Programada!.TextoEstado);
        var r = await servicio.GenerarAsync(_ctx, _reloj.Ahora.Date, [p.IdFacturaProgramada], _actor);
        Assert.Empty(r.Valor.Facturas);
        Assert.Contains(r.Valor.Incidencias, i => i.Contains("pausada"));

        var reanudada = await servicio.CambiarActivaAsync(p.IdFacturaProgramada, true, pausa.Programada.Version, _actor);
        Assert.True(reanudada.Guardada);
        Assert.Equal(2, (await servicio.GenerarAsync(_ctx, _reloj.Ahora.Date, [p.IdFacturaProgramada], _actor)).Valor.Facturas.Count);

        var d = await servicio.DuplicarAsync(p.IdFacturaProgramada, _actor);
        Assert.True(d.Guardada, string.Join(" | ", d.Incidencias.Select(i => i.Mensaje)));
        Assert.False(d.Programada!.Activa);
        Assert.Equal(0, d.Programada.FacturasGeneradas);
        Assert.Single(d.Programada.LineasHistorial);
        Assert.Single(d.Programada.Plantilla.Lineas);

        var actual = (await servicio.ObtenerAsync(p.IdFacturaProgramada))!;
        var e = await servicio.EliminarAsync(p.IdFacturaProgramada, actual.Version);
        Assert.False(e.EsCorrecto);
        Assert.Equal("Programadas.ConFacturas", e.Error!.Codigo);
        Assert.True((await servicio.EliminarAsync(d.Programada.IdFacturaProgramada, d.Programada.Version)).EsCorrecto);
        Assert.Null(await servicio.ObtenerAsync(d.Programada.IdFacturaProgramada));
    }

    [Fact]
    public async Task ProgramarDesdeUnaFacturaCopiaSuContenido()
    {
        await PrepararAsync();
        var facturas = Facturas();
        var b = (await facturas.NuevoBorradorAsync(_ctx)).Valor;
        b.IdCliente = _idCliente;
        b.Descripcion = "Mantenimiento";
        b.TipoRetencion = 19m;
        var l = new LineaFacturaVenta { Nombre = "Cuota", PrecioUnitario = 50m, CuentaContable = "7520001", Fecha = new DateTime(2026, 3, 1) };
        l.Recalcular();
        b.AnadirLinea(l);
        var guardado = (await facturas.GuardarBorradorAsync(b, _actor)).Valor;
        var p = await Servicio().DesdeFacturaAsync(guardado.IdFactura, _ctx);
        Assert.True(p.EsCorrecto);
        Assert.True(p.Valor.EsNueva);
        Assert.Equal(_idCliente, p.Valor.Plantilla.IdCliente);
        Assert.Equal(19m, p.Valor.Plantilla.TipoRetencion);
        Assert.Null(Assert.Single(p.Valor.Plantilla.Lineas).Fecha);
        Assert.True((await Servicio().GuardarAsync(p.Valor, _actor)).Guardada);
    }
}
