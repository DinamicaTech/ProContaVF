using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;

namespace ProConta.Terceros;

/// <summary>Resultado de búsqueda de terceros (sin cargar la ficha completa).</summary>
public sealed record ResumenTercero(int IdTercero, string Nombre, string? NombreComercial, string? Nif, bool Activo);

/// <summary>Movimiento del historial de un tercero (tabla HistorialTerceros).</summary>
/// <param name="Entidad">Sección de la ficha: Tercero, DatosContacto, Cliente, Proveedor, Contacto, Perfil o Relacion.</param>
public sealed record MovimientoHistorial(int IdHistorial, string IdOperacion, DateTime FechaHora, string Operador, string Entidad, int IdRegistro,
    string Operacion, string Campo, string? ValorAntiguo, string? ValorNuevo);

/// <summary>
/// Contrato del ente Tercero: única vía de escritura de Terceros, DomiciliosTercero, DatosContacto,
/// ContactosLibresTercero e HistorialTerceros, y de los roles, perfiles, relaciones y cuentas de rol que se guardan con la ficha.
/// </summary>
public interface IRepositorioTerceros
{
    Task<Tercero?> ObtenerAsync(int idTercero);

    /// <summary>Busca por nombre, razón social, nombre comercial o NIF. Dos terceros con el mismo NIF salen los dos.</summary>
    Task<IReadOnlyList<ResumenTercero>> BuscarAsync(string texto, int maximo = 50);

    /// <summary>
    /// Guarda la ficha completa en una transacción, con concurrencia optimista e historial de cambios: identidad,
    /// domicilios, datos de contacto, contactos libres, roles, perfiles, relaciones y, si la ficha las ha cargado, las
    /// cuentas de los roles. Ante cualquier error no queda nada a medias. Nunca modifica facturas emitidas.
    /// </summary>
    Task<Resultado<Tercero>> GuardarAsync(Tercero tercero, Actor actor);

    /// <summary>Historial del tercero, del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<MovimientoHistorial>> HistorialAsync(int idTercero);
}

public sealed class RepositorioTercerosAccess(IBaseDatos bd, IReloj? reloj = null) : IRepositorioTerceros
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    private const string Columnas =
        "IdTercero, TipoPersona, Nombre, RazonSocial, NIF, Telefono, Email, Direccion, CodigoPostal, Poblacion, Provincia, Pais, Notas, Activo, FechaAlta, " +
        "NombrePersona, PrimerApellido, SegundoApellido, NombreComercial, TipoIdentificacion, NotasInternas, Version";

    public Task<Tercero?> ObtenerAsync(int idTercero) => bd.EjecutarAsync(s => CargarAsync(s, idTercero));

    public Task<IReadOnlyList<ResumenTercero>> BuscarAsync(string texto, int maximo = 50)
    {
        var patron = "%" + (texto ?? "").Trim().Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]") + "%";
        return bd.EjecutarAsync(s => s.ConsultarAsync(
            $"SELECT TOP {Math.Clamp(maximo, 1, 500)} IdTercero, Nombre, NombreComercial, NIF, Activo FROM Terceros " +
            "WHERE Nombre LIKE ? OR RazonSocial LIKE ? OR NombreComercial LIKE ? OR NIF LIKE ? ORDER BY Nombre, IdTercero",
            f => new ResumenTercero(f.Entero("IdTercero"), f.Texto("Nombre") ?? "", f.Texto("NombreComercial"), f.Texto("NIF"), f.Logico("Activo")),
            patron, patron, patron, patron));
    }

    public Task<Resultado<Tercero>> GuardarAsync(Tercero tercero, Actor actor) =>
        bd.EnTransaccionAsync(async s =>
        {
            var anterior = tercero.EsNuevo ? null : await CargarAsync(s, tercero.IdTercero);
            if (!tercero.EsNuevo && anterior is null)
                return Resultado<Tercero>.Fallo(new Error("Tercero.NoExiste", "El tercero ya no existe.", $"Tercero {tercero.IdTercero}",
                    "Otra persona lo ha borrado.", "Vuelve a buscarlo."));
            var validacion = tercero.Validar(anterior?.Nif);
            if (!validacion.EsCorrecto) return Resultado<Tercero>.Fallo(validacion.Errores);

            tercero.Nombre = tercero.NombreCalculado();
            var operacion = Guid.NewGuid().ToString();
            var ahora = _reloj.Ahora;
            var tipo = Texto(tercero.TipoPersona);
            if (tercero.EsNuevo)
            {
                tercero.FechaAlta = ahora.Date;
                await s.EjecutarAsync(
                    "INSERT INTO Terceros (TipoPersona, Nombre, RazonSocial, NIF, Telefono, Email, Direccion, CodigoPostal, Poblacion, Provincia, Pais, Notas, Activo, FechaAlta, " +
                    "NombrePersona, PrimerApellido, SegundoApellido, NombreComercial, TipoIdentificacion, NotasInternas, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    [tipo, .. Valores(tercero), tercero.Activo, tercero.FechaAlta, tercero.NombrePersona, tercero.PrimerApellido, tercero.SegundoApellido,
                        tercero.NombreComercial, tercero.TipoIdentificacion, tercero.NotasInternas, 1]);
                tercero.IdTercero = await s.UltimoIdAsync();
                await HistorialAsync(s, tercero.IdTercero, operacion, ahora, actor, "Tercero", tercero.IdTercero, "ALTA", "Nombre", null, tercero.Nombre);
            }
            else
            {
                await s.ActualizarConVersionAsync(
                    "UPDATE Terceros SET TipoPersona = ?, Nombre = ?, RazonSocial = ?, NIF = ?, Telefono = ?, Email = ?, Direccion = ?, CodigoPostal = ?, Poblacion = ?, Provincia = ?, Pais = ?, Notas = ?, Activo = ?, " +
                    "NombrePersona = ?, PrimerApellido = ?, SegundoApellido = ?, NombreComercial = ?, TipoIdentificacion = ?, NotasInternas = ?, Version = Version + 1 WHERE IdTercero = ? AND Version = ?",
                    $"Tercero {tercero.IdTercero} {tercero.Nombre}",
                    [tipo, .. Valores(tercero), tercero.Activo, tercero.NombrePersona, tercero.PrimerApellido, tercero.SegundoApellido,
                        tercero.NombreComercial, tercero.TipoIdentificacion, tercero.NotasInternas, tercero.IdTercero, tercero.Version]);
                foreach (var (campo, antes, despues) in Diferencias(anterior!, tercero))
                    await HistorialAsync(s, tercero.IdTercero, operacion, ahora, actor, "Tercero", tercero.IdTercero, "MODIFICACION", campo, antes, despues);
            }

            await GuardarHijosAsync(s, tercero, anterior, operacion, ahora, actor);
            var h = new Historial(s, tercero.IdTercero, operacion, ahora, actor);
            await GuardarRolesAsync(s, tercero, h);
            var error = await GuardarPerfilesAsync(s, tercero, h) ?? await GuardarRelacionesAsync(s, tercero, h) ?? await GuardarCuentasAsync(s, tercero, h);
            if (error is not null) return Resultado<Tercero>.Fallo(error with { RegistroAfectado = error.RegistroAfectado ?? $"Tercero {tercero.Nombre}" });
            var guardado = (await CargarAsync(s, tercero.IdTercero))!;
            if (tercero.CuentasRoles is { } cuentas) guardado.CuentasRoles = await ServicioRoles.IdentidadesAsync(s, guardado.IdTercero, cuentas.IdSociedad);
            return Resultado<Tercero>.Correcto(guardado);
        });

    public Task<IReadOnlyList<MovimientoHistorial>> HistorialAsync(int idTercero) =>
        bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdHistorialTercero, IdOperacion, FechaHora, Operador, Entidad, IdRegistro, Operacion, Campo, ValorAntiguo, ValorNuevo FROM HistorialTerceros " +
            "WHERE IdTercero = ? ORDER BY FechaHora DESC, IdHistorialTercero DESC",
            f => new MovimientoHistorial(f.Entero("IdHistorialTercero"), f.Texto("IdOperacion") ?? "", f.Fecha("FechaHora"), f.Texto("Operador") ?? "",
                f.Texto("Entidad") ?? "", f.Entero("IdRegistro"), f.Texto("Operacion") ?? "", f.Texto("Campo") ?? "", f.Texto("ValorAntiguo"), f.Texto("ValorNuevo")),
            idTercero));

    private static object?[] Valores(Tercero t) =>
        [t.Nombre, Vacio(t.RazonSocial), Vacio(t.Nif), Vacio(t.Telefono), Vacio(t.Email), Vacio(t.Direccion), Vacio(t.CodigoPostal),
            Vacio(t.Poblacion), Vacio(t.Provincia), Vacio(t.Pais), Vacio(t.Notas)];

    /// <summary>Domicilios, contactos libres y datos de contacto: se sustituyen por los de la ficha, con historial.</summary>
    private async Task GuardarHijosAsync(ISesionDatos s, Tercero t, Tercero? anterior, string operacion, DateTime ahora, Actor actor)
    {
        await s.EjecutarAsync("DELETE FROM DomiciliosTercero WHERE IdTercero = ?", t.IdTercero);
        foreach (var d in t.Domicilios)
            await s.EjecutarAsync(
                "INSERT INTO DomiciliosTercero (IdTercero, TipoDomicilio, Direccion, CodigoPostal, Poblacion, Provincia, Pais, FechaInicio, FechaFin, EsPrincipal, Activo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                t.IdTercero, d.TipoDomicilio, d.Direccion, Vacio(d.CodigoPostal), Vacio(d.Poblacion), Vacio(d.Provincia), Vacio(d.Pais), d.FechaInicio, d.FechaFin, d.EsPrincipal, d.Activo);

        // Primero los datos (que apuntan a los grupos) y luego los grupos.
        await s.EjecutarAsync("DELETE FROM DatosContacto WHERE IdTercero = ?", t.IdTercero);
        await s.EjecutarAsync("DELETE FROM ContactosLibresTercero WHERE IdTercero = ?", t.IdTercero);
        var ids = new Dictionary<ContactoLibre, int>(ReferenceEqualityComparer.Instance);
        foreach (var grupo in t.ContactosLibres)
        {
            await s.EjecutarAsync("INSERT INTO ContactosLibresTercero (IdTercero, Nombre, Departamento) VALUES (?, ?, ?)", t.IdTercero, grupo.Nombre.Trim(), Vacio(grupo.Departamento));
            ids[grupo] = await s.UltimoIdAsync();
        }
        foreach (var d in t.DatosContacto)
            await s.EjecutarAsync(
                "INSERT INTO DatosContacto (IdTercero, Tipo, Etiqueta, Valor, Activo, EnvioFacturas, EnviosAdministrativos, IdContactoLibre) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
                t.IdTercero, d.Tipo == TipoDatoContacto.Email ? "EMAIL" : "TELEFONO", Vacio(d.Etiqueta), d.Valor.Trim(), d.Activo, d.EnvioFacturas, d.EnviosAdministrativos,
                d.ContactoLibre is null ? null : ids[d.ContactoLibre]);

        var antes = anterior is null ? "" : ResumenContactos(anterior);
        var despues = ResumenContactos(t);
        if (antes != despues)
            await HistorialAsync(s, t.IdTercero, operacion, ahora, actor, "DatosContacto", t.IdTercero, anterior is null ? "ALTA" : "MODIFICACION", "DatosContacto",
                anterior is null ? null : antes, despues);
    }

    private static string ResumenContactos(Tercero t) =>
        string.Join("; ", t.DatosContacto.Select(d => $"{d.Tipo}:{d.Valor.Trim()}{(d.Activo ? "" : " (inactivo)")}{(d.EnvioFacturas ? " [facturas]" : "")}{(d.EnviosAdministrativos ? " [administrativos]" : "")}{(d.ContactoLibre is null ? "" : $" <{d.ContactoLibre.Nombre.Trim()}>")}"))
        + " | " + string.Join("; ", t.ContactosLibres.Select(g => $"{g.Nombre.Trim()}{(string.IsNullOrWhiteSpace(g.Departamento) ? "" : $" ({g.Departamento.Trim()})")}"))
        + " | " + string.Join("; ", t.Domicilios.Select(d => $"{d.TipoDomicilio}:{d.Direccion} {d.CodigoPostal} {d.Poblacion}{(d.Activo ? "" : " (inactivo)")}"));

    private static IEnumerable<(string Campo, string? Antes, string? Despues)> Diferencias(Tercero a, Tercero b)
    {
        (string, string?, string?)[] campos =
        [
            ("TipoPersona", Texto(a.TipoPersona), Texto(b.TipoPersona)), ("Nombre", a.Nombre, b.Nombre), ("RazonSocial", a.RazonSocial, b.RazonSocial),
            ("NombrePersona", a.NombrePersona, b.NombrePersona), ("PrimerApellido", a.PrimerApellido, b.PrimerApellido), ("SegundoApellido", a.SegundoApellido, b.SegundoApellido),
            ("NombreComercial", a.NombreComercial, b.NombreComercial), ("NIF", a.Nif, b.Nif), ("TipoIdentificacion", a.TipoIdentificacion, b.TipoIdentificacion),
            ("Telefono", a.Telefono, b.Telefono), ("Email", a.Email, b.Email), ("Pais", a.Pais, b.Pais), ("CodigoPostal", a.CodigoPostal, b.CodigoPostal),
            ("Provincia", a.Provincia, b.Provincia), ("Poblacion", a.Poblacion, b.Poblacion), ("Direccion", a.Direccion, b.Direccion),
            ("Notas", a.Notas, b.Notas), ("NotasInternas", a.NotasInternas, b.NotasInternas), ("Activo", a.Activo.ToString(), b.Activo.ToString()),
        ];
        return campos.Where(c => !string.Equals(Vacio(c.Item2), Vacio(c.Item3), StringComparison.Ordinal));
    }

    private static Task HistorialAsync(ISesionDatos s, int idTercero, string operacion, DateTime ahora, Actor actor, string entidad, int idRegistro, string tipo, string campo, string? antes, string? despues) =>
        s.EjecutarAsync(
            "INSERT INTO HistorialTerceros (IdTercero, IdOperacion, FechaHora, Operador, Entidad, IdRegistro, Operacion, Campo, ValorAntiguo, ValorNuevo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idTercero, operacion, ahora, actor.ToString(), entidad, idRegistro, tipo, campo, antes, despues);

    /// <summary>Escribe filas de historial de una misma operación de guardado.</summary>
    private sealed class Historial(ISesionDatos s, int idTercero, string operacion, DateTime ahora, Actor actor)
    {
        public DateTime Ahora => ahora;

        public Task Anotar(string entidad, int idRegistro, string tipo, string campo, string? antes, string? despues, int? enTercero = null) =>
            HistorialAsync(s, enTercero ?? idTercero, operacion, ahora, actor, entidad, idRegistro, tipo, campo, antes, despues);
    }

    /// <summary>Roles: alta idempotente, activar o desactivar y sus datos propios. Un rol null en la ficha no se toca.</summary>
    private static async Task GuardarRolesAsync(ISesionDatos s, Tercero t, Historial h)
    {
        if (t.Roles.Cliente is { } cliente)
        {
            var db = await s.ConsultarPrimeroAsync("SELECT IdCliente, Activo, Notas FROM Clientes WHERE IdTercero = ?",
                f => new FilaRol(f.Entero("IdCliente"), f.Logico("Activo"), f.Texto("Notas")), t.IdTercero);
            if (db is null && cliente.Activo)
            {
                cliente.IdCliente = await ServicioRoles.AsegurarClienteAsync(s, t.IdTercero);
                if (Vacio(cliente.Notas) is { } n) await s.EjecutarAsync("UPDATE Clientes SET Notas = ? WHERE IdCliente = ?", n, cliente.IdCliente);
                await h.Anotar("Cliente", cliente.IdCliente.Value, "ALTA", "IdCliente", null, cliente.IdCliente.ToString());
            }
            else if (db is not null)
            {
                cliente.IdCliente = db.Id;
                await s.EjecutarAsync("UPDATE Clientes SET Activo = ?, Notas = ? WHERE IdCliente = ?", cliente.Activo, Vacio(cliente.Notas), db.Id);
                await AnotarRolAsync(h, "Cliente", db, cliente.Activo, ("Notas", db.Notas, cliente.Notas));
            }
        }
        if (t.Roles.Proveedor is { } proveedor)
        {
            var db = await s.ConsultarPrimeroAsync("SELECT IdProveedor, Activo, NotasProveedor FROM Proveedores WHERE IdTercero = ?",
                f => new FilaRol(f.Entero("IdProveedor"), f.Logico("Activo"), f.Texto("NotasProveedor")), t.IdTercero);
            if (db is null && proveedor.Activo)
            {
                proveedor.IdProveedor = await ServicioRoles.AsegurarProveedorAsync(s, t.IdTercero);
                if (Vacio(proveedor.Notas) is { } n) await s.EjecutarAsync("UPDATE Proveedores SET NotasProveedor = ? WHERE IdProveedor = ?", n, proveedor.IdProveedor);
                await h.Anotar("Proveedor", proveedor.IdProveedor.Value, "ALTA", "IdProveedor", null, proveedor.IdProveedor.ToString());
            }
            else if (db is not null)
            {
                proveedor.IdProveedor = db.Id;
                await s.EjecutarAsync("UPDATE Proveedores SET Activo = ?, NotasProveedor = ? WHERE IdProveedor = ?", proveedor.Activo, Vacio(proveedor.Notas), db.Id);
                await AnotarRolAsync(h, "Proveedor", db, proveedor.Activo, ("Notas", db.Notas, proveedor.Notas));
            }
        }
        if (t.Roles.Contacto is { } contacto)
        {
            var db = await s.ConsultarPrimeroAsync("SELECT IdContacto, Activo, Cargo, Departamento, NotasContacto FROM Contactos WHERE IdTercero = ?",
                f => new FilaRol(f.Entero("IdContacto"), f.Logico("Activo"), f.Texto("NotasContacto"), f.Texto("Cargo"), f.Texto("Departamento")), t.IdTercero);
            if (db is null && contacto.Activo)
            {
                contacto.IdContacto = await ServicioRoles.AsegurarContactoAsync(s, t.IdTercero, Vacio(contacto.Cargo), Vacio(contacto.Departamento));
                if (Vacio(contacto.Notas) is { } n) await s.EjecutarAsync("UPDATE Contactos SET NotasContacto = ? WHERE IdContacto = ?", n, contacto.IdContacto);
                await h.Anotar("Contacto", contacto.IdContacto.Value, "ALTA", "IdContacto", null, contacto.IdContacto.ToString());
            }
            else if (db is not null)
            {
                contacto.IdContacto = db.Id;
                await s.EjecutarAsync("UPDATE Contactos SET Activo = ?, Cargo = ?, Departamento = ?, NotasContacto = ? WHERE IdContacto = ?",
                    contacto.Activo, Vacio(contacto.Cargo), Vacio(contacto.Departamento), Vacio(contacto.Notas), db.Id);
                await AnotarRolAsync(h, "Contacto", db, contacto.Activo,
                    ("Cargo", db.Cargo, contacto.Cargo), ("Departamento", db.Departamento, contacto.Departamento), ("Notas", db.Notas, contacto.Notas));
            }
        }
    }

    private sealed record FilaRol(int Id, bool Activo, string? Notas, string? Cargo = null, string? Departamento = null);

    private static async Task AnotarRolAsync(Historial h, string rol, FilaRol db, bool activo, params (string Campo, string? Antes, string? Despues)[] campos)
    {
        if (db.Activo != activo) await h.Anotar(rol, db.Id, "MODIFICACION", "Activo", db.Activo.ToString(), activo.ToString());
        foreach (var (campo, a, d) in campos.Where(c => !string.Equals(Vacio(c.Antes), Vacio(c.Despues), StringComparison.Ordinal)))
            await h.Anotar(rol, db.Id, "MODIFICACION", campo, Vacio(a), Vacio(d));
    }

    /// <summary>
    /// Perfiles (RF-TER-12/13): asignar inserta o reactiva la misma fila; retirar marca Activo = false y la fecha de
    /// retirada; repetir el estado actual no escribe nada. Un perfil inactivo no admite asignaciones nuevas.
    /// </summary>
    private static async Task<Error?> GuardarPerfilesAsync(ISesionDatos s, Tercero t, Historial h)
    {
        var actuales = (await s.ConsultarAsync("SELECT IdPerfil, Activo FROM PerfilesTerceros WHERE IdTercero = ?",
            f => (IdPerfil: f.Entero("IdPerfil"), Activo: f.Logico("Activo")), t.IdTercero)).ToDictionary(x => x.IdPerfil, x => x.Activo);
        foreach (var a in t.Perfiles)
        {
            var existe = actuales.TryGetValue(a.IdPerfil, out var activoBd);
            if (existe ? activoBd == a.Activo : !a.Activo) continue;
            if (a.Activo)
            {
                var perfil = await s.ConsultarPrimeroAsync("SELECT Nombre, Activo FROM Perfiles WHERE IdPerfil = ?", f => (Nombre: f.Texto("Nombre") ?? "", Activo: f.Logico("Activo")), a.IdPerfil);
                if (perfil == default)
                    return new Error("Perfiles.NoExiste", $"El perfil «{a.Nombre}» ya no existe en el catálogo.", null, "Lo han borrado mientras editabas.", "Quítalo de la ficha y vuelve a guardar.");
                if (!perfil.Activo)
                    return new Error("Perfiles.Inactivo", $"El perfil «{perfil.Nombre}» está retirado del catálogo y no admite asignaciones nuevas.", null,
                        "Un perfil retirado conserva las asignaciones que ya tenía, pero no admite otras.", "Quítalo de los asignados o elige otro perfil.");
                if (existe)
                    await s.EjecutarAsync("UPDATE PerfilesTerceros SET Activo = True, FechaAsignacion = ?, FechaRetirada = NULL WHERE IdTercero = ? AND IdPerfil = ?", h.Ahora, t.IdTercero, a.IdPerfil);
                else
                    await s.EjecutarAsync("INSERT INTO PerfilesTerceros (IdTercero, IdPerfil, Activo, FechaAsignacion) VALUES (?, ?, ?, ?)", t.IdTercero, a.IdPerfil, true, h.Ahora);
                await h.Anotar("Perfil", a.IdPerfil, existe ? "MODIFICACION" : "ALTA", "Perfil", existe ? $"Retirado: {perfil.Nombre}" : null, $"Asignado: {perfil.Nombre}");
            }
            else
            {
                await s.EjecutarAsync("UPDATE PerfilesTerceros SET Activo = False, FechaRetirada = ? WHERE IdTercero = ? AND IdPerfil = ?", h.Ahora, t.IdTercero, a.IdPerfil);
                await h.Anotar("Perfil", a.IdPerfil, "BAJA", "Perfil", $"Asignado: {a.Nombre}", $"Retirado: {a.Nombre}");
            }
        }
        return null;
    }

    /// <summary>
    /// Relaciones persona-organización (RF-TER-11): las nuevas se insertan (la persona recibe el rol Contacto si no lo
    /// tiene, de forma idempotente) y las guardadas se actualizan; ninguna se borra, se cierra con la fecha de fin.
    /// El historial queda en los dos terceros.
    /// </summary>
    private static async Task<Error?> GuardarRelacionesAsync(ISesionDatos s, Tercero t, Historial h)
    {
        foreach (var r in t.Relaciones)
        {
            if (r.IdTerceroPersona == 0) r.IdTerceroPersona = t.IdTercero;
            if (r.IdTerceroOrganizacion == 0) r.IdTerceroOrganizacion = t.IdTercero;
            var otro = r.IdTerceroPersona == t.IdTercero ? r.IdTerceroOrganizacion : r.IdTerceroPersona;
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Terceros WHERE IdTercero = ?", otro)) == 0)
                return new Error("Relaciones.SinTercero", $"El tercero relacionado ({(otro == r.IdTerceroPersona ? r.NombrePersona : r.NombreOrganizacion)}) ya no existe.", null,
                    "Lo han borrado mientras editabas.", "Quita la relación y vuelve a guardar.");
            var descripcion = $"{r.TipoRelacion.Trim()}: {r.NombrePersona} → {r.NombreOrganizacion}";
            if (r.EsNueva)
            {
                var idContacto = await ServicioRoles.AsegurarContactoAsync(s, r.IdTerceroPersona);
                await s.EjecutarAsync("INSERT INTO RelacionesContactos (IdContacto, IdTerceroOrganizacion, TipoRelacion, FechaInicio, FechaFin, Observaciones) VALUES (?, ?, ?, ?, ?, ?)",
                    idContacto, r.IdTerceroOrganizacion, r.TipoRelacion.Trim(), r.FechaInicio?.Date, r.FechaFin?.Date, Vacio(r.Observaciones));
                r.IdRelacionContacto = await s.UltimoIdAsync();
                await h.Anotar("Relacion", r.IdRelacionContacto, "ALTA", "Relacion", null, descripcion + Vigencia(r));
                await h.Anotar("Relacion", r.IdRelacionContacto, "ALTA", "Relacion", null, descripcion + Vigencia(r), otro);
                continue;
            }
            var db = await s.ConsultarPrimeroAsync("SELECT TipoRelacion, FechaInicio, FechaFin, Observaciones FROM RelacionesContactos WHERE IdRelacionContacto = ?",
                f => new FilaRelacion(f.Texto("TipoRelacion") ?? "", f.FechaNula("FechaInicio"), f.FechaNula("FechaFin"), f.Texto("Observaciones")), r.IdRelacionContacto);
            if (db is null)
                return new Error("Relaciones.NoExiste", $"La relación «{descripcion}» ya no existe.", null, "La han borrado mientras editabas.", "Vuelve a abrir la ficha.");
            (string Campo, string? Antes, string? Despues)[] cambios =
            [
                ("TipoRelacion", db.Tipo, r.TipoRelacion.Trim()), ("FechaInicio", Fecha(db.Inicio), Fecha(r.FechaInicio)),
                ("FechaFin", Fecha(db.Fin), Fecha(r.FechaFin)), ("Observaciones", Vacio(db.Observaciones), Vacio(r.Observaciones)),
            ];
            var distintos = cambios.Where(c => !string.Equals(c.Antes, c.Despues, StringComparison.Ordinal)).ToList();
            if (distintos.Count == 0) continue;
            await s.EjecutarAsync("UPDATE RelacionesContactos SET TipoRelacion = ?, FechaInicio = ?, FechaFin = ?, Observaciones = ? WHERE IdRelacionContacto = ?",
                r.TipoRelacion.Trim(), r.FechaInicio?.Date, r.FechaFin?.Date, Vacio(r.Observaciones), r.IdRelacionContacto);
            foreach (var (campo, antes, despues) in distintos)
            {
                await h.Anotar("Relacion", r.IdRelacionContacto, "MODIFICACION", campo, antes, despues);
                await h.Anotar("Relacion", r.IdRelacionContacto, "MODIFICACION", campo, antes, despues, otro);
            }
        }
        return null;
    }

    private sealed record FilaRelacion(string Tipo, DateTime? Inicio, DateTime? Fin, string? Observaciones);

    private static string Vigencia(RelacionContacto r) =>
        r.FechaInicio is null && r.FechaFin is null ? "" : $" ({Fecha(r.FechaInicio) ?? "…"} - {Fecha(r.FechaFin) ?? "…"})";

    private static string? Fecha(DateTime? f) => f?.ToString("dd/MM/yyyy");

    /// <summary>Cuentas de los roles en la sociedad de la ficha (si la ficha las ha cargado); un rol sin dar no admite cuenta.</summary>
    private static async Task<Error?> GuardarCuentasAsync(ISesionDatos s, Tercero t, Historial h)
    {
        if (t.CuentasRoles is not { } cuentas) return null;
        foreach (var clase in new[] { ClaseCuentaRol.Cliente, ClaseCuentaRol.Proveedor, ClaseCuentaRol.Acreedor })
        {
            var deseada = cuentas.De(clase);
            int? idRol = clase == ClaseCuentaRol.Cliente ? t.Roles.Cliente?.IdCliente : t.Roles.Proveedor?.IdProveedor;
            if (idRol is null)
            {
                if (deseada is null) continue;
                return new Error("Roles.CuentaSinRol", $"No puedo guardar la {ServicioRoles.Describir(clase)} porque el tercero no tiene el rol {(clase == ClaseCuentaRol.Cliente ? "Cliente" : "Proveedor")}.",
                    null, "Solo los clientes y los proveedores tienen cuenta.", "Activa el rol en la pestaña Roles o quita la cuenta.");
            }
            var actual = await ServicioRoles.IdentidadAsignadaAsync(s, clase, idRol.Value, cuentas.IdSociedad);
            if (actual == deseada) continue;
            var error = await ServicioRoles.AsignarCuentaAsync(s, clase, idRol.Value, cuentas.IdSociedad, deseada);
            if (error is not null) return error;
            var campo = clase switch { ClaseCuentaRol.Cliente => "CuentaCliente", ClaseCuentaRol.Proveedor => "CuentaProveedor", _ => "CuentaAcreedor" };
            await h.Anotar(clase == ClaseCuentaRol.Cliente ? "Cliente" : "Proveedor", idRol.Value, "MODIFICACION", $"{campo} {cuentas.IdSociedad}",
                await DescribirIdentidadAsync(s, actual), await DescribirIdentidadAsync(s, deseada));
        }
        return null;
    }

    /// <summary>Código y nombre de la cuenta más reciente de una identidad (solo lectura de contabilidad, para el historial).</summary>
    private static async Task<string?> DescribirIdentidadAsync(ISesionDatos s, int? identidad) =>
        identidad is int id
            ? await s.ConsultarPrimeroAsync("SELECT c.Codigo, c.Nombre FROM CuentasContables AS c INNER JOIN SociedadesEjercicios AS se ON c.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                "WHERE c.IdIdentidadContable = ? ORDER BY se.Ejercicio DESC", f => $"{f.Texto("Codigo")} {f.Texto("Nombre")}", id) ?? $"Identidad contable {id}"
            : null;

    private static async Task<Tercero?> CargarAsync(ISesionDatos s, int idTercero)
    {
        var t = await s.ConsultarPrimeroAsync($"SELECT {Columnas} FROM Terceros WHERE IdTercero = ?", f => new Tercero
        {
            IdTercero = f.Entero("IdTercero"),
            TipoPersona = Tipo(f.Texto("TipoPersona")),
            Nombre = f.Texto("Nombre") ?? "",
            RazonSocial = f.Texto("RazonSocial"),
            Nif = f.Texto("NIF"),
            Telefono = f.Texto("Telefono"),
            Email = f.Texto("Email"),
            Direccion = f.Texto("Direccion"),
            CodigoPostal = f.Texto("CodigoPostal"),
            Poblacion = f.Texto("Poblacion"),
            Provincia = f.Texto("Provincia"),
            Pais = f.Texto("Pais"),
            Notas = f.Texto("Notas"),
            Activo = f.Logico("Activo"),
            FechaAlta = f.FechaNula("FechaAlta"),
            NombrePersona = f.Texto("NombrePersona"),
            PrimerApellido = f.Texto("PrimerApellido"),
            SegundoApellido = f.Texto("SegundoApellido"),
            NombreComercial = f.Texto("NombreComercial"),
            TipoIdentificacion = f.Texto("TipoIdentificacion"),
            NotasInternas = f.Texto("NotasInternas"),
            Version = f.Entero("Version"),
        }, idTercero);
        if (t is null) return null;

        t.Domicilios.AddRange(await s.ConsultarAsync(
            "SELECT IdDomicilioTercero, TipoDomicilio, Direccion, CodigoPostal, Poblacion, Provincia, Pais, FechaInicio, FechaFin, EsPrincipal, Activo FROM DomiciliosTercero WHERE IdTercero = ? ORDER BY IdDomicilioTercero",
            f => new Domicilio(f.Entero("IdDomicilioTercero"), f.Texto("TipoDomicilio") ?? "", f.Texto("Direccion") ?? "", f.Texto("CodigoPostal"), f.Texto("Poblacion"),
                f.Texto("Provincia"), f.Texto("Pais"), f.FechaNula("FechaInicio"), f.FechaNula("FechaFin"), f.Logico("EsPrincipal"), f.Logico("Activo")), idTercero));

        var grupos = (await s.ConsultarAsync("SELECT IdContactoLibre, Nombre, Departamento FROM ContactosLibresTercero WHERE IdTercero = ? ORDER BY IdContactoLibre",
            f => new ContactoLibre { IdContactoLibre = f.Entero("IdContactoLibre"), Nombre = f.Texto("Nombre") ?? "", Departamento = f.Texto("Departamento") }, idTercero))
            .ToDictionary(g => g.IdContactoLibre);
        t.ContactosLibres.AddRange(grupos.Values);
        t.DatosContacto.AddRange(await s.ConsultarAsync(
            "SELECT IdDatoContacto, Tipo, Etiqueta, Valor, Activo, EnvioFacturas, EnviosAdministrativos, IdContactoLibre FROM DatosContacto WHERE IdTercero = ? ORDER BY IdDatoContacto",
            f => new DatoContacto
            {
                IdDatoContacto = f.Entero("IdDatoContacto"),
                Tipo = string.Equals(f.Texto("Tipo"), "EMAIL", StringComparison.OrdinalIgnoreCase) ? TipoDatoContacto.Email : TipoDatoContacto.Telefono,
                Etiqueta = f.Texto("Etiqueta"),
                Valor = f.Texto("Valor") ?? "",
                Activo = f.Logico("Activo"),
                EnvioFacturas = f.Logico("EnvioFacturas"),
                EnviosAdministrativos = f.Logico("EnviosAdministrativos"),
                ContactoLibre = f.EnteroNulo("IdContactoLibre") is int g && grupos.TryGetValue(g, out var grupo) ? grupo : null,
            }, idTercero));

        t.Roles.Cliente = await s.ConsultarPrimeroAsync("SELECT IdCliente, Activo, Notas FROM Clientes WHERE IdTercero = ?",
            f => new RolCliente { IdCliente = f.Entero("IdCliente"), Activo = f.Logico("Activo"), Notas = f.Texto("Notas") }, idTercero);
        t.Roles.Proveedor = await s.ConsultarPrimeroAsync("SELECT IdProveedor, Activo, NotasProveedor FROM Proveedores WHERE IdTercero = ?",
            f => new RolProveedor { IdProveedor = f.Entero("IdProveedor"), Activo = f.Logico("Activo"), Notas = f.Texto("NotasProveedor") }, idTercero);
        t.Roles.Contacto = await s.ConsultarPrimeroAsync("SELECT IdContacto, Activo, Cargo, Departamento, NotasContacto FROM Contactos WHERE IdTercero = ?",
            f => new RolContacto { IdContacto = f.Entero("IdContacto"), Activo = f.Logico("Activo"), Cargo = f.Texto("Cargo"), Departamento = f.Texto("Departamento"), Notas = f.Texto("NotasContacto") }, idTercero);

        t.Perfiles.AddRange(await s.ConsultarAsync(
            "SELECT pt.IdPerfil, p.Nombre, p.Descripcion, p.Activo AS PerfilActivo, pt.Activo, pt.FechaAsignacion, pt.FechaRetirada " +
            "FROM PerfilesTerceros AS pt INNER JOIN Perfiles AS p ON pt.IdPerfil = p.IdPerfil WHERE pt.IdTercero = ? ORDER BY p.Nombre",
            f => new AsignacionPerfil(f.Entero("IdPerfil"), f.Texto("Nombre") ?? "", f.Texto("Descripcion"), f.Logico("PerfilActivo"))
            {
                Activo = f.Logico("Activo"), FechaAsignacion = f.FechaNula("FechaAsignacion") ?? DateTime.MinValue, FechaRetirada = f.FechaNula("FechaRetirada")
            }, idTercero));

        t.Relaciones.AddRange(await s.ConsultarAsync(
            "SELECT r.IdRelacionContacto, c.IdTercero AS IdPersona, tp.Nombre AS NombrePersona, r.IdTerceroOrganizacion, tor.Nombre AS NombreOrganizacion, " +
            "r.TipoRelacion, r.FechaInicio, r.FechaFin, r.Observaciones FROM ((RelacionesContactos AS r INNER JOIN Contactos AS c ON r.IdContacto = c.IdContacto) " +
            "INNER JOIN Terceros AS tp ON c.IdTercero = tp.IdTercero) INNER JOIN Terceros AS tor ON r.IdTerceroOrganizacion = tor.IdTercero " +
            "WHERE c.IdTercero = ? OR r.IdTerceroOrganizacion = ? ORDER BY r.FechaInicio, r.IdRelacionContacto",
            f => new RelacionContacto
            {
                IdRelacionContacto = f.Entero("IdRelacionContacto"), IdTerceroPersona = f.Entero("IdPersona"), NombrePersona = f.Texto("NombrePersona") ?? "",
                IdTerceroOrganizacion = f.Entero("IdTerceroOrganizacion"), NombreOrganizacion = f.Texto("NombreOrganizacion") ?? "",
                TipoRelacion = f.Texto("TipoRelacion") ?? "", FechaInicio = f.FechaNula("FechaInicio"), FechaFin = f.FechaNula("FechaFin"), Observaciones = f.Texto("Observaciones"),
            }, idTercero, idTercero));
        return t;
    }

    internal static string? Texto(TipoPersona? tipo) => tipo switch
    {
        TipoPersona.Fisica => "FISICA",
        TipoPersona.Juridica => "JURIDICA",
        TipoPersona.EntidadSinPersonalidad => "ENTIDAD",
        _ => null
    };

    internal static TipoPersona? Tipo(string? texto) => texto?.Trim().ToUpperInvariant() switch
    {
        "FISICA" => TipoPersona.Fisica,
        "JURIDICA" => TipoPersona.Juridica,
        "ENTIDAD" => TipoPersona.EntidadSinPersonalidad,
        _ => null
    };

    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
