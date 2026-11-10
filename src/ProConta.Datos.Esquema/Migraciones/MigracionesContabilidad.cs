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

    private static PasoPersonalizado RelacionActividad()
    {
        const string sql = "ALTER TABLE CentrosCoste ADD CONSTRAINT FK_CentrosCoste_Actividad FOREIGN KEY (IdActividad) REFERENCES Actividades (IdActividad)";
        return new PasoPersonalizado("Relacionar CentrosCoste.IdActividad con Actividades", sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, "CentrosCoste", "FK_CentrosCoste_Actividad")),
            (s, _) => s.EjecutarAsync(sql));
    }
}
