using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>
/// Semilla del modelo PGC Pymes 2008 de los balances: copia exacta de ModelosBalance, NodosBalance y ReglasCuentasBalance de la
/// maestra (2 modelos, 89 nodos y 139 reglas), en los ficheros incrustados Semillas/*.tsv (separados por tabuladores).
/// </summary>
internal static class SemillaBalances
{
    private static readonly Lazy<(string[][] Modelos, string[][] Nodos, string[][] Reglas)> _filas = new(() => (Leer("ModelosBalance"), Leer("NodosBalance"), Leer("ReglasCuentasBalance")));

    /// <summary>SHA-256 de las tres semillas: entra en la firma del paso, así que un cambio en ellas cambia el hash de la migración.</summary>
    public static string Huella { get; } = CalcularHuella();

    public static int Modelos => _filas.Value.Modelos.Length;
    public static int Nodos => _filas.Value.Nodos.Length;
    public static int Reglas => _filas.Value.Reglas.Length;

    /// <summary>El paso está hecho si alguna de las tres tablas tiene filas (no se mezcla la semilla con un modelo ya cargado).</summary>
    public static async Task<bool> HayModeloAsync(ISesionDatos s)
    {
        foreach (var tabla in new[] { "ModelosBalance", "NodosBalance", "ReglasCuentasBalance" })
            if (Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM {tabla}"), CultureInfo.InvariantCulture) > 0) return true;
        return false;
    }

    /// <summary>Inserta la semilla con sus identificadores; los nodos van en orden de identificador, así que cada padre existe antes que sus hijos.</summary>
    public static async Task CargarAsync(ISesionDatos s)
    {
        var (modelos, nodos, reglas) = _filas.Value;
        foreach (var m in modelos)
            await s.EjecutarAsync(
                "INSERT INTO ModelosBalance (IdModeloBalance, Codigo, Nombre, TipoBalance, VersionNormativa, FechaVigenciaDesde, FuenteNormativa, EstaActivo) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                Entero(m[0]), m[1], m[2], (byte)Entero(m[3]), "PGC Pymes 2008", new DateTime(2008, 1, 1),
                "Real Decreto 1515/2007; estructura contrastada con ProConta legado", true);
        foreach (var n in nodos.OrderBy(n => Entero(n[0])))
            await s.EjecutarAsync(
                "INSERT INTO NodosBalance (IdNodoBalance, IdModeloBalance, IdNodoPadre, Codigo, Nombre, TipoNodo, Orden, Nivel, Multiplicador, MostrarSiEsCero, EstaActivo) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                Entero(n[0]), Entero(n[1]), n[2].Length == 0 ? DBNull.Value : Entero(n[2]), n[3], n[4], (byte)Entero(n[5]), Entero(n[6]), (byte)Entero(n[7]),
                (short)1, false, true);
        foreach (var r in reglas)
            await s.EjecutarAsync(
                "INSERT INTO ReglasCuentasBalance (IdReglaCuentaBalance, IdNodoBalance, TipoCoincidencia, PrefijoCuenta, EsInclusion, Multiplicador, Prioridad, Descripcion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                Entero(r[0]), Entero(r[1]), (byte)2, r[2], true, (short)Entero(r[3]), (short)Entero(r[4]), r[5]);
    }

    private static int Entero(string s) => int.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

    private static string Texto(string nombre)
    {
        using var flujo = typeof(SemillaBalances).Assembly.GetManifestResourceStream($"ProConta.Datos.Esquema.Semillas.{nombre}.tsv")
            ?? throw new InvalidOperationException($"Falta la semilla incrustada Semillas/{nombre}.tsv en ProConta.Datos.Esquema.");
        using var lector = new StreamReader(flujo, Encoding.UTF8);
        return lector.ReadToEnd().Replace("\r\n", "\n");
    }

    private static string[][] Leer(string nombre) =>
        Texto(nombre).Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(l => l.Split('\t')).ToArray();

    private static string CalcularHuella() =>
        "semilla " + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Texto("ModelosBalance") + Texto("NodosBalance") + Texto("ReglasCuentasBalance"))))[..16];
}
