using System.Globalization;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// Modelo 123 de cada trimestre (contabilidad/modelos-tributarios/modelo-123), con el mismo contrato que el 111 y el 115: el estado de
/// los cuatro trimestres, la declaración (<see cref="Modelo123"/>) y su fichero (<see cref="FicheroRetenciones"/>), que la pieza común
/// valida y presenta.
/// <list type="bullet">
/// <item>Dividendos del trimestre: los repartos grabados en el programa con fecha en el trimestre (bruto y retención de cada socio) o, si
/// no hay, lo abonado en el trimestre en la subcuenta de retención de cada socio, con la base reconstruida al porcentaje de retención de
/// dividendos del ejercicio (la misma lectura que el 193).</item>
/// <item>Un trimestre sin dividendos no se presenta (el 123 no tiene declaración negativa en ese caso): su estado es «sin presentar» con
/// resultado cero y no se ofrece generar.</item>
/// <item>Forma de pago y plazos como el 111 y el 115: domiciliación del 1 al 15 del mes siguiente al trimestre; fuera de él, NRC.</item>
/// </list>
/// </summary>
public sealed class Servicio123(IBaseDatos bd, ServicioPresentacionAeat presentacion, IReloj? reloj = null)
{
    public const string Modelo = Modelo123.Modelo;
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly Servicio193 _dividendos = new(bd);

    public ServicioPresentacionAeat Presentacion => presentacion;
    public DateTime Hoy => _reloj.Ahora.Date;
    public IReadOnlyList<(int Casilla, string Concepto)> Conceptos => Modelo123.Conceptos;
    public IReadOnlySet<int> CasillasPerceptores => Modelo123.CasillasPerceptores;

    private static (DateTime Desde, DateTime Hasta) Trimestre(int ejercicio, int trimestre) =>
        (new DateTime(ejercicio, 3 * trimestre - 2, 1), new DateTime(ejercicio, 3 * trimestre - 2, 1).AddMonths(3));

    /// <summary>Los dividendos de cada socio del trimestre.</summary>
    public async Task<IReadOnlyList<Perceptor193>> DividendosAsync(ContextoSociedadEjercicio ctx, int trimestre, CancellationToken c = default)
    {
        var (desde, hasta) = Trimestre(ctx.Ejercicio, trimestre);
        return (await _dividendos.PerceptoresAsync(ctx, desde, hasta, c)).Perceptores;
    }

    /// <summary>Los cuatro trimestres del ejercicio con su estado.</summary>
    public async Task<IReadOnlyList<TrimestreRetenciones>> TrimestresAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var entorno = await presentacion.EntornoAsync(c);
        var todas = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c)).Where(p => p.Entorno == ServiciosAeat.Codigo(entorno)).ToList();
        var declarante = await new Servicio303(bd, presentacion).DeclaranteAsync(ctx);
        var r = new List<TrimestreRetenciones>();
        for (var t = 1; t <= 4; t++)
        {
            var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, t);
            var del = todas.Where(p => p.Periodo == $"{t}T").OrderBy(p => p.FechaHora).ThenBy(p => p.Id).ToList();
            var ultima = del.LastOrDefault();
            var d = Declarar(await DividendosAsync(ctx, t, c), ctx.Ejercicio, t, declarante, Rectificativa(ultima));
            var bloqueos = new List<string>();
            var avisos = new List<string>();
            if (Hoy > fin)
            {
                bloqueos.AddRange(d.Errores);
                avisos.AddRange(d.Avisos);
                if (d.TotalRetenciones == 0m && ultima is null) bloqueos.Add($"No hay dividendos en el {t}T: no se presenta el 123.");
                if (ServicioRetenciones.FueraDePlazoDomiciliacion(d, Hoy) is { } dom) avisos.Add(dom);
            }
            var estado = Hoy <= fin ? Estado303.EnCurso
                : ultima is null ? Estado303.SinPresentar
                : ultima.Resultado != d.TotalRetenciones ? Estado303.Desactualizado
                : ultima.ComplementariaDe is not null ? Estado303.Complementaria
                : Estado303.Presentado;
            if (estado == Estado303.Desactualizado)
                avisos.Add(ultima!.Resultado < d.TotalRetenciones
                    ? $"Se presentó con {Euros(ultima.Resultado)} de retenciones y ahora da {Euros(d.TotalRetenciones)}: si la diferencia es correcta, presenta una complementaria."
                    : $"Se presentó con {Euros(ultima.Resultado)} de retenciones y ahora da {Euros(d.TotalRetenciones)}, menos: una complementaria no puede bajarlo; pide la rectificación en la Sede.");
            r.Add(new TrimestreRetenciones(t, fin, estado, d.TotalRetenciones, d.Resultado, d.TipoDeclaracion, ultima, del, bloqueos, avisos));
        }
        return r;
    }

    /// <summary>La declaración del trimestre y su fichero. Con <paramref name="complementaria"/>, complementaria de la última presentada desde el programa.</summary>
    public async Task<Resultado<(DeclaracionRetenciones Declaracion, DeclaracionAeat Aeat)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, int trimestre, bool complementaria,
        string? nrc = null, CancellationToken c = default)
    {
        var registro = $"Modelo 123 {trimestre}T {ctx.Ejercicio}";
        Resultado<(DeclaracionRetenciones, DeclaracionAeat)> Fallo(string codigo, string mensaje, string? causa = null, string? solucion = null) =>
            Resultado<(DeclaracionRetenciones, DeclaracionAeat)>.Fallo(new Error($"Modelo123.{codigo}", mensaje, registro, causa, solucion));
        if (trimestre is < 1 or > 4) return Fallo("Trimestre", "El trimestre va de 1 a 4.");
        var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, trimestre);
        if (Hoy <= fin)
            return Fallo("EnCurso", $"El {trimestre}T de {ctx.Ejercicio} aún no ha terminado.", "El 123 se presenta cuando el trimestre ha terminado.",
                $"Vuelve a partir del {fin.AddDays(1):dd/MM/yyyy}.");
        PresentacionGuardada? anterior = null;
        if (complementaria)
        {
            var entorno = await presentacion.EntornoAsync(c);
            anterior = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c))
                .Where(p => p.Periodo == $"{trimestre}T" && p.Entorno == ServiciosAeat.Codigo(entorno)).OrderBy(p => p.FechaHora).ThenBy(p => p.Id).LastOrDefault();
            if (anterior is null)
                return Fallo("SinAnterior", $"No consta presentado el {trimestre}T de {ctx.Ejercicio} desde el programa.",
                    "Una complementaria necesita el justificante y el resultado de la presentación anterior.", "Si lo presentó la gestoría, pídele el justificante.");
        }
        var dividendos = await DividendosAsync(ctx, trimestre, c);
        if (dividendos.Count == 0 && anterior is null)
            return Fallo("SinDividendos", $"No hay dividendos en el {trimestre}T de {ctx.Ejercicio}.", "El 123 solo se presenta en los trimestres en que se pagan dividendos con retención.");
        var declarante = await new Servicio303(bd, presentacion).DeclaranteAsync(ctx);
        var d = Declarar(dividendos, ctx.Ejercicio, trimestre, declarante, Rectificativa(anterior));
        if (d.Casillas.Values.Any(v => v < 0m))
            return Fallo("Negativo", $"El 123 del {trimestre}T no se puede generar.", string.Join(" ", d.Errores), "Corrige lo que se indica y vuelve a intentarlo.");
        var fichero = FicheroRetenciones.Generar(d, ProgramaAeat.Actual);
        var resumen = new List<(string, string)>
        {
            ($"Dividendos: {d.Casilla(1):0} rentas (01), base (04)", Euros(d.Casilla(4))),
            ("Retenciones (07)", Euros(d.Casilla(7))),
        };
        if (d.Casilla(13) != 0m) resumen.Add(("Resultado de anteriores declaraciones (13)", Euros(d.Casilla(13))));
        resumen.Add(("Resultado a ingresar (14)", Euros(d.Casilla(14))));
        var aeat = new DeclaracionAeat(Modelo, ctx.Ejercicio, d.Periodo, fichero, declarante.Nif, declarante.RazonSocial, d.TipoDeclaracion, d.ImporteAIngresar,
            d.TotalRetenciones, declarante.Iban, anterior?.Justificante, resumen, FicheroRetenciones.LeerNif) { Nrc = d.TipoDeclaracion == 'I' ? nrc : null };
        return Resultado<(DeclaracionRetenciones, DeclaracionAeat)>.Correcto((d, aeat));
    }

    private DeclaracionRetenciones Declarar(IReadOnlyList<Perceptor193> dividendos, int ejercicio, int trimestre, Declarante303 declarante, RectificativaRetenciones? rect)
    {
        var d = Modelo123.Calcular(dividendos, ejercicio, trimestre, declarante, rect);
        return ServicioRetenciones.FueraDePlazoDomiciliacion(d, Hoy) is not null ? Modelo123.Calcular(dividendos, ejercicio, trimestre, declarante, rect, conNrc: true) : d;
    }

    private static RectificativaRetenciones? Rectificativa(PresentacionGuardada? anterior) =>
        anterior is null ? null : new RectificativaRetenciones(anterior.Justificante, anterior.Resultado);

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
