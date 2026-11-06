using System.Collections.ObjectModel;
using System.Globalization;
using ProConta.Compras.Facturas;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Compras.UI.VistaModelos;

/// <summary>Elemento de una lista desplegable de la ficha (valor nulo: «sin indicar»).</summary>
public sealed record OpcionFicha<T>(string Texto, T? Valor)
{
    public override string ToString() => Texto;
}

/// <summary>Confianza de un campo reconocido: porcentaje, color y motivo; «Manual» si el operador lo ha cambiado.</summary>
public sealed record MarcaConfianza(string Texto, string Nivel, string Motivo)
{
    public static MarcaConfianza Ninguna { get; } = new("", "", "");
    public static MarcaConfianza Manual { get; } = new("Manual", "manual", "Lo has escrito o corregido tú.");
    public bool Visible => Texto.Length > 0;

    /// <summary>Alta desde 0,9 (el umbral de aceptación), media desde 0,6 y baja por debajo.</summary>
    public static MarcaConfianza De(decimal? confianza, string campo, IReadOnlyList<string> avisos, bool plantilla = false)
    {
        if (confianza is not { } c) return Ninguna;
        if (plantilla)
            return new($"{Math.Round(c * 100m):0} % plantilla", "alta",
                $"Leído con la plantilla que se aprendió al guardar otra factura de este proveedor con el mismo formato ({campo}).");
        var nivel = c >= 0.9m ? "alta" : c >= 0.6m ? "media" : "baja";
        var motivo = c == 0m ? $"No he encontrado {campo} en el PDF." : $"Confianza del reconocimiento en {campo}: {Math.Round(c * 100m):0} %.";
        var relacionados = avisos.Where(a => a.Contains(campo, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (relacionados.Count > 0) motivo += " " + string.Join(" ", relacionados);
        return new($"{Math.Round(c * 100m):0} %", nivel, motivo);
    }
}

/// <summary>
/// Un tipo de IVA de la ficha: el tipo se elige de la lista del ejercicio (compras/pantallas 2026-11-06 12:10); base y
/// cuota se ven en moneda (1.234,56 €). Con inversión del sujeto pasivo la cuota es 0 y no se escribe.
/// </summary>
public sealed class DesgloseVM : ModeloVista
{
    private string _tipo = "", _base = "", _cuota = "";
    private readonly Action _cambio;
    private readonly Func<bool> _sinCuota;

    public DesgloseVM(Action cambio, decimal? tipo = null, decimal? @base = null, decimal? cuota = null, IReadOnlyList<decimal>? tipos = null, Func<bool>? sinCuota = null)
    {
        _cambio = cambio;
        _sinCuota = sinCuota ?? (() => false);
        _tipo = tipo is { } t ? Porcentaje(t) : "";
        _base = @base is { } b ? Formatos.Importe(b) : "";
        _cuota = cuota is { } c ? Formatos.Importe(c) : "";
        var lista = (tipos is { Count: > 0 } ? tipos : [21m, 10m, 4m, 0m]).Append(0m).Distinct().OrderByDescending(x => x).ToList();
        if (tipo is { } actual && !lista.Contains(actual)) lista.Add(actual);
        Tipos = lista.Select(x => new OpcionFicha<decimal?>(Porcentaje(x), x)).ToList();
    }

    private static string Porcentaje(decimal t) => t.ToString("0.##", Formatos.Cultura) + " %";

    /// <summary>0 % y los tipos de IVA del ejercicio (y el de la factura si no es uno de ellos).</summary>
    public IReadOnlyList<OpcionFicha<decimal?>> Tipos { get; }
    public OpcionFicha<decimal?>? TipoElegido
    {
        get => Tipos.FirstOrDefault(o => o.Valor == TipoValor);
        set { if (value?.Valor is { } v) Tipo = Porcentaje(v); }
    }

    public string Tipo { get => _tipo; set { if (Fijar(ref _tipo, value ?? "")) { Avisar(nameof(TipoElegido)); ProponerCuota(); _cambio(); } } }
    public string Base { get => _base; set { if (Fijar(ref _base, Importe(value))) { ProponerCuota(); _cambio(); } } }
    public string Cuota { get => _cuota; set { if (!_sinCuota() && Fijar(ref _cuota, Importe(value))) _cambio(); } }

    /// <summary>Lo escrito se vuelve a ver en moneda; si no es un importe, se deja tal cual para que se vea el error.</summary>
    internal static string Importe(string? texto) => Texto.LeerDecimal(texto) is { } v ? Formatos.Importe(v) : (texto ?? "").Trim();

    /// <summary>Con inversión del sujeto pasivo: la cuota pasa a 0.</summary>
    internal void QuitarCuota()
    {
        if (CuotaValor is null or 0m) return;
        _cuota = Formatos.Importe(0m);
        Avisar(nameof(Cuota));
    }

    public decimal? TipoValor => Texto.LeerDecimal(_tipo);
    public decimal? BaseValor => Texto.LeerDecimal(_base);
    public decimal? CuotaValor => Texto.LeerDecimal(_cuota);

    /// <summary>Con tipo y base, la cuota se propone redondeada a céntimos (alejándose de cero) si estaba vacía.</summary>
    private void ProponerCuota()
    {
        if (_sinCuota()) return;
        if (_cuota.Length == 0 && TipoValor is { } t && BaseValor is { } b)
        {
            _cuota = Formatos.Importe(Math.Round(b * t / 100m, 2, MidpointRounding.AwayFromZero));
            Avisar(nameof(Cuota));
        }
    }
}

/// <summary>
/// Datos editables de la factura activa (compras/pantallas, prototipo versión 26): proveedor por NIF, factura,
/// imputación, importes por tipo de IVA (o un solo importe en importación), retención, suplido, moneda y notas, con la
/// confianza de cada campo reconocido. Los cambios se guardan al cambiar de factura, sin preguntar.
/// </summary>
public sealed class FichaCompraVM : ModeloVista
{
    private IReadOnlyList<ProveedorFicha> _proveedores;
    private string? _cuentaProveedorNueva;
    private ConfianzaCampos _confianza;
    private readonly HashSet<string> _manuales = new(StringComparer.Ordinal);
    private bool _cargando = true;
    private bool _tieneCambios;

    private string _nif = "", _numero = "", _fecha = "", _total = "", _porcentajeRetencion = "", _suplido = "", _divisa = "EUR", _totalDivisa = "";
    private string _notas = "", _importeUnico = "";
    private OpcionFicha<byte?> _formaPago;
    private bool _isp, _conRetencion;
    private ProveedorFicha? _proveedor;
    private OpcionFicha<OperacionCompra> _operacion;
    private OpcionFicha<ProductoCompra?> _producto;
    private OpcionFicha<ClaseRetencion?> _claseRetencion;
    private readonly IReadOnlyList<TipoRetencionCompra> _tiposRetencionCatalogo;
    private IReadOnlyList<OpcionFicha<TipoRetencionCompra?>> _tiposRetencion;
    private OpcionFicha<TipoRetencionCompra?> _tipoRetencion;
    private OpcionFicha<string> _cuentaGasto;
    private IReadOnlyList<OpcionFicha<string>> _cuentasGasto;
    private OpcionFicha<int?> _centroCoste;
    private OpcionFicha<int?> _rectifica;
    private readonly IReadOnlyList<decimal> _tiposIva;
    private readonly Dictionary<string, Compras.Reconocimiento.PuntoDocumento> _elegidos = new(StringComparer.Ordinal);

    internal FichaCompraVM(FacturaCompra factura, IReadOnlyList<ProveedorFicha> proveedores, IReadOnlyList<OpcionFicha<string>> cuentasGasto,
        IReadOnlyList<CentroCoste> centros, IReadOnlyList<FilaFacturaCompra> otras, bool soloLectura, ConfianzaCampos? confianza = null,
        IReadOnlyList<decimal>? tiposIva = null, IReadOnlyList<(byte Id, string Descripcion)>? formasPago = null,
        IReadOnlyList<TipoRetencionCompra>? tiposRetencion = null)
    {
        Factura = factura;
        _tiposIva = tiposIva ?? [];
        _proveedores = proveedores;
        SoloLectura = soloLectura || factura.Contabilizada;
        _confianza = confianza ?? ConfianzaCampos.DesdeLog(factura.Log);

        Operaciones = [new("Nacional", OperacionCompra.Nacional), new("Intracomunitaria", OperacionCompra.Intracomunitaria), new("Importación", OperacionCompra.Importacion)];
        Productos = [new("Sin indicar", null), new("Servicios", ProductoCompra.Servicios), new("Bienes", ProductoCompra.Bienes)];
        ClasesRetencion = [new("Profesionales", Facturas.ClaseRetencion.Profesionales), new("Arrendamientos", Facturas.ClaseRetencion.Arrendamientos)];
        var gasto = cuentasGasto.ToList();
        if (factura.CuentaGasto is { Length: > 0 } cg && gasto.All(c => c.Valor != cg)) gasto.Add(new($"{cg} · (no está en el plan del ejercicio)", cg));
        _cuentasGasto = [new("Sin cuenta de gasto", null), .. gasto];
        BuscarCuentaGastoCommand = new ComandoDelegado(p => _ = BuscarCuentaGastoAsync(p as string, diferir: false), _ => Editable);
        CentrosCoste = [new("Sin centro de coste", null), .. centros.Where(c => c.EstaActivo && c.EsImputable).OrderBy(c => c.RutaJerarquica, StringComparer.Ordinal)
            .Select(c => new OpcionFicha<int?>($"{c.Codigo} · {c.Nombre}", c.IdCentroCoste))];
        Rectificables = [new("No rectifica a ninguna", null), .. otras
            .Where(o => o.IdRegistro != factura.IdRegistro && (o.IdRegistro == factura.IdRegistroFacturaRectificada || MismoProveedor(factura, o)))
            .OrderByDescending(o => o.Fecha).Select(o => new OpcionFicha<int?>($"{(o.NumeroFactura.Length > 0 ? o.NumeroFactura : o.NombreFichero)} · {(o.Fecha is { } f ? Formatos.Fecha(f) : "sin fecha")} · {(o.ImporteTotal is { } t ? Formatos.Importe(t) : "")}", o.IdRegistro))];

        var formas = (formasPago ?? []).Select(x => new OpcionFicha<byte?>(x.Descripcion, x.Id)).ToList();
        if (factura.IdFormaPago is byte fpa && formas.All(x => x.Valor != fpa)) formas.Add(new($"Forma de pago {fpa} (ya no existe)", fpa));
        FormasPago = [new("Sin forma de pago", null), .. formas];

        _nif = factura.NifProveedor ?? "";
        _proveedor = proveedores.FirstOrDefault(p => p.IdProveedor == factura.IdProveedor) ?? BuscarPorNif(_nif);
        _numero = factura.NumeroFactura;
        _fecha = factura.Fecha is { } fe ? Formatos.Fecha(fe) : "";
        _operacion = Operaciones.First(o => o.Valor == factura.Operacion);
        _producto = Productos.First(p => p.Valor == factura.Producto);
        _isp = factura.InversionSujetoPasivo;
        _conRetencion = (factura.PorcentajeRetencion ?? 0m) != 0m || (factura.ImporteRetencion ?? 0m) != 0m;
        _porcentajeRetencion = factura.PorcentajeRetencion is { } pr && pr != 0 ? pr.ToString("0.##", Formatos.Cultura) : _conRetencion ? "" : "15";
        _claseRetencion = ClasesRetencion.FirstOrDefault(c => c.Valor == factura.ClaseRetencion) ?? ClasesRetencion[0];
        _tiposRetencionCatalogo = tiposRetencion ?? [];
        _tiposRetencion = OpcionesRetencion();
        _tipoRetencion = OpcionRetencionActual();
        _suplido = factura.Suplido is { } su && su != 0 ? Formatos.Importe(su) : "";
        _total = factura.ImporteTotal is { } t ? Formatos.Importe(t) : "";
        _divisa = factura.Divisa;
        _totalDivisa = factura.ImporteTotalDivisa is { } td ? Formatos.Numero(td) : "";
        _cuentaGasto = CuentasGasto.FirstOrDefault(c => c.Valor == factura.CuentaGasto) ?? CuentasGasto[0];
        _centroCoste = CentrosCoste.FirstOrDefault(c => c.Valor == factura.IdCentroCoste) ?? CentrosCoste[0];
        DarDeAltaProveedorCommand = new ComandoDelegado(_ => _ = DarDeAltaProveedor?.Invoke(this), _ => DarDeAltaProveedor is not null && PuedeDarDeAltaProveedor);
        AnadirTextoClaveCommand = new ComandoDelegado(_ => _ = AnadirTextoClave?.Invoke(this),
            _ => AnadirTextoClave is not null && (_cuentaGasto.Valor is not null || _centroCoste.Valor is not null));
        _rectifica = Rectificables.FirstOrDefault(r => r.Valor == factura.IdRegistroFacturaRectificada) ?? Rectificables[0];
        _formaPago = FormasPago.FirstOrDefault(x => x.Valor == factura.IdFormaPago) ?? FormasPago[0];
        _notas = factura.Notas ?? "";
        // Una factura editable sin forma de pago trae la del proveedor; se guarda con lo demás.
        if (!SoloLectura)
        {
            ProponerFormaPago();
            // Sin producto, el del proveedor o bienes (compras/pantallas 2026-11-06 13:36).
            if (_producto.Valor is null) _producto = Productos.First(p => p.Valor == (_proveedor?.ProductoDefecto ?? ProductoCompra.Bienes));
        }

        if (factura.Desgloses.Count > 0)
            foreach (var d in factura.Desgloses) Desgloses.Add(NuevoDesglose(d.TipoIva, d.BaseImponible, d.CuotaIva));
        else if (factura.BaseImponible is not null || factura.CuotaIva is not null)
            Desgloses.Add(NuevoDesglose(TipoDeducido(factura), factura.BaseImponible, factura.CuotaIva));
        if (Desgloses.Count == 0) Desgloses.Add(NuevoDesglose(TipoGeneral));
        _importeUnico = factura.BaseImponible is { } bi ? Formatos.Importe(bi) : "";
        _cargando = false;
    }

    /// <summary>Factura tal como se leyó (o se guardó por última vez).</summary>
    public FacturaCompra Factura { get; private set; }
    public bool SoloLectura { get; }
    public bool Editable => !SoloLectura;
    public bool TieneCambios { get => _tieneCambios; private set => Fijar(ref _tieneCambios, value); }

    public IReadOnlyList<OpcionFicha<OperacionCompra>> Operaciones { get; }
    public IReadOnlyList<OpcionFicha<ProductoCompra?>> Productos { get; }
    public IReadOnlyList<OpcionFicha<ClaseRetencion?>> ClasesRetencion { get; }
    /// <summary>Subcuentas del grupo 6 del ejercicio (y las que se eligen o crean con el buscador).</summary>
    public IReadOnlyList<OpcionFicha<string>> CuentasGasto => _cuentasGasto;
    public IReadOnlyList<OpcionFicha<int?>> CentrosCoste { get; }
    public IReadOnlyList<OpcionFicha<int?>> Rectificables { get; }
    public ObservableCollection<DesgloseVM> Desgloses { get; } = [];

    /// <summary>Monedas que se ofrecen; se puede escribir cualquier otro código ISO de tres letras.</summary>
    public IReadOnlyList<string> Monedas { get; } = ["EUR", "USD", "GBP", "CHF"];

    private DesgloseVM NuevoDesglose(decimal? tipo, decimal? @base = null, decimal? cuota = null) =>
        new(AlCambiarImportes, tipo, @base, cuota, _tiposIva, () => _isp);

    /// <summary>El tipo general del ejercicio para una línea nueva: el más alto de los configurados, o el 21 %.</summary>
    private decimal TipoGeneral => _tiposIva.Count > 0 ? _tiposIva.Max() : 21m;

    /// <summary>Las facturas que puede rectificar: del mismo proveedor (por su id, su NIF o, sin ninguno, su carpeta).</summary>
    private static bool MismoProveedor(FacturaCompra f, FilaFacturaCompra o)
    {
        static string Compacto(string? t) => new((t ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        if (f.IdProveedor is { } id && o.IdProveedor is { } otro) return id == otro;
        var nif = Compacto(f.NifProveedor);
        if (nif.Length > 0 && Compacto(o.Nif).Length > 0) return nif == Compacto(o.Nif);
        return f.Carpeta.Length > 0 && string.Equals(f.Carpeta, o.Carpeta, StringComparison.OrdinalIgnoreCase);
    }

    // ---------- proveedor ----------

    public string Nif
    {
        get => _nif;
        set
        {
            if (!Cambiar(ref _nif, (value ?? "").Trim().ToUpperInvariant(), "nif")) return;
            _proveedor = BuscarPorNif(_nif);
            _cuentaProveedorNueva = null;
            Avisar(nameof(NombreProveedor), nameof(HayProveedor), nameof(AvisoProveedor), nameof(PuedeDarDeAltaProveedor));
            ProponerFormaPago();
            ProponerProducto();
        }
    }

    public bool HayProveedor => _proveedor is not null;
    public int? IdTerceroProveedor => _proveedor?.IdTercero;
    public string NombreProveedor => _proveedor?.Nombre ?? (Factura.Carpeta.Length > 0 ? $"Carpeta «{Factura.Carpeta}»" : "");
    public string? AvisoProveedor => _nif.Length == 0 ? null : _proveedor is null ? "Ningún proveedor de la empresa tiene este NIF: dalo de alta con clic derecho en el proveedor («Dar de alta el proveedor…»)." : null;
    public string Cuenta => _cuentaProveedorNueva is not null && _proveedor is not null ? _cuentaProveedorNueva
        : _proveedor is not null && _proveedor.IdProveedor == Factura.IdProveedor ? Factura.CuentaContableProveedor ?? "" : "";

    /// <summary>«Dar de alta el proveedor…» en el menú contextual del proveedor (compras/pantallas 2026-11-06 12:10).</summary>
    public System.Windows.Input.ICommand DarDeAltaProveedorCommand { get; }

    /// <summary>Lo pone la pantalla: confirma con la AEAT o pide los datos y da de alta el proveedor.</summary>
    internal Func<FichaCompraVM, Task>? DarDeAltaProveedor { get; set; }

    /// <summary>NIF de la factura sin proveedor en la empresa: se ofrece «Dar de alta el proveedor…».</summary>
    public bool PuedeDarDeAltaProveedor => !SoloLectura && _proveedor is null && _nif.Length > 0;

    /// <summary>Nombre del proveedor que leyó el reconocimiento en el PDF, para proponerlo en el alta.</summary>
    internal string? NombreLeido
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Factura.NombreProveedorAeat)) return Factura.NombreProveedorAeat.Trim();
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(Factura.Log) ? "{}" : Factura.Log);
                return doc.RootElement.TryGetProperty("candidato", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.Object
                    && c.TryGetProperty("ProveedorIdentificado", out var n) && n.ValueKind == System.Text.Json.JsonValueKind.String && !string.IsNullOrWhiteSpace(n.GetString())
                    ? n.GetString()!.Trim() : null;
            }
            catch (System.Text.Json.JsonException)
            {
                return null;
            }
        }
    }

    /// <summary>Tras dar de alta el proveedor: la ficha lo toma, con su cuenta, y queda con cambios para guardarlo en la factura.</summary>
    internal void ProveedorDadoDeAlta(IReadOnlyList<ProveedorFicha> proveedores, int idProveedor, string cuenta)
    {
        _proveedores = proveedores;
        _proveedor = proveedores.FirstOrDefault(p => p.IdProveedor == idProveedor);
        _cuentaProveedorNueva = cuenta;
        Marcar("proveedor");
        Avisar(nameof(NombreProveedor), nameof(HayProveedor), nameof(AvisoProveedor), nameof(PuedeDarDeAltaProveedor), nameof(IdTerceroProveedor), nameof(Cuenta));
        ProponerFormaPago();
        ProponerProducto();
    }

    private ProveedorFicha? BuscarPorNif(string nif)
    {
        var compacto = new string(nif.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (compacto.Length == 0) return null;
        var candidatos = _proveedores.Where(p => string.Equals(new string(p.Nif.Where(char.IsAsciiLetterOrDigit).ToArray()), compacto, StringComparison.OrdinalIgnoreCase)).ToList();
        return candidatos.FirstOrDefault(p => p.IdProveedor == Factura.IdProveedor) ?? candidatos.FirstOrDefault();
    }

    // ---------- factura ----------

    public string Numero { get => _numero; set => Cambiar(ref _numero, value ?? "", "numero"); }
    public string Fecha { get => _fecha; set { if (Cambiar(ref _fecha, value ?? "", "fecha")) Avisar(nameof(ErrorFecha)); } }
    public string? ErrorFecha => _fecha.Length > 0 && Texto.LeerFechaHora(_fecha) is null ? "Escribe la fecha como dd/mm/aaaa." : null;

    public OpcionFicha<OperacionCompra> Operacion
    {
        get => _operacion;
        set
        {
            if (value is null || !Cambiar(ref _operacion, value, "operacion")) return;
            if (value.Valor == OperacionCompra.Importacion && !_isp) { _isp = true; Avisar(nameof(Isp), nameof(CuotaBloqueada), nameof(PuedeMarcarIsp), nameof(AyudaIsp)); }
            Avisar(nameof(EsImportacion), nameof(NoEsImportacion));
            AlCambiarImportes();
        }
    }

    public bool EsImportacion => _operacion.Valor == OperacionCompra.Importacion;
    public bool NoEsImportacion => !EsImportacion;
    public OpcionFicha<ProductoCompra?> Producto { get => _producto; set { if (value is not null) Cambiar(ref _producto, value, "producto"); } }
    /// <summary>
    /// Inversión del sujeto pasivo: la factura no lleva IVA (lo autorrepercute la empresa con el tipo general del ejercicio).
    /// Con cuota de IVA no se puede marcar; al marcarla, la cuota de cada tipo pasa a 0.
    /// </summary>
    public bool Isp
    {
        get => _isp;
        set
        {
            if (value && !_cargando && CuotaCalculada != 0m) { Avisar(nameof(Isp)); return; }
            if (!Cambiar(ref _isp, value, "isp")) return;
            if (_isp) foreach (var d in Desgloses) d.QuitarCuota();
            Avisar(nameof(CuotaBloqueada), nameof(PuedeMarcarIsp), nameof(AyudaIsp));
            AlCambiarImportes();
        }
    }

    public bool CuotaBloqueada => SoloLectura || _isp;
    public bool PuedeMarcarIsp => Editable && (_isp || CuotaCalculada == 0m);
    public string AyudaIsp => _isp || CuotaCalculada == 0m
        ? "La cuota la autorrepercute la empresa con el tipo general del ejercicio (intracomunitarias, importación y casos de inversión); la factura no lleva IVA."
        : "Una factura con inversión del sujeto pasivo no puede llevar IVA: deja la cuota a 0 para marcarla.";

    public OpcionFicha<string> CuentaGasto
    {
        get => _cuentaGasto;
        set { if (value is not null && Cambiar(ref _cuentaGasto, value, "gasto")) Avisar(nameof(CodigoCuentaGasto), nameof(NombreCuentaGasto)); }
    }

    /// <summary>
    /// Código de la cuenta de gasto tal como se escribe en la ficha (contabilidad/cuenta-contable 2026-11-06): admite el
    /// punto de relleno (629.1 = 6290001); un código que no está en la lista abre el buscador con lo escrito.
    /// </summary>
    public string CodigoCuentaGasto { get => _cuentaGasto.Valor ?? ""; set => EscribirCuentaGasto(value); }

    public string NombreCuentaGasto => _cuentaGasto.Valor is null ? "Sin cuenta de gasto"
        : _cuentaGasto.Texto.IndexOf(" · ", StringComparison.Ordinal) is var i and >= 0 ? _cuentaGasto.Texto[(i + 3)..] : _cuentaGasto.Texto;

    /// <summary>Lupa o F4 junto a la cuenta de gasto: abre el buscador de cuentas con el texto del campo.</summary>
    public System.Windows.Input.ICommand BuscarCuentaGastoCommand { get; }

    /// <summary>Abre el buscador de cuentas (lo pone la pantalla): recibe el texto y devuelve la cuenta elegida o creada, o null.</summary>
    internal Func<string, Task<CuentaContable?>>? BuscarCuenta { get; set; }

    /// <summary>
    /// «Añadir texto clave…» en el menú contextual de la cuenta de gasto y del centro de coste (compras/pantallas
    /// 2026-11-06 17:02): un texto que, si aparece en el contenido o la ruta de un PDF, le asigna la cuenta y el centro
    /// que tiene ahora la ficha. No cambia la factura.
    /// </summary>
    public System.Windows.Input.ICommand AnadirTextoClaveCommand { get; }

    /// <summary>Lo pone la pantalla: pide el texto y lo guarda.</summary>
    internal Func<FichaCompraVM, Task>? AnadirTextoClave { get; set; }

    /// <summary>Cuenta de gasto elegida en la ficha (código y «código · nombre»), o null.</summary>
    internal (string Codigo, string Texto)? CuentaGastoActual => _cuentaGasto.Valor is { } v ? (v, _cuentaGasto.Texto) : null;

    /// <summary>Centro de coste elegido en la ficha, o null.</summary>
    internal (int Id, string Texto)? CentroCosteActual => _centroCoste.Valor is int v ? (v, _centroCoste.Texto) : null;

    private void EscribirCuentaGasto(string? texto)
    {
        var t = (texto ?? "").Trim();
        if (SoloLectura || t == (_cuentaGasto.Valor ?? "")) return;
        if (t.Length == 0) { CuentaGasto = _cuentasGasto[0]; return; }
        if (OpcionCuentaGasto(t) is { } op) { CuentaGasto = op; return; }
        // Código desconocido: el campo vuelve a la cuenta que había y se abre el buscador con lo escrito.
        Avisar(nameof(CodigoCuentaGasto));
        _ = BuscarCuentaGastoAsync(t, diferir: true);
    }

    /// <summary>La cuenta de la lista con ese código exacto o, con punto, la única que coincide al expandirlo.</summary>
    private OpcionFicha<string>? OpcionCuentaGasto(string texto)
    {
        if (_cuentasGasto.FirstOrDefault(c => c.Valor == texto) is { } exacta) return exacta;
        if (!texto.Contains('.')) return null;
        var porPunto = _cuentasGasto.Where(c => c.Valor is { } v && BuscadorCuentas.ExpandirPunto(texto, v.Length) == v).Take(2).ToList();
        return porPunto.Count == 1 ? porPunto[0] : null;
    }

    private bool _buscandoCuenta;

    /// <summary>Abre el buscador y pone la cuenta elegida; la añade a la lista si no estaba (la recién creada, por ejemplo).</summary>
    internal async Task BuscarCuentaGastoAsync(string? texto, bool diferir)
    {
        if (SoloLectura || BuscarCuenta is null || _buscandoCuenta) return;
        // Desde el campo, después de que termine de escribirse el valor (no se abre una ventana dentro del enlace).
        if (diferir) await Task.Yield();
        if (_buscandoCuenta) return;
        _buscandoCuenta = true;
        try
        {
            var elegida = await BuscarCuenta((texto ?? _cuentaGasto.Valor ?? "").Trim());
            if (elegida is null) return;
            var op = _cuentasGasto.FirstOrDefault(c => c.Valor == elegida.Codigo);
            if (op is null)
            {
                op = new OpcionFicha<string>($"{elegida.Codigo} · {elegida.Nombre}", elegida.Codigo);
                _cuentasGasto = [_cuentasGasto[0], .. _cuentasGasto.Skip(1).Append(op).OrderBy(c => c.Valor, StringComparer.Ordinal)];
                Avisar(nameof(CuentasGasto));
            }
            CuentaGasto = op;
            // Sin centro de coste en la factura, el de la cuenta (contabilidad/cuenta-contable 2026-11-06 18:10).
            if (_centroCoste.Valor is null && elegida.IdCentroCostePredeterminado is int cc && CentrosCoste.FirstOrDefault(c => c.Valor == cc) is { } centro)
                CentroCoste = centro;
        }
        finally
        {
            _buscandoCuenta = false;
        }
    }
    public OpcionFicha<int?> CentroCoste { get => _centroCoste; set { if (value is not null) Cambiar(ref _centroCoste, value, "centro"); } }
    public OpcionFicha<int?> Rectifica { get => _rectifica; set { if (value is not null) Cambiar(ref _rectifica, value, "rectifica"); } }
    /// <summary>Formas de pago del catálogo (organizacion/formas-pago); la elegida pasa a ser la del proveedor al guardar.</summary>
    public IReadOnlyList<OpcionFicha<byte?>> FormasPago { get; }
    public OpcionFicha<byte?> FormaPago { get => _formaPago; set { if (value is not null) Cambiar(ref _formaPago, value, "formaPago"); } }

    /// <summary>Al cambiar de proveedor, su producto por defecto si el operador no ha elegido otro.</summary>
    private void ProponerProducto()
    {
        if (_manuales.Contains("producto") || _proveedor?.ProductoDefecto is not ProductoCompra pd || _producto.Valor == pd) return;
        _producto = Productos.First(p => p.Valor == pd);
        Avisar(nameof(Producto));
    }

    /// <summary>Sin forma de pago elegida, propone la del proveedor (compras/pantallas 2026-11-06).</summary>
    private void ProponerFormaPago()
    {
        if (_formaPago.Valor is not null || _proveedor?.IdFormaPagoDefecto is not byte fp) return;
        if (FormasPago.FirstOrDefault(x => x.Valor == fp) is { } opcion) { _formaPago = opcion; Avisar(nameof(FormaPago)); }
    }
    public string Notas { get => _notas; set => Cambiar(ref _notas, value ?? "", "notas"); }

    // ---------- importes ----------

    /// <summary>En importación no hay desglose por tipos: un solo importe (sin IVA deducible en la factura).</summary>
    public string ImporteUnico { get => _importeUnico; set { if (Cambiar(ref _importeUnico, DesgloseVM.Importe(value), "importes")) AlCambiarImportes(); } }

    public bool ConRetencion { get => _conRetencion; set { if (Cambiar(ref _conRetencion, value, "retencion")) { SincronizarTipoRetencion(); AlCambiarImportes(); } } }
    public string PorcentajeRetencion { get => _porcentajeRetencion; set { if (Cambiar(ref _porcentajeRetencion, value ?? "", "retencion")) { SincronizarTipoRetencion(); AlCambiarImportes(); } } }
    public OpcionFicha<ClaseRetencion?> ClaseRetencion { get => _claseRetencion; set { if (value is not null) Cambiar(ref _claseRetencion, value, "retencion"); } }

    /// <summary>
    /// «Sin retención» y los tipos vigentes del catálogo (compras/pantallas 2026-11-06 13:36); un porcentaje que no esté en él
    /// (facturas antiguas o leídas así del PDF) se muestra igualmente.
    /// </summary>
    public IReadOnlyList<OpcionFicha<TipoRetencionCompra?>> TiposRetencion => _tiposRetencion;

    /// <summary>Elegir un tipo rellena el porcentaje y la clase de la retención.</summary>
    public OpcionFicha<TipoRetencionCompra?> TipoRetencion
    {
        get => _tipoRetencion;
        set
        {
            if (value is null || !Cambiar(ref _tipoRetencion, value, "retencion")) return;
            var t = value.Valor;
            _conRetencion = t is not null;
            if (t is not null)
            {
                _porcentajeRetencion = t.Porcentaje.ToString("0.##", Formatos.Cultura);
                _claseRetencion = ClasesRetencion.First(c => c.Valor == t.Clase);
            }
            Avisar(nameof(ConRetencion), nameof(PorcentajeRetencion), nameof(ClaseRetencion));
            AlCambiarImportes();
        }
    }

    private static string TextoRetencionOpcion(TipoRetencionCompra t) => $"{t.Porcentaje.ToString("0.##", Formatos.Cultura)} % · {t.Descripcion}";

    private IReadOnlyList<OpcionFicha<TipoRetencionCompra?>> OpcionesRetencion()
    {
        var l = new List<OpcionFicha<TipoRetencionCompra?>> { new("Sin retención", null) };
        l.AddRange(_tiposRetencionCatalogo.Select(t => new OpcionFicha<TipoRetencionCompra?>(TextoRetencionOpcion(t), t)));
        if (_conRetencion && Texto.LeerDecimal(_porcentajeRetencion) is { } p && Coincidente(l, p) is null)
        {
            var clase = _claseRetencion.Valor ?? Facturas.ClaseRetencion.Profesionales;
            var otro = new TipoRetencionCompra(p, clase == Facturas.ClaseRetencion.Arrendamientos ? "Arrendamientos (no está en la tabla)" : "no está en la tabla", clase);
            l.Add(new(TextoRetencionOpcion(otro), otro));
        }
        return l;
    }

    private OpcionFicha<TipoRetencionCompra?>? Coincidente(IEnumerable<OpcionFicha<TipoRetencionCompra?>> opciones, decimal porcentaje)
    {
        var mismo = opciones.Where(o => o.Valor is { } t && t.Porcentaje == porcentaje).ToList();
        return mismo.FirstOrDefault(o => o.Valor!.Clase == _claseRetencion.Valor) ?? mismo.FirstOrDefault();
    }

    private OpcionFicha<TipoRetencionCompra?> OpcionRetencionActual() =>
        _conRetencion && Texto.LeerDecimal(_porcentajeRetencion) is { } p ? Coincidente(_tiposRetencion, p) ?? _tiposRetencion[0] : _tiposRetencion[0];

    /// <summary>Tras cambiar la retención por otra vía (volver a reconocer), el desplegable refleja el porcentaje.</summary>
    private void SincronizarTipoRetencion()
    {
        _tiposRetencion = OpcionesRetencion();
        _tipoRetencion = OpcionRetencionActual();
        Avisar(nameof(TiposRetencion), nameof(TipoRetencion));
    }
    public string Suplido { get => _suplido; set { if (Cambiar(ref _suplido, DesgloseVM.Importe(value), "suplido")) AlCambiarImportes(); } }
    public string Total { get => _total; set { if (Cambiar(ref _total, DesgloseVM.Importe(value), "importes")) AlCambiarImportes(); } }

    public string Divisa { get => _divisa; set { if (Cambiar(ref _divisa, (value ?? "").Trim().ToUpperInvariant(), "divisa")) Avisar(nameof(EsDivisa), nameof(TextoCambio)); } }
    public bool EsDivisa => !string.Equals(_divisa, "EUR", StringComparison.OrdinalIgnoreCase) && _divisa.Length > 0;
    public string TotalDivisa { get => _totalDivisa; set => Cambiar(ref _totalDivisa, value ?? "", "divisa"); }
    public string TextoCambio => !EsDivisa ? ""
        : Factura.TipoCambioAplicado is { } c && Factura.FechaTipoCambio is { } f ? $"Cambio oficial del BCE del {Formatos.Fecha(f)}: 1 {Factura.Divisa} = {c.ToString("0.######", Formatos.Cultura)} €"
        : "Sin cambio oficial para la fecha de la factura: no se podrá contabilizar sin él.";

    public decimal BaseCalculada => EsImportacion ? Texto.LeerDecimal(_importeUnico) ?? 0m : Desgloses.Sum(d => d.BaseValor ?? 0m);
    public decimal CuotaCalculada => EsImportacion ? 0m : Desgloses.Sum(d => d.CuotaValor ?? 0m);

    /// <summary>Retención = base × % redondeada a céntimos (alejándose de cero).</summary>
    public decimal RetencionCalculada => _conRetencion && Texto.LeerDecimal(_porcentajeRetencion) is { } p
        ? Math.Round(BaseCalculada * p / 100m, 2, MidpointRounding.AwayFromZero) : 0m;

    /// <summary>Total = base + IVA − retención + suplido.</summary>
    public decimal TotalCalculado => BaseCalculada + CuotaCalculada - RetencionCalculada + (Texto.LeerDecimal(_suplido) ?? 0m);
    public string TextoBase => Formatos.Importe(BaseCalculada);
    public string TextoCuota => Formatos.Importe(CuotaCalculada);
    public string TextoRetencion => RetencionCalculada == 0 ? "" : "−" + Formatos.Importe(RetencionCalculada);
    public string TextoTotalCalculado => Formatos.Importe(TotalCalculado);

    /// <summary>Diferencia entre el total del PDF y el calculado; vacío si cuadra o falta el total.</summary>
    public string? Descuadre => Texto.LeerDecimal(_total) is { } t && t != TotalCalculado
        ? $"No cuadra: el total del PDF es {Formatos.Importe(t)} y la suma da {Formatos.Importe(TotalCalculado)} (diferencia {Formatos.Importe(t - TotalCalculado)})."
        : null;

    private void AlCambiarImportes()
    {
        Avisar(nameof(TextoBase), nameof(TextoCuota), nameof(TextoRetencion), nameof(TextoTotalCalculado), nameof(Descuadre), nameof(PuedeMarcarIsp), nameof(AyudaIsp));
        if (!_cargando) Marcar("importes");
    }

    // ---------- confianza ----------

    public MarcaConfianza ConfianzaNif => _manuales.Contains("nif") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Nif, "el NIF", _confianza.Avisos, _confianza.EsPlantilla("nif"));
    public MarcaConfianza ConfianzaNumero => _manuales.Contains("numero") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Numero, "el número", _confianza.Avisos, _confianza.EsPlantilla("numero"));
    public MarcaConfianza ConfianzaFecha => _manuales.Contains("fecha") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Fecha, "la fecha", _confianza.Avisos, _confianza.EsPlantilla("fecha"));
    public MarcaConfianza ConfianzaImportes => _manuales.Contains("importes") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Importes, "los importes", _confianza.Avisos, _confianza.EsPlantilla("total") && _confianza.Importes == 1m);

    /// <summary>Avisos del reconocimiento y lo que falta para que la factura esté lista.</summary>
    public IReadOnlyList<string> Avisos
    {
        get
        {
            var l = new List<string>(_confianza.Avisos);
            var f = Construir();
            var falta = f.Pendiente();
            if (falta.Count > 0) l.Insert(0, "Falta " + string.Join(", ", falta) + ".");
            if (Factura.Duplicada) l.Add("El ERP la marcó como duplicada de otra factura.");
            return l;
        }
    }

    public bool HayAvisos => Avisos.Count > 0;

    public string Resumen => Factura.Contabilizada ? "Contabilizada: solo se consulta."
        : _confianza.Global is { } g ? $"Reconocida con confianza {Math.Round(g * 100m):0} %{(_confianza.Fuente == "ocr" ? " (con OCR)" : "")}." : "Sin datos del reconocimiento.";

    // ---------- cambios ----------

    private bool Cambiar<T>(ref T campo, T valor, string clave, [System.Runtime.CompilerServices.CallerMemberName] string? propiedad = null)
    {
        if (SoloLectura && !_cargando) return false;
        if (!Fijar(ref campo, valor, propiedad)) return false;
        if (!_cargando) Marcar(clave);
        return true;
    }

    private void Marcar(string clave)
    {
        _manuales.Add(clave);
        TieneCambios = true;
        Avisar(nameof(ConfianzaNif), nameof(ConfianzaNumero), nameof(ConfianzaFecha), nameof(ConfianzaImportes), nameof(Avisos), nameof(HayAvisos));
    }

    /// <summary>Añade un tipo de IVA vacío al desglose.</summary>
    internal void AnadirTipo()
    {
        if (SoloLectura) return;
        Desgloses.Add(NuevoDesglose(TipoGeneral));
        AlCambiarImportes();
    }

    internal void QuitarTipo(DesgloseVM d)
    {
        if (SoloLectura || Desgloses.Count <= 1) return;
        Desgloses.Remove(d);
        AlCambiarImportes();
    }

    /// <summary>
    /// Vuelca lo leído de nuevo del PDF («Volver a reconocer») sobre la ficha, como cambios sin guardar: el número, la
    /// fecha, el NIF y los importes. No toca la imputación ni las notas.
    /// </summary>
    internal void AplicarLectura(FacturaCompra leida)
    {
        if (SoloLectura) return;
        // Lo leído de nuevo no es manual: la confianza pasa a ser la de esta lectura y queda como cambio sin guardar.
        _cargando = true;
        try
        {
            Nif = leida.NifProveedor ?? "";
            Numero = leida.NumeroFactura;
            Fecha = leida.Fecha is { } f ? Formatos.Fecha(f) : "";
            Total = leida.ImporteTotal is { } t ? Formatos.Importe(t) : "";
            Isp = leida.InversionSujetoPasivo;
            Desgloses.Clear();
            foreach (var d in leida.Desgloses) Desgloses.Add(NuevoDesglose(d.TipoIva, d.BaseImponible, d.CuotaIva));
            if (Desgloses.Count == 0) Desgloses.Add(NuevoDesglose(TipoDeducido(leida), leida.BaseImponible, leida.CuotaIva));
            ImporteUnico = leida.BaseImponible is { } b ? Formatos.Importe(b) : "";
            ConRetencion = (leida.ImporteRetencion ?? 0m) != 0m;
            if (leida.PorcentajeRetencion is { } p && p != 0) PorcentajeRetencion = p.ToString("0.##", Formatos.Cultura);
        }
        finally { _cargando = false; }
        SincronizarTipoRetencion();
        ProponerFormaPago();
        if (!string.IsNullOrWhiteSpace(leida.Log)) _confianza = ConfianzaCampos.DesdeLog(leida.Log);
        _manuales.Clear();
        _elegidos.Clear();
        TieneCambios = true;
        Avisar(nameof(TextoBase), nameof(TextoCuota), nameof(TextoRetencion), nameof(TextoTotalCalculado), nameof(Descuadre));
        Avisar(nameof(ConfianzaNif), nameof(ConfianzaNumero), nameof(ConfianzaFecha), nameof(ConfianzaImportes), nameof(Avisos), nameof(HayAvisos), nameof(Resumen));
        Avisar(nameof(CuotaBloqueada), nameof(PuedeMarcarIsp), nameof(AyudaIsp));
    }

    /// <summary>
    /// El operador ha elegido en el visor dónde está el valor de un campo (nif, numero, fecha o total): al guardar, la
    /// plantilla aprende de ese sitio y no de otro donde el valor también aparezca (compras/pantallas 2026-11-06 12:10).
    /// </summary>
    internal void Elegir(string campo, Compras.Reconocimiento.PuntoDocumento punto)
    {
        if (SoloLectura || !Compras.Reconocimiento.PlantillasReconocimiento.Campos.Contains(campo)) return;
        _elegidos[campo] = punto;
        TieneCambios = true;
        Avisar(nameof(Elegidos));
    }

    /// <summary>Dónde eligió el operador cada campo en el visor (se pasa al guardar).</summary>
    public IReadOnlyDictionary<string, Compras.Reconocimiento.PuntoDocumento> Elegidos => _elegidos;

    /// <summary>La factura con lo que hay en la ficha (sin guardar).</summary>
    internal FacturaCompra Construir()
    {
        var o = Factura;
        var f = new FacturaCompra
        {
            IdRegistro = o.IdRegistro, Ejercicio = o.Ejercicio, RutaPdf = o.RutaPdf, Md5 = o.Md5, Carpeta = o.Carpeta,
            IdProveedor = _proveedor?.IdProveedor ?? (_nif.Length == 0 ? o.IdProveedor : null),
            CuentaContableProveedor = _cuentaProveedorNueva is not null && _proveedor?.IdProveedor is not null ? _cuentaProveedorNueva
                : _proveedor is not null && _proveedor.IdProveedor == o.IdProveedor ? o.CuentaContableProveedor : null,
            NifProveedor = _nif.Length == 0 ? null : _nif,
            NombreProveedorAeat = o.NombreProveedorAeat,
            NumeroFactura = new string(_numero.Where(char.IsAsciiLetterOrDigit).ToArray()),
            Fecha = Texto.LeerFechaHora(_fecha),
            Concepto = o.Concepto,
            Operacion = _operacion.Valor, Producto = _producto.Valor, InversionSujetoPasivo = _isp,
            BaseImponible = BaseCalculada, CuotaIva = CuotaCalculada, ImporteTotal = Texto.LeerDecimal(_total),
            PorcentajeRetencion = _conRetencion ? Texto.LeerDecimal(_porcentajeRetencion) : null,
            ImporteRetencion = _conRetencion ? RetencionCalculada : null,
            ClaseRetencion = _conRetencion ? _claseRetencion.Valor : null,
            Suplido = Texto.LeerDecimal(_suplido),
            Divisa = _divisa.Length == 0 ? "EUR" : _divisa,
            ImporteTotalDivisa = EsDivisa ? Texto.LeerDecimal(_totalDivisa) : null,
            TipoCambioAplicado = EsDivisa ? o.TipoCambioAplicado : null,
            FechaTipoCambio = EsDivisa ? o.FechaTipoCambio : null,
            IdRegistroFacturaRectificada = _rectifica.Valor,
            CuentaGasto = _cuentaGasto.Valor, IdCentroCoste = _centroCoste.Valor,
            IdFormaPago = _formaPago.Valor, CuentaFormaPago = o.CuentaFormaPago,
            Notas = _notas.Length == 0 ? null : _notas,
            Confianza = o.Confianza, RequiereRevision = o.RequiereRevision, Fuente = o.Fuente, Log = o.Log,
            FechaHoraRegistro = o.FechaHoraRegistro, Version = o.Version, Contabilizada = o.Contabilizada, Duplicada = o.Duplicada
        };
        if (f.Concepto.Length == 0 || o.NumeroFactura.Length > 0 && f.Concepto.EndsWith(o.NumeroFactura, StringComparison.Ordinal))
            f.Concepto = $"{(_proveedor?.Nombre ?? o.Carpeta)} Fact: {f.NumeroFactura}".Trim();
        if (!EsImportacion)
        {
            short orden = 1;
            foreach (var d in Desgloses.Where(d => d.BaseValor is not null || d.CuotaValor is not null).OrderBy(d => d.TipoValor ?? 0m))
                f.Desgloses.Add(new DesgloseFacturaCompra(orden++, d.TipoValor ?? 0m, d.BaseValor ?? 0m, d.CuotaValor ?? 0m));
        }
        else if (f.BaseImponible is { } b && b != 0) f.Desgloses.Add(new DesgloseFacturaCompra(1, 0m, b, 0m));
        return f;
    }

    /// <summary>Tras guardar: la ficha pasa a la versión guardada y deja de tener cambios.</summary>
    internal void Guardada(FacturaCompra guardada)
    {
        Factura = guardada;
        TieneCambios = false;
        _elegidos.Clear();
        Avisar(nameof(Cuenta), nameof(Resumen), nameof(Avisos), nameof(HayAvisos));
    }

    private static decimal? TipoDeducido(FacturaCompra f) =>
        f.BaseImponible is { } b && b != 0 && f.CuotaIva is { } c ? Math.Round(c / b * 100m, 0, MidpointRounding.AwayFromZero) : 21m;
}

/// <summary>Lectura de los textos de la ficha (formato español: 1.234,56 y dd/mm/aaaa).</summary>
internal static class Texto
{
    public static decimal? LeerDecimal(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return null;
        var t = texto.Replace("€", "").Replace("%", "").Trim();
        return Formatos.LeerImporte(t, out var v) ? v
            : decimal.TryParse(t, NumberStyles.Number, Formatos.Cultura, out v) ? v : null;
    }

    public static DateTime? LeerFechaHora(string? texto) =>
        !string.IsNullOrWhiteSpace(texto) && DateTime.TryParseExact(texto.Trim(), ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "d-M-yyyy", "d.M.yyyy"], Formatos.Cultura, DateTimeStyles.None, out var f)
            ? f.Date : null;
}
