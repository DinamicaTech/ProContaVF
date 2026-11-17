using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Datos del programa que van en el bloque &lt;AUX&gt; de la cabecera: versión (4 caracteres) y NIF de la empresa que lo desarrolla.</summary>
public sealed record ProgramaAeat(string Version, string NifDesarrollo)
{
    /// <summary>Versión del programa en los ficheros de la AEAT. Se sube cuando cambie el formato de algún modelo.</summary>
    public const string VersionActual = "0100";
}

/// <summary>
/// Fichero del modelo 303 de 2026 con el diseño de registro de la AEAT (sources/aeat/DR303e26v101.xlsx, versión 1.01): cabecera
/// &lt;T3030EEEEPP0000&gt; con el bloque &lt;AUX&gt;, página 01 (identificación y liquidación), página 03 (información adicional y resultado)
/// y la página DID (siempre: la AEAT la exige), con el IBAN si se domicilia o la rectificativa devuelve; las páginas 02 (régimen simplificado), 04 y 05 (exonerados
/// del 390) no se incluyen porque la sociedad no tiene contenido para ellas. Sin saltos de línea: es un solo registro.
/// <list type="bullet">
/// <item>Alfanuméricos: a la izquierda y con blancos a la derecha; en mayúsculas; solo los caracteres que admite la AEAT (los demás, blanco).</item>
/// <item>Numéricos (Num): 15 enteros y 2 decimales sin coma, con ceros a la izquierda. Con signo (N): un negativo lleva «N» en la primera
/// posición.</item>
/// </list>
/// </summary>
public static class Fichero303
{
    public const string Modelo = "303";

    /// <summary>Longitudes de cada página del diseño (para las pruebas y la comprobación al generar).</summary>
    public const int LongitudPagina01 = 1581, LongitudPagina03 = 1017, LongitudPaginaDid = 823, LongitudCabecera = 328;

    public static string Generar(Declaracion303 d, ProgramaAeat programa)
    {
        var periodo = d.Periodo;
        var ejercicio = d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture);
        var r = new StringBuilder();
        // Cabecera (DP30300).
        r.Append("<T").Append(Modelo).Append('0').Append(ejercicio).Append(periodo).Append("0000>");
        r.Append("<AUX>").Append(' ', 70).Append(An(programa.Version, 4)).Append(' ', 4).Append(An(programa.NifDesarrollo, 9)).Append(' ', 213).Append("</AUX>");
        Comprobar(r.Length, LongitudCabecera, "la cabecera");
        r.Append(Pagina01(d));
        r.Append(Pagina03(d));
        r.Append(PaginaDid(d));
        r.Append("</T").Append(Modelo).Append('0').Append(ejercicio).Append(periodo).Append("0000>");
        return r.ToString();
    }

    /// <summary>NIF del declarante leído del propio fichero (página 01, posición 14): el que se enseña en la conformidad.</summary>
    public static string? LeerNif(string fichero)
    {
        var i = fichero.IndexOf("<T30301000>", StringComparison.Ordinal);
        return i < 0 || fichero.Length < i + 22 ? null : fichero.Substring(i + 13, 9).Trim();
    }

    private static string Pagina01(Declaracion303 d)
    {
        var p = new StringBuilder();
        var ultimo = d.Trimestre == 4;
        p.Append("<T30301000>");
        p.Append(' ');                                  // 12 indicador de página complementaria
        p.Append(d.TipoDeclaracion);                    // 13 tipo de declaración
        p.Append(An(d.Declarante.Nif, 9));              // 14
        p.Append(An(d.Declarante.RazonSocial, 80));     // 23
        p.Append(d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture)); // 103
        p.Append(d.Periodo);                            // 107
        p.Append('2');                                  // 109 tributación exclusivamente foral: no
        p.Append('2');                                  // 110 registro de devolución mensual: no
        p.Append('3');                                  // 111 régimen simplificado: no (solo régimen general)
        p.Append('2');                                  // 112 autoliquidación conjunta: no
        p.Append('2');                                  // 113 criterio de caja: no
        p.Append('2');                                  // 114 destinatario de operaciones con criterio de caja: no
        p.Append('2');                                  // 115 opción por la prorrata especial: no (Nota 6; la AEAT no admite blanco)
        p.Append('2');                                  // 116 revocación de la prorrata especial: no
        p.Append('2');                                  // 117 concurso de acreedores: no
        p.Append(' ', 8);                               // 118 fecha del auto de concurso
        p.Append(' ');                                  // 126 tipo de autoliquidación en concurso
        p.Append('2');                                  // 127 SII voluntario: no
        p.Append(ultimo ? '2' : '0');                   // 128 exonerado del 390 (Nota 4): no en el 4T
        p.Append('0');                                  // 129 volumen de operaciones distinto de cero (Nota 3: 0 si no está exonerado)
        p.Append('0');                                  // 130 pago a cuenta de gasolinas (Nota 8)
        p.Append(Num(0m)).Append("00000").Append(Num(0m));          // 150-152
        p.Append(Num(0m)).Append("00000").Append(Num(0m));          // 165-167
        p.Append(Num(d.Casilla(1))).Append("00400").Append(Num(d.Casilla(3)));   // 01-03
        p.Append(Num(0m)).Append("00000").Append(Num(0m));          // 153-155
        p.Append(Num(d.Casilla(4))).Append("01000").Append(Num(d.Casilla(6)));   // 04-06
        p.Append(Num(d.Casilla(7))).Append("02100").Append(Num(d.Casilla(9)));   // 07-09
        p.Append(Num(d.Casilla(10))).Append(Num(d.Casilla(11)));    // 10-11
        p.Append(Num(d.Casilla(12))).Append(Num(d.Casilla(13)));    // 12-13
        p.Append(N(d.Casilla(14))).Append(N(d.Casilla(15)));        // 14-15
        p.Append(Num(0m)).Append("00175").Append(Num(0m));          // 156-158 recargo de equivalencia
        p.Append(Num(0m)).Append("00050").Append(Num(0m));          // 168-170
        p.Append(Num(0m)).Append("00000").Append(Num(0m));          // 16-18
        p.Append(Num(0m)).Append("00140").Append(Num(0m));          // 19-21
        p.Append(Num(0m)).Append("00520").Append(Num(0m));          // 22-24
        p.Append(N(0m)).Append(N(0m));                              // 25-26
        p.Append(N(d.Casilla(27)));                                 // 27
        for (var n = 28; n <= 39; n++) p.Append(Num(d.Casilla(n))); // 28-39
        for (var n = 40; n <= 46; n++) p.Append(N(d.Casilla(n)));   // 40-46
        p.Append(' ', 521).Append(' ', 13);                         // reservado y sello electrónico
        p.Append("</T30301000>");
        Comprobar(p.Length, LongitudPagina01, "la página 01");
        return p.ToString();
    }

    private static string Pagina03(Declaracion303 d)
    {
        var p = new StringBuilder();
        p.Append("<T30303000>");
        foreach (var n in new[] { 59, 60, 120, 122, 123, 124, 62, 63, 74, 75, 76, 64 }) p.Append(N(d.Casilla(n)));
        p.Append(Porcentaje(d.Casilla(65)));                        // 65
        p.Append(N(d.Casilla(66)));                                 // 66
        p.Append(Num(d.Casilla(77)));                               // 77
        p.Append(Num(d.Casilla(110))).Append(Num(d.Casilla(78))).Append(Num(d.Casilla(87)));
        p.Append(N(d.Casilla(68))).Append(N(d.Casilla(108))).Append(N(d.Casilla(69)));
        p.Append(Num(d.Casilla(70))).Append(Num(d.Casilla(109))).Append(Num(d.Casilla(112)));
        p.Append(N(d.Casilla(71)));
        p.Append(' ');                                              // declaración sin actividad
        var rect = d.Rectificativa;
        p.Append(rect is null ? ' ' : 'X');                         // autoliquidación rectificativa
        p.Append(An(rect?.JustificanteAnterior, 13));               // justificante de la anterior
        p.Append(' ');                                              // baja o modificación de la domiciliación
        p.Append(Num(d.Casilla(111)));                              // 111
        p.Append(rect is null ? ' ' : 'X');                         // motivo: rectificaciones
        p.Append(' ');                                              // motivo: discrepancia de criterio administrativo
        p.Append(' ', 546);
        p.Append("</T30303000>");
        Comprobar(p.Length, LongitudPagina03, "la página 03");
        return p.ToString();
    }

    private static string PaginaDid(Declaracion303 d)
    {
        var p = new StringBuilder();
        p.Append("<T303DID00>");
        p.Append(' ', 11);                                          // SWIFT-BIC (solo devoluciones al extranjero)
        p.Append(An(ConCuenta(d) ? Iban.Normalizar(d.Declarante.Iban) : "", 34)); // IBAN: solo al domiciliar o si la rectificativa devuelve
        p.Append(' ', 70).Append(' ', 35).Append(' ', 30).Append(' ', 2); // banco, dirección, ciudad y país (solo devoluciones al extranjero)
        p.Append('0');                                              // marca SEPA de la devolución: vacía
        p.Append(' ', 617);
        p.Append("</T303DID00>");
        Comprobar(p.Length, LongitudPaginaDid, "la página DID");
        return p.ToString();
    }

    /// <summary>La página DID lleva la cuenta al domiciliar (U) o cuando la rectificativa devuelve lo ingresado (casilla 111, Nota 3).</summary>
    private static bool ConCuenta(Declaracion303 d) => d.TipoDeclaracion == 'U' || d.Casilla(111) > 0m;

    /// <summary>Numérico sin signo de 17: 15 enteros y 2 decimales sin coma.</summary>
    internal static string Num(decimal v)
    {
        if (v < 0m) throw new ArgumentOutOfRangeException(nameof(v), $"La casilla sin signo no admite el negativo {v}.");
        return Cifras(v, 17);
    }

    /// <summary>Numérico con signo de 17: el negativo lleva «N» en la primera posición.</summary>
    internal static string N(decimal v) => v < 0m ? "N" + Cifras(-v, 16) : Cifras(v, 17);

    /// <summary>Porcentaje de 5: 3 enteros y 2 decimales (100 % = «10000»).</summary>
    internal static string Porcentaje(decimal v) => Cifras(v, 5);

    private static string Cifras(decimal v, int longitud)
    {
        var centimos = decimal.Round(v * 100m, 0, MidpointRounding.AwayFromZero);
        var s = centimos.ToString("0", CultureInfo.InvariantCulture);
        if (s.Length > longitud) throw new ArgumentOutOfRangeException(nameof(v), $"El importe {v} no cabe en {longitud} posiciones.");
        return s.PadLeft(longitud, '0');
    }

    /// <summary>Alfanumérico: mayúsculas, sin caracteres que la AEAT no admite, a la izquierda y con blancos a la derecha.</summary>
    internal static string An(string? s, int longitud)
    {
        var limpio = new string((s ?? "").Trim().ToUpper(CultureInfo.GetCultureInfo("es-ES")).Select(ch => Admitido(ch) ? ch : ' ').ToArray());
        return limpio.Length >= longitud ? limpio[..longitud] : limpio.PadRight(longitud);
    }

    /// <summary>Caracteres que admite la AEAT (especificación de los servicios, apartado 6, ISO-8859-1).</summary>
    private static bool Admitido(char ch) =>
        ch is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or ' ' or '&' or '\'' or '(' or ')' or '+' or ',' or '-' or '.' or '/' or ':' or ';' or '_' or '"' or '#' or '@' or '·' or 'º' or 'ª'
        || "ÇÑçñÁÉÍÓÚáéíóúÀÈÌÒÙàèìòùÄËÏÖÜäëïöüÂÊÎÔÛâêîôûÅåÃãÆæØøÕõß".Contains(ch);

    private static void Comprobar(int longitud, int esperada, string que)
    {
        if (longitud != esperada) throw new InvalidOperationException($"El fichero del 303 tiene mal {que}: {longitud} posiciones en lugar de {esperada}.");
    }
}
