using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por el gestor documental (docs/documentos.md).</summary>
public static class MigracionesDocumentos
{
    /// <summary>Tipos de documento iniciales: código, nombre, subcarpeta de los que se suben a mano y orden.</summary>
    public static readonly IReadOnlyList<(string Codigo, string Nombre, string Carpeta, short Orden, bool PorAnio)> TiposIniciales =
    [
        ("MODELO", "Modelo presentado", "Modelos", 1, true),
        ("NOTIFICACION", "Notificación de Hacienda", "Hacienda", 2, true),
        ("ESCRITURA", "Escritura", "Empresa", 3, false),
        ("ACTA", "Acta", "Empresa", 4, false),
        ("CONTRATO", "Contrato", "Contratos", 5, false),
        ("CERTIFICADO", "Certificado", "Empresa", 6, false),
        ("JUSTIFICANTE_PAGO", "Justificante de pago", "Pagos", 7, true),
        ("FACTURA_VENTA", "Factura emitida", "Ventas", 20, true),
        ("FACTURA_COMPRA", "Factura recibida", "Compras", 21, true),
        ("DATOS_FISCALES", "Datos fiscales", "Impuestos", 22, true),
        ("NOMINA", "Nóminas", "Nominas", 23, true),
        ("INFORME_GESTORIA", "Informe para la gestoría", "Gestoria", 24, true),
        ("ADJUNTO_CORREO", "Adjunto de correo", "Correo", 25, true),
        ("OTRO", "Otro", "Otros", 99, true),
    ];

    /// <summary>
    /// Gestor documental (esquema 74; documentos 2026-11-18): un catálogo único de los documentos de cada sociedad, con los ficheros en disco
    /// (ruta relativa a la carpeta de documentos de la empresa) y solo sus datos en la BD.
    /// <list type="bullet">
    /// <item>TiposDocumento: catálogo de tipos con la subcarpeta de los que se suben a mano.</item>
    /// <item>Documentos: el documento lógico (sociedad, tipo, título, fecha, ejercicio, estado POR_CLASIFICAR o ARCHIVADO y la propuesta leída del fichero).</item>
    /// <item>DocumentosVersiones: un fichero inmutable por versión, con su ruta, tamaño y huella SHA-256.</item>
    /// <item>DocumentosVinculos: vínculos N:M en una sola tabla (propietario 2026-11-18, opción A): tipo de entidad más su Id o su clave.</item>
    /// </list>
    /// </summary>
    public static Migracion GestorDocumental { get; } = new(
        "20261118_1700_GESTOR_DOCUMENTAL", 261118003,
        "Gestor documental: tipos de documento, documentos de cada sociedad, sus versiones (fichero en disco con su huella) y sus vínculos con modelos, ejercicios, terceros, activos, facturas, asientos y la empresa.",
        "documentos",
        new CrearTabla("TiposDocumento",
            "CREATE TABLE TiposDocumento (Codigo TEXT(20) CONSTRAINT PK_TiposDocumento PRIMARY KEY, Nombre TEXT(60) NOT NULL, Carpeta TEXT(60) NOT NULL, " +
            "Orden SHORT NOT NULL, PorAnio YESNO NOT NULL, Manual YESNO NOT NULL, Activo YESNO NOT NULL)"),
        new PasoPersonalizado(
            "Dar de alta los tipos de documento iniciales",
            "INSERT INTO TiposDocumento: " + string.Join(", ", TiposIniciales.Select(t => $"{t.Codigo}={t.Nombre}/{t.Carpeta}/{t.Orden}/{t.PorAnio}")),
            async (s, e) => e.ExisteTabla("TiposDocumento") &&
                Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposDocumento")) >= TiposIniciales.Count,
            async (s, _) =>
            {
                foreach (var (codigo, nombre, carpeta, orden, porAnio) in TiposIniciales)
                    if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposDocumento WHERE Codigo = ?", codigo)) == 0)
                        await s.EjecutarAsync("INSERT INTO TiposDocumento (Codigo, Nombre, Carpeta, Orden, PorAnio, Manual, Activo) VALUES (?, ?, ?, ?, ?, ?, ?)",
                            codigo, nombre, carpeta, orden, porAnio, orden < 20 || codigo == "OTRO", true);
            }),
        new CrearTabla("Documentos",
            "CREATE TABLE Documentos (IdDocumento COUNTER CONSTRAINT PK_Documentos PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL, " + // sin relación con Sociedades: Access no admite más índices en esa tabla (como ModelosSociedad)
            "Tipo TEXT(20) CONSTRAINT FK_Documentos_Tipo REFERENCES TiposDocumento (Codigo), " +
            "Titulo TEXT(200) NOT NULL, FechaDocumento DATETIME, Ejercicio SHORT, Estado TEXT(15) NOT NULL, Origen TEXT(20) NOT NULL, " +
            "Notas LONGTEXT, Propuesta LONGTEXT, FechaAlta DATETIME NOT NULL, OperadorAlta TEXT(100), FechaModificacion DATETIME, OperadorModificacion TEXT(100))"),
        new CrearIndice("Documentos", "IX_Documentos_SociedadEstado", "CREATE INDEX IX_Documentos_SociedadEstado ON Documentos (IdSociedad, Estado, FechaDocumento)"),
        new CrearTabla("DocumentosVersiones",
            "CREATE TABLE DocumentosVersiones (IdVersionDocumento COUNTER CONSTRAINT PK_DocumentosVersiones PRIMARY KEY, " +
            "IdDocumento LONG NOT NULL CONSTRAINT FK_DocumentosVersiones_Documento REFERENCES Documentos (IdDocumento), " +
            "NumeroVersion SHORT NOT NULL, Ruta TEXT(255) NOT NULL, NombreOriginal TEXT(255), Extension TEXT(10), TamanoBytes LONG NOT NULL, Huella TEXT(64) NOT NULL, " +
            "FechaAlta DATETIME NOT NULL, Operador TEXT(100), Motivo TEXT(255))"),
        new CrearIndice("DocumentosVersiones", "UX_DocumentosVersiones_Numero",
            "CREATE UNIQUE INDEX UX_DocumentosVersiones_Numero ON DocumentosVersiones (IdDocumento, NumeroVersion)"),
        new CrearIndice("DocumentosVersiones", "IX_DocumentosVersiones_Huella", "CREATE INDEX IX_DocumentosVersiones_Huella ON DocumentosVersiones (Huella)"),
        new CrearTabla("DocumentosVinculos",
            "CREATE TABLE DocumentosVinculos (IdVinculoDocumento COUNTER CONSTRAINT PK_DocumentosVinculos PRIMARY KEY, " +
            "IdDocumento LONG NOT NULL CONSTRAINT FK_DocumentosVinculos_Documento REFERENCES Documentos (IdDocumento), " +
            "TipoEntidad TEXT(20) NOT NULL, IdEntidad LONG, ClaveEntidad TEXT(40), Etiqueta TEXT(200), FechaAlta DATETIME NOT NULL, Operador TEXT(100))"),
        new CrearIndice("DocumentosVinculos", "IX_DocumentosVinculos_Documento", "CREATE INDEX IX_DocumentosVinculos_Documento ON DocumentosVinculos (IdDocumento)"),
        new CrearIndice("DocumentosVinculos", "IX_DocumentosVinculos_Id", "CREATE INDEX IX_DocumentosVinculos_Id ON DocumentosVinculos (TipoEntidad, IdEntidad)"),
        new CrearIndice("DocumentosVinculos", "IX_DocumentosVinculos_Clave", "CREATE INDEX IX_DocumentosVinculos_Clave ON DocumentosVinculos (TipoEntidad, ClaveEntidad)"))
    {
        Descripciones =
        [
            new("TiposDocumento", null, "Tipos de documento del gestor documental (documentos): su nombre, la subcarpeta donde se archivan los que se suben a mano y el orden."),
            new("TiposDocumento", "Codigo", "Código estable del tipo (MODELO, NOTIFICACION, ESCRITURA, ACTA, CONTRATO, CERTIFICADO, FACTURA_VENTA…)."),
            new("TiposDocumento", "Nombre", "Nombre que ve el operador."),
            new("TiposDocumento", "Carpeta", "Subcarpeta de «Documentos» (dentro de la carpeta de documentos de la empresa) donde se archivan los que se suben a mano."),
            new("TiposDocumento", "Orden", "Orden en las listas."),
            new("TiposDocumento", "PorAnio", "Si se archivan en una subcarpeta por año (Documentos › año › Modelos) o en una común a todos los años (Documentos › Empresa)."),
            new("TiposDocumento", "Manual", "Si el operador puede elegirlo al subir un documento; los demás los pone el módulo que produce el documento."),
            new("TiposDocumento", "Activo", "Si el tipo se puede usar."),
            new("Documentos", null, "Documentos de cada sociedad (documentos): el documento lógico; sus ficheros están en DocumentosVersiones y sus vínculos en DocumentosVinculos."),
            new("Documentos", "IdDocumento", "Identificador del documento."),
            new("Documentos", "IdSociedad", "Sociedad dueña del documento."),
            new("Documentos", "Tipo", "Tipo de documento (TiposDocumento). Vacío mientras está por clasificar y no se ha propuesto."),
            new("Documentos", "Titulo", "Título; es también el nombre del fichero archivado."),
            new("Documentos", "FechaDocumento", "Fecha del propio documento (la de presentación, la de la notificación, la de la escritura…)."),
            new("Documentos", "Ejercicio", "Año del documento, para filtrar y para la subcarpeta del año."),
            new("Documentos", "Estado", "POR_CLASIFICAR: añadido, pendiente de revisar y archivar (bandeja); ARCHIVADO: en su carpeta definitiva."),
            new("Documentos", "Origen", "Quién lo produjo: MANUAL (subido por el operador) o el módulo que lo generó (FACTURA_VENTA, PRESENTACION…)."),
            new("Documentos", "Notas", "Notas libres del operador."),
            new("Documentos", "Propuesta", "Lo que el programa leyó del fichero al añadirlo: grado de confianza (ALTA, MEDIA, NINGUNA, COMPRA) y el motivo, para la bandeja."),
            new("Documentos", "FechaAlta", "Cuándo se añadió."),
            new("Documentos", "OperadorAlta", "Operador que lo añadió."),
            new("Documentos", "FechaModificacion", "Último cambio de sus datos o vínculos."),
            new("Documentos", "OperadorModificacion", "Operador del último cambio."),
            new("DocumentosVersiones", null, "Versiones de cada documento (documentos): cada una es un fichero inmutable en disco; los envíos apuntan a la versión exacta."),
            new("DocumentosVersiones", "IdVersionDocumento", "Identificador de la versión."),
            new("DocumentosVersiones", "IdDocumento", "Documento."),
            new("DocumentosVersiones", "NumeroVersion", "Número de versión (1, 2…); la mayor es la vigente."),
            new("DocumentosVersiones", "Ruta", "Ruta del fichero, relativa a la carpeta de documentos de la empresa (Sociedades.RutaDocumentos); completa solo si el fichero está fuera de ella."),
            new("DocumentosVersiones", "NombreOriginal", "Nombre del fichero tal como llegó."),
            new("DocumentosVersiones", "Extension", "Extensión del fichero (.pdf, .jpg…)."),
            new("DocumentosVersiones", "TamanoBytes", "Tamaño del fichero en bytes."),
            new("DocumentosVersiones", "Huella", "SHA-256 del contenido en hexadecimal: detecta duplicados y comprueba que el fichero no ha cambiado."),
            new("DocumentosVersiones", "FechaAlta", "Cuándo se guardó la versión."),
            new("DocumentosVersiones", "Operador", "Operador que la guardó."),
            new("DocumentosVersiones", "Motivo", "Motivo de la versión («Alta», «Versión firmada»…)."),
            new("DocumentosVinculos", null, "Vínculos de los documentos con otros registros (documentos): una sola tabla con el tipo de entidad y su Id o su clave."),
            new("DocumentosVinculos", "IdVinculoDocumento", "Identificador del vínculo."),
            new("DocumentosVinculos", "IdDocumento", "Documento."),
            new("DocumentosVinculos", "TipoEntidad", "EMPRESA, EJERCICIO, MODELO, TERCERO, ACTIVO, FACTURA_VENTA, FACTURA_COMPRA, ASIENTO…"),
            new("DocumentosVinculos", "IdEntidad", "Id del registro vinculado (IdSociedadEjercicio, IdTercero, IdActivo, IdFactura, IdRegistro, IdAsiento…)."),
            new("DocumentosVinculos", "ClaveEntidad", "Clave del registro cuando no tiene Id: «modelo|ejercicio|periodo» en un modelo (303|2026|2T) y el código de la sociedad en la empresa."),
            new("DocumentosVinculos", "Etiqueta", "Texto del registro vinculado al vincularlo (por ejemplo «303 · 2T 2026»), para enseñarlo aunque el registro cambie o desaparezca."),
            new("DocumentosVinculos", "FechaAlta", "Cuándo se vinculó."),
            new("DocumentosVinculos", "Operador", "Operador que lo vinculó."),
        ],
    };
}
