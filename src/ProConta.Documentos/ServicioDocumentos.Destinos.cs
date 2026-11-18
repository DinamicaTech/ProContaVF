using System.Globalization;
using System.Text.RegularExpressions;
using ProConta.Datos.Persistencia;

namespace ProConta.Documentos;

/// <summary>
/// Buscador de «Vincular a…» (documentos 2026-11-18, maqueta validada): una sola caja para encontrar cualquier registro al que vincular un
/// documento escribiendo, por ejemplo, «303 2t 2026», «prado», «2025» o «local». Busca en los modelos presentados (y propone el modelo escrito
/// aunque no tenga presentación grabada, para los antiguos), los ejercicios, la empresa, los terceros, los activos, las facturas emitidas y recibidas
/// y los asientos. Todas las palabras escritas tienen que aparecer en el registro.
/// </summary>
public sealed partial class ServicioDocumentos
{
    public const int MaximoPorGrupo = 15;

    private static readonly Regex _modeloEscrito = new(@"^\d{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex _anioEscrito = new(@"^(19|20)\d{2}$", RegexOptions.CultureInvariant);
    private static readonly Regex _periodoEscrito = new(@"^([1-4]t|[1-3]p|0a|0[1-9]|1[0-2])$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public Task<IReadOnlyList<DestinoVinculo>> BuscarDestinosAsync(string idSociedad, string? texto) => _bd.EjecutarAsync(async s =>
    {
        var palabras = Palabras(texto);
        var larga = palabras.Where(p => p.Length >= 3).OrderByDescending(p => p.Length).FirstOrDefault();
        var patron = larga is null ? null : "%" + larga.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
        var todos = new List<DestinoVinculo>();
        todos.AddRange(await ModelosAsync(s, idSociedad, palabras));
        todos.AddRange(await EjerciciosAsync(s, idSociedad));
        todos.AddRange(await EmpresaAsync(s, idSociedad));
        todos.AddRange(await ActivosAsync(s, idSociedad));
        if (patron is not null)
        {
            todos.AddRange(await TercerosAsync(s, patron));
            todos.AddRange(await FacturasVentaAsync(s, idSociedad, patron));
            todos.AddRange(await FacturasCompraAsync(s, idSociedad, patron));
            todos.AddRange(await AsientosAsync(s, idSociedad, patron, palabras));
        }
        return (IReadOnlyList<DestinoVinculo>)todos
            .Where(d => Coincide(d, palabras))
            .GroupBy(d => d.Vinculo.TipoEntidad)
            .OrderBy(g => TiposEntidad.Orden(g.Key))
            .SelectMany(g => g.Take(MaximoPorGrupo))
            .ToList();
    });

    internal static IReadOnlyList<string> Palabras(string? texto) =>
        LectorPropuestas.QuitarAcentos((texto ?? "").Replace('·', ' ')).ToLowerInvariant().Split([' ', ',', ';', '/', '-'], StringSplitOptions.RemoveEmptyEntries);

    internal static bool Coincide(DestinoVinculo d, IReadOnlyList<string> palabras)
    {
        if (palabras.Count == 0) return true;
        var texto = LectorPropuestas.QuitarAcentos($"{d.Grupo} {TiposEntidad.Nombre(d.Vinculo.TipoEntidad)} {d.Vinculo.Etiqueta} {d.Detalle}".Replace('·', ' ')).ToLowerInvariant();
        return palabras.All(p => texto.Contains(p, StringComparison.Ordinal));
    }

    private static async Task<IReadOnlyList<DestinoVinculo>> ModelosAsync(ISesionDatos s, string idSociedad, IReadOnlyList<string> palabras)
    {
        var filas = await s.ConsultarAsync(
            "SELECT Modelo, Ejercicio, Periodo, MAX(FechaHora) AS Ultima, MAX(Entorno) AS EntornoMax, MIN(Entorno) AS EntornoMin FROM PresentacionesAeat WHERE IdSociedad = ? " +
            "GROUP BY Modelo, Ejercicio, Periodo",
            f => (Modelo: (f.Texto("Modelo") ?? "").Trim(), Ejercicio: f.Entero("Ejercicio"), Periodo: (f.Texto("Periodo") ?? "").Trim(), Fecha: f.Fecha("Ultima"),
                SoloPruebas: f.Texto("EntornoMin") == "PRUEBAS" && f.Texto("EntornoMax") == "PRUEBAS"), idSociedad);
        var lista = filas.OrderByDescending(f => f.Fecha)
            .Select(f => new DestinoVinculo(Vinculo.Modelo(f.Modelo, f.Ejercicio, f.Periodo),
                $"Presentado el {f.Fecha:dd/MM/yyyy}{(f.SoloPruebas ? " (entorno de pruebas)" : "")}"))
            .ToList();
        // El modelo escrito («303 2t 2024»), aunque no tenga presentación grabada: los antiguos los presentó la gestoría o se presentaron fuera.
        var modelo = palabras.FirstOrDefault(p => _modeloEscrito.IsMatch(p));
        var anio = palabras.FirstOrDefault(p => _anioEscrito.IsMatch(p));
        if (modelo is not null && anio is not null)
        {
            var periodo = palabras.FirstOrDefault(p => _periodoEscrito.IsMatch(p))?.ToUpperInvariant() ?? "0A";
            var v = Vinculo.Modelo(modelo, int.Parse(anio, CultureInfo.InvariantCulture), periodo);
            if (!lista.Any(d => d.Vinculo.MismoDestino(v)))
                lista.Add(new DestinoVinculo(v, periodo == "0A" ? "Sin presentación grabada en el programa (anual)" : "Sin presentación grabada en el programa"));
        }
        return lista;
    }

    private static async Task<IReadOnlyList<DestinoVinculo>> EjerciciosAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio, EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdSociedadEjercicio"), Anio: (f.Texto("Ejercicio") ?? "").Trim(), Cerrado: f.Logico("EjercicioCerrado")), idSociedad))
        .OrderByDescending(e => e.Anio)
        .Select(e => new DestinoVinculo(new Vinculo(TiposEntidad.Ejercicio, e.Id, null, $"Ejercicio {e.Anio}"), e.Cerrado ? "Cerrado" : "Abierto"))
        .ToList();

    private static async Task<IReadOnlyList<DestinoVinculo>> EmpresaAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync("SELECT Nombre, CIF FROM Sociedades WHERE IdSociedad = ?", f => (Nombre: f.Texto("Nombre") ?? idSociedad, Cif: f.Texto("CIF")), idSociedad))
        .Select(e => new DestinoVinculo(Empresa(idSociedad, e.Nombre), $"La sociedad{(string.IsNullOrWhiteSpace(e.Cif) ? "" : " · " + e.Cif)} · escrituras, actas, certificados…"))
        .ToList();

    private static async Task<IReadOnlyList<DestinoVinculo>> ActivosAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync("SELECT IdActivo, Codigo, Nombre, FechaBaja FROM Activos WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdActivo"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim(), Baja: f.FechaNula("FechaBaja")), idSociedad))
        .OrderBy(a => a.Codigo, StringComparer.CurrentCultureIgnoreCase)
        .Select(a => new DestinoVinculo(new Vinculo(TiposEntidad.Activo, a.Id, null, a.Nombre.Length > 0 ? a.Nombre : a.Codigo),
            a.Codigo + (a.Baja is DateTime b ? $" · de baja desde {b:dd/MM/yyyy}" : "")))
        .ToList();

    private static async Task<IReadOnlyList<DestinoVinculo>> TercerosAsync(ISesionDatos s, string patron) =>
        (await s.ConsultarAsync("SELECT TOP 60 IdTercero, Nombre, RazonSocial, NombreComercial, NIF FROM Terceros " +
                                "WHERE Nombre LIKE ? OR RazonSocial LIKE ? OR NombreComercial LIKE ? OR NIF LIKE ? ORDER BY Nombre",
            f => (Id: f.Entero("IdTercero"), Nombre: f.Texto("Nombre") ?? "", Razon: f.Texto("RazonSocial"), Comercial: f.Texto("NombreComercial"), Nif: f.Texto("NIF")),
            patron, patron, patron, patron))
        .Select(t => new DestinoVinculo(new Vinculo(TiposEntidad.Tercero, t.Id, null, t.Nombre),
            string.Join(" · ", new[] { t.Nif, t.Comercial, t.Razon != t.Nombre ? t.Razon : null }.Where(x => !string.IsNullOrWhiteSpace(x)))))
        .ToList();

    private static async Task<IReadOnlyList<DestinoVinculo>> FacturasVentaAsync(ISesionDatos s, string idSociedad, string patron) =>
        (await s.ConsultarAsync("SELECT TOP 60 f.IdFactura, f.NumeroFactura, f.Nombre, f.FechaFactura, f.ImporteTotal FROM Facturas f " +
                                "INNER JOIN SociedadesEjercicios e ON f.IdSociedadEjercicio = e.IdSociedadEjercicio " +
                                "WHERE e.IdSociedad = ? AND f.NumeroFactura IS NOT NULL AND (f.NumeroFactura LIKE ? OR f.Nombre LIKE ? OR f.RazonSocial LIKE ?) ORDER BY f.FechaFactura DESC",
            f => (Id: f.Entero("IdFactura"), Numero: f.Texto("NumeroFactura") ?? "", Cliente: f.Texto("Nombre") ?? "", Fecha: f.FechaNula("FechaFactura"), Total: f.DecimalNulo("ImporteTotal")),
            idSociedad, patron, patron, patron))
        .Select(f => new DestinoVinculo(new Vinculo(TiposEntidad.FacturaVenta, f.Id, null, $"{f.Numero} · {f.Cliente}"),
            $"Emitida{(f.Fecha is DateTime d ? $" el {d:dd/MM/yyyy}" : "")}{(f.Total is decimal t ? $" · {t.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €" : "")}"))
        .ToList();

    private static async Task<IReadOnlyList<DestinoVinculo>> FacturasCompraAsync(ISesionDatos s, string idSociedad, string patron) =>
        (await s.ConsultarAsync("SELECT TOP 60 c.IdRegistro, c.NumeroFactura, c.Fecha, c.ImporteTotal, c.NombreProveedorAeat, t.Nombre AS Proveedor FROM FacturasCompra c " +
                                "LEFT JOIN Terceros t ON c.IdProveedor = t.IdTercero " +
                                "WHERE c.Sociedad = ? AND (c.NumeroFactura LIKE ? OR c.NombreProveedorAeat LIKE ? OR t.Nombre LIKE ?) ORDER BY c.Fecha DESC",
            f => (Id: f.Entero("IdRegistro"), Numero: f.Texto("NumeroFactura") ?? "", Proveedor: f.Texto("Proveedor") ?? f.Texto("NombreProveedorAeat") ?? "",
                Fecha: f.FechaNula("Fecha"), Total: f.DecimalNulo("ImporteTotal")),
            idSociedad, patron, patron, patron))
        .Select(f => new DestinoVinculo(new Vinculo(TiposEntidad.FacturaCompra, f.Id, null, $"{f.Numero} · {f.Proveedor}"),
            $"Recibida{(f.Fecha is DateTime d ? $" el {d:dd/MM/yyyy}" : "")}{(f.Total is decimal t ? $" · {t.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €" : "")}"))
        .ToList();

    private static async Task<IReadOnlyList<DestinoVinculo>> AsientosAsync(ISesionDatos s, string idSociedad, string patron, IReadOnlyList<string> palabras)
    {
        var numero = palabras.Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : (int?)null).FirstOrDefault(n => n is > 0);
        var filas = await s.ConsultarAsync("SELECT TOP 60 a.IdAsiento, a.FechaContable, a.Descripcion, a.TotalDebe FROM Asientos a " +
                                           "INNER JOIN SociedadesEjercicios e ON a.IdSociedadEjercicio = e.IdSociedadEjercicio " +
                                           "WHERE e.IdSociedad = ? AND (a.Descripcion LIKE ? OR a.IdAsiento = ?) ORDER BY a.FechaContable DESC",
            f => (Id: f.Entero("IdAsiento"), Fecha: f.Fecha("FechaContable"), Desc: f.Texto("Descripcion") ?? "", Total: f.DecimalNulo("TotalDebe")),
            idSociedad, patron, numero ?? -1);
        return filas.Select(a => new DestinoVinculo(new Vinculo(TiposEntidad.Asiento, a.Id, null, $"nº {a.Id} · {a.Desc}"),
                $"{a.Fecha:dd/MM/yyyy}{(a.Total is decimal t ? $" · {t.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €" : "")}"))
            .ToList();
    }
}
