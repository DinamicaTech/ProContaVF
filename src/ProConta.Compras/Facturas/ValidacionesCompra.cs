using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Identificacion;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Validaciones de la factura de compra que avisan sin bloquear (compras/factura-compra, «Derived from contabilidad» del
/// 2026-11-15 21:10, a partir de las incidencias tratadas con la gestoría): V1 tipo de operación frente al país del NIF del
/// proveedor, V2 factura sin retención de un proveedor que suele llevarla y V3 abono sin la factura que rectifica. Sin BD: la
/// ficha las pide con la factura y las demás facturas del ejercicio.
/// </summary>
public static class ValidacionesCompra
{
    /// <summary>Prefijos de país de los NIF-IVA de la Unión Europea (EL es Grecia; XI, Irlanda del Norte para bienes).</summary>
    public static readonly IReadOnlySet<string> PaisesUe = new HashSet<string>(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "HR", "HU", "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK", "XI",
    };

    /// <summary>Mínimo de facturas anteriores del proveedor para deducir que suele llevar retención (V2).</summary>
    public const int MinimoAnteriores = 3;

    /// <summary>
    /// País del NIF: «ES» si es un NIF español válido (con o sin el prefijo ES), el prefijo de dos letras si lo lleva (un NIF
    /// español empieza como mucho por una letra seguida de cifras), o null si no se puede saber.
    /// </summary>
    public static string? PaisNif(string? nif)
    {
        var t = new string((nif ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (t.Length == 0) return null;
        if (Nif.EsValido(t)) return "ES";
        if (t.Length > 2 && char.IsAsciiLetter(t[0]) && char.IsAsciiLetter(t[1])) return t[..2];
        return null;
    }

    public static bool EsUe(string pais) => PaisesUe.Contains(pais);

    /// <summary>Las facturas de compra del ejercicio, como las lista Compras › Facturas recibidas (para la salud de Inicio).</summary>
    public static Task<IReadOnlyList<FilaFacturaCompra>> ListarAsync(IBaseDatos bd, ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(s => RepositorioFacturasCompra.ListarAsync(s, ctx), cancelacion);

    /// <summary>Los avisos V1 a V3 de una factura, con las demás facturas del ejercicio (las del mismo proveedor se eligen aquí).</summary>
    public static IReadOnlyList<string> Avisos(FacturaCompra f, IEnumerable<FilaFacturaCompra> otras)
    {
        var l = new List<string>();
        if (AvisoOperacion(f.Operacion, f.NifProveedor) is { } v1) l.Add(v1);
        var delProveedor = otras.Where(o => o.IdRegistro != f.IdRegistro && MismoProveedor(f.IdProveedor, f.NifProveedor, o)).ToList();
        if (AvisoRetencion(f, delProveedor) is { } v2) l.Add(v2);
        if (AvisoAbono(f) is { } v3) l.Add(v3);
        return l;
    }

    /// <summary>V1: el tipo de operación no cuadra con el país del NIF del proveedor. Null si cuadra o si el país no se sabe.</summary>
    public static string? AvisoOperacion(OperacionCompra operacion, string? nif)
    {
        if (PaisNif(nif) is not { } pais) return null;
        var espanol = pais == "ES";
        var ue = !espanol && EsUe(pais);
        return operacion switch
        {
            OperacionCompra.Nacional when ue =>
                $"El NIF del proveedor es de otro país de la UE ({pais}) y la operación está como nacional: si es una adquisición intracomunitaria, cambia el tipo de operación.",
            OperacionCompra.Nacional when !espanol =>
                $"El NIF del proveedor es de fuera de la UE ({pais}) y la operación está como nacional: revisa si es una importación o una inversión del sujeto pasivo.",
            OperacionCompra.Intracomunitaria when espanol =>
                "La operación está como intracomunitaria, pero el NIF del proveedor es español: si es una compra en España, cambia el tipo de operación a nacional.",
            OperacionCompra.Intracomunitaria when !ue =>
                $"La operación está como intracomunitaria, pero el NIF del proveedor es de fuera de la UE ({pais}): revisa si es una importación.",
            OperacionCompra.Importacion when espanol || ue =>
                $"La operación está como importación, pero el NIF del proveedor es {(espanol ? "español" : $"de la UE ({pais})")}: una importación viene de fuera de la UE; revisa el tipo de operación.",
            _ => null,
        };
    }

    /// <summary>
    /// V2: la factura no lleva retención y el proveedor la llevó en la mayoría de sus facturas anteriores (al menos
    /// <see cref="MinimoAnteriores"/>, sin abonos ni duplicadas). Null si no aplica.
    /// </summary>
    public static string? AvisoRetencion(FacturaCompra f, IReadOnlyList<FilaFacturaCompra> delProveedor)
    {
        if ((f.ImporteRetencion ?? 0m) != 0m || (f.PorcentajeRetencion ?? 0m) != 0m) return null;
        if (f.ImporteTotal is not { } total || total <= 0) return null;
        var anteriores = delProveedor.Where(o => !o.Duplicada && o.ImporteTotal is > 0 && (f.Fecha is null || o.Fecha is null || o.Fecha <= f.Fecha)).ToList();
        if (anteriores.Count < MinimoAnteriores) return null;
        var con = anteriores.Count(o => o.TieneRetencion);
        if (con * 2 <= anteriores.Count) return null;
        return $"Esta factura no lleva retención, pero {con} de las {anteriores.Count} facturas anteriores de este proveedor sí la llevan: " +
               "si el proveedor es un profesional o un arrendador, marca la retención; si esta factura no la lleva, déjala así.";
    }

    /// <summary>V3: un abono (total negativo) sin la factura que rectifica enlazada. Null si no aplica.</summary>
    public static string? AvisoAbono(FacturaCompra f) =>
        f.ImporteTotal is < 0 && f.IdRegistroFacturaRectificada is null
            ? "Es un abono (total negativo) y no tiene enlazada la factura que rectifica: elígela en «Rectifica a la factura» para poder seguirla. Si la original es de otro ejercicio, anótalo en las notas."
            : null;

    /// <summary>Mismo proveedor: por su Id si las dos lo tienen; si no, por el NIF sin separadores.</summary>
    public static bool MismoProveedor(int? idProveedor, string? nif, FilaFacturaCompra o)
    {
        if (idProveedor is { } id && o.IdProveedor is { } otro) return id == otro;
        var a = Compacto(nif);
        return a.Length > 0 && a == Compacto(o.Nif);
    }

    private static string Compacto(string? t) => new((t ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>
    /// S10, posibles compras duplicadas: facturas del mismo proveedor con el mismo total, fechas a menos de
    /// <paramref name="dias"/> días y número distinto (sin las marcadas como duplicadas). Solo informa: dos facturas parecidas
    /// pueden ser correctas. Devuelve cada grupo ordenado por fecha.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<FilaFacturaCompra>> PosiblesDuplicadas(IEnumerable<FilaFacturaCompra> facturas, int dias = 7)
    {
        static string Clave(FilaFacturaCompra f) => f.IdProveedor is { } id ? "P" + id.ToString(CultureInfo.InvariantCulture) : "N" + Compacto(f.Nif);
        var grupos = new List<IReadOnlyList<FilaFacturaCompra>>();
        foreach (var g in facturas.Where(f => !f.Duplicada && f.ImporteTotal is { } t && t != 0 && f.Fecha is not null && (f.IdProveedor is not null || Compacto(f.Nif).Length > 0))
                     .GroupBy(f => (Clave(f), f.ImporteTotal!.Value)))
        {
            var orden = g.OrderBy(f => f.Fecha).ThenBy(f => f.IdRegistro).ToList();
            var usados = new HashSet<int>();
            for (var i = 0; i < orden.Count; i++)
            {
                if (usados.Contains(orden[i].IdRegistro)) continue;
                var grupo = new List<FilaFacturaCompra> { orden[i] };
                for (var j = i + 1; j < orden.Count; j++)
                {
                    if ((orden[j].Fecha!.Value - grupo[^1].Fecha!.Value).TotalDays >= dias) break;
                    if (!usados.Contains(orden[j].IdRegistro) && grupo.All(x => Compacto(x.NumeroFactura) != Compacto(orden[j].NumeroFactura))) grupo.Add(orden[j]);
                }
                if (grupo.Count < 2) continue;
                foreach (var x in grupo) usados.Add(x.IdRegistro);
                grupos.Add(grupo);
            }
        }
        return grupos.OrderBy(g => g[0].Fecha).ToList();
    }
}
