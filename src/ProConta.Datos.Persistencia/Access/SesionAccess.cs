using System.Data;
using System.Data.Common;
using System.Data.OleDb;
using ProConta.Nucleo.Resultados;

namespace ProConta.Datos.Persistencia.Access;

internal sealed class SesionAccess(OleDbConnection conexion, OleDbTransaction? transaccion, CancellationToken cancelacion) : ISesionDatos
{
    private static readonly System.Text.RegularExpressions.Regex _identificador = new("^[A-Za-z_][A-Za-z0-9_ñÑ]*$");

    public bool EnTransaccion => transaccion is not null;

    public async Task<IReadOnlyList<T>> ConsultarAsync<T>(string sql, Func<IFilaDatos, T> mapear, params object?[] parametros)
    {
        using var comando = Crear(sql, parametros);
        var filas = new List<T>();
        try
        {
            using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            var fila = new FilaAccess(lector);
            while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
                filas.Add(mapear(fila));
        }
        catch (OleDbException ex)
        {
            throw TraductorErroresAccess.Traducir(ex, conexion.DataSource, sql);
        }
        return filas;
    }

    public async Task<T?> ConsultarPrimeroAsync<T>(string sql, Func<IFilaDatos, T> mapear, params object?[] parametros)
    {
        var filas = await ConsultarAsync(sql, mapear, parametros).ConfigureAwait(false);
        return filas.Count > 0 ? filas[0] : default;
    }

    public async Task<object?> EscalarAsync(string sql, params object?[] parametros)
    {
        using var comando = Crear(sql, parametros);
        try
        {
            var valor = await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false);
            return valor is DBNull ? null : valor;
        }
        catch (OleDbException ex)
        {
            throw TraductorErroresAccess.Traducir(ex, conexion.DataSource, sql);
        }
    }

    public async Task<int> EjecutarAsync(string sql, params object?[] parametros)
    {
        using var comando = Crear(sql, parametros);
        try
        {
            return await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }
        catch (OleDbException ex)
        {
            throw TraductorErroresAccess.Traducir(ex, conexion.DataSource, sql);
        }
    }

    public async Task ActualizarConVersionAsync(string sql, string registroAfectado, params object?[] parametros)
    {
        var filas = await EjecutarAsync(sql, parametros).ConfigureAwait(false);
        if (filas == 0)
            throw new ConflictoConcurrenciaException(new Error(
                "Datos.ConflictoVersion",
                "No he guardado tus cambios porque otra persona ha modificado o borrado este registro mientras lo editabas.",
                registroAfectado,
                "La versión del registro ya no es la que leíste al abrirlo.",
                "Vuelve a abrir el registro para ver los cambios del otro operador y repite los tuyos."));
    }

    public async Task<int> UltimoIdAsync() =>
        Convert.ToInt32(await EscalarAsync("SELECT @@IDENTITY").ConfigureAwait(false));

    public async Task<int> SiguienteNumeroAsync(string tabla, string campo, string? filtro = null, params object?[] parametros)
    {
        if (!EnTransaccion)
            throw new InvalidOperationException("La numeración solo se calcula dentro de una transacción.");
        ComprobarIdentificador(tabla);
        ComprobarIdentificador(campo);
        var sql = $"SELECT MAX([{campo}]) FROM [{tabla}]" + (string.IsNullOrWhiteSpace(filtro) ? "" : " WHERE " + filtro);
        var maximo = await EscalarAsync(sql, parametros).ConfigureAwait(false);
        return maximo is null ? 1 : Convert.ToInt32(maximo) + 1;
    }

    public EsquemaFisico LeerEsquema() => LectorEsquemaAccess.Leer(conexion, transaccion);

    private OleDbCommand Crear(string sql, object?[] parametros)
    {
        cancelacion.ThrowIfCancellationRequested();
        var comando = new OleDbCommand(sql, conexion, transaccion);
        foreach (var valor in parametros)
            comando.Parameters.Add(Parametro(valor));
        return comando;
    }

    /// <summary>Parámetro posicional con el tipo OLE DB adecuado para Access.</summary>
    internal static OleDbParameter Parametro(object? valor) => valor switch
    {
        null or DBNull => new OleDbParameter { Value = DBNull.Value, OleDbType = OleDbType.VarWChar },
        string s => new OleDbParameter { Value = s, OleDbType = s.Length > 255 ? OleDbType.LongVarWChar : OleDbType.VarWChar },
        decimal d => new OleDbParameter { Value = d, OleDbType = OleDbType.Currency },
        DateTime f => new OleDbParameter { Value = new DateTime(f.Year, f.Month, f.Day, f.Hour, f.Minute, f.Second), OleDbType = OleDbType.Date },
        bool b => new OleDbParameter { Value = b, OleDbType = OleDbType.Boolean },
        int i => new OleDbParameter { Value = i, OleDbType = OleDbType.Integer },
        short i => new OleDbParameter { Value = i, OleDbType = OleDbType.SmallInt },
        byte i => new OleDbParameter { Value = i, OleDbType = OleDbType.UnsignedTinyInt },
        long i => new OleDbParameter { Value = i, OleDbType = OleDbType.BigInt },
        double x => new OleDbParameter { Value = x, OleDbType = OleDbType.Double },
        float x => new OleDbParameter { Value = x, OleDbType = OleDbType.Single },
        Guid g => new OleDbParameter { Value = g, OleDbType = OleDbType.Guid },
        byte[] bytes => new OleDbParameter { Value = bytes, OleDbType = OleDbType.LongVarBinary },
        Enum e => new OleDbParameter { Value = Convert.ToInt32(e), OleDbType = OleDbType.Integer },
        Nucleo.Importes.Importe imp => new OleDbParameter { Value = imp.Valor, OleDbType = OleDbType.Currency },
        _ => throw new ArgumentException($"Tipo de parámetro no admitido: {valor.GetType().Name}.")
    };

    internal static void ComprobarIdentificador(string nombre)
    {
        if (!_identificador.IsMatch(nombre))
            throw new ArgumentException($"Identificador SQL no válido: «{nombre}».");
    }

    private sealed class FilaAccess(DbDataReader lector) : IFilaDatos
    {
        private int Ordinal(string campo) => lector.GetOrdinal(campo);

        public object? Valor(string campo)
        {
            var v = lector.GetValue(Ordinal(campo));
            return v is DBNull ? null : v;
        }

        public bool EsNulo(string campo) => lector.IsDBNull(Ordinal(campo));
        public string? Texto(string campo) => Valor(campo)?.ToString();
        public int Entero(string campo) => Convert.ToInt32(Valor(campo) ?? throw Nulo(campo));
        public int? EnteroNulo(string campo) => Valor(campo) is { } v ? Convert.ToInt32(v) : null;
        public long EnteroLargo(string campo) => Convert.ToInt64(Valor(campo) ?? throw Nulo(campo));
        public decimal Decimal(string campo) => Convert.ToDecimal(Valor(campo) ?? throw Nulo(campo));
        public decimal? DecimalNulo(string campo) => Valor(campo) is { } v ? Convert.ToDecimal(v) : null;
        public double Doble(string campo) => Convert.ToDouble(Valor(campo) ?? throw Nulo(campo));
        public DateTime Fecha(string campo) => Convert.ToDateTime(Valor(campo) ?? throw Nulo(campo));
        public DateTime? FechaNula(string campo) => Valor(campo) is { } v ? Convert.ToDateTime(v) : null;
        public bool Logico(string campo) => Valor(campo) is { } v && Convert.ToBoolean(v);
        public byte[]? Binario(string campo) => Valor(campo) as byte[];

        private static InvalidOperationException Nulo(string campo) => new($"El campo {campo} es nulo y se esperaba un valor.");
    }
}
