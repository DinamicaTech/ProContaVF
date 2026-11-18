using System.Globalization;
using ProConta.Contabilidad.Impuestos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Una casilla (o un registro, en las informativas) del detalle de un periodo.</summary>
public sealed record CasillaModelo(string Codigo, string Concepto, string Importe, bool Total = false);

/// <summary>Detalle de un periodo: sus casillas o registros, lo que impide presentarlo, los avisos y una nota.</summary>
public sealed record DetalleModelo(string Titulo, IReadOnlyList<CasillaModelo> Casillas, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos, string? Nota = null);

/// <summary>
/// La declaración de un periodo lista para validar, guardar o presentar: la de presentación directa (<see cref="DeclaracionAeat"/>) o la
/// informativa de TGVI Online (<see cref="DeclaracionInformativa"/>), y lo que el propio modelo impide antes de presentar.
/// </summary>
public sealed record DeclaracionGenerada(DeclaracionAeat? Directa, DeclaracionInformativa? Informativa, IReadOnlyList<string> BloqueosPropios)
{
    public string Modelo => Directa?.Modelo ?? Informativa!.Modelo;
    public int Ejercicio => Directa?.Ejercicio ?? Informativa!.Ejercicio;
    public string Periodo => Directa?.Periodo ?? Informativa!.Periodo;
    public string Titulo => Directa?.Titulo ?? Informativa!.Titulo;

    /// <summary>El fichero con el diseño de registro: el de la presentación directa o los registros de la informativa, uno por línea.</summary>
    public string Fichero => Directa?.Fichero ?? string.Join("\r\n", new[] { Informativa!.RegistroTipo1 }.Concat(Informativa.RegistrosTipo2)) + "\r\n";

    /// <summary>Nombre del fichero: «303 2026 1T.txt».</summary>
    public string NombreFichero => $"{Modelo} {Ejercicio} {Periodo}.txt";
}

/// <summary>
/// Contrato común de los modelos para la pantalla Contabilidad › Modelos tributarios (contabilidad/modelos-tributarios): sus periodos
/// del ejercicio con el estado, la declaración de un periodo (para el borrador, el fichero o la presentación) y su detalle de casillas.
/// Cada modelo lo cumple con su servicio de siempre (<see cref="Servicio303"/>, <see cref="ServicioRetenciones"/>…).
/// </summary>
public interface IModeloTributario
{
    DefinicionModelo Definicion { get; }
    Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default);
    Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default);
    Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default);
}

/// <summary>Piezas comunes de los adaptadores.</summary>
internal static class Adaptador
{
    public static int Numero(string periodo) => periodo.Length >= 1 && char.IsAsciiDigit(periodo[0]) ? periodo[0] - '0' : 0;

    public static PeriodoModelo Periodo(string modelo, int ejercicio, string periodo, Estado303 estado, decimal? calculado, PresentacionGuardada? ultima,
        IReadOnlyList<PresentacionGuardada> presentaciones, IReadOnlyList<string> bloqueos, IReadOnlyList<string> avisos, string? soloConsulta = null, string? texto = null)
    {
        var (desde, hasta) = PlazosModelos.Plazo(modelo, ejercicio, periodo);
        return new PeriodoModelo(modelo, ejercicio, periodo, desde, hasta, estado, calculado, texto, ultima, presentaciones, bloqueos, avisos, soloConsulta);
    }

    public static PeriodoModelo De(PeriodoInformativa p, int ejercicio, string modelo) =>
        Periodo(modelo, ejercicio, p.Periodo, p.Estado, p.Total, p.Ultima, p.Presentaciones, p.Bloqueos, p.Avisos, texto: $"{p.Registros} registro{(p.Registros == 1 ? "" : "s")}");

    public static Resultado<DeclaracionGenerada> Directa<T>(Resultado<(T, DeclaracionAeat Aeat)> r, IReadOnlyList<string>? propios = null) =>
        r.EsCorrecto ? Resultado<DeclaracionGenerada>.Correcto(new DeclaracionGenerada(r.Valor.Aeat, null, propios ?? [])) : Resultado<DeclaracionGenerada>.Fallo(r.Errores);

    public static Resultado<DeclaracionGenerada> Informativa<T>(Resultado<(T, DeclaracionInformativa Tgvi)> r) =>
        r.EsCorrecto ? Resultado<DeclaracionGenerada>.Correcto(new DeclaracionGenerada(null, r.Valor.Tgvi, [])) : Resultado<DeclaracionGenerada>.Fallo(r.Errores);

    public static string Euros(decimal v) => PeriodoModelo.Euros(v);

    public static IReadOnlyList<CasillaModelo> Casillas(IReadOnlyList<(int Casilla, string Concepto)> conceptos, Func<int, decimal> valor, IReadOnlySet<int>? totales = null,
        Func<int, string>? formato = null) =>
        [.. conceptos.Where(c => valor(c.Casilla) != 0m || (totales?.Contains(c.Casilla) ?? false))
            .Select(c => new CasillaModelo(c.Casilla.ToString("00", CultureInfo.InvariantCulture), c.Concepto, formato?.Invoke(c.Casilla) ?? Euros(valor(c.Casilla)), totales?.Contains(c.Casilla) ?? false))];

    public static DetalleModelo Error(string titulo, Error e) => new(titulo, [], [e.TextoOperador()], []);
}

/// <summary>Modelo 303 (<see cref="Servicio303"/>).</summary>
public sealed class Modelo303Tributario(Servicio303 s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("303");

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) =>
        [.. (await s.TrimestresAsync(ctx, c)).Select(t => Adaptador.Periodo("303", ctx.Ejercicio, t.Periodo, t.Estado, t.ResultadoCalculado, t.Ultima, t.Presentaciones, t.Bloqueos, t.Avisos))];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default)
    {
        var r = await s.DeclaracionAsync(ctx, Adaptador.Numero(periodo), complementaria, nrc, c);
        return Adaptador.Directa(r, r.EsCorrecto ? await s.BloqueosAsync(ctx, r.Valor.Declaracion, c) : null);
    }

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var titulo = $"Casillas del 303 del {periodo} de {ctx.Ejercicio}";
        var t = (await s.TrimestresAsync(ctx, c)).First(x => x.Periodo == periodo);
        if (!t.Terminado) return new DetalleModelo(titulo, [], t.Bloqueos, t.Avisos, $"El {periodo} aún no ha terminado: sus casillas se ven a partir del {t.Fin.AddDays(1):dd/MM/yyyy}.");
        var r = await s.DeclaracionAsync(ctx, t.Trimestre, false, null, c);
        if (!r.EsCorrecto) return Adaptador.Error(titulo, r.Error!);
        var d = r.Valor.Declaracion;
        return new DetalleModelo(titulo,
            Adaptador.Casillas(Modelo303.Conceptos, d.Casilla, new HashSet<int> { 27, 45, 46, 69, 71 }, n => n == 65 ? $"{d.Casilla(65):0.00} %" : Adaptador.Euros(d.Casilla(n))),
            t.Bloqueos, t.Avisos, $"Forma de pago: {r.Valor.Aeat.TextoFormaPago}. Resultado de la autoliquidación: {Adaptador.Euros(t.ResultadoAutoliquidacion)}.");
    }
}

/// <summary>Modelos 111 y 115 (<see cref="ServicioRetenciones"/>).</summary>
public sealed class RetencionesTributario(ServicioRetenciones s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De(s.Modelo);

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) =>
        [.. (await s.TrimestresAsync(ctx, c)).Select(t => Adaptador.Periodo(s.Modelo, ctx.Ejercicio, t.Periodo, t.Estado, t.TotalCalculado, t.Ultima, t.Presentaciones, t.Bloqueos, t.Avisos))];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default)
    {
        var r = await s.DeclaracionAsync(ctx, Adaptador.Numero(periodo), complementaria, nrc, c);
        return Adaptador.Directa(r, r.EsCorrecto ? await s.BloqueosAsync(ctx, r.Valor.Declaracion, c) : null);
    }

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var titulo = $"Casillas del {s.Modelo} del {periodo} de {ctx.Ejercicio}";
        var t = (await s.TrimestresAsync(ctx, c)).First(x => x.Periodo == periodo);
        var r = await s.DeclaracionAsync(ctx, t.Trimestre, false, null, c);
        if (!r.EsCorrecto) return Adaptador.Error(titulo, r.Error!);
        return new DetalleModelo(titulo, Adaptador.Casillas(s.Conceptos, r.Valor.Declaracion.Casilla), t.Bloqueos, t.Avisos, $"Forma de pago: {r.Valor.Aeat.TextoFormaPago}.");
    }
}

/// <summary>Modelo 123 (<see cref="Servicio123"/>).</summary>
public sealed class Modelo123Tributario(Servicio123 s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("123");

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) =>
        [.. (await s.TrimestresAsync(ctx, c)).Select(t => Adaptador.Periodo("123", ctx.Ejercicio, t.Periodo, t.Estado, t.TotalCalculado, t.Ultima, t.Presentaciones, t.Bloqueos, t.Avisos))];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        Adaptador.Directa(await s.DeclaracionAsync(ctx, Adaptador.Numero(periodo), complementaria, nrc, c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var titulo = $"Casillas del 123 del {periodo} de {ctx.Ejercicio}";
        var t = (await s.TrimestresAsync(ctx, c)).First(x => x.Periodo == periodo);
        var r = await s.DeclaracionAsync(ctx, t.Trimestre, false, null, c);
        if (!r.EsCorrecto) return Adaptador.Error(titulo, r.Error!);
        return new DetalleModelo(titulo, Adaptador.Casillas(s.Conceptos, r.Valor.Declaracion.Casilla, formato: n => s.CasillasPerceptores.Contains(n)
            ? r.Valor.Declaracion.Casilla(n).ToString("0", CultureInfo.InvariantCulture) : Adaptador.Euros(r.Valor.Declaracion.Casilla(n))), t.Bloqueos, t.Avisos);
    }
}

/// <summary>Modelo 202 (<see cref="Servicio202"/>).</summary>
public sealed class Modelo202Tributario(Servicio202 s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("202");

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) =>
        [.. (await s.PeriodosAsync(ctx, null, c)).Select(p => Adaptador.Periodo("202", ctx.Ejercicio, p.Codigo, p.Estado, p.PagoCalculado, p.Ultima, p.Presentaciones, p.Bloqueos, p.Avisos))];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        Adaptador.Directa(await s.DeclaracionAsync(ctx, Adaptador.Numero(periodo), complementaria, null, nrc, c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var titulo = $"Casillas del 202 del {periodo} de {ctx.Ejercicio}";
        var p = (await s.PeriodosAsync(ctx, null, c)).First(x => x.Codigo == periodo);
        var r = await s.DeclaracionAsync(ctx, p.Periodo, false, null, null, c);
        if (!r.EsCorrecto) return new DetalleModelo(titulo, [], [r.Error!.TextoOperador(), .. p.Bloqueos], p.Avisos);
        return new DetalleModelo(titulo, Adaptador.Casillas(s.Conceptos, r.Valor.Declaracion.Casilla), p.Bloqueos, p.Avisos,
            string.Join(" · ", r.Valor.Aeat.Resumen.Select(x => $"{x.Etiqueta}: {x.Valor}")));
    }
}

/// <summary>Modelo 390 (<see cref="Servicio390"/>): solo consulta antes de 2025 (el programa tiene los diseños de 2025 y 2026).</summary>
public sealed class Modelo390Tributario(Servicio390 s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("390");

    public static string? SoloConsulta(int ejercicio) => ejercicio < 2025
        ? $"El programa tiene los diseños del 390 de 2025 y 2026; el de {ejercicio} no lo genera (se ve lo presentado y su calendario)." : null;

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var e = await s.EjercicioAsync(ctx, null, c);
        return [Adaptador.Periodo("390", ctx.Ejercicio, "0A", e.Estado, e.ResultadoCalculado, e.Ultima, e.Presentaciones, e.Bloqueos, e.Avisos, SoloConsulta(ctx.Ejercicio))];
    }

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        Adaptador.Directa(await s.DeclaracionAsync(ctx, null, complementaria, c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var titulo = $"Casillas del 390 de {ctx.Ejercicio}";
        var e = await s.EjercicioAsync(ctx, null, c);
        var r = await s.DeclaracionAsync(ctx, null, false, c);
        if (!r.EsCorrecto) return new DetalleModelo(titulo, [], [r.Error!.TextoOperador(), .. e.Bloqueos], e.Avisos);
        return new DetalleModelo(titulo, Adaptador.Casillas(s.Conceptos, r.Valor.Declaracion.Casilla), e.Bloqueos, e.Avisos);
    }
}

/// <summary>Modelo 200 (<see cref="Servicio200"/>): solo el ejercicio 2025, el del diseño que tiene el programa; se calcula cuando ha terminado el ejercicio.</summary>
public sealed class Modelo200Tributario(Servicio200 s, ServicioPresentacionAeat presentacion, DateTime hoy) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("200");

    public static string? SoloConsulta(int ejercicio) => ejercicio != 2025
        ? $"El programa tiene el diseño del 200 de 2025; el de {ejercicio} lo publica la AEAT en {ejercicio + 1} y hay que añadirlo antes de generarlo." : null;

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var entorno = await presentacion.EntornoAsync(c);
        var del = (await presentacion.PresentadasAsync(ctx.IdSociedad, "200", ctx.Ejercicio, c)).Where(p => p.Entorno == ServiciosAeat.Codigo(entorno) && p.Periodo == "0A").ToList();
        var ultima = del.LastOrDefault();
        var solo = SoloConsulta(ctx.Ejercicio);
        decimal? calculado = null;
        var bloqueos = new List<string>();
        var avisos = new List<string>();
        var terminado = hoy.Date > new DateTime(ctx.Ejercicio, 12, 31);
        if (solo is null && terminado)
        {
            try
            {
                var r = await s.DeclaracionAsync(ctx, c: c);
                if (r.EsCorrecto) { calculado = r.Valor.Declaracion.Resultado; bloqueos.AddRange(r.Valor.Declaracion.Errores); avisos.AddRange(r.Valor.Declaracion.Avisos); }
                else bloqueos.Add(r.Error!.TextoOperador());
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { bloqueos.Add($"No he podido calcular el 200: {ex.Message}"); }
        }
        var estado = !terminado ? Estado303.EnCurso : ultima is null ? Estado303.SinPresentar
            : calculado is decimal v && ultima.DistintoDe(v) ? Estado303.Desactualizado : Estado303.Presentado;
        return [Adaptador.Periodo("200", ctx.Ejercicio, "0A", estado, calculado, ultima, del, bloqueos, avisos, solo)];
    }

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        Adaptador.Directa(await s.DeclaracionAsync(ctx, c: c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var titulo = $"Casillas del 200 de {ctx.Ejercicio}";
        var r = await s.DeclaracionAsync(ctx, c: c);
        if (!r.EsCorrecto) return Adaptador.Error(titulo, r.Error!);
        var d = r.Valor.Declaracion;
        var casillas = d.Casillas.OrderBy(x => x.Key, StringComparer.Ordinal)
            .SelectMany(p => p.Value.Where(x => x.Value != 0m).OrderBy(x => x.Key).Select(x => new CasillaModelo(x.Key.ToString("00000", CultureInfo.InvariantCulture), $"Página {p.Key[..2]}{(p.Key[2] == '0' ? "" : " " + p.Key[2..3] + " bis")}", Adaptador.Euros(x.Value))))
            .ToList();
        return new DetalleModelo(titulo, casillas, d.Errores, d.Avisos, $"Resultado: {Adaptador.Euros(d.Resultado)}. Tipo de declaración {d.TipoDeclaracion}.");
    }
}

/// <summary>Modelo 349 (<see cref="Servicio349"/>).</summary>
public sealed class Modelo349Tributario(Servicio349 s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("349");

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) =>
        [.. (await s.TrimestresAsync(ctx, c)).Select(p => Adaptador.De(p, ctx.Ejercicio, "349"))];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        Adaptador.Informativa(await s.DeclaracionAsync(ctx, Adaptador.Numero(periodo), complementaria, null, c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var d = await s.CalcularAsync(ctx, Adaptador.Numero(periodo), c);
        return new DetalleModelo($"Operadores del 349 del {periodo} de {ctx.Ejercicio}",
            [.. d.Operadores.Select(o => new CasillaModelo($"{o.Pais} {o.Numero}", $"{o.Nombre} · clave {o.Clave}", Adaptador.Euros(o.Base))),
                new CasillaModelo("", "Total", Adaptador.Euros(d.Total), true)], d.Errores, d.Avisos);
    }
}

/// <summary>Modelo 347 (<see cref="Servicio347"/>).</summary>
public sealed class Modelo347Tributario(Servicio347 s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("347");

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) => [Adaptador.De(await s.EjercicioAsync(ctx, c), ctx.Ejercicio, "347")];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        Adaptador.Informativa(await s.DeclaracionAsync(ctx, complementaria, null, c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var d = await s.CalcularAsync(ctx, c);
        var filas = d.Declarados.Select(x => new CasillaModelo(x.Nif, $"{x.Nombre} · clave {x.Clave}{(x.ArrendamientoLocal ? " · alquiler de local" : "")}", Adaptador.Euros(x.Total))).ToList();
        filas.AddRange((d.Inmuebles ?? []).Select(a => new CasillaModelo(a.Inmueble.ReferenciaCatastral ?? "", $"Inmueble {a.Inmueble.Activo} · {a.Nombre}", Adaptador.Euros(a.Total))));
        filas.Add(new CasillaModelo("", "Total", Adaptador.Euros(d.Total), true));
        return new DetalleModelo($"Declarados del 347 de {ctx.Ejercicio}", filas, d.Errores, d.Avisos);
    }
}

/// <summary>Modelo 193 (<see cref="Servicio193"/>).</summary>
public sealed class Modelo193Tributario(Servicio193 s) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De("193");

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) => [Adaptador.De(await s.EjercicioAsync(ctx, c), ctx.Ejercicio, "193")];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        Adaptador.Informativa(await s.DeclaracionAsync(ctx, complementaria, null, c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        var d = await s.CalcularAsync(ctx, c);
        return new DetalleModelo($"Perceptores del 193 de {ctx.Ejercicio}",
            [.. d.Perceptores.Select(p => new CasillaModelo(p.Socio.Nif, $"{p.Socio.Nombre} · base {Adaptador.Euros(p.Base)} al {p.Porcentaje:0.##} %", Adaptador.Euros(p.Retencion))),
                new CasillaModelo("", "Total retenido", Adaptador.Euros(d.TotalRetenciones), true)], d.Errores, d.Avisos);
    }
}

/// <summary>Modelos 190 y 180 (<see cref="ServicioResumenRetenciones"/>).</summary>
public sealed class ResumenRetencionesTributario(ServicioResumenRetenciones s, string modelo) : IModeloTributario
{
    public DefinicionModelo Definicion { get; } = DefinicionModelo.De(modelo);

    public async Task<IReadOnlyList<PeriodoModelo>> PeriodosAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) =>
        [Adaptador.De(modelo == "190" ? await s.Ejercicio190Async(ctx, null, c) : await s.Ejercicio180Async(ctx, c), ctx.Ejercicio, modelo)];

    public async Task<Resultado<DeclaracionGenerada>> GenerarAsync(ContextoSociedadEjercicio ctx, string periodo, bool complementaria, string? nrc = null, CancellationToken c = default) =>
        modelo == "190" ? Adaptador.Informativa(await s.Declaracion190Async(ctx, complementaria, null, null, c)) : Adaptador.Informativa(await s.Declaracion180Async(ctx, complementaria, null, c));

    public async Task<DetalleModelo> DetalleAsync(ContextoSociedadEjercicio ctx, string periodo, CancellationToken c = default)
    {
        if (modelo == "190")
        {
            var d = await s.Calcular190Async(ctx, null, c);
            return new DetalleModelo($"Perceptores del 190 de {ctx.Ejercicio}",
                [.. d.Percepciones.Select(p => new CasillaModelo(p.Perceptor.Nif, $"{p.Perceptor.Nombre} · clave {p.Clave}{p.Subclave.Trim()} · percibido {Adaptador.Euros(p.Dinerario + p.Especie)}", Adaptador.Euros(p.Total))),
                    new CasillaModelo("", "Total retenido", Adaptador.Euros(d.TotalRetenciones), true)], d.Errores, d.Avisos);
        }
        var a = await s.Calcular180Async(ctx, c);
        return new DetalleModelo($"Arrendadores del 180 de {ctx.Ejercicio}",
            [.. a.Arrendadores.Select(x => new CasillaModelo(x.Arrendador.Nif, $"{x.Arrendador.Nombre} · base {Adaptador.Euros(x.Base)}{(x.Inmueble is { } i ? $" · {i.Activo}" : "")}", Adaptador.Euros(x.Retenciones))),
                new CasillaModelo("", "Total retenido", Adaptador.Euros(a.TotalRetenciones), true)], a.Errores, a.Avisos);
    }
}

/// <summary>Resultado de «Consultar a la AEAT»: lo que se ha añadido y lo que no se ha podido consultar.</summary>
public sealed record ConsultaModelosAeat(IReadOnlyList<PresentacionGuardada> Nuevas, IReadOnlyList<string> Errores, IReadOnlyList<string> SinConsulta);

/// <summary>
/// Servicio de la pantalla Contabilidad › Modelos tributarios (contabilidad/modelos-tributarios): los modelos con su contrato común,
/// los modelos de cada sociedad («Modelos de la sociedad…») y la consulta a la AEAT de lo presentado fuera del programa.
/// </summary>
public sealed class ServicioModelosTributarios
{
    private readonly ProConta.Datos.Persistencia.IBaseDatos _bd;
    private readonly IReloj _reloj;

    public ServicioModelosTributarios(ProConta.Datos.Persistencia.IBaseDatos bd, ServicioPresentacionAeat presentacion, ServicioTgvi tgvi, ServicioImpuestoSociedades impuesto,
        IReloj? reloj = null)
    {
        _bd = bd;
        _reloj = reloj ?? RelojSistema.Instancia;
        Presentacion = presentacion;
        Tgvi = tgvi;
        var s303 = new Servicio303(bd, presentacion, reloj);
        var resumen = new ServicioResumenRetenciones(bd, tgvi, reloj);
        Modelos =
        [
            new Modelo303Tributario(s303),
            new RetencionesTributario(ServicioRetenciones.Modelo111(bd, presentacion, reloj)),
            new RetencionesTributario(ServicioRetenciones.Modelo115(bd, presentacion, reloj)),
            new Modelo123Tributario(new Servicio123(bd, presentacion, reloj)),
            new Modelo349Tributario(new Servicio349(bd, tgvi, reloj)),
            new Modelo202Tributario(new Servicio202(bd, presentacion, reloj)),
            new Modelo390Tributario(new Servicio390(bd, presentacion, reloj)),
            new ResumenRetencionesTributario(resumen, "190"),
            new ResumenRetencionesTributario(resumen, "180"),
            new Modelo193Tributario(new Servicio193(bd, tgvi, reloj)),
            new Modelo347Tributario(new Servicio347(bd, tgvi, reloj)),
            new Modelo200Tributario(new Servicio200(bd, presentacion, impuesto), presentacion, _reloj.Ahora),
        ];
    }

    public ServicioPresentacionAeat Presentacion { get; }
    public ServicioTgvi Tgvi { get; }
    public IReadOnlyList<IModeloTributario> Modelos { get; }
    public DateTime Hoy => _reloj.Ahora.Date;

    public IModeloTributario Modelo(string id) => Modelos.First(m => m.Definicion.Id == id);

    /// <summary>
    /// Los modelos elegidos en «Modelos de la sociedad…» (null si la sociedad aún no ha elegido: entonces salen los que tienen datos o
    /// presentaciones, <see cref="PorDefecto"/>).
    /// </summary>
    public Task<IReadOnlySet<string>?> ElegidosAsync(string idSociedad) => ElegidosAsync(_bd, idSociedad);

    public static Task<IReadOnlySet<string>?> ElegidosAsync(ProConta.Datos.Persistencia.IBaseDatos bd, string idSociedad) => bd.EjecutarAsync<IReadOnlySet<string>?>(async s =>
    {
        if (!s.LeerEsquema().ExisteTabla("ModelosSociedad")) return null;
        var filas = await s.ConsultarAsync("SELECT Modelo, Visible FROM ModelosSociedad WHERE IdSociedad = ?", f => (Modelo: (f.Texto("Modelo") ?? "").Trim(), Visible: f.Logico("Visible")), idSociedad);
        return filas.Count == 0 ? null : filas.Where(x => x.Visible).Select(x => x.Modelo).ToHashSet();
    });

    /// <summary>Guarda qué modelos presenta la sociedad: una fila por modelo del catálogo, visible o no.</summary>
    public Task GuardarElegidosAsync(string idSociedad, IReadOnlyCollection<string> visibles) => _bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("DELETE FROM ModelosSociedad WHERE IdSociedad = ?", idSociedad);
        foreach (var m in DefinicionModelo.Todos)
            await s.EjecutarAsync("INSERT INTO ModelosSociedad (IdSociedad, Modelo, Visible) VALUES (?, ?, ?)", idSociedad, m.Id, visibles.Contains(m.Id));
        return true;
    });

    /// <summary>Modelos que salen sin haber elegido: los que tienen algo calculado o alguna presentación en el ejercicio.</summary>
    public static IReadOnlySet<string> PorDefecto(IReadOnlyDictionary<string, IReadOnlyList<PeriodoModelo>> periodos) =>
        periodos.Where(kv => kv.Value.Any(p => p.Presentaciones.Count > 0 || (p.Calculado ?? 0m) != 0m)).Select(kv => kv.Key).ToHashSet();

    /// <summary>
    /// Modelos para los plazos y avisos de Inicio, que no calcula los modelos: los elegidos o, sin elegir, el 303 y el 390 (los de
    /// siempre) más los que tienen alguna presentación guardada de la sociedad.
    /// </summary>
    public static async Task<IReadOnlySet<string>> ParaInicioAsync(ProConta.Datos.Persistencia.IBaseDatos bd, string idSociedad)
    {
        if (await ElegidosAsync(bd, idSociedad) is { } elegidos) return elegidos;
        return await bd.EjecutarAsync<IReadOnlySet<string>>(async s =>
        {
            var r = new HashSet<string> { "303", "390" };
            if (s.LeerEsquema().ExisteTabla("PresentacionesAeat"))
                foreach (var m in await s.ConsultarAsync("SELECT DISTINCT Modelo FROM PresentacionesAeat WHERE IdSociedad = ?", f => (f.Texto("Modelo") ?? "").Trim(), idSociedad))
                    r.Add(m);
            return r;
        });
    }

    /// <summary>Cuándo se consultó por última vez a la AEAT lo presentado (null si nunca).</summary>
    public Task<DateTime?> UltimaConsultaAsync(string idSociedad) => _bd.EjecutarAsync(async s =>
        s.LeerEsquema().ExisteColumna("Sociedades", "UltimaConsultaModelosAeat")
            ? await s.EscalarAsync("SELECT UltimaConsultaModelosAeat FROM Sociedades WHERE IdSociedad = ?", idSociedad) as DateTime?
            : null);

    /// <summary>Si toca la consulta automática del día al abrir la pantalla (una vez al día).</summary>
    public async Task<bool> TocaConsultaDelDiaAsync(string idSociedad) => await UltimaConsultaAsync(idSociedad) is not DateTime d || d.Date < Hoy;

    /// <summary>
    /// «Consultar a la AEAT» (ConsultaExt, presentación directa): lo presentado de cada modelo de <paramref name="modelos"/> en el
    /// ejercicio, también fuera del programa; lo que no está en PresentacionesAeat (mismo justificante o mismo CSV) se guarda con origen
    /// AEAT, sin importe ni fichero. Las informativas de TGVI Online no tienen consulta: van en <see cref="ConsultaModelosAeat.SinConsulta"/>.
    /// </summary>
    public async Task<ConsultaModelosAeat> ConsultarAeatAsync(ContextoSociedadEjercicio ctx, IEnumerable<string> modelos, CancellationToken c = default)
    {
        var nuevas = new List<PresentacionGuardada>();
        var errores = new List<string>();
        var sin = new List<string>();
        foreach (var m in modelos.Distinct())
        {
            var def = DefinicionModelo.De(m);
            if (def.Informativa) { sin.Add(m); continue; }
            var r = await Presentacion.ConsultarPresentadasAsync(ctx.IdSociedad, m, ctx.Ejercicio, c);
            if (!r.EsCorrecto) { errores.Add($"{m}: {r.Error!.TextoOperador()}"); if (r.Error.Codigo == "Presentacion.SinCertificado") break; continue; }
            var guardadas = await Presentacion.PresentadasAsync(ctx.IdSociedad, m, ctx.Ejercicio, c);
            foreach (var p in Nuevas(r.Valor.Presentadas, guardadas, ctx.Ejercicio, m))
                nuevas.Add(await Presentacion.GuardarExternaAsync(ctx.IdSociedad, p, r.Valor.Entorno, c));
        }
        if (errores.Count == 0 || nuevas.Count > 0)
            await _bd.EjecutarAsync(s => s.LeerEsquema().ExisteColumna("Sociedades", "UltimaConsultaModelosAeat")
                ? s.EjecutarAsync("UPDATE Sociedades SET UltimaConsultaModelosAeat = ? WHERE IdSociedad = ?", _reloj.Ahora, ctx.IdSociedad) : Task.FromResult(0));
        return new ConsultaModelosAeat(nuevas, errores, sin);
    }

    /// <summary>
    /// Lo que devuelve la AEAT y no está guardado: del modelo y ejercicio pedidos, sin las que ya están (mismo justificante o mismo CSV,
    /// del entorno que sea) ni las repetidas en la propia respuesta.
    /// </summary>
    public static IReadOnlyList<DeclaracionPresentadaAeat> Nuevas(IEnumerable<DeclaracionPresentadaAeat> aeat, IEnumerable<PresentacionGuardada> guardadas, int ejercicio, string modelo)
    {
        var justificantes = new HashSet<string>(guardadas.Select(g => g.Justificante.Trim()).Where(j => j.Length > 0), StringComparer.OrdinalIgnoreCase);
        var csvs = new HashSet<string>(guardadas.Select(g => g.Csv.Trim()).Where(x => x.Length > 0), StringComparer.OrdinalIgnoreCase);
        var r = new List<DeclaracionPresentadaAeat>();
        foreach (var p in aeat)
        {
            if (p.Modelo.Trim() != modelo || p.Ejercicio.Trim() != ejercicio.ToString(CultureInfo.InvariantCulture)) continue;
            if (p.Justificante.Trim().Length == 0 && p.Csv.Trim().Length == 0) continue;
            if (justificantes.Contains(p.Justificante.Trim()) || csvs.Contains(p.Csv.Trim())) continue;
            r.Add(p);
            if (p.Justificante.Trim().Length > 0) justificantes.Add(p.Justificante.Trim());
            if (p.Csv.Trim().Length > 0) csvs.Add(p.Csv.Trim());
        }
        return r;
    }
}
