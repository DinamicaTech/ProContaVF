using System.Runtime.InteropServices;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Descripción de una tabla (<see cref="Columna"/> null) o de un campo: la propiedad Description de Access, que recoge el diccionario.</summary>
public sealed record DescripcionEsquema(string Tabla, string? Columna, string Texto);

/// <summary>
/// Escribe las descripciones de tablas y campos de una migración. El SQL de Access no puede ponerlas, así que se escriben con DAO
/// (incluido en el motor de Access) en cuanto termina la migración, con la BD ya cerrada por OLE DB. No forman parte del hash de la
/// migración: si DAO no está disponible, la estructura queda igual y solo faltan las descripciones (el actualizador lo avisa).
/// </summary>
internal static class EscritorDescripciones
{
    private const int DbText = 10;

    /// <summary>Devuelve null si las ha escrito todas, o el motivo por el que no.</summary>
    public static string? Escribir(string rutaBd, IReadOnlyList<DescripcionEsquema> descripciones)
    {
        if (descripciones.Count == 0) return null;
        var tipo = Type.GetTypeFromProgID("DAO.DBEngine.120");
        if (tipo is null) return "No está DAO (DAO.DBEngine.120) del motor de Access en este equipo.";
        dynamic? motor = null;
        dynamic? bd = null;
        try
        {
            motor = Activator.CreateInstance(tipo)!;
            bd = motor.OpenDatabase(rutaBd, true, false);
            foreach (var d in descripciones)
            {
                dynamic tabla = bd.TableDefs[d.Tabla];
                dynamic objeto = d.Columna is null ? tabla : tabla.Fields[d.Columna];
                Poner(objeto, d.Texto.Length <= 255 ? d.Texto : d.Texto[..255]);
            }
            return null;
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or System.Reflection.TargetInvocationException)
        {
            return ex.Message;
        }
        finally
        {
            try { bd?.Close(); } catch (COMException) { /* ya cerrada */ }
            if (bd is not null) Marshal.FinalReleaseComObject(bd);
            if (motor is not null) Marshal.FinalReleaseComObject(motor);
        }
    }

    /// <summary>
    /// Escribe las descripciones del catálogo que faltan o son distintas en la BD. Se salta las tablas y campos que esta BD no
    /// tiene (el catálogo describe también tablas que solo existen en algunas instalaciones). Devuelve cuántas ha escrito y,
    /// si no ha podido terminar, el motivo.
    /// </summary>
    public static (int Escritas, string? Motivo) Completar(string rutaBd, IEnumerable<DescripcionEsquema> descripciones)
    {
        var tipo = Type.GetTypeFromProgID("DAO.DBEngine.120");
        if (tipo is null) return (0, "No está DAO (DAO.DBEngine.120) del motor de Access en este equipo.");
        dynamic? motor = null;
        dynamic? bd = null;
        var escritas = 0;
        try
        {
            motor = Activator.CreateInstance(tipo)!;
            bd = motor.OpenDatabase(rutaBd, true, false);
            var tablas = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
            foreach (dynamic t in bd.TableDefs) tablas[(string)t.Name] = t;
            foreach (var grupo in descripciones.GroupBy(d => d.Tabla, StringComparer.OrdinalIgnoreCase))
            {
                if (!tablas.TryGetValue(grupo.Key, out var tabla)) continue;
                var campos = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
                foreach (dynamic f in tabla.Fields) campos[(string)f.Name] = f;
                foreach (var d in grupo)
                {
                    dynamic? objeto = d.Columna is null ? tabla : campos.GetValueOrDefault(d.Columna);
                    if (objeto is null) continue;
                    var texto = d.Texto.Length <= 255 ? d.Texto : d.Texto[..255];
                    if (string.Equals(Leer(objeto), texto, StringComparison.Ordinal)) continue;
                    Poner(objeto, texto);
                    escritas++;
                }
            }
            return (escritas, null);
        }
        catch (Exception ex) when (ex is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or System.Reflection.TargetInvocationException)
        {
            return (escritas, ex.Message);
        }
        finally
        {
            try { bd?.Close(); } catch (COMException) { /* ya cerrada */ }
            if (bd is not null) Marshal.FinalReleaseComObject(bd);
            if (motor is not null) Marshal.FinalReleaseComObject(motor);
        }
    }

    /// <summary>Descripción actual de una tabla o campo; null si no tiene.</summary>
    public static string? Leer(dynamic objeto)
    {
        try
        {
            return (string?)objeto.Properties["Description"].Value;
        }
        catch (COMException)
        {
            return null;
        }
    }

    private static void Poner(dynamic objeto, string texto)
    {
        try
        {
            objeto.Properties["Description"].Value = texto;
        }
        catch (COMException)
        {
            // La propiedad no existe todavía: se crea.
            dynamic propiedad = objeto.CreateProperty("Description", DbText, texto);
            objeto.Properties.Append(propiedad);
        }
    }
}
