using System.Globalization;
using System.Text.RegularExpressions;

namespace ProConta.Nominas.Lectura;

/// <summary>
/// Interpreta la hoja «resumen» de nóminas de la gestoría (nominas 2026-11-13): metadatos de las filas de arriba (tipo de paga,
/// periodo «DEL … AL …» y «Empresa: código-nombre NIF: …»), la fila de cabeceras (la que trae la columna del trabajador), una
/// fila por trabajador y la fila «TOTAL EMPRESA». Las cabeceras se traducen a campos con los alias de la sociedad y, si no los
/// tiene, con los alias por defecto; la columna sin cabecera a la izquierda del trabajador es su código. No toca la BD.
/// </summary>
public static partial class InterpreteNominas
{
    private const int FilasCabeceraMaximas = 40;

    [GeneratedRegex(@"^\s*PAGA\s+(?<t>.+?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex RePaga();

    [GeneratedRegex(@"\bDEL\s+(?<d>\d{1,2}/\d{1,2}/\d{4})(?:\s+AL\s+(?<h>\d{1,2}/\d{1,2}/\d{4}))?", RegexOptions.IgnoreCase)]
    private static partial Regex RePeriodo();

    [GeneratedRegex(@"Empresa:\s*(?<c>[^-\s]+)\s*-\s*(?<n>.*?)(?:\s+NIF:\s*(?<nif>\S+))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ReEmpresa();

    /// <param name="filas">Celdas de la primera hoja, fila a fila (null = vacía).</param>
    /// <param name="aliasSociedad">Alias guardados de la sociedad: cabecera normalizada → campo (null o vacío = ignorar la columna).</param>
    public static FicheroNominas Interpretar(IReadOnlyList<IReadOnlyList<object?>> filas, IReadOnlyDictionary<string, string?>? aliasSociedad = null)
    {
        aliasSociedad ??= new Dictionary<string, string?>();
        var errores = new List<IncidenciaNominas>();
        var avisos = new List<IncidenciaNominas>();

        bool Alias(string? cabecera, out string? campo)
        {
            var clave = CamposNomina.NormalizarCabecera(cabecera);
            if (aliasSociedad.TryGetValue(clave, out campo)) { campo = string.IsNullOrWhiteSpace(campo) ? null : campo; return true; }
            if (CamposNomina.AliasPorDefecto.TryGetValue(clave, out var porDefecto)) { campo = porDefecto; return true; }
            campo = null;
            return false;
        }

        // 1. Fila de cabeceras: la primera que tiene la columna del trabajador y al menos dos importes conocidos.
        var filaCab = -1;
        for (var i = 0; i < Math.Min(filas.Count, FilasCabeceraMaximas) && filaCab < 0; i++)
        {
            var campos = filas[i].Select(c => Alias(Texto(c), out var x) ? x : null).ToList();
            if (campos.Contains(CamposNomina.Trabajador) && campos.Count(c => CamposNomina.Buscar(c)?.EsImporte == true) >= 2) filaCab = i;
        }
        if (filaCab < 0)
        {
            errores.Add(new("No encuentro la fila de cabeceras (la que tiene «TRABAJADOR», «BRUTO», «NETO»…). " +
                "Comprueba que es el resumen de nóminas que prepara la gestoría, con sus cabeceras."));
            return Vacio(errores);
        }

        // 2. Metadatos de las filas de arriba.
        string? textoPaga = null, codigoEmpresa = null, nombreEmpresa = null, nifEmpresa = null;
        DateTime? desde = null, hasta = null;
        foreach (var texto in filas.Take(filaCab).SelectMany(f => f).Select(Texto).Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            if (textoPaga is null && RePaga().Match(texto!) is { Success: true } p) textoPaga = p.Groups["t"].Value.Trim();
            if (desde is null && RePeriodo().Match(texto!) is { Success: true } r)
            {
                desde = FechaTexto(r.Groups["d"].Value);
                hasta = r.Groups["h"].Success ? FechaTexto(r.Groups["h"].Value) : null;
            }
            if (codigoEmpresa is null && ReEmpresa().Match(texto!) is { Success: true } e)
            {
                codigoEmpresa = e.Groups["c"].Value.Trim();
                nombreEmpresa = e.Groups["n"].Value.Trim();
                nifEmpresa = e.Groups["nif"].Success ? e.Groups["nif"].Value.Trim() : null;
            }
        }
        if (desde is DateTime d0 && hasta is null) hasta = new DateTime(d0.Year, d0.Month, DateTime.DaysInMonth(d0.Year, d0.Month));
        var tipoFichero = textoPaga is not null && textoPaga.Contains("EXTRA", StringComparison.OrdinalIgnoreCase) ? TipoPaga.Extra : TipoPaga.Mensual;

        // 3. Filas de datos: hasta la fila «TOTAL …».
        var cab = filas[filaCab];
        var datos = new List<(int Indice, IReadOnlyList<object?> Celdas)>();
        IReadOnlyList<object?>? total = null;
        for (var i = filaCab + 1; i < filas.Count; i++)
        {
            var primera = filas[i].Select(Texto).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t));
            if (primera is null) continue;
            if (primera.TrimStart().StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                total ??= primera.Contains("EMPRESA", StringComparison.OrdinalIgnoreCase) ? filas[i] : null;
                if (total is not null) break;
                continue;
            }
            datos.Add((i, filas[i]));
        }

        // 4. Columnas: alias, código del trabajador (columna sin cabecera a la izquierda del trabajador) y columnas por decidir.
        var mapeo = new Dictionary<int, string>();
        var sinAlias = new List<CabeceraSinAlias>();
        var colTrabajador = -1;
        var ancho = Math.Max(cab.Count, datos.Count == 0 ? 0 : datos.Max(x => x.Celdas.Count));
        for (var c = 0; c < ancho; c++)
        {
            var cabecera = Texto(Celda(cab, c));
            var conDatos = datos.Any(x => !string.IsNullOrWhiteSpace(Texto(Celda(x.Celdas, c))));
            if (string.IsNullOrWhiteSpace(cabecera))
            {
                if (!conDatos) continue;
                cabecera = $"Columna {c}";
            }
            if (Alias(cabecera, out var campo))
            {
                if (campo is null) continue;
                if (CamposNomina.Buscar(campo) is not { } def)
                {
                    errores.Add(new($"La columna «{cabecera}» está asignada al campo «{campo}», que no existe. Cambia su alias en el mantenimiento de nóminas."));
                    continue;
                }
                if (mapeo.ContainsValue(def.Nombre))
                {
                    avisos.Add(new($"Hay dos columnas para «{def.Nombre}»: se usa la primera y se ignora «{cabecera}»."));
                    continue;
                }
                mapeo[c] = def.Nombre;
                if (def.Nombre == CamposNomina.Trabajador) colTrabajador = c;
            }
            else if (!conDatos) continue;
            else sinAlias.Add(new(cabecera!, c, datos.Select(x => Texto(Celda(x.Celdas, c))).FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))));
        }
        if (!mapeo.ContainsValue(CamposNomina.CodigoTrabajador) && colTrabajador > 0)
        {
            // Sin cabecera: la columna de la izquierda del trabajador con códigos es el código del trabajador.
            var izquierda = sinAlias.Where(s => s.Columna < colTrabajador && s.Cabecera.StartsWith("Columna ", StringComparison.Ordinal)).OrderByDescending(s => s.Columna).FirstOrDefault();
            if (izquierda is not null)
            {
                mapeo[izquierda.Columna] = CamposNomina.CodigoTrabajador;
                sinAlias.Remove(izquierda);
            }
        }
        foreach (var obligatorio in new[] { "Bruto", "Neto" })
            if (!mapeo.ContainsValue(obligatorio)) errores.Add(new($"Falta la columna del {(obligatorio == "Bruto" ? "bruto" : "neto")}: sin ella no se puede comprobar ni contabilizar la nómina."));

        // 5. Filas.
        var resultado = new List<FilaNomina>();
        var ordinal = 0;
        foreach (var (indice, celdas) in datos)
        {
            var nfila = indice + 1;
            string? Valor(string campo) => mapeo.Where(m => m.Value == campo).Select(m => Texto(Celda(celdas, m.Key))?.Trim()).FirstOrDefault();
            var nombre = Valor(CamposNomina.Trabajador);
            var codigo = Valor(CamposNomina.CodigoTrabajador);
            if (string.IsNullOrWhiteSpace(nombre) && string.IsNullOrWhiteSpace(codigo)) continue;
            var importes = new Dictionary<string, decimal>();
            foreach (var (col, campo) in mapeo.Where(m => CamposNomina.Buscar(m.Value)!.EsImporte))
            {
                var celda = Celda(celdas, col);
                if (Importe(celda) is decimal v) { if (v != 0) importes[campo] = v; }
                else errores.Add(new($"«{Texto(celda)}» no es un importe (columna {Texto(Celda(cab, col)) ?? campo}).", nfila));
            }
            var tipo = Valor(CamposNomina.TipoPaga) is { Length: > 0 } tp ? (tp.Contains("EXTRA", StringComparison.OrdinalIgnoreCase) ? TipoPaga.Extra : TipoPaga.Mensual) : tipoFichero;
            var cobro = mapeo.Where(m => m.Value == CamposNomina.FechaCobro).Select(m => FechaCelda(Celda(celdas, m.Key))).FirstOrDefault();
            var fecha = cobro ?? hasta;
            if (fecha is null)
            {
                errores.Add(new("No sé la fecha de la nómina: el fichero no trae «FECHA COBRO» ni el periodo («DEL … AL …»).", nfila));
                fecha = DateTime.MinValue;
            }
            var fila = new FilaNomina(nfila, ++ordinal, string.IsNullOrWhiteSpace(codigo) ? null : codigo, (nombre ?? "").Trim(),
                Valor(CamposNomina.Dni) is { Length: > 0 } nif ? nif : null, tipo, fecha.Value.Date, importes);
            if (fila.DescuadreNeto != 0)
                avisos.Add(new($"{Describir(fila)}: el neto del fichero ({fila["Neto"]:N2}) no cuadra con el bruto menos las deducciones ({fila.NetoCalculado:N2}). " +
                    "Revísalo con la gestoría antes de contabilizar.", nfila));
            resultado.Add(fila);
        }
        if (resultado.Count == 0 && errores.Count == 0) errores.Add(new("El fichero no trae ninguna nómina debajo de las cabeceras."));
        if (resultado.Select(f => f.TipoPaga).Distinct().Count() > 1)
            errores.Add(new("El fichero mezcla pagas mensuales y extra. Pide a la gestoría un resumen por cada tipo de paga."));
        foreach (var g in resultado.Where(f => f.CodigoTrabajador is not null).GroupBy(f => f.CodigoTrabajador).Where(g => g.Count() > 1))
            errores.Add(new($"El código de trabajador {g.Key} sale {g.Count()} veces (filas {string.Join(", ", g.Select(f => f.Fila))})."));

        // 6. Total: la suma de las filas tiene que dar la fila «TOTAL EMPRESA» (si falta una fila, no cuadra).
        if (total is not null)
            foreach (var (col, campo) in mapeo.Where(m => CamposNomina.Buscar(m.Value)!.EsImporte))
                if (Importe(Celda(total, col)) is decimal t && t != resultado.Sum(f => f[campo]))
                    errores.Add(new($"La suma de «{Texto(Celda(cab, col))}» de las filas ({resultado.Sum(f => f[campo]):N2}) no da el total de la empresa ({t:N2}). " +
                        "Puede que falte una fila: vuelve a pedir el resumen a la gestoría."));

        var tipoFinal = resultado.FirstOrDefault()?.TipoPaga ?? tipoFichero;
        var fechaNomina = resultado.Select(f => (DateTime?)f.FechaNomina).Where(f => f != DateTime.MinValue).Min();
        return new FicheroNominas(codigoEmpresa, nombreEmpresa, nifEmpresa, textoPaga, tipoFinal, desde, hasta, fechaNomina, resultado, mapeo, sinAlias,
            errores, avisos, total is not null);
    }

    public static string Describir(FilaNomina f) => f.CodigoTrabajador is null ? f.Nombre : $"{f.Nombre} ({f.CodigoTrabajador})";

    private static FicheroNominas Vacio(List<IncidenciaNominas> errores) =>
        new(null, null, null, null, TipoPaga.Mensual, null, null, null, [], new Dictionary<int, string>(), [], errores, [], false);

    private static object? Celda(IReadOnlyList<object?> fila, int c) => c < fila.Count ? fila[c] : null;

    internal static string? Texto(object? v) => v switch
    {
        null => null,
        string s => s,
        double d => d.ToString(CultureInfo.InvariantCulture),
        DateTime f => f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        IFormattable x => x.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString(),
    };

    /// <summary>Importe de una celda redondeado a 2 decimales (vacía = 0); null si no es un número.</summary>
    internal static decimal? Importe(object? v)
    {
        switch (v)
        {
            case null: return 0m;
            case double d: return Math.Round((decimal)d, 2, MidpointRounding.AwayFromZero);
            case decimal m: return Math.Round(m, 2, MidpointRounding.AwayFromZero);
            case int i: return i;
            case long l: return l;
        }
        var s = Texto(v)!.Trim().Replace("€", "").Replace(" ", "");
        if (s.Length == 0) return 0m;
        // «1.234,56» (español) o «1234.56».
        var es = s.Contains(',') ? s.Replace(".", "").Replace(',', '.') : s;
        return decimal.TryParse(es, NumberStyles.Number, CultureInfo.InvariantCulture, out var r) ? Math.Round(r, 2, MidpointRounding.AwayFromZero) : null;
    }

    private static DateTime? FechaTexto(string s) =>
        DateTime.TryParseExact(s, ["d/M/yyyy", "dd/MM/yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var f) ? f : null;

    /// <summary>Fecha de una celda: fecha de Excel, número de serie de Excel o texto dd/mm/aaaa.</summary>
    internal static DateTime? FechaCelda(object? v) => v switch
    {
        DateTime f => f.Date,
        double d when d > 20000 && d < 80000 => DateTime.FromOADate(d).Date,
        string s => FechaTexto(s.Trim()),
        _ => null,
    };
}
