using System.Globalization;
using ProConta.Contabilidad.Presentacion;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Cómo se presenta un modelo: presentación directa (ServaliDos y PresBasicaDos) o TGVI Online (informativas).</summary>
public enum CanalModelo { Directa, Tgvi }

/// <summary>Proceso del programa relacionado con un modelo, que la pantalla abre desde el detalle o el menú del periodo.</summary>
public enum ProcesoModelo { Ninguno, LiquidacionIva, Nominas, ImpuestoSociedades, Prorrata, AplicacionResultado, FacturasCompra }

/// <summary>
/// Lo que la pantalla Contabilidad › Modelos tributarios cuenta de un modelo (contabilidad/modelos-tributarios): número, nombre,
/// periodicidad, descripción breve y larga, plazo, canal, pago, de dónde salen los datos y el proceso relacionado.
/// </summary>
public sealed record DefinicionModelo(string Id, string Nombre, string Periodicidad, string Corto, string Descripcion, string Plazo, CanalModelo Canal, string Pago,
    string Origen, ProcesoModelo Proceso, string? TextoProceso)
{
    public bool Informativa => Canal == CanalModelo.Tgvi;

    /// <summary>
    /// Si un periodo sin nada que declarar no se presenta: las informativas sin registros, las retenciones sin retenciones (111, 115,
    /// 123) y el 202 sin pago. El 303, el 390 y el 200 se presentan siempre.
    /// </summary>
    public bool SinObligacionSiCero => Informativa || Id is "111" or "115" or "123" or "202";
    public bool ConPago => Pago != SinPago;
    public string TextoCanal => Canal == CanalModelo.Tgvi ? "TGVI Online (informativa)" : "Presentación directa";

    public const string SinPago = "Sin pago";
    private const string Domiciliacion = "Domiciliación; fuera de su plazo, NRC";
    private const string Trimestral = "Del 1 al 20 del mes siguiente al trimestre; el 4T, del 1 al 20 de enero.";

    /// <summary>Los modelos que genera el programa, en el orden de la pantalla (el de la maqueta validada, con el 123 y el 193).</summary>
    public static IReadOnlyList<DefinicionModelo> Todos { get; } =
    [
        new("303", "IVA. Autoliquidación", "Trimestral", "El IVA repercutido menos el deducible de cada trimestre.",
            "Autoliquidación trimestral del IVA: lo repercutido en las ventas menos lo deducible de las compras, con la prorrata de las actividades que la aplican. Lo que sale a ingresar se domicilia.",
            "Del 1 al 20 del mes siguiente al trimestre; el 4T, del 1 al 30 de enero.", CanalModelo.Directa, Domiciliacion, "Libros de IVA", ProcesoModelo.LiquidacionIva, "Liquidación de IVA"),
        new("111", "Retenciones del trabajo y de profesionales", "Trimestral", "Lo retenido en nóminas y en facturas de profesionales.",
            "Retenciones e ingresos a cuenta del trimestre: las de las nóminas y las de las facturas recibidas de profesionales.",
            Trimestral, CanalModelo.Directa, Domiciliacion, "Nóminas y facturas de compra con retención", ProcesoModelo.Nominas, "Nóminas"),
        new("115", "Retenciones de alquileres", "Trimestral", "Lo retenido a los arrendadores de locales.",
            "Retenciones de los arrendamientos de inmuebles urbanos: las de las facturas de alquiler recibidas en el trimestre.",
            Trimestral, CanalModelo.Directa, Domiciliacion, "Facturas de compra de alquiler con retención", ProcesoModelo.FacturasCompra, "Facturas recibidas"),
        new("123", "Retenciones del capital mobiliario", "Trimestral", "Lo retenido a los socios en los dividendos.",
            "Retenciones de los rendimientos del capital mobiliario del trimestre: las de los dividendos pagados a los socios.",
            Trimestral, CanalModelo.Directa, Domiciliacion, "Repartos de dividendos", ProcesoModelo.AplicacionResultado, "Reparto de dividendos"),
        new("349", "Operaciones intracomunitarias", "Trimestral", "Compras y servicios con operadores de la UE (Airbnb, Booking).",
            "Declaración recapitulativa de las operaciones con operadores de otros países de la Unión Europea, por operador y con su NIF-IVA. Cuadra con las casillas intracomunitarias del 303.",
            "Del 1 al 20 del mes siguiente al trimestre; el 4T, del 1 al 30 de enero.", CanalModelo.Tgvi, SinPago, "Libros de IVA", ProcesoModelo.Ninguno, null),
        new("202", "Pago fraccionado del Impuesto sobre Sociedades", "Abril, octubre y diciembre", "Los tres pagos a cuenta del impuesto del año.",
            "Pagos fraccionados del Impuesto sobre Sociedades. Los presentados son los pagos a cuenta que después descuenta la liquidación del impuesto.",
            "Del 1 al 20 de abril, de octubre y de diciembre.", CanalModelo.Directa, Domiciliacion, "Última declaración del impuesto", ProcesoModelo.ImpuestoSociedades, "Impuesto sobre Sociedades"),
        new("390", "IVA. Resumen anual", "Anual", "El resumen de los cuatro 303 del año.",
            "Declaración resumen anual del IVA: las casillas del año a partir de los cuatro 303 y de la regularización de la prorrata.",
            "Del 1 al 30 de enero del año siguiente.", CanalModelo.Directa, SinPago, "Los cuatro 303 del año", ProcesoModelo.Prorrata, "Prorrata de IVA"),
        new("190", "Resumen anual de retenciones del trabajo", "Anual", "Un registro por empleado y profesional; cuadra con los 111.",
            "Resumen anual de las retenciones del trabajo y de profesionales: un registro por perceptor con lo percibido y lo retenido en el año. Su total coincide con la suma de los cuatro 111.",
            "Del 1 al 31 de enero del año siguiente.", CanalModelo.Tgvi, SinPago, "Nóminas y facturas de compra con retención", ProcesoModelo.Nominas, "Nóminas"),
        new("180", "Resumen anual de retenciones de alquileres", "Anual", "Un registro por arrendador; cuadra con los 115.",
            "Resumen anual de las retenciones de alquileres: un registro por arrendador con su inmueble. Su total coincide con la suma de los cuatro 115.",
            "Del 1 al 31 de enero del año siguiente.", CanalModelo.Tgvi, SinPago, "Facturas de compra de alquiler con retención", ProcesoModelo.FacturasCompra, "Facturas recibidas"),
        new("193", "Resumen anual de retenciones del capital mobiliario", "Anual", "Un registro por socio; cuadra con los 123.",
            "Resumen anual de las retenciones de los dividendos: un registro por socio con lo percibido y lo retenido en el año. Su total coincide con la suma de los cuatro 123.",
            "Del 1 al 31 de enero del año siguiente.", CanalModelo.Tgvi, SinPago, "Repartos de dividendos", ProcesoModelo.AplicacionResultado, "Reparto de dividendos"),
        new("347", "Operaciones con terceros", "Anual", "Clientes y proveedores con más de 3.005,06 € en el año.",
            "Declaración anual de operaciones con terceras personas: clientes y proveedores con los que se superan 3.005,06 € en el año, con el importe por trimestre.",
            "Febrero del año siguiente.", CanalModelo.Tgvi, SinPago, "Libros de IVA", ProcesoModelo.Ninguno, null),
        new("200", "Impuesto sobre Sociedades", "Anual", "La declaración anual del impuesto.",
            "Declaración anual del Impuesto sobre Sociedades, a partir del balance, la cuenta de pérdidas y ganancias y el cálculo del impuesto. El fichero también se puede importar en Sociedades WEB.",
            "Del 1 al 25 de julio del año siguiente.", CanalModelo.Directa, Domiciliacion, "Balance, pérdidas y ganancias y cálculo del impuesto", ProcesoModelo.ImpuestoSociedades, "Impuesto sobre Sociedades"),
    ];

    public static DefinicionModelo De(string id) => Todos.First(m => m.Id == id);
}

/// <summary>
/// Plazos de presentación de los modelos (contabilidad/modelos-tributarios): del primer al último día de cada plazo, con el último
/// pasado al siguiente día hábil si cae en sábado, domingo o festivo nacional (art. 48 de la Ley 39/2015). No conoce los festivos
/// autonómicos ni locales.
/// </summary>
public static class PlazosModelos
{
    /// <summary>Periodos de un modelo en un ejercicio: «1T»…«4T», «1P»…«3P» o «0A».</summary>
    public static IReadOnlyList<string> Periodos(string modelo) => modelo switch
    {
        "303" or "111" or "115" or "123" or "349" => ["1T", "2T", "3T", "4T"],
        "202" => ["1P", "2P", "3P"],
        _ => ["0A"],
    };

    /// <summary>Nombre del periodo en la pantalla: «1T», «2P» o «Año».</summary>
    public static string Nombre(string periodo) => periodo == "0A" ? "Año" : periodo;

    /// <summary>Plazo legal (sin ajustar) de un periodo.</summary>
    public static (DateTime Desde, DateTime Hasta) PlazoLegal(string modelo, int ejercicio, string periodo)
    {
        var n = periodo.Length == 2 && char.IsAsciiDigit(periodo[0]) ? periodo[0] - '0' : 0;
        switch (modelo)
        {
            case "303" or "349":
                return n == 4 ? (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 30)) : (new DateTime(ejercicio, 3 * n + 1, 1), new DateTime(ejercicio, 3 * n + 1, 20));
            case "111" or "115" or "123":
                return n == 4 ? (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 20)) : (new DateTime(ejercicio, 3 * n + 1, 1), new DateTime(ejercicio, 3 * n + 1, 20));
            case "202":
                var mes = n switch { 1 => 4, 2 => 10, _ => 12 };
                return (new DateTime(ejercicio, mes, 1), new DateTime(ejercicio, mes, 20));
            case "390":
                return (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 30));
            case "190" or "180" or "193":
                return (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 31));
            case "347":
                return (new DateTime(ejercicio + 1, 2, 1), new DateTime(ejercicio + 1, 3, 1).AddDays(-1));
            case "200":
                return (new DateTime(ejercicio + 1, 7, 1), new DateTime(ejercicio + 1, 7, 25));
            default:
                throw new ArgumentOutOfRangeException(nameof(modelo), modelo, "Modelo sin plazo conocido.");
        }
    }

    /// <summary>Plazo de un periodo con el último día pasado al siguiente hábil.</summary>
    public static (DateTime Desde, DateTime Hasta) Plazo(string modelo, int ejercicio, string periodo)
    {
        var (desde, hasta) = PlazoLegal(modelo, ejercicio, periodo);
        return (desde, DiaHabil(hasta));
    }

    /// <summary>El mismo día si es hábil; si no, el siguiente hábil (ni sábado, ni domingo, ni festivo nacional).</summary>
    public static DateTime DiaHabil(DateTime d)
    {
        d = d.Date;
        while (!EsHabil(d)) d = d.AddDays(1);
        return d;
    }

    public static bool EsHabil(DateTime d) => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && !FestivosNacionales(d.Year).Contains(d.Date);

    /// <summary>
    /// Festivos nacionales comunes a todo el territorio: 1 y 6 de enero, Viernes Santo, 1 de mayo, 15 de agosto, 12 de octubre, 1 de
    /// noviembre y 6, 8 y 25 de diciembre.
    /// </summary>
    public static IReadOnlySet<DateTime> FestivosNacionales(int anio)
    {
        var pascua = DomingoDePascua(anio);
        return new HashSet<DateTime>
        {
            new(anio, 1, 1), new(anio, 1, 6), pascua.AddDays(-2), new(anio, 5, 1), new(anio, 8, 15), new(anio, 10, 12), new(anio, 11, 1),
            new(anio, 12, 6), new(anio, 12, 8), new(anio, 12, 25),
        };
    }

    /// <summary>Domingo de Pascua (algoritmo anónimo gregoriano).</summary>
    public static DateTime DomingoDePascua(int anio)
    {
        int a = anio % 19, b = anio / 100, c = anio % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3, h = (19 * a + b - d - g + 15) % 30;
        int i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        var mes = (h + l - 7 * m + 114) / 31;
        var dia = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(anio, mes, dia);
    }
}

/// <summary>Estado de un periodo en la pantalla de modelos (el color de su ficha).</summary>
public enum EstadoPeriodoModelo
{
    /// <summary>Su plazo aún no ha empezado (el importe es provisional).</summary>
    AunNoAbre,
    /// <summary>En plazo y sin presentar.</summary>
    EnPlazo,
    /// <summary>El plazo terminó y no consta ninguna presentación.</summary>
    Vencido,
    /// <summary>Presentado (desde el programa o fuera de él).</summary>
    Presentado,
    /// <summary>Presentado desde el programa, pero lo que se calcula ahora es otro importe.</summary>
    Desactualizado,
    /// <summary>Sin nada que declarar (cero) en un modelo que entonces no se presenta: ni pendiente ni vencido.</summary>
    SinObligacion,
}

/// <summary>
/// Un periodo de un modelo en la pantalla: su plazo, el estado que calcula su servicio (<see cref="Estado303"/>), lo calculado, lo
/// presentado, lo que impide presentarlo y los avisos, y por qué es solo de consulta si el programa no lo puede generar.
/// </summary>
/// <param name="Calculado">Importe que calcula el programa (resultado a ingresar, total retenido o importe declarado); null si no se calcula.</param>
/// <param name="TextoCalculado">Lo calculado cuando no es un importe («14 declarados»).</param>
/// <param name="SoloConsulta">Por qué el programa no genera este periodo (diseño del ejercicio no disponible…); null si lo genera.</param>
public sealed record PeriodoModelo(string Modelo, int Ejercicio, string Periodo, DateTime PlazoDesde, DateTime PlazoHasta, Estado303 EstadoServicio, decimal? Calculado,
    string? TextoCalculado, PresentacionGuardada? Ultima, IReadOnlyList<PresentacionGuardada> Presentaciones, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos,
    string? SoloConsulta = null)
{
    public string Clave => $"{Modelo}|{Ejercicio}|{Periodo}";
    public string Nombre => PlazosModelos.Nombre(Periodo);
    public bool Presentado => Ultima is not null;
    public bool Generable => SoloConsulta is null;

    /// <summary>Estado en la pantalla el día <paramref name="hoy"/>.</summary>
    public EstadoPeriodoModelo Estado(DateTime hoy) =>
        Ultima is not null ? (EstadoServicio == Estado303.Desactualizado ? EstadoPeriodoModelo.Desactualizado : EstadoPeriodoModelo.Presentado)
        : hoy.Date < PlazoDesde ? EstadoPeriodoModelo.AunNoAbre
        : Calculado == 0m && SoloConsulta is null && DefinicionModelo.De(Modelo).SinObligacionSiCero ? EstadoPeriodoModelo.SinObligacion
        : hoy.Date <= PlazoHasta ? EstadoPeriodoModelo.EnPlazo
        : EstadoPeriodoModelo.Vencido;

    /// <summary>Días que quedan del plazo (0 el último día).</summary>
    public int DiasRestantes(DateTime hoy) => (PlazoHasta - hoy.Date).Days;

    /// <summary>Texto corto del estado («En plazo · quedan 3 días», «Abre el 01/01»…).</summary>
    public string TextoEstado(DateTime hoy) => Estado(hoy) switch
    {
        EstadoPeriodoModelo.Presentado => "Presentado",
        EstadoPeriodoModelo.Desactualizado => "Desactualizado",
        EstadoPeriodoModelo.SinObligacion => "Sin datos · no se presenta",
        EstadoPeriodoModelo.AunNoAbre => $"Abre el {PlazoDesde:dd/MM}",
        EstadoPeriodoModelo.EnPlazo => DiasRestantes(hoy) switch { 0 => "En plazo · termina hoy", 1 => "En plazo · queda 1 día", var n => $"En plazo · quedan {n} días" },
        _ => "Vencido",
    };

    /// <summary>Explicación del estado (tooltip).</summary>
    public string AyudaEstado(DateTime hoy) => Estado(hoy) switch
    {
        EstadoPeriodoModelo.Presentado => $"Presentado el {Ultima!.FechaHora:dd/MM/yyyy}" + (Ultima.EsExterna ? " fuera del programa (lo devuelve la consulta de la AEAT)" : " desde el programa") +
            $", CSV {Ultima.Csv}.",
        EstadoPeriodoModelo.Desactualizado => $"Lo presentado ({Euros(Ultima!.Resultado)}) ya no coincide con lo que el programa calcula ahora ({Euros(Calculado ?? 0m)}). Si procede, presenta una complementaria.",
        EstadoPeriodoModelo.AunNoAbre => $"El plazo va del {PlazoDesde:dd/MM/yyyy} al {PlazoHasta:dd/MM/yyyy}. Hasta entonces el importe es provisional.",
        EstadoPeriodoModelo.SinObligacion => $"El programa no calcula nada que declarar en este periodo: el {Modelo} no se presenta cuando sale a cero (no hay declaración negativa). " +
            "Si crees que falta algo, revisa los documentos del periodo; si quieres presentarlo igualmente, usa «Presentar…».",
        EstadoPeriodoModelo.EnPlazo => $"El plazo termina el {PlazoHasta:dd/MM/yyyy}.",
        _ => $"El plazo terminó el {PlazoHasta:dd/MM/yyyy} y no consta ninguna presentación. Pulsa «Consultar a la AEAT» por si se presentó fuera del programa.",
    };

    internal static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}

/// <summary>Una acción del menú contextual de los periodos: si se puede y, si no, por qué.</summary>
public sealed record AccionPeriodo(AccionModelo Accion, string Texto, string? NoSePuede, string? Ayuda = null)
{
    public bool Disponible => NoSePuede is null;
}

/// <summary>Acciones del menú contextual de los periodos (maqueta validada, decisión 2 del 2026-11-18 05:34).</summary>
public enum AccionModelo
{
    VerBorrador, GenerarFichero, Presentar, PresentarComplementaria, AbrirJustificante, DatosPresentacion, CopiarCsv, AbrirCasillas, AbrirProceso,
}

/// <summary>Qué se puede hacer con los periodos marcados, y por qué no lo demás (el texto sale en el menú, debajo de la opción).</summary>
public static class AccionesPeriodos
{
    public static IReadOnlyList<AccionPeriodo> De(IReadOnlyList<PeriodoModelo> marcados, DateTime hoy, bool puedePresentar)
    {
        if (marcados.Count == 0) return [];
        if (marcados.Count > 1)
        {
            var gen = marcados.Count(p => p.Generable);
            var pres = marcados.Count(p => p.Presentado);
            var cerrados = marcados.Count(p => p.Generable && hoy.Date >= p.PlazoDesde);
            return
            [
                new(AccionModelo.VerBorrador, $"Ver los borradores ({cerrados})", cerrados > 0 ? null : "Ninguno de los marcados se puede generar todavía"),
                new(AccionModelo.GenerarFichero, $"Generar los ficheros… ({gen})", gen > 0 ? null : "El programa no genera ninguno de los marcados"),
                new(AccionModelo.AbrirJustificante, $"Abrir los justificantes ({pres})", pres > 0 ? null : "Ninguno de los marcados está presentado"),
                new(AccionModelo.Presentar, "Presentar…", "Se presenta de uno en uno, para revisar cada PDF antes de dar la conformidad"),
            ];
        }
        var p = marcados[0];
        var def = DefinicionModelo.De(p.Modelo);
        var noGen = p.SoloConsulta;
        var abierto = hoy.Date >= p.PlazoDesde;
        var noAbre = abierto ? null : $"El plazo empieza el {p.PlazoDesde:dd/MM/yyyy}";
        var sinPermiso = puedePresentar ? null : "No tienes permiso para presentar modelos (Configuración › Seguridad)";
        var noPresentar = noGen ?? (p.Presentado ? "Ya está presentado; solo admite una complementaria" : null) ?? noAbre ?? sinPermiso;
        var noCompl = noGen ?? (!p.Presentado ? "Todavía no hay nada presentado de este periodo"
            : p.Ultima!.EsExterna ? "Lo presentó la gestoría y la consulta de la AEAT no da su importe: haz la complementaria en la Sede o con la gestoría"
            : p.Modelo == "200" ? "La complementaria del 200 se hace en Sociedades WEB, importando el fichero del programa"
            : null) ?? sinPermiso;
        var sinPres = p.Presentado ? null : "No consta ninguna presentación";
        var lista = new List<AccionPeriodo>
        {
            new(AccionModelo.VerBorrador, def.Informativa ? "Ver el borrador (vista previa de la AEAT)" : "Ver el borrador (PDF de la AEAT)", noGen ?? noAbre,
                "Valida el fichero con la AEAT y enseña su PDF; no presenta nada"),
            new(AccionModelo.GenerarFichero, "Generar el fichero…", noGen, "Guarda el fichero con el diseño de registro, para la gestoría o para importarlo en la Sede"),
            new(AccionModelo.Presentar, "Presentar…", noPresentar),
            new(AccionModelo.PresentarComplementaria, def.Informativa ? "Presentar complementaria o sustitutiva…" : "Presentar complementaria…", noCompl),
            new(AccionModelo.AbrirJustificante, "Abrir el justificante (PDF)", sinPres ?? (p.Ultima!.EsExterna && string.IsNullOrWhiteSpace(p.Ultima.RutaPdf) && string.IsNullOrWhiteSpace(p.Ultima.UrlPdf)
                ? "Presentado fuera del programa: descárgalo en la Sede con su CSV" : null)),
            new(AccionModelo.DatosPresentacion, "Datos de la presentación…", sinPres, p.Presentado ? "CSV, justificante, expediente y quién lo firmó" : null),
            new(AccionModelo.CopiarCsv, "Copiar el CSV", sinPres),
            new(AccionModelo.AbrirCasillas, def.Informativa ? $"Abrir los registros del {p.Modelo} de {p.Nombre}" : $"Abrir las casillas del {p.Modelo} de {p.Nombre}", noGen),
        };
        if (def.Proceso != ProcesoModelo.Ninguno) lista.Add(new(AccionModelo.AbrirProceso, $"Abrir «{def.TextoProceso}»", null));
        return lista;
    }
}
