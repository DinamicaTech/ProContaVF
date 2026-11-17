using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de contabilidad (docs/datos/esquema.md, Requirements).</summary>
public static class MigracionesContabilidad
{
    /// <summary>
    /// Motor de asientos: propiedad de los asientos automáticos (motor, proceso y clave de ejecución
    /// del lote que los genera) y registro de cambios de apuntes agrupado por IdLote.
    /// </summary>
    public static Migracion MotorAsientos { get; } = new(
        "20261031_1915_MOTOR_ASIENTOS", 261031005,
        "Asientos automáticos con lote propietario y registro de cambios de apuntes por IdLote.",
        "contabilidad/asientos",
        new AnadirColumna("Asientos", "EsAutomatico", "YESNO NOT NULL DEFAULT No"),
        new AnadirColumna("Asientos", "MotorPropietario", "TEXT(50)"),
        new AnadirColumna("Asientos", "CodigoProceso", "TEXT(100)"),
        new AnadirColumna("Asientos", "ClaveEjecucion", "TEXT(150)"),
        new CrearIndice("Asientos", "IX_Asientos_PropiedadAutomatica",
            "CREATE INDEX IX_Asientos_PropiedadAutomatica ON Asientos (EsAutomatico, MotorPropietario, CodigoProceso, IdSociedadEjercicio, ClaveEjecucion)"),
        new CrearTabla("RegistroCambiosApuntes",
            "CREATE TABLE RegistroCambiosApuntes (IdCambioApunte COUNTER CONSTRAINT PK_RegistroCambiosApuntes PRIMARY KEY, IdLote TEXT(36) NOT NULL, " +
            "FechaHora DATETIME NOT NULL, Operador TEXT(100) NOT NULL, Motivo LONGTEXT NOT NULL, IdAsiento LONG NOT NULL, IdApunte LONG, " +
            "Orden SHORT, Operacion TEXT(15) NOT NULL, Campo TEXT(64) NOT NULL, ValorAntiguo LONGTEXT, ValorNuevo LONGTEXT)"),
        new CrearIndice("RegistroCambiosApuntes", "IX_RegistroCambiosApuntes_Lote",
            "CREATE INDEX IX_RegistroCambiosApuntes_Lote ON RegistroCambiosApuntes (IdLote)"),
        new CrearIndice("RegistroCambiosApuntes", "IX_RegistroCambiosApuntes_Asiento",
            "CREATE INDEX IX_RegistroCambiosApuntes_Asiento ON RegistroCambiosApuntes (IdAsiento)"));

    /// <summary>
    /// Punteo y cuadre del Mayor (contabilidad/mayor 2026-11-11 20:05): la marca de punteado de cada apunte (la cambia el motor
    /// de asientos, también en automáticos y periodos cerrados, y se conserva al regenerar el lote), los cuadres por cuenta y
    /// ejercicio (casilla «Cuadrada» con el saldo con que se marcó, notas largas, operador y fecha), y el umbral de «Reiniciar el cuadre» en la
    /// configuración general (vacío = 0,05 €). Solo estructura, sin datos.
    /// </summary>
    public static Migracion PunteoCuadreMayor { get; } = new(
        "20261111_2005_PUNTEO_CUADRE_MAYOR", 261111015,
        "Punteo de los apuntes, cuadres de cuentas por ejercicio y umbral para reiniciar el cuadre del Mayor.",
        "contabilidad/mayor",
        new AnadirColumna("Apuntes", "Punteado", "YESNO NOT NULL DEFAULT No"),
        new CrearTabla("CuadresCuentas",
            "CREATE TABLE CuadresCuentas (IdCuadreCuenta COUNTER CONSTRAINT PK_CuadresCuentas PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_CuadresCuentas_SociedadEjercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "CuentaContable TEXT(20) NOT NULL, Cuadrada YESNO NOT NULL, Saldo CURRENCY, Notas LONGTEXT, Operador TEXT(30) NOT NULL, FechaHora DATETIME NOT NULL)"),
        new CrearIndice("CuadresCuentas", "UX_CuadresCuentas_EjercicioCuenta",
            "CREATE UNIQUE INDEX UX_CuadresCuentas_EjercicioCuenta ON CuadresCuentas (IdSociedadEjercicio, CuentaContable)"),
        new AnadirColumna("Configuracion", "UmbralCuadreMayor", "CURRENCY"));
    /// <summary>
    /// Plantillas de asiento (contabilidad/plantillas-asiento 2026-11-12 11:15): cabecera (PlantillasAsiento), datos de entrada
    /// (PlantillasAsientoDatos) y líneas (PlantillasAsientoLineas), con el catálogo inicial (seis plantillas de usuario y la de sistema
    /// «Nómina», sin líneas); en Asientos, la plantilla con que se creó cada asiento. La relación asiento de origen → corrección va en
    /// RelacionesAsientos (TipoRelacion CORRECCION), que ya existía. Sustituyen a PlantillasApuntes del ERP.
    /// </summary>
    public static Migracion PlantillasAsiento { get; } = new(
        "20261112_1230_PLANTILLAS_ASIENTO", 261112002,
        "Plantillas de asiento (cabecera, datos de entrada y líneas con fórmulas) con su catálogo inicial, y la plantilla usada en cada asiento.",
        "contabilidad/plantillas-asiento",
        new CrearTabla("PlantillasAsiento",
            "CREATE TABLE PlantillasAsiento (IdPlantillaAsiento COUNTER CONSTRAINT PK_PlantillasAsiento PRIMARY KEY, Nombre TEXT(100) NOT NULL, Descripcion LONGTEXT, " +
            "Tipo TEXT(10) NOT NULL, IdSociedad TEXT(3) CONSTRAINT FK_PlantillasAsiento_Sociedad REFERENCES Sociedades (IdSociedad), Origen TEXT(10) NOT NULL, " +
            "ClaveProceso TEXT(50), ReglaFecha TEXT(20) NOT NULL, AgruparPorCuenta YESNO NOT NULL, Activa YESNO NOT NULL, MotivoPropuesto TEXT(255), Ejemplo LONGTEXT, Orden SHORT NOT NULL)"),
        new CrearIndice("PlantillasAsiento", "IX_PlantillasAsiento_Nombre", "CREATE INDEX IX_PlantillasAsiento_Nombre ON PlantillasAsiento (Nombre)"),
        new CrearTabla("PlantillasAsientoDatos",
            "CREATE TABLE PlantillasAsientoDatos (IdPlantillaAsientoDato COUNTER CONSTRAINT PK_PlantillasAsientoDatos PRIMARY KEY, " +
            "IdPlantillaAsiento LONG NOT NULL CONSTRAINT FK_PlantillasAsientoDatos_Plantilla REFERENCES PlantillasAsiento (IdPlantillaAsiento), " +
            "Orden SHORT NOT NULL, Nombre TEXT(50) NOT NULL, Etiqueta TEXT(100) NOT NULL, Tipo TEXT(10) NOT NULL, ValorPropuesto TEXT(255), FiltroCuenta TEXT(30), " +
            "Ayuda TEXT(255), Obligatorio YESNO NOT NULL)"),
        new CrearIndice("PlantillasAsientoDatos", "UX_PlantillasAsientoDatos_Nombre",
            "CREATE UNIQUE INDEX UX_PlantillasAsientoDatos_Nombre ON PlantillasAsientoDatos (IdPlantillaAsiento, Nombre)"),
        new CrearTabla("PlantillasAsientoLineas",
            "CREATE TABLE PlantillasAsientoLineas (IdPlantillaAsientoLinea COUNTER CONSTRAINT PK_PlantillasAsientoLineas PRIMARY KEY, " +
            "IdPlantillaAsiento LONG NOT NULL CONSTRAINT FK_PlantillasAsientoLineas_Plantilla REFERENCES PlantillasAsiento (IdPlantillaAsiento), " +
            "NumeroAsiento SHORT NOT NULL, Orden SHORT NOT NULL, PorCadaApunte YESNO NOT NULL, FormulaCuenta TEXT(255) NOT NULL, Lado TEXT(255) NOT NULL, " +
            "FormulaImporte TEXT(255) NOT NULL, FormulaConcepto TEXT(255), FormulaCentro TEXT(255), Condicion TEXT(255))"),
        new CrearIndice("PlantillasAsientoLineas", "UX_PlantillasAsientoLineas_Orden",
            "CREATE UNIQUE INDEX UX_PlantillasAsientoLineas_Orden ON PlantillasAsientoLineas (IdPlantillaAsiento, NumeroAsiento, Orden)"),
        new AnadirColumna("Asientos", "IdPlantillaAsiento", "LONG"),
        new CrearIndice("Asientos", "IX_Asientos_Plantilla", "CREATE INDEX IX_Asientos_Plantilla ON Asientos (IdPlantillaAsiento)"),
        RelacionPlantillaAsiento(),
        new PasoPersonalizado(
            "Dar de alta el catálogo inicial de plantillas de asiento",
            "INSERT INTO PlantillasAsiento, PlantillasAsientoDatos, PlantillasAsientoLineas <- Semillas/PlantillasAsiento.json (las que falten por nombre, sin sociedad); " +
            SemillaPlantillasAsiento.Huella,
            SemillaPlantillasAsiento.CompletaAsync,
            async (s, _) => await SemillaPlantillasAsiento.CargarAsync(s)))
    {
        Descripciones = DescripcionesPlantillas,
    };

    /// <summary>Descripción de cada tabla y campo de las plantillas (propiedad Description de Access; la recoge el diccionario de datos).</summary>
    internal static IReadOnlyList<DescripcionEsquema> DescripcionesPlantillas =>
    [
        new("PlantillasAsiento", null, "Plantillas de asiento: recetas de apuntes con fórmulas que proponen uno o varios asientos (contabilidad/plantillas-asiento)."),
        new("PlantillasAsiento", "IdPlantillaAsiento", "Identificador de la plantilla."),
        new("PlantillasAsiento", "Nombre", "Nombre de la plantilla que ve el usuario (p. ej. «Anular asiento»)."),
        new("PlantillasAsiento", "Descripcion", "Explicación para el usuario de qué asiento genera y cuándo usarla."),
        new("PlantillasAsiento", "Tipo", "USUARIO: se ofrece en el asiento manual y en el Mayor. SISTEMA: solo la ejecuta su proceso (p. ej. la nómina)."),
        new("PlantillasAsiento", "IdSociedad", "Empresa a la que pertenece; vacío = todas las empresas."),
        new("PlantillasAsiento", "Origen", "Qué necesita para generar el asiento: NINGUNO, APUNTE (un apunte y su asiento) o PROCESO (registros de un proceso)."),
        new("PlantillasAsiento", "ClaveProceso", "Con origen PROCESO, el proceso que la ejecuta (p. ej. nominas)."),
        new("PlantillasAsiento", "ReglaFecha", "TRIMESTRE_ACTIVO: primer día del trimestre activo o la fecha del origen si es posterior. FECHA_ORIGEN: la fecha del origen."),
        new("PlantillasAsiento", "AgruparPorCuenta", "Sí: suma en una línea los importes de la misma cuenta y genera un solo asiento para todos los registros del proceso."),
        new("PlantillasAsiento", "Activa", "Solo las activas se ofrecen y se ejecutan."),
        new("PlantillasAsiento", "MotivoPropuesto", "Texto que se propone como motivo del cambio (el motivo es obligatorio al ejecutar)."),
        new("PlantillasAsiento", "Ejemplo", "Ejemplo ficticio en JSON (origen y datos de entrada) del que sale la vista previa de la plantilla."),
        new("PlantillasAsiento", "Orden", "Orden en que se ofrece la plantilla en las listas."),
        new("PlantillasAsientoDatos", null, "Datos de entrada que pide cada plantilla antes de proponer el asiento."),
        new("PlantillasAsientoDatos", "IdPlantillaAsientoDato", "Identificador del dato."),
        new("PlantillasAsientoDatos", "IdPlantillaAsiento", "Plantilla a la que pertenece."),
        new("PlantillasAsientoDatos", "Orden", "Orden en que se pide el dato."),
        new("PlantillasAsientoDatos", "Nombre", "Nombre con que lo usan las fórmulas, sin espacios (p. ej. NuevaCuenta)."),
        new("PlantillasAsientoDatos", "Etiqueta", "Texto con que se pide al usuario."),
        new("PlantillasAsientoDatos", "Tipo", "IMPORTE, CUENTA, TEXTO o FECHA."),
        new("PlantillasAsientoDatos", "ValorPropuesto", "Fórmula del valor que se propone (p. ej. Importe o UltimaUsada())."),
        new("PlantillasAsientoDatos", "FiltroCuenta", "En un dato de cuenta, prefijo de las cuentas que admite y que ofrece el buscador (p. ej. 551)."),
        new("PlantillasAsientoDatos", "Ayuda", "Ayuda para el usuario sobre el dato."),
        new("PlantillasAsientoDatos", "Obligatorio", "Sí: no se propone el asiento sin este dato."),
        new("PlantillasAsientoLineas", null, "Líneas de cada plantilla: un apunte de uno de sus asientos, con fórmulas."),
        new("PlantillasAsientoLineas", "IdPlantillaAsientoLinea", "Identificador de la línea."),
        new("PlantillasAsientoLineas", "IdPlantillaAsiento", "Plantilla a la que pertenece."),
        new("PlantillasAsientoLineas", "NumeroAsiento", "Asiento de la plantilla al que va la línea (una plantilla puede generar varios: 1, 2…)."),
        new("PlantillasAsientoLineas", "Orden", "Orden de la línea dentro de su asiento."),
        new("PlantillasAsientoLineas", "PorCadaApunte", "Sí: la línea se repite por cada apunte del asiento de origen (anular, rehacer) con los campos de ese apunte."),
        new("PlantillasAsientoLineas", "FormulaCuenta", "Fórmula de la cuenta: un campo, un dato, CuentaCuadres, CuentaTesoreria, un código entre comillas o un prefijo terminado en ?."),
        new("PlantillasAsientoLineas", "Lado", "D, H, SIGNO (positivo al Haber), MISMO o CONTRARIO (al lado del apunte de origen), o una fórmula que dé uno de ellos."),
        new("PlantillasAsientoLineas", "FormulaImporte", "Fórmula del importe (decimal, redondeado a 2 decimales); 0 = no se crea la línea; negativo = cambia de lado."),
        new("PlantillasAsientoLineas", "FormulaConcepto", "Fórmula del concepto del apunte (p. ej. \"Anulación: \" & Concepto)."),
        new("PlantillasAsientoLineas", "FormulaCentro", "Fórmula del centro de coste (p. ej. Centro, el del apunte de origen); vacío = sin centro."),
        new("PlantillasAsientoLineas", "Condicion", "Fórmula que, si da falso, hace que no se cree la línea; vacío = siempre."),
        new("Asientos", "IdPlantillaAsiento", "Plantilla de asiento con la que se creó el asiento; vacío si no se usó ninguna."),
    ];

    /// <summary>
    /// Clases de plantilla y orígenes de las correcciones (contabilidad/plantillas-asiento 2026-11-12 20:11): PlantillasAsiento.Clase (CORRECCION,
    /// GUIA, ASISTENTE o PROCESO); las seis correcciones pasan a origen ASIENTO (anular, rehacer), APUNTE (pagos) o SUBCUENTA (pasar el saldo,
    /// ajustar el saldo inicial), con sus datos y ejemplos nuevos; la «Nómina» es de PROCESO; y se da de alta la guía «Pasar un cliente de cobro
    /// dudoso a incobrable» (Semillas/PlantillasAsientoCatalogo.json).
    /// </summary>
    public static Migracion ClasesPlantillasAsiento { get; } = new(
        "20261112_2015_CLASES_PLANTILLAS_ASIENTO", 261112003,
        "Clases de plantilla de asiento (corrección, guía, asistente y proceso), orígenes Asiento y Subcuenta en las correcciones y la guía de cobro dudoso.",
        "contabilidad/plantillas-asiento",
        new AnadirColumna("PlantillasAsiento", "Clase", "TEXT(12)"),
        new PasoPersonalizado(
            "Poner al día el catálogo de plantillas de asiento (clases, orígenes y guía de cobro dudoso)",
            "UPDATE/INSERT PlantillasAsiento, PlantillasAsientoDatos, PlantillasAsientoLineas <- Semillas/PlantillasAsientoCatalogo.json (las comunes del catálogo, por nombre); " +
            SemillaPlantillasAsiento.HuellaCatalogo,
            SemillaPlantillasAsiento.CatalogoAlDiaAsync,
            async (s, _) => await SemillaPlantillasAsiento.ActualizarCatalogoAsync(s)))
    {
        Descripciones =
        [
            new("PlantillasAsiento", "Clase", "CORRECCION: corrige un asiento, un apunte o una subcuenta desde el Mayor. GUIA: explicación y asiento de ejemplo. ASISTENTE: pide datos y propone un asiento. PROCESO: la ejecuta un proceso."),
            new("PlantillasAsiento", "Origen", "Qué necesita: NINGUNO, ASIENTO (un asiento entero), APUNTE (un apunte), SUBCUENTA (una subcuenta y su saldo) o PROCESO (registros de un proceso)."),
        ],
    };

    private static PasoPersonalizado RelacionPlantillaAsiento()
    {
        const string sql = "ALTER TABLE Asientos ADD CONSTRAINT FK_Asientos_PlantillaAsiento FOREIGN KEY (IdPlantillaAsiento) REFERENCES PlantillasAsiento (IdPlantillaAsiento)";
        return new PasoPersonalizado("Relacionar Asientos.IdPlantillaAsiento con PlantillasAsiento", sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, "Asientos", "FK_Asientos_PlantillaAsiento")),
            (s, _) => s.EjecutarAsync(sql));
    }

    /// <summary>Descripción de la cuenta (contabilidad/cuenta-contable 2026-11-06): la NEXMAE del ERP, que trae su migración.</summary>
    public static Migracion DescripcionCuentas { get; } = new(
        "20261106_1810_DESCRIPCION_CUENTAS", 261106005,
        "Descripción opcional de las cuentas contables.",
        "contabilidad/cuenta-contable",
        new AnadirColumna("CuentasContables", "Descripcion", "TEXT(255)"));

    /// <summary>
    /// Actividades a efectos de IVA (propietario 2026-11-08 09:01; en el ERP, CategoriasIVA): por empresa, con su código,
    /// nombre, si el IVA soportado es deducible (0 no, 1 por prorrata, 2 al 100 %) y la serie de cuentas 472 de su IVA
    /// soportado (4725, 4726…). Cada centro de coste puede tener su actividad, y la heredan los que cuelgan de él: sustituye
    /// a la n:m CategoriasIVAPorDepartamento del ERP. Solo estructura, sin datos.
    /// </summary>
    public static Migracion ActividadesIva { get; } = new(
        "20261108_0930_ACTIVIDADES_IVA", 261108001,
        "Actividades a efectos de IVA (deducibilidad y serie de la 472) y actividad de cada centro de coste.",
        "contabilidad/procesos",
        new CrearTabla("Actividades",
            "CREATE TABLE Actividades (IdActividad COUNTER CONSTRAINT PK_Actividades PRIMARY KEY, " +
            "IdSociedad TEXT(3) NOT NULL CONSTRAINT FK_Actividades_Sociedad REFERENCES Sociedades (IdSociedad), " +
            "Codigo TEXT(10) NOT NULL, Nombre TEXT(100) NOT NULL, Deducible BYTE NOT NULL, SerieCuentaIva TEXT(4), Activa YESNO NOT NULL)"),
        new CrearIndice("Actividades", "UX_Actividades_SociedadCodigo",
            "CREATE UNIQUE INDEX UX_Actividades_SociedadCodigo ON Actividades (IdSociedad, Codigo)"),
        new AnadirColumna("CentrosCoste", "IdActividad", "LONG"),
        RelacionActividad());

    /// <summary>
    /// Modelo PGC Pymes 2008 de los balances (contabilidad/balances 2026-11-11): la maestra lo trae en ModelosBalance, NodosBalance
    /// y ReglasCuentasBalance (lo sembró la migración heredada 20260822_001_BALANCES_PGC_PYMES_2008), pero las BD hechas con
    /// copia-estructura antes de esta versión las dejaron vacías. Si las tres tablas están vacías, carga la misma semilla que la
    /// maestra, con sus mismos identificadores (Semillas/*.tsv); si ya tienen datos no toca nada.
    /// </summary>
    public static Migracion EsquemaBalances { get; } = new(
        "20261111_1430_ESQUEMA_BALANCES_PGC_PYMES_2008", 261111010,
        "Modelo PGC Pymes 2008 de los balances de situación y de pérdidas y ganancias, si las tablas de balances están vacías.",
        "contabilidad/balances",
        new PasoPersonalizado(
            "Cargar el modelo PGC Pymes 2008 de los balances",
            "INSERT INTO ModelosBalance, NodosBalance, ReglasCuentasBalance <- Semillas (2 modelos, 89 nodos, 139 reglas de la maestra) si las tres están vacías; " +
            SemillaBalances.Huella,
            async (s, _) => await SemillaBalances.HayModeloAsync(s),
            async (s, _) => await SemillaBalances.CargarAsync(s)));

    /// <summary>
    /// Cuentas con apuntes en la contabilidad importada de ContaSol (2018–2026) que el modelo PGC Pymes 2008 de la maestra no
    /// clasificaba (contabilidad/balances 2026-11-11): su sitio según el modelo oficial. En situación, además, todo el grupo 6
    /// y 7 va al resultado del ejercicio con la prioridad más baja, como las cuentas 6 y 7 que ya estaban una a una.
    /// </summary>
    internal static readonly (string Modelo, string Nodo, string Prefijo, short Multiplicador)[] ReglasBalancesContaSol =
    [
        ("PGC_PYMES_2008_SITUACION", "ACT.A.2.002", "211", 1),
        ("PGC_PYMES_2008_SITUACION", "ACT.A.4.004", "240", 1),
        ("PGC_PYMES_2008_SITUACION", "ACT.B.4.015", "542", 1),
        ("PGC_PYMES_2008_SITUACION", "ACT.B.4.015", "545", 1),
        ("PGC_PYMES_2008_SITUACION", "ACT.B.4.015", "552", 1),
        ("PGC_PYMES_2008_SITUACION", "PNP.A.3.S.024", "119", -1),
        ("PGC_PYMES_2008_SITUACION", "PNP.B.2.039", "171", -1),
        ("PGC_PYMES_2008_SITUACION", "PNP.B.3.040", "163", -1),
        ("PGC_PYMES_2008_SITUACION", "PNP.A.7.S.032", "6", -1),
        ("PGC_PYMES_2008_SITUACION", "PNP.A.7.S.032", "7", -1),
        ("PGC_PYMES_2008_PYG", "PYG.A.5.063", "754", -1),
        ("PGC_PYMES_2008_PYG", "PYG.A.11.070", "771", -1),
        ("PGC_PYMES_2008_PYG", "PYG.B.13.b.075", "760", -1),
        ("PGC_PYMES_2008_PYG", "PYG.B.17.079", "666", -1),
    ];

    /// <summary>Añade las reglas de <see cref="ReglasBalancesContaSol"/> que falten en el modelo (por prefijo); las que ya estén no se tocan.</summary>
    public static Migracion ReglasBalancesCompletas { get; } = new(
        "20261111_1540_REGLAS_BALANCES_CONTASOL", 261111011,
        "Reglas del modelo PGC Pymes 2008 para las cuentas de ContaSol que no clasificaba (163, 171, 119, 211, 240, 542, 545, 552, 754, 760, 666, 771 y el grupo 6 y 7 en situación).",
        "contabilidad/balances",
        new PasoPersonalizado(
            "Añadir al modelo PGC Pymes 2008 las reglas de las cuentas de ContaSol",
            "INSERT INTO ReglasCuentasBalance (prefijo y nodo por código del modelo, prioridad = longitud del prefijo) si el modelo no tiene regla de ese prefijo: " +
            string.Join(", ", ReglasBalancesContaSol.Select(r => $"{r.Modelo}/{r.Nodo}/{r.Prefijo}*{r.Multiplicador}")),
            async (s, _) =>
            {
                foreach (var r in ReglasBalancesContaSol)
                    if (await ReglaBalanceAsync(s, r.Modelo, r.Prefijo) == 0) return false;
                return true;
            },
            async (s, _) =>
            {
                foreach (var r in ReglasBalancesContaSol)
                {
                    if (await ReglaBalanceAsync(s, r.Modelo, r.Prefijo) > 0) continue;
                    var nodo = await s.EscalarAsync(
                        "SELECT n.IdNodoBalance FROM NodosBalance AS n INNER JOIN ModelosBalance AS m ON n.IdModeloBalance = m.IdModeloBalance WHERE m.Codigo = ? AND n.Codigo = ?",
                        r.Modelo, r.Nodo)
                        ?? throw new InvalidOperationException(
                            $"El modelo de balance {r.Modelo} no tiene el nodo {r.Nodo} de la maestra, así que no sé dónde poner las cuentas {r.Prefijo}*. " +
                            "Si se modificó el modelo a mano, añade esa regla en ReglasCuentasBalance o restaura el modelo de la maestra.");
                    await s.EjecutarAsync(
                        "INSERT INTO ReglasCuentasBalance (IdNodoBalance, TipoCoincidencia, PrefijoCuenta, EsInclusion, Multiplicador, Prioridad, Descripcion) VALUES (?, ?, ?, ?, ?, ?, ?)",
                        Convert.ToInt32(nodo, System.Globalization.CultureInfo.InvariantCulture), (byte)2, r.Prefijo, true, r.Multiplicador, (short)r.Prefijo.Length,
                        $"Regla de presentación {r.Prefijo}* (cuentas de ContaSol)");
                }
            }));

    /// <summary>
    /// Diario (contabilidad/diario 2026-11-14 21:50): copia de los asientos manuales borrados (AsientosBorrados y ApuntesBorrados, con
    /// la fecha, el operador y el motivo del borrado), consultable desde la pantalla, y la marca de asiento automático corregido a mano
    /// (Asientos.CorregidoAMano), que el motor quita al regenerar el lote. Solo añade.
    /// </summary>
    public static Migracion DiarioAsientosBorrados { get; } = new(
        "20261114_2200_DIARIO_ASIENTOS_BORRADOS", 261114030,
        "Copia de los asientos manuales borrados, con su motivo, y marca de asiento automático corregido a mano.",
        "contabilidad/diario",
        new CrearTabla("AsientosBorrados",
            "CREATE TABLE AsientosBorrados (IdAsientoBorrado COUNTER CONSTRAINT PK_AsientosBorrados PRIMARY KEY, IdAsiento LONG NOT NULL, " +
            "IdSociedadEjercicio LONG NOT NULL, FechaContable DATETIME NOT NULL, Descripcion TEXT(255) NOT NULL, TipoAsiento BYTE NOT NULL, Notas LONGTEXT, " +
            "OperadorCreacion TEXT(100), FechaHoraCreacion DATETIME, OperadorModificacion TEXT(100), FechaHoraModificacion DATETIME, " +
            "TotalDebe CURRENCY NOT NULL, TotalHaber CURRENCY NOT NULL, Version LONG NOT NULL, IdPlantillaAsiento LONG, " +
            "FechaHoraBorrado DATETIME NOT NULL, OperadorBorrado TEXT(100) NOT NULL, MotivoBorrado LONGTEXT NOT NULL)"),
        new CrearIndice("AsientosBorrados", "IX_AsientosBorrados_Ejercicio",
            "CREATE INDEX IX_AsientosBorrados_Ejercicio ON AsientosBorrados (IdSociedadEjercicio, FechaHoraBorrado)"),
        new CrearIndice("AsientosBorrados", "IX_AsientosBorrados_Asiento",
            "CREATE INDEX IX_AsientosBorrados_Asiento ON AsientosBorrados (IdAsiento)"),
        new CrearTabla("ApuntesBorrados",
            "CREATE TABLE ApuntesBorrados (IdApunteBorrado COUNTER CONSTRAINT PK_ApuntesBorrados PRIMARY KEY, " +
            "IdAsientoBorrado LONG NOT NULL CONSTRAINT FK_ApuntesBorrados_AsientoBorrado REFERENCES AsientosBorrados (IdAsientoBorrado), " +
            "IdApunte LONG NOT NULL, IdAsiento LONG NOT NULL, Orden SHORT NOT NULL, CuentaContable TEXT(20) NOT NULL, ImporteDebe CURRENCY, ImporteHaber CURRENCY, " +
            "Descripcion TEXT(255), NumeroDocumento TEXT(100), IdCliente LONG, IdProveedor LONG, FechaVencimiento DATETIME, IdCentroCoste LONG, " +
            "TipoApunte BYTE NOT NULL, Punteado YESNO NOT NULL)"),
        new AnadirColumna("Asientos", "CorregidoAMano", "YESNO NOT NULL DEFAULT No"))
    {
        Descripciones =
        [
            new("AsientosBorrados", null, "Copia de los asientos manuales eliminados, con cuándo, quién y por qué. Solo se consulta (Diario › «Asientos borrados…»)."),
            new("AsientosBorrados", "IdAsientoBorrado", "Identificador de la copia."),
            new("AsientosBorrados", "IdAsiento", "Número que tenía el asiento (Asientos.IdAsiento); no se reutiliza."),
            new("AsientosBorrados", "IdSociedadEjercicio", "Sociedad y ejercicio del asiento."),
            new("AsientosBorrados", "FechaContable", "Fecha contable que tenía."),
            new("AsientosBorrados", "Descripcion", "Descripción que tenía."),
            new("AsientosBorrados", "TipoAsiento", "Tipo de asiento que tenía."),
            new("AsientosBorrados", "Notas", "Notas que tenía."),
            new("AsientosBorrados", "OperadorCreacion", "Quién lo grabó."),
            new("AsientosBorrados", "FechaHoraCreacion", "Cuándo se grabó."),
            new("AsientosBorrados", "OperadorModificacion", "Quién lo cambió por última vez."),
            new("AsientosBorrados", "FechaHoraModificacion", "Cuándo se cambió por última vez."),
            new("AsientosBorrados", "TotalDebe", "Total del Debe."),
            new("AsientosBorrados", "TotalHaber", "Total del Haber."),
            new("AsientosBorrados", "Version", "Versión que tenía al borrarlo."),
            new("AsientosBorrados", "IdPlantillaAsiento", "Plantilla con la que se creó, si la hubo."),
            new("AsientosBorrados", "FechaHoraBorrado", "Cuándo se borró."),
            new("AsientosBorrados", "OperadorBorrado", "Quién lo borró."),
            new("AsientosBorrados", "MotivoBorrado", "Motivo que se dio al borrarlo (obligatorio)."),
            new("ApuntesBorrados", null, "Apuntes de los asientos de AsientosBorrados, tal como estaban al borrarlos."),
            new("ApuntesBorrados", "IdApunteBorrado", "Identificador de la copia."),
            new("ApuntesBorrados", "IdAsientoBorrado", "Asiento borrado al que pertenece."),
            new("ApuntesBorrados", "IdApunte", "Identificador que tenía el apunte (Apuntes.IdApunte)."),
            new("ApuntesBorrados", "IdAsiento", "Número del asiento que lo contenía."),
            new("ApuntesBorrados", "Orden", "Orden dentro del asiento."),
            new("ApuntesBorrados", "CuentaContable", "Subcuenta."),
            new("ApuntesBorrados", "ImporteDebe", "Importe al Debe."),
            new("ApuntesBorrados", "ImporteHaber", "Importe al Haber."),
            new("ApuntesBorrados", "Descripcion", "Concepto del apunte."),
            new("ApuntesBorrados", "NumeroDocumento", "Número de factura o documento."),
            new("ApuntesBorrados", "IdCliente", "Cliente del apunte."),
            new("ApuntesBorrados", "IdProveedor", "Proveedor del apunte."),
            new("ApuntesBorrados", "FechaVencimiento", "Vencimiento."),
            new("ApuntesBorrados", "IdCentroCoste", "Centro de coste."),
            new("ApuntesBorrados", "TipoApunte", "Ordinario o ajuste de redondeo."),
            new("ApuntesBorrados", "Punteado", "Si estaba punteado."),
            new("Asientos", "CorregidoAMano", "Asiento automático cuyas cuentas o importes se han corregido a mano; se quita al regenerar su origen."),
        ],
    };

    private static async Task<int> ReglaBalanceAsync(ISesionDatos s, string modelo, string prefijo) => Convert.ToInt32(await s.EscalarAsync(
        "SELECT COUNT(*) FROM (ReglasCuentasBalance AS r INNER JOIN NodosBalance AS n ON r.IdNodoBalance = n.IdNodoBalance) INNER JOIN ModelosBalance AS m ON n.IdModeloBalance = m.IdModeloBalance " +
        "WHERE m.Codigo = ? AND r.PrefijoCuenta = ?", modelo, prefijo), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Cambios de deducción de una actividad desde una fecha (contabilidad/procesos/cambio-deduccion-actividad 2026-11-15 19:56):
    /// desde FechaDesde, la actividad deduce como dice Deducible (0 no, 1 por prorrata, 2 al 100 %); antes del primer cambio vale
    /// Actividades.Deducible. Caso que lo origina: el alquiler turístico, 100 % deducible desde el 1/12/2026. Solo estructura, sin datos.
    /// </summary>
    public static Migracion DeduccionActividadFechas { get; } = new(
        "20261115_2000_DEDUCCION_ACTIVIDAD_FECHAS", 261115020,
        "Cambios de deducción de las actividades de IVA a partir de una fecha.",
        "contabilidad/procesos/cambio-deduccion-actividad",
        new CrearTabla("ActividadesDeduccion",
            "CREATE TABLE ActividadesDeduccion (IdActividadDeduccion COUNTER CONSTRAINT PK_ActividadesDeduccion PRIMARY KEY, " +
            "IdActividad LONG NOT NULL CONSTRAINT FK_ActividadesDeduccion_Actividad REFERENCES Actividades (IdActividad), " +
            "FechaDesde DATETIME NOT NULL, Deducible BYTE NOT NULL)"),
        new CrearIndice("ActividadesDeduccion", "UX_ActividadesDeduccion_Fecha",
            "CREATE UNIQUE INDEX UX_ActividadesDeduccion_Fecha ON ActividadesDeduccion (IdActividad, FechaDesde)"))
    {
        Descripciones =
        [
            new("ActividadesDeduccion", null, "Cambios de deducción de una actividad de IVA desde una fecha. Antes del primer cambio vale Actividades.Deducible. Cada factura se clasifica por su fecha de expedición."),
            new("ActividadesDeduccion", "IdActividadDeduccion", "Identificador del cambio."),
            new("ActividadesDeduccion", "IdActividad", "Actividad que cambia."),
            new("ActividadesDeduccion", "FechaDesde", "Primer día en que vale la deducción nueva (sin hora)."),
            new("ActividadesDeduccion", "Deducible", "Deducción desde esa fecha: 0 no deducible, 1 por prorrata, 2 deducible al 100 %."),
        ],
    };

    /// <summary>
    /// Liquidación del IVA por trimestre (contabilidad/procesos/liquidacion-iva 2026-11-16): una fila por ejercicio y trimestre
    /// liquidado, con la prorrata usada, lo calculado desde los libros, lo liquidado (que el operador puede ajustar a lo presentado),
    /// lo compensado de la 4700, el ajuste y su motivo, el asiento y quién lo confirmó. Añade a SociedadesEjercicios las cuatro
    /// cuentas de la liquidación (vacías: 4750000, 4700000, 6341000 y 6391000). Solo estructura, sin datos.
    /// </summary>
    public static Migracion LiquidacionesIva { get; } = new(
        "20261116_1000_LIQUIDACIONES_IVA", 261116010,
        "Liquidaciones del IVA por trimestre y cuentas de la liquidación del ejercicio.",
        "contabilidad/procesos/liquidacion-iva",
        new CrearTabla("LiquidacionesIva",
            "CREATE TABLE LiquidacionesIva (IdLiquidacionIva COUNTER CONSTRAINT PK_LiquidacionesIva PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_LiquidacionesIva_Ejercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "Trimestre BYTE NOT NULL, Prorrata DECIMAL(9,6), ResultadoCalculado CURRENCY NOT NULL, ImporteLiquidado CURRENCY NOT NULL, " +
            "Compensado CURRENCY NOT NULL, Ajuste CURRENCY NOT NULL, Motivo TEXT(255), IdAsiento LONG, Operador TEXT(100), FechaHoraConfirmacion DATETIME NOT NULL)"),
        new CrearIndice("LiquidacionesIva", "UX_LiquidacionesIva_Trimestre",
            "CREATE UNIQUE INDEX UX_LiquidacionesIva_Trimestre ON LiquidacionesIva (IdSociedadEjercicio, Trimestre)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaHaciendaAcreedoraIva", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaHaciendaDeudoraIva", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaAjustesNegativosIva", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaAjustesPositivosIva", "TEXT(10)"))
    {
        Descripciones =
        [
            new("LiquidacionesIva", null, "Liquidación del IVA de un trimestre del ejercicio: lo calculado desde los libros, lo liquidado y su asiento (lote contabilidad/procesos/liquidacion-iva)."),
            new("LiquidacionesIva", "IdLiquidacionIva", "Identificador de la liquidación."),
            new("LiquidacionesIva", "IdSociedadEjercicio", "Ejercicio liquidado."),
            new("LiquidacionesIva", "Trimestre", "Trimestre liquidado, de 1 a 4."),
            new("LiquidacionesIva", "Prorrata", "Prorrata provisional usada, en tanto por uno (0,15 = 15 %)."),
            new("LiquidacionesIva", "ResultadoCalculado", "Resultado del 303 calculado desde los libros de IVA: positivo a ingresar, negativo a compensar, antes de compensar trimestres anteriores."),
            new("LiquidacionesIva", "ImporteLiquidado", "Resultado con el que se liquidó: el calculado o el que fijó el operador para ajustarlo a lo presentado."),
            new("LiquidacionesIva", "Compensado", "Crédito de la 4700 de trimestres anteriores aplicado en esta liquidación."),
            new("LiquidacionesIva", "Ajuste", "Línea de ajuste del asiento: positiva a la 6391 (sale a pagar menos), negativa a la 6341 (sale a pagar más)."),
            new("LiquidacionesIva", "Motivo", "Motivo que dio el operador al confirmar (obligatorio con ajuste o en un trimestre cerrado)."),
            new("LiquidacionesIva", "IdAsiento", "Asiento de la liquidación."),
            new("LiquidacionesIva", "Operador", "Operador que confirmó la liquidación."),
            new("LiquidacionesIva", "FechaHoraConfirmacion", "Fecha y hora de la confirmación."),
            new("SociedadesEjercicios", "CuentaHaciendaAcreedoraIva", "Cuenta del IVA a ingresar en la liquidación (vacía: 4750000)."),
            new("SociedadesEjercicios", "CuentaHaciendaDeudoraIva", "Cuenta del IVA a compensar o devolver en la liquidación (vacía: 4700000)."),
            new("SociedadesEjercicios", "CuentaAjustesNegativosIva", "Cuenta del IVA no deducible y de los ajustes a pagar más en la liquidación (vacía: 6341000)."),
            new("SociedadesEjercicios", "CuentaAjustesPositivosIva", "Cuenta de los ajustes a pagar menos en la liquidación (vacía: 6391000)."),
        ],
    };

    /// <summary>
    /// Prorrata definitiva del ejercicio (contabilidad/procesos/prorrata 2026-11-17): SociedadesEjercicios.PorcentajeProrrataDefinitiva
    /// (en porcentaje, vacía mientras no se fije; con ella el 303 del 4T lleva la regularización) y la cuenta de la regularización
    /// (vacía: 4729001). Solo estructura, sin datos.
    /// </summary>
    public static Migracion ProrrataDefinitiva { get; } = new(
        "20261117_1100_PRORRATA_DEFINITIVA", 261117011,
        "Prorrata definitiva del ejercicio y cuenta de su regularización.",
        "contabilidad/procesos/prorrata",
        new AnadirColumna("SociedadesEjercicios", "PorcentajeProrrataDefinitiva", "DECIMAL(9,4)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaRegularizacionProrrata", "TEXT(10)"))
    {
        Descripciones =
        [
            new("SociedadesEjercicios", "PorcentajeProrrataDefinitiva", "Prorrata definitiva del ejercicio en porcentaje (20 = 20 %), la que se regulariza en el 4T; vacía mientras no se fije. Se copia como prorrata del año anterior del ejercicio siguiente."),
            new("SociedadesEjercicios", "CuentaRegularizacionProrrata", "Cuenta de la regularización anual de la prorrata (vacía: 4729001); la salda la liquidación del IVA del 4T."),
        ],
    };

    /// <summary>
    /// Impuesto sobre Sociedades del ejercicio (contabilidad/procesos/impuesto-sociedades 2026-11-17): una fila por ejercicio con el
    /// cálculo confirmado (como la hoja de la gestoría) y su asiento, sus líneas de ajuste, los parámetros del IS en SociedadesEjercicios
    /// (tipo general, tramo reducido, bonificación por alquiler de viviendas, prefijos de posibles ajustes y cuentas), la marca
    /// Actividades.BonificacionIS, lo deducido de cada 473, las cifras importadas de los datos fiscales de la AEAT y la dirección de la
    /// Sede para descargarlos (Configuracion.UrlDatosFiscalesIS). Solo estructura, sin datos.
    /// </summary>
    public static Migracion ImpuestoSociedades { get; } = new(
        "20261117_1200_IMPUESTO_SOCIEDADES", 261117012,
        "Impuesto sobre Sociedades del ejercicio: cálculo confirmado, ajustes, retenciones, datos fiscales de la AEAT, parámetros y cuentas.",
        "contabilidad/procesos/impuesto-sociedades",
        new CrearTabla("ImpuestosSociedades",
            "CREATE TABLE ImpuestosSociedades (IdImpuestoSociedades COUNTER CONSTRAINT PK_ImpuestosSociedades PRIMARY KEY, " +
            "IdSociedadEjercicio LONG NOT NULL CONSTRAINT FK_ImpuestosSociedades_Ejercicio REFERENCES SociedadesEjercicios (IdSociedadEjercicio), " +
            "ResultadoContable CURRENCY NOT NULL, AjustesAumentos CURRENCY NOT NULL, AjustesDisminuciones CURRENCY NOT NULL, BasesNegativas CURRENCY NOT NULL, " +
            "BaseImponible CURRENCY NOT NULL, TipoGeneral DECIMAL(9,4) NOT NULL, LimiteTramoReducido CURRENCY, TipoTramoReducido DECIMAL(9,4), " +
            "CuotaIntegra CURRENCY NOT NULL, Bonificaciones CURRENCY NOT NULL, Deducciones CURRENCY NOT NULL, CuotaCalculada CURRENCY NOT NULL, " +
            "CuotaLiquida CURRENCY NOT NULL, Retenciones CURRENCY NOT NULL, CreditoPerdidas CURRENCY NOT NULL, Resultado CURRENCY NOT NULL, " +
            "Motivo TEXT(255), IdAsiento LONG, Operador TEXT(100), FechaHoraConfirmacion DATETIME NOT NULL, SaldoRetenciones CURRENCY NOT NULL, " +
            "BasesNegativasAeat CURRENCY, ResultadoPresentadoAeat CURRENCY, RentaViviendas CURRENCY NOT NULL, RentaViviendasCalculada CURRENCY NOT NULL)"),
        new CrearIndice("ImpuestosSociedades", "UX_ImpuestosSociedades_Ejercicio",
            "CREATE UNIQUE INDEX UX_ImpuestosSociedades_Ejercicio ON ImpuestosSociedades (IdSociedadEjercicio)"),
        new CrearTabla("ImpuestosSociedadesAjustes",
            "CREATE TABLE ImpuestosSociedadesAjustes (IdImpuestoSociedadesAjuste COUNTER CONSTRAINT PK_ImpuestosSociedadesAjustes PRIMARY KEY, " +
            "IdImpuestoSociedades LONG NOT NULL CONSTRAINT FK_ImpuestosSociedadesAjustes_Impuesto REFERENCES ImpuestosSociedades (IdImpuestoSociedades), " +
            "Orden LONG NOT NULL, Temporal YESNO NOT NULL, Aumento YESNO NOT NULL, Descripcion TEXT(255) NOT NULL, Cuenta TEXT(10), Importe CURRENCY NOT NULL)"),
        new CrearIndice("ImpuestosSociedadesAjustes", "IX_ImpuestosSociedadesAjustes_Impuesto",
            "CREATE INDEX IX_ImpuestosSociedadesAjustes_Impuesto ON ImpuestosSociedadesAjustes (IdImpuestoSociedades, Orden)"),
        new CrearTabla("ImpuestosSociedadesRetenciones",
            "CREATE TABLE ImpuestosSociedadesRetenciones (IdImpuestoSociedadesRetencion COUNTER CONSTRAINT PK_ImpuestosSociedadesRetenciones PRIMARY KEY, " +
            "IdImpuestoSociedades LONG NOT NULL CONSTRAINT FK_ImpuestosSociedadesRetenciones_Impuesto REFERENCES ImpuestosSociedades (IdImpuestoSociedades), " +
            "Clave TEXT(80) NOT NULL, Pagador TEXT(120) NOT NULL, Nif TEXT(20), Cuenta TEXT(10), Hacienda CURRENCY, Contable CURRENCY NOT NULL, Propuesto CURRENCY NOT NULL, " +
            "Valor CURRENCY NOT NULL, Escrito YESNO NOT NULL, Justificacion TEXT(255))"),
        new CrearIndice("ImpuestosSociedadesRetenciones", "IX_ImpuestosSociedadesRetenciones_Impuesto",
            "CREATE INDEX IX_ImpuestosSociedadesRetenciones_Impuesto ON ImpuestosSociedadesRetenciones (IdImpuestoSociedades, Clave)"),
        new CrearTabla("ImpuestosSociedadesDatosAeat",
            "CREATE TABLE ImpuestosSociedadesDatosAeat (IdImpuestoSociedadesDatoAeat COUNTER CONSTRAINT PK_ImpuestosSociedadesDatosAeat PRIMARY KEY, " +
            "IdImpuestoSociedades LONG NOT NULL CONSTRAINT FK_ImpuestosSociedadesDatosAeat_Impuesto REFERENCES ImpuestosSociedades (IdImpuestoSociedades), " +
            "Orden LONG NOT NULL, Modelo TEXT(10) NOT NULL, Nif TEXT(20), Nombre TEXT(120) NOT NULL, Importe CURRENCY NOT NULL, Cuenta TEXT(10))"),
        new CrearIndice("ImpuestosSociedadesDatosAeat", "IX_ImpuestosSociedadesDatosAeat_Impuesto",
            "CREATE INDEX IX_ImpuestosSociedadesDatosAeat_Impuesto ON ImpuestosSociedadesDatosAeat (IdImpuestoSociedades, Orden)"),
        new AnadirColumna("Configuracion", "UrlDatosFiscalesIS", "TEXT(255)"),
        new AnadirColumna("SociedadesEjercicios", "TipoImpuestoSociedades", "DECIMAL(9,4)"),
        new AnadirColumna("SociedadesEjercicios", "LimiteTramoReducidoIS", "CURRENCY"),
        new AnadirColumna("SociedadesEjercicios", "TipoTramoReducidoIS", "DECIMAL(9,4)"),
        new AnadirColumna("SociedadesEjercicios", "BonificacionViviendasIS", "DECIMAL(9,4)"),
        new AnadirColumna("SociedadesEjercicios", "PrefijosAjustesIS", "TEXT(255)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaImpuestoSociedades", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaHaciendaAcreedoraIS", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaHaciendaDeudoraIS", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaCreditoPerdidasIS", "TEXT(10)"),
        new AnadirColumna("SociedadesEjercicios", "CuentaImpuestoDiferidoIS", "TEXT(10)"),
        new AnadirColumna("Actividades", "BonificacionIS", "YESNO"))
    {
        Descripciones =
        [
            new("ImpuestosSociedades", null, "Impuesto sobre Sociedades confirmado de un ejercicio: el cálculo (como la hoja de la gestoría) y su asiento (lote contabilidad/procesos/impuesto-sociedades)."),
            new("ImpuestosSociedades", "IdImpuestoSociedades", "Identificador del cálculo."),
            new("ImpuestosSociedades", "IdSociedadEjercicio", "Ejercicio del impuesto (uno por ejercicio)."),
            new("ImpuestosSociedades", "ResultadoContable", "Resultado contable antes del impuesto: grupos 6 y 7 sin las 630, 633 y 638, sin regularización ni cierre (positivo, beneficio)."),
            new("ImpuestosSociedades", "AjustesAumentos", "Suma de los ajustes (permanentes y temporales) que aumentan el resultado."),
            new("ImpuestosSociedades", "AjustesDisminuciones", "Suma de los ajustes (permanentes y temporales) que lo disminuyen."),
            new("ImpuestosSociedades", "BasesNegativas", "Compensación de bases imponibles negativas de ejercicios anteriores."),
            new("ImpuestosSociedades", "BaseImponible", "Base imponible: resultado + aumentos − disminuciones − bases negativas (negativa si hay pérdida fiscal)."),
            new("ImpuestosSociedades", "TipoGeneral", "Tipo general aplicado, en porcentaje (25 = 25 %)."),
            new("ImpuestosSociedades", "LimiteTramoReducido", "Base hasta la que se aplicó el tipo reducido; vacía sin tramo."),
            new("ImpuestosSociedades", "TipoTramoReducido", "Tipo del tramo reducido, en porcentaje; vacío sin tramo."),
            new("ImpuestosSociedades", "CuotaIntegra", "Cuota íntegra: la base por los tipos de sus tramos (0 si la base no es positiva)."),
            new("ImpuestosSociedades", "Bonificaciones", "Bonificaciones aplicadas (la del alquiler de viviendas)."),
            new("ImpuestosSociedades", "Deducciones", "Deducciones aplicadas (doble imposición, inversión…)."),
            new("ImpuestosSociedades", "CuotaCalculada", "Cuota líquida calculada: cuota íntegra − bonificaciones − deducciones, nunca negativa."),
            new("ImpuestosSociedades", "CuotaLiquida", "Cuota líquida contabilizada en la 6300: la calculada o la que fijó el operador."),
            new("ImpuestosSociedades", "Retenciones", "Retenciones y pagos a cuenta deducidos: lo que va al Haber de las 473 (detalle en ImpuestosSociedadesRetenciones)."),
            new("ImpuestosSociedades", "SaldoRetenciones", "Suma de los saldos deudores de las 473 a 31/12 al confirmar; si cambia, el impuesto queda desactualizado."),
            new("ImpuestosSociedades", "BasesNegativasAeat", "Bases negativas pendientes de compensar según los datos fiscales de la AEAT; vacía si no se importaron ni escribieron."),
            new("ImpuestosSociedades", "ResultadoPresentadoAeat", "Resultado de la declaración ya presentada (casilla 621 del modelo 200) según los datos fiscales de la AEAT."),
            new("ImpuestosSociedades", "CreditoPerdidas", "Crédito por pérdidas aplicado (6301 al Debe y 4745 al Haber)."),
            new("ImpuestosSociedades", "Resultado", "Cuota líquida − retenciones: positivo a ingresar (4752), negativo a devolver (4709)."),
            new("ImpuestosSociedades", "Motivo", "Motivo que dio el operador al confirmar (obligatorio si se repite, si la cuota no es la calculada o antes de terminar el ejercicio)."),
            new("ImpuestosSociedades", "IdAsiento", "Asiento del impuesto."),
            new("ImpuestosSociedades", "Operador", "Operador que lo confirmó."),
            new("ImpuestosSociedades", "FechaHoraConfirmacion", "Fecha y hora de la confirmación."),
            new("ImpuestosSociedadesAjustes", null, "Líneas de ajuste al resultado contable de un cálculo del Impuesto sobre Sociedades, como las escribió el operador."),
            new("ImpuestosSociedadesAjustes", "IdImpuestoSociedadesAjuste", "Identificador de la línea."),
            new("ImpuestosSociedadesAjustes", "IdImpuestoSociedades", "Cálculo al que pertenece."),
            new("ImpuestosSociedadesAjustes", "Orden", "Orden de la línea en la pantalla."),
            new("ImpuestosSociedadesAjustes", "Temporal", "Sí: ajuste temporal; no: permanente."),
            new("ImpuestosSociedadesAjustes", "Aumento", "Sí: aumenta el resultado; no: lo disminuye."),
            new("ImpuestosSociedadesAjustes", "Descripcion", "Texto del ajuste (por ejemplo «Multas y sanciones» o «Ajuste art. 21.3»)."),
            new("ImpuestosSociedadesAjustes", "Cuenta", "Cuenta de la que sale, si se añadió desde los posibles ajustes."),
            new("ImpuestosSociedadesAjustes", "Importe", "Importe del ajuste, positivo."),
            new("ImpuestosSociedadesRetenciones", null, "Contraste de retenciones de un cálculo del Impuesto sobre Sociedades: por pagador, Hacienda frente a contabilidad y el valor usado."),
            new("ImpuestosSociedadesRetenciones", "IdImpuestoSociedadesRetencion", "Identificador de la línea."),
            new("ImpuestosSociedadesRetenciones", "IdImpuestoSociedades", "Cálculo al que pertenece."),
            new("ImpuestosSociedadesRetenciones", "Clave", "Clave de la fila: «cuenta|NIF o pagador» si la contabilidad la tiene, «AEAT|modelo|pagador» si solo la conoce Hacienda."),
            new("ImpuestosSociedadesRetenciones", "Pagador", "Pagador (o la 473 si sus apuntes no tienen pagador)."),
            new("ImpuestosSociedadesRetenciones", "Nif", "NIF del pagador, si se conoce."),
            new("ImpuestosSociedadesRetenciones", "Cuenta", "Subcuenta 473 a cuyo Haber va el valor; vacía si no se eligió."),
            new("ImpuestosSociedadesRetenciones", "Hacienda", "Lo que dicen los datos fiscales de la AEAT; vacío sin datos fiscales."),
            new("ImpuestosSociedadesRetenciones", "Contable", "Lo contabilizado en la 473 para ese pagador (Debe − Haber de sus apuntes)."),
            new("ImpuestosSociedadesRetenciones", "Propuesto", "Lo propuesto: lo de Hacienda o, sin datos fiscales, lo contabilizado."),
            new("ImpuestosSociedadesRetenciones", "Valor", "Valor usado en el IS (Haber de la 473 en el asiento); lo que no se usa se queda en la cuenta."),
            new("ImpuestosSociedadesRetenciones", "Escrito", "Sí si el operador escribió o copió el valor en lugar del propuesto."),
            new("ImpuestosSociedadesRetenciones", "Justificacion", "Por qué el valor no es el de Hacienda (por ejemplo, el certificado del pagador)."),
            new("ImpuestosSociedades", "RentaViviendas", "Renta de viviendas aplicada en la bonificación (la calculada o la escrita)."),
            new("ImpuestosSociedades", "RentaViviendasCalculada", "Renta de viviendas calculada: ingresos − gastos directos − indirectos en proporción a los ingresos ordinarios."),
            new("ImpuestosSociedadesDatosAeat", null, "Retenciones y pagos a cuenta de los datos fiscales de la AEAT usados en un cálculo del Impuesto sobre Sociedades (sin el fichero)."),
            new("ImpuestosSociedadesDatosAeat", "IdImpuestoSociedadesDatoAeat", "Identificador de la línea."),
            new("ImpuestosSociedadesDatosAeat", "IdImpuestoSociedades", "Cálculo al que pertenece."),
            new("ImpuestosSociedadesDatosAeat", "Orden", "Orden de la línea."),
            new("ImpuestosSociedadesDatosAeat", "Modelo", "Modelo que la informa: 202 (pago fraccionado), 193 (capital mobiliario), 180 (alquiler de locales)…"),
            new("ImpuestosSociedadesDatosAeat", "Nif", "NIF del pagador, si lo trae."),
            new("ImpuestosSociedadesDatosAeat", "Nombre", "Nombre del pagador o concepto."),
            new("ImpuestosSociedadesDatosAeat", "Importe", "Retención o pago a cuenta según la AEAT."),
            new("ImpuestosSociedadesDatosAeat", "Cuenta", "Subcuenta 473 a la que corresponde; vacía si está sin asignar (no se deduce)."),
            new("Configuracion", "UrlDatosFiscalesIS", "Página de la Sede de la AEAT con el enlace a los datos fiscales del Impuesto sobre Sociedades (vacía: la de siempre)."),
            new("SociedadesEjercicios", "TipoImpuestoSociedades", "Tipo general del Impuesto sobre Sociedades del ejercicio, en porcentaje (vacío: 25 %)."),
            new("SociedadesEjercicios", "LimiteTramoReducidoIS", "Base imponible hasta la que se aplica el tipo reducido del IS (vacía: sin tramo, todo al general)."),
            new("SociedadesEjercicios", "TipoTramoReducidoIS", "Tipo del tramo reducido del IS, en porcentaje."),
            new("SociedadesEjercicios", "BonificacionViviendasIS", "Bonificación del IS por alquiler de viviendas, en porcentaje de la cuota que les corresponde (vacía: sin bonificación)."),
            new("SociedadesEjercicios", "PrefijosAjustesIS", "Prefijos de las cuentas que se listan como posibles ajustes fiscales del IS, separados por comas (vacío: 678, 669, 778, 760, 766, 666)."),
            new("SociedadesEjercicios", "CuentaImpuestoSociedades", "Cuenta del gasto por impuesto corriente (vacía: 6300000)."),
            new("SociedadesEjercicios", "CuentaHaciendaAcreedoraIS", "Cuenta del IS a ingresar (vacía: 4752000)."),
            new("SociedadesEjercicios", "CuentaHaciendaDeudoraIS", "Cuenta del IS a devolver (vacía: 4709000)."),
            new("SociedadesEjercicios", "CuentaCreditoPerdidasIS", "Cuenta del crédito por pérdidas a compensar (vacía: 4745000)."),
            new("SociedadesEjercicios", "CuentaImpuestoDiferidoIS", "Cuenta del gasto por impuesto diferido al aplicar el crédito por pérdidas (vacía: 6301000)."),
            new("Actividades", "BonificacionIS", "Sus rentas tienen la bonificación del Impuesto sobre Sociedades por alquiler de viviendas; su renta neta propone la bonificación."),
        ],
    };

    /// <summary>
    /// Cuenta de resultados del ejercicio (contabilidad/procesos/cierre-ejercicio 2026-11-17): SociedadesEjercicios.CuentaResultadoEjercicio,
    /// la subcuenta contra la que la regularización salda los grupos 6 y 7 (vacía: 1290000). Solo estructura, sin datos.
    /// </summary>
    public static Migracion CuentaResultadoEjercicio { get; } = new(
        "20261117_1500_CUENTA_RESULTADO_EJERCICIO", 261117015,
        "Cuenta de resultados del ejercicio, la de la regularización del cierre.",
        "contabilidad/procesos/cierre-ejercicio",
        new AnadirColumna("SociedadesEjercicios", "CuentaResultadoEjercicio", "TEXT(10)"))
    {
        Descripciones =
        [
            new("SociedadesEjercicios", "CuentaResultadoEjercicio", "Subcuenta del resultado del ejercicio: la regularización del cierre salda contra ella los grupos 6 y 7 (vacía: 1290000)."),
        ],
    };

    private static PasoPersonalizado RelacionActividad()
    {
        const string sql = "ALTER TABLE CentrosCoste ADD CONSTRAINT FK_CentrosCoste_Actividad FOREIGN KEY (IdActividad) REFERENCES Actividades (IdActividad)";
        return new PasoPersonalizado("Relacionar CentrosCoste.IdActividad con Actividades", sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, "CentrosCoste", "FK_CentrosCoste_Actividad")),
            (s, _) => s.EjecutarAsync(sql));
    }
}
