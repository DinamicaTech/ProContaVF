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

/// <summary>
/// Roles opcionales Cliente, Proveedor y Contacto (RF-TER-03), con IDs propios que conservan las
/// referencias; dar de alta un rol que ya existe es idempotente. Las cuentas son por rol y ejercicio
/// (RF-TER-04): el Contacto no tiene cuenta.
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
    public Task<Resultado<int>> AsegurarClienteAsync(int idTercero) =>
        AsegurarAsync(idTercero, "Clientes", "IdCliente", async s =>
        {
            var id = await s.SiguienteNumeroAsync("Clientes", "IdCliente");
            await s.EjecutarAsync("INSERT INTO Clientes (IdCliente, IdTercero, Activo, FechaAlta) VALUES (?, ?, ?, ?)", id, idTercero, true, DateTime.Today);
            return id;
        });

    /// <summary>Da al tercero el rol Proveedor si no lo tiene y devuelve su IdProveedor; si estaba inactivo, lo reactiva.</summary>
    public Task<Resultado<int>> AsegurarProveedorAsync(int idTercero) =>
        AsegurarAsync(idTercero, "Proveedores", "IdProveedor", s => Insertar(s, "INSERT INTO Proveedores (IdTercero, Activo) VALUES (?, ?)", idTercero, true));

    /// <summary>Da al tercero el rol Contacto (persona de contacto) si no lo tiene y devuelve su IdContacto.</summary>
    public Task<Resultado<int>> AsegurarContactoAsync(int idTercero, string? cargo = null, string? departamento = null) =>
        AsegurarAsync(idTercero, "Contactos", "IdContacto", s => Insertar(s, "INSERT INTO Contactos (IdTercero, Cargo, Departamento, Activo) VALUES (?, ?, ?, ?)",
            idTercero, cargo, departamento, true));

    private static async Task<int> Insertar(ISesionDatos s, string sql, params object?[] valores)
    {
        await s.EjecutarAsync(sql, valores);
        return await s.UltimoIdAsync();
    }

    /// <summary>Cuenta contable del cliente en el ejercicio (la predeterminada), si la tiene.</summary>
    public Task<CuentaContable?> CuentaClienteAsync(int idCliente, ContextoSociedadEjercicio contexto) =>
        CuentaAsync("CuentasClientesEjercicio", "IdCliente", idCliente, contexto);

    /// <summary>Cuenta contable del proveedor en el ejercicio (la predeterminada), si la tiene.</summary>
    public Task<CuentaContable?> CuentaProveedorAsync(int idProveedor, ContextoSociedadEjercicio contexto) =>
        CuentaAsync("CuentasProveedoresEjercicio", "IdProveedor", idProveedor, contexto);

    /// <summary>Asigna la cuenta predeterminada del cliente en el ejercicio; la cuenta debe ser de ese ejercicio.</summary>
    public Task<Resultado> AsignarCuentaClienteAsync(int idCliente, ContextoSociedadEjercicio contexto, int idCuentaContable) =>
        AsignarCuentaAsync("CuentasClientesEjercicio", "IdCliente", idCliente, contexto, idCuentaContable);

    /// <summary>Asigna la cuenta predeterminada del proveedor en el ejercicio; la cuenta debe ser de ese ejercicio.</summary>
    public Task<Resultado> AsignarCuentaProveedorAsync(int idProveedor, ContextoSociedadEjercicio contexto, int idCuentaContable) =>
        AsignarCuentaAsync("CuentasProveedoresEjercicio", "IdProveedor", idProveedor, contexto, idCuentaContable);

    private Task<Resultado<int>> AsegurarAsync(int idTercero, string tabla, string clave, Func<ISesionDatos, Task<int>> insertar) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Terceros WHERE IdTercero = ?", idTercero)) == 0)
                return Resultado<int>.Fallo(new Error("Roles.SinTercero", "El tercero no existe.", $"Tercero {idTercero}",
                    "No se puede dar un rol a un tercero que no existe.", "Guarda primero la ficha del tercero."));
            var existente = await s.ConsultarPrimeroAsync($"SELECT {clave} FROM {tabla} WHERE IdTercero = ? ORDER BY {clave}", f => (int?)f.Entero(clave), idTercero);
            if (existente is int id)
            {
                await s.EjecutarAsync($"UPDATE {tabla} SET Activo = True WHERE {clave} = ?", id);
                return Resultado<int>.Correcto(id);
            }
            return Resultado<int>.Correcto(await insertar(s));
        });

    private async Task<CuentaContable?> CuentaAsync(string tabla, string clave, int id, ContextoSociedadEjercicio contexto)
    {
        var idCuenta = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            $"SELECT IdCuentaContable FROM {tabla} WHERE {clave} = ? AND IdSociedadEjercicio = ? ORDER BY EsPredeterminada, IdCuentaContable",
            f => (int?)f.Entero("IdCuentaContable"), id, contexto.IdSociedadEjercicio));
        // ORDER BY de Access pone True (-1) antes que False (0): la predeterminada primero.
        return idCuenta is int c ? await cuentas.ObtenerAsync(c) : null;
    }

    private async Task<Resultado> AsignarCuentaAsync(string tabla, string clave, int id, ContextoSociedadEjercicio contexto, int idCuentaContable)
    {
        var cuenta = await cuentas.ObtenerAsync(idCuentaContable);
        if (cuenta is null || cuenta.IdSociedadEjercicio != contexto.IdSociedadEjercicio)
            return Resultado.Fallo(new Error("Roles.CuentaDeOtroEjercicio", "La cuenta no es de este ejercicio.", $"Cuenta {idCuentaContable}",
                "Las cuentas de los roles son por sociedad y ejercicio.", "Elige una cuenta del ejercicio de trabajo."));
        return await bd.EnTransaccionAsync(async s =>
        {
            await s.EjecutarAsync($"UPDATE {tabla} SET EsPredeterminada = False WHERE {clave} = ? AND IdSociedadEjercicio = ?", id, contexto.IdSociedadEjercicio);
            var actualizadas = await s.EjecutarAsync($"UPDATE {tabla} SET EsPredeterminada = True WHERE {clave} = ? AND IdSociedadEjercicio = ? AND IdCuentaContable = ?",
                id, contexto.IdSociedadEjercicio, idCuentaContable);
            if (actualizadas == 0)
                await s.EjecutarAsync($"INSERT INTO {tabla} ({clave}, IdSociedadEjercicio, IdCuentaContable, EsPredeterminada) VALUES (?, ?, ?, ?)",
                    id, contexto.IdSociedadEjercicio, idCuentaContable, true);
            return Resultado.Correcto();
        });
    }
}
