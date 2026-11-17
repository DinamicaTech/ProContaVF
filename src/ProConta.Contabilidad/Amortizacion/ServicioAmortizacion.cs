using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Amortizacion;

/// <summary>Estado de la amortización de un periodo.</summary>
public enum EstadoAmortizacion
{
    /// <summary>Sin asiento de este proceso.</summary>
    Pendiente,
    /// <summary>El asiento grabado coincide con lo que se propone ahora (o no había nada que amortizar y no hay asiento).</summary>
    Hecho,
    /// <summary>Hay asiento, pero lo que se propone ahora es otra cosa: hay que repetirlo.</summary>
    Desactualizado,
}

/// <summary>Un periodo del ejercicio con su estado, el asiento grabado (si lo hay) y lo que se propone ahora.</summary>
public sealed record SituacionPeriodoAmortizacion(PeriodoAmortizacion Periodo, EstadoAmortizacion Estado, int? IdAsiento, decimal Grabado, PropuestaAmortizacion Propuesta,
    string? Motivo, bool Terminado)
{
    /// <summary>Pendiente con algo que amortizar, o desactualizado: hay que hacer algo.</summary>
    public bool FaltaAlgo => Estado == EstadoAmortizacion.Desactualizado || (Estado == EstadoAmortizacion.Pendiente && Propuesta.Total > 0m);
}

/// <summary>Situación del ejercicio: periodicidad, periodos y si está cerrado.</summary>
public sealed record SituacionAmortizacion(int Ejercicio, PeriodicidadAmortizacion Periodicidad, IReadOnlyList<SituacionPeriodoAmortizacion> Periodos, bool EjercicioCerrado,
    int Activos);

/// <summary>
/// Proceso «Amortización de activos» (contabilidad/procesos/amortizacion-de-activos 2026-11-17 17:59) sobre Access. Lee los activos
/// amortizables y propios con su plan y sus subcuentas de gasto (68x) y de amortización acumulada (28x) en el ejercicio, los apuntes de
/// esas subcuentas en el año y lo excluido a mano (AmortizacionExclusiones); propone por periodo (<see cref="ProcesoAmortizacion"/>) y, al
/// confirmar, graba un asiento por activo en el lote del periodo del motor <see cref="Motor"/>, proceso AMORTIZACION, clave el periodo. Repetirlo no
/// duplica; deshacer borra el lote del periodo. Las 68 y 28 no son cuentas fiscales, así que también se graba en un trimestre cerrado.
/// </summary>
public sealed class ServicioAmortizacion(IBaseDatos bd, IMotorAsientos motor, Func<Task<bool>>? puede = null, IReloj? reloj = null)
{
    public const string Motor = "contabilidad/procesos/amortizacion-de-activos";
    public const string Proceso = "AMORTIZACION";
    public const string RolGasto = "GASTO_AMORTIZACION";
    public const string RolAcumulada = "AMORTIZACION_ACUMULADA";

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(PeriodoAmortizacion p) => new(Motor, Proceso, p.Clave);

    public DateTime Hoy => _reloj.Ahora.Date;

    /// <summary>Si hace falta motivo para confirmar: el periodo no ha terminado o ya estaba hecho (se repite).</summary>
    public static string? PorQueMotivo(bool terminado, bool repetir) =>
        !terminado ? "El periodo aún no ha terminado." : repetir ? "Se repite una amortización ya confirmada." : null;

    // ---------- situación ----------

    /// <summary>Todos los periodos del ejercicio con su estado y su propuesta (los datos se leen una vez).</summary>
    public async Task<SituacionAmortizacion> SituacionAsync(ContextoSociedadEjercicio ctx)
    {
        var d = await LeerAsync(ctx);
        var grabados = await GrabadosAsync(ctx);
        var periodos = PeriodoAmortizacion.DelEjercicio(ctx.Ejercicio, d.Periodicidad);
        var r = new List<SituacionPeriodoAmortizacion>();
        foreach (var p in periodos)
        {
            var propuesta = ProcesoAmortizacion.Proponer(p, d.Activos, d.Movimientos, d.Excluidos, Avisos(p));
            var terminado = Hoy > p.Hasta;
            if (!grabados.TryGetValue(p.Clave, out var g))
            {
                r.Add(new(p, propuesta.Lineas.Count == 0 && terminado && d.Activos.Count > 0 ? EstadoAmortizacion.Hecho : EstadoAmortizacion.Pendiente,
                    null, 0m, propuesta, null, terminado));
                continue;
            }
            var grabado = g.Lineas.Sum(l => l.Debe);
            var motivo = PropuestaAmortizacion.MismasLineas(g.Lineas, propuesta.Lineas) ? null
                : $"Con los datos de ahora la amortización de {p.Nombre} es de {Euros(propuesta.Total)} y la grabada, de {Euros(grabado)}" +
                  (propuesta.Total == grabado ? ", con otros activos, subcuentas o centros." : ".");
            r.Add(new(p, motivo is null ? EstadoAmortizacion.Hecho : EstadoAmortizacion.Desactualizado, g.IdAsiento, grabado, propuesta, motivo, terminado));
        }
        // Lotes con otra periodicidad (se cambió el parámetro): siguen contando como contabilizados, pero se avisa en el periodo que los contiene.
        return new SituacionAmortizacion(ctx.Ejercicio, d.Periodicidad, r, d.Cerrado, d.Activos.Count(a => a.Problema is null));
    }

    private IEnumerable<string> Avisos(PeriodoAmortizacion p)
    {
        if (Hoy <= p.Hasta)
            yield return $"El periodo {p.Nombre} aún no ha terminado (acaba el {p.Hasta:dd/MM/yyyy}): la cuota se calcula hasta esa fecha igualmente.";
    }

    /// <summary>Propuesta de un periodo con las casillas <paramref name="excluidos"/> (null: las guardadas).</summary>
    public async Task<PropuestaAmortizacion> ProponerAsync(ContextoSociedadEjercicio ctx, PeriodoAmortizacion periodo, IReadOnlySet<int>? excluidos = null)
    {
        var d = await LeerAsync(ctx);
        return ProcesoAmortizacion.Proponer(periodo, d.Activos, d.Movimientos, excluidos ?? d.Excluidos, Avisos(periodo));
    }

    /// <summary>
    /// Lo que falta por contabilizar de la amortización del año, por activo, para la ficha de activos (activos 2026-11-17 18:50: fila
    /// «Amortización prevista (sin contabilizar)»): la propuesta del periodo anual del ejercicio con el mismo cálculo del proceso, pero contando
    /// todo lo ya cargado en el año, también lo de este proceso; así es cero cuando la amortización del año ya está contabilizada del todo. Solo
    /// salen los activos que tocan este año y no tienen problema; las casillas desmarcadas del proceso no cuentan (la ficha muestra la cuota).
    /// </summary>
    public async Task<IReadOnlyDictionary<int, CuotaAmortizacion>> PrevistaAnualAsync(ContextoSociedadEjercicio ctx)
    {
        var d = await LeerAsync(ctx);
        var anual = PeriodoAmortizacion.DelEjercicio(ctx.Ejercicio, PeriodicidadAmortizacion.Anual)[0];
        var movimientos = d.Movimientos.Select(m => m with { ClaveLote = null }).ToList();
        var p = ProcesoAmortizacion.Proponer(anual, d.Activos, movimientos, new HashSet<int>());
        return p.Filas.Where(f => f.Cuota is not null).ToDictionary(f => f.Activo.IdActivo, f => f.Cuota!);
    }

    // ---------- confirmar ----------

    /// <summary>
    /// Graba exactamente la propuesta <paramref name="vista"/> del periodo (se vuelve a calcular con <paramref name="excluidos"/>; si ha
    /// cambiado, no se graba) y guarda las casillas en la misma transacción. Sin nada que amortizar, borra el asiento que hubiera.
    /// </summary>
    public async Task<Resultado<int?>> ConfirmarAsync(ContextoSociedadEjercicio ctx, PeriodoAmortizacion periodo, IReadOnlySet<int> excluidos, string? motivo, Actor actor,
        PropuestaAmortizacion? vista = null)
    {
        Resultado<int?> Fallo(Error e) => Resultado<int?>.Fallo(e);
        var registro = ProcesoAmortizacion.Descripcion(periodo);
        if (puede is not null && !await puede())
            return Fallo(new Error("Amortizacion.SinPermiso", "No tienes permiso para amortizar los activos.", registro,
                "La acción «Amortizar los activos» tiene grupos asignados (o, sin grupos, la de liquidar el IVA) y el tuyo no está entre ellos.",
                "Pídeselo a quien administra los permisos."));
        var p = await ProponerAsync(ctx, periodo, excluidos);
        if (vista is not null && !vista.Igual(p))
            return Fallo(new Error("Amortizacion.Cambiada", "La propuesta ha cambiado desde que la abriste.", registro,
                "Alguien ha contabilizado apuntes en las subcuentas de los activos o ha cambiado un activo, su plan o sus subcuentas mientras la revisabas.",
                "Pulsa «Actualizar», revísala y vuelve a confirmar."));
        var grabados = await GrabadosAsync(ctx);
        var repetir = grabados.ContainsKey(periodo.Clave);
        if (PorQueMotivo(Hoy > periodo.Hasta, repetir) is { } porQue && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("Amortizacion.SinMotivo", "Hay que escribir el motivo.", registro, porQue,
                "Escribe el motivo (por ejemplo «Amortización anticipada para el informe de la gestoría»)."));

        // Un asiento por activo (propietario 2026-11-17 18:24): las líneas van por pares (68x al Debe y 28x al Haber de cada activo), todas en el lote del periodo.
        var asientos = new List<Asiento>();
        for (var i = 0; i + 1 < p.Lineas.Count; i += 2)
        {
            var par = new[] { p.Lineas[i], p.Lineas[i + 1] };
            var a = new Asiento(ctx, periodo.Hasta, par[0].Concepto);
            if (!string.IsNullOrWhiteSpace(motivo)) a.Notas = $"Motivo: {motivo.Trim()}";
            foreach (var l in par)
            {
                var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
                ap.IdCentroCoste = l.IdCentroCoste;
                ap.GeneradoAutomaticamente = true;
                ap.MotivoGeneracion = "Amortización de activos";
                a.Apuntes.Add(ap);
            }
            asientos.Add(a);
        }
        var texto = string.IsNullOrWhiteSpace(motivo) ? registro : $"{registro}: {motivo.Trim()}";
        var grabado = await motor.ReemplazarLotesAsync([new LoteAsientos(ctx, Lote(periodo), asientos)], actor, texto,
            alFinal: async s => { await GuardarExcluidosAsync(s, ctx, p, actor); return Resultado.Correcto(); });
        if (!grabado.EsCorrecto) return Resultado<int?>.Fallo(grabado.Errores);
        return Resultado<int?>.Correcto(grabado.Valor[0].Count > 0 ? grabado.Valor[0][0] : null);
    }

    // ---------- deshacer ----------

    /// <summary>Borra el asiento del periodo, con motivo. Las casillas guardadas se conservan.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, PeriodoAmortizacion periodo, string motivo, Actor actor)
    {
        var registro = ProcesoAmortizacion.Descripcion(periodo);
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("Amortizacion.SinPermiso", "No tienes permiso para amortizar los activos.", registro, null,
                "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("Amortizacion.SinMotivo", "Hay que escribir el motivo.", registro,
                "Deshacer la amortización queda registrado con su motivo.", "Escribe el motivo."));
        return await motor.EliminarLoteAsync(ctx, Lote(periodo), actor, $"Deshacer la amortización de activos de {periodo.Nombre}: {motivo.Trim()}");
    }

    // ---------- lecturas ----------

    private sealed record Datos(PeriodicidadAmortizacion Periodicidad, bool Cerrado, IReadOnlyList<ActivoAmortizacion> Activos, IReadOnlyList<MovimientoAmortizacion> Movimientos,
        IReadOnlySet<int> Excluidos);

    public Task<PeriodicidadAmortizacion> PeriodicidadAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        !s.LeerEsquema().ExisteColumna("SociedadesEjercicios", "PeriodicidadAmortizacion") ? PeriodicidadAmortizacion.Anual
            : PeriodoAmortizacion.Leer(await s.EscalarAsync("SELECT PeriodicidadAmortizacion FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio) as string));

    private Task<Datos> LeerAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var esquema = s.LeerEsquema();
        var fila = await s.ConsultarPrimeroAsync(
            "SELECT EjercicioCerrado" + (esquema.ExisteColumna("SociedadesEjercicios", "PeriodicidadAmortizacion") ? ", PeriodicidadAmortizacion" : "") +
            " FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => (Cerrado: f.Logico("EjercicioCerrado"), Periodicidad: esquema.ExisteColumna("SociedadesEjercicios", "PeriodicidadAmortizacion") ? f.Texto("PeriodicidadAmortizacion") : null),
            ctx.IdSociedadEjercicio);
        var periodicidad = PeriodoAmortizacion.Leer(fila.Periodicidad);
        var inicio = new DateTime(ctx.Ejercicio, 1, 1);

        var activos = await s.ConsultarAsync(
            "SELECT IdActivo, Codigo, Nombre, IdCentroCoste, FechaBaja FROM Activos WHERE IdSociedad = ? AND EsAmortizable = True AND EsAjeno = False AND (FechaBaja IS NULL OR FechaBaja >= ?)",
            f => (Id: f.Entero("IdActivo"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim(), Centro: f.EnteroNulo("IdCentroCoste"), Baja: f.FechaNula("FechaBaja")),
            ctx.IdSociedad, inicio);
        if (activos.Count == 0) return new Datos(periodicidad, fila.Cerrado, [], [], new HashSet<int>());
        var ids = activos.Select(a => a.Id).ToHashSet();

        // Plan activo de cada activo: el de fecha de inicio más reciente.
        var planes = (await s.ConsultarAsync(
                "SELECT IdActivo, FechaInicio, BaseAmortizable, ValorResidual, PorcentajeAnual FROM PlanesAmortizacionActivo WHERE EstaActivo = True",
                f => (Id: f.Entero("IdActivo"), Inicio: f.Fecha("FechaInicio"), Base: f.DecimalNulo("BaseAmortizable") ?? 0m, Residual: f.DecimalNulo("ValorResidual") ?? 0m,
                    Pct: f.DecimalNulo("PorcentajeAnual"))))
            .Where(p => ids.Contains(p.Id)).GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.Inicio).First());

        // Subcuentas de los roles de amortización, por su identidad, con el código que tienen en este ejercicio.
        var roles = await s.ConsultarAsync(
            "SELECT sa.IdActivo, t.Rol, sa.IdIdentidadContable FROM (SubcuentasActivo AS sa INNER JOIN PlantillasSubcuentasActivo AS p ON sa.IdPlantillaSubcuentaActivo = p.IdPlantillaSubcuentaActivo) " +
            "INNER JOIN TiposSubcuentaActivo AS t ON p.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo WHERE t.Rol = ? OR t.Rol = ?",
            f => (Id: f.Entero("IdActivo"), Rol: (f.Texto("Rol") ?? "").Trim(), Identidad: f.Entero("IdIdentidadContable")), RolGasto, RolAcumulada);
        var codigos = (await s.ConsultarAsync("SELECT Codigo, IdIdentidadContable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND EsImputable = True AND IdIdentidadContable IS NOT NULL",
                f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Identidad: f.Entero("IdIdentidadContable")), ctx.IdSociedadEjercicio))
            .GroupBy(c => c.Identidad).ToDictionary(g => g.Key, g => g.Select(c => c.Codigo).Min(StringComparer.Ordinal)!);
        string? Cuenta(int id, string rol) => roles.Where(r => r.Id == id && r.Rol == rol).Select(r => codigos.GetValueOrDefault(r.Identidad)).Where(c => c is not null)
            .Order(StringComparer.Ordinal).FirstOrDefault();
        bool TieneRol(int id, string rol) => roles.Any(r => r.Id == id && r.Rol == rol);

        var centros = (await s.ConsultarAsync("SELECT IdCentroCoste FROM CentrosCoste WHERE IdSociedad = ? AND EstaActivo = True AND EsImputable = True",
            f => f.Entero("IdCentroCoste"), ctx.IdSociedad)).ToHashSet();

        var lista = new List<ActivoAmortizacion>();
        foreach (var a in activos)
        {
            var gasto = Cuenta(a.Id, RolGasto);
            var acumulada = Cuenta(a.Id, RolAcumulada);
            PlanAmortizacion? plan = planes.TryGetValue(a.Id, out var pl) && pl.Pct is decimal pct ? new PlanAmortizacion(pl.Inicio, pl.Base, pl.Residual, pct, a.Baja) : null;
            string? problema =
                !planes.ContainsKey(a.Id) ? "no tiene plan de amortización activo"
                : plan is null || plan.PorcentajeAnual <= 0m ? "su plan no tiene % anual"
                : plan.Base <= 0m ? "su plan no tiene base amortizable"
                : gasto is null ? (TieneRol(a.Id, RolGasto) ? $"su subcuenta de gasto por amortización (68) no existe en {ctx.Ejercicio}: créala con «Generar subcuentas» de su tipo de activo"
                    : "no tiene subcuenta de gasto por amortización (68): añádela a la plantilla de su tipo y usa «Generar subcuentas»")
                : acumulada is null ? (TieneRol(a.Id, RolAcumulada) ? $"su subcuenta de amortización acumulada (28) no existe en {ctx.Ejercicio}: créala con «Generar subcuentas» de su tipo de activo"
                    : "no tiene subcuenta de amortización acumulada (28): añádela a la plantilla de su tipo y usa «Generar subcuentas»")
                : null;
            // Un activo con plan que termina antes del ejercicio o empieza después no es un problema: simplemente no toca.
            lista.Add(new ActivoAmortizacion(a.Id, a.Codigo, a.Nombre, a.Centro is int c && centros.Contains(c) ? c : null, plan, gasto, acumulada, problema));
        }

        var cuentas = lista.SelectMany(a => new[] { a.CuentaGasto, a.CuentaAcumulada }).Where(c => c is not null).ToHashSet(StringComparer.Ordinal);
        var movimientos = (await s.ConsultarAsync(
                "SELECT p.CuentaContable, a.FechaContable, p.ImporteDebe, p.ImporteHaber, a.MotorPropietario, a.CodigoProceso, a.ClaveEjecucion " +
                "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 2 AND a.TipoAsiento <> 3 " +
                "AND (p.CuentaContable LIKE '68%' OR p.CuentaContable LIKE '28%')",
                f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Fecha: f.Fecha("FechaContable"), Debe: f.DecimalNulo("ImporteDebe") ?? 0m, Haber: f.DecimalNulo("ImporteHaber") ?? 0m,
                    Motor: f.Texto("MotorPropietario"), Proceso: f.Texto("CodigoProceso"), Clave: f.Texto("ClaveEjecucion")),
                ctx.IdSociedadEjercicio))
            .Where(m => cuentas.Contains(m.Cuenta))
            .Select(m => new MovimientoAmortizacion(m.Cuenta, m.Fecha, m.Debe, m.Haber, m.Motor == Motor && m.Proceso == Proceso ? m.Clave : null))
            .ToList();

        var excluidos = esquema.ExisteTabla("AmortizacionExclusiones")
            ? (await s.ConsultarAsync("SELECT IdActivo FROM AmortizacionExclusiones WHERE IdSociedadEjercicio = ?", f => f.Entero("IdActivo"), ctx.IdSociedadEjercicio)).ToHashSet()
            : new HashSet<int>();
        return new Datos(periodicidad, fila.Cerrado, lista, movimientos, excluidos);
    });

    /// <summary>Los lotes de este proceso en el ejercicio: clave → primer asiento, número de asientos y sus líneas.</summary>
    private Task<Dictionary<string, (int IdAsiento, IReadOnlyList<LineaAmortizacion> Lineas)>> GrabadosAsync(ContextoSociedadEjercicio ctx) =>
        bd.EjecutarAsync(async s =>
        {
            var filas = await s.ConsultarAsync(
                "SELECT a.IdAsiento, a.ClaveEjecucion, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.Descripcion, p.IdCentroCoste FROM Asientos AS a LEFT JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.MotorPropietario = ? AND a.CodigoProceso = ? ORDER BY a.IdAsiento, p.Orden",
                f => (Id: f.Entero("IdAsiento"), Clave: (f.Texto("ClaveEjecucion") ?? "").Trim(), Cuenta: f.Texto("CuentaContable")?.Trim(), Debe: f.DecimalNulo("ImporteDebe") ?? 0m,
                    Haber: f.DecimalNulo("ImporteHaber") ?? 0m, Concepto: f.Texto("Descripcion") ?? "", Centro: f.EnteroNulo("IdCentroCoste")),
                ctx.IdSociedadEjercicio, Motor, Proceso);
            return filas.GroupBy(x => x.Clave, StringComparer.Ordinal).ToDictionary(g => g.Key,
                g => (g.Min(x => x.Id), (IReadOnlyList<LineaAmortizacion>)g.Where(x => x.Cuenta is not null)
                    .Select(x => new LineaAmortizacion(x.Cuenta!, x.Debe, x.Haber, x.Concepto, x.Centro)).ToList()), StringComparer.Ordinal);
        });

    /// <summary>Guarda las casillas desmarcadas de la propuesta (las del ejercicio, no por periodo).</summary>
    private async Task GuardarExcluidosAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, PropuestaAmortizacion p, Actor actor)
    {
        if (!s.LeerEsquema().ExisteTabla("AmortizacionExclusiones")) return;
        // Solo se tocan los activos que salen en esta propuesta; los excluidos que no tocan este periodo se conservan.
        var ahora = new DateTime(_reloj.Ahora.Year, _reloj.Ahora.Month, _reloj.Ahora.Day, _reloj.Ahora.Hour, _reloj.Ahora.Minute, _reloj.Ahora.Second);
        foreach (var f in p.Filas.Where(f => !f.ConProblema))
        {
            await s.EjecutarAsync("DELETE FROM AmortizacionExclusiones WHERE IdSociedadEjercicio = ? AND IdActivo = ?", ctx.IdSociedadEjercicio, f.Activo.IdActivo);
            if (!f.Incluido)
                await s.EjecutarAsync("INSERT INTO AmortizacionExclusiones (IdSociedadEjercicio, IdActivo, Operador, FechaHora) VALUES (?, ?, ?, ?)",
                    ctx.IdSociedadEjercicio, f.Activo.IdActivo, Recortar(actor.ToString(), 100), ahora);
        }
    }

    /// <summary>Nombres de los centros de coste de la empresa, para enseñarlos en la propuesta.</summary>
    public Task<IReadOnlyDictionary<int, string>> NombresCentrosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlyDictionary<int, string>>(async s =>
        (await s.ConsultarAsync("SELECT IdCentroCoste, Nombre FROM CentrosCoste WHERE IdSociedad = ?", f => (Id: f.Entero("IdCentroCoste"), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedad))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Nombre));

    public static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";

    private static string Recortar(string? t, int n) => t is null ? "" : t.Length <= n ? t : t[..n];
}
