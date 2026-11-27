using ProConta.Activos.Financieros;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Formato;

namespace ProConta.Activos.UI;

/// <summary>
/// Análisis de un activo financiero dentro de su ficha (ergonomía de activos 2026-11-26, decisión de Ronald: un solo sitio por activo): cifras de la
/// posición en la cabecera y los apartados Cotización, Lotes y Proyección. Se enseña primero con lo guardado y después se actualizan sus cotizaciones.
/// </summary>
public sealed partial class FichaActivoVM
{
    private bool _primeraCarga = true;
    private AnalisisActivoVM? _analisis;
    private string _estadoCotizaciones = "";
    private IReadOnlyList<string> _avisosCotizaciones = [];
    private CancellationTokenSource? _cancelacionCotizaciones;

    /// <summary>El análisis del activo financiero (null en los demás o mientras se lee).</summary>
    public AnalisisActivoVM? Analisis { get => _analisis; private set { if (Fijar(ref _analisis, value)) Avisar(nameof(VerCotizacion), nameof(MetaFinanciero)); } }

    /// <summary>Apartados Cotización y Proyección: solo si el activo financiero tiene cotizaciones.</summary>
    public bool VerCotizacion => EsFinanciero && _analisis is { Cotiza: true };

    /// <summary>Apartados que no tiene un activo financiero (Cuenta de resultados, Facturas, Ventas y Amortización).</summary>
    public bool VerNoFinanciero => Propio && !EsFinanciero;

    /// <summary>Clase, ISIN, símbolo, bolsa y moneda, para la cabecera.</summary>
    public string MetaFinanciero
    {
        get
        {
            if (_analisis is not { } a) return "";
            var i = a.Activo.Posicion.Instrumento;
            return string.Join(" · ", new[]
            {
                a.Clase, i.Isin.Length > 0 ? $"ISIN {i.Isin}" : null, i.TickerYahoo ?? i.TickerEodhd, i.Bolsa,
                i.Moneda.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? null : $"cotiza en {i.Moneda}",
            }.Where(t => !string.IsNullOrEmpty(t)));
        }
    }

    /// <summary>De cuándo son las cotizaciones, o que se están actualizando.</summary>
    public string EstadoCotizaciones { get => _estadoCotizaciones; private set => Fijar(ref _estadoCotizaciones, value); }

    public IReadOnlyList<string> AvisosCotizaciones
    {
        get => _avisosCotizaciones;
        private set { _avisosCotizaciones = value; Avisar(nameof(AvisosCotizaciones), nameof(HayAvisosCotizaciones), nameof(TextoAvisosCotizaciones)); }
    }

    public bool HayAvisosCotizaciones => _avisosCotizaciones.Count > 0;
    public string TextoAvisosCotizaciones => string.Join(Environment.NewLine, _avisosCotizaciones);

    /// <summary>Lee la posición con lo guardado (<paramref name="conRed"/> false) o con las cotizaciones al día.</summary>
    private async Task CargarAnalisisAsync(bool conRed, CancellationToken cancelacion = default)
    {
        var periodo = _analisis?.Periodo;
        if (_modulo.Contexto.Cartera is { } cartera)
        {
            var (activo, avisos) = await cartera.CargarActivoAsync(IdActivo, conRed, cancelacion);
            if (conRed) AvisosCotizaciones = avisos;
            Analisis = activo is null ? null : new AnalisisActivoVM(activo, periodo);
            return;
        }
        // Sin servicio de cotizaciones: lotes y operaciones a coste.
        var inv = await _modulo.Contexto.Servicio.InversionAsync(IdActivo);
        Analisis = inv is null ? null : new AnalisisActivoVM(new ActivoDeCartera(IdActivo, Codigo, Nombre, _ficha?.DeBaja == true ? DateTime.Today : null,
            PosicionFinanciera.Crear(IdActivo, Codigo, inv, new SeriePrecios([]), null, DateTime.Today)), periodo);
    }

    /// <summary>Completa el histórico y pide el precio de hoy; al terminar, vuelve a pintar las cifras y el análisis.</summary>
    private async Task ActualizarCotizacionesAsync()
    {
        if (_modulo.Contexto.Cartera is null) { EstadoCotizaciones = "Sin servicio de cotizaciones: valorado a coste"; return; }
        _cancelacionCotizaciones?.Cancel();
        _cancelacionCotizaciones = new CancellationTokenSource();
        var ct = _cancelacionCotizaciones.Token;
        EstadoCotizaciones = "Actualizando las cotizaciones…";
        try
        {
            await CargarAnalisisAsync(conRed: true, ct);
            if (ct.IsCancellationRequested) return;
            EstadoCotizaciones = $"Cotizaciones actualizadas a las {DateTime.Now:HH:mm}";
            Indicadores = IndicadoresFinancieros();
            Avisar(nameof(Indicadores));
            // Con cotizaciones nuevas puede aparecer Cotización (un activo recién dado de alta).
            var a = Normalizar(_apartado);
            if (a != _apartado) Apartado = a;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            EstadoCotizaciones = "";
            AvisosCotizaciones = [$"No he podido actualizar las cotizaciones: {ex.Message} Se ven las guardadas; pulsa Actualizar (F5) para volver a intentarlo."];
        }
    }

    /// <summary>Las cifras de la posición (participaciones, coste, valor, plusvalía, rentabilidad y resultado de ventas) como indicadores de la cabecera.</summary>
    private IReadOnlyList<IndicadorFicha> IndicadoresFinancieros() => _analisis is null
        ? [new IndicadorFicha("Coste", Formatos.Importe(_ficha?.Datos.Coste ?? 0m), "sin instrumento: dalo de alta por ISIN")]
        : _analisis.Cifras.Select(c => new IndicadorFicha(c.Titulo, c.Valor, c.Nota, c.Tono == "Malo", c.Tono == "Bueno", c.Ayuda)).ToList();

    private void CancelarCotizaciones() => _cancelacionCotizaciones?.Cancel();
}
