using ProConta.Datos.Persistencia;

namespace ProConta.Organizacion.Sociedades;

/// <summary>
/// Tipo de IVA predeterminado del ejercicio con su cuenta de IVA repercutido (TipoIVA1..4 y CuentaIVARepercutido1..4; sin
/// ella, la 477 con la terminación de la CuentaTipoIVA, que es la de soportado de compras: propietario 2026-11-07 11:59).
/// </summary>
public sealed record TipoIvaEjercicio(int Posicion, decimal Porcentaje, string? Cuenta);

/// <summary>
/// Datos de facturación de un ejercicio de sociedad (tabla SociedadesEjercicios): emisor, valores predeterminados
/// de las facturas y cuentas de IVA y suplidos que usa la contabilización de ventas. Solo lectura: se mantienen en la
/// pantalla de mantenimiento (Ejercicios).
/// </summary>
public sealed record ConfiguracionEjercicio(
    int IdSociedadEjercicio,
    string IdSociedad,
    string NombreEmisor,
    string NifEmisor,
    byte? FormaPagoDefecto,
    decimal? PorcentajeRetencion,
    IReadOnlyList<TipoIvaEjercicio> TiposIva,
    string? CuentaIvaCero,
    string? CuentaSuplido,
    string? DireccionEmisor = null,
    string? CodigoPostalEmisor = null,
    string? PoblacionEmisor = null,
    string? ProvinciaEmisor = null)
{
    /// <summary>Cuenta de IVA repercutido para un porcentaje, o null si el ejercicio no la tiene configurada.</summary>
    public string? CuentaIva(decimal porcentaje)
    {
        if (porcentaje == 0m && !string.IsNullOrWhiteSpace(CuentaIvaCero)) return CuentaIvaCero.Trim();
        var tipo = TiposIva.FirstOrDefault(t => t.Porcentaje == porcentaje && !string.IsNullOrWhiteSpace(t.Cuenta));
        return tipo?.Cuenta?.Trim();
    }
}

/// <summary>Lee la configuración de facturación de un ejercicio (dueño: organizacion/sociedades-ejercicios).</summary>
public sealed class LectorConfiguracionEjercicio(IBaseDatos bd)
{
    public Task<ConfiguracionEjercicio?> ObtenerAsync(int idSociedadEjercicio) => bd.EjecutarAsync(s => ObtenerAsync(s, idSociedadEjercicio));

    public static Task<ConfiguracionEjercicio?> ObtenerAsync(ISesionDatos s, int idSociedadEjercicio) =>
        s.ConsultarPrimeroAsync(
            "SELECT se.IdSociedadEjercicio, se.IdSociedad, se.Nombre, se.CIF, so.Nombre AS NombreSociedad, so.CIF AS CifSociedad, so.DireccionFiscal, so.CodigoPostalFiscal, so.PoblacionFiscal, so.ProvinciaFiscal, " +
            "se.FormaPagoDefecto, se.PorcentajeRetencion, se.TipoIVA1, se.TipoIVA2, se.TipoIVA3, se.TipoIVA4, " +
            "se.CuentaTipoIVA0, se.CuentaTipoIVA1, se.CuentaTipoIVA2, se.CuentaTipoIVA3, se.CuentaTipoIVA4, se.CuentaSuplido, " +
            "se.CuentaIVARepercutido0, se.CuentaIVARepercutido1, se.CuentaIVARepercutido2, se.CuentaIVARepercutido3, se.CuentaIVARepercutido4 " +
            "FROM SociedadesEjercicios AS se LEFT JOIN Sociedades AS so ON se.IdSociedad = so.IdSociedad WHERE se.IdSociedadEjercicio = ?",
            Mapear, idSociedadEjercicio);

    /// <summary>La cuenta de IVA repercutido del tipo; sin ella, la 477 con la terminación de su 472 de soportado.</summary>
    private static string? Repercutido(IFilaDatos f, int i) =>
        f.Texto($"CuentaIVARepercutido{i}") is { } r && !string.IsNullOrWhiteSpace(r) ? r.Trim() : RepercutidoDe(f.Texto($"CuentaTipoIVA{i}"));

    /// <summary>4720021 → 4770021. Una cuenta que no es 472 se queda como está (ya era de repercutido).</summary>
    public static string? RepercutidoDe(string? soportado) =>
        soportado?.Trim() is { Length: > 3 } c && c.StartsWith("472", StringComparison.Ordinal) ? "477" + c[3..] : soportado?.Trim();

    private static ConfiguracionEjercicio Mapear(IFilaDatos f)
    {
        var tipos = new List<TipoIvaEjercicio>();
        for (var i = 1; i <= 4; i++)
        {
            if (f.EsNulo($"TipoIVA{i}")) continue;
            // SINGLE en Access: se redondea a dos decimales para que 21 no sea 20,99999.
            tipos.Add(new TipoIvaEjercicio(i, Math.Round((decimal)f.Doble($"TipoIVA{i}"), 2), Repercutido(f, i)));
        }
        static string Primero(params string?[] v) => v.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";
        return new ConfiguracionEjercicio(
            f.Entero("IdSociedadEjercicio"),
            f.Texto("IdSociedad")?.Trim() ?? "",
            Primero(f.Texto("Nombre"), f.Texto("NombreSociedad")),
            Primero(f.Texto("CIF"), f.Texto("CifSociedad")),
            f.EsNulo("FormaPagoDefecto") ? null : (byte?)Math.Clamp(f.Entero("FormaPagoDefecto"), 0, 255),
            f.EsNulo("PorcentajeRetencion") ? null : f.Entero("PorcentajeRetencion"),
            tipos,
            Repercutido(f, 0),
            f.Texto("CuentaSuplido"),
            f.Texto("DireccionFiscal"),
            f.Texto("CodigoPostalFiscal"),
            f.Texto("PoblacionFiscal"),
            f.Texto("ProvinciaFiscal"));
    }
}
