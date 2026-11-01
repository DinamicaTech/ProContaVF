using System.Data;
using System.Data.OleDb;

namespace ProConta.Datos.Persistencia.Access;

/// <summary>Lee la estructura física de un accdb mediante los conjuntos de esquema de OLE DB.</summary>
internal static class LectorEsquemaAccess
{
    public static EsquemaFisico Leer(OleDbConnection conexion, OleDbTransaction? transaccion)
    {
        var tablas = conexion.GetOleDbSchemaTable(OleDbSchemaGuid.Tables, null)!;
        var columnas = conexion.GetOleDbSchemaTable(OleDbSchemaGuid.Columns, null)!;
        var indices = conexion.GetOleDbSchemaTable(OleDbSchemaGuid.Indexes, null)!;
        var ajenas = conexion.GetOleDbSchemaTable(OleDbSchemaGuid.Foreign_Keys, null)!;

        var resultado = new List<TablaFisica>();
        foreach (DataRow t in tablas.Rows)
        {
            var tipo = t["TABLE_TYPE"]?.ToString();
            if (tipo is not ("TABLE" or "LINK")) continue;
            var nombre = t["TABLE_NAME"].ToString()!;
            if (nombre.StartsWith("MSys", StringComparison.OrdinalIgnoreCase)) continue; // tablas internas de Access
            var vinculada = tipo == "LINK";

            var autonumericos = vinculada ? new HashSet<string>() : Autonumericos(conexion, transaccion, nombre);

            var cols = columnas.Rows.Cast<DataRow>()
                .Where(c => c["TABLE_NAME"].ToString() == nombre)
                .Select(c => new ColumnaFisica(
                    c["COLUMN_NAME"].ToString()!,
                    Convert.ToInt32(c["ORDINAL_POSITION"]),
                    autonumericos.Contains(c["COLUMN_NAME"].ToString()!) ? "COUNTER" : TipoAccess(Convert.ToInt32(c["DATA_TYPE"]), c["CHARACTER_MAXIMUM_LENGTH"], c["COLUMN_FLAGS"]),
                    Longitud(c["CHARACTER_MAXIMUM_LENGTH"]),
                    Convert.ToBoolean(c["IS_NULLABLE"]),
                    c["COLUMN_DEFAULT"] is DBNull ? null : c["COLUMN_DEFAULT"]?.ToString(),
                    Texto(c, "DESCRIPTION")))
                .OrderBy(c => c.Posicion)
                .ToList();

            var idx = indices.Rows.Cast<DataRow>()
                .Where(i => i["TABLE_NAME"].ToString() == nombre)
                .GroupBy(i => i["INDEX_NAME"].ToString()!)
                .Select(g => new IndiceFisico(
                    g.Key,
                    g.Any(i => Convert.ToBoolean(i["PRIMARY_KEY"])),
                    g.Any(i => Convert.ToBoolean(i["UNIQUE"])),
                    g.OrderBy(i => Convert.ToInt32(i["ORDINAL_POSITION"])).Select(i => i["COLUMN_NAME"].ToString()!).ToList()))
                .OrderBy(i => i.Nombre, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rel = ajenas.Rows.Cast<DataRow>()
                .Where(f => f["FK_TABLE_NAME"].ToString() == nombre)
                .GroupBy(f => f["FK_NAME"].ToString()!)
                .Select(g =>
                {
                    var orden = g.OrderBy(f => Convert.ToInt32(f["ORDINAL"])).ToList();
                    return new RelacionFisica(
                        g.Key,
                        orden.Select(f => f["FK_COLUMN_NAME"].ToString()!).ToList(),
                        orden[0]["PK_TABLE_NAME"].ToString()!,
                        orden.Select(f => f["PK_COLUMN_NAME"].ToString()!).ToList(),
                        orden[0]["DELETE_RULE"]?.ToString() ?? "",
                        orden[0]["UPDATE_RULE"]?.ToString() ?? "");
                })
                .OrderBy(r => r.Nombre, StringComparer.OrdinalIgnoreCase)
                .ToList();

            resultado.Add(new TablaFisica(nombre, vinculada, Texto(t, "DESCRIPTION"), cols, idx, rel));
        }

        return new EsquemaFisico(resultado.OrderBy(t => t.Nombre, StringComparer.OrdinalIgnoreCase).ToList());
    }

    private static HashSet<string> Autonumericos(OleDbConnection conexion, OleDbTransaction? transaccion, string tabla)
    {
        var nombres = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var comando = new OleDbCommand($"SELECT * FROM [{tabla}] WHERE 1=0", conexion, transaccion);
            using var lector = comando.ExecuteReader(CommandBehavior.SchemaOnly | CommandBehavior.KeyInfo);
            var esquema = lector.GetSchemaTable();
            if (esquema is null) return nombres;
            foreach (DataRow c in esquema.Rows)
                if (c["IsAutoIncrement"] is true) nombres.Add(c["ColumnName"].ToString()!);
        }
        catch (OleDbException)
        {
            // Sin acceso a la tabla: se informa el tipo base en lugar de COUNTER.
        }
        return nombres;
    }

    private static int? Longitud(object? valor) =>
        valor is DBNull or null ? null : Convert.ToInt64(valor) is var l && l > 0 && l < int.MaxValue ? (int)l : null;

    private static string? Texto(DataRow fila, string columna) =>
        fila.Table.Columns.Contains(columna) && fila[columna] is string s && s.Length > 0 ? s : null;

    /// <summary>Nombre del tipo en DDL de Access para el tipo OLE DB.</summary>
    internal static string TipoAccess(int tipoOleDb, object? longitud, object? banderas)
    {
        var len = longitud is DBNull or null ? 0 : Convert.ToInt64(longitud);
        var esLargo = banderas is not (DBNull or null) && (Convert.ToInt64(banderas) & 0x80) != 0; // DBCOLUMNFLAGS_ISLONG
        return (OleDbType)tipoOleDb switch
        {
            OleDbType.WChar or OleDbType.VarWChar or OleDbType.LongVarWChar => esLargo || len == 0 ? "LONGTEXT" : $"TEXT({len})",
            OleDbType.Integer => "LONG",
            OleDbType.SmallInt => "SHORT",
            OleDbType.UnsignedTinyInt => "BYTE",
            OleDbType.Boolean => "YESNO",
            OleDbType.Currency => "CURRENCY",
            OleDbType.Date => "DATETIME",
            OleDbType.Double => "DOUBLE",
            OleDbType.Single => "SINGLE",
            OleDbType.Numeric or OleDbType.Decimal => "DECIMAL",
            OleDbType.Guid => "GUID",
            OleDbType.BigInt => "BIGINT",
            OleDbType.Binary or OleDbType.VarBinary => esLargo ? "LONGBINARY" : $"BINARY({len})",
            OleDbType.LongVarBinary => "LONGBINARY",
            _ => $"OLEDB{tipoOleDb}"
        };
    }
}
