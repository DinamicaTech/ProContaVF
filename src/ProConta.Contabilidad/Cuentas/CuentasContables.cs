using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Cuentas;

/// <summary>Naturaleza del saldo (columna Naturaleza): 0 sin definir, 1 deudora, 2 acreedora.</summary>
public enum Naturaleza : byte
{
    SinDefinir = 0,
    Deudora = 1,
    Acreedora = 2
}

/// <summary>
/// Cuenta de un ejercicio (tabla CuentasContables) ligada a su identidad permanente
/// (IdentidadesContables). Un mismo código en ejercicios distintos son cuentas distintas.
/// </summary>
public sealed record CuentaContable(
    int IdCuentaContable,
    int IdIdentidadContable,
    int IdSociedadEjercicio,
    int? IdCuentaPadre,
    string Codigo,
    string Nombre,
    bool EstaActiva,
    bool EstaBloqueada,
    bool EsImputable,
    int? IdCentroCostePredeterminado,
    decimal SaldoInicial,
    decimal SaldoActual,
    int NumeroApuntes);

/// <summary>Datos para dar de alta una cuenta nueva (con identidad nueva).</summary>
public sealed record AltaCuenta(string Codigo, string Nombre, int? IdCuentaPadre = null, int? IdTipoCuentaContable = null,
    Naturaleza Naturaleza = Naturaleza.SinDefinir, bool EsImputable = true, int? IdCentroCostePredeterminado = null);

/// <summary>
/// Contrato del ente CuentaContable: resolver por ejercicio y código exacto, comprobar que una cuenta
/// admite apuntes, alta con reserva histórica de código, y reconstruir la caché de saldos.
/// </summary>
public interface ICatalogoCuentas
{
    Task<CuentaContable?> ObtenerAsync(int idCuentaContable);

    /// <summary>Cuenta del ejercicio con ese código exacto, o error explicado si no existe.</summary>
    Task<Resultado<CuentaContable>> ResolverAsync(ContextoSociedadEjercicio contexto, string codigo);

    /// <summary>Cuentas del ejercicio, opcionalmente por prefijo de código.</summary>
    Task<IReadOnlyList<CuentaContable>> ListarAsync(ContextoSociedadEjercicio contexto, string? prefijo = null);

    Task<Resultado<CuentaContable>> AltaAsync(ContextoSociedadEjercicio contexto, AltaCuenta alta);

    /// <summary>Recalcula SaldoActual y NumeroApuntes de las cuentas del ejercicio desde sus apuntes.</summary>
    Task ReconstruirSaldosAsync(ContextoSociedadEjercicio contexto);
}

public static class ReglasCuenta
{
    public const int LongitudMaximaCodigo = 30;
    public const int LongitudMaximaNombre = 150;

    /// <summary>Una cuenta inexistente, inactiva, bloqueada o no imputable no recibe apuntes nuevos (RF-CUE-01).</summary>
    public static Resultado AdmiteApuntes(CuentaContable cuenta)
    {
        string? causa = !cuenta.EstaActiva ? "está inactiva" : cuenta.EstaBloqueada ? "está bloqueada" : !cuenta.EsImputable ? "no es imputable (es una cuenta de agrupación)" : null;
        return causa is null
            ? Resultado.Correcto()
            : Resultado.Fallo(new Error("Cuentas.NoAdmiteApuntes", $"La cuenta {cuenta.Codigo} {cuenta.Nombre} no admite apuntes.",
                $"Cuenta {cuenta.Codigo}", $"La cuenta {causa}.",
                cuenta.EsImputable ? "Usa otra cuenta o pide que la activen o desbloqueen." : "Usa una subcuenta imputable de esta cuenta."));
    }
}

public sealed class CatalogoCuentasAccess(IBaseDatos bd) : ICatalogoCuentas
{
    private const string Columnas =
        "IdCuentaContable, IdIdentidadContable, IdSociedadEjercicio, IdCuentaPadre, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, IdCentroCostePredeterminado, SaldoInicial, SaldoActual, NumeroApuntes";

    public Task<CuentaContable?> ObtenerAsync(int idCuentaContable) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM CuentasContables WHERE IdCuentaContable = ?", Mapear, idCuentaContable));

    public async Task<Resultado<CuentaContable>> ResolverAsync(ContextoSociedadEjercicio contexto, string codigo)
    {
        var texto = (codigo ?? "").Trim();
        var cuenta = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            $"SELECT {Columnas} FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", Mapear, contexto.IdSociedadEjercicio, texto));
        return cuenta is not null
            ? Resultado<CuentaContable>.Correcto(cuenta)
            : Resultado<CuentaContable>.Fallo(new Error("Cuentas.NoExiste", $"No existe la cuenta {texto} en el ejercicio {contexto.Ejercicio}.",
                $"Cuenta {texto}", "No hay ninguna cuenta con ese código exacto en este ejercicio.",
                "Revisa el código o da de alta la cuenta en este ejercicio."));
    }

    public Task<IReadOnlyList<CuentaContable>> ListarAsync(ContextoSociedadEjercicio contexto, string? prefijo = null) =>
        bd.EjecutarAsync(s => string.IsNullOrWhiteSpace(prefijo)
            ? s.ConsultarAsync($"SELECT {Columnas} FROM CuentasContables WHERE IdSociedadEjercicio = ? ORDER BY Codigo", Mapear, contexto.IdSociedadEjercicio)
            : s.ConsultarAsync($"SELECT {Columnas} FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo LIKE ? ORDER BY Codigo", Mapear,
                contexto.IdSociedadEjercicio, prefijo.Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%"));

    public Task<Resultado<CuentaContable>> AltaAsync(ContextoSociedadEjercicio contexto, AltaCuenta alta) =>
        bd.EnTransaccionAsync(async s =>
        {
            var codigo = (alta.Codigo ?? "").Trim();
            var nombre = (alta.Nombre ?? "").Trim();
            var registro = $"Cuenta {codigo}";
            if (codigo.Length == 0 || codigo.Length > ReglasCuenta.LongitudMaximaCodigo || !codigo.All(char.IsAsciiDigit))
                return Fallo("Cuentas.CodigoNoValido", $"El código de cuenta «{codigo}» no es válido.", registro,
                    $"Debe tener solo cifras, entre 1 y {ReglasCuenta.LongitudMaximaCodigo}.", "Escribe el código con cifras, por ejemplo 4300001.");
            if (nombre.Length == 0 || nombre.Length > ReglasCuenta.LongitudMaximaNombre)
                return Fallo("Cuentas.NombreNoValido", "Falta el nombre de la cuenta o es demasiado largo.", registro,
                    $"Debe tener entre 1 y {ReglasCuenta.LongitudMaximaNombre} caracteres.", "Escribe el nombre de la cuenta.");

            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", contexto.IdSociedadEjercicio, codigo)) > 0)
                return Fallo("Cuentas.Repetida", $"La cuenta {codigo} ya existe en el ejercicio {contexto.Ejercicio}.", registro,
                    "El código es único dentro de cada ejercicio.", "Usa la cuenta que ya existe o elige otro código.");
            var reservada = await s.ConsultarPrimeroAsync("SELECT IdIdentidadContable FROM CodigosContablesReservados WHERE IdSociedad = ? AND Codigo = ?",
                f => (int?)f.Entero("IdIdentidadContable"), contexto.IdSociedad, codigo);
            if (reservada is not null)
                return Fallo("Cuentas.CodigoReservado", $"El código {codigo} ya se usó en otro ejercicio para otra cuenta de esta sociedad.", registro,
                    "Los códigos usados se reservan para no confundir cuentas distintas en el histórico.",
                    "Si es la misma cuenta, cópiala desde el ejercicio en que existe; si es otra, elige otro código.");

            if (alta.IdCuentaPadre is int padre)
            {
                var ejercicioPadre = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM CuentasContables WHERE IdCuentaContable = ?", padre);
                if (ejercicioPadre is null || Convert.ToInt32(ejercicioPadre) != contexto.IdSociedadEjercicio)
                    return Fallo("Cuentas.PadreNoValido", "La cuenta padre no existe en este ejercicio.", registro,
                        "La jerarquía de cuentas es de cada ejercicio.", "Elige una cuenta padre del mismo ejercicio.");
            }

            var hoy = DateTime.Today;
            await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, IdTipoCuentaContable, Naturaleza, FechaAlta) VALUES (?, ?, ?, ?)",
                contexto.IdSociedad, alta.IdTipoCuentaContable, (byte)alta.Naturaleza, hoy);
            var identidad = await s.UltimoIdAsync();
            await s.EjecutarAsync(
                "INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, IdCuentaPadre, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, IdCentroCostePredeterminado, SaldoInicial, SaldoActual, NumeroApuntes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                identidad, contexto.IdSociedadEjercicio, alta.IdCuentaPadre, codigo, nombre, true, false, alta.EsImputable, alta.IdCentroCostePredeterminado, 0m, 0m, 0);
            var id = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CodigosContablesReservados (IdSociedad, Codigo, IdIdentidadContable, FechaPrimeraAsignacion) VALUES (?, ?, ?, ?)",
                contexto.IdSociedad, codigo, identidad, hoy);
            return Resultado<CuentaContable>.Correcto(new CuentaContable(id, identidad, contexto.IdSociedadEjercicio, alta.IdCuentaPadre, codigo, nombre,
                true, false, alta.EsImputable, alta.IdCentroCostePredeterminado, 0m, 0m, 0));
        });

    public Task ReconstruirSaldosAsync(ContextoSociedadEjercicio contexto) =>
        bd.EnTransaccionAsync(async s =>
        {
            // Los apuntes guardan el código de cuenta; se agregan por código dentro de los asientos del ejercicio.
            var movimientos = await s.ConsultarAsync(
                "SELECT ap.CuentaContable AS Codigo, SUM(IIF(ap.ImporteDebe IS NULL, 0, ap.ImporteDebe)) AS Debe, SUM(IIF(ap.ImporteHaber IS NULL, 0, ap.ImporteHaber)) AS Haber, COUNT(*) AS N " +
                "FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? GROUP BY ap.CuentaContable",
                f => (Codigo: f.Texto("Codigo") ?? "", Saldo: f.Decimal("Debe") - f.Decimal("Haber"), N: f.Entero("N")), contexto.IdSociedadEjercicio);
            await s.EjecutarAsync("UPDATE CuentasContables SET SaldoActual = SaldoInicial, NumeroApuntes = 0 WHERE IdSociedadEjercicio = ?", contexto.IdSociedadEjercicio);
            foreach (var m in movimientos)
                await s.EjecutarAsync("UPDATE CuentasContables SET SaldoActual = SaldoInicial + ?, NumeroApuntes = ? WHERE IdSociedadEjercicio = ? AND Codigo = ?",
                    m.Saldo, m.N, contexto.IdSociedadEjercicio, m.Codigo);
            return true;
        });

    private static Resultado<CuentaContable> Fallo(string codigo, string mensaje, string registro, string causa, string accion) =>
        Resultado<CuentaContable>.Fallo(new Error(codigo, mensaje, registro, causa, accion));

    internal static CuentaContable Mapear(IFilaDatos f) => new(
        f.Entero("IdCuentaContable"), f.Entero("IdIdentidadContable"), f.Entero("IdSociedadEjercicio"), f.EnteroNulo("IdCuentaPadre"),
        f.Texto("Codigo") ?? "", f.Texto("Nombre") ?? "", f.Logico("EstaActiva"), f.Logico("EstaBloqueada"), f.Logico("EsImputable"),
        f.EnteroNulo("IdCentroCostePredeterminado"), f.Decimal("SaldoInicial"), f.Decimal("SaldoActual"), f.Entero("NumeroApuntes"));
}
