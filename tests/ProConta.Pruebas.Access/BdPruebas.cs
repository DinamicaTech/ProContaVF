using System.Security.Cryptography;
using System.Text;
using ProConta.Datos.Esquema;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Auditoria;

namespace ProConta.Pruebas.Access;

/// <summary>
/// BD de pruebas: copia temporal de la maestra con su estructura y sin datos de negocio (las
/// pruebas solo insertan datos sintéticos). La maestra se lee una vez para crear una plantilla de la
/// versión 1 en %TEMP%\ProContaVF-pruebas, y de ella otra actualizada a la versión del programa;
/// nunca se abre ni se modifica el original. La ruta de la maestra se toma de PROCONTA_MAESTRA o,
/// si no está, de la ubicación habitual del equipo del propietario.
/// </summary>
public sealed class BdPruebas : IDisposable
{
    public const string RutaMaestraPredeterminada = @"C:\docs\codigo\ProContaVF\ProConta\Proconta.accdb";

    /// <summary>Formas de pago sintéticas del origen que usa la plantilla actualizada.</summary>
    public static readonly IReadOnlyList<(byte Codigo, string Descripcion)> FormasPagoSinteticas =
    [
        (0, "Indefinida"), (3, "Pendiente"), (10, "Metálico"), (110, "Recibo domiciliado"), (133, "Transferencia")
    ];

    private static readonly Lazy<string> _plantillaVersion1 = new(CrearPlantillaVersion1, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<string> _plantillaActual = new(CrearPlantillaActual, LazyThreadSafetyMode.ExecutionAndPublication);

    private BdPruebas(string ruta) => Ruta = ruta;

    public string Ruta { get; }

    public IBaseDatos Bd(ModoApertura modo = ModoApertura.Compartido) => new BaseDatosAccess(Ruta, modo);

    public static string RutaMaestra =>
        Environment.GetEnvironmentVariable("PROCONTA_MAESTRA") is { Length: > 0 } ruta ? ruta : RutaMaestraPredeterminada;

    public static string Carpeta => Path.Combine(Path.GetTempPath(), "ProContaVF-pruebas");

    /// <summary>Copia nueva con la versión de esquema del programa; se borra al liberarla.</summary>
    public static BdPruebas Nueva() => Copiar(_plantillaActual.Value);

    /// <summary>Copia nueva de la versión 1 (la maestra sin migraciones nuevas), para probar migraciones.</summary>
    public static BdPruebas NuevaVersion1() => Copiar(_plantillaVersion1.Value);

    /// <summary>Carpeta temporal propia de una prueba (copias de seguridad, diccionario…).</summary>
    public static string CarpetaTemporal()
    {
        var carpeta = Path.Combine(Carpeta, $"carpeta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(carpeta);
        return carpeta;
    }

    /// <summary>accdb sintético con una tabla FormasPago (Codigo, Descripcion) como la del ERP.</summary>
    public static string CrearOrigenFormasPago(string carpeta)
    {
        var ruta = Path.Combine(carpeta, $"origen-formas-pago-{Guid.NewGuid():N}.accdb");
        CrearBdVacia(ruta);
        new BaseDatosAccess(ruta).EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("CREATE TABLE FormasPago (Codigo BYTE NOT NULL CONSTRAINT PK_FormasPago PRIMARY KEY, Descripcion TEXT(50), Activa YESNO)");
            foreach (var (codigo, descripcion) in FormasPagoSinteticas)
                await s.EjecutarAsync("INSERT INTO FormasPago (Codigo, Descripcion, Activa) VALUES (?, ?, ?)", codigo, descripcion, true);
            return true;
        }).GetAwaiter().GetResult();
        return ruta;
    }

    /// <summary>Crea un accdb vacío con el motor de Access (DAO por COM, incluido en ACE).</summary>
    public static void CrearBdVacia(string ruta)
    {
        var tipo = Type.GetTypeFromProgID("DAO.DBEngine.120")
            ?? throw new InvalidOperationException("No está DAO.DBEngine.120 (motor de Access de 64 bits).");
        var motor = Activator.CreateInstance(tipo)!;
        try
        {
            // dbLangGeneral y dbVersion120 (formato accdb).
            var bd = tipo.InvokeMember("CreateDatabase", System.Reflection.BindingFlags.InvokeMethod, null, motor,
                [ruta, ";LANGID=0x0409;CP=1252;COUNTRY=0", 128])!;
            bd.GetType().InvokeMember("Close", System.Reflection.BindingFlags.InvokeMethod, null, bd, null);
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(bd);
        }
        finally
        {
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(motor);
        }
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
            // Si queda algún fichero bloqueado, se limpiará con la carpeta temporal.
        }
    }

    private static BdPruebas Copiar(string plantilla)
    {
        var destino = Path.Combine(Carpeta, $"prueba-{Guid.NewGuid():N}.accdb");
        File.Copy(plantilla, destino);
        return new BdPruebas(destino);
    }

    private static string CrearPlantillaVersion1()
    {
        if (!File.Exists(RutaMaestra))
            throw new InvalidOperationException(
                $"No encuentro la maestra en {RutaMaestra}. Las pruebas de la categoría Access la necesitan: " +
                "define PROCONTA_MAESTRA con su ruta o ejecuta «probar.cmd sinbd».");
        Directory.CreateDirectory(Carpeta);
        var huella = ActualizadorEsquema.Sha256(RutaMaestra)[..16];
        var plantilla = Path.Combine(Carpeta, $"plantilla-v1-{huella}.accdb");
        ConExclusion($"v1-{huella}", () =>
        {
            if (!File.Exists(plantilla))
                CopiaEstructura.CrearAsync(RutaMaestra, plantilla).GetAwaiter().GetResult();
        });
        return plantilla;
    }

    private static string CrearPlantillaActual()
    {
        var version1 = _plantillaVersion1.Value;
        // La plantilla actual se rehace cuando cambia el catálogo de migraciones.
        var catalogo = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join("|", CatalogoMigraciones.Todas.Select(m => m.Hash)))))[..12];
        var plantilla = Path.Combine(Carpeta, $"{Path.GetFileNameWithoutExtension(version1)}-v{CatalogoMigraciones.VersionPrograma}-{catalogo}.accdb");
        ConExclusion(Path.GetFileNameWithoutExtension(plantilla), () =>
        {
            if (File.Exists(plantilla)) return;
            var carpeta = CarpetaTemporal();
            var temporal = Path.Combine(carpeta, "plantilla.accdb");
            File.Copy(version1, temporal);
            var origen = CrearOrigenFormasPago(carpeta);
            var resultado = new ActualizadorEsquema(Actor.Proceso("pruebas"),
                    parametros: new Dictionary<string, string> { [MigracionesOrganizacion.ParametroOrigenFormasPago] = origen })
                .ActualizarAsync(temporal, Path.Combine(carpeta, "copias")).GetAwaiter().GetResult();
            if (!resultado.EsCorrecto)
                throw new InvalidOperationException($"No he podido actualizar la plantilla de pruebas: {resultado.Error}");
            File.Move(temporal, plantilla);
        });
        return plantilla;
    }

    // Varios proyectos de pruebas se ejecutan a la vez: solo uno crea cada plantilla.
    private static void ConExclusion(string nombre, Action accion)
    {
        using var mutex = new Mutex(false, $"Global\\ProContaVF-{nombre}");
        mutex.WaitOne();
        try { accion(); }
        finally { mutex.ReleaseMutex(); }
    }
}
