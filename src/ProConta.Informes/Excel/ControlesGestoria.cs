using System.Globalization;
using ProConta.Contabilidad.Iva;

namespace ProConta.Informes.Excel;

/// <summary>Estado de un control de la hoja «Controles».</summary>
public enum EstadoControl { OK, Aviso, Revisar }

/// <summary>
/// Cómo se decide el estado de un control (la misma regla en el programa y en la fórmula de la columna Estado):
/// <list type="bullet">
/// <item><see cref="Igual"/>: OK si |valor − referencia|, redondeado a céntimos, no pasa de la tolerancia.</item>
/// <item><see cref="Cero"/>: OK si el valor es 0.</item>
/// <item><see cref="Positivo"/>: OK si el valor es mayor que 0.</item>
/// <item><see cref="CaidaIngresos"/>: falla si la referencia (trimestre anterior) es mayor que 0 y el valor (este) es 0.</item>
/// <item><see cref="ConActivos"/>: OK si el valor es mayor que 0 o la referencia (activos amortizables) es 0.</item>
/// </list>
/// </summary>
public enum ReglaControl { Igual, Cero, Positivo, CaidaIngresos, ConActivos }

/// <summary>
/// Un control del informe: valor y referencia (con su fórmula viva sobre las tablas del libro, si la tiene), regla, estado si
/// falla y una nota que dice qué hacer. <paramref name="EsImporte"/>: importe en euros o número (recuento).
/// </summary>
public sealed record ControlInforme(
    string Nombre, decimal Valor, decimal? Referencia, ReglaControl Regla, EstadoControl SiFalla, string Nota,
    string? FormulaValor = null, string? FormulaReferencia = null, decimal Tolerancia = 0m, bool EsImporte = true)
{
    public decimal? Diferencia => Referencia is decimal r && Regla is ReglaControl.Igual or ReglaControl.Cero ? Valor - r : null;

    public EstadoControl Estado => Regla switch
    {
        ReglaControl.Igual => Math.Round(Math.Abs(Valor - (Referencia ?? 0m)), 2, MidpointRounding.AwayFromZero) <= Tolerancia ? EstadoControl.OK : SiFalla,
        ReglaControl.Cero => Valor == 0m ? EstadoControl.OK : SiFalla,
        ReglaControl.Positivo => Valor > 0m ? EstadoControl.OK : SiFalla,
        ReglaControl.CaidaIngresos => (Referencia ?? 0m) > 0m && Valor == 0m ? SiFalla : EstadoControl.OK,
        _ => Valor > 0m || (Referencia ?? 0m) == 0m ? EstadoControl.OK : SiFalla,
    };

    /// <summary>Fórmula de la columna Estado para la fila <paramref name="fila"/> (valor en D, referencia en E, diferencia en F).</summary>
    public string FormulaEstado(int fila)
    {
        var falla = $"\"{SiFalla}\"";
        var tol = Tolerancia.ToString(CultureInfo.InvariantCulture);
        return Regla switch
        {
            ReglaControl.Igual => $"IF(ROUND(ABS(F{fila}),2)<={tol},\"OK\",{falla})",
            ReglaControl.Cero => $"IF(D{fila}=0,\"OK\",{falla})",
            ReglaControl.Positivo => $"IF(D{fila}>0,\"OK\",{falla})",
            ReglaControl.CaidaIngresos => $"IF(AND(E{fila}>0,D{fila}=0),{falla},\"OK\")",
            _ => $"IF(OR(D{fila}>0,E{fila}=0),\"OK\",{falla})",
        };
    }
}

/// <summary>Nombres definidos del libro: una columna de cada tabla de datos, para que las fórmulas se lean solas.</summary>
public static class NombresGestoria
{
    public const string ProrrataProv = "ProrrataProv";
    public const string ProrrataReal = "ProrrataReal";
    public const string IvaTrim = "IVA_Trim", IvaAsiento = "IVA_Asiento", IvaSentido = "IVA_Sentido", IvaOperacion = "IVA_Operacion", IvaIsp = "IVA_ISP",
        IvaTipo = "IVA_Tipo", IvaCentro = "IVA_Centro", IvaActividad = "IVA_Actividad", IvaDeduccion = "IVA_Deduccion", IvaBase = "IVA_Base", IvaCuota = "IVA_Cuota",
        IvaAviso = "IVA_Aviso";
    public const string EmiTrim = "Emi_Trim", EmiBase = "Emi_Base", EmiContabilizada = "Emi_Contabilizada";
    public const string RecTrim = "Rec_Trim", RecBase = "Rec_Base";
    public const string PygApartado = "PyG_Apartado", PygTitulo = "PyG_Titulo", PygCuenta = "PyG_Cuenta", PygCentro = "PyG_Centro", PygActividad = "PyG_Actividad", PygImporte = "PyG_Importe";
    /// <summary>Texto de la columna Actividad (y del centro raíz) cuando no hay: así se puede filtrar y usar como criterio.</summary>
    public const string SinActividad = "(sin actividad)";
    public const string SinCentro = "(sin centro)";
    public const string SitLado = "Sit_Lado", SitApartado = "Sit_Apartado", SitTitulo = "Sit_Titulo", SitSubtitulo = "Sit_Subtitulo", SitCuenta = "Sit_Cuenta", SitImporte = "Sit_Importe";

    /// <summary>Texto de un criterio de SUMAR.SI.CONJUNTO entre comillas, con los comodines (* ? ~) escapados para que se busque tal cual.</summary>
    public static string Literal(string texto) => "\"" + texto.Replace("~", "~~").Replace("*", "~*").Replace("?", "~?").Replace("\"", "\"\"") + "\"";
}

/// <summary>
/// Controles automáticos del informe (hoja «Controles», informes/informe-gestoria). Cada uno lleva valor, referencia, estado y una
/// nota que dice qué hacer; los que salen de las tablas del libro llevan además su fórmula, que da lo mismo que el programa.
/// </summary>
public static class ControlesGestoria
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private static string Imp(decimal v) => v.ToString("#,##0.00", Es) + " €";

    private static string Lista<T>(IEnumerable<T> elementos, int maximo = 8)
    {
        var l = elementos.Select(e => Convert.ToString(e, Es)).Distinct().ToList();
        return l.Count <= maximo ? string.Join(", ", l) : string.Join(", ", l.Take(maximo)) + $" y {l.Count - maximo} más";
    }

    public static IReadOnlyList<ControlInforme> Calcular(InformeGestoria inf)
    {
        var c = new List<ControlInforme>();
        var t = inf.Trimestre;
        var lineas = inf.LineasIva;
        const string N = "\"";

        // 1. Líneas de IVA sin centro o sin actividad.
        var sinCentro = lineas.Where(l => l.Deduccion == DeduccionIva.SinCentro).ToList();
        c.Add(new("Líneas de IVA sin centro de coste o con un centro sin actividad", sinCentro.Count, 0m, ReglaControl.Cero, EstadoControl.Revisar,
            sinCentro.Count == 0
                ? "Todas las líneas de IVA tienen centro con actividad: su deducción está clasificada."
                : $"Asientos {Lista(sinCentro.Select(l => l.IdAsiento))} (cuota {Imp(sinCentro.Sum(l => l.Cuota))}): no entran en lo deducible. Abre cada asiento en Contabilidad › Diario y pon el centro de coste " +
                  "en su apunte de IVA, o asigna la actividad al centro en Catálogos › «Actividad de los centros de coste»; después vuelve a generar el informe.",
            $"COUNTIF({NombresGestoria.IvaDeduccion},{N}{TextosIva.SinCentro}{N})", EsImporte: false));

        // 2. Asientos con fecha fuera del ejercicio.
        var fueraIva = lineas.Where(l => l.Trimestre == 0).ToList();
        c.Add(new("Asientos con fecha fuera del ejercicio", inf.AsientosFueraEjercicio, 0m, ReglaControl.Cero, EstadoControl.Revisar,
            inf.AsientosFueraEjercicio == 0
                ? $"Todos los asientos del ejercicio {inf.Ejercicio} tienen fecha de {inf.Ejercicio}."
                : $"Hay asientos del ejercicio {inf.Ejercicio} fechados en otro año" + (fueraIva.Count > 0 ? $" (con IVA: {Lista(fueraIva.Select(l => l.IdAsiento))}, que no entran en ningún trimestre)" : "") +
                  ". Corrige su fecha en Contabilidad › Diario (ficha del asiento) y vuelve a generar el informe.", EsImporte: false));

        // 3. Libros de IVA frente a las cuentas 472 y 477, por trimestre.
        foreach (var q in inf.CuadreLibros.Where(q => q.Trimestre <= t).OrderBy(q => q.Trimestre))
        {
            c.Add(new($"Libro de IVA repercutido frente a las 477 · {q.Trimestre}T", q.LibroRepercutido, q.Cuentas477, ReglaControl.Igual, EstadoControl.Revisar,
                "Cuotas del libro de repercutido frente al saldo de las 477 (sin el autorrepercutido ni la liquidación). Si no coinciden, busca en el Mayor de las 477 " +
                "del trimestre el apunte sin registro en el libro (asiento manual con IVA) o el registro sin apunte, y corrígelo en el Diario.", Tolerancia: 0.01m));
            c.Add(new($"Libro de IVA soportado frente a las 472 · {q.Trimestre}T", q.LibroSoportado, q.Cuentas472, ReglaControl.Igual, EstadoControl.Revisar,
                "Cuotas del libro de soportado frente al saldo de las 472 (sin la liquidación). Si no coinciden, busca en el Mayor de las 472 del trimestre el apunte " +
                "sin registro en el libro o con otro importe, y corrígelo en el Diario o en Compras › Facturas recibidas.", Tolerancia: 0.01m));
        }

        // 4. Bases de emitidas frente al libro de repercutido, por trimestre.
        for (var q = 1; q <= t; q++)
        {
            var emi = inf.Emitidas.Where(e => e.Trimestre == q).Sum(e => e.Base);
            var lib = lineas.Where(l => l.Trimestre == q && l.Sentido == SentidoIva.Repercutido).Sum(l => l.Base);
            var difFact = inf.Emitidas.Where(e => e.Trimestre == q && !e.Contabilizada).ToList();
            c.Add(new($"Bases de facturas emitidas frente al libro de repercutido · {q}T", emi, lib, ReglaControl.Igual, EstadoControl.Revisar,
                emi == lib ? "Coinciden." :
                "La factura va al trimestre de su fecha y el libro al de la fecha de su asiento: una factura contabilizada en otro trimestre, o sin contabilizar" +
                (difFact.Count > 0 ? $" (aquí: {Lista(difFact.Select(e => e.Numero))})" : "") +
                ", explica la diferencia. Compáralas en las hojas «Emitidas» y «Datos IVA» filtrando el trimestre; contabiliza las pendientes desde Ventas › Facturas.",
                $"SUMIFS({NombresGestoria.EmiBase},{NombresGestoria.EmiTrim},{q})",
                $"SUMIFS({NombresGestoria.IvaBase},{NombresGestoria.IvaSentido},{N}{TextosIva.Repercutido}{N},{NombresGestoria.IvaTrim},{q})", Tolerancia: 0.01m));
        }

        // 5. Bases de recibidas frente al libro de soportado (acumulado hasta el trimestre).
        var rec = inf.Recibidas.Where(r => r.Trimestre is >= 1 && r.Trimestre <= t).Sum(r => r.Base);
        var libSop = lineas.Where(l => l.Trimestre >= 1 && l.Trimestre <= t && l.Sentido == SentidoIva.Soportado).Sum(l => l.Base);
        c.Add(new($"Bases de facturas recibidas frente al libro de soportado · 1T a {t}T", rec, libSop, ReglaControl.Igual, EstadoControl.Aviso,
            rec == libSop ? "Coinciden." :
            "Las facturas registradas en Compras deben coincidir con el libro de soportado. La diferencia son registros del libro sin factura en Compras (importados de ContaSol " +
            "o asientos manuales con IVA) o facturas con otra fecha: compara las hojas «Recibidas» y «Datos IVA» por asiento.",
            $"SUMIFS({NombresGestoria.RecBase},{NombresGestoria.RecTrim},\">=1\",{NombresGestoria.RecTrim},\"<={t}\")",
            $"SUMIFS({NombresGestoria.IvaBase},{NombresGestoria.IvaSentido},{N}{TextosIva.Soportado}{N},{NombresGestoria.IvaTrim},\">=1\",{NombresGestoria.IvaTrim},\"<={t}\")",
            Tolerancia: 0.05m));

        // 6. Ventas con cuota en actividades sin deducción plena (informativo).
        var ventasCuota = lineas.Where(l => l.Trimestre >= 1 && l.Sentido == SentidoIva.Repercutido && l.Deduccion != DeduccionIva.Deducible100 && l.Cuota != 0m).ToList();
        c.Add(new("Ventas con cuota de IVA en actividades sin deducción plena", ventasCuota.Sum(l => l.Cuota), 0m, ReglaControl.Cero, EstadoControl.Aviso,
            ventasCuota.Count == 0
                ? "Ninguna: todo el IVA repercutido viene de actividades 100 % deducibles."
                : $"Asientos {Lista(ventasCuota.Select(l => l.IdAsiento))}: el 303 las devenga igual, pero su base no cuenta como operación con derecho a deducir en la prorrata. " +
                  "Comprueba que el centro de coste de esas ventas es el correcto (Ventas › Facturas) y, si lo es, avisa a la gestoría.",
            $"SUMIFS({NombresGestoria.IvaCuota},{NombresGestoria.IvaSentido},{N}{TextosIva.Repercutido}{N},{NombresGestoria.IvaDeduccion},{N}<>{TextosIva.Deducible100}{N},{NombresGestoria.IvaTrim},\">0\")"));

        // 7. Actividades con ingresos el trimestre anterior y ninguno en este.
        if (t >= 2)
        {
            var rep = lineas.Where(l => l.Sentido == SentidoIva.Repercutido).ToList();
            foreach (var act in rep.Where(l => l.Trimestre == t - 1).GroupBy(l => l.Actividad ?? "").Where(g => g.Sum(l => l.Base) > 0m).Select(g => g.Key).OrderBy(a => a, StringComparer.CurrentCulture))
            {
                var este = rep.Where(l => l.Trimestre == t && (l.Actividad ?? "") == act).Sum(l => l.Base);
                var antes = rep.Where(l => l.Trimestre == t - 1 && (l.Actividad ?? "") == act).Sum(l => l.Base);
                var nombre = act.Length == 0 ? NombresGestoria.SinActividad : act;
                var criterio = NombresGestoria.Literal(nombre);
                c.Add(new($"Ingresos de «{nombre}» en el {t}T frente al {t - 1}T", este, antes, ReglaControl.CaidaIngresos, EstadoControl.Revisar,
                    este > 0m ? "La actividad sigue facturando." :
                    $"{Imp(antes)} en el {t - 1}T y nada en el {t}T. Si faltan facturas por contabilizar (Ventas › Facturas o Ventas por facturar), la prorrata real cambiará; si la actividad ha parado, no hay nada que hacer.",
                    $"SUMIFS({NombresGestoria.IvaBase},{NombresGestoria.IvaSentido},{N}{TextosIva.Repercutido}{N},{NombresGestoria.IvaActividad},{criterio},{NombresGestoria.IvaTrim},{t})",
                    $"SUMIFS({NombresGestoria.IvaBase},{NombresGestoria.IvaSentido},{N}{TextosIva.Repercutido}{N},{NombresGestoria.IvaActividad},{criterio},{NombresGestoria.IvaTrim},{t - 1})"));
            }
        }

        // 8. Balance cuadrado.
        var bloques = inf.Situacion.TotalesPorBloque;
        var activo = bloques.Count > 0 ? bloques[0] : ("ACTIVO", 0m);
        var pasivoNombres = bloques.Skip(1).Select(b => b.Bloque).ToList();
        var pasivo = bloques.Skip(1).Sum(b => b.Importe);
        c.Add(new("Balance cuadrado: activo frente a patrimonio neto y pasivo", activo.Item2, pasivo, ReglaControl.Igual, EstadoControl.Revisar,
            "Si no cuadra, hay asientos descuadrados o cuentas que el modelo no clasifica: mira el control «Cuentas sin clasificar» y la salud de Inicio (cuadre del Diario).",
            $"SUMIFS({NombresGestoria.SitImporte},{NombresGestoria.SitLado},{NombresGestoria.Literal(activo.Item1)})",
            pasivoNombres.Count == 0 ? "0" : string.Join("+", pasivoNombres.Select(b => $"SUMIFS({NombresGestoria.SitImporte},{NombresGestoria.SitLado},{NombresGestoria.Literal(b)})")),
            Tolerancia: 0.01m));

        // 9. Resultado de PyG frente al del balance sin la 129.
        var r67 = inf.Situacion.Filas.Where(f => f.CuentaContable.Length > 0 && f.CuentaContable[0] is '6' or '7').Sum(f => f.Importe);
        var r129 = inf.Situacion.Filas.Where(f => f.CuentaContable.StartsWith("129", StringComparison.Ordinal)).Sum(f => f.Importe);
        c.Add(new("Resultado: pérdidas y ganancias frente al balance sin la 129", inf.PyG.Total, r67, ReglaControl.Igual, EstadoControl.Revisar,
            (r129 != 0m ? $"La 129 tiene {Imp(r129)} en el balance (resultados de años anteriores sin aplicar o la regularización): no se compara. " : "") +
            "Si no coinciden, hay apuntes de los grupos 6 o 7 en asientos de regularización o con fecha posterior al trimestre: revísalos en el Diario.",
            $"SUM({NombresGestoria.PygImporte})",
            $"SUMIFS({NombresGestoria.SitImporte},{NombresGestoria.SitCuenta},\"6*\")+SUMIFS({NombresGestoria.SitImporte},{NombresGestoria.SitCuenta},\"7*\")", Tolerancia: 0.01m));

        // 10. Pasivo corriente con saldo deudor.
        var apartadoPc = inf.Situacion.Filas.Select(f => (f.Apartado, inf.Situacion.Modelo.Regla(f.CuentaContable)?.CodigoApartado)).FirstOrDefault(x => x.CodigoApartado == "PNP.C").Apartado;
        if (apartadoPc is not null)
        {
            var deudoras = inf.Situacion.Filas.Where(f => f.Apartado == apartadoPc).GroupBy(f => f.Subcuenta).Select(g => (g.Key, Saldo: g.Sum(f => f.Importe))).Where(x => x.Saldo < 0m).ToList();
            c.Add(new("Pasivo corriente con saldo deudor (subcuentas en negativo)", deudoras.Sum(x => x.Saldo), 0m, ReglaControl.Cero, EstadoControl.Revisar,
                deudoras.Count == 0 ? "Ninguna subcuenta del pasivo corriente tiene saldo deudor." :
                $"{Lista(deudoras.OrderBy(x => x.Saldo).Select(x => $"{x.Key} ({Imp(x.Saldo)})"), 5)}: un pasivo con saldo deudor suele ser un cobro o pago pendiente de aplicar. " +
                "Ábrelas en Contabilidad › Mayor y aplica los movimientos; mientras, falsean la liquidez y el endeudamiento."));
        }

        // 11. Amortizaciones contabilizadas.
        var amortizacion = -inf.PyG.Filas.Where(f => f.CuentaContable.StartsWith("68", StringComparison.Ordinal)).Sum(f => f.Importe);
        c.Add(new("Amortizaciones contabilizadas (referencia: activos amortizables)", amortizacion, inf.ActivosAmortizables, ReglaControl.ConActivos, EstadoControl.Revisar,
            amortizacion > 0m ? "Hay amortización contabilizada hasta el trimestre." :
            inf.ActivosAmortizables == 0 ? "No hay activos amortizables dados de alta." :
            $"Hay {inf.ActivosAmortizables} activos amortizables y ninguna amortización hasta el {inf.FinTrimestre:dd/MM/yyyy}: el resultado y los ratios no la recogen. " +
            "Contabilízala desde Activos (amortización del periodo) o avisa a la gestoría de que la hará al cierre.",
            $"-SUMIFS({NombresGestoria.PygImporte},{NombresGestoria.PygCuenta},\"68*\")"));

        // 12. Facturas emitidas sin contabilizar.
        var sinContabilizar = inf.Emitidas.Where(e => !e.Contabilizada).ToList();
        c.Add(new("Facturas emitidas sin contabilizar", sinContabilizar.Count, 0m, ReglaControl.Cero, EstadoControl.Aviso,
            sinContabilizar.Count == 0 ? $"Todas las facturas emitidas hasta el {inf.FinTrimestre:dd/MM/yyyy} tienen asiento." :
            $"Facturas {Lista(sinContabilizar.Select(e => e.Numero))}: no están en el libro de IVA. Contabilízalas desde Ventas › Facturas (menú contextual) antes de presentar el 303.",
            $"COUNTIF({NombresGestoria.EmiContabilizada},{N}{TextosIva.No}{N})", EsImporte: false));

        // 13. Prorrata provisional sin informar.
        c.Add(new("Prorrata provisional sin informar", inf.ProrrataProvisional is null ? 1m : 0m, 0m, ReglaControl.Cero, EstadoControl.Revisar,
            inf.ProrrataProvisional is null
                ? "El ejercicio no tiene la prorrata del año anterior: el cálculo usa el 100 %. Escríbela en Configuración › Parámetros y tablas › Ejercicios («Prorrata del año anterior (%)») y vuelve a generar el informe."
                : $"Se aplica la prorrata provisional del {inf.ProrrataProvisional.Value * 100m:0.##} % del ejercicio.", EsImporte: false));

        // 14. Cuentas sin clasificar en los balances.
        var sinClasificar = inf.Situacion.SinClasificar.Select(s => s.Cuenta).Concat(inf.PyG.SinClasificar.Select(s => s.Cuenta)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList();
        c.Add(new("Cuentas con apuntes que los balances no clasifican", sinClasificar.Count, 0m, ReglaControl.Cero, EstadoControl.Revisar,
            sinClasificar.Count == 0 ? "El modelo de balance clasifica todas las cuentas con apuntes." :
            $"Cuentas {Lista(sinClasificar)}: no están en PyG ni en Situación. Corrige la cuenta de esos apuntes en el Diario o añade su prefijo al modelo de balance (reglas de cuentas).",
            EsImporte: false));
        return c;
    }
}
