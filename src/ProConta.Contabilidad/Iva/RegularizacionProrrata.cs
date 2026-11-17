using System.Globalization;

namespace ProConta.Contabilidad.Iva;

/// <summary>
/// Cuentas de la regularización de la prorrata del ejercicio (organizacion/sociedades-ejercicios, esquema 63); vacías, las de
/// siempre: 4729001 regularización de la prorrata, 6341000 ajustes negativos y 6391000 ajustes positivos (las dos últimas, las
/// mismas de la liquidación del IVA).
/// </summary>
public sealed record CuentasRegularizacionProrrata(string Regularizacion, string AjustesNegativos, string AjustesPositivos)
{
    public const string RegularizacionDefecto = "4729001";

    public static CuentasRegularizacionProrrata Defecto { get; } =
        new(RegularizacionDefecto, CuentasLiquidacionIva.NegativosDefecto, CuentasLiquidacionIva.PositivosDefecto);

    public static CuentasRegularizacionProrrata Con(string? regularizacion, CuentasLiquidacionIva liquidacion) => new(
        string.IsNullOrWhiteSpace(regularizacion) ? RegularizacionDefecto : regularizacion.Trim(), liquidacion.AjustesNegativos, liquidacion.AjustesPositivos);
}

/// <summary>
/// Datos de la regularización: el IVA del ejercicio calculado con la definitiva que se quiere aplicar (<see cref="CalculoIvaEjercicio"/>),
/// las líneas de IVA (para repartir el ajuste por centro de coste) y las cuentas.
/// </summary>
public sealed record DatosRegularizacionProrrata(int Ejercicio, IvaEjercicio Iva, IReadOnlyList<LineaIva> Lineas, CuentasRegularizacionProrrata Cuentas);

/// <summary>Una línea del asiento propuesto.</summary>
/// <param name="NombreCentro">Nombre del centro de coste, para enseñarlo en la propuesta (no va al asiento).</param>
public sealed record LineaRegularizacionProrrata(string Cuenta, decimal Debe, decimal Haber, string Concepto, int? IdCentroCoste = null, string? NombreCentro = null);

/// <summary>Las cifras de un trimestre en la propuesta: cuota sujeta, deducido con la provisional, deducible con la definitiva y diferencia.</summary>
public sealed record TrimestreRegularizacionProrrata(int Trimestre, decimal CuotaSujeta, decimal Deducido, decimal Deducible)
{
    public decimal Regularizacion => Deducible - Deducido;
}

/// <summary>
/// Propuesta de regularización de la prorrata del ejercicio (contabilidad/procesos/prorrata): porcentajes, cifras por trimestre,
/// el asiento del 31/12 y los avisos. <see cref="Regularizacion"/> positiva a favor (más IVA deducible) y negativa en contra.
/// </summary>
public sealed record PropuestaRegularizacionProrrata(
    int Ejercicio, DateTime Fecha, decimal Provisional, bool ProvisionalInformada, decimal Legal, decimal Definitiva, bool DefinitivaEsLaLegal,
    decimal Numerador, decimal Denominador, IReadOnlyList<TrimestreRegularizacionProrrata> Trimestres, IReadOnlyList<LineaRegularizacionProrrata> Lineas,
    IReadOnlyList<string> Avisos)
{
    public decimal CuotaSujeta => Trimestres.Sum(t => t.CuotaSujeta);
    public decimal Deducido => Trimestres.Sum(t => t.Deducido);
    public decimal Deducible => Trimestres.Sum(t => t.Deducible);
    public decimal Regularizacion => Trimestres.Sum(t => t.Regularizacion);
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);
}

/// <summary>
/// Cálculo del asiento de regularización de la prorrata definitiva (contabilidad/procesos/prorrata), sin BD. Fecha: el 31/12 del
/// ejercicio. La regularización es la de <see cref="ResultadoProrrata.Regularizacion"/> con la definitiva: por trimestre,
/// Redondear(cuota sujeta × definitiva, 2) − lo deducido con la provisional, sumado en el año (la misma cifra que la hoja «Prorrata»
/// del informe para la gestoría y que la línea de regularización del 303 del 4T).
/// <list type="bullet">
/// <item>A favor (se deduce más): 4729 al Debe y la cuenta de ajustes positivos (6391) al Haber.</item>
/// <item>En contra (se deduce menos): la cuenta de ajustes negativos (6341) al Debe y la 4729 al Haber.</item>
/// <item>La 6341 o la 6391 se reparte por centro de coste en proporción a la cuota sujeta a prorrata de cada centro en el año (resto
/// mayor), como la parte no deducible de cada liquidación.</item>
/// <item>La liquidación del 4T salda la 4729 y el 303 del 4T deduce la misma cifra: el resultado del año cuadra con las cuentas.</item>
/// </list>
/// </summary>
public static class CalculoRegularizacionProrrata
{
    public static DateTime FechaAsiento(int ejercicio) => new(ejercicio, 12, 31);

    public static string Descripcion(int ejercicio) => $"Regularización de la prorrata definitiva {ejercicio}";

    private static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";

    private static string Porcentaje(decimal tantoPorUno) => (tantoPorUno * 100m).ToString("0.##", CultureInfo.GetCultureInfo("es-ES")) + " %";

    public static PropuestaRegularizacionProrrata Calcular(DatosRegularizacionProrrata d)
    {
        var p = d.Iva.Prorrata;
        var definitiva = p.Aplicada;
        var trimestres = Enumerable.Range(1, 4)
            .Select(t => new TrimestreRegularizacionProrrata(t, p.CuotaSujeta[t - 1], p.DeducidoProvisional[t - 1], p.DeducibleAplicada[t - 1])).ToList();
        var regularizacion = trimestres.Sum(t => t.Regularizacion);
        var lineas = new List<LineaRegularizacionProrrata>();
        var avisos = new List<string>();
        var concepto = $"Regularización de la prorrata {d.Ejercicio}: {Porcentaje(p.Provisional)} → {Porcentaje(definitiva)}";

        if (regularizacion != 0m)
        {
            var pesos = d.Lineas.Where(l => l.Trimestre is >= 1 and <= 4 && Calculo303.EsSoportado(l) && l.Deduccion == DeduccionIva.Prorrata)
                .GroupBy(l => l.IdCentroCoste).Select(g => (Centro: g.Key, Peso: g.Sum(l => l.Cuota))).OrderBy(x => x.Centro ?? int.MaxValue).ToList();
            var partes = CalculoLiquidacionIva.Repartir(Math.Abs(regularizacion), pesos);
            var nombres = d.Lineas.Where(l => l.IdCentroCoste is not null).GroupBy(l => l.IdCentroCoste!.Value)
                .ToDictionary(g => g.Key, g => g.Select(l => l.Departamento ?? l.Centro).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)));
            string? Nombre(int? centro) => centro is int c && nombres.TryGetValue(c, out var n) ? n : null;
            if (regularizacion > 0m)
            {
                lineas.Add(new(d.Cuentas.Regularizacion, regularizacion, 0m, concepto));
                lineas.AddRange(partes.Select(x => new LineaRegularizacionProrrata(d.Cuentas.AjustesPositivos, 0m, x.Importe, concepto, x.Centro, Nombre(x.Centro))));
            }
            else
            {
                lineas.AddRange(partes.Select(x => new LineaRegularizacionProrrata(d.Cuentas.AjustesNegativos, x.Importe, 0m, concepto, x.Centro, Nombre(x.Centro))));
                lineas.Add(new(d.Cuentas.Regularizacion, 0m, -regularizacion, concepto));
            }
        }
        else
            avisos.Add(p.CuotaSujetaAnual == 0m
                ? "El ejercicio no tiene IVA soportado sujeto a prorrata: no hay nada que regularizar."
                : "La definitiva deduce lo mismo que la provisional: no hace falta asiento. Al confirmar solo se guarda la definitiva.");

        if (!p.ProvisionalInformada)
            avisos.Add("El ejercicio no tiene prorrata provisional: los trimestres se liquidaron deduciendo el 100 %. Ponla en Parámetros y tablas › Ejercicios (Impuestos) si se aplicó otra.");
        var esLegal = definitiva == p.Real;
        if (!esLegal)
            avisos.Add($"La definitiva ({Porcentaje(definitiva)}) no es la legal calculada desde las ventas ({Porcentaje(p.Real)}). Si es la que marca la gestoría, adelante; si no, usa la legal.");
        if (p.Denominador == 0m)
            avisos.Add("El ejercicio no tiene ventas en los libros de IVA: la prorrata legal sale 0 %.");

        return new PropuestaRegularizacionProrrata(d.Ejercicio, FechaAsiento(d.Ejercicio), p.Provisional, p.ProvisionalInformada, p.Real, definitiva, esLegal,
            p.Numerador, p.Denominador, trimestres, lineas, avisos);
    }

    /// <summary>Texto para las notas del asiento: porcentajes y cifras del año.</summary>
    public static string Notas(PropuestaRegularizacionProrrata p, string? motivo)
    {
        var partes = new List<string>
        {
            $"Provisional {Porcentaje(p.Provisional)}, legal {Porcentaje(p.Legal)}, definitiva {Porcentaje(p.Definitiva)}.",
            $"Cuota sujeta {Euros(p.CuotaSujeta)}; deducido {Euros(p.Deducido)}; deducible {Euros(p.Deducible)}; regularización {Euros(p.Regularizacion)}.",
        };
        if (!string.IsNullOrWhiteSpace(motivo)) partes.Add($"Motivo: {motivo.Trim()}");
        var texto = string.Join(" ", partes);
        return texto.Length <= 255 ? texto : texto[..255];
    }
}
