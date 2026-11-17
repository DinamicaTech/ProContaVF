using System.Globalization;
using System.Windows.Input;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;

namespace ProConta.Activos.UI;

/// <summary>Fila que se abre con doble clic.</summary>
public interface IFilaAbrible
{
    ICommand? AbrirCommand { get; }
}

/// <summary>Opción del filtro por ejercicio de los apartados Facturas y Ventas (null = todos).</summary>
public sealed record OpcionEjercicioFiltro(int? Ejercicio, string Texto)
{
    public override string ToString() => Texto;
}

/// <summary>Una factura de compra del apartado Facturas.</summary>
public sealed record FilaCompraActivoVM(string Fecha, string Numero, string Proveedor, string Concepto, string Base, string Iva, string Total, string Estado,
    ICommand? AbrirCommand) : IFilaAbrible
{
    public string Ayuda => AbrirCommand is null ? "" : "Doble clic: abre la factura de compra en Compras › Facturas recibidas";
}

/// <summary>Una factura programada del apartado Ventas.</summary>
public sealed record FilaProgramadaActivoVM(string Rotulo, string Cliente, string Periodicidad, string Proxima, string Importe, string Activa, ICommand? AbrirCommand) : IFilaAbrible
{
    public string Ayuda => AbrirCommand is null ? "" : $"Doble clic: abre la programada {Rotulo}";
}

/// <summary>Una factura emitida o borrador del apartado Ventas.</summary>
public sealed record FilaVentaActivoVM(string Fecha, string Numero, string Cliente, string BaseActivo, string Total, string Estado, ICommand? AbrirCommand) : IFilaAbrible
{
    public string Ayuda => AbrirCommand is null ? "" : "Doble clic: abre la factura";
}

/// <summary>
/// Apartados «Facturas» y «Ventas» de la ficha (activos 2026-11-17 20:35): solo consulta, con un filtro por ejercicio común (por defecto el de trabajo,
/// con «Todos») y sus totales; el doble clic abre la factura de compra, la de venta o la programada.
/// </summary>
public sealed partial class FichaActivoVM
{
    private OpcionEjercicioFiltro? _ejercicioEnlaces;
    private IReadOnlyList<CompraDeActivo>? _compras;
    private VentasDeActivo? _ventas;

    /// <summary>«Todos» y los ejercicios de la sociedad, del más reciente al más antiguo.</summary>
    public IReadOnlyList<OpcionEjercicioFiltro> EjerciciosEnlaces =>
        [new(null, "Todos"), .. _ejercicios.Select(e => new OpcionEjercicioFiltro(e.Ejercicio, e.Ejercicio.ToString(CultureInfo.InvariantCulture)))];

    public OpcionEjercicioFiltro EjercicioEnlaces
    {
        get => _ejercicioEnlaces ?? EjerciciosEnlaces.FirstOrDefault(e => e.Ejercicio == _modulo.Contexto.Trabajo.Ejercicio) ?? EjerciciosEnlaces[0];
        set
        {
            if (value is null || value == EjercicioEnlaces) return;
            _ejercicioEnlaces = value;
            Avisar(nameof(EjercicioEnlaces));
            _compras = null; _ventas = null;
            _ = RecargarEnlacesAsync();
        }
    }

    public IReadOnlyList<FilaCompraActivoVM> FilasCompras { get; private set; } = [];
    public string TotalCompras { get; private set; } = "";
    public bool SinCompras => _compras is { Count: 0 };
    public IReadOnlyList<FilaProgramadaActivoVM> FilasProgramadas { get; private set; } = [];
    public string TotalProgramadas { get; private set; } = "";
    public bool SinProgramadas => _ventas is { Programadas.Count: 0 };
    public IReadOnlyList<FilaVentaActivoVM> FilasVentas { get; private set; } = [];
    public string TotalVentas { get; private set; } = "";
    public bool SinVentas => _ventas is { Facturas.Count: 0 };

    public string ExplicacionCompras => _ficha is { Datos.IdCentroCoste: not null }
        ? "Facturas de compra cuya cuenta de gasto es una subcuenta de este activo o cuyo centro de coste es el suyo (si comparte el centro con otro activo, solo por subcuenta)."
        : "Facturas de compra cuya cuenta de gasto es una subcuenta de este activo (no tiene centro de coste).";

    private string TextoEjercicio => EjercicioEnlaces.Ejercicio is int a ? $"en {a}" : "en todos los ejercicios";

    private async Task RecargarEnlacesAsync()
    {
        try
        {
            if (_apartado == ApartadoFicha.Facturas) await CargarComprasAsync();
            else if (_apartado == ApartadoFicha.Ventas) await CargarVentasAsync();
        }
        catch (Exception ex) { _modulo.Dialogos.Error("No he podido leer este apartado", $"{ex.Message} Pulsa Actualizar (F5) para volver a intentarlo."); }
    }

    private async Task CargarComprasAsync()
    {
        Ocupado = true;
        try { _compras = await _modulo.Contexto.Servicio.ComprasAsync(_modulo.Contexto.Trabajo.IdSociedad, IdActivo, EjercicioEnlaces.Ejercicio); }
        finally { Ocupado = false; }
        var abrir = _modulo.Contexto.AbrirFacturaCompra;
        FilasCompras = _compras.Select(c => new FilaCompraActivoVM(c.Fecha is { } f ? Formatos.Fecha(f) : "", c.Numero, c.Proveedor, c.Concepto, Formatos.Importe(c.Base),
            Formatos.Importe(c.Iva), Formatos.Importe(c.Total), c.Estado, abrir is null ? null : new ComandoDelegado(() => abrir(c.IdRegistro)))).ToList();
        TotalCompras = _compras.Count == 0 ? $"No hay facturas de compra de este activo {TextoEjercicio}."
            : $"{(_compras.Count == 1 ? "1 factura" : $"{_compras.Count} facturas")} {TextoEjercicio} · base {Formatos.Importe(_compras.Sum(c => c.Base))} · " +
              $"IVA {Formatos.Importe(_compras.Sum(c => c.Iva))} · total {Formatos.Importe(_compras.Sum(c => c.Total))}";
        Avisar(nameof(FilasCompras), nameof(TotalCompras), nameof(SinCompras), nameof(ExplicacionCompras), nameof(EjerciciosEnlaces), nameof(EjercicioEnlaces));
    }

    private async Task CargarVentasAsync()
    {
        Ocupado = true;
        try { _ventas = await _modulo.Contexto.Servicio.VentasAsync(_modulo.Contexto.Trabajo.IdSociedad, IdActivo, EjercicioEnlaces.Ejercicio); }
        finally { Ocupado = false; }
        var abrirProgramada = _modulo.Contexto.AbrirProgramada;
        var abrirFactura = _modulo.Contexto.AbrirFacturaVenta;
        FilasProgramadas = _ventas.Programadas.Select(p => new FilaProgramadaActivoVM(p.Rotulo, p.Cliente, p.Periodicidad,
            p.ProximaEmision is { } f ? Formatos.Fecha(f) : "—", Formatos.Importe(p.Importe), p.TextoActiva,
            abrirProgramada is null ? null : new ComandoDelegado(() => abrirProgramada(p.IdFacturaProgramada)))).ToList();
        TotalProgramadas = _ventas.Programadas.Count == 0 ? $"No hay facturas programadas con líneas de este activo {TextoEjercicio}."
            : $"{(_ventas.Programadas.Count == 1 ? "1 programada" : $"{_ventas.Programadas.Count} programadas")} · importe de sus líneas del activo por factura: " +
              $"{Formatos.Importe(_ventas.Programadas.Sum(p => p.Importe))} (las activas: {Formatos.Importe(_ventas.Programadas.Where(p => p.Activa).Sum(p => p.Importe))})";
        FilasVentas = _ventas.Facturas.Select(v => new FilaVentaActivoVM(v.Fecha is { } f ? Formatos.Fecha(f) : "", v.Numero, v.Cliente, Formatos.Importe(v.BaseActivo),
            Formatos.Importe(v.Total), v.Estado, abrirFactura is null ? null : new ComandoDelegado(() => abrirFactura(v.IdFactura)))).ToList();
        TotalVentas = _ventas.Facturas.Count == 0 ? $"No hay facturas con líneas de este activo {TextoEjercicio}."
            : $"{(_ventas.Facturas.Count == 1 ? "1 factura" : $"{_ventas.Facturas.Count} facturas")} {TextoEjercicio} · base de las líneas del activo " +
              $"{Formatos.Importe(_ventas.Facturas.Sum(v => v.BaseActivo))} · total de las facturas {Formatos.Importe(_ventas.Facturas.Sum(v => v.Total))}";
        Avisar(nameof(FilasProgramadas), nameof(TotalProgramadas), nameof(SinProgramadas), nameof(FilasVentas), nameof(TotalVentas), nameof(SinVentas),
            nameof(EjerciciosEnlaces), nameof(EjercicioEnlaces));
    }
}
