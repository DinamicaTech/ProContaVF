using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;
using ProConta.Nucleo.Seguridad;
using ProConta.Seguridad.Permisos;

namespace ProConta.Facturacion.Ventas;

/// <summary>Reserva tal como la da la API de propietario de Avaibook (GET /api/owner/bookings/), solo con los campos que se usan.</summary>
public sealed record ReservaAvaibook(
    string Id,
    string? ExternalId,
    string? Status,
    string? Origin,
    string? PartnerName,
    DateTime CheckIn,
    DateTime? CheckOut,
    string? AccommodationId,
    string? AccommodationName,
    decimal TotalAmount,
    decimal? PartnerFee,
    string? TravellerName)
{
    public bool Cancelada => string.Equals(Status, "CANCELLED", StringComparison.OrdinalIgnoreCase);

    /// <summary>Confirmada o cancelada; las pendientes de propietario o de pago aún no son una venta.</summary>
    public bool Firme => Cancelada || string.Equals(Status, "CONFIRMED", StringComparison.OrdinalIgnoreCase);

    /// <summary>Identificador estable de la reserva: el externalId de la plataforma o, si no lo tiene, el id de Avaibook.</summary>
    public string Clave => string.IsNullOrWhiteSpace(ExternalId) ? $"AB-{Id}" : ExternalId.Trim();

    public int Noches => CheckOut is DateTime s && s.Date > CheckIn.Date ? (s.Date - CheckIn.Date).Days : 1;

    /// <summary>Huella de los datos que importan para facturar: si cambia, la reserva ha cambiado en Avaibook.</summary>
    public string Huella()
    {
        var texto = string.Join("|", Status, PartnerName, CheckIn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            CheckOut?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), AccommodationId,
            TotalAmount.ToString("0.00", CultureInfo.InvariantCulture), PartnerFee?.ToString("0.00", CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto)))[..32];
    }
}

/// <summary>Acceso a la API de Avaibook (para poder probar el importador sin red).</summary>
public interface IClienteAvaibook
{
    /// <summary>Reservas con fecha de entrada entre las dos fechas (incluidas); como mucho un año por consulta.</summary>
    Task<Resultado<IReadOnlyList<ReservaAvaibook>>> ReservasAsync(string direccion, string token, DateTime entradaDesde, DateTime entradaHasta, CancellationToken cancelacion = default);
}

/// <summary>Cliente HTTP de la API de propietario de Avaibook (cabecera X-AUTH-TOKEN).</summary>
public sealed class ClienteAvaibookHttp(HttpClient http) : IClienteAvaibook
{
    public const string DireccionProduccion = "https://api.avaibook.com/api/owner/";

    public async Task<Resultado<IReadOnlyList<ReservaAvaibook>>> ReservasAsync(string direccion, string token, DateTime entradaDesde, DateTime entradaHasta, CancellationToken cancelacion = default)
    {
        var url = $"{direccion.TrimEnd('/')}/bookings/?checkinStartDate={entradaDesde:yyyy-MM-dd}&checkinEndDate={entradaHasta:yyyy-MM-dd}";
        using var peticion = new HttpRequestMessage(HttpMethod.Get, url);
        peticion.Headers.Add("X-AUTH-TOKEN", token);
        peticion.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        try
        {
            using var respuesta = await http.SendAsync(peticion, cancelacion);
            var cuerpo = await respuesta.Content.ReadAsStringAsync(cancelacion);
            if (respuesta.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Resultado<IReadOnlyList<ReservaAvaibook>>.Fallo(new Error("Avaibook.TokenNoValido", "Avaibook no acepta el token.", null,
                    "El token no es el de producción, ha caducado o se ha borrado en Avaibook.", "Copia la clave «owner» de app.avaibook.com › API y pégala en Parámetros y tablas › Integraciones › Avaibook · Conexión."));
            if (!respuesta.IsSuccessStatusCode)
                return Resultado<IReadOnlyList<ReservaAvaibook>>.Fallo(new Error("Avaibook.Error", $"Avaibook ha respondido {(int)respuesta.StatusCode} ({respuesta.ReasonPhrase}).", null, null,
                    "Vuelve a intentarlo en unos minutos; si sigue, revisa la dirección de la API en Parámetros y tablas › Integraciones › Avaibook · Conexión.", Recortar(cuerpo, 500)));
            return Resultado<IReadOnlyList<ReservaAvaibook>>.Correcto(Leer(cuerpo));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return Resultado<IReadOnlyList<ReservaAvaibook>>.Fallo(new Error("Avaibook.SinConexion", "No he podido conectar con Avaibook.", null, ex.Message,
                "Comprueba la conexión a Internet y vuelve a intentarlo."));
        }
        catch (JsonException ex)
        {
            return Resultado<IReadOnlyList<ReservaAvaibook>>.Fallo(new Error("Avaibook.RespuestaNoValida", "La respuesta de Avaibook no tiene el formato esperado.", null, ex.Message,
                "Avísanos: puede que Avaibook haya cambiado su API."));
        }
    }

    /// <summary>Lee la lista de reservas del JSON de la API (un array de «Booking (full)»).</summary>
    public static IReadOnlyList<ReservaAvaibook> Leer(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var raiz = doc.RootElement;
        var lista = raiz.ValueKind == JsonValueKind.Array ? raiz
            : raiz.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array ? items
            : raiz.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array ? data
            : throw new JsonException("No es una lista de reservas.");
        var r = new List<ReservaAvaibook>();
        foreach (var b in lista.EnumerateArray())
        {
            if (Fecha(b, "checkInDate") is not DateTime entrada) continue;
            r.Add(new ReservaAvaibook(Texto(b, "id") ?? "", Texto(b, "externalId"), Texto(b, "status"), Texto(b, "origin"), Texto(b, "partnerName"),
                entrada, Fecha(b, "checkOutDate"), Texto(b, "accommodationId"), Texto(b, "accommodationName"),
                Numero(b, "totalAmount") ?? 0m, Numero(b, "partnerFee"), Texto(b, "travellerName")));
        }
        return r;
    }

    private static string? Texto(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null
        } : null;

    private static decimal? Numero(JsonElement e, string nombre) =>
        e.TryGetProperty(nombre, out var v) ? v.ValueKind switch
        {
            JsonValueKind.Number => Math.Round(v.GetDecimal(), 2, MidpointRounding.AwayFromZero),
            JsonValueKind.String when decimal.TryParse(v.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var d) => Math.Round(d, 2, MidpointRounding.AwayFromZero),
            _ => null
        } : null;

    private static DateTime? Fecha(JsonElement e, string nombre) =>
        Texto(e, nombre) is string t && DateTime.TryParse(t, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d.Date : null;

    private static string Recortar(string t, int n) => t.Length <= n ? t : t[..n];
}

/// <summary>Conexión de una sociedad con Avaibook (el token solo se descifra en memoria).</summary>
public sealed record ConexionAvaibook(string IdSociedad, bool Activa, string Direccion, string? Token, DateTime? ImportarDesde, DateTime? UltimaImportacion, string? ResumenUltimaImportacion);

/// <summary>
/// Resultado de una importación de Avaibook (sin datos personales). <paramref name="Omitidas"/> son las que no entran sin que
/// haya nada que hacer (sin confirmar, canceladas antes de importarse); <paramref name="Incidencias"/>, lo que pide una acción
/// del operador; <paramref name="OrigenesNuevos"/>, los portales dados de alta en esta importación;
/// <paramref name="SinRegla"/>, cuántas ventas pendientes no tienen regla de venta que les dé cliente y concepto.
/// </summary>
public sealed record ResultadoImportacionAvaibook(int Leidas, int Nuevas, int Actualizadas, int Anuladas, int SinCambios, int Omitidas, IReadOnlyList<string> Incidencias)
{
    public IReadOnlyList<string> OrigenesNuevos { get; init; } = [];
    public IReadOnlyList<string> Informacion { get; init; } = [];
    public IReadOnlyList<string> SinRegla { get; init; } = [];
    public int AlojamientosSinActivo { get; init; }

    public string Resumen
    {
        get
        {
            static string N(int n, string uno, string varios) => n == 1 ? $"1 {uno}" : $"{n} {varios}";
            var partes = new List<string>();
            if (Nuevas > 0) partes.Add(N(Nuevas, "nueva", "nuevas"));
            if (Actualizadas > 0) partes.Add(N(Actualizadas, "actualizada", "actualizadas"));
            if (Anuladas > 0) partes.Add(N(Anuladas, "anulada por cancelación", "anuladas por cancelación"));
            if (SinCambios > 0) partes.Add(N(SinCambios, "que ya estaba y no ha cambiado", "que ya estaban y no han cambiado"));
            if (Omitidas > 0) partes.Add(N(Omitidas, "que no entra", "que no entran"));
            return $"{N(Leidas, "reserva leída", "reservas leídas")} de Avaibook" + (partes.Count == 0 ? "." : ": " + string.Join(", ", partes) + ".");
        }
    }

    /// <summary>¿Queda algo por hacer en Reglas de venta para poder facturar lo importado?</summary>
    public bool FaltanReglas => SinRegla.Count > 0;

    /// <summary>¿Queda algún alojamiento por vincular con su activo?</summary>
    public bool FaltanAlojamientos => AlojamientosSinActivo > 0;
}

/// <summary>
/// Importación de reservas de Avaibook como ventas por facturar (facturacion/reservas 2026-11-14 11:45). Cada reserva se
/// reconoce por su externalId: la nueva se crea pendiente; la ya importada y pendiente se actualiza si ha cambiado; la
/// cancelada pasa a anulada si está pendiente; una venta facturada no se toca y su cambio queda como incidencia. Las
/// reservas de un alojamiento sin activo relacionado se omiten con una incidencia (y el alojamiento se da de alta para
/// asignarlo). La venta solo guarda los datos de la reserva: el concepto, el IVA y el cliente salen de las reglas de venta al facturar.
/// Una reserva que coincide con una venta del ERP (mismo origen, fecha de entrada e importe) se reconoce como esa venta.
/// </summary>
public sealed class ImportadorAvaibook
{
    private readonly IBaseDatos _bd;
    private readonly ServicioVentas _ventas;
    private readonly IClienteAvaibook _cliente;
    private readonly IAutorizadorAcciones _autorizador;
    private readonly IReloj _reloj;

    public ImportadorAvaibook(IBaseDatos bd, ServicioVentas ventas, IClienteAvaibook cliente, IAutorizadorAcciones? autorizador = null, IReloj? reloj = null)
    {
        _bd = bd;
        _ventas = ventas;
        _cliente = cliente;
        _autorizador = autorizador ?? AutorizadorTodo.Instancia;
        _reloj = reloj ?? RelojSistema.Instancia;
    }

    public Task<ConexionAvaibook?> ConexionAsync(string idSociedad) =>
        _bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT IdSociedad, Activa, Direccion, SecretoProtegido, ImportarDesde, UltimaImportacion, ResumenUltimaImportacion FROM ConexionesAvaibook WHERE IdSociedad = ?",
            f => new ConexionAvaibook((f.Texto("IdSociedad") ?? "").Trim(), f.Logico("Activa"), f.Texto("Direccion") ?? ClienteAvaibookHttp.DireccionProduccion,
                SecretoProtegido.Desproteger(f.Texto("SecretoProtegido")), f.FechaNula("ImportarDesde"), f.FechaNula("UltimaImportacion"), f.Texto("ResumenUltimaImportacion")), idSociedad));

    /// <summary>
    /// Importa las reservas con fecha de entrada entre las dos fechas (por tramos de un año como mucho). Si no se dan, desde
    /// la fecha «Importar desde» de la conexión (o el 1 de enero del año pasado) hasta dentro de un año.
    /// </summary>
    public async Task<Resultado<ResultadoImportacionAvaibook>> ImportarAsync(string idSociedad, Actor actor, DateTime? desde = null, DateTime? hasta = null, CancellationToken cancelacion = default)
    {
        if (!await _autorizador.PuedeAsync(Acciones.FacturasEditar))
            return Resultado<ResultadoImportacionAvaibook>.Fallo(new Error("Seguridad.SinPermiso", "No tienes permiso para importar ventas.", null, null, "Pídeselo a quien administra los permisos."));
        var conexion = await ConexionAsync(idSociedad);
        if (conexion is null || !conexion.Activa)
            return Resultado<ResultadoImportacionAvaibook>.Fallo(new Error("Avaibook.SinConexion", "La sociedad no tiene la conexión con Avaibook activa.", null, null,
                "Dala de alta en Parámetros y tablas › Integraciones › Avaibook · Conexión, con el token de la API."));
        if (string.IsNullOrWhiteSpace(conexion.Token))
            return Resultado<ResultadoImportacionAvaibook>.Fallo(new Error("Avaibook.SinToken", "Falta el token de Avaibook (o el guardado no se puede leer en este equipo).", null, null,
                "Escríbelo en Parámetros y tablas › Integraciones › Avaibook · Conexión."));

        var hoy = _reloj.Ahora.Date;
        var inicio = (desde ?? conexion.ImportarDesde ?? new DateTime(hoy.Year - 1, 1, 1)).Date;
        var fin = (hasta ?? hoy.AddYears(1)).Date;
        var reservas = new List<ReservaAvaibook>();
        for (var d = inicio; d <= fin; d = d.AddYears(1))
        {
            var h = d.AddYears(1).AddDays(-1) < fin ? d.AddYears(1).AddDays(-1) : fin;
            var r = await _cliente.ReservasAsync(conexion.Direccion, conexion.Token, d, h, cancelacion);
            if (!r.EsCorrecto) return Resultado<ResultadoImportacionAvaibook>.Fallo(r.Errores);
            reservas.AddRange(r.Valor);
        }

        var resultado = await AplicarAsync(idSociedad, reservas.GroupBy(x => x.Clave).Select(g => g.Last()).ToList(), actor);
        await _bd.EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE ConexionesAvaibook SET UltimaImportacion = ?, OperadorUltimaImportacion = ?, ResumenUltimaImportacion = ? WHERE IdSociedad = ?",
            _reloj.Ahora, actor.Nombre, $"Entradas del {inicio:dd/MM/yyyy} al {fin:dd/MM/yyyy}. {resultado.Resumen}", idSociedad));
        return Resultado<ResultadoImportacionAvaibook>.Correcto(resultado);
    }

    /// <summary>Aplica una lista de reservas leídas de Avaibook a las ventas de la sociedad (sin red; lo usan las pruebas).</summary>
    public async Task<ResultadoImportacionAvaibook> AplicarAsync(string idSociedad, IReadOnlyList<ReservaAvaibook> reservas, Actor actor)
    {
        var origenes = (await _ventas.OrigenesAsync(idSociedad)).ToList();
        var directa = origenes.FirstOrDefault(o => o.EsDirecta && o.Activo);
        var incidencias = new List<string>();
        var informacion = new List<string>();
        var origenesNuevos = new List<string>();
        int nuevas = 0, actualizadas = 0, anuladas = 0, sinCambios = 0, omitidas = 0, sinConfirmar = 0, canceladas = 0, delErpReconocidas = 0, sinAlojamiento = 0;

        await _bd.EnTransaccionAsync(async s =>
        {
            var alojamientos = (await RepositorioVentas.AlojamientosAsync(s, idSociedad)).ToDictionary(a => a.IdExterno, StringComparer.OrdinalIgnoreCase);
            // Alojamientos nuevos: se dan de alta sin activo, para asignarlo en Parámetros y tablas.
            foreach (var g in reservas.Where(r => !string.IsNullOrWhiteSpace(r.AccommodationId) && !alojamientos.ContainsKey(r.AccommodationId!)).GroupBy(r => r.AccommodationId!))
            {
                var a = new AlojamientoAvaibook(0, idSociedad, g.Key, g.First().AccommodationName, null, true);
                var id = await RepositorioVentas.InsertarAlojamientoAsync(s, a);
                alojamientos[g.Key] = a with { IdAlojamientoAvaibook = id };
            }
            var existentes = (await RepositorioVentas.PorExternoAsync(s, idSociedad, FuenteVenta.Avaibook)).ToDictionary(v => v.IdExterno!.Trim(), StringComparer.OrdinalIgnoreCase);
            var delErp = (await RepositorioVentas.PorExternoAsync(s, idSociedad, FuenteVenta.Erp)).ToList();
            var ahora = _reloj.Ahora;
            var sinPiso = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in reservas.OrderBy(x => x.CheckIn))
            {
                if (!r.Firme) { omitidas++; sinConfirmar++; continue; }
                var origen = origenes.FirstOrDefault(o => o.Activo && o.Reconoce(r.PartnerName))
                             ?? (string.Equals(r.Origin, "PARTNER", StringComparison.OrdinalIgnoreCase) ? null : directa);
                // Un portal que aún no es origen de venta se da de alta solo (facturacion/reservas 2026-11-14 17:15): sus reservas
                // entran pendientes y quedan a falta de su regla de venta.
                if (origen is null && !string.IsNullOrWhiteSpace(r.PartnerName) && !origenes.Any(o => o.Reconoce(r.PartnerName)))
                {
                    var nuevo = OrigenNuevo(idSociedad, r.PartnerName, origenes);
                    origen = nuevo with { IdOrigenVenta = await RepositorioVentas.InsertarOrigenAsync(s, nuevo) };
                    origenes.Add(origen);
                    origenesNuevos.Add(origen.Nombre);
                }
                existentes.TryGetValue(r.Clave, out var venta);

                if (venta is not null)
                {
                    var huella = r.Huella();
                    if (venta.Huella == huella) { sinCambios++; continue; }
                    if (!venta.EsEditable && venta.Estado != EstadoVenta.Anulada)
                    {
                        var texto = r.Cancelada
                            ? $"{ahora:dd/MM/yyyy}: Avaibook la da por cancelada, pero ya está {VentaPorFacturar.TextoEstado(venta.Estado).ToLowerInvariant()} ({venta.RotuloFactura ?? "en el ERP"}). Si procede, rectifica la factura."
                            : $"{ahora:dd/MM/yyyy}: ha cambiado en Avaibook (importe {r.TotalAmount:N2} €, entrada {r.CheckIn:dd/MM/yyyy}), pero ya está {VentaPorFacturar.TextoEstado(venta.Estado).ToLowerInvariant()}. Si procede, rectifica la factura.";
                        if (venta.Incidencia != texto)
                        {
                            await RepositorioVentas.AnotarIncidenciaAsync(s, venta.IdVentaPorFacturar, texto);
                            incidencias.Add($"{venta.Rotulo} (reserva {r.Clave}): {texto}");
                        }
                        continue;
                    }
                    if (r.Cancelada)
                    {
                        if (!venta.Anulada) anuladas++; else sinCambios++;
                        venta.Anulada = true;
                        venta.MotivoAnulacion ??= "Cancelada en Avaibook";
                    }
                    else
                    {
                        if (venta.Anulada && venta.MotivoAnulacion == "Cancelada en Avaibook") { venta.Anulada = false; venta.MotivoAnulacion = null; }
                        Copiar(r, venta, origen, alojamientos);
                        actualizadas++;
                    }
                    venta.Huella = huella;
                    venta.Incidencia = null;
                    venta.FechaModificacion = ahora;
                    await RepositorioVentas.ActualizarAsync(s, venta);
                    continue;
                }

                if (origen is null)
                {
                    var texto = r.PartnerName ?? r.Origin ?? "sin origen";
                    if (sinPiso.Add("origen:" + texto))
                        incidencias.Add($"Hay reservas de «{texto}», un origen de venta que está inactivo o que no puedo reconocer (la primera, del {r.CheckIn:dd/MM/yyyy}): no se han importado. Actívalo o añade ese texto a un origen en Parámetros y tablas › Orígenes de venta y vuelve a importar.");
                    continue;
                }
                if (r.AccommodationId is not string idAloj || !alojamientos.TryGetValue(idAloj, out var aloj) || aloj.IdActivo is null || !aloj.Activo)
                {
                    sinAlojamiento++;
                    if (r.AccommodationId is string idA && sinPiso.Add(idA) && !(alojamientos.TryGetValue(idA, out var al) && !al.Activo))
                        incidencias.Add($"El alojamiento «{r.AccommodationName}» ({idA}) de Avaibook no tiene activo relacionado: sus reservas no se han importado. Asígnaselo en Parámetros y tablas › Integraciones › Avaibook · Alojamientos y vuelve a importar.");
                    continue;
                }
                // Una reserva ya cargada desde el ERP (mismo origen, fecha de entrada e importe) es la misma venta.
                if (delErp.FirstOrDefault(v => v.IdOrigenVenta == origen.IdOrigenVenta && v.FechaReferencia.Date == r.CheckIn.Date && v.Importe == r.TotalAmount) is { } erp)
                {
                    delErp.Remove(erp);
                    sinCambios++;
                    delErpReconocidas++;
                    continue;
                }
                if (r.Cancelada) { omitidas++; canceladas++; continue; }

                var nueva = new VentaPorFacturar
                {
                    IdSociedad = idSociedad,
                    Fuente = FuenteVenta.Avaibook,
                    IdExterno = r.Clave,
                    Huella = r.Huella(),
                    FechaAlta = ahora,
                    OperadorAlta = actor.Nombre,
                    FechaModificacion = ahora,
                };
                Copiar(r, nueva, origen, alojamientos);
                await RepositorioVentas.InsertarAsync(s, nueva);
                nuevas++;
            }
            return 0;
        });
        if (sinConfirmar > 0) informacion.Add($"{sinConfirmar} {(sinConfirmar == 1 ? "no entra porque aún no está confirmada" : "no entran porque aún no están confirmadas")} en Avaibook (pendiente de pago o de tu aceptación): {(sinConfirmar == 1 ? "entrará" : "entrarán")} sola{(sinConfirmar == 1 ? "" : "s")} al confirmarse.");
        if (canceladas > 0) informacion.Add($"{canceladas} {(canceladas == 1 ? "no entra porque ya estaba cancelada" : "no entran porque ya estaban canceladas")} cuando se {(canceladas == 1 ? "ha leído" : "han leído")}: no hay nada que facturar.");
        if (delErpReconocidas > 0) informacion.Add($"{delErpReconocidas} {(delErpReconocidas == 1 ? "es una reserva que ya venía del ERP" : "son reservas que ya venían del ERP")} (mismo origen, fecha de entrada e importe): no se {(delErpReconocidas == 1 ? "duplica" : "duplican")}.");
        if (sinAlojamiento > 0) informacion.Add($"{sinAlojamiento} {(sinAlojamiento == 1 ? "espera" : "esperan")} a que su alojamiento tenga activo relacionado.");

        // Lo importado que aún no se puede facturar por falta de regla (cliente o concepto), por origen y activo.
        var (datos, _) = await _ventas.DatosAsync(idSociedad);
        var pendientes = await _bd.EjecutarAsync(s => RepositorioVentas.PendientesAsync(s, idSociedad));
        var sinRegla = pendientes.Where(v => v.Fuente == FuenteVenta.Avaibook)
            .Select(v => (Venta: v, Res: AgrupadorVentas.Resolver(v, datos.Reglas)))
            .Where(x => x.Res.IdCliente is null || x.Res.IdConcepto is null)
            .GroupBy(x => (x.Venta.IdOrigenVenta, x.Venta.IdActivo))
            .Select(g =>
            {
                var origen = datos.Origenes.TryGetValue(g.Key.IdOrigenVenta, out var o) ? o.Nombre : $"origen {g.Key.IdOrigenVenta}";
                var activo = g.Key.IdActivo is int a && datos.Activos.TryGetValue(a, out var act) ? act.Nombre : "sin activo";
                var falta = string.Join(" y ", new[] { g.Any(x => x.Res.IdCliente is null) ? "cliente" : null, g.Any(x => x.Res.IdConcepto is null) ? "concepto" : null }.Where(t => t is not null));
                return $"{origen} · {activo}: {g.Count()} {(g.Count() == 1 ? "venta pendiente" : "ventas pendientes")} sin {falta}.";
            }).ToList();
        var alojamientosSinActivo = (await _ventas.AlojamientosAsync(idSociedad)).Count(a => a.Activo && a.IdActivo is null);
        return new ResultadoImportacionAvaibook(reservas.Count, nuevas, actualizadas, anuladas, sinCambios, omitidas + sinAlojamiento, incidencias)
        {
            OrigenesNuevos = origenesNuevos, Informacion = informacion, SinRegla = sinRegla, AlojamientosSinActivo = alojamientosSinActivo,
        };
    }

    /// <summary>
    /// Origen para un portal que llega por primera vez: el nombre es el texto de Avaibook sin la coletilla técnica
    /// («Vrbo [XML]» → «Vrbo») y el código, sus letras y cifras en mayúsculas, sin repetir uno que ya exista.
    /// </summary>
    internal static OrigenVenta OrigenNuevo(string idSociedad, string partnerName, IReadOnlyList<OrigenVenta> existentes)
    {
        var texto = partnerName.Trim();
        var corchete = texto.IndexOf('[', StringComparison.Ordinal);
        var nombre = (corchete > 0 ? texto[..corchete] : texto).Trim();
        if (nombre.Length == 0) nombre = texto;
        if (nombre.Length > 60) nombre = nombre[..60];
        var codigo = new string(nombre.ToUpperInvariant().Where(char.IsAsciiLetterOrDigit).ToArray());
        if (codigo.Length == 0) codigo = "ORIGEN";
        if (codigo.Length > 17) codigo = codigo[..17];
        var libre = codigo;
        for (var n = 2; existentes.Any(o => string.Equals(o.Codigo, libre, StringComparison.OrdinalIgnoreCase)); n++) libre = $"{codigo}{n}";
        return new OrigenVenta(0, idSociedad, libre, nombre, texto.Length <= 255 ? texto : texto[..255], false, true);
    }

    private static void Copiar(ReservaAvaibook r, VentaPorFacturar v, OrigenVenta? origen, IReadOnlyDictionary<string, AlojamientoAvaibook> alojamientos)
    {
        if (origen is not null) v.IdOrigenVenta = origen.IdOrigenVenta;
        if (r.AccommodationId is string id && alojamientos.TryGetValue(id, out var a))
        {
            v.IdActivo = a.IdActivo ?? v.IdActivo;
        }
        v.FechaReferencia = r.CheckIn.Date;
        v.FechaFin = r.CheckOut?.Date;
        v.Cantidad = r.Noches;
        v.Importe = r.TotalAmount;
        v.Comision = r.PartnerFee;
        v.Referencia = r.ExternalId is string e && e.Contains('_') ? e[(e.LastIndexOf('_') + 1)..] : r.ExternalId;
        v.Titular = r.TravellerName;
    }
}
