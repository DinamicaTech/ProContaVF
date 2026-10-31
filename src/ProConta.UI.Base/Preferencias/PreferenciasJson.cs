using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProConta.UI.Base.Preferencias;

/// <summary>Preferencias en memoria: para pruebas y antes de iniciar sesión.</summary>
public sealed class PreferenciasEnMemoria : IPreferenciasOperador
{
    private readonly Dictionary<string, string> _valores = new(StringComparer.Ordinal);

    public T? Leer<T>(string clave) =>
        _valores.TryGetValue(clave, out var json) ? JsonSerializer.Deserialize<T>(json, PreferenciasJson.Opciones) : default;

    public void Guardar<T>(string clave, T valor) => _valores[clave] = JsonSerializer.Serialize(valor, PreferenciasJson.Opciones);

    public void Borrar(string clave) => _valores.Remove(clave);
}

/// <summary>
/// Preferencias de un operador en un fichero JSON local (%LocalAppData%\ProConta\preferencias\&lt;operador&gt;.json).
/// Un fichero dañado no impide trabajar: se ignora y se vuelve a las preferencias de fábrica.
/// </summary>
public sealed class PreferenciasJson : IPreferenciasOperador
{
    internal static readonly JsonSerializerOptions Opciones = new() { WriteIndented = true };

    private readonly string _ruta;
    private readonly object _cerrojo = new();
    private JsonObject _datos;

    public PreferenciasJson(string operador, string? carpeta = null)
    {
        if (string.IsNullOrWhiteSpace(operador)) throw new ArgumentException("Falta el operador.", nameof(operador));
        carpeta ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProConta", "preferencias");
        Directory.CreateDirectory(carpeta);
        var nombre = string.Concat(operador.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        _ruta = Path.Combine(carpeta, nombre + ".json");
        _datos = Cargar(_ruta);
    }

    /// <summary>Ruta del fichero de este operador.</summary>
    public string Ruta => _ruta;

    private static JsonObject Cargar(string ruta)
    {
        try
        {
            if (File.Exists(ruta) && JsonNode.Parse(File.ReadAllText(ruta)) is JsonObject o) return o;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Preferencias ilegibles: se trabaja con las de fábrica y el fichero se rehace al guardar.
        }
        return new JsonObject();
    }

    public T? Leer<T>(string clave)
    {
        lock (_cerrojo)
        {
            if (_datos[clave] is not { } nodo) return default;
            try { return nodo.Deserialize<T>(Opciones); }
            catch (JsonException) { return default; }
        }
    }

    public void Guardar<T>(string clave, T valor)
    {
        lock (_cerrojo)
        {
            _datos[clave] = JsonSerializer.SerializeToNode(valor, Opciones);
            Escribir();
        }
    }

    public void Borrar(string clave)
    {
        lock (_cerrojo)
        {
            if (_datos.Remove(clave)) Escribir();
        }
    }

    private void Escribir()
    {
        var tmp = _ruta + ".tmp";
        File.WriteAllText(tmp, _datos.ToJsonString(Opciones));
        File.Move(tmp, _ruta, overwrite: true);
    }
}
