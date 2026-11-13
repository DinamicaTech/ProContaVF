using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Contabilidad.CentrosCoste;

/// <summary>
/// Cuenta de IVA de un tipo (organizacion/sociedades-ejercicios 2026-11-13 20:55): no se configura, se calcula. Es la serie
/// de la actividad del centro de coste más el porcentaje con tres cifras; la comparten compras y ventas.
/// - Soportado: la serie de la actividad (4 dígitos, p. ej. 4728) o, sin actividad, 4720. 21 % → 4720021, 4728021.
/// - Repercutido: «477» y el cuarto dígito de la serie (sin actividad, 0). 21 % → 4770021, 4778021.
/// La terminación es el porcentaje entero con tres cifras (21 → 021, 4 → 004, 0 → 000). Un porcentaje con decimales, negativo
/// o de 1000 o más no tiene cuenta: <see cref="Soportado"/> y <see cref="Repercutido"/> devuelven null y
/// <see cref="MotivoSinCuenta"/> explica por qué.
/// </summary>
public static class CuentasIva
{
    public const string SerieSoportadoGeneral = "4720";
    public const string SerieRepercutidoGeneral = "4770";

    /// <summary>Terminación de tres cifras del porcentaje, o null si no es un entero de 0 a 999.</summary>
    public static string? Terminacion(decimal porcentaje) =>
        porcentaje >= 0m && porcentaje < 1000m && decimal.Truncate(porcentaje) == porcentaje
            ? ((int)porcentaje).ToString("000", CultureInfo.InvariantCulture)
            : null;

    /// <summary>Serie de 4 dígitos válida de una actividad, o null si no tiene (o no es válida).</summary>
    private static string? SerieValida(string? serie) =>
        serie?.Trim() is { Length: 4 } s && s.All(char.IsAsciiDigit) ? s : null;

    /// <summary>Cuenta de IVA soportado del tipo para la serie de la actividad (null o vacía: sin actividad, 4720).</summary>
    public static string? Soportado(decimal porcentaje, string? serieActividad = null) =>
        Terminacion(porcentaje) is { } t ? (SerieValida(serieActividad) ?? SerieSoportadoGeneral) + t : null;

    /// <summary>Cuenta de IVA repercutido del tipo: «477» más el cuarto dígito de la serie de la actividad (sin actividad, 4770).</summary>
    public static string? Repercutido(decimal porcentaje, string? serieActividad = null) =>
        Terminacion(porcentaje) is { } t ? (SerieValida(serieActividad) is { } s ? "477" + s[3] : SerieRepercutidoGeneral) + t : null;

    /// <summary>Por qué un porcentaje no tiene cuenta de IVA calculable, y cómo arreglarlo.</summary>
    public static string MotivoSinCuenta(decimal porcentaje) =>
        $"El tipo de IVA del {porcentaje.ToString("0.####", CultureInfo.GetCultureInfo("es-ES"))} % no tiene cuenta: la cuenta de IVA es la serie más el porcentaje entero con tres cifras " +
        "(21 % → 4720021), y un porcentaje con decimales, negativo o de 1000 o más no se puede escribir así. Corrige el tipo de IVA de la factura o del catálogo «Tipos de IVA».";
}

/// <summary>
/// Tipos de IVA del catálogo TiposDeIVA (dueño: facturacion/factura-venta), que usan también compras: los activos, de mayor a
/// menor, redondeados a dos decimales (la columna es SINGLE).
/// </summary>
public static class CatalogoTiposIva
{
    public static Task<IReadOnlyList<decimal>> ActivosAsync(IBaseDatos bd) => bd.EjecutarAsync(ActivosAsync);

    public static async Task<IReadOnlyList<decimal>> ActivosAsync(ISesionDatos s)
    {
        var tipos = await s.ConsultarAsync("SELECT Valor FROM TiposDeIVA WHERE Activo = True", f => Math.Round((decimal)f.Doble("Valor"), 2));
        return tipos.Distinct().OrderByDescending(v => v).ToList();
    }
}
