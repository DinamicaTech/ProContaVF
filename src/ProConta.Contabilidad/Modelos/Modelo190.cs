using System.Globalization;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Identificación de un perceptor para el 190 o el 180: NIF, nombre («apellido apellido nombre» o razón social) y provincia (2 primeras cifras de su código postal).</summary>
public sealed record PerceptorInformativa(string Nif, string Nombre, string? Provincia);

/// <summary>
/// Un registro de percepción del 190: perceptor, clave (A empleados, E administradores, G profesionales) y subclave, percepción dineraria,
/// retenciones, valoración en especie e ingresos a cuenta del año.
/// </summary>
public sealed record Percepcion190(PerceptorInformativa Perceptor, char Clave, string Subclave, decimal Dinerario, decimal Retenciones, decimal Especie, decimal IngresosCuenta)
{
    public decimal Total => Retenciones + IngresosCuenta;
}

public sealed record Declaracion190(int Ejercicio, Declarante303 Declarante, IReadOnlyList<Percepcion190> Percepciones, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos)
{
    public decimal TotalPercepciones => Percepciones.Sum(p => p.Dinerario + p.Especie);
    public decimal TotalRetenciones => Percepciones.Sum(p => p.Total);
    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Modelo 190, resumen anual de retenciones del trabajo y de actividades profesionales (contabilidad/modelos-tributarios/modelo-190), sin
/// BD, desde las mismas percepciones que el 111 (<see cref="ServicioRetenciones"/>): un registro por perceptor y clave con la suma del año.
/// Cuadra por construcción con la suma de los cuatro 111 (casilla 28). Empleados con clave A salvo los marcados como administradores
/// (clave E, subclave indicada); profesionales con clave G, subclave 01. Los datos personales que el 190 pide de los trabajadores (año de
/// nacimiento, situación familiar, descendientes…) no están en el programa: sin ellos no se ofrece presentar.
/// </summary>
public static class Modelo190
{
    public const string Modelo = "190";

    /// <param name="perceptor">Identificación de cada perceptor por su clave interna (E…, P…).</param>
    /// <param name="administradores">Perceptores (clave interna) que son administradores, con su subclave de la clave E.</param>
    public static Declaracion190 Calcular(IEnumerable<PercepcionRetencion> percepciones, int ejercicio, Declarante303 declarante, Func<string, PerceptorInformativa> perceptor,
        IReadOnlyDictionary<string, string> administradores)
    {
        var errores = new List<string>();
        var avisos = new List<string>();
        var lista = new List<Percepcion190>();
        foreach (var g in percepciones.Where(p => p.Clase != ClaseRetencionModelo.Arrendamientos).GroupBy(p => (p.Perceptor, p.Clase)))
        {
            var quien = perceptor(g.Key.Perceptor);
            var (clave, sub) = g.Key.Clase == ClaseRetencionModelo.Profesionales ? ('G', "01")
                : administradores.TryGetValue(g.Key.Perceptor, out var s) ? ('E', s) : ('A', "  ");
            var p = new Percepcion190(quien, clave, sub, g.Sum(x => x.Dinerario), g.Sum(x => x.Retencion), g.Sum(x => x.Especie), g.Sum(x => x.IngresoCuenta));
            if (p.Dinerario == 0m && p.Especie == 0m && p.Total == 0m) continue;
            lista.Add(p);
            if (!Modelo347.EsNifEspanol(quien.Nif)) errores.Add($"{quien.Nombre} no tiene un NIF español válido ({quien.Nif}): corrígelo en su ficha.");
            if (quien.Provincia is null) errores.Add($"{quien.Nombre} ({quien.Nif}) no tiene código postal en su ficha: el 190 pide su provincia.");
            if (clave is 'A' or 'E')
                errores.Add($"Faltan los datos personales de {quien.Nombre} que el 190 pide en la clave {clave} (año de nacimiento, situación familiar, discapacidad, descendientes y ascendientes): " +
                    "el programa aún no los guarda.");
            if (p.Dinerario < 0m || p.Especie < 0m || p.Total < 0m) errores.Add($"{quien.Nombre} tiene importes negativos en el año: revisa sus nóminas o facturas.");
        }
        if (string.IsNullOrWhiteSpace(declarante.Nif)) errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        return new Declaracion190(ejercicio, declarante, [.. lista.OrderBy(p => p.Clave).ThenBy(p => p.Perceptor.Nif, StringComparer.Ordinal)], errores, avisos);
    }

    /// <summary>
    /// Registros del 190 (diseño de la Orden del 190, sources/aeat/DR190e2025.pdf): tipo 1 con el número de percepciones, el total de
    /// percepciones y el de retenciones e ingresos a cuenta; tipo 2 por percepción (datos adicionales a cero: el programa no los tiene).
    /// </summary>
    public static (string Tipo1, IReadOnlyList<string> Tipo2) Registros(Declaracion190 d, ContactoInformativa contacto, string numero, string? anterior = null, bool sustitutiva = false)
    {
        var t1 = Tipo1Informativa.Base(Modelo, d.Ejercicio, d.Declarante, contacto, numero, anterior, sustitutiva)
            .Num(136, 144, d.Percepciones.Count).ImporteConSigno(145, 160, d.TotalPercepciones).Importe(161, 175, d.TotalRetenciones);
        var t2 = d.Percepciones.Select(p => new RegistroInformativo()
            .Texto(1, "2").Texto(2, Modelo).Num(5, 8, d.Ejercicio).Nif(9, d.Declarante.Nif).Nif(18, p.Perceptor.Nif).An(36, 75, p.Perceptor.Nombre)
            .Texto(76, (p.Perceptor.Provincia ?? "00").PadLeft(2, '0')[..2]).Texto(78, p.Clave.ToString()).Texto(79, p.Clave == 'A' ? "00" : p.Subclave)
            .ImporteConSigno(81, 94, p.Dinerario).Importe(95, 107, p.Retenciones)
            .ImporteConSigno(108, 121, p.Especie).Importe(122, 134, p.IngresosCuenta).Importe(135, 147, 0m)
            .Num(148, 151, 0).Num(152, 152, 0)
            .Num(153, 156, 0).Num(157, 157, 0).Num(167, 167, 0).Num(168, 168, p.Clave == 'A' ? 1 : 0).Num(169, 170, 0)
            .Importe(171, 183, 0m).Importe(184, 196, 0m).Importe(197, 209, 0m).Importe(210, 222, 0m).Num(223, 253, 0).Num(254, 254, 0)
            .ImporteConSigno(255, 268, 0m).Importe(269, 281, 0m).ImporteConSigno(282, 295, 0m).Importe(296, 308, 0m).Importe(309, 321, 0m)
            .Num(322, 322, 0).Importe(323, 387, 0m)
            .ToString()).ToList();
        return (t1.ToString(), t2);
    }
}

/// <summary>Un arrendador del 180: identificación, base, porcentaje de retención y retenciones del año.</summary>
public sealed record Arrendador180(PerceptorInformativa Arrendador, decimal Base, decimal Retenciones)
{
    public decimal Porcentaje => Base == 0m ? 0m : decimal.Round(Retenciones * 100m / Base, 2);
}

public sealed record Declaracion180(int Ejercicio, Declarante303 Declarante, IReadOnlyList<Arrendador180> Arrendadores, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos)
{
    public decimal TotalBase => Arrendadores.Sum(a => a.Base);
    public decimal TotalRetenciones => Arrendadores.Sum(a => a.Retenciones);
    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Modelo 180, resumen anual de retenciones de arrendamientos de inmuebles urbanos (contabilidad/modelos-tributarios/modelo-180), sin BD,
/// desde las mismas percepciones que el 115: un registro por arrendador con su base y sus retenciones del año. Cuadra por construcción
/// con la suma de los cuatro 115 (casilla 03). Los datos del inmueble (referencia catastral y dirección) que pide el 180 no están en las
/// facturas de alquiler: sin ellos no se ofrece presentar.
/// </summary>
public static class Modelo180
{
    public const string Modelo = "180";

    public static Declaracion180 Calcular(IEnumerable<PercepcionRetencion> percepciones, int ejercicio, Declarante303 declarante, Func<string, PerceptorInformativa> perceptor)
    {
        var errores = new List<string>();
        var lista = new List<Arrendador180>();
        foreach (var g in percepciones.Where(p => p.Clase == ClaseRetencionModelo.Arrendamientos).GroupBy(p => p.Perceptor))
        {
            var quien = perceptor(g.Key);
            var a = new Arrendador180(quien, g.Sum(x => x.Dinerario), g.Sum(x => x.Retencion));
            if (a.Base == 0m && a.Retenciones == 0m) continue;
            lista.Add(a);
            if (!Modelo347.EsNifEspanol(quien.Nif)) errores.Add($"{quien.Nombre} no tiene un NIF español válido ({quien.Nif}): corrígelo en su ficha.");
            if (quien.Provincia is null) errores.Add($"{quien.Nombre} ({quien.Nif}) no tiene código postal en su ficha: el 180 pide su provincia.");
            errores.Add($"Faltan los datos del inmueble que alquila {quien.Nombre} (situación, referencia catastral y dirección) que pide el 180: el programa aún no los relaciona con las facturas de alquiler.");
            if (a.Base < 0m || a.Retenciones < 0m) errores.Add($"{quien.Nombre} tiene importes negativos en el año: revisa sus facturas de alquiler.");
        }
        if (string.IsNullOrWhiteSpace(declarante.Nif)) errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        return new Declaracion180(ejercicio, declarante, [.. lista.OrderBy(a => a.Arrendador.Nif, StringComparer.Ordinal)], errores, []);
    }
}

/// <summary>
/// Modelos 190 y 180 de cada ejercicio con el contrato de los demás servicios de modelos: el estado del año (enero del siguiente) y la
/// declaración calculada desde las percepciones del 111 y del 115 (<see cref="ServicioRetenciones"/>), con la identificación de cada
/// perceptor de su ficha de tercero. El 190 se puede generar para TGVI Online; el 180 se queda en el cálculo hasta que los inmuebles
/// estén ligados a las facturas de alquiler.
/// </summary>
public sealed class ServicioResumenRetenciones(IBaseDatos bd, ServicioTgvi tgvi, IReloj? reloj = null) : ServicioInformativaIva(bd, tgvi, reloj)
{
    public static (DateTime Desde, DateTime Hasta) Plazo(int ejercicio) => (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 31));

    private async Task<(LecturaRetenciones Lectura, Func<string, PerceptorInformativa> Perceptor)> LeerAsync(ContextoSociedadEjercicio ctx, CancellationToken c)
    {
        var lectura = await ServicioRetenciones.Modelo111(Bd, null!).LeerAsync(ctx, c);
        var (empleados, proveedores, nominas) = await Bd.EjecutarAsync(async s =>
        {
            var esquema = s.LeerEsquema();
            static PerceptorInformativa Desde(IFilaDatos f)
            {
                var juridica = string.Equals(f.Texto("TipoPersona")?.Trim(), "JURIDICA", StringComparison.OrdinalIgnoreCase);
                var nombre = juridica ? f.Texto("RazonSocial") ?? f.Texto("Nombre")
                    : string.Join(' ', new[] { f.Texto("PrimerApellido"), f.Texto("SegundoApellido"), f.Texto("NombrePersona") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())) is { Length: > 0 } n
                        ? n : f.Texto("Nombre") ?? "";
                var cp = f.Texto("CodigoPostal")?.Trim();
                return new PerceptorInformativa(Modelo347.Nif(f.Texto("NIF")), nombre.Trim(), cp is { Length: >= 2 } && cp[..2].All(char.IsAsciiDigit) ? cp[..2] : null);
            }
            const string campos = "t.NIF, t.TipoPersona, t.RazonSocial, t.Nombre, t.PrimerApellido, t.SegundoApellido, t.NombrePersona, t.CodigoPostal";
            var emp = esquema.ExisteTabla("Empleados")
                ? (await s.ConsultarAsync($"SELECT e.IdEmpleado, {campos} FROM Empleados AS e INNER JOIN Terceros AS t ON e.IdTercero = t.IdTercero", f => (Id: f.Entero("IdEmpleado"), P: Desde(f))))
                    .ToDictionary(x => $"E{x.Id}", x => x.P)
                : [];
            var pro = (await s.ConsultarAsync($"SELECT p.IdProveedor, {campos} FROM Proveedores AS p INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero", f => (Id: f.Entero("IdProveedor"), P: Desde(f))))
                .ToDictionary(x => $"P{x.Id}", x => x.P);
            var nom = esquema.ExisteTabla("Nominas")
                ? (await s.ConsultarAsync("SELECT NifOrigen, NombreOrigen FROM Nominas WHERE NifOrigen IS NOT NULL", f => (Nif: Modelo347.Nif(f.Texto("NifOrigen")), Nombre: (f.Texto("NombreOrigen") ?? "").Trim())))
                    .GroupBy(x => x.Nif).ToDictionary(g => g.Key, g => g.First().Nombre)
                : [];
            return (emp, pro, nom);
        }, c);
        PerceptorInformativa Perceptor(string clave) =>
            empleados.TryGetValue(clave, out var e) ? e
            : proveedores.TryGetValue(clave, out var p) ? p
            : new PerceptorInformativa(Modelo347.Nif(clave), nominas.GetValueOrDefault(Modelo347.Nif(clave)) ?? clave, null);
        return (lectura, Perceptor);
    }

    /// <param name="administradores">Perceptores que son administradores (clave interna «E{IdEmpleado}») con su subclave de la clave E.</param>
    public async Task<Declaracion190> Calcular190Async(ContextoSociedadEjercicio ctx, IReadOnlyDictionary<string, string>? administradores = null, CancellationToken c = default)
    {
        var (l, perceptor) = await LeerAsync(ctx, c);
        return Modelo190.Calcular(l.Percepciones, ctx.Ejercicio, await DeclaranteAsync(ctx), perceptor, administradores ?? new Dictionary<string, string>());
    }

    public async Task<Declaracion180> Calcular180Async(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var (l, perceptor) = await LeerAsync(ctx, c);
        return Modelo180.Calcular(l.Percepciones, ctx.Ejercicio, await DeclaranteAsync(ctx), perceptor);
    }

    public async Task<PeriodoInformativa> Ejercicio190Async(ContextoSociedadEjercicio ctx, IReadOnlyDictionary<string, string>? administradores = null, CancellationToken c = default)
    {
        var d = await Calcular190Async(ctx, administradores, c);
        var (desde, hasta) = Plazo(ctx.Ejercicio);
        return await PeriodoAsync(ctx, Modelo190.Modelo, "0A", desde, hasta, d.Percepciones.Count, d.TotalRetenciones, d.Errores, d.Avisos, c);
    }

    public async Task<PeriodoInformativa> Ejercicio180Async(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var d = await Calcular180Async(ctx, c);
        var (desde, hasta) = Plazo(ctx.Ejercicio);
        return await PeriodoAsync(ctx, Modelo180.Modelo, "0A", desde, hasta, d.Arrendadores.Count, d.TotalRetenciones, d.Errores, d.Avisos, c);
    }

    public async Task<Resultado<(Declaracion190 Declaracion, DeclaracionInformativa Tgvi)>> Declaracion190Async(ContextoSociedadEjercicio ctx, bool sustitutiva,
        IReadOnlyDictionary<string, string>? administradores = null, string? contacto = null, CancellationToken c = default)
    {
        var registro = $"Modelo 190 {ctx.Ejercicio}";
        var d = await Calcular190Async(ctx, administradores, c);
        if (!d.Valida)
            return Resultado<(Declaracion190, DeclaracionInformativa)>.Fallo(new Error("Modelo190.Errores", $"El 190 de {ctx.Ejercicio} no se puede presentar todavía.", registro,
                string.Join(" ", d.Errores), "Completa los datos que se indican y vuelve a intentarlo."));
        var persona = await ContactoAsync(ctx, contacto);
        if (persona.TelefonoNumerico == 0) return Resultado<(Declaracion190, DeclaracionInformativa)>.Fallo(SinTelefono(registro));
        var anterior = sustitutiva ? await AnteriorAsync(ctx, Modelo190.Modelo, "0A", c) : null;
        var numero = await Tgvi.NumeroJustificanteAsync(ctx.IdSociedad, Modelo190.Modelo, ctx.Ejercicio, "0A", c);
        var (t1, t2) = Modelo190.Registros(d, persona, numero, anterior?.Justificante, sustitutiva);
        return Resultado<(Declaracion190, DeclaracionInformativa)>.Correcto((d, new DeclaracionInformativa(Modelo190.Modelo, ctx.Ejercicio, "0A", d.Declarante.Nif, d.Declarante.RazonSocial,
            t1, t2, numero, d.TotalRetenciones, [("Percepciones", d.Percepciones.Count.ToString(CultureInfo.InvariantCulture)), ("Retenciones e ingresos a cuenta", Euros(d.TotalRetenciones))],
            anterior?.Justificante)));
    }
}
