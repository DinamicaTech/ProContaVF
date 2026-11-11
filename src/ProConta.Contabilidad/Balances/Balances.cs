using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Balances;

/// <summary>Estado contable (columna ModelosBalance.TipoBalance).</summary>
public enum TipoBalance : byte
{
    PerdidasYGanancias = 1,
    Situacion = 2
}

/// <summary>
/// Regla de clasificación de un modelo de balance (ReglasCuentasBalance con la ruta de su nodo): las cuentas que empiezan
/// por <paramref name="Prefijo"/> van a ese bloque, apartado, título y subtítulo, con el importe (Debe − Haber) por
/// <paramref name="Multiplicador"/>. Los órdenes son los de NodosBalance y fijan el orden oficial de presentación.
/// </summary>
public sealed record ReglaBalance(
    string Prefijo, int Multiplicador, int Prioridad,
    string Bloque, int OrdenBloque, string Apartado, int OrdenApartado, string Titulo, int OrdenTitulo, string? Subtitulo);

/// <summary>Modelo de balance activo de un tipo (ModelosBalance) con sus reglas.</summary>
public sealed record ModeloBalance(string Codigo, string Nombre, TipoBalance Tipo, IReadOnlyList<ReglaBalance> Reglas)
{
    private readonly ReglaBalance[] _ordenadas = Reglas.OrderByDescending(r => r.Prioridad).ThenByDescending(r => r.Prefijo.Length).ToArray();

    /// <summary>La regla aplicable a una cuenta: la de más prioridad y, a igualdad, la de prefijo más largo; null si ninguna.</summary>
    public ReglaBalance? Regla(string cuenta) =>
        Array.Find(_ordenadas, r => r.Prefijo.Length > 0 && cuenta.StartsWith(r.Prefijo, StringComparison.Ordinal));
}

/// <summary>Apunte contabilizado del ejercicio tal como lo necesitan los balances.</summary>
public sealed record ApunteBalance(
    DateTime Fecha, int IdAsiento, byte TipoAsiento, int Orden, string Cuenta, decimal Debe, decimal Haber, string Concepto, int? IdCentroCoste);

/// <summary>Centro de coste del apunte: su raíz (CentroDeCoste), su ruta (SubDepartamento) y su nombre (NombreDepartamento).</summary>
public sealed record CentroBalance(string Raiz, string Ruta, string Nombre);

/// <summary>Una fila de la hoja de datos: un apunte clasificado, con el importe ya con el signo del modelo.</summary>
public sealed record FilaBalance(
    DateTime Fecha, string Bloque, int OrdenBloque, string Apartado, int OrdenApartado, string Titulo, int OrdenTitulo, string? Subtitulo,
    string Cuenta, string Subcuenta, decimal Importe, string CuentaContable, int IdAsiento, int Orden, string Concepto,
    string? CentroCoste, string? SubDepartamento, string? NombreDepartamento);

/// <summary>Cuenta con apuntes que el modelo no clasifica: no entra en el balance y se lista aparte para que no se pierda.</summary>
public sealed record CuentaSinClasificar(string Cuenta, string Nombre, int Apuntes, decimal Debe, decimal Haber)
{
    public decimal Saldo => Debe - Haber;
}

/// <summary>Balance generado: las filas clasificadas en orden oficial y las cuentas sin clasificar.</summary>
public sealed record ResultadoBalance(
    ModeloBalance Modelo, string RazonSocial, int Ejercicio, IReadOnlyList<FilaBalance> Filas, IReadOnlyList<CuentaSinClasificar> SinClasificar)
{
    /// <summary>Suma de importes por bloque (ACTIVO, PATRIMONIO NETO Y PASIVO o la cuenta de pérdidas y ganancias), en orden.</summary>
    public IReadOnlyList<(string Bloque, decimal Importe)> TotalesPorBloque =>
        Filas.GroupBy(f => (f.OrdenBloque, f.Bloque)).OrderBy(g => g.Key.OrdenBloque).Select(g => (g.Key.Bloque, g.Sum(f => f.Importe))).ToList();

    /// <summary>Suma de todos los importes: en la cuenta de pérdidas y ganancias, el resultado (positivo, beneficio).</summary>
    public decimal Total => Filas.Sum(f => f.Importe);
}

/// <summary>
/// Balance de situación y cuenta de pérdidas y ganancias (contabilidad/balances): clasifica los apuntes contabilizados del
/// ejercicio con el modelo activo de ModelosBalance, como el ProConta anterior (sources/proconta-antiguo/menu-principal § 4).
/// </summary>
public static class Clasificador
{
    /// <summary>
    /// Qué asientos entran: en pérdidas y ganancias, todos salvo la regularización y el cierre, y solo las cuentas de los grupos
    /// 6 y 7; en el balance de situación, todos salvo el cierre (el resultado del ejercicio sale de las cuentas 6 y 7, o de la
    /// 129 si ya se regularizó).
    /// </summary>
    public static bool Entra(TipoBalance tipo, ApunteBalance a) => tipo switch
    {
        TipoBalance.PerdidasYGanancias => a.TipoAsiento is not (TipoRegularizacion or TipoCierre) && a.Cuenta.Length > 0 && a.Cuenta[0] is '6' or '7',
        _ => a.TipoAsiento != TipoCierre,
    };

    public const byte TipoRegularizacion = 2;
    public const byte TipoCierre = 3;

    /// <summary>Clasifica los apuntes. Las filas salen en orden oficial (bloque, apartado, título), cuenta, fecha y asiento.</summary>
    /// <param name="nombres">Nombre de cada código del ejercicio (subcuentas y cuentas de 3 dígitos).</param>
    public static ResultadoBalance Clasificar(
        ModeloBalance modelo, string razonSocial, int ejercicio, IEnumerable<ApunteBalance> apuntes,
        IReadOnlyDictionary<string, string> nombres, IReadOnlyDictionary<int, CentroBalance> centros)
    {
        var filas = new List<FilaBalance>();
        var sin = new Dictionary<string, CuentaSinClasificar>(StringComparer.Ordinal);
        foreach (var a in apuntes)
        {
            if (!Entra(modelo.Tipo, a)) continue;
            var regla = modelo.Regla(a.Cuenta);
            if (regla is null)
            {
                sin[a.Cuenta] = sin.TryGetValue(a.Cuenta, out var s)
                    ? s with { Apuntes = s.Apuntes + 1, Debe = s.Debe + a.Debe, Haber = s.Haber + a.Haber }
                    : new CuentaSinClasificar(a.Cuenta, nombres.GetValueOrDefault(a.Cuenta, ""), 1, a.Debe, a.Haber);
                continue;
            }
            var grupo = a.Cuenta.Length >= 3 ? a.Cuenta[..3] : a.Cuenta;
            var centro = a.IdCentroCoste is int id ? centros.GetValueOrDefault(id) : null;
            filas.Add(new FilaBalance(
                a.Fecha, regla.Bloque, regla.OrdenBloque, regla.Apartado, regla.OrdenApartado, regla.Titulo, regla.OrdenTitulo, regla.Subtitulo,
                Etiqueta(grupo, nombres), Etiqueta(a.Cuenta, nombres), (a.Debe - a.Haber) * regla.Multiplicador, a.Cuenta, a.IdAsiento, a.Orden,
                a.Concepto, centro?.Raiz, centro?.Ruta, centro?.Nombre));
        }
        var ordenadas = filas
            .OrderBy(f => f.OrdenBloque).ThenBy(f => f.OrdenApartado).ThenBy(f => f.OrdenTitulo)
            .ThenBy(f => f.CuentaContable, StringComparer.Ordinal).ThenBy(f => f.Fecha).ThenBy(f => f.IdAsiento).ThenBy(f => f.Orden)
            .ToList();
        return new ResultadoBalance(modelo, razonSocial, ejercicio, ordenadas, sin.Values.OrderBy(s => s.Cuenta, StringComparer.Ordinal).ToList());
    }

    private static string Etiqueta(string codigo, IReadOnlyDictionary<string, string> nombres) =>
        nombres.TryGetValue(codigo, out var n) && n.Length > 0 ? $"{codigo} {n}" : codigo;
}

/// <summary>Lee de la BD el modelo, los apuntes, los nombres y los centros, y genera el balance del ejercicio.</summary>
public sealed class GeneradorBalances(IBaseDatos bd)
{
    public async Task<ResultadoBalance> GenerarAsync(TipoBalance tipo, ContextoSociedadEjercicio contexto, string razonSocial, CancellationToken cancelacion = default)
    {
        var (modelo, apuntes, nombres, centros) = await bd.EjecutarAsync(async s =>
        {
            var m = await LeerModeloAsync(s, tipo);
            var a = await s.ConsultarAsync(
                "SELECT a.FechaContable, a.IdAsiento, a.TipoAsiento, a.Descripcion AS DescripcionAsiento, p.Orden, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, " +
                "p.Descripcion, p.IdCentroCoste FROM Asientos AS a INNER JOIN Apuntes AS p ON a.IdAsiento = p.IdAsiento " +
                "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1",
                f => new ApunteBalance(
                    f.Fecha("FechaContable").Date, f.Entero("IdAsiento"), Convert.ToByte(f.Valor("TipoAsiento"), CultureInfo.InvariantCulture),
                    f.EsNulo("Orden") ? 0 : Convert.ToInt32(f.Valor("Orden"), CultureInfo.InvariantCulture), (f.Texto("CuentaContable") ?? "").Trim(),
                    f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m,
                    (f.Texto("Descripcion") is { Length: > 0 } d ? d : f.Texto("DescripcionAsiento") ?? "").Trim(), f.EnteroNulo("IdCentroCoste")),
                contexto.IdSociedadEjercicio);
            var n = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (codigo, nombre) in await s.ConsultarAsync("SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?",
                         f => ((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim()), contexto.IdSociedadEjercicio))
                if (codigo.Length > 0) n.TryAdd(codigo, nombre);
            var filasCentros = await s.ConsultarAsync(
                "SELECT IdCentroCoste, IdCentroCosteRaiz, Codigo, Nombre, RutaJerarquica FROM CentrosCoste WHERE IdSociedad = ?",
                f => (Id: f.Entero("IdCentroCoste"), Raiz: f.EnteroNulo("IdCentroCosteRaiz"), Codigo: (f.Texto("Codigo") ?? "").Trim(),
                    Nombre: (f.Texto("Nombre") ?? "").Trim(), Ruta: f.Texto("RutaJerarquica")?.Trim()),
                contexto.IdSociedad);
            var porId = filasCentros.ToDictionary(c => c.Id);
            var c = filasCentros.ToDictionary(x => x.Id, x => new CentroBalance(
                x.Raiz is int r && porId.TryGetValue(r, out var raiz) ? raiz.Nombre : x.Nombre,
                string.IsNullOrEmpty(x.Ruta) ? x.Codigo : x.Ruta, x.Nombre));
            return (m, a, (IReadOnlyDictionary<string, string>)n, (IReadOnlyDictionary<int, CentroBalance>)c);
        }, cancelacion);
        return Clasificador.Clasificar(modelo, razonSocial, contexto.Ejercicio, apuntes, nombres, centros);
    }

    /// <summary>El modelo activo más reciente del tipo, con la ruta (bloque, apartado, título, subtítulo) de cada regla.</summary>
    public static async Task<ModeloBalance> LeerModeloAsync(ISesionDatos s, TipoBalance tipo)
    {
        var modelos = await s.ConsultarAsync(
            "SELECT IdModeloBalance, Codigo, Nombre FROM ModelosBalance WHERE TipoBalance = ? AND EstaActivo = True ORDER BY FechaVigenciaDesde DESC, IdModeloBalance DESC",
            f => (Id: f.Entero("IdModeloBalance"), Codigo: f.Texto("Codigo") ?? "", Nombre: f.Texto("Nombre") ?? ""), (byte)tipo);
        if (modelos.Count == 0)
            throw new InvalidOperationException(
                $"La BD no tiene ningún modelo activo de {(tipo == TipoBalance.Situacion ? "balance de situación" : "pérdidas y ganancias")} en ModelosBalance. " +
                "Actualiza el esquema con el ActualizadorEsquema: su migración «Esquema de balances PGC Pymes 2008» carga el modelo cuando las tablas están vacías.");
        var modelo = modelos[0];
        var nodos = (await s.ConsultarAsync(
                "SELECT IdNodoBalance, IdNodoPadre, Codigo, Nombre, Orden, Nivel FROM NodosBalance WHERE IdModeloBalance = ? AND EstaActivo = True",
                f => new NodoLeido(f.Entero("IdNodoBalance"), f.EnteroNulo("IdNodoPadre"), (f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(),
                    Convert.ToInt32(f.Valor("Orden"), CultureInfo.InvariantCulture), Convert.ToInt32(f.Valor("Nivel"), CultureInfo.InvariantCulture)),
                modelo.Id))
            .ToDictionary(n => n.Id);
        var reglas = await s.ConsultarAsync(
            "SELECT r.PrefijoCuenta, r.Multiplicador, r.Prioridad, r.IdNodoBalance FROM ReglasCuentasBalance AS r INNER JOIN NodosBalance AS n ON r.IdNodoBalance = n.IdNodoBalance " +
            "WHERE n.IdModeloBalance = ? AND n.EstaActivo = True AND r.EsInclusion = True",
            f => (Prefijo: (f.Texto("PrefijoCuenta") ?? "").Trim(), Multiplicador: Convert.ToInt32(f.Valor("Multiplicador"), CultureInfo.InvariantCulture),
                Prioridad: Convert.ToInt32(f.Valor("Prioridad"), CultureInfo.InvariantCulture), Nodo: f.Entero("IdNodoBalance")),
            modelo.Id);
        return new ModeloBalance(modelo.Codigo, modelo.Nombre, tipo,
            reglas.Where(r => r.Prefijo.Length > 0 && nodos.ContainsKey(r.Nodo)).Select(r =>
            {
                var ruta = Ruta(nodos, r.Nodo, tipo);
                return new ReglaBalance(r.Prefijo, r.Multiplicador, r.Prioridad, ruta.Bloque, ruta.OrdenBloque, ruta.Apartado, ruta.OrdenApartado,
                    ruta.Titulo, ruta.OrdenTitulo, ruta.Subtitulo);
            }).ToList());
    }

    internal sealed record NodoLeido(int Id, int? Padre, string Codigo, string Nombre, int Orden, int Nivel);

    /// <summary>
    /// Sube por el árbol desde el nodo de la regla. El orden del apartado es el menor de sus títulos (los apartados no lo
    /// guardan bien en la maestra). En pérdidas y ganancias, los títulos llevan delante su número del modelo oficial
    /// (el del código del nodo, «PYG.A.4…» → «4. Aprovisionamientos.»), como en las cuentas anuales.
    /// </summary>
    internal static (string Bloque, int OrdenBloque, string Apartado, int OrdenApartado, string Titulo, int OrdenTitulo, string? Subtitulo) Ruta(
        IReadOnlyDictionary<int, NodoLeido> nodos, int idNodo, TipoBalance tipo)
    {
        NodoLeido? bloque = null, apartado = null, titulo = null, subtitulo = null;
        int? actual = idNodo;
        for (var i = 0; actual is int id && i < 10 && nodos.TryGetValue(id, out var n); i++, actual = n.Padre)
        {
            switch (n.Nivel)
            {
                case 0: bloque = n; break;
                case 1: apartado = n; break;
                case 2: titulo = n; break;
                default: subtitulo ??= n; break;
            }
        }
        var ordenApartado = apartado is null ? 0 : nodos.Values.Where(x => x.Padre == apartado.Id && x.Nivel == 2).Select(x => x.Orden).DefaultIfEmpty(apartado.Orden).Min();
        var nombreTitulo = titulo?.Nombre ?? "";
        if (tipo == TipoBalance.PerdidasYGanancias && titulo is not null && nombreTitulo.Length > 0 && !char.IsDigit(nombreTitulo[0]))
        {
            var partes = titulo.Codigo.Split('.');
            if (partes.Length >= 3 && int.TryParse(partes[2], NumberStyles.None, CultureInfo.InvariantCulture, out var numero))
                nombreTitulo = $"{numero}. {nombreTitulo}";
        }
        return (bloque?.Nombre ?? "", bloque?.Orden ?? 0, apartado?.Nombre ?? "", ordenApartado, nombreTitulo, titulo?.Orden ?? 0,
            subtitulo?.Nombre is { Length: > 0 } st ? st : null);
    }
}
