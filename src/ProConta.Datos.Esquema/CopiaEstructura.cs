using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;

namespace ProConta.Datos.Esquema;

/// <summary>
/// Crea, a partir de una BD, una copia con la misma estructura y sin datos de negocio: se copia el
/// fichero y se vacían sus tablas propias. Sirve de BD de pruebas (con datos sintéticos) y de
/// maestra vacía. Nunca modifica el origen y nunca toca tablas vinculadas, que apuntan a otros ficheros.
/// </summary>
public static class CopiaEstructura
{
    /// <summary>Tablas técnicas que se conservan: control de versiones y catálogos sin datos personales.</summary>
    public static readonly IReadOnlySet<string> TablasConservadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "VersionesEsquema", "LogMigracionesEsquema", "TiposDeIVA", "TiposCuentaContable", "EstadosFactura",
        "TiposFactura", "TiposOperacion", "TiposCorreo", "TiposActivo"
    };

    public static async Task CrearAsync(string origen, string destino)
    {
        var rutaOrigen = Path.GetFullPath(origen);
        var rutaDestino = Path.GetFullPath(destino);
        if (string.Equals(rutaOrigen, rutaDestino, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("El destino de la copia no puede ser la propia BD de origen.", nameof(destino));
        if (File.Exists(rutaDestino))
            throw new IOException($"Ya existe {rutaDestino}; no lo sobrescribo.");
        Directory.CreateDirectory(Path.GetDirectoryName(rutaDestino)!);
        var temporal = rutaDestino + ".vaciando.accdb";
        File.Copy(rutaOrigen, temporal, overwrite: true);
        File.SetAttributes(temporal, FileAttributes.Normal);
        await VaciarAsync(new BaseDatosAccess(temporal, ModoApertura.Exclusivo));
        // Compactar elimina del fichero las páginas de los datos borrados.
        if (!Compactar(temporal, rutaDestino))
            File.Move(temporal, rutaDestino);
        else
        {
            File.Delete(temporal);
            // La compactación anota aquí las relaciones hacia tablas vinculadas cuyo fichero no está.
            await new BaseDatosAccess(rutaDestino, ModoApertura.Exclusivo).EjecutarAsync(async s =>
            {
                try { await s.EjecutarAsync("DROP TABLE MSysCompactError"); } catch (ExcepcionDatos) { /* no la había */ }
                return true;
            });
        }
    }

    /// <summary>Compacta con el motor de Access (DAO por COM, incluido en ACE); false si no está disponible.</summary>
    internal static bool Compactar(string origen, string destino)
    {
        var tipo = Type.GetTypeFromProgID("DAO.DBEngine.120");
        if (tipo is null) return false;
        object? motor = null;
        try
        {
            motor = Activator.CreateInstance(tipo);
            tipo.InvokeMember("CompactDatabase", System.Reflection.BindingFlags.InvokeMethod, null, motor, [origen, destino]);
            return File.Exists(destino);
        }
        catch (Exception ex) when (ex is System.Reflection.TargetInvocationException or System.Runtime.InteropServices.COMException)
        {
            if (File.Exists(destino)) File.Delete(destino);
            return false;
        }
        finally
        {
            if (motor is not null) System.Runtime.InteropServices.Marshal.FinalReleaseComObject(motor);
        }
    }

    /// <summary>Vacía las tablas propias, repitiendo las que fallan por relaciones hasta que no queda ninguna.</summary>
    internal static async Task VaciarAsync(IBaseDatos bd)
    {
        await bd.EjecutarAsync(async s =>
        {
            var tablas = s.LeerEsquema().Tablas
                .Where(t => !t.EsVinculada && !TablasConservadas.Contains(t.Nombre))
                .ToList();
            // Las tablas que se referencian a sí mismas (padre, rectificada…) se desenlazan antes de borrar.
            foreach (var tabla in tablas)
                foreach (var relacion in tabla.Relaciones.Where(r => string.Equals(r.TablaReferenciada, tabla.Nombre, StringComparison.OrdinalIgnoreCase)))
                    foreach (var columna in relacion.Columnas)
                        await s.EjecutarAsync($"UPDATE [{tabla.Nombre}] SET [{columna}] = NULL");
            var pendientes = tablas.Select(t => t.Nombre).ToList();
            while (pendientes.Count > 0)
            {
                var fallidas = new List<string>();
                ExcepcionDatos? ultimo = null;
                foreach (var tabla in pendientes)
                {
                    try { await s.EjecutarAsync($"DELETE FROM [{tabla}]"); }
                    catch (IntegridadReferencialException ex) { fallidas.Add(tabla); ultimo = ex; }
                }
                if (fallidas.Count == pendientes.Count)
                    throw new InvalidOperationException($"No puedo vaciar {string.Join(", ", fallidas)}: {ultimo?.Error.Causa}");
                pendientes = fallidas;
            }
            return true;
        });
    }
}
