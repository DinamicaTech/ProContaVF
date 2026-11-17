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
    /// «Reiniciar el cuadre» del ejercicio de trabajo (contabilidad/procesos/reiniciar-cuadre; el «Reinicia cuadre del Mayor» del
    /// antiguo, con el umbral parametrizado): las subcuentas con movimientos y saldo de hasta ±<paramref name="umbral"/> quedan
    /// marcadas como cuadradas con su saldo actual, y todas las demás quedan sin la casilla, también las cuadradas a mano con saldo.
    /// Las notas nunca se tocan (las heredadas del ejercicio anterior se copian al de trabajo al marcarla). Solo devuelve lo que
    /// cambia: una cuenta ya cuadrada con el mismo saldo y dentro del umbral se queda como está.
    /// </summary>
    public static PropuestaReinicioCuadre ProponerReinicio(ContextoCuadre ctx, IReadOnlyCollection<string> subcuentas, Func<string, string> nombre,
        decimal umbral, string operador, DateTime ahora)
    {
        umbral = Math.Abs(umbral);
        if (ctx.Trabajo is null) return new PropuestaReinicioCuadre(null, umbral, [], 0);
        var trabajo = ctx.Trabajo.IdSociedadEjercicio;
        var cambios = new Dictionary<string, CambioReinicioCuadre>(StringComparer.Ordinal);
        var siguen = 0;
        foreach (var cuenta in subcuentas.Union(ctx.Cuadres.Where(c => c.IdSociedadEjercicio == trabajo && c.Cuadrada).Select(c => c.Cuenta), StringComparer.Ordinal))
        {
            var (saldo, apuntes) = ctx.SaldosTrabajo.GetValueOrDefault(cuenta);
            saldo = Math.Round(saldo, 2);
            var dentro = apuntes > 0 && Math.Abs(saldo) <= umbral;
            var v = Ver(cuenta, ctx);
            var marcada = v.Registro is { Cuadrada: true };
            if (dentro && v.Estado == EstadoCuadre.Cuadrada) { siguen++; continue; }
            if (!dentro && !marcada) continue;
            var nuevo = dentro
                ? new CuadreCuenta(trabajo, cuenta, true, saldo, v.Notas, operador, ahora)
                : v.Registro! with { Cuadrada = false, Saldo = null, Operador = operador, FechaHora = ahora };
            cambios[cuenta] = new CambioReinicioCuadre(cuenta, nombre(cuenta), saldo, dentro, marcada, v.Notas is not null, nuevo);
        }
        return new PropuestaReinicioCuadre(ctx.Trabajo, umbral, cambios.Values.OrderBy(c => c.Cuenta, StringComparer.Ordinal).ToList(), siguen);
    }
}

/// <summary>Una subcuenta que cambia al reiniciar el cuadre: se marca como cuadrada (o se actualiza su saldo) o se le quita la casilla.</summary>
public sealed record CambioReinicioCuadre(string Cuenta, string Nombre, decimal Saldo, bool Marcar, bool EstabaMarcada, bool TieneNotas, CuadreCuenta Nuevo)
{
    public string Cambio => Marcar ? EstabaMarcada ? "Se actualiza" : "Se marca" : "Se desmarca";
}

/// <summary>Lo que hará «Reiniciar el cuadre» en el ejercicio de trabajo, para enseñarlo antes de grabar.</summary>
public sealed record PropuestaReinicioCuadre(EjercicioMayor? Trabajo, decimal Umbral, IReadOnlyList<CambioReinicioCuadre> Cambios, int SiguenCuadradas)
{
    public int SeMarcan => Cambios.Count(c => c.Marcar);
    public int SeDesmarcan => Cambios.Count(c => !c.Marcar);
}
