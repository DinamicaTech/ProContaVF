using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Ratios;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;

namespace ProConta.Inicio.UI;

/// <summary>
/// Cómo se muestran los ratios (informes/ratios): veces con 2 decimales, porcentajes con 1 y días enteros, redondeando solo al
/// mostrar (al alejarse del cero en el medio). Estado con icono y texto.
/// </summary>
public static class FormatoRatios
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public static string Valor(DefinicionRatio d, decimal? v) => v is not decimal x ? "sin datos" : d.Unidad switch
    {
        UnidadRatio.Porcentaje => Math.Round(x, 1, MidpointRounding.AwayFromZero).ToString("#,##0.0", Es) + " %",
        UnidadRatio.Dias => Math.Round(x, 0, MidpointRounding.AwayFromZero).ToString("#,##0", Es) + " días",
        _ => Math.Round(x, 2, MidpointRounding.AwayFromZero).ToString("#,##0.00", Es),
    };

    public static string Estado(EstadoRatio e) => e switch
    {
        EstadoRatio.Bien => "✓ Bien",
        EstadoRatio.Vigilar => "! Vigilar",
        EstadoRatio.Mal => "✕ Mal",
        _ => "— Sin datos",
    };

    public static string Familia(FamiliaRatio f) => f switch
    {
        FamiliaRatio.Liquidez => "Liquidez",
        FamiliaRatio.SolvenciaEndeudamiento => "Solvencia y endeudamiento",
        FamiliaRatio.RentabilidadMargenes => "Rentabilidad y márgenes",
        _ => "Gestión",
    };

    public static string Importe(decimal v) => Formatos.Importe(Math.Round(v, 2, MidpointRounding.AwayFromZero));
}

/// <summary>Fila de un ratio en el bloque: nombre, valor, franja de rango y estado; pulsarla abre su ficha.</summary>
public sealed record FilaRatioVM(
    string Nombre, string Valor, EstadoRatio Estado, string TextoEstado, decimal? Numero, decimal? Comparado, decimal? Minimo, decimal? Maximo, bool MenosEsMejor,
    string Ayuda, ICommand Comando);

/// <summary>Una familia de ratios con sus filas.</summary>
public sealed record FamiliaRatioVM(string Nombre, IReadOnlyList<FilaRatioVM> Filas);

/// <summary>Partida de la ficha: qué cuentas incluye y su importe, que abre el Mayor de esas cuentas.</summary>
public sealed record PartidaRatioVM(string Nombre, string Cuentas, string Importe, string Ayuda, ICommand? AbrirMayor);

/// <summary>Valor de un ratio a cierre de un ejercicio (o del actual) en la evolución.</summary>
public sealed record EvolucionRatioVM(string Ejercicio, string Valor, EstadoRatio Estado, string Ayuda);

/// <summary>Ficha de un ratio: fórmula y referencia, partidas, resultado con su estado y evolución por ejercicio.</summary>
public sealed class FichaRatioVM
{
    public required string Titulo { get; init; }
    public required string Subtitulo { get; init; }
    public required string Formula { get; init; }
    public required string Referencia { get; init; }
    public required IReadOnlyList<PartidaRatioVM> Partidas { get; init; }
    public required string Resultado { get; init; }
    public required EstadoRatio Estado { get; init; }
    public required string TextoEstado { get; init; }
    public string? Motivo { get; init; }
    public bool HayMotivo => Motivo is not null;
    public required IReadOnlyList<EvolucionRatioVM> Evolucion { get; init; }
    public required string NotaEvolucion { get; init; }
    public ICommand? AbrirBalanceCommand { get; init; }
    public bool HayBalance => AbrirBalanceCommand is not null;
    /// <summary>Lo pide la ficha al abrir otra pantalla: el diálogo se cierra.</summary>
    public Action? Cerrar { get; set; }
}
