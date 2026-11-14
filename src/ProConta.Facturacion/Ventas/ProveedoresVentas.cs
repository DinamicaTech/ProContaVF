using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Conceptos;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;
using ProConta.Nucleo.Seguridad;

namespace ProConta.Facturacion.Ventas;

/// <summary>
/// Proveedores de Parámetros y tablas para las ventas por facturar (facturacion/reservas): Orígenes de venta, Alojamientos
/// de Avaibook y Conexión con Avaibook.
/// </summary>
public static class ProveedoresVentas
{
    public static IEnumerable<IProveedorMantenimiento> Todos(IBaseDatos bd, ServicioVentas ventas, IClienteAvaibook avaibook) =>
    [
        new ProveedorOrigenesVenta(bd, ventas),
        new ProveedorAlojamientosAvaibook(bd, ventas),
        new ProveedorConexionAvaibook(bd, avaibook),
    ];

    internal static readonly IReadOnlyList<OpcionCampo> Causas =
    [
        new(null, "Sujeta y no exenta"), new("E1", "E1 · Exenta por el artículo 20 (alquiler sin servicios de hostelería)"), new("E2", "E2 · Exenta por el artículo 21"),
        new("E3", "E3 · Exenta por el artículo 22"), new("E4", "E4 · Exenta por los artículos 23 y 24"), new("E5", "E5 · Exenta por el artículo 25"), new("E6", "E6 · Exenta por otros"),
    ];
}

/// <summary>Orígenes de venta de la empresa: a quién se factura y con qué textos se reconocen al importar.</summary>
public sealed class ProveedorOrigenesVenta : ProveedorTablaAccess
{
    private readonly ServicioVentas _ventas;
    private IReadOnlyList<OpcionCampo> _clientes = [];

    public ProveedorOrigenesVenta(IBaseDatos bd, ServicioVentas ventas) : base(bd, "origenesventa", "Orígenes de venta", "Facturación",
        "De dónde llegan las ventas por facturar (Airbnb, Booking, directas…) y a qué cliente se facturan.", AmbitoMantenimiento.Sociedad, "origen",
        "OrigenesVenta", "IdOrigenVenta", ClaveNueva.Autonumerica, [], [])
    {
        _ventas = ventas;
        ColumnaAmbito = "IdSociedad";
        Orden = "Nombre";
        Alias = ["origen", "canal", "plataforma", "airbnb", "booking", "reservas", "ventas por facturar"];
        Usos = [new("VentasPorFacturar", "IdOrigenVenta", "ventas por facturar")];
        Titular = r => r.EsNuevo ? "Nuevo origen de venta" : r.Texto("Nombre") ?? "Origen sin nombre";
        Subtitular = r => r.Texto("Codigo");
        AntesDeGuardar = async (s, r, c) =>
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM OrigenesVenta WHERE IdSociedad = ? AND Codigo = ? AND IdOrigenVenta <> ?", c.Trabajo.IdSociedad,
                (r.Texto("Codigo") ?? "").Trim().ToUpperInvariant(), r.Clave is null ? -1 : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture) > 0
                ? new Error("Origenes.Repetido", $"Ya hay un origen con el código {r.Texto("Codigo")}.", null, null, "Usa otro código.")
                : null;
    }

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Codigo", "Código", TipoCampo.Codigo) { Obligatorio = true, Longitud = 20, Ancho = "s", Ayuda = "Código corto del origen (AIRBNB, BOOKING, DIRECTA). Único en la empresa." },
        new("Nombre", "Nombre") { Obligatorio = true, Longitud = 60, Ancho = "m", Ayuda = "Nombre que se ve en las ventas y en la descripción de sus facturas («Airbnb · enero 2026 · …»)." },
        new("IdCliente", "Cliente al que se factura", TipoCampo.Lista) { Opciones = _clientes, Ancho = "l",
            Ayuda = "La plataforma que paga las reservas. Vacío (ventas directas), cada venta debe llevar su cliente." },
        new("TextosImportacion", "Textos al importar") { Longitud = 255, Ancho = "l",
            Ayuda = "Cómo llega este origen en la importación de Avaibook (su partnerName), separados por punto y coma: por ejemplo «Booking.com;Booking»." },
        new("EsDirecta", "Reservas directas", TipoCampo.Logico) { Ayuda = "Marcado, recibe las reservas de Avaibook que no vienen de un portal (alta propia, motor de reservas)." },
        new("Activo", "Activo", TipoCampo.Logico) { Ayuda = "Solo los activos se ofrecen en las ventas y reconocen reservas al importar." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Codigo", "Código", 90), new("Nombre", "Nombre"), new("IdCliente", "Cliente"), new("Activo", "Activo", 56)];

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        await _ventas.OrigenesAsync(c.Trabajo.IdSociedad);
        _clientes = [new OpcionCampo(null, "Sin cliente (cada venta lleva el suyo)"), .. (await _ventas.NombresClientesAsync()).OrderBy(x => x.Value, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new OpcionCampo(x.Key, $"{x.Value} ({x.Key})"))];
        return await base.ListarAsync(c);
    }

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento c) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?> { ["Activo"] = true, ["EsDirecta"] = false }));
}

/// <summary>Alojamientos de Avaibook de la empresa: el piso (activo) y el concepto con que se importan sus reservas.</summary>
public sealed class ProveedorAlojamientosAvaibook : ProveedorTablaAccess
{
    private readonly ServicioVentas _ventas;
    private IReadOnlyList<OpcionCampo> _activos = [];
    private IReadOnlyList<OpcionCampo> _conceptos = [];

    public ProveedorAlojamientosAvaibook(IBaseDatos bd, ServicioVentas ventas) : base(bd, "alojamientosavaibook", "Alojamientos de Avaibook", "Facturación",
        "Cada alojamiento de Avaibook con el piso (activo) y el concepto con que se importan y facturan sus reservas.", AmbitoMantenimiento.Sociedad, "alojamiento",
        "AlojamientosAvaibook", "IdAlojamientoAvaibook", ClaveNueva.Autonumerica, [], [])
    {
        _ventas = ventas;
        ColumnaAmbito = "IdSociedad";
        Orden = "Nombre";
        Alias = ["avaibook", "alojamiento", "piso", "apartamento", "reservas"];
        Titular = r => r.EsNuevo ? "Nuevo alojamiento de Avaibook" : r.Texto("Nombre") ?? $"Alojamiento {r.Texto("IdExterno")}";
        Subtitular = r => r.Texto("IdExterno") is string id ? $"Id de Avaibook {id}" : null;
        Reglas = r => r.Logico("Activo") && (r["IdActivo"] is null || r["IdConcepto"] is null)
            ? [new ErrorCampo(r["IdActivo"] is null ? "IdActivo" : "IdConcepto", "Un alojamiento activo necesita piso y concepto para importar sus reservas; si no se usa, desactívalo.")]
            : [];
        AntesDeGuardar = async (s, r, c) =>
            Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM AlojamientosAvaibook WHERE IdSociedad = ? AND IdExterno = ? AND IdAlojamientoAvaibook <> ?", c.Trabajo.IdSociedad,
                (r.Texto("IdExterno") ?? "").Trim(), r.Clave is null ? -1 : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture) > 0
                ? new Error("Alojamientos.Repetido", $"El alojamiento {r.Texto("IdExterno")} de Avaibook ya está en la lista.", null, null, "Ábrelo en la lista.")
                : null;
    }

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("IdExterno", "Id de Avaibook", TipoCampo.Codigo) { Obligatorio = true, SoloAlta = true, Longitud = 40, Ancho = "s",
            Ayuda = "Número del alojamiento en Avaibook (accommodationId). Lo rellena la importación al encontrar un alojamiento nuevo." },
        new("Nombre", "Nombre en Avaibook") { Longitud = 255, Ancho = "l", Ayuda = "Nombre del anuncio en Avaibook, para reconocerlo." },
        new("IdActivo", "Piso (activo)", TipoCampo.Lista) { Opciones = _activos, Ancho = "l",
            Ayuda = "Activo de la empresa al que corresponden sus reservas: agrupa las facturas (una por piso y mes) y da el centro de coste de la línea." },
        new("IdConcepto", "Concepto", TipoCampo.Lista) { Opciones = _conceptos, Ancho = "l",
            Ayuda = "Concepto de la línea de factura de sus reservas: nombre, cuenta de venta e IVA (por ejemplo «Alquiler turístico»)." },
        new("CausaExencion", "Tratamiento del IVA", TipoCampo.Lista) { Opciones = ProveedoresVentas.Causas, Ancho = "l",
            Ayuda = "E1 para el alquiler turístico sin servicios de hostelería (exento, IVA 0). Si se prestan servicios de hostelería, «Sujeta y no exenta» con el IVA del concepto." },
        new("Activo", "Importar sus reservas", TipoCampo.Logico) { Ayuda = "Sin marcar, sus reservas no se importan (por ejemplo, un anuncio de prueba o que ya no se usa)." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("IdExterno", "Id", 90), new("Nombre", "Nombre en Avaibook"), new("IdActivo", "Piso"), new("Activo", "Activo", 56)];

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        _activos = [new OpcionCampo(null, "Sin asignar"), .. (await _ventas.ActivosAsync(c.Trabajo.IdSociedad)).Select(a => new OpcionCampo(a.IdActivo, a.Nombre))];
        _conceptos = [new OpcionCampo(null, "Sin concepto"), .. (await _ventas.Conceptos.ListarAsync(c.Trabajo, soloActivos: true))
            .Select(x => new OpcionCampo(x.IdConcepto, x.CuentaVenta is null ? x.Nombre : $"{x.Nombre} ({x.CuentaVenta})"))];
        return await base.ListarAsync(c);
    }

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento c) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?> { ["Activo"] = true, ["CausaExencion"] = "E1" }));
}

/// <summary>Conexión de la empresa con la API de propietario de Avaibook: dirección y token (cifrado).</summary>
public sealed class ProveedorConexionAvaibook(IBaseDatos bd, IClienteAvaibook avaibook) : ProveedorBase
{
    private const string Tabla = "ConexionesAvaibook";

    public override string Clave => "conexionavaibook";
    public override string Titulo => "Conexión con Avaibook";
    public override string Categoria => "Facturación";
    public override string Finalidad => "Token de la API de Avaibook con que se importan las reservas como ventas por facturar.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override Cardinalidad Cardinalidad => Cardinalidad.Unico;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "conexión";
    public override IReadOnlyList<string> Sinonimos => ["avaibook", "api", "token", "reservas", "importar"];
    public override IReadOnlyList<AccionMantenimiento> Acciones =>
        [new("probar", "Probar la conexión", "Pide a Avaibook las reservas con entrada en los próximos 30 días, sin importar nada. Guarda antes los cambios.", Modifica: false)];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Activa", "Conexión activa", TipoCampo.Logico) { Ayuda = "Desactivada, no se puede importar de Avaibook." },
        new("Direccion", "Dirección de la API") { Obligatorio = true, Longitud = 255, Ancho = "l",
            Ayuda = "La de producción es https://api.avaibook.com/api/owner/. La de api.avaibook.biz es el entorno de pruebas, sin tus alojamientos." },
        new("Token", "Token", TipoCampo.Secreto) { Longitud = 200, Ancho = "l", Sinonimos = ["clave", "api key", "owner"],
            Ayuda = "La clave «owner» de app.avaibook.com › API. Se guarda cifrada y no se vuelve a mostrar. Déjalo vacío para conservar el guardado." },
        new("EstadoToken", "Token guardado") { SoloLectura = true, Ancho = "m", Ayuda = "Dice si hay un token guardado, sin mostrarlo." },
        new("ImportarDesde", "Importar entradas desde", TipoCampo.Fecha) { Ancho = "s",
            Ayuda = "Primera fecha de entrada que se importa. Las reservas anteriores vienen del ERP; si alguna coincide, se reconoce y no se duplica." },
        new("UltimaImportacion", "Última importación") { SoloLectura = true, Ancho = "l", Ayuda = "Cuándo y quién importó por última vez, y su resumen." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Direccion", "Dirección")];

    public override string TituloRegistro(RegistroMantenimiento r) => "Conexión con Avaibook";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) =>
        [await LeerAsync(c.Trabajo.IdSociedad) ?? await NuevoAsync(c)];

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento c) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?>
        {
            ["Activa"] = true, ["Direccion"] = ClienteAvaibookHttp.DireccionProduccion, ["Token"] = null, ["EstadoToken"] = "No",
            ["ImportarDesde"] = new DateTime(DateTime.Today.Year, 1, 1), ["UltimaImportacion"] = "Nunca",
        }));

    private Task<RegistroMantenimiento?> LeerAsync(string idSociedad) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            $"SELECT IdSociedad, Activa, Direccion, SecretoProtegido, ImportarDesde, UltimaImportacion, OperadorUltimaImportacion, ResumenUltimaImportacion FROM {Tabla} WHERE IdSociedad = ?", f =>
            {
                var secreto = f.Texto("SecretoProtegido");
                return new RegistroMantenimiento((f.Texto("IdSociedad") ?? "").Trim(), new Dictionary<string, object?>
                {
                    ["Activa"] = f.Logico("Activa"), ["Direccion"] = f.Texto("Direccion"), ["Token"] = null,
                    ["EstadoToken"] = string.IsNullOrWhiteSpace(secreto) ? "No" : SecretoProtegido.Desproteger(secreto) is null ? "No se puede leer: escríbelo de nuevo" : "Sí",
                    ["ImportarDesde"] = f.FechaNula("ImportarDesde"),
                    ["UltimaImportacion"] = f.FechaNula("UltimaImportacion") is DateTime u
                        ? $"{u:dd/MM/yyyy HH:mm} · {f.Texto("OperadorUltimaImportacion")} · {f.Texto("ResumenUltimaImportacion")}" : "Nunca",
                });
            }, idSociedad));

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var errores = ValidacionGenerica.Validar(Campos, r).ToList();
        if (r.Logico("Activa") && r.Texto("Token") is null && r.Texto("EstadoToken") != "Sí")
            errores.Add(new("Token", "Falta el token de Avaibook (o el guardado no se puede leer): pega la clave «owner» de app.avaibook.com › API."));
        if (r.Texto("Direccion") is string d && !Uri.TryCreate(d, UriKind.Absolute, out var u) | (u is not null && u.Scheme != Uri.UriSchemeHttps))
            errores.Add(new("Direccion", "La dirección debe ser una URL https, como https://api.avaibook.com/api/owner/."));
        return errores;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var sociedad = c.Trabajo.IdSociedad;
        var token = (r["Token"] as string)?.Trim();
        await bd.EnTransaccionAsync(async s =>
        {
            var existe = Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM {Tabla} WHERE IdSociedad = ?", sociedad), CultureInfo.InvariantCulture) > 0;
            if (!existe)
                await s.EjecutarAsync($"INSERT INTO {Tabla} (IdSociedad, Activa, Direccion) VALUES (?, ?, ?)", sociedad, r.Logico("Activa"), r.Texto("Direccion"));
            await s.EjecutarAsync($"UPDATE {Tabla} SET Activa = ?, Direccion = ?, ImportarDesde = ? WHERE IdSociedad = ?",
                r.Logico("Activa"), r.Texto("Direccion"), r["ImportarDesde"] as DateTime?, sociedad);
            // Con un token nuevo, sustituye al guardado; vacío, se conserva.
            if (!string.IsNullOrEmpty(token))
                await s.EjecutarAsync($"UPDATE {Tabla} SET SecretoProtegido = ? WHERE IdSociedad = ?", SecretoProtegido.Proteger(token), sociedad);
            return true;
        });
        return Resultado<RegistroMantenimiento>.Correcto((await LeerAsync(sociedad))!);
    }

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento c)
    {
        if (accion != "probar") return await base.EjecutarAsync(accion, registro, c);
        var fila = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT Direccion, SecretoProtegido FROM {Tabla} WHERE IdSociedad = ?",
            f => (Direccion: f.Texto("Direccion"), Token: SecretoProtegido.Desproteger(f.Texto("SecretoProtegido"))), c.Trabajo.IdSociedad));
        if (fila.Direccion is null || string.IsNullOrWhiteSpace(fila.Token))
            return Resultado<ResultadoAccion>.Fallo(new Error("Avaibook.SinToken", "Aún no hay token guardado.", Titulo, null, "Pega el token, guarda y vuelve a probar."));
        var hoy = DateTime.Today;
        var r = await avaibook.ReservasAsync(fila.Direccion, fila.Token, hoy, hoy.AddDays(30));
        if (!r.EsCorrecto) return Resultado<ResultadoAccion>.Fallo(r.Errores);
        var alojamientos = r.Valor.Select(x => x.AccommodationId).Distinct().Count();
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(r.Valor.Count == 0
            ? "Avaibook acepta el token, pero no hay reservas con entrada en los próximos 30 días. Si esperabas alguna, comprueba que la dirección es la de producción."
            : $"Conexión correcta: {r.Valor.Count} {(r.Valor.Count == 1 ? "reserva" : "reservas")} con entrada en los próximos 30 días en {alojamientos} {(alojamientos == 1 ? "alojamiento" : "alojamientos")}."));
    }
}
