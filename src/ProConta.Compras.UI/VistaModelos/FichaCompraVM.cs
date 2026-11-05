using System.Collections.ObjectModel;
using System.Globalization;
using ProConta.Compras.Facturas;
using ProConta.Contabilidad.CentrosCoste;
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
    public static MarcaConfianza De(decimal? confianza, string campo, IReadOnlyList<string> avisos)
    {
        if (confianza is not { } c) return Ninguna;
        var nivel = c >= 0.9m ? "alta" : c >= 0.6m ? "media" : "baja";
        var motivo = c == 0m ? $"No he encontrado {campo} en el PDF." : $"Confianza del reconocimiento en {campo}: {Math.Round(c * 100m):0} %.";
        var relacionados = avisos.Where(a => a.Contains(campo, StringComparison.CurrentCultureIgnoreCase)).ToList();
        if (relacionados.Count > 0) motivo += " " + string.Join(" ", relacionados);
        return new($"{Math.Round(c * 100m):0} %", nivel, motivo);
    }
}

/// <summary>Un tipo de IVA editable de la ficha.</summary>
public sealed class DesgloseVM : ModeloVista
{
    private string _tipo = "", _base = "", _cuota = "";
    private readonly Action _cambio;

    public DesgloseVM(Action cambio, decimal? tipo = null, decimal? @base = null, decimal? cuota = null)
    {
        _cambio = cambio;
        _tipo = tipo is { } t ? t.ToString("0.##", Formatos.Cultura) : "";
        _base = @base is { } b ? Formatos.Numero(b) : "";
        _cuota = cuota is { } c ? Formatos.Numero(c) : "";
    }

    public string Tipo { get => _tipo; set { if (Fijar(ref _tipo, value ?? "")) { ProponerCuota(); _cambio(); } } }
    public string Base { get => _base; set { if (Fijar(ref _base, value ?? "")) { ProponerCuota(); _cambio(); } } }
    public string Cuota { get => _cuota; set { if (Fijar(ref _cuota, value ?? "")) _cambio(); } }

    public decimal? TipoValor => Texto.LeerDecimal(_tipo);
    public decimal? BaseValor => Texto.LeerDecimal(_base);
    public decimal? CuotaValor => Texto.LeerDecimal(_cuota);

    /// <summary>Con tipo y base, la cuota se propone redondeada a céntimos (alejándose de cero) si estaba vacía.</summary>
    private void ProponerCuota()
    {
        if (_cuota.Length == 0 && TipoValor is { } t && BaseValor is { } b)
        {
            _cuota = Formatos.Numero(Math.Round(b * t / 100m, 2, MidpointRounding.AwayFromZero));
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
    private readonly IReadOnlyList<ProveedorFicha> _proveedores;
    private readonly ConfianzaCampos _confianza;
    private readonly HashSet<string> _manuales = new(StringComparer.Ordinal);
    private bool _cargando = true;
    private bool _tieneCambios;

    private string _nif = "", _numero = "", _fecha = "", _total = "", _porcentajeRetencion = "", _suplido = "", _divisa = "EUR", _totalDivisa = "";
    private string _formaPago = "", _notas = "", _importeUnico = "";
    private bool _isp, _conRetencion;
    private ProveedorFicha? _proveedor;
    private OpcionFicha<OperacionCompra> _operacion;
    private OpcionFicha<ProductoCompra?> _producto;
    private OpcionFicha<ClaseRetencion?> _claseRetencion;
    private OpcionFicha<string> _cuentaGasto;
    private OpcionFicha<int?> _centroCoste;
    private OpcionFicha<int?> _rectifica;

    internal FichaCompraVM(FacturaCompra factura, IReadOnlyList<ProveedorFicha> proveedores, IReadOnlyList<OpcionFicha<string>> cuentasGasto,
        IReadOnlyList<CentroCoste> centros, IReadOnlyList<FilaFacturaCompra> otras, bool soloLectura, ConfianzaCampos? confianza = null)
    {
        Factura = factura;
        _proveedores = proveedores;
        SoloLectura = soloLectura || factura.Contabilizada;
        _confianza = confianza ?? ConfianzaCampos.DesdeLog(factura.Log);

        Operaciones = [new("Nacional", OperacionCompra.Nacional), new("Intracomunitaria", OperacionCompra.Intracomunitaria), new("Importación", OperacionCompra.Importacion)];
        Productos = [new("Sin indicar", null), new("Servicios", ProductoCompra.Servicios), new("Bienes", ProductoCompra.Bienes)];
        ClasesRetencion = [new("Profesionales", Facturas.ClaseRetencion.Profesionales), new("Arrendamientos", Facturas.ClaseRetencion.Arrendamientos)];
        var gasto = cuentasGasto.ToList();
        if (factura.CuentaGasto is { Length: > 0 } cg && gasto.All(c => c.Valor != cg)) gasto.Add(new($"{cg} · (no está en el plan del ejercicio)", cg));
        CuentasGasto = [new("Sin cuenta de gasto", null), .. gasto];
        CentrosCoste = [new("Sin centro de coste", null), .. centros.Where(c => c.EstaActivo && c.EsImputable).OrderBy(c => c.RutaJerarquica, StringComparer.Ordinal)
            .Select(c => new OpcionFicha<int?>($"{c.Codigo} · {c.Nombre}", c.IdCentroCoste))];
        Rectificables = [new("No rectifica a ninguna", null), .. otras
            .Where(o => o.IdRegistro != factura.IdRegistro && (factura.IdProveedor is null || o.IdProveedor == factura.IdProveedor))
            .OrderByDescending(o => o.Fecha).Select(o => new OpcionFicha<int?>($"{(o.NumeroFactura.Length > 0 ? o.NumeroFactura : o.NombreFichero)} · {(o.Fecha is { } f ? Formatos.Fecha(f) : "sin fecha")} · {(o.ImporteTotal is { } t ? Formatos.Importe(t) : "")}", o.IdRegistro))];

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
        _suplido = factura.Suplido is { } su && su != 0 ? Formatos.Numero(su) : "";
        _total = factura.ImporteTotal is { } t ? Formatos.Numero(t) : "";
        _divisa = factura.Divisa;
        _totalDivisa = factura.ImporteTotalDivisa is { } td ? Formatos.Numero(td) : "";
        _cuentaGasto = CuentasGasto.FirstOrDefault(c => c.Valor == factura.CuentaGasto) ?? CuentasGasto[0];
        _centroCoste = CentrosCoste.FirstOrDefault(c => c.Valor == factura.IdCentroCoste) ?? CentrosCoste[0];
        _rectifica = Rectificables.FirstOrDefault(r => r.Valor == factura.IdRegistroFacturaRectificada) ?? Rectificables[0];
        _formaPago = factura.CuentaFormaPago ?? "";
        _notas = factura.Notas ?? "";

        if (factura.Desgloses.Count > 0)
            foreach (var d in factura.Desgloses) Desgloses.Add(new DesgloseVM(AlCambiarImportes, d.TipoIva, d.BaseImponible, d.CuotaIva));
        else if (factura.BaseImponible is not null || factura.CuotaIva is not null)
            Desgloses.Add(new DesgloseVM(AlCambiarImportes, TipoDeducido(factura), factura.BaseImponible, factura.CuotaIva));
        if (Desgloses.Count == 0) Desgloses.Add(new DesgloseVM(AlCambiarImportes, 21m));
        _importeUnico = factura.BaseImponible is { } bi ? Formatos.Numero(bi) : "";
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
    public IReadOnlyList<OpcionFicha<string>> CuentasGasto { get; }
    public IReadOnlyList<OpcionFicha<int?>> CentrosCoste { get; }
    public IReadOnlyList<OpcionFicha<int?>> Rectificables { get; }
    public ObservableCollection<DesgloseVM> Desgloses { get; } = [];

    // ---------- proveedor ----------

    public string Nif
    {
        get => _nif;
        set
        {
            if (!Cambiar(ref _nif, (value ?? "").Trim().ToUpperInvariant(), "nif")) return;
            _proveedor = BuscarPorNif(_nif);
            Avisar(nameof(NombreProveedor), nameof(HayProveedor), nameof(AvisoProveedor));
        }
    }

    public bool HayProveedor => _proveedor is not null;
    public int? IdTerceroProveedor => _proveedor?.IdTercero;
    public string NombreProveedor => _proveedor?.Nombre ?? (Factura.Carpeta.Length > 0 ? $"Carpeta «{Factura.Carpeta}»" : "");
    public string? AvisoProveedor => _nif.Length == 0 ? null : _proveedor is null ? "Ningún proveedor de la empresa tiene este NIF: dalo de alta en Terceros." : null;
    public string Cuenta => _proveedor is not null && _proveedor.IdProveedor == Factura.IdProveedor ? Factura.CuentaContableProveedor ?? "" : "";

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
            if (value.Valor == OperacionCompra.Importacion && !_isp) { _isp = true; Avisar(nameof(Isp)); }
            Avisar(nameof(EsImportacion), nameof(NoEsImportacion));
            AlCambiarImportes();
        }
    }

    public bool EsImportacion => _operacion.Valor == OperacionCompra.Importacion;
    public bool NoEsImportacion => !EsImportacion;
    public OpcionFicha<ProductoCompra?> Producto { get => _producto; set { if (value is not null) Cambiar(ref _producto, value, "producto"); } }
    public bool Isp { get => _isp; set => Cambiar(ref _isp, value, "isp"); }

    public OpcionFicha<string> CuentaGasto { get => _cuentaGasto; set { if (value is not null) Cambiar(ref _cuentaGasto, value, "gasto"); } }
    public OpcionFicha<int?> CentroCoste { get => _centroCoste; set { if (value is not null) Cambiar(ref _centroCoste, value, "centro"); } }
    public OpcionFicha<int?> Rectifica { get => _rectifica; set { if (value is not null) Cambiar(ref _rectifica, value, "rectifica"); } }
    public string FormaPago { get => _formaPago; set => Cambiar(ref _formaPago, value ?? "", "formaPago"); }
    public string Notas { get => _notas; set => Cambiar(ref _notas, value ?? "", "notas"); }

    // ---------- importes ----------

    /// <summary>En importación no hay desglose por tipos: un solo importe (sin IVA deducible en la factura).</summary>
    public string ImporteUnico { get => _importeUnico; set { if (Cambiar(ref _importeUnico, value ?? "", "importes")) AlCambiarImportes(); } }

    public bool ConRetencion { get => _conRetencion; set { if (Cambiar(ref _conRetencion, value, "retencion")) AlCambiarImportes(); } }
    public string PorcentajeRetencion { get => _porcentajeRetencion; set { if (Cambiar(ref _porcentajeRetencion, value ?? "", "retencion")) AlCambiarImportes(); } }
    public OpcionFicha<ClaseRetencion?> ClaseRetencion { get => _claseRetencion; set { if (value is not null) Cambiar(ref _claseRetencion, value, "retencion"); } }
    public string Suplido { get => _suplido; set { if (Cambiar(ref _suplido, value ?? "", "suplido")) AlCambiarImportes(); } }
    public string Total { get => _total; set { if (Cambiar(ref _total, value ?? "", "importes")) AlCambiarImportes(); } }

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
        Avisar(nameof(TextoBase), nameof(TextoCuota), nameof(TextoRetencion), nameof(TextoTotalCalculado), nameof(Descuadre));
        if (!_cargando) Marcar("importes");
    }

    // ---------- confianza ----------

    public MarcaConfianza ConfianzaNif => _manuales.Contains("nif") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Nif, "el NIF", _confianza.Avisos);
    public MarcaConfianza ConfianzaNumero => _manuales.Contains("numero") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Numero, "el número", _confianza.Avisos);
    public MarcaConfianza ConfianzaFecha => _manuales.Contains("fecha") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Fecha, "la fecha", _confianza.Avisos);
    public MarcaConfianza ConfianzaImportes => _manuales.Contains("importes") ? MarcaConfianza.Manual : MarcaConfianza.De(_confianza.Importes, "los importes", _confianza.Avisos);

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
        Desgloses.Add(new DesgloseVM(AlCambiarImportes, 21m));
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
        Nif = leida.NifProveedor ?? "";
        Numero = leida.NumeroFactura;
        Fecha = leida.Fecha is { } f ? Formatos.Fecha(f) : "";
        Total = leida.ImporteTotal is { } t ? Formatos.Numero(t) : "";
        Isp = leida.InversionSujetoPasivo;
        Desgloses.Clear();
        foreach (var d in leida.Desgloses) Desgloses.Add(new DesgloseVM(AlCambiarImportes, d.TipoIva, d.BaseImponible, d.CuotaIva));
        if (Desgloses.Count == 0) Desgloses.Add(new DesgloseVM(AlCambiarImportes, TipoDeducido(leida), leida.BaseImponible, leida.CuotaIva));
        ImporteUnico = leida.BaseImponible is { } b ? Formatos.Numero(b) : "";
        ConRetencion = (leida.ImporteRetencion ?? 0m) != 0m;
        if (leida.PorcentajeRetencion is { } p && p != 0) PorcentajeRetencion = p.ToString("0.##", Formatos.Cultura);
        Marcar("importes");
    }

    /// <summary>La factura con lo que hay en la ficha (sin guardar).</summary>
    internal FacturaCompra Construir()
    {
        var o = Factura;
        var f = new FacturaCompra
        {
            IdRegistro = o.IdRegistro, Ejercicio = o.Ejercicio, RutaPdf = o.RutaPdf, Md5 = o.Md5, Carpeta = o.Carpeta,
            IdProveedor = _proveedor?.IdProveedor ?? (_nif.Length == 0 ? o.IdProveedor : null),
            CuentaContableProveedor = _proveedor is not null && _proveedor.IdProveedor == o.IdProveedor ? o.CuentaContableProveedor : null,
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
            CuentaFormaPago = _formaPago.Length == 0 ? null : _formaPago.Trim(),
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
