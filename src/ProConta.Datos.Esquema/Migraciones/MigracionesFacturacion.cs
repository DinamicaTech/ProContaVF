using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Amplía una columna de texto si es más corta que la longitud indicada (no la estrecha nunca).</summary>
public sealed class AmpliarTexto(string tabla, string columna, int longitud) : PasoMigracion
{
    public override string Descripcion => $"Ampliar {tabla}.{columna} a {longitud} caracteres";
    public override string Firma => $"ALTER TABLE [{tabla}] ALTER COLUMN [{columna}] TEXT({longitud})";
    public override Task<bool> YaAplicadoAsync(ISesionDatos sesion, EsquemaFisico esquema) =>
        Task.FromResult(esquema.Tabla(tabla)?.Columna(columna) is { Longitud: int l } && l >= longitud);
    public override Task AplicarAsync(ISesionDatos sesion, ContextoMigracion contexto) => sesion.EjecutarAsync(Firma);
}

/// <summary>Migraciones pedidas por los nodos de facturación, terceros/roles y seguridad/permisos para la facturación de venta.</summary>
public static class MigracionesFacturacion
{
    /// <summary>
    /// Conceptos por sociedad (facturacion/conceptos 2026-10-31 16:54): Conceptos.IdSociedad (la sociedad de su
    /// ejercicio) y Conceptos.IdIdentidadContable (la identidad de su cuenta de venta en ese ejercicio, que da la
    /// continuidad entre ejercicios aunque cambie el código). IdSociedadEjercicio se conserva como el ejercicio en
    /// que se creó el concepto y CuentaContable (7 caracteres) queda como dato heredado: el programa ya no la usa.
    /// Se conservan los IdConcepto, así que las líneas antiguas siguen apuntando bien.
    /// </summary>
    public static Migracion ConceptosSociedad { get; } = new(
        "20261101_1630_CONCEPTOS_SOCIEDAD", 261101004,
        "Conceptos por sociedad con la cuenta de venta como identidad contable.",
        "facturacion/conceptos",
        new AnadirColumna("Conceptos", "IdSociedad", "TEXT(3)"),
        new AnadirColumna("Conceptos", "IdIdentidadContable", "LONG"),
        new PasoPersonalizado(
            "Poner a cada concepto la sociedad de su ejercicio",
            "UPDATE Conceptos INNER JOIN SociedadesEjercicios ON Conceptos.IdSociedadEjercicio = SociedadesEjercicios.IdSociedadEjercicio SET Conceptos.IdSociedad = SociedadesEjercicios.IdSociedad WHERE Conceptos.IdSociedad IS NULL",
            async (s, _) => Convert.ToInt32(await s.EscalarAsync(
                "SELECT COUNT(*) FROM Conceptos AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE c.IdSociedad IS NULL AND se.IdSociedad IS NOT NULL")) == 0,
            (s, _) => s.EjecutarAsync(
                "UPDATE Conceptos INNER JOIN SociedadesEjercicios ON Conceptos.IdSociedadEjercicio = SociedadesEjercicios.IdSociedadEjercicio SET Conceptos.IdSociedad = SociedadesEjercicios.IdSociedad WHERE Conceptos.IdSociedad IS NULL")),
        new PasoPersonalizado(
            "Poner a cada concepto la identidad contable de su cuenta de venta",
            "UPDATE Conceptos SET IdIdentidadContable = (identidad de la cuenta CuentaContable en su ejercicio) WHERE IdIdentidadContable IS NULL",
            async (s, _) => (await PendientesIdentidadAsync(s)).Count == 0,
            async (s, _) =>
            {
                foreach (var (idConcepto, identidad) in await PendientesIdentidadAsync(s))
                    await s.EjecutarAsync("UPDATE Conceptos SET IdIdentidadContable = ? WHERE IdConcepto = ?", identidad, idConcepto);
            }),
        new CrearIndice("Conceptos", "IX_Conceptos_Sociedad", "CREATE INDEX IX_Conceptos_Sociedad ON Conceptos (IdSociedad)"));

    /// <summary>Conceptos sin identidad cuya cuenta existe en su ejercicio, con la identidad que les corresponde.</summary>
    private static Task<IReadOnlyList<(int IdConcepto, int Identidad)>> PendientesIdentidadAsync(ISesionDatos s) =>
        s.ConsultarAsync(
            "SELECT c.IdConcepto, cc.IdIdentidadContable FROM Conceptos AS c INNER JOIN CuentasContables AS cc " +
            "ON (c.IdSociedadEjercicio = cc.IdSociedadEjercicio AND c.CuentaContable = cc.Codigo) WHERE c.IdIdentidadContable IS NULL",
            f => (f.Entero("IdConcepto"), f.Entero("IdIdentidadContable")));

    /// <summary>Conceptos con el mismo nombre en una misma sociedad (venían de ejercicios distintos), para decidir si se unen.</summary>
    public static Task<IReadOnlyList<(string IdSociedad, string Nombre, int Veces)>> ConceptosRepetidosAsync(ISesionDatos s) =>
        s.ConsultarAsync(
            "SELECT IdSociedad, Nombre, COUNT(*) AS Veces FROM Conceptos GROUP BY IdSociedad, Nombre HAVING COUNT(*) > 1 ORDER BY IdSociedad, Nombre",
            f => (f.Texto("IdSociedad") ?? "", f.Texto("Nombre") ?? "", f.Entero("Veces")));

    /// <summary>
    /// Factura de venta (facturacion/factura-venta): instantánea del domicilio fiscal y el email del cliente al validar
    /// (definición de la referencia 0.24, con el email a 254 como Terceros.Email), marca de validación con el paso
    /// provisional «sin VeriFactu» y la cuenta de las líneas a 30 caracteres, como los códigos de cuenta.
    /// </summary>
    public static Migracion FacturasInstantanea { get; } = new(
        "20261101_1631_FACTURAS_INSTANTANEA", 261101005,
        "Domicilio y email del cliente congelados en Facturas, marca «sin VeriFactu» y cuenta de las líneas a 30 caracteres.",
        "facturacion/factura-venta",
        new AnadirColumna("Facturas", "DireccionCliente", "TEXT(255)"),
        new AnadirColumna("Facturas", "CodigoPostalCliente", "TEXT(20)"),
        new AnadirColumna("Facturas", "PoblacionCliente", "TEXT(100)"),
        new AnadirColumna("Facturas", "ProvinciaCliente", "TEXT(100)"),
        new AnadirColumna("Facturas", "EmailCliente", "TEXT(254)"),
        new AnadirColumnaObligatoria("Facturas", "SinVeriFactu", "YESNO", "False"),
        new AmpliarTexto("LineasFactura", "CuentaContable", 30));

    /// <summary>
    /// Cuenta de retención del cliente por sociedad (terceros/roles 2026-11-01 16:18): CuentasClientesSociedad pasa a
    /// tener una clase (CLIENTE o RETENCION), como las cuentas del proveedor; las filas existentes son CLIENTE y el
    /// índice único pasa a ser por cliente, sociedad y clase.
    /// </summary>
    public static Migracion CuentaRetencionCliente { get; } = new(
        "20261101_1632_CUENTA_RETENCION_CLIENTE", 261101006,
        "Clase de cuenta (CLIENTE o RETENCION) en CuentasClientesSociedad para la cuenta de retención del cliente.",
        "terceros/roles",
        new AnadirColumnaObligatoria("CuentasClientesSociedad", "Clase", "TEXT(10)", "'CLIENTE'"),
        new PasoPersonalizado(
            "Quitar el índice único UX_CCS_ClienteSociedad (cliente y sociedad)",
            "DROP INDEX UX_CCS_ClienteSociedad ON CuentasClientesSociedad",
            (_, e) => Task.FromResult(!e.ExisteIndice("CuentasClientesSociedad", "UX_CCS_ClienteSociedad")),
            (s, _) => s.EjecutarAsync("DROP INDEX UX_CCS_ClienteSociedad ON CuentasClientesSociedad")),
        new CrearIndice("CuentasClientesSociedad", "UX_CCS_ClienteSociedadClase",
            "CREATE UNIQUE INDEX UX_CCS_ClienteSociedadClase ON CuentasClientesSociedad (IdCliente, IdSociedad, Clase)"));

    /// <summary>
    /// Permisos de acciones por grupo (seguridad/permisos, regla común): una acción sin filas la puede hacer cualquier
    /// operador; con alguna fila, solo los grupos que la tienen. Primeras acciones: las de facturas y el mantenimiento.
    /// </summary>
    public static Migracion PermisosAcciones { get; } = new(
        "20261101_1633_PERMISOS_ACCIONES", 261101007,
        "Tabla PermisosAccionesGrupo (acción y grupo) para autorizar acciones en los servicios.",
        "seguridad/permisos",
        new CrearTabla("PermisosAccionesGrupo",
            "CREATE TABLE PermisosAccionesGrupo (IdPermisoAccion COUNTER CONSTRAINT PK_PermisosAccionesGrupo PRIMARY KEY, " +
            "CodigoAccion TEXT(60) NOT NULL, " +
            "IdGrupoOperadores LONG NOT NULL CONSTRAINT FK_PermisosAcciones_Grupo REFERENCES GruposOperadores (IdGrupoOperadores))"),
        new CrearIndice("PermisosAccionesGrupo", "UX_PermisosAcciones_AccionGrupo",
            "CREATE UNIQUE INDEX UX_PermisosAcciones_AccionGrupo ON PermisosAccionesGrupo (CodigoAccion, IdGrupoOperadores)"));
}
