using System.Globalization;
using System.Text;
using ProConta.Datos.Persistencia;

namespace ProConta.Terceros.Busqueda;

/// <summary>Rol por el que se filtra la búsqueda.</summary>
public enum RolBusqueda
{
    Cliente,
    Proveedor,
    Contacto
}

/// <summary>
/// Filtros del buscador de terceros. <see cref="Tipo"/> nulo y <see cref="Roles"/> vacío no filtran; con varios roles basta
/// con tener uno activo. <see cref="SinClasificar"/> filtra los terceros antiguos sin tipo de persona.
/// </summary>
public sealed record FiltroTerceros(
    string? Texto = null,
    TipoPersona? Tipo = null,
    bool SinClasificar = false,
    IReadOnlySet<RolBusqueda>? Roles = null,
    int? IdPerfil = null,
    bool IncluirInactivos = false,
    int Maximo = BuscadorTerceros.MaximoPredeterminado);

/// <summary>Dato en el que ha coincidido la búsqueda (por ejemplo «Persona de contacto · Lucía Ferrer Casals»).</summary>
public sealed record Coincidencia(string Fuente, string Valor)
{
    public override string ToString() => $"{Fuente} · {Valor}";
}

/// <summary>Rol de un resultado, con su Id y si está activo.</summary>
public sealed record RolResultado(RolBusqueda Rol, int Id, bool Activo)
{
    public string Abreviatura => Rol switch { RolBusqueda.Cliente => "Cli", RolBusqueda.Proveedor => "Prov", _ => "Cont" };
    public string Nombre => Rol switch { RolBusqueda.Cliente => "Cliente", RolBusqueda.Proveedor => "Proveedor", _ => "Contacto" };
}

/// <summary>Fila del buscador: datos de resumen, roles con su Id, perfiles y en qué ha coincidido.</summary>
public sealed record TerceroEncontrado(
    int IdTercero,
    TipoPersona? Tipo,
    string Nombre,
    string? NombreComercial,
    string? Nif,
    bool Activo,
    string? Poblacion,
    string? Telefono,
    string? Email,
    IReadOnlyList<RolResultado> Roles,
    IReadOnlyList<string> Perfiles,
    IReadOnlyList<Coincidencia> Coincidencias);

/// <summary>Recuentos para los filtros (sobre lo que coincide con el texto, el perfil y los inactivos, sin el tipo ni los roles).</summary>
public sealed record RecuentosBusqueda(int Todos, int Fisicas, int Juridicas, int Entidades, int SinClasificar, int Clientes, int Proveedores, int Contactos);

/// <summary>Resultado de una búsqueda: las filas (hasta el máximo) y cuántas hay en total.</summary>
public sealed record ResultadoBusquedaTerceros(IReadOnlyList<TerceroEncontrado> Filas, int Total, RecuentosBusqueda Recuentos)
{
    public bool Recortado => Total > Filas.Count;
}

/// <summary>Datos de un tercero preparados para buscar en memoria.</summary>
public sealed class EntradaIndiceTercero
{
    public int IdTercero { get; init; }
    public TipoPersona? Tipo { get; init; }
    public string Nombre { get; init; } = "";
    public string? NombreComercial { get; init; }
    public string? Nif { get; init; }
    public bool Activo { get; init; } = true;
    public string? Poblacion { get; init; }
    public string? Telefono { get; set; }
    public string? Email { get; set; }
    public List<RolResultado> Roles { get; } = [];
    public List<(int Id, string Nombre)> Perfiles { get; } = [];

    /// <summary>Textos en los que se busca, con su fuente.</summary>
    public List<Coincidencia> Textos { get; } = [];

    public void Anadir(string fuente, string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor)) return;
        var v = valor.Trim();
        if (!Textos.Any(t => string.Equals(t.Valor, v, StringComparison.OrdinalIgnoreCase))) Textos.Add(new Coincidencia(fuente, v));
    }
}

/// <summary>Índice en memoria de todos los terceros: la búsqueda multicriterio sin tocar la BD (se prueba sin Access).</summary>
public sealed class IndiceTerceros(IEnumerable<EntradaIndiceTercero> entradas)
{
    private readonly List<(EntradaIndiceTercero Entrada, (Coincidencia Dato, string Normal, string Compacto)[] Textos)> _entradas =
        entradas.OrderBy(e => e.Nombre, StringComparer.Create(CultureInfo.GetCultureInfo("es-ES"), true)).ThenBy(e => e.IdTercero)
            .Select(e => (e, e.Textos.Select(t => (t, Normalizar(t.Valor), Compactar(t.Valor))).ToArray())).ToList();

    public int Cuantos => _entradas.Count;

    public ResultadoBusquedaTerceros Buscar(FiltroTerceros filtro)
    {
        var palabras = Normalizar(filtro.Texto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct().ToArray();
        var coinciden = new List<(EntradaIndiceTercero Entrada, IReadOnlyList<Coincidencia> Donde)>();
        foreach (var (e, textos) in _entradas)
        {
            if (!filtro.IncluirInactivos && !e.Activo) continue;
            if (filtro.IdPerfil is int perfil && !e.Perfiles.Any(p => p.Id == perfil)) continue;
            var donde = new List<Coincidencia>();
            var todas = true;
            foreach (var palabra in palabras)
            {
                var compacta = Compactar(palabra);
                var esta = false;
                foreach (var (dato, normal, compacto) in textos)
                {
                    if (!normal.Contains(palabra, StringComparison.Ordinal) && !(compacta.Length >= 3 && compacto.Contains(compacta, StringComparison.Ordinal))) continue;
                    esta = true;
                    if (!donde.Contains(dato)) donde.Add(dato);
                }
                if (!esta) { todas = false; break; }
            }
            if (todas) coinciden.Add((e, donde));
        }

        var recuentos = new RecuentosBusqueda(coinciden.Count,
            coinciden.Count(c => c.Entrada.Tipo == TipoPersona.Fisica), coinciden.Count(c => c.Entrada.Tipo == TipoPersona.Juridica),
            coinciden.Count(c => c.Entrada.Tipo == TipoPersona.EntidadSinPersonalidad), coinciden.Count(c => c.Entrada.Tipo is null),
            coinciden.Count(c => TieneRol(c.Entrada, RolBusqueda.Cliente)), coinciden.Count(c => TieneRol(c.Entrada, RolBusqueda.Proveedor)),
            coinciden.Count(c => TieneRol(c.Entrada, RolBusqueda.Contacto)));

        var filtradas = coinciden.Where(c =>
            (filtro.Tipo is null || c.Entrada.Tipo == filtro.Tipo) &&
            (!filtro.SinClasificar || c.Entrada.Tipo is null) &&
            (filtro.Roles is not { Count: > 0 } roles || roles.Any(r => TieneRol(c.Entrada, r)))).ToList();
        var maximo = Math.Max(1, filtro.Maximo);
        var filas = filtradas.Take(maximo).Select(c => new TerceroEncontrado(
            c.Entrada.IdTercero, c.Entrada.Tipo, c.Entrada.Nombre, c.Entrada.NombreComercial, c.Entrada.Nif, c.Entrada.Activo, c.Entrada.Poblacion,
            c.Entrada.Telefono, c.Entrada.Email, c.Entrada.Roles.OrderBy(r => r.Rol).ToList(), c.Entrada.Perfiles.Select(p => p.Nombre).ToList(), c.Donde)).ToList();
        return new ResultadoBusquedaTerceros(filas, filtradas.Count, recuentos);
    }

    private static bool TieneRol(EntradaIndiceTercero e, RolBusqueda rol) => e.Roles.Any(r => r.Rol == rol && r.Activo);

    /// <summary>Minúsculas sin tildes ni signos repetidos: «Lucía  FERRER» → «lucia ferrer».</summary>
    public static string Normalizar(string texto)
    {
        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
        {
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            if (categoria == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Solo letras y cifras: para encontrar teléfonos y NIF escritos con espacios, puntos o guiones.</summary>
    public static string Compactar(string texto) => new(Normalizar(texto).Where(char.IsLetterOrDigit).ToArray());
}

/// <summary>Buscador de terceros (RF-TER-01): un mismo texto en todos los datos de la identidad, sus roles y sus contactos.</summary>
public interface IBuscadorTerceros
{
    Task<ResultadoBusquedaTerceros> BuscarAsync(FiltroTerceros filtro, CancellationToken cancelacion = default);

    /// <summary>Olvida el índice en memoria (tras guardar una ficha) para que la próxima búsqueda vea los cambios.</summary>
    void Invalidar();
}

/// <summary>
/// Buscador sobre Access: carga el índice con una consulta por tabla (no una por tercero; ver el informe de
/// rendimiento del listado de 01/10/2026) y lo reutiliza unos segundos.
/// </summary>
public sealed class BuscadorTerceros(IBaseDatos bd, TimeSpan? vigencia = null) : IBuscadorTerceros
{
    public const int MaximoPredeterminado = 500;

    private readonly TimeSpan _vigencia = vigencia ?? TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _cargando = new(1, 1);
    private (IndiceTerceros Indice, DateTime Hora)? _cache;

    public async Task<ResultadoBusquedaTerceros> BuscarAsync(FiltroTerceros filtro, CancellationToken cancelacion = default)
    {
        var indice = await IndiceAsync(cancelacion);
        cancelacion.ThrowIfCancellationRequested();
        return indice.Buscar(filtro);
    }

    public void Invalidar() => _cache = null;

    private async Task<IndiceTerceros> IndiceAsync(CancellationToken cancelacion)
    {
        if (_cache is { } c && DateTime.UtcNow - c.Hora < _vigencia) return c.Indice;
        await _cargando.WaitAsync(cancelacion);
        try
        {
            if (_cache is { } c2 && DateTime.UtcNow - c2.Hora < _vigencia) return c2.Indice;
            var indice = new IndiceTerceros(await bd.EjecutarAsync(CargarAsync, cancelacion));
            _cache = (indice, DateTime.UtcNow);
            return indice;
        }
        finally { _cargando.Release(); }
    }

    /// <summary>Carga todas las entradas del índice con una consulta por tabla.</summary>
    public static async Task<IReadOnlyList<EntradaIndiceTercero>> CargarAsync(ISesionDatos s)
    {
        var terceros = (await s.ConsultarAsync(
            "SELECT IdTercero, TipoPersona, Nombre, RazonSocial, NombrePersona, PrimerApellido, SegundoApellido, NombreComercial, NIF, Telefono, Email, " +
            "Direccion, CodigoPostal, Poblacion, Provincia, Activo FROM Terceros",
            f =>
            {
                var e = new EntradaIndiceTercero
                {
                    IdTercero = f.Entero("IdTercero"), Tipo = RepositorioTercerosAccess.Tipo(f.Texto("TipoPersona")), Nombre = f.Texto("Nombre") ?? "",
                    NombreComercial = f.Texto("NombreComercial"), Nif = f.Texto("NIF"), Activo = f.Logico("Activo"), Poblacion = f.Texto("Poblacion"),
                    Telefono = f.Texto("Telefono"), Email = f.Texto("Email"),
                };
                e.Anadir("Nombre", e.Nombre);
                e.Anadir("Razón social", f.Texto("RazonSocial"));
                e.Anadir("Nombre y apellidos", string.Join(' ', new[] { f.Texto("NombrePersona"), f.Texto("PrimerApellido"), f.Texto("SegundoApellido") }.Where(x => !string.IsNullOrWhiteSpace(x))));
                e.Anadir("Nombre comercial", e.NombreComercial);
                e.Anadir("NIF", e.Nif);
                e.Anadir("IdTercero", e.IdTercero.ToString(CultureInfo.InvariantCulture));
                e.Anadir("Teléfono", e.Telefono);
                e.Anadir("Email", e.Email);
                e.Anadir("Domicilio", string.Join(' ', new[] { f.Texto("Direccion"), f.Texto("CodigoPostal"), f.Texto("Poblacion"), f.Texto("Provincia") }.Where(x => !string.IsNullOrWhiteSpace(x))));
                return e;
            })).ToDictionary(e => e.IdTercero);

        EntradaIndiceTercero? De(int id) => terceros.GetValueOrDefault(id);

        foreach (var (rol, sql, clave) in new[]
                 {
                     (RolBusqueda.Cliente, "SELECT IdCliente AS Id, IdTercero, Activo, CuentaContable AS Extra FROM Clientes", "IdCliente"),
                     (RolBusqueda.Proveedor, "SELECT IdProveedor AS Id, IdTercero, Activo, NULL AS Extra FROM Proveedores", "IdProveedor"),
                     (RolBusqueda.Contacto, "SELECT IdContacto AS Id, IdTercero, Activo, Cargo AS Extra FROM Contactos", "IdContacto"),
                 })
            foreach (var (id, idTercero, activo, extra) in await s.ConsultarAsync(sql, f => (f.Entero("Id"), f.Entero("IdTercero"), f.Logico("Activo"), f.Texto("Extra"))))
                if (De(idTercero) is { } e)
                {
                    e.Roles.Add(new RolResultado(rol, id, activo));
                    e.Anadir(clave, id.ToString(CultureInfo.InvariantCulture));
                    e.Anadir(rol == RolBusqueda.Cliente ? "Cuenta contable" : "Cargo", extra);
                }

        foreach (var (idTercero, tipo, valor, etiqueta) in await s.ConsultarAsync("SELECT IdTercero, Tipo, Valor, Etiqueta FROM DatosContacto",
                     f => (f.Entero("IdTercero"), f.Texto("Tipo"), f.Texto("Valor"), f.Texto("Etiqueta"))))
            if (De(idTercero) is { } e)
            {
                var esEmail = string.Equals(tipo, "EMAIL", StringComparison.OrdinalIgnoreCase);
                e.Anadir(esEmail ? "Email" : "Teléfono", valor);
                if (esEmail) e.Email ??= valor; else e.Telefono ??= valor;
            }

        foreach (var (idTercero, texto) in await s.ConsultarAsync("SELECT IdTercero, Direccion, CodigoPostal, Poblacion, Provincia FROM DomiciliosTercero",
                     f => (f.Entero("IdTercero"), string.Join(' ', new[] { f.Texto("Direccion"), f.Texto("CodigoPostal"), f.Texto("Poblacion"), f.Texto("Provincia") }.Where(x => !string.IsNullOrWhiteSpace(x))))))
            De(idTercero)?.Anadir("Domicilio", texto);

        foreach (var (idTercero, nombre) in await s.ConsultarAsync("SELECT IdTercero, Nombre, Departamento FROM ContactosLibresTercero",
                     f => (f.Entero("IdTercero"), string.Join(" · ", new[] { f.Texto("Nombre"), f.Texto("Departamento") }.Where(x => !string.IsNullOrWhiteSpace(x))))))
            De(idTercero)?.Anadir("Persona de contacto", nombre);

        foreach (var (idTercero, idPerfil, nombre) in await s.ConsultarAsync(
                     "SELECT pt.IdTercero, pt.IdPerfil, p.Nombre FROM PerfilesTerceros AS pt INNER JOIN Perfiles AS p ON pt.IdPerfil = p.IdPerfil WHERE pt.Activo = True",
                     f => (f.Entero("IdTercero"), f.Entero("IdPerfil"), f.Texto("Nombre") ?? "")))
            if (De(idTercero) is { } e)
            {
                e.Perfiles.Add((idPerfil, nombre));
                e.Anadir("Perfil", nombre);
            }

        foreach (var (persona, organizacion) in await s.ConsultarAsync(
                     "SELECT c.IdTercero AS Persona, r.IdTerceroOrganizacion AS Organizacion FROM RelacionesContactos AS r INNER JOIN Contactos AS c ON r.IdContacto = c.IdContacto",
                     f => (f.Entero("Persona"), f.Entero("Organizacion"))))
        {
            if (De(persona) is { } p && De(organizacion) is { } o)
            {
                o.Anadir("Persona de contacto", p.Nombre);
                p.Anadir("Organización", o.Nombre);
            }
        }

        foreach (var sql in new[]
                 {
                     "SELECT DISTINCT c.IdTercero, cc.Codigo FROM (CuentasClientesSociedad AS x INNER JOIN Clientes AS c ON x.IdCliente = c.IdCliente) INNER JOIN CuentasContables AS cc ON x.IdIdentidadContable = cc.IdIdentidadContable",
                     "SELECT DISTINCT p.IdTercero, cc.Codigo FROM (CuentasProveedoresSociedad AS x INNER JOIN Proveedores AS p ON x.IdProveedor = p.IdProveedor) INNER JOIN CuentasContables AS cc ON x.IdIdentidadContable = cc.IdIdentidadContable",
                 })
            foreach (var (idTercero, codigo) in await s.ConsultarAsync(sql, f => (f.Entero("IdTercero"), f.Texto("Codigo"))))
                De(idTercero)?.Anadir("Cuenta contable", codigo);

        return terceros.Values.ToList();
    }
}
