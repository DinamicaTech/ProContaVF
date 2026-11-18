using System.Globalization;
using System.Text;
using ProConta.Nucleo.Identificacion;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// Ficheros del 111 y del 115 con los diseños de registro de la AEAT (sources/aeat/DR111v2.xls y DR115v13.xls): cabecera
/// &lt;T1110EEEEPP0000&gt; (o &lt;T1150…&gt;) con el bloque &lt;AUX&gt; (versión del programa y NIF de la empresa desarrolladora,
/// <see cref="ProgramaAeat"/>), una sola página 01 y el cierre. Un solo registro sin saltos de línea.
/// <list type="bullet">
/// <item>Alfanuméricos y numéricos con las mismas reglas que el 303 (<see cref="Fichero303"/>): mayúsculas a la izquierda con blancos;
/// importes de 17 (15 enteros y 2 decimales) con ceros a la izquierda; con signo, «N» delante si es negativo.</item>
/// <item>Número de perceptores: 8 posiciones en el 111 y 15 en el 115, con ceros a la izquierda.</item>
/// <item>Sociedad: la razón social en «Denominación o apellidos» (60) y «Nombre» en blanco.</item>
/// <item>El IBAN solo va si se domicilia (U); complementaria: «X» y el justificante de la anterior.</item>
/// </list>
/// </summary>
public static class FicheroRetenciones
{
    public const int LongitudCabecera = 328, LongitudPagina111 = 1000, LongitudPagina115 = 500;

    public static string Generar(DeclaracionRetenciones d, ProgramaAeat programa)
    {
        var es111 = d.Modelo == Modelo111.Modelo;
        if (!es111 && d.Modelo != Modelo115.Modelo) throw new ArgumentException($"El modelo {d.Modelo} no es de retenciones.", nameof(d));
        var ejercicio = d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture);
        var r = new StringBuilder();
        r.Append("<T").Append(d.Modelo).Append('0').Append(ejercicio).Append(d.Periodo).Append("0000>");
        r.Append("<AUX>").Append(' ', 70).Append(Fichero303.An(programa.Version, 4)).Append(' ', 4).Append(Fichero303.An(programa.NifDesarrollo, 9)).Append(' ', 213).Append("</AUX>");
        Comprobar(r.Length, LongitudCabecera, d.Modelo, "la cabecera");
        r.Append(es111 ? Pagina111(d) : Pagina115(d));
        r.Append("</T").Append(d.Modelo).Append('0').Append(ejercicio).Append(d.Periodo).Append("0000>");
        return r.ToString();
    }

    /// <summary>NIF del declarante leído del propio fichero (página 01, posición 14).</summary>
    public static string? LeerNif(string fichero)
    {
        foreach (var marca in new[] { "<T11101000>", "<T11501000>" })
        {
            var i = fichero.IndexOf(marca, StringComparison.Ordinal);
            if (i >= 0) return fichero.Length < i + 22 ? null : fichero.Substring(i + 13, 9).Trim();
        }
        return null;
    }

    private static void Identificacion(StringBuilder p, DeclaracionRetenciones d)
    {
        p.Append(d.TipoDeclaracion);                                // 13 tipo de declaración
        p.Append(Fichero303.An(d.Declarante.Nif, 9));               // 14 NIF
        p.Append(Fichero303.An(d.Declarante.RazonSocial, 60));      // 23 denominación
        p.Append(' ', 20);                                          // 83 nombre (solo personas físicas)
        p.Append(d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture)); // 103
        p.Append(d.Periodo);                                        // 107
    }

    private static string Pagina111(DeclaracionRetenciones d)
    {
        var p = new StringBuilder("<T11101000>");
        p.Append(' ');                                              // 12 página complementaria
        Identificacion(p, d);
        for (var g = 0; g < 9; g++)                                 // 109-486: nueve grupos de perceptores, percepciones y retenciones (01-27)
        {
            var n = 3 * g + 1;
            p.Append(Perceptores(d.Casilla(n), 8)).Append(Fichero303.Num(d.Casilla(n + 1))).Append(Fichero303.Num(d.Casilla(n + 2)));
        }
        p.Append(Fichero303.Num(d.Casilla(28)));                    // 487
        p.Append(Fichero303.N(d.Casilla(29)));                      // 504
        p.Append(Fichero303.Num(d.Casilla(30)));                    // 521
        Complementaria(p, d);                                       // 538-551
        p.Append(' ');                                              // 552 colegio concertado
        p.Append(Iban(d));                                          // 553
        p.Append(' ', 389).Append(' ', 13);                         // 587 reservado y 976 sello
        p.Append("</T11101000>");
        Comprobar(p.Length, LongitudPagina111, d.Modelo, "la página 01");
        return p.ToString();
    }

    private static string Pagina115(DeclaracionRetenciones d)
    {
        var p = new StringBuilder("<T11501000>");
        p.Append(' ');                                              // 12 página complementaria
        Identificacion(p, d);
        p.Append(Perceptores(d.Casilla(1), 15));                    // 109 [01]
        p.Append(Fichero303.Num(d.Casilla(2)));                     // 124 [02]
        p.Append(Fichero303.Num(d.Casilla(3)));                     // 141 [03]
        p.Append(Fichero303.Num(d.Casilla(4)));                     // 158 [04]
        p.Append(Fichero303.Num(d.Casilla(5)));                     // 175 [05]
        Complementaria(p, d);                                       // 192-205
        p.Append(Iban(d));                                          // 206
        p.Append(' ', 236).Append(' ', 13);                         // 240 reservado y 476 sello
        p.Append("</T11501000>");
        Comprobar(p.Length, LongitudPagina115, d.Modelo, "la página 01");
        return p.ToString();
    }

    private static void Complementaria(StringBuilder p, DeclaracionRetenciones d)
    {
        p.Append(d.Rectificativa is null ? ' ' : 'X');
        p.Append(Fichero303.An(d.Rectificativa?.JustificanteAnterior, 13));
    }

    private static string Iban(DeclaracionRetenciones d) => Fichero303.An(d.TipoDeclaracion == 'U' ? Ibanes.Normalizar(d.Declarante.Iban) : "", 34);

    private static string Perceptores(decimal n, int longitud)
    {
        if (n < 0m || n != decimal.Truncate(n)) throw new ArgumentOutOfRangeException(nameof(n), $"El número de perceptores {n} no es válido.");
        var s = n.ToString("0", CultureInfo.InvariantCulture);
        if (s.Length > longitud) throw new ArgumentOutOfRangeException(nameof(n), $"El número de perceptores {n} no cabe en {longitud} posiciones.");
        return s.PadLeft(longitud, '0');
    }

    private static void Comprobar(int longitud, int esperada, string modelo, string que)
    {
        if (longitud != esperada) throw new InvalidOperationException($"El fichero del {modelo} tiene mal {que}: {longitud} posiciones en lugar de {esperada}.");
    }
}
