using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros.Roles;

namespace ProConta.Norma43.Tests;

/// <summary>
/// Cambiar la subcuenta del tercero (terceros/roles/cambio-subcuenta) de punta a punta: facturas, asientos de banco de la conciliación
/// e imputaciones N43, contra una copia de pruebas con datos inventados.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class CambioSubcuentaTerceroAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }
    private static readonly RelojFijo Reloj = new(new DateTime(2026, 11, 18, 10, 0, 0));
    private ServicioConciliacionN43 Conciliacion => new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd), Reloj);
    private AsientosBancoN43 Banco => new(Bd, Conciliacion);
    private CambioSubcuentaTercero Cambio => new(Bd, new CatalogoCuentasAccess(Bd), new MotorAsientosAccess(Bd, reloj: Reloj), Banco, Reloj);

    private static Mov M(int mes, int dia, decimal importe, bool cargo, string concepto) => new(new DateTime(2026, mes, dia), importe, cargo, concepto, Comun: cargo ? "03" : "04");

    private async Task<int[]> PrepararAsync(params Mov[] movs)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco ficticio"), ("4300001", "Cliente inventado"), ("4300002", "Cliente inventado (nueva)"),
                     ("4000001", "Proveedor inventado"), ("4000002", "Proveedor inventado (nueva)"), ("4751001", "Retenciones ficticias") })
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "1500", "0012345678", "EUR", "Banco ficticio", true);
            var id = await s.UltimoIdAsync();
            return await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                id, _identidades["5720001"], new DateTime(2000, 1, 1));
        });
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 1000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "extracto.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario"))))
            .ToArray();
    }

    /// <summary>Cliente inventado de T01 con esa cuenta; devuelve su IdCliente.</summary>
    private Task<int> ClienteAsync(string nombre, string cuenta) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", nombre, true, 0);
        var tercero = await s.UltimoIdAsync();
        await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", tercero, tercero, true);
        await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable, Clase) VALUES (?, ?, ?, ?)", tercero, "T01", _identidades[cuenta], "CLIENTE");
        return tercero;
    });

    private Task ImputarAsync(int idLinea, string cuenta, decimal importeFirmado) => Bd.EjecutarAsync(s => s.EjecutarAsync(
        "INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Estado) VALUES (?, ?, ?, ?, ?, ?)",
        idLinea, _identidades[cuenta], "CONTRAPARTIDA", importeFirmado, "MANUAL", "CONFIRMADA"));

    /// <summary>Asiento manual sintético con esos apuntes (cuenta, Debe, Haber); devuelve su Id.</summary>
    private Task<int> ManualAsync(DateTime fecha, params (string Cuenta, decimal Debe, decimal Haber)[] apuntes) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync(
            "INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, TotalDebe, TotalHaber, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            9001, fecha, "Asiento sintético", (byte)1, (byte)1, "prueba", DateTime.Now, apuntes.Sum(a => a.Debe), apuntes.Sum(a => a.Haber), 1);
        var asiento = await s.UltimoIdAsync();
        short orden = 0;
        foreach (var a in apuntes)
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)",
                asiento, ++orden, a.Cuenta, a.Debe == 0m ? null : a.Debe, a.Haber == 0m ? null : a.Haber, (byte)0, false);
        return asiento;
    });

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private Task<IReadOnlyList<string>> CuentasAsync(int idAsiento) =>
        Bd.EjecutarAsync(s => s.ConsultarAsync("SELECT CuentaContable FROM Apuntes WHERE IdAsiento = ? ORDER BY Orden", f => (f.Texto("CuentaContable") ?? "").Trim(), idAsiento));

    private Task<IReadOnlyList<int>> ImputadasAsync(string cuenta) => Bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT IdLineaExtractoBancario FROM ImputacionesLineaExtractoBancario WHERE IdIdentidadContable = ? ORDER BY IdLineaExtractoBancario",
        f => f.Entero("IdLineaExtractoBancario"), _identidades[cuenta]));

    private Task AsignarAsync(int idCliente, string cuenta) => Bd.EjecutarAsync(s => s.EjecutarAsync(
        "UPDATE CuentasClientesSociedad SET IdIdentidadContable = ? WHERE IdCliente = ? AND Clase = 'CLIENTE'", _identidades[cuenta], idCliente));

    [Fact]
    public async Task Subcuenta_solo_suya_pasa_todo_salvo_el_asiento_con_retencion_del_trimestre_cerrado()
    {
        var ids = await PrepararAsync(M(3, 10, 500m, false, "TRANSFERENCIA INQUILINO"), M(11, 3, 300m, false, "TRANSFERENCIA INQUILINO"),
            M(11, 12, 100m, false, "TRANSFERENCIA INQUILINO"));
        var cliente = await ClienteAsync("INQUILINO FICTICIO", "4300001");
        await ImputarAsync(ids[0], "4300001", -500m);
        await ImputarAsync(ids[1], "4300001", -300m);
        await ImputarAsync(ids[2], "4300001", -100m);
        var bancos = (await Conciliacion.ContabilizarLineasAsync("T01", [ids[0], ids[1]], _actor)).Asientos;
        // Un manual del primer trimestre con retención (no se pasa) y otro con la 477 sin retención (sí, es solo cambio de subcuenta 4XX).
        var conRetencion = await ManualAsync(new DateTime(2026, 2, 1), ("4300001", 100m, 0m), ("4751001", 0m, 100m));
        var conIva = await ManualAsync(new DateTime(2026, 2, 2), ("4300001", 121m, 0m), ("4770001", 0m, 21m), ("7050001", 0m, 100m));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = 9001", (byte)4));

        var cambio = new SubcuentaCambiada(ClaseCuentaRol.Cliente, cliente, "T01", _identidades["4300001"], _identidades["4300002"]);
        var p = (await Cambio.ProponerAsync(cambio)).Valor;
        Assert.False(p.Compartida);
        Assert.Equal((3, 3), (p.NumeroApuntes, p.NumeroAsientos));
        Assert.Equal(conRetencion, Assert.Single(p.NoMovibles).IdAsiento);
        Assert.Equal(ids, p.LineasReimputar.OrderBy(i => i));
        Assert.True(p.HayQueDecidir);
        Assert.Equal("Se pasarían a la 4300002: 3 apuntes de 3 asientos de 2026 y 3 movimientos N43 imputados.", p.Resumen);

        await AsignarAsync(cliente, "4300002"); // lo que hace el guardado de la ficha
        var r = await Cambio.EjecutarAsync(p, "INQUILINO FICTICIO", _actor, _ctx);
        Assert.Equal((3, 3, 3), (r.Apuntes, r.Asientos, r.Lineas));
        Assert.Equal(["5720001", "4300002"], await CuentasAsync(bancos[ids[0]]));
        Assert.Equal(["5720001", "4300002"], await CuentasAsync(bancos[ids[1]]));
        Assert.Equal(["4300002", "4770001", "7050001"], await CuentasAsync(conIva));
        Assert.Equal(["4300001", "4751001"], await CuentasAsync(conRetencion));
        Assert.Equal(ids, await ImputadasAsync("4300002"));
        // No quedan como corregidos a mano y cada cambio está en el registro con el motivo.
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE CorregidoAMano = True"));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM RegistroCambiosApuntes WHERE Motivo LIKE 'Cambio de subcuenta del tercero INQUILINO FICTICIO%'"));
        Assert.Contains(r.Textos, t => t.StartsWith("1 asiento sigue en la 4300001", StringComparison.Ordinal));
        // La antigua conserva el asiento con retención, así que no se borra.
        Assert.Contains(r.Textos, t => t.StartsWith("La 4300001 se conserva", StringComparison.Ordinal));
        // La conciliación ve el asiento igual que lo propondría ahora: actualizar textos no pide volver a contabilizar.
        Assert.Equal(0, (await Banco.TerceroCambiadoAsync(cliente, _actor)).NoActualizados);
        Assert.Equal(0m, await SaldoAsync("4300001", excepto: conRetencion));
    }

    private async Task<decimal> SaldoAsync(string cuenta, int excepto) => (await Bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT ImporteDebe, ImporteHaber FROM Apuntes WHERE CuentaContable = ? AND IdAsiento <> ?",
        f => (f.DecimalNulo("ImporteDebe") ?? 0m) - (f.DecimalNulo("ImporteHaber") ?? 0m), cuenta, excepto))).Sum();

    [Fact]
    public async Task Subcuenta_compartida_solo_pasa_lo_de_sus_facturas_y_su_codigo_y_no_se_borra()
    {
        var ids = await PrepararAsync(M(11, 3, 500m, false, "TRANSFERENCIA INQUILINO A"), M(11, 5, 300m, false, "TRANSFERENCIA INQUILINO B"));
        var a = await ClienteAsync("INQUILINO A", "4300001");
        var b = await ClienteAsync("INQUILINO B", "4300001");
        await ImputarAsync(ids[0], "4300001", -500m);
        await ImputarAsync(ids[1], "4300001", -300m);
        var bancos = (await Conciliacion.ContabilizarLineasAsync("T01", ids, _actor)).Asientos;
        // La factura de A está cobrada por la primera línea; un manual lleva el código de A y otro el de B.
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9001, "Facturas 2026", 1, 99999, "S", false, true);
            var serie = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdSerie) FROM SeriesFacturacion WHERE IdSociedadEjercicio = 9001"), CultureInfo.InvariantCulture);
            return await s.EjecutarAsync(
                "INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, IdCliente, NumeroFactura, Nombre, Descripcion, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, " +
                "BaseImponible, ImporteTotal, LineaN43) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                9001, serie, a, "0007", "Inquilino A", "Alquiler ficticio", (byte)1, "F1", new DateTime(2026, 11, 1), new DateTime(2026, 11, 1), 500m, 500m, ids[0]);
        });
        var deA = await ManualAsync(new DateTime(2026, 11, 6), ("4300001", 50m, 0m), ("7050001", 0m, 50m));
        var deB = await ManualAsync(new DateTime(2026, 11, 7), ("4300001", 60m, 0m), ("7050001", 0m, 60m));
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Apuntes SET IdCliente = ? WHERE IdAsiento = ? AND Orden = 1", a, deA);
            return await s.EjecutarAsync("UPDATE Apuntes SET IdCliente = ? WHERE IdAsiento = ? AND Orden = 1", b, deB);
        });

        var p = (await Cambio.ProponerAsync(new SubcuentaCambiada(ClaseCuentaRol.Cliente, a, "T01", _identidades["4300001"], _identidades["4300002"]))).Valor;
        Assert.True(p.Compartida);
        Assert.Equal([ids[0]], p.LineasReimputar);
        Assert.Equal(2, p.NumeroApuntes);

        await AsignarAsync(a, "4300002");
        var r = await Cambio.EjecutarAsync(p, "INQUILINO A", _actor, _ctx);
        Assert.Equal(["5720001", "4300002"], await CuentasAsync(bancos[ids[0]]));
        Assert.Equal(["5720001", "4300001"], await CuentasAsync(bancos[ids[1]]));
        Assert.Equal("4300002", (await CuentasAsync(deA))[0]);
        Assert.Equal("4300001", (await CuentasAsync(deB))[0]);
        Assert.Equal([ids[0]], await ImputadasAsync("4300002"));
        Assert.Equal([ids[1]], await ImputadasAsync("4300001"));
        Assert.Contains("La 4300001 se conserva porque la usa otro tercero o rol.", r.Textos);
    }

    [Fact]
    public async Task Proveedor_sin_movimientos_recientes_pasa_sus_facturas_de_compra_y_la_antigua_se_borra()
    {
        var ids = await PrepararAsync(M(11, 4, 121m, true, "RECIBO PROVEEDOR FICTICIO"));
        var proveedor = await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", "PROVEEDOR FICTICIO", true, 0);
            var tercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Proveedores (IdTercero, Activo) VALUES (?, ?)", tercero, true);
            var id = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CuentasProveedoresSociedad (IdProveedor, IdSociedad, IdIdentidadContable, Clase) VALUES (?, ?, ?, ?)",
                id, "T01", _identidades["4000001"], "PROVEEDOR");
            await s.EjecutarAsync(
                "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, IdProveedor, CuentaContableProveedor, NumeroFactura, Fecha, ImporteTotal, Carpeta, NIF, MD5, " +
                "Pagada, Exportada, Duplicada, InversionSujetoPasivo, LineaN43) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", "2026", 9001, id, "4000001", "FP-12", new DateTime(2026, 11, 2), 121m, "Ficticio", "A00000000", "FP-12", false, false, false, false, ids[0]);
            return id;
        });
        await ImputarAsync(ids[0], "4000001", 121m);
        var banco = (await Conciliacion.ContabilizarLineasAsync("T01", ids, _actor)).Asientos[ids[0]];

        var p = (await Cambio.ProponerAsync(new SubcuentaCambiada(ClaseCuentaRol.Proveedor, proveedor, "T01", _identidades["4000001"], _identidades["4000002"]))).Valor;
        Assert.False(p.Compartida);
        Assert.Equal((1, 1), (p.NumeroApuntes, p.NumeroFacturasCompra));

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CuentasProveedoresSociedad SET IdIdentidadContable = ? WHERE IdProveedor = ?", _identidades["4000002"], proveedor));
        var r = await Cambio.EjecutarAsync(p, "PROVEEDOR FICTICIO", _actor, _ctx);
        Assert.Equal(["4000002", "5720001"], (await CuentasAsync(banco)).Order());
        Assert.Equal("4000002", await Bd.EjecutarAsync(async s => ((string)(await s.EscalarAsync("SELECT CuentaContableProveedor FROM FacturasCompra"))!).Trim()));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM CuentasContables WHERE Codigo = '4000001'"));
        Assert.Contains(r.Textos, t => t.StartsWith("He borrado la 4000001", StringComparison.Ordinal));
    }
}
