using System.Globalization;
using System.Text;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Mayor;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Diario;

/// <summary>Un ejercicio de la sociedad de trabajo, para el Diario: si está cerrado y su trimestre activo.</summary>
public sealed record EjercicioDiario(int IdSociedadEjercicio, int Ejercicio, bool Cerrado, int? TrimestreActivo)
{
    /// <summary>Primer día del trimestre activo, o null si no hay trimestre configurado (no se comprueba el trimestre cerrado).</summary>
    public DateTime? InicioTrimestreActivo => TrimestreActivo is >= 1 and <= 4 ? TrimestreContable.Inicio(Ejercicio, TrimestreActivo.Value) : null;
}

/// <summary>
/// Origen de un asiento en el Diario (contabilidad/diario 2026-11-14 21:50): «Manual» o el módulo que lo generó, por el motor
/// propietario de su lote. Los que no son «Manual» son automáticos.
/// </summary>
public static class OrigenesDiario
{
    public const string Manual = "Manual";
    public const string Ventas = "Ventas";
    public const string Compras = "Compras";
    public const string Bancos = "Bancos";
    public const string Nominas = "Nóminas";
    public const string ContaSol = "ContaSol";
    public const string Procesos = "Procesos";

    /// <summary>Todos, en el orden del filtro.</summary>
    public static readonly IReadOnlyList<string> Todos = [Manual, Ventas, Compras, Bancos, Nominas, ContaSol, Procesos];

    public static string De(bool automatico, string? motorPropietario) => !automatico ? Manual : (motorPropietario ?? "").Trim() switch
    {
        "facturacion/factura-venta" => Ventas,
        "compras/factura-compra" => Compras,
        "norma43/conciliacion" => Bancos,
        "nominas" => Nominas,
        OpcionesLote.MotorImportadorContaSol => ContaSol,
        _ => Procesos,
    };
}

/// <summary>Un apunte del Diario con lo que muestran sus columnas.</summary>
public sealed class ApunteDiario
{
    public required int IdApunte { get; init; }
    public required int Orden { get; init; }
    public required string Cuenta { get; init; }
    public string NombreCuenta { get; init; } = "";
    public decimal Debe { get; init; }
    public decimal Haber { get; init; }
    /// <summary>Concepto propio del apunte; null si usa la descripción del asiento.</summary>
    public string? ConceptoPropio { get; init; }
    public string? Documento { get; init; }
    public DateTime? Vencimiento { get; init; }
    public int? IdCentroCoste { get; init; }
    public string CentroCoste { get; init; } = "";
    public bool Punteado { get; set; }
    public AsientoDiario Asiento { get; internal set; } = null!;

    public string Concepto => string.IsNullOrWhiteSpace(ConceptoPropio) ? Asiento.Descripcion : ConceptoPropio.Trim();
    public decimal Importe => Debe != 0 ? Debe : Haber;

    private IReadOnlyList<string>? _contrapartidas;
    /// <summary>Cuentas de contrapartida, con la misma regla que el Mayor (<see cref="Contrapartidas"/>); la apertura no tiene.</summary>
    public IReadOnlyList<string> CuentasContrapartida => _contrapartidas ??= Asiento.TipoAsiento == TiposAsientoMayor.Apertura
        ? []
        : Contrapartidas.Candidatas(Cuenta, Orden, Debe, Asiento.Apuntes.Select(a => new LineaContrapartida(Asiento.IdAsiento, a.Orden, a.Cuenta, a.Debe, a.Haber, a.IdApunte)).ToList());
    public string Contrapartida => Contrapartidas.Texto(CuentasContrapartida);

    /// <summary>
    /// Por qué no se puede cambiar a mano la cuenta ni el importe de este apunte, o null si se puede. El motor aplica las mismas reglas
    /// (<see cref="ReglasAsiento.BloqueoCuentaAutomatico"/>, el trimestre y el ejercicio cerrados).
    /// </summary>
    public string? BloqueoCuenta => Asiento.BloqueoPeriodo ?? (Asiento.EsAutomatico ? ReglasAsiento.BloqueoCuentaAutomatico(Cuenta, Asiento.ConLibroIva) : null);

    /// <summary>Iconos de la columna «Bloqueos» con su explicación (contabilidad/diario 2026-11-14 21:50).</summary>
    public IReadOnlyList<(string Icono, string Ayuda)> Bloqueos
    {
        get
        {
            var lista = new List<(string, string)>();
            if (Asiento.BloqueoPeriodo is { } periodo) lista.Add(("🔒", periodo));
            if (Asiento.EsAutomatico && ReglasAsiento.EsCuentaBanco(Cuenta))
                lista.Add(("🏦", "Apunte de banco (572) de un asiento automático: su cuenta y su importe no se cambian."));
            if (Asiento.ConLibroIva)
                lista.Add(("🧾", ReglasAsiento.EsCuentaIvaFactura(Cuenta)
                    ? "Línea de IVA o de retención de una factura: se corrige en la factura, para que el libro de IVA siga cuadrando."
                    : "Va con el libro de IVA de una factura: se puede cambiar la subcuenta, pero el importe se corrige en la factura."));
            return lista;
        }
    }
}

/// <summary>Un asiento del Diario con sus apuntes.</summary>
public sealed class AsientoDiario
{
    public required int IdAsiento { get; init; }
    public required EjercicioDiario Ejercicio { get; init; }
    public required DateTime Fecha { get; init; }
    public required string Descripcion { get; init; }
    public byte TipoAsiento { get; init; }
    public string? Notas { get; init; }
    public bool EsAutomatico { get; init; }
    public string? MotorPropietario { get; init; }
    public string? CodigoProceso { get; init; }
    public string? ClaveEjecucion { get; init; }
    public bool CorregidoAMano { get; init; }
    /// <summary>El asiento tiene una fila en el libro de IVA (repercutido o soportado): es de una factura.</summary>
    public bool ConLibroIva { get; init; }
    public DateTime? FechaDocumento { get; init; }
    public string? OperadorCreacion { get; init; }
    public DateTime? GrabadoEl { get; init; }
    public string? OperadorModificacion { get; init; }
    public DateTime? ModificadoEl { get; init; }
    public int Version { get; init; }
    public string? Plantilla { get; init; }
    public List<ApunteDiario> Apuntes { get; } = [];

    /// <summary>Añade apuntes al asiento (los enlaza con él) y lo devuelve.</summary>
    public AsientoDiario Con(params ApunteDiario[] apuntes)
    {
        foreach (var a in apuntes)
        {
            a.Asiento = this;
            Apuntes.Add(a);
        }
        return this;
    }

    public string Origen => OrigenesDiario.De(EsAutomatico, MotorPropietario);
    public decimal TotalDebe => Apuntes.Sum(a => a.Debe);
    public decimal TotalHaber => Apuntes.Sum(a => a.Haber);

    /// <summary>Texto del origen de un automático para la ficha: el módulo y la clave de su documento.</summary>
    public string? Referencia => !EsAutomatico ? null
        : string.Join(" · ", new[] { Origen, CodigoProceso, ClaveEjecucion }.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t!.Trim()));

    /// <summary>El asiento es de un trimestre anterior al activo y lleva cuentas fiscales (IVA o retenciones).</summary>
    public bool TrimestreCerrado =>
        Ejercicio.InicioTrimestreActivo is { } inicio && Fecha.Date < inicio && ReglasAsiento.AfectaTrimestreFiscal(Apuntes.Select(a => a.Cuenta)) is not null;

    /// <summary>Por qué no se cambian cuentas ni importes ni se borra el asiento por su periodo (ejercicio o trimestre cerrado), o null.</summary>
    public string? BloqueoPeriodo =>
        Ejercicio.Cerrado ? $"El ejercicio {Ejercicio.Ejercicio} está cerrado: no se cambian cuentas ni importes, ni se borra."
        : TrimestreCerrado ? "Trimestre cerrado con cuentas de IVA o retenciones: no se cambian cuentas ni importes, ni se borra. Se corrige con un contra-asiento en el trimestre activo."
        : null;
}

/// <summary>Lo que carga la fuente para unos ejercicios: los asientos con sus apuntes, por fecha y número.</summary>
public sealed record DatosDiario(IReadOnlyList<EjercicioDiario> Ejercicios, IReadOnlyList<AsientoDiario> Asientos)
{
    public static readonly DatosDiario Vacio = new([], []);
}

/// <summary>Filtro del Diario (contabilidad/diario): el periodo y el origen son del asiento; el resto, de cada apunte.</summary>
public sealed record FiltroDiario
{
    public DateTime? Desde { get; init; }
    public DateTime? Hasta { get; init; }
    /// <summary>Texto que debe contener el apunte (descripción del asiento, concepto, documento, cuenta, su nombre o el importe); «#125» es el asiento 125.</summary>
    public string? Texto { get; init; }
    /// <summary>Subcuenta exacta o principio del código.</summary>
    public string? Cuenta { get; init; }
    public string? Origen { get; init; }
    public bool? Punteado { get; init; }
    public int? IdCentroCoste { get; init; }

    /// <summary>True si algún filtro actúa sobre los apuntes (en la vista por asiento, el resto del asiento se ve apagado).</summary>
    public bool FiltraApuntes => !string.IsNullOrWhiteSpace(Texto) || !string.IsNullOrWhiteSpace(Cuenta) || Punteado is not null || IdCentroCoste is not null;

    public bool CoincideAsiento(AsientoDiario a) =>
        (Desde is not { } d || a.Fecha.Date >= d.Date) && (Hasta is not { } h || a.Fecha.Date <= h.Date)
        && (string.IsNullOrWhiteSpace(Origen) || string.Equals(a.Origen, Origen, StringComparison.Ordinal));

    public bool CoincideApunte(ApunteDiario l)
    {
        if (!string.IsNullOrWhiteSpace(Cuenta) && !l.Cuenta.StartsWith(Cuenta.Trim(), StringComparison.Ordinal)) return false;
        if (Punteado is { } p && p != l.Punteado) return false;
        if (IdCentroCoste is { } c && l.IdCentroCoste != c) return false;
        if (string.IsNullOrWhiteSpace(Texto)) return true;
        var q = Texto.Trim();
        if (q[0] == '#') return string.Equals(l.Asiento.IdAsiento.ToString(CultureInfo.InvariantCulture), q[1..].Trim(), StringComparison.Ordinal);
        var donde = TextoDiario.Plano(string.Join(" ", l.Asiento.Descripcion, l.ConceptoPropio, l.Documento, l.Cuenta, l.NombreCuenta,
            l.Importe.ToString("#,##0.00", TextoDiario.Es), l.Importe.ToString("0.00", TextoDiario.Es)));
        return TextoDiario.Plano(q).Split(' ', StringSplitOptions.RemoveEmptyEntries).All(donde.Contains);
    }
}

/// <summary>Comparación de textos del Diario: sin mayúsculas ni acentos.</summary>
public static class TextoDiario
{
    internal static readonly CultureInfo Es = CrearCultura();

    private static CultureInfo CrearCultura()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("es-ES").Clone();
        c.NumberFormat.NumberGroupSeparator = ".";
        c.NumberFormat.NumberDecimalSeparator = ",";
        c.NumberFormat.NumberGroupSizes = [3];
        return c;
    }

    public static string Plano(string? texto)
    {
        var sb = new StringBuilder();
        foreach (var ch in (texto ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(ch));
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}

/// <summary>Un asiento que cumple el filtro, con sus apuntes que lo cumplen.</summary>
public sealed record AsientoFiltrado(AsientoDiario Asiento, IReadOnlyList<ApunteDiario> Coinciden);

/// <summary>Totales de un mes en la vista por asiento.</summary>
public sealed record TotalMesDiario(int Ejercicio, int Mes, int Asientos, decimal Debe, decimal Haber);

/// <summary>Resultado de aplicar el filtro: los asientos, sus recuentos y sus sumas.</summary>
public sealed record VistaDiario(IReadOnlyList<AsientoFiltrado> Asientos)
{
    public int NumeroAsientos => Asientos.Count;
    /// <summary>Apuntes que cumplen el filtro (los de la vista «Lista de apuntes»).</summary>
    public IEnumerable<ApunteDiario> Apuntes => Asientos.SelectMany(a => a.Coinciden);
    /// <summary>Totales de los asientos completos (vista «Por asiento»): siempre cuadran.</summary>
    public decimal DebeAsientos => Asientos.Sum(a => a.Asiento.TotalDebe);
    public decimal HaberAsientos => Asientos.Sum(a => a.Asiento.TotalHaber);
    public int ApuntesAsientos => Asientos.Sum(a => a.Asiento.Apuntes.Count);
    /// <summary>Totales de los apuntes que cumplen el filtro (vista «Lista de apuntes»).</summary>
    public decimal DebeApuntes => Apuntes.Sum(a => a.Debe);
    public decimal HaberApuntes => Apuntes.Sum(a => a.Haber);
    public int NumeroApuntes => Asientos.Sum(a => a.Coinciden.Count);

    /// <summary>Totales por mes de los asientos completos, en el orden de la vista.</summary>
    public IReadOnlyList<TotalMesDiario> TotalesPorMes() =>
        Asientos.GroupBy(a => (a.Asiento.Fecha.Year, a.Asiento.Fecha.Month))
            .Select(g => new TotalMesDiario(g.Key.Year, g.Key.Month, g.Count(), g.Sum(a => a.Asiento.TotalDebe), g.Sum(a => a.Asiento.TotalHaber)))
            .ToList();
}

/// <summary>Una cuenta cuya apertura no coincide con el saldo con que acabó el ejercicio anterior.</summary>
public sealed record DiferenciaApertura(string Cuenta, string Nombre, decimal SaldoCierre, decimal SaldoApertura)
{
    public decimal Diferencia => SaldoApertura - SaldoCierre;
}

/// <summary>Cálculos del Diario que no necesitan la BD: filtrar, sumar y comparar la apertura con el cierre anterior.</summary>
public static class CalculadoraDiario
{
    /// <summary>Asientos que cumplen el filtro, por fecha y número, con los apuntes que lo cumplen (un asiento sin ninguno no entra).</summary>
    public static VistaDiario Filtrar(DatosDiario datos, FiltroDiario filtro)
    {
        var lista = new List<AsientoFiltrado>();
        foreach (var a in datos.Asientos.OrderBy(a => a.Fecha.Date).ThenBy(a => a.IdAsiento))
        {
            if (!filtro.CoincideAsiento(a)) continue;
            var coinciden = filtro.FiltraApuntes ? a.Apuntes.Where(filtro.CoincideApunte).ToList() : a.Apuntes;
            if (coinciden.Count > 0) lista.Add(new AsientoFiltrado(a, coinciden));
        }
        return new VistaDiario(lista);
    }

    /// <summary>
    /// Compara la apertura del ejercicio de trabajo con el saldo con que acabó cada cuenta el anterior (contabilidad/diario 2026-11-14 21:31):
    /// solo las cuentas de balance (grupos 1 a 5); el resultado del anterior (129) cuenta con lo que aún quede en los grupos 6 y 7, por si
    /// no se ha regularizado; el cierre del anterior no se cuenta. Sin asiento de apertura no hay nada que comparar.
    /// </summary>
    public static IReadOnlyList<DiferenciaApertura> DiferenciasApertura(DatosDiario datos, EjercicioDiario anterior, EjercicioDiario trabajo)
    {
        var apertura = datos.Asientos.Where(a => a.Ejercicio.IdSociedadEjercicio == trabajo.IdSociedadEjercicio && a.TipoAsiento == TiposAsientoMayor.Apertura).ToList();
        if (apertura.Count == 0) return [];
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal);
        var cierre = new Dictionary<string, decimal>(StringComparer.Ordinal);
        var resultado = 0m;
        foreach (var l in datos.Asientos.Where(a => a.Ejercicio.IdSociedadEjercicio == anterior.IdSociedadEjercicio && a.TipoAsiento != TiposAsientoMayor.Cierre).SelectMany(a => a.Apuntes))
        {
            nombres[l.Cuenta] = l.NombreCuenta;
            if (l.Cuenta.Length > 0 && l.Cuenta[0] is '6' or '7') resultado += l.Debe - l.Haber;
            else if (EsDeBalance(l.Cuenta)) cierre[l.Cuenta] = cierre.GetValueOrDefault(l.Cuenta) + l.Debe - l.Haber;
        }
        var abre = new Dictionary<string, decimal>(StringComparer.Ordinal);
        foreach (var l in apertura.SelectMany(a => a.Apuntes).Where(l => EsDeBalance(l.Cuenta)))
        {
            nombres[l.Cuenta] = l.NombreCuenta;
            abre[l.Cuenta] = abre.GetValueOrDefault(l.Cuenta) + l.Debe - l.Haber;
        }
        // El resultado sin regularizar se compara en conjunto con las 129 de la apertura.
        if (resultado != 0m)
        {
            var c129 = cierre.Keys.Concat(abre.Keys).Where(c => c.StartsWith("129", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).ToList();
            var cierre129 = c129.Sum(c => cierre.GetValueOrDefault(c)) + resultado;
            var abre129 = c129.Sum(c => abre.GetValueOrDefault(c));
            foreach (var c in c129) { cierre.Remove(c); abre.Remove(c); }
            if (Math.Abs(cierre129 - abre129) >= 0.005m)
            {
                cierre["129"] = cierre129;
                abre["129"] = abre129;
                nombres["129"] = "Resultado del ejercicio (con los grupos 6 y 7 sin regularizar)";
            }
        }
        return cierre.Keys.Concat(abre.Keys).Distinct(StringComparer.Ordinal).OrderBy(c => c, StringComparer.Ordinal)
            .Select(c => new DiferenciaApertura(c, nombres.GetValueOrDefault(c, ""), cierre.GetValueOrDefault(c), abre.GetValueOrDefault(c)))
            .Where(d => Math.Abs(d.Diferencia) >= 0.005m).ToList();
    }

    private static bool EsDeBalance(string cuenta) => cuenta.Length > 0 && cuenta[0] is >= '1' and <= '5';
}
