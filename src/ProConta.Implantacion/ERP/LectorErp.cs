using System.Globalization;
using System.Text;
using System.Data.OleDb;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Resultados;

namespace ProConta.Implantacion.ERP;

/// <summary>Dirección de un cliente del fichero Direcciones.csv (fuera de Git), ya formateada.</summary>
public sealed record DireccionErp(int IdPersona, string Direccion, string? CodigoPostal, string? Poblacion, string? Provincia, string? Pais);

/// <summary>Lo leído del ERP: cada tabla por su nombre, los apuntes y registros de IVA para enlazar y las direcciones.</summary>
public sealed class OrigenErp
{
    public required string Ruta { get; init; }
    public required string Huella { get; init; }
    public string? RutaDirecciones { get; init; }
    public string? HuellaDirecciones { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<Fila>> Tablas { get; init; }

    /// <summary>Apuntes de cliente (43…), de ingresos (7…) y de IVA repercutido (477…) del ERP con documento, con su asiento («DIA:ASI») y su fecha.</summary>
    public required IReadOnlyList<(int Ejercicio, string Documento, int Diario, int Asiento, DateTime Fecha, bool EsCliente)> ApuntesCliente { get; init; }

    /// <summary>F_IVR de cada ejercicio: (ejercicio, código, número de factura, fecha de registro).</summary>
    public required IReadOnlyList<(int Ejercicio, int Codigo, string? Factura, DateTime? Fecha)> RegistrosRepercutido { get; init; }

    /// <summary>F_IVS de cada ejercicio: códigos existentes.</summary>
    public required IReadOnlySet<(int Ejercicio, int Codigo)> RegistrosSoportado { get; init; }

    /// <summary>Filas de las tablas que no se migran pero se nombran en el informe (nóminas).</summary>
    public required IReadOnlyDictionary<string, int> NoMigradas { get; init; }

    public required IReadOnlyList<DireccionErp> Direcciones { get; init; }

    public IReadOnlyList<Fila> Tabla(string nombre) => Tablas.TryGetValue(nombre, out var t) ? t : [];
}

/// <summary>
/// Lee el ERP (ProcontaEstructura.accdb) siempre en solo lectura: el origen nunca se modifica. Lee entera cada tabla que se
/// migra, y de los F_APU/F_IVR/F_IVS de cada ejercicio solo lo que hace falta para enlazar con lo importado de ContaSol.
/// </summary>
public sealed class LectorErp
{
    /// <summary>Tablas que se leen enteras (todas sus columnas).</summary>
    public static readonly IReadOnlyList<string> TablasMigradas =
    [
        "Sociedades", "FormasPago", "Agrupaciones", "Personas", "DatosContacto", "CategoriasIVA", "CategoriasIVAPorDepartamento",
        "ConceptosCaja", "TiposConceptosCaja", "FacturasVenta", "LineasFacturaVenta", "FacturasProgramadas", "LineasFacturaProgramada",
        "FacturasCompra", "PalabrasClaveSubDepartamento", "PalabrasClaveTipoGasto", "N43LotesImportacion", "N43Cabeceras", "N43Lineas",
        "N43EntidadesBancarias", "N43PalabrasClave", "Inmuebles", "PlantillasInmuebles", "CuentasInmuebles", "TiposInmueble",
    ];

    /// <summary>Tablas que no se migran (decisión del propietario) y que el informe nombra con sus filas.</summary>
    public static readonly IReadOnlyList<string> TablasNominas = ["F_NOM", "F_NOM_ALIAS", "AliasCamposF_NOM", "CamposF_NOM", "PlantillasApuntes"];

    public async Task<Resultado<OrigenErp>> LeerAsync(string ruta, string? rutaDirecciones, CancellationToken cancelacion = default)
    {
        if (!File.Exists(ruta))
            return Resultado<OrigenErp>.Fallo(new Error("Erp.NoExiste", $"No encuentro la BD del ERP {ruta}.", ruta, "El fichero no existe o no tengo acceso a él.",
                "Comprueba la ruta de ProcontaEstructura.accdb."));
        if (rutaDirecciones is not null && !File.Exists(rutaDirecciones))
            return Resultado<OrigenErp>.Fallo(new Error("Erp.SinDirecciones", $"No encuentro el fichero de direcciones {rutaDirecciones}.", rutaDirecciones,
                "El fichero no existe.", "Comprueba la ruta de Direcciones.csv."));
        IReadOnlyList<DireccionErp> direcciones = [];
        if (rutaDirecciones is not null)
        {
            var leidas = LeerDirecciones(rutaDirecciones);
            if (!leidas.EsCorrecto) return Resultado<OrigenErp>.Fallo(leidas.Errores);
            direcciones = leidas.Valor;
        }
        try
        {
            return await Task.Run(() => Leer(ruta, rutaDirecciones, direcciones, cancelacion), cancelacion);
        }
        catch (OleDbException ex)
        {
            return Resultado<OrigenErp>.Fallo(new Error("Erp.NoSeAbre", $"No he podido leer la BD del ERP {Path.GetFileName(ruta)}.", ruta, ex.Message,
                "Comprueba que el fichero no está dañado y que el ERP no lo tiene abierto en exclusiva."));
        }
    }

    /// <summary>
    /// Lectura con OLE DB en solo lectura (Mode=Read). No se usa el lector de esquema del programa: la copia del ERP tiene
    /// tablas cuyo esquema completo (índices y relaciones) ACE no sabe describir; aquí solo hacen falta tablas y columnas.
    /// </summary>
    private static Resultado<OrigenErp> Leer(string ruta, string? rutaDirecciones, IReadOnlyList<DireccionErp> direcciones, CancellationToken cancelacion)
    {
        using var cn = new OleDbConnection($"Provider={BaseDatosAccess.Proveedor};Data Source={Path.GetFullPath(ruta)};Mode=Read;Persist Security Info=False;OLE DB Services=-4");
        cn.Open();
        var existentes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var t = cn.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, [null, null, null, "TABLE"])!)
            foreach (System.Data.DataRow r in t.Rows) existentes.Add((string)r["TABLE_NAME"]);
        var faltan = TablasMigradas.Where(t => !existentes.Contains(t)).ToList();
        if (faltan.Count > 0)
            return Resultado<OrigenErp>.Fallo(new Error("Erp.NoReconocida", "Esta BD no tiene las tablas del ERP que sé migrar.", ruta,
                $"Faltan: {string.Join(", ", faltan)}.", "Elige la copia materializada ProcontaEstructura.accdb."));
        var tablas = new Dictionary<string, IReadOnlyList<Fila>>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TablasMigradas)
        {
            cancelacion.ThrowIfCancellationRequested();
            tablas[t] = Consultar(cn, $"SELECT * FROM [{t}]");
        }
        var apuntes = new List<(int, string, int, int, DateTime, bool)>();
        var repercutido = new List<(int, int, string?, DateTime?)>();
        var soportado = new HashSet<(int, int)>();
        var anioActual = AnioTablasSinSufijo(tablas["Sociedades"]);
        string? TablaAnio(string prefijo, int anio) =>
            existentes.Contains($"{prefijo}{anio}") ? $"{prefijo}{anio}" : anio == anioActual && existentes.Contains(prefijo) ? prefijo : null;
        for (var anio = 2000; anio <= 2100; anio++)
        {
            if (TablaAnio("F_APU", anio) is { } apu)
                foreach (var f in Consultar(cn, $"SELECT DIAAPU, ASIAPU, FECAPU, DOCAPU, CUEAPU FROM [{apu}] WHERE (CUEAPU LIKE '43%' OR CUEAPU LIKE '7%' OR CUEAPU LIKE '477%') AND DOCAPU IS NOT NULL"))
                    if (f.T("DOCAPU") is { } doc && f.F("FECAPU") is DateTime fecha)
                        apuntes.Add((anio, doc, f.E("DIAAPU") ?? 1, f.E("ASIAPU") ?? 0, fecha, f.T("CUEAPU")?.StartsWith("43", StringComparison.Ordinal) == true));
            if (TablaAnio("F_IVR", anio) is { } ivr)
                foreach (var f in Consultar(cn, $"SELECT CODIVR, FACIVR, FECIVR FROM [{ivr}]"))
                    repercutido.Add((anio, f.E("CODIVR") ?? 0, f.T("FACIVR"), f.F("FECIVR")));
            if (TablaAnio("F_IVS", anio) is { } ivs)
                foreach (var f in Consultar(cn, $"SELECT CODIVS FROM [{ivs}]")) soportado.Add((anio, f.E("CODIVS") ?? 0));
        }
        var noMigradas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in TablasNominas.Where(existentes.Contains))
            noMigradas[t] = Consultar(cn, $"SELECT COUNT(*) AS N FROM [{t}]")[0].E("N") ?? 0;
        cn.Close();
        return Resultado<OrigenErp>.Correcto(new OrigenErp
        {
            Ruta = Path.GetFullPath(ruta),
            Huella = LectorContaSol.Huella(ruta),
            RutaDirecciones = rutaDirecciones is null ? null : Path.GetFullPath(rutaDirecciones),
            HuellaDirecciones = rutaDirecciones is null ? null : LectorContaSol.Huella(rutaDirecciones),
            Tablas = tablas,
            ApuntesCliente = apuntes,
            RegistrosRepercutido = repercutido,
            RegistrosSoportado = soportado,
            NoMigradas = noMigradas,
            Direcciones = direcciones,
        });
    }

    private static List<Fila> Consultar(OleDbConnection cn, string sql)
    {
        using var cmd = new OleDbCommand(sql, cn);
        using var r = cmd.ExecuteReader()!;
        var columnas = Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray();
        var filas = new List<Fila>();
        while (r.Read())
        {
            var valores = new Dictionary<string, object?>(columnas.Length, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columnas.Length; i++) valores[columnas[i]] = r.IsDBNull(i) ? null : r.GetValue(i);
            filas.Add(new Fila(valores));
        }
        return filas;
    }

    /// <summary>Las tablas F_ sin sufijo de año son las del último ejercicio activo del ERP.</summary>
    private static int AnioTablasSinSufijo(IReadOnlyList<Fila> sociedades) =>
        sociedades.Where(s => s.B("Activa")).Select(s => s.E("Ejercicio") ?? 0).DefaultIfEmpty(DateTime.Today.Year).Max();

    /// <summary>
    /// Direcciones.csv: separado por «;», con cabecera IdPersona;Direccion;CP;Poblaciones.Nombre;Provincias.Nombre;Paises.Nombre. Se
    /// abre compartido (puede estar abierto en Excel) y se lee en UTF-8 o, si no lo es, en Windows-1252.
    /// </summary>
    public static Resultado<IReadOnlyList<DireccionErp>> LeerDirecciones(string ruta)
    {
        byte[] bytes;
        using (var flujo = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var memoria = new MemoryStream())
        {
            flujo.CopyTo(memoria);
            bytes = memoria.ToArray();
        }
        return InterpretarDirecciones(bytes, ruta);
    }

    public static Resultado<IReadOnlyList<DireccionErp>> InterpretarDirecciones(byte[] bytes, string ruta)
    {
        string texto;
        try { texto = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            texto = Encoding.GetEncoding(1252).GetString(bytes);
        }
        var lineas = texto.TrimStart('﻿').Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        if (lineas.Length == 0)
            return Resultado<IReadOnlyList<DireccionErp>>.Fallo(new Error("Erp.DireccionesVacio", "El fichero de direcciones está vacío.", ruta));
        var cabecera = lineas[0].Split(';').Select(c => c.Trim()).ToList();
        int Col(params string[] nombres) => cabecera.FindIndex(c => nombres.Any(n => c.Equals(n, StringComparison.OrdinalIgnoreCase)));
        int id = Col("IdPersona"), dir = Col("Direccion"), cp = Col("CP", "CodigoPostal"), pob = Col("Poblaciones.Nombre", "Poblacion"),
            pro = Col("Provincias.Nombre", "Provincia"), pai = Col("Paises.Nombre", "Pais");
        if (id < 0 || dir < 0)
            return Resultado<IReadOnlyList<DireccionErp>>.Fallo(new Error("Erp.DireccionesCabecera", "El fichero de direcciones no tiene la cabecera que espero.", ruta,
                $"Cabecera leída: {lineas[0]}.", "Debe empezar por IdPersona;Direccion;CP;Poblaciones.Nombre;Provincias.Nombre;Paises.Nombre."));
        var r = new List<DireccionErp>();
        foreach (var linea in lineas.Skip(1))
        {
            var c = linea.Split(';');
            string? V(int i) => i >= 0 && i < c.Length && c[i].Trim().Trim('"').Trim() is { Length: > 0 } t ? t : null;
            if (!int.TryParse(V(id), NumberStyles.Integer, CultureInfo.InvariantCulture, out var persona) || V(dir) is not { } via) continue;
            r.Add(new DireccionErp(persona, via, V(cp), V(pob), V(pro), V(pai)));
        }
        return Resultado<IReadOnlyList<DireccionErp>>.Correcto(r);
    }
}
