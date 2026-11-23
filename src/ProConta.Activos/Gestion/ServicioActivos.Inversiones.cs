using System.Globalization;
using ProConta.Activos.Financieros;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Gestion;

/// <summary>
/// Activos financieros (activos/activos-financieros 2026-11-23 20:32 y 20:38): el instrumento (InstrumentosFinancieros, uno por ISIN y común a todas las
/// sociedades), su enlace con el activo (ActivosInversiones, con las participaciones) y las compras (OperacionesInversion, tipo 1). Ninguna operación graba
/// asientos. El coste del activo es la suma del coste de sus lotes y su fecha de compra la del primero.
/// </summary>
public sealed partial class ServicioActivos
{
    /// <summary>El activo de la sociedad que ya tiene ese ISIN, con su código; null si no hay.</summary>
    public Task<(int IdActivo, string Codigo)?> ActivoConIsinAsync(string idSociedad, string isin) => _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync<(int, string)?>(
        "SELECT a.IdActivo, a.Codigo FROM (ActivosInversiones AS ai INNER JOIN Activos AS a ON ai.IdActivo = a.IdActivo) " +
        "INNER JOIN InstrumentosFinancieros AS i ON ai.IdInstrumentoFinanciero = i.IdInstrumentoFinanciero WHERE a.IdSociedad = ? AND i.Isin = ?",
        f => (f.Entero("IdActivo"), (f.Texto("Codigo") ?? "").Trim()), idSociedad, Isin.Normalizar(isin)));

    /// <summary>
    /// Un activo de tipo «Inversión financiera» se da de alta con su instrumento y su primera compra; los demás tipos, sin ellos. El ISIN debe ser válido
    /// y no estar ya en otro activo de la sociedad (para comprar más se usa «Nueva compra»). Null si todo está bien.
    /// </summary>
    private async Task<Error?> ValidarAltaInversionAsync(ContextoSociedadEjercicio ctx, AltaActivo alta, DatosActivo d, string registro)
    {
        var esInversion = TiposActivoPgc.EsInversion(d.CodigoTipo);
        if (!esInversion)
            return alta.Instrumento is null && alta.Compra is null ? null
                : new Error("Inversiones.TipoNoFinanciero", "Solo los activos de tipo «Inversión financiera» llevan ISIN y compras.", registro, null, "Elige ese tipo o quita el ISIN.");
        if (d.EsAjeno)
            return new Error("Inversiones.Ajeno", "Un activo financiero no puede ser ajeno a la sociedad: sus compras son de la sociedad.", registro, null, "Desmarca «No es de la sociedad».");
        if (alta.Instrumento is not { } i || alta.Compra is not { } c)
            return new Error("Inversiones.FaltaIsin", "Falta el ISIN o la compra del activo financiero.", registro, null, "Escribe el ISIN, busca sus datos y escribe la compra.");
        if (Isin.Error(i.Isin) is { } errorIsin) return new Error("Inversiones.IsinNoValido", errorIsin, registro);
        if (string.IsNullOrWhiteSpace(i.Nombre)) return new Error("Inversiones.SinNombre", "Falta el nombre del instrumento.", registro, null, "Escríbelo o búscalo por su ISIN.");
        if (c.Validar(DateTime.Today) is { Count: > 0 } errores) return new Error("Inversiones.DatoNoValido", string.Join(" ", errores), registro);
        if (await ActivoConIsinAsync(ctx.IdSociedad, i.Isin) is { } otro)
            return new Error("Inversiones.IsinRepetido", $"El ISIN {Isin.Normalizar(i.Isin)} ya es el del activo {otro.Codigo}.", registro, null,
                $"Para sumarle participaciones, cierra el alta y usa «Nueva compra…» en el menú contextual del activo {otro.Codigo}.");
        return null;
    }

    /// <summary>El instrumento y las operaciones de un activo financiero; null si el activo no lo es.</summary>
    public Task<InversionDeActivo?> InversionAsync(int idActivo) => _bd.EjecutarAsync(s => InversionAsync(s, idActivo));

    private static async Task<InversionDeActivo?> InversionAsync(ISesionDatos s, int idActivo)
    {
        var i = await s.ConsultarPrimeroAsync(
            "SELECT i.IdInstrumentoFinanciero, i.Isin, i.Nombre, i.Ticker, i.TickerEodhd, i.Bolsa, i.MonedaCotizacion, i.TipoInstrumento, i.OrigenDatos, ai.ParticipacionesActuales " +
            "FROM ActivosInversiones AS ai INNER JOIN InstrumentosFinancieros AS i ON ai.IdInstrumentoFinanciero = i.IdInstrumentoFinanciero WHERE ai.IdActivo = ?",
            f => (Id: f.Entero("IdInstrumentoFinanciero"), Participaciones: f.DecimalNulo("ParticipacionesActuales") ?? 0m,
                Datos: new DatosInstrumento((f.Texto("Isin") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), Vacio(f.Texto("Ticker")), Vacio(f.Texto("TickerEodhd")),
                    Vacio(f.Texto("Bolsa")), (f.Texto("MonedaCotizacion") ?? "EUR").Trim(), (f.Texto("TipoInstrumento") ?? "").Trim(), Origen(f.Texto("OrigenDatos")))),
            idActivo);
        if (i.Datos is null) return null;
        var ops = await s.ConsultarAsync(
            "SELECT IdOperacionInversion, Tipo, FechaOperacion, Participaciones, PrecioUnitario, Moneda, TipoCambioEuro, ImporteTotalEur, Notas FROM OperacionesInversion " +
            "WHERE IdActivo = ? ORDER BY FechaOperacion, IdOperacionInversion",
            f => new OperacionDeInversion(f.Entero("IdOperacionInversion"), Convert.ToInt32(f.Valor("Tipo"), CultureInfo.InvariantCulture), f.Fecha("FechaOperacion").Date,
                f.Decimal("Participaciones"), f.Decimal("PrecioUnitario"), (f.Texto("Moneda") ?? "EUR").Trim(), f.Decimal("TipoCambioEuro"), f.Decimal("ImporteTotalEur"),
                Vacio(f.Texto("Notas"))), idActivo);
        return new InversionDeActivo(idActivo, i.Id, i.Datos, i.Participaciones, ops);
    }

    private static FuenteCotizacion Origen(string? texto) => (texto ?? "").Trim().ToUpperInvariant() switch
    {
        "EODHD" => FuenteCotizacion.Eodhd,
        "MANUAL" => FuenteCotizacion.Manual,
        _ => FuenteCotizacion.Yahoo,
    };

    /// <summary>
    /// Dentro del alta: el instrumento (lo reutiliza si ya existe por ISIN, poniéndole al día nombre, símbolos y bolsa), su enlace con el activo y la
    /// primera compra.
    /// </summary>
    private static async Task AltaInversionAsync(ISesionDatos s, int idActivo, DatosInstrumento instrumento, CompraInversion compra)
    {
        var isin = Isin.Normalizar(instrumento.Isin);
        var idInstrumento = await s.EscalarAsync("SELECT IdInstrumentoFinanciero FROM InstrumentosFinancieros WHERE Isin = ?", isin) is { } v and not DBNull
            ? Convert.ToInt32(v, CultureInfo.InvariantCulture) : (int?)null;
        var nombre = Recortar(instrumento.Nombre, 255);
        var origen = instrumento.Origen.ToString().ToUpperInvariant();
        if (idInstrumento is int id)
        {
            await s.EjecutarAsync("UPDATE InstrumentosFinancieros SET Nombre = ?, Ticker = ?, TickerEodhd = ?, Bolsa = ?, MonedaCotizacion = ?, TipoInstrumento = ?, OrigenDatos = ?, " +
                "EstaActivo = True WHERE IdInstrumentoFinanciero = ?",
                nombre, Recortar(instrumento.TickerYahoo, 30), Recortar(instrumento.TickerEodhd, 40), Recortar(instrumento.Bolsa, 60), Moneda(instrumento.Moneda),
                Recortar(instrumento.TipoInstrumento, 40) ?? "OTRO", origen, id);
        }
        else
        {
            await s.EjecutarAsync("INSERT INTO InstrumentosFinancieros (Nombre, Isin, Ticker, TipoInstrumento, MonedaCotizacion, UrlInformacion, EsCandidato, EstaActivo, " +
                "TickerEodhd, Bolsa, OrigenDatos) VALUES (?, ?, ?, ?, ?, ?, False, True, ?, ?, ?)",
                nombre, isin, Recortar(instrumento.TickerYahoo, 30), Recortar(instrumento.TipoInstrumento, 40) ?? "OTRO", Moneda(instrumento.Moneda),
                instrumento.TickerYahoo is { Length: > 0 } t ? Recortar(UrlsCotizaciones.FichaYahoo(t), 255) : null,
                Recortar(instrumento.TickerEodhd, 40), Recortar(instrumento.Bolsa, 60), origen);
            idInstrumento = await s.UltimoIdAsync();
        }
        await s.EjecutarAsync("INSERT INTO ActivosInversiones (IdActivo, IdInstrumentoFinanciero, ParticipacionesActuales) VALUES (?, ?, ?)", idActivo, idInstrumento, 0m);
        await InsertarCompraAsync(s, idActivo, idInstrumento.Value, compra);
        await RecalcularInversionAsync(s, idActivo, cambiarVersion: false);
    }

    /// <summary>
    /// «Nueva compra» de un activo financiero (2026-11-23 20:32): una operación de compra más, sin pedir el ISIN; suma participaciones y coste al activo.
    /// No graba asientos.
    /// </summary>
    public async Task<Resultado<int>> NuevaCompraAsync(ContextoSociedadEjercicio ctx, int idActivo, CompraInversion compra)
    {
        var registro = $"Activo {idActivo}";
        if (!await PuedeAsync()) return Resultado<int>.Fallo(SinPermiso(registro));
        if (compra.Validar(DateTime.Today) is { Count: > 0 } errores)
            return Resultado<int>.Fallo(errores.Select(e => new Error("Inversiones.DatoNoValido", e, registro)));
        return await _bd.EnTransaccionAsync(async s =>
        {
            var activo = await s.ConsultarPrimeroAsync("SELECT a.Codigo, a.FechaBaja, ai.IdInstrumentoFinanciero FROM Activos AS a INNER JOIN ActivosInversiones AS ai ON a.IdActivo = ai.IdActivo " +
                "WHERE a.IdActivo = ? AND a.IdSociedad = ?", f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Baja: f.FechaNula("FechaBaja"), Instrumento: f.Entero("IdInstrumentoFinanciero")),
                idActivo, ctx.IdSociedad);
            if (activo.Codigo is null)
                return Resultado<int>.Fallo(new Error("Inversiones.NoEsFinanciero", "Este activo no es un activo financiero (no tiene ISIN): no se le pueden añadir compras.", registro, null,
                    "Elige un activo de tipo «Inversión financiera»."));
            if (activo.Baja is not null)
                return Resultado<int>.Fallo(new Error("Inversiones.DeBaja", $"El activo {activo.Codigo} está de baja: no se le pueden añadir compras.", $"Activo {activo.Codigo}"));
            var id = await InsertarCompraAsync(s, idActivo, activo.Instrumento, compra);
            await RecalcularInversionAsync(s, idActivo, cambiarVersion: true);
            return Resultado<int>.Correcto(id);
        });
    }

    /// <summary>
    /// Cambia el coste total de un lote con el importe que confirma el bróker (2026-11-23 20:32, B) y recalcula el coste del activo. No toca el precio
    /// del proveedor guardado en la operación.
    /// </summary>
    public async Task<Resultado<bool>> CambiarCosteLoteAsync(ContextoSociedadEjercicio ctx, int idOperacion, decimal importeTotalEur)
    {
        var registro = $"Operación {idOperacion}";
        if (!await PuedeAsync()) return Resultado<bool>.Fallo(SinPermiso(registro));
        if (importeTotalEur <= 0m)
            return Resultado<bool>.Fallo(new Error("Inversiones.CosteNoValido", "El coste total del lote debe ser mayor que cero.", registro, null, "Escribe el importe que te ha confirmado el bróker."));
        if (decimal.Round(importeTotalEur, 2) != importeTotalEur)
            return Resultado<bool>.Fallo(new Error("Inversiones.CosteNoValido", "El coste total del lote va en euros con dos decimales como mucho.", registro));
        return await _bd.EnTransaccionAsync(async s =>
        {
            var idActivo = await s.EscalarAsync("SELECT o.IdActivo FROM OperacionesInversion AS o INNER JOIN Activos AS a ON o.IdActivo = a.IdActivo " +
                "WHERE o.IdOperacionInversion = ? AND a.IdSociedad = ?", idOperacion, ctx.IdSociedad);
            if (idActivo is null or DBNull)
                return Resultado<bool>.Fallo(new Error("Inversiones.NoExiste", "Esa compra ya no existe.", registro, null, "Pulsa Actualizar (F5) en la ficha."));
            await s.EjecutarAsync("UPDATE OperacionesInversion SET ImporteTotalEur = ? WHERE IdOperacionInversion = ?", importeTotalEur, idOperacion);
            await RecalcularInversionAsync(s, Convert.ToInt32(idActivo, CultureInfo.InvariantCulture), cambiarVersion: true);
            return Resultado<bool>.Correcto(true);
        });
    }

    private static async Task<int> InsertarCompraAsync(ISesionDatos s, int idActivo, int idInstrumento, CompraInversion c)
    {
        var notas = c.Cotizacion is { } p
            ? $"Precio de {p.Origen} del {p.FechaCotizacion:dd/MM/yyyy}" + (p.Moneda == "EUR" ? "" : $"; cambio del BCE del {p.FechaCambio:dd/MM/yyyy}") + "."
            : "Precio escrito a mano.";
        await s.EjecutarAsync("INSERT INTO OperacionesInversion (IdActivo, Tipo, FechaOperacion, Participaciones, PrecioUnitario, Moneda, TipoCambioEuro, GastosEur, ImporteTotalEur, Notas) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idActivo, (byte)OperacionDeInversion.TipoCompra, c.Fecha.Date, c.Participaciones, c.PrecioUnitario, Moneda(c.Moneda), c.UnidadesPorEuro, 0m, c.ImporteTotalEur, notas);
        var id = await s.UltimoIdAsync();
        if (c.Cotizacion is { } q) await GuardarCotizacionAsync(s, idInstrumento, q);
        return id;
    }

    /// <summary>Guarda en el histórico la cotización que se usó (si ya hay una de ese día, la deja).</summary>
    private static async Task GuardarCotizacionAsync(ISesionDatos s, int idInstrumento, PrecioEnEuros p)
    {
        var hay = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM CotizacionesInstrumentoFinanciero WHERE IdInstrumentoFinanciero = ? AND FechaCotizacion = ?",
            idInstrumento, p.FechaCotizacion.Date), CultureInfo.InvariantCulture) > 0;
        if (!hay)
            await s.EjecutarAsync("INSERT INTO CotizacionesInstrumentoFinanciero (IdInstrumentoFinanciero, FechaCotizacion, Precio, Moneda, Origen, FechaHoraImportacion) VALUES (?, ?, ?, ?, ?, ?)",
                idInstrumento, p.FechaCotizacion.Date, p.Precio, Moneda(p.Moneda), Recortar(p.Origen, 50), DateTime.Now);
    }

    /// <summary>
    /// Participaciones = compras − ventas; coste del activo = suma del coste de las compras (también su valor contable) y fecha de compra = la de la
    /// primera. Con <paramref name="cambiarVersion"/>, sube la versión del activo para que una ficha abierta sepa que ha cambiado.
    /// </summary>
    private static async Task RecalcularInversionAsync(ISesionDatos s, int idActivo, bool cambiarVersion)
    {
        var ops = await s.ConsultarAsync("SELECT Tipo, FechaOperacion, Participaciones, ImporteTotalEur FROM OperacionesInversion WHERE IdActivo = ?",
            f => (Tipo: Convert.ToInt32(f.Valor("Tipo"), CultureInfo.InvariantCulture), Fecha: f.Fecha("FechaOperacion").Date, Participaciones: f.Decimal("Participaciones"),
                Importe: f.Decimal("ImporteTotalEur")), idActivo);
        var compras = ops.Where(o => o.Tipo == OperacionDeInversion.TipoCompra).ToList();
        var participaciones = compras.Sum(o => o.Participaciones) - ops.Where(o => o.Tipo == OperacionDeInversion.TipoVenta).Sum(o => o.Participaciones);
        var coste = compras.Sum(o => o.Importe);
        DateTime? primera = compras.Count == 0 ? null : compras.Min(o => o.Fecha);
        await s.EjecutarAsync("UPDATE ActivosInversiones SET ParticipacionesActuales = ? WHERE IdActivo = ?", participaciones, idActivo);
        await s.EjecutarAsync("UPDATE Activos SET CosteAdquisicionActual = ?, ValorContable = ?, FechaAdquisicion = ?" + (cambiarVersion ? ", Version = Version + 1" : "") +
            " WHERE IdActivo = ?", coste, coste, primera, idActivo);
    }

    private static string Moneda(string moneda) => ServicioCotizaciones.AMonedaIso(0m, moneda).Moneda is { Length: 3 } m ? m : moneda.Trim().ToUpperInvariant()[..Math.Min(3, moneda.Trim().Length)];

    private static string? Recortar(string? texto, int maximo)
    {
        var t = texto?.Trim();
        return string.IsNullOrEmpty(t) ? null : t.Length <= maximo ? t : t[..maximo];
    }
}
