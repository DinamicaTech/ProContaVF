using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// Fichero del modelo 200 para importar en Sociedades WEB o presentar, escrito campo a campo desde la tabla del diseño de registro del
/// ejercicio (<c>Diseno200.g.cs</c>, generada desde sources/aeat): cabecera &lt;T2000EEEE0A0000&gt; con &lt;AUX&gt;, las páginas que
/// genera el programa en el orden del diseño y el cierre.
/// <list type="bullet">
/// <item>Un diseño por ejercicio (<see cref="Diseno"/>): el de 2025 es la versión 1.02. Sin diseño del ejercicio no se genera; cuando la
/// AEAT publique el de 2026 se añade su tabla (contabilidad/modelos-tributarios/modelo-200, «Cambio de ejercicio»).</item>
/// <item>Importes de 17 posiciones con 2 decimales («N» delante si es negativo y el campo lo admite); la plantilla media, con 2 decimales;
/// las claves de la página 1, «1» o «0»; el tipo de gravamen (00558), sus cuatro cifras.</item>
/// </list>
/// </summary>
public static class Fichero200
{
    public const int LongitudCabecera = 328;

    /// <summary>Páginas del diseño de un ejercicio; null si el programa aún no tiene ese diseño.</summary>
    internal static IReadOnlyList<Pagina390>? Diseno(int ejercicio) => ejercicio switch
    {
        2025 => Diseno200.Paginas2025,
        _ => null,
    };

    /// <summary>Ejercicios con diseño en el programa.</summary>
    public static IReadOnlyList<int> Ejercicios => [2025];

    public static string Generar(Declaracion200 d, ProgramaAeat programa)
    {
        var diseno = Diseno(d.Ejercicio) ?? throw new InvalidOperationException(
            $"El programa no tiene el diseño de registro del modelo 200 de {d.Ejercicio}: solo el de {string.Join(", ", Ejercicios)}.");
        var ejercicio = d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture);
        var r = new StringBuilder();
        r.Append("<T2000").Append(ejercicio).Append("0A0000>");
        r.Append("<AUX>").Append(' ', 70).Append(Fichero303.An(programa.Version, 4)).Append(' ', 4).Append(Fichero303.An(programa.NifDesarrollo, 9)).Append(' ', 213).Append("</AUX>");
        if (r.Length != LongitudCabecera) throw new InvalidOperationException($"El fichero del 200 tiene mal la cabecera: {r.Length} posiciones.");
        foreach (var pagina in diseno.Where(p => d.Paginas.Contains(p.Codigo)))
        {
            // La página 2 bis lleva un titular real: se repite por cada uno (el secretario y los representantes, en la primera).
            var copias = pagina.Codigo == "02B00" ? Math.Max(1, (d.Datos.TitularesReales ?? []).Count) : 1;
            for (var i = 0; i < copias; i++) r.Append(Pagina(pagina, d, i));
        }
        r.Append("</T2000").Append(ejercicio).Append("0A0000>");
        return r.ToString();
    }

    /// <summary>NIF del declarante leído del fichero (página 1, posición 14).</summary>
    public static string? LeerNif(string fichero)
    {
        var i = fichero.IndexOf("<T20001000>", StringComparison.Ordinal);
        return i < 0 || fichero.Length < i + 22 ? null : fichero.Substring(i + 13, 9).Trim();
    }

    internal static string Pagina(Pagina390 pagina, Declaracion200 d, int copia = 0)
    {
        var textos = Textos(pagina.Codigo, d, copia);
        var p = new StringBuilder("<T200").Append(pagina.Codigo).Append('>');
        foreach (var campo in pagina.Campos)
        {
            if (p.Length != campo.Posicion - 1)
                throw new InvalidOperationException($"El diseño del 200 (página {pagina.Codigo}) no es continuo en la posición {campo.Posicion}.");
            if (campo.Descripcion.Contains("Fin de registro", StringComparison.OrdinalIgnoreCase)) { p.Append("</T200").Append(pagina.Codigo).Append('>'); continue; }
            if (textos.TryGetValue(campo.Posicion, out var texto))
                p.Append(campo.EsNumerico ? texto.PadLeft(campo.Longitud, '0') : Fichero303.An(texto, campo.Longitud));
            else if (campo.EsNumerico && campo.Longitud == 1)
                p.Append(campo.Casilla != 0 && pagina.Codigo == "01000" && d.Claves.Contains(campo.Casilla) ? '1' : '0');
            else if (campo.EsNumerico)
                p.Append(Numero(campo.Casilla == 0 ? 0m : d.Casilla(pagina.Codigo, campo.Casilla), campo, pagina.Codigo));
            else p.Append(' ', campo.Longitud);
        }
        if (p.Length != pagina.Longitud) throw new InvalidOperationException($"La página {pagina.Codigo} del 200 tiene {p.Length} posiciones en lugar de {pagina.Longitud}.");
        return p.ToString();
    }

    /// <summary>Numérico del diseño con 2 decimales; con signo (N), «N» delante si es negativo.</summary>
    private static string Numero(decimal v, Campo390 campo, string pagina)
    {
        if (v < 0m && campo.Tipo != "N") throw new ArgumentOutOfRangeException(nameof(v), $"La casilla {campo.Casilla:00000} del 200 (página {pagina}) no admite el negativo {v}.");
        var cifras = decimal.Round(Math.Abs(v) * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        var largo = v < 0m ? campo.Longitud - 1 : campo.Longitud;
        if (cifras.Length > largo) throw new ArgumentOutOfRangeException(nameof(v), $"El importe {v} no cabe en la casilla {campo.Casilla:00000} del 200.");
        return (v < 0m ? "N" : "") + cifras.PadLeft(largo, '0');
    }

    /// <summary>Contenido de los campos que no son importes, por posición en la página.</summary>
    private static Dictionary<int, string> Textos(string pagina, Declaracion200 d, int copia)
    {
        var t = new Dictionary<int, string>();
        var e = d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture);
        string Cent(decimal v) => decimal.Round(Math.Abs(v) * 100m, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        switch (pagina)
        {
            case "01000":
                t[13] = d.TipoDeclaracion.ToString();
                t[14] = d.Declarante.Nif;
                t[23] = d.Declarante.RazonSocial;
                t[103] = e;
                t[107] = "0A";
                t[109] = e; t[113] = "01"; t[115] = "01";
                t[117] = e; t[121] = "12"; t[123] = "31";
                t[125] = "1";                                       // tipo de ejercicio (1, 2 o 3): 1, el periodo impositivo empieza en el ejercicio
                t[126] = d.Datos.Cnae.Trim();
                t[130] = "0";                                       // actividades agrícolas o ganaderas: no
                t[131] = d.Datos.Telefono?.Trim() ?? "";
                if (d.JustificanteAnterior is not null) { t[149] = "1"; t[150] = d.JustificanteAnterior; t[163] = "1"; }
                t[241] = "1";                                       // cifra de negocios de los doce meses anteriores inferior a 20 M€
                if (d.Datos.Telefono is { Length: > 0 } tel && tel.Trim().All(char.IsAsciiDigit)) t[335] = tel.Trim();
                break;
            case "02000":
                var admins = d.Datos.Administradores ?? [];
                for (var i = 0; i < Math.Min(5, admins.Count); i++)
                {
                    var a = admins[i]; var b = 13 + 70 * i;
                    t[b] = Modelo200.Nif(a.Nif); t[b + 9] = a.Juridica ? "J" : "F"; t[b + 10] = "0"; t[b + 11] = a.Nombre;
                }
                var socios = d.Socios ?? [];
                for (var i = 0; i < Math.Min(6, socios.Count); i++)
                {
                    var so = socios[i]; var b = 1403 + 78 * i;
                    t[b] = Modelo200.Nif(so.Nif); t[b + 15] = "0"; t[b + 16] = so.Juridica ? "J" : "F"; t[b + 17] = so.Nombre; t[b + 54] = so.Provincia ?? "";
                    t[b + 56] = Cent(so.Nominal); t[b + 73] = Cent(so.Porcentaje);
                }
                break;
            case "02B00":
                if (copia > 0) t[12] = "C";                         // las copias de la página son complementarias
                if (d.Datos.TitularesReales is { Count: > 0 } titulares && copia < titulares.Count)
                {
                    var tr = titulares[copia];
                    t[2213] = "0"; t[2214] = tr.TipoDocumento.ToString(CultureInfo.InvariantCulture); t[2215] = Modelo200.Nif(tr.Nif); t[2233] = tr.Nombre;
                    t[2273] = tr.TipoDocumento == 1 ? "" : tr.PaisExpedicion; t[2275] = tr.FechaNacimiento?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? ""; t[2283] = tr.PaisResidencia; t[2285] = tr.Nacionalidad;
                }
                if (copia == 0)
                {
                    if (d.Datos.Secretario is { } sec) { t[2287] = sec.Nombre; t[2308] = Modelo200.Nif(sec.Nif); }
                    var reps = d.Datos.Representantes ?? [];
                    for (var i = 0; i < Math.Min(3, reps.Count); i++)
                    {
                        var b = 2317 + 65 * i;
                        t[b] = reps[i].Nombre; t[b + 36] = Modelo200.Nif(reps[i].Nif);
                        t[b + 45] = reps[i].FechaPoder?.ToString("yyyyMMdd", CultureInfo.InvariantCulture) ?? ""; t[b + 53] = reps[i].Notaria;
                    }
                }
                break;
            case "01B00":
                t[156] = "3";                                       // balance: modelo PYMES
                t[157] = "0";                                       // estado de cambios en el patrimonio neto: no consta
                t[158] = "3";                                       // pérdidas y ganancias: modelo PYMES
                t[162] = Cent(d.Datos.PersonalFijo);
                t[171] = Cent(d.Datos.PersonalNoFijo);
                break;
            case "14000":
                t[Diseno200Posicion(d.Ejercicio, "14000", 558)] = d.TipoGravamen;
                break;
            case "DID00":
                t[13] = "0";                                        // cuenta corriente tributaria: no
                t[14] = e; t[18] = "1"; t[19] = "0A";
                t[21] = "01"; t[23] = "01"; t[25] = e[2..]; t[27] = "31"; t[29] = "12"; t[31] = e[2..];
                t[33] = d.Declarante.Nif;
                t[42] = d.Declarante.RazonSocial;
                if (d.TipoDeclaracion is 'D' or 'R')
                {
                    t[207] = d.TipoDeclaracion.ToString();
                    t[208] = Cent(d.Resultado);
                    if (d.TipoDeclaracion == 'D')
                    {
                        var iban = (d.Declarante.Iban ?? "").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                        t[242] = iban.StartsWith("ES", StringComparison.Ordinal) ? "1" : "2";
                        t[243] = iban;
                    }
                }
                if (d.TipoDeclaracion is 'I' or 'U')
                {
                    t[425] = d.TipoDeclaracion.ToString();
                    t[426] = Cent(d.Resultado);
                    if (d.TipoDeclaracion == 'U') t[443] = (d.Declarante.Iban ?? "").Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();
                }
                t[562] = d.TipoDeclaracion == 'N' ? "1" : "0";
                break;
        }
        return t;
    }

    private static int Diseno200Posicion(int ejercicio, string pagina, int casilla) =>
        Diseno(ejercicio)!.First(p => p.Codigo == pagina).Campos.First(c => c.Casilla == casilla).Posicion;
}
