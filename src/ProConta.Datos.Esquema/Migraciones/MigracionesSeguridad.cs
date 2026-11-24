using ProConta.Datos.Persistencia;

namespace ProConta.Datos.Esquema.Migraciones;

/// <summary>Migraciones pedidas por los nodos de seguridad (docs/datos/esquema.md, Requirements).</summary>
public static class MigracionesSeguridad
{
    /// <summary>
    /// Parámetro general «Mostrar la lista de operadores en el acceso», guardado en negativo
    /// (Configuracion.OcultarListaOperadoresAcceso, por defecto no): la lista se muestra salvo que se oculte.
    /// </summary>
    public static Migracion ListaOperadoresAcceso { get; } = new(
        "20261101_0925_LISTA_OPERADORES_ACCESO", 261101001,
        "Configuracion.OcultarListaOperadoresAcceso (sí/no, por defecto no: el acceso muestra la lista de operadores).",
        "seguridad/acceso",
        new AnadirColumna("Configuracion", "OcultarListaOperadoresAcceso", "YESNO NOT NULL DEFAULT No"));

    /// <summary>
    /// Operadores como rol de terceros (seguridad/pantallas 2026-11-24): cada operador apunta a su tercero persona física
    /// (OperadoresSeguridad.IdTercero, obligatorio y único) y deja de guardar nombre y correo, que se leen del tercero. Cada operador
    /// existente se vincula al único tercero persona física activo con su nombre (de pila o completo) que no sea ya de otro operador;
    /// si no hay exactamente uno, se le da de alta un tercero persona física con ese nombre y su correo.
    /// </summary>
    public static Migracion OperadoresTerceros { get; } = new(
        "20261124_1200_OPERADORES_TERCEROS", 261124120,
        "OperadoresSeguridad.IdTercero (tercero persona física del operador, obligatorio y único); se quitan Nombre y Email, que salen del tercero.",
        "seguridad/pantallas",
        new AnadirColumna("OperadoresSeguridad", "IdTercero", "LONG"),
        new PasoPersonalizado(
            "Vincular cada operador a su tercero persona física (o darlo de alta)",
            "UPDATE OperadoresSeguridad SET IdTercero <- único tercero FISICA activo con UCASE(TRIM(NombrePersona)) o UCASE(TRIM(Nombre)) = UCASE(TRIM(Nombre del operador)) y sin otro operador; " +
            "si no hay exactamente uno, INSERT INTO Terceros (FISICA, NombrePersona = nombre del operador, Email) con su fila ALTA en HistorialTerceros",
            async (s, e) => !e.ExisteColumna("OperadoresSeguridad", "Nombre") ||
                Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM OperadoresSeguridad WHERE IdTercero IS NULL")) == 0,
            (s, _) => VincularOperadoresAsync(s)),
        new PasoPersonalizado(
            "Exigir OperadoresSeguridad.IdTercero",
            "ALTER TABLE OperadoresSeguridad ALTER COLUMN IdTercero LONG NOT NULL",
            (_, e) => Task.FromResult(e.Tabla("OperadoresSeguridad")?.Columna("IdTercero") is { AdmiteNulos: false }),
            (s, _) => s.EjecutarAsync("ALTER TABLE OperadoresSeguridad ALTER COLUMN IdTercero LONG NOT NULL")),
        new CrearIndice("OperadoresSeguridad", "UX_OperadoresSeguridad_IdTercero", "CREATE UNIQUE INDEX UX_OperadoresSeguridad_IdTercero ON OperadoresSeguridad (IdTercero)"),
        new PasoPersonalizado(
            "Relacionar OperadoresSeguridad (FK_OperadoresSeguridad_Tercero)",
            "ALTER TABLE OperadoresSeguridad ADD CONSTRAINT FK_OperadoresSeguridad_Tercero FOREIGN KEY (IdTercero) REFERENCES Terceros (IdTercero)",
            (_, e) => Task.FromResult(MigracionesOrganizacion.TieneRelacion(e, "OperadoresSeguridad", "FK_OperadoresSeguridad_Tercero")),
            (s, _) => s.EjecutarAsync("ALTER TABLE OperadoresSeguridad ADD CONSTRAINT FK_OperadoresSeguridad_Tercero FOREIGN KEY (IdTercero) REFERENCES Terceros (IdTercero)")),
        new QuitarColumna("OperadoresSeguridad", "Nombre"),
        new QuitarColumna("OperadoresSeguridad", "Email"))
    {
        Descripciones =
        [
            new("OperadoresSeguridad", null, "Rol «Operador ProConta» de un tercero persona física: con qué tercero entra, si está activo y su contraseña (hash). El nombre de acceso sale del nombre de pila del tercero, con los apellidos si otro operador se llama igual."),
            new("OperadoresSeguridad", "IdTercero", "Tercero persona física que es este operador (uno por tercero). Su nombre y su correo son los del tercero."),
        ],
    };

    private static async Task VincularOperadoresAsync(ISesionDatos s)
    {
        if (!s.LeerEsquema().ExisteColumna("OperadoresSeguridad", "Nombre")) return;
        var operadores = await s.ConsultarAsync("SELECT IdOperador, Nombre, Email FROM OperadoresSeguridad WHERE IdTercero IS NULL ORDER BY IdOperador",
            f => (Id: f.Entero("IdOperador"), Nombre: (f.Texto("Nombre") ?? "").Trim(), Email: f.Texto("Email")));
        var terceros = await s.ConsultarAsync("SELECT IdTercero, NombrePersona, Nombre FROM Terceros WHERE TipoPersona = 'FISICA' AND Activo = True",
            f => (Id: f.Entero("IdTercero"), Pila: (f.Texto("NombrePersona") ?? "").Trim(), Nombre: (f.Texto("Nombre") ?? "").Trim()));
        var usados = (await s.ConsultarAsync("SELECT IdTercero FROM OperadoresSeguridad WHERE IdTercero IS NOT NULL", f => f.Entero("IdTercero"))).ToHashSet();
        foreach (var o in operadores)
        {
            var nombre = o.Nombre.Length > 0 ? o.Nombre : $"Operador {o.Id}";
            var candidatos = terceros.Where(t => !usados.Contains(t.Id) &&
                (string.Equals(t.Pila, nombre, StringComparison.OrdinalIgnoreCase) || string.Equals(t.Nombre, nombre, StringComparison.OrdinalIgnoreCase))).ToList();
            int idTercero;
            if (candidatos.Count == 1) idTercero = candidatos[0].Id;
            else
            {
                var ahora = DateTime.Now;
                var email = string.IsNullOrWhiteSpace(o.Email) ? null : o.Email.Trim();
                await s.EjecutarAsync(
                    "INSERT INTO Terceros (TipoPersona, Nombre, NombrePersona, Email, TipoIdentificacion, Pais, Activo, FechaAlta, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    "FISICA", nombre, nombre, (object?)email ?? DBNull.Value, "NIF", "724", true, ahora.Date, 1);
                idTercero = await s.UltimoIdAsync();
                await s.EjecutarAsync(
                    "INSERT INTO HistorialTerceros (IdTercero, IdOperacion, FechaHora, Operador, Entidad, IdRegistro, Operacion, Campo, ValorAntiguo, ValorNuevo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    idTercero, Guid.NewGuid().ToString(), ahora, "Migración de operadores", "Tercero", idTercero, "ALTA", "Nombre", DBNull.Value, nombre);
            }
            usados.Add(idTercero);
            await s.EjecutarAsync("UPDATE OperadoresSeguridad SET IdTercero = ? WHERE IdOperador = ?", idTercero, o.Id);
        }
    }
}
