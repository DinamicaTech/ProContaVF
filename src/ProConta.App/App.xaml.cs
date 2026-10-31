using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Plataforma.Configuracion;
using ProConta.Seguridad.Acceso;

namespace ProConta.App;

/// <summary>
/// Arranque: ubicación de datos, compatibilidad del esquema, primer administrador si no hay
/// operadores, acceso en cuatro fases y ventana principal. No crea ni migra ninguna BD.
/// </summary>
public partial class App : Application
{
    /// <summary>Versión de entrega (InformationalVersion calculada desde git al compilar).</summary>
    public static string Version { get; } =
        typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "ProContaVF-local";

    private ServicioAcceso? _acceso;
    private SesionAcceso? _sesion;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var ubicacion = ResolverUbicacion(e.Args);
            if (ubicacion is null) { Shutdown(0); return; }

            var compatible = await ComprobadorArranque.ComprobarAsync(ubicacion);
            if (!compatible.EsCorrecto)
            {
                VentanaAviso.Mostrar("La base de datos no se puede usar con esta versión", compatible.Error!);
                Shutdown(1);
                return;
            }

            var bd = new BaseDatosAccess(ubicacion.RutaBaseDatos);
            var sociedades = new ServicioSociedadesAccess(bd);
            _acceso = new ServicioAcceso(bd, sociedades);

            if (!await _acceso.Operadores.HayOperadoresAsync() && !VentanaPrimerAdministrador.Crear(_acceso.Operadores))
            {
                Shutdown(0);
                return;
            }

            var acceso = new VentanaAcceso(_acceso, sociedades);
            if (acceso.ShowDialog() != true || acceso.Resultado is null)
            {
                Shutdown(0);
                return;
            }
            _sesion = acceso.Resultado.Sesion;

            var principal = new VentanaPrincipal(acceso.Resultado, ubicacion, compatible.Valor.VersionBd);
            principal.Closed += (_, _) => Shutdown(0);
            MainWindow = principal;
            principal.Show();
        }
        catch (ExcepcionDatos ex)
        {
            VentanaAviso.Mostrar("No he podido acceder a los datos", ex.Error);
            Shutdown(1);
        }
    }

    /// <summary>Ubicación resuelta o, si no la hay, la que elija el operador; null si cancela.</summary>
    private static UbicacionDatos? ResolverUbicacion(string[] argumentos)
    {
        var resolutor = new ResolutorUbicacionDatos(new AlmacenConfiguracionLocalJson());
        var resultado = resolutor.Resolver(argumentos);
        while (!resultado.EsCorrecto)
        {
            if (!VentanaAviso.Preguntar("Elige la base de datos de ProContaVF", resultado.Error!, "Elegir base de datos…"))
                return null;
            var dialogo = new OpenFileDialog
            {
                Title = "Elige la base de datos de ProContaVF",
                Filter = "Base de datos de Access (*.accdb)|*.accdb",
                CheckFileExists = true
            };
            if (dialogo.ShowDialog() != true) continue; // cancelar no cambia nada
            var seleccion = resolutor.ValidarSeleccion(dialogo.FileName);
            if (seleccion.EsCorrecto)
            {
                var guardada = resolutor.GuardarSeleccion(seleccion.Valor);
                if (!guardada.EsCorrecto) VentanaAviso.Mostrar("No he podido guardar la ubicación", guardada.Error!);
                return seleccion.Valor;
            }
            resultado = seleccion;
        }
        return resultado.Valor;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_acceso is not null && _sesion is not null)
        {
            try { _acceso.CerrarSesionAsync(_sesion).GetAwaiter().GetResult(); }
            catch (ExcepcionDatos) { /* la sesión vive en memoria: al salir deja de valer igualmente */ }
        }
        base.OnExit(e);
        // El ACE de Office falla al descargarse cuando termina el proceso (datos/persistencia).
        BaseDatosAccess.TerminarProceso(e.ApplicationExitCode);
    }

    /// <summary>Texto del error para el operador, en líneas separadas.</summary>
    internal static string Explicar(Error error) =>
        string.Join(Environment.NewLine, new[]
        {
            error.Mensaje,
            error.RegistroAfectado is null ? null : "Afecta a: " + error.RegistroAfectado,
            error.Causa is null ? null : "Causa: " + error.Causa,
            error.AccionCorrectora is null ? null : "Qué hacer: " + error.AccionCorrectora,
        }.Where(l => l is not null));
}
