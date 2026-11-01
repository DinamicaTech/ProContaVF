using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.Roles;

/// <summary>Roles de una identidad, con sus IDs funcionales (null si no tiene ese rol).</summary>
public sealed record RolesTercero(int IdTercero, int? IdCliente, int? IdProveedor, IReadOnlyList<int> IdsContacto)
{
    public bool EsCliente => IdCliente is not null;
    public bool EsProveedor => IdProveedor is not null;
    public bool EsContacto => IdsContacto.Count > 0;
}

/// <summary>Rol Cliente en la ficha (tabla Clientes).</summary>
public sealed class RolCliente
{
    public int? IdCliente { get; internal set; }
    public bool Activo { get; set; } = true;
    public string? Notas { get; set; }
    internal RolCliente Clonar() => (RolCliente)MemberwiseClone();
}

/// <summary>Rol Proveedor en la ficha (tabla Proveedores).</summary>
public sealed class RolProveedor
{
    public int? IdProveedor { get; internal set; }
    public bool Activo { get; set; } = true;
    public string? Notas { get; set; }
    internal RolProveedor Clonar() => (RolProveedor)MemberwiseClone();
}

/// <summary>Rol Contacto (persona de contacto) en la ficha (tabla Contactos).</summary>
public sealed class RolContacto
{
    public int? IdContacto { get; internal set; }
    public bool Activo { get; set; } = true;
    public string? Cargo { get; set; }
    public string? Departamento { get; set; }
    public string? Notas { get; set; }
    internal RolContacto Clonar() => (RolContacto)MemberwiseClone();
}

/// <summary>
/// Roles de la ficha. Null en un rol significa que el tercero nunca lo ha tenido y guardar no lo toca; para darlo
/// se crea con Activo = true y para quitarlo se pone Activo = false (la fila se conserva).
/// </summary>
public sealed class RolesFicha
{
    public RolCliente? Cliente { get; set; }
    public RolProveedor? Proveedor { get; set; }
    public RolContacto? Contacto { get; set; }

    public bool EsCliente => Cliente is { Activo: true };
    public bool EsProveedor => Proveedor is { Activo: true };
    public bool EsContacto => Contacto is { Activo: true };

    internal void CopiarDe(RolesFicha otro)
    {
        Cliente = otro.Cliente?.Clonar();
        Proveedor = otro.Proveedor?.Clonar();
        Contacto = otro.Contacto?.Clonar();
    }
}

/// <summary>
/// Cuenta de un rol (terceros/roles 2026-11-01 09:52): el cliente tiene la suya y la de retención (2026-11-01 16:18);
/// el proveedor, la de proveedor y la de acreedor.
/// </summary>
public enum ClaseCuentaRol
{
    Cliente,
    Proveedor,
    Acreedor,
    Retencion
}

/// <summary>
/// Cuentas de los roles de la ficha en una sociedad, como identidades contables (null: sin cuenta). La cuenta vale
/// para todos los ejercicios de la sociedad: en cada uno se usa la cuenta de esa identidad.
/// </summary>
public sealed class CuentasRolesFicha(string idSociedad)
{
    public string IdSociedad { get; } = idSociedad;
    public int? Cliente { get; set; }
    public int? Proveedor { get; set; }
    public int? Acreedor { get; set; }
    public int? Retencion { get; set; }

    public int? De(ClaseCuentaRol clase) => clase switch
    {
        ClaseCuentaRol.Cliente => Cliente,
        ClaseCuentaRol.Proveedor => Proveedor,
        ClaseCuentaRol.Retencion => Retencion,
        _ => Acreedor
    };

    public void Fijar(ClaseCuentaRol clase, int? identidad)
    {
        switch (clase)
        {
            case ClaseCuentaRol.Cliente: Cliente = identidad; break;
            case ClaseCuentaRol.Proveedor: Proveedor = identidad; break;
            case ClaseCuentaRol.Retencion: Retencion = identidad; break;
            default: Acreedor = identidad; break;
        }
    }

    internal CuentasRolesFicha Clonar() => (CuentasRolesFicha)MemberwiseClone();
}

/// <summary>
/// Cuenta de un rol resuelta en el ejercicio de trabajo: la de su identidad en ese ejercicio o, si aún no existe,
/// la última conocida (para decir «La cuenta 4300012 aún no existe en el ejercicio 2027» y ofrecer crearla).
/// </summary>
public sealed record CuentaRol(ClaseCuentaRol Clase, int IdIdentidadContable, CuentaContable? EnEjercicio, CuentaContable? Ultima)
{
    public bool ExisteEnEjercicio => EnEjercicio is not null;
    public string Codigo => EnEjercicio?.Codigo ?? Ultima?.Codigo ?? "";
    public string Nombre => EnEjercicio?.Nombre ?? Ultima?.Nombre ?? "";
}

/// <summary>Cuentas de los roles de un tercero en la sociedad y el ejercicio de trabajo.</summary>
public sealed record CuentasRolesTercero(CuentaRol? Cliente, CuentaRol? Proveedor, CuentaRol? Acreedor, CuentaRol? Retencion = null)
{
    public CuentaRol? De(ClaseCuentaRol clase) => clase switch
    {
        ClaseCuentaRol.Cliente => Cliente,
        ClaseCuentaRol.Proveedor => Proveedor,
        ClaseCuentaRol.Retencion => Retencion,
        _ => Acreedor
    };
}

/// <summary>
/// Roles opcionales Cliente, Proveedor y Contacto (RF-TER-03), con IDs propios que conservan las referencias; dar de
/// alta un rol que ya existe es idempotente. Las cuentas son por rol y sociedad con la identidad contable (RF-TER-04,
/// terceros/roles 2026-11-01 09:52): en cada ejercicio se usa la cuenta de esa identidad. El Contacto no tiene cuenta.
/// </summary>
public sealed class ServicioRoles(IBaseDatos bd, ICatalogoCuentas cuentas)
{
    public Task<RolesTercero> ObtenerAsync(int idTercero) =>
        bd.EjecutarAsync(async s => new RolesTercero(
            idTercero,
            await s.ConsultarPrimeroAsync("SELECT IdCliente FROM Clientes WHERE IdTercero = ?", f => (int?)f.Entero("IdCliente"), idTercero),
            await s.ConsultarPrimeroAsync("SELECT IdProveedor FROM Proveedores WHERE IdTercero = ?", f => (int?)f.Entero("IdProveedor"), idTercero),
            await s.ConsultarAsync("SELECT IdContacto FROM Contactos WHERE IdTercero = ? ORDER BY IdContacto", f => f.Entero("IdContacto"), idTercero)));

    /// <summary>
    /// Da al tercero el rol Cliente si no lo tiene y devuelve su IdCliente; si estaba inactivo, lo reactiva.
    /// IdCliente no es autonumérico (conserva los números del ERP): el nuevo es el siguiente libre.
    /// </summary>
    public Task<Resultado<int>> AsegurarClienteAsync(int idTercero) => AsegurarAsync(idTercero, (s, id) => AsegurarClienteAsync(s, id));

    /// <summary>Da al tercero el rol Proveedor si no lo tiene y devuelve su IdProveedor; si estaba inactivo, lo reactiva.</summary>
    public Task<Resultado<int>> AsegurarProveedorAsync(int idTercero) => AsegurarAsync(idTercero, (s, id) => AsegurarProveedorAsync(s, id));

    /// <summary>Da al tercero el rol Contacto (persona de contacto) si no lo tiene y devuelve su IdContacto.</summary>
    public Task<Resultado<int>> AsegurarContactoAsync(int idTercero, string? cargo = null, string? departamento = null) =>
        AsegurarAsync(idTercero, (s, id) => AsegurarContactoAsync(s, id, cargo, departamento));

    internal static Task<int> AsegurarClienteAsync(ISesionDatos s, int idTercero) =>
        AsegurarEnSesionAsync(s, idTercero, "Clientes", "IdCliente", async () =>
        {
            var id = await s.SiguienteNumeroAsync("Clientes", "IdCliente");
            await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo, FechaAlta) VALUES (?, ?, ?, ?)", id, idTercero, true, DateTime.Today);
            return id;
        });

    internal static Task<int> AsegurarProveedorAsync(ISesionDatos s, int idTercero) =>
        AsegurarEnSesionAsync(s, idTercero, "Proveedores", "IdProveedor", () => Insertar(s, "INSERT INTO Proveedores (IdTercero, Activo) VALUES (?, ?)", idTercero, true));

    internal static Task<int> AsegurarContactoAsync(ISesionDatos s, int idTercero, string? cargo = null, string? departamento = null) =>
        AsegurarEnSesionAsync(s, idTercero, "Contactos", "IdContacto", () => Insertar(s, "INSERT INTO Contactos (IdTercero, Cargo, Departamento, Activo) VALUES (?, ?, ?, ?)",
            idTercero, cargo, departamento, true));

    private static async Task<int> Insertar(ISesionDatos s, string sql, params object?[] valores)
    {
        await s.EjecutarAsync(sql, valores);
        return await s.UltimoIdAsync();
    }

    private static async Task<int> AsegurarEnSesionAsync(ISesionDatos s, int idTercero, string tabla, string clave, Func<Task<int>> insertar)
    {
        var existente = await s.ConsultarPrimeroAsync($"SELECT {clave} FROM {tabla} WHERE IdTercero = ? ORDER BY {clave}", f => (int?)f.Entero(clave), idTercero);
        if (existente is int id)
        {
            await s.EjecutarAsync($"UPDATE {tabla} SET Activo = True WHERE {clave} = ?", id);
            return id;
        }
        return await insertar();
    }

    private Task<Resultado<int>> AsegurarAsync(int idTercero, Func<ISesionDatos, int, Task<int>> asegurar) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Terceros WHERE IdTercero = ?", idTercero)) == 0)
                return Resultado<int>.Fallo(new Error("Roles.SinTercero", "El tercero no existe.", $"Tercero {idTercero}",
                    "No se puede dar un rol a un tercero que no existe.", "Guarda primero la ficha del tercero."));
            return Resultado<int>.Correcto(await asegurar(s, idTercero));
        });

    // ---------- cuentas por sociedad ----------

    /// <summary>Identidades contables de las cuentas de los roles del tercero en la sociedad (para la ficha).</summary>
    public Task<CuentasRolesFicha> IdentidadesAsync(int idTercero, string idSociedad) =>
        bd.EjecutarAsync(s => IdentidadesAsync(s, idTercero, idSociedad));

    internal static async Task<CuentasRolesFicha> IdentidadesAsync(ISesionDatos s, int idTercero, string idSociedad)
    {
        var r = new CuentasRolesFicha(idSociedad);
        foreach (var clase in new[] { ClaseCuentaRol.Cliente, ClaseCuentaRol.Retencion })
            r.Fijar(clase, await s.ConsultarPrimeroAsync(
                "SELECT ccs.IdIdentidadContable FROM CuentasClientesSociedad AS ccs INNER JOIN Clientes AS c ON ccs.IdCliente = c.IdCliente WHERE c.IdTercero = ? AND ccs.IdSociedad = ? AND ccs.Clase = ?",
                f => (int?)f.Entero("IdIdentidadContable"), idTercero, idSociedad, TextoClase(clase)));
        foreach (var (clase, texto) in new[] { (ClaseCuentaRol.Proveedor, "PROVEEDOR"), (ClaseCuentaRol.Acreedor, "ACREEDOR") })
            r.Fijar(clase, await s.ConsultarPrimeroAsync(
                "SELECT cps.IdIdentidadContable FROM CuentasProveedoresSociedad AS cps INNER JOIN Proveedores AS p ON cps.IdProveedor = p.IdProveedor WHERE p.IdTercero = ? AND cps.IdSociedad = ? AND cps.Clase = ?",
                f => (int?)f.Entero("IdIdentidadContable"), idTercero, idSociedad, texto));
        return r;
    }

    /// <summary>Cuentas de los roles del tercero resueltas en el ejercicio de trabajo.</summary>
    public async Task<CuentasRolesTercero> CuentasAsync(int idTercero, ContextoSociedadEjercicio contexto)
    {
        var ids = await IdentidadesAsync(idTercero, contexto.IdSociedad);
        return new CuentasRolesTercero(
            ids.Cliente is int c ? await ResolverAsync(ClaseCuentaRol.Cliente, c, contexto) : null,
            ids.Proveedor is int p ? await ResolverAsync(ClaseCuentaRol.Proveedor, p, contexto) : null,
            ids.Acreedor is int a ? await ResolverAsync(ClaseCuentaRol.Acreedor, a, contexto) : null,
            ids.Retencion is int r ? await ResolverAsync(ClaseCuentaRol.Retencion, r, contexto) : null);
    }

    /// <summary>La cuenta de una identidad en el ejercicio de trabajo o, si aún no existe, la última conocida.</summary>
    public async Task<CuentaRol> ResolverAsync(ClaseCuentaRol clase, int idIdentidadContable, ContextoSociedadEjercicio contexto)
    {
        var enEjercicio = await cuentas.CuentaDeIdentidadAsync(contexto, idIdentidadContable);
        return new CuentaRol(clase, idIdentidadContable, enEjercicio, enEjercicio ?? await cuentas.UltimaCuentaDeIdentidadAsync(idIdentidadContable));
    }

    /// <summary>
    /// Cuenta del ejercicio con ese código para asignarla a un rol. Si el código no existe en el ejercicio, el error lo
    /// explica (la ficha ofrece crearla con el nombre del tercero).
    /// </summary>
    public async Task<Resultado<CuentaRol>> BuscarPorCodigoAsync(ClaseCuentaRol clase, string codigo, ContextoSociedadEjercicio contexto)
    {
        var cuenta = await cuentas.ResolverAsync(contexto, codigo);
        if (!cuenta.EsCorrecto)
            return Resultado<CuentaRol>.Fallo(cuenta.Error! with
            {
                Mensaje = $"La cuenta {codigo.Trim()} no existe en el ejercicio {contexto.Ejercicio}.",
                AccionCorrectora = "Revisa el código o crea la cuenta con el nombre del tercero."
            });
        return Resultado<CuentaRol>.Correcto(new CuentaRol(clase, cuenta.Valor.IdIdentidadContable, cuenta.Valor, cuenta.Valor));
    }

    /// <summary>Asigna (o quita, con null) la cuenta de un rol en una sociedad, fuera de la ficha.</summary>
    public Task<Resultado> AsignarCuentaAsync(ClaseCuentaRol clase, int idRol, string idSociedad, int? idIdentidadContable) =>
        bd.EnTransaccionAsync(async s =>
        {
            var error = await AsignarCuentaAsync(s, clase, idRol, idSociedad, idIdentidadContable);
            return error is null ? Resultado.Correcto() : Resultado.Fallo(error);
        });

    /// <summary>Identidad asignada a un rol en una sociedad, o null.</summary>
    internal static Task<int?> IdentidadAsignadaAsync(ISesionDatos s, ClaseCuentaRol clase, int idRol, string idSociedad) =>
        EsDeCliente(clase)
            ? s.ConsultarPrimeroAsync("SELECT IdIdentidadContable FROM CuentasClientesSociedad WHERE IdCliente = ? AND IdSociedad = ? AND Clase = ?",
                f => (int?)f.Entero("IdIdentidadContable"), idRol, idSociedad, TextoClase(clase))
            : s.ConsultarPrimeroAsync("SELECT IdIdentidadContable FROM CuentasProveedoresSociedad WHERE IdProveedor = ? AND IdSociedad = ? AND Clase = ?",
                f => (int?)f.Entero("IdIdentidadContable"), idRol, idSociedad, TextoClase(clase));

    /// <summary>Escribe la cuenta de un rol en la sesión; devuelve un error si la identidad no es de esa sociedad.</summary>
    internal static async Task<Error?> AsignarCuentaAsync(ISesionDatos s, ClaseCuentaRol clase, int idRol, string idSociedad, int? idIdentidadContable)
    {
        if (idIdentidadContable is int identidad)
        {
            var sociedad = await s.ConsultarPrimeroAsync("SELECT IdSociedad FROM IdentidadesContables WHERE IdIdentidadContable = ?", f => f.Texto("IdSociedad"), identidad);
            if (sociedad is null || !string.Equals(sociedad.Trim(), idSociedad.Trim(), StringComparison.OrdinalIgnoreCase))
                return new Error("Roles.CuentaDeOtraSociedad", $"La {Describir(clase)} no es de la sociedad de trabajo.", $"Identidad contable {identidad}",
                    "Las cuentas de los roles son de cada sociedad.", "Elige una cuenta de la sociedad de trabajo.");
        }
        var (tabla, clave, filtro, valores) = EsDeCliente(clase)
            ? ("CuentasClientesSociedad", "IdCliente", "IdCliente = ? AND IdSociedad = ? AND Clase = ?", new object?[] { idRol, idSociedad, TextoClase(clase) })
            : ("CuentasProveedoresSociedad", "IdProveedor", "IdProveedor = ? AND IdSociedad = ? AND Clase = ?", new object?[] { idRol, idSociedad, TextoClase(clase) });
        if (idIdentidadContable is null)
        {
            await s.EjecutarAsync($"DELETE FROM {tabla} WHERE {filtro}", valores);
            return null;
        }
        if (await s.EjecutarAsync($"UPDATE {tabla} SET IdIdentidadContable = ? WHERE {filtro}", [idIdentidadContable, .. valores]) > 0) return null;
        await s.EjecutarAsync($"INSERT INTO {tabla} ({clave}, IdSociedad, Clase, IdIdentidadContable) VALUES (?, ?, ?, ?)", idRol, idSociedad, TextoClase(clase), idIdentidadContable);
        return null;
    }

    /// <summary>La cuenta de cliente y la de retención son del rol Cliente; las otras, del rol Proveedor.</summary>
    public static bool EsDeCliente(ClaseCuentaRol clase) => clase is ClaseCuentaRol.Cliente or ClaseCuentaRol.Retencion;

    internal static string TextoClase(ClaseCuentaRol clase) => clase switch
    {
        ClaseCuentaRol.Proveedor => "PROVEEDOR",
        ClaseCuentaRol.Acreedor => "ACREEDOR",
        ClaseCuentaRol.Retencion => "RETENCION",
        _ => "CLIENTE"
    };

    /// <summary>Texto para el operador: «cuenta de cliente», «cuenta de proveedor», «cuenta de acreedor».</summary>
    public static string Describir(ClaseCuentaRol clase) => clase switch
    {
        ClaseCuentaRol.Cliente => "cuenta de cliente",
        ClaseCuentaRol.Proveedor => "cuenta de proveedor",
        ClaseCuentaRol.Retencion => "cuenta de retención",
        _ => "cuenta de acreedor"
    };
}
