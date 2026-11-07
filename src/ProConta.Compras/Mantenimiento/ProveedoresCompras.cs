using System.Globalization;
using ProConta.Compras.Facturas;
using ProConta.Compras.Integraciones;
using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Mantenimiento;

/// <summary>Mantenimientos de compras para la pantalla genérica (plataforma/mantenimiento).</summary>
public static class ProveedoresCompras
{
    public static IReadOnlyList<IProveedorMantenimiento> Todos(IBaseDatos bd) =>
        [new ProveedorTextosClaveCompra(new CatalogoTextosClaveCompra(bd)), new ProveedorReconocimientoCompra(bd)];
}

/// <summary>
/// Textos clave de compras de la empresa de trabajo (TextosClaveCompra, compras/pantallas 2026-11-06 17:02): el texto que se
/// busca en el contenido y la ruta de los PDF y la cuenta de gasto y el centro de coste que asigna.
/// </summary>
public sealed class ProveedorTextosClaveCompra(CatalogoTextosClaveCompra catalogo) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _centros = [];

    public override string Clave => "textosclavecompra";
    public override string Titulo => "Textos clave de compras";
    public override string Categoria => "Catálogos";
    public override string Finalidad => "Textos que, si aparecen en el contenido o la ruta de un PDF de compra, le asignan una cuenta de gasto, un centro de coste o los dos.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override string NombreRegistro => "texto clave";
    public override IReadOnlyList<string> Sinonimos => ["texto clave", "palabra clave", "cuenta de gasto", "centro de coste", "departamento", "compras"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Texto", "Texto clave") { Obligatorio = true, Longitud = ReglasTextosClave.LongitudMaxima, Ancho = "l",
            Ayuda = "Se busca sin distinguir mayúsculas ni acentos. Si coinciden varios textos, gana el más largo; si empatan, no se asigna nada y la factura avisa." },
        new("Cuenta", "Cuenta de gasto", TipoCampo.Cuenta) { Longitud = 30, Ancho = "s", Sinonimos = ["cuenta", "gasto", "6"],
            Ayuda = "Subcuenta del ejercicio de trabajo. Se guarda su identidad contable, así vale en los demás ejercicios aunque cambie el código. Vacía, el texto no asigna cuenta." },
        new("NombreCuenta", "Nombre de la cuenta") { SoloLectura = true, Ancho = "l" },
        new("IdCentroCoste", "Centro de coste", TipoCampo.Lista) { Opciones = _centros, Ancho = "l", Ayuda = "Vacío, el texto no asigna centro de coste." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Texto", "Texto clave"), new("Cuenta", "Cuenta", 90), new("NombreCuenta", "Nombre de la cuenta"), new("Centro", "Centro de coste")];

    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nuevo texto clave" : r.Texto("Texto") ?? "Texto clave sin texto";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        _centros = [new OpcionCampo(null, "Sin centro de coste"), .. (await catalogo.CentrosAsync(c.Trabajo.IdSociedad)).Select(x => new OpcionCampo(x.Id, x.Texto))];
        return (await catalogo.ListarAsync(c.Trabajo)).Select(AsRegistro).ToList();
    }

    private static RegistroMantenimiento AsRegistro(TextoClaveCompra x) => new(x.IdTextoClave, new Dictionary<string, object?>
    {
        ["Texto"] = x.Texto, ["Cuenta"] = x.Cuenta,
        ["NombreCuenta"] = x.Cuenta is null ? null : x.CuentaEnEjercicio ? x.NombreCuenta : $"{x.NombreCuenta} (no existe en este ejercicio)",
        ["IdCentroCoste"] = x.IdCentroCoste, ["Centro"] = x.NombreCentro,
    });

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        int? id = r.Clave is null ? null : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture);
        var g = await catalogo.GuardarAsync(c.Trabajo, id, r.Texto("Texto") ?? "", r.Texto("Cuenta"), r.Entero("IdCentroCoste"));
        if (!g.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(g.Errores);
        var guardado = (await catalogo.ListarAsync(c.Trabajo)).First(x => x.IdTextoClave == g.Valor);
        return Resultado<RegistroMantenimiento>.Correcto(AsRegistro(guardado));
    }

    public override Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c) =>
        catalogo.EliminarAsync(c.Trabajo, Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture));
}

/// <summary>
/// Parámetros del reconocimiento de facturas de compra de la empresa de trabajo (ConfiguracionReconocimientoFacturasCompra,
/// compras/reconocimiento 2026-11-07): activo, VIES, OCR y las claves de AWS del OCR. El secreto se guarda cifrado
/// (<see cref="SecretoProtegido"/>) y nunca vuelve a la pantalla: solo se sustituye o se quita.
/// </summary>
public sealed class ProveedorReconocimientoCompra(IBaseDatos bd, Func<CredencialesOcr?, CancellationToken, Task<(bool Correcta, string Mensaje)>>? probar = null) : ProveedorBase
{
    private const string Tabla = "ConfiguracionReconocimientoFacturasCompra";

    public override string Clave => "reconocimientocompra";
    public override string Titulo => "Reconocimiento de facturas de compra";
    public override string Categoria => "Parámetros";
    public override string Finalidad => "Cómo se leen los PDF de compra de la empresa: si se reconocen, si se usa el OCR para los escaneados y con qué claves de AWS.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override Cardinalidad Cardinalidad => Cardinalidad.Unico;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "configuración del reconocimiento";
    public override IReadOnlyList<string> Sinonimos => ["ocr", "textract", "aws", "amazon", "escaneado", "credenciales", "vies", "reconocimiento"];
    public override IReadOnlyList<GrupoCampos> Grupos => [new("rec", "Reconocimiento"), new("ocr", "OCR (Amazon Textract)")];
    public override IReadOnlyList<AccionMantenimiento> Acciones =>
        [
            new("probarclaves", "Probar el OCR", "Manda a Amazon Textract una página de prueba con las claves guardadas (o, sin ellas, con el perfil de AWS de este equipo) y dice si la lee. Cuesta lo que una página de OCR. Guarda antes los cambios.", Modifica: false),
            new("quitarclaves", "Quitar las claves de AWS", "Borra la clave de acceso y el secreto guardados: el OCR usará el perfil de AWS de cada equipo, si lo tiene."),
        ];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Activo", "Reconocer los PDF de compra", TipoCampo.Logico) { Grupo = "rec" },
        new("SociedadTieneVies", "La empresa está en el VIES", TipoCampo.Logico) { Grupo = "rec", Sinonimos = ["vies", "intracomunitario"] },
        new("UmbralAceptacion", "Confianza mínima para aceptar", TipoCampo.Decimal) { Grupo = "rec", Obligatorio = true, Minimo = 0m, Maximo = 1m, Ancho = "s",
            Ayuda = "Entre 0 y 1. Por debajo, la factura queda para revisar." },
        new("UmbralDiscrepanciaImporte", "Descuadre de importes admitido", TipoCampo.Importe) { Grupo = "rec", Obligatorio = true, Minimo = 0m, Ancho = "s",
            Ayuda = "Diferencia máxima entre base + IVA − retención y el total para darla por cuadrada." },
        new("UsarTextract", "Usar OCR en los PDF escaneados", TipoCampo.Logico) { Grupo = "ocr", Sinonimos = ["ocr", "textract"],
            Ayuda = "Los PDF sin texto se leen con Amazon Textract (tiene coste por página). Sin marcar, esos PDF se rellenan a mano." },
        new("RegionAWS", "Región de AWS", TipoCampo.Codigo) { Grupo = "ocr", Longitud = 30, Ancho = "s", Sinonimos = ["region"],
            Ayuda = "Por ejemplo eu-west-1. Obligatoria si se guarda una clave de acceso." },
        new("ClaveAccesoAWS", "Clave de acceso (Access key ID)", TipoCampo.Codigo) { Grupo = "ocr", Longitud = 128, Ancho = "l", Sinonimos = ["access key", "credenciales"],
            Ayuda = "De un usuario de AWS que solo pueda usar Textract (DetectDocumentText). Vacía, cada equipo usa su perfil de AWS «proconta-textract»." },
        new("SecretoAWS", "Clave secreta (Secret access key)", TipoCampo.Secreto) { Grupo = "ocr", Longitud = 128, Ancho = "l", Sinonimos = ["secret", "secreto"],
            Ayuda = "Se guarda cifrada y no se vuelve a mostrar. Déjala vacía para conservar la guardada." },
        new("EstadoSecreto", "Clave secreta guardada") { Grupo = "ocr", SoloLectura = true, Ancho = "s" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("ClaveAccesoAWS", "Clave de acceso")];

    public override string TituloRegistro(RegistroMantenimiento r) => "Reconocimiento de facturas de compra";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) =>
        [await LeerAsync(c.Trabajo.IdSociedad) ?? await NuevoAsync(c)];

    public override Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto) =>
        Task.FromResult(new RegistroMantenimiento(null, new Dictionary<string, object?>
        {
            ["Activo"] = true, ["SociedadTieneVies"] = false, ["UsarTextract"] = false, ["UmbralAceptacion"] = 0.9m, ["UmbralDiscrepanciaImporte"] = 0.02m,
            ["RegionAWS"] = null, ["ClaveAccesoAWS"] = null, ["SecretoAWS"] = null, ["EstadoSecreto"] = "No",
        }));

    private Task<RegistroMantenimiento?> LeerAsync(string idSociedad) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            $"SELECT IdConfiguracion, Activo, SociedadTieneVies, UsarTextract, UmbralAceptacion, UmbralDiscrepanciaImporte, RegionAWS, ClaveAccesoAWS, SecretoAWSProtegido FROM {Tabla} WHERE IdSociedad = ?",
            r => new RegistroMantenimiento(r.Entero("IdConfiguracion"), new Dictionary<string, object?>
            {
                ["Activo"] = r.Logico("Activo"), ["SociedadTieneVies"] = r.Logico("SociedadTieneVies"), ["UsarTextract"] = r.Logico("UsarTextract"),
                ["UmbralAceptacion"] = r.Decimal("UmbralAceptacion"), ["UmbralDiscrepanciaImporte"] = r.Decimal("UmbralDiscrepanciaImporte"),
                ["RegionAWS"] = r.Texto("RegionAWS"), ["ClaveAccesoAWS"] = r.Texto("ClaveAccesoAWS"), ["SecretoAWS"] = null,
                ["EstadoSecreto"] = string.IsNullOrWhiteSpace(r.Texto("SecretoAWSProtegido")) ? "No"
                    : SecretoProtegido.Desproteger(r.Texto("SecretoAWSProtegido")) is null ? "No se puede leer: escríbela de nuevo" : "Sí",
            }), idSociedad));

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var errores = ValidacionGenerica.Validar(Campos, r).ToList();
        var clave = r.Texto("ClaveAccesoAWS");
        var secreto = r.Texto("SecretoAWS");
        var region = r.Texto("RegionAWS");
        if (clave is not null && region is null) errores.Add(new("RegionAWS", "Falta la región de AWS (por ejemplo eu-west-1)."));
        if (region is not null && !System.Text.RegularExpressions.Regex.IsMatch(region, "^[a-z]{2}(-[a-z]+)+-[0-9]$"))
            errores.Add(new("RegionAWS", "Región de AWS: escríbela como eu-west-1."));
        if (clave is not null && secreto is null && r.Texto("EstadoSecreto") != "Sí")
            errores.Add(new("SecretoAWS", "Falta la clave secreta de AWS."));
        if (clave is null && secreto is not null) errores.Add(new("ClaveAccesoAWS", "Falta la clave de acceso que va con la clave secreta."));
        return errores;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var idSociedad = c.Trabajo.IdSociedad;
        var clave = r.Texto("ClaveAccesoAWS");
        var secreto = r.Texto("SecretoAWS");
        await bd.EjecutarAsync(async s =>
        {
            var existe = await s.ConsultarPrimeroAsync($"SELECT IdConfiguracion FROM {Tabla} WHERE IdSociedad = ?", f => (int?)f.Entero("IdConfiguracion"), idSociedad);
            if (existe is null)
                await s.EjecutarAsync($"INSERT INTO {Tabla} (IdSociedad, Activo, SociedadTieneVies, UsarTextract, UmbralAceptacion, UmbralDiscrepanciaImporte, Version) VALUES (?, ?, ?, ?, ?, ?, 1)",
                    idSociedad, r.Logico("Activo"), r.Logico("SociedadTieneVies"), r.Logico("UsarTextract"), r.Decimal("UmbralAceptacion") ?? 0.9m, r.Decimal("UmbralDiscrepanciaImporte") ?? 0.02m);
            await s.EjecutarAsync($"UPDATE {Tabla} SET Activo = ?, SociedadTieneVies = ?, UsarTextract = ?, UmbralAceptacion = ?, UmbralDiscrepanciaImporte = ?, " +
                "RegionAWS = ?, ClaveAccesoAWS = ?, Version = Version + 1 WHERE IdSociedad = ?",
                r.Logico("Activo"), r.Logico("SociedadTieneVies"), r.Logico("UsarTextract"), r.Decimal("UmbralAceptacion") ?? 0.9m, r.Decimal("UmbralDiscrepanciaImporte") ?? 0.02m,
                r.Texto("RegionAWS"), clave, idSociedad);
            // Sin clave de acceso no se guarda ningún secreto; con un secreto nuevo, sustituye al anterior.
            if (clave is null) await s.EjecutarAsync($"UPDATE {Tabla} SET SecretoAWSProtegido = Null WHERE IdSociedad = ?", idSociedad);
            else if (secreto is not null) await s.EjecutarAsync($"UPDATE {Tabla} SET SecretoAWSProtegido = ? WHERE IdSociedad = ?", SecretoProtegido.Proteger(secreto), idSociedad);
            return true;
        });
        return Resultado<RegistroMantenimiento>.Correcto((await LeerAsync(idSociedad))!);
    }

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento c)
    {
        if (accion == "probarclaves")
        {
            var config = await bd.EjecutarAsync(s => RepositorioFacturasCompra.ConfiguracionAsync(s, c.Trabajo.IdSociedad));
            if (config.MotivoCredencialesOcr is { } motivo) return Resultado<ResultadoAccion>.Fallo(new Error("Reconocimiento.ClavesAws", $"Las claves guardadas no valen: {motivo}.", Titulo));
            var (correcta, mensaje) = await (probar ?? ExtractorOcrTextract.ProbarAsync)(config.CredencialesOcr, CancellationToken.None);
            return correcta ? Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(mensaje))
                : Resultado<ResultadoAccion>.Fallo(new Error("Reconocimiento.OcrNoFunciona", mensaje, Titulo, null, "Revisa la región y las claves, y que el usuario de AWS pueda usar Textract (DetectDocumentText)."));
        }
        if (accion != "quitarclaves") return await base.EjecutarAsync(accion, registro, c);
        await bd.EjecutarAsync(s => s.EjecutarAsync($"UPDATE {Tabla} SET ClaveAccesoAWS = Null, SecretoAWSProtegido = Null, Version = Version + 1 WHERE IdSociedad = ?", c.Trabajo.IdSociedad));
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion("Claves de AWS quitadas: el OCR usará el perfil de AWS de cada equipo."));
    }
}
