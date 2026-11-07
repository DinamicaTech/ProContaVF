using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;

namespace ProConta.Compras.Facturas;

/// <summary>Cuentas ya resueltas en el ejercicio para el asiento de una factura de compra.</summary>
/// <param name="Proveedor">Cuenta del proveedor o acreedor (Haber, por el total).</param>
/// <param name="Gasto">Cuenta de gasto de la factura (Debe, por las bases).</param>
/// <param name="IvaSoportado">Cuenta de IVA soportado por porcentaje (Debe), en las nacionales sin inversión del sujeto pasivo.</param>
/// <param name="Retencion">Cuenta de retención de servicios o de alquileres (Haber), si la factura retiene.</param>
/// <param name="AutoSoportado">IVA autorrepercutido, lado soportado (Debe): inversión del sujeto pasivo o intracomunitario.</param>
/// <param name="AutoRepercutido">IVA autorrepercutido, lado repercutido (Haber).</param>
/// <param name="Importacion">IVA de importación (4724001 en el prototipo).</param>
/// <param name="Suplido">Cuenta de suplidos (Debe).</param>
/// <param name="RedondeoGastos">Diferencias de redondeo cuando falta Debe.</param>
/// <param name="RedondeoIngresos">Diferencias de redondeo cuando sobra Debe.</param>
/// <param name="TipoGeneral">Tipo general del ejercicio, el de la autorrepercusión y la importación.</param>
/// <param name="NombreProveedor">Nombre del proveedor en las descripciones: el comercial si lo tiene, si no la razón social.</param>
/// <param name="NombreGasto">Nombre de la cuenta de gasto, para la descripción de su apunte.</param>
public sealed record CuentasCompra(
    string Proveedor, string Gasto, int? IdCentroCoste, IReadOnlyDictionary<decimal, string> IvaSoportado,
    string? Retencion, string? AutoSoportado, string? AutoRepercutido, string? Importacion, string? Suplido,
    string RedondeoGastos, string RedondeoIngresos, decimal TipoGeneral, string? NombreProveedor = null, string? NombreGasto = null);

/// <summary>Un tramo del libro de IVA soportado (DesglosesIVASoportado).</summary>
public sealed record TramoLibroCompra(decimal TipoIva, decimal Base, decimal CuotaSoportada, decimal CuotaAutorrepercutida, string? CuentaSoportado, string? CuentaRepercutido)
{
    public decimal CuotaDeducible => CuotaSoportada + CuotaAutorrepercutida;
    public TramoLibroCompra Negado() => this with { Base = -Base, CuotaSoportada = -CuotaSoportada, CuotaAutorrepercutida = -CuotaAutorrepercutida };
}

/// <summary>Registro del libro de IVA soportado de un asiento (IVASoportado y sus desgloses), en euros.</summary>
/// <param name="BienInversion">Compra de un bien de inversión (cuenta del grupo 2): en el 303 va en sus casillas y se regulariza.</param>
public sealed record LibroCompra(decimal Total, IReadOnlyList<TramoLibroCompra> Tramos, bool BienInversion = false)
{
    public decimal Base => Tramos.Sum(t => t.Base);
    public decimal CuotaSoportada => Tramos.Sum(t => t.CuotaSoportada);
    public decimal CuotaAutorrepercutida => Tramos.Sum(t => t.CuotaAutorrepercutida);
    public decimal CuotaDeducible => Tramos.Sum(t => t.CuotaDeducible);
    public LibroCompra Negado() => new(-Total, [.. Tramos.Select(t => t.Negado())], BienInversion);
    /// <summary>Sin registro en el libro (un asiento del ERP sin IVA soportado grabado).</summary>
    public bool Vacio => Total == 0m && Tramos.Count == 0;
}

/// <summary>Asiento propuesto con su registro del libro de IVA y el descuadre que hubo que ajustar.</summary>
public sealed record PropuestaAsientoCompra(Asiento Asiento, LibroCompra Libro, decimal Descuadre);

/// <summary>
/// Asiento de una factura de compra (compras/factura-compra/contabilizacion), sin BD:
/// Haber proveedor por el total y retención; Debe gasto por cada base (con su centro de coste), IVA soportado por tipo y
/// suplido. Con inversión del sujeto pasivo o intracomunitaria, IVA autorrepercutido al tipo general en soportado (Debe)
/// y repercutido (Haber). En la importación, el gasto es el importe / (1 + tipo general) y el resto, IVA de importación.
/// Un descuadre de hasta 0,03 € se ajusta con un apunte a la cuenta de redondeo; uno mayor, solo con un motivo escrito.
/// Los importes negativos (abonos) van en positivo al lado contrario.
/// </summary>
public static class AsientoCompra
{
    public const string MotorPropietario = "compras/factura-compra";
    public const string CodigoProceso = "COMPRA";
    public const decimal ToleranciaRedondeo = 0.03m;

    /// <summary>
    /// Lote de la factura en cada exportación: la primera es «IdRegistro»; cada contra-asiento de un trimestre cerrado
    /// abre otro, «IdRegistro.n», para que regenerar el lote nuevo no toque el asiento del trimestre cerrado.
    /// </summary>
    public static IdentidadLote Lote(int idRegistro, int generacion) => new(MotorPropietario, CodigoProceso,
        generacion <= 0 ? idRegistro.ToString(CultureInfo.InvariantCulture) : $"{idRegistro.ToString(CultureInfo.InvariantCulture)}.{generacion.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Base + cuota − retención + suplido − total: lo que el asiento tendría de más al Debe.</summary>
    public static decimal Descuadre(FacturaCompra f) => f.Operacion == OperacionCompra.Importacion ? 0m
        : Bases(f).Sum(b => b.Base) + (f.InversionSujetoPasivo ? 0m : Bases(f).Sum(b => b.Cuota)) - (f.ImporteRetencion ?? 0m) + (f.Suplido ?? 0m) - (f.ImporteTotal ?? 0m);

    /// <summary>Bases por tipo: los desgloses o, sin desgloses, la base y la cuota de la factura (tipo calculado).</summary>
    internal static IReadOnlyList<(decimal Tipo, decimal Base, decimal Cuota)> Bases(FacturaCompra f)
    {
        if (f.Desgloses.Count > 0)
            return [.. f.Desgloses.OrderBy(d => d.Orden).Select(d => (d.TipoIva, d.BaseImponible, d.CuotaIva))];
        var b = f.BaseImponible ?? 0m;
        var c = f.CuotaIva ?? 0m;
        return [(b == 0m || c == 0m ? 0m : Math.Round(c * 100m / b, 0), b, c)];
    }

    /// <summary>
    /// Construye el asiento. <paramref name="motivoAjuste"/> permite cuadrar un descuadre mayor de 0,03 € con un apunte
    /// a la cuenta de redondeo (compras/factura-compra/contabilizacion 2026-10-29 21:05); sin motivo, devuelve null.
    /// </summary>
    public static PropuestaAsientoCompra? Construir(FacturaCompra f, ContextoSociedadEjercicio contexto, CuentasCompra cuentas, DateTime fecha, string? motivoAjuste = null)
    {
        var numero = string.IsNullOrWhiteSpace(f.NumeroFactura) ? f.Rotulo : f.NumeroFactura.Trim();
        var descripcion = Recortar(string.IsNullOrWhiteSpace(f.Concepto) ? $"Factura de compra {numero}" : f.Concepto.Trim());
        var asiento = new Asiento(contexto, fecha, descripcion);
        var motivo = $"Contabilización de la factura de compra {numero}";
        // Descripciones legibles desde cualquier mayor (compras/factura-compra/contabilizacion 2026-11-07):
        // «Fra. F-2026/125 · Instalaciones García» en el proveedor y «<qué> · Instalaciones García · Fra. F-2026/125» en el resto.
        var nombre = string.IsNullOrWhiteSpace(cuentas.NombreProveedor) ? null : cuentas.NombreProveedor.Trim();
        var abreviado = string.IsNullOrWhiteSpace(f.ConceptoAbreviado) ? null : f.ConceptoAbreviado.Trim();
        var fra = $"Fra. {numero}";
        string Linea(string que, bool conConcepto = true) => Componer([que, nombre, fra], conConcepto ? abreviado : null);
        var varios = Bases(f).Select(b => b.Tipo).Distinct().Count() > 1;
        string Gasto(decimal tipo) => Componer([$"{abreviado ?? (string.IsNullOrWhiteSpace(cuentas.NombreGasto) ? "Gasto" : cuentas.NombreGasto.Trim())}{(varios ? $" {Pct(tipo)}" : "")}", nombre, fra], null);

        void Anadir(string? cuenta, decimal valor, bool alDebe, string texto, bool conProveedor = false, int? centro = null, string? motivoApunte = null)
        {
            var importe = Importe.Redondear(valor);
            if (importe.EsCero) return;
            var debe = importe.EsNegativo ? !alDebe : alDebe;
            var absoluto = importe.Absoluto();
            asiento.Apuntes.Add(new Apunte
            {
                CodigoCuenta = (cuenta ?? "").Trim(),
                Debe = debe ? absoluto : Importe.Cero,
                Haber = debe ? Importe.Cero : absoluto,
                Descripcion = Recortar(texto),
                NumeroDocumento = numero,
                IdProveedor = conProveedor ? f.IdProveedor : null,
                IdCentroCoste = centro,
                GeneradoAutomaticamente = true,
                MotivoGeneracion = motivoApunte ?? motivo
            });
        }

        var tramos = new List<TramoLibroCompra>();
        var total = f.ImporteTotal ?? 0m;
        Anadir(cuentas.Proveedor, total, false, Componer([fra, nombre], abreviado), conProveedor: true);
        if ((f.ImporteRetencion ?? 0m) != 0m)
            Anadir(cuentas.Retencion, f.ImporteRetencion!.Value, false, Linea($"Retención {Pct(f.PorcentajeRetencion ?? 0m)}"));

        if (f.Operacion == OperacionCompra.Importacion)
        {
            // Siempre al tipo general, lleve o no IVA desglosado (2026-10-31 07:46); sin 477.
            var sujeto = total + (f.ImporteRetencion ?? 0m) - (f.Suplido ?? 0m);
            var gasto = Importe.RedondearValor(sujeto / (1m + cuentas.TipoGeneral / 100m));
            Anadir(cuentas.Gasto, gasto, true, Componer([abreviado ?? (string.IsNullOrWhiteSpace(cuentas.NombreGasto) ? "Gasto" : cuentas.NombreGasto.Trim()), nombre, fra], null), centro: cuentas.IdCentroCoste);
            Anadir(cuentas.Importacion, sujeto - gasto, true, Linea($"IVA importación {Pct(cuentas.TipoGeneral)}"));
            tramos.Add(new TramoLibroCompra(cuentas.TipoGeneral, gasto, sujeto - gasto, 0m, cuentas.Importacion, null));
        }
        else
        {
            var autorrepercute = f.InversionSujetoPasivo || f.Operacion == OperacionCompra.Intracomunitaria;
            foreach (var (tipo, b, c) in Bases(f))
            {
                Anadir(cuentas.Gasto, b, true, Gasto(tipo), centro: cuentas.IdCentroCoste);
                if (autorrepercute) continue;
                Anadir(cuentas.IvaSoportado.TryGetValue(tipo, out var cuentaIva) ? cuentaIva : "", c, true, Linea($"IVA soportado {Pct(tipo)}"));
                tramos.Add(new TramoLibroCompra(tipo, b, c, 0m, cuentaIva, null));
            }
            if (autorrepercute)
            {
                var b = Bases(f).Sum(x => x.Base);
                var cuota = Importe.RedondearValor(b * cuentas.TipoGeneral / 100m);
                var texto = f.InversionSujetoPasivo ? "ISP" : "intracomunitario";
                Anadir(cuentas.AutoSoportado, cuota, true, Linea($"IVA soportado {Pct(cuentas.TipoGeneral)} ({texto})"));
                Anadir(cuentas.AutoRepercutido, cuota, false, Linea($"IVA autorrepercutido {Pct(cuentas.TipoGeneral)} ({texto})"));
                tramos.Add(new TramoLibroCompra(cuentas.TipoGeneral, b, 0m, cuota, cuentas.AutoSoportado, cuentas.AutoRepercutido));
            }
        }
        if ((f.Suplido ?? 0m) != 0m)
            Anadir(cuentas.Suplido, f.Suplido!.Value, true, Linea("Suplidos"));

        var descuadre = asiento.TotalDebe.Valor - asiento.TotalHaber.Valor;
        if (descuadre != 0m)
        {
            var manual = Math.Abs(descuadre) > ToleranciaRedondeo;
            if (manual && string.IsNullOrWhiteSpace(motivoAjuste)) return null;
            var texto = Linea(manual ? $"Ajuste manual: {motivoAjuste!.Trim()}" : "Ajuste de redondeo", conConcepto: false);
            var explicacion = manual
                ? $"Ajuste manual de {Math.Abs(descuadre):0.00} € con motivo: {motivoAjuste!.Trim()}"
                : $"Ajuste de redondeo de {Math.Abs(descuadre):0.00} € (hasta {ToleranciaRedondeo:0.00} € se ajusta solo).";
            if (descuadre > 0m) Anadir(cuentas.RedondeoIngresos, descuadre, false, texto, motivoApunte: explicacion);
            else Anadir(cuentas.RedondeoGastos, -descuadre, true, texto, motivoApunte: explicacion);
        }
        return new PropuestaAsientoCompra(asiento, new LibroCompra(total, tramos, FacturaCompra.EsBienInversion(f.CuentaGasto)), descuadre);
    }

    /// <summary>Contra-asiento de otro: los mismos apuntes con Debe y Haber cambiados, con la fecha que se le pase (la del original o el día 1 del trimestre activo).</summary>
    public static Asiento Contra(Asiento original, ContextoSociedadEjercicio contexto, DateTime fecha, string rotulo)
    {
        var contra = new Asiento(contexto, fecha, Recortar($"Anulación de {original.Descripcion}")) { Tipo = TipoAsiento.Rectificacion };
        foreach (var a in original.Apuntes)
            contra.Apuntes.Add(new Apunte
            {
                CodigoCuenta = a.CodigoCuenta, Debe = a.Haber, Haber = a.Debe, Descripcion = a.Descripcion, NumeroDocumento = a.NumeroDocumento,
                IdProveedor = a.IdProveedor, IdCentroCoste = a.IdCentroCoste, GeneradoAutomaticamente = true,
                MotivoGeneracion = Recortar($"Contra-asiento del asiento {original.IdAsiento} de la {rotulo.ToLowerInvariant()}")
            });
        return contra;
    }

    /// <summary>Explica un descuadre: cuánto falta y qué cuotas no corresponden a su base.</summary>
    public static string ExplicarDescuadre(FacturaCompra f)
    {
        var d = Descuadre(f);
        var partes = new List<string>
        {
            d > 0m ? $"Las bases y cuotas suman {d:0.00} € más que el total." : $"Faltan {-d:0.00} € en las bases y cuotas para llegar al total."
        };
        if (!f.InversionSujetoPasivo && f.Operacion == OperacionCompra.Nacional)
            foreach (var (tipo, b, c) in Bases(f))
            {
                var esperada = Importe.RedondearValor(b * tipo / 100m);
                if (Math.Abs(esperada - c) > 0.01m)
                    partes.Add($"La cuota del {tipo:0.##} % es {c:0.00} € y a su base de {b:0.00} € le corresponden {esperada:0.00} €.");
            }
        return string.Join(" ", partes);
    }

    private static string Pct(decimal tipo) => $"{tipo:0.##}%";

    /// <summary>
    /// Une las partes con « · » y el concepto abreviado al final. Si no cabe en la descripción, se recorta primero el
    /// concepto y luego el resto por el final.
    /// </summary>
    internal static string Componer(IEnumerable<string?> partes, string? concepto)
    {
        var fijo = string.Join(" · ", partes.Where(p => !string.IsNullOrWhiteSpace(p)));
        if (string.IsNullOrWhiteSpace(concepto)) return Recortar(fijo);
        var completo = $"{fijo} · {concepto.Trim()}";
        if (completo.Length <= ReglasAsiento.LongitudDescripcion) return completo;
        var libre = ReglasAsiento.LongitudDescripcion - fijo.Length - 3;
        return libre >= 4 ? $"{fijo} · {concepto.Trim()[..libre].TrimEnd()}" : Recortar(fijo);
    }

    private static string Recortar(string texto) => texto.Length <= ReglasAsiento.LongitudDescripcion ? texto : texto[..ReglasAsiento.LongitudDescripcion];
}
