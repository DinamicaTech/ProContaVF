using System.Text.Json.Nodes;
using ProConta.Plataforma.Configuracion;

namespace ProConta.Plataforma.Configuracion.Tests;

/// <summary>Entrada del conector en la configuración de Claude Desktop (plataforma/conector-claude, menú «IA»).</summary>
public sealed class ConectorClaudeDesktopTests : IDisposable
{
    private const string Exe = @"C:\Docs\ProConta\Conector\ProContaConector.exe";
    private const string Bd = @"C:\Docs\ProConta\datos\ProConta.accdb";
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF-pruebas", $"claude-{Guid.NewGuid():N}");

    public ConectorClaudeDesktopTests() => Directory.CreateDirectory(_carpeta);

    public void Dispose()
    {
        try { Directory.Delete(_carpeta, recursive: true); } catch (IOException) { }
    }

    private string Fichero => Path.Combine(_carpeta, ConectorClaudeDesktop.NombreFichero);

    [Fact]
    public void SinFicheroFaltaElConectorYConfigurarloLoCrea()
    {
        var c = new ConectorClaudeDesktop(Fichero);
        Assert.Equal(EstadoConectorClaude.SinConector, c.Comprobar(Exe, Bd).Estado);
        Assert.Null(c.Configurar(Exe, Bd, "Ronald")); // no había fichero: no hay copia
        Assert.Equal(EstadoConectorClaude.Correcto, c.Comprobar(Exe, Bd).Estado);
        var entrada = JsonNode.Parse(File.ReadAllText(Fichero))!["mcpServers"]!["procontavf"]!;
        Assert.Equal(Exe, entrada["command"]!.GetValue<string>());
        Assert.Equal(["--bd", Bd, "--operador", "Ronald"], entrada["args"]!.AsArray().Select(x => x!.GetValue<string>()));
    }

    [Fact]
    public void ConfigurarConservaElRestoYDejaCopia()
    {
        File.WriteAllText(Fichero, """{ "preferences": { "tema": "oscuro" }, "mcpServers": { "otro": { "command": "otro.exe" } } }""");
        var c = new ConectorClaudeDesktop(Fichero);
        var copia = c.Configurar(Exe, Bd, "Ronald");
        Assert.True(File.Exists(copia));
        var raiz = JsonNode.Parse(File.ReadAllText(Fichero))!;
        Assert.Equal("oscuro", raiz["preferences"]!["tema"]!.GetValue<string>());
        Assert.NotNull(raiz["mcpServers"]!["otro"]);
        Assert.NotNull(raiz["mcpServers"]!["procontavf"]);
    }

    [Fact]
    public void DetectaOtraBdYOtroEjecutable()
    {
        var c = new ConectorClaudeDesktop(Fichero);
        c.Configurar(Exe, @"C:\Docs\ProContaVF-validacion\copia.accdb", "Ronald");
        var otra = c.Comprobar(Exe, Bd);
        Assert.Equal((EstadoConectorClaude.OtraBd, @"C:\Docs\ProContaVF-validacion\copia.accdb"), (otra.Estado, otra.BdConfigurada));

        c.Configurar(@"D:\Antiguo\ProContaConector.exe", Bd, "Ronald");
        Assert.Equal(EstadoConectorClaude.OtroEjecutable, c.Comprobar(Exe, Bd).Estado);

        // La comparación de rutas no distingue mayúsculas.
        c.Configurar(Exe, Bd.ToUpperInvariant(), "Ronald");
        Assert.Equal(EstadoConectorClaude.Correcto, c.Comprobar(Exe, Bd).Estado);
    }

    [Fact]
    public void UnFicheroInvalidoNoSeToca()
    {
        // Dos objetos seguidos: lo que queda al pegar un bloque debajo del que ya había.
        const string texto = "{ \"preferences\": {} }\n{ \"mcpServers\": {} }";
        File.WriteAllText(Fichero, texto);
        var c = new ConectorClaudeDesktop(Fichero);
        Assert.Equal(EstadoConectorClaude.ConfiguracionInvalida, c.Comprobar(Exe, Bd).Estado);
        Assert.ThrowsAny<System.Text.Json.JsonException>(() => c.Configurar(Exe, Bd, "Ronald"));
        Assert.Equal(texto, File.ReadAllText(Fichero));
    }
}
