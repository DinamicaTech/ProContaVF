using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Implantacion.ContaSol;

/// <summary>Avance de la importación, para la pantalla.</summary>
public sealed record ProgresoImportacion(int Ejercicio, string Fase, int Hechos, int Total, string Texto);

/// <summary>Resultado de importar un ejercicio.</summary>
public sealed record ResumenEjercicio(
    int Ejercicio,
    string Ruta,
    int IdImportacion,
    string Estado,
    int Centros,
    int Cuentas,
    int AsientosGrabados,
    int AsientosSinCambios,
    int AsientosBorrados,
    int Apuntes,
    int Registros,
    int Terceros,
    IReadOnlyList<Incidencia> Incidencias)
{
    public int Bloqueantes => Incidencias.Count(i => i.Severidad == Severidad.Bloqueante);
    public int Advertencias => Incidencias.Count(i => i.Severidad == Severidad.Advertencia);

    public string Texto =>
        $"{Ejercicio}: {Cuentas} cuentas, {AsientosGrabados + AsientosSinCambios} asientos ({AsientosGrabados} grabados, {AsientosSinCambios} sin cambios" +
        (AsientosBorrados > 0 ? $", {AsientosBorrados} borrados" : "") + $"), {Apuntes} apuntes, {Registros} registros de IVA y {Terceros} terceros. " +
        $"{Bloqueantes} incidencias bloqueantes y {Advertencias} advertencias.";
}

/// <summary>Estados de ImportacionesContaSol y FasesImportacionContaSol.</summary>
public static class EstadosImportacion
{
    public const string EnCurso = "EN_CURSO";
    public const string Completada = "COMPLETADA";
    public const string ConIncidencias = "CON_INCIDENCIAS";
    public const string Fallida = "FALLIDA";
    public const string Cancelada = "CANCELADA";
}

/// <summary>Tipos de entidad de MapasIdentidadContaSol (los mismos que la importación anterior de 2026-08-22).</summary>
public static class TiposMapa
{
    public const string Centro = "CENTRO";
    public const string Cuenta = "CUENTA";
    public const string Asiento = "ASIENTO";
    public const string IvaSoportado = "IVA_SOPORTADO";
    public const string IvaRepercutido = "IVA_REPERCUTIDO";
    public const string Cliente = "CLIENTE";
    public const string Proveedor = "PROVEEDOR";
}

/// <summary>
/// Importador de ContaSol (implantacion/importador-contasol): importa cada ejercicio de ContaSol (una BD por ejercicio) en la
/// única BD de ProContaVF, en orden de ejercicio. Cada ejercicio es una ejecución de ImportacionesContaSol con sus fases,
/// incidencias y mapas de identidad. Repetir la importación de un ejercicio sustituye lo importado antes sin duplicar: los
/// asientos se graban en carga masiva por el motor (IMotorAsientos.CargarLibroExternoAsync, una clave por asiento de ContaSol),
/// los asientos sin cambios no se tocan y los que ya no están en ContaSol se borran. Una cuenta es la misma en todos los ejercicios
/// si tiene el mismo código (reserva histórica de códigos); un cliente o proveedor de ContaSol es el mismo tercero entre
/// ejercicios si conserva el NIF, y otro tercero si la subcuenta ha cambiado de titular (la cuenta queda en el más reciente).
/// </summary>
public sealed class ImportadorContaSol(IBaseDatos bd, IMotorAsientos motor, ICatalogoCentrosCoste centros, IRepositorioTerceros terceros, ServicioRoles roles,
    LectorContaSol? lector = null, IReloj? reloj = null)
{
    public const string VersionImportador = "2";
    public const string CodigoProceso = "CONTASOL";

    /// <summary>Tamaño de los lotes de escritura directa (cuentas, mapas, IVA): las transacciones de Access tienen un límite de bloqueos.</summary>
    private const int TamanoLote = 150;

    private readonly IBaseDatos _bdDestino = bd;
    private readonly IMotorAsientos _motor = motor;
    private readonly ICatalogoCentrosCoste _centros = centros;
    private readonly IRepositorioTerceros _terceros = terceros;
    private readonly ServicioRoles _roles = roles;
    private readonly LectorContaSol _lector = lector ?? new LectorContaSol();
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>Primer ejercicio cuyos clientes y proveedores pasan a terceros: los últimos cinco (propietario, 2026-11-10).</summary>
    public int PrimerEjercicioConTerceros => _reloj.Ahora.Year - 4;

    // =====================================================================================================================
    // Inspección y simulación: lee los orígenes y comprueba el destino sin escribir nada.
    // =====================================================================================================================

    /// <summary>
    /// Lee y planifica cada BD de ContaSol y comprueba el destino, sin escribir nada (simulación). Devuelve los planes en orden
    /// de ejercicio, cada uno con sus incidencias, incluidas las del destino.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<PlanEjercicio>>> AnalizarAsync(IEnumerable<string> rutas, IProgress<ProgresoImportacion>? progreso = null,
        CancellationToken cancelacion = default)
    {
        var planes = new List<PlanEjercicio>();
        var errores = new List<Error>();
        foreach (var ruta in rutas.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancelacion.ThrowIfCancellationRequested();
            progreso?.Report(new ProgresoImportacion(0, "Lectura", 0, 0, $"Leyendo {Path.GetFileName(ruta)}…"));
            var leido = await _lector.LeerAsync(ruta, cancelacion);
            if (!leido.EsCorrecto) { errores.AddRange(leido.Errores); continue; }
            planes.Add(PlanificadorContaSol.Planificar(leido.Valor, PrimerEjercicioConTerceros));
        }
        if (errores.Count > 0) return Resultado<IReadOnlyList<PlanEjercicio>>.Fallo(errores);

        var repetidos = planes.GroupBy(p => (p.Empresa.Codigo, p.Ejercicio)).Where(g => g.Count() > 1).ToList();
        if (repetidos.Count > 0)
            return Resultado<IReadOnlyList<PlanEjercicio>>.Fallo(repetidos.Select(g => new Error("ContaSol.EjercicioRepetido",
                $"Has elegido dos BD del ejercicio {g.Key.Ejercicio} de la empresa {g.Key.Codigo}.", string.Join(", ", g.Select(p => Path.GetFileName(p.Origen.Ruta))),
                "Cada ejercicio se importa de una sola BD.", "Quita una de las dos de la lista.")));

        var conDestino = new List<PlanEjercicio>();
        foreach (var original in planes.OrderBy(p => p.Empresa.Codigo, StringComparer.Ordinal).ThenBy(p => p.Ejercicio))
        {
            // Los ejercicios antiguos de ContaSol pueden no tener el NIF de la empresa (2018–2020): se toma el de otro ejercicio de la misma empresa.
            var plan = original;
            if (plan.Empresa.Nif.Trim().Length == 0
                && planes.Where(p => p.Empresa.Codigo == plan.Empresa.Codigo && p.Empresa.Nif.Trim().Length > 0).OrderByDescending(p => p.Ejercicio).FirstOrDefault() is { } conNif)
                plan = plan with
                {
                    Origen = plan.Origen with { Empresa = plan.Empresa with { Nif = conNif.Empresa.Nif.Trim() } },
                    Incidencias = [.. plan.Incidencias, new Incidencia(PlanificadorContaSol.FaseInspeccion, "F_EMP", plan.Empresa.Codigo, "NIFEMP", "ContaSol.EmpresaSinNif",
                        Severidad.Informativa, $"La BD de {plan.Ejercicio} no tiene el NIF de la empresa: uso el de {conNif.Ejercicio}, {conNif.Empresa.Nif.Trim()}.")],
                };
            conDestino.Add(plan with { Incidencias = [.. plan.Incidencias, .. await ComprobarDestinoAsync(plan)] });
        }
        return Resultado<IReadOnlyList<PlanEjercicio>>.Correcto(conDestino);
    }

    /// <summary>Comprobaciones del destino: la empresa no es otra con el mismo código y el ejercicio no tiene una importación de otra versión.</summary>
    private Task<IReadOnlyList<Incidencia>> ComprobarDestinoAsync(PlanEjercicio plan) =>
        _bdDestino.EjecutarAsync<IReadOnlyList<Incidencia>>(async s =>
        {
            var lista = new List<Incidencia>();
            var empresa = plan.Empresa;
            var cif = await s.ConsultarPrimeroAsync("SELECT CIF FROM Sociedades WHERE IdSociedad = ?", f => f.Texto("CIF") ?? "", empresa.Codigo);
            if (cif is null && empresa.Nif.Trim().Length == 0)
                lista.Add(new Incidencia("destino", "F_EMP", empresa.Codigo, "NIFEMP", "ContaSol.EmpresaSinNif", Severidad.Bloqueante,
                    $"La empresa {empresa.Codigo} no existe en ProContaVF y la BD de ContaSol de {empresa.Ejercicio} no tiene su NIF, que hace falta para darla de alta. " +
                    "Importa también un ejercicio que lo tenga (o escríbelo en los datos de la empresa en ContaSol)."));
            if (cif is not null && cif.Trim().Length > 0 && empresa.Nif.Trim().Length > 0
                && PlanificadorContaSol.NormalizarNif(cif) != PlanificadorContaSol.NormalizarNif(empresa.Nif))
                lista.Add(new Incidencia("destino", "F_EMP", empresa.Codigo, "NIFEMP", "ContaSol.EmpresaDistinta", Severidad.Bloqueante,
                    $"La empresa {empresa.Codigo} de ProContaVF tiene el CIF {cif.Trim()} y la de ContaSol el {empresa.Nif.Trim()}: no sé si son la misma. " +
                    "Corrige el CIF de la empresa en Parámetros y tablas o elige la BD de ContaSol de esta empresa."));
            var anteriores = await s.ConsultarAsync(
                "SELECT i.IdImportacionContaSol, i.VersionImportador, i.FechaHoraInicio FROM ImportacionesContaSol AS i INNER JOIN SociedadesEjercicios AS se " +
                "ON i.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE se.IdSociedad = ? AND se.Ejercicio = ?",
                f => (Id: f.Entero("IdImportacionContaSol"), Version: f.Texto("VersionImportador") ?? "", Fecha: f.Fecha("FechaHoraInicio")),
                empresa.Codigo, empresa.Ejercicio.ToString(CultureInfo.InvariantCulture));
            foreach (var a in anteriores.Where(a => a.Version != VersionImportador))
            {
                var manuales = Convert.ToInt32(await s.EscalarAsync(
                    "SELECT COUNT(*) FROM MapasIdentidadContaSol AS m INNER JOIN Asientos AS a ON m.IdDestino = a.IdAsiento " +
                    "WHERE m.IdImportacionContaSol = ? AND m.TipoEntidad = ? AND a.EsAutomatico = False", a.Id, TiposMapa.Asiento), CultureInfo.InvariantCulture);
                if (manuales > 0)
                    lista.Add(new Incidencia("destino", "ImportacionesContaSol", a.Id.ToString(CultureInfo.InvariantCulture), null, "ContaSol.ImportacionAnterior", Severidad.Bloqueante,
                        $"El ejercicio {empresa.Ejercicio} ya tiene {manuales} asientos de una importación de ContaSol hecha el {a.Fecha:dd/MM/yyyy} con otra versión del importador, " +
                        "que no sé sustituir sin duplicarlos. Importa en una BD nueva (copia de la estructura sin datos) o pide que se retire esa importación."));
            }
            return lista;
        });

    // =====================================================================================================================
    // Importación
    // =====================================================================================================================

    /// <summary>
    /// Importa los planes en orden de ejercicio. Un plan con incidencias bloqueantes se importa igualmente sin los registros
    /// afectados, y su ejecución queda «con incidencias»; una bloqueante del destino (otra empresa, importación de otra versión)
    /// impide importarlo, y apuntes en una antigua cuenta del IVA al 16 % (<see cref="IvaCeroContaSol"/>) impiden importar todos.
    /// </summary>
    public Task<IReadOnlyList<ResumenEjercicio>> ImportarAsync(IReadOnlyList<PlanEjercicio> planes, Actor actor, IProgress<ProgresoImportacion>? progreso = null,
        CancellationToken cancelacion = default) =>
        // Una conexión abierta durante toda la importación: sin ella, cada transacción (una por asiento) abre el fichero
        // en frío, y ese coste crece con el tamaño de la BD (2025 tardaba 34 minutos). La conexión no hace nada más.
        _bdDestino.EjecutarAsync(_ => ImportarTodoAsync(planes, actor, progreso, cancelacion), cancelacion);

    private async Task<IReadOnlyList<ResumenEjercicio>> ImportarTodoAsync(IReadOnlyList<PlanEjercicio> planes, Actor actor, IProgress<ProgresoImportacion>? progreso,
        CancellationToken cancelacion)
    {
        var resumenes = new List<ResumenEjercicio>();
        // Apuntes en una antigua cuenta del IVA al 16 % (4720000/4770000) en cualquier ejercicio: no se importa ninguno (2026-11-13 20:55).
        var parada = planes.SelectMany(p => p.Incidencias).Where(i => i.Codigo == IvaCeroContaSol.CodigoIva16ConApuntes).ToList();
        if (parada.Count > 0)
        {
            foreach (var plan in planes.OrderBy(p => p.Empresa.Codigo, StringComparer.Ordinal).ThenBy(p => p.Ejercicio))
            {
                resumenes.Add(new ResumenEjercicio(plan.Ejercicio, plan.Origen.Ruta, 0, EstadosImportacion.Fallida, 0, 0, 0, 0, 0, 0, 0, 0, plan.Incidencias));
                progreso?.Report(new ProgresoImportacion(plan.Ejercicio, "fin", 1, 1, $"{plan.Ejercicio}: no se importa. {IvaCeroContaSol.MotivoParada(parada)}"));
            }
            return resumenes;
        }
        foreach (var plan in planes.OrderBy(p => p.Empresa.Codigo, StringComparer.Ordinal).ThenBy(p => p.Ejercicio))
        {
            if (plan.Incidencias.Any(i => i.Severidad == Severidad.Bloqueante && i.Fase == "destino"))
            {
                resumenes.Add(new ResumenEjercicio(plan.Ejercicio, plan.Origen.Ruta, 0, EstadosImportacion.Fallida, 0, 0, 0, 0, 0, 0, 0, 0, plan.Incidencias));
                progreso?.Report(new ProgresoImportacion(plan.Ejercicio, "fin", 1, 1, $"{plan.Ejercicio}: no se importa. " +
                    string.Join(" ", plan.Incidencias.Where(i => i.Severidad == Severidad.Bloqueante && i.Fase == "destino").Select(i => i.Explicacion))));
                continue;
            }
            var resumen = await new Ejecucion(this, plan, actor, progreso, cancelacion).ImportarAsync();
            resumenes.Add(resumen);
            progreso?.Report(new ProgresoImportacion(plan.Ejercicio, "fin", 1, 1, $"{resumen.Estado} · {resumen.Texto}"));
            if (cancelacion.IsCancellationRequested) break;
        }
        return resumenes;
    }

    /// <summary>Una ejecución: la importación de un ejercicio.</summary>
    private sealed class Ejecucion(ImportadorContaSol imp, PlanEjercicio plan, Actor actor, IProgress<ProgresoImportacion>? progreso, CancellationToken cancelacion)
    {
        private readonly IBaseDatos _bd = imp._bdDestino;
        private readonly List<Incidencia> _incidencias = [.. plan.Incidencias];
        private readonly Dictionary<(string Tipo, string Clave), int> _anteriores = [];
        private readonly Dictionary<(string Tipo, string Clave), int> _mapas = [];
        private readonly List<(string Tipo, string Clave, int Id)> _mapasPendientes = [];
        private ContextoSociedadEjercicio _ctx = null!;
        private int _idImportacion;
        private int _grabados, _sinCambios, _borrados, _terceros;

        private string Sociedad => plan.Empresa.Codigo;
        private DateTime Ahora => imp._reloj.Ahora;

        private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

        public async Task<ResumenEjercicio> ImportarAsync()
        {
            var estado = EstadosImportacion.Fallida;
            try
            {
                await PrepararAsync();
                await FaseAsync("centros", plan.Centros.Count, ImportarCentrosAsync);
                await FaseAsync("cuentas", plan.Cuentas.Count, ImportarCuentasAsync);
                await FaseAsync("terceros", plan.Terceros.Count, ImportarTercerosAsync);
                await FaseAsync("asientos", plan.Asientos.Count, ImportarAsientosAsync);
                await FaseAsync("iva", plan.Registros.Count, ImportarIvaAsync);
                await FaseAsync("conciliacion", plan.Asientos.Count, ConciliarAsync);
                estado = _incidencias.Any(i => i.Severidad == Severidad.Bloqueante) ? EstadosImportacion.ConIncidencias : EstadosImportacion.Completada;
            }
            catch (OperationCanceledException)
            {
                estado = EstadosImportacion.Cancelada;
                Anotar("importacion", null, null, null, "ContaSol.Cancelada", Severidad.Advertencia,
                    "Has cancelado la importación: lo grabado hasta ahora se conserva y la siguiente importación de este ejercicio sigue desde ahí sin duplicar.");
            }
            catch (Exception ex)
            {
                Anotar("importacion", null, null, null, "ContaSol.Fallo", Severidad.Bloqueante, _idImportacion == 0
                    ? $"No he podido preparar la importación de este ejercicio (empresa, ejercicio o registro de la importación): {ex.Message}. No se ha grabado nada."
                    : $"La importación se ha detenido por un error: {ex.Message}. Lo grabado hasta ahora se conserva; corrige la causa y vuelve a importar este ejercicio, que sigue sin duplicar.");
            }
            var resumen = new ResumenEjercicio(plan.Ejercicio, plan.Origen.Ruta, _idImportacion, estado, plan.Centros.Count, plan.Cuentas.Count, _grabados, _sinCambios, _borrados,
                plan.Apuntes, plan.Registros.Count, _terceros, _incidencias);
            if (_idImportacion != 0) await CerrarAsync(resumen);
            return resumen;
        }

        private async Task FaseAsync(string codigo, int total, Func<Task<(int Creadas, int Omitidas)>> trabajo)
        {
            cancelacion.ThrowIfCancellationRequested();
            Avanzar(codigo, 0, total);
            var inicio = Ahora;
            var (creadas, omitidas) = await trabajo();
            await GrabarMapasAsync();
            await _bd.EjecutarAsync(s => s.EjecutarAsync(
                "INSERT INTO FasesImportacionContaSol (IdImportacionContaSol, CodigoFase, Estado, FechaHoraInicio, FechaHoraFin, FilasLeidas, FilasCreadas, FilasOmitidas) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                _idImportacion, codigo, _incidencias.Any(i => i.Fase == codigo && i.Severidad == Severidad.Bloqueante) ? EstadosImportacion.ConIncidencias : EstadosImportacion.Completada,
                inicio, Ahora, total, creadas, omitidas));
        }

        private void Avanzar(string fase, int hechos, int total) =>
            progreso?.Report(new ProgresoImportacion(plan.Ejercicio, fase, hechos, total, $"{plan.Ejercicio} · {fase}: {hechos} de {total}"));

        private void Anotar(string fase, string? tabla, string? clave, string? campo, string codigo, Severidad severidad, string texto, string? valor = null) =>
            _incidencias.Add(new Incidencia(fase, tabla ?? "", clave, campo, codigo, severidad, texto, valor));

        private void Mapear(string tipo, string clave, int id)
        {
            _mapas[(tipo, clave)] = id;
            _mapasPendientes.Add((tipo, clave, id));
        }

        /// <summary>Los mapas pendientes, en bloque (carga masiva por tandas).</summary>
        private async Task GrabarMapasAsync()
        {
            if (_mapasPendientes.Count == 0) return;
            using (var carga = new CargaMasivaAccess(_bd.Ruta))
                await carga.InsertarAsync("MapasIdentidadContaSol", ["IdImportacionContaSol", "TipoEntidad", "ClaveOrigen", "IdDestino"],
                    _mapasPendientes.Select(m => new object?[] { _idImportacion, m.Tipo, m.Clave, m.Id }));
            _mapasPendientes.Clear();
        }

        // ---------- preparación: empresa, ejercicio, ejecución y mapas anteriores ----------

        private async Task PrepararAsync()
        {
            var empresa = plan.Empresa;
            var anio = empresa.Ejercicio.ToString(CultureInfo.InvariantCulture);
            var (idEjercicio, idImportacion) = await _bd.EnTransaccionAsync(async s =>
            {
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Sociedades WHERE IdSociedad = ?", Sociedad), CultureInfo.InvariantCulture) == 0)
                {
                    await s.EjecutarAsync("INSERT INTO Sociedades (IdSociedad, Nombre, CIF) VALUES (?, ?, ?)", Sociedad,
                        PlanificadorContaSol.Recortar(empresa.Nombre, 255) is { Length: > 0 } nombre ? nombre : Sociedad, Vacio(PlanificadorContaSol.Recortar(empresa.Nif, 10)));
                    Anotar("preparacion", "F_EMP", Sociedad, null, "ContaSol.EmpresaCreada", Severidad.Informativa, $"He dado de alta la empresa {Sociedad} ({empresa.Nombre}).");
                }
                var id = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?", Sociedad, anio);
                if (id is null or DBNull)
                {
                    await s.EjecutarAsync("INSERT INTO SociedadesEjercicios (IdSociedad, Ejercicio, Nombre, CIF, Activa, EjercicioCerrado) VALUES (?, ?, ?, ?, ?, ?)",
                        Sociedad, anio, Vacio(PlanificadorContaSol.Recortar(empresa.Nombre, 255)), Vacio(PlanificadorContaSol.Recortar(empresa.Nif, 10)), true, false);
                    id = await s.UltimoIdAsync();
                    Anotar("preparacion", "F_EMP", Sociedad, "EJEEMP", "ContaSol.EjercicioCreado", Severidad.Informativa, $"He dado de alta el ejercicio {anio} de la empresa {Sociedad}.");
                }
                var idEj = Convert.ToInt32(id, CultureInfo.InvariantCulture);
                await s.EjecutarAsync(
                    "INSERT INTO ImportacionesContaSol (IdSociedad, IdSociedadEjercicio, RutaOrigen, HuellaOrigen, VersionImportador, FechaHoraInicio, Operador, Estado) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    Sociedad, idEj, PlanificadorContaSol.Recortar(plan.Origen.Ruta, 255), plan.Origen.Huella[..Math.Min(64, plan.Origen.Huella.Length)], VersionImportador, Ahora,
                    PlanificadorContaSol.Recortar(actor.ToString(), 100), EstadosImportacion.EnCurso);
                return (idEj, await s.UltimoIdAsync());
            });
            _ctx = new ContextoSociedadEjercicio(Sociedad, idEjercicio, empresa.Ejercicio);
            _idImportacion = idImportacion;
            // Mapas de las ejecuciones anteriores de este ejercicio (con esta versión): el último gana.
            var filas = await _bd.EjecutarAsync(s => s.ConsultarAsync(
                "SELECT m.TipoEntidad, m.ClaveOrigen, m.IdDestino FROM MapasIdentidadContaSol AS m INNER JOIN ImportacionesContaSol AS i ON m.IdImportacionContaSol = i.IdImportacionContaSol " +
                "WHERE i.IdSociedadEjercicio = ? AND i.VersionImportador = ? AND i.IdImportacionContaSol <> ? ORDER BY m.IdMapaIdentidadContaSol",
                f => (Tipo: f.Texto("TipoEntidad") ?? "", Clave: f.Texto("ClaveOrigen") ?? "", Id: f.Entero("IdDestino")), idEjercicio, VersionImportador, idImportacion));
            foreach (var f in filas) _anteriores[(f.Tipo, f.Clave)] = f.Id;
        }

        private async Task CerrarAsync(ResumenEjercicio resumen)
        {
            await GrabarMapasAsync();
            foreach (var trozo in _incidencias.Chunk(TamanoLote))
                await _bd.EnTransaccionAsync(async s =>
                {
                    foreach (var i in trozo)
                        await s.EjecutarAsync(
                            "INSERT INTO IncidenciasImportacionContaSol (IdImportacionContaSol, Fase, TablaOrigen, ClaveOrigen, CampoOrigen, Codigo, Severidad, Explicacion, ValorOriginal, Estado) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                            _idImportacion, PlanificadorContaSol.Recortar(i.Fase, 50), PlanificadorContaSol.Recortar(i.Tabla, 100), PlanificadorContaSol.Recortar(i.Clave, 150),
                            PlanificadorContaSol.Recortar(i.Campo, 100), PlanificadorContaSol.Recortar(i.Codigo, 50), i.Severidad.ToString().ToUpperInvariant(), i.Explicacion, i.ValorOriginal,
                            "PENDIENTE");
                    return true;
                });
            await _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE ImportacionesContaSol SET FechaHoraFin = ?, Estado = ?, Resumen = ? WHERE IdImportacionContaSol = ?",
                Ahora, resumen.Estado, $"{Sociedad} · {resumen.Texto}", _idImportacion));
        }

        // ---------- centros de coste ----------

        private async Task<(int, int)> ImportarCentrosAsync()
        {
            var existentes = await imp._centros.ListarAsync(Sociedad);
            var porRuta = existentes.ToDictionary(c => c.RutaJerarquica, c => c.IdCentroCoste, StringComparer.Ordinal);
            var porId = existentes.Select(c => c.IdCentroCoste).ToHashSet();
            int creados = 0, hechos = 0;
            foreach (var c in plan.Centros.OrderBy(c => c.ClavePadre is null ? 0 : 1).ThenBy(c => c.Clave, StringComparer.Ordinal))
            {
                Avanzar("centros", ++hechos, plan.Centros.Count);
                if (_anteriores.TryGetValue((TiposMapa.Centro, c.Clave), out var previo) && porId.Contains(previo)) { Mapear(TiposMapa.Centro, c.Clave, previo); continue; }
                if (porRuta.TryGetValue(c.Clave, out var id)) { Mapear(TiposMapa.Centro, c.Clave, id); continue; }
                int? padre = c.ClavePadre is null ? null : _mapas.GetValueOrDefault((TiposMapa.Centro, c.ClavePadre));
                var alta = await imp._centros.AltaAsync(Sociedad, c.Codigo, c.Nombre, padre);
                if (!alta.EsCorrecto)
                {
                    Anotar("centros", "F_CDP", c.Clave, null, "ContaSol.CentroNoCreado", Severidad.Bloqueante, $"No he podido crear el centro de coste {c.Clave}: {alta.Error!.TextoOperador()}");
                    continue;
                }
                if (c.Descripcion is { Length: > 0 } d)
                    await _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CentrosCoste SET Descripcion = ? WHERE IdCentroCoste = ?", d, alta.Valor.IdCentroCoste));
                porRuta[alta.Valor.RutaJerarquica] = alta.Valor.IdCentroCoste;
                Mapear(TiposMapa.Centro, c.Clave, alta.Valor.IdCentroCoste);
                creados++;
            }
            return (creados, 0);
        }

        private int? IdCentro(string? clave) => clave is null ? null : _mapas.TryGetValue((TiposMapa.Centro, clave), out var id) ? id : null;

        // ---------- plan contable ----------

        /// <summary>
        /// Cuentas del ejercicio. Mismo código en otro ejercicio = misma identidad (reserva histórica de códigos,
        /// contabilidad/cuenta-contable); un código nuevo crea identidad, cuenta y reserva, como el alta del catálogo. La cuenta
        /// existente se actualiza con el nombre, la descripción, el centro y la jerarquía de ContaSol. Se escribe por lotes en
        /// una transacción cada uno, porque son miles de cuentas por ejercicio.
        /// </summary>
        private async Task<(int, int)> ImportarCuentasAsync()
        {
            var existentes = await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdCuentaContable, Codigo FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                f => (Id: f.Entero("IdCuentaContable"), Codigo: (f.Texto("Codigo") ?? "").Trim()), _ctx.IdSociedadEjercicio));
            var porCodigo = existentes.GroupBy(c => c.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
            var reservas = await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT Codigo, IdIdentidadContable FROM CodigosContablesReservados WHERE IdSociedad = ?",
                f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Id: f.Entero("IdIdentidadContable")), Sociedad));
            var identidades = reservas.GroupBy(r => r.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
            var ordenadas = plan.Cuentas.OrderBy(c => c.Codigo.Length).ThenBy(c => c.Codigo, StringComparer.Ordinal).ToList();
            int creadas = 0, hechos = 0;
            foreach (var trozo in ordenadas.Chunk(TamanoLote))
            {
                cancelacion.ThrowIfCancellationRequested();
                await _bd.EnTransaccionAsync(async s =>
                {
                    foreach (var c in trozo)
                    {
                        var padre = Padre(c.Codigo, porCodigo);
                        var centro = IdCentro(c.ClaveCentro);
                        if (porCodigo.TryGetValue(c.Codigo, out var id))
                        {
                            await s.EjecutarAsync("UPDATE CuentasContables SET Nombre = ?, Descripcion = ?, EsImputable = ?, IdCuentaPadre = ?, IdCentroCostePredeterminado = ? WHERE IdCuentaContable = ?",
                                c.Nombre, c.Descripcion, c.EsImputable, padre, centro, id);
                        }
                        else
                        {
                            if (!identidades.TryGetValue(c.Codigo, out var identidad))
                            {
                                await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, IdTipoCuentaContable, Naturaleza, FechaAlta) VALUES (?, ?, ?, ?)",
                                    Sociedad, null, (byte)0, Ahora.Date);
                                identidad = await s.UltimoIdAsync();
                                await s.EjecutarAsync("INSERT INTO CodigosContablesReservados (IdSociedad, Codigo, IdIdentidadContable, FechaPrimeraAsignacion) VALUES (?, ?, ?, ?)",
                                    Sociedad, c.Codigo, identidad, Ahora.Date);
                                identidades[c.Codigo] = identidad;
                            }
                            await s.EjecutarAsync(
                                "INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, IdCuentaPadre, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, IdCentroCostePredeterminado, SaldoInicial, SaldoActual, NumeroApuntes, Descripcion) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                                identidad, _ctx.IdSociedadEjercicio, padre, c.Codigo, c.Nombre, true, false, c.EsImputable, centro, 0m, 0m, 0, c.Descripcion);
                            id = await s.UltimoIdAsync();
                            porCodigo[c.Codigo] = id;
                            creadas++;
                        }
                        Mapear(TiposMapa.Cuenta, c.Codigo, id);
                    }
                    return true;
                }, cancelacion);
                hechos += trozo.Length;
                Avanzar("cuentas", hechos, ordenadas.Count);
            }
            return (creadas, 0);
        }

        /// <summary>Cuenta padre: la del prefijo más largo del código que exista en el ejercicio.</summary>
        private static int? Padre(string codigo, IReadOnlyDictionary<string, int> porCodigo)
        {
            for (var n = codigo.Length - 1; n >= 1; n--)
                if (porCodigo.TryGetValue(codigo[..n], out var id)) return id;
            return null;
        }

        // ---------- terceros ----------

        private async Task<(int, int)> ImportarTercerosAsync()
        {
            if (plan.Terceros.Count == 0) return (0, 0);
            // Terceros ya importados de ContaSol en cualquier ejercicio de la sociedad, por código: (ejercicio, IdTercero, NIF).
            // (Access no admite condiciones constantes ni expresiones en un LEFT JOIN: se cruzan en memoria.)
            var mapasTerceros = await _bd.EjecutarAsync(s => s.ConsultarAsync(
                "SELECT m.TipoEntidad, m.ClaveOrigen, m.IdDestino, se.Ejercicio FROM (MapasIdentidadContaSol AS m INNER JOIN ImportacionesContaSol AS i ON m.IdImportacionContaSol = i.IdImportacionContaSol) " +
                "INNER JOIN SociedadesEjercicios AS se ON i.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                "WHERE se.IdSociedad = ? AND i.VersionImportador = ? AND m.TipoEntidad IN ('CLIENTE', 'PROVEEDOR')",
                f => (Tipo: f.Texto("TipoEntidad") ?? "", Codigo: f.Texto("ClaveOrigen") ?? "", Id: f.Entero("IdDestino"),
                    Ejercicio: int.TryParse(f.Texto("Ejercicio"), out var e) ? e : 0),
                Sociedad, VersionImportador));
            var terceroDeCliente = (await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdCliente, IdTercero FROM Clientes", f => (Id: f.Entero("IdCliente"), Tercero: f.Entero("IdTercero")))))
                .ToDictionary(x => x.Id, x => x.Tercero);
            var terceroDeProveedor = (await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdProveedor, IdTercero FROM Proveedores", f => (Id: f.Entero("IdProveedor"), Tercero: f.Entero("IdTercero")))))
                .ToDictionary(x => x.Id, x => x.Tercero);
            var nifDeTercero = (await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdTercero, Nif FROM Terceros", f => (Id: f.Entero("IdTercero"), Nif: PlanificadorContaSol.NormalizarNif(f.Texto("Nif"))))))
                .ToDictionary(x => x.Id, x => x.Nif);
            var historico = mapasTerceros.Select(m =>
            {
                int? idTercero = (m.Tipo == TiposMapa.Cliente ? terceroDeCliente : terceroDeProveedor).TryGetValue(m.Id, out var t) ? t : null;
                return (m.Tipo, m.Codigo, m.Ejercicio, IdTercero: idTercero, Nif: idTercero is int x && nifDeTercero.TryGetValue(x, out var n) ? n : "");
            }).ToList();
            var porNif = (await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdTercero, Nif FROM Terceros WHERE Nif IS NOT NULL",
                    f => (Id: f.Entero("IdTercero"), Nif: PlanificadorContaSol.NormalizarNif(f.Texto("Nif"))))))
                .Where(t => t.Nif.Length > 0).GroupBy(t => t.Nif).ToDictionary(g => g.Key, g => g.Min(t => t.Id), StringComparer.Ordinal);
            var identidades = (await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT Codigo, IdIdentidadContable FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                    f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Id: f.Entero("IdIdentidadContable")), _ctx.IdSociedadEjercicio)))
                .GroupBy(c => c.Codigo).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);

            int creados = 0, hechos = 0;
            foreach (var t in plan.Terceros)
            {
                cancelacion.ThrowIfCancellationRequested();
                Avanzar("terceros", ++hechos, plan.Terceros.Count);
                var tipo = t.Origen.EsCliente ? TiposMapa.Cliente : TiposMapa.Proveedor;
                var codigo = t.Origen.Codigo;
                // 1) El mismo código ya importado con el mismo titular; 2) un tercero con el mismo NIF; 3) uno nuevo.
                var mismo = historico.Where(h => h.Tipo == tipo && h.Codigo == codigo && h.IdTercero is not null && PlanificadorContaSol.MismoTitular(h.Nif, t.Nif))
                    .OrderByDescending(h => h.Ejercicio).Select(h => h.IdTercero).FirstOrDefault();
                var idTercero = mismo ?? (t.Nif.Length > 0 && porNif.TryGetValue(t.Nif, out var porElNif) ? porElNif : (int?)null);
                if (idTercero is null)
                {
                    var alta = await imp._terceros.GuardarAsync(NuevoTercero(t), actor);
                    if (!alta.EsCorrecto)
                    {
                        Anotar("terceros", t.Origen.EsCliente ? "F_CLI" : "F_PRO", codigo, null, "ContaSol.TerceroNoCreado", Severidad.Advertencia,
                            $"No he podido dar de alta el {(t.Origen.EsCliente ? "cliente" : "proveedor")} {codigo} {t.Origen.Nombre}: {alta.Error!.TextoOperador()}");
                        continue;
                    }
                    idTercero = alta.Valor.IdTercero;
                    if (t.Nif.Length > 0) porNif[t.Nif] = idTercero.Value;
                    creados++;
                }
                var rol = t.Origen.EsCliente ? await imp._roles.AsegurarClienteAsync(idTercero.Value) : await imp._roles.AsegurarProveedorAsync(idTercero.Value);
                if (!rol.EsCorrecto)
                {
                    Anotar("terceros", t.Origen.EsCliente ? "F_CLI" : "F_PRO", codigo, null, "ContaSol.RolNoCreado", Severidad.Advertencia, rol.Error!.TextoOperador());
                    continue;
                }
                Mapear(tipo, codigo, rol.Valor);
                _terceros++;
                // La cuenta va al titular más reciente: solo si ningún ejercicio posterior ya importado usa el código con otro titular.
                var posterior = historico.Any(h => h.Tipo == tipo && h.Codigo == codigo && h.Ejercicio > plan.Ejercicio && h.IdTercero != idTercero);
                if (posterior || !identidades.TryGetValue(codigo, out var identidad)) continue;
                var asignada = await _bd.EnTransaccionAsync(async s =>
                {
                    var (tabla, campo) = ServicioRoles.EsDeCliente(t.Clase) ? ("CuentasClientesSociedad", "IdCliente") : ("CuentasProveedoresSociedad", "IdProveedor");
                    var clase = t.Clase switch { ClaseCuentaRol.Cliente => "CLIENTE", ClaseCuentaRol.Proveedor => "PROVEEDOR", _ => "ACREEDOR" };
                    // El titular anterior de la subcuenta deja de tenerla (decisión del propietario, 2026-11-10 18:15).
                    var quitadas = await s.EjecutarAsync($"DELETE FROM {tabla} WHERE IdSociedad = ? AND Clase = ? AND IdIdentidadContable = ? AND {campo} <> ?",
                        Sociedad, clase, identidad, rol.Valor);
                    if (quitadas > 0)
                        Anotar("terceros", t.Origen.EsCliente ? "F_CLI" : "F_PRO", codigo, null, "ContaSol.CuentaReasignada", Severidad.Informativa,
                            $"La subcuenta {codigo} ha cambiado de titular: ahora es de {t.Origen.Nombre} y el titular anterior conserva su ficha sin esta cuenta.");
                    if (await s.EjecutarAsync($"UPDATE {tabla} SET IdIdentidadContable = ? WHERE {campo} = ? AND IdSociedad = ? AND Clase = ?", identidad, rol.Valor, Sociedad, clase) == 0)
                        await s.EjecutarAsync($"INSERT INTO {tabla} ({campo}, IdSociedad, Clase, IdIdentidadContable) VALUES (?, ?, ?, ?)", rol.Valor, Sociedad, clase, identidad);
                    return true;
                });
                _ = asignada;
            }
            return (creados, plan.Terceros.Count - _terceros);
        }

        private static Tercero NuevoTercero(TerceroPlan t)
        {
            var o = t.Origen;
            var nif = t.Nif.Length > 0 ? t.Nif : null;
            var extranjero = nif is { Length: > 2 } && char.IsAsciiLetter(nif[0]) && char.IsAsciiLetter(nif[1]) && !Nif.EsValido(nif);
            var juridica = nif is { Length: > 0 } && char.IsAsciiLetter(nif[0]) && !"XYZKLM".Contains(nif[0]) && !extranjero;
            var nombre = PlanificadorContaSol.Recortar(o.Nombre.Length > 0 ? o.Nombre : o.Codigo, 200)!;
            var tercero = new Tercero
            {
                TipoPersona = juridica || extranjero ? TipoPersona.Juridica : TipoPersona.Fisica,
                NombrePersona = juridica || extranjero ? null : nombre,
                RazonSocial = juridica || extranjero ? nombre : null,
                Nombre = nombre,
                NombreComercial = PlanificadorContaSol.Recortar(o.NombreComercial, 200),
                Nif = nif,
                // Un NIF que no pasa la validación española se guarda como otra identificación, sin perderlo.
                TipoIdentificacion = nif is null || Nif.EsValido(nif) ? null : extranjero ? "VAT" : "OTRO",
                Pais = extranjero ? nif![..2] : null,
                Direccion = PlanificadorContaSol.Recortar(o.Direccion, 255),
                CodigoPostal = PlanificadorContaSol.Recortar(o.CodigoPostal, 10),
                Poblacion = PlanificadorContaSol.Recortar(o.Poblacion, 100),
                Provincia = PlanificadorContaSol.Recortar(o.Provincia, 100),
                Telefono = PlanificadorContaSol.Recortar(o.Telefono, 50),
                Email = o.Email is { Length: <= 254 } e && e.Contains('@') ? e : null,
                Notas = $"Importado de ContaSol: {(o.EsCliente ? "cliente" : "proveedor")} {o.Codigo}.",
            };
            if (o.EsCliente) tercero.Roles.Cliente = new RolCliente { Activo = true };
            else tercero.Roles.Proveedor = new RolProveedor { Activo = true };
            return tercero;
        }

        // ---------- asientos ----------

        /// <summary>
        /// Asientos del ejercicio en carga masiva (implantacion/importador-contasol 2026-11-11 13:00): el motor valida en memoria y
        /// graba con INSERT … SELECT; los asientos sin cambios se conservan, los cambiados se sustituyen y los que ya no están en
        /// ContaSol se borran. Un asiento rechazado no se graba (bloqueante) y, si ya estaba importado, se conserva el anterior.
        /// </summary>
        private async Task<(int, int)> ImportarAsientosAsync()
        {
            var motivo = $"Importación de ContaSol {plan.Ejercicio} ({Path.GetFileName(plan.Origen.Ruta)})";
            var origen = plan.Asientos.ToDictionary(p => Clave(p.Clave), StringComparer.Ordinal);
            Avanzar("asientos", 0, plan.Asientos.Count);
            var cargado = await imp._motor.CargarLibroExternoAsync(_ctx, OpcionesLote.MotorImportadorContaSol, CodigoProceso,
                [.. plan.Asientos.Select(p => new AsientoExterno(Clave(p.Clave), Construir(p)))], actor, motivo, DesligarIvaAsync, cancelacion);
            if (!cargado.EsCorrecto)
            {
                Anotar("asientos", "F_APU", null, null, "ContaSol.AsientosNoGrabados", Severidad.Bloqueante,
                    $"No he podido grabar los asientos de {plan.Ejercicio}: {string.Join(" ", cargado.Errores.Select(e => e.TextoOperador()))}");
                return (0, plan.Asientos.Count);
            }
            var r = cargado.Valor;
            foreach (var (clave, id) in r.Ids) Mapear(TiposMapa.Asiento, origen[clave].Clave, id);
            foreach (var (clave, errores) in r.Rechazados)
            {
                var p = origen[clave];
                Anotar("asientos", "F_APU", p.Clave, null, "ContaSol.AsientoNoGrabado", Severidad.Bloqueante,
                    $"No he podido grabar el asiento {p.Numero} del {p.Fecha:dd/MM/yyyy}: {string.Join(" ", errores.Select(e => e.TextoOperador()))}");
            }
            foreach (var (clave, id) in r.Borrados)
                Anotar("asientos", "F_APU", clave, null, "ContaSol.AsientoBorrado", Severidad.Informativa,
                    $"He borrado el asiento {id} ({clave}), importado antes, porque ya no está en ContaSol.");
            _grabados = r.Grabados;
            _sinCambios = r.SinCambios;
            _borrados = r.Borrados.Count;
            Avanzar("asientos", plan.Asientos.Count, plan.Asientos.Count);
            return (_grabados, r.Rechazados.Count);
        }

        /// <summary>Los registros de IVA dejan de apuntar a los asientos que se van a borrar; la fase de IVA los vuelve a enlazar.</summary>
        private static async Task DesligarIvaAsync(ISesionDatos s, IReadOnlyList<int> ids)
        {
            if (ids.Count == 0) return;
            var lista = string.Join(", ", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            await s.EjecutarAsync($"UPDATE IVASoportado SET IdAsiento = NULL WHERE IdAsiento IN ({lista})");
            await s.EjecutarAsync($"UPDATE IVARepercutido SET IdAsiento = NULL WHERE IdAsiento IN ({lista})");
        }

        /// <summary>Clave de ejecución del asiento: «empresa/ejercicio/DIA:ASI».</summary>
        private string Clave(string clave) => $"{Sociedad}/{plan.Ejercicio}/{clave}";

        private Asiento Construir(AsientoPlan p)
        {
            var a = new Asiento(_ctx, p.Fecha, p.Descripcion) { Tipo = p.Tipo, Notas = p.Notas };
            foreach (var x in p.Apuntes)
            {
                var apunte = x.Debe > 0m ? Apunte.AlDebe(x.Cuenta, x.Debe, x.Descripcion) : Apunte.AlHaber(x.Cuenta, x.Haber, x.Descripcion);
                apunte.NumeroDocumento = x.Documento;
                apunte.IdCentroCoste = IdCentro(x.ClaveCentro);
                a.Apuntes.Add(apunte);
            }
            return a;
        }

        // ---------- libros de IVA ----------

        private async Task<(int, int)> ImportarIvaAsync()
        {
            var tipos = await _bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdTipoOperacion, Libro, CodigoContaSol FROM TiposOperacion",
                f => (Id: f.Entero("IdTipoOperacion"), Libro: (f.Texto("Libro") ?? "").Trim(), Codigo: f.EnteroNulo("CodigoContaSol"))));
            var periodos = new Dictionary<int, int>();
            int creados = 0, hechos = 0;
            var vigentes = new HashSet<(string, string)>();
            foreach (var trozo in plan.Registros.Chunk(TamanoLote / 3))
            {
                cancelacion.ThrowIfCancellationRequested();
                await _bd.EnTransaccionAsync(async s =>
                {
                    foreach (var r in trozo)
                    {
                        var tipoMapa = r.Soportado ? TiposMapa.IvaSoportado : TiposMapa.IvaRepercutido;
                        vigentes.Add((tipoMapa, r.Clave));
                        var libro = r.Soportado ? "Soportado" : "Repercutido";
                        var tipo = tipos.FirstOrDefault(t => t.Libro.Equals(libro, StringComparison.OrdinalIgnoreCase) && t.Codigo == r.TipoOperacionContaSol);
                        if (tipo.Id == 0)
                        {
                            tipo = tipos.FirstOrDefault(t => t.Libro.Equals(libro, StringComparison.OrdinalIgnoreCase) && t.Codigo == 0);
                            Anotar("iva", r.Soportado ? "F_IVS" : "F_IVR", r.Clave, "TIP", "ContaSol.TipoOperacionDesconocido", Severidad.Advertencia,
                                $"El tipo de operación {r.TipoOperacionContaSol} del registro de IVA {r.Clave} no está en el catálogo: lo importo como operación interior.");
                        }
                        if (!periodos.TryGetValue(r.Trimestre, out var periodo))
                            periodos[r.Trimestre] = periodo = await PeriodoAsync(s, r.Trimestre);
                        int? idAsiento = r.ClaveAsiento is { } k && _mapas.TryGetValue((TiposMapa.Asiento, k), out var a) ? a : null;
                        int? idTercero = _mapas.TryGetValue((r.Soportado ? TiposMapa.Proveedor : TiposMapa.Cliente, r.CuentaTercero), out var t) ? t : null;
                        var previo = _anteriores.TryGetValue((tipoMapa, r.Clave), out var p) ? p : (int?)null;
                        var id = await GrabarRegistroAsync(s, r, previo, idAsiento, idTercero, periodo, tipo.Id);
                        if (previo != id) creados++;
                        Mapear(tipoMapa, r.Clave, id);
                    }
                    return true;
                }, cancelacion);
                hechos += trozo.Length;
                Avanzar("iva", hechos, plan.Registros.Count);
            }
            // Registros importados antes que ya no están en ContaSol.
            foreach (var ((tipo, clave), id) in _anteriores.Where(m => m.Key.Tipo is TiposMapa.IvaSoportado or TiposMapa.IvaRepercutido && !vigentes.Contains(m.Key)))
                await _bd.EnTransaccionAsync(async s =>
                {
                    var (cabecera, desglose, campo) = tipo == TiposMapa.IvaSoportado
                        ? ("IVASoportado", "DesglosesIVASoportado", "IdIVASoportado") : ("IVARepercutido", "DesglosesIVARepercutido", "IdIVARepercutido");
                    await s.EjecutarAsync($"DELETE FROM {desglose} WHERE {campo} = ?", id);
                    await s.EjecutarAsync($"DELETE FROM {cabecera} WHERE {campo} = ?", id);
                    Anotar("iva", tipo == TiposMapa.IvaSoportado ? "F_IVS" : "F_IVR", clave, null, "ContaSol.IvaBorrado", Severidad.Informativa,
                        $"He borrado el registro de IVA {clave}, importado antes, porque ya no está en ContaSol.");
                    return true;
                });
            return (creados, 0);
        }

        private async Task<int> PeriodoAsync(ISesionDatos s, int trimestre)
        {
            var id = await s.EscalarAsync("SELECT IdPeriodoIVA FROM PeriodosIVA WHERE IdSociedadEjercicio = ? AND Numero = ?", _ctx.IdSociedadEjercicio, (byte)trimestre);
            if (id is not null and not DBNull) return Convert.ToInt32(id, CultureInfo.InvariantCulture);
            var inicio = new DateTime(plan.Ejercicio, (trimestre - 1) * 3 + 1, 1);
            await s.EjecutarAsync("INSERT INTO PeriodosIVA (IdSociedadEjercicio, Numero, FechaInicio, FechaFin, Estado) VALUES (?, ?, ?, ?, ?)",
                _ctx.IdSociedadEjercicio, (byte)trimestre, inicio, inicio.AddMonths(3).AddDays(-1), "ABIERTO");
            return await s.UltimoIdAsync();
        }

        /// <summary>Graba el registro (actualiza el de la importación anterior, así su Id no cambia) y sus desgloses.</summary>
        private async Task<int> GrabarRegistroAsync(ISesionDatos s, RegistroIvaPlan r, int? previo, int? idAsiento, int? idTercero, int periodo, int idTipo)
        {
            var ahora = Ahora;
            var (cabecera, desglose, campo) = r.Soportado
                ? ("IVASoportado", "DesglosesIVASoportado", "IdIVASoportado") : ("IVARepercutido", "DesglosesIVARepercutido", "IdIVARepercutido");
            var existe = previo is int p && Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM {cabecera} WHERE {campo} = ?", p), CultureInfo.InvariantCulture) > 0;
            int id;
            if (r.Soportado)
            {
                object?[] valores = [_ctx.IdSociedadEjercicio, idTercero, idAsiento, r.FechaExpedicion, r.FechaOperacion, r.FechaRegistro, periodo, r.NumeroFactura, r.Nombre, r.Nif,
                    r.Pais, idTipo, null, false, r.Total, r.Base, r.Cuota, r.Autorrepercutida, r.Deducible, r.Incluido347, "REGISTRADO", r.Descripcion, actor.ToString(), ahora, false];
                if (existe)
                    await s.EjecutarAsync("UPDATE IVASoportado SET IdSociedadEjercicio = ?, IdProveedor = ?, IdAsiento = ?, FechaExpedicion = ?, FechaOperacion = ?, FechaHoraRegistroContable = ?, " +
                        "IdPeriodoIVA = ?, NumeroFactura = ?, NombreProveedor = ?, IdentificacionFiscalProveedor = ?, PaisProveedor = ?, IdTipoOperacion = ?, TipoBienServicio = ?, " +
                        "InversionSujetoPasivo = ?, ImporteTotalFactura = ?, BaseImponibleTotal = ?, CuotaSoportadaTotal = ?, CuotaDevengadaAutorrepercutidaTotal = ?, CuotaDeducibleTotal = ?, " +
                        "IncluidoModelo347 = ?, Estado = ?, Descripcion = ?, OperadorCreacion = ?, FechaHoraCreacion = ?, BienInversion = ? WHERE IdIVASoportado = ?", [.. valores, previo]);
                else
                    await s.EjecutarAsync("INSERT INTO IVASoportado (IdSociedadEjercicio, IdProveedor, IdAsiento, FechaExpedicion, FechaOperacion, FechaHoraRegistroContable, IdPeriodoIVA, " +
                        "NumeroFactura, NombreProveedor, IdentificacionFiscalProveedor, PaisProveedor, IdTipoOperacion, TipoBienServicio, InversionSujetoPasivo, ImporteTotalFactura, " +
                        "BaseImponibleTotal, CuotaSoportadaTotal, CuotaDevengadaAutorrepercutidaTotal, CuotaDeducibleTotal, IncluidoModelo347, Estado, Descripcion, OperadorCreacion, " +
                        "FechaHoraCreacion, BienInversion) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", valores);
            }
            else
            {
                object?[] valores = [_ctx.IdSociedadEjercicio, idTercero, idAsiento, r.FechaExpedicion, r.FechaOperacion, r.FechaRegistro, periodo, r.NumeroFactura, r.Nombre, r.Nif,
                    r.Pais, idTipo, r.Total, r.Base, r.Cuota, r.Recargo, r.Incluido347, "REGISTRADO", r.Descripcion, actor.ToString(), ahora];
                if (existe)
                    await s.EjecutarAsync("UPDATE IVARepercutido SET IdSociedadEjercicio = ?, IdCliente = ?, IdAsiento = ?, FechaExpedicion = ?, FechaOperacion = ?, FechaHoraRegistroContable = ?, " +
                        "IdPeriodoIVA = ?, NumeroFactura = ?, NombreCliente = ?, IdentificacionFiscalCliente = ?, PaisCliente = ?, IdTipoOperacion = ?, ImporteTotalFactura = ?, " +
                        "BaseImponibleTotal = ?, CuotaRepercutidaTotal = ?, RecargoEquivalenciaTotal = ?, IncluidoModelo347 = ?, Estado = ?, Descripcion = ?, OperadorCreacion = ?, " +
                        "FechaHoraCreacion = ? WHERE IdIVARepercutido = ?", [.. valores, previo]);
                else
                    await s.EjecutarAsync("INSERT INTO IVARepercutido (IdSociedadEjercicio, IdCliente, IdAsiento, FechaExpedicion, FechaOperacion, FechaHoraRegistroContable, IdPeriodoIVA, " +
                        "NumeroFactura, NombreCliente, IdentificacionFiscalCliente, PaisCliente, IdTipoOperacion, ImporteTotalFactura, BaseImponibleTotal, CuotaRepercutidaTotal, " +
                        "RecargoEquivalenciaTotal, IncluidoModelo347, Estado, Descripcion, OperadorCreacion, FechaHoraCreacion) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        valores);
            }
            id = existe ? previo!.Value : await s.UltimoIdAsync();
            await s.EjecutarAsync($"DELETE FROM {desglose} WHERE {campo} = ?", id);
            var orden = 1;
            foreach (var t in r.Tramos)
            {
                if (r.Soportado)
                    await s.EjecutarAsync("INSERT INTO DesglosesIVASoportado (IdIVASoportado, Orden, ClaveRegimen, CalificacionOperacion, CausaExencion, BaseImponible, TipoIVA, CuotaSoportada, " +
                        "CuotaDevengadaAutorrepercutida, PorcentajeDeducible, CuotaDeducible, IdCuentaIVASoportado, IdCuentaIVARepercutido) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        id, orden++, r.ClaveRegimen, t.Calificacion, null, t.Base, t.Tipo, t.Cuota, t.Autorrepercutida, t.PorcentajeDeducible, t.CuotaDeducible, null, null);
                else
                    await s.EjecutarAsync("INSERT INTO DesglosesIVARepercutido (IdIVARepercutido, Orden, ClaveRegimen, CalificacionOperacion, CausaExencion, BaseImponible, TipoIVA, CuotaRepercutida, " +
                        "TipoRecargoEquivalencia, CuotaRecargoEquivalencia, IdCuentaIVARepercutido) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        id, orden++, r.ClaveRegimen, t.Calificacion, null, t.Base, t.Tipo, t.Cuota, t.TipoRecargo, t.CuotaRecargo, null);
            }
            return id;
        }

        // ---------- conciliación ----------

        /// <summary>
        /// Lo grabado coincide con ContaSol: número de asientos del importador, cuadre de cada asiento y saldo de cada cuenta. Una
        /// diferencia es bloqueante. La comparación con F_SAL del propio ContaSol ya está en las incidencias de la inspección.
        /// </summary>
        private async Task<(int, int)> ConciliarAsync()
        {
            var esperado = PlanificadorContaSol.SaldosDe(plan.Asientos.Where(a => _mapas.ContainsKey((TiposMapa.Asiento, a.Clave))));
            var (grabado, asientos, descuadrados) = await _bd.EjecutarAsync(async s =>
            {
                var saldos = (await s.ConsultarAsync(
                        "SELECT ap.CuentaContable, SUM(IIF(ap.ImporteDebe IS NULL, 0, ap.ImporteDebe)) - SUM(IIF(ap.ImporteHaber IS NULL, 0, ap.ImporteHaber)) AS Saldo " +
                        "FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.MotorPropietario = ? GROUP BY ap.CuentaContable",
                        f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Saldo: f.DecimalNulo("Saldo") ?? 0m), _ctx.IdSociedadEjercicio, OpcionesLote.MotorImportadorContaSol))
                    .ToDictionary(x => x.Cuenta, x => x.Saldo, StringComparer.Ordinal);
                var n = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = ? AND MotorPropietario = ?",
                    _ctx.IdSociedadEjercicio, OpcionesLote.MotorImportadorContaSol), CultureInfo.InvariantCulture);
                var d = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = ? AND MotorPropietario = ? AND TotalDebe <> TotalHaber",
                    _ctx.IdSociedadEjercicio, OpcionesLote.MotorImportadorContaSol), CultureInfo.InvariantCulture);
                return (saldos, n, d);
            });
            var esperados = plan.Asientos.Count(a => _mapas.ContainsKey((TiposMapa.Asiento, a.Clave)));
            if (asientos != esperados)
                Anotar("conciliacion", "F_APU", null, null, "ContaSol.RecuentoAsientos", Severidad.Bloqueante,
                    $"En ProContaVF hay {asientos} asientos importados de ContaSol en {plan.Ejercicio} y deberían ser {esperados}. Vuelve a importar el ejercicio.");
            if (descuadrados > 0)
                Anotar("conciliacion", "F_APU", null, null, "ContaSol.AsientosDescuadrados", Severidad.Bloqueante, $"Hay {descuadrados} asientos importados que no cuadran.");
            var diferencias = PlanificadorContaSol.CompararSaldos(grabado, esperado).Where(d => d.Calculado != 0m || esperado.ContainsKey(d.Cuenta)).ToList();
            foreach (var d in diferencias)
                Anotar("conciliacion", "F_APU", d.Cuenta, null, "ContaSol.SaldoDistinto", Severidad.Bloqueante,
                    $"El saldo importado de la cuenta {d.Cuenta} es {d.Calculado:0.00} y en ContaSol es {d.Control:0.00}. Vuelve a importar el ejercicio.");
            return (0, diferencias.Count);
        }
    }
}
