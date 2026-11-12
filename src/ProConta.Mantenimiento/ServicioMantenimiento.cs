using System.Globalization;
using System.Text;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Mantenimiento;

/// <summary>Resultado de la búsqueda Ctrl+K: un mantenimiento o uno de sus campos.</summary>
public sealed record ResultadoBusqueda(IProveedorMantenimiento Proveedor, CampoMantenimiento? Campo, string Texto, string Ruta);

/// <summary>
/// Servicio de la pantalla genérica (plataforma/mantenimiento): catálogo de proveedores, búsqueda, y guardar, borrar y
/// acciones con la comprobación de permiso (MANTENIMIENTO.EDITAR) y de validación en el servicio, no solo en la pantalla.
/// </summary>
public sealed class ServicioMantenimiento(IEnumerable<IProveedorMantenimiento> proveedores, Func<Task<bool>> puedeEditar)
{
    public static readonly IReadOnlyList<string> Categorias = ["General", "Empresas y ejercicios", "Facturación", "Activos", "Integraciones", "Catálogos"];

    public IReadOnlyList<IProveedorMantenimiento> Proveedores { get; } = proveedores.ToList();

    public Task<bool> PuedeEditarAsync() => puedeEditar();

    public IProveedorMantenimiento? Proveedor(string clave) => Proveedores.FirstOrDefault(p => p.Clave == clave);

    /// <summary>Busca por título, finalidad, sinónimos y etiquetas de campo, sin tildes ni mayúsculas.</summary>
    public IReadOnlyList<ResultadoBusqueda> Buscar(string texto, int maximo = 30)
    {
        var q = Plano(texto);
        if (q.Length == 0) return [];
        var r = new List<(int Peso, ResultadoBusqueda R)>();
        foreach (var p in Proveedores)
        {
            var ruta = $"{p.Categoria} › {p.Titulo}";
            if (Plano(p.Titulo).Contains(q)) r.Add((0, new(p, null, p.Titulo, p.Categoria)));
            else if (p.Sinonimos.Any(s => Plano(s).Contains(q)) || Plano(p.Finalidad).Contains(q)) r.Add((1, new(p, null, p.Titulo, p.Categoria)));
            foreach (var c in p.Campos.Where(c => Plano(c.Etiqueta).Contains(q) || c.Sinonimos.Any(s => Plano(s).Contains(q))))
                r.Add((2, new(p, c, c.Etiqueta, ruta)));
        }
        return r.OrderBy(x => x.Peso).ThenBy(x => x.R.Texto, StringComparer.CurrentCulture).Select(x => x.R).Take(maximo).ToList();
    }

    public async Task<Resultado<RegistroMantenimiento>> GuardarAsync(IProveedorMantenimiento p, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var bloqueo = await Bloqueo(p, r, r.EsNuevo ? "crear" : "cambiar");
        if (bloqueo is not null) return Resultado<RegistroMantenimiento>.Fallo(bloqueo);
        // Un parámetro único (fila sola) que aún no existe se crea al guardarlo por primera vez.
        if (r.EsNuevo && !p.PermiteAlta && p.Cardinalidad != Cardinalidad.Unico) return Resultado<RegistroMantenimiento>.Fallo(new Error("Mantenimiento.SinAlta", $"En {p.Titulo} no se crean registros."));
        var errores = p.Validar(r);
        if (errores.Count > 0)
            return Resultado<RegistroMantenimiento>.Fallo(errores.Select(e => new Error("Mantenimiento.Campo", e.Mensaje, e.Campo)));
        return await p.GuardarAsync(r, c);
    }

    public async Task<Resultado> EliminarAsync(IProveedorMantenimiento p, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var bloqueo = await Bloqueo(p, r, "borrar");
        if (bloqueo is not null) return Resultado.Fallo(bloqueo);
        if (!p.PermiteBaja) return Resultado.Fallo(new Error("Mantenimiento.SinBaja", $"En {p.Titulo} no se borran registros."));
        return await p.EliminarAsync(r, c);
    }

    public async Task<Resultado<ResultadoAccion>> EjecutarAsync(IProveedorMantenimiento p, string accion, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var a = p.Acciones.FirstOrDefault(x => x.Clave == accion);
        if (a is null) return Resultado<ResultadoAccion>.Fallo(new Error("Mantenimiento.AccionDesconocida", $"La acción «{accion}» no existe en {p.Titulo}."));
        if (a.Modifica && await Bloqueo(p, r, a.Texto.ToLowerInvariant()) is Error e) return Resultado<ResultadoAccion>.Fallo(e);
        return await p.EjecutarAsync(accion, r, c);
    }

    /// <summary>Propuesta de una acción (qué hará, sin cambiar nada), con las mismas comprobaciones que al ejecutarla.</summary>
    public async Task<Resultado<ResultadoAccion>> ProponerAsync(IProveedorMantenimiento p, string accion, RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var a = p.Acciones.FirstOrDefault(x => x.Clave == accion);
        if (a is null) return Resultado<ResultadoAccion>.Fallo(new Error("Mantenimiento.AccionDesconocida", $"La acción «{accion}» no existe en {p.Titulo}."));
        if (a.Modifica && await Bloqueo(p, r, a.Texto.ToLowerInvariant()) is Error e) return Resultado<ResultadoAccion>.Fallo(e);
        return await p.ProponerAsync(accion, r, c);
    }

    private async Task<Error?> Bloqueo(IProveedorMantenimiento p, RegistroMantenimiento r, string verbo)
    {
        if (p.MotivoSoloConsulta is string m) return new Error("Mantenimiento.SoloConsulta", m, p.Titulo);
        if (!await puedeEditar())
            return new Error("Seguridad.SinPermiso", $"No tienes permiso para {verbo} en los mantenimientos: tu perfil es de consulta.", p.Titulo,
                null, "Pídeselo a quien administra los permisos.");
        if (r.MotivoSoloLectura is string ml && verbo != "crear") return new Error("Mantenimiento.SoloLectura", ml, p.TituloRegistro(r));
        return null;
    }

    public static string Plano(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var d = texto.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(d.Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark).ToArray());
    }
}

/// <summary>Configuración general (tabla Configuracion, una sola fila con IdConfiguracion = 1; dueño: plataforma/mantenimiento).</summary>
public sealed class ProveedorConfiguracionGeneral(IBaseDatos bd) : ProveedorTablaAccess(bd, "general", "Configuración general", "General",
    "Identificación del sistema informático, ubicaciones de trabajo y correo de incidencias.", AmbitoMantenimiento.Compartido, "configuración",
    "Configuracion", "IdConfiguracion", ClaveNueva.Escrita,
    [
        new("SistemaInformaticoNombreRazon", "Nombre o razón social del productor") { Grupo = "sis", Longitud = 120, Ancho = "l" },
        new("SistemaInformaticoNIF", "NIF del productor", TipoCampo.Codigo) { Grupo = "sis", Longitud = 9, Ancho = "s", Sinonimos = ["nif"] },
        new("SistemaInformaticoNombre", "Nombre del sistema") { Grupo = "sis", Longitud = 30 },
        new("SistemaId", "Id del sistema", TipoCampo.Codigo) { Grupo = "sis", Longitud = 2, Ancho = "xs" },
        new("SistemaInformaticoVersion", "Versión") { Grupo = "sis", Longitud = 50, Ancho = "s", Sinonimos = ["version"] },
        new("SistemaInformaticoNumeroInstalacion", "Número de instalación") { Grupo = "sis", Longitud = 100, Ayuda = "Identificador estable y único de esta instalación. No se regenera ni se reutiliza en otra instalación." },
        new("SistemaInformaticoTipoUsoPosibleSoloVerifactu", "Tipo de uso posible solo VeriFactu", TipoCampo.Lista) { Grupo = "sis", Opciones = [new("S", "S · solo VeriFactu"), new("N", "N · también no VeriFactu")], Ancho = "s" },
        new("SistemaInformaticoTipoUsoPosibleMultiOT", "Tipo de uso posible multi-OT", TipoCampo.Lista) { Grupo = "sis", Opciones = [new("S", "S · varios obligados"), new("N", "N · un solo obligado")], Ancho = "s" },
        new("RutaDatosVeriFactu", "Carpeta de datos de VeriFactu", TipoCampo.Ruta) { Grupo = "ubi", Longitud = 255, Ancho = "l" },
        new("EndpointIPC", "Dirección del servicio IPC") { Grupo = "ubi", Longitud = 255, Ancho = "l", Sinonimos = ["endpoint", "url"] },
        new("EndpointIRAV", "Dirección del servicio IRAV") { Grupo = "ubi", Longitud = 255, Ancho = "l", Sinonimos = ["endpoint", "url"] },
        new("EmailIncidencias", "Correo para incidencias", TipoCampo.Email) { Grupo = "ubi", Longitud = 255, Ancho = "l", Sinonimos = ["email", "correo"], Ayuda = "Recibe los resúmenes de incidencias de los procesos. Vacío: no se envían avisos." },
        new("CorreoDesviarA", "Desviar todo el correo a", TipoCampo.Email) { Grupo = "ubi", Longitud = 254, Ancho = "l", Sinonimos = ["pruebas", "desvio", "correo", "email"],
            Ayuda = "Solo para una BD de pruebas: con una dirección, todo correo que envíe ProContaVF sale únicamente a ella, con los destinatarios reales anotados. Vacío, el correo va a sus destinatarios." },
        new("OcultarListaOperadoresAcceso", "Ocultar la lista de operadores al entrar", TipoCampo.Logico) { Grupo = "ubi" },
        new("VeriFactuActivo", "Activar VeriFactu", TipoCampo.Logico) { Grupo = "sis", Sinonimos = ["verifactu", "aeat"], Ayuda = "Desactivado, las facturas se validan sin comunicarse a la AEAT y sin QR. Las validadas así no se envían después. VeriFactu aún no está disponible en esta versión: activado, validar se detiene y lo explica." },
        new("UmbralCuadreMayor", "Umbral para reiniciar el cuadre del Mayor", TipoCampo.Importe) { Grupo = "con", Ancho = "s", Sinonimos = ["cuadre", "mayor", "umbral", "tolerancia"],
            Ayuda = "«Reiniciar el cuadre» del Mayor marca como cuadradas las cuentas con un saldo de hasta este importe, positivo o negativo. Vacío: 0,05 €." },
        new("FechaModificacion", "Modificado el", TipoCampo.Fecha) { Grupo = "ubi", SoloLectura = true },
        new("OperadorModificacion", "Modificado por") { Grupo = "ubi", SoloLectura = true },
    ],
    [])
{
    public override Cardinalidad Cardinalidad => Cardinalidad.Unico;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override IReadOnlyList<string> Sinonimos => ["sistema informatico", "nif", "version", "instalacion", "incidencias", "endpoint", "cuadre"];
    public override IReadOnlyList<GrupoCampos> Grupos => [new("sis", "Identificación del sistema"), new("ubi", "Ubicaciones e integraciones"), new("con", "Contabilidad")];
    public override string TituloRegistro(RegistroMantenimiento r) => "Configuración general";

    /// <summary>La única fila; si aún no existe, un registro nuevo que se crea al guardar.</summary>
    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto)
    {
        var filas = await base.ListarAsync(contexto);
        return filas.Count > 0 ? [filas[0]] : [await NuevoAsync(contexto)];
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto)
    {
        if (registro.EsNuevo)
        {
            await _bd2.EjecutarAsync(s => s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, OcultarListaOperadoresAcceso) VALUES (?, ?)", (byte)1, false));
            registro.Clave = (byte)1;
        }
        registro["FechaModificacion"] = DateTime.Now;
        registro["OperadorModificacion"] = contexto.Actor.Nombre.Length > 30 ? contexto.Actor.Nombre[..30] : contexto.Actor.Nombre;
        var r = await base.GuardarAsync(registro, contexto);
        if (r.EsCorrecto) await _bd2.EjecutarAsync(s => s.EjecutarAsync("UPDATE Configuracion SET FechaModificacion = ?, OperadorModificacion = ? WHERE IdConfiguracion = 1",
            registro["FechaModificacion"], registro["OperadorModificacion"]));
        return r.EsCorrecto ? Resultado<RegistroMantenimiento>.Correcto((await ListarAsync(contexto)).First()) : r;
    }

    private readonly IBaseDatos _bd2 = bd;
}
