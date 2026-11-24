using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.UI.Logica;
using ProConta.UI.Base.Comandos;

namespace ProConta.Terceros.UI.VistaModelos;

/// <summary>Grupo que se puede marcar en el rol «Operador ProConta» de la ficha.</summary>
public sealed class GrupoOperadorVM : Observable
{
    private readonly FichaTerceroVM _ficha;
    private bool _marcado;

    internal GrupoOperadorVM(FichaTerceroVM ficha, GrupoOperadorFicha grupo, bool marcado)
    {
        _ficha = ficha;
        Grupo = grupo;
        _marcado = marcado;
    }

    internal GrupoOperadorFicha Grupo { get; }
    public int IdGrupo => Grupo.IdGrupo;
    public string Nombre => Grupo.Nombre;
    public string Ayuda => string.IsNullOrWhiteSpace(Grupo.Descripcion)
        ? $"Marcado, el operador es del grupo «{Grupo.Nombre}» y tiene sus permisos al guardar la ficha"
        : $"{Grupo.Descripcion.Trim()}. Marcado, el operador es de este grupo al guardar la ficha";

    public bool Marcado
    {
        get => _marcado;
        set { if (Fijar(ref _marcado, value)) _ficha.OperadorCambiado(); }
    }

    internal void Poner(bool marcado)
    {
        _marcado = marcado;
        Avisar(nameof(Marcado));
    }
}

/// <summary>
/// Rol «Operador ProConta» de la ficha (seguridad/pantallas 2026-11-24): solo lo ven y lo cambian quienes pueden gestionar operadores.
/// Se guarda con la ficha, justo después del tercero; si el rol no se puede guardar, la ficha queda guardada y el cambio del rol sigue pendiente.
/// </summary>
public sealed partial class FichaTerceroVM
{
    private EstadoOperadorFicha? _operador;
    private bool _puedeOperadores;
    private bool _operadorActivo;
    private ICommand? _cambiarContrasenaOperador;

    /// <summary>Grupos activos, con los del operador marcados.</summary>
    public ObservableCollection<GrupoOperadorVM> GruposOperador { get; } = [];

    /// <summary>La ficha muestra el rol: el contenedor lo ha conectado y el operador de la sesión puede gestionar operadores.</summary>
    public bool MostrarOperador => _modulo?.Operadores is not null && _puedeOperadores;

    public bool OperadorActivo => _operadorActivo;
    public string IdOperador => _operador?.IdOperador.ToString() ?? (_operadorActivo ? "al guardar" : "");

    /// <summary>Nombre con el que entra: el guardado, o el que tendrá (su nombre de pila) si aún no es operador.</summary>
    public string NombreAccesoOperador => _operador?.NombreAcceso ?? (string.IsNullOrWhiteSpace(T.NombrePersona) ? "" : T.NombrePersona.Trim());

    public string EstadoOperador => _operadorActivo ? "" : _operador is null
        ? "No entra en ProContaVF."
        : "Desactivado: ya no puede entrar en ProContaVF; su historial de accesos se conserva.";

    public string TextoContrasenaOperador => _operador is null
        ? "La contraseña se pone después de guardar la ficha (es opcional: sin ella, entra con la contraseña vacía)."
        : _operador.TieneContrasena ? "Tiene contraseña." : "Sin contraseña: entra con la contraseña vacía.";

    /// <summary>Solo una persona física con nombre puede ser operador.</summary>
    public bool PuedeActivarOperador => T.TipoPersona == TipoPersona.Fisica;

    public string AyudaActivarOperador => PuedeActivarOperador
        ? "Al guardar, esta persona podrá entrar en ProContaVF con su nombre de pila (con los apellidos si otro operador se llama igual), con los grupos que marques"
        : "Solo una persona física puede ser operador: cambia el tipo de persona en la pestaña Identidad";

    public ICommand CambiarContrasenaOperadorCommand => _cambiarContrasenaOperador ??= new ComandoDelegado(
        () => _ = CambiarContrasenaOperadorAsync(), () => _operador is { Activo: true } && _operadorActivo && !Guardando);

    private ICommand? _pasarOperador;

    /// <summary>Pasa aquí un operador que está en otra ficha (p. ej. una ficha duplicada que creó la migración). Al momento.</summary>
    public ICommand PasarOperadorCommand => _pasarOperador ??= new ComandoDelegado(
        () => _ = PasarOperadorAsync(), () => PuedePasarOperador && !Guardando);

    /// <summary>Ficha guardada de una persona física que nunca ha sido operador y sin cambios pendientes en el rol.</summary>
    public bool PuedePasarOperador => _operador is null && !_operadorActivo && !T.EsNuevo && T.TipoPersona == TipoPersona.Fisica;

    private async Task PasarOperadorAsync()
    {
        if (_modulo?.Operadores is not { } operadores) return;
        try
        {
            if (await operadores.PasarOperadorAsync(T.IdTercero, NombreCabecera) is not { } aviso) return;
            _operador = await operadores.ObtenerAsync(T.IdTercero);
            _operadorActivo = _operador?.Activo ?? false;
            foreach (var g in GruposOperador) g.Poner(_operador?.Grupos.Contains(g.IdGrupo) ?? false);
            AvisarOperador();
            Modificado();
            AvisoEstado(aviso);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AvisoEstado($"No he podido pasar el operador a esta ficha ({ex.Message}). Vuelve a intentarlo.");
        }
    }

    private InsigniaRol? InsigniaOperador() => MostrarOperador && _operadorActivo
        ? new InsigniaRol("operador", "Operador", _operador?.NombreAcceso ?? "nuevo", "Entra en ProContaVF con este nombre. Pulsa para ir al rol en la pestaña Roles")
        : null;

    /// <summary>Lee el rol del tercero (null en una ficha nueva) y los grupos. Si falla, la ficha no muestra el rol.</summary>
    private async Task CargarOperadorAsync(int? idTercero)
    {
        _operador = null;
        _operadorActivo = false;
        GruposOperador.Clear();
        _puedeOperadores = false;
        if (_modulo?.Operadores is not { } operadores) return;
        try
        {
            _puedeOperadores = await operadores.PuedeGestionarAsync();
            if (_puedeOperadores)
            {
                _operador = idTercero is int id ? await operadores.ObtenerAsync(id) : null;
                _operadorActivo = _operador?.Activo ?? false;
                foreach (var g in await operadores.GruposAsync())
                    GruposOperador.Add(new GrupoOperadorVM(this, g, _operador?.Grupos.Contains(g.IdGrupo) ?? false));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _puedeOperadores = false;
            AvisoEstado($"No he podido leer si es operador de ProContaVF ({ex.Message}): el rol no se muestra en esta ficha.");
        }
        AvisarOperador();
    }

    /// <summary>Descartar: vuelve al rol guardado.</summary>
    private void RestaurarOperador()
    {
        _operadorActivo = _operador?.Activo ?? false;
        foreach (var g in GruposOperador) g.Poner(_operador?.Grupos.Contains(g.IdGrupo) ?? false);
        AvisarOperador();
    }

    private void CambiarOperador(bool activo)
    {
        if (_operadorActivo == activo) return;
        _operadorActivo = activo;
        OperadorCambiado();
    }

    internal void OperadorCambiado()
    {
        Tocar(this, "Operador");
        AvisarOperador();
        Modificado();
    }

    private void AvisarOperador() =>
        Avisar(nameof(MostrarOperador), nameof(OperadorActivo), nameof(IdOperador), nameof(NombreAccesoOperador), nameof(EstadoOperador),
            nameof(TextoContrasenaOperador), nameof(PuedeActivarOperador), nameof(AyudaActivarOperador), nameof(PuedePasarOperador), nameof(Insignias));

    private HashSet<int> GruposMarcados() => GruposOperador.Where(g => g.Marcado).Select(g => g.IdGrupo).ToHashSet();

    private bool HayCambiosOperador => MostrarOperador && CambiosOperador().Any();

    /// <summary>Cambios pendientes del rol, para el pie («Ver cuáles») y la marca de la pestaña Roles.</summary>
    private IEnumerable<CambioFicha> CambiosOperador()
    {
        if (!MostrarOperador) yield break;
        var activoAntes = _operador?.Activo ?? false;
        if (activoAntes != _operadorActivo) yield return new(PestanaFicha.Roles, _operadorActivo ? "Rol Operador ProConta activado" : "Rol Operador ProConta desactivado");
        if (!_operadorActivo) yield break;
        var antes = (_operador?.Grupos ?? []).ToHashSet();
        foreach (var g in GruposOperador)
            if (g.Marcado != antes.Contains(g.IdGrupo))
                yield return new(PestanaFicha.Roles, $"Operador · {(g.Marcado ? "en" : "fuera de")} el grupo {g.Nombre}");
    }

    /// <summary>Antes de guardar la ficha: el rol se podrá guardar (permiso, persona física, administradores).</summary>
    private async Task<Resultado> ComprobarOperadorAsync()
    {
        if (!HayCambiosOperador || _modulo?.Operadores is not { } operadores) return Resultado.Correcto();
        if (_operadorActivo && (T.TipoPersona != TipoPersona.Fisica || string.IsNullOrWhiteSpace(T.NombrePersona)))
            return Resultado.Fallo(new Error("Seguridad.OperadorNoPersona", "Solo una persona física con nombre puede ser operador de ProContaVF.", null,
                "El nombre con el que se entra sale del nombre de pila de la persona.", "Pon el tipo «Persona física» y su nombre en la pestaña Identidad, o desactiva el rol de operador."));
        try
        {
            return await operadores.GuardarAsync(T.EsNuevo ? 0 : T.IdTercero, _operadorActivo, GruposMarcados(), soloComprobar: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Resultado.Fallo(new Error("Seguridad.OperadorNoComprobado", $"No he podido comprobar el rol de operador ({ex.Message}).", null, null,
                "Vuelve a pulsar Guardar; si sigue fallando, desactiva el cambio del rol y guarda el resto."));
        }
    }

    /// <summary>
    /// Después de guardar el tercero: guarda el rol y lo vuelve a leer. Si no se puede, el cambio del rol sigue pendiente y devuelve el aviso.
    /// </summary>
    private async Task<string?> GuardarOperadorAsync(int idTercero)
    {
        if (!HayCambiosOperador || _modulo?.Operadores is not { } operadores) return null;
        var activo = _operadorActivo;
        var grupos = GruposMarcados();
        try
        {
            var r = await operadores.GuardarAsync(idTercero, activo, grupos, soloComprobar: false);
            if (!r.EsCorrecto) return $"No he guardado el rol de operador: {r.Error!.Mensaje} {r.Error.AccionCorrectora} El cambio sigue pendiente en la pestaña Roles.";
            await CargarOperadorAsync(idTercero);
            return activo
                ? $"{_operador?.NombreAcceso ?? "Esta persona"} ya puede entrar en ProContaVF{(_operador?.TieneContrasena == true ? "" : " (sin contraseña: puedes ponerle una con «Contraseña…»)")}."
                : "Ya no puede entrar en ProContaVF.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"No he guardado el rol de operador ({ex.Message}). El cambio sigue pendiente en la pestaña Roles: vuelve a pulsar Guardar.";
        }
        finally
        {
            // Si no se ha guardado, se mantiene lo elegido (la lectura de arriba solo se hace si se guardó).
            if (_operadorActivo != activo || !GruposMarcados().SetEquals(grupos))
            {
                _operadorActivo = activo;
                foreach (var g in GruposOperador) g.Poner(grupos.Contains(g.IdGrupo));
            }
            AvisarOperador();
        }
    }

    private async Task CambiarContrasenaOperadorAsync()
    {
        if (_modulo?.Operadores is not { } operadores || _operador is not { } op) return;
        try
        {
            if (await operadores.CambiarContrasenaAsync(op.IdOperador, op.NombreAcceso) is not { } aviso) return;
            // Solo se relee el estado guardado: los cambios pendientes del rol (grupos) se conservan.
            _operador = await operadores.ObtenerAsync(T.IdTercero) ?? op;
            AvisarOperador();
            AvisoEstado(aviso);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AvisoEstado($"No he podido cambiar la contraseña ({ex.Message}). Vuelve a intentarlo.");
        }
    }
}
