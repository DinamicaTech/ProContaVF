using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Modelos;

namespace ProConta.Documentos.UI;

/// <summary>Un resultado del buscador de «Vincular a…», con su casilla.</summary>
public sealed class DestinoVM(DestinoVinculo destino, bool elegido, Action<DestinoVM> alCambiar) : ModeloVista
{
    private bool _elegido = elegido;

    public DestinoVinculo Destino { get; } = destino;
    public string Grupo => Destino.Grupo;
    public string Etiqueta => Destino.Vinculo.Etiqueta;
    public string Detalle => Destino.Detalle;

    public bool Elegido
    {
        get => _elegido;
        set { if (Fijar(ref _elegido, value)) alCambiar(this); }
    }

    internal void Poner(bool elegido)
    {
        _elegido = elegido;
        Avisar(nameof(Elegido));
    }
}

/// <summary>
/// «Vincular a…» (documentos 2026-11-18, maqueta validada): una sola caja que busca en modelos, ejercicios, la empresa, terceros, activos,
/// facturas y asientos («303 2t 2026», «prado», «2025»…), resultados agrupados con casilla y arriba lo elegido, con su aspa. Un documento puede
/// tener varios vínculos. Con varios documentos, lo elegido se añade a todos.
/// </summary>
public sealed class VincularVM : ModeloVista
{
    private readonly ServicioDocumentos _servicio;
    private readonly string _idSociedad;
    private string _busqueda = "";
    private IReadOnlyList<DestinoVM> _resultados = [];
    private int _consulta;
    private bool _buscando;

    public VincularVM(ServicioDocumentos servicio, string idSociedad, string titulo, string explicacion, IEnumerable<Vinculo> actuales, bool reemplaza)
    {
        _servicio = servicio;
        _idSociedad = idSociedad;
        Titulo = titulo;
        Explicacion = explicacion;
        Reemplaza = reemplaza;
        foreach (var v in actuales) Elegidos.Add(new VinculoVM(v));
        QuitarCommand = new ComandoDelegado(p => { if (p is VinculoVM v) Quitar(v.Vinculo); });
    }

    public string Titulo { get; }
    public string Explicacion { get; }

    /// <summary>Con un documento: lo elegido sustituye a sus vínculos. Con varios: se añade.</summary>
    public bool Reemplaza { get; }

    public string TextoAceptar => Reemplaza ? "Guardar los vínculos" : "Vincular";

    public ObservableCollection<VinculoVM> Elegidos { get; } = [];
    public bool NadaElegido => Elegidos.Count == 0;
    public ICommand QuitarCommand { get; }

    public string Busqueda
    {
        get => _busqueda;
        set { if (Fijar(ref _busqueda, value ?? "")) _ = BuscarAsync(); }
    }

    public IReadOnlyList<DestinoVM> Resultados { get => _resultados; private set { _resultados = value; Avisar(nameof(Resultados), nameof(SinResultados)); } }
    public bool SinResultados => !_buscando && _resultados.Count == 0;
    public bool Buscando { get => _buscando; private set { if (Fijar(ref _buscando, value)) Avisar(nameof(SinResultados)); } }

    public async Task BuscarAsync()
    {
        var mia = ++_consulta;
        Buscando = true;
        try
        {
            var lista = await _servicio.BuscarDestinosAsync(_idSociedad, _busqueda);
            if (mia != _consulta) return;
            Resultados = lista.Select(d => new DestinoVM(d, Elegidos.Any(e => e.Vinculo.MismoDestino(d.Vinculo)), AlCambiar)).ToList();
        }
        catch (Exception)
        {
            if (mia == _consulta) Resultados = [];
        }
        finally { if (mia == _consulta) Buscando = false; }
    }

    private void AlCambiar(DestinoVM d)
    {
        if (d.Elegido) { if (!Elegidos.Any(e => e.Vinculo.MismoDestino(d.Destino.Vinculo))) Elegidos.Add(new VinculoVM(d.Destino.Vinculo)); }
        else Quitar(d.Destino.Vinculo);
        Avisar(nameof(NadaElegido));
    }

    private void Quitar(Vinculo v)
    {
        foreach (var e in Elegidos.Where(e => e.Vinculo.MismoDestino(v)).ToList()) Elegidos.Remove(e);
        foreach (var r in _resultados.Where(r => r.Destino.Vinculo.MismoDestino(v))) r.Poner(false);
        Avisar(nameof(NadaElegido));
    }

    public IReadOnlyList<Vinculo> Vinculos => Elegidos.Select(e => e.Vinculo).ToList();
}
