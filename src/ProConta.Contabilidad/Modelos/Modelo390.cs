using System.Globalization;
using ProConta.Contabilidad.Iva;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Una actividad de los datos estadísticos del 390 (página 1, apartado 3): descripción, clave de actividad (A01, A03…) y epígrafe del IAE.</summary>
public sealed record Actividad390(string Descripcion, string Clave, string Epigrafe);

/// <summary>
/// Datos de la sociedad que el 390 pide y el programa no guarda (sin cambio de esquema): las actividades (la primera es la principal) y
/// el CNAE de la prorrata. Los da quien pide la declaración (la pantalla de modelos); sin ellos no se ofrece presentar.
/// </summary>
public sealed record DatosEstadisticos390(IReadOnlyList<Actividad390> Actividades, string Cnae, bool DeclaraOperacionesTerceros = true);

/// <summary>Declaración anterior del ejercicio para una sustitutiva: su número de justificante.</summary>
public sealed record Sustitutiva390(string JustificanteAnterior);

/// <summary>
/// El 390 de un ejercicio: casillas (número → importe), textos de la página 1 y de la prorrata, si es sustitutiva y lo que impide
/// presentarlo o conviene revisar. Es una declaración informativa: no tiene forma de pago.
/// </summary>
public sealed record Declaracion390(int Ejercicio, Declarante303 Declarante, IReadOnlyDictionary<int, decimal> Casillas, DatosEstadisticos390? Datos,
    string TipoProrrata, Sustitutiva390? Sustitutiva, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos)
{
    public decimal Casilla(int numero) => Casillas.TryGetValue(numero, out var v) ? v : 0m;

    /// <summary>Resultado de la liquidación anual (casilla 86).</summary>
    public decimal Resultado => Casilla(86);

    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Modelo 390 de un ejercicio (contabilidad/modelos-tributarios/modelo-390), sin BD, desde las líneas de IVA del año, el cálculo del 303
/// (<see cref="IvaEjercicio"/>, con la regularización de la prorrata del 4T) y los cuatro 303 (<see cref="Declaracion303"/>), con el
/// diseño de registro de la AEAT (sources/aeat/DR390e2026v100.xlsx).
/// <list type="bullet">
/// <item>Devengado por tipo (4, 10 y 21 %): régimen ordinario (01-06), adquisiciones intracomunitarias de bienes (21-26) y de servicios
/// (545-552), inversión del sujeto pasivo (27/28), modificación de bases y cuotas (29/30), total (33/34 = Σ 27 de los cuatro 303).</item>
/// <item>Deducible por tipo y grupo, con la prorrata provisional redondeada por grupo y trimestre como el 303; la diferencia de redondeo
/// de cada casilla del 303 se lleva al tipo con más cuota, para que cada total cuadre con la suma de los cuatro 303: interiores
/// corrientes (48/49 = Σ 28/29), interiores de inversión (50/51), importaciones (52-55), intracomunitarias de bienes corrientes (56/57),
/// de inversión (58/59) y de servicios (597/598; 56 + 597 = Σ 36); 522 la regularización de la prorrata (casilla 44 del 4T); 64 suma de
/// deducciones (= Σ 45); 65 resultado del régimen general (= Σ 46).</item>
/// <item>Resultado anual: 84 = 65; 85 cuotas del ejercicio anterior compensadas (lo aplicado en el año, como mucho lo pendiente al
/// empezar); 86 = 84 − 85; territorio común 100 % (87); 95 lo que salió a ingresar en los 303; 97 lo que el 4T deja a compensar.</item>
/// <item>Volumen de operaciones (99-108), operaciones específicas (230, 109, 231 y 232: bases cuyo IVA no se dedujo) y la prorrata
/// (115 operaciones, 116 con derecho a deducir, 118 la aplicada: la definitiva si está fijada y, si no, la real).</item>
/// </list>
/// </summary>
public static class Modelo390
{
    public const string Modelo = "390";

    /// <summary>Tipos de IVA con casillas por tipo en este programa.</summary>
    public static readonly IReadOnlyList<decimal> Tipos = [4m, 10m, 21m];

    /// <summary>Grupos de lo deducible del 390 y sus casillas (base, cuota) por tipo 4, 10 y 21 %, con el total.</summary>
    internal sealed record Grupo(string Clave, string Clave303, (int B, int C)[] PorTipo, (int B, int C) Total);

    internal static readonly IReadOnlyList<Grupo> GruposDeducible =
    [
        new("intCorr", Calculo303.ClaveInterioresCorrientes, [(190, 191), (603, 604), (605, 606)], (48, 49)),
        new("intInv", Calculo303.ClaveInterioresInversion, [(196, 197), (611, 612), (613, 614)], (50, 51)),
        new("impCorr", Calculo303.ClaveImportacionesCorrientes, [(202, 203), (619, 620), (621, 622)], (52, 53)),
        new("impInv", Calculo303.ClaveImportacionesInversion, [(208, 209), (623, 624), (625, 626)], (54, 55)),
        new("intraBienCorr", Calculo303.ClaveIntracomunitariasCorrientes, [(214, 215), (627, 628), (629, 630)], (56, 57)),
        new("intraInv", Calculo303.ClaveIntracomunitariasInversion, [(220, 221), (631, 632), (633, 634)], (58, 59)),
        new("intraServ", Calculo303.ClaveIntracomunitariasCorrientes, [(587, 588), (635, 636), (637, 638)], (597, 598)),
    ];

    private static readonly (int B, int C)[] Ordinario = [(1, 2), (3, 4), (5, 6)];
    private static readonly (int B, int C)[] IntraBienes = [(21, 22), (23, 24), (25, 26)];
    private static readonly (int B, int C)[] IntraServicios = [(545, 546), (547, 548), (551, 552)];

    /// <summary>Claves de operación que no son «exentas sin derecho a deducir» (casilla 105).</summary>
    private static readonly IReadOnlySet<string> NoExentasSinDerecho = new HashSet<string>(StringComparer.Ordinal) { "E2", "E3", "E4", "E5", "N1", "N2", "S2" };

    /// <summary>Casillas que el programa calcula, en el orden del modelo, con su concepto (para la pantalla y el contraste con el PDF).</summary>
    public static readonly IReadOnlyList<(int Casilla, string Concepto)> Conceptos =
    [
        (1, "Régimen ordinario 4 % · base"), (2, "Régimen ordinario 4 % · cuota"), (3, "Régimen ordinario 10 % · base"), (4, "Régimen ordinario 10 % · cuota"),
        (5, "Régimen ordinario 21 % · base"), (6, "Régimen ordinario 21 % · cuota"), (25, "Adquisiciones intracomunitarias de bienes 21 % · base"),
        (26, "Adquisiciones intracomunitarias de bienes 21 % · cuota"), (551, "Adquisiciones intracomunitarias de servicios 21 % · base"),
        (552, "Adquisiciones intracomunitarias de servicios 21 % · cuota"), (27, "Inversión del sujeto pasivo · base"), (28, "Inversión del sujeto pasivo · cuota"),
        (29, "Modificación de bases y cuotas · base"), (30, "Modificación de bases y cuotas · cuota"), (33, "Total bases devengadas"), (34, "Total cuotas devengadas"),
        (48, "Interiores corrientes · base"), (49, "Interiores corrientes · cuota"), (50, "Interiores bienes de inversión · base"), (51, "Interiores bienes de inversión · cuota"),
        (52, "Importaciones corrientes · base"), (53, "Importaciones corrientes · cuota"), (54, "Importaciones bienes de inversión · base"), (55, "Importaciones bienes de inversión · cuota"),
        (56, "Intracomunitarias bienes corrientes · base"), (57, "Intracomunitarias bienes corrientes · cuota"), (58, "Intracomunitarias bienes de inversión · base"),
        (59, "Intracomunitarias bienes de inversión · cuota"), (597, "Intracomunitarias servicios · base"), (598, "Intracomunitarias servicios · cuota"),
        (522, "Regularización por la prorrata definitiva"), (64, "Suma de deducciones"), (65, "Resultado régimen general"), (84, "Suma de resultados"),
        (85, "Compensación de cuotas del ejercicio anterior"), (86, "Resultado de la liquidación"), (95, "Total a ingresar en las autoliquidaciones del ejercicio"),
        (97, "Resultado del último periodo a compensar"), (662, "Cuotas pendientes de compensación generadas en el ejercicio"),
        (99, "Volumen · régimen general"), (103, "Volumen · entregas intracomunitarias"), (104, "Volumen · exportaciones"), (105, "Volumen · exentas sin derecho a deducción"),
        (110, "Volumen · no sujetas por localización"), (125, "Volumen · sujetas con inversión del sujeto pasivo"), (108, "Total volumen de operaciones"),
        (230, "Adquisiciones interiores exentas"), (109, "Adquisiciones intracomunitarias exentas"), (231, "Importaciones exentas"),
        (232, "Bases del IVA soportado no deducible"), (115, "Prorrata · importe de operaciones"), (116, "Prorrata · operaciones con derecho a deducción"),
        (118, "Prorrata · porcentaje"),
    ];

    /// <param name="lineas">Las líneas de IVA del ejercicio.</param>
    /// <param name="iva">El 303 y la prorrata del ejercicio (de <see cref="CalculoIvaEjercicio"/>).</param>
    /// <param name="trimestres">Los cuatro 303 del ejercicio (de <see cref="Modelo303.Calcular"/>), para lo compensado y lo ingresado.</param>
    /// <param name="esBienIntracomunitario">Si una adquisición intracomunitaria es de bienes (si no, de servicios).</param>
    public static Declaracion390 Calcular(IReadOnlyList<LineaIva> lineas, IvaEjercicio iva, IReadOnlyList<Declaracion303> trimestres, int ejercicio, Declarante303 declarante,
        DatosEstadisticos390? datos, Func<LineaIva, bool> esBienIntracomunitario, Sustitutiva390? sustitutiva = null)
    {
        var r = iva.Modelo303;
        var p = r.ProrrataProvisional;
        var c = new SortedDictionary<int, decimal>();
        var errores = new List<string>();
        var avisos = new List<string>();
        void Poner(int casilla, decimal v) => c[casilla] = c.GetValueOrDefault(casilla) + v;
        var delAno = lineas.Where(l => l.Trimestre is >= 1 and <= 4).ToList();
        int IndiceTipo(decimal t) => Tipos.ToList().IndexOf(t);

        // ---------- devengado ----------
        foreach (var l in delAno.Where(l => Calculo303.EsRepercutido(l) && !l.Rectificativa && l.TipoIva > 0m))
        {
            var i = IndiceTipo(l.TipoIva);
            if (i < 0) { errores.Add($"Hay IVA repercutido al {l.TipoIva:0.##} % y el programa solo genera el 390 con el 4, el 10 y el 21 %: revisa la factura {l.Factura} de {l.Tercero}."); continue; }
            Poner(Ordinario[i].B, l.Base); Poner(Ordinario[i].C, l.Cuota);
        }
        foreach (var l in delAno.Where(Calculo303.EsIntracomunitaria))
        {
            var i = IndiceTipo(l.TipoIva);
            if (i < 0) { if (l.Base != 0m || l.Cuota != 0m) errores.Add($"Hay una adquisición intracomunitaria al {l.TipoIva:0.##} % ({l.Factura} de {l.Tercero}) y el programa solo tiene casillas del 4, el 10 y el 21 %."); continue; }
            var cas = esBienIntracomunitario(l) ? IntraBienes[i] : IntraServicios[i];
            Poner(cas.B, l.Base); Poner(cas.C, l.Cuota);
        }
        var isp = r.Linea(Calculo303.ClaveIsp);
        Poner(27, isp.En(0).Base); Poner(28, isp.En(0).Cuota);
        var mod = r.Linea(Calculo303.ClaveModificacion);
        Poner(29, mod.En(0).Base); Poner(30, mod.En(0).Cuota);
        var basesDev = new[] { 1, 3, 5, 21, 23, 25, 545, 547, 551, 27, 29 };
        c[33] = basesDev.Sum(n => c.GetValueOrDefault(n));
        c[34] = basesDev.Sum(n => c.GetValueOrDefault(n + 1));
        // El total de cuotas es, por construcción, la suma de los 27 de los cuatro 303 (comprobado en las pruebas).

        // ---------- deducible ----------
        for (var t = 1; t <= 4; t++)
        {
            var celdas = new Dictionary<(string Grupo, int Tipo), (decimal B, decimal C)>();
            foreach (var g in GruposDeducible)
            for (var i = 0; i < Tipos.Count; i++)
            {
                var del = delAno.Where(l => l.Trimestre == t && Calculo303.EsSoportado(l) && EnGrupo(g.Clave, l, esBienIntracomunitario) && l.TipoIva == Tipos[i]).ToList();
                var cien = del.Where(l => l.Deduccion == DeduccionIva.Deducible100).ToList();
                var pro = del.Where(l => l.Deduccion == DeduccionIva.Prorrata).ToList();
                celdas[(g.Clave, i)] = (cien.Sum(l => l.Base) + Calculo303.Redondear(pro.Sum(l => l.Base) * p), cien.Sum(l => l.Cuota) + Calculo303.Redondear(pro.Sum(l => l.Cuota) * p));
            }
            // Cada casilla del 303 manda: la diferencia de redondeo va al tipo con más cuota de sus grupos.
            foreach (var clave303 in GruposDeducible.Select(g => g.Clave303).Distinct())
            {
                var objetivo = r.Linea(clave303).En(t);
                var claves = celdas.Keys.Where(k => GruposDeducible.First(g => g.Clave == k.Grupo).Clave303 == clave303).ToList();
                var dB = objetivo.Base - claves.Sum(k => celdas[k].B);
                var dC = objetivo.Cuota - claves.Sum(k => celdas[k].C);
                if (dB == 0m && dC == 0m) continue;
                var mayor = claves.OrderByDescending(k => Math.Abs(celdas[k].C)).ThenByDescending(k => k.Tipo).First();
                celdas[mayor] = (celdas[mayor].B + dB, celdas[mayor].C + dC);
                if (Math.Abs(dB) > 0.05m || Math.Abs(dC) > 0.05m)
                    avisos.Add($"En el {t}T, lo deducible por tipos no cuadra con el 303 ({clave303}: {Euros(dC)} de cuota): se ha llevado al tipo con más cuota. Suele ser una línea de IVA con un tipo distinto del 4, el 10 o el 21 %.");
            }
            foreach (var g in GruposDeducible)
            for (var i = 0; i < Tipos.Count; i++)
            {
                var (b, q) = celdas[(g.Clave, i)];
                Poner(g.PorTipo[i].B, b); Poner(g.PorTipo[i].C, q);
            }
        }
        foreach (var g in GruposDeducible)
        {
            c[g.Total.B] = g.PorTipo.Sum(x => c.GetValueOrDefault(x.B));
            c[g.Total.C] = g.PorTipo.Sum(x => c.GetValueOrDefault(x.C));
        }
        c[62] = 0m; c[639] = 0m; c[63] = 0m;
        c[522] = r.Linea(Calculo303.ClaveRegularizacionProrrata).En(0).Cuota;
        c[64] = new[] { 49, 51, 53, 55, 57, 59, 598, 61, 661, 62, 652, 63, 522 }.Sum(n => c.GetValueOrDefault(n));
        c[65] = c[34] - c[64];

        // ---------- resultado de la liquidación anual ----------
        var porTrimestre = trimestres.OrderBy(d => d.Trimestre).ToList();
        if (porTrimestre.Count != 4) throw new ArgumentException("Hacen falta los cuatro 303 del ejercicio.", nameof(trimestres));
        c[658] = 0m;
        c[84] = c[65] + c[658];
        c[659] = 0m;
        var aplicado = porTrimestre.Sum(d => d.Casilla(78));
        var pendienteInicial = porTrimestre[0].Casilla(110);
        c[85] = Math.Min(aplicado, pendienteInicial);
        c[112] = 0m;
        c[86] = c[84] + c[659] - c[85] - c[112];
        c[87] = 100m;
        c[92] = c[84];
        c[93] = c[85];
        c[94] = c[92] + c[659] - c[93] - c[112];
        c[95] = porTrimestre.Sum(d => Math.Max(0m, d.Casilla(71)));
        c[96] = 0m; c[524] = 0m;
        var cuarto = porTrimestre[3];
        c[97] = cuarto.Casilla(71) < 0m ? -cuarto.Casilla(71) : 0m;
        c[98] = 0m;
        c[662] = Math.Max(0m, cuarto.Casilla(87) - Math.Max(0m, pendienteInicial - c[85]));
        if (c[86] != c[95] - c[97] - c[662] && Math.Abs(c[86] - (c[95] - c[97] - c[662])) > 0.05m)
            avisos.Add($"El resultado anual ({Euros(c[86])}) no es lo ingresado en los 303 ({Euros(c[95])}) menos lo que queda a compensar ({Euros(c[97] + c[662])}): " +
                "puede deberse a un 303 presentado con otro importe o a un crédito del año anterior que no se aplicó. Revisa la liquidación del IVA de cada trimestre.");

        // ---------- volumen de operaciones ----------
        var rep = delAno.Where(Calculo303.EsRepercutido).ToList();
        decimal Bases(IEnumerable<LineaIva> ls) => ls.Sum(l => l.Base);
        bool Clave(LineaIva l, params string[] claves) => l.ClaveOperacion is { } k && claves.Contains(k, StringComparer.Ordinal);
        c[99] = Bases(rep.Where(l => l.TipoIva > 0m));
        c[653] = 0m;
        c[103] = Bases(rep.Where(l => l.TipoIva == 0m && Clave(l, "E5")));
        c[104] = Bases(rep.Where(l => l.TipoIva == 0m && Clave(l, "E2", "E3", "E4")));
        c[105] = Bases(rep.Where(l => l.TipoIva == 0m && (l.ClaveOperacion is null || !NoExentasSinDerecho.Contains(l.ClaveOperacion))));
        c[110] = Bases(rep.Where(l => l.TipoIva == 0m && Clave(l, "N2")));
        c[125] = Bases(rep.Where(l => l.TipoIva == 0m && Clave(l, "S2")));
        c[106] = 0m; c[107] = 0m;
        c[108] = new[] { 99, 653, 103, 104, 105, 110, 125, 126, 127, 128, 100, 101, 102, 227, 228 }.Sum(n => c.GetValueOrDefault(n)) - c[106] - c[107];
        if (rep.Any(l => l.TipoIva == 0m && Clave(l, "N1")))
            avisos.Add("Hay ventas no sujetas (N1): no cuentan en el volumen de operaciones del 390.");

        // ---------- operaciones específicas y prorrata ----------
        var sop = delAno.Where(Calculo303.EsSoportado).ToList();
        c[230] = Bases(sop.Where(l => l.TipoIva == 0m && Calculo303.EsInterior(l)));
        c[109] = Bases(sop.Where(l => l.TipoIva == 0m && Calculo303.EsIntracomunitaria(l)));
        c[231] = Bases(sop.Where(l => l.TipoIva == 0m && Calculo303.EsImportacion(l)));
        var deducidas = GruposDeducible.Sum(g => c[g.Total.B]);
        c[232] = Math.Max(0m, Bases(sop.Where(l => l.TipoIva > 0m)) - deducidas);
        var prorrata = iva.Prorrata;
        var hayProrrata = sop.Any(l => l.Deduccion == DeduccionIva.Prorrata);
        c[115] = hayProrrata ? prorrata.Denominador : 0m;
        c[116] = hayProrrata ? prorrata.Numerador : 0m;
        c[118] = hayProrrata ? decimal.Round(prorrata.Aplicada * 100m, 2) : 0m;
        if (hayProrrata && !prorrata.DefinitivaFijada)
            avisos.Add($"La prorrata del ejercicio no está fijada: el 390 lleva la real ({c[118]:0.##} %) y el 4T del 303 no regulariza. Fíjala en Contabilidad › Prorrata de IVA antes de presentar.");

        // ---------- comprobaciones ----------
        if (string.IsNullOrWhiteSpace(declarante.Nif))
            errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        if (datos is null || datos.Actividades.Count == 0)
            errores.Add("Faltan las actividades de la sociedad (descripción, clave y epígrafe del IAE) de los datos estadísticos del 390.");
        else if (datos.Actividades.Count > 6)
            errores.Add("El 390 admite como mucho 6 actividades (la principal y 5 más).");
        if (hayProrrata && string.IsNullOrWhiteSpace(datos?.Cnae))
            errores.Add("Falta el CNAE de la actividad para la prorrata (página 7 del 390).");
        return new Declaracion390(ejercicio, declarante, c, datos, hayProrrata ? "G" : " ", sustitutiva, errores, avisos);
    }

    /// <summary>Si una línea soportada es del grupo del 390 (los grupos del 303, con las intracomunitarias corrientes separadas en bienes y servicios).</summary>
    private static bool EnGrupo(string grupo, LineaIva l, Func<LineaIva, bool> esBien) => grupo switch
    {
        "intCorr" => Calculo303.EsDeducibleInterior(l) && !l.BienInversion,
        "intInv" => Calculo303.EsDeducibleInterior(l) && l.BienInversion,
        "impCorr" => Calculo303.EsImportacion(l) && !l.BienInversion,
        "impInv" => Calculo303.EsImportacion(l) && l.BienInversion,
        "intraBienCorr" => Calculo303.EsIntracomunitaria(l) && !l.BienInversion && esBien(l),
        "intraInv" => Calculo303.EsIntracomunitaria(l) && l.BienInversion,
        "intraServ" => Calculo303.EsIntracomunitaria(l) && !l.BienInversion && !esBien(l),
        _ => false,
    };

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
