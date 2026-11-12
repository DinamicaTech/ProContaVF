using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>
/// Catálogo inicial de plantillas de asiento (contabilidad/plantillas-asiento 2026-11-12 11:15, «Catálogo inicial»): las seis plantillas
/// de usuario que sustituyen a las opciones de frmModificarAsiento y la plantilla de sistema «Nómina», sin líneas. Está en el fichero
/// incrustado Semillas/PlantillasAsiento.json, el mismo que leen las pruebas de contabilidad para comprobar cada plantilla sin BD.
/// </summary>
public static class SemillaPlantillasAsiento
{
    private static readonly Lazy<string> _texto = new(() =>
    {
        using var flujo = typeof(SemillaPlantillasAsiento).Assembly.GetManifestResourceStream("ProConta.Datos.Esquema.Semillas.PlantillasAsiento.json")
            ?? throw new InvalidOperationException("Falta la semilla incrustada Semillas/PlantillasAsiento.json en ProConta.Datos.Esquema.");
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        return lector.ReadToEnd().Replace("\r\n", "\n");
    });

    /// <summary>El JSON del catálogo inicial.</summary>
    public static string Json => _texto.Value;

    /// <summary>SHA-256 de la semilla: entra en la firma del paso.</summary>
    public static string Huella => "semilla " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Json)))[..16];

    /// <summary>Nombres de las plantillas del catálogo, en orden.</summary>
    public static IReadOnlyList<string> Nombres
    {
        get
        {
            using var doc = JsonDocument.Parse(Json);
            return doc.RootElement.EnumerateArray().Select(e => e.GetProperty("nombre").GetString()!).ToList();
        }
    }

    /// <summary>El paso está hecho si existen todas las plantillas comunes (sin sociedad) del catálogo, por nombre.</summary>
    public static async Task<bool> CompletaAsync(ISesionDatos s, EsquemaFisico e)
    {
        if (e.Tabla("PlantillasAsiento") is null) return false;
        foreach (var nombre in Nombres)
            if (await IdAsync(s, nombre) is null) return false;
        return true;
    }

    /// <summary>Da de alta las plantillas del catálogo que falten (las que ya estén, aunque se hayan cambiado, no se tocan).</summary>
    public static async Task CargarAsync(ISesionDatos s)
    {
        using var doc = JsonDocument.Parse(Json);
        foreach (var p in doc.RootElement.EnumerateArray())
        {
            var nombre = p.GetProperty("nombre").GetString()!;
            if (await IdAsync(s, nombre) is not null) continue;
            await s.EjecutarAsync(
                "INSERT INTO PlantillasAsiento (Nombre, Descripcion, Tipo, IdSociedad, Origen, ClaveProceso, ReglaFecha, AgruparPorCuenta, Activa, MotivoPropuesto, Ejemplo, Orden) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                nombre, Texto(p, "descripcion"), Texto(p, "tipo") ?? "USUARIO", DBNull.Value, Texto(p, "origen") ?? "NINGUNO", Texto(p, "claveProceso"),
                Texto(p, "reglaFecha") ?? "TRIMESTRE_ACTIVO", Logico(p, "agruparPorCuenta", false), Logico(p, "activa", true), Texto(p, "motivoPropuesto"),
                p.TryGetProperty("ejemplo", out var ej) ? ej.GetRawText() : null, (short)(p.TryGetProperty("orden", out var o) ? o.GetInt32() : 0));
            var id = Convert.ToInt32(await s.UltimoIdAsync(), CultureInfo.InvariantCulture);
            var orden = 0;
            foreach (var d in p.GetProperty("datos").EnumerateArray())
                await s.EjecutarAsync(
                    "INSERT INTO PlantillasAsientoDatos (IdPlantillaAsiento, Orden, Nombre, Etiqueta, Tipo, ValorPropuesto, FiltroCuenta, Ayuda, Obligatorio) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    id, (short)++orden, Texto(d, "nombre"), Texto(d, "etiqueta") ?? Texto(d, "nombre"), Texto(d, "tipo") ?? "IMPORTE", Texto(d, "valorPropuesto"),
                    Texto(d, "filtroCuenta"), Texto(d, "ayuda"), Logico(d, "obligatorio", true));
            foreach (var l in p.GetProperty("lineas").EnumerateArray())
                await s.EjecutarAsync(
                    "INSERT INTO PlantillasAsientoLineas (IdPlantillaAsiento, NumeroAsiento, Orden, PorCadaApunte, FormulaCuenta, Lado, FormulaImporte, FormulaConcepto, FormulaCentro, Condicion) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    id, (short)l.GetProperty("asiento").GetInt32(), (short)l.GetProperty("orden").GetInt32(), Logico(l, "porCadaApunte", false), Texto(l, "cuenta"),
                    Texto(l, "lado"), Texto(l, "importe"), Texto(l, "concepto"), Texto(l, "centro"), Texto(l, "condicion"));
        }
    }

    private static async Task<int?> IdAsync(ISesionDatos s, string nombre) =>
        await s.EscalarAsync("SELECT TOP 1 IdPlantillaAsiento FROM PlantillasAsiento WHERE Nombre = ? AND IdSociedad IS NULL", nombre) is { } v and not DBNull
            ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : null;

    private static string? Texto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Logico(JsonElement e, string nombre, bool porDefecto) =>
        e.TryGetProperty(nombre, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : porDefecto;
}
