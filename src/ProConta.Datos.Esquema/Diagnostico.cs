using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Esquema;

public enum EstadoEsquema
{
    /// <summary>La BD tiene la versión que espera el programa.</summary>
    Actualizada,
    /// <summary>Le faltan migraciones del catálogo; el ActualizadorEsquema puede aplicarlas.</summary>
    ConPendientes,
    /// <summary>Tiene migraciones que este programa no conoce: es de una versión posterior o la cambió otra herramienta.</summary>
    MasNuevaQueElPrograma,
    /// <summary>Una migración empezó y no terminó, o una aplicada no está completa: no se reejecuta a ciegas.</summary>
    MigracionParcial,
    /// <summary>Es una BD de ProConta pero no contiene todas las migraciones de la versión 1.</summary>
    LineaBaseIncompleta,
    /// <summary>No tiene el control de versiones de ProConta.</summary>
    NoEsProConta
}

public sealed record DiagnosticoEsquema(
    string RutaBd,
    EstadoEsquema Estado,
    int VersionBd,
    int VersionPrograma,
    IReadOnlyList<Migracion> Pendientes,
    IReadOnlyList<string> Desconocidas,
    IReadOnlyList<string> Parciales,
    IReadOnlyList<string> HeredadasAusentes)
{
    public bool EsCompatible => Estado == EstadoEsquema.Actualizada;

    public bool SePuedeActualizar => Estado == EstadoEsquema.ConPendientes;

    /// <summary>Error para el operador cuando la BD no es compatible; null si lo es.</summary>
    public Error? ComoError() => Estado switch
    {
        EstadoEsquema.Actualizada => null,
        EstadoEsquema.ConPendientes => new Error("Esquema.Pendiente",
            $"La base de datos está en la versión {VersionBd} del esquema y este programa necesita la {VersionPrograma}.",
            RutaBd,
            $"Faltan {Pendientes.Count} cambios de estructura: {string.Join(", ", Pendientes.Select(p => p.Id))}.",
            "Ejecuta el ActualizadorEsquema (hace una copia de seguridad antes de cambiar nada) y vuelve a abrir el programa."),
        EstadoEsquema.MasNuevaQueElPrograma => new Error("Esquema.MasNueva",
            "La base de datos es de una versión del programa más nueva que esta.",
            RutaBd,
            $"Tiene cambios de estructura que no conozco: {string.Join(", ", Desconocidas)}.",
            "Instala la versión del programa que corresponde a esta base de datos; no la abras con esta versión."),
        EstadoEsquema.MigracionParcial => new Error("Esquema.Parcial",
            "La base de datos tiene un cambio de estructura a medias.",
            RutaBd,
            $"No terminó: {string.Join("; ", Parciales)}. Access no deshace los cambios de estructura, así que no lo repito a ciegas.",
            "Restaura la copia de seguridad que hizo el ActualizadorEsquema antes de ese cambio (carpeta de copias) y vuelve a ejecutarlo."),
        EstadoEsquema.LineaBaseIncompleta => new Error("Esquema.LineaBase",
            "La base de datos no tiene la estructura de la versión 1 de ProContaVF.",
            RutaBd,
            $"Le faltan migraciones heredadas: {string.Join(", ", HeredadasAusentes)}.",
            "Comprueba que la ubicación de datos apunta a la BD de ProContaVF correcta."),
        _ => new Error("Esquema.NoEsProConta",
            "El fichero no es una base de datos de ProContaVF.",
            RutaBd,
            "No tiene la tabla VersionesEsquema que registra su versión.",
            "Selecciona la BD de ProContaVF en la configuración de la ubicación de datos.")
    };

    public string Resumen()
    {
        var lineas = new List<string>
        {
            $"BD: {RutaBd}",
            $"Estado: {Estado}",
            $"Versión de la BD: {VersionBd} · versión del programa: {VersionPrograma}"
        };
        if (Pendientes.Count > 0) lineas.Add("Pendientes: " + string.Join(", ", Pendientes.Select(p => p.Id)));
        if (Desconocidas.Count > 0) lineas.Add("Desconocidas: " + string.Join(", ", Desconocidas));
        if (Parciales.Count > 0) lineas.Add("Parciales: " + string.Join("; ", Parciales));
        if (HeredadasAusentes.Count > 0) lineas.Add("Heredadas ausentes: " + string.Join(", ", HeredadasAusentes));
        if (ComoError() is { } error) lineas.Add("Qué hacer: " + error.AccionCorrectora);
        return string.Join(Environment.NewLine, lineas);
    }
}

/// <summary>Inspecciona la versión y la estructura de una BD sin modificarla.</summary>
public static class DiagnosticadorEsquema
{
    public static Task<DiagnosticoEsquema> DiagnosticarAsync(IBaseDatos bd, IReadOnlyList<Migracion>? catalogo = null) =>
        bd.EjecutarAsync(s => DiagnosticarAsync(s, bd.Ruta, catalogo ?? CatalogoMigraciones.Todas));

    internal static async Task<DiagnosticoEsquema> DiagnosticarAsync(ISesionDatos sesion, string ruta, IReadOnlyList<Migracion> catalogo)
    {
        var versionPrograma = LineaBase.Version + catalogo.Count;
        var esquema = sesion.LeerEsquema();
        if (!esquema.ExisteTabla("VersionesEsquema"))
            return new(ruta, EstadoEsquema.NoEsProConta, 0, versionPrograma, [], [], [], []);

        var aplicadas = (await sesion.ConsultarAsync("SELECT IdMigracion FROM VersionesEsquema", f => f.Texto("IdMigracion")!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var heredadasAusentes = LineaBase.MigracionesHeredadas.Where(id => !aplicadas.Contains(id)).ToList();
        var conocidas = LineaBase.MigracionesHeredadas.Concat(catalogo.Select(m => m.Id)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desconocidas = aplicadas.Where(id => !conocidas.Contains(id)).OrderBy(id => id).ToList();

        var parciales = new List<string>();
        if (esquema.ExisteTabla("LogMigracionesEsquema"))
        {
            var ultimos = await sesion.ConsultarAsync(
                "SELECT IdMigracion, Estado, PasoActual, IdEjecucion FROM LogMigracionesEsquema ORDER BY IdEjecucion",
                f => (Id: f.Texto("IdMigracion")!, Estado: f.Texto("Estado") ?? "", Paso: f.Texto("PasoActual")));
            foreach (var ultimo in ultimos.GroupBy(u => u.Id, StringComparer.OrdinalIgnoreCase).Select(g => g.Last()))
                if (!aplicadas.Contains(ultimo.Id) && !string.Equals(ultimo.Estado, "COMPLETADA", StringComparison.OrdinalIgnoreCase))
                    parciales.Add($"{ultimo.Id} quedó en estado {ultimo.Estado} ({ultimo.Paso})");
        }

        // Las migraciones del catálogo se aplican en orden: la versión es el tramo inicial aplicado.
        var version = LineaBase.Version;
        var huecoEncontrado = false;
        var pendientes = new List<Migracion>();
        foreach (var migracion in catalogo)
        {
            if (aplicadas.Contains(migracion.Id))
            {
                if (huecoEncontrado)
                    parciales.Add($"{migracion.Id} está aplicada pero falta una anterior");
                else
                    version++;
                foreach (var paso in migracion.Pasos)
                    if (!await paso.YaAplicadoAsync(sesion, esquema))
                        parciales.Add($"{migracion.Id} consta como aplicada pero no está hecho «{paso.Descripcion}»");
            }
            else
            {
                huecoEncontrado = true;
                pendientes.Add(migracion);
            }
        }

        var estado =
            heredadasAusentes.Count > 0 ? EstadoEsquema.LineaBaseIncompleta :
            parciales.Count > 0 ? EstadoEsquema.MigracionParcial :
            desconocidas.Count > 0 ? EstadoEsquema.MasNuevaQueElPrograma :
            pendientes.Count > 0 ? EstadoEsquema.ConPendientes :
            EstadoEsquema.Actualizada;

        return new(ruta, estado, version, versionPrograma, pendientes, desconocidas, parciales, heredadasAusentes);
    }
}
