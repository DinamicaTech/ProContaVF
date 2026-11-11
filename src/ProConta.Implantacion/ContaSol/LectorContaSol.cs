using System.Globalization;
using System.Security.Cryptography;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Resultados;

namespace ProConta.Implantacion.ContaSol;

/// <summary>
/// Lee una BD de ContaSol (un ejercicio) siempre en solo lectura: el origen nunca se modifica (RF-CS-01). Vale también para
/// cualquier accdb que tenga las tablas F_ de ContaSol con su estructura (por ejemplo, ProContaEstructura).
/// Solo se leen las tablas que se importan o que sirven de control: F_EMP, F_DEP, F_CDP, F_MAE, F_APU, F_OAS, F_IVS,
/// F_IVR, F_CLI, F_PRO y F_SAL. F_347, F_349, F_BAN, F_DIA, F_EFC y F_EFP no se importan (propietario, 2026-11-10).
/// </summary>
public sealed class LectorContaSol
{
    /// <summary>Columnas que el importador necesita de cada tabla; si falta alguna, la BD no se reconoce como de ContaSol.</summary>
    public static readonly IReadOnlyDictionary<string, string[]> ColumnasNecesarias = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        ["F_EMP"] = ["CODEMP", "NIFEMP", "DENEMP", "EJEEMP"],
        ["F_DEP"] = ["CODDEP", "DENDEP", "OBSDEP"],
        ["F_CDP"] = ["DEPCDP", "CODCDP", "DENCDP", "OBSCDP"],
        ["F_MAE"] = ["CODMAE", "NOMMAE", "NEXMAE", "DEPMAE", "SUBMAE"],
        ["F_APU"] = ["DIAAPU", "FECAPU", "ASIAPU", "ORDAPU", "CUEAPU", "IMEAPU", "CONAPU", "DOCAPU", "D-HAPU", "DEPAPU", "SUBAPU", "TIVAPU", "CIVAPU"],
        ["F_OAS"] = ["DIAOAS", "NASOAS", "TEXOAS"],
        ["F_IVS"] = ["CODIVS", "FECIVS", "FFAIVS", "FROIVS", "CUEIVS", "FACIVS", "NOMIVS", "CIFIVS", "BA0IVS", "BA1IVS", "BA2IVS", "BA3IVS", "PI1IVS", "PI2IVS", "PI3IVS",
            "CI1IVS", "CI2IVS", "CI3IVS", "TOTIVS", "TIPIVS", "347IVS", "PDEIVS", "OBSIVS"],
        ["F_IVR"] = ["CODIVR", "FECIVR", "FFAIVR", "FROIVR", "CUEIVR", "FACIVR", "NOMIVR", "CIFIVR", "BA0IVR", "BA1IVR", "BA2IVR", "BA3IVR", "PI1IVR", "PI2IVR", "PI3IVR",
            "CI1IVR", "CI2IVR", "CI3IVR", "PR1IVR", "PR2IVR", "PR3IVR", "CR1IVR", "CR2IVR", "CR3IVR", "TOTIVR", "TIPIVR", "347IVR", "OBSIVR"],
        ["F_CLI"] = ["CODCLI", "NOMCLI", "NCOCLI", "CIFCLI", "DOMCLI", "CPOCLI", "POBCLI", "POVCLI", "PAICLI", "TELCLI", "EMACLI"],
        ["F_PRO"] = ["CODPRO", "NOMPRO", "NCOPRO", "CIFPRO", "DOMPRO", "CPOPRO", "POBPRO", "POVPRO", "PAIPRO", "TELPRO", "EMAPRO"],
        ["F_SAL"] = ["CUESAL", .. Enumerable.Range(0, 15).SelectMany(p => new[] { $"ED{p}SAL", $"EH{p}SAL" })],
    };

    /// <summary>Lee el ejercicio completo. Falla con un error explicado si el fichero no existe o no es de ContaSol.</summary>
    public async Task<Resultado<OrigenContaSol>> LeerAsync(string ruta, CancellationToken cancelacion = default)
    {
        if (!File.Exists(ruta))
            return Resultado<OrigenContaSol>.Fallo(new Error("ContaSol.NoExiste", $"No encuentro la BD de ContaSol {ruta}.", ruta,
                "El fichero no existe o no tengo acceso a él.", "Comprueba la ruta y que la carpeta está disponible."));
        var bd = new BaseDatosAccess(ruta, ModoApertura.SoloLectura);
        try
        {
            return await bd.EjecutarAsync(async s =>
            {
                var esquema = s.LeerEsquema();
                var faltan = ColumnasNecesarias
                    .SelectMany(t => esquema.ExisteTabla(t.Key) ? t.Value.Where(c => !esquema.ExisteColumna(t.Key, c)).Select(c => $"{t.Key}.{c}") : [t.Key])
                    .ToList();
                if (faltan.Count > 0)
                    return Resultado<OrigenContaSol>.Fallo(new Error("ContaSol.NoReconocida", "Esta BD no tiene la estructura de ContaSol que sé importar.", ruta,
                        $"Faltan: {string.Join(", ", faltan.Take(12))}{(faltan.Count > 12 ? "…" : "")}.",
                        "Elige la BD de un ejercicio de ContaSol (por ejemplo 0022026.accdb)."));

                var empresas = await s.ConsultarAsync("SELECT CODEMP, NIFEMP, DENEMP, EJEEMP FROM F_EMP",
                    f => new EmpresaContaSol(Txt(f, "CODEMP") ?? "", Txt(f, "NIFEMP") ?? "", Txt(f, "DENEMP") ?? "", Ent(f, "EJEEMP")));
                if (empresas.Count != 1 || empresas[0].Ejercicio is < 1990 or > 2100 || empresas[0].Codigo.Length is 0 or > 3)
                    return Resultado<OrigenContaSol>.Fallo(new Error("ContaSol.SinEmpresa", "No sé de qué empresa y ejercicio es esta BD de ContaSol.", ruta,
                        $"F_EMP tiene {empresas.Count} filas; necesito una, con código de empresa de 1 a 3 caracteres y ejercicio.",
                        "Abre la BD en ContaSol y comprueba los datos de la empresa."));

                var departamentos = await s.ConsultarAsync("SELECT CODDEP, DENDEP, OBSDEP FROM F_DEP",
                    f => new DepartamentoContaSol(Ent(f, "CODDEP"), Txt(f, "DENDEP") ?? "", Txt(f, "OBSDEP")));
                var subdepartamentos = await s.ConsultarAsync("SELECT DEPCDP, CODCDP, DENCDP, OBSCDP FROM F_CDP",
                    f => new SubdepartamentoContaSol(Ent(f, "DEPCDP"), Ent(f, "CODCDP"), Txt(f, "DENCDP") ?? "", Txt(f, "OBSCDP")));
                var cuentas = await s.ConsultarAsync("SELECT CODMAE, NOMMAE, NEXMAE, DEPMAE, SUBMAE FROM F_MAE",
                    f => new CuentaContaSol(Txt(f, "CODMAE") ?? "", Txt(f, "NOMMAE") ?? "", Txt(f, "NEXMAE"), Ent(f, "DEPMAE"), Ent(f, "SUBMAE")));
                var apuntes = await s.ConsultarAsync(
                    "SELECT DIAAPU, FECAPU, ASIAPU, ORDAPU, CUEAPU, IMEAPU, CONAPU, DOCAPU, [D-HAPU], DEPAPU, SUBAPU, TIVAPU, CIVAPU FROM F_APU ORDER BY DIAAPU, ASIAPU, ORDAPU",
                    f => new ApunteContaSol(Ent(f, "DIAAPU"), Ent(f, "ASIAPU"), Ent(f, "ORDAPU"), Fec(f, "FECAPU") ?? DateTime.MinValue, Txt(f, "CUEAPU") ?? "",
                        Dec(f, "IMEAPU"), !string.Equals(Txt(f, "D-HAPU"), "H", StringComparison.OrdinalIgnoreCase), Txt(f, "CONAPU"), Txt(f, "DOCAPU"),
                        Ent(f, "DEPAPU"), Ent(f, "SUBAPU"), Txt(f, "TIVAPU"), Ent(f, "CIVAPU")));
                var textos = await s.ConsultarAsync("SELECT DIAOAS, NASOAS, TEXOAS FROM F_OAS",
                    f => new TextoAsientoContaSol(Ent(f, "DIAOAS"), Ent(f, "NASOAS"), Txt(f, "TEXOAS") ?? ""));
                var soportado = await s.ConsultarAsync("SELECT * FROM F_IVS", f => Registro(f, "IVS", soportado: true));
                var repercutido = await s.ConsultarAsync("SELECT * FROM F_IVR", f => Registro(f, "IVR", soportado: false));
                var clientes = await s.ConsultarAsync("SELECT CODCLI, NOMCLI, NCOCLI, CIFCLI, DOMCLI, CPOCLI, POBCLI, POVCLI, PAICLI, TELCLI, EMACLI FROM F_CLI",
                    f => Tercero(f, "CLI", esCliente: true));
                var proveedores = await s.ConsultarAsync("SELECT CODPRO, NOMPRO, NCOPRO, CIFPRO, DOMPRO, CPOPRO, POBPRO, POVPRO, PAIPRO, TELPRO, EMAPRO FROM F_PRO",
                    f => Tercero(f, "PRO", esCliente: false));
                var saldos = new Dictionary<string, decimal>(StringComparer.Ordinal);
                foreach (var (cuenta, saldo) in await s.ConsultarAsync("SELECT * FROM F_SAL",
                             f => (Txt(f, "CUESAL") ?? "", Enumerable.Range(0, 15).Sum(p => Dec(f, $"ED{p}SAL") - Dec(f, $"EH{p}SAL")))))
                    saldos[cuenta.Trim()] = saldos.GetValueOrDefault(cuenta.Trim()) + saldo;

                return Resultado<OrigenContaSol>.Correcto(new OrigenContaSol(Path.GetFullPath(ruta), Huella(ruta), empresas[0], departamentos, subdepartamentos, cuentas,
                    apuntes, textos, [.. soportado, .. repercutido], [.. clientes, .. proveedores], saldos));
            }, cancelacion);
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<OrigenContaSol>.Fallo(new Error("ContaSol.NoSeAbre", $"No he podido leer la BD de ContaSol {Path.GetFileName(ruta)}.", ruta,
                ex.Message, "Comprueba que el fichero no está dañado y que ContaSol no lo tiene abierto en exclusiva."));
        }
    }

    /// <summary>SHA-256 del fichero: identifica el origen aunque se mueva de carpeta (protocolo de importación).</summary>
    public static string Huella(string ruta)
    {
        using var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(flujo));
    }

    private static RegistroIvaContaSol Registro(IFilaDatos f, string sufijo, bool soportado)
    {
        string C(string campo) => campo + sufijo;
        var tramos = new List<TramoIvaContaSol>();
        for (var i = 1; i <= 3; i++)
            tramos.Add(new TramoIvaContaSol(Dec(f, C($"BA{i}")), Dec(f, C($"PI{i}")), Dec(f, C($"CI{i}")),
                soportado ? 0m : Dec(f, C($"PR{i}")), soportado ? 0m : Dec(f, C($"CR{i}"))));
        return new RegistroIvaContaSol(soportado, Ent(f, C("COD")), Fec(f, C("FEC")) ?? DateTime.MinValue, Fec(f, C("FFA")), Fec(f, C("FRO")), Txt(f, C("CUE")) ?? "",
            Txt(f, C("FAC")), Txt(f, C("NOM")), Txt(f, C("CIF")), Dec(f, C("BA0")), tramos, Dec(f, C("TOT")), Ent(f, C("TIP")), Ent(f, "347" + sufijo) != 0,
            soportado ? Dec(f, "PDEIVS") : 100m, Txt(f, C("OBS")));
    }

    private static TerceroContaSol Tercero(IFilaDatos f, string sufijo, bool esCliente)
    {
        string? C(string campo) => Txt(f, campo + sufijo);
        return new TerceroContaSol(esCliente, C("COD") ?? "", C("NOM") ?? "", C("NCO"), C("CIF"), C("DOM"), C("CPO"), C("POB"), C("POV"), C("PAI"), C("TEL"), C("EMA"));
    }

    // Lectura tolerante: ContaSol guarda números como texto o número según la tabla y la versión.
    private static string? Txt(IFilaDatos f, string campo) => f.Valor(campo) is { } v && v is not DBNull && v.ToString()!.Trim() is { Length: > 0 } t ? t : null;

    private static int Ent(IFilaDatos f, string campo) =>
        f.Valor(campo) is { } v && v is not DBNull && int.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture)?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : f.Valor(campo) is { } w && w is not DBNull && w is IConvertible ? Convert.ToInt32(Convert.ToDecimal(w, CultureInfo.InvariantCulture)) : 0;

    private static decimal Dec(IFilaDatos f, string campo) =>
        f.Valor(campo) is { } v && v is not DBNull ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;

    /// <summary>Fecha; ContaSol guarda «sin fecha» como 30/12/1899 o 01/01/1900, que se leen como null.</summary>
    private static DateTime? Fec(IFilaDatos f, string campo) =>
        f.Valor(campo) is DateTime d && d.Year > 1900 ? d.Date : null;
}
