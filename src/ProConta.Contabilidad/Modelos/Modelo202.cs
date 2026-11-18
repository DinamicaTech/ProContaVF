using System.Globalization;
using System.Text;
using ProConta.Nucleo.Identificacion;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// De dónde sale la base del pago fraccionado (modalidad del art. 40.2 LIS): el último Impuesto sobre Sociedades presentado, su cuota
/// líquida y las retenciones e ingresos a cuenta que dedujo (sin los pagos fraccionados), y cómo se ha obtenido.
/// </summary>
/// <param name="Ejercicio">Ejercicio del Impuesto sobre Sociedades que se toma.</param>
/// <param name="Origen">«Impuesto sobre Sociedades del programa» o «asiento del impuesto» (el de la gestoría).</param>
/// <param name="CuentasPagosFraccionados">Subcuentas 473 que se han tomado como pagos fraccionados (no restan).</param>
public sealed record BasePagoFraccionado(int Ejercicio, string Origen, decimal CuotaLiquida, decimal Retenciones, IReadOnlyList<string> CuentasPagosFraccionados)
{
    /// <summary>Cuota íntegra menos deducciones y bonificaciones, retenciones e ingresos a cuenta (casilla 599 del modelo 200); 0 si sale negativa.</summary>
    public decimal Base => Math.Max(0m, CuotaLiquida - Retenciones);
}

/// <summary>Datos de cabecera del 202 que el programa no guarda (sin cambio de esquema): el CNAE de la actividad principal.</summary>
public sealed record DatosActividad202(string Cnae);

/// <summary>Declaración anterior del mismo periodo para una complementaria: su justificante y lo que salió a ingresar (casilla 02).</summary>
public sealed record Rectificativa202(string JustificanteAnterior, decimal IngresadoAnterior);

/// <summary>
/// El 202 de un periodo (1P abril, 2P octubre, 3P diciembre) en la modalidad del art. 40.2 LIS: base (01), resultado de la anterior si es
/// complementaria (02) y a ingresar (03 = Redondear(18 % × 01, 2) − 02), con la forma de pago y los datos de la cabecera.
/// </summary>
public sealed record Declaracion202(int Ejercicio, int Periodo, Declarante303 Declarante, IReadOnlyDictionary<int, decimal> Casillas, char TipoDeclaracion,
    Rectificativa202? Rectificativa, string Cnae, bool CifraNegociosMenor1M, string TipoGravamen, BasePagoFraccionado? Origen, IReadOnlyList<string> Errores,
    IReadOnlyList<string> Avisos)
{
    public string Codigo => $"{Periodo}P";
    public decimal Casilla(int n) => Casillas.TryGetValue(n, out var v) ? v : 0m;
    public decimal Resultado => Casilla(3);
    public decimal Pago => Casilla(3) + Casilla(2);
    public decimal ImporteAIngresar => TipoDeclaracion is 'U' or 'I' ? Casilla(3) : 0m;
    public bool Valida => Errores.Count == 0;
}

/// <summary>
/// Modelo 202, pagos fraccionados del Impuesto sobre Sociedades (contabilidad/modelos-tributarios/modelo-202), sin BD, en la modalidad
/// del art. 40.2 LIS, la que usa la sociedad: el 18 % de la base, que es la cuota líquida del último Impuesto sobre Sociedades
/// presentado menos sus retenciones e ingresos a cuenta (casilla 599 del 200). En abril (1P) el último presentado es el de dos
/// ejercicios antes; en octubre (2P) y diciembre (3P), el del anterior (plazo del 200: 25 de julio). La modalidad del art. 40.3 cabe en
/// el diseño (sus casillas van a cero) pero no se calcula.
/// </summary>
public static class Modelo202
{
    public const string Modelo = "202";
    public const decimal Porcentaje = 0.18m;

    public static readonly IReadOnlyList<(int Casilla, string Concepto)> Conceptos =
        [(1, "Base del pago fraccionado (art. 40.2 LIS)"), (2, "Resultado de la declaración anterior (complementaria)"), (3, "A ingresar")];

    /// <summary>Ejercicio del Impuesto sobre Sociedades del que sale la base de cada periodo.</summary>
    public static int EjercicioBase(int ejercicio, int periodo) => periodo == 1 ? ejercicio - 2 : ejercicio - 1;

    /// <summary>Primer y último día del plazo del periodo (1 al 20 de abril, octubre y diciembre).</summary>
    public static (DateTime Desde, DateTime Hasta) Plazo(int ejercicio, int periodo) => periodo switch
    {
        1 => (new DateTime(ejercicio, 4, 1), new DateTime(ejercicio, 4, 20)),
        2 => (new DateTime(ejercicio, 10, 1), new DateTime(ejercicio, 10, 20)),
        3 => (new DateTime(ejercicio, 12, 1), new DateTime(ejercicio, 12, 20)),
        _ => throw new ArgumentOutOfRangeException(nameof(periodo), "El periodo va de 1 a 3."),
    };

    /// <summary>La AEAT admite domiciliar hasta 5 días antes del fin del plazo: del 1 al 15 del mes.</summary>
    public static (DateTime Desde, DateTime Hasta) PlazoDomiciliacion(int ejercicio, int periodo) => (Plazo(ejercicio, periodo).Desde, Plazo(ejercicio, periodo).Desde.AddDays(14));

    public static Declaracion202 Calcular(int ejercicio, int periodo, Declarante303 declarante, BasePagoFraccionado? origen, DatosActividad202? datos, bool cifraNegociosMenor1M,
        string tipoGravamen, Rectificativa202? rectificativa = null, bool conNrc = false)
    {
        if (periodo is < 1 or > 3) throw new ArgumentOutOfRangeException(nameof(periodo), "El periodo va de 1 a 3.");
        var c = new SortedDictionary<int, decimal>();
        var errores = new List<string>();
        var avisos = new List<string>();
        c[1] = origen?.Base ?? 0m;
        c[2] = rectificativa is null ? 0m : Math.Max(0m, rectificativa.IngresadoAnterior);
        c[3] = decimal.Round(c[1] * Porcentaje, 2, MidpointRounding.AwayFromZero) - c[2];
        var tipo = c[3] > 0m ? (conNrc ? 'I' : 'U') : 'N';
        if (origen is null)
            errores.Add($"No hay Impuesto sobre Sociedades de {EjercicioBase(ejercicio, periodo)} en el programa ni su asiento en la contabilidad: sin él no se puede calcular la base del pago fraccionado. " +
                $"Contabiliza el impuesto de {EjercicioBase(ejercicio, periodo)} en Contabilidad › Impuesto sobre Sociedades (o el asiento de la gestoría).");
        else if (origen.CuotaLiquida - origen.Retenciones <= 0m)
            avisos.Add($"El Impuesto sobre Sociedades de {origen.Ejercicio} salió a {Euros(origen.CuotaLiquida - origen.Retenciones)} después de retenciones: no hay pago fraccionado (declaración negativa).");
        if (c[3] < 0m)
            errores.Add($"La complementaria daría {Euros(c[3])}: es menos que lo ingresado en la anterior ({Euros(c[2])}). Una complementaria solo puede subir el pago; para bajarlo hay que pedir la rectificación en la Sede.");
        if (rectificativa is not null && c[3] == 0m)
            errores.Add("La complementaria daría lo mismo que la anterior: la AEAT no admite una complementaria sin más importe a ingresar.");
        if (string.IsNullOrWhiteSpace(declarante.Nif)) errores.Add("La sociedad no tiene NIF: escríbelo en Configuración › Parámetros y tablas › Empresas.");
        if (string.IsNullOrWhiteSpace(datos?.Cnae) || datos!.Cnae.Trim().Length != 4 || !datos.Cnae.Trim().All(char.IsAsciiDigit))
            errores.Add("Falta el CNAE de la actividad principal (4 cifras) de la cabecera del 202.");
        if (tipo == 'U')
        {
            if (string.IsNullOrWhiteSpace(declarante.Iban))
                errores.Add($"El 202 sale a ingresar ({Euros(c[3])}) y la sociedad no tiene cuenta de domiciliación: escríbela en Configuración › Parámetros y tablas › Empresas («IBAN de domiciliación»).");
            else if (!Ibanes.EsValido(declarante.Iban))
                errores.Add($"El IBAN de domiciliación de la sociedad ({declarante.Iban}) no es válido: corrígelo en Configuración › Parámetros y tablas › Empresas.");
        }
        return new Declaracion202(ejercicio, periodo, declarante, c, tipo, rectificativa, datos?.Cnae?.Trim() ?? "", cifraNegociosMenor1M, tipoGravamen, origen, errores, avisos);
    }

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";
}

/// <summary>
/// Fichero del 202 con el diseño de registro de la AEAT (sources/aeat/DR202v13.xlsx, versión 1.3): cabecera &lt;T2020EEEEPP0000&gt;
/// con &lt;AUX&gt;, página 01 (identificación, datos adicionales y liquidación del 40.2; las casillas del 40.3 a cero) y página 02
/// (liquidación del 40.3 a cero, información adicional en blanco, complementaria y domiciliación), y el cierre.
/// </summary>
public static class Fichero202
{
    public const int LongitudCabecera = 328, LongitudPagina01 = 700, LongitudPagina02 = 900;

    public static string Generar(Declaracion202 d, ProgramaAeat programa)
    {
        var ej = d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture);
        var r = new StringBuilder();
        r.Append("<T2020").Append(ej).Append(d.Codigo).Append("0000>");
        r.Append("<AUX>").Append(' ', 70).Append(Fichero303.An(programa.Version, 4)).Append(' ', 4).Append(Fichero303.An(programa.NifDesarrollo, 9)).Append(' ', 213).Append("</AUX>");
        Comprobar(r.Length, LongitudCabecera, "la cabecera");
        r.Append(Pagina01(d)).Append(Pagina02(d));
        r.Append("</T2020").Append(ej).Append(d.Codigo).Append("0000>");
        return r.ToString();
    }

    public static string? LeerNif(string fichero)
    {
        var i = fichero.IndexOf("<T20201000>", StringComparison.Ordinal);
        return i < 0 || fichero.Length < i + 22 ? null : fichero.Substring(i + 13, 9).Trim();
    }

    private static string Pagina01(Declaracion202 d)
    {
        var p = new StringBuilder("<T20201000>");
        p.Append(' ');                                              // 12 reservado
        p.Append(d.TipoDeclaracion);                                // 13
        p.Append(Fichero303.An(d.Declarante.Nif, 9));               // 14
        p.Append(Fichero303.An(d.Declarante.RazonSocial, 60));      // 23
        p.Append(' ', 20);                                          // 83 nombre
        p.Append(' ');                                              // 103 territorio foral
        p.Append(d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture)); // 104
        p.Append(d.Codigo);                                         // 108
        p.Append("0101").Append(d.Ejercicio.ToString("0000", CultureInfo.InvariantCulture)); // 110 inicio del periodo impositivo (año natural)
        p.Append(Fichero303.An(d.Cnae, 4));                         // 118 CNAE
        p.Append(' ', 8);                                           // 122-129 regímenes especiales: no
        p.Append(d.CifraNegociosMenor1M ? 'X' : ' ');               // 130 cifra de negocios del periodo anterior < 1 M€
        p.Append(' ');                                              // 131 más de un tipo impositivo: no
        p.Append(Fichero303.An(d.TipoGravamen, 15));                // 132 tipo de gravamen
        p.Append('0');                                              // 147 cifra de negocios ≥ 10 M€: no consta
        p.Append('A');                                              // 148 modalidad del art. 40.2
        p.Append(Fichero303.Num(d.Casilla(1))).Append(Fichero303.Num(d.Casilla(2))).Append(Fichero303.Num(d.Casilla(3))); // 149 [01], 166 [02], 183 [03]
        p.Append(Fichero303.N(0m));                                 // 200 [04]
        for (var i = 0; i < 8; i++) p.Append(Fichero303.Num(0m));   // 217-336 [05]-[39]
        p.Append(Fichero303.N(0m));                                 // 353 [13]
        for (var i = 0; i < 4; i++) p.Append(Fichero303.Num(0m));   // 370-421 [44], [14], reserva de nivelación
        p.Append(' ', 251);                                         // 438 reservado
        p.Append("</T20201000>");
        Comprobar(p.Length, LongitudPagina01, "la página 01");
        return p.ToString();
    }

    /// <summary>Posiciones de los numéricos de la página 02 (modalidad del 40.3, a cero) hasta la 502, y cuáles son porcentajes (5) o con signo.</summary>
    private static readonly int[] Numericos02 = [13, 30, 35, 52, 69, 86, 103, 120, 137, 154, 159, 176, 193, 198, 215, 232, 237, 254, 271, 276, 293, 310, 327, 344, 361, 378, 395, 412, 417, 434, 451, 468, 485, 502];

    private static string Pagina02(Declaracion202 d)
    {
        var p = new StringBuilder("<T20202000>");
        p.Append(' ');                                              // 12 reservado
        for (var i = 0; i < Numericos02.Length - 1; i++) p.Append('0', Numericos02[i + 1] - Numericos02[i]); // 13-501: casillas del 40.3 a cero
        p.Append(' ');                                              // 502 comunicación de datos adicionales
        p.Append(' ', 22);                                          // 503 NRS
        for (var i = 0; i < 11; i++) p.Append('0', 17);             // 525-711 información adicional a cero
        p.Append(d.Rectificativa is null ? ' ' : 'X');              // 712 complementaria
        p.Append(Fichero303.An(d.Rectificativa?.JustificanteAnterior, 13)); // 713
        p.Append(Fichero303.An(d.TipoDeclaracion == 'U' ? Ibanes.Normalizar(d.Declarante.Iban) : "", 34)); // 726 IBAN
        p.Append(' ', 116).Append(' ', 13);                         // 760 reservado y 876 sello
        p.Append("</T20202000>");
        Comprobar(p.Length, LongitudPagina02, "la página 02");
        return p.ToString();
    }

    private static void Comprobar(int longitud, int esperada, string que)
    {
        if (longitud != esperada) throw new InvalidOperationException($"El fichero del 202 tiene mal {que}: {longitud} posiciones en lugar de {esperada}.");
    }
}
