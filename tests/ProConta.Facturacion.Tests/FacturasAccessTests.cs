using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Tests;

/// <summary>Reloj fijo para que fechas y número no dependan del día en que se ejecutan las pruebas.</summary>
internal sealed class RelojFijo(DateTime ahora) : IReloj
{
    public DateTime Ahora { get; set; } = ahora;
}

[Trait("Categoria", "Access")]
public sealed class FacturasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly RelojFijo _reloj = new(new DateTime(2026, 3, 10, 10, 0, 0));
    private int _idCliente;
    private int _idTercero;

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private ServicioRoles Roles => new(Bd, Cuentas);

    private ServicioFacturas Servicio()
    {
        var sociedades = new ServicioSociedadesAccess(Bd);
        var contabilizador = new ContabilizadorVentas(Bd, new MotorAsientosAccess(Bd, reloj: _reloj), Cuentas, Roles, sociedades, _reloj);
        return new ServicioFacturas(Bd, sociedades, new RepositorioTercerosAccess(Bd, _reloj), Roles, Cuentas, contabilizador, reloj: _reloj);
    }

    private async Task<int> Contar(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)));

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética SL");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE SociedadesEjercicios SET Nombre = ?, CIF = ?, TipoIVA1 = ?, CuentaTipoIVA1 = ?, TipoIVA2 = ?, CuentaTipoIVA2 = ?, CuentaSuplido = ? WHERE IdSociedadEjercicio = ?",
            "Sociedad sintética SL (2026)", "B12345674", 21f, "4770021", 10f, "4770010", "5550001", 9001));
        await Bd.EjecutarAsync(async s =>
        {
            foreach (var (tipo, descripcion, rect) in new[] { ("F1", "Factura", false), ("F2", "Simplificada", false), ("R4", "Rectificativa (resto)", true), ("R5", "Rectificativa simplificada", true) })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = ?", tipo)) == 0)
                    await s.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES (?, ?, ?)", tipo, descripcion, rect);
            foreach (var (id, nombre) in new[] { ((byte)0, "Pendiente de validar"), ((byte)1, "Validada"), ((byte)2, "Rectificada"), ((byte)3, "Borrador"), ((byte)4, "Rectificación pendiente") })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM EstadosFactura WHERE IdEstado = ?", id)) == 0)
                    await s.EjecutarAsync("INSERT INTO EstadosFactura (IdEstado, Nombre, EsModificable, EsTerminal) VALUES (?, ?, ?, ?)", id, nombre, id == 3, id == 2);
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Facturas 2026", 1, 99999, "F2026", false, true);
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Rectificativas 2026", 1, 99999, "R2026", true, false);
            return 0;
        });
        foreach (var (codigo, nombre) in new[] { ("4300001", "Cliente sintético"), ("7520001", "Ingresos por arrendamientos"), ("4770021", "IVA repercutido 21"), ("4770010", "IVA repercutido 10"), ("5550001", "Suplidos") })
            Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);

        var t = await new RepositorioTercerosAccess(Bd, _reloj).GuardarAsync(new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Inquilina Sintética SL", Nif = "B87654323", Direccion = "Calle Mayor 1", CodigoPostal = "08001", Poblacion = "Barcelona", Pais = "724" }, _actor);
        Assert.True(t.EsCorrecto, t.Error?.ToString());
        _idTercero = t.Valor.IdTercero;
        _idCliente = (await Roles.AsegurarClienteAsync(_idTercero)).Valor;
        var cuenta = (await Cuentas.ResolverAsync(_ctx, "4300001")).Valor;
        Assert.True((await Roles.AsignarCuentaAsync(ClaseCuentaRol.Cliente, _idCliente, "T01", cuenta.IdIdentidadContable)).EsCorrecto);
    }

    private async Task<FacturaVenta> BorradorAsync(ServicioFacturas servicio, decimal precio = 1000m, decimal retencion = 0m)
    {
        var f = (await servicio.NuevoBorradorAsync(_ctx)).Valor;
        f.IdCliente = _idCliente;
        f.Descripcion = "Alquiler del local, marzo 2026";
        f.TipoRetencion = retencion;
        var l = new LineaFacturaVenta { Nombre = "Renta marzo", PrecioUnitario = precio, CuentaContable = "7520001" };
        l.Recalcular();
        f.AnadirLinea(l);
        var r = await servicio.GuardarBorradorAsync(f, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        return r.Valor;
    }

    [Fact]
    public async Task GuardarNoNumeraYValidarNumeraCongelaYContabiliza()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var b = await BorradorAsync(servicio);
        Assert.Null(b.NumeroFactura);
        Assert.Equal(EstadoFactura.Borrador, b.Estado);
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM SeriesFacturacion WHERE UltimoNumero IS NOT NULL"));

        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(v.Validada, string.Join(" | ", v.Incidencias.Select(i => i.Mensaje)));
        Assert.True(v.Contabilizada, string.Join(" | ", v.ErrorContabilizacion?.Select(e => e.ToString()) ?? []));
        var f = v.Factura!;
        Assert.Equal("F20260001", f.NumeroFactura);
        Assert.Equal(EstadoFactura.Validada, f.Estado);
        Assert.True(f.SinVeriFactu);
        Assert.Equal("B87654323", f.Cliente.Nif);
        Assert.Equal("ES", f.Cliente.Pais);
        // El emisor es el nombre del ejercicio (puede cambiar de un año a otro), no el de la sociedad.
        Assert.Equal("Sociedad sintética SL (2026)", f.NombreEmisor);
        Assert.NotNull(f.IdAsientoContable);

        // Regenerar el asiento no lo duplica.
        Assert.True((await servicio.ContabilizarAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ? AND ClaveEjecucion = ?", AsientoVenta.MotorPropietario, f.IdFactura.ToString()));

        // Una factura numerada no se cambia ni se borra; cambiar el tercero no cambia la factura emitida.
        Assert.Equal("Facturas.NoEsBorrador", (await servicio.GuardarBorradorAsync(f, _actor)).Error!.Codigo);
        Assert.Equal("Facturas.NoEsBorrador", (await servicio.EliminarBorradorAsync(f.IdFactura, f.Version, _actor)).Error!.Codigo);
        var repo = new RepositorioTercerosAccess(Bd, _reloj);
        var t = (await repo.ObtenerAsync(_idTercero))!;
        t.RazonSocial = "Nombre nuevo SL";
        Assert.True((await repo.GuardarAsync(t, _actor)).EsCorrecto);
        Assert.Equal("Inquilina Sintética SL", (await servicio.ObtenerAsync(f.IdFactura))!.Cliente.RazonSocial);

        // La siguiente sigue la serie.
        var b2 = await BorradorAsync(servicio);
        Assert.Equal("F20260002", (await servicio.ValidarAsync(b2.IdFactura, b2.Version, _actor)).Factura!.NumeroFactura);
        await Invariantes.EnBaseDeDatosAsync(Bd, servicio);
    }

    [Fact]
    public async Task ImporteCeroPideConfirmacion()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var b = await BorradorAsync(servicio, precio: 0m);
        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(v.SoloAvisos);
        Assert.Null((await servicio.ObtenerAsync(b.IdFactura))!.NumeroFactura);
        Assert.True((await servicio.ValidarAsync(b.IdFactura, b.Version, _actor, aceptarAvisos: true)).Validada);
        await Invariantes.EnBaseDeDatosAsync(Bd, servicio);
    }

    [Fact]
    public async Task RetencionSinCuentaSeExplicaYSeCreaConElNombreDelCliente()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var b = await BorradorAsync(servicio, retencion: 19m);
        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.False(v.Validada);
        Assert.Contains(v.Incidencias, i => i.Accion == "AsignarCuentaRetencion");
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM SeriesFacturacion WHERE UltimoNumero IS NOT NULL"));

        var codigo = await servicio.ProponerCodigoCuentaAsync(ClaseCuentaRol.Retencion, _ctx);
        Assert.Equal("4730001", codigo);
        var cuenta = await servicio.AsignarCuentaClienteAsync(_idCliente, ClaseCuentaRol.Retencion, _ctx, codigo);
        Assert.True(cuenta.EsCorrecto, cuenta.Error?.ToString());
        Assert.Equal("Retención IRPF Inquilina Sintética SL", cuenta.Valor.Nombre);

        var ok = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(ok.Contabilizada, string.Join(" | ", ok.Incidencias.Select(i => i.Mensaje)));
        Assert.Equal(190m, ok.Factura!.Totales.Retencion.Valor);
        await Invariantes.EnBaseDeDatosAsync(Bd, servicio);
    }

    [Fact]
    public async Task RectificativaCompletaYEliminarSuBorradorRestauraLaOriginal()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var b = await BorradorAsync(servicio);
        var original = (await servicio.ValidarAsync(b.IdFactura, b.Version, _actor)).Factura!;

        var r = await servicio.CrearRectificativaAsync(original.IdFactura, _ctx, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal("R4", r.Valor.TipoFactura);
        Assert.Equal(-original.Totales.ImporteTotal.Valor, r.Valor.Totales.ImporteTotal.Valor);
        Assert.Equal(EstadoFactura.RectificacionPendiente, (await servicio.ObtenerAsync(original.IdFactura))!.Estado);
        Assert.Equal("Facturas.NoRectificable", (await servicio.CrearRectificativaAsync(original.IdFactura, _ctx, _actor)).Error!.Codigo);

        Assert.True((await servicio.EliminarBorradorAsync(r.Valor.IdFactura, r.Valor.Version, _actor)).EsCorrecto);
        Assert.Equal(EstadoFactura.Validada, (await servicio.ObtenerAsync(original.IdFactura))!.Estado);

        var r2 = (await servicio.CrearRectificativaAsync(original.IdFactura, _ctx, _actor)).Valor;
        // En una rectificativa las líneas no se cambian: guardar conserva las de la original en negativo.
        r2.Lineas[0].PrecioUnitario = 1m;
        r2.Lineas[0].Recalcular();
        r2 = (await servicio.GuardarBorradorAsync(r2, _actor)).Valor;
        Assert.Equal(-original.Totales.ImporteTotal.Valor, r2.Totales.ImporteTotal.Valor);

        var v = await servicio.ValidarAsync(r2.IdFactura, r2.Version, _actor);
        Assert.True(v.Contabilizada, string.Join(" | ", v.Incidencias.Select(i => i.Mensaje).Concat(v.ErrorContabilizacion?.Select(e => e.ToString()) ?? [])));
        Assert.Equal("R20260001", v.Factura!.NumeroFactura);
        Assert.Equal(original.Cliente, v.Factura.Cliente);
        Assert.Equal(EstadoFactura.Rectificada, (await servicio.ObtenerAsync(original.IdFactura))!.Estado);
        await Invariantes.EnBaseDeDatosAsync(Bd, servicio);
    }

    [Fact]
    public async Task DuplicarNoConservaNumeroNiEstado()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var b = await BorradorAsync(servicio);
        var f = (await servicio.ValidarAsync(b.IdFactura, b.Version, _actor)).Factura!;
        var d = await servicio.DuplicarAsync(f.IdFactura, _ctx, _actor);
        Assert.True(d.EsCorrecto, d.Error?.ToString());
        Assert.Null(d.Valor.NumeroFactura);
        Assert.Equal(EstadoFactura.Borrador, d.Valor.Estado);
        Assert.Null(d.Valor.IdAsientoContable);
        Assert.True(d.Valor.Cliente.EstaVacia);
        Assert.Equal(f.Totales.ImporteTotal, d.Valor.Totales.ImporteTotal);
        Assert.Contains(d.Valor.LineasHistorial, h => h.Contains("Duplicado de F20260001"));
        await Invariantes.EnBaseDeDatosAsync(Bd, servicio);
    }

    [Fact]
    public async Task ValidacionesALaVezNoRepitenNumero()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var borradores = new List<FacturaVenta>();
        for (var i = 0; i < 4; i++) borradores.Add(await BorradorAsync(servicio));
        var resultados = await Task.WhenAll(borradores.Select(b => Task.Run(() => Servicio().ValidarAsync(b.IdFactura, b.Version, _actor))));
        var numeros = resultados.Where(r => r.Validada).Select(r => r.Factura!.NumeroFactura).ToList();
        Assert.NotEmpty(numeros);
        Assert.Equal(numeros.Count, numeros.Distinct().Count());
        Assert.Equal(numeros.Count, await Contar("SELECT COUNT(*) FROM Facturas WHERE NumeroFactura IS NOT NULL"));
        await Invariantes.EnBaseDeDatosAsync(Bd, servicio);
    }

    [Fact]
    public async Task BorradorConConflictoDeVersion()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var b = await BorradorAsync(servicio);
        var otra = b.Clonar();
        b.Descripcion = "Cambio 1";
        Assert.True((await servicio.GuardarBorradorAsync(b, _actor)).EsCorrecto);
        otra.Descripcion = "Cambio 2";
        Assert.False((await servicio.GuardarBorradorAsync(otra, _actor)).EsCorrecto);
    }
}
