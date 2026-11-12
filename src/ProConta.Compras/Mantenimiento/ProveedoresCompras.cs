using ProConta.Nucleo.Seguridad;
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
        [new ProveedorTextosClaveCompra(new CatalogoTextosClaveCompra(bd)), new ProveedorReconocimientoCompra(bd), new ProveedorCertificadoEmpresa(bd),
         new ProveedorActividades(bd), new ProveedorActividadCentros(bd)];
}

/// <summary>
/// Actividades a efectos de IVA de la empresa (Actividades, contabilidad/procesos 2026-11-08; en el ERP, CategoriasIVA): si
/// su IVA soportado es deducible y la serie de cuentas 472 a la que va. La tabla es de contabilidad; el mantenimiento se
/// publica aquí porque compras es quien la usa al exportar.
/// </summary>
public sealed class ProveedorActividades : ProveedorTablaAccess
{
    public ProveedorActividades(IBaseDatos bd) : base(bd, "actividades", "Actividades (IVA)", "Catálogos",
        "Actividades de la empresa a efectos de IVA: si su IVA soportado es deducible y a qué serie de la 472 va. Cada centro de coste puede tener la suya.",
        AmbitoMantenimiento.Sociedad, "actividad", "Actividades", "IdActividad", ClaveNueva.Autonumerica,
        [
            new("Codigo", "Código", TipoCampo.Codigo) { Ayuda = "Código corto de la actividad (por ejemplo ALQTUR), el que se ve en las listas y en los centros de coste.", Obligatorio = true, Longitud = 10, Ancho = "s" },
            new("Nombre", "Nombre") { Obligatorio = true, Longitud = 100, Ancho = "l", Ayuda = "Por ejemplo «Alquileres turísticos»." },
            new("Deducible", "IVA soportado deducible", TipoCampo.Lista) { Obligatorio = true, Ancho = "m", Sinonimos = ["deducible", "prorrata", "exento"],
                Opciones = [new OpcionCampo((byte)2, "100 % deducible"), new OpcionCampo((byte)1, "Por prorrata"), new OpcionCampo((byte)0, "No deducible")],
                Ayuda = "No deducible: el IVA de sus compras va al gasto. Por prorrata: por ahora se deduce entero en la 472 de la actividad; la regularización (4729) se hará al cierre." },
            new("SerieCuentaIva", "Serie de la 472", TipoCampo.Codigo) { Longitud = 4, Ancho = "xs", Sinonimos = ["472", "4725", "4726", "4727", "4728"],
                Ayuda = "Cuatro cifras, por ejemplo 4728. El IVA va a la serie con la terminación de la cuenta general (4720021 → 4728021; intracomunitario x121 y x221, inversión del sujeto pasivo x321, importación x421). Su IVA repercutido va a la 477 con el 2 cambiado por el 7 (4728 → 4778). Vacía: las cuentas generales del ejercicio." },
            new("Activa", "Activa", TipoCampo.Logico) { Ayuda = "Una actividad inactiva no se aplica: sus centros usan las cuentas generales." },
            new(CampoSubcuentas, "Subcuentas de IVA en este ejercicio", TipoCampo.TextoLargo) { SoloLectura = true, Ancho = "f",
                Ayuda = "Las subcuentas del ejercicio de trabajo de la serie (472…) y de su 477 (el 2 cambiado por el 7): las que ya había y las que se crean al contabilizar." },
        ],
        [new("Codigo", "Código", 70), new("Nombre", "Nombre"), new("SerieCuentaIva", "Serie", 60), new("Activa", "Activa", 60)])
    {
        _bd = bd;
        ColumnaAmbito = "IdSociedad";
        Orden = "Codigo";
        Usos = [new UsoRegistro("CentrosCoste", "IdActividad", "centros de coste")];
        Alias = ["actividad", "categoria de iva", "prorrata", "deducible", "472"];
        Titular = r => r.EsNuevo ? "Nueva actividad" : $"{r.Texto("Codigo")} · {r.Texto("Nombre")}";
        Reglas = r => r.Texto("SerieCuentaIva") is { } serie && (serie.Length != 4 || !serie.StartsWith("472", StringComparison.Ordinal) || !serie.All(char.IsAsciiDigit))
            ? [new ErrorCampo("SerieCuentaIva", "La serie son cuatro cifras que empiezan por 472, por ejemplo 4728.")] : [];
        AntesDeGuardar = async (s, r, c) =>
        {
            var repetidas = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Actividades WHERE IdSociedad = ? AND Codigo = ? AND IdActividad <> ?",
                c.Trabajo.IdSociedad, r.Texto("Codigo") ?? "", r.Clave is null ? -1 : Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
            return repetidas > 0 ? new Error("Actividades.Repetida", $"Ya hay una actividad con el código {r.Texto("Codigo")}.", null, null, "Usa otro código.") : null;
        };
    }

    private const string CampoSubcuentas = "Subcuentas";

    private readonly IBaseDatos _bd;

    /// <summary>Lista las actividades con las subcuentas de su serie en el ejercicio de trabajo (propietario 2026-11-08 09:34).</summary>
    public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) => _bd.EjecutarAsync(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT IdActividad, Codigo, Nombre, Deducible, SerieCuentaIva, Activa FROM Actividades WHERE IdSociedad = ? ORDER BY Codigo",
            f => new RegistroMantenimiento(f.Entero("IdActividad"), new Dictionary<string, object?>
            {
                ["Codigo"] = f.Texto("Codigo")?.Trim(), ["Nombre"] = f.Texto("Nombre")?.Trim(),
                ["Deducible"] = f.EsNulo("Deducible") ? null : Convert.ToByte(f.Valor("Deducible"), CultureInfo.InvariantCulture),
                ["SerieCuentaIva"] = f.Texto("SerieCuentaIva")?.Trim(), ["Activa"] = f.Logico("Activa"),
            }), c.Trabajo.IdSociedad);
        foreach (var r in filas)
        {
            if (r.Texto("SerieCuentaIva") is not { Length: 4 } serie)
            {
                r[CampoSubcuentas] = "Sin serie: usa las cuentas de IVA generales del ejercicio.";
                continue;
            }
            // La 477 de la actividad es su serie con el 2 cambiado por el 7 (4728 → 4778).
            var repercutido = "477" + serie[3];
            var cuentas = await s.ConsultarAsync(
                "SELECT Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ? AND (Codigo LIKE ? OR Codigo LIKE ?) AND Codigo <> ? AND Codigo <> ? ORDER BY Codigo",
                f => $"{f.Texto("Codigo")?.Trim()}  {f.Texto("Nombre")?.Trim()}", c.Trabajo.IdSociedadEjercicio, serie + "%", repercutido + "%", serie, repercutido);
            r[CampoSubcuentas] = cuentas.Count == 0 ? $"Aún no hay subcuentas {serie} ni {repercutido} en este ejercicio: se crean al contabilizar." : string.Join(Environment.NewLine, cuentas);
        }
        return (IReadOnlyList<RegistroMantenimiento>)filas;
    });

    public override async Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto)
    {
        var r = await base.NuevoAsync(contexto);
        r["Deducible"] = (byte)2;
        r["Activa"] = true;
        return r;
    }
}

/// <summary>
/// Actividad de cada centro de coste de la empresa (CentrosCoste.IdActividad, contabilidad/procesos 2026-11-08): la heredan
/// los centros que cuelgan de él y no tienen otra. No da de alta ni borra centros: solo asigna la actividad.
/// </summary>
public sealed class ProveedorActividadCentros(IBaseDatos bd) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _actividades = [];

    public override string Clave => "actividadcentros";
    public override string Titulo => "Actividad de los centros de coste";
    public override string Categoria => "Catálogos";
    public override string Finalidad => "Qué actividad (a efectos de IVA) tiene cada centro de coste. Los centros sin actividad propia usan la del centro del que cuelgan.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "centro de coste";
    public override IReadOnlyList<string> Sinonimos => ["centro de coste", "departamento", "actividad", "472", "prorrata"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Ruta", "Centro de coste", TipoCampo.Codigo) { Ayuda = "Código completo del centro de coste, con los de los que cuelga. Los centros se crean en Contabilidad.", SoloLectura = true, Ancho = "m" },
        new("Nombre", "Nombre") { Ayuda = "Nombre del centro de coste. Se cambia en Contabilidad.", SoloLectura = true, Ancho = "l" },
        new("IdActividad", "Actividad", TipoCampo.Lista) { Opciones = _actividades, Ancho = "l",
            Ayuda = "Vacía: la del centro del que cuelga (o, si ninguno la tiene, las cuentas de IVA generales del ejercicio)." },
        new("Aplica", "Actividad que se aplica") { Ayuda = "La actividad que se usa de verdad: la propia o, si no tiene, la heredada del centro del que cuelga. Se calcula sola.", SoloLectura = true, Ancho = "l" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Ruta", "Centro", 110), new("Nombre", "Nombre"), new("Aplica", "Actividad que se aplica")];

    public override string TituloRegistro(RegistroMantenimiento r) => $"{r.Texto("Ruta")} · {r.Texto("Nombre")}";

    private sealed record Fila(int Id, int? Padre, string Ruta, string Nombre, int? IdActividad);

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        var (actividades, centros) = await bd.EjecutarAsync(async s =>
        {
            var a = await s.ConsultarAsync("SELECT IdActividad, Codigo, Nombre, Activa FROM Actividades WHERE IdSociedad = ? ORDER BY Codigo",
                f => (Id: f.Entero("IdActividad"), Texto: $"{f.Texto("Codigo")?.Trim()} · {f.Texto("Nombre")?.Trim()}{(f.Logico("Activa") ? "" : " (inactiva)")}"),
                c.Trabajo.IdSociedad);
            var cc = await s.ConsultarAsync("SELECT IdCentroCoste, IdCentroCostePadre, RutaJerarquica, Nombre, IdActividad FROM CentrosCoste WHERE IdSociedad = ? ORDER BY RutaOrden",
                f => new Fila(f.Entero("IdCentroCoste"), f.EnteroNulo("IdCentroCostePadre"), f.Texto("RutaJerarquica") ?? "", f.Texto("Nombre") ?? "", f.EnteroNulo("IdActividad")),
                c.Trabajo.IdSociedad);
            return (a, cc);
        });
        _actividades = [new OpcionCampo(null, "La del centro del que cuelga"), .. actividades.Select(x => new OpcionCampo(x.Id, x.Texto))];
        var textos = actividades.ToDictionary(x => x.Id, x => x.Texto);
        var porId = centros.ToDictionary(x => x.Id);
        string Aplica(Fila f)
        {
            for (var (actual, nivel) = (f, 0); nivel < 20; nivel++)
            {
                if (actual.IdActividad is int id && textos.TryGetValue(id, out var t))
                    return ReferenceEquals(actual, f) ? t : $"{t} (de {actual.Ruta})";
                if (actual.Padre is not int p || !porId.TryGetValue(p, out var padre)) break;
                actual = padre;
            }
            return "Sin actividad: cuentas generales";
        }
        return [.. centros.Select(f => new RegistroMantenimiento(f.Id, new Dictionary<string, object?>
        {
            ["Ruta"] = f.Ruta, ["Nombre"] = f.Nombre, ["IdActividad"] = f.IdActividad, ["Aplica"] = Aplica(f),
        }))];
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture);
        var actividad = r.Entero("IdActividad");
        var filas = await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE CentrosCoste SET IdActividad = ? WHERE IdCentroCoste = ? AND IdSociedad = ?",
            actividad, id, c.Trabajo.IdSociedad));
        if (filas == 0) return Resultado<RegistroMantenimiento>.Fallo(new Error("CentrosCoste.NoExiste", "El centro de coste ya no existe en esta empresa.", null, null, "Actualiza la lista."));
        var guardado = (await ListarAsync(c)).First(x => Convert.ToInt32(x.Clave, CultureInfo.InvariantCulture) == id);
        return Resultado<RegistroMantenimiento>.Correcto(guardado);
    }
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
        new("Cuenta", "Cuenta de gasto", TipoCampo.Cuenta) { Longitud = 30, Ancho = "s", Sinonimos = ["cuenta", "gasto", "6"], GruposCuenta = Facturas.FacturaCompra.GruposCuentaGasto,
            Ayuda = "Subcuenta del ejercicio de trabajo. Se guarda su identidad contable, así vale en los demás ejercicios aunque cambie el código. Vacía, el texto no asigna cuenta." },
        new("NombreCuenta", "Nombre de la cuenta") { Ayuda = "Nombre de la cuenta de gasto en el ejercicio de trabajo, para comprobar que es la buena. Se rellena solo.", SoloLectura = true, Ancho = "l" },
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
        new("Activo", "Reconocer los PDF de compra", TipoCampo.Logico) { Ayuda = "Marcado, los PDF de compra se leen solos y se proponen como facturas recibidas; sin marcar, se rellenan a mano.", Grupo = "rec" },
        new("SociedadTieneVies", "La empresa está en el VIES", TipoCampo.Logico) { Ayuda = "Marcado, la empresa está dada de alta en el censo VIES y sus compras a otros países de la UE se tratan como intracomunitarias.", Grupo = "rec", Sinonimos = ["vies", "intracomunitario"] },
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
        new("EstadoSecreto", "Clave secreta guardada") { Ayuda = "Dice si hay una clave secreta de AWS guardada, sin mostrarla. Se cambia en «Clave secreta».", Grupo = "ocr", SoloLectura = true, Ancho = "s" },
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

/// <summary>Un certificado del almacén de Windows que puede usar la empresa (con clave privada).</summary>
public sealed record CertificadoEquipo(string Huella, string Titular, string? Nif, DateTime Caduca)
{
    public string Texto => $"{Titular}{(Nif is null ? "" : $" · {Nif}")} · caduca el {Caduca:dd/MM/yyyy}";
}

/// <summary>
/// Certificado de la empresa de trabajo (VeriFactuConfiguracion.CertificateThumbprint, compras/pantallas 2026-11-07): se
/// elige en un desplegable entre los instalados en Windows con clave privada y sin caducar, y se guarda su huella. Lo usan
/// la consulta del NIF en la AEAT (alta de proveedores) y, más adelante, VeriFactu. Cada equipo que lo use debe tenerlo
/// instalado; con el mismo certificado, la huella es la misma.
/// </summary>
public sealed class ProveedorCertificadoEmpresa(IBaseDatos bd, Func<IReadOnlyList<CertificadoEquipo>>? certificados = null,
    Func<string, string, CancellationToken, Task<Proveedores.ResultadoNifAeat>>? consultar = null) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _opciones = [];

    public override string Clave => "certificadoempresa";
    public override string Titulo => "Certificado de la empresa";
    public override string Categoria => "Parámetros";
    public override string Finalidad => "Certificado digital con el que la empresa consulta la AEAT (razón social de los NIF de proveedores) y, más adelante, envía a VeriFactu.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override Cardinalidad Cardinalidad => Cardinalidad.Unico;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "certificado";
    public override IReadOnlyList<string> Sinonimos => ["certificado", "aeat", "firma", "verifactu", "vnif", "huella"];
    public override IReadOnlyList<AccionMantenimiento> Acciones =>
        [new("probaraeat", "Probar con la AEAT", "Consulta en la AEAT el NIF de la propia empresa con el certificado guardado y dice si responde. Guarda antes los cambios.", Modifica: false)];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Huella", "Certificado", TipoCampo.Lista) { Opciones = _opciones, Ancho = "f",
            Ayuda = "Certificados instalados en Windows (del equipo o de tu usuario) con clave privada y sin caducar. Si no sale, instálalo en este equipo (doble clic en el .pfx) y vuelve a abrir esta pantalla." },
        new("HuellaGuardada", "Huella guardada", TipoCampo.Codigo) { Ayuda = "Huella del certificado que se usa ahora, para comprobar cuál es. Cambia al elegir otro en «Certificado».", SoloLectura = true, Ancho = "l" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("HuellaGuardada", "Huella")];

    public override string TituloRegistro(RegistroMantenimiento r) => "Certificado de la empresa";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        var instalados = (certificados ?? CertificadosEquipo)();
        var guardada = await HuellaAsync(c.Trabajo.IdSociedad);
        var opciones = new List<OpcionCampo> { new(null, "Sin certificado") };
        opciones.AddRange(instalados.Select(x => new OpcionCampo(x.Huella, x.Texto)));
        if (guardada.Existe && guardada.Huella is { } h && instalados.All(x => !string.Equals(x.Huella, h, StringComparison.OrdinalIgnoreCase)))
            opciones.Add(new OpcionCampo(h, $"No instalado en este equipo (huella {h})"));
        _opciones = opciones;
        var valor = guardada.Huella is { } g ? opciones.FirstOrDefault(o => string.Equals(o.Valor as string, g, StringComparison.OrdinalIgnoreCase))?.Valor : null;
        return [new RegistroMantenimiento(guardada.Existe ? c.Trabajo.IdSociedad : null, new Dictionary<string, object?> { ["Huella"] = valor, ["HuellaGuardada"] = guardada.Huella })];
    }

    private Task<(bool Existe, string? Huella)> HuellaAsync(string idSociedad) =>
        bd.EjecutarAsync(async s =>
        {
            var filas = await s.ConsultarAsync("SELECT CertificateThumbprint FROM VeriFactuConfiguracion WHERE IdSociedad = ?", f => f.Texto("CertificateThumbprint")?.Replace(" ", "").Trim(), idSociedad);
            return filas.Count == 0 ? (false, (string?)null) : (true, string.IsNullOrWhiteSpace(filas[0]) ? null : filas[0]!.ToUpperInvariant());
        });

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = c.Trabajo.IdSociedad;
        var huella = r.Texto("Huella");
        try
        {
            await bd.EjecutarAsync(async s =>
            {
                var existe = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM VeriFactuConfiguracion WHERE IdSociedad = ?", id), CultureInfo.InvariantCulture) > 0;
                // El número de serie se quita: manda la huella elegida aquí.
                if (existe) await s.EjecutarAsync("UPDATE VeriFactuConfiguracion SET CertificateThumbprint = ?, CertificateSerial = Null WHERE IdSociedad = ?", huella, id);
                else
                {
                    // La clave de la maestra no siempre es autonumérica: la siguiente libre vale en los dos casos.
                    var siguiente = Convert.ToInt32(await s.EscalarAsync("SELECT IIf(MAX(IdVeriFactuConfiguracion) Is Null, 0, MAX(IdVeriFactuConfiguracion)) + 1 FROM VeriFactuConfiguracion"), CultureInfo.InvariantCulture);
                    await s.EjecutarAsync("INSERT INTO VeriFactuConfiguracion (IdVeriFactuConfiguracion, IdSociedad, CertificateThumbprint) VALUES (?, ?, ?)", siguiente, id, huella);
                }
                return true;
            });
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<RegistroMantenimiento>.Fallo(new Error("Certificado.Guardar", $"No he podido guardar el certificado: {ex.Message}", Titulo));
        }
        return Resultado<RegistroMantenimiento>.Correcto((await ListarAsync(c))[0]);
    }

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento registro, ContextoMantenimiento c)
    {
        if (accion != "probaraeat") return await base.EjecutarAsync(accion, registro, c);
        var nif = await bd.EjecutarAsync(s => s.EscalarAsync("SELECT CIF FROM Sociedades WHERE IdSociedad = ?", c.Trabajo.IdSociedad)) as string;
        if (string.IsNullOrWhiteSpace(nif))
            return Resultado<ResultadoAccion>.Fallo(new Error("Certificado.SinNif", "La empresa no tiene NIF: no hay nada que consultar.", Titulo));
        Proveedores.ResultadoNifAeat r;
        try
        {
            r = consultar is not null ? await consultar(c.Trabajo.IdSociedad, nif.Trim(), CancellationToken.None)
                : await new Integraciones.ConsultaNifAeatVNif(bd).ConsultarAsync(c.Trabajo.IdSociedad, nif.Trim(), null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Security.Cryptography.CryptographicException)
        {
            r = Proveedores.ResultadoNifAeat.SinConsultar($"No he podido conectar con la AEAT: {ex.Message}");
        }
        if (r.Identificado) return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion($"La AEAT responde con este certificado: el NIF {nif.Trim()} es de {r.RazonSocial}."));
        return r.Consultado
            ? Resultado<ResultadoAccion>.Correcto(new ResultadoAccion($"La AEAT responde con este certificado, pero no identifica el NIF {nif.Trim()}: {r.Motivo}"))
            : Resultado<ResultadoAccion>.Fallo(new Error("Certificado.SinAeat", r.Motivo ?? "No he podido consultar la AEAT.", Titulo, null,
                "Comprueba que el certificado elegido está instalado en este equipo con su clave privada y que no ha caducado."));
    }

    /// <summary>Certificados con clave privada y sin caducar del almacén personal del equipo y del usuario.</summary>
    public static IReadOnlyList<CertificadoEquipo> CertificadosEquipo()
    {
        var lista = new Dictionary<string, CertificadoEquipo>(StringComparer.OrdinalIgnoreCase);
        foreach (var ubicacion in new[] { System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser, System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine })
        {
            using var almacen = new System.Security.Cryptography.X509Certificates.X509Store(System.Security.Cryptography.X509Certificates.StoreName.My, ubicacion);
            try { almacen.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly | System.Security.Cryptography.X509Certificates.OpenFlags.OpenExistingOnly); }
            catch (System.Security.Cryptography.CryptographicException) { continue; }
            foreach (var x in almacen.Certificates)
            {
                using (x)
                {
                    if (!x.HasPrivateKey || x.NotAfter < DateTime.Now || lista.ContainsKey(x.Thumbprint)) continue;
                    lista[x.Thumbprint] = new CertificadoEquipo(x.Thumbprint.ToUpperInvariant(), Titular(x.Subject), NifDe(x.Subject), x.NotAfter);
                }
            }
        }
        return lista.Values.OrderBy(c => c.Titular, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>El nombre del titular (CN, o la organización) del sujeto del certificado.</summary>
    internal static string Titular(string sujeto)
    {
        string? Valor(string clave) => System.Text.RegularExpressions.Regex.Match(sujeto, $@"(?:^|,\s*){clave}=(""[^""]*""|[^,]*)") is { Success: true } m ? m.Groups[1].Value.Trim('"', ' ') : null;
        return Valor("CN") ?? Valor("O") ?? sujeto;
    }

    /// <summary>NIF del sujeto: el identificador de organización (VATES-…) o el número de serie (IDCES-…).</summary>
    internal static string? NifDe(string sujeto)
    {
        var m = System.Text.RegularExpressions.Regex.Match(sujeto, @"(?:OID\.2\.5\.4\.97|organizationIdentifier)=VATES-([A-Z0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();
        m = System.Text.RegularExpressions.Regex.Match(sujeto, @"SERIALNUMBER=(?:IDCES-)?([A-Z0-9]{9})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value.ToUpperInvariant() : null;
    }
}
