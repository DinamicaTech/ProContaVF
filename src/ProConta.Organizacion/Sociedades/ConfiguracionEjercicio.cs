using ProConta.Datos.Persistencia;

namespace ProConta.Organizacion.Sociedades;

/// <summary>
/// Datos de facturación de un ejercicio de sociedad: emisor (siempre el de la empresa, Sociedades.Nombre y CIF; el Nombre del
/// ejercicio es solo su etiqueta), forma de pago predeterminada y cuenta de suplidos. Las cuentas de IVA no se configuran: las
/// calcula <c>ProConta.Contabilidad.CentrosCoste.CuentasIva</c> (organizacion/sociedades-ejercicios 2026-11-13 20:55). Solo
/// lectura: se mantienen en Parámetros › Empresas y › Ejercicios.
/// </summary>
public sealed record ConfiguracionEjercicio(
    int IdSociedadEjercicio,
    string IdSociedad,
    string NombreEmisor,
    string NifEmisor,
    byte? FormaPagoDefecto,
    string? CuentaSuplido,
    string? DireccionEmisor = null,
    string? CodigoPostalEmisor = null,
    string? PoblacionEmisor = null,
    string? ProvinciaEmisor = null);

/// <summary>Lee la configuración de facturación de un ejercicio (dueño: organizacion/sociedades-ejercicios).</summary>
public sealed class LectorConfiguracionEjercicio(IBaseDatos bd)
{
    public Task<ConfiguracionEjercicio?> ObtenerAsync(int idSociedadEjercicio) => bd.EjecutarAsync(s => ObtenerAsync(s, idSociedadEjercicio));

    public static Task<ConfiguracionEjercicio?> ObtenerAsync(ISesionDatos s, int idSociedadEjercicio) =>
        s.ConsultarPrimeroAsync(
            "SELECT se.IdSociedadEjercicio, se.IdSociedad, so.Nombre AS NombreSociedad, so.CIF AS CifSociedad, so.DireccionFiscal, so.CodigoPostalFiscal, " +
            "so.PoblacionFiscal, so.ProvinciaFiscal, se.FormaPagoDefecto, se.CuentaSuplido " +
            "FROM SociedadesEjercicios AS se LEFT JOIN Sociedades AS so ON se.IdSociedad = so.IdSociedad WHERE se.IdSociedadEjercicio = ?",
            Mapear, idSociedadEjercicio);

    private static ConfiguracionEjercicio Mapear(IFilaDatos f) => new(
        f.Entero("IdSociedadEjercicio"),
        f.Texto("IdSociedad")?.Trim() ?? "",
        f.Texto("NombreSociedad")?.Trim() ?? "",
        f.Texto("CifSociedad")?.Trim() ?? "",
        f.EsNulo("FormaPagoDefecto") ? null : (byte?)Math.Clamp(f.Entero("FormaPagoDefecto"), 0, 255),
        f.Texto("CuentaSuplido"),
        f.Texto("DireccionFiscal"),
        f.Texto("CodigoPostalFiscal"),
        f.Texto("PoblacionFiscal"),
        f.Texto("ProvinciaFiscal"));
}
