using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.CentrosCoste;

/// <summary>Centro de coste jerárquico de una sociedad (tabla CentrosCoste).</summary>
public sealed record CentroCoste(
    int IdCentroCoste,
    string IdSociedad,
    int? IdCentroCostePadre,
    string Codigo,
    string Nombre,
    bool EsImputable,
    bool EstaActivo,
    int Nivel,
    string RutaJerarquica);

/// <summary>
/// Contrato de centros de coste: listar la jerarquía de la sociedad, comprobar que un centro admite
/// apuntes y dar de alta centros con su ruta jerárquica calculada.
/// </summary>
public interface ICatalogoCentrosCoste
{
    Task<IReadOnlyList<CentroCoste>> ListarAsync(string idSociedad);
    Task<CentroCoste?> ObtenerAsync(int idCentroCoste);

    /// <summary>El centro existe, es de la sociedad, está activo y es imputable.</summary>
    Task<Resultado> ComprobarImputableAsync(string idSociedad, int idCentroCoste);

    Task<Resultado<CentroCoste>> AltaAsync(string idSociedad, string codigo, string nombre, int? idPadre = null, bool esImputable = true);
}

public sealed class CatalogoCentrosCosteAccess(IBaseDatos bd) : ICatalogoCentrosCoste
{
    public const char SeparadorRuta = '/';

    private const string Columnas = "IdCentroCoste, IdSociedad, IdCentroCostePadre, Codigo, Nombre, EsImputable, EstaActivo, Nivel, RutaJerarquica";

    public Task<IReadOnlyList<CentroCoste>> ListarAsync(string idSociedad) =>
        bd.EjecutarAsync(s => s.ConsultarAsync($"SELECT {Columnas} FROM CentrosCoste WHERE IdSociedad = ? ORDER BY RutaOrden", Mapear, idSociedad));

    public Task<CentroCoste?> ObtenerAsync(int idCentroCoste) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM CentrosCoste WHERE IdCentroCoste = ?", Mapear, idCentroCoste));

    public async Task<Resultado> ComprobarImputableAsync(string idSociedad, int idCentroCoste)
    {
        var centro = await ObtenerAsync(idCentroCoste);
        string? causa = centro is null || !string.Equals(centro.IdSociedad, idSociedad, StringComparison.OrdinalIgnoreCase)
            ? "no existe en esta sociedad"
            : !centro.EstaActivo ? "está inactivo" : !centro.EsImputable ? "no es imputable (agrupa otros centros)" : null;
        return causa is null
            ? Resultado.Correcto()
            : Resultado.Fallo(new Error("CentrosCoste.NoImputable", $"El centro de coste {centro?.Codigo ?? idCentroCoste.ToString()} no admite apuntes.",
                $"Centro de coste {idCentroCoste}", $"El centro {causa}.", "Elige un centro de coste activo e imputable de esta sociedad."));
    }

    public Task<Resultado<CentroCoste>> AltaAsync(string idSociedad, string codigo, string nombre, int? idPadre = null, bool esImputable = true) =>
        bd.EnTransaccionAsync(async s =>
        {
            var cod = (codigo ?? "").Trim();
            var nom = (nombre ?? "").Trim();
            if (cod.Length is 0 or > 30 || cod.Contains(SeparadorRuta) || nom.Length is 0 or > 150)
                return Resultado<CentroCoste>.Fallo(new Error("CentrosCoste.DatosNoValidos", "El código o el nombre del centro de coste no son válidos.",
                    $"Centro {cod}", $"El código tiene de 1 a 30 caracteres sin «{SeparadorRuta}» y el nombre de 1 a 150.", "Corrige el código o el nombre."));

            CentroCoste? padre = null;
            if (idPadre is int id)
            {
                padre = await s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM CentrosCoste WHERE IdCentroCoste = ?", Mapear, id);
                if (padre is null || !string.Equals(padre.IdSociedad, idSociedad, StringComparison.OrdinalIgnoreCase))
                    return Resultado<CentroCoste>.Fallo(new Error("CentrosCoste.PadreNoValido", "El centro padre no existe en esta sociedad.",
                        $"Centro {cod}", "La jerarquía es de cada sociedad.", "Elige un centro padre de la misma sociedad."));
            }
            var ruta = padre is null ? cod : padre.RutaJerarquica + SeparadorRuta + cod;
            var nivel = padre is null ? 1 : padre.Nivel + 1;
            var hermanos = Convert.ToInt32(padre is null
                ? await s.EscalarAsync("SELECT COUNT(*) FROM CentrosCoste WHERE IdSociedad = ? AND IdCentroCostePadre IS NULL", idSociedad)
                : await s.EscalarAsync("SELECT COUNT(*) FROM CentrosCoste WHERE IdSociedad = ? AND IdCentroCostePadre = ?", idSociedad, padre.IdCentroCoste));
            var rutaOrden = (padre is null ? "" : await s.EscalarAsync("SELECT RutaOrden FROM CentrosCoste WHERE IdCentroCoste = ?", padre.IdCentroCoste) + "/") + (hermanos + 1).ToString("D4");
            try
            {
                await s.EjecutarAsync(
                    "INSERT INTO CentrosCoste (IdSociedad, IdCentroCostePadre, Codigo, Nombre, EsImputable, EstaActivo, Orden, Nivel, RutaJerarquica, RutaOrden, IdCentroCosteRaiz) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    idSociedad, padre?.IdCentroCoste, cod, nom, esImputable, true, (short)(hermanos + 1), (byte)nivel, ruta, rutaOrden, null);
            }
            catch (UnicidadException)
            {
                return Resultado<CentroCoste>.Fallo(new Error("CentrosCoste.Repetido", $"Ya existe el centro de coste {ruta} en esta sociedad.",
                    $"Centro {ruta}", "La ruta jerárquica es única en cada sociedad.", "Usa el centro que ya existe o elige otro código."));
            }
            var nuevo = await s.UltimoIdAsync();
            var raiz = padre is null ? nuevo : await s.EscalarAsync("SELECT IIF(IdCentroCosteRaiz IS NULL, IdCentroCoste, IdCentroCosteRaiz) FROM CentrosCoste WHERE IdCentroCoste = ?", padre.IdCentroCoste);
            await s.EjecutarAsync("UPDATE CentrosCoste SET IdCentroCosteRaiz = ? WHERE IdCentroCoste = ?", Convert.ToInt32(raiz), nuevo);
            return Resultado<CentroCoste>.Correcto(new CentroCoste(nuevo, idSociedad, padre?.IdCentroCoste, cod, nom, esImputable, true, nivel, ruta));
        });

    private static CentroCoste Mapear(IFilaDatos f) => new(
        f.Entero("IdCentroCoste"), f.Texto("IdSociedad") ?? "", f.EnteroNulo("IdCentroCostePadre"), f.Texto("Codigo") ?? "", f.Texto("Nombre") ?? "",
        f.Logico("EsImputable"), f.Logico("EstaActivo"), f.Entero("Nivel"), f.Texto("RutaJerarquica") ?? "");
}
