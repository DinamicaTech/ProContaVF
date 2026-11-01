using ProConta.Datos.Persistencia;

namespace ProConta.Seguridad.Permisos;

/// <summary>
/// Política común por ámbito (RF-SEG-04): si el ámbito no tiene ninguna asignación, todo operador
/// válido tiene permiso; en cuanto tiene alguna, solo los grupos asignados. Un grupo sin filas en un
/// ámbito configurado no hereda acceso. Una tabla ausente o ilegible no es «ámbito sin configurar»:
/// la excepción de datos se propaga.
/// </summary>
public static class PoliticaAmbito
{
    /// <param name="asignaciones">Pares (grupo, recurso) de todo el ámbito.</param>
    public static bool Permite<TRecurso>(IReadOnlyCollection<(int Grupo, TRecurso Recurso)> asignaciones, IEnumerable<int> gruposOperador, TRecurso recurso, IEqualityComparer<TRecurso>? comparador = null)
    {
        if (asignaciones.Count == 0) return true;
        comparador ??= EqualityComparer<TRecurso>.Default;
        var grupos = gruposOperador.ToHashSet();
        return asignaciones.Any(a => grupos.Contains(a.Grupo) && comparador.Equals(a.Recurso, recurso));
    }
}

/// <summary>Ámbito «acceso a sociedades» (tabla AccesoSociedadesGrupo).</summary>
public sealed class AutorizadorSociedades(IBaseDatos bd, ServicioOperadores operadores)
{
    /// <summary>Filtra las sociedades a las que el operador puede acceder.</summary>
    public async Task<IReadOnlyList<string>> FiltrarAsync(int idOperador, IEnumerable<string> sociedades)
    {
        var asignaciones = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdGrupoOperadores, IdSociedad FROM AccesoSociedadesGrupo",
            f => (Grupo: f.Entero("IdGrupoOperadores"), Recurso: f.Texto("IdSociedad") ?? "")));
        var grupos = asignaciones.Count == 0 ? [] : await operadores.GruposActivosAsync(idOperador);
        return sociedades.Where(id => PoliticaAmbito.Permite(asignaciones, grupos, id, StringComparer.OrdinalIgnoreCase)).ToList();
    }
}

/// <summary>
/// Acciones con permiso propio (seguridad/permisos 2026-11-01 16:20): cada código de acción es un ámbito con la
/// regla común, en la tabla PermisosAccionesGrupo. Una acción sin filas la puede hacer cualquier operador; con alguna,
/// solo los grupos activos del operador que la tienen. Lo consultan los servicios, no solo las pantallas.
/// </summary>
public interface IAutorizadorAcciones
{
    Task<bool> PuedeAsync(string codigoAccion);
}

/// <summary>Códigos de las acciones que se autorizan hoy.</summary>
public static class Acciones
{
    public const string FacturasEditar = "FACTURAS.EDITAR";
    public const string FacturasValidar = "FACTURAS.VALIDAR";
    public const string FacturasRectificar = "FACTURAS.RECTIFICAR";
    public const string MantenimientoEditar = "MANTENIMIENTO.EDITAR";
}

/// <summary>Autoriza para un operador concreto (el de la sesión).</summary>
public sealed class AutorizadorAccionesAccess(IBaseDatos bd, ServicioOperadores operadores, int idOperador) : IAutorizadorAcciones
{
    private IReadOnlyList<int>? _grupos;

    public async Task<bool> PuedeAsync(string codigoAccion)
    {
        var asignaciones = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdGrupoOperadores, CodigoAccion FROM PermisosAccionesGrupo WHERE CodigoAccion = ?",
            f => (Grupo: f.Entero("IdGrupoOperadores"), Recurso: f.Texto("CodigoAccion") ?? ""), codigoAccion));
        if (asignaciones.Count == 0) return true;
        _grupos ??= await operadores.GruposActivosAsync(idOperador);
        return PoliticaAmbito.Permite(asignaciones, _grupos, codigoAccion, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Autorizador que lo permite todo (pruebas y procesos internos).</summary>
public sealed class AutorizadorTodo : IAutorizadorAcciones
{
    public static readonly AutorizadorTodo Instancia = new();
    public Task<bool> PuedeAsync(string codigoAccion) => Task.FromResult(true);
}
