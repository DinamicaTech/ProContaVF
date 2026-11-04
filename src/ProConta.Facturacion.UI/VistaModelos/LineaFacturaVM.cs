using System.Collections;
using System.ComponentModel;
using System.Globalization;
using ProConta.Facturacion.Facturas;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Facturacion.UI.VistaModelos;

/// <summary>
/// Línea del editor: los campos se escriben como texto (formato español) y la línea se recalcula al cambiar cualquiera.
/// Escribir el total con IVA fija la base conservando ese total (entrada inversa, RF-FAC-02).
/// </summary>
public sealed class LineaFacturaVM : ModeloVista, INotifyDataErrorInfo
{
    private readonly Action _cambiada;
    private readonly Dictionary<string, string> _errores = new(StringComparer.Ordinal);

    internal LineaFacturaVM(LineaFacturaVenta linea, bool editable, IReadOnlyList<decimal> tiposIva, IReadOnlyList<OpcionCentroCoste> centros, Action cambiada)
    {
        Datos = linea;
        Editable = editable;
        TiposIva = tiposIva.Contains(linea.TipoIva) ? tiposIva : tiposIva.Append(linea.TipoIva).OrderByDescending(v => v).ToList();
        // Un valor que no es un centro activo e imputable se sigue viendo en la línea que lo tiene. Hasta la migración del
        // ERP, conceptos y líneas antiguos guardan aquí el código del centro del ERP (318…), no el id del centro nuevo.
        CentrosCoste = linea.IdCentroCoste is int c && centros.All(o => o.Id != c)
            ? centros.Append(new OpcionCentroCoste(c, $"ERP {c} (sin migrar)")).ToList()
            : centros;
        _fecha = Texto.Fecha(linea.Fecha);
        _cambiada = cambiada;
    }

    private string _fecha;

    public LineaFacturaVenta Datos { get; }
    public bool Editable { get; }
    public bool SoloLectura => !Editable;
    public IReadOnlyList<decimal> TiposIva { get; }

    public int Numero => Datos.NumeroLinea;

    /// <summary>Fecha de entrega o consumo de la línea (por ejemplo, la entrada de un alquiler turístico); opcional.</summary>
    public string Fecha
    {
        get => _fecha;
        set
        {
            _fecha = value ?? "";
            if (Texto.LeerFecha(_fecha, out var f)) { Datos.Fecha = f; QuitarError(nameof(Fecha)); }
            else PonerError(nameof(Fecha), $"«{_fecha}» no es una fecha. Escríbela como dd/mm/aaaa, por ejemplo 01/07/2026, o déjala vacía.");
            Cambiada(nameof(Fecha));
        }
    }

    /// <summary>Centros de coste imputables de la sociedad, con «Sin centro» al principio.</summary>
    public IReadOnlyList<OpcionCentroCoste> CentrosCoste { get; }

    /// <summary>Centro de coste (analítica) de la línea: viene del concepto y se puede cambiar al emitir.</summary>
    public OpcionCentroCoste? CentroCoste
    {
        get => CentrosCoste.FirstOrDefault(o => o.Id == Datos.IdCentroCoste);
        set { Datos.IdCentroCoste = value?.Id; Cambiada(nameof(CentroCoste)); }
    }

    internal void AvisarNumero() => Avisar(nameof(Numero));

    public string Nombre
    {
        get => Datos.Nombre;
        set { Datos.Nombre = value ?? ""; Cambiada(nameof(Nombre)); }
    }

    public string Descripcion
    {
        get => Datos.Descripcion;
        set { Datos.Descripcion = value ?? ""; Cambiada(nameof(Descripcion)); }
    }

    public string Cantidad
    {
        get => Texto.Cantidad(Datos.Cantidad);
        set { if (Leer(value, nameof(Cantidad), out var v)) { Datos.Cantidad = v; Recalcular(); } }
    }

    public string Precio
    {
        get => Texto.Precio(Datos.PrecioUnitario);
        set { if (Leer(value, nameof(Precio), out var v)) { Datos.PrecioUnitario = v; Recalcular(); } }
    }

    public string DescuentoPorcentaje
    {
        get => Datos.DescuentoPorcentaje.ToString("0.##", Formatos.Cultura);
        set { if (Leer(value, nameof(DescuentoPorcentaje), out var v)) { Datos.DescuentoPorcentaje = v; Recalcular(); } }
    }

    public string DescuentoImporte
    {
        get => Formatos.Numero(Datos.DescuentoImporte);
        set { if (Leer(value, nameof(DescuentoImporte), out var v)) { Datos.DescuentoImporte = v; Recalcular(); } }
    }

    public decimal TipoIva
    {
        get => Datos.TipoIva;
        set { Datos.TipoIva = value; Recalcular(); }
    }

    public bool EsSuplido
    {
        get => Datos.EsSuplido;
        set
        {
            Datos.EsSuplido = value;
            if (value) Datos.TipoIva = 0m;
            Recalcular();
            Avisar(nameof(TipoIva), nameof(PuedeCambiarIva));
        }
    }

    public bool PuedeCambiarIva => Editable && !Datos.EsSuplido;

    public string Cuenta
    {
        get => Datos.CuentaContable ?? "";
        set { Datos.CuentaContable = string.IsNullOrWhiteSpace(value) ? null : value.Trim(); Cambiada(nameof(Cuenta)); }
    }

    public string Base => Formatos.Numero(Datos.BaseImponible.Valor);
    public string Cuota => Formatos.Numero((Datos.CuotaIva + Datos.CuotaRecargo).Valor);

    /// <summary>Total con IVA; si se escribe, se calcula la base conservando ese total.</summary>
    public string Total
    {
        get => Formatos.Numero(Datos.ImporteTotal.Valor);
        set
        {
            if (!Leer(value, nameof(Total), out var v)) return;
            Datos.FijarTotal(v);
            AvisarImportes();
            Avisar(nameof(Precio), nameof(DescuentoPorcentaje), nameof(DescuentoImporte), nameof(Cantidad));
            _cambiada();
        }
    }

    private void Recalcular()
    {
        Datos.Recalcular();
        AvisarImportes();
        _cambiada();
    }

    private void AvisarImportes() => Avisar(nameof(Base), nameof(Cuota), nameof(Total));

    private void Cambiada(string propiedad)
    {
        Avisar(propiedad);
        _cambiada();
    }

    /// <summary>Lee un número en formato español («1.234,56», con o sin €); si no es válido, marca el campo.</summary>
    private bool Leer(string? texto, string campo, out decimal valor)
    {
        if (string.IsNullOrWhiteSpace(texto)) { valor = 0m; QuitarError(campo); return true; }
        if (Texto.LeerNumero(texto, out valor)) { QuitarError(campo); return true; }
        PonerError(campo, $"«{texto}» no es un número. Escríbelo con coma decimal, por ejemplo 1.234,56.");
        return false;
    }

    // ---------- errores ----------

    public bool HasErrors => _errores.Count > 0;
    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && _errores.TryGetValue(propertyName, out var e) ? new[] { e } : Array.Empty<string>();

    internal void PonerError(string propiedad, string mensaje)
    {
        _errores[propiedad] = mensaje;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    internal void QuitarError(string propiedad)
    {
        if (_errores.Remove(propiedad)) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propiedad));
    }

    internal void LimpiarErrores()
    {
        foreach (var p in _errores.Keys.ToList()) QuitarError(p);
    }

    /// <summary>Propiedad de la línea que corresponde al campo de una incidencia («Linea3.CuentaContable» → Cuenta).</summary>
    internal static string? Propiedad(string campoMotor) => campoMotor switch
    {
        "Nombre" => nameof(Nombre),
        "CuentaContable" => nameof(Cuenta),
        "TipoIva" => nameof(TipoIva),
        "DescuentoPorcentaje" => nameof(DescuentoPorcentaje),
        _ => null
    };
}

/// <summary>Centro de coste elegible en una línea; Id null es «Sin centro».</summary>
public sealed record OpcionCentroCoste(int? Id, string Rotulo)
{
    public override string ToString() => Rotulo;
}

/// <summary>Formato y lectura de números del editor (formato español; precios con hasta cuatro decimales).</summary>
internal static class Texto
{
    public static string Precio(decimal v) => Signo(v, Math.Abs(v).ToString("#,##0.00##", Formatos.Cultura));
    public static string Cantidad(decimal v) => Signo(v, Math.Abs(v).ToString("#,##0.####", Formatos.Cultura));
    public static string Porcentaje(decimal v) => v.ToString("0.##", Formatos.Cultura);

    private static string Signo(decimal v, string abs) => v < 0 ? Formatos.Menos + abs : abs;

    /// <summary>
    /// Lee un número escrito por el operador: con o sin «€» o «%», con o sin miles, con «-» o «−». La coma es el decimal;
    /// un único punto seguido de una a cuatro cifras al final también (punto del teclado numérico).
    /// </summary>
    public static bool LeerNumero(string? texto, out decimal valor)
    {
        valor = 0m;
        if (string.IsNullOrWhiteSpace(texto)) return false;
        var t = texto.Replace("€", "", StringComparison.Ordinal).Replace("%", "", StringComparison.Ordinal).Replace(" ", "", StringComparison.Ordinal)
            .Replace("\u00A0", "", StringComparison.Ordinal).Replace(Formatos.Menos, "-", StringComparison.Ordinal).Trim();
        if (t.Length == 0) return false;
        if (!t.Contains(',', StringComparison.Ordinal) && t.Count(c => c == '.') == 1)
        {
            var i = t.IndexOf('.', StringComparison.Ordinal);
            if (t.Length - i - 1 is >= 1 and <= 4 && !(t.Length - i - 1 == 3)) t = t[..i] + "," + t[(i + 1)..];
        }
        return decimal.TryParse(t, NumberStyles.Number, Formatos.Cultura, out valor);
    }

    public static string Fecha(DateTime? f) => f is { } d ? Formatos.Fecha(d) : "";

    /// <summary>Lee una fecha dd/mm/aaaa (también d/m/aa y con guiones); vacía es null.</summary>
    public static bool LeerFecha(string? texto, out DateTime? fecha)
    {
        fecha = null;
        if (string.IsNullOrWhiteSpace(texto)) return true;
        var formatos = new[] { "d/M/yyyy", "d/M/yy", "d-M-yyyy", "d-M-yy", "d.M.yyyy", "ddMMyyyy" };
        if (DateTime.TryParseExact(texto.Trim(), formatos, Formatos.Cultura, DateTimeStyles.None, out var d)) { fecha = d.Date; return true; }
        return false;
    }
}
