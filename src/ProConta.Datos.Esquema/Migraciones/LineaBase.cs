namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>
/// Versión 1 del esquema: la maestra C:\docs\codigo\ProContaVF\ProConta\Proconta.accdb tal como
/// estaba el 31/10/2026 (opción A del propietario, docs/datos/esquema.md). Una BD es de versión 1
/// si su tabla VersionesEsquema contiene todas estas migraciones heredadas. Los manifiestos
/// antiguos 001..024 del paquete de traspaso no forman parte de ella: son solo referencia.
/// </summary>
public static class LineaBase
{
    public const int Version = 1;

    /// <summary>SHA-256 de la maestra al fijar la versión 1.</summary>
    public const string Sha256Maestra = "A6CC24684CEC173E200B48B6F0E7EAFEA99EED7D279501D9CEDDD3BCCD0BF78F";

    public static readonly IReadOnlyList<string> MigracionesHeredadas =
    [
        "20260811_000_BASE_MODELO_ACTUAL",
        "20260812_001_SERIE_PREDETERMINADA",
        "20260812_002_BORRADORES_NULOS",
        "20260813_001_NORMALIZAR_SOCIEDADES",
        "20260813_002_PROGRAMACIONES_EDITABLES",
        "20260814_001_ALIAS_CAMPOS_CONFIGURACION",
        "20260819_001_TIPOS_DE_IVA",
        "20260821_001_NUCLEO_CONTABLE",
        "20260821_002_CUENTAS_CONTABLES",
        "20260821_003_CONVERSACIONES_IA",
        "20260821_002_MOTOR_IMPORTACION_NORMA43",
        "20260822_001_NUCLEO_CONTABLE_CONTASOL",
        "20260822_001_BALANCES_PGC_PYMES_2008",
        "20260822_003_MOTOR_SEGURIDAD",
        "20260822_004_IDENTIDAD_GRUPOS_SEGURIDAD",
        "20260903_001_ACTIVOS",
        "20260903_002_PLANTILLAS_CUENTAS_ACTIVO",
        "20260903_003_ALINEACION_PLANTILLAS_ACTIVO",
        "20260906_001_MAIL_DISPATCHER",
        "20261001_001_TERCEROS_VF_INCREMENTAL",
        "20261001_002_CONTACTOS_LIBRES_TERCEROS",
    ];
}

/// <summary>
/// Catálogo de las migraciones nuevas, en orden. La versión del esquema es 1 más el número de
/// migraciones del catálogo aplicadas. Cada una la pide un nodo con su línea «Derived from».
/// </summary>
public static class CatalogoMigraciones
{
    public static IReadOnlyList<Migracion> Todas { get; } = [];

    /// <summary>Versión del esquema que espera esta versión del programa.</summary>
    public static int VersionPrograma => LineaBase.Version + Todas.Count;
}
