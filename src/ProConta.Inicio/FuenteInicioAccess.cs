using System.Globalization;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Norma43.Importacion;

namespace ProConta.Inicio;

/// <summary>
/// Lecturas de Inicio en Access (solo SELECT). Asientos: solo los contabilizados (EstadoAsiento = 1) y nunca los de cierre;
/// el resultado quita además la regularización al calcular. Ventas: facturas validadas (estados 1 y 2) por fecha de factura,
/// sin suplidos; si el ejercicio no tiene ninguna, el grupo 70 de la contabilidad.
/// </summary>
public sealed class FuenteInicioAccess(IBaseDatos bd) : IFuenteInicio
{
    private const string AsientosValidos = "a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 3";

    public Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync<IReadOnlyList<EjercicioSociedad>>(async s =>
        {
            var filas = await s.ConsultarAsync(
                "SELECT IdSociedadEjercicio, Ejercicio, TrimestreActivo, [PorcentajeProrrataAñoAnterior] AS Prorrata FROM SociedadesEjercicios WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdSociedadEjercicio"), Anio: f.Texto("Ejercicio"), Trimestre: f.EnteroNulo("TrimestreActivo"),
                    Prorrata: f.EsNulo("Prorrata") ? (decimal?)null : Convert.ToDecimal(f.Valor("Prorrata"), CultureInfo.InvariantCulture)),
                idSociedad);
            return filas.Where(x => int.TryParse(x.Anio, out _))
                .Select(x => new EjercicioSociedad(x.Id, int.Parse(x.Anio!, CultureInfo.InvariantCulture), x.Trimestre, x.Prorrata))
                .OrderByDescending(e => e.Ejercicio).ToList();
        }, cancelacion);

    public Task<DatosEjercicio> DatosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var id = ejercicio.IdSociedadEjercicio;
            var movimientos = await s.ConsultarAsync(
                "SELECT a.FechaContable, a.TipoAsiento, p.CuentaContable, SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) AS D, SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) AS H " +
                $"FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento WHERE {AsientosValidos} GROUP BY a.FechaContable, a.TipoAsiento, p.CuentaContable",
                f => new MovimientoDia(f.Fecha("FechaContable").Date, (f.Texto("CuentaContable") ?? "").Trim(), Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture),
                    f.DecimalNulo("D") ?? 0m, f.DecimalNulo("H") ?? 0m),
                id);
            // Varias filas del mismo día y cuenta si FechaContable lleva hora: se juntan.
            var agrupados = movimientos.GroupBy(m => (m.Fecha, m.Cuenta, m.TipoAsiento))
                .Select(g => g.Count() == 1 ? g.First() : new MovimientoDia(g.Key.Fecha, g.Key.Cuenta, g.Key.TipoAsiento, g.Sum(m => m.Debe), g.Sum(m => m.Haber))).ToList();

            var ventas = await s.ConsultarAsync(
                "SELECT f.FechaFactura, l.BaseImponible, l.IdCentroCoste FROM Facturas AS f INNER JOIN LineasFactura AS l ON f.IdFactura = l.IdFactura " +
                "WHERE f.IdSociedadEjercicio = ? AND f.EstadoFactura IN (1, 2) AND l.EsSuplido = False AND f.FechaFactura IS NOT NULL",
                f => new LineaVenta(f.Fecha("FechaFactura").Date, f.DecimalNulo("BaseImponible") ?? 0m, f.EnteroNulo("IdCentroCoste")), id);
            var deContabilidad = false;
            if (ventas.Count == 0)
            {
                ventas = await s.ConsultarAsync(
                    "SELECT a.FechaContable, p.ImporteDebe, p.ImporteHaber, p.IdCentroCoste FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
                    $"WHERE {AsientosValidos} AND a.TipoAsiento <> 2 AND LEFT(p.CuentaContable, 2) = '70'",
                    f => new LineaVenta(f.Fecha("FechaContable").Date, (f.DecimalNulo("ImporteHaber") ?? 0m) - (f.DecimalNulo("ImporteDebe") ?? 0m), f.EnteroNulo("IdCentroCoste")), id);
                deContabilidad = ventas.Count > 0;
            }

            var cartera = await s.ConsultarAsync(
                "SELECT p.CuentaContable, a.FechaContable, p.FechaVencimiento, p.ImporteDebe, p.ImporteHaber FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
                $"WHERE {AsientosValidos} AND LEFT(p.CuentaContable, 3) BETWEEN '430' AND '436'",
                f => new ApunteCartera((f.Texto("CuentaContable") ?? "").Trim(), f.Fecha("FechaContable").Date, f.FechaNula("FechaVencimiento")?.Date,
                    f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m), id);

            var iva = await s.ConsultarAsync(
                "SELECT a.FechaContable, p.CuentaContable, p.ImporteDebe, p.ImporteHaber FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
                $"WHERE {AsientosValidos} AND a.TipoAsiento <> 2 AND LEFT(p.CuentaContable, 3) IN ('472', '477') " +
                "AND NOT EXISTS (SELECT q.IdApunte FROM Apuntes AS q WHERE q.IdAsiento = a.IdAsiento AND LEFT(q.CuentaContable, 4) IN ('4700', '4750'))",
                f => new ApunteIva(f.Fecha("FechaContable").Date, (f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m), id);

            var nombres = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (codigo, nombre) in await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                         f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), id))
                if (codigo.Length > 0) nombres.TryAdd(codigo, nombre);

            return new DatosEjercicio(ejercicio, agrupados, ventas, deContabilidad, cartera, iva, nombres);
        }, cancelacion);

    public async Task<IReadOnlyList<BancoN43>> BancosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default)
    {
        var cuentas = await new ConsultaBancos(bd).CuentasAsync(new ContextoSociedadEjercicio(idSociedad, ejercicio.IdSociedadEjercicio, ejercicio.Ejercicio));
        return cuentas.Where(c => c.CodigoContable is { Length: > 0 })
            .Select(c => new BancoN43(string.IsNullOrWhiteSpace(c.Nombre) ? c.NombreContable ?? c.Ccc : c.Nombre!.Trim(), c.CodigoContable!.Trim(), c.FechaSaldoBanco))
            .ToList();
    }

    public async Task<IReadOnlyDictionary<int, ActividadInicio>> ActividadesPorCentroAsync(string idSociedad, CancellationToken cancelacion = default)
    {
        var porCentro = await new LectorActividadesIva(bd).PorCentroAsync(idSociedad);
        return porCentro.ToDictionary(p => p.Key, p => new ActividadInicio(p.Value.Nombre, p.Value.Deducible, p.Value.Serie));
    }

    public Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync<IReadOnlyList<ActividadInicio>>(async s => await s.ConsultarAsync(
            "SELECT Nombre, Deducible, SerieCuentaIva FROM Actividades WHERE IdSociedad = ? AND Activa = True",
            f => new ActividadInicio((f.Texto("Nombre") ?? "").Trim(), f.EsNulo("Deducible") ? (byte)2 : Convert.ToByte(f.Valor("Deducible"), CultureInfo.InvariantCulture),
                string.IsNullOrWhiteSpace(f.Texto("SerieCuentaIva")) ? null : f.Texto("SerieCuentaIva")!.Trim()),
            idSociedad), cancelacion);
}
