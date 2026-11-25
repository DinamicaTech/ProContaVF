using System.Globalization;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Estado del 303 de un trimestre en la pantalla.</summary>
public enum Estado303
{
    /// <summary>El trimestre no ha terminado.</summary>
    EnCurso,
    /// <summary>Terminado y sin presentar desde el programa ni, que se sepa, por la gestoría.</summary>
    SinPresentar,
    /// <summary>Presentado (con su CSV).</summary>
    Presentado,
    /// <summary>Presentado y después rectificado con una complementaria.</summary>
    Complementaria,
    /// <summary>Presentado, pero el 303 que se calcula ahora da otro resultado: puede hacer falta una complementaria.</summary>
    Desactualizado,
}

/// <summary>Un trimestre en la pantalla del 303: su estado, el resultado calculado, la última presentación (del entorno de la BD) y lo que hay que revisar.</summary>
public sealed record Trimestre303(int Trimestre, DateTime Fin, Estado303 Estado, decimal ResultadoCalculado, decimal ResultadoAutoliquidacion, char FormaPago,
    PresentacionGuardada? Ultima, IReadOnlyList<PresentacionGuardada> Presentaciones, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos)
{
    public string Periodo => $"{Trimestre}T";
    public bool Terminado => Estado != Estado303.EnCurso;
    public bool SePuedePresentar => Terminado && Bloqueos.Count == 0;
}

/// <summary>
/// Modelo 303 de cada trimestre (contabilidad/modelos-tributarios/modelo-303): el estado de los cuatro trimestres, la declaración con
/// sus casillas (<see cref="Modelo303"/>) y su fichero (<see cref="Fichero303"/>), que la pieza común valida y presenta
/// (<see cref="ServicioPresentacionAeat"/>).
/// <list type="bullet">
/// <item>Cuotas a compensar de periodos anteriores (110): el saldo deudor de las 4700 el último día del trimestre sin la liquidación del
/// propio trimestre, como la liquidación del IVA (V4).</item>
/// <item>No se ofrece presentar si hay facturas del trimestre con asiento contabilizado y sin registro en el libro de IVA (no estarían en
/// el 303); se avisa de los registros con fecha de expedición de un trimestre anterior (por si hace falta una complementaria de aquel).</item>
/// <item>Versión del programa y NIF de la empresa desarrolladora del bloque &lt;AUX&gt;: <see cref="ProgramaAeat.Actual"/> (B62344502,
/// propietario 2026-11-17 15:33).</item>
/// </list>
/// </summary>
public sealed class Servicio303(IBaseDatos bd, ServicioPresentacionAeat presentacion, IReloj? reloj = null)
{
    public const string Modelo = "303";
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public ServicioPresentacionAeat Presentacion => presentacion;

    public DateTime Hoy => _reloj.Ahora.Date;

    /// <summary>Los cuatro trimestres del ejercicio con su estado.</summary>
    public async Task<IReadOnlyList<Trimestre303>> TrimestresAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var (lineas, _, _, iva) = await new LectorLineasIva(bd).CalcularAsync(ctx, c);
        var entorno = await presentacion.EntornoAsync(c);
        var todas = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c)).Where(p => p.Entorno == ServiciosAeat.Codigo(entorno)).ToList();
        var declarante = await DeclaranteAsync(ctx);
        var r = new List<Trimestre303>();
        for (var t = 1; t <= 4; t++)
        {
            var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, t);
            var del = todas.Where(p => p.Periodo == $"{t}T").OrderBy(p => p.FechaHora).ThenBy(p => p.Id).ToList();
            var ultima = del.LastOrDefault();
            var calculado = iva.Modelo303.Resultado(t);
            var pendiente = await PendienteCompensarAsync(ctx, t);
            var d = Modelo303.Calcular(iva.Modelo303, ctx.Ejercicio, t, declarante, pendiente, Rectificativa(ultima));
            if (Hoy > fin && FueraDePlazoDomiciliacion(d, Hoy) is not null)
                d = Modelo303.Calcular(iva.Modelo303, ctx.Ejercicio, t, declarante, pendiente, Rectificativa(ultima), conNrc: true);
            var bloqueos = new List<string>();
            var avisos = new List<string>();
            if (Hoy > fin)
            {
                bloqueos.AddRange(await SinRegistroAsync(ctx, t));
                avisos.AddRange(ExpedicionAnterior(lineas, ctx.Ejercicio, t));
                bloqueos.AddRange(d.Errores);
                avisos.AddRange(d.Avisos);
                if (FueraDePlazoDomiciliacion(d, Hoy) is { } dom) avisos.Add(dom);
            }
            var estado = Hoy <= fin ? Estado303.EnCurso
                : ultima is null ? Estado303.SinPresentar
                : ultima.DistintoDe(calculado) ? Estado303.Desactualizado
                : ultima.ComplementariaDe is not null ? Estado303.Complementaria
                : Estado303.Presentado;
            if (estado == Estado303.Desactualizado)
                avisos.Add($"Se presentó con un resultado de {Euros(ultima!.Resultado)} y ahora da {Euros(calculado)}: si la diferencia es correcta, presenta una complementaria.");
            r.Add(new Trimestre303(t, fin, estado, calculado, d.Resultado, d.TipoDeclaracion, ultima, del, bloqueos, avisos));
        }
        return r;
    }

    /// <summary>
    /// Las casillas del trimestre como las calcula el programa, sin generar el fichero ni mirar si ha terminado: para compararlas con el
    /// justificante de la AEAT (contabilidad/modelos-tributarios/presentacion 2026-11-25), también de ejercicios cuyo fichero no se genera.
    /// </summary>
    public async Task<Declaracion303> CalcularAsync(ContextoSociedadEjercicio ctx, int trimestre, CancellationToken c = default)
    {
        var (_, _, _, iva) = await new LectorLineasIva(bd).CalcularAsync(ctx, c);
        var declarante = await DeclaranteAsync(ctx);
        var pendiente = await PendienteCompensarAsync(ctx, trimestre);
        return Modelo303.Calcular(iva.Modelo303, ctx.Ejercicio, trimestre, declarante, pendiente, Rectificativa(null));
    }

    /// <summary>
    /// La declaración del trimestre con sus casillas y su fichero. Con <paramref name="complementaria"/>, una rectificativa de la última
    /// presentada (su justificante y lo que salió a ingresar van en la página 3).
    /// </summary>
    /// <param name="nrc">NRC del pago cuando sale a ingresar fuera del plazo de domiciliación (forma I); se ignora si se domicilia.</param>
    public async Task<Resultado<(Declaracion303 Declaracion, DeclaracionAeat Aeat)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, int trimestre, bool complementaria, string? nrc = null,
        CancellationToken c = default)
    {
        var registro = $"Modelo 303 {trimestre}T {ctx.Ejercicio}";
        if (trimestre is < 1 or > 4) return Resultado<(Declaracion303, DeclaracionAeat)>.Fallo(new Error("Modelo303.Trimestre", "El trimestre va de 1 a 4.", registro));
        var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, trimestre);
        if (Hoy <= fin)
            return Resultado<(Declaracion303, DeclaracionAeat)>.Fallo(new Error("Modelo303.EnCurso", $"El {trimestre}T de {ctx.Ejercicio} aún no ha terminado.", registro,
                "El 303 se presenta cuando el trimestre ha terminado.", $"Vuelve a partir del {fin.AddDays(1):dd/MM/yyyy}."));
        var (lineas, _, _, iva) = await new LectorLineasIva(bd).CalcularAsync(ctx, c);
        var especial = Modelo390.ProrrataEspecial(lineas);
        var entorno = await presentacion.EntornoAsync(c);
        PresentacionGuardada? anterior = null;
        if (complementaria)
        {
            anterior = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c))
                .Where(p => p.Periodo == $"{trimestre}T" && p.Entorno == ServiciosAeat.Codigo(entorno)).OrderBy(p => p.FechaHora).ThenBy(p => p.Id).LastOrDefault();
            if (anterior is null)
                return Resultado<(Declaracion303, DeclaracionAeat)>.Fallo(new Error("Modelo303.SinAnterior", $"No consta presentado el {trimestre}T de {ctx.Ejercicio} desde el programa.", registro,
                    "Una complementaria (autoliquidación rectificativa) necesita el justificante de la presentación anterior.",
                    "Si lo presentó la gestoría, pídele el justificante; la complementaria con justificante externo aún no está disponible."));
        }
        var declarante = await DeclaranteAsync(ctx);
        var pendiente = await PendienteCompensarAsync(ctx, trimestre);
        var d = Modelo303.Calcular(iva.Modelo303, ctx.Ejercicio, trimestre, declarante, pendiente, Rectificativa(anterior));
        // Fuera del plazo de domiciliación se paga antes y se presenta con el NRC (forma I).
        if (FueraDePlazoDomiciliacion(d, Hoy) is not null)
            d = Modelo303.Calcular(iva.Modelo303, ctx.Ejercicio, trimestre, declarante, pendiente, Rectificativa(anterior), conNrc: true);
        d = d with { OpcionProrrataEspecial = especial };
        var fichero = Fichero303.Generar(d, ProgramaAeat.Actual);
        var resumen = new List<(string, string)>
        {
            ("Total cuota devengada (27)", Euros(d.Casilla(27))),
            ("Total a deducir (45)", Euros(d.Casilla(45))),
            ("Resultado régimen general (46)", Euros(d.Casilla(46))),
        };
        if (d.Casilla(78) != 0m) resumen.Add(("Cuotas a compensar aplicadas (78)", Euros(d.Casilla(78))));
        if (d.Casilla(70) != 0m) resumen.Add(("Ingresado en la anterior (70)", Euros(d.Casilla(70))));
        resumen.Add(("Resultado (71)", Euros(d.Casilla(71))));
        var aeat = new DeclaracionAeat(Modelo, ctx.Ejercicio, d.Periodo, fichero, declarante.Nif, declarante.RazonSocial, d.TipoDeclaracion, d.ImporteAIngresar,
            d.ResultadoLiquidacion, declarante.Iban, anterior?.Justificante, resumen, Fichero303.LeerNif) { Nrc = d.TipoDeclaracion == 'I' ? nrc : null };
        return Resultado<(Declaracion303, DeclaracionAeat)>.Correcto((d, aeat));
    }

    /// <summary>Lo que impide presentar la declaración, además de lo que diga la AEAT (null si nada).</summary>
    public async Task<IReadOnlyList<string>> BloqueosAsync(ContextoSociedadEjercicio ctx, Declaracion303 d, CancellationToken c = default)
    {
        var r = new List<string>(await SinRegistroAsync(ctx, d.Trimestre));
        r.AddRange(d.Errores);
        return r;
    }

    /// <summary>
    /// La AEAT solo admite domiciliar hasta 5 días antes del fin del plazo: hasta el 15 del mes siguiente al trimestre (en el 4T,
    /// hasta el 25 de enero). Fuera de ese plazo, lo que sale a ingresar se paga antes y se presenta con el NRC (forma I, propietario
    /// 2026-11-17 15:32). Devuelve la explicación, o null si se puede domiciliar o no sale a ingresar.
    /// </summary>
    public static string? FueraDePlazoDomiciliacion(Declaracion303 d, DateTime hoy)
    {
        if (d.TipoDeclaracion is not ('U' or 'I')) return null;
        var (desde, hasta) = PlazoDomiciliacion(d.Ejercicio, d.Trimestre);
        if (hoy.Date >= desde && hoy.Date <= hasta) return null;
        return $"El {d.Periodo} sale a ingresar ({Euros(d.Casilla(71))}) y el plazo para domiciliarlo " + (hoy.Date < desde ? $"empieza el {desde:dd/MM/yyyy}" : $"terminó el {hasta:dd/MM/yyyy}") +
            ": antes de presentar hay que pagarlo en el banco o en la Sede de la AEAT y escribir el NRC que den.";
    }

    public static (DateTime Desde, DateTime Hasta) PlazoDomiciliacion(int ejercicio, int trimestre) => trimestre == 4
        ? (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 25))
        : (new DateTime(ejercicio, 3 * trimestre + 1, 1), new DateTime(ejercicio, 3 * trimestre + 1, 15));

    // ---------- lecturas ----------

    private static Rectificativa303? Rectificativa(PresentacionGuardada? anterior) =>
        anterior is null ? null : new Rectificativa303(anterior.Justificante, anterior.FormaPago is "U" or "I" ? anterior.Importe : 0m);

    /// <summary>Casilla 110: crédito de la 4700 el último día del trimestre, sin la liquidación del propio trimestre.</summary>
    internal async Task<decimal> PendienteCompensarAsync(ContextoSociedadEjercicio ctx, int trimestre)
    {
        var saldos = await new ServicioLiquidacionIva(bd, null!, null!).SaldosAsync(ctx, CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, trimestre),
            ServicioLiquidacionIva.Lote(ctx.Ejercicio, trimestre));
        return saldos.Where(s => s.Cuenta.StartsWith(CalculoLiquidacionIva.PrefijoCredito, StringComparison.Ordinal) && s.Saldo > 0m).Sum(s => s.Saldo);
    }

    internal Task<Declarante303> DeclaranteAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var conIban = s.LeerEsquema().ExisteColumna("Sociedades", "IbanDomiciliacion");
        return await s.ConsultarPrimeroAsync($"SELECT Nombre, CIF{(conIban ? ", IbanDomiciliacion" : "")} FROM Sociedades WHERE IdSociedad = ?",
            f => new Declarante303((f.Texto("CIF") ?? "").Trim().ToUpperInvariant(), (f.Texto("Nombre") ?? "").Trim(), conIban ? f.Texto("IbanDomiciliacion")?.Trim() : null),
            ctx.IdSociedad) ?? new Declarante303("", "", null);
    });

    /// <summary>Facturas del trimestre con asiento contabilizado y sin registro en el libro de IVA: el 303 no las tendría.</summary>
    internal Task<IReadOnlyList<string>> SinRegistroAsync(ContextoSociedadEjercicio ctx, int trimestre) => bd.EjecutarAsync<IReadOnlyList<string>>(async s =>
    {
        var desde = new DateTime(ctx.Ejercicio, 3 * trimestre - 2, 1);
        var hasta = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, trimestre);
        var ventas = await s.ConsultarAsync(
            "SELECT f.NumeroFactura FROM Facturas AS f INNER JOIN Asientos AS a ON f.IdAsientoContable = a.IdAsiento WHERE f.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 " +
            "AND a.FechaContable >= ? AND a.FechaContable <= ? AND NOT EXISTS (SELECT r.IdIVARepercutido FROM IVARepercutido AS r WHERE r.IdAsiento = a.IdAsiento OR r.IdFactura = f.IdFactura)",
            f => (f.Texto("NumeroFactura") ?? "").Trim(), ctx.IdSociedadEjercicio, desde, hasta);
        var compras = s.LeerEsquema().ExisteColumna("FacturasCompra", "IdAsientoContable")
            ? await s.ConsultarAsync(
                "SELECT c.NumeroFactura FROM FacturasCompra AS c INNER JOIN Asientos AS a ON c.IdAsientoContable = a.IdAsiento WHERE c.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 " +
                "AND a.FechaContable >= ? AND a.FechaContable <= ? AND NOT EXISTS (SELECT r.IdIVASoportado FROM IVASoportado AS r WHERE r.IdAsiento = a.IdAsiento)",
                f => (f.Texto("NumeroFactura") ?? "").Trim(), ctx.IdSociedadEjercicio, desde, hasta)
            : [];
        var r = new List<string>();
        if (ventas.Count > 0)
            r.Add($"Hay {ventas.Count} factura{(ventas.Count == 1 ? "" : "s")} de venta del {trimestre}T con asiento y sin registro en el libro de IVA repercutido " +
                $"({string.Join(", ", ventas.Take(5))}{(ventas.Count > 5 ? "…" : "")}): no entrarían en el 303. Vuelve a contabilizarlas desde Ventas › Facturas.");
        if (compras.Count > 0)
            r.Add($"Hay {compras.Count} factura{(compras.Count == 1 ? "" : "s")} de compra del {trimestre}T con asiento y sin registro en el libro de IVA soportado " +
                $"({string.Join(", ", compras.Take(5))}{(compras.Count > 5 ? "…" : "")}): no entrarían en el 303. Vuelve a registrarlas desde Compras › Facturas recibidas.");
        return r;
    });

    /// <summary>Aviso de los registros del trimestre con fecha de expedición de un trimestre anterior (por si hace falta una complementaria de aquel).</summary>
    internal static IReadOnlyList<string> ExpedicionAnterior(IReadOnlyList<LineaIva> lineas, int ejercicio, int trimestre)
    {
        var inicio = new DateTime(ejercicio, 3 * trimestre - 2, 1);
        var anteriores = lineas.Where(l => l.Trimestre == trimestre && l.FechaExpedicion is { } f && f < inicio)
            .GroupBy(l => (l.IdAsiento, l.Factura, l.Tercero, l.FechaExpedicion)).Select(g => g.Key).OrderBy(k => k.FechaExpedicion).ToList();
        if (anteriores.Count == 0) return [];
        var ejemplos = string.Join("; ", anteriores.Take(5).Select(k => $"{k.Tercero} {k.Factura} del {k.FechaExpedicion:dd/MM/yyyy}"));
        return [$"{anteriores.Count} registro{(anteriores.Count == 1 ? "" : "s")} del {trimestre}T tiene{(anteriores.Count == 1 ? "" : "n")} fecha de expedición de un trimestre anterior " +
            $"({ejemplos}{(anteriores.Count > 5 ? "…" : "")}). Se declaran en este trimestre; si debían ir en aquel, hace falta una complementaria de aquel trimestre."];
    }

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
