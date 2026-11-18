using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Documentos.UI;

/// <summary>Un vínculo como se ve: una etiqueta con el tipo («MODELO 303 · 2T 2026») y su aspa para desvincular.</summary>
public sealed class VinculoVM(Vinculo vinculo)
{
    public Vinculo Vinculo { get; } = vinculo;
    public string Tipo => TiposEntidad.Nombre(Vinculo.TipoEntidad).ToUpperInvariant();
    public string Etiqueta => Vinculo.Etiqueta;

    /// <summary>Color de la etiqueta del tipo: MODELO, TERCERO, EJERCICIO, EMPRESA, ACTIVO, FACTURA o ASIENTO.</summary>
    public string Clase => Vinculo.TipoEntidad switch
    {
        TiposEntidad.FacturaVenta or TiposEntidad.FacturaCompra => "FACTURA",
        _ => Vinculo.TipoEntidad,
    };

    public string Ayuda => $"{TiposEntidad.Nombre(Vinculo.TipoEntidad)}: {Vinculo.Etiqueta}. Clic: abrirlo";
}

/// <summary>
/// Una fila de la pantalla Documentos o del panel de una ficha. En la bandeja, el tipo, la fecha y el título se editan en la propia fila y se guardan
/// al cambiarlos (<see cref="Cambiado"/>); un clic fuera de los campos marca o desmarca la fila.
/// </summary>
public sealed class FilaDocumentoVM : ModeloVista
{
    private bool _marcado;
    private string? _tipo;
    private DateTime? _fecha;
    private string _titulo;
    private readonly Action _alMarcar;

    public FilaDocumentoVM(DocumentoListado datos, Action alMarcar)
    {
        Datos = datos;
        _alMarcar = alMarcar;
        _tipo = datos.Tipo;
        _fecha = datos.FechaDocumento;
        _titulo = datos.Titulo;
        Vinculos = datos.Vinculos.Select(v => new VinculoVM(v)).ToList();
    }

    public DocumentoListado Datos { get; private set; }
    public int Id => Datos.IdDocumento;

    /// <summary>Lo llama la fila al cambiar el tipo, la fecha o el título en la bandeja: la pantalla lo guarda.</summary>
    public event EventHandler? Cambiado;

    public bool Marcado { get => _marcado; set { if (Fijar(ref _marcado, value)) _alMarcar(); } }

    public string Fichero => Datos.Vigente?.NombreOriginal ?? Path.GetFileName(Datos.Vigente?.Ruta ?? "");
    public string? Ruta => Datos.Vigente?.RutaCompleta;
    public string Extension => Path.GetExtension(Datos.Vigente?.Ruta ?? "").ToLowerInvariant();

    public string? Tipo
    {
        get => _tipo;
        set
        {
            if (!Fijar(ref _tipo, string.IsNullOrWhiteSpace(value) ? null : value)) return;
            Datos = Datos with { Tipo = _tipo };
            AvisarEstado();
            Cambiado?.Invoke(this, EventArgs.Empty);
        }
    }

    public DateTime? Fecha
    {
        get => _fecha;
        set
        {
            if (!Fijar(ref _fecha, value?.Date)) return;
            Datos = Datos with { FechaDocumento = _fecha };
            Avisar(nameof(FechaTexto));
            AvisarEstado();
            Cambiado?.Invoke(this, EventArgs.Empty);
        }
    }

    public string Titulo
    {
        get => _titulo;
        set
        {
            var limpio = (value ?? "").Trim();
            if (limpio.Length == 0 || !Fijar(ref _titulo, limpio)) { Avisar(); return; }
            Datos = Datos with { Titulo = _titulo };
            AvisarEstado();
            Cambiado?.Invoke(this, EventArgs.Empty);
        }
    }

    public string NombreTipo => Datos.NombreTipo ?? (Datos.Tipo is null ? "" : Datos.Tipo);
    public string FechaTexto => Datos.FechaDocumento is DateTime f ? Formatos.Fecha(f) : "";
    public DateTime FechaOrden => Datos.FechaDocumento ?? DateTime.MinValue;
    public int? Ejercicio => Datos.Ejercicio ?? Datos.FechaDocumento?.Year;

    public IReadOnlyList<VinculoVM> Vinculos { get; private set; }
    public bool SinVinculos => Vinculos.Count == 0;
    public string TextoVinculos => string.Join(" · ", Vinculos.Select(v => v.Etiqueta));

    /// <summary>En el panel de una ficha, el registro de la ficha: sus otros vínculos se ven aparte.</summary>
    public Vinculo? Excluir { get; init; }
    public string TextoOtros => Vinculos.Where(v => Excluir is null || !v.Vinculo.MismoDestino(Excluir)).Select(v => v.Etiqueta) is var o && o.Any() ? string.Join(" · ", o) : "—";
    public int NumeroVersiones => Datos.NumeroVersiones;
    public string Tamano => Datos.Vigente is { } v ? (v.TamanoBytes >= 1024 * 1024 ? $"{v.TamanoBytes / 1024m / 1024m:0.0} MB" : $"{Math.Max(1, v.TamanoBytes / 1024)} KB") : "";

    public string TextoEstado => Datos.PorClasificar ? "Por clasificar" : "Archivado";

    public bool EsCompra => Datos.Confianza == Confianza.FacturaCompra;
    public bool Listo => Datos.Listo;

    /// <summary>SEGURA, REVISAR, FALTA o COMPRA: el color de la pastilla de la propuesta.</summary>
    public string ClasePropuesta => EsCompra ? "COMPRA" : !Listo ? "FALTA" : Datos.Confianza == Confianza.Alta ? "SEGURA" : "REVISAR";

    public string TextoPropuesta => ClasePropuesta switch
    {
        "COMPRA" => "No es de aquí",
        "FALTA" => "Falta completar",
        "SEGURA" => "Segura",
        _ => "Revisar",
    };

    public string AyudaPropuesta => (Datos.Motivo.Length > 0 ? Datos.Motivo + " " : "") + (Listo ? "Lista para archivar." : ServicioDocumentos.QueFalta(Datos));

    public string AyudaFila => Datos.PorClasificar
        ? $"{Fichero}. {AyudaPropuesta}"
        : $"{Datos.Titulo} · {NombreTipo} · {FechaTexto}. {Datos.Vigente?.Ruta}. Doble clic: abrirlo";

    /// <summary>Tras volver a leer los vínculos (después de «Vincular a…»).</summary>
    internal void Actualizar(DocumentoListado datos)
    {
        Datos = datos;
        _tipo = datos.Tipo;
        _fecha = datos.FechaDocumento;
        _titulo = datos.Titulo;
        Vinculos = datos.Vinculos.Select(v => new VinculoVM(v)).ToList();
        Avisar(nameof(Tipo), nameof(Fecha), nameof(Titulo), nameof(FechaTexto), nameof(NombreTipo), nameof(Vinculos), nameof(SinVinculos), nameof(TextoVinculos),
            nameof(NumeroVersiones), nameof(Tamano), nameof(TextoOtros));
        AvisarEstado();
    }

    private void AvisarEstado() => Avisar(nameof(Listo), nameof(ClasePropuesta), nameof(TextoPropuesta), nameof(AyudaPropuesta), nameof(AyudaFila));
}
