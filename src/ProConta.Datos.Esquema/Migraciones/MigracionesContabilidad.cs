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
    /// ejercicio con el saldo con que se marcaron, su comentario, operador y fecha, y el umbral de «Reiniciar el cuadre» en la
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
            "CuentaContable TEXT(20) NOT NULL, Saldo CURRENCY NOT NULL, Comentario TEXT(255), Operador TEXT(30) NOT NULL, FechaHora DATETIME NOT NULL)"),
        new CrearIndice("CuadresCuentas", "UX_CuadresCuentas_EjercicioCuenta",
            "CREATE UNIQUE INDEX UX_CuadresCuentas_EjercicioCuenta ON CuadresCuentas (IdSociedadEjercicio, CuentaContable)"),
        new AnadirColumna("Configuracion", "UmbralCuadreMayor", "CURRENCY"));
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

    private static async Task<int> ReglaBalanceAsync(ISesionDatos s, string modelo, string prefijo) => Convert.ToInt32(await s.EscalarAsync(
        "SELECT COUNT(*) FROM (ReglasCuentasBalance AS r INNER JOIN NodosBalance AS n ON r.IdNodoBalance = n.IdNodoBalance) INNER JOIN ModelosBalance AS m ON n.IdModeloBalance = m.IdModeloBalance " +
        "WHERE m.Codigo = ? AND r.PrefijoCuenta = ?", modelo, prefijo), System.Globalization.CultureInfo.InvariantCulture);

    private static PasoPersonalizado RelacionActividad()
    {
        const string sql = "ALTER TABLE CentrosCoste ADD CONSTRAINT FK_CentrosCoste_Actividad FOREIGN KEY (IdActividad) REFERENCES Actividades (IdActividad)";
        return new PasoPersonalizado("Relacionar CentrosCoste.IdActividad con Actividades", sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, "CentrosCoste", "FK_CentrosCoste_Actividad")),
            (s, _) => s.EjecutarAsync(sql));
    }
}
