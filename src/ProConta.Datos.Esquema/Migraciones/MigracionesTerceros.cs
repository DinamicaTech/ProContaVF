using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de terceros (docs/datos/esquema.md, Requirements).</summary>
public static class MigracionesTerceros
{
    /// <summary>Versión de fila de la ficha del tercero, para la concurrencia optimista (las filas existentes empiezan en 0).</summary>
    public static Migracion VersionTercero { get; } = new(
        "20261031_1905_TERCEROS_VERSION", 261031003,
        "Terceros.Version (versión de fila para concurrencia optimista).",
        "terceros/tercero",
        new AnadirColumnaObligatoria("Terceros", "Version", "LONG", "0"));

    public const string AntiguaClientes = "CuentasClientesEjercicio";
    public const string AntiguaProveedores = "CuentasProveedoresEjercicio";

    /// <summary>
    /// Cuentas de los roles por sociedad con la identidad contable (terceros/roles 2026-11-01 09:52): tablas
    /// CuentasClientesSociedad y CuentasProveedoresSociedad (clase PROVEEDOR o ACREEDOR). Cada fila de las tablas por
    /// ejercicio pasa a la identidad de su cuenta y a la sociedad de su ejercicio (las de proveedor como PROVEEDOR); si
    /// para un rol y sociedad hay varias identidades, gana la predeterminada del ejercicio más reciente. Después se
    /// retiran las tablas por ejercicio. Antes de cambiar nada se comprueba que todas las filas se pueden trasladar.
    /// </summary>
    public static Migracion CuentasRolesSociedad { get; } = new(
        "20261101_1000_CUENTAS_ROLES_SOCIEDAD", 261101002,
        "Cuentas de cliente, proveedor y acreedor por sociedad con la identidad contable; se retiran las tablas por ejercicio.",
        "terceros/roles",
        new CrearTabla("CuentasClientesSociedad",
            "CREATE TABLE CuentasClientesSociedad (IdCuentaClienteSociedad COUNTER CONSTRAINT PK_CuentasClientesSociedad PRIMARY KEY, " +
            "IdCliente LONG NOT NULL CONSTRAINT FK_CCS_Cliente REFERENCES Clientes (IdCliente), " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_CCS_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "IdIdentidadContable LONG NOT NULL CONSTRAINT FK_CCS_Identidad REFERENCES IdentidadesContables (IdIdentidadContable))"),
        new CrearIndice("CuentasClientesSociedad", "UX_CCS_ClienteSociedad",
            "CREATE UNIQUE INDEX UX_CCS_ClienteSociedad ON CuentasClientesSociedad (IdCliente, IdSociedad)"),
        new CrearTabla("CuentasProveedoresSociedad",
            "CREATE TABLE CuentasProveedoresSociedad (IdCuentaProveedorSociedad COUNTER CONSTRAINT PK_CuentasProveedoresSociedad PRIMARY KEY, " +
            "IdProveedor LONG NOT NULL CONSTRAINT FK_CPS_Proveedor REFERENCES Proveedores (IdProveedor), " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_CPS_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Clase TEXT(10) NOT NULL, " +
            "IdIdentidadContable LONG NOT NULL CONSTRAINT FK_CPS_Identidad REFERENCES IdentidadesContables (IdIdentidadContable))"),
        new CrearIndice("CuentasProveedoresSociedad", "UX_CPS_ProveedorSociedadClase",
            "CREATE UNIQUE INDEX UX_CPS_ProveedorSociedadClase ON CuentasProveedoresSociedad (IdProveedor, IdSociedad, Clase)"),
        new PasoPersonalizado(
            "Trasladar las cuentas de los clientes por ejercicio a CuentasClientesSociedad",
            $"INSERT INTO CuentasClientesSociedad (IdCliente, IdSociedad, IdIdentidadContable) <- {AntiguaClientes}: identidad de la cuenta y sociedad del ejercicio; gana la predeterminada del ejercicio más reciente",
            (_, e) => Task.FromResult(!e.ExisteTabla(AntiguaClientes)),
            (s, _) => TrasladarAsync(s, AntiguaClientes, "IdCliente", "CuentasClientesSociedad", null)),
        new PasoPersonalizado(
            "Trasladar las cuentas de los proveedores por ejercicio a CuentasProveedoresSociedad",
            $"INSERT INTO CuentasProveedoresSociedad (IdProveedor, IdSociedad, Clase, IdIdentidadContable) <- {AntiguaProveedores}: Clase PROVEEDOR; gana la predeterminada del ejercicio más reciente",
            (_, e) => Task.FromResult(!e.ExisteTabla(AntiguaProveedores)),
            (s, _) => TrasladarAsync(s, AntiguaProveedores, "IdProveedor", "CuentasProveedoresSociedad", "PROVEEDOR")),
        new PasoPersonalizado(
            "Retirar CuentasClientesEjercicio y CuentasProveedoresEjercicio",
            $"DROP TABLE {AntiguaClientes}; DROP TABLE {AntiguaProveedores}",
            (_, e) => Task.FromResult(!e.ExisteTabla(AntiguaClientes) && !e.ExisteTabla(AntiguaProveedores)),
            async (s, _) =>
            {
                var e = s.LeerEsquema();
                if (e.ExisteTabla(AntiguaClientes)) await s.EjecutarAsync($"DROP TABLE {AntiguaClientes}");
                if (e.ExisteTabla(AntiguaProveedores)) await s.EjecutarAsync($"DROP TABLE {AntiguaProveedores}");
            }))
    {
        Precondicion = ComprobarTrasladoAsync
    };

    /// <summary>Fila de una tabla de cuentas por ejercicio con lo necesario para trasladarla.</summary>
    public sealed record FilaCuentaEjercicio(int Id, int IdRol, int IdCuentaContable, int? IdIdentidadContable, string? IdSociedad, string? Ejercicio, bool EsPredeterminada);

    /// <summary>Filas de una tabla por ejercicio con su identidad y su sociedad (nulas si no se pueden resolver).</summary>
    public static async Task<IReadOnlyList<FilaCuentaEjercicio>> LeerFilasAntiguasAsync(ISesionDatos s, string tabla, string claveRol)
    {
        if (!s.LeerEsquema().ExisteTabla(tabla)) return [];
        var id = tabla == AntiguaClientes ? "IdCuentaClienteEjercicio" : "IdCuentaProveedorEjercicio";
        return await s.ConsultarAsync(
            $"SELECT t.{id} AS Id, t.{claveRol} AS IdRol, t.IdCuentaContable, cc.IdIdentidadContable, se.IdSociedad, se.Ejercicio, t.EsPredeterminada " +
            $"FROM ({tabla} AS t LEFT JOIN CuentasContables AS cc ON t.IdCuentaContable = cc.IdCuentaContable) " +
            "LEFT JOIN SociedadesEjercicios AS se ON t.IdSociedadEjercicio = se.IdSociedadEjercicio",
            f => new FilaCuentaEjercicio(f.Entero("Id"), f.Entero("IdRol"), f.Entero("IdCuentaContable"), f.EnteroNulo("IdIdentidadContable"),
                f.Texto("IdSociedad"), f.Texto("Ejercicio"), f.Logico("EsPredeterminada")));
    }

    /// <summary>Para cada rol y sociedad, la identidad que gana: la predeterminada del ejercicio más reciente.</summary>
    public static IEnumerable<(int IdRol, string IdSociedad, int IdIdentidad)> Ganadoras(IEnumerable<FilaCuentaEjercicio> filas) =>
        filas.Where(f => f.IdIdentidadContable is not null && !string.IsNullOrWhiteSpace(f.IdSociedad))
            .GroupBy(f => (f.IdRol, IdSociedad: f.IdSociedad!.Trim()))
            .Select(g => g.OrderByDescending(f => f.Ejercicio?.Trim(), StringComparer.Ordinal).ThenByDescending(f => f.EsPredeterminada).ThenByDescending(f => f.Id).First())
            .Select(f => (f.IdRol, f.IdSociedad!.Trim(), f.IdIdentidadContable!.Value));

    private static async Task TrasladarAsync(ISesionDatos s, string antigua, string claveRol, string nueva, string? clase)
    {
        foreach (var (idRol, sociedad, identidad) in Ganadoras(await LeerFilasAntiguasAsync(s, antigua, claveRol)))
        {
            var filtroClase = clase is null ? "" : " AND Clase = ?";
            object?[] clave = clase is null ? [idRol, sociedad] : [idRol, sociedad, clase];
            if (Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM {nueva} WHERE {claveRol} = ? AND IdSociedad = ?{filtroClase}", clave)) > 0) continue;
            if (clase is null)
                await s.EjecutarAsync($"INSERT INTO {nueva} ({claveRol}, IdSociedad, IdIdentidadContable) VALUES (?, ?, ?)", idRol, sociedad, identidad);
            else
                await s.EjecutarAsync($"INSERT INTO {nueva} ({claveRol}, IdSociedad, Clase, IdIdentidadContable) VALUES (?, ?, ?, ?)", idRol, sociedad, clase, identidad);
        }
    }

    /// <summary>Comprobación previa: si alguna fila no se puede trasladar (cuenta o ejercicio inexistente), no se cambia nada.</summary>
    private static async Task<Error?> ComprobarTrasladoAsync(ISesionDatos s, ContextoMigracion _)
    {
        var malas = new List<string>();
        foreach (var (tabla, rol) in new[] { (AntiguaClientes, "IdCliente"), (AntiguaProveedores, "IdProveedor") })
            foreach (var f in await LeerFilasAntiguasAsync(s, tabla, rol))
            {
                if (f.IdIdentidadContable is null) malas.Add($"{tabla} fila {f.Id}: la cuenta {f.IdCuentaContable} no existe");
                else if (string.IsNullOrWhiteSpace(f.IdSociedad)) malas.Add($"{tabla} fila {f.Id}: su ejercicio no tiene sociedad");
            }
        if (malas.Count == 0) return null;
        return new Error("Esquema.CuentasRolesSinTrasladar",
            $"No puedo pasar las cuentas de los roles a la sociedad: hay {malas.Count} filas que no se pueden trasladar. No he cambiado nada.",
            string.Join("; ", malas.Take(20)) + (malas.Count > 20 ? $"; y {malas.Count - 20} más" : ""),
            "Esas filas apuntan a una cuenta contable o a un ejercicio que ya no existen.",
            "Corrige o borra esas filas en la BD (o pide ayuda al responsable) y vuelve a ejecutar el ActualizadorEsquema.");
    }
}
