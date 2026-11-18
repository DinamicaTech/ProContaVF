using ProConta.Contabilidad.Modelos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.ModelosTributarios;

/// <summary>
/// Pestaña con el detalle de un periodo de un modelo (contabilidad/modelos-tributarios): sus casillas (o, en las informativas, sus
/// registros), lo que impide presentarlo y los avisos. Sustituye a la antigua pantalla «Modelo 303» (decisión 1 del propietario del
/// 2026-11-18 05:34): se abre desde Modelos tributarios, como detalle del periodo.
/// </summary>
public sealed class CasillasModeloVM(DetalleModelo detalle, string corto, string razonSocial) : ModeloVista, IPestanaTrabajo
{
    private object? _vista;

    public DetalleModelo Detalle { get; } = detalle;
    public string Titulo => $"Casillas {corto}";
    public string Ayuda => $"{Detalle.Titulo} · {razonSocial}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaCasillasModelo { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() { }

    public string TituloDetalle => Detalle.Titulo;
    public string Subtitulo => razonSocial;
    public IReadOnlyList<CasillaModelo> Casillas => Detalle.Casillas;
    public IReadOnlyList<string> Bloqueos => Detalle.Bloqueos;
    public IReadOnlyList<string> Avisos => Detalle.Avisos;
    public bool HayBloqueos => Bloqueos.Count > 0;
    public bool HayAvisos => Avisos.Count > 0;
    public string Nota => Detalle.Nota ?? "";
    public bool HayNota => !string.IsNullOrWhiteSpace(Detalle.Nota);
    public bool SinCasillas => Casillas.Count == 0;
}
