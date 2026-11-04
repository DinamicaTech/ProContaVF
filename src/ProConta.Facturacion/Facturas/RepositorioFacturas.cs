using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Lectura y escritura de Facturas, LineasFactura y SeriesFacturacion dentro de una sesión. Es interno: el resto del
/// programa usa <see cref="ServicioFacturas"/>, que aplica las reglas de estado y permisos.
/// </summary>
internal static class RepositorioFacturas
{
    private const string ColumnasCabecera =
        "f.IdFactura, f.IdSociedadEjercicio, f.IdSerie, f.NumeroFactura, f.IdCliente, f.IdContrato, f.CIF, f.Nombre, f.RazonSocial, f.PaisCliente, " +
        "f.TipoIdentificacionCliente, f.DireccionCliente, f.CodigoPostalCliente, f.PoblacionCliente, f.ProvinciaCliente, f.EmailCliente, " +
        "f.NIFEmisor, f.NombreEmisor, f.FechaFactura, f.FechaOperacion, f.FechaInicioValidez, f.FechaFinValidez, f.FechaCreacion, f.FechaHoraEmision, " +
        "f.FormaDePago, f.DiaDePago, f.Descripcion, f.NotasInternas, f.TipoRetencion, f.TipoFactura, f.TipoRectificativa, f.EstadoFactura, " +
        "f.IdFacturaRectificada, f.IdAsientoContable, f.FechaExportacionContable, f.Historial, f.SinVeriFactu, f.IdFacturaProgramada, f.Version, f.MotivoRectificacion";

    private const string ColumnasLinea =
        "IdLineaFactura, NumeroLinea, IdConcepto, Nombre, Descripcion, NotasInternas, Fecha, Cantidad, PrecioUnitario, DescuentoPorcentaje, DescuentoImporte, " +
        "BaseImponible, Impuesto, ClaveRegimen, CalificacionOperacion, CausaExencion, TipoIVA, CuotaIVA, TipoRecargoEquivalencia, CuotaRecargoEquivalencia, " +
        "EsSuplido, ImporteTotal, CuentaContable, IdDepartamento, IdCentroCoste";

    public static async Task<FacturaVenta?> CargarAsync(ISesionDatos s, int idFactura)
    {
        var f = await s.ConsultarPrimeroAsync(
            $"SELECT {ColumnasCabecera}, o.NumeroFactura AS NumeroOriginal FROM Facturas AS f LEFT JOIN Facturas AS o ON f.IdFacturaRectificada = o.IdFactura WHERE f.IdFactura = ?",
            MapearCabecera, idFactura);
        if (f is null) return null;
        var lineas = await s.ConsultarAsync($"SELECT {ColumnasLinea} FROM LineasFactura WHERE IdFactura = ? ORDER BY NumeroLinea", MapearLinea, idFactura);
        f.Lineas.AddRange(lineas);
        f.Renumerar();
        return f;
    }

    private static FacturaVenta MapearCabecera(IFilaDatos r) => new()
    {
        IdFactura = r.Entero("IdFactura"),
        IdSociedadEjercicio = r.Entero("IdSociedadEjercicio"),
        IdSerie = r.Entero("IdSerie"),
        NumeroFactura = r.Texto("NumeroFactura"),
        IdCliente = r.EnteroNulo("IdCliente"),
        IdContrato = r.EnteroNulo("IdContrato"),
        Cliente = new InstantaneaCliente(r.Texto("CIF"), r.Texto("Nombre"), r.Texto("RazonSocial"), r.Texto("PaisCliente"),
            r.EsNulo("TipoIdentificacionCliente") ? null : (byte)r.Entero("TipoIdentificacionCliente"),
            r.Texto("DireccionCliente"), r.Texto("CodigoPostalCliente"), r.Texto("PoblacionCliente"), r.Texto("ProvinciaCliente"), r.Texto("EmailCliente")),
        NifEmisor = r.Texto("NIFEmisor"),
        NombreEmisor = r.Texto("NombreEmisor"),
        FechaFactura = FechaReal(r, "FechaFactura"),
        FechaOperacion = FechaReal(r, "FechaOperacion"),
        FechaInicioValidez = FechaReal(r, "FechaInicioValidez"),
        FechaFinValidez = FechaReal(r, "FechaFinValidez"),
        FechaCreacion = r.FechaNula("FechaCreacion") ?? DateTime.MinValue,
        FechaHoraEmision = r.FechaNula("FechaHoraEmision"),
        FormaDePago = r.EsNulo("FormaDePago") ? null : (byte)r.Entero("FormaDePago"),
        DiaDePago = r.EsNulo("DiaDePago") ? null : (byte)r.Entero("DiaDePago"),
        Descripcion = r.Texto("Descripcion") ?? "",
        NotasInternas = r.Texto("NotasInternas"),
        TipoRetencion = r.EsNulo("TipoRetencion") ? 0m : Redondear(r.Doble("TipoRetencion")),
        TipoFactura = r.Texto("TipoFactura")?.Trim() ?? "F1",
        TipoRectificativa = r.Texto("TipoRectificativa"),
        Estado = (EstadoFactura)r.Entero("EstadoFactura"),
        IdFacturaRectificada = r.EnteroNulo("IdFacturaRectificada"),
        NumeroFacturaRectificada = r.Texto("NumeroOriginal"),
        IdAsientoContable = r.EnteroNulo("IdAsientoContable"),
        FechaExportacionContable = r.FechaNula("FechaExportacionContable"),
        Historial = r.Texto("Historial"),
        SinVeriFactu = r.Logico("SinVeriFactu"),
        IdFacturaProgramada = r.EnteroNulo("IdFacturaProgramada"),
        Version = r.EnteroNulo("Version") ?? 0,
        MotivoRectificacion = r.Texto("MotivoRectificacion")
    };

    private static LineaFacturaVenta MapearLinea(IFilaDatos r)
    {
        var l = new LineaFacturaVenta
        {
            IdLineaFactura = r.Entero("IdLineaFactura"),
            NumeroLinea = r.Entero("NumeroLinea"),
            IdConcepto = r.EnteroNulo("IdConcepto"),
            Nombre = r.Texto("Nombre") ?? "",
            Descripcion = r.Texto("Descripcion") ?? "",
            NotasInternas = r.Texto("NotasInternas"),
            Fecha = FechaReal(r, "Fecha"),
            Cantidad = Redondear(r.Doble("Cantidad"), 6),
            PrecioUnitario = r.Decimal("PrecioUnitario"),
            DescuentoPorcentaje = Redondear(r.Doble("DescuentoPorcentaje")),
            DescuentoImporte = r.Decimal("DescuentoImporte"),
            Impuesto = r.Texto("Impuesto")?.Trim() ?? "01",
            ClaveRegimen = r.Texto("ClaveRegimen")?.Trim() ?? "01",
            CalificacionOperacion = r.Texto("CalificacionOperacion")?.Trim() ?? "S1",
            CausaExencion = r.Texto("CausaExencion"),
            TipoIva = Redondear(r.Doble("TipoIVA")),
            TipoRecargo = Redondear(r.Doble("TipoRecargoEquivalencia")),
            EsSuplido = r.Logico("EsSuplido"),
            CuentaContable = r.Texto("CuentaContable")?.Trim(),
            IdDepartamento = r.EnteroNulo("IdDepartamento"),
            IdCentroCoste = r.EnteroNulo("IdCentroCoste")
        };
        l.CargarImportes(r.Decimal("BaseImponible"), r.Decimal("CuotaIVA"), r.Decimal("CuotaRecargoEquivalencia"), r.Decimal("ImporteTotal"));
        return l;
    }

    /// <summary>Los porcentajes y cantidades se guardan como DOUBLE/SINGLE: se redondean al leer para que 21 no sea 20,99999.</summary>
    /// <summary>
    /// Fecha que puede faltar. El ERP antiguo guardaba a veces la fecha cero de Access (30/12/1899) en lugar de vacío:
    /// se lee como vacía para no mostrarla ni arrastrarla.
    /// </summary>
    internal static DateTime? FechaReal(IFilaDatos r, string campo) => SinFechaCero(r.FechaNula(campo));

    internal static DateTime? SinFechaCero(DateTime? fecha) => fecha is { } d && d.Date != FechaCeroAccess ? d : null;

    private static readonly DateTime FechaCeroAccess = new(1899, 12, 30);

    private static decimal Redondear(double valor, int decimales = 4) => Math.Round((decimal)valor, decimales, MidpointRounding.AwayFromZero);

    private static object? Nulo(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    /// <summary>Inserta la factura nueva con sus líneas y devuelve su IdFactura.</summary>
    public static async Task<int> InsertarAsync(ISesionDatos s, FacturaVenta f)
    {
        var t = f.Totales;
        await s.EjecutarAsync(
            "INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, IdCliente, IdContrato, FechaFactura, FechaOperacion, FechaInicioValidez, FechaFinValidez, FechaCreacion, " +
            "FormaDePago, DiaDePago, Descripcion, NotasInternas, BaseImponible, IVA, ImporteBruto, TipoRetencion, Retencion, ImporteTotal, TipoFactura, TipoRectificativa, " +
            "EstadoFactura, IdFacturaRectificada, Historial, CIF, Nombre, RazonSocial, PaisCliente, TipoIdentificacionCliente, DireccionCliente, CodigoPostalCliente, " +
            "PoblacionCliente, ProvinciaCliente, EmailCliente, SinVeriFactu, Version, MotivoRectificacion) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            f.IdSociedadEjercicio, f.IdSerie, f.IdCliente, f.IdContrato, f.FechaFactura, f.FechaOperacion, f.FechaInicioValidez, f.FechaFinValidez, f.FechaCreacion,
            f.FormaDePago, f.DiaDePago, f.Descripcion ?? "", Nulo(f.NotasInternas), t.BaseImponible, t.Iva, t.ImporteBruto, (float)f.TipoRetencion, t.Retencion, t.ImporteTotal,
            f.TipoFactura, f.TipoRectificativa, (byte)f.Estado, f.IdFacturaRectificada, f.Historial,
            Nulo(f.Cliente.Nif), Nulo(f.Cliente.Nombre), Nulo(f.Cliente.RazonSocial), Nulo(f.Cliente.Pais), f.Cliente.TipoIdentificacion,
            Nulo(f.Cliente.Direccion), Nulo(f.Cliente.CodigoPostal), Nulo(f.Cliente.Poblacion), Nulo(f.Cliente.Provincia), Nulo(f.Cliente.Email), f.SinVeriFactu, 0, Nulo(f.MotivoRectificacion));
        var id = await s.UltimoIdAsync();
        f.IdFactura = id;
        f.Version = 0;
        await InsertarLineasAsync(s, f);
        return id;
    }

    /// <summary>Guarda cabecera y líneas de un borrador con concurrencia optimista (la versión leída).</summary>
    public static async Task ActualizarBorradorAsync(ISesionDatos s, FacturaVenta f)
    {
        var t = f.Totales;
        await s.ActualizarConVersionAsync(
            "UPDATE Facturas SET IdSerie = ?, IdCliente = ?, FechaFactura = ?, FechaOperacion = ?, FechaInicioValidez = ?, FechaFinValidez = ?, FormaDePago = ?, DiaDePago = ?, " +
            "Descripcion = ?, NotasInternas = ?, BaseImponible = ?, IVA = ?, ImporteBruto = ?, TipoRetencion = ?, Retencion = ?, ImporteTotal = ?, TipoFactura = ?, " +
            "Historial = ?, MotivoRectificacion = ?, Version = Version + 1 WHERE IdFactura = ? AND Version = ? AND EstadoFactura = ?",
            f.Rotulo,
            f.IdSerie, f.IdCliente, f.FechaFactura, f.FechaOperacion, f.FechaInicioValidez, f.FechaFinValidez, f.FormaDePago, f.DiaDePago,
            f.Descripcion ?? "", Nulo(f.NotasInternas), t.BaseImponible, t.Iva, t.ImporteBruto, (float)f.TipoRetencion, t.Retencion, t.ImporteTotal, f.TipoFactura,
            f.Historial, Nulo(f.MotivoRectificacion), f.IdFactura, f.Version, (byte)EstadoFactura.Borrador);
        f.Version++;
        await s.EjecutarAsync("DELETE FROM LineasFactura WHERE IdFactura = ?", f.IdFactura);
        await InsertarLineasAsync(s, f);
    }

    private static async Task InsertarLineasAsync(ISesionDatos s, FacturaVenta f)
    {
        f.Renumerar();
        foreach (var l in f.Lineas)
        {
            await s.EjecutarAsync(
                "INSERT INTO LineasFactura (IdFactura, NumeroLinea, IdConcepto, Nombre, Descripcion, NotasInternas, Fecha, Cantidad, PrecioUnitario, DescuentoPorcentaje, " +
                "DescuentoImporte, BaseImponible, Impuesto, ClaveRegimen, CalificacionOperacion, CausaExencion, TipoIVA, CuotaIVA, TipoRecargoEquivalencia, " +
                "CuotaRecargoEquivalencia, EsSuplido, ImporteTotal, CuentaContable, IdDepartamento, IdCentroCoste) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                f.IdFactura, (short)l.NumeroLinea, l.IdConcepto, l.Nombre.Trim(), string.IsNullOrWhiteSpace(l.Descripcion) ? " " : l.Descripcion, Nulo(l.NotasInternas), l.Fecha,
                (double)l.Cantidad, l.PrecioUnitario, (double)l.DescuentoPorcentaje, l.DescuentoImporte, l.BaseImponible, l.Impuesto, l.ClaveRegimen, l.CalificacionOperacion,
                Nulo(l.CausaExencion), (double)l.TipoIva, l.CuotaIva, (double)l.TipoRecargo, l.CuotaRecargo, l.EsSuplido, l.ImporteTotal, Nulo(l.CuentaContable),
                l.IdDepartamento, l.IdCentroCoste);
            l.IdLineaFactura = await s.UltimoIdAsync();
        }
    }

    /// <summary>
    /// Pasa el borrador a factura numerada: número, fecha, emisión, instantáneas y estado, solo si sigue siendo el
    /// borrador de la versión leída.
    /// </summary>
    public static Task EmitirAsync(ISesionDatos s, FacturaVenta f) =>
        s.ActualizarConVersionAsync(
            "UPDATE Facturas SET NumeroFactura = ?, FechaFactura = ?, FechaHoraEmision = ?, EstadoFactura = ?, CIF = ?, Nombre = ?, RazonSocial = ?, PaisCliente = ?, " +
            "TipoIdentificacionCliente = ?, DireccionCliente = ?, CodigoPostalCliente = ?, PoblacionCliente = ?, ProvinciaCliente = ?, EmailCliente = ?, NIFEmisor = ?, " +
            "NombreEmisor = ?, Historial = ?, SinVeriFactu = ?, Version = Version + 1 WHERE IdFactura = ? AND Version = ? AND EstadoFactura = ?",
            f.Rotulo,
            f.NumeroFactura, f.FechaFactura, f.FechaHoraEmision, (byte)f.Estado, Nulo(f.Cliente.Nif), Nulo(f.Cliente.Nombre), Nulo(f.Cliente.RazonSocial),
            Nulo(f.Cliente.Pais), f.Cliente.TipoIdentificacion, Nulo(f.Cliente.Direccion), Nulo(f.Cliente.CodigoPostal), Nulo(f.Cliente.Poblacion),
            Nulo(f.Cliente.Provincia), Nulo(f.Cliente.Email), Nulo(f.NifEmisor), Nulo(f.NombreEmisor), f.Historial, f.SinVeriFactu,
            f.IdFactura, f.Version, (byte)EstadoFactura.Borrador);

    /// <summary>Cambia el estado de una factura (y su historial) solo si está en el estado esperado.</summary>
    public static async Task<bool> CambiarEstadoAsync(ISesionDatos s, int idFactura, EstadoFactura esperado, EstadoFactura nuevo, string? historial, bool? sinVeriFactu = null)
    {
        var filas = sinVeriFactu is bool sv
            ? await s.EjecutarAsync("UPDATE Facturas SET EstadoFactura = ?, Historial = ?, SinVeriFactu = ?, Version = Version + 1 WHERE IdFactura = ? AND EstadoFactura = ?",
                (byte)nuevo, historial, sv, idFactura, (byte)esperado)
            : await s.EjecutarAsync("UPDATE Facturas SET EstadoFactura = ?, Historial = ?, Version = Version + 1 WHERE IdFactura = ? AND EstadoFactura = ?",
                (byte)nuevo, historial, idFactura, (byte)esperado);
        return filas == 1;
    }

    public static async Task BorrarAsync(ISesionDatos s, int idFactura)
    {
        await s.EjecutarAsync("DELETE FROM LineasFactura WHERE IdFactura = ?", idFactura);
        await s.EjecutarAsync("DELETE FROM Facturas WHERE IdFactura = ?", idFactura);
    }

    // ---------- series ----------

    private const string ColumnasSerie = "IdSerie, IdSociedadEjercicio, Descripcion, Prefijo, RangoDesde, RangoHasta, UltimoNumero, EsRectificativa, EsPredeterminada";

    internal static Serie MapearSerie(IFilaDatos r) => new(r.Entero("IdSerie"), r.Entero("IdSociedadEjercicio"), r.Texto("Descripcion") ?? "",
        r.Texto("Prefijo") ?? "", r.Entero("RangoDesde"), r.Entero("RangoHasta"), r.EnteroNulo("UltimoNumero"), r.Logico("EsRectificativa"), r.Logico("EsPredeterminada"));

    public static Task<IReadOnlyList<Serie>> SeriesAsync(ISesionDatos s, int idSociedadEjercicio) =>
        s.ConsultarAsync($"SELECT {ColumnasSerie} FROM SeriesFacturacion WHERE IdSociedadEjercicio = ? ORDER BY EsRectificativa, Prefijo", MapearSerie, idSociedadEjercicio);

    public static Task<Serie?> SerieAsync(ISesionDatos s, int idSerie) =>
        s.ConsultarPrimeroAsync($"SELECT {ColumnasSerie} FROM SeriesFacturacion WHERE IdSerie = ?", MapearSerie, idSerie);

    /// <summary>Serie normal que se propone al crear o duplicar: la predeterminada o, si solo hay una normal, esa.</summary>
    public static async Task<Resultado<Serie>> SerieNormalAsync(ISesionDatos s, int idSociedadEjercicio, int ejercicio)
    {
        var normales = (await SeriesAsync(s, idSociedadEjercicio)).Where(x => !x.EsRectificativa).ToList();
        var elegida = normales.FirstOrDefault(x => x.EsPredeterminada) ?? (normales.Count == 1 ? normales[0] : null);
        if (elegida is not null) return Resultado<Serie>.Correcto(elegida);
        return Resultado<Serie>.Fallo(new Error("Facturas.SinSeriePredeterminada",
            normales.Count == 0 ? $"El ejercicio {ejercicio} no tiene ninguna serie de facturación normal." : $"El ejercicio {ejercicio} tiene varias series y ninguna es la predeterminada.",
            null, "La factura necesita una serie para numerarse al validarla.",
            normales.Count == 0 ? "Crea la serie en Configuración › Parámetros y tablas › Series de facturación." : "Marca una como predeterminada en Configuración › Parámetros y tablas › Series de facturación."));
    }

    /// <summary>La serie rectificativa del ejercicio (debe haber una).</summary>
    public static async Task<Resultado<Serie>> SerieRectificativaAsync(ISesionDatos s, int idSociedadEjercicio, int ejercicio)
    {
        var rect = (await SeriesAsync(s, idSociedadEjercicio)).Where(x => x.EsRectificativa).ToList();
        if (rect.Count == 1) return Resultado<Serie>.Correcto(rect[0]);
        return Resultado<Serie>.Fallo(new Error("Facturas.SinSerieRectificativa",
            rect.Count == 0 ? $"El ejercicio {ejercicio} no tiene serie para rectificativas." : $"El ejercicio {ejercicio} tiene {rect.Count} series de rectificativas y debe haber una.",
            null, "Las rectificativas se numeran en su propia serie.",
            "Revisa las series en Configuración › Parámetros y tablas › Series de facturación: deja una sola marcada como rectificativa."));
    }

    /// <summary>Fecha de la última factura numerada de la serie (null si no hay ninguna).</summary>
    public static async Task<DateTime?> UltimaFechaSerieAsync(ISesionDatos s, int idSerie, int? excluir = null) =>
        await s.EscalarAsync("SELECT MAX(FechaFactura) FROM Facturas WHERE IdSerie = ? AND NumeroFactura IS NOT NULL AND IdFactura <> ?", idSerie, excluir ?? 0)
            is DateTime d ? d : null;

    /// <summary>Número de la última factura emitida en la serie, para continuar su formato.</summary>
    public static Task<string?> UltimoNumeroEmitidoAsync(ISesionDatos s, int idSerie) =>
        s.ConsultarPrimeroAsync("SELECT TOP 1 NumeroFactura FROM Facturas WHERE IdSerie = ? AND NumeroFactura IS NOT NULL ORDER BY FechaHoraEmision DESC, IdFactura DESC",
            r => r.Texto("NumeroFactura"), idSerie);

    /// <summary>
    /// Reserva el siguiente número de la serie dentro de la transacción: actualiza UltimoNumero solo si nadie lo ha
    /// cambiado desde que se leyó (la primera escritura sobre la serie bloquea a las demás sesiones); si otra sesión ganó
    /// la carrera, devuelve null y el llamador lo repite. El índice único de NumeroFactura es la última barrera.
    /// </summary>
    public static async Task<int?> ReservarNumeroAsync(ISesionDatos s, Serie serie)
    {
        var filas = serie.UltimoNumero is int ultimo
            ? await s.EjecutarAsync("UPDATE SeriesFacturacion SET UltimoNumero = ? WHERE IdSerie = ? AND UltimoNumero = ?", serie.Siguiente, serie.IdSerie, ultimo)
            : await s.EjecutarAsync("UPDATE SeriesFacturacion SET UltimoNumero = ? WHERE IdSerie = ? AND UltimoNumero IS NULL", serie.Siguiente, serie.IdSerie);
        return filas == 1 ? serie.Siguiente : null;
    }
}
