using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.UI.Asientos;

/// <summary>
/// Lo que lee y graba la pantalla de asiento manual (contabilidad/pantallas, PR 2 de contabilidad/plantillas-asiento). La implementa
/// <see cref="FuenteAsientoManualAccess"/>; las pruebas de la pantalla la sustituyen para no tocar la BD.
/// </summary>
public interface IFuenteAsientoManual
{
    ContextoSociedadEjercicio Contexto { get; }

    /// <summary>Plantillas de usuario activas de la empresa (nunca las de sistema), en su orden; con <paramref name="origen"/>, solo las de ese origen.</summary>
    Task<IReadOnlyList<PlantillaAsiento>> PlantillasAsync(OrigenPlantilla? origen = null);

    /// <summary>Cuentas del ejercicio con su nombre, cuentas 113 y 570 de la empresa, trimestre activo y últimas cuentas usadas con la plantilla.</summary>
    Task<EntornoSociedad> EntornoAsync(PlantillaAsiento? plantilla = null);

    Task<IReadOnlyList<CentroCoste>> CentrosAsync();

    Task<Resultado<OrigenApunte>> OrigenApunteAsync(int idApunte);

    /// <summary>Una subcuenta del ejercicio de trabajo con su saldo y su saldo de apertura (correcciones de origen Subcuenta).</summary>
    Task<Resultado<OrigenSubcuenta>> OrigenSubcuentaAsync(string cuenta);

    Task<Asiento?> AsientoAsync(int idAsiento);

    /// <summary>Graba una propuesta de plantilla (corrección de un apunte o varios asientos): relación con el origen, IdLote común y punteo.</summary>
    Task<Resultado<IReadOnlyList<int>>> EjecutarPlantillaAsync(PropuestaPlantilla propuesta, string motivo);

    /// <summary>Alta de uno o varios asientos manuales nuevos en una transacción.</summary>
    Task<Resultado<IReadOnlyList<int>>> GuardarNuevosAsync(IReadOnlyList<Asiento> asientos, string motivo);

    /// <summary>Modificación de un asiento manual (con su motivo).</summary>
    Task<Resultado<Asiento>> ModificarAsync(Asiento asiento, string motivo);
}

/// <summary>La fuente de la pantalla de asiento manual contra la BD: plantillas, cuentas, centros y motor de asientos.</summary>
public sealed class FuenteAsientoManualAccess(IBaseDatos bd, ContextoSociedadEjercicio contexto, Actor actor) : IFuenteAsientoManual
{
    private readonly ServicioPlantillasAsiento _plantillas = new(bd);
    private readonly MotorAsientosAccess _motor = new(bd);
    private readonly CatalogoCentrosCosteAccess _centros = new(bd);

    public ContextoSociedadEjercicio Contexto => contexto;

    public Task<IReadOnlyList<PlantillaAsiento>> PlantillasAsync(OrigenPlantilla? origen = null) => _plantillas.ListarAsync(contexto.IdSociedad, incluirSistema: false, origen);
    public Task<EntornoSociedad> EntornoAsync(PlantillaAsiento? plantilla = null) => _plantillas.EntornoAsync(contexto, plantilla);
    public Task<IReadOnlyList<CentroCoste>> CentrosAsync() => _centros.ListarAsync(contexto.IdSociedad);
    public Task<Resultado<OrigenApunte>> OrigenApunteAsync(int idApunte) => _plantillas.OrigenApunteAsync(idApunte);
    public Task<Resultado<OrigenSubcuenta>> OrigenSubcuentaAsync(string cuenta) => _plantillas.OrigenSubcuentaAsync(contexto, cuenta);
    public Task<Asiento?> AsientoAsync(int idAsiento) => _motor.ObtenerAsync(idAsiento);
    public Task<Resultado<IReadOnlyList<int>>> EjecutarPlantillaAsync(PropuestaPlantilla propuesta, string motivo) => _plantillas.EjecutarAsync(propuesta, motivo, actor);
    public Task<Resultado<IReadOnlyList<int>>> GuardarNuevosAsync(IReadOnlyList<Asiento> asientos, string motivo) => _motor.GuardarManualesAsync(asientos, actor, motivo);
    public Task<Resultado<Asiento>> ModificarAsync(Asiento asiento, string motivo) => _motor.GuardarManualAsync(asiento, actor, motivo);
}

/// <summary>Diálogos de la pantalla de asiento manual (los implementa <see cref="DialogosAsiento"/>; las pruebas los sustituyen).</summary>
public interface IDialogosAsiento
{
    /// <summary>Buscador común de cuentas del ejercicio, limitado a <paramref name="grupos"/> y con «Crear subcuenta»: el código elegido, o null.</summary>
    (string Codigo, string Nombre)? ElegirCuenta(string? texto, IReadOnlyList<string> grupos, string titulo);

    /// <summary>Pide un importe en euros (la coma es el decimal); null si se cancela.</summary>
    decimal? PedirImporte(string titulo, string explicacion);

    void Error(string titulo, string mensaje);
}
