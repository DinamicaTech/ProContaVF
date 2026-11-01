namespace ProConta.Terceros.UI.Logica;

/// <summary>Opción de una lista: el valor que se guarda y el texto que se ve.</summary>
public sealed record Opcion(string Valor, string Texto)
{
    public override string ToString() => Texto;
}

/// <summary>
/// Países de los domicilios. La BD guarda el código numérico ISO 3166 (724 es España); un código que no está en la
/// lista se muestra tal cual y se conserva.
/// </summary>
public static class Paises
{
    public const string Espana = "724";

    public static readonly IReadOnlyList<Opcion> Lista =
    [
        new("724", "España"), new("620", "Portugal"), new("250", "Francia"), new("276", "Alemania"), new("380", "Italia"),
        new("826", "Reino Unido"), new("372", "Irlanda"), new("528", "Países Bajos"), new("056", "Bélgica"), new("442", "Luxemburgo"),
        new("756", "Suiza"), new("040", "Austria"), new("208", "Dinamarca"), new("752", "Suecia"), new("578", "Noruega"), new("246", "Finlandia"),
        new("616", "Polonia"), new("203", "Chequia"), new("642", "Rumanía"), new("300", "Grecia"), new("020", "Andorra"), new("504", "Marruecos"),
        new("840", "Estados Unidos"), new("124", "Canadá"), new("484", "México"), new("032", "Argentina"), new("152", "Chile"), new("170", "Colombia"),
        new("604", "Perú"), new("076", "Brasil"), new("156", "China"), new("392", "Japón"), new("356", "India"),
    ];

    public static bool EsEspana(string? codigo) => string.IsNullOrWhiteSpace(codigo) || codigo.Trim() is Espana or "ES" or "ESP";

    private static readonly Dictionary<string, string> _alias = new(StringComparer.OrdinalIgnoreCase) { ["ES"] = "España", ["ESP"] = "España" };

    public static string Nombre(string? codigo) =>
        string.IsNullOrWhiteSpace(codigo) ? "" : Lista.FirstOrDefault(p => p.Valor == codigo.Trim())?.Texto ?? codigo.Trim();

    /// <summary>La lista con el código dado aunque no esté en ella (para no perder un país antiguo).</summary>
    public static IReadOnlyList<Opcion> ListaCon(string? codigo) =>
        string.IsNullOrWhiteSpace(codigo) || Lista.Any(p => p.Valor == codigo.Trim()) ? Lista
            : [.. Lista, new Opcion(codigo.Trim(), _alias.TryGetValue(codigo.Trim(), out var n) ? $"{n} ({codigo.Trim()})" : $"Código {codigo.Trim()}")];
}

/// <summary>Tipos de identificación y la etiqueta del campo según el tipo (maqueta: NIF, CIF, NIE, NIF-IVA, Nº pasaporte…).</summary>
public static class Identificaciones
{
    public static readonly IReadOnlyList<Opcion> Lista =
    [
        new("NIF", "NIF"), new("CIF", "CIF"), new("NIE", "NIE"), new("VAT", "NIF-IVA (VAT)"), new("PASAPORTE", "Pasaporte"), new("OTRO", "Otro documento"),
    ];

    public static string Etiqueta(string? tipo) => (tipo ?? "").Trim().ToUpperInvariant() switch
    {
        "CIF" => "CIF",
        "NIE" => "NIE",
        "VAT" => "NIF-IVA",
        "PASAPORTE" => "Nº pasaporte",
        "OTRO" => "Nº de documento",
        _ => "NIF"
    };

    /// <summary>Tipo propuesto para un tercero nuevo según su tipo de persona.</summary>
    public static string Propuesto(TipoPersona? tipo) => tipo == TipoPersona.Fisica ? "NIF" : "CIF";
}

/// <summary>Finalidades de los domicilios adicionales (DomiciliosTercero.TipoDomicilio).</summary>
public static class TiposDomicilio
{
    public static readonly IReadOnlyList<Opcion> Lista =
    [
        new("FISCAL", "Fiscal"), new("ENVIO", "Envío"), new("ALMACEN", "Almacén"), new("OFICINA", "Oficina"), new("FACTURACION", "Facturación"), new("OTRO", "Otro"),
    ];

    public static string Texto(string? valor) =>
        Lista.FirstOrDefault(o => string.Equals(o.Valor, valor?.Trim(), StringComparison.OrdinalIgnoreCase))?.Texto ?? (valor ?? "");

    public static IReadOnlyList<Opcion> ListaCon(string? valor) =>
        string.IsNullOrWhiteSpace(valor) || Lista.Any(o => string.Equals(o.Valor, valor.Trim(), StringComparison.OrdinalIgnoreCase))
            ? Lista : [.. Lista, new Opcion(valor.Trim(), valor.Trim())];
}

public static class TiposPersona
{
    public static string Texto(TipoPersona? tipo) => tipo switch
    {
        TipoPersona.Fisica => "Particular",
        TipoPersona.Juridica => "Empresa",
        TipoPersona.EntidadSinPersonalidad => "Entidad",
        _ => "Sin clasificar"
    };

    public static string Ayuda(TipoPersona? tipo) => tipo switch
    {
        TipoPersona.Fisica => "Persona física: nombre y apellidos",
        TipoPersona.Juridica => "Persona jurídica (sociedad): razón social",
        TipoPersona.EntidadSinPersonalidad => "Entidad sin personalidad jurídica (comunidad de propietarios, asociación…): razón social",
        _ => "Tercero antiguo pendiente de clasificar: elige su tipo de persona"
    };
}
