using ProConta.Correo;
using ProConta.Nominas.Contabilizacion;
using ProConta.Nominas.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.Nominas.Correo;

/// <summary>
/// Excel de nóminas recibido por correo (correo/recepcion, derivado de nominas 2026-11-13, P6): se importa con el mismo motor que
/// la pantalla Nóminas y se contabiliza si no queda nada por resolver. Lo que necesita a una persona (trabajador sin reconocer,
/// cabecera nueva, otra versión de un periodo ya importado, un error al contabilizar) deja el adjunto «para revisar» con la
/// explicación; las nóminas importadas esperan en la pantalla Nóminas.
/// </summary>
public sealed class ProcesadorNominasCorreo(ServicioImportacionNominas importacion, ServicioContabilizacionNominas contabilizacion) : IProcesadorNominasRecibidas
{
    public async Task<ResultadoNominasRecibidas> ProcesarAsync(ContextoSociedadEjercicio contexto, string ruta, string nombre, int idAdjunto, Actor actor,
        CancellationToken cancelacion = default)
    {
        var contenido = await File.ReadAllBytesAsync(ruta, cancelacion);
        var imp = await importacion.ImportarAsync(contexto, contenido, nombre, OrigenNominas.Correo, actor, idAdjuntoRecibido: idAdjunto);
        if (!imp.EsCorrecto) return new(EstadoAdjuntoRecibido.Error, null, string.Join(" ", imp.Errores.Select(e => e.TextoOperador())));
        var r = imp.Valor;
        switch (r.Veredicto)
        {
            case VeredictoNominas.YaImportado:
                return new(EstadoAdjuntoRecibido.Procesado, null, r.Explicacion);
            case VeredictoNominas.Importable when r.Estado == ServicioImportacionNominas.EstadoPendiente && r.IdImportacion is int id:
            {
                var c = await contabilizacion.ContabilizarAsync(id, actor);
                if (!c.EsCorrecto)
                    return new(EstadoAdjuntoRecibido.Revisar, null, r.Explicacion + " No se han podido contabilizar: " + string.Join(" ", c.Errores.Select(e => e.TextoOperador())) +
                        " Están en la pantalla Nóminas, pendientes de contabilizar.");
                var avisos = c.Valor.Avisos.Count == 0 ? "" : " " + string.Join(" ", c.Valor.Avisos);
                return new(EstadoAdjuntoRecibido.Procesado, c.Valor.Asientos.Count == 1 ? c.Valor.Asientos[0] : null,
                    r.Explicacion + $" Contabilizadas en {c.Valor.Asientos.Count} asiento(s) del {c.Valor.FechaAsiento:dd/MM/yyyy}." + avisos);
            }
            case VeredictoNominas.Importable:
                return new(EstadoAdjuntoRecibido.Revisar, null, r.Explicacion + " Resuélvelos en la pantalla Nóminas (clic derecho › Resolver trabajador…) y contabiliza.");
            case VeredictoNominas.OtraVersion:
                return new(EstadoAdjuntoRecibido.Revisar, null, r.Explicacion +
                    " Si es la versión corregida, ábrela en la pantalla Nóminas con «Reemplazar con otro fichero…» sobre ese periodo (el adjunto está en " + ruta + ").");
            case VeredictoNominas.CabecerasSinAlias:
                return new(EstadoAdjuntoRecibido.Revisar, null, r.Explicacion +
                    " Impórtalo desde la pantalla Nóminas («Importar fichero…», el adjunto está en " + ruta + "): allí se elige el campo de cada columna y se recuerda para los siguientes.");
            default:
                return new(EstadoAdjuntoRecibido.Error, null, r.Explicacion);
        }
    }
}
