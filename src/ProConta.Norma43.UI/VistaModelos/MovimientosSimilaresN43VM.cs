using System.Globalization;
using System.Windows.Input;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Similares;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>Lo que se pide en el diálogo de movimientos similares al cerrarlo.</summary>
public enum AccionSimilarN43 { Asignar, IrALinea }

/// <summary>Elección del diálogo: asignar la cuenta de un resultado o ir a su línea.</summary>
public sealed record EleccionSimilarN43(AccionSimilarN43 Accion, SimilarN43 Resultado);

/// <summary>
/// Diálogo «Movimientos similares» (norma43/movimientos-similares): las líneas con cuenta cuyo concepto se parece al de la línea, desde el
/// 20 % y hasta 50, del más parecido al menos. Desde cada resultado: asignar su cuenta a las líneas elegidas, copiarla, ver su asiento o ir a
/// la línea. Sustituye a frmMovimientosN43Similares.
/// </summary>
public sealed class MovimientosSimilaresN43VM : ModeloVista
{
    private readonly Func<int, Task>? _verAsiento;

    /// <param name="linea">La línea con la que se compara.</param>
    /// <param name="destino">Las líneas a las que «Asignar» pone la cuenta (la selección, o solo la línea si se abre desde el detalle).</param>
    /// <param name="motivoAsignar">Por qué no se puede asignar a esas líneas (ejercicio cerrado, ERP…); null si se puede.</param>
    public MovimientosSimilaresN43VM(LineaN43 linea, IReadOnlyList<LineaN43> destino, IReadOnlyList<SimilarN43> resultados, string? motivoAsignar,
        Func<int, Task>? verAsiento)
    {
        ArgumentNullException.ThrowIfNull(linea);
        ArgumentNullException.ThrowIfNull(destino);
        ArgumentNullException.ThrowIfNull(resultados);
        Linea = linea;
        Destino = destino;
        MotivoAsignar = motivoAsignar;
        _verAsiento = verAsiento;
        Resultados = resultados.Select(r => new SimilarN43VM(this, r)).ToList();
    }

    public LineaN43 Linea { get; }
    public IReadOnlyList<LineaN43> Destino { get; }
    public string? MotivoAsignar { get; }
    public IReadOnlyList<SimilarN43VM> Resultados { get; }
    public bool HayResultados => Resultados.Count > 0;

    /// <summary>Lo que el operador ha elegido al cerrar; null si cierra sin más.</summary>
    public EleccionSimilarN43? Eleccion { get; private set; }

    /// <summary>La vista lo escucha para cerrar el diálogo cuando se elige algo.</summary>
    public event Action? Cerrar;

    public string Titulo => $"Movimientos similares · {Minuscula(Linea.Rotulo)}";
    public string Concepto => Linea.Concepto ?? "";

    public string Explicacion
    {
        get
        {
            var (desde, hasta) = ReglasSimilaresN43.Rango(Linea.Ejercicio);
            return $"Líneas con cuenta de {desde} a {hasta} cuyo concepto comparte palabras con este, desde el {ReglasSimilaresN43.MinimoLista} % y hasta las " +
                $"{ReglasSimilaresN43.MaximoLista} más parecidas. No cuentan los números ni las palabras de menos de 3 letras; una palabra cortada por el banco cuenta como la completa.";
        }
    }

    public string Alcance => MotivoAsignar is { } m ? $"No se puede asignar: {m}"
        : Destino.Count == 1 ? $"«Asignar» pone la cuenta elegida al {Minuscula(Destino[0].Rotulo)}."
        : $"«Asignar» pone la cuenta elegida a las {Destino.Count} líneas seleccionadas.";

    public string TextoSinResultados =>
        $"Ninguna línea con cuenta se parece al menos un {ReglasSimilaresN43.MinimoLista} %. Asígnale la cuenta con «Asignar cuenta…» (F2) o crea una palabra clave (Ctrl+K) para que las próximas la tomen solas.";

    public string Resumen => HayResultados ? Formatos.Plural(Resultados.Count, "movimiento parecido", "movimientos parecidos") : "";

    internal void Elegir(AccionSimilarN43 accion, SimilarN43VM r)
    {
        Eleccion = new EleccionSimilarN43(accion, r.Datos);
        Cerrar?.Invoke();
    }

    internal Task VerAsientoAsync(int id) => _verAsiento?.Invoke(id) ?? Task.CompletedTask;
    internal bool PuedeVerAsientos => _verAsiento is not null;

    internal static string Minuscula(string s) => s.Length == 0 ? s : char.ToLowerInvariant(s[0]) + s[1..];
}

/// <summary>Un resultado del diálogo de movimientos similares, con sus acciones.</summary>
public sealed class SimilarN43VM : ModeloVista
{
    private readonly MovimientosSimilaresN43VM _padre;

    internal SimilarN43VM(MovimientosSimilaresN43VM padre, SimilarN43 datos)
    {
        _padre = padre;
        Datos = datos;
        AsignarCommand = new ComandoDelegado(() => _padre.Elegir(AccionSimilarN43.Asignar, this), () => PuedeAsignar);
        IrCommand = new ComandoDelegado(() => _padre.Elegir(AccionSimilarN43.IrALinea, this));
        VerAsientoCommand = new ComandoDelegado(() => { if (Datos.Linea.IdAsiento is int id) _ = _padre.VerAsientoAsync(id); }, () => PuedeVerAsiento);
    }

    public SimilarN43 Datos { get; }
    public string Porcentaje => $"{Datos.Porcentaje} %";
    /// <summary>Los de la mitad o más se resaltan: son los que de verdad se parecen.</summary>
    public bool Destacado => Datos.Porcentaje >= ReglasSimilaresN43.MinimoAvance;
    public string Fecha => Formatos.Fecha(Datos.Linea.Fecha);
    public string Importe => Formatos.Importe(Datos.Linea.EsCargo ? -Datos.Linea.Importe : Datos.Linea.Importe);
    public bool EsCargo => Datos.Linea.EsCargo;
    public string Concepto => Datos.Linea.Concepto;
    public string Cuenta => Datos.Linea.Cuenta ?? "(sin código)";
    public string NombreCuenta => Datos.Linea.NombreCuenta ?? "";
    public string Asiento => Datos.Linea.IdAsiento?.ToString(CultureInfo.InvariantCulture) ?? "";

    public bool PuedeAsignar => _padre.MotivoAsignar is null && Datos.Linea.Cuenta is not null;
    public bool PuedeVerAsiento => Datos.Linea.IdAsiento is not null && _padre.PuedeVerAsientos;

    public string AyudaAsignar => _padre.MotivoAsignar is { } m ? m
        : Datos.Linea.Cuenta is null ? "La cuenta de esta línea no tiene código: elige otra."
        : $"Pone la {Cuenta} ({NombreCuenta}) a {(_padre.Destino.Count == 1 ? "la línea" : $"las {_padre.Destino.Count} líneas seleccionadas")} y cierra; si ya estaban contabilizadas, regenera su asiento";
    public string AyudaCopiar => $"Copia la cuenta {Cuenta} al portapapeles";
    public string AyudaVerAsiento => PuedeVerAsiento ? $"Muestra el asiento {Asiento} de esta línea (también con doble clic)" : "Esta línea aún no está contabilizada: no tiene asiento.";
    public string AyudaIr => $"Cierra y selecciona en la lista la línea del {Fecha}" +
        " (si es de un ejercicio no marcado, lo marca; si los filtros la ocultan, los quita)";
    public string Ayuda => $"{Concepto}\n{Porcentaje} de palabras en común · {Cuenta} · {NombreCuenta}";

    public ICommand AsignarCommand { get; }
    public ICommand IrCommand { get; }
    public ICommand VerAsientoCommand { get; }
}

/// <summary>Un resultado del avance de movimientos similares en el detalle de la línea, con su botón «Usar».</summary>
public sealed class AvanceSimilarN43VM(SimilarN43 datos, string? motivo, Action usar) : ModeloVista
{
    public SimilarN43 Datos { get; } = datos;
    public string Porcentaje => $"{Datos.Porcentaje} %";
    public string Concepto => Datos.Linea.Concepto;
    public string Pie => $"{Datos.Linea.Cuenta} · {Datos.Linea.NombreCuenta} · {Formatos.Fecha(Datos.Linea.Fecha)}";
    public bool PuedeUsar => motivo is null;
    public string AyudaUsar => motivo ?? $"Pone la {Datos.Linea.Cuenta} ({Datos.Linea.NombreCuenta}) a esta línea; si ya estaba contabilizada, regenera su asiento";
    public ICommand UsarCommand { get; } = new ComandoDelegado(usar, () => motivo is null);
}
