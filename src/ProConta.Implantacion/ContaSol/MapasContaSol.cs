using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Implantacion.ContaSol;

/// <summary>
/// Lectura de los mapas de identidad que publica el importador de ContaSol, para la migración del ERP
/// (implantacion/migracion-erp): de una clave de ContaSol (código de cuenta, «DEP/SUB», «DIA:ASI», código de IVA, código
/// de cliente o proveedor) al Id de ProContaVF. Vale la última importación correcta o con incidencias de cada ejercicio
/// hecha con la versión actual del importador.
/// </summary>
public sealed class MapasContaSol(IBaseDatos bd)
{
    /// <summary>Mapa de un tipo (<see cref="TiposMapa"/>) de un ejercicio: clave de ContaSol → Id de ProContaVF.</summary>
    public Task<IReadOnlyDictionary<string, int>> LeerAsync(string idSociedad, int ejercicio, string tipo) =>
        bd.EjecutarAsync<IReadOnlyDictionary<string, int>>(async s =>
        {
            var ultima = await s.EscalarAsync(
                "SELECT MAX(i.IdImportacionContaSol) FROM ImportacionesContaSol AS i INNER JOIN SociedadesEjercicios AS se ON i.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                "WHERE se.IdSociedad = ? AND se.Ejercicio = ? AND i.VersionImportador = ? AND i.Estado IN (?, ?)",
                idSociedad, ejercicio.ToString(CultureInfo.InvariantCulture), ImportadorContaSol.VersionImportador,
                EstadosImportacion.Completada, EstadosImportacion.ConIncidencias);
            if (ultima is null or DBNull) return new Dictionary<string, int>(StringComparer.Ordinal);
            var filas = await s.ConsultarAsync(
                "SELECT ClaveOrigen, IdDestino FROM MapasIdentidadContaSol WHERE IdImportacionContaSol = ? AND TipoEntidad = ? ORDER BY IdMapaIdentidadContaSol",
                f => (Clave: f.Texto("ClaveOrigen") ?? "", Id: f.Entero("IdDestino")), Convert.ToInt32(ultima, CultureInfo.InvariantCulture), tipo);
            var mapa = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (clave, id) in filas) mapa[clave] = id;
            return mapa;
        });
}
