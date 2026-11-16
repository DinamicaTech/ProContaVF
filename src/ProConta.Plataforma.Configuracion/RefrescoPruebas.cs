using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Resultados;

namespace ProConta.Plataforma.Configuracion;

/// <summary>Una carpeta de producción y la de pruebas que la replica.</summary>
public sealed record CarpetaReplicada(string Origen, string Destino);

/// <summary>Lo que hará el refresco, ya comprobado: la BD de producción, la de pruebas y las carpetas que se replican.</summary>
/// <param name="Carpetas">La primera es la carpeta de la BD; las demás, las carpetas de documentos de empresas que están fuera de ella.</param>
public sealed record PlanRefresco(string BdProduccion, string CarpetaPruebas, string BdPruebas, IReadOnlyList<CarpetaReplicada> Carpetas, bool HayCopiaAnterior);

/// <summary>Lo que hizo el refresco.</summary>
public sealed record InformeRefresco(
    string BdPruebas,
    DateTime Fecha,
    int FicherosCopiados,
    int FicherosBorrados,
    int RutasCambiadas,
    IReadOnlyList<string> CuentasPruebasConservadas,
    int MensajesCancelados,
    string? CopiaAnterior);

/// <summary>
/// Refresco de la copia de pruebas desde producción (plataforma/entorno-pruebas). Se lanza desde producción y:
/// copia la BD y todas sus carpetas de datos a la carpeta de pruebas (por defecto <c>datospruebas</c>, junto a la de la BD),
/// cambia en la copia las rutas que apuntan a carpetas de producción, la marca como BD de pruebas y la deja sin efectos fuera:
/// todo el correo desviado, sin recepción POP3 salvo la cuenta de pruebas que tuviera la copia anterior, los envíos pendientes
/// cancelados y VeriFactu desactivado y en el entorno de pruebas de la AEAT. Conserva el token de Avaibook y las credenciales de
/// Textract. La copia anterior de pruebas se guarda en <c>copias-pruebas</c>. Nunca escribe en producción.
/// </summary>
public sealed class RefrescoPruebas(Func<DateTime>? ahora = null)
{
    public const string NombreCarpetaPruebas = "datospruebas";
    public const string CarpetaCopiasPruebas = "copias-pruebas";
    public const string CarpetaExternas = "externas";

    /// <summary>Direcciones de VeriFactu del entorno de pruebas de la AEAT (los valores por defecto de VeriFactuConfiguracion).</summary>
    public const string VeriFactuPruebasEnvio = "https://prewww1.aeat.es/wlpl/TIKE-CONT/ws/SistemaFacturacion/VerifactuSOAP";
    public const string VeriFactuPruebasQr = "https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR";

    private static readonly string[] _columnasCuenta =
    [
        "Activa", "Direccion", "NombreVisible", "Usuario", "SecretoProtegido", "ResponderA", "ServidorSmtp", "PuertoSmtp", "SeguridadSmtp",
        "ServidorPop3", "PuertoPop3", "SeguridadPop3", "EliminarTrasProcesar", "IntervaloRecepcionMinutos", "TiempoEsperaSegundos",
    ];

    private readonly Func<DateTime> _ahora = ahora ?? (() => DateTime.Now);

    /// <summary>Carpeta de pruebas que se propone: <c>datospruebas</c> junto a la carpeta de la BD de producción.</summary>
    public static string CarpetaPruebasPorDefecto(string bdProduccion)
    {
        var carpeta = Path.GetDirectoryName(Path.GetFullPath(bdProduccion))!;
        var padre = Path.GetDirectoryName(carpeta);
        return padre is null ? Path.Combine(carpeta, NombreCarpetaPruebas) : Path.Combine(padre, NombreCarpetaPruebas);
    }

    /// <summary>Comprueba que se puede refrescar y calcula qué se copia y adónde. No cambia nada.</summary>
    public async Task<Resultado<PlanRefresco>> PlanificarAsync(string bdProduccion, string carpetaPruebas)
    {
        bdProduccion = Path.GetFullPath(bdProduccion);
        if (!Path.IsPathFullyQualified(carpetaPruebas.Trim()))
            return Fallo("Pruebas.CarpetaRelativa", "La carpeta de pruebas tiene que ser una ruta completa.", carpetaPruebas,
                "Una ruta relativa depende de la carpeta desde la que se abre el programa.", $"Indica la ruta completa, por ejemplo {CarpetaPruebasPorDefecto(bdProduccion)}.");
        carpetaPruebas = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpetaPruebas.Trim()));
        var bdPruebas = Path.Combine(carpetaPruebas, Path.GetFileName(bdProduccion));
        var carpetaBd = Path.GetDirectoryName(bdProduccion)!;

        var produccion = new BaseDatosAccess(bdProduccion, ModoApertura.SoloLectura);
        if ((await InfoEntorno.LeerAsync(produccion)).EsPruebas)
            return Fallo("Pruebas.OrigenEsPruebas", "La base de datos abierta ya es una copia de pruebas: el refresco se hace desde producción.", bdProduccion,
                "Esta BD tiene la marca de entorno de pruebas.", "Sal y entra en el entorno de producción para actualizar la copia de pruebas.");

        IReadOnlyList<string?> rutasDocumentos;
        try
        {
            rutasDocumentos = await produccion.EjecutarAsync(s => s.ConsultarAsync("SELECT RutaDocumentos FROM Sociedades", f => f.Texto("RutaDocumentos")));
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<PlanRefresco>.Fallo(ex.Error);
        }

        var carpetas = new List<CarpetaReplicada> { new(carpetaBd, carpetaPruebas) };
        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ruta in rutasDocumentos.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => Path.TrimEndingDirectorySeparator(RaizDocumentos(r!, bdProduccion)))
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(r => r.Length))
        {
            if (carpetas.Any(c => Dentro(ruta, c.Origen))) continue; // ya se copia con su carpeta superior
            var nombre = Path.GetFileName(ruta) is { Length: > 0 } n ? n : "documentos";
            var unico = nombre;
            for (var i = 2; !nombres.Add(unico); i++) unico = $"{nombre}-{i}";
            carpetas.Add(new(ruta, Path.Combine(carpetaPruebas, CarpetaExternas, unico)));
        }

        foreach (var c in carpetas)
        {
            if (Dentro(carpetaPruebas, c.Origen) || Dentro(c.Origen, carpetaPruebas))
                return Fallo("Pruebas.CarpetaSolapada", "La carpeta de pruebas no puede estar dentro de una carpeta de producción, ni al revés.", carpetaPruebas,
                    $"Se solapa con la carpeta de producción {c.Origen}, y el refresco borraría o mezclaría sus ficheros.",
                    $"Elige una carpeta aparte, como {CarpetaPruebasPorDefecto(bdProduccion)}.");
            if (!Directory.Exists(c.Origen))
                return Fallo("Pruebas.CarpetaOrigenAusente", "No encuentro una carpeta de documentos de producción.", c.Origen,
                    "Sociedades › Carpeta de documentos apunta a una carpeta que no existe.", "Corrige la carpeta de documentos de la empresa en Parámetros y tablas › Empresas, o crea la carpeta.");
        }

        var hayAnterior = File.Exists(bdPruebas);
        if (hayAnterior && !(await InfoEntorno.LeerAsync(new BaseDatosAccess(bdPruebas, ModoApertura.SoloLectura))).EsPruebas)
            return Fallo("Pruebas.DestinoNoEsPruebas", "En la carpeta de pruebas ya hay una base de datos que no es una copia de pruebas, así que no la sustituyo.", bdPruebas,
                "Esa BD no tiene la marca de entorno de pruebas: podría ser una BD de producción o de otro uso.", "Muévela a otra carpeta o elige otra carpeta de pruebas.");

        return Resultado<PlanRefresco>.Correcto(new(bdProduccion, carpetaPruebas, bdPruebas, carpetas, hayAnterior));
    }

    /// <summary>Hace el refresco planificado. <paramref name="desviarCorreoA"/> es obligatorio: en pruebas todo el correo sale a esa dirección.</summary>
    public async Task<Resultado<InformeRefresco>> EjecutarAsync(PlanRefresco plan, string desviarCorreoA, IProgress<string>? progreso = null, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(desviarCorreoA) || !desviarCorreoA.Contains('@'))
            return FalloInforme("Pruebas.SinDesvio", "Falta la dirección a la que se desvía el correo de la copia de pruebas.", null,
                "En pruebas todo el correo sale a una sola dirección, para que no llegue nada a clientes ni a la gestoría.", "Escribe tu dirección de correo.");
        var fecha = _ahora();
        Directory.CreateDirectory(plan.CarpetaPruebas);

        if (plan.HayCopiaAnterior && EnUso(plan.BdPruebas))
            return FalloInforme("Pruebas.CopiaEnUso", "La copia de pruebas está abierta, así que no la puedo sustituir.", plan.BdPruebas,
                "Hay otro ProContaVF, o el conector de Claude Desktop, trabajando con la BD de pruebas.",
                "Cierra ProContaVF en el entorno de pruebas y Claude Desktop si usa el conector de pruebas, y vuelve a intentarlo.");

        var cuentasPruebas = new List<Dictionary<string, object?>>();
        if (plan.HayCopiaAnterior)
        {
            progreso?.Report("Leyendo la cuenta de correo de pruebas de la copia anterior…");
            try { cuentasPruebas = await CuentasDePruebasAsync(plan.BdPruebas, plan.BdProduccion); }
            catch (ExcepcionDatos ex) { return FalloInforme(ex.Error.Codigo, ex.Error.Mensaje, plan.BdPruebas, ex.Error.Causa ?? "", ex.Error.AccionCorrectora ?? "Vuelve a intentarlo."); }
        }

        var nueva = Path.Combine(plan.CarpetaPruebas, Path.GetFileNameWithoutExtension(plan.BdPruebas) + ".nueva" + Path.GetExtension(plan.BdPruebas));
        try
        {
            progreso?.Report("Copiando la base de datos de producción…");
            CopiarFichero(plan.BdProduccion, nueva);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FalloInforme("Pruebas.SinCopiaBd", "No he podido copiar la base de datos de producción.", plan.BdProduccion, ex.Message,
                "Comprueba el espacio libre del disco y que tienes permiso de escritura en la carpeta de pruebas, y vuelve a intentarlo.");
        }

        int rutas, cancelados;
        IReadOnlyList<string> conservadas;
        try
        {
            progreso?.Report("Preparando la copia: marca de pruebas, rutas, correo y VeriFactu…");
            (rutas, conservadas, cancelados) = await PrepararCopiaAsync(nueva, plan, desviarCorreoA.Trim(), fecha, cuentasPruebas);
        }
        catch (ExcepcionDatos ex)
        {
            BorrarSinFallar(nueva);
            return Resultado<InformeRefresco>.Fallo(ex.Error with { AccionCorrectora = "La copia de pruebas anterior sigue como estaba. " + (ex.Error.AccionCorrectora ?? "Vuelve a intentarlo.") });
        }

        int copiados = 0, borrados = 0;
        try
        {
            foreach (var c in plan.Carpetas)
            {
                cancelacion.ThrowIfCancellationRequested();
                progreso?.Report($"Copiando {c.Origen}…");
                var (cop, bor) = Replicar(c, plan, nueva);
                copiados += cop;
                borrados += bor;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            BorrarSinFallar(nueva);
            return FalloInforme("Pruebas.SinCopiaCarpetas", "No he podido terminar de copiar las carpetas de producción.", plan.CarpetaPruebas, ex.Message,
                "La BD de pruebas anterior sigue como estaba (sus carpetas pueden haber quedado a medias). Cierra lo que tenga abiertos esos ficheros y vuelve a actualizar la copia.");
        }

        string? anterior = null;
        try
        {
            progreso?.Report("Sustituyendo la copia de pruebas…");
            if (File.Exists(plan.BdPruebas))
            {
                var carpetaAnterior = Path.Combine(plan.CarpetaPruebas, CarpetaCopiasPruebas);
                Directory.CreateDirectory(carpetaAnterior);
                anterior = Path.Combine(carpetaAnterior, Path.GetFileNameWithoutExtension(plan.BdPruebas) + ".anterior" + Path.GetExtension(plan.BdPruebas));
                ConReintentos(() => File.Move(plan.BdPruebas, anterior, overwrite: true));
            }
            ConReintentos(() => File.Move(nueva, plan.BdPruebas));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return FalloInforme("Pruebas.SinSustituir", "He preparado la copia nueva, pero no he podido ponerla en lugar de la anterior.", plan.BdPruebas, ex.Message,
                $"Cierra lo que tenga abierta la BD de pruebas y vuelve a intentarlo. La copia nueva está en {nueva}.");
        }
        return Resultado<InformeRefresco>.Correcto(new(plan.BdPruebas, fecha, copiados, borrados, rutas, conservadas, cancelados, anterior));
    }

    /// <summary>
    /// Cuentas de correo de la copia anterior que no son de producción (la cuenta de pruebas que el operador dio de alta en ella), por
    /// sociedad y ejercicio, con todas sus columnas, para volver a ponerlas en la copia nueva.
    /// </summary>
    private static async Task<List<Dictionary<string, object?>>> CuentasDePruebasAsync(string bdPruebas, string bdProduccion)
    {
        const string sql = "SELECT * FROM CuentasCorreo";
        var leer = (IFilaDatos f) => _columnasCuenta.Append("IdSociedadEjercicio").ToDictionary(c => c, f.Valor, StringComparer.OrdinalIgnoreCase);
        var anteriores = await new BaseDatosAccess(bdPruebas, ModoApertura.SoloLectura).EjecutarAsync(s => s.ConsultarAsync(sql, leer));
        var deProduccion = await new BaseDatosAccess(bdProduccion, ModoApertura.SoloLectura).EjecutarAsync(s => s.ConsultarAsync(sql, leer));
        return anteriores.Where(a => !deProduccion.Any(p => Equals(p["IdSociedadEjercicio"], a["IdSociedadEjercicio"])
                && string.Equals((p["Direccion"] as string)?.Trim(), (a["Direccion"] as string)?.Trim(), StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    private static async Task<(int Rutas, IReadOnlyList<string> Conservadas, int Cancelados)> PrepararCopiaAsync(
        string nueva, PlanRefresco plan, string desvio, DateTime fecha, List<Dictionary<string, object?>> cuentasPruebas)
    {
        var bd = new BaseDatosAccess(nueva, ModoApertura.Exclusivo);
        return await bd.EjecutarAsync(async s =>
        {
            var esquema = s.LeerEsquema();
            if (!esquema.ExisteColumna("Configuracion", "EntornoPruebas"))
                throw new ExcepcionDatos(new Error("Pruebas.EsquemaAntiguo", "La BD de producción no tiene todavía la marca de entorno de pruebas.", plan.BdProduccion,
                    "Su esquema es anterior a la versión que trae el entorno de pruebas.", "Entra en producción con esta versión del programa para que la actualice y vuelve a intentarlo."));
            var n = await s.EjecutarAsync("UPDATE Configuracion SET EntornoPruebas = True, CopiaProduccionFecha = ?, CopiaProduccionOrigen = ?, CorreoDesviarA = ?",
                fecha, Recortar(plan.BdProduccion, 255), Recortar(desvio, 254));
            if (n == 0)
                await s.EjecutarAsync("INSERT INTO Configuracion (EntornoPruebas, CopiaProduccionFecha, CopiaProduccionOrigen, CorreoDesviarA) VALUES (True, ?, ?, ?)",
                    fecha, Recortar(plan.BdProduccion, 255), Recortar(desvio, 254));

            // Rutas a carpetas de producción → sus réplicas en pruebas, en todas las columnas de texto (las más largas primero).
            var rutas = 0;
            var cambios = plan.Carpetas.OrderByDescending(c => c.Origen.Length).ToList();
            foreach (var tabla in esquema.Tablas.Where(t => !t.EsVinculada && !t.Nombre.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)))
                foreach (var col in tabla.Columnas.Where(c => c.Tipo.StartsWith("TEXT", StringComparison.OrdinalIgnoreCase) || c.Tipo == "LONGTEXT"))
                    foreach (var c in cambios)
                        rutas += await s.EjecutarAsync(SqlCambioRuta(tabla.Nombre, col.Nombre), c.Destino, c.Origen.Length + 1, c.Origen.Length + 1, c.Origen + "\\", c.Origen);

            // Correo: sin recepción ni borrado del buzón real; la cuenta de pruebas de la copia anterior vuelve a su ejercicio.
            var conservadas = new List<string>();
            if (esquema.ExisteTabla("CuentasCorreo"))
            {
                await s.EjecutarAsync("UPDATE CuentasCorreo SET ServidorPop3 = Null, EliminarTrasProcesar = False");
                foreach (var cuenta in cuentasPruebas)
                {
                    var ejercicio = cuenta["IdSociedadEjercicio"];
                    if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ejercicio)) == 0) continue;
                    var valores = _columnasCuenta.Select(c => cuenta[c] is DBNull ? null : cuenta[c]).ToArray();
                    var existe = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM CuentasCorreo WHERE IdSociedadEjercicio = ?", ejercicio)) > 0;
                    if (existe)
                        await s.EjecutarAsync($"UPDATE CuentasCorreo SET {string.Join(", ", _columnasCuenta.Select(c => $"[{c}] = ?"))}, Version = Version + 1 WHERE IdSociedadEjercicio = ?",
                            [.. valores, ejercicio]);
                    else
                        await s.EjecutarAsync($"INSERT INTO CuentasCorreo (IdSociedadEjercicio, {string.Join(", ", _columnasCuenta.Select(c => $"[{c}]"))}, Version) VALUES (?, {string.Join(", ", _columnasCuenta.Select(_ => "?"))}, 1)",
                            [ejercicio, .. valores]);
                    conservadas.Add((cuenta["Direccion"] as string ?? "").Trim());
                }
            }
            var cancelados = 0;
            if (esquema.ExisteTabla("MensajesCorreo"))
                cancelados = await s.EjecutarAsync("UPDATE MensajesCorreo SET Estado = 'Cancelado', UltimoError = ?, Version = Version + 1 WHERE Estado IN ('Pendiente', 'EnProceso')",
                    "Cancelado al crear la copia de pruebas: era un envío pendiente de producción.");
            if (esquema.ExisteTabla("TurnosCorreo")) await s.EjecutarAsync("DELETE FROM TurnosCorreo");

            // VeriFactu desactivado y en el entorno de pruebas de la AEAT; el certificado se conserva (lo usa la consulta de NIF, que solo lee).
            if (esquema.Tabla("VeriFactuConfiguracion") is { } vf)
            {
                await s.EjecutarAsync("UPDATE VeriFactuConfiguracion SET Activo = False");
                if (vf.Columna("Entorno") is not null) await s.EjecutarAsync("UPDATE VeriFactuConfiguracion SET Entorno = 'PRUEBAS'");
                if (vf.Columna("VeriFactuEndPointPrefix") is not null) await s.EjecutarAsync("UPDATE VeriFactuConfiguracion SET VeriFactuEndPointPrefix = ?", VeriFactuPruebasEnvio);
                if (vf.Columna("VeriFactuEndPointValidatePrefix") is not null) await s.EjecutarAsync("UPDATE VeriFactuConfiguracion SET VeriFactuEndPointValidatePrefix = ?", VeriFactuPruebasQr);
            }
            return (rutas, (IReadOnlyList<string>)conservadas, cancelados);
        });
    }

    /// <summary>UPDATE que cambia el principio de la ruta (la carpeta entera, o una ruta dentro de ella) en una columna de texto.</summary>
    internal static string SqlCambioRuta(string tabla, string columna) =>
        $"UPDATE [{tabla}] SET [{columna}] = ? & Mid([{columna}], ?) WHERE Left([{columna}], ?) = ? OR [{columna}] = ?";

    /// <summary>La misma sustitución en memoria (para las pruebas y para explicar el cambio): null si la ruta no es de esa carpeta.</summary>
    public static string? CambiarRuta(string ruta, CarpetaReplicada carpeta)
    {
        if (string.Equals(ruta, carpeta.Origen, StringComparison.OrdinalIgnoreCase)) return carpeta.Destino;
        return ruta.StartsWith(carpeta.Origen + "\\", StringComparison.OrdinalIgnoreCase) ? carpeta.Destino + ruta[carpeta.Origen.Length..] : null;
    }

    /// <summary>Replica una carpeta (copia lo nuevo o cambiado y borra lo que ya no está en producción), sin tocar lo propio de pruebas.</summary>
    private static (int Copiados, int Borrados) Replicar(CarpetaReplicada c, PlanRefresco plan, string nueva)
    {
        var esLaDeBd = string.Equals(c.Destino, plan.CarpetaPruebas, StringComparison.OrdinalIgnoreCase);
        // En origen: la BD (va aparte), los bloqueos de Access y las copias de esquema de producción.
        bool ExcluirOrigen(string relativa) =>
            relativa.EndsWith(".laccdb", StringComparison.OrdinalIgnoreCase)
            || (esLaDeBd && (string.Equals(relativa, Path.GetFileName(plan.BdProduccion), StringComparison.OrdinalIgnoreCase)
                || Primera(relativa).Equals(ComprobadorArranque.CarpetaCopiasEsquema, StringComparison.OrdinalIgnoreCase)));
        // En destino no se borra: la BD de pruebas y la nueva, sus bloqueos, sus copias, sus copias de esquema y las réplicas externas.
        bool Protegido(string relativa) =>
            relativa.EndsWith(".laccdb", StringComparison.OrdinalIgnoreCase)
            || (esLaDeBd && (string.Equals(relativa, Path.GetFileName(plan.BdPruebas), StringComparison.OrdinalIgnoreCase)
                || string.Equals(relativa, Path.GetFileName(nueva), StringComparison.OrdinalIgnoreCase)
                || Primera(relativa) is var p && (p.Equals(CarpetaCopiasPruebas, StringComparison.OrdinalIgnoreCase)
                    || p.Equals(CarpetaExternas, StringComparison.OrdinalIgnoreCase) || p.Equals(ComprobadorArranque.CarpetaCopiasEsquema, StringComparison.OrdinalIgnoreCase))));

        var copiados = 0;
        var enOrigen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fichero in Directory.EnumerateFiles(c.Origen, "*", SearchOption.AllDirectories))
        {
            var relativa = Path.GetRelativePath(c.Origen, fichero);
            if (ExcluirOrigen(relativa)) continue;
            enOrigen.Add(relativa);
            var destino = Path.Combine(c.Destino, relativa);
            var info = new FileInfo(fichero);
            var actual = new FileInfo(destino);
            if (actual.Exists && actual.Length == info.Length && actual.LastWriteTimeUtc == info.LastWriteTimeUtc) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(destino)!);
            CopiarFichero(fichero, destino);
            File.SetLastWriteTimeUtc(destino, info.LastWriteTimeUtc);
            copiados++;
        }
        foreach (var carpeta in Directory.EnumerateDirectories(c.Origen, "*", SearchOption.AllDirectories))
        {
            var relativa = Path.GetRelativePath(c.Origen, carpeta);
            if (!ExcluirOrigen(relativa)) Directory.CreateDirectory(Path.Combine(c.Destino, relativa));
        }

        var borrados = 0;
        if (!Directory.Exists(c.Destino)) return (copiados, 0);
        foreach (var fichero in Directory.EnumerateFiles(c.Destino, "*", SearchOption.AllDirectories).ToList())
        {
            var relativa = Path.GetRelativePath(c.Destino, fichero);
            if (Protegido(relativa) || enOrigen.Contains(relativa)) continue;
            File.Delete(fichero);
            borrados++;
        }
        foreach (var carpeta in Directory.EnumerateDirectories(c.Destino, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToList())
        {
            var relativa = Path.GetRelativePath(c.Destino, carpeta);
            if (Protegido(relativa) || Directory.Exists(Path.Combine(c.Origen, relativa)) || Directory.EnumerateFileSystemEntries(carpeta).Any()) continue;
            Directory.Delete(carpeta);
        }
        return (copiados, borrados);
    }

    /// <summary>Copia un fichero aunque esté abierto (la BD de producción lo está mientras se refresca).</summary>
    private static void CopiarFichero(string origen, string destino)
    {
        using var entrada = new FileStream(origen, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var salida = new FileStream(destino, FileMode.Create, FileAccess.Write, FileShare.None);
        entrada.CopyTo(salida);
    }

    /// <summary>Si alguien tiene abierta la BD: no se puede abrir en exclusiva o tiene un bloqueo de Access que no se puede borrar.</summary>
    private static bool EnUso(string bd)
    {
        try
        {
            using (new FileStream(bd, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
            var bloqueo = Path.ChangeExtension(bd, ".laccdb");
            if (File.Exists(bloqueo)) File.Delete(bloqueo);
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    /// <summary>ACE suelta el fichero un poco después de cerrar la última conexión.</summary>
    private static void ConReintentos(Action accion)
    {
        for (var i = 0; ; i++)
        {
            try { accion(); return; }
            catch (IOException) when (i < 10) { Thread.Sleep(300); }
        }
    }

    private static void BorrarSinFallar(string ruta)
    {
        try { if (File.Exists(ruta)) File.Delete(ruta); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>La misma regla que la carpeta de documentos de la empresa: ruta completa tal cual o, si es relativa, junto a la BD.</summary>
    internal static string RaizDocumentos(string rutaDocumentos, string rutaBd)
    {
        var ruta = rutaDocumentos.Trim();
        return Path.IsPathFullyQualified(ruta) ? Path.GetFullPath(ruta) : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(rutaBd)) ?? "", ruta));
    }

    internal static bool Dentro(string ruta, string carpeta)
    {
        var r = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ruta));
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(carpeta));
        return string.Equals(r, c, StringComparison.OrdinalIgnoreCase) || r.StartsWith(c + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string Primera(string relativa) => relativa.Split(Path.DirectorySeparatorChar, 2)[0];

    private static string? Recortar(string? texto, int maximo) => texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];

    private static Resultado<PlanRefresco> Fallo(string codigo, string mensaje, string? registro, string causa, string accion) =>
        Resultado<PlanRefresco>.Fallo(new Error(codigo, mensaje, registro, causa, accion));

    private static Resultado<InformeRefresco> FalloInforme(string codigo, string mensaje, string? registro, string causa, string accion) =>
        Resultado<InformeRefresco>.Fallo(new Error(codigo, mensaje, registro, causa, accion));
}
