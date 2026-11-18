using System.Text.RegularExpressions;
using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los modelos tributarios (docs/contabilidad/modelos-tributarios).</summary>
public static class MigracionesModelosTributarios
{
    /// <summary>
    /// Datos que faltaban para presentar los modelos (esquema 72; contabilidad/modelos-tributarios 2026-11-18, tanda 6 del hilo «Completar
    /// los modelos tributarios y su pantalla»). Solo añade: columnas opcionales y dos tablas; lo que ya había sigue igual.
    /// <list type="bullet">
    /// <item>Sociedad: el CNAE principal (Sociedades.Cnae; el teléfono ya estaba).</item>
    /// <item>Actividades: clave y epígrafe del IAE, CNAE y orden en el 390 (Actividades.Clave390, EpigrafeIae, Cnae, Orden390).</item>
    /// <item>Ejercicio, para el 200: plantilla media fija y no fija y opción del 0,7 % para fines sociales.</item>
    /// <item>CargosSociedad: administradores, titulares reales, secretario y representantes legales de cada sociedad, con referencia al tercero.</item>
    /// <item>Ajustes del IS: la línea del 200 de cada ajuste (ImpuestosSociedadesAjustes.Linea200), con el valor inicial de la regla anterior
    /// (por la descripción).</item>
    /// <item>Empleados, para el 190: año de nacimiento, situación familiar, NIF del cónyuge, discapacidad, contrato o relación y movilidad
    /// geográfica; FamiliaresEmpleado: descendientes y ascendientes.</item>
    /// <item>Inmuebles, para el 180 y el 347: situación y dirección estructurada (como la piden los diseños) en ActivosInmuebles; la
    /// referencia catastral ya estaba.</item>
    /// </list>
    /// </summary>
    public static Migracion DatosModelos { get; } = new(
        "20261118_1300_DATOS_MODELOS_TRIBUTARIOS", 261118001,
        "Datos de los modelos tributarios: CNAE de la sociedad, actividades del 390, plantilla y opción del 0,7 % del 200, cargos de la sociedad, línea del 200 de los ajustes del IS, " +
        "datos personales de los empleados y sus familiares para el 190 y situación y dirección de los inmuebles para el 180 y el 347.",
        "contabilidad/modelos-tributarios",
        new AnadirColumna("Sociedades", "Cnae", "TEXT(4)"),
        new AnadirColumna("Actividades", "Clave390", "TEXT(3)"),
        new AnadirColumna("Actividades", "EpigrafeIae", "TEXT(10)"),
        new AnadirColumna("Actividades", "Cnae", "TEXT(4)"),
        new AnadirColumna("Actividades", "Orden390", "SHORT"),
        new AnadirColumna("SociedadesEjercicios", "PersonalFijo", "DOUBLE"),
        new AnadirColumna("SociedadesEjercicios", "PersonalNoFijo", "DOUBLE"),
        new AnadirColumna("SociedadesEjercicios", "OpcionFinesSociales", "YESNO"),
        new CrearTabla("CargosSociedad",
            "CREATE TABLE CargosSociedad (IdCargoSociedad COUNTER CONSTRAINT PK_CargosSociedad PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_CargosSociedad_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "IdTercero LONG NOT NULL CONSTRAINT FK_CargosSociedad_Tercero REFERENCES Terceros (IdTercero), " +
            "Cargo TEXT(20) NOT NULL, FechaNacimiento DATETIME, Nacionalidad TEXT(2), PaisResidencia TEXT(2), FechaPoder DATETIME, Notaria TEXT(12), " +
            "FechaDesde DATETIME, FechaHasta DATETIME, Activo YESNO NOT NULL, Notas TEXT(255))"),
        new CrearIndice("CargosSociedad", "IX_CargosSociedad_Sociedad", "CREATE INDEX IX_CargosSociedad_Sociedad ON CargosSociedad (IdSociedad, Cargo)"),
        new AnadirColumna("ImpuestosSociedadesAjustes", "Linea200", "TEXT(30)"),
        new PasoPersonalizado(
            "Poner la línea del 200 de los ajustes del IS con la regla anterior (por la descripción)",
            "UPDATE ImpuestosSociedadesAjustes SET Linea200 = 'EXENCION_ART21' si la descripción dice «art. 21» o «exención»; si no, 'OTRAS'",
            async (s, e) => e.ExisteColumna("ImpuestosSociedadesAjustes", "Linea200") &&
                Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM ImpuestosSociedadesAjustes WHERE Linea200 IS NULL")) == 0,
            async (s, _) => await RellenarLinea200Async(s)),
        new AnadirColumna("Empleados", "AnioNacimiento", "SHORT"),
        new AnadirColumna("Empleados", "SituacionFamiliar", "BYTE"),
        new AnadirColumna("Empleados", "NifConyuge", "TEXT(9)"),
        new AnadirColumna("Empleados", "Discapacidad", "BYTE"),
        new AnadirColumna("Empleados", "ContratoRelacion", "BYTE"),
        new AnadirColumna("Empleados", "MovilidadGeografica", "YESNO"),
        new AnadirColumna("EmpleadosSociedad", "SubclaveAdministrador190", "TEXT(2)"),
        new CrearTabla("FamiliaresEmpleado",
            "CREATE TABLE FamiliaresEmpleado (IdFamiliarEmpleado COUNTER CONSTRAINT PK_FamiliaresEmpleado PRIMARY KEY, " +
            "IdEmpleado LONG NOT NULL CONSTRAINT FK_FamiliaresEmpleado_Empleado REFERENCES Empleados (IdEmpleado), " +
            "Parentesco TEXT(1) NOT NULL, Nombre TEXT(100), AnioNacimiento SHORT NOT NULL, AnioAdopcion SHORT, Discapacidad BYTE, " +
            "PorEntero YESNO NOT NULL, Activo YESNO NOT NULL)"),
        new CrearIndice("FamiliaresEmpleado", "IX_FamiliaresEmpleado_Empleado", "CREATE INDEX IX_FamiliaresEmpleado_Empleado ON FamiliaresEmpleado (IdEmpleado)"),
        new AnadirColumna("ActivosInmuebles", "SituacionInmueble", "BYTE"),
        new AnadirColumna("ActivosInmuebles", "TipoVia", "TEXT(5)"),
        new AnadirColumna("ActivosInmuebles", "NombreVia", "TEXT(50)"),
        new AnadirColumna("ActivosInmuebles", "TipoNumeracion", "TEXT(3)"),
        new AnadirColumna("ActivosInmuebles", "NumeroCasa", "TEXT(5)"),
        new AnadirColumna("ActivosInmuebles", "CalificadorNumero", "TEXT(3)"),
        new AnadirColumna("ActivosInmuebles", "Bloque", "TEXT(3)"),
        new AnadirColumna("ActivosInmuebles", "Portal", "TEXT(3)"),
        new AnadirColumna("ActivosInmuebles", "Escalera", "TEXT(3)"),
        new AnadirColumna("ActivosInmuebles", "Planta", "TEXT(3)"),
        new AnadirColumna("ActivosInmuebles", "Puerta", "TEXT(3)"),
        new AnadirColumna("ActivosInmuebles", "Complemento", "TEXT(40)"),
        new AnadirColumna("ActivosInmuebles", "Localidad", "TEXT(30)"),
        new AnadirColumna("ActivosInmuebles", "Municipio", "TEXT(30)"),
        new AnadirColumna("ActivosInmuebles", "CodigoMunicipio", "TEXT(5)"),
        new AnadirColumna("ActivosInmuebles", "CodigoProvincia", "TEXT(2)"),
        new AnadirColumna("ActivosInmuebles", "CodigoPostal", "TEXT(5)"))
    {
        Descripciones =
        [
            new("Sociedades", "Cnae", "Código CNAE-2025 (cuatro cifras) de la actividad principal: lo piden el 200, el 202 y el 390."),
            new("Actividades", "Clave390", "Clave de la actividad en los datos estadísticos del 390 (A01 empresarial, A03 profesional…); vacía: la actividad no va al 390."),
            new("Actividades", "EpigrafeIae", "Epígrafe del IAE de la actividad para el 390 (por ejemplo 8612)."),
            new("Actividades", "Cnae", "CNAE de la actividad (cuatro cifras); el 390 lo usa en la prorrata. Vacío: el de la sociedad."),
            new("Actividades", "Orden390", "Orden de la actividad en el 390: la de menor orden es la principal. Varias actividades con la misma clave y epígrafe van una sola vez."),
            new("SociedadesEjercicios", "PersonalFijo", "Plantilla media de personal fijo del ejercicio (casilla 00041 del 200)."),
            new("SociedadesEjercicios", "PersonalNoFijo", "Plantilla media de personal no fijo del ejercicio (casilla 00042 del 200)."),
            new("SociedadesEjercicios", "OpcionFinesSociales", "Opción del 0,7 % de la cuota íntegra para fines sociales en el 200 (clave 00073)."),
            new("CargosSociedad", null, "Personas con un cargo en la sociedad que piden los modelos: administradores, titulares reales, secretario del consejo y representantes legales (contabilidad/modelos-tributarios)."),
            new("CargosSociedad", "IdCargoSociedad", "Identificador del cargo."),
            new("CargosSociedad", "IdSociedad", "Sociedad."),
            new("CargosSociedad", "IdTercero", "Persona o entidad que tiene el cargo (su NIF, nombre y domicilio salen de su ficha de tercero)."),
            new("CargosSociedad", "Cargo", "ADMINISTRADOR, TITULAR_REAL, SECRETARIO o REPRESENTANTE."),
            new("CargosSociedad", "FechaNacimiento", "Fecha de nacimiento del titular real (página 2 bis del 200)."),
            new("CargosSociedad", "Nacionalidad", "Nacionalidad del titular real, código ISO de dos letras (vacía: ES)."),
            new("CargosSociedad", "PaisResidencia", "País de residencia del titular real, código ISO de dos letras (vacío: ES)."),
            new("CargosSociedad", "FechaPoder", "Fecha del poder del representante legal (200 y 390)."),
            new("CargosSociedad", "Notaria", "Notaría (u «otros») del poder del representante legal."),
            new("CargosSociedad", "FechaDesde", "Desde cuándo tiene el cargo (vacía: siempre)."),
            new("CargosSociedad", "FechaHasta", "Hasta cuándo tiene el cargo (vacía: sigue)."),
            new("CargosSociedad", "Activo", "Si el cargo está vigente."),
            new("CargosSociedad", "Notas", "Notas libres."),
            new("ImpuestosSociedadesAjustes", "Linea200", "Línea del detalle de correcciones del 200 del ajuste: EXENCION_ART21 (transmisión de valores de entidades residentes) u OTRAS."),
            new("Empleados", "AnioNacimiento", "Año de nacimiento del empleado (190)."),
            new("Empleados", "SituacionFamiliar", "Situación familiar del 190: 1 monoparental con descendientes, 2 casado con cónyuge sin rentas superiores al límite, 3 otras."),
            new("Empleados", "NifConyuge", "NIF del cónyuge, solo con la situación familiar 2 (190)."),
            new("Empleados", "Discapacidad", "Discapacidad del 190: 0 ninguna o menor del 33 %, 1 del 33 al 65 %, 2 del 33 al 65 % con ayuda de terceros o movilidad reducida, 3 del 65 % o más."),
            new("Empleados", "ContratoRelacion", "Contrato o relación del 190: 1 general, 2 inferior al año, 3 relación laboral especial, 4 peonadas (vacío: 1)."),
            new("Empleados", "MovilidadGeografica", "Tiene derecho en el ejercicio al incremento de gastos por movilidad geográfica (190)."),
            new("EmpleadosSociedad", "SubclaveAdministrador190", "Subclave de la clave E del 190 (01 a 04) del empleado marcado como administrador en esta sociedad."),
            new("FamiliaresEmpleado", null, "Descendientes y ascendientes de un empleado que dan derecho a mínimo familiar (190, contabilidad/modelos-tributarios/modelo-190)."),
            new("FamiliaresEmpleado", "IdFamiliarEmpleado", "Identificador del familiar."),
            new("FamiliaresEmpleado", "IdEmpleado", "Empleado."),
            new("FamiliaresEmpleado", "Parentesco", "D descendiente, A ascendiente."),
            new("FamiliaresEmpleado", "Nombre", "Nombre del familiar (solo para reconocerlo; no va a la AEAT)."),
            new("FamiliaresEmpleado", "AnioNacimiento", "Año de nacimiento."),
            new("FamiliaresEmpleado", "AnioAdopcion", "Año de la adopción o el acogimiento de un descendiente (cuenta como menor de 3 años en ese año y los dos siguientes)."),
            new("FamiliaresEmpleado", "Discapacidad", "Discapacidad: 0 o vacía ninguna, 1 del 33 al 65 %, 2 del 33 al 65 % con movilidad reducida, 3 del 65 % o más."),
            new("FamiliaresEmpleado", "PorEntero", "Se computa por entero a efectos de la retención (si no, por mitad, al compartirlo con otro)."),
            new("FamiliaresEmpleado", "Activo", "Si cuenta en el ejercicio."),
            new("ActivosInmuebles", "SituacionInmueble", "Situación del inmueble como la pide el 180: 1 con referencia catastral fuera del País Vasco y Navarra, 2 País Vasco, 3 Navarra, 4 sin referencia catastral."),
            new("ActivosInmuebles", "TipoVia", "Tipo de vía normalizado del INE (CALLE, AVDA, PZA…)."),
            new("ActivosInmuebles", "NombreVia", "Nombre de la vía pública."),
            new("ActivosInmuebles", "TipoNumeracion", "Tipo de numeración: NUM, KM. o S/N."),
            new("ActivosInmuebles", "NumeroCasa", "Número de casa o punto kilométrico."),
            new("ActivosInmuebles", "CalificadorNumero", "Calificador del número: BIS, DUP…"),
            new("ActivosInmuebles", "Bloque", "Bloque."),
            new("ActivosInmuebles", "Portal", "Portal."),
            new("ActivosInmuebles", "Escalera", "Escalera."),
            new("ActivosInmuebles", "Planta", "Planta o piso."),
            new("ActivosInmuebles", "Puerta", "Puerta."),
            new("ActivosInmuebles", "Complemento", "Datos complementarios de la dirección (urbanización, centro comercial…)."),
            new("ActivosInmuebles", "Localidad", "Localidad o población, si es distinta del municipio."),
            new("ActivosInmuebles", "Municipio", "Municipio."),
            new("ActivosInmuebles", "CodigoMunicipio", "Código del municipio del INE (cinco cifras)."),
            new("ActivosInmuebles", "CodigoProvincia", "Código de la provincia (dos cifras)."),
            new("ActivosInmuebles", "CodigoPostal", "Código postal."),
        ],
    };

    /// <summary>
    /// Pantalla Contabilidad › Modelos tributarios (esquema 73; contabilidad/modelos-tributarios 2026-11-18, tanda 7):
    /// <list type="bullet">
    /// <item>PresentacionesAeat.Origen: PROGRAMA (presentada desde el programa; vacío en las de antes) o AEAT (traída por «Consultar a la AEAT»:
    /// presentada fuera del programa, por ejemplo por la gestoría, sin importe ni fichero).</item>
    /// <item>ModelosSociedad: qué modelos salen en la pantalla, en los plazos y en los avisos de Inicio de cada sociedad («Modelos de la
    /// sociedad…»). Sin filas, los que tienen datos o presentaciones.</item>
    /// <item>Sociedades.UltimaConsultaModelosAeat: cuándo se consultó por última vez a la AEAT lo presentado (la pantalla lo hace sola una vez al día).</item>
    /// </list>
    /// </summary>
    public static Migracion PantallaModelos { get; } = new(
        "20261118_1500_PANTALLA_MODELOS_TRIBUTARIOS", 261118002,
        "Pantalla de modelos tributarios: origen de cada presentación (programa o consulta a la AEAT), modelos de cada sociedad y fecha de la última consulta a la AEAT.",
        "contabilidad/modelos-tributarios",
        new AnadirColumna("PresentacionesAeat", "Origen", "TEXT(10)"),
        new AnadirColumna("Sociedades", "UltimaConsultaModelosAeat", "DATETIME"),
        new CrearTabla("ModelosSociedad",
            "CREATE TABLE ModelosSociedad (IdModeloSociedad COUNTER CONSTRAINT PK_ModelosSociedad PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, " +
            "Modelo TEXT(3) NOT NULL, Visible YESNO NOT NULL)"),
        new CrearIndice("ModelosSociedad", "UX_ModelosSociedad_Modelo", "CREATE UNIQUE INDEX UX_ModelosSociedad_Modelo ON ModelosSociedad (IdSociedad, Modelo)"))
    {
        Descripciones =
        [
            new("PresentacionesAeat", "Origen", "PROGRAMA: presentada desde el programa (vacío en las anteriores al esquema 73); AEAT: presentada fuera del programa y traída por «Consultar a la AEAT» (sin importe ni fichero)."),
            new("Sociedades", "UltimaConsultaModelosAeat", "Fecha y hora de la última consulta a la AEAT de lo presentado por la sociedad (pantalla Modelos tributarios)."),
            new("ModelosSociedad", null, "Modelos que presenta cada sociedad: los que salen en Contabilidad › Modelos tributarios, en sus plazos y en los avisos de Inicio (contabilidad/modelos-tributarios)."),
            new("ModelosSociedad", "IdModeloSociedad", "Identificador."),
            new("ModelosSociedad", "IdSociedad", "Sociedad."),
            new("ModelosSociedad", "Modelo", "Número del modelo (303, 111, 190…)."),
            new("ModelosSociedad", "Visible", "Si el modelo sale en la pantalla, los plazos y los avisos de esta sociedad."),
        ],
    };

    /// <summary>Clave de la línea del 200 de un ajuste por su descripción (la regla que usaba el 200 antes del esquema 72).</summary>
    public static string Linea200(string? descripcion) =>
        Regex.IsMatch(descripcion ?? "", @"\bart(\.|[íi]culo)?\s*21\b|exenci[óo]n", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ? "EXENCION_ART21" : "OTRAS";

    private static async Task RellenarLinea200Async(ISesionDatos s)
    {
        var filas = await s.ConsultarAsync("SELECT IdImpuestoSociedadesAjuste, Descripcion FROM ImpuestosSociedadesAjustes WHERE Linea200 IS NULL",
            f => (Id: f.Entero("IdImpuestoSociedadesAjuste"), Descripcion: f.Texto("Descripcion")));
        foreach (var (id, descripcion) in filas)
            await s.EjecutarAsync("UPDATE ImpuestosSociedadesAjustes SET Linea200 = ? WHERE IdImpuestoSociedadesAjuste = ?", Linea200(descripcion), id);
    }
}
