namespace ProConta.UI.Base.Graficos;

/// <summary>Una serie del gráfico temporal: un valor por fecha (null = hueco, no se interpola).</summary>
/// <param name="Grosor">Grosor de la línea en píxeles.</param>
/// <param name="Sutil">Fechas en las que la línea se dibuja punteada y muy tenue (por ejemplo, cuando el activo no estaba en cartera); null = ninguna.</param>
/// <param name="Escalon">La línea va en escalones: mantiene el valor hasta la fecha siguiente (por ejemplo, el capital invertido).</param>
public sealed record SerieTemporal(string Nombre, string Color, IReadOnlyList<double?> Valores, bool Discontinua = false, double Grosor = 2,
    IReadOnlyList<bool>? Sutil = null, bool Escalon = false);

/// <summary>Una marca en el eje de fechas (una compra ▲ o una venta ▼) en el índice de la fecha.</summary>
public sealed record MarcaTemporal(int Indice, bool Compra);

/// <summary>
/// Datos del gráfico temporal: fechas (ordenadas) y series con un valor por fecha. Con <paramref name="AreaEntre"/> (serie A, serie B) se sombrea el
/// espacio entre las dos: verde donde A está por encima de B y rojo donde está por debajo (por ejemplo, valor frente a coste: la plusvalía latente).
/// </summary>
/// <param name="IncluirCero">El eje vertical llega hasta el cero (importes); si no, se ajusta a los valores (precios).</param>
/// <param name="LineaCero">Dibuja más marcada la línea del cero (variaciones en %).</param>
public sealed record DatosTemporales(IReadOnlyList<DateTime> Fechas, IReadOnlyList<SerieTemporal> Series, Func<int, ContenidoTooltip>? Tooltip = null,
    (int A, int B)? AreaEntre = null, IReadOnlyList<MarcaTemporal>? Marcas = null, bool IncluirCero = false, bool LineaCero = false, string? Descripcion = null);
