using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de organizacion (docs/datos/esquema.md, Requirements).</summary>
public static class MigracionesOrganizacion
{
    /// <summary>Parámetro del ActualizadorEsquema: accdb del que copiar las formas de pago si el vínculo no funciona.</summary>
    public const string ParametroOrigenFormasPago = "origen-formas-pago";

    private const string Provisional = "FormasPagoMigracion";

    /// <summary>
    /// FormasPago pasa de tabla vinculada al ERP a tabla propia muy simplificada (Id, Descripcion, SEPA),
    /// conservando los códigos. Las filas se copian del vínculo o, si su fichero no está, del accdb
    /// indicado en <see cref="ParametroOrigenFormasPago"/>. Las dos relaciones antiguas hacia el vínculo
    /// (Facturas y Contratos) se quitan y no se recrean: hay valores (el código 1) sin forma de pago.
    /// </summary>
    public static Migracion FormasPagoPropia { get; } = new(
        "20261031_1945_FORMAS_PAGO_PROPIA", 261031001,
        "FormasPago como tabla propia (Id, Descripcion, SEPA) con las filas del vínculo anterior.",
        "organizacion/formas-pago",
        new PasoPersonalizado(
            "Copiar las formas de pago del vínculo a una tabla provisional",
            $"CREATE TABLE {Provisional} (Id BYTE NOT NULL, Descripcion TEXT(100) NOT NULL, SEPA YESNO NOT NULL) <- FormasPago(Codigo, Descripcion); SEPA = descripción contiene «domicili»",
            (_, e) => Task.FromResult(e.ExisteTabla(Provisional) || e.Tabla("FormasPago") is { EsVinculada: false }),
            CopiarAProvisionalAsync),
        new PasoPersonalizado(
            "Quitar las relaciones antiguas hacia el vínculo FormasPago",
            "ALTER TABLE Facturas DROP CONSTRAINT FK_FormasPago_Facturas; ALTER TABLE Contratos DROP CONSTRAINT FK_FormasPago_Contratos",
            (_, e) => Task.FromResult(!TieneRelacion(e, "Facturas", "FK_FormasPago_Facturas") && !TieneRelacion(e, "Contratos", "FK_FormasPago_Contratos")),
            async (s, _) =>
            {
                var e = s.LeerEsquema();
                if (TieneRelacion(e, "Facturas", "FK_FormasPago_Facturas")) await s.EjecutarAsync("ALTER TABLE Facturas DROP CONSTRAINT FK_FormasPago_Facturas");
                if (TieneRelacion(e, "Contratos", "FK_FormasPago_Contratos")) await s.EjecutarAsync("ALTER TABLE Contratos DROP CONSTRAINT FK_FormasPago_Contratos");
            }),
        new QuitarVinculo("FormasPago"),
        new CrearTabla("FormasPago",
            "CREATE TABLE FormasPago (Id BYTE NOT NULL CONSTRAINT PK_FormasPago PRIMARY KEY, Descripcion TEXT(100) NOT NULL, SEPA YESNO NOT NULL)"),
        new PasoPersonalizado(
            "Pasar las formas de pago de la tabla provisional a FormasPago",
            $"INSERT INTO FormasPago (Id, Descripcion, SEPA) SELECT Id, Descripcion, SEPA FROM {Provisional}; DROP TABLE {Provisional}",
            (_, e) => Task.FromResult(!e.ExisteTabla(Provisional)),
            async (s, _) =>
            {
                await s.EjecutarAsync($"INSERT INTO FormasPago (Id, Descripcion, SEPA) SELECT Id, Descripcion, SEPA FROM {Provisional}");
                await s.EjecutarAsync($"DROP TABLE {Provisional}");
            }))
    {
        Precondicion = ComprobarOrigenAsync
    };

    /// <summary>Marca de ejercicio cerrado en SociedadesEjercicios: limita operaciones, no el acceso (RF-SEG-02).</summary>
    public static Migracion EjercicioCerrado { get; } = new(
        "20261031_1946_EJERCICIO_CERRADO", 261031002,
        "SociedadesEjercicios.EjercicioCerrado (sí/no, por defecto no).",
        "organizacion/sociedades-ejercicios",
        new AnadirColumna("SociedadesEjercicios", "EjercicioCerrado", "YESNO NOT NULL DEFAULT No"));

    private static bool TieneRelacion(EsquemaFisico esquema, string tabla, string relacion) =>
        esquema.Tabla(tabla)?.Relaciones.Any(r => string.Equals(r.Nombre, relacion, StringComparison.OrdinalIgnoreCase)) == true;

    private const string Consulta = "SELECT Codigo, Descripcion FROM [FormasPago]";

    /// <summary>Lee las formas de pago del accdb indicado o, si no se indica, del vínculo; null si no hay de dónde.</summary>
    private static async Task<IReadOnlyList<(int Codigo, string Descripcion)>?> LeerOrigenAsync(ISesionDatos s, ContextoMigracion contexto)
    {
        // El origen indicado se lee con su propia conexión de solo lectura (ACE no admite aquí la consulta entre ficheros).
        if (contexto.Parametro(ParametroOrigenFormasPago) is { } origen)
            return File.Exists(origen)
                ? await new BaseDatosAccess(origen, ModoApertura.SoloLectura).EjecutarAsync(o =>
                    o.ConsultarAsync(Consulta, f => (f.Entero("Codigo"), f.Texto("Descripcion") ?? "")))
                : null;
        try
        {
            return await s.ConsultarAsync(Consulta, f => (f.Entero("Codigo"), f.Texto("Descripcion") ?? ""));
        }
        catch (ExcepcionDatos)
        {
            return null;
        }
    }

    private static async Task<Nucleo.Resultados.Error?> ComprobarOrigenAsync(ISesionDatos s, ContextoMigracion contexto)
    {
        if (s.LeerEsquema().Tabla("FormasPago") is { EsVinculada: false }) return null;
        try
        {
            if (await LeerOrigenAsync(s, contexto) is not null) return null;
        }
        catch (ExcepcionDatos ex)
        {
            return ex.Error with { Mensaje = "No puedo leer las formas de pago del fichero indicado; no he cambiado nada." };
        }
        var indicado = contexto.Parametro(ParametroOrigenFormasPago);
        return new Nucleo.Resultados.Error("Esquema.OrigenFormasPago",
            "No puedo convertir FormasPago en tabla propia porque no encuentro de dónde copiar sus filas; no he cambiado nada.",
            "FormasPago",
            indicado is null
                ? "El fichero al que apunta el vínculo FormasPago no está disponible."
                : $"No existe el fichero indicado: {indicado}.",
            $"Vuelve a ejecutar el ActualizadorEsquema indicando el accdb que contiene la tabla FormasPago: --{ParametroOrigenFormasPago} \"<ruta del accdb>\".");
    }

    private static async Task CopiarAProvisionalAsync(ISesionDatos s, ContextoMigracion contexto)
    {
        var filas = await LeerOrigenAsync(s, contexto)
            ?? throw new InvalidOperationException("No encuentro de dónde copiar las formas de pago.");

        if (filas.Any(f => f.Codigo is < 0 or > 255))
            throw new InvalidOperationException("Hay formas de pago con código fuera de 0..255; no caben en la tabla nueva sin cambiar su código.");

        await s.EjecutarAsync($"CREATE TABLE {Provisional} (Id BYTE NOT NULL, Descripcion TEXT(100) NOT NULL, SEPA YESNO NOT NULL)");
        foreach (var (codigo, descripcion) in filas)
        {
            var texto = descripcion.Trim();
            var sepa = texto.Contains("domicili", StringComparison.OrdinalIgnoreCase);
            await s.EjecutarAsync($"INSERT INTO {Provisional} (Id, Descripcion, SEPA) VALUES (?, ?, ?)",
                (byte)codigo, texto.Length > 100 ? texto[..100] : texto, sepa);
        }
    }
}
