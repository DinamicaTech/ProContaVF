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

    /// <summary>
    /// Venta de activos financieros y su conciliación con el banco (activos/activos-financieros 2026-11-24 09:14 y 09:24):
    /// <list type="bullet">
    /// <item>OperacionesInversion.LineaN43: el movimiento bancario con que se concilió la compra o la venta («Compra de activo…» y «Venta de activo…» en
    /// Líneas N43); nulo si no está conciliada. Deshacer el asiento de la línea lo vacía;</item>
    /// <item>OperacionesInversion.ImporteAntesConciliar: el importe que tenía la operación antes de ponerle el del banco, para recuperarlo al deshacer;</item>
    /// <item>la plantilla de subcuentas del tipo «Inversión financiera», si no tiene ninguna línea: la del ERP (PlantillasInmuebles de las inversiones,
    /// propietario 2026-11-24 09:31), con la 54001 como valor del activo (rol «Valor del bien») y la 76600 y la 66600 para el beneficio y la pérdida de la
    /// venta, que la conciliación busca por su código (766 y 666).</item>
    /// </list>
    /// </summary>
    public static Migracion VentaYConciliacion { get; } = new(
        "20261124_0930_ACTIVOS_FINANCIEROS_VENTA", 261124093,
        "Activos financieros: conciliación de compras y ventas con el banco y plantilla de subcuentas del tipo «Inversión financiera».",
        "activos/activos-financieros",
        new AnadirColumna("OperacionesInversion", "LineaN43", "LONG"),
        new AnadirColumna("OperacionesInversion", "ImporteAntesConciliar", "CURRENCY"),
        new CrearIndice("OperacionesInversion", "IX_OperacionesInversion_LineaN43", "CREATE INDEX IX_OperacionesInversion_LineaN43 ON OperacionesInversion (LineaN43)"),
        new PasoPersonalizado(
            "Plantilla de subcuentas del tipo «Inversión financiera» (la del ERP) si no tiene ninguna línea",
            "INSERT INTO TiposSubcuentaActivo y PlantillasSubcuentasActivo: 54001 rol ACTIVO; 66901, 76001, 76301, 66301, 47301, 76600 y 66600 rol LIBRE",
            async (s, _) =>
            {
                var tipo = await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = ?", TipoInversion);
                if (tipo is null or DBNull) return true;
                return Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PlantillasSubcuentasActivo WHERE IdTipoActivo = ?", tipo), CultureInfo.InvariantCulture) > 0;
            },
            async (s, _) =>
            {
                var tipo = await s.EscalarAsync("SELECT IdTipoActivo FROM TiposActivo WHERE Codigo = ?", TipoInversion);
                if (tipo is null or DBNull) return;
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PlantillasSubcuentasActivo WHERE IdTipoActivo = ?", tipo), CultureInfo.InvariantCulture) > 0) return;
                short orden = 1;
                foreach (var (nombre, descripcion, rol, prefijo) in PlantillaInversion)
                {
                    var id = await s.EscalarAsync("SELECT IdTipoSubcuentaActivo FROM TiposSubcuentaActivo WHERE Nombre = ?", nombre);
                    if (id is null or DBNull)
                    {
                        await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES (?, ?, ?, True)", nombre, descripcion, rol);
                        id = await s.UltimoIdAsync();
                    }
                    await s.EjecutarAsync("INSERT INTO PlantillasSubcuentasActivo (IdTipoActivo, IdTipoSubcuentaActivo, Prefijo, Orden, EstaActiva) VALUES (?, ?, ?, ?, True)",
                        tipo, id, prefijo, orden++);
                }
            }))
    {
        Descripciones =
        [
            new("OperacionesInversion", "LineaN43", "Movimiento bancario (LineasExtractoBancario) con que se concilió la compra o la venta desde Líneas N43; nulo si no está conciliada."),
            new("OperacionesInversion", "ImporteAntesConciliar", "Importe que tenía la operación antes de ponerle el del banco al conciliarla; se recupera al deshacer el asiento de la línea."),
        ],
    };

    /// <summary>
    /// Líneas de la plantilla del tipo «Inversión financiera» (la del ERP, propietario 2026-11-24 09:31): nombre del tipo de subcuenta, descripción, rol y
    /// prefijo. «PÉRDIDA POR VENA» del ERP se corrige a «PÉRDIDA POR VENTA».
    /// </summary>
    public static IReadOnlyList<(string Nombre, string Descripcion, string Rol, string Prefijo)> PlantillaInversion { get; } =
    [
        ("INVERSIÓN FINANCIERA [TOKEN]", "Inversión en fondos de inversión", "ACTIVO", "54001"),
        ("GASTOS FINANCIEROS [TOKEN]", "Gastos derivados por la compra y gestión de fondos de inversión", "LIBRE", "66901"),
        ("DIVIDENDOS INVERSION [TOKEN]", "Ingresos obtenidos por dividendos de una inversión financiera", "LIBRE", "76001"),
        ("INCREMENTO VALOR [TOKEN]", "Beneficio de cartera de negociación", "LIBRE", "76301"),
        ("DECREMENTO VALOR [TOKEN]", "Pérdida valor de cartera de negociación", "LIBRE", "66301"),
        ("RETENCION [TOKEN]", "Retenciones practicadas", "LIBRE", "47301"),
        ("BENEFICIOS POR VENTA [TOKEN]", "Beneficios recogidos al amortizar una inversión en bolsa", "LIBRE", "76600"),
        ("PÉRDIDA POR VENTA [TOKEN]", "Pérdidas generadas al amortizar una inversión en bolsa", "LIBRE", "66600"),
    ];
}
