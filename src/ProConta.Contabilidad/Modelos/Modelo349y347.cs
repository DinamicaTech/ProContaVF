using System.Globalization;
using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Un operador intracomunitario del 349: país y número de su NIF comunitario, nombre, clave de operación y base del periodo.</summary>
public sealed record Operador349(string Pais, string Numero, string Nombre, char Clave, decimal Base);

/// <summary>El 349 de un trimestre: los operadores (uno por NIF y clave), el cuadre con el 303 y lo que impide presentarlo o conviene revisar.</summary>
public sealed record Declaracion349(int Ejercicio, int Trimestre, Declarante303 Declarante, IReadOnlyList<Operador349> Operadores, IReadOnlyList<string> Errores,
    IReadOnlyList<string> Avisos)
{
    public string Periodo => $"{Trimestre}T";
    public decimal Total => Operadores.Sum(o => o.Base);
    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Modelo 349, declaración recapitulativa de operaciones intracomunitarias (contabilidad/modelos-tributarios/modelo-349), sin BD, desde las
/// líneas de IVA del trimestre: adquisiciones intracomunitarias de bienes (clave A) y de servicios (I), entregas intracomunitarias
/// exentas (E5, clave E) y prestaciones de servicios a empresarios de la UE no sujetas por localización (N2, clave S). Un registro por
/// operador y clave con la suma de sus bases. Cuadra con el 303 del trimestre: A + I = casilla 10, E = 59. Sin registros de
/// rectificaciones: un operador con la suma negativa (abonos de un periodo anterior) no se genera y se explica cómo hacerlo en la Sede.
/// </summary>
public static class Modelo349
{
    public const string Modelo = "349";

    /// <summary>Códigos de país de la UE del NIF comunitario (Grecia es EL; XI, Irlanda del Norte para bienes).</summary>
    public static readonly IReadOnlySet<string> PaisesUe = new HashSet<string>(StringComparer.Ordinal)
    {
        "AT", "BE", "BG", "CY", "CZ", "DE", "DK", "EE", "EL", "ES", "FI", "FR", "HR", "HU", "IE", "IT", "LT", "LU", "LV", "MT", "NL", "PL", "PT", "RO", "SE", "SI", "SK", "XI",
    };

    /// <param name="esBien">Si una adquisición intracomunitaria es de bienes (si no, de servicios).</param>
    /// <param name="pais">País del tercero cuando su NIF no lleva el prefijo (del libro de IVA).</param>
    public static Declaracion349 Calcular(IReadOnlyList<LineaIva> lineas, Resultado303 r303, int ejercicio, int trimestre, Declarante303 declarante, Func<LineaIva, bool> esBien,
        Func<LineaIva, string?> pais)
    {
        var errores = new List<string>();
        var avisos = new List<string>();
        var del = lineas.Where(l => l.Trimestre == trimestre).ToList();
        var partes = new List<(string Pais, string Numero, string Nombre, char Clave, decimal Base)>();
        void Anadir(LineaIva l, char clave)
        {
            var (p, n) = NifComunitario(l.Nif, pais(l));
            if (p is null || p == "ES" || !PaisesUe.Contains(p))
            {
                errores.Add($"{l.Tercero} ({l.Nif}) no tiene un NIF comunitario con país de la UE: corrige su NIF o su país en la ficha del tercero (factura {l.Factura}).");
                return;
            }
            partes.Add((p, n, l.Tercero, clave, l.Base));
        }
        foreach (var l in del.Where(Calculo303.EsIntracomunitaria)) Anadir(l, esBien(l) ? 'A' : 'I');
        foreach (var l in del.Where(l => Calculo303.EsRepercutido(l) && l.ClaveOperacion == "E5")) Anadir(l, 'E');
        foreach (var l in del.Where(l => Calculo303.EsRepercutido(l) && l.ClaveOperacion == "N2" && NifComunitario(l.Nif, pais(l)).Pais is { } p && PaisesUe.Contains(p) && p != "ES"))
            Anadir(l, 'S');
        var operadores = partes.GroupBy(x => (x.Pais, x.Numero, x.Clave))
            .Select(g => new Operador349(g.Key.Pais, g.Key.Numero, g.First().Nombre, g.Key.Clave, g.Sum(x => x.Base)))
            .OrderBy(o => o.Clave).ThenBy(o => o.Pais).ThenBy(o => o.Numero, StringComparer.Ordinal).ToList();
        foreach (var o in operadores.Where(o => o.Base < 0m))
            errores.Add($"{o.Nombre} ({o.Pais}{o.Numero}, clave {o.Clave}) suma {Euros(o.Base)} en el {trimestre}T: los abonos de operaciones de otro periodo van como rectificación, " +
                "que el programa aún no genera. Preséntalo desde la Sede o espera a que la suma sea positiva.");
        // Cuadre con el 303 del trimestre.
        var intra = r303.Linea(Calculo303.ClaveIntracomunitarias).En(trimestre).Base;
        var ai = operadores.Where(o => o.Clave is 'A' or 'I').Sum(o => o.Base);
        if (ai != intra) avisos.Add($"Las adquisiciones intracomunitarias del 349 ({Euros(ai)}) no son la casilla 10 del 303 del {trimestre}T ({Euros(intra)}): revisa las líneas sin NIF comunitario.");
        var entregas = r303.Linea(Calculo303.ClaveEntregasIntracomunitarias).En(trimestre).Base;
        var e = operadores.Where(o => o.Clave == 'E').Sum(o => o.Base);
        if (e != entregas) avisos.Add($"Las entregas intracomunitarias del 349 ({Euros(e)}) no son la casilla 59 del 303 del {trimestre}T ({Euros(entregas)}).");
        if (string.IsNullOrWhiteSpace(declarante.Nif)) errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        return new Declaracion349(ejercicio, trimestre, declarante, operadores, errores, avisos);
    }

    /// <summary>Separa el NIF comunitario en país (2 letras) y número; si el NIF no lleva prefijo de la UE se usa el país del tercero.</summary>
    public static (string? Pais, string Numero) NifComunitario(string? nif, string? pais)
    {
        var n = RegistroInformativo.NormalizarNif(nif);
        if (n.Length > 2 && char.IsAsciiLetter(n[0]) && char.IsAsciiLetter(n[1]) && PaisesUe.Contains(n[..2])) return (n[..2], n[2..]);
        var p = (pais ?? "").Trim().ToUpperInvariant();
        if (p == "GR") p = "EL";
        return (p.Length == 2 ? p : null, n);
    }

    /// <summary>Registros del fichero: el tipo 1 del declarante y un tipo 2 por operador (diseño de la Orden del 349, sources/aeat/DR349.pdf).</summary>
    public static (string Tipo1, IReadOnlyList<string> Tipo2) Registros(Declaracion349 d, ContactoInformativa contacto, string numero, string? anterior = null, bool sustitutiva = false)
    {
        var validos = d.Operadores.Where(o => o.Base >= 0m).ToList();
        var t1 = Tipo1Informativa.Base(Modelo, d.Ejercicio, d.Declarante, contacto, numero, anterior, sustitutiva)
            .Texto(136, d.Periodo)
            .Num(138, 146, validos.Count).Importe(147, 161, validos.Sum(o => o.Base))
            .Num(162, 170, 0).Importe(171, 185, 0m)
            .Texto(186, " ");
        var t2 = validos.Select(o => new RegistroInformativo()
            .Texto(1, "2").Texto(2, Modelo).Num(5, 8, d.Ejercicio).Nif(9, d.Declarante.Nif)
            .An(76, 77, o.Pais).An(78, 92, o.Numero).An(93, 132, o.Nombre).Texto(133, o.Clave.ToString())
            .Importe(134, 146, o.Base).ToString()).ToList();
        return (t1.ToString(), t2);
    }

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}

/// <summary>Un declarado del 347: NIF, nombre, provincia (2 cifras del código postal), clave (A compras, B ventas) e importe de cada trimestre (IVA incluido).</summary>
public sealed record Declarado347(string Nif, string Nombre, string Provincia, char Clave, IReadOnlyList<decimal> Trimestres, bool InversionSujetoPasivo,
    bool ArrendamientoLocal = false)
{
    public decimal Total => Trimestres.Sum();
}

/// <summary>Un local de negocio alquilado a un arrendatario en el año (registro de inmueble del 347): el inmueble y lo facturado por trimestres, IVA incluido.</summary>
public sealed record Arrendamiento347(string Nif, string Nombre, Inmueble180 Inmueble, IReadOnlyList<decimal> Trimestres)
{
    public decimal Total => Trimestres.Sum();

    /// <summary>Situación del 347 (1 normal, 2 País Vasco o Navarra, 3 sin referencia catastral, 4 extranjero) desde la del 180 (1 a 4).</summary>
    public int Situacion347 => Inmueble.Situacion switch { 2 or 3 => 2, 4 => 3, _ => 1 };
}

public sealed record Declaracion347(int Ejercicio, Declarante303 Declarante, IReadOnlyList<Declarado347> Declarados, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos,
    IReadOnlyList<Arrendamiento347>? Inmuebles = null)
{
    public decimal Total => Declarados.Sum(x => x.Total);
    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Modelo 347, declaración anual de operaciones con terceras personas (contabilidad/modelos-tributarios/modelo-347), sin BD, desde las
/// líneas de IVA del año: por tercero (NIF español) y clave (A adquisiciones, B entregas), la suma de las facturas IVA incluido (en la
/// inversión del sujeto pasivo, la base y la marca), por trimestres, de los que pasan de 3.005,06 € en el año. Fuera: lo que va al 349
/// (intracomunitarias), las importaciones, los terceros sin NIF español (exportaciones, no establecidos), las facturas con retención (van
/// en el 190 o el 180) y los registros marcados en el libro como «no incluir en el 347».
/// </summary>
public static class Modelo347
{
    public const string Modelo = "347";
    public const decimal Umbral = 3005.06m;

    /// <param name="excluir">Líneas que no van: con retención o marcadas «no incluir en el 347» en el libro de IVA.</param>
    /// <param name="codigoPostal">Código postal del tercero por su NIF (la provincia son sus dos primeras cifras).</param>
    /// <param name="arrendamientos">Locales de negocio alquilados en el año, por arrendatario e inmueble: su parte de las ventas va en un registro aparte con la marca
    /// de arrendamiento de local de negocio y cada uno en un registro de inmueble.</param>
    public static Declaracion347 Calcular(IReadOnlyList<LineaIva> lineas, int ejercicio, Declarante303 declarante, Func<LineaIva, bool> excluir, Func<string, string?> codigoPostal,
        IReadOnlyList<Arrendamiento347>? arrendamientos = null)
    {
        var errores = new List<string>();
        var avisos = new List<string>();
        var validas = lineas.Where(l => l.Trimestre is >= 1 and <= 4 && !Calculo303.EsIntracomunitaria(l) && !Calculo303.EsImportacion(l) && !excluir(l)).ToList();
        var extranjeros = validas.Where(l => !EsNifEspanol(l.Nif)).Select(l => l.Tercero).Distinct().ToList();
        if (extranjeros.Count > 0)
            avisos.Add($"No van en el 347 {extranjeros.Count} tercero{(extranjeros.Count == 1 ? "" : "s")} sin NIF español ({string.Join(", ", extranjeros.Take(5))}{(extranjeros.Count > 5 ? "…" : "")}): " +
                "si alguno es español, corrige su NIF en la ficha del tercero.");
        var declarados = new List<Declarado347>();
        foreach (var g in validas.Where(l => EsNifEspanol(l.Nif)).GroupBy(l => (Nif: Nif(l.Nif), Clave: Calculo303.EsSoportado(l) ? 'A' : 'B')))
        {
            var t = Enumerable.Range(1, 4).Select(q => g.Where(l => l.Trimestre == q).Sum(Importe)).ToList();
            if (Math.Abs(t.Sum()) <= Umbral) continue;
            var cp = codigoPostal(g.Key.Nif);
            var provincia = cp is { Length: >= 2 } && cp[..2].All(char.IsAsciiDigit) && int.Parse(cp[..2], CultureInfo.InvariantCulture) is >= 1 and <= 52 ? cp[..2] : null;
            if (provincia is null) errores.Add($"{g.First().Tercero} ({g.Key.Nif}) no tiene código postal en su ficha: el 347 pide su provincia. Escríbelo en Terceros.");
            var locales = g.Key.Clave == 'B' ? (arrendamientos ?? []).Where(a => Nif(a.Nif) == g.Key.Nif).ToList() : [];
            if (locales.Count == 0)
            {
                declarados.Add(new Declarado347(g.Key.Nif, g.First().Tercero, provincia ?? "00", g.Key.Clave, t, g.Any(l => l.Isp)));
                continue;
            }
            // El arrendamiento de locales de negocio va en un registro aparte, con su marca (diseño del 347, posición 100).
            var deLocal = Enumerable.Range(0, 4).Select(q => locales.Sum(a => a.Trimestres[q])).ToList();
            var resto = Enumerable.Range(0, 4).Select(q => t[q] - deLocal[q]).ToList();
            declarados.Add(new Declarado347(g.Key.Nif, g.First().Tercero, provincia ?? "00", 'B', deLocal, false, ArrendamientoLocal: true));
            if (resto.Any(x => x != 0m)) declarados.Add(new Declarado347(g.Key.Nif, g.First().Tercero, provincia ?? "00", 'B', resto, g.Any(l => l.Isp)));
        }
        var inmuebles = (arrendamientos ?? []).Where(a => declarados.Any(d => d.ArrendamientoLocal && d.Nif == Nif(a.Nif))).ToList();
        foreach (var a in inmuebles.Where(a => a.Inmueble.Falta().Count > 0).DistinctBy(a => a.Inmueble.Activo))
            errores.Add($"Al local {a.Inmueble.Activo}, alquilado a {a.Nombre}, le falta {string.Join(", ", a.Inmueble.Falta())} para el registro de inmuebles del 347: complétalo en " +
                "Activos › Inmuebles para los modelos (180 y 347).");
        if (string.IsNullOrWhiteSpace(declarante.Nif)) errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        return new Declaracion347(ejercicio, declarante, [.. declarados.OrderBy(d => d.Clave).ThenBy(d => d.Nif, StringComparer.Ordinal).ThenBy(d => d.ArrendamientoLocal)], errores, avisos,
            inmuebles);
    }

    /// <summary>Importe de una línea para el 347: IVA incluido; con inversión del sujeto pasivo, solo la base.</summary>
    public static decimal Importe(LineaIva l) => l.Isp ? l.Base : l.Base + l.Cuota;

    public static string Nif(string? nif)
    {
        var n = RegistroInformativo.NormalizarNif(nif);
        return n.StartsWith("ES", StringComparison.Ordinal) && n.Length == 11 ? n[2..] : n;
    }

    /// <summary>NIF español: 9 caracteres (con o sin el prefijo ES), con letra o cifra de control.</summary>
    public static bool EsNifEspanol(string? nif)
    {
        var n = Nif(nif);
        return n.Length == 9 && (char.IsAsciiDigit(n[0]) || "ABCDEFGHJKLMNPQRSUVWXYZ".Contains(n[0])) && n.Skip(1).Take(7).All(char.IsAsciiDigit);
    }

    public static (string Tipo1, IReadOnlyList<string> Tipo2) Registros(Declaracion347 d, ContactoInformativa contacto, string numero, string? anterior = null, bool sustitutiva = false)
    {
        var t1 = Tipo1Informativa.Base(Modelo, d.Ejercicio, d.Declarante, contacto, numero, anterior, sustitutiva)
            .Num(136, 144, d.Declarados.Count).ImporteConSigno(145, 160, d.Total)
            .Num(161, 169, (d.Inmuebles ?? []).Count).ImporteConSigno(170, 185, (d.Inmuebles ?? []).Sum(a => a.Total));
        var t2 = d.Declarados.Select(x =>
        {
            var r = new RegistroInformativo()
                .Texto(1, "2").Texto(2, Modelo).Num(5, 8, d.Ejercicio).Nif(9, d.Declarante.Nif).Nif(18, x.Nif).An(36, 75, x.Nombre)
                .Texto(76, "D").Texto(77, x.Provincia).An(79, 80, "").Texto(82, x.Clave.ToString()).ImporteConSigno(83, 98, x.Total).Texto(100, x.ArrendamientoLocal ? "X" : " ")
                .Importe(101, 115, 0m).ImporteConSigno(116, 131, 0m).Num(132, 135, 0);
            for (var q = 0; q < 4; q++) r.ImporteConSigno(136 + 32 * q, 151 + 32 * q, x.Trimestres[q]).ImporteConSigno(152 + 32 * q, 167 + 32 * q, 0m);
            r.An(264, 280, "").Texto(282, x.InversionSujetoPasivo ? "X" : " ").ImporteConSigno(284, 299, 0m).Num(300, 305, 0);
            return r.ToString();
        }).ToList();
        // Registros de inmueble (tipo de hoja «I»): un local alquilado por arrendatario, con lo facturado en el año.
        t2.AddRange((d.Inmuebles ?? []).Select(a =>
        {
            var r = new RegistroInformativo()
                .Texto(1, "2").Texto(2, Modelo).Num(5, 8, d.Ejercicio).Nif(9, d.Declarante.Nif).Nif(18, a.Nif).An(36, 75, a.Nombre).Texto(76, "I")
                .ImporteConSigno(99, 114, a.Total).Num(115, 115, a.Situacion347).An(116, 140, a.Inmueble.ReferenciaCatastral);
            a.Inmueble.Direccion.Escribir(r, 141);
            return r.ToString();
        }));
        return (t1.ToString(), t2);
    }
}
