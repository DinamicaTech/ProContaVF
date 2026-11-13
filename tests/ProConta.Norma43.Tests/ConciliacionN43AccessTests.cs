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

    private static Mov M(int mes, int dia, decimal importe, bool cargo, string concepto, string comun = "02") =>
        new(new DateTime(2026, mes, dia), importe, cargo, concepto, Comun: comun);

    /// <summary>Cliente inventado de la sociedad T01 vinculado a la cuenta (su nombre va en la descripción del asiento).</summary>
    private Task ClienteAsync(string cuenta, string nombre) => Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", nombre, true, 0);
        var tercero = await s.UltimoIdAsync();
        await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo) VALUES (?, ?, ?)", tercero, tercero, true);
        return await s.EjecutarAsync("INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable, Clase) VALUES (?, ?, ?, ?)",
            tercero, "T01", _identidades[cuenta], "CLIENTE");
    });

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
        var ids = await PrepararAsync(M(11, 2, 120.50m, true, ".COR RECIBO LUZ FICTICIA", comun: "03"));
        await ImputarAsync(ids[0], "6280001", 120.50m);

        var p = Assert.Single(await Servicio.ProponerAsync("T01", ids));
        Assert.True(p.Contabilizable, Errores(p));
        Assert.Equal(new DateTime(2026, 11, 2), p.Asiento!.Fecha);
        // Tipo y descripción corta de ConceptosN43 (03 cargo: recibo); sin tercero, el nombre de la cuenta; los dos apuntes la llevan.
        Assert.Equal("Recibo", p.Tipo);
        Assert.Equal("Recibo de Suministros ficticios", p.Asiento.Descripcion);
        Assert.Equal([("5720001", 0m, 120.50m, "Recibo de Suministros ficticios"), ("6280001", 120.50m, 0m, "Recibo de Suministros ficticios")],
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
        Assert.Equal(".COR RECIBO LUZ FICTICIA", asiento.Notas);
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

    private sealed class FacturasFijas(Dictionary<int, IReadOnlyList<FacturaEnlazadaN43>> facturas) : IFacturasLineaN43
    {
        public Task<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>> FacturasAsync(IReadOnlyCollection<int> idsLineas) =>
            Task.FromResult<IReadOnlyDictionary<int, IReadOnlyList<FacturaEnlazadaN43>>>(facturas);
    }

    private ServicioConciliacionN43 ConFacturas(Dictionary<int, IReadOnlyList<FacturaEnlazadaN43>> facturas) =>
        new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd), new RelojFijo(new DateTime(2026, 11, 13, 10, 0, 0)), new FacturasFijas(facturas));

    [Fact]
    public async Task Actualizar_textos_pone_la_factura_enlazada_en_el_mismo_asiento_aunque_el_trimestre_este_cerrado()
    {
        var ids = await PrepararAsync(M(3, 10, 974m, false, "Recibo SEPA Contrato Alquiler ficticio", comun: "15"));
        await ClienteAsync("4300001", "INQUILINO FICTICIO");
        await ImputarAsync(ids[0], "4300001", -974m);
        var idAsiento = (await Servicio.ContabilizarLineasAsync("T01", ids, _actor)).Asientos[ids[0]];
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = 9001", (byte)2));

        // Se enlaza una factura después: el asiento del trimestre cerrado se actualiza (mismo número, misma fecha), solo en sus textos.
        var servicio = ConFacturas(new() { [ids[0]] = [new FacturaEnlazadaN43("083050", "Alquiler mensual Calle Inventada 03/2026")] });
        var r = await servicio.ActualizarTextosAsync("T01", ids, _actor);
        Assert.Empty(r.Rechazadas);
        Assert.Equal(idAsiento, r.Asientos[ids[0]]);
        var asiento = (await new MotorAsientosAccess(Bd).ObtenerAsync(idAsiento))!;
        Assert.Equal(new DateTime(2026, 3, 10), asiento.FechaContable);
        Assert.Equal("Domiciliación de INQUILINO FICTICIO", asiento.Descripcion);
        Assert.All(asiento.Apuntes, a => Assert.Equal(("Domiciliación de INQUILINO FICTICIO · Alquiler mensual Calle Inventada 03/2026", "083050"),
            (a.Descripcion, a.NumeroDocumento)));
        Assert.True(await ContarAsync("SELECT COUNT(*) FROM RegistroCambiosApuntes WHERE IdAsiento = ? AND Campo = 'NumeroDocumento'", idAsiento) > 0);

        // Si las cuentas ya no son las del asiento, no se tocan los textos: hay que volver a contabilizar.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET IdIdentidadContable = ?", _identidades["6290001"]));
        Assert.Equal("Conciliacion.AsientoDistinto", (await servicio.ActualizarTextosAsync("T01", ids, _actor)).Rechazadas[ids[0]][0].Codigo);

        // En un ejercicio cerrado no se actualiza y se dice.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET IdIdentidadContable = ?", _identidades["4300001"]));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9001"));
        Assert.Equal("Conciliacion.EjercicioCerrado", (await servicio.ActualizarTextosAsync("T01", ids, _actor)).Rechazadas[ids[0]][0].Codigo);
    }

    [Fact]
    public async Task El_tipo_de_cada_movimiento_se_calcula_al_vuelo_para_la_rejilla()
    {
        var ids = await PrepararAsync(M(11, 2, 50m, true, ".COMPRA EN TIENDA FICTICIA TARJ. :*0000", comun: "00"), M(11, 3, 70m, true, "SIN CLASIFICAR", comun: "00"),
            M(11, 4, 650m, true, "TRANSFERENCIA FICTICIA", comun: "04"));
        var c = await new CatalogoCuentasAccess(Bd).AltaAsync(_ctx, new AltaCuenta("4650001", "Remuneración pendiente ficticia"));
        _identidades["4650001"] = c.Valor.IdIdentidadContable;
        await ImputarAsync(ids[2], "4650001", 650m);
        var tipos = await Servicio.TiposAsync(ids);
        Assert.Equal(("Tarjeta", (string?)null, "Nómina"), (tipos[ids[0]], tipos[ids[1]], tipos[ids[2]]));
    }

    [Fact]
    public async Task Un_abono_va_al_debe_del_banco_y_un_reparto_agrupa_el_banco_en_un_apunte()
    {
        var ids = await PrepararAsync(M(11, 3, 2500m, false, "TRANSFERENCIA CLIENTE INVENTADO", comun: "04"), M(11, 4, 100m, true, "COMPRA REPARTIDA", comun: "12"));
        await ClienteAsync("4300001", "CLIENTE INVENTADO DE PRUEBA, S.L.");
        await ImputarAsync(ids[0], "4300001", -2500m);
        await ImputarAsync(ids[1], "6280001", 60m);
        await ImputarAsync(ids[1], "6290001", 40m, estado: "PROPUESTA");

        var ps = await Servicio.ProponerAsync("T01", ids);
        Assert.All(ps, p => Assert.True(p.Contabilizable, Errores(p)));
        // El nombre es el del tercero de la cuenta; un reparto lleva el primero y « y otras».
        Assert.Equal([("5720001", 2500m, 0m, "Transferencia de CLIENTE INVENTADO DE PRUEBA, S.L."), ("4300001", 0m, 2500m, "Transferencia de CLIENTE INVENTADO DE PRUEBA, S.L.")],
            ps[0].Asiento!.Apuntes.Select(a => (a.Cuenta, a.Debe, a.Haber, a.Concepto)));
        Assert.Equal("Pago con tarjeta a Suministros ficticios y otras", ps[1].Asiento!.Descripcion);
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
