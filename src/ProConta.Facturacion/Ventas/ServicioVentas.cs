using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Permisos;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Ventas;

/// <summary>Huéspedes dados de alta como clientes (reglas «Factura nominal al huésped»): cuántos, a cuántas ventas se ha puesto y qué no se ha podido hacer.</summary>
public sealed record AltaHuespedes(int Altas, int Ventas, IReadOnlyList<string> Incidencias)
{
    public static readonly AltaHuespedes Nada = new(0, 0, []);
}

/// <summary>Resultado de guardar una venta: la guardada o, si no se ha guardado, por qué (con el campo que corregir).</summary>
public sealed record ResultadoVenta(VentaPorFacturar? Venta, IReadOnlyList<Incidencia> Incidencias)
{
    public bool Guardada => Venta is not null;
}

/// <summary>Factura creada por «Facturar todo».</summary>
public sealed record FacturaDeVentas(int IdFactura, string Rotulo, string Cliente, string Descripcion, int Ventas, decimal Total, bool Validada);

/// <summary>Resultado de «Facturar todo»: las facturas creadas y lo que no se ha podido facturar (no detiene a los demás grupos).</summary>
public sealed record ResultadoFacturarVentas(IReadOnlyList<FacturaDeVentas> Facturas, IReadOnlyList<string> Incidencias)
{
    public bool SinIncidencias => Incidencias.Count == 0;
}

/// <summary>
/// Servicio de las ventas por facturar (facturacion/reservas): alta y edición manual, anulación, borrado de las manuales y
/// «Facturar todo». Los permisos son los de las facturas: FACTURAS.EDITAR (validar las creadas lo comprueba el servicio
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
    private readonly IRepositorioTerceros? _terceros;
    private readonly ServicioRoles? _roles;

    /// <param name="terceros">Para dar de alta a los huéspedes como clientes (reglas «Factura nominal al huésped»); sin él, no se dan de alta.</param>
    /// <param name="roles">Rol de cliente y 430 de los huéspedes; sin él, no se dan de alta.</param>
    public ServicioVentas(IBaseDatos bd, IServicioSociedades sociedades, ServicioFacturas facturas, CatalogoConceptos conceptos, IAutorizadorAcciones? autorizador = null, IReloj? reloj = null,
        IRepositorioTerceros? terceros = null, ServicioRoles? roles = null)
    {
        _bd = bd;
        _sociedades = sociedades;
        _facturas = facturas;
        _conceptos = conceptos;
        _autorizador = autorizador ?? AutorizadorTodo.Instancia;
        _reloj = reloj ?? RelojSistema.Instancia;
        _terceros = terceros;
        _roles = roles;
    }

    public CatalogoConceptos Conceptos => _conceptos;

    // ---------- lectura ----------

    public Task<VentaPorFacturar?> ObtenerAsync(int id) => _bd.EjecutarAsync(s => RepositorioVentas.CargarAsync(s, id));

    public Task<IReadOnlyList<VentaPorFacturar>> ListarAsync(string idSociedad, DateTime? desde = null, DateTime? hasta = null) =>
        _bd.EjecutarAsync(s => RepositorioVentas.ListarAsync(s, idSociedad, desde, hasta));

    /// <summary>
    /// Orígenes de la sociedad. Si aún no tiene ninguno, crea los habituales (Airbnb, Booking y Directa); el concepto, el IVA
    /// y el cliente de cada uno se ponen en Parámetros y tablas › Reglas de venta.
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
        new(0, idSociedad, "AIRBNB", "Airbnb", "Airbnb", false, true),
        new(0, idSociedad, "BOOKING", "Booking", "Booking.com;Booking", false, true),
        new(0, idSociedad, "DIRECTA", "Directa", null, true, true),
    ];

    public Task<IReadOnlyList<AlojamientoAvaibook>> AlojamientosAsync(string idSociedad) =>
        _bd.EjecutarAsync(s => RepositorioVentas.AlojamientosAsync(s, idSociedad));

    /// <summary>Reglas de venta de la sociedad (concepto, IVA y cliente por origen y activo).</summary>
    public Task<IReadOnlyList<ReglaVenta>> ReglasAsync(string idSociedad) =>
        _bd.EjecutarAsync(s => RepositorioVentas.ReglasAsync(s, idSociedad));

    /// <summary>Activos propios de la sociedad (no ajenos), para elegir el activo relacionado de una venta.</summary>
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

    /// <summary>Nombre de cada proveedor (el de su tercero), para elegir el acreedor de la plataforma en las reglas de venta.</summary>
    public Task<IReadOnlyDictionary<int, string>> NombresProveedoresAsync() =>
        _bd.EjecutarAsync(async s =>
        {
            var filas = await s.ConsultarAsync("SELECT p.IdProveedor, t.Nombre FROM Proveedores AS p INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero WHERE p.Activo = True",
                f => (f.Entero("IdProveedor"), (f.Texto("Nombre") ?? "").Trim()));
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
        var res = AgrupadorVentas.Resolver(v, await ReglasAsync(v.IdSociedad));
        if (origen is null) r.Add(new("Ventas.SinOrigen", "Elige el origen de la venta (Airbnb, Booking, Directa…).", "IdOrigenVenta"));
        else if (res.IdCliente is null)
            r.Add(new("Ventas.SinCliente", $"No hay regla de venta con cliente para «{origen.Nombre}» y este activo: elige el cliente al que se factura esta venta (o ponlo en Parámetros y tablas › Reglas de venta).", "IdCliente"));
        if (v.FechaReferencia == default) r.Add(new("Ventas.SinFecha", "Escribe la fecha de la venta (en una reserva, la de entrada): decide el mes en que se factura.", "FechaReferencia"));
        if (v.FechaFin is DateTime fin && fin.Date < v.FechaReferencia.Date)
            r.Add(new("Ventas.FechasAlReves", "La fecha final es anterior a la de entrada. Corrígela.", "FechaFin"));
        if (v.Cantidad <= 0m) r.Add(new("Ventas.CantidadNoValida", "La cantidad (noches) debe ser mayor que cero.", "Cantidad"));
        if (v.Importe == 0m) r.Add(new("Ventas.SinImporte", "Escribe el importe que se factura (lo que paga el huésped, comisión incluida).", "Importe"));
        if (origen is not null && res.IdConcepto is null)
            r.Add(new("Ventas.SinConcepto", $"No hay regla de venta con concepto para «{origen.Nombre}» y este activo: elige el concepto de esta venta (o ponlo en Parámetros y tablas › Reglas de venta).", "IdConcepto"));
        if (!string.IsNullOrWhiteSpace(v.CausaExencion) && v.TipoIva is > 0m)
            r.Add(new("Ventas.ExentaConIva", "Una venta exenta no lleva IVA: quita la causa de exención o el porcentaje.", "TipoIva"));
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

    // ---------- huéspedes como clientes ----------

    /// <summary>
    /// Factura nominal al huésped (facturacion/reservas 2026-11-25 14:13): cada venta pendiente sin cliente cuya regla de venta
    /// lo pide recibe como cliente a su huésped. El huésped se da de alta como tercero (persona física con el nombre, el email
    /// y el teléfono de la reserva, sin NIF hasta que se escriba en su ficha) con el rol de cliente y, como cuenta, la misma
    /// 430 que el cliente de la regla, que es la de todos los huéspedes del origen. Un huésped que repite (mismo email entre
    /// los clientes de esa 430) reutiliza su ficha. Es idempotente: lo llaman la importación de Avaibook y «Facturar todo».
    /// </summary>
    public async Task<AltaHuespedes> AsegurarClientesHuespedAsync(string idSociedad, Actor actor)
    {
        if (_terceros is null || _roles is null) return AltaHuespedes.Nada;
        var reglas = await ReglasAsync(idSociedad);
        if (!reglas.Any(r => r.Activa && r.FacturaAlHuesped)) return AltaHuespedes.Nada;
        await SimplificarBorradoresSinNifAsync(idSociedad, reglas);
        var pendientes = await _bd.EjecutarAsync(s => RepositorioVentas.PendientesAsync(s, idSociedad));
        var candidatas = pendientes
            .Select(v => (Venta: v, Regla: ReglaVenta.Para(reglas, v.IdOrigenVenta, v.IdActivo)))
            .Where(x => x.Venta.IdCliente is null && x.Regla is { FacturaAlHuesped: true } && !string.IsNullOrWhiteSpace(x.Venta.Titular))
            .ToList();
        if (candidatas.Count == 0) return AltaHuespedes.Nada;

        var incidencias = new List<string>();
        var avisadas = new HashSet<int>();
        var identidades = new Dictionary<int, int?>();
        var porEmail = new Dictionary<(int, string), int>();
        int altas = 0, ventas = 0;
        var nombres = await NombresClientesAsync();
        foreach (var (v, regla) in candidatas)
        {
            if (regla!.IdCliente is not int idClienteRegla)
            {
                if (avisadas.Add(regla.IdReglaVenta))
                    incidencias.Add("Una regla de venta factura al huésped pero no tiene cliente: elige en ella el cliente cuya 430 compartirán los huéspedes (Parámetros y tablas › Reglas de venta).");
                continue;
            }
            if (!identidades.TryGetValue(idClienteRegla, out var identidad))
                identidades[idClienteRegla] = identidad = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
                    "SELECT IdIdentidadContable FROM CuentasClientesSociedad WHERE IdCliente = ? AND IdSociedad = ? AND Clase = 'CLIENTE'",
                    f => f.EnteroNulo("IdIdentidadContable"), idClienteRegla, idSociedad));
            if (identidad is not int idIdentidad)
            {
                if (avisadas.Add(regla.IdReglaVenta))
                    incidencias.Add($"El cliente «{nombres.GetValueOrDefault(idClienteRegla, $"cliente {idClienteRegla}")}» de una regla que factura al huésped no tiene cuenta 430 en esta sociedad: " +
                                    "ponle la 430 que compartirán los huéspedes en su ficha (Terceros) y vuelve a importar o a facturar.");
                continue;
            }

            var email = string.IsNullOrWhiteSpace(v.EmailTitular) ? null : v.EmailTitular.Trim();
            int? idCliente = null;
            if (email is not null)
            {
                if (porEmail.TryGetValue((idIdentidad, email.ToLowerInvariant()), out var yaVisto)) idCliente = yaVisto;
                else idCliente = await _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
                    "SELECT TOP 1 c.IdCliente FROM (Terceros AS t INNER JOIN Clientes AS c ON c.IdTercero = t.IdTercero) " +
                    "INNER JOIN CuentasClientesSociedad AS x ON x.IdCliente = c.IdCliente " +
                    "WHERE t.Email = ? AND x.IdSociedad = ? AND x.Clase = 'CLIENTE' AND x.IdIdentidadContable = ? AND c.IdCliente <> ? ORDER BY c.IdCliente",
                    f => (int?)f.Entero("IdCliente"), email, idSociedad, idIdentidad, idClienteRegla));
            }
            if (idCliente is null)
            {
                var alta = await AltaHuespedAsync(v, idSociedad, idIdentidad, actor);
                if (!alta.EsCorrecto)
                {
                    incidencias.Add($"{v.Rotulo} ({v.FechaReferencia:dd/MM/yyyy}): no he podido dar de alta a su huésped como cliente. {alta.Error!.TextoOperador()}");
                    continue;
                }
                idCliente = alta.Valor;
                altas++;
            }
            if (email is not null) porEmail[(idIdentidad, email.ToLowerInvariant())] = idCliente.Value;
            var ahora = _reloj.Ahora;
            if (await _bd.EjecutarAsync(s => RepositorioVentas.AsignarClienteHuespedAsync(s, v.IdVentaPorFacturar, idCliente.Value, ahora))) ventas++;
        }
        return new AltaHuespedes(altas, ventas, incidencias);
    }

    private async Task<Resultado<int>> AltaHuespedAsync(VentaPorFacturar v, string idSociedad, int idIdentidad, Actor actor)
    {
        var nombre = v.Titular!.Trim();
        var tercero = new Tercero
        {
            TipoPersona = TipoPersona.Fisica,
            NombrePersona = nombre.Length <= 100 ? nombre : nombre[..100],
            Email = RepositorioVentas.Recortar(v.EmailTitular, 254),
            Telefono = RepositorioVentas.Recortar(v.TelefonoTitular, 50),
            Notas = $"Huésped dado de alta desde la venta {v.Rotulo}{(string.IsNullOrWhiteSpace(v.Referencia) ? "" : $" (reserva {v.Referencia.Trim()})")}. Escribe aquí su NIF y su dirección si pide factura completa.",
        };
        tercero.Roles.Cliente = new RolCliente { Activo = true };
        var guardado = await _terceros!.GuardarAsync(tercero, actor);
        if (!guardado.EsCorrecto) return Resultado<int>.Fallo(guardado.Errores);
        var rol = await _roles!.AsegurarClienteAsync(guardado.Valor.IdTercero);
        if (!rol.EsCorrecto) return rol;
        var cuenta = await _roles.AsignarCuentaAsync(ClaseCuentaRol.Cliente, rol.Valor, idSociedad, idIdentidad);
        return cuenta.EsCorrecto ? rol : Resultado<int>.Fallo(cuenta.Errores);
    }

    /// <summary>
    /// Tipo de la factura nominal al huésped (facturacion/reservas 2026-11-27 14:38, sustituye a la Q1 b de 2026-11-25): completa
    /// (F1) si el huésped tiene NIF; sin NIF, simplificada (F2) sea cual sea el importe, aunque pase de los 400 € del
    /// RD 1619/2012, art. 4.1 (decisión del propietario: muchos huéspedes no dan el NIF).
    /// </summary>
    public static string TipoFacturaHuesped(bool tieneNif, decimal total) => tieneNif ? "F1" : "F2";

    /// <summary>
    /// Los borradores completos (F1) de reservas con regla nominal cuyo huésped sigue sin NIF pasan a simplificada (F2), para
    /// que se puedan validar (facturacion/reservas 2026-11-27 14:38). Los creados antes de ese cambio esperaban al NIF.
    /// </summary>
    private async Task SimplificarBorradoresSinNifAsync(string idSociedad, IReadOnlyList<ReglaVenta> reglas)
    {
        var borradores = await _bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT DISTINCT f.IdFactura, v.IdOrigenVenta, v.IdActivo, t.NIF FROM ((Facturas AS f INNER JOIN VentasPorFacturar AS v ON v.IdFactura = f.IdFactura) " +
            "INNER JOIN Clientes AS c ON c.IdCliente = f.IdCliente) INNER JOIN Terceros AS t ON t.IdTercero = c.IdTercero " +
            "WHERE v.IdSociedad = ? AND f.EstadoFactura = ? AND f.TipoFactura = 'F1'",
            x => (Id: x.Entero("IdFactura"), Origen: x.Entero("IdOrigenVenta"), Activo: x.EnteroNulo("IdActivo"), Nif: x.Texto("NIF")), idSociedad, (byte)EstadoFactura.Borrador));
        var ids = borradores.Where(b => string.IsNullOrWhiteSpace(b.Nif) && ReglaVenta.Para(reglas, b.Origen, b.Activo) is { FacturaAlHuesped: true })
            .Select(b => b.Id).Distinct().ToList();
        foreach (var id in ids)
            await _bd.EjecutarAsync(s => s.EjecutarAsync(
                "UPDATE Facturas SET TipoFactura = 'F2', Version = Version + 1 WHERE IdFactura = ? AND EstadoFactura = ? AND TipoFactura = 'F1'", id, (byte)EstadoFactura.Borrador));
    }

    // ---------- facturación ----------

    /// <summary>Catálogos para resolver y agrupar las ventas de la sociedad, con los conceptos en el ejercicio de hoy (el de las facturas).</summary>
    public async Task<(DatosVentas Datos, SociedadEjercicio? Ejercicio)> DatosAsync(string idSociedad)
    {
        var hoy = _reloj.Ahora.Date;
        var ejercicio = (await _sociedades.ListarEjerciciosAsync(idSociedad)).FirstOrDefault(e => e.Ejercicio == hoy.Year);
        var conceptos = ejercicio is null ? [] : await _conceptos.ListarAsync(ejercicio.Contexto);
        return (new DatosVentas((await OrigenesAsync(idSociedad)).ToDictionary(o => o.IdOrigenVenta), await ReglasAsync(idSociedad), await NombresClientesAsync(),
            (await ActivosAsync(idSociedad)).ToDictionary(a => a.IdActivo), conceptos.ToDictionary(c => c.IdConcepto)), ejercicio);
    }

    /// <summary>
    /// Qué haría «Facturar todo» con las ventas pendientes indicadas (todas si no se indican; con <paramref name="hasta"/>, solo
    /// las de fecha de referencia hasta ese día): los grupos (una factura cada uno) y las ventas que no se pueden facturar con
    /// su motivo. No cambia nada.
    /// </summary>
    public async Task<IReadOnlyList<GrupoVentas>> PrevisualizarAsync(string idSociedad, IReadOnlySet<int>? soloVentas = null, DateTime? hasta = null, Actor? actor = null)
    {
        // Con el operador, antes se dan de alta los huéspedes de las reglas nominales, para que la previsualización ya los vea como clientes.
        if (actor is not null && await _autorizador.PuedeAsync(Acciones.FacturasEditar)) await AsegurarClientesHuespedAsync(idSociedad, actor);
        var (d, _) = await DatosAsync(idSociedad);
        var pendientes = await _bd.EjecutarAsync(s => RepositorioVentas.PendientesAsync(s, idSociedad, hasta));
        return AgrupadorVentas.Agrupar(soloVentas is null ? pendientes : pendientes.Where(v => soloVentas.Contains(v.IdVentaPorFacturar)), d);
    }

    /// <summary>
    /// «Facturar todo» (facturacion/reservas 2026-11-14 17:15): factura las ventas pendientes indicadas según su regla de
    /// venta: una factura por cliente, origen, periodo (el de la agrupación de la regla) y, si la regla separa por activo,
    /// activo; con fecha de hoy, en la serie predeterminada del ejercicio de hoy. Cada factura y el enlace de sus ventas se
    /// guardan en una sola transacción: si otra sesión ya ha facturado alguna de sus ventas, no se crea esa factura. Las
    /// reglas que piden «facturar y exportar» pasan después cada factura por la validación normal (número, instantánea, paso
    /// fiscal y asiento); si no se puede validar, queda en borrador y se avisa. <paramref name="validar"/> impone lo mismo a
    /// todas, diga lo que diga su regla.
    /// </summary>
    public async Task<Resultado<ResultadoFacturarVentas>> FacturarAsync(string idSociedad, Actor actor, IReadOnlySet<int>? soloVentas = null, DateTime? hasta = null, bool? validar = null)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar)) return SinPermiso<ResultadoFacturarVentas>("facturar ventas", null);
        var (d, ejercicio) = await DatosAsync(idSociedad);
        var hoy = _reloj.Ahora.Date;
        if (ejercicio is null)
            return Resultado<ResultadoFacturarVentas>.Fallo(new Error("Ventas.SinEjercicio", $"La sociedad no tiene el ejercicio {hoy.Year}, que es donde van las facturas de hoy.", null, null, "Créalo en Parámetros y tablas › Ejercicios."));
        if (ejercicio.Cerrado)
            return Resultado<ResultadoFacturarVentas>.Fallo(new Error("Ventas.EjercicioCerrado", $"El ejercicio {hoy.Year} está cerrado y no admite facturas nuevas."));

        var huespedes = await AsegurarClientesHuespedAsync(idSociedad, actor);
        if (huespedes.Altas > 0 || huespedes.Ventas > 0) (d, _) = await DatosAsync(idSociedad);
        var pendientes = await _bd.EjecutarAsync(s => RepositorioVentas.PendientesAsync(s, idSociedad, hasta));
        var grupos = AgrupadorVentas.Agrupar(soloVentas is null ? pendientes : pendientes.Where(v => soloVentas.Contains(v.IdVentaPorFacturar)), d);
        var incidencias = grupos.Where(g => !g.Facturable)
            .Select(g => $"{g.Ventas[0].Rotulo} ({g.Ventas[0].FechaReferencia:dd/MM/yyyy}, {g.Ventas[0].Importe:N2} €): {g.Problema}").ToList();
        incidencias.AddRange(huespedes.Incidencias);
        var creadas = new List<FacturaDeVentas>();

        foreach (var g in grupos.Where(g => g.Facturable))
        {
            var borrador = await _facturas.NuevoBorradorAsync(ejercicio.Contexto);
            if (!borrador.EsCorrecto)
            {
                incidencias.Add($"{g.Descripcion}: {string.Join(" ", borrador.Errores.Select(e => e.TextoOperador()))}");
                continue;
            }
            var propuesta = await _facturas.PropuestaClienteAsync(g.Clave!.IdCliente, idSociedad);
            FacturaVenta f = borrador.Valor;
            f.IdCliente = g.Clave.IdCliente;
            f.FechaFactura = hoy;
            f.FechaOperacion = g.Clave.Hasta < hoy ? g.Clave.Hasta : hoy;
            f.FechaInicioValidez = g.Clave.Desde;
            f.FechaFinValidez = g.Clave.Hasta;
            f.Descripcion = g.Descripcion;
            if (propuesta?.FormaDePago is byte forma) f.FormaDePago = forma;
            if (propuesta?.DiaDePago is byte dia) f.DiaDePago = dia;
            f.TipoRetencion = 0m;
            if (AgrupadorVentas.Resolver(g.Ventas[0], d.Reglas).Regla is { FacturaAlHuesped: true })
                f.TipoFactura = TipoFacturaHuesped(await TieneNifAsync(g.Clave.IdCliente), g.Total);
            foreach (var v in g.Ventas)
            {
                var res = AgrupadorVentas.Resolver(v, d.Reglas);
                f.AnadirLinea(AgrupadorVentas.Linea(v, res, d.Conceptos[res.IdConcepto!.Value], v.IdActivo is int a ? d.Activos[a] : null, d.Origenes.GetValueOrDefault(v.IdOrigenVenta)));
            }
            var ahora = _reloj.Ahora;
            f.Anotar(ahora, actor.Nombre, $"Creada por «Facturar todo» con {g.Ventas.Count} {(g.Ventas.Count == 1 ? "venta" : "ventas")} ({string.Join(", ", g.Ventas.Select(v => v.IdVentaPorFacturar))})");
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
                if (validar ?? g.Validar)
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
                        incidencias.Add($"{rotulo} ({g.Descripcion}) creado pero no validado; queda como borrador. {string.Join(" ", r.Incidencias.Select(i => i.Mensaje))}");
                }
                creadas.Add(new FacturaDeVentas(idFactura, rotulo, g.Cliente, g.Descripcion, g.Ventas.Count, g.Total, validada));
            }
            catch (ExcepcionDatos ex)
            {
                incidencias.Add($"{g.Descripcion}: no se ha creado la factura. {ex.Error.Mensaje} Actualiza la lista y vuelve a facturar si hace falta.");
            }
        }
        return Resultado<ResultadoFacturarVentas>.Correcto(new ResultadoFacturarVentas(creadas, incidencias));
    }

    private Task<bool> TieneNifAsync(int idCliente) =>
        _bd.EjecutarAsync(async s => await s.EscalarAsync("SELECT t.NIF FROM Clientes AS c INNER JOIN Terceros AS t ON c.IdTercero = t.IdTercero WHERE c.IdCliente = ?", idCliente)
            is string nif && !string.IsNullOrWhiteSpace(nif));

    private static Resultado<T> SinPermiso<T>(string que, string? registro) =>
        Resultado<T>.Fallo(new Error("Seguridad.SinPermiso", $"No tienes permiso para {que}.", registro, null, "Pídeselo a quien administra los permisos."));
}
