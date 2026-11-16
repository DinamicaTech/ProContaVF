using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Resultados;

namespace ProConta.Mantenimiento;

/// <summary>Cómo se obtiene la clave de un registro nuevo.</summary>
public enum ClaveNueva
{
    /// <summary>Autonumérica (COUNTER).</summary>
    Autonumerica,
    /// <summary>La siguiente libre (MAX + 1), para claves manuales que conservan los números del ERP.</summary>
    Siguiente,
    /// <summary>La escribe el operador al crear el registro (campo <see cref="CampoMantenimiento.SoloAlta"/>).</summary>
    Escrita
}

/// <summary>Columna que referencia la clave de la tabla: impide borrar un registro que se usa.</summary>
public sealed record UsoRegistro(string Tabla, string Columna, string Nombre);

/// <summary>
/// Proveedor de una tabla sencilla, sin invariantes entre registros: lista, alta, modificación y baja según uso. El
/// ámbito se aplica con la columna de sociedad o de ejercicio. Las tablas con reglas propias usan su servicio tipado.
/// </summary>
public class ProveedorTablaAccess : ProveedorBase
{
    private readonly IBaseDatos _bd;

    public ProveedorTablaAccess(IBaseDatos bd, string clave, string titulo, string categoria, string finalidad, AmbitoMantenimiento ambito,
        string nombreRegistro, string tabla, string columnaClave, ClaveNueva claveNueva, IReadOnlyList<CampoMantenimiento> campos,
        IReadOnlyList<ColumnaLista> columnas)
    {
        _bd = bd;
        Clave = clave;
        Titulo = titulo;
        Categoria = categoria;
        Finalidad = finalidad;
        Ambito = ambito;
        NombreRegistro = nombreRegistro;
        Tabla = tabla;
        ColumnaClave = columnaClave;
        TipoClaveNueva = claveNueva;
        Campos = campos;
        Columnas = columnas;
    }

    public override string Clave { get; }
    public override string Titulo { get; }
    public override string Categoria { get; }
    public override string Finalidad { get; }
    public override AmbitoMantenimiento Ambito { get; }
    public override string NombreRegistro { get; }
    public override IReadOnlyList<CampoMantenimiento> Campos { get; }
    public override IReadOnlyList<ColumnaLista> Columnas { get; }

    public string Tabla { get; }
    public string ColumnaClave { get; }
    public ClaveNueva TipoClaveNueva { get; }

    /// <summary>Columna que guarda la sociedad o el ejercicio (según el ámbito); null si es compartido.</summary>
    public string? ColumnaAmbito { get; init; }
    public string Orden { get; init; } = "";
    public IReadOnlyList<UsoRegistro> Usos { get; init; } = [];
    public string? SoloConsulta { get; init; }
    public override string? MotivoSoloConsulta => SoloConsulta;
    public bool Alta { get; init; } = true;
    public bool Baja { get; init; } = true;
    public override bool PermiteAlta => Alta && SoloConsulta is null && Cardinalidad == Cardinalidad.Lista;
    public override bool PermiteBaja => Baja && SoloConsulta is null && Cardinalidad == Cardinalidad.Lista;
    public Cardinalidad Unico { get; init; } = Cardinalidad.Lista;
    public override Cardinalidad Cardinalidad => Unico;
    public IReadOnlyList<string> Alias { get; init; } = [];
    public override IReadOnlyList<string> Sinonimos => Alias;
    public IReadOnlyList<GrupoCampos> GruposCampos { get; init; } = [];
    public override IReadOnlyList<GrupoCampos> Grupos => GruposCampos;
    public Func<RegistroMantenimiento, string>? Titular { get; init; }
    public Func<RegistroMantenimiento, string?>? Subtitular { get; init; }
    /// <summary>Reglas propias además de las genéricas (sin BD).</summary>
    public Func<RegistroMantenimiento, IEnumerable<ErrorCampo>>? Reglas { get; init; }
    /// <summary>Comprobaciones con BD antes de guardar (unicidad…); devuelve el error o null.</summary>
    public Func<ISesionDatos, RegistroMantenimiento, ContextoMantenimiento, Task<Error?>>? AntesDeGuardar { get; init; }

    public override string TituloRegistro(RegistroMantenimiento r) => Titular?.Invoke(r) ?? base.TituloRegistro(r);
    public override string? SubtituloRegistro(RegistroMantenimiento r) => Subtitular?.Invoke(r);

    public override IReadOnlyList<ErrorCampo> Validar(RegistroMantenimiento r) =>
        [.. base.Validar(r), .. Reglas?.Invoke(r) ?? []];

    private object? ValorAmbito(ContextoMantenimiento c) => Ambito switch
    {
        AmbitoMantenimiento.Sociedad => c.Trabajo.IdSociedad,
        AmbitoMantenimiento.Ejercicio => c.Trabajo.IdSociedadEjercicio,
        _ => null
    };

    private string Columnas_ => string.Join(", ", new[] { ColumnaClave }.Concat(Campos.Select(c => c.Clave)).Distinct(StringComparer.OrdinalIgnoreCase).Select(c => $"[{c}]"));

    public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto) =>
        _bd.EjecutarAsync(s =>
        {
            var sql = $"SELECT {Columnas_} FROM [{Tabla}]";
            var ambito = ValorAmbito(contexto);
            var orden = string.IsNullOrWhiteSpace(Orden) ? "" : $" ORDER BY {Orden}";
            return ColumnaAmbito is not null && ambito is not null
                ? s.ConsultarAsync($"{sql} WHERE [{ColumnaAmbito}] = ?{orden}", Mapear, ambito)
                : s.ConsultarAsync(sql + orden, Mapear);
        });

    protected virtual RegistroMantenimiento Mapear(IFilaDatos f)
    {
        var r = new RegistroMantenimiento(f.Valor(ColumnaClave));
        foreach (var c in Campos) r[c.Clave] = Normalizar(c, f.EsNulo(c.Clave) ? null : f.Valor(c.Clave));
        return r;
    }

    /// <summary>SINGLE y DOUBLE se redondean para que 21 no sea 20,99999; los textos se recortan.</summary>
    protected static object? Normalizar(CampoMantenimiento c, object? v) => v switch
    {
        float x => Math.Round((decimal)x, 4),
        double x => Math.Round((decimal)x, 4),
        string t => c.Tipo is TipoCampo.TextoLargo ? t : t.TrimEnd(),
        _ => v
    };

    /// <summary>Valor que se escribe en la BD según el tipo del campo.</summary>
    protected static object? Escribir(CampoMantenimiento c, RegistroMantenimiento r) => c.Tipo switch
    {
        TipoCampo.Logico => r.Logico(c.Clave),
        TipoCampo.Entero => r.Entero(c.Clave),
        TipoCampo.Importe => r.Decimal(c.Clave),
        TipoCampo.Decimal or TipoCampo.Porcentaje => r.Decimal(c.Clave) is decimal d ? (double)d : null,
        TipoCampo.Lista or TipoCampo.Referencia => r[c.Clave] switch { null => null, string s when s.Length == 0 => null, var o => o },
        TipoCampo.Fecha => r[c.Clave] as DateTime?,
        TipoCampo.TextoLargo => r[c.Clave] is string s && !string.IsNullOrWhiteSpace(s) ? s : null,
        _ => r.Texto(c.Clave)
    };

    public override async Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto)
    {
        try
        {
            var clave = await _bd.EnTransaccionAsync(async s =>
            {
                if (AntesDeGuardar is not null && await AntesDeGuardar(s, registro, contexto) is Error e) throw new ExcepcionDatos(e);
                var editables = Campos.Where(c => !c.SoloLectura && !c.Clave.Equals(ColumnaClave, StringComparison.OrdinalIgnoreCase)
                    && !c.Clave.Equals(ColumnaAmbito, StringComparison.OrdinalIgnoreCase) && !registro.Bloqueos.ContainsKey(c.Clave)).ToList();
                if (!registro.EsNuevo)
                {
                    var sets = editables.Where(c => !c.SoloAlta).ToList();
                    if (sets.Count > 0)
                    {
                        var filas = await s.EjecutarAsync($"UPDATE [{Tabla}] SET {string.Join(", ", sets.Select(c => $"[{c.Clave}] = ?"))} WHERE [{ColumnaClave}] = ?",
                            [.. sets.Select(c => Escribir(c, registro)), registro.Clave]);
                        if (filas == 0)
                            throw new ConflictoConcurrenciaException(new Error("Mantenimiento.NoExiste", $"El {NombreRegistro} ya no existe: otro operador lo ha borrado.",
                                TituloRegistro(registro), null, "Actualiza la lista."));
                    }
                    return registro.Clave!;
                }
                var columnas = new List<string>();
                var valores = new List<object?>();
                object? nueva = null;
                if (TipoClaveNueva == ClaveNueva.Siguiente)
                {
                    nueva = await s.SiguienteNumeroAsync(Tabla, ColumnaClave);
                    columnas.Add(ColumnaClave);
                    valores.Add(nueva);
                }
                else if (TipoClaveNueva == ClaveNueva.Escrita)
                {
                    var campoClave = Campos.First(c => c.Clave.Equals(ColumnaClave, StringComparison.OrdinalIgnoreCase));
                    nueva = Escribir(campoClave, registro);
                    if (Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM [{Tabla}] WHERE [{ColumnaClave}] = ?", nueva)) > 0)
                        throw new UnicidadException(new Error("Mantenimiento.ClaveRepetida", $"Ya hay un {NombreRegistro} con {campoClave.Etiqueta.ToLowerInvariant()} {nueva}.",
                            null, null, "Usa otro código."));
                    columnas.Add(ColumnaClave);
                    valores.Add(nueva);
                }
                if (ColumnaAmbito is not null && ValorAmbito(contexto) is { } ambito)
                {
                    columnas.Add(ColumnaAmbito);
                    valores.Add(ambito);
                }
                foreach (var c in editables)
                {
                    columnas.Add(c.Clave);
                    valores.Add(Escribir(c, registro));
                }
                await s.EjecutarAsync($"INSERT INTO [{Tabla}] ({string.Join(", ", columnas.Select(c => $"[{c}]"))}) VALUES ({string.Join(", ", columnas.Select(_ => "?"))})", [.. valores]);
                return nueva ?? await s.UltimoIdAsync();
            });
            var guardado = (await ListarAsync(contexto)).FirstOrDefault(r => MismaClave(r.Clave, clave));
            return guardado is null
                ? Resultado<RegistroMantenimiento>.Fallo(new Error("Mantenimiento.NoVisible", $"He guardado el {NombreRegistro}, pero no está en la lista de esta empresa o ejercicio."))
                : Resultado<RegistroMantenimiento>.Correcto(guardado);
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<RegistroMantenimiento>.Fallo(ex.Error);
        }
    }

    public override async Task<Resultado> EliminarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto)
    {
        if (!PermiteBaja) return await base.EliminarAsync(registro, contexto);
        var usos = await UsosAsync(registro.Clave);
        if (usos.Count > 0)
            return Resultado.Fallo(new Error("Mantenimiento.EnUso", $"No se puede borrar: el {NombreRegistro} se usa en {string.Join(", ", usos)}.",
                TituloRegistro(registro), "Borrarlo dejaría esos registros apuntando a nada.",
                Campos.Any(c => c.Clave == "Activo") ? "Desactívalo para que no se ofrezca en registros nuevos." : "Cambia antes esos registros."));
        try
        {
            await _bd.EjecutarAsync(s => s.EjecutarAsync($"DELETE FROM [{Tabla}] WHERE [{ColumnaClave}] = ?", registro.Clave));
            return Resultado.Correcto();
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado.Fallo(ex.Error);
        }
    }

    /// <summary>Dónde se usa la clave, en frases («12 facturas»).</summary>
    public Task<IReadOnlyList<string>> UsosAsync(object? clave) =>
        _bd.EjecutarAsync(async s =>
        {
            var r = new List<string>();
            foreach (var u in Usos)
            {
                var n = Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM [{u.Tabla}] WHERE [{u.Columna}] = ?", clave), CultureInfo.InvariantCulture);
                if (n > 0) r.Add($"{n.ToString("N0", CultureInfo.CurrentCulture)} {u.Nombre}");
            }
            return (IReadOnlyList<string>)r;
        });

    protected static bool MismaClave(object? a, object? b) =>
        a is not null && b is not null && string.Equals(Convert.ToString(a, CultureInfo.InvariantCulture)?.Trim(), Convert.ToString(b, CultureInfo.InvariantCulture)?.Trim(), StringComparison.OrdinalIgnoreCase);
}
