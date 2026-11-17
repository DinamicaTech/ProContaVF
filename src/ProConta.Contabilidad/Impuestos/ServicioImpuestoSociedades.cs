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
/// <param name="Retenciones">Lo deducido de las 473.</param>
/// <param name="SaldoRetenciones">Suma de los saldos deudores de las 473 al confirmar (para saber si han cambiado).</param>
public sealed record ImpuestoSociedadesGuardado(decimal ResultadoContable, decimal BaseImponible, decimal CuotaLiquida, decimal Retenciones, decimal Resultado,
    string? Motivo, int? IdAsiento, string? Operador, DateTime FechaHora, EntradaImpuestoSociedades Entrada, decimal SaldoRetenciones = 0m);

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
            else if (totalRet != g.SaldoRetenciones)
                motivo = $"Las 473 suman ahora {CalculoImpuestoSociedades.Euros(totalRet)} y al contabilizarlo {CalculoImpuestoSociedades.Euros(g.SaldoRetenciones)}.";
        }
        return new(motivo is null ? EstadoImpuestoSociedades.Contabilizado : EstadoImpuestoSociedades.Desactualizado, g, motivo, terminado);
    }

    /// <summary>Propuesta con lo escrito; null: lo guardado al confirmar o, si no hay, la propuesta sin ajustes.</summary>
    public async Task<Resultado<PropuestaImpuestoSociedades>> ProponerAsync(ContextoSociedadEjercicio ctx, EntradaImpuestoSociedades? entrada = null, CancellationToken c = default)
    {
        entrada ??= (await GuardadoAsync(ctx))?.Entrada ?? EntradaImpuestoSociedades.Vacia;
        if (CalculoImpuestoSociedades.Validar(entrada) is { } error)
            return Resultado<PropuestaImpuestoSociedades>.Fallo(new Error("ImpuestoSociedades.Entrada", error, CalculoImpuestoSociedades.Descripcion(ctx.Ejercicio)));
        return Resultado<PropuestaImpuestoSociedades>.Correcto(Calcular(ctx, await DatosAsync(ctx, c), entrada));
    }

    /// <summary>
    /// La propuesta con unos datos ya leídos (la pantalla los lee una vez y recalcula al escribir): el cálculo y, si el ejercicio no ha
    /// terminado, el aviso de que es provisional. Lo escrito tiene que ser válido (<see cref="CalculoImpuestoSociedades.Validar"/>).
    /// </summary>
    public PropuestaImpuestoSociedades Calcular(ContextoSociedadEjercicio ctx, DatosImpuestoSociedades d, EntradaImpuestoSociedades entrada)
    {
        var p = CalculoImpuestoSociedades.Calcular(d, entrada);
        if (!EjercicioTerminado(ctx))
            p = p with
            {
                Avisos = ["El ejercicio aún no ha terminado: el cálculo es provisional con lo contabilizado hasta hoy. Se contabiliza en enero, antes de la regularización, con las cifras de la gestoría.",
                    .. p.Avisos],
            };
        return p;
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
        if (p.PorQueNoContraste is { } contraste)
            return Fallo(new Error("ImpuestoSociedades.Contraste", "Falta completar el contraste de retenciones.", registro, contraste,
                "Ábrelo desde la pantalla del impuesto y complétalo."));
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

    // ---------- datos fiscales de la AEAT ----------

    /// <summary>Dirección de la página de la Sede con el enlace a los datos fiscales (Configuración general; vacía, la de siempre).</summary>
    public async Task<string> UrlDatosFiscalesAsync()
    {
        var v = await bd.EjecutarAsync(s => s.EscalarAsync("SELECT TOP 1 UrlDatosFiscalesIS FROM Configuracion ORDER BY IdConfiguracion")) as string;
        return string.IsNullOrWhiteSpace(v) ? LectorDatosFiscalesAeat.UrlDefecto : v.Trim();
    }

    /// <summary>
    /// Lee el HTML de los datos fiscales guardado desde la Sede, comprueba que es de esta sociedad y este ejercicio, y asigna a cada
    /// retención su 473 (<see cref="LectorDatosFiscalesAeat.Asignar"/>).
    /// </summary>
    public async Task<Resultado<DatosFiscalesAeat>> ImportarDatosFiscalesAsync(ContextoSociedadEjercicio ctx, byte[] contenido, DatosImpuestoSociedades datos)
    {
        Resultado<DatosFiscalesAeat> Fallo(string mensaje, string? causa, string accion) =>
            Resultado<DatosFiscalesAeat>.Fallo(new Error("ImpuestoSociedades.DatosFiscales", mensaje, CalculoImpuestoSociedades.Descripcion(ctx.Ejercicio), causa, accion));
        var leidos = LectorDatosFiscalesAeat.Leer(contenido);
        if (leidos.Ejercicio is null)
            return Fallo("El fichero no son los datos fiscales del Impuesto sobre Sociedades.", leidos.Avisos.FirstOrDefault(),
                "Ábrelos en la Sede, guárdalos con «Guardar página como…» y elige ese fichero.");
        if (leidos.Ejercicio != ctx.Ejercicio)
            return Fallo($"Son los datos fiscales de {leidos.Ejercicio} y estás en {ctx.Ejercicio}.", null, "Descarga los del ejercicio de trabajo o cambia de ejercicio.");
        var nif = await bd.EjecutarAsync(s => s.EscalarAsync("SELECT CIF FROM Sociedades WHERE IdSociedad = ?", ctx.IdSociedad)) as string;
        static string Normal(string? n) => new((n ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (leidos.Nif is not null && Normal(nif).Length > 0 && Normal(nif) != Normal(leidos.Nif))
            return Fallo($"Son los datos fiscales del NIF {leidos.Nif} y la sociedad abierta tiene el {nif!.Trim()}.", null, "Descarga los de esta sociedad.");
        return Resultado<DatosFiscalesAeat>.Correcto(leidos with { Retenciones = LectorDatosFiscalesAeat.Asignar(leidos.Retenciones, datos.Retenciones) });
    }

    // ---------- grabación ----------

    private async Task GuardarAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, PropuestaImpuestoSociedades p, string? motivo, Actor actor, int? idAsiento)
    {
        await BorrarGuardadoAsync(s, ctx);
        await s.EjecutarAsync(
            "INSERT INTO ImpuestosSociedades (IdSociedadEjercicio, ResultadoContable, AjustesAumentos, AjustesDisminuciones, BasesNegativas, BaseImponible, TipoGeneral, " +
            "LimiteTramoReducido, TipoTramoReducido, CuotaIntegra, Bonificaciones, Deducciones, CuotaCalculada, CuotaLiquida, Retenciones, CreditoPerdidas, Resultado, " +
            "Motivo, IdAsiento, Operador, FechaHoraConfirmacion, SaldoRetenciones, BasesNegativasAeat, ResultadoPresentadoAeat, RentaViviendas, RentaViviendasCalculada) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            ctx.IdSociedadEjercicio, p.ResultadoContable, p.AumentosPermanentes + p.AumentosTemporales, p.DisminucionesPermanentes + p.DisminucionesTemporales,
            p.BasesNegativas, p.BaseImponible, Porcentaje(p.TipoGeneral), p.LimiteTramo, p.TipoTramo is decimal t ? Porcentaje(t) : null, p.CuotaIntegra,
            p.Bonificaciones, p.Deducciones, p.CuotaCalculada, p.CuotaLiquida, p.TotalRetenciones, p.CreditoPerdidas, p.Resultado,
            string.IsNullOrWhiteSpace(motivo) ? null : Recortar(motivo.Trim(), 255), idAsiento, Recortar(actor.ToString(), 100), _reloj.Ahora,
            p.TotalSaldos473, p.Entrada.BasesNegativasAeat, p.Entrada.ResultadoPresentado, p.RentaViviendas, p.RentaViviendasCalculada);
        var idImpuesto = await s.UltimoIdAsync();
        var e = p.Entrada;
        foreach (var r in p.FilasContraste)
            await s.EjecutarAsync(
                "INSERT INTO ImpuestosSociedadesRetenciones (IdImpuestoSociedades, Clave, Pagador, Nif, Cuenta, Hacienda, Contable, Propuesto, Valor, Escrito, Justificacion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                idImpuesto, Recortar(r.Clave, 80), Recortar(r.Pagador, 120), string.IsNullOrWhiteSpace(r.Nif) ? null : Recortar(r.Nif.Trim(), 20), r.Cuenta, r.Hacienda,
                r.Contable, r.Propuesto, r.Valor, r.Escrito, r.Justificacion is null ? null : Recortar(r.Justificacion, 255));
        var aeat = e.RetencionesAeat ?? [];
        for (var i = 0; i < aeat.Count; i++)
        {
            var a = aeat[i];
            await s.EjecutarAsync(
                "INSERT INTO ImpuestosSociedadesDatosAeat (IdImpuestoSociedades, Orden, Modelo, Nif, Nombre, Importe, Cuenta) VALUES (?, ?, ?, ?, ?, ?, ?)",
                idImpuesto, i + 1, Recortar(a.Modelo, 10), string.IsNullOrWhiteSpace(a.Nif) ? null : Recortar(a.Nif.Trim(), 20), Recortar(a.Nombre.Trim(), 120), a.Importe,
                string.IsNullOrWhiteSpace(a.Cuenta) ? null : a.Cuenta.Trim());
        }
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
        foreach (var tabla in (string[])["ImpuestosSociedadesAjustes", "ImpuestosSociedadesRetenciones", "ImpuestosSociedadesDatosAeat"])
            await s.EjecutarAsync($"DELETE FROM {tabla} WHERE IdImpuestoSociedades IN (SELECT IdImpuestoSociedades FROM ImpuestosSociedades WHERE IdSociedadEjercicio = ?)",
                ctx.IdSociedadEjercicio);
        await s.EjecutarAsync("DELETE FROM ImpuestosSociedades WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio);
    }

    // ---------- lecturas ----------

    /// <summary>Lo guardado al confirmar, con sus ajustes; null si no hay.</summary>
    public Task<ImpuestoSociedadesGuardado?> GuardadoAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<ImpuestoSociedadesGuardado?>(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT IdImpuestoSociedades, ResultadoContable, BasesNegativas, BaseImponible, Bonificaciones, Deducciones, CuotaCalculada, CuotaLiquida, Retenciones, " +
            "CreditoPerdidas, Resultado, Motivo, IdAsiento, Operador, FechaHoraConfirmacion, SaldoRetenciones, BasesNegativasAeat, ResultadoPresentadoAeat, " +
            "RentaViviendas, RentaViviendasCalculada " +
            "FROM ImpuestosSociedades WHERE IdSociedadEjercicio = ?",
            f => (Id: f.Entero("IdImpuestoSociedades"), Resultado: Dec(f, "ResultadoContable"), Bases: Dec(f, "BasesNegativas"), Base: Dec(f, "BaseImponible"),
                Bonif: Dec(f, "Bonificaciones"), Deduc: Dec(f, "Deducciones"), Calculada: Dec(f, "CuotaCalculada"), Cuota: Dec(f, "CuotaLiquida"),
                Ret: Dec(f, "Retenciones"), Credito: Dec(f, "CreditoPerdidas"), Saldo: Dec(f, "Resultado"), Motivo: f.Texto("Motivo"), IdAsiento: f.EnteroNulo("IdAsiento"),
                Operador: f.Texto("Operador"), Fecha: f.Fecha("FechaHoraConfirmacion"), SaldoRet: Dec(f, "SaldoRetenciones"),
                BasesAeat: f.DecimalNulo("BasesNegativasAeat"), Presentado: f.DecimalNulo("ResultadoPresentadoAeat"),
                Renta: Dec(f, "RentaViviendas"), RentaCalculada: Dec(f, "RentaViviendasCalculada")),
            ctx.IdSociedadEjercicio);
        if (filas.Count == 0) return null;
        var g = filas[0];
        var ajustes = await s.ConsultarAsync(
            "SELECT Temporal, Aumento, Descripcion, Cuenta, Importe FROM ImpuestosSociedadesAjustes WHERE IdImpuestoSociedades = ? ORDER BY Orden",
            f => new AjusteImpuestoSociedades(f.Logico("Temporal"), f.Logico("Aumento"), (f.Texto("Descripcion") ?? "").Trim(), Dec(f, "Importe"),
                string.IsNullOrWhiteSpace(f.Texto("Cuenta")) ? null : f.Texto("Cuenta")!.Trim()),
            g.Id);
        var valores = await s.ConsultarAsync(
            "SELECT Clave, Cuenta, Valor, Escrito, Justificacion FROM ImpuestosSociedadesRetenciones WHERE IdImpuestoSociedades = ? " +
            "AND (Escrito = True OR Justificacion Is Not Null OR Left(Clave, 5) = 'AEAT|') ORDER BY Clave",
            f => new ValorRetencion((f.Texto("Clave") ?? "").Trim(), f.Logico("Escrito") ? Dec(f, "Valor") : null,
                string.IsNullOrWhiteSpace(f.Texto("Justificacion")) ? null : f.Texto("Justificacion")!.Trim(),
                // La 473 solo se elige en las filas que solo conoce Hacienda; en las demás es la de sus apuntes.
                (f.Texto("Clave") ?? "").StartsWith("AEAT|", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(f.Texto("Cuenta")) ? f.Texto("Cuenta")!.Trim() : null), g.Id);
        var aeat = await s.ConsultarAsync(
            "SELECT Modelo, Nif, Nombre, Importe, Cuenta FROM ImpuestosSociedadesDatosAeat WHERE IdImpuestoSociedades = ? ORDER BY Orden",
            f => new RetencionAeat((f.Texto("Modelo") ?? "").Trim(), string.IsNullOrWhiteSpace(f.Texto("Nif")) ? null : f.Texto("Nif")!.Trim(), (f.Texto("Nombre") ?? "").Trim(),
                Dec(f, "Importe"), string.IsNullOrWhiteSpace(f.Texto("Cuenta")) ? null : f.Texto("Cuenta")!.Trim()),
            g.Id);
        // Lo escrito: la bonificación y el crédito tal como se grabaron, la cuota solo si se fijó a mano, lo escrito en las 473 y los datos fiscales.
        var entrada = new EntradaImpuestoSociedades(ajustes, g.Bases, g.Bonif, g.Deduc, g.Credito, g.Cuota != g.Calculada ? g.Cuota : null,
            aeat.Count == 0 ? null : aeat, g.BasesAeat, g.Presentado, valores.Count == 0 ? null : valores, g.Renta != g.RentaCalculada ? g.Renta : null);
        return new ImpuestoSociedadesGuardado(g.Resultado, g.Base, g.Cuota, g.Ret, g.Saldo, g.Motivo, g.IdAsiento, g.Operador, g.Fecha, entrada, g.SaldoRet);
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

    private sealed record ApunteLeido(int IdAsiento, int Tipo, string Cuenta, decimal Debe, decimal Haber, int? IdCentro, bool DelLote, DateTime Fecha = default, string Texto = "");

    /// <summary>Todo lo que el cálculo lee de la contabilidad del ejercicio.</summary>
    public async Task<DatosImpuestoSociedades> DatosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var parametros = await ParametrosAsync(ctx);
        var (bonificados, generales, nombresCentros, hayBonificada) = await CentrosAsync(ctx.IdSociedad);
        var lote = Lote(ctx.Ejercicio);
        var fin = CalculoImpuestoSociedades.FechaAsiento(ctx.Ejercicio);
        var (apuntes, nombres, nifs) = await bd.EjecutarAsync(async s =>
        {
            var a = await s.ConsultarAsync(
                "SELECT a.IdAsiento, a.TipoAsiento, a.FechaContable, a.Descripcion AS TextoAsiento, a.MotorPropietario, a.CodigoProceso, a.ClaveEjecucion, p.CuentaContable, " +
                "p.ImporteDebe, p.ImporteHaber, p.IdCentroCoste, p.Descripcion AS TextoApunte " +
                "FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.FechaContable <= ?",
                f => new ApunteLeido(f.Entero("IdAsiento"), Convert.ToInt32(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture), (f.Texto("CuentaContable") ?? "").Trim(),
                    f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m, f.EnteroNulo("IdCentroCoste"),
                    f.Texto("MotorPropietario") == lote.MotorPropietario && f.Texto("CodigoProceso") == lote.CodigoProceso && f.Texto("ClaveEjecucion") == lote.ClaveEjecucion,
                    f.Fecha("FechaContable"), (f.Texto("TextoApunte") is { Length: > 0 } ta ? ta : f.Texto("TextoAsiento") ?? "").Trim()),
                ctx.IdSociedadEjercicio, fin.AddDays(1).AddTicks(-1));
            var n = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (codigo, nombre) in await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                         f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedadEjercicio))
                if (codigo.Length > 0) n.TryAdd(codigo, nombre);
            // NIF del tercero de cada 43 y 44 del ejercicio (clientes y proveedores), para casar los pagadores con Hacienda.
            var nifs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var sql in (string[])[
                         "SELECT cc.Codigo, t.NIF FROM ((CuentasContables AS cc INNER JOIN CuentasClientesSociedad AS cs ON cc.IdIdentidadContable = cs.IdIdentidadContable) " +
                         "INNER JOIN Clientes AS cl ON cs.IdCliente = cl.IdCliente) INNER JOIN Terceros AS t ON cl.IdTercero = t.IdTercero WHERE cc.IdSociedadEjercicio = ?",
                         "SELECT cc.Codigo, t.NIF FROM ((CuentasContables AS cc INNER JOIN CuentasProveedoresSociedad AS cs ON cc.IdIdentidadContable = cs.IdIdentidadContable) " +
                         "INNER JOIN Proveedores AS pr ON cs.IdProveedor = pr.IdProveedor) INNER JOIN Terceros AS t ON pr.IdTercero = t.IdTercero WHERE cc.IdSociedadEjercicio = ?"])
                foreach (var (codigo, nif) in await s.ConsultarAsync(sql, f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("NIF") ?? "").Trim()), ctx.IdSociedadEjercicio))
                    if (codigo.Length > 0 && nif.Length > 0) nifs.TryAdd(codigo, nif);
            return (a, n, nifs);
        }, c);

        // Sin este proceso, sin la regularización (2) ni el cierre (3).
        var propios = apuntes.Where(x => !x.DelLote).ToList();
        var ordinarios = propios.Where(x => x.Tipo is not (TipoRegularizacion or TipoCierre)).ToList();
        var ajenos = ordinarios.Where(x => CalculoImpuestoSociedades.EsImpuestoBeneficios(x.Cuenta)).Select(x => x.IdAsiento).ToHashSet();
        var enResultado = ordinarios.Where(x => CalculoImpuestoSociedades.EntraEnResultado(x.Cuenta)).ToList();
        var resultado = enResultado.Sum(x => x.Haber - x.Debe);
        string Nombre(string cuenta) => nombres.GetValueOrDefault(cuenta, "");
        var viviendas = RentaViviendas(enResultado, bonificados, generales, nombresCentros, Nombre);

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
        var apuntesRet = paraRet.Where(x => CalculoImpuestoSociedades.EsRetencion(x.Cuenta) && x.Debe - x.Haber != 0m).Select(x =>
        {
            // También en los abonos (una factura rectificativa del inquilino resta de su fila, no forma una aparte).
            var pagador = porAsiento[x.IdAsiento].Select(o => o.Cuenta).FirstOrDefault(o => o.StartsWith("43", StringComparison.Ordinal) || o.StartsWith("44", StringComparison.Ordinal));
            return new ApunteRetencion(x.IdAsiento, x.Fecha, x.Cuenta, x.Texto, x.Debe - x.Haber,
                pagador is null ? null : (Nombre(pagador) is { Length: > 0 } np ? np : pagador), pagador is null ? null : nifs.GetValueOrDefault(pagador));
        }).ToList();

        var posibles = ordinarios.Where(x => parametros.PrefijosAjustes.Any(pr => x.Cuenta.StartsWith(pr, StringComparison.Ordinal)) && x.Cuenta.Length > 3)
            .GroupBy(x => x.Cuenta).Select(g => new SaldoCuentaImpuesto(g.Key, Nombre(g.Key), g.Sum(x => x.Debe - x.Haber), g.Count()))
            .Where(r => r.Saldo != 0m).OrderBy(r => r.Cuenta, StringComparer.Ordinal).ToList();

        var credito = paraRet.Where(x => x.Cuenta == parametros.Cuentas.CreditoPerdidas).Sum(x => x.Debe - x.Haber);
        var textosAjenos = ajenos.Count == 0 ? [] : await TextosAsientosAsync(ajenos);
        return new DatosImpuestoSociedades(ctx.Ejercicio, resultado, viviendas.Renta, hayBonificada, retenciones, posibles, Math.Max(0m, credito), parametros, textosAjenos,
            apuntesRet, viviendas);
    }

    /// <summary>
    /// Renta de viviendas (propietario 2026-11-17 12:44, art. 49.2 LIS): ingresos de los centros bonificados − sus gastos directos − los gastos
    /// indirectos (sin centro, sin actividad o de la actividad general) en proporción a los ingresos ordinarios (70 a 75) de viviendas sobre los totales.
    /// </summary>
    private static RentaViviendasDetalle RentaViviendas(IReadOnlyList<ApunteLeido> enResultado, HashSet<int> bonificados, HashSet<int> generales,
        IReadOnlyDictionary<int, string> centros, Func<string, string> nombre)
    {
        static bool Ordinario(string c) => c.Length >= 2 && c[0] == '7' && c[1] is >= '0' and <= '5';
        string? Clase(ApunteLeido x)
        {
            var viv = x.IdCentro is int ce && bonificados.Contains(ce);
            var general = x.IdCentro is not int cg || generales.Contains(cg);
            if (x.Cuenta[0] == '7') return viv ? PartidaRentaViviendas.IngresoViviendas : Ordinario(x.Cuenta) ? PartidaRentaViviendas.OtroIngresoOrdinario : null;
            return viv ? PartidaRentaViviendas.GastoDirecto : general ? PartidaRentaViviendas.GastoIndirecto : null;
        }
        var partidas = enResultado.Select(x => (x, Clase: Clase(x))).Where(t => t.Clase is not null)
            .GroupBy(t => (t.Clase!, t.x.IdCentro, t.x.Cuenta))
            .Select(g => new PartidaRentaViviendas(g.Key.Item1, g.Key.IdCentro is int ce ? centros.GetValueOrDefault(ce, ce.ToString(CultureInfo.InvariantCulture)) : "(sin centro)",
                g.Key.Cuenta, nombre(g.Key.Cuenta), g.Sum(t => g.Key.Item1 is PartidaRentaViviendas.IngresoViviendas or PartidaRentaViviendas.OtroIngresoOrdinario
                    ? t.x.Haber - t.x.Debe : t.x.Debe - t.x.Haber), g.Count()))
            .Where(p => p.Importe != 0m).OrderBy(p => p.Clase, StringComparer.Ordinal).ThenBy(p => p.Centro, StringComparer.CurrentCulture).ThenBy(p => p.Cuenta, StringComparer.Ordinal).ToList();
        decimal Suma(string clase, bool soloOrdinarios = false) => partidas.Where(p => p.Clase == clase && (!soloOrdinarios || Ordinario(p.Cuenta))).Sum(p => p.Importe);
        var ordViv = Suma(PartidaRentaViviendas.IngresoViviendas, true);
        return new RentaViviendasDetalle(Suma(PartidaRentaViviendas.IngresoViviendas), Suma(PartidaRentaViviendas.GastoDirecto), Suma(PartidaRentaViviendas.GastoIndirecto),
            ordViv, ordViv + Suma(PartidaRentaViviendas.OtroIngresoOrdinario), partidas);
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

    /// <summary>
    /// Centros de la sociedad: los de actividad (propia o heredada) con la bonificación por alquiler de viviendas, los generales (sin actividad o
    /// de la actividad general, serie de IVA 4720 o vacía), sus nombres y si hay alguna actividad marcada.
    /// </summary>
    private Task<(HashSet<int> Bonificados, HashSet<int> Generales, Dictionary<int, string> Nombres, bool HayActividad)> CentrosAsync(string idSociedad) => bd.EjecutarAsync(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT c.IdCentroCoste, c.IdCentroCostePadre, c.Codigo AS CodigoCentro, c.Nombre AS NombreCentro, a.Codigo, a.Nombre, a.Activa, a.BonificacionIS, a.SerieCuentaIva " +
            "FROM CentrosCoste AS c LEFT JOIN Actividades AS a ON c.IdActividad = a.IdActividad WHERE c.IdSociedad = ?",
            f => (Id: f.Entero("IdCentroCoste"), Padre: f.EnteroNulo("IdCentroCostePadre"),
                Nombre: $"{(f.Texto("CodigoCentro") ?? "").Trim()} {(f.Texto("NombreCentro") ?? "").Trim()}".Trim(),
                Propia: f.Texto("Codigo") is { } codigo && !f.EsNulo("Activa") && f.Logico("Activa")
                    ? new ActividadIva(codigo.Trim(), (f.Texto("Nombre") ?? codigo).Trim(), (byte)(!f.EsNulo("BonificacionIS") && f.Logico("BonificacionIS") ? 1 : 0),
                        (f.Texto("SerieCuentaIva") ?? "").Trim())
                    : null),
            idSociedad);
        var hay = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Actividades WHERE IdSociedad = ? AND BonificacionIS = True AND Activa = True", idSociedad),
            CultureInfo.InvariantCulture) > 0;
        // El campo Deducible del registro se usa aquí solo como marca de bonificación (1).
        var resueltas = LectorActividadesIva.ResolverHerencia(filas.Select(f => (f.Id, f.Padre, f.Propia)));
        var bonificados = resueltas.Where(kv => kv.Value.Deducible == 1).Select(kv => kv.Key).ToHashSet();
        var generales = filas.Select(f => f.Id).Where(id => !resueltas.TryGetValue(id, out var a) || string.IsNullOrEmpty(a.Serie) || a.Serie == "4720").ToHashSet();
        return (bonificados, generales, filas.ToDictionary(f => f.Id, f => f.Nombre), hay);
    });

    private const int TipoRegularizacion = 2;
    private const int TipoCierre = 3;

    private static decimal Dec(IFilaDatos f, string campo) => f.DecimalNulo(campo) ?? 0m;
    private static decimal Porcentaje(decimal tantoPorUno) => Math.Round(tantoPorUno * 100m, 4, MidpointRounding.AwayFromZero);
    private static string Recortar(string s, int max) => s.Length <= max ? s : s[..max];
}
