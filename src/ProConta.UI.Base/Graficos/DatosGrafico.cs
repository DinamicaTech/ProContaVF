namespace ProConta.UI.Base.Graficos;

/// <summary>
/// Paleta categórica de las series (maqueta de la pantalla de inicio, validada): el orden es fijo y nunca se rota; la primera
/// serie es siempre la del dato principal.
/// </summary>
public static class PaletaGraficos
{
    public const string Serie1 = "#2a78d6";
    public const string Serie2 = "#eb6834";
    public const string Serie3 = "#1baf7a";
    public const string Serie4 = "#eda100";
    public const string Serie5 = "#e87ba4";
    public const string Rejilla = "#ECE8DE";
    public const string Apagado = "#7A8090";

    public static IReadOnlyList<string> Series { get; } = [Serie1, Serie2, Serie3, Serie4, Serie5];

    /// <summary>Color de la serie <paramref name="indice"/>; a partir de la sexta, el gris apagado (agrupar antes en «Otras»).</summary>
    public static string Color(int indice) => indice >= 0 && indice < Series.Count ? Series[indice] : Apagado;
}

/// <summary>Una fila del tooltip: muestra de color (opcional), etiqueta y valor.</summary>
public sealed record FilaTooltip(string Etiqueta, string Valor, string? Color = null, bool Destacada = false);

/// <summary>Contenido del tooltip de un gráfico al pasar el ratón: título y filas.</summary>
public sealed record ContenidoTooltip(string Titulo, IReadOnlyList<FilaTooltip> Filas);

/// <summary>
/// Serie de un gráfico: un valor por categoría (null = sin dato: hueco, nunca se interpola). En barras, <paramref name="Proyeccion"/>
/// se apila rayada encima del valor real; en líneas, una serie <paramref name="Discontinua"/> se dibuja a trazos con el último
/// punto hueco y <paramref name="Area"/> rellena suavemente bajo la línea.
/// </summary>
public sealed record SerieGrafico(
    string Nombre, string Color, IReadOnlyList<decimal?> Valores, IReadOnlyList<decimal?>? Proyeccion = null,
    bool Discontinua = false, bool Area = false, string? Rotulo = null);

/// <summary>
/// Datos de un gráfico de barras agrupadas por categoría (meses) o de líneas. <paramref name="Tooltip"/> da el contenido de
/// cada categoría; sin él, el motor lo arma con el nombre y el valor de cada serie. <paramref name="Marca"/> es el índice de
/// la categoría con la marca vertical (por ejemplo «hoy») y <paramref name="TextoMarca"/> su rótulo.
/// </summary>
public sealed record DatosGrafico(
    IReadOnlyList<string> Categorias, IReadOnlyList<SerieGrafico> Series, Func<int, ContenidoTooltip>? Tooltip = null,
    int? Marca = null, string? TextoMarca = null, double? Minimo = null, double? Maximo = null, string? Descripcion = null)
{
    /// <summary>Todos los valores que debe abarcar el eje (en barras, real más proyección apilada).</summary>
    public IEnumerable<double?> ValoresEje(bool apilarProyeccion)
    {
        foreach (var s in Series)
            for (var i = 0; i < s.Valores.Count; i++)
            {
                var v = s.Valores[i];
                var p = s.Proyeccion is { } pr && i < pr.Count ? pr[i] : null;
                if (apilarProyeccion && p is not null) yield return (double)((v ?? 0) + p.Value);
                else if (!apilarProyeccion && p is not null) yield return (double)p.Value;
                if (v is not null) yield return (double)v.Value;
            }
    }

    /// <summary>Tooltip de la categoría <paramref name="indice"/>: el del consumidor o uno con el valor de cada serie.</summary>
    public ContenidoTooltip TooltipDe(int indice, Func<decimal, string> formato)
    {
        if (Tooltip is not null) return Tooltip(indice);
        var filas = new List<FilaTooltip>();
        foreach (var s in Series)
        {
            var v = indice < s.Valores.Count ? s.Valores[indice] : null;
            var p = s.Proyeccion is { } pr && indice < pr.Count ? pr[indice] : null;
            if (v is null && p is null && s.Discontinua) continue;
            filas.Add(new FilaTooltip(s.Nombre, v is null ? "—" : formato(v.Value), s.Color));
            if (p is not null) filas.Add(new FilaTooltip("Proyección", formato(p.Value)));
        }
        return new ContenidoTooltip(indice < Categorias.Count ? Categorias[indice] : "", filas);
    }
}

/// <summary>Tramo de una barra apilada (antigüedad de la deuda): etiqueta, importe y color.</summary>
public sealed record TramoApilado(string Etiqueta, decimal Valor, string Color, string? Ayuda = null);
