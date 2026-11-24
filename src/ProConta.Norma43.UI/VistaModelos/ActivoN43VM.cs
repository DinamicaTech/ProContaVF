using ProConta.Norma43.Conciliacion;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>
/// Diálogo «Compra de activo…» / «Venta de activo…» de Líneas N43 (activos/activos-financieros 2026-11-24 09:14): las compras (cargo) o ventas (abono)
/// de activos financieros sin conciliar con su fecha e importe (el escrito o el estimado), la de importe más parecido al del banco primero y ya elegida.
/// Se elige una sola; al aceptar se genera el asiento de la línea y la operación queda conciliada.
/// </summary>
public sealed class ActivoN43VM : ModeloVista
{
    private bool _cambiando;

    public ActivoN43VM(PropuestaActivoN43 propuesta)
    {
        ArgumentNullException.ThrowIfNull(propuesta);
        Propuesta = propuesta;
        Filas = propuesta.Operaciones.Select(o => new FilaActivoN43VM(this, o)).ToList();
        if (Filas.Count > 0) Filas[0].Marcada = true;
    }

    public PropuestaActivoN43 Propuesta { get; }
    public IReadOnlyList<FilaActivoN43VM> Filas { get; }
    public string TituloDialogo => Propuesta.EsCompra ? "Compra de activo" : "Venta de activo";
    public string Titulo => $"{(Propuesta.EsCompra ? "Cargo" : "Abono")} del {Formatos.Fecha(Propuesta.Linea.FechaOperacion)} · {Formatos.Importe(Propuesta.Linea.Importe)} · {Propuesta.Linea.NombreBanco}";
    public string Concepto => Propuesta.Linea.Concepto ?? "";
    public string Explicacion => Propuesta.EsCompra
        ? "Elige la compra que paga este cargo. Se genera el asiento de la línea (la subcuenta del activo contra el banco) y la compra queda conciliada; su importe pasa a ser el del banco. " +
          "«Deshacer el asiento…» de la línea la deja otra vez sin conciliar."
        : "Elige la venta que cobra este abono. Se genera el asiento de la línea: el banco contra la subcuenta del activo por el coste medio de lo vendido, y la diferencia " +
          "a la 766 (beneficio) o a la 666 (pérdida) del activo. La venta queda conciliada y su importe pasa a ser el del banco. «Deshacer el asiento…» de la línea la deja otra vez sin conciliar.";

    public FilaActivoN43VM? Elegida => Filas.FirstOrDefault(f => f.Marcada);
    public bool Cuadra => Elegida is not null;

    public string Cuadre => Elegida is not { } e
        ? (Propuesta.EsCompra ? "Elige una compra." : "Elige una venta.")
        : e.Datos.Diferencia == 0m
            ? $"{e.Codigo}: el importe coincide con el del banco."
            : $"{e.Codigo}: el banco {(Propuesta.EsCompra ? "cargó" : "abonó")} {Formatos.Importe(Propuesta.Linea.Importe)} y la operación tenía {e.Importe}; se queda con el del banco.";

    public int? IdOperacion => Elegida?.Datos.Operacion.IdOperacion;

    /// <summary>Solo una marcada: al marcar una se desmarcan las demás.</summary>
    internal void AlMarcar(FilaActivoN43VM fila)
    {
        if (_cambiando) return;
        _cambiando = true;
        if (fila.Marcada) foreach (var f in Filas.Where(f => f != fila)) f.Marcada = false;
        _cambiando = false;
        Avisar(nameof(Elegida), nameof(Cuadra), nameof(Cuadre));
    }
}

/// <summary>Una compra o venta sin conciliar del diálogo.</summary>
public sealed class FilaActivoN43VM(ActivoN43VM padre, OperacionActivoN43 datos) : ModeloVista
{
    private bool _marcada;

    public OperacionActivoN43 Datos { get; } = datos;
    public bool Marcada { get => _marcada; set { if (Fijar(ref _marcada, value)) padre.AlMarcar(this); } }
    public string Codigo => Datos.Operacion.CodigoActivo;
    public string Nombre => Datos.Operacion.NombreActivo;
    public string Fecha => Formatos.Fecha(Datos.Operacion.Fecha);
    public string Participaciones => Formatos.Numero(Datos.Operacion.Participaciones);
    public string Importe => Formatos.Importe(Datos.Operacion.ImporteTotalEur);
    public bool Estimado => Datos.Operacion.Notas?.Contains("estimado", StringComparison.OrdinalIgnoreCase) == true;
    public string Origen => Estimado ? "Estimado" : "Escrito";
    public string Diferencia => Datos.Diferencia == 0m ? "" : Formatos.Importe(Datos.Diferencia);
    public string Ayuda => string.Join("\n", new[]
    {
        $"{Codigo} · {Nombre}",
        $"{(Datos.Operacion.EsCompra ? "Compra" : "Venta")} del {Fecha}: {Participaciones} participaciones por {Importe}" +
            (Estimado ? " (estimado: participaciones × precio del día)" : ""),
        Datos.Operacion.CosteVendido is { } c ? $"Coste medio de lo vendido: {Formatos.Importe(c)}" : null,
        Datos.Diferencia == 0m ? "Mismo importe que el banco" : $"Diferencia con el banco: {Formatos.Importe(Datos.Diferencia)}",
        Datos.Operacion.Notas,
    }.Where(t => !string.IsNullOrWhiteSpace(t)));
}
