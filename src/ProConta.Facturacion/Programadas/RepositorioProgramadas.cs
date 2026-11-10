using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;

namespace ProConta.Facturacion.Programadas;

/// <summary>
/// Lectura y escritura de FacturasProgramadas, LineasFacturasProgramadas y LotesFacturasProgramadas dentro de una sesión.
/// Es interno: el resto del programa usa <see cref="ServicioProgramadas"/>.
/// </summary>
internal static class RepositorioProgramadas
{
    private const string Columnas =
        "p.IdFacturaProgramada, p.IdSociedadEjercicio, p.IdContrato, p.IdCliente, p.IdSerie, p.TipoFactura, p.FechaInicio, p.FechaFin, p.Periodicidad, p.DiaEmision, " +
        "p.ProximaFechaEmision, p.FechaUltimaGeneracion, p.FormaDePago, p.DiaDePago, p.TipoRetencion, p.Descripcion, p.NotasInternas, p.Activa, p.FechaAlta, " +
        "p.OperadorAlta, p.Historial, p.EnviarPorEmail, p.PeriodoDesde, p.PeriodoHasta, p.NumeroRepeticiones, p.FacturasGeneradas, p.AlGenerar, p.Version";

    private const string ColumnasLinea =
        "IdFacturaProgramada, NumeroLinea, IdConcepto, Nombre, Descripcion, NotasInternas, Cantidad, PrecioUnitario, DescuentoPorcentaje, DescuentoImporte, Impuesto, " +
        "ClaveRegimen, CalificacionOperacion, CausaExencion, TipoIVA, TipoRecargoEquivalencia, EsSuplido, CuentaContable, IdDepartamento, IdCentroCoste";

    public static async Task<FacturaProgramada?> CargarAsync(ISesionDatos s, int id)
    {
        var p = await s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM FacturasProgramadas AS p WHERE p.IdFacturaProgramada = ?", Mapear, id);
        if (p is null) return null;
        foreach (var (_, l) in await s.ConsultarAsync(
                     $"SELECT {ColumnasLinea} FROM LineasFacturasProgramadas WHERE IdFacturaProgramada = ? AND Activa = True ORDER BY NumeroLinea", MapearLinea, id))
            p.Plantilla.AnadirLinea(l);
        return p;
    }

    /// <summary>Programadas de la sociedad (de todos sus ejercicios) con sus líneas.</summary>
    public static async Task<IReadOnlyList<FacturaProgramada>> ListarAsync(ISesionDatos s, string idSociedad)
    {
        var lista = await s.ConsultarAsync(
            $"SELECT {Columnas} FROM FacturasProgramadas AS p INNER JOIN SociedadesEjercicios AS se ON p.IdSociedadEjercicio = se.IdSociedadEjercicio " +
            "WHERE se.IdSociedad = ? ORDER BY p.IdFacturaProgramada", Mapear, idSociedad);
        var porId = lista.ToDictionary(p => p.IdFacturaProgramada);
        var lineas = await s.ConsultarAsync(
            "SELECT l.IdFacturaProgramada, l.NumeroLinea, l.IdConcepto, l.Nombre, l.Descripcion, l.NotasInternas, l.Cantidad, l.PrecioUnitario, l.DescuentoPorcentaje, " +
            "l.DescuentoImporte, l.Impuesto, l.ClaveRegimen, l.CalificacionOperacion, l.CausaExencion, l.TipoIVA, l.TipoRecargoEquivalencia, l.EsSuplido, l.CuentaContable, " +
            "l.IdDepartamento, l.IdCentroCoste FROM (LineasFacturasProgramadas AS l INNER JOIN FacturasProgramadas AS p ON l.IdFacturaProgramada = p.IdFacturaProgramada) " +
            "INNER JOIN SociedadesEjercicios AS se ON p.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE se.IdSociedad = ? AND l.Activa = True ORDER BY l.IdFacturaProgramada, l.NumeroLinea",
            MapearLinea, idSociedad);
        foreach (var (id, l) in lineas)
            if (porId.TryGetValue(id, out var p)) p.Plantilla.AnadirLinea(l);
        return lista;
    }

    private static FacturaProgramada Mapear(IFilaDatos r)
    {
        var p = new FacturaProgramada
        {
            IdFacturaProgramada = r.Entero("IdFacturaProgramada"),
            IdSociedadEjercicio = r.Entero("IdSociedadEjercicio"),
            IdContrato = r.EnteroNulo("IdContrato"),
            Periodicidad = Periodicidad.De(r.Texto("Periodicidad")),
            FechaInicio = RepositorioFacturas.FechaReal(r, "FechaInicio"),
            FechaFin = RepositorioFacturas.FechaReal(r, "FechaFin"),
            DiaEmision = r.EsNulo("DiaEmision") ? null : (byte)r.Entero("DiaEmision"),
            ProximaFechaEmision = RepositorioFacturas.FechaReal(r, "ProximaFechaEmision"),
            FechaUltimaGeneracion = r.FechaNula("FechaUltimaGeneracion"),
            Activa = r.Logico("Activa"),
            FechaAlta = r.FechaNula("FechaAlta") ?? DateTime.MinValue,
            OperadorAlta = r.Texto("OperadorAlta"),
            Historial = r.Texto("Historial"),
            EnviarPorEmail = r.Logico("EnviarPorEmail"),
            NumeroRepeticiones = r.EnteroNulo("NumeroRepeticiones"),
            FacturasGeneradas = r.EnteroNulo("FacturasGeneradas") ?? 0,
            AlGenerar = string.Equals(r.Texto("AlGenerar")?.Trim(), "VALIDAR", StringComparison.OrdinalIgnoreCase) ? AlGenerar.Validar : AlGenerar.Borrador,
            Version = r.EnteroNulo("Version") ?? 0
        };
        p.Plantilla = new FacturaVenta
        {
            IdSociedadEjercicio = p.IdSociedadEjercicio,
            IdSerie = r.Entero("IdSerie"),
            IdCliente = r.EnteroNulo("IdCliente"),
            IdContrato = p.IdContrato,
            TipoFactura = r.Texto("TipoFactura")?.Trim() ?? "F1",
            FormaDePago = r.EsNulo("FormaDePago") ? null : (byte)r.Entero("FormaDePago"),
            DiaDePago = r.EsNulo("DiaDePago") ? null : (byte)r.Entero("DiaDePago"),
            TipoRetencion = r.EsNulo("TipoRetencion") ? 0m : Math.Round((decimal)r.Doble("TipoRetencion"), 4, MidpointRounding.AwayFromZero),
            Descripcion = r.Texto("Descripcion") ?? "",
            NotasInternas = r.Texto("NotasInternas"),
            FechaInicioValidez = RepositorioFacturas.FechaReal(r, "PeriodoDesde"),
            FechaFinValidez = RepositorioFacturas.FechaReal(r, "PeriodoHasta"),
            FechaCreacion = p.FechaAlta
        };
        return p;
    }

    private static (int IdFacturaProgramada, LineaFacturaVenta Linea) MapearLinea(IFilaDatos r)
    {
        static decimal R(double v, int d = 4) => Math.Round((decimal)v, d, MidpointRounding.AwayFromZero);
        var l = new LineaFacturaVenta
        {
            IdConcepto = r.EnteroNulo("IdConcepto"),
            Nombre = r.Texto("Nombre") ?? "",
            Descripcion = r.Texto("Descripcion") is { } d && d.Trim().Length > 0 ? d : "",
            NotasInternas = r.Texto("NotasInternas"),
            Cantidad = R(r.Doble("Cantidad"), 6),
            PrecioUnitario = r.Decimal("PrecioUnitario"),
            DescuentoPorcentaje = R(r.Doble("DescuentoPorcentaje")),
            DescuentoImporte = r.Decimal("DescuentoImporte"),
            Impuesto = r.Texto("Impuesto")?.Trim() ?? "01",
            ClaveRegimen = r.Texto("ClaveRegimen")?.Trim() ?? "01",
            CalificacionOperacion = r.Texto("CalificacionOperacion")?.Trim() is { Length: > 0 } c ? c : "S1",
            CausaExencion = r.Texto("CausaExencion"),
            TipoIva = R(r.Doble("TipoIVA")),
            TipoRecargo = R(r.Doble("TipoRecargoEquivalencia")),
            EsSuplido = r.Logico("EsSuplido"),
            CuentaContable = r.Texto("CuentaContable")?.Trim(),
            IdDepartamento = r.EnteroNulo("IdDepartamento"),
            IdCentroCoste = r.EnteroNulo("IdCentroCoste")
        };
        l.Recalcular();
        return (r.Entero("IdFacturaProgramada"), l);
    }

    private static object? Nulo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static object?[] ValoresCabecera(FacturaProgramada p, DateTime proxima)
    {
        var f = p.Plantilla;
        return
        [
            f.IdCliente, f.IdSerie, f.TipoFactura, p.FechaInicio!.Value.Date, p.FechaFin?.Date, p.Periodicidad.Codigo, p.DiaEmision, proxima, f.FormaDePago, f.DiaDePago,
            (float)f.TipoRetencion, Nulo(f.Descripcion), Nulo(f.NotasInternas), p.Activa, p.Historial, p.EnviarPorEmail, f.FechaInicioValidez?.Date, f.FechaFinValidez?.Date,
            p.Limite, p.AlGenerar == AlGenerar.Validar ? "VALIDAR" : "BORRADOR"
        ];
    }

    private const string CamposCabecera =
        "IdCliente = ?, IdSerie = ?, TipoFactura = ?, FechaInicio = ?, FechaFin = ?, Periodicidad = ?, DiaEmision = ?, ProximaFechaEmision = ?, FormaDePago = ?, DiaDePago = ?, " +
        "TipoRetencion = ?, Descripcion = ?, NotasInternas = ?, Activa = ?, Historial = ?, EnviarPorEmail = ?, PeriodoDesde = ?, PeriodoHasta = ?, NumeroRepeticiones = ?, AlGenerar = ?";

    /// <summary>Próxima fecha que se guarda (la columna es obligatoria): la calculada o, si ya no hay vencimientos, la última.</summary>
    private static DateTime Proxima(FacturaProgramada p) =>
        p.ProximaCalculada ?? p.Emision(Math.Max(0, p.FacturasGeneradas - 1)) ?? p.FechaInicio!.Value.Date;

    public static async Task<int> InsertarAsync(ISesionDatos s, FacturaProgramada p)
    {
        var v = ValoresCabecera(p, Proxima(p));
        await s.EjecutarAsync(
            "INSERT INTO FacturasProgramadas (IdCliente, IdSerie, TipoFactura, FechaInicio, FechaFin, Periodicidad, DiaEmision, ProximaFechaEmision, FormaDePago, DiaDePago, " +
            "TipoRetencion, Descripcion, NotasInternas, Activa, Historial, EnviarPorEmail, PeriodoDesde, PeriodoHasta, NumeroRepeticiones, AlGenerar, " +
            "IdSociedadEjercicio, IdContrato, FechaAlta, OperadorAlta, FacturasGeneradas, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [.. v, p.IdSociedadEjercicio, p.IdContrato, p.FechaAlta, Nulo(p.OperadorAlta), p.FacturasGeneradas, 0]);
        p.IdFacturaProgramada = await s.UltimoIdAsync();
        p.Version = 0;
        await InsertarLineasAsync(s, p);
        return p.IdFacturaProgramada;
    }

    /// <summary>Guarda cabecera y líneas con concurrencia optimista (la versión leída).</summary>
    public static async Task ActualizarAsync(ISesionDatos s, FacturaProgramada p)
    {
        await s.ActualizarConVersionAsync($"UPDATE FacturasProgramadas SET {CamposCabecera}, Version = Version + 1 WHERE IdFacturaProgramada = ? AND Version = ?",
            p.Rotulo, [.. ValoresCabecera(p, Proxima(p)), p.IdFacturaProgramada, p.Version]);
        p.Version++;
        await s.EjecutarAsync("DELETE FROM LineasFacturasProgramadas WHERE IdFacturaProgramada = ?", p.IdFacturaProgramada);
        await InsertarLineasAsync(s, p);
    }

    private static async Task InsertarLineasAsync(ISesionDatos s, FacturaProgramada p)
    {
        p.Plantilla.Renumerar();
        foreach (var l in p.Plantilla.Lineas)
            await s.EjecutarAsync(
                "INSERT INTO LineasFacturasProgramadas (IdFacturaProgramada, NumeroLinea, IdConcepto, Nombre, Descripcion, NotasInternas, Cantidad, PrecioUnitario, " +
                "DescuentoPorcentaje, DescuentoImporte, Impuesto, ClaveRegimen, CalificacionOperacion, CausaExencion, TipoIVA, TipoRecargoEquivalencia, EsSuplido, " +
                "CuentaContable, IdDepartamento, IdCentroCoste, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                p.IdFacturaProgramada, (short)l.NumeroLinea, l.IdConcepto, Nulo(l.Nombre), string.IsNullOrWhiteSpace(l.Descripcion) ? " " : l.Descripcion, Nulo(l.NotasInternas),
                (double)l.Cantidad, l.PrecioUnitario, (double)l.DescuentoPorcentaje, l.DescuentoImporte, l.Impuesto, l.ClaveRegimen, l.CalificacionOperacion, Nulo(l.CausaExencion),
                (double)l.TipoIva, (double)l.TipoRecargo, l.EsSuplido, Nulo(l.CuentaContable), l.IdDepartamento, l.IdCentroCoste, true);
    }

    /// <summary>
    /// Avanza la programada tras crear la factura del vencimiento <paramref name="indice"/>: solo si nadie la ha cambiado
    /// ni avanzado desde que se leyó (si no, lanza el conflicto y la transacción no deja nada).
    /// </summary>
    public static async Task AvanzarAsync(ISesionDatos s, FacturaProgramada p, int indice, DateTime ahora)
    {
        await s.ActualizarConVersionAsync(
            "UPDATE FacturasProgramadas SET FacturasGeneradas = ?, ProximaFechaEmision = ?, FechaUltimaGeneracion = ?, Historial = ?, Version = Version + 1 " +
            "WHERE IdFacturaProgramada = ? AND Version = ? AND FacturasGeneradas = ?",
            p.Rotulo, indice + 1, Proxima(p), ahora, p.Historial, p.IdFacturaProgramada, p.Version, indice);
        p.Version++;
    }

    public static async Task<int> FacturasDeAsync(ISesionDatos s, int id) =>
        Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Facturas WHERE IdFacturaProgramada = ?", id));

    public static async Task BorrarAsync(ISesionDatos s, FacturaProgramada p)
    {
        await s.EjecutarAsync("DELETE FROM LineasFacturasProgramadas WHERE IdFacturaProgramada = ?", p.IdFacturaProgramada);
        await s.ActualizarConVersionAsync("DELETE FROM FacturasProgramadas WHERE IdFacturaProgramada = ? AND Version = ?", p.Rotulo, p.IdFacturaProgramada, p.Version);
    }

    // ---------- lotes ----------

    public static async Task<int> AbrirLoteAsync(ISesionDatos s, int idSociedadEjercicio, DateTime hasta, int programaciones, string operador, DateTime ahora, string historial)
    {
        await s.EjecutarAsync(
            "INSERT INTO LotesFacturasProgramadas (IdSociedadEjercicio, FechaHoraInicio, FechaGeneracionHasta, Estado, NumeroProgramaciones, NumeroFacturasGeneradas, Operador, Historial) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?)", idSociedadEjercicio, ahora, hasta.Date, "PROCESANDO", programaciones, 0, Nulo(operador), historial);
        return await s.UltimoIdAsync();
    }

    public static Task CerrarLoteAsync(ISesionDatos s, int idLote, string estado, int generadas, string? errores, DateTime ahora, string historial) =>
        s.EjecutarAsync("UPDATE LotesFacturasProgramadas SET Estado = ?, NumeroFacturasGeneradas = ?, DescripcionError = ?, FechaHoraFin = ?, Historial = ? WHERE IdLoteFacturaProgramada = ?",
            estado, generadas, errores, ahora, historial, idLote);

    /// <summary>Marca la procedencia de la factura creada: el índice único (programada, fecha de origen) impide repetirla.</summary>
    public static Task MarcarOrigenAsync(ISesionDatos s, int idFactura, int idProgramada, int idLote, DateTime fechaOrigen) =>
        s.EjecutarAsync("UPDATE Facturas SET IdFacturaProgramada = ?, IdLoteFacturaProgramada = ?, FechaProgramadaOrigen = ? WHERE IdFactura = ?",
            idProgramada, idLote, fechaOrigen.Date, idFactura);
}
