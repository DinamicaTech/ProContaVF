using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Asientos;

/// <summary>Estado del asiento (columna EstadoAsiento, valores del motor anterior).</summary>
public enum EstadoAsiento : byte
{
    Borrador = 0,
    Contabilizado = 1,
    Anulado = 2
}

/// <summary>Tipo del asiento (columna TipoAsiento, valores del motor anterior).</summary>
public enum TipoAsiento : byte
{
    Ordinario = 0,
    Apertura = 1,
    Regularizacion = 2,
    Cierre = 3,
    Rectificacion = 4
}

/// <summary>Tipo de apunte: ordinario o ajuste de redondeo añadido por la política de cuadre.</summary>
public enum TipoApunte : byte
{
    Ordinario = 0,
    AjusteRedondeo = 1
}

/// <summary>
/// Identidad del lote que es dueño de un asiento automático: el motor del área (ventas, compras,
/// nóminas, N43…), el proceso y la clave de la ejecución (p. ej. la factura). Solo ese lote puede
/// regenerar o borrar sus asientos (RF-ASI-03).
/// </summary>
public sealed record IdentidadLote(string MotorPropietario, string CodigoProceso, string ClaveEjecucion)
{
    public override string ToString() => $"{MotorPropietario}/{CodigoProceso}/{ClaveEjecucion}";
}

/// <summary>Apunte de un asiento (tabla Apuntes). Cada apunte va al Debe o al Haber, nunca a los dos.</summary>
public sealed class Apunte
{
    public int IdApunte { get; internal set; }
    public int Orden { get; internal set; }
    public string CodigoCuenta { get; set; } = "";
    public Importe Debe { get; set; }
    public Importe Haber { get; set; }
    public string? Descripcion { get; set; }
    public string? NumeroDocumento { get; set; }
    public int? IdCliente { get; set; }
    public int? IdProveedor { get; set; }
    public DateTime? FechaVencimiento { get; set; }
    public int? IdCentroCoste { get; set; }
    public TipoApunte Tipo { get; internal set; } = TipoApunte.Ordinario;
    public bool GeneradoAutomaticamente { get; set; }
    public string? MotivoGeneracion { get; set; }
    /// <summary>
    /// Punteado en el Mayor (contabilidad/mayor 2026-11-11 20:05). Solo lo cambia <see cref="IMotorAsientos.PuntearApuntesAsync"/>;
    /// al guardar o regenerar el asiento se conserva en el apunte que sigue en el mismo orden con la misma cuenta e importe.
    /// </summary>
    public bool Punteado { get; set; }

    public static Apunte AlDebe(string cuenta, decimal importe, string? descripcion = null) =>
        new() { CodigoCuenta = cuenta, Debe = Importe.Redondear(importe), Descripcion = descripcion };

    public static Apunte AlHaber(string cuenta, decimal importe, string? descripcion = null) =>
        new() { CodigoCuenta = cuenta, Haber = Importe.Redondear(importe), Descripcion = descripcion };
}

/// <summary>
/// Agregado Asiento: cabecera y todos sus apuntes, que se validan y guardan juntos (RF-ASI-01).
/// </summary>
public sealed class Asiento
{
    public Asiento(ContextoSociedadEjercicio contexto, DateTime fechaContable, string descripcion)
    {
        Contexto = contexto;
        FechaContable = fechaContable.Date;
        Descripcion = descripcion;
    }

    public int IdAsiento { get; internal set; }
    public ContextoSociedadEjercicio Contexto { get; }
    public DateTime FechaContable { get; set; }
    public string Descripcion { get; set; }
    public TipoAsiento Tipo { get; set; } = TipoAsiento.Ordinario;
    public EstadoAsiento Estado { get; internal set; } = EstadoAsiento.Contabilizado;
    public string? Notas { get; set; }
    public List<Apunte> Apuntes { get; } = [];

    /// <summary>Plantilla de asiento con la que se creó (Asientos.IdPlantillaAsiento, contabilidad/plantillas-asiento); null si no se usó ninguna.</summary>
    public int? IdPlantilla { get; set; }

    /// <summary>Lote propietario si es automático; null si es manual.</summary>
    public IdentidadLote? Lote { get; internal set; }

    public bool EsAutomatico => Lote is not null;
    public int Version { get; internal set; }
    public bool EsNuevo => IdAsiento == 0;

    public Importe TotalDebe => Importe.Sumar(Apuntes.Select(a => a.Debe));
    public Importe TotalHaber => Importe.Sumar(Apuntes.Select(a => a.Haber));

    public override string ToString() => EsNuevo ? $"Asiento nuevo «{Descripcion}»" : $"Asiento {IdAsiento}";
}

/// <summary>
/// Política de cuadre (RF-ASI-02): por defecto Debe y Haber deben coincidir al céntimo. Solo con una
/// tolerancia y unas cuentas configuradas expresamente se añade un apunte visible de ajuste de redondeo.
/// </summary>
public sealed record PoliticaCuadre(decimal Tolerancia = 0m, string? CuentaGastosRedondeo = null, string? CuentaIngresosRedondeo = null)
{
    public static readonly PoliticaCuadre Estricta = new();

    public Resultado Validar()
    {
        if (Tolerancia is < 0m or > 1m)
            return Resultado.Fallo(new Error("Asientos.ToleranciaNoValida", "La tolerancia de cuadre debe estar entre 0 y 1 euro.", null,
                $"Es {Tolerancia}.", "Corrige la configuración de cuadre."));
        if (Tolerancia > 0m && (string.IsNullOrWhiteSpace(CuentaGastosRedondeo) || string.IsNullOrWhiteSpace(CuentaIngresosRedondeo)))
            return Resultado.Fallo(new Error("Asientos.SinCuentasRedondeo", "Hay tolerancia de cuadre pero faltan las cuentas de redondeo.", null,
                "Un ajuste de redondeo necesita una cuenta de gastos y otra de ingresos.", "Configura las dos cuentas o deja la tolerancia a 0."));
        return Resultado.Correcto();
    }
}

/// <summary>Reglas del agregado que no necesitan la BD.</summary>
public static class ReglasAsiento
{
    public const int LongitudDescripcion = 255;

    /// <summary>
    /// Cuentas fiscales del trimestre (contabilidad/asientos 2026-11-13 09:45), por prefijo de código: las de los modelos 303 (472, 477,
    /// 4700, 4750), 111 (4751, 640, 623) y 115 (4751, 621). Un asiento con algún apunte en ellas no se crea, cambia ni borra en un
    /// trimestre cerrado; el resto (bancos, clientes, proveedores, amortizaciones…) sí, porque no altera las declaraciones presentadas.
    /// </summary>
    public static readonly IReadOnlyList<string> CuentasFiscalesTrimestre = ["472", "477", "4700", "4750", "4751", "640", "623", "621"];

    /// <summary>Si la cuenta es fiscal del trimestre (<see cref="CuentasFiscalesTrimestre"/>).</summary>
    public static bool EsCuentaFiscalTrimestre(string? codigo) =>
        !string.IsNullOrWhiteSpace(codigo) && CuentasFiscalesTrimestre.Any(p => codigo.Trim().StartsWith(p, StringComparison.Ordinal));

    /// <summary>La primera cuenta fiscal del trimestre de la lista, o null si no hay ninguna: con ella, el asiento afecta a un trimestre fiscal.</summary>
    public static string? AfectaTrimestreFiscal(IEnumerable<string?> cuentas) => cuentas.FirstOrDefault(EsCuentaFiscalTrimestre)?.Trim();

    /// <summary>Prefijo de las retenciones practicadas: un asiento que la lleva no admite ni el cambio de subcuenta 4XX en un trimestre cerrado.</summary>
    public const string PrefijoRetenciones = "4751";

    /// <summary>
    /// Si se puede pasar un apunte de la subcuenta <paramref name="anterior"/> a <paramref name="nueva"/> en un trimestre cerrado
    /// (contabilidad/asientos 2026-11-13 10:50): las dos del mismo grupo 4XX de terceros (los tres primeros dígitos iguales, p. ej. 430 → 430)
    /// y ninguna fiscal. Que el asiento no lleve la 4751 lo comprueba quien llama.
    /// </summary>
    public static bool EsCambioDeSubcuentaDeTercero(string? anterior, string? nueva)
    {
        var a = anterior?.Trim() ?? "";
        var n = nueva?.Trim() ?? "";
        return a.Length >= 3 && n.Length >= 3 && a[0] == '4' && string.Equals(a[..3], n[..3], StringComparison.Ordinal)
            && !EsCuentaFiscalTrimestre(a) && !EsCuentaFiscalTrimestre(n);
    }

    /// <summary>
    /// Si el cambio de <paramref name="anterior"/> a <paramref name="nuevo"/> no tiene efecto fiscal y se admite en un trimestre cerrado
    /// (contabilidad/asientos 2026-11-13 10:50): misma fecha, mismos apuntes en el mismo orden con los mismos importes, lado y centro de
    /// coste, y la misma cuenta salvo un cambio de subcuenta del mismo grupo 4XX de terceros si el asiento no lleva la 4751. Pueden cambiar
    /// la descripción, las notas y el concepto, el documento, el cliente o proveedor y el vencimiento de los apuntes.
    /// </summary>
    public static bool EsCambioSinEfectoFiscal(Asiento anterior, Asiento nuevo)
    {
        if (anterior.FechaContable.Date != nuevo.FechaContable.Date || anterior.Apuntes.Count != nuevo.Apuntes.Count) return false;
        var conRetenciones = anterior.Apuntes.Concat(nuevo.Apuntes).Any(a => a.CodigoCuenta.Trim().StartsWith(PrefijoRetenciones, StringComparison.Ordinal));
        foreach (var (v, n) in anterior.Apuntes.OrderBy(a => a.Orden).Zip(nuevo.Apuntes))
        {
            if (v.Debe.Valor != n.Debe.Valor || v.Haber.Valor != n.Haber.Valor || v.IdCentroCoste != n.IdCentroCoste) return false;
            var cuentaV = v.CodigoCuenta.Trim();
            var cuentaN = n.CodigoCuenta.Trim();
            if (string.Equals(cuentaV, cuentaN, StringComparison.Ordinal)) continue;
            if (conRetenciones || !EsCambioDeSubcuentaDeTercero(cuentaV, cuentaN)) return false;
        }
        return true;
    }

    /// <summary>
    /// Valida y, si la política lo permite, cuadra el asiento con un apunte de ajuste visible.
    /// Numera los apuntes en orden. No toca la BD.
    /// </summary>
    public static Resultado ValidarYCuadrar(Asiento asiento, PoliticaCuadre politica)
    {
        var registro = asiento.ToString();
        var errores = new List<Error>();
        var politicaValida = politica.Validar();
        if (!politicaValida.EsCorrecto) return politicaValida;

        // Un ajuste de redondeo anterior se recalcula: se quita antes de validar.
        asiento.Apuntes.RemoveAll(a => a.Tipo == TipoApunte.AjusteRedondeo);

        if (string.IsNullOrWhiteSpace(asiento.Descripcion) || asiento.Descripcion.Trim().Length > LongitudDescripcion)
            errores.Add(new Error("Asientos.SinDescripcion", "El asiento necesita una descripción de hasta 255 caracteres.", registro,
                "La descripción falta o es demasiado larga.", "Escribe una descripción breve del asiento."));
        if (asiento.FechaContable.Year != asiento.Contexto.Ejercicio)
            errores.Add(new Error("Asientos.FechaFueraDeEjercicio", $"La fecha {asiento.FechaContable:dd/MM/yyyy} no es del ejercicio {asiento.Contexto.Ejercicio}.",
                registro, "Un asiento se contabiliza en el ejercicio de su fecha.", "Corrige la fecha o cambia al ejercicio que corresponde."));
        if (asiento.Apuntes.Count < 2)
            errores.Add(new Error("Asientos.PocosApuntes", "Un asiento necesita al menos dos apuntes.", registro,
                "Tiene " + asiento.Apuntes.Count + ".", "Añade la contrapartida."));

        for (var i = 0; i < asiento.Apuntes.Count; i++)
        {
            var a = asiento.Apuntes[i];
            a.Orden = i + 1;
            if (string.IsNullOrWhiteSpace(a.CodigoCuenta))
                errores.Add(new Error("Asientos.ApunteSinCuenta", $"El apunte {a.Orden} no tiene cuenta.", registro, "La cuenta es obligatoria.", "Indica la cuenta del apunte."));
            if (a.Debe.EsNegativo || a.Haber.EsNegativo)
                errores.Add(new Error("Asientos.ImporteNegativo", $"El apunte {a.Orden} tiene un importe negativo.", registro,
                    "Los importes van siempre en positivo, al Debe o al Haber.", "Pásalo en positivo al lado contrario."));
            if (a.Debe.EsCero == a.Haber.EsCero)
                errores.Add(new Error("Asientos.ApunteSinLado", $"El apunte {a.Orden} debe tener importe al Debe o al Haber, y solo en uno.", registro,
                    a.Debe.EsCero ? "No tiene importe." : "Tiene importe en los dos lados.", "Deja el importe en un solo lado."));
        }
        if (errores.Count > 0) return Resultado.Fallo(errores);

        var diferencia = asiento.TotalDebe - asiento.TotalHaber;
        if (diferencia.EsCero) return Resultado.Correcto();
        if (politica.Tolerancia > 0m && diferencia.Absoluto().Valor <= politica.Tolerancia)
        {
            var ajuste = diferencia.EsNegativo
                ? new Apunte { CodigoCuenta = politica.CuentaGastosRedondeo!.Trim(), Debe = diferencia.Absoluto() }
                : new Apunte { CodigoCuenta = politica.CuentaIngresosRedondeo!.Trim(), Haber = diferencia.Absoluto() };
            ajuste.Tipo = TipoApunte.AjusteRedondeo;
            ajuste.GeneradoAutomaticamente = true;
            ajuste.MotivoGeneracion = $"Ajuste de redondeo de {diferencia.Absoluto()} € (tolerancia {politica.Tolerancia:0.00} €).";
            ajuste.Descripcion = "Ajuste de redondeo";
            ajuste.Orden = asiento.Apuntes.Count + 1;
            asiento.Apuntes.Add(ajuste);
            return Resultado.Correcto();
        }
        return Resultado.Fallo(new Error("Asientos.Descuadrado", $"El asiento no cuadra: Debe {asiento.TotalDebe} y Haber {asiento.TotalHaber}.",
            registro, $"Hay una diferencia de {diferencia.Absoluto()} €.", "Revisa los importes de los apuntes hasta que Debe y Haber coincidan."));
    }
}
