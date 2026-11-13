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

namespace ProConta.Norma43.Tests;

/// <summary>Conciliación bancaria (norma43/conciliacion) contra una copia de pruebas con datos inventados.</summary>
[Trait("Categoria", "Access")]
public sealed class ConciliacionN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioConciliacionN43 Servicio => new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd), new RelojFijo(new DateTime(2026, 11, 13, 10, 0, 0)));

    private sealed class RelojFijo(DateTime ahora) : IReloj
    {
        public DateTime Ahora => ahora;
    }

    private static Mov M(int mes, int dia, decimal importe, bool cargo, string concepto) => new(new DateTime(2026, mes, dia), importe, cargo, concepto);

    /// <summary>Sociedad T01 con su ejercicio 2026, unas cuentas, la cuenta bancaria con su 572 y un extracto importado; devuelve los Id de sus líneas en orden.</summary>
    private async Task<int[]> PrepararAsync(params Mov[] movs)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco ficticio"), ("6280001", "Suministros ficticios"), ("6290001", "Otros servicios ficticios"), ("4300001", "Cliente inventado") })
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
        var desde = movs.Min(m => m.Fecha);
        var hasta = movs.Max(m => m.Fecha);
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", desde, hasta, 1000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "extracto.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario"))))
            .ToArray();
    }

    /// <summary>Asigna una cuenta a la línea con la convención de la migración del ERP: positivo en cargos, negativo en abonos.</summary>
    private Task ImputarAsync(int idLinea, string cuenta, decimal importeFirmado, string estado = "CONFIRMADA", int? centro = null) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Estado, IdCentroCoste) VALUES (?, ?, ?, ?, ?, ?, ?)",
            idLinea, _identidades[cuenta], "CONTRAPARTIDA", importeFirmado, "MANUAL", estado, centro));

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private static string Errores(PropuestaLineaN43 p) => string.Join(" | ", p.Errores);

    [Fact]
    public async Task Un_cargo_con_cuenta_genera_el_asiento_del_banco_contra_la_cuenta_y_volver_a_contabilizar_no_duplica()
    {
        var ids = await PrepararAsync(M(11, 2, 120.50m, true, "RECIBO LUZ FICTICIA"));
        await ImputarAsync(ids[0], "6280001", 120.50m);

        var p = Assert.Single(await Servicio.ProponerAsync("T01", ids));
        Assert.True(p.Contabilizable, Errores(p));
        Assert.Equal(new DateTime(2026, 11, 2), p.Asiento!.Fecha);
        Assert.Equal("RECIBO LUZ FICTICIA", p.Asiento.Descripcion);
        Assert.Equal([("5720001", 0m, 120.50m, "Pago a Suministros ficticios"), ("6280001", 120.50m, 0m, "Pago desde Banco ficticio")],
            p.Asiento.Apuntes.Select(a => (a.Cuenta, a.Debe, a.Haber, a.Concepto)));
        // Proponer no graba nada.
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));

        var r = await Servicio.ContabilizarAsync([p], _actor);
        Assert.Empty(r.Rechazadas);
        var idAsiento = r.Asientos[ids[0]];
        var asiento = (await new MotorAsientosAccess(Bd).ObtenerAsync(idAsiento))!;
        Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        Assert.Equal(ServicioConciliacionN43.Lote(ids[0]), asiento.Lote);
        Assert.NotNull(asiento.IdPlantilla);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ContabilizacionesLineaExtracto WHERE IdLineaExtractoBancario = ? AND IdAsiento = ? AND Estado = 'CONTABILIZADA'", ids[0], idAsiento));

        // Con otra cuenta, contabilizar otra vez sustituye el asiento con el mismo número.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET IdIdentidadContable = ?", _identidades["6290001"]));
        var otra = Assert.Single(await Servicio.ProponerAsync("T01", ids));
        Assert.True(otra.Regenera);
        var r2 = await Servicio.ContabilizarAsync([otra], _actor);
        Assert.Empty(r2.Rechazadas);
        Assert.Equal(idAsiento, r2.Asientos[ids[0]]);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ContabilizacionesLineaExtracto"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Apuntes WHERE CuentaContable = '6290001' AND ImporteDebe = 120.5"));
    }

    [Fact]
    public async Task Un_abono_va_al_debe_del_banco_y_un_reparto_agrupa_el_banco_en_un_apunte()
    {
        var ids = await PrepararAsync(M(11, 3, 2500m, false, "TRANSFERENCIA CLIENTE INVENTADO"), M(11, 4, 100m, true, "COMPRA REPARTIDA"));
        await ImputarAsync(ids[0], "4300001", -2500m);
        await ImputarAsync(ids[1], "6280001", 60m);
        await ImputarAsync(ids[1], "6290001", 40m, estado: "PROPUESTA");

        var ps = await Servicio.ProponerAsync("T01", ids);
        Assert.All(ps, p => Assert.True(p.Contabilizable, Errores(p)));
        Assert.Equal([("5720001", 2500m, 0m, "Cobro de Cliente inventado"), ("4300001", 0m, 2500m, "Ingreso en Banco ficticio")],
            ps[0].Asiento!.Apuntes.Select(a => (a.Cuenta, a.Debe, a.Haber, a.Concepto)));
        Assert.Equal([("5720001", 0m, 100m), ("6280001", 60m, 0m), ("6290001", 40m, 0m)], ps[1].Asiento!.Apuntes.Select(a => (a.Cuenta, a.Debe, a.Haber)));

        var r = await Servicio.ContabilizarAsync(ps, _actor);
        Assert.Equal(2, r.Hechas);
        // El banco queda con el saldo del extracto: +2500 − 100.
        Assert.Equal(2400m, await Bd.EjecutarAsync(async s => Convert.ToDecimal(await s.EscalarAsync(
            "SELECT SUM(IIf(ImporteDebe Is Null, 0, ImporteDebe) - IIf(ImporteHaber Is Null, 0, ImporteHaber)) FROM Apuntes WHERE CuentaContable = '5720001'"), CultureInfo.InvariantCulture)));
    }

    [Fact]
    public async Task Sin_cuenta_o_con_un_reparto_que_no_suma_el_movimiento_no_se_contabiliza()
    {
        var ids = await PrepararAsync(M(11, 2, 60m, true, "SIN CUENTA"), M(11, 3, 80m, true, "REPARTO MAL"), M(11, 4, 30m, false, "SIGNO CAMBIADO"));
        await ImputarAsync(ids[1], "6280001", 50m);
        await ImputarAsync(ids[2], "6280001", 30m);
        // Una imputación descartada no cuenta.
        await ImputarAsync(ids[0], "6280001", 60m, estado: "DESCARTADA");

        var ps = await Servicio.ProponerAsync("T01", ids);
        Assert.Equal("Conciliacion.SinCuenta", Assert.Single(ps[0].Errores).Codigo);
        Assert.Equal("Conciliacion.RepartoDescuadrado", Assert.Single(ps[1].Errores).Codigo);
        Assert.Contains("signo", Assert.Single(ps[2].Errores).Causa);
        Assert.All(ps, p => Assert.Null(p.Asiento));

        var r = await Servicio.ContabilizarLineasAsync("T01", ids, _actor);
        Assert.Equal(0, r.Hechas);
        Assert.Equal(3, r.Rechazadas.Count);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Apuntes WHERE CuentaContable LIKE '555%'"));
    }

    [Fact]
    public async Task Un_movimiento_de_un_trimestre_cerrado_conserva_su_fecha_y_se_puede_deshacer()
    {
        var ids = await PrepararAsync(M(3, 10, 45m, true, "CUOTA MARZO"));
        await ImputarAsync(ids[0], "6290001", 45m);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = 9001", (byte)2));

        // Banco contra una cuenta no fiscal: no toca el IVA ni las retenciones del trimestre cerrado.
        var p = Assert.Single(await Servicio.ProponerAsync("T01", ids));
        Assert.True(p.Contabilizable, Errores(p));
        Assert.Equal(new DateTime(2026, 3, 10), p.Asiento!.Fecha);
        Assert.Empty(p.Avisos);
        Assert.Equal(1, (await Servicio.ContabilizarAsync([p], _actor)).Hechas);
        Assert.Equal(1, (await Servicio.DeshacerAsync("T01", ids, _actor, "Probar")).Hechas);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
    }

    [Fact]
    public async Task Un_movimiento_de_un_trimestre_cerrado_con_cuenta_fiscal_va_al_primer_dia_del_trimestre_activo()
    {
        var ids = await PrepararAsync(M(3, 10, 1500m, true, "NOMINA MARZO"));
        var c = await new CatalogoCuentasAccess(Bd).AltaAsync(_ctx, new AltaCuenta("6400000", "Sueldos ficticios"));
        Assert.True(c.EsCorrecto, c.Error?.ToString());
        _identidades["6400000"] = c.Valor.IdIdentidadContable;
        await ImputarAsync(ids[0], "6400000", 1500m);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = 9001", (byte)2));

        var p = Assert.Single(await Servicio.ProponerAsync("T01", ids));
        Assert.True(p.Contabilizable, Errores(p));
        Assert.Equal(new DateTime(2026, 4, 1), p.Asiento!.Fecha);
        Assert.Contains(p.Avisos, a => a.Contains("trimestre cerrado"));
        Assert.Equal(1, (await Servicio.ContabilizarAsync([p], _actor)).Hechas);
    }

    [Fact]
    public async Task Deshacer_borra_el_asiento_y_la_contabilizacion_y_la_linea_vuelve_a_estar_pendiente()
    {
        var ids = await PrepararAsync(M(11, 2, 120.50m, true, "RECIBO LUZ FICTICIA"));
        await ImputarAsync(ids[0], "6280001", 120.50m);
        var hecha = await Servicio.ContabilizarLineasAsync("T01", ids, _actor);
        var idAsiento = hecha.Asientos[ids[0]];

        Assert.Equal("Conciliacion.SinMotivo", (await Servicio.DeshacerAsync("T01", ids, _actor, " ")).Rechazadas[ids[0]][0].Codigo);
        var r = await Servicio.DeshacerAsync("T01", ids, _actor, "La cuenta era otra");
        Assert.Equal(idAsiento, r.Asientos[ids[0]]);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ContabilizacionesLineaExtracto"));
        var p = Assert.Single(await Servicio.ProponerAsync("T01", ids));
        Assert.True(p.Contabilizable && !p.Regenera, Errores(p));
        Assert.Equal("Conciliacion.NoContabilizada", (await Servicio.DeshacerAsync("T01", ids, _actor, "Otra vez")).Rechazadas[ids[0]][0].Codigo);
    }

    [Fact]
    public async Task Una_linea_contabilizada_en_el_erp_no_se_toca()
    {
        var ids = await PrepararAsync(M(11, 2, 120.50m, true, "RECIBO DEL ERP"));
        await ImputarAsync(ids[0], "6280001", 120.50m);
        var erp = new Asiento(_ctx, new DateTime(2026, 11, 2), "Asiento de ContaSol");
        erp.Apuntes.AddRange([Apunte.AlDebe("6280001", 120.50m), Apunte.AlHaber("5720001", 120.50m)]);
        var guardado = await new MotorAsientosAccess(Bd).GuardarManualAsync(erp, _actor);
        Assert.True(guardado.EsCorrecto);
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO ContabilizacionesLineaExtracto (IdLineaExtractoBancario, IdAsiento, Estado, VersionProceso, FechaHora, Operador) VALUES (?, ?, ?, ?, ?, ?)",
            ids[0], guardado.Valor.IdAsiento, "CONTABILIZADA", 1, new DateTime(2026, 11, 3), "ERP"));

        var p = Assert.Single(await Servicio.ProponerAsync("T01", ids));
        Assert.Equal("Conciliacion.ContabilizadaFuera", Assert.Single(p.Errores).Codigo);
        Assert.Equal(guardado.Valor.IdAsiento, p.IdAsientoActual);
        Assert.Equal("Conciliacion.ContabilizadaFuera", (await Servicio.DeshacerAsync("T01", ids, _actor, "Probar")).Rechazadas[ids[0]][0].Codigo);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
    }

    [Fact]
    public async Task Una_propuesta_corregida_se_graba_si_el_banco_sigue_llevando_el_movimiento_entero()
    {
        var ids = await PrepararAsync(M(11, 2, 100m, true, "COMPRA"), M(11, 3, 50m, true, "OTRA"));
        await ImputarAsync(ids[0], "6280001", 100m);
        await ImputarAsync(ids[1], "6280001", 50m);
        var ps = await Servicio.ProponerAsync("T01", ids);

        // Contrapartida cambiada a mano: vale. Importe del banco cambiado: no.
        var cambiada = ps[0] with { Asiento = ps[0].Asiento! with { Apuntes = [ps[0].Asiento!.Apuntes[0], ps[0].Asiento!.Apuntes[1] with { Cuenta = "6290001" }] } };
        var mal = ps[1] with
        {
            Asiento = ps[1].Asiento! with { Apuntes = [ps[1].Asiento!.Apuntes[0] with { Importe = 40m }, ps[1].Asiento!.Apuntes[1] with { Importe = 40m }] },
        };
        var r = await Servicio.ContabilizarAsync([cambiada, mal], _actor);
        Assert.True(r.Asientos.ContainsKey(ids[0]));
        Assert.Equal("Conciliacion.BancoNoCuadra", r.Rechazadas[ids[1]][0].Codigo);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Apuntes WHERE CuentaContable = '6290001'"));
    }

    [Fact]
    public async Task La_migracion_da_de_alta_la_plantilla_de_sistema_igual_que_la_del_codigo()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        var p = await new ServicioPlantillasAsiento(Bd).DeProcesoAsync(ServicioConciliacionN43.ClaveProceso, "T01");
        Assert.NotNull(p);
        var codigo = PlantillaConciliacionN43.Predeterminada;
        Assert.Equal((codigo.Nombre, codigo.Tipo, codigo.Origen, codigo.ReglaFecha, codigo.AgruparPorCuenta, codigo.Clase),
            (p!.Nombre, p.Tipo, p.Origen, p.ReglaFecha, p.AgruparPorCuenta, p.Clase));
        Assert.Equal(codigo.Lineas.Select(l => (l.NumeroAsiento, l.Orden, l.Cuenta, l.Lado, l.Importe, l.Concepto, l.Centro)),
            p.Lineas.Select(l => (l.NumeroAsiento, l.Orden, l.Cuenta, l.Lado, l.Importe, l.Concepto, l.Centro)));
        Assert.True(ServicioPlantillasAsiento.VistaPrevia(p).EsCorrecto, string.Join(" | ", ServicioPlantillasAsiento.VistaPrevia(p).Errores));
        // No se ofrece a mano.
        Assert.DoesNotContain(await new ServicioPlantillasAsiento(Bd).ListarAsync("T01"), x => x.Nombre == codigo.Nombre);
    }
}
