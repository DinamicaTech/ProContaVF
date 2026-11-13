using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Facturacion.Conceptos;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Implantacion.ERP;

/// <summary>Opciones de una ejecución de la migración del ERP.</summary>
/// <param name="RaizDocumentos">Raíz de la que se quitan las rutas de los PDF de compras (producción: D:\SynologyBox).</param>
public sealed record OpcionesMigracionErp(string IdSociedad = "002", string RaizDocumentos = @"D:\SynologyBox");

/// <summary>Recuento de una tabla del ERP en una ejecución.</summary>
public sealed class EstadisticaTabla(string fase, string tabla)
{
    public string Fase { get; } = fase;
    public string Tabla { get; } = tabla;
    public int Leidas { get; set; }
    public int Cargadas { get; set; }
    public int Excluidas { get; set; }
}

/// <summary>Una tabla de controles del informe.</summary>
public sealed record SeccionControl(string Titulo, IReadOnlyList<string> Cabecera, IReadOnlyList<IReadOnlyList<string>> Filas, string? Nota = null);

/// <summary>Resultado de una ejecución de la migración del ERP.</summary>
public sealed class ResultadoMigracionErp
{
    public int IdMigracion { get; set; }
    public string Estado { get; set; } = EstadosImportacion.Fallida;
    public DateTime Inicio { get; set; }
    public DateTime Fin { get; set; }
    public List<EstadisticaTabla> Tablas { get; } = [];
    public List<Incidencia> Incidencias { get; } = [];
    public List<(string Fase, TimeSpan Duracion)> Tiempos { get; } = [];
    public List<SeccionControl> Controles { get; } = [];
    public Dictionary<string, IReadOnlyList<string>> CamposConstantes { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> Notas { get; } = [];
}

/// <summary>
/// Migración única del ERP (implantacion/migracion-erp): carga sobre una BD que ya tiene ContaSol importado los maestros, los
/// conceptos y las facturas de venta, las programadas, las compras, la Norma 43 y los activos del ERP, enlazados con lo importado
/// de ContaSol por sus mapas de identidad (asiento por «DIA:ASI»). Cada ejecución es una fila de MigracionesERP con sus incidencias
/// y mapas; repetirla en la misma BD sustituye lo cargado por la anterior, sin duplicar. El origen se lee en solo lectura.
/// </summary>
public sealed class MigradorErp(IBaseDatos bd, IRepositorioTerceros terceros, ServicioRoles roles, ICatalogoCuentas cuentas, CatalogoConceptos conceptos,
    IReloj? reloj = null)
{
    public const string VersionMigrador = "1";

    internal IBaseDatos Bd { get; } = bd;
    internal IRepositorioTerceros Terceros { get; } = terceros;
    internal ServicioRoles Roles { get; } = roles;
    internal ICatalogoCuentas Cuentas { get; } = cuentas;
    internal CatalogoConceptos Conceptos { get; } = conceptos;
    internal MapasContaSol MapasContaSol { get; } = new(bd);
    internal IReloj Reloj { get; } = reloj ?? RelojSistema.Instancia;

    /// <summary>Migra todo con una sola conexión abierta durante el proceso (sin ella cada sentencia abre el fichero en frío).</summary>
    public Task<ResultadoMigracionErp> MigrarAsync(OrigenErp origen, OpcionesMigracionErp opciones, Actor actor, IProgress<string>? progreso = null,
        CancellationToken cancelacion = default) =>
        Bd.EjecutarAsync(_ => new EjecucionErp(this, origen, opciones, actor, progreso, cancelacion).EjecutarAsync(), cancelacion);
}

/// <summary>Una ejecución de la migración. Las fases están en los ficheros EjecucionErp.*.cs.</summary>
internal sealed partial class EjecucionErp(MigradorErp m, OrigenErp o, OpcionesMigracionErp op, Actor actor, IProgress<string>? progreso, CancellationToken cancelacion)
{
    private const int TamanoLote = 150;

    private readonly IBaseDatos _bd = m.Bd;
    private readonly ResultadoMigracionErp _r = new();
    private readonly Dictionary<(string Tipo, string Clave), int> _anteriores = [];
    private readonly Dictionary<(string Tipo, string Clave), int> _mapas = [];
    private readonly List<(string Tipo, string Clave, int Id)> _pendientes = [];
    private readonly Dictionary<(int Anio, string Tipo), IReadOnlyDictionary<string, int>> _mapasCs = [];
    private readonly Dictionary<int, (int Id, string? Nombre)> _ejercicios = [];
    private readonly Dictionary<string, int> _centros = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _identidades = new(StringComparer.Ordinal);
    private int _idMigracion;

    private string Sociedad => op.IdSociedad;
    private DateTime Ahora => m.Reloj.Ahora;
    private string Operador => ReglasErp.Recortar(actor.ToString(), 30) ?? "MigradorERP";

    // Índices del origen.
    private Dictionary<int, Fila> _personas = [];
    private Dictionary<int, Fila> _agrupaciones = [];
    private Dictionary<int, Fila> _conceptosErp = [];

    public async Task<ResultadoMigracionErp> EjecutarAsync()
    {
        _r.Inicio = Ahora;
        try
        {
            await PrepararAsync();
            await FaseAsync("limpieza", LimpiarAnteriorAsync);
            await FaseAsync("1 maestros", MaestrosAsync);
            await FaseAsync("2 ventas", VentasAsync);
            await FaseAsync("3 programadas", ProgramadasAsync);
            await FaseAsync("4 compras", ComprasAsync);
            await FaseAsync("5 norma43", Norma43Async);
            await FaseAsync("6 activos", ActivosAsync);
            await FaseAsync("7 nominas", NominasAsync);
            await FaseAsync("controles", ControlesAsync);
            _r.Estado = _r.Incidencias.Any(i => i.Severidad == Severidad.Bloqueante) ? EstadosImportacion.ConIncidencias : EstadosImportacion.Completada;
        }
        catch (OperationCanceledException)
        {
            _r.Estado = EstadosImportacion.Cancelada;
            Anotar("migracion", null, null, null, "Erp.Cancelada", Severidad.Advertencia, "Has cancelado la migración: lo grabado se conserva y la siguiente ejecución lo sustituye sin duplicar.");
        }
        catch (Exception ex)
        {
            _r.Estado = EstadosImportacion.Fallida;
            Anotar("migracion", null, null, null, "Erp.Fallo", Severidad.Bloqueante, $"La migración se ha detenido: {ex.Message}");
        }
        _r.Fin = Ahora;
        _r.IdMigracion = _idMigracion;
        if (_idMigracion != 0) await CerrarAsync();
        return _r;
    }

    private async Task FaseAsync(string fase, Func<Task> trabajo)
    {
        cancelacion.ThrowIfCancellationRequested();
        var inicio = DateTime.Now;
        Avisar($"Fase {fase}…");
        try
        {
            await trabajo();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Una fase que falla se registra y la migración sigue con las demás (lo que dependa de ella saldrá en las incidencias).
            Anotar(fase, null, null, null, "Erp.FaseDetenida", Severidad.Bloqueante, $"La fase «{fase}» se ha detenido por un error: {ex.Message}", ex.ToString());
        }
        await GrabarMapasAsync();
        _r.Tiempos.Add((fase, DateTime.Now - inicio));
        Avisar($"Fase {fase}: {DateTime.Now - inicio:mm\\:ss}");
    }

    private void Avisar(string texto) => progreso?.Report($"{DateTime.Now:HH:mm:ss} {texto}");

    // ---------- incidencias, recuentos y mapas ----------

    private void Anotar(string fase, string? tabla, string? clave, string? campo, string codigo, Severidad severidad, string texto, string? valor = null) =>
        _r.Incidencias.Add(new Incidencia(fase, tabla ?? "", clave, campo, codigo, severidad, texto, valor));

    /// <summary>Criterio general 2: la clave foránea que no encuentra su registro queda nula y se anota con su valor original.</summary>
    private void ClaveNula(string fase, string tabla, string clave, string campo, object? valor, string texto) =>
        Anotar(fase, tabla, clave, campo, "Erp.ClaveNula", Severidad.Advertencia, texto, Convert.ToString(valor, CultureInfo.InvariantCulture));

    private void Excluir(string fase, string tabla, string clave, string motivo, Severidad severidad = Severidad.Advertencia)
    {
        Est(fase, tabla).Excluidas++;
        Anotar(fase, tabla, clave, null, "Erp.Excluido", severidad, motivo);
    }

    private EstadisticaTabla Est(string fase, string tabla)
    {
        var e = _r.Tablas.FirstOrDefault(t => t.Tabla == tabla && t.Fase == fase);
        if (e is null) _r.Tablas.Add(e = new EstadisticaTabla(fase, tabla));
        return e;
    }

    private void Mapear(string tipo, string clave, int id)
    {
        _mapas[(tipo, clave)] = id;
        _pendientes.Add((tipo, clave, id));
    }

    private int? Mapa(string tipo, string clave) => _mapas.TryGetValue((tipo, clave), out var id) ? id : null;

    private int? Anterior(string tipo, string clave) => _anteriores.TryGetValue((tipo, clave), out var id) ? id : null;

    private IEnumerable<int> Anteriores(string tipo) => _anteriores.Where(a => a.Key.Tipo == tipo).Select(a => a.Value).Distinct();

    private async Task GrabarMapasAsync()
    {
        if (_pendientes.Count == 0) return;
        var unicos = _pendientes.GroupBy(p => (p.Tipo, p.Clave)).Select(g => g.Last()).ToList();
        _pendientes.Clear();
        await MasivaAsync("MapasIdentidadERP", ["IdMigracionERP", "TipoEntidad", "ClaveOrigen", "IdDestino"],
            unicos.Select(m => new object?[] { _idMigracion, m.Tipo, ReglasErp.Recortar(m.Clave, 150), m.Id }).ToList());
    }

    /// <summary>Carga masiva (DAO por tandas); con <paramref name="campoId"/> devuelve los autonuméricos en el orden de las filas.</summary>
    private async Task<IReadOnlyList<int>> MasivaAsync(string tabla, string[] campos, IReadOnlyList<object?[]> filas, string? campoId = null)
    {
        if (filas.Count == 0) return [];
        using var carga = new CargaMasivaAccess(_bd.Ruta);
        return await carga.InsertarAsync(tabla, campos, filas, campoId, cancelacion: cancelacion);
    }

    /// <summary>Ejecuta las sentencias en transacciones de <see cref="TamanoLote"/>.</summary>
    private async Task EnLotesAsync<T>(IEnumerable<T> elementos, Func<ISesionDatos, T, Task> trabajo)
    {
        foreach (var trozo in elementos.Chunk(TamanoLote))
        {
            cancelacion.ThrowIfCancellationRequested();
            await _bd.EnTransaccionAsync(async s =>
            {
                foreach (var e in trozo) await trabajo(s, e);
                return true;
            }, cancelacion);
        }
    }

    private Task<int> EjecutarAsync(string sql, params object?[] p) => _bd.EjecutarAsync(s => s.EjecutarAsync(sql, p), cancelacion);

    private Task<IReadOnlyList<T>> ConsultarAsync<T>(string sql, Func<IFilaDatos, T> mapear, params object?[] p) =>
        _bd.EjecutarAsync(s => s.ConsultarAsync(sql, mapear, p), cancelacion);

    private async Task<int> InsertarAsync(string sql, params object?[] p) =>
        await _bd.EjecutarAsync(async s => { await s.EjecutarAsync(sql, p); return await s.UltimoIdAsync(); }, cancelacion);

    /// <summary>Borra por Id en trozos (Access limita la longitud de una lista IN).</summary>
    private async Task<int> BorrarAsync(string tabla, string campo, IEnumerable<int> ids)
    {
        var n = 0;
        foreach (var trozo in ids.Distinct().Chunk(200))
            n += await EjecutarAsync($"DELETE FROM {tabla} WHERE {campo} IN ({Lista(trozo)})");
        return n;
    }

    private async Task<int> AnularAsync(string tabla, string campo, IEnumerable<int> ids)
    {
        var n = 0;
        foreach (var trozo in ids.Distinct().Chunk(200))
            n += await EjecutarAsync($"UPDATE {tabla} SET {campo} = NULL WHERE {campo} IN ({Lista(trozo)})");
        return n;
    }

    private static string Lista(IEnumerable<int> ids) => string.Join(", ", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));

    private static string Txt(int? n) => n?.ToString(CultureInfo.InvariantCulture) ?? "";

    // ---------- preparación ----------

    private async Task PrepararAsync()
    {
        Avisar("Preparando la migración…");
        var sociedad = await ConsultarAsync("SELECT IdSociedad FROM Sociedades WHERE IdSociedad = ?", f => f.Texto("IdSociedad"), Sociedad);
        if (sociedad.Count == 0)
            throw new InvalidOperationException($"La empresa {Sociedad} no existe en la BD de destino: importa antes ContaSol.");
        var importaciones = Convert.ToInt32(await _bd.EjecutarAsync(s => s.EscalarAsync(
            "SELECT COUNT(*) FROM ImportacionesContaSol WHERE IdSociedad = ? AND Estado IN (?, ?)", Sociedad, EstadosImportacion.Completada, EstadosImportacion.ConIncidencias)),
            CultureInfo.InvariantCulture);
        if (importaciones == 0)
            throw new InvalidOperationException("La BD de destino no tiene ninguna importación de ContaSol válida: la migración del ERP enlaza con ella y no se puede ejecutar antes.");

        _idMigracion = await InsertarAsync(
            "INSERT INTO MigracionesERP (IdSociedad, RutaOrigen, HuellaOrigen, RutaDirecciones, HuellaDirecciones, VersionMigrador, FechaHoraInicio, Operador, Estado) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            Sociedad, ReglasErp.Recortar(o.Ruta, 255), o.Huella[..Math.Min(64, o.Huella.Length)], ReglasErp.Recortar(o.RutaDirecciones, 255),
            o.HuellaDirecciones is { } h ? h[..Math.Min(64, h.Length)] : null, MigradorErp.VersionMigrador, Ahora, ReglasErp.Recortar(actor.ToString(), 100), EstadosImportacion.EnCurso);

        // Mapas de la ejecución anterior que llegó a grabarlos: lo que cargó se sustituye.
        var anterior = await _bd.EjecutarAsync(s => s.EscalarAsync(
            "SELECT MAX(m.IdMigracionERP) FROM MigracionesERP AS m WHERE m.IdMigracionERP <> ? AND m.IdSociedad = ? AND EXISTS (SELECT 1 FROM MapasIdentidadERP AS x WHERE x.IdMigracionERP = m.IdMigracionERP)",
            _idMigracion, Sociedad));
        if (anterior is not null and not DBNull)
        {
            foreach (var f in await ConsultarAsync("SELECT TipoEntidad, ClaveOrigen, IdDestino FROM MapasIdentidadERP WHERE IdMigracionERP = ?",
                         f => (Tipo: f.Texto("TipoEntidad") ?? "", Clave: f.Texto("ClaveOrigen") ?? "", Id: f.Entero("IdDestino")), Convert.ToInt32(anterior, CultureInfo.InvariantCulture)))
                _anteriores[(f.Tipo, f.Clave)] = f.Id;
            Anotar("preparacion", "MigracionesERP", Convert.ToString(anterior, CultureInfo.InvariantCulture), null, "Erp.Repeticion", Severidad.Informativa,
                $"Esta BD ya tiene la migración {anterior}: sustituyo lo que cargó ({_anteriores.Count} mapas) sin duplicarlo.");
        }

        await CargarEjerciciosAsync();
        await CargarCentrosAsync();
        foreach (var (codigo, id) in await ConsultarAsync("SELECT Codigo, IdIdentidadContable FROM CodigosContablesReservados WHERE IdSociedad = ?",
                     f => ((f.Texto("Codigo") ?? "").Trim(), f.Entero("IdIdentidadContable")), Sociedad))
            _identidades[codigo] = id;
        foreach (var anio in _ejercicios.Keys)
            foreach (var tipo in new[] { TiposMapa.Asiento, TiposMapa.IvaRepercutido, TiposMapa.IvaSoportado, TiposMapa.Cliente, TiposMapa.Proveedor })
                _mapasCs[(anio, tipo)] = await m.MapasContaSol.LeerAsync(Sociedad, anio, tipo);

        _personas = o.Tabla("Personas").Where(p => p.E("IdPersona") is not null).GroupBy(p => p.E("IdPersona")!.Value).ToDictionary(g => g.Key, g => g.First());
        _agrupaciones = o.Tabla("Agrupaciones").Where(p => p.E("IdAgrupacion") is not null).GroupBy(p => p.E("IdAgrupacion")!.Value).ToDictionary(g => g.Key, g => g.First());
        _conceptosErp = o.Tabla("ConceptosCaja").Where(p => p.E("Id") is not null).GroupBy(p => p.E("Id")!.Value).ToDictionary(g => g.Key, g => g.First());
        foreach (var t in LectorErp.TablasMigradas)
        {
            var constantes = ReglasErp.CamposConstantes(o.Tabla(t));
            if (constantes.Count > 0) _r.CamposConstantes[t] = constantes;
        }
    }

    private async Task CargarEjerciciosAsync()
    {
        _ejercicios.Clear();
        foreach (var e in await ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio, Nombre FROM SociedadesEjercicios WHERE IdSociedad = ?",
                     f => (Id: f.Entero("IdSociedadEjercicio"), Anio: int.TryParse(f.Texto("Ejercicio"), out var a) ? a : 0, Nombre: f.Texto("Nombre")), Sociedad))
            if (e.Anio > 0) _ejercicios[e.Anio] = (e.Id, e.Nombre);
    }

    private async Task CargarCentrosAsync()
    {
        _centros.Clear();
        foreach (var (ruta, id) in await ConsultarAsync("SELECT RutaJerarquica, IdCentroCoste FROM CentrosCoste WHERE IdSociedad = ?",
                     f => ((f.Texto("RutaJerarquica") ?? "").Trim(), f.Entero("IdCentroCoste")), Sociedad))
            _centros[ruta] = id;
    }

    private int? Centro(string? clave) => clave is not null && _centros.TryGetValue(clave, out var id) ? id : null;

    private int? Identidad(string? codigo) => codigo?.Trim() is { Length: > 0 } c && _identidades.TryGetValue(c, out var id) ? id : null;

    private IReadOnlyDictionary<string, int> MapaCs(int anio, string tipo) =>
        _mapasCs.TryGetValue((anio, tipo), out var mapa) ? mapa : new Dictionary<string, int>();

    /// <summary>Asiento importado de ContaSol de un ejercicio por su «DIA:ASI».</summary>
    private int? AsientoCs(int anio, int? diario, int? asiento) =>
        asiento is int a && MapaCs(anio, TiposMapa.Asiento).TryGetValue($"{diario ?? 1}:{a}", out var id) ? id : null;

    private ContextoSociedadEjercicio? Contexto(int anio) => _ejercicios.TryGetValue(anio, out var e) ? new ContextoSociedadEjercicio(Sociedad, e.Id, anio) : null;

    /// <summary>Ejercicio más reciente de la sociedad (el de trabajo para conceptos y programadas).</summary>
    private int UltimoEjercicio => _ejercicios.Keys.Max();

    // ---------- cierre ----------

    private async Task CerrarAsync()
    {
        await GrabarMapasAsync();
        await MasivaAsync("IncidenciasMigracionERP",
            ["IdMigracionERP", "Fase", "TablaOrigen", "ClaveOrigen", "CampoOrigen", "Codigo", "Severidad", "Explicacion", "ValorOriginal", "Estado"],
            _r.Incidencias.Select(i => new object?[]
            {
                _idMigracion, ReglasErp.Recortar(i.Fase, 50), ReglasErp.Recortar(i.Tabla, 100), ReglasErp.Recortar(i.Clave, 150), ReglasErp.Recortar(i.Campo, 100),
                ReglasErp.Recortar(i.Codigo, 50) ?? "", i.Severidad.ToString().ToUpperInvariant(), i.Explicacion, i.ValorOriginal, "PENDIENTE",
            }).ToList());
        var resumen = string.Join("; ", _r.Tablas.Select(t => $"{t.Tabla} {t.Cargadas}/{t.Leidas}"));
        await EjecutarAsync("UPDATE MigracionesERP SET FechaHoraFin = ?, Estado = ?, Resumen = ? WHERE IdMigracionERP = ?", Ahora, _r.Estado, resumen, _idMigracion);
    }

    // ---------- limpieza de la ejecución anterior ----------

    /// <summary>
    /// Repetir la migración no duplica: antes de cargar, se borra lo que cargó la ejecución anterior en orden inverso de
    /// dependencias (nóminas migradas, activos, Norma 43, compras, programadas y facturas). Los terceros, los conceptos, las actividades y los
    /// catálogos no se borran: se actualizan por sus mapas.
    /// </summary>
    private async Task LimpiarAnteriorAsync()
    {
        if (_anteriores.Count == 0) return;
        var nominas = Anteriores(TiposErp.ImportacionNomina).ToList();
        await BorrarAsync("Nominas", "IdImportacionNomina", nominas);
        await BorrarAsync("ImportacionesNomina", "IdImportacionNomina", nominas);

        var activos = Anteriores(TiposErp.Activo).ToList();
        foreach (var t in new[] { "SubcuentasActivo", "CuentasActivo", "ActivosInmuebles", "PlanesAmortizacionActivo" })
            await BorrarAsync(t, "IdActivo", activos);
        await BorrarAsync("Activos", "IdActivo", activos);

        var lineas = Anteriores(TiposErp.LineaN43).ToList();
        foreach (var t in new[] { "ImputacionesLineaExtractoBancario", "ContabilizacionesLineaExtracto", "ComplementosLineaExtractoBancario" })
            await BorrarAsync(t, "IdLineaExtractoBancario", lineas);
        await AnularAsync("FacturasCompra", "LineaN43", lineas);
        await BorrarAsync("LineasExtractoBancario", "IdLineaExtractoBancario", lineas);
        await BorrarAsync("ExtractosBancarios", "IdExtractoBancario", Anteriores(TiposErp.ExtractoN43));
        await BorrarAsync("IncidenciasImportacionBancaria", "IdLoteImportacionBancaria", Anteriores(TiposErp.LoteN43));
        await BorrarAsync("LotesImportacionBancaria", "IdLoteImportacionBancaria", Anteriores(TiposErp.LoteN43));
        await BorrarAsync("PalabrasClaveBancarias", "IdPalabraClaveBancaria", Anteriores(TiposErp.PalabraN43));

        var compras = Anteriores(TiposErp.Compra).ToList();
        await AnularAsync("Activos", "IdFacturaCompra", compras);
        await AnularAsync("AdjuntosRecibidosCorreo", "IdFacturaCompra", compras);
        await BorrarAsync("DesglosesFacturaCompra", "IdRegistro", compras);
        await BorrarAsync("FacturasCompra", "IdRegistro", compras);

        var programadas = Anteriores(TiposErp.Programada).ToList();
        await AnularAsync("Facturas", "IdFacturaProgramada", programadas);
        await BorrarAsync("LineasFacturasProgramadas", "IdFacturaProgramada", programadas);
        await BorrarAsync("FacturasProgramadas", "IdFacturaProgramada", programadas);

        var facturas = Anteriores(TiposErp.Factura).ToList();
        await AnularAsync("IVARepercutido", "IdFactura", facturas);
        await AnularAsync("Facturas", "IdFacturaRectificada", facturas);
        await BorrarAsync("LineasFactura", "IdFactura", facturas);
        var borradas = await BorrarAsync("Facturas", "IdFactura", facturas);
        Anotar("limpieza", null, null, null, "Erp.Sustituido", Severidad.Informativa,
            $"Borrado lo cargado por la ejecución anterior para volver a cargarlo: {borradas} facturas, {programadas.Count} programadas, {compras.Count} compras, " +
            $"{lineas.Count} líneas bancarias y {activos.Count} activos.");
    }
}

/// <summary>Tipos de entidad de MapasIdentidadERP (clave del ERP → Id de ProContaVF).</summary>
public static class TiposErp
{
    public const string Persona = "PERSONA";
    public const string Cliente = "CLIENTE";
    public const string Ejercicio = "EJERCICIO";
    public const string Actividad = "ACTIVIDAD";
    public const string TipoConcepto = "TIPO_CONCEPTO";
    public const string Concepto = "CONCEPTO";
    public const string Serie = "SERIE";
    public const string Factura = "FACTURA";
    public const string Programada = "PROGRAMADA";
    public const string Compra = "COMPRA";
    public const string TextoClave = "TEXTO_CLAVE";
    public const string CuentaBancaria = "CUENTA_BANCARIA";
    public const string LoteN43 = "LOTE_N43";
    public const string ExtractoN43 = "EXTRACTO_N43";
    public const string LineaN43 = "LINEA_N43";
    public const string PalabraN43 = "PALABRA_N43";
    public const string TipoActivo = "TIPO_ACTIVO";
    public const string PlantillaActivo = "PLANTILLA_ACTIVO";
    public const string Activo = "ACTIVO";
    public const string Empleado = "EMPLEADO";
    public const string ImportacionNomina = "IMPORTACION_NOMINA";
}
