using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Un campo de una página del diseño del 390: posición (desde 1), longitud, tipo (An, A, Num o N) y casilla (0 si no es una casilla).</summary>
internal sealed record Campo390(int Posicion, int Longitud, string Tipo, int Casilla, string Descripcion)
{
    public bool EsNumerico => Tipo is "Num" or "N";
}

/// <summary>Una página del diseño del 390: código («01000», «02B00»…), longitud total y campos desde la posición 12.</summary>
internal sealed record Pagina390(string Codigo, int Longitud, IReadOnlyList<Campo390> Campos);

/// <summary>
/// Fichero del modelo 390 con el diseño de registro de la AEAT (sources/aeat/DR390e2026v100.xlsx, versión 1.00), escrito campo a campo
/// desde la tabla generada del diseño (<c>Diseno390.g.cs</c>): cabecera &lt;T3900EEEE0A0000&gt; con &lt;AUX&gt;, las páginas 01 (sujeto
/// pasivo y datos estadísticos), 02 (devengado), 03 y 04 (deducible), 06 (resultado y volumen de operaciones) y 07 (operaciones
/// específicas y prorrata), y el cierre. Las páginas 02 bis (recargo de equivalencia), 05 (régimen simplificado) y 08 (sectores
/// diferenciados) no van: la sociedad no tiene esas operaciones.
/// <list type="bullet">
/// <item>Casillas: importes de 17 posiciones (15 enteros y 2 decimales) y porcentajes de 5 (3 y 2); con signo (N), «N» delante si es
/// negativo. Los numéricos que no son casillas que calcula el programa van a cero.</item>
/// <item>Alfanuméricos: los de la página 01 y de la prorrata desde la declaración; el resto en blanco.</item>
/// </list>
/// </summary>
public static class Fichero390
{
    public const int LongitudCabecera = 328;

    /// <summary>Páginas que se presentan.</summary>
    public static readonly IReadOnlyList<string> PaginasPresentadas = ["01000", "02000", "03000", "04000", "06000", "07000"];

    public static string Generar(Declaracion390 d, ProgramaAeat programa, IReadOnlyList<string>? paginas = null)
    {
        var ejercicio = d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture);
        var r = new StringBuilder();
        r.Append("<T3900").Append(ejercicio).Append("0A0000>");
        r.Append("<AUX>").Append(' ', 70).Append(Fichero303.An(programa.Version, 4)).Append(' ', 4).Append(Fichero303.An(programa.NifDesarrollo, 9)).Append(' ', 213).Append("</AUX>");
        if (r.Length != LongitudCabecera) throw new InvalidOperationException($"El fichero del 390 tiene mal la cabecera: {r.Length} posiciones.");
        foreach (var codigo in paginas ?? PaginasPresentadas) r.Append(Pagina(Diseno390.Paginas.First(p => p.Codigo == codigo), d));
        r.Append("</T3900").Append(ejercicio).Append("0A0000>");
        return r.ToString();
    }

    /// <summary>NIF del declarante leído del fichero (página 01, posición 14).</summary>
    public static string? LeerNif(string fichero)
    {
        var i = fichero.IndexOf("<T39001000>", StringComparison.Ordinal);
        return i < 0 || fichero.Length < i + 22 ? null : fichero.Substring(i + 13, 9).Trim();
    }

    internal static string Pagina(Pagina390 pagina, Declaracion390 d)
    {
        var textos = Textos(pagina.Codigo, d);
        var p = new StringBuilder("<T390").Append(pagina.Codigo).Append('>');
        foreach (var campo in pagina.Campos)
        {
            if (p.Length != campo.Posicion - 1)
                throw new InvalidOperationException($"El diseño del 390 (página {pagina.Codigo}) no es continuo en la posición {campo.Posicion}.");
            if (campo.Descripcion.Contains("Fin de registro", StringComparison.OrdinalIgnoreCase)) { p.Append("</T390").Append(pagina.Codigo).Append('>'); continue; }
            if (campo.EsNumerico)
            {
                // La prorrata va en la primera fila de la página 07 (las otras cuatro repiten las casillas 114 a 118 y van a cero).
                var otraFilaProrrata = pagina.Codigo == "07000" && campo.Posicion > 283;
                if (campo.Casilla == 0 && textos.TryGetValue(campo.Posicion, out var codigo))
                    p.Append(codigo.PadLeft(campo.Longitud, '0'));                       // códigos numéricos (ejercicio, marcas), tal cual
                else p.Append(Numero(campo.Casilla != 0 && !otraFilaProrrata ? d.Casilla(campo.Casilla) : 0m, campo));
            }
            else p.Append(Fichero303.An(textos.GetValueOrDefault(campo.Posicion), campo.Longitud));
        }
        if (p.Length != pagina.Longitud) throw new InvalidOperationException($"La página {pagina.Codigo} del 390 tiene {p.Length} posiciones en lugar de {pagina.Longitud}.");
        return p.ToString();
    }

    /// <summary>Numérico del diseño: 2 decimales salvo los de 6 posiciones (1 entero y 5 decimales); con signo, «N» delante si es negativo.</summary>
    private static string Numero(decimal v, Campo390 campo)
    {
        if (v < 0m && campo.Tipo != "N") throw new ArgumentOutOfRangeException(nameof(v), $"La casilla {campo.Casilla} del 390 no admite el negativo {v}.");
        var decimales = campo.Longitud == 6 ? 5 : 2;
        var cifras = decimal.Round(Math.Abs(v) * (decimal)Math.Pow(10, decimales), 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
        var largo = v < 0m ? campo.Longitud - 1 : campo.Longitud;
        if (cifras.Length > largo) throw new ArgumentOutOfRangeException(nameof(v), $"El importe {v} no cabe en la casilla {campo.Casilla} del 390.");
        return (v < 0m ? "N" : "") + cifras.PadLeft(largo, '0');
    }

    /// <summary>Contenido de los campos que no son casillas, por posición en la página.</summary>
    private static Dictionary<int, string> Textos(string pagina, Declaracion390 d)
    {
        var t = new Dictionary<int, string>();
        switch (pagina)
        {
            case "01000":
                t[14] = d.Declarante.Nif;
                t[23] = d.Declarante.RazonSocial;
                t[103] = d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture);
                t[109] = "0";                                       // registro de devolución mensual: no
                t[110] = "0";                                       // régimen especial del grupo de entidades: no
                t[118] = "0"; t[119] = "0"; t[120] = "0";
                t[130] = "2";                                       // concurso de acreedores en el ejercicio: no
                t[131] = "2";                                       // criterio de caja: no
                t[132] = "2";                                       // destinatario del criterio de caja: no
                t[133] = "0";
                t[134] = d.Sustitutiva is null ? "0" : "1";         // sustitutiva
                t[135] = "0";                                       // sustitutiva por rectificación de cuotas (concurso): no
                t[136] = d.Sustitutiva?.JustificanteAnterior ?? "";
                var actividades = d.Datos?.Actividades ?? [];
                for (var i = 0; i < Math.Min(6, actividades.Count); i++)
                {
                    var a = actividades[i];
                    var pos = 149 + 47 * i;
                    t[pos] = a.Descripcion; t[pos + 40] = a.Clave; t[pos + 43] = a.Epigrafe;
                }
                t[431] = d.Datos?.DeclaraOperacionesTerceros == true ? "1" : "0"; // declaración anual de operaciones con terceras personas (347)
                break;
            case "07000":
                if (d.Casilla(115) != 0m)
                {
                    t[200] = d.Datos?.Actividades.FirstOrDefault()?.Descripcion ?? "";
                    t[240] = d.Datos?.Cnae ?? "";
                    t[278] = d.TipoProrrata;
                }
                break;
        }
        return t;
    }
}
