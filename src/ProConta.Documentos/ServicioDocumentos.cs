using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Documentos;

/// <summary>
/// Gestor documental sobre Access y el disco (documentos 2026-11-18): un catálogo de los documentos de cada sociedad con sus versiones (ficheros
/// inmutables en la carpeta de documentos de la empresa) y sus vínculos. Los que se suben a mano entran en la bandeja «Por clasificar» con una
/// propuesta leída del fichero y se archivan en «Documentos\…»; los módulos productores guardan los suyos con <see cref="GuardarAsync"/> o
/// <see cref="RegistrarAsync"/>, decidiendo ellos el nombre y la subcarpeta. Nunca se sobrescribe un fichero ni se borra uno vinculado; desvincular
/// no toca el fichero.
/// </summary>
public sealed partial class ServicioDocumentos
{
    private readonly IBaseDatos _bd;
    private readonly string _operador;
    private readonly IReloj _reloj;
    private readonly Func<Task<bool>>? _puede;

    public ServicioDocumentos(IBaseDatos bd, string operador, IReloj? reloj = null, Func<Task<bool>>? puede = null)
    {
        _bd = bd;
        _operador = operador;
        _reloj = reloj ?? RelojSistema.Instancia;
        _puede = puede;
    }

    /// <summary>Si el operador puede añadir, archivar, vincular y eliminar documentos (verlos lo puede cualquiera).</summary>
    public async Task<bool> PuedeAsync() => _puede is null || await _puede();

    private static Error SinPermiso(string registro) => new("Documentos.SinPermiso", "No tienes permiso para añadir, archivar, vincular ni eliminar documentos.", registro,
        "La acción «Documentos» tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos.");

    private static Error SinCarpeta(string idSociedad) => new("Documentos.SinCarpeta", "La empresa no tiene carpeta de documentos, así que no sé dónde guardar los ficheros.",
        $"Sociedad {idSociedad}", "El campo «Carpeta de documentos» de la empresa está vacío.",
        "Escríbelo en Parámetros y tablas › Empresas › Carpeta de documentos (por ejemplo, una carpeta compartida junto a la BD) y vuelve a intentarlo.");

    private static Error NoEncontrado(int id) => new("Documentos.NoEncontrado", "No encuentro el documento: puede que otro operador lo haya eliminado.", $"Documento {id}",
        null, "Pulsa Actualizar (F5) para volver a leer la lista.");

    // ---------- carpeta y catálogo ----------

    /// <summary>Carpeta de documentos de la empresa (ruta completa) o null si no está configurada.</summary>
    public Task<string?> RaizAsync(string idSociedad) => _bd.EjecutarAsync(s => RaizAsync(s, idSociedad));

    private async Task<string?> RaizAsync(ISesionDatos s, string idSociedad)
    {
        var ruta = await s.EscalarAsync("SELECT RutaDocumentos FROM Sociedades WHERE IdSociedad = ?", idSociedad) as string;
        return string.IsNullOrWhiteSpace(ruta) ? null : RutasDocumentos.Raiz(ruta, _bd.Ruta);
    }

    public Task<IReadOnlyList<TipoDocumento>> TiposAsync() => _bd.EjecutarAsync(TiposAsync);

    private static Task<IReadOnlyList<TipoDocumento>> TiposAsync(ISesionDatos s) =>
        s.ConsultarAsync("SELECT Codigo, Nombre, Carpeta, Orden, PorAnio, Manual FROM TiposDocumento WHERE Activo = True ORDER BY Orden, Nombre",
            f => new TipoDocumento(f.Texto("Codigo")!, f.Texto("Nombre") ?? "", f.Texto("Carpeta") ?? "Otros", f.Entero("Orden"), f.Logico("PorAnio"), f.Logico("Manual")));

    // ---------- lectura ----------

    /// <summary>Documentos de la sociedad en un estado (bandeja o archivados), con su versión vigente y sus vínculos.</summary>
    public Task<IReadOnlyList<DocumentoListado>> ListarAsync(string idSociedad, string estado) => _bd.EjecutarAsync(async s =>
    {
        var raiz = await RaizAsync(s, idSociedad) ?? "";
        return await LeerDocumentosAsync(s, raiz, "d.IdSociedad = ? AND d.Estado = ?", idSociedad, estado);
    });

    /// <summary>Documentos vinculados a un registro (el panel Documentos de una ficha), archivados o por clasificar.</summary>
    public Task<IReadOnlyList<DocumentoListado>> DeAsync(string idSociedad, Vinculo destino) => _bd.EjecutarAsync(async s =>
    {
        var raiz = await RaizAsync(s, idSociedad) ?? "";
        var (filtro, p) = FiltroDestino(destino);
        var ids = await s.ConsultarAsync($"SELECT DISTINCT v.IdDocumento FROM DocumentosVinculos v INNER JOIN Documentos d ON v.IdDocumento = d.IdDocumento WHERE d.IdSociedad = ? AND {filtro}",
            f => f.Entero("IdDocumento"), [idSociedad, .. p]);
        if (ids.Count == 0) return (IReadOnlyList<DocumentoListado>)[];
        return await LeerDocumentosAsync(s, raiz, $"d.IdDocumento IN ({string.Join(",", ids)})");
    });

    /// <summary>Un documento con su versión vigente y sus vínculos; null si no existe.</summary>
    public Task<DocumentoListado?> LeerAsync(int idDocumento) => _bd.EjecutarAsync(s => LeerAsync(s, idDocumento));

    private async Task<DocumentoListado?> LeerAsync(ISesionDatos s, int idDocumento)
    {
        var soc = await s.EscalarAsync("SELECT IdSociedad FROM Documentos WHERE IdDocumento = ?", idDocumento) as string;
        if (soc is null) return null;
        var raiz = await RaizAsync(s, soc) ?? "";
        return (await LeerDocumentosAsync(s, raiz, "d.IdDocumento = ?", idDocumento)).FirstOrDefault();
    }

    /// <summary>Todas las versiones de un documento, de la más nueva a la más antigua.</summary>
    public Task<IReadOnlyList<VersionDocumento>> VersionesAsync(int idDocumento) => _bd.EjecutarAsync(async s =>
    {
        var soc = await s.EscalarAsync("SELECT IdSociedad FROM Documentos WHERE IdDocumento = ?", idDocumento) as string;
        var raiz = soc is null ? "" : await RaizAsync(s, soc) ?? "";
        return await s.ConsultarAsync("SELECT * FROM DocumentosVersiones WHERE IdDocumento = ? ORDER BY NumeroVersion DESC", f => Version(f, raiz), idDocumento);
    });

    private static (string Filtro, object?[] Parametros) FiltroDestino(Vinculo v) => v.IdEntidad is int id
        ? ("v.TipoEntidad = ? AND v.IdEntidad = ?", [v.TipoEntidad, id])
        : ("v.TipoEntidad = ? AND v.ClaveEntidad = ?", [v.TipoEntidad, v.Clave ?? ""]);

    private static VersionDocumento Version(IFilaDatos f, string raiz) => new(f.Entero("IdVersionDocumento"), f.Entero("NumeroVersion"), f.Texto("Ruta") ?? "",
        raiz.Length == 0 && !Path.IsPathFullyQualified(f.Texto("Ruta") ?? "") ? f.Texto("Ruta") ?? "" : RutasDocumentos.Completa(raiz, f.Texto("Ruta") ?? ""),
        f.Texto("NombreOriginal"), f.EnteroLargo("TamanoBytes"), f.Texto("Huella") ?? "", f.Fecha("FechaAlta"), f.Texto("Operador"), f.Texto("Motivo"));

    private static Vinculo VinculoDe(IFilaDatos f) =>
        new(f.Texto("TipoEntidad") ?? "", f.EnteroNulo("IdEntidad"), f.Texto("ClaveEntidad"), f.Texto("Etiqueta") ?? f.Texto("ClaveEntidad") ?? "");

    private static async Task<IReadOnlyList<DocumentoListado>> LeerDocumentosAsync(ISesionDatos s, string raiz, string filtro, params object?[] p)
    {
        var docs = await s.ConsultarAsync(
            "SELECT d.IdDocumento, d.IdSociedad, d.Tipo, t.Nombre AS NombreTipo, d.Titulo, d.FechaDocumento, d.Ejercicio, d.Estado, d.Origen, d.Notas, d.Propuesta, d.FechaAlta " +
            $"FROM Documentos d LEFT JOIN TiposDocumento t ON d.Tipo = t.Codigo WHERE {filtro}",
            f => (Id: f.Entero("IdDocumento"), Soc: f.Texto("IdSociedad") ?? "", Tipo: f.Texto("Tipo"), NombreTipo: f.Texto("NombreTipo"), Titulo: f.Texto("Titulo") ?? "",
                Fecha: f.FechaNula("FechaDocumento"), Ejercicio: f.EnteroNulo("Ejercicio"), Estado: f.Texto("Estado") ?? "", Origen: f.Texto("Origen") ?? "",
                Notas: f.Texto("Notas"), Propuesta: f.Texto("Propuesta"), Alta: f.Fecha("FechaAlta")), p);
        if (docs.Count == 0) return [];
        var ids = string.Join(",", docs.Select(d => d.Id));
        var versiones = (await s.ConsultarAsync($"SELECT * FROM DocumentosVersiones WHERE IdDocumento IN ({ids})", f => (Id: f.Entero("IdDocumento"), V: Version(f, raiz))))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Select(x => x.V).OrderByDescending(v => v.NumeroVersion).ToList());
        var vinculos = (await s.ConsultarAsync($"SELECT * FROM DocumentosVinculos WHERE IdDocumento IN ({ids}) ORDER BY IdVinculoDocumento",
                f => (Id: f.Entero("IdDocumento"), V: VinculoDe(f))))
            .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => (IReadOnlyList<Vinculo>)g.Select(x => x.V).ToList());
        return docs.Select(d =>
            {
                var (confianza, motivo) = Propuesta.Leer(d.Propuesta);
                var vs = versiones.GetValueOrDefault(d.Id) ?? [];
                return new DocumentoListado(d.Id, d.Soc, d.Tipo, d.NombreTipo, d.Titulo, d.Fecha, d.Ejercicio, d.Estado, d.Origen, d.Notas, confianza, motivo, d.Alta,
                    vs.FirstOrDefault(), vs.Count, vinculos.GetValueOrDefault(d.Id) ?? []);
            })
            .OrderByDescending(d => d.FechaDocumento ?? DateTime.MinValue).ThenByDescending(d => d.IdDocumento).ToList();
    }

    // ---------- bandeja ----------

    /// <summary>
    /// Añade ficheros (o carpetas, con todo lo que tengan dentro) a la bandeja «Por clasificar»: copia cada uno a «Documentos\Por clasificar», lee su
    /// texto y guarda la propuesta de tipo, fecha, título y vínculos. Un fichero cuyo contenido ya está en el gestor no se añade otra vez. Con
    /// <paramref name="moverOriginal"/>, el original va a la papelera de Windows una vez copiado. Con <paramref name="vinculosFijos"/> (soltar en el panel
    /// de una ficha) se vincula además a ese registro y, si la propuesta está completa, se archiva en el acto.
    /// </summary>
    public async Task<Resultado<ResultadoAnadir>> AnadirABandejaAsync(string idSociedad, IReadOnlyList<string> rutas, bool moverOriginal,
        IReadOnlyList<Vinculo>? vinculosFijos = null, bool archivarSiListo = false, CancellationToken cancelacion = default)
    {
        if (!await PuedeAsync()) return Resultado<ResultadoAnadir>.Fallo(SinPermiso("Añadir documentos"));
        var raiz = await RaizAsync(idSociedad);
        if (raiz is null) return Resultado<ResultadoAnadir>.Fallo(SinCarpeta(idSociedad));
        var ficheros = Expandir(rutas);
        var datos = await DatosPropuestaAsync(idSociedad);
        var anadidos = new List<int>();
        var repetidos = new List<string>();
        var errores = new List<string>();
        var vistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var carpeta = Path.Combine(raiz, RutasDocumentos.CarpetaManuales, RutasDocumentos.CarpetaBandeja);
        foreach (var origen in ficheros)
        {
            cancelacion.ThrowIfCancellationRequested();
            var nombre = Path.GetFileName(origen);
            string? copia = null;
            try
            {
                var huella = await RutasDocumentos.HuellaAsync(origen, cancelacion);
                if (!vistos.Add(huella)) { repetidos.Add($"«{nombre}» está repetido entre los ficheros elegidos: se añade una sola vez."); continue; }
                if (await DuplicadoAsync(idSociedad, huella) is { } ya)
                {
                    repetidos.Add(ya.Estado == EstadosDocumento.Archivado
                        ? $"«{nombre}» ya está archivado como «{ya.Titulo}»."
                        : $"«{nombre}» ya está en «Por clasificar» como «{ya.Titulo}».");
                    continue;
                }
                var propuesta = LectorPropuestas.Proponer(TextoFichero.Leer(origen), nombre, datos);
                Directory.CreateDirectory(carpeta);
                copia = RutasDocumentos.RutaLibre(carpeta, $"{_reloj.Ahora:yyyyMMdd-HHmmss} {nombre}");
                File.Copy(origen, copia);
                var tamano = new FileInfo(copia).Length;
                var vinculos = propuesta.Vinculos.ToList();
                foreach (var v in vinculosFijos ?? [])
                    if (!vinculos.Any(x => x.MismoDestino(v))) vinculos.Add(v);
                var tipo = propuesta.Confianza == Confianza.FacturaCompra ? null : propuesta.Tipo;
                var ruta = RutasDocumentos.ParaGuardar(raiz, copia);
                var id = await _bd.EnTransaccionAsync(async s =>
                {
                    var ahora = _reloj.Ahora;
                    await s.EjecutarAsync("INSERT INTO Documentos (IdSociedad, Tipo, Titulo, FechaDocumento, Ejercicio, Estado, Origen, Propuesta, FechaAlta, OperadorAlta) " +
                                          "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        idSociedad, tipo, Recortar(propuesta.Titulo, 200), propuesta.Fecha, (object?)EjercicioDe(vinculos, propuesta.Fecha), EstadosDocumento.PorClasificar,
                        OrigenesDocumento.Manual, propuesta.Serializar(), ahora, _operador);
                    var nuevo = await s.UltimoIdAsync();
                    await InsertarVersionAsync(s, nuevo, 1, ruta, nombre, tamano, huella, "Alta");
                    foreach (var v in vinculos) await InsertarVinculoAsync(s, nuevo, v);
                    return nuevo;
                }, cancelacion);
                anadidos.Add(id);
                if (moverOriginal && !Papelera.Enviar(origen))
                    errores.Add($"«{nombre}» se ha añadido, pero el original no se ha podido mandar a la papelera (¿abierto en otro programa?). Bórralo a mano si no lo quieres.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Data.Common.DbException)
            {
                if (copia is not null) try { File.Delete(copia); } catch (IOException) { }
                errores.Add($"«{nombre}» no se ha añadido: {ex.Message} Comprueba que el fichero no está abierto en otro programa y que tienes permiso en la carpeta de documentos.");
            }
        }
        if (archivarSiListo && anadidos.Count > 0)
        {
            var r = await ArchivarAsync(anadidos, soloListos: true);
            if (!r.EsCorrecto) errores.AddRange(r.Errores.Select(e => e.TextoOperador()));
            else errores.AddRange(r.Valor.Errores);
        }
        return Resultado<ResultadoAnadir>.Correcto(new ResultadoAnadir(anadidos, repetidos, errores));
    }

    private static IReadOnlyList<string> Expandir(IReadOnlyList<string> rutas)
    {
        var lista = new List<string>();
        foreach (var r in rutas)
        {
            if (Directory.Exists(r))
                lista.AddRange(Directory.EnumerateFiles(r, "*", SearchOption.AllDirectories)
                    .Where(f => (File.GetAttributes(f) & (FileAttributes.Hidden | FileAttributes.System)) == 0 && !Path.GetFileName(f).StartsWith("~$", StringComparison.Ordinal))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(r)) lista.Add(r);
        }
        return lista;
    }

    private Task<(string Titulo, string Estado)?> DuplicadoAsync(string idSociedad, string huella) => _bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync<(string Titulo, string Estado)?>(
            "SELECT TOP 1 d.Titulo, d.Estado FROM DocumentosVersiones v INNER JOIN Documentos d ON v.IdDocumento = d.IdDocumento WHERE d.IdSociedad = ? AND v.Huella = ?",
            f => (f.Texto("Titulo") ?? "", f.Texto("Estado") ?? ""), idSociedad, huella));

    /// <summary>Lo que el lector necesita: CIF de la sociedad, presentaciones grabadas y terceros con NIF.</summary>
    public Task<DatosPropuesta> DatosPropuestaAsync(string idSociedad) => _bd.EjecutarAsync(async s =>
    {
        var soc = await s.ConsultarPrimeroAsync("SELECT Nombre, CIF FROM Sociedades WHERE IdSociedad = ?", f => (Nombre: f.Texto("Nombre") ?? idSociedad, Cif: f.Texto("CIF")), idSociedad);
        var presentaciones = await s.ConsultarAsync(
            "SELECT Modelo, Ejercicio, Periodo, Justificante, Csv, FechaHora FROM PresentacionesAeat WHERE IdSociedad = ?",
            f => new PresentacionConocida((f.Texto("Modelo") ?? "").Trim(), f.Entero("Ejercicio"), (f.Texto("Periodo") ?? "").Trim(), (f.Texto("Justificante") ?? "").Trim(),
                f.Texto("Csv"), f.Fecha("FechaHora")), idSociedad);
        var terceros = await s.ConsultarAsync("SELECT IdTercero, NIF, Nombre FROM Terceros WHERE NIF IS NOT NULL AND NIF <> ''",
            f => new TerceroConocido(f.Entero("IdTercero"), f.Texto("NIF") ?? "", f.Texto("Nombre") ?? ""));
        return new DatosPropuesta(soc.Cif, Empresa(idSociedad, soc.Nombre ?? idSociedad), presentaciones, terceros);
    });

    /// <summary>Vínculo con la empresa (la sociedad).</summary>
    public static Vinculo Empresa(string idSociedad, string nombre) => new(TiposEntidad.Empresa, null, idSociedad, nombre);

    /// <summary>Cambia los datos de un documento: tipo, título, fecha y notas. El título de uno archivado cambia también el nombre de su fichero.</summary>
    public async Task<Resultado> ActualizarAsync(int idDocumento, string? tipo, string titulo, DateTime? fecha, string? notas)
    {
        var registro = $"Documento {idDocumento}";
        if (!await PuedeAsync()) return Resultado.Fallo(SinPermiso(registro));
        if (string.IsNullOrWhiteSpace(titulo))
            return Resultado.Fallo(new Error("Documentos.SinTitulo", "Falta el título del documento.", registro, null, "Escribe un título: será también el nombre del fichero."));
        var doc = await LeerAsync(idDocumento);
        if (doc is null) return Resultado.Fallo(NoEncontrado(idDocumento));
        await _bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Documentos SET Tipo = ?, Titulo = ?, FechaDocumento = ?, Ejercicio = ?, Notas = ?, FechaModificacion = ?, OperadorModificacion = ? WHERE IdDocumento = ?",
                string.IsNullOrWhiteSpace(tipo) ? null : tipo, Recortar(titulo.Trim(), 200), fecha, (object?)EjercicioDe(doc.Vinculos, fecha),
                string.IsNullOrWhiteSpace(notas) ? null : notas, _reloj.Ahora, _operador, idDocumento);
            return true;
        });
        return Resultado.Correcto();
    }

    /// <summary>
    /// Vincula documentos a unos registros. Con <paramref name="reemplazar"/>, los vínculos de cada documento pasan a ser exactamente esos; sin él, se
    /// añaden (sin repetir los que ya tiene).
    /// </summary>
    public async Task<Resultado> VincularAsync(IReadOnlyList<int> ids, IReadOnlyList<Vinculo> vinculos, bool reemplazar)
    {
        if (!await PuedeAsync()) return Resultado.Fallo(SinPermiso("Vincular documentos"));
        if (ids.Count == 0) return Resultado.Correcto();
        await _bd.EnTransaccionAsync(async s =>
        {
            foreach (var id in ids)
            {
                var actuales = await s.ConsultarAsync("SELECT * FROM DocumentosVinculos WHERE IdDocumento = ?", VinculoDe, id);
                if (reemplazar)
                {
                    foreach (var v in actuales.Where(a => !vinculos.Any(n => n.MismoDestino(a))))
                        await BorrarVinculoAsync(s, id, v);
                }
                foreach (var v in vinculos.Where(n => !actuales.Any(a => a.MismoDestino(n))))
                    await InsertarVinculoAsync(s, id, v);
                var fecha = await s.EscalarAsync("SELECT FechaDocumento FROM Documentos WHERE IdDocumento = ?", id) as DateTime?;
                var todos = reemplazar ? vinculos : [.. actuales, .. vinculos];
                await s.EjecutarAsync("UPDATE Documentos SET Ejercicio = ?, FechaModificacion = ?, OperadorModificacion = ? WHERE IdDocumento = ?",
                    (object?)EjercicioDe(todos, fecha), _reloj.Ahora, _operador, id);
            }
            return true;
        });
        return Resultado.Correcto();
    }

    /// <summary>Quita un vínculo. El documento y su fichero se quedan como estaban.</summary>
    public async Task<Resultado> DesvincularAsync(int idDocumento, Vinculo vinculo)
    {
        if (!await PuedeAsync()) return Resultado.Fallo(SinPermiso($"Documento {idDocumento}"));
        await _bd.EnTransaccionAsync(async s =>
        {
            await BorrarVinculoAsync(s, idDocumento, vinculo);
            await s.EjecutarAsync("UPDATE Documentos SET FechaModificacion = ?, OperadorModificacion = ? WHERE IdDocumento = ?", _reloj.Ahora, _operador, idDocumento);
            return true;
        });
        return Resultado.Correcto();
    }

    /// <summary>
    /// Archiva documentos de la bandeja: mueve cada fichero a «Documentos\&lt;año&gt;\&lt;carpeta del tipo&gt;» (o la común del tipo) con el título
    /// como nombre, sin sobrescribir, y lo marca como archivado. Los que no están listos (sin tipo, título, fecha o vínculo, o que son facturas de
    /// compra) se quedan en la bandeja y se dicen en <see cref="ResultadoArchivar.NoListos"/>.
    /// </summary>
    public async Task<Resultado<ResultadoArchivar>> ArchivarAsync(IReadOnlyList<int> ids, bool soloListos = true)
    {
        if (!await PuedeAsync()) return Resultado<ResultadoArchivar>.Fallo(SinPermiso("Archivar documentos"));
        var tipos = (await TiposAsync()).ToDictionary(t => t.Codigo, StringComparer.OrdinalIgnoreCase);
        var archivados = 0;
        var noListos = new List<string>();
        var errores = new List<string>();
        foreach (var id in ids)
        {
            var d = await LeerAsync(id);
            if (d is null || !d.PorClasificar || d.Vigente is null) continue;
            if (!d.Listo || !tipos.TryGetValue(d.Tipo!, out var tipo))
            {
                noListos.Add($"«{d.Titulo}»: {QueFalta(d)}");
                continue;
            }
            var raiz = await RaizAsync(d.IdSociedad);
            if (raiz is null) return Resultado<ResultadoArchivar>.Fallo(SinCarpeta(d.IdSociedad));
            var origen = d.Vigente.RutaCompleta;
            if (!File.Exists(origen))
            {
                errores.Add($"«{d.Titulo}»: no encuentro su fichero en {origen}. Quítalo de la bandeja y vuelve a añadirlo.");
                continue;
            }
            var carpeta = Path.Combine(raiz, RutasDocumentos.SubcarpetaManual(tipo, d.Ejercicio ?? d.FechaDocumento?.Year));
            string? destino = null;
            try
            {
                Directory.CreateDirectory(carpeta);
                destino = RutasDocumentos.RutaLibre(carpeta, d.Titulo + Path.GetExtension(origen));
                // En pruebas, un fichero de una carpeta de producción (el NAS) se copia y el original se queda donde estaba.
                ProConta.Nucleo.Ficheros.EntornoFicheros.Mover(origen, destino);
                destino = ProConta.Nucleo.Ficheros.EntornoFicheros.ParaEscribir(destino);
                var ruta = RutasDocumentos.ParaGuardar(raiz, destino);
                await _bd.EnTransaccionAsync(async s =>
                {
                    await s.EjecutarAsync("UPDATE DocumentosVersiones SET Ruta = ? WHERE IdVersionDocumento = ?", ruta, d.Vigente.IdVersionDocumento);
                    await s.EjecutarAsync("UPDATE Documentos SET Estado = ?, FechaModificacion = ?, OperadorModificacion = ? WHERE IdDocumento = ?",
                        EstadosDocumento.Archivado, _reloj.Ahora, _operador, id);
                    return true;
                });
                archivados++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException or InvalidOperationException)
            {
                if (destino is not null && File.Exists(destino) && !File.Exists(origen)) try { File.Move(destino, origen); } catch (IOException) { }
                else if (destino is not null && File.Exists(destino) && ProConta.Nucleo.Ficheros.EntornoFicheros.Protegida(origen)) try { File.Delete(destino); } catch (IOException) { }
                errores.Add($"«{d.Titulo}» no se ha archivado: {ex.Message} Comprueba que el fichero no está abierto y vuelve a intentarlo.");
            }
        }
        return Resultado<ResultadoArchivar>.Correcto(new ResultadoArchivar(archivados, noListos, errores));
    }

    /// <summary>Qué le falta a un documento de la bandeja para archivarlo, en palabras.</summary>
    public static string QueFalta(DocumentoListado d)
    {
        if (d.Confianza == Confianza.FacturaCompra) return "es una factura de compra; regístrala en Compras › Facturas recibidas.";
        var falta = new List<string>();
        if (string.IsNullOrWhiteSpace(d.Tipo)) falta.Add("el tipo");
        if (d.FechaDocumento is null) falta.Add("la fecha");
        if (string.IsNullOrWhiteSpace(d.Titulo)) falta.Add("el título");
        if (d.Vinculos.Count == 0) falta.Add("a qué se vincula");
        return falta.Count == 0 ? "listo para archivar." : "falta " + Unir(falta) + ".";
    }

    private static string Unir(IReadOnlyList<string> partes) => partes.Count <= 1 ? string.Concat(partes) : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[^1];

    /// <summary>
    /// Guarda una versión nueva de un documento archivado a partir de un fichero: se copia junto a la vigente con « (v2)», « (v3)»… y la anterior se
    /// conserva intacta (los envíos que la usaron siguen apuntando a ella).
    /// </summary>
    public async Task<Resultado<DocumentoGuardado>> NuevaVersionAsync(int idDocumento, string rutaFichero, string? motivo)
    {
        var registro = $"Documento {idDocumento}";
        if (!await PuedeAsync()) return Resultado<DocumentoGuardado>.Fallo(SinPermiso(registro));
        var d = await LeerAsync(idDocumento);
        if (d is null) return Resultado<DocumentoGuardado>.Fallo(NoEncontrado(idDocumento));
        var raiz = await RaizAsync(d.IdSociedad);
        if (raiz is null) return Resultado<DocumentoGuardado>.Fallo(SinCarpeta(d.IdSociedad));
        var huella = await RutasDocumentos.HuellaAsync(rutaFichero);
        if (d.Vigente is { } vigente && vigente.Huella == huella)
            return Resultado<DocumentoGuardado>.Fallo(new Error("Documentos.MismaVersion", "Ese fichero es igual que la versión vigente: no hace falta otra versión.", registro, null,
                "Elige el fichero modificado (por ejemplo, el firmado)."));
        var numero = d.NumeroVersiones + 1;
        var carpeta = d.Vigente is { } v ? Path.GetDirectoryName(v.RutaCompleta)! : Path.Combine(raiz, RutasDocumentos.CarpetaManuales);
        string? destino = null;
        try
        {
            Directory.CreateDirectory(carpeta);
            destino = RutasDocumentos.RutaLibre(carpeta, $"{d.Titulo} (v{numero}){Path.GetExtension(rutaFichero)}");
            File.Copy(rutaFichero, destino);
            var ruta = RutasDocumentos.ParaGuardar(raiz, destino);
            var tamano = new FileInfo(destino).Length;
            var idVersion = await _bd.EnTransaccionAsync(async s =>
            {
                var nueva = await InsertarVersionAsync(s, idDocumento, numero, ruta, Path.GetFileName(rutaFichero), tamano, huella,
                    string.IsNullOrWhiteSpace(motivo) ? $"Versión {numero}" : motivo.Trim());
                await s.EjecutarAsync("UPDATE Documentos SET FechaModificacion = ?, OperadorModificacion = ? WHERE IdDocumento = ?", _reloj.Ahora, _operador, idDocumento);
                return nueva;
            });
            return Resultado<DocumentoGuardado>.Correcto(new DocumentoGuardado(idDocumento, idVersion, numero, ruta, destino, false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Data.Common.DbException or InvalidOperationException)
        {
            if (destino is not null) try { File.Delete(destino); } catch (IOException) { }
            return Resultado<DocumentoGuardado>.Fallo(new Error("Documentos.NoGuardado", $"No he podido guardar la versión nueva: {ex.Message}", registro, null,
                "Comprueba que el fichero no está abierto en otro programa y que tienes permiso en la carpeta de documentos."));
        }
    }

    /// <summary>
    /// Elimina documentos: los de la bandeja siempre; los archivados solo si no tienen ningún vínculo. Sus ficheros van a la papelera de Windows.
    /// Devuelve cuántos se han eliminado; los que no se pueden se explican en el error.
    /// </summary>
    public async Task<Resultado<int>> EliminarAsync(IReadOnlyList<int> ids)
    {
        if (!await PuedeAsync()) return Resultado<int>.Fallo(SinPermiso("Eliminar documentos"));
        var hechos = 0;
        var noSe = new List<string>();
        foreach (var id in ids)
        {
            var d = await LeerAsync(id);
            if (d is null) continue;
            if (!d.PorClasificar && d.Vinculos.Count > 0)
            {
                noSe.Add($"«{d.Titulo}» tiene {d.Vinculos.Count} vínculo(s)");
                continue;
            }
            var versiones = await VersionesAsync(id);
            await _bd.EnTransaccionAsync(async s =>
            {
                await s.EjecutarAsync("DELETE FROM DocumentosVinculos WHERE IdDocumento = ?", id);
                await s.EjecutarAsync("DELETE FROM DocumentosVersiones WHERE IdDocumento = ?", id);
                await s.EjecutarAsync("DELETE FROM Documentos WHERE IdDocumento = ?", id);
                return true;
            });
            foreach (var v in versiones) Papelera.Enviar(v.RutaCompleta);
            hechos++;
        }
        if (noSe.Count > 0)
            return Resultado<int>.Fallo(new Error("Documentos.ConVinculos",
                $"He eliminado {hechos} documento(s), pero no los que están vinculados: {string.Join("; ", noSe)}.", null,
                "Un documento archivado y vinculado puede ser el respaldo de un modelo, una factura o un asiento.",
                "Si de verdad sobra, desvincúlalo antes (en el detalle, la × de cada vínculo) y vuelve a eliminarlo."));
        return Resultado<int>.Correcto(hechos);
    }

    // ---------- comunes ----------

    private async Task<int> InsertarVersionAsync(ISesionDatos s, int idDocumento, int numero, string ruta, string? nombreOriginal, long tamano, string huella, string? motivo)
    {
        await s.EjecutarAsync("INSERT INTO DocumentosVersiones (IdDocumento, NumeroVersion, Ruta, NombreOriginal, Extension, TamanoBytes, Huella, FechaAlta, Operador, Motivo) " +
                              "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idDocumento, (short)numero, Recortar(ruta, 255), Recortar(nombreOriginal, 255), Recortar(Path.GetExtension(ruta).ToLowerInvariant(), 10),
            (int)Math.Min(tamano, int.MaxValue), huella, _reloj.Ahora, _operador, Recortar(motivo, 255));
        return await s.UltimoIdAsync();
    }

    private async Task InsertarVinculoAsync(ISesionDatos s, int idDocumento, Vinculo v) =>
        await s.EjecutarAsync("INSERT INTO DocumentosVinculos (IdDocumento, TipoEntidad, IdEntidad, ClaveEntidad, Etiqueta, FechaAlta, Operador) VALUES (?, ?, ?, ?, ?, ?, ?)",
            idDocumento, v.TipoEntidad, (object?)v.IdEntidad, Recortar(v.Clave, 40), Recortar(v.Etiqueta, 200), _reloj.Ahora, _operador);

    private static Task<int> BorrarVinculoAsync(ISesionDatos s, int idDocumento, Vinculo v) => v.IdEntidad is int idE
        ? s.EjecutarAsync("DELETE FROM DocumentosVinculos WHERE IdDocumento = ? AND TipoEntidad = ? AND IdEntidad = ?", idDocumento, v.TipoEntidad, idE)
        : s.EjecutarAsync("DELETE FROM DocumentosVinculos WHERE IdDocumento = ? AND TipoEntidad = ? AND ClaveEntidad = ?", idDocumento, v.TipoEntidad, v.Clave ?? "");

    /// <summary>Año del documento: el del modelo vinculado si lo hay; si no, el de su fecha.</summary>
    internal static int? EjercicioDe(IEnumerable<Vinculo> vinculos, DateTime? fecha)
    {
        foreach (var v in vinculos.Where(v => v.TipoEntidad == TiposEntidad.Modelo && v.Clave is not null))
            if (v.Clave!.Split('|') is [_, var a, _] && int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var anio)) return anio;
        return fecha?.Year;
    }

    internal static string? Recortar(string? texto, int largo) => texto is null ? null : texto.Length <= largo ? texto : texto[..largo];
}
