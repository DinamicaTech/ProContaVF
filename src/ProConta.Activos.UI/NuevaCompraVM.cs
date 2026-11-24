using System.Windows.Input;
using ProConta.Activos.Financieros;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Modelos;

namespace ProConta.Activos.UI;

/// <summary>
/// «Nueva compra…» y «Vender…» de un activo financiero (la venta, activos/activos-financieros 2026-11-24 09:14: participaciones, fecha e importe total
/// opcional).
/// «Nueva compra…» de un activo financiero (activos/activos-financieros 2026-11-23 20:32): sin ISIN ni búsqueda, solo la fecha (hoy por defecto) y las
/// participaciones; el precio del día se pide solo y el coste del lote se puede corregir. No graba asientos.
/// </summary>
public sealed class NuevaCompraVM : ModeloVista
{
    private readonly ContextoActivos _ctx;
    private readonly int _idActivo;
    private string _errores = "";
    private bool _ocupado;

    public NuevaCompraVM(ContextoActivos ctx, int idActivo, string codigo, InversionDeActivo inversion, bool esVenta = false)
    {
        _ctx = ctx;
        _idActivo = idActivo;
        Codigo = codigo;
        Inversion = inversion;
        EsVenta = esVenta;
        Compra = new FormularioCompraVM(ctx.Cotizaciones) { Referencia = inversion.Instrumento.Referencia, EsVenta = esVenta };
        GuardarCommand = new ComandoDelegado(() => _ = GuardarAsync(), () => !_ocupado);
    }

    public string Codigo { get; }
    public InversionDeActivo Inversion { get; }
    public FormularioCompraVM Compra { get; }
    public ICommand GuardarCommand { get; }

    public bool EsVenta { get; }
    public string Titulo => EsVenta ? $"Venta de {Codigo}" : $"Nueva compra de {Codigo}";
    public string TextoGuardar => EsVenta ? "Guardar la venta" : "Guardar la compra";
    public string Cabecera => $"{Inversion.Instrumento.Nombre} · ISIN {Inversion.Instrumento.Isin}" +
                              (Inversion.Instrumento.TickerYahoo is { } t ? $" · {t}" : "") + $" · cotiza en {Inversion.Instrumento.Moneda}";
    public string Situacion => $"Ahora tiene {Inversion.Participaciones.ToString("#,##0.######", ProConta.UI.Base.Formato.Formatos.Cultura)} participaciones " +
                               $"con un coste de {ProConta.UI.Base.Formato.Formatos.Importe(Inversion.Coste)}.";
    public bool SinSimbolo => !Compra.PuedeConsultar;

    public string Errores { get => _errores; private set { if (Fijar(ref _errores, value)) Avisar(nameof(HayErrores)); } }
    public bool HayErrores => _errores.Length > 0;
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    /// <summary>Lo lanza el VM cuando la compra se ha guardado: la ventana se cierra aceptando.</summary>
    public event EventHandler? Guardada;

    public int? IdOperacion { get; private set; }

    /// <summary>Pide el precio de hoy al abrir.</summary>
    public Task CargarAsync() => Compra.PuedeConsultar ? Compra.ConsultarPrecioAsync() : Task.CompletedTask;

    public async Task GuardarAsync()
    {
        var errores = Compra.Errores();
        if (errores.Count > 0 || Compra.Construir() is not { } compra) { Errores = string.Join(Environment.NewLine, errores.Select(e => "· " + e)); return; }
        Ocupado = true;
        try
        {
            var r = EsVenta ? await _ctx.Servicio.VenderAsync(_ctx.Trabajo, _idActivo, compra, Compra.ImporteEscrito)
                : await _ctx.Servicio.NuevaCompraAsync(_ctx.Trabajo, _idActivo, compra);
            if (!r.EsCorrecto) { Errores = string.Join(Environment.NewLine, r.Errores.Select(e => "· " + e.TextoOperador())); return; }
            IdOperacion = r.Valor;
            Guardada?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Errores = $"· No se ha guardado la {(EsVenta ? "venta" : "compra")}: {ex.Message} No se ha grabado nada; vuelve a pulsar «{TextoGuardar}»."; }
        finally { Ocupado = false; }
    }
}
