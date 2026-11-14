using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Permisos;

namespace ProConta.Facturacion.Ventas;

/// <summary>Resultado de guardar una venta: la guardada o, si no se ha guardado, por qué (con el campo que corregir).</summary>
public sealed record ResultadoVenta(VentaPorFacturar? Venta, IReadOnlyList<Incidencia> Incidencias)
{
    public bool Guardada => Venta is not null;
}

/// <summary>Factura creada por «Facturar ventas…».</summary>
public sealed record FacturaDeVentas(int IdFactura, string Rotulo, string Cliente, string Piso, DateTime Mes, int Ventas, decimal Total, bool Validada);

/// <summary>Resultado de «Facturar ventas…»: las facturas creadas y lo que no se ha podido facturar (no detiene a los demás grupos).</summary>
public sealed record ResultadoFacturarVentas(IReadOnlyList<FacturaDeVentas> Facturas, IReadOnlyList<string> Incidencias)
{
    public bool SinIncidencias => Incidencias.Count == 0;
}

/// <summary>
/// Servicio de las ventas por facturar (facturacion/reservas): alta y edición manual, anulación, borrado de las manuales y
/// «Facturar ventas…». Los permisos son los de las facturas: FACTURAS.EDITAR (validar las creadas lo comprueba el servicio
/// de facturas con FACTURAS.VALIDAR).
/// </summary>
public sealed class ServicioVentas
{
    private readonly IBaseDatos _bd;
    private readonly IServicioSociedades _sociedades;
    private readonly ServicioFacturas _facturas;
    private readonly CatalogoConceptos _conceptos;
    private readonly IAutorizadorAcciones _autorizador;
    private readonly IReloj _reloj;

    public ServicioVentas(IBaseDatos bd, IServicioSociedades sociedades, ServicioFacturas facturas, CatalogoConceptos conceptos, IAutorizadorAcciones? autorizador = null, IReloj? reloj = null)
    {
        _bd = bd;
        _sociedades = sociedades;
        _facturas = facturas;
        _conceptos = conceptos;
        _autorizador = autorizador ?? AutorizadorTodo.Instancia;
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    public CatalogoConceptos Conceptos => _conceptos;

    // ---------- lectura ----------

    public Task<VentaPorFacturar?> ObtenerAsync(int id) => _bd.EjecutarAsync(s => RepositorioVentas.CargarAsync(s, id));

    public Task<IReadOnlyList<VentaPorFacturar>> ListarAsync(string idSociedad, DateTime? desde = null, DateTime? hasta = null) =>
        _bd.EjecutarAsync(s => RepositorioVentas.ListarAsync(s, idSociedad, desde, hasta));

    /// <summary>
    /// Orígenes de la sociedad. Si aún no tiene ninguno, crea los habituales (Airbnb, Booking y Directa) sin cliente, para que
    /// el operador les ponga el suyo en Parámetros y tablas › Orígenes de venta.
    /// </summary>
    public Task<IReadOnlyList<OrigenVenta>> OrigenesAsync(string idSociedad) =>
        _bd.EnTransaccionAsync(async s =>
        {
            var lista = await RepositorioVentas.OrigenesAsync(s, idSociedad);
            if (lista.Count > 0) return lista;
            foreach (var o in OrigenesIniciales(idSociedad)) await RepositorioVentas.InsertarOrigenAsync(s, o);
            return await RepositorioVentas.OrigenesAsync(s, idSociedad);
        });

    public static IReadOnlyList<OrigenVenta> OrigenesIniciales(string idSociedad) =>
    [
        new(0, idSociedad, "AIRBNB", "Airbnb", null, "Airbnb", false, true),
        new(0, idSociedad, "BOOKING", "Booking", null, "Booking.com;Booking", false, true),
        new(0, idSociedad, "DIRECTA", "Directa", null, null, true, true),
    ];

    public Task<IReadOnlyList<AlojamientoAvaibook>> AlojamientosAsync(string idSociedad) =>
        _bd.EjecutarAsync(s => RepositorioVentas.AlojamientosAsync(s, idSociedad));

    /// <summary>Activos propios de la sociedad (no ajenos), para elegir el piso de una venta.</summary>
    public Task<IReadOnlyList<ActivoVenta>> ActivosAsync(string idSociedad) =>
        _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdActivo, Nombre, Codigo, IdCentroCoste FROM Activos WHERE IdSociedad = ? AND EsAjeno = False ORDER BY Nombre",
            f => new ActivoVenta(f.Entero("IdActivo"), (f.Texto("Nombre") ?? f.Texto("Codigo") ?? $"Activo {f.Entero("IdActivo")}").Trim(), f.EnteroNulo("IdCentroCoste")), idSociedad));

    /// <summary>Nombre de cada cliente (el de su tercero).</summary>
    public Task<IReadOnlyDictionary<int, string>> NombresClientesAsync() =>
        _bd.EjecutarAsync(async s =>
        {
            var filas = await s.ConsultarAsync("SELECT c.IdCliente, t.Nombre FROM Clientes AS c INNER JOIN Terceros AS t ON c.IdTercero = t.IdTercero",
                f => (f.Entero("IdCliente"), (f.Texto("Nombre") ?? "").Trim()));
            return (IReadOnlyDictionary<int, string>)filas.GroupBy(x => x.Item1).ToDictionary(g => g.Key, g => g.First().Item2);
        });

    // ---------- alta y edición manual ----------

    public VentaPorFacturar Nueva(string idSociedad, OrigenVenta? origen) => new()
    {
        IdSociedad = idSociedad,
        IdOrigenVenta = origen?.IdOrigenVenta ?? 0,
        FechaReferencia = _reloj.Ahora.Date,
        Cantidad = 1m,
        Fuente = FuenteVenta.Manual,
    };

    /// <summary>Todo lo que impediría guardar la venta tal como está.</summary>
    public async Task<IReadOnlyList<Incidencia>> ComprobarAsync(VentaPorFacturar v)
    {
        var r = new List<Incidencia>();
        var origenes = await OrigenesAsync(v.IdSociedad);
        var origen = origenes.FirstOrDefault(o => o.IdOrigenVenta == v.IdOrigenVenta);
        if (origen is null) r.Add(new("Ventas.SinOrigen", "Elige el origen de la venta (Airbnb, Booking, Directa…).", "IdOrigenVenta"));
        else if (origen.IdCliente is null && v.IdCliente is null)
            r.Add(new("Ventas.SinCliente", $"El origen «{origen.Nombre}» no tiene cliente: elige el cliente al que se factura esta venta (o pónselo al origen en Parámetros y tablas › Orígenes de venta).", "IdCliente"));
        if (v.FechaReferencia == default) r.Add(new("Ventas.SinFecha", "Escribe la fecha de la venta (en una reserva, la de entrada): decide el mes en que se factura.", "FechaReferencia"));
        if (v.FechaFin is DateTime fin && fin.Date < v.FechaReferencia.Date)
            r.Add(new("Ventas.FechasAlReves", "La fecha final es anterior a la de entrada. Corrígela.", "FechaFin"));
        if (v.Cantidad <= 0m) r.Add(new("Ventas.CantidadNoValida", "La cantidad (noches) debe ser mayor que cero.", "Cantidad"));
        if (v.Importe == 0m) r.Add(new("Ventas.SinImporte", "Escribe el importe que se factura (lo que paga el huésped, comisión incluida).", "Importe"));
        if (v.IdConcepto is null) r.Add(new("Ventas.SinConcepto", "Elige el concepto: da el nombre, la cuenta de venta y el IVA de la línea de factura.", "IdConcepto"));
        if (!string.IsNullOrWhiteSpace(v.CausaExencion) && v.CausaExencion.Trim() is not ("E1" or "E2" or "E3" or "E4" or "E5" or "E6"))
            r.Add(new("Ventas.CausaNoValida", "La causa de exención debe ser E1 a E6, o quedar vacía si la venta no está exenta.", "CausaExencion"));
        return r;
    }

    /// <summary>Guarda una venta nueva o pendiente (concurrencia optimista). Una venta en borrador, facturada o anulada no se edita.</summary>
    public async Task<ResultadoVenta> GuardarAsync(VentaPorFacturar venta, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar))
            return new(null, [new("Seguridad.SinPermiso", "No tienes permiso para editar ventas por facturar. Pídeselo a quien administra los permisos.")]);
        var incidencias = await ComprobarAsync(venta);
        if (incidencias.Count > 0) return new(null, incidencias);
        try
        {
            var id = await _bd.EnTransaccionAsync(async s =>
            {
                var v = venta.Clonar();
                v.FechaModificacion = _reloj.Ahora;
                if (v.EsNueva)
                {
                    v.FechaAlta = _reloj.Ahora;
                    v.OperadorAlta = actor.Nombre;
                    return await RepositorioVentas.InsertarAsync(s, v);
                }
                var actual = await RepositorioVentas.CargarAsync(s, v.IdVentaPorFacturar)
                    ?? throw new ConflictoConcurrenciaException(new Error("Ventas.NoExiste", "La venta ya no existe: otro operador la ha borrado.", v.Rotulo));
                if (!actual.EsEditable)
                    throw new ConflictoConcurrenciaException(new Error("Ventas.NoEditable", $"La venta está {VentaPorFacturar.TextoEstado(actual.Estado).ToLowerInvariant()} y ya no se edita.", v.Rotulo));
                await RepositorioVentas.ActualizarAsync(s, v);
                return v.IdVentaPorFacturar;
            });
            return new(await ObtenerAsync(id), []);
        }
        catch (ExcepcionDatos ex)
        {
            return new(null, [new(ex.Error.Codigo, ex.Error.Codigo.StartsWith("Ventas.", StringComparison.Ordinal) ? ex.Error.Mensaje
                : ex is ConflictoConcurrenciaException ? "Otro operador ha cambiado esta venta después de abrirla. Vuelve a abrirla y repite el cambio."
                : ex.Error.TextoOperador())]);
        }
    }

    /// <summary>Anula una venta pendiente (una reserva cancelada): ya no se factura. Se puede reactivar.</summary>
    public Task<Resultado> AnularAsync(int id, int versionLeida, string? motivo, Actor actor) => CambiarAnuladaAsync(id, versionLeida, true, motivo, actor);

    public Task<Resultado> ReactivarAsync(int id, int versionLeida, Actor actor) => CambiarAnuladaAsync(id, versionLeida, false, null, actor);

    private async Task<Resultado> CambiarAnuladaAsync(int id, int versionLeida, bool anular, string? motivo, Actor actor)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<bool>(anular ? "anular ventas" : "reactivar ventas", VentaPorFacturar.RotuloDe(id));
        try
        {
            return await _bd.EnTransaccionAsync(async s =>
            {
                var v = await RepositorioVentas.CargarAsync(s, id);
                if (v is null) return Resultado.Fallo(new Error("Ventas.NoExiste", "La venta ya no existe.", VentaPorFacturar.RotuloDe(id), "Otro operador la ha borrado.", "Actualiza la lista."));
                if (v.Version != versionLeida) return Resultado.Fallo(new Error("Ventas.Conflicto", "Otro operador ha cambiado esta venta.", v.Rotulo, null, "Actualiza la lista y repite."));
                if (anular && !v.EsEditable)
                    return Resultado.Fallo(new Error("Ventas.NoAnulable", $"Solo se anula una venta pendiente; esta está {VentaPorFacturar.TextoEstado(v.Estado).ToLowerInvariant()}.", v.Rotulo, null,
                        v.Estado == EstadoVenta.EnBorrador ? "Borra antes el borrador de su factura (vuelve a pendiente) y anúlala después." : "Para corregir una venta facturada, rectifica su factura."));
                if (!anular && v.Estado != EstadoVenta.Anulada) return Resultado.Correcto();
                v.Anulada = anular;
                v.MotivoAnulacion = anular ? (string.IsNullOrWhiteSpace(motivo) ? $"Anulada por {actor.Nombre}" : motivo.Trim()) : null;
                v.FechaModificacion = _reloj.Ahora;
                await RepositorioVentas.ActualizarAsync(s, v);
                return Resultado.Correcto();
            });
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado.Fallo(ex.Error);
        }
    }

    /// <summary>Borra una venta manual pendiente o anulada. Las importadas no se borran (volverían a entrar): se anulan.</summary>
    public async Task<Resultado> EliminarAsync(int id, int versionLeida)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<bool>("eliminar ventas", VentaPorFacturar.RotuloDe(id));
        try
        {
            return await _bd.EnTransaccionAsync(async s =>
            {
                var v = await RepositorioVentas.CargarAsync(s, id);
                if (v is null) return Resultado.Correcto();
                if (v.Version != versionLeida) return Resultado.Fallo(new Error("Ventas.Conflicto", "Otro operador ha cambiado esta venta.", v.Rotulo, null, "Actualiza la lista y repite."));
                if (v.Fuente != FuenteVenta.Manual)
                    return Resultado.Fallo(new Error("Ventas.Importada", $"La venta viene de {(v.Fuente == FuenteVenta.Avaibook ? "Avaibook" : "el ERP")} y no se borra: volvería a entrar en la próxima importación.", v.Rotulo, null, "Anúlala si no se ha de facturar."));
                if (v.Estado is EstadoVenta.EnBorrador or EstadoVenta.Facturada)
                    return Resultado.Fallo(new Error("Ventas.ConFactura", $"La venta está en {v.RotuloFactura} y no se borra.", v.Rotulo, null, "Borra antes el borrador (vuelve a pendiente)."));
                await RepositorioVentas.BorrarAsync(s, id);
                return Resultado.Correcto();
            });
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado.Fallo(ex.Error);
        }
    }

    // ---------- facturación ----------

    /// <summary>Último día del último mes cerrado (el mes anterior al de hoy): la fecha que propone «Facturar ventas…».</summary>
    public DateTime HastaPropuesto => new DateTime(_reloj.Ahora.Year, _reloj.Ahora.Month, 1).AddDays(-1);

    private sealed record Datos(IReadOnlyDictionary<int, OrigenVenta> Origenes, IReadOnlyDictionary<int, string> Clientes, IReadOnlyDictionary<int, ActivoVenta> Activos,
        IReadOnlyDictionary<int, Concepto> Conceptos, SociedadEjercicio? Ejercicio);

    private async Task<Datos> DatosAsync(string idSociedad)
    {
        var hoy = _reloj.Ahora.Date;
        var ejercicio = (await _sociedades.ListarEjerciciosAsync(idSociedad)).FirstOrDefault(e => e.Ejercicio == hoy.Year);
        var conceptos = ejercicio is null ? [] : await _conceptos.ListarAsync(ejercicio.Contexto);
        return new Datos((await OrigenesAsync(idSociedad)).ToDictionary(o => o.IdOrigenVenta), await NombresClientesAsync(),
            (await ActivosAsync(idSociedad)).ToDictionary(a => a.IdActivo), conceptos.ToDictionary(c => c.IdConcepto), ejercicio);
    }

    /// <summary>
    /// Qué haría «Facturar ventas…» hasta <paramref name="hasta"/>: los grupos (una factura cada uno) y las ventas que no se
    /// pueden facturar con su motivo. No cambia nada.
    /// </summary>
    public async Task<IReadOnlyList<GrupoVentas>> PrevisualizarAsync(string idSociedad, DateTime hasta, IReadOnlySet<int>? soloVentas = null)
    {
        var d = await DatosAsync(idSociedad);
        var pendientes = await _bd.EjecutarAsync(s => RepositorioVentas.PendientesAsync(s, idSociedad, hasta));
        return AgrupadorVentas.Agrupar(soloVentas is null ? pendientes : pendientes.Where(v => soloVentas.Contains(v.IdVentaPorFacturar)), d.Origenes, d.Clientes, d.Activos, d.Conceptos);
    }

    /// <summary>
    /// «Facturar ventas…» (facturacion/reservas 2026-11-14 11:45): una factura por cliente, mes de la fecha de referencia y
    /// piso con las ventas pendientes hasta <paramref name="hasta"/>, con fecha de hoy, en la serie predeterminada del
    /// ejercicio de hoy. Cada factura y el enlace de sus ventas se guardan en una sola transacción: si otra sesión ya ha
    /// facturado alguna de sus ventas, no se crea esa factura. Con <paramref name="validar"/>, cada factura pasa después por
    /// la validación normal; si no se puede validar, queda en borrador y se avisa.
    /// </summary>
    public async Task<Resultado<ResultadoFacturarVentas>> FacturarAsync(string idSociedad, DateTime hasta, bool validar, Actor actor, IReadOnlySet<int>? soloVentas = null)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<ResultadoFacturarVentas>("facturar ventas", null);
        var d = await DatosAsync(idSociedad);
        var hoy = _reloj.Ahora.Date;
        if (d.Ejercicio is null)
            return Resultado<ResultadoFacturarVentas>.Fallo(new Error("Ventas.SinEjercicio", $"La sociedad no tiene el ejercicio {hoy.Year}, que es donde van las facturas de hoy.", null, null, "Créalo en Parámetros y tablas › Ejercicios."));
        if (d.Ejercicio.Cerrado)
            return Resultado<ResultadoFacturarVentas>.Fallo(new Error("Ventas.EjercicioCerrado", $"El ejercicio {hoy.Year} está cerrado y no admite facturas nuevas."));

        var pendientes = await _bd.EjecutarAsync(s => RepositorioVentas.PendientesAsync(s, idSociedad, hasta));
        var grupos = AgrupadorVentas.Agrupar(soloVentas is null ? pendientes : pendientes.Where(v => soloVentas.Contains(v.IdVentaPorFacturar)), d.Origenes, d.Clientes, d.Activos, d.Conceptos);
        var incidencias = grupos.Where(g => !g.Facturable)
            .Select(g => $"{g.Ventas[0].Rotulo} ({g.Ventas[0].FechaReferencia:dd/MM/yyyy}, {g.Ventas[0].Importe:N2} €): {g.Problema}").ToList();
        var creadas = new List<FacturaDeVentas>();

        foreach (var g in grupos.Where(g => g.Facturable))
        {
            var borrador = await _facturas.NuevoBorradorAsync(d.Ejercicio.Contexto);
            if (!borrador.EsCorrecto)
            {
                incidencias.Add($"{g.Cliente} · {g.Piso} · {g.Clave!.PrimerDia:MM/yyyy}: {string.Join(" ", borrador.Errores.Select(e => e.TextoOperador()))}");
                continue;
            }
            var propuesta = await _facturas.PropuestaClienteAsync(g.Clave!.IdCliente, idSociedad);
            FacturaVenta f = borrador.Valor;
            f.IdCliente = g.Clave.IdCliente;
            f.FechaFactura = hoy;
            f.FechaOperacion = g.Clave.UltimoDia < hoy ? g.Clave.UltimoDia : hoy;
            f.FechaInicioValidez = g.Clave.PrimerDia;
            f.FechaFinValidez = g.Clave.UltimoDia;
            f.Descripcion = AgrupadorVentas.DescripcionFactura(g, d.Origenes);
            if (propuesta?.FormaDePago is byte forma) f.FormaDePago = forma;
            if (propuesta?.DiaDePago is byte dia) f.DiaDePago = dia;
            f.TipoRetencion = 0m;
            foreach (var v in g.Ventas)
                f.AnadirLinea(AgrupadorVentas.Linea(v, d.Conceptos[v.IdConcepto!.Value], v.IdActivo is int a ? d.Activos[a] : null, d.Origenes.GetValueOrDefault(v.IdOrigenVenta)));
            var ahora = _reloj.Ahora;
            f.Anotar(ahora, actor.Nombre, $"Creada por «Facturar ventas…» con {g.Ventas.Count} {(g.Ventas.Count == 1 ? "venta" : "ventas")} ({string.Join(", ", g.Ventas.Select(v => v.IdVentaPorFacturar))})");
            try
            {
                var idFactura = await _bd.EnTransaccionAsync(async s =>
                {
                    var id = await RepositorioFacturas.InsertarAsync(s, f);
                    foreach (var v in g.Ventas)
                        if (!await RepositorioVentas.EnlazarFacturaAsync(s, v.IdVentaPorFacturar, v.Version, id, ahora))
                            throw new ConflictoConcurrenciaException(new Error("Ventas.Conflicto", $"{v.Rotulo} ha cambiado o ya está facturada.", v.Rotulo));
                    return id;
                });
                var rotulo = FacturaVenta.RotuloBorrador(idFactura);
                var validada = false;
                if (validar)
                {
                    var leida = await _facturas.ObtenerAsync(idFactura);
                    var r = await _facturas.ValidarAsync(idFactura, leida!.Version, actor, aceptarAvisos: true);
                    validada = r.Validada;
                    if (r.Validada)
                    {
                        rotulo = r.Factura!.NumeroFactura ?? rotulo;
                        if (!r.Contabilizada)
                            incidencias.Add($"{rotulo} validada sin asiento: {string.Join(" ", (r.ErrorContabilizacion ?? []).Select(e => e.Mensaje))} Contabilízala desde Facturas.");
                    }
                    else
                        incidencias.Add($"{rotulo} ({g.Cliente} · {g.Piso}) creado pero no validado; queda como borrador. {string.Join(" ", r.Incidencias.Select(i => i.Mensaje))}");
                }
                creadas.Add(new FacturaDeVentas(idFactura, rotulo, g.Cliente, g.Piso, g.Clave.PrimerDia, g.Ventas.Count, g.Total, validada));
            }
            catch (ExcepcionDatos ex)
            {
                incidencias.Add($"{g.Cliente} · {g.Piso} · {g.Clave.PrimerDia:MM/yyyy}: no se ha creado la factura. {ex.Error.Mensaje} Actualiza la lista y vuelve a facturar si hace falta.");
            }
        }
        return Resultado<ResultadoFacturarVentas>.Correcto(new ResultadoFacturarVentas(creadas, incidencias));
    }

    private static Resultado<T> SinPermiso<T>(string que, string? registro) =>
        Resultado<T>.Fallo(new Error("Seguridad.SinPermiso", $"No tienes permiso para {que}.", registro, null, "Pídeselo a quien administra los permisos."));
}
