using System.Globalization;
using System.Text;
using ProConta.Datos.Persistencia;

namespace ProConta.Contabilidad.Mayor;

/// <summary>
/// El Mayor sobre la BD, sin cambiar su estructura: Asientos y Apuntes (movimientos, alta y último cambio del asiento), libro de
/// IVA repercutido y soportado (fecha de la factura), CuentasContables (nombres) y RegistroCambiosApuntes (historial). Cada
/// carga de la tabla hace un número fijo de consultas, sea cual sea el número de apuntes: los apuntes de las cuentas, todas las
/// líneas de sus asientos (para la contrapartida), las fechas de factura y los nombres.
/// </summary>
public sealed class FuenteMayorAccess(IBaseDatos bd) : IFuenteMayor
{
    public Task<IReadOnlyList<EjercicioMayor>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var filas = await s.ConsultarAsync(
                "SELECT se.IdSociedadEjercicio, se.Ejercicio, se.EjercicioCerrado, " +
                "(SELECT COUNT(*) FROM Asientos AS a WHERE a.IdSociedadEjercicio = se.IdSociedadEjercicio) AS N " +
                "FROM SociedadesEjercicios AS se WHERE se.IdSociedad = ?",
                f => (Id: f.Entero("IdSociedadEjercicio"), Anio: (f.Texto("Ejercicio") ?? "").Trim(), Cerrado: !f.EsNulo("EjercicioCerrado") && f.Logico("EjercicioCerrado"),
                    N: Convert.ToInt32(f.Valor("N") ?? 0, CultureInfo.InvariantCulture)),
                idSociedad);
            return (IReadOnlyList<EjercicioMayor>)filas
                .Where(x => int.TryParse(x.Anio, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                .Select(x => new EjercicioMayor(x.Id, int.Parse(x.Anio, CultureInfo.InvariantCulture), x.Cerrado, x.N > 0))
                .OrderBy(e => e.Ejercicio).ToList();
        }, cancelacion);

    public Task<(IReadOnlyList<SumaCuenta> Sumas, IReadOnlyList<PlanCuenta> Plan)> CuentasAsync(IReadOnlyList<EjercicioMayor> ejercicios, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            if (ejercicios.Count == 0) return ((IReadOnlyList<SumaCuenta>)[], (IReadOnlyList<PlanCuenta>)[]);
            var en = ListaIds(ejercicios);
            var sumas = await s.ConsultarAsync(
                "SELECT p.CuentaContable, a.IdSociedadEjercicio, a.TipoAsiento, SUM(p.ImporteDebe) AS D, SUM(p.ImporteHaber) AS H, COUNT(*) AS N " +
                "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                $"WHERE a.IdSociedadEjercicio IN ({en}) AND a.EstadoAsiento = 1 AND a.TipoAsiento <> {TiposAsientoMayor.Cierre} " +
                "GROUP BY p.CuentaContable, a.IdSociedadEjercicio, a.TipoAsiento",
                f => new SumaCuenta((f.Texto("CuentaContable") ?? "").Trim(), f.Entero("IdSociedadEjercicio"), Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture),
                    f.DecimalNulo("D") ?? 0m, f.DecimalNulo("H") ?? 0m, Convert.ToInt32(f.Valor("N"), CultureInfo.InvariantCulture)));
            var plan = await LeerPlanAsync(s, en);
            return ((IReadOnlyList<SumaCuenta>)sumas.Where(x => x.Cuenta.Length > 0).ToList(), (IReadOnlyList<PlanCuenta>)plan);
        }, cancelacion);

    public Task<DatosMayor> ApuntesAsync(SolicitudMayor solicitud, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            if (solicitud.Ejercicios.Count == 0 || (solicitud.Cuentas.Count == 0 && !solicitud.BuscaFacturas)) return DatosMayor.Vacio;
            var en = ListaIds(solicitud.Ejercicios);
            var anioDe = solicitud.Ejercicios.ToDictionary(e => e.IdSociedadEjercicio, e => e.Ejercicio);
            var (condicion, parametros) = Condicion(solicitud);
            var desde = "FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                        $"WHERE a.IdSociedadEjercicio IN ({en}) AND a.EstadoAsiento = 1 AND a.TipoAsiento <> {TiposAsientoMayor.Cierre} AND ({condicion})";

            // Fechas de la factura del libro de IVA (repercutido y soportado), por asiento: dos consultas agrupadas para todo el periodo.
            var fechasFactura = new Dictionary<int, DateTime>();
            foreach (var tabla in new[] { "IVARepercutido", "IVASoportado" })
                foreach (var (id, fecha) in await s.ConsultarAsync(
                             $"SELECT r.IdAsiento, MIN(r.FechaExpedicion) AS F FROM {tabla} AS r WHERE r.IdSociedadEjercicio IN ({en}) AND r.IdAsiento IS NOT NULL GROUP BY r.IdAsiento",
                             f => (f.Entero("IdAsiento"), f.FechaNula("F"))))
                    if (fecha is { } d && (!fechasFactura.TryGetValue(id, out var otra) || d < otra)) fechasFactura[id] = d.Date;

            var apuntes = await s.ConsultarAsync(
                "SELECT p.IdApunte, p.IdAsiento, p.Orden, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.Descripcion, p.NumeroDocumento, p.Punteado, " +
                "a.IdSociedadEjercicio, a.FechaContable, a.TipoAsiento, a.Descripcion AS DescripcionAsiento, a.Notas, a.FechaHoraCreacion, a.FechaHoraModificacion, a.EsAutomatico " + desde,
                f =>
                {
                    var idAsiento = f.Entero("IdAsiento");
                    var ej = f.Entero("IdSociedadEjercicio");
                    var concepto = f.Texto("Descripcion") is { Length: > 0 } d && d.Trim().Length > 0 ? d : f.Texto("DescripcionAsiento") ?? "";
                    return new ApunteMayor(
                        f.Entero("IdApunte"), idAsiento, f.EsNulo("Orden") ? 0 : Convert.ToInt32(f.Valor("Orden"), CultureInfo.InvariantCulture), ej, anioDe[ej],
                        f.Fecha("FechaContable").Date, Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture), (f.Texto("CuentaContable") ?? "").Trim(),
                        f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m, concepto.Trim(),
                        f.Texto("NumeroDocumento")?.Trim() is { Length: > 0 } doc ? doc : null,
                        f.Texto("Notas")?.Trim() is { Length: > 0 } n ? n : null,
                        fechasFactura.TryGetValue(idAsiento, out var fd) ? fd : null, f.FechaNula("FechaHoraCreacion"), f.FechaNula("FechaHoraModificacion"),
                        !f.EsNulo("Punteado") && f.Logico("Punteado"), !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico"));
                },
                parametros);
            if (solicitud.BuscaFacturas)
            {
                // La BD compara el texto tal cual; aquí se deja solo lo que de verdad es la misma factura.
                var buscadas = solicitud.Facturas!.ToHashSet(StringComparer.Ordinal);
                apuntes = apuntes.Where(a => buscadas.Contains(Facturas.Normalizar(a.Documento))).ToList();
            }
            else
                apuntes = apuntes.Where(a => solicitud.Incluye(a.Cuenta)).ToList();

            // Todas las líneas de esos asientos, con una sola consulta: la de los apuntes, como tabla derivada.
            var lineas = await s.ConsultarAsync(
                "SELECT q.IdApunte, q.IdAsiento, q.Orden, q.CuentaContable, q.ImporteDebe, q.ImporteHaber FROM Apuntes AS q INNER JOIN " +
                $"(SELECT DISTINCT p.IdAsiento {desde}) AS x ON q.IdAsiento = x.IdAsiento",
                f => new LineaContrapartida(f.Entero("IdAsiento"), f.EsNulo("Orden") ? 0 : Convert.ToInt32(f.Valor("Orden"), CultureInfo.InvariantCulture),
                    (f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m, f.Entero("IdApunte")),
                parametros);

            var nombres = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var p in (await LeerPlanAsync(s, en)).OrderBy(p => anioDe.GetValueOrDefault(p.IdSociedadEjercicio)))
                nombres[p.Codigo] = p.Nombre;
            return new DatosMayor(apuntes, lineas, nombres);
        }, cancelacion);

    public Task<AsientoMayor?> AsientoAsync(int idAsiento, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var cab = (await s.ConsultarAsync(
                "SELECT a.IdAsiento, a.IdSociedadEjercicio, se.Ejercicio, a.FechaContable, a.Descripcion, a.TipoAsiento, a.Notas, a.EsAutomatico, a.MotorPropietario, " +
                "a.OperadorCreacion, a.FechaHoraCreacion, a.OperadorModificacion, a.FechaHoraModificacion " +
                "FROM Asientos AS a INNER JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE a.IdAsiento = ?",
                f => (IdEj: f.Entero("IdSociedadEjercicio"), Anio: int.TryParse((f.Texto("Ejercicio") ?? "").Trim(), out var an) ? an : 0, Fecha: f.Fecha("FechaContable").Date,
                    Desc: (f.Texto("Descripcion") ?? "").Trim(), Tipo: Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture), Notas: f.Texto("Notas"),
                    Auto: !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico"), Motor: f.Texto("MotorPropietario"),
                    OpC: f.Texto("OperadorCreacion"), FC: f.FechaNula("FechaHoraCreacion"), OpM: f.Texto("OperadorModificacion"), FM: f.FechaNula("FechaHoraModificacion")),
                idAsiento)).FirstOrDefault();
            if (cab.Fecha == default) return null;
            var lineas = await s.ConsultarAsync(
                "SELECT p.Orden, p.CuentaContable, c.Nombre, p.Descripcion, p.NumeroDocumento, p.ImporteDebe, p.ImporteHaber " +
                "FROM Apuntes AS p LEFT JOIN (SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?) AS c ON p.CuentaContable = c.Codigo " +
                "WHERE p.IdAsiento = ? ORDER BY p.Orden",
                f => new LineaAsientoMayor(f.EsNulo("Orden") ? 0 : Convert.ToInt32(f.Valor("Orden"), CultureInfo.InvariantCulture), (f.Texto("CuentaContable") ?? "").Trim(),
                    (f.Texto("Nombre") ?? "").Trim(), (f.Texto("Descripcion") is { Length: > 0 } d ? d : cab.Desc).Trim(), f.Texto("NumeroDocumento")?.Trim(),
                    f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m),
                cab.IdEj, idAsiento);
            return new AsientoMayor(idAsiento, cab.Anio, cab.Fecha, cab.Desc, cab.Tipo, cab.Notas, cab.Auto, cab.Motor, cab.FC, cab.OpC, cab.FM, cab.OpM, lineas);
        }, cancelacion);

    public Task<IReadOnlyList<CambioApunteMayor>> HistorialAsync(int idAsiento, int idApunte, int orden, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s => (IReadOnlyList<CambioApunteMayor>)(await s.ConsultarAsync(
                "SELECT FechaHora, Operador, Motivo, Operacion, Campo, ValorAntiguo, ValorNuevo, IdLote FROM RegistroCambiosApuntes " +
                "WHERE IdAsiento = ? AND (IdApunte = ? OR (IdApunte IS NULL AND Orden = ?)) ORDER BY FechaHora DESC, IdCambioApunte DESC",
                f => new CambioApunteMayor(f.Fecha("FechaHora"), f.Texto("Operador") ?? "", f.Texto("Motivo") ?? "", f.Texto("Operacion") ?? "", f.Texto("Campo") ?? "",
                    f.Texto("ValorAntiguo"), f.Texto("ValorNuevo"), f.Texto("IdLote") ?? ""),
                idAsiento, idApunte, orden)).ToList(), cancelacion);

    public Task<IReadOnlyList<CuadreCuenta>> CuadresAsync(IReadOnlyList<EjercicioMayor> ejercicios, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s => ejercicios.Count == 0 ? (IReadOnlyList<CuadreCuenta>)[] : (await s.ConsultarAsync(
            $"SELECT IdSociedadEjercicio, CuentaContable, Saldo, Comentario, Operador, FechaHora FROM CuadresCuentas WHERE IdSociedadEjercicio IN ({ListaIds(ejercicios)})",
            f => new CuadreCuenta(f.Entero("IdSociedadEjercicio"), (f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("Saldo") ?? 0m, f.Texto("Comentario"),
                f.Texto("Operador") ?? "", f.Fecha("FechaHora")))).ToList(), cancelacion);

    public Task GuardarCuadresAsync(IReadOnlyList<CuadreCuenta> marcar, IReadOnlyList<(int IdSociedadEjercicio, string Cuenta)> quitar, CancellationToken cancelacion = default) =>
        bd.EnTransaccionAsync(async s =>
        {
            foreach (var (ej, cuenta) in quitar.Concat(marcar.Select(m => (m.IdSociedadEjercicio, m.Cuenta))).Distinct())
                await s.EjecutarAsync("DELETE FROM CuadresCuentas WHERE IdSociedadEjercicio = ? AND CuentaContable = ?", ej, cuenta);
            foreach (var m in marcar)
                await s.EjecutarAsync("INSERT INTO CuadresCuentas (IdSociedadEjercicio, CuentaContable, Saldo, Comentario, Operador, FechaHora) VALUES (?, ?, ?, ?, ?, ?)",
                    m.IdSociedadEjercicio, m.Cuenta, Math.Round(m.Saldo, 2), string.IsNullOrWhiteSpace(m.Comentario) ? null : m.Comentario.Trim(),
                    m.Operador.Length > 30 ? m.Operador[..30] : m.Operador, m.FechaHora);
            return true;
        });

    public Task<decimal> UmbralCuadreAsync(CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var v = await s.EscalarAsync("SELECT TOP 1 UmbralCuadreMayor FROM Configuracion ORDER BY IdConfiguracion");
            return v is null or DBNull ? CuadresMayor.UmbralPredeterminado : Math.Abs(Convert.ToDecimal(v, CultureInfo.InvariantCulture));
        }, cancelacion);

    private static async Task<IReadOnlyList<PlanCuenta>> LeerPlanAsync(ISesionDatos s, string en) =>
        (await s.ConsultarAsync($"SELECT Codigo, Nombre, EsImputable, IdSociedadEjercicio FROM CuentasContables WHERE IdSociedadEjercicio IN ({en})",
            f => new PlanCuenta((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), !f.EsNulo("EsImputable") && f.Logico("EsImputable"), f.Entero("IdSociedadEjercicio"))))
        .Where(p => p.Codigo.Length > 0).ToList();

    /// <summary>Lista de ids para IN (enteros: no hay inyección posible).</summary>
    private static string ListaIds(IEnumerable<EjercicioMayor> ejercicios) =>
        string.Join(",", ejercicios.Select(e => e.IdSociedadEjercicio.ToString(CultureInfo.InvariantCulture)).Distinct());

    /// <summary>Condición de las cuentas (= exacta o LIKE prefijo) o de las facturas (sus formas grabadas), con sus parámetros.</summary>
    internal static (string Sql, object?[] Parametros) Condicion(SolicitudMayor s)
    {
        var sql = new StringBuilder();
        var ps = new List<object?>();
        if (s.BuscaFacturas)
        {
            var variantes = s.Facturas!.SelectMany(Facturas.Variantes).Distinct(StringComparer.Ordinal).ToList();
            sql.Append("p.NumeroDocumento IN (").Append(string.Join(",", variantes.Select(_ => "?"))).Append(')');
            ps.AddRange(variantes);
        }
        else
        {
            foreach (var c in s.Cuentas)
            {
                if (sql.Length > 0) sql.Append(" OR ");
                if (s.Exactas.Contains(c)) { sql.Append("p.CuentaContable = ?"); ps.Add(c); }
                else { sql.Append("p.CuentaContable LIKE ?"); ps.Add(c.Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%"); }
            }
        }
        return (sql.ToString(), ps.ToArray());
    }
}
