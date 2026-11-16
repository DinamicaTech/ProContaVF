using ClosedXML.Excel;

namespace ProConta.Nominas.Tests;

/// <summary>
/// Resumen de nóminas sintético con la forma del de la gestoría (nominas 2026-11-13): metadatos arriba, cabeceras en la fila 8,
/// el código del trabajador en la columna B sin cabecera, una fila por trabajador y «TOTAL EMPRESA». Datos inventados.
/// </summary>
internal sealed class ResumenSintetico
{
    public static readonly string[] Cabeceras21 =
        ["TRABAJADOR", "BRUTO", "DTO.RE.ESP", "IN.RET.ESP", "S.S.EMP.", "TC1", "AUTONOMO", "IRPF 111", "NETO", "ANTIC/OTRO", "RET EMBARG", "FALTA PREA",
         "COST EMP", "COS+PP EXT", "DTO VAC", "DIF.EXTRA", "B.FORM.CON", "H.E.FESTIV", "REG.BASES"];

    public string Paga { get; set; } = "PAGA MENSUAL + FINIQUITO";
    public string Periodo { get; set; } = "DEL 01/10/2026 AL 31/10/2026";
    public string Empresa { get; set; } = "Empresa: 9999-SOCIEDAD SINTETICA SL NIF: B12345674";
    public List<string> Cabeceras { get; } = [.. Cabeceras21];
    public List<(string Codigo, string Nombre, Dictionary<string, object?> Valores)> Trabajadores { get; } = [];
    public bool ConTotal { get; set; } = true;
    public Dictionary<string, decimal> AjusteTotal { get; } = [];

    /// <summary>Trabajador con bruto, IRPF y Seguridad Social; el neto sale cuadrado salvo que se indique.</summary>
    public ResumenSintetico Trabajador(string codigo, string nombre, decimal bruto, decimal ssEmp = 0, decimal cuotas = 0, decimal irpf = 0,
        decimal dtoEspecie = 0, decimal autonomo = 0, decimal? neto = null, Dictionary<string, object?>? extra = null)
    {
        var v = new Dictionary<string, object?>
        {
            ["BRUTO"] = (double)bruto, ["S.S.EMP."] = ssEmp == 0 ? null : (double)ssEmp, ["TC1"] = ssEmp + cuotas == 0 ? null : (double)(ssEmp + cuotas),
            ["IRPF 111"] = irpf == 0 ? null : (double)irpf, ["DTO.RE.ESP"] = dtoEspecie == 0 ? null : (double)dtoEspecie,
            ["AUTONOMO"] = autonomo == 0 ? null : (double)autonomo,
            ["NETO"] = (double)(neto ?? bruto + dtoEspecie - autonomo - irpf - cuotas),
            ["COST EMP"] = (double)(bruto + ssEmp),
        };
        foreach (var (k, x) in extra ?? []) v[k] = x;
        Trabajadores.Add((codigo, nombre, v));
        return this;
    }

    /// <summary>Celdas como las devuelve ExcelDataReader: double, string o null.</summary>
    public IReadOnlyList<IReadOnlyList<object?>> Celdas()
    {
        var ancho = Cabeceras.Count + 2;
        object?[] Fila() => new object?[ancho];
        var filas = new List<IReadOnlyList<object?>>();
        filas.Add(Fila());
        foreach (var t in new[] { "Moneda: Euro", Paga, Periodo, Empresa })
        {
            var f = Fila(); f[0] = t; filas.Add(f);
        }
        filas.Add(Fila()); filas.Add(Fila());
        var cab = Fila();
        for (var i = 0; i < Cabeceras.Count; i++) cab[i + 2] = Cabeceras[i];
        filas.Add(cab);
        filas.Add(Fila());
        foreach (var (codigo, nombre, valores) in Trabajadores)
        {
            var f = Fila();
            f[1] = codigo;
            for (var i = 0; i < Cabeceras.Count; i++)
                f[i + 2] = Cabeceras[i] == "TRABAJADOR" ? nombre : valores.GetValueOrDefault(Cabeceras[i]);
            filas.Add(f);
        }
        filas.Add(Fila());
        if (ConTotal)
        {
            var f = Fila();
            f[0] = "TOTAL EMPRESA";
            for (var i = 0; i < Cabeceras.Count; i++)
            {
                if (Cabeceras[i] is "TRABAJADOR" or "N.I.F." or "TIPO PAGA" or "FECHA COBRO") continue;
                var suma = Trabajadores.Sum(t => t.Valores.GetValueOrDefault(Cabeceras[i]) is double d ? (decimal)d : 0m) + AjusteTotal.GetValueOrDefault(Cabeceras[i]);
                f[i + 2] = (double)suma;
            }
            filas.Add(f);
            var g = Fila(); g[0] = "TOTAL TRABAJADOR"; filas.Add(g);
        }
        return filas;
    }

    /// <summary>El mismo resumen como .xlsx.</summary>
    public byte[] Xlsx()
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Resumen");
        var celdas = Celdas();
        for (var r = 0; r < celdas.Count; r++)
            for (var c = 0; c < celdas[r].Count; c++)
                switch (celdas[r][c])
                {
                    case string s: hoja.Cell(r + 1, c + 1).Value = s; break;
                    case double d: hoja.Cell(r + 1, c + 1).Value = d; break;
                    case DateTime f: hoja.Cell(r + 1, c + 1).Value = f; break;
                }
        using var m = new MemoryStream();
        libro.SaveAs(m);
        return m.ToArray();
    }

    /// <summary>Tres trabajadores inventados de octubre de 2026: dos con retribución en especie y una con Seguridad Social.</summary>
    public static ResumenSintetico Octubre() => new ResumenSintetico()
        .Trabajador("000901", "FICTICIO UNO, ANA", 2300m, dtoEspecie: -40m, autonomo: 1300m)
        .Trabajador("000902", "FICTICIO DOS, LUIS", 2400m, dtoEspecie: -120m, autonomo: 1300m, irpf: 780m)
        .Trabajador("000903", "INVENTADA TRES, EVA", 1450m, ssEmp: 460m, cuotas: 92m, irpf: 87m);
}
