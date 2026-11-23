using System.Globalization;
using System.Windows.Input;
using ProConta.Activos.Financieros;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Activos.UI;

/// <summary>
/// La compra de participaciones de un activo financiero, común al alta por ISIN y a «Nueva compra…» (activos/activos-financieros 2026-11-23 20:32):
/// fecha (hoy por defecto), participaciones, precio del día pedido al proveedor y pasado a euros con el cambio del BCE, y coste total del lote, que se
/// propone como participaciones × precio en euros y se puede corregir. El precio y el cambio también se pueden escribir a mano.
/// </summary>
public sealed class FormularioCompraVM : ModeloVista
{
    private static readonly CultureInfo Es = Formatos.Cultura;
    private readonly ICotizaciones? _cotizaciones;
    private readonly Func<DateTime> _hoy;
    private ReferenciaCotizacion? _referencia;
    private PrecioEnEuros? _cotizacion;
    private string _fecha, _participaciones = "", _precio = "", _moneda = "EUR", _cambio = "1", _coste = "", _origen = "", _errorPrecio = "";
    private bool _costeEscrito, _consultando, _poniendo;
    private int _peticion;

    public FormularioCompraVM(ICotizaciones? cotizaciones, Func<DateTime>? hoy = null)
    {
        _cotizaciones = cotizaciones;
        _hoy = hoy ?? (() => DateTime.Today);
        _fecha = Formatos.Fecha(_hoy());
        ConsultarPrecioCommand = new ComandoDelegado(() => _ = ConsultarPrecioAsync(), () => !_consultando && PuedeConsultar);
    }

    public ICommand ConsultarPrecioCommand { get; }

    /// <summary>Con qué se pide el precio; al cambiar se vuelve a consultar.</summary>
    public ReferenciaCotizacion? Referencia
    {
        get => _referencia;
        set
        {
            if (_referencia == value) return;
            _referencia = value;
            if (value is { Moneda.Length: > 0 } r) Moneda = ServicioCotizaciones.AMonedaIso(0m, r.Moneda).Moneda;
            Avisar(nameof(PuedeConsultar));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool PuedeConsultar => _cotizaciones is not null && _referencia is { } r && (!string.IsNullOrWhiteSpace(r.TickerYahoo) || !string.IsNullOrWhiteSpace(r.TickerEodhd));

    public string Fecha { get => _fecha; set { if (Fijar(ref _fecha, value) && LeerFecha(value) is not null && PuedeConsultar) _ = ConsultarPrecioAsync(); } }
    public string Participaciones { get => _participaciones; set { if (Fijar(ref _participaciones, value)) Recalcular(); } }
    public string Precio { get => _precio; set { if (Fijar(ref _precio, value)) { if (!_poniendo) Manual(); Recalcular(); } } }
    public string Cambio { get => _cambio; set { if (Fijar(ref _cambio, value)) { if (!_poniendo) Manual(); Recalcular(); } } }
    public string Moneda { get => _moneda; private set { if (Fijar(ref _moneda, value)) Avisar(nameof(EnOtraMoneda), nameof(RotuloPrecio), nameof(AyudaCambio)); } }
    public bool EnOtraMoneda => !string.Equals(_moneda, "EUR", StringComparison.OrdinalIgnoreCase);
    public string RotuloPrecio => $"Precio por participación ({_moneda})";
    public string AyudaCambio => $"Cuántos {_moneda} vale un euro según el Banco Central Europeo ese día. Se rellena solo; puedes corregirlo.";

    /// <summary>Coste total del lote en euros; mientras no se escriba a mano, participaciones × precio en euros.</summary>
    public string CosteTotal { get => _coste; set { if (Fijar(ref _coste, value) && !_poniendo) _costeEscrito = true; } }

    public string PrecioEuros => PrecioEurValor is decimal p ? p.ToString("#,##0.00####", Es) + " €" : "";
    public string Origen { get => _origen; private set => Fijar(ref _origen, value); }
    public string ErrorPrecio { get => _errorPrecio; private set { if (Fijar(ref _errorPrecio, value)) Avisar(nameof(HayErrorPrecio)); } }
    public bool HayErrorPrecio => _errorPrecio.Length > 0;
    public bool Consultando { get => _consultando; private set { if (Fijar(ref _consultando, value)) CommandManager.InvalidateRequerySuggested(); } }

    /// <summary>El precio que se pidió al proveedor; null si el precio o el cambio se han escrito a mano.</summary>
    public PrecioEnEuros? Cotizacion => _cotizacion;

    private decimal? PrecioEurValor => LeerNumero(_precio) is decimal p && p > 0m && LeerNumero(_cambio) is decimal c && c > 0m ? ServicioCotizaciones.AEuros(p, c) : null;

    /// <summary>Pide el precio de la fecha al proveedor y lo pone con su cambio; si falla, lo dice y deja escribirlo a mano.</summary>
    public async Task ConsultarPrecioAsync()
    {
        if (_cotizaciones is null || _referencia is not { } r) return;
        if (LeerFecha(_fecha) is not DateTime fecha) { ErrorPrecio = "Escribe la fecha de compra como dd/mm/aaaa para consultar su precio."; return; }
        var peticion = ++_peticion;
        Consultando = true;
        Origen = "Consultando el precio…";
        try
        {
            var p = await _cotizaciones.PrecioEnEurosAsync(r, fecha);
            if (peticion != _peticion) return; // llegó otra petición después
            if (!p.EsCorrecto)
            {
                ErrorPrecio = "No he podido consultar el precio: " + string.Join(" ", p.Errores.Select(e => e.TextoOperador())) + " Puedes escribir el precio a mano.";
                Origen = "";
                return;
            }
            Poner(p.Valor);
        }
        catch (Exception ex)
        {
            if (peticion == _peticion) { ErrorPrecio = $"No he podido consultar el precio: {ex.Message} Puedes escribirlo a mano."; Origen = ""; }
        }
        finally { if (peticion == _peticion) Consultando = false; }
    }

    /// <summary>Pone un precio consultado (en pruebas, directamente).</summary>
    public void Poner(PrecioEnEuros p)
    {
        _poniendo = true;
        try
        {
            Moneda = p.Moneda;
            Precio = p.Precio.ToString("0.######", Es);
            Cambio = p.UnidadesPorEuro.ToString("0.######", Es);
            _cotizacion = p;
            ErrorPrecio = "";
            Origen = $"{p.Origen} del {Formatos.Fecha(p.FechaCotizacion)}" +
                     (p.FechaCotizacion != LeerFecha(_fecha) ? " (último día con cotización)" : "") +
                     (EnOtraMoneda ? $"; cambio del BCE del {Formatos.Fecha(p.FechaCambio)}" : "") + ". Es un coste orientativo: corrige el coste total cuando el bróker te confirme el importe.";
        }
        finally { _poniendo = false; }
        Recalcular();
    }

    private void Manual()
    {
        if (_cotizacion is null) return;
        _cotizacion = null;
        Origen = "Precio escrito a mano.";
    }

    private void Recalcular()
    {
        Avisar(nameof(PrecioEuros));
        if (_costeEscrito) return;
        _poniendo = true;
        try
        {
            CosteTotal = LeerNumero(_participaciones) is decimal n && n > 0m && PrecioEurValor is decimal p ? Formatos.Numero(CompraInversion.CostePropuesto(n, p)) : "";
        }
        finally { _poniendo = false; }
    }

    /// <summary>Errores de lo escrito, con qué hacer.</summary>
    public IReadOnlyList<string> Errores()
    {
        var e = new List<string>();
        if (LeerFecha(_fecha) is null) e.Add("Escribe la fecha de compra como dd/mm/aaaa (por ejemplo 20/11/2026).");
        if (_participaciones.Trim().Length > 0 && LeerNumero(_participaciones) is null) e.Add("Escribe las participaciones como un número (por ejemplo 12,345).");
        if (_precio.Trim().Length > 0 && LeerNumero(_precio) is null) e.Add("Escribe el precio como un número (por ejemplo 98,4512).");
        if (EnOtraMoneda && LeerNumero(_cambio) is null) e.Add($"Escribe el cambio como un número: cuántos {_moneda} vale un euro (por ejemplo 1,0532).");
        if (_coste.Trim().Length > 0 && FichaActivoVM.Importe(_coste) is null) e.Add("Escribe el coste total del lote como un importe (por ejemplo 1.250,40).");
        if (e.Count == 0 && Construir() is { } c) e.AddRange(c.Validar(_hoy()));
        return e;
    }

    /// <summary>La compra escrita; null si la fecha no se lee.</summary>
    public CompraInversion? Construir() => LeerFecha(_fecha) is DateTime f
        ? new CompraInversion(f, LeerNumero(_participaciones) ?? 0m, LeerNumero(_precio) ?? 0m, _moneda, EnOtraMoneda ? LeerNumero(_cambio) ?? 0m : 1m,
            FichaActivoVM.Importe(_coste) ?? 0m, _cotizacion)
        : null;

    internal static DateTime? LeerFecha(string? t) => FichaActivoVM.Fecha(t);

    /// <summary>
    /// Un número con hasta seis decimales: la coma es el decimal y el punto, separador de miles; sin coma, un único punto seguido de menos de tres
    /// cifras también es el decimal (teclado numérico). Null si no se lee.
    /// </summary>
    public static decimal? LeerNumero(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Replace("€", "").Replace(" ", "").Replace(" ", "").Trim();
        if (!t.Contains(',') && t.Count(c => c == '.') == 1 && t.Length - t.IndexOf('.') - 1 != 3) t = t.Replace('.', ',');
        return decimal.TryParse(t, NumberStyles.Number, Es, out var v) ? Math.Round(v, 6, MidpointRounding.AwayFromZero) : null;
    }
}
