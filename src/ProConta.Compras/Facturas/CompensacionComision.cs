using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Plantilla de sistema «Compensación de comisión de plataforma» (facturacion/reservas/compensacion-comision): la misma que da de alta la
/// migración 20261116_1700_COMPENSACION_COMISION en PlantillasAsiento. Recibe un registro con CuentaProveedor (la 410 de la factura de
/// comisión), CuentaCliente (la 430 del mismo tercero), Importe (lo que la factura llevó a la 410; negativo en un abono) y Concepto, y genera
/// un asiento: la 410 al Debe y la 430 al Haber. Se usa si la BD aún no tiene la plantilla (o la han borrado).
/// </summary>
public static class PlantillaCompensacionComision
{
    public const string ClaveProceso = "comision-plataforma";
    public const string Nombre = "Compensación de comisión de plataforma";

    public const string Descripcion =
        "Compensa la comisión de una plataforma (Airbnb, Booking…) que la plataforma descuenta de lo que paga: la cuenta de acreedor (410) de la " +
        "factura de comisión al Debe contra la cuenta de cliente (430) del mismo tercero al Haber, por lo que la factura llevó a la 410. Así la 430 " +
        "queda con lo que ingresa el banco. Es una plantilla de sistema: la ejecuta la exportación de las facturas de compra y no se usa a mano.";

    public static PlantillaAsiento Predeterminada { get; } = new(
        0, Nombre, Descripcion, TipoPlantilla.Sistema, OrigenPlantilla.Proceso, [],
        [
            new LineaPlantilla(1, 1, "CuentaProveedor", "D", "Importe", "Concepto"),
            new LineaPlantilla(1, 2, "CuentaCliente", "H", "Importe", "Concepto"),
        ],
        ClaveProceso: ClaveProceso, ReglaFecha: ReglaFecha.FechaOrigen, Orden: 102, Clase: ClasePlantilla.Proceso);
}

/// <summary>Factura de comisión de una plataforma exportada y sin compensar (aviso de Inicio).</summary>
public sealed record ComisionPendiente(int IdRegistro, string Plataforma, string? NumeroFactura, DateTime? Fecha, decimal Importe);

/// <summary>Resultado de compensar las comisiones pendientes de un ejercicio.</summary>
public sealed record ResultadoCompensaciones(int Hechas, decimal Importe, IReadOnlyList<string> Problemas)
{
    public string Texto => (Hechas == 0 ? "No había comisiones por compensar." :
        $"He compensado {Hechas} {(Hechas == 1 ? "factura de comisión" : "facturas de comisión")} por {Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €.")
        + (Problemas.Count == 0 ? "" : " No he podido compensar: " + string.Join(" ", Problemas));
}

/// <summary>
/// Compensación de la comisión de las plataformas (facturacion/reservas/compensacion-comision): la plataforma paga lo que paga el huésped menos su
/// comisión, que factura aparte (factura de compra). Cuando el proveedor de una factura de compra exportada es el mismo tercero que el cliente de
/// una regla de venta activa de la sociedad (una plataforma), su 410 se compensa contra su 430 con un asiento propio por factura, generado con la
/// plantilla de sistema <see cref="PlantillaCompensacionComision"/>, en el lote «compras/factura-compra/COMPENSACION/IdRegistro». Exportar,
/// re-exportar y deshacer la exportación lo ponen al día (<see cref="SincronizarAsync"/>): nunca hay dos compensaciones de una factura. Si el
/// asiento vigente es de un trimestre cerrado, no se toca: el lote siguiente («IdRegistro.n») lleva su contra-asiento y, si toca, el nuevo.
/// </summary>
public sealed class CompensacionComision(IBaseDatos bd, IMotorAsientos motor, IReloj? reloj = null)
{
    public const string CodigoProceso = "COMPENSACION";
    public const string PrefijoAnulacion = "Anulación: ";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static IdentidadLote Lote(int idRegistro, int generacion) => new(AsientoCompra.MotorPropietario, CodigoProceso,
        generacion <= 0 ? idRegistro.ToString(CultureInfo.InvariantCulture) : $"{idRegistro.ToString(CultureInfo.InvariantCulture)}.{generacion.ToString(CultureInfo.InvariantCulture)}");

    /// <summary>Lo que debe haber: cuentas, importe, fecha y textos del asiento de compensación de una factura.</summary>
    private sealed record Deseada(string CuentaProveedor, string CuentaCliente, decimal Importe, DateTime Fecha, string Concepto, string? Documento);

    /// <summary>Datos de la factura que hacen falta para compensarla.</summary>
    private sealed record DatosFactura(int IdRegistro, ContextoSociedadEjercicio Contexto, int? IdProveedor, string? Cuenta, string? Numero, string Rotulo);

    // ---------- plataforma ----------

    /// <summary>
    /// Cliente de la plataforma del proveedor: el rol de cliente del mismo tercero si es el cliente de alguna regla de venta activa de la
    /// sociedad; null si el proveedor no es una plataforma.
    /// </summary>
    internal static async Task<(int IdCliente, string Nombre)?> PlataformaAsync(ISesionDatos s, string idSociedad, int idProveedor)
    {
        // El acreedor indicado en la regla de venta (facturacion/reservas 2026-11-25 14:13) gana: su comisión va contra la 430 del cliente de la regla.
        var porRegla = await s.ConsultarPrimeroAsync(
            "SELECT TOP 1 r.IdCliente, t.Nombre FROM (ReglasVenta AS r INNER JOIN Proveedores AS p ON p.IdProveedor = r.IdAcreedor) " +
            "INNER JOIN Terceros AS t ON t.IdTercero = p.IdTercero " +
            "WHERE r.IdAcreedor = ? AND r.IdSociedad = ? AND r.Activa = True AND r.IdCliente IS NOT NULL ORDER BY r.IdReglaVenta",
            f => (Id: f.Entero("IdCliente"), Nombre: f.Texto("Nombre") ?? ""), idProveedor, idSociedad);
        if (porRegla.Id > 0) return (porRegla.Id, porRegla.Nombre);
        var fila = await s.ConsultarPrimeroAsync(
            "SELECT TOP 1 c.IdCliente, t.Nombre FROM ((Proveedores AS p INNER JOIN Clientes AS c ON c.IdTercero = p.IdTercero) " +
            "INNER JOIN Terceros AS t ON t.IdTercero = p.IdTercero) INNER JOIN ReglasVenta AS r ON r.IdCliente = c.IdCliente " +
            "WHERE p.IdProveedor = ? AND r.IdSociedad = ? AND r.Activa = True ORDER BY c.IdCliente",
            f => (Id: f.Entero("IdCliente"), Nombre: f.Texto("Nombre") ?? ""), idProveedor, idSociedad);
        return fila.Id > 0 ? (fila.Id, fila.Nombre) : null;
    }

    /// <summary>Proveedores que son plataformas en la sociedad (su tercero es el cliente de una regla de venta activa).</summary>
    internal static async Task<IReadOnlyDictionary<int, string>> PlataformasAsync(ISesionDatos s, string idSociedad) =>
        (await s.ConsultarAsync(
            "SELECT DISTINCT p.IdProveedor, t.Nombre FROM ((Proveedores AS p INNER JOIN Clientes AS c ON c.IdTercero = p.IdTercero) " +
            "INNER JOIN Terceros AS t ON t.IdTercero = p.IdTercero) INNER JOIN ReglasVenta AS r ON r.IdCliente = c.IdCliente " +
            "WHERE r.IdSociedad = ? AND r.Activa = True",
            f => (Id: f.Entero("IdProveedor"), Nombre: f.Texto("Nombre") ?? ""), idSociedad))
        .Concat(await s.ConsultarAsync(
            "SELECT DISTINCT p.IdProveedor, t.Nombre FROM (ReglasVenta AS r INNER JOIN Proveedores AS p ON p.IdProveedor = r.IdAcreedor) " +
            "INNER JOIN Terceros AS t ON t.IdTercero = p.IdTercero WHERE r.IdSociedad = ? AND r.Activa = True AND r.IdCliente IS NOT NULL",
            f => (Id: f.Entero("IdProveedor"), Nombre: f.Texto("Nombre") ?? ""), idSociedad))
        .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Nombre);

    // ---------- poner al día ----------

    /// <summary>
    /// Pone al día la compensación de una factura de compra: si está exportada y su proveedor es una plataforma, su asiento de compensación por
    /// lo que la factura llevó a la 410; si no, ninguno. Devuelve un texto para el operador (vacío si no ha cambiado nada) o el error que lo
    /// impide (sin cuenta de cliente en el ejercicio…), que no deshace la exportación.
    /// </summary>
    public async Task<Resultado<string>> SincronizarAsync(int idRegistro, Actor actor)
    {
        var datos = await bd.EjecutarAsync(s => LeerFacturaAsync(s, idRegistro));
        if (datos is null) return Resultado<string>.Correcto("");
        var deseada = await DeseadaAsync(datos);
        if (!deseada.EsCorrecto) return Resultado<string>.Fallo(deseada.Errores);
        return await AplicarAsync(datos, deseada.Valor, actor);
    }

    private async Task<Resultado<Deseada?>> DeseadaAsync(DatosFactura d)
    {
        if (d.IdProveedor is not int idProveedor) return Resultado<Deseada?>.Correcto(null);
        var estado = await bd.EjecutarAsync(s => ContabilizadorCompras.EstadoAsync(s, d.IdRegistro));
        if (estado.IdAsiento is not int idAsiento) return Resultado<Deseada?>.Correcto(null);
        var ctx = d.Contexto;
        var plataforma = await bd.EjecutarAsync(s => PlataformaAsync(s, ctx.IdSociedad, idProveedor));
        if (plataforma is not { } p) return Resultado<Deseada?>.Correcto(null);

        var asiento = await motor.ObtenerAsync(idAsiento);
        if (asiento is null) return Resultado<Deseada?>.Correcto(null);
        // La cuenta del proveedor es la de la factura; si el asiento (del ERP) no la lleva, la 40/41 del asiento.
        var apuntes = asiento.Apuntes.Where(a => a.CodigoCuenta == d.Cuenta).ToList();
        if (apuntes.Count == 0) apuntes = asiento.Apuntes.Where(a => a.CodigoCuenta.StartsWith("40", StringComparison.Ordinal) || a.CodigoCuenta.StartsWith("41", StringComparison.Ordinal)).ToList();
        var cuentasProveedor = apuntes.Select(a => a.CodigoCuenta).Distinct().ToList();
        if (cuentasProveedor.Count != 1)
            return Resultado<Deseada?>.Fallo(new Error("Compensacion.SinCuentaProveedor",
                $"No sé qué cuenta de proveedor compensar en el asiento {idAsiento} de la {d.Rotulo.ToLowerInvariant()}.", d.Rotulo,
                cuentasProveedor.Count == 0 ? "El asiento no tiene ningún apunte a una cuenta 400 o 410." : $"El asiento tiene apuntes a {string.Join(" y ", cuentasProveedor)}.",
                "Revisa la cuenta del proveedor en la factura y vuelve a exportarla."));
        var importe = apuntes.Sum(a => a.Haber.Valor - a.Debe.Valor);
        if (importe == 0m) return Resultado<Deseada?>.Correcto(null);

        var cuentaCliente = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT TOP 1 cc.Codigo FROM CuentasClientesSociedad AS x INNER JOIN CuentasContables AS cc ON cc.IdIdentidadContable = x.IdIdentidadContable " +
            "WHERE x.IdCliente = ? AND x.IdSociedad = ? AND x.Clase = 'CLIENTE' AND cc.IdSociedadEjercicio = ?",
            f => f.Texto("Codigo"), p.IdCliente, ctx.IdSociedad, ctx.IdSociedadEjercicio));
        if (string.IsNullOrWhiteSpace(cuentaCliente))
            return Resultado<Deseada?>.Fallo(new Error("Compensacion.SinCuentaCliente",
                $"No he podido compensar la comisión de {p.Nombre}: su cliente no tiene cuenta 430 en {ctx.Ejercicio}.", d.Rotulo,
                "La compensación lleva la 410 de la factura contra la 430 del mismo tercero.",
                $"Abre la ficha del tercero {p.Nombre} en Terceros, dale su cuenta de cliente y pulsa «Compensar» en el aviso de Inicio."));

        var config = await bd.EjecutarAsync(s => ContabilizadorCompras.ConfiguracionAsync(s, ctx.IdSociedadEjercicio));
        var activo = TrimestreContable.Activo(config?.TrimestreActivo, ctx.Ejercicio, _reloj.Ahora.Date);
        var fecha = TrimestreContable.FechaAsiento(asiento.FechaContable, ctx.Ejercicio, activo);
        var numero = string.IsNullOrWhiteSpace(d.Numero) ? null : d.Numero.Trim();
        var concepto = $"Compensación comisión {p.Nombre}" + (numero is null ? "" : $" fra. {numero}");
        return Resultado<Deseada?>.Correcto(new Deseada(cuentasProveedor[0], cuentaCliente.Trim(), importe, fecha, concepto, numero));
    }

    private async Task<Resultado<string>> AplicarAsync(DatosFactura d, Deseada? deseada, Actor actor)
    {
        var ctx = d.Contexto;
        var (generacion, actuales) = await LoteVigenteAsync(ctx, d.IdRegistro);
        var vigente = actuales.LastOrDefault(a => !a.Descripcion.StartsWith(PrefijoAnulacion, StringComparison.Ordinal));
        if (deseada is null && vigente is null) return Resultado<string>.Correcto("");
        if (deseada is not null && vigente is not null && Igual(vigente, deseada)) return Resultado<string>.Correcto("");
        if (deseada is null && actuales.All(a => !Cerrado(a, ctx) && !a.Descripcion.StartsWith(PrefijoAnulacion, StringComparison.Ordinal)))
        {
            // Sin nada de un trimestre cerrado que anular: se borra el lote.
            var borrado = await motor.EliminarLoteAsync(ctx, Lote(d.IdRegistro, generacion), actor, $"Quitar la compensación de la {d.Rotulo.ToLowerInvariant()}");
            return borrado.EsCorrecto ? Resultado<string>.Correcto("He quitado su compensación con la 430.") : Resultado<string>.Fallo(borrado.Errores);
        }

        Asiento? nuevo = null;
        if (deseada is not null)
        {
            var construido = await ConstruirAsync(ctx, deseada);
            if (!construido.EsCorrecto) return Resultado<string>.Fallo(construido.Errores);
            nuevo = construido.Valor;
        }
        var asientos = new List<Asiento>();
        var cerrado = actuales.Any(a => Cerrado(a, ctx));
        if (cerrado)
        {
            // El lote vigente tiene un asiento de un trimestre cerrado: no se toca; el siguiente lleva el contra-asiento de su compensación.
            generacion++;
            if (vigente is not null) asientos.Add(Contra(vigente, ctx));
        }
        else asientos.AddRange(actuales.Where(a => a.Descripcion.StartsWith(PrefijoAnulacion, StringComparison.Ordinal)).Select(Copia));
        if (nuevo is not null) asientos.Add(nuevo);
        var motivo = $"Compensar la comisión de la {d.Rotulo.ToLowerInvariant()}";
        var r = await motor.ReemplazarLoteAsync(ctx, Lote(d.IdRegistro, generacion), asientos, actor, motivo);
        if (!r.EsCorrecto) return Resultado<string>.Fallo(r.Errores);
        return Resultado<string>.Correcto(deseada is null
            ? $"He anulado su compensación con la 430 con el asiento {r.Valor[^1]}."
            : $"He compensado su 410 contra la {deseada.CuentaCliente} con el asiento {r.Valor[^1]} ({deseada.Importe.ToString("N2", Es)} €).");
    }

    private async Task<Resultado<Asiento>> ConstruirAsync(ContextoSociedadEjercicio ctx, Deseada d)
    {
        var plantilla = await new ServicioPlantillasAsiento(bd).DeProcesoAsync(PlantillaCompensacionComision.ClaveProceso, ctx.IdSociedad)
            ?? PlantillaCompensacionComision.Predeterminada;
        var registro = new RegistroProceso("1", d.Fecha, new Dictionary<string, object?>
        {
            ["CuentaProveedor"] = d.CuentaProveedor,
            ["CuentaCliente"] = d.CuentaCliente,
            ["Importe"] = d.Importe,
            ["Concepto"] = d.Concepto,
        });
        var propuesta = MotorPlantillas.Proponer(plantilla, new OrigenProceso(PlantillaCompensacionComision.ClaveProceso, [registro]), null, new EntornoSociedad(ctx), d.Fecha);
        if (!propuesta.EsCorrecto) return Resultado<Asiento>.Fallo(propuesta.Errores);
        if (propuesta.Valor.Asientos.Count != 1)
            return Resultado<Asiento>.Fallo(new Error("Compensacion.PlantillaNoValida",
                $"La plantilla «{plantilla.Nombre}» genera {propuesta.Valor.Asientos.Count} asientos para una factura.", null,
                "Cada factura de comisión se compensa con un único asiento.", "Corrige la plantilla para que tenga un solo número de asiento."));
        var p = propuesta.Valor.Asientos[0];
        var asiento = new Asiento(ctx, p.Fecha, p.Descripcion) { IdPlantilla = plantilla.IdPlantilla > 0 ? plantilla.IdPlantilla : null };
        foreach (var x in p.Apuntes)
        {
            var apunte = x.AlDebe ? Apunte.AlDebe(x.Cuenta, x.Importe, x.Concepto) : Apunte.AlHaber(x.Cuenta, x.Importe, x.Concepto);
            apunte.NumeroDocumento = x.Documento ?? d.Documento;
            asiento.Apuntes.Add(apunte);
        }
        return Resultado<Asiento>.Correcto(asiento);
    }

    /// <summary>La compensación vigente ya es la que toca (mismas cuentas e importes; la fecha no cuenta).</summary>
    private static bool Igual(Asiento a, Deseada d)
    {
        var debe = a.Apuntes.Where(x => x.CodigoCuenta == d.CuentaProveedor).Sum(x => x.Debe.Valor - x.Haber.Valor);
        var haber = a.Apuntes.Where(x => x.CodigoCuenta == d.CuentaCliente).Sum(x => x.Haber.Valor - x.Debe.Valor);
        return a.Apuntes.All(x => x.CodigoCuenta == d.CuentaProveedor || x.CodigoCuenta == d.CuentaCliente) && debe == d.Importe && haber == d.Importe;
    }

    private bool Cerrado(Asiento a, ContextoSociedadEjercicio ctx) =>
        a.FechaContable < TrimestreContable.Inicio(ctx.Ejercicio, TrimestreActivo(ctx));

    private int TrimestreActivo(ContextoSociedadEjercicio ctx) =>
        TrimestreContable.Activo(_trimestres.TryGetValue(ctx.IdSociedadEjercicio, out var t) ? t : null, ctx.Ejercicio, _reloj.Ahora.Date);

    private readonly Dictionary<int, int?> _trimestres = [];

    /// <summary>Contra-asiento de una compensación de un trimestre cerrado, el día 1 del trimestre activo.</summary>
    private Asiento Contra(Asiento original, ContextoSociedadEjercicio ctx)
    {
        var contra = new Asiento(ctx, TrimestreContable.Inicio(ctx.Ejercicio, TrimestreActivo(ctx)), Recortar(PrefijoAnulacion + original.Descripcion)) { IdPlantilla = original.IdPlantilla };
        foreach (var a in original.Apuntes)
        {
            var x = a.Debe.Valor != 0m ? Apunte.AlHaber(a.CodigoCuenta, a.Debe.Valor, Recortar(PrefijoAnulacion + a.Descripcion)) : Apunte.AlDebe(a.CodigoCuenta, a.Haber.Valor, Recortar(PrefijoAnulacion + a.Descripcion));
            x.NumeroDocumento = a.NumeroDocumento;
            contra.Apuntes.Add(x);
        }
        return contra;
    }

    /// <summary>Copia de un asiento del lote para volver a grabarlo igual al regenerar el lote.</summary>
    private static Asiento Copia(Asiento original)
    {
        var copia = new Asiento(original.Contexto, original.FechaContable, original.Descripcion) { IdPlantilla = original.IdPlantilla, Notas = original.Notas };
        foreach (var a in original.Apuntes)
        {
            var x = a.Debe.Valor != 0m ? Apunte.AlDebe(a.CodigoCuenta, a.Debe.Valor, a.Descripcion) : Apunte.AlHaber(a.CodigoCuenta, a.Haber.Valor, a.Descripcion);
            x.NumeroDocumento = a.NumeroDocumento;
            copia.Apuntes.Add(x);
        }
        return copia;
    }

    private static string Recortar(string texto) => texto.Length <= ReglasAsiento.LongitudDescripcion ? texto : texto[..ReglasAsiento.LongitudDescripcion];

    /// <summary>Generación vigente del lote de compensación de la factura (la más alta que tiene asientos) y sus asientos, en orden.</summary>
    private async Task<(int Generacion, IReadOnlyList<Asiento> Asientos)> LoteVigenteAsync(ContextoSociedadEjercicio ctx, int idRegistro)
    {
        var id = idRegistro.ToString(CultureInfo.InvariantCulture);
        var filas = await bd.EjecutarAsync(async s =>
        {
            _trimestres[ctx.IdSociedadEjercicio] = (await ContabilizadorCompras.ConfiguracionAsync(s, ctx.IdSociedadEjercicio))?.TrimestreActivo;
            return await s.ConsultarAsync(
                "SELECT IdAsiento, ClaveEjecucion FROM Asientos WHERE EsAutomatico = True AND MotorPropietario = ? AND CodigoProceso = ? AND IdSociedadEjercicio = ? " +
                "AND (ClaveEjecucion = ? OR ClaveEjecucion LIKE ?) ORDER BY IdAsiento",
                f => (Id: f.Entero("IdAsiento"), Clave: f.Texto("ClaveEjecucion") ?? ""), AsientoCompra.MotorPropietario, CodigoProceso, ctx.IdSociedadEjercicio, id, id + ".%");
        });
        if (filas.Count == 0) return (0, []);
        int Gen(string clave) => clave.Contains('.', StringComparison.Ordinal) && int.TryParse(clave[(clave.IndexOf('.', StringComparison.Ordinal) + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var g) ? g : 0;
        var generacion = filas.Max(f => Gen(f.Clave));
        var asientos = new List<Asiento>();
        foreach (var f in filas.Where(f => Gen(f.Clave) == generacion))
            if (await motor.ObtenerAsync(f.Id) is { } a) asientos.Add(a);
        return (generacion, asientos);
    }

    private static async Task<DatosFactura?> LeerFacturaAsync(ISesionDatos s, int idRegistro)
    {
        var f = await s.ConsultarPrimeroAsync(
            "SELECT fc.IdSociedadEjercicio, fc.IdProveedor, fc.CuentaContableProveedor, fc.NumeroFactura, se.IdSociedad, se.Ejercicio " +
            "FROM FacturasCompra AS fc INNER JOIN SociedadesEjercicios AS se ON se.IdSociedadEjercicio = fc.IdSociedadEjercicio WHERE fc.IdRegistro = ?",
            x => (IdSe: x.Entero("IdSociedadEjercicio"), Prov: x.EnteroNulo("IdProveedor"), Cuenta: x.Texto("CuentaContableProveedor"), Numero: x.Texto("NumeroFactura"),
                Sociedad: x.Texto("IdSociedad") ?? "", Ejercicio: x.Texto("Ejercicio")), idRegistro);
        if (f.IdSe == 0 || !int.TryParse(f.Ejercicio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ejercicio)) return null;
        var numero = string.IsNullOrWhiteSpace(f.Numero) ? null : f.Numero.Trim();
        return new DatosFactura(idRegistro, new ContextoSociedadEjercicio(f.Sociedad, f.IdSe, ejercicio), f.Prov, f.Cuenta?.Trim(), numero,
            numero is null ? $"Factura de compra {idRegistro}" : $"Factura de compra {numero}");
    }

    // ---------- pendientes (aviso de Inicio) ----------

    /// <summary>
    /// Facturas de comisión de plataformas del ejercicio exportadas (por ProContaVF o en el ERP) sin compensación vigente, con lo que llevaron a
    /// la 410 (facturacion/reservas/compensacion-comision, propietario 2026-11-16: el ejercicio de trabajo; 2025 lo cubre el asiento anual del ERP).
    /// </summary>
    public async Task<IReadOnlyList<ComisionPendiente>> PendientesAsync(string idSociedad, int idSociedadEjercicio)
    {
        var (plataformas, facturas) = await bd.EjecutarAsync(async s =>
        {
            var p = await PlataformasAsync(s, idSociedad);
            if (p.Count == 0) return (p, (IReadOnlyList<(int Id, int Prov, string? Numero, DateTime? Fecha)>)[]);
            var lista = await s.ConsultarAsync(
                "SELECT IdRegistro, IdProveedor, NumeroFactura, Fecha FROM FacturasCompra WHERE IdSociedadEjercicio = ? AND IdProveedor IN (" +
                string.Join(",", p.Keys.Select(k => k.ToString(CultureInfo.InvariantCulture))) + ") AND (Duplicada = False OR Duplicada IS NULL) ORDER BY Fecha, IdRegistro",
                x => (Id: x.Entero("IdRegistro"), Prov: x.Entero("IdProveedor"), Numero: x.Texto("NumeroFactura"), Fecha: x.FechaNula("Fecha")), idSociedadEjercicio);
            return (p, lista);
        });
        var r = new List<ComisionPendiente>();
        foreach (var f in facturas)
        {
            var datos = await bd.EjecutarAsync(s => LeerFacturaAsync(s, f.Id));
            if (datos is null) continue;
            var deseada = await DeseadaAsync(datos);
            var (_, actuales) = await LoteVigenteAsync(datos.Contexto, f.Id);
            var vigente = actuales.LastOrDefault(a => !a.Descripcion.StartsWith(PrefijoAnulacion, StringComparison.Ordinal));
            if (!deseada.EsCorrecto)
            {
                r.Add(new ComisionPendiente(f.Id, plataformas[f.Prov], f.Numero, f.Fecha, 0m));
                continue;
            }
            if (deseada.Valor is { } d && (vigente is null || !Igual(vigente, d)))
                r.Add(new ComisionPendiente(f.Id, plataformas[f.Prov], f.Numero, f.Fecha, d.Importe));
        }
        return r;
    }

    /// <summary>Compensa las facturas de comisión pendientes del ejercicio (botón del aviso de Inicio), una por una: un fallo no para las demás.</summary>
    public async Task<ResultadoCompensaciones> CompensarPendientesAsync(string idSociedad, int idSociedadEjercicio, Actor actor)
    {
        var pendientes = await PendientesAsync(idSociedad, idSociedadEjercicio);
        int hechas = 0;
        decimal total = 0m;
        var problemas = new List<string>();
        foreach (var p in pendientes)
        {
            var r = await SincronizarAsync(p.IdRegistro, actor);
            if (r.EsCorrecto) { hechas++; total += p.Importe; }
            else problemas.AddRange(r.Errores.Select(e => e.AccionCorrectora is { } a ? $"{e.Mensaje} {a}" : e.Mensaje));
        }
        return new ResultadoCompensaciones(hechas, total, problemas);
    }
}
