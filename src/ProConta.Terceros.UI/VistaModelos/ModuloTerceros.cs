using ProConta.Organizacion.Geografia;
using ProConta.Terceros.UI.Logica;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Terceros.UI.VistaModelos;

/// <summary>
/// Terceros dentro del contenedor: abre la pestaña del buscador y las fichas, con un máximo de cinco (al abrir la
/// sexta se sustituye en su posición la abierta hace más tiempo sin cambios; si todas tienen cambios, se avisa).
/// </summary>
public sealed class ModuloTerceros
{
    private readonly ServiciosTerceros _s;
    private readonly IAnfitrionPestanas _anfitrion;
    private readonly IDialogos _dialogos;
    private readonly List<FichaTerceroVM> _fichas = [];
    private BuscadorTercerosVM? _buscador;
    private long _orden;
    private int _nuevas;
    private Task<CatalogoGeografico>? _geografia;

    public ModuloTerceros(ServiciosTerceros servicios, IAnfitrionPestanas anfitrion, IDialogos dialogos)
    {
        _anfitrion = anfitrion;
        _dialogos = dialogos;
        // El catálogo geográfico se carga una vez por sesión y lo comparten todas las fichas.
        _s = servicios with { Geografia = () => _geografia ??= servicios.Geografia() };
    }

    /// <summary>
    /// «Enviar email» de una dirección de la ficha (dirección, tercero y su nombre): lo conecta el contenedor con el
    /// compositor común de correo; null, el botón no está disponible.
    /// </summary>
    public Func<string, int, string, Task>? EnviarEmail { get; set; }

    public int NumeroFichas => _fichas.Count;

    public IReadOnlySet<int> IdsAbiertos() => _fichas.Where(f => f.Clave > 0).Select(f => f.Clave).ToHashSet();

    /// <summary>Abre (o activa) la pestaña del buscador de terceros.</summary>
    public void AbrirBuscador()
    {
        if (_buscador is not null)
        {
            _anfitrion.Activar(_buscador);
            _buscador.Refrescar();
            return;
        }
        _buscador = new BuscadorTercerosVM(_s, this, _dialogos);
        _anfitrion.Abrir(_buscador);
        _ = _buscador.BuscarAsync();
    }

    internal void BuscadorCerrado() => _buscador = null;

    public void AbrirFicha(int idTercero)
    {
        var ficha = Preparar(idTercero);
        if (ficha is not null) _ = ficha.CargarAsync(idTercero);
    }

    /// <param name="rol">«cliente», «proveedor», «contacto» o null (tercero sin rol).</param>
    public void NuevaFicha(string? rol)
    {
        var ficha = Preparar(null);
        if (ficha is not null) _ = ficha.NuevaAsync(rol is "cliente" or "proveedor" or "contacto" ? rol : null);
    }

    private FichaTerceroVM? Preparar(int? idTercero)
    {
        var decision = GestorFichas.Decidir(_fichas.Select(f => new FichaAbierta(f.Clave, f.OrdenApertura, f.TieneCambios)).ToList(), idTercero);
        switch (decision.Accion)
        {
            case AccionApertura.Activar:
                _anfitrion.Activar(_fichas.First(f => f.Clave == decision.ClaveSustituida));
                return null;
            case AccionApertura.Rechazar:
                var masAntigua = _fichas.OrderBy(f => f.OrdenApertura).First();
                var opcion = _dialogos.Preguntar("Cinco fichas con cambios",
                    $"Ya tienes {GestorFichas.Maximo} fichas de terceros abiertas y todas tienen cambios sin guardar, así que no abro otra para no perder nada. " +
                    "Guarda o descarta los cambios de alguna y vuelve a intentarlo.",
                    _fichas.Select(f => $"{f.Titulo}: {f.TextoCambios}").ToList(),
                    new OpcionMensaje($"Ir a {masAntigua.Titulo}", "Muestra la ficha abierta hace más tiempo para que la guardes o la cierres", Principal: true),
                    new OpcionMensaje("Cerrar", "No abre la ficha", Cancelar: true));
                if (opcion == 0) _anfitrion.Activar(masAntigua);
                return null;
        }
        var ficha = new FichaTerceroVM(_s, this, _dialogos, idTercero ?? -(++_nuevas), ++_orden);
        var sustituida = decision.Accion == AccionApertura.Sustituir ? _fichas.First(f => f.Clave == decision.ClaveSustituida) : null;
        _fichas.Add(ficha);
        _anfitrion.Abrir(ficha, sustituida);
        if (sustituida is not null) Avisar($"He cerrado la ficha de {sustituida.Titulo}, la abierta hace más tiempo sin cambios, para no pasar de {GestorFichas.Maximo} fichas.");
        return ficha;
    }

    internal void FichaCerrada(FichaTerceroVM ficha) => _fichas.Remove(ficha);

    internal void FichaGuardada(FichaTerceroVM ficha)
    {
        _s.Buscador.Invalidar();
        _buscador?.Refrescar();
    }

    internal void Avisar(string texto) => _anfitrion.Avisar(texto);
}
