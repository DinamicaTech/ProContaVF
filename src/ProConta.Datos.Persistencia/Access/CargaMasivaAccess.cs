using System.Globalization;
using System.Text;

namespace ProConta.Datos.Persistencia.Access;

/// <summary>Tipo de una columna de un fichero de carga masiva.</summary>
public enum TipoCarga
{
    /// <summary>Número entero (Long de Access).</summary>
    Entero,
    /// <summary>Texto de hasta 255 caracteres. También importes y fechas, que se convierten en la consulta (<see cref="CargaMasivaAccess.Importe"/>, <see cref="CargaMasivaAccess.Fecha"/>).</summary>
    Texto,
    /// <summary>Texto largo (Memo).</summary>
    TextoLargo,
}

/// <summary>Columna de un fichero de carga masiva; la consulta la nombra como <c>t.Nombre</c>.</summary>
public sealed record ColumnaCarga(string Nombre, TipoCarga Tipo);

/// <summary>
/// Carga masiva en Access (datos/persistencia): las filas se escriben en ficheros de texto temporales y se insertan con
/// un INSERT … SELECT por tanda, leyendo el fichero con el controlador de texto de ACE. Mil filas en una sentencia cuestan
/// lo que unas pocas filas una a una. Los importes viajan como texto con punto decimal y las fechas como «yyyy-MM-dd HH:mm:ss»,
/// y se convierten en la consulta (sin depender de la configuración regional del equipo).
/// </summary>
public sealed class CargaMasivaAccess : IDisposable
{
    /// <summary>Filas por sentencia y transacción: por debajo del límite de bloqueos de las transacciones de Access.</summary>
    public const int FilasPorTanda = 1000;

    private readonly StringBuilder _esquema = new();
    private int _ficheros;

    public CargaMasivaAccess(string? carpetaBase = null)
    {
        Carpeta = Path.Combine(carpetaBase ?? Path.GetTempPath(), "ProConta-carga-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Carpeta);
    }

    /// <summary>Carpeta temporal de los ficheros (se borra al terminar).</summary>
    public string Carpeta { get; }

    /// <summary>
    /// Inserta las filas en tandas de <paramref name="filasPorTanda"/>, cada una en su transacción. <paramref name="insertar"/> es
    /// «INSERT INTO Tabla (Campos)» y <paramref name="seleccionar"/> la lista de expresiones del SELECT sobre las columnas <c>t.*</c>.
    /// Devuelve las filas insertadas.
    /// </summary>
    public async Task<int> InsertarAsync(IBaseDatos bd, string insertar, string seleccionar, IReadOnlyList<ColumnaCarga> columnas, IEnumerable<object?[]> filas,
        int filasPorTanda = FilasPorTanda, CancellationToken cancelacion = default)
    {
        var total = 0;
        foreach (var tanda in filas.Chunk(filasPorTanda))
        {
            cancelacion.ThrowIfCancellationRequested();
            var origen = Preparar(columnas, tanda);
            total += await bd.EnTransaccionAsync(s => s.EjecutarAsync($"{insertar} SELECT {seleccionar} FROM {origen} AS t"), cancelacion);
        }
        return total;
    }

    /// <summary>Escribe un fichero con las filas y su sección de schema.ini; devuelve el origen para la cláusula FROM.</summary>
    public string Preparar(IReadOnlyList<ColumnaCarga> columnas, IReadOnlyCollection<object?[]> filas)
    {
        var nombre = $"c{++_ficheros}.txt";
        var texto = new StringBuilder();
        foreach (var fila in filas)
        {
            if (fila.Length != columnas.Count) throw new ArgumentException($"La fila tiene {fila.Length} valores y el fichero {columnas.Count} columnas.", nameof(filas));
            for (var i = 0; i < fila.Length; i++)
            {
                if (i > 0) texto.Append('\t');
                texto.Append(Valor(fila[i]));
            }
            texto.Append("\r\n");
        }
        File.WriteAllText(Path.Combine(Carpeta, nombre), texto.ToString(), new UTF8Encoding(false));
        _esquema.Append('[').Append(nombre).Append("]\r\n")
            .Append("ColNameHeader=False\r\nFormat=TabDelimited\r\nTextDelimiter=none\r\nCharacterSet=65001\r\nMaxScanRows=0\r\n");
        for (var i = 0; i < columnas.Count; i++)
            _esquema.Append("Col").Append(i + 1).Append('=').Append(columnas[i].Nombre).Append(' ').Append(columnas[i].Tipo switch
            {
                TipoCarga.Entero => "Long",
                TipoCarga.TextoLargo => "LongChar",
                _ => "Text Width 255",
            }).Append("\r\n");
        File.WriteAllText(Path.Combine(Carpeta, "schema.ini"), _esquema.ToString(), Encoding.ASCII);
        return $"[Text;DATABASE={Carpeta};HDR=No].[{nombre.Replace('.', '#')}]";
    }

    /// <summary>Un valor en el fichero: vacío es NULL; sin tabuladores ni saltos de línea, que separan columnas y filas.</summary>
    public static string Valor(object? valor) => valor switch
    {
        null or DBNull => "",
        string t => Limpiar(t),
        decimal d => d.ToString("0.00######", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        DateTime f => f.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        Enum e => Convert.ToInt64(e, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => Limpiar(valor.ToString() ?? ""),
    };

    private static string Limpiar(string texto)
    {
        if (texto.AsSpan().IndexOfAny('\t', '\r', '\n') < 0) return texto;
        return texto.Replace("\r\n", " ").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
    }

    /// <summary>Expresión que convierte una columna de texto con un importe («1234.56») en moneda; vacía, NULL.</summary>
    public static string Importe(string columna) => $"IIF({columna} IS NULL, NULL, CCur(Val({columna} & '')))";

    /// <summary>Expresión que convierte una columna de texto «yyyy-MM-dd HH:mm:ss» en fecha; vacía, NULL.</summary>
    public static string Fecha(string columna) =>
        $"IIF({columna} IS NULL, NULL, DateSerial(Val(Left({columna} & '', 4)), Val(Mid({columna} & '', 6, 2)), Val(Mid({columna} & '', 9, 2))) + " +
        $"TimeSerial(Val(Mid({columna} & '', 12, 2)), Val(Mid({columna} & '', 15, 2)), Val(Mid({columna} & '', 18, 2))))";

    /// <summary>Literal de texto de Access ('' para la comilla); null es NULL.</summary>
    public static string Literal(string? texto) => texto is null ? "NULL" : "'" + texto.Replace("'", "''") + "'";

    /// <summary>Literal de fecha de Access, independiente de la configuración regional.</summary>
    public static string Literal(DateTime fecha) => fecha.ToString("'#'MM'/'dd'/'yyyy HH':'mm':'ss'#'", CultureInfo.InvariantCulture);

    /// <summary>Literal entero.</summary>
    public static string Literal(int numero) => numero.ToString(CultureInfo.InvariantCulture);

    public void Dispose()
    {
        try { Directory.Delete(Carpeta, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
