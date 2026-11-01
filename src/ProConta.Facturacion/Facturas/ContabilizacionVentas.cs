using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Facturas;

/// <summary>Cuentas ya resueltas en el ejercicio para el asiento de una factura de venta.</summary>
/// <param name="Cliente">Cuenta del rol Cliente en la sociedad (Debe, por el total a cobrar).</param>
/// <param name="Retencion">Cuenta de retención del cliente (Debe), obligatoria si la factura retiene.</param>
/// <param name="Iva">Cuenta de IVA repercutido por porcentaje (Haber).</param>
/// <param name="Suplido">Cuenta de suplidos del ejercicio, para las líneas suplidas sin cuenta propia.</param>
public sealed record CuentasVenta(string Cliente, string? Retencion, IReadOnlyDictionary<decimal, string> Iva, string? Suplido);

/// <summary>
/// Asiento de una factura de venta (facturacion/factura-venta/contabilizacion), sin BD: Debe cliente por el total y
/// retención; Haber ingresos por cuenta de línea (y centro de coste), IVA por tipo y suplidos. Los importes negativos
/// (rectificativas) van en positivo al lado contrario, porque el motor de asientos no admite negativos.
/// </summary>
public static class AsientoVenta
{
    public const string MotorPropietario = "facturacion/factura-venta";
    public const string CodigoProceso = "VENTA";

    public static IdentidadLote Lote(int idFactura) => new(MotorPropietario, CodigoProceso, idFactura.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Fecha del asiento: el día en que se genera si cae en el ejercicio; si no, la fecha de la factura.</summary>
    public static DateTime Fecha(FacturaVenta f, int ejercicio, DateTime hoy) =>
        hoy.Year == ejercicio ? hoy.Date : (f.FechaFactura ?? new DateTime(ejercicio, 12, 31)).Date;

    public static Asiento Construir(FacturaVenta f, ContextoSociedadEjercicio contexto, CuentasVenta cuentas, DateTime fecha)
    {
        var numero = f.NumeroFactura ?? f.Rotulo;
        var cliente = f.Cliente.NombreVisible;
        var descripcion = Recortar($"{(f.EsRectificativa ? "Rectificativa" : "Factura")} {numero}{(cliente.Length > 0 ? " · " + cliente : "")}", ReglasAsiento.LongitudDescripcion);
        var asiento = new Asiento(contexto, fecha, descripcion)
        {
            Tipo = f.EsRectificativa ? TipoAsiento.Rectificacion : TipoAsiento.Ordinario
        };
        var t = f.Totales;
        void Anadir(string cuenta, Importe importe, bool alDebe, string texto, bool conCliente = false, int? centro = null)
        {
            if (importe.EsCero) return;
            var debe = importe.EsNegativo ? !alDebe : alDebe;
            var valor = importe.Absoluto();
            asiento.Apuntes.Add(new Apunte
            {
                CodigoCuenta = cuenta.Trim(),
                Debe = debe ? valor : Importe.Cero,
                Haber = debe ? Importe.Cero : valor,
                Descripcion = Recortar(texto, ReglasAsiento.LongitudDescripcion),
                NumeroDocumento = numero,
                IdCliente = conCliente ? f.IdCliente : null,
                IdCentroCoste = centro,
                GeneradoAutomaticamente = true,
                MotivoGeneracion = $"Contabilización de la factura de venta {numero}"
            });
        }

        Anadir(cuentas.Cliente, t.ImporteTotal, true, descripcion, conCliente: true);
        if (!t.Retencion.EsCero)
            Anadir(cuentas.Retencion ?? "", t.Retencion, true, $"Retención {f.TipoRetencion:0.##} % · {numero}", conCliente: true);
        foreach (var g in f.Lineas.Where(l => !l.EsSuplido).GroupBy(l => (Cuenta: l.CuentaContable?.Trim() ?? "", l.IdCentroCoste)).OrderBy(g => g.Key.Cuenta))
            Anadir(g.Key.Cuenta, Importe.Sumar(g.Select(l => l.BaseImponible)), false,
                g.Count() == 1 ? $"{g.First().Nombre} · {numero}" : $"Ingresos · {numero}", centro: g.Key.IdCentroCoste);
        foreach (var d in CalculoFactura.Desglose(f.Lineas).GroupBy(d => d.TipoIva).OrderByDescending(g => g.Key))
            Anadir(cuentas.Iva.TryGetValue(d.Key, out var c) ? c : "", Importe.Sumar(d.Select(x => x.CuotaIva)), false, $"IVA {d.Key:0.##} % · {numero}");
        foreach (var g in f.Lineas.Where(l => l.EsSuplido).GroupBy(l => string.IsNullOrWhiteSpace(l.CuentaContable) ? cuentas.Suplido ?? "" : l.CuentaContable.Trim()))
            Anadir(g.Key, Importe.Sumar(g.Select(l => l.ImporteTotal)), false, $"Suplidos · {numero}");
        return asiento;
    }

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}

/// <summary>
/// Contabilización de las ventas con BD: resuelve las cuentas en el ejercicio de la factura (cliente y retención por
/// su rol en la sociedad, ingresos por línea, IVA y suplidos del ejercicio), las comprueba y regenera el lote de la
/// factura. Un fallo aquí no deshace la validación: la factura queda validada y sin asiento, y se puede repetir.
/// </summary>
public sealed class ContabilizadorVentas(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, ServicioRoles roles, IServicioSociedades sociedades, IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>Comprueba que todas las cuentas del asiento existen y admiten apuntes; devuelve las incidencias (vacía si todo está bien).</summary>
    public async Task<(CuentasVenta? Cuentas, IReadOnlyList<Incidencia> Incidencias)> ResolverAsync(FacturaVenta f, ContextoSociedadEjercicio contexto)
    {
        var r = new List<Incidencia>();
        var config = await new LectorConfiguracionEjercicio(bd).ObtenerAsync(contexto.IdSociedadEjercicio);
        if (config is null)
        {
            r.Add(new("Contabilizacion.SinEjercicio", $"No encuentro el ejercicio {contexto.Ejercicio} de la factura."));
            return (null, r);
        }

        async Task<string?> Comprobar(string? codigo, string queEs, string? campo, string? accion = null)
        {
            if (string.IsNullOrWhiteSpace(codigo)) return null;
            var c = await cuentas.ResolverAsync(contexto, codigo);
            if (!c.EsCorrecto)
            {
                r.Add(new("Contabilizacion.CuentaNoExiste", $"La {queEs} {codigo.Trim()} no existe en el ejercicio {contexto.Ejercicio}. Revísala o da de alta la cuenta.", campo, Accion: accion));
                return null;
            }
            var admite = ReglasCuenta.AdmiteApuntes(c.Valor);
            if (!admite.EsCorrecto)
            {
                r.Add(new("Contabilizacion.CuentaNoAdmiteApuntes", $"La {queEs} {c.Valor.Codigo} {c.Valor.Nombre} no admite apuntes: {admite.Error!.Causa?.TrimEnd('.').ToLowerInvariant()}.", campo, Accion: accion));
                return null;
            }
            return c.Valor.Codigo;
        }

        // Cliente y retención: las cuentas de su rol en la sociedad (terceros/roles).
        string? cuentaCliente = null, cuentaRetencion = null;
        var t = f.Totales;
        if (f.IdCliente is int idCliente)
        {
            var idTercero = await bd.EjecutarAsync(s => Instantaneas.TerceroDelClienteAsync(s, idCliente));
            var deRoles = idTercero is int it ? await roles.CuentasAsync(it, contexto) : new CuentasRolesTercero(null, null, null);
            cuentaCliente = await CuentaRolAsync(deRoles.Cliente, "cuenta del cliente", "AsignarCuentaCliente");
            if (!t.Retencion.EsCero)
                cuentaRetencion = await CuentaRolAsync(deRoles.Retencion, "cuenta de retención del cliente", "AsignarCuentaRetencion");
        }

        async Task<string?> CuentaRolAsync(CuentaRol? rol, string queEs, string accion)
        {
            if (rol is null)
            {
                r.Add(new("Contabilizacion.ClienteSinCuenta", $"El cliente no tiene {queEs} en esta sociedad. Asígnale una o créala con su nombre.", "Cliente", Accion: accion));
                return null;
            }
            if (!rol.ExisteEnEjercicio)
            {
                r.Add(new("Contabilizacion.CuentaClienteFueraDeEjercicio", $"La {queEs} {rol.Codigo} aún no existe en el ejercicio {contexto.Ejercicio}. Créala en este ejercicio.", "Cliente", Accion: accion));
                return null;
            }
            return await Comprobar(rol.Codigo, queEs, "Cliente", accion);
        }

        foreach (var cuenta in f.Lineas.Where(l => !l.EsSuplido && !string.IsNullOrWhiteSpace(l.CuentaContable)).Select(l => l.CuentaContable!.Trim()).Distinct())
        {
            var linea = f.Lineas.First(l => l.CuentaContable?.Trim() == cuenta);
            await Comprobar(cuenta, "cuenta de ingreso", $"Linea{linea.NumeroLinea}.CuentaContable");
        }

        var iva = new Dictionary<decimal, string>();
        foreach (var d in CalculoFactura.Desglose(f.Lineas))
        {
            if (d.TipoRecargo != 0m || !d.CuotaRecargo.EsCero)
                r.Add(new("Contabilizacion.RecargoNoAdmitido", $"Hay líneas con recargo de equivalencia del {d.TipoRecargo:0.##} %, que aún no se contabiliza. Quita el recargo o factúralo con el ERP.", "Lineas"));
            if (d.CuotaIva.EsCero || iva.ContainsKey(d.TipoIva)) continue;
            var codigo = config.CuentaIva(d.TipoIva);
            if (codigo is null)
                r.Add(new("Contabilizacion.SinCuentaIva", $"El ejercicio {contexto.Ejercicio} no tiene cuenta de IVA repercutido para el {d.TipoIva:0.##} %. Configúrala en Parámetros y tablas › Ejercicios."));
            else if (await Comprobar(codigo, $"cuenta de IVA del {d.TipoIva:0.##} %", null) is string ok)
                iva[d.TipoIva] = ok;
        }

        string? suplido = null;
        foreach (var l in f.Lineas.Where(l => l.EsSuplido && !l.ImporteTotal.EsCero))
        {
            if (!string.IsNullOrWhiteSpace(l.CuentaContable))
                await Comprobar(l.CuentaContable, "cuenta del suplido", $"Linea{l.NumeroLinea}.CuentaContable");
            else if (suplido is null)
            {
                if (string.IsNullOrWhiteSpace(config.CuentaSuplido))
                    r.Add(new("Contabilizacion.SinCuentaSuplido", $"La línea {l.NumeroLinea} es un suplido sin cuenta y el ejercicio no tiene cuenta de suplidos. Pon la cuenta en la línea o configúrala en Parámetros y tablas › Ejercicios.", $"Linea{l.NumeroLinea}.CuentaContable"));
                else
                    suplido = await Comprobar(config.CuentaSuplido, "cuenta de suplidos del ejercicio", null) ?? "";
            }
        }

        return r.Count > 0 || cuentaCliente is null ? (null, r) : (new CuentasVenta(cuentaCliente, cuentaRetencion, iva, string.IsNullOrEmpty(suplido) ? null : suplido), r);
    }

    /// <summary>
    /// Genera (o regenera) el asiento de una factura numerada y guarda su IdAsientoContable. Regenerar sustituye el
    /// asiento anterior del lote de la factura, nunca lo duplica.
    /// </summary>
    public async Task<Resultado<int>> ContabilizarAsync(FacturaVenta f, Actor actor)
    {
        var registro = f.Rotulo;
        if (f.NumeroFactura is null || f.Estado is EstadoFactura.Borrador)
            return Resultado<int>.Fallo(new Error("Contabilizacion.SinNumero", "Solo se contabiliza una factura validada.", registro,
                "Un borrador no tiene número ni asiento.", "Valida antes la factura."));
        var ejercicio = await sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio);
        if (ejercicio is null)
            return Resultado<int>.Fallo(new Error("Contabilizacion.SinEjercicio", "No encuentro el ejercicio de la factura.", registro));
        var contexto = ejercicio.Contexto;
        var (cuentasVenta, incidencias) = await ResolverAsync(f, contexto);
        if (cuentasVenta is null)
            return Resultado<int>.Fallo(incidencias.Select(i => new Error(i.Codigo, i.Mensaje, registro, null,
                i.Accion is null ? "Corrígelo y vuelve a contabilizar la factura." : null)));
        var asiento = AsientoVenta.Construir(f, contexto, cuentasVenta, AsientoVenta.Fecha(f, contexto.Ejercicio, _reloj.Ahora));
        var lote = await motor.ReemplazarLoteAsync(contexto, AsientoVenta.Lote(f.IdFactura), [asiento], actor,
            f.IdAsientoContable is null ? $"Validación de la factura {f.NumeroFactura}" : $"Regenerar el asiento de la factura {f.NumeroFactura}");
        if (!lote.EsCorrecto) return Resultado<int>.Fallo(lote.Errores);
        var idAsiento = lote.Valor[0];
        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Facturas SET IdAsientoContable = ? WHERE IdFactura = ?", idAsiento, f.IdFactura));
        f.IdAsientoContable = idAsiento;
        return Resultado<int>.Correcto(idAsiento);
    }
}
