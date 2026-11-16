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
