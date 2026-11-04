using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Permisos;

namespace ProConta.Facturacion.Programadas;

/// <summary>Resultado de guardar una programada: la guardada o, si no se ha guardado, por qué (con el campo que corregir).</summary>
public sealed record ResultadoProgramada(FacturaProgramada? Programada, IReadOnlyList<Incidencia> Incidencias)
{
    public bool Guardada => Programada is not null;
}

/// <summary>Programada con vencimientos pendientes hasta una fecha, para elegir cuáles generar.</summary>
public sealed record ProgramadaPendiente(FacturaProgramada Programada, string Cliente, IReadOnlyList<DateTime> Vencimientos);

/// <summary>Factura creada por un lote: su vencimiento y si se ha validado.</summary>
public sealed record FacturaGenerada(int IdFactura, string Programada, DateTime Vencimiento, string Rotulo, bool Validada);

/// <summary>
/// Resultado de un lote (RF-PRO-03, 04): lo creado y las incidencias de cada programada, que no detienen a las demás.
/// Una factura que no se ha podido validar queda como borrador y se valida después desde Facturas.
/// </summary>
public sealed record ResultadoLote(int IdLote, IReadOnlyList<FacturaGenerada> Facturas, IReadOnlyList<string> Incidencias)
{
    public bool SinIncidencias => Incidencias.Count == 0;
}

/// <summary>
/// Servicio del ente FacturaProgramada (facturacion/programadas): alta, edición, duplicado y borrado de programadas,
/// y generación por lotes. Los permisos se comprueban aquí: FACTURAS.EDITAR para todo, y validar las generadas lo
/// comprueba el servicio de facturas con FACTURAS.VALIDAR.
/// </summary>
public sealed class ServicioProgramadas
{
    /// <summary>Tope de facturas de una programada en un mismo lote (atrasos), para que un error de fechas no cree cientos.</summary>
    public const int MaximoPorProgramada = 24;

    private readonly IBaseDatos _bd;
    private readonly IServicioSociedades _sociedades;
    private readonly ServicioFacturas _facturas;
    private readonly IAutorizadorAcciones _autorizador;
    private readonly IReloj _reloj;

    public ServicioProgramadas(IBaseDatos bd, IServicioSociedades sociedades, ServicioFacturas facturas, IAutorizadorAcciones? autorizador = null, IReloj? reloj = null)
    {
        _bd = bd;
        _sociedades = sociedades;
        _facturas = facturas;
        _autorizador = autorizador ?? AutorizadorTodo.Instancia;
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    public Task<FacturaProgramada?> ObtenerAsync(int id) => _bd.EjecutarAsync(s => RepositorioProgramadas.CargarAsync(s, id));

    public Task<IReadOnlyList<FacturaProgramada>> ListarAsync(string idSociedad) => _bd.EjecutarAsync(s => RepositorioProgramadas.ListarAsync(s, idSociedad));

    /// <summary>Facturas generadas por cada programada en un ejercicio (para el listado).</summary>
    public Task<IReadOnlyDictionary<int, int>> GeneradasEnEjercicioAsync(int idSociedadEjercicio) =>
        _bd.EjecutarAsync(async s =>
        {
            var filas = await s.ConsultarAsync("SELECT IdFacturaProgramada, COUNT(*) AS N FROM Facturas WHERE IdSociedadEjercicio = ? AND IdFacturaProgramada IS NOT NULL GROUP BY IdFacturaProgramada",
                f => (f.Entero("IdFacturaProgramada"), f.Entero("N")), idSociedadEjercicio);
            return (IReadOnlyDictionary<int, int>)filas.ToDictionary(x => x.Item1, x => x.Item2);
        });

    // ---------- alta y edición ----------

    /// <summary>
    /// Programada nueva sin guardar: la plantilla de un borrador nuevo del ejercicio de trabajo, mensual, desde hoy, con
    /// emisión el día 1 y como periodo de la primera factura el mes de su emisión. Nace activa.
    /// </summary>
    public async Task<Resultado<FacturaProgramada>> NuevaAsync(ContextoSociedadEjercicio contexto)
    {
        var b = await _facturas.NuevoBorradorAsync(contexto);
        if (!b.EsCorrecto) return Resultado<FacturaProgramada>.Fallo(b.Errores);
        var p = new FacturaProgramada
        {
            IdSociedadEjercicio = contexto.IdSociedadEjercicio,
            Plantilla = b.Valor,
            FechaInicio = _reloj.Ahora.Date,
            DiaEmision = 1,
            Activa = true,
            FechaAlta = _reloj.Ahora
        };
        ProponerPeriodo(p);
        return Resultado<FacturaProgramada>.Correcto(p);
    }

    /// <summary>Propone como periodo de la primera factura el mes completo de su emisión.</summary>
    public static void ProponerPeriodo(FacturaProgramada p)
    {
        if (p.Emision(0) is not { } e) return;
        var desde = new DateTime(e.Year, e.Month, 1);
        p.Plantilla.FechaInicioValidez = desde;
        p.Plantilla.FechaFinValidez = desde.AddMonths(Math.Max(1, p.Periodicidad.Meses)).AddDays(-1);
    }

    /// <summary>
    /// Programada nueva sin guardar a partir de una factura («Programar…»): copia cliente, condiciones, descripción y
    /// líneas (una rectificativa, en positivo y como F1), sin número ni fechas propias de la factura.
    /// </summary>
    public async Task<Resultado<FacturaProgramada>> DesdeFacturaAsync(int idFactura, ContextoSociedadEjercicio contexto)
    {
        var origen = await _facturas.ObtenerAsync(idFactura);
        if (origen is null) return Resultado<FacturaProgramada>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura), "Otro operador la ha borrado.", "Actualiza el listado."));
        var nueva = await NuevaAsync(contexto);
        if (!nueva.EsCorrecto) return nueva;
        var p = nueva.Valor;
        var f = p.Plantilla;
        f.IdCliente = origen.IdCliente;
        f.Descripcion = origen.Descripcion;
        f.NotasInternas = origen.NotasInternas;
        f.FormaDePago = origen.FormaDePago ?? f.FormaDePago;
        f.DiaDePago = origen.DiaDePago;
        f.TipoRetencion = origen.TipoRetencion;
        f.TipoFactura = origen.EsRectificativa ? "F1" : origen.TipoFactura;
        foreach (var l in origen.Lineas) f.AnadirLinea(LineasProgramada.Copiar(origen.EsRectificativa ? l.Copiar(invertir: true) : l));
        return Resultado<FacturaProgramada>.Correcto(p);
    }

    /// <summary>Todas las incidencias que impedirían guardar la programada tal como está, sin cambiar nada.</summary>
    public async Task<IReadOnlyList<Incidencia>> ComprobarAsync(FacturaProgramada p)
    {
        var r = new List<Incidencia>(p.Activa ? ReglasProgramada.ValidarParaActivar(p) : ReglasProgramada.ValidarProgramacion(p));
        var ejercicio = await _sociedades.ObtenerEjercicioAsync(p.IdSociedadEjercicio);
        if (ejercicio is null) { r.Add(new("Facturas.SinEjercicio", "El ejercicio de la programada no existe.")); return r; }
        var serie = await _bd.EjecutarAsync(s => RepositorioFacturas.SerieAsync(s, p.Plantilla.IdSerie));
        if (serie is null || serie.EsRectificativa)
            r.Add(new("Facturas.SerieNoValida", "Elige una serie normal: con ella se numerarán las facturas (en otro ejercicio, la del mismo prefijo o la predeterminada).", "IdSerie"));
        else if (await _sociedades.ObtenerEjercicioAsync(serie.IdSociedadEjercicio) is not { } es || es.IdSociedad != ejercicio.IdSociedad)
            r.Add(new("Facturas.SerieNoValida", "La serie elegida es de otra sociedad. Elige una de esta sociedad.", "IdSerie"));
        return r;
    }

    /// <summary>
    /// Guarda la programada (alta o modificación) con concurrencia optimista. Activa exige que esté completa, como para
    /// validar una factura; pausada solo exige que la programación tenga sentido. Cambiarla solo afecta a las facturas
    /// que se generen a partir de ahora.
    /// </summary>
    public async Task<ResultadoProgramada> GuardarAsync(FacturaProgramada programada, Actor actor, string? anotacion = null)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar))
            return new(null, [new("Seguridad.SinPermiso", "No tienes permiso para editar facturas programadas. Pídeselo a quien administra los permisos.")]);
        var incidencias = await ComprobarAsync(programada);
        if (incidencias.Count > 0) return new(null, incidencias);
        try
        {
            var id = await _bd.EnTransaccionAsync(async s =>
            {
                var p = programada.Clonar();
                foreach (var l in p.Plantilla.Lineas) l.Nombre = l.Nombre.Trim();
                var ahora = _reloj.Ahora;
                if (p.EsNueva)
                {
                    p.FechaAlta = ahora;
                    p.OperadorAlta = actor.Nombre;
                    p.Anotar(ahora, actor.Nombre, anotacion ?? "Programada creada");
                    return await RepositorioProgramadas.InsertarAsync(s, p);
                }
                var actual = await RepositorioProgramadas.CargarAsync(s, p.IdFacturaProgramada);
                if (actual is not null && actual.Activa != p.Activa) p.Anotar(ahora, actor.Nombre, p.Activa ? "Reanudada" : "Pausada");
                if (anotacion is not null) p.Anotar(ahora, actor.Nombre, anotacion);
                await RepositorioProgramadas.ActualizarAsync(s, p);
                return p.IdFacturaProgramada;
            });
            return new(await ObtenerAsync(id), []);
        }
        catch (ExcepcionDatos ex)
        {
            return new(null, [new(ex.Error.Codigo, ex.Error.Codigo == "Persistencia.Conflicto" || ex is ConflictoConcurrenciaException
                ? "Otro operador ha cambiado esta programada (o se ha generado una factura) después de abrirla. Vuelve a abrirla y repite el cambio."
                : ex.Error.TextoOperador())]);
        }
    }

    /// <summary>Pausa o reanuda una programada (Activa). Reanudar exige que esté completa.</summary>
    public async Task<ResultadoProgramada> CambiarActivaAsync(int id, bool activa, int versionLeida, Actor actor)
    {
        var p = await ObtenerAsync(id);
        if (p is null) return new(null, [new("Programadas.NoExiste", "La programada ya no existe: otro operador la ha borrado.")]);
        if (p.Version != versionLeida) return new(null, [new("Programadas.Conflicto", "Otro operador ha cambiado esta programada. Actualiza el listado y repite.")]);
        p.Activa = activa;
        return await GuardarAsync(p, actor);
    }

    /// <summary>
    /// Duplica la programada (RF-PRO-06): conserva contenido, programación y preferencias, pero queda pausada, sin
    /// facturas generadas ni historial, para que nadie la genere antes de revisarla.
    /// </summary>
    public async Task<ResultadoProgramada> DuplicarAsync(int id, Actor actor)
    {
        var origen = await ObtenerAsync(id);
        if (origen is null) return new(null, [new("Programadas.NoExiste", "La programada ya no existe: otro operador la ha borrado.")]);
        var p = origen.Clonar();
        p.IdFacturaProgramada = 0;
        p.Version = 0;
        p.Activa = false;
        p.FacturasGeneradas = 0;
        p.FechaUltimaGeneracion = null;
        p.Historial = null;
        return await GuardarAsync(p, actor, $"Duplicado de {origen.Rotulo}: queda pausada hasta revisarla");
    }

    /// <summary>Borra una programada que no ha generado ninguna factura; si ya tiene facturas, se pausa en su lugar.</summary>
    public async Task<Resultado> EliminarAsync(int id, int versionLeida)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar))
            return Resultado.Fallo(new Error("Seguridad.SinPermiso", "No tienes permiso para eliminar facturas programadas.", FacturaProgramada.RotuloDe(id), null, "Pídeselo a quien administra los permisos."));
        try
        {
            return await _bd.EnTransaccionAsync(async s =>
            {
                var p = await RepositorioProgramadas.CargarAsync(s, id);
                if (p is null) return Resultado.Correcto();
                if (p.Version != versionLeida)
                    return Resultado.Fallo(new Error("Programadas.Conflicto", "Otro operador ha cambiado esta programada después de abrirla.", p.Rotulo, null, "Vuelve a abrirla y repite."));
                var facturas = await RepositorioProgramadas.FacturasDeAsync(s, id);
                if (facturas > 0)
                    return Resultado.Fallo(new Error("Programadas.ConFacturas", $"{p.Rotulo} ya ha generado {facturas} {(facturas == 1 ? "factura" : "facturas")} y no se puede eliminar: las facturas guardan de qué programada vienen.",
                        p.Rotulo, null, "Páusala para que no genere más («Pausar»)."));
                await RepositorioProgramadas.BorrarAsync(s, p);
                return Resultado.Correcto();
            });
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado.Fallo(ex.Error);
        }
    }

    // ---------- generación ----------

    /// <summary>
    /// Programadas activas de la sociedad con vencimientos hasta <paramref name="hasta"/> (incluido), con esos
    /// vencimientos (como mucho <see cref="MaximoPorProgramada"/> cada una).
    /// </summary>
    public async Task<IReadOnlyList<ProgramadaPendiente>> PendientesAsync(string idSociedad, DateTime hasta)
    {
        var lista = await ListarAsync(idSociedad);
        var clientes = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT c.IdCliente, t.Nombre FROM Clientes AS c INNER JOIN Terceros AS t ON c.IdTercero = t.IdTercero",
            f => (f.Entero("IdCliente"), f.Texto("Nombre") ?? "")));
        var nombres = clientes.GroupBy(c => c.Item1).ToDictionary(g => g.Key, g => g.First().Item2);
        return lista.Where(p => p.Activa)
            .Select(p => new ProgramadaPendiente(p, p.Plantilla.IdCliente is int c && nombres.TryGetValue(c, out var n) ? n : "Sin cliente", Vencimientos(p, hasta)))
            .Where(x => x.Vencimientos.Count > 0)
            .OrderBy(x => x.Vencimientos[0]).ThenBy(x => x.Programada.IdFacturaProgramada)
            .ToList();
    }

    private static IReadOnlyList<DateTime> Vencimientos(FacturaProgramada p, DateTime hasta)
    {
        var r = new List<DateTime>();
        for (var i = p.FacturasGeneradas; r.Count < MaximoPorProgramada && p.ExisteVencimiento(i) && p.Emision(i)!.Value <= hasta.Date; i++) r.Add(p.Emision(i)!.Value);
        return r;
    }

    /// <summary>
    /// Genera, en un lote de la sociedad de trabajo, las facturas de los vencimientos hasta <paramref name="hasta"/> de
    /// las programadas elegidas. Cada factura se crea y su programada avanza en una sola transacción (nunca una sin la
    /// otra); si otro proceso ya la avanzó, esa programada no genera nada y se informa. Un fallo en una programada se
    /// anota como incidencia y el lote sigue con las demás. Las programadas que validan pasan después cada factura por
    /// la validación normal (número, instantánea, paso fiscal y asiento); si no se puede validar, queda en borrador.
    /// </summary>
    public async Task<Resultado<ResultadoLote>> GenerarAsync(ContextoSociedadEjercicio contexto, DateTime hasta, IReadOnlyList<int> ids, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar))
            return Resultado<ResultadoLote>.Fallo(new Error("Seguridad.SinPermiso", "No tienes permiso para generar facturas programadas.", null, null, "Pídeselo a quien administra los permisos."));
        if (ids.Count == 0)
            return Resultado<ResultadoLote>.Fallo(new Error("Programadas.SinSeleccion", "No hay ninguna programada elegida.", null, null, "Marca las programadas que quieres generar."));
        var ejercicios = await _sociedades.ListarEjerciciosAsync(contexto.IdSociedad);
        var inicio = _reloj.Ahora;
        int idLote;
        try
        {
            idLote = await _bd.EjecutarAsync(s => RepositorioProgramadas.AbrirLoteAsync(s, contexto.IdSociedadEjercicio, hasta, ids.Count, actor.Nombre, inicio,
                $"{inicio:dd/MM/yyyy HH:mm} · {actor.Nombre} · Inicia el lote hasta el {hasta:dd/MM/yyyy} con {ids.Count} {(ids.Count == 1 ? "programada" : "programadas")}"));
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<ResultadoLote>.Fallo(ex.Error);
        }

        var generadas = new List<FacturaGenerada>();
        var incidencias = new List<string>();
        foreach (var id in ids.Distinct())
        {
            try
            {
                await GenerarProgramadaAsync(id, hasta, idLote, contexto.IdSociedad, ejercicios, actor, generadas, incidencias);
            }
            catch (Exception ex) when (ex is ExcepcionDatos or InvalidOperationException)
            {
                incidencias.Add($"{FacturaProgramada.RotuloDe(id)}: {(ex is ExcepcionDatos d ? d.Error.TextoOperador() : ex.Message)}");
            }
        }

        var fin = _reloj.Ahora;
        var historial = $"{inicio:dd/MM/yyyy HH:mm} · {actor.Nombre} · Inicia el lote hasta el {hasta:dd/MM/yyyy} con {ids.Count} {(ids.Count == 1 ? "programada" : "programadas")}" + Environment.NewLine +
                        $"{fin:dd/MM/yyyy HH:mm} · {actor.Nombre} · Cierra el lote: {generadas.Count} {(generadas.Count == 1 ? "factura creada" : "facturas creadas")}, {incidencias.Count} {(incidencias.Count == 1 ? "incidencia" : "incidencias")}";
        try
        {
            await _bd.EjecutarAsync(async s => { await RepositorioProgramadas.CerrarLoteAsync(s, idLote, incidencias.Count == 0 ? "COMPLETADO" : "ERROR", generadas.Count,
                incidencias.Count == 0 ? null : string.Join(Environment.NewLine, incidencias), fin, historial); return 0; });
        }
        catch (ExcepcionDatos ex)
        {
            incidencias.Add($"No he podido cerrar el lote {idLote}: {ex.Error.TextoOperador()}");
        }
        return Resultado<ResultadoLote>.Correcto(new ResultadoLote(idLote, generadas, incidencias));
    }

    private async Task GenerarProgramadaAsync(int id, DateTime hasta, int idLote, string idSociedad, IReadOnlyList<SociedadEjercicio> ejercicios, Actor actor,
        List<FacturaGenerada> generadas, List<string> incidencias)
    {
        for (var n = 0; n < MaximoPorProgramada; n++)
        {
            var p = await ObtenerAsync(id);
            if (p is null) { incidencias.Add($"{FacturaProgramada.RotuloDe(id)}: ya no existe."); return; }
            if (n == 0 && !p.Activa) { incidencias.Add($"{p.Rotulo}: está pausada; reanúdala para generarla."); return; }
            if (!p.Activa) return;
            var indice = p.FacturasGeneradas;
            if (!p.ExisteVencimiento(indice) || p.Emision(indice)!.Value > hasta.Date)
            {
                if (n == 0) incidencias.Add($"{p.Rotulo}: no tiene ningún vencimiento hasta el {hasta:dd/MM/yyyy} (quizá otro operador ya la ha generado).");
                return;
            }
            var completa = ReglasProgramada.ValidarParaActivar(p);
            if (completa.Count > 0) { incidencias.Add($"{p.Rotulo}: {string.Join(" ", completa.Select(i => i.Mensaje))}"); return; }

            var vencimiento = p.Emision(indice)!.Value;
            var hoy = _reloj.Ahora.Date;
            // La factura se expide en la fecha del vencimiento o, si ya pasó, hoy; va al ejercicio de esa fecha.
            var fecha = vencimiento > hoy ? vencimiento : hoy;
            var ejercicio = ejercicios.FirstOrDefault(e => e.Ejercicio == fecha.Year);
            if (ejercicio is null)
            {
                incidencias.Add($"{p.Rotulo}: la sociedad no tiene el ejercicio {fecha.Year}, que es donde va la factura del {fecha:dd/MM/yyyy}. Créalo en Parámetros y tablas › Ejercicios y vuelve a generar.");
                return;
            }
            if (ejercicio.Cerrado)
            {
                incidencias.Add($"{p.Rotulo}: el ejercicio {fecha.Year} está cerrado y no admite facturas nuevas.");
                return;
            }

            Resultado<FacturaVenta> resultado;
            try
            {
                resultado = await _bd.EnTransaccionAsync(async s =>
                {
                    var serie = await SerieDestinoAsync(s, p.Plantilla.IdSerie, ejercicio);
                    if (!serie.EsCorrecto) return Resultado<FacturaVenta>.Fallo(serie.Errores);
                    var (desde, hastaPeriodo) = p.Periodo(indice);
                    var plantilla = p.Plantilla;
                    var ahora = _reloj.Ahora;
                    var f = new FacturaVenta
                    {
                        IdSociedadEjercicio = ejercicio.IdSociedadEjercicio,
                        IdSerie = serie.Valor.IdSerie,
                        IdCliente = plantilla.IdCliente,
                        IdContrato = p.IdContrato,
                        FechaFactura = fecha,
                        FechaInicioValidez = desde,
                        FechaFinValidez = hastaPeriodo,
                        FechaCreacion = ahora,
                        FormaDePago = plantilla.FormaDePago,
                        DiaDePago = plantilla.DiaDePago,
                        Descripcion = plantilla.Descripcion,
                        NotasInternas = plantilla.NotasInternas,
                        TipoRetencion = plantilla.TipoRetencion,
                        TipoFactura = plantilla.TipoFactura
                    };
                    foreach (var l in plantilla.Lineas) f.AnadirLinea(LineasProgramada.Copiar(l));
                    f.Anotar(ahora, actor.Nombre, $"Generada por la programada {p.Rotulo}, vencimiento del {vencimiento:dd/MM/yyyy} (lote {idLote})");
                    var idFactura = await RepositorioFacturas.InsertarAsync(s, f);
                    await RepositorioProgramadas.MarcarOrigenAsync(s, idFactura, p.IdFacturaProgramada, idLote, vencimiento);
                    p.Anotar(ahora, actor.Nombre, $"Genera {f.Rotulo} del vencimiento del {vencimiento:dd/MM/yyyy} (lote {idLote})");
                    p.FacturasGeneradas = indice + 1;
                    await RepositorioProgramadas.AvanzarAsync(s, p, indice, ahora);
                    return Resultado<FacturaVenta>.Correcto(f);
                });
            }
            catch (ConflictoConcurrenciaException)
            {
                resultado = Resultado<FacturaVenta>.Fallo(new Error("Programadas.Conflicto",
                    "otro proceso la ha cambiado o generado a la vez y no se ha creado nada.", null, null, "Actualiza la lista y vuelve a generar si hace falta."));
            }
            catch (UnicidadException)
            {
                resultado = Resultado<FacturaVenta>.Fallo(new Error("Programadas.Duplicada", $"ya existe la factura del vencimiento del {vencimiento:dd/MM/yyyy}; no se crea otra."));
            }

            if (!resultado.EsCorrecto)
            {
                incidencias.Add($"{p.Rotulo}: {string.Join(" ", resultado.Errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")))}");
                return;
            }

            var creada = resultado.Valor;
            var validada = false;
            if (p.AlGenerar == AlGenerar.Validar)
            {
                var v = await _facturas.ValidarAsync(creada.IdFactura, creada.Version, actor);
                validada = v.Validada;
                if (!v.Validada)
                    incidencias.Add($"{p.Rotulo}: {creada.Rotulo} creado pero no validado; queda como borrador para validarlo desde Facturas. {string.Join(" ", v.Incidencias.Select(i => i.Mensaje))}");
                else if (!v.Contabilizada)
                    incidencias.Add($"{p.Rotulo}: {v.Factura!.NumeroFactura} validada sin asiento. {string.Join(" ", (v.ErrorContabilizacion ?? []).Select(e => e.Mensaje))} Contabilízala desde Facturas.");
                if (v.Validada) creada = v.Factura!;
            }
            generadas.Add(new FacturaGenerada(creada.IdFactura, p.Rotulo, vencimiento, creada.Rotulo, validada));
        }
    }

    /// <summary>
    /// Serie con la que va la factura: la de la programada si es del ejercicio de destino; si no, la serie normal del
    /// ejercicio de destino con el mismo prefijo o, si no hay, su predeterminada.
    /// </summary>
    private static async Task<Resultado<Serie>> SerieDestinoAsync(ISesionDatos s, int idSerie, SociedadEjercicio destino)
    {
        var propia = await RepositorioFacturas.SerieAsync(s, idSerie);
        if (propia is not null && propia.IdSociedadEjercicio == destino.IdSociedadEjercicio && !propia.EsRectificativa) return Resultado<Serie>.Correcto(propia);
        var series = (await RepositorioFacturas.SeriesAsync(s, destino.IdSociedadEjercicio)).Where(x => !x.EsRectificativa).ToList();
        if (propia is not null && series.FirstOrDefault(x => x.Prefijo.Trim() == propia.Prefijo.Trim()) is { } igual) return Resultado<Serie>.Correcto(igual);
        return await RepositorioFacturas.SerieNormalAsync(s, destino.IdSociedadEjercicio, destino.Ejercicio);
    }
}
