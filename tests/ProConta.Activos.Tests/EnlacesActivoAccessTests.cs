using System.Globalization;
using ProConta.Activos.Gestion;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Activos.Tests;

/// <summary>
/// Enlaces con compras y facturación contra una copia de pruebas con datos sintéticos (activos 2026-11-17 20:35): relleno inicial del activo de las
/// líneas de venta y de programada, y los apartados «Facturas» y «Ventas» de la ficha, con un centro compartido por dos activos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class EnlacesActivoAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private ServicioActivos Servicio => new(Bd, Cuentas);

    private int _pisoA, _pisoB, _pisoC, _centroA, _centroComun, _serie, _cliente;

    private static int Id(object? v) => Convert.ToInt32(v, CultureInfo.InvariantCulture);

    /// <summary>
    /// Sociedad T01 con 2025 y 2026. Piso A (centro 300) con subcuentas 6250001 (seguro) y 7520001 (ingreso); Piso B y Piso C comparten el centro 321;
    /// Piso B tiene la subcuenta 6250002.
    /// </summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        foreach (var (codigo, nombre) in new[] { ("625", "Seguros"), ("629", "Otros servicios"), ("705", "Prestaciones de servicios"), ("752", "Arrendamientos") })
            Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("6250001", "Seguro Piso A"), ("6250002", "Seguro Piso B"), ("6290000", "Otros servicios"), ("7050000", "Servicios"),
                     ("7520001", "Alquiler Piso A"), ("7520002", "Alquiler Piso B") })
            Assert.True((await Cuentas.AltaSubcuentaAsync(_ctx, codigo, nombre)).EsCorrecto);
        async Task<int> Identidad(string codigo) => (await Cuentas.ResolverAsync(_ctx, codigo)).Valor.IdIdentidadContable;
        var seguroA = await Identidad("6250001");
        var ingresoA = await Identidad("7520001");
        var seguroB = await Identidad("6250002");
        var ingresoB = await Identidad("7520002");

        await Bd.EjecutarAsync(async s =>
        {
            var vivienda = Id(await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = 'VIVIENDA'"));
            async Task<int> Centro(string codigo, string nombre)
            {
                await s.EjecutarAsync("INSERT INTO CentrosCoste (IdSociedad, IdCentroCostePadre, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    "T01", null, codigo, nombre, true, true, (short)1, (byte)1, codigo, codigo);
                return await s.UltimoIdAsync();
            }
            _centroA = await Centro("300", "Piso A");
            _centroComun = await Centro("321", "Montflorit");
            async Task<int> Activo(string codigo, string nombre, int centro)
            {
                await s.EjecutarAsync(
                    "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, AmortizacionAcumulada, DeterioroAcumulado, " +
                    "ValorContable, EstadoCalculo, IdCentroCoste, EsAjeno, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    "T01", vivienda, codigo, nombre, new DateTime(2020, 1, 1), (byte)1, true, 0m, 0m, 0m, 0m, (byte)0, centro, false, 0);
                return await s.UltimoIdAsync();
            }
            _pisoA = await Activo("PisoA", "Piso A", _centroA);
            _pisoB = await Activo("PisoB", "Piso B", _centroComun);
            _pisoC = await Activo("PisoC", "Piso C", _centroComun);
            async Task<int> Linea(string nombre, string rol, string prefijo, short orden)
            {
                await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES (?, ?, ?, ?)", nombre, nombre, rol, true);
                var tipoSub = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO PlantillasSubcuentasActivo (IdTipoActivo, IdTipoSubcuentaActivo, Prefijo, Orden, EstaActiva) VALUES (?, ?, ?, ?, ?)", vivienda, tipoSub, prefijo, orden, true);
                return await s.UltimoIdAsync();
            }
            var seguro = await Linea("Seguro [TOKEN]", RolesSubcuentaActivo.Libre, "6250", 1);
            var ingreso = await Linea("Alquiler [TOKEN]", RolesSubcuentaActivo.IngresoAlquiler, "7520", 2);
            foreach (var (activo, plantilla, identidad) in new[] { (_pisoA, seguro, seguroA), (_pisoA, ingreso, ingresoA), (_pisoB, seguro, seguroB), (_pisoB, ingreso, ingresoB) })
                await s.EjecutarAsync("INSERT INTO SubcuentasActivo (IdActivo, IdPlantillaSubcuentaActivo, IdIdentidadContable, FechaHoraCreacion) VALUES (?, ?, ?, ?)",
                    activo, plantilla, identidad, new DateTime(2026, 1, 1));

            // Facturación: estados, tipo, serie y un cliente.
            foreach (var (estado, nombre) in new[] { ((byte)1, "Validada"), ((byte)3, "Borrador") })
                if (Id(await s.EscalarAsync("SELECT COUNT(*) FROM EstadosFactura WHERE IdEstado = ?", estado)) == 0)
                    await s.EjecutarAsync("INSERT INTO EstadosFactura (IdEstado, Nombre, EsModificable, EsTerminal) VALUES (?, ?, ?, ?)", estado, nombre, estado == 3, false);
            if (Id(await s.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = 'F1'")) == 0)
                await s.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES ('F1', 'Factura', False)");
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Facturas 2026", 1, 99999, "S2026", false, true);
            _serie = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Terceros (TipoPersona, Nombre, RazonSocial, NIF) VALUES (?, ?, ?, ?)", "J", "Inquilina Sintética", "Inquilina Sintética SL", "B87654323");
            var tercero = await s.UltimoIdAsync();
            _cliente = 9100;
            await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", _cliente, tercero, true);
            return 0;
        });
    }

    private Task<int> FacturaAsync(string? numero, byte estado, DateTime fecha, decimal total) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, Descripcion, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, ImporteTotal, IdCliente) " +
                              "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", 9001, _serie, (object?)numero ?? DBNull.Value, "Sintética", estado, "F1", fecha, fecha, total, _cliente);
        return await s.UltimoIdAsync();
    });

    private Task<int> LineaFacturaAsync(int factura, short numero, string cuenta, int? centro, decimal @base) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO LineasFactura (IdFactura, NumeroLinea, Descripcion, BaseImponible, EsSuplido, CuentaContable, IdCentroCoste) VALUES (?, ?, ?, ?, ?, ?, ?)",
            factura, numero, "Alquiler", @base, false, cuenta, centro);
        return await s.UltimoIdAsync();
    });

    private Task<int> ProgramadaAsync(DateTime inicio, DateTime? fin, string cuenta, int? centro, decimal precio) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO FacturasProgramadas (IdSociedadEjercicio, IdCliente, IdSerie, TipoFactura, FechaInicio, FechaFin, ProximaFechaEmision, Activa, FechaAlta, Periodicidad) " +
                              "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", 9001, _cliente, _serie, "F1", inicio, (object?)fin ?? DBNull.Value, new DateTime(2026, 12, 1), true, inicio, "MENSUAL");
        var id = await s.UltimoIdAsync();
        await s.EjecutarAsync("INSERT INTO LineasFacturasProgramadas (IdFacturaProgramada, NumeroLinea, Descripcion, Cantidad, PrecioUnitario, DescuentoPorcentaje, DescuentoImporte, " +
                              "CalificacionOperacion, TipoIVA, CuentaContable, IdCentroCoste, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            id, (short)1, "Renta", 1d, precio, 0d, 0m, "S1", 21d, cuenta, centro, true);
        return id;
    });

    private Task<int?> ActivoDeAsync(string tabla, string clave, int id) =>
        Bd.EjecutarAsync(async s => await s.EscalarAsync($"SELECT IdActivo FROM {tabla} WHERE {clave} = ?", id) is { } v and not DBNull ? (int?)Id(v) : null);

    [Fact]
    public async Task El_relleno_inicial_pone_el_activo_por_subcuenta_o_por_centro_de_un_solo_activo()
    {
        await PrepararAsync();
        var f = await FacturaAsync("S20260001", 1, new DateTime(2026, 3, 1), 3000m);
        var porSubcuenta = await LineaFacturaAsync(f, 1, "7520001", null, 1000m);
        var porCentro = await LineaFacturaAsync(f, 2, "7050000", _centroA, 500m);
        var centroCompartido = await LineaFacturaAsync(f, 3, "7050000", _centroComun, 700m);
        var subcuentaB = await LineaFacturaAsync(f, 4, "7520002", _centroComun, 800m);
        var p = await ProgramadaAsync(new DateTime(2025, 1, 1), null, "7050000", _centroA, 500m);

        var (facturas, programadas) = await Bd.EjecutarAsync(s => RellenoActivoLineas.AplicarAsync(s));
        Assert.Equal((3, 1), (facturas, programadas));
        Assert.Equal(_pisoA, await ActivoDeAsync("LineasFactura", "IdLineaFactura", porSubcuenta));
        Assert.Equal(_pisoA, await ActivoDeAsync("LineasFactura", "IdLineaFactura", porCentro));
        Assert.Null(await ActivoDeAsync("LineasFactura", "IdLineaFactura", centroCompartido));
        Assert.Equal(_pisoB, await ActivoDeAsync("LineasFactura", "IdLineaFactura", subcuentaB));
        Assert.Equal(_pisoA, await ActivoDeAsync("LineasFacturasProgramadas", "IdFacturaProgramada", p));

        // Repetirlo no cambia nada y el paso de la migración se da por hecho.
        Assert.Equal((0, 0), await Bd.EjecutarAsync(s => RellenoActivoLineas.AplicarAsync(s)));
        var paso = MigracionesActivos.LineasVentaActivo.Pasos.Last();
        Assert.True(await Bd.EjecutarAsync(s => paso.YaAplicadoAsync(s, s.LeerEsquema())));
    }

    [Fact]
    public async Task El_apartado_Ventas_lista_programadas_y_facturas_con_la_base_de_las_lineas_del_activo()
    {
        await PrepararAsync();
        var f1 = await FacturaAsync("S20260001", 1, new DateTime(2026, 3, 1), 1815m);
        await LineaFacturaAsync(f1, 1, "7520001", _centroA, 1000m);
        await LineaFacturaAsync(f1, 2, "7050000", _centroA, 500m);
        var f2 = await FacturaAsync(null, 3, new DateTime(2026, 4, 1), 1800m);
        await LineaFacturaAsync(f2, 1, "7050000", _centroComun, 700m);
        await LineaFacturaAsync(f2, 2, "7520002", _centroComun, 800m);
        var pA = await ProgramadaAsync(new DateTime(2025, 1, 1), null, "7520001", _centroA, 950m);
        await ProgramadaAsync(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31), "7520001", _centroA, 900m);
        await Bd.EjecutarAsync(s => RellenoActivoLineas.AplicarAsync(s));

        var a = await Servicio.VentasAsync("T01", _pisoA, 2026);
        var factura = Assert.Single(a.Facturas);
        Assert.Equal((f1, 1500m, 1815m, "Validada", "S20260001"), (factura.IdFactura, factura.BaseActivo, factura.Total, factura.Estado, factura.Numero));
        Assert.Equal("Inquilina Sintética SL", factura.Cliente);
        var programada = Assert.Single(a.Programadas);
        Assert.Equal((pA, 950m, "Mensual", true), (programada.IdFacturaProgramada, programada.Importe, programada.Periodicidad, programada.Activa));
        // Con «Todos» sale también la programada que terminó en 2024.
        Assert.Equal(2, (await Servicio.VentasAsync("T01", _pisoA, null)).Programadas.Count);
        Assert.Empty((await Servicio.VentasAsync("T01", _pisoA, 2025)).Facturas);

        // Piso B: solo su línea del borrador (la del centro compartido no tiene activo).
        var b = Assert.Single((await Servicio.VentasAsync("T01", _pisoB, 2026)).Facturas);
        Assert.Equal((f2, 800m, "Borrador", "(borrador)"), (b.IdFactura, b.BaseActivo, b.Estado, b.Numero));
        Assert.Empty((await Servicio.VentasAsync("T01", _pisoC, 2026)).Facturas);
    }

    [Fact]
    public async Task El_apartado_Facturas_lista_las_compras_por_subcuenta_o_por_centro_si_no_es_compartido()
    {
        await PrepararAsync();
        var ids = await Bd.EjecutarAsync(async s =>
        {
            var r = new Dictionary<string, int>();
            foreach (var (clave, idEj, ejercicio, cuenta, centro, duplicada, exportada) in new (string, int?, string, string, int?, bool, bool)[]
                     {
                         ("SeguroA", 9001, "2026", "6250001", null, false, true),
                         ("CentroA", 9001, "2026", "6290000", _centroA, false, false),
                         ("CentroComun", 9001, "2026", "6290000", _centroComun, false, true),
                         ("SeguroB", 9001, "2026", "6250002", _centroComun, false, true),
                         ("Duplicada", 9001, "2026", "6250001", null, true, false),
                         ("Antigua", null, "2025", "6290000", _centroA, false, true),
                     })
            {
                await s.EjecutarAsync(
                    "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, CuentaContableProveedor, NumeroFactura, Fecha, ImporteBI, ImporteIVA, ImporteTotal, ConceptoGasto, IdCentroCoste, " +
                    "Concepto, Pagada, Exportada, Duplicada, InversionSujetoPasivo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    "T01", ejercicio, (object?)idEj ?? DBNull.Value, "4000001", clave, new DateTime(int.Parse(ejercicio, CultureInfo.InvariantCulture), 5, 1), 100m, 21m, 121m, cuenta,
                    (object?)centro ?? DBNull.Value, clave, false, exportada, duplicada, false);
                r[clave] = await s.UltimoIdAsync();
            }
            return r;
        });

        var a2026 = await Servicio.ComprasAsync("T01", _pisoA, 2026);
        Assert.Equal(new[] { ids["SeguroA"], ids["CentroA"] }.Order(), a2026.Select(c => c.IdRegistro).Order());
        Assert.Equal("Pendiente de contabilizar", a2026.Single(c => c.IdRegistro == ids["CentroA"]).Estado);
        Assert.Equal(242m, a2026.Sum(c => c.Total));
        Assert.Equal([ids["Antigua"]], (await Servicio.ComprasAsync("T01", _pisoA, 2025)).Select(c => c.IdRegistro));
        Assert.Equal(3, (await Servicio.ComprasAsync("T01", _pisoA, null)).Count);
        // Centro compartido por B y C: solo cuenta la subcuenta.
        Assert.Equal([ids["SeguroB"]], (await Servicio.ComprasAsync("T01", _pisoB, 2026)).Select(c => c.IdRegistro));
        Assert.Empty(await Servicio.ComprasAsync("T01", _pisoC, 2026));
    }
}
