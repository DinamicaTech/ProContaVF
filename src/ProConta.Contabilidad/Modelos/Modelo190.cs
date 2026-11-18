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
public sealed record Percepcion190(PerceptorInformativa Perceptor, char Clave, string Subclave, decimal Dinerario, decimal Retenciones, decimal Especie, decimal IngresosCuenta,
    DatosPersonales190? Datos = null)
{
    public decimal Total => Retenciones + IngresosCuenta;
}

/// <summary>Un descendiente (D) o ascendiente (A) del empleado que da derecho a mínimo familiar (FamiliaresEmpleado, esquema 72).</summary>
public sealed record Familiar190(char Parentesco, int AnioNacimiento, int? AnioAdopcion = null, byte Discapacidad = 0, bool PorEntero = true);

/// <summary>Datos personales del empleado para el 190 (Empleados, esquema 72): los de su modelo 145.</summary>
public sealed record DatosPersonales190(int? AnioNacimiento, byte SituacionFamiliar = 3, string? NifConyuge = null, byte Discapacidad = 0, byte ContratoRelacion = 1,
    bool MovilidadGeografica = false, IReadOnlyList<Familiar190>? Familiares = null);

/// <summary>
/// Recuento de los familiares que pide el 190 (posiciones 223 a 253): descendientes menores de 3 años y resto, descendientes y ascendientes por
/// grado de discapacidad, ascendientes menores y mayores de 75, cuántos por entero, y el cómputo de los tres primeros hijos.
/// </summary>
public sealed record Familia190(int Menores3, int Menores3Entero, int Resto, int RestoEntero, int[] DiscDesc, int[] DiscDescEntero, int Asc75, int Asc75Entero,
    int AscMas75, int AscMas75Entero, int[] DiscAsc, int[] DiscAscEntero, int[] ComputoHijos)
{
    /// <summary>Edades al 31/12 del ejercicio: menor de 3 años si nació en los dos años anteriores o en el mismo, o si se adoptó o acogió en ellos.</summary>
    public static Familia190 De(IReadOnlyList<Familiar190> familiares, int ejercicio)
    {
        var desc = familiares.Where(f => f.Parentesco == 'D').OrderBy(f => f.AnioNacimiento).ToList();
        var asc = familiares.Where(f => f.Parentesco == 'A').ToList();
        bool Menor3(Familiar190 f) => ejercicio - f.AnioNacimiento < 3 || (f.AnioAdopcion is int a && ejercicio - a < 3);
        int[] Grado(IEnumerable<Familiar190> l, bool entero) => [.. Enumerable.Range(1, 3).Select(g => l.Count(f => f.Discapacidad == g && (!entero || f.PorEntero)))];
        var menores = desc.Where(Menor3).ToList();
        var resto = desc.Where(f => !Menor3(f)).ToList();
        var asc75 = asc.Where(f => ejercicio - f.AnioNacimiento < 75).ToList();
        var mas75 = asc.Where(f => ejercicio - f.AnioNacimiento >= 75).ToList();
        int[] computo = [.. desc.Take(3).Select(f => f.PorEntero ? 1 : 2).Concat([0, 0, 0]).Take(3)];
        return new(menores.Count, menores.Count(f => f.PorEntero), resto.Count, resto.Count(f => f.PorEntero), Grado(desc, false), Grado(desc, true),
            asc75.Count, asc75.Count(f => f.PorEntero), mas75.Count, mas75.Count(f => f.PorEntero), Grado(asc, false), Grado(asc, true), computo);
    }
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
/// (clave E, subclave indicada); profesionales con clave G, subclave 01. Los datos personales que el 190 pide de los trabajadores de la
/// clave A (año de nacimiento, situación familiar, descendientes…) se leen de Empleados y FamiliaresEmpleado (esquema 72): sin el año de
/// nacimiento no se ofrece presentar; sin código postal del perceptor solo se avisa.
/// </summary>
public static class Modelo190
{
    public const string Modelo = "190";

    /// <param name="perceptor">Identificación de cada perceptor por su clave interna (E…, P…).</param>
    /// <param name="administradores">Perceptores (clave interna) que son administradores, con su subclave de la clave E.</param>
    /// <param name="datosPersonales">Datos personales de cada perceptor por su clave interna (null: no los tiene).</param>
    public static Declaracion190 Calcular(IEnumerable<PercepcionRetencion> percepciones, int ejercicio, Declarante303 declarante, Func<string, PerceptorInformativa> perceptor,
        IReadOnlyDictionary<string, string> administradores, Func<string, DatosPersonales190?>? datosPersonales = null)
    {
        var errores = new List<string>();
        var avisos = new List<string>();
        var lista = new List<Percepcion190>();
        foreach (var g in percepciones.Where(p => p.Clase != ClaseRetencionModelo.Arrendamientos).GroupBy(p => (p.Perceptor, p.Clase)))
        {
            var quien = perceptor(g.Key.Perceptor);
            var (clave, sub) = g.Key.Clase == ClaseRetencionModelo.Profesionales ? ('G', "01")
                : administradores.TryGetValue(g.Key.Perceptor, out var s) ? ('E', s) : ('A', "  ");
            var datos = clave == 'A' ? datosPersonales?.Invoke(g.Key.Perceptor) : null;
            var p = new Percepcion190(quien, clave, sub, g.Sum(x => x.Dinerario), g.Sum(x => x.Retencion), g.Sum(x => x.Especie), g.Sum(x => x.IngresoCuenta), datos);
            if (p.Dinerario == 0m && p.Especie == 0m && p.Total == 0m) continue;
            lista.Add(p);
            if (!Modelo347.EsNifEspanol(quien.Nif)) errores.Add($"{quien.Nombre} no tiene un NIF español válido ({quien.Nif}): corrígelo en su ficha.");
            if (quien.Provincia is null)
                avisos.Add($"{quien.Nombre} ({quien.Nif}) no tiene código postal en su ficha de tercero: el 190 pide su provincia. Complétalo en Terceros (si no, sale «00»).");
            if (clave == 'E' && string.IsNullOrWhiteSpace(sub))
                errores.Add($"Falta la subclave de la clave E de {quien.Nombre} ({quien.Nif}), que está marcado como administrador: elígela en Parámetros y tablas › " +
                    "Datos de los empleados (190).");
            if (clave == 'A' && datos?.AnioNacimiento is null)
                errores.Add($"Falta el año de nacimiento de {quien.Nombre} ({quien.Nif}), que el 190 pide en la clave A: escríbelo en Parámetros y tablas › Datos de los empleados (190), " +
                    "con su situación familiar y discapacidad; sus hijos y ascendientes, en Familiares de los empleados (190).");
            if (clave == 'A' && datos is { SituacionFamiliar: 2 } && !Modelo347.EsNifEspanol(datos.NifConyuge))
                errores.Add($"{quien.Nombre} tiene la situación familiar 2 y le falta el NIF del cónyuge: escríbelo en Parámetros y tablas › Datos de los empleados (190).");
            if (p.Dinerario < 0m || p.Especie < 0m || p.Total < 0m) errores.Add($"{quien.Nombre} tiene importes negativos en el año: revisa sus nóminas o facturas.");
        }
        if (string.IsNullOrWhiteSpace(declarante.Nif)) errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        return new Declaracion190(ejercicio, declarante, [.. lista.OrderBy(p => p.Clave).ThenBy(p => p.Perceptor.Nif, StringComparer.Ordinal)], errores, avisos);
    }

    /// <summary>
    /// Registros del 190 (diseño de la Orden del 190, sources/aeat/DR190e2025.pdf): tipo 1 con el número de percepciones, el total de
    /// percepciones y el de retenciones e ingresos a cuenta; tipo 2 por percepción con los datos adicionales de la clave A (año de nacimiento,
    /// situación familiar, NIF del cónyuge, discapacidad, contrato, movilidad geográfica, descendientes y ascendientes) y a cero en las demás.
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
            .Personales(p, d.Ejercicio)
            .ImporteConSigno(255, 268, 0m).Importe(269, 281, 0m).ImporteConSigno(282, 295, 0m).Importe(296, 308, 0m).Importe(309, 321, 0m)
            // 323-335 Hacienda estatal: en la clave E, lo retenido e ingresado a cuenta (todo a la Hacienda estatal); 336-387 las forales, a cero.
            .Num(322, 322, 0).Importe(323, 335, p.Clave == 'E' ? p.Total : 0m).Importe(336, 387, 0m)
            // 388 tipo de entidad del 389, 389 excesos de acciones de empresas emergentes, 390-394 prestaciones de la clave B.01: numéricos a cero (diseño de 2025).
            .Num(388, 394, 0)
            .ToString()).ToList();
        return (t1.ToString(), t2);
    }
}

/// <summary>Dirección de un inmueble como la piden el 180 y el registro de inmuebles del 347 (ActivosInmuebles, esquema 72).</summary>
public sealed record DireccionInmueble(string? TipoVia, string? NombreVia, string? TipoNumeracion, string? Numero, string? Calificador, string? Bloque, string? Portal,
    string? Escalera, string? Planta, string? Puerta, string? Complemento, string? Localidad, string? Municipio, string? CodigoMunicipio, string? CodigoProvincia, string? CodigoPostal)
{
    /// <summary>Escribe la dirección desde <paramref name="desde"/> (135 en el 180, 141 en el 347): 193 posiciones.</summary>
    internal RegistroInformativo Escribir(RegistroInformativo r, int desde) => r
        .An(desde, desde + 4, TipoVia).An(desde + 5, desde + 54, NombreVia).An(desde + 55, desde + 57, TipoNumeracion).An(desde + 58, desde + 62, Numero?.Trim() is { Length: > 0 and <= 5 } n && n.All(char.IsAsciiDigit) ? n.PadLeft(5, '0') : Numero)
        .An(desde + 63, desde + 65, Calificador).An(desde + 66, desde + 68, Bloque).An(desde + 69, desde + 71, Portal).An(desde + 72, desde + 74, Escalera)
        .An(desde + 75, desde + 77, Planta).An(desde + 78, desde + 80, Puerta).An(desde + 81, desde + 120, Complemento).An(desde + 121, desde + 150, Localidad)
        .An(desde + 151, desde + 180, Municipio).An(desde + 181, desde + 185, CodigoMunicipio).An(desde + 186, desde + 187, CodigoProvincia).An(desde + 188, desde + 192, CodigoPostal);

    /// <summary>Lo que falta para declararla: la vía, el municipio y su código y la provincia.</summary>
    public IReadOnlyList<string> Falta() =>
        [.. new[] { (NombreVia, "la vía"), (Municipio, "el municipio"), (CodigoMunicipio, "el código del municipio"), (CodigoProvincia, "la provincia") }
            .Where(x => string.IsNullOrWhiteSpace(x.Item1)).Select(x => x.Item2)];
}

/// <summary>Inmueble arrendado: el activo, su situación (1 a 4, la del 180), su referencia catastral y su dirección.</summary>
public sealed record Inmueble180(string Activo, int? Situacion, string? ReferenciaCatastral, DireccionInmueble Direccion)
{
    /// <summary>Lo que falta para declararlo, con el nombre del activo; vacío si está completo.</summary>
    public IReadOnlyList<string> Falta()
    {
        var f = new List<string>();
        if (Situacion is not (>= 1 and <= 4)) f.Add("la situación");
        if (Situacion is >= 1 and <= 3 && string.IsNullOrWhiteSpace(ReferenciaCatastral)) f.Add("la referencia catastral");
        f.AddRange(Direccion.Falta());
        return f;
    }
}

/// <summary>Un arrendador del 180: identificación, base, porcentaje de retención y retenciones del año, y el inmueble que alquila.</summary>
public sealed record Arrendador180(PerceptorInformativa Arrendador, decimal Base, decimal Retenciones, Inmueble180? Inmueble = null)
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
/// con la suma de los cuatro 115 (casilla 03). El inmueble de cada arrendador sale del activo de tipo inmueble cuyo centro de coste es el de
/// sus facturas de alquiler (esquema 72): sin él, o sin su situación, referencia catastral y dirección, no se ofrece presentar.
/// </summary>
public static class Modelo180
{
    public const string Modelo = "180";

    /// <param name="inmuebles">Inmuebles de las facturas de alquiler de cada arrendador (por su clave interna) y las facturas que no tienen inmueble.</param>
    public static Declaracion180 Calcular(IEnumerable<PercepcionRetencion> percepciones, int ejercicio, Declarante303 declarante, Func<string, PerceptorInformativa> perceptor,
        Func<string, (IReadOnlyList<Inmueble180> Inmuebles, IReadOnlyList<string> SinInmueble)>? inmuebles = null)
    {
        var avisos = new List<string>();
        var errores = new List<string>();
        var lista = new List<Arrendador180>();
        foreach (var g in percepciones.Where(p => p.Clase == ClaseRetencionModelo.Arrendamientos).GroupBy(p => p.Perceptor))
        {
            var quien = perceptor(g.Key);
            var (lista180, sinInmueble) = inmuebles?.Invoke(g.Key) ?? ([], []);
            var a = new Arrendador180(quien, g.Sum(x => x.Dinerario), g.Sum(x => x.Retencion), lista180.Count == 1 ? lista180[0] : null);
            if (a.Base == 0m && a.Retenciones == 0m) continue;
            lista.Add(a);
            if (!Modelo347.EsNifEspanol(quien.Nif)) errores.Add($"{quien.Nombre} no tiene un NIF español válido ({quien.Nif}): corrígelo en su ficha.");
            if (quien.Provincia is null)
                avisos.Add($"{quien.Nombre} ({quien.Nif}) no tiene código postal en su ficha de tercero: el 180 pide su provincia. Complétalo en Terceros (si no, sale «00»).");
            foreach (var f in sinInmueble)
                errores.Add($"La factura de alquiler {f} de {quien.Nombre} no tiene inmueble: ponle el centro de coste del inmueble y, si el inmueble no está en Activos, dalo de alta " +
                    "(como activo ajeno si no es de la empresa) con ese centro de coste.");
            if (lista180.Count == 0 && sinInmueble.Count == 0)
                errores.Add($"No encuentro el inmueble que alquila {quien.Nombre}: sus facturas de alquiler no tienen centro de coste de un inmueble de Activos.");
            if (lista180.Count > 1)
                errores.Add($"{quien.Nombre} alquila {lista180.Count} inmuebles ({string.Join(", ", lista180.Select(x => x.Activo))}): el programa aún no reparte un arrendador entre varios inmuebles. " +
                    "Presenta el 180 desde la Sede o pide que se añada.");
            if (a.Inmueble is { } i && i.Falta() is { Count: > 0 } falta)
                errores.Add($"Al inmueble {i.Activo} le falta {string.Join(", ", falta)}: complétalo en Activos › Inmuebles para los modelos (180 y 347).");
            if (a.Base < 0m || a.Retenciones < 0m) errores.Add($"{quien.Nombre} tiene importes negativos en el año: revisa sus facturas de alquiler.");
        }
        if (string.IsNullOrWhiteSpace(declarante.Nif)) errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        return new Declaracion180(ejercicio, declarante, [.. lista.OrderBy(a => a.Arrendador.Nif, StringComparer.Ordinal)], errores, avisos);
    }

    /// <summary>
    /// Registros del 180 (sources/aeat/DR180e2023.pdf): tipo 1 con el número de perceptores, la base y las retenciones; tipo 2 por arrendador con
    /// su provincia, modalidad dineraria, base, porcentaje, retenciones y el inmueble (situación, referencia catastral y dirección).
    /// </summary>
    public static (string Tipo1, IReadOnlyList<string> Tipo2) Registros(Declaracion180 d, ContactoInformativa contacto, string numero, string? anterior = null, bool sustitutiva = false)
    {
        var t1 = Tipo1Informativa.Base(Modelo, d.Ejercicio, d.Declarante, contacto, numero, anterior, sustitutiva)
            .Num(136, 144, d.Arrendadores.Count).ImporteConSigno(145, 160, d.TotalBase).Importe(161, 175, d.TotalRetenciones);
        var t2 = d.Arrendadores.Select(a =>
        {
            var r = new RegistroInformativo()
                .Texto(1, "2").Texto(2, Modelo).Num(5, 8, d.Ejercicio).Nif(9, d.Declarante.Nif).Nif(18, a.Arrendador.Nif).An(36, 75, a.Arrendador.Nombre)
                .Texto(76, (a.Arrendador.Provincia ?? "00").PadLeft(2, '0')[..2]).Num(78, 78, 1).ImporteConSigno(79, 92, a.Base)
                .Num(93, 96, (long)decimal.Round(a.Porcentaje * 100m, 0, MidpointRounding.AwayFromZero)).Importe(97, 109, a.Retenciones).Num(110, 113, 0);
            if (a.Inmueble is { } i)
            {
                r.Num(114, 114, i.Situacion ?? 4).An(115, 134, i.ReferenciaCatastral);
                i.Direccion.Escribir(r, 135);
            }
            return r.ToString();
        }).ToList();
        return (t1.ToString(), t2);
    }
}

/// <summary>
/// Modelos 190 y 180 de cada ejercicio con el contrato de los demás servicios de modelos: el estado del año (enero del siguiente) y la
/// declaración calculada desde las percepciones del 111 y del 115 (<see cref="ServicioRetenciones"/>), con la identificación de cada
/// perceptor de su ficha de tercero. Los dos se generan para TGVI Online; el inmueble del 180 sale del activo cuyo centro de coste
/// llevan las facturas de alquiler con retención (esquema 72).
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
                return new PerceptorInformativa(Modelo347.Nif(f.Texto("NIF")), (nombre ?? "").Trim(), cp is { Length: >= 2 } && cp[..2].All(char.IsAsciiDigit) ? cp[..2] : null);
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

    /// <param name="administradores">
    /// Perceptores que son administradores (clave interna «E{IdEmpleado}») con su subclave de la clave E; null: los empleados marcados como
    /// administradores de la empresa (EmpleadosSociedad.Administrador; la subclave, su cargo si es 01 a 04 y, si no, 01).
    /// </param>
    public async Task<Declaracion190> Calcular190Async(ContextoSociedadEjercicio ctx, IReadOnlyDictionary<string, string>? administradores = null, CancellationToken c = default)
    {
        var (l, perceptor) = await LeerAsync(ctx, c);
        var (personales, admins) = await DatosEmpleadosAsync(ctx);
        return Modelo190.Calcular(l.Percepciones, ctx.Ejercicio, await DeclaranteAsync(ctx), perceptor, administradores ?? admins, clave => personales.GetValueOrDefault(clave));
    }

    public async Task<Declaracion180> Calcular180Async(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var (l, perceptor) = await LeerAsync(ctx, c);
        var inmuebles = await InmueblesAlquilerAsync(ctx);
        return Modelo180.Calcular(l.Percepciones, ctx.Ejercicio, await DeclaranteAsync(ctx), perceptor,
            clave => inmuebles.TryGetValue(clave, out var x) ? (x.Inmuebles, x.SinInmueble) : ([], []));
    }

    /// <summary>
    /// Datos personales de los empleados para el 190 (Empleados y FamiliaresEmpleado, esquema 72) por su clave interna «E{IdEmpleado}», y los que son
    /// administradores de la empresa con su subclave de la clave E.
    /// </summary>
    private Task<(Dictionary<string, DatosPersonales190> Personales, Dictionary<string, string> Administradores)> DatosEmpleadosAsync(ContextoSociedadEjercicio ctx) =>
        Bd.EjecutarAsync(async s =>
        {
            var esquema = s.LeerEsquema();
            // La subclave de la clave E: la elegida en «Datos de los empleados (190)» o, sin ella, el cargo si es un código de 01 a 04; si no, vacía (bloquea).
            var conSubclave = esquema.ExisteColumna("EmpleadosSociedad", "SubclaveAdministrador190");
            var admins = esquema.ExisteColumna("EmpleadosSociedad", "Administrador")
                ? (await s.ConsultarAsync($"SELECT IdEmpleado, CargoAdministrador{(conSubclave ? ", SubclaveAdministrador190" : "")} FROM EmpleadosSociedad WHERE IdSociedad = ? AND Administrador = True",
                    f => (Id: f.Entero("IdEmpleado"), Cargo: (f.Texto("CargoAdministrador") ?? "").Trim(), Sub: conSubclave ? (f.Texto("SubclaveAdministrador190") ?? "").Trim() : ""), ctx.IdSociedad))
                    .ToDictionary(x => $"E{x.Id}", x => x.Sub is "01" or "02" or "03" or "04" ? x.Sub : x.Cargo is "01" or "02" or "03" or "04" ? x.Cargo : "")
                : [];
            if (!esquema.ExisteColumna("Empleados", "AnioNacimiento")) return (new Dictionary<string, DatosPersonales190>(), admins);
            static byte B(IFilaDatos f, string c, byte defecto) => f.Valor(c) is null or DBNull ? defecto : Convert.ToByte(f.Valor(c), CultureInfo.InvariantCulture);
            var familiares = (await s.ConsultarAsync("SELECT IdEmpleado, Parentesco, AnioNacimiento, AnioAdopcion, Discapacidad, PorEntero FROM FamiliaresEmpleado WHERE Activo = True",
                f => (Id: f.Entero("IdEmpleado"), F: new Familiar190((f.Texto("Parentesco") ?? "D").Trim() == "A" ? 'A' : 'D', Convert.ToInt32(f.Valor("AnioNacimiento"), CultureInfo.InvariantCulture),
                    f.Valor("AnioAdopcion") is null or DBNull ? null : Convert.ToInt32(f.Valor("AnioAdopcion"), CultureInfo.InvariantCulture), B(f, "Discapacidad", 0), f.Logico("PorEntero")))))
                .ToLookup(x => x.Id, x => x.F);
            var personales = (await s.ConsultarAsync(
                "SELECT IdEmpleado, AnioNacimiento, SituacionFamiliar, NifConyuge, Discapacidad, ContratoRelacion, MovilidadGeografica FROM Empleados",
                f => (Id: f.Entero("IdEmpleado"), D: new DatosPersonales190(f.Valor("AnioNacimiento") is null or DBNull ? null : Convert.ToInt32(f.Valor("AnioNacimiento"), CultureInfo.InvariantCulture),
                    B(f, "SituacionFamiliar", 3), f.Texto("NifConyuge")?.Trim(), B(f, "Discapacidad", 0), B(f, "ContratoRelacion", 1), f.Logico("MovilidadGeografica")))))
                .ToDictionary(x => $"E{x.Id}", x => x.D with { Familiares = [.. familiares[x.Id]] });
            return (personales, admins);
        });

    /// <summary>
    /// Inmueble de las facturas de alquiler con retención de cada arrendador (clave interna «P{IdProveedor}»): el activo de tipo inmueble cuyo
    /// centro de coste es el de la factura (ActivosInmuebles, esquema 72), y las facturas sin centro o con un centro sin inmueble.
    /// </summary>
    private Task<Dictionary<string, (IReadOnlyList<Inmueble180> Inmuebles, IReadOnlyList<string> SinInmueble)>> InmueblesAlquilerAsync(ContextoSociedadEjercicio ctx) =>
        Bd.EjecutarAsync(async s =>
        {
            var r = new Dictionary<string, (IReadOnlyList<Inmueble180>, IReadOnlyList<string>)>();
            if (!s.LeerEsquema().ExisteColumna("ActivosInmuebles", "SituacionInmueble")) return r;
            var facturas = await s.ConsultarAsync(
                "SELECT IdProveedor, NumeroFactura, IdCentroCoste FROM FacturasCompra WHERE IdSociedadEjercicio = ? AND TipoRetencion = 2 AND IdProveedor IS NOT NULL",
                f => (Proveedor: f.Entero("IdProveedor"), Numero: (f.Texto("NumeroFactura") ?? "").Trim(), Centro: f.EnteroNulo("IdCentroCoste")), ctx.IdSociedadEjercicio);
            var inmuebles = (await s.ConsultarAsync(
                "SELECT a.IdCentroCoste, a.Codigo, a.Nombre, i.SituacionInmueble, i.ReferenciaCatastral, i.TipoVia, i.NombreVia, i.TipoNumeracion, i.NumeroCasa, i.CalificadorNumero, " +
                "i.Bloque, i.Portal, i.Escalera, i.Planta, i.Puerta, i.Complemento, i.Localidad, i.Municipio, i.CodigoMunicipio, i.CodigoProvincia, i.CodigoPostal " +
                "FROM (Activos AS a INNER JOIN TiposActivo AS t ON a.IdTipoActivo = t.IdTipoActivo) LEFT JOIN ActivosInmuebles AS i ON a.IdActivo = i.IdActivo " +
                "WHERE a.IdSociedad = ? AND t.Categoria = 'INMUEBLE' AND a.IdCentroCoste IS NOT NULL",
                f => (Centro: f.Entero("IdCentroCoste"), I: Inmueble(f)), ctx.IdSociedad))
                .GroupBy(x => x.Centro).ToDictionary(g => g.Key, g => g.First().I);
            foreach (var g in facturas.GroupBy(x => x.Proveedor))
            {
                var lista = g.Where(x => x.Centro is int cc && inmuebles.ContainsKey(cc)).Select(x => inmuebles[x.Centro!.Value]).DistinctBy(x => x.Activo).ToList();
                var sin = g.Where(x => x.Centro is not int cc || !inmuebles.ContainsKey(cc)).Select(x => x.Numero).Distinct().ToList();
                r[$"P{g.Key}"] = (lista, sin);
            }
            return r;
        });

    /// <summary>Inmueble de una fila con las columnas de ActivosInmuebles (para el 180 y el 347).</summary>
    internal static Inmueble180 Inmueble(IFilaDatos f) =>
        new($"{f.Texto("Codigo")?.Trim()} · {f.Texto("Nombre")?.Trim()}",
            f.Valor("SituacionInmueble") is null or DBNull ? null : Convert.ToInt32(f.Valor("SituacionInmueble"), CultureInfo.InvariantCulture), f.Texto("ReferenciaCatastral")?.Trim(),
            new DireccionInmueble(f.Texto("TipoVia"), f.Texto("NombreVia"), f.Texto("TipoNumeracion"), f.Texto("NumeroCasa"), f.Texto("CalificadorNumero"), f.Texto("Bloque"),
                f.Texto("Portal"), f.Texto("Escalera"), f.Texto("Planta"), f.Texto("Puerta"), f.Texto("Complemento"), f.Texto("Localidad"), f.Texto("Municipio"),
                f.Texto("CodigoMunicipio"), f.Texto("CodigoProvincia"), f.Texto("CodigoPostal")));

    /// <summary>La declaración del 180 y sus registros para presentarla por TGVI Online; con <paramref name="sustitutiva"/>, sustitutiva de la última presentada.</summary>
    public async Task<Resultado<(Declaracion180 Declaracion, DeclaracionInformativa Tgvi)>> Declaracion180Async(ContextoSociedadEjercicio ctx, bool sustitutiva, string? contacto = null,
        CancellationToken c = default)
    {
        var registro = $"Modelo 180 {ctx.Ejercicio}";
        var d = await Calcular180Async(ctx, c);
        if (d.Arrendadores.Count == 0)
            return Resultado<(Declaracion180, DeclaracionInformativa)>.Fallo(new Error("Modelo180.SinAlquileres", $"No hay alquileres con retención en {ctx.Ejercicio}.", registro,
                "El 180 resume las retenciones de los alquileres que paga la empresa; sin ellos no se presenta."));
        if (!d.Valida)
            return Resultado<(Declaracion180, DeclaracionInformativa)>.Fallo(new Error("Modelo180.Errores", $"El 180 de {ctx.Ejercicio} no se puede presentar todavía.", registro,
                string.Join(" ", d.Errores), "Completa los datos que se indican y vuelve a intentarlo."));
        var persona = await ContactoAsync(ctx, contacto);
        if (persona.TelefonoNumerico == 0) return Resultado<(Declaracion180, DeclaracionInformativa)>.Fallo(SinTelefono(registro));
        var anterior = sustitutiva ? await AnteriorAsync(ctx, Modelo180.Modelo, "0A", c) : null;
        var numero = await Tgvi.NumeroJustificanteAsync(ctx.IdSociedad, Modelo180.Modelo, ctx.Ejercicio, "0A", c);
        var (t1, t2) = Modelo180.Registros(d, persona, numero, anterior?.Justificante, sustitutiva);
        return Resultado<(Declaracion180, DeclaracionInformativa)>.Correcto((d, new DeclaracionInformativa(Modelo180.Modelo, ctx.Ejercicio, "0A", d.Declarante.Nif, d.Declarante.RazonSocial,
            t1, t2, numero, d.TotalRetenciones, [("Arrendadores", d.Arrendadores.Count.ToString(CultureInfo.InvariantCulture)), ("Base", Euros(d.TotalBase)),
                ("Retenciones", Euros(d.TotalRetenciones))], anterior?.Justificante)));
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

/// <summary>Datos adicionales del registro de percepción del 190 (posiciones 153 a 254).</summary>
internal static class DatosAdicionales190
{
    public static RegistroInformativo Personales(this RegistroInformativo r, Percepcion190 p, int ejercicio)
    {
        var d = p.Clave == 'A' ? p.Datos ?? new DatosPersonales190(null) : null;
        if (d is null)
            return r.Num(153, 156, 0).Num(157, 157, 0).Num(167, 167, 0).Num(168, 168, 0).Num(169, 170, 0)
                .Importe(171, 183, 0m).Importe(184, 196, 0m).Importe(197, 209, 0m).Importe(210, 222, 0m).Num(223, 253, 0).Num(254, 254, 0);
        var f = Familia190.De(d.Familiares ?? [], ejercicio);
        r.Num(153, 156, d.AnioNacimiento ?? 0).Num(157, 157, d.SituacionFamiliar is >= 1 and <= 3 ? d.SituacionFamiliar : 3);
        if (d.SituacionFamiliar == 2 && !string.IsNullOrWhiteSpace(d.NifConyuge)) r.Nif(158, d.NifConyuge);
        r.Num(167, 167, d.Discapacidad is <= 3 ? d.Discapacidad : 0).Num(168, 168, d.ContratoRelacion is >= 1 and <= 4 ? d.ContratoRelacion : 1).Num(169, 169, 0)
            .Num(170, 170, d.MovilidadGeografica ? 1 : 0)
            .Importe(171, 183, 0m).Importe(184, 196, 0m).Importe(197, 209, 0m).Importe(210, 222, 0m)
            .Num(223, 223, Math.Min(9, f.Menores3)).Num(224, 224, Math.Min(9, f.Menores3Entero)).Num(225, 226, f.Resto).Num(227, 228, f.RestoEntero);
        for (var g = 0; g < 3; g++) r.Num(229 + 4 * g, 230 + 4 * g, f.DiscDesc[g]).Num(231 + 4 * g, 232 + 4 * g, f.DiscDescEntero[g]);
        r.Num(241, 241, Math.Min(9, f.Asc75)).Num(242, 242, Math.Min(9, f.Asc75Entero)).Num(243, 243, Math.Min(9, f.AscMas75)).Num(244, 244, Math.Min(9, f.AscMas75Entero));
        for (var g = 0; g < 3; g++) r.Num(245 + 2 * g, 245 + 2 * g, Math.Min(9, f.DiscAsc[g])).Num(246 + 2 * g, 246 + 2 * g, Math.Min(9, f.DiscAscEntero[g]));
        return r.Num(251, 251, f.ComputoHijos[0]).Num(252, 252, f.ComputoHijos[1]).Num(253, 253, f.ComputoHijos[2]).Num(254, 254, 0);
    }
}
