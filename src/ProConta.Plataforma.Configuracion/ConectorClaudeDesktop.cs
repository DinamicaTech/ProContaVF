using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProConta.Plataforma.Configuracion;

/// <summary>Cómo está el conector de ProContaVF en la configuración de Claude Desktop (plataforma/conector-claude).</summary>
public enum EstadoConectorClaude
{
    /// <summary>Configurado con este ejecutable del conector y la BD abierta.</summary>
    Correcto,
    /// <summary>Claude Desktop no tiene la entrada «procontavf».</summary>
    SinConector,
    /// <summary>La entrada apunta a otra BD: el conector trabajaría sobre datos distintos de los de ProContaVF.</summary>
    OtraBd,
    /// <summary>La entrada apunta a otro ejecutable del conector (otra instalación o una versión antigua).</summary>
    OtroEjecutable,
    /// <summary>El fichero de configuración existe pero no es JSON válido: no se toca.</summary>
    ConfiguracionInvalida,
}

/// <summary>Resultado de la comprobación: el estado, el fichero, y la BD y el ejecutable que tiene configurados ahora (si los tiene).</summary>
public sealed record ComprobacionConectorClaude(EstadoConectorClaude Estado, string RutaConfiguracion, string? BdConfigurada, string? EjecutableConfigurado, string? Detalle = null);

/// <summary>
/// La entrada del conector en <c>claude_desktop_config.json</c> (menú «IA», plataforma/conector-claude): comprueba si existe y si apunta a la
/// misma BD que ProContaVF, la crea o la corrige sin tocar el resto del fichero (con copia previa), y abre Claude Desktop.
/// Claude Desktop instalado desde la Tienda guarda la configuración en %LOCALAPPDATA%\Claude-Data\claude_*; el instalador clásico, en %APPDATA%\Claude.
/// </summary>
public sealed class ConectorClaudeDesktop(string? rutaConfiguracion = null, string servidor = ConectorClaudeDesktop.NombreServidor)
{
    public const string NombreServidor = "procontavf";

    /// <summary>Entrada del conector para la copia de pruebas (plataforma/entorno-pruebas): convive con la de producción.</summary>
    public const string NombreServidorPruebas = "procontavf-pruebas";

    /// <summary>La entrada de Claude Desktop que comprueba y configura esta instancia.</summary>
    public string Servidor { get; } = servidor;

    /// <summary>La entrada que corresponde a un entorno.</summary>
    public static string ServidorDe(bool pruebas) => pruebas ? NombreServidorPruebas : NombreServidor;
    public const string NombreFichero = "claude_desktop_config.json";

    public string RutaConfiguracion { get; } = rutaConfiguracion ?? LocalizarConfiguracion();

    /// <summary>Ruta del fichero de configuración de Claude Desktop en este equipo: el que exista o, si no hay ninguno, donde lo crearía Claude Desktop.</summary>
    public static string LocalizarConfiguracion()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var tienda = Directory.Exists(Path.Combine(local, "Claude-Data"))
            ? Directory.GetDirectories(Path.Combine(local, "Claude-Data"), "claude_*").OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToList()
            : [];
        var candidatos = tienda.Select(d => Path.Combine(d, NombreFichero)).Append(Path.Combine(roaming, "Claude", NombreFichero)).ToList();
        return candidatos.FirstOrDefault(File.Exists) ?? candidatos[0];
    }

    public ComprobacionConectorClaude Comprobar(string ejecutableConector, string rutaBd)
    {
        JsonObject raiz;
        try
        {
            raiz = Leer();
        }
        catch (JsonException ex)
        {
            return new(EstadoConectorClaude.ConfiguracionInvalida, RutaConfiguracion, null, null, ex.Message);
        }
        if (raiz["mcpServers"]?[Servidor] is not JsonObject entrada)
            return new(EstadoConectorClaude.SinConector, RutaConfiguracion, null, null);
        var comando = entrada["command"]?.GetValue<string>();
        var args = entrada["args"] is JsonArray a ? a.Select(x => x?.GetValue<string>() ?? "").ToList() : [];
        var i = args.FindIndex(x => x.Equals("--bd", StringComparison.OrdinalIgnoreCase));
        var bd = i >= 0 && i + 1 < args.Count ? args[i + 1] : null;
        if (!MismaRuta(bd, rutaBd)) return new(EstadoConectorClaude.OtraBd, RutaConfiguracion, bd, comando);
        if (!MismaRuta(comando, ejecutableConector)) return new(EstadoConectorClaude.OtroEjecutable, RutaConfiguracion, bd, comando);
        return new(EstadoConectorClaude.Correcto, RutaConfiguracion, bd, comando);
    }

    /// <summary>
    /// Crea o sustituye la entrada «procontavf» con este ejecutable, la BD y el operador, conservando el resto del fichero. Si el fichero ya existía,
    /// deja antes una copia al lado (<c>.copia-AAAAMMDD-HHMMSS</c>) y devuelve su ruta. Un fichero que no es JSON válido no se toca (lanza JsonException).
    /// </summary>
    public string? Configurar(string ejecutableConector, string rutaBd, string operador)
    {
        var raiz = Leer();
        string? copia = null;
        if (File.Exists(RutaConfiguracion))
        {
            copia = $"{RutaConfiguracion}.copia-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(RutaConfiguracion, copia, overwrite: true);
        }
        if (raiz["mcpServers"] is not JsonObject servidores)
        {
            servidores = [];
            raiz["mcpServers"] = servidores;
        }
        servidores[Servidor] = new JsonObject
        {
            ["command"] = ejecutableConector,
            ["args"] = new JsonArray("--bd", rutaBd, "--operador", operador),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(RutaConfiguracion)!);
        var temporal = RutaConfiguracion + ".tmp";
        File.WriteAllText(temporal, raiz.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        File.Move(temporal, RutaConfiguracion, overwrite: true);
        return copia;
    }

    /// <summary>Si Claude Desktop está abierto ahora (tendrá que reiniciarse para cargar un cambio de configuración).</summary>
    public static bool ClaudeAbierto() => Process.GetProcessesByName("claude").Length > 0;

    /// <summary>Abre Claude Desktop o lo trae al frente con su protocolo (claude://). False si no está instalado.</summary>
    public static bool AbrirClaude()
    {
        try
        {
            Process.Start(new ProcessStartInfo("claude://") { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private JsonObject Leer()
    {
        if (!File.Exists(RutaConfiguracion)) return [];
        var texto = File.ReadAllText(RutaConfiguracion);
        if (string.IsNullOrWhiteSpace(texto)) return [];
        return JsonNode.Parse(texto, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new JsonException("El fichero no contiene un objeto JSON.");
    }

    private static bool MismaRuta(string? a, string? b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
        try
        {
            return string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
