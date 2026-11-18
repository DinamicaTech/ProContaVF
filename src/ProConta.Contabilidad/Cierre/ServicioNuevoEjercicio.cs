using ProConta.Contabilidad.Cuentas;
using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Cierre;

/// <summary>Lo que copiaría «Crear el ejercicio siguiente», para enseñarlo antes de hacerlo.</summary>
/// <param name="Prorrata">Prorrata del año anterior que tendrá el ejercicio nuevo (en porcentaje): la definitiva del actual o, si no hay, su provisional.</param>
public sealed record PrevisionNuevoEjercicio(int Ejercicio, bool Existe, int Cuentas, int Series, IReadOnlyList<string> NombresSeries, decimal? Prorrata, bool DefinitivaFijada);

/// <summary>Lo que hizo «Crear el ejercicio siguiente»: el ejercicio (nuevo o ya existente) y lo que añadió.</summary>
public sealed record ResumenNuevoEjercicio(ContextoSociedadEjercicio Ejercicio, bool Creado, int CuentasCopiadas, int SeriesCopiadas, IReadOnlyList<string> OtrasTablas,
    decimal? Prorrata)
{
    /// <summary>Cuentas del ejercicio que estaban vacías y ha asignado «Auto» (organizacion/sociedades-ejercicios 2026-11-18).</summary>
    public IReadOnlyList<string> CuentasAsignadas { get; init; } = [];
}

/// <summary>
/// Proceso «Crear el ejercicio siguiente» (contabilidad/procesos/nuevo-ejercicio, propietario 2026-11-17 11:50, P1 A): copia del ejercicio
/// actual su fila de SociedadesEjercicios (todos los parámetros y cuentas, sin la marca de cerrado, con el trimestre activo en el 1.º y,
/// como «Prorrata del año anterior», la definitiva del actual o, si no está fijada, su provisional), el plan de cuentas con las mismas
/// identidades contables, las series de facturación (el año del prefijo pasa al siguiente y la numeración empieza en 1) y la
/// configuración de correo del ejercicio. Repetirlo no duplica nada: solo añade lo que falta. El cierre lo usa también para copiar al
/// ejercicio siguiente las subcuentas que se hayan creado después.
/// </summary>
public sealed class ServicioNuevoEjercicio(IBaseDatos bd, Func<Task<bool>>? puede = null)
{
    /// <summary>Tablas por ejercicio que se copian enteras si el ejercicio nuevo aún no tiene ninguna fila (la configuración del correo).</summary>
    public static readonly IReadOnlyList<string> TablasConfiguracion = ["CuentasCorreo", "RemitentesAutorizadosCorreo", "ConfiguracionMailDispatcher"];

    private const int Tanda = 150;

    /// <summary>El ejercicio siguiente de la sociedad, si existe.</summary>
    public Task<ContextoSociedadEjercicio?> SiguienteAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(s => SiguienteAsync(s, ctx));

    private static async Task<ContextoSociedadEjercicio?> SiguienteAsync(ISesionDatos s, ContextoSociedadEjercicio ctx)
    {
        var v = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?",
            ctx.IdSociedad, (ctx.Ejercicio + 1).ToString(CultureInfo.InvariantCulture));
        return v is null or DBNull ? null : new ContextoSociedadEjercicio(ctx.IdSociedad, Convert.ToInt32(v, CultureInfo.InvariantCulture), ctx.Ejercicio + 1);
    }

    public Task<PrevisionNuevoEjercicio> PrevisionAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var siguiente = await SiguienteAsync(s, ctx);
        var cuentas = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM CuentasContables WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio), CultureInfo.InvariantCulture);
        var series = await s.ConsultarAsync("SELECT Prefijo, Descripcion FROM SeriesFacturacion WHERE IdSociedadEjercicio = ? ORDER BY EsRectificativa, Prefijo",
            f => $"{NuevoTexto((f.Texto("Prefijo") ?? "").Trim(), ctx.Ejercicio)} · {NuevoTexto((f.Texto("Descripcion") ?? "").Trim(), ctx.Ejercicio)}".Trim(' ', '·'),
            ctx.IdSociedadEjercicio);
        var (definitiva, provisional) = await ProrratasAsync(s, ctx);
        return new PrevisionNuevoEjercicio(ctx.Ejercicio + 1, siguiente is not null, cuentas, series.Count, series, definitiva ?? provisional, definitiva is not null);
    });

    /// <summary>Crea el ejercicio siguiente (o completa el que ya existe con lo que le falte).</summary>
    public async Task<Resultado<ResumenNuevoEjercicio>> CrearSiguienteAsync(ContextoSociedadEjercicio ctx)
    {
        var registro = $"Ejercicio {ctx.Ejercicio + 1}";
        if (puede is not null && !await puede())
            return Resultado<ResumenNuevoEjercicio>.Fallo(new Error("NuevoEjercicio.SinPermiso", "No tienes permiso para crear el ejercicio siguiente.", registro,
                "Crear el ejercicio va con la acción «Cerrar y reabrir el ejercicio», que tiene grupos asignados y el tuyo no está entre ellos.", "Pídeselo a quien administra los permisos."));

        var (siguiente, creado, prorrata) = await bd.EnTransaccionAsync(async s =>
        {
            if (await SiguienteAsync(s, ctx) is { } ya) return (ya, false, (decimal?)null);
            var tabla = s.LeerEsquema().Tabla("SociedadesEjercicios")!;
            var fila = await s.ConsultarPrimeroAsync("SELECT * FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
                f => tabla.Columnas.ToDictionary(c => c.Nombre, c => f.Valor(c.Nombre), StringComparer.OrdinalIgnoreCase), ctx.IdSociedadEjercicio)
                ?? throw new InvalidOperationException($"No existe el ejercicio {ctx}.");
            var (definitiva, provisional) = await ProrratasAsync(s, ctx);
            var nuevaProrrata = definitiva ?? provisional;
            var valores = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in tabla.Columnas.Where(c => !EsAutonumerico(c)))
            {
                var v = fila[c.Nombre];
                valores[c.Nombre] = c.Nombre.ToUpperInvariant() switch
                {
                    "EJERCICIO" => (ctx.Ejercicio + 1).ToString(CultureInfo.InvariantCulture),
                    "NOMBRE" or "CARPETAFACTURASCOMPRA" => v is string t ? NuevoTexto(t, ctx.Ejercicio) : v,
                    "EJERCICIOCERRADO" => false,
                    "ACTIVA" => true,
                    "TRIMESTREACTIVO" => 1,
                    "PORCENTAJEPRORRATADEFINITIVA" => null,
                    "PORCENTAJEPRORRATAAÑOANTERIOR" => nuevaProrrata is decimal p ? (double)p : null,
                    _ => v,
                };
            }
            await InsertarAsync(s, "SociedadesEjercicios", valores);
            var id = await s.UltimoIdAsync();
            return (new ContextoSociedadEjercicio(ctx.IdSociedad, id, ctx.Ejercicio + 1), true, nuevaProrrata);
        });

        var cuentas = await CopiarCuentasQueFaltanAsync(ctx, siguiente);
        var series = await CopiarSeriesAsync(ctx, siguiente);
        var otras = await CopiarConfiguracionAsync(ctx, siguiente);
        // Las cuentas del ejercicio siempre asignadas (propietario 2026-11-18 05:10): las que vinieran vacías, con la regla de «Auto».
        var asignadas = await new AsignacionCuentasEjercicio(bd, new CatalogoCuentasAccess(bd)).AsignarAsync(siguiente);
        return Resultado<ResumenNuevoEjercicio>.Correcto(new ResumenNuevoEjercicio(siguiente, creado, cuentas, series, otras, prorrata) { CuentasAsignadas = asignadas });
    }

    /// <summary>
    /// Copia al ejercicio <paramref name="destino"/> las cuentas del <paramref name="origen"/> que aún no tiene (por código), con la misma identidad
    /// contable, nombre, descripción, marcas y centro de coste predeterminado, sin saldos, y les pone la cuenta padre del destino. Devuelve cuántas.
    /// Va por tandas (las transacciones de Access tienen un límite de bloqueos): si se corta, repetirlo termina el trabajo.
    /// </summary>
    public async Task<int> CopiarCuentasQueFaltanAsync(ContextoSociedadEjercicio origen, ContextoSociedadEjercicio destino)
    {
        var deOrigen = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdCuentaContable, IdIdentidadContable, IdCuentaPadre, Codigo, Nombre, Descripcion, EstaActiva, EstaBloqueada, EsImputable, IdCentroCostePredeterminado " +
            "FROM CuentasContables WHERE IdSociedadEjercicio = ?",
            f => (Id: f.Entero("IdCuentaContable"), Identidad: f.Entero("IdIdentidadContable"), Padre: f.EnteroNulo("IdCuentaPadre"), Codigo: (f.Texto("Codigo") ?? "").Trim(),
                Nombre: f.Texto("Nombre") ?? "", Descripcion: f.Texto("Descripcion"), Activa: f.Logico("EstaActiva"), Bloqueada: f.Logico("EstaBloqueada"),
                Imputable: f.Logico("EsImputable"), Centro: f.EnteroNulo("IdCentroCostePredeterminado")),
            origen.IdSociedadEjercicio));
        var enDestino = await CodigosAsync(destino);
        var faltan = deOrigen.Where(c => c.Codigo.Length > 0 && !enDestino.ContainsKey(c.Codigo)).OrderBy(c => c.Codigo.Length).ThenBy(c => c.Codigo, StringComparer.Ordinal).ToList();
        if (faltan.Count == 0) return 0;
        foreach (var tanda in faltan.Chunk(Tanda))
            await bd.EnTransaccionAsync(async s =>
            {
                foreach (var c in tanda)
                    await s.EjecutarAsync(
                        "INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, IdCuentaPadre, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, " +
                        "IdCentroCostePredeterminado, SaldoInicial, SaldoActual, NumeroApuntes, Descripcion) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        c.Identidad, destino.IdSociedadEjercicio, null, c.Codigo, c.Nombre, c.Activa, c.Bloqueada, c.Imputable, c.Centro, 0m, 0m, 0, c.Descripcion);
                return true;
            });
        // La cuenta padre de cada copia es la cuenta del destino con el código del padre en el origen.
        var codigoDe = deOrigen.ToDictionary(c => c.Id, c => c.Codigo);
        var nuevos = await CodigosAsync(destino);
        var padres = faltan.Where(c => c.Padre is int p && codigoDe.ContainsKey(p) && nuevos.ContainsKey(codigoDe[p]))
            .Select(c => (Id: nuevos[c.Codigo], Padre: nuevos[codigoDe[c.Padre!.Value]])).ToList();
        foreach (var tanda in padres.Chunk(Tanda))
            await bd.EnTransaccionAsync(async s =>
            {
                foreach (var (id, padre) in tanda)
                    await s.EjecutarAsync("UPDATE CuentasContables SET IdCuentaPadre = ? WHERE IdCuentaContable = ?", padre, id);
                return true;
            });
        return faltan.Count;
    }

    /// <summary>Primer número de una serie sin el año en el prefijo: el año y cuatro cifras (2027 → 20270001; propietario 2026-11-17 15:38, punto 5).</summary>
    public static int PrimerNumero(int ejercicio) => ejercicio * 10000 + 1;

    /// <summary>
    /// Rango de la serie en el ejercicio <paramref name="ejercicio"/>: con el año en el prefijo (F2026 → F2027), de 1 al «hasta» de la serie;
    /// sin él, del año seguido de 0001 al año seguido de 9999, para que la numeración no se repita entre ejercicios.
    /// </summary>
    public static (int Desde, int Hasta) RangoSerie(string prefijoOrigen, int ejercicioOrigen, int hastaOrigen) =>
        prefijoOrigen.Contains(ejercicioOrigen.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            ? (1, Math.Max(hastaOrigen, 1))
            : (PrimerNumero(ejercicioOrigen + 1), (ejercicioOrigen + 1) * 10000 + 9999);

    /// <summary>Copia las series que faltan: el año del prefijo y de la descripción pasa al siguiente y la numeración empieza de nuevo (<see cref="RangoSerie"/>).</summary>
    private Task<int> CopiarSeriesAsync(ContextoSociedadEjercicio origen, ContextoSociedadEjercicio destino) => bd.EnTransaccionAsync(async s =>
    {
        var series = await s.ConsultarAsync(
            "SELECT Descripcion, Prefijo, RangoHasta, EsRectificativa, EsPredeterminada FROM SeriesFacturacion WHERE IdSociedadEjercicio = ? ORDER BY IdSerie",
            f => (Descripcion: f.Texto("Descripcion") ?? "", Prefijo: f.Texto("Prefijo") ?? "", Hasta: f.Entero("RangoHasta"), Rectificativa: f.Logico("EsRectificativa"),
                Predeterminada: f.Logico("EsPredeterminada")),
            origen.IdSociedadEjercicio);
        var existentes = (await s.ConsultarAsync("SELECT Prefijo FROM SeriesFacturacion WHERE IdSociedadEjercicio = ?", f => (f.Texto("Prefijo") ?? "").Trim(), destino.IdSociedadEjercicio))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var copiadas = 0;
        foreach (var serie in series)
        {
            // Un prefijo vacío se guarda como espacio si el campo no admite cadenas vacías (como la migración del ERP).
            var prefijo = NuevoTexto(serie.Prefijo, origen.Ejercicio);
            if (!existentes.Add(prefijo.Trim())) continue;
            var (desde, hasta) = RangoSerie(serie.Prefijo, origen.Ejercicio, serie.Hasta);
            await s.EjecutarAsync(
                "INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, Prefijo, RangoDesde, RangoHasta, UltimoNumero, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                destino.IdSociedadEjercicio, NuevoTexto(serie.Descripcion, origen.Ejercicio), prefijo, desde, hasta, null, serie.Rectificativa, serie.Predeterminada);
            copiadas++;
        }
        return copiadas;
    });

    /// <summary>Copia las tablas de configuración por ejercicio que el destino aún no tiene. Devuelve las que ha copiado.</summary>
    private Task<IReadOnlyList<string>> CopiarConfiguracionAsync(ContextoSociedadEjercicio origen, ContextoSociedadEjercicio destino) => bd.EnTransaccionAsync<IReadOnlyList<string>>(async s =>
    {
        var esquema = s.LeerEsquema();
        var copiadas = new List<string>();
        foreach (var nombre in TablasConfiguracion)
        {
            if (esquema.Tabla(nombre) is not { } tabla || tabla.Columna("IdSociedadEjercicio") is null) continue;
            if (Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM [{tabla.Nombre}] WHERE IdSociedadEjercicio = ?", destino.IdSociedadEjercicio), CultureInfo.InvariantCulture) > 0)
                continue;
            var filas = await s.ConsultarAsync($"SELECT * FROM [{tabla.Nombre}] WHERE IdSociedadEjercicio = ?",
                f => tabla.Columnas.Where(c => !EsAutonumerico(c)).ToDictionary(c => c.Nombre, c => f.Valor(c.Nombre), StringComparer.OrdinalIgnoreCase), origen.IdSociedadEjercicio);
            foreach (var fila in filas)
            {
                fila["IdSociedadEjercicio"] = destino.IdSociedadEjercicio;
                await InsertarAsync(s, tabla.Nombre, fila);
            }
            if (filas.Count > 0) copiadas.Add(tabla.Nombre);
        }
        return copiadas;
    });

    private Task<Dictionary<string, int>> CodigosAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
        (await s.ConsultarAsync("SELECT IdCuentaContable, Codigo FROM CuentasContables WHERE IdSociedadEjercicio = ?",
            f => (Id: f.Entero("IdCuentaContable"), Codigo: (f.Texto("Codigo") ?? "").Trim()), ctx.IdSociedadEjercicio))
        .GroupBy(x => x.Codigo, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal));

    /// <summary>Prorrata definitiva y provisional (año anterior) del ejercicio, en porcentaje.</summary>
    private static async Task<(decimal? Definitiva, decimal? Provisional)> ProrratasAsync(ISesionDatos s, ContextoSociedadEjercicio ctx)
    {
        var esquema = s.LeerEsquema();
        var definitiva = esquema.ExisteColumna("SociedadesEjercicios", "PorcentajeProrrataDefinitiva")
            ? await s.EscalarAsync("SELECT PorcentajeProrrataDefinitiva FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio) : null;
        var provisional = esquema.ExisteColumna("SociedadesEjercicios", "PorcentajeProrrataAñoAnterior")
            ? await s.EscalarAsync("SELECT [PorcentajeProrrataAñoAnterior] FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", ctx.IdSociedadEjercicio) : null;
        return (Decimal(definitiva), Decimal(provisional));
        static decimal? Decimal(object? v) => v is null or DBNull ? null : Math.Round(Convert.ToDecimal(v, CultureInfo.InvariantCulture), 4, MidpointRounding.AwayFromZero);
    }

    private static Task InsertarAsync(ISesionDatos s, string tabla, IReadOnlyDictionary<string, object?> valores)
    {
        var columnas = valores.Keys.ToList();
        return s.EjecutarAsync($"INSERT INTO [{tabla}] ({string.Join(", ", columnas.Select(c => $"[{c}]"))}) VALUES ({string.Join(", ", columnas.Select(_ => "?"))})",
            [.. columnas.Select(c => valores[c])]);
    }

    private static bool EsAutonumerico(ColumnaFisica c) => c.Tipo.StartsWith("COUNTER", StringComparison.OrdinalIgnoreCase);

    /// <summary>El texto con el año <paramref name="ejercicio"/> cambiado por el siguiente («F2026» → «F2027»); sin el año, igual.</summary>
    public static string NuevoTexto(string texto, int ejercicio) =>
        texto.Replace(ejercicio.ToString(CultureInfo.InvariantCulture), (ejercicio + 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
