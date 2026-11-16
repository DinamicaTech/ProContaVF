using System.Globalization;
using System.Text.RegularExpressions;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Plantillas;
using ProConta.Nominas.Lectura;

namespace ProConta.Nominas.Contabilizacion;

/// <summary>Una nómina tal como la usa la plantilla «Nómina»: el trabajador, su cuenta 465, la fecha y los importes por campo.</summary>
/// <param name="Clave">Clave del registro (la nómina o el trabajador): solo identifica el registro en los errores.</param>
/// <param name="CuentaContable">Cuenta 465 del trabajador en el ejercicio (el campo CUENTACONTABLE de la plantilla).</param>
public sealed record NominaContable(string Clave, string Trabajador, string CuentaContable, DateTime Fecha, IReadOnlyDictionary<string, decimal> Importes,
    string? CodigoTrabajador = null, string? Dni = null, string? TipoPaga = null);

/// <summary>
/// La plantilla de sistema «Nómina» (clave de proceso «nominas», nominas 2026-11-13): los campos que reciben sus fórmulas, la conversión
/// de las líneas de PlantillasApuntes de ProConta antiguo y el contra-asiento de una anulación. Sin BD.
/// </summary>
public static partial class PlantillaNomina
{
    /// <summary>Clave del proceso de la plantilla de sistema «Nómina».</summary>
    public const string ClaveProceso = "nominas";

    public const string Nombre = "Nómina";

    /// <summary>Descripción de la copia de la sociedad de la plantilla «Nómina».</summary>
    public const string Descripcion = "Contabiliza las nóminas de un mes: un asiento por trabajador, con fórmulas sobre los importes de la nómina. Es una plantilla de " +
        "sistema: la ejecuta el proceso de nóminas y no se usa a mano. Sus líneas vienen de la plantilla de nóminas de ProConta antiguo (PlantillasApuntes).";

    /// <summary>
    /// Campos del registro de una nómina para las fórmulas, con los nombres de F_NOM (los de PlantillasApuntes): los importes, Trabajador
    /// (nombre como lo trae la gestoría), CuentaContable (la 465), IdTrabajador (código en la gestoría), DNI y TipoPaga. La fecha la pone
    /// el motor de plantillas (campo Fecha, dd/mm/aaaa). Un importe que no viene vale 0.
    /// </summary>
    public static RegistroProceso Registro(NominaContable n)
    {
        var campos = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Trabajador"] = n.Trabajador,
            ["CuentaContable"] = n.CuentaContable,
            ["IdTrabajador"] = n.CodigoTrabajador,
            ["DNI"] = n.Dni,
            ["TipoPaga"] = n.TipoPaga,
        };
        foreach (var c in CamposNomina.Importes) campos[c.Nombre] = n.Importes.GetValueOrDefault(c.Nombre);
        return new RegistroProceso(n.Clave, n.Fecha.Date, campos);
    }

    /// <summary>Línea de PlantillasApuntes de ProConta antiguo, tal cual.</summary>
    public sealed record LineaAntigua(int Orden, string? FormulaDescripcion, string? FormulaCuenta, string? DH, string? FormulaImporte, string? Condicion);

    /// <summary>
    /// Convierte las líneas de PlantillasApuntes a líneas de la plantilla «Nómina» (un asiento, el número 1, por nómina) y dice qué no
    /// se ha podido convertir. Las fórmulas de Access pasan a la sintaxis del motor: sin corchetes, <c>ABS</c> → <c>Abs</c>, <c>OR</c>/<c>AND</c>/<c>NOT</c>
    /// → <c>O</c>/<c>Y</c>/<c>No</c>, <c>IIf(c, a, b)</c> → <c>Si(c; a; b)</c> e <c>IsNull(X)=True</c> → <c>X = 0</c> (un importe que no viene vale 0).
    /// </summary>
    public static (IReadOnlyList<LineaPlantilla> Lineas, IReadOnlyList<string> Errores) Convertir(IEnumerable<LineaAntigua> antiguas)
    {
        var lineas = new List<LineaPlantilla>();
        var errores = new List<string>();
        foreach (var a in antiguas.OrderBy(a => a.Orden))
        {
            var lado = (a.DH ?? "").Trim().ToUpperInvariant();
            if (lado is not ("D" or "H")) { errores.Add($"Línea {a.Orden}: el lado «{a.DH}» no es D ni H."); continue; }
            var linea = new LineaPlantilla(1, a.Orden + 1, FormulaAcceso(a.FormulaCuenta) ?? "", lado, FormulaAcceso(a.FormulaImporte) ?? "",
                FormulaAcceso(a.FormulaDescripcion), Condicion: FormulaAcceso(a.Condicion));
            var mal = new[] { ("cuenta", linea.Cuenta), ("importe", linea.Importe), ("descripción", linea.Concepto), ("condición", linea.Condicion) }
                .Select(x => (x.Item1, Error: string.IsNullOrWhiteSpace(x.Item2) && x.Item1 is "cuenta" or "importe" ? "está vacía" : Formula.Validar(x.Item2)?.Message))
                .Where(x => x.Error is not null).ToList();
            if (mal.Count > 0) { errores.AddRange(mal.Select(m => $"Línea {a.Orden}, {m.Item1}: {m.Error}")); continue; }
            lineas.Add(linea);
        }
        return (lineas, errores);
    }

    /// <summary>Fórmula de Access de PlantillasApuntes en la sintaxis del motor de plantillas (null si viene vacía).</summary>
    public static string? FormulaAcceso(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;
        var f = formula.Trim();
        f = Corchetes().Replace(f, m => m.Groups[1].Value.Replace(" ", "", StringComparison.Ordinal));
        f = EsNuloVerdadero().Replace(f, m => $"{m.Groups[1].Value} = 0");
        f = EsNuloFalso().Replace(f, m => $"{m.Groups[1].Value} <> 0");
        f = Palabra("ABS").Replace(f, "Abs(");
        f = IIf().Replace(f, "Si(");
        f = FueraDeTextos(f, t => Palabra("OR", false).Replace(Palabra("AND", false).Replace(Palabra("NOT", false).Replace(t, "No"), "Y"), "O"));
        // Argumentos de Si(): Access los separa con comas; el motor, con punto y coma (las comas decimales no aparecen en estas fórmulas).
        if (f.Contains("Si(", StringComparison.Ordinal)) f = FueraDeTextos(f, t => t.Replace(',', ';'));
        return f;
    }

    private static string FueraDeTextos(string f, Func<string, string> cambio)
    {
        var partes = f.Split('"');
        for (var i = 0; i < partes.Length; i += 2) partes[i] = cambio(partes[i]);
        return string.Join('"', partes);
    }

    private static Regex Palabra(string palabra, bool funcion = true) =>
        new(funcion ? $@"\b{palabra}\s*\(" : $@"\b{palabra}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"\[([^\]]+)\]")]
    private static partial Regex Corchetes();

    [GeneratedRegex(@"IsNull\s*\(\s*(\w+)\s*\)\s*=\s*True", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EsNuloVerdadero();

    [GeneratedRegex(@"IsNull\s*\(\s*(\w+)\s*\)\s*=\s*False", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EsNuloFalso();

    [GeneratedRegex(@"\bIIf\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IIf();

    /// <summary>
    /// Contra-asiento que anula <paramref name="original"/> (nominas 2026-11-13 10:11): mismas cuentas, conceptos y documentos con el Debe y el
    /// Haber cambiados, en la fecha indicada (el día 1 del trimestre activo si el original es de un trimestre cerrado).
    /// </summary>
    public static Asiento Anulacion(Asiento original, DateTime fecha, string? notas = null)
    {
        var a = new Asiento(original.Contexto, fecha, Recortar($"Anulación: {original.Descripcion}", ReglasAsiento.LongitudDescripcion))
        {
            Notas = notas ?? $"Anula el asiento {original.IdAsiento} del {original.FechaContable:dd/MM/yyyy}.",
            IdPlantilla = original.IdPlantilla,
        };
        foreach (var x in original.Apuntes.OrderBy(x => x.Orden))
        {
            var apunte = x.Debe.EsCero
                ? Apunte.AlDebe(x.CodigoCuenta, x.Haber.Valor, x.Descripcion)
                : Apunte.AlHaber(x.CodigoCuenta, x.Debe.Valor, x.Descripcion);
            apunte.NumeroDocumento = x.NumeroDocumento;
            apunte.IdCentroCoste = x.IdCentroCoste;
            a.Apuntes.Add(apunte);
        }
        return a;
    }

    /// <summary>Texto de un importe en euros para los mensajes.</summary>
    public static string Euros(decimal importe) => importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES")) + " €";

    private static string Recortar(string texto, int longitud) => texto.Length <= longitud ? texto : texto[..longitud].TrimEnd();
}
