using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.Roles;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Cuentas y tipos de compras del ejercicio (SociedadesEjercicios, organizacion/sociedades-ejercicios): IVA soportado
/// por tipo, retenciones, autorrepercusión, importación, suplidos, redondeo, tipo general y trimestre activo. El IVA
/// soportado de cada tipo es CuentaTipoIVA0..4, que en los datos del ERP son cuentas 472 (4720021 para el 21 %).
/// </summary>
public sealed record ConfiguracionCompras(
    IReadOnlyDictionary<decimal, string> IvaSoportado, decimal? TipoGeneral, IReadOnlyList<decimal> Tipos, int? TrimestreActivo,
    string? RetencionServicios, string? RetencionAlquileres,
    string? IspSoportado, string? IspRepercutido,
    string? IntraSoportadoBienes, string? IntraSoportadoServicios, string? IntraRepercutidoBienes, string? IntraRepercutidoServicios,
    string? Importacion, string? Suplido, string? RedondeoGastos, string? RedondeoIngresos)
{
    public const string RedondeoGastosDefecto = "6590000";
    public const string RedondeoIngresosDefecto = "7590000";

    /// <summary>El tipo general configurado o, sin configurar, el más alto de los tipos del ejercicio (2026-11-06 12:10).</summary>
    public decimal? TipoGeneralEfectivo => TipoGeneral is > 0m ? TipoGeneral : Tipos.Count > 0 ? Tipos.Max() : null;
}

/// <summary>Estado de la exportación de una factura de compra hecha por ProContaVF.</summary>
/// <param name="Generacion">Número de lote vigente (0 el primero; sube con cada contra-asiento de un trimestre cerrado).</param>
/// <param name="IdAsiento">Asiento vigente de la factura (null: no exportada).</param>
/// <param name="IdAsientoAnulado">Asiento que anula el contra-asiento del lote vigente, si lo tiene.</param>
/// <param name="TrimestreLote">Trimestre del libro de IVA del lote vigente (null: lote vacío).</param>
/// <param name="DelErp">Contabilizada en el ERP: <paramref name="IdAsiento"/> es su asiento del ERP, que nunca se toca.</param>
public sealed record EstadoExportacionCompra(int Generacion, int? IdAsiento, int? IdAsientoAnulado, int? TrimestreLote, int? EjercicioLote, bool DelErp = false)
{
    public bool Exportada => IdAsiento is not null;
}

/// <summary>Lo que la exportación necesita del operador antes de seguir.</summary>
public enum PreguntaExportacion
{
    Ninguna,
    /// <summary>Primera exportación de un acreedor (410): ¿proveedor (400) o acreedor (410)? (2026-11-06 12:06)</summary>
    ProveedorOAcreedor,
    /// <summary>El descuadre pasa de 0,03 €: hace falta un motivo para el ajuste manual (2026-10-29 21:05).</summary>
    MotivoAjuste
}

/// <summary>Resultado de comprobar si una factura se puede exportar.</summary>
public sealed record ComprobacionExportacion(IReadOnlyList<Error> Errores, PreguntaExportacion Pregunta, string? Explicacion, bool TrimestreCerrado, decimal Descuadre)
{
    public bool Lista => Errores.Count == 0;
}

/// <summary>Opciones del operador para exportar.</summary>
/// <param name="EsProveedor">Respuesta a la pregunta 400/410: true pasa la 410 a una 400; false la deja como acreedor.</param>
/// <param name="MotivoAjuste">Motivo del ajuste manual de un descuadre mayor de 0,03 €.</param>
/// <param name="Masiva">En la exportación masiva no se pregunta: la factura que necesita respuesta queda pendiente.</param>
public sealed record OpcionesExportacion(bool? EsProveedor = null, string? MotivoAjuste = null, bool Masiva = false);

/// <summary>Lo que ha hecho la exportación o su deshacer, para avisar al operador.</summary>
public sealed record ResultadoExportacionCompra(int? IdAsiento, int? IdContraAsiento, string Aviso, string? CuentaProveedor = null);

/// <summary>
/// Exportación de facturas de compra a la contabilidad (compras/factura-compra/contabilizacion y compras/pantallas
/// 2026-11-06 12:10 y 2026-11-07): asiento con la fecha de la factura, o el día 1 del trimestre activo si es anterior
/// (<see cref="TrimestreContable"/>), libro de IVA soportado al trimestre de esa fecha en la misma transacción,
/// re-exportar y deshacer con las reglas del trimestre activo:
/// - si el lote vigente es del trimestre activo, re-exportar lo regenera y deshacer lo anula (borra el asiento);
/// - si es de un trimestre cerrado, no se toca: se abre un lote nuevo con el contra-asiento (y el asiento nuevo al
///   re-exportar), en el trimestre activo. Re-exportar otra vez en el trimestre activo modifica esos dos.
/// Solo se exportan, re-exportan o deshacen las facturas exportadas por ProContaVF; las del ERP no se tocan.
/// </summary>
public sealed class ContabilizadorCompras(IBaseDatos bd, IMotorAsientos motor, ICatalogoCuentas cuentas, ServicioRoles roles, IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public const string CuentaProveedores = "400";
    public const string CuentaAcreedores = "410";

    // ---------- configuración ----------

    public Task<ConfiguracionCompras?> ConfiguracionAsync(int idSociedadEjercicio) => bd.EjecutarAsync(s => ConfiguracionAsync(s, idSociedadEjercicio));

    internal static Task<ConfiguracionCompras?> ConfiguracionAsync(ISesionDatos s, int idSociedadEjercicio) =>
        s.ConsultarPrimeroAsync(
            "SELECT TipoIVA1, TipoIVA2, TipoIVA3, TipoIVA4, TipoIVAGeneral, TrimestreActivo, " +
            "CuentaTipoIVA0, CuentaTipoIVA1, CuentaTipoIVA2, CuentaTipoIVA3, CuentaTipoIVA4, " +
            "CuentaRetencionServicios, CuentaRetencionAlquileres, CuentaIVAInversionSujetoPasivoSoportado, CuentaIVAInversionSujetoPasivoRepercutido, " +
            "CuentaIVAIntracomunitarioSoportadoBienes, CuentaIVAIntracomunitarioSoportadoServicios, CuentaIVAIntracomunitarioRepercutidoBienes, " +
            "CuentaIVAIntracomunitarioRepercutidoServicios, CuentaIVAImportacion, CuentaSuplido, CuentaRedondeoGastos, CuentaRedondeoIngresos " +
            "FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f =>
            {
                var iva = new Dictionary<decimal, string>();
                var tipos = new List<decimal>();
                if (Texto(f, "CuentaTipoIVA0") is { } cero) iva[0m] = cero;
                for (var i = 1; i <= 4; i++)
                {
                    if (f.EsNulo($"TipoIVA{i}")) continue;
                    var tipo = Math.Round((decimal)f.Doble($"TipoIVA{i}"), 2);
                    tipos.Add(tipo);
                    if (Texto(f, $"CuentaTipoIVA{i}") is { } c) iva.TryAdd(tipo, c);
                }
                return new ConfiguracionCompras(iva, f.EsNulo("TipoIVAGeneral") ? null : Math.Round((decimal)f.Doble("TipoIVAGeneral"), 2), tipos,
                    f.EsNulo("TrimestreActivo") ? null : f.Entero("TrimestreActivo"),
                    Texto(f, "CuentaRetencionServicios"), Texto(f, "CuentaRetencionAlquileres"),
                    Texto(f, "CuentaIVAInversionSujetoPasivoSoportado"), Texto(f, "CuentaIVAInversionSujetoPasivoRepercutido"),
                    Texto(f, "CuentaIVAIntracomunitarioSoportadoBienes"), Texto(f, "CuentaIVAIntracomunitarioSoportadoServicios"),
                    Texto(f, "CuentaIVAIntracomunitarioRepercutidoBienes"), Texto(f, "CuentaIVAIntracomunitarioRepercutidoServicios"),
                    Texto(f, "CuentaIVAImportacion"), Texto(f, "CuentaSuplido"), Texto(f, "CuentaRedondeoGastos"), Texto(f, "CuentaRedondeoIngresos"));
            }, idSociedadEjercicio);

    private static string? Texto(IFilaDatos f, string campo) => f.Texto(campo) is { } t && !string.IsNullOrWhiteSpace(t) ? t.Trim() : null;

    /// <summary>Trimestre de una fecha (1 a 4).</summary>
    public static int Trimestre(DateTime fecha) => TrimestreContable.De(fecha);

    private static string Nombre(DateTime fecha) => TrimestreContable.De(fecha) switch { 1 => "ene-mar", 2 => "abr-jun", 3 => "jul-sep", _ => "oct-dic" };

    // ---------- estado ----------

    public Task<EstadoExportacionCompra> EstadoAsync(int idRegistro) => bd.EjecutarAsync(s => EstadoAsync(s, idRegistro));

    /// <summary>
    /// Estado de la exportación. Una factura contabilizada en el ERP (Exportada sin asiento de ProContaVF ni lote) da su
    /// asiento del ERP como asiento vigente, marcado <see cref="EstadoExportacionCompra.DelErp"/>: re-exportarla o
    /// deshacerla hace siempre un contra-asiento de él (compras/pantallas 2026-11-07). Null en IdAsiento si no se encuentra.
    /// </summary>
    internal static async Task<EstadoExportacionCompra> EstadoAsync(ISesionDatos s, int idRegistro)
    {
        var fila = await s.ConsultarPrimeroAsync(
            "SELECT IdSociedadEjercicio, GeneracionExportacion, IdAsientoContable, IdAsientoAnulado, Exportada, NumeroFactura, CuentaContableProveedor FROM FacturasCompra WHERE IdRegistro = ?",
            f => (Ejercicio: f.EnteroNulo("IdSociedadEjercicio") ?? 0, Generacion: f.EnteroNulo("GeneracionExportacion") ?? 0,
                Asiento: f.EnteroNulo("IdAsientoContable"), Anulado: f.EnteroNulo("IdAsientoAnulado"), Exportada: f.Logico("Exportada"),
                Numero: f.Texto("NumeroFactura")?.Trim(), Cuenta: f.Texto("CuentaContableProveedor")?.Trim()), idRegistro);
        if (fila.Exportada && fila.Asiento is null && fila.Anulado is null && fila.Generacion == 0)
        {
            // El enlace con el asiento del ERP sale de lo grabado (número y cuenta del proveedor), no de lo que se esté editando.
            var erp = string.IsNullOrEmpty(fila.Numero) || string.IsNullOrEmpty(fila.Cuenta) ? null
                : await RepositorioFacturasCompra.AsientoErpAsync(s, fila.Ejercicio != 0 ? fila.Ejercicio : await RepositorioFacturasCompra.EjercicioDeRegistroAsync(s, idRegistro),
                    fila.Numero, fila.Cuenta);
            return new EstadoExportacionCompra(0, erp?.IdAsiento, null, null, null, DelErp: true);
        }
        var lote = AsientoCompra.Lote(idRegistro, fila.Generacion);
        // El trimestre del lote es el menor de los periodos de IVA de sus registros: si uno ya está cerrado, el lote no se toca.
        var periodos = await s.ConsultarAsync(
            "SELECT p.Numero, se.Ejercicio FROM ((Asientos AS a INNER JOIN IVASoportado AS i ON i.IdAsiento = a.IdAsiento) " +
            "INNER JOIN PeriodosIVA AS p ON p.IdPeriodoIVA = i.IdPeriodoIVA) INNER JOIN SociedadesEjercicios AS se ON se.IdSociedadEjercicio = a.IdSociedadEjercicio " +
            "WHERE a.EsAutomatico = True AND a.MotorPropietario = ? AND a.CodigoProceso = ? AND a.ClaveEjecucion = ?",
            f => (Numero: f.Entero("Numero"), Ejercicio: int.TryParse(f.Texto("Ejercicio"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var e) ? e : 0),
            lote.MotorPropietario, lote.CodigoProceso, lote.ClaveEjecucion);
        return new EstadoExportacionCompra(fila.Generacion, fila.Asiento, fila.Anulado,
            periodos.Count == 0 ? null : periodos.Min(p => p.Numero), periodos.Count == 0 ? null : periodos.Min(p => p.Ejercicio));
    }

    /// <summary>
    /// El lote vigente está en un trimestre cerrado: es de otro ejercicio o de un trimestre anterior al activo del
    /// ejercicio de la factura (<see cref="TrimestreContable.Activo"/>).
    /// </summary>
    internal static bool LoteCerrado(EstadoExportacionCompra estado, ConfiguracionCompras config, int ejercicio, DateTime hoy) =>
        estado.DelErp || estado.TrimestreLote is int t && (estado.EjercicioLote != ejercicio || t < TrimestreContable.Activo(config.TrimestreActivo, ejercicio, hoy));

    /// <summary>El asiento vigente de la factura es de un trimestre cerrado: deshacer o re-exportar harán contra-asiento.</summary>
    public async Task<bool> TrimestreCerradoAsync(FacturaCompra f)
    {
        var estado = await EstadoAsync(f.IdRegistro);
        var config = await ConfiguracionAsync(f.Ejercicio.IdSociedadEjercicio);
        return config is not null && LoteCerrado(estado, config, f.Ejercicio.Ejercicio, _reloj.Ahora.Date);
    }

    // ---------- comprobar ----------

    /// <summary>Comprueba si la factura se puede exportar (o re-exportar) y qué hay que preguntar.</summary>
    public async Task<ComprobacionExportacion> ComprobarAsync(FacturaCompra f, OpcionesExportacion? opciones = null)
    {
        opciones ??= new OpcionesExportacion();
        var r = await PrepararAsync(f, opciones);
        return r.Comprobacion;
    }

    /// <param name="PorCrear">Cuentas configuradas en parámetros que aún no existen: se crean al exportar.</param>
    private sealed record Preparacion(ComprobacionExportacion Comprobacion, CuentasCompra? Cuentas, ConfiguracionCompras? Config, EstadoExportacionCompra? Estado,
        IReadOnlyList<(string Codigo, string Nombre)>? PorCrear = null);

    private async Task<Preparacion> PrepararAsync(FacturaCompra f, OpcionesExportacion opciones)
    {
        var registro = f.Rotulo;
        var errores = new List<Error>();
        Preparacion Fallo(params Error[] e) => new(new ComprobacionExportacion(e, PreguntaExportacion.Ninguna, null, false, 0m), null, null, null);

        var ctx = f.Ejercicio;
        var hoy = _reloj.Ahora.Date;
        if (f.Fecha is { } fp && fp.Year > ctx.Ejercicio)
            return Fallo(new Error("Exportar.EjercicioPosterior", $"La factura tiene fecha {fp:dd/MM/yyyy}, de un ejercicio posterior al {ctx.Ejercicio}.", registro,
                "El asiento lleva la fecha de la factura (o el día 1 del trimestre activo, si es anterior), dentro del ejercicio.", $"Expórtala desde el ejercicio {fp.Year} o corrige la fecha."));
        if (f.Fecha is { } ff && ff.Date > hoy)
            return Fallo(new Error("Exportar.FechaFutura", $"La factura tiene fecha {ff:dd/MM/yyyy}, posterior a hoy.", registro, null, "Corrige la fecha."));

        var pendiente = f.Pendiente().Where(p => p != "importes que cuadren").ToList();
        if (f.ImporteTotal is not null && f.BaseImponible is null) pendiente.Add("base imponible");
        if (pendiente.Count > 0)
            return Fallo(new Error("Exportar.Incompleta", $"Falta {string.Join(", ", pendiente)}.", registro, null, "Complétalo en la ficha y vuelve a exportar."));
        if (f.Duplicada)
            return Fallo(new Error("Exportar.Duplicada", "Esta factura está marcada como duplicada.", registro, null, "Exporta la original."));

        var estado = await EstadoAsync(f.IdRegistro);
        if (estado is { DelErp: true, IdAsiento: null })
            return Fallo(SinAsientoErp(registro));

        var config = await ConfiguracionAsync(ctx.IdSociedadEjercicio);
        if (config is null) return Fallo(new Error("Exportar.SinEjercicio", "No encuentro la configuración del ejercicio.", registro));
        var tipoGeneral = config.TipoGeneralEfectivo;

        var porCrear = new List<(string Codigo, string Nombre)>();
        async Task<string?> Cuenta(string? codigo, string que, string? campo = null, bool obligatoria = true)
        {
            if (string.IsNullOrWhiteSpace(codigo))
            {
                if (obligatoria)
                    errores.Add(new Error("Exportar.SinCuenta", $"Falta la {que}.", campo ?? registro, null,
                        campo is null ? "Configúrala en Parámetros y tablas › Ejercicios (grupo Cuentas)." : "Complétala en la ficha."));
                return null;
            }
            var c = await cuentas.ResolverAsync(ctx, codigo.Trim());
            // Una cuenta de los parámetros del ejercicio que no existe se crea al exportar, con el nombre de su parámetro.
            if (!c.EsCorrecto && campo is null)
            {
                var nombre = que.StartsWith("cuenta de ", StringComparison.Ordinal) ? que["cuenta de ".Length..] : que;
                porCrear.Add((codigo.Trim(), char.ToUpperInvariant(nombre[0]) + nombre[1..]));
                return codigo.Trim();
            }
            if (!c.EsCorrecto)
            {
                errores.Add(new Error("Exportar.CuentaNoExiste", $"La {que} {codigo.Trim()} no existe o no admite apuntes en el ejercicio {ctx.Ejercicio}.", registro, null,
                    "Dala de alta en el plan de cuentas o corrige la configuración."));
                return null;
            }
            return c.Valor.Codigo;
        }

        var proveedor = await Cuenta(f.CuentaContableProveedor, "cuenta del proveedor", "proveedor");
        var gasto = await Cuenta(f.CuentaGasto, "cuenta de gasto", "gasto");
        var bases = AsientoCompra.Bases(f);
        var iva = new Dictionary<decimal, string>();
        string? retencion = null, autoS = null, autoR = null, importacion = null, suplido = null;
        if (f.Operacion == OperacionCompra.Importacion || f.InversionSujetoPasivo || f.Operacion == OperacionCompra.Intracomunitaria)
        {
            if (tipoGeneral is null)
                errores.Add(new Error("Exportar.SinTipoGeneral", "El ejercicio no tiene tipo de IVA general ni tipos de IVA.", registro, null,
                    "Configura el tipo general en Parámetros y tablas › Ejercicios."));
        }
        if (f.Operacion == OperacionCompra.Importacion)
            importacion = await Cuenta(config.Importacion, "cuenta de IVA de importación");
        else if (f.InversionSujetoPasivo)
        {
            autoS = await Cuenta(config.IspSoportado, "cuenta de IVA soportado por inversión del sujeto pasivo");
            autoR = await Cuenta(config.IspRepercutido, "cuenta de IVA repercutido por inversión del sujeto pasivo");
        }
        else if (f.Operacion == OperacionCompra.Intracomunitaria)
        {
            var servicios = f.Producto == ProductoCompra.Servicios;
            autoS = await Cuenta(servicios ? config.IntraSoportadoServicios : config.IntraSoportadoBienes, $"cuenta de IVA intracomunitario soportado de {(servicios ? "servicios" : "bienes")}");
            autoR = await Cuenta(servicios ? config.IntraRepercutidoServicios : config.IntraRepercutidoBienes, $"cuenta de IVA intracomunitario repercutido de {(servicios ? "servicios" : "bienes")}");
        }
        else
        {
            foreach (var (tipo, _, cuota) in bases)
            {
                if (cuota == 0m || iva.ContainsKey(tipo)) continue;
                if (bases.Count == 1 && f.Desgloses.Count == 0 && !config.Tipos.Contains(tipo))
                {
                    errores.Add(new Error("Exportar.SinDesglose", "La factura no tiene desglose de IVA y su cuota no corresponde a ningún tipo del ejercicio.", "iva", null,
                        "Añade el desglose por tipo en la ficha."));
                    continue;
                }
                if (await Cuenta(config.IvaSoportado.TryGetValue(tipo, out var c) ? c : null, $"cuenta de IVA soportado del {tipo:0.##} %") is { } cc) iva[tipo] = cc;
            }
        }
        if ((f.ImporteRetencion ?? 0m) != 0m)
        {
            if (f.ClaseRetencion is null)
                errores.Add(new Error("Exportar.SinClaseRetencion", "La factura retiene pero no dice si es de servicios profesionales o de alquileres.", "retencion", null, "Elige el tipo de retención."));
            else
                retencion = await Cuenta(f.ClaseRetencion == ClaseRetencion.Arrendamientos ? config.RetencionAlquileres : config.RetencionServicios,
                    f.ClaseRetencion == ClaseRetencion.Arrendamientos ? "cuenta de retención de alquileres" : "cuenta de retención de servicios");
        }
        if ((f.Suplido ?? 0m) != 0m) suplido = await Cuenta(config.Suplido, "cuenta de suplidos");

        var descuadre = AsientoCompra.Descuadre(f);
        string? redG = null, redI = null;
        if (descuadre != 0m)
        {
            if (descuadre < 0m) redG = await Cuenta(config.RedondeoGastos ?? ConfiguracionCompras.RedondeoGastosDefecto, "cuenta de diferencias de redondeo (gastos)");
            else redI = await Cuenta(config.RedondeoIngresos ?? ConfiguracionCompras.RedondeoIngresosDefecto, "cuenta de diferencias de redondeo (ingresos)");
        }

        var cerrado = LoteCerrado(estado, config, ctx.Ejercicio, hoy);
        if (errores.Count > 0) return new(new ComprobacionExportacion(errores, PreguntaExportacion.Ninguna, null, cerrado, descuadre), null, config, estado);

        var pregunta = PreguntaExportacion.Ninguna;
        string? explicacion = null;
        if (Math.Abs(descuadre) > AsientoCompra.ToleranciaRedondeo && string.IsNullOrWhiteSpace(opciones.MotivoAjuste))
        {
            pregunta = PreguntaExportacion.MotivoAjuste;
            explicacion = AsientoCompra.ExplicarDescuadre(f);
        }
        else if (opciones.EsProveedor is null && await EsPrimeraDeAcreedorAsync(f, proveedor!))
        {
            pregunta = PreguntaExportacion.ProveedorOAcreedor;
            explicacion = TextoProveedorOAcreedor;
        }
        var nombreGasto = (await cuentas.ResolverAsync(ctx, gasto!)) is { EsCorrecto: true } g ? g.Valor.Nombre : null;
        var cuentasCompra = new CuentasCompra(proveedor!, gasto!, f.IdCentroCoste, iva, retencion, autoS, autoR, importacion, suplido,
            redG ?? "", redI ?? "", tipoGeneral ?? 0m, await NombreProveedorAsync(f), nombreGasto);
        return new(new ComprobacionExportacion([], pregunta, explicacion, cerrado, descuadre), cuentasCompra, config, estado, porCrear);
    }

    /// <summary>Nombre del proveedor para las descripciones: el comercial, si no la razón social (o el nombre leído, o la carpeta).</summary>
    private Task<string?> NombreProveedorAsync(FacturaCompra f) => bd.EjecutarAsync(async s =>
    {
        var t = await s.ConsultarPrimeroAsync(
            "SELECT t.NombreComercial, t.RazonSocial, t.Nombre FROM Proveedores AS p INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero WHERE p.IdProveedor = ?",
            r => (Comercial: r.Texto("NombreComercial"), Razon: r.Texto("RazonSocial"), Nombre: r.Texto("Nombre")), f.IdProveedor ?? 0);
        return new[] { t.Comercial, t.Razon, t.Nombre, f.NombreProveedorAeat, f.Carpeta }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
    });

    private static Error SinAsientoErp(string registro) => new("Exportar.SinAsientoErp", "No encuentro el asiento del ERP de esta factura.", registro,
        "Lo busco por el número de factura y la cuenta del proveedor grabados, en el apunte del proveedor que lleva el documento.",
        "Revisa el asiento en Contabilidad; si no existe, corrige la factura con un asiento manual.");

    public const string TextoProveedorOAcreedor =
        "Es la primera factura que se exporta de este acreedor (cuenta 410). Un proveedor (400) es quien te vende lo que forma parte de tu actividad: " +
        "mercaderías, materias primas o lo que revendes o transformas. Un acreedor (410) es quien te presta servicios o te vende otras cosas: " +
        "suministros, alquileres, asesoría, reparaciones, publicidad. ¿Lo dejo como acreedor o lo paso a proveedor (cambia la 410 por una 400)?";

    /// <summary>Primera exportación de un acreedor: su cuenta es una 410 sin apuntes y no tiene otras facturas exportadas.</summary>
    private Task<bool> EsPrimeraDeAcreedorAsync(FacturaCompra f, string cuentaProveedor) =>
        f.Contabilizada || !cuentaProveedor.StartsWith(CuentaAcreedores, StringComparison.Ordinal) || f.IdProveedor is null
            ? Task.FromResult(false)
            : bd.EjecutarAsync(async s =>
            {
                var apuntes = await s.EscalarAsync("SELECT NumeroApuntes FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", f.Ejercicio.IdSociedadEjercicio, cuentaProveedor);
                if (apuntes is not null and not DBNull && Convert.ToInt32(apuntes, CultureInfo.InvariantCulture) > 0) return false;
                var otras = await s.EscalarAsync("SELECT COUNT(*) FROM FacturasCompra WHERE IdProveedor = ? AND IdSociedadEjercicio = ? AND Exportada = True AND IdRegistro <> ?",
                    f.IdProveedor, f.Ejercicio.IdSociedadEjercicio, f.IdRegistro);
                return Convert.ToInt32(otras, CultureInfo.InvariantCulture) == 0;
            });

    // ---------- propuesta ----------

    /// <summary>
    /// Asiento que generaría exportar la factura, sin grabar nada (compras/pantallas 2026-11-07): la misma fecha, cuentas y
    /// apuntes que al exportar. Si algo impide exportarla, el mensaje lo dice y no hay asiento; si al exportar habrá que
    /// responder algo (motivo del ajuste, proveedor o acreedor) o se crearán cuentas, el mensaje lo avisa.
    /// </summary>
    public async Task<(AsientoFactura? Asiento, string? Mensaje)> ProponerAsync(FacturaCompra f)
    {
        ArgumentNullException.ThrowIfNull(f);
        var validacion = f.Validar();
        if (!validacion.EsCorrecto) return (null, "Para exportarla: " + string.Join(" ", validacion.Errores.Select(e => e.Mensaje)));
        var prep = await PrepararAsync(f, new OpcionesExportacion());
        var comp = prep.Comprobacion;
        if (!comp.Lista) return (null, "Aún no se puede exportar: " + string.Join(" ", comp.Errores.Select(e => e.AccionCorrectora is { } a ? $"{e.Mensaje} {a}" : e.Mensaje)));
        var avisos = new List<string>();
        string? motivo = null;
        if (comp.Pregunta == PreguntaExportacion.MotivoAjuste)
        {
            motivo = "propuesta";
            avisos.Add($"Descuadra {Math.Abs(comp.Descuadre):0.00} €: al exportar te pediré el motivo del ajuste.");
        }
        else if (comp.Pregunta == PreguntaExportacion.ProveedorOAcreedor) avisos.Add("Al exportar te preguntaré si es proveedor (400) o acreedor (410).");
        var hoy = _reloj.Ahora.Date;
        var config = prep.Config!;
        var ejercicio = f.Ejercicio.Ejercicio;
        var fecha = TrimestreContable.FechaAsiento(f.Fecha!.Value, ejercicio, TrimestreContable.Activo(config.TrimestreActivo, ejercicio, hoy));
        if (AsientoCompra.Construir(f, f.Ejercicio, prep.Cuentas!, fecha, motivo) is not { } propuesta) return (null, AsientoCompra.ExplicarDescuadre(f));
        var porCrear = (prep.PorCrear ?? []).ToDictionary(c => c.Codigo, c => c.Nombre, StringComparer.Ordinal);
        if (porCrear.Count > 0) avisos.Add($"Al exportar se crea{(porCrear.Count == 1 ? "" : "n")} {string.Join(", ", porCrear.Select(c => $"{c.Key} «{c.Value}»"))}.");
        if (prep.Estado is { Exportada: true }) avisos.Add("Ya está exportada: este sería el asiento al re-exportar.");
        var nombres = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var codigo in propuesta.Asiento.Apuntes.Select(a => a.CodigoCuenta).Distinct())
            nombres[codigo] = porCrear.TryGetValue(codigo, out var n) ? $"{n} (se creará)"
                : (await cuentas.ResolverAsync(f.Ejercicio, codigo)) is { EsCorrecto: true } c ? c.Valor.Nombre : null;
        var apuntes = propuesta.Asiento.Apuntes
            .Select(a => new ApunteFactura(a.CodigoCuenta, nombres.GetValueOrDefault(a.CodigoCuenta), a.Debe.Valor, a.Haber.Valor, a.Descripcion)).ToList();
        return (new AsientoFactura(0, propuesta.Asiento.FechaContable, propuesta.Asiento.Descripcion, apuntes), avisos.Count == 0 ? null : string.Join(" ", avisos));
    }

    // ---------- exportar ----------

    /// <summary>
    /// Exporta o re-exporta la factura. Sus datos se guardan siempre en la misma transacción que el asiento, para que la
    /// factura y el asiento coincidan («Guardar y re-exportar» y lo que la ficha propuso). <paramref name="guardarCambios"/>
    /// se conserva por compatibilidad.
    /// </summary>
    public async Task<Resultado<ResultadoExportacionCompra>> ExportarAsync(FacturaCompra f, Actor actor, OpcionesExportacion? opciones = null, bool guardarCambios = false)
    {
        ArgumentNullException.ThrowIfNull(f);
        opciones ??= new OpcionesExportacion();
        var registro = f.Rotulo;
        var validacion = f.Validar();
        if (!validacion.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(validacion.Errores);
        var prep = await PrepararAsync(f, opciones);
        var comp = prep.Comprobacion;
        if (!comp.Lista) return Resultado<ResultadoExportacionCompra>.Fallo(comp.Errores);
        if (comp.Pregunta != PreguntaExportacion.Ninguna)
            return Resultado<ResultadoExportacionCompra>.Fallo(new Error(comp.Pregunta == PreguntaExportacion.MotivoAjuste ? "Exportar.NecesitaMotivo" : "Exportar.Pregunta400",
                opciones.Masiva ? "Necesita una respuesta del operador: queda pendiente de exportar." : comp.Explicacion ?? "", registro, comp.Explicacion,
                "Expórtala sola para responder."));

        var creadas = new List<string>();
        foreach (var (codigo, nombre) in (prep.PorCrear ?? []).Distinct())
        {
            var creada = await cuentas.AltaSubcuentaAsync(f.Ejercicio, codigo, nombre);
            if (!creada.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(creada.Errores);
            creadas.Add($"{creada.Valor.Codigo} «{creada.Valor.Nombre}»");
        }
        string? cuentaNueva = null;
        var cuentasCompra = prep.Cuentas!;
        if (opciones.EsProveedor == true && cuentasCompra.Proveedor.StartsWith(CuentaAcreedores, StringComparison.Ordinal))
        {
            var paso = await PasarAProveedorAsync(f, cuentasCompra.Proveedor);
            if (!paso.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(paso.Errores);
            cuentaNueva = paso.Valor;
            f.CuentaContableProveedor = cuentaNueva;
            cuentasCompra = cuentasCompra with { Proveedor = cuentaNueva };
            guardarCambios = true;
        }

        var hoy = _reloj.Ahora.Date;
        var config = prep.Config!;
        var ejercicio = f.Ejercicio.Ejercicio;
        var activo = TrimestreContable.Activo(config.TrimestreActivo, ejercicio, hoy);
        var fecha = TrimestreContable.FechaAsiento(f.Fecha!.Value, ejercicio, activo);
        var propuesta = AsientoCompra.Construir(f, f.Ejercicio, cuentasCompra, fecha, opciones.MotivoAjuste);
        if (propuesta is null)
            return Resultado<ResultadoExportacionCompra>.Fallo(new Error("Exportar.NecesitaMotivo", AsientoCompra.ExplicarDescuadre(f), registro));

        var estado = prep.Estado!;
        var cerrado = LoteCerrado(estado, config, ejercicio, hoy);
        var generacion = estado.Generacion;
        var anulado = estado.IdAsientoAnulado;
        if (cerrado)
        {
            generacion++;
            anulado = estado.IdAsiento;
        }
        var asientos = new List<Asiento>();
        var libros = new List<LibroCompra>();
        if (anulado is int idAnulado)
        {
            var contra = await ContraAsync(idAnulado, f, activo);
            if (!contra.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(contra.Errores);
            asientos.Add(contra.Valor.Asiento);
            libros.Add(contra.Valor.Libro);
        }
        asientos.Add(propuesta.Asiento);
        libros.Add(propuesta.Libro);

        var lote = AsientoCompra.Lote(f.IdRegistro, generacion);
        var reexportando = estado.Exportada;
        var motivo = reexportando ? $"Re-exportar la {registro.ToLowerInvariant()}" : $"Exportar la {registro.ToLowerInvariant()}";
        var r = await motor.ReemplazarLoteAsync(f.Ejercicio, lote, asientos, actor, motivo, new TrabajoLote(
            Antes: BorrarLibroAsync,
            Despues: async (s, ids) =>
            {
                // La factura se guarda siempre con el asiento: lo que la ficha propuso (producto, forma de pago) queda igual en los dos.
                {
                    var guardada = await RepositorioFacturasCompra.ActualizarAsync(s, f, permitirExportada: true);
                    if (!guardada.EsCorrecto) return Resultado.Fallo(guardada.Errores);
                    f.Version = guardada.Valor;
                }
                // Cada registro del libro va al trimestre de la fecha de su asiento.
                for (var i = 0; i < ids.Count; i++)
                {
                    // El asiento del ERP puede no tener registro en el libro: su contra-asiento tampoco lo lleva.
                    if (libros[i].Vacio) continue;
                    var periodo = await PeriodoAsync(s, f.Ejercicio.IdSociedadEjercicio, ejercicio, TrimestreContable.De(asientos[i].FechaContable));
                    await GrabarLibroAsync(s, f, ids[i], periodo, libros[i], actor, i < ids.Count - 1);
                }
                await MarcarAsync(s, f.IdRegistro, true, generacion, ids[^1], anulado, _reloj.Ahora);
                return Resultado.Correcto();
            }));
        if (!r.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(r.Errores);
        f.Contabilizada = true;
        var idAsiento = r.Valor[^1];
        int? idContra = r.Valor.Count > 1 ? r.Valor[0] : null;
        var aviso = idContra is int ic && estado.DelErp
            ? $"El asiento {estado.IdAsiento} es del ERP: queda como estaba y he hecho el contra-asiento {ic} ({Nombre(asientos[0].FechaContable)}) y el asiento nuevo {idAsiento} ({Nombre(fecha)})."
            : idContra is int ic2 && cerrado
            ? $"El asiento anterior es de un trimestre cerrado: queda como estaba y he hecho el contra-asiento {ic2} ({Nombre(asientos[0].FechaContable)}) y el asiento nuevo {idAsiento} ({Nombre(fecha)})."
            : reexportando ? $"Asiento {idAsiento} actualizado con los cambios, con fecha {fecha:dd/MM/yyyy}." : $"Exportada: asiento {idAsiento}, con fecha {fecha:dd/MM/yyyy}.";
        if (propuesta.Descuadre != 0m) aviso += $" Lleva un ajuste de {Math.Abs(propuesta.Descuadre):0.00} € a la cuenta de redondeo.";
        if (cuentaNueva is not null) aviso += $" El proveedor pasa a la cuenta {cuentaNueva}.";
        if (creadas.Count > 0) aviso += $" He creado {(creadas.Count == 1 ? "la cuenta" : "las cuentas")} {string.Join(", ", creadas)}, que estaba{(creadas.Count == 1 ? "" : "n")} en los parámetros del ejercicio.";
        return Resultado<ResultadoExportacionCompra>.Correcto(new ResultadoExportacionCompra(idAsiento, idContra, aviso, cuentaNueva));
    }

    // ---------- deshacer ----------

    /// <summary>
    /// Deshace la exportación: si el lote vigente es del trimestre activo, borra su asiento (y su registro del libro), sin
    /// consecuencias fiscales; si es de un trimestre cerrado, hace un contra-asiento en el trimestre activo.
    /// </summary>
    public async Task<Resultado<ResultadoExportacionCompra>> DeshacerAsync(FacturaCompra f, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(f);
        var registro = f.Rotulo;
        var estado = await EstadoAsync(f.IdRegistro);
        if (estado.IdAsiento is not int actual)
            return Resultado<ResultadoExportacionCompra>.Fallo(estado.DelErp ? SinAsientoErp(registro) : new Error("Deshacer.NoExportada", "La factura no está exportada.", registro));
        var config = await ConfiguracionAsync(f.Ejercicio.IdSociedadEjercicio);
        if (config is null) return Resultado<ResultadoExportacionCompra>.Fallo(new Error("Exportar.SinEjercicio", "No encuentro la configuración del ejercicio.", registro));
        var hoy = _reloj.Ahora.Date;
        var ejercicio = f.Ejercicio.Ejercicio;
        var activo = TrimestreContable.Activo(config.TrimestreActivo, ejercicio, hoy);
        var cerrado = LoteCerrado(estado, config, ejercicio, hoy);

        var generacion = cerrado ? estado.Generacion + 1 : estado.Generacion;
        var anulado = cerrado ? actual : estado.IdAsientoAnulado;
        var lote = AsientoCompra.Lote(f.IdRegistro, generacion);
        var motivo = $"Deshacer la exportación de la {registro.ToLowerInvariant()}";
        if (anulado is not int idAnulado)
        {
            var borrado = await motor.EliminarLoteAsync(f.Ejercicio, lote, actor, motivo, new TrabajoLote(
                Antes: BorrarLibroAsync,
                Despues: async (s, _) => { await MarcarAsync(s, f.IdRegistro, false, generacion, null, null, null); return Resultado.Correcto(); }));
            if (!borrado.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(borrado.Errores);
            f.Contabilizada = false;
            return Resultado<ResultadoExportacionCompra>.Correcto(new ResultadoExportacionCompra(null, null, $"Exportación deshecha: he borrado el asiento {actual}."));
        }
        var contra = await ContraAsync(idAnulado, f, activo);
        if (!contra.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(contra.Errores);
        var r = await motor.ReemplazarLoteAsync(f.Ejercicio, lote, [contra.Valor.Asiento], actor, motivo, new TrabajoLote(
            Antes: BorrarLibroAsync,
            Despues: async (s, ids) =>
            {
                if (!contra.Valor.Libro.Vacio)
                {
                    var periodo = await PeriodoAsync(s, f.Ejercicio.IdSociedadEjercicio, ejercicio, TrimestreContable.De(contra.Valor.Asiento.FechaContable));
                    await GrabarLibroAsync(s, f, ids[0], periodo, contra.Valor.Libro, actor, anulacion: true);
                }
                await MarcarAsync(s, f.IdRegistro, false, generacion, null, idAnulado, null);
                return Resultado.Correcto();
            }));
        if (!r.EsCorrecto) return Resultado<ResultadoExportacionCompra>.Fallo(r.Errores);
        f.Contabilizada = false;
        var texto = estado.DelErp
            ? $"El asiento {actual} es del ERP: queda como estaba y he hecho el contra-asiento {r.Valor[0]} ({Nombre(contra.Valor.Asiento.FechaContable)}). La factura vuelve a Pendientes."
            : cerrado
            ? $"El asiento {actual} es de un trimestre cerrado: queda como estaba y he hecho el contra-asiento {r.Valor[0]} ({Nombre(contra.Valor.Asiento.FechaContable)})."
            : $"Exportación deshecha: he quitado el asiento {actual}; queda el contra-asiento {r.Valor[0]} del asiento {idAnulado}, que es de un trimestre cerrado.";
        return Resultado<ResultadoExportacionCompra>.Correcto(new ResultadoExportacionCompra(null, r.Valor[0], texto));
    }

    // ---------- piezas ----------

    /// <summary>Contra-asiento con la fecha del original, o el día 1 del trimestre activo si es anterior.</summary>
    private async Task<Resultado<(Asiento Asiento, LibroCompra Libro)>> ContraAsync(int idAsiento, FacturaCompra f, int trimestreActivo)
    {
        var original = await motor.ObtenerAsync(idAsiento);
        if (original is null)
            return Resultado<(Asiento, LibroCompra)>.Fallo(new Error("Exportar.SinAsientoAnterior", $"No encuentro el asiento {idAsiento} de la factura.", f.Rotulo, null,
                "Revisa el asiento en Contabilidad."));
        var libro = await bd.EjecutarAsync(s => LeerLibroAsync(s, idAsiento, f));
        var fecha = TrimestreContable.FechaAsiento(original.FechaContable, f.Ejercicio.Ejercicio, trimestreActivo);
        return Resultado<(Asiento, LibroCompra)>.Correcto((AsientoCompra.Contra(original, f.Ejercicio, fecha, f.Rotulo), libro.Negado()));
    }

    /// <summary>
    /// Pasa el acreedor a proveedor: la 410 sin apuntes cambia de código a la siguiente subcuenta libre de la 400; si tiene
    /// apuntes, se crea esa 400 con el mismo nombre. La cuenta cambia también en sus facturas pendientes.
    /// </summary>
    private async Task<Resultado<string>> PasarAProveedorAsync(FacturaCompra f, string cuenta410)
    {
        var ctx = f.Ejercicio;
        var antigua = await cuentas.ResolverAsync(ctx, cuenta410);
        if (!antigua.EsCorrecto) return Resultado<string>.Fallo(antigua.Errores);
        var codigo = await cuentas.SiguienteSubcuentaAsync(ctx, CuentaProveedores);
        if (codigo is null)
            return Resultado<string>.Fallo(new Error("Exportar.SinCodigos400", "No quedan códigos libres en la cuenta 400.", f.Rotulo, null, "Asígnale la cuenta a mano en la ficha del tercero."));
        // Sin apuntes en ningún ejercicio, la 410 cambia de código y pasa a ser la 400: no queda una subcuenta sin usar
        // (propietario 2026-11-07 11:59). Con apuntes, se crea una 400 nueva con el mismo nombre.
        var alta = await cuentas.CambiarCodigoSinApuntesAsync(ctx, antigua.Valor.IdIdentidadContable, codigo);
        var renombrada = alta.EsCorrecto;
        if (!renombrada)
        {
            var padre = await cuentas.ResolverAsync(ctx, CuentaProveedores);
            alta = await cuentas.AltaAsync(ctx, new AltaCuenta(codigo, antigua.Valor.Nombre, padre.EsCorrecto ? padre.Valor.IdCuentaContable : null));
            if (!alta.EsCorrecto) return Resultado<string>.Fallo(alta.Errores);
        }
        var asignada = await roles.AsignarCuentaAsync(ClaseCuentaRol.Proveedor, f.IdProveedor!.Value, ctx.IdSociedad, alta.Valor.IdIdentidadContable);
        if (!asignada.EsCorrecto) return Resultado<string>.Fallo(asignada.Errores);
        await roles.AsignarCuentaAsync(ClaseCuentaRol.Acreedor, f.IdProveedor!.Value, ctx.IdSociedad, null);
        await bd.EjecutarAsync(s => s.EjecutarAsync(
            // Renombrada, el código cambia en todos los ejercicios; si no, solo en este.
            "UPDATE FacturasCompra SET CuentaContableProveedor = ? WHERE IdProveedor = ? AND CuentaContableProveedor = ? AND Exportada = False AND IdRegistro <> ?" +
            (renombrada ? "" : " AND IdSociedadEjercicio = " + ctx.IdSociedadEjercicio.ToString(CultureInfo.InvariantCulture)),
            alta.Valor.Codigo, f.IdProveedor, cuenta410, f.IdRegistro));
        return Resultado<string>.Correcto(alta.Valor.Codigo);
    }

    private static Task MarcarAsync(ISesionDatos s, int idRegistro, bool exportada, int generacion, int? idAsiento, int? idAnulado, DateTime? cuando) =>
        s.EjecutarAsync("UPDATE FacturasCompra SET Exportada = ?, GeneracionExportacion = ?, IdAsientoContable = ?, IdAsientoAnulado = ?, FechaHoraExportacion = ? WHERE IdRegistro = ?",
            exportada, generacion, idAsiento, idAnulado, cuando, idRegistro);

    /// <summary>Antes de borrar los asientos de un lote, se quita su registro del libro de IVA (lo referencia).</summary>
    private static async Task BorrarLibroAsync(ISesionDatos s, IReadOnlyList<int> idsAsiento)
    {
        foreach (var id in idsAsiento)
        {
            var registros = await s.ConsultarAsync("SELECT IdIVASoportado FROM IVASoportado WHERE IdAsiento = ?", f => f.Entero("IdIVASoportado"), id);
            foreach (var r in registros)
            {
                await s.EjecutarAsync("DELETE FROM DesglosesIVASoportado WHERE IdIVASoportado = ?", r);
                await s.EjecutarAsync("DELETE FROM IVASoportado WHERE IdIVASoportado = ?", r);
            }
        }
    }

    /// <summary>Periodo de IVA del trimestre en el ejercicio; si aún no existe, se crea abierto.</summary>
    internal static async Task<int> PeriodoAsync(ISesionDatos s, int idSociedadEjercicio, int ejercicio, int trimestre)
    {
        var id = await s.EscalarAsync("SELECT IdPeriodoIVA FROM PeriodosIVA WHERE IdSociedadEjercicio = ? AND Numero = ?", idSociedadEjercicio, (byte)trimestre);
        if (id is not null and not DBNull) return Convert.ToInt32(id, CultureInfo.InvariantCulture);
        var inicio = new DateTime(ejercicio, (trimestre - 1) * 3 + 1, 1);
        await s.EjecutarAsync("INSERT INTO PeriodosIVA (IdSociedadEjercicio, Numero, FechaInicio, FechaFin, Estado) VALUES (?, ?, ?, ?, ?)",
            idSociedadEjercicio, (byte)trimestre, inicio, inicio.AddMonths(3).AddDays(-1), "ABIERTO");
        return await s.UltimoIdAsync();
    }

    /// <summary>Tipo de operación del libro de IVA soportado (TiposOperacion, catálogo técnico).</summary>
    internal static async Task<int> TipoOperacionAsync(ISesionDatos s, FacturaCompra f)
    {
        var codigo = f.Operacion switch
        {
            OperacionCompra.Intracomunitaria => CodigoTipoIntracomunitaria,
            OperacionCompra.Importacion => CodigoTipoImportacion,
            _ => f.InversionSujetoPasivo ? CodigoTipoInversion : CodigoTipoInterior
        };
        var id = await s.EscalarAsync("SELECT IdTipoOperacion FROM TiposOperacion WHERE Codigo = ?", codigo)
            ?? await s.EscalarAsync("SELECT IdTipoOperacion FROM TiposOperacion WHERE Codigo = ?", CodigoTipoInterior);
        if (id is null or DBNull) throw new InvalidOperationException($"Falta el tipo de operación «{codigo}» en TiposOperacion.");
        return Convert.ToInt32(id, CultureInfo.InvariantCulture);
    }

    // Códigos del catálogo técnico TiposOperacion (los del importador de ContaSol): nacional, intracomunitario e importación.
    // La inversión del sujeto pasivo nacional es una operación nacional con la marca InversionSujetoPasivo.
    internal const string CodigoTipoInterior = "SOPORTADO_0";
    internal const string CodigoTipoInversion = "SOPORTADO_0";
    internal const string CodigoTipoIntracomunitaria = "SOPORTADO_1";
    internal const string CodigoTipoImportacion = "SOPORTADO_2";

    private static async Task GrabarLibroAsync(ISesionDatos s, FacturaCompra f, int idAsiento, int idPeriodo, LibroCompra libro, Actor actor, bool anulacion)
    {
        var ahora = DateTime.Now;
        var tipo = await TipoOperacionAsync(s, f);
        var fecha = (f.Fecha ?? ahora).Date;
        var nif = f.NifProveedor;
        var pais = nif is { Length: > 2 } && char.IsAsciiLetter(nif[0]) && char.IsAsciiLetter(nif[1]) && !nif.StartsWith("ES", StringComparison.OrdinalIgnoreCase) && f.Operacion != OperacionCompra.Nacional
            ? nif[..2].ToUpperInvariant() : "ES";
        var nombres = await s.ConsultarPrimeroAsync("SELECT t.RazonSocial, t.Nombre FROM Proveedores AS p INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero WHERE p.IdProveedor = ?",
            r => (Razon: r.Texto("RazonSocial"), Nombre: r.Texto("Nombre")), f.IdProveedor ?? 0);
        var nombreProveedor = new[] { nombres.Razon, nombres.Nombre, f.NombreProveedorAeat, f.Carpeta }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
        await s.EjecutarAsync(
            "INSERT INTO IVASoportado (IdSociedadEjercicio, IdProveedor, IdAsiento, FechaExpedicion, FechaOperacion, FechaHoraRegistroContable, IdPeriodoIVA, NumeroFactura, " +
            "NombreProveedor, IdentificacionFiscalProveedor, PaisProveedor, IdTipoOperacion, TipoBienServicio, InversionSujetoPasivo, ImporteTotalFactura, BaseImponibleTotal, " +
            "CuotaSoportadaTotal, CuotaDevengadaAutorrepercutidaTotal, CuotaDeducibleTotal, IncluidoModelo347, Estado, Descripcion, OperadorCreacion, FechaHoraCreacion) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            f.Ejercicio.IdSociedadEjercicio, f.IdProveedor, idAsiento, fecha, fecha, ahora, idPeriodo, Recortar(f.NumeroFactura, 100),
            Recortar(nombreProveedor, 150), Recortar(nif, 30), pais, tipo, f.Producto == ProductoCompra.Servicios ? "SERVICIOS" : "BIENES", f.InversionSujetoPasivo,
            libro.Total, libro.Base, libro.CuotaSoportada, libro.CuotaAutorrepercutida, libro.CuotaDeducible,
            // Modelo 347: nacionales sin retención (las que la llevan se declaran en el 190 o el 180).
            f.Operacion == OperacionCompra.Nacional && (f.ImporteRetencion ?? 0m) == 0m,
            anulacion ? "ANULACION" : "REGISTRADO", anulacion ? $"Anulación de la {f.Rotulo.ToLowerInvariant()}" : f.Concepto, actor.ToString(), ahora);
        var idLibro = await s.UltimoIdAsync();
        var orden = 1;
        var calificacion = f.InversionSujetoPasivo || f.Operacion == OperacionCompra.Intracomunitaria ? "S2" : "S1";
        var clave = f.Operacion switch { OperacionCompra.Intracomunitaria => "09", OperacionCompra.Importacion => "13", _ => "01" };
        foreach (var t in libro.Tramos)
        {
            var idS = t.CuentaSoportado is null ? null : await s.EscalarAsync("SELECT IdCuentaContable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", f.Ejercicio.IdSociedadEjercicio, t.CuentaSoportado);
            var idR = t.CuentaRepercutido is null ? null : await s.EscalarAsync("SELECT IdCuentaContable FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?", f.Ejercicio.IdSociedadEjercicio, t.CuentaRepercutido);
            await s.EjecutarAsync(
                "INSERT INTO DesglosesIVASoportado (IdIVASoportado, Orden, ClaveRegimen, CalificacionOperacion, CausaExencion, BaseImponible, TipoIVA, CuotaSoportada, " +
                "CuotaDevengadaAutorrepercutida, PorcentajeDeducible, CuotaDeducible, IdCuentaIVASoportado, IdCuentaIVARepercutido) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                idLibro, orden++, clave, t.TipoIva == 0m && t.CuotaAutorrepercutida == 0m ? "N1" : calificacion, null, t.Base, t.TipoIva, t.CuotaSoportada,
                t.CuotaAutorrepercutida, 100m, t.CuotaDeducible, idS is null or DBNull ? null : idS, idR is null or DBNull ? null : idR);
        }
    }

    /// <summary>
    /// Registro del libro de un asiento, para su contra-asiento. Los registros del ERP no suelen tener IdAsiento: si no hay
    /// ninguno enlazado, se busca el de la factura sin asiento por su número y su proveedor en el ejercicio.
    /// </summary>
    private static async Task<LibroCompra> LeerLibroAsync(ISesionDatos s, int idAsiento, FacturaCompra factura)
    {
        var cabecera = await s.ConsultarPrimeroAsync("SELECT IdIVASoportado, ImporteTotalFactura FROM IVASoportado WHERE IdAsiento = ?",
            f => (Id: f.Entero("IdIVASoportado"), Total: f.Decimal("ImporteTotalFactura")), idAsiento);
        var idProveedor = factura.IdProveedor ?? await RepositorioFacturasCompra.ProveedorDeErpAsync(s, factura);
        if (cabecera.Id == 0 && idProveedor is int prov && !string.IsNullOrWhiteSpace(factura.NumeroFactura))
            cabecera = await s.ConsultarPrimeroAsync(
                "SELECT TOP 1 IdIVASoportado, ImporteTotalFactura FROM IVASoportado WHERE IdAsiento Is Null AND IdProveedor = ? AND Trim(NumeroFactura) = ? " +
                "AND (IdSociedadEjercicio = ? OR IdSociedadEjercicio Is Null) ORDER BY IdIVASoportado",
                f => (Id: f.Entero("IdIVASoportado"), Total: f.Decimal("ImporteTotalFactura")),
                prov, factura.NumeroFactura.Trim(), factura.Ejercicio.IdSociedadEjercicio);
        // El ERP puede haber grabado el número de otra forma: el mismo proveedor, la misma fecha y el mismo total.
        if (cabecera.Id == 0 && idProveedor is int prov2 && factura.Fecha is DateTime fecha && factura.ImporteTotal is decimal total)
            cabecera = await s.ConsultarPrimeroAsync(
                "SELECT TOP 1 IdIVASoportado, ImporteTotalFactura FROM IVASoportado WHERE IdAsiento Is Null AND IdProveedor = ? AND FechaExpedicion = ? " +
                "AND ImporteTotalFactura = ? AND (IdSociedadEjercicio = ? OR IdSociedadEjercicio Is Null) ORDER BY IdIVASoportado",
                f => (Id: f.Entero("IdIVASoportado"), Total: f.Decimal("ImporteTotalFactura")),
                prov2, fecha.Date, total, factura.Ejercicio.IdSociedadEjercicio);
        if (cabecera.Id == 0) return new LibroCompra(0m, []);
        var tramos = await s.ConsultarAsync(
            "SELECT d.TipoIVA, d.BaseImponible, d.CuotaSoportada, d.CuotaDevengadaAutorrepercutida, cs.Codigo AS CuentaS, cr.Codigo AS CuentaR " +
            "FROM (DesglosesIVASoportado AS d LEFT JOIN CuentasContables AS cs ON d.IdCuentaIVASoportado = cs.IdCuentaContable) " +
            "LEFT JOIN CuentasContables AS cr ON d.IdCuentaIVARepercutido = cr.IdCuentaContable WHERE d.IdIVASoportado = ? ORDER BY d.Orden",
            f => new TramoLibroCompra(f.Decimal("TipoIVA"), f.Decimal("BaseImponible"), f.Decimal("CuotaSoportada"), f.Decimal("CuotaDevengadaAutorrepercutida"),
                f.Texto("CuentaS"), f.Texto("CuentaR")), cabecera.Id);
        return new LibroCompra(cabecera.Total, tramos);
    }

    private static string? Recortar(string? texto, int maximo) => texto is null ? null : texto.Length <= maximo ? texto : texto[..maximo];
}
