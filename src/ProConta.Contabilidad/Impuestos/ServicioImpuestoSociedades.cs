using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Impuestos;

/// <summary>Estado del Impuesto sobre Sociedades del ejercicio.</summary>
public enum EstadoImpuestoSociedades
{
    /// <summary>Sin calcular ni contabilizar.</summary>
    Pendiente,
    /// <summary>Confirmado y al día: el resultado contable y las retenciones son los que se grabaron y su asiento existe.</summary>
    Contabilizado,
    /// <summary>Confirmado, pero después ha cambiado el resultado o las retenciones, o falta el asiento: hay que repetirlo.</summary>
    Desactualizado,
}

/// <summary>Lo que se guardó al confirmar (tabla ImpuestosSociedades) y lo que escribió el operador.</summary>
public sealed record ImpuestoSociedadesGuardado(decimal ResultadoContable, decimal BaseImponible, decimal CuotaLiquida, decimal Retenciones, decimal Resultado,
    string? Motivo, int? IdAsiento, string? Operador, DateTime FechaHora, EntradaImpuestoSociedades Entrada);

/// <summary>Situación del impuesto: lo guardado frente a lo que se calcula ahora con lo mismo que se escribió.</summary>
public sealed record SituacionImpuestoSociedades(EstadoImpuestoSociedades Estado, ImpuestoSociedadesGuardado? Guardado, string? Motivo, bool EjercicioTerminado);

/// <summary>Resultado de confirmar: el asiento (null si no hacía falta) y las cuentas creadas.</summary>
public sealed record ConfirmacionImpuestoSociedades(int? IdAsiento, IReadOnlyList<string> CuentasCreadas, PropuestaImpuestoSociedades Propuesta);

/// <summary>
/// Proceso «Impuesto sobre Sociedades» (contabilidad/procesos/impuesto-sociedades) sobre Access: lee el resultado contable, las
/// retenciones (473), los posibles ajustes y los parámetros del ejercicio, calcula la propuesta con <see cref="CalculoImpuestoSociedades"/>
/// y graba el asiento del 31/12 como lote propio regenerable, con el cálculo en ImpuestosSociedades. Los saldos salen de los apuntes,
/// nunca de la caché. Va antes de la regularización: no se confirma si el ejercicio ya la tiene.
/// </summary>
public sealed class ServicioImpuestoSociedades(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, Func<Task<bool>>? puede = null, IReloj? reloj = null)
{
    public const string MotorPropietario = "contabilidad/procesos/impuesto-sociedades";
    public const string CodigoProceso = "IMPUESTO_SOCIEDADES";
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(int ejercicio) => new(MotorPropietario, CodigoProceso, ejercicio.ToString(CultureInfo.InvariantCulture));

    public DateTime Hoy => _reloj.Ahora.Date;

    public bool EjercicioTerminado(ContextoSociedadEjercicio ctx) => Hoy > CalculoImpuestoSociedades.FechaAsiento(ctx.Ejercicio);

    /// <summary>Si hace falta motivo para confirmar.</summary>
    public static string? PorQueMotivo(bool ejercicioTerminado, bool repetir, bool cuotaFijada) =>
        !ejercicioTerminado ? "El ejercicio aún no ha terminado."
        : cuotaFijada ? "La cuota líquida no es la calculada."
        : repetir ? "Se repite un impuesto ya contabilizado." : null;

    /// <summary>Lo guardado frente a lo que se calcula ahora con lo mismo que se escribió.</summary>
    public async Task<SituacionImpuestoSociedades> SituacionAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var terminado = EjercicioTerminado(ctx);
        var g = await GuardadoAsync(ctx);
        var (idLote, _) = await AsientoDelLoteAsync(ctx);
        if (g is null && idLote is null) return new(EstadoImpuestoSociedades.Pendiente, null, null, terminado);
        string? motivo = null;
        if (g is null) motivo = "Hay asiento del impuesto, pero no su cálculo guardado.";
        else
        {
            var d = await DatosAsync(ctx, c);
            var totalRet = d.Retenciones.Where(r => r.Saldo > 0m).Sum(r => r.Saldo);
            if (g.IdAsiento is int id && await motor.ObtenerAsync(id) is null) motivo = "Su asiento ya no existe.";
            else if (g.IdAsiento is null && idLote is not null) motivo = "Hay un asiento del impuesto que no es el del cálculo guardado.";
            else if (d.ResultadoContable != g.ResultadoContable)
                motivo = $"El resultado contable es ahora {CalculoImpuestoSociedades.Euros(d.ResultadoContable)} y al contabilizarlo era {CalculoImpuestoSociedades.Euros(g.ResultadoContable)}.";
            else if (totalRet != g.Retenciones)
                motivo = $"Las retenciones y pagos a cuenta suman ahora {CalculoImpuestoSociedades.Euros(totalRet)} y al contabilizarlo {CalculoImpuestoSociedades.Euros(g.Retenciones)}.";
        }
        return new(motivo is null ? EstadoImpuestoSociedades.Contabilizado : EstadoImpuestoSociedades.Desactualizado, g, motivo, terminado);
    }

    /// <summary>Propuesta con lo escrito; null: lo guardado al confirmar o, si no hay, la propuesta sin ajustes.</summary>
    public async Task<Resultado<PropuestaImpuestoSociedades>> ProponerAsync(ContextoSociedadEjercicio ctx, EntradaImpuestoSociedades? entrada = null, CancellationToken c = default)
    {
        entrada ??= (await GuardadoAsync(ctx))?.Entrada ?? EntradaImpuestoSociedades.Vacia;
        if (CalculoImpuestoSociedades.Validar(entrada) is { } error)
            return Resultado<PropuestaImpuestoSociedades>.Fallo(new Error("ImpuestoSociedades.Entrada", error, CalculoImpuestoSociedades.Descripcion(ctx.Ejercicio)));
        var d = await DatosAsync(ctx, c);
        var p = CalculoImpuestoSociedades.Calcular(d, entrada);
        if (!EjercicioTerminado(ctx))
            p = p with
            {
                Avisos = ["El ejercicio aún no ha terminado: el cálculo es provisional con lo contabilizado hasta hoy. Se contabiliza en enero, antes de la regularización, con las cifras de la gestoría.",
                    .. p.Avisos],
            };
        return Resultado<PropuestaImpuestoSociedades>.Correcto(p);
    }

    /// <summary>
    /// Graba exactamente la propuesta que resulta de <paramref name="entrada"/> (se vuelve a calcular en el momento): el asiento del
    /// 31/12 como lote regenerable y, en la misma transacción, el cálculo y sus ajustes. Sin líneas, no hay asiento (y se borra el anterior).
    /// </summary>
    public async Task<Resultado<ConfirmacionImpuestoSociedades>> ConfirmarAsync(ContextoSociedadEjercicio ctx, EntradaImpuestoSociedades entrada, string? motivo, Actor actor,
        PropuestaImpuestoSociedades? vista = null)
    {
        Resultado<ConfirmacionImpuestoSociedades> Fallo(Error e) => Resultado<ConfirmacionImpuestoSociedades>.Fallo(e);
        var registro = CalculoImpuestoSociedades.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Fallo(new Error("ImpuestoSociedades.SinPermiso", "No tienes permiso para contabilizar el Impuesto sobre Sociedades.", registro,
                "La acción «Contabilizar el Impuesto sobre Sociedades» tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos."));
        var prop = await ProponerAsync(ctx, entrada);
        if (!prop.EsCorrecto) return Resultado<ConfirmacionImpuestoSociedades>.Fallo(prop.Errores);
        var p = prop.Valor;
        if (vista is not null && (!vista.Lineas.SequenceEqual(p.Lineas) || vista.ResultadoContable != p.ResultadoContable || vista.CuotaLiquida != p.CuotaLiquida))
            return Fallo(new Error("ImpuestoSociedades.Cambiada", "La propuesta ha cambiado desde que la abriste.", registro,
                "Alguien ha contabilizado o cambiado apuntes del ejercicio mientras la revisabas.", "Vuelve a calcularla y revísala antes de confirmar."));
        if (await RegularizacionAsync(ctx) is { } reg)
            return Fallo(new Error("ImpuestoSociedades.Regularizado", "El ejercicio ya está regularizado.", registro,
                $"El impuesto va antes de la regularización y el ejercicio ya tiene el asiento {reg}: la 6300 quedaría fuera del resultado.",
                "Deshaz antes el cierre del ejercicio (o la regularización) y vuelve aquí."));
        var ajenos = await AsientosAjenosAsync(ctx);
        if (ajenos.Count > 0)
            return Fallo(new Error("ImpuestoSociedades.AsientoAjeno", "Ya hay asiento del impuesto hecho fuera de este proceso.", registro,
                $"El ejercicio tiene {string.Join("; ", ajenos)}: contabilizarlo aquí lo duplicaría.", "Bórralo desde el Diario (o déjalo y no uses este proceso este año)."));
        var (idAnterior, _) = await AsientoDelLoteAsync(ctx);
        var repetir = idAnterior is not null || await GuardadoAsync(ctx) is not null;
        if (PorQueMotivo(EjercicioTerminado(ctx), repetir, p.CuotaFijada) is { } porQue && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("ImpuestoSociedades.SinMotivo", "Hay que escribir el motivo.", registro, porQue, "Escribe el motivo (por ejemplo «Cálculo de la gestoría»)."));

        var trabajo = new TrabajoLote(Despues: async (s, ids) => { await GuardarAsync(s, ctx, p, motivo, actor, ids.Count > 0 ? ids[0] : null); return Resultado.Correcto(); });
        var texto = string.IsNullOrWhiteSpace(motivo) ? registro : $"{registro}: {motivo.Trim()}";

        if (p.Lineas.Count == 0)
        {
            if (idAnterior is not null)
            {
                var borrado = await motor.EliminarLoteAsync(ctx, Lote(ctx.Ejercicio), actor, texto, trabajo);
                if (!borrado.EsCorrecto) return Resultado<ConfirmacionImpuestoSociedades>.Fallo(borrado.Errores);
            }
            else
                await bd.EnTransaccionAsync(async s => { await GuardarAsync(s, ctx, p, motivo, actor, null); return 0; });
            return Resultado<ConfirmacionImpuestoSociedades>.Correcto(new(null, [], p));
        }

        // Cuentas del impuesto que aún no existen en el ejercicio: se crean, colgadas de su cuenta.
        var cfg = (await ParametrosAsync(ctx)).Cuentas;
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [cfg.Impuesto] = "Impuesto corriente",
            [cfg.Acreedora] = "Hacienda Pública, acreedora por impuesto sobre sociedades",
            [cfg.Deudora] = "Hacienda Pública, deudora por impuesto sobre sociedades",
            [cfg.CreditoPerdidas] = "Crédito por pérdidas a compensar del ejercicio",
            [cfg.Diferido] = "Impuesto diferido",
        };
        var creadas = new List<string>();
        foreach (var codigo in p.Lineas.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal))
        {
            if ((await cuentas.ResolverAsync(ctx, codigo)).EsCorrecto) continue;
            if (!nombres.TryGetValue(codigo, out var nombre)) continue;
            var alta = await cuentas.AltaSubcuentaAsync(ctx, codigo, nombre);
            if (!alta.EsCorrecto) return Resultado<ConfirmacionImpuestoSociedades>.Fallo(alta.Errores);
            creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}»");
        }

        var asiento = new Asiento(ctx, p.Fecha, registro) { Notas = CalculoImpuestoSociedades.Notas(p, motivo) };
        foreach (var l in p.Lineas)
        {
            var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
            ap.GeneradoAutomaticamente = true;
            ap.MotivoGeneracion = "Impuesto sobre Sociedades";
            asiento.Apuntes.Add(ap);
        }
        var grabado = await motor.ReemplazarLoteAsync(ctx, Lote(ctx.Ejercicio), [asiento], actor, texto, trabajo);
        if (!grabado.EsCorrecto) return Resultado<ConfirmacionImpuestoSociedades>.Fallo(grabado.Errores);
        return Resultado<ConfirmacionImpuestoSociedades>.Correcto(new(grabado.Valor[0], creadas, p));
    }

    /// <summary>Borra el asiento del impuesto y el cálculo guardado, con motivo.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, string motivo, Actor actor)
    {
        var registro = CalculoImpuestoSociedades.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("ImpuestoSociedades.SinPermiso", "No tienes permiso para contabilizar el Impuesto sobre Sociedades.", registro, null, "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("ImpuestoSociedades.SinMotivo", "Hay que escribir el motivo.", registro, "Deshacer el impuesto queda registrado con su motivo.", "Escribe el motivo."));
        if (await RegularizacionAsync(ctx) is { } reg)
            return Resultado<int>.Fallo(new Error("ImpuestoSociedades.Regularizado", "El ejercicio ya está regularizado.", registro,
                $"El ejercicio tiene el asiento {reg} y su resultado ya incluye este impuesto.", "Deshaz antes el cierre del ejercicio (o la regularización)."));
        var (id, _) = await AsientoDelLoteAsync(ctx);
        if (id is null)
        {
            await bd.EnTransaccionAsync(async s => { await BorrarGuardadoAsync(s, ctx); return 0; });
            return Resultado<int>.Correcto(0);
        }
        var trabajo = new TrabajoLote(Despues: async (s, _) => { await BorrarGuardadoAsync(s, ctx); return Resultado.Correcto(); });
        return await motor.EliminarLoteAsync(ctx, Lote(ctx.Ejercicio), actor, $"Deshacer el {registro}: {motivo.Trim()}", trabajo);
    }

    // ---------- grabación ----------

    private async Task GuardarAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, PropuestaImpuestoSociedades p, string? motivo, Actor actor, int? idAsiento)
    {
        await BorrarGuardadoAsync(s, ctx);
        await s.EjecutarAsync(
            "INSERT INTO ImpuestosSociedades (IdSociedadEjercicio, ResultadoContable, AjustesAumentos, AjustesDisminuciones, BasesNegativas, BaseImponible, TipoGeneral, " +
            "LimiteTramoReducido, TipoTramoReducido, CuotaIntegra, Bonificaciones, Deducciones, CuotaCalculada, CuotaLiquida, Retenciones, CreditoPerdidas, Resultado, " +
            "Motivo, IdAsiento, Operador, FechaHoraConfirmacion) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ctx.IdSociedadEjercicio, p.ResultadoContable, p.AumentosPermanentes + p.AumentosTemporales, p.DisminucionesPermanentes + p.DisminucionesTemporales,
            p.BasesNegativas, p.BaseImponible, Porcentaje(p.TipoGeneral), p.LimiteTramo, p.TipoTramo is decimal t ? Porcentaje(t) : null, p.CuotaIntegra,
            p.Bonificaciones, p.Deducciones, p.CuotaCalculada, p.CuotaLiquida, p.TotalRetenciones, p.CreditoPerdidas, p.Resultado,
            string.IsNullOrWhiteSpace(motivo) ? null : Recortar(motivo.Trim(), 255), idAsiento, Recortar(actor.ToString(), 100), _reloj.Ahora);
        var idImpuesto = await s.UltimoIdAsync();
        var e = p.Entrada;
        for (var i = 0; i < e.Ajustes.Count; i++)
        {
            var a = e.Ajustes[i];
            await s.EjecutarAsync(
                "INSERT INTO ImpuestosSociedadesAjustes (IdImpuestoSociedades, Orden, Temporal, Aumento, Descripcion, Cuenta, Importe) VALUES (?, ?, ?, ?, ?, ?, ?)",
                idImpuesto, i + 1, a.Temporal, a.Aumento, Recortar(a.Descripcion.Trim(), 255), string.IsNullOrWhiteSpace(a.Cuenta) ? null : a.Cuenta.Trim(), a.Importe);
        }
    }

    private static async Task BorrarGuardadoAsync(ISesionDatos s, ContextoSociedadEjercicio ctx)
    {
        await s.EjecutarAsync("DELETE FROM ImpuestosSociedadesAjustes WHERE IdImpuestoSociedades IN (SELECT IdImpuestoSociedades FROM ImpuestosSociedades WHERE IdSociedadEjercicio = ?)",
            ctx.IdSociedadEjercicio);
        await s.EjecutarAsync("DELETE FROM ImpuestosSociedades WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio);
    }

    // ---------- lecturas ----------

    /// <summary>Lo guardado al confirmar, con sus ajustes; null si no hay.</summary>
    public Task<ImpuestoSociedadesGuardado?> GuardadoAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<ImpuestoSociedadesGuardado?>(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT IdImpuestoSociedades, ResultadoContable, BasesNegativas, BaseImponible, Bonificaciones, Deducciones, CuotaCalculada, CuotaLiquida, Retenciones, " +
            "CreditoPerdidas, Resultado, Motivo, IdAsiento, Operador, FechaHoraConfirmacion FROM ImpuestosSociedades WHERE IdSociedadEjercicio = ?",
            f => (Id: f.Entero("IdImpuestoSociedades"), Resultado: Dec(f, "ResultadoContable"), Bases: Dec(f, "BasesNegativas"), Base: Dec(f, "BaseImponible"),
                Bonif: Dec(f, "Bonificaciones"), Deduc: Dec(f, "Deducciones"), Calculada: Dec(f, "CuotaCalculada"), Cuota: Dec(f, "CuotaLiquida"),
                Ret: Dec(f, "Retenciones"), Credito: Dec(f, "CreditoPerdidas"), Saldo: Dec(f, "Resultado"), Motivo: f.Texto("Motivo"), IdAsiento: f.EnteroNulo("IdAsiento"),
                Operador: f.Texto("Operador"), Fecha: f.Fecha("FechaHoraConfirmacion")),
            ctx.IdSociedadEjercicio);
        if (filas.Count == 0) return null;
        var g = filas[0];
        var ajustes = await s.ConsultarAsync(
            "SELECT Temporal, Aumento, Descripcion, Cuenta, Importe FROM ImpuestosSociedadesAjustes WHERE IdImpuestoSociedades = ? ORDER BY Orden",
            f => new AjusteImpuestoSociedades(f.Logico("Temporal"), f.Logico("Aumento"), (f.Texto("Descripcion") ?? "").Trim(), Dec(f, "Importe"),
                string.IsNullOrWhiteSpace(f.Texto("Cuenta")) ? null : f.Texto("Cuenta")!.Trim()),
            g.Id);
        // Lo escrito: la bonificación y el crédito tal como se grabaron, y la cuota solo si se fijó a mano.
        var entrada = new EntradaImpuestoSociedades(ajustes, g.Bases, g.Bonif, g.Deduc, g.Credito, g.Cuota != g.Calculada ? g.Cuota : null);
        return new ImpuestoSociedadesGuardado(g.Resultado, g.Base, g.Cuota, g.Ret, g.Saldo, g.Motivo, g.IdAsiento, g.Operador, g.Fecha, entrada);
    });

    /// <summary>Parámetros y cuentas del IS del ejercicio.</summary>
    public Task<ParametrosImpuestoSociedades> ParametrosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync(
            "SELECT TipoImpuestoSociedades, LimiteTramoReducidoIS, TipoTramoReducidoIS, BonificacionViviendasIS, PrefijosAjustesIS, CuentaImpuestoSociedades, " +
            "CuentaHaciendaAcreedoraIS, CuentaHaciendaDeudoraIS, CuentaCreditoPerdidasIS, CuentaImpuestoDiferidoIS FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => new ParametrosImpuestoSociedades(
                f.DecimalNulo("TipoImpuestoSociedades") is decimal t ? t / 100m : ParametrosImpuestoSociedades.TipoGeneralDefecto,
                f.DecimalNulo("LimiteTramoReducidoIS"), f.DecimalNulo("TipoTramoReducidoIS") is decimal r ? r / 100m : null,
                (f.DecimalNulo("BonificacionViviendasIS") ?? 0m) / 100m, ParametrosImpuestoSociedades.LeerPrefijos(f.Texto("PrefijosAjustesIS")),
                CuentasImpuestoSociedades.Con(f.Texto("CuentaImpuestoSociedades"), f.Texto("CuentaHaciendaAcreedoraIS"), f.Texto("CuentaHaciendaDeudoraIS"),
                    f.Texto("CuentaCreditoPerdidasIS"), f.Texto("CuentaImpuestoDiferidoIS"))),
            ctx.IdSociedadEjercicio) ?? ParametrosImpuestoSociedades.Defecto);

    private sealed record ApunteLeido(int IdAsiento, int Tipo, string Cuenta, decimal Debe, decimal Haber, int? IdCentro, bool DelLote);

    /// <summary>Todo lo que el cálculo lee de la contabilidad del ejercicio.</summary>
    public async Task<DatosImpuestoSociedades> DatosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var parametros = await ParametrosAsync(ctx);
        var (bonificados, hayBonificada) = await CentrosBonificadosAsync(ctx.IdSociedad);
        var lote = Lote(ctx.Ejercicio);
        var fin = CalculoImpuestoSociedades.FechaAsiento(ctx.Ejercicio);
        var (apuntes, nombres) = await bd.EjecutarAsync(async s =>
        {
            var a = await s.ConsultarAsync(
                "SELECT a.IdAsiento, a.TipoAsiento, a.FechaContable, a.MotorPropietario, a.CodigoProceso, a.ClaveEjecucion, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.IdCentroCoste " +
                "FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.FechaContable <= ?",
                f => new ApunteLeido(f.Entero("IdAsiento"), Convert.ToInt32(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture), (f.Texto("CuentaContable") ?? "").Trim(),
                    f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m, f.EnteroNulo("IdCentroCoste"),
                    f.Texto("MotorPropietario") == lote.MotorPropietario && f.Texto("CodigoProceso") == lote.CodigoProceso && f.Texto("ClaveEjecucion") == lote.ClaveEjecucion),
                ctx.IdSociedadEjercicio, fin.AddDays(1).AddTicks(-1));
            var n = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (codigo, nombre) in await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                         f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedadEjercicio))
                if (codigo.Length > 0) n.TryAdd(codigo, nombre);
            return (a, n);
        }, c);

        // Sin este proceso, sin la regularización (2) ni el cierre (3).
        var propios = apuntes.Where(x => !x.DelLote).ToList();
        var ordinarios = propios.Where(x => x.Tipo is not (TipoRegularizacion or TipoCierre)).ToList();
        var ajenos = ordinarios.Where(x => CalculoImpuestoSociedades.EsImpuestoBeneficios(x.Cuenta)).Select(x => x.IdAsiento).ToHashSet();
        var enResultado = ordinarios.Where(x => CalculoImpuestoSociedades.EntraEnResultado(x.Cuenta)).ToList();
        var resultado = enResultado.Sum(x => x.Haber - x.Debe);
        var viviendas = enResultado.Where(x => x.IdCentro is int ce && bonificados.Contains(ce)).Sum(x => x.Haber - x.Debe);
        string Nombre(string cuenta) => nombres.GetValueOrDefault(cuenta, "");

        // Retenciones: saldo de cada 473 sin el cierre ni los asientos del impuesto hechos fuera del proceso.
        var paraRet = propios.Where(x => x.Tipo != TipoCierre && !ajenos.Contains(x.IdAsiento)).ToList();
        var porAsiento = paraRet.ToLookup(x => x.IdAsiento);
        var retenciones = paraRet.Where(x => CalculoImpuestoSociedades.EsRetencion(x.Cuenta)).GroupBy(x => x.Cuenta)
            .Select(g =>
            {
                var pagadores = g.Where(x => x.Debe > 0m).Select(x => x.IdAsiento).Distinct()
                    .SelectMany(id => porAsiento[id].Where(o => o.Cuenta.StartsWith("43", StringComparison.Ordinal) || o.Cuenta.StartsWith("44", StringComparison.Ordinal)))
                    .Select(o => Nombre(o.Cuenta) is { Length: > 0 } nn ? nn : o.Cuenta).Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(x => x, StringComparer.CurrentCulture).ToList();
                return new SaldoCuentaImpuesto(g.Key, Nombre(g.Key), g.Sum(x => x.Debe - x.Haber), g.Count(), pagadores);
            })
            .Where(r => r.Saldo != 0m).OrderBy(r => r.Cuenta, StringComparer.Ordinal).ToList();

        var posibles = ordinarios.Where(x => parametros.PrefijosAjustes.Any(pr => x.Cuenta.StartsWith(pr, StringComparison.Ordinal)) && x.Cuenta.Length > 3)
            .GroupBy(x => x.Cuenta).Select(g => new SaldoCuentaImpuesto(g.Key, Nombre(g.Key), g.Sum(x => x.Debe - x.Haber), g.Count()))
            .Where(r => r.Saldo != 0m).OrderBy(r => r.Cuenta, StringComparer.Ordinal).ToList();

        var credito = paraRet.Where(x => x.Cuenta == parametros.Cuentas.CreditoPerdidas).Sum(x => x.Debe - x.Haber);
        var textosAjenos = ajenos.Count == 0 ? [] : await TextosAsientosAsync(ajenos);
        return new DatosImpuestoSociedades(ctx.Ejercicio, resultado, viviendas, hayBonificada, retenciones, posibles, Math.Max(0m, credito), parametros, textosAjenos);
    }

    /// <summary>Asientos del ejercicio con una 630 hechos fuera de este proceso (sin regularización ni cierre), como texto.</summary>
    public async Task<IReadOnlyList<string>> AsientosAjenosAsync(ContextoSociedadEjercicio ctx) => (await DatosAsync(ctx)).AsientosAjenos;

    private Task<IReadOnlyList<string>> TextosAsientosAsync(IReadOnlyCollection<int> ids) => bd.EjecutarAsync<IReadOnlyList<string>>(async s =>
    {
        var r = new List<string>();
        foreach (var id in ids.OrderBy(x => x))
        {
            var f = await s.ConsultarPrimeroAsync("SELECT FechaContable, Descripcion FROM Asientos WHERE IdAsiento = ?",
                x => (Fecha: x.Fecha("FechaContable"), Texto: (x.Texto("Descripcion") ?? "").Trim()), id);
            r.Add($"asiento {id} del {f.Fecha:dd/MM/yyyy}, «{f.Texto}»");
        }
        return r;
    });

    /// <summary>Número y fecha del primer asiento de regularización o cierre del ejercicio; null si no tiene.</summary>
    public Task<string?> RegularizacionAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<string?>(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT IdAsiento, FechaContable, TipoAsiento FROM Asientos WHERE IdSociedadEjercicio = ? AND EstadoAsiento = 1 AND TipoAsiento IN (2, 3) ORDER BY TipoAsiento, IdAsiento",
            f => (Id: f.Entero("IdAsiento"), Fecha: f.Fecha("FechaContable"), Tipo: Convert.ToInt32(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture)),
            ctx.IdSociedadEjercicio);
        return filas.Count == 0 ? null : $"{filas[0].Id} de {(filas[0].Tipo == TipoRegularizacion ? "regularización" : "cierre")} del {filas[0].Fecha:dd/MM/yyyy}";
    });

    /// <summary>El asiento del lote del impuesto (si lo hay) y su importe en la 630 (Debe − Haber).</summary>
    public Task<(int? IdAsiento, decimal Importe)> AsientoDelLoteAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<(int? IdAsiento, decimal Importe)>(async s =>
    {
        var lote = Lote(ctx.Ejercicio);
        var filas = await s.ConsultarAsync(
            "SELECT a.IdAsiento, p.CuentaContable, p.ImporteDebe, p.ImporteHaber FROM Asientos AS a LEFT JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.ClaveEjecucion = ?",
            f => (Id: f.Entero("IdAsiento"), Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Debe: f.DecimalNulo("ImporteDebe") ?? 0m, Haber: f.DecimalNulo("ImporteHaber") ?? 0m),
            ctx.IdSociedadEjercicio, lote.MotorPropietario, lote.CodigoProceso, lote.ClaveEjecucion);
        if (filas.Count == 0) return ((int?)null, 0m);
        return ((int?)filas.Min(x => x.Id), filas.Where(x => CalculoImpuestoSociedades.EsImpuestoBeneficios(x.Cuenta)).Sum(x => x.Debe - x.Haber));
    });

    /// <summary>Centros de la sociedad cuya actividad (propia o heredada) tiene la bonificación por alquiler de viviendas, y si hay alguna actividad marcada.</summary>
    private Task<(HashSet<int> Centros, bool HayActividad)> CentrosBonificadosAsync(string idSociedad) => bd.EjecutarAsync(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT c.IdCentroCoste, c.IdCentroCostePadre, a.Codigo, a.Nombre, a.Activa, a.BonificacionIS FROM CentrosCoste AS c " +
            "LEFT JOIN Actividades AS a ON c.IdActividad = a.IdActividad WHERE c.IdSociedad = ?",
            f => (Id: f.Entero("IdCentroCoste"), Padre: f.EnteroNulo("IdCentroCostePadre"),
                Propia: f.Texto("Codigo") is { } codigo && !f.EsNulo("Activa") && f.Logico("Activa")
                    ? new ActividadIva(codigo.Trim(), (f.Texto("Nombre") ?? codigo).Trim(), (byte)(!f.EsNulo("BonificacionIS") && f.Logico("BonificacionIS") ? 1 : 0), null)
                    : null),
            idSociedad);
        var hay = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Actividades WHERE IdSociedad = ? AND BonificacionIS = True AND Activa = True", idSociedad),
            CultureInfo.InvariantCulture) > 0;
        // El campo Deducible del registro se usa aquí solo como marca de bonificación (1).
        var resueltas = LectorActividadesIva.ResolverHerencia(filas.Select(f => (f.Id, f.Padre, f.Propia)));
        return (resueltas.Where(kv => kv.Value.Deducible == 1).Select(kv => kv.Key).ToHashSet(), hay);
    });

    private const int TipoRegularizacion = 2;
    private const int TipoCierre = 3;

    private static decimal Dec(IFilaDatos f, string campo) => f.DecimalNulo(campo) ?? 0m;
    private static decimal Porcentaje(decimal tantoPorUno) => Math.Round(tantoPorUno * 100m, 4, MidpointRounding.AwayFromZero);
    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
