using ProConta.Datos.Persistencia;

namespace ProConta.Seguridad.Permisos;

/// <summary>
/// Política común por ámbito (RF-SEG-04): si el ámbito no tiene ninguna asignación, todo operador
/// válido tiene permiso; en cuanto tiene alguna, solo los grupos asignados. Un grupo sin filas en un
/// ámbito configurado no hereda acceso. Una tabla ausente o ilegible no es «ámbito sin configurar»:
/// la excepción de datos se propaga.
/// </summary>
public static class PoliticaAmbito
{
    /// <param name="asignaciones">Pares (grupo, recurso) de todo el ámbito.</param>
    public static bool Permite<TRecurso>(IReadOnlyCollection<(int Grupo, TRecurso Recurso)> asignaciones, IEnumerable<int> gruposOperador, TRecurso recurso, IEqualityComparer<TRecurso>? comparador = null)
    {
        if (asignaciones.Count == 0) return true;
        comparador ??= EqualityComparer<TRecurso>.Default;
        var grupos = gruposOperador.ToHashSet();
        return asignaciones.Any(a => grupos.Contains(a.Grupo) && comparador.Equals(a.Recurso, recurso));
    }
}

/// <summary>Ámbito «acceso a sociedades» (tabla AccesoSociedadesGrupo).</summary>
public sealed class AutorizadorSociedades(IBaseDatos bd, ServicioOperadores operadores)
{
    /// <summary>Filtra las sociedades a las que el operador puede acceder.</summary>
    public async Task<IReadOnlyList<string>> FiltrarAsync(int idOperador, IEnumerable<string> sociedades)
    {
        var asignaciones = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdGrupoOperadores, IdSociedad FROM AccesoSociedadesGrupo",
            f => (Grupo: f.Entero("IdGrupoOperadores"), Recurso: f.Texto("IdSociedad") ?? "")));
        var grupos = asignaciones.Count == 0 ? [] : await operadores.GruposActivosAsync(idOperador);
        return sociedades.Where(id => PoliticaAmbito.Permite(asignaciones, grupos, id, StringComparer.OrdinalIgnoreCase)).ToList();
    }
}

/// <summary>
/// Acciones con permiso propio (seguridad/permisos 2026-11-01 16:20): cada código de acción es un ámbito con la
/// regla común, en la tabla PermisosAccionesGrupo. Una acción sin filas la puede hacer cualquier operador; con alguna,
/// solo los grupos activos del operador que la tienen. Lo consultan los servicios, no solo las pantallas.
/// </summary>
public interface IAutorizadorAcciones
{
    Task<bool> PuedeAsync(string codigoAccion);
}

/// <summary>Códigos de las acciones que se autorizan hoy.</summary>
public static class Acciones
{
    public const string FacturasEditar = "FACTURAS.EDITAR";
    public const string FacturasValidar = "FACTURAS.VALIDAR";
    public const string FacturasRectificar = "FACTURAS.RECTIFICAR";
    public const string MantenimientoEditar = "MANTENIMIENTO.EDITAR";
    /// <summary>Cambiar el trimestre activo (compras 2026-11-07).</summary>
    public const string TrimestreCambiar = "TRIMESTRE.CAMBIAR";
    /// <summary>Liquidar el IVA de un trimestre y deshacerlo (contabilidad/procesos/liquidacion-iva 2026-11-16).</summary>
    public const string LiquidacionIva = "PROCESOS.LIQUIDACION_IVA";
    /// <summary>Repartir dividendos, corregir y deshacer un reparto (contabilidad/procesos/aplicacion-resultado 2026-11-16).</summary>
    public const string Dividendos = "PROCESOS.DIVIDENDOS";
    /// <summary>Contabilizar el Impuesto sobre Sociedades del ejercicio y deshacerlo (contabilidad/procesos/impuesto-sociedades 2026-11-17).</summary>
    public const string ImpuestoSociedades = "PROCESOS.IMPUESTO_SOCIEDADES";
    /// <summary>Cerrar el ejercicio, crear el siguiente y reabrirlo (contabilidad/procesos/cierre-ejercicio 2026-11-17).</summary>
    public const string CierreEjercicio = "PROCESOS.CIERRE_EJERCICIO";
    /// <summary>
    /// Contabilizar las facturas pendientes de recibir: confirmar la provisión del 31/12 con su reversión y deshacerlas
    /// (contabilidad/procesos/facturas-pendientes-recibir 2026-11-17).
    /// </summary>
    public const string FacturasPendientes = "PROCESOS.FACTURAS_PENDIENTES";
    /// <summary>Amortizar los activos: confirmar y deshacer la amortización de un periodo (contabilidad/procesos/amortizacion-de-activos 2026-11-17).</summary>
    public const string Amortizacion = "PROCESOS.AMORTIZACION";

    /// <summary>
    /// Acción que manda mientras una acción nueva no tenga ningún grupo asignado: «Cambiar el trimestre activo» la tienen
    /// al principio quienes pueden editar facturas, sin copiar datos; en cuanto se le asigna algún grupo, va por su cuenta.
    /// </summary>
    public static string? Respaldo(string codigoAccion) => codigoAccion switch
    {
        TrimestreCambiar => FacturasEditar,
        // Liquidar el IVA cierra el trimestre: mientras no tenga grupos, la tienen quienes pueden cambiar el trimestre activo.
        LiquidacionIva => TrimestreCambiar,
        // Repartir dividendos graba asientos de reservas y retenciones: mientras no tenga grupos, lo tienen quienes pueden liquidar el IVA.
        Dividendos => LiquidacionIva,
        // El Impuesto sobre Sociedades graba el gasto por impuesto y salda las 473: mientras no tenga grupos, lo tienen quienes pueden liquidar el IVA.
        ImpuestoSociedades => LiquidacionIva,
        // Cerrar y reabrir el ejercicio graba la regularización, el cierre y la apertura: mientras no tenga grupos, lo tienen quienes pueden liquidar el IVA.
        CierreEjercicio => LiquidacionIva,
        // Las facturas pendientes de recibir graban gasto contra la 4009 a 31/12 y su reversión: mientras no tenga grupos, lo tienen quienes pueden liquidar el IVA.
        FacturasPendientes => LiquidacionIva,
        // La amortización graba asientos de 68 contra 28: mientras no tenga grupos, la tienen quienes pueden liquidar el IVA.
        Amortizacion => LiquidacionIva,
        _ => null
    };
}

/// <summary>Autoriza para un operador concreto (el de la sesión).</summary>
public sealed class AutorizadorAccionesAccess(IBaseDatos bd, ServicioOperadores operadores, int idOperador) : IAutorizadorAcciones
{
    private IReadOnlyList<int>? _grupos;

    public async Task<bool> PuedeAsync(string codigoAccion)
    {
        var asignaciones = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdGrupoOperadores, CodigoAccion FROM PermisosAccionesGrupo WHERE CodigoAccion = ?",
            f => (Grupo: f.Entero("IdGrupoOperadores"), Recurso: f.Texto("CodigoAccion") ?? ""), codigoAccion));
        if (asignaciones.Count == 0) return Acciones.Respaldo(codigoAccion) is not { } respaldo || await PuedeAsync(respaldo);
        _grupos ??= await operadores.GruposActivosAsync(idOperador);
        return PoliticaAmbito.Permite(asignaciones, _grupos, codigoAccion, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Autorizador que lo permite todo (pruebas y procesos internos).</summary>
public sealed class AutorizadorTodo : IAutorizadorAcciones
{
    public static readonly AutorizadorTodo Instancia = new();
    public Task<bool> PuedeAsync(string codigoAccion) => Task.FromResult(true);
}
