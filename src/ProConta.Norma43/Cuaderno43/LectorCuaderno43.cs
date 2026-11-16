using System.Globalization;
using System.Text;

namespace ProConta.Norma43.Cuaderno43;

/// <summary>Gravedad de una incidencia de lectura: un error rechaza el fichero entero; una advertencia se guarda con el lote.</summary>
public enum SeveridadN43 { Error, Advertencia }

/// <summary>Incidencia de lectura o validación, con la línea física del fichero (desde 1) y el registro tal como llegó.</summary>
public sealed record IncidenciaN43(int? Linea, string Codigo, SeveridadN43 Severidad, string Mensaje, string? Registro = null)
{
    public string Texto => Linea is int l ? $"Línea {l}: {Mensaje}" : Mensaje;
}

/// <summary>Registro complementario de un movimiento: 23 (conceptos) o 24 (equivalencia en otra divisa).</summary>
public sealed record ComplementoN43(int Ordinal, int Linea, string Tipo, string? CodigoDato, string? Concepto1, string? Concepto2, string? Divisa, decimal? Importe, string Texto);

/// <summary>
/// Movimiento (registro 22) con sus complementos. <see cref="Sentido"/> es «D» (cargo, clave 1) o «H» (abono, clave 2) y el
/// importe va siempre en positivo, como en el fichero.
/// </summary>
public sealed record MovimientoN43(
    int Ordinal, int Linea, string? OficinaOrigen, DateTime FechaOperacion, DateTime FechaValor, string? ConceptoComun, string? ConceptoPropio,
    string Sentido, decimal Importe, string? Documento, string? Referencia1, string? Referencia2, string Texto, IReadOnlyList<ComplementoN43> Complementos)
{
    /// <summary>
    /// El texto de los registros 23 seguido, tal como lo escribe el banco (nulo si no hay ninguno): las 76 posiciones de cada
    /// registro se concatenan sin separador, porque las palabras siguen de una mitad a otra y de un registro al siguiente
    /// («MA» + «NDATO»), y después los espacios repetidos de relleno se dejan en uno.
    /// </summary>
    public string? ConceptoCompleto
    {
        get
        {
            var texto = string.Concat(Complementos.Where(c => c.Tipo == "23").Select(c => c.Texto.Length >= 80 ? c.Texto.Substring(4, 76) : c.Texto[4..]));
            var limpio = System.Text.RegularExpressions.Regex.Replace(texto, " {2,}", " ").Trim();
            return limpio.Length == 0 ? null : limpio;
        }
    }

    /// <summary>Importe con signo desde el punto de vista del titular: abono en positivo, cargo en negativo.</summary>
    public decimal ImporteFirmado => Sentido == "H" ? Importe : -Importe;
}

/// <summary>
/// Extracto de una cuenta (registro 11 con sus movimientos y su registro 33). Los saldos <see cref="SaldoInicialFirmado"/> y
/// <see cref="SaldoFinal"/> van con signo: acreedor (dinero a favor del titular, clave 2) en positivo y deudor en negativo.
/// </summary>
public sealed class ExtractoN43
{
    public required int Ordinal { get; init; }
    public required int Linea { get; init; }
    public required string Entidad { get; init; }
    public required string Oficina { get; init; }
    public required string Cuenta { get; init; }
    /// <summary>Divisa ISO alfabética (EUR) si se conoce el código numérico del fichero; si no, el código tal cual.</summary>
    public required string Divisa { get; init; }
    public required DateTime FechaInicio { get; init; }
    public required DateTime FechaFin { get; init; }
    public required string SentidoSaldoInicial { get; init; }
    public required decimal SaldoInicial { get; init; }
    public string? Modalidad { get; init; }
    public string? Nombre { get; init; }
    public required string TextoCabecera { get; init; }
    public List<MovimientoN43> Movimientos { get; } = [];
    public int? NumeroDebeDeclarado { get; set; }
    public decimal? TotalDebeDeclarado { get; set; }
    public int? NumeroHaberDeclarado { get; set; }
    public decimal? TotalHaberDeclarado { get; set; }
    public decimal? SaldoFinal { get; set; }
    public string? TextoCierre { get; set; }

    public decimal SaldoInicialFirmado => SentidoSaldoInicial == "H" ? SaldoInicial : -SaldoInicial;
    public int NumeroDebeCalculado => Movimientos.Count(m => m.Sentido == "D");
    public decimal TotalDebeCalculado => Movimientos.Where(m => m.Sentido == "D").Sum(m => m.Importe);
    public int NumeroHaberCalculado => Movimientos.Count(m => m.Sentido == "H");
    public decimal TotalHaberCalculado => Movimientos.Where(m => m.Sentido == "H").Sum(m => m.Importe);

    /// <summary>Cuenta en formato CCC (entidad oficina DC cuenta), como se muestra al operador.</summary>
    public string Ccc => $"{Entidad} {Oficina} {Cuenta}";
}

/// <summary>Resultado de leer un fichero: extractos, recuentos e incidencias. Solo se importa si no tiene errores.</summary>
public sealed record FicheroN43(IReadOnlyList<ExtractoN43> Extractos, int RegistrosLeidos, int? RegistrosDeclarados, string Codificacion, IReadOnlyList<IncidenciaN43> Incidencias)
{
    public bool EsValido => Extractos.Count > 0 && Incidencias.All(i => i.Severidad != SeveridadN43.Error);
    public IEnumerable<IncidenciaN43> Errores => Incidencias.Where(i => i.Severidad == SeveridadN43.Error);
    public IEnumerable<IncidenciaN43> Advertencias => Incidencias.Where(i => i.Severidad == SeveridadN43.Advertencia);
    public int NumeroMovimientos => Extractos.Sum(e => e.Movimientos.Count);
}

/// <summary>
/// Lector puro del cuaderno 43 de la AEB (extractos de cuenta), sin BD (norma43 2026-11-12). Registros de 80 posiciones:
/// 11 cabecera de cuenta, 22 movimiento, 23 conceptos complementarios (hasta 5), 24 equivalencia de divisa, 33 final de cuenta
/// y 88 fin de fichero. Valida el cuadre de cada extracto (número e importe de cargos y abonos declarados frente a los leídos,
/// y saldo inicial más movimientos igual al saldo final) y el número de registros del 88. Las líneas más cortas se completan
/// con espacios (hay bancos que los recortan); una línea de más de 80 posiciones es un error. El fichero se interpreta como
/// UTF-8 si lo es y, si no, como Windows-1252, la codificación habitual de los bancos españoles.
/// </summary>
public static class LectorCuaderno43
{
    private const int Largo = 80;

    /// <summary>Indica si el contenido parece un fichero Norma 43 (primer registro 11 de 80 posiciones).</summary>
    public static bool Parece(byte[] contenido)
    {
        var (texto, _) = Decodificar(contenido);
        var primera = texto.Split('\n').Select(l => l.TrimEnd('\r')).FirstOrDefault(l => l.Trim().Length > 0);
        return primera is not null && primera.StartsWith("11", StringComparison.Ordinal) && primera.TrimEnd().Length is >= 50 and <= Largo;
    }

    public static FicheroN43 Leer(byte[] contenido)
    {
        var (texto, codificacion) = Decodificar(contenido);
        var incidencias = new List<IncidenciaN43>();
        var extractos = new List<ExtractoN43>();
        var lineas = texto.Split('\n');
        ExtractoN43? actual = null;
        MovimientoN43? movimiento = null;
        List<ComplementoN43>? complementos = null;
        int? declarados = null;
        var leidos = 0;
        var fin = false;

        void Cerrar()
        {
            if (movimiento is not null && actual is not null) actual.Movimientos.Add(movimiento with { Complementos = complementos! });
            movimiento = null;
            complementos = null;
        }
        void Error(int linea, string codigo, string mensaje, string? registro) => incidencias.Add(new IncidenciaN43(linea, codigo, SeveridadN43.Error, mensaje, registro));
        void Aviso(int linea, string codigo, string mensaje, string? registro) => incidencias.Add(new IncidenciaN43(linea, codigo, SeveridadN43.Advertencia, mensaje, registro));

        for (var i = 0; i < lineas.Length; i++)
        {
            var n = i + 1;
            var bruto = lineas[i].TrimEnd('\r');
            if (bruto.Trim().Length == 0) continue;
            if (bruto.Length > Largo)
            {
                if (bruto.TrimEnd().Length > Largo)
                {
                    Error(n, "N43.LargoRegistro", $"El registro tiene {bruto.TrimEnd().Length} posiciones y el cuaderno 43 usa 80: no es un fichero Norma 43 o está dañado.", Recortar(bruto));
                    continue;
                }
                bruto = bruto.TrimEnd();
            }
            var r = bruto.PadRight(Largo);
            leidos++;
            if (fin) { Error(n, "N43.DespuesDeFin", "Hay registros después del registro 88 de fin de fichero.", r); continue; }
            var tipo = r[..2];
            switch (tipo)
            {
                case "11":
                {
                    Cerrar();
                    if (actual is not null && actual.TextoCierre is null)
                        Error(actual.Linea, "N43.SinCierre", $"El extracto de la cuenta {actual.Ccc} no tiene registro 33 de final de cuenta.", actual.TextoCabecera);
                    var desde = Fecha(r, 20);
                    var hasta = Fecha(r, 26);
                    var saldo = Importe(r, 33);
                    var clave = r[32];
                    if (desde is null || hasta is null) Error(n, "N43.Fecha", "Las fechas inicial o final de la cabecera no son fechas AAMMDD válidas.", r);
                    if (saldo is null) Error(n, "N43.Importe", "El saldo inicial de la cabecera no es un importe válido.", r);
                    if (clave is not ('1' or '2')) Error(n, "N43.ClaveSaldo", $"La clave del saldo inicial es «{clave}» y debe ser 1 (deudor) o 2 (acreedor).", r);
                    if (!Digitos(r, 2, 18)) Error(n, "N43.Cuenta", "La entidad, la oficina o la cuenta de la cabecera no son números.", r);
                    actual = new ExtractoN43
                    {
                        Ordinal = extractos.Count + 1, Linea = n, Entidad = r.Substring(2, 4), Oficina = r.Substring(6, 4), Cuenta = r.Substring(10, 10),
                        Divisa = Divisa(r.Substring(47, 3)), FechaInicio = desde ?? DateTime.MinValue, FechaFin = hasta ?? DateTime.MinValue,
                        SentidoSaldoInicial = clave == '1' ? "D" : "H", SaldoInicial = saldo ?? 0m, Modalidad = Nulo(r.Substring(50, 1)), Nombre = Nulo(r.Substring(51, 26)),
                        TextoCabecera = r,
                    };
                    extractos.Add(actual);
                    break;
                }
                case "22":
                {
                    Cerrar();
                    if (actual is null || actual.TextoCierre is not null) { Error(n, "N43.SinCabecera", "Hay un movimiento (registro 22) fuera de un extracto: falta su registro 11.", r); break; }
                    var fo = Fecha(r, 10);
                    var fv = Fecha(r, 16);
                    var importe = Importe(r, 28);
                    var clave = r[27];
                    if (fo is null || fv is null) Error(n, "N43.Fecha", "La fecha de operación o la de valor del movimiento no son fechas AAMMDD válidas.", r);
                    if (importe is null) Error(n, "N43.Importe", "El importe del movimiento no es un importe válido.", r);
                    if (clave is not ('1' or '2')) Error(n, "N43.ClaveMovimiento", $"La clave debe/haber del movimiento es «{clave}» y debe ser 1 (cargo) o 2 (abono).", r);
                    else if (fo is DateTime f && (f < actual.FechaInicio || f > actual.FechaFin))
                        Aviso(n, "N43.FueraDePeriodo", $"El movimiento del {f:dd/MM/yyyy} está fuera del periodo del extracto ({actual.FechaInicio:dd/MM/yyyy}–{actual.FechaFin:dd/MM/yyyy}).", r);
                    complementos = [];
                    movimiento = new MovimientoN43(actual.Movimientos.Count + 1, n, Nulo(r.Substring(6, 4)), fo ?? DateTime.MinValue, fv ?? fo ?? DateTime.MinValue,
                        Nulo(r.Substring(22, 2)), Nulo(r.Substring(24, 3)), clave == '1' ? "D" : "H", importe ?? 0m, Nulo(r.Substring(42, 10)), Nulo(r.Substring(52, 12)),
                        Nulo(r.Substring(64, 16)), r, complementos);
                    break;
                }
                case "23":
                    if (movimiento is null) { Error(n, "N43.ComplementoSuelto", "Hay un concepto complementario (registro 23) sin su movimiento (registro 22).", r); break; }
                    if (complementos!.Count(c => c.Tipo == "23") >= 5) Aviso(n, "N43.MasDeCinco23", "El movimiento tiene más de cinco registros 23; se conservan todos.", r);
                    complementos!.Add(new ComplementoN43(complementos.Count + 1, n, "23", Nulo(r.Substring(2, 2)), Nulo(r.Substring(4, 38)), Nulo(r.Substring(42, 38)), null, null, r));
                    break;
                case "24":
                {
                    if (movimiento is null) { Error(n, "N43.ComplementoSuelto", "Hay una equivalencia de divisa (registro 24) sin su movimiento (registro 22).", r); break; }
                    var importe = Importe(r, 7);
                    if (importe is null) Aviso(n, "N43.Importe", "El importe de la equivalencia de divisa no es válido; se conserva el registro tal como llegó.", r);
                    complementos!.Add(new ComplementoN43(complementos.Count + 1, n, "24", Nulo(r.Substring(2, 2)), null, null, Divisa(r.Substring(4, 3)), importe, r));
                    break;
                }
                case "33":
                {
                    Cerrar();
                    if (actual is null || actual.TextoCierre is not null) { Error(n, "N43.SinCabecera", "Hay un final de cuenta (registro 33) sin su registro 11.", r); break; }
                    if (r.Substring(2, 18) != actual.TextoCabecera.Substring(2, 18))
                        Error(n, "N43.CierreOtraCuenta", $"El final de cuenta es de {r.Substring(2, 4)} {r.Substring(6, 4)} {r.Substring(10, 10)} y el extracto es de {actual.Ccc}.", r);
                    actual.NumeroDebeDeclarado = Entero(r, 20, 5);
                    actual.TotalDebeDeclarado = Importe(r, 25);
                    actual.NumeroHaberDeclarado = Entero(r, 39, 5);
                    actual.TotalHaberDeclarado = Importe(r, 44);
                    var saldo = Importe(r, 59);
                    var clave = r[58];
                    if (actual.NumeroDebeDeclarado is null || actual.TotalDebeDeclarado is null || actual.NumeroHaberDeclarado is null || actual.TotalHaberDeclarado is null || saldo is null)
                        Error(n, "N43.Importe", "Los recuentos, totales o el saldo final del registro 33 no son números válidos.", r);
                    if (clave is not ('1' or '2')) Error(n, "N43.ClaveSaldo", $"La clave del saldo final es «{clave}» y debe ser 1 (deudor) o 2 (acreedor).", r);
                    actual.SaldoFinal = saldo is decimal s ? (clave == '1' ? -s : s) : null;
                    actual.TextoCierre = r;
                    ValidarCuadre(actual, n, incidencias);
                    break;
                }
                case "88":
                    Cerrar();
                    fin = true;
                    declarados = Entero(r, 20, 6);
                    if (declarados is null) Aviso(n, "N43.Registros", "El número de registros del fin de fichero no es un número.", r);
                    break;
                default:
                    Error(n, "N43.TipoRegistro", $"Tipo de registro «{tipo}» desconocido: el cuaderno 43 solo tiene 11, 22, 23, 24, 33 y 88.", r);
                    break;
            }
        }
        Cerrar();
        if (actual is not null && actual.TextoCierre is null)
            Error(actual.Linea, "N43.SinCierre", $"El extracto de la cuenta {actual.Ccc} no tiene registro 33 de final de cuenta: el fichero parece cortado.", actual.TextoCabecera);
        if (extractos.Count == 0 && incidencias.All(x => x.Severidad != SeveridadN43.Error))
            incidencias.Add(new IncidenciaN43(null, "N43.Vacio", SeveridadN43.Error, "El fichero no contiene ningún extracto (registro 11): no es un fichero Norma 43."));
        if (!fin) incidencias.Add(new IncidenciaN43(null, "N43.SinFin", SeveridadN43.Advertencia, "El fichero no tiene registro 88 de fin de fichero."));
        else if (declarados is int d && d != leidos - 1 && d != leidos)
            incidencias.Add(new IncidenciaN43(null, "N43.Registros", SeveridadN43.Advertencia, $"El fin de fichero declara {d} registros y hay {leidos - 1} (sin contar el 88)."));
        return new FicheroN43(extractos, leidos, declarados, codificacion, incidencias);
    }

    private static void ValidarCuadre(ExtractoN43 e, int linea, List<IncidenciaN43> incidencias)
    {
        void Error(string codigo, string mensaje) => incidencias.Add(new IncidenciaN43(linea, codigo, SeveridadN43.Error, mensaje, e.TextoCierre));
        if (e.NumeroDebeDeclarado is int nd && nd != e.NumeroDebeCalculado)
            Error("N43.DescuadreNumero", $"La cuenta {e.Ccc} declara {nd} cargos y el fichero trae {e.NumeroDebeCalculado}.");
        if (e.NumeroHaberDeclarado is int nh && nh != e.NumeroHaberCalculado)
            Error("N43.DescuadreNumero", $"La cuenta {e.Ccc} declara {nh} abonos y el fichero trae {e.NumeroHaberCalculado}.");
        if (e.TotalDebeDeclarado is decimal td && td != e.TotalDebeCalculado)
            Error("N43.DescuadreImporte", $"La cuenta {e.Ccc} declara {td:N2} € de cargos y los movimientos suman {e.TotalDebeCalculado:N2} €.");
        if (e.TotalHaberDeclarado is decimal th && th != e.TotalHaberCalculado)
            Error("N43.DescuadreImporte", $"La cuenta {e.Ccc} declara {th:N2} € de abonos y los movimientos suman {e.TotalHaberCalculado:N2} €.");
        var esperado = e.SaldoInicialFirmado + e.TotalHaberCalculado - e.TotalDebeCalculado;
        if (e.SaldoFinal is decimal sf && sf != esperado)
            Error("N43.DescuadreSaldo", $"En la cuenta {e.Ccc}, el saldo inicial ({e.SaldoInicialFirmado:N2} €) más los abonos menos los cargos da {esperado:N2} € y el saldo final declarado es {sf:N2} €.");
    }

    internal static (string Texto, string Codificacion) Decodificar(byte[] contenido)
    {
        var desde = contenido.Length >= 3 && contenido[0] == 0xEF && contenido[1] == 0xBB && contenido[2] == 0xBF ? 3 : 0;
        if (contenido.Skip(desde).All(b => b < 0x80)) return (Encoding.ASCII.GetString(contenido, desde, contenido.Length - desde), "ASCII");
        try
        {
            return (new UTF8Encoding(false, true).GetString(contenido, desde, contenido.Length - desde), "UTF-8");
        }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return (Encoding.GetEncoding(1252).GetString(contenido), "WINDOWS-1252");
        }
    }

    private static DateTime? Fecha(string r, int desde)
    {
        var t = r.Substring(desde, 6);
        return DateTime.TryParseExact("20" + t, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;
    }

    /// <summary>Importe de 14 posiciones con dos decimales implícitos.</summary>
    private static decimal? Importe(string r, int desde)
    {
        var t = r.Substring(desde, 14);
        return t.All(char.IsAsciiDigit) ? decimal.Parse(t, CultureInfo.InvariantCulture) / 100m : null;
    }

    private static int? Entero(string r, int desde, int largo)
    {
        var t = r.Substring(desde, largo);
        return t.All(char.IsAsciiDigit) ? int.Parse(t, CultureInfo.InvariantCulture) : null;
    }

    private static bool Digitos(string r, int desde, int largo) => r.Substring(desde, largo).All(char.IsAsciiDigit);

    private static string? Nulo(string t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();

    private static string Recortar(string t) => t.Length <= Largo ? t : t[..Largo];

    /// <summary>Código ISO 4217 numérico del fichero → alfabético (978 → EUR); si no se conoce, se deja tal cual.</summary>
    public static string Divisa(string codigo) => codigo.Trim() switch
    {
        "978" or "" => "EUR",
        "840" => "USD",
        "826" => "GBP",
        "756" => "CHF",
        "392" => "JPY",
        var c => c,
    };

    /// <summary>
    /// IBAN español a partir de la entidad, la oficina y los diez dígitos de la cuenta del fichero. El cuaderno 43 no trae los
    /// dígitos de control del CCC, así que se calculan; si los datos no son numéricos, devuelve nulo.
    /// </summary>
    public static string? Iban(string entidad, string oficina, string cuenta)
    {
        if (!(entidad + oficina + cuenta).All(char.IsAsciiDigit) || entidad.Length != 4 || oficina.Length != 4 || cuenta.Length != 10) return null;
        static int Dc(string digitos)
        {
            int[] pesos = [1, 2, 4, 8, 5, 10, 9, 7, 3, 6];
            var suma = 0;
            for (var i = 0; i < 10; i++) suma += (digitos[i] - '0') * pesos[i];
            var dc = 11 - suma % 11;
            return dc == 11 ? 0 : dc == 10 ? 1 : dc;
        }
        var ccc = $"{entidad}{oficina}{Dc("00" + entidad + oficina)}{Dc(cuenta)}{cuenta}";
        // ES = 14 28; control = 98 - (ccc + "142800") mod 97.
        var resto = 0;
        foreach (var c in ccc + "142800") resto = (resto * 10 + (c - '0')) % 97;
        return $"ES{98 - resto:00}{ccc}";
    }
}
