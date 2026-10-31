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
