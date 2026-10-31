using System.Diagnostics;
using System.Security.Cryptography;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Esquema;

/// <summary>Resultado de una actualización.</summary>
/// <param name="CopiaSeguridad">Copia hecha antes de cambiar nada; null si no hacía falta actualizar.</param>
public sealed record InformeActualizacion(
    string RutaBd,
    int VersionInicial,
    int VersionFinal,
    IReadOnlyList<string> Aplicadas,
    string? CopiaSeguridad);

/// <summary>
/// Aplica a una instalación las migraciones pendientes del catálogo (RF-ESQ-01/02): abre la BD en
/// exclusiva, hace y verifica una copia de seguridad, y registra cada intento en
/// LogMigracionesEsquema y cada migración terminada en VersionesEsquema. Access no deshace el DDL,
/// así que si una migración falla se para, queda registrada como ERROR y se indica la copia a restaurar.
/// </summary>
public sealed class ActualizadorEsquema
{
    private readonly IReadOnlyList<Migracion> _catalogo;
    private readonly Actor _actor;
    private readonly IReloj _reloj;
    private readonly string _versionAplicacion;

    public ActualizadorEsquema(Actor actor, IReadOnlyList<Migracion>? catalogo = null, IReloj? reloj = null, string? versionAplicacion = null)
    {
        _catalogo = catalogo ?? CatalogoMigraciones.Todas;
        _actor = actor;
        _reloj = reloj ?? RelojSistema.Instancia;
        _versionAplicacion = versionAplicacion ?? VersionAplicacion();
        ComprobarCatalogo(_catalogo);
    }

    public async Task<Resultado<InformeActualizacion>> ActualizarAsync(string rutaBd, string carpetaCopias)
    {
        var exclusiva = new BaseDatosAccess(rutaBd, ModoApertura.Exclusivo);
        DiagnosticoEsquema diagnostico;
        try
        {
            diagnostico = await DiagnosticadorEsquema.DiagnosticarAsync(exclusiva, _catalogo);
        }
        catch (BloqueoException ex)
        {
            return Resultado<InformeActualizacion>.Fallo(ex.Error with
            {
                Mensaje = "No puedo actualizar la base de datos porque hay alguien usándola.",
                AccionCorrectora = "Pide a todos los usuarios que cierren ProContaVF (también en las otras sesiones de Escritorio remoto) y vuelve a ejecutar el ActualizadorEsquema."
            });
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<InformeActualizacion>.Fallo(ex.Error);
        }

        if (diagnostico.Estado == EstadoEsquema.Actualizada)
            return Resultado<InformeActualizacion>.Correcto(new(exclusiva.Ruta, diagnostico.VersionBd, diagnostico.VersionBd, [], null));
        if (!diagnostico.SePuedeActualizar)
            return Resultado<InformeActualizacion>.Fallo(diagnostico.ComoError()!);

        var copia = HacerCopia(exclusiva.Ruta, carpetaCopias, diagnostico.VersionBd);
        if (!copia.EsCorrecto) return Resultado<InformeActualizacion>.Fallo(copia.Errores);

        var aplicadas = new List<string>();
        foreach (var migracion in diagnostico.Pendientes)
        {
            var aplicada = await AplicarAsync(exclusiva, migracion, copia.Valor);
            if (!aplicada.EsCorrecto) return Resultado<InformeActualizacion>.Fallo(aplicada.Errores);
            aplicadas.Add(migracion.Id);
        }

        var final = await DiagnosticadorEsquema.DiagnosticarAsync(exclusiva, _catalogo);
        if (!final.EsCompatible) return Resultado<InformeActualizacion>.Fallo(final.ComoError()!);
        return Resultado<InformeActualizacion>.Correcto(new(exclusiva.Ruta, diagnostico.VersionBd, final.VersionBd, aplicadas, copia.Valor));
    }

    private async Task<Resultado> AplicarAsync(IBaseDatos bd, Migracion migracion, string copia)
    {
        var inicio = _reloj.Ahora;
        var cronometro = Stopwatch.StartNew();
        var idEjecucion = await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO LogMigracionesEsquema (IdMigracion, FechaHoraInicio, Estado, PasoActual, VersionAplicacion, Equipo, Operador, CopiaSeguridad) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                migracion.Id, inicio, "INICIADA", "Preparando la migración", _versionAplicacion, Environment.MachineName, _actor.ToString(), copia);
            return await s.UltimoIdAsync();
        });

        var pasoActual = "Preparando la migración";
        try
        {
            await bd.EjecutarAsync(async s =>
            {
                foreach (var paso in migracion.Pasos)
                {
                    pasoActual = paso.Descripcion;
                    await s.EjecutarAsync("UPDATE LogMigracionesEsquema SET PasoActual = ? WHERE IdEjecucion = ?", Recortar(pasoActual, 255), idEjecucion);
                    if (await paso.YaAplicadoAsync(s, s.LeerEsquema())) continue;
                    await paso.AplicarAsync(s);
                }
                await s.EjecutarAsync(
                    "INSERT INTO VersionesEsquema (IdMigracion, OrdenMigracion, Descripcion, FechaHoraAplicacion, VersionAplicacion, HashMigracion, DuracionMilisegundos, CopiaSeguridad) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                    migracion.Id, migracion.Orden, Recortar(migracion.Descripcion, 255), _reloj.Ahora, _versionAplicacion, migracion.Hash, (int)cronometro.ElapsedMilliseconds, Recortar(copia, 255));
                await s.EjecutarAsync("UPDATE LogMigracionesEsquema SET Estado = ?, PasoActual = ?, FechaHoraFin = ? WHERE IdEjecucion = ?",
                    "COMPLETADA", "Finalizada", _reloj.Ahora, idEjecucion);
                return true;
            });
            return Resultado.Correcto();
        }
        catch (Exception ex) when (ex is ExcepcionDatos or InvalidOperationException or ArgumentException)
        {
            var mensaje = ex is ExcepcionDatos datos ? datos.Error.Causa ?? datos.Error.Mensaje : ex.Message;
            try
            {
                await bd.EjecutarAsync(s => s.EjecutarAsync(
                    "UPDATE LogMigracionesEsquema SET Estado = ?, FechaHoraFin = ?, DescripcionError = ?, DetalleTecnico = ? WHERE IdEjecucion = ?",
                    "ERROR", _reloj.Ahora, mensaje, ex.ToString(), idEjecucion));
            }
            catch (ExcepcionDatos)
            {
                // Si ni siquiera se puede registrar el error, el diagnóstico verá la migración INICIADA.
            }
            return Resultado.Fallo(new Error("Esquema.MigracionFallida",
                $"No he podido terminar el cambio de estructura {migracion.Id}.",
                bd.Ruta,
                $"Falló el paso «{pasoActual}»: {mensaje}",
                $"La BD ha quedado a medias. Restaura la copia de seguridad {copia} sobre la BD y avisa al responsable antes de repetir la actualización.",
                ex.ToString()));
        }
    }

    private Resultado<string> HacerCopia(string rutaBd, string carpetaCopias, int version)
    {
        try
        {
            Directory.CreateDirectory(carpetaCopias);
            var nombre = $"{Path.GetFileNameWithoutExtension(rutaBd)}.pre-esquema-v{version}-{_reloj.Ahora:yyyyMMdd-HHmmss}{Path.GetExtension(rutaBd)}";
            var destino = Path.Combine(carpetaCopias, nombre);
            File.Copy(rutaBd, destino, overwrite: false);
            if (!string.Equals(Sha256(rutaBd), Sha256(destino), StringComparison.Ordinal))
                return Resultado<string>.Fallo(new Error("Esquema.CopiaDistinta",
                    "La copia de seguridad no es idéntica a la base de datos; no cambio nada.", destino,
                    "La huella SHA-256 de la copia no coincide con la del original.",
                    "Comprueba el espacio libre y el disco de la carpeta de copias y vuelve a ejecutar el ActualizadorEsquema."));
            return Resultado<string>.Correcto(destino);
        }
        catch (IOException ex)
        {
            return Resultado<string>.Fallo(new Error("Esquema.SinCopia",
                "No he podido hacer la copia de seguridad, así que no cambio nada.", carpetaCopias,
                ex.Message, "Comprueba que la carpeta de copias existe, tiene espacio y permiso de escritura, y que nadie tiene abierta la BD.", ex.ToString()));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Resultado<string>.Fallo(new Error("Esquema.SinCopia",
                "No tengo permiso para hacer la copia de seguridad, así que no cambio nada.", carpetaCopias,
                ex.Message, "Usa una carpeta de copias en la que tengas permiso de escritura.", ex.ToString()));
        }
    }

    public static string Sha256(string ruta)
    {
        using var fichero = File.OpenRead(ruta);
        return Convert.ToHexString(SHA256.HashData(fichero));
    }

    private static void ComprobarCatalogo(IReadOnlyList<Migracion> catalogo)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordenAnterior = 261001002; // última migración heredada
        foreach (var m in catalogo)
        {
            if (!ids.Add(m.Id)) throw new InvalidOperationException($"Migración repetida en el catálogo: {m.Id}.");
            if (m.Orden <= ordenAnterior) throw new InvalidOperationException($"El orden de {m.Id} debe ser mayor que {ordenAnterior}.");
            ordenAnterior = m.Orden;
        }
    }

    private static string Recortar(string texto, int longitud) => texto.Length <= longitud ? texto : texto[..longitud];

    private static string VersionAplicacion() =>
        typeof(ActualizadorEsquema).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion ?? "ProContaVF-local";
}
