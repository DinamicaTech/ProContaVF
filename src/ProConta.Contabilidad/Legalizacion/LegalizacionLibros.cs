using System.Globalization;
using System.Security.Cryptography;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.CuentasAnuales;
using ProConta.Contabilidad.Diario;

namespace ProConta.Contabilidad.Legalizacion;

/// <summary>Un apunte del libro Diario oficial: cuenta, nombre, concepto, documento e importes.</summary>
public sealed record ApunteLibro(string Cuenta, string Nombre, string Concepto, string? Documento, decimal Debe, decimal Haber);

/// <summary>
/// Un asiento del libro Diario oficial: el número de orden correlativo del libro (1, 2, 3… por fecha) y, al lado, el número de asiento del
/// programa (Asientos.IdAsiento, que no se renumera y tiene huecos), con su fecha, su descripción y sus apuntes.
/// </summary>
public sealed record AsientoLibro(int Numero, int IdAsiento, DateTime Fecha, string Descripcion, byte TipoAsiento, IReadOnlyList<ApunteLibro> Apuntes)
{
    public decimal Debe => Apuntes.Sum(a => a.Debe);
    public decimal Haber => Apuntes.Sum(a => a.Haber);
}

/// <summary>El libro Diario oficial de un ejercicio: todos los asientos contabilizados en el orden del libro y sus totales.</summary>
public sealed record LibroDiario(string RazonSocial, string Nif, int Ejercicio, IReadOnlyList<AsientoLibro> Asientos)
{
    public decimal TotalDebe => Asientos.Sum(a => a.Debe);
    public decimal TotalHaber => Asientos.Sum(a => a.Haber);
    public int NumeroApuntes => Asientos.Sum(a => a.Apuntes.Count);
    public bool Cuadra => TotalDebe == TotalHaber && Asientos.All(a => a.Debe == a.Haber);
}

/// <summary>Una línea de una página del Diario: el apunte, y en la primera de cada asiento su número, su fecha y su descripción.</summary>
public sealed record LineaDiario(AsientoLibro Asiento, ApunteLibro Apunte, bool PrimeraDelAsiento, bool UltimaDelAsiento, bool SigueDeLaAnterior);

/// <summary>
/// Una página del Diario impreso: sus líneas, la suma que viene de la página anterior («Suma anterior») y la que pasa a la siguiente
/// («Suma y sigue»; en la última, «Total del ejercicio»).
/// </summary>
public sealed record PaginaDiario(int Numero, IReadOnlyList<LineaDiario> Lineas, decimal AnteriorDebe, decimal AnteriorHaber, decimal SigueDebe, decimal SigueHaber, bool EsUltima);

/// <summary>Una línea del balance inicial (la apertura) o del inventario de cierre: la subcuenta con su saldo deudor o acreedor.</summary>
public sealed record SaldoInventario(string Cuenta, string Nombre, decimal Deudor, decimal Acreedor);

/// <summary>Un elemento del inmovilizado en el inventario de cierre: lo que costó, lo amortizado hasta el cierre y su valor neto.</summary>
public sealed record ActivoInventario(string Codigo, string Nombre, string Tipo, DateTime? FechaCompra, decimal Coste, decimal Amortizado)
{
    public decimal ValorNeto => Coste - Amortizado;
}

/// <summary>Un fichero generado para la legalización, con su nombre oficial, su contenido, sus páginas y su huella SHA-256.</summary>
public sealed record FicheroLegalizacion(string Nombre, string Libro, byte[] Contenido, int Paginas)
{
    /// <summary>Huella SHA-256 en base64 (RFC 4648), como la pide el Anexo I de la Instrucción de la DGRN de 12/02/2015.</summary>
    public string HuellaBase64 => LegalizacionLibros.HuellaBase64(Contenido);

    /// <summary>La misma huella en hexadecimal (64 caracteres), por si el programa de presentación la pide así.</summary>
    public string HuellaHex => LegalizacionLibros.HuellaHex(Contenido);
}

/// <summary>Gravedad de una comprobación antes de generar los libros.</summary>
public enum GravedadLegalizacion : byte { Correcta, Aviso, Error }

/// <summary>Una comprobación antes de generar los libros, con lo que hay que hacer si no se cumple.</summary>
public sealed record ComprobacionLegalizacion(GravedadLegalizacion Gravedad, string Titulo, string Detalle, string? QueHacer = null);

/// <summary>
/// Todo lo que va en los dos libros de un ejercicio: el Diario, el balance inicial (la apertura), los balances de sumas y saldos al final de
/// cada trimestre, el inventario de cierre (saldos de los grupos 1 a 5 a 31/12 y los activos) y las cuentas anuales con su memoria.
/// </summary>
public sealed record DatosLegalizacion(
    int IdSociedadEjercicio, bool Cerrado, LibroDiario Diario, IReadOnlyList<SaldoInventario> BalanceInicial, IReadOnlyList<ResultadoSumasYSaldos> Trimestres,
    IReadOnlyList<SaldoInventario> InventarioCierre, IReadOnlyList<ActivoInventario> Activos, CuentasAnualesPymes CuentasAnuales,
    IReadOnlyList<ApartadoMemoria> Memoria, DateTime Generado)
{
    public int Ejercicio => Diario.Ejercicio;
    public string RazonSocial => Diario.RazonSocial;
    public string Nif => Diario.Nif;
    public DateTime Plazo => LegalizacionLibros.Plazo(Ejercicio);
    public IReadOnlyList<ComprobacionLegalizacion> Comprobaciones => LegalizacionLibros.Comprobar(this, DateTime.Today);
    public bool TieneErrores => Comprobaciones.Any(c => c.Gravedad == GravedadLegalizacion.Error);
}

/// <summary>
/// Reglas de la legalización telemática de los libros en el Registro Mercantil (contabilidad/procesos/legalizacion-libros), sin BD: el orden
/// y la numeración del Diario, su reparto en páginas con «suma y sigue», el balance inicial y el inventario de cierre, el plazo, los nombres
/// oficiales de los ficheros (Anexo I de la Instrucción de la DGRN de 12/02/2015) y sus huellas.
/// </summary>
public static class LegalizacionLibros
{
    /// <summary>Nombre oficial del fichero del libro Diario (primer libro del tipo «DIARIO» del ejercicio).</summary>
    public const string FicheroDiario = "DIARIO_001.pdf";

    /// <summary>Nombre oficial del fichero del libro de Inventarios y Cuentas Anuales (tipo «INV_CUEN»).</summary>
    public const string FicheroInventarios = "INV_CUEN_001.pdf";

    /// <summary>Dirección del Colegio de Registradores donde se presentan los libros (sede electrónica y programa Legalia 2).</summary>
    public const string UrlPresentacion = "https://www.registradores.org/";

    /// <summary>Tipos de asiento (Asientos.TipoAsiento): 1 apertura, 2 regularización, 3 cierre; el resto, ordinarios.</summary>
    public const byte TipoApertura = 1, TipoRegularizacion = 2, TipoCierre = 3;

    /// <summary>
    /// Último día para presentar los libros: cuatro meses después del cierre del ejercicio social (art. 18 de la Ley 14/2013). Con el
    /// ejercicio natural, el 30 de abril del año siguiente.
    /// </summary>
    public static DateTime Plazo(int ejercicio) => new DateTime(ejercicio, 12, 31).AddMonths(4);

    /// <summary>
    /// Orden del libro: por fecha; el mismo día, la apertura primero, luego los ordinarios, la regularización y el cierre al final; a igualdad,
    /// por número de asiento del programa (orden de alta).
    /// </summary>
    private static int Rango(byte tipo) => tipo switch { TipoApertura => 0, TipoRegularizacion => 2, TipoCierre => 3, _ => 1 };

    /// <summary>El Diario oficial de los asientos contabilizados de un ejercicio, numerados del 1 en adelante en el orden del libro.</summary>
    public static LibroDiario Diario(string razonSocial, string nif, int ejercicio, IEnumerable<AsientoDiario> asientos)
    {
        var numero = 0;
        var libro = asientos
            .OrderBy(a => a.Fecha.Date).ThenBy(a => Rango(a.TipoAsiento)).ThenBy(a => a.IdAsiento)
            .Select(a => new AsientoLibro(++numero, a.IdAsiento, a.Fecha.Date, a.Descripcion, a.TipoAsiento,
                a.Apuntes.OrderBy(p => p.Orden).ThenBy(p => p.IdApunte)
                    .Select(p => new ApunteLibro(p.Cuenta, p.NombreCuenta, p.Concepto, p.Documento, p.Debe, p.Haber)).ToList()))
            .ToList();
        return new LibroDiario(razonSocial, nif, ejercicio, libro);
    }

    /// <summary>
    /// Reparte el Diario en páginas de <paramref name="lineasPorPagina"/> apuntes como máximo. Un asiento puede seguir en la página siguiente
    /// (su primera línea allí dice que sigue). Cada página lleva la suma acumulada que trae y la que pasa a la siguiente.
    /// </summary>
    public static IReadOnlyList<PaginaDiario> Paginar(LibroDiario libro, int lineasPorPagina)
    {
        if (lineasPorPagina < 1) throw new ArgumentOutOfRangeException(nameof(lineasPorPagina));
        var lineas = new List<LineaDiario>();
        foreach (var a in libro.Asientos)
            for (var i = 0; i < a.Apuntes.Count; i++)
                lineas.Add(new LineaDiario(a, a.Apuntes[i], i == 0, i == a.Apuntes.Count - 1, false));
        var paginas = new List<PaginaDiario>();
        decimal debe = 0m, haber = 0m;
        for (var inicio = 0; inicio < lineas.Count || paginas.Count == 0; inicio += lineasPorPagina)
        {
            var trozo = lineas.Skip(inicio).Take(lineasPorPagina).ToList();
            if (trozo.Count > 0 && !trozo[0].PrimeraDelAsiento) trozo[0] = trozo[0] with { SigueDeLaAnterior = true };
            var d = debe + trozo.Sum(l => l.Apunte.Debe);
            var h = haber + trozo.Sum(l => l.Apunte.Haber);
            paginas.Add(new PaginaDiario(paginas.Count + 1, trozo, debe, haber, d, h, inicio + lineasPorPagina >= lineas.Count));
            (debe, haber) = (d, h);
        }
        return paginas;
    }

    /// <summary>
    /// Balance inicial detallado: el saldo de cada subcuenta en los asientos de apertura del ejercicio. Vacío si el ejercicio no tiene apertura
    /// (por ejemplo, el primero de la sociedad).
    /// </summary>
    public static IReadOnlyList<SaldoInventario> BalanceInicial(LibroDiario libro) =>
        Saldos(libro.Asientos.Where(a => a.TipoAsiento == TipoApertura).SelectMany(a => a.Apuntes).Select(p => (p.Cuenta, p.Nombre, p.Debe, p.Haber)));

    /// <summary>
    /// Inventario de cierre: el saldo a 31/12, antes del asiento de cierre, de cada subcuenta de los grupos 1 a 5 (las del balance), sacado del
    /// balance de sumas y saldos a nivel de subcuenta. Las que quedan a cero no salen.
    /// </summary>
    public static IReadOnlyList<SaldoInventario> InventarioCierre(ResultadoSumasYSaldos alCierre) =>
        Saldos(alCierre.Filas.Where(f => f.Nivel == NivelSumasYSaldos.Subcuenta && f.Cuenta.Length > 0 && f.Cuenta[0] is >= '1' and <= '5')
            .Select(f => (f.Cuenta, f.Nombre, f.Debe, f.Haber)));

    private static List<SaldoInventario> Saldos(IEnumerable<(string Cuenta, string Nombre, decimal Debe, decimal Haber)> movimientos) =>
        movimientos.GroupBy(m => m.Cuenta, StringComparer.Ordinal)
            .Select(g => (Cuenta: g.Key, Nombre: g.Select(x => x.Nombre).FirstOrDefault(n => n.Length > 0) ?? "", Saldo: g.Sum(x => x.Debe) - g.Sum(x => x.Haber)))
            .Where(s => s.Saldo != 0m)
            .OrderBy(s => s.Cuenta, StringComparer.Ordinal)
            .Select(s => new SaldoInventario(s.Cuenta, s.Nombre, s.Saldo > 0m ? s.Saldo : 0m, s.Saldo < 0m ? -s.Saldo : 0m))
            .ToList();

    /// <summary>Fechas de cierre de los cuatro trimestres del ejercicio (31/03, 30/06, 30/09 y 31/12).</summary>
    public static IReadOnlyList<DateTime> FinesDeTrimestre(int ejercicio) =>
        [new DateTime(ejercicio, 3, 31), new DateTime(ejercicio, 6, 30), new DateTime(ejercicio, 9, 30), new DateTime(ejercicio, 12, 31)];

    /// <summary>Parámetros del balance de sumas y saldos acumulado hasta el final de un trimestre: cuentas y subcuentas, sin el cierre.</summary>
    public static ParametrosSumasYSaldos ParametrosTrimestre(int ejercicio, DateTime fin) =>
        new(new DateTime(ejercicio, 1, 1), fin, [NivelSumasYSaldos.Cuenta, NivelSumasYSaldos.Subcuenta]);

    /// <summary>Huella SHA-256 del fichero en base64.</summary>
    public static string HuellaBase64(byte[] contenido) => Convert.ToBase64String(SHA256.HashData(contenido));

    /// <summary>Huella SHA-256 del fichero en hexadecimal (mayúsculas).</summary>
    public static string HuellaHex(byte[] contenido) => Convert.ToHexString(SHA256.HashData(contenido));

    /// <summary>
    /// Lo que se comprueba antes de generar: que el ejercicio esté cerrado (los libros se legalizan cerrados), que el Diario cuadre, que el
    /// balance de sumas y saldos a 31/12 cuadre, que las cuentas anuales no tengan errores del Registro y el plazo de presentación.
    /// </summary>
    public static IReadOnlyList<ComprobacionLegalizacion> Comprobar(DatosLegalizacion d, DateTime hoy)
    {
        var es = CultureInfo.GetCultureInfo("es-ES");
        var r = new List<ComprobacionLegalizacion>();
        r.Add(d.Cerrado
            ? new(GravedadLegalizacion.Correcta, $"El ejercicio {d.Ejercicio} está cerrado", "Los libros recogen la regularización, el cierre y la apertura del ejercicio siguiente.")
            : new(GravedadLegalizacion.Error, $"El ejercicio {d.Ejercicio} no está cerrado",
                "Los libros que se legalizan son los del ejercicio cerrado: sin la regularización y el cierre, el Diario y el inventario no son los definitivos.",
                "Ciérralo en Contabilidad › Fin de ejercicio › Cierre del ejercicio (botón «Cierre del ejercicio» de arriba). Puedes generar los libros igualmente como borrador para revisarlos."));
        if (d.Diario.Asientos.Count == 0)
            r.Add(new(GravedadLegalizacion.Error, "El Diario no tiene asientos", $"No hay asientos contabilizados en {d.Ejercicio}.",
                "Elige otro ejercicio o revisa en Contabilidad › Diario que los asientos estén contabilizados."));
        else if (!d.Diario.Cuadra)
        {
            var descuadrados = d.Diario.Asientos.Where(a => a.Debe != a.Haber).Select(a => $"{a.Numero} (asiento {a.IdAsiento})").Take(5).ToList();
            r.Add(new(GravedadLegalizacion.Error, "El Diario no cuadra",
                $"Debe {d.Diario.TotalDebe.ToString("N2", es)} € y Haber {d.Diario.TotalHaber.ToString("N2", es)} €" +
                (descuadrados.Count > 0 ? $"; asientos descuadrados: {string.Join(", ", descuadrados)}." : "."),
                "Corrige esos asientos en Contabilidad › Diario y vuelve a pulsar Actualizar."));
        }
        else
            r.Add(new(GravedadLegalizacion.Correcta, "El Diario cuadra",
                $"{d.Diario.Asientos.Count} asientos y {d.Diario.NumeroApuntes} apuntes; Debe y Haber {d.Diario.TotalDebe.ToString("N2", es)} €."));
        if (d.Trimestres.Count > 0 && d.Trimestres[^1] is { } fin && fin.TotalDebe != fin.TotalHaber)
            r.Add(new(GravedadLegalizacion.Error, "El balance de sumas y saldos a 31/12 no cuadra",
                $"Sumas: {fin.TotalDebe.ToString("N2", es)} € al Debe y {fin.TotalHaber.ToString("N2", es)} € al Haber.", "Revisa los asientos en Contabilidad › Diario."));
        var errores = d.CuentasAnuales.Comprobaciones.Where(c => c.Gravedad == GravedadComprobacion.Error).ToList();
        r.Add(errores.Count == 0
            ? new(GravedadLegalizacion.Correcta, "Las cuentas anuales pasan las comprobaciones del Registro",
                $"Activo {d.CuentasAnuales.Actual.TotalActivo.ToString("N2", es)} € · resultado {d.CuentasAnuales.Actual.Resultado.ToString("N2", es)} €.")
            : new(GravedadLegalizacion.Error, "Las cuentas anuales tienen errores",
                string.Join(" ", errores.Select(e => e.Titulo + ".")), "Ábrelas en Fin de ejercicio › Cuentas anuales, donde se explica cada error."));
        if (d.Memoria.Count == 0)
            r.Add(new(GravedadLegalizacion.Aviso, "Las cuentas anuales van sin memoria", "El libro llevará el balance y la cuenta de pérdidas y ganancias.",
                "Revisa la memoria en Fin de ejercicio › Cuentas anuales."));
        else if (d.Memoria.Any(m => !m.Guardado))
            r.Add(new(GravedadLegalizacion.Aviso, "La memoria tiene apartados con el texto propuesto",
                $"{d.Memoria.Count(m => !m.Guardado)} de {d.Memoria.Count} apartados no se han revisado: el libro los lleva con el texto que propone el programa.",
                "Revísalos y guárdalos en Fin de ejercicio › Cuentas anuales, pestaña Memoria."));
        if (d.BalanceInicial.Count == 0)
            r.Add(new(GravedadLegalizacion.Aviso, "El ejercicio no tiene asiento de apertura",
                "El libro de Inventarios dirá que no hay balance inicial (es lo normal en el primer ejercicio de la sociedad)."));
        var plazo = Plazo(d.Ejercicio);
        r.Add(hoy.Date > plazo
            ? new(GravedadLegalizacion.Aviso, $"Fuera de plazo desde el {plazo:dd/MM/yyyy}",
                "Se pueden presentar igualmente; el Registro hará constar que se presentan fuera de plazo.")
            : new(GravedadLegalizacion.Correcta, $"Plazo de presentación: hasta el {plazo:dd/MM/yyyy}",
                hoy.Year <= d.Ejercicio ? "El plazo empieza al cerrar el ejercicio." : $"Quedan {(plazo - hoy.Date).Days} días."));
        return r;
    }
}
