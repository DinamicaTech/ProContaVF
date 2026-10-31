using System.Text.Json;

namespace ProConta.Plataforma.Configuracion;

/// <summary>Configuración local del puesto, fuera de Access (RF-CFG-03). Nunca guarda secretos.</summary>
public sealed record ConfiguracionLocal
{
    /// <summary>Ruta completa del accdb elegido en este puesto.</summary>
    public string? RutaBaseDatos { get; init; }
}

public interface IAlmacenConfiguracionLocal
{
    /// <summary>Ruta del fichero, para mostrarla en los errores.</summary>
    string Ruta { get; }

    /// <summary>Lee la configuración; null si no existe. Lanza <see cref="InvalidDataException"/> si está dañada.</summary>
    ConfiguracionLocal? Leer();

    /// <summary>Guarda de forma atómica: o queda la configuración nueva completa o la anterior.</summary>
    void Guardar(ConfiguracionLocal configuracion);
}

/// <summary>
/// Configuración local en JSON dentro del perfil del usuario de Windows
/// (%LocalAppData%\ProContaVF\configuracion.json).
/// </summary>
public sealed class AlmacenConfiguracionLocalJson : IAlmacenConfiguracionLocal
{
    private static readonly JsonSerializerOptions _opciones = new() { WriteIndented = true };

    public AlmacenConfiguracionLocalJson(string? carpeta = null)
    {
        carpeta ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProContaVF");
        Ruta = Path.Combine(carpeta, "configuracion.json");
    }

    public string Ruta { get; }

    public ConfiguracionLocal? Leer()
    {
        if (!File.Exists(Ruta)) return null;
        try
        {
            return JsonSerializer.Deserialize<ConfiguracionLocal>(File.ReadAllText(Ruta), _opciones)
                ?? throw new InvalidDataException("El fichero está vacío.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    public void Guardar(ConfiguracionLocal configuracion)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta)!);
        var temporal = Ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(configuracion, _opciones));
        File.Move(temporal, Ruta, overwrite: true);
    }
}
