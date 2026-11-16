using System.Collections.ObjectModel;
using ProConta.Norma43.Remesas;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>
/// Diálogo «Explotar remesa» (norma43/remesas): las facturas SEPA del mes pendientes de cobro, marcadas las que propone la remesa, con el
/// importe cobrado editable. Solo deja explotar si la suma cuadra con el abono del banco al céntimo; una factura cobrada por otro importe
/// deja la diferencia pendiente en la 430 de su cliente.
/// </summary>
public sealed class RemesaN43VM : ModeloVista
{
    public RemesaN43VM(PropuestaRemesaN43 propuesta, Action<int>? abrirFactura)
    {
        ArgumentNullException.ThrowIfNull(propuesta);
        Propuesta = propuesta;
        Filas = propuesta.Candidatas.Select(c => new FilaRemesaN43VM(this, c, abrirFactura)).ToList();
    }

    public PropuestaRemesaN43 Propuesta { get; }
    public IReadOnlyList<FilaRemesaN43VM> Filas { get; }
    public bool HayFacturas => Filas.Count > 0;

    public string Titulo => $"Remesa del {Formatos.Fecha(Propuesta.Linea.FechaOperacion)} · {Formatos.Importe(Propuesta.Linea.Importe)} € · {Propuesta.Linea.NombreBanco}";
    public string Concepto => Propuesta.Linea.Concepto ?? "";

    public string Explicacion => HayFacturas
        ? $"Facturas domiciliadas (SEPA) de {Formatos.Fecha(new DateTime(Propuesta.Linea.FechaOperacion.Year, Propuesta.Linea.FechaOperacion.Month, 1))[3..]} pendientes de cobro. " +
          $"Vienen marcadas las que se cargan a ±{ReglasRemesasN43.DiasCargo} días del día {Propuesta.Linea.FechaOperacion.Day}. Si el banco cobró una factura por otro importe " +
          "(por ejemplo, sin la actualización del IPC), escribe en «Cobrado» lo que cobró: la diferencia queda pendiente en la cuenta del cliente."
        : "No hay facturas domiciliadas (con forma de pago SEPA) de este mes pendientes de cobro. Revisa la forma y el día de pago de las facturas del mes, o si ya están cobradas.";

    private IEnumerable<FilaRemesaN43VM> Marcadas => Filas.Where(f => f.Marcada);

    /// <summary>Cuadra al céntimo y todos los importes marcados son válidos.</summary>
    public bool Cuadra => Marcadas.Any() && Marcadas.All(f => f.ImporteValido) && Marcadas.Sum(f => f.Cobrado) == Propuesta.Linea.Importe;

    public string Cuadre
    {
        get
        {
            if (Marcadas.Any(f => !f.ImporteValido)) return "Hay un importe cobrado que no es válido: escríbelo como 1.234,56.";
            var texto = ReglasRemesasN43.TextoCuadre(Propuesta.Linea.Importe, Marcadas.Select(f => f.Cobrado).ToList());
            var distintas = Marcadas.Count(f => f.Distinto);
            return distintas == 0 ? texto : $"{texto} {Formatos.Plural(distintas, "factura cobrada", "facturas cobradas")} por otro importe: la diferencia queda en la 430.";
        }
    }

    public IReadOnlyList<ReciboRemesa> Recibos() => Marcadas.Select(f => new ReciboRemesa(f.Datos.Id, f.Cobrado)).ToList();

    internal void AlCambiar() => Avisar(nameof(Cuadra), nameof(Cuadre));
}

/// <summary>Una factura del diálogo de la remesa.</summary>
public sealed class FilaRemesaN43VM : ModeloVista
{
    private readonly RemesaN43VM _padre;
    private readonly Action<int>? _abrir;
    private bool _marcada;
    private string _cobradoTexto;

    public FilaRemesaN43VM(RemesaN43VM padre, CandidataRemesa c, Action<int>? abrir)
    {
        _padre = padre;
        _abrir = abrir;
        Datos = c.Factura;
        _marcada = c.Marcada;
        _cobradoTexto = Formatos.Importe(c.Factura.Importe);
    }

    public FacturaRemesa Datos { get; }
    public bool Marcada { get => _marcada; set { if (Fijar(ref _marcada, value)) _padre.AlCambiar(); } }
    public string CobradoTexto
    {
        get => _cobradoTexto;
        set { if (Fijar(ref _cobradoTexto, value ?? "")) { Avisar(nameof(ImporteValido), nameof(Distinto), nameof(Ayuda)); _padre.AlCambiar(); } }
    }

    public bool ImporteValido => Formatos.LeerImporte(_cobradoTexto, out var i) && i > 0m && decimal.Round(i, 2) == i;
    public decimal Cobrado => Formatos.LeerImporte(_cobradoTexto, out var i) ? i : 0m;
    public bool Distinto => ImporteValido && Cobrado != Datos.Importe;

    public string Numero => Datos.Numero;
    public string Fecha => Formatos.Fecha(Datos.Fecha);
    public string Dia => Datos.DiaDePago?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string Cliente => Datos.Tercero ?? "";
    public string Descripcion => Datos.Descripcion ?? "";
    public string Importe => Formatos.Importe(Datos.Importe);
    public bool PuedeAbrir => _abrir is not null;
    public void Abrir() => _abrir?.Invoke(Datos.Id);

    public string Ayuda => $"Factura {Numero} del {Fecha} de {Cliente}, {Importe} €" + (Datos.DiaDePago is int d ? $", se carga el día {d}" : ", sin día de pago") + ". " +
        (Distinto ? $"El banco cobró {Formatos.Importe(Cobrado)} €: la diferencia de {Formatos.Importe(Datos.Importe - Cobrado)} € queda en la cuenta del cliente. " : "") +
        "Marca las que van en la remesa." + (PuedeAbrir ? " Doble clic: abrir la factura." : "");
}

/// <summary>
/// Diálogo «Conciliar facturas devueltas» (norma43/remesas): los recibos aún cobrados de la remesa más cercana anterior a la devolución
/// (se puede elegir otra), con marcada la combinación que suma el cargo. Solo deja conciliar si la suma cuadra.
/// </summary>
public sealed class DevolucionN43VM : ModeloVista
{
    private readonly Func<int, Task<PropuestaDevolucionN43?>> _leer;
    private PropuestaDevolucionN43 _propuesta;
    private ObservableCollection<FilaDevolucionN43VM> _filas = [];
    private RemesaAnteriorN43? _remesa;
    private string? _error;

    public DevolucionN43VM(PropuestaDevolucionN43 propuesta, Func<int, Task<PropuestaDevolucionN43?>> leer)
    {
        ArgumentNullException.ThrowIfNull(propuesta);
        _leer = leer;
        _propuesta = propuesta;
        Poner(propuesta);
    }

    public IReadOnlyList<RemesaAnteriorN43> Remesas => _propuesta.Remesas;

    /// <summary>La remesa de la que salen los recibos; al cambiarla se leen los suyos.</summary>
    public RemesaAnteriorN43? Remesa
    {
        get => _remesa;
        set
        {
            if (value is null || value.IdExtracto == _remesa?.IdExtracto) return;
            _remesa = value;
            Avisar(nameof(Remesa));
            _ = CambiarAsync(value.IdExtracto);
        }
    }

    private async Task CambiarAsync(int idExtracto)
    {
        try
        {
            if (await _leer(idExtracto) is { } p) Poner(p);
        }
        catch (Exception ex)
        {
            Error = $"No he podido leer los recibos de esa remesa: {ex.Message} Cierra y vuelve a abrir el diálogo.";
        }
    }

    private void Poner(PropuestaDevolucionN43 p)
    {
        _propuesta = p;
        _remesa = p.Remesa;
        Filas = new ObservableCollection<FilaDevolucionN43VM>(p.Candidatas.Select(c => new FilaDevolucionN43VM(this, c)));
        Error = p.Bloqueo?.TextoOperador();
        Avisar(nameof(Remesa), nameof(Remesas), nameof(Cuadre), nameof(Cuadra), nameof(HayRecibos), nameof(Explicacion));
    }

    public string? Error { get => _error; private set => Fijar(ref _error, value); }
    public ObservableCollection<FilaDevolucionN43VM> Filas { get => _filas; private set => Fijar(ref _filas, value); }
    public bool HayRecibos => _filas.Count > 0;

    public string Titulo => $"Devolución del {Formatos.Fecha(_propuesta.Linea.FechaOperacion)} · {Formatos.Importe(_propuesta.Linea.Importe)} € · {_propuesta.Linea.NombreBanco}";
    public string Concepto => _propuesta.Linea.Concepto ?? "";

    public string Explicacion => HayRecibos
        ? "Recibos de la remesa elegida que siguen cobrados. Viene marcada la combinación que suma el cargo del banco; marca los que se devolvieron. " +
          "Con un solo recibo, el movimiento va a la cuenta del cliente; con varios, se desglosa en un recibo devuelto por factura."
        : "Esta remesa no tiene recibos cobrados pendientes de devolver: elige otra remesa.";

    private IEnumerable<FilaDevolucionN43VM> Marcadas => _filas.Where(f => f.Marcada);
    public bool Cuadra => Marcadas.Any() && Marcadas.Sum(f => f.Datos.Importe) == _propuesta.Linea.Importe;
    public string Cuadre => ReglasRemesasN43.TextoCuadre(_propuesta.Linea.Importe, Marcadas.Select(f => f.Datos.Importe).ToList());

    public IReadOnlyList<int> IdsRecibos() => Marcadas.Select(f => f.Datos.IdLineaRecibo).ToList();

    internal void AlCambiar() => Avisar(nameof(Cuadra), nameof(Cuadre));
}

/// <summary>
/// Diálogo «Ajustes de importe» (norma43/remesas 2026-11-16 12:08-12:12), tras explotar una remesa con facturas cobradas por otro
/// importe: si el banco cobró más, actualizar la programada y emitir la factura de diferencia (marcadas si se pueden); si cobró menos,
/// el operador elige entre asumir la diferencia o cobrársela al cliente.
/// </summary>
public sealed class AjustesRemesaN43VM(IReadOnlyList<PropuestaAjusteN43> propuestas) : ModeloVista
{
    public IReadOnlyList<FilaAjusteN43VM> Filas { get; } = propuestas.Select(p => new FilaAjusteN43VM(p)).ToList();
    public IReadOnlyList<FilaAjusteN43VM> Mas => Filas.Where(f => f.Datos.CobraMas).ToList();
    public IReadOnlyList<FilaAjusteN43VM> Menos => Filas.Where(f => !f.Datos.CobraMas).ToList();

    public string Explicacion =>
        (Mas.Count > 0 ? "Facturas que el banco cobró por más: se puede actualizar su programada al importe cobrado y emitir una factura por la diferencia, que queda cobrada con el mismo recibo. " : "") +
        (Menos.Count > 0 ? "Facturas que el banco cobró por menos: elige si se cobra la diferencia al cliente (queda en su cuenta) o se asume. Al terminar se recuerda actualizar la remesa del banco." : "");

    public IReadOnlyList<AjusteElegidoN43> Elegidos(Func<int, int> reciboDe)
    {
        ArgumentNullException.ThrowIfNull(reciboDe);
        return Filas.Select(f => new AjusteElegidoN43(f.Datos.IdFactura, f.Datos.Cobrado, reciboDe(f.Datos.IdFactura), f.ActualizarProgramada && f.PuedeProgramada,
            f.EmitirDiferencia && f.PuedeDiferencia, f.Asumir && f.PuedeAsumir ? ModoMenorN43.Asumir : ModoMenorN43.CobrarAlCliente)).ToList();
    }
}

/// <summary>Una factura del diálogo de ajustes, con lo que se hará con ella.</summary>
public sealed class FilaAjusteN43VM(PropuestaAjusteN43 p) : ModeloVista
{
    private bool _programada = p.PuedeProgramada && p.CobraMas;
    private bool _diferencia = p.PuedeDiferencia;
    private bool _asumir = p.PuedeAsumir;

    public PropuestaAjusteN43 Datos { get; } = p;
    public string Numero => Datos.Numero;
    public string Importe => Formatos.Importe(Datos.Importe);
    public string Cobrado => Formatos.Importe(Datos.Cobrado);
    public string Diferencia => Formatos.Importe(Datos.Diferencia);
    public bool PuedeProgramada => Datos.PuedeProgramada;
    public bool PuedeDiferencia => Datos.PuedeDiferencia;
    public bool ActualizarProgramada { get => _programada; set => Fijar(ref _programada, value); }
    public bool EmitirDiferencia { get => _diferencia; set => Fijar(ref _diferencia, value); }
    /// <summary>Solo si cobró menos: true asume la diferencia, false la deja en la cuenta del cliente.</summary>
    public bool Asumir { get => _asumir; set => Fijar(ref _asumir, value); }

    public string TextoProgramada => Datos.PuedeProgramada
        ? $"Actualizar su programada de {Formatos.Importe(Datos.TotalProgramada ?? 0m)} € a {Cobrado} €"
        : $"Programada: {Datos.MotivoSinProgramada}";
    public bool PuedeAsumir => Datos.PuedeAsumir;
    public string TextoAsumir => Datos.PuedeAsumir
        ? $"Asumirla: anular la factura con una rectificativa y emitir otra por {Cobrado} €"
        : $"Asumirla: {Datos.MotivoSinAsumir}";
    public string TextoDiferencia => Datos.PuedeDiferencia
        ? $"Emitir una factura por la diferencia de {Diferencia} € (base {Formatos.Importe(Datos.BaseDiferencia ?? 0m)} €)"
        : $"Factura de diferencia: {Datos.MotivoSinDiferencia}";
}

/// <summary>Un recibo del diálogo de la devolución.</summary>
public sealed class FilaDevolucionN43VM(DevolucionN43VM padre, CandidataDevolucion c) : ModeloVista
{
    private bool _marcada = c.Marcada;

    public ReciboCobrado Datos { get; } = c.Recibo;
    public bool Marcada { get => _marcada; set { if (Fijar(ref _marcada, value)) padre.AlCambiar(); } }
    public string Numero => Datos.Factura.Numero;
    public string Cliente => Datos.Factura.Tercero ?? "";
    public string Descripcion => Datos.Factura.Descripcion ?? "";
    public string Importe => Formatos.Importe(Datos.Importe);
    public string Ayuda => $"Recibo de la factura {Numero} de {Cliente}, cobrado el {Formatos.Fecha(Datos.Fecha)} por {Importe} €. Márcalo si es uno de los devueltos.";
}
