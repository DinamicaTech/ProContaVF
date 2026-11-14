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
                    "El token no es el de producción, ha caducado o se ha borrado en Avaibook.", "Copia la clave «owner» de app.avaibook.com › API y pégala en Parámetros y tablas › Conexión con Avaibook."));
            if (!respuesta.IsSuccessStatusCode)
                return Resultado<IReadOnlyList<ReservaAvaibook>>.Fallo(new Error("Avaibook.Error", $"Avaibook ha respondido {(int)respuesta.StatusCode} ({respuesta.ReasonPhrase}).", null, null,
                    "Vuelve a intentarlo en unos minutos; si sigue, revisa la dirección de la API en Parámetros y tablas › Conexión con Avaibook.", Recortar(cuerpo, 500)));
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

/// <summary>Resultado de una importación de Avaibook (sin datos personales).</summary>
public sealed record ResultadoImportacionAvaibook(int Leidas, int Nuevas, int Actualizadas, int Anuladas, int SinCambios, int Omitidas, IReadOnlyList<string> Incidencias)
{
    public string Resumen =>
        $"{Leidas} reservas leídas: {Nuevas} nuevas, {Actualizadas} actualizadas, {Anuladas} anuladas, {SinCambios} sin cambios, {Omitidas} omitidas; {Incidencias.Count} incidencias.";
}

/// <summary>
/// Importación de reservas de Avaibook como ventas por facturar (facturacion/reservas 2026-11-14 11:45). Cada reserva se
/// reconoce por su externalId: la nueva se crea pendiente; la ya importada y pendiente se actualiza si ha cambiado; la
/// cancelada pasa a anulada si está pendiente; una venta facturada no se toca y su cambio queda como incidencia. Las
/// reservas de un alojamiento sin piso asignado se omiten con una incidencia (y el alojamiento se da de alta para asignarlo).
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
                "Dala de alta en Parámetros y tablas › Conexión con Avaibook, con el token de la API."));
        if (string.IsNullOrWhiteSpace(conexion.Token))
            return Resultado<ResultadoImportacionAvaibook>.Fallo(new Error("Avaibook.SinToken", "Falta el token de Avaibook (o el guardado no se puede leer en este equipo).", null, null,
                "Escríbelo en Parámetros y tablas › Conexión con Avaibook."));

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
        var origenes = await _ventas.OrigenesAsync(idSociedad);
        var directa = origenes.FirstOrDefault(o => o.EsDirecta && o.Activo);
        var incidencias = new List<string>();
        int nuevas = 0, actualizadas = 0, anuladas = 0, sinCambios = 0, omitidas = 0;

        await _bd.EnTransaccionAsync(async s =>
        {
            var alojamientos = (await RepositorioVentas.AlojamientosAsync(s, idSociedad)).ToDictionary(a => a.IdExterno, StringComparer.OrdinalIgnoreCase);
            // Alojamientos nuevos: se dan de alta sin piso, para asignarlo en Parámetros y tablas.
            foreach (var g in reservas.Where(r => !string.IsNullOrWhiteSpace(r.AccommodationId) && !alojamientos.ContainsKey(r.AccommodationId!)).GroupBy(r => r.AccommodationId!))
            {
                var a = new AlojamientoAvaibook(0, idSociedad, g.Key, g.First().AccommodationName, null, null, "E1", true);
                var id = await RepositorioVentas.InsertarAlojamientoAsync(s, a);
                alojamientos[g.Key] = a with { IdAlojamientoAvaibook = id };
            }
            var existentes = (await RepositorioVentas.PorExternoAsync(s, idSociedad, FuenteVenta.Avaibook)).ToDictionary(v => v.IdExterno!.Trim(), StringComparer.OrdinalIgnoreCase);
            var delErp = (await RepositorioVentas.PorExternoAsync(s, idSociedad, FuenteVenta.Erp)).ToList();
            var ahora = _reloj.Ahora;
            var sinPiso = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var r in reservas.OrderBy(x => x.CheckIn))
            {
                if (!r.Firme) { omitidas++; continue; }
                var origen = origenes.FirstOrDefault(o => o.Activo && o.Reconoce(r.PartnerName))
                             ?? (string.Equals(r.Origin, "PARTNER", StringComparison.OrdinalIgnoreCase) ? null : directa);
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
                    omitidas++;
                    incidencias.Add($"Reserva {r.Clave} ({r.CheckIn:dd/MM/yyyy}): llega de «{r.PartnerName ?? r.Origin}», que no es ningún origen de venta. Añade ese texto a un origen en Parámetros y tablas › Orígenes de venta y vuelve a importar.");
                    continue;
                }
                if (r.AccommodationId is not string idAloj || !alojamientos.TryGetValue(idAloj, out var aloj) || aloj.IdActivo is null || !aloj.Activo)
                {
                    omitidas++;
                    if (r.AccommodationId is string idA && sinPiso.Add(idA) && !(alojamientos.TryGetValue(idA, out var al) && !al.Activo))
                        incidencias.Add($"El alojamiento «{r.AccommodationName}» ({idA}) de Avaibook no tiene piso asignado: sus reservas no se han importado. Asígnale el piso y el concepto en Parámetros y tablas › Alojamientos de Avaibook y vuelve a importar.");
                    continue;
                }
                // Una reserva ya cargada desde el ERP (mismo origen, fecha de entrada e importe) es la misma venta.
                if (delErp.FirstOrDefault(v => v.IdOrigenVenta == origen.IdOrigenVenta && v.FechaReferencia.Date == r.CheckIn.Date && v.Importe == r.TotalAmount) is { } erp)
                {
                    delErp.Remove(erp);
                    sinCambios++;
                    continue;
                }
                if (r.Cancelada) { omitidas++; continue; }

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
        return new ResultadoImportacionAvaibook(reservas.Count, nuevas, actualizadas, anuladas, sinCambios, omitidas, incidencias);
    }

    private static void Copiar(ReservaAvaibook r, VentaPorFacturar v, OrigenVenta? origen, IReadOnlyDictionary<string, AlojamientoAvaibook> alojamientos)
    {
        if (origen is not null) v.IdOrigenVenta = origen.IdOrigenVenta;
        if (r.AccommodationId is string id && alojamientos.TryGetValue(id, out var a))
        {
            v.IdActivo = a.IdActivo ?? v.IdActivo;
            v.IdConcepto = a.IdConcepto ?? v.IdConcepto;
            v.CausaExencion = string.IsNullOrWhiteSpace(a.CausaExencion) ? null : a.CausaExencion.Trim();
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
