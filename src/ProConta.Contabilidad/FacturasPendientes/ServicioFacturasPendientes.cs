using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Mantenimiento;

namespace ProConta.Contabilidad.FacturasPendientes;

/// <summary>Estado de las facturas pendientes de recibir del ejercicio.</summary>
public enum EstadoFacturasPendientes
{
    /// <summary>Sin asientos de este proceso.</summary>
    Pendiente,
    /// <summary>La provisión y su reversión coinciden con lo que se propone ahora.</summary>
    Hecho,
    /// <summary>Hay asientos, pero lo que se propone ahora es otra cosa (o falta uno de los dos): hay que repetirlo.</summary>
    Desactualizado,
}

/// <summary>Situación del proceso: estado, los dos asientos grabados, lo que se propone ahora y por qué está desactualizado.</summary>
public sealed record SituacionFacturasPendientes(EstadoFacturasPendientes Estado, int? IdProvision, int? IdReversion, decimal Grabado, PropuestaFacturasPendientes Propuesta,
    string? Motivo, bool EjercicioTerminado, bool ExisteSiguiente);

/// <summary>Resultado de confirmar: los dos asientos (null si no había nada que provisionar) y las cuentas creadas.</summary>
public sealed record ConfirmacionFacturasPendientes(int? IdProvision, int? IdReversion, IReadOnlyList<string> CuentasCreadas, PropuestaFacturasPendientes Propuesta);

/// <summary>
/// Proceso «Facturas pendientes de recibir» (contabilidad/procesos/facturas-pendientes-recibir 2026-11-17 16:40) sobre Access. Propone,
/// para los proveedores y acreedores cuadrados en el Mayor con saldo deudor a 31/12, el gasto por la base supuesta contra la 4009
/// (<see cref="CalculoFacturasPendientes"/>) y, al confirmar, graba en una transacción dos lotes del motor
/// <see cref="OpcionesLote.MotorFacturasPendientes"/>: PROVISION a 31/12 en el ejercicio y REVERSION a 1/1 en el siguiente (clave, el
/// año), exentos del trimestre cerrado como la prorrata, con lo excluido y las bases escritas a mano (FacturasPendientesAjustes).
/// Repetirlo no duplica; deshacer borra los dos lotes. El cierre del ejercicio solo mira su estado y reabrirlo no toca estos lotes.
/// </summary>
public sealed class ServicioFacturasPendientes(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, Func<Task<bool>>? puede = null, IReloj? reloj = null)
{
    public const string Motor = OpcionesLote.MotorFacturasPendientes;
    public const string ProcesoProvision = "PROVISION";
    public const string ProcesoReversion = "REVERSION";

    private static readonly OpcionesLote Opciones = new(TrimestreLiquidado: 4);
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(string proceso, int ejercicio) => new(Motor, proceso, ejercicio.ToString(CultureInfo.InvariantCulture));

    public DateTime Hoy => _reloj.Ahora.Date;

    public bool EjercicioTerminado(ContextoSociedadEjercicio ctx) => Hoy > CalculoFacturasPendientes.FechaProvision(ctx.Ejercicio);

    // ---------- situación ----------

    /// <summary>Lo grabado frente a lo que se propone ahora con los ajustes guardados.</summary>
    public async Task<SituacionFacturasPendientes> SituacionAsync(ContextoSociedadEjercicio ctx)
    {
        var propuesta = (await ProponerAsync(ctx)).Valor;
        var siguiente = await SiguienteAsync(ctx);
        var (provision, idProvision) = await GrabadoAsync(ctx, ProcesoProvision, ctx.Ejercicio);
        var (reversion, idReversion) = siguiente is null ? ([], null) : await GrabadoAsync(siguiente, ProcesoReversion, ctx.Ejercicio);
        var grabado = provision.Sum(l => l.Debe);
        var terminado = EjercicioTerminado(ctx);
        if (idProvision is null && idReversion is null)
            return new(EstadoFacturasPendientes.Pendiente, null, null, 0m, propuesta, null, terminado, siguiente is not null);
        string? motivo = null;
        if (idProvision is null) motivo = "Falta la provisión del 31/12: solo queda su reversión.";
        else if (idReversion is null) motivo = $"Falta la reversión del 1/1/{ctx.Ejercicio + 1}.";
        else if (!provision.SequenceEqual(propuesta.Provision) || !reversion.SequenceEqual(propuesta.Reversion))
            motivo = $"Con los saldos de ahora la provisión es de {CalculoFacturasPendientes.Euros(propuesta.Total)} " +
                $"({Plural(propuesta.Incluidas.Count(), "proveedor", "proveedores")}) y la grabada, de {CalculoFacturasPendientes.Euros(grabado)}" +
                (propuesta.Total == grabado ? ", con otros proveedores, cuentas o centros." : ".");
        return new(motivo is null ? EstadoFacturasPendientes.Hecho : EstadoFacturasPendientes.Desactualizado, idProvision, idReversion, grabado, propuesta, motivo,
            terminado, siguiente is not null);
    }

    // ---------- propuesta ----------

    /// <summary>Propuesta con los ajustes <paramref name="ajustes"/> (null: los guardados en el ejercicio).</summary>
    public async Task<Resultado<PropuestaFacturasPendientes>> ProponerAsync(ContextoSociedadEjercicio ctx, IReadOnlyList<AjusteFacturaPendiente>? ajustes = null)
    {
        // Sin cuenta por defecto implícita: si falta alguna del ejercicio, el proceso se detiene y dice dónde asignarla (organizacion/sociedades-ejercicios 2026-11-18).
        if (await bd.EjecutarAsync(s => CuentasEjercicio.FaltanAsync(s, ctx.IdSociedadEjercicio, "CuentaFacturasPendientesRecibir", "CuentaGastoFacturasPendientes")) is { } faltaCuenta)
            return Resultado<PropuestaFacturasPendientes>.Fallo(faltaCuenta);
        var cfg = await ConfiguracionAsync(ctx);
        var subcuentas = await SubcuentasAsync(ctx, cfg);
        ajustes ??= await AjustesAsync(ctx);
        var p = CalculoFacturasPendientes.Calcular(ctx.Ejercicio, cfg, subcuentas, ajustes);
        var avisos = new List<string>();
        if (!EjercicioTerminado(ctx))
            avisos.Add("El ejercicio aún no ha terminado: la propuesta sale de los saldos y cuadres de hoy. Lo normal es hacerlo en enero, con los proveedores ya cuadrados a 31/12.");
        if (await SiguienteAsync(ctx) is null)
            avisos.Add($"No existe el ejercicio {ctx.Ejercicio + 1}, que recibe la reversión del 1/1: créalo antes de confirmar.");
        return Resultado<PropuestaFacturasPendientes>.Correcto(CalculoFacturasPendientes.ConAvisos(p, avisos));
    }

    /// <summary>Si hace falta motivo para confirmar: el ejercicio no ha terminado o ya estaba hecho (se repite).</summary>
    public static string? PorQueMotivo(bool ejercicioTerminado, bool repetir) =>
        !ejercicioTerminado ? "El ejercicio aún no ha terminado." : repetir ? "Se repite un proceso ya confirmado." : null;

    // ---------- confirmar ----------

    /// <summary>
    /// Graba exactamente la propuesta <paramref name="vista"/> (se vuelve a calcular con <paramref name="ajustes"/>; si ha cambiado, no se
    /// graba): crea las cuentas que falten en los dos ejercicios y graba la provisión, la reversión y los ajustes en una transacción. Sin
    /// proveedores incluidos, borra los asientos que hubiera.
    /// </summary>
    public async Task<Resultado<ConfirmacionFacturasPendientes>> ConfirmarAsync(ContextoSociedadEjercicio ctx, IReadOnlyList<AjusteFacturaPendiente> ajustes, string? motivo,
        Actor actor, PropuestaFacturasPendientes? vista = null)
    {
        Resultado<ConfirmacionFacturasPendientes> Fallo(Error e) => Resultado<ConfirmacionFacturasPendientes>.Fallo(e);
        var registro = CalculoFacturasPendientes.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Fallo(new Error("FacturasPendientes.SinPermiso", "No tienes permiso para contabilizar las facturas pendientes de recibir.", registro,
                "La acción «Contabilizar las facturas pendientes de recibir» tiene grupos asignados (o, sin grupos, la de liquidar el IVA) y el tuyo no está entre ellos.",
                "Pídeselo a quien administra los permisos."));
        if (ajustes.FirstOrDefault(a => a.BaseManual is <= 0m) is { } malo)
            return Fallo(new Error("FacturasPendientes.Base", $"La base de {malo.Cuenta} tiene que ser mayor que cero.", registro,
                "Una base de cero o negativa no es un gasto pendiente.", "Escribe la base correcta o desmarca el proveedor para dejarlo fuera."));
        var siguiente = await SiguienteAsync(ctx);
        if (siguiente is null)
            return Fallo(new Error("FacturasPendientes.SinSiguiente", $"No existe el ejercicio {ctx.Ejercicio + 1}.", registro,
                $"La reversión de la provisión va el 1/1/{ctx.Ejercicio + 1}, en el ejercicio siguiente, y se graba junto con ella.",
                $"Crea el ejercicio {ctx.Ejercicio + 1} con «Crear el ejercicio {ctx.Ejercicio + 1}…» y vuelve a confirmar."));
        var prop = await ProponerAsync(ctx, ajustes);
        if (!prop.EsCorrecto) return Resultado<ConfirmacionFacturasPendientes>.Fallo(prop.Errores);
        var p = prop.Valor;
        if (vista is not null && !vista.Igual(p))
            return Fallo(new Error("FacturasPendientes.Cambiada", "La propuesta ha cambiado desde que la abriste.", registro,
                "Alguien ha contabilizado apuntes de proveedores, cambiado un cuadre del Mayor o los datos de un proveedor mientras la revisabas.",
                "Pulsa «Actualizar», revísala y vuelve a confirmar."));
        var (_, idAnterior) = await GrabadoAsync(ctx, ProcesoProvision, ctx.Ejercicio);
        var (_, idAnteriorReversion) = await GrabadoAsync(siguiente, ProcesoReversion, ctx.Ejercicio);
        var repetir = idAnterior is not null || idAnteriorReversion is not null;
        if (PorQueMotivo(EjercicioTerminado(ctx), repetir) is { } porQue && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("FacturasPendientes.SinMotivo", "Hay que escribir el motivo.", registro, porQue,
                "Escribe el motivo (por ejemplo «Proveedores cuadrados a 31/12 con la gestoría»)."));

        // Cuentas que faltan, en el ejercicio y en el siguiente (la reversión usa las mismas).
        var creadas = new List<string>();
        foreach (var codigo in p.Provision.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal))
            foreach (var c in new[] { ctx, siguiente })
            {
                if ((await cuentas.ResolverAsync(c, codigo)).EsCorrecto) continue;
                // Si el código ya es de una cuenta de la empresa en otro ejercicio, se da de alta esa misma cuenta (su identidad); si no, una nueva.
                var (identidad, nombre) = await CuentaDeLaEmpresaAsync(ctx, codigo, p.Configuracion);
                var alta = identidad is int id ? await cuentas.AltaEnEjercicioAsync(c, id, codigo, nombre) : await cuentas.AltaSubcuentaAsync(c, codigo, nombre);
                if (!alta.EsCorrecto) return Resultado<ConfirmacionFacturasPendientes>.Fallo(alta.Errores);
                creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}» en {c.Ejercicio}");
            }

        var notas = CalculoFacturasPendientes.Notas(p, motivo);
        Asiento Asiento(ContextoSociedadEjercicio c, DateTime fecha, string descripcion, IReadOnlyList<LineaFacturaPendiente> lineas)
        {
            var a = new Asiento(c, fecha, descripcion) { Notas = notas };
            foreach (var l in lineas)
            {
                var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
                ap.IdCentroCoste = l.IdCentroCoste;
                ap.GeneradoAutomaticamente = true;
                ap.MotivoGeneracion = "Facturas pendientes de recibir";
                a.Apuntes.Add(ap);
            }
            return a;
        }
        var hay = p.Provision.Count > 0;
        var lotes = new List<LoteAsientos>
        {
            new(ctx, Lote(ProcesoProvision, ctx.Ejercicio), hay ? [Asiento(ctx, p.Fecha, registro, p.Provision)] : []),
            new(siguiente, Lote(ProcesoReversion, ctx.Ejercicio),
                hay ? [Asiento(siguiente, p.FechaReversion, CalculoFacturasPendientes.DescripcionReversion(ctx.Ejercicio), p.Reversion)] : []),
        };
        var texto = string.IsNullOrWhiteSpace(motivo) ? registro : $"{registro}: {motivo.Trim()}";
        var grabado = await motor.ReemplazarLotesAsync(lotes, actor, texto,
            alFinal: async s => { await GuardarAjustesAsync(s, ctx, p, actor); return Resultado.Correcto(); }, opciones: Opciones);
        if (!grabado.EsCorrecto) return Resultado<ConfirmacionFacturasPendientes>.Fallo(grabado.Errores);
        int? Primero(int i) => grabado.Valor[i].Count > 0 ? grabado.Valor[i][0] : null;
        return Resultado<ConfirmacionFacturasPendientes>.Correcto(new(Primero(0), Primero(1), creadas, p));
    }

    // ---------- deshacer ----------

    /// <summary>Borra la provisión y su reversión, en una transacción y con motivo. Los ajustes a mano se conservan para la próxima propuesta.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, string motivo, Actor actor)
    {
        var registro = CalculoFacturasPendientes.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("FacturasPendientes.SinPermiso", "No tienes permiso para contabilizar las facturas pendientes de recibir.", registro, null,
                "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("FacturasPendientes.SinMotivo", "Hay que escribir el motivo.", registro,
                "Deshacer la provisión queda registrado con su motivo.", "Escribe el motivo."));
        var siguiente = await SiguienteAsync(ctx);
        var (_, idProvision) = await GrabadoAsync(ctx, ProcesoProvision, ctx.Ejercicio);
        var (_, idReversion) = siguiente is null ? ([], null) : await GrabadoAsync(siguiente, ProcesoReversion, ctx.Ejercicio);
        if (idProvision is null && idReversion is null) return Resultado<int>.Correcto(0);
        var lotes = new List<LoteAsientos> { new(ctx, Lote(ProcesoProvision, ctx.Ejercicio), []) };
        if (siguiente is not null) lotes.Add(new(siguiente, Lote(ProcesoReversion, ctx.Ejercicio), []));
        var hecho = await motor.ReemplazarLotesAsync(lotes, actor, $"Deshacer las facturas pendientes de recibir {ctx.Ejercicio}: {motivo.Trim()}", opciones: Opciones);
        if (!hecho.EsCorrecto) return Resultado<int>.Fallo(hecho.Errores);
        return Resultado<int>.Correcto(new[] { idProvision, idReversion }.Count(x => x is not null));
    }

    // ---------- lecturas ----------

    public Task<ConfiguracionFacturasPendientes> ConfiguracionAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        if (!s.LeerEsquema().ExisteColumna("SociedadesEjercicios", "CuentaFacturasPendientesRecibir")) return ConfiguracionFacturasPendientes.Defecto;
        return await s.ConsultarPrimeroAsync(
            "SELECT CuentaFacturasPendientesRecibir, CuentaGastoFacturasPendientes, TipoIvaFacturasPendientes FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => ConfiguracionFacturasPendientes.Con(f.Texto("CuentaFacturasPendientesRecibir"), f.Texto("CuentaGastoFacturasPendientes"), f.DecimalNulo("TipoIvaFacturasPendientes")),
            ctx.IdSociedadEjercicio) ?? ConfiguracionFacturasPendientes.Defecto;
    });

    /// <summary>Ajustes a mano guardados en el ejercicio (proveedores excluidos y bases escritas).</summary>
    public Task<IReadOnlyList<AjusteFacturaPendiente>> AjustesAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlyList<AjusteFacturaPendiente>>(async s =>
        !s.LeerEsquema().ExisteTabla("FacturasPendientesAjustes") ? [] : await s.ConsultarAsync(
            "SELECT CuentaProveedor, Excluido, BaseManual FROM FacturasPendientesAjustes WHERE IdSociedadEjercicio = ?",
            f => new AjusteFacturaPendiente((f.Texto("CuentaProveedor") ?? "").Trim(), f.Logico("Excluido"), f.DecimalNulo("BaseManual")), ctx.IdSociedadEjercicio));

    /// <summary>Guarda los ajustes de las filas de la propuesta (solo los que cambian algo); los de proveedores que ya no están se borran.</summary>
    private async Task GuardarAjustesAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, PropuestaFacturasPendientes p, Actor actor)
    {
        await s.EjecutarAsync("DELETE FROM FacturasPendientesAjustes WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio);
        var ahora = new DateTime(_reloj.Ahora.Year, _reloj.Ahora.Month, _reloj.Ahora.Day, _reloj.Ahora.Hour, _reloj.Ahora.Minute, _reloj.Ahora.Second);
        foreach (var f in p.Filas.Where(f => !f.Incluido || f.BaseManual is not null))
            await s.EjecutarAsync("INSERT INTO FacturasPendientesAjustes (IdSociedadEjercicio, CuentaProveedor, Excluido, BaseManual, Operador, FechaHora) VALUES (?, ?, ?, ?, ?, ?)",
                ctx.IdSociedadEjercicio, f.Cuenta, !f.Incluido, f.BaseManual, Recortar(actor.ToString(), 100), ahora);
    }

    /// <summary>
    /// Las subcuentas 40 y 41 de 7 dígitos con saldo en el ejercicio: saldo a 31/12 (apuntes contabilizados sin el cierre ni los lotes de
    /// este proceso), cuadre del Mayor, última factura contabilizada y cuenta de gasto y centro por defecto del proveedor.
    /// </summary>
    public Task<IReadOnlyList<SubcuentaProveedor>> SubcuentasAsync(ContextoSociedadEjercicio ctx, ConfiguracionFacturasPendientes cfg) =>
        bd.EjecutarAsync<IReadOnlyList<SubcuentaProveedor>>(async s =>
        {
            var saldos = (await s.ConsultarAsync(
                    "SELECT p.CuentaContable, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS Saldo " +
                    "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                    "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 3 AND (a.MotorPropietario IS NULL OR a.MotorPropietario <> ?) " +
                    "AND (p.CuentaContable LIKE '40%' OR p.CuentaContable LIKE '41%') GROUP BY p.CuentaContable",
                    f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Saldo: f.DecimalNulo("Saldo") ?? 0m), ctx.IdSociedadEjercicio, Motor))
                .Where(x => CalculoFacturasPendientes.EsCandidata(x.Cuenta, cfg) && Math.Round(x.Saldo, 2, MidpointRounding.AwayFromZero) > 0m)
                .GroupBy(x => x.Cuenta, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Sum(x => x.Saldo), StringComparer.Ordinal);
            if (saldos.Count == 0) return [];

            var cuadres = s.LeerEsquema().ExisteTabla("CuadresCuentas")
                ? (await s.ConsultarAsync("SELECT CuentaContable, Cuadrada, Saldo FROM CuadresCuentas WHERE IdSociedadEjercicio = ?",
                    f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Cuadrada: f.Logico("Cuadrada"), Saldo: f.DecimalNulo("Saldo")), ctx.IdSociedadEjercicio))
                    .GroupBy(x => x.Cuenta, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal)
                : [];
            var cuentasEjercicio = (await s.ConsultarAsync("SELECT Codigo, Nombre, IdIdentidadContable, EsImputable FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                    f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim(), Identidad: f.EnteroNulo("IdIdentidadContable"), Imputable: f.Logico("EsImputable")),
                    ctx.IdSociedadEjercicio))
                .GroupBy(x => x.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var codigoDeIdentidad = cuentasEjercicio.Values.Where(c => c.Identidad is not null && c.Imputable)
                .GroupBy(c => c.Identidad!.Value).ToDictionary(g => g.Key, g => g.First().Codigo);

            // Cuenta de gasto y centro por defecto del proveedor (CuentasProveedoresSociedad), por la identidad de su subcuenta.
            var defectos = new Dictionary<string, (string? Gasto, int? Centro)>(StringComparer.Ordinal);
            foreach (var d in await s.ConsultarAsync(
                         "SELECT IdIdentidadContable, IdIdentidadGastoDefecto, IdCentroCosteDefecto FROM CuentasProveedoresSociedad WHERE IdSociedad = ? AND IdIdentidadContable IS NOT NULL",
                         f => (Identidad: f.Entero("IdIdentidadContable"), Gasto: f.EnteroNulo("IdIdentidadGastoDefecto"), Centro: f.EnteroNulo("IdCentroCosteDefecto")), ctx.IdSociedad))
            {
                if (!codigoDeIdentidad.TryGetValue(d.Identidad, out var cuenta) || !saldos.ContainsKey(cuenta)) continue;
                var actual = defectos.GetValueOrDefault(cuenta);
                var gasto = d.Gasto is int g && codigoDeIdentidad.TryGetValue(g, out var cg) ? cg : null;
                defectos[cuenta] = (actual.Gasto ?? gasto, actual.Centro ?? d.Centro);
            }

            // Última factura de compra contabilizada del proveedor en la empresa (cualquier ejercicio, hasta el 31/12).
            var ultimas = new Dictionary<string, FacturaReferencia>(StringComparer.Ordinal);
            foreach (var f in await s.ConsultarAsync(
                         "SELECT CuentaContableProveedor, Fecha, NumeroFactura, ImporteBI, ImporteTotal, ConceptoGasto, IdCentroCoste FROM FacturasCompra " +
                         "WHERE Sociedad = ? AND Exportada = True AND Fecha <= ? AND ImporteBI IS NOT NULL AND ImporteTotal IS NOT NULL " +
                         "AND (CuentaContableProveedor LIKE '40%' OR CuentaContableProveedor LIKE '41%') ORDER BY Fecha, IdRegistro",
                         r => (Cuenta: (r.Texto("CuentaContableProveedor") ?? "").Trim(), Factura: new FacturaReferencia(r.Fecha("Fecha"), r.Texto("NumeroFactura")?.Trim(),
                             r.Decimal("ImporteBI"), r.Decimal("ImporteTotal"), r.Texto("ConceptoGasto")?.Trim(), r.EnteroNulo("IdCentroCoste"))),
                         ctx.IdSociedad, CalculoFacturasPendientes.FechaProvision(ctx.Ejercicio)))
                if (saldos.ContainsKey(f.Cuenta)) ultimas[f.Cuenta] = f.Factura;

            // Los centros que no admiten apuntes (inactivos o de grupo) no se proponen.
            var centros = (await s.ConsultarAsync("SELECT IdCentroCoste FROM CentrosCoste WHERE IdSociedad = ? AND EstaActivo = True AND EsImputable = True",
                f => f.Entero("IdCentroCoste"), ctx.IdSociedad)).ToHashSet();
            int? Centro(int? c) => c is int x && centros.Contains(x) ? x : null;

            var r = new List<SubcuentaProveedor>();
            foreach (var (cuenta, saldo) in saldos)
            {
                var cuadre = cuadres.GetValueOrDefault(cuenta);
                var marcada = cuadre.Cuenta is not null && cuadre.Cuadrada;
                var mismoSaldo = cuadre.Saldo is not decimal m || Math.Abs(m - saldo) < 0.005m;
                var (gasto, centro) = defectos.GetValueOrDefault(cuenta);
                var ultima = ultimas.GetValueOrDefault(cuenta);
                if (ultima is not null) ultima = ultima with { IdCentroCoste = Centro(ultima.IdCentroCoste) };
                r.Add(new SubcuentaProveedor(cuenta, cuentasEjercicio.TryGetValue(cuenta, out var c) ? c.Nombre : "", saldo, marcada && mismoSaldo, marcada && !mismoSaldo,
                    ultima, gasto, Centro(centro)));
            }
            return r;
        });

    /// <summary>Los apuntes grabados de un lote de este proceso (en el orden del asiento) y el primer asiento.</summary>
    private Task<(IReadOnlyList<LineaFacturaPendiente> Lineas, int? IdAsiento)> GrabadoAsync(ContextoSociedadEjercicio ctx, string proceso, int ejercicio) =>
        bd.EjecutarAsync<(IReadOnlyList<LineaFacturaPendiente>, int?)>(async s =>
        {
            var lote = Lote(proceso, ejercicio);
            var filas = await s.ConsultarAsync(
                "SELECT a.IdAsiento, p.Orden, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.Descripcion, p.IdCentroCoste FROM Asientos AS a LEFT JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.ClaveEjecucion = ? ORDER BY a.IdAsiento, p.Orden",
                f => (Id: f.Entero("IdAsiento"), Cuenta: f.Texto("CuentaContable")?.Trim(), Debe: f.DecimalNulo("ImporteDebe") ?? 0m, Haber: f.DecimalNulo("ImporteHaber") ?? 0m,
                    Concepto: f.Texto("Descripcion") ?? "", Centro: f.EnteroNulo("IdCentroCoste")),
                ctx.IdSociedadEjercicio, lote.MotorPropietario, lote.CodigoProceso, lote.ClaveEjecucion);
            if (filas.Count == 0) return ([], null);
            return (filas.Where(x => x.Cuenta is not null).Select(x => new LineaFacturaPendiente(x.Cuenta!, x.Debe, x.Haber, x.Concepto, x.Centro)).ToList(), filas.Min(x => x.Id));
        });

    /// <summary>
    /// La cuenta con ese código en otro ejercicio de la empresa (su identidad y nombre, del más reciente) para dar de alta una que falta; si
    /// no existe en ninguno, sin identidad y con el nombre de siempre para las del proceso.
    /// </summary>
    private Task<(int? Identidad, string Nombre)> CuentaDeLaEmpresaAsync(ContextoSociedadEjercicio ctx, string codigo, ConfiguracionFacturasPendientes cfg) =>
        bd.EjecutarAsync<(int?, string)>(async s =>
        {
            var fila = await s.ConsultarPrimeroAsync(
                "SELECT TOP 1 c.IdIdentidadContable, c.Nombre FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS e ON c.IdSociedadEjercicio = e.IdSociedadEjercicio " +
                "WHERE e.IdSociedad = ? AND c.Codigo = ? ORDER BY e.Ejercicio DESC",
                f => (Identidad: f.EnteroNulo("IdIdentidadContable"), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedad, codigo);
            if (fila.Identidad is not null && fila.Nombre.Length > 0) return (fila.Identidad, fila.Nombre);
            return (null, codigo == cfg.CuentaPendientes ? "Proveedores, facturas pendientes de recibir o de formalizar"
                : codigo == cfg.CuentaGasto ? "Otros servicios" : $"Cuenta {codigo}");
        });

    /// <summary>Nombres de los centros de coste de la empresa, para enseñarlos en la propuesta.</summary>
    public Task<IReadOnlyDictionary<int, string>> NombresCentrosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlyDictionary<int, string>>(async s =>
        (await s.ConsultarAsync("SELECT IdCentroCoste, Nombre FROM CentrosCoste WHERE IdSociedad = ?", f => (Id: f.Entero("IdCentroCoste"), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedad))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Nombre));

    public Task<ContextoSociedadEjercicio?> SiguienteAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var v = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?",
            ctx.IdSociedad, (ctx.Ejercicio + 1).ToString(CultureInfo.InvariantCulture));
        return v is null or DBNull ? null : new ContextoSociedadEjercicio(ctx.IdSociedad, Convert.ToInt32(v, CultureInfo.InvariantCulture), ctx.Ejercicio + 1);
    });

    private static string Recortar(string? t, int n) => t is null ? "" : t.Length <= n ? t : t[..n];

    private static string Plural(int n, string uno, string varios) => $"{n.ToString("#,##0", CultureInfo.GetCultureInfo("es-ES"))} {(n == 1 ? uno : varios)}";
}
