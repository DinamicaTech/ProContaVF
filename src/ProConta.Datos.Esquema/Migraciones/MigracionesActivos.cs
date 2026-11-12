using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por el nodo activos (docs/activos.md).</summary>
public static class MigracionesActivos
{
    /// <summary>
    /// Fase 1 del módulo de activos (activos, decisiones 2026-11-11 18:21 y 2026-11-12 09:47 y 10:00):
    /// <list type="bullet">
    /// <item>tipos de activo según el PGC (vivienda, local, parking, piso turístico, vehículo, instalación técnica y
    /// aplicación informática); los heredados INMUEBLE y TECNICO quedan inactivos y la INVERSION sigue para la segunda fase;</item>
    /// <item>catálogo TiposSubcuentaActivo (nombre con [TOKEN] y rol) y líneas PlantillasSubcuentasActivo por tipo de activo;</item>
    /// <item>SubcuentasActivo: las subcuentas generadas de cada activo y línea de plantilla;</item>
    /// <item>ActivosInmuebles: la ficha del inmueble (catastro, % de construcción, compra, alquiler y tributo);</item>
    /// <item>en Activos, un único centro de coste y la marca «ajeno» (solo informativo); en el plan, el % anual.</item>
    /// </list>
    /// Es aditiva: PlantillasCuentasActivo y CuentasActivo, y Activos.IdDepartamento/IdSubDepartamento, quedan obsoletos pero
    /// no se borran, porque las copias de pruebas antiguas (p. ej. la v18) tienen datos en ellos; se retirarán cuando se
    /// regenere la BD.
    /// </summary>
    public static Migracion Estructura { get; } = new(
        "20261112_1030_ACTIVOS_ESTRUCTURA", 261112001,
        "Activos: tipos según el PGC, catálogo y plantillas de subcuentas con rol, subcuentas por activo, ficha del inmueble, centro de coste, activo ajeno y % de amortización.",
        "activos",
        new PasoPersonalizado(
            "Dar de alta los tipos de activo según el PGC",
            "INSERT INTO TiposActivo (Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo) si falta: " +
            string.Join("; ", TiposPgc.Select(t => $"{t.Codigo}={t.Nombre}/{t.Categoria}")) + "; UPDATE TiposActivo SET EstaActivo = False WHERE Codigo IN (INMUEBLE, TECNICO)",
            async (s, _) => await TiposPgcCompletosAsync(s),
            async (s, _) => await CrearTiposPgcAsync(s)),
        new CrearTabla("TiposSubcuentaActivo",
            "CREATE TABLE TiposSubcuentaActivo (IdTipoSubcuentaActivo COUNTER CONSTRAINT PK_TiposSubcuentaActivo PRIMARY KEY, " +
            "Nombre TEXT(255) NOT NULL, Descripcion TEXT(255), Rol TEXT(30) NOT NULL, EstaActivo YESNO NOT NULL)"),
        new CrearIndice("TiposSubcuentaActivo", "UX_TiposSubcuentaActivo_Nombre",
            "CREATE UNIQUE INDEX UX_TiposSubcuentaActivo_Nombre ON TiposSubcuentaActivo (Nombre)"),
        new CrearTabla("PlantillasSubcuentasActivo",
            "CREATE TABLE PlantillasSubcuentasActivo (IdPlantillaSubcuentaActivo COUNTER CONSTRAINT PK_PlantillasSubcuentasActivo PRIMARY KEY, " +
            "IdTipoActivo LONG NOT NULL CONSTRAINT FK_PlantillasSubcuentasActivo_Tipo REFERENCES TiposActivo (IdTipoActivo), " +
            "IdTipoSubcuentaActivo LONG NOT NULL CONSTRAINT FK_PlantillasSubcuentasActivo_TipoSubcuenta REFERENCES TiposSubcuentaActivo (IdTipoSubcuentaActivo), " +
            "Prefijo TEXT(7) NOT NULL, Orden SHORT NOT NULL, EstaActiva YESNO NOT NULL)"),
        new CrearIndice("PlantillasSubcuentasActivo", "UX_PlantillasSubcuentasActivo_Linea",
            "CREATE UNIQUE INDEX UX_PlantillasSubcuentasActivo_Linea ON PlantillasSubcuentasActivo (IdTipoActivo, IdTipoSubcuentaActivo)"),
        new CrearIndice("PlantillasSubcuentasActivo", "IX_PlantillasSubcuentasActivo_TipoSubcuenta",
            "CREATE INDEX IX_PlantillasSubcuentasActivo_TipoSubcuenta ON PlantillasSubcuentasActivo (IdTipoSubcuentaActivo)"),
        new CrearTabla("SubcuentasActivo",
            "CREATE TABLE SubcuentasActivo (IdSubcuentaActivo COUNTER CONSTRAINT PK_SubcuentasActivo PRIMARY KEY, " +
            "IdActivo LONG NOT NULL CONSTRAINT FK_SubcuentasActivo_Activo REFERENCES Activos (IdActivo), " +
            "IdPlantillaSubcuentaActivo LONG NOT NULL CONSTRAINT FK_SubcuentasActivo_Plantilla REFERENCES PlantillasSubcuentasActivo (IdPlantillaSubcuentaActivo), " +
            "IdIdentidadContable LONG NOT NULL CONSTRAINT FK_SubcuentasActivo_Identidad REFERENCES IdentidadesContables (IdIdentidadContable), " +
            "FechaHoraCreacion DATETIME NOT NULL)"),
        new CrearIndice("SubcuentasActivo", "UX_SubcuentasActivo_Plantilla",
            "CREATE UNIQUE INDEX UX_SubcuentasActivo_Plantilla ON SubcuentasActivo (IdActivo, IdPlantillaSubcuentaActivo)"),
        new CrearIndice("SubcuentasActivo", "IX_SubcuentasActivo_PlantillaLinea",
            "CREATE INDEX IX_SubcuentasActivo_PlantillaLinea ON SubcuentasActivo (IdPlantillaSubcuentaActivo)"),
        new CrearIndice("SubcuentasActivo", "IX_SubcuentasActivo_Identidad",
            "CREATE INDEX IX_SubcuentasActivo_Identidad ON SubcuentasActivo (IdIdentidadContable)"),
        new CrearTabla("ActivosInmuebles",
            "CREATE TABLE ActivosInmuebles (IdActivo LONG NOT NULL CONSTRAINT PK_ActivosInmuebles PRIMARY KEY, " +
            "ReferenciaCatastral TEXT(30), ValorCatastralSuelo CURRENCY, ValorCatastralConstruccion CURRENCY, ValorCatastralTotal CURRENCY, " +
            "PorcentajeConstruccion DECIMAL(9,6), ValorCompra CURRENCY, CosteCompra CURRENCY, PrecioAlquiler CURRENCY, FechaInicioAlquiler DATETIME, " +
            "TributoMetropolitano CURRENCY, CONSTRAINT FK_ActivosInmuebles_Activo FOREIGN KEY (IdActivo) REFERENCES Activos (IdActivo))"),
        new CrearIndice("ActivosInmuebles", "IX_ActivosInmuebles_ReferenciaCatastral",
            "CREATE INDEX IX_ActivosInmuebles_ReferenciaCatastral ON ActivosInmuebles (ReferenciaCatastral)"),
        new AnadirColumna("Activos", "IdCentroCoste", "LONG"),
        new CrearIndice("Activos", "IX_Activos_CentroCoste", "CREATE INDEX IX_Activos_CentroCoste ON Activos (IdCentroCoste)"),
        Relacion("Activos", "FK_Activos_CentroCoste", "ALTER TABLE Activos ADD CONSTRAINT FK_Activos_CentroCoste FOREIGN KEY (IdCentroCoste) REFERENCES CentrosCoste (IdCentroCoste)"),
        new AnadirColumnaObligatoria("Activos", "EsAjeno", "YESNO", "False"),
        new AnadirColumna("PlanesAmortizacionActivo", "PorcentajeAnual", "DECIMAL(9,6)"),
        new CrearIndice("PlanesAmortizacionActivo", "IX_PlanesAmortizacionActivo_Activo",
            "CREATE INDEX IX_PlanesAmortizacionActivo_Activo ON PlanesAmortizacionActivo (IdActivo)"));

    /// <summary>Tipos de activo según el PGC (activos, decisión 2026-11-12 09:47).</summary>
    public static readonly (string Codigo, string Nombre, string Categoria)[] TiposPgc =
    [
        ("VIVIENDA", "Vivienda", "INMUEBLE"),
        ("LOCAL", "Local", "INMUEBLE"),
        ("PARKING", "Parking", "INMUEBLE"),
        ("TURISTICO", "Piso turístico", "INMUEBLE"),
        ("VEHICULO", "Vehículo", "MOVIL"),
        ("INSTALACION", "Instalación técnica", "MATERIAL"),
        ("APLICACION", "Aplicación informática", "INTANGIBLE"),
    ];

    /// <summary>Tipos heredados que los del PGC sustituyen: quedan inactivos (los activos antiguos los conservan).</summary>
    public static readonly string[] TiposSustituidos = ["INMUEBLE", "TECNICO"];

    private static async Task<bool> TiposPgcCompletosAsync(ISesionDatos s)
    {
        var codigos = (await s.ConsultarAsync("SELECT Codigo, EstaActivo FROM TiposActivo", f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Activo: f.Logico("EstaActivo"))))
            .ToDictionary(x => x.Codigo, x => x.Activo, StringComparer.OrdinalIgnoreCase);
        return TiposPgc.All(t => codigos.ContainsKey(t.Codigo)) && TiposSustituidos.All(t => !codigos.TryGetValue(t, out var activo) || !activo);
    }

    private static async Task CrearTiposPgcAsync(ISesionDatos s)
    {
        var codigos = (await s.ConsultarAsync("SELECT Codigo FROM TiposActivo", f => (f.Texto("Codigo") ?? "").Trim())).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (codigo, nombre, categoria) in TiposPgc.Where(t => !codigos.Contains(t.Codigo)))
            await s.EjecutarAsync("INSERT INTO TiposActivo (Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo) VALUES (?, ?, ?, True, True)", codigo, nombre, categoria);
        foreach (var codigo in TiposSustituidos)
            await s.EjecutarAsync("UPDATE TiposActivo SET EstaActivo = False WHERE Codigo = ?", codigo);
    }

    private static PasoPersonalizado Relacion(string tabla, string nombre, string sql) =>
        new($"Relacionar {tabla} ({nombre})", sql,
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, tabla, nombre)),
            (s, _) => s.EjecutarAsync(sql));
}
