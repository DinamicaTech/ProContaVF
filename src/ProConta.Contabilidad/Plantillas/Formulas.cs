using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Plantillas;

/// <summary>
/// Valor de una fórmula: número (siempre decimal), texto, sí/no o vacío.
/// </summary>
public readonly record struct ValorFormula(object? Dato)
{
    public static readonly ValorFormula Vacio = new(null);

    public static ValorFormula Numero(decimal n) => new(n);
    public static ValorFormula Texto(string? t) => new(t);
    public static ValorFormula Logico(bool b) => new(b);

    public bool EsVacio => Dato is null || Dato is string { Length: 0 };
    public bool EsNumero => Dato is decimal;
    public bool EsTexto => Dato is string;
    public bool EsLogico => Dato is bool;

    /// <summary>Número del valor; un texto numérico («121,50» o «121.50») también vale. Vacío = 0.</summary>
    public decimal ComoNumero(string formula)
    {
        switch (Dato)
        {
            case null: return 0m;
            case decimal d: return d;
            case bool b: return b ? 1m : 0m;
            case string s when s.Trim().Length == 0: return 0m;
            case string s when TryNumero(s, out var n): return n;
            default:
                throw new ErrorFormula(formula, $"«{Dato}» no es un número.", "Usa un importe o un dato de tipo importe en esta parte de la fórmula.");
        }
    }

    public string ComoTexto() => Dato switch
    {
        null => "",
        decimal d => d == decimal.Truncate(d) ? decimal.Truncate(d).ToString(CultureInfo.InvariantCulture) : d.ToString("0.00", new CultureInfo("es-ES")),
        bool b => b ? "Sí" : "No",
        string s => s,
        _ => Convert.ToString(Dato, CultureInfo.InvariantCulture) ?? "",
    };

    public bool ComoLogico(string formula) => Dato switch
    {
        null => false,
        bool b => b,
        decimal d => d != 0m,
        string s => s.Trim().Length > 0 && !string.Equals(s.Trim(), "no", StringComparison.OrdinalIgnoreCase) && s.Trim() != "0",
        _ => throw new ErrorFormula(formula, $"«{Dato}» no es una condición.", "Usa una comparación, por ejemplo Importe > 0."),
    };

    internal static bool TryNumero(string texto, out decimal n) =>
        decimal.TryParse(texto.Trim().Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out n);

    public override string ToString() => ComoTexto();
}

/// <summary>Error al leer o evaluar una fórmula de plantilla: dice qué fórmula, qué falla y cómo corregirlo.</summary>
public sealed class ErrorFormula(string formula, string causa, string accion) : Exception($"La fórmula «{formula}» no es válida: {causa}")
{
    public string Formula { get; } = formula;
    public string Causa { get; } = causa;
    public string Accion { get; } = accion;
}

/// <summary>
/// Lenguaje de fórmulas de las plantillas de asiento (contabilidad/plantillas-asiento 2026-11-12 11:15, «Fórmulas»), propio y
/// pequeño, sin librerías:
/// <list type="bullet">
/// <item>números (<c>121,50</c> o <c>121.50</c>, siempre decimal) y textos entre comillas (<c>"Anulación: "</c>, comillas dobles para una comilla);</item>
/// <item>nombres: campos del origen (Cuenta, Lado, Importe, Concepto, Documento, Fecha, Centro…), datos de entrada y cuentas de la
/// sociedad (CuentaCuadres, CuentaTesoreria); <c>Origen.Campo</c> es siempre el apunte de origen;</item>
/// <item><c>+ - * /</c> (también <c>×</c> y <c>÷</c>), <c>&amp;</c> para unir textos, paréntesis, comparaciones
/// <c>= &lt;&gt; &lt; &gt; &lt;= &gt;=</c> (los textos sin distinguir mayúsculas), <c>Y</c>, <c>O</c> y <c>No</c>;</item>
/// <item>funciones: <c>Redondear(x)</c> a 2 decimales con la mitad hacia arriba (MidpointRounding.AwayFromZero), <c>Abs(x)</c>,
/// <c>Si(condición; sí; no)</c>, <c>Subcuenta(prefijo; cuenta)</c> (el prefijo completado con los últimos dígitos de la cuenta) y
/// <c>UltimaUsada()</c> (en el valor propuesto de un dato de cuenta: la última cuenta usada con la plantilla que cumple su filtro).</item>
/// </list>
/// Los argumentos se separan con <c>;</c>. Una fórmula vacía vale vacío.
/// </summary>
public static class Formula
{
    private static readonly Dictionary<string, Nodo> _cache = new(StringComparer.Ordinal);
    private static readonly object _bloqueo = new();

    /// <summary>Evalúa <paramref name="formula"/> con los nombres que resuelve <paramref name="entorno"/>.</summary>
    public static ValorFormula Evaluar(string? formula, IEntornoFormula entorno)
    {
        if (string.IsNullOrWhiteSpace(formula)) return ValorFormula.Vacio;
        return Analizar(formula).Evaluar(new Evaluacion(formula, entorno));
    }

    /// <summary>Comprueba la sintaxis sin evaluar; devuelve null si es correcta o el error.</summary>
    public static ErrorFormula? Validar(string? formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;
        try { Analizar(formula); return null; }
        catch (ErrorFormula e) { return e; }
    }

    private static Nodo Analizar(string formula)
    {
        lock (_bloqueo)
            if (_cache.TryGetValue(formula, out var hecho)) return hecho;
        var nodo = new Analizador(formula).Todo();
        lock (_bloqueo) _cache[formula] = nodo;
        return nodo;
    }

    // ---------- Léxico ----------

    private enum Tipo { Numero, Texto, Nombre, Operador, AbreParentesis, CierraParentesis, PuntoYComa, Fin }

    private readonly record struct Simbolo(Tipo Tipo, string Texto, decimal Numero = 0m);

    private static List<Simbolo> Simbolos(string f)
    {
        var lista = new List<Simbolo>();
        var i = 0;
        while (i < f.Length)
        {
            var c = f[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsAsciiDigit(c))
            {
                var inicio = i;
                while (i < f.Length && char.IsAsciiDigit(f[i])) i++;
                if (i + 1 < f.Length && (f[i] == ',' || f[i] == '.') && char.IsAsciiDigit(f[i + 1]))
                {
                    i++;
                    while (i < f.Length && char.IsAsciiDigit(f[i])) i++;
                }
                var texto = f[inicio..i];
                lista.Add(new Simbolo(Tipo.Numero, texto, decimal.Parse(texto.Replace(',', '.'), CultureInfo.InvariantCulture)));
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= f.Length) throw new ErrorFormula(f, "falta la comilla que cierra un texto.", "Cierra el texto con \".");
                    if (f[i] == '"')
                    {
                        if (i + 1 < f.Length && f[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    sb.Append(f[i++]);
                }
                lista.Add(new Simbolo(Tipo.Texto, sb.ToString()));
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                var inicio = i;
                while (i < f.Length && (char.IsLetterOrDigit(f[i]) || f[i] == '_' || (f[i] == '.' && i + 1 < f.Length && char.IsLetter(f[i + 1])))) i++;
                lista.Add(new Simbolo(Tipo.Nombre, f[inicio..i]));
                continue;
            }
            switch (c)
            {
                case '(': lista.Add(new Simbolo(Tipo.AbreParentesis, "(")); i++; continue;
                case ')': lista.Add(new Simbolo(Tipo.CierraParentesis, ")")); i++; continue;
                case ';': lista.Add(new Simbolo(Tipo.PuntoYComa, ";")); i++; continue;
                case '+' or '-' or '*' or '/' or '&' or '=':
                    lista.Add(new Simbolo(Tipo.Operador, c.ToString())); i++; continue;
                case '×': lista.Add(new Simbolo(Tipo.Operador, "*")); i++; continue;
                case '÷': lista.Add(new Simbolo(Tipo.Operador, "/")); i++; continue;
                case '<' or '>':
                    if (i + 1 < f.Length && (f[i + 1] == '=' || (c == '<' && f[i + 1] == '>')))
                    {
                        lista.Add(new Simbolo(Tipo.Operador, f.Substring(i, 2)));
                        i += 2;
                    }
                    else
                    {
                        lista.Add(new Simbolo(Tipo.Operador, c.ToString()));
                        i++;
                    }
                    continue;
            }
            throw new ErrorFormula(f, $"no entiendo el carácter «{c}».", "Usa números, textos entre comillas, nombres, + - * / & ( ) ; y comparaciones.");
        }
        lista.Add(new Simbolo(Tipo.Fin, ""));
        return lista;
    }

    // ---------- Sintaxis (descenso recursivo) ----------

    private sealed class Analizador(string formula)
    {
        private readonly List<Simbolo> _s = Simbolos(formula);
        private int _i;

        private Simbolo Actual => _s[_i];

        public Nodo Todo()
        {
            var n = O();
            if (Actual.Tipo != Tipo.Fin)
                throw new ErrorFormula(formula, $"sobra «{Actual.Texto}».", "Revisa los paréntesis y los operadores entre los valores.");
            return n;
        }

        private bool EsPalabra(string palabra) => Actual.Tipo == Tipo.Nombre && string.Equals(Actual.Texto, palabra, StringComparison.OrdinalIgnoreCase);
        private bool EsOperador(params string[] ops) => Actual.Tipo == Tipo.Operador && ops.Contains(Actual.Texto);

        private Nodo O()
        {
            var n = Y();
            while (EsPalabra("O")) { _i++; n = new Binario("O", n, Y()); }
            return n;
        }

        private Nodo Y()
        {
            var n = Negacion();
            while (EsPalabra("Y")) { _i++; n = new Binario("Y", n, Negacion()); }
            return n;
        }

        private Nodo Negacion()
        {
            if (EsPalabra("No")) { _i++; return new Unario("No", Negacion()); }
            return Comparacion();
        }

        private Nodo Comparacion()
        {
            var n = Union();
            while (EsOperador("=", "<>", "<", ">", "<=", ">="))
            {
                var op = Actual.Texto;
                _i++;
                n = new Binario(op, n, Union());
            }
            return n;
        }

        private Nodo Union()
        {
            var n = Suma();
            while (EsOperador("&")) { _i++; n = new Binario("&", n, Suma()); }
            return n;
        }

        private Nodo Suma()
        {
            var n = Producto();
            while (EsOperador("+", "-"))
            {
                var op = Actual.Texto;
                _i++;
                n = new Binario(op, n, Producto());
            }
            return n;
        }

        private Nodo Producto()
        {
            var n = Signo();
            while (EsOperador("*", "/"))
            {
                var op = Actual.Texto;
                _i++;
                n = new Binario(op, n, Signo());
            }
            return n;
        }

        private Nodo Signo()
        {
            if (EsOperador("-")) { _i++; return new Unario("-", Signo()); }
            if (EsOperador("+")) { _i++; return Signo(); }
            return Primario();
        }

        private Nodo Primario()
        {
            var s = Actual;
            switch (s.Tipo)
            {
                case Tipo.Numero: _i++; return new Literal(ValorFormula.Numero(s.Numero));
                case Tipo.Texto: _i++; return new Literal(ValorFormula.Texto(s.Texto));
                case Tipo.AbreParentesis:
                {
                    _i++;
                    var n = O();
                    Esperar(Tipo.CierraParentesis, ")");
                    return n;
                }
                case Tipo.Nombre:
                {
                    _i++;
                    if (Actual.Tipo != Tipo.AbreParentesis)
                    {
                        if (string.Equals(s.Texto, "Verdadero", StringComparison.OrdinalIgnoreCase)) return new Literal(ValorFormula.Logico(true));
                        if (string.Equals(s.Texto, "Falso", StringComparison.OrdinalIgnoreCase)) return new Literal(ValorFormula.Logico(false));
                        return new Nombre(s.Texto);
                    }
                    _i++;
                    var argumentos = new List<Nodo>();
                    if (Actual.Tipo != Tipo.CierraParentesis)
                    {
                        argumentos.Add(O());
                        while (Actual.Tipo == Tipo.PuntoYComa) { _i++; argumentos.Add(O()); }
                    }
                    Esperar(Tipo.CierraParentesis, ")");
                    return Funcion.Crear(formula, s.Texto, argumentos);
                }
                default:
                    throw new ErrorFormula(formula, s.Tipo == Tipo.Fin ? "está incompleta." : $"no esperaba «{s.Texto}».",
                        "Completa la fórmula: falta un valor después de un operador o dentro de un paréntesis.");
            }
        }

        private void Esperar(Tipo tipo, string texto)
        {
            if (Actual.Tipo != tipo)
                throw new ErrorFormula(formula, $"falta «{texto}».", texto == ")" ? "Cierra el paréntesis." : $"Añade «{texto}».");
            _i++;
        }
    }

    // ---------- Árbol y evaluación ----------

    private sealed record Evaluacion(string Formula, IEntornoFormula Entorno);

    private abstract record Nodo
    {
        public abstract ValorFormula Evaluar(Evaluacion e);
    }

    private sealed record Literal(ValorFormula Valor) : Nodo
    {
        public override ValorFormula Evaluar(Evaluacion e) => Valor;
    }

    private sealed record Nombre(string Texto) : Nodo
    {
        public override ValorFormula Evaluar(Evaluacion e) =>
            e.Entorno.Resolver(Texto) ?? throw new ErrorFormula(e.Formula, $"no conozco el nombre «{Texto}».",
                "Usa un campo del origen (Cuenta, Lado, Importe, Concepto, Documento, Fecha, Centro), un dato de entrada de la plantilla o una cuenta de la empresa (CuentaCuadres, CuentaTesoreria).");
    }

    private sealed record Unario(string Operador, Nodo Operando) : Nodo
    {
        public override ValorFormula Evaluar(Evaluacion e)
        {
            var v = Operando.Evaluar(e);
            return Operador == "-" ? ValorFormula.Numero(-v.ComoNumero(e.Formula)) : ValorFormula.Logico(!v.ComoLogico(e.Formula));
        }
    }

    private sealed record Binario(string Operador, Nodo Izquierda, Nodo Derecha) : Nodo
    {
        public override ValorFormula Evaluar(Evaluacion e)
        {
            if (Operador == "Y") return ValorFormula.Logico(Izquierda.Evaluar(e).ComoLogico(e.Formula) && Derecha.Evaluar(e).ComoLogico(e.Formula));
            if (Operador == "O") return ValorFormula.Logico(Izquierda.Evaluar(e).ComoLogico(e.Formula) || Derecha.Evaluar(e).ComoLogico(e.Formula));
            var a = Izquierda.Evaluar(e);
            var b = Derecha.Evaluar(e);
            switch (Operador)
            {
                case "&": return ValorFormula.Texto(a.ComoTexto() + b.ComoTexto());
                case "+":
                    // Con un texto no numérico, + une textos (como en Access).
                    if ((a.EsTexto && !ValorFormula.TryNumero((string)a.Dato!, out _)) || (b.EsTexto && !ValorFormula.TryNumero((string)b.Dato!, out _)))
                        return ValorFormula.Texto(a.ComoTexto() + b.ComoTexto());
                    return ValorFormula.Numero(a.ComoNumero(e.Formula) + b.ComoNumero(e.Formula));
                case "-": return ValorFormula.Numero(a.ComoNumero(e.Formula) - b.ComoNumero(e.Formula));
                case "*": return ValorFormula.Numero(a.ComoNumero(e.Formula) * b.ComoNumero(e.Formula));
                case "/":
                    var divisor = b.ComoNumero(e.Formula);
                    if (divisor == 0m) throw new ErrorFormula(e.Formula, "divide entre cero.", "Comprueba que el divisor no puede valer 0, o protégelo con Si(divisor = 0; 0; …).");
                    return ValorFormula.Numero(a.ComoNumero(e.Formula) / divisor);
                default:
                    var c = Comparar(a, b, e.Formula);
                    return ValorFormula.Logico(Operador switch
                    {
                        "=" => c == 0,
                        "<>" => c != 0,
                        "<" => c < 0,
                        ">" => c > 0,
                        "<=" => c <= 0,
                        _ => c >= 0,
                    });
            }
        }

        private static int Comparar(ValorFormula a, ValorFormula b, string formula)
        {
            var numericos = (a.EsNumero || a.EsVacio || (a.EsTexto && ValorFormula.TryNumero((string)a.Dato!, out _))) &&
                            (b.EsNumero || b.EsVacio || (b.EsTexto && ValorFormula.TryNumero((string)b.Dato!, out _)));
            if ((a.EsNumero || b.EsNumero) && numericos) return a.ComoNumero(formula).CompareTo(b.ComoNumero(formula));
            if (a.EsLogico || b.EsLogico) return a.ComoLogico(formula).CompareTo(b.ComoLogico(formula));
            return string.Compare(a.ComoTexto().Trim(), b.ComoTexto().Trim(), CultureInfo.GetCultureInfo("es-ES"), CompareOptions.IgnoreCase);
        }
    }

    private sealed record Funcion(string Nombre, IReadOnlyList<Nodo> Argumentos) : Nodo
    {
        private static readonly Dictionary<string, (int Min, int Max)> _aridad = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Redondear"] = (1, 1),
            ["Abs"] = (1, 1),
            ["Si"] = (3, 3),
            ["Subcuenta"] = (2, 2),
            ["UltimaUsada"] = (0, 0),
        };

        public static Funcion Crear(string formula, string nombre, List<Nodo> argumentos)
        {
            if (!_aridad.TryGetValue(nombre, out var n))
                throw new ErrorFormula(formula, $"no conozco la función «{nombre}».", "Las funciones son Redondear(x), Abs(x), Si(condición; sí; no), Subcuenta(prefijo; cuenta) y UltimaUsada().");
            if (argumentos.Count < n.Min || argumentos.Count > n.Max)
                throw new ErrorFormula(formula, $"{nombre} necesita {n.Min} {(n.Min == 1 ? "valor" : "valores")} y tiene {argumentos.Count}.", "Separa los valores con «;».");
            return new Funcion(_aridad.Keys.First(k => string.Equals(k, nombre, StringComparison.OrdinalIgnoreCase)), argumentos);
        }

        public override ValorFormula Evaluar(Evaluacion e)
        {
            switch (Nombre)
            {
                case "Redondear": return ValorFormula.Numero(Math.Round(Argumentos[0].Evaluar(e).ComoNumero(e.Formula), 2, MidpointRounding.AwayFromZero));
                case "Abs": return ValorFormula.Numero(Math.Abs(Argumentos[0].Evaluar(e).ComoNumero(e.Formula)));
                case "Si": return Argumentos[0].Evaluar(e).ComoLogico(e.Formula) ? Argumentos[1].Evaluar(e) : Argumentos[2].Evaluar(e);
                case "Subcuenta":
                    return ValorFormula.Texto(CuentasPlantilla.Completar(Argumentos[0].Evaluar(e).ComoTexto(), Argumentos[1].Evaluar(e).ComoTexto()));
                default:
                    return e.Entorno.UltimaUsada() is { Length: > 0 } c ? ValorFormula.Texto(c) : ValorFormula.Vacio;
            }
        }
    }
}

/// <summary>Resuelve los nombres de una fórmula (campos del origen, datos de entrada y cuentas de la sociedad).</summary>
public interface IEntornoFormula
{
    /// <summary>Valor del nombre, o null si no existe (es un error de la fórmula).</summary>
    ValorFormula? Resolver(string nombre);

    /// <summary>Para <c>UltimaUsada()</c>: la última cuenta usada con la plantilla para el dato que se está proponiendo.</summary>
    string? UltimaUsada() => null;
}

/// <summary>Entorno con un diccionario de nombres (sin distinguir mayúsculas).</summary>
public sealed class EntornoDiccionario(IReadOnlyDictionary<string, ValorFormula> valores, Func<string?>? ultimaUsada = null) : IEntornoFormula
{
    public ValorFormula? Resolver(string nombre) => valores.TryGetValue(nombre, out var v) ? v : null;
    public string? UltimaUsada() => ultimaUsada?.Invoke();
}

/// <summary>Cuentas con «?»: un prefijo completado con los últimos dígitos de otra cuenta, como las plantillas de nóminas del ERP.</summary>
public static class CuentasPlantilla
{
    /// <summary>
    /// «4760?» con la cuenta 4650012 da 4760012: el prefijo (sin «?») más los últimos dígitos de <paramref name="cuentaBase"/> hasta
    /// su longitud. Sin «?» devuelve el prefijo tal cual.
    /// </summary>
    public static string Completar(string prefijo, string cuentaBase)
    {
        var p = (prefijo ?? "").Trim();
        if (!p.EndsWith('?')) return p;
        p = p.TrimEnd('?');
        var b = (cuentaBase ?? "").Trim();
        return b.Length <= p.Length ? p : p + b[p.Length..];
    }
}
