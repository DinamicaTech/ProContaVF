using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.Roles;

/// <summary>Qué ha pasado con la subcuenta de un rol al guardar la ficha.</summary>
public enum TipoCambioSubcuenta
{
    /// <summary>Se ha creado la siguiente libre y se ha asignado al rol.</summary>
    Creada,
    /// <summary>No se ha podido crear (sin códigos libres, error de datos…).</summary>
    NoCreada,
    /// <summary>Se ha borrado del plan en todos sus ejercicios y se ha quitado del rol.</summary>
    Eliminada,
    /// <summary>Tenía apuntes de hace más de 5 años: se ha borrado solo en los ejercicios donde estaba vacía.</summary>
    EliminadaEnRecientes,
    /// <summary>Se conserva (movimientos recientes, la usa otro tercero o rol, o la usa otro registro).</summary>
    Conservada
}

/// <summary>Cambio en la subcuenta de un rol, con el texto para el operador.</summary>
public sealed record CambioSubcuenta(ClaseCuentaRol Clase, TipoCambioSubcuenta Tipo, string Codigo, string Texto, int? IdIdentidadContable = null);

/// <summary>
/// Subcuentas automáticas de los roles (terceros/roles 2026-11-16): al dar un rol se crea y asigna la siguiente subcuenta
/// libre de su cuenta (cliente 430, acreedor 410, empleado 465; con «Auto» también proveedor 400 y retención 473) con el
/// nombre del tercero; al quitarlo, su subcuenta se borra si no tiene movimientos en los últimos 5 años y no la usa nadie más.
/// </summary>
public sealed class SubcuentasRoles(IBaseDatos bd, ICatalogoCuentas cuentas, IReloj? reloj = null)
{
    /// <summary>Años sin movimientos que tiene que tener la subcuenta de un rol quitado para borrarla.</summary>
    public const int AniosSinMovimientos = 5;

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>Cuenta de la que cuelgan las subcuentas de cada clase.</summary>
    public static string Raiz(ClaseCuentaRol clase) => clase switch
    {
        ClaseCuentaRol.Cliente => "430",
        ClaseCuentaRol.Proveedor => "400",
        ClaseCuentaRol.Retencion => "473",
        ClaseCuentaRol.Empleado => "465",
        ClaseCuentaRol.Dividendos => "526",
        ClaseCuentaRol.RetencionDividendos => "4751",
        _ => "410"
    };

    /// <summary>Nombre de la subcuenta: el del tercero («Retención IRPF …» en la de retención), recortado a lo que admite el plan.</summary>
    public static string NombreCuenta(ClaseCuentaRol clase, string nombreTercero)
    {
        var n = (clase == ClaseCuentaRol.Retencion ? "Retención IRPF " : "") + (nombreTercero ?? "").Trim();
        return n.Length <= ReglasCuenta.LongitudMaximaNombre ? n : n[..ReglasCuenta.LongitudMaximaNombre];
    }

    /// <summary>
    /// Cuentas que hay que crear al guardar: las de los roles activos sin cuenta en la sociedad. El proveedor sin cuenta de
    /// proveedor ni de acreedor recibe una de acreedor (410), como en Compras; la de retención no se crea sola.
    /// </summary>
    public static IReadOnlyList<ClaseCuentaRol> QueCrear(RolesFicha roles, CuentasRolesFicha? cuentasFicha)
    {
        if (cuentasFicha is null) return [];
        var r = new List<ClaseCuentaRol>();
        if (roles.EsCliente && cuentasFicha.Cliente is null) r.Add(ClaseCuentaRol.Cliente);
        if (roles.EsProveedor && cuentasFicha.Proveedor is null && cuentasFicha.Acreedor is null) r.Add(ClaseCuentaRol.Acreedor);
        if (roles.EsEmpleado && roles.Empleado!.IdSociedad == cuentasFicha.IdSociedad && cuentasFicha.Empleado is null) r.Add(ClaseCuentaRol.Empleado);
        if (roles.EsSocio && roles.Socio!.IdSociedad == cuentasFicha.IdSociedad)
        {
            if (cuentasFicha.Dividendos is null) r.Add(ClaseCuentaRol.Dividendos);
            if (cuentasFicha.RetencionDividendos is null) r.Add(ClaseCuentaRol.RetencionDividendos);
        }
        return r;
    }

    /// <summary>Subcuenta que hay que intentar borrar tras guardar: la de un rol que estaba activo y se ha quitado.</summary>
    public sealed record SubcuentaQuitada(ClaseCuentaRol Clase, int IdRol, int IdIdentidadContable);

    /// <summary>Las cuentas de los roles que estaban activos en <paramref name="antes"/> y ya no lo están en <paramref name="guardado"/>.</summary>
    public static IReadOnlyList<SubcuentaQuitada> QueEliminar(Tercero antes, Tercero guardado)
    {
        if (guardado.CuentasRoles is not { } c) return [];
        var r = new List<SubcuentaQuitada>();
        void Anadir(bool estaba, bool esta, int? idRol, params ClaseCuentaRol[] clases)
        {
            if (!estaba || esta || idRol is not int id) return;
            foreach (var clase in clases)
                if (c.De(clase) is int identidad) r.Add(new SubcuentaQuitada(clase, id, identidad));
        }
        Anadir(antes.Roles.EsCliente, guardado.Roles.EsCliente, guardado.Roles.Cliente?.IdCliente, ClaseCuentaRol.Cliente, ClaseCuentaRol.Retencion);
        Anadir(antes.Roles.EsProveedor, guardado.Roles.EsProveedor, guardado.Roles.Proveedor?.IdProveedor, ClaseCuentaRol.Proveedor, ClaseCuentaRol.Acreedor);
        if (guardado.Roles.Empleado is { } e && e.IdSociedad == c.IdSociedad)
            Anadir(antes.Roles.EsEmpleado, guardado.Roles.EsEmpleado, e.IdEmpleado, ClaseCuentaRol.Empleado);
        if (guardado.Roles.Socio is { } so && so.IdSociedad == c.IdSociedad)
            Anadir(antes.Roles.EsSocio, guardado.Roles.EsSocio, so.IdSocio, ClaseCuentaRol.Dividendos, ClaseCuentaRol.RetencionDividendos);
        return r;
    }

    /// <summary>Crea en el ejercicio de trabajo la siguiente subcuenta libre de la clase, con el nombre del tercero (no la asigna).</summary>
    public async Task<Resultado<CuentaContable>> CrearAsync(ClaseCuentaRol clase, string nombreTercero, ContextoSociedadEjercicio contexto)
    {
        var raiz = Raiz(clase);
        var nombre = NombreCuenta(clase, nombreTercero);
        if (nombre.Trim().Length == 0)
            return Resultado<CuentaContable>.Fallo(new Error("Roles.SinNombreParaCuenta", $"No puedo crear la {ServicioRoles.Describir(clase)} porque el tercero no tiene nombre.",
                null, "La subcuenta se crea con el nombre del tercero.", "Escribe el nombre o la razón social del tercero."));
        var codigo = await cuentas.SiguienteSubcuentaAsync(contexto, raiz);
        if (codigo is null)
            return Resultado<CuentaContable>.Fallo(new Error("Roles.SinCodigosLibres", $"No quedan códigos libres en la cuenta {raiz} del ejercicio {contexto.Ejercicio}.",
                $"Cuenta {raiz}", null, $"Escribe a mano el código de una subcuenta {raiz} que ya exista."));
        var padre = await cuentas.ResolverAsync(contexto, raiz);
        // La 4751 no suele existir como cuenta: la subcuenta cuelga entonces de la cuenta del prefijo más largo (la 475).
        if (!padre.EsCorrecto && raiz.Length > 3) return await cuentas.AltaSubcuentaAsync(contexto, codigo, nombre);
        return await cuentas.AltaAsync(contexto, new AltaCuenta(codigo, nombre, padre.EsCorrecto ? padre.Valor.IdCuentaContable : null));
    }

    /// <summary>
    /// Borra la subcuenta de un rol quitado si no tiene movimientos en los últimos 5 años y ningún otro tercero o rol la usa.
    /// Si se borra del todo, también se quita del rol y queda en el historial de la ficha.
    /// </summary>
    public async Task<CambioSubcuenta> EliminarAsync(int idTercero, SubcuentaQuitada q, ContextoSociedadEjercicio contexto, Actor actor)
    {
        var ultima = await cuentas.UltimaCuentaDeIdentidadAsync(q.IdIdentidadContable);
        var codigo = ultima?.Codigo.Trim() ?? "";
        var describir = ServicioRoles.Describir(q.Clase);
        var otrosUsos = await bd.EjecutarAsync(s => OtrosUsosAsync(s, q));
        if (otrosUsos > 0)
            return new CambioSubcuenta(q.Clase, TipoCambioSubcuenta.Conservada, codigo,
                $"La {describir} {codigo} se conserva porque la usa también otro tercero o rol.", q.IdIdentidadContable);

        var ahora = _reloj.Ahora;
        var desde = ahora.Date.AddYears(-AniosSinMovimientos);
        var r = await cuentas.EliminarSinMovimientosDesdeAsync(contexto, q.IdIdentidadContable, desde, async (s, completo) =>
        {
            if (!completo) return;
            // Quitar la cuenta (null) nunca falla: solo se comprueba la sociedad al asignar una.
            await ServicioRoles.AsignarCuentaAsync(s, q.Clase, q.IdRol, contexto.IdSociedad, null);
            await RepositorioTercerosAccess.HistorialAsync(s, idTercero, Guid.NewGuid().ToString(), ahora, actor, Entidad(q.Clase), q.IdRol, "MODIFICACION",
                $"{Campo(q.Clase)} {contexto.IdSociedad}", $"{codigo} {ultima?.Nombre.Trim()}".Trim(), null);
        });
        if (!r.EsCorrecto)
            return new CambioSubcuenta(q.Clase, TipoCambioSubcuenta.Conservada, codigo,
                $"La {describir} {codigo} se conserva: {r.Error!.Mensaje}{(r.Error.Causa is { } c ? " " + c : "")}", q.IdIdentidadContable);
        var b = r.Valor;
        if (b.Completo)
            return new CambioSubcuenta(q.Clase, TipoCambioSubcuenta.Eliminada, b.Codigo, $"He borrado la {describir} {b.Codigo} «{b.Nombre}», que no tenía movimientos.", q.IdIdentidadContable);
        if (b.Ejercicios.Count == 0)
            return new CambioSubcuenta(q.Clase, TipoCambioSubcuenta.Conservada, b.Codigo,
                $"La {describir} {b.Codigo} se conserva porque tiene apuntes antiguos en todos sus ejercicios.", q.IdIdentidadContable);
        return new CambioSubcuenta(q.Clase, TipoCambioSubcuenta.EliminadaEnRecientes, b.Codigo,
            $"He borrado la {describir} {b.Codigo} de {string.Join(", ", b.Ejercicios)}, donde estaba vacía; se conserva en los ejercicios con apuntes de hace más de {AniosSinMovimientos} años.",
            q.IdIdentidadContable);
    }

    /// <summary>
    /// Tras cambiar la subcuenta de un rol y pasar sus apuntes a la nueva (terceros/roles/cambio-subcuenta), borra la antigua con la
    /// misma regla que al quitar un rol (sin movimientos en los últimos 5 años y sin otro tercero o registro que la use), sin tocar el
    /// rol, que ya tiene la nueva. Devuelve el texto para el aviso.
    /// </summary>
    public async Task<string> EliminarAntiguaAsync(ClaseCuentaRol clase, int idIdentidadAntigua, ContextoSociedadEjercicio contexto)
    {
        var ultima = await cuentas.UltimaCuentaDeIdentidadAsync(idIdentidadAntigua);
        var codigo = ultima?.Codigo.Trim() ?? "";
        if (await bd.EjecutarAsync(s => OtrosUsosAsync(s, new SubcuentaQuitada(clase, -1, idIdentidadAntigua))) > 0)
            return $"La {codigo} se conserva porque la usa otro tercero o rol.";
        var r = await cuentas.EliminarSinMovimientosDesdeAsync(contexto, idIdentidadAntigua, _reloj.Ahora.Date.AddYears(-AniosSinMovimientos));
        if (!r.EsCorrecto) return $"La {codigo} se conserva: {r.Error!.Mensaje}{(r.Error.Causa is { } c ? " " + c : "")}";
        var b = r.Valor;
        if (b.Completo) return $"He borrado la {b.Codigo} «{b.Nombre}», que ya no tiene movimientos.";
        if (b.Ejercicios.Count == 0) return $"La {b.Codigo} se conserva porque tiene movimientos recientes en un ejercicio cerrado.";
        return $"He borrado la {b.Codigo} de {string.Join(", ", b.Ejercicios)}, donde ha quedado vacía; se conserva en los ejercicios con movimientos.";
    }

    /// <summary>Cuántas asignaciones de roles (de cualquier tercero) usan la identidad, sin contar la del rol que se quita.</summary>
    internal static async Task<int> OtrosUsosAsync(ISesionDatos s, SubcuentaQuitada q)
    {
        int Contar(object? v) => Convert.ToInt32(v, System.Globalization.CultureInfo.InvariantCulture);
        var clientes = Contar(await s.EscalarAsync("SELECT COUNT(*) FROM CuentasClientesSociedad WHERE IdIdentidadContable = ? AND NOT (IdCliente = ? AND Clase = ?)",
            q.IdIdentidadContable, ServicioRoles.EsDeCliente(q.Clase) ? q.IdRol : -1, ServicioRoles.TextoClase(q.Clase)));
        var proveedores = Contar(await s.EscalarAsync("SELECT COUNT(*) FROM CuentasProveedoresSociedad WHERE IdIdentidadContable = ? AND NOT (IdProveedor = ? AND Clase = ?)",
            q.IdIdentidadContable, !ServicioRoles.EsDeCliente(q.Clase) && q.Clase != ClaseCuentaRol.Empleado && !ServicioRoles.EsDeSocio(q.Clase) ? q.IdRol : -1, ServicioRoles.TextoClase(q.Clase)));
        var empleados = Contar(await s.EscalarAsync("SELECT COUNT(*) FROM EmpleadosSociedad WHERE IdIdentidadContable = ? AND IdEmpleado <> ?",
            q.IdIdentidadContable, q.Clase == ClaseCuentaRol.Empleado ? q.IdRol : -1));
        var socios = Contar(await s.EscalarAsync("SELECT COUNT(*) FROM SociosSociedad WHERE IdIdentidadDividendos = ? AND IdSocio <> ?", q.IdIdentidadContable,
                q.Clase == ClaseCuentaRol.Dividendos ? q.IdRol : -1))
            + Contar(await s.EscalarAsync("SELECT COUNT(*) FROM SociosSociedad WHERE IdIdentidadRetencion = ? AND IdSocio <> ?", q.IdIdentidadContable,
                q.Clase == ClaseCuentaRol.RetencionDividendos ? q.IdRol : -1));
        return clientes + proveedores + empleados + socios;
    }

    private static string Entidad(ClaseCuentaRol clase) =>
        clase == ClaseCuentaRol.Empleado ? "Empleado" : ServicioRoles.EsDeSocio(clase) ? "Socio" : ServicioRoles.EsDeCliente(clase) ? "Cliente" : "Proveedor";

    /// <summary>Campo del historial, el mismo que escribe el guardado de la ficha.</summary>
    private static string Campo(ClaseCuentaRol clase) => clase switch
    {
        ClaseCuentaRol.Cliente => "CuentaCliente",
        ClaseCuentaRol.Retencion => "CuentaRetencion",
        ClaseCuentaRol.Proveedor => "CuentaProveedor",
        ClaseCuentaRol.Empleado => "Cuenta465",
        ClaseCuentaRol.Dividendos => "Cuenta526",
        ClaseCuentaRol.RetencionDividendos => "Cuenta4751",
        _ => "CuentaAcreedor"
    };
}
