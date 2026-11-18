using System.Globalization;
using ProConta.Nucleo.Identificacion;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Clase de una retención: de qué documento sale y a qué modelo y casillas va.</summary>
public enum ClaseRetencionModelo
{
    /// <summary>Rendimientos del trabajo: las nóminas (111, casillas 01-06).</summary>
    Trabajo,
    /// <summary>Actividades económicas (profesionales): facturas de compra con retención de servicios (111, casillas 07-09).</summary>
    Profesionales,
    /// <summary>Arrendamiento de inmuebles urbanos: facturas de compra con retención de alquileres (115).</summary>
    Arrendamientos,
}

/// <summary>
/// Lo que un documento del trimestre aporta al 111 o al 115: el perceptor (para contarlo una vez), lo percibido en dinero y su
/// retención, y lo percibido en especie y su ingreso a cuenta (solo nóminas). Un abono lleva los importes en negativo.
/// </summary>
/// <param name="Perceptor">Clave del perceptor: el empleado o el proveedor (NIF o cuenta), para contar perceptores distintos.</param>
/// <param name="Documento">Texto del documento para los avisos (número de factura o nómina y fecha).</param>
public sealed record PercepcionRetencion(ClaseRetencionModelo Clase, string Perceptor, DateTime Fecha, decimal Dinerario, decimal Retencion, decimal Especie = 0m,
    decimal IngresoCuenta = 0m, string Documento = "");

/// <summary>Declaración anterior del mismo periodo para una complementaria: su justificante y su total de retenciones (lo que ya se declaró).</summary>
public sealed record RectificativaRetenciones(string JustificanteAnterior, decimal ResultadoAnterior);

/// <summary>
/// El 111 o el 115 de un trimestre con sus casillas (número → importe; los números de perceptores también van aquí), la forma de pago
/// (U domiciliación, I ingreso con NRC, N negativa), la complementaria y lo que impide presentarlo (<see cref="Errores"/>) o conviene
/// revisar (<see cref="Avisos"/>).
/// </summary>
public sealed record DeclaracionRetenciones(string Modelo, int Ejercicio, int Trimestre, Declarante303 Declarante, IReadOnlyDictionary<int, decimal> Casillas,
    char TipoDeclaracion, RectificativaRetenciones? Rectificativa, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos)
{
    public string Periodo => $"{Trimestre}T";

    public decimal Casilla(int numero) => Casillas.TryGetValue(numero, out var v) ? v : 0m;

    private bool Es111 => Modelo == Modelo111.Modelo;

    /// <summary>Total de retenciones e ingresos a cuenta del trimestre (111: casilla 28; 115: casilla 03): lo que se guarda como resultado del modelo.</summary>
    public decimal TotalRetenciones => Casilla(Es111 ? 28 : 3);

    /// <summary>Resultado de las declaraciones anteriores del periodo (111: 29; 115: 04), solo en una complementaria.</summary>
    public decimal ResultadoAnteriores => Casilla(Es111 ? 29 : 4);

    /// <summary>Resultado a ingresar (111: 30; 115: 05).</summary>
    public decimal Resultado => Casilla(Es111 ? 30 : 5);

    public decimal ImporteAIngresar => TipoDeclaracion is 'U' or 'I' ? Resultado : 0m;

    public bool Valida => Errores.Count == 0;

    public string FormaPago => TipoDeclaracion switch
    {
        'U' => "Domiciliación del ingreso",
        'I' => "Ingreso con NRC (pagado antes en el banco o en la Sede)",
        'N' => "Negativa (sin resultado a ingresar)",
        _ => TipoDeclaracion.ToString(),
    };
}

/// <summary>Cálculo común del 111 y del 115: resultado, complementaria, forma de pago y comprobaciones. Sin BD.</summary>
internal static class CalculoRetenciones
{
    /// <summary>
    /// Pone las casillas de total, anteriores y resultado, decide la forma de pago y comprueba lo que impide presentar.
    /// Forma de pago: a ingresar, U (domiciliación con el IBAN de la sociedad) o I (NRC, fuera del plazo de domiciliación); cero, N
    /// (negativa). Estos modelos no admiten resultado negativo: una complementaria que baja lo declarado es una rectificación que se
    /// pide en la Sede.
    /// </summary>
    public static DeclaracionRetenciones Cerrar(string modelo, int ejercicio, int trimestre, Declarante303 declarante, SortedDictionary<int, decimal> c, int casillaTotal,
        int casillaAnteriores, int casillaResultado, RectificativaRetenciones? rectificativa, bool conNrc, List<string> errores, List<string> avisos)
    {
        c[casillaAnteriores] = rectificativa?.ResultadoAnterior ?? 0m;
        c[casillaResultado] = c[casillaTotal] - c[casillaAnteriores];
        var resultado = c[casillaResultado];
        var tipo = resultado > 0m ? (conNrc ? 'I' : 'U') : 'N';

        if (trimestre is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(trimestre), "El trimestre va de 1 a 4.");
        if (string.IsNullOrWhiteSpace(declarante.Nif))
            errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        if (c[casillaTotal] < 0m)
            errores.Add($"El total de retenciones del {trimestre}T sale negativo ({Euros(c[casillaTotal])}) y el modelo {modelo} no lo admite. Suele deberse a abonos de " +
                "proveedores con retención mayores que las facturas del trimestre o a una nómina con la retención en negativo: revisa los documentos del trimestre.");
        if (resultado < 0m && rectificativa is not null)
            errores.Add($"La complementaria daría {Euros(resultado)}: lo declarado ahora ({Euros(c[casillaTotal])}) es menos que lo de la anterior ({Euros(rectificativa.ResultadoAnterior)}). " +
                $"Una complementaria solo puede subir el resultado; para bajarlo hay que pedir la rectificación del {modelo} en la Sede de la AEAT.");
        if (rectificativa is not null && resultado == 0m)
            avisos.Add($"La complementaria no cambia el resultado ({Euros(c[casillaTotal])}, lo mismo que la anterior): solo tiene sentido si corrige perceptores o bases.");
        if (tipo == 'U')
        {
            if (string.IsNullOrWhiteSpace(declarante.Iban))
                errores.Add($"El {modelo} sale a ingresar ({Euros(resultado)}) y la sociedad no tiene cuenta de domiciliación: escríbela en Configuración › Parámetros y tablas › Empresas («IBAN de domiciliación»).");
            else if (!Ibanes.EsValido(declarante.Iban))
                errores.Add($"El IBAN de domiciliación de la sociedad ({declarante.Iban}) no es válido: corrígelo en Configuración › Parámetros y tablas › Empresas.");
        }
        return new DeclaracionRetenciones(modelo, ejercicio, trimestre, declarante, c, tipo, rectificativa, errores, avisos);
    }

    /// <summary>Número de perceptores distintos con algún importe distinto de cero en lo que se elige.</summary>
    public static int Perceptores(IEnumerable<PercepcionRetencion> p, Func<PercepcionRetencion, decimal> importe, Func<PercepcionRetencion, decimal> retencion) =>
        p.GroupBy(x => x.Perceptor, StringComparer.OrdinalIgnoreCase).Count(g => g.Sum(importe) != 0m || g.Sum(retencion) != 0m);

    public static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}

/// <summary>
/// Modelo 111 de un trimestre (contabilidad/modelos-tributarios/modelo-111), sin BD, con el diseño de registro de la AEAT
/// (sources/aeat/DR111v2.xls):
/// <list type="bullet">
/// <item>01-03 rendimientos del trabajo dinerarios (nóminas): perceptores distintos, percepciones y retenciones; 04-06 en especie
/// (solo las nóminas con ingreso a cuenta).</item>
/// <item>07-09 actividades económicas dinerarias (facturas de compra con retención de servicios profesionales); 10-27 (especie de
/// actividades, premios, aprovechamientos forestales y derechos de imagen) a cero: la sociedad no tiene esas operaciones.</item>
/// <item>28 total de retenciones; 29 resultado de las declaraciones anteriores del periodo (complementaria); 30 = 28 − 29.</item>
/// </list>
/// </summary>
public static class Modelo111
{
    public const string Modelo = "111";

    public static readonly IReadOnlyList<(int Casilla, string Concepto)> Conceptos =
    [
        (1, "Trabajo · dinerarios · perceptores"), (2, "Trabajo · dinerarios · percepciones"), (3, "Trabajo · dinerarios · retenciones"),
        (4, "Trabajo · en especie · perceptores"), (5, "Trabajo · en especie · valor"), (6, "Trabajo · en especie · ingresos a cuenta"),
        (7, "Actividades económicas · dinerarios · perceptores"), (8, "Actividades económicas · dinerarios · percepciones"), (9, "Actividades económicas · dinerarios · retenciones"),
        (10, "Actividades económicas · en especie · perceptores"), (11, "Actividades económicas · en especie · valor"), (12, "Actividades económicas · en especie · ingresos a cuenta"),
        (28, "Suma de retenciones e ingresos a cuenta"), (29, "Resultado de anteriores declaraciones"), (30, "Resultado a ingresar"),
    ];

    /// <summary>Casillas que son números de perceptores (no importes).</summary>
    public static readonly IReadOnlySet<int> CasillasPerceptores = new HashSet<int> { 1, 4, 7, 10, 13, 16, 19, 22, 25 };

    /// <param name="percepciones">Las percepciones del trimestre de trabajo y profesionales (las de arrendamientos se ignoran).</param>
    /// <param name="conNrc">Si sale a ingresar, se paga con NRC (forma I) en lugar de domiciliarlo (fuera del plazo de domiciliación).</param>
    public static DeclaracionRetenciones Calcular(IEnumerable<PercepcionRetencion> percepciones, int ejercicio, int trimestre, Declarante303 declarante,
        RectificativaRetenciones? rectificativa = null, bool conNrc = false)
    {
        var lista = percepciones.ToList();
        var trabajo = lista.Where(p => p.Clase == ClaseRetencionModelo.Trabajo).ToList();
        var prof = lista.Where(p => p.Clase == ClaseRetencionModelo.Profesionales).ToList();
        var c = new SortedDictionary<int, decimal>();
        for (var n = 1; n <= 27; n++) c[n] = 0m;
        c[1] = CalculoRetenciones.Perceptores(trabajo, p => p.Dinerario, p => p.Retencion);
        c[2] = trabajo.Sum(p => p.Dinerario);
        c[3] = trabajo.Sum(p => p.Retencion);
        c[4] = CalculoRetenciones.Perceptores(trabajo, p => p.Especie, p => p.IngresoCuenta);
        c[5] = trabajo.Sum(p => p.Especie);
        c[6] = trabajo.Sum(p => p.IngresoCuenta);
        c[7] = CalculoRetenciones.Perceptores(prof, p => p.Dinerario, p => p.Retencion);
        c[8] = prof.Sum(p => p.Dinerario);
        c[9] = prof.Sum(p => p.Retencion);
        c[28] = new[] { 3, 6, 9, 12, 15, 18, 21, 24, 27 }.Sum(n => c[n]);
        var errores = new List<string>();
        var avisos = new List<string>();
        foreach (var n in new[] { 2, 3, 5, 6, 8, 9 }.Where(n => c[n] < 0m))
            errores.Add($"La casilla {n:00} sale negativa ({CalculoRetenciones.Euros(c[n])}) y el modelo no lo admite: revisa los abonos y las nóminas del trimestre.");
        if (c[3] == 0m && c[2] > 0m)
            avisos.Add($"Las nóminas del {trimestre}T no tienen retención de IRPF ({CalculoRetenciones.Euros(c[2])} de percepciones): comprueba con la gestoría que es correcto.");
        return CalculoRetenciones.Cerrar(Modelo, ejercicio, trimestre, declarante, c, 28, 29, 30, rectificativa, conNrc, errores, avisos);
    }
}

/// <summary>
/// Modelo 115 de un trimestre (contabilidad/modelos-tributarios/modelo-115), sin BD, con el diseño de registro de la AEAT
/// (sources/aeat/DR115v13.xls): 01 arrendadores distintos, 02 base de las retenciones, 03 retenciones (facturas de compra con
/// retención de alquileres); 04 resultado de las declaraciones anteriores del periodo (complementaria); 05 = 03 − 04. Sin alquileres
/// con retención en el trimestre, la declaración es negativa (N).
/// </summary>
public static class Modelo115
{
    public const string Modelo = "115";

    public static readonly IReadOnlyList<(int Casilla, string Concepto)> Conceptos =
    [
        (1, "Número de perceptores"), (2, "Base de las retenciones e ingresos a cuenta"), (3, "Retenciones e ingresos a cuenta"),
        (4, "Resultado de anteriores declaraciones"), (5, "Resultado a ingresar"),
    ];

    public static readonly IReadOnlySet<int> CasillasPerceptores = new HashSet<int> { 1 };

    public static DeclaracionRetenciones Calcular(IEnumerable<PercepcionRetencion> percepciones, int ejercicio, int trimestre, Declarante303 declarante,
        RectificativaRetenciones? rectificativa = null, bool conNrc = false)
    {
        var alquileres = percepciones.Where(p => p.Clase == ClaseRetencionModelo.Arrendamientos).ToList();
        var c = new SortedDictionary<int, decimal>
        {
            [1] = CalculoRetenciones.Perceptores(alquileres, p => p.Dinerario, p => p.Retencion),
            [2] = alquileres.Sum(p => p.Dinerario),
            [3] = alquileres.Sum(p => p.Retencion),
        };
        var errores = new List<string>();
        if (c[2] < 0m)
            errores.Add($"La base de las retenciones sale negativa ({CalculoRetenciones.Euros(c[2])}) y el modelo no lo admite: revisa los abonos de alquileres del trimestre.");
        return CalculoRetenciones.Cerrar(Modelo, ejercicio, trimestre, declarante, c, 3, 4, 5, rectificativa, conNrc, errores, []);
    }
}
