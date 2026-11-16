using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Lineas;

/// <summary>Resultado de una acción sobre varias líneas: las hechas y las rechazadas con su motivo.</summary>
/// <param name="Recontabilizar">Líneas hechas que ya estaban contabilizadas por ProContaVF: su asiento hay que regenerarlo.</param>
public sealed record ResultadoLineasN43(IReadOnlyList<int> Hechas, IReadOnlyDictionary<int, Error> Rechazadas, IReadOnlyList<int> Recontabilizar)
{
    public static ResultadoLineasN43 Fallo(IEnumerable<int> ids, Error e) => new([], ids.Distinct().ToDictionary(i => i, _ => e), []);
}

/// <summary>Palabra clave bancaria con su cuenta, para el diálogo de palabras clave de una línea.</summary>
public sealed record PalabraClaveLineaN43(int Id, string? Entidad, string? Sentido, string PalabraClave, string? PalabraClave2, int? IdIdentidadContable,
    string? Cuenta, string? NombreCuenta, int? Especial, int Usos)
{
    public bool EsRemesa => Especial == ReglasPalabrasClave.EspecialRemesa;
}

/// <summary>Datos de una palabra clave nueva o modificada (Id null = nueva).</summary>
/// <param name="Sentido">Con el convenio de PalabrasClaveBancarias (el de la 572): «H» para cargos de la línea, «D» para abonos; null = los dos.</param>
public sealed record EdicionPalabraClaveN43(int? Id, string? Entidad, string? Sentido, string PalabraClave, string? PalabraClave2, int? IdIdentidadContable, int? Especial);

/// <summary>
/// Escrituras de la pantalla de líneas N43 (norma43/pantallas) que no son de la conciliación: poner la cuenta de destino a mano,
/// registrar un traspaso entre bancos contra la cuenta puente y mantener las palabras clave desde una línea. Contabilizar, deshacer y
/// actualizar textos son de <see cref="ServicioConciliacionN43"/>; la reasignación automática, de <see cref="AsignadorCuentasN43"/>.
/// </summary>
public sealed class ServicioLineasN43(IBaseDatos bd)
{
    /// <summary>Origen de las imputaciones puestas a mano desde la pantalla.</summary>
    public const string OrigenManual = "MANUAL";

    /// <summary>Origen de las imputaciones de un traspaso entre bancos.</summary>
    public const string OrigenTraspaso = "TRASPASO";

    /// <summary>
    /// Cuenta puente de los traspasos entre bancos propios si la cuenta bancaria no tiene otra configurada
    /// (ConfiguracionesContablesCuentaBancaria.IdIdentidadCuentaPuente): la del ERP.
    /// </summary>
    public const string CuentaPuentePredeterminada = "5729999";

    // ---------- asignar cuenta ----------

    /// <summary>
    /// Pone a las líneas la cuenta de destino indicada (identidad contable) por el importe entero del movimiento, en lugar de la que
    /// tuvieran (las imputaciones anteriores quedan DESCARTADA, sin borrarse). No se tocan las líneas contabilizadas en el ERP, las de
    /// lotes revertidos ni las de ejercicios cerrados; la cuenta tiene que existir en el ejercicio de cada línea. Las que estaban
    /// contabilizadas por ProContaVF salen en <see cref="ResultadoLineasN43.Recontabilizar"/>: su asiento ya no lleva esa cuenta.
    /// </summary>
    public Task<ResultadoLineasN43> AsignarCuentaAsync(string idSociedad, IReadOnlyCollection<int> ids, int idIdentidadContable, Actor actor) =>
        AsignarAsync(idSociedad, ids, _ => (idIdentidadContable, (string?)null), OrigenManual, actor);

    private async Task<ResultadoLineasN43> AsignarAsync(string idSociedad, IReadOnlyCollection<int> ids, Func<LineaEscritura, (int? Identidad, string? Error)> cuenta,
        string origen, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(actor);
        return await bd.EnTransaccionAsync(async s =>
        {
            var lineas = await LeerAsync(s, idSociedad, ids);
            var hechas = new List<int>();
            var recontabilizar = new List<int>();
            var rechazadas = new Dictionary<int, Error>();
            foreach (var id in ids.Distinct())
            {
                if (!lineas.TryGetValue(id, out var l)) { rechazadas[id] = NoExiste(id); continue; }
                if (Bloqueo(l) is { } e) { rechazadas[id] = e; continue; }
                var (identidad, error) = cuenta(l);
                if (identidad is not int idIdentidad)
                {
                    rechazadas[id] = new Error("Lineas.SinCuentaPuente", error ?? "No hay cuenta que asignar.", l.Rotulo, null, null);
                    continue;
                }
                var existe = await s.EscalarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?",
                    l.IdSociedadEjercicio ?? 0, idIdentidad);
                if (Convert.ToInt32(existe, CultureInfo.InvariantCulture) == 0)
                {
                    rechazadas[id] = new Error("Lineas.CuentaNoEstaEnEjercicio", $"La cuenta elegida no existe en el ejercicio {l.Ejercicio}.", l.Rotulo,
                        "La cuenta de destino tiene que estar dada de alta en el ejercicio del movimiento, que es donde irá su asiento.",
                        $"Elige una cuenta del ejercicio {l.Ejercicio} o dala de alta en él (en el buscador, «Crear subcuenta…»).");
                    continue;
                }
                await s.EjecutarAsync("UPDATE ImputacionesLineaExtractoBancario SET Estado = 'DESCARTADA' WHERE IdLineaExtractoBancario = ? AND Estado <> 'DESCARTADA'", id);
                await s.EjecutarAsync(
                    "INSERT INTO ImputacionesLineaExtractoBancario (IdLineaExtractoBancario, IdIdentidadContable, Rol, ImporteFirmado, Origen, Confianza, Estado, Concepto, IdCentroCoste, IdPalabraClaveBancaria) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    id, idIdentidad, AsignadorCuentasN43.Rol, l.Sentido == ServicioConciliacionN43.Cargo ? l.Importe : -l.Importe, origen, null, AsignadorCuentasN43.Estado,
                    l.Concepto, null, null);
                hechas.Add(id);
                if (l.IdAsiento is not null) recontabilizar.Add(id);
            }
            return new ResultadoLineasN43(hechas, rechazadas, recontabilizar);
        });
    }

    // ---------- traspaso entre bancos ----------

    /// <summary>
    /// Registra dos líneas como traspaso entre cuentas bancarias de la sociedad: el cargo en un banco y el abono en el otro, por el mismo
    /// importe y sin contabilizar. Las dos van a la cuenta puente (la de la configuración de cada cuenta bancaria o, si no tiene, la
    /// <see cref="CuentaPuentePredeterminada"/> del ejercicio de la línea), donde sus dos asientos se compensan. Se hace todo o nada.
    /// </summary>
    public async Task<ResultadoLineasN43> TraspasoAsync(string idSociedad, int idA, int idB, Actor actor)
    {
        var ids = new[] { idA, idB };
        var lineas = await bd.EjecutarAsync(s => LeerAsync(s, idSociedad, ids));
        if (!lineas.TryGetValue(idA, out var a) || !lineas.TryGetValue(idB, out var b))
            return ResultadoLineasN43.Fallo(ids, NoExiste(lineas.ContainsKey(idA) ? idB : idA));
        var error = ErrorTraspaso(a, b);
        if (error is not null) return ResultadoLineasN43.Fallo(ids, error);
        var puentes = new Dictionary<int, (int? Id, string? Error)>();
        foreach (var l in new[] { a, b }) puentes[l.Id] = await CuentaPuenteAsync(l);
        if (puentes.Values.FirstOrDefault(p => p.Id is null) is { Error: { } sinPuente })
            return ResultadoLineasN43.Fallo(ids, new Error("Lineas.SinCuentaPuente", sinPuente, null,
                "Los traspasos entre bancos propios se compensan en la cuenta puente.", $"Da de alta la {CuentaPuentePredeterminada} en el plan de cuentas del ejercicio y vuelve a intentarlo."));
        var r = await AsignarAsync(idSociedad, ids, l => puentes[l.Id], OrigenTraspaso, actor);
        if (r.Rechazadas.Count > 0 && r.Hechas.Count > 0)
            throw new InvalidOperationException("El traspaso quedó a medias."); // no ocurre: las comprobaciones son las mismas que las de AsignarAsync
        return r;
    }

    /// <summary>Por qué dos líneas no forman un traspaso entre bancos (null si sí); mismas reglas que <see cref="AccionesLineasN43"/>.</summary>
    internal static Error? ErrorTraspaso(LineaEscritura a, LineaEscritura b)
    {
        string? motivo = a.Sentido == b.Sentido ? "Las dos líneas son del mismo sentido: un traspaso es un cargo en un banco y un abono en el otro."
            : a.Importe != b.Importe ? "Los importes no coinciden: un traspaso mueve el mismo importe de un banco a otro."
            : a.IdCuentaBancaria == b.IdCuentaBancaria ? "Las dos líneas son de la misma cuenta bancaria."
            : a.IdAsiento is not null || b.IdAsiento is not null ? "Alguna de las dos ya está contabilizada."
            : null;
        if (motivo is null) return Bloqueo(a) ?? Bloqueo(b);
        return new Error("Lineas.NoEsTraspaso", motivo, $"{a.Rotulo} y {b.Rotulo}", null,
            "Elige el cargo de un banco y el abono del mismo importe en el otro, sin contabilizar (deshaz antes su asiento si hace falta).");
    }

    private Task<(int? Id, string? Error)> CuentaPuenteAsync(LineaEscritura l) => bd.EjecutarAsync(async s =>
    {
        if (l.IdCuentaBancaria is int cb)
        {
            var cfg = await s.ConsultarAsync(
                "SELECT IdIdentidadCuentaPuente, FechaDesde, FechaHasta FROM ConfiguracionesContablesCuentaBancaria WHERE IdCuentaBancaria = ? ORDER BY FechaDesde DESC",
                f => (Id: f.EnteroNulo("IdIdentidadCuentaPuente"), Desde: f.FechaNula("FechaDesde"), Hasta: f.FechaNula("FechaHasta")), cb);
            var fecha = l.FechaMovimiento;
            var vigente = cfg.FirstOrDefault(x => (x.Desde is null || x.Desde.Value.Date <= fecha) && (x.Hasta is null || x.Hasta.Value.Date >= fecha));
            if (vigente.Id is int puente) return ((int?)puente, (string?)null);
        }
        var id = await s.EscalarAsync("SELECT TOP 1 IdIdentidadContable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?",
            l.IdSociedadEjercicio ?? 0, CuentaPuentePredeterminada);
        return id is null or DBNull
            ? (null, $"El ejercicio {l.Ejercicio} no tiene la cuenta puente de traspasos {CuentaPuentePredeterminada} ni la cuenta bancaria {l.Ccc} tiene otra configurada.")
            : (Convert.ToInt32(id, CultureInfo.InvariantCulture), null);
    });

    // ---------- palabras clave ----------

    /// <summary>Palabras clave activas de la sociedad que encajan con la línea (las que la asignarían), con su cuenta y cuántas líneas asignaron.</summary>
    public async Task<IReadOnlyList<PalabraClaveLineaN43>> PalabrasQueEncajanAsync(string idSociedad, LineaN43 linea)
    {
        ArgumentNullException.ThrowIfNull(linea);
        var todas = await PalabrasAsync(idSociedad);
        var l = ParaAsignar(linea);
        return todas.Where(p => ReglasPalabrasClave.Encaja(new PalabraClaveN43(p.Id, p.Entidad, p.Sentido, p.PalabraClave, p.PalabraClave2, p.IdIdentidadContable, p.Especial), l)).ToList();
    }

    private Task<IReadOnlyList<PalabraClaveLineaN43>> PalabrasAsync(string idSociedad) => bd.EjecutarAsync(async s =>
    {
        var palabras = await s.ConsultarAsync(
            "SELECT IdPalabraClaveBancaria, Entidad, Sentido, PalabraClave, PalabraClave2, IdIdentidadContable, Especial FROM PalabrasClaveBancarias " +
            "WHERE IdSociedad = ? AND Activa = True ORDER BY IdPalabraClaveBancaria",
            f => (Id: f.Entero("IdPalabraClaveBancaria"), Entidad: ConsultaLineasN43.Limpio(f.Texto("Entidad")), Sentido: ConsultaLineasN43.Limpio(f.Texto("Sentido")),
                Palabra: f.Texto("PalabraClave") ?? "", Palabra2: ConsultaLineasN43.Limpio(f.Texto("PalabraClave2")), Identidad: f.EnteroNulo("IdIdentidadContable"),
                Especial: f.EnteroNulo("Especial")), idSociedad);
        var usos = (await s.ConsultarAsync(
                "SELECT IdPalabraClaveBancaria, COUNT(*) AS N FROM ImputacionesLineaExtractoBancario WHERE IdPalabraClaveBancaria IS NOT NULL AND Estado <> 'DESCARTADA' GROUP BY IdPalabraClaveBancaria",
                f => (Id: f.Entero("IdPalabraClaveBancaria"), N: f.Entero("N"))))
            .ToDictionary(x => x.Id, x => x.N);
        var cuentas = new Dictionary<int, (string Codigo, string Nombre)?>();
        var r = new List<PalabraClaveLineaN43>();
        foreach (var p in palabras)
        {
            (string Codigo, string Nombre)? c = null;
            if (p.Identidad is int i && !cuentas.TryGetValue(i, out c))
                cuentas[i] = c = await s.ConsultarPrimeroAsync(
                    "SELECT TOP 1 Codigo, Nombre FROM CuentasContables WHERE IdIdentidadContable = ? ORDER BY IdSociedadEjercicio DESC",
                    f => ((string Codigo, string Nombre)?)((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), i);
            r.Add(new PalabraClaveLineaN43(p.Id, p.Entidad, p.Sentido, p.Palabra, p.Palabra2, p.Identidad, c?.Codigo, c?.Nombre, p.Especial, usos.GetValueOrDefault(p.Id)));
        }
        return (IReadOnlyList<PalabraClaveLineaN43>)r;
    });

    /// <summary>
    /// Crea o modifica una palabra clave. Tiene que encajar con la línea desde la que se edita (su texto es un trozo literal del concepto) y
    /// llevar cuenta, salvo las de remesa. No cambia ninguna línea: para aplicarla, «Reasignar por palabras clave».
    /// </summary>
    public async Task<Resultado<int>> GuardarPalabraAsync(string idSociedad, LineaN43 linea, EdicionPalabraClaveN43 p)
    {
        ArgumentNullException.ThrowIfNull(linea);
        ArgumentNullException.ThrowIfNull(p);
        if (ValidarPalabra(linea, p) is { } e) return Resultado<int>.Fallo(e);
        return await bd.EnTransaccionAsync(async s =>
        {
            var palabra = p.PalabraClave.Trim();
            var palabra2 = string.IsNullOrWhiteSpace(p.PalabraClave2) ? null : p.PalabraClave2.Trim();
            var entidad = string.IsNullOrWhiteSpace(p.Entidad) ? null : p.Entidad.Trim();
            var sentido = string.IsNullOrWhiteSpace(p.Sentido) ? null : p.Sentido.Trim().ToUpperInvariant();
            if (p.Id is int id)
            {
                var n = await s.EjecutarAsync(
                    "UPDATE PalabrasClaveBancarias SET Entidad = ?, Sentido = ?, PalabraClave = ?, PalabraClave2 = ?, IdIdentidadContable = ?, Especial = ? WHERE IdPalabraClaveBancaria = ? AND IdSociedad = ?",
                    entidad, sentido, palabra, palabra2, p.IdIdentidadContable, p.Especial, id, idSociedad);
                return n == 1 ? Resultado<int>.Correcto(id)
                    : Resultado<int>.Fallo(new Error("PalabrasClave.NoExiste", "La palabra clave ya no existe.", palabra, "Se ha borrado desde otro puesto.", "Cierra el diálogo y vuelve a abrirlo."));
            }
            await s.EjecutarAsync(
                "INSERT INTO PalabrasClaveBancarias (IdSociedad, Entidad, Sentido, PalabraClave, PalabraClave2, IdIdentidadContable, Especial, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                idSociedad, entidad, sentido, palabra, palabra2, p.IdIdentidadContable, p.Especial, true);
            return Resultado<int>.Correcto(await s.UltimoIdAsync());
        });
    }

    /// <summary>Por qué no se puede guardar la palabra clave (null si se puede).</summary>
    public static Error? ValidarPalabra(LineaN43 linea, EdicionPalabraClaveN43 p)
    {
        ArgumentNullException.ThrowIfNull(linea);
        ArgumentNullException.ThrowIfNull(p);
        if (ReglasPalabrasClave.Trozos(p.PalabraClave).Count == 0)
            return new Error("PalabrasClave.Vacia", "Falta el texto de la palabra clave.", null, "La palabra clave es el trozo del concepto que identifica el movimiento.",
                "Selecciona en el concepto el trozo que se repite en estos movimientos y pulsa «Usar como palabra clave».");
        if (p.IdIdentidadContable is null && p.Especial != ReglasPalabrasClave.EspecialRemesa)
            return new Error("PalabrasClave.SinCuenta", "Falta la cuenta de la palabra clave.", p.PalabraClave, "Sin cuenta, la palabra clave no asigna nada.", "Elige la cuenta con «Elegir cuenta…».");
        var palabra = new PalabraClaveN43(p.Id ?? 0, p.Entidad, p.Sentido, p.PalabraClave, p.PalabraClave2, p.IdIdentidadContable, p.Especial);
        if (!ReglasPalabrasClave.Encaja(palabra, ParaAsignar(linea)))
            return new Error("PalabrasClave.NoEncaja", "La palabra clave no encaja con esta línea.", p.PalabraClave,
                "Su texto (y el segundo, si lo tiene) tiene que aparecer en el concepto de la línea, y el banco y el sentido tienen que ser los suyos.",
                "Elige los textos seleccionándolos en el concepto, o quita la marca de «Solo este banco» o «Solo cargos/abonos».");
        return null;
    }

    /// <summary>
    /// Borra una palabra clave. Si ya asignó cuentas a alguna línea, se desactiva en lugar de borrarse (las líneas guardan qué palabra
    /// las asignó) y deja de aplicarse igual. Devuelve el texto para el operador.
    /// </summary>
    public Task<Resultado<string>> BorrarPalabraAsync(string idSociedad, int idPalabra) => bd.EnTransaccionAsync(async s =>
    {
        var usos = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM ImputacionesLineaExtractoBancario WHERE IdPalabraClaveBancaria = ?", idPalabra), CultureInfo.InvariantCulture);
        var n = usos == 0
            ? await s.EjecutarAsync("DELETE FROM PalabrasClaveBancarias WHERE IdPalabraClaveBancaria = ? AND IdSociedad = ?", idPalabra, idSociedad)
            : await s.EjecutarAsync("UPDATE PalabrasClaveBancarias SET Activa = False WHERE IdPalabraClaveBancaria = ? AND IdSociedad = ?", idPalabra, idSociedad);
        if (n != 1)
            return Resultado<string>.Fallo(new Error("PalabrasClave.NoExiste", "La palabra clave ya no existe.", null, "Se ha borrado desde otro puesto.", "Cierra el diálogo y vuelve a abrirlo."));
        return Resultado<string>.Correcto(usos == 0 ? "Palabra clave borrada."
            : $"Palabra clave desactivada: ya no se aplica, pero se conserva porque asignó la cuenta de {usos} línea(s), que no cambian.");
    });

    internal static LineaN43ParaAsignar ParaAsignar(LineaN43 l) =>
        new(l.Id, l.Entidad, l.Sentido, l.Importe, l.Concepto, l.Documento, l.Referencia1, l.Referencia2);

    // ---------- lecturas ----------

    internal sealed record LineaEscritura(int Id, int? IdCuentaBancaria, string Ccc, DateTime FechaOperacion, DateTime FechaValor, string Sentido, decimal Importe,
        string? Concepto, int? IdAsiento, bool AsientoPropio, bool LoteRevertido, int? IdSociedadEjercicio, bool EjercicioCerrado)
    {
        public DateTime FechaMovimiento => ServicioConciliacionN43.FechaMovimiento(FechaOperacion, FechaValor);
        public int Ejercicio => FechaMovimiento.Year;
        public string Rotulo => $"Movimiento del {FechaOperacion:dd/MM/yyyy} de {Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €";
    }

    private static Error NoExiste(int id) => new("Lineas.NoExiste", $"No encuentro el movimiento {id}.", $"Movimiento {id}",
        "Se ha revertido su extracto o es de otra sociedad.", "Actualiza la lista (F5).");

    /// <summary>Por qué no se puede cambiar la cuenta de una línea (null si se puede).</summary>
    internal static Error? Bloqueo(LineaEscritura l) =>
        l.LoteRevertido ? new Error("Lineas.LoteRevertido", "El extracto de este movimiento está revertido.", l.Rotulo, "Sus movimientos no cuentan.", "Vuelve a importar el fichero si el movimiento es bueno.")
        : l.IdSociedadEjercicio is null ? new Error("Lineas.SinEjercicio", $"La sociedad no tiene el ejercicio {l.Ejercicio}.", l.Rotulo,
            "La cuenta de destino se busca en el ejercicio del movimiento.", $"Da de alta el ejercicio {l.Ejercicio} en Parámetros y tablas › Ejercicios.")
        : l.EjercicioCerrado ? new Error("Lineas.EjercicioCerrado", $"El ejercicio {l.Ejercicio} está cerrado.", l.Rotulo, "Las líneas de un ejercicio cerrado solo se consultan.",
            "Si hay que corregir algo, reabre el ejercicio con el proceso contable autorizado.")
        : l.IdAsiento is not null && !l.AsientoPropio ? new Error("Lineas.ContabilizadaFuera", $"Este movimiento se contabilizó en el ERP (asiento {l.IdAsiento}).", l.Rotulo,
            "Ese asiento no lo generó ProContaVF y no se regenera desde aquí.", "Corrígelo desde el Mayor con una plantilla de corrección o un asiento manual.")
        : null;

    private static async Task<Dictionary<int, LineaEscritura>> LeerAsync(ISesionDatos s, string idSociedad, IReadOnlyCollection<int> ids)
    {
        var r = new Dictionary<int, LineaEscritura>();
        var ejercicios = (await s.ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio, EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdSociedadEjercicio"), Anio: ConsultaLineasN43.Anio(f.Texto("Ejercicio")), Cerrado: !f.EsNulo("EjercicioCerrado") && f.Logico("EjercicioCerrado")), idSociedad))
            .GroupBy(e => e.Anio).ToDictionary(g => g.Key, g => g.First());
        foreach (var bloque in ids.Distinct().Chunk(300))
        {
            var lista = ConsultaLineasN43.Lista(bloque);
            var filas = await s.ConsultarAsync(
                "SELECT l.IdLineaExtractoBancario, e.IdCuentaBancaria, e.Entidad, e.Oficina, e.Cuenta, l.FechaOperacion, l.FechaValor, l.Sentido, l.Importe, l.ConceptoCompleto, lo.Estado " +
                "FROM (LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
                "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria " +
                $"WHERE lo.IdSociedad = ? AND l.IdLineaExtractoBancario IN ({lista})",
                f => (Id: f.Entero("IdLineaExtractoBancario"), Cuenta: f.EnteroNulo("IdCuentaBancaria"),
                    Ccc: $"{ConsultaLineasN43.Limpio(f.Texto("Entidad"))} {ConsultaLineasN43.Limpio(f.Texto("Oficina"))} {ConsultaLineasN43.Limpio(f.Texto("Cuenta"))}",
                    Op: f.Fecha("FechaOperacion").Date, Valor: f.Fecha("FechaValor").Date, Sentido: (ConsultaLineasN43.Limpio(f.Texto("Sentido")) ?? "").ToUpperInvariant(),
                    Importe: f.Decimal("Importe"), Concepto: ConsultaLineasN43.Limpio(f.Texto("ConceptoCompleto")), Revertido: f.Texto("Estado") == ServicioImportacionN43.EstadoRevertido),
                idSociedad);
            var asientos = (await s.ConsultarAsync(
                    "SELECT c.IdLineaExtractoBancario, c.IdAsiento, a.EsAutomatico, a.MotorPropietario, a.CodigoProceso, a.ClaveEjecucion " +
                    "FROM ContabilizacionesLineaExtracto AS c LEFT JOIN Asientos AS a ON c.IdAsiento = a.IdAsiento " +
                    $"WHERE c.IdLineaExtractoBancario IN ({lista}) ORDER BY c.IdContabilizacionLineaExtracto DESC",
                    f => (Linea: f.Entero("IdLineaExtractoBancario"), Asiento: f.Entero("IdAsiento"), Existe: !f.EsNulo("EsAutomatico"),
                        Propio: !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico") && f.Texto("MotorPropietario") == ServicioConciliacionN43.MotorPropietario
                            && f.Texto("CodigoProceso") == ServicioConciliacionN43.CodigoProceso
                            && f.Texto("ClaveEjecucion") == f.Entero("IdLineaExtractoBancario").ToString(CultureInfo.InvariantCulture))))
                .Where(x => x.Existe).GroupBy(x => x.Linea).ToDictionary(g => g.Key, g => g.First());
            foreach (var f in filas)
            {
                var anio = ServicioConciliacionN43.FechaMovimiento(f.Op, f.Valor).Year;
                var ej = ejercicios.TryGetValue(anio, out var e) ? e : default;
                var a = asientos.TryGetValue(f.Id, out var x) ? x : default;
                r[f.Id] = new LineaEscritura(f.Id, f.Cuenta, f.Ccc, f.Op, f.Valor, f.Sentido, f.Importe, f.Concepto, a.Linea == 0 ? null : a.Asiento, a.Propio, f.Revertido,
                    ej.Id == 0 ? null : ej.Id, ej.Cerrado);
            }
        }
        return r;
    }
}
