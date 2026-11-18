using System.Globalization;
using System.Text.RegularExpressions;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Impuestos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// Modelo 200 de un ejercicio (contabilidad/modelos-tributarios/modelo-200): reúne lo que el programa ya tiene y lo pasa a
/// <see cref="Modelo200"/> y <see cref="Fichero200"/>; la pieza común (<see cref="ServicioPresentacionAeat"/>) lo valida y lo presenta.
/// <list type="bullet">
/// <item>Balance y pérdidas y ganancias: los saldos por subcuenta del módulo de balances (contabilidad/balances) a 31/12, con el asiento
/// del impuesto ya contabilizado.</item>
/// <item>Liquidación: la propuesta del Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades) con lo grabado en él o lo que
/// se le pase. Sus ajustes se clasifican por su descripción: «art. 21» o «exención» es la exención por transmisión de valores de entidades
/// residentes; los demás, «otras correcciones».</item>
/// <item>Retenciones: cada 473 que descuenta el IS, por lo que la AEAT le asigna en los datos fiscales importados (180 arrendamientos;
/// 193, 196, 187, 188 y 194 capital mobiliario; 202 pago fraccionado) o, sin ellos, por su código y nombre: 4735 o «alquiler»/«arrend»,
/// arrendamientos; las de pagos fraccionados (las del 202), por el mes del cargo; el resto, capital mobiliario.</item>
/// <item>Tipo reducido de las entidades con cifra de negocios del ejercicio anterior inferior a 1 M€ (clave 00088): cuentas 70 y 752 del
/// ejercicio anterior.</item>
/// </list>
/// </summary>
public sealed class Servicio200(IBaseDatos bd, ServicioPresentacionAeat presentacion, ServicioImpuestoSociedades impuesto)
{
    public const string Modelo = Modelo200.Modelo;
    public const string Periodo = "0A";

    public ServicioPresentacionAeat Presentacion => presentacion;

    /// <summary>
    /// La declaración y su fichero. <paramref name="datos"/> null: los de la BD (<see cref="DatosAsync"/>); <paramref name="entrada"/> null: lo grabado en el
    /// Impuesto sobre Sociedades del ejercicio.
    /// </summary>
    public async Task<Resultado<(Declaracion200 Declaracion, DeclaracionAeat Aeat)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, DatosDeclaracion200? datos = null,
        EntradaImpuestoSociedades? entrada = null, bool renunciaDevolucion = false, CancellationToken c = default)
    {
        datos ??= await DatosAsync(ctx) with { RenunciaDevolucion = renunciaDevolucion };
        var registro = $"Modelo 200 {ctx.Ejercicio}";
        if (Fichero200.Diseno(ctx.Ejercicio) is null)
            return Resultado<(Declaracion200, DeclaracionAeat)>.Fallo(new Error("Modelo200.SinDiseno", $"El programa no tiene el diseño del modelo 200 de {ctx.Ejercicio}.", registro,
                $"La AEAT publica el diseño de cada ejercicio en primavera; el programa tiene el de {string.Join(", ", Fichero200.Ejercicios)}.",
                "Cuando esté publicado, hay que añadir su tabla al programa."));
        var propuesta = await impuesto.ProponerAsync(ctx, entrada, c);
        if (!propuesta.EsCorrecto) return Resultado<(Declaracion200, DeclaracionAeat)>.Fallo(propuesta.Errores);
        var p = propuesta.Valor;
        var declarante = await new Servicio303(bd, presentacion).DeclaranteAsync(ctx);
        var (saldos, sinClasificar) = await SaldosAsync(ctx, declarante.RazonSocial, c);
        var ajustes = p.Entrada.Ajustes.Select(a => new AjusteDeclaracion200(Linea(a), a.Temporal, a.Aumento, a.Importe, a.Descripcion)).ToList();
        var (capital, arrendamientos, otras, pagos, avisosRetenciones) = await RetencionesAsync(ctx, p);
        var liq = new Liquidacion200(p.ResultadoContable, p.BasesNegativas, p.BaseImponible, p.TipoGeneral, p.LimiteTramo is > 0m ? p.TipoTramo : null, p.CuotaIntegra,
            p.Bonificaciones, p.RentaViviendas > 0m, p.Deducciones, p.CuotaLiquida, capital, arrendamientos, otras, pagos);
        datos = datos with { Telefono = string.IsNullOrWhiteSpace(datos.Telefono) ? await TelefonoAsync(ctx) : datos.Telefono };
        var menor1M = await CifraNegociosAsync(ctx.IdSociedad, ctx.Ejercicio - 1) is decimal cn && cn < 1_000_000m;
        var escriturado = saldos.Where(x => x.Lado == Lado200.PatrimonioNetoPasivo && Modelo200.CadenaDe(x.Lado, x.Cuenta) is [188, ..]).Sum(x => x.Importe);
        var d = Modelo200.Calcular(ctx.Ejercicio, declarante, datos, saldos, ajustes, liq, menor1M, null, await SociosAsync(ctx, escriturado));
        var errores = d.Errores.Concat(sinClasificar.Select(x => $"La subcuenta {x} tiene apuntes y el balance no la clasifica: no entra en el 200.")).ToList();
        var avisos = d.Avisos.Concat(avisosRetenciones).Concat(p.Avisos.Where(a => !a.Contains("provisional", StringComparison.OrdinalIgnoreCase))).ToList();
        if (d.Casilla("08000", 326) == 0m && p.CuotaLiquida != 0m)
            avisos.Add("El gasto por el impuesto no está contabilizado: confirma el Impuesto sobre Sociedades antes de generar el 200 para que el balance y la PyG lo recojan.");
        d = d with { Errores = errores, Avisos = avisos };
        string fichero;
        try { fichero = Fichero200.Generar(d, ProgramaAeat.Actual); }
        catch (ArgumentOutOfRangeException ex)
        {
            return Resultado<(Declaracion200, DeclaracionAeat)>.Fallo(new Error("Modelo200.Fichero", "El fichero del 200 no se puede generar.", registro, ex.Message,
                "Revisa la contabilidad del ejercicio y el cálculo del Impuesto sobre Sociedades."));
        }
        var resumen = new List<(string, string)>
        {
            ("Resultado contable antes del impuesto (00501)", Modelo200.Euros(d.Casilla("12000", 501))),
            ("Base imponible (00552)", Modelo200.Euros(d.Casilla("14000", 552))),
            ("Cuota íntegra (00562)", Modelo200.Euros(d.Casilla("14000", 562))),
            ("Cuota líquida (00592)", Modelo200.Euros(d.Casilla("14B00", 592))),
            ("Retenciones e ingresos a cuenta (01766)", Modelo200.Euros(d.Casilla("14B00", 1766))),
            ("Pagos fraccionados (00601 + 00603 + 00605)", Modelo200.Euros(d.Casilla("14B00", 601) + d.Casilla("14B00", 603) + d.Casilla("14B00", 605))),
            ("Resultado (01586)", Modelo200.Euros(d.Resultado)),
        };
        var aeat = new DeclaracionAeat(Modelo, ctx.Ejercicio, Periodo, fichero, declarante.Nif, declarante.RazonSocial, d.TipoDeclaracion, d.ImporteAIngresar, d.Resultado,
            declarante.Iban, d.JustificanteAnterior, resumen, Fichero200.LeerNif);
        return Resultado<(Declaracion200, DeclaracionAeat)>.Correcto((d, aeat));
    }

    /// <summary>Línea del detalle de correcciones de un ajuste del IS: la guardada en el ajuste (esquema 72) o, sin ella, la de su descripción.</summary>
    public static Correccion200 Linea(AjusteImpuestoSociedades a) =>
        a.LineaDel200 == AjusteImpuestoSociedades.LineaExencionArt21 ? Correccion200.ExencionTransmisionValoresResidentes : Correccion200.Otras;

    /// <summary>Línea por la descripción (la regla anterior al esquema 72; es la propuesta para los ajustes sin línea).</summary>
    public static Correccion200 Linea(string descripcion) => Linea(new AjusteImpuestoSociedades(false, false, descripcion, 0m));

    /// <summary>
    /// Los datos del 200 que están en la BD (esquema 72): el CNAE y el teléfono de la empresa (Empresas), la plantilla media y la opción del 0,7 %
    /// del ejercicio (Ejercicios), lo que se destina a dividendos en la aplicación del resultado del ejercicio y los cargos de la empresa vigentes
    /// a 31/12 (Cargos de la empresa): administradores, titulares reales, secretario y representantes legales.
    /// </summary>
    public Task<DatosDeclaracion200> DatosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var esquema = s.LeerEsquema();
        var sociedad = await s.ConsultarPrimeroAsync(
            $"SELECT {(esquema.ExisteColumna("Sociedades", "Cnae") ? "Cnae" : "Null AS Cnae")}, {(esquema.ExisteColumna("Sociedades", "Telefono") ? "Telefono" : "Null AS Telefono")} FROM Sociedades WHERE IdSociedad = ?",
            f => (Cnae: f.Texto("Cnae")?.Trim() ?? "", Telefono: f.Texto("Telefono")?.Trim()), ctx.IdSociedad);
        var conPlantilla = esquema.ExisteColumna("SociedadesEjercicios", "PersonalFijo");
        var ejercicio = conPlantilla
            ? await s.ConsultarPrimeroAsync("SELECT PersonalFijo, PersonalNoFijo, OpcionFinesSociales FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
                f => (Fijo: Convert.ToDecimal(f.Valor("PersonalFijo") is null or DBNull ? 0 : f.Valor("PersonalFijo"), CultureInfo.InvariantCulture),
                    NoFijo: Convert.ToDecimal(f.Valor("PersonalNoFijo") is null or DBNull ? 0 : f.Valor("PersonalNoFijo"), CultureInfo.InvariantCulture), Fines: f.Logico("OpcionFinesSociales")),
                ctx.IdSociedadEjercicio)
            : default;
        var dividendos = esquema.ExisteTabla("AplicacionesResultado")
            ? await s.EscalarAsync("SELECT SUM(a.Dividendos) FROM AplicacionesResultado AS a INNER JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                "WHERE se.IdSociedad = ? AND a.EjercicioResultado = ?", ctx.IdSociedad, (short)ctx.Ejercicio) is { } v and not DBNull ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m
            : 0m;
        var cargos = new List<(string Cargo, Persona200 Persona, DateTime? Nacimiento, string? Nacionalidad, string? Residencia, DateTime? Poder, string? Notaria)>();
        if (esquema.ExisteTabla("CargosSociedad"))
        {
            var fin = new DateTime(ctx.Ejercicio, 12, 31);
            cargos.AddRange(await s.ConsultarAsync(
                "SELECT c.Cargo, c.FechaNacimiento, c.Nacionalidad, c.PaisResidencia, c.FechaPoder, c.Notaria, t.NIF, t.TipoPersona, t.RazonSocial, t.Nombre, t.PrimerApellido, " +
                "t.SegundoApellido, t.NombrePersona, t.CodigoPostal FROM CargosSociedad AS c INNER JOIN Terceros AS t ON c.IdTercero = t.IdTercero " +
                "WHERE c.IdSociedad = ? AND c.Activo = True AND (c.FechaDesde IS NULL OR c.FechaDesde <= ?) AND (c.FechaHasta IS NULL OR c.FechaHasta >= ?) ORDER BY c.IdCargoSociedad",
                f => (Cargo: (f.Texto("Cargo") ?? "").Trim(), Persona: Persona(f), Nacimiento: f.FechaNula("FechaNacimiento"), Nacionalidad: f.Texto("Nacionalidad")?.Trim(),
                    Residencia: f.Texto("PaisResidencia")?.Trim(), Poder: f.FechaNula("FechaPoder"), Notaria: f.Texto("Notaria")?.Trim()),
                ctx.IdSociedad, fin, fin));
        }
        IEnumerable<T> De<T>(string cargo, Func<(string Cargo, Persona200 Persona, DateTime? Nacimiento, string? Nacionalidad, string? Residencia, DateTime? Poder, string? Notaria), T> m) =>
            cargos.Where(x => x.Cargo == cargo).Select(m);
        return new DatosDeclaracion200(sociedad.Cnae, ejercicio.Fijo, ejercicio.NoFijo, ejercicio.Fines, sociedad.Telefono, dividendos, false,
            [.. De("ADMINISTRADOR", x => x.Persona)],
            [.. De("TITULAR_REAL", x => new TitularReal200(x.Persona.Nif, x.Persona.Nombre, x.Nacimiento, 1, "ES", Pais(x.Residencia), Pais(x.Nacionalidad)))],
            De("SECRETARIO", x => x.Persona).FirstOrDefault(),
            [.. De("REPRESENTANTE", x => new Representante200(x.Persona.Nif, x.Persona.Nombre, x.Poder, x.Notaria ?? ""))]);
    });

    private static string Pais(string? p) => string.IsNullOrWhiteSpace(p) ? "ES" : p.Trim().ToUpperInvariant();

    /// <summary>Persona del 200 desde su ficha de tercero: NIF con letra, «apellidos, nombre» o razón social y provincia del código postal.</summary>
    internal static Persona200 Persona(IFilaDatos f)
    {
        var juridica = string.Equals(f.Texto("TipoPersona")?.Trim(), "JURIDICA", StringComparison.OrdinalIgnoreCase);
        var apellidos = string.Join(' ', new[] { f.Texto("PrimerApellido"), f.Texto("SegundoApellido") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        var nombre = juridica ? f.Texto("RazonSocial") ?? f.Texto("Nombre") ?? ""
            : apellidos.Length > 0 ? apellidos + (string.IsNullOrWhiteSpace(f.Texto("NombrePersona")) ? "" : ", " + f.Texto("NombrePersona")!.Trim()) : f.Texto("Nombre") ?? "";
        var cp = f.Texto("CodigoPostal")?.Trim();
        return new Persona200(Modelo200.Nif(f.Texto("NIF")), nombre.Trim(), juridica, cp is { Length: >= 2 } && cp[..2].All(char.IsAsciiDigit) ? cp[..2] : null);
    }

    /// <summary>Saldos por subcuenta del balance de situación y de la PyG del ejercicio, y las subcuentas que el balance no clasifica.</summary>
    private async Task<(IReadOnlyList<SaldoCuenta200> Saldos, IReadOnlyList<string> SinClasificar)> SaldosAsync(ContextoSociedadEjercicio ctx, string razon, CancellationToken c)
    {
        var gen = new GeneradorBalances(bd);
        var r = new List<SaldoCuenta200>();
        var sin = new List<string>();
        foreach (var tipo in new[] { TipoBalance.Situacion, TipoBalance.PerdidasYGanancias })
        {
            var b = await gen.GenerarAsync(tipo, ctx, razon, c);
            foreach (var g in b.Filas.GroupBy(f => (Lado: Lado(tipo, f.Bloque), Cuenta: f.CuentaContable.Trim())))
                r.Add(new SaldoCuenta200(g.Key.Lado, g.Key.Cuenta, g.Sum(f => f.Importe)));
            sin.AddRange(b.SinClasificar.Where(x => x.Debe != x.Haber).Select(x => $"{x.Cuenta} {x.Nombre}"));
        }
        return (r, sin.Distinct().ToList());
    }

    private static Lado200 Lado(TipoBalance tipo, string bloque) =>
        tipo == TipoBalance.PerdidasYGanancias ? Lado200.PerdidasGanancias
        : bloque.Contains("PASIVO", StringComparison.OrdinalIgnoreCase) || bloque.Contains("PATRIMONIO", StringComparison.OrdinalIgnoreCase) ? Lado200.PatrimonioNetoPasivo
        : Lado200.Activo;

    private static readonly string[] ModelosCapital = ["193", "196", "187", "188", "194"];

    /// <summary>Lo que el IS descuenta de cada 473, por su naturaleza, y los pagos fraccionados por periodo según el mes del cargo.</summary>
    private Task<(decimal Capital, decimal Arrendamientos, decimal Otras, IReadOnlyList<decimal> Pagos, IReadOnlyList<string> Avisos)> RetencionesAsync(
        ContextoSociedadEjercicio ctx, PropuestaImpuestoSociedades p) => bd.EjecutarAsync<(decimal, decimal, decimal, IReadOnlyList<decimal>, IReadOnlyList<string>)>(async s =>
    {
        var avisos = new List<string>();
        var pf = (await Servicio202.CuentasPagosFraccionadosAsync(s, ctx.IdSociedadEjercicio, ctx.Ejercicio)).ToHashSet(StringComparer.Ordinal);
        var aeat = (p.Entrada.RetencionesAeat ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Cuenta)).GroupBy(x => x.Cuenta!.Trim()).ToDictionary(g => g.Key, g => g.First().Modelo);
        foreach (var (cuenta, modelo) in aeat) if (modelo == "202") pf.Add(cuenta);
        decimal capital = 0m, arrendamientos = 0m, otras = 0m, deducidoPf = 0m;
        foreach (var r in p.Retenciones.Where(r => r.Deducido != 0m))
        {
            var nombre = r.Nombre.ToUpperInvariant();
            if (pf.Contains(r.Cuenta)) deducidoPf += r.Deducido;
            else if (aeat.TryGetValue(r.Cuenta, out var m) && m != "202")
            {
                if (m == "180") arrendamientos += r.Deducido;
                else if (ModelosCapital.Contains(m)) capital += r.Deducido;
                else otras += r.Deducido;
            }
            else if (r.Cuenta.StartsWith("4735", StringComparison.Ordinal) || nombre.Contains("ALQUIL", StringComparison.Ordinal) || nombre.Contains("ARREND", StringComparison.Ordinal))
                arrendamientos += r.Deducido;
            else capital += r.Deducido;
        }
        var pagos = new decimal[3];
        if (pf.Count > 0)
        {
            var cargos = await s.ConsultarAsync(
                "SELECT p.CuentaContable, a.FechaContable, p.ImporteDebe, p.ImporteHaber FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento = 0 AND p.CuentaContable LIKE '473%' " +
                "AND a.IdAsiento NOT IN (SELECT q.IdAsiento FROM Apuntes AS q WHERE q.CuentaContable LIKE '630%')",
                f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Fecha: f.Fecha("FechaContable"), Neto: (f.DecimalNulo("ImporteDebe") ?? 0m) - (f.DecimalNulo("ImporteHaber") ?? 0m)),
                ctx.IdSociedadEjercicio);
            foreach (var x in cargos.Where(x => pf.Contains(x.Cuenta)))
                pagos[x.Fecha.Month <= 6 ? 0 : x.Fecha.Month <= 10 ? 1 : 2] += x.Neto;
        }
        if (pagos.Sum() != deducidoPf)
            avisos.Add($"Los pagos fraccionados cargados en el ejercicio ({Modelo200.Euros(pagos.Sum())}) no son lo que el IS descuenta de sus cuentas ({Modelo200.Euros(deducidoPf)}): " +
                "revisa los cargos del 202 en la 473 de pagos a cuenta.");
        return (capital, arrendamientos, otras, pagos, avisos);
    });

    /// <summary>Socios de la sociedad (SociosSociedad) con su participación y el nominal que le corresponde del capital escriturado.</summary>
    private Task<IReadOnlyList<Persona200>> SociosAsync(ContextoSociedadEjercicio ctx, decimal capital) => bd.EjecutarAsync<IReadOnlyList<Persona200>>(async s =>
    {
        if (!s.LeerEsquema().ExisteTabla("SociosSociedad")) return [];
        return await s.ConsultarAsync(
            "SELECT ss.Participacion, t.NIF, t.TipoPersona, t.RazonSocial, t.Nombre, t.PrimerApellido, t.SegundoApellido, t.NombrePersona, t.CodigoPostal " +
            "FROM (SociosSociedad AS ss INNER JOIN Socios AS so ON ss.IdSocio = so.IdSocio) LEFT JOIN Terceros AS t ON so.IdTercero = t.IdTercero " +
            "WHERE ss.IdSociedad = ? AND ss.Activo = True ORDER BY ss.Participacion DESC, ss.IdSocio",
            f =>
            {
                var juridica = string.Equals(f.Texto("TipoPersona")?.Trim(), "JURIDICA", StringComparison.OrdinalIgnoreCase);
                var apellidos = string.Join(' ', new[] { f.Texto("PrimerApellido"), f.Texto("SegundoApellido") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
                var nombre = juridica ? f.Texto("RazonSocial") ?? f.Texto("Nombre") ?? ""
                    : apellidos.Length > 0 ? apellidos + (string.IsNullOrWhiteSpace(f.Texto("NombrePersona")) ? "" : ", " + f.Texto("NombrePersona")!.Trim()) : f.Texto("Nombre") ?? "";
                var cp = f.Texto("CodigoPostal")?.Trim();
                var pct = f.DecimalNulo("Participacion") ?? 0m;
                return new Persona200(Modelo200.Nif(f.Texto("NIF")), nombre.Trim(), juridica, cp is { Length: >= 2 } && cp[..2].All(char.IsAsciiDigit) ? cp[..2] : null,
                    decimal.Round(capital * pct / 100m, 2, MidpointRounding.AwayFromZero), pct);
            }, ctx.IdSociedad);
    });

    /// <summary>Teléfono de la sociedad (columna Sociedades.Telefono si existe).</summary>
    private Task<string?> TelefonoAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        s.LeerEsquema().ExisteColumna("Sociedades", "Telefono")
            ? (await s.EscalarAsync("SELECT Telefono FROM Sociedades WHERE IdSociedad = ?", ctx.IdSociedad)) is string t && t.Trim().Length > 0 ? t.Trim() : null
            : null);

    /// <summary>Importe neto de la cifra de negocios de un ejercicio (abonos menos cargos de las 70 y la 752); null si el ejercicio no existe.</summary>
    private Task<decimal?> CifraNegociosAsync(string idSociedad, int ejercicio) => bd.EjecutarAsync(async s =>
    {
        var id = await s.ConsultarPrimeroAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?",
            f => (int?)f.Entero("IdSociedadEjercicio"), idSociedad, (short)ejercicio);
        if (id is null) return (decimal?)null;
        var v = await s.EscalarAsync(
            "SELECT SUM(p.ImporteHaber) - SUM(p.ImporteDebe) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 " +
            "AND (a.TipoAsiento = 0 OR a.TipoAsiento = 4) AND (p.CuentaContable LIKE '70%' OR p.CuentaContable LIKE '752%')", id.Value);
        return v is null or DBNull ? 0m : Convert.ToDecimal(v, CultureInfo.InvariantCulture);
    });
}
