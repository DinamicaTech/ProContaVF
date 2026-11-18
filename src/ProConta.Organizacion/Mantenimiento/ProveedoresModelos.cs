using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

namespace ProConta.Organizacion.Mantenimiento;

/// <summary>
/// Datos que piden los modelos tributarios y no tenían sitio (contabilidad/modelos-tributarios, esquema 72), en la pantalla genérica de
/// mantenimiento: los cargos de la empresa (200 y 390), los datos de los empleados para el 190 y sus familiares.
/// </summary>
public static class ProveedoresModelos
{
    public const string Categoria = "Empresas y ejercicios";

    public static IEnumerable<IProveedorMantenimiento> Todos(IBaseDatos bd) =>
        [new ProveedorCargosSociedad(bd), new ProveedorEmpleados190(bd), new ProveedorFamiliaresEmpleado(bd)];

    internal static string Texto(IFilaDatos f, string campo) => (f.Texto(campo) ?? "").Trim();

    internal static Resultado<RegistroMantenimiento> Fallo(string codigo, string mensaje, string? accion = null) =>
        Resultado<RegistroMantenimiento>.Fallo(new Error(codigo, mensaje, null, null, accion));

    internal static int? Id(object? clave) => clave is null ? null : Convert.ToInt32(clave, CultureInfo.InvariantCulture);

    internal static byte? Byte(RegistroMantenimiento r, string campo) => r[campo] is null or "" ? null : Convert.ToByte(r[campo], CultureInfo.InvariantCulture);

    internal static short? Short(RegistroMantenimiento r, string campo) => r[campo] is null or "" ? null : Convert.ToInt16(r[campo], CultureInfo.InvariantCulture);

    internal static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static readonly IReadOnlyList<OpcionCampo> OpcionesDiscapacidad =
    [
        new((byte)0, "Ninguna o menos del 33 %"), new((byte)1, "Del 33 % al 65 %"), new((byte)2, "Del 33 % al 65 %, con ayuda de terceros o movilidad reducida"), new((byte)3, "65 % o más"),
    ];
}

/// <summary>
/// Cargos de la empresa (CargosSociedad, esquema 72): administradores, titulares reales, secretario y representantes legales, con referencia
/// a su ficha de tercero (NIF, nombre y domicilio). Los usan el 200 (páginas 2 y 2 bis) y el 390 (representantes de la página 1).
/// </summary>
public sealed class ProveedorCargosSociedad(IBaseDatos bd) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _terceros = [];

    public static readonly IReadOnlyList<OpcionCampo> Cargos =
    [
        new("ADMINISTRADOR", "Administrador"), new("TITULAR_REAL", "Titular real"), new("SECRETARIO", "Secretario del consejo"), new("REPRESENTANTE", "Representante legal"),
    ];

    public override string Clave => "cargossociedad";
    public override string Titulo => "Cargos de la empresa";
    public override string Categoria => ProveedoresModelos.Categoria;
    public override string Finalidad =>
        "Administradores, titulares reales, secretario y representantes legales de la empresa: los piden el modelo 200 (páginas 2 y 2 bis) y el 390. Su NIF y su nombre salen de la ficha del tercero.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override string NombreRegistro => "cargo";
    public override IReadOnlyList<string> Sinonimos => ["administrador", "titular real", "secretario", "representante", "apoderado", "200", "390", "blanqueo"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("IdTercero", "Persona o entidad", TipoCampo.Lista) { Obligatorio = true, Opciones = _terceros, Ancho = "l",
            Ayuda = "Su ficha de tercero (Terceros): de ella salen el NIF, el nombre y el domicilio. Si no está, dala de alta primero en Terceros." },
        new("Cargo", "Cargo", TipoCampo.Lista) { Obligatorio = true, Opciones = Cargos, Ancho = "m",
            Ayuda = "Administrador (200, página 2), titular real (200, página 2 bis: una por persona), secretario del consejo o quien haga sus funciones, o representante legal (200 y 390)." },
        new("FechaNacimiento", "Fecha de nacimiento", TipoCampo.Fecha) { Ancho = "s", Ayuda = "Solo de los titulares reales: la pide la página 2 bis del 200." },
        new("Nacionalidad", "Nacionalidad", TipoCampo.Codigo) { Longitud = 2, Ancho = "xs", Ayuda = "Solo de los titulares reales: código ISO de dos letras (ES, NL…). Vacía: ES." },
        new("PaisResidencia", "País de residencia", TipoCampo.Codigo) { Longitud = 2, Ancho = "xs", Ayuda = "Solo de los titulares reales: código ISO de dos letras. Vacío: ES." },
        new("FechaPoder", "Fecha del poder", TipoCampo.Fecha) { Ancho = "s", Ayuda = "Solo de los representantes legales: fecha de la escritura de poder (200 y 390)." },
        new("Notaria", "Notaría", TipoCampo.Texto) { Longitud = 12, Ancho = "s", Ayuda = "Solo de los representantes legales: notaría del poder (doce caracteres) u «OTROS»." },
        new("FechaDesde", "Desde", TipoCampo.Fecha) { Ancho = "s", Ayuda = "Desde cuándo tiene el cargo. Vacía: siempre." },
        new("FechaHasta", "Hasta", TipoCampo.Fecha) { Ancho = "s", Ayuda = "Hasta cuándo tuvo el cargo. Vacía: lo sigue teniendo. Los modelos de un ejercicio toman los cargos vigentes a su 31/12." },
        new("Activo", "Activo", TipoCampo.Logico) { Ayuda = "Un cargo inactivo no sale en los modelos." },
        new("Notas", "Notas", TipoCampo.Texto) { Longitud = 255, Ancho = "l", Ayuda = "Notas libres: no van a la AEAT." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Tercero", "Persona o entidad"), new("CargoTexto", "Cargo", 160), new("Activo", "Activo", 60)];

    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nuevo cargo" : $"{r.Texto("Tercero")} · {r.Texto("CargoTexto")}";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        var (terceros, filas) = await bd.EjecutarAsync(async s =>
        {
            var t = await s.ConsultarAsync("SELECT IdTercero, Nombre, NIF FROM Terceros WHERE Activo = True ORDER BY Nombre",
                f => (Id: f.Entero("IdTercero"), Texto: $"{ProveedoresModelos.Texto(f, "Nombre")}{(string.IsNullOrWhiteSpace(f.Texto("NIF")) ? "" : $" · {ProveedoresModelos.Texto(f, "NIF")}")}"));
            var r = await s.ConsultarAsync(
                "SELECT IdCargoSociedad, IdTercero, Cargo, FechaNacimiento, Nacionalidad, PaisResidencia, FechaPoder, Notaria, FechaDesde, FechaHasta, Activo, Notas FROM CargosSociedad " +
                "WHERE IdSociedad = ? ORDER BY Cargo, IdCargoSociedad",
                f => new RegistroMantenimiento(f.Entero("IdCargoSociedad"), new Dictionary<string, object?>
                {
                    ["IdTercero"] = f.Entero("IdTercero"), ["Cargo"] = ProveedoresModelos.Texto(f, "Cargo"), ["FechaNacimiento"] = f.FechaNula("FechaNacimiento"),
                    ["Nacionalidad"] = f.Texto("Nacionalidad"), ["PaisResidencia"] = f.Texto("PaisResidencia"), ["FechaPoder"] = f.FechaNula("FechaPoder"),
                    ["Notaria"] = f.Texto("Notaria"), ["FechaDesde"] = f.FechaNula("FechaDesde"), ["FechaHasta"] = f.FechaNula("FechaHasta"), ["Activo"] = f.Logico("Activo"),
                    ["Notas"] = f.Texto("Notas"),
                }), c.Trabajo.IdSociedad);
            return (t, r);
        });
        _terceros = [.. terceros.Select(x => new OpcionCampo(x.Id, x.Texto))];
        var textos = terceros.ToDictionary(x => x.Id, x => x.Texto);
        foreach (var r in filas)
        {
            r["Tercero"] = r["IdTercero"] is int id && textos.TryGetValue(id, out var t) ? t : $"Tercero {r["IdTercero"]} (inactivo)";
            r["CargoTexto"] = Cargos.FirstOrDefault(x => Equals(x.Valor, r.Texto("Cargo")))?.Texto ?? r.Texto("Cargo");
        }
        return filas;
    }

    public override async Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto)
    {
        if (_terceros.Count == 0) await ListarAsync(contexto);
        var r = await base.NuevoAsync(contexto);
        r["Activo"] = true;
        return r;
    }

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var e = new List<ErrorCampo>(base.Validar(r));
        if (r.Texto("Cargo") == "TITULAR_REAL" && r["FechaNacimiento"] is null)
            e.Add(new ErrorCampo("FechaNacimiento", "El titular real necesita su fecha de nacimiento: la pide la página 2 bis del 200."));
        if (r.Texto("Cargo") == "REPRESENTANTE" && r["FechaPoder"] is null)
            e.Add(new ErrorCampo("FechaPoder", "El representante legal necesita la fecha de su poder: la piden el 200 y el 390."));
        foreach (var campo in new[] { "Nacionalidad", "PaisResidencia" })
            if (r.Texto(campo) is { Length: > 0 } p && (p.Trim().Length != 2 || !p.Trim().All(char.IsAsciiLetter)))
                e.Add(new ErrorCampo(campo, "Código ISO de dos letras, por ejemplo ES."));
        return e;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        if (r["IdTercero"] is null || string.IsNullOrWhiteSpace(r.Texto("Cargo")))
            return ProveedoresModelos.Fallo("CargosSociedad.Incompleto", "Falta la persona o el cargo.", "Complétalos y vuelve a guardar.");
        object?[] valores =
        [
            Convert.ToInt32(r["IdTercero"], CultureInfo.InvariantCulture), r.Texto("Cargo")!.Trim(), r["FechaNacimiento"] as DateTime?, ProveedoresModelos.Vacio(r.Texto("Nacionalidad"))?.ToUpperInvariant(),
            ProveedoresModelos.Vacio(r.Texto("PaisResidencia"))?.ToUpperInvariant(), r["FechaPoder"] as DateTime?, ProveedoresModelos.Vacio(r.Texto("Notaria")), r["FechaDesde"] as DateTime?,
            r["FechaHasta"] as DateTime?, r["Activo"] is true, ProveedoresModelos.Vacio(r.Texto("Notas")),
        ];
        var id = await bd.EjecutarAsync(async s =>
        {
            if (ProveedoresModelos.Id(r.Clave) is int existente)
            {
                await s.EjecutarAsync("UPDATE CargosSociedad SET IdTercero = ?, Cargo = ?, FechaNacimiento = ?, Nacionalidad = ?, PaisResidencia = ?, FechaPoder = ?, Notaria = ?, " +
                    "FechaDesde = ?, FechaHasta = ?, Activo = ?, Notas = ? WHERE IdCargoSociedad = ? AND IdSociedad = ?", [.. valores, existente, c.Trabajo.IdSociedad]);
                return existente;
            }
            await s.EjecutarAsync("INSERT INTO CargosSociedad (IdTercero, Cargo, FechaNacimiento, Nacionalidad, PaisResidencia, FechaPoder, Notaria, FechaDesde, FechaHasta, Activo, Notas, IdSociedad) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", [.. valores, c.Trabajo.IdSociedad]);
            return await s.UltimoIdAsync();
        });
        var guardado = (await ListarAsync(c)).FirstOrDefault(x => ProveedoresModelos.Id(x.Clave) == id);
        return guardado is null ? ProveedoresModelos.Fallo("CargosSociedad.NoExiste", "El cargo ya no existe.", "Actualiza la lista.") : Resultado<RegistroMantenimiento>.Correcto(guardado);
    }

    public override async Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        await bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM CargosSociedad WHERE IdCargoSociedad = ? AND IdSociedad = ?", ProveedoresModelos.Id(r.Clave), c.Trabajo.IdSociedad));
        return Resultado.Correcto();
    }
}

/// <summary>
/// Datos de los empleados para el modelo 190 (Empleados, esquema 72): año de nacimiento, situación familiar, NIF del cónyuge, discapacidad,
/// contrato o relación y movilidad geográfica. Lista los empleados de la empresa; se dan de alta como hasta ahora (rol Empleado del tercero o
/// importación de nóminas). El código postal sale de la ficha del tercero: si falta, se avisa.
/// </summary>
public sealed class ProveedorEmpleados190(IBaseDatos bd) : ProveedorBase
{
    public override string Clave => "empleados190";
    public override string Titulo => "Datos de los empleados (190)";
    public override string Categoria => ProveedoresModelos.Categoria;
    public override string Finalidad =>
        "Lo que el modelo 190 pide de cada empleado y no está en sus nóminas: año de nacimiento, situación familiar, discapacidad, contrato y movilidad geográfica. Los hijos y ascendientes van en «Familiares de los empleados (190)».";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "empleado";
    public override IReadOnlyList<string> Sinonimos => ["190", "empleado", "trabajador", "nacimiento", "situacion familiar", "discapacidad", "conyuge"];

    public static readonly IReadOnlyList<OpcionCampo> Situaciones =
    [
        new((byte)1, "1 · Monoparental con hijos que conviven solo con él"), new((byte)2, "2 · Casado con cónyuge sin rentas superiores al límite"), new((byte)3, "3 · Otras situaciones o no la comunica"),
    ];

    public static readonly IReadOnlyList<OpcionCampo> Contratos =
    [
        new((byte)1, "1 · General"), new((byte)2, "2 · Inferior al año"), new((byte)3, "3 · Relación laboral especial"), new((byte)4, "4 · Peonadas o jornales"),
    ];

    public static readonly IReadOnlyList<OpcionCampo> SubclavesAdministrador =
    [
        new("01", "01 · Consejeros y administradores"), new("02", "02 · Ver las instrucciones del 190"), new("03", "03 · Ver las instrucciones del 190"),
        new("04", "04 · Administradores de entidades con cifra de negocios inferior a 100.000 €"),
    ];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Nombre", "Empleado") { SoloLectura = true, Ancho = "l", Ayuda = "Nombre de su ficha de tercero." },
        new("Nif", "NIF") { SoloLectura = true, Ancho = "s", Ayuda = "NIF de su ficha de tercero." },
        new("CodigoPostal", "Código postal") { SoloLectura = true, Ancho = "s", Ayuda = "De su ficha de tercero: el 190 pide su provincia. Si falta, complétalo en Terceros." },
        new("AnioNacimiento", "Año de nacimiento", TipoCampo.Entero) { Ancho = "xs", Minimo = 1900, Maximo = 2100, Ayuda = "Cuatro cifras. Lo pide el 190 en la clave A." },
        new("SituacionFamiliar", "Situación familiar", TipoCampo.Lista) { Opciones = Situaciones, Ancho = "l", Ayuda = "La que comunicó el empleado (modelo 145). Vacía: 3." },
        new("NifConyuge", "NIF del cónyuge", TipoCampo.Codigo) { Longitud = 9, Ancho = "s", Ayuda = "Solo con la situación familiar 2." },
        new("Discapacidad", "Discapacidad", TipoCampo.Lista) { Opciones = ProveedoresModelos.OpcionesDiscapacidad, Ancho = "l", Ayuda = "Grado de discapacidad acreditado. Vacía: ninguna." },
        new("ContratoRelacion", "Contrato o relación", TipoCampo.Lista) { Opciones = Contratos, Ancho = "m", Ayuda = "Vacío: 1, general." },
        new("MovilidadGeografica", "Movilidad geográfica", TipoCampo.Logico) { Ayuda = "Tiene derecho este año al incremento de gastos por aceptar un puesto en otro municipio (art. 19.2.f LIRPF)." },
        new("Administrador", "Administrador", TipoCampo.Logico) { SoloLectura = true,
            Ayuda = "De su ficha de tercero (rol Empleado): los administradores van en el 190 con la clave E y no necesitan los datos personales." },
        new("SubclaveAdministrador190", "Subclave de administrador (190)", TipoCampo.Lista) { Opciones = SubclavesAdministrador, Ancho = "l",
            Ayuda = "Solo para los administradores: la subclave de la clave E que pide el 190. Elige la que usa la gestoría; sin ella no se ofrece presentar el 190." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Nombre", "Empleado"), new("Nif", "NIF", 100), new("AnioNacimiento", "Nacimiento", 80), new("Falta", "Falta", 220)];

    public override string TituloRegistro(RegistroMantenimiento r) => r.Texto("Nombre") ?? "Empleado";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) => await bd.EjecutarAsync(async s =>
        await s.ConsultarAsync(
            "SELECT e.IdEmpleado, t.Nombre, t.NIF, t.CodigoPostal, e.AnioNacimiento, e.SituacionFamiliar, e.NifConyuge, e.Discapacidad, e.ContratoRelacion, e.MovilidadGeografica, " +
            "es.Administrador, es.SubclaveAdministrador190 " +
            "FROM (EmpleadosSociedad AS es INNER JOIN Empleados AS e ON es.IdEmpleado = e.IdEmpleado) INNER JOIN Terceros AS t ON e.IdTercero = t.IdTercero " +
            "WHERE es.IdSociedad = ? ORDER BY t.Nombre",
            f =>
            {
                var cp = ProveedoresModelos.Texto(f, "CodigoPostal");
                var anio = f.Valor("AnioNacimiento") is null or DBNull ? (short?)null : Convert.ToInt16(f.Valor("AnioNacimiento"), CultureInfo.InvariantCulture);
                var falta = new List<string>();
                var administrador = f.Logico("Administrador");
                var subclave = f.Texto("SubclaveAdministrador190")?.Trim();
                if (administrador && string.IsNullOrEmpty(subclave)) falta.Add("subclave de administrador");
                if (anio is null && !administrador) falta.Add("año de nacimiento");
                if (cp.Length < 2) falta.Add("código postal (Terceros)");
                return new RegistroMantenimiento(f.Entero("IdEmpleado"), new Dictionary<string, object?>
                {
                    ["Nombre"] = ProveedoresModelos.Texto(f, "Nombre"), ["Nif"] = ProveedoresModelos.Texto(f, "NIF"), ["CodigoPostal"] = cp.Length == 0 ? "Falta: complétalo en Terceros" : cp,
                    ["AnioNacimiento"] = anio, ["SituacionFamiliar"] = Byte(f, "SituacionFamiliar"), ["NifConyuge"] = f.Texto("NifConyuge"), ["Discapacidad"] = Byte(f, "Discapacidad"),
                    ["ContratoRelacion"] = Byte(f, "ContratoRelacion"), ["MovilidadGeografica"] = f.Logico("MovilidadGeografica"),
                    ["Administrador"] = administrador, ["SubclaveAdministrador190"] = string.IsNullOrEmpty(subclave) ? null : subclave, ["Falta"] = falta.Count == 0 ? "" : "Falta " + string.Join(" y ", falta),
                });
            }, c.Trabajo.IdSociedad));

    private static byte? Byte(IFilaDatos f, string campo) => f.Valor(campo) is null or DBNull ? null : Convert.ToByte(f.Valor(campo), CultureInfo.InvariantCulture);

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var e = new List<ErrorCampo>(base.Validar(r));
        if (r.Texto("NifConyuge") is { Length: > 0 } && Convert.ToInt32(r["SituacionFamiliar"] ?? 0, CultureInfo.InvariantCulture) != 2)
            e.Add(new ErrorCampo("NifConyuge", "El NIF del cónyuge solo se indica con la situación familiar 2."));
        return e;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = ProveedoresModelos.Id(r.Clave);
        await bd.EjecutarAsync(s => s.EjecutarAsync(
            "UPDATE Empleados SET AnioNacimiento = ?, SituacionFamiliar = ?, NifConyuge = ?, Discapacidad = ?, ContratoRelacion = ?, MovilidadGeografica = ? WHERE IdEmpleado = ?",
            ProveedoresModelos.Short(r, "AnioNacimiento"), ProveedoresModelos.Byte(r, "SituacionFamiliar"), ProveedoresModelos.Vacio(r.Texto("NifConyuge"))?.ToUpperInvariant(),
            ProveedoresModelos.Byte(r, "Discapacidad"), ProveedoresModelos.Byte(r, "ContratoRelacion"), r["MovilidadGeografica"] is true, id));
        await bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE EmpleadosSociedad SET SubclaveAdministrador190 = ? WHERE IdEmpleado = ? AND IdSociedad = ?",
            ProveedoresModelos.Vacio(r.Texto("SubclaveAdministrador190")), id, c.Trabajo.IdSociedad));
        var guardado = (await ListarAsync(c)).FirstOrDefault(x => ProveedoresModelos.Id(x.Clave) == id);
        return guardado is null ? ProveedoresModelos.Fallo("Empleados.NoExiste", "El empleado ya no está en esta empresa.", "Actualiza la lista.") : Resultado<RegistroMantenimiento>.Correcto(guardado);
    }
}

/// <summary>
/// Descendientes y ascendientes de los empleados que dan derecho a mínimo familiar (FamiliaresEmpleado, esquema 72): el 190 cuenta los
/// menores de 3 años, el resto, los mayores y menores de 75 y los de cada grado de discapacidad, y cuántos se computan por entero.
/// </summary>
public sealed class ProveedorFamiliaresEmpleado(IBaseDatos bd) : ProveedorBase
{
    private IReadOnlyList<OpcionCampo> _empleados = [];

    public override string Clave => "familiaresempleado";
    public override string Titulo => "Familiares de los empleados (190)";
    public override string Categoria => ProveedoresModelos.Categoria;
    public override string Finalidad => "Hijos y otros descendientes y ascendientes de cada empleado que dan derecho a mínimo familiar: el 190 los cuenta por edad, discapacidad y si se computan por entero.";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override string NombreRegistro => "familiar";
    public override IReadOnlyList<string> Sinonimos => ["190", "hijos", "descendientes", "ascendientes", "minimo familiar", "145"];

    public static readonly IReadOnlyList<OpcionCampo> Parentescos = [new("D", "Descendiente (hijo, nieto…)"), new("A", "Ascendiente (padre, madre, abuelo…)")];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("IdEmpleado", "Empleado", TipoCampo.Lista) { Obligatorio = true, Opciones = _empleados, Ancho = "l", Ayuda = "Empleado de esta empresa del que es familiar." },
        new("Parentesco", "Parentesco", TipoCampo.Lista) { Obligatorio = true, Opciones = Parentescos, Ancho = "m",
            Ayuda = "Descendiente (hijos, nietos…) o ascendiente (padres, abuelos…) por el que el empleado tiene derecho a mínimo familiar." },
        new("Nombre", "Nombre") { Longitud = 100, Ancho = "l", Ayuda = "Solo para reconocerlo en la lista: no se envía a la AEAT." },
        new("AnioNacimiento", "Año de nacimiento", TipoCampo.Entero) { Obligatorio = true, Minimo = 1900, Maximo = 2100, Ancho = "xs",
            Ayuda = "Cuatro cifras. Con él se cuentan los menores de 3 años y los ascendientes de 75 o más al 31/12 del ejercicio." },
        new("AnioAdopcion", "Año de adopción o acogimiento", TipoCampo.Entero) { Minimo = 1900, Maximo = 2100, Ancho = "xs",
            Ayuda = "Solo de los descendientes adoptados o acogidos: cuentan como menores de 3 años ese año y los dos siguientes." },
        new("Discapacidad", "Discapacidad", TipoCampo.Lista) { Opciones = ProveedoresModelos.OpcionesDiscapacidad, Ancho = "l", Ayuda = "Vacía: ninguna." },
        new("PorEntero", "Se computa por entero", TipoCampo.Logico) { Ayuda = "Desmárcalo si el mínimo se reparte con otra persona (por ejemplo, el otro progenitor): entonces cuenta por mitad." },
        new("Activo", "Cuenta este año", TipoCampo.Logico) { Ayuda = "Desmárcalo cuando deje de dar derecho al mínimo (por ejemplo, un hijo con rentas propias)." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Empleado", "Empleado"), new("ParentescoTexto", "Parentesco", 110), new("Nombre", "Nombre"), new("AnioNacimiento", "Nacimiento", 80)];

    public override string TituloRegistro(RegistroMantenimiento r) => r.EsNuevo ? "Nuevo familiar" : $"{r.Texto("Empleado")} · {r.Texto("Nombre") ?? r.Texto("ParentescoTexto")}";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c)
    {
        var (empleados, filas) = await bd.EjecutarAsync(async s =>
        {
            var e = await s.ConsultarAsync(
                "SELECT e.IdEmpleado, t.Nombre FROM (EmpleadosSociedad AS es INNER JOIN Empleados AS e ON es.IdEmpleado = e.IdEmpleado) INNER JOIN Terceros AS t ON e.IdTercero = t.IdTercero " +
                "WHERE es.IdSociedad = ? ORDER BY t.Nombre", f => (Id: f.Entero("IdEmpleado"), Texto: ProveedoresModelos.Texto(f, "Nombre")), c.Trabajo.IdSociedad);
            var ids = e.Select(x => x.Id).ToHashSet();
            var r = await s.ConsultarAsync(
                "SELECT IdFamiliarEmpleado, IdEmpleado, Parentesco, Nombre, AnioNacimiento, AnioAdopcion, Discapacidad, PorEntero, Activo FROM FamiliaresEmpleado ORDER BY IdEmpleado, AnioNacimiento",
                f => new RegistroMantenimiento(f.Entero("IdFamiliarEmpleado"), new Dictionary<string, object?>
                {
                    ["IdEmpleado"] = f.Entero("IdEmpleado"), ["Parentesco"] = ProveedoresModelos.Texto(f, "Parentesco"), ["Nombre"] = f.Texto("Nombre"),
                    ["AnioNacimiento"] = Convert.ToInt16(f.Valor("AnioNacimiento"), CultureInfo.InvariantCulture),
                    ["AnioAdopcion"] = f.Valor("AnioAdopcion") is null or DBNull ? null : Convert.ToInt16(f.Valor("AnioAdopcion"), CultureInfo.InvariantCulture),
                    ["Discapacidad"] = f.Valor("Discapacidad") is null or DBNull ? null : Convert.ToByte(f.Valor("Discapacidad"), CultureInfo.InvariantCulture),
                    ["PorEntero"] = f.Logico("PorEntero"), ["Activo"] = f.Logico("Activo"),
                }));
            return (e, r.Where(x => x["IdEmpleado"] is int id && ids.Contains(id)).ToList());
        });
        _empleados = [.. empleados.Select(x => new OpcionCampo(x.Id, x.Texto))];
        var textos = empleados.ToDictionary(x => x.Id, x => x.Texto);
        foreach (var r in filas)
        {
            r["Empleado"] = textos.GetValueOrDefault((int)r["IdEmpleado"]!, "");
            r["ParentescoTexto"] = r.Texto("Parentesco") == "A" ? "Ascendiente" : "Descendiente";
        }
        return filas;
    }

    public override async Task<RegistroMantenimiento> NuevoAsync(ContextoMantenimiento contexto)
    {
        if (_empleados.Count == 0) await ListarAsync(contexto);
        var r = await base.NuevoAsync(contexto);
        r["Parentesco"] = "D";
        r["PorEntero"] = true;
        r["Activo"] = true;
        return r;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        if (r["IdEmpleado"] is null || ProveedoresModelos.Short(r, "AnioNacimiento") is not short anio)
            return ProveedoresModelos.Fallo("FamiliaresEmpleado.Incompleto", "Falta el empleado o el año de nacimiento.", "Complétalos y vuelve a guardar.");
        object?[] valores =
        [
            Convert.ToInt32(r["IdEmpleado"], CultureInfo.InvariantCulture), r.Texto("Parentesco") == "A" ? "A" : "D", ProveedoresModelos.Vacio(r.Texto("Nombre")), anio,
            ProveedoresModelos.Short(r, "AnioAdopcion"), ProveedoresModelos.Byte(r, "Discapacidad"), r["PorEntero"] is true, r["Activo"] is true,
        ];
        var id = await bd.EjecutarAsync(async s =>
        {
            if (ProveedoresModelos.Id(r.Clave) is int existente)
            {
                await s.EjecutarAsync("UPDATE FamiliaresEmpleado SET IdEmpleado = ?, Parentesco = ?, Nombre = ?, AnioNacimiento = ?, AnioAdopcion = ?, Discapacidad = ?, PorEntero = ?, Activo = ? " +
                    "WHERE IdFamiliarEmpleado = ?", [.. valores, existente]);
                return existente;
            }
            await s.EjecutarAsync("INSERT INTO FamiliaresEmpleado (IdEmpleado, Parentesco, Nombre, AnioNacimiento, AnioAdopcion, Discapacidad, PorEntero, Activo) VALUES (?, ?, ?, ?, ?, ?, ?, ?)", valores);
            return await s.UltimoIdAsync();
        });
        var guardado = (await ListarAsync(c)).FirstOrDefault(x => ProveedoresModelos.Id(x.Clave) == id);
        return guardado is null ? ProveedoresModelos.Fallo("FamiliaresEmpleado.NoExiste", "El familiar ya no existe o su empleado no es de esta empresa.", "Actualiza la lista.")
            : Resultado<RegistroMantenimiento>.Correcto(guardado);
    }

    public override async Task<Resultado> EliminarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        await bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM FamiliaresEmpleado WHERE IdFamiliarEmpleado = ?", ProveedoresModelos.Id(r.Clave)));
        return Resultado.Correcto();
    }
}
