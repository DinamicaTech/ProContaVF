using System.Text;
using ExcelDataReader;

namespace ProConta.Nominas.Lectura;

/// <summary>
/// Lee la primera hoja de un Excel (.xls de Excel 97-2003, como los de la gestoría, o .xlsx) con ExcelDataReader y la pasa
/// a <see cref="InterpreteNominas"/>. Las celdas llegan como double (números), DateTime (fechas) o string.
/// </summary>
public static class LectorExcelNominas
{
    static LectorExcelNominas() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); // el .xls antiguo usa la página de códigos 1252

    /// <summary>Celdas de la primera hoja; un error con su motivo si el fichero no es un Excel legible.</summary>
    public static (IReadOnlyList<IReadOnlyList<object?>> Filas, string? Error) LeerCeldas(byte[] contenido)
    {
        try
        {
            using var flujo = new MemoryStream(contenido, writable: false);
            using var lector = ExcelReaderFactory.CreateReader(flujo);
            var filas = new List<IReadOnlyList<object?>>();
            while (lector.Read())
            {
                var celdas = new object?[lector.FieldCount];
                for (var i = 0; i < celdas.Length; i++) celdas[i] = lector.GetValue(i);
                filas.Add(celdas);
            }
            return (filas, null);
        }
        catch (Exception ex) when (ex is ExcelDataReader.Exceptions.ExcelReaderException or InvalidDataException or IOException or NotSupportedException
                                   or ArgumentException or IndexOutOfRangeException)
        {
            return ([], $"No puedo leer el fichero como Excel ({ex.Message}). Guárdalo desde Excel como .xls o .xlsx y vuelve a importarlo.");
        }
    }

    /// <summary>Lee e interpreta el Excel con los alias de la sociedad.</summary>
    public static FicheroNominas Leer(byte[] contenido, IReadOnlyDictionary<string, string?>? aliasSociedad = null)
    {
        var (filas, error) = LeerCeldas(contenido);
        return error is null
            ? InterpreteNominas.Interpretar(filas, aliasSociedad)
            : new FicheroNominas(null, null, null, null, TipoPaga.Mensual, null, null, null, [], new Dictionary<int, string>(), [], [new IncidenciaNominas(error)], [], false);
    }
}
