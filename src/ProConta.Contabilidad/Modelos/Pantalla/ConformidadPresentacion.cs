using System.Globalization;
using ProConta.Contabilidad.Presentacion;
using ProConta.Nucleo.Identificacion;

namespace ProConta.Contabilidad.Modelos;

/// <summary>Una fila de la ventana de conformidad: etiqueta, valor y si hay que revisarla (sale en rojo).</summary>
public sealed record FilaConformidad(string Etiqueta, string Valor, bool Revisar = false);

/// <summary>
/// Lo que enseña la ventana de conformidad antes de presentar (contabilidad/modelos-tributarios/presentacion), igual para la
/// presentación directa y para TGVI Online: título, entorno, los datos de la declaración, lo que impide presentar, los avisos y si
/// hay PDF (el de ServaliDos o la vista previa de TGVI).
/// </summary>
public sealed record ConformidadPresentacion(string Titulo, EntornoAeat Entorno, IReadOnlyList<FilaConformidad> Filas, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos,
    byte[]? Pdf, bool SePuedePresentar)
{
    public bool Pruebas => Entorno == EntornoAeat.Pruebas;

    public static ConformidadPresentacion De(BorradorAeat b)
    {
        var d = b.Declaracion;
        var nifCorrecto = string.Equals(b.NifLeido, d.NifSociedad, StringComparison.OrdinalIgnoreCase);
        var filas = new List<FilaConformidad>
        {
            new("Modelo y periodo", d.Titulo),
            new("Declarante (NIF leído del fichero)", $"{b.NifLeido ?? "ninguno"} · {d.RazonSocial}" + (nifCorrecto ? "" : $"  ⚠ no es el de la sociedad ({d.NifSociedad})"), !nifCorrecto),
        };
        filas.AddRange(d.Resumen.Select(r => new FilaConformidad(r.Etiqueta, r.Valor)));
        filas.Add(new("Forma de pago", d.TextoFormaPago));
        if (d.FormaPago is 'U' or 'I') filas.Add(new("Importe a ingresar", PeriodoModelo.Euros(d.ImporteAIngresar)));
        if (d.FormaPago == 'U') filas.Add(new("Cuenta de domiciliación", Ibanes.Agrupar(d.Iban) ?? "sin cuenta", d.Iban is null));
        if (d.FormaPago == 'I') filas.Add(new("NRC del pago", d.Nrc ?? "sin NRC", d.Nrc is null));
        if (d.ComplementariaDe is { } ant) filas.Add(new("Rectifica la declaración", $"justificante {ant}"));
        filas.Add(new("Presentador (titular del certificado)", b.Presentador?.Descripcion ?? "sin certificado", b.Presentador is null));
        filas.Add(new("Se envía a", b.Entorno == EntornoAeat.Pruebas ? "Entorno de PRUEBAS de la AEAT, por presentación directa" : "AEAT (producción), por presentación directa"));
        return new ConformidadPresentacion($"Presentar {d.Titulo}", b.Entorno, filas, b.Bloqueos, b.Avisos, b.Validacion.Pdf, b.SePuedePresentar);
    }

    public static ConformidadPresentacion De(BorradorTgvi b)
    {
        var d = b.Declaracion;
        var nifCorrecto = string.Equals(d.NifLeido, d.NifSociedad.TrimStart('0'), StringComparison.OrdinalIgnoreCase) || string.Equals(d.NifLeido, d.NifSociedad, StringComparison.OrdinalIgnoreCase);
        var filas = new List<FilaConformidad>
        {
            new("Modelo y periodo", d.Titulo),
            new("Declarante (NIF leído del registro de declarante)", $"{d.NifLeido} · {d.RazonSocial}" + (nifCorrecto ? "" : $"  ⚠ no es el de la sociedad ({d.NifSociedad})"), !nifCorrecto),
        };
        filas.AddRange(d.Resumen.Select(r => new FilaConformidad(r.Etiqueta, r.Valor)));
        filas.Add(new("Registros", $"{b.Correctos.ToString(CultureInfo.InvariantCulture)} correctos" + (b.Erroneos > 0 ? $" y {b.Erroneos} erróneos (no se presentan; quedan para una complementaria)" : ""), b.Erroneos > 0));
        filas.Add(new("Importe declarado", PeriodoModelo.Euros(d.Total)));
        filas.Add(new("Número identificativo", d.NumeroJustificante));
        if (d.ComplementariaDe is { } ant) filas.Add(new("Sustituye o completa la declaración", ant));
        filas.Add(new("Presentador (titular del certificado)", b.Presentador?.Descripcion ?? "sin certificado", b.Presentador is null));
        filas.Add(new("Se envía a", b.Entorno == EntornoAeat.Pruebas ? "Entorno de PRUEBAS de la AEAT, por TGVI Online" : "AEAT (producción), por TGVI Online"));
        // Los registros erróneos no impiden presentar los correctos (los bloqueos ya lo dicen si no queda ninguno): se enseñan como avisos.
        var avisos = new List<string>(b.Avisos);
        avisos.AddRange(b.Errores.Take(20).Select(e => $"Registro erróneo: {e}"));
        avisos.AddRange(b.AvisosAeat.Select(a => $"Aviso de la AEAT: {a}"));
        return new ConformidadPresentacion($"Presentar {d.Titulo}", b.Entorno, filas, b.Bloqueos, avisos, b.VistaPrevia, b.SePuedePresentar);
    }
}
