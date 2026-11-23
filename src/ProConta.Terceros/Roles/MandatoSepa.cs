using System.Globalization;
using System.Net;
using System.Numerics;
using System.Text;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Identificacion;

namespace ProConta.Terceros.Roles;

/// <summary>Domicilio de una de las partes del mandato, tal como sale impreso.</summary>
public sealed record DomicilioSepa(string Direccion, string CodigoPostal, string Poblacion, string Provincia, string Pais);

/// <summary>Acreedor del mandato: la sociedad activa (tabla Sociedades).</summary>
public sealed record AcreedorSepa(string Nombre, string Nif, DomicilioSepa Domicilio)
{
    /// <summary>Identificador SEPA del acreedor (AT-02) con el sufijo 000: ES + dígitos de control + 000 + NIF.</summary>
    public string Identificador => MandatoSepa.IdentificadorAcreedor(Nif);
}

/// <summary>
/// Datos de la orden de domiciliación SEPA Core que firma el cliente (terceros/roles/mandato-sepa 2026-11-23): referencia = código de
/// cliente, pago periódico, sin SWIFT; la fecha es la de emisión y la localidad, la de la sociedad.
/// </summary>
public sealed record DatosMandatoSepa(string Referencia, string Deudor, DomicilioSepa DomicilioDeudor, string Iban, AcreedorSepa Acreedor, DateTime Fecha)
{
    public string Localidad => Acreedor.Domicilio.Poblacion;
}

/// <summary>Orden de domiciliación de adeudo directo SEPA Core en HTML de una página (A4), para convertir a PDF.</summary>
public static class MandatoSepa
{
    /// <summary>Página del PDF en milímetros: A4 vertical con márgenes de 18 mm.</summary>
    public static (double Ancho, double Alto, double Margen) Pagina => (210, 297, 18);

    /// <summary>
    /// Identificador del acreedor (AT-02 del Rulebook SEPA): país, dos dígitos de control ISO 7064 MOD 97-10 calculados sobre el NIF y
    /// el país (sin el sufijo), sufijo comercial y NIF. Un NIF con prefijo «ES» o con espacios y guiones se limpia antes.
    /// </summary>
    public static string IdentificadorAcreedor(string nif, string sufijo = "000", string pais = "ES")
    {
        var limpio = new string(nif.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (limpio.Length > 9 && limpio.StartsWith(pais, StringComparison.Ordinal)) limpio = limpio[pais.Length..];
        var numerico = new StringBuilder();
        foreach (var c in limpio + pais + "00") numerico.Append(char.IsDigit(c) ? c.ToString() : (c - 'A' + 10).ToString(CultureInfo.InvariantCulture));
        var control = 98 - (int)(BigInteger.Parse(numerico.ToString(), CultureInfo.InvariantCulture) % 97);
        return $"{pais}{control:00}{sufijo}{limpio}";
    }

    public static string NombreFichero(DatosMandatoSepa d)
    {
        var nombre = new string(d.Deudor.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? ' ' : c).ToArray()).Trim();
        return $"Mandato SEPA {d.Referencia} {nombre} {d.Fecha:yyyy-MM-dd}.pdf";
    }

    public static string Html(DatosMandatoSepa d)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
        static string Fila(string etiqueta, string valor, string clase = "") =>
            $"<tr><th>{H(etiqueta)}</th><td class=\"{clase}\">{H(valor)}</td></tr>";
        string Bloque(string titulo, string nombre, DomicilioSepa dom, params (string, string, string)[] extra)
        {
            var sb = new StringBuilder($"<h2>{H(titulo)}</h2><table>");
            sb.Append(Fila("Nombre", nombre));
            foreach (var (e, v, c) in extra) sb.Append(Fila(e, v, c));
            sb.Append(Fila("Dirección", dom.Direccion)).Append(Fila("Código postal", dom.CodigoPostal))
              .Append(Fila("Población", dom.Poblacion + (dom.Provincia.Length > 0 && dom.Provincia != dom.Poblacion ? $" ({dom.Provincia})" : "")))
              .Append(Fila("País", dom.Pais));
            return sb.Append("</table>").ToString();
        }

        var a = d.Acreedor;
        var sbHtml = new StringBuilder();
        sbHtml.Append("""
            <!DOCTYPE html><html lang="es"><head><meta charset="utf-8"><title>Orden de domiciliación SEPA</title><style>
            body{font-family:"Segoe UI",Arial,sans-serif;font-size:10.5pt;color:#111;margin:0}
            h1{font-size:14pt;text-align:center;margin:0 0 2mm}
            .sub{text-align:center;font-size:9pt;color:#555;margin-bottom:6mm}
            h2{font-size:10.5pt;margin:6mm 0 2mm;padding:1.2mm 2mm;background:#eef1f5;border-left:3px solid #1f3a5f}
            table{width:100%;border-collapse:collapse}
            th{width:45mm;text-align:left;font-weight:normal;color:#444;padding:1.4mm 2mm;vertical-align:top}
            td{padding:1.4mm 2mm;border-bottom:1px solid #ccd;font-weight:600}
            .mono{font-family:Consolas,"Courier New",monospace;letter-spacing:.4px}
            .ref{margin:0 0 4mm}
            .texto{text-align:justify;line-height:1.45;margin:0 0 2mm}
            .nota{font-size:8.5pt;color:#555;text-align:justify;line-height:1.4;margin-top:3mm}
            .casilla{display:inline-block;width:3.6mm;height:3.6mm;border:1px solid #333;text-align:center;line-height:3.6mm;font-size:9pt;margin:0 2mm 0 1mm;vertical-align:-0.6mm}
            .firma{margin-top:8mm;display:flex;gap:10mm}
            .firma div{flex:1}
            .recuadro{height:28mm;border:1px solid #888;margin-top:2mm}
            </style></head><body>
            """);
        sbHtml.Append("<h1>ORDEN DE DOMICILIACIÓN DE ADEUDO DIRECTO SEPA CORE</h1><div class=\"sub\">SEPA Direct Debit Core Mandate</div>");
        sbHtml.Append($"<table class=\"ref\">{Fila("Referencia de la orden", d.Referencia, "mono")}</table>");
        sbHtml.Append($"<p class=\"texto\">Mediante la firma de esta orden de domiciliación, el deudor autoriza (A) al acreedor <b>{H(a.Nombre)}</b> a enviar " +
            "instrucciones a la entidad del deudor para adeudar su cuenta y (B) a la entidad para efectuar los adeudos en su cuenta siguiendo las " +
            "instrucciones del acreedor.</p>");
        sbHtml.Append(Bloque("DATOS DEL DEUDOR", d.Deudor, d.DomicilioDeudor, ("Número de cuenta (IBAN)", Ibanes.Agrupar(d.Iban) ?? "", "mono")));
        sbHtml.Append(Bloque("DATOS DEL ACREEDOR", a.Nombre, a.Domicilio, ("Identificador del acreedor", a.Identificador, "mono")));
        sbHtml.Append("<h2>TIPO DE PAGO</h2><p>Pago periódico<span class=\"casilla\">X</span> &nbsp;&nbsp; Pago único<span class=\"casilla\"></span></p>");
        sbHtml.Append($"<div class=\"firma\"><div><table>{Fila("Localidad", d.Localidad)}{Fila("Fecha", d.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture))}</table></div>" +
            "<div>Firma del deudor:<div class=\"recuadro\"></div></div></div>");
        sbHtml.Append("<p class=\"nota\">Como parte de sus derechos, el deudor está legitimado al reembolso por su entidad en los términos y condiciones del " +
            "contrato suscrito con la misma. La solicitud de reembolso deberá efectuarse dentro de las ocho semanas que siguen a la fecha de adeudo en cuenta. " +
            "Puede obtener información adicional sobre sus derechos en su entidad financiera. Todos los campos han de ser cumplimentados obligatoriamente. " +
            "Una vez firmada, esta orden de domiciliación debe ser enviada al acreedor para su custodia.</p>");
        return sbHtml.Append("</body></html>").ToString();
    }
}

/// <summary>Lee de la tabla Sociedades los datos del acreedor del mandato SEPA.</summary>
public sealed class LectorAcreedorSepa(IBaseDatos bd)
{
    public Task<AcreedorSepa?> LeerAsync(string idSociedad) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT Nombre, CIF, DireccionFiscal, CodigoPostalFiscal, PoblacionFiscal, ProvinciaFiscal FROM Sociedades WHERE IdSociedad = ?",
        f => (AcreedorSepa?)new AcreedorSepa((f.Texto("Nombre") ?? "").Trim(), (f.Texto("CIF") ?? "").Trim(),
            new DomicilioSepa((f.Texto("DireccionFiscal") ?? "").Trim(), (f.Texto("CodigoPostalFiscal") ?? "").Trim(), (f.Texto("PoblacionFiscal") ?? "").Trim(),
                (f.Texto("ProvinciaFiscal") ?? "").Trim(), "España")), idSociedad));
}
