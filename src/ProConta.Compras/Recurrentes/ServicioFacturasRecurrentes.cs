using System.Globalization;
using ProConta.Compras.Facturas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Terceros.Roles;

namespace ProConta.Compras.Recurrentes;

/// <summary>
/// Compras › Facturas recurrentes pendientes (compras/facturas-recurrentes-pendientes): lee las facturas de compra de la sociedad de los últimos
/// 12 meses (sin mirar el ejercicio; las del ERP sin IdSociedadEjercicio se reconocen por su sociedad), los proveedores con la periodicidad de su
/// ficha y los periodos descartados, y se los pasa al <see cref="DetectorFacturasRecurrentes"/>. «No llegará» y «Volver a esperarla» escriben en
/// FacturasRecurrentesDescartadas. No genera asientos ni toca las facturas.
/// </summary>
/// <param name="hoy">Día de hoy; por defecto, la fecha de Europe/Madrid.</param>
public sealed class ServicioFacturasRecurrentes(IBaseDatos bd, Func<DateTime>? hoy = null)
{
    /// <summary>Fecha de hoy en Europe/Madrid (la zona horaria del proyecto), sea cual sea la del puesto.</summary>
    public static DateTime HoyMadrid()
    {
        try { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Europe/Madrid").Date; }
        catch (TimeZoneNotFoundException)
        {
            try { return TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Romance Standard Time").Date; }
            catch (TimeZoneNotFoundException) { return DateTime.Today; }
        }
    }

    public DateTime Hoy => (hoy ?? HoyMadrid)().Date;

    /// <summary>Facturas recurrentes pendientes de la sociedad hasta hoy, con la ruta del PDF de la última factura de cada proveedor.</summary>
    public Task<IReadOnlyList<FacturaRecurrentePendiente>> PendientesAsync(string idSociedad, bool mostrarDescartadas = false, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync<IReadOnlyList<FacturaRecurrentePendiente>>(async s =>
        {
            var hoyDia = Hoy;
            var facturas = await s.ConsultarAsync(
                "SELECT fc.IdRegistro, fc.IdProveedor, fc.Fecha, fc.ImporteTotal, fc.Duplicada, fc.IdRegistroFacturaRectificada, fc.NombreProveedorAeat " +
                "FROM FacturasCompra AS fc LEFT JOIN SociedadesEjercicios AS se ON fc.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                "WHERE (se.IdSociedad = ? OR (fc.IdSociedadEjercicio Is Null AND fc.Sociedad = ?)) AND fc.IdProveedor Is Not Null AND fc.Fecha >= ? AND fc.Fecha < ?",
                f =>
                {
                    var total = f.DecimalNulo("ImporteTotal") ?? 0m;
                    return (Factura: new FacturaParaRecurrencia(f.Entero("IdRegistro"), f.EnteroNulo("IdProveedor"), f.Fecha("Fecha"), total,
                        !f.EsNulo("Duplicada") && f.Logico("Duplicada"), f.EnteroNulo("IdRegistroFacturaRectificada") is not null || total < 0m),
                        Aeat: f.Texto("NombreProveedorAeat")?.Trim());
                },
                idSociedad, idSociedad, DetectorFacturasRecurrentes.Desde(hoyDia), hoyDia.AddDays(1));
            var ids = facturas.Select(f => f.Factura.IdProveedor!.Value).ToHashSet();
            var aeat = facturas.Where(f => !string.IsNullOrWhiteSpace(f.Aeat)).GroupBy(f => f.Factura.IdProveedor!.Value)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Factura.Fecha).First().Aeat!);
            var proveedores = (await s.ConsultarAsync(
                    "SELECT p.IdProveedor, p.IdTercero, p.PeriodicidadFacturas, t.NombreComercial, t.RazonSocial, t.Nombre " +
                    "FROM Proveedores AS p INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero",
                    f => (Id: f.Entero("IdProveedor"), Tercero: f.Entero("IdTercero"), Periodicidad: f.EnteroNulo("PeriodicidadFacturas"),
                        Nombres: new[] { f.Texto("NombreComercial"), f.Texto("RazonSocial"), f.Texto("Nombre") })))
                .Where(p => ids.Contains(p.Id))
                .ToDictionary(p => p.Id, p => new ProveedorParaRecurrencia(p.Id, p.Tercero,
                    Primero([.. p.Nombres, aeat.GetValueOrDefault(p.Id)]),
                    p.Periodicidad is int v && Enum.IsDefined(typeof(PeriodicidadFacturas), (byte)v) ? (PeriodicidadFacturas)(byte)v : null));
            var descartados = await DescartadosAsync(s, idSociedad);
            var filas = DetectorFacturasRecurrentes.Detectar(facturas.Select(f => f.Factura), proveedores, descartados, hoyDia, mostrarDescartadas);
            if (filas.Count == 0) return filas;
            var pdf = await PdfsAsync(s, idSociedad, filas.Select(f => f.IdRegistroUltima).Distinct().ToList());
            return filas.Select(f => f with { RutaPdfUltima = pdf.GetValueOrDefault(f.IdRegistroUltima) }).ToList();
        }, cancelacion);

    /// <summary>«No llegará»: descarta esos periodos de esos proveedores en la sociedad. Repetirlo no duplica nada.</summary>
    public Task<int> DescartarAsync(string idSociedad, IReadOnlyList<(int IdProveedor, DateTime Periodo)> periodos, Actor actor) =>
        bd.EjecutarAsync(async s =>
        {
            var hechos = 0;
            var ahora = DateTime.Now;
            foreach (var (idProveedor, periodo) in periodos.Distinct())
            {
                var mes = DetectorFacturasRecurrentes.Periodo(periodo);
                if (await ExisteAsync(s, idSociedad, idProveedor, mes)) continue;
                await s.EjecutarAsync("INSERT INTO FacturasRecurrentesDescartadas (IdSociedad, IdProveedor, Periodo, FechaHora, Operador) VALUES (?, ?, ?, ?, ?)",
                    idSociedad, idProveedor, mes, ahora, Recortar(actor.ToString(), 100));
                hechos++;
            }
            return hechos;
        });

    /// <summary>«Volver a esperarla»: quita el descarte de esos periodos, que vuelven a salir como pendientes.</summary>
    public Task<int> DeshacerDescarteAsync(string idSociedad, IReadOnlyList<(int IdProveedor, DateTime Periodo)> periodos) =>
        bd.EjecutarAsync(async s =>
        {
            var hechos = 0;
            foreach (var (idProveedor, periodo) in periodos.Distinct())
                hechos += await s.EjecutarAsync("DELETE FROM FacturasRecurrentesDescartadas WHERE IdSociedad = ? AND IdProveedor = ? AND Periodo = ?",
                    idSociedad, idProveedor, DetectorFacturasRecurrentes.Periodo(periodo));
            return hechos;
        });

    private static async Task<bool> ExisteAsync(ISesionDatos s, string idSociedad, int idProveedor, DateTime periodo) =>
        Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM FacturasRecurrentesDescartadas WHERE IdSociedad = ? AND IdProveedor = ? AND Periodo = ?",
            idSociedad, idProveedor, periodo), CultureInfo.InvariantCulture) > 0;

    private static async Task<IReadOnlySet<(int IdProveedor, DateTime Periodo)>> DescartadosAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync("SELECT IdProveedor, Periodo FROM FacturasRecurrentesDescartadas WHERE IdSociedad = ?",
            f => (f.Entero("IdProveedor"), DetectorFacturasRecurrentes.Periodo(f.Fecha("Periodo"))), idSociedad)).ToHashSet();

    /// <summary>Ruta del PDF de cada factura, localizada como en Facturas recibidas (en la carpeta de compras de su ejercicio).</summary>
    private async Task<IReadOnlyDictionary<int, string>> PdfsAsync(ISesionDatos s, string idSociedad, IReadOnlyList<int> registros)
    {
        var (_, rutaDocumentos) = await RepositorioFacturasCompra.EmpresaAsync(s, idSociedad);
        var raiz = string.IsNullOrWhiteSpace(rutaDocumentos) ? null : ServicioFacturasCompra.RaizEmpresa(bd.Ruta, rutaDocumentos);
        var rutas = new Dictionary<int, string>();
        foreach (var id in registros)
        {
            var f = await s.ConsultarPrimeroAsync(
                "SELECT fc.ArchivoOriginal, fc.Archivo, fc.Ejercicio, fc.Fecha, se.Ejercicio AS AnioEjercicio, se.CarpetaFacturasCompra " +
                "FROM FacturasCompra AS fc LEFT JOIN SociedadesEjercicios AS se ON fc.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE fc.IdRegistro = ?",
                r => (Rutas: new[] { r.Texto("ArchivoOriginal"), r.Texto("Archivo") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct().ToList(),
                    Ejercicio: r.EnteroNulo("AnioEjercicio") ?? (int.TryParse(r.Texto("Ejercicio"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : r.Fecha("Fecha").Year),
                    Carpeta: r.Texto("CarpetaFacturasCompra")), id);
            if (f.Rutas is not { Count: > 0 }) continue;
            var carpeta = ServicioFacturasCompra.CarpetaEjercicio(raiz, f.Carpeta, f.Ejercicio);
            var ruta = ServicioFacturasCompra.LocalizarPdf(f.Rutas, raiz, carpeta);
            if (!string.IsNullOrWhiteSpace(ruta)) rutas[id] = ruta;
        }
        return rutas;
    }

    private static string Primero(IEnumerable<string?> textos) => textos.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim() ?? "";

    private static string Recortar(string texto, int largo) => texto.Length <= largo ? texto : texto[..largo];
}
