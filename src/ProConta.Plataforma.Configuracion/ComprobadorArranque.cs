using ProConta.Datos.Esquema;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;

namespace ProConta.Plataforma.Configuracion;

/// <summary>Resultado de la comprobación al arrancar: el diagnóstico (si se pudo leer) y, si no es compatible, el error.</summary>
public sealed record ComprobacionArranque(DiagnosticoEsquema? Diagnostico, Error? Error)
{
    public bool EsCompatible => Error is null && Diagnostico is not null;

    /// <summary>Solo le faltan migraciones del catálogo y se puede actualizar: el arranque ofrece «Actualizar ahora».</summary>
    public bool SePuedeActualizarAhora => Error is not null && Diagnostico is { SePuedeActualizar: true, Pendientes.Count: > 0 };
}

/// <summary>
/// Comprobación al arrancar (RF-CFG-04): la BD resuelta tiene la versión de esquema que espera el programa. Comprobar no cambia
/// nada; si solo faltan migraciones, el arranque ofrece actualizarla en el momento con <see cref="ActualizarAhoraAsync"/>, que hace
/// lo mismo que el ActualizadorEsquema con su copia previa (plataforma/entorno-pruebas 2026-11-16).
/// </summary>
public static class ComprobadorArranque
{
    /// <summary>Subcarpeta, junto a la BD, donde «Actualizar ahora» deja la copia previa.</summary>
    public const string CarpetaCopiasEsquema = "copias-esquema";

    public static async Task<Resultado<DiagnosticoEsquema>> ComprobarAsync(UbicacionDatos ubicacion)
    {
        var c = await DiagnosticarAsync(ubicacion);
        return c.EsCompatible ? Resultado<DiagnosticoEsquema>.Correcto(c.Diagnostico!) : Resultado<DiagnosticoEsquema>.Fallo(c.Error!);
    }

    public static async Task<ComprobacionArranque> DiagnosticarAsync(UbicacionDatos ubicacion)
    {
        try
        {
            var diagnostico = await DiagnosticadorEsquema.DiagnosticarAsync(new BaseDatosAccess(ubicacion.RutaBaseDatos, ModoApertura.SoloLectura));
            return new(diagnostico, diagnostico.EsCompatible ? null : diagnostico.ComoError());
        }
        catch (ExcepcionDatos ex)
        {
            return new(null, ex.Error);
        }
    }

    /// <summary>Carpeta de la copia previa de «Actualizar ahora» para esta BD.</summary>
    public static string CarpetaCopias(UbicacionDatos ubicacion) => Path.Combine(ubicacion.DirectorioDatos, CarpetaCopiasEsquema);

    /// <summary>Aplica las migraciones pendientes con copia previa en <see cref="CarpetaCopias"/>. Exige que nadie tenga abierta la BD.</summary>
    public static Task<Resultado<InformeActualizacion>> ActualizarAhoraAsync(UbicacionDatos ubicacion, string operador) =>
        new ActualizadorEsquema(new Actor(operador)).ActualizarAsync(ubicacion.RutaBaseDatos, CarpetaCopias(ubicacion));
}
