using ProConta.Datos.Esquema;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Resultados;

namespace ProConta.Plataforma.Configuracion;

/// <summary>
/// Comprobación al arrancar (RF-CFG-04): la BD resuelta tiene la versión de esquema que espera el
/// programa. No crea ni migra nada: si hay cambios pendientes indica que se ejecute el ActualizadorEsquema.
/// </summary>
public static class ComprobadorArranque
{
    public static async Task<Resultado<DiagnosticoEsquema>> ComprobarAsync(UbicacionDatos ubicacion)
    {
        try
        {
            var diagnostico = await DiagnosticadorEsquema.DiagnosticarAsync(new BaseDatosAccess(ubicacion.RutaBaseDatos, ModoApertura.SoloLectura));
            return diagnostico.EsCompatible
                ? Resultado<DiagnosticoEsquema>.Correcto(diagnostico)
                : Resultado<DiagnosticoEsquema>.Fallo(diagnostico.ComoError()!);
        }
        catch (ExcepcionDatos ex)
        {
            return Resultado<DiagnosticoEsquema>.Fallo(ex.Error);
        }
    }
}
