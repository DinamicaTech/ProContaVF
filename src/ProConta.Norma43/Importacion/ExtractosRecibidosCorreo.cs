using ProConta.Correo;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.Norma43.Importacion;

/// <summary>
/// Extractos Norma 43 recibidos por correo (correo/recepcion, derivado de norma43 2026-11-12): los importa con el mismo motor
/// que la pantalla Bancos. Sin diálogo posible, una cuenta bancaria desconocida deja el adjunto con incidencia: se da de alta
/// importando el fichero desde Bancos, o se da de alta y se vuelve a procesar el mensaje.
/// </summary>
public sealed class ProcesadorExtractosCorreo(ServicioImportacionN43 servicio) : IProcesadorExtractoBancarioRecibido
{
    public async Task<ResultadoExtractoRecibido> ProcesarAsync(ContextoSociedadEjercicio contexto, string ruta, string nombre, Actor actor, CancellationToken cancelacion = default)
    {
        var contenido = await File.ReadAllBytesAsync(ruta, cancelacion);
        var r = await servicio.ImportarAsync(contexto, contenido, nombre, ruta, actor, null, cancelacion);
        return r.Veredicto switch
        {
            VeredictoImportacion.Importable or VeredictoImportacion.Reactivable or VeredictoImportacion.YaImportado =>
                new ResultadoExtractoRecibido(EstadoAdjuntoRecibido.Procesado, r.Explicacion),
            VeredictoImportacion.CuentasDesconocidas => new ResultadoExtractoRecibido(EstadoAdjuntoRecibido.Error,
                r.Explicacion + " Desde el correo no puedo darlas de alta: importa este fichero en Bancos › Importar N43, donde se ofrece el alta, o da de alta la cuenta y vuelve a procesar el mensaje."),
            _ => new ResultadoExtractoRecibido(EstadoAdjuntoRecibido.Error, r.Explicacion),
        };
    }
}
