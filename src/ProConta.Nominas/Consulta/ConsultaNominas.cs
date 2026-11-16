using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nominas.Importacion;
using ProConta.Nominas.Lectura;
using ProConta.Nucleo.Contexto;
using ProConta.Terceros.Roles;

namespace ProConta.Nominas.Consulta;

/// <summary>Una importación de nóminas (un periodo y una paga) con sus totales, para la pantalla de gestión.</summary>
public sealed record ImportacionNominaResumen(
    int IdImportacion,
    DateTime? PeriodoDesde,
    DateTime? PeriodoHasta,
    TipoPaga TipoPaga,
    string? TextoPaga,
    DateTime FechaNomina,
    OrigenNominas Origen,
    string NombreFichero,
    string Estado,
    int? IdAdjuntoRecibido,
    int? IdImportacionSustituida,
    int Version,
    DateTime FechaHoraImportacion,
    string? Operador,
    DateTime? FechaHoraEstado,
    string? OperadorEstado,
    string? MotivoEstado,
    int Trabajadores,
    int SinResolver,
    int ConAsiento,
    decimal Bruto,
    decimal SSEmp,
    decimal Irpf,
    decimal Neto,
    decimal CosteEmpresa)
{
    public bool EsVigente => Estado is not (ServicioImportacionNominas.EstadoReemplazada or ServicioImportacionNominas.EstadoDeshecha);

    /// <summary>Mes (día 1) que cubre la nómina.</summary>
    public DateTime Mes => (PeriodoDesde ?? FechaNomina) is var f ? new DateTime(f.Year, f.Month, 1) : default;

    public string TextoPeriodo => Mes.ToString("MMMM yyyy", CultureInfo.GetCultureInfo("es-ES")) is var t ? char.ToUpper(t[0], CultureInfo.GetCultureInfo("es-ES")) + t[1..] : "";
}

/// <summary>Una nómina guardada: la fila del fichero con su empleado, su cuenta 465 y su asiento.</summary>
public sealed record NominaGuardada(int IdNomina, int IdImportacion, int? IdEmpleado, int? IdTercero, string? NombreTercero, string? Cuenta465, int? IdAsiento,
    FilaNomina Fila);

/// <summary>Una nómina de un empleado, con el periodo y el estado de su importación (pestaña Nóminas de la ficha del tercero).</summary>
public sealed record NominaEmpleado(NominaGuardada Nomina, ImportacionNominaResumen Importacion, int Ejercicio);

/// <summary>Datos del rol Empleado de un tercero en una sociedad, para la pestaña Nóminas de su ficha.</summary>
public sealed record EmpleadoNominas(int IdEmpleado, string? CodigoTrabajador, string? Cuenta465, string? NombreCuenta465, DateTime? PrimeraNomina, bool Activo);

/// <summary>Lecturas de nóminas para las pantallas (nominas/pantallas): importaciones con totales, sus nóminas y las de un empleado.</summary>
public sealed class ConsultaNominas(IBaseDatos bd)
{
    private const string SqlImportaciones =
        "SELECT i.*, se.Ejercicio, " +
        "(SELECT COUNT(*) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina) AS Trabajadores, " +
        "(SELECT COUNT(*) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina AND n.IdEmpleado IS NULL) AS SinResolver, " +
        "(SELECT COUNT(*) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina AND n.IdAsiento IS NOT NULL) AS ConAsiento, " +
        "(SELECT SUM(n.Bruto) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina) AS TBruto, " +
        "(SELECT SUM(n.SSEmp) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina) AS TSSEmp, " +
        "(SELECT SUM(n.IRPF111) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina) AS TIrpf, " +
        "(SELECT SUM(n.Neto) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina) AS TNeto, " +
        "(SELECT SUM(n.CostEmp) FROM Nominas AS n WHERE n.IdImportacionNomina = i.IdImportacionNomina) AS TCoste " +
        "FROM ImportacionesNomina AS i INNER JOIN SociedadesEjercicios AS se ON i.IdSociedadEjercicio = se.IdSociedadEjercicio ";

    /// <summary>Importaciones del ejercicio, la más reciente primero; las reemplazadas y deshechas solo si se piden.</summary>
    public async Task<IReadOnlyList<ImportacionNominaResumen>> ImportacionesAsync(ContextoSociedadEjercicio ctx, bool incluirAntiguas = false)
    {
        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync(SqlImportaciones + "WHERE i.IdSociedadEjercicio = ? ORDER BY i.PeriodoDesde DESC, i.IdImportacionNomina DESC",
            Mapear, ctx.IdSociedadEjercicio));
        return Versionar(filas).Where(i => incluirAntiguas || i.EsVigente).ToList();
    }

    public async Task<ImportacionNominaResumen?> ImportacionAsync(int idImportacion)
    {
        var i = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(SqlImportaciones + "WHERE i.IdImportacionNomina = ?", Mapear, idImportacion));
        if (i is null) return null;
        var hermanas = await bd.EjecutarAsync(s => s.ConsultarAsync(SqlImportaciones + "WHERE i.IdSociedadEjercicio = (SELECT IdSociedadEjercicio FROM ImportacionesNomina WHERE IdImportacionNomina = ?)",
            Mapear, idImportacion));
        return Versionar(hermanas).FirstOrDefault(x => x.IdImportacion == idImportacion) ?? i;
    }

    /// <summary>Número de versión dentro de cada periodo y paga: 1 la primera importada, 2 la que la reemplazó…</summary>
    private static List<ImportacionNominaResumen> Versionar(IReadOnlyList<ImportacionNominaResumen> filas)
    {
        var porId = filas.ToDictionary(f => f.IdImportacion);
        int Version(ImportacionNominaResumen f, int profundidad = 0) =>
            f.IdImportacionSustituida is int ant && porId.TryGetValue(ant, out var a) && profundidad < 50 ? Version(a, profundidad + 1) + 1 : 1;
        return filas.Select(f => f with { Version = Version(f) }).ToList();
    }

    private static ImportacionNominaResumen Mapear(IFilaDatos f) => new(
        f.Entero("IdImportacionNomina"), f.EsNulo("PeriodoDesde") ? null : f.Fecha("PeriodoDesde"), f.EsNulo("PeriodoHasta") ? null : f.Fecha("PeriodoHasta"),
        f.Texto("TipoPaga") == "EXTRA" ? TipoPaga.Extra : TipoPaga.Mensual, f.Texto("TextoPaga"), f.Fecha("FechaNomina"),
        (f.Texto("Origen") ?? "").ToUpperInvariant() switch { "CORREO" => OrigenNominas.Correo, "MIGRACION" => OrigenNominas.Migracion, _ => OrigenNominas.Fichero },
        f.Texto("NombreFichero") ?? "", f.Texto("Estado") ?? "", f.EnteroNulo("IdAdjuntoRecibido"), f.EnteroNulo("IdImportacionSustituida"), 1,
        f.Fecha("FechaHoraImportacion"), f.Texto("Operador"), f.EsNulo("FechaHoraEstado") ? null : f.Fecha("FechaHoraEstado"), f.Texto("OperadorEstado"),
        f.Texto("MotivoEstado"), f.Entero("Trabajadores"), f.Entero("SinResolver"), f.Entero("ConAsiento"), Suma(f, "TBruto"), Suma(f, "TSSEmp"), Suma(f, "TIrpf"),
        Suma(f, "TNeto"), Suma(f, "TCoste"));

    private static decimal Suma(IFilaDatos f, string campo) => f.DecimalNulo(campo) ?? 0m;

    /// <summary>Nóminas de una importación en el orden del fichero, con su empleado, su 465 del ejercicio y su asiento.</summary>
    public Task<IReadOnlyList<NominaGuardada>> NominasAsync(int idImportacion) => bd.EjecutarAsync(s => s.ConsultarAsync(
        "SELECT n.*, e.IdTercero, t.Nombre AS NombreTercero, " +
        "(SELECT TOP 1 c.Codigo FROM (EmpleadosSociedad AS es INNER JOIN CuentasContables AS c ON es.IdIdentidadContable = c.IdIdentidadContable) " +
        " INNER JOIN SociedadesEjercicios AS se ON es.IdSociedad = se.IdSociedad " +
        " WHERE es.IdEmpleado = n.IdEmpleado AND se.IdSociedadEjercicio = i.IdSociedadEjercicio AND c.IdSociedadEjercicio = i.IdSociedadEjercicio) AS Cuenta465 " +
        "FROM ((Nominas AS n INNER JOIN ImportacionesNomina AS i ON n.IdImportacionNomina = i.IdImportacionNomina) " +
        "LEFT JOIN Empleados AS e ON n.IdEmpleado = e.IdEmpleado) LEFT JOIN Terceros AS t ON e.IdTercero = t.IdTercero " +
        "WHERE n.IdImportacionNomina = ? ORDER BY n.Ordinal", MapearNomina, idImportacion));

    private static NominaGuardada MapearNomina(IFilaDatos f)
    {
        var importes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var c in CamposNomina.Importes)
            if (f.DecimalNulo(c.Nombre) is decimal d) importes[c.Nombre] = d;
        var fila = new FilaNomina(f.EnteroNulo("FilaOrigen") ?? 0, f.Entero("Ordinal"), f.Texto("CodigoTrabajador"), f.Texto("NombreOrigen") ?? "", f.Texto("NifOrigen"),
            f.Texto("TipoPaga") == "EXTRA" ? TipoPaga.Extra : TipoPaga.Mensual, f.Fecha("FechaNomina"), importes);
        return new NominaGuardada(f.Entero("IdNomina"), f.Entero("IdImportacionNomina"), f.EnteroNulo("IdEmpleado"), f.EnteroNulo("IdTercero"), f.Texto("NombreTercero"),
            f.Texto("Cuenta465"), f.EnteroNulo("IdAsiento"), fila);
    }

    /// <summary>El rol Empleado del tercero en la sociedad del contexto, con su 465 (del ejercicio, o la última) y su primera nómina; null si no es empleado.</summary>
    public Task<EmpleadoNominas?> EmpleadoAsync(ContextoSociedadEjercicio ctx, int idTercero) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT e.IdEmpleado, e.Activo, es.CodigoTrabajador, es.Activo AS ActivoSociedad, " +
        "(SELECT TOP 1 c.Codigo FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
        " WHERE c.IdIdentidadContable = es.IdIdentidadContable ORDER BY IIf(c.IdSociedadEjercicio = ?, 0, 1), se.Ejercicio DESC) AS Cuenta, " +
        "(SELECT TOP 1 c.Nombre FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
        " WHERE c.IdIdentidadContable = es.IdIdentidadContable ORDER BY IIf(c.IdSociedadEjercicio = ?, 0, 1), se.Ejercicio DESC) AS NombreCuenta, " +
        "(SELECT MIN(n.FechaNomina) FROM (Nominas AS n INNER JOIN ImportacionesNomina AS i ON n.IdImportacionNomina = i.IdImportacionNomina) " +
        " INNER JOIN SociedadesEjercicios AS se ON i.IdSociedadEjercicio = se.IdSociedadEjercicio " +
        " WHERE n.IdEmpleado = e.IdEmpleado AND se.IdSociedad = es.IdSociedad AND i.Estado NOT IN ('REEMPLAZADA', 'DESHECHA')) AS Primera " +
        "FROM Empleados AS e INNER JOIN EmpleadosSociedad AS es ON e.IdEmpleado = es.IdEmpleado WHERE e.IdTercero = ? AND es.IdSociedad = ?",
        f => new EmpleadoNominas(f.Entero("IdEmpleado"), f.Texto("CodigoTrabajador"), f.Texto("Cuenta"), f.Texto("NombreCuenta"),
            f.EsNulo("Primera") ? null : f.Fecha("Primera"), f.Logico("Activo") && f.Logico("ActivoSociedad")),
        ctx.IdSociedadEjercicio, ctx.IdSociedadEjercicio, idTercero, ctx.IdSociedad));

    /// <summary>Nóminas vigentes del empleado en la sociedad del contexto, de todos los ejercicios, la más reciente primero.</summary>
    public async Task<IReadOnlyList<NominaEmpleado>> DeEmpleadoAsync(ContextoSociedadEjercicio ctx, int idEmpleado)
    {
        var ids = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT DISTINCT i.IdImportacionNomina, se.Ejercicio FROM (Nominas AS n INNER JOIN ImportacionesNomina AS i ON n.IdImportacionNomina = i.IdImportacionNomina) " +
            "INNER JOIN SociedadesEjercicios AS se ON i.IdSociedadEjercicio = se.IdSociedadEjercicio " +
            "WHERE n.IdEmpleado = ? AND se.IdSociedad = ? AND i.Estado NOT IN ('REEMPLAZADA', 'DESHECHA')",
            f => (Id: f.Entero("IdImportacionNomina"), Ejercicio: int.Parse((f.Texto("Ejercicio") ?? "0").Trim(), CultureInfo.InvariantCulture)), idEmpleado, ctx.IdSociedad));
        var resultado = new List<NominaEmpleado>();
        foreach (var (id, ejercicio) in ids)
        {
            if (await ImportacionAsync(id) is not { } imp) continue;
            foreach (var n in (await NominasAsync(id)).Where(n => n.IdEmpleado == idEmpleado))
                resultado.Add(new NominaEmpleado(n, imp, ejercicio));
        }
        return resultado.OrderByDescending(r => r.Nomina.Fila.FechaNomina).ThenByDescending(r => r.Importacion.IdImportacion).ToList();
    }

    /// <summary>Candidatos para el trabajador de una nómina sin reconocer (mismo NIF, nombre parecido); nunca se asigna solo.</summary>
    public Task<ResolucionTrabajador> CandidatosAsync(ContextoSociedadEjercicio ctx, NominaGuardada n) =>
        bd.EjecutarAsync(s => ServicioEmpleados.BuscarAsync(s, ctx.IdSociedad, n.Fila.CodigoTrabajador, n.Fila.Nif, n.Fila.Nombre));
}
