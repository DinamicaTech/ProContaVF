using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProConta.Conector;
using ProConta.Datos.Esquema;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;

// Conector de Claude Desktop para ProContaVF (docs/plataforma/conector-claude.md): servidor MCP por entrada y salida estándar.
//   ProContaConector --bd <ruta del accdb> [--operador <nombre>]
// Claude Desktop lo arranca solo (claude_desktop_config.json). La salida estándar es del protocolo: los mensajes van a la de errores.

Console.OutputEncoding = Encoding.UTF8;
string? Valor(string nombre)
{
    var i = Array.FindIndex(args, a => a.Equals(nombre, StringComparison.OrdinalIgnoreCase));
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

var ruta = Valor("--bd");
if (string.IsNullOrWhiteSpace(ruta) || !File.Exists(ruta))
{
    Console.Error.WriteLine(string.IsNullOrWhiteSpace(ruta)
        ? "Falta la BD. Uso: ProContaConector --bd <ruta del accdb> [--operador <nombre>]"
        : $"No encuentro la BD «{ruta}». Revisa la ruta en claude_desktop_config.json.");
    return 2;
}

IBaseDatos bd = new BaseDatosAccess(ruta, ModoApertura.Compartido);
var diagnostico = await DiagnosticadorEsquema.DiagnosticarAsync(bd);
if (diagnostico.Estado != EstadoEsquema.Actualizada)
{
    Console.Error.WriteLine($"La BD tiene la versión de esquema {diagnostico.VersionBd} y el conector espera la {diagnostico.VersionPrograma}. " +
        "Abre ProContaVF con esta BD para que se actualice, o recompila el conector con la misma versión del programa.");
    BaseDatosAccess.TerminarProceso(3);
}

var entorno = await ProConta.Plataforma.Configuracion.InfoEntorno.LeerAsync(bd);
var servicio = new ServicioConector(bd, Valor("--operador") ?? Environment.UserName) { Entorno = entorno.Nombre };

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = false, Args = [] });
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddSingleton(servicio);
builder.Services
    .AddMcpServer(o =>
    {
        o.ServerInfo = new() { Name = entorno.EsPruebas ? "ProContaVF (pruebas)" : "ProContaVF", Version = "1.0" };
        o.ServerInstructions = InstruccionesConector.Texto + (entorno.EsPruebas ? InstruccionesConector.Pruebas : InstruccionesConector.Produccion);
    })
    .WithStdioServerTransport()
    .WithTools<HerramientasConector>();

await builder.Build().RunAsync();
// ACE falla al descargarse al salir de un proceso .NET: se termina sin descargarlo (BaseDatosAccess.TerminarProceso).
BaseDatosAccess.TerminarProceso(0);
return 0;
