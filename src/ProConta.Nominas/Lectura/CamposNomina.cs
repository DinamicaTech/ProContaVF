using System.Globalization;
using System.Text;

namespace ProConta.Nominas.Lectura;

/// <summary>Campo de la nómina al que se traduce una columna del Excel (los mismos nombres que F_NOM de ProConta antiguo).</summary>
/// <param name="Nombre">Nombre con que lo guardan la tabla Nominas y lo usan las fórmulas de la plantilla «Nómina».</param>
/// <param name="EsImporte">Importe en euros (decimal); si no, dato identificativo.</param>
public sealed record CampoNomina(string Nombre, string Descripcion, bool EsImporte = true);

/// <summary>
/// Catálogo de campos de la nómina y alias por defecto de las cabeceras habituales de las gestorías (los de AliasCamposF_NOM y
/// F_NOM_ALIAS de ProConta antiguo, que son genéricos). Los alias de cada sociedad (tabla AliasCamposNomina) ganan a estos.
/// </summary>
public static class CamposNomina
{
    public const string Trabajador = "Trabajador", CodigoTrabajador = "IdTrabajador", Dni = "DNI", TipoPaga = "TipoPaga", FechaCobro = "Fecha";

    public static readonly IReadOnlyList<CampoNomina> Identificativos =
    [
        new(Trabajador, "Nombre del trabajador tal como lo escribe la gestoría.", false),
        new(CodigoTrabajador, "Código del trabajador en la gestoría.", false),
        new(Dni, "NIF del trabajador.", false),
        new(TipoPaga, "Tipo de paga de la fila (MENSUAL o EXTRA).", false),
        new(FechaCobro, "Fecha de cobro de la nómina.", false),
    ];

    public static readonly IReadOnlyList<CampoNomina> Importes =
    [
        new("Bruto", "Total devengado (bruto)."),
        new("DtoReEsp", "Descuento por retribución en especie (llega en negativo)."),
        new("InRetEsp", "Ingreso a cuenta por retribución en especie."),
        new("SSEmp", "Seguridad Social a cargo de la empresa."),
        new("TC1", "Total de la liquidación a la Seguridad Social (empresa + cuotas obreras)."),
        new("Autonomo", "Cuota de autónomo pagada como retribución en especie."),
        new("IRPF111", "Retención de IRPF (modelo 111)."),
        new("Neto", "Líquido a percibir."),
        new("AnticOtro", "Anticipos y otras deducciones."),
        new("RetEmbarg", "Retenciones por embargo (llegan en negativo)."),
        new("FaltaPrea", "Falta de preaviso."),
        new("CostEmp", "Coste para la empresa."),
        new("CostPPExt", "Coste con la prorrata de pagas extra."),
        new("DtoVac", "Descuento por vacaciones."),
        new("DifExtra", "Diferencia de paga extra."),
        new("BFormCon", "Bonificación por formación continua."),
        new("HEFestiv", "Horas extra festivas."),
        new("RegBases", "Regularización de bases."),
        new("EnfermedadAccidente", "Enfermedad o accidente (incapacidad temporal)."),
        new("CompensBonif", "Compensación de bonificaciones."),
        new("BonifFundae", "Bonificación FUNDAE (formación)."),
        new("TotalCosteSS", "Total coste de Seguridad Social."),
        new("TotalFLC_FPHIB", "Total FLC / FPHIB."),
        new("ProrrataPagasExtra", "Prorrata de pagas extra."),
        new("CuotasObreras", "Cuotas obreras (Seguridad Social a cargo del trabajador)."),
    ];

    public static IEnumerable<CampoNomina> Todos => Identificativos.Concat(Importes);

    private static readonly Dictionary<string, CampoNomina> PorNombre = Todos.ToDictionary(c => c.Nombre, StringComparer.OrdinalIgnoreCase);

    /// <summary>El campo con ese nombre (sin distinguir mayúsculas; «TotalFLC-FPHIB» de F_NOM vale por TotalFLC_FPHIB), o null.</summary>
    public static CampoNomina? Buscar(string? nombre) =>
        nombre is null ? null : PorNombre.GetValueOrDefault(nombre.Trim().Replace('-', '_'));

    /// <summary>Alias por defecto: cabecera normalizada → campo.</summary>
    public static readonly IReadOnlyDictionary<string, string> AliasPorDefecto = new Dictionary<string, string>
    {
        ["TRABAJADOR"] = Trabajador, ["NOMBRE TRABAJADOR"] = Trabajador,
        ["N.I.F."] = Dni, ["NIF"] = Dni, ["DNI"] = Dni,
        ["TIPO PAGA"] = TipoPaga, ["FECHA COBRO"] = FechaCobro,
        ["COD. TRABAJADOR"] = CodigoTrabajador, ["CODIGO TRABAJADOR"] = CodigoTrabajador,
        ["BRUTO"] = "Bruto", ["TOTAL DEVENGADO"] = "Bruto",
        ["DTO.RE.ESP"] = "DtoReEsp", ["ANTICIPOS Y DEDUCCION"] = "AnticOtro",
        ["IN.RET.ESP"] = "InRetEsp",
        ["S.S.EMP."] = "SSEmp", ["COSTE S.S. EMPRESA"] = "SSEmp",
        ["TC1"] = "TC1", ["CUADRE TC1 APROX."] = "TC1",
        ["AUTONOMO"] = "Autonomo",
        ["IRPF 111"] = "IRPF111", ["RETENCION I.R.P.F."] = "IRPF111",
        ["NETO"] = "Neto", ["LIQUIDO A PERCIBIR"] = "Neto",
        ["ANTIC/OTRO"] = "AnticOtro",
        ["RET EMBARG"] = "RetEmbarg",
        ["FALTA PREA"] = "FaltaPrea",
        ["COST EMP"] = "CostEmp", ["COSTE TOTAL"] = "CostEmp",
        ["COS+PP EXT"] = "CostPPExt",
        ["DTO VAC"] = "DtoVac",
        ["DIF.EXTRA"] = "DifExtra",
        ["B.FORM.CON"] = "BFormCon",
        ["H.E.FESTIV"] = "HEFestiv",
        ["REG.BASES"] = "RegBases",
        ["ENFERMEDAD/ ACCIDENTE"] = "EnfermedadAccidente",
        ["COMPENS. BONIF."] = "CompensBonif",
        ["BONIF. FUNDAE"] = "BonifFundae",
        ["TOTAL COSTE S.S."] = "TotalCosteSS",
        ["TOTAL FLC/ FPHIB"] = "TotalFLC_FPHIB",
        ["PRORRATA PAGAS EXT."] = "ProrrataPagasExtra",
        ["CUOTAS OBRERAS"] = "CuotasObreras",
    };

    /// <summary>Cabecera normalizada para buscar su alias: sin acentos, en mayúsculas y con los espacios juntos.</summary>
    public static string NormalizarCabecera(string? cabecera)
    {
        var d = (cabecera ?? "").Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        var espacio = false;
        foreach (var ch in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsWhiteSpace(ch)) { espacio = sb.Length > 0; continue; }
            if (espacio) { sb.Append(' '); espacio = false; }
            sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }
}
