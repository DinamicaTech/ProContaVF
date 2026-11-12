using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;
using ProConta.Nucleo.Seguridad;

namespace ProConta.Correo.Mantenimiento;

/// <summary>Proveedores de Parámetros y tablas del área de correo.</summary>
public static class ProveedoresCorreo
{
    public static IEnumerable<IProveedorMantenimiento> Todos(IBaseDatos bd, ITransporteSmtp? smtp = null, IPruebaPop3? pop3 = null) =>
        [new ProveedorCuentaCorreo(bd, smtp ?? new TransporteSmtpMailKit(), pop3 ?? new PruebaPop3MailKit()), CrearPlantillas(bd), CrearRemitentes(bd)];

    /// <summary>
    /// Remitentes autorizados del ejercicio (correo/recepcion 2026-11-10 19:40): solo el correo que envían ellos a la cuenta del
    /// ejercicio se procesa y se responde, con permisos separados para enviar documentos a importar y para pedir informes.
    /// </summary>
    public static ProveedorTablaAccess CrearRemitentes(IBaseDatos bd) => new ProveedorRemitentes(bd, "remitentescorreo", "Remitentes autorizados", "Correo",
        "Quién puede enviar documentos (facturas de compra, extractos, nóminas) o pedir informes por correo a la cuenta de este ejercicio. Lo que envía cualquier otro se guarda sin procesar.",
        AmbitoMantenimiento.Ejercicio, "remitente", "RemitentesAutorizadosCorreo", "IdRemitenteAutorizado", ClaveNueva.Autonumerica,
        [
            new("Direccion", "Dirección", TipoCampo.Email) { Obligatorio = true, Longitud = 254, Ancho = "l", Ayuda = "Dirección desde la que escribe (la del remitente del correo)." },
            new("Nombre", "Nombre") { Longitud = 150, Ancho = "l", Ayuda = "Para reconocerlo en la lista y en las respuestas." },
            new("PuedeImportar", "Puede enviar documentos para importar", TipoCampo.Logico) { Ayuda = "Sus facturas de compra se registran (y se contabilizan si se leen bien); sus extractos y nóminas, cuando lleguen esos módulos." },
            new("PuedePedirInformes", "Puede pedir informes", TipoCampo.Logico) { Ayuda = "Puede escribir un comando en el asunto (por ejemplo «MAYOR 4300001 PDF») cuando los informes por correo estén disponibles." },
            new("Activo", "Activo", TipoCampo.Logico) { Ayuda = "Desactivado, su correo se guarda sin procesar, como el de cualquier remitente no autorizado." },
        ],
        [new("Direccion", "Dirección"), new("Nombre", "Nombre")])
    {
        ColumnaAmbito = "IdSociedadEjercicio",
        Orden = "Direccion",
        Alias = ["remitente", "autorizado", "correo", "email", "buzon", "recepcion"],
        Titular = r => r.EsNuevo ? "Remitente nuevo" : r.Texto("Nombre") ?? r.Texto("Direccion") ?? "Remitente",
    };

    /// <summary>Plantillas de asunto y cuerpo por empresa y tipo de correo (PlantillasCorreo).</summary>
    public static ProveedorTablaAccess CrearPlantillas(IBaseDatos bd) => new(bd, "plantillascorreo", "Plantillas de correo", "Correo",
        "Asunto y texto con que se preparan los correos de la empresa (por ejemplo, el de enviar una factura). Las variables van entre llaves.",
        AmbitoMantenimiento.Sociedad, "plantilla", "PlantillasCorreo", "IdPlantillaCorreo", ClaveNueva.Autonumerica,
        [
            new("TipoCorreo", "Tipo de correo", TipoCampo.Lista)
            {
                Obligatorio = true, SoloAlta = true, Ancho = "m",
                Opciones = [new(TiposCorreo.Factura, "Factura"), new(TiposCorreo.Otro, "Otro")],
                Ayuda = "Para qué correo es: «Factura» es el que se prepara al enviar una factura. Solo hay una plantilla por tipo y empresa.",
            },
            new("Asunto", "Asunto") { Obligatorio = true, Longitud = 255, Ancho = "l", Ayuda = "Variables de las facturas: " + ServicioPlantillasCorreo.VariablesFactura + "." },
            new("Cuerpo", "Texto", TipoCampo.TextoLargo) { Ancho = "f", Ayuda = "En HTML (<p>, <br>, <b>…) o texto, según el formato. Variables de las facturas: " + ServicioPlantillasCorreo.VariablesFactura + "." },
            new("FormatoCuerpo", "Formato", TipoCampo.Lista) { Ayuda = "HTML permite negritas, párrafos y enlaces en el texto; Texto lo envía sin formato.", Obligatorio = true, Ancho = "s", Opciones = [new("HTML", "HTML"), new("TEXTO", "Texto")] },
            new("Activo", "Activa", TipoCampo.Logico) { Ayuda = "Desactivada, se usa la plantilla del programa." },
        ],
        [new("TipoCorreo", "Tipo"), new("Asunto", "Asunto")])
    {
        ColumnaAmbito = "IdSociedad",
        Orden = "TipoCorreo",
        Alias = ["plantilla", "correo", "email", "asunto", "mensaje"],
        Titular = r => r.EsNuevo ? "Plantilla nueva" : $"Plantilla de {(r.Texto("TipoCorreo") == TiposCorreo.Factura ? "factura" : (r.Texto("TipoCorreo") ?? "correo").ToLowerInvariant())}",
    };
}

/// <summary>Remitentes autorizados: uno nuevo nace activo y con permiso para enviar documentos.</summary>
internal sealed class ProveedorRemitentes(IBaseDatos bd, string clave, string titulo, string categoria, string finalidad, AmbitoMantenimiento ambito,
    string nombreRegistro, string tabla, string columnaClave, ClaveNueva claveNueva, IReadOnlyList<CampoMantenimiento> campos, IReadOnlyList<ColumnaLista> columnas)
    : ProveedorTablaAccess(bd, clave, titulo, categoria, finalidad, ambito, nombreRegistro, tabla, columnaClave, claveNueva, campos, columnas)
{
    public override async Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto)
    {
        var r = await base.NuevoAsync(contexto);
        r["Activo"] = true;
        r["PuedeImportar"] = true;
        return r;
    }
}

/// <summary>
/// Cuenta de correo del ejercicio de trabajo (CuentasCorreo, correo/envio 2026-11-10 19:40): dirección, servidor SMTP de
/// salida y POP3 de entrada, ambos cifrados, y la contraseña, que se guarda cifrada y no vuelve a la pantalla (solo se
/// sustituye). «Probar envío» y «Probar recepción» se conectan e inician sesión sin enviar ni descargar nada.
/// </summary>
public sealed class ProveedorCuentaCorreo(IBaseDatos bd, ITransporteSmtp smtp, IPruebaPop3 pop3) : ProveedorBase
{
    private const string Tabla = "CuentasCorreo";
    private static readonly IReadOnlyList<OpcionCampo> Seguridades =
        [new("STARTTLS", "STARTTLS (puertos 587 y 110)"), new("TLS", "TLS implícito (puertos 465 y 995)")];

    public override string Clave => "cuentacorreo";
    public override string Titulo => "Cuenta de correo";
    public override string Categoria => "Correo";
    public override string Finalidad => "Cuenta con que la empresa envía y recibe correo en este ejercicio (por ejemplo, empresa2026@dominio.es): servidores, seguridad y contraseña.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Ejercicio;
    public override Cardinalidad Cardinalidad => Cardinalidad.Unico;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "cuenta de correo";
    public override IReadOnlyList<string> Sinonimos => ["correo", "email", "smtp", "pop3", "servidor", "contraseña", "buzon", "cuenta"];
    public override IReadOnlyList<GrupoCampos> Grupos => [new("cta", "Cuenta"), new("smtp", "Envío (SMTP)"), new("pop", "Recepción (POP3)")];
    public override IReadOnlyList<AccionMantenimiento> Acciones =>
    [
        new("probarenvio", "Probar envío", "Se conecta al servidor SMTP con la seguridad elegida e inicia sesión con la contraseña guardada, sin enviar nada. Guarda antes los cambios.", Modifica: false),
        new("probarrecepcion", "Probar recepción", "Se conecta al servidor POP3 con la seguridad elegida e inicia sesión, sin descargar ni borrar nada. Guarda antes los cambios.", Modifica: false),
    ];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Activa", "Cuenta activa", TipoCampo.Logico) { Grupo = "cta", Ayuda = "Desactivada, no se envía ni se recibe nada con esta cuenta; los correos pendientes esperan." },
        new("Direccion", "Dirección", TipoCampo.Email) { Grupo = "cta", Obligatorio = true, Longitud = 254, Ancho = "l", Ayuda = "La que ven los destinatarios como remitente y a la que se escribe para enviar documentos a ProContaVF." },
        new("NombreVisible", "Nombre del remitente") { Grupo = "cta", Longitud = 150, Ancho = "l", Ayuda = "Nombre que ven los destinatarios junto a la dirección. Vacío, solo se ve la dirección." },
        new("Usuario", "Usuario") { Grupo = "cta", Longitud = 254, Ancho = "l", Ayuda = "Con el que se inicia sesión en el servidor. Vacío, se usa la dirección." },
        new("Contrasena", "Contraseña", TipoCampo.Secreto) { Grupo = "cta", Longitud = 200, Ancho = "m", Sinonimos = ["password", "clave"],
            Ayuda = "Se guarda cifrada en la BD y no se vuelve a mostrar. Déjala vacía para conservar la guardada." },
        new("EstadoContrasena", "Contraseña guardada") { Ayuda = "Dice si hay una contraseña guardada, sin mostrarla. Se cambia en «Contraseña».", Grupo = "cta", SoloLectura = true, Ancho = "m" },
        new("ResponderA", "Responder a", TipoCampo.Email) { Grupo = "cta", Longitud = 254, Ancho = "l", Ayuda = "Dirección a la que llegan las respuestas, si no es la de la cuenta. Vacía, a la de la cuenta." },
        new("ServidorSmtp", "Servidor SMTP") { Ayuda = "Servidor de correo saliente que da tu proveedor de correo, por ejemplo smtp.ionos.es.", Grupo = "smtp", Obligatorio = true, Longitud = 255, Ancho = "l", Sinonimos = ["smtp"] },
        new("PuertoSmtp", "Puerto", TipoCampo.Entero) { Ayuda = "Puerto del servidor de correo saliente: normalmente 587 (STARTTLS) o 465 (SSL).", Grupo = "smtp", Obligatorio = true, Minimo = 1, Maximo = 65535, Ancho = "xs" },
        new("SeguridadSmtp", "Seguridad", TipoCampo.Lista) { Grupo = "smtp", Obligatorio = true, Opciones = Seguridades, Ancho = "m",
            Ayuda = "La conexión siempre va cifrada: si el servidor no lo admite, no se envía la contraseña." },
        new("ServidorPop3", "Servidor POP3") { Ayuda = "Servidor de correo entrante que da tu proveedor de correo, por ejemplo pop.ionos.es. Vacío, no se reciben correos en esta cuenta.", Grupo = "pop", Longitud = 255, Ancho = "l", Sinonimos = ["pop3", "pop"] },
        new("PuertoPop3", "Puerto", TipoCampo.Entero) { Ayuda = "Puerto del servidor de correo entrante: normalmente 995 (SSL).", Grupo = "pop", Obligatorio = true, Minimo = 1, Maximo = 65535, Ancho = "xs" },
        new("SeguridadPop3", "Seguridad", TipoCampo.Lista) { Ayuda = "Cómo se cifra la conexión con el servidor de correo entrante; la indica tu proveedor de correo.", Grupo = "pop", Obligatorio = true, Opciones = Seguridades, Ancho = "m" },
        new("EliminarTrasProcesar", "Borrar del servidor lo ya procesado", TipoCampo.Logico) { Grupo = "pop",
            Ayuda = "Un correo recibido se borra del buzón cuando se ha descargado y procesado entero; los que quedan con incidencia se conservan." },
        new("IntervaloRecepcionMinutos", "Mirar el buzón cada (minutos)", TipoCampo.Entero) { Ayuda = "Cada cuántos minutos se descargan los correos nuevos mientras la aplicación está abierta.", Grupo = "pop", Obligatorio = true, Minimo = 1, Maximo = 1440, Ancho = "xs" },
        new("TiempoEsperaSegundos", "Tiempo de espera (segundos)", TipoCampo.Entero) { Grupo = "smtp", Obligatorio = true, Minimo = 10, Maximo = 600, Ancho = "xs",
            Ayuda = "Cuánto se espera la respuesta del servidor antes de dar el intento por fallido." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Direccion", "Dirección")];

    public override string TituloRegistro(RegistroMantenimiento r) => "Cuenta de correo";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) =>
        [await LeerAsync(c.Trabajo.IdSociedadEjercicio) ?? await NuevoAsync(c)];

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?>
        {
            ["Activa"] = true, ["Direccion"] = null, ["NombreVisible"] = contexto.RazonSocial, ["Usuario"] = null, ["Contrasena"] = null, ["EstadoContrasena"] = "No",
            ["ResponderA"] = null, ["ServidorSmtp"] = null, ["PuertoSmtp"] = 587, ["SeguridadSmtp"] = "STARTTLS", ["ServidorPop3"] = null, ["PuertoPop3"] = 110,
            ["SeguridadPop3"] = "STARTTLS", ["EliminarTrasProcesar"] = true, ["IntervaloRecepcionMinutos"] = 5, ["TiempoEsperaSegundos"] = 60,
        }));

    private Task<RegistroMantenimiento?> LeerAsync(int idSociedadEjercicio) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT {RepositorioCuentasCorreo.Columnas} FROM {Tabla} WHERE IdSociedadEjercicio = ?", f =>
        {
            var secreto = f.Texto("SecretoProtegido");
            return new RegistroMantenimiento(f.Entero("IdCuentaCorreo"), new Dictionary<string, object?>
            {
                ["Activa"] = f.Logico("Activa"), ["Direccion"] = f.Texto("Direccion"), ["NombreVisible"] = f.Texto("NombreVisible"), ["Usuario"] = f.Texto("Usuario"),
                ["Contrasena"] = null,
                ["EstadoContrasena"] = string.IsNullOrWhiteSpace(secreto) ? "No" : SecretoProtegido.Desproteger(secreto) is null ? "No se puede leer: escríbela de nuevo" : "Sí",
                ["ResponderA"] = f.Texto("ResponderA"), ["ServidorSmtp"] = f.Texto("ServidorSmtp"), ["PuertoSmtp"] = f.Entero("PuertoSmtp"),
                ["SeguridadSmtp"] = f.Texto("SeguridadSmtp"), ["ServidorPop3"] = f.Texto("ServidorPop3"), ["PuertoPop3"] = f.Entero("PuertoPop3"),
                ["SeguridadPop3"] = f.Texto("SeguridadPop3"), ["EliminarTrasProcesar"] = f.Logico("EliminarTrasProcesar"),
                ["IntervaloRecepcionMinutos"] = f.Entero("IntervaloRecepcionMinutos"), ["TiempoEsperaSegundos"] = f.Entero("TiempoEsperaSegundos"),
            });
        }, idSociedadEjercicio));

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var errores = ValidacionGenerica.Validar(Campos, r).ToList();
        if (r.Logico("Activa") && r.Texto("Contrasena") is null && r.Texto("EstadoContrasena") != "Sí")
            errores.Add(new("Contrasena", "Falta la contraseña de la cuenta (o la guardada no se puede leer): escríbela."));
        if (r.Texto("Direccion") is { } d && ReglasMensaje.Normalizar(d) is { Direccion: null, Motivo: var m }) errores.Add(new("Direccion", $"Dirección: {m}"));
        return errores;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var se = c.Trabajo.IdSociedadEjercicio;
        var contrasena = r["Contrasena"] as string;
        var operador = c.Actor.Nombre.Length > 30 ? c.Actor.Nombre[..30] : c.Actor.Nombre;
        object?[] valores =
        [
            r.Logico("Activa"), r.Texto("Direccion"), r.Texto("NombreVisible"), r.Texto("Usuario"), r.Texto("ResponderA"), r.Texto("ServidorSmtp"),
            r.Entero("PuertoSmtp") ?? 587, r.Texto("SeguridadSmtp") ?? "STARTTLS", r.Texto("ServidorPop3"), r.Entero("PuertoPop3") ?? 110, r.Texto("SeguridadPop3") ?? "STARTTLS",
            r.Logico("EliminarTrasProcesar"), r.Entero("IntervaloRecepcionMinutos") ?? 5, r.Entero("TiempoEsperaSegundos") ?? 60, DateTime.Now, operador,
        ];
        const string asignaciones = "Activa = ?, Direccion = ?, NombreVisible = ?, Usuario = ?, ResponderA = ?, ServidorSmtp = ?, PuertoSmtp = ?, SeguridadSmtp = ?, " +
            "ServidorPop3 = ?, PuertoPop3 = ?, SeguridadPop3 = ?, EliminarTrasProcesar = ?, IntervaloRecepcionMinutos = ?, TiempoEsperaSegundos = ?, FechaModificacion = ?, OperadorModificacion = ?";
        await bd.EnTransaccionAsync(async s =>
        {
            var existe = await s.ConsultarPrimeroAsync($"SELECT IdCuentaCorreo FROM {Tabla} WHERE IdSociedadEjercicio = ?", f => (int?)f.Entero("IdCuentaCorreo"), se);
            if (existe is null)
                await s.EjecutarAsync($"INSERT INTO {Tabla} (IdSociedadEjercicio, Activa, Direccion, PuertoSmtp, SeguridadSmtp, PuertoPop3, SeguridadPop3, EliminarTrasProcesar, " +
                    "IntervaloRecepcionMinutos, TiempoEsperaSegundos, Version) VALUES (?, ?, ?, 587, 'STARTTLS', 110, 'STARTTLS', True, 5, 60, 0)", se, r.Logico("Activa"), r.Texto("Direccion"));
            await s.EjecutarAsync($"UPDATE {Tabla} SET {asignaciones}, Version = Version + 1 WHERE IdSociedadEjercicio = ?", [.. valores, se]);
            // Con una contraseña nueva, sustituye a la guardada; vacía, se conserva.
            if (!string.IsNullOrEmpty(contrasena))
                await s.EjecutarAsync($"UPDATE {Tabla} SET SecretoProtegido = ? WHERE IdSociedadEjercicio = ?", SecretoProtegido.Proteger(contrasena), se);
            return true;
        });
        return Resultado<RegistroMantenimiento>.Correcto((await LeerAsync(se))!);
    }

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento c)
    {
        if (accion is not ("probarenvio" or "probarrecepcion")) return await base.EjecutarAsync(accion, registro, c);
        var cuenta = await new RepositorioCuentasCorreo(bd).ObtenerPorEjercicioAsync(c.Trabajo.IdSociedadEjercicio);
        if (cuenta is null)
            return Resultado<ResultadoAccion>.Fallo(new Error("Correo.SinCuenta", "Aún no hay cuenta de correo guardada en este ejercicio.", Titulo, null, "Rellénala, guarda y vuelve a probar."));
        var prueba = accion == "probarenvio" ? await smtp.ProbarAsync(cuenta) : await pop3.ProbarAsync(cuenta);
        return prueba.Correcta
            ? Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(prueba.Mensaje))
            : Resultado<ResultadoAccion>.Fallo(new Error("Correo.PruebaFallida", prueba.Mensaje, cuenta.Direccion, null, prueba.AccionCorrectora));
    }
}
