using System.Windows.Input;
using ProConta.Activos.Financieros;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;

namespace ProConta.Activos.UI;

/// <summary>Una compra (o venta) del apartado Operaciones; el doble clic cambia el coste del lote.</summary>
public sealed record FilaOperacionVM(OperacionDeInversion Operacion, string Fecha, string Tipo, string Participaciones, string Precio, string Cambio, string PrecioEur,
    string Coste, string Notas, ICommand? AbrirCommand) : IFilaAbrible
{
    public string Ayuda => Operacion.EsCompra ? "Doble clic o menú contextual: cambiar el coste total del lote con el importe que confirma el bróker" : "";
}

/// <summary>
/// Apartado «Operaciones» de la ficha de un activo financiero (activos/activos-financieros 2026-11-23 20:32): el instrumento (ISIN, símbolos, bolsa y
/// moneda) y sus compras con el precio que dio el proveedor y el coste del lote, que se cambia con el importe que confirma el bróker. «Nueva compra…»
/// en la cabecera del apartado.
/// </summary>
public sealed partial class FichaActivoVM
{
    private InversionDeActivo? _inversion;
    private bool _operacionesLeidas;
    private FilaOperacionVM? _operacionElegida;
    private ICommand? _cambiarCosteCommand, _nuevaCompraCommand;

    /// <summary>Si el activo es de tipo «Inversión financiera»: tiene el apartado Operaciones y no se amortiza.</summary>
    public bool EsFinanciero => TiposActivoPgc.EsInversion(_ficha?.Datos.CodigoTipo ?? "");

    public IReadOnlyList<FilaOperacionVM> FilasOperaciones { get; private set; } = [];
    public string TextoInstrumento { get; private set; } = "";
    public string TotalOperaciones { get; private set; } = "";

    public FilaOperacionVM? OperacionElegida { get => _operacionElegida; set { _operacionElegida = value; Avisar(nameof(OperacionElegida)); CommandManager.InvalidateRequerySuggested(); } }

    public ICommand CambiarCosteCommand => _cambiarCosteCommand ??= new ComandoDelegado(() => { if (_operacionElegida is { } f) _ = CambiarCosteAsync(f.Operacion); },
        () => !_ocupado && _operacionElegida is { Operacion.EsCompra: true });

    public ICommand NuevaCompraCommand => _nuevaCompraCommand ??= new ComandoDelegado(() => _ = _modulo.NuevaCompraAsync(IdActivo, Codigo),
        () => !_ocupado && EsFinanciero && _ficha is { DeBaja: false });

    private async Task CargarOperacionesAsync()
    {
        Ocupado = true;
        try { _inversion = await _modulo.Contexto.Servicio.InversionAsync(IdActivo); }
        finally { Ocupado = false; }
        _operacionesLeidas = true;
        if (_inversion is not { } inv)
        {
            FilasOperaciones = [];
            TextoInstrumento = "Este activo es de tipo «Inversión financiera» pero no tiene instrumento: se dio de alta antes del alta por ISIN.";
            TotalOperaciones = "";
        }
        else
        {
            var i = inv.Instrumento;
            TextoInstrumento = $"{i.Nombre} · ISIN {i.Isin} · " + (i.TickerYahoo is { } t ? $"Yahoo: {t}" : "sin símbolo de Yahoo") +
                               (i.TickerEodhd is { } e ? $" · EODHD: {e}" : "") + (i.Bolsa is { } b ? $" · {b}" : "") + $" · cotiza en {i.Moneda}" +
                               (i.Origen == FuenteCotizacion.Manual ? " · datos escritos a mano" : "");
            FilasOperaciones = inv.Operaciones.Select(o => new FilaOperacionVM(o, Formatos.Fecha(o.Fecha), o.EsCompra ? "Compra" : "Venta",
                o.Participaciones.ToString("#,##0.######", Es), o.PrecioUnitario.ToString("#,##0.00####", Es) + " " + o.Moneda,
                o.Moneda == "EUR" ? "" : o.UnidadesPorEuro.ToString("0.######", Es), o.PrecioEurEfectivo.ToString("#,##0.00####", Es) + " €", Formatos.Importe(o.ImporteTotalEur),
                o.Notas ?? "", o.EsCompra ? new ComandoDelegado(() => _ = CambiarCosteAsync(o)) : null)).ToList();
            TotalOperaciones = $"{Formatos.Plural(inv.Operaciones.Count, "operación", "operaciones")} · {inv.Participaciones.ToString("#,##0.######", Es)} participaciones · " +
                               $"coste {Formatos.Importe(inv.Coste)}";
        }
        Avisar(nameof(FilasOperaciones), nameof(TextoInstrumento), nameof(TotalOperaciones));
    }

    /// <summary>Pide el coste total del lote (el que confirma el bróker) y lo guarda; el coste del activo se recalcula.</summary>
    private async Task CambiarCosteAsync(OperacionDeInversion o)
    {
        var importe = _modulo.Dialogos.PedirImporte($"Coste del lote del {Formatos.Fecha(o.Fecha)}",
            $"Escribe el importe total en euros que te ha confirmado el bróker por las {o.Participaciones.ToString("#,##0.######", Es)} participaciones " +
            $"compradas el {Formatos.Fecha(o.Fecha)} (con sus comisiones). Ahora es {Formatos.Importe(o.ImporteTotalEur)}, calculado con el precio de cierre.", o.ImporteTotalEur);
        if (importe is not decimal v || v == o.ImporteTotalEur) return;
        try
        {
            var r = await _modulo.Contexto.Servicio.CambiarCosteLoteAsync(_modulo.Contexto.Trabajo, o.IdOperacion, v);
            if (!r.EsCorrecto) { _modulo.Dialogos.Error("No se ha cambiado el coste", string.Join(Environment.NewLine, r.Errores.Select(e => e.TextoOperador()))); return; }
            _modulo.Contexto.Avisar($"Coste del lote del {Formatos.Fecha(o.Fecha)}: {Formatos.Importe(v)}.");
            _modulo.Cambiado();
            await RecargarTrasCompraAsync();
        }
        catch (Exception ex) { _modulo.Dialogos.Error("No se ha cambiado el coste", $"{ex.Message} No se ha grabado nada; vuelve a intentarlo."); }
    }

    /// <summary>Tras una compra o un cambio de coste: vuelve a leer la ficha (coste y fecha de compra) y las operaciones, sin tocar lo que se esté editando.</summary>
    internal async Task RecargarTrasCompraAsync()
    {
        if (TieneCambios) { _operacionesLeidas = false; if (_apartado == ApartadoFicha.Operaciones) await CargarOperacionesAsync(); return; }
        _cuenta = null; _operacionesLeidas = false;
        await CargarAsync();
    }
}
