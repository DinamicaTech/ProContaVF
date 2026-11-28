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
    private Func<ContextoAcceso, VentanaPrincipal>? _crearPrincipal;

    /// <summary>Servicio de acceso de la sesión, para cambiar de sociedad o ejercicio sin volver a entrar.</summary>
    internal ServicioAcceso? Acceso => _acceso;

    /// <summary>Crea y muestra la ventana principal en un contexto; al cerrarla termina el programa salvo que se haya sustituido.</summary>
    private VentanaPrincipal AbrirPrincipal(ContextoAcceso contexto)
    {
        var principal = _crearPrincipal!(contexto);
        principal.Closed += (_, _) => { if (!principal.Sustituida) Shutdown(0); };
        MainWindow = principal;
        ErroresInesperados.Principal = principal;
        principal.Show();
        return principal;
    }

    /// <summary>
    /// Cambio de sociedad o ejercicio sin volver a entrar (plataforma/contenedor 2026-11-28): abre la ventana principal del
    /// contexto nuevo con la misma sesión y cierra la anterior, que ya ha cerrado sus pestañas.
    /// </summary>
    internal void SustituirPrincipal(VentanaPrincipal anterior, ContextoAcceso nuevo)
    {
        Rect? ventana = anterior.WindowState == WindowState.Maximized ? null : new Rect(anterior.Left, anterior.Top, anterior.Width, anterior.Height);
        anterior.Sustituida = true;
        var principal = AbrirPrincipal(nuevo);
        if (ventana is Rect r)
        {
            principal.WindowState = WindowState.Normal;
            principal.Left = r.Left; principal.Top = r.Top; principal.Width = r.Width; principal.Height = r.Height;
        }
        anterior.Close();
        principal.Activate();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ErroresInesperados.Instalar(this, e.Args);
        try
        {
            var resuelta = ResolverUbicacion(e.Args);
            if (resuelta is null) { Shutdown(0); return; }

            // Entorno (plataforma/entorno-pruebas): si el puesto tiene copia de pruebas de esta BD, se elige antes del acceso.
            ConfiguracionLocal? local = null;
            try { local = new AlmacenConfiguracionLocalJson().Leer(); } catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.IOException) { }
            var opciones = SelectorEntorno.Resolver(resuelta, e.Args, local);
            if (!opciones.EsCorrecto)
            {
                VentanaAviso.Mostrar("No sé en qué entorno entrar", opciones.Error!);
                Shutdown(1);
                return;
            }
            var ubicacion = opciones.Valor.Eleccion == EleccionEntorno.Preguntar
                ? VentanaEntorno.Elegir(opciones.Valor, await InfoEntorno.LeerAsync(new BaseDatosAccess(opciones.Valor.Pruebas!.RutaBaseDatos, ModoApertura.SoloLectura)))
                : opciones.Valor.Elegida;
            if (ubicacion is null) { Shutdown(0); return; }
            var quierePruebas = opciones.Valor.Pruebas is { } p && string.Equals(p.RutaBaseDatos, ubicacion.RutaBaseDatos, StringComparison.OrdinalIgnoreCase);

            var compatible = await ComprobarOActualizarAsync(ubicacion);
            if (compatible is null) { Shutdown(1); return; }
            // El esquema ya está al día y no cambia mientras el programa está abierto: su estructura se lee una sola vez
            // (registro-errores 2026-11-28; las consultas van por turno y no deben esperar lecturas de estructura repetidas).
            MemoriaEsquema.RecordarEnElProceso();

            var bd = new BaseDatosAccess(ubicacion.RutaBaseDatos);
            var entorno = await InfoEntorno.LeerAsync(bd);
            ErroresInesperados.BaseDatos = ubicacion.RutaBaseDatos;
            ErroresInesperados.Entorno = entorno.Nombre;
            if (quierePruebas && !entorno.EsPruebas)
            {
                VentanaAviso.Mostrar("Esta no es la copia de pruebas", new Error("Configuracion.PruebasSinMarca",
                    "La base de datos registrada como copia de pruebas no tiene la marca de pruebas, así que no entro en ella: podría ser una BD de verdad.",
                    ubicacion.RutaBaseDatos, "No la ha creado «Actualizar la copia de pruebas…», o se ha sustituido a mano.",
                    "Entra en producción y vuelve a crear la copia con Configuración › «Actualizar la copia de pruebas…»."));
                Shutdown(1);
                return;
            }
            Informes.ConversorHtmlPdf.MarcaDeAgua = entorno.EsPruebas ? "PRUEBAS" : null;
            // En pruebas nada se escribe, mueve ni sobrescribe fuera de la carpeta de pruebas (plataforma/entorno-pruebas 2026-11-24).
            ProConta.Nucleo.Ficheros.EntornoFicheros.Configurar(entorno.EsPruebas ? System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(ubicacion.RutaBaseDatos)) : null);
            var sociedades = new ServicioSociedadesAccess(bd);
            _acceso = new ServicioAcceso(bd, sociedades);

            if (!await _acceso.Operadores.HayOperadoresAsync() && !VentanaPrimerAdministrador.Crear(_acceso.Operadores))
            {
                Shutdown(0);
                return;
            }

            var almacen = new AlmacenConfiguracionLocalJson();
            var operadores = await _acceso.Operadores.MostrarListaOperadoresAsync() ? await _acceso.Operadores.ListarNombresActivosAsync() : null;
            var acceso = new VentanaAcceso(_acceso, operadores, PreferenciasPuesto.UltimoOperador(almacen));
            if (acceso.ShowDialog() != true || acceso.Resultado is null)
            {
                Shutdown(0);
                return;
            }
            _sesion = acceso.Resultado.Sesion;
            ErroresInesperados.Operador = _sesion.NombreOperador;
            PreferenciasPuesto.RecordarOperador(almacen, _sesion.NombreOperador);
            // Columnas de las tablas y paneles se guardan por operador (ProConta.UI.Base).
            UI.Base.Preferencias.PreferenciasOperador.Actual = new UI.Base.Preferencias.PreferenciasJson(_sesion.NombreOperador);

            _crearPrincipal = contexto => new VentanaPrincipal(contexto, ubicacion, compatible.VersionBd, bd, entorno);
            var principal = AbrirPrincipal(acceso.Resultado);
            ErroresInesperados.RevisarCierresAnteriores(principal);
            if (e.Args.Contains("--provocar-error", StringComparer.OrdinalIgnoreCase)) ErroresInesperados.ProvocarDePrueba(principal);
        }
        catch (ExcepcionDatos ex)
        {
            VentanaAviso.Mostrar("No he podido acceder a los datos", ex.Error);
            Shutdown(1);
        }
    }

    /// <summary>
    /// Comprueba el esquema y, si solo le faltan migraciones, ofrece «Actualizar ahora» con copia previa (plataforma/entorno-pruebas
    /// 2026-11-16). Devuelve el diagnóstico compatible, o null si no se puede entrar (ya se ha avisado al operador).
    /// </summary>
    private static async Task<Datos.Esquema.DiagnosticoEsquema?> ComprobarOActualizarAsync(UbicacionDatos ubicacion)
    {
        var comprobacion = await ComprobadorArranque.DiagnosticarAsync(ubicacion);
        if (comprobacion.EsCompatible) return comprobacion.Diagnostico;
        if (!comprobacion.SePuedeActualizarAhora)
        {
            VentanaAviso.Mostrar("La base de datos no se puede usar con esta versión", comprobacion.Error!);
            return null;
        }
        var d = comprobacion.Diagnostico!;
        var carpeta = ComprobadorArranque.CarpetaCopias(ubicacion);
        if (VentanaActualizar(d, ubicacion.RutaBaseDatos, carpeta).ShowDialog() != true)
            return null;
        var actualizada = await ComprobadorArranque.ActualizarAhoraAsync(ubicacion, Environment.UserName);
        if (!actualizada.EsCorrecto)
        {
            VentanaAviso.Mostrar("No he podido actualizar la base de datos", actualizada.Error!);
            return null;
        }
        comprobacion = await ComprobadorArranque.DiagnosticarAsync(ubicacion);
        if (comprobacion.EsCompatible) return comprobacion.Diagnostico;
        VentanaAviso.Mostrar("La base de datos no se puede usar con esta versión", comprobacion.Error!);
        return null;
    }

    /// <summary>
    /// Aviso «La base de datos necesita actualizarse» (datos/esquema 2026-11-16 19:09): qué pasa y qué hacer arriba, los cambios pendientes en un cuadro
    /// con barra de desplazamiento y los botones siempre a la vista. Sin abrirla, para poder capturarla.
    /// </summary>
    internal static Window VentanaActualizar(Datos.Esquema.DiagnosticoEsquema d, string rutaBd, string carpetaCopias)
    {
        var pregunta = new Error("Esquema.Pendiente",
            $"Esta versión del programa trae {(d.Pendientes.Count == 1 ? "un cambio" : $"{d.Pendientes.Count} cambios")} de estructura que la base de datos todavía no tiene " +
            $"(está en la versión {d.VersionBd} y el programa usa la {d.VersionPrograma}).",
            rutaBd,
            null,
            $"Pulsa «Actualizar ahora»: antes hago una copia de la base de datos en {carpetaCopias}. Si prefieres no actualizarla todavía, sal y abre la versión anterior del programa. " +
            "Si no es la base de datos que querías abrir, sal y ábrela con su ruta (--bd \"<fichero accdb>\").");
        return VentanaAviso.Crear("La base de datos necesita actualizarse", pregunta, "Actualizar ahora",
            "Hace una copia de la base de datos y le aplica los cambios de estructura de esta versión; después entra", "Cambios pendientes:", CambiosPendientes(d));
    }

    /// <summary>Un cambio de esquema pendiente por línea, con la versión a la que lleva delante: «v8 · Cuentas de cliente…».</summary>
    internal static IReadOnlyList<string> CambiosPendientes(Datos.Esquema.DiagnosticoEsquema d) =>
        d.Pendientes.Select((m, i) => $"v{d.VersionBd + i + 1} · {m.Descripcion}").ToList();

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
