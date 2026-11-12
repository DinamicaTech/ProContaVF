using System.IO;
using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Correo;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>Importación, solapes, alta de cuentas y reversión contra una copia de pruebas con datos inventados.</summary>
[Trait("Categoria", "Access")]
public sealed class ImportacionN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly ContextoSociedadEjercicio _otra = new("T02", 9101, 2026);
    private readonly Actor _actor = new("Pruebas");
    private static readonly DateTime Nov = new(2026, 11, 1);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioImportacionN43 Servicio => new(Bd);
    private int _cuenta572;

    private static Mov M(int dia, decimal importe, bool cargo, string concepto) => new(new DateTime(2026, 11, dia), importe, cargo, concepto);

    private static readonly Mov[] Semana1 = [M(2, 120.50m, true, "RECIBO LUZ FICTICIA"), M(3, 2500m, false, "TRANSFERENCIA CLIENTE INVENTADO"), M(4, 3.20m, true, "CAFETERIA"), M(4, 3.20m, true, "CAFETERIA")];
    private static readonly Mov[] Semana2 = [M(9, 60m, true, "CUOTA GIMNASIO FICTICIO"), M(10, 400m, false, "ABONO DEVOLUCION")];

    private static FicheroSintetico Fichero(DateTime desde, DateTime hasta, decimal saldo, params Mov[] movs) =>
        new FicheroSintetico().Cuenta("0049", "1500", "0012345678", desde, hasta, saldo, "EMPRESA FICTICIA", movs);

    private async Task EmpresaAsync(bool conCuentaBancaria = true)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.SociedadAsync(Bd, "T02", "Otra sociedad sintética", "B87654321");
        await DatosSinteticos.EjercicioAsync(Bd, 9101, "T02", 2026);
        var c = await new CatalogoCuentasAccess(Bd).AltaAsync(_ctx, new AltaCuenta("5720001", "Banco ficticio"));
        Assert.True(c.EsCorrecto, c.Error?.ToString());
        _cuenta572 = c.Valor.IdIdentidadContable;
        if (conCuentaBancaria)
            await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "1500", "0012345678", "EUR", "Banco ficticio", true));
    }

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));
    private Task<int> LineasAsync() => ContarAsync("SELECT COUNT(*) FROM LineasExtractoBancario");

    [Fact]
    public async Task Importa_el_lote_completo_y_reimportar_el_mismo_fichero_no_duplica()
    {
        await EmpresaAsync();
        var bytes = Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes();
        var r = await Servicio.ImportarAsync(_ctx, bytes, "extracto-semana1.n43", @"C:\ficticio\extracto-semana1.n43", _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        Assert.NotNull(r.IdLote);
        Assert.Equal(4, await LineasAsync());
        Assert.Equal(4, await ContarAsync("SELECT COUNT(*) FROM ComplementosLineaExtractoBancario"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ExtractosBancarios WHERE IdCuentaBancaria IS NOT NULL AND EstadoCuadre = 'CUADRADO'"));
        Assert.Equal("RECIBO LUZ FICTICIA", await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT ConceptoCompleto FROM LineasExtractoBancario WHERE OrdinalMovimiento = 1")));

        var otra = await Servicio.ImportarAsync(_ctx, bytes, "extracto-semana1.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.YaImportado, otra.Veredicto);
        Assert.Equal(r.IdLote, otra.IdLote);
        Assert.Equal(4, await LineasAsync());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM LotesImportacionBancaria"));
    }

    [Fact]
    public async Task El_mismo_contenido_con_otro_hash_se_da_por_importado()
    {
        await EmpresaAsync();
        Assert.Equal(VeredictoImportacion.Importable, (await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes(), "a.n43", null, _actor)).Veredicto);
        var redescargado = new FicheroSintetico { FinDeLinea = "\n" }.Cuenta("0049", "1500", "0012345678", Nov, new(2026, 11, 7), 1000m, "NOMBRE CAMBIADO", Semana1).Bytes();
        var r = await Servicio.ImportarAsync(_ctx, redescargado, "a-otra-vez.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.YaImportado, r.Veredicto);
        Assert.Equal(4, await LineasAsync());
        // Un fichero diario contenido en lo ya importado tampoco duplica.
        var diario = Fichero(new(2026, 11, 4), new(2026, 11, 4), 3379.50m, Semana1[2], Semana1[3]).Bytes();
        Assert.Equal(VeredictoImportacion.YaImportado, (await Servicio.ImportarAsync(_ctx, diario, "dia4.n43", null, _actor)).Veredicto);
    }

    [Fact]
    public async Task Un_fichero_con_un_rango_mayor_que_incluye_lineas_importadas_se_rechaza_entero()
    {
        await EmpresaAsync();
        await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes(), "semana1.n43", null, _actor);
        var mes = Fichero(Nov, new(2026, 11, 14), 1000m, [.. Semana1, .. Semana2]).Bytes();
        var r = await Servicio.ImportarAsync(_ctx, mes, "mes.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Solape, r.Veredicto);
        var solape = Assert.Single(r.Analisis.Solapes);
        Assert.Equal((4, 2), (solape.Coinciden, solape.Nuevos));
        Assert.Contains("semana1.n43", r.Explicacion);
        Assert.Equal(4, await LineasAsync());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM LotesImportacionBancaria"));
        // El periodo siguiente, sin solape, sí entra.
        Assert.Equal(VeredictoImportacion.Importable, (await Servicio.ImportarAsync(_ctx, Fichero(new(2026, 11, 8), new(2026, 11, 14), 3373.10m, Semana2).Bytes(), "semana2.n43", null, _actor)).Veredicto);
        Assert.Equal(6, await LineasAsync());
    }

    [Fact]
    public async Task Dos_movimientos_identicos_solo_estan_importados_si_en_la_bd_hay_los_dos()
    {
        await EmpresaAsync();
        // En la BD solo hay una cafetería del día 4; el fichero trae dos: la segunda es nueva → solape, no «ya importado».
        await Servicio.ImportarAsync(_ctx, Fichero(new(2026, 11, 4), new(2026, 11, 4), 0m, Semana1[2]).Bytes(), "una.n43", null, _actor);
        var r = await Servicio.ImportarAsync(_ctx, Fichero(new(2026, 11, 4), new(2026, 11, 4), 0m, Semana1[2], Semana1[3]).Bytes(), "dos.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Solape, r.Veredicto);
        Assert.Equal((1, 1), (r.Analisis.Solapes[0].Coinciden, r.Analisis.Solapes[0].Nuevos));
    }

    [Fact]
    public async Task Detecta_lo_migrado_del_erp_aunque_guardara_la_fecha_valor_como_fecha_de_operacion()
    {
        await EmpresaAsync();
        var movs = new[] { new Mov(new(2026, 11, 2), 50m, true, "RECIBO CON VALOR POSTERIOR", Valor: new DateTime(2026, 11, 5)), M(3, 10m, false, "ABONO") };
        var bytes = Fichero(Nov, new(2026, 11, 7), 100m, movs).Bytes();
        Assert.Equal(VeredictoImportacion.Importable, (await Servicio.ImportarAsync(_ctx, bytes, "erp.n43", null, _actor)).Veredicto);
        // Como en la migración del ERP: otro hash (sin el fichero real) y la fecha valor en la fecha de operación.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE LotesImportacionBancaria SET HashSha256Contenido = 'ERP-N43-LOTE-1', VersionEsquema = 'ERP'"));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE LineasExtractoBancario SET FechaOperacion = FechaValor"));
        var r = await Servicio.ImportarAsync(_ctx, bytes, "erp.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.YaImportado, r.Veredicto);
        Assert.Equal(2, await LineasAsync());
    }

    [Fact]
    public async Task Un_fichero_descuadrado_no_escribe_nada()
    {
        await EmpresaAsync();
        var malo = new FicheroSintetico { AjusteSaldoFinal = 1m }.Cuenta("0049", "1500", "0012345678", Nov, new(2026, 11, 7), 1000m, "X", Semana1).Bytes();
        var r = await Servicio.ImportarAsync(_ctx, malo, "malo.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Invalido, r.Veredicto);
        Assert.Contains("saldo final", r.Explicacion);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM LotesImportacionBancaria"));
    }

    [Fact]
    public async Task Una_cuenta_desconocida_se_puede_dar_de_alta_para_seguir()
    {
        await EmpresaAsync(conCuentaBancaria: false);
        var bytes = Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes();
        var analisis = await Servicio.AnalizarAsync(_ctx, bytes);
        Assert.Equal(VeredictoImportacion.CuentasDesconocidas, analisis.Veredicto);
        var d = Assert.Single(analisis.Desconocidas);
        Assert.Equal(("0049 1500 0012345678", "EMPRESA FICTICIA"), (d.Ccc, d.NombreEnFichero));
        Assert.NotNull(d.IbanPropuesto);
        Assert.Equal(VeredictoImportacion.CuentasDesconocidas, (await Servicio.ImportarAsync(_ctx, bytes, "a.n43", null, _actor)).Veredicto);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM CuentasBancarias"));

        var alta = new AltaCuentaBancaria(d.Entidad, d.Oficina, d.Cuenta, d.Divisa, "Banco ficticio nuevo", d.IbanPropuesto, _cuenta572);
        var r = await Servicio.ImportarAsync(_ctx, bytes, "a.n43", null, _actor, [alta]);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CuentasBancarias WHERE IdSociedad = 'T01' AND Nombre = 'Banco ficticio nuevo'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ConfiguracionesContablesCuentaBancaria WHERE IdIdentidadContableBanco = ?", _cuenta572));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ExtractosBancarios WHERE IdCuentaBancaria IS NULL"));
    }

    [Fact]
    public async Task Una_cuenta_de_otra_sociedad_se_rechaza()
    {
        await EmpresaAsync(conCuentaBancaria: true);
        var r = await Servicio.ImportarAsync(_otra, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes(), "a.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.OtraSociedad, r.Veredicto);
        Assert.Contains("T01", r.Explicacion);
        Assert.Equal(0, await LineasAsync());
    }

    [Fact]
    public async Task Revertir_deja_el_lote_auditado_y_reimportarlo_lo_reactiva_sin_duplicar()
    {
        await EmpresaAsync();
        var bytes = Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes();
        var id = (await Servicio.ImportarAsync(_ctx, bytes, "a.n43", null, _actor)).IdLote!.Value;
        Assert.False((await Servicio.RevertirAsync(_ctx, id, _actor, "  ")).EsCorrecto);
        Assert.Equal("N43.OtraSociedad", (await Servicio.RevertirAsync(_otra, id, _actor, "Prueba")).Error!.Codigo);
        var rev = await Servicio.RevertirAsync(_ctx, id, _actor, "Fichero equivocado");
        Assert.True(rev.EsCorrecto, rev.Error?.ToString());
        var lote = await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT Estado, OperadorReversion, MotivoReversion, FechaHoraReversion FROM LotesImportacionBancaria WHERE IdLoteImportacionBancaria = ?",
            f => (f.Texto("Estado"), f.Texto("OperadorReversion"), f.Texto("MotivoReversion"), f.FechaNula("FechaHoraReversion")), id));
        Assert.Equal(("REVERTIDO", "Pruebas", "Fichero equivocado"), (lote.Item1, lote.Item2, lote.Item3));
        Assert.NotNull(lote.Item4);
        Assert.Equal(4, await LineasAsync());
        Assert.Equal("N43.YaRevertido", (await Servicio.RevertirAsync(_ctx, id, _actor, "Otra vez")).Error!.Codigo);

        // Las líneas revertidas no cuentan como importadas: otro fichero con ese contenido entraría, pero el mismo reactiva el lote.
        Assert.Equal(VeredictoImportacion.Reactivable, (await Servicio.AnalizarAsync(_ctx, bytes)).Veredicto);
        var r = await Servicio.ImportarAsync(_ctx, bytes, "a.n43", null, _actor);
        Assert.Equal((VeredictoImportacion.Reactivable, id), (r.Veredicto, r.IdLote!.Value));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM LotesImportacionBancaria WHERE Estado = 'IMPORTADO' AND MotivoReversion IS NULL AND FechaHoraReversion IS NULL"));
        Assert.Equal(4, await LineasAsync());
    }

    [Fact]
    public async Task Revertir_un_lote_con_asiento_o_con_cuenta_asignada_se_bloquea()
    {
        await EmpresaAsync();
        var id = (await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes(), "a.n43", null, _actor)).IdLote!.Value;
        var linea = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT MIN(IdLineaExtractoBancario) FROM LineasExtractoBancario")), CultureInfo.InvariantCulture);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Estado) VALUES (?, ?, ?, ?, ?, ?)",
            linea, _cuenta572, "CONTRAPARTIDA", -120.50m, "MANUAL", "PROPUESTA"));
        var r = await Servicio.RevertirAsync(_ctx, id, _actor, "Prueba");
        Assert.Equal("N43.ConImputaciones", r.Error!.Codigo);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA'"));
        var asiento = await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, TotalDebe, TotalHaber, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                9001, new DateTime(2026, 11, 2), "Recibo ficticio", (byte)1, (byte)1, "pruebas", DateTime.Now, 0m, 0m, 1);
            return await s.UltimoIdAsync();
        });
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO ContabilizacionesLineaExtracto (IdLineaExtractoBancario, IdAsiento, Estado, VersionProceso, FechaHora, Operador) VALUES (?, ?, ?, ?, ?, ?)",
            linea, asiento, "CONTABILIZADA", 1, DateTime.Now, "pruebas"));
        r = await Servicio.RevertirAsync(_ctx, id, _actor, "Prueba");
        Assert.Equal("N43.ConAsientos", r.Error!.Codigo);
        Assert.Contains("asiento", r.Error.TextoOperador());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM LotesImportacionBancaria WHERE Estado = 'IMPORTADO'"));
    }

    [Fact]
    public async Task Bancos_muestra_saldos_movimientos_y_lotes_de_cada_cuenta()
    {
        await EmpresaAsync();
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) SELECT IdCuentaBancaria, ?, ? FROM CuentasBancarias",
            _cuenta572, new DateTime(2000, 1, 1)));
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, TotalDebe, TotalHaber, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                9001, new DateTime(2026, 11, 1), "Saldo ficticio", (byte)1, (byte)1, "pruebas", DateTime.Now, 1000m, 1000m, 1);
            var a = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente) VALUES (?, ?, ?, ?, ?, ?, ?)", a, (short)1, "5720001", 1000m, null, (byte)0, false);
            return 0;
        });
        await Servicio.ImportarAsync(_ctx, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes(), "semana1.n43", null, _actor);
        var id2 = (await Servicio.ImportarAsync(_ctx, Fichero(new(2026, 11, 8), new(2026, 11, 14), 3373.10m, Semana2).Bytes(), "semana2.n43", null, _actor)).IdLote!.Value;

        var consulta = new ConsultaBancos(Bd);
        var c = Assert.Single(await consulta.CuentasAsync(_ctx));
        Assert.Equal((3713.10m, new DateTime(2026, 11, 14), 1000m, 6, new DateTime(2026, 11, 10), 2), (c.SaldoBanco!.Value, c.FechaSaldoBanco!.Value, c.SaldoContable!.Value, c.Movimientos, c.UltimoMovimiento!.Value, c.Lotes));
        Assert.Equal("5720001", c.CodigoContable);
        Assert.Equal(2713.10m, c.Diferencia);

        await Servicio.RevertirAsync(_ctx, id2, _actor, "Prueba");
        c = Assert.Single(await consulta.CuentasAsync(_ctx));
        Assert.Equal((3373.10m, 4, 1), (c.SaldoBanco!.Value, c.Movimientos, c.Lotes));
        var lotes = await consulta.LotesAsync(_ctx, c.IdCuentaBancaria);
        Assert.Equal(["semana2.n43", "semana1.n43"], lotes.Select(l => l.NombreFichero));
        Assert.True(lotes[0].Revertido);
        Assert.Equal((2, "Fichero Norma 43"), (lotes[0].Movimientos, lotes[0].Origen));
        Assert.Contains(await consulta.CuentasContablesBancoAsync(_ctx), x => x.Codigo == "5720001");
    }

    [Fact]
    public async Task Un_extracto_recibido_por_correo_se_importa_y_una_cuenta_desconocida_queda_con_incidencia()
    {
        await EmpresaAsync(conCuentaBancaria: false);
        var carpeta = BdPruebas.CarpetaTemporal();
        var ruta = Path.Combine(carpeta, "extracto.n43");
        await File.WriteAllBytesAsync(ruta, Fichero(Nov, new(2026, 11, 7), 1000m, Semana1).Bytes());
        var procesador = new ProcesadorExtractosCorreo(Servicio);
        var r = await procesador.ProcesarAsync(_ctx, ruta, "extracto.n43", Actor.Proceso("correo-entrante"));
        Assert.Equal(EstadoAdjuntoRecibido.Error, r.Estado);
        Assert.Contains("Bancos", r.Explicacion);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
            "T01", "0049", "1500", "0012345678", "EUR", "Banco ficticio", true));
        r = await procesador.ProcesarAsync(_ctx, ruta, "extracto.n43", Actor.Proceso("correo-entrante"));
        Assert.Equal(EstadoAdjuntoRecibido.Procesado, r.Estado);
        r = await procesador.ProcesarAsync(_ctx, ruta, "extracto.n43", Actor.Proceso("correo-entrante"));
        Assert.Equal(EstadoAdjuntoRecibido.Procesado, r.Estado);
        Assert.Contains("ya está importado", r.Explicacion);
        Assert.Equal(4, await LineasAsync());
    }
}
