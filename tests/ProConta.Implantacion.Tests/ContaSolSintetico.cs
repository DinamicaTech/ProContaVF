using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Implantacion.ContaSol;
using ProConta.Pruebas.Access;

namespace ProConta.Implantacion.Tests;

/// <summary>
/// BD de ContaSol sintética (un ejercicio) con las columnas que lee el importador. Datos inventados, nunca reales.
/// </summary>
public sealed class ContaSolSintetico
{
    public sealed record Linea(int Asiento, int Orden, DateTime Fecha, string Cuenta, decimal Importe, bool Debe, string Concepto, int Dep = 0, int Sub = 0, string? TipoIva = null, int CodigoIva = 0);

    public string Empresa { get; init; } = "T01";
    public string Nif { get; init; } = "B12345674";
    public required int Ejercicio { get; init; }
    public List<(string Codigo, string Nombre)> Cuentas { get; } = [];
    public List<Linea> Apuntes { get; } = [];
    public List<(bool Cliente, string Codigo, string Nombre, string Nif)> Terceros { get; } = [];
    /// <summary>Teléfono y email de la ficha de ContaSol de un tercero, por su código.</summary>
    public Dictionary<string, (string? Telefono, string? Email)> ContactosTerceros { get; } = [];
    public List<(int Codigo, DateTime Fecha, string Cuenta, string Nif, decimal Base, decimal Tipo, decimal Cuota)> IvaSoportado { get; } = [];
    public List<(int Dep, int Sub, string Nombre)> Centros { get; } = [];

    public void Asiento(int numero, DateTime fecha, string concepto, params (string Cuenta, decimal Debe, decimal Haber)[] lineas)
    {
        var orden = 1;
        foreach (var (cuenta, debe, haber) in lineas)
            Apuntes.Add(new Linea(numero, orden++, fecha, cuenta, debe != 0m ? debe : haber, debe != 0m, concepto));
    }

    private static string Tipo(string columna)
    {
        if (columna.StartsWith("FEC") || columna.StartsWith("FFA") || columna.StartsWith("FRO")) return "DATETIME";
        if (columna is "D-HAPU" || columna.StartsWith("TIV")) return "TEXT(1)";
        string[] numeros = ["IME", "BA", "PI", "CI", "PR", "CR", "TOT", "PDE", "ED", "EH"];
        if (numeros.Any(n => columna.StartsWith(n) && !columna.StartsWith("CIF") && !columna.StartsWith("CIV") && !columna.StartsWith("CPO"))) return "DOUBLE";
        string[] enteros = ["DIA", "ASI", "ORD", "DEP", "SUB", "CIV", "TIP", "347", "EJE", "NAS", "CODDEP", "CODCDP", "CODIVS", "CODIVR"];
        if (enteros.Any(columna.StartsWith)) return "LONG";
        return "TEXT(255)";
    }

    public async Task<string> CrearAsync(string carpeta)
    {
        var ruta = Path.Combine(carpeta, $"{Empresa}{Ejercicio}-{Guid.NewGuid():N}.accdb");
        BdPruebas.CrearBdVacia(ruta);
        IBaseDatos bd = new BaseDatosAccess(ruta);
        await bd.EjecutarAsync(async s =>
        {
            foreach (var (tabla, columnas) in LectorContaSol.ColumnasNecesarias)
                await s.EjecutarAsync($"CREATE TABLE {tabla} ({string.Join(", ", columnas.Select(c => $"[{c}] {Tipo(c)}"))})");
            await s.EjecutarAsync("INSERT INTO F_EMP (CODEMP, NIFEMP, DENEMP, EJEEMP) VALUES (?, ?, ?, ?)", Empresa, Nif, "Sociedad sintética", Ejercicio);
            foreach (var dep in Centros.Where(c => c.Sub == 0))
                await s.EjecutarAsync("INSERT INTO F_DEP (CODDEP, DENDEP) VALUES (?, ?)", dep.Dep, dep.Nombre);
            foreach (var sub in Centros.Where(c => c.Sub != 0))
                await s.EjecutarAsync("INSERT INTO F_CDP (DEPCDP, CODCDP, DENCDP) VALUES (?, ?, ?)", sub.Dep, sub.Sub, sub.Nombre);
            foreach (var (codigo, nombre) in Cuentas)
                await s.EjecutarAsync("INSERT INTO F_MAE (CODMAE, NOMMAE, DEPMAE, SUBMAE) VALUES (?, ?, 0, 0)", codigo, nombre);
            foreach (var a in Apuntes)
                await s.EjecutarAsync("INSERT INTO F_APU (DIAAPU, FECAPU, ASIAPU, ORDAPU, CUEAPU, IMEAPU, CONAPU, [D-HAPU], DEPAPU, SUBAPU, TIVAPU, CIVAPU) VALUES (1, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    a.Fecha, a.Asiento, a.Orden, a.Cuenta, (double)a.Importe, a.Concepto, a.Debe ? "D" : "H", a.Dep, a.Sub, a.TipoIva ?? "", a.CodigoIva);
            foreach (var t in Terceros)
                await s.EjecutarAsync(t.Cliente ? "INSERT INTO F_CLI (CODCLI, NOMCLI, CIFCLI) VALUES (?, ?, ?)" : "INSERT INTO F_PRO (CODPRO, NOMPRO, CIFPRO) VALUES (?, ?, ?)",
                    t.Codigo, t.Nombre, t.Nif);
            foreach (var (codigo, (telefono, email)) in ContactosTerceros)
            {
                var cliente = Terceros.First(t => t.Codigo == codigo).Cliente;
                await s.EjecutarAsync(cliente ? "UPDATE F_CLI SET TELCLI = ?, EMACLI = ? WHERE CODCLI = ?" : "UPDATE F_PRO SET TELPRO = ?, EMAPRO = ? WHERE CODPRO = ?",
                    telefono ?? "", email ?? "", codigo);
            }
            foreach (var r in IvaSoportado)
                await s.EjecutarAsync("INSERT INTO F_IVS (CODIVS, FECIVS, FFAIVS, CUEIVS, FACIVS, NOMIVS, CIFIVS, BA0IVS, BA1IVS, PI1IVS, CI1IVS, TOTIVS, TIPIVS, [347IVS], PDEIVS) " +
                                      "VALUES (?, ?, ?, ?, ?, ?, ?, 0, ?, ?, ?, ?, 0, 1, 100)",
                    r.Codigo, r.Fecha, r.Fecha, r.Cuenta, $"F-{r.Codigo}", "Proveedor sintético", r.Nif, (double)r.Base, (double)r.Tipo, (double)r.Cuota, (double)(r.Base + r.Cuota));
            // F_SAL coherente con los apuntes (saldo anual en el periodo 1).
            foreach (var g in Apuntes.GroupBy(a => a.Cuenta))
            {
                var saldo = g.Sum(a => a.Debe ? a.Importe : -a.Importe);
                await s.EjecutarAsync("INSERT INTO F_SAL (CUESAL, ED1SAL, EH1SAL) VALUES (?, ?, ?)", g.Key, (double)Math.Max(saldo, 0m), (double)Math.Max(-saldo, 0m));
            }
            return true;
        });
        return ruta;
    }
}
