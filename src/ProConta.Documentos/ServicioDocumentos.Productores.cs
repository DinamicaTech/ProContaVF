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
        var raiz = await RaizAsync(solicitud.IdSociedad);
        if (raiz is null) return Resultado<DocumentoGuardado>.Fallo(SinCarpeta(solicitud.IdSociedad));
        var huella = RutasDocumentos.Huella(contenido);
        if (await MismoContenidoAsync(solicitud, huella, raiz) is { } ya) return Resultado<DocumentoGuardado>.Correcto(ya);
        var carpeta = Carpeta(raiz, solicitud.Subcarpeta);
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
        var raiz = await RaizAsync(solicitud.IdSociedad);
        if (raiz is null) return Resultado<DocumentoGuardado>.Fallo(SinCarpeta(solicitud.IdSociedad));
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
