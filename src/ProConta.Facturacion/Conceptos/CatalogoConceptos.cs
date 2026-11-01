using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Facturacion.Conceptos;

/// <summary>
/// Concepto facturable de una sociedad (facturacion/conceptos): valores que se proponen al añadirlo a una línea. La
/// cuenta de venta se guarda como identidad contable, así vale en cada ejercicio aunque cambie su código. Cambiar un
/// concepto no reescribe las líneas ya hechas.
/// </summary>
public sealed record Concepto(
    int IdConcepto,
    string IdSociedad,
    int? IdTipoConcepto,
    string Nombre,
    string? Descripcion,
    int? IdIdentidadContable,
    string? CuentaVenta,
    string? NombreCuentaVenta,
    bool CuentaEnEjercicio,
    int? IdDepartamento,
    int? IdCentroCoste,
    decimal PrecioUnitario,
    decimal? TipoIva,
    bool Activo,
    string? Notas);

/// <summary>Dónde se usa un concepto.</summary>
public sealed record UsoConcepto(int LineasFactura, int LineasContrato, int LineasProgramadas, IReadOnlyList<string> UltimasFacturas)
{
    public bool EnUso => LineasFactura + LineasContrato + LineasProgramadas > 0;

    public IReadOnlyList<string> Frases() =>
        new[] { (LineasFactura, "líneas de factura"), (LineasContrato, "líneas de contrato"), (LineasProgramadas, "líneas de facturas programadas") }
            .Where(x => x.Item1 > 0).Select(x => $"{x.Item1.ToString("N0", CultureInfo.CurrentCulture)} {x.Item2}").ToList();
}

public sealed record TipoConcepto(int IdTipoConcepto, string Descripcion, bool Activo);

public sealed class CatalogoConceptos(IBaseDatos bd, ICatalogoCuentas cuentas)
{
    public const int LongitudNombre = 255;

    private const string Columnas = "c.IdConcepto, c.IdSociedad, c.IdTipoConcepto, c.Nombre, c.Descripcion, c.IdIdentidadContable, c.IdDepartamento, c.IdCentroCoste, c.PrecioUnitario, c.TipoIVA, c.Activo, c.Notas";

    /// <summary>Conceptos de la sociedad de trabajo con su cuenta de venta resuelta en el ejercicio de trabajo.</summary>
    public async Task<IReadOnlyList<Concepto>> ListarAsync(ContextoSociedadEjercicio contexto, bool soloActivos = false)
    {
        var filas = await bd.EjecutarAsync(s => s.ConsultarAsync(
            // El filtro del ejercicio va en una subconsulta: Access no admite bien parámetros en el ON de un LEFT JOIN.
            $"SELECT {Columnas}, cc.Codigo AS CodigoCuenta, cc.Nombre AS NombreCuenta FROM Conceptos AS c LEFT JOIN " +
            "(SELECT IdIdentidadContable, Codigo, Nombre FROM CuentasContables WHERE IdSociedadEjercicio = ?) AS cc " +
            "ON c.IdIdentidadContable = cc.IdIdentidadContable WHERE c.IdSociedad = ?" + (soloActivos ? " AND c.Activo = True" : "") + " ORDER BY c.Nombre",
            Mapear, contexto.IdSociedadEjercicio, contexto.IdSociedad));
        // La identidad sin cuenta en este ejercicio muestra su último código conocido (para avisar y ofrecer crearla).
        var r = new List<Concepto>();
        foreach (var c in filas)
            r.Add(c.CuentaVenta is null && c.IdIdentidadContable is int id && await cuentas.UltimaCuentaDeIdentidadAsync(id) is { } ultima
                ? c with { CuentaVenta = ultima.Codigo, NombreCuentaVenta = ultima.Nombre, CuentaEnEjercicio = false }
                : c);
        return r;
    }

    public async Task<Concepto?> ObtenerAsync(int idConcepto, ContextoSociedadEjercicio contexto) =>
        (await ListarAsync(contexto)).FirstOrDefault(c => c.IdConcepto == idConcepto);

    private static Concepto Mapear(IFilaDatos f) => new(
        f.Entero("IdConcepto"),
        f.Texto("IdSociedad")?.Trim() ?? "",
        f.EnteroNulo("IdTipoConcepto"),
        f.Texto("Nombre") ?? "",
        f.Texto("Descripcion"),
        f.EnteroNulo("IdIdentidadContable"),
        f.Texto("CodigoCuenta"),
        f.Texto("NombreCuenta"),
        !f.EsNulo("CodigoCuenta"),
        f.EnteroNulo("IdDepartamento"),
        f.EnteroNulo("IdCentroCoste"),
        f.EsNulo("PrecioUnitario") ? 0m : f.Decimal("PrecioUnitario"),
        f.EsNulo("TipoIVA") ? null : Math.Round((decimal)f.Doble("TipoIVA"), 2),
        f.Logico("Activo"),
        f.Texto("Notas"));

    /// <summary>
    /// Alta o modificación. <paramref name="codigoCuenta"/> es el código de la cuenta de venta en el ejercicio de trabajo
    /// (se guarda su identidad); vacío deja el concepto sin cuenta. Un concepto nuevo recibe el siguiente IdConcepto libre.
    /// </summary>
    public async Task<Resultado<Concepto>> GuardarAsync(Concepto concepto, string? codigoCuenta, ContextoSociedadEjercicio contexto)
    {
        var nombre = concepto.Nombre?.Trim() ?? "";
        var registro = concepto.IdConcepto > 0 ? $"Concepto {concepto.IdConcepto}" : "Concepto nuevo";
        if (nombre.Length == 0)
            return Resultado<Concepto>.Fallo(new Error("Conceptos.SinNombre", "Falta el nombre del concepto.", registro, null, "Escribe cómo aparece en la factura."));
        if (nombre.Length > LongitudNombre)
            return Resultado<Concepto>.Fallo(new Error("Conceptos.NombreLargo", $"El nombre tiene más de {LongitudNombre} caracteres.", registro, null, "Acórtalo."));
        if (concepto.TipoIva is < 0m or > 100m)
            return Resultado<Concepto>.Fallo(new Error("Conceptos.IvaFueraDeRango", "El IVA debe estar entre 0 y 100 %.", registro));
        int? identidad = concepto.IdIdentidadContable;
        if (string.IsNullOrWhiteSpace(codigoCuenta)) identidad = null;
        else if (!(concepto.CuentaVenta == codigoCuenta.Trim() && identidad is not null))
        {
            var cuenta = await cuentas.ResolverAsync(contexto, codigoCuenta);
            if (!cuenta.EsCorrecto) return Resultado<Concepto>.Fallo(cuenta.Errores);
            var admite = ReglasCuenta.AdmiteApuntes(cuenta.Valor);
            if (!admite.EsCorrecto) return Resultado<Concepto>.Fallo(admite.Errores);
            identidad = cuenta.Valor.IdIdentidadContable;
        }
        try
        {
            var id = await bd.EnTransaccionAsync(async s =>
            {
                var repetido = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Conceptos WHERE IdSociedad = ? AND Nombre = ? AND IdConcepto <> ?",
                    contexto.IdSociedad, nombre, concepto.IdConcepto), CultureInfo.InvariantCulture);
                if (repetido > 0)
                    throw new UnicidadException(new Error("Conceptos.Repetido", $"Ya hay un concepto «{nombre}» en esta empresa.", registro, null,
                        "Usa ese concepto o cambia el nombre."));
                // CuentaContable (7 caracteres) queda como dato heredado: se rellena si el código cabe, para el ERP.
                var heredada = codigoCuenta?.Trim() is { Length: > 0 and <= 7 } c ? c : null;
                if (concepto.IdConcepto <= 0)
                {
                    var nuevo = await s.SiguienteNumeroAsync("Conceptos", "IdConcepto");
                    await s.EjecutarAsync(
                        "INSERT INTO Conceptos (IdConcepto, IdSociedadEjercicio, IdSociedad, IdTipoConcepto, Nombre, Descripcion, IdIdentidadContable, CuentaContable, IdDepartamento, IdCentroCoste, PrecioUnitario, TipoIVA, Activo, Notas) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        nuevo, contexto.IdSociedadEjercicio, contexto.IdSociedad, concepto.IdTipoConcepto, nombre, Vacio(concepto.Descripcion), identidad, heredada,
                        concepto.IdDepartamento, concepto.IdCentroCoste, concepto.PrecioUnitario, concepto.TipoIva is decimal t ? (double)t : null, concepto.Activo, Vacio(concepto.Notas));
                    return nuevo;
                }
                var filas = await s.EjecutarAsync(
                    "UPDATE Conceptos SET IdTipoConcepto = ?, Nombre = ?, Descripcion = ?, IdIdentidadContable = ?, CuentaContable = ?, IdDepartamento = ?, IdCentroCoste = ?, PrecioUnitario = ?, TipoIVA = ?, Activo = ?, Notas = ? " +
                    "WHERE IdConcepto = ? AND IdSociedad = ?",
                    concepto.IdTipoConcepto, nombre, Vacio(concepto.Descripcion), identidad, heredada, concepto.IdDepartamento, concepto.IdCentroCoste,
                    concepto.PrecioUnitario, concepto.TipoIva is decimal t2 ? (double)t2 : null, concepto.Activo, Vacio(concepto.Notas), concepto.IdConcepto, contexto.IdSociedad);
                if (filas == 0)
                    throw new ConflictoConcurrenciaException(new Error("Conceptos.NoExiste", "El concepto ya no existe en esta empresa.", registro, "Otro operador lo ha borrado.", "Actualiza la lista."));
                return concepto.IdConcepto;
            });
            return Resultado<Concepto>.Correcto((await ObtenerAsync(id, contexto))!);
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<Concepto>.Fallo(ex.Error);
        }
    }

    /// <summary>Copia del concepto («… (copia)»), inactiva hasta que se revise, para no ofrecer dos iguales en las facturas.</summary>
    public async Task<Resultado<Concepto>> DuplicarAsync(int idConcepto, ContextoSociedadEjercicio contexto)
    {
        var c = await ObtenerAsync(idConcepto, contexto);
        if (c is null) return Resultado<Concepto>.Fallo(new Error("Conceptos.NoExiste", "El concepto ya no existe.", $"Concepto {idConcepto}"));
        var existentes = (await ListarAsync(contexto)).Select(x => x.Nombre).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var nombre = $"{c.Nombre} (copia)";
        for (var i = 2; existentes.Contains(nombre); i++) nombre = $"{c.Nombre} (copia {i})";
        if (nombre.Length > LongitudNombre) nombre = nombre[^LongitudNombre..];
        return await GuardarAsync(c with { IdConcepto = 0, Nombre = nombre, Activo = false }, c.CuentaEnEjercicio ? c.CuentaVenta : null, contexto);
    }

    public Task<UsoConcepto> UsoAsync(int idConcepto) =>
        bd.EjecutarAsync(async s =>
        {
            async Task<int> Contar(string tabla) => Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM [{tabla}] WHERE IdConcepto = ?", idConcepto), CultureInfo.InvariantCulture);
            var ultimas = await s.ConsultarAsync(
                "SELECT TOP 5 f.IdFactura, f.NumeroFactura, f.FechaFactura FROM Facturas AS f WHERE f.IdFactura IN (SELECT IdFactura FROM LineasFactura WHERE IdConcepto = ?) ORDER BY f.FechaFactura DESC, f.IdFactura DESC",
                f => $"{f.Texto("NumeroFactura") ?? $"Borrador B-{f.Entero("IdFactura"):0000}"}{(f.FechaNula("FechaFactura") is DateTime d ? $" · {d:dd/MM/yyyy}" : "")}", idConcepto);
            return new UsoConcepto(await Contar("LineasFactura"), await Contar("LineasContrato"), await Contar("LineasFacturasProgramadas"), ultimas);
        });

    /// <summary>Borra un concepto que nunca se ha usado; si se ha usado, se explica y se propone desactivarlo.</summary>
    public async Task<Resultado> EliminarAsync(int idConcepto, ContextoSociedadEjercicio contexto)
    {
        var uso = await UsoAsync(idConcepto);
        if (uso.EnUso)
            return Resultado.Fallo(new Error("Conceptos.EnUso", $"No se puede borrar: el concepto se usa en {string.Join(", ", uso.Frases())}.", $"Concepto {idConcepto}",
                "Las líneas ya hechas siguen apuntando a él.", "Desactívalo para que no se ofrezca en facturas nuevas."));
        await bd.EjecutarAsync(s => s.EjecutarAsync("DELETE FROM Conceptos WHERE IdConcepto = ? AND IdSociedad = ?", idConcepto, contexto.IdSociedad));
        return Resultado.Correcto();
    }

    public Task<IReadOnlyList<TipoConcepto>> TiposAsync() =>
        bd.EjecutarAsync(s => s.ConsultarAsync("SELECT IdTipoConcepto, Descripcion, Activo FROM TiposConcepto ORDER BY Descripcion",
            f => new TipoConcepto(f.Entero("IdTipoConcepto"), f.Texto("Descripcion") ?? "", f.Logico("Activo"))));

    private static object? Vacio(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
