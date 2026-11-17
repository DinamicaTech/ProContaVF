using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Dividendos;

/// <summary>Un socio del reparto guardado (tabla RepartosDividendosSocios).</summary>
public sealed record SocioRepartoGuardado(int IdSocio, string Nombre, decimal? Participacion, decimal Neto, decimal Bruto, decimal Retencion, string CuentaDividendos, string CuentaRetencion);

/// <summary>Un reparto guardado (tabla RepartosDividendos) con sus socios.</summary>
public sealed record RepartoGuardado(int IdReparto, DateTime Fecha, decimal PorcentajeRetencion, string CuentaReservas, decimal Neto, decimal Bruto, decimal Retencion,
    string? Motivo, int? IdAsiento, string? Operador, DateTime FechaHora, IReadOnlyList<SocioRepartoGuardado> Socios);

/// <summary>
/// Un socio en la ficha del reparto: su %, sus subcuentas, lo ya pagado (saldo deudor de su 526 a la fecha, sin este reparto) y el neto
/// propuesto (lo pagado, o lo del reparto guardado al corregirlo). <see cref="Error"/>: por qué no puede entrar en el reparto (sin subcuentas).
/// </summary>
public sealed record SocioParaReparto(int IdSocio, string Nombre, decimal Participacion, string CuentaDividendos, string CuentaRetencion, decimal Pagado, decimal NetoPropuesto,
    bool Incluido, string? Error);

/// <summary>Neto que da el operador a un socio.</summary>
public sealed record NetoSocio(int IdSocio, decimal Neto);

/// <summary>Lo que necesita la ficha de un reparto: la configuración, los socios y, al corregir, el reparto guardado.</summary>
public sealed record DatosReparto(DateTime Fecha, ConfiguracionDividendos Configuracion, IReadOnlyList<SocioParaReparto> Socios, RepartoGuardado? Guardado, decimal SumaParticipaciones = 0m,
    int SociosActivos = 0);

/// <summary>Resultado de confirmar: el reparto, su asiento y las cuentas creadas en el ejercicio.</summary>
public sealed record ConfirmacionDividendos(int IdReparto, int IdAsiento, IReadOnlyList<string> CuentasCreadas, PropuestaDividendos Propuesta);

/// <summary>
/// Proceso «Reparto de dividendos» (contabilidad/procesos/aplicacion-resultado) sobre Access: propone el asiento con <see cref="CalculoDividendos"/>
/// a partir del neto de cada socio y lo graba como lote propio regenerable <c>contabilidad/procesos/aplicacion-resultado · DIVIDENDOS · IdReparto</c>.
/// Corregir un reparto regenera su asiento sin duplicar y deshacerlo borra el asiento y la fila, los dos con motivo. Al dar de alta un reparto se
/// crea antes su fila (el número es la clave del lote) y, si el asiento no se graba, se borra. Los saldos salen de los apuntes, nunca de la caché.
/// </summary>
public sealed class ServicioDividendos(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, Func<Task<bool>>? puede = null, IReloj? reloj = null)
{
    public const string MotorPropietario = "contabilidad/procesos/aplicacion-resultado";
    public const string CodigoProceso = "DIVIDENDOS";
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly ConsultaSocios _socios = new(bd);

    public static IdentidadLote Lote(int idReparto) => new(MotorPropietario, CodigoProceso, idReparto.ToString(CultureInfo.InvariantCulture));

    private static string Clave(IdentidadLote l) => $"{l.MotorPropietario}|{l.CodigoProceso}|{l.ClaveEjecucion}";

    public DateTime Hoy => _reloj.Ahora.Date;

    /// <summary>Fecha propuesta para un reparto nuevo: hoy, o el último día del ejercicio si hoy cae fuera.</summary>
    public DateTime FechaPropuesta(ContextoSociedadEjercicio ctx) =>
        Hoy.Year == ctx.Ejercicio ? Hoy : Hoy.Year > ctx.Ejercicio ? new DateTime(ctx.Ejercicio, 12, 31) : new DateTime(ctx.Ejercicio, 1, 1);

    public Task<ConfiguracionDividendos> ConfiguracionAsync(ContextoSociedadEjercicio ctx) => _socios.ConfiguracionAsync(ctx.IdSociedadEjercicio);

    /// <summary>
    /// Socios y configuración para la ficha. Neto propuesto de cada socio: al corregir, el del reparto guardado; si no, lo que ya tiene pagado, el saldo
    /// deudor de su 526 a la fecha sin contar este reparto (los pagos se hacen a cuenta antes de declararlo). Los socios del reparto guardado que ya no
    /// están activos también salen.
    /// </summary>
    public async Task<DatosReparto> DatosAsync(ContextoSociedadEjercicio ctx, DateTime fecha, int? idReparto = null)
    {
        var config = await ConfiguracionAsync(ctx);
        var guardado = idReparto is int id ? (await RepartosAsync(ctx)).FirstOrDefault(r => r.IdReparto == id) : null;
        var activos = await _socios.ActivosAsync(ctx.IdSociedad, ctx.IdSociedadEjercicio);
        var codigos = activos.Select(s => s.Dividendos?.Codigo).OfType<string>().Where(c => c.Length > 0).ToList();
        var saldos = await bd.EjecutarAsync(s => ConsultaSocios.SaldosAsync(s, ctx.IdSociedadEjercicio, fecha, codigos, idReparto is int r ? Clave(Lote(r)) : null));
        var lista = new List<SocioParaReparto>();
        foreach (var s in activos)
        {
            var dividendos = s.Dividendos?.Codigo ?? "";
            var retencion = s.Retencion?.Codigo ?? "";
            var pagado = dividendos.Length > 0 && saldos.TryGetValue(dividendos, out var saldo) && saldo > 0m ? saldo : 0m;
            var enGuardado = guardado?.Socios.FirstOrDefault(g => g.IdSocio == s.IdSocio);
            string? error = dividendos.Length == 0 || retencion.Length == 0
                ? $"{s.Nombre} no tiene {(dividendos.Length == 0 ? "subcuenta 526" : "subcuenta 4751")}: abre su ficha, pestaña Roles › Socio, y guárdala (se crean solas)."
                : null;
            lista.Add(new SocioParaReparto(s.IdSocio, s.Nombre, s.Participacion, dividendos, retencion, pagado,
                enGuardado?.Neto ?? pagado, error is null && (guardado is null || enGuardado is not null), error));
        }
        if (guardado is not null)
            foreach (var g in guardado.Socios.Where(g => lista.All(l => l.IdSocio != g.IdSocio)))
                lista.Add(new SocioParaReparto(g.IdSocio, g.Nombre, g.Participacion ?? 0m, g.CuentaDividendos, g.CuentaRetencion, 0m, g.Neto, true, null));
        return new DatosReparto(fecha.Date, config, lista, guardado, activos.Sum(a => a.Participacion), activos.Count);
    }

    /// <summary>
    /// Propuesta del reparto con los netos dados (los socios que no están en <paramref name="netos"/> no entran). Avisa si la cuenta de reservas no
    /// llega al bruto o si los % de los socios activos no suman 100.
    /// </summary>
    /// <remarks>
    /// <paramref name="cuentaOrigen"/>: cuenta que se carga por el bruto en lugar de la de reservas del ejercicio (la aplicación del resultado reparte
    /// con cargo a la 129). Al corregir un reparto se mantiene la cuenta con que se grabó.
    /// </remarks>
    public async Task<Resultado<PropuestaDividendos>> ProponerAsync(ContextoSociedadEjercicio ctx, DateTime fecha, IReadOnlyList<NetoSocio> netos, int? idReparto = null,
        string? cuentaOrigen = null)
    {
        ArgumentNullException.ThrowIfNull(netos);
        var registro = CalculoDividendos.Descripcion(fecha);
        if (fecha.Year != ctx.Ejercicio)
            return Resultado<PropuestaDividendos>.Fallo(new Error("Dividendos.FechaFueraDelEjercicio", $"La fecha del reparto ({fecha:dd/MM/yyyy}) no es del ejercicio {ctx.Ejercicio}.",
                registro, "El asiento se graba en el ejercicio de trabajo.", $"Escribe una fecha de {ctx.Ejercicio} o cambia el ejercicio de trabajo."));
        if (netos.Count == 0 || netos.All(n => n.Neto == 0m))
            return Resultado<PropuestaDividendos>.Fallo(new Error("Dividendos.SinSocios", "No hay ningún socio con importe en el reparto.", registro, null,
                "Marca los socios que cobran y escribe su neto (o un neto total, que se reparte por su participación)."));
        if (netos.Any(n => n.Neto < 0m || decimal.Round(n.Neto, 2) != n.Neto))
            return Resultado<PropuestaDividendos>.Fallo(new Error("Dividendos.NetoNoValido", "Algún neto es negativo o tiene más de dos decimales.", registro, null, "Corrige el neto de ese socio."));
        var datos = await DatosAsync(ctx, fecha, idReparto);
        var socios = new List<SocioCalculoDividendos>();
        foreach (var n in netos.Where(n => n.Neto != 0m))
        {
            var s = datos.Socios.FirstOrDefault(x => x.IdSocio == n.IdSocio);
            if (s is null)
                return Resultado<PropuestaDividendos>.Fallo(new Error("Dividendos.SocioNoExiste", "Uno de los socios ya no es socio activo de la sociedad.", registro,
                    "Se le ha quitado el rol mientras tenías la ficha abierta.", "Cierra la ficha del reparto y vuelve a abrirla."));
            if (s.Error is { } e)
                return Resultado<PropuestaDividendos>.Fallo(new Error("Dividendos.SocioSinCuentas", e, registro, "Cada socio lleva su 526 y su 4751 en el asiento.", "Desmárcalo o créale las subcuentas."));
            socios.Add(new SocioCalculoDividendos(s.IdSocio, s.Nombre, s.Participacion, s.CuentaDividendos, s.CuentaRetencion, n.Neto));
        }
        var origen = !string.IsNullOrWhiteSpace(cuentaOrigen) ? cuentaOrigen.Trim()
            : datos.Guardado is { CuentaReservas.Length: > 0 } g ? g.CuentaReservas : datos.Configuracion.CuentaReservas;
        var config = datos.Configuracion with { CuentaReservas = origen };
        var saldoReservas = await bd.EjecutarAsync(s => ConsultaSocios.SaldosAsync(s, ctx.IdSociedadEjercicio, fecha, [origen],
            idReparto is int r ? Clave(Lote(r)) : null));
        var acreedor = -saldoReservas.GetValueOrDefault(origen);
        return Resultado<PropuestaDividendos>.Correcto(CalculoDividendos.Calcular(fecha, config, socios, acreedor,
            datos.SociosActivos == 0 ? null : datos.SumaParticipaciones));
    }

    /// <summary>
    /// Graba exactamente la propuesta que resulta de los netos (se vuelve a calcular en el momento). Sin <paramref name="idReparto"/>, es un reparto
    /// nuevo; con él, corrige ese reparto (regenera su asiento sin duplicar) y pide motivo. Crea en el ejercicio la cuenta de reservas y las
    /// subcuentas de los socios que existen en otro ejercicio y aún no en este.
    /// </summary>
    public async Task<Resultado<ConfirmacionDividendos>> ConfirmarAsync(ContextoSociedadEjercicio ctx, DateTime fecha, IReadOnlyList<NetoSocio> netos, int? idReparto,
        string? motivo, Actor actor, PropuestaDividendos? vista = null, string? cuentaOrigen = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        Resultado<ConfirmacionDividendos> Fallo(Error e) => Resultado<ConfirmacionDividendos>.Fallo(e);
        var registro = CalculoDividendos.Descripcion(fecha);
        if (puede is not null && !await puede())
            return Fallo(new Error("Dividendos.SinPermiso", "No tienes permiso para repartir dividendos.", registro,
                "La acción «Repartir dividendos» tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos."));
        if (idReparto is not null && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("Dividendos.SinMotivo", "Hay que escribir el motivo.", registro, "Corregir un reparto confirmado queda registrado con su motivo.",
                "Escribe el motivo (por ejemplo «Neto acordado en la junta»)."));
        if (idReparto is int existente && (await RepartosAsync(ctx)).All(r => r.IdReparto != existente))
            return Fallo(new Error("Dividendos.NoExiste", "El reparto ya no existe.", registro, "Se ha deshecho mientras lo corregías.", "Vuelve a la lista y crea un reparto nuevo."));
        var prop = await ProponerAsync(ctx, fecha, netos, idReparto, cuentaOrigen);
        if (!prop.EsCorrecto) return Resultado<ConfirmacionDividendos>.Fallo(prop.Errores);
        var p = prop.Valor;
        if (vista is not null && !MismoAsiento(vista, p))
            return Fallo(new Error("Dividendos.Cambiada", "La propuesta ha cambiado desde que la viste.", registro,
                "Han cambiado los socios, sus cuentas o la configuración del ejercicio mientras la revisabas.", "Vuelve a calcularla y revísala antes de confirmar."));

        // Cuentas que aún no existen en el ejercicio: la de reservas se crea colgada de su cuenta; las de los socios, desde su identidad.
        var creadas = new List<string>();
        var activos = await _socios.ActivosAsync(ctx.IdSociedad, ctx.IdSociedadEjercicio);
        foreach (var codigo in p.Lineas.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal))
        {
            if ((await cuentas.ResolverAsync(ctx, codigo)).EsCorrecto) continue;
            var identidad = activos.SelectMany(s => new[] { s.Dividendos, s.Retencion }).OfType<CuentaSocio>().FirstOrDefault(c => c.Codigo == codigo && !c.ExisteEnEjercicio);
            var alta = identidad is not null
                ? await cuentas.AltaEnEjercicioAsync(ctx, identidad.IdIdentidadContable)
                : codigo == p.CuentaReservas ? await cuentas.AltaSubcuentaAsync(ctx, codigo, codigo.StartsWith("129", StringComparison.Ordinal) ? "Resultado del ejercicio" : "Reservas voluntarias") : null;
            if (alta is null) continue; // el motor explica el error de las demás
            if (!alta.EsCorrecto) return Resultado<ConfirmacionDividendos>.Fallo(alta.Errores);
            creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}»");
        }

        var asiento = new Asiento(ctx, p.Fecha, CalculoDividendos.Descripcion(p.Fecha))
        {
            Notas = Recortar($"Reparto de dividendos: neto {Euros(p.Neto)}, retención {p.PorcentajeRetencion.ToString("0.##", CultureInfo.GetCultureInfo("es-ES"))} % ({Euros(p.Retencion)}), " +
                $"bruto {Euros(p.Bruto)} de la {p.CuentaReservas}." + (string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {motivo.Trim()}"), 255),
        };
        foreach (var l in p.Lineas)
        {
            var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
            ap.GeneradoAutomaticamente = true;
            ap.MotivoGeneracion = "Reparto de dividendos";
            asiento.Apuntes.Add(ap);
        }

        // Reparto nuevo: primero su fila, porque su número es la clave del lote; si el asiento no se graba, se borra.
        var nuevo = idReparto is null;
        var id = idReparto ?? await bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO RepartosDividendos (IdSociedadEjercicio, Fecha, PorcentajeRetencion, CuentaReservas, Neto, Bruto, Retencion, Motivo, IdAsiento, Operador, FechaHoraConfirmacion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                ctx.IdSociedadEjercicio, p.Fecha, p.PorcentajeRetencion, p.CuentaReservas, p.Neto, p.Bruto, p.Retencion, null, null, Recortar(actor.ToString(), 100), SinMilisegundos(_reloj.Ahora));
            return await s.UltimoIdAsync();
        });
        var texto = $"{registro} (reparto {id})" + (string.IsNullOrWhiteSpace(motivo) ? "" : $": {motivo.Trim()}");
        var trabajo = new TrabajoLote(Despues: async (s, ids) =>
        {
            await s.EjecutarAsync(
                "UPDATE RepartosDividendos SET Fecha = ?, PorcentajeRetencion = ?, CuentaReservas = ?, Neto = ?, Bruto = ?, Retencion = ?, Motivo = ?, IdAsiento = ?, Operador = ?, " +
                "FechaHoraConfirmacion = ? WHERE IdRepartoDividendos = ?",
                p.Fecha, p.PorcentajeRetencion, p.CuentaReservas, p.Neto, p.Bruto, p.Retencion, string.IsNullOrWhiteSpace(motivo) ? null : Recortar(motivo.Trim(), 255),
                ids.Count > 0 ? ids[0] : null, Recortar(actor.ToString(), 100), SinMilisegundos(_reloj.Ahora), id);
            await s.EjecutarAsync("DELETE FROM RepartosDividendosSocios WHERE IdRepartoDividendos = ?", id);
            foreach (var so in p.Socios)
                await s.EjecutarAsync(
                    "INSERT INTO RepartosDividendosSocios (IdRepartoDividendos, IdSocio, Participacion, Neto, Bruto, Retencion, CuentaDividendos, CuentaRetencion) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    id, so.IdSocio, so.Participacion, so.Neto, so.Bruto, so.Retencion, so.CuentaDividendos, so.CuentaRetencion);
            return Resultado.Correcto();
        });
        Resultado<IReadOnlyList<int>> grabado;
        try
        {
            grabado = await motor.ReemplazarLoteAsync(ctx, Lote(id), [asiento], actor, texto, trabajo);
        }
        catch when (nuevo)
        {
            await BorrarFilaAsync(id);
            throw;
        }
        if (!grabado.EsCorrecto)
        {
            if (nuevo) await BorrarFilaAsync(id);
            return Resultado<ConfirmacionDividendos>.Fallo(grabado.Errores);
        }
        return Resultado<ConfirmacionDividendos>.Correcto(new(id, grabado.Valor[0], creadas, p));
    }

    /// <summary>Borra el asiento del reparto y su fila, con motivo.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, int idReparto, string motivo, Actor actor)
    {
        var registro = $"Reparto de dividendos {idReparto}";
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("Dividendos.SinPermiso", "No tienes permiso para repartir dividendos.", registro, null, "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("Dividendos.SinMotivo", "Hay que escribir el motivo.", registro, "Deshacer un reparto queda registrado con su motivo.", "Escribe el motivo."));
        if ((await RepartosAsync(ctx)).All(r => r.IdReparto != idReparto))
            return Resultado<int>.Fallo(new Error("Dividendos.NoExiste", "El reparto ya no existe.", registro, "Se ha deshecho desde otro puesto.", "Actualiza la lista (F5)."));
        var trabajo = new TrabajoLote(Despues: async (s, _) =>
        {
            await s.EjecutarAsync("DELETE FROM RepartosDividendosSocios WHERE IdRepartoDividendos = ?", idReparto);
            await s.EjecutarAsync("DELETE FROM RepartosDividendos WHERE IdRepartoDividendos = ? AND IdSociedadEjercicio = ?", idReparto, ctx.IdSociedadEjercicio);
            return Resultado.Correcto();
        });
        return await motor.EliminarLoteAsync(ctx, Lote(idReparto), actor, $"Deshacer el reparto de dividendos {idReparto}: {motivo.Trim()}", trabajo);
    }

    /// <summary>Repartos del ejercicio, del más reciente al más antiguo, con sus socios.</summary>
    public Task<IReadOnlyList<RepartoGuardado>> RepartosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlyList<RepartoGuardado>>(async s =>
    {
        var repartos = await s.ConsultarAsync(
            "SELECT IdRepartoDividendos, Fecha, PorcentajeRetencion, CuentaReservas, Neto, Bruto, Retencion, Motivo, IdAsiento, Operador, FechaHoraConfirmacion " +
            "FROM RepartosDividendos WHERE IdSociedadEjercicio = ? ORDER BY Fecha DESC, IdRepartoDividendos DESC",
            f => (Id: f.Entero("IdRepartoDividendos"), Fecha: f.Fecha("Fecha").Date, Pct: f.DecimalNulo("PorcentajeRetencion") ?? 0m, Reservas: (f.Texto("CuentaReservas") ?? "").Trim(),
                Neto: f.DecimalNulo("Neto") ?? 0m, Bruto: f.DecimalNulo("Bruto") ?? 0m, Ret: f.DecimalNulo("Retencion") ?? 0m, Motivo: f.Texto("Motivo"), Asiento: f.EnteroNulo("IdAsiento"),
                Operador: f.Texto("Operador"), FechaHora: f.Fecha("FechaHoraConfirmacion")), ctx.IdSociedadEjercicio);
        var r = new List<RepartoGuardado>(repartos.Count);
        foreach (var x in repartos)
        {
            var socios = await s.ConsultarAsync(
                "SELECT rs.IdSocio, t.Nombre, rs.Participacion, rs.Neto, rs.Bruto, rs.Retencion, rs.CuentaDividendos, rs.CuentaRetencion " +
                "FROM (RepartosDividendosSocios AS rs INNER JOIN Socios AS so ON rs.IdSocio = so.IdSocio) INNER JOIN Terceros AS t ON so.IdTercero = t.IdTercero " +
                "WHERE rs.IdRepartoDividendos = ? ORDER BY t.Nombre",
                f => new SocioRepartoGuardado(f.Entero("IdSocio"), (f.Texto("Nombre") ?? "").Trim(), f.DecimalNulo("Participacion"), f.DecimalNulo("Neto") ?? 0m,
                    f.DecimalNulo("Bruto") ?? 0m, f.DecimalNulo("Retencion") ?? 0m, (f.Texto("CuentaDividendos") ?? "").Trim(), (f.Texto("CuentaRetencion") ?? "").Trim()), x.Id);
            r.Add(new RepartoGuardado(x.Id, x.Fecha, x.Pct, x.Reservas, x.Neto, x.Bruto, x.Ret, x.Motivo, x.Asiento, x.Operador, x.FechaHora, socios));
        }
        return r;
    });

    // ---------- apoyo ----------

    private Task BorrarFilaAsync(int id) => bd.EnTransaccionAsync(async s =>
    {
        await s.EjecutarAsync("DELETE FROM RepartosDividendosSocios WHERE IdRepartoDividendos = ?", id);
        await s.EjecutarAsync("DELETE FROM RepartosDividendos WHERE IdRepartoDividendos = ?", id);
        return true;
    });

    private static bool MismoAsiento(PropuestaDividendos a, PropuestaDividendos b) =>
        a.Fecha == b.Fecha && a.Lineas.Count == b.Lineas.Count && a.Lineas.Zip(b.Lineas).All(x => x.First == x.Second);

    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];

    private static DateTime SinMilisegundos(DateTime d) => new(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Kind);

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
