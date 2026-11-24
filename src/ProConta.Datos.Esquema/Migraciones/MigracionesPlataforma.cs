namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de plataforma (docs/plataforma.md).</summary>
public static class MigracionesPlataforma
{
    /// <summary>
    /// Entorno de pruebas (plataforma/entorno-pruebas 2026-11-16): la marca que dice que esta BD es una copia de pruebas
    /// viaja dentro del propio fichero, no en su ruta. La pone solo el refresco de la copia de pruebas, junto con la fecha
    /// de la copia y la BD de producción de la que sale; una BD de producción nunca la tiene.
    /// </summary>
    public static Migracion EntornoPruebas { get; } = new(
        "20261116_1400_ENTORNO_PRUEBAS", 261116014,
        "Entorno de pruebas: marca de BD de pruebas con la fecha y el origen de la copia de producción.",
        "plataforma/entorno-pruebas",
        new AnadirColumnaObligatoria("Configuracion", "EntornoPruebas", "YESNO", "No"),
        new AnadirColumna("Configuracion", "CopiaProduccionFecha", "DATETIME"),
        new AnadirColumna("Configuracion", "CopiaProduccionOrigen", "TEXT(255)"))
    {
        Descripciones =
        [
            new("Configuracion", "EntornoPruebas", "Sí si esta BD es la copia de pruebas (la pone el refresco desde producción). En producción, siempre No."),
            new("Configuracion", "CopiaProduccionFecha", "Fecha y hora en que se copió de producción esta BD de pruebas. Nulo en producción."),
            new("Configuracion", "CopiaProduccionOrigen", "Ruta de la BD de producción de la que sale esta copia de pruebas. Nulo en producción."),
        ],
    };

    /// <summary>
    /// Conexión de AWS en Integraciones (plataforma/mantenimiento/conexion-aws 2026-11-24): la región y las claves de AWS de cada
    /// sociedad pasan de la configuración del reconocimiento de compras a una tabla propia, ConexionesAws, que comparten Textract
    /// y lo que use AWS después. Copia las claves guardadas (el secreto, tal cual, ya cifrado) y quita las columnas antiguas.
    /// </summary>
    public static Migracion ConexionAws { get; } = new(
        "20261124_1300_CONEXION_AWS", 261124130,
        "Conexión de AWS por sociedad en Integraciones: tabla ConexionesAws con las claves que estaban en la configuración del reconocimiento de compras.",
        "plataforma/mantenimiento/conexion-aws",
        // Sin relación con Sociedades: Access no admite más índices en esa tabla (como ModelosSociedad o ConexionesAvaibook).
        new CrearTabla("ConexionesAws",
            "CREATE TABLE ConexionesAws (IdSociedad TEXT(3) NOT NULL CONSTRAINT PK_ConexionesAws PRIMARY KEY, " +
            "Region TEXT(30), ClaveAcceso TEXT(128), SecretoProtegido LONGTEXT)"),
        new PasoPersonalizado(
            "Copiar a ConexionesAws las claves de AWS guardadas en la configuración del reconocimiento de compras",
            "INSERT INTO ConexionesAws (IdSociedad, Region, ClaveAcceso, SecretoProtegido) SELECT IdSociedad, RegionAWS, ClaveAccesoAWS, SecretoAWSProtegido " +
            "FROM ConfiguracionReconocimientoFacturasCompra WHERE (RegionAWS Is Not Null OR ClaveAccesoAWS Is Not Null) AND IdSociedad NOT IN (SELECT IdSociedad FROM ConexionesAws)",
            async (s, esquema) => !esquema.ExisteColumna(ReconocimientoCompras, "ClaveAccesoAWS") || !esquema.ExisteColumna(ReconocimientoCompras, "RegionAWS")
                || !esquema.ExisteColumna(ReconocimientoCompras, "SecretoAWSProtegido")
                || (esquema.ExisteTabla("ConexionesAws") && Convert.ToInt32(await s.EscalarAsync(
                    $"SELECT COUNT(*) FROM {ReconocimientoCompras} WHERE (RegionAWS Is Not Null OR ClaveAccesoAWS Is Not Null) " +
                    "AND IdSociedad NOT IN (SELECT IdSociedad FROM ConexionesAws)"), System.Globalization.CultureInfo.InvariantCulture) == 0),
            (s, _) => s.EjecutarAsync(
                "INSERT INTO ConexionesAws (IdSociedad, Region, ClaveAcceso, SecretoProtegido) SELECT IdSociedad, RegionAWS, ClaveAccesoAWS, SecretoAWSProtegido " +
                $"FROM {ReconocimientoCompras} WHERE (RegionAWS Is Not Null OR ClaveAccesoAWS Is Not Null) AND IdSociedad NOT IN (SELECT IdSociedad FROM ConexionesAws)")),
        new QuitarColumna(ReconocimientoCompras, "RegionAWS"),
        new QuitarColumna(ReconocimientoCompras, "ClaveAccesoAWS"),
        new QuitarColumna(ReconocimientoCompras, "SecretoAWSProtegido"))
    {
        Descripciones =
        [
            new("ConexionesAws", null, "Conexión de una sociedad con Amazon Web Services (Parámetros › Integraciones › «AWS · Conexión»): la usan el OCR de Textract y lo que use AWS después."),
            new("ConexionesAws", "IdSociedad", "Sociedad."),
            new("ConexionesAws", "Region", "Región de AWS (eu-west-1…). Obligatoria si hay clave de acceso."),
            new("ConexionesAws", "ClaveAcceso", "Clave de acceso (Access key ID) del usuario de AWS del programa. Vacía: cada equipo usa su perfil de AWS."),
            new("ConexionesAws", "SecretoProtegido", "Clave secreta (Secret access key), cifrada; no se vuelve a mostrar."),
        ],
    };

    private const string ReconocimientoCompras = "ConfiguracionReconocimientoFacturasCompra";
}
