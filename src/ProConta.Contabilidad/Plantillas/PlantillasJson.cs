using System.Globalization;
using System.Text.Json;
using ProConta.Contabilidad.Asientos;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;

namespace ProConta.Contabilidad.Plantillas;

/// <summary>Ejemplo de una plantilla ya leído: el origen, los datos y el entorno ficticios de su vista previa.</summary>
public sealed record EjemploPlantilla(OrigenEjecucion Origen, IReadOnlyDictionary<string, string?> Datos, EntornoSociedad Entorno);

/// <summary>
/// Plantillas en JSON: el catálogo inicial (la semilla de la migración, src/ProConta.Datos.Esquema/Semillas/PlantillasAsiento.json)
/// y el ejemplo de cada plantilla (columna PlantillasAsiento.Ejemplo), del que sale su vista previa sin texto escrito a mano.
/// </summary>
public static class PlantillasJson
{
    /// <summary>Contexto ficticio de las vistas previas (no corresponde a ninguna sociedad).</summary>
    public const string SociedadEjemplo = "EJE";

    /// <summary>Lee un catálogo (una lista de plantillas en el formato de la semilla).</summary>
    public static IReadOnlyList<PlantillaAsiento> LeerCatalogo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.EnumerateArray().Select((e, i) => LeerPlantilla(e, -(i + 1))).ToList();
    }

    private static PlantillaAsiento LeerPlantilla(JsonElement e, int id) => new(
        id,
        Texto(e, "nombre")!,
        Texto(e, "descripcion") ?? "",
        Tipo(Texto(e, "tipo")),
        Origen(Texto(e, "origen")),
        e.TryGetProperty("datos", out var datos)
            ? datos.EnumerateArray().Select((d, i) => new DatoEntrada(Texto(d, "nombre")!, Texto(d, "etiqueta") ?? Texto(d, "nombre")!, TipoDeDato(Texto(d, "tipo")),
                Texto(d, "valorPropuesto"), Texto(d, "filtroCuenta"), Texto(d, "ayuda"), !d.TryGetProperty("obligatorio", out var o) || o.GetBoolean(), i + 1)).ToList()
            : [],
        e.TryGetProperty("lineas", out var lineas)
            ? lineas.EnumerateArray().Select(l => new LineaPlantilla(l.GetProperty("asiento").GetInt32(), l.GetProperty("orden").GetInt32(), Texto(l, "cuenta") ?? "",
                Texto(l, "lado") ?? "", Texto(l, "importe") ?? "", Texto(l, "concepto"), Texto(l, "centro"), Texto(l, "condicion"),
                l.TryGetProperty("porCadaApunte", out var pc) && pc.GetBoolean())).ToList()
            : [],
        Texto(e, "idSociedad"),
        Texto(e, "claveProceso"),
        Regla(Texto(e, "reglaFecha")),
        e.TryGetProperty("agruparPorCuenta", out var ag) && ag.GetBoolean(),
        !e.TryGetProperty("activa", out var ac) || ac.GetBoolean(),
        Texto(e, "motivoPropuesto"),
        e.TryGetProperty("ejemplo", out var ej) ? ej.GetRawText() : null,
        e.TryGetProperty("orden", out var or) ? or.GetInt32() : 0);

    /// <summary>
    /// Lee el ejemplo de una plantilla: ejercicio y trimestre activo, cuentas de la empresa (113, 570), nombres de cuentas, asiento de
    /// origen con el apunte elegido o registros del proceso, datos de entrada y últimas cuentas usadas. Todo es ficticio.
    /// </summary>
    public static EjemploPlantilla LeerEjemplo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        var ejercicio = e.TryGetProperty("ejercicio", out var ej) ? ej.GetInt32() : DateTime.Today.Year;
        var contexto = new ContextoSociedadEjercicio(SociedadEjemplo, 0, ejercicio);
        var cuentas = Mapa(e, "cuentas");
        var entorno = new EntornoSociedad(contexto, Texto(e, "cuentaCuadres"), Texto(e, "cuentaTesoreria"),
            e.TryGetProperty("trimestreActivo", out var t) ? t.GetInt32() : null, cuentas, Mapa(e, "ultimasCuentas"));

        OrigenEjecucion origen = OrigenEjecucion.Ninguno;
        if (e.TryGetProperty("asiento", out var a))
        {
            var asiento = new Asiento(contexto, Fecha(a, "fecha") ?? new DateTime(ejercicio, 1, 1), Texto(a, "descripcion") ?? "Asiento de ejemplo") { IdAsiento = 0 };
            var orden = 0;
            foreach (var x in a.GetProperty("apuntes").EnumerateArray())
                asiento.Apuntes.Add(new Apunte
                {
                    Orden = ++orden,
                    CodigoCuenta = Texto(x, "cuenta") ?? "",
                    Debe = Importe.Redondear(x.TryGetProperty("debe", out var d) ? d.GetDecimal() : 0m),
                    Haber = Importe.Redondear(x.TryGetProperty("haber", out var h) ? h.GetDecimal() : 0m),
                    Descripcion = Texto(x, "concepto"),
                    NumeroDocumento = Texto(x, "documento"),
                    IdCentroCoste = x.TryGetProperty("centro", out var c) ? c.GetInt32() : null,
                });
            if (a.TryGetProperty("automatico", out var au) && au.GetBoolean()) asiento.Lote = new IdentidadLote("ejemplo", "ejemplo", "ejemplo");
            origen = new OrigenApunte(asiento, e.TryGetProperty("apunte", out var ap) ? ap.GetInt32() : 1, a.TryGetProperty("libroIva", out var iva) && iva.GetBoolean());
        }
        else if (e.TryGetProperty("registros", out var registros))
        {
            origen = new OrigenProceso(Texto(e, "claveProceso") ?? "nominas", registros.EnumerateArray().Select(r => new RegistroProceso(
                Texto(r, "clave") ?? "", Fecha(r, "fecha") ?? new DateTime(ejercicio, 1, 31),
                r.TryGetProperty("campos", out var campos)
                    ? campos.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind switch
                    {
                        JsonValueKind.Number => (object?)p.Value.GetDecimal(),
                        JsonValueKind.True => true,
                        JsonValueKind.False => false,
                        JsonValueKind.Null => null,
                        _ => p.Value.GetString(),
                    }, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object?>())).ToList());
        }
        var datos = Mapa(e, "datos")?.ToDictionary(k => k.Key, k => (string?)k.Value, StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        return new EjemploPlantilla(origen, datos, entorno);
    }

    public static TipoPlantilla Tipo(string? texto) => string.Equals(texto, "SISTEMA", StringComparison.OrdinalIgnoreCase) ? TipoPlantilla.Sistema : TipoPlantilla.Usuario;
    public static string Codigo(TipoPlantilla t) => t == TipoPlantilla.Sistema ? "SISTEMA" : "USUARIO";

    public static OrigenPlantilla Origen(string? texto) => texto?.ToUpperInvariant() switch
    {
        "APUNTE" => OrigenPlantilla.Apunte,
        "PROCESO" => OrigenPlantilla.Proceso,
        _ => OrigenPlantilla.Ninguno,
    };
    public static string Codigo(OrigenPlantilla o) => o.ToString().ToUpperInvariant();

    public static ReglaFecha Regla(string? texto) =>
        string.Equals(texto, "FECHA_ORIGEN", StringComparison.OrdinalIgnoreCase) ? ReglaFecha.FechaOrigen : ReglaFecha.TrimestreActivo;
    public static string Codigo(ReglaFecha r) => r == ReglaFecha.FechaOrigen ? "FECHA_ORIGEN" : "TRIMESTRE_ACTIVO";

    public static TipoDato TipoDeDato(string? texto) => texto?.ToUpperInvariant() switch
    {
        "CUENTA" => TipoDato.Cuenta,
        "TEXTO" => TipoDato.Texto,
        "FECHA" => TipoDato.Fecha,
        _ => TipoDato.Importe,
    };
    public static string Codigo(TipoDato t) => t.ToString().ToUpperInvariant();

    private static string? Texto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind != JsonValueKind.Null ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.GetRawText()) : null;

    private static DateTime? Fecha(JsonElement e, string nombre) =>
        Texto(e, nombre) is { } f ? DateTime.ParseExact(f, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

    private static Dictionary<string, string>? Mapa(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var m) && m.ValueKind == JsonValueKind.Object
            ? m.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()! : p.Value.GetRawText(), StringComparer.OrdinalIgnoreCase)
            : null;
}
