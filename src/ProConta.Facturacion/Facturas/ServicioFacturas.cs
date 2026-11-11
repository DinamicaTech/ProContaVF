using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Permisos;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Resultado de validar un borrador. Si <see cref="Factura"/> es null no se ha validado: <see cref="Incidencias"/> dice
/// por qué, y si solo hay avisos basta con confirmarlos. Si se ha validado pero el asiento falló, la factura queda
/// validada sin asiento y <see cref="ErrorContabilizacion"/> lo explica (se puede volver a contabilizar).
/// </summary>
public sealed record ResultadoValidacion(FacturaVenta? Factura, IReadOnlyList<Incidencia> Incidencias, IReadOnlyList<Error>? ErrorContabilizacion = null)
{
    public bool Validada => Factura is not null;
    public bool SoloAvisos => Factura is null && Incidencias.Count > 0 && Incidencias.All(i => i.EsAviso);
    public bool Contabilizada => Validada && (ErrorContabilizacion is null || ErrorContabilizacion.Count == 0);
}

/// <summary>Valores que se proponen al elegir el cliente de un borrador: los de su última factura en la sociedad.</summary>
public sealed record PropuestaCliente(int IdCliente, string Nombre, string? Nif, decimal? TipoRetencion, byte? FormaDePago, byte? DiaDePago);

/// <summary>
/// Servicio del ente FacturaVenta (facturacion/factura-venta): cada acción comprueba el estado y el permiso en el
/// servicio, nunca solo en la pantalla (RF-FAC-06, 07). Guardar un borrador no numera; validar numera en una transacción,
/// congela las instantáneas, pasa por el paso fiscal y después contabiliza.
/// </summary>
public sealed class ServicioFacturas
{
    private const int IntentosNumeracion = 5;

    private readonly IBaseDatos _bd;
    private readonly IServicioSociedades _sociedades;
    private readonly IRepositorioTerceros _terceros;
    private readonly ServicioRoles _roles;
    private readonly ICatalogoCuentas _cuentas;
    private readonly ContabilizadorVentas _contabilizador;
    private readonly IAutorizadorAcciones _autorizador;
    private readonly IPasoFiscal _pasoFiscal;
    private readonly IReloj _reloj;

    public ServicioFacturas(IBaseDatos bd, IServicioSociedades sociedades, IRepositorioTerceros terceros, ServicioRoles roles, ICatalogoCuentas cuentas,
        ContabilizadorVentas contabilizador, IAutorizadorAcciones? autorizador = null, IPasoFiscal? pasoFiscal = null, IReloj? reloj = null)
    {
        _bd = bd;
        _sociedades = sociedades;
        _terceros = terceros;
        _roles = roles;
        _cuentas = cuentas;
        _contabilizador = contabilizador;
        _autorizador = autorizador ?? AutorizadorTodo.Instancia;
        _pasoFiscal = pasoFiscal ?? PasoFiscalSinVeriFactu.Instancia;
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    public string NombrePasoFiscal => _pasoFiscal.Nombre;

    public Task<FacturaVenta?> ObtenerAsync(int idFactura) => _bd.EjecutarAsync(s => RepositorioFacturas.CargarAsync(s, idFactura));

    public Task<IReadOnlyList<Serie>> SeriesAsync(int idSociedadEjercicio) => _bd.EjecutarAsync(s => RepositorioFacturas.SeriesAsync(s, idSociedadEjercicio));

    public Task<bool> PuedeAsync(string accion) => _autorizador.PuedeAsync(accion);

    /// <summary>Parámetro general «Activar VeriFactu» (Configuracion.VeriFactuActivo); desactivado si aún no hay configuración.</summary>
    public Task<bool> VeriFactuActivoAsync() =>
        _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT TOP 1 VeriFactuActivo FROM Configuracion ORDER BY IdConfiguracion", f => f.Logico("VeriFactuActivo")));

    /// <summary>Datos actuales del cliente como quedarían congelados al validar (para la proforma de un borrador).</summary>
    public async Task<InstantaneaCliente?> InstantaneaClienteAsync(int idCliente) =>
        await TerceroDeClienteAsync(idCliente) is { } t ? Instantaneas.DeTercero(t) : null;

    // ---------- borrador ----------

    /// <summary>
    /// Borrador nuevo sin guardar en el ejercicio de trabajo: serie normal predeterminada, fecha de hoy (si cae en el
    /// ejercicio) y la forma de pago del ejercicio. No tiene número ni consume ninguno.
    /// </summary>
    public async Task<Resultado<FacturaVenta>> NuevoBorradorAsync(ContextoSociedadEjercicio contexto)
    {
        var serie = await _bd.EjecutarAsync(s => RepositorioFacturas.SerieNormalAsync(s, contexto.IdSociedadEjercicio, contexto.Ejercicio));
        if (!serie.EsCorrecto) return Resultado<FacturaVenta>.Fallo(serie.Errores);
        var config = await new LectorConfiguracionEjercicio(_bd).ObtenerAsync(contexto.IdSociedadEjercicio);
        var hoy = _reloj.Ahora.Date;
        return Resultado<FacturaVenta>.Correcto(new FacturaVenta
        {
            IdSociedadEjercicio = contexto.IdSociedadEjercicio,
            IdSerie = serie.Valor.IdSerie,
            FechaFactura = hoy.Year == contexto.Ejercicio ? hoy : null,
            // Fecha de operación y periodo proponen el día de hoy (facturacion/pantallas 2026-11-04); se pueden cambiar.
            FechaOperacion = hoy,
            FechaInicioValidez = hoy,
            FechaFinValidez = hoy,
            FechaCreacion = _reloj.Ahora,
            FormaDePago = config?.FormaPagoDefecto,
            TipoFactura = "F1"
        });
    }

    /// <summary>Lo que se propone al elegir cliente: retención, forma y día de pago de su última factura en la sociedad.</summary>
    public Task<PropuestaCliente?> PropuestaClienteAsync(int idCliente, string idSociedad) =>
        _bd.EjecutarAsync(async s =>
        {
            var cliente = await s.ConsultarPrimeroAsync(
                "SELECT c.IdCliente, t.Nombre, t.NIF FROM Clientes AS c INNER JOIN Terceros AS t ON c.IdTercero = t.IdTercero WHERE c.IdCliente = ?",
                f => (Nombre: f.Texto("Nombre") ?? "", Nif: f.Texto("NIF")), idCliente);
            if (cliente == default) return null;
            var ultima = await s.ConsultarPrimeroAsync(
                "SELECT TOP 1 f.TipoRetencion, f.FormaDePago, f.DiaDePago FROM Facturas AS f INNER JOIN SociedadesEjercicios AS se ON f.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                "WHERE f.IdCliente = ? AND se.IdSociedad = ? AND f.NumeroFactura IS NOT NULL ORDER BY f.FechaFactura DESC, f.IdFactura DESC",
                f => (Retencion: f.EsNulo("TipoRetencion") ? (decimal?)null : Math.Round((decimal)f.Doble("TipoRetencion"), 2),
                    Forma: f.EsNulo("FormaDePago") ? (byte?)null : (byte)f.Entero("FormaDePago"),
                    Dia: f.EsNulo("DiaDePago") ? (byte?)null : (byte)f.Entero("DiaDePago")),
                idCliente, idSociedad);
            return (PropuestaCliente?)new PropuestaCliente(idCliente, cliente.Nombre, cliente.Nif, ultima.Retencion, ultima.Forma, ultima.Dia);
        });

    /// <summary>
    /// Guarda un borrador (alta o modificación) con concurrencia optimista. No valida ni numera: un borrador puede estar
    /// incompleto. En una rectificativa solo se guardan descripción, fechas y notas: cliente y líneas son los de la
    /// original en negativo (rectificativa completa 1:1).
    /// </summary>
    public async Task<Resultado<FacturaVenta>> GuardarBorradorAsync(FacturaVenta factura, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<FacturaVenta>("editar facturas", factura.Rotulo);
        if (!factura.EsBorrador)
            return Resultado<FacturaVenta>.Fallo(NoEsBorrador(factura, "cambiar"));
        var ejercicio = await _sociedades.ObtenerEjercicioAsync(factura.IdSociedadEjercicio);
        if (ejercicio is null) return Resultado<FacturaVenta>.Fallo(new Error("Facturas.SinEjercicio", "El ejercicio de la factura no existe.", factura.Rotulo));
        var abierto = await _sociedades.ComprobarEjercicioAbiertoAsync(ejercicio.Contexto);
        if (!abierto.EsCorrecto) return Resultado<FacturaVenta>.Fallo(abierto.Errores);
        var serie = await _bd.EjecutarAsync(s => RepositorioFacturas.SerieAsync(s, factura.IdSerie));
        if (serie is null || serie.IdSociedadEjercicio != factura.IdSociedadEjercicio || serie.EsRectificativa != factura.EsRectificativa)
            return Resultado<FacturaVenta>.Fallo(new Error("Facturas.SerieNoValida",
                factura.EsRectificativa ? "La serie de una rectificativa debe ser la de rectificativas de su ejercicio." : "La serie elegida no es una serie normal del ejercicio de la factura.",
                factura.Rotulo, null, "Elige otra serie en la cabecera."));

        try
        {
            var guardada = await _bd.EnTransaccionAsync(async s =>
            {
                var f = factura.Clonar();
                if (f.EsRectificativa && !f.EsNueva)
                {
                    var actual = await RepositorioFacturas.CargarAsync(s, f.IdFactura);
                    if (actual is not null)
                    {
                        f.IdCliente = actual.IdCliente;
                        f.TipoRetencion = actual.TipoRetencion;
                        f.Lineas.Clear();
                        f.Lineas.AddRange(actual.Lineas);
                    }
                }
                foreach (var l in f.Lineas) l.Nombre = l.Nombre.Trim();
                if (f.EsNueva)
                {
                    f.FechaCreacion = f.FechaCreacion == default ? _reloj.Ahora : f.FechaCreacion;
                    f.Anotar(_reloj.Ahora, actor.Nombre, "Borrador creado");
                    await RepositorioFacturas.InsertarAsync(s, f);
                }
                else
                {
                    await RepositorioFacturas.ActualizarBorradorAsync(s, f);
                }
                return f;
            });
            return Resultado<FacturaVenta>.Correcto((await ObtenerAsync(guardada.IdFactura))!);
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<FacturaVenta>.Fallo(ex.Error);
        }
    }

    /// <summary>
    /// Borra un borrador (nunca una factura numerada). Si es una rectificativa, su original vuelve a Validada en la misma
    /// transacción.
    /// </summary>
    public async Task<Resultado> EliminarBorradorAsync(int idFactura, int versionLeida, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<bool>("eliminar borradores", FacturaVenta.RotuloBorrador(idFactura));
        try
        {
            return await _bd.EnTransaccionAsync(async s =>
            {
                var f = await RepositorioFacturas.CargarAsync(s, idFactura);
                if (f is null) return Resultado.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura), "Otro operador la ha borrado.", "Actualiza el listado."));
                if (!f.EsBorrador || f.NumeroFactura is not null) return Resultado.Fallo(NoEsBorrador(f, "eliminar"));
                if (f.Version != versionLeida)
                    return Resultado.Fallo(new Error("Facturas.Conflicto", "Otro operador ha cambiado este borrador después de abrirlo.", f.Rotulo,
                        "Lo que ves no es lo último guardado.", "Vuelve a abrir el borrador y repite la operación."));
                if (f.IdFacturaRectificada is int idOriginal)
                {
                    var original = await RepositorioFacturas.CargarAsync(s, idOriginal);
                    if (original is not null)
                    {
                        original.Anotar(_reloj.Ahora, actor.Nombre, $"Se elimina el borrador de su rectificativa; vuelve a Validada");
                        await RepositorioFacturas.CambiarEstadoAsync(s, idOriginal, EstadoFactura.RectificacionPendiente, EstadoFactura.Validada, original.Historial);
                    }
                }
                await RepositorioFacturas.BorrarAsync(s, idFactura);
                return Resultado.Correcto();
            });
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado.Fallo(ex.Error);
        }
    }

    // ---------- factura validada sin enviar a VeriFactu ----------

    /// <summary>
    /// Una factura numerada se puede modificar o deshacer su exportación mientras no se haya enviado a VeriFactu: marcada
    /// «sin VeriFactu» o sin ningún registro de envío (facturacion/factura-venta 2026-11-08).
    /// </summary>
    public Task<bool> EnviadaAVeriFactuAsync(FacturaVenta f) =>
        f.SinVeriFactu ? Task.FromResult(false) : _bd.EjecutarAsync(s => RepositorioFacturas.TieneEnvioVeriFactuAsync(s, f.IdFactura));

    /// <summary>La factura está validada, sin rectificar y sin enviar a VeriFactu: se puede modificar cualquier campo o línea.</summary>
    public async Task<bool> SePuedeModificarAsync(FacturaVenta f) =>
        f.NumeroFactura is not null && f.Estado == EstadoFactura.Validada && !await EnviadaAVeriFactuAsync(f);

    /// <summary>Si guardar estos cambios afecta al asiento vigente, lo que se hará con él; si no, «NoExportada».</summary>
    public async Task<PlanDeshacerVenta> PlanGuardarValidadaAsync(FacturaVenta f)
    {
        var actual = await ObtenerAsync(f.IdFactura);
        return actual is null || actual.IdAsientoContable is null || AsientoVenta.Huella(actual) == AsientoVenta.Huella(f)
            ? new PlanDeshacerVenta(ModoDeshacerVenta.NoExportada, null)
            : await _contabilizador.PlanDeshacerAsync(actual);
    }

    /// <summary>
    /// Guarda una factura validada sin enviar a VeriFactu. El número y la serie no cambian y la fecha debe seguir el orden
    /// de la serie. Si el cambio afecta al asiento, el asiento se regenera en la misma transacción: se sustituye o, si es de
    /// un trimestre cerrado, se hace su contra-asiento y uno nuevo. Si no tenía asiento, sigue sin él.
    /// </summary>
    public async Task<Resultado<FacturaVenta>> GuardarValidadaAsync(FacturaVenta factura, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<FacturaVenta>("editar facturas", factura.Rotulo);
        var actual = await ObtenerAsync(factura.IdFactura);
        if (actual is null) return Resultado<FacturaVenta>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", factura.Rotulo));
        if (actual.Version != factura.Version)
            return Resultado<FacturaVenta>.Fallo(new Error("Facturas.Conflicto", "Otro operador ha cambiado esta factura después de abrirla.", factura.Rotulo, null, "Vuelve a abrirla y repite el cambio."));
        if (!await SePuedeModificarAsync(actual))
            return Resultado<FacturaVenta>.Fallo(new Error("Facturas.NoModificable", "Esta factura ya no se puede modificar.", factura.Rotulo,
                actual.Estado == EstadoFactura.Validada ? "Ya se ha enviado a VeriFactu." : "Está rectificada o pendiente de rectificación.",
                "Corrígela con una rectificativa."));

        var f = factura.Clonar();
        f.IdSerie = actual.IdSerie;
        if (f.EsRectificativa)
        {
            f.IdCliente = actual.IdCliente;
            f.TipoRetencion = actual.TipoRetencion;
            f.Lineas.Clear();
            f.Lineas.AddRange(actual.Lineas);
        }
        foreach (var l in f.Lineas) l.Nombre = l.Nombre.Trim();
        InstantaneaCliente? nuevoCliente = null;
        if (f.IdCliente != actual.IdCliente && f.IdCliente is int idCliente && await TerceroDeClienteAsync(idCliente) is { } tercero)
            nuevoCliente = Instantaneas.DeTercero(tercero);
        f.Cliente = f.IdCliente == actual.IdCliente ? actual.Cliente : nuevoCliente ?? InstantaneaCliente.Vacia;

        var errores = (await ComprobarAsync(f)).Where(i => !i.EsAviso).ToList();
        if (errores.Count > 0)
            return Resultado<FacturaVenta>.Fallo(errores.Select(i => new Error(i.Codigo, i.Mensaje, f.Rotulo)));

        var regenerar = actual.IdAsientoContable is not null && AsientoVenta.Huella(actual) != AsientoVenta.Huella(f);
        f.Anotar(_reloj.Ahora, actor.Nombre, regenerar ? "Modificada después de validar; asiento regenerado" : "Modificada después de validar");
        try
        {
            if (regenerar)
            {
                var asiento = await _contabilizador.ContabilizarAsync(f, actor, s => RepositorioFacturas.ActualizarValidadaAsync(s, f));
                if (!asiento.EsCorrecto) return Resultado<FacturaVenta>.Fallo(asiento.Errores);
            }
            else
            {
                await _bd.EnTransaccionAsync(async s => { await RepositorioFacturas.ActualizarValidadaAsync(s, f); return true; });
            }
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<FacturaVenta>.Fallo(ex.Error);
        }
        return Resultado<FacturaVenta>.Correcto((await ObtenerAsync(f.IdFactura))!);
    }

    /// <summary>La factura tiene un PDF archivado (Facturas.PDFFactura).</summary>
    public async Task<bool> TienePdfArchivadoAsync(int idFactura) =>
        await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT PDFFactura FROM Facturas WHERE IdFactura = ?", idFactura)) is string ruta && !string.IsNullOrWhiteSpace(ruta);

    /// <summary>Lo que hará deshacer la exportación de la factura, para avisar antes.</summary>
    public Task<PlanDeshacerVenta> PlanDeshacerAsync(FacturaVenta f) => _contabilizador.PlanDeshacerAsync(f);

    /// <inheritdoc cref="PlanDeshacerAsync(FacturaVenta)"/>
    public async Task<PlanDeshacerVenta> PlanDeshacerAsync(int idFactura) =>
        await ObtenerAsync(idFactura) is { } f ? await _contabilizador.PlanDeshacerAsync(f) : new PlanDeshacerVenta(ModoDeshacerVenta.NoExportada, null);

    /// <summary>
    /// «Deshacer exportación contable» (propietario 2026-11-08): del trimestre activo o posterior borra el asiento; de uno
    /// cerrado hace un contra-asiento. La factura queda sin asiento. No se puede si ya se ha enviado a VeriFactu.
    /// </summary>
    public async Task<Resultado<string>> DeshacerExportacionAsync(int idFactura, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasValidar)) return SinPermiso<string>("deshacer la exportación contable", FacturaVenta.RotuloBorrador(idFactura));
        var f = await ObtenerAsync(idFactura);
        if (f is null) return Resultado<string>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura)));
        if (await EnviadaAVeriFactuAsync(f))
            return Resultado<string>.Fallo(new Error("Facturas.EnviadaVeriFactu", "Esta factura ya se ha enviado a VeriFactu, así que no se puede deshacer su exportación.", f.Rotulo, null,
                "Corrígela con una rectificativa."));
        try
        {
            return await _contabilizador.DeshacerAsync(f, actor);
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<string>.Fallo(ex.Error);
        }
    }

    /// <summary>Notas internas de una factura numerada que ya no se puede modificar (RF-FAC-07).</summary>
    public async Task<Resultado> GuardarNotasAsync(int idFactura, string? notas, int versionLeida, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<bool>("editar facturas", FacturaVenta.RotuloBorrador(idFactura));
        try
        {
            await _bd.EjecutarAsync(async s =>
            {
                var f = await RepositorioFacturas.CargarAsync(s, idFactura);
                await s.ActualizarConVersionAsync("UPDATE Facturas SET NotasInternas = ?, Version = Version + 1 WHERE IdFactura = ? AND Version = ?",
                    f?.Rotulo ?? FacturaVenta.RotuloBorrador(idFactura), string.IsNullOrWhiteSpace(notas) ? null : notas.Trim(), idFactura, versionLeida);
                return true;
            });
            return Resultado.Correcto();
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado.Fallo(ex.Error);
        }
    }

    // ---------- validación ----------

    /// <summary>
    /// Todas las incidencias que impedirían validar el borrador, sin cambiar nada: reglas de la factura, ejercicio,
    /// serie, cliente y cuentas del asiento.
    /// </summary>
    public async Task<IReadOnlyList<Incidencia>> ComprobarAsync(FacturaVenta f)
    {
        var r = new List<Incidencia>();
        var ejercicio = await _sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio);
        if (ejercicio is null)
        {
            r.Add(new("Facturas.SinEjercicio", "El ejercicio de la factura no existe."));
            return r;
        }
        var abierto = await _sociedades.ComprobarEjercicioAbiertoAsync(ejercicio.Contexto);
        if (!abierto.EsCorrecto) r.Add(new(abierto.Error!.Codigo, abierto.Error.Mensaje));
        var numerada = f.NumeroFactura is not null;
        if (!numerada && await VeriFactuActivoAsync())
            r.Add(new("Facturas.VeriFactuNoDisponible", "VeriFactu está activado, pero aún no está disponible en esta versión, así que no se puede validar sin dejar la factura fuera de la AEAT. Desactívalo en Configuración › Parámetros y tablas › Configuración general para seguir validando sin VeriFactu."));
        var configuracion = await new LectorConfiguracionEjercicio(_bd).ObtenerAsync(f.IdSociedadEjercicio);
        if (string.IsNullOrWhiteSpace(configuracion?.DireccionEmisor))
            r.Add(new("Facturas.EmisorSinDomicilio", $"La empresa {ejercicio.RazonSocial} no tiene domicilio fiscal, que es obligatorio en la factura. Escríbelo en Configuración › Parámetros y tablas › Empresas."));

        var (serie, ultimaFecha) = await _bd.EjecutarAsync(async s =>
            (await RepositorioFacturas.SerieAsync(s, f.IdSerie), await RepositorioFacturas.UltimaFechaSerieAsync(s, f.IdSerie, f.IdFactura)));
        var fecha = (f.FechaFactura ?? _reloj.Ahora).Date;
        var conFecha = f.Clonar();
        conFecha.FechaFactura = fecha;
        if (numerada)
        {
            // Ya tiene número: su fecha debe seguir el orden de la serie, sin adelantar a la anterior ni pasar a la siguiente.
            var (anterior, siguiente) = await _bd.EjecutarAsync(s => RepositorioFacturas.FechasVecinasAsync(s, f.IdSerie, f.NumeroFactura!));
            ultimaFecha = anterior;
            conFecha.Estado = EstadoFactura.Borrador;
            if (siguiente is { } sig && fecha > sig.Date)
                r.Add(new("Facturas.FechaPosteriorSerie", $"La fecha de factura ({fecha:dd/MM/yyyy}) es posterior a la de la siguiente factura numerada de la serie ({sig:dd/MM/yyyy}). Usa esa fecha o una anterior.", "FechaFactura"));
        }
        r.AddRange(ReglasFactura.ValidarParaEmision(conFecha, ultimaFecha));

        if (fecha.Year != ejercicio.Ejercicio)
            r.Add(new("Facturas.FechaFueraDeEjercicio", $"La fecha de factura ({fecha:dd/MM/yyyy}) no es del ejercicio {ejercicio.Ejercicio}. Usa una fecha de {ejercicio.Ejercicio} o crea la factura en el ejercicio que corresponde.", "FechaFactura"));
        if (serie is null || serie.IdSociedadEjercicio != f.IdSociedadEjercicio)
            r.Add(new("Facturas.SerieNoValida", "La serie de la factura no es de su ejercicio. Elige otra en la cabecera.", "IdSerie"));
        else if (serie.EsRectificativa != f.EsRectificativa)
            r.Add(new("Facturas.SerieNoValida", f.EsRectificativa ? "Una rectificativa se numera en la serie de rectificativas." : "La serie de rectificativas solo numera rectificativas. Elige una serie normal.", "IdSerie"));
        else if (serie.Agotada && !numerada)
            r.Add(new("Facturas.SerieAgotada", $"La serie {serie.Prefijo.Trim()} ha llegado al final de su rango ({serie.RangoHasta}). Amplía el rango o crea otra serie en Parámetros y tablas › Series de facturación.", "IdSerie"));

        if (f.IdCliente is int idCliente && !f.EsRectificativa)
        {
            var tercero = await TerceroDeClienteAsync(idCliente);
            if (tercero is null)
                r.Add(new("Facturas.ClienteNoExiste", "El cliente de la factura ya no existe. Elige otro.", "Cliente"));
            else
            {
                if (!tercero.Activo)
                    r.Add(new("Facturas.ClienteInactivo", $"El cliente {tercero.Nombre} está dado de baja. Reactívalo en su ficha o elige otro.", "Cliente"));
                if (f.TipoFactura != "F2" && string.IsNullOrWhiteSpace(tercero.Nif))
                    r.Add(new("Facturas.ClienteSinNif", $"El cliente {tercero.Nombre} no tiene NIF, que es obligatorio en una factura completa. Escríbelo en su ficha.", "Cliente", Accion: "AbrirFichaCliente"));
                if (string.IsNullOrWhiteSpace(tercero.RazonSocial) && string.IsNullOrWhiteSpace(tercero.Nombre))
                    r.Add(new("Facturas.ClienteSinNombre", "El cliente no tiene nombre ni razón social. Complétalo en su ficha.", "Cliente", Accion: "AbrirFichaCliente"));
            }
        }

        var (_, cuentas) = await _contabilizador.ResolverAsync(f, ejercicio.Contexto);
        r.AddRange(cuentas);
        return r;
    }

    /// <summary>
    /// Valida un borrador: comprueba todo a la vez; si solo hay avisos y no se aceptan, no hace nada. Después, en una
    /// transacción, reserva el número de la serie, congela emisor y cliente, y aplica el paso fiscal; si es una
    /// rectificativa, su original pasa a Rectificada en la misma transacción. Por último genera el asiento.
    /// </summary>
    public async Task<ResultadoValidacion> ValidarAsync(int idFactura, int versionLeida, Actor actor, bool aceptarAvisos = false)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasValidar))
            return new ResultadoValidacion(null, [new("Seguridad.SinPermiso", "No tienes permiso para validar facturas. Pídeselo a quien administra los permisos.")]);
        var f = await ObtenerAsync(idFactura);
        if (f is null) return new ResultadoValidacion(null, [new("Facturas.NoExiste", "La factura ya no existe: otro operador la ha borrado.")]);
        if (f.Version != versionLeida)
            return new ResultadoValidacion(null, [new("Facturas.Conflicto", "Otro operador ha cambiado este borrador después de abrirlo. Vuelve a abrirlo y repite la validación.")]);

        var incidencias = await ComprobarAsync(f);
        if (incidencias.Any(i => !i.EsAviso) || (incidencias.Count > 0 && !aceptarAvisos))
            return new ResultadoValidacion(null, incidencias);

        var ejercicio = (await _sociedades.ObtenerEjercicioAsync(f.IdSociedadEjercicio))!;
        var config = await new LectorConfiguracionEjercicio(_bd).ObtenerAsync(f.IdSociedadEjercicio);
        InstantaneaCliente cliente;
        if (f.EsRectificativa && f.IdFacturaRectificada is int idOriginalInst)
            cliente = (await ObtenerAsync(idOriginalInst))?.Cliente ?? InstantaneaCliente.Vacia;
        else
            cliente = Instantaneas.DeTercero((await TerceroDeClienteAsync(f.IdCliente!.Value))!);

        FacturaVenta validada;
        try
        {
            validada = await _bd.EnTransaccionAsync(async s =>
            {
                var actual = await RepositorioFacturas.CargarAsync(s, idFactura);
                if (actual is null || !actual.EsBorrador || actual.Version != versionLeida)
                    throw new ConflictoConcurrenciaException(new Error("Facturas.Conflicto", "Otro operador ha cambiado o validado este borrador mientras lo validabas.",
                        f.Rotulo, null, "Vuelve a abrirlo y comprueba su estado."));
                int? numero = null;
                Serie? serie = null;
                for (var intento = 0; intento < IntentosNumeracion && numero is null; intento++)
                {
                    serie = await RepositorioFacturas.SerieAsync(s, actual.IdSerie);
                    if (serie is null || serie.Agotada) break;
                    numero = await RepositorioFacturas.ReservarNumeroAsync(s, serie);
                }
                if (numero is null || serie is null)
                    throw new BloqueoException(new Error("Facturas.SinNumero", "No he podido reservar el número de la serie.", f.Rotulo,
                        serie is { Agotada: true } ? "La serie ha llegado al final de su rango." : "Otras validaciones de la misma serie la tenían ocupada.",
                        "Vuelve a validar en unos segundos."));

                var ahora = _reloj.Ahora;
                actual.NumeroFactura = ReglasFactura.ComponerNumero(serie, numero.Value, await RepositorioFacturas.UltimoNumeroEmitidoAsync(s, serie.IdSerie));
                actual.FechaFactura = (actual.FechaFactura ?? ahora).Date;
                actual.FechaHoraEmision = ahora;
                actual.Cliente = cliente;
                actual.NifEmisor = config?.NifEmisor;
                actual.NombreEmisor = config?.NombreEmisor;
                actual.DireccionEmisor = config?.DireccionEmisor;
                actual.CodigoPostalEmisor = config?.CodigoPostalEmisor;
                actual.PoblacionEmisor = config?.PoblacionEmisor;
                actual.ProvinciaEmisor = config?.ProvinciaEmisor;
                actual.Estado = EstadoFactura.PendienteValidar;
                actual.Estado = await _pasoFiscal.AlEmitirAsync(s, actual);
                actual.Anotar(ahora, actor.Nombre, $"Validada con el número {actual.NumeroFactura} ({_pasoFiscal.Nombre})");
                await RepositorioFacturas.EmitirAsync(s, actual);
                actual.Version++;

                if (actual.IdFacturaRectificada is int idOriginal && actual.Estado == EstadoFactura.Validada)
                {
                    var original = await RepositorioFacturas.CargarAsync(s, idOriginal);
                    original?.Anotar(ahora, actor.Nombre, $"Rectificada por {actual.NumeroFactura}");
                    if (original is null || !await RepositorioFacturas.CambiarEstadoAsync(s, idOriginal, EstadoFactura.RectificacionPendiente, EstadoFactura.Rectificada, original.Historial))
                        throw new ConflictoConcurrenciaException(new Error("Facturas.OriginalNoPendiente", "La factura que se rectifica ya no está pendiente de rectificación.",
                            original?.Rotulo, "Otro operador la ha cambiado.", "Revisa la factura original antes de validar la rectificativa."));
                }
                return actual;
            });
        }
        catch (ExcepcionDatos ex)
        {
            return new ResultadoValidacion(null, [new(ex.Error.Codigo, ex.Error.TextoOperador())]);
        }

        // La factura ya está numerada (transacción confirmada): lo que falle a partir de aquí no la deja sin validar,
        // solo sin asiento, y se puede volver a contabilizar.
        IReadOnlyList<Error>? errorAsiento;
        try
        {
            var asiento = await _contabilizador.ContabilizarAsync(validada, actor);
            errorAsiento = asiento.EsCorrecto ? null : asiento.Errores;
        }
        catch (ExcepcionDatos ex)
        {
            errorAsiento = [ex.Error];
        }
        FacturaVenta final;
        try { final = await ObtenerAsync(validada.IdFactura) ?? validada; }
        catch (ExcepcionDatos) { final = validada; }
        return new ResultadoValidacion(final, incidencias, errorAsiento);
    }

    /// <summary>Vuelve a generar el asiento de una factura validada (por ejemplo, tras corregir una cuenta), con su registro del libro de IVA. No duplica.</summary>
    /// <param name="avisar">Recibe el aviso de una factura de un trimestre cerrado, que va al libro del trimestre activo.</param>
    public async Task<Resultado<int>> ContabilizarAsync(int idFactura, Actor actor, Action<string>? avisar = null)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasValidar)) return SinPermiso<int>("contabilizar facturas", FacturaVenta.RotuloBorrador(idFactura));
        var f = await ObtenerAsync(idFactura);
        if (f is null) return Resultado<int>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura)));
        return await _contabilizador.ContabilizarAsync(f, actor, avisar: avisar);
    }

    // ---------- duplicar y rectificar ----------

    /// <summary>
    /// Duplica una factura como borrador nuevo del ejercicio de trabajo, con su serie normal predeterminada: copia
    /// cliente, descripción, condiciones y líneas; no copia número, fechas de emisión, instantánea, asiento, historial
    /// ni el vínculo de rectificación. Una rectificativa se duplica como factura normal (F1) en positivo.
    /// </summary>
    public async Task<Resultado<FacturaVenta>> DuplicarAsync(int idFactura, ContextoSociedadEjercicio trabajo, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<FacturaVenta>("crear facturas", FacturaVenta.RotuloBorrador(idFactura));
        var origen = await ObtenerAsync(idFactura);
        if (origen is null) return Resultado<FacturaVenta>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura)));
        var nuevo = await NuevoBorradorAsync(trabajo);
        if (!nuevo.EsCorrecto) return nuevo;
        var f = nuevo.Valor;
        f.IdCliente = origen.IdCliente;
        f.Descripcion = origen.Descripcion;
        f.NotasInternas = origen.NotasInternas;
        f.FormaDePago = origen.FormaDePago ?? f.FormaDePago;
        f.DiaDePago = origen.DiaDePago;
        f.TipoRetencion = origen.TipoRetencion;
        f.FechaInicioValidez = origen.FechaInicioValidez ?? f.FechaInicioValidez;
        f.FechaFinValidez = origen.FechaFinValidez ?? f.FechaFinValidez;
        f.TipoFactura = origen.EsRectificativa ? "F1" : origen.TipoFactura;
        foreach (var l in origen.Lineas) f.AnadirLinea(l.Copiar(invertir: origen.EsRectificativa));
        var r = await GuardarBorradorAsync(f, actor);
        if (r.EsCorrecto) await AnotarAsync(r.Valor.IdFactura, actor, $"Duplicado de {origen.Rotulo}");
        return r.EsCorrecto ? Resultado<FacturaVenta>.Correcto((await ObtenerAsync(r.Valor.IdFactura))!) : r;
    }

    /// <summary>
    /// Crea el borrador de la rectificativa completa 1:1 (R4, o R5 si la original es simplificada; por diferencias) en la
    /// serie de rectificativas del ejercicio de trabajo, con las líneas de la original en negativo y su misma instantánea
    /// de cliente. La original pasa a «Rectificación pendiente» en la misma transacción: si algo falla, no cambia nada.
    /// </summary>
    public async Task<Resultado<FacturaVenta>> CrearRectificativaAsync(int idFactura, string motivo, ContextoSociedadEjercicio trabajo, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasRectificar)) return SinPermiso<FacturaVenta>("rectificar facturas", FacturaVenta.RotuloBorrador(idFactura));
        motivo = motivo?.Trim() ?? "";
        if (motivo.Length == 0)
            return Resultado<FacturaVenta>.Fallo(new Error("Facturas.SinMotivo", "Falta el motivo de la rectificación.", FacturaVenta.RotuloBorrador(idFactura), null,
                "Explica por qué se anula la factura original."));
        if (motivo.Length > ReglasFactura.LongitudMotivo)
            return Resultado<FacturaVenta>.Fallo(new Error("Facturas.MotivoLargo", $"El motivo tiene más de {ReglasFactura.LongitudMotivo} caracteres.", FacturaVenta.RotuloBorrador(idFactura), null, "Acórtalo."));
        var original = await ObtenerAsync(idFactura);
        if (original is null) return Resultado<FacturaVenta>.Fallo(new Error("Facturas.NoExiste", "La factura ya no existe.", FacturaVenta.RotuloBorrador(idFactura)));
        if (original.Estado != EstadoFactura.Validada || original.EsRectificativa)
            return Resultado<FacturaVenta>.Fallo(new Error("Facturas.NoRectificable",
                original.EsRectificativa ? "Una rectificativa no se rectifica." : $"Solo se rectifica una factura validada y esta está {TextosEstado.Texto(original.Estado).ToLowerInvariant()}.",
                original.Rotulo, null, original.Estado == EstadoFactura.RectificacionPendiente ? "Abre el borrador de su rectificativa en el listado." : null));
        var abierto = await _sociedades.ComprobarEjercicioAbiertoAsync(trabajo);
        if (!abierto.EsCorrecto) return Resultado<FacturaVenta>.Fallo(abierto.Errores);
        var serie = await _bd.EjecutarAsync(s => RepositorioFacturas.SerieRectificativaAsync(s, trabajo.IdSociedadEjercicio, trabajo.Ejercicio));
        if (!serie.EsCorrecto) return Resultado<FacturaVenta>.Fallo(serie.Errores);
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<FacturaVenta>("crear facturas", original.Rotulo);

        var hoy = _reloj.Ahora.Date;
        var f = new FacturaVenta
        {
            IdSociedadEjercicio = trabajo.IdSociedadEjercicio,
            IdSerie = serie.Valor.IdSerie,
            IdCliente = original.IdCliente,
            IdContrato = original.IdContrato,
            Cliente = original.Cliente,
            FechaFactura = hoy.Year == trabajo.Ejercicio ? hoy : null,
            FechaOperacion = original.FechaOperacion,
            FechaInicioValidez = original.FechaInicioValidez,
            FechaFinValidez = original.FechaFinValidez,
            FechaCreacion = _reloj.Ahora,
            FormaDePago = original.FormaDePago,
            DiaDePago = original.DiaDePago,
            Descripcion = Recortar($"Rectificación de la factura {original.NumeroFactura}: {original.Descripcion}".Trim(), 4000),
            TipoRetencion = original.TipoRetencion,
            TipoFactura = ReglasFactura.TipoRectificativaDe(original.TipoFactura),
            TipoRectificativa = "I",
            IdFacturaRectificada = original.IdFactura,
            NumeroFacturaRectificada = original.NumeroFactura,
            MotivoRectificacion = motivo
        };
        foreach (var l in original.Lineas) f.AnadirLinea(l.Copiar(invertir: true));

        try
        {
            var id = await _bd.EnTransaccionAsync(async s =>
            {
                var ahora = _reloj.Ahora;
                var o = await RepositorioFacturas.CargarAsync(s, idFactura);
                o!.Anotar(ahora, actor.Nombre, $"Rectificación pendiente: se crea el borrador de su rectificativa. Motivo: {motivo}");
                if (!await RepositorioFacturas.CambiarEstadoAsync(s, idFactura, EstadoFactura.Validada, EstadoFactura.RectificacionPendiente, o.Historial))
                    throw new ConflictoConcurrenciaException(new Error("Facturas.Conflicto", "Otro operador ha cambiado la factura mientras la rectificabas.",
                        original.Rotulo, null, "Actualiza el listado y comprueba su estado."));
                f.Anotar(ahora, actor.Nombre, $"Borrador de rectificativa de {original.NumeroFactura}. Motivo: {motivo}");
                return await RepositorioFacturas.InsertarAsync(s, f);
            });
            return Resultado<FacturaVenta>.Correcto((await ObtenerAsync(id))!);
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<FacturaVenta>.Fallo(ex.Error);
        }
    }

    // ---------- cuentas del cliente ----------

    /// <summary>
    /// Asigna al cliente, en la sociedad de la factura, la cuenta de cliente o de retención con ese código. Si no existe
    /// en el ejercicio, la crea con el nombre del cliente («Retención IRPF …» para la de retención). Si el rol ya tiene
    /// una cuenta de otro ejercicio, la da de alta en este con su código.
    /// </summary>
    public async Task<Resultado<CuentaContable>> AsignarCuentaClienteAsync(int idCliente, ClaseCuentaRol clase, ContextoSociedadEjercicio contexto, string? codigo)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<CuentaContable>("cambiar cuentas de clientes", $"Cliente {idCliente}");
        var tercero = await TerceroDeClienteAsync(idCliente);
        if (tercero is null) return Resultado<CuentaContable>.Fallo(new Error("Facturas.ClienteNoExiste", "El cliente no existe.", $"Cliente {idCliente}"));
        var actuales = await _roles.CuentasAsync(tercero.IdTercero, contexto);
        var rol = actuales.De(clase);
        Resultado<CuentaContable> cuenta;
        if (string.IsNullOrWhiteSpace(codigo) && rol is { ExisteEnEjercicio: false })
            cuenta = await _cuentas.AltaEnEjercicioAsync(contexto, rol.IdIdentidadContable);
        else if (string.IsNullOrWhiteSpace(codigo))
            return Resultado<CuentaContable>.Fallo(new Error("Facturas.SinCodigo", "Escribe el código de la cuenta.", tercero.Nombre));
        else
        {
            var existente = await _cuentas.ResolverAsync(contexto, codigo);
            cuenta = existente.EsCorrecto
                ? existente
                : await _cuentas.AltaAsync(contexto, new AltaCuenta(codigo.Trim(), Recortar((clase == ClaseCuentaRol.Retencion ? "Retención IRPF " : "") + tercero.Nombre, ReglasCuenta.LongitudMaximaNombre)));
        }
        if (!cuenta.EsCorrecto) return cuenta;
        var asignada = await _roles.AsignarCuentaAsync(clase, idCliente, contexto.IdSociedad, cuenta.Valor.IdIdentidadContable);
        return asignada.EsCorrecto ? cuenta : Resultado<CuentaContable>.Fallo(asignada.Errores);
    }

    /// <summary>
    /// Código que se propone para una cuenta nueva del cliente: el siguiente libre del grupo (430 o 473) con la longitud
    /// de las cuentas que ya hay en él (7 cifras si no hay ninguna).
    /// </summary>
    public async Task<string> ProponerCodigoCuentaAsync(ClaseCuentaRol clase, ContextoSociedadEjercicio contexto)
    {
        var prefijo = clase == ClaseCuentaRol.Retencion ? "473" : "430";
        var existentes = (await _cuentas.ListarAsync(contexto, prefijo)).Where(c => c.EsImputable && c.Codigo.All(char.IsAsciiDigit)).ToList();
        var longitud = existentes.Count == 0 ? 7 : existentes.GroupBy(c => c.Codigo.Length).OrderByDescending(g => g.Count()).First().Key;
        var mismas = existentes.Where(c => c.Codigo.Length == longitud).Select(c => long.Parse(c.Codigo, System.Globalization.CultureInfo.InvariantCulture)).ToList();
        var siguiente = mismas.Count == 0 ? long.Parse(prefijo.PadRight(longitud - 1, '0') + "1", System.Globalization.CultureInfo.InvariantCulture) : mismas.Max() + 1;
        return siguiente.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------- utilidades ----------

    /// <summary>El tercero del cliente (con sus datos de contacto actuales); null si ya no existe.</summary>
    public async Task<Tercero?> TerceroDeClienteAsync(int idCliente)
    {
        var idTercero = await _bd.EjecutarAsync(s => Instantaneas.TerceroDelClienteAsync(s, idCliente));
        return idTercero is int id ? await _terceros.ObtenerAsync(id) : null;
    }

    private Task AnotarAsync(int idFactura, Actor actor, string accion) =>
        _bd.EjecutarAsync(async s =>
        {
            var f = await RepositorioFacturas.CargarAsync(s, idFactura);
            if (f is null) return 0;
            f.Anotar(_reloj.Ahora, actor.Nombre, accion);
            return await s.EjecutarAsync("UPDATE Facturas SET Historial = ? WHERE IdFactura = ?", f.Historial, idFactura);
        });

    private static Error NoEsBorrador(FacturaVenta f, string verbo) =>
        new("Facturas.NoEsBorrador", $"No se puede {verbo} la factura {f.Rotulo}: está {TextosEstado.Texto(f.Estado).ToLowerInvariant()} y una factura numerada no cambia.",
            f.Rotulo, "Tras validarla solo se pueden cambiar sus notas internas.", "Si hay que corregirla, rectifícala o duplícala.");

    private static Resultado<T> SinPermiso<T>(string que, string registro) =>
        Resultado<T>.Fallo(new Error("Seguridad.SinPermiso", $"No tienes permiso para {que}.", registro, "Tu grupo de operadores no tiene esa acción.",
            "Pídeselo a quien administra los permisos."));

    private static string Recortar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}
