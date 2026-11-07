using System.Globalization;
using System.Text;
using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Lectura y escritura de FacturasCompra, DesglosesFacturaCompra y ConfiguracionReconocimientoFacturasCompra dentro de una
/// sesión. Es interno: el resto del programa usa <see cref="ServicioFacturasCompra"/>.
/// </summary>
internal static class RepositorioFacturasCompra
{
    /// <summary>Registro que ya tiene ese PDF (mismo MD5), si lo hay.</summary>
    public static Task<(int IdRegistro, string? Archivo)> BuscarPorMd5Async(ISesionDatos s, string md5) =>
        s.ConsultarPrimeroAsync(
            "SELECT TOP 1 IdRegistro, ArchivoOriginal, Archivo FROM FacturasCompra WHERE MD5 = ? ORDER BY IdRegistro",
            f => (f.Entero("IdRegistro"), f.Texto("ArchivoOriginal") ?? f.Texto("Archivo")), md5);

    /// <summary>Todos los MD5 registrados, en minúsculas (el ERP los guardaba igual).</summary>
    public static async Task<HashSet<string>> Md5RegistradosAsync(ISesionDatos s) =>
        (await s.ConsultarAsync("SELECT DISTINCT MD5 FROM FacturasCompra WHERE MD5 Is Not Null", f => f.Texto("MD5")!.Trim().ToLowerInvariant()))
        .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Inserta la cabecera y sus desgloses. Debe llamarse dentro de una transacción: comprueba el MD5 en la misma
    /// transacción y, si el PDF ya está, no inserta nada.
    /// </summary>
    public static async Task<Resultado<int>> InsertarAsync(ISesionDatos s, FacturaCompra f, DateTime ahora)
    {
        if (!s.EnTransaccion) throw new InvalidOperationException("La factura de compra se inserta dentro de una transacción.");
        var (idExistente, archivo) = await BuscarPorMd5Async(s, f.Md5);
        if (idExistente != 0)
            return Resultado<int>.Fallo(new Error("Compras.Duplicada", "Este PDF ya está registrado; no creo otra factura de compra.", f.Rotulo,
                $"Sus bytes son idénticos a los de la factura {idExistente}{(string.IsNullOrWhiteSpace(archivo) ? "" : $" ({archivo})")}.",
                "Si es otro documento, comprueba que el PDF sea el correcto."));

        // Una factura nueva trae la forma de pago y el producto del proveedor; sin producto, bienes (compras/pantallas 2026-11-06 13:36).
        f.IdFormaPago ??= await FormaPagoDefectoAsync(s, f);
        f.Producto ??= await ProductoDefectoAsync(s, f) ?? ProductoCompra.Bienes;
        await s.EjecutarAsync(
            "INSERT INTO FacturasCompra (Sociedad, Ejercicio, IdSociedadEjercicio, IdProveedor, CuentaContableProveedor, NIF, NombreProveedorAeat, " +
            "NumeroFactura, Fecha, Concepto, ImporteBI, ImporteIVA, ImporteTotal, PorcentajeRetencion, ImporteRetencion, InversionSujetoPasivo, " +
            "Divisa, ImporteTotalDivisa, TipoCambioAplicado, FechaTipoCambio, IdRegistroFacturaRectificada, ArchivoOriginal, Carpeta, NombreFichero, MD5, " +
            "Pagada, Exportada, Duplicada, FechaHoraRegistro, ConfianzaImportacion, RequiereRevisionImportacion, FuenteImportacion, LOGImportacion, Version, " +
            "TipoOperacion, TipoProducto, TipoRetencion, Suplido, ConceptoGasto, IdCentroCoste, CuentaFormaPago, NotasFactura, IdFormaPago) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            f.Ejercicio.IdSociedad, f.Ejercicio.Ejercicio.ToString(CultureInfo.InvariantCulture), f.Ejercicio.IdSociedadEjercicio, f.IdProveedor,
            Nulo(f.CuentaContableProveedor), Nulo(f.NifProveedor), Nulo(f.NombreProveedorAeat),
            Nulo(f.NumeroFactura), f.Fecha?.Date, Nulo(f.Concepto), f.BaseImponible, f.CuotaIva, f.ImporteTotal,
            f.PorcentajeRetencion is { } pr ? (float)pr : null, f.ImporteRetencion, f.InversionSujetoPasivo,
            f.Divisa, f.ImporteTotalDivisa, f.TipoCambioAplicado is { } tc ? (double)tc : null, f.FechaTipoCambio?.Date, f.IdRegistroFacturaRectificada,
            f.RutaPdf, Nulo(f.Carpeta), f.NombreFichero, f.Md5,
            false, false, false, ahora, f.Confianza, f.RequiereRevision, f.Fuente, Nulo(f.Log), 0,
            (int)f.Operacion, f.Producto is { } p ? (int)p : null, f.ClaseRetencion is { } cr ? (byte)cr : null, f.Suplido,
            Nulo(f.CuentaGasto), f.IdCentroCoste, Nulo(f.CuentaFormaPago), Nulo(f.Notas), f.IdFormaPago);
        var id = await s.UltimoIdAsync();

        foreach (var d in f.Desgloses)
            await s.EjecutarAsync(
                "INSERT INTO DesglosesFacturaCompra (IdRegistro, Orden, TipoIVA, BaseImponible, CuotaIVA, Confianza, Evidencia) VALUES (?, ?, ?, ?, ?, ?, ?)",
                id, d.Orden, d.TipoIva, d.BaseImponible, d.CuotaIva, d.Confianza, Recortar(d.Evidencia, 255));
        return Resultado<int>.Correcto(id);
    }

    /// <summary>
    /// Guarda los datos editables de una factura sin contabilizar, con control de versión: si otro operador la cambió
    /// después de leerla, no se pisa. Los desgloses se sustituyen enteros. Debe llamarse dentro de una transacción.
    /// </summary>
    /// <remarks><paramref name="permitirExportada"/>: al re-exportar, los cambios se guardan en la misma transacción que el asiento nuevo.</remarks>
    public static async Task<Resultado<int>> ActualizarAsync(ISesionDatos s, FacturaCompra f, bool permitirExportada = false)
    {
        if (!s.EnTransaccion) throw new InvalidOperationException("La factura de compra se guarda dentro de una transacción.");
        var estado = await s.ConsultarPrimeroAsync("SELECT Exportada, Version FROM FacturasCompra WHERE IdRegistro = ?",
            r => (Existe: true, Exportada: r.Logico("Exportada"), Version: r.EnteroNulo("Version") ?? 0), f.IdRegistro);
        if (!estado.Existe)
            return Resultado<int>.Fallo(new Error("Compras.NoExiste", "La factura de compra ya no existe.", f.Rotulo, "Otro operador la ha borrado.", "Actualiza la lista."));
        // Una contabilizada se guarda sin re-exportar solo si el cambio no toca el asiento (notas, forma de pago).
        if (estado.Exportada && !permitirExportada && await CargarAsync(s, f.IdRegistro) is { } guardada && FacturaCompra.AfectaAlAsiento(guardada, f))
            return Resultado<int>.Fallo(new Error("Compras.Contabilizada", "Este cambio afecta al asiento de la factura, que ya está contabilizada.", f.Rotulo, null,
                "Usa «Guardar y re-exportar», que actualiza el asiento (o hace contra-asiento si es de un trimestre cerrado)."));
        if (estado.Version != f.Version)
            return Resultado<int>.Fallo(new Error("Compras.Concurrencia", "Otro operador ha cambiado esta factura mientras la tenías abierta.", f.Rotulo,
                "Tus cambios no se han guardado para no pisar los suyos.", "Actualiza la lista y vuelve a hacer tus cambios."));

        var nueva = f.Version + 1;
        await s.EjecutarAsync(
            "UPDATE FacturasCompra SET IdProveedor = ?, CuentaContableProveedor = ?, NIF = ?, NumeroFactura = ?, Fecha = ?, Concepto = ?, " +
            "ImporteBI = ?, ImporteIVA = ?, ImporteTotal = ?, PorcentajeRetencion = ?, ImporteRetencion = ?, InversionSujetoPasivo = ?, " +
            "Divisa = ?, ImporteTotalDivisa = ?, TipoCambioAplicado = ?, FechaTipoCambio = ?, IdRegistroFacturaRectificada = ?, " +
            "TipoOperacion = ?, TipoProducto = ?, TipoRetencion = ?, Suplido = ?, ConceptoGasto = ?, IdCentroCoste = ?, CuentaFormaPago = ?, IdFormaPago = ?, " +
            "NotasFactura = ?, RequiereRevisionImportacion = ?, ConfianzaImportacion = ?, FuenteImportacion = ?, LOGImportacion = ?, Version = ? WHERE IdRegistro = ? AND Version = ?",
            f.IdProveedor, Nulo(f.CuentaContableProveedor), Nulo(f.NifProveedor), Nulo(f.NumeroFactura), f.Fecha?.Date, Nulo(f.Concepto),
            f.BaseImponible, f.CuotaIva, f.ImporteTotal, f.PorcentajeRetencion is { } pr ? (float)pr : null, f.ImporteRetencion, f.InversionSujetoPasivo,
            f.Divisa, f.ImporteTotalDivisa, f.TipoCambioAplicado is { } tc ? (double)tc : null, f.FechaTipoCambio?.Date, f.IdRegistroFacturaRectificada,
            (int)f.Operacion, f.Producto is { } p ? (int)p : null, f.ClaseRetencion is { } cr ? (byte)cr : null, f.Suplido,
            Nulo(f.CuentaGasto), f.IdCentroCoste, Nulo(f.CuentaFormaPago), f.IdFormaPago, Nulo(f.Notas), f.RequiereRevision, f.Confianza, Nulo(f.Fuente), Nulo(f.Log), nueva, f.IdRegistro, f.Version);
        // La forma de pago elegida pasa a ser la del proveedor por defecto (compras/pantallas 2026-11-06).
        if (f.IdFormaPago is { } fp && f.IdProveedor is { } prov)
            await s.EjecutarAsync("UPDATE CuentasProveedoresSociedad SET IdFormaPagoDefecto = ? WHERE IdProveedor = ? AND IdSociedad = ?", fp, prov, f.Ejercicio.IdSociedad);
        // Igual el producto (compras/pantallas 2026-11-06 13:36).
        if (f.Producto is { } producto && f.IdProveedor is { } prov2)
            await s.EjecutarAsync("UPDATE CuentasProveedoresSociedad SET TipoProductoDefecto = ? WHERE IdProveedor = ? AND IdSociedad = ?", (byte)producto, prov2, f.Ejercicio.IdSociedad);
        // Y la cuenta de gasto (por su identidad) y el centro de coste (compras/pantallas 2026-11-06 17:02).
        if (f.IdProveedor is { } prov3)
        {
            if (Nulo(f.CuentaGasto) is { } gasto && await s.EscalarAsync("SELECT IdIdentidadContable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?",
                    f.Ejercicio.IdSociedadEjercicio, gasto) is { } identidad and not DBNull)
                await s.EjecutarAsync("UPDATE CuentasProveedoresSociedad SET IdIdentidadGastoDefecto = ? WHERE IdProveedor = ? AND IdSociedad = ?",
                    Convert.ToInt32(identidad, CultureInfo.InvariantCulture), prov3, f.Ejercicio.IdSociedad);
            if (f.IdCentroCoste is { } centro)
                await s.EjecutarAsync("UPDATE CuentasProveedoresSociedad SET IdCentroCosteDefecto = ? WHERE IdProveedor = ? AND IdSociedad = ?", centro, prov3, f.Ejercicio.IdSociedad);
        }
        await s.EjecutarAsync("DELETE FROM DesglosesFacturaCompra WHERE IdRegistro = ?", f.IdRegistro);
        foreach (var d in f.Desgloses)
            await s.EjecutarAsync(
                "INSERT INTO DesglosesFacturaCompra (IdRegistro, Orden, TipoIVA, BaseImponible, CuotaIVA, Confianza, Evidencia) VALUES (?, ?, ?, ?, ?, ?, ?)",
                f.IdRegistro, d.Orden, d.TipoIva, d.BaseImponible, d.CuotaIva, d.Confianza, Recortar(d.Evidencia, 255));
        return Resultado<int>.Correcto(nueva);
    }

    /// <summary>
    /// Asiento de una factura contabilizada en el ERP (compras/pantallas 2026-11-05 20:29). ASIAPU no es el IdAsiento: el
    /// enlace fiable es el apunte del proveedor con el número de la factura y con Documento (la ruta del PDF), que solo
    /// lleva el apunte de la factura, no el del pago.
    /// </summary>
    public static async Task<AsientoFactura?> AsientoErpAsync(ISesionDatos s, int idSociedadEjercicio, string numeroFactura, string cuentaProveedor)
    {
        var cabecera = await s.ConsultarPrimeroAsync(
            "SELECT TOP 1 a.IdAsiento, a.FechaContable, a.Descripcion FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND ap.NumeroDocumento = ? AND ap.CuentaContable = ? AND ap.Documento Is Not Null AND ap.Documento <> '' ORDER BY a.IdAsiento",
            r => (Id: r.Entero("IdAsiento"), Fecha: r.FechaNula("FechaContable"), Descripcion: r.Texto("Descripcion") ?? ""),
            idSociedadEjercicio, numeroFactura, cuentaProveedor);
        if (cabecera.Id == 0) return null;
        return await AsientoAsync(s, idSociedadEjercicio, cabecera.Id, cabecera.Fecha, cabecera.Descripcion);
    }

    /// <summary>Asiento exportado por ProContaVF (FacturasCompra.IdAsientoContable).</summary>
    public static async Task<AsientoFactura?> AsientoPorIdAsync(ISesionDatos s, int idSociedadEjercicio, int idAsiento)
    {
        var cabecera = await s.ConsultarPrimeroAsync("SELECT IdAsiento, FechaContable, Descripcion FROM Asientos WHERE IdAsiento = ?",
            r => (Id: r.Entero("IdAsiento"), Fecha: r.FechaNula("FechaContable"), Descripcion: r.Texto("Descripcion") ?? ""), idAsiento);
        return cabecera.Id == 0 ? null : await AsientoAsync(s, idSociedadEjercicio, cabecera.Id, cabecera.Fecha, cabecera.Descripcion);
    }

    private static async Task<AsientoFactura> AsientoAsync(ISesionDatos s, int idSociedadEjercicio, int idAsiento, DateTime? fechaContable, string descripcion)
    {
        var cabecera = (Id: idAsiento, Fecha: fechaContable, Descripcion: descripcion);
        // Sin parámetros en el ON: Access los recibe en otro orden y devolvía los apuntes de otro asiento.
        var leidos = await s.ConsultarAsync(
            "SELECT Orden, CuentaContable, ImporteDebe, ImporteHaber, Descripcion FROM Apuntes WHERE IdAsiento = ? ORDER BY Orden",
            r => (Cuenta: r.Texto("CuentaContable")?.Trim() ?? "", Debe: r.DecimalNulo("ImporteDebe") ?? 0m, Haber: r.DecimalNulo("ImporteHaber") ?? 0m,
                Descripcion: r.Texto("Descripcion")?.Trim()),
            cabecera.Id);
        var nombres = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var cuenta in leidos.Select(x => x.Cuenta).Where(x => x.Length > 0).Distinct())
            nombres[cuenta] = (await s.EscalarAsync("SELECT TOP 1 Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?",
                idSociedadEjercicio, cuenta) as string)?.Trim();
        var apuntes = leidos.Select(x => new ApunteFactura(x.Cuenta, nombres.GetValueOrDefault(x.Cuenta), x.Debe, x.Haber, x.Descripcion)).ToList();
        // Antes de 1901 es la fecha vacía del ERP.
        var fecha = cabecera.Fecha is { Year: > 1900 } f ? f : (DateTime?)null;
        return new AsientoFactura(cabecera.Id, fecha, cabecera.Descripcion, apuntes);
    }

    /// <summary>
    /// La factura pasa a otro PDF (la primera parte al separar facturas): sus datos y su reconocimiento se sustituyen por los
    /// de la nueva lectura, con el mismo control de versión que al guardar. Debe llamarse dentro de una transacción.
    /// </summary>
    public static async Task<Resultado<int>> ReemplazarPdfAsync(ISesionDatos s, FacturaCompra f)
    {
        var r = await ActualizarAsync(s, f);
        if (!r.EsCorrecto) return r;
        await s.EjecutarAsync(
            "UPDATE FacturasCompra SET MD5 = ?, NombreFichero = ?, ConfianzaImportacion = ?, FuenteImportacion = ?, LOGImportacion = ? WHERE IdRegistro = ?",
            f.Md5, f.NombreFichero, f.Confianza, f.Fuente, Nulo(f.Log), f.IdRegistro);
        return r;
    }

    /// <summary>
    /// Facturas de compra del ejercicio de la empresa para la lista de la pantalla, con el nombre del proveedor. Las del ERP
    /// sin IdSociedadEjercicio se reconocen por su sociedad y su año.
    /// </summary>
    public static async Task<IReadOnlyList<FilaFacturaCompra>> ListarAsync(ISesionDatos s, ContextoSociedadEjercicio ctx)
    {
        var filas = await s.ConsultarAsync(
            "SELECT fc.IdRegistro, fc.IdProveedor, fc.NIF, fc.NumeroFactura, fc.Fecha, fc.ImporteBI, fc.ImporteIVA, fc.ImporteTotal, fc.ImporteRetencion, " +
            "fc.PorcentajeRetencion, fc.Suplido, fc.InversionSujetoPasivo, fc.TipoOperacion, fc.Divisa, fc.IdRegistroFacturaRectificada, fc.Exportada, " +
            "fc.Duplicada, fc.ArchivoOriginal, fc.Archivo, fc.Carpeta, fc.ConceptoGasto, fc.IdCentroCoste, fc.ConfianzaImportacion, " +
            "fc.RequiereRevisionImportacion, fc.FechaHoraRegistro, fc.ASIAPU, fc.IdAsientoContable, fc.LOGImportacion, fc.Version, t.Nombre AS NombreTercero, t.RazonSocial, t.NombreComercial, fc.NombreProveedorAeat " +
            "FROM (FacturasCompra AS fc LEFT JOIN Proveedores AS p ON fc.IdProveedor = p.IdProveedor) LEFT JOIN Terceros AS t ON p.IdTercero = t.IdTercero " +
            "WHERE fc.IdSociedadEjercicio = ? OR (fc.IdSociedadEjercicio Is Null AND fc.Sociedad = ? AND fc.Ejercicio = ?)",
            r => new FilaFacturaCompra
            {
                IdRegistro = r.Entero("IdRegistro"),
                IdProveedor = r.EnteroNulo("IdProveedor"),
                Proveedor = Primero(r.Texto("NombreComercial"), r.Texto("RazonSocial"), r.Texto("NombreTercero"), r.Texto("NombreProveedorAeat")),
                Nif = r.Texto("NIF")?.Trim(),
                NumeroFactura = r.Texto("NumeroFactura")?.Trim() ?? "",
                Fecha = r.FechaNula("Fecha"),
                BaseImponible = r.DecimalNulo("ImporteBI"),
                CuotaIva = r.DecimalNulo("ImporteIVA"),
                ImporteTotal = r.DecimalNulo("ImporteTotal"),
                ImporteRetencion = r.DecimalNulo("ImporteRetencion"),
                TieneRetencion = (r.DecimalNulo("ImporteRetencion") ?? 0m) != 0m || (!r.EsNulo("PorcentajeRetencion") && r.Doble("PorcentajeRetencion") != 0d),
                Suplido = r.DecimalNulo("Suplido"),
                InversionSujetoPasivo = r.Logico("InversionSujetoPasivo"),
                Operacion = r.EnteroNulo("TipoOperacion") is int op && Enum.IsDefined((OperacionCompra)op) ? (OperacionCompra)op : OperacionCompra.Nacional,
                Divisa = r.Texto("Divisa")?.Trim() is { Length: 3 } d ? d : "EUR",
                IdRegistroFacturaRectificada = r.EnteroNulo("IdRegistroFacturaRectificada"),
                Contabilizada = r.Logico("Exportada"),
                Duplicada = r.Logico("Duplicada"),
                RutaPdf = r.Texto("ArchivoOriginal") ?? r.Texto("Archivo") ?? "",
                RutasGuardadas = Rutas(r),
                Carpeta = r.Texto("Carpeta")?.Trim() ?? "",
                CuentaGasto = r.Texto("ConceptoGasto")?.Trim(),
                IdCentroCoste = r.EnteroNulo("IdCentroCoste"),
                Confianza = r.DecimalNulo("ConfianzaImportacion"),
                RequiereRevision = !r.EsNulo("RequiereRevisionImportacion") && r.Logico("RequiereRevisionImportacion"),
                FechaHoraRegistro = r.FechaNula("FechaHoraRegistro"),
                Asiento = r.Logico("Exportada") ? r.EnteroNulo("IdAsientoContable") ?? r.EnteroNulo("ASIAPU") : null,
                ConfianzaMinima = ConfianzaMinima(r.Texto("LOGImportacion")),
                Version = r.EnteroNulo("Version") ?? 0
            },
            ctx.IdSociedadEjercicio, ctx.IdSociedad, ctx.Ejercicio.ToString(CultureInfo.InvariantCulture));
        return filas;
    }

    /// <summary>
    /// Proveedores de la empresa con su tercero, su nombre y su forma de pago por defecto, para buscar por NIF, abrir la
    /// ficha y proponer la forma de pago.
    /// </summary>
    public static async Task<IReadOnlyList<ProveedorFicha>> ProveedoresFichaAsync(ISesionDatos s, string idSociedad)
    {
        var filas = await s.ConsultarAsync(
            "SELECT p.IdProveedor, p.IdTercero, t.NIF, t.Nombre, t.RazonSocial, t.NombreComercial, cps.IdFormaPagoDefecto, cps.TipoProductoDefecto FROM (CuentasProveedoresSociedad AS cps " +
            "INNER JOIN Proveedores AS p ON cps.IdProveedor = p.IdProveedor) INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero WHERE cps.IdSociedad = ?",
            r => new ProveedorFicha(r.Entero("IdProveedor"), r.Entero("IdTercero"), r.Texto("NIF")?.Trim() ?? "",
                Primero(r.Texto("NombreComercial"), r.Texto("RazonSocial"), r.Texto("Nombre")),
                r.EnteroNulo("IdFormaPagoDefecto") is int fp ? (byte)fp : null,
                r.EnteroNulo("TipoProductoDefecto") is int tp && Enum.IsDefined((ProductoCompra)tp) ? (ProductoCompra)tp : null), idSociedad);
        // Un proveedor puede tener varias cuentas en la empresa: uno por proveedor, con la forma de pago que tenga alguna.
        return filas.GroupBy(p => p.IdProveedor)
            .Select(g => g.First() with
            {
                IdFormaPagoDefecto = g.Select(p => p.IdFormaPagoDefecto).FirstOrDefault(x => x is not null),
                ProductoDefecto = g.Select(p => p.ProductoDefecto).FirstOrDefault(x => x is not null)
            }).ToList();
    }

    /// <summary>La confianza más baja de NIF, número, fecha e importes según el log del reconocimiento; null sin log.</summary>
    internal static decimal? ConfianzaMinima(string? log)
    {
        var c = ConfianzaCampos.DesdeLog(log);
        var valores = new[] { c.Nif, c.Numero, c.Fecha, c.Importes }.Where(v => v is not null).Select(v => v!.Value).ToList();
        return valores.Count == 0 ? null : valores.Min();
    }

    /// <summary>Rutas del PDF guardadas en la fila (ArchivoOriginal y Archivo), sin vacías ni repetidas.</summary>
    private static IReadOnlyList<string> Rutas(IFilaDatos r) =>
        new[] { r.Texto("ArchivoOriginal")?.Trim(), r.Texto("Archivo")?.Trim() }
            .Where(x => !string.IsNullOrEmpty(x)).Select(x => x!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static string Primero(params string?[] textos) => textos.FirstOrDefault(t => !string.IsNullOrWhiteSpace(t))?.Trim() ?? "";

    /// <summary>La forma de pago por defecto del proveedor en la empresa, para una factura nueva suya.</summary>
    private static async Task<byte?> FormaPagoDefectoAsync(ISesionDatos s, FacturaCompra f)
    {
        if (f.IdProveedor is not { } prov) return null;
        var v = await s.EscalarAsync("SELECT TOP 1 IdFormaPagoDefecto FROM CuentasProveedoresSociedad WHERE IdProveedor = ? AND IdSociedad = ? AND IdFormaPagoDefecto Is Not Null",
            prov, f.Ejercicio.IdSociedad);
        return v is null or DBNull ? null : Convert.ToByte(v, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Cuenta de gasto (código en el ejercicio de la factura) y centro de coste por defecto del proveedor en la empresa. La
    /// cuenta que no existe en el ejercicio o no admite apuntes, y el centro inactivo o no imputable, no se proponen.
    /// </summary>
    public static async Task<(string? Cuenta, int? IdCentroCoste)> ImputacionDefectoAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, int? idProveedor)
    {
        if (idProveedor is not { } prov) return (null, null);
        var filas = await s.ConsultarAsync("SELECT IdIdentidadGastoDefecto, IdCentroCosteDefecto FROM CuentasProveedoresSociedad WHERE IdProveedor = ? AND IdSociedad = ?",
            r => (Identidad: r.EnteroNulo("IdIdentidadGastoDefecto"), Centro: r.EnteroNulo("IdCentroCosteDefecto")), prov, ctx.IdSociedad);
        string? cuenta = null;
        if (filas.Select(f => f.Identidad).FirstOrDefault(x => x is not null) is int identidad)
            cuenta = await s.ConsultarPrimeroAsync("SELECT Codigo FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ? AND EsImputable = True",
                r => r.Texto("Codigo")?.Trim(), ctx.IdSociedadEjercicio, identidad);
        int? centro = filas.Select(f => f.Centro).FirstOrDefault(x => x is not null) is int c && await CentroImputableAsync(s, ctx.IdSociedad, c) ? c : null;
        return (cuenta, centro);
    }

    /// <summary>Centro de coste por defecto de una subcuenta del ejercicio (CuentasContables.IdCentroCostePredeterminado), si es imputable.</summary>
    public static async Task<int?> CentroDeCuentaAsync(ISesionDatos s, ContextoSociedadEjercicio ctx, string cuenta)
    {
        var v = await s.EscalarAsync("SELECT IdCentroCostePredeterminado FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", ctx.IdSociedadEjercicio, cuenta);
        if (v is null or DBNull) return null;
        var c = Convert.ToInt32(v, CultureInfo.InvariantCulture);
        return await CentroImputableAsync(s, ctx.IdSociedad, c) ? c : null;
    }

    /// <summary>El centro es de la empresa, está activo y admite apuntes.</summary>
    public static async Task<bool> CentroImputableAsync(ISesionDatos s, string idSociedad, int idCentroCoste)
    {
        var c = await s.ConsultarPrimeroAsync("SELECT IdSociedad, EstaActivo, EsImputable FROM CentrosCoste WHERE IdCentroCoste = ?",
            r => (Sociedad: r.Texto("IdSociedad")?.Trim(), Activo: r.Logico("EstaActivo"), Imputable: r.Logico("EsImputable")), idCentroCoste);
        return c.Sociedad == idSociedad && c.Activo && c.Imputable;
    }

    /// <summary>El producto por defecto del proveedor en la empresa, para una factura nueva suya.</summary>
    private static async Task<ProductoCompra?> ProductoDefectoAsync(ISesionDatos s, FacturaCompra f)
    {
        if (f.IdProveedor is not { } prov) return null;
        var v = await s.EscalarAsync("SELECT TOP 1 TipoProductoDefecto FROM CuentasProveedoresSociedad WHERE IdProveedor = ? AND IdSociedad = ? AND TipoProductoDefecto Is Not Null",
            prov, f.Ejercicio.IdSociedad);
        return v is null or DBNull || !Enum.IsDefined((ProductoCompra)Convert.ToInt32(v, CultureInfo.InvariantCulture)) ? null : (ProductoCompra)Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    public static async Task<FacturaCompra?> CargarAsync(ISesionDatos s, int idRegistro)
    {
        var respaldo = await EjercicioDeRegistroAsync(s, idRegistro);
        var f = await s.ConsultarPrimeroAsync(
            "SELECT fc.*, se.IdSociedad AS SociedadEjercicio, se.Ejercicio AS AnioEjercicio FROM FacturasCompra AS fc " +
            "LEFT JOIN SociedadesEjercicios AS se ON fc.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE fc.IdRegistro = ?", r => Mapear(r, respaldo), idRegistro);
        if (f is null) return null;
        f.Desgloses.AddRange(await s.ConsultarAsync(
            "SELECT Orden, TipoIVA, BaseImponible, CuotaIVA, Confianza, Evidencia FROM DesglosesFacturaCompra WHERE IdRegistro = ? ORDER BY Orden",
            r => new DesgloseFacturaCompra((short)r.Entero("Orden"), r.Decimal("TipoIVA"), r.Decimal("BaseImponible"), r.Decimal("CuotaIVA"),
                r.DecimalNulo("Confianza"), r.Texto("Evidencia")), idRegistro));
        return f;
    }

    /// <summary>
    /// IdSociedadEjercicio de una sociedad y un año. Las facturas del ERP no lo tienen grabado (solo Sociedad y Ejercicio).
    /// </summary>
    public static async Task<int?> IdEjercicioAsync(ISesionDatos s, string? idSociedad, int ejercicio)
    {
        if (string.IsNullOrWhiteSpace(idSociedad) || ejercicio == 0) return null;
        var v = await s.EscalarAsync("SELECT IdSociedadEjercicio FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?",
            idSociedad.Trim(), ejercicio.ToString(CultureInfo.InvariantCulture));
        return v is null or DBNull ? null : Convert.ToInt32(v, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// IdSociedadEjercicio de una factura: el grabado o, en las del ERP que no lo tienen, el de su sociedad y su año.
    /// </summary>
    public static async Task<int> EjercicioDeRegistroAsync(ISesionDatos s, int idRegistro)
    {
        var fila = await s.ConsultarPrimeroAsync("SELECT IdSociedadEjercicio, Sociedad, Ejercicio FROM FacturasCompra WHERE IdRegistro = ?",
            r => (Id: r.EnteroNulo("IdSociedadEjercicio"), Sociedad: r.Texto("Sociedad"), Anio: r.Texto("Ejercicio")), idRegistro);
        if (fila.Id is int id) return id;
        return int.TryParse(fila.Anio, NumberStyles.Integer, CultureInfo.InvariantCulture, out var anio)
            && await IdEjercicioAsync(s, fila.Sociedad, anio) is int resuelto ? resuelto : 0;
    }

    private static FacturaCompra Mapear(IFilaDatos r, int respaldo)
    {
        var idSe = r.EnteroNulo("IdSociedadEjercicio") ?? respaldo;
        var sociedad = r.Texto("SociedadEjercicio") ?? r.Texto("Sociedad") ?? string.Empty;
        var anio = int.TryParse(r.Texto("AnioEjercicio") ?? r.Texto("Ejercicio"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? a : 0;
        return new FacturaCompra
        {
            IdRegistro = r.Entero("IdRegistro"),
            Ejercicio = new ContextoSociedadEjercicio(sociedad.Trim(), idSe, anio),
            IdProveedor = r.EnteroNulo("IdProveedor"),
            CuentaContableProveedor = r.Texto("CuentaContableProveedor"),
            NifProveedor = r.Texto("NIF"),
            NombreProveedorAeat = r.Texto("NombreProveedorAeat"),
            NumeroFactura = r.Texto("NumeroFactura") ?? string.Empty,
            Fecha = r.FechaNula("Fecha"),
            Concepto = r.Texto("Concepto") ?? string.Empty,
            BaseImponible = r.DecimalNulo("ImporteBI"),
            CuotaIva = r.DecimalNulo("ImporteIVA"),
            ImporteTotal = r.DecimalNulo("ImporteTotal"),
            PorcentajeRetencion = r.EsNulo("PorcentajeRetencion") ? null : Math.Round((decimal)r.Doble("PorcentajeRetencion"), 4),
            ImporteRetencion = r.DecimalNulo("ImporteRetencion"),
            InversionSujetoPasivo = r.Logico("InversionSujetoPasivo"),
            Divisa = r.Texto("Divisa")?.Trim() is { Length: 3 } d ? d : "EUR",
            ImporteTotalDivisa = r.DecimalNulo("ImporteTotalDivisa"),
            TipoCambioAplicado = r.DecimalNulo("TipoCambioAplicado"),
            FechaTipoCambio = r.FechaNula("FechaTipoCambio"),
            IdRegistroFacturaRectificada = r.EnteroNulo("IdRegistroFacturaRectificada"),
            RutaPdf = r.Texto("ArchivoOriginal") ?? r.Texto("Archivo") ?? string.Empty,
            RutasGuardadas = Rutas(r),
            Carpeta = r.Texto("Carpeta") ?? string.Empty,
            Md5 = r.Texto("MD5")?.Trim().ToLowerInvariant() ?? string.Empty,
            Confianza = r.DecimalNulo("ConfianzaImportacion") ?? 0m,
            RequiereRevision = !r.EsNulo("RequiereRevisionImportacion") && r.Logico("RequiereRevisionImportacion"),
            Fuente = r.Texto("FuenteImportacion") ?? string.Empty,
            Log = r.Texto("LOGImportacion") ?? string.Empty,
            FechaHoraRegistro = r.FechaNula("FechaHoraRegistro") ?? DateTime.MinValue,
            Version = r.EnteroNulo("Version") ?? 0,
            Operacion = r.EnteroNulo("TipoOperacion") is int op && Enum.IsDefined((OperacionCompra)op) ? (OperacionCompra)op : OperacionCompra.Nacional,
            Producto = r.EnteroNulo("TipoProducto") is int tp && Enum.IsDefined((ProductoCompra)tp) ? (ProductoCompra)tp : null,
            ClaseRetencion = r.EnteroNulo("TipoRetencion") is int cr && Enum.IsDefined((ClaseRetencion)cr) ? (ClaseRetencion)cr : null,
            Suplido = r.DecimalNulo("Suplido"),
            CuentaGasto = r.Texto("ConceptoGasto")?.Trim(),
            IdCentroCoste = r.EnteroNulo("IdCentroCoste"),
            CuentaFormaPago = r.Texto("CuentaFormaPago")?.Trim(),
            IdFormaPago = r.EsNulo("IdFormaPago") ? null : (byte)r.Entero("IdFormaPago"),
            Notas = r.Texto("NotasFactura"),
            Contabilizada = r.Logico("Exportada"),
            IdAsientoContable = r.EnteroNulo("IdAsientoContable"),
            Duplicada = r.Logico("Duplicada")
        };
    }

    /// <summary>Configuración del reconocimiento de la empresa; sin fila, la predeterminada (activo, sin OCR ni VIES).</summary>
    public static async Task<ConfiguracionReconocimiento> ConfiguracionAsync(ISesionDatos s, string idSociedad) =>
        await s.ConsultarPrimeroAsync(
            "SELECT Activo, SociedadTieneVies, UsarTextract, UmbralAceptacion, UmbralDiscrepanciaImporte FROM ConfiguracionReconocimientoFacturasCompra WHERE IdSociedad = ?",
            r => new ConfiguracionReconocimiento
            {
                Activo = r.Logico("Activo"),
                SociedadTieneVies = r.Logico("SociedadTieneVies"),
                UsarOcr = r.Logico("UsarTextract"),
                UmbralAceptacion = r.Decimal("UmbralAceptacion"),
                UmbralDiscrepanciaImporte = r.Decimal("UmbralDiscrepanciaImporte")
            }, idSociedad) ?? new ConfiguracionReconocimiento();

    /// <summary>Carpeta de documentos de la empresa (Sociedades.RutaDocumentos) y su nombre.</summary>
    public static Task<(string Nombre, string? Raiz)> EmpresaAsync(ISesionDatos s, string idSociedad) =>
        s.ConsultarPrimeroAsync("SELECT Nombre, RutaDocumentos FROM Sociedades WHERE IdSociedad = ?",
            r => (r.Texto("Nombre") ?? idSociedad, r.Texto("RutaDocumentos")), idSociedad);

    /// <summary>NIF de todas las empresas, para no tomar nunca el del receptor como el del proveedor.</summary>
    public static async Task<IReadOnlyList<string>> NifsEmpresasAsync(ISesionDatos s) =>
        (await s.ConsultarAsync("SELECT CIF FROM Sociedades WHERE CIF Is Not Null", r => r.Texto("CIF")?.Trim() ?? ""))
        .Where(n => n.Length > 0).ToList();

    /// <summary>
    /// Proveedores de la empresa con su NIF del catálogo de terceros y los códigos de cuenta que han tenido en sus ejercicios
    /// (CuentasProveedoresSociedad → Proveedores → Terceros, y la identidad contable → CuentasContables).
    /// </summary>
    public static async Task<IReadOnlyList<ProveedorEmpresa>> ProveedoresAsync(ISesionDatos s, string idSociedad, int idSociedadEjercicio)
    {
        var filas = await s.ConsultarAsync(
            "SELECT cps.IdProveedor, cps.Clase, cps.IdIdentidadContable, t.NIF FROM (CuentasProveedoresSociedad AS cps " +
            "INNER JOIN Proveedores AS p ON cps.IdProveedor = p.IdProveedor) INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero WHERE cps.IdSociedad = ?",
            r => (IdProveedor: r.Entero("IdProveedor"), Clase: r.Texto("Clase")?.Trim() ?? "", Identidad: r.Entero("IdIdentidadContable"), Nif: r.Texto("NIF")?.Trim() ?? ""),
            idSociedad);
        var cuentas = await s.ConsultarAsync(
            "SELECT cc.IdIdentidadContable, cc.Codigo, cc.IdSociedadEjercicio FROM CuentasContables AS cc INNER JOIN SociedadesEjercicios AS se " +
            "ON cc.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE se.IdSociedad = ?",
            r => (Identidad: r.Entero("IdIdentidadContable"), Codigo: r.Texto("Codigo")?.Trim() ?? "", IdSe: r.Entero("IdSociedadEjercicio")), idSociedad);
        var porIdentidad = cuentas.Where(c => c.Codigo.Length > 0).GroupBy(c => c.Identidad).ToDictionary(g => g.Key, g => g.ToList());
        // Un proveedor puede tener cuenta de proveedor y de acreedor: se unen, con la de proveedor primero.
        return filas.GroupBy(f => f.IdProveedor).Select(g =>
        {
            var orden = g.OrderBy(f => f.Clase.Equals("PROVEEDOR", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();
            var suyas = orden.SelectMany(f => porIdentidad.TryGetValue(f.Identidad, out var l) ? l : []).ToList();
            return new ProveedorEmpresa(g.Key, orden.Select(f => f.Nif).FirstOrDefault(n => n.Length > 0) ?? "",
                suyas.Where(c => c.IdSe == idSociedadEjercicio).Select(c => c.Codigo).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                suyas.Select(c => c.Codigo).ToHashSet(StringComparer.OrdinalIgnoreCase));
        }).ToList();
    }

    /// <summary>Carpetas de proveedor que ya tienen facturas de la empresa, con la cuenta o el proveedor de esas facturas.</summary>
    public static Task<IReadOnlyList<(string Carpeta, string? Cuenta, int? IdProveedor)>> CarpetasHistoricasAsync(ISesionDatos s, string idSociedad) =>
        s.ConsultarAsync(
            "SELECT Carpeta, CuentaContableProveedor, IdProveedor FROM FacturasCompra WHERE Sociedad = ? AND Carpeta Is Not Null " +
            "GROUP BY Carpeta, CuentaContableProveedor, IdProveedor",
            r => (r.Texto("Carpeta")!.Trim(), r.Texto("CuentaContableProveedor")?.Trim(), r.EnteroNulo("IdProveedor")), idSociedad);

    /// <summary>
    /// Hasta veinte números recientes del proveedor (por fecha y, a igualdad, por registro), como el ERP, y cuántas facturas
    /// completas tiene (número, fecha y total), que es el histórico que consolida la confianza.
    /// </summary>
    public static async Task<(IReadOnlyList<string> Numeros, int Completas)> HistoricoProveedorAsync(
        ISesionDatos s, string idSociedad, int? idProveedor, IReadOnlyCollection<string> cuentas)
    {
        if (idProveedor is null && cuentas.Count == 0) return ([], 0);
        var filtro = new StringBuilder("Sociedad = ? AND (");
        var parametros = new List<object?> { idSociedad };
        var condiciones = new List<string>();
        if (idProveedor is { } id) { condiciones.Add("IdProveedor = ?"); parametros.Add(id); }
        if (cuentas.Count > 0)
        {
            condiciones.Add($"CuentaContableProveedor IN ({string.Join(", ", cuentas.Select(_ => "?"))})");
            parametros.AddRange(cuentas);
        }
        filtro.Append(string.Join(" OR ", condiciones)).Append(')');
        var numeros = await s.ConsultarAsync(
            $"SELECT TOP 20 NumeroFactura, Fecha, IdRegistro FROM FacturasCompra WHERE {filtro} AND NumeroFactura Is Not Null AND Trim(NumeroFactura) <> '' " +
            "ORDER BY Fecha DESC, IdRegistro DESC", r => r.Texto("NumeroFactura")!.Trim(), [.. parametros]);
        var completas = Convert.ToInt32(await s.EscalarAsync(
            $"SELECT COUNT(*) FROM FacturasCompra WHERE {filtro} AND NumeroFactura Is Not Null AND Fecha Is Not Null AND ImporteTotal Is Not Null",
            [.. parametros]), CultureInfo.InvariantCulture);
        return (numeros, completas);
    }

    private static string? Nulo(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Recortar(string? s, int max) => Nulo(s) is { } t ? (t.Length > max ? t[..max] : t) : null;
}

/// <summary>Proveedor de la empresa tal como lo necesita la ficha: su tercero (para abrir su ficha), su NIF y su nombre.</summary>
public sealed record ProveedorFicha(int IdProveedor, int IdTercero, string Nif, string Nombre, byte? IdFormaPagoDefecto = null, ProductoCompra? ProductoDefecto = null);

/// <summary>Factura de compra resumida para la lista de la pantalla.</summary>
public sealed class FilaFacturaCompra
{
    public int IdRegistro { get; init; }
    public int? IdProveedor { get; init; }
    public string Proveedor { get; init; } = "";
    public string? Nif { get; init; }
    public string NumeroFactura { get; init; } = "";
    public DateTime? Fecha { get; init; }
    public decimal? BaseImponible { get; init; }
    public decimal? CuotaIva { get; init; }
    public decimal? ImporteTotal { get; init; }
    public decimal? ImporteRetencion { get; init; }
    public bool TieneRetencion { get; init; }
    public decimal? Suplido { get; init; }
    public bool InversionSujetoPasivo { get; init; }
    public OperacionCompra Operacion { get; init; }
    public string Divisa { get; init; } = "EUR";
    public int? IdRegistroFacturaRectificada { get; init; }
    public bool Contabilizada { get; init; }
    public bool Duplicada { get; init; }
    /// <summary>Ruta del PDF: la guardada si existe; si no, la reubicada en la carpeta de la empresa (ver ServicioFacturasCompra.LocalizarPdf).</summary>
    public string RutaPdf { get; set; } = "";
    /// <summary>Rutas tal como están grabadas (ArchivoOriginal y Archivo); no se modifican.</summary>
    public IReadOnlyList<string> RutasGuardadas { get; init; } = [];
    public string Carpeta { get; init; } = "";
    public string? CuentaGasto { get; init; }
    public int? IdCentroCoste { get; init; }
    public decimal? Confianza { get; init; }
    public bool RequiereRevision { get; init; }
    public DateTime? FechaHoraRegistro { get; init; }
    /// <summary>Número de asiento del ERP (ASIAPU), si está contabilizada.</summary>
    public int? Asiento { get; init; }
    /// <summary>La confianza más baja de los campos reconocidos (columna «Mín. %»); null sin reconocimiento.</summary>
    public decimal? ConfianzaMinima { get; init; }
    /// <summary>Versión de la fila: mayor que 0 si alguien la ha guardado desde la pantalla (revisada).</summary>
    public int Version { get; init; }

    public string NombreFichero => Path.GetFileName(RutaPdf);

    /// <summary>Base + cuota − retención + suplido = total, cuando los tres importes están.</summary>
    public bool Cuadra => BaseImponible is { } b && CuotaIva is { } c && ImporteTotal is { } t && b + c - (ImporteRetencion ?? 0m) + (Suplido ?? 0m) == t;

    /// <summary>Con proveedor, número, fecha e importes que cuadran.</summary>
    public bool Lista => (IdProveedor is not null || !string.IsNullOrWhiteSpace(Nif)) && NumeroFactura.Length > 0 && Fecha is not null && Cuadra;

    /// <summary>Sin nada reconocido ni escrito todavía.</summary>
    public bool Vacia => NumeroFactura.Length == 0 && Fecha is null && ImporteTotal is null;

    public bool EsRectificativa => IdRegistroFacturaRectificada is not null || ImporteTotal < 0;
}

/// <summary>Asiento de una factura de compra, para la pastilla de solo lectura de la ficha.</summary>
public sealed record AsientoFactura(int IdAsiento, DateTime? Fecha, string Descripcion, IReadOnlyList<ApunteFactura> Apuntes);

/// <summary>Un apunte del asiento de la factura.</summary>
public sealed record ApunteFactura(string Cuenta, string? NombreCuenta, decimal Debe, decimal Haber, string? Descripcion);

/// <summary>Proveedor de una empresa: su NIF de terceros, su cuenta en el ejercicio y los códigos que ha tenido.</summary>
internal sealed record ProveedorEmpresa(int IdProveedor, string Nif, IReadOnlyList<string> CuentasEjercicio, IReadOnlySet<string> Codigos)
{
    /// <summary>Cuenta del ejercicio para la factura: la de sus facturas anteriores si la tiene en este ejercicio; si no, la primera.</summary>
    public string? CuentaPara(string? cuentaHistorica) =>
        cuentaHistorica is not null && CuentasEjercicio.Contains(cuentaHistorica, StringComparer.OrdinalIgnoreCase) ? cuentaHistorica : CuentasEjercicio.FirstOrDefault();
}
