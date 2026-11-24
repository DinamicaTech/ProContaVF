using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Plataforma.Configuracion;

/// <summary>
/// En qué entorno está una BD (plataforma/entorno-pruebas): la marca va dentro del fichero (Configuracion.EntornoPruebas),
/// así que viaja con él aunque se copie o se mueva. Solo la pone el refresco de la copia de pruebas.
/// </summary>
/// <param name="CopiaFecha">Cuándo se copió de producción (solo en pruebas).</param>
/// <param name="CopiaOrigen">BD de producción de la que sale (solo en pruebas).</param>
public sealed record InfoEntorno(bool EsPruebas, DateTime? CopiaFecha = null, string? CopiaOrigen = null)
{
    public static readonly InfoEntorno Produccion = new(false);

    /// <summary>Texto corto para la cabecera, la barra de estado y el conector.</summary>
    public string Nombre => EsPruebas ? "Pruebas" : "Producción";

    /// <summary>Lee la marca de la BD. Una BD sin la columna (esquema anterior) o sin fila de Configuracion es de producción.</summary>
    public static async Task<InfoEntorno> LeerAsync(IBaseDatos bd)
    {
        try
        {
            return await bd.EjecutarAsync(async s =>
            {
                if (!s.LeerEsquema().ExisteColumna("Configuracion", "EntornoPruebas")) return Produccion;
                return await s.ConsultarPrimeroAsync("SELECT TOP 1 EntornoPruebas, CopiaProduccionFecha, CopiaProduccionOrigen FROM Configuracion",
                    f => new InfoEntorno(f.Logico("EntornoPruebas"), f.FechaNula("CopiaProduccionFecha"), f.Texto("CopiaProduccionOrigen"))) ?? Produccion;
            });
        }
        catch (ExcepcionDatos)
        {
            return Produccion;
        }
    }
}

/// <summary>La copia de pruebas registrada en este puesto (en configuracion.json): dónde está y de qué BD de producción sale.</summary>
/// <param name="DesviarCorreoA">Dirección a la que el refresco desvía todo el correo de la copia; se propone la última usada.</param>
public sealed record EntornoPruebasLocal(string RutaBaseDatos, string RutaProduccion, string? DesviarCorreoA = null);

/// <summary>Qué hacer con el entorno al arrancar.</summary>
public enum EleccionEntorno
{
    /// <summary>Entrar en la BD resuelta sin preguntar (no hay copia de pruebas registrada para ella, o se pidió producción).</summary>
    Produccion,
    /// <summary>Entrar en la copia de pruebas sin preguntar (se pidió con --entorno pruebas, o la BD resuelta ya es la de pruebas).</summary>
    Pruebas,
    /// <summary>Preguntar al operador antes del acceso; la opción por defecto es producción.</summary>
    Preguntar,
}

/// <summary>Resultado de <see cref="SelectorEntorno.Resolver"/>: la BD de producción, la de pruebas si la hay y qué hacer.</summary>
public sealed record OpcionesEntorno(UbicacionDatos Produccion, UbicacionDatos? Pruebas, EleccionEntorno Eleccion)
{
    /// <summary>La ubicación con la que se entra cuando no hay que preguntar.</summary>
    public UbicacionDatos Elegida => Eleccion == EleccionEntorno.Pruebas && Pruebas is not null ? Pruebas : Produccion;

    /// <summary>
    /// Los entornos que se ofrecen en la ventana de elegir entorno, en orden: producción primero (la opción por defecto) y después
    /// la copia de pruebas si la hay. La ventana pinta tantas tarjetas como entradas, así que admite más entornos sin cambiarla.
    /// </summary>
    /// <param name="pruebas">La marca leída de la copia de pruebas, para enseñar de cuándo es; null si no se pudo leer.</param>
    public IReadOnlyList<EntradaEntorno> Entradas(InfoEntorno? pruebas)
    {
        var lista = new List<EntradaEntorno>
        {
            new("Producción", "Contabilidad real", "La base de datos de verdad: facturas, asientos y modelos que se presentan.",
                Produccion, TonoEntorno.Produccion),
        };
        if (Pruebas is not null)
            lista.Add(new("Pruebas", pruebas?.CopiaFecha is DateTime f ? $"Copia del {f:dd/MM HH:mm}" : "Copia de producción",
                "Copia de producción para probar sin miedo. El correo va desviado y VeriFactu está apagado.", Pruebas, TonoEntorno.Pruebas));
        return lista;
    }
}

/// <summary>Color con el que se distingue un entorno en la ventana de elegir entorno (y, en pruebas, en la franja de la ventana principal).</summary>
public enum TonoEntorno
{
    Produccion,
    Pruebas,
}

/// <summary>Una tarjeta de la ventana de elegir entorno.</summary>
/// <param name="Etiqueta">Texto corto junto al nombre: «Contabilidad real» o de cuándo es la copia.</param>
public sealed record EntradaEntorno(string Nombre, string Etiqueta, string Descripcion, UbicacionDatos Ubicacion, TonoEntorno Tono);

/// <summary>
/// Paso previo al acceso (plataforma/entorno-pruebas): si este puesto tiene una copia de pruebas de la BD resuelta, se elige
/// entre producción y pruebas. <c>--entorno produccion|pruebas</c> lo decide sin preguntar (accesos directos, pruebas de Claude).
/// </summary>
public static class SelectorEntorno
{
    public const string ArgumentoEntorno = "--entorno";

    public static Resultado<OpcionesEntorno> Resolver(UbicacionDatos resuelta, IReadOnlyList<string> argumentos, ConfiguracionLocal? local)
    {
        string? pedido = null;
        for (var i = 0; i < argumentos.Count; i++)
            if (string.Equals(argumentos[i], ArgumentoEntorno, StringComparison.OrdinalIgnoreCase))
                pedido = i + 1 < argumentos.Count ? argumentos[i + 1].Trim().ToLowerInvariant() : "";
        if (pedido is not null && pedido is not ("produccion" or "producción" or "pruebas"))
            return Fallo("Configuracion.EntornoDesconocido", $"El argumento --entorno «{pedido}» no es válido.", null,
                "Solo se admiten «produccion» y «pruebas».", "Corrige el acceso directo: --entorno produccion o --entorno pruebas.");

        var registrada = local?.Pruebas;
        // La BD resuelta ya es la copia de pruebas (p. ej. --bd con la ruta de pruebas): se entra en ella.
        if (registrada is not null && MismaRuta(resuelta.RutaBaseDatos, registrada.RutaBaseDatos))
        {
            if (pedido is "produccion" or "producción")
                return Fallo("Configuracion.EntornoContradictorio", "Has pedido producción, pero la base de datos indicada es la copia de pruebas.", resuelta.RutaBaseDatos,
                    "El argumento --bd señala la BD de pruebas y --entorno dice produccion.", "Quita uno de los dos argumentos del acceso directo.");
            return Resultado<OpcionesEntorno>.Correcto(new(resuelta, resuelta, EleccionEntorno.Pruebas));
        }

        UbicacionDatos? pruebas = null;
        if (registrada is not null && MismaRuta(resuelta.RutaBaseDatos, registrada.RutaProduccion) && File.Exists(registrada.RutaBaseDatos))
            pruebas = new UbicacionDatos(Path.GetFullPath(registrada.RutaBaseDatos), resuelta.Origen);

        if (pedido == "pruebas")
            return pruebas is null
                ? Fallo("Configuracion.SinEntornoPruebas", "Has pedido el entorno de pruebas, pero este puesto no tiene una copia de pruebas de esta base de datos.",
                    registrada?.RutaBaseDatos ?? resuelta.RutaBaseDatos,
                    registrada is null ? "Todavía no se ha creado la copia de pruebas." : "La copia de pruebas registrada no existe o es de otra BD de producción.",
                    "Entra en producción y usa Configuración › «Actualizar la copia de pruebas…» para crearla.")
                : Resultado<OpcionesEntorno>.Correcto(new(resuelta, pruebas, EleccionEntorno.Pruebas));
        if (pedido is "produccion" or "producción" || pruebas is null)
            return Resultado<OpcionesEntorno>.Correcto(new(resuelta, pruebas, EleccionEntorno.Produccion));
        return Resultado<OpcionesEntorno>.Correcto(new(resuelta, pruebas, EleccionEntorno.Preguntar));
    }

    /// <summary>Registra (o actualiza) la copia de pruebas en la configuración local, conservando el resto.</summary>
    public static Resultado Registrar(IAlmacenConfiguracionLocal almacen, EntornoPruebasLocal pruebas)
    {
        try
        {
            ConfiguracionLocal? anterior = null;
            try { anterior = almacen.Leer(); } catch (InvalidDataException) { /* dañada: se sustituye */ }
            almacen.Guardar((anterior ?? new ConfiguracionLocal()) with { Pruebas = pruebas });
            return Resultado.Correcto();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Fallo(new Error("Configuracion.PruebasNoGuardada", "La copia de pruebas está hecha, pero no he podido apuntarla en la configuración de este puesto.",
                almacen.Ruta, ex.Message, "Comprueba los permisos de tu perfil de Windows y vuelve a actualizar la copia de pruebas; hasta entonces no saldrá la elección de entorno."));
        }
    }

    internal static bool MismaRuta(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try { return string.Equals(Path.GetFullPath(a.Trim().Trim('"')), Path.GetFullPath(b.Trim().Trim('"')), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return false; }
    }

    private static Resultado<OpcionesEntorno> Fallo(string codigo, string mensaje, string? registro, string causa, string accion) =>
        Resultado<OpcionesEntorno>.Fallo(new Error(codigo, mensaje, registro, causa, accion));
}
