using System.Globalization;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Facturas;

/// <summary>Cliente que se puede elegir en una factura: su rol Cliente con los datos actuales del tercero.</summary>
public sealed record ClienteElegible(int IdCliente, int IdTercero, string Nombre, string? Nif, string Domicilio, bool Activo, string? NombreAsiento = null)
{
    public string Rotulo => string.IsNullOrWhiteSpace(Nif) ? Nombre : $"{Nombre} · {Nif}";
}

/// <summary>Tipo de factura de la AEAT (F1…F3, R1…R5).</summary>
public sealed record TipoFacturaAeat(string Codigo, string Descripcion, bool EsRectificativa)
{
    public string Rotulo => $"{Codigo} · {Descripcion}";
}

/// <summary>
/// Consultas de apoyo del editor del borrador (facturacion/pantallas): buscar cliente, sus cuentas en la sociedad y
/// los catálogos de tipos de IVA y de factura. Solo lectura.
/// </summary>
public sealed class ApoyoEditorFacturas(IBaseDatos bd, ServicioRoles roles)
{
    private const string SqlClientes =
        "SELECT c.IdCliente, c.IdTercero, c.Activo, t.Nombre, t.RazonSocial, t.NIF, t.Direccion, t.CodigoPostal, t.Poblacion, t.TipoPersona, t.NombrePersona, t.PrimerApellido, t.NombreComercial " +
        "FROM Clientes AS c INNER JOIN Terceros AS t ON c.IdTercero = t.IdTercero ";

    /// <summary>Clientes cuyo nombre, razón social o NIF contienen el texto, o cuyo Id es ese número; los activos primero. Máximo 50.</summary>
    public async Task<IReadOnlyList<ClienteElegible>> BuscarClientesAsync(string texto)
    {
        var t = (texto ?? "").Trim();
        if (t.Length == 0) return [];
        var patron = "%" + t.Replace("[", "[[]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal) + "%";
        var id = int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : -1;
        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync(
            SqlClientes + "WHERE t.Nombre LIKE ? OR t.RazonSocial LIKE ? OR t.NIF LIKE ? OR c.IdCliente = ?", Mapear, patron, patron, patron, id));
        return filas.OrderByDescending(c => c.Activo).ThenByDescending(c => c.IdCliente == id)
            .ThenBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).Take(50).ToList();
    }

    public Task<ClienteElegible?> ClienteAsync(int idCliente) =>
        bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(SqlClientes + "WHERE c.IdCliente = ?", Mapear, idCliente));

    /// <summary>Serie de 472 de la actividad de cada centro de coste de la sociedad, para la vista previa del asiento.</summary>
    public async Task<IReadOnlyDictionary<int, string>> SeriesPorCentroAsync(string idSociedad) =>
        (await new ProConta.Contabilidad.CentrosCoste.LectorActividadesIva(bd).PorCentroAsync(idSociedad))
            .Where(a => a.Value.Serie is { Length: 4 }).ToDictionary(a => a.Key, a => a.Value.Serie!);

    /// <summary>Cuentas del cliente y de retención del rol Cliente en la sociedad de trabajo.</summary>
    public async Task<(CuentaRol? Cliente, CuentaRol? Retencion)> CuentasClienteAsync(int idCliente, ContextoSociedadEjercicio contexto)
    {
        var idTercero = await bd.EjecutarAsync(s => Instantaneas.TerceroDelClienteAsync(s, idCliente));
        if (idTercero is not int it) return (null, null);
        var c = await roles.CuentasAsync(it, contexto);
        return (c.Cliente, c.Retencion);
    }

    /// <summary>Porcentajes de IVA activos, de mayor a menor; si la tabla está vacía, 21, 10, 4 y 0.</summary>
    public async Task<IReadOnlyList<decimal>> TiposIvaAsync()
    {
        var lista = await CatalogoTiposIva.ActivosAsync(bd);
        return lista.Count > 0 ? lista : [21m, 10m, 4m, 0m];
    }

    public async Task<IReadOnlyList<TipoFacturaAeat>> TiposFacturaAsync()
    {
        var tipos = await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT TipoFactura, Descripcion, EsRectificativa FROM TiposFactura ORDER BY TipoFactura",
            f => new TipoFacturaAeat(f.Texto("TipoFactura")?.Trim() ?? "", f.Texto("Descripcion")?.Trim() ?? "", !f.EsNulo("EsRectificativa") && f.Logico("EsRectificativa"))));
        return tipos.Count > 0 ? tipos :
        [
            new("F1", "Factura", false), new("F2", "Factura simplificada", false), new("F3", "Factura en sustitución de simplificadas", false),
            new("R4", "Rectificativa (resto)", true), new("R5", "Rectificativa de simplificada", true)
        ];
    }

    private static ClienteElegible Mapear(IFilaDatos f)
    {
        var razon = f.Texto("RazonSocial");
        var nombre = (string.IsNullOrWhiteSpace(razon) ? f.Texto("Nombre") : razon)?.Trim() ?? "";
        var domicilio = string.Join(" · ", new[] { f.Texto("Direccion"), string.Join(" ", new[] { f.Texto("CodigoPostal"), f.Texto("Poblacion") }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim())) }
            .Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
        var fisica = string.Equals(f.Texto("TipoPersona")?.Trim(), "FISICA", StringComparison.OrdinalIgnoreCase);
        return new ClienteElegible(f.Entero("IdCliente"), f.Entero("IdTercero"), nombre, f.Texto("NIF")?.Trim(), domicilio, f.EsNulo("Activo") || f.Logico("Activo"),
            Instantaneas.NombreCliente(fisica, f.Texto("NombrePersona"), f.Texto("PrimerApellido"), f.Texto("NombreComercial"), razon, f.Texto("Nombre")));
    }
}
