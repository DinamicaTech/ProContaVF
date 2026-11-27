using System.Globalization;
using ProConta.Activos.Subcuentas;
using ProConta.Contabilidad.Amortizacion;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Impuestos;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Gestion;

/// <summary>
/// Pantalla de activos sobre Access (activos 2026-11-17 18:50): listado con las cifras del ejercicio de trabajo, ficha, guardar, alta en una
/// transacción, apertura por centro de coste, cuenta de resultados por activo y ejercicio, tabla de amortización y la regla de bloqueo del tipo.
/// Los cálculos son los de <see cref="CalculoCuentaResultados"/>, <see cref="TablaAmortizacionActivo"/> y <see cref="ReglasFichaActivo"/>; la
/// amortización prevista sale del proceso (<see cref="ServicioAmortizacion.PrevistaAnualAsync"/>) y los gastos generales, del Impuesto sobre
/// Sociedades (<see cref="ServicioImpuestoSociedades.GastosGeneralesAsync"/>). Ninguna operación graba asientos.
/// </summary>
public sealed partial class ServicioActivos
{
    private readonly IBaseDatos _bd;
    private readonly ICatalogoCuentas _cuentas;
    private readonly Func<Task<bool>>? _puede;
    private readonly ServicioAmortizacion _amortizacion;
    private readonly ServicioImpuestoSociedades _impuesto;

    public ServicioActivos(IBaseDatos bd, ICatalogoCuentas cuentas, Func<Task<bool>>? puede = null)
    {
        _bd = bd;
        _cuentas = cuentas;
        _puede = puede;
        Generador = new GeneradorSubcuentasActivo(bd, cuentas);
        var motor = new MotorAsientosAccess(bd);
        _amortizacion = new ServicioAmortizacion(bd, motor);
        _impuesto = new ServicioImpuestoSociedades(bd, motor, cuentas);
    }

    /// <summary>El proceso «Generar subcuentas» de la fase 2.</summary>
    public GeneradorSubcuentasActivo Generador { get; }

    private const int TipoApertura = 1, TipoRegularizacion = 2, TipoCierre = 3;

    /// <summary>Si el operador tiene el permiso de activos (ver y modificar).</summary>
    public async Task<bool> PuedeAsync() => _puede is null || await _puede();

    private static Error SinPermiso(string registro) => new("Activos.SinPermiso", "No tienes permiso para ver ni modificar los activos.", registro,
        "La acción «Activos» tiene grupos asignados (o, sin grupos, la de editar Parámetros y tablas) y el tuyo no está entre ellos.",
        "Pídeselo a quien administra los permisos.");

    // ---------- catálogos ----------

    public Task<IReadOnlyList<TipoActivo>> TiposAsync() => _bd.EjecutarAsync<IReadOnlyList<TipoActivo>>(async s =>
    {
        await CategoriasAsync(s);
        return await s.ConsultarAsync("SELECT IdTipoActivo, Codigo, Nombre, EsAmortizablePredeterminado FROM TiposActivo WHERE EstaActivo = True ORDER BY IdTipoActivo",
            f => new TipoActivo(f.Entero("IdTipoActivo"), (f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), f.Logico("EsAmortizablePredeterminado")));
    });

    /// <summary>Qué tipos son inversiones financieras por su categoría (<see cref="TiposActivoPgc.RegistrarCategorias"/>).</summary>
    private static async Task<bool> CategoriasAsync(ISesionDatos s)
    {
        TiposActivoPgc.RegistrarCategorias(await s.ConsultarAsync("SELECT Codigo, Categoria FROM TiposActivo", f => ((f.Texto("Codigo") ?? "").Trim(), f.Texto("Categoria"))));
        return true;
    }

    public Task<IReadOnlyList<CentroParaActivo>> CentrosAsync(string idSociedad) => _bd.EjecutarAsync<IReadOnlyList<CentroParaActivo>>(async s =>
        await s.ConsultarAsync("SELECT IdCentroCoste, Codigo, Nombre, IdCentroCostePadre, Nivel, EsImputable, EstaActivo FROM CentrosCoste WHERE IdSociedad = ? ORDER BY RutaOrden",
            f => new CentroParaActivo(f.Entero("IdCentroCoste"), (f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), f.EnteroNulo("IdCentroCostePadre"),
                Convert.ToInt32(f.Valor("Nivel"), CultureInfo.InvariantCulture), f.Logico("EsImputable"), f.Logico("EstaActivo")), idSociedad));

    public Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad) => _bd.EjecutarAsync<IReadOnlyList<EjercicioSociedad>>(async s =>
        await s.ConsultarAsync("SELECT IdSociedadEjercicio, Ejercicio FROM SociedadesEjercicios WHERE IdSociedad = ? ORDER BY Ejercicio DESC",
            f => new EjercicioSociedad(f.Entero("IdSociedadEjercicio"), f.Entero("Ejercicio")), idSociedad));

    /// <summary>
    /// Uso de cada tipo en la sociedad, para el alta (ergonomía de activos 2026-11-26): cuántos activos propios tiene y el centro del último dado de alta,
    /// del que se propone su padre para el centro nuevo.
    /// </summary>
    public Task<IReadOnlyList<UsoTipoActivo>> UsoTiposAsync(string idSociedad) => _bd.EjecutarAsync<IReadOnlyList<UsoTipoActivo>>(async s =>
    {
        var filas = await s.ConsultarAsync("SELECT IdActivo, IdTipoActivo, IdCentroCoste FROM Activos WHERE IdSociedad = ? AND EsAjeno = False ORDER BY IdActivo DESC",
            f => (Tipo: f.Entero("IdTipoActivo"), Centro: f.EnteroNulo("IdCentroCoste")), idSociedad);
        return filas.GroupBy(f => f.Tipo).Select(g => new UsoTipoActivo(g.Key, g.Count(), g.Select(f => f.Centro).FirstOrDefault(c => c is not null))).ToList();
    });

    /// <summary>Número de líneas activas de la plantilla de un tipo (para decir cuántas subcuentas se crearán).</summary>
    public Task<int> LineasPlantillaAsync(int idTipoActivo) => _bd.EjecutarAsync(async s =>
        Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PlantillasSubcuentasActivo WHERE IdTipoActivo = ? AND EstaActiva = True", idTipoActivo), CultureInfo.InvariantCulture));

    // ---------- lecturas comunes ----------

    private sealed record FilaActivo(int Id, int IdTipo, string CodigoTipo, string Tipo, string Codigo, string Nombre, string? Alias, string? Notas, DateTime? Compra,
        decimal Coste, bool Amortizable, int? Centro, bool Ajeno, DateTime? Baja, int Version);

    private sealed record FilaPlan(int IdPlan, int IdActivo, DateTime Inicio, decimal Base, decimal Residual, decimal? Pct, bool Activo);

    /// <summary>Una subcuenta del activo: su enlace, la identidad, el rol de su línea y su línea de plantilla.</summary>
    private sealed record EnlaceSubcuenta(int IdSubcuentaActivo, int IdActivo, int IdIdentidad, string Rol, int IdPlantilla);

    private const string SelectActivos =
        "SELECT a.IdActivo, a.IdTipoActivo, t.Codigo AS CodigoTipo, t.Nombre AS NombreTipo, a.Codigo, a.Nombre, a.AliasActivo, a.Descripcion, a.FechaAdquisicion, " +
        "a.CosteAdquisicionActual, a.EsAmortizable, a.IdCentroCoste, a.EsAjeno, a.FechaBaja, a.Version FROM Activos AS a INNER JOIN TiposActivo AS t ON a.IdTipoActivo = t.IdTipoActivo ";

    private static FilaActivo LeerActivo(IFilaDatos f) => new(f.Entero("IdActivo"), f.Entero("IdTipoActivo"), (f.Texto("CodigoTipo") ?? "").Trim(), (f.Texto("NombreTipo") ?? "").Trim(),
        (f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), Vacio(f.Texto("AliasActivo")), Vacio(f.Texto("Descripcion")), f.FechaNula("FechaAdquisicion"),
        f.DecimalNulo("CosteAdquisicionActual") ?? 0m, f.Logico("EsAmortizable"), f.EnteroNulo("IdCentroCoste"), f.Logico("EsAjeno"), f.FechaNula("FechaBaja"), f.Entero("Version"));

    private static async Task<IReadOnlyList<FilaPlan>> PlanesAsync(ISesionDatos s, string idSociedad) =>
        await s.ConsultarAsync(
            "SELECT p.IdPlanAmortizacion, p.IdActivo, p.FechaInicio, p.BaseAmortizable, p.ValorResidual, p.PorcentajeAnual, p.EstaActivo FROM PlanesAmortizacionActivo AS p " +
            "INNER JOIN Activos AS a ON p.IdActivo = a.IdActivo WHERE a.IdSociedad = ?",
            f => new FilaPlan(f.Entero("IdPlanAmortizacion"), f.Entero("IdActivo"), f.Fecha("FechaInicio"), f.DecimalNulo("BaseAmortizable") ?? 0m, f.DecimalNulo("ValorResidual") ?? 0m,
                f.DecimalNulo("PorcentajeAnual"), f.Logico("EstaActivo")), idSociedad);

    /// <summary>El plan que manda: el activo de inicio más reciente (como el proceso); sin ninguno activo, el más reciente.</summary>
    private static FilaPlan? PlanDe(IEnumerable<FilaPlan> planes, int idActivo) =>
        planes.Where(p => p.IdActivo == idActivo).OrderByDescending(p => p.Activo).ThenByDescending(p => p.Inicio).ThenByDescending(p => p.IdPlan).FirstOrDefault();

    private static async Task<IReadOnlyList<EnlaceSubcuenta>> EnlacesAsync(ISesionDatos s, string idSociedad, int? idActivo = null) =>
        await s.ConsultarAsync(
            "SELECT sa.IdSubcuentaActivo, sa.IdActivo, sa.IdIdentidadContable, t.Rol, sa.IdPlantillaSubcuentaActivo FROM ((SubcuentasActivo AS sa " +
            "INNER JOIN Activos AS a ON sa.IdActivo = a.IdActivo) INNER JOIN PlantillasSubcuentasActivo AS p ON sa.IdPlantillaSubcuentaActivo = p.IdPlantillaSubcuentaActivo) " +
            "INNER JOIN TiposSubcuentaActivo AS t ON p.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo WHERE a.IdSociedad = ?" + (idActivo is null ? "" : " AND sa.IdActivo = ?"),
            f => new EnlaceSubcuenta(f.Entero("IdSubcuentaActivo"), f.Entero("IdActivo"), f.Entero("IdIdentidadContable"), (f.Texto("Rol") ?? "").Trim(),
                f.Entero("IdPlantillaSubcuentaActivo")),
            idActivo is null ? [idSociedad] : [idSociedad, idActivo]);

    /// <summary>Código y nombre de cada cuenta del ejercicio, y el código de cada identidad.</summary>
    private static async Task<(Dictionary<string, string> Nombres, Dictionary<int, string> CodigoDe)> CuentasEjercicioAsync(ISesionDatos s, int idSociedadEjercicio)
    {
        var filas = await s.ConsultarAsync("SELECT Codigo, Nombre, IdIdentidadContable FROM CuentasContables WHERE IdSociedadEjercicio = ?",
            f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim(), Identidad: f.EnteroNulo("IdIdentidadContable")), idSociedadEjercicio);
        var nombres = new Dictionary<string, string>(StringComparer.Ordinal);
        var codigos = new Dictionary<int, string>();
        foreach (var c in filas.Where(c => c.Codigo.Length > 0))
        {
            nombres.TryAdd(c.Codigo, c.Nombre);
            if (c.Identidad is int i && (!codigos.TryGetValue(i, out var ya) || string.CompareOrdinal(c.Codigo, ya) < 0)) codigos[i] = c.Codigo;
        }
        return (nombres, codigos);
    }

    /// <summary>Apuntes en firme del ejercicio de los grupos 6 y 7 y de las 28 (para la amortización acumulada), sin el cierre.</summary>
    private static async Task<IReadOnlyList<ApunteResultado>> ApuntesEjercicioAsync(ISesionDatos s, int idSociedadEjercicio) =>
        await s.ConsultarAsync(
            "SELECT p.CuentaContable, a.FechaContable, p.ImporteDebe, p.ImporteHaber, p.IdCentroCoste, a.TipoAsiento FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 3 AND (p.CuentaContable LIKE '6%' OR p.CuentaContable LIKE '7%' OR p.CuentaContable LIKE '28%')",
            f => new ApunteResultado((f.Texto("CuentaContable") ?? "").Trim(), f.Fecha("FechaContable"), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m,
                f.EnteroNulo("IdCentroCoste"), Convert.ToInt32(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture)),
            idSociedadEjercicio);

    private static Dictionary<int, string> TextosCentros(IEnumerable<CentroParaActivo> centros) => centros.ToDictionary(c => c.IdCentroCoste, c => c.Texto);

    /// <summary>Criterio de cada activo en un ejercicio: sus subcuentas (códigos del ejercicio) y su centro, compartido o no.</summary>
    private static Dictionary<int, CriterioActivo> Criterios(IReadOnlyList<FilaActivo> activos, IReadOnlyList<EnlaceSubcuenta> enlaces, IReadOnlyDictionary<int, string> codigoDe)
    {
        var porCentro = activos.Where(a => a.Centro is not null).GroupBy(a => a.Centro!.Value).ToDictionary(g => g.Key, g => g.Count());
        return activos.ToDictionary(a => a.Id, a => new CriterioActivo(
            enlaces.Where(e => e.IdActivo == a.Id).Select(e => codigoDe.GetValueOrDefault(e.IdIdentidad)).OfType<string>().ToHashSet(StringComparer.Ordinal),
            a.Ajeno ? null : a.Centro, a.Centro is int c && porCentro.GetValueOrDefault(c) > 1));
    }

    /// <summary>Saldo acreedor de la amortización acumulada del activo en el ejercicio (sus subcuentas del rol), sin el cierre.</summary>
    private static decimal Amortizado(IEnumerable<ApunteResultado> apuntes, IReadOnlyList<EnlaceSubcuenta> enlaces, IReadOnlyDictionary<int, string> codigoDe, int idActivo)
    {
        var cuentas = enlaces.Where(e => e.IdActivo == idActivo && e.Rol == RolesSubcuentaActivo.AmortizacionAcumulada)
            .Select(e => codigoDe.GetValueOrDefault(e.IdIdentidad)).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return cuentas.Count == 0 ? 0m : apuntes.Where(a => a.TipoAsiento != TipoCierre && cuentas.Contains(a.Cuenta)).Sum(a => a.Haber - a.Debe);
    }

    // ---------- listado ----------

    /// <summary>Todos los activos de la sociedad (también ajenos y de baja) con las cifras del ejercicio de trabajo.</summary>
    public async Task<IReadOnlyList<ActivoListado>> ListarAsync(ContextoSociedadEjercicio ctx)
    {
        var (activos, planes, enlaces, nombres, codigoDe, apuntes, centros) = await _bd.EjecutarAsync(async s =>
        {
            await CategoriasAsync(s);
            var a = await s.ConsultarAsync(SelectActivos + "WHERE a.IdSociedad = ? ORDER BY a.Codigo", LeerActivo, ctx.IdSociedad);
            var p = await PlanesAsync(s, ctx.IdSociedad);
            var e = await EnlacesAsync(s, ctx.IdSociedad);
            var (n, c) = await CuentasEjercicioAsync(s, ctx.IdSociedadEjercicio);
            var ap = await ApuntesEjercicioAsync(s, ctx.IdSociedadEjercicio);
            var ce = await s.ConsultarAsync("SELECT IdCentroCoste, Codigo, Nombre FROM CentrosCoste WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdCentroCoste"), Texto: $"{(f.Texto("Codigo") ?? "").Trim()} {(f.Texto("Nombre") ?? "").Trim()}".Trim()), ctx.IdSociedad);
            return (a, p, e, n, c, ap, ce.ToDictionary(x => x.Id, x => x.Texto));
        });
        var previstas = await _amortizacion.PrevistaAnualAsync(ctx);
        var faltan = await QueFaltanPorActivoAsync(ctx, activos.Select(a => a.IdTipo).Distinct().ToList());
        var criterios = Criterios(activos, enlaces, codigoDe);
        var resultado = new List<ActivoListado>();
        foreach (var a in activos)
        {
            var plan = PlanDe(planes, a.Id);
            var cr = a.Ajeno ? null : CalculoCuentaResultados.Calcular(ctx.Ejercicio, apuntes, criterios[a.Id], c => nombres.GetValueOrDefault(c, ""),
                previstas.TryGetValue(a.Id, out var cuota) ? cuota.Importe : 0m);
            resultado.Add(new ActivoListado(a.Id, a.Codigo, a.Nombre, a.Alias, a.IdTipo, a.CodigoTipo, a.Tipo, a.Ajeno ? null : a.Centro,
                a.Ajeno || a.Centro is not int c ? "" : centros.GetValueOrDefault(c, ""), a.Compra, a.Coste,
                plan is null || a.Ajeno ? 0m : Math.Max(0m, plan.Base - plan.Residual), a.Ajeno ? 0m : Amortizado(apuntes, enlaces, codigoDe, a.Id),
                cr?.Ingresos ?? 0m, cr?.Gastos ?? 0m, a.Ajeno, a.Baja, a.Ajeno || a.Baja is not null ? 0 : faltan.GetValueOrDefault(a.Id)));
        }
        return resultado;
    }

    /// <summary>Por activo, cuántas líneas de su plantilla no tienen subcuenta en el ejercicio de trabajo (crear o dar de alta en el ejercicio).</summary>
    private async Task<Dictionary<int, int>> QueFaltanPorActivoAsync(ContextoSociedadEjercicio ctx, IReadOnlyCollection<int> tipos) =>
        (await Generador.ProponerAsync(ctx, tipos)).Where(o => o.Accion != AccionSubcuenta.Renombrar)
            .GroupBy(o => o.Activo.IdActivo).ToDictionary(g => g.Key, g => g.Count());

    // ---------- ficha ----------

    public async Task<FichaActivo?> FichaAsync(ContextoSociedadEjercicio ctx, int idActivo)
    {
        var leido = await _bd.EjecutarAsync(async s =>
        {
            await CategoriasAsync(s);
            var a = await s.ConsultarPrimeroAsync(SelectActivos + "WHERE a.IdActivo = ? AND a.IdSociedad = ?", LeerActivo, idActivo, ctx.IdSociedad);
            if (a is null) return null;
            var inm = await s.ConsultarPrimeroAsync(
                "SELECT ReferenciaCatastral, ValorCatastralSuelo, ValorCatastralConstruccion, PorcentajeConstruccion, PrecioAlquiler, FechaInicioAlquiler, TributoMetropolitano " +
                "FROM ActivosInmuebles WHERE IdActivo = ?",
                f => new DatosInmueble(Vacio(f.Texto("ReferenciaCatastral")), f.DecimalNulo("ValorCatastralSuelo"), f.DecimalNulo("ValorCatastralConstruccion"),
                    f.DecimalNulo("PorcentajeConstruccion"), f.DecimalNulo("PrecioAlquiler"), f.FechaNula("FechaInicioAlquiler"), f.DecimalNulo("TributoMetropolitano")), idActivo);
            var plan = PlanDe(await PlanesAsync(s, ctx.IdSociedad), idActivo);
            var enlaces = await EnlacesAsync(s, ctx.IdSociedad, idActivo);
            var (_, codigoDe) = await CuentasEjercicioAsync(s, ctx.IdSociedadEjercicio);
            var apuntes = await ApuntesEjercicioAsync(s, ctx.IdSociedadEjercicio);
            var subcuentas = await SubcuentasConApuntesAsync(s, ctx, enlaces);
            string? centro = a.Centro is int c
                ? await s.ConsultarPrimeroAsync("SELECT Codigo, Nombre FROM CentrosCoste WHERE IdCentroCoste = ?",
                    f => $"{(f.Texto("Codigo") ?? "").Trim()} {(f.Texto("Nombre") ?? "").Trim()}".Trim(), c)
                : null;
            return new { a, inm, plan, enlaces, codigoDe, amortizado = Amortizado(apuntes, enlaces, codigoDe, idActivo), subcuentas, centro };
        });
        if (leido is null) return null;
        var a = leido.a;
        string? actividad = null;
        if (a.Centro is int idCentro && (await new LectorActividadesIva(_bd).PorCentroAsync(ctx.IdSociedad)).TryGetValue(idCentro, out var act))
            actividad = $"{act.Codigo} {act.Nombre}".Trim();
        var p = leido.plan;
        var datos = new DatosActivo
        {
            IdActivo = a.Id, Codigo = a.Codigo, Nombre = a.Nombre, NombreCorto = a.Alias, IdTipoActivo = a.IdTipo, CodigoTipo = a.CodigoTipo, EsAjeno = a.Ajeno, Notas = a.Notas,
            FechaCompra = a.Compra, Coste = a.Coste, IdCentroCoste = a.Centro,
            Inmueble = TiposActivoPgc.EsInmueble(a.CodigoTipo) ? leido.inm ?? new DatosInmueble() : leido.inm,
            SeAmortiza = a.Amortizable && !a.Ajeno && (p?.Activo ?? true), InicioAmortizacion = p?.Inicio ?? a.Compra, BaseAmortizable = p?.Base ?? 0m,
            ValorResidual = p?.Residual ?? 0m, PorcentajeAnual = p?.Pct ?? 0m,
        };
        return new FichaActivo(datos, a.Version, a.Tipo, leido.centro, actividad, a.Baja, ReglasFichaActivo.MotivoBloqueoTipo(ctx.Ejercicio, leido.subcuentas),
            p is null || a.Ajeno ? 0m : Math.Max(0m, p.Base - p.Residual), a.Ajeno ? 0m : leido.amortizado, leido.subcuentas);
    }

    /// <summary>Las subcuentas del activo con sus apuntes: los recientes (ejercicio de trabajo y cuatro anteriores) y los de cualquier ejercicio.</summary>
    private static async Task<IReadOnlyList<SubcuentaDelActivo>> SubcuentasConApuntesAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, IReadOnlyList<EnlaceSubcuenta> enlaces)
    {
        var r = new List<SubcuentaDelActivo>();
        var desde = ctx.Ejercicio - ReglasFichaActivo.EjerciciosBloqueo + 1;
        foreach (var e in enlaces)
        {
            var cuentas = await s.ConsultarAsync(
                "SELECT c.IdSociedadEjercicio, c.Codigo, c.Nombre, c.SaldoInicial, se.Ejercicio FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se " +
                "ON c.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE c.IdIdentidadContable = ? ORDER BY se.Ejercicio DESC",
                f => (IdEj: f.Entero("IdSociedadEjercicio"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim(), Inicial: f.DecimalNulo("SaldoInicial") ?? 0m,
                    Ejercicio: f.Entero("Ejercicio")), e.IdIdentidad);
            int recientes = 0, totales = 0;
            var anos = new List<int>();
            foreach (var c in cuentas)
            {
                var n = Convert.ToInt32(await s.EscalarAsync(
                    "SELECT COUNT(*) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND p.CuentaContable = ?",
                    c.IdEj, c.Codigo), CultureInfo.InvariantCulture);
                totales += n;
                if (c.Ejercicio >= desde && c.Ejercicio <= ctx.Ejercicio && n > 0) { recientes += n; anos.Add(c.Ejercicio); }
            }
            var actual = cuentas.FirstOrDefault(c => c.IdEj == ctx.IdSociedadEjercicio);
            var ultima = actual.Codigo is null ? cuentas.FirstOrDefault() : actual;
            r.Add(new SubcuentaDelActivo(e.IdSubcuentaActivo, e.IdIdentidad, ultima.Codigo, ultima.Nombre, recientes, totales, cuentas.Any(c => c.Inicial != 0m), anos));
        }
        return r.OrderBy(x => x.Codigo, StringComparer.Ordinal).ToList();
    }

    // ---------- guardar ----------

    /// <summary>
    /// Lo que pasará con las subcuentas si se guarda con otro tipo o con otra marca de ajeno (para enseñarlo antes de guardar); null si no cambian. Falla si
    /// la regla de bloqueo no lo permite.
    /// </summary>
    public async Task<Resultado<CambioTipoActivo?>> ProponerCambioTipoAsync(ContextoSociedadEjercicio ctx, DatosActivo nuevos)
    {
        var ficha = await FichaAsync(ctx, nuevos.IdActivo);
        if (ficha is null) return Resultado<CambioTipoActivo?>.Fallo(NoExiste(nuevos.IdActivo));
        var cambio = ReglasFichaActivo.PlanificarCambio(ficha.Datos, nuevos, ficha.Subcuentas);
        if (cambio is not null && ficha.MotivoBloqueoTipo is { } motivo)
            return Resultado<CambioTipoActivo?>.Fallo(new Error("Activos.TipoBloqueado", "No se puede cambiar el tipo ni la marca de ajeno de este activo.",
                $"Activo {ficha.Datos.Codigo}", motivo, "Deja el tipo y la marca como estaban y guarda el resto de cambios."));
        return Resultado<CambioTipoActivo?>.Correcto(cambio);
    }

    /// <summary>
    /// Guarda el apartado Datos (ficha, inmueble y plan) en una transacción con control de versión. Si cambian el tipo o la marca de ajeno, después
    /// borra las subcuentas sin apuntes, desvincula las que tienen apuntes antiguos y genera las de la plantilla del tipo nuevo; devuelve los avisos de lo
    /// que no se haya podido hacer.
    /// </summary>
    public async Task<Resultado<IReadOnlyList<string>>> GuardarAsync(ContextoSociedadEjercicio ctx, DatosActivo d, int version)
    {
        var registro = $"Activo {d.Codigo}";
        Resultado<IReadOnlyList<string>> Fallo(Error e) => Resultado<IReadOnlyList<string>>.Fallo(e);
        if (!await PuedeAsync()) return Fallo(SinPermiso(registro));
        var repetido = await CodigoRepetidoAsync(ctx.IdSociedad, d.Codigo, d.IdActivo);
        if (ReglasFichaActivo.Validar(d, repetido) is { Count: > 0 } errores)
            return Resultado<IReadOnlyList<string>>.Fallo(errores.Select(e => new Error("Activos.DatoNoValido", e.Texto, registro, null, null, e.Campo)));
        var cambio = await ProponerCambioTipoAsync(ctx, d);
        if (!cambio.EsCorrecto) return Resultado<IReadOnlyList<string>>.Fallo(cambio.Errores);
        var limpio = Normalizar(d);
        if (cambio.Valor is { QuitarCentroYPlan: true }) limpio = limpio with { IdCentroCoste = null, SeAmortiza = false };
        try
        {
            var r = await _bd.EnTransaccionAsync(async s =>
            {
                await s.ActualizarConVersionAsync(
                    "UPDATE Activos SET IdTipoActivo = ?, Codigo = ?, Nombre = ?, AliasActivo = ?, Descripcion = ?, FechaAdquisicion = ?, EsAmortizable = ?, " +
                    "CosteAdquisicionActual = ?, ValorContable = ? - AmortizacionAcumulada, IdCentroCoste = ?, EsAjeno = ?, Version = Version + 1 " +
                    "WHERE IdActivo = ? AND IdSociedad = ? AND Version = ?",
                    registro, limpio.IdTipoActivo, limpio.Codigo, limpio.Nombre, limpio.NombreCorto, limpio.Notas, limpio.FechaCompra, limpio.SeAmortiza && !limpio.EsAjeno,
                    limpio.Coste, limpio.Coste, limpio.IdCentroCoste, limpio.EsAjeno, limpio.IdActivo, ctx.IdSociedad, version);
                await GuardarInmuebleAsync(s, limpio);
                await GuardarPlanAsync(s, limpio);
                return Resultado.Correcto();
            });
            if (!r.EsCorrecto) return Resultado<IReadOnlyList<string>>.Fallo(r.Errores);
        }
        catch (ConflictoConcurrenciaException)
        {
            return Fallo(new Error("Activos.Cambiado", "Alguien ha cambiado este activo mientras lo editabas y no he guardado tus cambios.", registro,
                "La ficha que tienes abierta ya no es la última versión.", "Copia lo que hayas escrito, pulsa «Cancelar» para ver la versión actual y vuelve a hacer tus cambios."));
        }
        catch (UnicidadException)
        {
            return Fallo(new Error("Activos.CodigoRepetido", $"Ya hay otro activo con el código «{limpio.Codigo}» en esta sociedad.", registro, null, "Elige otro código."));
        }
        var avisos = cambio.Valor is { } c ? await AplicarCambioTipoAsync(ctx, limpio, c) : [];
        return Resultado<IReadOnlyList<string>>.Correcto(avisos);
    }

    private async Task<IReadOnlyList<string>> AplicarCambioTipoAsync(ContextoSociedadEjercicio ctx, DatosActivo d, CambioTipoActivo c)
    {
        var avisos = new List<string>();
        foreach (var sub in c.Borrar)
        {
            var r = await _cuentas.EliminarSinApuntesAsync(ctx, sub.IdIdentidadContable,
                s => s.EjecutarAsync("DELETE FROM SubcuentasActivo WHERE IdSubcuentaActivo = ?", sub.IdSubcuentaActivo));
            if (!r.EsCorrecto) avisos.Add($"{sub.Codigo} {sub.Nombre}: {r.Error!.TextoOperador()}");
        }
        foreach (var sub in c.Desvincular)
            await _bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM SubcuentasActivo WHERE IdSubcuentaActivo = ?", sub.IdSubcuentaActivo));
        if (c.GenerarNuevas)
        {
            var g = await Generador.GenerarAsync(ctx, [d.IdTipoActivo], [d.IdActivo]);
            avisos.AddRange(g.Errores);
        }
        return avisos;
    }

    private static async Task GuardarInmuebleAsync(ISesionDatos s, DatosActivo d)
    {
        if (!d.EsInmueble || d.Inmueble is not { } i) return;
        var total = (i.ValorSuelo ?? 0m) + (i.ValorConstruccion ?? 0m);
        object?[] valores = [i.ReferenciaCatastral, i.ValorSuelo, i.ValorConstruccion, i.ValorSuelo is null && i.ValorConstruccion is null ? null : total,
            i.PorcentajeConstruccion, d.EsAjeno ? null : d.Coste, i.AlquilerMensual, i.InicioAlquiler, i.TributoMetropolitano, d.IdActivo];
        var n = await s.EjecutarAsync(
            "UPDATE ActivosInmuebles SET ReferenciaCatastral = ?, ValorCatastralSuelo = ?, ValorCatastralConstruccion = ?, ValorCatastralTotal = ?, PorcentajeConstruccion = ?, " +
            "CosteCompra = ?, PrecioAlquiler = ?, FechaInicioAlquiler = ?, TributoMetropolitano = ? WHERE IdActivo = ?", valores);
        if (n == 0)
            await s.EjecutarAsync(
                "INSERT INTO ActivosInmuebles (ReferenciaCatastral, ValorCatastralSuelo, ValorCatastralConstruccion, ValorCatastralTotal, PorcentajeConstruccion, CosteCompra, " +
                "PrecioAlquiler, FechaInicioAlquiler, TributoMetropolitano, IdActivo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", valores);
    }

    /// <summary>El plan que manda se actualiza (o se crea); sin amortización (o ajeno), se desactiva.</summary>
    private static async Task GuardarPlanAsync(ISesionDatos s, DatosActivo d)
    {
        var plan = await s.ConsultarPrimeroAsync(
            "SELECT IdPlanAmortizacion FROM PlanesAmortizacionActivo WHERE IdActivo = ? ORDER BY EstaActivo, FechaInicio DESC, IdPlanAmortizacion DESC",
            f => (int?)f.Entero("IdPlanAmortizacion"), d.IdActivo);
        // ORDER BY EstaActivo en Access: False (0) va después de True (−1), así que el primero es el activo más reciente.
        if (!d.SeAmortiza || d.EsAjeno)
        {
            if (plan is int id) await s.EjecutarAsync("UPDATE PlanesAmortizacionActivo SET EstaActivo = False WHERE IdPlanAmortizacion = ?", id);
            return;
        }
        var inicio = d.InicioAmortizacion ?? d.FechaCompra ?? DateTime.Today;
        if (plan is int idPlan)
            await s.EjecutarAsync(
                "UPDATE PlanesAmortizacionActivo SET FechaInicio = ?, BaseAmortizable = ?, ValorResidual = ?, VidaUtilMeses = ?, PorcentajeAnual = ?, EstaActivo = True WHERE IdPlanAmortizacion = ?",
                inicio, d.BaseAmortizable, d.ValorResidual, ReglasFichaActivo.VidaUtilMeses(d.PorcentajeAnual), d.PorcentajeAnual, idPlan);
        else
            await s.EjecutarAsync(
                "INSERT INTO PlanesAmortizacionActivo (IdActivo, FechaInicio, BaseAmortizable, ValorResidual, VidaUtilMeses, Metodo, EstaActivo, PorcentajeAnual) VALUES (?, ?, ?, ?, ?, 'LINEAL', True, ?)",
                d.IdActivo, inicio, d.BaseAmortizable, d.ValorResidual, ReglasFichaActivo.VidaUtilMeses(d.PorcentajeAnual), d.PorcentajeAnual);
    }

    private static DatosActivo Normalizar(DatosActivo d) => d with
    {
        Codigo = d.Codigo.Trim(), Nombre = d.Nombre.Trim(), NombreCorto = Vacio(d.NombreCorto), Notas = Vacio(d.Notas),
        Coste = CalculoAmortizacion.Redondear(d.Coste), BaseAmortizable = CalculoAmortizacion.Redondear(d.BaseAmortizable), ValorResidual = CalculoAmortizacion.Redondear(d.ValorResidual),
        PorcentajeAnual = Math.Round(d.PorcentajeAnual, 6, MidpointRounding.AwayFromZero),
        IdCentroCoste = d.EsAjeno ? null : d.IdCentroCoste,
        Inmueble = d.Inmueble is { } i ? i with
        {
            ReferenciaCatastral = Vacio(i.ReferenciaCatastral),
            PorcentajeConstruccion = i.PorcentajeConstruccion is decimal p ? Math.Round(p, 4, MidpointRounding.AwayFromZero) : null,
        } : null,
    };

    public Task<bool> CodigoRepetidoAsync(string idSociedad, string codigo, int idActivo) => _bd.EjecutarAsync(async s =>
        Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Activos WHERE IdSociedad = ? AND Codigo = ? AND IdActivo <> ?", idSociedad, codigo.Trim(), idActivo),
            CultureInfo.InvariantCulture) > 0);

    // ---------- alta ----------

    /// <summary>
    /// Alta paso a paso: en una transacción, el centro nuevo (si se pidió: bajo el padre elegido, con el código del activo y su nombre corto), la ficha,
    /// la del inmueble y el plan. No graba asientos; las subcuentas se generan después, con su propuesta. Un activo ajeno no crea centro ni plan.
    /// </summary>
    public async Task<Resultado<int>> AltaAsync(ContextoSociedadEjercicio ctx, AltaActivo alta)
    {
        var d = Normalizar(alta.Datos with { IdActivo = 0 });
        var registro = $"Activo {d.Codigo}";
        if (!await PuedeAsync()) return Resultado<int>.Fallo(SinPermiso(registro));
        var nuevoCentro = !d.EsAjeno && alta.IdCentroPadreNuevo is not null;
        var paraValidar = nuevoCentro ? d with { IdCentroCoste = -1 } : d;
        var repetido = await CodigoRepetidoAsync(ctx.IdSociedad, d.Codigo, 0);
        if (ReglasFichaActivo.Validar(paraValidar, repetido) is { Count: > 0 } errores)
            return Resultado<int>.Fallo(errores.Select(e => new Error("Activos.DatoNoValido", e.Texto, registro, null, null, e.Campo)));
        if (await ValidarAltaInversionAsync(ctx, alta, d, registro) is { } errorInversion) return Resultado<int>.Fallo(errorInversion);
        try
        {
            return await _bd.EnTransaccionAsync(async s =>
            {
                var centro = d.IdCentroCoste;
                if (nuevoCentro)
                {
                    var nombreCentro = d.NombreCorto ?? d.Nombre;
                    var c = await CatalogoCentrosCosteAccess.AltaAsync(s, ctx.IdSociedad, ReglasFichaActivo.CodigoCentro(d.Codigo),
                        nombreCentro.Length <= 150 ? nombreCentro : nombreCentro[..150], alta.IdCentroPadreNuevo);
                    if (!c.EsCorrecto) return Resultado<int>.Fallo(c.Errores);
                    centro = c.Valor.IdCentroCoste;
                }
                if (d.EsAjeno) centro = null;
                await s.EjecutarAsync(
                    "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, AliasActivo, Descripcion, FechaAdquisicion, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, " +
                    "AmortizacionAcumulada, DeterioroAcumulado, ValorContable, EstadoCalculo, IdCentroCoste, EsAjeno, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    ctx.IdSociedad, d.IdTipoActivo, d.Codigo, d.Nombre, d.NombreCorto, d.Notas, d.FechaCompra, DateTime.Today, (byte)1, d.SeAmortiza && !d.EsAjeno, d.Coste,
                    0m, 0m, d.Coste, (byte)0, centro, d.EsAjeno, 0);
                var id = await s.UltimoIdAsync();
                var guardado = d with { IdActivo = id, IdCentroCoste = centro };
                await GuardarInmuebleAsync(s, guardado);
                if (guardado.SeAmortiza && !guardado.EsAjeno) await GuardarPlanAsync(s, guardado);
                if (alta.Instrumento is { } instrumento && alta.Compra is { } compra) await AltaInversionAsync(s, id, instrumento, compra);
                return Resultado<int>.Correcto(id);
            });
        }
        catch (UnicidadException)
        {
            return Resultado<int>.Fallo(new Error("Activos.CodigoRepetido", $"Ya hay otro activo con el código «{d.Codigo}» en esta sociedad.", registro, null, "Elige otro código."));
        }
    }

    // ---------- subcuentas ----------

    /// <summary>Las subcuentas del activo con su papel y su saldo en el ejercicio de trabajo, y lo que propone «Generar las que falten…».</summary>
    public async Task<SubcuentasDeActivo> SubcuentasAsync(ContextoSociedadEjercicio ctx, int idActivo)
    {
        var (subcuentas, idTipo, ajeno) = await _bd.EjecutarAsync(async s =>
        {
            var tipo = await s.ConsultarPrimeroAsync("SELECT IdTipoActivo, EsAjeno FROM Activos WHERE IdActivo = ?", f => (Tipo: f.Entero("IdTipoActivo"), Ajeno: f.Logico("EsAjeno")), idActivo);
            var enlaces = await EnlacesAsync(s, ctx.IdSociedad, idActivo);
            var lista = new List<SubcuentaFicha>();
            foreach (var e in enlaces)
            {
                var cuenta = await s.ConsultarPrimeroAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ? ORDER BY Codigo",
                    f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim()), ctx.IdSociedadEjercicio, e.IdIdentidad);
                var papel = RolesSubcuentaActivo.Textos.GetValueOrDefault(e.Rol, RolesSubcuentaActivo.Textos[RolesSubcuentaActivo.Libre]);
                if (cuenta.Codigo is null)
                {
                    var ultima = await s.ConsultarPrimeroAsync(
                        "SELECT c.Codigo, c.Nombre FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                        "WHERE c.IdIdentidadContable = ? ORDER BY se.Ejercicio DESC", f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim()), e.IdIdentidad);
                    lista.Add(new SubcuentaFicha(e.IdSubcuentaActivo, ultima.Codigo, ultima.Nombre ?? "", papel, 0m, false));
                    continue;
                }
                var saldo = Convert.ToDecimal(await s.EscalarAsync(
                    // Debe o Haber pueden estar vacíos (nulos): se suman por separado para que un nulo no anule la fila.
                    "SELECT SUM(IIF(p.ImporteDebe IS NULL, 0, p.ImporteDebe)) - SUM(IIF(p.ImporteHaber IS NULL, 0, p.ImporteHaber)) FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                    "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND a.TipoAsiento <> 2 AND a.TipoAsiento <> 3 AND p.CuentaContable = ?",
                    ctx.IdSociedadEjercicio, cuenta.Codigo) is { } v and not DBNull ? v : 0m, CultureInfo.InvariantCulture);
                lista.Add(new SubcuentaFicha(e.IdSubcuentaActivo, cuenta.Codigo, cuenta.Nombre, papel, saldo, true));
            }
            return (lista.OrderBy(x => x.Codigo, StringComparer.Ordinal).ToList(), tipo.Tipo, tipo.Ajeno);
        });
        var propuesta = ajeno || idTipo == 0 ? [] : await Generador.ProponerAsync(ctx, [idTipo], [idActivo]);
        return new SubcuentasDeActivo(subcuentas, propuesta);
    }

    /// <summary>Propuesta de «Generar las que falten…» para unos activos (los ajenos no reciben nada).</summary>
    public async Task<IReadOnlyList<OperacionSubcuenta>> ProponerSubcuentasAsync(ContextoSociedadEjercicio ctx, IReadOnlyCollection<int> idsActivo)
    {
        var tipos = await TiposDeAsync(idsActivo);
        return tipos.Count == 0 ? [] : await Generador.ProponerAsync(ctx, tipos, idsActivo);
    }

    /// <summary>Aplica «Generar las que falten…» a esos activos (vuelve a calcular la propuesta).</summary>
    public async Task<Resultado<ResultadoGeneracionSubcuentas>> GenerarSubcuentasAsync(ContextoSociedadEjercicio ctx, IReadOnlyCollection<int> idsActivo)
    {
        if (!await PuedeAsync()) return Resultado<ResultadoGeneracionSubcuentas>.Fallo(SinPermiso("Subcuentas de activos"));
        var tipos = await TiposDeAsync(idsActivo);
        return Resultado<ResultadoGeneracionSubcuentas>.Correcto(tipos.Count == 0 ? new ResultadoGeneracionSubcuentas(0, 0, 0, []) : await Generador.GenerarAsync(ctx, tipos, idsActivo));
    }

    private Task<IReadOnlyList<int>> TiposDeAsync(IReadOnlyCollection<int> idsActivo) => _bd.EjecutarAsync<IReadOnlyList<int>>(async s =>
    {
        if (idsActivo.Count == 0) return [];
        var marcas = string.Join(", ", idsActivo.Select(_ => "?"));
        return (await s.ConsultarAsync($"SELECT DISTINCT IdTipoActivo FROM Activos WHERE IdActivo IN ({marcas})", f => f.Entero("IdTipoActivo"), [.. idsActivo.Cast<object?>()])).ToList();
    });

    /// <summary>Códigos en el ejercicio de trabajo de las subcuentas de esos activos (para abrir su Mayor).</summary>
    public Task<IReadOnlyList<string>> CuentasDeActivosAsync(ContextoSociedadEjercicio ctx, IReadOnlyCollection<int> idsActivo) => _bd.EjecutarAsync<IReadOnlyList<string>>(async s =>
    {
        if (idsActivo.Count == 0) return [];
        var marcas = string.Join(", ", idsActivo.Select(_ => "?"));
        return (await s.ConsultarAsync(
                $"SELECT DISTINCT c.Codigo FROM SubcuentasActivo AS sa INNER JOIN CuentasContables AS c ON sa.IdIdentidadContable = c.IdIdentidadContable " +
                $"WHERE c.IdSociedadEjercicio = ? AND sa.IdActivo IN ({marcas})", f => (f.Texto("Codigo") ?? "").Trim(), [ctx.IdSociedadEjercicio, .. idsActivo.Cast<object?>()]))
            .Where(c => c.Length > 0).Order(StringComparer.Ordinal).ToList();
    });

    // ---------- cuenta de resultados ----------

    /// <summary>
    /// Cuenta de resultados del activo en <paramref name="ejercicio"/> (cualquiera de la sociedad), con la del anterior si se pide y su parte de los gastos
    /// generales si se pide. Null si la sociedad no tiene ese ejercicio.
    /// </summary>
    public async Task<ResultadosDeActivo?> CuentaResultadosAsync(string idSociedad, int idActivo, int ejercicio, bool conAnterior, bool conGenerales)
    {
        var ejercicios = await EjerciciosAsync(idSociedad);
        var ej = ejercicios.FirstOrDefault(e => e.Ejercicio == ejercicio);
        if (ej is null) return null;
        var (actual, generales) = await CuentaDeEjercicioAsync(idSociedad, idActivo, ej, conGenerales);
        CuentaResultados? anterior = null;
        if (conAnterior && ejercicios.FirstOrDefault(e => e.Ejercicio == ejercicio - 1) is { } previo)
            anterior = (await CuentaDeEjercicioAsync(idSociedad, idActivo, previo, conGenerales)).Cuenta;
        return new ResultadosDeActivo(actual, anterior, generales);
    }

    private async Task<(CuentaResultados Cuenta, GastosGeneralesAnual? Generales)> CuentaDeEjercicioAsync(string idSociedad, int idActivo, EjercicioSociedad ej, bool conGenerales)
    {
        var ctx = new ContextoSociedadEjercicio(idSociedad, ej.IdSociedadEjercicio, ej.Ejercicio);
        var (activos, enlaces, nombres, codigoDe, apuntes) = await _bd.EjecutarAsync(async s =>
        {
            var a = await s.ConsultarAsync(SelectActivos + "WHERE a.IdSociedad = ?", LeerActivo, idSociedad);
            var e = await EnlacesAsync(s, idSociedad);
            var (n, c) = await CuentasEjercicioAsync(s, ej.IdSociedadEjercicio);
            var ap = await ApuntesEjercicioAsync(s, ej.IdSociedadEjercicio);
            return (a, e, n, c, ap);
        });
        var criterio = Criterios(activos, enlaces, codigoDe)[idActivo];
        var previstas = await _amortizacion.PrevistaAnualAsync(ctx);
        var prevista = previstas.TryGetValue(idActivo, out var c) ? c.Importe : 0m;
        string Nombre(string cuenta) => nombres.GetValueOrDefault(cuenta, "");
        if (!conGenerales) return (CalculoCuentaResultados.Calcular(ej.Ejercicio, apuntes, criterio, Nombre, prevista), null);
        var g = await _impuesto.GastosGeneralesAsync(ctx);
        var cuenta = CalculoCuentaResultados.Calcular(ej.Ejercicio, apuntes, criterio, Nombre, prevista, g);
        var ordinarios = cuenta.IngresosOrdinarios;
        return (cuenta, new GastosGeneralesAnual(g.GastosGenerales, g.IngresosOrdinarios, ordinarios, CalculoCuentaResultados.ParteGastosGenerales(g, ordinarios)));
    }

    // ---------- amortización ----------

    /// <summary>El plan del activo y su tabla por ejercicio: lo contabilizado de verdad en los ejercicios pasados y la proyección desde el de trabajo.</summary>
    public async Task<AmortizacionDeActivo> AmortizacionAsync(ContextoSociedadEjercicio ctx, int idActivo)
    {
        var (activo, plan, contabilizada) = await _bd.EjecutarAsync(async s =>
        {
            var a = await s.ConsultarPrimeroAsync(SelectActivos + "WHERE a.IdActivo = ?", LeerActivo, idActivo);
            var p = PlanDe(await PlanesAsync(s, ctx.IdSociedad), idActivo);
            var enlaces = (await EnlacesAsync(s, ctx.IdSociedad, idActivo))
                .Where(e => e.Rol is RolesSubcuentaActivo.GastoAmortizacion or RolesSubcuentaActivo.AmortizacionAcumulada).ToList();
            var r = new Dictionary<int, AmortizacionContabilizada>();
            if (enlaces.Count > 0)
            {
                var marcas = string.Join(", ", enlaces.Select(_ => "?"));
                // Sus cuentas en cada ejercicio: ahí lo contabilizado es la verdad (aunque sea cero). Los apuntes guardan el código, así que se leen por cuenta.
                var cuentasEj = await s.ConsultarAsync(
                    $"SELECT c.IdIdentidadContable, c.IdSociedadEjercicio, c.Codigo, se.Ejercicio FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se " +
                    $"ON c.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE c.IdIdentidadContable IN ({marcas})",
                    f => (Identidad: f.Entero("IdIdentidadContable"), IdEj: f.Entero("IdSociedadEjercicio"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Ejercicio: f.Entero("Ejercicio")),
                    [.. enlaces.Select(e => (object?)e.IdIdentidad)]);
                var existentes = cuentasEj.Select(c => c.Ejercicio).Distinct().ToList();
                var movimientos = new List<(int Ejercicio, int Identidad, int Tipo, decimal Debe, decimal Haber)>();
                foreach (var c in cuentasEj)
                    movimientos.AddRange(await s.ConsultarAsync(
                        "SELECT a.TipoAsiento, p.ImporteDebe, p.ImporteHaber FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
                        "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND p.CuentaContable = ?",
                        f => (c.Ejercicio, c.Identidad, Convert.ToInt32(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m),
                        c.IdEj, c.Codigo));
                var gasto = enlaces.Where(e => e.Rol == RolesSubcuentaActivo.GastoAmortizacion).Select(e => e.IdIdentidad).ToHashSet();
                var acumulada = enlaces.Where(e => e.Rol == RolesSubcuentaActivo.AmortizacionAcumulada).Select(e => e.IdIdentidad).ToHashSet();
                foreach (var y in existentes.Distinct())
                {
                    var delAno = movimientos.Where(m => m.Ejercicio == y).ToList();
                    r[y] = new AmortizacionContabilizada(y,
                        delAno.Where(m => gasto.Contains(m.Identidad) && m.Tipo is not (TipoRegularizacion or TipoCierre)).Sum(m => m.Debe - m.Haber),
                        delAno.Where(m => acumulada.Contains(m.Identidad) && m.Tipo != TipoCierre).Sum(m => m.Haber - m.Debe));
                }
            }
            return (a, p, r);
        });
        if (activo is null || activo.Ajeno || plan is null || plan.Pct is not decimal pct)
            return new AmortizacionDeActivo(null, activo is { Ajeno: false, Amortizable: true }, []);
        var p = new PlanAmortizacion(plan.Inicio, plan.Base, plan.Residual, pct, activo.Baja);
        return new AmortizacionDeActivo(p, activo.Amortizable && plan.Activo, TablaAmortizacionActivo.Construir(p, ctx.Ejercicio, contabilizada));
    }

    // ---------- apertura por centro ----------

    /// <summary>
    /// Los activos no dados de baja del centro de coste nombrado como lo enseña el Impuesto sobre Sociedades («código nombre»; también vale solo el código
    /// o solo el nombre).
    /// </summary>
    public async Task<ActivosDeCentro> ActivosDeCentroAsync(ContextoSociedadEjercicio ctx, string textoCentro)
    {
        var texto = (textoCentro ?? "").Trim();
        var centros = await CentrosAsync(ctx.IdSociedad);
        var centro = centros.FirstOrDefault(c => string.Equals(c.Texto, texto, StringComparison.CurrentCultureIgnoreCase))
                     ?? centros.FirstOrDefault(c => string.Equals(c.Codigo, texto, StringComparison.CurrentCultureIgnoreCase))
                     ?? centros.FirstOrDefault(c => string.Equals(c.Nombre, texto, StringComparison.CurrentCultureIgnoreCase));
        if (centro is null) return new ActivosDeCentro(null, texto, []);
        var ids = await _bd.EjecutarAsync<IReadOnlyList<int>>(async s => await s.ConsultarAsync(
            "SELECT IdActivo FROM Activos WHERE IdSociedad = ? AND IdCentroCoste = ? AND FechaBaja IS NULL AND EsAjeno = False", f => f.Entero("IdActivo"), ctx.IdSociedad, centro.IdCentroCoste));
        if (ids.Count == 0) return new ActivosDeCentro(centro.IdCentroCoste, centro.Texto, []);
        var todos = await ListarAsync(ctx);
        return new ActivosDeCentro(centro.IdCentroCoste, centro.Texto, todos.Where(a => ids.Contains(a.IdActivo)).ToList());
    }

    private static Error NoExiste(int id) => new("Activos.NoExiste", "El activo ya no existe.", $"Activo {id}", "Lo ha borrado otra persona.", "Cierra la ficha y actualiza el listado.");

    private static string? Vacio(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
