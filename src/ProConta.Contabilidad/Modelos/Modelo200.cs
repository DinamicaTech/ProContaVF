using System.Globalization;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// Datos del 200 que el programa no guarda (sin cambio de esquema; propuesta para el esquema 72): el CNAE de la actividad principal, la
/// plantilla media, la opción del 0,7 % para fines sociales, el teléfono, lo que se destina a dividendos en la aplicación del resultado y,
/// si sale a devolver, si se renuncia a la devolución.
/// </summary>
public sealed record DatosDeclaracion200(string Cnae, decimal PersonalFijo = 0m, decimal PersonalNoFijo = 0m, bool FinesSociales = false, string? Telefono = null,
    decimal Dividendos = 0m, bool RenunciaDevolucion = false, IReadOnlyList<Persona200>? Administradores = null, IReadOnlyList<TitularReal200>? TitularesReales = null,
    Persona200? Secretario = null, IReadOnlyList<Representante200>? Representantes = null);

/// <summary>Titular real (Ley 10/2010): documento (1, NIF), país de expedición, fecha de nacimiento, residencia y nacionalidad (códigos ISO de dos letras).</summary>
public sealed record TitularReal200(string Nif, string Nombre, DateTime? FechaNacimiento = null, int TipoDocumento = 1, string PaisExpedicion = "ES", string PaisResidencia = "ES",
    string Nacionalidad = "ES");

/// <summary>Representante legal de la sociedad: fecha del poder y notaría (o «otros»).</summary>
public sealed record Representante200(string Nif, string Nombre, DateTime? FechaPoder, string Notaria);

/// <summary>Persona de la página 2 del 200: administrador o socio, con su provincia (dos cifras del código postal) y, si es socio, su participación.</summary>
public sealed record Persona200(string Nif, string Nombre, bool Juridica, string? Provincia, decimal Nominal = 0m, decimal Porcentaje = 0m);

/// <summary>Lado del balance o la cuenta de pérdidas y ganancias de un saldo.</summary>
public enum Lado200 { Activo, PatrimonioNetoPasivo, PerdidasGanancias }

/// <summary>Saldo de una subcuenta a 31/12 con el signo del balance (activo, patrimonio neto y pasivo en positivo) o de la PyG (ingresos en positivo).</summary>
public sealed record SaldoCuenta200(Lado200 Lado, string Cuenta, decimal Importe);

/// <summary>Línea del detalle de correcciones al resultado contable (página 13 y cuadros de las páginas 20 bis y 26).</summary>
public enum Correccion200 { ExencionTransmisionValoresResidentes, Otras }

/// <summary>Un ajuste del Impuesto sobre Sociedades, ya clasificado en su línea del 200.</summary>
public sealed record AjusteDeclaracion200(Correccion200 Linea, bool Temporal, bool Aumento, decimal Importe, string Descripcion);

/// <summary>
/// La liquidación del Impuesto sobre Sociedades tal como la calcula el programa (contabilidad/procesos/impuesto-sociedades), con las
/// retenciones repartidas por su naturaleza y los pagos fraccionados por periodo.
/// </summary>
/// <param name="ResultadoContable">Resultado antes del impuesto (casilla 00501): el del cálculo del IS.</param>
/// <param name="TipoTramo">Tipo del tramo reducido (0,21 = 21 %); null sin tramo.</param>
/// <param name="BonificacionViviendas">Si las bonificaciones son las de arrendamiento de viviendas (casilla 00576, clave 00038); si no, van a «otras» (00569).</param>
/// <param name="PagosFraccionados">Lo ingresado en el 1P, 2P y 3P (casillas 00601, 00603 y 00605).</param>
public sealed record Liquidacion200(decimal ResultadoContable, decimal BasesNegativas, decimal BaseImponible, decimal TipoGeneral, decimal? TipoTramo,
    decimal CuotaIntegra, decimal Bonificaciones, bool BonificacionViviendas, decimal Deducciones, decimal CuotaLiquida, decimal RetencionesCapitalMobiliario,
    decimal RetencionesArrendamientos, decimal RetencionesOtras, IReadOnlyList<decimal> PagosFraccionados);

/// <summary>
/// Modelo 200 de un ejercicio: las casillas por página, las claves de la página 1 que van marcadas, la forma de declarar (I, U, N, D o R), y lo
/// que impide presentarlo o conviene revisar.
/// </summary>
public sealed record Declaracion200(int Ejercicio, Declarante303 Declarante, DatosDeclaracion200 Datos, IReadOnlyDictionary<string, IReadOnlyDictionary<int, decimal>> Casillas,
    IReadOnlySet<int> Claves, string TipoGravamen, char TipoDeclaracion, IReadOnlyList<string> Paginas, IReadOnlyList<string> Errores, IReadOnlyList<string> Avisos,
    string? JustificanteAnterior = null, IReadOnlyList<Persona200>? Socios = null)
{
    public decimal Casilla(string pagina, int casilla) => Casillas.TryGetValue(pagina, out var p) && p.TryGetValue(casilla, out var v) ? v : 0m;
    /// <summary>Resultado de la autoliquidación (casilla 01586): positivo a ingresar, negativo a devolver.</summary>
    public decimal Resultado => Casilla("14B00", 1586);
    public decimal ImporteAIngresar => TipoDeclaracion is 'I' or 'U' ? Resultado : 0m;
    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Modelo 200, Impuesto sobre Sociedades (contabilidad/modelos-tributarios/modelo-200), sin BD. El balance y la cuenta de pérdidas y
/// ganancias en el modelo PYMES (el que usa la gestoría) salen de los saldos de las subcuentas con una tabla de prefijos del PGC; la
/// liquidación, del cálculo del Impuesto sobre Sociedades del programa.
/// <list type="bullet">
/// <item>Balance (páginas 3 a 6) y PyG (7 y 8): cada subcuenta suma en su casilla y en las de los epígrafes que la contienen; los totales
/// (activo, patrimonio neto y pasivo, resultados de explotación, financiero, antes de impuestos y del ejercicio) se suman. Una subcuenta sin
/// casilla es un error: no se pierde ningún importe.</item>
/// <item>Liquidación (12, 13, 14 y 14 bis): resultado de la PyG, corrección por el gasto del impuesto, detalle de correcciones, base,
/// tipo, cuota íntegra, bonificaciones, deducciones, cuota líquida, retenciones por naturaleza, pagos fraccionados y resultado.</item>
/// <item>Cuadros de detalle de las correcciones (20 bis y 26), aplicación del resultado (20 quater) y página de ingreso o devolución.</item>
/// </list>
/// Lo que el programa no conoce (detalle de bases negativas por año, límite de gastos financieros, deducciones por año de origen) no
/// se genera: si hace falta, es un error que lo dice.
/// </summary>
public static class Modelo200
{
    public const string Modelo = "200";

    /// <summary>Casillas de los epígrafes del balance y la PyG: subcuentas (prefijos) y cadena de casillas desde la más concreta a la raíz.</summary>
    private static readonly (Lado200 Lado, string Prefijos, int[] Cadena)[] Reglas =
    [
        // Activo no corriente
        (Lado200.Activo, "20 280 290", [102, 101]),
        (Lado200.Activo, "21 23 281 291", [111, 101]),
        (Lado200.Activo, "22 282 292", [115, 101]),
        (Lado200.Activo, "240 293", [119, 118, 101]),
        (Lado200.Activo, "241 242 294 295", [125, 118, 101]),
        (Lado200.Activo, "250 259", [127, 126, 101]),
        (Lado200.Activo, "251 252 253 254 255 257 258 26 296 297 298", [133, 126, 101]),
        (Lado200.Activo, "474", [134, 101]),
        // Activo corriente
        (Lado200.Activo, "580 581 582 583 584 599", [137, 136]),
        (Lado200.Activo, "30 31 32 33 34 35 36 39 407", [138, 136]),
        (Lado200.Activo, "430 431 432 435 436 437 490 493", [152, 150, 149, 136]),
        (Lado200.Activo, "5580", [158, 149, 136]),
        (Lado200.Activo, "44 460 470 471 472 473 544", [159, 149, 136]),
        (Lado200.Activo, "530 539", [161, 160, 136]),
        (Lado200.Activo, "531 532 533 534 535 536", [167, 160, 136]),
        (Lado200.Activo, "540 549", [169, 168, 136]),
        (Lado200.Activo, "541 542 543 545 546 547 548 551 552 553 565 566 59", [175, 168, 136]),
        (Lado200.Activo, "480 567", [176, 136]),
        (Lado200.Activo, "57", [177, 136]),
        // Patrimonio neto
        (Lado200.PatrimonioNetoPasivo, "100 101 102", [188, 187, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "1030 1040", [189, 187, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "110", [190, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "112 113 114 115 119", [193, 191, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "108 109", [194, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "120 121", [195, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "118", [198, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "129 6 7", [199, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "557", [200, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "111", [201, 186, 185]),
        (Lado200.PatrimonioNetoPasivo, "133 1340 137", [208, 185]),
        (Lado200.PatrimonioNetoPasivo, "130 131 132", [209, 185]),
        // Pasivo no corriente
        (Lado200.PatrimonioNetoPasivo, "14", [211, 210]),
        (Lado200.PatrimonioNetoPasivo, "1605 170", [218, 216, 210]),
        (Lado200.PatrimonioNetoPasivo, "1625 174", [219, 216, 210]),
        (Lado200.PatrimonioNetoPasivo, "15 1615 1635 171 172 173 175 176 177 178 179 180 185 189", [222, 216, 210]),
        (Lado200.PatrimonioNetoPasivo, "1603 1604 1613 1614 1623 1624 1633 1634", [223, 210]),
        (Lado200.PatrimonioNetoPasivo, "479", [224, 210]),
        (Lado200.PatrimonioNetoPasivo, "181", [225, 210]),
        // Pasivo corriente
        (Lado200.PatrimonioNetoPasivo, "585 586 587 588 589", [229, 228]),
        (Lado200.PatrimonioNetoPasivo, "499 529", [230, 228]),
        (Lado200.PatrimonioNetoPasivo, "5105 520 527", [233, 231, 228]),
        (Lado200.PatrimonioNetoPasivo, "5125 524", [234, 231, 228]),
        (Lado200.PatrimonioNetoPasivo, "500 501 505 506 509 5115 5135 5145 521 522 523 525 526 528 551 5525 555 5565 5566 5595 5598 560 561 569", [237, 231, 228]),
        (Lado200.PatrimonioNetoPasivo, "5103 5104 5113 5114 5123 5124 5133 5134 5143 5144 5523 5524 5563 5564", [238, 228]),
        (Lado200.PatrimonioNetoPasivo, "40", [242, 240, 239, 228]),
        (Lado200.PatrimonioNetoPasivo, "41 438 465 466 47", [249, 239, 228]),
        (Lado200.PatrimonioNetoPasivo, "485 568", [250, 228]),
        // Pérdidas y ganancias: explotación
        (Lado200.PerdidasGanancias, "700 701 702 703 704 705 706 708 709", [255]),
        (Lado200.PerdidasGanancias, "6930 71 7930", [258]),
        (Lado200.PerdidasGanancias, "73", [259]),
        (Lado200.PerdidasGanancias, "600 6060 6080 6090", [760, 261, 260]),
        (Lado200.PerdidasGanancias, "610", [761, 261, 260]),
        (Lado200.PerdidasGanancias, "601 602 6061 6062 6081 6082 6091 6092", [762, 262, 260]),
        (Lado200.PerdidasGanancias, "611 612", [763, 262, 260]),
        (Lado200.PerdidasGanancias, "607", [263, 260]),
        (Lado200.PerdidasGanancias, "6931 6932 6933 7931 7932 7933", [264, 260]),
        (Lado200.PerdidasGanancias, "752", [267, 266, 265]),
        (Lado200.PerdidasGanancias, "75", [268, 266, 265]),
        (Lado200.PerdidasGanancias, "740 747", [269, 265]),
        (Lado200.PerdidasGanancias, "640", [271, 270]),
        (Lado200.PerdidasGanancias, "641", [273, 270]),
        (Lado200.PerdidasGanancias, "642 643", [274, 270]),
        (Lado200.PerdidasGanancias, "644 6450", [275, 270]),
        (Lado200.PerdidasGanancias, "6457", [276, 270]),
        (Lado200.PerdidasGanancias, "649", [277, 270]),
        (Lado200.PerdidasGanancias, "623", [253, 280, 279]),
        (Lado200.PerdidasGanancias, "62", [254, 280, 279]),
        (Lado200.PerdidasGanancias, "631 634 636 639", [281, 279]),
        (Lado200.PerdidasGanancias, "650 694 695 794 7954", [282, 279]),
        (Lado200.PerdidasGanancias, "651 659", [283, 279]),
        (Lado200.PerdidasGanancias, "68", [284]),
        (Lado200.PerdidasGanancias, "746", [285]),
        (Lado200.PerdidasGanancias, "7951 7952 7955 7956", [286]),
        (Lado200.PerdidasGanancias, "690 691 692", [289, 288, 287]),
        (Lado200.PerdidasGanancias, "790 791 792", [290, 288, 287]),
        (Lado200.PerdidasGanancias, "770 771 772", [292, 291, 287]),
        (Lado200.PerdidasGanancias, "670 671 672", [293, 291, 287]),
        (Lado200.PerdidasGanancias, "774", [294]),
        (Lado200.PerdidasGanancias, "678 778", [295]),
        // Pérdidas y ganancias: financiero e impuesto
        (Lado200.PerdidasGanancias, "760", [300, 298, 297]),
        (Lado200.PerdidasGanancias, "761 762 767 769", [303, 301, 297]),
        (Lado200.PerdidasGanancias, "660", [308, 305]),
        (Lado200.PerdidasGanancias, "661 662 664 665 669", [307, 305]),
        (Lado200.PerdidasGanancias, "663 763", [309]),
        (Lado200.PerdidasGanancias, "668 768", [312]),
        (Lado200.PerdidasGanancias, "696 697 698 699", [316, 314, 313]),
        (Lado200.PerdidasGanancias, "796 797 798 799", [318, 314, 313]),
        (Lado200.PerdidasGanancias, "666 667 673 675", [323, 319, 313]),
        (Lado200.PerdidasGanancias, "766 773 775", [321, 319, 313]),
        (Lado200.PerdidasGanancias, "630 633 638", [326]),
    ];

    /// <summary>Gastos financieros netos deducibles en todo caso (art. 16.1 LIS).</summary>
    public const decimal LimiteGastosFinancieros = 1_000_000m;

    /// <summary>
    /// NIF para el 200: sin espacios ni guiones, en mayúsculas y, si falta la letra de un DNI (8 cifras) o un NIE (X, Y o Z y 7 cifras),
    /// con la letra que le corresponde.
    /// </summary>
    public static string Nif(string? nif)
    {
        var n = new string((nif ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()).ToUpperInvariant();
        const string letras = "TRWAGMYFPDXBNJZSQVHLCKE";
        if (n.Length == 8 && n.All(char.IsAsciiDigit)) return n + letras[int.Parse(n, CultureInfo.InvariantCulture) % 23];
        if (n.Length == 8 && n[0] is 'X' or 'Y' or 'Z' && n[1..].All(char.IsAsciiDigit))
            return n + letras[int.Parse("XYZ".IndexOf(n[0]) + n[1..], CultureInfo.InvariantCulture) % 23];
        return n;
    }

    private static readonly int[] RaicesExplotacion = [255, 258, 259, 260, 265, 270, 279, 284, 285, 286, 287, 294, 295];
    private static readonly int[] RaicesFinanciero = [297, 304, 305, 309, 312, 313, 329];

    /// <summary>Páginas que genera el programa, en el orden del diseño (el fichero tiene que respetarlo).</summary>
    public static readonly IReadOnlyList<string> PaginasGeneradas =
        ["01000", "01B00", "02000", "02B00", "03000", "04000", "05000", "06000", "07000", "08000", "12000", "13000", "14000", "14B00", "20000", "20B00", "20D00", "26E00", "26G00", "DID00"];

    /// <summary>Casilla del balance o la PyG de una subcuenta (la más concreta) y sus epígrafes; null si no tiene.</summary>
    public static int[]? CadenaDe(Lado200 lado, string cuenta)
    {
        int[]? mejor = null;
        var largo = 0;
        foreach (var (l, prefijos, cadena) in Reglas)
        {
            if (l != lado) continue;
            foreach (var p in prefijos.Split(' '))
                if (p.Length > largo && cuenta.StartsWith(p, StringComparison.Ordinal)) { mejor = cadena; largo = p.Length; }
        }
        return mejor;
    }

    /// <summary>Página del balance o la PyG (03000 a 08000) en la que está una casilla.</summary>
    internal static string PaginaBalance(int casilla) => casilla switch
    {
        >= 101 and <= 148 or 700 or 701 => "03000",
        >= 149 and <= 180 => "04000",
        >= 185 and <= 227 or 764 or 765 or 702 or 712 or 766 or 767 or 768 or 769 or >= 780 and <= 784 or 1001 or 1002 => "05000",
        >= 228 and <= 252 or >= 785 and <= 789 or 703 or 704 => "06000",
        >= 253 and <= 296 or >= 705 and <= 711 or >= 760 and <= 763 or >= 770 and <= 772 or >= 790 and <= 793 => "07000",
        _ => "08000",
    };

    /// <summary>Tipo de gravamen de la casilla 00558: cuatro cifras, «2500» con un solo tipo o «2122» con el tramo reducido (21 % y 22 %).</summary>
    public static string TipoGravamen(decimal tipoGeneral, decimal? tipoTramo)
    {
        static string D(decimal t) => decimal.Round(t * 100m, 0, MidpointRounding.AwayFromZero).ToString("00", CultureInfo.InvariantCulture);
        return tipoTramo is decimal t && t > 0m ? D(t) + D(tipoGeneral) : D(tipoGeneral) + "00";
    }

    public static Declaracion200 Calcular(int ejercicio, Declarante303 declarante, DatosDeclaracion200 datos, IReadOnlyList<SaldoCuenta200> saldos,
        IReadOnlyList<AjusteDeclaracion200> ajustes, Liquidacion200 liq, bool cifraNegociosAnteriorMenor1M, string? justificanteAnterior = null,
        IReadOnlyList<Persona200>? socios = null)
    {
        var errores = new List<string>();
        var avisos = new List<string>();
        var c = new Dictionary<string, Dictionary<int, decimal>>();
        void Sumar(string pagina, int casilla, decimal v)
        {
            if (v == 0m) return;
            if (!c.TryGetValue(pagina, out var p)) c[pagina] = p = [];
            p[casilla] = p.GetValueOrDefault(casilla) + v;
        }
        void Poner(string pagina, int casilla, decimal v)
        {
            if (!c.TryGetValue(pagina, out var p)) c[pagina] = p = [];
            if (v == 0m) p.Remove(casilla); else p[casilla] = v;
        }
        decimal V(string pagina, int casilla) => c.TryGetValue(pagina, out var p) ? p.GetValueOrDefault(casilla) : 0m;
        decimal B(int casilla) => V(PaginaBalance(casilla), casilla);

        // Balance y pérdidas y ganancias
        foreach (var s in saldos.Where(s => s.Importe != 0m))
        {
            if (CadenaDe(s.Lado, s.Cuenta) is not { } cadena)
            {
                errores.Add($"La subcuenta {s.Cuenta} ({Euros(s.Importe)}, {Texto(s.Lado)}) no tiene casilla en el 200: revisa su código.");
                continue;
            }
            foreach (var casilla in cadena) Sumar(PaginaBalance(casilla), casilla, s.Importe);
        }
        Poner("04000", 180, B(101) + B(136));
        Poner("06000", 252, B(185) + B(210) + B(228));
        if (B(180) != B(252))
            errores.Add($"El balance no cuadra: activo {Euros(B(180))} y patrimonio neto y pasivo {Euros(B(252))}. Revisa la contabilidad antes de generar el 200.");
        var explotacion = RaicesExplotacion.Sum(B);
        var financiero = RaicesFinanciero.Sum(B);
        Poner("07000", 296, explotacion);
        Poner("08000", 324, financiero);
        Poner("08000", 325, explotacion + financiero);
        Poner("08000", 327, explotacion + financiero + B(326));
        Poner("08000", 500, B(327) + B(328));
        if (B(500) != B(199))
            errores.Add($"El resultado de la cuenta de pérdidas y ganancias ({Euros(B(500))}) no es el del balance ({Euros(B(199))}).");

        // Liquidación I: resultado y corrección por el impuesto
        var resultado = B(500);
        Poner("12000", 500, resultado);
        var gastoImpuesto = -B(326);
        Poner("12000", 301, Math.Max(0m, gastoImpuesto));
        Poner("12000", 302, Math.Max(0m, -gastoImpuesto));
        var antes = resultado + gastoImpuesto;
        Poner("12000", 501, antes);
        if (antes != liq.ResultadoContable)
            avisos.Add($"El resultado antes del impuesto de la contabilidad ({Euros(antes)}) no es el del cálculo del Impuesto sobre Sociedades ({Euros(liq.ResultadoContable)}): " +
                "recalcula el impuesto y revisa los asientos del cierre.");

        // Detalle de las correcciones (página 13) y cuadros (20 bis y 26)
        foreach (var a in ajustes.Where(a => a.Importe != 0m))
        {
            var (detalle, cuadro, pagina26) = a.Linea switch
            {
                Correccion200.ExencionTransmisionValoresResidentes => (a.Aumento ? 2182 : 2183, a.Aumento ? (a.Temporal ? 2982 : 2981) : (a.Temporal ? 2987 : 2986), "26E00"),
                _ => (a.Aumento ? 413 : 414, a.Aumento ? (a.Temporal ? 3392 : 3391) : (a.Temporal ? 3397 : 3396), "26G00"),
            };
            Sumar("13000", detalle, a.Importe);
            Sumar("13000", a.Aumento ? 417 : 418, a.Importe);
            var total = a.Aumento ? (a.Temporal ? 2303 : 2301) : (a.Temporal ? 2304 : 2302);
            Sumar("20B00", total, a.Importe);
            Sumar("20B00", a.Aumento ? 417 : 418, a.Importe);
            Sumar(pagina26, cuadro, a.Importe);
            Sumar("26G00", total, a.Importe);
            if (a.Temporal)
                avisos.Add($"«{a.Descripcion}» es una corrección temporaria: el 200 pide sus saldos pendientes a principio y fin de ejercicio (página 26), que el programa no lleva. Complétalos en Sociedades WEB.");
        }

        // Liquidación III: base, cuota y bonificaciones
        var previa = antes + V("13000", 417) - V("13000", 418);
        Poner("14000", 550, previa);
        Poner("14000", 547, liq.BasesNegativas);
        Poner("14000", 552, liq.BaseImponible);
        Poner("14000", 1330, liq.BaseImponible);
        if (previa - liq.BasesNegativas != liq.BaseImponible)
            errores.Add($"La base imponible del cálculo del IS ({Euros(liq.BaseImponible)}) no es la del 200 ({Euros(previa)} − {Euros(liq.BasesNegativas)} de bases negativas): " +
                "algún ajuste del IS no está en el detalle de correcciones.");
        if (liq.BasesNegativas > 0m)
            errores.Add("El 200 pide el detalle de las bases negativas compensadas por año de origen (página 15), que el programa no lleva: complétalo en Sociedades WEB.");
        var tipo = TipoGravamen(liq.TipoGeneral, liq.TipoTramo);
        Poner("14000", 562, liq.CuotaIntegra);
        Poner("14000", liq.BonificacionViviendas ? 576 : 569, liq.Bonificaciones);
        var ajustada = Math.Max(0m, liq.CuotaIntegra - liq.Bonificaciones);
        Poner("14000", 582, ajustada);

        // Liquidación IV: deducciones, cuota líquida, retenciones, pagos fraccionados y resultado
        Poner("14B00", 583, liq.Deducciones);
        if (liq.Deducciones > 0m)
            avisos.Add("Las deducciones van en «Apoyo fiscal a la inversión y otras deducciones» (00583): su desglose por año de origen (página 16) hay que completarlo en Sociedades WEB.");
        Poner("14B00", 592, liq.CuotaLiquida);
        if (Math.Max(0m, ajustada - liq.Deducciones) != liq.CuotaLiquida)
            avisos.Add($"La cuota líquida del IS ({Euros(liq.CuotaLiquida)}) no es la cuota ajustada menos deducciones ({Euros(Math.Max(0m, ajustada - liq.Deducciones))}): está fijada a mano en el cálculo del impuesto.");
        Poner("14B00", 1785, liq.RetencionesCapitalMobiliario);
        Poner("14B00", 1787, liq.RetencionesArrendamientos);
        Poner("14B00", 1798, liq.RetencionesOtras);
        var retenciones = liq.RetencionesCapitalMobiliario + liq.RetencionesArrendamientos + liq.RetencionesOtras;
        Poner("14B00", 1766, retenciones);
        var cuotaEjercicio = liq.CuotaLiquida - retenciones;
        Poner("14B00", 599, cuotaEjercicio);
        var pagos = liq.PagosFraccionados.Concat([0m, 0m, 0m]).Take(3).ToArray();
        Poner("14B00", 601, pagos[0]);
        Poner("14B00", 603, pagos[1]);
        Poner("14B00", 605, pagos[2]);
        var diferencial = cuotaEjercicio - pagos.Sum();
        Poner("14B00", 611, diferencial);
        Poner("14B00", 1586, diferencial);
        Poner("14B00", 621, diferencial);

        // Limitación de gastos financieros (art. 16 LIS, página 20): sin los saldos pendientes de años anteriores, que el programa no lleva.
        var gastosFinancieros = -B(305);
        if (gastosFinancieros > 0m)
        {
            Poner("20000", 1245, gastosFinancieros);
            Poner("20000", 1246, gastosFinancieros);
            var netos = Math.Max(0m, gastosFinancieros);
            Poner("20000", 1248, netos);
            Poner("20000", 1250, B(296));
            Poner("20000", 1251, B(284));
            Poner("20000", 1252, B(285));
            Poner("20000", 1253, B(287));
            var limite = Math.Max(0m, decimal.Round(0.30m * (B(296) - B(284) - B(285) - B(287)), 2, MidpointRounding.AwayFromZero));
            Poner("20000", 1249, limite);
            var total = Math.Max(limite, LimiteGastosFinancieros);
            Poner("20000", 2369, total);
            Poner("20000", 1256, Math.Min(netos, total));
            Poner("20000", 1257, Math.Max(0m, netos - total));
            Poner("20000", 3585, Math.Min(netos, total));               // aplicado del ejercicio
            Poner("20000", 1214, Math.Min(netos, total));
            // Límite del beneficio operativo del ejercicio: lo que absorben los gastos netos y lo que queda para los cinco siguientes.
            var aplicadoBo = Math.Min(netos, limite);
            Poner("20000", 3588, limite - aplicadoBo); Poner("20000", 3590, limite - aplicadoBo);
            Poner("20000", 538, limite - aplicadoBo); Poner("20000", 546, limite - aplicadoBo);
            avisos.Add("Página 20: el límite del beneficio operativo no aplicado de años anteriores no lo lleva el programa (solo el de este año): complétalo en Sociedades WEB.");
            if (netos > LimiteGastosFinancieros)
                avisos.Add("Los gastos financieros netos superan 1 M€: revisa en Sociedades WEB la limitación del art. 16 LIS con los importes pendientes de años anteriores.");
        }

        // Aplicación del resultado (20 quater)
        if (resultado > 0m)
        {
            Poner("20D00", 650, resultado);
            Poner("20D00", 653, resultado);
            var dividendos = Math.Min(datos.Dividendos, resultado);
            Poner("20D00", 656, dividendos);
            Poner("20D00", 1522, resultado - dividendos);
            Poner("20D00", 654, resultado - dividendos);
            Poner("20D00", 666, resultado);
        }
        else if (resultado < 0m)
            avisos.Add("Con pérdidas, la aplicación del resultado (página 20 quater) no se genera: complétala en Sociedades WEB si la junta acordó algo distinto de dejarlas pendientes.");

        // Página de ingreso o devolución
        Poner("DID00", 552, liq.BaseImponible);
        Poner("DID00", 562, liq.CuotaIntegra);
        Poner("DID00", 1586, diferencial);
        Poner("DID00", 621, diferencial);

        // Claves de la página 1
        var claves = new SortedSet<int>();
        if (liq.BonificacionViviendas && liq.Bonificaciones > 0m) claves.Add(38);
        if (cifraNegociosAnteriorMenor1M && liq.TipoTramo is > 0m) claves.Add(88);
        if (datos.FinesSociales) claves.Add(73);
        if (liq.BaseImponible <= 0m) claves.Add(27);

        var tipoDeclaracion = diferencial > 0m ? (string.IsNullOrWhiteSpace(declarante.Iban) ? 'I' : 'U')
            : diferencial < 0m ? (datos.RenunciaDevolucion ? 'R' : 'D') : 'N';
        if (tipoDeclaracion == 'D' && string.IsNullOrWhiteSpace(declarante.Iban))
            errores.Add("El 200 sale a devolver y la sociedad no tiene IBAN: escríbelo en Sociedades para que la AEAT ingrese la devolución.");
        if (string.IsNullOrWhiteSpace(datos.Cnae) || datos.Cnae.Trim().Length != 4 || !datos.Cnae.Trim().All(char.IsAsciiDigit))
            errores.Add("Falta el código CNAE-2025 de la actividad principal (cuatro cifras).");
        if (declarante.Nif.Length != 9) errores.Add("La sociedad no tiene NIF.");
        if ((datos.Administradores ?? []).Count == 0) errores.Add("Faltan los administradores de la sociedad (página 2, apartado A).");
        if ((datos.Administradores ?? []).Count > 5) avisos.Add("El fichero lleva los cinco primeros administradores: el resto, en Sociedades WEB.");
        if ((datos.TitularesReales ?? []).Count == 0) errores.Add("Faltan los titulares reales de la sociedad (página 2 bis, apartado F).");
        if (datos.Secretario is null) errores.Add("Falta el secretario del consejo o quien haga sus funciones (página 2 bis, apartado G).");
        if ((datos.Representantes ?? []).Count == 0) errores.Add("Falta al menos un representante legal de la sociedad (página 2 bis, apartado G).");
        if ((datos.Representantes ?? []).Count > 3) avisos.Add("El fichero lleva los tres primeros representantes: el resto, en Sociedades WEB.");
        if ((socios ?? []).Count == 0) errores.Add("Faltan los socios (página 2, apartado B.2): una SL tiene que declarar al menos uno. Dalos de alta en Socios.");
        if ((socios ?? []).Count > 6) avisos.Add("El fichero lleva los seis primeros socios: el resto, en Sociedades WEB.");
        foreach (var p in (socios ?? []).Where(p => p.Provincia is null).DistinctBy(p => p.Nif))
            errores.Add($"{p.Nombre} no tiene código postal en su ficha: el 200 pide su provincia.");

        var paginas = PaginasGeneradas.Where(p => p switch
        {
            "13000" or "20B00" => V("13000", 417) != 0m || V("13000", 418) != 0m,
            "26E00" => c.ContainsKey("26E00"),
            "26G00" => c.ContainsKey("26G00"),
            "20D00" => c.ContainsKey("20D00"),
            _ => true,
        }).ToList();
        var r = c.Where(x => x.Value.Values.Any(v => v != 0m))
            .ToDictionary(x => x.Key, x => (IReadOnlyDictionary<int, decimal>)x.Value.Where(v => v.Value != 0m).ToDictionary(v => v.Key, v => v.Value));
        return new Declaracion200(ejercicio, declarante, datos, r, claves, tipo, tipoDeclaracion, paginas, errores, avisos, justificanteAnterior, socios ?? []);
    }

    private static string Texto(Lado200 lado) => lado switch
    {
        Lado200.Activo => "activo",
        Lado200.PatrimonioNetoPasivo => "patrimonio neto y pasivo",
        _ => "pérdidas y ganancias",
    };

    internal static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}
