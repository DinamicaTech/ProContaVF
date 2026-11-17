using System.Globalization;

namespace ProConta.Contabilidad.FacturasPendientes;

/// <summary>
/// Parámetros del ejercicio (organizacion/sociedades-ejercicios, esquema 67); vacíos, los de siempre: 4009000 facturas pendientes de
/// recibir, 6299000 gasto por defecto y 21 % de IVA supuesto.
/// </summary>
public sealed record ConfiguracionFacturasPendientes(string CuentaPendientes, string CuentaGasto, decimal TipoIva)
{
    public const string CuentaPendientesDefecto = "4009000";
    public const string CuentaGastoDefecto = "6299000";
    public const decimal TipoIvaDefecto = 21m;

    public static ConfiguracionFacturasPendientes Defecto { get; } = new(CuentaPendientesDefecto, CuentaGastoDefecto, TipoIvaDefecto);

    public static ConfiguracionFacturasPendientes Con(string? pendientes, string? gasto, decimal? tipo) => new(
        string.IsNullOrWhiteSpace(pendientes) ? CuentaPendientesDefecto : pendientes.Trim(),
        string.IsNullOrWhiteSpace(gasto) ? CuentaGastoDefecto : gasto.Trim(),
        tipo is decimal t and >= 0m ? t : TipoIvaDefecto);
}

/// <summary>Última factura de compra contabilizada del proveedor: de ella salen la proporción base / total y, si el proveedor no tiene otra, la cuenta de gasto y el centro.</summary>
public sealed record FacturaReferencia(DateTime Fecha, string? Numero, decimal Base, decimal Total, string? CuentaGasto, int? IdCentroCoste);

/// <summary>
/// Una subcuenta de proveedor o acreedor con lo que hace falta para decidir si entra: su saldo a 31/12 (Debe − Haber de los apuntes
/// contabilizados del ejercicio sin el cierre ni los lotes de este proceso), si está cuadrada en el Mayor (casilla «Cuadrada» con ese
/// mismo saldo) y sus datos de imputación.
/// </summary>
/// <param name="CuadradaConOtroSaldo">Tiene la casilla «Cuadrada», pero su saldo ha cambiado desde que se marcó (no entra, se avisa).</param>
public sealed record SubcuentaProveedor(string Cuenta, string Nombre, decimal Saldo, bool Cuadrada, bool CuadradaConOtroSaldo = false,
    FacturaReferencia? UltimaFactura = null, string? CuentaGastoDefecto = null, int? IdCentroDefecto = null);

/// <summary>Lo que el operador ha cambiado a mano en la propuesta de un proveedor (tabla FacturasPendientesAjustes).</summary>
public sealed record AjusteFacturaPendiente(string Cuenta, bool Excluido, decimal? BaseManual);

/// <summary>De dónde sale la base propuesta.</summary>
public enum OrigenBase
{
    /// <summary>Saldo × base / total de la última factura contabilizada del proveedor.</summary>
    UltimaFactura,
    /// <summary>Saldo / (1 + tipo de IVA supuesto del ejercicio): el proveedor no tiene facturas.</summary>
    TipoSupuesto,
}

/// <summary>De dónde salen la cuenta de gasto y el centro.</summary>
public enum OrigenGasto { Proveedor, UltimaFactura, Ejercicio }

/// <summary>Una fila de la propuesta: un proveedor con su saldo, la base calculada, la que se usa (la manual si la hay), la cuenta de gasto y si entra.</summary>
public sealed record FilaFacturaPendiente(string Cuenta, string Nombre, decimal Saldo, OrigenBase Origen, string OrigenTexto, decimal BaseCalculada, decimal? BaseManual,
    bool Incluido, string CuentaGasto, OrigenGasto OrigenGasto, int? IdCentroCoste)
{
    public decimal Base => BaseManual ?? BaseCalculada;
}

/// <summary>Una línea de un asiento propuesto.</summary>
public sealed record LineaFacturaPendiente(string Cuenta, decimal Debe, decimal Haber, string Concepto, int? IdCentroCoste = null);

/// <summary>
/// Propuesta de las facturas pendientes de recibir de un ejercicio: las filas (incluidas y excluidas), la provisión del 31/12, su
/// reversión del 1/1 del siguiente y los avisos.
/// </summary>
/// <param name="Fijos">Avisos que no dependen de lo marcado ni de las bases (ejercicio sin terminar, cuadres con otro saldo…).</param>
public sealed record PropuestaFacturasPendientes(int Ejercicio, DateTime Fecha, DateTime FechaReversion, ConfiguracionFacturasPendientes Configuracion,
    IReadOnlyList<FilaFacturaPendiente> Filas, IReadOnlyList<LineaFacturaPendiente> Provision, IReadOnlyList<LineaFacturaPendiente> Reversion, IReadOnlyList<string> Avisos,
    IReadOnlyList<string> Fijos)
{
    /// <summary>Ajustes a mano de la propuesta (lo que se guarda en FacturasPendientesAjustes).</summary>
    public IReadOnlyList<AjusteFacturaPendiente> Ajustes => Filas.Where(f => !f.Incluido || f.BaseManual is not null)
        .Select(f => new AjusteFacturaPendiente(f.Cuenta, !f.Incluido, f.BaseManual)).ToList();

    public IEnumerable<FilaFacturaPendiente> Incluidas => Filas.Where(f => f.Incluido);
    public decimal Total => Incluidas.Sum(f => f.Base);
    public decimal TotalDebe => Provision.Sum(l => l.Debe);
    public decimal TotalHaber => Provision.Sum(l => l.Haber);

    /// <summary>Mismos asientos (lo que se graba): sirve para confirmar solo lo que se ve.</summary>
    public bool Igual(PropuestaFacturasPendientes otra) =>
        Ejercicio == otra.Ejercicio && Provision.SequenceEqual(otra.Provision) && Reversion.SequenceEqual(otra.Reversion);
}

/// <summary>
/// Cálculo de las facturas pendientes de recibir (contabilidad/procesos/facturas-pendientes-recibir 2026-11-17 16:40), sin BD.
/// <list type="bullet">
/// <item>Entran las subcuentas de 7 dígitos de las 40 y 41 (sin la propia cuenta de facturas pendientes, ni las 4009 ni las 407 de
/// anticipos) cuadradas en el Mayor del ejercicio y con saldo deudor a 31/12.</item>
/// <item>Base: saldo × base / total de la última factura contabilizada del proveedor (acierta con el IVA reducido y con la retención); sin
/// facturas, saldo / (1 + tipo de IVA supuesto). Redondeo a céntimos alejándose de cero, por proveedor. El operador puede escribir otra
/// base o excluir el proveedor, y se respeta mientras siga en la lista.</item>
/// <item>Cuenta de gasto y centro: los de por defecto del proveedor; si no tiene, los de su última factura; si tampoco, la cuenta de gasto
/// por defecto del ejercicio, sin centro.</item>
/// <item>Provisión a 31/12: por proveedor, el gasto al Debe y la cuenta de facturas pendientes al Haber; reversión a 1/1 del siguiente, al
/// revés. El proveedor no se toca: repetir el proceso da lo mismo y no se realimenta.</item>
/// </list>
/// </summary>
public static class CalculoFacturasPendientes
{
    public static DateTime FechaProvision(int ejercicio) => new(ejercicio, 12, 31);

    public static DateTime FechaReversion(int ejercicio) => new(ejercicio + 1, 1, 1);

    public static string Descripcion(int ejercicio) => $"Facturas pendientes de recibir {ejercicio}";

    public static string DescripcionReversion(int ejercicio) => $"Reversión de las facturas pendientes de recibir {ejercicio}";

    public static string Concepto(string nombre)
    {
        var texto = $"Factura pendiente de recibir · {nombre.Trim()}";
        return texto.Length <= 255 ? texto : texto[..255];
    }

    public static string Euros(decimal v) => v.ToString("#,##0.00", CultureInfo.GetCultureInfo("es-ES")) + " €";

    private static string Porcentaje(decimal v) => v.ToString("0.##", CultureInfo.GetCultureInfo("es-ES")) + " %";

    /// <summary>Subcuenta de proveedor o acreedor que puede tener facturas pendientes de recibir (7 dígitos, 40 o 41, sin 4009 ni 407 ni la cuenta del proceso).</summary>
    public static bool EsCandidata(string cuenta, ConfiguracionFacturasPendientes cfg)
    {
        var c = (cuenta ?? "").Trim();
        return c.Length == 7 && c.All(char.IsAsciiDigit) && (c.StartsWith("40", StringComparison.Ordinal) || c.StartsWith("41", StringComparison.Ordinal))
            && !c.StartsWith("4009", StringComparison.Ordinal) && !c.StartsWith("407", StringComparison.Ordinal) && c != cfg.CuentaPendientes;
    }

    /// <summary>Base supuesta de un saldo y de dónde sale.</summary>
    public static (decimal Base, OrigenBase Origen) BaseSupuesta(decimal saldo, FacturaReferencia? ultima, decimal tipoIva)
    {
        if (ultima is { Total: not 0m } f && f.Base != 0m)
            return (Math.Round(saldo * f.Base / f.Total, 2, MidpointRounding.AwayFromZero), OrigenBase.UltimaFactura);
        return (Math.Round(saldo / (1m + tipoIva / 100m), 2, MidpointRounding.AwayFromZero), OrigenBase.TipoSupuesto);
    }

    public static PropuestaFacturasPendientes Calcular(int ejercicio, ConfiguracionFacturasPendientes cfg, IEnumerable<SubcuentaProveedor> subcuentas,
        IEnumerable<AjusteFacturaPendiente>? ajustes = null)
    {
        var porCuenta = (ajustes ?? []).GroupBy(a => a.Cuenta.Trim(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var filas = new List<FilaFacturaPendiente>();
        var cambiadas = new List<string>();

        foreach (var s in subcuentas.Where(s => EsCandidata(s.Cuenta, cfg)).OrderBy(s => s.Cuenta, StringComparer.Ordinal))
        {
            var saldo = Math.Round(s.Saldo, 2, MidpointRounding.AwayFromZero);
            if (saldo <= 0m) continue;
            if (!s.Cuadrada)
            {
                if (s.CuadradaConOtroSaldo) cambiadas.Add($"{s.Cuenta} {s.Nombre}".Trim());
                continue;
            }
            var (calculada, origen) = BaseSupuesta(saldo, s.UltimaFactura, cfg.TipoIva);
            var origenTexto = origen == OrigenBase.UltimaFactura
                ? $"Última factura{(string.IsNullOrWhiteSpace(s.UltimaFactura!.Numero) ? "" : $" {s.UltimaFactura.Numero.Trim()}")} del {s.UltimaFactura.Fecha:dd/MM/yyyy}: base {Euros(s.UltimaFactura.Base)} de {Euros(s.UltimaFactura.Total)}"
                : $"Sin facturas: IVA supuesto del {Porcentaje(cfg.TipoIva)}";
            string cuentaGasto;
            OrigenGasto origenGasto;
            int? centro;
            if (!string.IsNullOrWhiteSpace(s.CuentaGastoDefecto))
                (cuentaGasto, origenGasto, centro) = (s.CuentaGastoDefecto.Trim(), OrigenGasto.Proveedor, s.IdCentroDefecto ?? s.UltimaFactura?.IdCentroCoste);
            else if (!string.IsNullOrWhiteSpace(s.UltimaFactura?.CuentaGasto))
                (cuentaGasto, origenGasto, centro) = (s.UltimaFactura.CuentaGasto.Trim(), OrigenGasto.UltimaFactura, s.IdCentroDefecto ?? s.UltimaFactura.IdCentroCoste);
            else
                (cuentaGasto, origenGasto, centro) = (cfg.CuentaGasto, OrigenGasto.Ejercicio, null);
            porCuenta.TryGetValue(s.Cuenta.Trim(), out var ajuste);
            var manual = ajuste?.BaseManual is decimal m && m != calculada ? Math.Round(m, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
            filas.Add(new FilaFacturaPendiente(s.Cuenta.Trim(), s.Nombre.Trim(), saldo, origen, origenTexto, calculada, manual, ajuste?.Excluido != true, cuentaGasto, origenGasto, centro));
        }

        var fijos = new List<string>();
        if (cambiadas.Count > 0)
            fijos.Add($"Marcadas como cuadradas, pero su saldo ha cambiado desde entonces (no entran hasta que se vuelvan a cuadrar en el Mayor): {string.Join("; ", cambiadas.Take(5))}{(cambiadas.Count > 5 ? "…" : "")}.");
        return Componer(ejercicio, cfg, filas, fijos);
    }

    /// <summary>
    /// La misma propuesta con otros ajustes a mano (casillas y bases de la pantalla), sin volver a leer nada: recalcula los asientos y
    /// los avisos. Una base igual a la calculada deja de ser manual.
    /// </summary>
    public static PropuestaFacturasPendientes Aplicar(PropuestaFacturasPendientes p, IEnumerable<AjusteFacturaPendiente> ajustes)
    {
        var porCuenta = ajustes.GroupBy(a => a.Cuenta.Trim(), StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        var filas = p.Filas.Select(f =>
        {
            porCuenta.TryGetValue(f.Cuenta, out var a);
            var manual = a?.BaseManual is decimal m && Math.Round(m, 2, MidpointRounding.AwayFromZero) != f.BaseCalculada ? Math.Round(m, 2, MidpointRounding.AwayFromZero) : (decimal?)null;
            return f with { Incluido = a?.Excluido != true, BaseManual = manual };
        }).ToList();
        return Componer(p.Ejercicio, p.Configuracion, filas, p.Fijos);
    }

    /// <summary>La misma propuesta con más avisos fijos delante.</summary>
    public static PropuestaFacturasPendientes ConAvisos(PropuestaFacturasPendientes p, IReadOnlyList<string> avisos) =>
        avisos.Count == 0 ? p : Componer(p.Ejercicio, p.Configuracion, p.Filas, [.. avisos, .. p.Fijos]);

    private static PropuestaFacturasPendientes Componer(int ejercicio, ConfiguracionFacturasPendientes cfg, IReadOnlyList<FilaFacturaPendiente> filas, IReadOnlyList<string> fijos)
    {
        var provision = new List<LineaFacturaPendiente>();
        foreach (var f in filas.Where(f => f.Incluido && f.Base != 0m))
        {
            var concepto = Concepto(f.Nombre.Length > 0 ? f.Nombre : f.Cuenta);
            provision.Add(new(f.CuentaGasto, f.Base, 0m, concepto, f.IdCentroCoste));
            provision.Add(new(cfg.CuentaPendientes, 0m, f.Base, concepto));
        }
        var reversion = provision.Select(l => l with { Debe = l.Haber, Haber = l.Debe }).ToList();

        var avisos = new List<string>(fijos);
        if (filas.Count == 0)
            avisos.Add("Ningún proveedor ni acreedor cuadrado en el Mayor tiene saldo deudor a 31/12: no hay facturas pendientes de recibir que provisionar.");
        else if (provision.Count == 0)
            avisos.Add("Todos los proveedores de la lista están excluidos: al confirmar no se graba ningún asiento (y se borran los que hubiera).");
        foreach (var f in filas.Where(f => f.Incluido && f.Base > f.Saldo))
            avisos.Add($"{f.Cuenta} {f.Nombre}: la base ({Euros(f.Base)}) es mayor que el saldo ({Euros(f.Saldo)}).");

        return new PropuestaFacturasPendientes(ejercicio, FechaProvision(ejercicio), FechaReversion(ejercicio), cfg, filas, provision, reversion, avisos, fijos);
    }

    /// <summary>Texto para las notas de los asientos: proveedores, total y motivo.</summary>
    public static string Notas(PropuestaFacturasPendientes p, string? motivo)
    {
        var n = p.Incluidas.Count();
        var partes = new List<string> { $"{n} {(n == 1 ? "proveedor" : "proveedores")} pagados sin factura recibida; base supuesta {Euros(p.Total)}." };
        var excluidos = p.Filas.Count(f => !f.Incluido);
        if (excluidos > 0) partes.Add($"{excluidos} excluido{(excluidos == 1 ? "" : "s")} a mano.");
        var manuales = p.Incluidas.Count(f => f.BaseManual is not null);
        if (manuales > 0) partes.Add($"{manuales} con la base escrita a mano.");
        if (!string.IsNullOrWhiteSpace(motivo)) partes.Add($"Motivo: {motivo.Trim()}");
        var texto = string.Join(" ", partes);
        return texto.Length <= 255 ? texto : texto[..255];
    }
}
