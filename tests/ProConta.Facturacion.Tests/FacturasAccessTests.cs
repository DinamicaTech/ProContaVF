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
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET DireccionFiscal = ?, CodigoPostalFiscal = ?, PoblacionFiscal = ? WHERE IdSociedad = ?", "Calle Sintética 1", "08001", "Barcelona", "T01"));
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

    private async Task<FacturaVenta> ValidadaAsync(ServicioFacturas servicio)
    {
        var b = await BorradorAsync(servicio);
        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(v.Validada && v.Contabilizada, string.Join(" ", v.Incidencias.Select(i => i.Mensaje)));
        return v.Factura!;
    }

    private Task TrimestreActivoAsync(int trimestre) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = ?", (byte)trimestre, 9001));

    private Task<int> AsientosDe(int idFactura) =>
        Contar("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ? AND ClaveEjecucion LIKE ?", AsientoVenta.MotorPropietario, idFactura + "%");

    [Fact]
    public async Task La_factura_congela_el_nombre_formateado_y_el_pdf_sigue_con_la_razon_social()
    {
        await PrepararAsync();
        var repo = new RepositorioTercerosAccess(Bd, _reloj);
        var t = (await repo.ObtenerAsync(_idTercero))!;
        t.NombreComercial = "Inquilina";
        Assert.True((await repo.GuardarAsync(t, _actor)).EsCorrecto);
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        Assert.Equal("Inquilina", f.Cliente.Nombre);
        Assert.Equal("Inquilina Sintética SL", f.Cliente.NombreVisible);
        var asiento = await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT Descripcion FROM Asientos WHERE IdAsiento = ?", f.IdAsientoContable));
        Assert.Equal("Fra. F20260001 - Inquilina - Alquiler del local, marzo 2026", asiento);
    }

    [Fact]
    public async Task El_iva_va_a_la_477_de_la_actividad_del_centro_y_crea_la_subcuenta()
    {
        await PrepararAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Actividades (IdSociedad, Codigo, Nombre, Deducible, SerieCuentaIva, Activa) VALUES (?, ?, ?, ?, ?, ?)", "T01", "TUR", "Turístico", (byte)1, "4728", true));
        var idActividad = await Contar("SELECT MAX(IdActividad) FROM Actividades");
        var centros = new ProConta.Contabilidad.CentrosCoste.CatalogoCentrosCosteAccess(Bd);
        var padre = (await centros.AltaAsync("T01", "3", "Alquileres", esImputable: false)).Valor.IdCentroCoste;
        var hijo = (await centros.AltaAsync("T01", "318", "Piso 318", padre)).Valor.IdCentroCoste;
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CentrosCoste SET IdActividad = ? WHERE IdCentroCoste = ?", idActividad, padre));

        var servicio = Servicio();
        var b = await BorradorAsync(servicio);
        b.Lineas[0].IdCentroCoste = hijo;
        b.Lineas[0].TipoIva = 21m;
        b.Lineas[0].Recalcular();
        var otra = new LineaFacturaVenta { Nombre = "Sin actividad", PrecioUnitario = 100m, TipoIva = 21m, CuentaContable = "7520001" };
        otra.Recalcular();
        b.AnadirLinea(otra);
        b = (await servicio.GuardarBorradorAsync(b, _actor)).Valor;
        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(v.Validada && v.Contabilizada, string.Join(" ", v.Incidencias.Select(i => i.Mensaje)) + string.Join(" ", (v.ErrorContabilizacion ?? []).Select(e => e.Mensaje)));
        var (cuenta, _) = (await Cuentas.ResolverAsync(_ctx, "4778021"), 0);
        Assert.True(cuenta.EsCorrecto);
        Assert.Equal("IVA repercutido del 21 % de Turístico", cuenta.Valor.Nombre);
        var a = await new MotorAsientosAccess(Bd, reloj: _reloj).ObtenerAsync(v.Factura!.IdAsientoContable!.Value);
        Invariantes.Asiento(a!);
        Assert.Equal(210m, a!.Apuntes.Single(x => x.CodigoCuenta == "4778021").Haber.Valor);
        Assert.Equal(21m, a.Apuntes.Single(x => x.CodigoCuenta == "4770021").Haber.Valor);
    }

    [Fact]
    public async Task Deshacer_en_el_trimestre_activo_borra_el_asiento()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        Assert.Equal(ModoDeshacerVenta.BorrarAsiento, (await servicio.PlanDeshacerAsync(f)).Modo);
        var r = await servicio.DeshacerExportacionAsync(f.IdFactura, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Null((await servicio.ObtenerAsync(f.IdFactura))!.IdAsientoContable);
        Assert.Equal(0, await AsientosDe(f.IdFactura));
        Assert.True((await servicio.ContabilizarAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Equal(1, await AsientosDe(f.IdFactura));
    }

    [Fact]
    public async Task Deshacer_de_un_trimestre_cerrado_hace_contra_asiento_y_conserva_el_original()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        var original = f.IdAsientoContable!.Value;
        await TrimestreActivoAsync(2);
        Assert.Equal(ModoDeshacerVenta.ContraAsiento, (await servicio.PlanDeshacerAsync(f)).Modo);
        Assert.True((await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Null((await servicio.ObtenerAsync(f.IdFactura))!.IdAsientoContable);
        Assert.Equal(2, await AsientosDe(f.IdFactura));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM Asientos WHERE IdAsiento = ?", original));

        // Volver a contabilizar conserva el contra-asiento; deshacer otra vez solo quita el asiento nuevo.
        Assert.True((await servicio.ContabilizarAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Equal(3, await AsientosDe(f.IdFactura));
        Assert.True((await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Equal(2, await AsientosDe(f.IdFactura));
    }

    private sealed record RegistroLibro(int Id, int? IdAsiento, int? IdFactura, byte Trimestre, string Estado, decimal Total, decimal Base, decimal Cuota, string? Descripcion);

    private Task<IReadOnlyList<RegistroLibro>> LibroDe(int idFactura) => Bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT r.IdIVARepercutido, r.IdAsiento, r.IdFactura, p.Numero, r.Estado, r.ImporteTotalFactura, r.BaseImponibleTotal, r.CuotaRepercutidaTotal, r.Descripcion " +
        "FROM IVARepercutido AS r INNER JOIN PeriodosIVA AS p ON p.IdPeriodoIVA = r.IdPeriodoIVA WHERE r.IdFactura = ? ORDER BY r.IdIVARepercutido",
        f => new RegistroLibro(f.Entero("IdIVARepercutido"), f.EnteroNulo("IdAsiento"), f.EnteroNulo("IdFactura"), Convert.ToByte(f.Valor("Numero")), f.Texto("Estado")!,
            f.Decimal("ImporteTotalFactura"), f.Decimal("BaseImponibleTotal"), f.Decimal("CuotaRepercutidaTotal"), f.Texto("Descripcion")), idFactura));

    private Task<IReadOnlyList<(string? Cuenta, string Calificacion, decimal Tipo, decimal Base, decimal Cuota)>> DesgloseDe(int idRegistro) => Bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT c.Codigo, d.CalificacionOperacion, d.TipoIVA, d.BaseImponible, d.CuotaRepercutida FROM DesglosesIVARepercutido AS d " +
        "LEFT JOIN CuentasContables AS c ON d.IdCuentaIVARepercutido = c.IdCuentaContable WHERE d.IdIVARepercutido = ? ORDER BY d.Orden",
        f => (f.Texto("Codigo"), f.Texto("CalificacionOperacion")!, f.Decimal("TipoIVA"), f.Decimal("BaseImponible"), f.Decimal("CuotaRepercutida")), idRegistro));

    /// <summary>Actividad «Locales» (serie 4727) en el centro 3/304, la cuenta del 0 % del ejercicio (4720001 → 4770001) y una factura con tres líneas.</summary>
    private async Task<FacturaVenta> ValidadaConActividadAsync(ServicioFacturas servicio)
    {
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET CuentaTipoIVA0 = ? WHERE IdSociedadEjercicio = ?", "4720001", 9001));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Actividades (IdSociedad, Codigo, Nombre, Deducible, SerieCuentaIva, Activa) VALUES (?, ?, ?, ?, ?, ?)", "T01", "LOC", "Locales", (byte)1, "4727", true));
        var idActividad = await Contar("SELECT MAX(IdActividad) FROM Actividades");
        var centros = new ProConta.Contabilidad.CentrosCoste.CatalogoCentrosCosteAccess(Bd);
        var padre = (await centros.AltaAsync("T01", "3", "Alquileres", esImputable: false)).Valor.IdCentroCoste;
        var local = (await centros.AltaAsync("T01", "304", "Local", padre)).Valor.IdCentroCoste;
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CentrosCoste SET IdActividad = ? WHERE IdCentroCoste = ?", idActividad, local));

        var b = await BorradorAsync(servicio);
        b.Lineas[0].IdCentroCoste = local;
        b.Lineas[0].Recalcular();
        var sinCentro = new LineaFacturaVenta { Nombre = "Sin actividad", PrecioUnitario = 100m, TipoIva = 21m, CuentaContable = "7520001" };
        sinCentro.Recalcular();
        b.AnadirLinea(sinCentro);
        var exenta = new LineaFacturaVenta { Nombre = "Exenta", PrecioUnitario = 400m, TipoIva = 0m, CausaExencion = "E1", IdCentroCoste = local, CuentaContable = "7520001" };
        exenta.Recalcular();
        b.AnadirLinea(exenta);
        b = (await servicio.GuardarBorradorAsync(b, _actor)).Valor;
        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(v.Validada && v.Contabilizada, string.Join(" ", v.Incidencias.Select(i => i.Mensaje)) + string.Join(" ", (v.ErrorContabilizacion ?? []).Select(e => e.Mensaje)));
        return v.Factura!;
    }

    [Fact]
    public async Task El_libro_de_iva_recoge_cada_factura_con_su_desglose_por_477_de_la_actividad_y_las_exentas()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaConActividadAsync(servicio);

        var libro = Assert.Single(await LibroDe(f.IdFactura));
        Assert.Equal((f.IdAsientoContable, (byte)1, "REGISTRADO"), (libro.IdAsiento, libro.Trimestre, libro.Estado));
        Assert.Equal((1500m, 231m, f.Totales.ImporteTotal.Valor), (libro.Base, libro.Cuota, libro.Total));
        var desglose = await DesgloseDe(libro.Id);
        Assert.Equal(3, desglose.Count);
        Assert.Contains(("4777021", "S1", 21m, 1000m, 210m), desglose);
        Assert.Contains(("4770021", "S1", 21m, 100m, 21m), desglose);
        Assert.Contains(("4777001", "E1", 0m, 400m, 0m), desglose);
        Assert.Equal(libro.Base, desglose.Sum(d => d.Base));
        Assert.Equal(libro.Cuota, desglose.Sum(d => d.Cuota));
        // La 477 del 0 % de la actividad se crea, pero no lleva apunte; las demás cuadran con el asiento.
        Assert.True((await Cuentas.ResolverAsync(_ctx, "4777001")).EsCorrecto);
        var a = (await new MotorAsientosAccess(Bd, reloj: _reloj).ObtenerAsync(f.IdAsientoContable!.Value))!;
        Invariantes.Asiento(a);
        Assert.DoesNotContain(a.Apuntes, x => x.CodigoCuenta == "4777001");
        foreach (var d in desglose.Where(d => d.Cuota != 0m))
            Assert.Equal(d.Cuota, a.Apuntes.Where(x => x.CodigoCuenta == d.Cuenta).Sum(x => x.Haber.Valor - x.Debe.Valor));
    }

    [Fact]
    public async Task Regenerar_sustituye_el_registro_y_en_un_trimestre_cerrado_lleva_su_anulacion()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        var primero = Assert.Single(await LibroDe(f.IdFactura));

        // Trimestre activo: regenerar sustituye el registro, sin duplicarlo.
        f.Lineas[0].PrecioUnitario = 2000m;
        f.Lineas[0].Recalcular();
        var g = await servicio.GuardarValidadaAsync(f, _actor);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        var segundo = Assert.Single(await LibroDe(f.IdFactura));
        Assert.NotEqual(primero.Id, segundo.Id);
        Assert.Equal((2000m, 420m, g.Valor.IdAsientoContable), (segundo.Base, segundo.Cuota, segundo.IdAsiento));

        // Trimestre cerrado: el registro del 1T se queda, su anulación y el nuevo van al 2T, con el aviso en el nuevo.
        await TrimestreActivoAsync(2);
        f = g.Valor;
        f.Lineas[0].PrecioUnitario = 3000m;
        f.Lineas[0].Recalcular();
        g = await servicio.GuardarValidadaAsync(f, _actor);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        var libro = await LibroDe(f.IdFactura);
        Assert.Equal(3, libro.Count);
        Assert.Equal((segundo.Id, "REGISTRADO", (byte)1), (libro[0].Id, libro[0].Estado, libro[0].Trimestre));
        Assert.Equal(("ANULACION", (byte)2, -2000m, -420m), (libro[1].Estado, libro[1].Trimestre, libro[1].Base, libro[1].Cuota));
        Assert.Equal(("REGISTRADO", (byte)2, 3000m, g.Valor.IdAsientoContable), (libro[2].Estado, libro[2].Trimestre, libro[2].Base, libro[2].IdAsiento));
        Assert.Contains("1T de 2026", libro[2].Descripcion);
        Assert.Equal(3000m, libro.Sum(r => r.Base));
    }

    [Fact]
    public async Task Deshacer_borra_el_registro_en_el_trimestre_activo_y_lo_anula_en_uno_cerrado()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        Assert.True((await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Empty(await LibroDe(f.IdFactura));

        Assert.True((await servicio.ContabilizarAsync(f.IdFactura, _actor)).EsCorrecto);
        await TrimestreActivoAsync(2);
        Assert.True((await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).EsCorrecto);
        var libro = await LibroDe(f.IdFactura);
        Assert.Equal(new[] { "REGISTRADO", "ANULACION" }, libro.Select(r => r.Estado));
        Assert.Equal(0m, libro.Sum(r => r.Base));
        Assert.Equal(0m, libro.Sum(r => r.Total));

        // Volver a contabilizar y deshacer en el 2T: el registro nuevo se borra y la anulación se queda.
        var avisos = new List<string>();
        Assert.True((await servicio.ContabilizarAsync(f.IdFactura, _actor, avisos.Add)).EsCorrecto);
        Assert.Single(avisos);
        Assert.Equal(3, (await LibroDe(f.IdFactura)).Count);
        Assert.True((await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Equal(new[] { "REGISTRADO", "ANULACION" }, (await LibroDe(f.IdFactura)).Select(r => r.Estado));
    }

    [Fact]
    public async Task El_contra_asiento_de_un_asiento_sin_registro_no_lleva_anulacion()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        // Factura contabilizada antes de que existiera el libro de ventas.
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("DELETE FROM DesglosesIVARepercutido WHERE IdIVARepercutido IN (SELECT IdIVARepercutido FROM IVARepercutido WHERE IdFactura = ?)", f.IdFactura);
            await s.EjecutarAsync("DELETE FROM IVARepercutido WHERE IdFactura = ?", f.IdFactura);
            return 0;
        });
        await TrimestreActivoAsync(2);
        Assert.True((await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).EsCorrecto);
        Assert.Empty(await LibroDe(f.IdFactura));
    }

    [Fact]
    public async Task Una_factura_de_un_año_anterior_a_su_ejercicio_no_se_contabiliza()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        Assert.True((await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).EsCorrecto);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Facturas SET FechaFactura = ? WHERE IdFactura = ?", new DateTime(2019, 5, 3), f.IdFactura));
        var r = await servicio.ContabilizarAsync(f.IdFactura, _actor);
        Assert.False(r.EsCorrecto);
        Assert.Equal("Contabilizacion.EjercicioAnterior", r.Error!.Codigo);
        Assert.Equal(0, await AsientosDe(f.IdFactura));
        Assert.Empty(await LibroDe(f.IdFactura));
    }

    [Fact]
    public async Task Una_factura_enviada_a_VeriFactu_no_se_modifica_ni_se_deshace()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Facturas SET SinVeriFactu = False WHERE IdFactura = ?", f.IdFactura));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO VeriFactuEnvios (IdFactura, TipoRegistro, NumeroIntento, Entorno) VALUES (?, ?, ?, ?)", f.IdFactura, "ALTA", (short)1, "PRUEBAS"));
        f = (await servicio.ObtenerAsync(f.IdFactura))!;
        Assert.False(await servicio.SePuedeModificarAsync(f));
        Assert.Equal("Facturas.EnviadaVeriFactu", (await servicio.DeshacerExportacionAsync(f.IdFactura, _actor)).Error!.Codigo);
        f.Descripcion = "Cambio";
        Assert.Equal("Facturas.NoModificable", (await servicio.GuardarValidadaAsync(f, _actor)).Error!.Codigo);
    }

    [Fact]
    public async Task Modificar_una_validada_regenera_el_asiento_con_el_mismo_numero_y_sin_duplicar()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = await ValidadaAsync(servicio);
        var numero = f.NumeroFactura;
        var asiento = f.IdAsientoContable;

        // Las notas no afectan al asiento.
        f.NotasInternas = "Nota";
        Assert.Equal(ModoDeshacerVenta.NoExportada, (await servicio.PlanGuardarValidadaAsync(f)).Modo);
        var g = await servicio.GuardarValidadaAsync(f, _actor);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal(asiento, g.Valor.IdAsientoContable);

        // Los importes sí: mismo número de factura, importes nuevos y un solo asiento.
        f = g.Valor;
        f.Lineas[0].PrecioUnitario = 2000m;
        f.Lineas[0].Recalcular();
        Assert.Equal(ModoDeshacerVenta.BorrarAsiento, (await servicio.PlanGuardarValidadaAsync(f)).Modo);
        g = await servicio.GuardarValidadaAsync(f, _actor);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal(numero, g.Valor.NumeroFactura);
        Assert.Equal(EstadoFactura.Validada, g.Valor.Estado);
        Assert.Equal(1, await AsientosDe(f.IdFactura));
        var a = await new MotorAsientosAccess(Bd, reloj: _reloj).ObtenerAsync(g.Valor.IdAsientoContable!.Value);
        Invariantes.Asiento(a!);
        Assert.Equal(g.Valor.Totales.ImporteTotal.Valor, a!.Apuntes.Single(x => x.CodigoCuenta == "4300001").Debe.Valor);

        // En un trimestre cerrado: el original se queda, con su contra-asiento y uno nuevo.
        await TrimestreActivoAsync(2);
        f = g.Valor;
        f.Descripcion = "Otra descripción";
        Assert.Equal(ModoDeshacerVenta.ContraAsiento, (await servicio.PlanGuardarValidadaAsync(f)).Modo);
        g = await servicio.GuardarValidadaAsync(f, _actor);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal(3, await AsientosDe(f.IdFactura));
    }

    [Fact]
    public async Task La_fecha_de_una_validada_debe_seguir_el_orden_de_la_serie()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var primera = await ValidadaAsync(servicio);
        await ValidadaAsync(servicio);
        primera.FechaFactura = primera.FechaFactura!.Value.AddDays(5);
        var g = await servicio.GuardarValidadaAsync(primera, _actor);
        Assert.Equal("Facturas.FechaPosteriorSerie", g.Error!.Codigo);
    }

    [Fact]
    public async Task BorradorNuevoConFechasDeHoyYLineasConFechaCentroYOrden()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var f = (await servicio.NuevoBorradorAsync(_ctx)).Valor;
        var hoy = new DateTime(2026, 3, 10);
        Assert.Equal(hoy, f.FechaOperacion);
        Assert.Equal(hoy, f.FechaInicioValidez);
        Assert.Equal(hoy, f.FechaFinValidez);

        var centro = await new ProConta.Contabilidad.CentrosCoste.CatalogoCentrosCosteAccess(Bd).AltaAsync("T01", "TUR", "Alquiler turístico");
        Assert.True(centro.EsCorrecto, centro.Error?.ToString());
        f.IdCliente = _idCliente;
        f.Descripcion = "Alquileres por meses";
        foreach (var nombre in new[] { "Junio", "Julio", "Agosto" })
        {
            var l = new LineaFacturaVenta { Nombre = nombre, PrecioUnitario = 100m, CuentaContable = "7520001" };
            l.Recalcular();
            f.AnadirLinea(l);
        }
        f.Lineas[2].Fecha = new DateTime(2026, 8, 1);
        f.Lineas[2].IdCentroCoste = centro.Valor.IdCentroCoste;
        Assert.True(f.MoverLinea(f.Lineas[2], -2));
        var r = await servicio.GuardarBorradorAsync(f, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());

        var leida = (await servicio.ObtenerAsync(r.Valor.IdFactura))!;
        Assert.Equal(["Agosto", "Junio", "Julio"], leida.Lineas.Select(l => l.Nombre));
        Assert.Equal([1, 2, 3], leida.Lineas.Select(l => l.NumeroLinea));
        Assert.Equal(new DateTime(2026, 8, 1), leida.Lineas[0].Fecha);
        Assert.Equal(centro.Valor.IdCentroCoste, leida.Lineas[0].IdCentroCoste);
        Assert.Null(leida.Lineas[1].Fecha);
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

        Assert.Equal("Facturas.SinMotivo", (await servicio.CrearRectificativaAsync(original.IdFactura, "  ", _ctx, _actor)).Error!.Codigo);
        var r = await servicio.CrearRectificativaAsync(original.IdFactura, "Error en el importe de la renta", _ctx, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal("R4", r.Valor.TipoFactura);
        Assert.Equal("Error en el importe de la renta", r.Valor.MotivoRectificacion);
        Assert.Equal(-original.Totales.ImporteTotal.Valor, r.Valor.Totales.ImporteTotal.Valor);
        Assert.Equal(EstadoFactura.RectificacionPendiente, (await servicio.ObtenerAsync(original.IdFactura))!.Estado);
        Assert.Equal("Facturas.NoRectificable", (await servicio.CrearRectificativaAsync(original.IdFactura, "Otra vez", _ctx, _actor)).Error!.Codigo);

        Assert.True((await servicio.EliminarBorradorAsync(r.Valor.IdFactura, r.Valor.Version, _actor)).EsCorrecto);
        Assert.Equal(EstadoFactura.Validada, (await servicio.ObtenerAsync(original.IdFactura))!.Estado);

        var r2 = (await servicio.CrearRectificativaAsync(original.IdFactura, "Error en el importe de la renta", _ctx, _actor)).Valor;
        Assert.Equal("Error en el importe de la renta", r2.MotivoRectificacion);
        Assert.Contains(r2.LineasHistorial, h => h.Contains("Motivo: Error en el importe de la renta"));
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
