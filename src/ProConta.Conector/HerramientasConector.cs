using System.ComponentModel;
using System.Text.Encodings.Web;
using System.Text.Json;
using ModelContextProtocol.Server;
using ProConta.Nucleo.Resultados;

namespace ProConta.Conector;

/// <summary>Herramientas MCP que ve Claude Desktop (plataforma/conector-claude). Cada una devuelve JSON o el texto de los errores para el operador.</summary>
[McpServerToolType]
public sealed class HerramientasConector(ServicioConector servicio)
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "sociedades", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Lista las sociedades de ProContaVF con sus ejercicios (si están cerrados y el trimestre activo). Úsala primero para saber el código de sociedad y el ejercicio.")]
    public async Task<string> Sociedades() => Texto(await servicio.SociedadesAsync());

    [McpServerTool(Name = "buscar_asientos", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Busca asientos contabilizados de una sociedad y ejercicio por número, fechas, cuenta (prefijo) o texto de la descripción, concepto o documento. " +
        "Devuelve como mucho 50 (o 'maximo'). El número es el que se ve en el Diario de ProContaVF.")]
    public async Task<string> BuscarAsientos(
        [Description("Código de la sociedad (p. ej. «01»).")] string idSociedad,
        [Description("Año del ejercicio.")] int ejercicio,
        [Description("Número de asiento exacto.")] int? numero = null,
        [Description("Fecha inicial, AAAA-MM-DD.")] DateTime? desde = null,
        [Description("Fecha final, AAAA-MM-DD.")] DateTime? hasta = null,
        [Description("Cuenta o prefijo de cuenta (p. ej. 472 o 4300001).")] string? cuenta = null,
        [Description("Texto a buscar en la descripción, el concepto o el documento.")] string? texto = null,
        [Description("Solo asientos cuyo Debe no coincide con el Haber.")] bool soloDescuadrados = false,
        [Description("Máximo de asientos (1-200).")] int maximo = ServicioConector.MaximoAsientos) =>
        Texto(await servicio.BuscarAsientosAsync(idSociedad, ejercicio, numero, desde, hasta, cuenta, texto, soloDescuadrados, maximo));

    [McpServerTool(Name = "ver_asiento", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Muestra un asiento completo: apuntes con el nombre de cada cuenta, si es de una factura (libro de IVA) o de un proceso automático, su versión, " +
        "sus relaciones con otros asientos y cómo se corrige.")]
    public async Task<string> VerAsiento([Description("Número del asiento.")] int numero) => Texto(await servicio.VerAsientoAsync(numero));

    [McpServerTool(Name = "buscar_cuentas", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Busca cuentas del plan de la sociedad y ejercicio por código, prefijo o nombre. Solo las imputables (subcuentas) admiten apuntes.")]
    public async Task<string> BuscarCuentas(string idSociedad, int ejercicio,
        [Description("Código, prefijo (430, 4300.) o parte del nombre.")] string texto,
        [Description("Máximo de cuentas (1-200).")] int maximo = 50) =>
        Texto(await servicio.BuscarCuentasAsync(idSociedad, ejercicio, texto, maximo));

    [McpServerTool(Name = "mayor_cuenta", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Mayor de una subcuenta en el ejercicio: saldo de apertura, saldo actual (Debe − Haber) y sus movimientos, opcionalmente entre dos fechas.")]
    public async Task<string> MayorCuenta(string idSociedad, int ejercicio,
        [Description("Subcuenta; admite el punto (43.1 = 4300001).")] string cuenta,
        DateTime? desde = null, DateTime? hasta = null,
        [Description("Máximo de movimientos (1-1000).")] int maximo = 200) =>
        Texto(await servicio.MayorAsync(idSociedad, ejercicio, cuenta, desde, hasta, maximo));

    [McpServerTool(Name = "avisos_salud", ReadOnly = true, Destructive = false, OpenWorld = false)]
    [Description("Avisos de salud de la contabilidad de la pantalla de inicio (descuadres, IVA, bancos, plazos…) con cómo se resuelve cada uno. Tarda unos segundos.")]
    public async Task<string> AvisosSalud(string idSociedad, int ejercicio) => Texto(await servicio.AvisosSaludAsync(idSociedad, ejercicio));

    [McpServerTool(Name = "proponer_asientos", ReadOnly = true, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Valida uno o varios asientos manuales nuevos con todas las reglas de ProContaVF (cuadre, cuentas, ejercicio y trimestre cerrados) SIN GRABAR NADA y " +
        "devuelve un número de propuesta. Para corregir un asiento manual indica 'asientoOrigen' y normalmente dos asientos: uno que lo anula (anula = true, las mismas " +
        "cuentas con Debe y Haber cambiados) y otro con los importes correctos. Los asientos de factura y los de procesos automáticos (salvo los importados de ContaSol sin IVA) no se corrigen así. " +
        "Enseña la propuesta al operador en una tabla y espera su OK antes de grabarla.")]
    public async Task<string> ProponerAsientos(string idSociedad, int ejercicio,
        [Description("Por qué se hace, con las palabras del operador. Queda en las notas del asiento y en el registro de cambios.")] string motivo,
        [Description("Asientos: fecha (AAAA-MM-DD), descripción, apuntes (cuenta, debe o haber positivo, concepto y documento opcionales) y 'anula' si es el que anula el origen.")]
        List<AsientoPedido> asientos,
        [Description("Número del asiento que se corrige, si es una corrección.")] int? asientoOrigen = null)
    {
        var r = await servicio.ProponerAsync(new PeticionPropuesta(idSociedad, ejercicio, motivo, asientos ?? [], asientoOrigen));
        return r.EsCorrecto
            ? JsonSerializer.Serialize(new
            {
                propuesta = r.Valor.Numero,
                estado = "Validada, sin grabar",
                asientoOrigen = r.Valor.AsientoOrigen,
                asientos = r.Valor.Asientos,
                siguientePaso = $"Enseña los asientos al operador. Solo si dice que están bien, llama a grabar_propuesta con propuesta = {r.Valor.Numero}.",
            }, Json)
            : Errores(r.Errores);
    }

    [McpServerTool(Name = "grabar_propuesta", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Graba en ProContaVF, tal como se validó, una propuesta de proponer_asientos. Úsala SOLO cuando el operador haya aceptado expresamente esa propuesta. " +
        "Si el asiento de origen ha cambiado desde la propuesta, no graba nada.")]
    public async Task<string> GrabarPropuesta([Description("Número de propuesta que devolvió proponer_asientos.")] int propuesta)
    {
        var r = await servicio.GrabarAsync(propuesta);
        return r.EsCorrecto
            ? JsonSerializer.Serialize(new { grabada = propuesta, asientos = r.Valor, nota = "Se ven en el Diario de ProContaVF (pulsa Actualizar si está abierto)." }, Json)
            : Errores(r.Errores);
    }

    private static string Texto<T>(T valor) => JsonSerializer.Serialize(valor, Json);

    private static string Texto<T>(Resultado<T> r) => r.EsCorrecto ? JsonSerializer.Serialize(r.Valor, Json) : Errores(r.Errores);

    internal static string Errores(IEnumerable<Error> errores) => "No se ha hecho nada. " + string.Join("\n", errores.Select(e => "• " + e.TextoOperador()));
}
