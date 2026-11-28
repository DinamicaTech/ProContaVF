using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using ProConta.Plataforma.Configuracion;
using ProConta.UI.Base.Controles;

namespace ProConta.App;

/// <summary>
/// Errores inesperados de ProContaVF (plataforma/registro-errores): los que ningún código ha controlado. Se guardan en el
/// registro de errores con la versión, el entorno, la BD, el operador, la sociedad y la pantalla, y el operador ve un aviso
/// que dice qué ha pasado y dónde está el fichero. Los de la interfaz no cierran el programa; los de otros hilos, que sí lo
/// cierran, quedan guardados antes.
/// </summary>
internal static class ErroresInesperados
{
    public static RegistroErrores Registro { get; } = new(Contexto);

    public static string? BaseDatos { get; set; }
    public static string? Entorno { get; set; }
    public static string? Operador { get; set; }
    public static VentanaPrincipal? Principal { get; set; }

    private static bool _mostrando;

    public static void Instalar(Application app, string[] argumentos)
    {
        Registro.Purgar();
        app.DispatcherUnhandledException += (_, e) =>
        {
            e.Handled = true;
            AlFallarEnPantalla(app, e.Exception);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is not Exception ex) return;
            var ruta = Registro.Registrar(ex, e.IsTerminating ? "Hilo en segundo plano (ProContaVF se cierra)" : "Hilo en segundo plano");
            if (!e.IsTerminating) return;
            // Desde aquí no se puede usar la interfaz de WPF: un cuadro de Windows basta para que el operador sepa qué ha pasado.
            MessageBox.Show(TextoCierre(ex, ruta), "ProContaVF · Tengo que cerrarme", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            // Tarea que falló sin que nadie esperase su resultado: no interrumpe al operador, pero queda en el registro.
            Registro.Registrar(e.Exception, "Tarea en segundo plano cuyo resultado nadie esperaba");
            e.SetObserved();
        };
    }

    private static ContextoError Contexto()
    {
        var p = Principal;
        return new ContextoError("ProContaVF", App.Version, Entorno, BaseDatos, Operador,
            p?.SociedadParaRegistro,
            p?.PantallaParaRegistro ?? (p is null ? "Arranque (todavía no se ha abierto la ventana principal)" : null));
    }

    private static void AlFallarEnPantalla(Application app, Exception ex)
    {
        var ruta = Registro.Registrar(ex, "Pantalla (hilo de la interfaz)");
        var sinVentana = Principal is null || !Principal.IsLoaded;
        // Un error que se repite mientras el aviso está abierto (p. ej. al repintar) solo se registra: no se apilan avisos.
        if (_mostrando) return;
        _mostrando = true;
        try
        {
            Mostrar(ex, ruta, sinVentana);
        }
        catch (Exception otro)
        {
            Registro.Registrar(otro, "Aviso de error inesperado (no se pudo mostrar)");
        }
        finally
        {
            _mostrando = false;
        }
        if (sinVentana) app.Shutdown(1);
    }

    private static void Mostrar(Exception ex, string? ruta, bool sinVentana)
    {
        var pantalla = Principal?.PantallaParaRegistro;
        var donde = pantalla is null ? "" : $" en «{QuitarRecuento(pantalla)}»";
        var mensaje = ruta is not null
            ? $"Ha fallado algo que ProContaVF no esperaba{donde} y no he podido terminar lo que estabas haciendo. " +
              "He guardado el detalle en el registro de errores para poder averiguar la causa." + Environment.NewLine + Environment.NewLine +
              (sinVentana
                  ? "Vuelve a abrir ProContaVF. Si se repite, envía ese fichero a quien te da soporte."
                  : "Comprueba que lo último que hiciste está como esperabas y vuelve a intentarlo. Si se repite, envía ese fichero a quien te da soporte.")
            : $"Ha fallado algo que ProContaVF no esperaba{donde} y no he podido guardar el detalle en el registro de errores ({Registro.RutaActual}): " +
              "puede que el disco esté lleno o que la carpeta no deje escribir." + Environment.NewLine + Environment.NewLine +
              "Pulsa «Copiar» para conservar el detalle y envíalo a quien te da soporte.";
        var detalles = new List<string>
        {
            ruta is not null ? "Fichero: " + ruta : "Fichero: no se ha podido escribir",
            "Error: " + RegistroErrores.Resumen(ex),
            "Fecha: " + RegistroErrores.FechaCorta(DateTime.Now) + " · versión " + App.Version,
        };
        if (ruta is null) detalles.Add("Detalle técnico: " + ex);

        var opciones = new List<OpcionMensaje>();
        if (ruta is not null)
            opciones.Add(new OpcionMensaje("Abrir la carpeta", "Abre la carpeta del registro de errores con el fichero seleccionado, para enviarlo o mirarlo"));
        opciones.Add(sinVentana
            ? new OpcionMensaje("Cerrar ProContaVF", "Cierra el programa; vuelve a abrirlo después", Principal: true, Cancelar: true)
            : new OpcionMensaje("Seguir trabajando", "Cierra este aviso; ProContaVF sigue abierto", Principal: true, Cancelar: true));

        var elegida = ServicioMensajes.Preguntar(Principal is { IsLoaded: true } p ? p : null, "Algo ha fallado", mensaje, detalles, [.. opciones]);
        if (ruta is not null && elegida == 0) AbrirCarpeta(ruta);
    }

    /// <summary>
    /// Al arrancar (registro-errores 2026-11-28): busca en el visor de eventos de Windows los cierres bruscos de este ejecutable
    /// desde la última vez, los copia al registro de errores y avisa. Se lee en segundo plano para no retrasar la entrada.
    /// </summary>
    public static void RevisarCierresAnteriores(Window principal)
    {
        if (Environment.ProcessPath is not { } exe) return;
        Task.Run(() => CierresWindows.Buscar(Registro.Carpeta, exe, DateTime.Now))
            .ContinueWith(t =>
            {
                if (t.IsFaulted || t.Result.Count == 0) return;
                string? ruta = null;
                foreach (var c in t.Result)
                    ruta = Registro.RegistrarExterno(c.Fecha, new ContextoError("ProContaVF", "la instalada entonces (no se conoce)", Entorno, BaseDatos, Operador),
                        "Cierre brusco anotado por Windows en su visor de eventos (ProContaVF no pudo guardarlo)", c.Resumen, c.Detalle) ?? ruta;
                AvisarCierres(principal, t.Result, ruta);
            }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static void AvisarCierres(Window principal, IReadOnlyList<CierreBrusco> cierres, string? ruta)
    {
        var ultimo = cierres[^1];
        var cuando = cierres.Count == 1 ? $"el {RegistroErrores.FechaCorta(ultimo.Fecha)}" : $"{cierres.Count} veces, la última el {RegistroErrores.FechaCorta(ultimo.Fecha)}";
        var mensaje = $"ProContaVF se cerró de golpe {cuando}, sin poder avisar ni guardar el detalle: falló un componente de Windows o de Office " +
            "que usa el programa (por ejemplo, el motor de Access). Windows sí lo anotó, y " +
            (ruta is not null ? "he copiado su detalle al registro de errores para poder averiguar la causa." : $"no he podido copiarlo al registro de errores ({Registro.RutaActual}).") +
            Environment.NewLine + Environment.NewLine +
            "Comprueba que lo último que hiciste antes del cierre está como esperabas. Si se repite, envía ese fichero a quien te da soporte.";
        var detalles = new List<string>
        {
            ruta is not null ? "Fichero: " + ruta : "Fichero: no se ha podido escribir",
            "Error: " + ultimo.Resumen,
        };
        var opciones = new List<OpcionMensaje>();
        if (ruta is not null) opciones.Add(new OpcionMensaje("Abrir la carpeta", "Abre la carpeta del registro de errores con el fichero seleccionado, para enviarlo o mirarlo"));
        opciones.Add(new OpcionMensaje("Seguir trabajando", "Cierra este aviso", Principal: true, Cancelar: true));
        try
        {
            var elegida = ServicioMensajes.Preguntar(principal.IsLoaded ? principal : null, "ProContaVF se cerró de golpe", mensaje, detalles, [.. opciones]);
            if (ruta is not null && elegida == 0) AbrirCarpeta(ruta);
        }
        catch (Exception ex)
        {
            Registro.Registrar(ex, "Aviso de cierre brusco anterior (no se pudo mostrar)");
        }
    }

    /// <summary>Abre el Explorador con el fichero seleccionado. Si no puede, no hace nada: la ruta ya está en el aviso.</summary>
    internal static void AbrirCarpeta(string ruta)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{ruta}\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
    }

    internal static string TextoCierre(Exception ex, string? ruta) =>
        "ProContaVF ha tenido un error que no esperaba y tiene que cerrarse." + Environment.NewLine + Environment.NewLine +
        (ruta is not null
            ? $"He guardado el detalle en:{Environment.NewLine}{ruta}{Environment.NewLine}{Environment.NewLine}Vuelve a abrir ProContaVF y comprueba que lo último que hiciste está guardado. Si se repite, envía ese fichero a quien te da soporte."
            : $"No he podido guardar el detalle en el registro de errores ({Registro.RutaActual}). Pulsa Ctrl+C para copiar este aviso y envíalo a quien te da soporte.{Environment.NewLine}{Environment.NewLine}{ex}");

    /// <summary>«Diario (3 pestañas abiertas)» → «Diario».</summary>
    private static string QuitarRecuento(string pantalla)
    {
        var i = pantalla.LastIndexOf(" (", StringComparison.Ordinal);
        return i > 0 ? pantalla[..i] : pantalla;
    }

    /// <summary>
    /// <c>--provocar-error</c>: lanza un error de prueba en la pantalla activa en cuanto se abre la ventana principal, para
    /// comprobar el registro y el aviso sin tocar ningún dato.
    /// </summary>
    public static void ProvocarDePrueba(Window principal) =>
        principal.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            throw new InvalidOperationException("Error de prueba provocado con --provocar-error para comprobar el registro de errores. No ha cambiado ningún dato.")));
}
