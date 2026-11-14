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

    /// <summary>
    /// Asiento automático cuyas cuentas o importes se han corregido a mano (Asientos.CorregidoAMano, contabilidad/diario 2026-11-14);
    /// regenerar su lote sustituye el asiento y quita la marca.
    /// </summary>
    public bool CorregidoAMano { get; internal set; }
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
    /// (contabilidad/asientos 2026-11-14 23:30): misma fecha, mismos apuntes en el mismo orden con los mismos importes y lado, y la misma
    /// cuenta salvo un cambio de subcuenta del mismo grupo 4XX de terceros si el asiento no lleva la 4751. Pueden cambiar la descripción,
    /// las notas y el concepto, el documento, el cliente o proveedor, el vencimiento y el centro de coste de los apuntes (el centro solo
    /// cuenta para la prorrata, que se calcula a final de año).
    /// </summary>
    public static bool EsCambioSinEfectoFiscal(Asiento anterior, Asiento nuevo)
    {
        if (anterior.FechaContable.Date != nuevo.FechaContable.Date || anterior.Apuntes.Count != nuevo.Apuntes.Count) return false;
        var conRetenciones = anterior.Apuntes.Concat(nuevo.Apuntes).Any(a => a.CodigoCuenta.Trim().StartsWith(PrefijoRetenciones, StringComparison.Ordinal));
        foreach (var (v, n) in anterior.Apuntes.OrderBy(a => a.Orden).Zip(nuevo.Apuntes))
        {
            if (v.Debe.Valor != n.Debe.Valor || v.Haber.Valor != n.Haber.Valor) return false;
            var cuentaV = v.CodigoCuenta.Trim();
            var cuentaN = n.CodigoCuenta.Trim();
            if (string.Equals(cuentaV, cuentaN, StringComparison.Ordinal)) continue;
            if (conRetenciones || !EsCambioDeSubcuentaDeTercero(cuentaV, cuentaN)) return false;
        }
        return true;
    }

    /// <summary>Prefijo de las cuentas de banco: en un asiento automático sus apuntes no se cambian a mano (contabilidad/diario 2026-11-14 21:50).</summary>
    public const string PrefijoBanco = "572";

    /// <summary>
    /// Líneas de IVA y retención de una factura, por prefijo: en un asiento con libro de IVA no se cambian a mano, se corrigen en la
    /// factura para que el 303 siga cuadrando con la contabilidad (contabilidad/diario 2026-11-14 21:50).
    /// </summary>
    public static readonly IReadOnlyList<string> CuentasIvaFactura = ["472", "477", "4751"];

    public static bool EsCuentaBanco(string? codigo) => (codigo ?? "").Trim().StartsWith(PrefijoBanco, StringComparison.Ordinal);

    public static bool EsCuentaIvaFactura(string? codigo) =>
        !string.IsNullOrWhiteSpace(codigo) && CuentasIvaFactura.Any(p => codigo.Trim().StartsWith(p, StringComparison.Ordinal));

    /// <summary>
    /// Por qué no se puede cambiar a mano la cuenta o el importe de un apunte de un asiento automático, o null si se puede: el apunte de
    /// banco (572) nunca; con libro de IVA, tampoco las líneas de IVA y retención. Lo usan el motor y las pantallas (columna «Bloqueos»).
    /// </summary>
    public static string? BloqueoCuentaAutomatico(string? cuenta, bool conLibroIva) =>
        EsCuentaBanco(cuenta) ? "Es el apunte de banco (572) de un asiento automático: su cuenta y su importe no se cambian a mano."
        : conLibroIva && EsCuentaIvaFactura(cuenta) ? "Es una línea de IVA o de retención de una factura: se corrige en la factura, para que el libro de IVA siga cuadrando."
        : null;

    /// <summary>
    /// Reglas de la corrección a mano de un asiento automático (contabilidad/diario 2026-11-14 21:50): los apuntes de la 572 quedan como
    /// estaban (misma cuenta e importe, ninguno nuevo ni quitado) y, si el asiento lleva libro de IVA (factura de venta o de compra),
    /// tampoco cambian la fecha, el número de apuntes, los importes ni las líneas de IVA y retención: solo las subcuentas de gasto,
    /// ingreso o tercero, y nunca a una cuenta de IVA. No toca la BD.
    /// </summary>
    public static Resultado ValidarCorreccionAutomatico(Asiento anterior, Asiento nuevo, bool conLibroIva)
    {
        var registro = anterior.ToString();
        static IEnumerable<string> Huella(Asiento a) =>
            a.Apuntes.Where(x => EsCuentaBanco(x.CodigoCuenta)).Select(x => $"{x.CodigoCuenta.Trim()}|{x.Debe.Valor}|{x.Haber.Valor}").OrderBy(x => x, StringComparer.Ordinal);

        if (!Huella(anterior).SequenceEqual(Huella(nuevo)))
            return Resultado.Fallo(new Error("Asientos.BancoAutomatico", "Los apuntes de banco (572) de un asiento automático no se cambian a mano.", registro,
                "Has cambiado, añadido o quitado un apunte de la 572; ese apunte lo fija el movimiento del banco.",
                "Deja los apuntes de la 572 como estaban y corrige solo las demás cuentas o importes."));
        if (!conLibroIva) return Resultado.Correcto();

        if (anterior.FechaContable.Date != nuevo.FechaContable.Date)
            return Resultado.Fallo(new Error("Asientos.FacturaConLibroIva", "La fecha del asiento de una factura no se cambia a mano.", registro,
                "El asiento va con el libro de IVA de su factura, y la fecha decide el periodo del IVA.", "Corrige la fecha en la factura y vuelve a contabilizarla."));
        var viejos = anterior.Apuntes.OrderBy(a => a.Orden).ToList();
        if (viejos.Count != nuevo.Apuntes.Count)
            return Resultado.Fallo(new Error("Asientos.FacturaConLibroIva", "En el asiento de una factura no se añaden ni se quitan apuntes a mano.", registro,
                "El asiento va con el libro de IVA de su factura.", "Cambia solo las subcuentas de gasto, ingreso o tercero; el resto se corrige en la factura."));
        foreach (var (v, n) in viejos.Zip(nuevo.Apuntes))
        {
            if (v.Debe.Valor != n.Debe.Valor || v.Haber.Valor != n.Haber.Valor)
                return Resultado.Fallo(new Error("Asientos.FacturaConLibroIva", $"El importe del apunte {v.Orden} no se cambia a mano: es de una factura.", registro,
                    "Los importes van con el libro de IVA de la factura, y el modelo 303 debe cuadrar con la contabilidad.", "Corrige el importe en la factura y vuelve a contabilizarla."));
            var cuentaV = v.CodigoCuenta.Trim();
            var cuentaN = n.CodigoCuenta.Trim();
            if (string.Equals(cuentaV, cuentaN, StringComparison.Ordinal)) continue;
            if (EsCuentaIvaFactura(cuentaV) || EsCuentaIvaFactura(cuentaN))
                return Resultado.Fallo(new Error("Asientos.FacturaConLibroIva", $"La cuenta del apunte {v.Orden} no se cambia a mano: es una línea de IVA o de retención de una factura.", registro,
                    $"Pasaría de {cuentaV} a {cuentaN}; las líneas de IVA y retención van con el libro de IVA.", "Corrige el IVA o la retención en la factura y vuelve a contabilizarla."));
        }
        return Resultado.Correcto();
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
