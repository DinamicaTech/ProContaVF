using System.Globalization;

namespace ProConta.Contabilidad.Mayor;

/// <summary>Números de factura del Mayor: «F2024001», «F-2024001» y «2024001» son la misma factura.</summary>
public static class Facturas
{
    /// <summary>Forma comparable de un número de factura (sin espacios, en mayúsculas y sin la «F» inicial); vacío si no hay.</summary>
    public static string Normalizar(string? documento)
    {
        if (string.IsNullOrWhiteSpace(documento)) return "";
        var t = documento.Trim().ToUpperInvariant().Replace(" ", "");
        if (t.Length > 1 && t[0] == 'F' && (char.IsDigit(t[1]) || t[1] == '-'))
        {
            t = t[1..];
            if (t.Length > 1 && t[0] == '-') t = t[1..];
        }
        return t;
    }

    /// <summary>Formas en que puede estar grabado un número normalizado (para buscarlo en la BD).</summary>
    public static IEnumerable<string> Variantes(string normalizado) =>
        normalizado.Length == 0 ? [] : new[] { normalizado, "F" + normalizado, "F-" + normalizado }.Distinct(StringComparer.Ordinal);
}

/// <summary>
/// Contrapartida de un apunte (contabilidad/mayor 2026-10-31 09:40): las otras líneas del asiento, preferiblemente del lado
/// contrario, sin contar las cuentas de IVA (472 y 477); si queda una sola cuenta es esa, y si quedan varias, «Varias».
/// </summary>
public static class Contrapartidas
{
    public const string Varias = "Varias";
    public const string Ninguna = "—";

    /// <summary>Cuentas de IVA soportado (472) y repercutido (477), que no deciden la contrapartida.</summary>
    public static bool EsCuentaIva(string cuenta) =>
        cuenta.StartsWith("472", StringComparison.Ordinal) || cuenta.StartsWith("477", StringComparison.Ordinal);

    /// <summary>Cuentas candidatas a contrapartida del apunte (sin repetir, en orden del asiento).</summary>
    public static IReadOnlyList<string> Candidatas(string cuenta, int orden, decimal debe, IReadOnlyList<LineaContrapartida> asiento)
    {
        var otras = asiento.Where(l => l.Orden != orden && !string.Equals(l.Cuenta, cuenta, StringComparison.Ordinal)).ToList();
        var alDebe = debe > 0;
        var opuestas = otras.Where(l => alDebe ? l.Haber > 0 : l.Debe > 0).ToList();
        IEnumerable<LineaContrapartida> elegidas = opuestas.Where(l => !EsCuentaIva(l.Cuenta)).ToList();
        if (!elegidas.Any()) elegidas = opuestas;
        if (!elegidas.Any()) elegidas = otras.Where(l => !EsCuentaIva(l.Cuenta)).ToList();
        if (!elegidas.Any()) elegidas = otras;
        return elegidas.Select(l => l.Cuenta).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Texto de la columna: la cuenta, «Varias» o «—».</summary>
    public static string Texto(IReadOnlyList<string> candidatas) => candidatas.Count switch { 0 => Ninguna, 1 => candidatas[0], _ => Varias };

    /// <summary>Las otras cuentas del asiento con su importe (Debe − Haber), sumado por cuenta, para la ayuda.</summary>
    public static IReadOnlyList<ImporteCuenta> Detalle(string cuenta, int orden, IReadOnlyList<LineaContrapartida> asiento) =>
        asiento.Where(l => l.Orden != orden && !string.Equals(l.Cuenta, cuenta, StringComparison.Ordinal))
            .GroupBy(l => l.Cuenta, StringComparer.Ordinal)
            .Select(g => new ImporteCuenta(g.Key, g.Sum(l => l.Importe)))
            .ToList();
}

/// <summary>Una cuenta con un importe (Debe − Haber).</summary>
public sealed record ImporteCuenta(string Cuenta, decimal Importe);

/// <summary>Filtros y estado de la vista que se aplican al calcular.</summary>
/// <param name="FiltroFacturas">Solo las filas de estos números de factura normalizados (en las cuentas de la vista).</param>
/// <param name="Texto">Filtro libre: concepto o documento que contiene el texto, número de asiento o importe exacto.</param>
/// <param name="Quitadas">Claves de las filas quitadas de la vista con Supr.</param>
/// <param name="SoloNoPunteados">Oculta las filas cuyos apuntes están todos punteados.</param>
public sealed record OpcionesMayor(
    VistaMayor Vista = VistaMayor.Normal, IReadOnlyCollection<string>? FiltroFacturas = null, string? Texto = null, IReadOnlySet<string>? Quitadas = null,
    bool SoloNoPunteados = false)
{
    public bool HayFiltros => FiltroFacturas is { Count: > 0 } || !string.IsNullOrWhiteSpace(Texto) || SoloNoPunteados;
}

/// <summary>Una fila del Mayor: un apunte (vista Normal) o un grupo de apuntes (las demás vistas).</summary>
public sealed class FilaMayor
{
    /// <summary>Identifica la fila dentro de su vista (para quitarla con Supr).</summary>
    public required string Clave { get; init; }
    public required string Cuenta { get; init; }
    public required string NombreCuenta { get; init; }
    public required int Ejercicio { get; init; }
    public required int IdSociedadEjercicio { get; init; }
    public required bool EjercicioCerrado { get; init; }
    public required DateTime Fecha { get; init; }
    public required int IdAsiento { get; init; }
    public required int Orden { get; init; }
    public required string Concepto { get; init; }
    public required string? Documento { get; init; }
    public required decimal Debe { get; init; }
    public required decimal Haber { get; init; }
    /// <summary>Saldo acumulado en orden de fecha (deudor positivo). En una apertura que continúa, el saldo que traía.</summary>
    public decimal Saldo { get; internal set; }
    public required bool EsApertura { get; init; }
    /// <summary>Apertura de un ejercicio que sigue al anterior de la vista: se ve, pero no suma ni en el saldo ni en los totales.</summary>
    public required bool AperturaContinua { get; init; }
    /// <summary>Si la apertura no coincide con el saldo con que acabó la cuenta el ejercicio anterior, lo explica.</summary>
    public string? AvisoApertura { get; init; }
    public required string Contrapartida { get; init; }
    public required string NombreContrapartida { get; init; }
    /// <summary>Cuentas a las que lleva «Ir a la contrapartida» (una, o varias si dice «Varias»).</summary>
    public required IReadOnlyList<string> CuentasContrapartida { get; init; }
    /// <summary>Las otras cuentas del asiento con su importe, para la ayuda de la contrapartida.</summary>
    public required IReadOnlyList<ImporteCuenta> DetalleContrapartida { get; init; }
    public required string? Notas { get; init; }
    public required DateTime? FechaDocumento { get; init; }
    public required DateTime? GrabadoEl { get; init; }
    public required DateTime? ModificadoEl { get; init; }
    /// <summary>Apuntes que resume la fila (1 en la vista Normal).</summary>
    public required IReadOnlyList<int> IdsApunte { get; init; }
    /// <summary>Asientos de la fila (para verlos).</summary>
    public required IReadOnlyList<int> IdsAsiento { get; init; }
    /// <summary>Clave del color del saldo: «43» (clientes y deudores), el grupo común de las cuentas de la vista, o «0» si se mezclan.</summary>
    public string GrupoSaldo { get; internal set; } = "0";
    /// <summary>Todos los apuntes de la fila están punteados.</summary>
    public bool Punteado { get; init; }
    /// <summary>Algunos apuntes de la fila están punteados y otros no (vistas agrupadas).</summary>
    public bool PunteoParcial { get; init; }
    /// <summary>Algún apunte de la fila es de un asiento automático: su concepto y su cuenta no se cambian desde el Mayor.</summary>
    public bool EsAutomatico { get; init; }
    /// <summary>Apuntes de la contrapartida (las líneas de la cuenta de destino, si es una sola), para «Cambiar la contrapartida».</summary>
    public IReadOnlyList<int> IdsApunteContrapartida { get; init; } = [];

    public decimal Importe => Debe - Haber;
    public string FacturaNormalizada => Facturas.Normalizar(Documento);
    public int NumeroApuntes => IdsApunte.Count;
    /// <summary>Lo que suma en los totales y en el saldo.</summary>
    public decimal ImporteQueSuma => AperturaContinua ? 0m : Importe;
}

/// <summary>Resultado de calcular la vista: filas en orden de fecha, totales sobre todas ellas y aviso de saldo parcial.</summary>
public sealed record ResultadoMayor(
    IReadOnlyList<FilaMayor> Filas, decimal TotalDebe, decimal TotalHaber, int NumeroApuntes, int Ocultas, int Quitadas, string? AvisoParcial)
{
    public static readonly ResultadoMayor Vacio = new([], 0m, 0m, 0, 0, 0, null);

    public decimal Saldo => TotalDebe - TotalHaber;
    public bool EsParcial => AvisoParcial is not null;

    /// <summary>Invariante: el saldo final es la suma de lo que suma cada fila (Debe − Haber), y el último saldo acumulado.</summary>
    public decimal SaldoFinal => Filas.Count == 0 ? 0m : Filas[^1].Saldo;
}

/// <summary>
/// Calcula las filas del Mayor sin tocar la BD (contabilidad/mayor): orden por fecha, apertura de los ejercicios que siguen al
/// anterior sin sumar, vistas Normal, Compacto, Solo pendientes y Agrupar 700, contrapartida, filtros, filas quitadas, saldo
/// acumulado y totales sobre todas las filas. Los asientos de cierre no se muestran nunca.
/// </summary>
public static class CalculadoraMayor
{
    /// <summary>Por debajo de este importe una factura netea a cero en «Solo pendientes».</summary>
    public const decimal ToleranciaPendiente = 0.05m;

    public static ResultadoMayor Calcular(DatosMayor datos, IReadOnlyList<EjercicioMayor> ejercicios, OpcionesMayor opciones)
    {
        var porId = ejercicios.ToDictionary(e => e.IdSociedadEjercicio);
        var anios = ejercicios.Select(e => e.Ejercicio).ToHashSet();
        // Una apertura «continúa» si el ejercicio anterior también está en la vista: su saldo ya viene de él.
        bool Continua(ApunteMayor a) => a.EsApertura && anios.Contains(a.Ejercicio - 1);

        var lineasPorAsiento = datos.Lineas.GroupBy(l => l.IdAsiento).ToDictionary(g => g.Key, g => (IReadOnlyList<LineaContrapartida>)g.OrderBy(l => l.Orden).ToList());
        var apuntes = datos.Apuntes.Where(a => a.TipoAsiento != TiposAsientoMayor.Cierre)
            .OrderBy(a => a.Fecha).ThenBy(a => a.EsApertura ? 0 : 1).ThenBy(a => a.IdAsiento).ThenBy(a => a.Orden).ToList();
        var avisos = AvisosApertura(apuntes, Continua);

        var filas = opciones.Vista switch
        {
            VistaMayor.Normal => apuntes.Select(a => Fila(a, porId, datos, lineasPorAsiento, Continua(a), avisos)).ToList(),
            VistaMayor.Agrupar700 => Agrupar(apuntes, a => $"{a.Cuenta}|{a.Fecha:yyyyMMdd}|{Clave(a)}|{(a.Debe > 0 ? 'D' : 'H')}|{(Continua(a) ? 1 : 0)}",
                porId, datos, lineasPorAsiento, Continua, avisos),
            _ => Agrupar(apuntes.Where(a => !Continua(a)), a => $"{a.Cuenta}|{Clave(a)}", porId, datos, lineasPorAsiento, Continua, avisos),
        };

        var total = filas.Count;
        if (opciones.Vista == VistaMayor.SoloPendientes)
            filas = filas.Where(f => f.FacturaNormalizada.Length == 0 || Math.Abs(f.Importe) > ToleranciaPendiente).ToList();
        var pendientesOcultas = total - filas.Count;

        var antes = filas.Count;
        if (opciones.FiltroFacturas is { Count: > 0 } ff)
        {
            var set = ff.Select(Facturas.Normalizar).Where(x => x.Length > 0).ToHashSet(StringComparer.Ordinal);
            filas = filas.Where(f => set.Contains(f.FacturaNormalizada)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(opciones.Texto))
            filas = filas.Where(f => CumpleTexto(f, opciones.Texto!)).ToList();
        if (opciones.SoloNoPunteados)
            filas = filas.Where(f => !f.Punteado).ToList();
        var ocultas = antes - filas.Count;

        var quitadas = 0;
        if (opciones.Quitadas is { Count: > 0 } q)
        {
            var n = filas.Count;
            filas = filas.Where(f => !q.Contains(f.Clave)).ToList();
            quitadas = n - filas.Count;
        }

        // Saldo acumulado en orden de fecha sobre lo que se ve; una apertura que continúa enseña el saldo que traía.
        var grupo = Formato.GrupoComun(filas.Select(f => f.Cuenta)) ?? "0";
        decimal saldo = 0m, debe = 0m, haber = 0m;
        var numero = 0;
        foreach (var f in filas)
        {
            saldo += f.ImporteQueSuma;
            f.Saldo = saldo;
            f.GrupoSaldo = grupo;
            if (!f.AperturaContinua) { debe += f.Debe; haber += f.Haber; numero += f.NumeroApuntes; }
        }

        string? aviso = null;
        if (ocultas > 0 || quitadas > 0)
        {
            var partes = new List<string>();
            if (ocultas > 0) partes.Add(Plural(ocultas, "fila oculta por los filtros", "filas ocultas por los filtros"));
            if (quitadas > 0) partes.Add(Plural(quitadas, "fila quitada de la vista", "filas quitadas de la vista"));
            aviso = "Saldo parcial: " + string.Join(" y ", partes) + ". El saldo y los totales solo cuentan lo que se ve.";
        }
        return new ResultadoMayor(filas, debe, haber, numero, ocultas + pendientesOcultas, quitadas, aviso);
    }

    private static string Clave(ApunteMayor a) => Facturas.Normalizar(a.Documento) is { Length: > 0 } f ? "F:" + f : "A:" + a.IdAsiento;

    private static bool CumpleTexto(FilaMayor f, string texto)
    {
        var t = texto.Trim();
        var ci = CultureInfo.GetCultureInfo("es-ES").CompareInfo;
        const CompareOptions op = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        if (ci.IndexOf(f.Concepto, t, op) >= 0) return true;
        if (f.Documento is { } d && ci.IndexOf(d, t, op) >= 0) return true;
        if (int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && f.IdsAsiento.Contains(n)) return true;
        if (LeerImporte(t) is decimal importe)
            return Math.Abs(f.Debe) == importe || Math.Abs(f.Haber) == importe || Math.Abs(f.Importe) == importe;
        return false;
    }

    /// <summary>Lee un importe escrito en español («1.234,56», «1234,56 €»); null si no lo es.</summary>
    internal static decimal? LeerImporte(string texto)
    {
        var t = texto.Replace("€", "").Replace(" ", "").Replace("−", "-").Trim();
        if (!t.Contains(',')) return null; // sin decimales es un número de asiento o un texto
        return decimal.TryParse(t, NumberStyles.Number, CultureInfo.GetCultureInfo("es-ES"), out var v) ? Math.Abs(Math.Round(v, 2)) : null;
    }

    /// <summary>
    /// Aperturas que continúan y no coinciden con el saldo con que acabó la cuenta el ejercicio anterior (sin cierre): se avisa en
    /// la fila, porque el saldo sigue el del ejercicio anterior y no el de la apertura.
    /// </summary>
    private static Dictionary<int, string> AvisosApertura(IReadOnlyList<ApunteMayor> apuntes, Func<ApunteMayor, bool> continua)
    {
        var avisos = new Dictionary<int, string>();
        var saldos = new Dictionary<(string Cuenta, int Ejercicio), decimal>();
        foreach (var a in apuntes)
            if (!continua(a))
            {
                var k = (a.Cuenta, a.Ejercicio);
                saldos[k] = saldos.GetValueOrDefault(k) + a.Importe;
            }
        // El saldo al cierre de un ejercicio es la suma de todo lo anterior que sí suma.
        decimal SaldoHasta(string cuenta, int ejercicio) => saldos.Where(s => s.Key.Cuenta == cuenta && s.Key.Ejercicio <= ejercicio).Sum(s => s.Value);
        foreach (var g in apuntes.Where(continua).GroupBy(a => (a.Cuenta, a.Ejercicio)))
        {
            var apertura = g.Sum(a => a.Importe);
            var anterior = SaldoHasta(g.Key.Cuenta, g.Key.Ejercicio - 1);
            if (Math.Round(apertura - anterior, 2) != 0)
                avisos[g.First().IdApunte] =
                    $"La apertura de {g.Key.Ejercicio} ({Importe(apertura)}) no coincide con el saldo con que acabó {g.Key.Ejercicio - 1} ({Importe(anterior)}): " +
                    "el saldo de la vista sigue el del ejercicio anterior. Revisa el cierre de " + (g.Key.Ejercicio - 1) + " o la apertura de " + g.Key.Ejercicio + ".";
        }
        return avisos;
    }

    private static FilaMayor Fila(ApunteMayor a, IReadOnlyDictionary<int, EjercicioMayor> ejercicios, DatosMayor datos,
        IReadOnlyDictionary<int, IReadOnlyList<LineaContrapartida>> lineas, bool continua, IReadOnlyDictionary<int, string> avisos)
    {
        var asiento = lineas.GetValueOrDefault(a.IdAsiento) ?? [];
        var candidatas = a.EsApertura ? [] : Contrapartidas.Candidatas(a.Cuenta, a.Orden, a.Debe, asiento);
        var texto = Contrapartidas.Texto(candidatas);
        return new FilaMayor
        {
            Clave = "a" + a.IdApunte,
            Cuenta = a.Cuenta,
            NombreCuenta = datos.Nombres.GetValueOrDefault(a.Cuenta, ""),
            Ejercicio = a.Ejercicio,
            IdSociedadEjercicio = a.IdSociedadEjercicio,
            EjercicioCerrado = ejercicios.TryGetValue(a.IdSociedadEjercicio, out var e) && e.Cerrado,
            Fecha = a.Fecha,
            IdAsiento = a.IdAsiento,
            Orden = a.Orden,
            Concepto = a.Concepto,
            Documento = a.Documento,
            Debe = a.Debe,
            Haber = a.Haber,
            EsApertura = a.EsApertura,
            AperturaContinua = continua,
            AvisoApertura = avisos.GetValueOrDefault(a.IdApunte),
            Contrapartida = texto,
            NombreContrapartida = a.EsApertura ? "Asiento de apertura" : candidatas.Count == 1 ? datos.Nombres.GetValueOrDefault(candidatas[0], "") : "",
            CuentasContrapartida = candidatas,
            DetalleContrapartida = a.EsApertura ? [] : Contrapartidas.Detalle(a.Cuenta, a.Orden, asiento),
            Notas = a.Notas,
            FechaDocumento = a.FechaDocumento,
            GrabadoEl = a.GrabadoEl,
            ModificadoEl = a.ModificadoEl,
            IdsApunte = [a.IdApunte],
            IdsAsiento = [a.IdAsiento],
            Punteado = a.Punteado,
            EsAutomatico = a.EsAutomatico,
            IdsApunteContrapartida = candidatas.Count == 1
                ? asiento.Where(l => l.Orden != a.Orden && l.Cuenta == candidatas[0] && l.IdApunte != 0).Select(l => l.IdApunte).ToList()
                : [],
        };
    }

    private static List<FilaMayor> Agrupar(IEnumerable<ApunteMayor> apuntes, Func<ApunteMayor, string> clave,
        IReadOnlyDictionary<int, EjercicioMayor> ejercicios, DatosMayor datos, IReadOnlyDictionary<int, IReadOnlyList<LineaContrapartida>> lineas,
        Func<ApunteMayor, bool> continua, IReadOnlyDictionary<int, string> avisos)
    {
        var grupos = new Dictionary<string, List<ApunteMayor>>(StringComparer.Ordinal);
        var orden = new List<string>();
        foreach (var a in apuntes)
        {
            var k = clave(a);
            if (!grupos.TryGetValue(k, out var l)) { grupos[k] = l = []; orden.Add(k); }
            l.Add(a);
        }
        var filas = new List<FilaMayor>(orden.Count);
        foreach (var k in orden)
        {
            var g = grupos[k];
            var primera = Fila(g[0], ejercicios, datos, lineas, continua(g[0]), avisos);
            if (g.Count == 1) { filas.Add(primera.ConClave("g" + k)); continue; }
            var cuentas = g.SelectMany(a => a.EsApertura ? [] : Contrapartidas.Candidatas(a.Cuenta, a.Orden, a.Debe, lineas.GetValueOrDefault(a.IdAsiento) ?? []))
                .Distinct(StringComparer.Ordinal).ToList();
            var detalle = g.SelectMany(a => a.EsApertura ? [] : Contrapartidas.Detalle(a.Cuenta, a.Orden, lineas.GetValueOrDefault(a.IdAsiento) ?? []))
                .GroupBy(d => d.Cuenta, StringComparer.Ordinal).Select(x => new ImporteCuenta(x.Key, x.Sum(d => d.Importe))).ToList();
            filas.Add(new FilaMayor
            {
                Clave = "g" + k,
                Cuenta = primera.Cuenta,
                NombreCuenta = primera.NombreCuenta,
                Ejercicio = primera.Ejercicio,
                IdSociedadEjercicio = primera.IdSociedadEjercicio,
                EjercicioCerrado = primera.EjercicioCerrado,
                Fecha = primera.Fecha,
                IdAsiento = primera.IdAsiento,
                Orden = primera.Orden,
                Concepto = primera.Concepto,
                Documento = g.Select(a => a.Documento).FirstOrDefault(d => !string.IsNullOrWhiteSpace(d)),
                Debe = g.Sum(a => a.Debe),
                Haber = g.Sum(a => a.Haber),
                EsApertura = g.All(a => a.EsApertura),
                AperturaContinua = primera.AperturaContinua,
                AvisoApertura = primera.AvisoApertura,
                Contrapartida = Contrapartidas.Texto(cuentas),
                NombreContrapartida = cuentas.Count == 1 ? datos.Nombres.GetValueOrDefault(cuentas[0], "") : "",
                CuentasContrapartida = cuentas,
                DetalleContrapartida = detalle,
                Notas = primera.Notas,
                FechaDocumento = g.Select(a => a.FechaDocumento).Where(f => f is not null).Min(),
                GrabadoEl = g.Select(a => a.GrabadoEl).Where(f => f is not null).Min(),
                ModificadoEl = g.Select(a => a.ModificadoEl).Where(f => f is not null).Max(),
                IdsApunte = g.Select(a => a.IdApunte).ToList(),
                IdsAsiento = g.Select(a => a.IdAsiento).Distinct().ToList(),
                Punteado = g.All(a => a.Punteado),
                PunteoParcial = g.Any(a => a.Punteado) && !g.All(a => a.Punteado),
                EsAutomatico = g.Any(a => a.EsAutomatico),
            });
        }
        return filas;
    }

    private static FilaMayor ConClave(this FilaMayor f, string clave) => new()
    {
        Clave = clave, Cuenta = f.Cuenta, NombreCuenta = f.NombreCuenta, Ejercicio = f.Ejercicio, IdSociedadEjercicio = f.IdSociedadEjercicio,
        EjercicioCerrado = f.EjercicioCerrado, Fecha = f.Fecha, IdAsiento = f.IdAsiento, Orden = f.Orden, Concepto = f.Concepto, Documento = f.Documento,
        Debe = f.Debe, Haber = f.Haber, EsApertura = f.EsApertura, AperturaContinua = f.AperturaContinua, AvisoApertura = f.AvisoApertura,
        Contrapartida = f.Contrapartida, NombreContrapartida = f.NombreContrapartida, CuentasContrapartida = f.CuentasContrapartida,
        DetalleContrapartida = f.DetalleContrapartida, Notas = f.Notas, FechaDocumento = f.FechaDocumento, GrabadoEl = f.GrabadoEl,
        ModificadoEl = f.ModificadoEl, IdsApunte = f.IdsApunte, IdsAsiento = f.IdsAsiento,
        Punteado = f.Punteado, PunteoParcial = f.PunteoParcial, EsAutomatico = f.EsAutomatico, IdsApunteContrapartida = f.IdsApunteContrapartida,
    };

    internal static string Importe(decimal v) =>
        (v < 0 ? "−" : "") + Math.Abs(Math.Round(v, 2)).ToString("#,##0.00", Formato.Cultura) + " €";

    private static string Plural(int n, string uno, string varios) => n.ToString("#,##0", Formato.Cultura) + " " + (n == 1 ? uno : varios);
}

/// <summary>Pequeñas ayudas de formato del dominio (los textos de aviso van en euros, como en pantalla).</summary>
internal static class Formato
{
    public static readonly CultureInfo Cultura = Crear();

    private static CultureInfo Crear()
    {
        var c = (CultureInfo)CultureInfo.GetCultureInfo("es-ES").Clone();
        c.NumberFormat.NumberGroupSizes = [3];
        return CultureInfo.ReadOnly(c);
    }

    /// <summary>
    /// Clave del color del saldo de un conjunto de cuentas (la misma regla que ReglaSaldo.ClaveComun):
    /// «43» si todas son de clientes o deudores (43 y 44), el grupo si todas son del mismo grupo y null si se mezclan.
    /// </summary>
    public static string? GrupoComun(IEnumerable<string> cuentas)
    {
        char? g = null;
        bool? cobro = null;
        foreach (var c in cuentas)
        {
            if (string.IsNullOrEmpty(c)) continue;
            var esCobro = c.StartsWith("43", StringComparison.Ordinal) || c.StartsWith("44", StringComparison.Ordinal);
            if (g is null) { g = c[0]; cobro = esCobro; }
            else if (g != c[0] || cobro != esCobro) return null;
        }
        return g is null ? null : cobro == true ? "43" : g.Value.ToString();
    }
}
