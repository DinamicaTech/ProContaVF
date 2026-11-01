using System.Data;
using System.Data.OleDb;
using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Persistencia.Access;

/// <summary>
/// BD Access (accdb) abierta con ACE/OLE DB x64. Es la única clase del proyecto que crea
/// conexiones; el resto del código trabaja con <see cref="IBaseDatos"/> e <see cref="ISesionDatos"/>.
/// </summary>
public sealed class BaseDatosAccess : IBaseDatos
{
    public const string Proveedor = "Microsoft.ACE.OLEDB.12.0";

    private readonly string _cadenaConexion;

    public BaseDatosAccess(string ruta, ModoApertura modo = ModoApertura.Compartido)
    {
        if (string.IsNullOrWhiteSpace(ruta)) throw new ArgumentException("Falta la ruta de la BD.", nameof(ruta));
        Ruta = Path.GetFullPath(ruta);
        Modo = modo;
        var modoAce = modo switch
        {
            ModoApertura.SoloLectura => "Read",
            ModoApertura.Exclusivo => "Share Exclusive",
            _ => "Share Deny None"
        };
        // Sin grupo de conexiones (OLE DB Services=-4): cerrar es cerrar de verdad, así el fichero
        // queda libre para la apertura exclusiva del ActualizadorEsquema y para las copias.
        _cadenaConexion = $"Provider={Proveedor};Data Source={Ruta};Mode={modoAce};Persist Security Info=False;OLE DB Services=-4";
    }

    public string Ruta { get; }

    public ModoApertura Modo { get; }

    /// <summary>
    /// Termina el proceso sin descargar el proveedor ACE. El ACE de Office (Hacer clic y ejecutar)
    /// provoca un fallo (mso20win32client.dll) cuando se descarga al cerrar un proceso .NET, aunque
    /// todas las conexiones estén cerradas; los ejecutables lo llaman como último paso al salir,
    /// después de cerrar conexiones y ficheros y vaciar la consola.
    /// </summary>
    public static void TerminarProceso(int codigoSalida)
    {
        Console.Out.Flush();
        Console.Error.Flush();
        TerminateProcess(GetCurrentProcess(), unchecked((uint)codigoSalida));
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr proceso, uint codigoSalida);

    public async Task<T> EjecutarAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        var conexion = Abrir();
        try
        {
            var sesion = new SesionAccess(conexion, null, cancelacion);
            return await trabajo(sesion).ConfigureAwait(false);
        }
        finally
        {
            Cerrar(conexion);
        }
    }

    public async Task<T> EnTransaccionAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        var conexion = Abrir();
        try
        {
            using var transaccion = conexion.BeginTransaction(IsolationLevel.ReadCommitted);
            var sesion = new SesionAccess(conexion, transaccion, cancelacion);
            T resultado;
            try
            {
                resultado = await trabajo(sesion).ConfigureAwait(false);
            }
            catch
            {
                transaccion.Rollback();
                throw;
            }

            if (resultado is Resultado { EsCorrecto: false })
                transaccion.Rollback();
            else
                transaccion.Commit();
            return resultado;
        }
        finally
        {
            Cerrar(conexion);
        }
    }

    // ACE falla (violación de acceso) si dos hilos del mismo proceso abren o cierran conexiones a la
    // vez; se serializan solo la apertura y el cierre, no el trabajo de cada sesión.
    private static readonly object _cerrojoProveedor = new();

    private static void Cerrar(OleDbConnection conexion)
    {
        lock (_cerrojoProveedor) conexion.Dispose();
    }

    private OleDbConnection Abrir()
    {
        if (!File.Exists(Ruta))
            throw new BaseDatosNoDisponibleException(new Error(
                "Datos.BDNoEncontrada",
                "No encuentro la base de datos.",
                Ruta,
                "El fichero no existe en esa ruta o la unidad no está disponible.",
                "Comprueba la ubicación de datos en la configuración y que el disco del servidor está accesible."));

        var conexion = new OleDbConnection(_cadenaConexion);
        try
        {
            lock (_cerrojoProveedor) conexion.Open();
            return conexion;
        }
        catch (OleDbException ex)
        {
            conexion.Dispose();
            throw TraductorErroresAccess.Traducir(ex, Ruta, null);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains(Proveedor, StringComparison.OrdinalIgnoreCase))
        {
            conexion.Dispose();
            throw new BaseDatosNoDisponibleException(new Error(
                "Datos.ProveedorAusente",
                "No puedo abrir la base de datos porque falta el motor de Access de 64 bits.",
                Ruta,
                $"El proveedor {Proveedor} no está instalado o no es de 64 bits.",
                "Instala «Microsoft Access Database Engine» de 64 bits en el servidor (se instala una sola vez).",
                ex.Message), ex);
        }
    }
}
