using System.Data.OleDb;
using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Persistencia.Access;

/// <summary>Traduce los errores de ACE a excepciones explícitas con su acción correctora.</summary>
internal static class TraductorErroresAccess
{
    private static readonly HashSet<int> _tablaAusente = [3078, 3011, 3265];
    private static readonly HashSet<int> _unicidad = [3022];
    private static readonly HashSet<int> _integridad = [3200, 3201];
    private static readonly HashSet<int> _bloqueo = [3006, 3008, 3704, 3356, 3009, 3045, 3050, 3051, 3186, 3187, 3188, 3197, 3218, 3260, 3734];
    private static readonly HashSet<int> _noDisponible = [3024, 3043, 3044, 3049];

    public static ExcepcionDatos Traducir(OleDbException ex, string ruta, string? sql)
    {
        var codigo = CodigoAccess(ex);
        var detalle = $"ACE {codigo?.ToString() ?? "?"}: {ex.Message}" + (sql is null ? "" : $" | SQL: {sql}");
        var mensaje = ex.Message.ToLowerInvariant();

        if (Es(codigo, _tablaAusente) || mensaje.Contains("no puede encontrar la tabla") || mensaje.Contains("cannot find the input table"))
            return new TablaAusenteException(new Error("Datos.TablaAusente",
                "Falta una tabla o consulta que necesito en la base de datos.", ruta,
                "La estructura de la BD no es la que espera esta versión del programa: " + ex.Message,
                "Ejecuta el ActualizadorEsquema sobre esta BD o comprueba que la ubicación de datos apunta a la BD correcta.", detalle), ex);

        if (Es(codigo, _unicidad) || mensaje.Contains("duplicados") || mensaje.Contains("duplicate values"))
            return new UnicidadException(new Error("Datos.Duplicado",
                "No puedo guardar el registro porque ya existe otro con los mismos datos únicos.", null,
                "Un índice único de la BD impide el duplicado.",
                "Revisa el código o identificador que has escrito; si es un número automático, vuelve a intentarlo.", detalle), ex);

        if (Es(codigo, _integridad) || mensaje.Contains("registro relacionado") || mensaje.Contains("related record"))
            return new IntegridadReferencialException(new Error("Datos.Integridad",
                "No puedo hacer el cambio porque rompería la relación con otros registros.", null,
                codigo == 3200 ? "Hay registros que dependen de este." : "Falta el registro relacionado.",
                "Revisa los registros relacionados antes de repetir la operación.", detalle), ex);

        if (Es(codigo, _bloqueo) || mensaje.Contains("bloquead") || mensaje.Contains("locked") || mensaje.Contains("en uso"))
            return new BloqueoException(new Error("Datos.Bloqueo",
                "La base de datos o el registro está ocupado por otra sesión.", ruta,
                "Otro usuario lo está usando en este momento.",
                "Espera unos segundos y vuelve a intentarlo; si persiste, comprueba qué sesión lo tiene abierto.", detalle), ex);

        if (Es(codigo, _noDisponible))
            return new BaseDatosNoDisponibleException(new Error("Datos.BDNoDisponible",
                "No puedo abrir la base de datos.", ruta,
                ex.Message,
                "Comprueba la ubicación de datos en la configuración y que tienes permiso sobre la carpeta.", detalle), ex);

        return new ExcepcionDatos(new Error("Datos.Fallo",
            "La base de datos ha devuelto un error.", ruta, ex.Message,
            "Vuelve a intentarlo; si se repite, copia el detalle técnico y avisa al responsable.", detalle), ex);
    }

    private static bool Es(int? codigo, HashSet<int> codigos) => codigo is int c && codigos.Contains(c);

    /// <summary>ACE devuelve el número de error de Access en SQLState; si no, se usa NativeError.</summary>
    internal static int? CodigoAccess(OleDbException ex)
    {
        foreach (OleDbError error in ex.Errors)
        {
            if (int.TryParse(error.SQLState, out var codigo) && codigo > 0) return codigo;
            if (error.NativeError > 0 && error.NativeError < 100000) return error.NativeError;
        }
        return null;
    }
}
