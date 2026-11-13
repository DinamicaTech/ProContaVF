using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Nominas.Importacion;
using ProConta.Nominas.Lectura;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Nominas.Contabilizacion;

/// <summary>Asiento propuesto para una nómina, o por qué no se puede proponer.</summary>
public sealed record NominaPropuesta(int IdNomina, int Ordinal, string Trabajador, string? CuentaTrabajador, AsientoPropuesto? Asiento, IReadOnlyList<Error> Errores);

/// <summary>Lo que generaría contabilizar una importación: un asiento por nómina, con la fecha que toca y las cuentas que se crearán.</summary>
public sealed record PropuestaNominas(
    int IdImportacion,
    ContextoSociedadEjercicio? Contexto,
    PlantillaAsiento? Plantilla,
    DateTime? FechaNomina,
    DateTime? FechaAsiento,
    IReadOnlyList<NominaPropuesta> Nominas,
    IReadOnlyList<Error> Errores,
    IReadOnlyList<string> Avisos,
    IReadOnlyList<(string Codigo, string Nombre)> CuentasNuevas)
{
    public bool Contabilizable => Errores.Count == 0 && Nominas.Count > 0 && Nominas.All(n => n.Asiento is not null && n.Errores.Count == 0);

    /// <summary>Todos los errores: los de la importación y los de cada nómina.</summary>
    public IReadOnlyList<Error> TodosLosErrores => [.. Errores, .. Nominas.SelectMany(n => n.Errores)];
}

/// <summary>Resultado de contabilizar una importación: los asientos por nómina.</summary>
public sealed record ResultadoContabilizacionNominas(int IdImportacion, IReadOnlyList<int> Asientos, DateTime FechaAsiento, IReadOnlyList<string> Avisos);

/// <summary>Resultado de deshacer una contabilización: borrados (trimestre abierto) o anulados con contra-asientos (trimestre cerrado o asientos migrados).</summary>
public sealed record ResultadoDeshacerNominas(int IdImportacion, bool Anulados, IReadOnlyList<int> AsientosOriginales, IReadOnlyList<int> AsientosAnulacion, DateTime? FechaAnulacion)
{
    public string Explicacion => Anulados
        ? $"Los {AsientosOriginales.Count} asientos no se han tocado: se han anulado con {AsientosAnulacion.Count} contra-asientos del {FechaAnulacion:dd/MM/yyyy}. Las nóminas quedan pendientes."
        : $"Se han borrado los {AsientosOriginales.Count} asientos. Las nóminas quedan pendientes.";
}

/// <summary>Resultado de reemplazar una importación por otra versión del fichero.</summary>
public sealed record ResultadoReemplazoNominas(ResultadoImportacionNominas Importacion, ResultadoDeshacerNominas? Deshecha, ResultadoContabilizacionNominas? Contabilizada,
    IReadOnlyList<Error> ErroresContabilizacion);

/// <summary>
/// Contabilización de las nóminas importadas (nominas 2026-11-13): un asiento por nómina con la plantilla de sistema «Nómina» de la sociedad
/// (clave «nominas»), todos en un lote automático propio por importación (motor «nominas», proceso NOMINA, clave «importación.n»), en una
/// sola transacción. Fecha: la de la nómina, o el día 1 del trimestre activo si es de un trimestre cerrado. Deshacer borra los asientos
/// si su trimestre está abierto; si está cerrado (o son asientos migrados de ContaSol), no los toca y genera su anulación (proceso
/// NOMINA_ANULACION) con la misma regla de fecha. En los dos casos las nóminas vuelven a estar pendientes.
/// </summary>
public sealed class ServicioContabilizacionNominas(IBaseDatos bd, IMotorAsientos motor, ServicioPlantillasAsiento plantillas, ICatalogoCuentas cuentas,
    ServicioImportacionNominas importacion, IReloj? reloj = null)
{
    public ServicioContabilizacionNominas(IBaseDatos bd)
        : this(bd, new MotorAsientosAccess(bd), new ServicioPlantillasAsiento(bd), new CatalogoCuentasAccess(bd), new ServicioImportacionNominas(bd)) { }

    public const string MotorPropietario = "nominas";
    public const string CodigoProceso = "NOMINA";
    public const string CodigoAnulacion = "NOMINA_ANULACION";

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>Lote de la contabilización número <paramref name="n"/> de una importación.</summary>
    public static IdentidadLote Lote(int idImportacion, int n) => new(MotorPropietario, CodigoProceso, Clave(idImportacion, n));

    /// <summary>Lote de la anulación de la contabilización número <paramref name="n"/> de una importación.</summary>
    public static IdentidadLote LoteAnulacion(int idImportacion, int n) => new(MotorPropietario, CodigoAnulacion, Clave(idImportacion, n));

    private static string Clave(int id, int n) => $"{id.ToString(CultureInfo.InvariantCulture)}.{n.ToString(CultureInfo.InvariantCulture)}";

    // ---------- proponer ----------

    /// <summary>Propone los asientos de una importación pendiente, sin grabar nada.</summary>
    public async Task<PropuestaNominas> ProponerAsync(int idImportacion)
    {
        var registro = $"Importación {idImportacion}";
        var imp = await LeerImportacionAsync(idImportacion);
        if (imp is null)
            return Vacia(idImportacion, new Error("Nominas.ImportacionInexistente", "La importación de nóminas no existe.", registro, null, "Actualiza la pantalla (F5)."));
        var ctx = imp.Contexto;
        var errores = new List<Error>();
        var avisos = new List<string>();
        if (imp.Estado != ServicioImportacionNominas.EstadoPendiente)
            errores.Add(imp.Estado switch
            {
                ServicioImportacionNominas.EstadoIncidencias => new Error("Nominas.Incidencias", "La importación tiene trabajadores sin resolver.", registro,
                    "No se contabiliza una nómina sin su empleado y su cuenta 465.", "Resuelve los trabajadores (asígnalos a un empleado o dalos de alta) y vuelve a contabilizar."),
                ServicioImportacionNominas.EstadoContabilizada => new Error("Nominas.YaContabilizada", "Estas nóminas ya están contabilizadas.", registro, null,
                    "Si hay que rehacerlas, deshaz antes la contabilización."),
                _ => new Error("Nominas.ImportacionCerrada", $"La importación está {imp.Estado.ToLowerInvariant()}: ya no se contabiliza.", registro, null,
                    "Contabiliza la versión vigente del periodo."),
            });
        if (imp.EjercicioCerrado)
            errores.Add(new Error("Nominas.EjercicioCerrado", $"El ejercicio {ctx.Ejercicio} está cerrado.", registro, null, "Reabre el ejercicio para contabilizar estas nóminas."));

        var plantilla = await plantillas.DeProcesoAsync(PlantillaNomina.ClaveProceso, ctx.IdSociedad);
        if (plantilla is null || plantilla.Lineas.Count == 0)
            errores.Add(new Error("Nominas.SinPlantilla", "La plantilla «Nómina» de la sociedad no tiene líneas.", registro,
                "Sus líneas se cargan con la migración del ERP desde la plantilla de ProConta antiguo (PlantillasApuntes).",
                "Pasa la migración del ERP a esta BD o pide que se den de alta las líneas de la plantilla «Nómina» de la sociedad."));

        var fechaAsiento = TrimestreContable.FechaAsiento(imp.FechaNomina, ctx.Ejercicio, TrimestreContable.Activo(imp.TrimestreActivo, ctx.Ejercicio, _reloj.Ahora));
        if (fechaAsiento != imp.FechaNomina.Date)
            avisos.Add($"Las nóminas son del {imp.FechaNomina:dd/MM/yyyy}, de un trimestre cerrado: los asientos van al {fechaAsiento:dd/MM/yyyy}, primer día del trimestre activo.");
        if (errores.Count > 0 || plantilla is null) return new(idImportacion, ctx, plantilla, imp.FechaNomina, fechaAsiento, [], errores, avisos, []);

        var entorno = await plantillas.EntornoAsync(ctx, plantilla);
        var conocidas = new Dictionary<string, string>(entorno.Cuentas ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var nuevas = new List<(string Codigo, string Nombre)>();
        var nominas = new List<NominaPropuesta>();
        foreach (var n in await LeerNominasAsync(idImportacion, ctx.IdSociedad))
        {
            var donde = $"Nómina de {n.Nombre}";
            var cuenta = n.IdIdentidad is int idIdentidad
                ? await cuentas.CuentaDeIdentidadAsync(ctx, idIdentidad) ?? await cuentas.UltimaCuentaDeIdentidadAsync(idIdentidad)
                : null;
            if (cuenta is null)
            {
                nominas.Add(new(n.IdNomina, n.Ordinal, n.Nombre, null, null, [new Error("Nominas.SinCuenta465", $"{n.Nombre} no tiene cuenta 465 en la sociedad.", donde,
                    "La cuenta de remuneraciones pendientes del empleado es la contrapartida del neto.", "Asígnale la cuenta en la ficha del tercero (pestaña Roles) y vuelve a contabilizar.")]));
                continue;
            }
            if (!conocidas.ContainsKey(cuenta.Codigo))
            {
                // La 465 es de otro ejercicio: se da de alta en este al contabilizar.
                conocidas[cuenta.Codigo] = cuenta.Nombre;
                nuevas.Add((cuenta.Codigo, cuenta.Nombre));
            }
            var contable = new NominaContable(n.IdNomina.ToString(CultureInfo.InvariantCulture), n.Nombre, cuenta.Codigo, n.Fecha ?? imp.FechaNomina, n.Importes,
                n.Codigo, n.Nif, n.TipoPaga);
            var propuesta = MotorPlantillas.Proponer(plantilla, new OrigenProceso(PlantillaNomina.ClaveProceso, [PlantillaNomina.Registro(contable)]), null,
                entorno with { Cuentas = conocidas }, fechaAsiento);
            if (!propuesta.EsCorrecto) { nominas.Add(new(n.IdNomina, n.Ordinal, n.Nombre, cuenta.Codigo, null, propuesta.Errores)); continue; }
            if (propuesta.Valor.Asientos.Count != 1)
            {
                nominas.Add(new(n.IdNomina, n.Ordinal, n.Nombre, cuenta.Codigo, null, [new Error("Nominas.PlantillaNoValida",
                    $"La plantilla «{plantilla.Nombre}» genera {propuesta.Valor.Asientos.Count} asientos para una nómina.", donde,
                    "Cada nómina se contabiliza en un único asiento.", "Corrige la plantilla para que tenga un solo número de asiento.")]));
                continue;
            }
            foreach (var c in propuesta.Valor.CuentasNuevas.Where(c => nuevas.All(x => x.Codigo != c.Codigo))) nuevas.Add(c);
            nominas.Add(new(n.IdNomina, n.Ordinal, n.Nombre, cuenta.Codigo, propuesta.Valor.Asientos[0], []));
        }
        if (nominas.Count == 0)
            errores.Add(new Error("Nominas.SinNominas", "La importación no tiene nóminas.", registro, null, "Vuelve a importar el fichero."));
        return new(idImportacion, ctx, plantilla, imp.FechaNomina, fechaAsiento, nominas, errores, avisos, nuevas);
    }

    private static PropuestaNominas Vacia(int id, Error e) => new(id, null, null, null, null, [], [e], [], []);

    // ---------- contabilizar ----------

    /// <summary>
    /// Contabiliza una importación pendiente: crea las cuentas que falten (la 465 del ejercicio y las «?» de la plantilla) y graba un
    /// asiento por nómina en un lote propio, enlaza cada nómina con su asiento y deja la importación contabilizada, todo o nada.
    /// </summary>
    public async Task<Resultado<ResultadoContabilizacionNominas>> ContabilizarAsync(int idImportacion, Actor actor)
    {
        var p = await ProponerAsync(idImportacion);
        if (!p.Contabilizable) return Resultado<ResultadoContabilizacionNominas>.Fallo(p.TodosLosErrores);
        var ctx = p.Contexto!;
        foreach (var (codigo, nombre) in p.CuentasNuevas)
        {
            if ((await cuentas.ResolverAsync(ctx, codigo)).EsCorrecto) continue;
            var deIdentidad = p.Nominas.FirstOrDefault(n => n.CuentaTrabajador == codigo) is not null
                ? await IdentidadDeCodigoAsync(ctx.IdSociedad, codigo) : null;
            var alta = deIdentidad is int id
                ? await cuentas.AltaEnEjercicioAsync(ctx, id, codigo, nombre)
                : await cuentas.AltaAsync(ctx, new AltaCuenta(codigo, nombre));
            if (!alta.EsCorrecto) return Resultado<ResultadoContabilizacionNominas>.Fallo(alta.Errores);
        }

        var descripcion = await DescripcionImportacionAsync(idImportacion);
        var asientos = p.Nominas.Select(n => AsientoDe(ctx, n.Asiento!, p.Plantilla!, $"Nóminas {descripcion}, importación {idImportacion}.")).ToList();
        var numero = await SiguienteNumeroAsync(idImportacion);
        var idsNominas = p.Nominas.Select(n => n.IdNomina).ToList();
        var r = await motor.ReemplazarLoteAsync(ctx, Lote(idImportacion, numero), asientos, actor, $"Contabilizar las nóminas {descripcion}", new TrabajoLote(
            Despues: async (s, ids) =>
            {
                for (var i = 0; i < ids.Count; i++)
                    await s.EjecutarAsync("UPDATE Nominas SET IdAsiento = ? WHERE IdNomina = ?", ids[i], idsNominas[i]);
                var n = await s.EjecutarAsync(
                    "UPDATE ImportacionesNomina SET Estado = ?, FechaHoraEstado = ?, OperadorEstado = ?, MotivoEstado = NULL WHERE IdImportacionNomina = ? AND Estado = ?",
                    ServicioImportacionNominas.EstadoContabilizada, Ahora(), Recortar(actor.ToString(), 100), idImportacion, ServicioImportacionNominas.EstadoPendiente);
                return n == 1 ? Resultado.Correcto() : Resultado.Fallo(new Error("Nominas.HaCambiado", "La importación ha cambiado mientras se contabilizaba.",
                    $"Importación {idImportacion}", "Otra persona la ha contabilizado, reemplazado o cambiado.", "Actualiza la pantalla (F5) y vuelve a intentarlo."));
            }));
        return r.EsCorrecto
            ? Resultado<ResultadoContabilizacionNominas>.Correcto(new(idImportacion, r.Valor, p.FechaAsiento!.Value, p.Avisos))
            : Resultado<ResultadoContabilizacionNominas>.Fallo(r.Errores);
    }

    private static Asiento AsientoDe(ContextoSociedadEjercicio ctx, AsientoPropuesto propuesta, PlantillaAsiento plantilla, string notas)
    {
        var a = new Asiento(ctx, propuesta.Fecha, propuesta.Descripcion) { Notas = notas, IdPlantilla = plantilla.IdPlantilla > 0 ? plantilla.IdPlantilla : null };
        foreach (var x in propuesta.Apuntes)
        {
            var apunte = x.AlDebe ? Apunte.AlDebe(x.Cuenta, x.Importe, x.Concepto) : Apunte.AlHaber(x.Cuenta, x.Importe, x.Concepto);
            apunte.NumeroDocumento = x.Documento;
            apunte.IdCentroCoste = x.IdCentroCoste;
            a.Apuntes.Add(apunte);
        }
        return a;
    }

    // ---------- deshacer ----------

    /// <summary>
    /// Deshace la contabilización de una importación (nominas 2026-11-13 10:11): si sus asientos son de este proceso y su trimestre está
    /// abierto, se borran; si no (trimestre cerrado, o asientos migrados de ContaSol), no se tocan y se generan sus contra-asientos con
    /// fecha del día 1 del trimestre activo (o la suya si su trimestre está abierto). Las nóminas quedan pendientes, listas para volver a
    /// contabilizar. El motivo es obligatorio y queda en el registro de los asientos.
    /// </summary>
    public async Task<Resultado<ResultadoDeshacerNominas>> DeshacerAsync(int idImportacion, Actor actor, string motivo)
    {
        var registro = $"Importación {idImportacion}";
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<ResultadoDeshacerNominas>.Fallo(new Error("Nominas.MotivoObligatorio", "Falta el motivo para deshacer la contabilización.", registro,
                "Borrar o anular asientos queda registrado con su motivo.", "Escribe por qué deshaces la contabilización."));
        var imp = await LeerImportacionAsync(idImportacion);
        if (imp is null)
            return Resultado<ResultadoDeshacerNominas>.Fallo(new Error("Nominas.ImportacionInexistente", "La importación de nóminas no existe.", registro, null, "Actualiza la pantalla (F5)."));
        if (imp.Estado != ServicioImportacionNominas.EstadoContabilizada)
            return Resultado<ResultadoDeshacerNominas>.Fallo(new Error("Nominas.NoContabilizada", "Estas nóminas no están contabilizadas.", registro, null, "No hay nada que deshacer."));

        var ids = (await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT DISTINCT IdAsiento FROM Nominas WHERE IdImportacionNomina = ? AND IdAsiento IS NOT NULL",
            f => f.Entero("IdAsiento"), idImportacion))).Order().ToList();
        var originales = new List<Asiento>();
        foreach (var id in ids)
            if (await motor.ObtenerAsync(id) is { } a) originales.Add(a);
        var ctx = imp.Contexto;
        var activo = TrimestreContable.Activo(imp.TrimestreActivo, ctx.Ejercicio, _reloj.Ahora);
        var inicioActivo = TrimestreContable.Inicio(ctx.Ejercicio, activo);
        var lotes = originales.Select(a => a.Lote).Distinct().ToList();
        var propios = originales.Count > 0 && lotes.Count == 1 && lotes[0] is { MotorPropietario: MotorPropietario, CodigoProceso: CodigoProceso } lote
            && lote.ClaveEjecucion.StartsWith($"{idImportacion.ToString(CultureInfo.InvariantCulture)}.", StringComparison.Ordinal);
        var cerrado = originales.Any(a => a.FechaContable.Date < inicioActivo);
        var motivoLimpio = motivo.Trim();

        async Task<Resultado> Pendiente(ISesionDatos s)
        {
            await s.EjecutarAsync("UPDATE Nominas SET IdAsiento = NULL WHERE IdImportacionNomina = ?", idImportacion);
            var n = await s.EjecutarAsync(
                "UPDATE ImportacionesNomina SET Estado = ?, FechaHoraEstado = ?, OperadorEstado = ?, MotivoEstado = ? WHERE IdImportacionNomina = ? AND Estado = ?",
                ServicioImportacionNominas.EstadoPendiente, Ahora(), Recortar(actor.ToString(), 100), motivoLimpio, idImportacion, ServicioImportacionNominas.EstadoContabilizada);
            return n == 1 ? Resultado.Correcto() : Resultado.Fallo(new Error("Nominas.HaCambiado", "La importación ha cambiado mientras se deshacía.", registro, null,
                "Actualiza la pantalla (F5) y vuelve a intentarlo."));
        }

        if (propios && !cerrado)
        {
            var borrado = await motor.EliminarLoteAsync(ctx, lotes[0]!, actor, motivoLimpio, new TrabajoLote(Despues: async (s, _) => await Pendiente(s)));
            return borrado.EsCorrecto
                ? Resultado<ResultadoDeshacerNominas>.Correcto(new(idImportacion, false, ids, [], null))
                : Resultado<ResultadoDeshacerNominas>.Fallo(borrado.Errores);
        }

        // Anulación: contra-asientos en un lote propio, con la regla de fecha de las nóminas.
        var numero = propios && int.TryParse(lotes[0]!.ClaveEjecucion.Split('.').Last(), NumberStyles.None, CultureInfo.InvariantCulture, out var n0)
            ? n0 : await SiguienteNumeroAsync(idImportacion);
        var anulaciones = originales.Select(a => PlantillaNomina.Anulacion(a, TrimestreContable.FechaAsiento(a.FechaContable, a.Contexto.Ejercicio, activo),
            $"Anula el asiento {a.IdAsiento} del {a.FechaContable:dd/MM/yyyy} (nóminas, importación {idImportacion}). Motivo: {motivoLimpio}")).ToList();
        if (anulaciones.Count == 0)
            return Resultado<ResultadoDeshacerNominas>.Fallo(new Error("Nominas.SinAsientos", "No encuentro los asientos de estas nóminas.", registro,
                "Se han borrado fuera de ProContaVF.", "Revisa el Mayor; si ya no existen, vuelve a importar el fichero."));
        var r = await motor.ReemplazarLoteAsync(ctx, LoteAnulacion(idImportacion, numero), anulaciones, actor, $"Anular las nóminas de la importación {idImportacion}: {motivoLimpio}",
            new TrabajoLote(Despues: async (s, _) => await Pendiente(s)));
        return r.EsCorrecto
            ? Resultado<ResultadoDeshacerNominas>.Correcto(new(idImportacion, true, ids, r.Valor, anulaciones[0].FechaContable))
            : Resultado<ResultadoDeshacerNominas>.Fallo(r.Errores);
    }

    // ---------- reemplazar ----------

    /// <summary>
    /// Reemplaza la importación vigente de un periodo por otra versión del fichero (el caso de mayo, nominas 2026-11-13): deshace la
    /// contabilización de la anterior si la tenía (borrado o anulación), importa la nueva marcando la anterior como reemplazada y, si no
    /// le queda nada por resolver, la contabiliza. El motivo es obligatorio.
    /// </summary>
    public async Task<Resultado<ResultadoReemplazoNominas>> ReemplazarAsync(ContextoSociedadEjercicio ctx, int idAnterior, byte[] contenido, string nombreFichero,
        OrigenNominas origen, Actor actor, string motivo, IReadOnlyList<AliasNuevo>? alias = null, int? idAdjuntoRecibido = null)
    {
        var registro = $"Importación {idAnterior}";
        if (string.IsNullOrWhiteSpace(motivo))
            return Resultado<ResultadoReemplazoNominas>.Fallo(new Error("Nominas.MotivoObligatorio", "Para reemplazar unas nóminas hay que explicar el motivo.", registro, null,
                "Escribe el motivo (p. ej. «la gestoría corrige la baja de …»)."));
        var analisis = await importacion.AnalizarAsync(ctx, contenido, alias);
        if (analisis.Veredicto != VeredictoNominas.OtraVersion || analisis.IdImportacionExistente != idAnterior)
            return Resultado<ResultadoReemplazoNominas>.Fallo(new Error("Nominas.NoEsOtraVersion", "Este fichero no es otra versión de esas nóminas.", registro,
                analisis.Explicacion, "Elige el fichero corregido del mismo periodo y tipo de paga."));
        ResultadoDeshacerNominas? deshecha = null;
        if (await EstadoAsync(idAnterior) == ServicioImportacionNominas.EstadoContabilizada)
        {
            var d = await DeshacerAsync(idAnterior, actor, motivo);
            if (!d.EsCorrecto) return Resultado<ResultadoReemplazoNominas>.Fallo(d.Errores);
            deshecha = d.Valor;
        }
        var imp = await importacion.ImportarAsync(ctx, contenido, nombreFichero, origen, actor, alias, idAnterior, idAdjuntoRecibido, motivo);
        if (!imp.EsCorrecto) return Resultado<ResultadoReemplazoNominas>.Fallo(imp.Errores);
        if (imp.Valor.Estado != ServicioImportacionNominas.EstadoPendiente || imp.Valor.IdImportacion is not int nueva)
            return Resultado<ResultadoReemplazoNominas>.Correcto(new(imp.Valor, deshecha, null, []));
        var c = await ContabilizarAsync(nueva, actor);
        return Resultado<ResultadoReemplazoNominas>.Correcto(new(imp.Valor, deshecha, c.EsCorrecto ? c.Valor : null, c.EsCorrecto ? [] : c.Errores));
    }

    // ---------- plantilla ----------

    /// <summary>
    /// Carga <paramref name="lineas"/> en la copia de la sociedad de la plantilla de sistema «Nómina» (la crea si no la tiene; si la tiene,
    /// sustituye sus líneas). La usa la migración del ERP con las líneas convertidas de PlantillasApuntes. Devuelve el id de la plantilla.
    /// </summary>
    public Task<int> CargarPlantillaAsync(string idSociedad, IReadOnlyList<LineaPlantilla> lineas, bool agruparPorCuenta = false) => bd.EnTransaccionAsync(async s =>
    {
        var id = await s.ConsultarPrimeroAsync("SELECT IdPlantillaAsiento FROM PlantillasAsiento WHERE Tipo = 'SISTEMA' AND ClaveProceso = ? AND IdSociedad = ?",
            f => (int?)f.Entero("IdPlantillaAsiento"), PlantillaNomina.ClaveProceso, idSociedad);
        if (id is int existente)
        {
            await s.EjecutarAsync("UPDATE PlantillasAsiento SET Descripcion = ?, AgruparPorCuenta = ?, Activa = True WHERE IdPlantillaAsiento = ?",
                PlantillaNomina.Descripcion, agruparPorCuenta, existente);
            await s.EjecutarAsync("DELETE FROM PlantillasAsientoLineas WHERE IdPlantillaAsiento = ?", existente);
        }
        else
        {
            await s.EjecutarAsync(
                "INSERT INTO PlantillasAsiento (Nombre, Descripcion, Tipo, Clase, IdSociedad, Origen, ClaveProceso, ReglaFecha, AgruparPorCuenta, Activa, Orden) " +
                "VALUES (?, ?, 'SISTEMA', 'PROCESO', ?, 'PROCESO', ?, 'FECHA_ORIGEN', ?, True, 100)",
                PlantillaNomina.Nombre, PlantillaNomina.Descripcion, idSociedad, PlantillaNomina.ClaveProceso, agruparPorCuenta);
            id = await s.UltimoIdAsync();
        }
        foreach (var l in lineas)
            await s.EjecutarAsync(
                "INSERT INTO PlantillasAsientoLineas (IdPlantillaAsiento, NumeroAsiento, Orden, PorCadaApunte, FormulaCuenta, Lado, FormulaImporte, FormulaConcepto, FormulaCentro, Condicion) " +
                "VALUES (?, ?, ?, False, ?, ?, ?, ?, ?, ?)", id, l.NumeroAsiento, l.Orden, l.Cuenta, l.Lado, l.Importe, l.Concepto, l.Centro, l.Condicion);
        return id!.Value;
    });

    // ---------- lecturas ----------

    private sealed record ImportacionLeida(ContextoSociedadEjercicio Contexto, string Estado, DateTime FechaNomina, int? TrimestreActivo, bool EjercicioCerrado);

    private sealed record NominaLeida(int IdNomina, int Ordinal, string Nombre, string? Codigo, string? Nif, string? TipoPaga, DateTime? Fecha, int? IdIdentidad,
        IReadOnlyDictionary<string, decimal> Importes);

    private Task<ImportacionLeida?> LeerImportacionAsync(int id) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT i.IdSociedadEjercicio, i.Estado, i.FechaNomina, i.PeriodoHasta, se.IdSociedad, se.Ejercicio, se.TrimestreActivo, se.EjercicioCerrado " +
        "FROM ImportacionesNomina AS i INNER JOIN SociedadesEjercicios AS se ON i.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE i.IdImportacionNomina = ?",
        f => new ImportacionLeida(
            new ContextoSociedadEjercicio(f.Texto("IdSociedad") ?? "", f.Entero("IdSociedadEjercicio"), int.Parse((f.Texto("Ejercicio") ?? "0").Trim(), CultureInfo.InvariantCulture)),
            f.Texto("Estado") ?? "", (f.EsNulo("FechaNomina") ? f.Fecha("PeriodoHasta") : f.Fecha("FechaNomina")).Date,
            f.EnteroNulo("TrimestreActivo"), !f.EsNulo("EjercicioCerrado") && f.Logico("EjercicioCerrado")), id));

    private Task<IReadOnlyList<NominaLeida>> LeerNominasAsync(int idImportacion, string idSociedad) => bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT n.*, es.IdIdentidadContable FROM Nominas AS n LEFT JOIN (SELECT IdEmpleado, IdIdentidadContable FROM EmpleadosSociedad WHERE IdSociedad = ?) AS es " +
        "ON n.IdEmpleado = es.IdEmpleado " +
        "WHERE n.IdImportacionNomina = ? ORDER BY n.Ordinal",
        f =>
        {
            var importes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in CamposNomina.Importes)
                if (!f.EsNulo(c.Nombre)) importes[c.Nombre] = f.Decimal(c.Nombre);
            return new NominaLeida(f.Entero("IdNomina"), f.Entero("Ordinal"), f.Texto("NombreOrigen") ?? "", f.Texto("CodigoTrabajador"), f.Texto("NifOrigen"),
                f.Texto("TipoPaga"), f.EsNulo("FechaNomina") ? null : f.Fecha("FechaNomina"), f.EnteroNulo("IdIdentidadContable"), importes);
        }, idSociedad, idImportacion));

    private Task<string?> EstadoAsync(int id) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT Estado FROM ImportacionesNomina WHERE IdImportacionNomina = ?", f => f.Texto("Estado"), id));

    private async Task<string> DescripcionImportacionAsync(int id)
    {
        var d = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT PeriodoDesde, TipoPaga, NombreFichero FROM ImportacionesNomina WHERE IdImportacionNomina = ?",
            f => (Desde: f.Fecha("PeriodoDesde"), Tipo: f.Texto("TipoPaga"), Fichero: f.Texto("NombreFichero")), id));
        var mes = d.Desde.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-ES"));
        return $"de {mes}" + (d.Tipo == "EXTRA" ? " (paga extra)" : "") + (d.Fichero is { Length: > 0 } fi ? $" ({fi})" : "");
    }

    /// <summary>Siguiente número de contabilización de una importación (1 la primera vez): no reutiliza la clave de un lote anulado.</summary>
    private async Task<int> SiguienteNumeroAsync(int idImportacion)
    {
        var prefijo = $"{idImportacion.ToString(CultureInfo.InvariantCulture)}.";
        var claves = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT DISTINCT ClaveEjecucion FROM Asientos WHERE MotorPropietario = ? AND CodigoProceso IN (?, ?) AND ClaveEjecucion LIKE ?",
            f => f.Texto("ClaveEjecucion") ?? "", MotorPropietario, CodigoProceso, CodigoAnulacion, prefijo + "%"));
        return claves.Select(c => int.TryParse(c[prefijo.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
    }

    private Task<int?> IdentidadDeCodigoAsync(string idSociedad, string codigo) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT TOP 1 c.IdIdentidadContable FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
        "WHERE se.IdSociedad = ? AND c.Codigo = ? ORDER BY se.Ejercicio DESC", f => (int?)f.Entero("IdIdentidadContable"), idSociedad, codigo));

    private DateTime Ahora() => new(_reloj.Ahora.Year, _reloj.Ahora.Month, _reloj.Ahora.Day, _reloj.Ahora.Hour, _reloj.Ahora.Minute, _reloj.Ahora.Second);

    private static string? Recortar(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];
}
