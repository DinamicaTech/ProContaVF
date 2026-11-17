using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Impuestos;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using Xunit.Abstractions;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Impuesto sobre Sociedades contra Access (contabilidad/procesos/impuesto-sociedades): lee el resultado (sin la 630), las 473 con sus
/// pagadores, los posibles ajustes y la renta de las actividades bonificadas; graba el asiento del 31/12 como lote regenerable con su
/// cálculo y ajustes; repetir pide motivo y no duplica; deshacer lo borra; un cambio posterior lo deja desactualizado; y no deja confirmar
/// con regularización o con otro asiento de la 630. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ImpuestoSociedadesAccessTests(ITestOutputHelper salida) : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private static readonly DateTime Enero = new(2027, 1, 10);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas() => new(Bd);
    private ServicioImpuestoSociedades Servicio(DateTime hoy) => new(Bd, new MotorAsientosAccess(Bd), Cuentas(), null, new RelojFijo(hoy));

    private static readonly EntradaImpuestoSociedades ConMulta = new([new(false, true, "Multas y sanciones", 100m, "6780000")]);

    /// <summary>
    /// Ingresos 20.000 (alquiler de viviendas, con 1.900 de retención), gastos 4.000 + multa 100, pago a cuenta 500:
    /// resultado 15.900; con la multa, base 16.000 y cuota (25 %) 4.000; retenciones 2.400 → 1.600 a ingresar.
    /// </summary>
    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        foreach (var (codigo, nombre) in new[] { ("630", "Impuesto sobre beneficios"), ("475", "HP acreedora"), ("470", "HP deudora") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("4300001", "Inquilino sintético"), ("7520000", "Ingresos por arrendamientos"), ("4730001", "Retenciones alquiler"),
                     ("4730002", "Pagos a cuenta"), ("6290001", "Otros servicios"), ("6780000", "Gastos excepcionales"), ("5720001", "Banco sintético") })
            Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        var centro = await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Actividades (IdSociedad, Codigo, Nombre, Deducible, SerieCuentaIva, Activa, BonificacionIS) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "VIV", "Alquiler de viviendas", (byte)0, "4726", true, true);
            var actividad = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CentrosCoste (IdSociedad, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden, IdActividad) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                "T01", "300", "Piso A", true, true, (short)1, (byte)1, "300", "300", actividad);
            return await s.UltimoIdAsync();
        });
        var factura = new Asiento(_ctx, new DateTime(2026, 3, 1), "Alquiler sintético");
        var ingreso = Apunte.AlHaber("7520000", 20000m);
        ingreso.IdCentroCoste = centro;
        factura.Apuntes.AddRange([Apunte.AlDebe("4300001", 18100m), Apunte.AlDebe("4730001", 1900m), ingreso]);
        var gasto = new Asiento(_ctx, new DateTime(2026, 4, 1), "Gasto sintético");
        gasto.Apuntes.AddRange([Apunte.AlDebe("6290001", 4000m), Apunte.AlDebe("6780000", 100m), Apunte.AlHaber("5720001", 4100m)]);
        var pago = new Asiento(_ctx, new DateTime(2026, 10, 20), "Pago a cuenta sintético");
        pago.Apuntes.AddRange([Apunte.AlDebe("4730002", 500m), Apunte.AlHaber("5720001", 500m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([factura, gasto, pago], _actor, "Datos sintéticos")).EsCorrecto);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET BonificacionViviendasIS = 40 WHERE IdSociedadEjercicio = 9001"));
    }

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p)));

    private Task<int> AsientosDelLoteAsync() =>
        ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ? AND ClaveEjecucion = ?", ServicioImpuestoSociedades.MotorPropietario, "2026");

    [Fact]
    public async Task LeeElResultadoLasRetencionesYLosPosiblesAjustes()
    {
        await PrepararAsync();
        var d = await Servicio(Enero).DatosAsync(_ctx);
        Assert.Equal(15900m, d.ResultadoContable);
        // Renta de viviendas: los 20.000 del piso menos los gastos sin centro (generales) en proporción a los ingresos ordinarios (todos del piso).
        Assert.Equal(15900m, d.RentaViviendas);
        var v = d.Viviendas!;
        Assert.Equal((20000m, 0m, 4100m, 20000m, 20000m, 4100m), (v.Ingresos, v.GastosDirectos, v.GastosIndirectos, v.IngresosOrdinariosViviendas, v.IngresosOrdinariosTotales, v.IndirectosImputados));
        Assert.Equal([(PartidaRentaViviendas.GastoIndirecto, "(sin centro)", "6290001", 4000m), (PartidaRentaViviendas.GastoIndirecto, "(sin centro)", "6780000", 100m),
                (PartidaRentaViviendas.IngresoViviendas, "300 Piso A", "7520000", 20000m)],
            v.Partidas.Select(x => (x.Clase, x.Centro, x.Cuenta, x.Importe)).ToList());
        Assert.Equal(1900m, d.ApuntesRetencion!.Where(x => x.Cuenta == "4730001").Sum(x => x.Importe));
        Assert.Equal("Inquilino sintético", d.ApuntesRetencion!.Single(x => x.Cuenta == "4730001").Pagador);
        Assert.True(d.HayActividadBonificada);
        Assert.Equal([("4730001", 1900m), ("4730002", 500m)], d.Retenciones.Select(r => (r.Cuenta, r.Saldo)).ToList());
        Assert.Equal(["Inquilino sintético"], d.Retenciones[0].Pagadores!);
        Assert.Equal([("6780000", 100m)], d.PosiblesAjustes.Select(r => (r.Cuenta, r.Saldo)).ToList());
        Assert.Equal(0.25m, d.Parametros.TipoGeneral);
        Assert.Equal(0.40m, d.Parametros.Bonificacion);
    }

    [Fact]
    public async Task ConfirmarGrabaElAsientoYElCalculoYRepetirNoDuplica()
    {
        await PrepararAsync();
        var servicio = Servicio(Enero);
        Assert.Equal(EstadoImpuestoSociedades.Pendiente, (await servicio.SituacionAsync(_ctx)).Estado);
        var entrada = ConMulta with { Bonificaciones = 0m };
        var p = (await servicio.ProponerAsync(_ctx, entrada)).Valor;
        Assert.Equal(16000m, p.BaseImponible);
        Assert.Equal(4000m, p.CuotaLiquida);
        Assert.Equal([("6300000", 4000m, 0m), ("4730001", 0m, 1900m), ("4730002", 0m, 500m), ("4752000", 0m, 1600m)], p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());

        var r = await servicio.ConfirmarAsync(_ctx, entrada, null, _actor, p);
        Assert.True(r.EsCorrecto, r.Error?.Mensaje);
        Assert.NotNull(r.Valor.IdAsiento);
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("6300000"));
        Assert.Contains(r.Valor.CuentasCreadas, c => c.StartsWith("4752000"));
        Assert.Equal(1, await AsientosDelLoteAsync());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImpuestosSociedades WHERE IdSociedadEjercicio = 9001 AND CuotaLiquida = 4000 AND Retenciones = 2400"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImpuestosSociedadesAjustes WHERE Cuenta = '6780000' AND Aumento = True AND Importe = 100"));
        var situacion = await servicio.SituacionAsync(_ctx);
        Assert.Equal(EstadoImpuestoSociedades.Contabilizado, situacion.Estado);

        // Lo guardado vuelve como lo escrito; las 473 ya no tienen saldo fuera del lote.
        var otra = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.Equal(entrada.Ajustes, otra.Entrada.Ajustes);
        Assert.Equal(0m, otra.Entrada.Bonificaciones);
        Assert.Null(otra.Entrada.CuotaLiquida);
        Assert.Equal(p.Lineas, otra.Lineas);

        // Repetir pide motivo y regenera el mismo lote.
        Assert.False((await servicio.ConfirmarAsync(_ctx, entrada, null, _actor)).EsCorrecto);
        Assert.True((await servicio.ConfirmarAsync(_ctx, entrada, "Cifras de la gestoría", _actor)).EsCorrecto);
        Assert.Equal(1, await AsientosDelLoteAsync());
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImpuestosSociedades"));
    }

    [Fact]
    public async Task UnCambioPosteriorLoDejaDesactualizadoYDeshacerLoBorra()
    {
        await PrepararAsync();
        var servicio = Servicio(Enero);
        Assert.True((await servicio.ConfirmarAsync(_ctx, ConMulta, null, _actor)).EsCorrecto);
        var mas = new Asiento(_ctx, new DateTime(2026, 11, 30), "Otro gasto");
        mas.Apuntes.AddRange([Apunte.AlDebe("6290001", 50m), Apunte.AlHaber("5720001", 50m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([mas], _actor, "Datos sintéticos")).EsCorrecto);
        var situacion = await servicio.SituacionAsync(_ctx);
        Assert.Equal(EstadoImpuestoSociedades.Desactualizado, situacion.Estado);
        Assert.Contains("resultado contable", situacion.Motivo);

        Assert.False((await servicio.DeshacerAsync(_ctx, " ", _actor)).EsCorrecto);
        Assert.True((await servicio.DeshacerAsync(_ctx, "Prueba", _actor)).EsCorrecto);
        Assert.Equal(0, await AsientosDelLoteAsync());
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ImpuestosSociedades"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ImpuestosSociedadesAjustes"));
        Assert.Equal(EstadoImpuestoSociedades.Pendiente, (await servicio.SituacionAsync(_ctx)).Estado);
    }

    [Fact]
    public async Task AntesDeAcabarElAnoPideMotivo()
    {
        await PrepararAsync();
        var servicio = Servicio(new DateTime(2026, 11, 17));
        var p = (await servicio.ProponerAsync(_ctx, ConMulta)).Valor;
        Assert.Contains(p.Avisos, a => a.Contains("aún no ha terminado"));
        Assert.Equal("ImpuestoSociedades.SinMotivo", (await servicio.ConfirmarAsync(_ctx, ConMulta, null, _actor)).Error!.Codigo);
        Assert.True((await servicio.ConfirmarAsync(_ctx, ConMulta, "Prueba", _actor)).EsCorrecto);
    }

    [Fact]
    public async Task OtroAsientoDeLa630NoCuentaYNoDejaConfirmar()
    {
        await PrepararAsync();
        Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta("6300000", "Impuesto corriente"))).EsCorrecto);
        Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta("4752000", "HP acreedora IS"))).EsCorrecto);
        var hecho = new Asiento(_ctx, new DateTime(2026, 12, 31), "Impuesto a mano");
        hecho.Apuntes.AddRange([Apunte.AlDebe("6300000", 3000m), Apunte.AlHaber("4730001", 1900m), Apunte.AlHaber("4730002", 500m), Apunte.AlHaber("4752000", 600m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([hecho], _actor, "Datos sintéticos")).EsCorrecto);

        var servicio = Servicio(Enero);
        var d = await servicio.DatosAsync(_ctx);
        Assert.Equal(15900m, d.ResultadoContable);
        Assert.Equal(2400m, d.Retenciones.Sum(r => r.Saldo));
        Assert.Single(d.AsientosAjenos);
        var r = await servicio.ConfirmarAsync(_ctx, ConMulta, null, _actor);
        Assert.Equal("ImpuestoSociedades.AsientoAjeno", r.Error!.Codigo);
        Assert.Equal(0, await AsientosDelLoteAsync());
    }

    [Fact]
    public async Task ConRegularizacionNoDejaConfirmar()
    {
        await PrepararAsync();
        Assert.True((await Cuentas().AltaAsync(_ctx, new AltaCuenta("1290000", "Resultado del ejercicio"))).EsCorrecto);
        var reg = new Asiento(_ctx, new DateTime(2026, 12, 31), "Regularización") { Tipo = TipoAsiento.Regularizacion };
        reg.Apuntes.AddRange([Apunte.AlDebe("7520000", 20000m), Apunte.AlHaber("6290001", 4000m), Apunte.AlHaber("6780000", 100m), Apunte.AlHaber("1290000", 15900m)]);
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualesAsync([reg], _actor, "Datos sintéticos")).EsCorrecto);

        var servicio = Servicio(Enero);
        Assert.Equal(15900m, (await servicio.DatosAsync(_ctx)).ResultadoContable);
        Assert.Equal("ImpuestoSociedades.Regularizado", (await servicio.ConfirmarAsync(_ctx, ConMulta, null, _actor)).Error!.Codigo);
    }

    [Fact]
    public async Task DatosFiscalesYLoDeducidoSeGuardanYVuelven()
    {
        await PrepararAsync();
        var servicio = Servicio(Enero);
        var d = await servicio.DatosAsync(_ctx);
        // Hacienda conoce 1.800 del inquilino (no los 1.900 contabilizados) y 500 de pago fraccionado; el operador usa 1.850 con justificación.
        var importados = await servicio.ImportarDatosFiscalesAsync(_ctx, System.Text.Encoding.Latin1.GetBytes(DatosFiscalesAeatTests.Html(2026, "B12345674",
            pagos: [("2T", "500,00")], alquileres: [("X0000000X", "INQUILINO SINTETICO", "1.800,00")], bases: "1.000,00", presentado: null)), d);
        Assert.True(importados.EsCorrecto, importados.Error?.Mensaje);
        Assert.Equal(["4730002", "4730001"], importados.Valor.Retenciones.Select(r => r.Cuenta));
        var sinValor = servicio.Calcular(_ctx, d, ConMulta with { Bonificaciones = 0m, RetencionesAeat = importados.Valor.Retenciones, BasesNegativasAeat = importados.Valor.BasesNegativas });
        var inquilino = sinValor.FilasContraste.Single(f => f.Cuenta == "4730001");
        Assert.Equal(("Inquilino sintético", (decimal?)1800m, 1900m, 1), (inquilino.Pagador, inquilino.Hacienda, inquilino.Contable, inquilino.Apuntes.Count));
        Assert.Equal(1900m, inquilino.Apuntes[0].Importe);
        var entrada = sinValor.Entrada with { Valores = [new(inquilino.Clave, 1850m, "Certificado del inquilino: 1.850")] };
        var p = servicio.Calcular(_ctx, d, entrada);
        Assert.Null(p.PorQueNoContraste);
        Assert.Equal([("6300000", 4000m, 0m), ("4730001", 0m, 1850m), ("4730002", 0m, 500m), ("4752000", 0m, 1650m)], p.Lineas.Select(l => (l.Cuenta, l.Debe, l.Haber)).ToList());
        Assert.True((await servicio.ConfirmarAsync(_ctx, entrada, null, _actor, p)).EsCorrecto);

        var otra = (await servicio.ProponerAsync(_ctx)).Valor;
        Assert.True(otra.Entrada.Equivale(entrada with { Bonificaciones = otra.Entrada.Bonificaciones, CreditoPerdidas = otra.Entrada.CreditoPerdidas }));
        Assert.Equal(p.Lineas, otra.Lineas);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImpuestosSociedadesRetenciones WHERE Cuenta = '4730001' AND Hacienda = 1800 AND Contable = 1900 AND Propuesto = 1800 " +
            "AND Valor = 1850 AND Escrito = True AND Justificacion = 'Certificado del inquilino: 1.850'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM ImpuestosSociedadesDatosAeat"));
        // Sin justificación no se confirma.
        var sinJustificar = servicio.Calcular(_ctx, d, entrada with { Valores = [new(inquilino.Clave, 1850m)] });
        Assert.False((await servicio.ConfirmarAsync(_ctx, sinJustificar.Entrada, "Prueba", _actor, sinJustificar)).EsCorrecto);
        Assert.Equal(EstadoImpuestoSociedades.Contabilizado, (await servicio.SituacionAsync(_ctx)).Estado);

        // Otro NIF u otro ejercicio no se importan.
        var otroNif = await servicio.ImportarDatosFiscalesAsync(_ctx, System.Text.Encoding.Latin1.GetBytes(DatosFiscalesAeatTests.Html(2026, "B99999999", [], [], null, null)), d);
        Assert.False(otroNif.EsCorrecto);
        Assert.False((await servicio.ImportarDatosFiscalesAsync(_ctx, System.Text.Encoding.Latin1.GetBytes(DatosFiscalesAeatTests.Html(2025, "B12345674", [], [], null, null)), d)).EsCorrecto);
        Assert.Equal(LectorDatosFiscalesAeat.UrlDefecto, await servicio.UrlDatosFiscalesAsync());
    }

    /// <summary>
    /// Paridad con la hoja de la gestoría de 2025 contra una BD de validación del propietario, solo si PROCONTA_BD_VALIDACION_IS apunta a
    /// ella (se actualiza y se lee una copia temporal; el original no se abre). Sin la variable no hace nada. Escribe en la salida las cifras
    /// leídas y la bonificación que se propondría con la actividad de serie 4726 marcada.
    /// </summary>
    [Fact]
    public async Task ParidadConLaGestoria2025EnLaBdDeValidacion()
    {
        if (Environment.GetEnvironmentVariable("PROCONTA_BD_VALIDACION_IS") is not { Length: > 0 } origen || !File.Exists(origen)) return;
        var carpeta = BdPruebas.CarpetaTemporal();
        var copia = Path.Combine(carpeta, Path.GetFileName(origen));
        File.Copy(origen, copia);
        try
        {
            var act = await new ProConta.Datos.Esquema.ActualizadorEsquema(_actor).ActualizarAsync(copia, carpeta);
            Assert.True(act.EsCorrecto, act.Error?.Mensaje);
            var bd = new ProConta.Datos.Persistencia.Access.BaseDatosAccess(copia);
            await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Actividades SET BonificacionIS = True WHERE SerieCuentaIva = '4726'"));
            var ctx = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
                "SELECT IdSociedad, IdSociedadEjercicio, Ejercicio FROM SociedadesEjercicios WHERE Ejercicio = '2025'",
                f => new ContextoSociedadEjercicio(f.Texto("IdSociedad") ?? "", f.Entero("IdSociedadEjercicio"), 2025)));
            var servicio = new ServicioImpuestoSociedades(bd, new MotorAsientosAccess(bd), new CatalogoCuentasAccess(bd), null, new RelojFijo(new DateTime(2026, 11, 17)));
            var d = await servicio.DatosAsync(ctx!);
            d = d with { Parametros = d.Parametros with { TipoGeneral = 0.22m, LimiteTramo = 50000m, TipoTramo = 0.21m, Bonificacion = 0.40m } };
            salida.WriteLine($"Resultado contable {d.ResultadoContable}; renta de viviendas {d.RentaViviendas}; asientos ajenos: {string.Join(" | ", d.AsientosAjenos)}");
            foreach (var r in d.Retenciones) salida.WriteLine($"  {r.Cuenta} {r.Nombre}: {r.Saldo} ({string.Join(", ", r.Pagadores ?? [])})");
            foreach (var r in d.PosiblesAjustes) salida.WriteLine($"  posible {r.Cuenta} {r.Nombre}: {r.Saldo} en {r.Apuntes} apuntes");
            var propuesta = CalculoImpuestoSociedades.Calcular(d, new([new(false, false, "Ajuste art. 21.3", 51727.50m)]));
            salida.WriteLine($"Base {propuesta.BaseImponible}; cuota íntegra {propuesta.CuotaIntegra}; bonificación propuesta {propuesta.BonificacionPropuesta}; cuota líquida {propuesta.CuotaLiquida}");
            Assert.Equal(116430.95m, d.ResultadoContable);
            Assert.Equal(13734.76m, propuesta.CuotaIntegra);
            if (d.Viviendas is { } v)
            {
                salida.WriteLine($"Renta de viviendas: ingresos {v.Ingresos}; directos {v.GastosDirectos}; generales {v.GastosIndirectos}; ordinarios viviendas {v.IngresosOrdinariosViviendas} " +
                    $"de {v.IngresosOrdinariosTotales} ({v.Proporcion:P2}); imputados {v.IndirectosImputados}; renta {v.Renta}");
                foreach (var g in v.Partidas.GroupBy(x => (x.Clase, x.Centro)))
                    salida.WriteLine($"  {g.Key.Clase} · {g.Key.Centro}: {g.Sum(x => x.Importe)} ({g.Count()} cuentas)");
                foreach (var x in v.Partidas.Where(x => x.Clase != PartidaRentaViviendas.OtroIngresoOrdinario).OrderByDescending(x => x.Importe).Take(25))
                    salida.WriteLine($"    {x.Clase} · {x.Centro} · {x.Cuenta} {x.NombreCuenta}: {x.Importe}");
            }
            // Con los datos fiscales (PROCONTA_DATOS_FISCALES_IS: el HTML de la Sede), la gestoría deduce lo de Hacienda salvo en la 4735079, donde deduce lo contabilizado.
            if (Environment.GetEnvironmentVariable("PROCONTA_DATOS_FISCALES_IS") is not { Length: > 0 } html || !File.Exists(html)) return;
            var importados = await servicio.ImportarDatosFiscalesAsync(ctx!, await File.ReadAllBytesAsync(html), d);
            Assert.True(importados.EsCorrecto, importados.Error?.Mensaje);
            var aeat = importados.Valor;
            foreach (var r in aeat.Retenciones) salida.WriteLine($"  AEAT {r.Modelo} {r.Nif} {r.Nombre}: {r.Importe} → {r.Cuenta ?? "sin cuenta"}");
            salida.WriteLine($"Bases negativas según la AEAT {aeat.BasesNegativas}; presentado {aeat.ResultadoPresentado}; viviendas {aeat.ArrendamientoViviendas}");
            var conAeat = new EntradaImpuestoSociedades([new(false, false, "Ajuste art. 21.3", 51727.50m)], Bonificaciones: 2148.29m,
                RetencionesAeat: aeat.Retenciones, BasesNegativasAeat: aeat.BasesNegativas, ResultadoPresentado: aeat.ResultadoPresentado);
            var contraste = CalculoImpuestoSociedades.Calcular(d, conAeat).FilasContraste;
            foreach (var f in contraste)
                salida.WriteLine($"  contraste {f.Cuenta ?? "sin cuenta"} · {f.Pagador} ({f.Nif}): Hacienda {f.Hacienda}, contabilidad {f.Contable} en {f.Apuntes.Count} apuntes, " +
                    $"líneas de Hacienda {f.Aeat.Count}");
            var gestoria = CalculoImpuestoSociedades.Calcular(d, conAeat with
            {
                Valores = [.. contraste.Where(f => f.Cuenta == "4735079" && f.Contable != (f.Hacienda ?? 0m))
                    .Select(f => new ValorRetencion(f.Clave, Math.Max(0m, f.Contable), "Pagado por el inquilino; Hacienda no lo conoce"))],
            });
            foreach (var r in gestoria.Retenciones) salida.WriteLine($"  {r.Cuenta}: saldo {r.Saldo}, AEAT {r.SegunAeat}, propuesto {r.Propuesto}, deducido {r.Deducido}");
            foreach (var a in gestoria.Avisos) salida.WriteLine($"  aviso: {a}");
            Assert.Equal(11586.47m, gestoria.CuotaLiquida);
            Assert.Equal(17469.41m, gestoria.TotalRetenciones);
            Assert.Equal(-5882.94m, gestoria.Resultado);
            Assert.DoesNotContain(gestoria.Avisos, a => a.Contains("declaración presentada"));
        }
        finally
        {
            try { File.Delete(copia); } catch (IOException) { }
        }
    }
}
