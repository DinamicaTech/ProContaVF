namespace ProConta.Contabilidad.Mayor;

/// <summary>
/// Lo que hace falta para ver el cuadre de las cuentas: el ejercicio de trabajo, todos los ejercicios de la sociedad (para heredar
/// las notas del último anterior), el saldo y los apuntes de cada cuenta en el ejercicio de trabajo y los cuadres guardados.
/// </summary>
public sealed class ContextoCuadre(EjercicioMayor? trabajo, IReadOnlyList<EjercicioMayor> ejercicios,
    IReadOnlyDictionary<string, (decimal Saldo, int Apuntes)> saldosTrabajo, IReadOnlyList<CuadreCuenta> cuadres)
{
    public static readonly ContextoCuadre Vacio = new(null, [], new Dictionary<string, (decimal, int)>(), []);

    private readonly Dictionary<int, int> _anioDe = ejercicios.ToDictionary(e => e.IdSociedadEjercicio, e => e.Ejercicio);
    private readonly ILookup<string, CuadreCuenta> _porCuenta = cuadres.ToLookup(c => c.Cuenta, StringComparer.Ordinal);

    public EjercicioMayor? Trabajo { get; } = trabajo;
    public IReadOnlyList<CuadreCuenta> Cuadres { get; } = cuadres;
    public IReadOnlyDictionary<string, (decimal Saldo, int Apuntes)> SaldosTrabajo { get; } = saldosTrabajo;

    public int AnioDe(int idSociedadEjercicio) => _anioDe.GetValueOrDefault(idSociedadEjercicio);

    /// <summary>Cuadre de la cuenta en el ejercicio de trabajo, si lo tiene.</summary>
    public CuadreCuenta? DelTrabajo(string cuenta) =>
        Trabajo is null ? null : _porCuenta[cuenta].FirstOrDefault(c => c.IdSociedadEjercicio == Trabajo.IdSociedadEjercicio);

    /// <summary>Cuadre del último ejercicio anterior al de trabajo con notas (de ahí se heredan).</summary>
    public CuadreCuenta? AnteriorConNotas(string cuenta) =>
        Trabajo is null ? null : _porCuenta[cuenta]
            .Where(c => !string.IsNullOrWhiteSpace(c.Notas) && AnioDe(c.IdSociedadEjercicio) is var a && a > 0 && a < Trabajo.Ejercicio)
            .OrderByDescending(c => AnioDe(c.IdSociedadEjercicio)).FirstOrDefault();
}

/// <summary>
/// Cuadre de cuentas del Mayor (contabilidad/mayor 2026-11-12), sin BD. Solo se cuadra el ejercicio de trabajo: cada cuenta tiene
/// la casilla «Cuadrada» (con el saldo que tenía al marcarla) y unas notas largas, que si el ejercicio aún no tiene cuadre se
/// heredan del último anterior. Rojo: ni notas ni casilla; ámbar: notas sin la casilla, o casilla con un saldo que ha cambiado;
/// verde: casilla con el mismo saldo.
/// </summary>
public static class CuadresMayor
{
    /// <summary>Umbral de «Reiniciar el cuadre» cuando la configuración no tiene otro (el del ProConta antiguo).</summary>
    public const decimal UmbralPredeterminado = 0.05m;

    /// <summary>Diferencia de saldo a partir de la cual una cuenta marcada deja de estar cuadrada.</summary>
    public const decimal Tolerancia = 0.005m;

    /// <summary>Saldo y apuntes de cada cuenta en un ejercicio, con su apertura y sin cierres (el que se guarda al marcarla).</summary>
    public static IReadOnlyDictionary<string, (decimal Saldo, int Apuntes)> SaldosDe(IEnumerable<SumaCuenta> sumas, int idSociedadEjercicio)
    {
        var d = new Dictionary<string, (decimal Saldo, int Apuntes)>(StringComparer.Ordinal);
        foreach (var s in sumas.Where(s => s.IdSociedadEjercicio == idSociedadEjercicio && s.TipoAsiento != TiposAsientoMayor.Cierre))
        {
            var x = d.GetValueOrDefault(s.Cuenta);
            d[s.Cuenta] = (x.Saldo + s.Debe - s.Haber, x.Apuntes + s.Apuntes);
        }
        return d;
    }

    public static VistaCuadre Ver(string cuenta, ContextoCuadre ctx)
    {
        if (ctx.Trabajo is null) return VistaCuadre.Vacia(cuenta);
        var (saldo, apuntes) = ctx.SaldosTrabajo.GetValueOrDefault(cuenta);
        var propio = ctx.DelTrabajo(cuenta);
        var heredado = propio is null ? ctx.AnteriorConNotas(cuenta) : null;
        var notas = propio?.Notas ?? heredado?.Notas;
        notas = string.IsNullOrWhiteSpace(notas) ? null : notas;
        if (propio is { Cuadrada: true })
        {
            if (propio.Saldo is decimal marcado && Math.Abs(marcado - saldo) >= Tolerancia)
                return new VistaCuadre(cuenta, EstadoCuadre.EnCurso, notas, false, propio, saldo,
                    $"El saldo ha cambiado desde el cuadre del {propio.FechaHora:dd/MM/yyyy}: era {CalculadoraMayor.Importe(marcado)} y ahora es {CalculadoraMayor.Importe(saldo)}.");
            return new VistaCuadre(cuenta, EstadoCuadre.Cuadrada, notas, false, propio, saldo, null);
        }
        if (notas is not null) return new VistaCuadre(cuenta, EstadoCuadre.EnCurso, notas, heredado is not null, propio, saldo, null);
        return new VistaCuadre(cuenta, apuntes == 0 && propio is null ? EstadoCuadre.SinMovimientos : EstadoCuadre.SinCuadrar, null, false, propio, saldo, null);
    }

    /// <summary>Ayuda del cuadre de una cuenta: estado, aviso, quién y cuándo, y las notas.</summary>
    public static string? Ayuda(VistaCuadre v, EjercicioMayor? trabajo)
    {
        if (v.Estado == EstadoCuadre.SinMovimientos) return null;
        var partes = new List<string> { $"{Texto(v.Estado)} en {trabajo?.Ejercicio}" };
        if (v.Aviso is not null) partes.Add(v.Aviso);
        if (v.Registro is { } r) partes.Add($"Última modificación: {r.FechaHora:dd/MM/yyyy HH:mm} por {r.Operador}.");
        if (v.Heredadas) partes.Add("Notas del ejercicio anterior (este aún no tiene cuadre):");
        if (v.Notas is not null) partes.Add(v.Notas.Length > 600 ? v.Notas[..600] + "…" : v.Notas);
        return string.Join(Environment.NewLine, partes);
    }

    public static string Texto(EstadoCuadre e) => e switch
    {
        EstadoCuadre.Cuadrada => "Cuadrada",
        EstadoCuadre.EnCurso => "En curso",
        EstadoCuadre.SinCuadrar => "Sin cuadrar",
        _ => "",
    };

    /// <summary>Estado de varias cuentas (una de grupo o un mayor combinado): el peor, con el recuento.</summary>
    public static (EstadoCuadre Estado, string? Resumen) Resumen(IEnumerable<VistaCuadre> cuentas)
    {
        var lista = cuentas.Where(v => v.Estado != EstadoCuadre.SinMovimientos).ToList();
        if (lista.Count == 0) return (EstadoCuadre.SinMovimientos, null);
        var verdes = lista.Count(v => v.Estado == EstadoCuadre.Cuadrada);
        var ambar = lista.Count(v => v.Estado == EstadoCuadre.EnCurso);
        var rojas = lista.Count - verdes - ambar;
        var estado = rojas > 0 ? EstadoCuadre.SinCuadrar : ambar > 0 ? EstadoCuadre.EnCurso : EstadoCuadre.Cuadrada;
        return (estado, $"{lista.Count} subcuentas: {verdes} cuadradas, {ambar} en curso y {rojas} sin cuadrar.");
    }

    /// <summary>
    /// Lo que se guarda al aceptar el diálogo de cuadre: casilla y notas en el ejercicio de trabajo; con la casilla, el saldo actual.
    /// </summary>
    public static CuadreCuenta Guardar(string cuenta, ContextoCuadre ctx, bool cuadrada, string? notas, string operador, DateTime ahora)
    {
        if (ctx.Trabajo is null) throw new InvalidOperationException("Sin ejercicio de trabajo no hay cuadre.");
        var saldo = ctx.SaldosTrabajo.GetValueOrDefault(cuenta).Saldo;
        return new CuadreCuenta(ctx.Trabajo.IdSociedadEjercicio, cuenta, cuadrada, cuadrada ? Math.Round(saldo, 2) : null,
            string.IsNullOrWhiteSpace(notas) ? null : notas.TrimEnd(), operador, ahora);
    }

    /// <summary>
    /// «Reiniciar el cuadre» del ejercicio de trabajo (el «Reinicia cuadre del Mayor» del antiguo, con el umbral parametrizado):
    /// con <paramref name="desmarcarResto"/> se quita antes la casilla de todas las cuentas (las notas se conservan); después se
    /// marcan como cuadradas las subcuentas con movimientos y saldo de hasta ±<paramref name="umbral"/>, con sus notas (o las
    /// heredadas). Devuelve los cuadres que hay que guardar.
    /// </summary>
    public static IReadOnlyList<CuadreCuenta> Reiniciar(ContextoCuadre ctx, IReadOnlyCollection<string> subcuentas, decimal umbral, bool desmarcarResto,
        string operador, DateTime ahora)
    {
        if (ctx.Trabajo is null) return [];
        umbral = Math.Abs(umbral);
        var cambios = new Dictionary<string, CuadreCuenta>(StringComparer.Ordinal);
        if (desmarcarResto)
            foreach (var c in ctx.Cuadres.Where(c => c.IdSociedadEjercicio == ctx.Trabajo.IdSociedadEjercicio && c.Cuadrada))
                cambios[c.Cuenta] = c with { Cuadrada = false, Saldo = null, Operador = operador, FechaHora = ahora };
        foreach (var cuenta in subcuentas)
        {
            var (saldo, apuntes) = ctx.SaldosTrabajo.GetValueOrDefault(cuenta);
            if (apuntes == 0 || Math.Abs(Math.Round(saldo, 2)) > umbral) continue;
            var v = Ver(cuenta, ctx);
            if (v.Estado == EstadoCuadre.Cuadrada && !desmarcarResto) continue;
            cambios[cuenta] = new CuadreCuenta(ctx.Trabajo.IdSociedadEjercicio, cuenta, true, Math.Round(saldo, 2), v.Notas, operador, ahora);
        }
        return cambios.Values.ToList();
    }
}
