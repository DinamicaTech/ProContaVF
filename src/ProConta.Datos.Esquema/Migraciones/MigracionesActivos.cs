using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por el nodo activos (docs/activos.md).</summary>
public static class MigracionesActivos
{
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

    /// <summary>
    /// Enlaces de ventas con activos (activos 2026-11-17 20:35; facturacion/factura-venta y facturacion/programadas): las líneas de factura de venta
    /// y de factura programada guardan su activo (IdActivo, opcional). Relleno inicial, una vez: la línea toma el activo cuando su cuenta es una
    /// subcuenta de un solo activo propio o, si no, cuando su centro de coste es el de un solo activo propio; las demás quedan sin activo
    /// (<see cref="RellenoActivoLineas"/>).
    /// </summary>
    public static Migracion LineasVentaActivo { get; } = new(
        "20261117_2035_ACTIVO_LINEAS_VENTA", 261117021,
        "Activo de cada línea de factura de venta y de factura programada, con relleno inicial por subcuenta o centro de coste.",
        "activos",
        new AnadirColumna("LineasFactura", "IdActivo", "LONG"),
        new CrearIndice("LineasFactura", "IX_LineasFactura_Activo", "CREATE INDEX IX_LineasFactura_Activo ON LineasFactura (IdActivo)"),
        Relacion("LineasFactura", "FK_LineasFactura_Activo", "ALTER TABLE LineasFactura ADD CONSTRAINT FK_LineasFactura_Activo FOREIGN KEY (IdActivo) REFERENCES Activos (IdActivo)"),
        new AnadirColumna("LineasFacturasProgramadas", "IdActivo", "LONG"),
        new CrearIndice("LineasFacturasProgramadas", "IX_LineasFacturasProgramadas_Activo",
            "CREATE INDEX IX_LineasFacturasProgramadas_Activo ON LineasFacturasProgramadas (IdActivo)"),
        Relacion("LineasFacturasProgramadas", "FK_LineasFacturasProgramadas_Activo",
            "ALTER TABLE LineasFacturasProgramadas ADD CONSTRAINT FK_LineasFacturasProgramadas_Activo FOREIGN KEY (IdActivo) REFERENCES Activos (IdActivo)"),
        new PasoPersonalizado(
            "Rellenar el activo de las líneas de factura y de programada por subcuenta de un solo activo o, si no, por centro de un solo activo",
            "UPDATE LineasFactura/LineasFacturasProgramadas SET IdActivo = subcuenta de un solo activo propio; si no, centro de un solo activo propio",
            // Hecho si alguna línea ya tiene activo o si el relleno no tiene nada que poner: después el operador puede quitar el activo de una
            // línea a propósito sin que la migración parezca a medias.
            async (s, e) => e.ExisteColumna("LineasFactura", "IdActivo") && e.ExisteColumna("LineasFacturasProgramadas", "IdActivo") &&
                (await RellenoActivoLineas.HayAlgunoAsync(s) || (await RellenoActivoLineas.ProponerAsync(s)).Count == 0),
            async (s, _) => await RellenoActivoLineas.AplicarAsync(s)))
    {
        Descripciones =
        [
            new("LineasFactura", "IdActivo", "Activo al que corresponde la línea (opcional): lo elige el operador en el editor, lo pone «Facturar ventas…» con el de la venta o lo copia la programada. Lo usa el apartado «Ventas» de la ficha del activo."),
            new("LineasFacturasProgramadas", "IdActivo", "Activo al que corresponde la línea (opcional); las facturas que genera la programada lo copian."),
        ],
    };

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

/// <summary>
/// Relleno inicial del activo de las líneas de venta (activos 2026-11-17 20:35). Una línea toma el activo cuando su cuenta es una subcuenta de un
/// solo activo propio (no ajeno; también los dados de baja) o, si no, cuando su centro de coste es el de un solo activo propio. Las cuentas se
/// buscan por código en todos los ejercicios de la sociedad, porque una programada vive varios ejercicios. Solo toca líneas sin activo.
/// </summary>
public static class RellenoActivoLineas
{
    /// <summary>La regla: subcuenta de un solo activo; si no, centro de un solo activo; si no, ninguno.</summary>
    public static int? Elegir(IReadOnlyCollection<int> activosDeLaCuenta, IReadOnlyCollection<int> activosDelCentro) =>
        activosDeLaCuenta.Count == 1 ? activosDeLaCuenta.First()
        : activosDelCentro.Count == 1 ? activosDelCentro.First()
        : null;

    /// <summary>Línea propuesta: tabla («F» factura, «P» programada), identificador de la línea y activo.</summary>
    public sealed record Propuesta(char Tabla, int IdLinea, int IdActivo);

    internal static async Task<bool> HayAlgunoAsync(ISesionDatos s) =>
        Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM LineasFactura WHERE IdActivo IS NOT NULL"), System.Globalization.CultureInfo.InvariantCulture) > 0 ||
        Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM LineasFacturasProgramadas WHERE IdActivo IS NOT NULL"), System.Globalization.CultureInfo.InvariantCulture) > 0;

    public static async Task<IReadOnlyList<Propuesta>> ProponerAsync(ISesionDatos s)
    {
        var porCuenta = new Dictionary<(string Sociedad, string Codigo), HashSet<int>>();
        foreach (var f in await s.ConsultarAsync(
                     "SELECT DISTINCT se.IdSociedad, c.Codigo, a.IdActivo FROM ((SubcuentasActivo AS sa INNER JOIN Activos AS a ON sa.IdActivo = a.IdActivo) " +
                     "INNER JOIN CuentasContables AS c ON sa.IdIdentidadContable = c.IdIdentidadContable) INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                     "WHERE a.EsAjeno = False AND se.IdSociedad = a.IdSociedad",
                     r => (Sociedad: (r.Texto("IdSociedad") ?? "").Trim(), Codigo: (r.Texto("Codigo") ?? "").Trim(), Activo: r.Entero("IdActivo"))))
        {
            if (!porCuenta.TryGetValue((f.Sociedad, f.Codigo), out var set)) porCuenta[(f.Sociedad, f.Codigo)] = set = [];
            set.Add(f.Activo);
        }
        var porCentro = (await s.ConsultarAsync("SELECT IdSociedad, IdCentroCoste, IdActivo FROM Activos WHERE EsAjeno = False AND IdCentroCoste IS NOT NULL",
                r => (Sociedad: (r.Texto("IdSociedad") ?? "").Trim(), Centro: r.Entero("IdCentroCoste"), Activo: r.Entero("IdActivo"))))
            .GroupBy(x => (x.Sociedad, x.Centro)).ToDictionary(g => g.Key, g => (IReadOnlyCollection<int>)g.Select(x => x.Activo).Distinct().ToList());

        var lineas = new List<(char Tabla, int Id, string Sociedad, string? Cuenta, int? Centro)>();
        lineas.AddRange((await s.ConsultarAsync(
            "SELECT l.IdLineaFactura AS Id, se.IdSociedad, l.CuentaContable, l.IdCentroCoste FROM (LineasFactura AS l INNER JOIN Facturas AS f ON l.IdFactura = f.IdFactura) " +
            "INNER JOIN SociedadesEjercicios AS se ON f.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE l.IdActivo IS NULL",
            Leer)).Select(x => ('F', x.Id, x.Sociedad, x.Cuenta, x.Centro)));
        lineas.AddRange((await s.ConsultarAsync(
            "SELECT l.IdLineaFacturaProgramada AS Id, se.IdSociedad, l.CuentaContable, l.IdCentroCoste FROM (LineasFacturasProgramadas AS l INNER JOIN FacturasProgramadas AS p " +
            "ON l.IdFacturaProgramada = p.IdFacturaProgramada) INNER JOIN SociedadesEjercicios AS se ON p.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE l.IdActivo IS NULL",
            Leer)).Select(x => ('P', x.Id, x.Sociedad, x.Cuenta, x.Centro)));

        IReadOnlyCollection<int> ninguno = [];
        var propuestas = new List<Propuesta>();
        foreach (var l in lineas)
        {
            IReadOnlyCollection<int> deCuenta = l.Cuenta is { Length: > 0 } c && porCuenta.TryGetValue((l.Sociedad, c), out var a) ? a : ninguno;
            var deCentro = l.Centro is int ce && porCentro.TryGetValue((l.Sociedad, ce), out var b) ? b : ninguno;
            if (Elegir(deCuenta, deCentro) is int activo) propuestas.Add(new Propuesta(l.Tabla, l.Id, activo));
        }
        return propuestas;

        static (int Id, string Sociedad, string? Cuenta, int? Centro) Leer(IFilaDatos r) =>
            (r.Entero("Id"), (r.Texto("IdSociedad") ?? "").Trim(), r.Texto("CuentaContable")?.Trim(), r.EnteroNulo("IdCentroCoste"));
    }

    /// <summary>Aplica el relleno y devuelve cuántas líneas de factura y de programada han tomado activo.</summary>
    public static async Task<(int Facturas, int Programadas)> AplicarAsync(ISesionDatos s)
    {
        var propuestas = await ProponerAsync(s);
        foreach (var p in propuestas)
            await s.EjecutarAsync(p.Tabla == 'F'
                ? "UPDATE LineasFactura SET IdActivo = ? WHERE IdLineaFactura = ? AND IdActivo IS NULL"
                : "UPDATE LineasFacturasProgramadas SET IdActivo = ? WHERE IdLineaFacturaProgramada = ? AND IdActivo IS NULL", p.IdActivo, p.IdLinea);
        return (propuestas.Count(p => p.Tabla == 'F'), propuestas.Count(p => p.Tabla == 'P'));
    }
}
