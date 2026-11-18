using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Mantenimiento;

namespace ProConta.Contabilidad.Dividendos;

/// <summary>Estado de la aplicación del resultado del ejercicio anterior.</summary>
public enum EstadoAplicacion
{
    /// <summary>La cuenta de resultados no tiene saldo (sin apertura, resultado cero o ya aplicado fuera del proceso).</summary>
    SinResultado,
    Pendiente,
    Aplicada,
    /// <summary>El resultado ha cambiado desde que se aplicó (se ha reabierto y vuelto a cerrar el ejercicio anterior).</summary>
    Desactualizada,
}

/// <summary>Aplicación grabada (tabla AplicacionesResultado).</summary>
public sealed record AplicacionGuardada(int IdAplicacion, int EjercicioResultado, DateTime Fecha, decimal Resultado, decimal Compensacion, decimal ReservaLegal, decimal Dividendos,
    decimal Voluntarias, int? IdReparto, int? IdAsiento, string? Motivo, string? Operador, DateTime FechaHora);

/// <summary>Situación de la aplicación del resultado del ejercicio anterior en el ejercicio de trabajo.</summary>
public sealed record SituacionAplicacion(int EjercicioResultado, EstadoAplicacion Estado, decimal Resultado, AplicacionGuardada? Guardada, decimal DividendosNeto, string? Motivo)
{
    public bool Hecha => Guardada is not null;
}

/// <summary>La propuesta de la aplicación y, si hay dividendos, la de su reparto con cargo al resultado.</summary>
public sealed record PropuestaAplicacionCompleta(PropuestaAplicacion Aplicacion, PropuestaDividendos? Dividendos, IReadOnlyList<NetoSocio> Netos)
{
    public bool SePuedeConfirmar => Aplicacion.SePuedeConfirmar;
}

/// <summary>Resultado de confirmar: la aplicación, su asiento (null si todo fue a dividendos), el reparto y las cuentas creadas.</summary>
public sealed record ConfirmacionAplicacion(int IdAplicacion, int? IdAsiento, int? IdReparto, IReadOnlyList<string> CuentasCreadas, PropuestaAplicacionCompleta Propuesta);

/// <summary>
/// Proceso «Aplicación del resultado» (contabilidad/procesos/aplicacion-resultado 2026-11-17 20:10) sobre Access. En el ejercicio de trabajo aplica
/// el resultado del anterior, que la apertura deja en su cuenta de resultados: lo propone con <see cref="CalculoAplicacionResultado"/> desde los
/// apuntes y lo graba como lote propio regenerable <c>contabilidad/procesos/aplicacion-resultado · APLICACION · año del resultado</c>. Los
/// dividendos con cargo al resultado se graban como un reparto normal (<see cref="ServicioDividendos"/>) cuya cuenta de origen es la de
/// resultados, para que tengan su detalle por socio, su pago en N43 y su acta; la aplicación guarda su número. Repetir sustituye el asiento y
/// corrige el reparto, con motivo; deshacer borra los dos, con motivo.
/// </summary>
public sealed class ServicioAplicacionResultado(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, ServicioDividendos dividendos, Func<Task<bool>>? puede = null,
    IReloj? reloj = null)
{
    public const string CodigoProceso = "APLICACION";
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly ConsultaSocios _socios = new(bd);

    public static IdentidadLote Lote(int ejercicioResultado) =>
        new(ServicioDividendos.MotorPropietario, CodigoProceso, ejercicioResultado.ToString(CultureInfo.InvariantCulture));

    private static string Clave(IdentidadLote l) => $"{l.MotorPropietario}|{l.CodigoProceso}|{l.ClaveEjecucion}";

    /// <summary>Fecha propuesta: la de la aplicación guardada o el 30/06 del ejercicio de trabajo.</summary>
    public DateTime FechaPropuesta(ContextoSociedadEjercicio ctx, SituacionAplicacion? situacion = null) =>
        situacion?.Guardada?.Fecha ?? CalculoAplicacionResultado.FechaPropuesta(ctx.Ejercicio);

    /// <summary>
    /// Cuentas de la aplicación: la de resultados es la del cierre del ejercicio anterior (la que recibe su regularización); las demás, las del
    /// ejercicio de trabajo (vacías: 1120000, 1130000 y 1210000).
    /// </summary>
    public Task<ConfiguracionAplicacion> ConfiguracionAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var esquema = s.LeerEsquema();
        bool Existe(string c) => esquema.ExisteColumna("SociedadesEjercicios", c);
        async Task<string?> Leer(int id, string columna) => Existe(columna)
            ? await s.EscalarAsync($"SELECT {columna} FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", id) is string t ? t : null
            : null;
        var anterior = await ConsultaSocios.EjercicioAsync(s, ctx.IdSociedad, ctx.Ejercicio - 1);
        var resultado = (anterior is int a ? await Leer(a, "CuentaResultadoEjercicio") : null) ?? await Leer(ctx.IdSociedadEjercicio, "CuentaResultadoEjercicio");
        return ConfiguracionAplicacion.Con(resultado, await Leer(ctx.IdSociedadEjercicio, "CuentaReservaLegal"), await Leer(ctx.IdSociedadEjercicio, "CuentaReservasDividendos"),
            await Leer(ctx.IdSociedadEjercicio, "CuentaResultadosNegativos"));
    });

    /// <summary>La aplicación guardada del resultado del ejercicio anterior (null si no hay).</summary>
    public Task<AplicacionGuardada?> GuardadaAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT IdAplicacionResultado, EjercicioResultado, Fecha, Resultado, Compensacion, ReservaLegal, Dividendos, Voluntarias, IdRepartoDividendos, IdAsiento, Motivo, Operador, " +
        "FechaHoraConfirmacion FROM AplicacionesResultado WHERE IdSociedadEjercicio = ? AND EjercicioResultado = ?",
        f => (AplicacionGuardada?)new AplicacionGuardada(f.Entero("IdAplicacionResultado"), f.Entero("EjercicioResultado"), f.Fecha("Fecha").Date, f.DecimalNulo("Resultado") ?? 0m,
            f.DecimalNulo("Compensacion") ?? 0m, f.DecimalNulo("ReservaLegal") ?? 0m, f.DecimalNulo("Dividendos") ?? 0m, f.DecimalNulo("Voluntarias") ?? 0m,
            f.EnteroNulo("IdRepartoDividendos"), f.EnteroNulo("IdAsiento"), f.Texto("Motivo"), f.Texto("Operador"), f.Fecha("FechaHoraConfirmacion")),
        ctx.IdSociedadEjercicio, ctx.Ejercicio - 1));

    /// <summary>
    /// Saldos a la fecha desde los apuntes contabilizados del ejercicio de trabajo, sin los asientos de esta aplicación ni los de su reparto:
    /// resultado (acreedor de la cuenta de resultados), pérdidas pendientes (deudor de la 121), reserva legal (acreedor) y capital (acreedor de las 100).
    /// </summary>
    public Task<SaldosAplicacion> SaldosAsync(ContextoSociedadEjercicio ctx, ConfiguracionAplicacion config, DateTime fecha, int? idReparto) => bd.EjecutarAsync(async s =>
    {
        var sin = new List<string> { Clave(Lote(ctx.Ejercicio - 1)) };
        if (idReparto is int r) sin.Add(Clave(ServicioDividendos.Lote(r)));
        var saldos = await ConsultaSocios.SaldosAsync(s, ctx.IdSociedadEjercicio, fecha, [config.CuentaResultado, config.CuentaResultadosNegativos, config.CuentaReservaLegal], sin);
        var capital = await s.EscalarAsync(
            "SELECT SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) - SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.FechaContable <= ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 3 AND p.CuentaContable LIKE '100%'", ctx.IdSociedadEjercicio, fecha.Date);
        return new SaldosAplicacion(-saldos.GetValueOrDefault(config.CuentaResultado), saldos.GetValueOrDefault(config.CuentaResultadosNegativos),
            -saldos.GetValueOrDefault(config.CuentaReservaLegal), capital is null or DBNull ? 0m : Convert.ToDecimal(capital, CultureInfo.InvariantCulture));
    });

    /// <summary>Estado de la aplicación del resultado del ejercicio anterior (para la pantalla y la comprobación previa del cierre).</summary>
    public async Task<SituacionAplicacion> SituacionAsync(ContextoSociedadEjercicio ctx)
    {
        var anio = ctx.Ejercicio - 1;
        var guardada = await GuardadaAsync(ctx);
        var config = await ConfiguracionAsync(ctx);
        var fecha = guardada?.Fecha ?? new DateTime(ctx.Ejercicio, 12, 31);
        var saldos = await SaldosAsync(ctx, config, fecha, guardada?.IdReparto);
        var r = Math.Round(saldos.Resultado, 2, MidpointRounding.AwayFromZero);
        var neto = 0m;
        if (guardada?.IdReparto is int idReparto)
            neto = (await dividendos.RepartosAsync(ctx)).FirstOrDefault(x => x.IdReparto == idReparto)?.Neto ?? 0m;
        if (guardada is null)
            return new(anio, r == 0m ? EstadoAplicacion.SinResultado : EstadoAplicacion.Pendiente, r, null, 0m, null);
        if (guardada.Resultado != r)
            return new(anio, EstadoAplicacion.Desactualizada, r, guardada, neto,
                $"el resultado de {anio} era {CalculoAplicacionResultado.Euros(guardada.Resultado)} al aplicarlo y ahora es {CalculoAplicacionResultado.Euros(r)} (¿se ha reabierto y vuelto a cerrar {anio}?).");
        if (guardada.IdReparto is int rep && neto == 0m && guardada.Dividendos != 0m)
            return new(anio, EstadoAplicacion.Desactualizada, r, guardada, neto, $"su reparto de dividendos {rep} ya no existe (se ha deshecho en Reparto de dividendos).");
        return new(anio, EstadoAplicacion.Aplicada, r, guardada, neto, null);
    }

    /// <summary>
    /// Propuesta con los importes escritos (null: los propuestos). Si hay dividendos (neto total), se reparten entre los socios activos por su
    /// participación y se calcula su reparto con cargo a la cuenta de resultados.
    /// </summary>
    public async Task<Resultado<PropuestaAplicacionCompleta>> ProponerAsync(ContextoSociedadEjercicio ctx, DateTime fecha, ImportesAplicacion importes)
    {
        // Sin cuenta por defecto implícita: si falta alguna del ejercicio, el proceso se detiene y dice dónde asignarla (organizacion/sociedades-ejercicios 2026-11-18).
        if (await bd.EjecutarAsync(s => CuentasEjercicio.FaltanAsync(s, ctx.IdSociedadEjercicio, "CuentaResultadoEjercicio", "CuentaReservaLegal", "CuentaReservasDividendos", "CuentaResultadosNegativos")) is { } faltaCuenta)
            return Resultado<PropuestaAplicacionCompleta>.Fallo(faltaCuenta);
        ArgumentNullException.ThrowIfNull(importes);
        var anio = ctx.Ejercicio - 1;
        var registro = CalculoAplicacionResultado.Descripcion(anio);
        if (fecha.Year != ctx.Ejercicio)
            return Resultado<PropuestaAplicacionCompleta>.Fallo(new Error("Aplicacion.FechaFueraDelEjercicio", $"La fecha de la aplicación ({fecha:dd/MM/yyyy}) no es del ejercicio {ctx.Ejercicio}.",
                registro, $"El resultado de {anio} se aplica en {ctx.Ejercicio}, el ejercicio en que la junta aprueba las cuentas.", $"Escribe una fecha de {ctx.Ejercicio}."));
        if (importes.DividendosNeto < 0m || decimal.Round(importes.DividendosNeto, 2) != importes.DividendosNeto)
            return Resultado<PropuestaAplicacionCompleta>.Fallo(new Error("Aplicacion.DividendosNoValidos", "Los dividendos son negativos o tienen más de dos decimales.", registro, null,
                "Escribe el neto total que perciben los socios, en euros y céntimos."));
        var guardada = await GuardadaAsync(ctx);
        var idReparto = await RepartoVivoAsync(ctx, guardada);
        var config = await ConfiguracionAsync(ctx);
        var saldos = await SaldosAsync(ctx, config, fecha, idReparto);

        PropuestaDividendos? reparto = null;
        IReadOnlyList<NetoSocio> netos = [];
        if (importes.DividendosNeto > 0m)
        {
            var socios = (await _socios.ActivosAsync(ctx.IdSociedad, ctx.IdSociedadEjercicio)).Where(x => x.Dividendos is not null && x.Retencion is not null).ToList();
            if (socios.Count == 0)
                return Resultado<PropuestaAplicacionCompleta>.Fallo(new Error("Aplicacion.SinSocios", "No hay socios activos con sus subcuentas 526 y 4751.", registro,
                    "Los dividendos se reparten entre los socios por su participación.", "Da el rol Socio a cada socio en su ficha (Roles › Socio), con su % y sus subcuentas, o deja los dividendos a cero."));
            var partes = CalculoDividendos.Repartir(importes.DividendosNeto, socios.Select(x => x.Participacion).ToList());
            netos = socios.Select((x, i) => new NetoSocio(x.IdSocio, partes[i])).Where(n => n.Neto != 0m).ToList();
            var prop = await dividendos.ProponerAsync(ctx, fecha, netos, idReparto, config.CuentaResultado);
            if (!prop.EsCorrecto) return Resultado<PropuestaAplicacionCompleta>.Fallo(prop.Errores);
            reparto = prop.Valor;
        }
        var aplicacion = CalculoAplicacionResultado.Calcular(anio, fecha, config, saldos, importes, reparto?.Bruto ?? 0m);
        return Resultado<PropuestaAplicacionCompleta>.Correcto(new(aplicacion, reparto, netos));
    }

    /// <summary>
    /// Graba exactamente la propuesta que resulta de los importes (se vuelve a calcular en el momento): primero el reparto de los dividendos (o lo
    /// deshace si ya no hay), después el asiento de la aplicación con su fila. Si la aplicación ya existe, pide motivo y la sustituye. Si el asiento
    /// no se graba y el reparto era nuevo, lo deshace.
    /// </summary>
    public async Task<Resultado<ConfirmacionAplicacion>> ConfirmarAsync(ContextoSociedadEjercicio ctx, DateTime fecha, ImportesAplicacion importes, string? motivo, Actor actor,
        PropuestaAplicacionCompleta? vista = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var anio = ctx.Ejercicio - 1;
        var registro = CalculoAplicacionResultado.Descripcion(anio);
        Resultado<ConfirmacionAplicacion> Fallo(Error e) => Resultado<ConfirmacionAplicacion>.Fallo(e);
        if (puede is not null && !await puede())
            return Fallo(new Error("Aplicacion.SinPermiso", "No tienes permiso para aplicar el resultado.", registro,
                "La aplicación del resultado usa el permiso «Repartir dividendos» y tu grupo no lo tiene.", "Pídeselo a quien administra los permisos."));
        var guardada = await GuardadaAsync(ctx);
        if (guardada is not null && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("Aplicacion.SinMotivo", "Hay que escribir el motivo.", registro, "Repetir una aplicación confirmada queda registrado con su motivo.",
                "Escribe el motivo (por ejemplo «Importes aprobados en la junta»)."));
        var prop = await ProponerAsync(ctx, fecha, importes);
        if (!prop.EsCorrecto) return Resultado<ConfirmacionAplicacion>.Fallo(prop.Errores);
        var p = prop.Valor;
        if (!p.Aplicacion.SePuedeConfirmar)
            return Fallo(new Error("Aplicacion.NoValida", p.Aplicacion.Errores.FirstOrDefault() ?? "No hay resultado que aplicar.", registro, null, "Corrige los importes."));
        if (vista is not null && !Igual(vista, p))
            return Fallo(new Error("Aplicacion.Cambiada", "La propuesta ha cambiado desde que la viste.", registro,
                "Han cambiado los saldos, los socios o la configuración del ejercicio mientras la revisabas.", "Vuelve a calcularla y revísala antes de confirmar."));

        var texto = registro + (string.IsNullOrWhiteSpace(motivo) ? "" : $": {motivo.Trim()}");
        var motivoReparto = string.IsNullOrWhiteSpace(motivo) ? registro : motivo.Trim();

        // 1. Dividendos con cargo al resultado: un reparto normal (o se deshace el que hubiera).
        var idRepartoAnterior = await RepartoVivoAsync(ctx, guardada);
        int? idReparto = null;
        var repartoNuevo = false;
        if (p.Dividendos is not null)
        {
            var r = await dividendos.ConfirmarAsync(ctx, fecha, p.Netos, idRepartoAnterior, idRepartoAnterior is null ? null : motivoReparto, actor, p.Dividendos, p.Aplicacion.Config.CuentaResultado);
            if (!r.EsCorrecto) return Resultado<ConfirmacionAplicacion>.Fallo(r.Errores);
            idReparto = r.Valor.IdReparto;
            repartoNuevo = idRepartoAnterior is null;
        }
        else if (idRepartoAnterior is int quitar)
        {
            var d = await dividendos.DeshacerAsync(ctx, quitar, $"{registro} sin dividendos: {motivoReparto}", actor);
            if (!d.EsCorrecto) return Resultado<ConfirmacionAplicacion>.Fallo(d.Errores);
        }

        async Task<Resultado<ConfirmacionAplicacion>> Revertir(IReadOnlyList<Error> errores)
        {
            if (repartoNuevo && idReparto is int rn) await dividendos.DeshacerAsync(ctx, rn, $"No se ha grabado la {registro.ToLowerInvariant()}", actor);
            return Resultado<ConfirmacionAplicacion>.Fallo(errores);
        }

        // 2. Cuentas que aún no existen en el ejercicio.
        var creadas = new List<string>();
        var a = p.Aplicacion;
        foreach (var codigo in a.Lineas.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal))
        {
            if ((await cuentas.ResolverAsync(ctx, codigo)).EsCorrecto) continue;
            var nombre = codigo == a.Config.CuentaReservaLegal ? "Reserva legal" : codigo == a.Config.CuentaVoluntarias ? "Reservas voluntarias"
                : codigo == a.Config.CuentaResultadosNegativos ? "Resultados negativos de ejercicios anteriores" : "Resultado del ejercicio";
            var alta = await cuentas.AltaSubcuentaAsync(ctx, codigo, nombre);
            if (!alta.EsCorrecto) return await Revertir(alta.Errores);
            creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}»");
        }

        // 3. El asiento y la fila de la aplicación, en una transacción.
        async Task GuardarFila(ISesionDatos s, int? idAsiento)
        {
            await s.EjecutarAsync("DELETE FROM AplicacionesResultado WHERE IdSociedadEjercicio = ? AND EjercicioResultado = ?", ctx.IdSociedadEjercicio, anio);
            await s.EjecutarAsync(
                "INSERT INTO AplicacionesResultado (IdSociedadEjercicio, EjercicioResultado, Fecha, Resultado, Compensacion, ReservaLegal, Dividendos, Voluntarias, CuentaResultado, " +
                "CuentaReservaLegal, CuentaVoluntarias, CuentaResultadosNegativos, IdRepartoDividendos, IdAsiento, Motivo, Operador, FechaHoraConfirmacion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                ctx.IdSociedadEjercicio, anio, a.Fecha, a.Resultado, a.Compensacion, a.ReservaLegal, a.DividendosBruto, a.Voluntarias, a.Config.CuentaResultado,
                a.Config.CuentaReservaLegal, a.Config.CuentaVoluntarias, a.Config.CuentaResultadosNegativos, idReparto, idAsiento,
                string.IsNullOrWhiteSpace(motivo) ? null : Recortar(motivo.Trim(), 255), Recortar(actor.ToString(), 100), SinMilisegundos(_reloj.Ahora));
        }

        int? idAsientoGrabado = null;
        try
        {
            if (a.Lineas.Count == 0)
            {
                var trabajo = new TrabajoLote(Despues: async (s, _) => { await GuardarFila(s, null); return Resultado.Correcto(); });
                var borrado = await motor.EliminarLoteAsync(ctx, Lote(anio), actor, texto, trabajo);
                if (!borrado.EsCorrecto) return await Revertir(borrado.Errores);
            }
            else
            {
                var asiento = new Asiento(ctx, a.Fecha, registro)
                {
                    Notas = Recortar($"{registro}: resultado {CalculoAplicacionResultado.Euros(a.Resultado)}" +
                        (a.EsPerdida ? $" a la {a.Config.CuentaResultadosNegativos}." :
                            $"; compensación {CalculoAplicacionResultado.Euros(a.Compensacion)}, reserva legal {CalculoAplicacionResultado.Euros(a.ReservaLegal)}, " +
                            $"dividendos {CalculoAplicacionResultado.Euros(a.DividendosBruto)}{(idReparto is int ir ? $" (reparto {ir})" : "")}, voluntarias {CalculoAplicacionResultado.Euros(a.Voluntarias)}.") +
                        (string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {motivo.Trim()}"), 255),
                };
                foreach (var l in a.Lineas)
                {
                    var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
                    ap.GeneradoAutomaticamente = true;
                    ap.MotivoGeneracion = "Aplicación del resultado";
                    asiento.Apuntes.Add(ap);
                }
                var trabajo = new TrabajoLote(Despues: async (s, ids) => { await GuardarFila(s, ids.Count > 0 ? ids[0] : null); return Resultado.Correcto(); });
                var grabado = await motor.ReemplazarLoteAsync(ctx, Lote(anio), [asiento], actor, texto, trabajo);
                if (!grabado.EsCorrecto) return await Revertir(grabado.Errores);
                idAsientoGrabado = grabado.Valor.Count > 0 ? grabado.Valor[0] : null;
            }
        }
        catch
        {
            if (repartoNuevo && idReparto is int rn) await dividendos.DeshacerAsync(ctx, rn, $"No se ha grabado la {registro.ToLowerInvariant()}", actor);
            throw;
        }
        var id = (await GuardadaAsync(ctx))?.IdAplicacion ?? 0;
        return Resultado<ConfirmacionAplicacion>.Correcto(new(id, idAsientoGrabado, idReparto, creadas, p));
    }

    /// <summary>Borra el asiento de la aplicación, su fila y su reparto de dividendos, con motivo.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, string motivo, Actor actor)
    {
        var anio = ctx.Ejercicio - 1;
        var registro = CalculoAplicacionResultado.Descripcion(anio);
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("Aplicacion.SinPermiso", "No tienes permiso para aplicar el resultado.", registro, null, "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("Aplicacion.SinMotivo", "Hay que escribir el motivo.", registro, "Deshacer la aplicación queda registrado con su motivo.", "Escribe el motivo."));
        var guardada = await GuardadaAsync(ctx);
        if (guardada is null)
            return Resultado<int>.Fallo(new Error("Aplicacion.NoExiste", "La aplicación ya no existe.", registro, "Se ha deshecho desde otro puesto.", "Actualiza la pantalla (F5)."));
        if (await RepartoVivoAsync(ctx, guardada) is int idReparto)
        {
            var d = await dividendos.DeshacerAsync(ctx, idReparto, $"Deshacer la {registro.ToLowerInvariant()}: {motivo.Trim()}", actor);
            if (!d.EsCorrecto) return d;
        }
        var trabajo = new TrabajoLote(Despues: async (s, _) =>
        {
            await s.EjecutarAsync("DELETE FROM AplicacionesResultado WHERE IdSociedadEjercicio = ? AND EjercicioResultado = ?", ctx.IdSociedadEjercicio, anio);
            return Resultado.Correcto();
        });
        return await motor.EliminarLoteAsync(ctx, Lote(anio), actor, $"Deshacer la {registro.ToLowerInvariant()}: {motivo.Trim()}", trabajo);
    }

    // ---------- apoyo ----------

    /// <summary>El reparto de la aplicación si sigue existiendo (se puede haber deshecho desde la pantalla de repartos).</summary>
    private async Task<int?> RepartoVivoAsync(ContextoSociedadEjercicio ctx, AplicacionGuardada? guardada) =>
        guardada?.IdReparto is int id && (await dividendos.RepartosAsync(ctx)).Any(r => r.IdReparto == id) ? id : null;

    private static bool Igual(PropuestaAplicacionCompleta a, PropuestaAplicacionCompleta b) =>
        a.Aplicacion.Fecha == b.Aplicacion.Fecha && a.Aplicacion.Lineas.SequenceEqual(b.Aplicacion.Lineas)
        && (a.Dividendos is null ? b.Dividendos is null : b.Dividendos is not null && a.Dividendos.Lineas.SequenceEqual(b.Dividendos.Lineas));

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];

    private static DateTime SinMilisegundos(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Kind);
}
