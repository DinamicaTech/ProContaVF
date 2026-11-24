using ProConta.Compras.Integraciones;
using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;
using ProConta.Nucleo.Seguridad;

namespace ProConta.Compras.Mantenimiento;

/// <summary>
/// Parámetros › Integraciones › «AWS · Conexión» (plataforma/mantenimiento/conexion-aws 2026-11-24; regla «Integraciones»): la región y
/// las claves de AWS de la sociedad (ConexionesAws), que antes estaban en la configuración del reconocimiento de compras. Las usa el
/// OCR de Textract y las usará lo que vaya después sobre AWS. El secreto se guarda cifrado (<see cref="SecretoProtegido"/>) y nunca
/// vuelve a la pantalla: solo se sustituye o se quita. Sin claves, cada equipo usa su perfil de AWS «proconta-textract».
/// </summary>
public sealed class ProveedorConexionAws(IBaseDatos bd, Func<CredencialesOcr?, CancellationToken, Task<(bool Correcta, string Mensaje)>>? probar = null) : ProveedorBase
{
    private const string Tabla = "ConexionesAws";

    public override string Clave => "conexionaws";
    public override string Titulo => "AWS · Conexión";
    public override string Categoria => "Integraciones";
    public override string Finalidad => "Región y claves de Amazon Web Services de la empresa. Las usa el OCR de las facturas de compra escaneadas (Amazon Textract).";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override Cardinalidad Cardinalidad => Cardinalidad.Unico;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "conexión";
    public override IReadOnlyList<string> Sinonimos => ["aws", "amazon", "textract", "ocr", "credenciales", "access key", "iam", "claves"];
    public override IReadOnlyList<AccionMantenimiento> Acciones =>
        [
            new("probar", "Probar la conexión", "Manda a Amazon Textract una página de prueba con las claves guardadas (o, sin ellas, con el perfil de AWS de este equipo) y dice si la lee. Cuesta lo que una página de OCR. Guarda antes los cambios.", Modifica: false),
            new("quitarclaves", "Quitar las claves", "Borra la clave de acceso y la secreta guardadas: el OCR usará el perfil de AWS de cada equipo, si lo tiene."),
        ];

    /// <summary>Cómo conseguir las claves en la consola de AWS (lo pidió el propietario el 2026-11-24).</summary>
    public static IReadOnlyList<ApartadoExplicacion> ComoObtenerClaves { get; } =
    [
        new("Qué necesitas", "Una cuenta de AWS (aws.amazon.com) y, dentro, un usuario solo para el programa que únicamente pueda usar Amazon Textract. " +
            "No uses las claves del usuario raíz ni las de un administrador: el programa guarda el secreto cifrado, pero quien tenga la BD y el programa podría usarlo."),
        new("1. Crear el usuario", "Entra en console.aws.amazon.com › busca «IAM» › Usuarios › «Crear usuario». Ponle un nombre (por ejemplo proconta-textract) " +
            "y deja SIN marcar «Proporcionar acceso a la consola de administración de AWS»."),
        new("2. Darle solo el permiso de Textract", "En «Establecer permisos» elige «Adjuntar políticas directamente» › «Crear política» › pestaña JSON y pega:\n" +
            "{ \"Version\": \"2012-10-17\", \"Statement\": [ { \"Effect\": \"Allow\", \"Action\": \"textract:DetectDocumentText\", \"Resource\": \"*\" } ] }\n" +
            "Guárdala (por ejemplo ProContaTextract), vuelve al usuario, márcala y termina de crearlo. Textract no hay que «activarlo»: basta con este permiso."),
        new("3. Crear la clave de acceso", "Abre el usuario › pestaña «Credenciales de seguridad» › «Claves de acceso» › «Crear clave de acceso». " +
            "Como caso de uso elige «Aplicación que se ejecuta fuera de AWS». AWS muestra la «Clave de acceso» (empieza por AKIA) y la «Clave de acceso secreta»: " +
            "cópialas aquí. La secreta solo se ve en ese momento; si la pierdes, crea otra clave y desactiva la anterior."),
        new("4. Región", "Escribe la región donde se usará Textract, por ejemplo eu-west-1 (Irlanda) o eu-central-1 (Fráncfort). " +
            "No hace falta elegirla en la consola: sirve cualquiera en la que exista Textract."),
        new("5. Comprobarlo", "Guarda y pulsa «Probar la conexión». Cada página leída con Textract se paga (la prueba cuesta una página); " +
            "el gasto se ve en la consola › «Billing and Cost Management»."),
    ];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Region", "Región de AWS", TipoCampo.Codigo) { Longitud = 30, Ancho = "s", Sinonimos = ["region"], Explicacion = ComoObtenerClaves,
            Ayuda = "Por ejemplo eu-west-1. Obligatoria si se guarda una clave de acceso. Haz clic en la etiqueta para ver cómo conseguir las claves." },
        new("ClaveAcceso", "Clave de acceso (Access key ID)", TipoCampo.Codigo) { Longitud = 128, Ancho = "l", Sinonimos = ["access key", "credenciales"], Explicacion = ComoObtenerClaves,
            Ayuda = "De un usuario de AWS que solo pueda usar Textract (DetectDocumentText). Vacía, cada equipo usa su perfil de AWS «proconta-textract». Haz clic en la etiqueta para ver cómo conseguirla." },
        new("Secreto", "Clave secreta (Secret access key)", TipoCampo.Secreto) { Longitud = 128, Ancho = "l", Sinonimos = ["secret", "secreto"], Explicacion = ComoObtenerClaves,
            Ayuda = "Se guarda cifrada y no se vuelve a mostrar. Déjala vacía para conservar la guardada." },
        new("EstadoSecreto", "Clave secreta guardada") { Ayuda = "Dice si hay una clave secreta de AWS guardada, sin mostrarla. Se cambia en «Clave secreta».", SoloLectura = true, Ancho = "m" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("ClaveAcceso", "Clave de acceso")];

    public override string TituloRegistro(RegistroMantenimiento r) => "Conexión con AWS";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) =>
        [await LeerAsync(c.Trabajo.IdSociedad) ?? await NuevoAsync(c)];

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?>
        {
            ["Region"] = null, ["ClaveAcceso"] = null, ["Secreto"] = null, ["EstadoSecreto"] = "No",
        }));

    private Task<RegistroMantenimiento?> LeerAsync(string idSociedad) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync($"SELECT IdSociedad, Region, ClaveAcceso, SecretoProtegido FROM {Tabla} WHERE IdSociedad = ?",
            r => new RegistroMantenimiento((r.Texto("IdSociedad") ?? "").Trim(), new Dictionary<string, object?>
            {
                ["Region"] = r.Texto("Region"), ["ClaveAcceso"] = r.Texto("ClaveAcceso"), ["Secreto"] = null,
                ["EstadoSecreto"] = string.IsNullOrWhiteSpace(r.Texto("SecretoProtegido")) ? "No"
                    : SecretoProtegido.Desproteger(r.Texto("SecretoProtegido")) is null ? "No se puede leer: escríbela de nuevo" : "Sí",
            }), idSociedad));

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var errores = ValidacionGenerica.Validar(Campos, r).ToList();
        var clave = r.Texto("ClaveAcceso");
        var secreto = r.Texto("Secreto");
        var region = r.Texto("Region");
        if (clave is not null && region is null) errores.Add(new("Region", "Falta la región de AWS (por ejemplo eu-west-1)."));
        if (region is not null && !System.Text.RegularExpressions.Regex.IsMatch(region, "^[a-z]{2}(-[a-z]+)+-[0-9]$"))
            errores.Add(new("Region", "Región de AWS: escríbela como eu-west-1."));
        if (clave is not null && secreto is null && r.Texto("EstadoSecreto") != "Sí")
            errores.Add(new("Secreto", "Falta la clave secreta de AWS: se copia al crear la clave de acceso (haz clic en la etiqueta para ver cómo)."));
        if (clave is null && secreto is not null) errores.Add(new("ClaveAcceso", "Falta la clave de acceso que va con la clave secreta."));
        return errores;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var idSociedad = c.Trabajo.IdSociedad;
        var clave = r.Texto("ClaveAcceso");
        var secreto = r.Texto("Secreto");
        await bd.EnTransaccionAsync(async s =>
        {
            var existe = await s.ConsultarPrimeroAsync($"SELECT IdSociedad FROM {Tabla} WHERE IdSociedad = ?", f => f.Texto("IdSociedad"), idSociedad) is not null;
            if (!existe) await s.EjecutarAsync($"INSERT INTO {Tabla} (IdSociedad) VALUES (?)", idSociedad);
            await s.EjecutarAsync($"UPDATE {Tabla} SET Region = ?, ClaveAcceso = ? WHERE IdSociedad = ?", r.Texto("Region"), clave, idSociedad);
            // Sin clave de acceso no se guarda ningún secreto; con un secreto nuevo, sustituye al anterior.
            if (clave is null) await s.EjecutarAsync($"UPDATE {Tabla} SET SecretoProtegido = Null WHERE IdSociedad = ?", idSociedad);
            else if (secreto is not null) await s.EjecutarAsync($"UPDATE {Tabla} SET SecretoProtegido = ? WHERE IdSociedad = ?", SecretoProtegido.Proteger(secreto), idSociedad);
            return true;
        });
        return Resultado<RegistroMantenimiento>.Correcto((await LeerAsync(idSociedad))!);
    }

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento c)
    {
        if (accion == "probar") return await ProbarAsync(bd, c.Trabajo.IdSociedad, probar, Titulo);
        if (accion != "quitarclaves") return await base.EjecutarAsync(accion, registro, c);
        await bd.EjecutarAsync(s => s.EjecutarAsync($"UPDATE {Tabla} SET ClaveAcceso = Null, SecretoProtegido = Null WHERE IdSociedad = ?", c.Trabajo.IdSociedad));
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion("Claves de AWS quitadas: el OCR usará el perfil de AWS de cada equipo."));
    }

    /// <summary>
    /// Manda a Textract la página de prueba con las claves guardadas de la sociedad (o, sin ellas, el perfil del puesto). Lo comparten
    /// «Probar la conexión» de aquí y «Probar el OCR» del reconocimiento de compras.
    /// </summary>
    internal static async Task<Resultado<ResultadoAccion>> ProbarAsync(IBaseDatos bd, string idSociedad,
        Func<CredencialesOcr?, CancellationToken, Task<(bool Correcta, string Mensaje)>>? probar, string titulo)
    {
        var (credenciales, motivo) = await bd.EjecutarAsync(s => CredencialesAsync(s, idSociedad));
        if (motivo is not null)
            return Resultado<ResultadoAccion>.Fallo(new Error("Aws.ClavesNoValen", $"No puedo usar las claves de AWS: {motivo}.", titulo, null,
                "Corrígelas en Parámetros y tablas › Integraciones › «AWS · Conexión» y guarda."));
        var (correcta, mensaje) = await (probar ?? ExtractorOcrTextract.ProbarAsync)(credenciales, CancellationToken.None);
        return correcta ? Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(mensaje))
            : Resultado<ResultadoAccion>.Fallo(new Error("Aws.OcrNoFunciona", mensaje, titulo, null,
                "Revisa en Parámetros y tablas › Integraciones › «AWS · Conexión» la región y las claves, y que el usuario de AWS pueda usar Textract (DetectDocumentText)."));
    }

    /// <summary>
    /// Las claves de AWS guardadas de la sociedad, descifradas; sin clave de acceso, ninguna (se usa el perfil del puesto). El motivo
    /// dice por qué las guardadas no valen.
    /// </summary>
    public static async Task<(CredencialesOcr? Credenciales, string? Motivo)> CredencialesAsync(ISesionDatos s, string idSociedad)
    {
        var fila = await s.ConsultarPrimeroAsync($"SELECT Region, ClaveAcceso, SecretoProtegido FROM {Tabla} WHERE IdSociedad = ?",
            r => (Region: r.Texto("Region"), Clave: r.Texto("ClaveAcceso"), Secreto: r.Texto("SecretoProtegido")), idSociedad);
        return Credenciales(fila.Region, fila.Clave, fila.Secreto);
    }

    /// <summary>Las claves a partir de lo guardado; sin clave de acceso, ninguna.</summary>
    internal static (CredencialesOcr? Credenciales, string? Motivo) Credenciales(string? region, string? claveAcceso, string? secretoProtegido)
    {
        if (string.IsNullOrWhiteSpace(claveAcceso)) return (null, null);
        if (string.IsNullOrWhiteSpace(region)) return (null, "las claves de AWS guardadas no dicen la región");
        if (SecretoProtegido.Desproteger(secretoProtegido) is not { Length: > 0 } secreto)
            return (null, "no puedo descifrar la clave secreta de AWS guardada: vuelve a escribirla en Integraciones › «AWS · Conexión»");
        return (new CredencialesOcr(region.Trim(), claveAcceso.Trim(), secreto), null);
    }
}
