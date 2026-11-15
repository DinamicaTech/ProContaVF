using System.Globalization;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Ratios;

namespace ProConta.Informes.Excel;

/// <summary>Factura emitida del listado (facturacion/factura-venta): base antes de retención y total = base + IVA − retención.</summary>
public sealed record FilaEmitida(
    string Numero, DateTime Fecha, int Trimestre, string? Nif, string Cliente, string? Descripcion, decimal Base, decimal Iva, decimal Retencion,
    decimal Total, string? Centro, string? Departamento, bool Contabilizada);

/// <summary>Factura recibida contabilizada del listado (compras/factura-compra), con la ruta de su PDF si la tiene.</summary>
public sealed record FilaRecibida(
    DateTime Fecha, int Trimestre, int? Asiento, string? Nif, string Proveedor, string Numero, string Operacion, bool Isp, decimal Base, decimal Iva,
    decimal Retencion, string? Centro, string? Area, string? RutaPdf, bool PdfExiste);

/// <summary>Cuotas de los libros de IVA frente al saldo de las 477 y 472 de un trimestre (las mismas cifras que la salud de Inicio).</summary>
public sealed record CuadreLibrosIva(int Trimestre, decimal LibroRepercutido, decimal Cuentas477, decimal LibroSoportado, decimal Cuentas472);

/// <summary>
/// Todos los datos del informe para la gestoría de un trimestre, ya calculados (informes/informe-gestoria): el libro Excel solo
/// los presenta. Cada cifra la calcula el nodo dueño (303 y prorrata en contabilidad, balances, ratios, listados de facturación
/// y compras); aquí solo se juntan con el sello.
/// </summary>
/// <param name="ProrrataProvisional">La del ejercicio en tanto por uno, o null si no está informada (el cálculo usa 100 %).</param>
/// <param name="UltimoAsiento">Fecha del último asiento contabilizado del ejercicio incluido en los datos.</param>
/// <param name="AsientosFueraEjercicio">Asientos del ejercicio con fecha contable de otro año.</param>
/// <param name="ActivosAmortizables">Activos amortizables, propios y sin dar de baja (para el control de amortizaciones).</param>
public sealed record InformeGestoria(
    string RazonSocial, int Ejercicio, int Trimestre, DateTime Generado, DateTime? UltimoAsiento, decimal? ProrrataProvisional,
    IReadOnlyList<LineaIva> LineasIva, Resultado303 Iva303, ResultadoProrrata Prorrata,
    ResultadoBalance PyG, ResultadoBalance Situacion, ResultadoRatios? Ratios, string? MotivoSinRatios,
    IReadOnlyList<FilaEmitida> Emitidas, IReadOnlyList<FilaRecibida> Recibidas, IReadOnlyList<CuadreLibrosIva> CuadreLibros,
    int AsientosFueraEjercicio, int ActivosAmortizables)
{
    /// <summary>Último día del trimestre del informe.</summary>
    public DateTime FinTrimestre => FinDe(Ejercicio, Trimestre);

    public static DateTime FinDe(int ejercicio, int trimestre) => new DateTime(ejercicio, trimestre * 3, 1).AddMonths(1).AddDays(-1);

    /// <summary>
    /// Trimestre que se propone al generar: el último terminado del ejercicio a la fecha de hoy (si el ejercicio ya acabó, el 4.º;
    /// si aún no ha terminado ninguno, el 1.º).
    /// </summary>
    public static int TrimestrePropuesto(int ejercicio, DateTime hoy)
    {
        if (hoy.Year > ejercicio) return 4;
        if (hoy.Year < ejercicio) return 1;
        return Math.Max(1, (hoy.Month - 1) / 3);
    }

    /// <summary>Nombre del fichero: «InformeGestoria_2026-3T_20261115-183000.xlsx».</summary>
    public static string NombreFichero(int ejercicio, int trimestre, DateTime ahora) =>
        $"InformeGestoria_{ejercicio}-{trimestre}T_{ahora.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.xlsx";
}
