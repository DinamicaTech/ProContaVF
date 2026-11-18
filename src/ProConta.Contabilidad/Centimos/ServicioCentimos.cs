using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Mantenimiento;

namespace ProConta.Contabilidad.Centimos;

/// <summary>Estado de los céntimos residuales del ejercicio.</summary>
public enum EstadoCentimos
{
    /// <summary>Sin asientos de este proceso.</summary>
    Pendiente,
    /// <summary>Los asientos grabados coinciden con lo que se propone ahora (o no había nada que saldar).</summary>
    Hecho,
    /// <summary>Hay asientos, pero lo que se propone ahora es otra cosa: hay que repetirlo.</summary>
    Desactualizado,
}

/// <summary>Situación del proceso: estado, los asientos grabados, lo que se propone ahora y por qué está desactualizado.</summary>
public sealed record SituacionCentimos(EstadoCentimos Estado, int? IdAsiento, int Asientos, decimal Grabado, PropuestaCentimos Propuesta, string? Motivo,
    bool EjercicioTerminado, bool EjercicioCerrado)
{
    /// <summary>Pendiente con algo que saldar, o desactualizado: hay que hacer algo.</summary>
    public bool FaltaAlgo => Estado == EstadoCentimos.Desactualizado || (Estado == EstadoCentimos.Pendiente && Propuesta.Asientos.Count > 0);
}

/// <summary>Resultado de confirmar: el primer asiento (null si no había nada que saldar), cuántos y las cuentas creadas.</summary>
public sealed record ConfirmacionCentimos(int? IdAsiento, int Asientos, IReadOnlyList<string> CuentasCreadas, PropuestaCentimos Propuesta);

/// <summary>
/// Proceso «Céntimos residuales» (contabilidad/procesos/centimos-residuales 2026-11-17 19:05) sobre Access. Lee el saldo a 31/12 de las
/// subcuentas del ejercicio desde los apuntes contabilizados (sin regularización, cierre ni los lotes de este proceso, así que repetirlo da
/// lo mismo), el umbral y las cuentas de redondeo del ejercicio y lo excluido a mano (CentimosResidualesExclusiones); propone con
/// <see cref="CalculoCentimos"/> y, al confirmar, graba un asiento por subcuenta en el lote del motor <see cref="Motor"/>, proceso
/// CENTIMOS, clave el año. No toca cuentas fiscales del trimestre (sin 47), así que también se graba con el 4T liquidado; nunca en un
/// ejercicio cerrado. Deshacer borra el lote.
/// </summary>
public sealed class ServicioCentimos(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, Func<Task<bool>>? puede = null, IReloj? reloj = null)
{
    public const string Motor = "contabilidad/procesos/centimos-residuales";
    public const string Proceso = "CENTIMOS";

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(int ejercicio) => new(Motor, Proceso, ejercicio.ToString(CultureInfo.InvariantCulture));

    public DateTime Hoy => _reloj.Ahora.Date;

    public bool EjercicioTerminado(ContextoSociedadEjercicio ctx) => Hoy > CalculoCentimos.Fecha(ctx.Ejercicio);

    /// <summary>Si hace falta motivo para confirmar: el ejercicio no ha terminado o ya estaba hecho (se repite).</summary>
    public static string? PorQueMotivo(bool ejercicioTerminado, bool repetir) =>
        !ejercicioTerminado ? "El ejercicio aún no ha terminado." : repetir ? "Se repite un proceso ya confirmado." : null;

    // ---------- situación ----------

    /// <summary>Lo grabado frente a lo que se propone ahora con las exclusiones guardadas.</summary>
    public async Task<SituacionCentimos> SituacionAsync(ContextoSociedadEjercicio ctx)
    {
        var propuesta = await ProponerAsync(ctx);
        var cerrado = await CerradoAsync(ctx);
        var grabados = await GrabadosAsync(ctx, propuesta.Configuracion);
        var terminado = EjercicioTerminado(ctx);
        if (grabados.Asientos.Count == 0)
            return new(propuesta.Asientos.Count == 0 && terminado ? EstadoCentimos.Hecho : EstadoCentimos.Pendiente, null, 0, 0m, propuesta, null, terminado, cerrado);
        var grabado = grabados.Asientos.Sum(a => a.Importe);
        var motivo = PropuestaCentimos.MismosAsientos(grabados.Asientos, propuesta.Asientos) ? null
            : $"Con los saldos de ahora se saldan {Plural(propuesta.Asientos.Count, "subcuenta", "subcuentas")} por {CalculoCentimos.Euros(propuesta.Total)} " +
              $"y lo grabado son {Plural(grabados.Asientos.Count, "subcuenta", "subcuentas")} por {CalculoCentimos.Euros(grabado)}.";
        return new(motivo is null ? EstadoCentimos.Hecho : EstadoCentimos.Desactualizado, grabados.IdAsiento, grabados.Asientos.Count, grabado, propuesta, motivo,
            terminado, cerrado);
    }

    // ---------- propuesta ----------

    /// <summary>Propuesta con las exclusiones <paramref name="excluidas"/> (null: las guardadas en el ejercicio).</summary>
    public async Task<PropuestaCentimos> ProponerAsync(ContextoSociedadEjercicio ctx, IReadOnlySet<string>? excluidas = null)
    {
        var cfg = await ConfiguracionAsync(ctx);
        var saldos = await SaldosAsync(ctx);
        excluidas ??= await ExcluidasAsync(ctx);
        var avisos = new List<string>();
        if (!EjercicioTerminado(ctx))
            avisos.Add($"El ejercicio aún no ha terminado: la propuesta sale de los saldos de hoy. Lo normal es hacerlo en enero, antes del cierre, con los cobros y pagos de {ctx.Ejercicio} ya contabilizados.");
        return CalculoCentimos.Calcular(ctx.Ejercicio, cfg, saldos, excluidas, avisos);
    }

    // ---------- confirmar ----------

    /// <summary>
    /// Graba exactamente la propuesta <paramref name="vista"/> (se vuelve a calcular con <paramref name="excluidas"/>; si ha cambiado, no
    /// se graba): crea las cuentas de redondeo que falten y graba un asiento por subcuenta y las exclusiones en una transacción. Sin
    /// subcuentas incluidas, borra los asientos que hubiera.
    /// </summary>
    public async Task<Resultado<ConfirmacionCentimos>> ConfirmarAsync(ContextoSociedadEjercicio ctx, IReadOnlySet<string> excluidas, string? motivo, Actor actor,
        PropuestaCentimos? vista = null)
    {
        // Sin cuenta por defecto implícita: si falta alguna del ejercicio, el proceso se detiene y dice dónde asignarla (organizacion/sociedades-ejercicios 2026-11-18).
        if (await bd.EjecutarAsync(s => CuentasEjercicio.FaltanAsync(s, ctx.IdSociedadEjercicio, "CuentaRedondeoGastos", "CuentaRedondeoIngresos")) is { } faltaCuenta)
            return Resultado<ConfirmacionCentimos>.Fallo(faltaCuenta);
        Resultado<ConfirmacionCentimos> Fallo(Error e) => Resultado<ConfirmacionCentimos>.Fallo(e);
        var registro = CalculoCentimos.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Fallo(new Error("Centimos.SinPermiso", "No tienes permiso para saldar los céntimos residuales.", registro,
                "La acción «Saldar los céntimos residuales» tiene grupos asignados (o, sin grupos, la de liquidar el IVA) y el tuyo no está entre ellos.",
                "Pídeselo a quien administra los permisos."));
        if (await CerradoAsync(ctx))
            return Fallo(new Error("Centimos.EjercicioCerrado", $"El ejercicio {ctx.Ejercicio} está cerrado.", registro,
                "Un ejercicio cerrado no admite asientos nuevos.", "Reábrelo en Contabilidad › Fin de ejercicio › Cierre del ejercicio si de verdad hay que saldar algo."));
        var p = await ProponerAsync(ctx, excluidas);
        if (vista is not null && !vista.Igual(p))
            return Fallo(new Error("Centimos.Cambiada", "La propuesta ha cambiado desde que la abriste.", registro,
                "Alguien ha contabilizado apuntes o ha cambiado el umbral o las cuentas de redondeo del ejercicio mientras la revisabas.",
                "Pulsa «Actualizar», revísala y vuelve a confirmar."));
        var anteriores = await GrabadosAsync(ctx, p.Configuracion);
        var repetir = anteriores.Asientos.Count > 0;
        if (PorQueMotivo(EjercicioTerminado(ctx), repetir) is { } porQue && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("Centimos.SinMotivo", "Hay que escribir el motivo.", registro, porQue,
                "Escribe el motivo (por ejemplo «Céntimos de cobros redondeados antes del cierre»)."));

        // Las cuentas de redondeo que falten en el ejercicio (las subcuentas saldadas ya existen: tienen apuntes).
        var creadas = new List<string>();
        foreach (var codigo in p.Incluidas.Select(f => f.Contrapartida).Distinct(StringComparer.Ordinal))
        {
            if ((await cuentas.ResolverAsync(ctx, codigo)).EsCorrecto) continue;
            var (identidad, nombre) = await CuentaDeLaEmpresaAsync(ctx, codigo, p.Configuracion);
            var alta = identidad is int id ? await cuentas.AltaEnEjercicioAsync(ctx, id, codigo, nombre) : await cuentas.AltaSubcuentaAsync(ctx, codigo, nombre);
            if (!alta.EsCorrecto) return Resultado<ConfirmacionCentimos>.Fallo(alta.Errores);
            creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}»");
        }

        // Un asiento por subcuenta (propietario 2026-11-17 19:05), todos en el lote del ejercicio.
        var asientos = new List<Asiento>();
        foreach (var x in p.Asientos)
        {
            var a = new Asiento(ctx, p.Fecha, x.Lineas[0].Concepto);
            if (!string.IsNullOrWhiteSpace(motivo)) a.Notas = $"Motivo: {motivo.Trim()}";
            foreach (var l in x.Lineas)
            {
                var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
                ap.GeneradoAutomaticamente = true;
                ap.MotivoGeneracion = "Céntimos residuales";
                a.Apuntes.Add(ap);
            }
            asientos.Add(a);
        }
        var texto = string.IsNullOrWhiteSpace(motivo) ? registro : $"{registro}: {motivo.Trim()}";
        var grabado = await motor.ReemplazarLotesAsync([new LoteAsientos(ctx, Lote(ctx.Ejercicio), asientos)], actor, texto,
            alFinal: async s => { await GuardarExcluidasAsync(s, ctx, p, actor); return Resultado.Correcto(); });
        if (!grabado.EsCorrecto) return Resultado<ConfirmacionCentimos>.Fallo(grabado.Errores);
        var ids = grabado.Valor[0];
        return Resultado<ConfirmacionCentimos>.Correcto(new(ids.Count > 0 ? ids.Min() : null, ids.Count, creadas, p));
    }

    // ---------- deshacer ----------

    /// <summary>Borra los asientos del proceso, en una transacción y con motivo. Las exclusiones se conservan para la próxima propuesta.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, string motivo, Actor actor)
    {
        var registro = CalculoCentimos.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("Centimos.SinPermiso", "No tienes permiso para saldar los céntimos residuales.", registro, null,
                "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("Centimos.SinMotivo", "Hay que escribir el motivo.", registro,
                "Deshacer los céntimos residuales queda registrado con su motivo.", "Escribe el motivo."));
        return await motor.EliminarLoteAsync(ctx, Lote(ctx.Ejercicio), actor, $"Deshacer los céntimos residuales {ctx.Ejercicio}: {motivo.Trim()}");
    }

    // ---------- lecturas ----------

    public Task<ConfiguracionCentimos> ConfiguracionAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var conUmbral = s.LeerEsquema().ExisteColumna("SociedadesEjercicios", "UmbralCentimosResiduales");
        return await s.ConsultarPrimeroAsync(
            "SELECT CuentaRedondeoGastos, CuentaRedondeoIngresos" + (conUmbral ? ", UmbralCentimosResiduales" : "") + " FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => ConfiguracionCentimos.Con(conUmbral ? f.DecimalNulo("UmbralCentimosResiduales") : null, f.Texto("CuentaRedondeoGastos"), f.Texto("CuentaRedondeoIngresos")),
            ctx.IdSociedadEjercicio) ?? ConfiguracionCentimos.Defecto;
    });

    private Task<bool> CerradoAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync("SELECT EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", f => f.Logico("EjercicioCerrado"), ctx.IdSociedadEjercicio));

    /// <summary>
    /// Saldo a 31/12 de las subcuentas de los grupos 1 a 5 con apuntes en el ejercicio: apuntes contabilizados, con la apertura, sin la
    /// regularización (TipoAsiento 2), el cierre (3) ni los lotes de este proceso.
    /// </summary>
    public Task<IReadOnlyList<SaldoSubcuenta>> SaldosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlyList<SaldoSubcuenta>>(async s =>
    {
        var saldos = await s.ConsultarAsync(
            "SELECT p.CuentaContable, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS Saldo " +
            "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 2 AND a.TipoAsiento <> 3 AND (a.MotorPropietario IS NULL OR a.MotorPropietario <> ?) " +
            "AND (p.CuentaContable LIKE '1%' OR p.CuentaContable LIKE '2%' OR p.CuentaContable LIKE '3%' OR p.CuentaContable LIKE '4%' OR p.CuentaContable LIKE '5%') " +
            "GROUP BY p.CuentaContable",
            f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Saldo: f.DecimalNulo("Saldo") ?? 0m), ctx.IdSociedadEjercicio, Motor);
        var conSaldo = saldos.Where(x => Math.Round(x.Saldo, 2, MidpointRounding.AwayFromZero) != 0m).ToList();
        if (conSaldo.Count == 0) return [];
        var nombres = (await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedadEjercicio))
            .GroupBy(x => x.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Nombre, StringComparer.Ordinal);
        return conSaldo.GroupBy(x => x.Cuenta, StringComparer.Ordinal)
            .Select(g => new SaldoSubcuenta(g.Key, nombres.GetValueOrDefault(g.Key) ?? "", g.Sum(x => x.Saldo))).ToList();
    });

    /// <summary>Subcuentas excluidas a mano en el ejercicio.</summary>
    public Task<IReadOnlySet<string>> ExcluidasAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlySet<string>>(async s =>
        !s.LeerEsquema().ExisteTabla("CentimosResidualesExclusiones") ? new HashSet<string>(StringComparer.Ordinal)
            : (await s.ConsultarAsync("SELECT CuentaContable FROM CentimosResidualesExclusiones WHERE IdSociedadEjercicio = ?",
                f => (f.Texto("CuentaContable") ?? "").Trim(), ctx.IdSociedadEjercicio)).ToHashSet(StringComparer.Ordinal));

    /// <summary>Guarda las casillas de las filas de la propuesta; las exclusiones de subcuentas que ya no salen se conservan.</summary>
    private async Task GuardarExcluidasAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, PropuestaCentimos p, Actor actor)
    {
        if (!s.LeerEsquema().ExisteTabla("CentimosResidualesExclusiones")) return;
        var ahora = new DateTime(_reloj.Ahora.Year, _reloj.Ahora.Month, _reloj.Ahora.Day, _reloj.Ahora.Hour, _reloj.Ahora.Minute, _reloj.Ahora.Second);
        foreach (var f in p.Filas)
        {
            await s.EjecutarAsync("DELETE FROM CentimosResidualesExclusiones WHERE IdSociedadEjercicio = ? AND CuentaContable = ?", ctx.IdSociedadEjercicio, f.Cuenta);
            if (!f.Incluido)
                await s.EjecutarAsync("INSERT INTO CentimosResidualesExclusiones (IdSociedadEjercicio, CuentaContable, Operador, FechaHora) VALUES (?, ?, ?, ?)",
                    ctx.IdSociedadEjercicio, f.Cuenta, Recortar(actor.ToString(), 100), ahora);
        }
    }

    /// <summary>Los asientos del lote de este proceso en el ejercicio, con sus líneas, y el primero.</summary>
    private Task<(IReadOnlyList<AsientoCentimos> Asientos, int? IdAsiento)> GrabadosAsync(ContextoSociedadEjercicio ctx, ConfiguracionCentimos cfg) =>
        bd.EjecutarAsync<(IReadOnlyList<AsientoCentimos>, int?)>(async s =>
        {
            var lote = Lote(ctx.Ejercicio);
            var filas = await s.ConsultarAsync(
                "SELECT a.IdAsiento, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.Descripcion FROM Asientos AS a LEFT JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.ClaveEjecucion = ? ORDER BY a.IdAsiento, p.Orden",
                f => (Id: f.Entero("IdAsiento"), Cuenta: f.Texto("CuentaContable")?.Trim(), Debe: f.DecimalNulo("ImporteDebe") ?? 0m, Haber: f.DecimalNulo("ImporteHaber") ?? 0m,
                    Concepto: f.Texto("Descripcion") ?? ""),
                ctx.IdSociedadEjercicio, lote.MotorPropietario, lote.CodigoProceso, lote.ClaveEjecucion);
            if (filas.Count == 0) return ([], null);
            var asientos = filas.Where(x => x.Cuenta is not null).GroupBy(x => x.Id).Select(g =>
            {
                var lineas = g.Select(x => new LineaCentimos(x.Cuenta!, x.Debe, x.Haber, x.Concepto)).ToList();
                var sub = lineas.Select(l => l.Cuenta).FirstOrDefault(c => c != cfg.CuentaGastos && c != cfg.CuentaIngresos) ?? lineas[0].Cuenta;
                return new AsientoCentimos(sub, lineas);
            }).ToList();
            return (asientos, filas.Min(x => x.Id));
        });

    /// <summary>
    /// La cuenta con ese código en otro ejercicio de la empresa (su identidad y nombre, del más reciente) para dar de alta una que falta; si
    /// no existe en ninguno, sin identidad y con el nombre de siempre para las cuentas de redondeo.
    /// </summary>
    private Task<(int? Identidad, string Nombre)> CuentaDeLaEmpresaAsync(ContextoSociedadEjercicio ctx, string codigo, ConfiguracionCentimos cfg) =>
        bd.EjecutarAsync<(int?, string)>(async s =>
        {
            var fila = await s.ConsultarPrimeroAsync(
                "SELECT TOP 1 c.IdIdentidadContable, c.Nombre FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS e ON c.IdSociedadEjercicio = e.IdSociedadEjercicio " +
                "WHERE e.IdSociedad = ? AND c.Codigo = ? ORDER BY e.Ejercicio DESC",
                f => (Identidad: f.EnteroNulo("IdIdentidadContable"), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedad, codigo);
            if (fila.Identidad is not null && fila.Nombre.Length > 0) return (fila.Identidad, fila.Nombre);
            return (null, codigo == cfg.CuentaGastos ? "Diferencias de redondeo (gastos)" : codigo == cfg.CuentaIngresos ? "Diferencias de redondeo (ingresos)" : $"Cuenta {codigo}");
        });

    private static string Recortar(string? t, int n) => t is null ? "" : t.Length <= n ? t : t[..n];

    private static string Plural(int n, string uno, string varios) => $"{n.ToString("#,##0", CultureInfo.GetCultureInfo("es-ES"))} {(n == 1 ? uno : varios)}";
}
