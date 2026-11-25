using System.Globalization;
using ProConta.Norma43.Busqueda;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>
/// Diálogo «Buscar por importe» (norma43/busqueda-por-importe): cada línea seleccionada con sus facturas candidatas, que se marcan o
/// desmarcan con un clic. Al abrirlo vienen marcadas las ya enlazadas y las que están a un día o menos del movimiento. Una factura solo
/// puede quedar marcada en una línea: marcarla en otra la desmarca en la primera.
/// </summary>
public sealed class BusquedaImporteN43VM : ModeloVista
{
    public BusquedaImporteN43VM(IReadOnlyList<BusquedaLineaN43> busquedas, Action<CandidatoN43>? abrir)
    {
        ArgumentNullException.ThrowIfNull(busquedas);
        var premarcadas = ReglasBusquedaImporteN43.Premarcadas(busquedas);
        // Solo los movimientos con alguna factura que conciliar (propietario 2026-11-25): los demás no aparecen.
        Buscados = busquedas.Count;
        Grupos = busquedas.Where(b => b.Candidatos.Count > 0).Select(b => new GrupoBusquedaImporteN43VM(this, b,
            b.Candidatos.Select(c => new CandidatoBusquedaImporteN43VM(this, b.Linea.Id, c, premarcadas.Contains((b.Linea.Id, c.Documento.Tipo, c.Documento.Id)), abrir)).ToList())).ToList();
    }

    /// <summary>Los movimientos con alguna factura candidata; los que no tienen ninguna no se muestran.</summary>
    public IReadOnlyList<GrupoBusquedaImporteN43VM> Grupos { get; }

    /// <summary>Cuántos movimientos se buscaron (con candidatas o sin ellas).</summary>
    public int Buscados { get; }

    /// <summary>Cuántos movimientos buscados no tienen ninguna factura que conciliar y no aparecen.</summary>
    public int SinFacturas => Buscados - Grupos.Count;

    private IEnumerable<CandidatoBusquedaImporteN43VM> Todos => Grupos.SelectMany(g => g.Candidatos);

    public bool HayCandidatos => Todos.Any();

    public string Cabecera
    {
        get
        {
            if (Grupos.Count == 0)
                return Buscados == 1 ? "No he encontrado ninguna factura que pueda conciliar este movimiento."
                    : $"Ninguno de los {Buscados} movimientos tiene facturas que pueda conciliar.";
            var marca = Grupos.Count == 1 ? "Marca con un clic las facturas que paga o cobra este movimiento." : "Marca con un clic las facturas que paga o cobra cada uno.";
            if (SinFacturas == 0) return Grupos.Count == 1 ? marca : $"Los {Grupos.Count} movimientos tienen facturas que pueden conciliar. {marca}";
            return $"{(Grupos.Count == 1 ? "Se muestra 1 movimiento" : $"Se muestran {Grupos.Count} movimientos")} de los {Buscados} elegidos; " +
                $"{(SinFacturas == 1 ? "el otro no tiene" : $"los otros {SinFacturas} no tienen")} ninguna factura que pueda conciliar. {marca}";
        }
    }

    public string Criterios =>
        $"Cargos: facturas de compra a ±{ReglasBusquedaImporteN43.DiasCompra} días. Abonos: facturas de venta de hasta {ReglasBusquedaImporteN43.DiasVentaAntes} días antes " +
        $"o {ReglasBusquedaImporteN43.DiasVentaDespues} después. Importe ±{ReglasBusquedaImporteN43.Tolerancia.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €, " +
        $"sin enlazar con otro movimiento; como mucho {ReglasBusquedaImporteN43.Maximo} por movimiento, de la más cercana en fecha a la más lejana. " +
        $"Vienen marcadas las ya enlazadas y las que están a {ReglasBusquedaImporteN43.DiasMarcado} día o menos.";

    /// <summary>Lo que cambia respecto a como estaba: las marcadas sin enlazar y las enlazadas que se han desmarcado.</summary>
    public IReadOnlyList<CambioEnlaceN43> Cambios() =>
        Todos.Where(c => c.Marcado != c.Datos.Enlazada).Select(c => new CambioEnlaceN43(c.IdLinea, c.Datos.Documento.Tipo, c.Datos.Documento.Id, c.Marcado)).ToList();

    public bool HayCambios => Cambios().Count > 0;

    public string Resumen
    {
        get
        {
            var cambios = Cambios();
            var enlazar = cambios.Count(c => c.Enlazar);
            var quitar = cambios.Count - enlazar;
            if (cambios.Count == 0) return "Sin cambios: marca alguna factura para enlazarla.";
            var partes = new List<string>();
            if (enlazar > 0) partes.Add($"Se enlaza{(enlazar > 1 ? "n" : "")} {Formatos.Plural(enlazar, "factura", "facturas")}");
            if (quitar > 0) partes.Add($"{(partes.Count == 0 ? "Se" : "se")} desenlaza{(quitar > 1 ? "n" : "")} {Formatos.Plural(quitar, "factura", "facturas")}");
            return string.Join(" y ", partes) + ".";
        }
    }

    /// <summary>Al marcar una factura en una línea, se desmarca en las demás (solo puede ir con un movimiento).</summary>
    internal void AlMarcar(CandidatoBusquedaImporteN43VM marcada)
    {
        if (marcada.Marcado)
            foreach (var otra in Todos.Where(c => !ReferenceEquals(c, marcada) && c.Marcado && c.Datos.Documento.Tipo == marcada.Datos.Documento.Tipo
                         && c.Datos.Documento.Id == marcada.Datos.Documento.Id))
                otra.FijarSinAvisar(false);
        Avisar(nameof(Resumen), nameof(HayCambios));
    }
}

/// <summary>Una línea del diálogo con sus candidatas.</summary>
public sealed class GrupoBusquedaImporteN43VM(BusquedaImporteN43VM padre, BusquedaLineaN43 busqueda, IReadOnlyList<CandidatoBusquedaImporteN43VM> candidatos)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public BusquedaImporteN43VM Padre { get; } = padre;
    public IReadOnlyList<CandidatoBusquedaImporteN43VM> Candidatos { get; } = candidatos;

    public string Titulo => $"{Formatos.Fecha(busqueda.Linea.FechaOperacion)} · {(busqueda.Linea.EsCargo ? "Cargo" : "Abono")} de {busqueda.Linea.Importe.ToString("N2", Es)} € · " +
        $"{busqueda.Linea.NombreBanco}";

    public string Concepto => busqueda.Linea.Concepto ?? "";

    public bool SinCandidatos => Candidatos.Count == 0;

    public string TextoSinCandidatos => busqueda.Linea.EsCargo
        ? $"Ninguna factura de compra de {busqueda.Linea.Importe.ToString("N2", Es)} € (±0,02) a ±{ReglasBusquedaImporteN43.DiasCompra} días sin enlazar. Si existe, revisa su importe total o su fecha en Compras."
        : $"Ninguna factura de venta de {busqueda.Linea.Importe.ToString("N2", Es)} € (±0,02) en los {ReglasBusquedaImporteN43.DiasVentaAntes} días anteriores sin enlazar. Si es un cobro de varias facturas, búscalas en Facturas.";
}

/// <summary>Una factura candidata: se marca o desmarca con un clic.</summary>
public sealed class CandidatoBusquedaImporteN43VM(BusquedaImporteN43VM padre, int idLinea, CandidatoN43 datos, bool marcado, Action<CandidatoN43>? abrir) : ModeloVista
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private bool _marcado = marcado;

    public int IdLinea { get; } = idLinea;
    public CandidatoN43 Datos { get; } = datos;

    public bool Marcado
    {
        get => _marcado;
        set { if (Fijar(ref _marcado, value)) padre.AlMarcar(this); }
    }

    internal void FijarSinAvisar(bool valor) { _marcado = valor; Avisar(nameof(Marcado)); }

    public void Alternar() => Marcado = !Marcado;

    public bool PuedeAbrir => abrir is not null;
    public void Abrir() => abrir?.Invoke(Datos);

    public string Numero => Datos.Documento.Numero;
    public string Fecha => Formatos.Fecha(Datos.Documento.Fecha);
    public string Dias => Datos.Dias switch
    {
        0 => "mismo día",
        > 0 => $"{Datos.Dias} d antes",
        _ => $"{-Datos.Dias} d después",
    };
    public string Importe => Formatos.Importe(Datos.Documento.Importe);
    /// <summary>Diferencia en céntimos; vacía si no llega a medio céntimo (las compras guardan más decimales).</summary>
    public string Diferencia => Math.Round(Datos.Diferencia, 2) == 0 ? "" : Math.Round(Datos.Diferencia, 2).ToString("+0.00;-0.00", Es);
    public string Tercero => string.Join(" · ", new[] { Datos.Documento.Nif, Datos.Documento.Tercero }.Where(t => !string.IsNullOrWhiteSpace(t)));
    public string Descripcion => Datos.Documento.Descripcion ?? "";
    public string Estado => Datos.Enlazada ? "Ya enlazada" : "";

    public string Ayuda =>
        $"{(Datos.Documento.Tipo == TipoDocumentoN43.Compra ? "Factura de compra" : "Factura de venta")} {Numero} del {Fecha} ({Dias} del movimiento), " +
        $"{Importe} €{(Diferencia.Length == 0 ? "" : $", {Diferencia} € de diferencia")}. " +
        (Datos.Enlazada ? "Ya está enlazada con este movimiento: desmárcala para quitar el enlace. " : "Clic: marcarla o desmarcarla. ") +
        (PuedeAbrir ? "Doble clic o «Ver»: abrirla." : "");
}
