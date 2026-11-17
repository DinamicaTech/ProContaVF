using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.FacturasPendientes;
using ProConta.Contabilidad.Impuestos;
using ProConta.Contabilidad.Iva;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Cierre;

/// <summary>Cómo queda una comprobación previa al cierre.</summary>
public enum ResultadoComprobacion
{
    Correcta,
    /// <summary>Falta algo: se puede cerrar igualmente, con motivo.</summary>
    Aviso,
    /// <summary>Impide cerrar (solo el ejercicio siguiente que falta).</summary>
    Bloqueo,
    /// <summary>No aplica a esta sociedad o el proceso aún no existe.</summary>
    NoAplica,
}

/// <summary>Pantalla que resuelve una comprobación (la abre la interfaz).</summary>
public enum DestinoComprobacion
{
    Ninguno,
    CrearEjercicio,
    ImpuestoSociedades,
    Amortizacion,
    LiquidacionIva,
    Prorrata,
    FacturasPendientes,
    CentimosResiduales,
    Diario,
    Salud,
}

/// <summary>Una comprobación previa al cierre (validación V5): qué se mira, cómo está y dónde se resuelve.</summary>
public sealed record ComprobacionCierre(string Clave, string Titulo, ResultadoComprobacion Resultado, string Detalle, DestinoComprobacion Destino = DestinoComprobacion.Ninguno);

/// <summary>
/// Situación del cierre del ejercicio: si está cerrado, el ejercicio siguiente, los asientos del cierre (regularización, cierre y apertura),
/// quién y cuándo lo cerró y los asientos de regularización, cierre o apertura hechos fuera de este proceso (impiden cerrar).
/// </summary>
public sealed record SituacionCierre(int Ejercicio, bool Cerrado, ContextoSociedadEjercicio? Siguiente, bool SiguienteCerrado, int? IdRegularizacion, int? IdCierre,
    int? IdApertura, DateTime? FechaHoraCierre, string? Operador, IReadOnlyList<string> Ajenos)
{
    /// <summary>Tiene los asientos de este proceso.</summary>
    public bool TieneAsientos => IdRegularizacion is not null || IdCierre is not null || IdApertura is not null;
}

/// <summary>Resultado de cerrar: los tres asientos (null si no había nada que saldar), las cuentas creadas y las copiadas al ejercicio siguiente.</summary>
public sealed record ConfirmacionCierre(int? IdRegularizacion, int? IdCierre, int? IdApertura, IReadOnlyList<string> CuentasCreadas, int CuentasCopiadas, PropuestaCierre Propuesta);

/// <summary>
/// Proceso «Cierre del ejercicio» (contabilidad/procesos/cierre-ejercicio 2026-11-17) sobre Access. Comprueba antes lo que suele faltar
/// (validación V5: avisa, no impide), propone la regularización, el cierre y la apertura del ejercicio siguiente desde los apuntes
/// contabilizados (<see cref="CalculoCierreEjercicio"/>) y, al confirmar, graba los tres asientos como lotes propios (motor
/// <see cref="OpcionesLote.MotorCierreEjercicio"/>, procesos REGULARIZACION y CIERRE en el ejercicio y APERTURA en el siguiente, clave el
/// año que se cierra) y marca el ejercicio como cerrado, todo en una transacción. Reabrir quita la marca y borra solo esos lotes, también
/// en una transacción. Repetir el cierre no duplica nada. El ejercicio siguiente tiene que existir (contabilidad/procesos/nuevo-ejercicio).
/// </summary>
public sealed class ServicioCierreEjercicio(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, ServicioNuevoEjercicio nuevoEjercicio,
    Func<Task<bool>>? puede = null, IReloj? reloj = null, Func<ContextoSociedadEjercicio, Task<IReadOnlyList<string>>>? avisosGraves = null)
{
    public const string Motor = OpcionesLote.MotorCierreEjercicio;
    public const string ProcesoRegularizacion = "REGULARIZACION";
    public const string ProcesoCierre = "CIERRE";
    public const string ProcesoApertura = "APERTURA";

    private static readonly OpcionesLote Opciones = new(CierreEjercicio: true);
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(string proceso, int ejercicio) => new(Motor, proceso, ejercicio.ToString(CultureInfo.InvariantCulture));

    public DateTime Hoy => _reloj.Ahora.Date;

    public bool EjercicioTerminado(ContextoSociedadEjercicio ctx) => Hoy > CalculoCierreEjercicio.FechaCierre(ctx.Ejercicio);

    public ServicioNuevoEjercicio NuevoEjercicio => nuevoEjercicio;

    // ---------- situación ----------

    public Task<SituacionCierre> SituacionAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var cerrado = await CerradoAsync(s, ctx.IdSociedadEjercicio);
        var siguiente = await SiguienteAsync(s, ctx);
        var siguienteCerrado = siguiente is not null && await CerradoAsync(s, siguiente.IdSociedadEjercicio);
        var clave = ctx.Ejercicio.ToString(CultureInfo.InvariantCulture);
        var propios = await s.ConsultarAsync(
            "SELECT IdAsiento, IdSociedadEjercicio, CodigoProceso, FechaHoraCreacion, OperadorCreacion FROM Asientos " +
            "WHERE MotorPropietario = ? AND ClaveEjecucion = ? AND (IdSociedadEjercicio = ? OR IdSociedadEjercicio = ?) ORDER BY IdAsiento",
            f => (Id: f.Entero("IdAsiento"), Ejercicio: f.Entero("IdSociedadEjercicio"), Proceso: (f.Texto("CodigoProceso") ?? "").Trim(),
                Fecha: f.FechaNula("FechaHoraCreacion"), Operador: f.Texto("OperadorCreacion")),
            Motor, clave, ctx.IdSociedadEjercicio, siguiente?.IdSociedadEjercicio ?? -1);
        int? Id(string proceso, int ejercicio) => propios.Where(p => p.Proceso == proceso && p.Ejercicio == ejercicio).Select(p => (int?)p.Id).FirstOrDefault();
        var primero = propios.Where(p => p.Ejercicio == ctx.IdSociedadEjercicio).OrderBy(p => p.Id).Select(p => ((DateTime? Fecha, string? Operador)?)(p.Fecha, p.Operador)).FirstOrDefault();
        return new SituacionCierre(ctx.Ejercicio, cerrado, siguiente, siguienteCerrado, Id(ProcesoRegularizacion, ctx.IdSociedadEjercicio), Id(ProcesoCierre, ctx.IdSociedadEjercicio),
            siguiente is null ? null : Id(ProcesoApertura, siguiente.IdSociedadEjercicio), primero?.Fecha, primero?.Operador, await AjenosAsync(s, ctx, siguiente));
    });

    /// <summary>
    /// Asientos de regularización o cierre del ejercicio, o de apertura del siguiente, que no son de este proceso (los de ContaSol, por
    /// ejemplo): con ellos el cierre saldría doble, así que impiden cerrar.
    /// </summary>
    private static async Task<IReadOnlyList<string>> AjenosAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, ContextoSociedadEjercicio? siguiente)
    {
        const string NoEsDelCierre = "(MotorPropietario IS NULL OR MotorPropietario <> ?)";
        var filas = (await s.ConsultarAsync(
            $"SELECT IdAsiento, FechaContable, Descripcion FROM Asientos WHERE IdSociedadEjercicio = ? AND EstadoAsiento <> 2 AND TipoAsiento IN (2, 3) AND {NoEsDelCierre} ORDER BY IdAsiento",
            Texto, ctx.IdSociedadEjercicio, Motor)).ToList();
        if (siguiente is not null)
            filas.AddRange(await s.ConsultarAsync(
                $"SELECT IdAsiento, FechaContable, Descripcion FROM Asientos WHERE IdSociedadEjercicio = ? AND EstadoAsiento <> 2 AND TipoAsiento = 1 AND {NoEsDelCierre} ORDER BY IdAsiento",
                Texto, siguiente.IdSociedadEjercicio, Motor));
        return filas;
        static string Texto(IFilaDatos f) => $"asiento {f.Entero("IdAsiento")} del {f.Fecha("FechaContable"):dd/MM/yyyy}, «{(f.Texto("Descripcion") ?? "").Trim()}»";
    }

    // ---------- comprobación previa (V5) ----------

    /// <summary>
    /// Comprobación previa al cierre (validación V5, contabilidad 2026-11-15 21:10): el ejercicio siguiente (impide cerrar si falta), el
    /// Impuesto sobre Sociedades, la amortización, la regularización de la prorrata, la liquidación del IVA del 4T, las facturas pendientes
    /// de recibir, los asientos en borrador y los avisos graves de la salud. Lo demás avisa, no impide.
    /// </summary>
    public async Task<IReadOnlyList<ComprobacionCierre>> ComprobarAsync(ContextoSociedadEjercicio ctx)
    {
        var r = new List<ComprobacionCierre>();
        var siguiente = await nuevoEjercicio.SiguienteAsync(ctx);
        r.Add(siguiente is null
            ? new("ejercicio-siguiente", $"Ejercicio {ctx.Ejercicio + 1}", ResultadoComprobacion.Bloqueo,
                $"No existe: la apertura no se puede grabar. Créalo con «Crear el ejercicio {ctx.Ejercicio + 1}…».", DestinoComprobacion.CrearEjercicio)
            : new("ejercicio-siguiente", $"Ejercicio {ctx.Ejercicio + 1}", ResultadoComprobacion.Correcta, "Creado: recibirá la apertura."));

        r.Add(await ComprobarImpuestoAsync(ctx));
        r.Add(await ComprobarAmortizacionAsync(ctx));
        r.Add(await ComprobarProrrataAsync(ctx));
        r.Add(await ComprobarLiquidacionAsync(ctx));
        r.Add(await ComprobarFacturasPendientesAsync(ctx));
        r.Add(await ComprobarCentimosAsync(ctx));

        var borradores = await bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(
            "SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = ? AND EstadoAsiento = 0", ctx.IdSociedadEjercicio), CultureInfo.InvariantCulture));
        r.Add(borradores == 0
            ? new("borradores", "Asientos en borrador", ResultadoComprobacion.Correcta, "No hay ninguno.")
            : new("borradores", "Asientos en borrador", ResultadoComprobacion.Aviso,
                $"{Plural(borradores, "asiento", "asientos")} en borrador de {ctx.Ejercicio}: no entran en el cierre y, cerrado el ejercicio, ya no se podrán contabilizar.",
                DestinoComprobacion.Diario));

        if (avisosGraves is not null)
        {
            var graves = await avisosGraves(ctx);
            r.Add(graves.Count == 0
                ? new("salud", "Salud de la contabilidad", ResultadoComprobacion.Correcta, "Sin avisos graves.")
                : new("salud", "Salud de la contabilidad", ResultadoComprobacion.Aviso,
                    $"{Plural(graves.Count, "aviso grave", "avisos graves")}: {string.Join("; ", graves.Take(3))}{(graves.Count > 3 ? "…" : "")}.", DestinoComprobacion.Salud));
        }
        return r;
    }

    private async Task<ComprobacionCierre> ComprobarImpuestoAsync(ContextoSociedadEjercicio ctx)
    {
        const string Titulo = "Impuesto sobre Sociedades";
        var situacion = await new ServicioImpuestoSociedades(bd, motor, cuentas, null, _reloj).SituacionAsync(ctx);
        if (situacion.Estado == EstadoImpuestoSociedades.Contabilizado && situacion.Guardado is { } g)
            return new("impuesto", Titulo, ResultadoComprobacion.Correcta, $"Contabilizado a 31/12: cuota de {CalculoCierreEjercicio.Euros(g.CuotaLiquida)}.", DestinoComprobacion.ImpuestoSociedades);
        if (situacion.Estado == EstadoImpuestoSociedades.Desactualizado)
            return new("impuesto", Titulo, ResultadoComprobacion.Aviso, $"Desactualizado: {situacion.Motivo} Repítelo antes de cerrar.", DestinoComprobacion.ImpuestoSociedades);
        var fuera = await bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(
            "SELECT COUNT(*) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento NOT IN (1, 2, 3) AND LEFT(p.CuentaContable, 3) = '630'", ctx.IdSociedadEjercicio),
            CultureInfo.InvariantCulture));
        return fuera > 0
            ? new("impuesto", Titulo, ResultadoComprobacion.Correcta, "Hay asiento con la 630, hecho fuera del proceso del impuesto.", DestinoComprobacion.ImpuestoSociedades)
            : new("impuesto", Titulo, ResultadoComprobacion.Aviso, $"Sin contabilizar: el resultado de {ctx.Ejercicio} irá a la 129 sin el gasto por impuesto.",
                DestinoComprobacion.ImpuestoSociedades);
    }

    /// <summary>
    /// Amortización del ejercicio (contabilidad/procesos/amortizacion-de-activos 2026-11-17 17:59): el estado real de cada periodo del
    /// proceso. Sin activos que puedan amortizarse con el proceso, como antes: mira si hay algún cargo en las 68.
    /// </summary>
    private async Task<ComprobacionCierre> ComprobarAmortizacionAsync(ContextoSociedadEjercicio ctx)
    {
        const string Titulo = "Amortización del ejercicio";
        var situacion = await new ProConta.Contabilidad.Amortizacion.ServicioAmortizacion(bd, motor, null, _reloj).SituacionAsync(ctx);
        if (situacion.Activos == 0) return await ComprobarCargosAmortizacionAsync(ctx);
        var faltan = situacion.Periodos.Where(p => p.FaltaAlgo).ToList();
        var grabado = situacion.Periodos.Sum(p => p.Grabado);
        if (faltan.Count == 0)
            return new ComprobacionCierre("amortizacion", Titulo, ResultadoComprobacion.Correcta,
                grabado == 0m ? "Hecha: no había nada que amortizar." : $"Hecha: {CalculoCierreEjercicio.Euros(grabado)} contabilizados en {Plural(situacion.Periodos.Count(p => p.IdAsiento is not null), "periodo", "periodos")} (un asiento por activo).",
                DestinoComprobacion.Amortizacion);
        var desactualizados = faltan.Where(p => p.Estado == ProConta.Contabilidad.Amortizacion.EstadoAmortizacion.Desactualizado).Select(p => p.Periodo.Nombre).ToList();
        var pendientes = faltan.Where(p => p.Estado == ProConta.Contabilidad.Amortizacion.EstadoAmortizacion.Pendiente).ToList();
        var texto = string.Join(" ", new[]
        {
            pendientes.Count == 0 ? null : $"Sin contabilizar: {string.Join(", ", pendientes.Select(p => p.Periodo.Nombre))} ({CalculoCierreEjercicio.Euros(pendientes.Sum(p => p.Propuesta.Total))}).",
            desactualizados.Count == 0 ? null : $"Desactualizada: {string.Join(", ", desactualizados)}; repítela antes de cerrar.",
        }.Where(t => t is not null));
        return new ComprobacionCierre("amortizacion", Titulo, ResultadoComprobacion.Aviso, texto, DestinoComprobacion.Amortizacion);
    }

    private Task<ComprobacionCierre> ComprobarCargosAmortizacionAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        const string Titulo = "Amortización del ejercicio";
        var amortizado = await s.EscalarAsync(
            "SELECT SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento NOT IN (1, 2, 3) AND LEFT(p.CuentaContable, 2) = '68'", ctx.IdSociedadEjercicio);
        var importe = amortizado is null or DBNull ? 0m : Convert.ToDecimal(amortizado, CultureInfo.InvariantCulture);
        if (importe != 0m)
            return new ComprobacionCierre("amortizacion", Titulo, ResultadoComprobacion.Correcta, $"Contabilizada: {CalculoCierreEjercicio.Euros(importe)} en las 68.", DestinoComprobacion.Amortizacion);
        var acumulada = await s.EscalarAsync(
            "SELECT COUNT(*) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 3 AND LEFT(p.CuentaContable, 2) = '28'", ctx.IdSociedadEjercicio);
        return Convert.ToInt32(acumulada, CultureInfo.InvariantCulture) > 0
            ? new ComprobacionCierre("amortizacion", Titulo, ResultadoComprobacion.Aviso, $"No hay ningún asiento que cargue una 68 en {ctx.Ejercicio} y hay amortización acumulada (28): falta la amortización del año.",
                DestinoComprobacion.Amortizacion)
            : new ComprobacionCierre("amortizacion", Titulo, ResultadoComprobacion.NoAplica, "Sin amortizaciones: no hay amortización acumulada (28).");
    });

    private async Task<ComprobacionCierre> ComprobarProrrataAsync(ContextoSociedadEjercicio ctx)
    {
        const string Titulo = "Prorrata de IVA";
        var enProrrata = await bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(
            "SELECT COUNT(*) FROM Actividades WHERE IdSociedad = ? AND Deducible = 1", ctx.IdSociedad), CultureInfo.InvariantCulture));
        if (enProrrata == 0) return new("prorrata", Titulo, ResultadoComprobacion.NoAplica, "Ninguna actividad va en prorrata.");
        var situacion = await new ServicioRegularizacionProrrata(bd, motor, cuentas, null, _reloj).SituacionAsync(ctx);
        return situacion.Estado switch
        {
            EstadoRegularizacionProrrata.Regularizada => new("prorrata", Titulo, ResultadoComprobacion.Correcta,
                $"Definitiva fijada ({(situacion.Definitiva ?? 0m) * 100m:0.##} %) y regularizada a la 4729.", DestinoComprobacion.Prorrata),
            EstadoRegularizacionProrrata.Desactualizada => new("prorrata", Titulo, ResultadoComprobacion.Aviso, $"Desactualizada: {situacion.Motivo} Repítela antes de cerrar.", DestinoComprobacion.Prorrata),
            _ => new("prorrata", Titulo, ResultadoComprobacion.Aviso, "Sin la prorrata definitiva: falta fijarla y regularizarla a 31/12 (antes de liquidar el 4T).", DestinoComprobacion.Prorrata),
        };
    }

    /// <summary>
    /// Facturas pendientes de recibir (contabilidad/procesos/facturas-pendientes-recibir): su estado. Son lotes propios de ese proceso
    /// (provisión a 31/12 y reversión a 1/1 del siguiente): el cierre no los graba ni los borra al reabrir.
    /// </summary>
    private async Task<ComprobacionCierre> ComprobarFacturasPendientesAsync(ContextoSociedadEjercicio ctx)
    {
        const string Clave = "facturas-pendientes", Titulo = "Facturas pendientes de recibir";
        var situacion = await new ServicioFacturasPendientes(bd, motor, cuentas, null, _reloj).SituacionAsync(ctx);
        var p = situacion.Propuesta;
        var n = p.Incluidas.Count();
        return situacion.Estado switch
        {
            EstadoFacturasPendientes.Hecho when situacion.Grabado == 0m => new(Clave, Titulo, ResultadoComprobacion.Correcta, "Hecho: no había nada que provisionar.", DestinoComprobacion.FacturasPendientes),
            EstadoFacturasPendientes.Hecho => new(Clave, Titulo, ResultadoComprobacion.Correcta,
                $"Provisionadas a 31/12 por {CalculoCierreEjercicio.Euros(situacion.Grabado)}, con su reversión el 1/1/{ctx.Ejercicio + 1}.", DestinoComprobacion.FacturasPendientes),
            EstadoFacturasPendientes.Desactualizado => new(Clave, Titulo, ResultadoComprobacion.Aviso, $"Desactualizadas: {situacion.Motivo} Repítelo antes de cerrar.",
                DestinoComprobacion.FacturasPendientes),
            _ when n == 0 => new(Clave, Titulo, ResultadoComprobacion.Correcta,
                p.Filas.Count == 0 ? "Ningún proveedor cuadrado en el Mayor tiene saldo deudor: nada que provisionar." : "Todos los proveedores con saldo deudor están excluidos.",
                DestinoComprobacion.FacturasPendientes),
            _ => new(Clave, Titulo, ResultadoComprobacion.Aviso,
                $"Sin contabilizar: {Plural(n, "proveedor cuadrado", "proveedores cuadrados")} con saldo deudor (pagado sin factura) por {CalculoCierreEjercicio.Euros(p.Total)} de base.",
                DestinoComprobacion.FacturasPendientes),
        };
    }

    /// <summary>
    /// Céntimos residuales (contabilidad/procesos/centimos-residuales 2026-11-17 19:05): el estado del proceso; avisa, no bloquea, si quedan
    /// subcuentas con céntimos por saldar o si lo grabado ya no coincide con la propuesta.
    /// </summary>
    private async Task<ComprobacionCierre> ComprobarCentimosAsync(ContextoSociedadEjercicio ctx)
    {
        const string Clave = "centimos", Titulo = "Céntimos residuales";
        var situacion = await new ProConta.Contabilidad.Centimos.ServicioCentimos(bd, motor, cuentas, null, _reloj).SituacionAsync(ctx);
        var p = situacion.Propuesta;
        var n = p.Asientos.Count;
        return situacion.Estado switch
        {
            ProConta.Contabilidad.Centimos.EstadoCentimos.Hecho when situacion.Asientos == 0 => new(Clave, Titulo, ResultadoComprobacion.Correcta,
                p.Filas.Count == 0 ? "Ninguna subcuenta tiene céntimos residuales." : "Las subcuentas con céntimos residuales están excluidas.", DestinoComprobacion.CentimosResiduales),
            ProConta.Contabilidad.Centimos.EstadoCentimos.Hecho => new(Clave, Titulo, ResultadoComprobacion.Correcta,
                $"Saldados a 31/12: {Plural(situacion.Asientos, "subcuenta", "subcuentas")} por {CalculoCierreEjercicio.Euros(situacion.Grabado)}.", DestinoComprobacion.CentimosResiduales),
            ProConta.Contabilidad.Centimos.EstadoCentimos.Desactualizado => new(Clave, Titulo, ResultadoComprobacion.Aviso, $"Desactualizados: {situacion.Motivo} Repítelo antes de cerrar.",
                DestinoComprobacion.CentimosResiduales),
            _ when n == 0 => new(Clave, Titulo, ResultadoComprobacion.Correcta,
                p.Filas.Count == 0 ? "Ninguna subcuenta tiene céntimos residuales." : "Las subcuentas con céntimos residuales están excluidas.", DestinoComprobacion.CentimosResiduales),
            _ => new(Clave, Titulo, ResultadoComprobacion.Aviso,
                $"Sin saldar: {Plural(n, "subcuenta", "subcuentas")} con céntimos residuales por {CalculoCierreEjercicio.Euros(p.Total)}; pasarían así al ejercicio siguiente.",
                DestinoComprobacion.CentimosResiduales),
        };
    }

    private async Task<ComprobacionCierre> ComprobarLiquidacionAsync(ContextoSociedadEjercicio ctx)
    {
        const string Titulo = "Liquidación del IVA del 4T";
        var trimestres = await new ServicioLiquidacionIva(bd, motor, cuentas, null, _reloj).TrimestresAsync(ctx);
        var cuarto = trimestres.FirstOrDefault(t => t.Trimestre == 4);
        return cuarto?.Estado switch
        {
            EstadoLiquidacionIva.Liquidado => new("liquidacion", Titulo, ResultadoComprobacion.Correcta, "Liquidado: las 472 y 477 están a cero.", DestinoComprobacion.LiquidacionIva),
            EstadoLiquidacionIva.Desactualizado => new("liquidacion", Titulo, ResultadoComprobacion.Aviso,
                "Liquidado, pero algo ha cambiado después: las 472 o 477 no están a cero o el 303 da otro resultado. Repítela antes de cerrar.", DestinoComprobacion.LiquidacionIva),
            EstadoLiquidacionIva.EnCurso => new("liquidacion", Titulo, ResultadoComprobacion.Aviso,
                "El trimestre aún no ha terminado: las 472 y 477 pasarán al ejercicio siguiente con su saldo.", DestinoComprobacion.LiquidacionIva),
            _ => new("liquidacion", Titulo, ResultadoComprobacion.Aviso, "Sin liquidar: las 472 y 477 aún tienen saldo y pasarán así al ejercicio siguiente.", DestinoComprobacion.LiquidacionIva),
        };
    }

    /// <summary>Por qué hay que escribir el motivo para cerrar (null si no hace falta): avisos en la comprobación o el ejercicio aún sin terminar.</summary>
    public static string? PorQueMotivo(IReadOnlyList<ComprobacionCierre> comprobaciones, bool ejercicioTerminado)
    {
        var avisos = comprobaciones.Count(c => c.Resultado == ResultadoComprobacion.Aviso);
        return avisos > 0 ? $"Hay {Plural(avisos, "aviso", "avisos")} en la comprobación previa."
            : !ejercicioTerminado ? "El ejercicio aún no ha terminado."
            : null;
    }

    // ---------- propuesta ----------

    /// <summary>Propuesta del cierre desde los apuntes contabilizados de ahora, con lo que impide cerrar.</summary>
    public async Task<Resultado<PropuestaCierre>> ProponerAsync(ContextoSociedadEjercicio ctx)
    {
        var situacion = await SituacionAsync(ctx);
        var (cuenta, nombre) = await CuentaResultadoAsync(ctx);
        var saldos = await SaldosAsync(ctx);
        var p = CalculoCierreEjercicio.Calcular(ctx.Ejercicio, cuenta, nombre, saldos);
        var bloqueos = new List<string>();
        if (situacion.Cerrado)
            bloqueos.Add($"El ejercicio {ctx.Ejercicio} ya está cerrado. Para volver a cerrarlo, reábrelo antes.");
        if (situacion.Siguiente is null)
            bloqueos.Add($"No existe el ejercicio {ctx.Ejercicio + 1}, que recibe la apertura: créalo con «Crear el ejercicio {ctx.Ejercicio + 1}…».");
        else if (situacion.SiguienteCerrado)
            bloqueos.Add($"El ejercicio {ctx.Ejercicio + 1} está cerrado y no admite la apertura. Reábrelo antes.");
        if (situacion.Ajenos.Count > 0)
            bloqueos.Add($"Ya hay asientos de regularización, cierre o apertura hechos fuera de este proceso ({string.Join("; ", situacion.Ajenos)}): " +
                "con ellos el cierre saldría doble. Los ejercicios importados de ContaSol se quedan como vienen; si son de prueba, bórralos antes.");
        return Resultado<PropuestaCierre>.Correcto(p with { Bloqueos = [.. bloqueos, .. p.Bloqueos] });
    }

    // ---------- cerrar ----------

    /// <summary>
    /// Cierra el ejercicio con exactamente la propuesta <paramref name="vista"/> (se vuelve a calcular; si ha cambiado, no se graba): crea
    /// la cuenta de resultados si falta, copia al ejercicio siguiente las cuentas que aún no tiene y graba los tres asientos y la marca de
    /// cerrado en una transacción. Pide motivo si hay avisos en la comprobación o el ejercicio aún no ha terminado.
    /// </summary>
    public async Task<Resultado<ConfirmacionCierre>> ConfirmarAsync(ContextoSociedadEjercicio ctx, string? motivo, Actor actor, PropuestaCierre? vista = null)
    {
        Resultado<ConfirmacionCierre> Fallo(Error e) => Resultado<ConfirmacionCierre>.Fallo(e);
        var registro = $"Cierre del ejercicio {ctx.Ejercicio}";
        if (puede is not null && !await puede())
            return Fallo(new Error("Cierre.SinPermiso", "No tienes permiso para cerrar el ejercicio.", registro,
                "La acción «Cerrar y reabrir el ejercicio» tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos."));
        var prop = await ProponerAsync(ctx);
        if (!prop.EsCorrecto) return Resultado<ConfirmacionCierre>.Fallo(prop.Errores);
        var p = prop.Valor;
        if (!p.SePuedeCerrar)
            return Fallo(new Error("Cierre.NoSePuede", "No se puede cerrar el ejercicio.", registro, string.Join(" ", p.Bloqueos), "Resuélvelo y vuelve a calcular la propuesta."));
        if (vista is not null && !vista.Igual(p))
            return Fallo(new Error("Cierre.Cambiada", "La propuesta ha cambiado desde que la abriste.", registro,
                "Alguien ha contabilizado o cambiado apuntes del ejercicio mientras la revisabas.", "Vuelve a calcularla y revísala antes de cerrar."));
        if (!p.Cuadra)
            return Fallo(new Error("Cierre.Descuadre", "La regularización, el cierre y la apertura no cuadran entre sí.", registro,
                "Cada asiento debe cuadrar y la apertura debe ser el cierre al revés.", "Vuelve a calcular la propuesta; si sigue igual, revisa los apuntes del ejercicio."));
        var comprobaciones = await ComprobarAsync(ctx);
        if (PorQueMotivo(comprobaciones, EjercicioTerminado(ctx)) is { } porQue && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("Cierre.SinMotivo", "Hay que escribir el motivo.", registro, porQue, "Escribe el motivo para cerrar igualmente (queda en los asientos y en su historial)."));
        var siguiente = (await nuevoEjercicio.SiguienteAsync(ctx))!;

        // La cuenta de resultados, si aún no existe, se crea (colgada de su cuenta); después pasan al siguiente las cuentas que le faltan.
        var creadas = new List<string>();
        if (!(await cuentas.ResolverAsync(ctx, p.CuentaResultado)).EsCorrecto)
        {
            var alta = await cuentas.AltaSubcuentaAsync(ctx, p.CuentaResultado, CalculoCierreEjercicio.NombreResultadoDefecto);
            if (!alta.EsCorrecto) return Resultado<ConfirmacionCierre>.Fallo(alta.Errores);
            creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}»");
        }
        var copiadas = await nuevoEjercicio.CopiarCuentasQueFaltanAsync(ctx, siguiente);

        var texto = string.IsNullOrWhiteSpace(motivo) ? registro : $"{registro}: {motivo.Trim()}";
        var notas = $"{registro}, confirmado por {actor.Nombre} el {_reloj.Ahora:dd/MM/yyyy HH:mm}." + (string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {motivo.Trim()}");
        IReadOnlyList<Asiento> Uno(ContextoSociedadEjercicio c, AsientoCierre? a) => a is null ? [] : [CalculoCierreEjercicio.AAsiento(c, a, notas)];
        var lotes = new List<LoteAsientos>
        {
            new(ctx, Lote(ProcesoRegularizacion, ctx.Ejercicio), Uno(ctx, p.Regularizacion)),
            new(ctx, Lote(ProcesoCierre, ctx.Ejercicio), Uno(ctx, p.Cierre)),
            new(siguiente, Lote(ProcesoApertura, ctx.Ejercicio), Uno(siguiente, p.Apertura)),
        };
        var grabado = await motor.ReemplazarLotesAsync(lotes, actor, texto,
            alFinal: async s =>
            {
                await s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio);
                return Resultado.Correcto();
            },
            opciones: Opciones);
        if (!grabado.EsCorrecto) return Resultado<ConfirmacionCierre>.Fallo(grabado.Errores);
        int? Primero(int i) => grabado.Valor[i].Count > 0 ? grabado.Valor[i][0] : null;
        return Resultado<ConfirmacionCierre>.Correcto(new(Primero(0), Primero(1), Primero(2), creadas, copiadas, p));
    }

    // ---------- reabrir ----------

    /// <summary>
    /// Reabre el ejercicio: quita la marca de cerrado y borra la regularización, el cierre y la apertura de este proceso (los demás asientos no
    /// se tocan), en una transacción y con motivo. No deja si el ejercicio siguiente está cerrado: hay que reabrir antes ese. Devuelve los asientos borrados.
    /// </summary>
    public async Task<Resultado<int>> ReabrirAsync(ContextoSociedadEjercicio ctx, string motivo, Actor actor)
    {
        Resultado<int> Fallo(Error e) => Resultado<int>.Fallo(e);
        var registro = $"Reapertura del ejercicio {ctx.Ejercicio}";
        if (puede is not null && !await puede())
            return Fallo(new Error("Cierre.SinPermiso", "No tienes permiso para reabrir el ejercicio.", registro,
                "La acción «Cerrar y reabrir el ejercicio» tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("Cierre.SinMotivo", "Hay que escribir el motivo.", registro, "Reabrir un ejercicio queda registrado con su motivo.", "Escribe el motivo."));
        var situacion = await SituacionAsync(ctx);
        if (!situacion.Cerrado)
            return Fallo(new Error("Cierre.NoCerrado", $"El ejercicio {ctx.Ejercicio} no está cerrado.", registro, "Solo se reabre un ejercicio cerrado.", "Actualiza la pantalla."));
        if (situacion.SiguienteCerrado)
            return Fallo(new Error("Cierre.SiguienteCerrado", $"El ejercicio {ctx.Ejercicio + 1} está cerrado.", registro,
                $"Su apertura sale del cierre de {ctx.Ejercicio}: mientras {ctx.Ejercicio + 1} esté cerrado, no se puede deshacer.",
                $"Reabre antes el ejercicio {ctx.Ejercicio + 1} (trabajando en ese ejercicio)."));
        var lotes = new List<LoteAsientos>
        {
            new(ctx, Lote(ProcesoRegularizacion, ctx.Ejercicio), []),
            new(ctx, Lote(ProcesoCierre, ctx.Ejercicio), []),
        };
        if (situacion.Siguiente is { } siguiente) lotes.Add(new(siguiente, Lote(ProcesoApertura, ctx.Ejercicio), []));
        var hecho = await motor.ReemplazarLotesAsync(lotes, actor, $"{registro}: {motivo.Trim()}",
            alPrincipio: async s =>
            {
                await s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = False WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio);
                return Resultado.Correcto();
            },
            opciones: Opciones);
        if (!hecho.EsCorrecto) return Resultado<int>.Fallo(hecho.Errores);
        return Resultado<int>.Correcto(new[] { situacion.IdRegularizacion, situacion.IdCierre, situacion.IdApertura }.Count(x => x is not null));
    }

    // ---------- lecturas ----------

    /// <summary>
    /// Saldos (Debe − Haber) de cada subcuenta del ejercicio desde sus apuntes contabilizados, sin los asientos de este proceso (para que
    /// repetir el cierre no los cuente), con el nombre de la cuenta.
    /// </summary>
    public Task<IReadOnlyList<SaldoCuentaCierre>> SaldosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlyList<SaldoCuentaCierre>>(async s =>
    {
        var nombres = (await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedadEjercicio))
            .GroupBy(x => x.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Nombre, StringComparer.Ordinal);
        var filas = await s.ConsultarAsync(
            "SELECT p.CuentaContable, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS Saldo " +
            "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND (a.MotorPropietario IS NULL OR a.MotorPropietario <> ?) GROUP BY p.CuentaContable",
            f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Saldo: f.DecimalNulo("Saldo") ?? 0m), ctx.IdSociedadEjercicio, Motor);
        return filas.Select(f => new SaldoCuentaCierre(f.Cuenta, nombres.GetValueOrDefault(f.Cuenta, ""), f.Saldo)).ToList();
    });

    /// <summary>La cuenta de resultados del ejercicio (vacía: 1290000) y su nombre si ya existe.</summary>
    public Task<(string Cuenta, string? Nombre)> CuentaResultadoAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<(string, string?)>(async s =>
    {
        var configurada = s.LeerEsquema().ExisteColumna("SociedadesEjercicios", "CuentaResultadoEjercicio")
            ? await s.EscalarAsync("SELECT CuentaResultadoEjercicio FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio) : null;
        var cuenta = configurada is string t && !string.IsNullOrWhiteSpace(t) ? t.Trim() : CalculoCierreEjercicio.CuentaResultadoDefecto;
        var nombre = await s.EscalarAsync("SELECT Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", ctx.IdSociedadEjercicio, cuenta);
        return (cuenta, nombre is string n ? n.Trim() : null);
    });

    private static async Task<bool> CerradoAsync(ISesionDatos s, int idSociedadEjercicio)
    {
        var v = await s.EscalarAsync("SELECT EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", idSociedadEjercicio);
        return v is not (null or DBNull) && Convert.ToBoolean(v, CultureInfo.InvariantCulture);
    }

    private static async Task<ContextoSociedadEjercicio?> SiguienteAsync(ISesionDatos s, ContextoSociedadEjercicio ctx)
    {
        var v = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?",
            ctx.IdSociedad, (ctx.Ejercicio + 1).ToString(CultureInfo.InvariantCulture));
        return v is null or DBNull ? null : new ContextoSociedadEjercicio(ctx.IdSociedad, Convert.ToInt32(v, CultureInfo.InvariantCulture), ctx.Ejercicio + 1);
    }

    private static string Plural(int n, string uno, string varios) => $"{n.ToString("#,##0", CultureInfo.GetCultureInfo("es-ES"))} {(n == 1 ? uno : varios)}";
}
