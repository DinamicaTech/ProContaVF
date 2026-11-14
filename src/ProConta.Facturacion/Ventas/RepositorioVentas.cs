using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;

namespace ProConta.Facturacion.Ventas;

/// <summary>Lectura y escritura de VentasPorFacturar, OrigenesVenta y AlojamientosAvaibook dentro de una sesión.</summary>
internal static class RepositorioVentas
{
    private const string Columnas =
        "v.IdVentaPorFacturar, v.IdSociedad, v.IdOrigenVenta, v.IdCliente, v.IdActivo, v.IdConcepto, v.FechaReferencia, v.FechaFin, v.Cantidad, v.Importe, " +
        "v.Comision, v.TipoIva, v.CausaExencion, v.Descripcion, v.Referencia, v.Titular, v.Anulada, v.MotivoAnulacion, v.FacturadaFuera, v.IdFactura, " +
        "v.Fuente, v.IdExterno, v.Huella, v.Incidencia, v.Notas, v.FechaAlta, v.OperadorAlta, v.FechaModificacion, v.Version, " +
        "f.EstadoFactura AS EstadoFacturaEnlazada, f.NumeroFactura AS NumeroFacturaEnlazada";

    private const string Desde = "VentasPorFacturar AS v LEFT JOIN Facturas AS f ON v.IdFactura = f.IdFactura";

    internal static VentaPorFacturar Mapear(IFilaDatos r)
    {
        var v = new VentaPorFacturar
        {
            IdVentaPorFacturar = r.Entero("IdVentaPorFacturar"),
            IdSociedad = (r.Texto("IdSociedad") ?? "").Trim(),
            IdOrigenVenta = r.Entero("IdOrigenVenta"),
            IdCliente = r.EnteroNulo("IdCliente"),
            IdActivo = r.EnteroNulo("IdActivo"),
            IdConcepto = r.EnteroNulo("IdConcepto"),
            FechaReferencia = r.Fecha("FechaReferencia"),
            FechaFin = r.FechaNula("FechaFin"),
            Cantidad = r.Decimal("Cantidad"),
            Importe = r.Decimal("Importe"),
            Comision = r.DecimalNulo("Comision"),
            TipoIva = r.DecimalNulo("TipoIva"),
            CausaExencion = r.Texto("CausaExencion")?.Trim(),
            Descripcion = r.Texto("Descripcion"),
            Referencia = r.Texto("Referencia"),
            Titular = r.Texto("Titular"),
            Anulada = r.Logico("Anulada"),
            MotivoAnulacion = r.Texto("MotivoAnulacion"),
            FacturadaFuera = r.Logico("FacturadaFuera"),
            IdFactura = r.EnteroNulo("IdFactura"),
            Fuente = (r.Texto("Fuente") ?? FuenteVenta.Manual).Trim(),
            IdExterno = r.Texto("IdExterno"),
            Huella = r.Texto("Huella"),
            Incidencia = r.Texto("Incidencia"),
            Notas = r.Texto("Notas"),
            FechaAlta = r.Fecha("FechaAlta"),
            OperadorAlta = r.Texto("OperadorAlta"),
            FechaModificacion = r.FechaNula("FechaModificacion"),
            Version = r.Entero("Version"),
        };
        if (v.IdFactura is int id && r.EnteroNulo("EstadoFacturaEnlazada") is int estado)
        {
            v.EstadoFacturaEnlazada = (EstadoFactura)estado;
            v.RotuloFactura = r.Texto("NumeroFacturaEnlazada") ?? FacturaVenta.RotuloBorrador(id);
        }
        return v;
    }

    public static Task<VentaPorFacturar?> CargarAsync(ISesionDatos s, int id) =>
        s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM {Desde} WHERE v.IdVentaPorFacturar = ?", Mapear, id);

    public static Task<IReadOnlyList<VentaPorFacturar>> ListarAsync(ISesionDatos s, string idSociedad, DateTime? desde = null, DateTime? hasta = null) =>
        (desde, hasta) switch
        {
            (DateTime d, DateTime h) => s.ConsultarAsync($"SELECT {Columnas} FROM {Desde} WHERE v.IdSociedad = ? AND v.FechaReferencia >= ? AND v.FechaReferencia < ? ORDER BY v.FechaReferencia DESC, v.IdVentaPorFacturar DESC",
                Mapear, idSociedad, d.Date, h.Date.AddDays(1)),
            _ => s.ConsultarAsync($"SELECT {Columnas} FROM {Desde} WHERE v.IdSociedad = ? ORDER BY v.FechaReferencia DESC, v.IdVentaPorFacturar DESC", Mapear, idSociedad),
        };

    /// <summary>Ventas pendientes de facturar (sin factura, no anuladas ni facturadas fuera) con fecha de referencia hasta el día indicado.</summary>
    public static Task<IReadOnlyList<VentaPorFacturar>> PendientesAsync(ISesionDatos s, string idSociedad, DateTime hasta) =>
        s.ConsultarAsync($"SELECT {Columnas} FROM {Desde} WHERE v.IdSociedad = ? AND v.IdFactura IS NULL AND v.Anulada = False AND v.FacturadaFuera = False " +
                         "AND v.FechaReferencia < ? ORDER BY v.FechaReferencia, v.IdVentaPorFacturar", Mapear, idSociedad, hasta.Date.AddDays(1));

    public static Task<IReadOnlyList<VentaPorFacturar>> PorExternoAsync(ISesionDatos s, string idSociedad, string fuente) =>
        s.ConsultarAsync($"SELECT {Columnas} FROM {Desde} WHERE v.IdSociedad = ? AND v.Fuente = ? AND v.IdExterno IS NOT NULL", Mapear, idSociedad, fuente);

    public static async Task<int> InsertarAsync(ISesionDatos s, VentaPorFacturar v)
    {
        await s.EjecutarAsync(
            "INSERT INTO VentasPorFacturar (IdSociedad, IdOrigenVenta, IdCliente, IdActivo, IdConcepto, FechaReferencia, FechaFin, Cantidad, Importe, Comision, TipoIva, CausaExencion, " +
            "Descripcion, Referencia, Titular, Anulada, MotivoAnulacion, FacturadaFuera, IdFactura, Fuente, IdExterno, Huella, Incidencia, Notas, FechaAlta, OperadorAlta, FechaModificacion, Version) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0)",
            v.IdSociedad, v.IdOrigenVenta, v.IdCliente, v.IdActivo, v.IdConcepto, v.FechaReferencia.Date, v.FechaFin?.Date, v.Cantidad, v.Importe, v.Comision, v.TipoIva,
            Vacio(v.CausaExencion), Recortar(v.Descripcion, 255), Recortar(v.Referencia, 60), Recortar(v.Titular, 150), v.Anulada, Recortar(v.MotivoAnulacion, 255), v.FacturadaFuera,
            v.IdFactura, v.Fuente, Recortar(v.IdExterno, 80), v.Huella, Recortar(v.Incidencia, 255), Vacio(v.Notas), v.FechaAlta, Recortar(v.OperadorAlta, 100), v.FechaModificacion);
        return await s.UltimoIdAsync();
    }

    /// <summary>Actualiza los datos de la venta si nadie la ha cambiado desde que se leyó (Version); si no, ConflictoConcurrenciaException.</summary>
    public static Task ActualizarAsync(ISesionDatos s, VentaPorFacturar v) =>
        s.ActualizarConVersionAsync(
            "UPDATE VentasPorFacturar SET IdOrigenVenta = ?, IdCliente = ?, IdActivo = ?, IdConcepto = ?, FechaReferencia = ?, FechaFin = ?, Cantidad = ?, Importe = ?, Comision = ?, " +
            "TipoIva = ?, CausaExencion = ?, Descripcion = ?, Referencia = ?, Titular = ?, Anulada = ?, MotivoAnulacion = ?, Huella = ?, Incidencia = ?, Notas = ?, " +
            "FechaModificacion = ?, Version = Version + 1 WHERE IdVentaPorFacturar = ? AND Version = ?",
            v.Rotulo,
            v.IdOrigenVenta, v.IdCliente, v.IdActivo, v.IdConcepto, v.FechaReferencia.Date, v.FechaFin?.Date, v.Cantidad, v.Importe, v.Comision, v.TipoIva,
            Vacio(v.CausaExencion), Recortar(v.Descripcion, 255), Recortar(v.Referencia, 60), Recortar(v.Titular, 150), v.Anulada, Recortar(v.MotivoAnulacion, 255),
            v.Huella, Recortar(v.Incidencia, 255), Vacio(v.Notas), v.FechaModificacion, v.IdVentaPorFacturar, v.Version);

    /// <summary>Solo anota una incidencia de la fuente (venta ya facturada que ha cambiado), sin tocar sus datos.</summary>
    public static Task AnotarIncidenciaAsync(ISesionDatos s, int id, string? incidencia) =>
        s.EjecutarAsync("UPDATE VentasPorFacturar SET Incidencia = ? WHERE IdVentaPorFacturar = ?", Recortar(incidencia, 255), id);

    /// <summary>Enlaza la venta con su factura solo si sigue pendiente; devuelve si la ha enlazado.</summary>
    public static async Task<bool> EnlazarFacturaAsync(ISesionDatos s, int id, int version, int idFactura, DateTime ahora) =>
        await s.EjecutarAsync("UPDATE VentasPorFacturar SET IdFactura = ?, FechaModificacion = ?, Version = Version + 1 " +
                              "WHERE IdVentaPorFacturar = ? AND Version = ? AND IdFactura IS NULL AND Anulada = False AND FacturadaFuera = False", idFactura, ahora, id, version) == 1;

    /// <summary>Al borrar un borrador, sus ventas vuelven a pendientes (en la misma transacción que el borrado).</summary>
    public static Task<int> LiberarDeFacturaAsync(ISesionDatos s, int idFactura) =>
        s.EjecutarAsync("UPDATE VentasPorFacturar SET IdFactura = NULL, Version = Version + 1 WHERE IdFactura = ?", idFactura);

    public static Task BorrarAsync(ISesionDatos s, int id) => s.EjecutarAsync("DELETE FROM VentasPorFacturar WHERE IdVentaPorFacturar = ?", id);

    // ---------- orígenes y alojamientos ----------

    internal static OrigenVenta MapearOrigen(IFilaDatos r) => new(r.Entero("IdOrigenVenta"), (r.Texto("IdSociedad") ?? "").Trim(), (r.Texto("Codigo") ?? "").Trim(),
        r.Texto("Nombre") ?? "", r.EnteroNulo("IdCliente"), r.Texto("TextosImportacion"), r.Logico("EsDirecta"), r.Logico("Activo"));

    public static Task<IReadOnlyList<OrigenVenta>> OrigenesAsync(ISesionDatos s, string idSociedad) =>
        s.ConsultarAsync("SELECT IdOrigenVenta, IdSociedad, Codigo, Nombre, IdCliente, TextosImportacion, EsDirecta, Activo FROM OrigenesVenta WHERE IdSociedad = ? ORDER BY Nombre",
            MapearOrigen, idSociedad);

    public static async Task<int> InsertarOrigenAsync(ISesionDatos s, OrigenVenta o)
    {
        await s.EjecutarAsync("INSERT INTO OrigenesVenta (IdSociedad, Codigo, Nombre, IdCliente, TextosImportacion, EsDirecta, Activo) VALUES (?, ?, ?, ?, ?, ?, ?)",
            o.IdSociedad, o.Codigo, o.Nombre, o.IdCliente, Vacio(o.TextosImportacion), o.EsDirecta, o.Activo);
        return await s.UltimoIdAsync();
    }

    internal static AlojamientoAvaibook MapearAlojamiento(IFilaDatos r) => new(r.Entero("IdAlojamientoAvaibook"), (r.Texto("IdSociedad") ?? "").Trim(),
        (r.Texto("IdExterno") ?? "").Trim(), r.Texto("Nombre"), r.EnteroNulo("IdActivo"), r.EnteroNulo("IdConcepto"), r.Texto("CausaExencion")?.Trim(), r.Logico("Activo"));

    public static Task<IReadOnlyList<AlojamientoAvaibook>> AlojamientosAsync(ISesionDatos s, string idSociedad) =>
        s.ConsultarAsync("SELECT IdAlojamientoAvaibook, IdSociedad, IdExterno, Nombre, IdActivo, IdConcepto, CausaExencion, Activo FROM AlojamientosAvaibook WHERE IdSociedad = ?",
            MapearAlojamiento, idSociedad);

    public static async Task<int> InsertarAlojamientoAsync(ISesionDatos s, AlojamientoAvaibook a)
    {
        await s.EjecutarAsync("INSERT INTO AlojamientosAvaibook (IdSociedad, IdExterno, Nombre, IdActivo, IdConcepto, CausaExencion, Activo) VALUES (?, ?, ?, ?, ?, ?, ?)",
            a.IdSociedad, a.IdExterno, Recortar(a.Nombre, 255), a.IdActivo, a.IdConcepto, Vacio(a.CausaExencion), a.Activo);
        return await s.UltimoIdAsync();
    }

    internal static string? Vacio(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();

    internal static string? Recortar(string? t, int longitud)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        t = t.Trim();
        return t.Length <= longitud ? t : t[..longitud];
    }
}
