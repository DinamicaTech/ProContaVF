using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Resultados;

namespace ProConta.Activos.Mantenimiento;

/// <summary>
/// Situación y dirección de los inmuebles para el 180 y el 347 (ActivosInmuebles, esquema 72): los activos de tipo inmueble de la empresa,
/// propios y ajenos, con su referencia catastral, la situación (1 a 4, como la pide el 180) y la dirección estructurada como la piden los
/// diseños de la AEAT. No da de alta ni de baja activos: eso se hace en Activos.
/// </summary>
public sealed class ProveedorInmueblesModelos(IBaseDatos bd) : ProveedorBase
{
    public override string Clave => "inmueblesmodelos";
    public override string Titulo => "Inmuebles para los modelos (180 y 347)";
    public override string Categoria => ProveedoresActivos.Categoria;
    public override string Finalidad =>
        "Situación, referencia catastral y dirección de cada inmueble como las piden el 180 (los que se alquilan con retención) y el registro de inmuebles del 347 (los locales que se alquilan a terceros).";
    public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Sociedad;
    public override bool PermiteAlta => false;
    public override bool PermiteBaja => false;
    public override string NombreRegistro => "inmueble";
    public override IReadOnlyList<string> Sinonimos => ["180", "347", "catastro", "referencia catastral", "direccion", "local", "inmueble", "alquiler"];

    public static readonly IReadOnlyList<OpcionCampo> Situaciones =
    [
        new((byte)1, "1 · Con referencia catastral, fuera del País Vasco y Navarra"), new((byte)2, "2 · Con referencia catastral, en el País Vasco"),
        new((byte)3, "3 · Con referencia catastral, en Navarra"), new((byte)4, "4 · Sin referencia catastral"),
    ];

    private static readonly string[] Textos =
        ["TipoVia", "NombreVia", "TipoNumeracion", "NumeroCasa", "CalificadorNumero", "Bloque", "Portal", "Escalera", "Planta", "Puerta", "Complemento", "Localidad", "Municipio",
         "CodigoMunicipio", "CodigoProvincia", "CodigoPostal"];

    public override IReadOnlyList<CampoMantenimiento> Campos =>
    [
        new("Activo", "Inmueble") { SoloLectura = true, Ancho = "l", Ayuda = "El activo (Activos). Los ajenos son los que alquila la empresa." },
        new("ReferenciaCatastral", "Referencia catastral", TipoCampo.Codigo) { Longitud = 20, Ancho = "m", Ayuda = "Veinte caracteres, como en el recibo del IBI. También se ve en la ficha del activo." },
        new("SituacionInmueble", "Situación", TipoCampo.Lista) { Opciones = Situaciones, Ancho = "l",
            Ayuda = "La del 180. El 347 usa sus propias claves y el programa las traduce (País Vasco y Navarra van juntas; sin referencia es la 3)." },
        new("TipoVia", "Tipo de vía", TipoCampo.Codigo) { Longitud = 5, Ancho = "xs", Ayuda = "Código del INE: CALLE, AVDA, PZA, CTRA, PSO…" },
        new("NombreVia", "Nombre de la vía") { Longitud = 50, Ancho = "l", Ayuda = "Nombre de la calle, avenida…, sin el tipo de vía." },
        new("TipoNumeracion", "Tipo de numeración", TipoCampo.Codigo) { Longitud = 3, Ancho = "xs", Ayuda = "NUM (número), KM. (punto kilométrico) o S/N." },
        new("NumeroCasa", "Número", TipoCampo.Codigo) { Longitud = 5, Ancho = "xs", Ayuda = "Número de casa o punto kilométrico." },
        new("CalificadorNumero", "Calificador", TipoCampo.Codigo) { Longitud = 3, Ancho = "xs", Ayuda = "BIS, DUP… Vacío si no tiene." },
        new("Bloque", "Bloque", TipoCampo.Codigo) { Longitud = 3, Ancho = "xs", Ayuda = "Vacío si no tiene." },
        new("Portal", "Portal", TipoCampo.Codigo) { Longitud = 3, Ancho = "xs", Ayuda = "Vacío si no tiene." },
        new("Escalera", "Escalera", TipoCampo.Codigo) { Longitud = 3, Ancho = "xs", Ayuda = "Vacía si no tiene." },
        new("Planta", "Planta", TipoCampo.Codigo) { Longitud = 3, Ancho = "xs", Ayuda = "Planta o piso: BJ, 01, AT…" },
        new("Puerta", "Puerta", TipoCampo.Codigo) { Longitud = 3, Ancho = "xs", Ayuda = "Vacía si no tiene." },
        new("Complemento", "Complemento") { Longitud = 40, Ancho = "l", Ayuda = "Urbanización, centro comercial, local…" },
        new("Localidad", "Localidad") { Longitud = 30, Ancho = "m", Ayuda = "Solo si es distinta del municipio." },
        new("Municipio", "Municipio") { Longitud = 30, Ancho = "m", Ayuda = "Nombre del municipio, por ejemplo Castelldefels." },
        new("CodigoMunicipio", "Código del municipio (INE)", TipoCampo.Codigo) { Longitud = 5, Ancho = "xs", Ayuda = "Cinco cifras del INE, por ejemplo 08056 (Castelldefels)." },
        new("CodigoProvincia", "Código de la provincia", TipoCampo.Codigo) { Longitud = 2, Ancho = "xs", Ayuda = "Dos cifras, por ejemplo 08." },
        new("CodigoPostal", "Código postal", TipoCampo.Codigo) { Longitud = 5, Ancho = "xs", Ayuda = "Cinco cifras." },
    ];

    public override IReadOnlyList<ColumnaLista> Columnas => [new("Activo", "Inmueble"), new("ReferenciaCatastral", "Referencia catastral", 170), new("Falta", "Falta", 220)];

    public override string TituloRegistro(RegistroMantenimiento r) => r.Texto("Activo") ?? "Inmueble";

    public override async Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento c) => await bd.EjecutarAsync(async s =>
        await s.ConsultarAsync(
            "SELECT a.IdActivo, a.Codigo, a.Nombre, a.EsAjeno, i.IdActivo AS ConFicha, i.ReferenciaCatastral, i.SituacionInmueble, " + string.Join(", ", Textos.Select(t => "i." + t)) + " " +
            "FROM (Activos AS a INNER JOIN TiposActivo AS t ON a.IdTipoActivo = t.IdTipoActivo) LEFT JOIN ActivosInmuebles AS i ON a.IdActivo = i.IdActivo " +
            "WHERE a.IdSociedad = ? AND t.Categoria = 'INMUEBLE' ORDER BY a.Codigo",
            f =>
            {
                var v = new Dictionary<string, object?>
                {
                    ["Activo"] = $"{f.Texto("Codigo")?.Trim()} · {f.Texto("Nombre")?.Trim()}{(f.Logico("EsAjeno") ? " (ajeno)" : "")}",
                    ["ReferenciaCatastral"] = f.Texto("ReferenciaCatastral")?.Trim(),
                    ["SituacionInmueble"] = f.Valor("SituacionInmueble") is null or DBNull ? null : Convert.ToByte(f.Valor("SituacionInmueble"), CultureInfo.InvariantCulture),
                };
                foreach (var t in Textos) v[t] = f.Texto(t)?.Trim();
                var falta = new List<string>();
                if (v["SituacionInmueble"] is null) falta.Add("situación");
                if (string.IsNullOrWhiteSpace(v["NombreVia"] as string)) falta.Add("dirección");
                if (string.IsNullOrWhiteSpace(v["CodigoMunicipio"] as string) || string.IsNullOrWhiteSpace(v["CodigoProvincia"] as string)) falta.Add("municipio y provincia");
                v["Falta"] = falta.Count == 0 ? "" : "Falta " + string.Join(", ", falta);
                return new RegistroMantenimiento(f.Entero("IdActivo"), v);
            }, c.Trabajo.IdSociedad));

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r)
    {
        var e = new List<ErrorCampo>(base.Validar(r));
        if (r.Texto("CodigoProvincia") is { Length: > 0 } p && (p.Trim().Length != 2 || !p.Trim().All(char.IsAsciiDigit)))
            e.Add(new ErrorCampo("CodigoProvincia", "Dos cifras, por ejemplo 08."));
        if (r.Texto("CodigoMunicipio") is { Length: > 0 } m && (m.Trim().Length != 5 || !m.Trim().All(char.IsAsciiDigit)))
            e.Add(new ErrorCampo("CodigoMunicipio", "Cinco cifras del INE, por ejemplo 08056."));
        if (r["SituacionInmueble"] is not null && Convert.ToInt32(r["SituacionInmueble"], CultureInfo.InvariantCulture) is >= 1 and <= 3 && string.IsNullOrWhiteSpace(r.Texto("ReferenciaCatastral")))
            e.Add(new ErrorCampo("ReferenciaCatastral", "Con las situaciones 1 a 3 hace falta la referencia catastral; si no la tiene, elige la situación 4."));
        return e;
    }

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento r, ContextoMantenimiento c)
    {
        var id = Convert.ToInt32(r.Clave, CultureInfo.InvariantCulture);
        static object? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim().ToUpperInvariant();
        var valores = new List<object?>
        {
            Vacio(r.Texto("ReferenciaCatastral")),
            r["SituacionInmueble"] is null ? null : Convert.ToByte(r["SituacionInmueble"], CultureInfo.InvariantCulture),
        };
        valores.AddRange(Textos.Select(t => Vacio(r.Texto(t))));
        var hecho = await bd.EjecutarAsync(async s =>
        {
            var existe = await s.EscalarAsync("SELECT COUNT(*) FROM Activos WHERE IdActivo = ? AND IdSociedad = ?", id, c.Trabajo.IdSociedad);
            if (Convert.ToInt32(existe, CultureInfo.InvariantCulture) == 0) return false;
            var conFicha = Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM ActivosInmuebles WHERE IdActivo = ?", id), CultureInfo.InvariantCulture) > 0;
            var columnas = new[] { "ReferenciaCatastral", "SituacionInmueble" }.Concat(Textos).ToList();
            if (conFicha)
                await s.EjecutarAsync($"UPDATE ActivosInmuebles SET {string.Join(", ", columnas.Select(x => x + " = ?"))} WHERE IdActivo = ?", [.. valores, id]);
            else
                await s.EjecutarAsync($"INSERT INTO ActivosInmuebles ({string.Join(", ", columnas)}, IdActivo) VALUES ({string.Join(", ", columnas.Select(_ => "?"))}, ?)", [.. valores, id]);
            return true;
        });
        if (!hecho) return Resultado<RegistroMantenimiento>.Fallo(new Error("Inmuebles.NoExiste", "El activo ya no existe en esta empresa.", null, null, "Actualiza la lista."));
        var guardado = (await ListarAsync(c)).First(x => Convert.ToInt32(x.Clave, CultureInfo.InvariantCulture) == id);
        return Resultado<RegistroMantenimiento>.Correcto(guardado);
    }
}
