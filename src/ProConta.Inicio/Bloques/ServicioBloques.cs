using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Programadas;

namespace ProConta.Inicio.Bloques;

/// <summary>Lecturas de los bloques de la fase 4 (ocupación turística y agenda). Solo lee.</summary>
public interface IFuenteBloques
{
    Task<IReadOnlyList<int>> EjerciciosAsync(string idSociedad, CancellationToken c = default);
    Task<IReadOnlyList<ReservaOcupacion>> ReservasAsync(string idSociedad, CancellationToken c = default);
    Task<IReadOnlyList<AlojamientoOcupacion>> AlojamientosAsync(string idSociedad, CancellationToken c = default);
    Task<IReadOnlyList<VencimientoProgramado>> ProgramadasAsync(string idSociedad, DateTime hasta, CancellationToken c = default);

    /// <summary>Modelos de la sociedad y lo presentado, para los plazos de la agenda (contabilidad/modelos-tributarios).</summary>
    Task<ProConta.Inicio.Salud.ModelosInicio> ModelosAsync(string idSociedad, CancellationToken c = default) => Task.FromResult(ProConta.Inicio.Salud.ModelosInicio.PorDefecto);
}

/// <summary>Lecturas de los bloques de la fase 4 en Access.</summary>
public sealed class FuenteBloquesAccess(IBaseDatos bd) : IFuenteBloques
{
    public Task<IReadOnlyList<int>> EjerciciosAsync(string idSociedad, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<int>>(async s => (await s.ConsultarAsync("SELECT Ejercicio FROM SociedadesEjercicios WHERE IdSociedad = ?",
            f => Convert.ToInt32(f.Valor("Ejercicio"), CultureInfo.InvariantCulture), idSociedad)).ToList(), c);

    public Task<IReadOnlyList<ReservaOcupacion>> ReservasAsync(string idSociedad, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<ReservaOcupacion>>(async s => await s.ConsultarAsync(
            // Todas las ventas por facturar sin anular (también las facturadas fuera, como las del ERP): la ocupación no depende de la factura.
            "SELECT IdActivo, FechaReferencia, FechaFin, Cantidad FROM VentasPorFacturar WHERE IdSociedad = ? AND Anulada = False",
            f => new ReservaOcupacion(f.EnteroNulo("IdActivo"), f.Fecha("FechaReferencia"), f.FechaNula("FechaFin"), f.DecimalNulo("Cantidad") ?? 0m), idSociedad), c);

    public Task<IReadOnlyList<AlojamientoOcupacion>> AlojamientosAsync(string idSociedad, CancellationToken c = default) =>
        bd.EjecutarAsync<IReadOnlyList<AlojamientoOcupacion>>(async s => await s.ConsultarAsync(
            // Alojamientos turísticos: los activos que tienen alguna venta por facturar o un alojamiento de Avaibook en uso.
            "SELECT a.IdActivo, a.Codigo, a.AliasActivo, a.Nombre, a.FechaAdquisicion, a.FechaAlta, a.FechaBaja FROM Activos AS a WHERE a.IdSociedad = ? AND (" +
            "EXISTS (SELECT v.IdVentaPorFacturar FROM VentasPorFacturar AS v WHERE v.IdActivo = a.IdActivo AND v.Anulada = False) OR " +
            "EXISTS (SELECT x.IdAlojamientoAvaibook FROM AlojamientosAvaibook AS x WHERE x.IdActivo = a.IdActivo AND x.Activo = True))",
            f => new AlojamientoOcupacion(f.Entero("IdActivo"),
                f.Texto("AliasActivo") is { Length: > 0 } alias ? alias.Trim() : f.Texto("Codigo") is { Length: > 0 } cod ? cod.Trim() : (f.Texto("Nombre") ?? "").Trim(),
                f.FechaNula("FechaAdquisicion") ?? f.FechaNula("FechaAlta"), f.FechaNula("FechaBaja")), idSociedad), c);

    public Task<IReadOnlyList<VencimientoProgramado>> ProgramadasAsync(string idSociedad, DateTime hasta, CancellationToken c = default) =>
        AgendaProgramadas.VencimientosAsync(bd, idSociedad, hasta, c);

    public Task<ProConta.Inicio.Salud.ModelosInicio> ModelosAsync(string idSociedad, CancellationToken c = default) => ProConta.Inicio.Salud.ModelosInicio.LeerAsync(bd, idSociedad, c);
}

/// <summary>
/// Bloques de la fase 4 de Inicio: lee una vez las reservas, los alojamientos y la agenda (hasta «Actualizar») y calcula la ocupación
/// para el ejercicio comparado que se elija sin volver a leer.
/// </summary>
public sealed class ServicioBloques(IFuenteBloques fuente, Func<DateTime>? hoy = null)
{
    private IReadOnlyList<ReservaOcupacion>? _reservas;
    private IReadOnlyList<AlojamientoOcupacion>? _alojamientos;

    public DateTime Hoy => (hoy ?? (() => DateTime.Today))().Date;

    public void Olvidar()
    {
        _reservas = null;
        _alojamientos = null;
    }

    /// <summary>Ocupación del ejercicio frente al comparado; null si la sociedad no tiene reservas.</summary>
    public async Task<PanelOcupacion?> OcupacionAsync(string idSociedad, int ejercicio, int? comparado, CancellationToken c = default)
    {
        _reservas ??= await fuente.ReservasAsync(idSociedad, c).ConfigureAwait(false);
        if (_reservas.Count == 0) return null;
        _alojamientos ??= await fuente.AlojamientosAsync(idSociedad, c).ConfigureAwait(false);
        return CalculoOcupacion.Calcular(ejercicio, comparado, CalculoInicio.FechaCorte(ejercicio, Hoy), _reservas, _alojamientos);
    }

    /// <summary>La agenda de los próximos <see cref="CalculoAgenda.Dias"/> días.</summary>
    public async Task<IReadOnlyList<EntradaAgenda>> AgendaAsync(string idSociedad, CancellationToken c = default)
    {
        var dia = Hoy;
        var ejercicios = (await fuente.EjerciciosAsync(idSociedad, c).ConfigureAwait(false)).ToHashSet();
        var programadas = await fuente.ProgramadasAsync(idSociedad, dia.AddDays(CalculoAgenda.Dias), c).ConfigureAwait(false);
        var modelos = await fuente.ModelosAsync(idSociedad, c).ConfigureAwait(false);
        return CalculoAgenda.Calcular(dia, ejercicios, programadas, null, modelos);
    }
}
