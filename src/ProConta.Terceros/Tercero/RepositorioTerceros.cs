using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.Contactos;

namespace ProConta.Terceros;

/// <summary>Resultado de búsqueda de terceros (sin cargar la ficha completa).</summary>
public sealed record ResumenTercero(int IdTercero, string Nombre, string? NombreComercial, string? Nif, bool Activo);

/// <summary>
/// Contrato del ente Tercero: única vía de escritura de Terceros, DomiciliosTercero, DatosContacto,
/// ContactosLibresTercero e HistorialTerceros.
/// </summary>
public interface IRepositorioTerceros
{
    Task<Tercero?> ObtenerAsync(int idTercero);

    /// <summary>Busca por nombre, razón social, nombre comercial o NIF. Dos terceros con el mismo NIF salen los dos.</summary>
    Task<IReadOnlyList<ResumenTercero>> BuscarAsync(string texto, int maximo = 50);

    /// <summary>Guarda la ficha completa en una transacción, con concurrencia optimista e historial de cambios.</summary>
    Task<Resultado<Tercero>> GuardarAsync(Tercero tercero, Actor actor);
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
            return Resultado<Tercero>.Correcto((await CargarAsync(s, tercero.IdTercero))!);
        });

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
        + " | " + string.Join("; ", t.Domicilios.Select(d => $"{d.TipoDomicilio}:{d.Direccion}"));

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
        return t;
    }

    private static string? Texto(TipoPersona? tipo) => tipo switch
    {
        TipoPersona.Fisica => "FISICA",
        TipoPersona.Juridica => "JURIDICA",
        TipoPersona.EntidadSinPersonalidad => "ENTIDAD",
        _ => null
    };

    private static TipoPersona? Tipo(string? texto) => texto?.Trim().ToUpperInvariant() switch
    {
        "FISICA" => TipoPersona.Fisica,
        "JURIDICA" => TipoPersona.Juridica,
        "ENTIDAD" => TipoPersona.EntidadSinPersonalidad,
        _ => null
    };

    private static string? Vacio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
