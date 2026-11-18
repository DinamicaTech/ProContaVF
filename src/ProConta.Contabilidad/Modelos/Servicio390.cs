using System.Globalization;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>El 390 de un ejercicio en la pantalla de modelos: estado (los mismos que el 303; «Complementaria» es una sustitutiva), resultado calculado, la última presentación del entorno de la BD y lo que impide presentar o conviene revisar.</summary>
public sealed record Ejercicio390(int Ejercicio, DateTime Fin, Estado303 Estado, decimal ResultadoCalculado, PresentacionGuardada? Ultima,
    IReadOnlyList<PresentacionGuardada> Presentaciones, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos)
{
    public bool Terminado => Estado != Estado303.EnCurso;
    public bool SePuedePresentar => Terminado && Bloqueos.Count == 0;
}

/// <summary>
/// Modelo 390 de un ejercicio (contabilidad/modelos-tributarios/modelo-390), con el mismo contrato que <see cref="Servicio303"/> para la
/// pantalla única de modelos: el estado del año, la declaración con sus casillas (<see cref="Modelo390"/>) y su fichero
/// (<see cref="Fichero390"/>), que la pieza común valida y presenta (<see cref="ServicioPresentacionAeat"/>).
/// <list type="bullet">
/// <item>Sale de las mismas líneas de IVA y del mismo cálculo que el 303 (<see cref="LectorLineasIva"/>, con la prorrata), y de los
/// cuatro 303 calculados con el crédito de la 4700 de cada trimestre (lo compensado y lo ingresado).</item>
/// <item>Adquisiciones intracomunitarias de bienes: las del IVA en la cuenta «Intracomunitario soportado · bienes» (o repercutido ·
/// bienes) del ejercicio; las demás son de servicios.</item>
/// <item>Se puede generar antes de que acabe el año (para validarlo), con un aviso; presentar se ofrece cuando ha terminado. Plazo: del 1
/// al 30 de enero del año siguiente. Una segunda presentación es una sustitutiva de la última.</item>
/// <item>No se ofrece presentar con facturas del año con asiento y sin registro en el libro de IVA (las mismas que bloquean el 303).</item>
/// </list>
/// </summary>
public sealed class Servicio390(IBaseDatos bd, ServicioPresentacionAeat presentacion, IReloj? reloj = null)
{
    public const string Modelo = Modelo390.Modelo;
    public const string Periodo = "0A";
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly Servicio303 _s303 = new(bd, presentacion, reloj);

    public ServicioPresentacionAeat Presentacion => presentacion;
    public DateTime Hoy => _reloj.Ahora.Date;
    public IReadOnlyList<(int Casilla, string Concepto)> Conceptos => Modelo390.Conceptos;

    public static (DateTime Desde, DateTime Hasta) Plazo(int ejercicio) => (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 30));

    public async Task<Ejercicio390> EjercicioAsync(ContextoSociedadEjercicio ctx, DatosEstadisticos390? datos, CancellationToken c = default)
    {
        var fin = new DateTime(ctx.Ejercicio, 12, 31);
        var entorno = await presentacion.EntornoAsync(c);
        var del = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c)).Where(p => p.Entorno == ServiciosAeat.Codigo(entorno))
            .OrderBy(p => p.FechaHora).ThenBy(p => p.Id).ToList();
        var ultima = del.LastOrDefault();
        var (d, bloqueos, avisos) = await CalcularAsync(ctx, datos, ultima is null ? null : new Sustitutiva390(ultima.Justificante), c);
        if (Hoy <= fin) bloqueos.Insert(0, $"El ejercicio {ctx.Ejercicio} aún no ha terminado: el 390 se presenta del {Plazo(ctx.Ejercicio).Desde:dd/MM/yyyy} al {Plazo(ctx.Ejercicio).Hasta:dd/MM/yyyy}.");
        else if (Hoy > Plazo(ctx.Ejercicio).Hasta && ultima is null)
            avisos.Add($"El plazo del 390 de {ctx.Ejercicio} terminó el {Plazo(ctx.Ejercicio).Hasta:dd/MM/yyyy}: si no lo presentó la gestoría, preséntalo cuanto antes.");
        var estado = Hoy <= fin ? Estado303.EnCurso
            : ultima is null ? Estado303.SinPresentar
            : ultima.Resultado != d.Resultado ? Estado303.Desactualizado
            : ultima.ComplementariaDe is not null ? Estado303.Complementaria
            : Estado303.Presentado;
        if (estado == Estado303.Desactualizado)
            avisos.Add($"Se presentó con un resultado anual de {Euros(ultima!.Resultado)} y ahora da {Euros(d.Resultado)}: si la diferencia es correcta, presenta una sustitutiva.");
        return new Ejercicio390(ctx.Ejercicio, fin, estado, d.Resultado, ultima, del, bloqueos, avisos);
    }

    /// <summary>
    /// La declaración del ejercicio con sus casillas y su fichero. Con <paramref name="sustitutiva"/>, sustituye a la última presentada
    /// desde el programa en el entorno de la BD. Antes de acabar el año se genera igual (para validarla), con un aviso.
    /// </summary>
    public async Task<Resultado<(Declaracion390 Declaracion, DeclaracionAeat Aeat)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, DatosEstadisticos390? datos, bool sustitutiva,
        CancellationToken c = default)
    {
        var registro = $"Modelo 390 {ctx.Ejercicio}";
        PresentacionGuardada? anterior = null;
        if (sustitutiva)
        {
            var entorno = await presentacion.EntornoAsync(c);
            anterior = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c)).Where(p => p.Entorno == ServiciosAeat.Codigo(entorno))
                .OrderBy(p => p.FechaHora).ThenBy(p => p.Id).LastOrDefault();
            if (anterior is null)
                return Resultado<(Declaracion390, DeclaracionAeat)>.Fallo(new Error("Modelo390.SinAnterior", $"No consta presentado el 390 de {ctx.Ejercicio} desde el programa.", registro,
                    "Una sustitutiva necesita el justificante de la declaración anterior.", "Si lo presentó la gestoría, pídele el justificante; la sustitutiva con justificante externo aún no está disponible."));
        }
        var (d, _, _) = await CalcularAsync(ctx, datos, anterior is null ? null : new Sustitutiva390(anterior.Justificante), c);
        if (d.Casillas.Any(kv => kv.Value < 0m && kv.Key is 1 or 2 or 3 or 4 or 5 or 6))
            return Resultado<(Declaracion390, DeclaracionAeat)>.Fallo(new Error("Modelo390.Negativo", $"El 390 de {ctx.Ejercicio} tiene bases devengadas negativas.", registro,
                string.Join(" ", d.Errores), "Revisa las facturas rectificativas del año en el informe para la gestoría."));
        string fichero;
        try { fichero = Fichero390.Generar(d, ProgramaAeat.Actual); }
        catch (ArgumentOutOfRangeException ex)
        {
            return Resultado<(Declaracion390, DeclaracionAeat)>.Fallo(new Error("Modelo390.Fichero", $"No se puede escribir el fichero del 390 de {ctx.Ejercicio}.", registro, ex.Message,
                "Revisa la casilla que se indica: el diseño de la AEAT no admite ese importe."));
        }
        var resumen = new List<(string, string)>
        {
            ("Total cuotas devengadas (34)", Euros(d.Casilla(34))),
            ("Suma de deducciones (64)", Euros(d.Casilla(64))),
            ("Resultado régimen general (65)", Euros(d.Casilla(65))),
        };
        if (d.Casilla(85) != 0m) resumen.Add(("Compensación de cuotas del ejercicio anterior (85)", Euros(d.Casilla(85))));
        resumen.Add(("Resultado de la liquidación anual (86)", Euros(d.Casilla(86))));
        resumen.Add(("Total volumen de operaciones (108)", Euros(d.Casilla(108))));
        var aeat = new DeclaracionAeat(Modelo, ctx.Ejercicio, Periodo, fichero, d.Declarante.Nif, d.Declarante.RazonSocial, 'N', 0m, d.Resultado, null,
            anterior?.Justificante, resumen, Fichero390.LeerNif);
        return Resultado<(Declaracion390, DeclaracionAeat)>.Correcto((d, aeat));
    }

    private async Task<(Declaracion390 D, List<string> Bloqueos, List<string> Avisos)> CalcularAsync(ContextoSociedadEjercicio ctx, DatosEstadisticos390? datos, Sustitutiva390? sustitutiva,
        CancellationToken c)
    {
        var (lineas, _, _, iva) = await new LectorLineasIva(bd).CalcularAsync(ctx, c);
        var declarante = await _s303.DeclaranteAsync(ctx);
        var trimestres = new List<Declaracion303>();
        for (var t = 1; t <= 4; t++)
            trimestres.Add(Modelo303.Calcular(iva.Modelo303, ctx.Ejercicio, t, declarante, await _s303.PendienteCompensarAsync(ctx, t)));
        var bienes = await CuentasBienesIntraAsync(ctx);
        var d = Modelo390.Calcular(lineas, iva, trimestres, ctx.Ejercicio, declarante, datos, l => l.CuentaIva is { } cta && bienes.Contains(cta.Trim()), sustitutiva);
        var bloqueos = new List<string>(d.Errores);
        for (var t = 1; t <= 4; t++)
            if (Hoy > CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, t)) bloqueos.AddRange(await _s303.SinRegistroAsync(ctx, t));
        var avisos = new List<string>(d.Avisos);
        if (Hoy <= new DateTime(ctx.Ejercicio, 12, 31))
            avisos.Insert(0, $"El ejercicio {ctx.Ejercicio} aún no ha terminado: este 390 es provisional (lleva lo contabilizado hasta hoy).");
        return (d, bloqueos, avisos);
    }

    /// <summary>Cuentas de IVA de las adquisiciones intracomunitarias de bienes del ejercicio (soportado y repercutido · bienes).</summary>
    private Task<IReadOnlySet<string>> CuentasBienesIntraAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync<IReadOnlySet<string>>(async s =>
    {
        var cols = new[] { "CuentaIVAIntracomunitarioSoportadoBienes", "CuentaIVAIntracomunitarioRepercutidoBienes" }.Where(x => s.LeerEsquema().ExisteColumna("SociedadesEjercicios", x)).ToList();
        if (cols.Count == 0) return new HashSet<string>();
        var fila = await s.ConsultarPrimeroAsync($"SELECT {string.Join(", ", cols)} FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => cols.Select(x => f.Texto(x)?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Cast<string>().ToList(), ctx.IdSociedadEjercicio);
        return new HashSet<string>(fila ?? [], StringComparer.Ordinal);
    });

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
