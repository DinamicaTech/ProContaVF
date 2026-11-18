using ProConta.Nucleo.Resultados;

namespace ProConta.Documentos;

/// <summary>
/// La capa común por la que guardan sus ficheros los módulos que producen documentos (facturas, justificantes de modelos, actas, datos fiscales,
/// adjuntos del correo, nóminas, informe para la gestoría; documentos 2026-11-18). El productor decide el nombre y la subcarpeta; el gestor:
/// <list type="bullet">
/// <item>si ya hay en la sociedad un fichero con el mismo contenido para ese documento, no guarda otro (devuelve el existente, <see cref="DocumentoGuardado.YaExistia"/>);</item>
/// <item>con <see cref="SolicitudDocumento.NuevaVersionDe"/>, guarda una versión nueva del mismo documento sin tocar la anterior;</item>
/// <item>si el nombre está ocupado por otro contenido, añade « (2)», « (3)»…; nunca sobrescribe;</item>
/// <item>limpia el nombre de lo que Windows no admite y recorta rutas demasiado largas;</item>
/// <item>registra la ruta relativa, el tamaño, la huella y los vínculos que le pasa el productor.</item>
/// </list>
/// Los productores no comprueban el permiso «Documentos»: guardar el resultado de su proceso es parte de ese proceso.
/// </summary>
public sealed partial class ServicioDocumentos
{
    /// <summary>Guarda un documento producido por un módulo a partir de su contenido (por ejemplo, el PDF recién generado).</summary>
    public async Task<Resultado<DocumentoGuardado>> GuardarAsync(SolicitudDocumento solicitud, byte[] contenido, CancellationToken cancelacion = default)
    {
        var raiz = await RaizProductorAsync(solicitud.IdSociedad);
        var carpeta = Carpeta(raiz, solicitud.Subcarpeta);
        if (solicitud is { NuevaVersionDe: null, VersionSiMismoNombre: true } &&
            await DocumentoEnRutaAsync(solicitud.IdSociedad, RutasDocumentos.ParaGuardar(raiz, Path.Combine(carpeta, RutasDocumentos.NombreLimpio(solicitud.NombreFichero)))) is int previo)
            solicitud = solicitud with { NuevaVersionDe = previo };
        var huella = RutasDocumentos.Huella(contenido);
        if (await MismoContenidoAsync(solicitud, huella, raiz) is { } ya) return Resultado<DocumentoGuardado>.Correcto(ya);
        string? destino = null;
        try
        {
            Directory.CreateDirectory(carpeta);
            destino = RutasDocumentos.RutaLibre(carpeta, solicitud.NombreFichero);
            // Primero a un temporal en la misma carpeta: un fallo a medias no deja un fichero cortado con el nombre definitivo.
            var parcial = destino + ".parcial";
            await File.WriteAllBytesAsync(parcial, contenido, cancelacion);
            File.Move(parcial, destino);
            return await RegistrarFicheroAsync(solicitud, raiz, destino, Path.GetFileName(destino), contenido.LongLength, huella, cancelacion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException or InvalidOperationException)
        {
            if (destino is not null) try { File.Delete(destino); File.Delete(destino + ".parcial"); } catch (IOException) { }
            return Resultado<DocumentoGuardado>.Fallo(NoGuardado(solicitud, ex));
        }
    }

    /// <summary>
    /// Registra un fichero que ya está en su sitio y no se mueve (por ejemplo, el PDF de una factura recibida en la carpeta de facturas del ejercicio).
    /// Si ese mismo fichero ya está registrado, devuelve el existente.
    /// </summary>
    public async Task<Resultado<DocumentoGuardado>> RegistrarAsync(SolicitudDocumento solicitud, string rutaExistente, CancellationToken cancelacion = default)
    {
        var raiz = await RaizProductorAsync(solicitud.IdSociedad);
        if (!File.Exists(rutaExistente))
            return Resultado<DocumentoGuardado>.Fallo(new Error("Documentos.SinFichero", $"No encuentro el fichero {rutaExistente}.", solicitud.Titulo, null,
                "Comprueba que la carpeta está accesible desde este equipo y vuelve a intentarlo."));
        try
        {
            var ruta = RutasDocumentos.ParaGuardar(raiz, rutaExistente);
            var existente = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync<(int Doc, int Ver, int Num)?>(
                "SELECT TOP 1 v.IdDocumento, v.IdVersionDocumento, v.NumeroVersion FROM DocumentosVersiones v INNER JOIN Documentos d ON v.IdDocumento = d.IdDocumento " +
                "WHERE d.IdSociedad = ? AND v.Ruta = ? ORDER BY v.NumeroVersion DESC",
                f => (f.Entero("IdDocumento"), f.Entero("IdVersionDocumento"), f.Entero("NumeroVersion")), solicitud.IdSociedad, ruta), cancelacion);
            if (existente is { } e)
            {
                await AnadirVinculosAsync(e.Doc, solicitud.Vinculos);
                return Resultado<DocumentoGuardado>.Correcto(new DocumentoGuardado(e.Doc, e.Ver, e.Num, ruta, rutaExistente, true));
            }
            var huella = await RutasDocumentos.HuellaAsync(rutaExistente, cancelacion);
            return await RegistrarFicheroAsync(solicitud, raiz, rutaExistente, Path.GetFileName(rutaExistente), new FileInfo(rutaExistente).Length, huella, cancelacion);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException or InvalidOperationException)
        {
            return Resultado<DocumentoGuardado>.Fallo(NoGuardado(solicitud, ex));
        }
    }

    /// <summary>
    /// Carpeta raíz para los productores: la de documentos de la empresa o, si está vacía, la que contiene la BD (lo que ya hacían los módulos
    /// antes del gestor, para que nada deje de guardarse por no tenerla configurada).
    /// </summary>
    private async Task<string> RaizProductorAsync(string idSociedad) =>
        await RaizAsync(idSociedad) ?? Path.GetDirectoryName(Path.GetFullPath(_bd.Ruta)) ?? Path.GetTempPath();

    /// <summary>Documento de la sociedad que tiene alguna versión en esa ruta (relativa a la raíz, como se guarda); null si no hay.</summary>
    private Task<int?> DocumentoEnRutaAsync(string idSociedad, string ruta) => _bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync<int?>("SELECT TOP 1 v.IdDocumento FROM DocumentosVersiones v INNER JOIN Documentos d ON v.IdDocumento = d.IdDocumento " +
                                            "WHERE d.IdSociedad = ? AND v.Ruta = ? ORDER BY v.IdVersionDocumento DESC", f => f.Entero("IdDocumento"), idSociedad, ruta));

    /// <summary>
    /// Fichero vigente (la versión más nueva) del documento que tuvo alguna versión en <paramref name="rutaCompleta"/>: así un productor que
    /// guardaba siempre en la misma ruta lee la última versión. Null si esa ruta no está en el gestor.
    /// </summary>
    public async Task<string?> VigenteDeAsync(string idSociedad, string rutaCompleta)
    {
        var raiz = await RaizProductorAsync(idSociedad);
        if (await DocumentoEnRutaAsync(idSociedad, RutasDocumentos.ParaGuardar(raiz, rutaCompleta)) is not int id) return null;
        return (await VersionesAsync(id)).FirstOrDefault()?.RutaCompleta;
    }

    /// <summary>
    /// Para ficheros que vive y mueve otro módulo (el PDF de una factura recibida, que Compras mueve a la carpeta del proveedor o parte en dos):
    /// deja el gestor al día con el fichero de <paramref name="rutaActual"/> como versión vigente del documento de <paramref name="clave"/>. Sin
    /// documento, lo registra; con el mismo contenido en otra ruta, solo actualiza la ruta (se ha movido); con otro contenido, guarda una versión
    /// nueva. Los vínculos de los tipos que trae la solicitud se dejan como ella dice (por ejemplo, el proveedor si ha cambiado); los demás se
    /// conservan.
    /// </summary>
    public async Task<Resultado<DocumentoGuardado>> SincronizarAsync(SolicitudDocumento solicitud, string rutaActual, Vinculo clave, CancellationToken cancelacion = default)
    {
        if (!File.Exists(rutaActual))
            return Resultado<DocumentoGuardado>.Fallo(new Error("Documentos.SinFichero", $"No encuentro el fichero {rutaActual}.", solicitud.Titulo, null,
                "Comprueba que la carpeta está accesible desde este equipo y vuelve a intentarlo."));
        var raiz = await RaizProductorAsync(solicitud.IdSociedad);
        var (filtro, p) = FiltroDestino(clave);
        var id = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync<int?>(
            $"SELECT TOP 1 v.IdDocumento FROM DocumentosVinculos v INNER JOIN Documentos d ON v.IdDocumento = d.IdDocumento WHERE d.IdSociedad = ? AND d.Tipo = ? AND {filtro} " +
            "ORDER BY v.IdDocumento DESC", f => f.Entero("IdDocumento"), [solicitud.IdSociedad, solicitud.Tipo, .. p]), cancelacion);
        var vinculos = solicitud.Vinculos.Any(v => v.MismoDestino(clave)) ? solicitud.Vinculos : [clave, .. solicitud.Vinculos];
        if (id is not int idDoc) return await RegistrarAsync(solicitud with { Vinculos = vinculos }, rutaActual, cancelacion);
        try
        {
            var ruta = RutasDocumentos.ParaGuardar(raiz, rutaActual);
            var huella = await RutasDocumentos.HuellaAsync(rutaActual, cancelacion);
            var vigente = (await VersionesAsync(idDoc)).FirstOrDefault();
            DocumentoGuardado guardado;
            if (vigente is not null && vigente.Huella == huella)
            {
                if (!string.Equals(vigente.Ruta, ruta, StringComparison.OrdinalIgnoreCase))
                    await _bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE DocumentosVersiones SET Ruta = ? WHERE IdVersionDocumento = ?", Recortar(ruta, 255), vigente.IdVersionDocumento), cancelacion);
                guardado = new DocumentoGuardado(idDoc, vigente.IdVersionDocumento, vigente.NumeroVersion, ruta, rutaActual, true);
            }
            else
            {
                var r = await RegistrarFicheroAsync(solicitud with { NuevaVersionDe = idDoc, Vinculos = [] }, raiz, rutaActual, Path.GetFileName(rutaActual),
                    new FileInfo(rutaActual).Length, huella, cancelacion);
                if (!r.EsCorrecto) return r;
                guardado = r.Valor;
            }
            await _bd.EnTransaccionAsync(async s =>
            {
                await s.EjecutarAsync("UPDATE Documentos SET Titulo = ?, FechaDocumento = ?, Ejercicio = ?, FechaModificacion = ?, OperadorModificacion = ? WHERE IdDocumento = ?",
                    Recortar(solicitud.Titulo, 200), solicitud.FechaDocumento, (object?)(solicitud.Ejercicio ?? EjercicioDe(vinculos, solicitud.FechaDocumento)), _reloj.Ahora, _operador, idDoc);
                var actuales = await s.ConsultarAsync("SELECT * FROM DocumentosVinculos WHERE IdDocumento = ?", VinculoDe, idDoc);
                var tipos = vinculos.Select(v => v.TipoEntidad).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var v in actuales.Where(a => tipos.Contains(a.TipoEntidad) && !vinculos.Any(n => n.MismoDestino(a)))) await BorrarVinculoAsync(s, idDoc, v);
                foreach (var v in vinculos.Where(n => !actuales.Any(a => a.MismoDestino(n)))) await InsertarVinculoAsync(s, idDoc, v);
                return true;
            }, cancelacion);
            return Resultado<DocumentoGuardado>.Correcto(guardado);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException or InvalidOperationException)
        {
            return Resultado<DocumentoGuardado>.Fallo(NoGuardado(solicitud, ex));
        }
    }

    /// <summary>
    /// Registra un adjunto de correo que ya está en su carpeta (documentos 2026-11-18, parte d): si su contenido ya es un documento de la sociedad
    /// (por ejemplo, el PDF de la factura que se envía), solo le añade los vínculos de la solicitud (el mensaje), sin duplicarlo; si no, lo registra
    /// donde está.
    /// </summary>
    public async Task<Resultado<DocumentoGuardado>> RegistrarAdjuntoAsync(SolicitudDocumento solicitud, string rutaCompleta, string huella, CancellationToken cancelacion = default)
    {
        var existente = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync<(int Doc, int Ver, int Num, string Ruta)?>(
            "SELECT TOP 1 v.IdDocumento, v.IdVersionDocumento, v.NumeroVersion, v.Ruta FROM DocumentosVersiones v INNER JOIN Documentos d ON v.IdDocumento = d.IdDocumento " +
            "WHERE d.IdSociedad = ? AND v.Huella = ? ORDER BY v.IdVersionDocumento", f => (f.Entero("IdDocumento"), f.Entero("IdVersionDocumento"), f.Entero("NumeroVersion"), f.Texto("Ruta") ?? ""),
            solicitud.IdSociedad, huella.ToUpperInvariant()), cancelacion);
        if (existente is { } e)
        {
            await AnadirVinculosAsync(e.Doc, solicitud.Vinculos);
            var raiz = await RaizProductorAsync(solicitud.IdSociedad);
            return Resultado<DocumentoGuardado>.Correcto(new DocumentoGuardado(e.Doc, e.Ver, e.Num, e.Ruta, RutasDocumentos.Completa(raiz, e.Ruta), true));
        }
        return await RegistrarAsync(solicitud, rutaCompleta, cancelacion);
    }

    /// <summary>Vincula un documento ya guardado a más registros (sin comprobar el permiso: lo usan los productores).</summary>
    public async Task AnadirVinculosAsync(int idDocumento, IReadOnlyList<Vinculo> vinculos)
    {
        if (vinculos.Count == 0) return;
        await _bd.EnTransaccionAsync(async s =>
        {
            var actuales = await s.ConsultarAsync("SELECT * FROM DocumentosVinculos WHERE IdDocumento = ?", VinculoDe, idDocumento);
            foreach (var v in vinculos.Where(n => !actuales.Any(a => a.MismoDestino(n)))) await InsertarVinculoAsync(s, idDocumento, v);
            return true;
        });
    }

    /// <summary>Documento ya guardado con ese contenido: la versión del mismo documento (nueva versión) o un documento de la sociedad con ese tipo y vínculos.</summary>
    private async Task<DocumentoGuardado?> MismoContenidoAsync(SolicitudDocumento sol, string huella, string raiz)
    {
        var candidatos = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT v.IdDocumento, v.IdVersionDocumento, v.NumeroVersion, v.Ruta FROM DocumentosVersiones v INNER JOIN Documentos d ON v.IdDocumento = d.IdDocumento " +
            "WHERE d.IdSociedad = ? AND v.Huella = ? AND d.Tipo = ?",
            f => (Doc: f.Entero("IdDocumento"), Ver: f.Entero("IdVersionDocumento"), Num: f.Entero("NumeroVersion"), Ruta: f.Texto("Ruta") ?? ""),
            sol.IdSociedad, huella, sol.Tipo));
        foreach (var c in candidatos)
        {
            if (sol.NuevaVersionDe is int doc && c.Doc != doc) continue;
            var completa = RutasDocumentos.Completa(raiz, c.Ruta);
            if (!File.Exists(completa)) continue;
            await AnadirVinculosAsync(c.Doc, sol.Vinculos);
            return new DocumentoGuardado(c.Doc, c.Ver, c.Num, c.Ruta, completa, true);
        }
        return null;
    }

    private async Task<Resultado<DocumentoGuardado>> RegistrarFicheroAsync(SolicitudDocumento sol, string raiz, string rutaCompleta, string nombreOriginal, long tamano, string huella,
        CancellationToken cancelacion)
    {
        var ruta = RutasDocumentos.ParaGuardar(raiz, rutaCompleta);
        var r = await _bd.EnTransaccionAsync(async s =>
        {
            var ahora = _reloj.Ahora;
            int idDoc, numero;
            if (sol.NuevaVersionDe is int existente &&
                Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Documentos WHERE IdDocumento = ?", existente)) > 0)
            {
                idDoc = existente;
                var maximo = await s.EscalarAsync("SELECT MAX(NumeroVersion) FROM DocumentosVersiones WHERE IdDocumento = ?", existente);
                numero = (maximo is null or DBNull ? 0 : Convert.ToInt32(maximo)) + 1;
                await s.EjecutarAsync("UPDATE Documentos SET Titulo = ?, FechaDocumento = ?, FechaModificacion = ?, OperadorModificacion = ? WHERE IdDocumento = ?",
                    Recortar(sol.Titulo, 200), sol.FechaDocumento, ahora, _operador, idDoc);
            }
            else
            {
                await s.EjecutarAsync("INSERT INTO Documentos (IdSociedad, Tipo, Titulo, FechaDocumento, Ejercicio, Estado, Origen, FechaAlta, OperadorAlta) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    sol.IdSociedad, sol.Tipo, Recortar(sol.Titulo, 200), sol.FechaDocumento, (object?)(sol.Ejercicio ?? EjercicioDe(sol.Vinculos, sol.FechaDocumento)),
                    EstadosDocumento.Archivado, sol.Origen, ahora, _operador);
                idDoc = await s.UltimoIdAsync();
                numero = 1;
            }
            var idVersion = await InsertarVersionAsync(s, idDoc, numero, ruta, nombreOriginal, tamano, huella, sol.Motivo ?? (numero == 1 ? "Alta" : $"Versión {numero}"));
            var actuales = await s.ConsultarAsync("SELECT * FROM DocumentosVinculos WHERE IdDocumento = ?", VinculoDe, idDoc);
            foreach (var v in sol.Vinculos.Where(n => !actuales.Any(a => a.MismoDestino(n)))) await InsertarVinculoAsync(s, idDoc, v);
            return new DocumentoGuardado(idDoc, idVersion, numero, ruta, rutaCompleta, false);
        }, cancelacion);
        return Resultado<DocumentoGuardado>.Correcto(r);
    }

    private static string Carpeta(string raiz, string subcarpeta) =>
        Path.IsPathFullyQualified(subcarpeta) ? subcarpeta : Path.Combine(raiz, RutasDocumentos.SubcarpetaLimpia(subcarpeta));

    private static Error NoGuardado(SolicitudDocumento s, Exception ex) => new("Documentos.NoGuardado", $"No he podido guardar «{s.Titulo}» en la carpeta de documentos: {ex.Message}",
        s.Titulo, null, "Comprueba que la carpeta de documentos de la empresa está accesible y que tienes permiso de escritura, y vuelve a intentarlo.");
}
