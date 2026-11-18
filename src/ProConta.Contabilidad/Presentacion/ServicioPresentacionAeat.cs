using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Presentacion;

/// <summary>
/// Lo que un modelo entrega a la pieza común para validarlo y presentarlo: su fichero con el diseño de registro, el declarante
/// esperado, la forma de pago, el importe a ingresar, el resultado que se liquida en la contabilidad, el resumen que se enseña en la
/// conformidad y, en una rectificativa (complementaria), el justificante de la anterior.
/// </summary>
/// <param name="LeerNif">Lee el NIF del declarante del fichero (cada modelo lo tiene en su sitio).</param>
public sealed record DeclaracionAeat(string Modelo, int Ejercicio, string Periodo, string Fichero, string NifSociedad, string RazonSocial, char FormaPago,
    decimal ImporteAIngresar, decimal Resultado, string? Iban, string? ComplementariaDe, IReadOnlyList<(string Etiqueta, string Valor)> Resumen,
    Func<string, string?> LeerNif)
{
    /// <summary>NRC del pago (22 caracteres), solo con la forma de pago I: lo da el banco o la Sede al pagar.</summary>
    public string? Nrc { get; init; }

    /// <summary>Por qué el NRC no vale (null si vale): 22 letras o cifras.</summary>
    public static string? ErrorNrc(string? nrc)
    {
        var s = (nrc ?? "").Trim().ToUpperInvariant();
        if (s.Length == 0) return "Falta el NRC del pago: págalo en el banco o en la Sede de la AEAT y escribe el NRC que te den.";
        if (s.Length != 22 || s.Any(ch => !char.IsAsciiLetterOrDigit(ch))) return $"El NRC tiene 22 letras o cifras y «{s}» tiene {s.Length}: revisa que esté bien copiado del justificante del pago.";
        return null;
    }

    public string Titulo => $"Modelo {Modelo} · {Periodo} {Ejercicio}" + (ComplementariaDe is null ? "" : " · rectificativa");

    public string TextoFormaPago => FormaPago switch
    {
        'U' => "Domiciliación del ingreso",
        'I' => "Ingreso con NRC",
        'C' => "A compensar",
        'N' => "Sin resultado (cero)",
        'D' => "Solicitud de devolución",
        _ => FormaPago.ToString(),
    };
}

/// <summary>
/// Paso previo a presentar: el PDF validado por la AEAT, el NIF leído del fichero, el entorno, el presentador (titular del
/// certificado), lo que ya consta presentado y lo que impide presentar (<see cref="Bloqueos"/>, cada uno con su solución).
/// </summary>
public sealed record BorradorAeat(DeclaracionAeat Declaracion, EntornoAeat Entorno, ValidacionAeat Validacion, string? NifLeido, TitularCertificado? Presentador,
    IReadOnlyList<DeclaracionPresentadaAeat> YaPresentadas, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos)
{
    public bool SePuedePresentar => Bloqueos.Count == 0;
}

/// <summary>Una presentación guardada en PresentacionesAeat.</summary>
public sealed record PresentacionGuardada(int Id, string IdSociedad, string Modelo, int Ejercicio, string Periodo, string Entorno, DateTime FechaHora, string Csv,
    string? Expediente, string Justificante, string? ComplementariaDe, string? FormaPago, decimal Importe, decimal Resultado, string? NifPresentador,
    string? Presentador, string? Operador, string? Avisos, string? UrlPdf, string? RutaPdf, string? Origen = null)
{
    /// <summary>Origen de una presentación traída por «Consultar a la AEAT» (hecha fuera del programa, por ejemplo por la gestoría).</summary>
    public const string OrigenAeat = "AEAT";
    /// <summary>Origen de una presentación hecha desde el programa.</summary>
    public const string OrigenPrograma = "PROGRAMA";

    public bool EsPruebas => Entorno == ServiciosAeat.Codigo(EntornoAeat.Pruebas);

    /// <summary>Presentada fuera del programa: la consulta de la AEAT no da su importe ni su fichero.</summary>
    public bool EsExterna => Origen == OrigenAeat;

    /// <summary>
    /// Si lo presentado no coincide con lo que se calcula ahora (estado «Desactualizado»). Una presentada fuera del programa no se compara:
    /// la consulta de la AEAT no da su importe.
    /// </summary>
    public bool DistintoDe(decimal calculado) => !EsExterna && Resultado != calculado;
}

/// <summary>Resultado de presentar: lo guardado, si el PDF justificante quedó archivado y los avisos (de la AEAT y propios).</summary>
public sealed record PresentacionHecha(PresentacionGuardada Guardada, IReadOnlyList<string> Avisos);

/// <summary>
/// Pieza común de presentación a la AEAT (contabilidad/modelos-tributarios/presentacion), para cualquier modelo que genere su fichero:
/// <list type="number">
/// <item><see cref="PrepararAsync"/>: valida con ServaliDos (devuelve el PDF oficial o los errores; no presenta nada), lee el NIF del
/// declarante del fichero, busca el certificado de la sociedad y consulta con ConsultaExt si el modelo y periodo ya están presentados
/// (por el programa o por la gestoría): si lo están, solo admite una rectificativa (complementaria).</item>
/// <item><see cref="PresentarAsync"/>: con la conformidad del operador, presenta con PresBasicaDos en el entorno de la BD (una BD de
/// pruebas solo envía al entorno de pruebas de la AEAT y producción solo a producción), guarda la respuesta en PresentacionesAeat y
/// archiva el PDF justificante en la carpeta de documentos de la empresa. Los errores de la AEAT se devuelven sin guardar nada.</item>
/// </list>
/// </summary>
public sealed class ServicioPresentacionAeat(IBaseDatos bd, IClienteAeat cliente, Func<string, Task<X509Certificate2?>>? certificado = null, IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>Entorno de la AEAT de esta BD: el de pruebas si la BD es la copia de pruebas (Configuracion.EntornoPruebas), producción si no.</summary>
    public Task<EntornoAeat> EntornoAsync(CancellationToken cancelacion = default) => bd.EjecutarAsync(async s =>
        s.LeerEsquema().ExisteColumna("Configuracion", "EntornoPruebas") && await s.EscalarAsync("SELECT TOP 1 EntornoPruebas FROM Configuracion") is bool b && b
            ? EntornoAeat.Pruebas : EntornoAeat.Produccion, cancelacion);

    private Task<X509Certificate2?> CertificadoAsync(string idSociedad) =>
        certificado is not null ? certificado(idSociedad) : CertificadosAeat.DeSociedadAsync(bd, idSociedad);

    /// <summary>Solo la validación con ServaliDos: el PDF del modelo («Ver el PDF»). No presenta nada ni necesita certificado.</summary>
    public Task<ValidacionAeat> ValidarAsync(DeclaracionAeat d, CancellationToken cancelacion = default) =>
        cliente.ValidarAsync(d.Modelo, d.Ejercicio.ToString(CultureInfo.InvariantCulture), d.Periodo, d.Fichero, cancelacion);

    public async Task<BorradorAeat> PrepararAsync(ContextoSociedadEjercicio ctx, DeclaracionAeat d, CancellationToken cancelacion = default)
    {
        var bloqueos = new List<string>();
        var avisos = new List<string>();
        var entorno = await EntornoAsync(cancelacion);
        ValidacionAeat validacion;
        try { validacion = await ValidarAsync(d, cancelacion); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            validacion = new ValidacionAeat(null, [], [$"No he podido conectar con el servicio de validación de la AEAT: {ex.Message} Comprueba la conexión a internet y vuelve a intentarlo."]);
        }
        foreach (var e in validacion.Errores) bloqueos.Add($"La AEAT rechaza el fichero: {e}");
        if (d.FormaPago == 'I' && DeclaracionAeat.ErrorNrc(d.Nrc) is { } nrc) bloqueos.Add(nrc);

        var nif = d.LeerNif(d.Fichero);
        if (!string.Equals(nif, d.NifSociedad.Trim(), StringComparison.OrdinalIgnoreCase))
            bloqueos.Add($"El NIF del declarante del fichero ({nif ?? "ninguno"}) no es el de la sociedad ({d.NifSociedad}). Revisa el NIF en Configuración › Parámetros y tablas › Empresas.");

        TitularCertificado? titular = null;
        var presentadas = new List<DeclaracionPresentadaAeat>();
        using var cert = await CertificadoAsync(ctx.IdSociedad);
        if (cert is null)
            bloqueos.Add("La sociedad no tiene certificado en este equipo. Elígelo en Configuración › Parámetros y tablas › «Certificado de la empresa» (el mismo de VeriFactu).");
        else
        {
            titular = TitularCertificado.De(cert);
            if (titular.Caduca < _reloj.Ahora) bloqueos.Add($"El certificado de la sociedad caducó el {titular.Caduca:dd/MM/yyyy}: renuévalo y vuelve a elegirlo en Parámetros y tablas.");
            try
            {
                var consulta = await cliente.ConsultarAsync(entorno, cert, d.NifSociedad, d.RazonSocial, d.Modelo, d.Ejercicio.ToString(CultureInfo.InvariantCulture), d.Periodo, cancelacion);
                if (consulta.Correcta) presentadas.AddRange(consulta.Presentadas.Where(p => string.Equals(p.Periodo, d.Periodo, StringComparison.OrdinalIgnoreCase)));
                else bloqueos.Add($"No he podido comprobar en la AEAT si el {d.Modelo} del {d.Periodo} ya está presentado: {consulta.Error} Vuelve a intentarlo más tarde.");
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                bloqueos.Add($"No he podido comprobar en la AEAT si el {d.Modelo} del {d.Periodo} ya está presentado ({ex.Message}). " +
                    (entorno == EntornoAeat.Pruebas ? "El entorno de pruebas de la AEAT solo funciona los días laborables de 8:00 a 15:00." : "Comprueba la conexión a internet y vuelve a intentarlo."));
            }
        }
        // También lo presentado desde el programa (por si la consulta no lo devuelve todavía).
        var locales = (await PresentadasAsync(ctx.IdSociedad, d.Modelo, d.Ejercicio, cancelacion)).Where(p => p.Periodo == d.Periodo && p.Entorno == ServiciosAeat.Codigo(entorno)).ToList();
        foreach (var l in locales.Where(l => presentadas.All(p => p.Justificante != l.Justificante)))
            presentadas.Add(new DeclaracionPresentadaAeat(d.Ejercicio.ToString(CultureInfo.InvariantCulture), d.Modelo, d.Periodo, d.NifSociedad, l.Csv, l.Expediente ?? "", l.Justificante,
                l.ComplementariaDe, l.FechaHora));
        var ultima = presentadas.OrderByDescending(p => p.FechaHora ?? DateTime.MinValue).FirstOrDefault();
        if (d.ComplementariaDe is null && ultima is not null)
            bloqueos.Add($"El {d.Modelo} del {d.Periodo} {d.Ejercicio} ya está presentado (justificante {ultima.Justificante}" +
                (ultima.FechaHora is { } f ? $", {f:dd/MM/yyyy HH:mm}" : "") + "). Solo se puede presentar una rectificativa: usa «Presentar complementaria…».");
        if (d.ComplementariaDe is { } anterior && presentadas.Count > 0 && presentadas.All(p => p.Justificante != anterior))
            avisos.Add($"La AEAT no devuelve el justificante {anterior} entre lo presentado del {d.Periodo}; la última presentada es la {ultima?.Justificante}.");
        if (d.ComplementariaDe is not null && presentadas.Count == 0)
            avisos.Add($"No consta ninguna presentación anterior del {d.Modelo} del {d.Periodo}: la rectificativa necesita el justificante de la anterior.");
        foreach (var a in validacion.Avisos) avisos.Add($"Aviso de la AEAT: {a}");
        if (d.FormaPago == 'U')
            avisos.Add("Si la cuenta de domiciliación es de una entidad que no colabora con la AEAT, el cargo puede tener comisiones.");
        return new BorradorAeat(d, entorno, validacion, nif, titular, presentadas, bloqueos, avisos);
    }

    /// <summary>
    /// Presenta el borrador con la conformidad del operador. Antes de enviar vuelve a comprobar el entorno de la BD y que el borrador no
    /// tiene bloqueos. Si la AEAT rechaza al presentador (FIRNIF) se prueba con el otro titular del certificado: un rechazo no graba nada.
    /// </summary>
    public async Task<Resultado<PresentacionHecha>> PresentarAsync(ContextoSociedadEjercicio ctx, BorradorAeat b, bool conforme, Actor actor, CancellationToken cancelacion = default)
    {
        var d = b.Declaracion;
        var registro = d.Titulo;
        Resultado<PresentacionHecha> Fallo(string mensaje, string? causa, string? solucion) =>
            Resultado<PresentacionHecha>.Fallo(new Error("Presentacion.NoPresentada", mensaje, registro, causa, solucion));
        if (!conforme) return Fallo("No se ha presentado: falta tu conformidad.", "La AEAT entiende que el propio envío acepta los datos.", "Revisa el PDF y marca «Conforme».");
        if (!b.SePuedePresentar) return Fallo("No se puede presentar.", string.Join(" ", b.Bloqueos), "Resuelve lo que se indica y vuelve a abrir la presentación.");
        var entorno = await EntornoAsync(cancelacion);
        if (entorno != b.Entorno)
            return Fallo("El entorno de la base de datos ha cambiado desde que abriste la presentación.", null, "Cierra la ventana y vuelve a empezar.");
        using var cert = await CertificadoAsync(ctx.IdSociedad);
        if (cert is null) return Fallo("La sociedad no tiene certificado en este equipo.", null, "Elígelo en Configuración › Parámetros y tablas › «Certificado de la empresa».");
        var titular = TitularCertificado.De(cert);
        RespuestaPresentacionAeat? r = null;
        string? firNif = null, firNombre = null;
        foreach (var (nif, nombre) in titular.Firmantes)
        {
            try
            {
                r = await cliente.PresentarAsync(entorno, cert, new PeticionPresentacionAeat(d.Modelo, d.Ejercicio.ToString(CultureInfo.InvariantCulture), d.Periodo, d.Fichero, nif, nombre,
                    d.FormaPago == 'I' ? d.Nrc?.Trim().ToUpperInvariant() : null), cancelacion);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return Fallo($"No he podido conectar con la AEAT ({ex.Message}).",
                    "No consta que se haya presentado, pero si la conexión se cortó después de enviar, la AEAT podría haberlo recibido.",
                    "Antes de repetirlo, pulsa «Actualizar» en la pantalla del modelo: consulta en la AEAT lo presentado. " +
                    (entorno == EntornoAeat.Pruebas ? "El entorno de pruebas solo funciona los días laborables de 8:00 a 15:00." : ""));
            }
            firNif = nif;
            firNombre = nombre;
            if (r.Correcta || !r.Errores.Any(e => e.Contains("FIRNIF", StringComparison.OrdinalIgnoreCase) || e.Contains("presentador", StringComparison.OrdinalIgnoreCase))) break;
        }
        if (r is null) return Fallo("El certificado no tiene NIF de titular.", null, "Elige otro certificado en Parámetros y tablas.");
        if (!r.Correcta)
            return Resultado<PresentacionHecha>.Fallo(new Error("Presentacion.Rechazada", $"La AEAT no ha aceptado la presentación ({r.Errores.Count} error{(r.Errores.Count == 1 ? "" : "es")}): no se ha presentado nada.",
                registro, string.Join(Environment.NewLine, r.Errores), "Corrige lo que indica la AEAT y vuelve a presentar.", r.Json));

        // Aceptada: se guarda pase lo que pase con el PDF.
        var avisos = r.Avisos.Concat(r.Advertencias).ToList();
        var propios = new List<string>();
        var fecha = r.FechaHora ?? _reloj.Ahora;
        var guardada = new PresentacionGuardada(0, ctx.IdSociedad, d.Modelo, d.Ejercicio, d.Periodo, ServiciosAeat.Codigo(entorno), fecha, r.Csv!, r.Expediente, r.Justificante ?? "",
            d.ComplementariaDe, r.FormaPago ?? d.FormaPago.ToString(), r.ImporteAIngresar ?? d.ImporteAIngresar, d.Resultado, firNif, Recortar(r.Presentador ?? firNombre, 120),
            Recortar(actor.ToString(), 100), avisos.Count == 0 ? null : string.Join(Environment.NewLine, avisos), r.UrlPdf, null);
        int id;
        try
        {
            id = await GuardarAsync(guardada, d.Fichero, r.Json);
        }
        catch (Exception ex)
        {
            // La AEAT ya la tiene: que el operador no la pierda.
            var copia = await CopiaDeSeguridadAsync(d, r, fecha);
            return Resultado<PresentacionHecha>.Fallo(new Error("Presentacion.NoGuardada",
                $"El {d.Modelo} se ha presentado (CSV {r.Csv}, justificante {r.Justificante}), pero no he podido guardarlo en la base de datos.", registro, ex.Message,
                $"Guarda estos datos. La respuesta de la AEAT está en {copia}.", r.Json));
        }
        guardada = guardada with { Id = id };
        if (!string.IsNullOrWhiteSpace(r.UrlPdf))
        {
            try
            {
                var pdf = await cliente.DescargarAsync(r.UrlPdf, cert, cancelacion);
                var ruta = await JustificantesModelos.ArchivarAsync(bd, actor.ToString(), _reloj, ctx.IdSociedad, d.Modelo, d.Ejercicio, d.Periodo, r.Justificante ?? r.Csv!, entorno,
                    fecha, id, pdf, cancelacion);
                guardada = guardada with { RutaPdf = ruta };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                propios.Add($"Presentado, pero no he podido descargar o archivar el PDF justificante ({ex.Message}). Se puede volver a intentar con «Abrir el justificante».");
            }
        }
        else propios.Add("La AEAT no ha dado la dirección del PDF justificante" + (entorno == EntornoAeat.Pruebas ? " (en el entorno de pruebas puede no estar disponible)." : "."));
        return Resultado<PresentacionHecha>.Correcto(new PresentacionHecha(guardada, [.. avisos.Select(a => $"Aviso de la AEAT: {a}"), .. propios]));
    }

    /// <summary>Vuelve a descargar y archivar el PDF justificante de una presentación guardada (si no se pudo al presentar).</summary>
    public async Task<Resultado<string>> ArchivarJustificanteAsync(ContextoSociedadEjercicio ctx, PresentacionGuardada p, CancellationToken cancelacion = default)
    {
        if (p.RutaPdf is { } existente && File.Exists(existente)) return Resultado<string>.Correcto(existente);
        if (string.IsNullOrWhiteSpace(p.UrlPdf))
            return Resultado<string>.Fallo(new Error("Presentacion.SinPdf", "La AEAT no dio la dirección del PDF justificante de esta presentación.", $"Justificante {p.Justificante}", null,
                $"Descárgalo en la Sede de la AEAT con el CSV {p.Csv}."));
        using var cert = await CertificadoAsync(ctx.IdSociedad);
        try
        {
            var pdf = await cliente.DescargarAsync(p.UrlPdf, cert, cancelacion);
            var entorno = p.EsPruebas ? EntornoAeat.Pruebas : EntornoAeat.Produccion;
            var ruta = await JustificantesModelos.ArchivarAsync(bd, p.Operador ?? "programa", _reloj, ctx.IdSociedad, p.Modelo, p.Ejercicio, p.Periodo, p.Justificante, entorno,
                p.FechaHora, p.Id, pdf, cancelacion);
            return Resultado<string>.Correcto(ruta);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return Resultado<string>.Fallo(new Error("Presentacion.SinPdf", "No he podido descargar el PDF justificante.", $"Justificante {p.Justificante}", ex.Message,
                $"Vuelve a intentarlo o descárgalo en la Sede de la AEAT con el CSV {p.Csv}."));
        }
    }

    // ---------- consulta de lo presentado (pantalla de modelos) ----------

    /// <summary>
    /// ConsultaExt de un modelo y ejercicio (todos sus periodos) con el certificado de la sociedad, en el entorno de la BD: lo presentado,
    /// también lo que se presentó fuera del programa. Sin certificado, el error «Presentacion.SinCertificado».
    /// </summary>
    public async Task<Resultado<(IReadOnlyList<DeclaracionPresentadaAeat> Presentadas, EntornoAeat Entorno)>> ConsultarPresentadasAsync(string idSociedad, string modelo, int ejercicio,
        CancellationToken cancelacion = default)
    {
        var registro = $"Modelo {modelo} {ejercicio}";
        var entorno = await EntornoAsync(cancelacion);
        var (nif, nombre) = await bd.EjecutarAsync(async s => await s.ConsultarPrimeroAsync("SELECT CIF, Nombre FROM Sociedades WHERE IdSociedad = ?",
            f => ((f.Texto("CIF") ?? "").Trim().ToUpperInvariant(), (f.Texto("Nombre") ?? "").Trim()), idSociedad), cancelacion);
        using var cert = await CertificadoAsync(idSociedad);
        if (cert is null)
            return Resultado<(IReadOnlyList<DeclaracionPresentadaAeat>, EntornoAeat)>.Fallo(new Error("Presentacion.SinCertificado", "La sociedad no tiene certificado en este equipo.", registro, null,
                "Elígelo en Configuración › Parámetros y tablas › «Certificado de la empresa» (el mismo de VeriFactu)."));
        try
        {
            var r = await cliente.ConsultarAsync(entorno, cert, nif, nombre, modelo, ejercicio.ToString(CultureInfo.InvariantCulture), null, cancelacion);
            return r.Correcta ? Resultado<(IReadOnlyList<DeclaracionPresentadaAeat>, EntornoAeat)>.Correcto((r.Presentadas, entorno))
                : Resultado<(IReadOnlyList<DeclaracionPresentadaAeat>, EntornoAeat)>.Fallo(new Error("Presentacion.Consulta", $"La AEAT no ha respondido a la consulta del {modelo}.", registro, r.Error,
                    "Vuelve a intentarlo más tarde."));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Resultado<(IReadOnlyList<DeclaracionPresentadaAeat>, EntornoAeat)>.Fallo(new Error("Presentacion.Consulta", $"No he podido consultar a la AEAT el {modelo}.", registro, ex.Message,
                entorno == EntornoAeat.Pruebas ? "El entorno de pruebas de la AEAT solo funciona los días laborables de 8:00 a 15:00." : "Comprueba la conexión a internet y vuelve a intentarlo."));
        }
    }

    /// <summary>
    /// Guarda una presentación hecha fuera del programa que devuelve la consulta de la AEAT (origen AEAT): sin importe, sin fichero y sin
    /// firmante, que la consulta no da. Si la AEAT no da la fecha, la de ahora.
    /// </summary>
    public async Task<PresentacionGuardada> GuardarExternaAsync(string idSociedad, DeclaracionPresentadaAeat p, EntornoAeat entorno, CancellationToken cancelacion = default)
    {
        var ejercicio = int.TryParse(p.Ejercicio, NumberStyles.None, CultureInfo.InvariantCulture, out var e) ? e : _reloj.Ahora.Year;
        var g = new PresentacionGuardada(0, idSociedad, p.Modelo.Trim(), ejercicio, p.Periodo.Trim().ToUpperInvariant(), ServiciosAeat.Codigo(entorno), p.FechaHora ?? _reloj.Ahora,
            p.Csv.Trim(), string.IsNullOrWhiteSpace(p.Expediente) ? null : p.Expediente.Trim(), p.Justificante.Trim(), p.JustificanteAnterior, null, 0m, 0m, null, null,
            "Consulta a la AEAT", "Presentada fuera del programa: la consulta de la AEAT no da su importe ni su fichero.", null, null, PresentacionGuardada.OrigenAeat);
        var id = await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO PresentacionesAeat (IdSociedad, Modelo, Ejercicio, Periodo, Entorno, FechaHora, Csv, Expediente, Justificante, ComplementariaDe, FormaPago, Importe, Resultado, " +
                "NifPresentador, Presentador, Operador, Fichero, Respuesta, Avisos, UrlPdf, RutaPdf, Origen) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                g.IdSociedad, Recortar(g.Modelo, 3), (short)g.Ejercicio, Recortar(g.Periodo, 2), g.Entorno, g.FechaHora, Recortar(g.Csv, 16), Recortar(g.Expediente, 25), Recortar(g.Justificante, 13),
                Recortar(g.ComplementariaDe, 13), null, 0m, 0m, null, null, g.Operador, "(presentada fuera del programa)", null, g.Avisos, null, null, PresentacionGuardada.OrigenAeat);
            return await s.UltimoIdAsync();
        }, cancelacion);
        return g with { Id = id };
    }

    // ---------- lecturas y escrituras ----------

    public Task<IReadOnlyList<PresentacionGuardada>> PresentadasAsync(string idSociedad, string modelo, int ejercicio, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s => s.LeerEsquema().ExisteTabla("PresentacionesAeat")
            ? await s.ConsultarAsync(
                "SELECT IdPresentacionAeat, IdSociedad, Modelo, Ejercicio, Periodo, Entorno, FechaHora, Csv, Expediente, Justificante, ComplementariaDe, FormaPago, Importe, Resultado, " +
                "NifPresentador, Presentador, Operador, Avisos, UrlPdf, RutaPdf" + (s.LeerEsquema().ExisteColumna("PresentacionesAeat", "Origen") ? ", Origen" : ", Null AS Origen") +
                " FROM PresentacionesAeat WHERE IdSociedad = ? AND Modelo = ? AND Ejercicio = ? ORDER BY FechaHora, IdPresentacionAeat",
                f => new PresentacionGuardada(f.Entero("IdPresentacionAeat"), f.Texto("IdSociedad") ?? "", f.Texto("Modelo") ?? "", f.Entero("Ejercicio"), f.Texto("Periodo") ?? "",
                    f.Texto("Entorno") ?? "", f.Fecha("FechaHora"), f.Texto("Csv") ?? "", f.Texto("Expediente"), f.Texto("Justificante") ?? "", f.Texto("ComplementariaDe"),
                    f.Texto("FormaPago"), f.DecimalNulo("Importe") ?? 0m, f.DecimalNulo("Resultado") ?? 0m, f.Texto("NifPresentador"), f.Texto("Presentador"), f.Texto("Operador"),
                    f.Texto("Avisos"), f.Texto("UrlPdf"), f.Texto("RutaPdf"), f.Texto("Origen")),
                idSociedad, modelo, (short)ejercicio)
            : (IReadOnlyList<PresentacionGuardada>)[], cancelacion);

    private Task<int> GuardarAsync(PresentacionGuardada p, string fichero, string respuesta) => bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync(
            "INSERT INTO PresentacionesAeat (IdSociedad, Modelo, Ejercicio, Periodo, Entorno, FechaHora, Csv, Expediente, Justificante, ComplementariaDe, FormaPago, Importe, Resultado, " +
            "NifPresentador, Presentador, Operador, Fichero, Respuesta, Avisos, UrlPdf, RutaPdf) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            p.IdSociedad, p.Modelo, (short)p.Ejercicio, p.Periodo, p.Entorno, p.FechaHora, Recortar(p.Csv, 16), Recortar(p.Expediente, 25), Recortar(p.Justificante, 13),
            Recortar(p.ComplementariaDe, 13), Recortar(p.FormaPago, 1), p.Importe, p.Resultado, Recortar(p.NifPresentador, 9), p.Presentador, p.Operador, fichero, respuesta, p.Avisos,
            Recortar(p.UrlPdf, 255), null);
        var id = await s.UltimoIdAsync();
        await MarcarOrigenProgramaAsync(s, id);
        return id;
    });

    /// <summary>Anota que la presentación se hizo desde el programa (esquema 73; sin la columna no hace nada).</summary>
    internal static async Task MarcarOrigenProgramaAsync(ISesionDatos s, int id)
    {
        if (s.LeerEsquema().ExisteColumna("PresentacionesAeat", "Origen"))
            await s.EjecutarAsync("UPDATE PresentacionesAeat SET Origen = ? WHERE IdPresentacionAeat = ?", PresentacionGuardada.OrigenPrograma, id);
    }

    /// <summary>
    /// Ruta del PDF justificante: carpeta de documentos de la empresa (Sociedades.RutaDocumentos; relativa, junto a la BD) \ ejercicio \
    /// «Modelos AEAT» \ modelo, con el nombre «303 2026 1T 3030000000000.pdf»; en el entorno de pruebas, con «PRUEBAS» delante.
    /// Sin carpeta de documentos, junto a la BD.
    /// </summary>
    /// <summary>Carpeta de la empresa: la ruta completa tal cual; una relativa o vacía, junto a la BD.</summary>
    public static string CarpetaEmpresa(string rutaBd, string? rutaDocumentos)
    {
        var dirBd = Path.GetDirectoryName(Path.GetFullPath(rutaBd)) ?? "";
        if (string.IsNullOrWhiteSpace(rutaDocumentos)) return dirBd;
        var r = rutaDocumentos.Trim();
        return Path.IsPathFullyQualified(r) ? r : Path.GetFullPath(Path.Combine(dirBd, r));
    }

    private async Task<string> CopiaDeSeguridadAsync(DeclaracionAeat d, RespuestaPresentacionAeat r, DateTime fecha)
    {
        var carpeta = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(bd.Ruta)) ?? Path.GetTempPath(), "Presentaciones AEAT sin guardar");
        try
        {
            Directory.CreateDirectory(carpeta);
            var ruta = Path.Combine(carpeta, $"{d.Modelo} {d.Ejercicio} {d.Periodo} {fecha:yyyyMMdd-HHmmss}.json");
            await File.WriteAllTextAsync(ruta, r.Json);
            await File.WriteAllTextAsync(Path.ChangeExtension(ruta, ".txt"), d.Fichero);
            return ruta;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"ninguna parte (tampoco he podido escribir en {carpeta}): apunta el CSV {r.Csv} y el justificante {r.Justificante}";
        }
    }

    private static string? Recortar(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
