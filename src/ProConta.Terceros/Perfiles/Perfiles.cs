using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.Perfiles;

/// <summary>Perfil del catálogo (tabla Perfiles): clasificación libre de terceros, independiente de los roles; no concede permisos.</summary>
public sealed record Perfil(int IdPerfil, string Nombre, string? Descripcion, bool Activo);

/// <summary>
/// Asignación de un perfil a un tercero (tabla PerfilesTerceros, RF-TER-12). Retirar la marca inactiva con fecha de
/// retirada; reasignar reactiva la misma fila. Se guarda con la ficha.
/// </summary>
public sealed class AsignacionPerfil(int idPerfil, string nombre, string? descripcion, bool perfilActivo)
{
    public int IdPerfil { get; } = idPerfil;
    public string Nombre { get; } = nombre;
    public string? Descripcion { get; } = descripcion;

    /// <summary>Si el perfil sigue activo en el catálogo; uno inactivo no admite asignaciones nuevas.</summary>
    public bool PerfilActivo { get; } = perfilActivo;

    public bool Activo { get; set; }
    public DateTime? FechaAsignacion { get; internal set; }
    public DateTime? FechaRetirada { get; internal set; }

    /// <summary>Aún no existe su fila en la BD.</summary>
    public bool EsNueva => FechaAsignacion is null;

    internal AsignacionPerfil Clonar() => (AsignacionPerfil)MemberwiseClone();
}

/// <summary>Catálogo de perfiles (dueño: terceros/perfiles-notas).</summary>
public interface ICatalogoPerfiles
{
    Task<IReadOnlyList<Perfil>> ListarAsync();

    /// <summary>Alta de un perfil nuevo en el catálogo (nombre único).</summary>
    Task<Resultado<Perfil>> AltaAsync(string nombre, string? descripcion);
}

public sealed class CatalogoPerfilesAccess(IBaseDatos bd) : ICatalogoPerfiles
{
    public const int LongitudMaximaNombre = 100;

    public Task<IReadOnlyList<Perfil>> ListarAsync() =>
        bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdPerfil, Nombre, Descripcion, Activo FROM Perfiles ORDER BY Nombre",
            f => new Perfil(f.Entero("IdPerfil"), f.Texto("Nombre") ?? "", f.Texto("Descripcion"), f.Logico("Activo"))));

    public Task<Resultado<Perfil>> AltaAsync(string nombre, string? descripcion) =>
        bd.EnTransaccionAsync(async s =>
        {
            var n = (nombre ?? "").Trim();
            var d = string.IsNullOrWhiteSpace(descripcion) ? null : descripcion.Trim();
            if (n.Length == 0 || n.Length > LongitudMaximaNombre)
                return Resultado<Perfil>.Fallo(new Error("Perfiles.NombreNoValido", "Falta el nombre del perfil o es demasiado largo.", "Perfil nuevo",
                    $"Debe tener entre 1 y {LongitudMaximaNombre} caracteres.", "Escribe un nombre corto, por ejemplo «Proveedor homologado»."));
            var existente = await s.ConsultarPrimeroAsync("SELECT IdPerfil, Nombre, Descripcion, Activo FROM Perfiles WHERE Nombre = ?",
                f => new Perfil(f.Entero("IdPerfil"), f.Texto("Nombre") ?? "", f.Texto("Descripcion"), f.Logico("Activo")), n);
            if (existente is not null)
                return Resultado<Perfil>.Fallo(new Error("Perfiles.Repetido", $"Ya existe el perfil «{existente.Nombre}».", $"Perfil {existente.IdPerfil}",
                    "El nombre del perfil es único.", existente.Activo ? "Asígnalo desde la lista de disponibles." : "Ese perfil está retirado del catálogo; elige otro nombre."));
            await s.EjecutarAsync("INSERT INTO Perfiles (Nombre, Descripcion, Activo) VALUES (?, ?, ?)", n, d, true);
            return Resultado<Perfil>.Correcto(new Perfil(await s.UltimoIdAsync(), n, d, true));
        });
}
