using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por el nodo activos/activos-financieros (docs/activos/activos-financieros.md).</summary>
public static class MigracionesActivosFinancieros
{
    /// <summary>Código del tipo de activo de las inversiones financieras (heredado de la maestra).</summary>
    public const string TipoInversion = "INVERSION";

    /// <summary>
    /// Alta de activos financieros por ISIN (activos/activos-financieros 2026-11-23 20:32 y 20:38):
    /// <list type="bullet">
    /// <item>CambiosDivisa: cambios de referencia diarios del BCE (unidades de la moneda por un euro), guardados al pedirlos;</item>
    /// <item>ConexionesCotizaciones: la clave de EODHD de cada sociedad, cifrada (Parámetros › Integraciones › «Cotizaciones · EODHD»);</item>
    /// <item>en InstrumentosFinancieros, el símbolo de EODHD, la bolsa y de dónde salen sus datos (Yahoo, EODHD o a mano);</item>
    /// <item>el tipo de activo INVERSION («Inversión financiera») activo y no amortizable, para elegirlo en «Nuevo activo…».</item>
    /// </list>
    /// No cambia datos existentes salvo el tipo INVERSION.
    /// </summary>
    public static Migracion AltaPorIsin { get; } = new(
        "20261123_2045_ACTIVOS_FINANCIEROS_ISIN", 261123204,
        "Activos financieros: cambios de divisa del BCE, clave de EODHD, símbolo de EODHD y bolsa del instrumento y tipo «Inversión financiera» activo.",
        "activos/activos-financieros",
        new CrearTabla("CambiosDivisa",
            "CREATE TABLE CambiosDivisa (IdCambioDivisa COUNTER CONSTRAINT PK_CambiosDivisa PRIMARY KEY, Moneda TEXT(3) NOT NULL, Fecha DATETIME NOT NULL, " +
            "UnidadesPorEuro DECIMAL(18,6) NOT NULL, Origen TEXT(20) NOT NULL, FechaHoraImportacion DATETIME NOT NULL)"),
        new CrearIndice("CambiosDivisa", "UX_CambiosDivisa_MonedaFecha", "CREATE UNIQUE INDEX UX_CambiosDivisa_MonedaFecha ON CambiosDivisa (Moneda, Fecha)"),
        new CrearTabla("ConexionesCotizaciones",
            "CREATE TABLE ConexionesCotizaciones (IdSociedad TEXT(3) NOT NULL CONSTRAINT PK_ConexionesCotizaciones PRIMARY KEY, Activa YESNO NOT NULL, " +
            "SecretoProtegido LONGTEXT)"),
        new AnadirColumna("InstrumentosFinancieros", "TickerEodhd", "TEXT(40)"),
        new AnadirColumna("InstrumentosFinancieros", "Bolsa", "TEXT(60)"),
        new AnadirColumna("InstrumentosFinancieros", "OrigenDatos", "TEXT(10)"),
        new PasoPersonalizado(
            "Dejar activo el tipo de activo «Inversión financiera» (INVERSION), no amortizable",
            "INSERT INTO TiposActivo (Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo) si falta; UPDATE TiposActivo SET Categoria = 'INVERSION', " +
            "EsAmortizablePredeterminado = False, EstaActivo = True WHERE Codigo = 'INVERSION'",
            async (s, _) => Convert.ToInt32(await s.EscalarAsync(
                "SELECT COUNT(*) FROM TiposActivo WHERE Codigo = ? AND EstaActivo = True AND EsAmortizablePredeterminado = False AND Categoria = ?", TipoInversion, TipoInversion),
                CultureInfo.InvariantCulture) > 0,
            async (s, _) =>
            {
                var existe = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposActivo WHERE Codigo = ?", TipoInversion), CultureInfo.InvariantCulture) > 0;
                if (!existe)
                    await s.EjecutarAsync("INSERT INTO TiposActivo (Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo) VALUES (?, ?, ?, False, True)",
                        TipoInversion, "Inversión financiera", TipoInversion);
                else
                    await s.EjecutarAsync("UPDATE TiposActivo SET Categoria = ?, EsAmortizablePredeterminado = False, EstaActivo = True WHERE Codigo = ?", TipoInversion, TipoInversion);
            }))
    {
        Descripciones =
        [
            new("CambiosDivisa", null, "Cambios de referencia diarios del euro publicados por el BCE, guardados la primera vez que hacen falta para pasar una cotización a euros."),
            new("CambiosDivisa", "Moneda", "Moneda en ISO 4217 (USD, GBP…)."),
            new("CambiosDivisa", "Fecha", "Día del cambio publicado por el BCE (solo días hábiles)."),
            new("CambiosDivisa", "UnidadesPorEuro", "Unidades de la moneda que vale un euro (el precio en euros es precio / este valor)."),
            new("CambiosDivisa", "Origen", "De dónde sale el cambio (BCE)."),
            new("CambiosDivisa", "FechaHoraImportacion", "Cuándo se pidió al BCE."),
            new("ConexionesCotizaciones", null, "Conexión de una sociedad con el proveedor de cotizaciones EODHD (Parámetros › Integraciones › «Cotizaciones · EODHD»)."),
            new("ConexionesCotizaciones", "IdSociedad", "Sociedad."),
            new("ConexionesCotizaciones", "Activa", "Si se consulta EODHD; desactivada, solo se usa Yahoo Finance."),
            new("ConexionesCotizaciones", "SecretoProtegido", "Clave (API token) de EODHD, cifrada."),
            new("InstrumentosFinancieros", "Ticker", "Símbolo del instrumento en Yahoo Finance (IWDA.AS); vacío si Yahoo no lo tiene."),
            new("InstrumentosFinancieros", "TickerEodhd", "Símbolo del instrumento en EODHD (IWDA.AS, EUNL.XETRA o ISIN.EUFUND en un fondo)."),
            new("InstrumentosFinancieros", "Bolsa", "Bolsa en la que se sigue su cotización (un mismo ISIN puede cotizar en varias)."),
            new("InstrumentosFinancieros", "OrigenDatos", "De dónde salieron su nombre y sus símbolos al darlo de alta: YAHOO, EODHD o MANUAL."),
        ],
    };
}
