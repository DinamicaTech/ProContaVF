using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>
/// Ventas, compras y terceros actualizan los textos de los asientos de banco enlazados (norma43/conciliacion/actualizar-textos), contra
/// una copia de pruebas con datos inventados.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class AsientosBancoN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioConciliacionN43 Conciliacion => new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd), new RelojFijo(new DateTime(2026, 11, 13, 10, 0, 0)));
    private AsientosBancoN43 Banco => new(Bd, Conciliacion);

    private sealed class RelojFijo(DateTime ahora) : IReloj
    {
        public DateTime Ahora => ahora;
    }

    private static Mov M(int mes, int dia, decimal importe, bool cargo, string concepto, string comun) =>
        new(new DateTime(2026, mes, dia), importe, cargo, concepto, Comun: comun);

    private async Task<int[]> PrepararAsync(params Mov[] movs)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco ficticio"), ("6280001", "Suministros ficticios"), ("4300001", "Cliente inventado") })
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

    /// <summary>Cliente inventado de T01 con la cuenta 4300001; devuelve el Id del tercero.</summary>
    private Task<int> ClienteAsync(string nombre) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", nombre, true, 0);
        var tercero = await s.UltimoIdAsync();
        await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", tercero, tercero, true);
        await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable, Clase) VALUES (?, ?, ?, ?)",
            tercero, "T01", _identidades["4300001"], "CLIENTE");
        return tercero;
    });

    private Task ImputarAsync(int idLinea, string cuenta, decimal importeFirmado) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Estado) VALUES (?, ?, ?, ?, ?, ?)",
            idLinea, _identidades[cuenta], "CONTRAPARTIDA", importeFirmado, "MANUAL", "CONFIRMADA"));

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private async Task<Asiento> AsientoAsync(int id) => (await new MotorAsientosAccess(Bd).ObtenerAsync(id))!;

    /// <summary>Factura de venta S0007 de 500 € cobrada por la línea; devuelve su Id.</summary>
    private Task<int> VentaAsync(int idLinea, string abreviada) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
            9001, "Facturas 2026", 1, 99999, "S", false, true);
        var serie = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdSerie) FROM SeriesFacturacion WHERE IdSociedadEjercicio = 9001"), CultureInfo.InvariantCulture);
        await s.EjecutarAsync(
            "INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, Nombre, Descripcion, DescripcionAbreviada, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, " +
            "BaseImponible, ImporteTotal, LineaN43) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            9001, serie, "0007", "Inquilino ficticio", "Alquiler ficticio", abreviada, (byte)1, "F1", new DateTime(2026, 3, 1), new DateTime(2026, 3, 1), 500m, 500m, idLinea);
        return await s.UltimoIdAsync();
    });

    [Fact]
    public async Task Cambiar_la_descripcion_abreviada_de_la_venta_actualiza_su_cobro_aunque_el_trimestre_este_cerrado_y_sin_cambios_no_graba()
    {
        var ids = await PrepararAsync(M(3, 10, 500m, false, "TRANSFERENCIA INQUILINO FICTICIO", "04"));
        await ClienteAsync("INQUILINO FICTICIO");
        await ImputarAsync(ids[0], "4300001", -500m);
        var idFactura = await VentaAsync(ids[0], "Alquiler 03/2026");
        var idAsiento = (await Conciliacion.ContabilizarLineasAsync("T01", ids, _actor)).Asientos[ids[0]];
        Assert.All((await AsientoAsync(idAsiento)).Apuntes, a => Assert.Equal("Transferencia de INQUILINO FICTICIO · Alquiler 03/2026", a.Descripcion));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = 9001", (byte)2));

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Facturas SET DescripcionAbreviada = ? WHERE IdFactura = ?", "Alquiler marzo Calle Inventada", idFactura));
        var r = await Banco.FacturaVentaCambiadaAsync(idFactura, _actor);
        Assert.Equal((1, 0), (r.Actualizados, r.NoActualizados));
        Assert.Equal("He actualizado el texto de 1 asiento de banco.", r.Aviso);
        var asiento = await AsientoAsync(idAsiento);
        Assert.Equal(new DateTime(2026, 3, 10), asiento.FechaContable);
        Assert.All(asiento.Apuntes, a => Assert.Equal(("Transferencia de INQUILINO FICTICIO · Alquiler marzo Calle Inventada", "S0007"), (a.Descripcion, a.NumeroDocumento)));
        Assert.Equal([("5720001", 500m, 0m), ("4300001", 0m, 500m)], asiento.Apuntes.Select(a => (a.CodigoCuenta.Trim(), a.Debe.Valor, a.Haber.Valor)));

        // Guardar otra vez sin cambios no graba nada ni dice nada.
        var cambios = await ContarAsync("SELECT COUNT(*) FROM RegistroCambiosApuntes WHERE IdAsiento = ?", idAsiento);
        var otra = await Banco.FacturaVentaCambiadaAsync(idFactura, _actor);
        Assert.Equal((0, 0, (string?)null), (otra.Actualizados, otra.NoActualizados, otra.Aviso));
        Assert.Equal(cambios, await ContarAsync("SELECT COUNT(*) FROM RegistroCambiosApuntes WHERE IdAsiento = ?", idAsiento));
    }

    [Fact]
    public async Task Cambiar_el_numero_de_la_compra_actualiza_el_documento_de_su_pago_y_una_factura_sin_enlace_no_hace_nada()
    {
        var ids = await PrepararAsync(M(11, 4, 121m, true, "RECIBO PROVEEDOR FICTICIO", "03"));
        await ImputarAsync(ids[0], "6280001", 121m);
        var idRegistro = await Bd.EjecutarAsync(async s =>
        {
            foreach (var (numero, linea) in new[] { ("FP-12", (object)ids[0]), ("FP-99", DBNull.Value) })
                await s.EjecutarAsync(
                    "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, CuentaContableProveedor, NumeroFactura, Fecha, ImporteTotal, Carpeta, NIF, MD5, Pagada, Exportada, " +
                    "Duplicada, InversionSujetoPasivo, LineaN43) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    "T01", "2026", 9001, "4000001", numero, new DateTime(2026, 11, 2), 121m, "Ficticio", "A00000000", numero, false, false, false, false, linea);
            return Convert.ToInt32(await s.EscalarAsync("SELECT IdRegistro FROM FacturasCompra WHERE NumeroFactura = 'FP-12'"), CultureInfo.InvariantCulture);
        });
        var idAsiento = (await Conciliacion.ContabilizarLineasAsync("T01", ids, _actor)).Asientos[ids[0]];

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE FacturasCompra SET NumeroFactura = 'FP-13', ConceptoAbreviado = 'Luz noviembre' WHERE IdRegistro = ?", idRegistro));
        var r = await Banco.FacturaCompraCambiadaAsync(idRegistro, _actor);
        Assert.Equal(1, r.Actualizados);
        Assert.All((await AsientoAsync(idAsiento)).Apuntes, a => Assert.Equal(("Recibo de Suministros ficticios · Luz noviembre", "FP-13"), (a.Descripcion, a.NumeroDocumento)));

        var sinEnlace = await Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT IdRegistro FROM FacturasCompra WHERE NumeroFactura = 'FP-99'"), CultureInfo.InvariantCulture));
        Assert.Same(ResultadoAsientosBanco.Nada, await Banco.FacturaCompraCambiadaAsync(sinEnlace, _actor));
    }

    [Fact]
    public async Task Cambiar_el_nombre_del_tercero_actualiza_sus_asientos_de_banco_y_en_un_ejercicio_cerrado_avisa_sin_cambiar_nada()
    {
        var ids = await PrepararAsync(M(11, 3, 500m, false, "TRANSFERENCIA INQUILINO", "04"), M(11, 5, 300m, false, "TRANSFERENCIA INQUILINO", "04"),
            M(11, 6, 40m, true, "RECIBO LUZ", "03"));
        var tercero = await ClienteAsync("INQUILINO FICTICIO");
        await ImputarAsync(ids[0], "4300001", -500m);
        await ImputarAsync(ids[1], "4300001", -300m);
        await ImputarAsync(ids[2], "6280001", 40m);
        var asientos = (await Conciliacion.ContabilizarLineasAsync("T01", ids, _actor)).Asientos;
        // La segunda línea se contabilizó fuera (asiento del ERP): no cuenta.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Asientos SET MotorPropietario = 'ERP' WHERE IdAsiento = ?", asientos[ids[1]]));

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Terceros SET Nombre = 'INQUILINO RENOMBRADO, S.L.' WHERE IdTercero = ?", tercero));
        var r = await Banco.TerceroCambiadoAsync(tercero, _actor);
        Assert.Equal((1, 0), (r.Actualizados, r.NoActualizados));
        Assert.Equal("Transferencia de INQUILINO RENOMBRADO, S.L.", (await AsientoAsync(asientos[ids[0]])).Descripcion);
        Assert.Equal("Transferencia de INQUILINO FICTICIO", (await AsientoAsync(asientos[ids[1]])).Descripcion);
        Assert.Equal("Recibo de Suministros ficticios", (await AsientoAsync(asientos[ids[2]])).Descripcion);

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Terceros SET Nombre = 'INQUILINO OTRA VEZ' WHERE IdTercero = ?", tercero));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9001"));
        var cerrado = await Banco.TerceroCambiadoAsync(tercero, _actor);
        Assert.Equal((0, 1), (cerrado.Actualizados, cerrado.NoActualizados));
        Assert.Equal("1 asiento de banco no se ha actualizado: ejercicio cerrado (1), se queda con el texto anterior.", cerrado.Aviso);
        Assert.Equal("Transferencia de INQUILINO RENOMBRADO, S.L.", (await AsientoAsync(asientos[ids[0]])).Descripcion);
    }
}

/// <summary>Aviso de la actualización de los asientos de banco, sin BD.</summary>
public sealed class AvisoAsientosBancoTests
{
    [Fact]
    public void El_aviso_agrupa_los_rechazos_por_motivo()
    {
        Error E(string codigo, string mensaje) => new(codigo, mensaje, "Movimiento", null, null);
        var motivos = AsientosBancoN43.Resumir([E("Conciliacion.EjercicioCerrado", "a"), E("Conciliacion.EjercicioCerrado", "b"), E("Conciliacion.AsientoDistinto", "c"),
            E("Otro", "El motor dice que no.")]);
        Assert.Equal(["ejercicio cerrado (2), se queda con el texto anterior.",
            "sus cuentas han cambiado (1), vuelve a contabilizar el movimiento desde Líneas N43.", "El motor dice que no."], motivos);
        Assert.Equal("He actualizado el texto de 2 asientos de banco. 4 asientos de banco no se han actualizado: " + string.Join(" ", motivos),
            new ResultadoAsientosBanco(2, 4, motivos).Aviso);
        Assert.Null(ResultadoAsientosBanco.Nada.Aviso);
    }
}
