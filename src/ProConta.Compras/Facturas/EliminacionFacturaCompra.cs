using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Compras.Facturas;

/// <summary>Cómo se puede eliminar una factura de compra (compras/pantallas 2026-11-25).</summary>
public enum ClaseEliminacion
{
    /// <summary>No ha llegado a la contabilidad: se elimina sin más.</summary>
    Directa,
    /// <summary>Exportada por ProContaVF en el trimestre activo: se deshace la exportación (se borra el asiento y su libro de IVA) y se elimina.</summary>
    DeshacerAntes,
    /// <summary>No se puede eliminar; <see cref="ComprobacionEliminacion.Motivo"/> dice por qué y <see cref="ComprobacionEliminacion.QueHacer"/>, qué hacer.</summary>
    Imposible
}

/// <summary>Si una factura de compra se puede eliminar y, si no, por qué y qué hacer.</summary>
public sealed record ComprobacionEliminacion(int IdRegistro, string Rotulo, ClaseEliminacion Clase, string? Motivo = null, string? QueHacer = null)
{
    /// <summary>La explicación para el operador: «Factura de compra X: motivo. Qué hacer».</summary>
    public string Texto => Motivo is null ? Rotulo : $"{Rotulo}: {Motivo}{(QueHacer is null ? "" : " " + QueHacer)}";
}

/// <summary>Lo que ha hecho eliminar una factura de compra.</summary>
/// <param name="RutaPdf">El PDF de la factura (vacío si no tenía).</param>
/// <param name="PdfEnPapelera">El PDF ha ido a la papelera de Windows.</param>
/// <param name="AvisoPdf">Por qué el PDF no ha ido a la papelera, si no ha ido.</param>
public sealed record FacturaCompraEliminada(int IdRegistro, string Rotulo, string RutaPdf, bool PdfEnPapelera, string? AvisoPdf);

/// <summary>
/// Reglas para eliminar una factura de compra registrada por error (compras/pantallas 2026-11-25, Q1 a): solo si no ha dejado rastro en la
/// contabilidad ni la usa otra cosa. Exportada por ProContaVF en el trimestre activo, se deshace antes la exportación; con el asiento en un
/// trimestre cerrado (ya declarado), en el ERP o con un contra-asiento, se queda.
/// </summary>
internal static class EliminacionFacturaCompra
{
    internal static async Task<ComprobacionEliminacion?> ComprobarAsync(ISesionDatos s, int idRegistro, DateTime hoy)
    {
        var f = await RepositorioFacturasCompra.CargarAsync(s, idRegistro);
        if (f is null) return null;
        var rotulo = f.Rotulo;
        ComprobacionEliminacion No(string motivo, string queHacer) => new(idRegistro, rotulo, ClaseEliminacion.Imposible, motivo, queHacer);

        var linea = await s.EscalarAsync("SELECT LineaN43 FROM FacturasCompra WHERE IdRegistro = ?", idRegistro);
        if (linea is not null and not DBNull)
            return No($"está conciliada con el movimiento {Convert.ToInt32(linea, CultureInfo.InvariantCulture)} del banco.",
                "Deshaz antes la conciliación en Bancos › Líneas N43 y vuelve a eliminarla.");

        var activos = await s.ConsultarAsync("SELECT Codigo, Nombre FROM Activos WHERE IdFacturaCompra = ?",
            x => $"{x.Texto("Codigo")?.Trim()} {x.Texto("Nombre")?.Trim()}".Trim(), idRegistro);
        if (activos.Count > 0)
            return No($"es la factura de compra del activo {string.Join(", ", activos)}.",
                "Si de verdad sobra, quítala antes de la ficha del activo (Activos › ficha › Facturas).");

        var rectificativas = await s.ConsultarAsync("SELECT NumeroFactura FROM FacturasCompra WHERE IdRegistroFacturaRectificada = ?",
            x => x.Texto("NumeroFactura")?.Trim() ?? "(sin número)", idRegistro);
        if (rectificativas.Count > 0)
            return No($"es la factura que rectifica {string.Join(", ", rectificativas)}.",
                "Elimina antes la rectificativa, o quítale en su ficha la factura rectificada.");

        var estado = await ContabilizadorCompras.EstadoAsync(s, idRegistro);
        if (estado.DelErp)
            return No($"se contabilizó en el ERP{(estado.IdAsiento is int erp ? $" (asiento {erp})" : "")}, así que ya está en la contabilidad y en el libro de IVA.",
                "Si sobra, usa «Deshacer exportación», que hace un contra-asiento; la factura se queda como constancia.");
        if (estado.IdAsientoAnulado is int anulado)
            return No($"tiene un contra-asiento del asiento {anulado}, que es de un trimestre cerrado: la contabilidad ya la recoge.",
                "Déjala como está; si aún está exportada, «Deshacer exportación» la anula en el trimestre activo.");
        if (estado.IdAsiento is int asiento)
        {
            var config = await ContabilizadorCompras.ConfiguracionAsync(s, f.Ejercicio.IdSociedadEjercicio);
            if (config is null || ContabilizadorCompras.LoteCerrado(estado, config, f.Ejercicio.Ejercicio, hoy))
                return No($"su asiento {asiento} es de un trimestre cerrado, que ya se ha declarado.",
                    "Usa «Deshacer exportación», que hace un contra-asiento en el trimestre activo; la factura se queda como constancia.");
            return new ComprobacionEliminacion(idRegistro, rotulo, ClaseEliminacion.DeshacerAntes);
        }
        var otros = await AsientosDeLaFacturaAsync(s, idRegistro);
        if (otros.Count > 0)
            return No($"quedan asientos suyos en la contabilidad ({string.Join(", ", otros)}), por ejemplo la compensación de una comisión.",
                "Revísalos en el Diario; mientras existan, la factura se queda.");
        return new ComprobacionEliminacion(idRegistro, rotulo, ClaseEliminacion.Directa);
    }

    /// <summary>Asientos de cualquier lote de la factura (su exportación, sus contra-asientos y la compensación de la comisión).</summary>
    internal static async Task<IReadOnlyList<int>> AsientosDeLaFacturaAsync(ISesionDatos s, int idRegistro)
    {
        var clave = idRegistro.ToString(CultureInfo.InvariantCulture);
        return await s.ConsultarAsync(
            "SELECT IdAsiento FROM Asientos WHERE EsAutomatico = True AND MotorPropietario = ? AND (ClaveEjecucion = ? OR ClaveEjecucion LIKE ?) ORDER BY IdAsiento",
            x => x.Entero("IdAsiento"), AsientoCompra.MotorPropietario, clave, clave + ".%");
    }

    /// <summary>Borra la factura, su desglose y sus vínculos del gestor documental; el correo del que vino deja de apuntarle.</summary>
    internal static async Task BorrarAsync(ISesionDatos s, int idRegistro)
    {
        await s.EjecutarAsync("UPDATE AdjuntosRecibidosCorreo SET IdFacturaCompra = NULL WHERE IdFacturaCompra = ?", idRegistro);
        await s.EjecutarAsync("DELETE FROM DocumentosVinculos WHERE TipoEntidad = ? AND IdEntidad = ?", ProConta.Documentos.TiposEntidad.FacturaCompra, idRegistro);
        await s.EjecutarAsync("DELETE FROM DesglosesFacturaCompra WHERE IdRegistro = ?", idRegistro);
        await s.EjecutarAsync("DELETE FROM FacturasCompra WHERE IdRegistro = ?", idRegistro);
    }
}
