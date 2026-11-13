using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;

namespace ProConta.Norma43.Lineas;

/// <summary>Situación de una línea bancaria en la pantalla de líneas N43 (norma43/pantallas).</summary>
public enum EstadoLineaN43
{
    /// <summary>Sin cuenta de destino: no se puede contabilizar.</summary>
    SinCuenta,
    /// <summary>Con cuenta, pendiente de contabilizar.</summary>
    ConCuenta,
    /// <summary>Contabilizada por ProContaVF: se puede recontabilizar, actualizar sus textos o deshacer.</summary>
    Contabilizada,
    /// <summary>Contabilizada en el ERP (su asiento es de ContaSol): solo se consulta; se corrige desde el Mayor.</summary>
    ContabilizadaErp,
}

/// <summary>Ejercicio de la sociedad para el selector de la pantalla.</summary>
public sealed record EjercicioLineasN43(int IdSociedadEjercicio, int Ejercicio, bool Cerrado);

/// <summary>Cuenta de destino de una línea (una imputación viva): su código en el ejercicio de la línea, el importe y quién la puso.</summary>
/// <param name="ImporteFirmado">Positivo en los cargos (la contrapartida va al Debe) y negativo en los abonos.</param>
/// <param name="Origen">PALABRA_CLAVE, MANUAL, TRASPASO o lo que trajo la migración del ERP.</param>
public sealed record DestinoLineaN43(int IdIdentidadContable, string? Cuenta, string? Nombre, decimal ImporteFirmado, string Origen, int? IdPalabraClave, string? PalabraClave);

/// <summary>Línea bancaria Norma 43 tal como la muestra la rejilla, sin las de lotes revertidos.</summary>
/// <param name="Ejercicio">Año de la fecha contable del movimiento (la fecha valor salvo que sea del año anterior a la de operación).</param>
/// <param name="CuentaOrigen">La 572 de la cuenta bancaria en la fecha del movimiento, con su código del ejercicio de la línea.</param>
/// <param name="Tipo">ConceptosN43.ConceptoReducido (norma43/conciliacion 2026-11-13 10:50), calculado al vuelo; null si ninguna regla encaja.</param>
public sealed record LineaN43(
    int Id, int IdLote, int? IdCuentaBancaria, string NombreBanco, string Ccc, string Entidad,
    DateTime FechaOperacion, DateTime FechaValor, int Ejercicio, bool EjercicioCerrado,
    string Sentido, decimal Importe, string? ConceptoComun, string? Concepto, string? Documento, string? Referencia1, string? Referencia2,
    string? CuentaOrigen, string? NombreCuentaOrigen, IReadOnlyList<DestinoLineaN43> Destinos,
    int? IdAsiento, bool AsientoPropio, string? Tipo)
{
    /// <summary>Importe con signo desde el punto de vista del banco: positivo si entra dinero (abono), negativo si sale (cargo).</summary>
    public decimal ImporteFirmado => Sentido == ServicioConciliacionN43.Abono ? Importe : -Importe;

    public bool EsCargo => Sentido == ServicioConciliacionN43.Cargo;

    public EstadoLineaN43 Estado => IdAsiento is not null
        ? (AsientoPropio ? EstadoLineaN43.Contabilizada : EstadoLineaN43.ContabilizadaErp)
        : Destinos.Count == 0 ? EstadoLineaN43.SinCuenta : EstadoLineaN43.ConCuenta;

    /// <summary>Código de la cuenta de destino; «Varias» si está repartida.</summary>
    public string? CuentaDestino => Destinos.Count switch { 0 => null, 1 => Destinos[0].Cuenta, _ => "Varias" };

    public string? NombreCuentaDestino => Destinos.Count switch
    {
        0 => null,
        1 => Destinos[0].Nombre,
        _ => string.Join(" · ", Destinos.Select(d => d.Cuenta)),
    };

    /// <summary>Texto en que se buscan el filtro de texto y las palabras clave: concepto, referencias y documento.</summary>
    public string TextoBusqueda => string.Join(" ", new[] { Concepto, Referencia1, Referencia2, Documento }.Where(t => !string.IsNullOrWhiteSpace(t)));

    /// <summary>Cómo se nombra la línea en los mensajes al operador.</summary>
    public string Rotulo => $"Movimiento del {FechaOperacion:dd/MM/yyyy} de {Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €";
}

/// <summary>
/// Lecturas de la pantalla de líneas N43 (norma43/pantallas): las líneas de la sociedad en los ejercicios elegidos con su cuenta de
/// origen (la 572 de su banco), sus cuentas de destino (las imputaciones vivas), su asiento y su tipo. Pocas consultas grandes, que se
/// cruzan en memoria: con Access es mucho más rápido que una consulta por línea.
/// </summary>
public sealed class ConsultaLineasN43(IBaseDatos bd)
{
    /// <summary>Ejercicios de la sociedad, del más reciente al más antiguo.</summary>
    public Task<IReadOnlyList<EjercicioLineasN43>> EjerciciosAsync(string idSociedad) => bd.EjecutarAsync(async s =>
        (IReadOnlyList<EjercicioLineasN43>)(await s.ConsultarAsync(
            "SELECT IdSociedadEjercicio, Ejercicio, EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedad = ?",
            f => new EjercicioLineasN43(f.Entero("IdSociedadEjercicio"), Anio(f.Texto("Ejercicio")), !f.EsNulo("EjercicioCerrado") && f.Logico("EjercicioCerrado")), idSociedad))
        .Where(e => e.Ejercicio > 0).OrderByDescending(e => e.Ejercicio).ToList());

    /// <summary>Líneas de la sociedad cuya fecha contable cae en alguno de los ejercicios; sin las de lotes revertidos.</summary>
    public async Task<IReadOnlyList<LineaN43>> LineasAsync(string idSociedad, IReadOnlyCollection<int> anios)
    {
        ArgumentNullException.ThrowIfNull(anios);
        if (anios.Count == 0) return [];
        var ejercicios = await EjerciciosAsync(idSociedad);
        return await LeerAsync(idSociedad, anios, null, ejercicios);
    }

    /// <summary>Las líneas indicadas (de cualquier ejercicio) con sus datos al día, para refrescar tras una acción.</summary>
    public async Task<IReadOnlyList<LineaN43>> LineasAsync(string idSociedad, IReadOnlyCollection<int>? anios, IReadOnlyCollection<int> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return [];
        var ejercicios = await EjerciciosAsync(idSociedad);
        return await LeerAsync(idSociedad, anios, ids, ejercicios);
    }

    private Task<IReadOnlyList<LineaN43>> LeerAsync(string idSociedad, IReadOnlyCollection<int>? anios, IReadOnlyCollection<int>? ids,
        IReadOnlyList<EjercicioLineasN43> ejercicios) => bd.EjecutarAsync<IReadOnlyList<LineaN43>>(async s =>
    {
        const string desde = "(LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
            "INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria";
        var sql = "SELECT l.IdLineaExtractoBancario, lo.IdLoteImportacionBancaria, e.IdCuentaBancaria, e.Entidad, e.Oficina, e.Cuenta, l.FechaOperacion, l.FechaValor, " +
            "l.Sentido, l.Importe, l.ConceptoComun, l.ConceptoCompleto, l.Documento, l.Referencia1, l.Referencia2 " +
            $"FROM {desde} WHERE lo.IdSociedad = ? AND lo.Estado <> ?";
        var parametros = new List<object?> { idSociedad, ServicioImportacionN43.EstadoRevertido };
        if (ids is null && anios is { Count: > 0 })
        {
            // La fecha contable es la valor o la de operación: basta con que una de las dos caiga en el rango (se afina en memoria).
            sql += " AND ((l.FechaValor >= ? AND l.FechaValor < ?) OR (l.FechaOperacion >= ? AND l.FechaOperacion < ?))";
            var inicio = new DateTime(anios.Min(), 1, 1);
            var fin = new DateTime(anios.Max() + 1, 1, 1);
            parametros.AddRange([inicio, fin, inicio, fin]);
        }
        var filas = new List<FilaLeida>();
        IEnumerable<int[]?> bloques = ids is null ? [null] : ids.Distinct().Chunk(300);
        foreach (var bloque in bloques)
        {
            var sqlBloque = bloque is null ? sql : sql + $" AND l.IdLineaExtractoBancario IN ({Lista(bloque)})";
            filas.AddRange(await s.ConsultarAsync(sqlBloque, f => new FilaLeida(
                f.Entero("IdLineaExtractoBancario"), f.Entero("IdLoteImportacionBancaria"), f.EnteroNulo("IdCuentaBancaria"), Limpio(f.Texto("Entidad")) ?? "",
                $"{Limpio(f.Texto("Entidad"))} {Limpio(f.Texto("Oficina"))} {Limpio(f.Texto("Cuenta"))}", f.Fecha("FechaOperacion").Date, f.Fecha("FechaValor").Date,
                (Limpio(f.Texto("Sentido")) ?? "").ToUpperInvariant(), f.Decimal("Importe"), Limpio(f.Texto("ConceptoComun")), Limpio(f.Texto("ConceptoCompleto")),
                Limpio(f.Texto("Documento")), Limpio(f.Texto("Referencia1")), Limpio(f.Texto("Referencia2"))), [.. parametros]));
        }
        var porAnio = ejercicios.ToDictionary(e => e.Ejercicio);
        if (anios is { Count: > 0 } && ids is null)
        {
            var elegidos = anios.ToHashSet();
            filas = filas.Where(f => elegidos.Contains(f.Ejercicio)).ToList();
        }
        if (filas.Count == 0) return [];

        // Imputaciones vivas, contabilizaciones y su asiento, por bloques de Id.
        var imputaciones = new List<(int Linea, int Identidad, decimal Importe, string Origen, int? Palabra)>();
        var contabilizadas = new Dictionary<int, (int Asiento, bool Propio, bool Existe)>();
        foreach (var bloque in filas.Select(f => f.Id).Chunk(300))
        {
            var lista = Lista(bloque);
            imputaciones.AddRange(await s.ConsultarAsync(
                "SELECT IdImputacionLineaExtractoBancario, IdLineaExtractoBancario, IdIdentidadContable, ImporteFirmado, Origen, IdPalabraClaveBancaria FROM ImputacionesLineaExtractoBancario " +
                $"WHERE Estado <> 'DESCARTADA' AND IdLineaExtractoBancario IN ({lista}) ORDER BY IdImputacionLineaExtractoBancario",
                f => (f.Entero("IdLineaExtractoBancario"), f.Entero("IdIdentidadContable"), f.Decimal("ImporteFirmado"), (Limpio(f.Texto("Origen")) ?? "").ToUpperInvariant(),
                    f.EnteroNulo("IdPalabraClaveBancaria"))));
            var c = await s.ConsultarAsync(
                "SELECT c.IdLineaExtractoBancario, c.IdAsiento, a.EsAutomatico, a.MotorPropietario, a.CodigoProceso, a.ClaveEjecucion " +
                "FROM ContabilizacionesLineaExtracto AS c LEFT JOIN Asientos AS a ON c.IdAsiento = a.IdAsiento " +
                $"WHERE c.IdLineaExtractoBancario IN ({lista}) ORDER BY c.IdContabilizacionLineaExtracto DESC",
                f => (Linea: f.Entero("IdLineaExtractoBancario"), Asiento: f.Entero("IdAsiento"), Existe: !f.EsNulo("EsAutomatico"),
                    Propio: !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico") && f.Texto("MotorPropietario") == ServicioConciliacionN43.MotorPropietario
                        && f.Texto("CodigoProceso") == ServicioConciliacionN43.CodigoProceso
                        && f.Texto("ClaveEjecucion") == f.Entero("IdLineaExtractoBancario").ToString(CultureInfo.InvariantCulture)));
            foreach (var g in c.GroupBy(x => x.Linea))
            {
                var mejor = g.OrderByDescending(x => x.Existe).First();
                contabilizadas[g.Key] = (mejor.Asiento, mejor.Propio, mejor.Existe);
            }
        }

        // La 572 de cada cuenta bancaria según la fecha, y la cuenta puente.
        var configuraciones = (await s.ConsultarAsync(
                "SELECT cfg.IdCuentaBancaria, cfg.IdIdentidadContableBanco, cfg.FechaDesde, cfg.FechaHasta, cb.Nombre FROM ConfiguracionesContablesCuentaBancaria AS cfg " +
                "INNER JOIN CuentasBancarias AS cb ON cfg.IdCuentaBancaria = cb.IdCuentaBancaria WHERE cb.IdSociedad = ? ORDER BY cfg.FechaDesde DESC",
                f => (Cuenta: f.Entero("IdCuentaBancaria"), Identidad: f.Entero("IdIdentidadContableBanco"), Desde: f.FechaNula("FechaDesde"), Hasta: f.FechaNula("FechaHasta")), idSociedad))
            .ToLookup(x => x.Cuenta);
        var nombresBanco = (await s.ConsultarAsync("SELECT IdCuentaBancaria, Nombre FROM CuentasBancarias WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdCuentaBancaria"), Nombre: Limpio(f.Texto("Nombre"))), idSociedad)).ToDictionary(x => x.Id, x => x.Nombre);
        int? Banco(int? idCuenta, DateTime fecha)
        {
            if (idCuenta is not int id) return null;
            var lista = configuraciones[id].ToList();
            var enFecha = lista.FirstOrDefault(x => (x.Desde is null || x.Desde.Value.Date <= fecha) && (x.Hasta is null || x.Hasta.Value.Date >= fecha));
            if (enFecha.Identidad != 0) return enFecha.Identidad;
            var vigente = lista.FirstOrDefault(x => x.Hasta is null);
            return vigente.Identidad != 0 ? vigente.Identidad : null;
        }

        // Códigos de las cuentas en el ejercicio de cada línea (o, si no existen en él, el último que tuvieron).
        var identidades = imputaciones.Select(i => i.Identidad).Concat(filas.Select(f => Banco(f.IdCuentaBancaria, f.FechaMovimiento) ?? 0)).Where(i => i != 0).Distinct().ToList();
        var codigos = new Dictionary<(int Ejercicio, int Identidad), (string Codigo, string Nombre)>();
        var ultimos = new Dictionary<int, (string Codigo, string Nombre)>();
        var idsEjercicio = ejercicios.ToDictionary(e => e.IdSociedadEjercicio, e => e.Ejercicio);
        foreach (var bloque in identidades.Chunk(300))
        {
            var cuentas = await s.ConsultarAsync(
                $"SELECT IdSociedadEjercicio, IdIdentidadContable, Codigo, Nombre FROM CuentasContables WHERE IdIdentidadContable IN ({Lista(bloque)}) ORDER BY IdSociedadEjercicio",
                f => (Ej: f.Entero("IdSociedadEjercicio"), Id: f.Entero("IdIdentidadContable"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim()));
            foreach (var c in cuentas)
            {
                ultimos[c.Id] = (c.Codigo, c.Nombre);
                if (idsEjercicio.TryGetValue(c.Ej, out var anio)) codigos[(anio, c.Id)] = (c.Codigo, c.Nombre);
            }
        }
        (string Codigo, string Nombre)? Cuenta(int anio, int identidad) =>
            codigos.TryGetValue((anio, identidad), out var c) ? c : ultimos.TryGetValue(identidad, out var u) ? u : null;

        var palabras = new Dictionary<int, string>();
        if (imputaciones.Any(i => i.Palabra is not null))
            foreach (var p in await s.ConsultarAsync("SELECT IdPalabraClaveBancaria, PalabraClave, PalabraClave2 FROM PalabrasClaveBancarias WHERE IdSociedad = ?",
                         f => (Id: f.Entero("IdPalabraClaveBancaria"), Uno: Limpio(f.Texto("PalabraClave")), Dos: Limpio(f.Texto("PalabraClave2"))), idSociedad))
                palabras[p.Id] = p.Dos is null ? p.Uno ?? "" : $"{p.Uno} + {p.Dos}";
        var reglas = await ReglasConceptoN43.LeerAsync(bd);
        var porLinea = imputaciones.ToLookup(i => i.Linea);
        var r = new List<LineaN43>(filas.Count);
        foreach (var f in filas)
        {
            var anio = f.Ejercicio;
            var destinos = porLinea[f.Id].Select(i =>
            {
                var c = Cuenta(anio, i.Identidad);
                return new DestinoLineaN43(i.Identidad, c?.Codigo, c?.Nombre, i.Importe, i.Origen, i.Palabra, i.Palabra is int p ? palabras.GetValueOrDefault(p) : null);
            }).ToList();
            var banco = Banco(f.IdCuentaBancaria, f.FechaMovimiento) is int b ? Cuenta(anio, b) : null;
            var c = contabilizadas.TryGetValue(f.Id, out var x) && x.Existe ? x : ((int Asiento, bool Propio, bool Existe)?)null;
            var tipo = ReglasConceptoN43.Elegir(reglas, new MovimientoConceptoN43(f.ConceptoComun, f.Sentido, f.Entidad, f.Concepto, destinos.FirstOrDefault()?.Cuenta))?.ConceptoReducido;
            var nombreBanco = f.IdCuentaBancaria is int ib ? nombresBanco.GetValueOrDefault(ib) : null;
            r.Add(new LineaN43(f.Id, f.IdLote, f.IdCuentaBancaria, nombreBanco ?? banco?.Nombre ?? f.Ccc, f.Ccc, f.Entidad, f.FechaOperacion, f.FechaValor, anio,
                porAnio.TryGetValue(anio, out var ej) && ej.Cerrado, f.Sentido, f.Importe, f.ConceptoComun, f.Concepto, f.Documento, f.Referencia1, f.Referencia2,
                banco?.Codigo, banco?.Nombre, destinos, c?.Asiento, c?.Propio ?? false, tipo));
        }
        return r.OrderBy(l => l.FechaOperacion).ThenBy(l => l.Id).ToList();
    });

    private sealed record FilaLeida(int Id, int IdLote, int? IdCuentaBancaria, string Entidad, string Ccc, DateTime FechaOperacion, DateTime FechaValor, string Sentido,
        decimal Importe, string? ConceptoComun, string? Concepto, string? Documento, string? Referencia1, string? Referencia2)
    {
        public DateTime FechaMovimiento => ServicioConciliacionN43.FechaMovimiento(FechaOperacion, FechaValor);
        public int Ejercicio => FechaMovimiento.Year;
    }

    internal static string Lista(IEnumerable<int> ids) => string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));

    internal static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    internal static int Anio(string? texto) => int.TryParse((texto ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? a : 0;
}
