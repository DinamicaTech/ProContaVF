using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Presentacion;

/// <summary>
/// Lo que una declaración informativa entrega a la pieza común de TGVI Online: el registro tipo 1 (declarante) y los tipo 2
/// (declarados), cada uno con la longitud de su diseño; el número identificativo de la declaración que va en el tipo 1; el total
/// declarado (lo que se guarda como resultado) y el resumen para la conformidad.
/// </summary>
/// <param name="ComplementariaDe">Número identificativo de la declaración anterior si es complementaria o sustitutiva (va en el tipo 1).</param>
public sealed record DeclaracionInformativa(string Modelo, int Ejercicio, string Periodo, string NifSociedad, string RazonSocial, string RegistroTipo1,
    IReadOnlyList<string> RegistrosTipo2, string NumeroJustificante, decimal Total, IReadOnlyList<(string Etiqueta, string Valor)> Resumen, string? ComplementariaDe = null)
{
    public string Titulo => $"Modelo {Modelo} · {(Periodo == "0A" ? "" : Periodo + " ")}{Ejercicio}" + (ComplementariaDe is null ? "" : " · sustitutiva o complementaria");

    /// <summary>NIF del declarante leído del registro tipo 1 (posiciones 9 a 17 en todas las informativas).</summary>
    public string NifLeido => RegistroTipo1.Length >= 17 ? RegistroTipo1.Substring(8, 9).Trim().TrimStart('0') : "";

    /// <summary>Bloques de hasta 40.000 registros tipo 2, en orden.</summary>
    public IReadOnlyList<string> Bloques => [.. RegistrosTipo2.Chunk(ServiciosTgvi.RegistrosPorBloque).Select(b => string.Concat(b))];
}

/// <summary>
/// Una declaración validada en TGVI Online y pendiente de presentar: el envío (idenvio), los registros correctos y erróneos, los errores
/// de los erróneos (uno por registro), los avisos, la vista previa (PDF con marca de agua) y lo que impide presentar.
/// </summary>
public sealed record BorradorTgvi(DeclaracionInformativa Declaracion, EntornoAeat Entorno, string? IdEnvio, int Correctos, int Erroneos, IReadOnlyList<string> Errores,
    IReadOnlyList<string> AvisosAeat, byte[]? VistaPrevia, TitularCertificado? Presentador, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos)
{
    public bool SePuedePresentar => Bloqueos.Count == 0 && IdEnvio is not null;
}

/// <summary>
/// Pieza común de presentación de las declaraciones informativas por TGVI Online (contabilidad/modelos-tributarios/presentacion;
/// sources/aeat/Especificaciones_TGVI_Online.pdf, versión 2.8), con el mismo certificado, entorno ligado a la BD, conformidad y registro
/// en PresentacionesAeat que la presentación directa:
/// <list type="number">
/// <item><see cref="PrepararAsync"/>: InicializarEnvio con el tipo 1, EnviarDatos con los tipo 2 en bloques de 40.000 en orden y sin
/// paralelizar, RecuperarErrores y RecuperarAvisos si los hay y VistaPrevia. Validar no presenta nada.</item>
/// <item><see cref="PresentarAsync"/>: con la conformidad, PresentarEnvio (firma no criptográfica): solo se presentan los registros
/// correctos; los erróneos quedan para una complementaria (<see cref="ErroneosAsync"/> da su fichero). Guarda el CSV, el expediente y el
/// número identificativo y archiva el PDF justificante.</item>
/// <item><see cref="BajaAsync"/>: da de baja una declaración presentada (para una sustitutiva).</item>
/// </list>
/// </summary>
public sealed class ServicioTgvi(IBaseDatos bd, IClienteTgvi cliente, Func<string, Task<X509Certificate2?>>? certificado = null, IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly ServicioPresentacionAeat _directa = new(bd, new ClienteAeatHttp(), certificado, reloj);

    /// <summary>Modelos que admiten declaración negativa (sin registros tipo 2, numbloques 0).</summary>
    public static readonly IReadOnlySet<string> AdmitenNegativa = new HashSet<string> { "180", "187", "188", "190", "193", "194", "196" };

    public Task<EntornoAeat> EntornoAsync(CancellationToken c = default) => _directa.EntornoAsync(c);

    private Task<X509Certificate2?> CertificadoAsync(string idSociedad) =>
        certificado is not null ? certificado(idSociedad) : CertificadosAeat.DeSociedadAsync(bd, idSociedad);

    /// <summary>
    /// Número identificativo de la declaración (13 cifras): el modelo, los dos últimos dígitos del ejercicio, el periodo («00» anual, «01»
    /// a «04» trimestral) y un secuencial de 6 cifras, uno más que las presentadas desde el programa para ese modelo, ejercicio y periodo;
    /// en el entorno de pruebas empieza en 900001 para no coincidir nunca con uno de producción.
    /// </summary>
    public async Task<string> NumeroJustificanteAsync(string idSociedad, string modelo, int ejercicio, string periodo, CancellationToken c = default)
    {
        var entorno = await EntornoAsync(c);
        var previas = (await _directa.PresentadasAsync(idSociedad, modelo, ejercicio, c)).Count(p => p.Periodo == periodo && p.Entorno == ServiciosAeat.Codigo(entorno));
        return NumeroJustificante(modelo, ejercicio, periodo, previas + 1 + (entorno == EntornoAeat.Pruebas ? 900000 : 0));
    }

    public static string NumeroJustificante(string modelo, int ejercicio, string periodo, int secuencial)
    {
        var per = periodo.Length == 2 && periodo[1] == 'T' && char.IsAsciiDigit(periodo[0]) ? "0" + periodo[0] : "00";
        return modelo + (ejercicio % 100).ToString("00", CultureInfo.InvariantCulture) + per + secuencial.ToString("000000", CultureInfo.InvariantCulture);
    }

    public async Task<BorradorTgvi> PrepararAsync(ContextoSociedadEjercicio ctx, DeclaracionInformativa d, CancellationToken c = default)
    {
        var bloqueos = new List<string>();
        var avisos = new List<string>();
        var entorno = await EntornoAsync(c);
        if (!string.Equals(d.NifLeido, d.NifSociedad.Trim().TrimStart('0'), StringComparison.OrdinalIgnoreCase))
            bloqueos.Add($"El NIF del declarante del registro tipo 1 ({d.NifLeido}) no es el de la sociedad ({d.NifSociedad}). Revisa el NIF en Configuración › Parámetros y tablas › Empresas.");
        if (d.RegistrosTipo2.Count == 0 && !AdmitenNegativa.Contains(d.Modelo))
            bloqueos.Add($"El modelo {d.Modelo} no tiene nada que declarar y no admite declaración negativa: no hay que presentarlo.");
        using var cert = await CertificadoAsync(ctx.IdSociedad);
        if (cert is null)
        {
            bloqueos.Add("La sociedad no tiene certificado en este equipo. Elígelo en Configuración › Parámetros y tablas › «Certificado de la empresa» (el mismo de VeriFactu).");
            return new BorradorTgvi(d, entorno, null, 0, 0, [], [], null, null, bloqueos, avisos);
        }
        var titular = TitularCertificado.De(cert);
        if (titular.Caduca < _reloj.Ahora) bloqueos.Add($"El certificado de la sociedad caducó el {titular.Caduca:dd/MM/yyyy}: renuévalo y vuelve a elegirlo en Parámetros y tablas.");
        if (bloqueos.Count > 0) return new BorradorTgvi(d, entorno, null, 0, 0, [], [], null, titular, bloqueos, avisos);

        string? idEnvio = null;
        int ok = 0, ko = 0;
        var errores = new List<string>();
        var avisosAeat = new List<string>();
        byte[]? vista = null;
        try
        {
            var bloques = d.Bloques;
            var ini = await cliente.InicializarAsync(entorno, cert, d.Modelo, d.Ejercicio.ToString(CultureInfo.InvariantCulture), d.Periodo, d.NifSociedad, bloques.Count, d.RegistroTipo1, c);
            if (ini.Codigo == 8888)
                bloqueos.Add($"La AEAT dice que este envío se puede validar pero no presentar: {ini.Mensaje} Suele ser que el {d.Modelo} ya está presentado (haz una sustitutiva) o que el justificante anterior no existe.");
            else if (!ini.Correcta)
            {
                bloqueos.Add($"La AEAT rechaza el registro del declarante ({ini.Codigo}): {ini.Mensaje}");
                return new BorradorTgvi(d, entorno, null, 0, 0, [], [], null, titular, bloqueos, avisos);
            }
            idEnvio = ini.IdEnvio;
            if (idEnvio is null)
            {
                bloqueos.Add("La AEAT no ha devuelto el identificador del envío.");
                return new BorradorTgvi(d, entorno, null, 0, 0, [], [], null, titular, bloqueos, avisos);
            }
            var hayAvisos = false;
            for (var i = 0; i < bloques.Count; i++)
            {
                var r = await cliente.EnviarDatosAsync(entorno, cert, idEnvio, i + 1, bloques[i], c);
                if (!r.Correcta)
                {
                    bloqueos.Add($"La AEAT rechaza el bloque {i + 1} ({r.Codigo}): {r.Mensaje}");
                    return new BorradorTgvi(d, entorno, idEnvio, ok, ko, errores, avisosAeat, null, titular, bloqueos, avisos);
                }
                ok = r.Entero("totalt2ok") ?? ok;
                ko = r.Entero("totalt2ko") ?? ko;
                hayAvisos |= r.Cabecera("avisos") == "S";
            }
            if (ko > 0)
            {
                var e = await cliente.RecuperarAsync(entorno, cert, "RecuperarErrores", idEnvio, c);
                errores.AddRange(Lineas(e.Texto));
                if (ok == 0) bloqueos.Add($"Todos los registros ({ko}) tienen errores: corrígelos antes de presentar.");
                else avisos.Add($"{ko} de {ok + ko} registros tienen errores: si presentas, solo van los {ok} correctos y los erróneos quedan para una complementaria.");
            }
            if (hayAvisos) avisosAeat.AddRange(Lineas((await cliente.RecuperarAsync(entorno, cert, "RecuperarAvisos", idEnvio, c)).Texto));
            var v = await cliente.VistaPreviaAsync(entorno, cert, idEnvio, c);
            if (v.Correcta && v.Cuerpo.Length > 0) vista = v.Cuerpo;
            else avisos.Add($"La AEAT no ha dado la vista previa ({v.Codigo}: {v.Mensaje}).");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            bloqueos.Add($"No he podido conectar con TGVI Online ({ex.Message}). " +
                (entorno == EntornoAeat.Pruebas ? "El entorno de pruebas de la AEAT solo funciona los días laborables de 8:00 a 15:00." : "Comprueba la conexión a internet y vuelve a intentarlo."));
        }
        return new BorradorTgvi(d, entorno, idEnvio, ok, ko, errores, avisosAeat, vista, titular, bloqueos, avisos);
    }

    /// <summary>Presenta el envío validado con la conformidad del operador: solo los registros correctos. Un rechazo no guarda nada.</summary>
    public async Task<Resultado<PresentacionHecha>> PresentarAsync(ContextoSociedadEjercicio ctx, BorradorTgvi b, bool conforme, Actor actor, CancellationToken c = default)
    {
        var d = b.Declaracion;
        Resultado<PresentacionHecha> Fallo(string mensaje, string? causa, string? solucion) =>
            Resultado<PresentacionHecha>.Fallo(new Error("Presentacion.NoPresentada", mensaje, d.Titulo, causa, solucion));
        if (!conforme) return Fallo("No se ha presentado: falta tu conformidad.", "La AEAT entiende que el propio envío acepta los datos.", "Revisa la vista previa y marca «Conforme».");
        if (!b.SePuedePresentar) return Fallo("No se puede presentar.", string.Join(" ", b.Bloqueos), "Resuelve lo que se indica y vuelve a validar.");
        var entorno = await EntornoAsync(c);
        if (entorno != b.Entorno) return Fallo("El entorno de la base de datos ha cambiado desde que validaste.", null, "Vuelve a validar.");
        using var cert = await CertificadoAsync(ctx.IdSociedad);
        if (cert is null) return Fallo("La sociedad no tiene certificado en este equipo.", null, "Elígelo en Configuración › Parámetros y tablas › «Certificado de la empresa».");
        var titular = TitularCertificado.De(cert);
        RespuestaTgvi? r = null;
        string? firNif = null, firNombre = null;
        try
        {
            foreach (var (nif, nombre) in titular.Firmantes)
            {
                r = await cliente.PresentarAsync(entorno, cert, b.IdEnvio!, nif, nombre, c);
                firNif = nif; firNombre = nombre;
                if (r.Correcta || r.Codigo is not (4005 or 4006 or 4010)) break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Fallo($"No he podido conectar con TGVI Online ({ex.Message}).", "No consta que se haya presentado, pero si la conexión se cortó después de enviar, la AEAT podría haberlo recibido.",
                "Antes de repetirlo, consulta lo presentado en la Sede.");
        }
        if (r is null) return Fallo("El certificado no tiene NIF de titular.", null, "Elige otro certificado en Parámetros y tablas.");
        if (!r.Correcta || string.IsNullOrWhiteSpace(r.Csv))
            return Resultado<PresentacionHecha>.Fallo(new Error("Presentacion.Rechazada", $"La AEAT no ha aceptado la presentación ({r.Codigo}): no se ha presentado nada.", d.Titulo, r.Mensaje,
                "Corrige lo que indica la AEAT y vuelve a validar.", r.Json));

        var fecha = _reloj.Ahora;
        var avisos = new List<string>();
        if (b.Erroneos > 0) avisos.Add($"Se han presentado {r.Entero("numeroregistros") ?? b.Correctos} registros; {b.Erroneos} erróneos quedan para una complementaria.");
        avisos.AddRange(b.AvisosAeat.Select(a => $"Aviso de la AEAT: {a}"));
        var url = ServiciosTgvi.Justificante(entorno, r.Csv!);
        var guardada = new PresentacionGuardada(0, ctx.IdSociedad, d.Modelo, d.Ejercicio, d.Periodo, ServiciosAeat.Codigo(entorno), fecha, r.Csv!, r.Expediente, d.NumeroJustificante,
            d.ComplementariaDe, null, 0m, d.Total, firNif, Recortar(firNombre, 120), Recortar(actor.ToString(), 100), avisos.Count == 0 ? null : string.Join(Environment.NewLine, avisos), url, null);
        int id;
        try { id = await GuardarAsync(guardada, d, b, r.Json); }
        catch (Exception ex)
        {
            return Resultado<PresentacionHecha>.Fallo(new Error("Presentacion.NoGuardada",
                $"El {d.Modelo} se ha presentado (CSV {r.Csv}, expediente {r.Expediente}), pero no he podido guardarlo en la base de datos.", d.Titulo, ex.Message,
                $"Apunta el CSV {r.Csv}: el justificante se descarga en la Sede con él.", r.Json));
        }
        guardada = guardada with { Id = id };
        var propios = new List<string>();
        try
        {
            var pdf = await cliente.DescargarAsync(url, cert, c);
            var ruta = await RutaAsync(ctx.IdSociedad, d, entorno);
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllBytesAsync(ruta, pdf, c);
            await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE PresentacionesAeat SET RutaPdf = ? WHERE IdPresentacionAeat = ?", Recortar(ruta, 255), id));
            guardada = guardada with { RutaPdf = ruta };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            propios.Add($"Presentado, pero no he podido descargar o archivar el PDF justificante ({ex.Message}). Se puede descargar en la Sede con el CSV {r.Csv}.");
        }
        return Resultado<PresentacionHecha>.Correcto(new PresentacionHecha(guardada, [.. avisos, .. propios]));
    }

    /// <summary>Fichero con los registros erróneos del envío en formato de complementaria (tipo 1 recalculado), para corregirlos y volver a validar.</summary>
    public async Task<Resultado<string>> ErroneosAsync(ContextoSociedadEjercicio ctx, BorradorTgvi b, CancellationToken c = default)
    {
        using var cert = await CertificadoAsync(ctx.IdSociedad);
        if (cert is null || b.IdEnvio is null) return Resultado<string>.Fallo(new Error("Presentacion.SinEnvio", "No hay envío validado o certificado.", b.Declaracion.Titulo));
        var r = await cliente.RecuperarAsync(b.Entorno, cert, "RecuperarErroneos", b.IdEnvio, c);
        return r.Correcta ? Resultado<string>.Correcto(r.Texto)
            : Resultado<string>.Fallo(new Error("Presentacion.SinErroneos", $"La AEAT no ha dado los registros erróneos ({r.Codigo}).", b.Declaracion.Titulo, r.Mensaje));
    }

    /// <summary>Da de baja en la AEAT una declaración informativa presentada (para presentar después una sustitutiva). Queda anotado en sus avisos.</summary>
    public async Task<Resultado<string>> BajaAsync(ContextoSociedadEjercicio ctx, PresentacionGuardada p, Actor actor, CancellationToken c = default)
    {
        var registro = $"Modelo {p.Modelo} {p.Periodo} {p.Ejercicio}";
        var entorno = await EntornoAsync(c);
        if (ServiciosAeat.Codigo(entorno) != p.Entorno)
            return Resultado<string>.Fallo(new Error("Presentacion.Entorno", "La presentación es de otro entorno que el de esta base de datos.", registro));
        if (string.IsNullOrWhiteSpace(p.Expediente))
            return Resultado<string>.Fallo(new Error("Presentacion.SinExpediente", "La presentación no tiene expediente: no se puede dar de baja desde el programa.", registro, null, "Dala de baja en la Sede."));
        using var cert = await CertificadoAsync(ctx.IdSociedad);
        if (cert is null) return Resultado<string>.Fallo(new Error("Presentacion.SinCertificado", "La sociedad no tiene certificado en este equipo.", registro));
        var nif = await bd.EjecutarAsync(s => s.EscalarAsync("SELECT CIF FROM Sociedades WHERE IdSociedad = ?", ctx.IdSociedad)) as string ?? "";
        var (firNif, firNombre) = TitularCertificado.De(cert).Firmantes.First();
        var r = await cliente.BajaAsync(entorno, cert, p.Modelo, p.Ejercicio.ToString(CultureInfo.InvariantCulture), p.Periodo, nif.Trim(), p.Expediente, firNif, firNombre, c);
        if (!r.Correcta)
            return Resultado<string>.Fallo(new Error("Presentacion.BajaRechazada", $"La AEAT no ha dado de baja la declaración ({r.Codigo}).", registro, r.Mensaje));
        var nota = $"Dada de baja el {_reloj.Ahora:dd/MM/yyyy HH:mm} por {actor}, expediente de la baja {r.Expediente}.";
        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE PresentacionesAeat SET Avisos = ? WHERE IdPresentacionAeat = ?", string.IsNullOrEmpty(p.Avisos) ? nota : p.Avisos + Environment.NewLine + nota, p.Id));
        return Resultado<string>.Correcto(r.Expediente ?? "");
    }

    private Task<int> GuardarAsync(PresentacionGuardada p, DeclaracionInformativa d, BorradorTgvi b, string respuesta) => bd.EjecutarAsync(async s =>
    {
        var fichero = d.RegistroTipo1 + string.Concat(d.RegistrosTipo2);
        await s.EjecutarAsync(
            "INSERT INTO PresentacionesAeat (IdSociedad, Modelo, Ejercicio, Periodo, Entorno, FechaHora, Csv, Expediente, Justificante, ComplementariaDe, FormaPago, Importe, Resultado, " +
            "NifPresentador, Presentador, Operador, Fichero, Respuesta, Avisos, UrlPdf, RutaPdf) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            p.IdSociedad, p.Modelo, (short)p.Ejercicio, p.Periodo, p.Entorno, p.FechaHora, Recortar(p.Csv, 16), Recortar(p.Expediente, 25), Recortar(p.Justificante, 13),
            Recortar(p.ComplementariaDe, 13), null, 0m, p.Resultado, Recortar(p.NifPresentador, 9), p.Presentador, p.Operador, fichero,
            $"{{\"idenvio\":\"{b.IdEnvio}\",\"correctos\":{b.Correctos},\"erroneos\":{b.Erroneos},\"presentacion\":{respuesta}}}", p.Avisos, Recortar(p.UrlPdf, 255), null);
        return await s.UltimoIdAsync();
    });

    private async Task<string> RutaAsync(string idSociedad, DeclaracionInformativa d, EntornoAeat entorno)
    {
        var ruta = await bd.EjecutarAsync(s => s.EscalarAsync("SELECT RutaDocumentos FROM Sociedades WHERE IdSociedad = ?", idSociedad));
        var raiz = ServicioPresentacionAeat.CarpetaEmpresa(bd.Ruta, ruta as string);
        var nombre = $"{(entorno == EntornoAeat.Pruebas ? "PRUEBAS " : "")}{d.Modelo} {d.Ejercicio} {d.Periodo} {d.NumeroJustificante}.pdf";
        return Path.Combine(raiz, d.Ejercicio.ToString(CultureInfo.InvariantCulture), "Modelos AEAT", d.Modelo, string.Concat(nombre.Split(Path.GetInvalidFileNameChars())));
    }

    private static IReadOnlyList<string> Lineas(string texto) => [.. texto.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0)];

    private static string? Recortar(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
