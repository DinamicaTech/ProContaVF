using ProConta.Nucleo.Resultados;

namespace ProConta.Plataforma.Configuracion;

/// <summary>De dónde salió la ubicación de datos.</summary>
public enum OrigenUbicacion
{
    Argumento,
    Entorno,
    ConfiguracionLocal,
    Seleccion
}

/// <summary>Ubicación de datos validada: la BD existe y es un accdb.</summary>
public sealed record UbicacionDatos(string RutaBaseDatos, OrigenUbicacion Origen)
{
    public string DirectorioDatos => Path.GetDirectoryName(RutaBaseDatos)!;
}

/// <summary>
/// Resolutor único de la ubicación de datos (RF-CFG-01/02). Precedencia: argumento
/// <c>--bd &lt;ruta&gt;</c>, variable de entorno <c>PROCONTA_BD</c> y configuración local del usuario.
/// Una fuente prioritaria inválida es un error: no se pasa en silencio a la siguiente. Nunca crea
/// ni busca otra BD, nunca abre ventanas y no guarda nada salvo en <see cref="GuardarSeleccion"/>.
/// </summary>
public sealed class ResolutorUbicacionDatos(IAlmacenConfiguracionLocal almacen, Func<string, string?>? entorno = null)
{
    public const string ArgumentoBd = "--bd";
    public const string VariableEntorno = "PROCONTA_BD";

    private readonly Func<string, string?> _entorno = entorno ?? Environment.GetEnvironmentVariable;

    public Resultado<UbicacionDatos> Resolver(IReadOnlyList<string> argumentos)
    {
        var deArgumentos = new List<string>();
        for (var i = 0; i < argumentos.Count; i++)
        {
            if (!string.Equals(argumentos[i], ArgumentoBd, StringComparison.OrdinalIgnoreCase)) continue;
            if (i + 1 >= argumentos.Count || string.IsNullOrWhiteSpace(argumentos[i + 1]))
                return Fallo("Configuracion.ArgumentoIncompleto", "Falta la ruta detrás del argumento --bd.", null,
                    "El programa se ha lanzado con --bd pero sin ruta.", "Corrige el acceso directo: --bd \"<ruta completa del accdb>\".");
            deArgumentos.Add(argumentos[++i]);
        }
        if (deArgumentos.Select(Normalizar).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            return Fallo("Configuracion.ArgumentosIncompatibles", "Se ha indicado más de una base de datos distinta con --bd.",
                string.Join(" | ", deArgumentos), "Los argumentos señalan BD diferentes.", "Deja un solo --bd en el acceso directo.");
        if (deArgumentos.Count > 0)
            return Validar(deArgumentos[0], OrigenUbicacion.Argumento);

        var deEntorno = _entorno(VariableEntorno);
        if (!string.IsNullOrWhiteSpace(deEntorno))
            return Validar(deEntorno, OrigenUbicacion.Entorno);

        ConfiguracionLocal? local;
        try
        {
            local = almacen.Leer();
        }
        catch (InvalidDataException ex)
        {
            return Fallo("Configuracion.Danada", "No puedo leer la configuración local de este puesto.", almacen.Ruta,
                $"El fichero está dañado: {ex.Message}", "Vuelve a elegir la base de datos; se guardará una configuración nueva.");
        }
        catch (IOException ex)
        {
            return Fallo("Configuracion.Ilegible", "No puedo leer la configuración local de este puesto.", almacen.Ruta,
                ex.Message, "Comprueba los permisos de tu perfil de Windows y vuelve a abrir el programa.");
        }

        if (!string.IsNullOrWhiteSpace(local?.RutaBaseDatos))
            return Validar(local.RutaBaseDatos, OrigenUbicacion.ConfiguracionLocal);

        return Fallo("Configuracion.NoConfigurada", "Este puesto todavía no sabe qué base de datos usar.", almacen.Ruta,
            "No hay ruta en los argumentos, en la variable PROCONTA_BD ni en la configuración local.",
            "Elige el fichero accdb de ProContaVF del servidor.");
    }

    /// <summary>Valida una ruta elegida por el operador sin guardarla.</summary>
    public Resultado<UbicacionDatos> ValidarSeleccion(string ruta) => Validar(ruta, OrigenUbicacion.Seleccion);

    /// <summary>Guarda la selección confirmada. Si el operador cancela, no se llama y nada cambia.</summary>
    public Resultado GuardarSeleccion(UbicacionDatos ubicacion)
    {
        var validada = Validar(ubicacion.RutaBaseDatos, OrigenUbicacion.Seleccion);
        if (!validada.EsCorrecto) return validada;
        try
        {
            almacen.Guardar(new ConfiguracionLocal { RutaBaseDatos = validada.Valor.RutaBaseDatos });
            return Resultado.Correcto();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Resultado.Fallo(new Error("Configuracion.NoGuardada", "No he podido guardar la ubicación elegida.", almacen.Ruta,
                ex.Message, "Comprueba los permisos de tu perfil de Windows; la configuración anterior sigue intacta."));
        }
    }

    private static Resultado<UbicacionDatos> Validar(string ruta, OrigenUbicacion origen)
    {
        var fuente = origen switch
        {
            OrigenUbicacion.Argumento => "el argumento --bd",
            OrigenUbicacion.Entorno => $"la variable de entorno {VariableEntorno}",
            OrigenUbicacion.ConfiguracionLocal => "la configuración local de este puesto",
            _ => "la selección"
        };
        if (!Path.IsPathFullyQualified(ruta.Trim()))
            return Fallo("Configuracion.RutaRelativa", $"La ruta de la BD de {fuente} no es una ruta completa.", ruta,
                "Una ruta relativa depende de la carpeta desde la que se abre el programa.", "Indica la ruta completa, con unidad y carpetas.");
        var completa = Normalizar(ruta);
        if (!string.Equals(Path.GetExtension(completa), ".accdb", StringComparison.OrdinalIgnoreCase))
            return Fallo("Configuracion.NoEsAccdb", $"El fichero de {fuente} no es una base de datos Access (.accdb).", completa,
                "La extensión no es .accdb.", "Elige el fichero .accdb de ProContaVF.");
        var carpeta = Path.GetDirectoryName(completa);
        if (carpeta is null || !Directory.Exists(carpeta))
            return Fallo("Configuracion.CarpetaInexistente", $"No existe la carpeta de la BD indicada en {fuente}.", carpeta ?? completa,
                "La carpeta no existe o la unidad no está disponible.", "Comprueba la unidad del servidor o elige otra ubicación.");
        if (!File.Exists(completa))
            return Fallo("Configuracion.FicheroAusente", $"No existe la base de datos indicada en {fuente}.", completa,
                "La carpeta existe pero el fichero no está.", "Elige el fichero accdb correcto; no creo una BD nueva en su lugar.");
        return Resultado<UbicacionDatos>.Correcto(new UbicacionDatos(completa, origen));
    }

    private static string Normalizar(string ruta) => Path.GetFullPath(ruta.Trim().Trim('"'));

    private static Resultado<UbicacionDatos> Fallo(string codigo, string mensaje, string? registro, string causa, string accion) =>
        Resultado<UbicacionDatos>.Fallo(new Error(codigo, mensaje, registro, causa, accion));
}
