using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Activos.Gestion;

/// <summary>Una factura de compra del apartado «Facturas» de la ficha.</summary>
public sealed record CompraDeActivo(int IdRegistro, DateTime? Fecha, string Numero, string Proveedor, string Concepto, decimal Base, decimal Iva, decimal Total,
    string Estado, int? Ejercicio);

/// <summary>Una factura programada del apartado «Ventas»: <paramref name="Importe"/> es la base de sus líneas del activo.</summary>
public sealed record ProgramadaDeActivo(int IdFacturaProgramada, string Cliente, string Periodicidad, DateTime? ProximaEmision, decimal Importe, bool Activa)
{
    public string Rotulo => $"P-{IdFacturaProgramada:0000}";
    public string TextoActiva => Activa ? "Sí" : "No";
}

/// <summary>Una factura emitida o borrador del apartado «Ventas»: <paramref name="BaseActivo"/> es la base de sus líneas del activo.</summary>
public sealed record VentaDeActivo(int IdFactura, DateTime? Fecha, string Numero, string Cliente, decimal BaseActivo, decimal Total, string Estado, int? Ejercicio);

/// <summary>El apartado «Ventas»: las programadas arriba y las facturas debajo.</summary>
public sealed record VentasDeActivo(IReadOnlyList<ProgramadaDeActivo> Programadas, IReadOnlyList<VentaDeActivo> Facturas);

/// <summary>
/// Reglas de los apartados «Facturas» y «Ventas» de la ficha (activos 2026-11-17 20:35), sin base de datos.
/// </summary>
public static class ReglasEnlacesActivo
{
    /// <summary>
    /// Una factura de compra es del activo si su cuenta de gasto es una subcuenta del activo (con su código en el ejercicio de la factura) o si su
    /// centro de coste es el del activo; con el centro compartido por varios activos, solo por subcuenta (la misma regla que la cuenta de resultados).
    /// </summary>
    public static bool EsCompraDelActivo(string? cuentaGasto, int? centroFactura, IReadOnlySet<string> subcuentasEnEjercicio, int? centroActivo, bool centroCompartido) =>
        cuentaGasto is { Length: > 0 } c && subcuentasEnEjercicio.Contains(c.Trim())
        || !centroCompartido && centroActivo is int ca && centroFactura == ca;

    /// <summary>
    /// Una programada entra en un ejercicio si emite (o puede emitir) en él: empieza antes de que acabe el año y no termina antes de que empiece.
    /// Sin ejercicio («Todos»), entran todas.
    /// </summary>
    public static bool ProgramadaEnEjercicio(DateTime? inicio, DateTime? fin, int? ejercicio) =>
        ejercicio is not int a
        || (inicio is null || inicio.Value.Date <= new DateTime(a, 12, 31)) && (fin is null || fin.Value.Date >= new DateTime(a, 1, 1));

    /// <summary>Base de una línea de programada, con el cálculo de la factura: cantidad × precio, menos el % y después el descuento fijo, a céntimos.</summary>
    public static decimal BaseLinea(decimal cantidad, decimal precio, decimal descuentoPorcentaje, decimal descuentoImporte)
    {
        var bruto = cantidad * precio;
        return Math.Round(bruto - bruto * descuentoPorcentaje / 100m - descuentoImporte, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>Estado de una factura de compra: contabilizada (exportada) o pendiente de contabilizar.</summary>
    public static string EstadoCompra(bool exportada) => exportada ? "Contabilizada" : "Pendiente de contabilizar";

    /// <summary>Estado de una factura de venta (Facturas.EstadoFactura), con los textos del listado de facturas.</summary>
    public static string EstadoVenta(int estado) => estado switch
    {
        3 => "Borrador",
        0 => "Pendiente de validar",
        1 => "Validada",
        4 => "Rectificación pendiente",
        _ => "Rectificada",
    };

    /// <summary>Texto de la periodicidad de una programada (FacturasProgramadas.Periodicidad).</summary>
    public static string Periodicidad(string? codigo) => (codigo ?? "").Trim().ToUpperInvariant() switch
    {
        "BIMESTRAL" => "Bimestral",
        "TRIMESTRAL" => "Trimestral",
        "CUATRIMESTRAL" => "Cuatrimestral",
        "SEMESTRAL" => "Semestral",
        "ANUAL" => "Anual",
        "PUNTUAL" => "Una sola vez",
        _ => "Mensual",
    };
}

public sealed partial class ServicioActivos
{
    /// <summary>
    /// Apartado «Facturas»: las facturas de compra del activo (por su cuenta de gasto o su centro; con centro compartido, solo por subcuenta) de un
    /// ejercicio, o de todos con <paramref name="ejercicio"/> null. Sin las marcadas como duplicadas.
    /// </summary>
    public Task<IReadOnlyList<CompraDeActivo>> ComprasAsync(string idSociedad, int idActivo, int? ejercicio) =>
        _bd.EjecutarAsync<IReadOnlyList<CompraDeActivo>>(async s =>
        {
            var activo = await s.ConsultarPrimeroAsync("SELECT IdCentroCoste, EsAjeno FROM Activos WHERE IdActivo = ? AND IdSociedad = ?",
                f => (Centro: f.EnteroNulo("IdCentroCoste"), Ajeno: f.Logico("EsAjeno")), idActivo, idSociedad);
            // Un activo ajeno no tiene estos apartados.
            if (activo.Ajeno) return [];
            var compartido = activo.Centro is int c && Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Activos WHERE IdSociedad = ? AND IdCentroCoste = ?", idSociedad, c),
                CultureInfo.InvariantCulture) > 1;
            var ejercicios = (await s.ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio FROM SociedadesEjercicios WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdSociedadEjercicio"), Anio: f.Entero("Ejercicio")), idSociedad)).ToDictionary(x => x.Id, x => x.Anio);
            var subcuentas = (await s.ConsultarAsync(
                    "SELECT c.IdSociedadEjercicio, c.Codigo FROM SubcuentasActivo AS sa INNER JOIN CuentasContables AS c ON sa.IdIdentidadContable = c.IdIdentidadContable WHERE sa.IdActivo = ?",
                    f => (Ejercicio: f.Entero("IdSociedadEjercicio"), Codigo: (f.Texto("Codigo") ?? "").Trim()), idActivo))
                .Where(x => ejercicios.ContainsKey(x.Ejercicio))
                .GroupBy(x => ejercicios[x.Ejercicio]).ToDictionary(g => g.Key, g => (IReadOnlySet<string>)g.Select(x => x.Codigo).ToHashSet(StringComparer.Ordinal));
            IReadOnlySet<string> ninguna = new HashSet<string>();
            var filas = await s.ConsultarAsync(
                "SELECT fc.IdRegistro, fc.IdSociedadEjercicio, fc.Ejercicio, fc.Fecha, fc.NumeroFactura, fc.NombreProveedorAeat, t.RazonSocial, t.Nombre, fc.Concepto, " +
                "fc.ImporteBI, fc.ImporteIVA, fc.ImporteTotal, fc.Exportada, fc.CuentaGasto, fc.IdCentroCoste " +
                "FROM (FacturasCompra AS fc LEFT JOIN Proveedores AS p ON fc.IdProveedor = p.IdProveedor) LEFT JOIN Terceros AS t ON p.IdTercero = t.IdTercero " +
                "WHERE fc.Duplicada = False AND (fc.IdSociedadEjercicio IN (SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ?) " +
                "OR (fc.IdSociedadEjercicio IS NULL AND fc.Sociedad = ?))",
                f =>
                {
                    int? anio = f.EnteroNulo("IdSociedadEjercicio") is int ie && ejercicios.TryGetValue(ie, out var a) ? a
                        : int.TryParse(f.Texto("Ejercicio")?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var e) ? e : null;
                    var proveedor = new[] { f.Texto("NombreProveedorAeat"), f.Texto("RazonSocial"), f.Texto("Nombre") }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";
                    return (Anio: anio, Cuenta: f.Texto("CuentaGasto")?.Trim(), Centro: f.EnteroNulo("IdCentroCoste"),
                        Fila: new CompraDeActivo(f.Entero("IdRegistro"), f.FechaNula("Fecha"), (f.Texto("NumeroFactura") ?? "").Trim(), proveedor, (f.Texto("Concepto") ?? "").Trim(),
                            f.DecimalNulo("ImporteBI") ?? 0m, f.DecimalNulo("ImporteIVA") ?? 0m, f.DecimalNulo("ImporteTotal") ?? 0m,
                            ReglasEnlacesActivo.EstadoCompra(f.Logico("Exportada")), anio));
                },
                idSociedad, idSociedad);
            return filas
                .Where(x => (ejercicio is null || x.Anio == ejercicio)
                    && ReglasEnlacesActivo.EsCompraDelActivo(x.Cuenta, x.Centro, x.Anio is int a && subcuentas.TryGetValue(a, out var sc) ? sc : ninguna, activo.Centro, compartido))
                .Select(x => x.Fila)
                .OrderByDescending(x => x.Fecha ?? DateTime.MinValue).ThenByDescending(x => x.IdRegistro).ToList();
        });

    /// <summary>
    /// Apartado «Ventas»: las programadas con alguna línea del activo que emiten en el ejercicio y las facturas emitidas y borradores del ejercicio
    /// con alguna línea del activo (todas con <paramref name="ejercicio"/> null). Los importes del activo son la base de sus líneas.
    /// </summary>
    public Task<VentasDeActivo> VentasAsync(string idSociedad, int idActivo, int? ejercicio) =>
        _bd.EjecutarAsync(async s =>
        {
            static string Cliente(IFilaDatos f, string razon, string nombre) =>
                new[] { f.Texto(razon), f.Texto(nombre) }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? "";

            var lineasProgramadas = await s.ConsultarAsync(
                "SELECT IdFacturaProgramada, Cantidad, PrecioUnitario, DescuentoPorcentaje, DescuentoImporte FROM LineasFacturasProgramadas WHERE IdActivo = ? AND Activa = True",
                f => (Id: f.Entero("IdFacturaProgramada"), Base: ReglasEnlacesActivo.BaseLinea(Math.Round((decimal)f.Doble("Cantidad"), 6), f.Decimal("PrecioUnitario"),
                    f.EsNulo("DescuentoPorcentaje") ? 0m : Math.Round((decimal)f.Doble("DescuentoPorcentaje"), 2), f.DecimalNulo("DescuentoImporte") ?? 0m)), idActivo);
            var basePorProgramada = lineasProgramadas.GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.Base));
            var programadas = (await s.ConsultarAsync(
                    "SELECT p.IdFacturaProgramada, p.Periodicidad, p.FechaInicio, p.FechaFin, p.ProximaFechaEmision, p.Activa, t.RazonSocial, t.Nombre " +
                    "FROM (((FacturasProgramadas AS p INNER JOIN SociedadesEjercicios AS se ON p.IdSociedadEjercicio = se.IdSociedadEjercicio) " +
                    "LEFT JOIN Clientes AS c ON p.IdCliente = c.IdCliente) LEFT JOIN Terceros AS t ON c.IdTercero = t.IdTercero) " +
                    "WHERE se.IdSociedad = ? AND p.IdFacturaProgramada IN (SELECT IdFacturaProgramada FROM LineasFacturasProgramadas WHERE IdActivo = ? AND Activa = True)",
                    f => (Inicio: f.FechaNula("FechaInicio"), Fin: f.FechaNula("FechaFin"),
                        Fila: new ProgramadaDeActivo(f.Entero("IdFacturaProgramada"), Cliente(f, "RazonSocial", "Nombre"), ReglasEnlacesActivo.Periodicidad(f.Texto("Periodicidad")),
                            f.FechaNula("ProximaFechaEmision"), basePorProgramada.GetValueOrDefault(f.Entero("IdFacturaProgramada")), f.Logico("Activa"))),
                    idSociedad, idActivo))
                .Where(x => ReglasEnlacesActivo.ProgramadaEnEjercicio(x.Inicio, x.Fin, ejercicio))
                .Select(x => x.Fila).OrderByDescending(x => x.Activa).ThenBy(x => x.ProximaEmision ?? DateTime.MaxValue).ThenBy(x => x.IdFacturaProgramada).ToList();

            var basePorFactura = (await s.ConsultarAsync("SELECT IdFactura, BaseImponible FROM LineasFactura WHERE IdActivo = ?",
                    f => (Id: f.Entero("IdFactura"), Base: f.DecimalNulo("BaseImponible") ?? 0m), idActivo))
                .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.Sum(x => x.Base));
            var facturas = (await s.ConsultarAsync(
                    "SELECT f.IdFactura, f.NumeroFactura, f.FechaFactura, f.EstadoFactura, f.ImporteTotal, f.RazonSocial, f.Nombre, t.RazonSocial AS RazonTercero, t.Nombre AS NombreTercero, se.Ejercicio " +
                    "FROM (((Facturas AS f INNER JOIN SociedadesEjercicios AS se ON f.IdSociedadEjercicio = se.IdSociedadEjercicio) " +
                    "LEFT JOIN Clientes AS c ON f.IdCliente = c.IdCliente) LEFT JOIN Terceros AS t ON c.IdTercero = t.IdTercero) " +
                    "WHERE se.IdSociedad = ? AND f.IdFactura IN (SELECT IdFactura FROM LineasFactura WHERE IdActivo = ?)" + (ejercicio is null ? "" : " AND se.Ejercicio = ?"),
                    f =>
                    {
                        var numero = f.Texto("NumeroFactura")?.Trim();
                        var cliente = numero is { Length: > 0 } ? Cliente(f, "RazonSocial", "Nombre") : "";
                        if (cliente.Length == 0) cliente = Cliente(f, "RazonTercero", "NombreTercero");
                        var estado = Convert.ToInt32(f.Valor("EstadoFactura"), CultureInfo.InvariantCulture);
                        return new VentaDeActivo(f.Entero("IdFactura"), f.FechaNula("FechaFactura"), numero is { Length: > 0 } ? numero : "(borrador)", cliente,
                            basePorFactura.GetValueOrDefault(f.Entero("IdFactura")), f.DecimalNulo("ImporteTotal") ?? 0m, ReglasEnlacesActivo.EstadoVenta(estado), f.Entero("Ejercicio"));
                    },
                    ejercicio is int a ? [idSociedad, idActivo, a] : [idSociedad, idActivo]))
                .OrderByDescending(x => x.Estado == "Borrador").ThenByDescending(x => x.Fecha ?? DateTime.MaxValue).ThenByDescending(x => x.IdFactura).ToList();
            return new VentasDeActivo(programadas, facturas);
        });
}
