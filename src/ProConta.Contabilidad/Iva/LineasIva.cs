using ProConta.Contabilidad.CentrosCoste;

namespace ProConta.Contabilidad.Iva;

/// <summary>Libro del que sale una línea de IVA.</summary>
public enum SentidoIva { Repercutido, Soportado }

/// <summary>Tipo de operación de una línea de IVA soportado (las repercutidas son siempre nacionales).</summary>
public enum OperacionIva { Nacional, Intracomunitaria, Importacion }

/// <summary>
/// Derecho a deducir de la línea, según la actividad del centro de coste (Actividades.Deducible, con herencia): 0 no deducible,
/// 1 prorrata, 2 100 % deducible. <see cref="SinCentro"/>: el asiento no tiene centro o el centro no tiene actividad; la línea
/// no entra en el cálculo de lo deducible y sale como aviso.
/// </summary>
public enum DeduccionIva { SinCentro, NoDeducible, Prorrata, Deducible100 }

/// <summary>Textos de las líneas de IVA tal como salen en el libro de la gestoría (las fórmulas los usan como criterio).</summary>
public static class TextosIva
{
    public const string Repercutido = "Repercutido";
    public const string Soportado = "Soportado";
    public const string Nacional = "Nacional";
    public const string Intracomunitaria = "Intracomunitaria";
    public const string Importacion = "Importación";
    public const string SinCentro = "Sin centro";
    public const string NoDeducible = "No deducible";
    public const string Prorrata = "Prorrata";
    public const string Deducible100 = "100 % deducible";
    public const string Si = "Sí";
    public const string No = "No";
    public const string AvisoSinCentro = "Asiento sin centro de coste";
    public const string AvisoSinActividad = "Centro sin actividad de IVA";
    public const string AvisoFueraEjercicio = "Fecha fuera del ejercicio";

    public static string Texto(this SentidoIva s) => s == SentidoIva.Repercutido ? Repercutido : Soportado;

    public static string Texto(this OperacionIva o) => o switch
    {
        OperacionIva.Intracomunitaria => Intracomunitaria,
        OperacionIva.Importacion => Importacion,
        _ => Nacional,
    };

    public static string Texto(this DeduccionIva d) => d switch
    {
        DeduccionIva.NoDeducible => NoDeducible,
        DeduccionIva.Prorrata => Prorrata,
        DeduccionIva.Deducible100 => Deducible100,
        _ => SinCentro,
    };

    /// <summary>
    /// Deducción de una actividad en una fecha (la de la factura): Actividades.Deducible o el cambio vigente en esa fecha
    /// (contabilidad/procesos/cambio-deduccion-actividad). 0 no deducible, 1 prorrata, 2 (o cualquier otro) 100 %.
    /// </summary>
    public static DeduccionIva DeActividad(ActividadIva? actividad, DateTime fecha) => actividad?.DeducibleEn(fecha) switch
    {
        null => DeduccionIva.SinCentro,
        0 => DeduccionIva.NoDeducible,
        1 => DeduccionIva.Prorrata,
        _ => DeduccionIva.Deducible100,
    };
}

/// <summary>
/// Una línea de IVA: un desglose de los libros IVASoportado/DesglosesIVASoportado o IVARepercutido/DesglosesIVARepercutido del
/// ejercicio, con la fecha contable de su asiento. <paramref name="Trimestre"/> es 0 si el asiento tiene fecha fuera del
/// ejercicio (no entra en ningún trimestre y sale como aviso). <paramref name="TipoIva"/> es el porcentaje (21 = 21 %).
/// <paramref name="CuotaAutorrepercutida"/>: la cuota devengada por el propio comprador (intracomunitarias, inversión del
/// sujeto pasivo e importaciones); en las demás, 0. Base y cuota con el signo del libro (positivas en una factura normal).
/// </summary>
public sealed record LineaIva(
    DateTime Fecha, int Trimestre, int IdAsiento, SentidoIva Sentido, OperacionIva Operacion, bool Isp, decimal TipoIva,
    string? Centro, string? Departamento, string? Actividad, DeduccionIva Deduccion, string Tercero, string? Nif, string? Factura,
    decimal Base, decimal Cuota, decimal CuotaAutorrepercutida, string? CuentaIva, string? Aviso)
{
    /// <summary>Centro de coste del que sale la clasificación (el del apunte de IVA o, si no lo lleva, el de gasto o ingreso); null sin centro.</summary>
    public int? IdCentroCoste { get; init; }

    /// <summary>Línea de IVA soportado autorrepercutida: intracomunitaria, inversión del sujeto pasivo o importación.</summary>
    public bool Autorrepercutida => Sentido == SentidoIva.Soportado && (Operacion != OperacionIva.Nacional || Isp);
}

/// <summary>Registro de un libro de IVA tal como se lee de la BD, con sus desgloses y los apuntes de su asiento.</summary>
/// <param name="CodigoTipoOperacion">TiposOperacion.Codigo del registro (SOPORTADO_0 interior, SOPORTADO_1 y SOPORTADO_2 exterior).</param>
/// <param name="FechaFactura">
/// Fecha de expedición de la factura del libro: decide la deducción cuando la actividad la cambia desde una fecha
/// (contabilidad/procesos/cambio-deduccion-actividad). Null: la fecha contable.
/// </param>
public sealed record RegistroIvaLeido(
    SentidoIva Sentido, int IdAsiento, DateTime FechaContable, string? CodigoTipoOperacion, bool IspLibro, string Tercero, string? Nif, string? Factura,
    IReadOnlyList<DesgloseIvaLeido> Desgloses, IReadOnlyList<ApunteIvaLeido> Apuntes, DateTime? FechaFactura = null);

/// <summary>Desglose de un registro de IVA: base, tipo (porcentaje), cuota y cuota autorrepercutida grabadas en el libro.</summary>
public sealed record DesgloseIvaLeido(decimal Base, decimal TipoIva, decimal Cuota, decimal CuotaAutorrepercutida);

/// <summary>Apunte del asiento de un registro de IVA (cuenta, importes y centro de coste).</summary>
public sealed record ApunteIvaLeido(string Cuenta, decimal Debe, decimal Haber, int? IdCentroCoste);

/// <summary>Centro de coste para las líneas de IVA: su raíz (el centro de nivel 1) y su nombre.</summary>
public sealed record CentroIva(string Raiz, string Nombre);

/// <summary>
/// Convierte los registros de los libros de IVA en líneas de IVA (contabilidad/modelos-tributarios, informe para la gestoría).
/// Sin BD: lo comparten el lector de Access y las pruebas.
/// <list type="bullet">
/// <item>Centro: el del apunte de IVA del asiento (472 en el soportado, 477 en el repercutido), el de importe igual a la cuota del
/// desglose si hay varios; si el asiento no lo lleva ahí, el del apunte de gasto (6, 2) o de ingreso (7). Es el enlace que
/// reproduce el departamento (CODCDP) del libro anterior en todas las líneas de 2026.</item>
/// <item>Deducción: la de la actividad del centro, con herencia (<see cref="LectorActividadesIva"/>), vigente en la fecha de la
/// factura (contabilidad/procesos/cambio-deduccion-actividad 2026-11-15 19:43). Sin centro o sin actividad: «Sin centro», con aviso.</item>
/// <item>Operación: un registro interior (SOPORTADO_0) es nacional; uno exterior (SOPORTADO_1 o SOPORTADO_2) es intracomunitario
/// si su asiento autorrepercute el IVA en una 477 y es una importación si no (el IVA de importación va a la 4724 sin 477,
/// compras/factura-compra/contabilizacion 2026-10-31 07:46). Así no depende del código del tipo de operación, que los datos
/// importados de ContaSol traen al revés que el catálogo (ver el nodo).</item>
/// <item>Inversión del sujeto pasivo: la marca del libro, o una operación nacional con IVA autorrepercutido en una 477.</item>
/// </list>
/// </summary>
public static class ClasificadorLineasIva
{
    public const string CodigoInterior = "SOPORTADO_0";

    public static IReadOnlyList<LineaIva> Clasificar(
        IEnumerable<RegistroIvaLeido> registros, int ejercicio, IReadOnlyDictionary<int, ActividadIva> actividades, IReadOnlyDictionary<int, CentroIva> centros)
    {
        var r = new List<LineaIva>();
        foreach (var reg in registros)
        {
            var soportado = reg.Sentido == SentidoIva.Soportado;
            var prefijoIva = soportado ? "472" : "477";
            var hay477 = reg.Apuntes.Any(a => a.Cuenta.StartsWith("477", StringComparison.Ordinal));
            var operacion = !soportado || reg.CodigoTipoOperacion is null || reg.CodigoTipoOperacion == CodigoInterior || !reg.CodigoTipoOperacion.StartsWith("SOPORTADO", StringComparison.Ordinal)
                ? OperacionIva.Nacional
                : hay477 ? OperacionIva.Intracomunitaria : OperacionIva.Importacion;
            var isp = soportado && (reg.IspLibro || (operacion == OperacionIva.Nacional && hay477));
            var fueraEjercicio = reg.FechaContable.Year != ejercicio;
            var trimestre = fueraEjercicio ? 0 : (reg.FechaContable.Month - 1) / 3 + 1;
            foreach (var d in reg.Desgloses)
            {
                var apunteIva = ApunteIva(reg.Apuntes, prefijoIva, d.Cuota);
                var idCentro = apunteIva?.IdCentroCoste
                    ?? reg.Apuntes.FirstOrDefault(a => a.IdCentroCoste is not null && a.Cuenta.Length > 0 && a.Cuenta[0] is '6' or '7' or '2')?.IdCentroCoste;
                var actividad = idCentro is int id ? actividades.GetValueOrDefault(id) : null;
                var centro = idCentro is int ic ? centros.GetValueOrDefault(ic) : null;
                var deduccion = TextosIva.DeActividad(actividad, reg.FechaFactura ?? reg.FechaContable);
                var aviso = fueraEjercicio ? TextosIva.AvisoFueraEjercicio
                    : idCentro is null ? TextosIva.AvisoSinCentro
                    : actividad is null ? TextosIva.AvisoSinActividad : null;
                var autorrepercutida = soportado && (operacion != OperacionIva.Nacional || isp)
                    ? (d.CuotaAutorrepercutida != 0m ? d.CuotaAutorrepercutida : d.Cuota)
                    : 0m;
                r.Add(new LineaIva(reg.FechaContable.Date, trimestre, reg.IdAsiento, reg.Sentido, operacion, isp, d.TipoIva,
                    centro?.Raiz, centro?.Nombre, actividad?.Nombre, deduccion, reg.Tercero, reg.Nif, reg.Factura,
                    d.Base, d.Cuota, autorrepercutida, apunteIva?.Cuenta, aviso) { IdCentroCoste = idCentro });
            }
        }
        return r.OrderBy(l => l.Fecha).ThenBy(l => l.IdAsiento).ThenBy(l => l.Sentido).ToList();
    }

    /// <summary>El apunte de IVA del asiento con centro: el de importe igual a la cuota; si no hay, el de mayor importe.</summary>
    private static ApunteIvaLeido? ApunteIva(IReadOnlyList<ApunteIvaLeido> apuntes, string prefijo, decimal cuota)
    {
        var candidatos = apuntes.Where(a => a.Cuenta.StartsWith(prefijo, StringComparison.Ordinal) && a.IdCentroCoste is not null).ToList();
        if (candidatos.Count == 0) return null;
        return candidatos.FirstOrDefault(a => Math.Abs(a.Debe - a.Haber) == Math.Abs(cuota))
            ?? candidatos.OrderByDescending(a => Math.Abs(a.Debe - a.Haber)).First();
    }
}
