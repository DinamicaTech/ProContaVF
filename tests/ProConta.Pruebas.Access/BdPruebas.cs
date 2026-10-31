using ProConta.Datos.Esquema;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;

namespace ProConta.Pruebas.Access;

/// <summary>
/// BD de pruebas: copia temporal de la maestra con su estructura y sin datos de negocio (las
/// pruebas solo insertan datos sintéticos). La maestra se lee una vez para crear una plantilla en
/// %TEMP%\ProContaVF-pruebas; nunca se abre ni se modifica el original. La ruta de la maestra se
/// toma de la variable PROCONTA_MAESTRA o, si no está, de la ubicación habitual del equipo del propietario.
/// </summary>
public sealed class BdPruebas : IDisposable
{
    public const string RutaMaestraPredeterminada = @"C:\docs\codigo\ProContaVF\ProConta\Proconta.accdb";

    private static readonly Lazy<string> _plantilla = new(CrearPlantilla, LazyThreadSafetyMode.ExecutionAndPublication);

    private BdPruebas(string ruta) => Ruta = ruta;

    public string Ruta { get; }

    public IBaseDatos Bd(ModoApertura modo = ModoApertura.Compartido) => new BaseDatosAccess(Ruta, modo);

    public static string RutaMaestra =>
        Environment.GetEnvironmentVariable("PROCONTA_MAESTRA") is { Length: > 0 } ruta ? ruta : RutaMaestraPredeterminada;

    public static string Carpeta => Path.Combine(Path.GetTempPath(), "ProContaVF-pruebas");

    /// <summary>Copia nueva e independiente para una prueba; se borra al liberarla.</summary>
    public static BdPruebas Nueva()
    {
        var destino = Path.Combine(Carpeta, $"prueba-{Guid.NewGuid():N}.accdb");
        File.Copy(_plantilla.Value, destino);
        return new BdPruebas(destino);
    }

    /// <summary>Carpeta temporal propia de una prueba (copias de seguridad, diccionario…).</summary>
    public static string CarpetaTemporal()
    {
        var carpeta = Path.Combine(Carpeta, $"carpeta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        return carpeta;
    }

    public void Dispose()
    {
        try
        {
            File.Delete(Ruta);
            File.Delete(Path.ChangeExtension(Ruta, ".laccdb"));
        }
        catch (IOException)
        {
            // Una conexión aún sin cerrar del grupo de OLE DB; la carpeta temporal se limpia en la próxima plantilla.
        }
    }

    private static string CrearPlantilla()
    {
        if (!File.Exists(RutaMaestra))
            throw new InvalidOperationException(
                $"No encuentro la maestra en {RutaMaestra}. Las pruebas de la categoría Access la necesitan: " +
                "define PROCONTA_MAESTRA con su ruta o ejecuta «probar.cmd sinbd».");
        Directory.CreateDirectory(Carpeta);
        var huella = ActualizadorEsquema.Sha256(RutaMaestra)[..16];
        var plantilla = Path.Combine(Carpeta, $"plantilla-{huella}.accdb");

        // Varios proyectos de pruebas se ejecutan a la vez: solo uno crea la plantilla.
        using var mutex = new Mutex(false, $"Global\\ProContaVF-plantilla-{huella}");
        mutex.WaitOne();
        try
        {
            if (!File.Exists(plantilla))
                CopiaEstructura.CrearAsync(RutaMaestra, plantilla).GetAwaiter().GetResult();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
        return plantilla;
    }
}
