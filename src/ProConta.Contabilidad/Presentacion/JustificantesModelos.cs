using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Documentos;
using ProConta.Nucleo.Auditoria;

namespace ProConta.Contabilidad.Presentacion;

/// <summary>
/// Archivo del PDF justificante de una presentación (AEAT o TGVI) por el gestor documental (documentos 2026-11-18, parte b): se guarda donde se
/// guardaba («&lt;ejercicio&gt;\Modelos AEAT\&lt;modelo&gt;\[PRUEBAS ]&lt;modelo&gt; &lt;ejercicio&gt; &lt;periodo&gt; &lt;justificante&gt;.pdf», el productor decide el
/// nombre), queda registrado como documento de tipo «Modelo presentado» vinculado al modelo y periodo, y la presentación apunta a él
/// (PresentacionesAeat.IdDocumento) además de guardar la ruta (RutaPdf), que siguen usando «Abrir el justificante» y la pantalla.
/// </summary>
public static class JustificantesModelos
{
    public const string Origen = "PRESENTACION";

    /// <summary>Guarda el PDF y devuelve su ruta completa. Lanza <see cref="IOException"/> si no se ha podido guardar.</summary>
    public static async Task<string> ArchivarAsync(IBaseDatos bd, string operador, IReloj reloj, string idSociedad, string modelo, int ejercicio, string periodo,
        string justificante, EntornoAeat entorno, DateTime fecha, int idPresentacion, byte[] pdf, CancellationToken cancelacion = default)
    {
        var anio = ejercicio.ToString(CultureInfo.InvariantCulture);
        var pruebas = entorno == EntornoAeat.Pruebas ? "PRUEBAS " : "";
        var vinculo = Vinculo.Modelo(modelo, ejercicio, periodo);
        var titulo = $"{pruebas}{modelo} {Vinculo.PeriodoTexto(periodo)} {anio} · justificante {justificante}".Replace("  ", " ");
        var solicitud = new SolicitudDocumento(idSociedad, "MODELO", titulo, Path.Combine(anio, "Modelos AEAT", modelo),
            $"{pruebas}{modelo} {anio} {periodo} {justificante}.pdf", fecha.Date, ejercicio, Origen, [vinculo], Motivo: "Presentación");
        var r = await new ServicioDocumentos(bd, operador, reloj).GuardarAsync(solicitud, pdf, cancelacion);
        if (!r.EsCorrecto) throw new IOException(r.Error!.TextoOperador());
        await bd.EjecutarAsync(async s =>
        {
            var conDocumento = s.LeerEsquema().ExisteColumna("PresentacionesAeat", "IdDocumento");
            if (conDocumento)
                await s.EjecutarAsync("UPDATE PresentacionesAeat SET RutaPdf = ?, IdDocumento = ? WHERE IdPresentacionAeat = ?",
                    Recortar(r.Valor.RutaCompleta, 255), r.Valor.IdDocumento, idPresentacion);
            else
                await s.EjecutarAsync("UPDATE PresentacionesAeat SET RutaPdf = ? WHERE IdPresentacionAeat = ?", Recortar(r.Valor.RutaCompleta, 255), idPresentacion);
            return true;
        }, cancelacion);
        return r.Valor.RutaCompleta;
    }

    private static string? Recortar(string? s, int max) => s is null ? null : s.Length <= max ? s : s[..max];
}
