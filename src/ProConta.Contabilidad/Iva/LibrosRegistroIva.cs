using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Iva;

/// <summary>
/// Parámetros de los libros registro de IVA (informes/excel/libros-registro-iva): las facturas cuyo asiento tiene fecha contable entre
/// <paramref name="Desde"/> y <paramref name="Hasta"/>, ambos incluidos (como el 303), y qué libros se piden.
/// </summary>
public sealed record ParametrosLibrosIva(DateTime Desde, DateTime Hasta, bool Expedidas = true, bool Recibidas = true)
{
    /// <summary>Primer y último día de un trimestre (1 a 4).</summary>
    public static ParametrosLibrosIva Trimestre(int ejercicio, int trimestre, bool expedidas = true, bool recibidas = true)
    {
        var desde = new DateTime(ejercicio, 3 * trimestre - 2, 1);
        return new(desde, desde.AddMonths(3).AddDays(-1), expedidas, recibidas);
    }

    /// <summary>Primer y último día de un mes.</summary>
    public static ParametrosLibrosIva Mes(int ejercicio, int mes, bool expedidas = true, bool recibidas = true)
    {
        var desde = new DateTime(ejercicio, mes, 1);
        return new(desde, desde.AddMonths(1).AddDays(-1), expedidas, recibidas);
    }

    /// <summary>
    /// Trimestre propuesto: el último terminado del ejercicio (el que toca presentar); en el primer trimestre del año en curso, ese mismo;
    /// en un ejercicio ya pasado, el 4T.
    /// </summary>
    public static int TrimestrePropuesto(int ejercicio, DateTime hoy)
    {
        if (hoy.Year > ejercicio) return 4;
        if (hoy.Year < ejercicio) return 1;
        var enCurso = (hoy.Month - 1) / 3 + 1;
        return Math.Max(1, enCurso - 1);
    }

    /// <summary>Qué falla en los parámetros para el ejercicio dado, con cómo arreglarlo; null si son válidos.</summary>
    public string? Error(int ejercicio)
    {
        if (!Expedidas && !Recibidas) return "Marca al menos un libro: facturas expedidas, facturas recibidas o los dos.";
        if (Desde.Date > Hasta.Date) return $"La fecha «Desde» ({Desde:dd/MM/yyyy}) es posterior a «Hasta» ({Hasta:dd/MM/yyyy}): cámbialas de orden.";
        if (Desde.Year != ejercicio || Hasta.Year != ejercicio)
            return $"Las dos fechas tienen que ser del ejercicio de trabajo ({ejercicio}): del 01/01/{ejercicio} al 31/12/{ejercicio}. Para otro año, cambia antes el ejercicio de trabajo.";
        return null;
    }

    /// <summary>El periodo en palabras: «1T 2026», «marzo de 2026», «año 2026» o «del 05/01/2026 al 20/02/2026».</summary>
    public string Periodo
    {
        get
        {
            var es = CultureInfo.GetCultureInfo("es-ES");
            var d = Desde.Date;
            var h = Hasta.Date;
            if (d.Day == 1 && h == d.AddMonths(12).AddDays(-1) && d.Month == 1) return $"año {d.Year}";
            if (d.Day == 1 && (d.Month - 1) % 3 == 0 && h == d.AddMonths(3).AddDays(-1)) return $"{(d.Month - 1) / 3 + 1}T {d.Year}";
            if (d.Day == 1 && h == d.AddMonths(1).AddDays(-1)) return $"{d.ToString("MMMM", es)} de {d.Year}";
            return $"del {d:dd/MM/yyyy} al {h:dd/MM/yyyy}";
        }
    }
}

/// <summary>
/// Una fila de un libro registro: un desglose (tipo de IVA) de una factura. <paramref name="Orden"/> es el número de la factura en el libro
/// del periodo (las filas de una misma factura comparten número). Base y cuota con el signo del libro (negativas en una rectificativa a la baja).
/// </summary>
public sealed record FilaLibroIva(
    int Orden, DateTime FechaContable, DateTime? FechaExpedicion, string? Factura, string Tercero, string? Nif,
    decimal Base, decimal TipoIva, decimal Cuota, decimal CuotaAutorrepercutida, OperacionIva Operacion, bool Isp, bool BienInversion,
    bool Rectificativa, string? ClaveOperacion, DeduccionIva Deduccion, int IdAsiento, string? Aviso)
{
    /// <summary>Texto de la operación para el libro de recibidas: «Nacional», «Intracomunitaria», «Importación», con « · ISP» si invierte el sujeto pasivo.</summary>
    public string TextoOperacion => Operacion.Texto() + (Isp ? " · ISP" : "");
}

/// <summary>Totales de un tipo de IVA en un libro.</summary>
public sealed record TotalTipoIva(decimal TipoIva, decimal Base, decimal Cuota, decimal CuotaAutorrepercutida, int Filas);

/// <summary>
/// Los libros registro calculados: las filas de cada libro pedido y el 303 de las mismas facturas (<see cref="Cuadre303"/>), para comprobar
/// que los libros cuadran con el modelo. El 303 se calcula con la prorrata provisional del ejercicio, trimestre a trimestre, sin la
/// regularización anual de la prorrata (casilla 44), que no sale de ninguna factura.
/// </summary>
public sealed record ResultadoLibrosIva(
    string RazonSocial, int Ejercicio, ParametrosLibrosIva Parametros, IReadOnlyList<FilaLibroIva> Expedidas, IReadOnlyList<FilaLibroIva> Recibidas,
    Resultado303 Cuadre303, decimal ProrrataProvisional, bool ProrrataInformada, DateTime Generado)
{
    public IReadOnlyList<TotalTipoIva> TotalesExpedidas => Totales(Expedidas);
    public IReadOnlyList<TotalTipoIva> TotalesRecibidas => Totales(Recibidas);

    /// <summary>Número de facturas de cada libro (no de filas).</summary>
    public int FacturasExpedidas => Expedidas.Select(f => f.Orden).Distinct().Count();
    public int FacturasRecibidas => Recibidas.Select(f => f.Orden).Distinct().Count();

    private static IReadOnlyList<TotalTipoIva> Totales(IReadOnlyList<FilaLibroIva> filas) =>
        filas.GroupBy(f => f.TipoIva).OrderByDescending(g => g.Key)
            .Select(g => new TotalTipoIva(g.Key, g.Sum(f => f.Base), g.Sum(f => f.Cuota), g.Sum(f => f.CuotaAutorrepercutida), g.Count()))
            .ToList();

    /// <summary>Línea de cabecera: periodo, criterio de fechas y prorrata.</summary>
    public string Descripcion =>
        $"Ejercicio {Ejercicio} · {Parametros.Periodo} · por fecha contable del asiento, sin el asiento de cierre";
}

/// <summary>Cálculo de los libros registro a partir de las líneas de IVA del ejercicio (sin BD; lo prueban las pruebas unitarias).</summary>
public static class CalculadoraLibrosIva
{
    /// <summary>
    /// Filtra las líneas por fecha contable, las ordena (fecha contable y asiento) y numera las facturas de cada libro. Una factura es un
    /// registro de IVA: el mismo asiento, número de factura y tercero. El cuadre es el 303 de las líneas del periodo (con los dos libros,
    /// aunque se pida solo uno).
    /// </summary>
    public static ResultadoLibrosIva Calcular(string razonSocial, int ejercicio, ParametrosLibrosIva parametros, IReadOnlyList<LineaIva> lineas,
        decimal? prorrataProvisional, DateTime generado)
    {
        var desde = parametros.Desde.Date;
        var hasta = parametros.Hasta.Date;
        var delPeriodo = lineas.Where(l => l.Trimestre > 0 && l.Fecha.Date >= desde && l.Fecha.Date <= hasta).ToList();
        IReadOnlyList<FilaLibroIva> Libro(SentidoIva sentido)
        {
            var filas = new List<FilaLibroIva>();
            var orden = 0;
            (int, string?, string)? anterior = null;
            foreach (var l in delPeriodo.Where(l => l.Sentido == sentido).OrderBy(l => l.Fecha).ThenBy(l => l.IdAsiento))
            {
                var clave = (l.IdAsiento, l.Factura, l.Tercero);
                if (anterior != clave) { orden++; anterior = clave; }
                filas.Add(new FilaLibroIva(orden, l.Fecha, l.FechaExpedicion, l.Factura, l.Tercero, l.Nif, l.Base, l.TipoIva, l.Cuota, l.CuotaAutorrepercutida,
                    l.Operacion, l.Isp, l.BienInversion, l.Rectificativa, l.ClaveOperacion, l.Deduccion, l.IdAsiento,
                    l.Aviso is TextosIva.AvisoFueraEjercicio ? null : l.Aviso));
            }
            return filas;
        }
        var prorrata = prorrataProvisional ?? 1m;
        return new ResultadoLibrosIva(razonSocial, ejercicio, parametros,
            parametros.Expedidas ? Libro(SentidoIva.Repercutido) : [],
            parametros.Recibidas ? Libro(SentidoIva.Soportado) : [],
            Calculo303.Calcular(delPeriodo, prorrata), prorrata, prorrataProvisional is not null, generado);
    }

    /// <summary>Nombre del fichero: «Libros IVA 2026 1T (20261123-1530).pdf», «Libro IVA expedidas 2026 marzo de 2026 (…).xlsx».</summary>
    public static string NombreFichero(ResultadoLibrosIva r, string extension)
    {
        var p = r.Parametros;
        var libro = p.Expedidas && p.Recibidas ? "Libros IVA" : p.Expedidas ? "Libro IVA expedidas" : "Libro IVA recibidas";
        var periodo = p.Periodo.Replace("/", "-", StringComparison.Ordinal);
        if (!periodo.Contains(r.Ejercicio.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)) periodo = $"{r.Ejercicio} {periodo}";
        return string.Create(CultureInfo.InvariantCulture, $"{libro} {periodo} ({r.Generado:yyyyMMdd-HHmm}){extension}");
    }
}

/// <summary>Lee las líneas de IVA del ejercicio (las mismas del 303 y de la liquidación) y calcula los libros registro del periodo.</summary>
public sealed class GeneradorLibrosIva(IBaseDatos bd)
{
    public async Task<ResultadoLibrosIva> GenerarAsync(ContextoSociedadEjercicio contexto, string razonSocial, ParametrosLibrosIva parametros,
        CancellationToken cancelacion = default)
    {
        if (parametros.Error(contexto.Ejercicio) is { } error) throw new InvalidOperationException(error);
        var (lineas, provisional, _, _) = await new LectorLineasIva(bd).CalcularAsync(contexto, cancelacion);
        return CalculadoraLibrosIva.Calcular(razonSocial, contexto.Ejercicio, parametros, lineas, provisional, DateTime.Now);
    }
}
