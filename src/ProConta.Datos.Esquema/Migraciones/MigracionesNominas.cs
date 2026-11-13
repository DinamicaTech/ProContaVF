namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por nóminas (docs/nominas.md) y por el rol Empleado de terceros/roles.</summary>
public static class MigracionesNominas
{
    /// <summary>
    /// Importación de nóminas (nominas 2026-11-13): el rol Empleado de los terceros con su código de trabajador y su cuenta 465 por
    /// sociedad (terceros/roles, derivado de nominas), los alias de las cabeceras del Excel por sociedad, cada fichero importado
    /// (idempotente por sociedad-ejercicio y SHA-256) y una fila por trabajador con los importes que usa la plantilla «Nómina».
    /// </summary>
    public static Migracion Importacion { get; } = new(
        "20261113_1100_NOMINAS_IMPORTACION", 261113010,
        "Nóminas: rol Empleado por sociedad, alias de cabeceras, importaciones de ficheros de la gestoría y nóminas por trabajador.",
        "nominas",
        new CrearTabla("Empleados",
            "CREATE TABLE Empleados (IdEmpleado COUNTER CONSTRAINT PK_Empleados PRIMARY KEY, " +
            "IdTercero LONG NOT NULL CONSTRAINT FK_Empleados_Tercero REFERENCES Terceros (IdTercero), " +
            "Activo YESNO NOT NULL, FechaAlta DATETIME NOT NULL, Notas LONGTEXT)"),
        new CrearIndice("Empleados", "UX_Empleados_Tercero", "CREATE UNIQUE INDEX UX_Empleados_Tercero ON Empleados (IdTercero)"),
        new CrearTabla("EmpleadosSociedad",
            "CREATE TABLE EmpleadosSociedad (IdEmpleadoSociedad COUNTER CONSTRAINT PK_EmpleadosSociedad PRIMARY KEY, " +
            "IdEmpleado LONG NOT NULL CONSTRAINT FK_EmpleadosSociedad_Empleado REFERENCES Empleados (IdEmpleado), " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_EmpleadosSociedad_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "CodigoTrabajador TEXT(30), " +
            "IdIdentidadContable LONG CONSTRAINT FK_EmpleadosSociedad_Identidad REFERENCES IdentidadesContables (IdIdentidadContable), " +
            "Activo YESNO NOT NULL)"),
        new CrearIndice("EmpleadosSociedad", "UX_EmpleadosSociedad_EmpleadoSociedad",
            "CREATE UNIQUE INDEX UX_EmpleadosSociedad_EmpleadoSociedad ON EmpleadosSociedad (IdEmpleado, IdSociedad)"),
        new CrearIndice("EmpleadosSociedad", "IX_EmpleadosSociedad_Codigo",
            "CREATE INDEX IX_EmpleadosSociedad_Codigo ON EmpleadosSociedad (IdSociedad, CodigoTrabajador)"),
        new CrearTabla("AliasCamposNomina",
            "CREATE TABLE AliasCamposNomina (IdAliasCampoNomina COUNTER CONSTRAINT PK_AliasCamposNomina PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_AliasCamposNomina_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Cabecera TEXT(100) NOT NULL, Campo TEXT(50))"),
        new CrearIndice("AliasCamposNomina", "UX_AliasCamposNomina_SociedadCabecera",
            "CREATE UNIQUE INDEX UX_AliasCamposNomina_SociedadCabecera ON AliasCamposNomina (IdSociedad, Cabecera)"),
        new CrearTabla("ImportacionesNomina",
            "CREATE TABLE ImportacionesNomina (IdImportacionNomina COUNTER CONSTRAINT PK_ImportacionesNomina PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_ImportacionesNomina_Ejercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "NombreFichero TEXT(255) NOT NULL, Huella TEXT(64) NOT NULL, Origen TEXT(20) NOT NULL, " +
            "IdAdjuntoRecibido LONG CONSTRAINT FK_ImportacionesNomina_Adjunto REFERENCES AdjuntosRecibidosCorreo (IdAdjuntoRecibido), " +
            "CodigoEmpresaGestoria TEXT(20), NifEmpresa TEXT(20), TextoPaga TEXT(100), TipoPaga TEXT(10) NOT NULL, " +
            "PeriodoDesde DATETIME, PeriodoHasta DATETIME, FechaNomina DATETIME NOT NULL, Estado TEXT(20) NOT NULL, " +
            "IdImportacionSustituida LONG CONSTRAINT FK_ImportacionesNomina_Sustituida REFERENCES ImportacionesNomina (IdImportacionNomina), " +
            "Incidencias LONGTEXT, FechaHoraImportacion DATETIME NOT NULL, Operador TEXT(100), " +
            "FechaHoraEstado DATETIME, OperadorEstado TEXT(100), MotivoEstado LONGTEXT)"),
        new CrearIndice("ImportacionesNomina", "UX_ImportacionesNomina_Huella",
            "CREATE UNIQUE INDEX UX_ImportacionesNomina_Huella ON ImportacionesNomina (IdSociedadEjercicio, Huella)"),
        new CrearIndice("ImportacionesNomina", "IX_ImportacionesNomina_Periodo",
            "CREATE INDEX IX_ImportacionesNomina_Periodo ON ImportacionesNomina (IdSociedadEjercicio, PeriodoDesde, TipoPaga)"),
        new CrearTabla("Nominas",
            "CREATE TABLE Nominas (IdNomina COUNTER CONSTRAINT PK_Nominas PRIMARY KEY, " +
            "IdImportacionNomina LONG NOT NULL CONSTRAINT FK_Nominas_Importacion REFERENCES ImportacionesNomina (IdImportacionNomina), " +
            "Ordinal LONG NOT NULL, FilaOrigen LONG, " +
            "IdEmpleado LONG CONSTRAINT FK_Nominas_Empleado REFERENCES Empleados (IdEmpleado), " +
            "CodigoTrabajador TEXT(30), NombreOrigen TEXT(150) NOT NULL, NifOrigen TEXT(20), TipoPaga TEXT(10) NOT NULL, FechaNomina DATETIME NOT NULL, " +
            "Bruto CURRENCY, DtoReEsp CURRENCY, InRetEsp CURRENCY, SSEmp CURRENCY, TC1 CURRENCY, Autonomo CURRENCY, IRPF111 CURRENCY, Neto CURRENCY, AnticOtro CURRENCY, RetEmbarg CURRENCY, FaltaPrea CURRENCY, CostEmp CURRENCY, CostPPExt CURRENCY, DtoVac CURRENCY, DifExtra CURRENCY, BFormCon CURRENCY, HEFestiv CURRENCY, RegBases CURRENCY, EnfermedadAccidente CURRENCY, CompensBonif CURRENCY, BonifFundae CURRENCY, TotalCosteSS CURRENCY, TotalFLC_FPHIB CURRENCY, ProrrataPagasExtra CURRENCY, CuotasObreras CURRENCY, " +
            "IdAsiento LONG CONSTRAINT FK_Nominas_Asiento REFERENCES Asientos (IdAsiento))"),
        new CrearIndice("Nominas", "UX_Nominas_ImportacionOrdinal",
            "CREATE UNIQUE INDEX UX_Nominas_ImportacionOrdinal ON Nominas (IdImportacionNomina, Ordinal)"),
        new CrearIndice("Nominas", "IX_Nominas_Empleado", "CREATE INDEX IX_Nominas_Empleado ON Nominas (IdEmpleado)"),
        new CrearIndice("Nominas", "IX_Nominas_Asiento", "CREATE INDEX IX_Nominas_Asiento ON Nominas (IdAsiento)"))
    {
        Descripciones = Descripciones,
    };

    /// <summary>Descripción de cada tabla y campo (propiedad Description de Access; la recoge el diccionario de datos).</summary>
    internal static IReadOnlyList<DescripcionEsquema> Descripciones =>
    [
        new("Empleados", null, "Rol Empleado de un tercero (terceros/roles): la persona a la que la empresa paga nóminas."),
        new("Empleados", "IdEmpleado", "Identificador del rol Empleado."),
        new("Empleados", "IdTercero", "Tercero que tiene el rol (uno como mucho por tercero)."),
        new("Empleados", "Activo", "Si el rol está activo; al quitarlo la fila se conserva."),
        new("Empleados", "FechaAlta", "Día en que se dio el rol."),
        new("Empleados", "Notas", "Notas del rol."),
        new("EmpleadosSociedad", null, "Datos del empleado en cada sociedad: su código en la gestoría y su cuenta 465 (terceros/roles)."),
        new("EmpleadosSociedad", "IdEmpleadoSociedad", "Identificador de la fila."),
        new("EmpleadosSociedad", "IdEmpleado", "Empleado."),
        new("EmpleadosSociedad", "IdSociedad", "Sociedad en la que trabaja."),
        new("EmpleadosSociedad", "CodigoTrabajador", "Código del trabajador en la gestoría (columna sin cabecera del Excel); con él se reconoce al importar."),
        new("EmpleadosSociedad", "IdIdentidadContable", "Cuenta 465 (remuneraciones pendientes de pago) del empleado; en cada ejercicio se usa la cuenta de esta identidad."),
        new("EmpleadosSociedad", "Activo", "Si sigue trabajando en la sociedad."),
        new("AliasCamposNomina", null, "Qué campo de la nómina es cada cabecera del Excel de la gestoría, por sociedad (se aprende la primera vez)."),
        new("AliasCamposNomina", "IdAliasCampoNomina", "Identificador del alias."),
        new("AliasCamposNomina", "IdSociedad", "Sociedad del alias."),
        new("AliasCamposNomina", "Cabecera", "Cabecera del Excel normalizada (mayúsculas, sin acentos); «Columna N» si está vacía."),
        new("AliasCamposNomina", "Campo", "Campo de Nominas al que corresponde; vacío = la columna se ignora."),
        new("ImportacionesNomina", null, "Cada fichero de nóminas importado (resumen de la gestoría), con su periodo, tipo de paga y estado."),
        new("ImportacionesNomina", "IdImportacionNomina", "Identificador de la importación."),
        new("ImportacionesNomina", "IdSociedadEjercicio", "Sociedad y ejercicio de las nóminas."),
        new("ImportacionesNomina", "NombreFichero", "Nombre del fichero Excel."),
        new("ImportacionesNomina", "Huella", "SHA-256 del contenido: el mismo fichero no se importa dos veces."),
        new("ImportacionesNomina", "Origen", "FICHERO (desde la pantalla), CORREO (adjunto recibido) o MIGRACION (histórico de ProConta antiguo)."),
        new("ImportacionesNomina", "IdAdjuntoRecibido", "Adjunto del correo del que viene, si llegó por correo."),
        new("ImportacionesNomina", "CodigoEmpresaGestoria", "Código de la empresa en la gestoría («Empresa: 1804-…»)."),
        new("ImportacionesNomina", "NifEmpresa", "NIF de la empresa que trae el fichero; tiene que ser el de la sociedad."),
        new("ImportacionesNomina", "TextoPaga", "Texto de la paga en el fichero («PAGA MENSUAL + FINIQUITO», «PAGA EXTRA»…)."),
        new("ImportacionesNomina", "TipoPaga", "MENSUAL o EXTRA."),
        new("ImportacionesNomina", "PeriodoDesde", "Primer día del periodo («DEL …»)."),
        new("ImportacionesNomina", "PeriodoHasta", "Último día del periodo («AL …»)."),
        new("ImportacionesNomina", "FechaNomina", "Fecha de la nómina: la fecha de cobro si la trae, si no el último día del periodo."),
        new("ImportacionesNomina", "Estado", "PENDIENTE (lista para contabilizar), INCIDENCIAS (algo por resolver), CONTABILIZADA, REEMPLAZADA (otra versión la sustituye) o DESHECHA."),
        new("ImportacionesNomina", "IdImportacionSustituida", "Versión anterior del mismo periodo y paga a la que sustituye."),
        new("ImportacionesNomina", "Incidencias", "Lo que falta por resolver o las advertencias de la lectura, una por línea."),
        new("ImportacionesNomina", "FechaHoraImportacion", "Cuándo se importó."),
        new("ImportacionesNomina", "Operador", "Quién la importó (o el proceso de correo)."),
        new("ImportacionesNomina", "FechaHoraEstado", "Cuándo cambió de estado por última vez."),
        new("ImportacionesNomina", "OperadorEstado", "Quién cambió el estado."),
        new("ImportacionesNomina", "MotivoEstado", "Motivo del último cambio de estado (deshacer, reemplazar)."),
        new("Nominas", null, "Una nómina: la fila de un trabajador en un fichero importado, con los importes que usa la plantilla «Nómina»."),
        new("Nominas", "IdNomina", "Identificador de la nómina."),
        new("Nominas", "IdImportacionNomina", "Importación de la que viene."),
        new("Nominas", "Ordinal", "Orden de la fila dentro del fichero (1, 2…)."),
        new("Nominas", "FilaOrigen", "Fila del Excel."),
        new("Nominas", "IdEmpleado", "Empleado reconocido; vacío mientras el trabajador está sin resolver."),
        new("Nominas", "CodigoTrabajador", "Código del trabajador en la gestoría, tal como viene."),
        new("Nominas", "NombreOrigen", "Nombre del trabajador tal como viene."),
        new("Nominas", "NifOrigen", "NIF del trabajador si el fichero lo trae."),
        new("Nominas", "TipoPaga", "MENSUAL o EXTRA."),
        new("Nominas", "FechaNomina", "Fecha de la nómina."),
        new("Nominas", "Bruto", "Total devengado (bruto)."),
        new("Nominas", "DtoReEsp", "Descuento por retribución en especie (llega en negativo)."),
        new("Nominas", "InRetEsp", "Ingreso a cuenta por retribución en especie."),
        new("Nominas", "SSEmp", "Seguridad Social a cargo de la empresa."),
        new("Nominas", "TC1", "Total de la liquidación a la Seguridad Social (empresa + cuotas obreras)."),
        new("Nominas", "Autonomo", "Cuota de autónomo pagada como retribución en especie."),
        new("Nominas", "IRPF111", "Retención de IRPF (modelo 111)."),
        new("Nominas", "Neto", "Líquido a percibir."),
        new("Nominas", "AnticOtro", "Anticipos y otras deducciones."),
        new("Nominas", "RetEmbarg", "Retenciones por embargo (llegan en negativo)."),
        new("Nominas", "FaltaPrea", "Falta de preaviso."),
        new("Nominas", "CostEmp", "Coste para la empresa."),
        new("Nominas", "CostPPExt", "Coste con la prorrata de pagas extra."),
        new("Nominas", "DtoVac", "Descuento por vacaciones."),
        new("Nominas", "DifExtra", "Diferencia de paga extra."),
        new("Nominas", "BFormCon", "Bonificación por formación continua."),
        new("Nominas", "HEFestiv", "Horas extra festivas."),
        new("Nominas", "RegBases", "Regularización de bases."),
        new("Nominas", "EnfermedadAccidente", "Enfermedad o accidente (incapacidad temporal)."),
        new("Nominas", "CompensBonif", "Compensación de bonificaciones."),
        new("Nominas", "BonifFundae", "Bonificación FUNDAE (formación)."),
        new("Nominas", "TotalCosteSS", "Total coste de Seguridad Social."),
        new("Nominas", "TotalFLC_FPHIB", "Total FLC / FPHIB."),
        new("Nominas", "ProrrataPagasExtra", "Prorrata de pagas extra."),
        new("Nominas", "CuotasObreras", "Cuotas obreras (Seguridad Social a cargo del trabajador)."),
        new("Nominas", "IdAsiento", "Asiento de la nómina una vez contabilizada."),
    ];
}
