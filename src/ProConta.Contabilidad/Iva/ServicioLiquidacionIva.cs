using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Iva;

/// <summary>Estado de la liquidación del IVA de un trimestre.</summary>
public enum EstadoLiquidacionIva
{
    /// <summary>El trimestre aún no ha terminado: no se liquida.</summary>
    EnCurso,
    /// <summary>Terminado y sin liquidar.</summary>
    Pendiente,
    /// <summary>Liquidado y al día: sus 472 y 477 están a cero y el 303 no ha cambiado.</summary>
    Liquidado,
    /// <summary>Liquidado, pero después ha cambiado algo: las 472/477 no están a cero o el 303 da otro resultado. Hay que repetirla.</summary>
    Desactualizado,
}

/// <summary>Lo que se guardó al confirmar una liquidación (tabla LiquidacionesIva).</summary>
public sealed record LiquidacionGuardada(int Trimestre, decimal? Prorrata, decimal ResultadoCalculado, decimal ImporteLiquidado, decimal Compensado, decimal Ajuste,
    string? Motivo, int? IdAsiento, string? Operador, DateTime FechaHora);

/// <summary>Un trimestre en la pantalla de la liquidación: su estado, lo guardado y por qué está desactualizado.</summary>
public sealed record TrimestreLiquidacionIva(int Trimestre, DateTime Fin, EstadoLiquidacionIva Estado, bool Cerrado, decimal ResultadoCalculado,
    LiquidacionGuardada? Guardada, string? Motivo)
{
    public string Nombre => CalculoLiquidacionIva.Nombre(Trimestre);
}

/// <summary>Resultado de confirmar: el asiento, el trimestre activo después y las cuentas creadas.</summary>
public sealed record ConfirmacionLiquidacionIva(int IdAsiento, int? TrimestreActivo, IReadOnlyList<string> CuentasCreadas, PropuestaLiquidacionIva Propuesta);

/// <summary>
/// Proceso «Liquidación de IVA» (contabilidad/procesos/liquidacion-iva) sobre Access: propone el asiento de un trimestre con
/// <see cref="CalculoLiquidacionIva"/>, lo graba como lote propio regenerable (repetirlo no duplica) y, si el trimestre era el
/// activo, activa el siguiente (el cuarto lo cierra el cierre del ejercicio). Puede grabar en el trimestre cerrado que liquida
/// (propietario 2026-11-16 08:37, decisión 2) con motivo. Los saldos salen de los apuntes, nunca de la caché.
/// </summary>
public sealed class ServicioLiquidacionIva(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, Func<Task<bool>>? puede = null, IReloj? reloj = null)
{
    public const string CodigoProceso = "LIQUIDACION_IVA";
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(int ejercicio, int trimestre) =>
        new(OpcionesLote.MotorLiquidacionIva, CodigoProceso, $"{ejercicio}-T{trimestre}");

    public DateTime Hoy => _reloj.Ahora.Date;

    /// <summary>Los cuatro trimestres del ejercicio con su estado.</summary>
    public async Task<IReadOnlyList<TrimestreLiquidacionIva>> TrimestresAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var lector = new LectorLineasIva(bd);
        var lineas = await lector.LeerAsync(ctx, c);
        var provisional = await lector.ProrrataProvisionalAsync(ctx, c);
        var r303 = Calculo303.Calcular(lineas, provisional ?? 1m);
        var guardadas = await GuardadasAsync(ctx);
        var activo = await TrimestreActivoAsync(ctx);
        var r = new List<TrimestreLiquidacionIva>();
        for (var t = 1; t <= 4; t++)
        {
            var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, t);
            var g = guardadas.FirstOrDefault(x => x.Trimestre == t);
            var calculado = r303.Resultado(t);
            var cerrado = activo is int a && t < a;
            if (g is null)
            {
                r.Add(new(t, fin, Hoy > fin ? EstadoLiquidacionIva.Pendiente : EstadoLiquidacionIva.EnCurso, cerrado, calculado, null, null));
                continue;
            }
            var pendientes = (await SaldosAsync(ctx, fin, null)).Where(s => CalculoLiquidacionIva.SeLiquida(s.Cuenta) && s.Saldo != 0m).ToList();
            string? motivo = null;
            if (g.IdAsiento is int id && await motor.ObtenerAsync(id) is null) motivo = "Su asiento ya no existe.";
            else if (pendientes.Count > 0)
                motivo = $"Después de liquidarla han cambiado apuntes del trimestre: {string.Join(", ", pendientes.Take(4).Select(s => s.Cuenta))}{(pendientes.Count > 4 ? "…" : "")} no están a cero.";
            else if (calculado != g.ResultadoCalculado)
                motivo = $"El 303 da ahora {Euros(calculado)} y al liquidarla daba {Euros(g.ResultadoCalculado)}.";
            r.Add(new(t, fin, motivo is null ? EstadoLiquidacionIva.Liquidado : EstadoLiquidacionIva.Desactualizado, cerrado, calculado, g, motivo));
        }
        return r;
    }

    /// <summary>
    /// Propuesta del trimestre: saldos de las 472/477 y crédito de la 4700 al último día del trimestre sin contar esta liquidación,
    /// 303 desde los libros con la prorrata provisional. <paramref name="importeFijado"/>: el importe que fija el operador (null: el calculado).
    /// </summary>
    public async Task<Resultado<PropuestaLiquidacionIva>> ProponerAsync(ContextoSociedadEjercicio ctx, int trimestre, decimal? importeFijado = null, CancellationToken c = default)
    {
        if (trimestre is < 1 or > 4)
            return Resultado<PropuestaLiquidacionIva>.Fallo(new Error("LiquidacionIva.Trimestre", "El trimestre va de 1 a 4.", $"Trimestre {trimestre}"));
        var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, trimestre);
        if (Hoy <= fin)
            return Resultado<PropuestaLiquidacionIva>.Fallo(new Error("LiquidacionIva.EnCurso",
                $"El {CalculoLiquidacionIva.Nombre(trimestre)} de {ctx.Ejercicio} aún no ha terminado: se liquida a partir del {fin.AddDays(1):dd/MM/yyyy}.",
                $"Trimestre {trimestre}", "La liquidación salda las cuentas de IVA del trimestre completo.", "Vuelve cuando haya terminado."));
        // Por orden: un trimestre anterior sin liquidar dejaría sus saldos en este (salvo que ya estén a cero, como en los años migrados).
        var guardadas = await GuardadasAsync(ctx);
        for (var anterior = 1; anterior < trimestre; anterior++)
        {
            if (guardadas.Any(g => g.Trimestre == anterior)) continue;
            var pendientes = (await SaldosAsync(ctx, CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, anterior), null)).Where(s => CalculoLiquidacionIva.SeLiquida(s.Cuenta)).ToList();
            if (pendientes.Count > 0)
                return Resultado<PropuestaLiquidacionIva>.Fallo(new Error("LiquidacionIva.AnteriorPendiente",
                    $"Antes hay que liquidar el {CalculoLiquidacionIva.Nombre(anterior)}.", $"Trimestre {trimestre}",
                    $"Sus cuentas de IVA ({string.Join(", ", pendientes.Take(3).Select(s => s.Cuenta))}{(pendientes.Count > 3 ? "…" : "")}) no están a cero el {CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, anterior):dd/MM/yyyy}, y su saldo entraría en esta liquidación.",
                    $"Elige el {CalculoLiquidacionIva.Nombre(anterior)} en la lista y liquídalo primero."));
        }
        var lector = new LectorLineasIva(bd);
        var lineas = await lector.LeerAsync(ctx, c);
        var provisional = await lector.ProrrataProvisionalAsync(ctx, c);
        var r303 = Calculo303.Calcular(lineas, provisional ?? 1m);
        var lote = Lote(ctx.Ejercicio, trimestre);
        var saldos = await SaldosAsync(ctx, fin, lote);
        var config = await CuentasAsync(ctx);
        int? centroAjustes = null;
        if ((await cuentas.ResolverAsync(ctx, config.AjustesNegativos)) is { EsCorrecto: true } neg) centroAjustes = neg.Valor.IdCentroCostePredeterminado;
        var datos = new DatosLiquidacionIva(ctx.Ejercicio, trimestre, saldos.Where(s => CalculoLiquidacionIva.SeLiquida(s.Cuenta)).ToList(),
            saldos.Where(s => s.Cuenta.StartsWith(CalculoLiquidacionIva.PrefijoCredito, StringComparison.Ordinal)).ToList(), r303, lineas, config, importeFijado, centroAjustes);
        var propuesta = CalculoLiquidacionIva.Calcular(datos);
        if (provisional is null)
            propuesta = propuesta with { Avisos = [.. propuesta.Avisos, "El ejercicio no tiene prorrata provisional: se deduce el 100 % del IVA en prorrata. Ponla en Parámetros y tablas › Ejercicios (Impuestos)."] };
        return Resultado<PropuestaLiquidacionIva>.Correcto(propuesta);
    }

    /// <summary>Si hace falta motivo para confirmar: hay ajuste, el trimestre está cerrado o ya estaba liquidado (se repite).</summary>
    public static string? PorQueMotivo(PropuestaLiquidacionIva p, bool cerrado, bool repetir) =>
        p.HayAjuste ? "La liquidación lleva una línea de ajuste." : cerrado ? "El trimestre está cerrado." : repetir ? "Se repite una liquidación ya confirmada." : null;

    /// <summary>
    /// Graba exactamente la propuesta que resulta de <paramref name="importeFijado"/> (se vuelve a calcular en el momento), crea las
    /// cuentas de la liquidación que falten, guarda la fila de LiquidacionesIva y, si el trimestre era el activo, activa el siguiente.
    /// </summary>
    public async Task<Resultado<ConfirmacionLiquidacionIva>> ConfirmarAsync(ContextoSociedadEjercicio ctx, int trimestre, decimal? importeFijado, string? motivo, Actor actor,
        PropuestaLiquidacionIva? vista = null)
    {
        Resultado<ConfirmacionLiquidacionIva> Fallo(Error e) => Resultado<ConfirmacionLiquidacionIva>.Fallo(e);
        var registro = $"Liquidación del IVA {CalculoLiquidacionIva.Nombre(trimestre)} {ctx.Ejercicio}";
        if (puede is not null && !await puede())
            return Fallo(new Error("LiquidacionIva.SinPermiso", "No tienes permiso para liquidar el IVA.", registro,
                "La acción «Liquidar el IVA» tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos."));
        var prop = await ProponerAsync(ctx, trimestre, importeFijado);
        if (!prop.EsCorrecto) return Resultado<ConfirmacionLiquidacionIva>.Fallo(prop.Errores);
        var p = prop.Valor;
        if (vista is not null && !MismoAsiento(vista, p))
            return Fallo(new Error("LiquidacionIva.Cambiada", "La propuesta ha cambiado desde que la abriste.", registro,
                "Alguien ha contabilizado o cambiado apuntes de IVA del trimestre mientras la revisabas.", "Vuelve a calcularla y revísala antes de confirmar."));
        if (p.Lineas.Count == 0)
            return Fallo(new Error("LiquidacionIva.Vacia", "No hay nada que liquidar en el trimestre.", registro,
                "Las 472 y 477 están a cero y el 303 da cero.", null));
        var activo = await TrimestreActivoAsync(ctx);
        var cerrado = activo is int a && trimestre < a;
        var repetir = (await GuardadasAsync(ctx)).Any(g => g.Trimestre == trimestre);
        if (PorQueMotivo(p, cerrado, repetir) is { } porQue && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("LiquidacionIva.SinMotivo", "Hay que escribir el motivo.", registro, porQue, "Escribe el motivo (por ejemplo «Ajuste a lo presentado por la gestoría»)."));

        // Cuentas de la liquidación que aún no existen en el ejercicio: se crean, colgadas de su cuenta (p. ej. 6391000 de la 639).
        var config = await CuentasAsync(ctx);
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [config.HaciendaAcreedora] = "Hacienda Pública, acreedora por IVA",
            [config.HaciendaDeudora] = "Hacienda Pública, deudora por IVA",
            [config.AjustesNegativos] = "Ajustes negativos en IVA de activo corriente",
            [config.AjustesPositivos] = "Ajustes positivos en IVA de activo corriente",
        };
        var creadas = new List<string>();
        foreach (var codigo in p.Lineas.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal))
        {
            if ((await cuentas.ResolverAsync(ctx, codigo)).EsCorrecto) continue;
            if (!nombres.TryGetValue(codigo, out var nombre)) continue; // el motor explica el error de las demás
            var alta = await cuentas.AltaSubcuentaAsync(ctx, codigo, nombre);
            if (!alta.EsCorrecto) return Resultado<ConfirmacionLiquidacionIva>.Fallo(alta.Errores);
            creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}»");
        }

        var asiento = new Asiento(ctx, p.Fecha, CalculoLiquidacionIva.Descripcion(ctx.Ejercicio, trimestre))
        {
            Notas = Notas(p, motivo),
        };
        foreach (var l in p.Lineas)
        {
            var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
            ap.IdCentroCoste = l.IdCentroCoste;
            ap.GeneradoAutomaticamente = true;
            ap.MotivoGeneracion = "Liquidación del IVA";
            asiento.Apuntes.Add(ap);
        }
        var texto = string.IsNullOrWhiteSpace(motivo) ? registro : $"{registro}: {motivo.Trim()}";
        int? activoDespues = activo;
        var trabajo = new TrabajoLote(Despues: async (s, ids) =>
        {
            await s.EjecutarAsync("DELETE FROM LiquidacionesIva WHERE IdSociedadEjercicio = ? AND Trimestre = ?", ctx.IdSociedadEjercicio, (byte)trimestre);
            await s.EjecutarAsync(
                "INSERT INTO LiquidacionesIva (IdSociedadEjercicio, Trimestre, Prorrata, ResultadoCalculado, ImporteLiquidado, Compensado, Ajuste, Motivo, IdAsiento, Operador, FechaHoraConfirmacion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                ctx.IdSociedadEjercicio, (byte)trimestre, p.Prorrata, p.ResultadoCalculado, p.ImporteLiquidado, p.Compensado, p.Ajuste,
                string.IsNullOrWhiteSpace(motivo) ? null : Recortar(motivo.Trim(), 255), ids.Count > 0 ? ids[0] : null, Recortar(actor.ToString(), 100), _reloj.Ahora);
            // Al confirmar se activa el trimestre siguiente (propietario 2026-11-13 11:52, P2); el cuarto lo cierra el cierre del ejercicio.
            if (activo == trimestre && trimestre < 4)
            {
                await s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = ?", trimestre + 1, ctx.IdSociedadEjercicio);
                activoDespues = trimestre + 1;
            }
            return Resultado.Correcto();
        });
        var grabado = await motor.ReemplazarLoteAsync(ctx, Lote(ctx.Ejercicio, trimestre), [asiento], actor, texto, trabajo, new OpcionesLote(TrimestreLiquidado: trimestre));
        if (!grabado.EsCorrecto) return Resultado<ConfirmacionLiquidacionIva>.Fallo(grabado.Errores);
        return Resultado<ConfirmacionLiquidacionIva>.Correcto(new(grabado.Valor[0], activoDespues, creadas, p));
    }

    /// <summary>Borra el asiento de la liquidación y su fila. No reactiva el trimestre: eso se hace aparte, si se puede.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, int trimestre, string motivo, Actor actor)
    {
        var registro = $"Liquidación del IVA {CalculoLiquidacionIva.Nombre(trimestre)} {ctx.Ejercicio}";
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("LiquidacionIva.SinPermiso", "No tienes permiso para liquidar el IVA.", registro, null, "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("LiquidacionIva.SinMotivo", "Hay que escribir el motivo.", registro, "Deshacer una liquidación queda registrado con su motivo.", "Escribe el motivo."));
        var trabajo = new TrabajoLote(Despues: async (s, _) =>
        {
            await s.EjecutarAsync("DELETE FROM LiquidacionesIva WHERE IdSociedadEjercicio = ? AND Trimestre = ?", ctx.IdSociedadEjercicio, (byte)trimestre);
            return Resultado.Correcto();
        });
        return await motor.EliminarLoteAsync(ctx, Lote(ctx.Ejercicio, trimestre), actor, $"Deshacer la {registro.ToLowerInvariant()[0]}{registro[1..]}: {motivo.Trim()}", trabajo,
            new OpcionesLote(TrimestreLiquidado: trimestre));
    }

    // ---------- lecturas ----------

    /// <summary>
    /// Saldos (Debe − Haber) de las 472, 477 y 4700 hasta <paramref name="hasta"/> desde los apuntes contabilizados del ejercicio, sin
    /// el cierre y sin los asientos del <paramref name="sin"/> (la propia liquidación al repetirla).
    /// </summary>
    public Task<IReadOnlyList<SaldoSubcuenta>> SaldosAsync(ContextoSociedadEjercicio ctx, DateTime hasta, IdentidadLote? sin) => bd.EjecutarAsync<IReadOnlyList<SaldoSubcuenta>>(async s =>
    {
        var excluir = sin is null ? "" : "AND (IIF(a.MotorPropietario IS NULL, '', a.MotorPropietario) & '|' & IIF(a.CodigoProceso IS NULL, '', a.CodigoProceso) & '|' & IIF(a.ClaveEjecucion IS NULL, '', a.ClaveEjecucion)) <> ? ";
        var parametros = new List<object?> { ctx.IdSociedadEjercicio, hasta.Date };
        if (sin is not null) parametros.Add($"{sin.MotorPropietario}|{sin.CodigoProceso}|{sin.ClaveEjecucion}");
        var filas = await s.ConsultarAsync(
            "SELECT p.CuentaContable, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS Saldo " +
            "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.FechaContable <= ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 3 " +
            "AND (LEFT(p.CuentaContable, 3) IN ('472', '477') OR LEFT(p.CuentaContable, 4) = '4700') " + excluir +
            "GROUP BY p.CuentaContable",
            f => new SaldoSubcuenta((f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("Saldo") ?? 0m), [.. parametros]);
        return filas.Where(x => x.Saldo != 0m).ToList();
    });

    public Task<IReadOnlyList<LiquidacionGuardada>> GuardadasAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT Trimestre, Prorrata, ResultadoCalculado, ImporteLiquidado, Compensado, Ajuste, Motivo, IdAsiento, Operador, FechaHoraConfirmacion FROM LiquidacionesIva " +
        "WHERE IdSociedadEjercicio = ? ORDER BY Trimestre",
        f => new LiquidacionGuardada(f.Entero("Trimestre"), f.DecimalNulo("Prorrata"), f.DecimalNulo("ResultadoCalculado") ?? 0m, f.DecimalNulo("ImporteLiquidado") ?? 0m,
            f.DecimalNulo("Compensado") ?? 0m, f.DecimalNulo("Ajuste") ?? 0m, f.Texto("Motivo"), f.EnteroNulo("IdAsiento"), f.Texto("Operador"), f.Fecha("FechaHoraConfirmacion")),
        ctx.IdSociedadEjercicio));

    public Task<int?> TrimestreActivoAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var v = await s.EscalarAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio);
        return v is null or DBNull ? (int?)null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    });

    public Task<CuentasLiquidacionIva> CuentasAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync(
            "SELECT CuentaHaciendaAcreedoraIva, CuentaHaciendaDeudoraIva, CuentaAjustesNegativosIva, CuentaAjustesPositivosIva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => CuentasLiquidacionIva.Con(f.Texto("CuentaHaciendaAcreedoraIva"), f.Texto("CuentaHaciendaDeudoraIva"), f.Texto("CuentaAjustesNegativosIva"), f.Texto("CuentaAjustesPositivosIva")),
            ctx.IdSociedadEjercicio) ?? CuentasLiquidacionIva.Defecto);

    // ---------- apoyo ----------

    private static bool MismoAsiento(PropuestaLiquidacionIva a, PropuestaLiquidacionIva b) =>
        a.Lineas.Count == b.Lineas.Count && a.Lineas.Zip(b.Lineas).All(x => x.First == x.Second);

    private static string Notas(PropuestaLiquidacionIva p, string? motivo)
    {
        var partes = new List<string>
        {
            $"303 calculado: {Euros(p.ResultadoCalculado)} (devengado {Euros(p.Devengado303)}, deducible {Euros(p.Deducible303)}, prorrata {p.Prorrata * 100m:0.##} %).",
        };
        if (p.ImporteModificado) partes.Add($"Liquidado: {Euros(p.ImporteLiquidado)} (fijado a mano).");
        if (p.Compensado != 0m) partes.Add($"Compensado de trimestres anteriores: {Euros(p.Compensado)}.");
        if (p.HayAjuste) partes.Add($"Ajuste: {Euros(p.Ajuste)}.");
        if (!string.IsNullOrWhiteSpace(motivo)) partes.Add($"Motivo: {motivo.Trim()}");
        return Recortar(string.Join(" ", partes), 255);
    }

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
