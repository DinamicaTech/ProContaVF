using System.Globalization;
using ProConta.Norma43.Plataformas;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>
/// Diálogo «Conciliar plataformas» (norma43/conciliacion/plataformas): los cruces de los cobros de Airbnb, Booking… con las facturas de sus
/// reservas, todos marcados, que se desmarcan con un clic; lo que no cruza con su motivo, y las reservas facturadas que siguen sin cobro.
/// </summary>
public sealed class CobrosPlataformasN43VM : ModeloVista
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public CobrosPlataformasN43VM(PropuestaCobrosPlataformas propuesta, string periodo)
    {
        ArgumentNullException.ThrowIfNull(propuesta);
        Propuesta = propuesta;
        Periodo = periodo;
        Cruces = propuesta.Cruces.Select(c => new CrucePlataformaN43VM(this, c, propuesta)).ToList();
        SinCruzar = propuesta.SinCruzar
            .Select(s => (Linea: propuesta.Lineas[s.IdLinea], s.Motivo))
            .OrderBy(x => x.Linea.Fecha).ThenBy(x => x.Linea.Id)
            .Select(x => new TextoPlataformaN43(ServicioCobrosPlataformasN43.Describir(x.Linea), x.Motivo)).ToList();
        SinCobro = propuesta.SinCobro.Select(f => new TextoPlataformaN43(ServicioCobrosPlataformasN43.Describir(f),
            $"{f.Plataforma}: total {f.Total.ToString("N2", Es)} €, comisión {f.Comision.ToString("N2", Es)} €")).ToList();
    }

    public PropuestaCobrosPlataformas Propuesta { get; }
    public string Periodo { get; }
    public IReadOnlyList<CrucePlataformaN43VM> Cruces { get; }
    public IReadOnlyList<TextoPlataformaN43> SinCruzar { get; }
    public IReadOnlyList<TextoPlataformaN43> SinCobro { get; }

    public string Cabecera => Cruces.Count == 0
        ? $"No he encontrado ningún cobro de las plataformas que cuadre con sus reservas ({Periodo})."
        : $"{Formatos.Plural(Cruces.Count, "cruce", "cruces")} de cobros de las plataformas con las facturas de sus reservas ({Periodo}). " +
          "Desmarca con un clic los que no quieras conciliar.";

    public string Criterios =>
        "Cada reserva espera su total menos la comisión (y, en Booking, menos el 1,3 % de cargo por servicio de los pagos). Un cobro puede pagar " +
        $"hasta {ReglasCobrosPlataformas.MaximoReservasPorCobro} reservas y una reserva llegar en hasta {ReglasCobrosPlataformas.MaximoCobrosPorReserva} cobros, " +
        "dentro del plazo de su plataforma y con una sola combinación posible. Los cobros que completan una factura la dejan cobrada; " +
        "a las líneas sin contabilizar se les pone la 430 de la plataforma.";

    public IReadOnlyList<CrucePlataforma> Elegidos() => Cruces.Where(c => c.Marcado).Select(c => c.Datos).ToList();

    public bool HayMarcados => Cruces.Any(c => c.Marcado);

    public string Resumen
    {
        get
        {
            var m = Cruces.Where(c => c.Marcado).ToList();
            return m.Count == 0 ? "Ningún cruce marcado."
                : $"Se conciliarán {Formatos.Plural(m.Sum(c => c.Datos.Lineas.Count), "cobro", "cobros")} con " +
                  $"{Formatos.Plural(m.Sum(c => c.Datos.Facturas.Count), "factura", "facturas")}.";
        }
    }

    internal void Cambiado() => Avisar(nameof(Resumen), nameof(HayMarcados));
}

/// <summary>Un texto con su explicación (para las listas de lo que no cruza y de las reservas sin cobro).</summary>
public sealed record TextoPlataformaN43(string Texto, string Detalle);

/// <summary>Un cruce del diálogo: sus cobros y sus facturas, marcado o no.</summary>
public sealed class CrucePlataformaN43VM : ModeloVista
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly CobrosPlataformasN43VM _padre;
    private bool _marcado = true;

    internal CrucePlataformaN43VM(CobrosPlataformasN43VM padre, CrucePlataforma datos, PropuestaCobrosPlataformas p)
    {
        _padre = padre;
        Datos = datos;
        var lineas = datos.Lineas.Select(id => p.Lineas[id]).ToList();
        var facturas = datos.Facturas.Select(id => p.Facturas[id]).ToList();
        var tipo = datos.Tipo switch
        {
            TipoCrucePlataforma.Simple => "un cobro, una reserva",
            TipoCrucePlataforma.Agrupado => $"un cobro de {facturas.Count} reservas",
            TipoCrucePlataforma.Fraccionado => $"una reserva cobrada en {lineas.Count} trozos",
            _ => $"{lineas.Count} cobros de {facturas.Count} reservas",
        };
        Titulo = $"{lineas[0].Plataforma} · {tipo}";
        Cobros = lineas.Select(ServicioCobrosPlataformasN43.Describir).ToList();
        Facturas = facturas.Select(ServicioCobrosPlataformasN43.Describir).ToList();
        var cobrado = lineas.Sum(l => l.Importe);
        var esperado = facturas.Sum(f => f.Esperado);
        Ayuda = $"Cobrado {cobrado.ToString("N2", Es)} €, esperado {esperado.ToString("N2", Es)} € (total {facturas.Sum(f => f.Total).ToString("N2", Es)} € " +
                $"menos comisión y cargos {(facturas.Sum(f => f.Total) - esperado).ToString("N2", Es)} €). Un clic lo marca o desmarca.";
    }

    public CrucePlataforma Datos { get; }
    public string Titulo { get; }
    public IReadOnlyList<string> Cobros { get; }
    public IReadOnlyList<string> Facturas { get; }
    public string Ayuda { get; }

    public bool Marcado
    {
        get => _marcado;
        set { if (Fijar(ref _marcado, value)) _padre.Cambiado(); }
    }
}
