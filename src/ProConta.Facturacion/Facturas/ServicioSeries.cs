using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Facturacion.Facturas;

/// <summary>Uso de una serie: facturas numeradas y borradores que la tienen.</summary>
public sealed record UsoSerie(int Numeradas, int Borradores)
{
    public bool TieneFacturas => Numeradas + Borradores > 0;
}

/// <summary>
/// Servicio tipado de las series (facturacion/factura-venta, RF-MAN-03): una serie con facturas no cambia de prefijo ni
/// de clase, ni baja su rango por debajo del último número; hay una sola serie de rectificativas y una sola
/// predeterminada por ejercicio; el último número solo lo cambia la validación de facturas.
/// </summary>
public sealed class ServicioSeries(IBaseDatos bd)
{
    public Task<IReadOnlyList<Serie>> ListarAsync(int idSociedadEjercicio) => bd.EjecutarAsync(s => RepositorioFacturas.SeriesAsync(s, idSociedadEjercicio));

    public Task<UsoSerie> UsoAsync(int idSerie) =>
        bd.EjecutarAsync(async s => new UsoSerie(
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Facturas WHERE IdSerie = ? AND NumeroFactura IS NOT NULL", idSerie), CultureInfo.InvariantCulture),
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Facturas WHERE IdSerie = ? AND NumeroFactura IS NULL", idSerie), CultureInfo.InvariantCulture)));

    /// <summary>
    /// Alta (IdSerie 0) o modificación. Marcar una serie como predeterminada quita la marca a las demás normales del
    /// ejercicio, y así lo dice el resultado.
    /// </summary>
    public async Task<Resultado<(Serie Serie, string? Aviso)>> GuardarAsync(Serie serie)
    {
        var registro = serie.IdSerie > 0 ? $"Serie {serie.Prefijo.Trim()}" : "Serie nueva";
        var errores = new List<Error>();
        var prefijo = serie.Prefijo?.Trim() ?? "";
        if (prefijo.Length == 0) errores.Add(new("Series.SinPrefijo", "Falta el prefijo de la serie.", registro, null, "Escribe el prefijo, por ejemplo F2026."));
        else if (prefijo.Length > 20) errores.Add(new("Series.PrefijoLargo", "El prefijo admite hasta 20 caracteres.", registro));
        if (string.IsNullOrWhiteSpace(serie.Descripcion)) errores.Add(new("Series.SinDescripcion", "Falta la descripción de la serie.", registro));
        else if (serie.Descripcion.Trim().Length > 100) errores.Add(new("Series.DescripcionLarga", "La descripción admite hasta 100 caracteres.", registro));
        if (serie.RangoDesde < 1) errores.Add(new("Series.RangoNoValido", "El rango empieza en 1 o más.", registro));
        if (serie.RangoHasta < serie.RangoDesde) errores.Add(new("Series.RangoAlReves", $"El rango termina ({serie.RangoHasta}) antes de empezar ({serie.RangoDesde}).", registro));
        if (serie.EsRectificativa && serie.EsPredeterminada) errores.Add(new("Series.RectificativaPredeterminada", "La serie de rectificativas no puede ser la predeterminada: esa es la de las facturas normales.", registro));
        if (errores.Count > 0) return Resultado<(Serie, string?)>.Fallo(errores);

        try
        {
            return await bd.EnTransaccionAsync(async s =>
            {
                string? aviso = null;
                var otras = (await RepositorioFacturas.SeriesAsync(s, serie.IdSociedadEjercicio)).Where(x => x.IdSerie != serie.IdSerie).ToList();
                if (otras.Any(x => x.Prefijo.Trim().Equals(prefijo, StringComparison.OrdinalIgnoreCase)))
                    return Fallo($"Ya hay una serie con el prefijo {prefijo} en este ejercicio.", "Usa otro prefijo.");
                if (serie.EsRectificativa && otras.FirstOrDefault(x => x.EsRectificativa) is { } rect)
                    return Fallo($"El ejercicio ya tiene la serie de rectificativas {rect.Prefijo.Trim()}: solo puede haber una.", "Usa esa serie o quítale la marca antes.");

                if (serie.IdSerie > 0)
                {
                    var actual = await RepositorioFacturas.SerieAsync(s, serie.IdSerie);
                    if (actual is null) return Fallo("La serie ya no existe.", "Actualiza la lista.");
                    var uso = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Facturas WHERE IdSerie = ?", serie.IdSerie), CultureInfo.InvariantCulture);
                    if (uso > 0 && !actual.Prefijo.Trim().Equals(prefijo, StringComparison.Ordinal))
                        return Fallo($"La serie ya tiene {uso} facturas: su prefijo {actual.Prefijo.Trim()} no se puede cambiar.", "Crea una serie nueva con el prefijo que quieres.");
                    if (uso > 0 && actual.EsRectificativa != serie.EsRectificativa)
                        return Fallo("La serie ya tiene facturas: no puede pasar a ser de rectificativas ni dejar de serlo.", "Crea otra serie para el otro uso.");
                    if (actual.UltimoNumero is int ultimo && (serie.RangoHasta < ultimo || serie.RangoDesde > ultimo))
                        return Fallo($"El último número emitido es {ultimo}: el rango debe incluirlo.", $"Deja «Hasta» en {ultimo} o más y «Desde» en {ultimo} o menos.");
                    await s.EjecutarAsync("UPDATE SeriesFacturacion SET Descripcion = ?, Prefijo = ?, RangoDesde = ?, RangoHasta = ?, EsRectificativa = ?, EsPredeterminada = ? WHERE IdSerie = ?",
                        serie.Descripcion.Trim(), prefijo, serie.RangoDesde, serie.RangoHasta, serie.EsRectificativa, serie.EsPredeterminada, serie.IdSerie);
                }
                else
                {
                    await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, Prefijo, RangoDesde, RangoHasta, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        serie.IdSociedadEjercicio, serie.Descripcion.Trim(), prefijo, serie.RangoDesde, serie.RangoHasta, serie.EsRectificativa, serie.EsPredeterminada);
                    serie = serie with { IdSerie = await s.UltimoIdAsync() };
                }
                if (serie.EsPredeterminada && otras.Where(x => x.EsPredeterminada).Select(x => x.Prefijo.Trim()).ToList() is { Count: > 0 } quitadas)
                {
                    await s.EjecutarAsync("UPDATE SeriesFacturacion SET EsPredeterminada = False WHERE IdSociedadEjercicio = ? AND IdSerie <> ?", serie.IdSociedadEjercicio, serie.IdSerie);
                    aviso = $"La serie {string.Join(", ", quitadas)} deja de ser la predeterminada.";
                }
                return Resultado<(Serie, string?)>.Correcto(((await RepositorioFacturas.SerieAsync(s, serie.IdSerie))!, aviso));
            });
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<(Serie, string?)>.Fallo(ex.Error);
        }

        Resultado<(Serie, string?)> Fallo(string mensaje, string accion) =>
            Resultado<(Serie, string?)>.Fallo(new Error("Series.NoValida", mensaje, registro, null, accion));
    }

    /// <summary>Borra una serie sin ninguna factura (ni borradores).</summary>
    public async Task<Resultado> EliminarAsync(int idSerie)
    {
        var uso = await UsoAsync(idSerie);
        if (uso.TieneFacturas)
            return Resultado.Fallo(new Error("Series.EnUso",
                $"No se puede borrar: la serie tiene {uso.Numeradas} facturas numeradas y {uso.Borradores} borradores.", $"Serie {idSerie}",
                "Las facturas apuntan a su serie.", "Déjala sin marcar como predeterminada para que no se proponga."));
        await bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM SeriesFacturacion WHERE IdSerie = ?", idSerie));
        return Resultado.Correcto();
    }
}
