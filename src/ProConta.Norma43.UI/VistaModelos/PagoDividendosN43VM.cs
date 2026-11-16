using ProConta.Contabilidad.Dividendos;
using ProConta.Norma43.Remesas;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>
/// Diálogo «Pago de dividendos» de Líneas N43 (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55): los socios activos de la sociedad con su
/// casilla (todos marcados), su % y el importe propuesto, el cargo repartido entre los marcados según su % (resto mayor al céntimo), editable. Al marcar
/// o desmarcar un socio se vuelve a repartir. Solo deja pagar si la suma cuadra con el cargo al céntimo.
/// </summary>
public sealed class PagoDividendosN43VM : ModeloVista
{
    private bool _repartiendo;

    public PagoDividendosN43VM(PropuestaPagoDividendosN43 propuesta)
    {
        ArgumentNullException.ThrowIfNull(propuesta);
        Propuesta = propuesta;
        Filas = propuesta.Socios.Select(s => new FilaPagoDividendosN43VM(this, s)).ToList();
        Repartir();
    }

    public PropuestaPagoDividendosN43 Propuesta { get; }
    public IReadOnlyList<FilaPagoDividendosN43VM> Filas { get; }

    public string Titulo => $"Cargo del {Formatos.Fecha(Propuesta.Linea.FechaOperacion)} · {Formatos.Importe(Propuesta.Linea.Importe)} · {Propuesta.Linea.NombreBanco}";
    public string Concepto => Propuesta.Linea.Concepto ?? "";
    public string Explicacion =>
        "Marca a quien o quienes paga el banco. El importe propuesto es el cargo repartido entre los marcados según su participación; puedes cambiarlo. " +
        "Con un socio, la línea va a su 526 con el concepto «Pago de dividendos <socio>». Con varios, se desglosa como una remesa en la cuenta bancaria de remesas del banco: " +
        "una línea por socio con su importe y su 526 (se deshace con «Deshacer explosión»).";

    private IEnumerable<FilaPagoDividendosN43VM> Marcadas => Filas.Where(f => f.Marcada);

    public bool Cuadra => Marcadas.Any() && Marcadas.All(f => f.ImporteValido) && Marcadas.Sum(f => f.Importe) == Propuesta.Linea.Importe;

    public string Cuadre
    {
        get
        {
            if (!Marcadas.Any()) return "Marca algún socio.";
            if (Marcadas.Any(f => !f.ImporteValido)) return "Hay un importe que no es válido: escríbelo como 1.234,56 (mayor que cero).";
            var texto = ReglasRemesasN43.TextoCuadre(Propuesta.Linea.Importe, Marcadas.Select(f => f.Importe).ToList());
            return Marcadas.Count() == 1 ? $"{texto} La línea irá a la 526 del socio." : $"{texto} Se desglosará en {Marcadas.Count()} líneas, una por socio.";
        }
    }

    public IReadOnlyList<PagoSocio> Pagos() => Marcadas.Select(f => new PagoSocio(f.Datos.IdSocio, f.Importe)).ToList();

    /// <summary>Reparte el cargo entre los socios marcados según su % (resto mayor al céntimo).</summary>
    internal void Repartir()
    {
        var marcadas = Marcadas.ToList();
        var partes = CalculoDividendos.Repartir(Propuesta.Linea.Importe, marcadas.Select(f => f.Datos.Participacion).ToList());
        _repartiendo = true;
        for (var i = 0; i < marcadas.Count; i++) marcadas[i].ImporteTexto = Formatos.Numero(partes[i]);
        foreach (var f in Filas.Where(f => !f.Marcada)) f.ImporteTexto = "";
        _repartiendo = false;
        AlCambiar();
    }

    internal void AlCambiar()
    {
        if (_repartiendo) return;
        Avisar(nameof(Cuadra), nameof(Cuadre));
    }
}

/// <summary>Un socio del diálogo del pago de dividendos.</summary>
public sealed class FilaPagoDividendosN43VM : ModeloVista
{
    private readonly PagoDividendosN43VM _padre;
    private bool _marcada;
    private string _importeTexto = "";

    public FilaPagoDividendosN43VM(PagoDividendosN43VM padre, SocioPagoDividendos datos)
    {
        _padre = padre;
        Datos = datos;
        _marcada = datos.Error is null;
    }

    public SocioPagoDividendos Datos { get; }
    public bool Elegible => Datos.Error is null;

    /// <summary>Si cobra en este pago; al cambiarla se vuelve a repartir el cargo entre los marcados.</summary>
    public bool Marcada
    {
        get => _marcada;
        set { if (Elegible && Fijar(ref _marcada, value)) _padre.Repartir(); }
    }

    public string ImporteTexto
    {
        get => _importeTexto;
        set { if (Fijar(ref _importeTexto, value ?? "")) { Avisar(nameof(ImporteValido), nameof(Ayuda)); _padre.AlCambiar(); } }
    }

    public bool ImporteValido => Formatos.LeerImporte(_importeTexto, out var i) && i > 0m && decimal.Round(i, 2) == i;
    public decimal Importe => Formatos.LeerImporte(_importeTexto, out var i) ? i : 0m;

    public string Nombre => Datos.Nombre;
    public string Participacion => Datos.Participacion.ToString("0.####", Formatos.Cultura) + " %";
    public string Cuenta => Datos.Cuenta526 ?? "";

    public string Ayuda => Datos.Error ?? $"{Nombre}, {Participacion} del capital: el pago va a su {Cuenta} (dividendos a pagar). Marca si cobra en este cargo y ajusta el importe si no es el de su participación.";
}
