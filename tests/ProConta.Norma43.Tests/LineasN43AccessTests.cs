using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Norma43.Tests;

/// <summary>Pantalla de líneas N43 (norma43/pantallas) contra una copia de pruebas con datos inventados: lectura, cuenta a mano, traspaso, reasignación y palabras clave.</summary>
[Trait("Categoria", "Access")]
public sealed class LineasN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ConsultaLineasN43 Consulta => new(Bd);
    private ServicioLineasN43 Servicio => new(Bd);
    private ServicioConciliacionN43 Conciliacion => new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd));

    private static Mov M(int mes, int dia, decimal importe, bool cargo, string concepto, string comun = "02") =>
        new(new DateTime(2026, mes, dia), importe, cargo, concepto, Comun: comun);

    /// <summary>
    /// Sociedad T01 con sus ejercicios 2025 (cerrado) y 2026, cuentas inventadas, dos cuentas bancarias con su 572 y un extracto de cada una;
    /// devuelve los Id de las líneas en orden (primero las del banco uno).
    /// </summary>
    private async Task<int[]> PrepararAsync(Mov[] banco1, Mov[]? banco2 = null, bool puente = true)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2025, cerrado: true);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        var cuentas = new List<(string, string)> { ("5720001", "Banco uno ficticio"), ("5720002", "Banco dos ficticio"), ("6280001", "Suministros ficticios"), ("6290001", "Otros servicios ficticios") };
        if (puente) cuentas.Add((ServicioLineasN43.CuentaPuentePredeterminada, "Traspasos entre bancos"));
        foreach (var (codigo, nombre) in cuentas)
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        async Task BancoAsync(string entidad, string cuenta, string nombre, string contable) => await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", entidad, "1500", cuenta, "EUR", nombre, true);
            var id = await s.UltimoIdAsync();
            return await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                id, _identidades[contable], new DateTime(2000, 1, 1));
        });
        await BancoAsync("0049", "0012345678", "Banco uno ficticio", "5720001");
        await BancoAsync("2100", "0099999999", "Banco dos ficticio", "5720002");
        async Task ImportarAsync(string entidad, string cuenta, Mov[] movs, string fichero)
        {
            var bytes = new FicheroSintetico().Cuenta(entidad, "1500", cuenta, movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 1000m, "EMPRESA FICTICIA", movs).Bytes();
            var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, fichero, null, _actor);
            Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        }
        await ImportarAsync("0049", "0012345678", banco1, "uno.n43");
        if (banco2 is { Length: > 0 }) await ImportarAsync("2100", "0099999999", banco2, "dos.n43");
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT l.IdLineaExtractoBancario FROM LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario " +
            "ORDER BY e.IdExtractoBancario, l.OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario")))).ToArray();
    }

    private Task<int> PalabraAsync(string palabra, string? sentido, string cuenta, string entidad = "0049") => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO PalabrasClaveBancarias (IdSociedad, Entidad, Sentido, PalabraClave, PalabraClave2, IdIdentidadContable, Especial, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            "T01", entidad, sentido, palabra, null, _identidades[cuenta], null, true);
        return await s.UltimoIdAsync();
    });

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private async Task<LineaN43> LineaAsync(int id) => Assert.Single(await Consulta.LineasAsync("T01", [2026], [id]));

    [Fact]
    public async Task La_rejilla_lee_origen_destino_estado_y_tipo_de_cada_linea_y_no_las_de_lotes_revertidos()
    {
        var ids = await PrepararAsync([M(3, 2, 120.50m, true, ".COR RECIBO LUZ FICTICIA", comun: "03"), M(3, 5, 500m, false, "TRANSFERENCIA DE CLIENTE FICTICIO", comun: "02")],
            [M(3, 6, 40m, true, "COMISION MANTENIMIENTO", comun: "17")]);
        await PalabraAsync("RECIBO LUZ", "H", "6280001");
        await new AsignadorCuentasN43(Bd).AsignarAsync(_ctx);

        var ejercicios = await Consulta.EjerciciosAsync("T01");
        Assert.Equal([(2026, false), (2025, true)], ejercicios.Select(e => (e.Ejercicio, e.Cerrado)));
        var lineas = await Consulta.LineasAsync("T01", [2026]);
        Assert.Equal(3, lineas.Count);
        var luz = lineas.Single(l => l.Id == ids[0]);
        Assert.Equal(("5720001", "Banco uno ficticio", "6280001", "Suministros ficticios"), (luz.CuentaOrigen, luz.NombreCuentaOrigen, luz.CuentaDestino, luz.NombreCuentaDestino));
        Assert.Equal(EstadoLineaN43.ConCuenta, luz.Estado);
        Assert.Equal(-120.50m, luz.ImporteFirmado);
        Assert.Equal("RECIBO LUZ", Assert.Single(luz.Destinos).PalabraClave);
        Assert.Equal(EstadoLineaN43.SinCuenta, lineas.Single(l => l.Id == ids[1]).Estado);
        Assert.Equal("5720002", lineas.Single(l => l.Id == ids[2]).CuentaOrigen);
        // El tipo coincide con el que calcula la conciliación.
        var tipos = await Conciliacion.TiposAsync(ids);
        Assert.All(lineas, l => Assert.Equal(tipos[l.Id], l.Tipo));
        Assert.Equal("Recibo", luz.Tipo);
        // Otros ejercicios no traen nada; los lotes revertidos no se ven.
        Assert.Empty(await Consulta.LineasAsync("T01", [2025]));
        var lote2 = await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT IdLoteImportacionBancaria FROM LotesImportacionBancaria WHERE NombreFichero = 'dos.n43'"));
        Assert.True((await new ServicioImportacionN43(Bd).RevertirAsync(_ctx, Convert.ToInt32(lote2, CultureInfo.InvariantCulture), _actor, "prueba")).EsCorrecto);
        Assert.Equal(2, (await Consulta.LineasAsync("T01", [2026])).Count);
    }

    [Fact]
    public async Task Asignar_cuenta_sustituye_la_anterior_sin_borrarla_y_avisa_de_las_que_hay_que_recontabilizar()
    {
        var ids = await PrepararAsync([M(3, 2, 120.50m, true, "RECIBO LUZ FICTICIA", comun: "03"), M(3, 3, 60m, true, "RECIBO AGUA FICTICIA", comun: "03")]);
        var r = await Servicio.AsignarCuentaAsync("T01", ids, _identidades["6280001"], _actor);
        Assert.Equal(ids, r.Hechas);
        Assert.Empty(r.Recontabilizar);
        var c = await Conciliacion.ContabilizarLineasAsync("T01", [ids[0]], _actor);
        Assert.Empty(c.Rechazadas);

        // Cambiar la cuenta de una línea contabilizada: la imputación anterior queda DESCARTADA y hay que regenerar su asiento.
        var r2 = await Servicio.AsignarCuentaAsync("T01", [ids[0]], _identidades["6290001"], _actor);
        Assert.Equal([ids[0]], r2.Recontabilizar);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ? AND Estado = 'DESCARTADA'", ids[0]));
        var l = await LineaAsync(ids[0]);
        Assert.Equal("6290001", l.CuentaDestino);
        Assert.Equal(ServicioLineasN43.OrigenManual, l.Destinos[0].Origen);
        Assert.Equal(120.50m, l.Destinos[0].ImporteFirmado); // cargo: positivo
        var c2 = await Conciliacion.ContabilizarLineasAsync("T01", r2.Recontabilizar, _actor);
        Assert.Equal(c.Asientos[ids[0]], c2.Asientos[ids[0]]); // mismo número
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Apuntes WHERE CuentaContable = '6290001' AND ImporteDebe = 120.5"));

        // Una cuenta que no existe en el ejercicio de la línea se rechaza con su solución.
        var otra = await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, Naturaleza, FechaAlta) VALUES (?, ?, ?)", "T01", (byte)0, new DateTime(2026, 1, 1));
            return await s.UltimoIdAsync();
        });
        var r3 = await Servicio.AsignarCuentaAsync("T01", [ids[1]], otra, _actor);
        Assert.Empty(r3.Hechas);
        Assert.Contains("no existe en el ejercicio 2026", r3.Rechazadas[ids[1]].Mensaje);
        Assert.NotNull(r3.Rechazadas[ids[1]].AccionCorrectora);
    }

    [Fact]
    public async Task El_traspaso_entre_bancos_lleva_las_dos_lineas_a_la_cuenta_puente_y_sus_asientos_se_compensan()
    {
        var ids = await PrepararAsync([M(4, 10, 2500m, true, "TRASPASO A CUENTA PROPIA")], [M(4, 10, 2500m, false, "TRASPASO DE CUENTA PROPIA")]);
        Assert.Equal(2, ids.Length);
        // La misma línea dos veces no es un traspaso.
        var mal = await Servicio.TraspasoAsync("T01", ids[0], ids[0], _actor);
        Assert.Empty(mal.Hechas);

        var r = await Servicio.TraspasoAsync("T01", ids[0], ids[1], _actor);
        Assert.Empty(r.Rechazadas);
        Assert.Equal(2, r.Hechas.Count);
        foreach (var id in ids)
        {
            var l = await LineaAsync(id);
            Assert.Equal(ServicioLineasN43.CuentaPuentePredeterminada, l.CuentaDestino);
            Assert.Equal(ServicioLineasN43.OrigenTraspaso, l.Destinos[0].Origen);
        }
        var c = await Conciliacion.ContabilizarLineasAsync("T01", ids, _actor);
        Assert.Empty(c.Rechazadas);
        // Cada asiento cuadra y la cuenta puente queda a cero entre los dos.
        foreach (var a in c.Asientos.Values)
        {
            var asiento = (await new MotorAsientosAccess(Bd).ObtenerAsync(a))!;
            Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        }
        var saldo = await Bd.EjecutarAsync(async s => Convert.ToDecimal(await s.EscalarAsync(
            "SELECT SUM(ImporteDebe - ImporteHaber) FROM Apuntes WHERE CuentaContable = ?", ServicioLineasN43.CuentaPuentePredeterminada), CultureInfo.InvariantCulture));
        Assert.Equal(0m, saldo);
        // Contabilizadas, ya no admiten otro traspaso.
        Assert.Contains("contabilizada", (await Servicio.TraspasoAsync("T01", ids[0], ids[1], _actor)).Rechazadas[ids[0]].Mensaje);
    }

    [Fact]
    public async Task Sin_cuenta_puente_el_traspaso_no_cambia_nada_y_dice_como_resolverlo()
    {
        var ids = await PrepararAsync([M(4, 10, 100m, true, "TRASPASO")], [M(4, 10, 100m, false, "TRASPASO")], puente: false);
        var r = await Servicio.TraspasoAsync("T01", ids[0], ids[1], _actor);
        Assert.Empty(r.Hechas);
        Assert.Contains(ServicioLineasN43.CuentaPuentePredeterminada, r.Rechazadas[ids[0]].AccionCorrectora);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario"));
    }

    [Fact]
    public async Task Reasignar_vuelve_a_elegir_las_puestas_por_palabra_clave_y_respeta_las_manuales()
    {
        var ids = await PrepararAsync([M(3, 2, 120.50m, true, "RECIBO LUZ FICTICIA", comun: "03"), M(3, 3, 60m, true, "RECIBO LUZ FICTICIA BIS", comun: "03")]);
        var palabra = await PalabraAsync("RECIBO LUZ", "H", "6280001");
        await new AsignadorCuentasN43(Bd).AsignarAsync(_ctx);
        await Servicio.AsignarCuentaAsync("T01", [ids[1]], _identidades["6280001"], _actor); // la segunda, a mano

        // La palabra clave cambia de cuenta: reasignar mueve la primera (era suya) y no la segunda (manual).
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE PalabrasClaveBancarias SET IdIdentidadContable = ? WHERE IdPalabraClaveBancaria = ?", _identidades["6290001"], palabra));
        var r = await new AsignadorCuentasN43(Bd).ReasignarLineasAsync(_ctx, ids);
        Assert.Equal(1, r.Asignadas);
        Assert.Equal("6290001", (await LineaAsync(ids[0])).CuentaDestino);
        Assert.Equal("6280001", (await LineaAsync(ids[1])).CuentaDestino);
        // Repetirlo no cambia nada ni deja imputaciones vivas de más.
        await new AsignadorCuentasN43(Bd).ReasignarLineasAsync(_ctx, ids);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA'", ids[0]));
        // Una línea contabilizada no se reasigna.
        await Conciliacion.ContabilizarLineasAsync("T01", [ids[0]], _actor);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE PalabrasClaveBancarias SET IdIdentidadContable = ? WHERE IdPalabraClaveBancaria = ?", _identidades["6280001"], palabra));
        await new AsignadorCuentasN43(Bd).ReasignarLineasAsync(_ctx, [ids[0]]);
        Assert.Equal("6290001", (await LineaAsync(ids[0])).CuentaDestino);
    }

    [Fact]
    public async Task Las_palabras_clave_se_crean_corrigen_y_borran_desde_la_linea()
    {
        var ids = await PrepararAsync([M(3, 2, 120.50m, true, "RECIBO ENDESA ENERGIA FICTICIA", comun: "03")]);
        var linea = await LineaAsync(ids[0]);
        Assert.Empty(await Servicio.PalabrasQueEncajanAsync("T01", linea));

        var mal = await Servicio.GuardarPalabraAsync("T01", linea, new EdicionPalabraClaveN43(null, "0049", "H", "IBERDROLA", null, _identidades["6280001"], null));
        Assert.False(mal.EsCorrecto);
        var nueva = await Servicio.GuardarPalabraAsync("T01", linea, new EdicionPalabraClaveN43(null, "0049", "H", "ENDESA", null, _identidades["6280001"], null));
        Assert.True(nueva.EsCorrecto, string.Join(" ", nueva.Errores));
        var p = Assert.Single(await Servicio.PalabrasQueEncajanAsync("T01", linea));
        Assert.Equal(("ENDESA", "6280001", 0), (p.PalabraClave, p.Cuenta, p.Usos));

        // Corregirla (segunda palabra y otra cuenta) y aplicarla.
        var corregida = await Servicio.GuardarPalabraAsync("T01", linea, new EdicionPalabraClaveN43(nueva.Valor, "0049", "H", "ENDESA", "ENERGIA", _identidades["6290001"], null));
        Assert.True(corregida.EsCorrecto);
        await new AsignadorCuentasN43(Bd).ReasignarLineasAsync(_ctx, ids);
        Assert.Equal("6290001", (await LineaAsync(ids[0])).CuentaDestino);

        // Ya asignó una línea: borrarla la desactiva y la línea conserva su cuenta.
        var b = await Servicio.BorrarPalabraAsync("T01", nueva.Valor);
        Assert.Contains("desactivada", b.Valor);
        Assert.Empty(await Servicio.PalabrasQueEncajanAsync("T01", linea));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM PalabrasClaveBancarias WHERE Activa = False"));
        Assert.Equal("6290001", (await LineaAsync(ids[0])).CuentaDestino);
        // Una sin usar se borra de verdad.
        var otra = await Servicio.GuardarPalabraAsync("T01", linea, new EdicionPalabraClaveN43(null, null, null, "FICTICIA", null, _identidades["6280001"], null));
        Assert.Contains("borrada", (await Servicio.BorrarPalabraAsync("T01", otra.Valor)).Valor);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM PalabrasClaveBancarias"));
    }

    [Fact]
    public async Task Las_lineas_de_un_ejercicio_cerrado_se_ven_pero_no_se_cambian()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var ctx25 = new ContextoSociedadEjercicio("T01", 9000, 2025);
        var c = await new CatalogoCuentasAccess(Bd).AltaAsync(ctx25, new AltaCuenta("6280001", "Suministros ficticios"));
        Assert.True(c.EsCorrecto, c.Error?.ToString());
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9000"));
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", new DateTime(2025, 12, 1), new DateTime(2025, 12, 1), 1000m, "EMPRESA FICTICIA",
            new Mov(new DateTime(2025, 12, 1), 10m, true, "RECIBO VIEJO")).Bytes();
        var imp = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "viejo.n43", null, _actor,
            [new AltaCuentaBancaria("0049", "1500", "0012345678", "EUR", "Banco uno ficticio", null, null)]);
        Assert.True(imp.Veredicto is VeredictoImportacion.Importable, imp.Explicacion);
        var linea = Assert.Single(await Consulta.LineasAsync("T01", [2025]));
        Assert.True(linea.EjercicioCerrado);
        Assert.NotNull(AccionesLineasN43.Evaluar([linea]).AsignarCuenta);
        var r = await Servicio.AsignarCuentaAsync("T01", [linea.Id], c.Valor.IdIdentidadContable, _actor);
        Assert.Contains("2025 está cerrado", r.Rechazadas[linea.Id].Mensaje);
    }
}
