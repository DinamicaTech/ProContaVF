using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
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
/// <param name="Iva">Cuenta general de IVA repercutido por porcentaje (Haber).</param>
/// <param name="Suplido">Cuenta de suplidos del ejercicio, para las líneas suplidas sin cuenta propia.</param>
/// <param name="SeriesPorCentro">Serie de 472 de la actividad de cada centro de coste; el centro que no esté usa la cuenta general.</param>
public sealed record CuentasVenta(string Cliente, string? Retencion, IReadOnlyDictionary<decimal, string> Iva, string? Suplido, IReadOnlyDictionary<int, string>? SeriesPorCentro = null)
{
    /// <summary>La 477 de la línea: la de la actividad de su centro de coste o, si no tiene, la general del tipo.</summary>
    public string CuentaIva(LineaFacturaVenta linea)
    {
        var general = Iva.TryGetValue(linea.TipoIva, out var c) ? c : "";
        return linea.IdCentroCoste is int centro && SeriesPorCentro?.TryGetValue(centro, out var serie) == true ? AsientoVenta.CuentaIvaActividad(general, serie) : general;
    }
}

/// <summary>
/// Una fila del desglose del libro de IVA repercutido (DesglosesIVARepercutido): régimen, calificación, tipo y 477 de la
/// actividad. Las líneas exentas también tienen fila, con la 477 del 0 % de su actividad aunque no lleven apunte: son las
/// que necesita la prorrata.
/// </summary>
/// <param name="Calificacion">S1, S2, N1 o N2; en una exenta, su causa (E1 a E6), como el importador de ContaSol.</param>
public sealed record TramoLibroVenta(string ClaveRegimen, string Calificacion, string? CausaExencion, decimal TipoIva, decimal Base, decimal Cuota,
    decimal TipoRecargo, decimal CuotaRecargo, string? Cuenta)
{
    public TramoLibroVenta Negado() => this with { Base = -Base, Cuota = -Cuota, CuotaRecargo = -CuotaRecargo };
}

/// <summary>Registro del libro de IVA repercutido de una factura: total de la factura y su desglose.</summary>
public sealed record LibroVenta(decimal Total, IReadOnlyList<TramoLibroVenta> Tramos)
{
    public decimal Base => Tramos.Sum(t => t.Base);
    public decimal Cuota => Tramos.Sum(t => t.Cuota);
    public decimal Recargo => Tramos.Sum(t => t.CuotaRecargo);
    public LibroVenta Negado() => new(-Total, [.. Tramos.Select(t => t.Negado())]);
    public bool Vacio => Total == 0m && Tramos.Count == 0;
}

/// <summary>
/// Asiento de una factura de venta (facturacion/factura-venta/contabilizacion), sin BD: Debe cliente por el total y
/// retención; Haber ingresos por cuenta de línea (y centro de coste), IVA por tipo y suplidos. Los importes negativos
/// (rectificativas) van en positivo al lado contrario, porque el motor de asientos no admite negativos.
/// </summary>
public static class AsientoVenta
{
    public const string MotorPropietario = "facturacion/factura-venta";
    public const string CodigoProceso = "VENTA";

    /// <summary>Lote de la factura: el primero es «IdFactura»; cada contra-asiento de un trimestre cerrado abre «IdFactura.N».</summary>
    public static IdentidadLote Lote(int idFactura, int generacion = 0)
    {
        var id = idFactura.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new(MotorPropietario, CodigoProceso, generacion <= 0 ? id : $"{id}.{generacion.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }

    /// <summary>Contra-asiento: los mismos apuntes con Debe y Haber cambiados.</summary>
    public static Asiento Contra(Asiento original, ContextoSociedadEjercicio contexto, DateTime fecha, string numero)
    {
        var contra = new Asiento(contexto, fecha, Recortar($"Anulación de {original.Descripcion}", ReglasAsiento.LongitudDescripcion)) { Tipo = TipoAsiento.Rectificacion };
        foreach (var a in original.Apuntes)
            contra.Apuntes.Add(new Apunte
            {
                CodigoCuenta = a.CodigoCuenta, Debe = a.Haber, Haber = a.Debe, Descripcion = a.Descripcion, NumeroDocumento = a.NumeroDocumento,
                IdCliente = a.IdCliente, IdCentroCoste = a.IdCentroCoste, GeneradoAutomaticamente = true,
                MotivoGeneracion = Recortar($"Contra-asiento del asiento {original.IdAsiento} de la factura de venta {numero}", ReglasAsiento.LongitudDescripcion)
            });
        return contra;
    }

    /// <summary>Lo que el asiento toma de la factura: si cambia, hay que deshacer y regenerar su exportación.</summary>
    public static string Huella(FacturaVenta f)
    {
        var b = new System.Text.StringBuilder();
        b.Append(System.Globalization.CultureInfo.InvariantCulture, $"{f.IdCliente}|{f.Cliente.Nombre}|{f.FechaFactura:d}|{f.TipoFactura}|{f.TipoRetencion}|{f.Descripcion?.Trim()}");
        foreach (var l in f.Lineas)
            b.Append(System.Globalization.CultureInfo.InvariantCulture,
                $"#{l.Nombre.Trim()}|{l.CuentaContable?.Trim()}|{l.IdCentroCoste}|{l.BaseImponible.Valor}|{l.TipoIva}|{l.CuotaIva.Valor}|{l.TipoRecargo}|{l.CuotaRecargo.Valor}|{l.EsSuplido}|{l.ImporteTotal.Valor}");
        return b.ToString();
    }

    /// <summary>Lo que sale en el documento: si cambia, el PDF archivado se vuelve a crear. Las notas internas no cuentan.</summary>
    public static string HuellaDocumento(FacturaVenta f)
    {
        var b = new System.Text.StringBuilder(Huella(f));
        b.Append(System.Globalization.CultureInfo.InvariantCulture, $"|{f.Cliente.RazonSocial}|{f.Cliente.Nif}|{f.FechaOperacion:d}|{f.FechaInicioValidez:d}|{f.FechaFinValidez:d}|{f.FormaDePago}|{f.DiaDePago}|{f.MotivoRectificacion}");
        foreach (var l in f.Lineas)
            b.Append(System.Globalization.CultureInfo.InvariantCulture, $"#{l.Descripcion}|{l.Cantidad}|{l.PrecioUnitario}|{l.DescuentoPorcentaje}|{l.DescuentoImporte}|{l.Fecha:d}");
        return b.ToString();
    }

    /// <summary>
    /// Fecha del asiento (propietario 2026-11-07 11:59, igual que en compras): la de la factura, o el día 1 del trimestre
    /// activo si es anterior (<see cref="TrimestreContable.FechaAsiento"/>). Sin fecha de factura, hoy si cae en el ejercicio.
    /// </summary>
    public static DateTime Fecha(FacturaVenta f, int ejercicio, int? trimestreActivo, DateTime hoy) =>
        TrimestreContable.FechaAsiento(f.FechaFactura ?? (hoy.Year == ejercicio ? hoy.Date : new DateTime(ejercicio, 12, 31)), ejercicio,
            TrimestreContable.Activo(trimestreActivo, ejercicio, hoy));

    /// <summary>
    /// La 477 de una actividad (facturacion/factura-venta/contabilizacion 2026-11-08): su serie de la 472 con el 2 cambiado
    /// por el 7 y la terminación de la 477 general del tipo (serie 4728 y 4770021 → 4778021).
    /// </summary>
    public static string CuentaIvaActividad(string general, string serie)
    {
        var g = general.Trim();
        return serie is { Length: 4 } && g.Length >= 3 && g.All(char.IsAsciiDigit) ? "477" + serie[3] + g[^3..] : g;
    }

    private const string Separador = " - ";

    /// <summary>
    /// Une las partes de la descripción de un apunte con « - » sin pasar de la longitud máxima. Solo se recorta la parte
    /// <paramref name="recortable"/> (la descripción de la factura o de la línea, propietario 2026-11-08): el cliente y el
    /// número se ven siempre.
    /// </summary>
    public static string Componer(int recortable, params string?[] partes)
    {
        var limpias = partes.Select(p => string.Join(' ', (p ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))).ToArray();
        var fijas = limpias.Where((p, i) => i != recortable && p.Length > 0).Sum(p => p.Length + Separador.Length);
        var cabe = Math.Max(0, ReglasAsiento.LongitudDescripcion - fijas);
        if (limpias[recortable].Length > cabe) limpias[recortable] = limpias[recortable][..cabe].TrimEnd();
        return Recortar(string.Join(Separador, limpias.Where(p => p.Length > 0)), ReglasAsiento.LongitudDescripcion);
    }

    /// <param name="nombreCliente">Nombre para las descripciones en lugar del congelado (vista previa de un cliente aún sin congelar).</param>
    public static Asiento Construir(FacturaVenta f, ContextoSociedadEjercicio contexto, CuentasVenta cuentas, DateTime fecha, string? nombreCliente = null)
    {
        var numero = f.NumeroFactura ?? f.Rotulo;
        var cliente = nombreCliente ?? (f.Cliente.EstaVacia ? "" : f.Cliente.NombreAsiento);
        var referencia = $"{(f.EsRectificativa ? "FraRect." : "Fra.")} {numero}";
        var texto = f.Descripcion;
        var descripcion = Componer(2, referencia, cliente, texto);
        var asiento = new Asiento(contexto, fecha, descripcion)
        {
            Tipo = f.EsRectificativa ? TipoAsiento.Rectificacion : TipoAsiento.Ordinario
        };
        var t = f.Totales;
        void Anadir(string cuenta, Importe importe, bool alDebe, string detalle, bool conCliente = false, int? centro = null)
        {
            if (importe.EsCero) return;
            var debe = importe.EsNegativo ? !alDebe : alDebe;
            var valor = importe.Absoluto();
            asiento.Apuntes.Add(new Apunte
            {
                CodigoCuenta = cuenta.Trim(),
                Debe = debe ? valor : Importe.Cero,
                Haber = debe ? Importe.Cero : valor,
                Descripcion = detalle,
                NumeroDocumento = numero,
                IdCliente = conCliente ? f.IdCliente : null,
                IdCentroCoste = centro,
                GeneradoAutomaticamente = true,
                MotivoGeneracion = $"Contabilización de la factura de venta {numero}"
            });
        }

        Anadir(cuentas.Cliente, t.ImporteTotal, true, descripcion, conCliente: true);
        if (!t.Retencion.EsCero)
            Anadir(cuentas.Retencion ?? "", t.Retencion, true, Componer(3, $"Retención {f.TipoRetencion:0.##} %", cliente, referencia, texto), conCliente: true);
        foreach (var g in f.Lineas.Where(l => !l.EsSuplido).GroupBy(l => (Cuenta: l.CuentaContable?.Trim() ?? "", l.IdCentroCoste)).OrderBy(g => g.Key.Cuenta))
            Anadir(g.Key.Cuenta, Importe.Sumar(g.Select(l => l.BaseImponible)), false,
                Componer(0, string.IsNullOrWhiteSpace(g.First().Nombre) ? texto : g.First().Nombre, cliente, referencia), centro: g.Key.IdCentroCoste);
        // El IVA de cada tipo, repartido por la 477 de la actividad de cada línea.
        foreach (var g in f.Lineas.Where(l => !l.EsSuplido).GroupBy(l => (l.TipoIva, Cuenta: cuentas.CuentaIva(l))).OrderByDescending(g => g.Key.TipoIva).ThenBy(g => g.Key.Cuenta, StringComparer.Ordinal))
            Anadir(g.Key.Cuenta, Importe.Sumar(g.Select(l => l.CuotaIva)), false, Componer(3, $"IVA repercutido {g.Key.TipoIva:0.##} %", cliente, referencia, texto));
        foreach (var g in f.Lineas.Where(l => l.EsSuplido).GroupBy(l => string.IsNullOrWhiteSpace(l.CuentaContable) ? cuentas.Suplido ?? "" : l.CuentaContable.Trim()))
            Anadir(g.Key, Importe.Sumar(g.Select(l => l.ImporteTotal)), false, Componer(3, "Suplidos", cliente, referencia, texto));
        return asiento;
    }

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];

    /// <summary>Calificación del libro: la causa de exención si la línea es exenta (E1 a E6); si no, su calificación (S1, S2, N1, N2).</summary>
    public static string Calificacion(LineaFacturaVenta l) =>
        !string.IsNullOrWhiteSpace(l.CausaExencion) ? l.CausaExencion.Trim()
        : string.IsNullOrWhiteSpace(l.CalificacionOperacion) ? "S1" : l.CalificacionOperacion.Trim();

    /// <summary>
    /// Registro del libro de IVA repercutido (facturacion/factura-venta/contabilizacion 2026-11-11), sin BD: una fila por
    /// régimen, calificación, tipo y 477 de la actividad de las líneas no suplidas. Las cuotas suman las de las líneas, igual
    /// que los apuntes de IVA del asiento. En una rectificativa los importes van en negativo, como en sus líneas.
    /// </summary>
    public static LibroVenta Libro(FacturaVenta f, CuentasVenta cuentas) =>
        new(f.Totales.ImporteTotal.Valor, f.Lineas.Where(l => !l.EsSuplido)
            .GroupBy(l => (Clave: string.IsNullOrWhiteSpace(l.ClaveRegimen) ? "01" : l.ClaveRegimen.Trim(), Calificacion: Calificacion(l),
                Causa: string.IsNullOrWhiteSpace(l.CausaExencion) ? null : l.CausaExencion.Trim(), l.TipoIva, l.TipoRecargo, Cuenta: cuentas.CuentaIva(l)))
            .OrderByDescending(g => g.Key.TipoIva).ThenBy(g => g.Key.Calificacion, StringComparer.Ordinal).ThenBy(g => g.Key.Cuenta, StringComparer.Ordinal)
            .Select(g => new TramoLibroVenta(g.Key.Clave, g.Key.Calificacion, g.Key.Causa, g.Key.TipoIva,
                Importe.Sumar(g.Select(l => l.BaseImponible)).Valor, Importe.Sumar(g.Select(l => l.CuotaIva)).Valor,
                g.Key.TipoRecargo, Importe.Sumar(g.Select(l => l.CuotaRecargo)).Valor, string.IsNullOrWhiteSpace(g.Key.Cuenta) ? null : g.Key.Cuenta))
            .ToList());

    /// <summary>
    /// Aviso de la decisión del propietario del 2026-11-11 (punto 1a): una factura de un trimestre ya cerrado se registra
    /// en el libro del trimestre de su asiento, el activo; su fecha real queda en el registro. Null si coinciden.
    /// </summary>
    public static string? AvisoTrimestre(DateTime? fechaFactura, DateTime fechaAsiento) =>
        fechaFactura is not DateTime ff || (ff.Year == fechaAsiento.Year && TrimestreContable.De(ff) == TrimestreContable.De(fechaAsiento)) ? null
        : $"Es del {TrimestreContable.De(ff)}T de {ff.Year}, que está cerrado: queda en el libro del {TrimestreContable.De(fechaAsiento)}T de {fechaAsiento.Year}. " +
          "Si ese 303 ya se presentó, coméntalo con tu gestor por si hay que hacer una complementaria.";
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
    /// <param name="crearCuentasIva">Al contabilizar: la cuenta de IVA repercutido configurada (o la 477 deducida) que no exista se crea.</param>
    public async Task<(CuentasVenta? Cuentas, IReadOnlyList<Incidencia> Incidencias)> ResolverAsync(FacturaVenta f, ContextoSociedadEjercicio contexto, bool crearCuentasIva = false)
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

        foreach (var d in CalculoFactura.Desglose(f.Lineas).Where(d => d.TipoRecargo != 0m || !d.CuotaRecargo.EsCero))
            r.Add(new("Contabilizacion.RecargoNoAdmitido", $"Hay líneas con recargo de equivalencia del {d.TipoRecargo:0.##} %, que aún no se contabiliza. Quita el recargo o factúralo con el ERP.", "Lineas"));

        // IVA de cada tipo por la 477 de la actividad de cada línea (propietario 2026-11-08 09:40). La subcuenta del ejercicio
        // que aún no existe no es un error: se crea al contabilizar (propietario 2026-11-07 11:59).
        var conIva = f.Lineas.Where(l => !l.EsSuplido && !l.CuotaIva.EsCero).ToList();
        // Las exentas no llevan apunte, pero su 477 del 0 % de la actividad va al libro de IVA (para la prorrata).
        var sinCuota = f.Lineas.Where(l => !l.EsSuplido && l.CuotaIva.EsCero).ToList();
        IReadOnlyDictionary<int, ActividadIva> actividades = conIva.Concat(sinCuota).Any(l => l.IdCentroCoste is not null)
            ? await new LectorActividadesIva(bd).PorCentroAsync(contexto.IdSociedad) : new Dictionary<int, ActividadIva>();
        var series = new Dictionary<int, string>();
        foreach (var centro in conIva.Concat(sinCuota).Select(l => l.IdCentroCoste).OfType<int>().Distinct())
            if (actividades.TryGetValue(centro, out var a) && a.Serie is { Length: 4 } serie) series[centro] = serie;

        var iva = new Dictionary<decimal, string>();
        var comprobadas = new HashSet<string>();
        foreach (var l in conIva)
        {
            var general = config.CuentaIva(l.TipoIva);
            if (general is null)
            {
                if (iva.TryAdd(l.TipoIva, ""))
                    r.Add(new("Contabilizacion.SinCuentaIva", $"El ejercicio {contexto.Ejercicio} no tiene cuenta de IVA repercutido para el {l.TipoIva:0.##} %. Configúrala en Parámetros y tablas › Ejercicios."));
                continue;
            }
            iva[l.TipoIva] = general;
            var serieLinea = l.IdCentroCoste is int c && series.TryGetValue(c, out var sc) ? sc : null;
            var codigo = serieLinea is null ? general : AsientoVenta.CuentaIvaActividad(general, serieLinea);
            if (!comprobadas.Add(codigo)) continue;
            if (!(await cuentas.ResolverAsync(contexto, codigo)).EsCorrecto)
            {
                if (!crearCuentasIva) continue;
                var nombre = $"IVA repercutido del {l.TipoIva:0.##} %{(serieLinea is null ? "" : " de " + actividades[l.IdCentroCoste!.Value].Nombre)}";
                var alta = await cuentas.AltaSubcuentaAsync(contexto, codigo, nombre);
                if (!alta.EsCorrecto) r.Add(new("Contabilizacion.CuentaNoCreada", $"No he podido crear la cuenta de IVA repercutido {codigo}: {alta.Error!.Mensaje}"));
            }
            await Comprobar(codigo, $"cuenta de IVA del {l.TipoIva:0.##} %", null);
        }

        // La 477 de las líneas sin cuota solo se usa en el libro: si el ejercicio no tiene cuenta de ese tipo o no se puede
        // crear, el desglose queda sin cuenta y no impide contabilizar.
        foreach (var l in sinCuota)
        {
            if (config.CuentaIva(l.TipoIva) is not { } general) continue;
            iva.TryAdd(l.TipoIva, general);
            var serieLinea = l.IdCentroCoste is int c && series.TryGetValue(c, out var sc) ? sc : null;
            var codigo = serieLinea is null ? general : AsientoVenta.CuentaIvaActividad(general, serieLinea);
            if (!comprobadas.Add(codigo) || !crearCuentasIva || (await cuentas.ResolverAsync(contexto, codigo)).EsCorrecto) continue;
            await cuentas.AltaSubcuentaAsync(contexto, codigo,
                $"IVA repercutido del {l.TipoIva:0.##} %{(serieLinea is null ? "" : " de " + actividades[l.IdCentroCoste!.Value].Nombre)}");
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

        return r.Count > 0 || cuentaCliente is null ? (null, r) : (new CuentasVenta(cuentaCliente, cuentaRetencion, iva, string.IsNullOrEmpty(suplido) ? null : suplido, series), r);
    }

    /// <summary>
    /// Genera (o regenera) el asiento de una factura numerada y, en la misma transacción, su registro del libro de IVA
    /// repercutido, y guarda su IdAsientoContable. Regenerar sustituye el asiento y el registro anteriores del lote de la
    /// factura, nunca los duplica. Una factura de un año anterior al de su ejercicio no se contabiliza (propietario
    /// 2026-11-11, punto 3a).
    /// </summary>
    /// <param name="enLaTransaccion">Cambios de la factura que se guardan junto con su asiento: o todo o nada.</param>
    /// <param name="avisar">Recibe el aviso de una factura de un trimestre cerrado que va al libro del trimestre activo.</param>
    public async Task<Resultado<int>> ContabilizarAsync(FacturaVenta f, Actor actor, Func<ISesionDatos, Task>? enLaTransaccion = null, Action<string>? avisar = null)
    {
        var registro = f.Rotulo;
        if (f.NumeroFactura is null || f.Estado is EstadoFactura.Borrador)
            return Resultado<int>.Fallo(new Error("Contabilizacion.SinNumero", "Solo se contabiliza una factura validada.", registro,
                "Un borrador no tiene número ni asiento.", "Valida antes la factura."));
        var ejercicio = await sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio);
        if (ejercicio is null)
            return Resultado<int>.Fallo(new Error("Contabilizacion.SinEjercicio", "No encuentro el ejercicio de la factura.", registro));
        var contexto = ejercicio.Contexto;
        if (f.FechaFactura is DateTime ff && ff.Year < contexto.Ejercicio)
            return Resultado<int>.Fallo(new Error("Contabilizacion.EjercicioAnterior",
                $"La factura es del {ff:dd/MM/yyyy}, de un año anterior al ejercicio {contexto.Ejercicio} en el que está.", registro,
                "Una factura se contabiliza en el ejercicio de su fecha; las de años anteriores que vienen del ERP ya tienen su asiento en la contabilidad de ese año.",
                $"Si de verdad le falta el asiento, contabilízala desde el ejercicio {ff.Year}."));
        var (cuentasVenta, incidencias) = await ResolverAsync(f, contexto, crearCuentasIva: true);
        if (cuentasVenta is null)
            return Resultado<int>.Fallo(incidencias.Select(i => new Error(i.Codigo, i.Mensaje, registro, null,
                i.Accion is null ? "Corrígelo y vuelve a contabilizar la factura." : null)));
        var activo = await new ServicioTrimestreActivo(bd, _reloj).ObtenerAsync(contexto.IdSociedadEjercicio);
        var trimestre = TrimestreContable.Activo(activo, contexto.Ejercicio, _reloj.Ahora);
        var estado = await EstadoAsync(f.IdFactura);
        var cerrado = await LoteCerradoAsync(f, contexto, estado, trimestre);
        // Lote abierto: se sustituye, y su contra-asiento, si lo tiene, se queda. Cerrado: lote nuevo con el contra-asiento del vigente.
        var generacion = cerrado ? estado.Generacion + 1 : estado.Generacion;
        var anulado = cerrado ? estado.Vigente : estado.Anulado;
        var asientos = new List<Asiento>();
        var libros = new List<(LibroVenta Libro, bool Anulacion)>();
        if (anulado is int idAnulado)
        {
            var contra = await ContraAsync(idAnulado, f, contexto, trimestre);
            if (!contra.EsCorrecto) return Resultado<int>.Fallo(contra.Errores);
            asientos.Add(contra.Valor.Asiento);
            libros.Add((contra.Valor.Libro, true));
        }
        var fecha = AsientoVenta.Fecha(f, contexto.Ejercicio, activo, _reloj.Ahora);
        asientos.Add(AsientoVenta.Construir(f, contexto, cuentasVenta, fecha));
        libros.Add((AsientoVenta.Libro(f, cuentasVenta), false));
        var aviso = AsientoVenta.AvisoTrimestre(f.FechaFactura, fecha);
        if (aviso is not null) f.Anotar(_reloj.Ahora, actor.Nombre, $"Libro de IVA: {aviso}");
        var ahora = _reloj.Ahora;
        var lote = await motor.ReemplazarLoteAsync(contexto, AsientoVenta.Lote(f.IdFactura, generacion), asientos, actor,
            estado.Vigente is null ? $"Validación de la factura {f.NumeroFactura}" : $"Regenerar el asiento de la factura {f.NumeroFactura}",
            new TrabajoLote(
                Antes: BorrarLibroAsync,
                Despues: async (s, ids) =>
                {
                    if (enLaTransaccion is not null) await enLaTransaccion(s);
                    for (var i = 0; i < ids.Count; i++)
                    {
                        // El asiento anulado puede no tener registro (anterior al libro de ventas): su contra-asiento tampoco lo lleva.
                        if (libros[i].Libro.Vacio) continue;
                        var periodo = await PeriodoIvaAsync(s, contexto.IdSociedadEjercicio, contexto.Ejercicio, TrimestreContable.De(asientos[i].FechaContable));
                        await GrabarLibroAsync(s, f, contexto, ids[i], periodo, libros[i].Libro, libros[i].Anulacion, actor, ahora,
                            libros[i].Anulacion ? null : aviso);
                    }
                    if (aviso is null)
                        await s.EjecutarAsync("UPDATE Facturas SET IdAsientoContable = ?, IdAsientoAnulado = ?, GeneracionExportacion = ?, FechaExportacionContable = ? WHERE IdFactura = ?",
                            ids[^1], anulado, generacion, ahora, f.IdFactura);
                    else
                        await s.EjecutarAsync("UPDATE Facturas SET IdAsientoContable = ?, IdAsientoAnulado = ?, GeneracionExportacion = ?, FechaExportacionContable = ?, Historial = ? WHERE IdFactura = ?",
                            ids[^1], anulado, generacion, ahora, f.Historial, f.IdFactura);
                    return Resultado.Correcto();
                }));
        if (!lote.EsCorrecto) return Resultado<int>.Fallo(lote.Errores);
        f.IdAsientoContable = lote.Valor[^1];
        if (aviso is not null) avisar?.Invoke(aviso);
        return Resultado<int>.Correcto(lote.Valor[^1]);
    }

    /// <summary>
    /// Lo que hará deshacer la exportación de la factura: del trimestre activo o posterior, borrar su asiento; de uno cerrado
    /// (o un asiento del ERP), un contra-asiento (propietario 2026-11-08).
    /// </summary>
    public async Task<PlanDeshacerVenta> PlanDeshacerAsync(FacturaVenta f)
    {
        var estado = await EstadoAsync(f.IdFactura);
        if (estado.Vigente is not int vigente) return new PlanDeshacerVenta(ModoDeshacerVenta.NoExportada, null);
        var ejercicio = await sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio);
        if (ejercicio is null) return new PlanDeshacerVenta(ModoDeshacerVenta.NoExportada, null);
        var activo = await new ServicioTrimestreActivo(bd, _reloj).ObtenerAsync(ejercicio.Contexto.IdSociedadEjercicio);
        var cerrado = await LoteCerradoAsync(f, ejercicio.Contexto, estado, TrimestreContable.Activo(activo, ejercicio.Contexto.Ejercicio, _reloj.Ahora));
        return new PlanDeshacerVenta(cerrado ? ModoDeshacerVenta.ContraAsiento : ModoDeshacerVenta.BorrarAsiento, vigente);
    }

    /// <summary>Deshace la exportación contable y deja la factura sin asiento, lista para contabilizarla de nuevo.</summary>
    public async Task<Resultado<string>> DeshacerAsync(FacturaVenta f, Actor actor)
    {
        var registro = f.Rotulo;
        var estado = await EstadoAsync(f.IdFactura);
        if (estado.Vigente is not int vigente)
            return Resultado<string>.Fallo(new Error("Deshacer.NoExportada", "La factura no está exportada a contabilidad.", registro, null, "No hay nada que deshacer."));
        var ejercicio = await sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio);
        if (ejercicio is null) return Resultado<string>.Fallo(new Error("Contabilizacion.SinEjercicio", "No encuentro el ejercicio de la factura.", registro));
        var contexto = ejercicio.Contexto;
        var activo = TrimestreContable.Activo(await new ServicioTrimestreActivo(bd, _reloj).ObtenerAsync(contexto.IdSociedadEjercicio), contexto.Ejercicio, _reloj.Ahora);
        var cerrado = await LoteCerradoAsync(f, contexto, estado, activo);
        var generacion = cerrado ? estado.Generacion + 1 : estado.Generacion;
        var anulado = cerrado ? vigente : estado.Anulado;
        var lote = AsientoVenta.Lote(f.IdFactura, generacion);
        var motivo = $"Deshacer la exportación de la factura {f.NumeroFactura}";
        var texto = cerrado ? $"Exportación deshecha con contra-asiento del asiento {vigente}, de un trimestre cerrado" : $"Exportación deshecha: asiento {vigente} borrado";
        f.Anotar(_reloj.Ahora, actor.Nombre, texto);
        Task Marcar(ISesionDatos s) => s.EjecutarAsync(
            "UPDATE Facturas SET IdAsientoContable = NULL, IdAsientoAnulado = ?, GeneracionExportacion = ?, FechaExportacionContable = NULL, Historial = ? WHERE IdFactura = ?",
            anulado, generacion, f.Historial, f.IdFactura);
        if (anulado is int idAnulado)
        {
            // El contra-asiento lleva el registro de anulación del libro, en negativo y en el trimestre de su fecha.
            var contra = await ContraAsync(idAnulado, f, contexto, activo);
            if (!contra.EsCorrecto) return Resultado<string>.Fallo(contra.Errores);
            var ahora = _reloj.Ahora;
            var r = await motor.ReemplazarLoteAsync(contexto, lote, [contra.Valor.Asiento], actor, motivo, new TrabajoLote(
                Antes: BorrarLibroAsync,
                Despues: async (s, ids) =>
                {
                    if (!contra.Valor.Libro.Vacio)
                    {
                        var periodo = await PeriodoIvaAsync(s, contexto.IdSociedadEjercicio, contexto.Ejercicio, TrimestreContable.De(contra.Valor.Asiento.FechaContable));
                        await GrabarLibroAsync(s, f, contexto, ids[0], periodo, contra.Valor.Libro, true, actor, ahora, null);
                    }
                    await Marcar(s);
                    return Resultado.Correcto();
                }));
            if (!r.EsCorrecto) return Resultado<string>.Fallo(r.Errores);
        }
        else
        {
            // Trimestre activo o posterior: se borran el asiento y su registro del libro, sin rastro fiscal.
            var r = await motor.EliminarLoteAsync(contexto, lote, actor, motivo, new TrabajoLote(
                Antes: BorrarLibroAsync,
                Despues: async (s, _) => { await Marcar(s); return Resultado.Correcto(); }));
            if (!r.EsCorrecto) return Resultado<string>.Fallo(r.Errores);
        }
        f.IdAsientoContable = null;
        return Resultado<string>.Correcto(texto + ".");
    }

    private sealed record EstadoExportacion(int? Vigente, int? Anulado, int Generacion);

    private Task<EstadoExportacion> EstadoAsync(int idFactura) => bd.EjecutarAsync(async s =>
        await s.ConsultarPrimeroAsync("SELECT IdAsientoContable, IdAsientoAnulado, GeneracionExportacion FROM Facturas WHERE IdFactura = ?",
            r => new EstadoExportacion(r.EnteroNulo("IdAsientoContable"), r.EnteroNulo("IdAsientoAnulado"), r.EnteroNulo("GeneracionExportacion") ?? 0), idFactura)
        ?? new EstadoExportacion(null, null, 0));

    /// <summary>
    /// El lote vigente no se puede sustituir: alguno de sus asientos es de un trimestre anterior al activo, o el asiento
    /// vigente no es del lote (viene del ERP).
    /// </summary>
    private async Task<bool> LoteCerradoAsync(FacturaVenta f, ContextoSociedadEjercicio contexto, EstadoExportacion estado, int trimestreActivo)
    {
        var lote = AsientoVenta.Lote(f.IdFactura, estado.Generacion);
        var asientos = await bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdAsiento, FechaContable FROM Asientos WHERE EsAutomatico = True AND MotorPropietario = ? AND CodigoProceso = ? AND IdSociedadEjercicio = ? AND ClaveEjecucion = ?",
            r => (Id: r.Entero("IdAsiento"), Fecha: r.Fecha("FechaContable")), lote.MotorPropietario, lote.CodigoProceso, contexto.IdSociedadEjercicio, lote.ClaveEjecucion));
        if (estado.Vigente is int vigente && asientos.All(a => a.Id != vigente)) return true;
        return asientos.Any(a => TrimestreContable.De(a.Fecha) < trimestreActivo);
    }

    /// <summary>
    /// Contra-asiento con la fecha del original, o el día 1 del trimestre activo si es anterior, y su registro de anulación
    /// del libro: el del asiento anulado en negativo (vacío si el asiento no tiene registro).
    /// </summary>
    private async Task<Resultado<(Asiento Asiento, LibroVenta Libro)>> ContraAsync(int idAsiento, FacturaVenta f, ContextoSociedadEjercicio contexto, int trimestreActivo)
    {
        var original = await motor.ObtenerAsync(idAsiento);
        if (original is null)
            return Resultado<(Asiento, LibroVenta)>.Fallo(new Error("Contabilizacion.SinAsientoAnterior", $"No encuentro el asiento {idAsiento} de la factura.", f.Rotulo, null, "Revisa el asiento en Contabilidad."));
        var libro = await bd.EjecutarAsync(s => LeerLibroAsync(s, idAsiento, f));
        var contra = AsientoVenta.Contra(original, contexto, TrimestreContable.FechaAsiento(original.FechaContable, contexto.Ejercicio, trimestreActivo), f.NumeroFactura ?? f.Rotulo);
        return Resultado<(Asiento, LibroVenta)>.Correcto((contra, libro.Negado()));
    }

    // ---------- libro de IVA repercutido ----------

    /// <summary>Antes de borrar los asientos de un lote, se quitan sus registros del libro de IVA (los referencian).</summary>
    private static async Task BorrarLibroAsync(ISesionDatos s, IReadOnlyList<int> idsAsiento)
    {
        foreach (var id in idsAsiento)
            foreach (var r in await s.ConsultarAsync("SELECT IdIVARepercutido FROM IVARepercutido WHERE IdAsiento = ?", f => f.Entero("IdIVARepercutido"), id))
            {
                await s.EjecutarAsync("DELETE FROM DesglosesIVARepercutido WHERE IdIVARepercutido = ?", r);
                await s.EjecutarAsync("DELETE FROM IVARepercutido WHERE IdIVARepercutido = ?", r);
            }
    }

    /// <summary>Periodo de IVA del trimestre en el ejercicio; si aún no existe, se crea abierto (como en compras).</summary>
    internal static async Task<int> PeriodoIvaAsync(ISesionDatos s, int idSociedadEjercicio, int ejercicio, int trimestre)
    {
        var id = await s.EscalarAsync("SELECT IdPeriodoIVA FROM PeriodosIVA WHERE IdSociedadEjercicio = ? AND Numero = ?", idSociedadEjercicio, (byte)trimestre);
        if (id is not null and not DBNull) return Convert.ToInt32(id, System.Globalization.CultureInfo.InvariantCulture);
        var inicio = new DateTime(ejercicio, (trimestre - 1) * 3 + 1, 1);
        await s.EjecutarAsync("INSERT INTO PeriodosIVA (IdSociedadEjercicio, Numero, FechaInicio, FechaFin, Estado) VALUES (?, ?, ?, ?, ?)",
            idSociedadEjercicio, (byte)trimestre, inicio, inicio.AddMonths(3).AddDays(-1), "ABIERTO");
        return await s.UltimoIdAsync();
    }

    /// <summary>Código del catálogo técnico TiposOperacion para las ventas (el del importador de ContaSol).</summary>
    internal const string CodigoTipoRepercutido = "REPERCUTIDO_0";

    private static async Task GrabarLibroAsync(ISesionDatos s, FacturaVenta f, ContextoSociedadEjercicio contexto, int idAsiento, int idPeriodo, LibroVenta libro,
        bool anulacion, Actor actor, DateTime ahora, string? aviso)
    {
        var tipo = await s.EscalarAsync("SELECT IdTipoOperacion FROM TiposOperacion WHERE Codigo = ?", CodigoTipoRepercutido);
        if (tipo is null or DBNull) throw new InvalidOperationException($"Falta el tipo de operación «{CodigoTipoRepercutido}» en TiposOperacion.");
        var expedicion = (f.FechaFactura ?? ahora).Date;
        var descripcion = anulacion ? $"Anulación de la factura {f.NumeroFactura}"
            : aviso is null ? f.Descripcion : string.IsNullOrWhiteSpace(f.Descripcion) ? aviso : $"{f.Descripcion.Trim()} · {aviso}";
        await s.EjecutarAsync(
            "INSERT INTO IVARepercutido (IdSociedadEjercicio, IdCliente, IdAsiento, FechaExpedicion, FechaOperacion, FechaHoraRegistroContable, IdPeriodoIVA, NumeroFactura, " +
            "NombreCliente, IdentificacionFiscalCliente, PaisCliente, IdTipoOperacion, ImporteTotalFactura, BaseImponibleTotal, CuotaRepercutidaTotal, RecargoEquivalenciaTotal, " +
            "IncluidoModelo347, Estado, Descripcion, OperadorCreacion, FechaHoraCreacion, IdFactura) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            contexto.IdSociedadEjercicio, f.IdCliente, idAsiento, expedicion, (f.FechaOperacion ?? expedicion).Date, ahora, idPeriodo, Recortar(f.NumeroFactura, 100),
            Recortar(f.Cliente.NombreVisible, 150), Recortar(f.Cliente.Nif, 30), Recortar(f.Cliente.Pais, 3), Convert.ToInt32(tipo, System.Globalization.CultureInfo.InvariantCulture),
            libro.Total, libro.Base, libro.Cuota, libro.Recargo,
            // Modelo 347: las facturas sin retención, como en compras y en el ERP (las que la llevan se declaran en el 180 o el 190).
            f.TipoRetencion == 0m,
            anulacion ? "ANULACION" : "REGISTRADO", descripcion, actor.ToString(), ahora, f.IdFactura);
        var idLibro = await s.UltimoIdAsync();
        var orden = 1;
        foreach (var t in libro.Tramos)
        {
            var idCuenta = t.Cuenta is null ? null
                : await s.EscalarAsync("SELECT IdCuentaContable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", contexto.IdSociedadEjercicio, t.Cuenta);
            await s.EjecutarAsync(
                "INSERT INTO DesglosesIVARepercutido (IdIVARepercutido, Orden, ClaveRegimen, CalificacionOperacion, CausaExencion, BaseImponible, TipoIVA, CuotaRepercutida, " +
                "TipoRecargoEquivalencia, CuotaRecargoEquivalencia, IdCuentaIVARepercutido) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                idLibro, orden++, t.ClaveRegimen, t.Calificacion, t.CausaExencion, t.Base, t.TipoIva, t.Cuota, t.TipoRecargo, t.CuotaRecargo,
                idCuenta is null or DBNull ? null : idCuenta);
        }
    }

    /// <summary>
    /// Registro del libro de un asiento, para su anulación. Sin registro enlazado con el asiento (un asiento del ERP), el de
    /// la factura sin asiento: por IdFactura o, si no lo tiene, por número en el ejercicio. Vacío si no hay ninguno.
    /// </summary>
    private static async Task<LibroVenta> LeerLibroAsync(ISesionDatos s, int idAsiento, FacturaVenta f)
    {
        static (int Id, decimal Total) Cabecera(IFilaDatos r) => (r.Entero("IdIVARepercutido"), r.Decimal("ImporteTotalFactura"));
        var cabecera = await s.ConsultarPrimeroAsync("SELECT IdIVARepercutido, ImporteTotalFactura FROM IVARepercutido WHERE IdAsiento = ? AND Estado <> 'ANULACION'", Cabecera, idAsiento);
        if (cabecera.Id == 0)
            cabecera = await s.ConsultarPrimeroAsync("SELECT TOP 1 IdIVARepercutido, ImporteTotalFactura FROM IVARepercutido WHERE IdAsiento Is Null AND IdFactura = ? ORDER BY IdIVARepercutido",
                Cabecera, f.IdFactura);
        if (cabecera.Id == 0 && !string.IsNullOrWhiteSpace(f.NumeroFactura))
            cabecera = await s.ConsultarPrimeroAsync(
                "SELECT TOP 1 IdIVARepercutido, ImporteTotalFactura FROM IVARepercutido WHERE IdAsiento Is Null AND Trim(NumeroFactura) = ? AND IdSociedadEjercicio = ? ORDER BY IdIVARepercutido",
                Cabecera, f.NumeroFactura.Trim(), f.IdSociedadEjercicio);
        if (cabecera.Id == 0) return new LibroVenta(0m, []);
        var tramos = await s.ConsultarAsync(
            "SELECT d.ClaveRegimen, d.CalificacionOperacion, d.CausaExencion, d.TipoIVA, d.BaseImponible, d.CuotaRepercutida, d.TipoRecargoEquivalencia, d.CuotaRecargoEquivalencia, " +
            "c.Codigo FROM DesglosesIVARepercutido AS d LEFT JOIN CuentasContables AS c ON d.IdCuentaIVARepercutido = c.IdCuentaContable WHERE d.IdIVARepercutido = ? ORDER BY d.Orden",
            r => new TramoLibroVenta(r.Texto("ClaveRegimen")?.Trim() ?? "01", r.Texto("CalificacionOperacion")?.Trim() ?? "S1", r.Texto("CausaExencion")?.Trim(),
                r.Decimal("TipoIVA"), r.Decimal("BaseImponible"), r.Decimal("CuotaRepercutida"), r.EsNulo("TipoRecargoEquivalencia") ? 0m : r.Decimal("TipoRecargoEquivalencia"),
                r.EsNulo("CuotaRecargoEquivalencia") ? 0m : r.Decimal("CuotaRecargoEquivalencia"), r.Texto("Codigo")?.Trim()), cabecera.Id);
        return new LibroVenta(cabecera.Total, tramos);
    }

    private static string? Recortar(string? texto, int maximo) => texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];
}

/// <summary>Cómo se deshace la exportación contable de una factura de venta.</summary>
public enum ModoDeshacerVenta { NoExportada, BorrarAsiento, ContraAsiento }

/// <summary>Lo que hará deshacer la exportación, para avisar antes al operador.</summary>
public sealed record PlanDeshacerVenta(ModoDeshacerVenta Modo, int? IdAsiento)
{
    public string Explicacion => Modo switch
    {
        ModoDeshacerVenta.BorrarAsiento => $"se borrará el asiento {IdAsiento}",
        ModoDeshacerVenta.ContraAsiento => $"el asiento {IdAsiento} es de un trimestre cerrado y no se toca: se hará un contra-asiento en el trimestre activo",
        _ => "no hay asiento que deshacer"
    };
}
