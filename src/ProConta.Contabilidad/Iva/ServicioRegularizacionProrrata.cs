using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Iva;

/// <summary>Estado de la regularización de la prorrata del ejercicio.</summary>
public enum EstadoRegularizacionProrrata
{
    /// <summary>Sin definitiva fijada ni asiento.</summary>
    Pendiente,
    /// <summary>La definitiva está fijada y el asiento (o su ausencia, si no hay nada que regularizar) coincide con lo que se calcula ahora.</summary>
    Regularizada,
    /// <summary>Hay definitiva o asiento, pero lo que se calcula ahora es otra cosa: hay que repetirla.</summary>
    Desactualizada,
}

/// <summary>Situación de la regularización: la definitiva guardada, el asiento grabado y lo que se calcula ahora con esa definitiva.</summary>
public sealed record SituacionRegularizacionProrrata(EstadoRegularizacionProrrata Estado, decimal? Definitiva, int? IdAsiento, decimal Grabada,
    decimal Calculada, string? Motivo, bool EjercicioTerminado);

/// <summary>Resultado de confirmar: el asiento (null si no hacía falta), si se copió la definitiva al ejercicio siguiente y las cuentas creadas.</summary>
public sealed record ConfirmacionRegularizacionProrrata(int? IdAsiento, int? EjercicioSiguiente, IReadOnlyList<string> CuentasCreadas, PropuestaRegularizacionProrrata Propuesta);

/// <summary>
/// Proceso «Prorrata de IVA» (contabilidad/procesos/prorrata) sobre Access: propone la regularización de la prorrata del ejercicio
/// con la definitiva (por defecto, la legal), guarda la definitiva en el ejercicio (SociedadesEjercicios.PorcentajeProrrataDefinitiva,
/// en porcentaje) y la copia como provisional del ejercicio siguiente si existe, y graba el asiento del 31/12 como lote propio
/// regenerable (repetirlo no duplica). El lote es del motor de la liquidación del IVA, para poder grabar en el 4T aunque esté cerrado.
/// </summary>
public sealed class ServicioRegularizacionProrrata(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, Func<Task<bool>>? puede = null, IReloj? reloj = null)
{
    public const string CodigoProceso = "REGULARIZACION_PRORRATA";
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(int ejercicio) => new(OpcionesLote.MotorLiquidacionIva, CodigoProceso, ejercicio.ToString(CultureInfo.InvariantCulture));

    public DateTime Hoy => _reloj.Ahora.Date;

    public bool EjercicioTerminado(ContextoSociedadEjercicio ctx) => Hoy > CalculoRegularizacionProrrata.FechaAsiento(ctx.Ejercicio);

    /// <summary>Lo guardado (definitiva y asiento) frente a lo que se calcula ahora con esa definitiva.</summary>
    public async Task<SituacionRegularizacionProrrata> SituacionAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var (_, _, definitiva, iva) = await new LectorLineasIva(bd).CalcularAsync(ctx, c);
        var (idAsiento, grabada) = await GrabadaAsync(ctx);
        var calculada = definitiva is null ? 0m : iva.Prorrata.Regularizacion(0);
        var terminado = EjercicioTerminado(ctx);
        if (definitiva is null && idAsiento is null)
            return new(EstadoRegularizacionProrrata.Pendiente, null, null, 0m, 0m, null, terminado);
        string? motivo = null;
        if (definitiva is null) motivo = "Hay asiento de regularización, pero el ejercicio ya no tiene la prorrata definitiva.";
        else if (idAsiento is int id && await motor.ObtenerAsync(id) is null) motivo = "Su asiento ya no existe.";
        else if (grabada != calculada)
            motivo = $"Con los apuntes de ahora la regularización es de {Euros(calculada)} y el asiento tiene {Euros(grabada)}.";
        return new(motivo is null ? EstadoRegularizacionProrrata.Regularizada : EstadoRegularizacionProrrata.Desactualizada, definitiva, idAsiento, grabada, calculada,
            motivo, terminado);
    }

    /// <summary>
    /// Propuesta con la definitiva <paramref name="definitiva"/> (en tanto por uno); null: la guardada en el ejercicio o, si no hay, la legal.
    /// </summary>
    public async Task<Resultado<PropuestaRegularizacionProrrata>> ProponerAsync(ContextoSociedadEjercicio ctx, decimal? definitiva = null, CancellationToken c = default)
    {
        if (definitiva is < 0m or > 1m)
            return Resultado<PropuestaRegularizacionProrrata>.Fallo(new Error("Prorrata.Porcentaje", "La prorrata definitiva va de 0 % a 100 %.",
                $"Prorrata {ctx.Ejercicio}", null, "Escríbela en porcentaje, por ejemplo 20."));
        var lector = new LectorLineasIva(bd);
        var (lineas, provisional, guardada, iva) = await lector.CalcularAsync(ctx, c);
        var aplicar = definitiva ?? guardada ?? iva.Prorrata.Real;
        var conDefinitiva = CalculoIvaEjercicio.Calcular(lineas, provisional, aplicar);
        var propuesta = CalculoRegularizacionProrrata.Calcular(new DatosRegularizacionProrrata(ctx.Ejercicio, conDefinitiva, lineas, await CuentasAsync(ctx)));
        if (!EjercicioTerminado(ctx))
            propuesta = propuesta with
            {
                Avisos = [$"El ejercicio aún no ha terminado: la prorrata y la regularización son provisionales con lo contabilizado hasta hoy. La definitiva se fija cuando la gestoría la dé, en enero.",
                    .. propuesta.Avisos],
            };
        return Resultado<PropuestaRegularizacionProrrata>.Correcto(propuesta);
    }

    /// <summary>Si hace falta motivo para confirmar: el ejercicio no ha terminado o ya había una regularización (se repite).</summary>
    public static string? PorQueMotivo(bool ejercicioTerminado, bool repetir) =>
        !ejercicioTerminado ? "El ejercicio aún no ha terminado." : repetir ? "Se repite una regularización ya confirmada." : null;

    /// <summary>
    /// Guarda la definitiva (y la copia como provisional del ejercicio siguiente, si existe) y graba exactamente la propuesta que resulta
    /// de ella (se vuelve a calcular en el momento); si la regularización es cero, no hay asiento y se borra el que hubiera.
    /// </summary>
    public async Task<Resultado<ConfirmacionRegularizacionProrrata>> ConfirmarAsync(ContextoSociedadEjercicio ctx, decimal definitiva, string? motivo, Actor actor,
        PropuestaRegularizacionProrrata? vista = null)
    {
        Resultado<ConfirmacionRegularizacionProrrata> Fallo(Error e) => Resultado<ConfirmacionRegularizacionProrrata>.Fallo(e);
        var registro = CalculoRegularizacionProrrata.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Fallo(new Error("Prorrata.SinPermiso", "No tienes permiso para regularizar la prorrata.", registro,
                "La regularización de la prorrata va con la acción «Liquidar el IVA», que tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos."));
        var prop = await ProponerAsync(ctx, definitiva);
        if (!prop.EsCorrecto) return Resultado<ConfirmacionRegularizacionProrrata>.Fallo(prop.Errores);
        var p = prop.Valor;
        if (vista is not null && (vista.Definitiva != p.Definitiva || !vista.Lineas.SequenceEqual(p.Lineas)))
            return Fallo(new Error("Prorrata.Cambiada", "La propuesta ha cambiado desde que la abriste.", registro,
                "Alguien ha contabilizado o cambiado apuntes de IVA del ejercicio mientras la revisabas.", "Vuelve a calcularla y revísala antes de confirmar."));
        var (idAnterior, _) = await GrabadaAsync(ctx);
        var guardada = await new LectorLineasIva(bd).ProrrataDefinitivaAsync(ctx);
        var repetir = idAnterior is not null || guardada is not null;
        if (PorQueMotivo(EjercicioTerminado(ctx), repetir) is { } porQue && string.IsNullOrWhiteSpace(motivo))
            return Fallo(new Error("Prorrata.SinMotivo", "Hay que escribir el motivo.", registro, porQue, "Escribe el motivo (por ejemplo «Prorrata definitiva que da la gestoría»)."));

        var siguiente = await IdEjercicioAsync(ctx.IdSociedad, ctx.Ejercicio + 1);
        var porcentaje = Math.Round(p.Definitiva * 100m, 4, MidpointRounding.AwayFromZero);
        Func<ISesionDatos, Task> guardar = async s =>
        {
            await s.EjecutarAsync("UPDATE SociedadesEjercicios SET PorcentajeProrrataDefinitiva = ? WHERE IdSociedadEjercicio = ?", porcentaje, ctx.IdSociedadEjercicio);
            // La definitiva de este año es la provisional del siguiente (organizacion/sociedades-ejercicios).
            if (siguiente is int id)
                await s.EjecutarAsync("UPDATE SociedadesEjercicios SET [PorcentajeProrrataAñoAnterior] = ? WHERE IdSociedadEjercicio = ?", (double)porcentaje, id);
        };
        var trabajo = new TrabajoLote(Despues: async (s, _) => { await guardar(s); return Resultado.Correcto(); });
        var texto = string.IsNullOrWhiteSpace(motivo) ? registro : $"{registro}: {motivo.Trim()}";
        var opciones = new OpcionesLote(TrimestreLiquidado: 4);

        if (p.Lineas.Count == 0)
        {
            if (idAnterior is not null)
            {
                var borrado = await motor.EliminarLoteAsync(ctx, Lote(ctx.Ejercicio), actor, texto, trabajo, opciones);
                if (!borrado.EsCorrecto) return Resultado<ConfirmacionRegularizacionProrrata>.Fallo(borrado.Errores);
            }
            else
                await bd.EjecutarAsync(async s => { await guardar(s); return 0; });
            return Resultado<ConfirmacionRegularizacionProrrata>.Correcto(new(null, siguiente is null ? null : ctx.Ejercicio + 1, [], p));
        }

        // Cuentas que aún no existen en el ejercicio: se crean, colgadas de su cuenta.
        var config = await CuentasAsync(ctx);
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [config.Regularizacion] = "Regularización de la prorrata del IVA soportado",
            [config.AjustesNegativos] = "Ajustes negativos en IVA de activo corriente",
            [config.AjustesPositivos] = "Ajustes positivos en IVA de activo corriente",
        };
        var creadas = new List<string>();
        foreach (var codigo in p.Lineas.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal))
        {
            if ((await cuentas.ResolverAsync(ctx, codigo)).EsCorrecto) continue;
            if (!nombres.TryGetValue(codigo, out var nombre)) continue;
            var alta = await cuentas.AltaSubcuentaAsync(ctx, codigo, nombre);
            if (!alta.EsCorrecto) return Resultado<ConfirmacionRegularizacionProrrata>.Fallo(alta.Errores);
            creadas.Add($"{alta.Valor.Codigo} «{alta.Valor.Nombre}»");
        }

        var asiento = new Asiento(ctx, p.Fecha, registro) { Notas = CalculoRegularizacionProrrata.Notas(p, motivo) };
        foreach (var l in p.Lineas)
        {
            var ap = l.Debe != 0m ? Apunte.AlDebe(l.Cuenta, l.Debe, l.Concepto) : Apunte.AlHaber(l.Cuenta, l.Haber, l.Concepto);
            ap.IdCentroCoste = l.IdCentroCoste;
            ap.GeneradoAutomaticamente = true;
            ap.MotivoGeneracion = "Regularización de la prorrata";
            asiento.Apuntes.Add(ap);
        }
        var grabado = await motor.ReemplazarLoteAsync(ctx, Lote(ctx.Ejercicio), [asiento], actor, texto, trabajo, opciones);
        if (!grabado.EsCorrecto) return Resultado<ConfirmacionRegularizacionProrrata>.Fallo(grabado.Errores);
        return Resultado<ConfirmacionRegularizacionProrrata>.Correcto(new(grabado.Valor[0], siguiente is null ? null : ctx.Ejercicio + 1, creadas, p));
    }

    /// <summary>Borra el asiento de la regularización y quita la definitiva del ejercicio. No toca la provisional del ejercicio siguiente.</summary>
    public async Task<Resultado<int>> DeshacerAsync(ContextoSociedadEjercicio ctx, string motivo, Actor actor)
    {
        var registro = CalculoRegularizacionProrrata.Descripcion(ctx.Ejercicio);
        if (puede is not null && !await puede())
            return Resultado<int>.Fallo(new Error("Prorrata.SinPermiso", "No tienes permiso para regularizar la prorrata.", registro, null, "Pídeselo a quien administra los permisos."));
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<int>.Fallo(new Error("Prorrata.SinMotivo", "Hay que escribir el motivo.", registro, "Deshacer la regularización queda registrado con su motivo.", "Escribe el motivo."));
        async Task Quitar(ISesionDatos s) =>
            await s.EjecutarAsync("UPDATE SociedadesEjercicios SET PorcentajeProrrataDefinitiva = NULL WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio);
        var (id, _) = await GrabadaAsync(ctx);
        if (id is null)
        {
            await bd.EjecutarAsync(async s => { await Quitar(s); return 0; });
            return Resultado<int>.Correcto(0);
        }
        var trabajo = new TrabajoLote(Despues: async (s, _) => { await Quitar(s); return Resultado.Correcto(); });
        return await motor.EliminarLoteAsync(ctx, Lote(ctx.Ejercicio), actor, $"Deshacer la regularización de la prorrata {ctx.Ejercicio}: {motivo.Trim()}", trabajo,
            new OpcionesLote(TrimestreLiquidado: 4));
    }

    // ---------- lecturas ----------

    /// <summary>El asiento del lote de la regularización (si lo hay) y su importe en la 4729 (Debe − Haber).</summary>
    public Task<(int? IdAsiento, decimal Importe)> GrabadaAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<(int? IdAsiento, decimal Importe)>(async s =>
    {
        var lote = Lote(ctx.Ejercicio);
        var filas = await s.ConsultarAsync(
            "SELECT a.IdAsiento, p.CuentaContable, p.ImporteDebe, p.ImporteHaber FROM Asientos AS a LEFT JOIN Apuntes AS p ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.ClaveEjecucion = ?",
            f => (Id: f.Entero("IdAsiento"), Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Debe: f.DecimalNulo("ImporteDebe") ?? 0m, Haber: f.DecimalNulo("ImporteHaber") ?? 0m),
            ctx.IdSociedadEjercicio, lote.MotorPropietario, lote.CodigoProceso, lote.ClaveEjecucion);
        if (filas.Count == 0) return ((int?)null, 0m);
        return ((int?)filas.Min(x => x.Id), filas.Where(x => CalculoLiquidacionIva.EsRegularizacionProrrata(x.Cuenta)).Sum(x => x.Debe - x.Haber));
    });

    public Task<CuentasRegularizacionProrrata> CuentasAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync(
            "SELECT CuentaRegularizacionProrrata, CuentaAjustesNegativosIva, CuentaAjustesPositivosIva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => CuentasRegularizacionProrrata.Con(f.Texto("CuentaRegularizacionProrrata"),
                CuentasLiquidacionIva.Con(null, null, f.Texto("CuentaAjustesNegativosIva"), f.Texto("CuentaAjustesPositivosIva"))),
            ctx.IdSociedadEjercicio) ?? CuentasRegularizacionProrrata.Defecto);

    private Task<int?> IdEjercicioAsync(string idSociedad, int ejercicio) => bd.EjecutarAsync<int?>(async s =>
    {
        var v = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?",
            idSociedad, ejercicio.ToString(CultureInfo.InvariantCulture));
        return v is null or DBNull ? (int?)null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    });

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
