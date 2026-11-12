using System.Globalization;
using ProConta.Activos.Subcuentas;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Mantenimiento;

/// <summary>
/// Mantenimientos de activos para la pantalla genérica (activos 2026-11-12 11:45): tipos de activo, tipos de subcuenta y
/// plantillas de subcuentas, comunes a todas las sociedades, con «Generar subcuentas» y «Borrar subcuentas sin apuntes».
/// </summary>
public static class ProveedoresActivos
{
    public const string Categoria = "Activos";

    public static IReadOnlyList<IProveedorMantenimiento> Todos(IBaseDatos bd, ICatalogoCuentas cuentas)
    {
        var generador = new GeneradorSubcuentasActivo(bd, cuentas);
        return [new ProveedorTiposActivo(bd, generador), new ProveedorTiposSubcuentaActivo(bd), new ProveedorPlantillasSubcuentasActivo(bd, generador)];
    }

    internal const string AccionGenerar = "generar-subcuentas";
    internal const string AccionBorrarSinApuntes = "borrar-sin-apuntes";

    internal static AccionMantenimiento Generar(string ayuda) => new(AccionGenerar, "Generar subcuentas", ayuda, Modifica: true, ConPropuesta: true);

    internal static int Id(object? clave) => Convert.ToInt32(clave, CultureInfo.InvariantCulture);

    internal static Resultado<T> Fallo<T>(string codigo, string mensaje, string? campo = null, string? accion = null) =>
        Resultado<T>.Fallo(new Error(codigo, mensaje, campo, null, accion));

    /// <summary>Propuesta de «Generar subcuentas» para los activos de un tipo, como texto para el operador.</summary>
    internal static async Task<Resultado<ResultadoAccion>> ProponerGenerarAsync(GeneradorSubcuentasActivo generador, ContextoMantenimiento c, int idTipoActivo, string tipo)
    {
        var ops = await generador.ProponerAsync(c.Trabajo, [idTipoActivo]);
        if (ops.Count == 0)
            return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(
                $"Las subcuentas de los activos «{tipo}» de {c.RazonSocial} ya están al día en {c.Trabajo.Ejercicio}: no hay nada que generar.") { NadaQueHacer = true });
        int crear = ops.Count(o => o.Accion == AccionSubcuenta.Crear), alta = ops.Count(o => o.Accion == AccionSubcuenta.AltaEnEjercicio),
            renombrar = ops.Count(o => o.Accion == AccionSubcuenta.Renombrar), distintos = ops.Count(o => o.MuyDistinto);
        var partes = new List<string>();
        if (crear > 0) partes.Add($"crear {Plural(crear, "subcuenta", "subcuentas")}");
        if (alta > 0) partes.Add($"dar de alta en el ejercicio {Plural(alta, "subcuenta", "subcuentas")} que ya existía{(alta == 1 ? "" : "n")} en otro");
        if (renombrar > 0) partes.Add($"renombrar {Plural(renombrar, "subcuenta", "subcuentas")}");
        var mensaje = $"En {c.Trabajo.Ejercicio} de {c.RazonSocial}, para los activos «{tipo}» voy a {Unir(partes)}." +
            (distintos > 0 ? $" Revisa {(distintos == 1 ? "el renombre marcado" : $"los {distintos} renombres marcados")} con ⚠: el nombre nuevo se parece poco al actual." : "") +
            " ¿Confirmas?";
        var detalle = ops.Select(o => $"{o.Activo.Nombre}: {o.Texto}").ToList();
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(mensaje, null, detalle));
    }

    internal static async Task<Resultado<ResultadoAccion>> GenerarAsync(GeneradorSubcuentasActivo generador, ContextoMantenimiento c, int idTipoActivo, string tipo)
    {
        var r = await generador.GenerarAsync(c.Trabajo, [idTipoActivo]);
        var hechas = new List<string>();
        if (r.Creadas > 0) hechas.Add($"he creado {Plural(r.Creadas, "subcuenta", "subcuentas")}");
        if (r.DadasDeAlta > 0) hechas.Add($"he dado de alta en el ejercicio {Plural(r.DadasDeAlta, "subcuenta", "subcuentas")}");
        if (r.Renombradas > 0) hechas.Add($"he renombrado {Plural(r.Renombradas, "subcuenta", "subcuentas")}");
        var mensaje = (hechas.Count == 0 ? "No he cambiado ninguna subcuenta" : "Hecho: " + Unir(hechas)) + $" de los activos «{tipo}» en {c.Trabajo.Ejercicio}." +
            (r.Errores.Count == 0 ? "" : $" {Plural(r.Errores.Count, "subcuenta no se ha podido", "subcuentas no se han podido")} generar; te dejo el motivo abajo.");
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(mensaje, null, r.Errores.Count == 0 ? null : r.Errores));
    }

    internal static string Plural(int n, string uno, string varios) => n == 1 ? $"1 {uno}" : $"{n.ToString(CultureInfo.CurrentCulture)} {varios}";

    private static string Unir(IReadOnlyList<string> partes) =>
        partes.Count <= 1 ? string.Concat(partes) : string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[^1];
}

/// <summary>Tipos de activo (TiposActivo): los del PGC más los que añada el operador.</summary>
public sealed class ProveedorTiposActivo(IBaseDatos bd, GeneradorSubcuentasActivo generador) : ProveedorBase
{
    private static readonly IReadOnlyList<OpcionCampo> Categorias =
    [
        new("INMUEBLE", "Inmueble"), new("MOVIL", "Elemento de transporte"), new("MATERIAL", "Inmovilizado material"),
        new("INTANGIBLE", "Inmovilizado intangible"), new("INVERSION", "Inversión financiera"),
    ];

    public override string Clave => "activos.tipos";
    public override string Titulo => "Tipos de activo";
    public override string Categoria => ProveedoresActivos.Categoria;
    public override string Finalidad => "Clases de activo (vivienda, local, vehículo…). Cada tipo tiene su plantilla de subcuentas; desde aquí se generan las de sus activos.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Compartido;
    public override string NombreRegistro => "tipo de activo";
    public override IReadOnlyList<string> Sinonimos => ["activo", "inmueble", "vivienda", "local", "vehiculo", "amortizacion", "pgc"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Codigo", "Código", TipoCampo.Codigo) { Obligatorio = true, Longitud = 30, Ancho = "s", SoloAlta = true,
            Ayuda = "En mayúsculas y sin espacios, por ejemplo VIVIENDA. No se cambia después de crearlo." },
        new("Nombre", "Nombre") { Obligatorio = true, Longitud = 100, Ancho = "l" },
        new("Categoria", "Categoría", TipoCampo.Lista) { Obligatorio = true, Opciones = Categorias, Ancho = "m" },
        new("EsAmortizablePredeterminado", "Amortizable por defecto", TipoCampo.Logico) { Ayuda = "Los activos nuevos de este tipo nacen amortizables." },
        new("EstaActivo", "Activo", TipoCampo.Logico) { Ayuda = "Un tipo inactivo no se ofrece para activos nuevos; los que ya lo tienen lo conservan." },
        new("Lineas", "Líneas de plantilla", TipoCampo.Entero) { SoloLectura = true, Ancho = "xs" },
        new("Activos", "Activos de esta empresa", TipoCampo.Entero) { SoloLectura = true, Ancho = "xs" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas =>
        [new("Codigo", "Código", 110), new("Nombre", "Nombre"), new("Categoria", "Categoría", 160), new("EstaActivo", "Activo", 60), new("Lineas", "Líneas", 60, true), new("Activos", "Activos", 70, true)];

    public override IReadOnlyList<AccionMantenimiento> Acciones =>
        [ProveedoresActivos.Generar("Crea, da de alta en el ejercicio o renombra las subcuentas de los activos de este tipo según su plantilla. Antes te enseña la propuesta.")];

    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nuevo tipo de activo" : $"{r.Texto("Codigo")} · {r.Texto("Nombre")}";

    public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) => bd.EjecutarAsync(async s =>
    {
        var lineas = (await s.ConsultarAsync("SELECT IdTipoActivo, COUNT(*) AS N FROM PlantillasSubcuentasActivo GROUP BY IdTipoActivo", f => (Id: f.Entero("IdTipoActivo"), N: f.Entero("N"))))
            .ToDictionary(x => x.Id, x => x.N);
        var activos = (await s.ConsultarAsync("SELECT IdTipoActivo, COUNT(*) AS N FROM Activos WHERE IdSociedad = ? GROUP BY IdTipoActivo",
            f => (Id: f.Entero("IdTipoActivo"), N: f.Entero("N")), c.Trabajo.IdSociedad)).ToDictionary(x => x.Id, x => x.N);
        return (IReadOnlyList<RegistroMantenimiento>)await s.ConsultarAsync(
            "SELECT IdTipoActivo, Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo FROM TiposActivo ORDER BY EstaActivo, Nombre",
            f =>
            {
                var id = f.Entero("IdTipoActivo");
                return new RegistroMantenimiento(id, new Dictionary<string, object?>
                {
                    ["Codigo"] = f.Texto("Codigo")?.Trim(), ["Nombre"] = f.Texto("Nombre")?.Trim(), ["Categoria"] = f.Texto("Categoria")?.Trim(),
                    ["EsAmortizablePredeterminado"] = f.Logico("EsAmortizablePredeterminado"), ["EstaActivo"] = f.Logico("EstaActivo"),
                    ["Lineas"] = lineas.GetValueOrDefault(id), ["Activos"] = activos.GetValueOrDefault(id),
                });
            });
    });

    public override async Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto)
    {
        var r = await base.NuevoAsync(contexto);
        r["EsAmortizablePredeterminado"] = true;
        r["EstaActivo"] = true;
        return r;
    }

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var errores = base.Validar(r).ToList();
        if (r.EsNuevo && r.Texto("Codigo") is { } codigo && !codigo.All(ch => char.IsAsciiLetterUpper(ch) || char.IsAsciiDigit(ch) || ch == '_'))
            errores.Add(new("Codigo", "Código: solo mayúsculas sin acentos, cifras y _, por ejemplo VIVIENDA."));
        return errores;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var nombre = r.Texto("Nombre")!;
        var categoria = r.Texto("Categoria")!;
        var id = await bd.EnTransaccionAsync(async s =>
        {
            if (r.EsNuevo)
            {
                var codigo = r.Texto("Codigo")!;
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposActivo WHERE Codigo = ?", codigo), CultureInfo.InvariantCulture) > 0) return -1;
                await s.EjecutarAsync("INSERT INTO TiposActivo (Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo) VALUES (?, ?, ?, ?, ?)",
                    codigo, nombre, categoria, r.Logico("EsAmortizablePredeterminado"), r.Logico("EstaActivo"));
                return await s.UltimoIdAsync();
            }
            var clave = ProveedoresActivos.Id(r.Clave);
            await s.EjecutarAsync("UPDATE TiposActivo SET Nombre = ?, Categoria = ?, EsAmortizablePredeterminado = ?, EstaActivo = ? WHERE IdTipoActivo = ?",
                nombre, categoria, r.Logico("EsAmortizablePredeterminado"), r.Logico("EstaActivo"), clave);
            return clave;
        });
        if (id < 0) return ProveedoresActivos.Fallo<RegistroMantenimiento>("TiposActivo.Repetido", $"Ya hay un tipo de activo con el código {r.Texto("Codigo")}.", "Codigo", "Usa otro código.");
        var guardado = (await ListarAsync(c)).FirstOrDefault(x => ProveedoresActivos.Id(x.Clave) == id);
        return guardado is null
            ? ProveedoresActivos.Fallo<RegistroMantenimiento>("TiposActivo.NoExiste", "El tipo de activo ya no existe.", null, "Actualiza la lista.")
            : Resultado<RegistroMantenimiento>.Correcto(guardado);
    }

    public override async Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = ProveedoresActivos.Id(r.Clave);
        var usos = await bd.EjecutarAsync(async s => (
            Activos: Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Activos WHERE IdTipoActivo = ?", id), CultureInfo.InvariantCulture),
            Lineas: Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PlantillasSubcuentasActivo WHERE IdTipoActivo = ?", id), CultureInfo.InvariantCulture)));
        if (usos.Activos > 0 || usos.Lineas > 0)
            return Resultado.Fallo(new Error("TiposActivo.EnUso",
                $"«{r.Texto("Nombre")}» no se borra: lo usan {ProveedoresActivos.Plural(usos.Activos, "activo", "activos")} (de todas las empresas) y tiene {ProveedoresActivos.Plural(usos.Lineas, "línea", "líneas")} de plantilla.",
                TituloRegistro(r), null, "Desmarca «Activo» para que no se ofrezca más."));
        await bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM TiposActivo WHERE IdTipoActivo = ?", id));
        return Resultado.Correcto();
    }

    public override Task<Resultado<ResultadoAccion>> ProponerAsync(string accion, RegistroMantenimiento r, ContextoMantenimiento c) =>
        accion == ProveedoresActivos.AccionGenerar && !r.EsNuevo
            ? ProveedoresActivos.ProponerGenerarAsync(generador, c, ProveedoresActivos.Id(r.Clave), r.Texto("Nombre") ?? "")
            : base.ProponerAsync(accion, r, c);

    public override Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento r, ContextoMantenimiento c) =>
        accion == ProveedoresActivos.AccionGenerar && !r.EsNuevo
            ? ProveedoresActivos.GenerarAsync(generador, c, ProveedoresActivos.Id(r.Clave), r.Texto("Nombre") ?? "")
            : base.EjecutarAsync(accion, r, c);
}

/// <summary>
/// Catálogo de tipos de subcuenta (TiposSubcuentaActivo): nombre con [TOKEN], descripción y rol. El rol se ve con su texto.
/// En cada tipo de activo, como mucho una línea de plantilla por rol fijo.
/// </summary>
public sealed class ProveedorTiposSubcuentaActivo(IBaseDatos bd) : ProveedorBase
{
    private static readonly IReadOnlyList<OpcionCampo> Roles = [.. RolesSubcuentaActivo.Textos.Select(x => new OpcionCampo(x.Key, x.Value))];

    public override string Clave => "activos.tipos-subcuenta";
    public override string Titulo => "Tipos de subcuenta de activo";
    public override string Categoria => ProveedoresActivos.Categoria;
    public override string Finalidad => "Las subcuentas que puede tener un activo (valor del bien, amortización acumulada, seguro…): su nombre, con [TOKEN] donde va el nombre del activo, y para qué la usan los procesos.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Compartido;
    public override string NombreRegistro => "tipo de subcuenta";
    public override IReadOnlyList<string> Sinonimos => ["subcuenta", "plantilla", "rol", "token", "amortizacion acumulada", "281", "282", "681", "682"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Nombre", "Nombre", TipoCampo.Texto) { Obligatorio = true, Longitud = ReglasSubcuentaActivo.LongitudMaximaNombre, Ancho = "l",
            Ayuda = "El nombre de la subcuenta, con [TOKEN] (u otra palabra entre corchetes) donde va el nombre del activo. Por ejemplo «Amort. acumulada [TOKEN]»." },
        new("Descripcion", "Descripción") { Longitud = 255, Ancho = "f", Ayuda = "Se copia como descripción de las subcuentas que se creen." },
        new("Rol", "Rol", TipoCampo.Lista) { Obligatorio = true, Opciones = Roles, Ancho = "l",
            Ayuda = "Para qué la usan los procesos (amortización, baja, facturación). En la plantilla de cada tipo de activo, cada rol fijo solo puede estar una vez." },
        new("EstaActivo", "Activo", TipoCampo.Logico) { Ayuda = "Un tipo inactivo no se ofrece al añadir líneas de plantilla; las líneas que ya lo usan siguen igual." },
        new("Lineas", "Líneas de plantilla", TipoCampo.Entero) { SoloLectura = true, Ancho = "xs" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas =>
        [new("Nombre", "Nombre"), new("Rol", "Rol", 230), new("EstaActivo", "Activo", 60), new("Lineas", "Líneas", 60, true)];

    public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) => bd.EjecutarAsync(async s =>
    {
        var lineas = (await s.ConsultarAsync("SELECT IdTipoSubcuentaActivo, COUNT(*) AS N FROM PlantillasSubcuentasActivo GROUP BY IdTipoSubcuentaActivo",
            f => (Id: f.Entero("IdTipoSubcuentaActivo"), N: f.Entero("N")))).ToDictionary(x => x.Id, x => x.N);
        return (IReadOnlyList<RegistroMantenimiento>)await s.ConsultarAsync(
            "SELECT IdTipoSubcuentaActivo, Nombre, Descripcion, Rol, EstaActivo FROM TiposSubcuentaActivo ORDER BY Nombre",
            f =>
            {
                var id = f.Entero("IdTipoSubcuentaActivo");
                return new RegistroMantenimiento(id, new Dictionary<string, object?>
                {
                    ["Nombre"] = f.Texto("Nombre")?.Trim(), ["Descripcion"] = f.Texto("Descripcion")?.Trim(), ["Rol"] = f.Texto("Rol")?.Trim(),
                    ["EstaActivo"] = f.Logico("EstaActivo"), ["Lineas"] = lineas.GetValueOrDefault(id),
                });
            });
    });

    public override async Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto)
    {
        var r = await base.NuevoAsync(contexto);
        r["Rol"] = RolesSubcuentaActivo.Libre;
        r["EstaActivo"] = true;
        return r;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var nombre = r.Texto("Nombre")!;
        var rol = r.Texto("Rol")!;
        var resultado = await bd.EnTransaccionAsync(async s =>
        {
            var clave = r.EsNuevo ? -1 : ProveedoresActivos.Id(r.Clave);
            var repetido = await s.ConsultarPrimeroAsync("SELECT IdTipoSubcuentaActivo FROM TiposSubcuentaActivo WHERE Nombre = ? AND IdTipoSubcuentaActivo <> ?",
                f => (int?)f.Entero("IdTipoSubcuentaActivo"), nombre, clave);
            if (repetido is not null)
                return ProveedoresActivos.Fallo<int>("TiposSubcuentaActivo.Repetido", $"Ya hay un tipo de subcuenta llamado «{nombre}».", "Nombre", "Usa ese o cambia el nombre.");
            if (!r.EsNuevo && RolesSubcuentaActivo.EsFijo(rol))
            {
                // El rol fijo nuevo no puede chocar con otra línea del mismo tipo de activo que ya lo tenga.
                var choques = await s.ConsultarAsync(
                    "SELECT ta.Nombre FROM ((PlantillasSubcuentasActivo AS p INNER JOIN PlantillasSubcuentasActivo AS otra ON p.IdTipoActivo = otra.IdTipoActivo) " +
                    "INNER JOIN TiposSubcuentaActivo AS t ON otra.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo) INNER JOIN TiposActivo AS ta ON p.IdTipoActivo = ta.IdTipoActivo " +
                    "WHERE p.IdTipoSubcuentaActivo = ? AND otra.IdTipoSubcuentaActivo <> ? AND t.Rol = ?",
                    f => f.Texto("Nombre")?.Trim() ?? "", clave, clave, rol);
                if (choques.Count > 0)
                    return ProveedoresActivos.Fallo<int>("TiposSubcuentaActivo.RolRepetido",
                        $"El rol «{RolesSubcuentaActivo.Textos[rol]}» ya lo tiene otra línea en la plantilla de {string.Join(", ", choques.Distinct())}.", "Rol",
                        "Elige otro rol o cambia antes esa otra línea.");
            }
            if (r.EsNuevo)
            {
                await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES (?, ?, ?, ?)", nombre, r.Texto("Descripcion"), rol, r.Logico("EstaActivo"));
                return Resultado<int>.Correcto(await s.UltimoIdAsync());
            }
            await s.EjecutarAsync("UPDATE TiposSubcuentaActivo SET Nombre = ?, Descripcion = ?, Rol = ?, EstaActivo = ? WHERE IdTipoSubcuentaActivo = ?",
                nombre, r.Texto("Descripcion"), rol, r.Logico("EstaActivo"), clave);
            return Resultado<int>.Correcto(clave);
        });
        if (!resultado.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(resultado.Errores);
        var guardado = (await ListarAsync(c)).First(x => ProveedoresActivos.Id(x.Clave) == resultado.Valor);
        if (!ReglasSubcuentaActivo.TieneToken(nombre))
            guardado["_Aviso"] = "El nombre no lleva [TOKEN]: todas las subcuentas de este tipo se llamarán igual, sin el nombre del activo.";
        return Resultado<RegistroMantenimiento>.Correcto(guardado);
    }

    public override async Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = ProveedoresActivos.Id(r.Clave);
        var lineas = await bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM PlantillasSubcuentasActivo WHERE IdTipoSubcuentaActivo = ?", id), CultureInfo.InvariantCulture));
        if (lineas > 0)
            return Resultado.Fallo(new Error("TiposSubcuentaActivo.EnUso", $"«{r.Texto("Nombre")}» no se borra: lo usan {ProveedoresActivos.Plural(lineas, "línea", "líneas")} de plantilla.",
                r.Texto("Nombre"), null, "Desmarca «Activo» para que no se ofrezca más."));
        await bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM TiposSubcuentaActivo WHERE IdTipoSubcuentaActivo = ?", id));
        return Resultado.Correcto();
    }
}

/// <summary>
/// Líneas de las plantillas de subcuentas (PlantillasSubcuentasActivo): tipo de activo, tipo de subcuenta (se elige o se escribe
/// y se crea por dentro con rol libre), prefijo, orden y si está activa. Una línea con subcuentas no se borra: se desactiva, y
/// las suyas que nunca han tenido apuntes se pueden borrar (activos 2026-11-12 11:45, C).
/// </summary>
public sealed class ProveedorPlantillasSubcuentasActivo(IBaseDatos bd, GeneradorSubcuentasActivo generador) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _tiposActivo = [];
    private IReadOnlyList<OpcionCampo> _tiposSubcuenta = [];

    public override string Clave => "activos.plantillas";
    public override string Titulo => "Plantillas de subcuentas";
    public override string Categoria => ProveedoresActivos.Categoria;
    public override string Finalidad => "Qué subcuentas tiene cada tipo de activo: una línea por subcuenta, con el prefijo del que cuelga. Escribe el tipo de activo en el filtro para ver su plantilla.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Compartido;
    public override string NombreRegistro => "línea de plantilla";
    public override IReadOnlyList<string> Sinonimos => ["plantilla", "subcuenta", "prefijo", "generar subcuentas", "inmueble", "cuentas inmuebles"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("IdTipoActivo", "Tipo de activo", TipoCampo.Lista) { Obligatorio = true, Opciones = _tiposActivo, Ancho = "m" },
        new("TipoSubcuenta", "Tipo de subcuenta", TipoCampo.ListaAbierta) { Obligatorio = true, Opciones = _tiposSubcuenta, Longitud = ReglasSubcuentaActivo.LongitudMaximaNombre, Ancho = "l",
            Ayuda = "Elige uno del catálogo o escribe un nombre nuevo (con [TOKEN] donde va el nombre del activo): se crea en «Tipos de subcuenta de activo» con rol «Otra cuenta del activo»." },
        new("Rol", "Rol") { SoloLectura = true, Ancho = "l", Ayuda = "El del tipo de subcuenta; se cambia en «Tipos de subcuenta de activo»." },
        new("Prefijo", "Prefijo", TipoCampo.Cuenta) { Obligatorio = true, Longitud = 7, Ancho = "s",
            Ayuda = "Cuenta de la que cuelgan las subcuentas, por ejemplo 2810 o 21800. Cambiarlo no recodifica las subcuentas que ya existen." },
        new("Orden", "Orden", TipoCampo.Entero) { Minimo = 0, Maximo = 32767, Ancho = "xs", Ayuda = "Vacío: después de la última línea del tipo de activo." },
        new("EstaActiva", "Activa", TipoCampo.Logico) { Ayuda = "Una línea inactiva no genera subcuentas; las que ya generó se quedan." },
        new("Subcuentas", "Subcuentas generadas (todas las empresas)", TipoCampo.Entero) { SoloLectura = true, Ancho = "xs" },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas =>
    [
        new("IdTipoActivo", "Tipo de activo", 150), new("Orden", "Orden", 55, true), new("TipoSubcuenta", "Tipo de subcuenta"), new("Rol", "Rol", 200),
        new("Prefijo", "Prefijo", 70), new("EstaActiva", "Activa", 55), new("Subcuentas", "Subcuentas", 80, true),
    ];

    public override IReadOnlyList<AccionMantenimiento> Acciones =>
    [
        ProveedoresActivos.Generar("Crea, da de alta en el ejercicio o renombra las subcuentas de los activos del tipo de esta línea según su plantilla. Antes te enseña la propuesta."),
        new(ProveedoresActivos.AccionBorrarSinApuntes, "Borrar subcuentas sin apuntes",
            "En una línea desactivada: borra sus subcuentas de esta empresa que nunca han tenido apuntes, para no dejar residuos. Antes te enseña cuáles.", Modifica: true, ConPropuesta: true),
    ];

    public override string TituloRegistro(RegistroMantenimiento r) =>
        r.EsNuevo ? "Nueva línea de plantilla" : $"{Texto(_tiposActivo, r["IdTipoActivo"])} · {Texto(_tiposSubcuenta, r["TipoSubcuenta"])}";

    private static string Texto(IReadOnlyList<OpcionCampo> opciones, object? valor) =>
        opciones.FirstOrDefault(o => Equals(o.Valor, valor is null ? null : Convert.ToInt32(valor, CultureInfo.InvariantCulture)))?.Texto ?? Convert.ToString(valor, CultureInfo.CurrentCulture) ?? "";

    private async Task CargarOpcionesAsync(ISesionDatos s)
    {
        _tiposActivo = await s.ConsultarAsync("SELECT IdTipoActivo, Nombre, EstaActivo FROM TiposActivo ORDER BY Nombre",
            f => new OpcionCampo(f.Entero("IdTipoActivo"), (f.Texto("Nombre")?.Trim() ?? "") + (f.Logico("EstaActivo") ? "" : " (inactivo)")));
        // Los tipos de subcuenta inactivos no se ofrecen, pero se muestran en las líneas que ya los usan (si no, se vería su id).
        _tiposSubcuenta = await s.ConsultarAsync("SELECT IdTipoSubcuentaActivo, Nombre, EstaActivo FROM TiposSubcuentaActivo ORDER BY EstaActivo, Nombre",
            f => new OpcionCampo(f.Entero("IdTipoSubcuentaActivo"), (f.Texto("Nombre")?.Trim() ?? "") + (f.Logico("EstaActivo") ? "" : " (inactivo)")));
    }

    public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) => bd.EjecutarAsync(async s =>
    {
        await CargarOpcionesAsync(s);
        var subcuentas = (await s.ConsultarAsync("SELECT IdPlantillaSubcuentaActivo, COUNT(*) AS N FROM SubcuentasActivo GROUP BY IdPlantillaSubcuentaActivo",
            f => (Id: f.Entero("IdPlantillaSubcuentaActivo"), N: f.Entero("N")))).ToDictionary(x => x.Id, x => x.N);
        return (IReadOnlyList<RegistroMantenimiento>)await s.ConsultarAsync(
            "SELECT p.IdPlantillaSubcuentaActivo, p.IdTipoActivo, p.IdTipoSubcuentaActivo, p.Prefijo, p.Orden, p.EstaActiva, t.Rol, ta.Nombre AS NombreTipo " +
            "FROM (PlantillasSubcuentasActivo AS p INNER JOIN TiposSubcuentaActivo AS t ON p.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo) " +
            "INNER JOIN TiposActivo AS ta ON p.IdTipoActivo = ta.IdTipoActivo ORDER BY ta.Nombre, p.Orden, p.IdPlantillaSubcuentaActivo",
            f =>
            {
                var id = f.Entero("IdPlantillaSubcuentaActivo");
                var rol = f.Texto("Rol")?.Trim() ?? "";
                var n = subcuentas.GetValueOrDefault(id);
                var r = new RegistroMantenimiento(id, new Dictionary<string, object?>
                {
                    ["IdTipoActivo"] = f.Entero("IdTipoActivo"), ["TipoSubcuenta"] = f.Entero("IdTipoSubcuentaActivo"),
                    ["Rol"] = RolesSubcuentaActivo.Textos.GetValueOrDefault(rol, rol), ["Prefijo"] = f.Texto("Prefijo")?.Trim(), ["Orden"] = f.Entero("Orden"),
                    ["EstaActiva"] = f.Logico("EstaActiva"), ["Subcuentas"] = n,
                });
                if (n > 0) r.Bloqueos["IdTipoActivo"] = "Ya tiene subcuentas generadas: para otro tipo de activo, crea otra línea.";
                return r;
            });
    });

    public override async Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto)
    {
        await bd.EjecutarAsync(async s => { await CargarOpcionesAsync(s); return true; });
        var r = await base.NuevoAsync(contexto);
        r["EstaActiva"] = true;
        return r;
    }

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var errores = base.Validar(r).ToList();
        if (r.Texto("Prefijo") is { } prefijo && (prefijo.Length < 3 || !prefijo.All(char.IsAsciiDigit)))
            errores.Add(new("Prefijo", "Prefijo: entre 3 y 7 cifras, por ejemplo 2810."));
        return errores;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var idTipoActivo = r.Entero("IdTipoActivo")!.Value;
        var prefijo = r.Texto("Prefijo")!;
        var activa = r.Logico("EstaActiva");
        var elegido = r["TipoSubcuenta"];
        var resultado = await bd.EnTransaccionAsync(async s =>
        {
            var clave = r.EsNuevo ? -1 : ProveedoresActivos.Id(r.Clave);
            // Tipo de subcuenta: el elegido, el del catálogo con ese nombre o uno nuevo con rol libre (activos 2026-11-12 09:47, Q10).
            int idTipoSubcuenta;
            string rol;
            var creado = false;
            if (elegido is int or short or long or decimal)
            {
                idTipoSubcuenta = Convert.ToInt32(elegido, CultureInfo.InvariantCulture);
                var existente = await s.ConsultarPrimeroAsync("SELECT Rol FROM TiposSubcuentaActivo WHERE IdTipoSubcuentaActivo = ?", f => f.Texto("Rol")?.Trim(), idTipoSubcuenta);
                if (existente is null) return ProveedoresActivos.Fallo<int>("Plantillas.TipoSubcuentaNoExiste", "El tipo de subcuenta elegido ya no existe.", "TipoSubcuenta", "Elige otro.");
                rol = existente;
            }
            else
            {
                var nombre = (Convert.ToString(elegido, CultureInfo.CurrentCulture) ?? "").Trim();
                var existente = await s.ConsultarPrimeroAsync("SELECT IdTipoSubcuentaActivo, Rol FROM TiposSubcuentaActivo WHERE Nombre = ?",
                    f => (Id: f.Entero("IdTipoSubcuentaActivo"), Rol: f.Texto("Rol")?.Trim() ?? ""), nombre);
                if (existente.Id > 0)
                    (idTipoSubcuenta, rol) = existente;
                else
                {
                    await s.EjecutarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES (?, ?, ?, ?)", nombre, null, RolesSubcuentaActivo.Libre, true);
                    (idTipoSubcuenta, rol, creado) = (await s.UltimoIdAsync(), RolesSubcuentaActivo.Libre, true);
                }
            }
            if (!creado)
            {
                var repetida = await s.ConsultarPrimeroAsync(
                    "SELECT IdPlantillaSubcuentaActivo FROM PlantillasSubcuentasActivo WHERE IdTipoActivo = ? AND IdTipoSubcuentaActivo = ? AND IdPlantillaSubcuentaActivo <> ?",
                    f => (int?)f.Entero("IdPlantillaSubcuentaActivo"), idTipoActivo, idTipoSubcuenta, clave);
                if (repetida is not null)
                    return ProveedoresActivos.Fallo<int>("Plantillas.LineaRepetida", "Este tipo de activo ya tiene una línea con ese tipo de subcuenta.", "TipoSubcuenta", "Cambia esa línea en vez de crear otra.");
                if (RolesSubcuentaActivo.EsFijo(rol))
                {
                    var conRol = await s.ConsultarPrimeroAsync(
                        "SELECT t.Nombre FROM PlantillasSubcuentasActivo AS p INNER JOIN TiposSubcuentaActivo AS t ON p.IdTipoSubcuentaActivo = t.IdTipoSubcuentaActivo " +
                        "WHERE p.IdTipoActivo = ? AND t.Rol = ? AND p.IdPlantillaSubcuentaActivo <> ?", f => f.Texto("Nombre")?.Trim(), idTipoActivo, rol, clave);
                    if (conRol is not null)
                        return ProveedoresActivos.Fallo<int>("Plantillas.RolRepetido",
                            $"Este tipo de activo ya tiene una línea con el rol «{RolesSubcuentaActivo.Textos[rol]}» («{conRol}»).", "TipoSubcuenta",
                            "Cada rol fijo solo puede estar una vez en la plantilla de un tipo de activo.");
                }
            }
            var orden = r.Entero("Orden") ?? Convert.ToInt32(await s.EscalarAsync("SELECT IIF(MAX(Orden) IS NULL, 0, MAX(Orden)) FROM PlantillasSubcuentasActivo WHERE IdTipoActivo = ?", idTipoActivo),
                CultureInfo.InvariantCulture) + 1;
            if (r.EsNuevo)
            {
                await s.EjecutarAsync("INSERT INTO PlantillasSubcuentasActivo (IdTipoActivo, IdTipoSubcuentaActivo, Prefijo, Orden, EstaActiva) VALUES (?, ?, ?, ?, ?)",
                    idTipoActivo, idTipoSubcuenta, prefijo, (short)orden, activa);
                return Resultado<int>.Correcto(await s.UltimoIdAsync());
            }
            await s.EjecutarAsync("UPDATE PlantillasSubcuentasActivo SET IdTipoActivo = ?, IdTipoSubcuentaActivo = ?, Prefijo = ?, Orden = ?, EstaActiva = ? WHERE IdPlantillaSubcuentaActivo = ?",
                idTipoActivo, idTipoSubcuenta, prefijo, (short)orden, activa, clave);
            return Resultado<int>.Correcto(clave);
        });
        if (!resultado.EsCorrecto) return Resultado<RegistroMantenimiento>.Fallo(resultado.Errores);
        var guardado = (await ListarAsync(c)).First(x => ProveedoresActivos.Id(x.Clave) == resultado.Valor);
        if (!activa && guardado.Entero("Subcuentas") is > 0)
        {
            var sinApuntes = (await generador.SinApuntesAsync(c.Trabajo, resultado.Valor)).Count;
            guardado["_Aviso"] = sinApuntes > 0
                ? $"La línea ya no generará más subcuentas. {ProveedoresActivos.Plural(sinApuntes, "subcuenta suya de esta empresa no tiene", "subcuentas suyas de esta empresa no tienen")} ningún apunte: puedes borrarlas con «Borrar subcuentas sin apuntes»."
                : "La línea ya no generará más subcuentas; las que ya generó se quedan.";
        }
        return Resultado<RegistroMantenimiento>.Correcto(guardado);
    }

    public override async Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = ProveedoresActivos.Id(r.Clave);
        var n = await bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM SubcuentasActivo WHERE IdPlantillaSubcuentaActivo = ?", id), CultureInfo.InvariantCulture));
        if (n > 0)
            return Resultado.Fallo(new Error("Plantillas.ConSubcuentas", $"La línea no se borra: ya generó {ProveedoresActivos.Plural(n, "subcuenta", "subcuentas")}.", TituloRegistro(r), null,
                "Desmárcala como activa: no generará más. Si quieres, borra después las que no tienen apuntes con «Borrar subcuentas sin apuntes»."));
        await bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM PlantillasSubcuentasActivo WHERE IdPlantillaSubcuentaActivo = ?", id));
        return Resultado.Correcto();
    }

    public override async Task<Resultado<ResultadoAccion>> ProponerAsync(string accion, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        if (r.EsNuevo) return ProveedoresActivos.Fallo<ResultadoAccion>("Plantillas.SinGuardar", "Guarda antes la línea.");
        var idTipo = r.Entero("IdTipoActivo")!.Value;
        if (accion == ProveedoresActivos.AccionGenerar)
            return await ProveedoresActivos.ProponerGenerarAsync(generador, c, idTipo, Texto(_tiposActivo, idTipo));
        if (accion != ProveedoresActivos.AccionBorrarSinApuntes) return await base.ProponerAsync(accion, r, c);
        if (r.Logico("EstaActiva"))
            return ProveedoresActivos.Fallo<ResultadoAccion>("Plantillas.LineaActiva", "La línea está activa: si borro sus subcuentas, «Generar subcuentas» las volvería a crear.",
                null, "Desmarca «Activa», guarda y vuelve a pedirlo.");
        var subcuentas = await generador.SinApuntesAsync(c.Trabajo, ProveedoresActivos.Id(r.Clave));
        if (subcuentas.Count == 0)
            return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion($"Ninguna subcuenta de esta línea en {c.RazonSocial} está sin apuntes: no hay nada que borrar.") { NadaQueHacer = true });
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(
            $"Voy a borrar {ProveedoresActivos.Plural(subcuentas.Count, "subcuenta", "subcuentas")} de {c.RazonSocial} que nunca {(subcuentas.Count == 1 ? "ha" : "han")} tenido apuntes, en todos los ejercicios, y a liberar su código. No se puede deshacer. ¿Confirmas?",
            null, [.. subcuentas.Select(x => $"{x.Activo}: {x.Codigo} {x.Nombre}")]));
    }

    public override async Task<Resultado<ResultadoAccion>> EjecutarAsync(string accion, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        if (r.EsNuevo) return ProveedoresActivos.Fallo<ResultadoAccion>("Plantillas.SinGuardar", "Guarda antes la línea.");
        var idTipo = r.Entero("IdTipoActivo")!.Value;
        if (accion == ProveedoresActivos.AccionGenerar)
            return await ProveedoresActivos.GenerarAsync(generador, c, idTipo, Texto(_tiposActivo, idTipo));
        if (accion != ProveedoresActivos.AccionBorrarSinApuntes) return await base.EjecutarAsync(accion, r, c);
        if (r.Logico("EstaActiva"))
            return ProveedoresActivos.Fallo<ResultadoAccion>("Plantillas.LineaActiva", "La línea está activa: desmarca «Activa» y guarda antes de borrar sus subcuentas.");
        var (borradas, errores) = await generador.BorrarSinApuntesAsync(c.Trabajo, ProveedoresActivos.Id(r.Clave));
        var mensaje = $"Hecho: he borrado {ProveedoresActivos.Plural(borradas, "subcuenta", "subcuentas")} sin apuntes." +
            (errores.Count == 0 ? "" : $" {ProveedoresActivos.Plural(errores.Count, "subcuenta no se ha podido borrar", "subcuentas no se han podido borrar")}; te dejo el motivo abajo.");
        return Resultado<ResultadoAccion>.Correcto(new ResultadoAccion(mensaje, null, errores.Count == 0 ? null : errores));
    }
}
