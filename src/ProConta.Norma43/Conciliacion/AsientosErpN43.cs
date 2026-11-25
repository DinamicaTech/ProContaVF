using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Datos.Persistencia;

namespace ProConta.Norma43.Conciliacion;

/// <summary>
/// Lo que hace falta saber del asiento del ERP (importado de ContaSol) de una línea N43 para decidir si «Deshacer el asiento…» lo puede borrar
/// (norma43/conciliacion 2026-11-25 15:30): sus cuentas, si lleva libro de IVA, qué otro proceso lo usa y qué líneas lo comparten.
/// </summary>
/// <param name="Cuentas">Cuenta de cada apunte.</param>
/// <param name="UsadoPor">Otro proceso que lo tiene como su asiento (p. ej. «una factura de compra»), o null.</param>
/// <param name="Lineas">Líneas N43 contabilizadas con este asiento (más de una en traspasos y cargos agrupados del ERP).</param>
public sealed record AsientoErpN43(int IdAsiento, bool ImportadoContaSol, IReadOnlyList<string> Cuentas, bool LibroIva, string? UsadoPor, IReadOnlyList<int> Lineas);

/// <summary>Reglas y lecturas del deshacer de las líneas contabilizadas en el ERP (norma43/conciliacion 2026-11-25 15:30).</summary>
public static class AsientosErpN43
{
    /// <summary>Cuenta puente del ERP: el asiento de la línea no lleva el banco, que va en otro asiento.</summary>
    public const string PrefijoPuente = "555";

    private static readonly string[] Iva = ["472", "477"];
    private static readonly string[] Nomina = ["640", "476"];

    /// <summary>
    /// Por qué no se puede deshacer el asiento del ERP de una línea, o null si es un asiento «de banco limpio»: importado de ContaSol, con la
    /// 572 del banco de la línea, sin la 555 puente, sin IVA (472/477 o libro de IVA), sin nómina (640/476) y sin otro proceso que lo use.
    /// </summary>
    /// <param name="cuentaBanco">La 572 de la línea en su ejercicio; null si no se conoce (entonces basta con que lleve alguna 572).</param>
    public static string? Bloqueo(AsientoErpN43? a, string? cuentaBanco)
    {
        if (a is null) return "Su asiento ya no existe: pulsa F5 para releer las líneas.";
        var cuentas = a.Cuentas.Select(c => (c ?? "").Trim()).ToList();
        string? Con(IEnumerable<string> prefijos) => cuentas.FirstOrDefault(c => prefijos.Any(p => c.StartsWith(p, StringComparison.Ordinal)));
        if (!a.ImportadoContaSol)
            return $"Su asiento {a.IdAsiento} no es el que trajo ContaSol: corrígelo desde el Mayor.";
        if (a.LibroIva || Con(Iva) is not null)
            return $"Su asiento {a.IdAsiento} es el de una factura (lleva IVA{(Con(Iva) is { } iva ? $", {iva}" : "")}): borrarlo dejaría la factura sin asiento. Corrígelo desde el Mayor con «Asistente de corrección…».";
        if (a.UsadoPor is not null)
            return $"Su asiento {a.IdAsiento} es también el de {a.UsadoPor}: borrarlo lo dejaría sin asiento. Corrígelo desde el Mayor.";
        if (Con(Nomina) is { } nomina)
            return $"Su asiento {a.IdAsiento} es el de una nómina (lleva la {nomina}): borrarlo quitaría la nómina. Corrígelo desde el Mayor.";
        if (Con([PrefijoPuente]) is { } puente)
            return $"Su asiento {a.IdAsiento} pasa por la cuenta puente {puente} y el banco va en otro asiento: borrarlo dejaría la {PrefijoPuente} descuadrada. Corrígelo desde el Mayor.";
        var bancos = cuentas.Where(ReglasAsiento.EsCuentaBanco).ToList();
        if (bancos.Count == 0)
            return $"Su asiento {a.IdAsiento} no lleva la 572 del banco: es el de otra operación. Corrígelo desde el Mayor.";
        if (!string.IsNullOrWhiteSpace(cuentaBanco) && !bancos.Contains(cuentaBanco.Trim(), StringComparer.Ordinal))
            return $"Su asiento {a.IdAsiento} lleva la {bancos[0]} y no la {cuentaBanco.Trim()} de este banco: corrígelo desde el Mayor.";
        return null;
    }

    /// <summary>Lee los asientos indicados (los del ERP de unas líneas) con sus cuentas, su libro de IVA, quién más los usa y sus líneas.</summary>
    public static async Task<IReadOnlyDictionary<int, AsientoErpN43>> LeerAsync(ISesionDatos s, IEnumerable<int> idsAsiento)
    {
        var r = new Dictionary<int, AsientoErpN43>();
        foreach (var bloque in idsAsiento.Distinct().Chunk(300))
        {
            var lista = string.Join(",", bloque.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            var cabeceras = await s.ConsultarAsync($"SELECT IdAsiento, EsAutomatico, MotorPropietario FROM Asientos WHERE IdAsiento IN ({lista})",
                f => (Id: f.Entero("IdAsiento"), ContaSol: !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico")
                    && string.Equals(f.Texto("MotorPropietario"), OpcionesLote.MotorImportadorContaSol, StringComparison.Ordinal)));
            if (cabeceras.Count == 0) continue;
            var cuentas = (await s.ConsultarAsync($"SELECT IdAsiento, CuentaContable FROM Apuntes WHERE IdAsiento IN ({lista}) ORDER BY IdAsiento, Orden",
                f => (Id: f.Entero("IdAsiento"), Cuenta: (f.Texto("CuentaContable") ?? "").Trim()))).ToLookup(x => x.Id, x => x.Cuenta);
            var conIva = new HashSet<int>();
            foreach (var tabla in new[] { "IVARepercutido", "IVASoportado" })
                conIva.UnionWith(await s.ConsultarAsync($"SELECT DISTINCT IdAsiento FROM {tabla} WHERE IdAsiento IN ({lista})", f => f.Entero("IdAsiento")));
            var usados = new Dictionary<int, string>();
            foreach (var (tabla, campo, que) in Usos)
                foreach (var id in await s.ConsultarAsync($"SELECT DISTINCT {campo} AS Id FROM {tabla} WHERE {campo} IN ({lista})", f => f.Entero("Id")))
                    usados.TryAdd(id, que);
            var lineas = (await s.ConsultarAsync($"SELECT IdAsiento, IdLineaExtractoBancario FROM ContabilizacionesLineaExtracto WHERE IdAsiento IN ({lista}) ORDER BY IdLineaExtractoBancario",
                f => (Id: f.Entero("IdAsiento"), Linea: f.Entero("IdLineaExtractoBancario")))).ToLookup(x => x.Id, x => x.Linea);
            foreach (var c in cabeceras)
                r[c.Id] = new AsientoErpN43(c.Id, c.ContaSol, [.. cuentas[c.Id]], conIva.Contains(c.Id), usados.GetValueOrDefault(c.Id), [.. lineas[c.Id].Distinct()]);
        }
        return r;
    }

    /// <summary>Procesos que guardan un asiento propio: si el del ERP es uno de ellos, no se borra desde la línea.</summary>
    private static readonly (string Tabla, string Campo, string Que)[] Usos =
    [
        ("FacturasCompra", "IdAsientoContable", "una factura de compra"),
        ("FacturasCompra", "IdAsientoAnulado", "la anulación de una factura de compra"),
        ("LiquidacionesIva", "IdAsiento", "una liquidación del IVA"),
        ("ImpuestosSociedades", "IdAsiento", "un impuesto de sociedades"),
        ("RepartosDividendos", "IdAsiento", "un reparto de dividendos"),
        ("AplicacionesResultado", "IdAsiento", "una aplicación del resultado"),
        ("DetallesLoteAmortizacionActivo", "IdAsiento", "una amortización"),
    ];
}
