using System.Globalization;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Un periodo del 202 en la pantalla de modelos: su estado (los mismos que el 303), el pago calculado, la forma de pago, la última presentación y lo que impide presentar o conviene revisar.</summary>
public sealed record Periodo202(int Periodo, DateTime Desde, DateTime Hasta, Estado303 Estado, decimal PagoCalculado, decimal Resultado, char FormaPago,
    PresentacionGuardada? Ultima, IReadOnlyList<PresentacionGuardada> Presentaciones, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos)
{
    public string Codigo => $"{Periodo}P";
    public bool Terminado => Estado != Estado303.EnCurso;
    public bool SePuedePresentar => Terminado && Bloqueos.Count == 0;
}

/// <summary>
/// Modelo 202 de cada periodo (contabilidad/modelos-tributarios/modelo-202), con el mismo contrato que <see cref="Servicio303"/>: el
/// estado de los tres periodos, la declaración (<see cref="Modelo202"/>) y su fichero (<see cref="Fichero202"/>), que la pieza común
/// valida y presenta.
/// <list type="bullet">
/// <item>Base (art. 40.2 LIS): del Impuesto sobre Sociedades del ejercicio de referencia grabado en el programa (ImpuestosSociedades:
/// cuota líquida menos las retenciones que dedujo, sin las de las cuentas de pagos fraccionados) o, si no, de su asiento en la
/// contabilidad (cargo a la 630 a 31/12 menos los abonos a las 473 que no son de pagos fraccionados), como el de la gestoría.</item>
/// <item>Cuentas de pagos fraccionados: las 473 asignadas al modelo 202 en los datos fiscales importados del IS y las que, en el
/// ejercicio de referencia, solo reciben cargos del banco del 1 al 25 de abril, octubre o diciembre (los pagos del 202) o se llaman
/// «pago a cuenta» o «pago fraccionado».</item>
/// <item>Cabecera: inicio del periodo impositivo el 1 de enero; cifra de negocios del ejercicio anterior inferior a 1 M€ (cuentas 70 y
/// 752) marcada; tipo de gravamen el de Parámetros y tablas › Ejercicios («tramo/general») o, sin él, el general del 25 %.</item>
/// <item>Encaje con el IS: lo presentado con importe debe estar cargado en una cuenta de pagos fraccionados del ejercicio (la 473 que
/// el Impuesto sobre Sociedades descuenta); si no, se avisa.</item>
/// </list>
/// </summary>
public sealed class Servicio202(IBaseDatos bd, ServicioPresentacionAeat presentacion, IReloj? reloj = null)
{
    public const string Modelo = Modelo202.Modelo;
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly Servicio303 _s303 = new(bd, presentacion, reloj);

    public ServicioPresentacionAeat Presentacion => presentacion;
    public DateTime Hoy => _reloj.Ahora.Date;
    public IReadOnlyList<(int Casilla, string Concepto)> Conceptos => Modelo202.Conceptos;

    public async Task<IReadOnlyList<Periodo202>> PeriodosAsync(ContextoSociedadEjercicio ctx, DatosActividad202? datos, CancellationToken c = default)
    {
        var entorno = await presentacion.EntornoAsync(c);
        var todas = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c)).Where(p => p.Entorno == ServiciosAeat.Codigo(entorno)).ToList();
        var cargos = await CargosPagosFraccionadosAsync(ctx, ctx.Ejercicio);
        var r = new List<Periodo202>();
        for (var periodo = 1; periodo <= 3; periodo++)
        {
            var (desde, hasta) = Modelo202.Plazo(ctx.Ejercicio, periodo);
            var del = todas.Where(p => p.Periodo == $"{periodo}P").OrderBy(p => p.FechaHora).ThenBy(p => p.Id).ToList();
            var ultima = del.LastOrDefault();
            var d = await DeclararAsync(ctx, periodo, datos, ultima, null);
            var bloqueos = new List<string>();
            var avisos = new List<string>();
            if (Hoy >= desde)
            {
                bloqueos.AddRange(d.Errores);
                avisos.AddRange(d.Avisos);
                if (FueraDePlazoDomiciliacion(d, Hoy) is { } dom) avisos.Add(dom);
                if (Hoy > hasta && ultima is null) avisos.Add($"El plazo del {d.Codigo} terminó el {hasta:dd/MM/yyyy}: si no lo presentó la gestoría, preséntalo cuanto antes (con recargo).");
            }
            else bloqueos.Add($"El {periodo}P de {ctx.Ejercicio} se presenta del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}.");
            var pago = d.Pago;
            var estado = Hoy < desde ? Estado303.EnCurso
                : ultima is null ? Estado303.SinPresentar
                : ultima.DistintoDe(pago) ? Estado303.Desactualizado
                : ultima.ComplementariaDe is not null ? Estado303.Complementaria
                : Estado303.Presentado;
            if (estado == Estado303.Desactualizado)
                avisos.Add($"Se presentó con un pago de {Euros(ultima!.Resultado)} y ahora da {Euros(pago)}: " + (ultima.Resultado < pago ? "si es correcto, presenta una complementaria." : "una complementaria no puede bajarlo."));
            var ingresado = del.Sum(p => p.FormaPago is "U" or "I" ? p.Importe : 0m);
            if (ingresado > 0m)
            {
                var cargado = cargos.Where(x => x.Fecha >= desde && x.Fecha < desde.AddMonths(2)).Sum(x => x.Importe);
                if (Math.Abs(cargado - ingresado) > 0.05m)
                    avisos.Add($"Se ingresaron {Euros(ingresado)} por el {d.Codigo} y las cuentas de pagos fraccionados (473) tienen {Euros(cargado)} cargados desde el {desde:dd/MM}: " +
                        "contabiliza el cargo del banco en la 473 de pagos a cuenta para que el Impuesto sobre Sociedades lo descuente.");
            }
            r.Add(new Periodo202(periodo, desde, hasta, estado, pago, d.Resultado, d.TipoDeclaracion, ultima, del, bloqueos, avisos));
        }
        return r;
    }

    /// <summary>La declaración del periodo y su fichero. Con <paramref name="complementaria"/>, complementaria de la última presentada desde el programa.</summary>
    public async Task<Resultado<(Declaracion202 Declaracion, DeclaracionAeat Aeat)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, int periodo, bool complementaria, DatosActividad202? datos,
        string? nrc = null, CancellationToken c = default)
    {
        var registro = $"Modelo 202 {periodo}P {ctx.Ejercicio}";
        if (periodo is < 1 or > 3)
            return Resultado<(Declaracion202, DeclaracionAeat)>.Fallo(new Error("Modelo202.Periodo", "El periodo va de 1 a 3 (abril, octubre y diciembre).", registro));
        var (desde, hasta) = Modelo202.Plazo(ctx.Ejercicio, periodo);
        if (Hoy < desde)
            return Resultado<(Declaracion202, DeclaracionAeat)>.Fallo(new Error("Modelo202.EnCurso", $"El plazo del {periodo}P de {ctx.Ejercicio} aún no ha empezado.", registro,
                $"El 202 del {periodo}P se presenta del {desde:dd/MM/yyyy} al {hasta:dd/MM/yyyy}.", $"Vuelve a partir del {desde:dd/MM/yyyy}."));
        PresentacionGuardada? anterior = null;
        if (complementaria)
        {
            var entorno = await presentacion.EntornoAsync(c);
            anterior = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c)).Where(p => p.Periodo == $"{periodo}P" && p.Entorno == ServiciosAeat.Codigo(entorno))
                .OrderBy(p => p.FechaHora).ThenBy(p => p.Id).LastOrDefault();
            if (anterior is null)
                return Resultado<(Declaracion202, DeclaracionAeat)>.Fallo(new Error("Modelo202.SinAnterior", $"No consta presentado el {periodo}P de {ctx.Ejercicio} desde el programa.", registro,
                    "Una complementaria necesita el justificante de la anterior.", "Si lo presentó la gestoría, pídele el justificante."));
        }
        var d = await DeclararAsync(ctx, periodo, datos, anterior, nrc);
        if (d.Casillas.Values.Any(v => v < 0m))
            return Resultado<(Declaracion202, DeclaracionAeat)>.Fallo(new Error("Modelo202.Negativo", $"El 202 del {periodo}P no se puede generar.", registro, string.Join(" ", d.Errores),
                "Corrige lo que se indica y vuelve a intentarlo."));
        var fichero = Fichero202.Generar(d, ProgramaAeat.Actual);
        var resumen = new List<(string, string)>
        {
            ($"Impuesto sobre Sociedades de {Modelo202.EjercicioBase(ctx.Ejercicio, periodo)} ({d.Origen?.Origen ?? "no hay"})",
                d.Origen is null ? "—" : $"cuota {Euros(d.Origen.CuotaLiquida)} − retenciones {Euros(d.Origen.Retenciones)}"),
            ("Base del pago fraccionado (01)", Euros(d.Casilla(1))),
        };
        if (d.Casilla(2) != 0m) resumen.Add(("Resultado de la declaración anterior (02)", Euros(d.Casilla(2))));
        resumen.Add(("A ingresar (03) = 18 % de la base", Euros(d.Casilla(3))));
        var aeat = new DeclaracionAeat(Modelo, ctx.Ejercicio, d.Codigo, fichero, d.Declarante.Nif, d.Declarante.RazonSocial, d.TipoDeclaracion, d.ImporteAIngresar, d.Pago,
            d.Declarante.Iban, anterior?.Justificante, resumen, Fichero202.LeerNif) { Nrc = d.TipoDeclaracion == 'I' ? nrc : null };
        return Resultado<(Declaracion202, DeclaracionAeat)>.Correcto((d, aeat));
    }

    public static string? FueraDePlazoDomiciliacion(Declaracion202 d, DateTime hoy)
    {
        if (d.TipoDeclaracion is not ('U' or 'I')) return null;
        var (desde, hasta) = Modelo202.PlazoDomiciliacion(d.Ejercicio, d.Periodo);
        if (hoy.Date >= desde && hoy.Date <= hasta) return null;
        return $"El 202 del {d.Codigo} sale a ingresar ({Euros(d.Casilla(3))}) y el plazo para domiciliarlo " + (hoy.Date < desde ? $"empieza el {desde:dd/MM/yyyy}" : $"terminó el {hasta:dd/MM/yyyy}") +
            ": antes de presentar hay que pagarlo en el banco o en la Sede de la AEAT y escribir el NRC que den.";
    }

    private async Task<Declaracion202> DeclararAsync(ContextoSociedadEjercicio ctx, int periodo, DatosActividad202? datos, PresentacionGuardada? anterior, string? nrc)
    {
        // Sin datos escritos, el CNAE de la sociedad (Parámetros y tablas › Empresas, esquema 72).
        datos ??= await bd.EjecutarAsync(async s => s.LeerEsquema().ExisteColumna("Sociedades", "Cnae")
            && await s.EscalarAsync("SELECT Cnae FROM Sociedades WHERE IdSociedad = ?", ctx.IdSociedad) is string cnae && cnae.Trim().Length > 0 ? new DatosActividad202(cnae.Trim()) : null);
        var declarante = await _s303.DeclaranteAsync(ctx);
        var origen = await BaseAsync(ctx.IdSociedad, Modelo202.EjercicioBase(ctx.Ejercicio, periodo));
        var menor1M = await CifraNegociosAsync(ctx.IdSociedad, ctx.Ejercicio - 1) is decimal cn && cn < 1_000_000m;
        var tipo = await TipoGravamenAsync(ctx);
        var rect = anterior is null ? null : new Rectificativa202(anterior.Justificante, anterior.FormaPago is "U" or "I" ? anterior.Importe : 0m);
        var d = Modelo202.Calcular(ctx.Ejercicio, periodo, declarante, origen, datos, menor1M, tipo, rect);
        return FueraDePlazoDomiciliacion(d, Hoy) is not null ? Modelo202.Calcular(ctx.Ejercicio, periodo, declarante, origen, datos, menor1M, tipo, rect, conNrc: true) : d;
    }

    // ---------- lecturas ----------

    private Task<int?> IdEjercicioAsync(ISesionDatos s, string idSociedad, int ejercicio) =>
        s.ConsultarPrimeroAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?", f => (int?)f.Entero("IdSociedadEjercicio"), idSociedad, (short)ejercicio);

    /// <summary>La base del pago fraccionado con el Impuesto sobre Sociedades de <paramref name="ejercicio"/>; null si no está ni grabado ni contabilizado.</summary>
    public Task<BasePagoFraccionado?> BaseAsync(string idSociedad, int ejercicio) => bd.EjecutarAsync(async s =>
    {
        var id = await IdEjercicioAsync(s, idSociedad, ejercicio);
        if (id is null) return null;
        var esquema = s.LeerEsquema();
        var pf = (await CuentasPagosFraccionadosAsync(s, id.Value, ejercicio)).ToHashSet(StringComparer.Ordinal);
        if (esquema.ExisteTabla("ImpuestosSociedades"))
        {
            var fila = await s.ConsultarPrimeroAsync("SELECT IdImpuestoSociedades, CuotaLiquida FROM ImpuestosSociedades WHERE IdSociedadEjercicio = ?",
                f => (Id: f.Entero("IdImpuestoSociedades"), Cuota: f.DecimalNulo("CuotaLiquida") ?? 0m), id.Value);
            if (fila.Id != 0)
            {
                var aeat = await s.ConsultarAsync("SELECT Cuenta FROM ImpuestosSociedadesDatosAeat WHERE IdImpuestoSociedades = ? AND Modelo = '202'", f => (f.Texto("Cuenta") ?? "").Trim(), fila.Id);
                foreach (var cta in aeat.Where(x => x.Length > 0)) pf.Add(cta);
                var ret = await s.ConsultarAsync("SELECT Cuenta, Valor FROM ImpuestosSociedadesRetenciones WHERE IdImpuestoSociedades = ?",
                    f => (Cuenta: (f.Texto("Cuenta") ?? "").Trim(), Valor: f.DecimalNulo("Valor") ?? 0m), fila.Id);
                return new BasePagoFraccionado(ejercicio, "Impuesto sobre Sociedades del programa", fila.Cuota, ret.Where(x => !pf.Contains(x.Cuenta)).Sum(x => x.Valor), [.. pf.Order()]);
            }
        }
        // Asiento del impuesto de la gestoría: cargo a la 630 a 31/12 y abonos a las 473.
        var apuntes = await s.ConsultarAsync(
            "SELECT p.IdAsiento, p.CuentaContable, p.ImporteDebe, p.ImporteHaber FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND (a.TipoAsiento = 0 OR a.TipoAsiento = 4) AND a.FechaContable >= ? " +
            "AND a.IdAsiento IN (SELECT q.IdAsiento FROM Apuntes AS q WHERE q.CuentaContable LIKE '6300%' AND q.ImporteDebe <> 0)",
            f => (Asiento: f.Entero("IdAsiento"), Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Debe: f.DecimalNulo("ImporteDebe") ?? 0m, Haber: f.DecimalNulo("ImporteHaber") ?? 0m),
            id.Value, new DateTime(ejercicio, 12, 1));
        if (apuntes.Count == 0) return null;
        var cuota = apuntes.Where(a => a.Cuenta.StartsWith("6300", StringComparison.Ordinal)).Sum(a => a.Debe - a.Haber);
        var retenciones = apuntes.Where(a => a.Cuenta.StartsWith("473", StringComparison.Ordinal) && !pf.Contains(a.Cuenta)).Sum(a => a.Haber - a.Debe);
        return new BasePagoFraccionado(ejercicio, "asiento del impuesto", cuota, retenciones, [.. pf.Order()]);
    });

    /// <summary>
    /// Subcuentas 473 de pagos fraccionados de un ejercicio: las que se llaman «pago a cuenta» o «pago fraccionado», o cuyos cargos del
    /// ejercicio son todos del banco (57) entre el 1 y el 25 de abril, octubre o diciembre.
    /// </summary>
    internal static async Task<IReadOnlyList<string>> CuentasPagosFraccionadosAsync(ISesionDatos s, int idEjercicio, int ejercicio)
    {
        var cargos = await s.ConsultarAsync(
            "SELECT p.CuentaContable, a.FechaContable, (SELECT COUNT(*) FROM Apuntes AS b WHERE b.IdAsiento = a.IdAsiento AND b.CuentaContable LIKE '57%') AS Banco " +
            "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento = 0 " +
            "AND p.CuentaContable LIKE '473%' AND p.ImporteDebe > 0",
            f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Fecha: f.Fecha("FechaContable"), Banco: f.Entero("Banco") > 0), idEjercicio);
        var nombres = await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo LIKE '473%'",
            f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").ToUpperInvariant()), idEjercicio);
        var r = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in nombres.Where(n => (n.Nombre.Contains("PAGO A C", StringComparison.Ordinal) || n.Nombre.Contains("FRACCION", StringComparison.Ordinal)) && !n.Nombre.Contains("RETEN", StringComparison.Ordinal)))
            r.Add(n.Codigo);
        foreach (var g in cargos.GroupBy(x => x.Cuenta))
            if (g.All(x => x.Banco && x.Fecha.Month is 4 or 10 or 12 && x.Fecha.Day <= 25)) r.Add(g.Key);
        return [.. r];
    }

    /// <summary>Cargos del ejercicio a las cuentas de pagos fraccionados (lo pagado por el 202).</summary>
    private Task<IReadOnlyList<(DateTime Fecha, decimal Importe)>> CargosPagosFraccionadosAsync(ContextoSociedadEjercicio ctx, int ejercicio) =>
        bd.EjecutarAsync<IReadOnlyList<(DateTime Fecha, decimal Importe)>>(async s =>
        {
            var cuentas = (await CuentasPagosFraccionadosAsync(s, ctx.IdSociedadEjercicio, ejercicio)).ToHashSet(StringComparer.Ordinal);
            var cargos = await s.ConsultarAsync(
                "SELECT p.CuentaContable, a.FechaContable, p.ImporteDebe FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento = 0 AND p.CuentaContable LIKE '473%' AND p.ImporteDebe > 0",
                f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Fecha: f.Fecha("FechaContable"), Importe: f.DecimalNulo("ImporteDebe") ?? 0m), ctx.IdSociedadEjercicio);
            return [.. cargos.Where(x => cuentas.Contains(x.Cuenta)).Select(x => (x.Fecha, x.Importe))];
        });

    /// <summary>Importe neto de la cifra de negocios de un ejercicio (abonos menos cargos de las 70 y la 752); null si el ejercicio no existe.</summary>
    private Task<decimal?> CifraNegociosAsync(string idSociedad, int ejercicio) => bd.EjecutarAsync(async s =>
    {
        var id = await IdEjercicioAsync(s, idSociedad, ejercicio);
        if (id is null) return (decimal?)null;
        var v = await s.EscalarAsync(
            "SELECT SUM(p.ImporteHaber) - SUM(p.ImporteDebe) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 " +
            "AND (a.TipoAsiento = 0 OR a.TipoAsiento = 4) AND (p.CuentaContable LIKE '70%' OR p.CuentaContable LIKE '752%')", id.Value);
        return v is null or DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    });

    /// <summary>Tipo de gravamen del ejercicio para la cabecera: «tramo/general» de Parámetros y tablas › Ejercicios o, sin él, el general del 25 %.</summary>
    private Task<string> TipoGravamenAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        if (!s.LeerEsquema().ExisteColumna("SociedadesEjercicios", "TipoImpuestoSociedades")) return "25";
        var f = await s.ConsultarPrimeroAsync("SELECT TipoImpuestoSociedades, TipoTramoReducidoIS FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            r => (General: r.DecimalNulo("TipoImpuestoSociedades"), Tramo: r.DecimalNulo("TipoTramoReducidoIS")), ctx.IdSociedadEjercicio);
        static string T(decimal v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        var general = f.General ?? 25m;
        return f.Tramo is decimal t && t > 0m ? $"{T(t)}/{T(general)}" : T(general);
    });

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
