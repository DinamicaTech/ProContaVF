using System.Globalization;
using ProConta.Norma43.Asignacion;

namespace ProConta.Norma43.Lineas;

/// <summary>Filtro de estado de la pantalla de líneas N43.</summary>
public enum FiltroEstadoN43
{
    Todas,
    /// <summary>Sin contabilizar: sin cuenta o con cuenta pendiente.</summary>
    Pendientes,
    SinCuenta,
    ConCuenta,
    /// <summary>Contabilizadas, por ProContaVF o en el ERP.</summary>
    Contabilizadas,
}

/// <summary>
/// Filtros de la pantalla de líneas N43 (norma43/pantallas), combinados en Y: estado, cuenta bancaria, texto, importe y fechas.
/// Los ejercicios no van aquí: deciden qué líneas se leen. Cada filtro activo es una ficha que se quita por separado.
/// </summary>
public sealed record FiltroLineasN43(
    FiltroEstadoN43 Estado = FiltroEstadoN43.Todas,
    int? IdCuentaBancaria = null,
    string? Texto = null,
    decimal? Importe = null,
    DateTime? Desde = null,
    DateTime? Hasta = null)
{
    public static readonly FiltroLineasN43 Ninguno = new();

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>True si la línea pasa todos los filtros activos.</summary>
    public bool Cumple(LineaN43 l)
    {
        ArgumentNullException.ThrowIfNull(l);
        var estado = Estado switch
        {
            FiltroEstadoN43.Pendientes => l.Estado is EstadoLineaN43.SinCuenta or EstadoLineaN43.ConCuenta,
            FiltroEstadoN43.SinCuenta => l.Estado == EstadoLineaN43.SinCuenta,
            FiltroEstadoN43.ConCuenta => l.Estado == EstadoLineaN43.ConCuenta,
            FiltroEstadoN43.Contabilizadas => l.Estado is EstadoLineaN43.Contabilizada or EstadoLineaN43.ContabilizadaErp,
            _ => true,
        };
        if (!estado) return false;
        if (IdCuentaBancaria is int c && l.IdCuentaBancaria != c) return false;
        // El importe se compara sin signo (cargo o abono), al céntimo.
        if (Importe is decimal i && Math.Round(Math.Abs(i), 2) != Math.Round(l.Importe, 2)) return false;
        if (Desde is DateTime d && l.FechaOperacion < d.Date) return false;
        if (Hasta is DateTime h && l.FechaOperacion > h.Date) return false;
        if (!string.IsNullOrWhiteSpace(Texto) && !CumpleTexto(l, Texto)) return false;
        return true;
    }

    /// <summary>
    /// El texto se busca, sin distinguir mayúsculas, acentos ni signos, en el concepto, las referencias, el documento, el tipo, el banco y
    /// las cuentas de origen y destino (código y nombre); varias palabras separadas por espacios deben aparecer todas.
    /// </summary>
    private static bool CumpleTexto(LineaN43 l, string texto)
    {
        var donde = ReglasPalabrasClave.Normalizar(string.Join(" ", l.TextoBusqueda, l.Tipo, l.NombreBanco, l.CuentaOrigen, l.NombreCuentaOrigen,
            string.Join(" ", l.Destinos.Select(d => $"{d.Cuenta} {d.Nombre}"))));
        return texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(ReglasPalabrasClave.Normalizar).Where(p => p.Length > 0).All(p => donde.Contains(p, StringComparison.Ordinal));
    }

    /// <summary>Fichas de los filtros activos, en orden, con el texto que se ve y la clave para quitarlas.</summary>
    public IReadOnlyList<(string Clave, string Texto)> Fichas(Func<int, string>? nombreBanco = null)
    {
        var r = new List<(string, string)>();
        if (Estado != FiltroEstadoN43.Todas) r.Add(("estado", "Estado: " + TextoEstado(Estado)));
        if (IdCuentaBancaria is int c) r.Add(("banco", "Banco: " + (nombreBanco?.Invoke(c) ?? c.ToString(CultureInfo.InvariantCulture))));
        if (!string.IsNullOrWhiteSpace(Texto)) r.Add(("texto", $"Texto: «{Texto.Trim()}»"));
        if (Importe is decimal i) r.Add(("importe", "Importe: " + Math.Abs(i).ToString("N2", Es) + " €"));
        if (Desde is DateTime d) r.Add(("desde", "Desde " + d.ToString("dd/MM/yyyy", Es)));
        if (Hasta is DateTime h) r.Add(("hasta", "Hasta " + h.ToString("dd/MM/yyyy", Es)));
        return r;
    }

    /// <summary>El mismo filtro sin la ficha indicada.</summary>
    public FiltroLineasN43 Sin(string clave) => clave switch
    {
        "estado" => this with { Estado = FiltroEstadoN43.Todas },
        "banco" => this with { IdCuentaBancaria = null },
        "texto" => this with { Texto = null },
        "importe" => this with { Importe = null },
        "desde" => this with { Desde = null },
        "hasta" => this with { Hasta = null },
        _ => this,
    };

    public static string TextoEstado(FiltroEstadoN43 e) => e switch
    {
        FiltroEstadoN43.Pendientes => "pendientes de contabilizar",
        FiltroEstadoN43.SinCuenta => "sin cuenta",
        FiltroEstadoN43.ConCuenta => "con cuenta, sin contabilizar",
        FiltroEstadoN43.Contabilizadas => "contabilizadas",
        _ => "todas",
    };

    public static string TextoEstado(EstadoLineaN43 e) => e switch
    {
        EstadoLineaN43.SinCuenta => "Sin cuenta",
        EstadoLineaN43.ConCuenta => "Por contabilizar",
        EstadoLineaN43.Contabilizada => "Contabilizada",
        _ => "Contabilizada en el ERP",
    };
}

/// <summary>
/// Qué acciones del menú contextual admite una selección de líneas (norma43/pantallas) y, si no la admiten, por qué (el texto va en el
/// tooltip de la opción desactivada). Una sola regla para el menú, los atajos y las pruebas.
/// </summary>
public sealed record AccionesLineasN43(
    string? AsignarCuenta,
    string TextoContabilizar, string? Contabilizar, IReadOnlyList<int> AContabilizar,
    string? Deshacer, IReadOnlyList<int> ADeshacer,
    string? ActualizarTextos,
    string TextoReasignar, string? Reasignar,
    string? PalabrasClave,
    string? Traspaso,
    string? VerAsiento)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    /// <summary>Evalúa las acciones para la selección (las filas seleccionadas que se ven).</summary>
    public static AccionesLineasN43 Evaluar(IReadOnlyList<LineaN43> sel)
    {
        ArgumentNullException.ThrowIfNull(sel);
        var n = sel.Count;
        var ninguna = n == 0 ? "Selecciona una o varias líneas (Ctrl o Mayús para elegir varias)." : null;
        var cerrada = sel.FirstOrDefault(l => l.EjercicioCerrado);
        var soloLectura = cerrada is null ? null : $"El ejercicio {cerrada.Ejercicio} está cerrado: sus líneas solo se consultan. Quítalas de la selección o reabre el ejercicio.";
        var erp = sel.FirstOrDefault(l => l.Estado == EstadoLineaN43.ContabilizadaErp);
        var porErp = erp is null ? null
            : $"El {Minuscula(erp.Rotulo)} se contabilizó en el ERP (asiento {erp.IdAsiento}): no se toca desde aquí. Corrígelo desde el Mayor con doble clic en su cuenta.";

        var asignar = ninguna ?? soloLectura ?? porErp;

        var aContabilizar = sel.Where(l => l.Estado is EstadoLineaN43.ConCuenta or EstadoLineaN43.Contabilizada).Select(l => l.Id).ToList();
        var todasYa = aContabilizar.Count > 0 && sel.Where(l => aContabilizar.Contains(l.Id)).All(l => l.Estado == EstadoLineaN43.Contabilizada);
        var textoContabilizar = (todasYa ? "Recontabilizar" : "Contabilizar") + (n > 1 && aContabilizar.Count > 0 ? $" ({aContabilizar.Count})" : "");
        var contabilizar = ninguna ?? soloLectura ?? (aContabilizar.Count == 0
            ? sel.All(l => l.Estado == EstadoLineaN43.SinCuenta)
                ? (n == 1 ? "La línea no tiene cuenta" : "Ninguna línea tiene cuenta") + ": asígnasela con «Asignar cuenta…» (F2) o «Reasignar por palabras clave»."
                : "Ninguna línea se puede contabilizar: las contabilizadas en el ERP se corrigen desde el Mayor y las que no tienen cuenta necesitan una (F2)."
            : null);

        var propias = sel.Where(l => l.Estado == EstadoLineaN43.Contabilizada).Select(l => l.Id).ToList();
        var sinPropias = propias.Count == 0
            ? (erp is not null ? "Solo hay líneas contabilizadas en el ERP: su asiento no es de ProContaVF y se corrige desde el Mayor."
                : n == 1 ? "La línea no está contabilizada." : "Ninguna línea seleccionada está contabilizada.")
            : null;
        var deshacer = ninguna ?? soloLectura ?? sinPropias;
        var actualizar = ninguna ?? soloLectura ?? sinPropias;

        // Sin selección, la reasignación actúa sobre todas las pendientes sin cuenta; con selección, sobre las seleccionadas sin asiento.
        var textoReasignar = n == 0 ? "Reasignar por palabras clave (todas las pendientes)" : "Reasignar por palabras clave" + (n > 1 ? $" ({n})" : "");
        var reasignar = soloLectura ?? (n > 0 && sel.All(l => l.IdAsiento is not null)
            ? "Las líneas contabilizadas no se reasignan: deshaz antes su asiento si quieres cambiarles la cuenta."
            : null);

        var palabras = n != 1 ? (n == 0 ? "Selecciona una línea." : "Las palabras clave se crean desde una sola línea: deja seleccionada solo una.") : null;

        string? traspaso;
        if (n != 2) traspaso = "Selecciona exactamente dos líneas: el cargo en un banco y el abono en el otro.";
        else
        {
            var (a, b) = (sel[0], sel[1]);
            traspaso = soloLectura
                ?? (a.Sentido == b.Sentido ? $"Las dos líneas son {(a.EsCargo ? "cargos" : "abonos")}: un traspaso es un cargo en un banco y un abono en el otro." : null)
                ?? (a.Importe != b.Importe ? $"Los importes no coinciden ({a.Importe.ToString("N2", Es)} € y {b.Importe.ToString("N2", Es)} €): un traspaso mueve el mismo importe." : null)
                ?? (a.IdCuentaBancaria is not null && a.IdCuentaBancaria == b.IdCuentaBancaria ? "Las dos líneas son de la misma cuenta bancaria: un traspaso va de un banco a otro." : null)
                ?? (a.IdAsiento is not null || b.IdAsiento is not null ? "Alguna de las dos ya está contabilizada: deshaz antes su asiento (solo si lo generó ProContaVF)." : null);
        }

        var verAsiento = n != 1 ? (n == 0 ? "Selecciona una línea." : "Se abre el asiento de una sola línea.") : null;

        return new AccionesLineasN43(asignar, textoContabilizar, contabilizar, aContabilizar, deshacer, propias, actualizar, textoReasignar, reasignar, palabras, traspaso, verAsiento);
    }

    private static string Minuscula(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}
