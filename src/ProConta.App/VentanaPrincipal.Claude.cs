using System.IO;
using ProConta.Plataforma.Configuracion;
using ProConta.UI.Base.Controles;

namespace ProConta.App;

/// <summary>
/// Menú «IA › Abrir Claude» (plataforma/conector-claude): antes de abrir Claude Desktop comprueba que su conector de ProContaVF apunta a esta
/// misma BD y a este ejecutable del conector, y si no, lo configura con permiso del operador.
/// </summary>
public partial class VentanaPrincipal
{
    private string _rutaBd = "";
    private string _operador = "";

    /// <summary>El conector se instala con el programa, en la subcarpeta Conector.</summary>
    private static string EjecutableConector => Path.Combine(AppContext.BaseDirectory, "Conector", "ProContaConector.exe");

    private void AbrirClaude()
    {
        if (!File.Exists(EjecutableConector))
        {
            Informar("Falta el conector de Claude",
                "No encuentro el conector de Claude junto a ProContaVF, así que Claude Desktop no podría consultar esta base de datos.",
                [$"Debería estar en {EjecutableConector}.", "Se instala con el programa: vuelve a compilar o a copiar ProContaVF completo, con su carpeta Conector."]);
            return;
        }
        var conector = new ConectorClaudeDesktop();
        var estado = conector.Comprobar(EjecutableConector, _rutaBd);
        if (estado.Estado == EstadoConectorClaude.Correcto)
        {
            Abrir("Claude Desktop abierto: su conector trabaja con esta misma base de datos.");
            return;
        }
        if (estado.Estado == EstadoConectorClaude.ConfiguracionInvalida)
        {
            var opcion = ServicioMensajes.Preguntar(this, "La configuración de Claude Desktop no es válida",
                "El fichero de configuración de Claude Desktop tiene un error y no lo cambio para no perder lo que tiene. Corrígelo (suele ser un bloque pegado fuera de las llaves) y vuelve a pulsar «Abrir Claude».",
                [estado.RutaConfiguracion, $"Error: {estado.Detalle}"],
                new OpcionMensaje("Cancelar", "No abre Claude Desktop", Cancelar: true),
                new OpcionMensaje("Abrir el fichero", "Abre el fichero de configuración en el Bloc de notas para corregirlo", Principal: true));
            if (opcion == 1) AbrirEnBlocDeNotas(estado.RutaConfiguracion);
            return;
        }

        var (titulo, mensaje) = estado.Estado switch
        {
            EstadoConectorClaude.OtraBd => ("Claude trabajaría con otra base de datos",
                "El conector de Claude Desktop está configurado con una base de datos distinta de la que tienes abierta. Si lo abres así, Claude consultaría y grabaría en la otra."),
            EstadoConectorClaude.OtroEjecutable => ("El conector de Claude es de otra instalación",
                "Claude Desktop usa el conector de otra carpeta o de una versión anterior de ProContaVF. Conviene que use el de esta instalación."),
            _ => ("Claude Desktop no tiene el conector de ProContaVF",
                "Para que Claude pueda consultar esta base de datos y proponer asientos, hay que añadir el conector de ProContaVF a la configuración de Claude Desktop."),
        };
        var detalles = new List<string> { $"Base de datos de ProContaVF: {_rutaBd}" };
        if (estado.Estado == EstadoConectorClaude.OtraBd) detalles.Add($"Base de datos del conector: {estado.BdConfigurada ?? "(ninguna)"}");
        if (estado.Estado == EstadoConectorClaude.OtroEjecutable) detalles.Add($"Conector configurado: {estado.EjecutableConfigurado ?? "(ninguno)"}");
        detalles.Add($"Configuración: {estado.RutaConfiguracion} (se guarda una copia antes de cambiarla)");
        var elegida = ServicioMensajes.Preguntar(this, titulo, mensaje, detalles,
            new OpcionMensaje("Cancelar", "No cambia nada ni abre Claude Desktop", Cancelar: true),
            new OpcionMensaje("Configurar y abrir Claude", "Pone en la configuración de Claude Desktop el conector de esta instalación con esta base de datos, y lo abre", Principal: true));
        if (elegida != 1) return;
        try
        {
            conector.Configurar(EjecutableConector, _rutaBd, _operador);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Informar("No he podido configurar Claude Desktop", $"No he podido guardar la configuración de Claude Desktop: {ex.Message}",
                [estado.RutaConfiguracion, "Comprueba que el fichero no está abierto en otro programa y vuelve a intentarlo."]);
            return;
        }
        if (ConectorClaudeDesktop.ClaudeAbierto())
        {
            Informar("Reinicia Claude Desktop",
                "He configurado el conector, pero Claude Desktop ya estaba abierto y no lo cargará hasta que lo reinicies: ciérralo del todo (también desde su icono junto al reloj) y vuelve a abrirlo.",
                ["Al cerrarlo se interrumpe lo que Claude esté haciendo en ese momento, incluidos los hilos de trabajo que usan este equipo."]);
            Abrir("Conector configurado: reinicia Claude Desktop para que lo cargue.");
        }
        else
        {
            Abrir("Conector configurado y Claude Desktop abierto con esta base de datos.");
        }
    }

    private void Abrir(string aviso)
    {
        if (ConectorClaudeDesktop.AbrirClaude()) Avisar(aviso);
        else Informar("No encuentro Claude Desktop", "No he podido abrir Claude Desktop: parece que no está instalado en este equipo.",
            ["Instálalo desde claude.ai/download y vuelve a pulsar «Abrir Claude»."]);
    }

    private void Informar(string titulo, string mensaje, IReadOnlyList<string> detalles) =>
        ServicioMensajes.Preguntar(this, titulo, mensaje, detalles, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));

    private void AbrirEnBlocDeNotas(string ruta)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("notepad.exe", $"\"{ruta}\"") { UseShellExecute = true })?.Dispose(); }
        catch (System.ComponentModel.Win32Exception ex) { Avisar($"No he podido abrir {ruta}: {ex.Message}"); }
    }
}
