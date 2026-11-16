using System.Globalization;
using System.Text;

namespace ProConta.Contabilidad.Diario;

/// <summary>
/// Una columna del Diario (contabilidad/diario 2026-11-14 21:50): su clave estable, título, ayuda, si viene oculta de fábrica y el valor
/// de un apunte, con su tipo (número, fecha, importe o texto) para que Excel lo pueda sumar y filtrar.
/// </summary>
public sealed record ColumnaDiario(string Clave, string Titulo, string Ayuda, bool OcultaDeFabrica, Func<ApunteDiario, object?> Valor, bool Derecha = false, bool Mono = false)
{
    /// <summary>El valor como texto de pantalla o de PDF: importes con dos decimales (vacío si es cero), fechas dd/MM/aaaa y ✓ en las casillas.</summary>
    public string Texto(ApunteDiario apunte) => ColumnasDiario.Formatear(Valor(apunte));
}

/// <summary>Las columnas del Diario, en su orden de fábrica. La pantalla y la exportación usan las mismas.</summary>
public static class ColumnasDiario
{
    public const string Asiento = "asiento", IdApunte = "id-apunte", Fecha = "fecha", Origen = "origen", Cuenta = "cuenta", Nombre = "nombre", Concepto = "concepto",
        Documento = "documento", Debe = "debe", Haber = "haber", Centro = "centro", Punteado = "punteado", Bloqueos = "bloqueos", Orden = "orden", Contrapartida = "contrapartida",
        Vencimiento = "vencimiento", FechaDocumento = "fecha-documento", Grabado = "grabado", Modificado = "modificado", Operador = "operador";

    public static readonly IReadOnlyList<ColumnaDiario> Todas =
    [
        new(Asiento, "Asiento", "Número del asiento (Asientos.IdAsiento): fijo, no cambia al grabar otros asientos; los huecos son asientos borrados", false, a => a.Asiento.IdAsiento, Derecha: true),
        new(IdApunte, "Id apunte", "Identificador del apunte (Apuntes.IdApunte)", false, a => a.IdApunte, Derecha: true),
        new(Fecha, "Fecha", "Fecha contable: decide el periodo y el trimestre de IVA", false, a => a.Asiento.Fecha, Mono: true),
        new(Origen, "Origen", "Módulo que generó el asiento. Los que no son «Manual» son automáticos", false, a => a.Asiento.Origen),
        new(Cuenta, "Subcuenta", "Subcuenta del apunte", false, a => a.Cuenta, Mono: true),
        new(Nombre, "Descripción", "Descripción de la subcuenta", false, a => a.NombreCuenta),
        new(Concepto, "Concepto", "Descripción del apunte", false, a => a.Concepto),
        new(Documento, "Documento", "Número de factura o de documento del apunte", false, a => a.Documento, Mono: true),
        new(Debe, "Debe", "Importe al Debe", false, a => a.Debe == 0 ? null : a.Debe, Derecha: true),
        new(Haber, "Haber", "Importe al Haber", false, a => a.Haber == 0 ? null : a.Haber, Derecha: true),
        new(Centro, "Centro de coste", "Centro de coste del apunte", false, a => a.CentroCoste),
        new(Punteado, "Punteado", "Punteado: apunte ya revisado", false, a => a.Punteado),
        new(Bloqueos, "Bloqueos", "Qué no se puede cambiar del apunte y por qué. Pasa el ratón por el icono", false, a => string.Concat(a.Bloqueos.Select(b => b.Icono))),
        new(Orden, "Orden", "Orden del apunte dentro de su asiento", true, a => a.Orden, Derecha: true),
        new(Contrapartida, "Contrapartida", "Cuenta del lado contrario del mismo asiento, sin contar el IVA. «Varias» si hay más de una", true, a => a.Contrapartida, Mono: true),
        new(Vencimiento, "Vencimiento", "Fecha de vencimiento del apunte", true, a => a.Vencimiento, Mono: true),
        new(FechaDocumento, "Fecha del documento", "Fecha de expedición de la factura del libro de IVA del asiento", true, a => a.Asiento.FechaDocumento, Mono: true),
        new(Grabado, "Grabado el", "Día del alta del asiento. No cambia nunca", true, a => a.Asiento.GrabadoEl?.Date, Mono: true),
        new(Modificado, "Modificado el", "Último cambio del asiento", true, a => a.Asiento.ModificadoEl?.Date, Mono: true),
        new(Operador, "Operador", "Quién grabó el asiento", true, a => a.Asiento.OperadorCreacion),
    ];

    public static ColumnaDiario De(string clave) => Todas.First(c => c.Clave == clave);

    public static string Formatear(object? valor) => valor switch
    {
        null => "",
        decimal d => d.ToString("#,##0.00", TextoDiario.Es),
        DateTime f => f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        bool b => b ? "✓" : "",
        int i => i.ToString(CultureInfo.InvariantCulture),
        _ => valor.ToString() ?? "",
    };
}

/// <summary>Lo que identifica una exportación del Diario: qué se ve y con qué filtro, para la cabecera del PDF y del Excel.</summary>
public sealed record CabeceraExportacionDiario(string RazonSocial, string Ejercicios, string Periodo, string Filtros, bool PorAsiento, bool TotalesPorMes, DateTime Generado);

/// <summary>
/// Exportación del Diario (contabilidad/diario 2026-11-14 21:50): todas las filas que cumplen el filtro, con las columnas visibles y sus
/// totales. No hay libro Diario oficial con «suma y sigue». El HTML es el que el motor de informes convierte en PDF con texto real.
/// </summary>
public static class ExportacionDiario
{
    private static readonly string[] _meses = ["enero", "febrero", "marzo", "abril", "mayo", "junio", "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"];

    public static string NombreMes(int mes) => _meses[Math.Clamp(mes, 1, 12) - 1];

    /// <summary>Apuntes que salen en la exportación, en su orden: en la vista por asiento, cada asiento completo; en lista, los que cumplen el filtro.</summary>
    public static IEnumerable<ApunteDiario> Apuntes(VistaDiario vista, bool porAsiento) =>
        porAsiento ? vista.Asientos.SelectMany(a => a.Asiento.Apuntes) : vista.Apuntes;

    public static string TituloHoja(CabeceraExportacionDiario c) => $"Diario · {c.RazonSocial} · {c.Ejercicios} · {c.Periodo}" + (c.Filtros.Length > 0 ? $" · {c.Filtros}" : "");

    /// <summary>
    /// HTML del Diario para el PDF, en A4 horizontal. Por asiento: una línea de cabecera por asiento con su total y debajo todos sus apuntes
    /// (y, si se pide, una línea de totales por mes); en lista: un apunte por fila, en el orden recibido. Al final, los totales.
    /// </summary>
    public static string Html(CabeceraExportacionDiario cabecera, IReadOnlyList<ColumnaDiario> columnas, VistaDiario vista, IReadOnlyList<ApunteDiario>? ordenLista = null)
    {
        // Solo lo que rompe el HTML: el documento va en UTF-8, así que los acentos y «·» se dejan como están.
        static string E(string? t) => (t ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><title>Diario</title><style>")
          .Append("body{font:8.5pt 'Segoe UI',Arial,sans-serif;color:#16213A;margin:0}h1{font-size:14pt;margin:0 0 2pt}p{margin:0 0 6pt;color:#3B4252}")
          .Append("table{border-collapse:collapse;width:100%}thead{display:table-header-group}tr{page-break-inside:avoid}")
          .Append("th{text-align:left;border-bottom:1pt solid #16213A;padding:3pt 4pt;font-size:8pt;white-space:nowrap}")
          .Append("td{padding:2pt 4pt;border-bottom:.4pt solid #E1DDD2;vertical-align:top}.n{text-align:right;white-space:nowrap}.m{font-family:Consolas,monospace;white-space:nowrap}")
          .Append("tr.g td{background:#F3F0E9;font-weight:600;border-top:.6pt solid #7A8090}tr.t td{background:#ECE8DE;font-weight:600}tr.f td{border-top:1pt solid #16213A;font-weight:700}")
          .Append("</style></head><body>");
        sb.Append("<h1>Diario · ").Append(E(cabecera.RazonSocial)).Append("</h1><p>")
          .Append(E($"Ejercicio {cabecera.Ejercicios} · {cabecera.Periodo}" + (cabecera.Filtros.Length > 0 ? $" · Filtros: {cabecera.Filtros}" : "")
                    + $" · {(cabecera.PorAsiento ? "Por asiento" : "Lista de apuntes")} · generado el {cabecera.Generado:dd/MM/yyyy HH:mm}"))
          .Append("</p><table><thead><tr>");
        foreach (var c in columnas) sb.Append(c.Derecha ? "<th class=\"n\">" : "<th>").Append(E(c.Titulo)).Append("</th>");
        sb.Append("</tr></thead><tbody>");

        void Celdas(Func<ColumnaDiario, string?> texto)
        {
            foreach (var c in columnas)
                sb.Append("<td").Append(c.Derecha ? " class=\"n\"" : c.Mono ? " class=\"m\"" : "").Append('>').Append(E(texto(c))).Append("</td>");
        }
        void Apunte(ApunteDiario a, bool agrupado)
        {
            sb.Append("<tr>");
            Celdas(c => agrupado && c.Clave is ColumnasDiario.Asiento or ColumnasDiario.Fecha or ColumnasDiario.Origen ? "" : c.Texto(a));
            sb.Append("</tr>");
        }
        // Una fila de resumen: el texto en la primera columna de texto libre que haya y los importes en Debe y Haber.
        void Resumen(string clase, string texto, decimal debe, decimal haber, int? asiento = null, DateTime? fecha = null, string? origen = null)
        {
            var dondeTexto = columnas.FirstOrDefault(c => c.Clave is ColumnasDiario.Concepto or ColumnasDiario.Nombre)
                             ?? columnas.FirstOrDefault(c => c.Clave is not (ColumnasDiario.Asiento or ColumnasDiario.Fecha or ColumnasDiario.Debe or ColumnasDiario.Haber or ColumnasDiario.Origen))
                             ?? columnas[0];
            sb.Append("<tr class=\"").Append(clase).Append("\">");
            Celdas(c => c.Clave switch
            {
                ColumnasDiario.Debe => ColumnasDiario.Formatear(debe),
                ColumnasDiario.Haber => ColumnasDiario.Formatear(haber),
                ColumnasDiario.Asiento when asiento is not null => ColumnasDiario.Formatear(asiento),
                ColumnasDiario.Fecha when fecha is not null => ColumnasDiario.Formatear(fecha),
                ColumnasDiario.Origen when origen is not null && c != dondeTexto => origen,
                _ => c == dondeTexto ? texto : "",
            });
            sb.Append("</tr>");
        }

        if (cabecera.PorAsiento)
        {
            (int, int)? mes = null;
            var totales = cabecera.TotalesPorMes ? vista.TotalesPorMes().ToDictionary(t => (t.Ejercicio, t.Mes)) : null;
            void CerrarMes()
            {
                if (totales is null || mes is not { } m) return;
                var t = totales[m];
                Resumen("t", $"Total de {NombreMes(t.Mes)} de {t.Ejercicio} · {t.Asientos} asiento{(t.Asientos == 1 ? "" : "s")}", t.Debe, t.Haber);
            }
            foreach (var (asiento, _) in vista.Asientos)
            {
                var actual = (asiento.Fecha.Year, asiento.Fecha.Month);
                if (mes != actual) { CerrarMes(); mes = actual; }
                var hayAsiento = columnas.Any(c => c.Clave == ColumnasDiario.Asiento);
                var hayFecha = columnas.Any(c => c.Clave == ColumnasDiario.Fecha);
                var texto = (hayAsiento ? "" : $"Asiento {asiento.IdAsiento} · ") + (hayFecha ? "" : $"{asiento.Fecha:dd/MM/yyyy} · ") + asiento.Descripcion
                            + (columnas.Any(c => c.Clave == ColumnasDiario.Origen) ? "" : $" · {asiento.Origen}");
                Resumen("g", texto, asiento.TotalDebe, asiento.TotalHaber, asiento.IdAsiento, asiento.Fecha, asiento.Origen);
                foreach (var a in asiento.Apuntes) Apunte(a, agrupado: true);
            }
            CerrarMes();
            Resumen("f", $"Total · {vista.NumeroAsientos} asiento{(vista.NumeroAsientos == 1 ? "" : "s")} · {vista.ApuntesAsientos} apuntes", vista.DebeAsientos, vista.HaberAsientos);
        }
        else
        {
            var lista = ordenLista ?? vista.Apuntes.ToList();
            foreach (var a in lista) Apunte(a, agrupado: false);
            var debe = lista.Sum(a => a.Debe);
            var haber = lista.Sum(a => a.Haber);
            Resumen("f", $"Total · {lista.Count} apunte{(lista.Count == 1 ? "" : "s")} · saldo {ColumnasDiario.Formatear(debe - haber)}", debe, haber);
        }
        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }
}
