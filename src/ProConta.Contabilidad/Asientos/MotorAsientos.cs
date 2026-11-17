using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Asientos;

/// <summary>Una línea del registro de cambios de apuntes (tabla RegistroCambiosApuntes).</summary>
public sealed record CambioApunte(Guid IdLote, DateTime FechaHora, string Operador, string Motivo, int IdAsiento, int? IdApunte, int? Orden,
    string Operacion, string Campo, string? ValorAntiguo, string? ValorNuevo);

/// <summary>
/// Trabajo del área dueña del lote dentro de la misma transacción que sus asientos (p. ej. el libro de IVA y la marca
/// de la factura, compras/factura-compra/contabilizacion 2026-11-05 11:24). Antes recibe los asientos del lote que se
/// van a borrar (para quitar lo que los referencia); Despues, los nuevos. Si Despues falla, no se guarda nada.
/// </summary>
public sealed record TrabajoLote(
    Func<ISesionDatos, IReadOnlyList<int>, Task>? Antes = null,
    Func<ISesionDatos, IReadOnlyList<int>, Task<Resultado>>? Despues = null);

/// <summary>
/// Opciones de un lote automático. <see cref="LibroExterno"/>: el lote copia un libro contable externo ya cerrado en su
/// origen (el importador de ContaSol, implantacion/importador-contasol 2026-11-10), no edita la contabilidad; por eso se
/// admite en ejercicios cerrados y en trimestres anteriores al activo. El resto de reglas (cuadre, cuentas, centros, fecha
/// dentro del ejercicio, auditoría) se aplican igual.
/// </summary>
/// <see cref="DiferirSaldos"/>: no recalcula la caché de saldos de las cuentas en cada lote; quien la pide llama a
/// <see cref="IMotorAsientos.RecalcularSaldosAsync"/> al terminar (cargas masivas: el importador de ContaSol graba miles de lotes).
/// <see cref="CierreEjercicio"/>: lotes del cierre del ejercicio (regularización, cierre y apertura, contabilidad/procesos/cierre-ejercicio
/// 2026-11-17): no tienen efecto fiscal, así que se admiten aunque su fecha caiga en un trimestre cerrado. Solo para el motor del cierre.
public sealed record OpcionesLote(bool LibroExterno = false, bool DiferirSaldos = false, int? TrimestreLiquidado = null, bool CierreEjercicio = false)
{
    public static readonly OpcionesLote Normal = new();

    /// <summary>Motor propietario de los lotes del cierre del ejercicio, el único que puede pedir <see cref="CierreEjercicio"/>.</summary>
    public const string MotorCierreEjercicio = "contabilidad/procesos/cierre-ejercicio";

    /// <summary>Motor propietario de los lotes de la liquidación del IVA, que puede pedir <see cref="TrimestreLiquidado"/>.</summary>
    public const string MotorLiquidacionIva = "contabilidad/procesos/liquidacion-iva";

    /// <summary>
    /// Motor propietario de los lotes de las facturas pendientes de recibir (contabilidad/procesos/facturas-pendientes-recibir
    /// 2026-11-17 16:40): la provisión del 31/12 lleva cuentas de gasto que son fiscales (621, 623…), así que también puede pedir
    /// <see cref="TrimestreLiquidado"/> 4, como la regularización de la prorrata.
    /// </summary>
    public const string MotorFacturasPendientes = "contabilidad/procesos/facturas-pendientes-recibir";

    /// <summary>
    /// Si el asiento cae en el trimestre que liquida el lote (<see cref="TrimestreLiquidado"/>, contabilidad/procesos/liquidacion-iva
    /// 2026-11-16): ese asiento se admite aunque el trimestre esté cerrado, porque es el que cierra su declaración.
    /// </summary>
    public bool ExentoTrimestreCerrado(Asiento asiento) =>
        LibroExterno || CierreEjercicio || (TrimestreLiquidado is int t && asiento.FechaContable.Year == asiento.Contexto.Ejercicio && Nucleo.Contexto.TrimestreContable.De(asiento.FechaContable) == t);

    /// <summary>Motor propietario de los lotes del importador de ContaSol, el único que puede pedir <see cref="LibroExterno"/>.</summary>
    public const string MotorImportadorContaSol = "implantacion/importador-contasol";
}

/// <summary>
/// Motor común de asientos: la única vía para crear, modificar o borrar asientos y apuntes. Valida el
/// agregado completo (cuadre, cuentas, centros, fecha y ejercicio abierto) dentro de la transacción en
/// que lo guarda, mantiene la caché de saldos de las cuentas afectadas, registra cada revisión
/// (RevisionesAsientos) y cada cambio de apunte con su valor anterior y nuevo, agrupados por IdLote.
/// Los asientos automáticos pertenecen a su lote y solo se regeneran o borran por él.
/// </summary>
public interface IMotorAsientos
{
    Task<Asiento?> ObtenerAsync(int idAsiento);

    /// <summary>Alta o modificación de un asiento manual. Modificar exige motivo; un asiento automático se rechaza.</summary>
    Task<Resultado<Asiento>> GuardarManualAsync(Asiento asiento, Actor actor, string? motivo = null);

    /// <summary>
    /// Alta de varios asientos manuales nuevos en una sola transacción y con un IdLote común en el registro de cambios (los que propone
    /// una plantilla, contabilidad/plantillas-asiento 2026-11-12 11:15): si uno falla, no se guarda ninguno. Con <paramref name="opciones"/>
    /// se graba también la relación de cada asiento nuevo con el de origen (RelacionesAsientos) y se puntean los apuntes indicados.
    /// </summary>
    Task<Resultado<IReadOnlyList<int>>> GuardarManualesAsync(IReadOnlyList<Asiento> asientos, Actor actor, string motivo, OpcionesAltaManual? opciones = null);

    /// <summary>
    /// Borra un asiento manual (con la versión leída y motivo) y deja su copia en AsientosBorrados y ApuntesBorrados, con la fecha,
    /// el operador y el motivo del borrado (contabilidad/diario 2026-11-14 21:50). El número del asiento no se reutiliza.
    /// </summary>
    Task<Resultado> EliminarManualAsync(int idAsiento, int versionLeida, Actor actor, string motivo);

    /// <summary>Copias de los asientos manuales borrados de unos ejercicios, del más reciente al más antiguo, con sus apuntes.</summary>
    Task<IReadOnlyList<AsientoBorrado>> AsientosBorradosAsync(IReadOnlyCollection<int> idsSociedadEjercicio);

    /// <summary>
    /// Corrección a mano de un asiento automático (contabilidad/diario 2026-11-14 21:50): cambia sus cuentas e importes con motivo, salvo
    /// los apuntes de la 572 y, si lleva libro de IVA, la fecha, los importes y las líneas de IVA y retención
    /// (<see cref="ReglasAsiento.ValidarCorreccionAutomatico"/>). El asiento sigue siendo de su lote y queda marcado como corregido a mano;
    /// regenerar el lote lo sustituye sin avisar. Se aplican el cuadre, el ejercicio cerrado y el trimestre cerrado como en cualquier asiento.
    /// </summary>
    Task<Resultado<Asiento>> CorregirAutomaticoAsync(Asiento asiento, Actor actor, string motivo);

    /// <summary>Si el asiento lleva libro de IVA (una fila en IVARepercutido o IVASoportado): factura de venta o de compra.</summary>
    Task<bool> TieneLibroIvaAsync(int idAsiento);

    /// <summary>
    /// Cambia el concepto, el documento, el vencimiento, el centro de coste o el punteo de unos apuntes en una operación (un IdLote en el
    /// registro de cambios), también en asientos automáticos y en trimestres cerrados, porque no tienen efecto contable directo
    /// (contabilidad/diario 2026-11-14 21:50). En un ejercicio cerrado solo se admiten el documento y el punteo. Todo salvo el punteo pide motivo.
    /// </summary>
    Task<Resultado<Guid>> CambiarDatosApuntesAsync(IReadOnlyCollection<int> idsApunte, CambioDatosApunte cambio, Actor actor, string? motivo = null);

    /// <summary>
    /// Sustituye todos los asientos de un lote automático por los indicados, en una sola transacción:
    /// regenerar no duplica, y si uno falla no se guarda ninguno.
    /// </summary>
    Task<Resultado<IReadOnlyList<int>>> ReemplazarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, IReadOnlyList<Asiento> asientos, Actor actor, string motivo,
        TrabajoLote? enLaTransaccion = null, OpcionesLote? opciones = null);

    /// <summary>
    /// Sustituye varios lotes, de uno o varios ejercicios, en una sola transacción (el cierre del ejercicio graba la regularización y el
    /// cierre en un ejercicio y la apertura en el siguiente, contabilidad/procesos/cierre-ejercicio 2026-11-17): un lote con la lista vacía
    /// se borra. <paramref name="alPrincipio"/> corre antes de comprobar nada (p. ej. quitar la marca de cerrado al reabrir) y
    /// <paramref name="alFinal"/> después de grabar todos los lotes (p. ej. poner la marca); si cualquiera falla, no se guarda nada.
    /// Devuelve los asientos de cada lote, en el orden recibido.
    /// </summary>
    Task<Resultado<IReadOnlyList<IReadOnlyList<int>>>> ReemplazarLotesAsync(IReadOnlyList<LoteAsientos> lotes, Actor actor, string motivo,
        Func<ISesionDatos, Task<Resultado>>? alPrincipio = null, Func<ISesionDatos, Task<Resultado>>? alFinal = null, OpcionesLote? opciones = null);

    /// <summary>Borra los asientos de un lote automático; devuelve cuántos.</summary>
    Task<Resultado<int>> EliminarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, Actor actor, string motivo, TrabajoLote? enLaTransaccion = null,
        OpcionesLote? opciones = null);

    /// <summary>
    /// Cambia de cuenta varios apuntes en una operación (mismo IdLote en el registro). En los asientos automáticos no se cambian los
    /// apuntes de la 572 ni, con libro de IVA, las líneas de IVA y retención, y el asiento queda marcado como corregido a mano.
    /// </summary>
    Task<Resultado<Guid>> CambiarCuentaApuntesAsync(ContextoSociedadEjercicio contexto, IReadOnlyCollection<int> idsApunte, string nuevaCuenta, Actor actor, string motivo);

    /// <summary>
    /// Puntea o despuntea apuntes desde el Mayor (contabilidad/mayor 2026-11-11 20:05), también de asientos automáticos y de
    /// periodos cerrados: el punteo no cambia la contabilidad, así que no cambia la versión del asiento. Cada cambio va al
    /// registro de cambios con un IdLote común; se conserva al guardar o regenerar el asiento.
    /// </summary>
    Task<Resultado<Guid>> PuntearApuntesAsync(IReadOnlyCollection<int> idsApunte, bool punteado, Actor actor, string? motivo = null);

    /// <summary>
    /// Pone el número de factura (NumeroDocumento) de unos apuntes, o lo quita con un texto vacío; también en asientos
    /// automáticos y en periodos cerrados, y regenerar el lote lo conserva si el proceso no trae otro.
    /// </summary>
    Task<Resultado<Guid>> AsignarNumeroDocumentoAsync(IReadOnlyCollection<int> idsApunte, string? numero, Actor actor, string motivo);

    /// <summary>Cambia el concepto de unos apuntes (vacío: el del asiento), también en automáticos; nunca en un ejercicio cerrado.</summary>
    Task<Resultado<Guid>> CambiarConceptoApuntesAsync(IReadOnlyCollection<int> idsApunte, string? concepto, Actor actor, string motivo);

    Task<IReadOnlyList<CambioApunte>> RegistroCambiosAsync(int idAsiento);

    /// <summary>Recalcula la caché de saldo y número de apuntes de todas las cuentas del ejercicio (tras lotes con <see cref="OpcionesLote.DiferirSaldos"/>).</summary>
    Task RecalcularSaldosAsync(ContextoSociedadEjercicio contexto);

    /// <summary>
    /// Carga masiva de un libro externo ya cerrado (contabilidad/asientos 2026-11-11 13:00): graba de una vez todos los asientos
    /// del ejercicio de ese motor y proceso, uno por clave de ejecución. Valida en memoria lo mismo que un lote; conserva los
    /// asientos sin cambios, sustituye los cambiados y borra los de claves que ya no vienen. Un asiento que no pasa la validación
    /// no se graba y, si ya estaba, se conserva. <paramref name="antesDeBorrar"/> recibe, en la transacción, los asientos que se van a borrar.
    /// </summary>
    Task<Resultado<ResultadoCargaLibro>> CargarLibroExternoAsync(ContextoSociedadEjercicio contexto, string motorPropietario, string codigoProceso,
        IReadOnlyList<AsientoExterno> asientos, Actor actor, string motivo, Func<ISesionDatos, IReadOnlyList<int>, Task>? antesDeBorrar = null,
        CancellationToken cancelacion = default);
}

/// <summary>
/// Opciones de <see cref="IMotorAsientos.GuardarManualesAsync"/>: el asiento de origen con que se relacionan los nuevos (TipoRelacion en
/// RelacionesAsientos, p. ej. «CORRECCION») y los apuntes ya grabados que quedan punteados (el apunte de origen) con el mismo IdLote.
/// </summary>
public sealed record OpcionesAltaManual(int? IdAsientoOrigen = null, string TipoRelacion = OpcionesAltaManual.RelacionCorreccion,
    IReadOnlyCollection<int>? PuntearApuntes = null)
{
    public const string RelacionCorreccion = "CORRECCION";
}

/// <summary>
/// Lo que cambia <see cref="IMotorAsientos.CambiarDatosApuntesAsync"/>: solo los campos con su «Cambia…» a true (o <see cref="Punteado"/>
/// con valor). Un texto vacío quita el concepto (queda el del asiento) o el documento; un vencimiento o centro null los quita.
/// </summary>
public sealed record CambioDatosApunte
{
    public bool CambiaConcepto { get; init; }
    public string? Concepto { get; init; }
    public bool CambiaDocumento { get; init; }
    public string? Documento { get; init; }
    public bool CambiaVencimiento { get; init; }
    public DateTime? Vencimiento { get; init; }
    public bool CambiaCentro { get; init; }
    public int? IdCentroCoste { get; init; }
    public bool? Punteado { get; init; }

    /// <summary>True si solo cambia el punteo: no pide motivo ni cambia la versión del asiento.</summary>
    public bool SoloPunteo => !CambiaConcepto && !CambiaDocumento && !CambiaVencimiento && !CambiaCentro;
    public bool Vacio => SoloPunteo && Punteado is null;
}

/// <summary>Un lote con su ejercicio y sus asientos, para <see cref="IMotorAsientos.ReemplazarLotesAsync"/> (lista vacía: se borra).</summary>
public sealed record LoteAsientos(ContextoSociedadEjercicio Contexto, IdentidadLote Lote, IReadOnlyList<Asiento> Asientos);

/// <summary>Apunte de un asiento borrado (tabla ApuntesBorrados).</summary>
public sealed record ApunteBorrado(int IdApunte, int Orden, string Cuenta, decimal Debe, decimal Haber, string? Concepto, string? Documento,
    DateTime? Vencimiento, int? IdCentroCoste, bool Punteado);

/// <summary>Copia de un asiento manual borrado (tabla AsientosBorrados) con sus apuntes, quién lo borró, cuándo y por qué.</summary>
public sealed record AsientoBorrado(int IdAsiento, int IdSociedadEjercicio, DateTime Fecha, string Descripcion, string? Notas, decimal TotalDebe,
    DateTime FechaHoraBorrado, string Operador, string Motivo, string? OperadorCreacion, DateTime? FechaHoraCreacion, IReadOnlyList<ApunteBorrado> Apuntes);

/// <summary>Un asiento de un libro externo con su clave de ejecución (la del documento de origen).</summary>
public sealed record AsientoExterno(string ClaveEjecucion, Asiento Asiento);

/// <summary>
/// Resultado de <see cref="IMotorAsientos.CargarLibroExternoAsync"/>: el IdAsiento de cada clave grabada o sin cambios, los
/// asientos borrados (clave e Id) y los errores de cada clave rechazada.
/// </summary>
public sealed record ResultadoCargaLibro(
    IReadOnlyDictionary<string, int> Ids,
    int Grabados,
    int SinCambios,
    IReadOnlyList<(string Clave, int IdAsiento)> Borrados,
    IReadOnlyDictionary<string, IReadOnlyList<Error>> Rechazados);

public sealed partial class MotorAsientosAccess(IBaseDatos bd, PoliticaCuadre? politica = null, IReloj? reloj = null) : IMotorAsientos
{
    private enum OperacionRevision : byte { Alta = 0, Modificacion = 1, Baja = 2, Lote = 3 }

    private readonly PoliticaCuadre _politica = politica ?? PoliticaCuadre.Estricta;
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    private const string ColumnasAsiento =
        "IdAsiento, IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, Notas, EstadoAsiento, Version, EsAutomatico, MotorPropietario, CodigoProceso, ClaveEjecucion, IdPlantillaAsiento, CorregidoAMano";
    private const string ColumnasApunte =
        "IdApunte, IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, Descripcion, NumeroDocumento, IdCliente, IdProveedor, FechaVencimiento, IdCentroCoste, TipoApunte, GeneradoAutomaticamente, MotivoGeneracion, Punteado";

    public Task<Asiento?> ObtenerAsync(int idAsiento) => bd.EjecutarAsync(s => CargarAsync(s, idAsiento));

    public Task<Resultado<Asiento>> GuardarManualAsync(Asiento asiento, Actor actor, string? motivo = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (asiento.EsAutomatico) return Resultado<Asiento>.Fallo(AutomaticoNoManual(asiento));
            if (!asiento.EsNuevo && string.IsNullOrWhiteSpace(motivo))
                return Resultado<Asiento>.Fallo(new Error("Asientos.SinMotivo", "Para modificar un asiento hay que indicar el motivo.", asiento.ToString(),
                    "Toda modificación queda registrada con su motivo.", "Escribe por qué cambias el asiento."));
            var idLote = Guid.NewGuid();
            var guardado = await GuardarInternoAsync(s, asiento, actor, motivo?.Trim() ?? "Alta manual", idLote, manual: true);
            return guardado.EsCorrecto ? Resultado<Asiento>.Correcto((await CargarAsync(s, guardado.Valor))!) : Resultado<Asiento>.Fallo(guardado.Errores);
        });

    public Task<Resultado<IReadOnlyList<int>>> GuardarManualesAsync(IReadOnlyList<Asiento> asientos, Actor actor, string motivo, OpcionesAltaManual? opciones = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (string.IsNullOrWhiteSpace(motivo)) return Resultado<IReadOnlyList<int>>.Fallo(SinMotivo("Asientos nuevos"));
            if (asientos.Count == 0)
                return Resultado<IReadOnlyList<int>>.Fallo(new Error("Asientos.SinAsientos", "No hay ningún asiento que guardar.", null, null, "Añade al menos un asiento."));
            foreach (var a in asientos)
            {
                if (a.EsAutomatico) return Resultado<IReadOnlyList<int>>.Fallo(AutomaticoNoManual(a));
                if (!a.EsNuevo)
                    return Resultado<IReadOnlyList<int>>.Fallo(new Error("Asientos.NoEsNuevo", "Esta operación solo da de alta asientos nuevos.", a.ToString(),
                        "El asiento ya existe.", "Para modificarlo, ábrelo y guárdalo con su motivo."));
            }
            var idLote = Guid.NewGuid();
            var texto = motivo.Trim();
            var ids = new List<int>();
            var errores = new List<Error>();
            foreach (var asiento in asientos)
            {
                var guardado = await GuardarInternoAsync(s, asiento, actor, texto, idLote, manual: true);
                if (guardado.EsCorrecto) ids.Add(guardado.Valor);
                else errores.AddRange(guardado.Errores);
            }
            if (errores.Count > 0) return Resultado<IReadOnlyList<int>>.Fallo(errores);
            if (opciones?.IdAsientoOrigen is int origen)
                foreach (var id in ids)
                    await s.EjecutarAsync("INSERT INTO RelacionesAsientos (IdAsientoOrigen, IdAsientoDestino, TipoRelacion, Motivo) VALUES (?, ?, ?, ?)",
                        origen, id, opciones.TipoRelacion, texto);
            if (opciones?.PuntearApuntes is { Count: > 0 } puntear)
            {
                var leidos = await LeerApuntesAsync(s, puntear);
                if (!leidos.EsCorrecto) return Resultado<IReadOnlyList<int>>.Fallo(leidos.Errores);
                foreach (var a in leidos.Valor.Where(a => !a.Punteado))
                {
                    await s.EjecutarAsync("UPDATE Apuntes SET Punteado = ? WHERE IdApunte = ?", true, a.IdApunte);
                    await RegistrarCambioAsync(s, idLote, actor, texto, a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "Punteado", "No", "Sí");
                }
            }
            return Resultado<IReadOnlyList<int>>.Correcto(ids);
        });

    public Task<Resultado> EliminarManualAsync(int idAsiento, int versionLeida, Actor actor, string motivo) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (string.IsNullOrWhiteSpace(motivo)) return Resultado.Fallo(SinMotivo($"Asiento {idAsiento}"));
            var actual = await CargarAsync(s, idAsiento);
            if (actual is null) return Resultado.Fallo(NoExiste(idAsiento));
            if (actual.EsAutomatico) return Resultado.Fallo(AutomaticoNoManual(actual));
            if (actual.Version != versionLeida) throw Conflicto(actual);
            var abierto = await ComprobarEjercicioAbiertoAsync(s, actual.Contexto);
            if (!abierto.EsCorrecto) return abierto;
            var trimestre = await ComprobarTrimestreAbiertoAsync(s, actual);
            if (!trimestre.EsCorrecto) return trimestre;
            await CopiarBorradoAsync(s, actual, actor, motivo.Trim());
            await BorrarAsync(s, actual, actor, motivo.Trim(), Guid.NewGuid());
            await ActualizarSaldosAsync(s, actual.Contexto, actual.Apuntes.Select(a => a.CodigoCuenta));
            return Resultado.Correcto();
        });

    public Task<Resultado<IReadOnlyList<int>>> ReemplazarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, IReadOnlyList<Asiento> asientos, Actor actor, string motivo,
        TrabajoLote? enLaTransaccion = null, OpcionesLote? opciones = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            var hecho = await ReemplazarEnSesionAsync(s, contexto, lote, asientos, actor, motivo, Guid.NewGuid(), enLaTransaccion, opciones);
            if (!hecho.EsCorrecto) return Resultado<IReadOnlyList<int>>.Fallo(hecho.Errores);
            if (!(opciones ?? OpcionesLote.Normal).DiferirSaldos) await ActualizarSaldosAsync(s, contexto, hecho.Valor.Cuentas);
            return Resultado<IReadOnlyList<int>>.Correcto(hecho.Valor.Nuevos);
        });

    public Task<Resultado<IReadOnlyList<IReadOnlyList<int>>>> ReemplazarLotesAsync(IReadOnlyList<LoteAsientos> lotes, Actor actor, string motivo,
        Func<ISesionDatos, Task<Resultado>>? alPrincipio = null, Func<ISesionDatos, Task<Resultado>>? alFinal = null, OpcionesLote? opciones = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            Resultado<IReadOnlyList<IReadOnlyList<int>>> Fallo(IReadOnlyList<Error> e) => Resultado<IReadOnlyList<IReadOnlyList<int>>>.Fallo(e);
            if (alPrincipio is not null && await alPrincipio(s) is { EsCorrecto: false } previo) return Fallo(previo.Errores);
            var idLote = Guid.NewGuid();
            var ids = new List<IReadOnlyList<int>>();
            var cuentas = new List<(ContextoSociedadEjercicio Contexto, List<string> Cuentas)>();
            foreach (var l in lotes)
            {
                var hecho = await ReemplazarEnSesionAsync(s, l.Contexto, l.Lote, l.Asientos, actor, motivo, idLote, null, opciones);
                if (!hecho.EsCorrecto) return Fallo(hecho.Errores);
                ids.Add(hecho.Valor.Nuevos);
                cuentas.Add((l.Contexto, hecho.Valor.Cuentas));
            }
            if (alFinal is not null && await alFinal(s) is { EsCorrecto: false } final) return Fallo(final.Errores);
            if (!(opciones ?? OpcionesLote.Normal).DiferirSaldos)
                foreach (var g in cuentas.GroupBy(c => c.Contexto))
                    await ActualizarSaldosAsync(s, g.Key, g.SelectMany(c => c.Cuentas));
            return Resultado<IReadOnlyList<IReadOnlyList<int>>>.Correcto(ids);
        });

    /// <summary>
    /// Sustituye un lote dentro de una transacción ya abierta: valida, reutiliza los asientos que ya tiene (cambian sus apuntes, no su
    /// número), borra los que sobran y crea los que faltan. Devuelve los asientos nuevos y las cuentas cuyo saldo hay que rehacer.
    /// </summary>
    private async Task<Resultado<(IReadOnlyList<int> Nuevos, List<string> Cuentas)>> ReemplazarEnSesionAsync(ISesionDatos s, ContextoSociedadEjercicio contexto,
        IdentidadLote lote, IReadOnlyList<Asiento> asientos, Actor actor, string motivo, Guid idLote, TrabajoLote? enLaTransaccion, OpcionesLote? opciones)
    {
        Resultado<(IReadOnlyList<int>, List<string>)> Fallo(IReadOnlyList<Error> e) => Resultado<(IReadOnlyList<int>, List<string>)>.Fallo(e);
        var op = opciones ?? OpcionesLote.Normal;
        var externo = op.LibroExterno;
        var diferir = op.DiferirSaldos;
        var error = ValidarLote(lote, motivo) ?? ValidarExterno(lote, externo) ?? ValidarLiquidacion(lote, opciones) ?? ValidarCierre(lote, opciones)
            ?? asientos.Where(a => a.Contexto != contexto).Select(a => new Error("Asientos.LoteOtroEjercicio",
                "Todos los asientos del lote deben ser del mismo ejercicio.", a.ToString(), "El lote es de un ejercicio.", "Genera un lote por ejercicio.")).FirstOrDefault();
        if (error is not null) return Fallo([error]);
        var abierto = await ComprobarEjercicioAbiertoAsync(s, contexto, externo);
        if (!abierto.EsCorrecto) return Fallo(abierto.Errores);

        var anteriores = await IdsDelLoteAsync(s, contexto, lote);
        if (enLaTransaccion?.Antes is { } antes) await antes(s, anteriores);
        // Los asientos que ya tiene el lote se reutilizan en orden (cambian sus apuntes, no su número), y solo se
        // borran los que sobran o se crean los que faltan (contabilidad/asientos 2026-11-07).
        var cuentas = new List<string>();
        var reutilizados = new List<Asiento>();
        foreach (var (id, i) in anteriores.Select((id, i) => (id, i)))
        {
            var viejo = (await CargarAsync(s, id))!;
            cuentas.AddRange(viejo.Apuntes.Select(a => a.CodigoCuenta));
            // El que se reutiliza lo comprueba GuardarInterno comparándolo con el nuevo (un cambio sin efecto fiscal se admite).
            if (i < asientos.Count)
            {
                reutilizados.Add(viejo);
                continue;
            }
            var trimestre = op.ExentoTrimestreCerrado(viejo) ? Resultado.Correcto() : await ComprobarTrimestreAbiertoAsync(s, viejo);
            if (!trimestre.EsCorrecto) return Fallo(trimestre.Errores);
            await BorrarAsync(s, viejo, actor, motivo.Trim(), idLote);
        }
        var nuevos = new List<int>();
        var errores = new List<Error>();
        for (var i = 0; i < asientos.Count; i++)
        {
            var asiento = asientos[i];
            asiento.IdAsiento = i < reutilizados.Count ? reutilizados[i].IdAsiento : 0;
            if (i < reutilizados.Count) asiento.Version = reutilizados[i].Version;
            asiento.Lote = lote;
            var guardado = await GuardarInternoAsync(s, asiento, actor, motivo.Trim(), idLote, manual: false, externo, diferir,
                trimestreExento: op.ExentoTrimestreCerrado(asiento) && (i >= reutilizados.Count || op.ExentoTrimestreCerrado(reutilizados[i])));
            if (guardado.EsCorrecto) nuevos.Add(guardado.Valor);
            else errores.AddRange(guardado.Errores);
        }
        if (errores.Count > 0) return Fallo(errores);
        if (enLaTransaccion?.Despues is { } despues)
        {
            var hecho = await despues(s, nuevos);
            if (!hecho.EsCorrecto) return Fallo(hecho.Errores);
        }
        return Resultado<(IReadOnlyList<int>, List<string>)>.Correcto((nuevos, cuentas));
    }

    public Task<Resultado<int>> EliminarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, Actor actor, string motivo, TrabajoLote? enLaTransaccion = null,
        OpcionesLote? opciones = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            var externo = (opciones ?? OpcionesLote.Normal).LibroExterno;
            var error = ValidarLote(lote, motivo) ?? ValidarExterno(lote, externo) ?? ValidarLiquidacion(lote, opciones) ?? ValidarCierre(lote, opciones);
            if (error is not null) return Resultado<int>.Fallo(error);
            var abierto = await ComprobarEjercicioAbiertoAsync(s, contexto, externo);
            if (!abierto.EsCorrecto) return Resultado<int>.Fallo(abierto.Errores);
            var idLote = Guid.NewGuid();
            var ids = await IdsDelLoteAsync(s, contexto, lote);
            if (enLaTransaccion?.Antes is { } antes) await antes(s, ids);
            var cuentas = new List<string>();
            foreach (var id in ids)
            {
                var viejo = (await CargarAsync(s, id))!;
                var trimestre = (opciones ?? OpcionesLote.Normal).ExentoTrimestreCerrado(viejo) ? Resultado.Correcto() : await ComprobarTrimestreAbiertoAsync(s, viejo);
                if (!trimestre.EsCorrecto) return Resultado<int>.Fallo(trimestre.Errores);
                cuentas.AddRange(viejo.Apuntes.Select(a => a.CodigoCuenta));
                await BorrarAsync(s, viejo, actor, motivo.Trim(), idLote);
            }
            if (enLaTransaccion?.Despues is { } despues)
            {
                var hecho = await despues(s, []);
                if (!hecho.EsCorrecto) return Resultado<int>.Fallo(hecho.Errores);
            }
            if (!(opciones ?? OpcionesLote.Normal).DiferirSaldos) await ActualizarSaldosAsync(s, contexto, cuentas);
            return Resultado<int>.Correcto(ids.Count);
        });

    public Task<Resultado<Guid>> CambiarCuentaApuntesAsync(ContextoSociedadEjercicio contexto, IReadOnlyCollection<int> idsApunte, string nuevaCuenta, Actor actor, string motivo) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (string.IsNullOrWhiteSpace(motivo)) return Resultado<Guid>.Fallo(SinMotivo("Cambio de cuenta"));
            var abierto = await ComprobarEjercicioAbiertoAsync(s, contexto);
            if (!abierto.EsCorrecto) return Resultado<Guid>.Fallo(abierto.Errores);
            var cuenta = await ResolverCuentaAsync(s, contexto, nuevaCuenta);
            if (!cuenta.EsCorrecto) return Resultado<Guid>.Fallo(cuenta.Errores);

            var idLote = Guid.NewGuid();
            var afectadas = new HashSet<string>(StringComparer.Ordinal) { cuenta.Valor.Codigo };
            var anteriores = new Dictionary<int, Asiento>();
            var libroIva = new Dictionary<int, bool>();
            foreach (var idApunte in idsApunte.Distinct())
            {
                var fila = await s.ConsultarPrimeroAsync(
                    "SELECT ap.IdAsiento, ap.Orden, ap.CuentaContable, a.IdSociedadEjercicio, a.EsAutomatico FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE ap.IdApunte = ?",
                    f => (IdAsiento: f.Entero("IdAsiento"), Orden: f.Entero("Orden"), Cuenta: f.Texto("CuentaContable") ?? "", Ejercicio: f.Entero("IdSociedadEjercicio"), Automatico: f.Logico("EsAutomatico")),
                    idApunte);
                if (fila.IdAsiento == 0 || fila.Ejercicio != contexto.IdSociedadEjercicio)
                    return Resultado<Guid>.Fallo(new Error("Asientos.ApunteNoEncontrado", $"El apunte {idApunte} no existe en este ejercicio.", $"Apunte {idApunte}",
                        "Se ha borrado o es de otro ejercicio.", "Vuelve a cargar los apuntes."));
                if (fila.Cuenta == cuenta.Valor.Codigo) continue;
                if (fila.Automatico)
                {
                    // Corrección de un automático (contabilidad/diario 2026-11-14 21:50): ni la 572 ni, con libro de IVA, las líneas de IVA.
                    if (!libroIva.TryGetValue(fila.IdAsiento, out var conLibro)) libroIva[fila.IdAsiento] = conLibro = await TieneLibroIvaAsync(s, fila.IdAsiento);
                    var bloqueo = ReglasAsiento.BloqueoCuentaAutomatico(fila.Cuenta, conLibro) ?? ReglasAsiento.BloqueoCuentaAutomatico(cuenta.Valor.Codigo, conLibro);
                    if (bloqueo is not null)
                        return Resultado<Guid>.Fallo(new Error("Asientos.ApunteBloqueado", $"El apunte {fila.Orden} del asiento {fila.IdAsiento} no se puede pasar de {fila.Cuenta} a {cuenta.Valor.Codigo}.",
                            $"Asiento {fila.IdAsiento}", bloqueo, "Cambia solo los apuntes que no son de banco ni de IVA; el resto se corrige en su origen."));
                }
                if (!anteriores.ContainsKey(fila.IdAsiento))
                {
                    var asiento = (await CargarAsync(s, fila.IdAsiento))!;
                    anteriores[fila.IdAsiento] = asiento;
                }
                // En un trimestre cerrado solo se admite pasar a otra subcuenta del mismo grupo 4XX de terceros si el asiento no lleva
                // la 4751 (contabilidad/asientos 2026-11-13 10:50).
                var deTercero = ReglasAsiento.EsCambioDeSubcuentaDeTercero(fila.Cuenta, cuenta.Valor.Codigo)
                    && !anteriores[fila.IdAsiento].Apuntes.Any(a => a.CodigoCuenta.Trim().StartsWith(ReglasAsiento.PrefijoRetenciones, StringComparison.Ordinal));
                if (!deTercero)
                {
                    var trimestre = await ComprobarTrimestreAbiertoAsync(s, anteriores[fila.IdAsiento], cuenta.Valor.Codigo);
                    if (!trimestre.EsCorrecto) return Resultado<Guid>.Fallo(trimestre.Errores);
                }
                await s.EjecutarAsync("UPDATE Apuntes SET CuentaContable = ? WHERE IdApunte = ?", cuenta.Valor.Codigo, idApunte);
                await RegistrarCambioAsync(s, idLote, actor, motivo.Trim(), fila.IdAsiento, idApunte, fila.Orden, "MODIFICACION", "CuentaContable", fila.Cuenta, cuenta.Valor.Codigo);
                afectadas.Add(fila.Cuenta);
            }
            foreach (var (idAsiento, anterior) in anteriores)
            {
                await s.EjecutarAsync("UPDATE Asientos SET Version = Version + 1, OperadorModificacion = ?, FechaHoraModificacion = ?, CorregidoAMano = ? WHERE IdAsiento = ?",
                    actor.ToString(), _reloj.Ahora, anterior.EsAutomatico, idAsiento);
                await RevisionAsync(s, idAsiento, OperacionRevision.Modificacion, motivo.Trim(), actor, anterior, (await CargarAsync(s, idAsiento))!);
            }
            await ActualizarSaldosAsync(s, contexto, afectadas);
            return Resultado<Guid>.Correcto(idLote);
        });

    /// <summary>Lo que hace falta de un apunte para las operaciones del Mayor.</summary>
    private sealed record ApunteOperacion(int IdApunte, int IdAsiento, int Orden, string? Descripcion, string? Documento, bool Punteado, bool Automatico,
        DateTime? Vencimiento = null, int? IdCentroCoste = null);

    private static async Task<Resultado<IReadOnlyList<ApunteOperacion>>> LeerApuntesAsync(ISesionDatos s, IReadOnlyCollection<int> idsApunte)
    {
        var lista = new List<ApunteOperacion>();
        foreach (var idApunte in idsApunte.Distinct())
        {
            var fila = await s.ConsultarPrimeroAsync(
                "SELECT ap.IdApunte, ap.IdAsiento, ap.Orden, ap.Descripcion, ap.NumeroDocumento, ap.Punteado, ap.FechaVencimiento, ap.IdCentroCoste, a.EsAutomatico FROM Apuntes AS ap " +
                "INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE ap.IdApunte = ?",
                f => new ApunteOperacion(f.Entero("IdApunte"), f.Entero("IdAsiento"), f.Entero("Orden"), f.Texto("Descripcion"), f.Texto("NumeroDocumento"),
                    !f.EsNulo("Punteado") && f.Logico("Punteado"), !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico"), f.FechaNula("FechaVencimiento"), f.EnteroNulo("IdCentroCoste")),
                idApunte);
            if (fila is null)
                return Resultado<IReadOnlyList<ApunteOperacion>>.Fallo(new Error("Asientos.ApunteNoEncontrado", $"El apunte {idApunte} ya no existe.", $"Apunte {idApunte}",
                    "Se ha borrado, o su asiento se ha regenerado después de cargar la pantalla.", "Vuelve a cargar los apuntes y repite la operación."));
            lista.Add(fila);
        }
        return lista.Count == 0
            ? Resultado<IReadOnlyList<ApunteOperacion>>.Fallo(new Error("Asientos.SinApuntes", "No hay ningún apunte elegido.", "Apuntes", null, "Elige una o varias filas."))
            : Resultado<IReadOnlyList<ApunteOperacion>>.Correcto(lista);
    }

    public Task<Resultado<Guid>> PuntearApuntesAsync(IReadOnlyCollection<int> idsApunte, bool punteado, Actor actor, string? motivo = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            var leidos = await LeerApuntesAsync(s, idsApunte);
            if (!leidos.EsCorrecto) return Resultado<Guid>.Fallo(leidos.Errores);
            var idLote = Guid.NewGuid();
            var texto = string.IsNullOrWhiteSpace(motivo) ? (punteado ? "Punteo en el Mayor" : "Despunteo en el Mayor") : motivo.Trim();
            foreach (var a in leidos.Valor.Where(a => a.Punteado != punteado))
            {
                await s.EjecutarAsync("UPDATE Apuntes SET Punteado = ? WHERE IdApunte = ?", punteado, a.IdApunte);
                await RegistrarCambioAsync(s, idLote, actor, texto, a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "Punteado", a.Punteado ? "Sí" : "No", punteado ? "Sí" : "No");
            }
            return Resultado<Guid>.Correcto(idLote);
        });

    public Task<Resultado<Guid>> AsignarNumeroDocumentoAsync(IReadOnlyCollection<int> idsApunte, string? numero, Actor actor, string motivo) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (string.IsNullOrWhiteSpace(motivo)) return Resultado<Guid>.Fallo(SinMotivo("Número de factura"));
            var nuevo = string.IsNullOrWhiteSpace(numero) ? null : numero.Trim();
            if (nuevo is { Length: > 100 })
                return Resultado<Guid>.Fallo(new Error("Asientos.DocumentoLargo", "El número de factura tiene más de 100 caracteres.", nuevo[..20] + "…",
                    "El documento del apunte admite hasta 100 caracteres.", "Acórtalo: basta con el número de la factura."));
            var leidos = await LeerApuntesAsync(s, idsApunte);
            if (!leidos.EsCorrecto) return Resultado<Guid>.Fallo(leidos.Errores);
            var idLote = Guid.NewGuid();
            var cambiados = new Dictionary<int, Asiento>();
            foreach (var a in leidos.Valor.Where(a => !string.Equals(a.Documento?.Trim(), nuevo, StringComparison.Ordinal)))
            {
                if (!cambiados.ContainsKey(a.IdAsiento)) cambiados[a.IdAsiento] = (await CargarAsync(s, a.IdAsiento))!;
                await s.EjecutarAsync("UPDATE Apuntes SET NumeroDocumento = ? WHERE IdApunte = ?", nuevo, a.IdApunte);
                await RegistrarCambioAsync(s, idLote, actor, motivo.Trim(), a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "NumeroDocumento", a.Documento, nuevo);
            }
            await CerrarModificacionAsync(s, cambiados, actor, motivo.Trim());
            return Resultado<Guid>.Correcto(idLote);
        });

    public Task<Resultado<Guid>> CambiarConceptoApuntesAsync(IReadOnlyCollection<int> idsApunte, string? concepto, Actor actor, string motivo) =>
        CambiarDatosApuntesAsync(idsApunte, new CambioDatosApunte { CambiaConcepto = true, Concepto = concepto }, actor, motivo);

    public Task<Resultado<Guid>> CambiarDatosApuntesAsync(IReadOnlyCollection<int> idsApunte, CambioDatosApunte cambio, Actor actor, string? motivo = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (cambio.Vacio)
                return Resultado<Guid>.Fallo(new Error("Asientos.SinCambios", "No hay ningún cambio que guardar.", "Apuntes", null, "Cambia algún dato del apunte."));
            if (!cambio.SoloPunteo && string.IsNullOrWhiteSpace(motivo)) return Resultado<Guid>.Fallo(SinMotivo("Cambio de datos del apunte"));
            var concepto = string.IsNullOrWhiteSpace(cambio.Concepto) ? null : cambio.Concepto.Trim();
            if (cambio.CambiaConcepto && concepto is { Length: > 255 })
                return Resultado<Guid>.Fallo(new Error("Asientos.ConceptoLargo", "El concepto tiene más de 255 caracteres.", concepto[..30] + "…",
                    "El concepto del apunte admite hasta 255 caracteres.", "Acórtalo."));
            var documento = string.IsNullOrWhiteSpace(cambio.Documento) ? null : cambio.Documento.Trim();
            if (cambio.CambiaDocumento && documento is { Length: > 100 })
                return Resultado<Guid>.Fallo(new Error("Asientos.DocumentoLargo", "El número de factura tiene más de 100 caracteres.", documento[..20] + "…",
                    "El documento del apunte admite hasta 100 caracteres.", "Acórtalo: basta con el número de la factura."));
            var vencimiento = cambio.Vencimiento?.Date;
            var leidos = await LeerApuntesAsync(s, idsApunte);
            if (!leidos.EsCorrecto) return Resultado<Guid>.Fallo(leidos.Errores);

            (string? Sociedad, bool Activo, bool Imputable) centro = default;
            if (cambio.CambiaCentro && cambio.IdCentroCoste is int idCentro)
                centro = await s.ConsultarPrimeroAsync("SELECT IdSociedad, EstaActivo, EsImputable FROM CentrosCoste WHERE IdCentroCoste = ?",
                    f => ((string?)f.Texto("IdSociedad"), f.Logico("EstaActivo"), f.Logico("EsImputable")), idCentro);

            var idLote = Guid.NewGuid();
            var texto = string.IsNullOrWhiteSpace(motivo) ? (cambio.Punteado == true ? "Punteo" : "Despunteo") : motivo.Trim();
            var cambiados = new Dictionary<int, Asiento>();
            var revisados = new Dictionary<int, Asiento>();
            static string? Dia(DateTime? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var a in leidos.Valor)
            {
                var conceptoCambia = cambio.CambiaConcepto && !string.Equals(a.Descripcion?.Trim(), concepto, StringComparison.Ordinal);
                var documentoCambia = cambio.CambiaDocumento && !string.Equals(a.Documento?.Trim(), documento, StringComparison.Ordinal);
                var vencimientoCambia = cambio.CambiaVencimiento && a.Vencimiento?.Date != vencimiento;
                var centroCambia = cambio.CambiaCentro && a.IdCentroCoste != cambio.IdCentroCoste;
                var punteoCambia = cambio.Punteado is bool p && p != a.Punteado;
                if (conceptoCambia || documentoCambia || vencimientoCambia || centroCambia)
                {
                    if (!revisados.TryGetValue(a.IdAsiento, out var asiento))
                    {
                        asiento = (await CargarAsync(s, a.IdAsiento))!;
                        revisados[a.IdAsiento] = asiento;
                    }
                    if (conceptoCambia || vencimientoCambia || centroCambia)
                    {
                        // Sin efecto fiscal: también en un trimestre cerrado (contabilidad/asientos 2026-11-14 23:30); nunca en un ejercicio cerrado.
                        var abierto = await ComprobarEjercicioAbiertoAsync(s, asiento.Contexto);
                        if (!abierto.EsCorrecto) return Resultado<Guid>.Fallo(abierto.Errores);
                    }
                    if (centroCambia && cambio.IdCentroCoste is not null
                        && (!string.Equals(centro.Sociedad, asiento.Contexto.IdSociedad, StringComparison.OrdinalIgnoreCase) || !centro.Activo || !centro.Imputable))
                        return Resultado<Guid>.Fallo(new Error("Asientos.CentroNoValido", $"El centro de coste elegido no admite apuntes.", $"Asiento {a.IdAsiento}, apunte {a.Orden}",
                            "No existe en esta sociedad, está inactivo o agrupa otros centros.", "Elige un centro de coste activo e imputable."));
                    cambiados[a.IdAsiento] = asiento;
                }
                if (conceptoCambia)
                {
                    await s.EjecutarAsync("UPDATE Apuntes SET Descripcion = ? WHERE IdApunte = ?", concepto, a.IdApunte);
                    await RegistrarCambioAsync(s, idLote, actor, texto, a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "Descripcion", a.Descripcion, concepto);
                }
                if (documentoCambia)
                {
                    await s.EjecutarAsync("UPDATE Apuntes SET NumeroDocumento = ? WHERE IdApunte = ?", documento, a.IdApunte);
                    await RegistrarCambioAsync(s, idLote, actor, texto, a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "NumeroDocumento", a.Documento, documento);
                }
                if (vencimientoCambia)
                {
                    await s.EjecutarAsync("UPDATE Apuntes SET FechaVencimiento = ? WHERE IdApunte = ?", vencimiento, a.IdApunte);
                    await RegistrarCambioAsync(s, idLote, actor, texto, a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "FechaVencimiento", Dia(a.Vencimiento), Dia(vencimiento));
                }
                if (centroCambia)
                {
                    await s.EjecutarAsync("UPDATE Apuntes SET IdCentroCoste = ? WHERE IdApunte = ?", cambio.IdCentroCoste, a.IdApunte);
                    await RegistrarCambioAsync(s, idLote, actor, texto, a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "IdCentroCoste",
                        a.IdCentroCoste?.ToString(CultureInfo.InvariantCulture), cambio.IdCentroCoste?.ToString(CultureInfo.InvariantCulture));
                }
                if (punteoCambia)
                {
                    await s.EjecutarAsync("UPDATE Apuntes SET Punteado = ? WHERE IdApunte = ?", cambio.Punteado!.Value, a.IdApunte);
                    await RegistrarCambioAsync(s, idLote, actor, texto, a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "Punteado", a.Punteado ? "Sí" : "No", cambio.Punteado.Value ? "Sí" : "No");
                }
            }
            await CerrarModificacionAsync(s, cambiados, actor, texto);
            return Resultado<Guid>.Correcto(idLote);
        });

    public Task<Resultado<Asiento>> CorregirAutomaticoAsync(Asiento asiento, Actor actor, string motivo) =>
        bd.EnTransaccionAsync(async s =>
        {
            if (string.IsNullOrWhiteSpace(motivo)) return Resultado<Asiento>.Fallo(SinMotivo(asiento.ToString()));
            if (asiento.EsNuevo) return Resultado<Asiento>.Fallo(NoExiste(0));
            var anterior = await CargarAsync(s, asiento.IdAsiento);
            if (anterior is null) return Resultado<Asiento>.Fallo(NoExiste(asiento.IdAsiento));
            if (!anterior.EsAutomatico)
                return Resultado<Asiento>.Fallo(new Error("Asientos.NoEsAutomatico", "Este asiento es manual: se modifica como cualquier asiento manual.", anterior.ToString(),
                    "La corrección a mano es solo para los asientos automáticos.", "Ábrelo con «Modificar el asiento…»."));
            asiento.Lote = anterior.Lote;
            var reglas = ReglasAsiento.ValidarCorreccionAutomatico(anterior, asiento, await TieneLibroIvaAsync(s, asiento.IdAsiento));
            if (!reglas.EsCorrecto) return Resultado<Asiento>.Fallo(reglas.Errores);
            var guardado = await GuardarInternoAsync(s, asiento, actor, motivo.Trim(), Guid.NewGuid(), manual: true, correccion: true);
            return guardado.EsCorrecto ? Resultado<Asiento>.Correcto((await CargarAsync(s, guardado.Valor))!) : Resultado<Asiento>.Fallo(guardado.Errores);
        });

    public Task<bool> TieneLibroIvaAsync(int idAsiento) => bd.EjecutarAsync(s => TieneLibroIvaAsync(s, idAsiento));

    private static async Task<bool> TieneLibroIvaAsync(ISesionDatos s, int idAsiento)
    {
        foreach (var tabla in new[] { "IVARepercutido", "IVASoportado" })
            if (Convert.ToInt32(await s.EscalarAsync($"SELECT COUNT(*) FROM {tabla} WHERE IdAsiento = ?", idAsiento), CultureInfo.InvariantCulture) > 0) return true;
        return false;
    }

    /// <summary>Copia el asiento y sus apuntes a AsientosBorrados y ApuntesBorrados antes de borrarlo (contabilidad/diario 2026-11-14 21:50).</summary>
    private async Task CopiarBorradoAsync(ISesionDatos s, Asiento asiento, Actor actor, string motivo)
    {
        var alta = await s.ConsultarPrimeroAsync(
            "SELECT OperadorCreacion, FechaHoraCreacion, OperadorModificacion, FechaHoraModificacion FROM Asientos WHERE IdAsiento = ?",
            f => (OpC: f.Texto("OperadorCreacion"), FC: f.FechaNula("FechaHoraCreacion"), OpM: f.Texto("OperadorModificacion"), FM: f.FechaNula("FechaHoraModificacion")),
            asiento.IdAsiento);
        await s.EjecutarAsync(
            "INSERT INTO AsientosBorrados (IdAsiento, IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, Notas, OperadorCreacion, FechaHoraCreacion, " +
            "OperadorModificacion, FechaHoraModificacion, TotalDebe, TotalHaber, Version, IdPlantillaAsiento, FechaHoraBorrado, OperadorBorrado, MotivoBorrado) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            asiento.IdAsiento, asiento.Contexto.IdSociedadEjercicio, asiento.FechaContable, asiento.Descripcion, (byte)asiento.Tipo, asiento.Notas, alta.OpC, alta.FC,
            alta.OpM, alta.FM, asiento.TotalDebe, asiento.TotalHaber, asiento.Version, asiento.IdPlantilla, _reloj.Ahora, actor.ToString(), motivo);
        var id = await s.UltimoIdAsync();
        foreach (var a in asiento.Apuntes)
            await s.EjecutarAsync(
                "INSERT INTO ApuntesBorrados (IdAsientoBorrado, IdApunte, IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, Descripcion, NumeroDocumento, " +
                "IdCliente, IdProveedor, FechaVencimiento, IdCentroCoste, TipoApunte, Punteado) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                id, a.IdApunte, asiento.IdAsiento, (short)a.Orden, a.CodigoCuenta, a.Debe.EsCero ? null : a.Debe, a.Haber.EsCero ? null : a.Haber, a.Descripcion, a.NumeroDocumento,
                a.IdCliente, a.IdProveedor, a.FechaVencimiento, a.IdCentroCoste, (byte)a.Tipo, a.Punteado);
    }

    public Task<IReadOnlyList<AsientoBorrado>> AsientosBorradosAsync(IReadOnlyCollection<int> idsSociedadEjercicio) =>
        bd.EjecutarAsync(async s =>
        {
            if (idsSociedadEjercicio.Count == 0) return (IReadOnlyList<AsientoBorrado>)[];
            var en = string.Join(", ", idsSociedadEjercicio.Distinct().Select(i => i.ToString(CultureInfo.InvariantCulture)));
            var apuntes = (await s.ConsultarAsync(
                "SELECT p.IdAsientoBorrado, p.IdApunte, p.Orden, p.CuentaContable, p.ImporteDebe, p.ImporteHaber, p.Descripcion, p.NumeroDocumento, p.FechaVencimiento, p.IdCentroCoste, p.Punteado " +
                $"FROM ApuntesBorrados AS p INNER JOIN AsientosBorrados AS b ON p.IdAsientoBorrado = b.IdAsientoBorrado WHERE b.IdSociedadEjercicio IN ({en})",
                f => (Id: f.Entero("IdAsientoBorrado"), Apunte: new ApunteBorrado(f.Entero("IdApunte"), f.Entero("Orden"), (f.Texto("CuentaContable") ?? "").Trim(),
                    f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m, f.Texto("Descripcion"), f.Texto("NumeroDocumento"),
                    f.FechaNula("FechaVencimiento"), f.EnteroNulo("IdCentroCoste"), !f.EsNulo("Punteado") && f.Logico("Punteado")))))
                .ToLookup(x => x.Id, x => x.Apunte);
            var borrados = await s.ConsultarAsync(
                "SELECT IdAsientoBorrado, IdAsiento, IdSociedadEjercicio, FechaContable, Descripcion, Notas, TotalDebe, FechaHoraBorrado, OperadorBorrado, MotivoBorrado, " +
                $"OperadorCreacion, FechaHoraCreacion FROM AsientosBorrados WHERE IdSociedadEjercicio IN ({en})",
                f => new AsientoBorrado(f.Entero("IdAsiento"), f.Entero("IdSociedadEjercicio"), f.Fecha("FechaContable").Date, f.Texto("Descripcion") ?? "", f.Texto("Notas"),
                    f.DecimalNulo("TotalDebe") ?? 0m, f.Fecha("FechaHoraBorrado"), f.Texto("OperadorBorrado") ?? "", f.Texto("MotivoBorrado") ?? "",
                    f.Texto("OperadorCreacion"), f.FechaNula("FechaHoraCreacion"), apuntes[f.Entero("IdAsientoBorrado")].OrderBy(a => a.Orden).ToList()));
            return (IReadOnlyList<AsientoBorrado>)borrados.OrderByDescending(b => b.FechaHoraBorrado).ThenByDescending(b => b.IdAsiento).ToList();
        });

    /// <summary>Tras cambiar apuntes sueltos: nueva versión, autor y fecha del cambio y revisión de cada asiento tocado.</summary>
    private async Task CerrarModificacionAsync(ISesionDatos s, IReadOnlyDictionary<int, Asiento> anteriores, Actor actor, string motivo)
    {
        foreach (var (idAsiento, anterior) in anteriores)
        {
            await s.EjecutarAsync("UPDATE Asientos SET Version = Version + 1, OperadorModificacion = ?, FechaHoraModificacion = ? WHERE IdAsiento = ?",
                actor.ToString(), _reloj.Ahora, idAsiento);
            await RevisionAsync(s, idAsiento, OperacionRevision.Modificacion, motivo, actor, anterior, (await CargarAsync(s, idAsiento))!);
        }
    }

    /// <summary>
    /// Al guardar o regenerar un asiento, lo que se hizo desde el Mayor se conserva en el apunte que sigue en el mismo orden con la
    /// misma cuenta e importe: el punteo siempre y, en los lotes (<paramref name="numeroDocumento"/>), el número de factura si el
    /// proceso no trae otro (contabilidad/mayor 2026-11-11 18:30). Si el apunte cambia, el punteo se pierde y lo dice el registro.
    /// </summary>
    internal static void ConservarDelMayor(Asiento anterior, Asiento nuevo, bool numeroDocumento)
    {
        var viejos = anterior.Apuntes.GroupBy(a => a.Orden).ToDictionary(g => g.Key, g => g.First());
        foreach (var a in nuevo.Apuntes)
        {
            if (!viejos.TryGetValue(a.Orden, out var v) || !string.Equals(v.CodigoCuenta.Trim(), a.CodigoCuenta.Trim(), StringComparison.Ordinal)
                || v.Debe != a.Debe || v.Haber != a.Haber) continue;
            if (v.Punteado) a.Punteado = true;
            if (numeroDocumento && string.IsNullOrWhiteSpace(a.NumeroDocumento) && !string.IsNullOrWhiteSpace(v.NumeroDocumento)) a.NumeroDocumento = v.NumeroDocumento;
        }
    }

    public Task<IReadOnlyList<CambioApunte>> RegistroCambiosAsync(int idAsiento) =>
        bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdLote, FechaHora, Operador, Motivo, IdAsiento, IdApunte, Orden, Operacion, Campo, ValorAntiguo, ValorNuevo FROM RegistroCambiosApuntes WHERE IdAsiento = ? ORDER BY IdCambioApunte",
            f => new CambioApunte(Guid.Parse(f.Texto("IdLote")!), f.Fecha("FechaHora"), f.Texto("Operador") ?? "", f.Texto("Motivo") ?? "", f.Entero("IdAsiento"),
                f.EnteroNulo("IdApunte"), f.EnteroNulo("Orden"), f.Texto("Operacion") ?? "", f.Texto("Campo") ?? "", f.Texto("ValorAntiguo"), f.Texto("ValorNuevo")),
            idAsiento));

    /// <summary>Valida y guarda un asiento dentro de la transacción en curso. Devuelve su IdAsiento.</summary>
    private async Task<Resultado<int>> GuardarInternoAsync(ISesionDatos s, Asiento asiento, Actor actor, string motivo, Guid idLote, bool manual, bool externo = false, bool diferirSaldos = false,
        bool correccion = false, bool trimestreExento = false)
    {
        var reglas = ReglasAsiento.ValidarYCuadrar(asiento, _politica);
        if (!reglas.EsCorrecto) return Resultado<int>.Fallo(reglas.Errores);
        var abierto = await ComprobarEjercicioAbiertoAsync(s, asiento.Contexto, externo);
        if (!abierto.EsCorrecto) return Resultado<int>.Fallo(abierto.Errores);

        var errores = new List<Error>();
        foreach (var apunte in asiento.Apuntes)
        {
            var cuenta = await ResolverCuentaAsync(s, asiento.Contexto, apunte.CodigoCuenta);
            if (!cuenta.EsCorrecto) { errores.AddRange(cuenta.Errores.Select(e => e with { RegistroAfectado = $"{asiento}, apunte {apunte.Orden}" })); continue; }
            apunte.CodigoCuenta = cuenta.Valor.Codigo;
            if (apunte.IdCentroCoste is int centro)
            {
                var fila = await s.ConsultarPrimeroAsync("SELECT IdSociedad, EstaActivo, EsImputable FROM CentrosCoste WHERE IdCentroCoste = ?",
                    f => (Sociedad: f.Texto("IdSociedad"), Activo: f.Logico("EstaActivo"), Imputable: f.Logico("EsImputable")), centro);
                if (!string.Equals(fila.Sociedad, asiento.Contexto.IdSociedad, StringComparison.OrdinalIgnoreCase) || !fila.Activo || !fila.Imputable)
                    errores.Add(CentroNoValido(asiento, apunte));
            }
        }
        if (errores.Count > 0) return Resultado<int>.Fallo(errores);

        Asiento? anterior = null;
        if (!asiento.EsNuevo)
        {
            anterior = await CargarAsync(s, asiento.IdAsiento);
            if (anterior is null) return Resultado<int>.Fallo(NoExiste(asiento.IdAsiento));
            if (manual && anterior.EsAutomatico && !correccion) return Resultado<int>.Fallo(AutomaticoNoManual(anterior));
            if (anterior.Contexto.IdSociedadEjercicio != asiento.Contexto.IdSociedadEjercicio)
                return Resultado<int>.Fallo(new Error("Asientos.CambioEjercicio", "Un asiento no se puede mover a otro ejercicio.", asiento.ToString(),
                    "El ejercicio forma parte de la identidad del asiento.", "Bórralo y créalo en el ejercicio correcto, o usa la plantilla de rectificación."));
        }
        // Trimestre cerrado (contabilidad/asientos 2026-11-13 10:50): se comprueban el asiento anterior y el nuevo, salvo que el cambio no
        // tenga efecto fiscal (solo textos, documento o la subcuenta de un tercero del mismo grupo 4XX).
        if (!externo && !trimestreExento && (anterior is null || !ReglasAsiento.EsCambioSinEfectoFiscal(anterior, asiento)))
        {
            var trimestre = await ComprobarTrimestreAbiertoAsync(s, asiento);
            if (!trimestre.EsCorrecto) return Resultado<int>.Fallo(trimestre.Errores);
            if (anterior is not null)
            {
                var trimestreAnterior = await ComprobarTrimestreAbiertoAsync(s, anterior);
                if (!trimestreAnterior.EsCorrecto) return Resultado<int>.Fallo(trimestreAnterior.Errores);
            }
        }

        var ahora = _reloj.Ahora;
        int id;
        if (anterior is null)
        {
            await s.EjecutarAsync(
                "INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, Notas, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, " +
                "OperadorContabilizacion, FechaHoraContabilizacion, TotalDebe, TotalHaber, Version, EsAutomatico, MotorPropietario, CodigoProceso, ClaveEjecucion, IdPlantillaAsiento) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                asiento.Contexto.IdSociedadEjercicio, asiento.FechaContable, asiento.Descripcion.Trim(), (byte)asiento.Tipo, asiento.Notas, (byte)EstadoAsiento.Contabilizado,
                actor.ToString(), ahora, actor.ToString(), ahora, asiento.TotalDebe, asiento.TotalHaber, 1, asiento.EsAutomatico,
                asiento.Lote?.MotorPropietario, asiento.Lote?.CodigoProceso, asiento.Lote?.ClaveEjecucion, asiento.IdPlantilla);
            id = await s.UltimoIdAsync();
        }
        else
        {
            id = asiento.IdAsiento;
            await s.ActualizarConVersionAsync(
                "UPDATE Asientos SET FechaContable = ?, Descripcion = ?, TipoAsiento = ?, Notas = ?, OperadorModificacion = ?, FechaHoraModificacion = ?, " +
                "TotalDebe = ?, TotalHaber = ?, CorregidoAMano = ?, Version = Version + 1 WHERE IdAsiento = ? AND Version = ?",
                asiento.ToString(),
                asiento.FechaContable, asiento.Descripcion.Trim(), (byte)asiento.Tipo, asiento.Notas, actor.ToString(), ahora,
                // La marca solo la pone la corrección a mano de un automático; regenerar el lote la quita (contabilidad/diario 2026-11-14).
                asiento.TotalDebe, asiento.TotalHaber, correccion, id, asiento.Version);
            await s.EjecutarAsync("DELETE FROM Apuntes WHERE IdAsiento = ?", id);
        }

        if (anterior is not null) ConservarDelMayor(anterior, asiento, numeroDocumento: !manual);
        foreach (var a in asiento.Apuntes)
            await s.EjecutarAsync(
                $"INSERT INTO Apuntes ({ColumnasApunte.Replace("IdApunte, ", "")}) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                id, (short)a.Orden, a.CodigoCuenta, a.Debe.EsCero ? null : a.Debe, a.Haber.EsCero ? null : a.Haber, a.Descripcion, a.NumeroDocumento,
                a.IdCliente, a.IdProveedor, a.FechaVencimiento, a.IdCentroCoste, (byte)a.Tipo, a.GeneradoAutomaticamente, a.MotivoGeneracion, a.Punteado);

        await RegistrarDiferenciasAsync(s, idLote, actor, motivo, id, anterior, asiento);
        await RevisionAsync(s, id, anterior is null ? (manual ? OperacionRevision.Alta : OperacionRevision.Lote) : OperacionRevision.Modificacion, motivo, actor,
            anterior, (await CargarAsync(s, id))!);
        if (!diferirSaldos)
            await ActualizarSaldosAsync(s, asiento.Contexto, asiento.Apuntes.Select(a => a.CodigoCuenta).Concat(anterior?.Apuntes.Select(a => a.CodigoCuenta) ?? []));
        return Resultado<int>.Correcto(id);
    }

    private async Task BorrarAsync(ISesionDatos s, Asiento asiento, Actor actor, string motivo, Guid idLote)
    {
        foreach (var a in asiento.Apuntes)
            await RegistrarCambioAsync(s, idLote, actor, motivo, asiento.IdAsiento, a.IdApunte, a.Orden, "BAJA", "Apunte", Resumen(a), null);
        await s.EjecutarAsync("DELETE FROM Apuntes WHERE IdAsiento = ?", asiento.IdAsiento);
        await s.EjecutarAsync("DELETE FROM RevisionesAsientos WHERE IdAsiento = ?", asiento.IdAsiento);
        await s.EjecutarAsync("DELETE FROM RelacionesAsientos WHERE IdAsientoOrigen = ? OR IdAsientoDestino = ?", asiento.IdAsiento, asiento.IdAsiento);
        await s.EjecutarAsync("DELETE FROM Asientos WHERE IdAsiento = ?", asiento.IdAsiento);
        await RegistrarCambioAsync(s, idLote, actor, motivo, asiento.IdAsiento, null, null, "BAJA", "Asiento",
            $"{asiento.FechaContable:yyyy-MM-dd} {asiento.Descripcion} ({asiento.Lote?.ToString() ?? "manual"})", null);
    }

    /// <summary>Registro de cambios por apunte: ALTA, BAJA o MODIFICACION de cada campo, comparando por orden.</summary>
    private async Task RegistrarDiferenciasAsync(ISesionDatos s, Guid idLote, Actor actor, string motivo, int idAsiento, Asiento? anterior, Asiento nuevo)
    {
        var viejos = anterior?.Apuntes.ToDictionary(a => a.Orden) ?? [];
        foreach (var a in nuevo.Apuntes)
        {
            if (!viejos.Remove(a.Orden, out var v))
            {
                await RegistrarCambioAsync(s, idLote, actor, motivo, idAsiento, null, a.Orden, "ALTA", "Apunte", null, Resumen(a));
                continue;
            }
            foreach (var (campo, antes, despues) in Campos(v).Zip(Campos(a), (x, y) => (x.Campo, x.Valor, y.Valor)))
                if (!string.Equals(antes, despues, StringComparison.Ordinal))
                    await RegistrarCambioAsync(s, idLote, actor, motivo, idAsiento, v.IdApunte, a.Orden, "MODIFICACION", campo, antes, despues);
        }
        foreach (var v in viejos.Values)
            await RegistrarCambioAsync(s, idLote, actor, motivo, idAsiento, v.IdApunte, v.Orden, "BAJA", "Apunte", Resumen(v), null);
    }

    private static IEnumerable<(string Campo, string? Valor)> Campos(Apunte a) =>
    [
        ("CuentaContable", a.CodigoCuenta), ("ImporteDebe", a.Debe.ToString()), ("ImporteHaber", a.Haber.ToString()), ("Descripcion", a.Descripcion),
        ("NumeroDocumento", a.NumeroDocumento), ("IdCliente", a.IdCliente?.ToString()), ("IdProveedor", a.IdProveedor?.ToString()),
        ("FechaVencimiento", a.FechaVencimiento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("IdCentroCoste", a.IdCentroCoste?.ToString()),
        ("Punteado", a.Punteado ? "Sí" : "No"),
    ];

    private static string Resumen(Apunte a) =>
        $"{a.CodigoCuenta} D {a.Debe} H {a.Haber}" + (string.IsNullOrWhiteSpace(a.Descripcion) ? "" : $" «{a.Descripcion}»");

    private Task RegistrarCambioAsync(ISesionDatos s, Guid idLote, Actor actor, string motivo, int idAsiento, int? idApunte, int? orden, string operacion, string campo, string? antes, string? despues) =>
        s.EjecutarAsync(
            "INSERT INTO RegistroCambiosApuntes (IdLote, FechaHora, Operador, Motivo, IdAsiento, IdApunte, Orden, Operacion, Campo, ValorAntiguo, ValorNuevo) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            idLote.ToString(), _reloj.Ahora, actor.ToString(), motivo, idAsiento, idApunte, orden is int o ? (short)o : null, operacion, campo, antes, despues);

    /// <summary>Revisión auditada con la instantánea JSON anterior (null al crear) y la resultante.</summary>
    private async Task RevisionAsync(ISesionDatos s, int idAsiento, OperacionRevision operacion, string motivo, Actor actor, Asiento? anterior, Asiento posterior)
    {
        var numero = Convert.ToInt32(await s.EscalarAsync("SELECT IIF(MAX(NumeroRevision) IS NULL, 0, MAX(NumeroRevision)) FROM RevisionesAsientos WHERE IdAsiento = ?", idAsiento)) + 1;
        await s.EjecutarAsync(
            "INSERT INTO RevisionesAsientos (IdAsiento, NumeroRevision, TipoOperacion, Motivo, Operador, FechaHora, ContenidoAnterior, ContenidoPosterior) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            idAsiento, numero, (byte)operacion, motivo, actor.ToString(), _reloj.Ahora, anterior is null ? null : Instantanea(anterior), Instantanea(posterior));
    }

    private static string Instantanea(Asiento a) => System.Text.Json.JsonSerializer.Serialize(new
    {
        a.IdAsiento,
        a.Contexto.IdSociedadEjercicio,
        Fecha = a.FechaContable.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        a.Descripcion,
        Tipo = a.Tipo.ToString(),
        a.Notas,
        Lote = a.Lote?.ToString(),
        a.Version,
        Apuntes = a.Apuntes.Select(x => new
        {
            x.Orden, Cuenta = x.CodigoCuenta, Debe = x.Debe.Valor, Haber = x.Haber.Valor, x.Descripcion, x.NumeroDocumento, x.IdCliente, x.IdProveedor,
            Vencimiento = x.FechaVencimiento?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), x.IdCentroCoste, Tipo = x.Tipo.ToString()
        })
    });

    public async Task RecalcularSaldosAsync(ContextoSociedadEjercicio contexto)
    {
        // La misma suma que compara el proceso «Recalcular saldos» (contabilidad/procesos/recalcular-saldos).
        var saldos = ProConta.Contabilidad.Saldos.ComparacionSaldos.Agrupar(
            await bd.EjecutarAsync(s => ProConta.Contabilidad.Saldos.ServicioRecalcularSaldos.MovimientosAsync(s, contexto.IdSociedadEjercicio)));
        var codigos = await bd.EjecutarAsync(s => s.ConsultarAsync("SELECT Codigo FROM CuentasContables WHERE IdSociedadEjercicio = ?",
            f => (f.Texto("Codigo") ?? "").Trim(), contexto.IdSociedadEjercicio));
        // Por tandas: las transacciones de Access tienen un límite de bloqueos.
        foreach (var tanda in codigos.Chunk(150))
            await bd.EnTransaccionAsync(async s =>
            {
                foreach (var codigo in tanda)
                {
                    var (saldo, n) = saldos.GetValueOrDefault(codigo);
                    await s.EjecutarAsync("UPDATE CuentasContables SET SaldoActual = SaldoInicial + ?, NumeroApuntes = ? WHERE IdSociedadEjercicio = ? AND Codigo = ?",
                        saldo, n, contexto.IdSociedadEjercicio, codigo);
                }
                return true;
            });
    }

    /// <summary>Recalcula la caché de saldo y número de apuntes de las cuentas afectadas, en la misma transacción.</summary>
    private static async Task ActualizarSaldosAsync(ISesionDatos s, ContextoSociedadEjercicio contexto, IEnumerable<string> codigos)
    {
        foreach (var codigo in codigos.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal))
        {
            var (saldo, n) = await s.ConsultarPrimeroAsync(
                "SELECT SUM(IIF(ap.ImporteDebe IS NULL, 0, ap.ImporteDebe)) - SUM(IIF(ap.ImporteHaber IS NULL, 0, ap.ImporteHaber)) AS Saldo, COUNT(*) AS N " +
                "FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? AND ap.CuentaContable = ?",
                f => (f.DecimalNulo("Saldo") ?? 0m, f.Entero("N")), contexto.IdSociedadEjercicio, codigo);
            await s.EjecutarAsync("UPDATE CuentasContables SET SaldoActual = SaldoInicial + ?, NumeroApuntes = ? WHERE IdSociedadEjercicio = ? AND Codigo = ?",
                saldo, n, contexto.IdSociedadEjercicio, codigo);
        }
    }

    /// <summary>Protección del ejercicio cerrado en la propia transacción, no solo en pantalla (RF-ASI-03).</summary>
    private static async Task<Resultado> ComprobarEjercicioAbiertoAsync(ISesionDatos s, ContextoSociedadEjercicio contexto, bool externo = false)
    {
        var fila = await s.ConsultarPrimeroAsync("SELECT IdSociedad, Ejercicio, EjercicioCerrado FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?",
            f => (Sociedad: f.Texto("IdSociedad"), Ejercicio: f.Texto("Ejercicio"), Cerrado: f.Logico("EjercicioCerrado")), contexto.IdSociedadEjercicio);
        if (!string.Equals(fila.Sociedad, contexto.IdSociedad, StringComparison.OrdinalIgnoreCase) || fila.Ejercicio != contexto.Ejercicio.ToString(CultureInfo.InvariantCulture))
            return Resultado.Fallo(new Error("Asientos.ContextoNoValido", "El ejercicio de trabajo no es válido.", contexto.ToString(),
                "No existe o no corresponde a la sociedad.", "Vuelve a elegir la empresa y el ejercicio."));
        return fila.Cerrado && !externo
            ? Resultado.Fallo(new Error("Asientos.EjercicioCerrado", $"El ejercicio {contexto.Ejercicio} está cerrado y no admite cambios en sus asientos.", contexto.ToString(),
                "Un ejercicio cerrado solo se puede consultar.", "Si hay que corregirlo, reabre el ejercicio con el proceso contable autorizado."))
            : Resultado.Correcto();
    }

    /// <summary>
    /// Protección del trimestre cerrado (contabilidad/asientos 2026-11-13 09:45): con un trimestre activo configurado en el
    /// ejercicio (SociedadesEjercicios.TrimestreActivo), ningún módulo crea, cambia ni borra un asiento fechado antes de
    /// él si lleva alguna cuenta fiscal del trimestre (<see cref="ReglasAsiento.CuentasFiscalesTrimestre"/>), ni le pone una
    /// (<paramref name="cuentaNueva"/>); se corrige con un contra-asiento en el trimestre activo. Los asientos sin cuentas
    /// fiscales (bancos, amortizaciones…) sí se tocan. Sin trimestre configurado no se comprueba.
    /// </summary>
    private static async Task<Resultado> ComprobarTrimestreAbiertoAsync(ISesionDatos s, Asiento asiento, string? cuentaNueva = null)
    {
        var fiscal = ReglasAsiento.AfectaTrimestreFiscal(asiento.Apuntes.Select(a => a.CodigoCuenta).Append(cuentaNueva));
        if (fiscal is null) return Resultado.Correcto();
        var contexto = asiento.Contexto;
        var valor = await s.EscalarAsync("SELECT TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedadEjercicio = ?", contexto.IdSociedadEjercicio);
        if (valor is null or DBNull) return Resultado.Correcto();
        var activo = Convert.ToInt32(valor, CultureInfo.InvariantCulture);
        if (activo is < 1 or > 4) return Resultado.Correcto();
        var inicio = TrimestreContable.Inicio(contexto.Ejercicio, activo);
        if (asiento.FechaContable.Date >= inicio) return Resultado.Correcto();
        var cerrado = NombreTrimestre(TrimestreContable.De(asiento.FechaContable));
        var registro = asiento.EsNuevo ? $"Asiento del {asiento.FechaContable:dd/MM/yyyy}" : $"Asiento {asiento.IdAsiento} del {asiento.FechaContable:dd/MM/yyyy}";
        return Resultado.Fallo(new Error("Asientos.TrimestreCerrado",
            $"El trimestre {cerrado} de {contexto.Ejercicio} está cerrado y el asiento lleva la cuenta fiscal {fiscal}: no se crea, cambia ni borra.", registro,
            $"El trimestre activo es {NombreTrimestre(activo)}; lo que toca la cuenta {fiscal} en un trimestre cerrado ya puede estar en su declaración (IVA o retenciones).",
            $"Corrígelo con un contra-asiento fechado en {NombreTrimestre(activo)}."));
    }

    private static string NombreTrimestre(int t) => t switch { 1 => "ene-mar", 2 => "abr-jun", 3 => "jul-sep", _ => "oct-dic" };

    private static async Task<Resultado<CuentaContable>> ResolverCuentaAsync(ISesionDatos s, ContextoSociedadEjercicio contexto, string codigo)
    {
        var texto = (codigo ?? "").Trim();
        var cuenta = await s.ConsultarPrimeroAsync(
            "SELECT IdCuentaContable, IdIdentidadContable, IdSociedadEjercicio, IdCuentaPadre, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, IdCentroCostePredeterminado, SaldoInicial, SaldoActual, NumeroApuntes, Descripcion FROM CuentasContables WHERE IdSociedadEjercicio = ? AND Codigo = ?",
            CatalogoCuentasAccess.Mapear, contexto.IdSociedadEjercicio, texto);
        if (cuenta is null) return Resultado<CuentaContable>.Fallo(CuentaNoExiste(texto, contexto));
        var admite = ReglasCuenta.AdmiteApuntes(cuenta);
        return admite.EsCorrecto ? Resultado<CuentaContable>.Correcto(cuenta) : Resultado<CuentaContable>.Fallo(admite.Errores);
    }

    private static Error CuentaNoExiste(string codigo, ContextoSociedadEjercicio contexto) =>
        new("Cuentas.NoExiste", $"No existe la cuenta {codigo} en el ejercicio {contexto.Ejercicio}.", $"Cuenta {codigo}",
            "No hay ninguna cuenta con ese código exacto en este ejercicio.", "Revisa el código o da de alta la cuenta en este ejercicio.");

    private static Error CentroNoValido(Asiento asiento, Apunte apunte) =>
        new("Asientos.CentroNoValido", $"El centro de coste del apunte {apunte.Orden} no admite apuntes.", $"{asiento}, apunte {apunte.Orden}",
            "No existe en esta sociedad, está inactivo o agrupa otros centros.", "Elige un centro de coste activo e imputable.");

    private static Task<IReadOnlyList<int>> IdsDelLoteAsync(ISesionDatos s, ContextoSociedadEjercicio contexto, IdentidadLote lote) =>
        s.ConsultarAsync(
            "SELECT IdAsiento FROM Asientos WHERE EsAutomatico = True AND MotorPropietario = ? AND CodigoProceso = ? AND IdSociedadEjercicio = ? AND ClaveEjecucion = ? ORDER BY IdAsiento",
            f => f.Entero("IdAsiento"), lote.MotorPropietario, lote.CodigoProceso, contexto.IdSociedadEjercicio, lote.ClaveEjecucion);

    private static async Task<Asiento?> CargarAsync(ISesionDatos s, int idAsiento)
    {
        var cabecera = await s.ConsultarPrimeroAsync(
            $"SELECT a.{ColumnasAsiento.Replace(", ", ", a.")}, se.IdSociedad, se.Ejercicio FROM Asientos AS a INNER JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio WHERE a.IdAsiento = ?",
            f =>
            {
                var contexto = new ContextoSociedadEjercicio(f.Texto("IdSociedad") ?? "", f.Entero("IdSociedadEjercicio"),
                    int.TryParse(f.Texto("Ejercicio"), NumberStyles.None, CultureInfo.InvariantCulture, out var anio) ? anio : 0);
                return new Asiento(contexto, f.Fecha("FechaContable"), f.Texto("Descripcion") ?? "")
                {
                    IdAsiento = f.Entero("IdAsiento"),
                    Tipo = (TipoAsiento)f.Entero("TipoAsiento"),
                    Notas = f.Texto("Notas"),
                    Estado = (EstadoAsiento)f.Entero("EstadoAsiento"),
                    Version = f.Entero("Version"),
                    Lote = f.Logico("EsAutomatico") ? new IdentidadLote(f.Texto("MotorPropietario") ?? "", f.Texto("CodigoProceso") ?? "", f.Texto("ClaveEjecucion") ?? "") : null,
                    IdPlantilla = f.EnteroNulo("IdPlantillaAsiento"),
                    CorregidoAMano = !f.EsNulo("CorregidoAMano") && f.Logico("CorregidoAMano"),
                };
            }, idAsiento);
        if (cabecera is null) return null;
        cabecera.Apuntes.AddRange(await s.ConsultarAsync($"SELECT {ColumnasApunte} FROM Apuntes WHERE IdAsiento = ? ORDER BY Orden", f => new Apunte
        {
            IdApunte = f.Entero("IdApunte"),
            Orden = f.Entero("Orden"),
            CodigoCuenta = f.Texto("CuentaContable") ?? "",
            Debe = Importe.Redondear(f.DecimalNulo("ImporteDebe") ?? 0m),
            Haber = Importe.Redondear(f.DecimalNulo("ImporteHaber") ?? 0m),
            Descripcion = f.Texto("Descripcion"),
            NumeroDocumento = f.Texto("NumeroDocumento"),
            IdCliente = f.EnteroNulo("IdCliente"),
            IdProveedor = f.EnteroNulo("IdProveedor"),
            FechaVencimiento = f.FechaNula("FechaVencimiento"),
            IdCentroCoste = f.EnteroNulo("IdCentroCoste"),
            Tipo = (TipoApunte)f.Entero("TipoApunte"),
            GeneradoAutomaticamente = f.Logico("GeneradoAutomaticamente"),
            MotivoGeneracion = f.Texto("MotivoGeneracion"),
            Punteado = !f.EsNulo("Punteado") && f.Logico("Punteado"),
        }, idAsiento));
        return cabecera;
    }

    private static Error? ValidarLote(IdentidadLote lote, string motivo) =>
        string.IsNullOrWhiteSpace(lote.MotorPropietario) || string.IsNullOrWhiteSpace(lote.CodigoProceso) || string.IsNullOrWhiteSpace(lote.ClaveEjecucion)
            ? new Error("Asientos.LoteSinIdentidad", "El lote automático no tiene motor, proceso o clave.", lote.ToString(),
                "Sin identidad completa no se pueden regenerar sus asientos.", "Indica el motor, el proceso y la clave del documento de origen.")
            : string.IsNullOrWhiteSpace(motivo) ? SinMotivo(lote.ToString()) : null;

    /// <summary>Solo el importador de ContaSol puede grabar un libro externo en periodos cerrados.</summary>
    private static Error? ValidarExterno(IdentidadLote lote, bool externo) =>
        externo && !string.Equals(lote.MotorPropietario, OpcionesLote.MotorImportadorContaSol, StringComparison.Ordinal)
            ? new Error("Asientos.LibroExternoNoPermitido", "Este lote no puede grabarse en un periodo cerrado.", lote.ToString(),
                "Solo los asientos importados de ContaSol, copia de un libro ya cerrado, se admiten en ejercicios y trimestres cerrados.",
                "Fecha el asiento en el trimestre activo o reabre el periodo con el proceso contable autorizado.")
            : null;

    /// <summary>Solo la liquidación del IVA (y las facturas pendientes de recibir, a 31/12) puede grabar su asiento en el trimestre cerrado que liquida.</summary>
    private static Error? ValidarLiquidacion(IdentidadLote lote, OpcionesLote? opciones) =>
        opciones?.TrimestreLiquidado is not null && !string.Equals(lote.MotorPropietario, OpcionesLote.MotorLiquidacionIva, StringComparison.Ordinal)
            && !string.Equals(lote.MotorPropietario, OpcionesLote.MotorFacturasPendientes, StringComparison.Ordinal)
            ? new Error("Asientos.TrimestreLiquidadoNoPermitido", "Este lote no puede grabarse en un trimestre cerrado.", lote.ToString(),
                "Solo la liquidación del IVA graba su propio asiento en el trimestre cerrado que liquida.",
                "Fecha el asiento en el trimestre activo.")
            : null;

    /// <summary>Solo el cierre del ejercicio puede grabar sus asientos en un trimestre cerrado.</summary>
    private static Error? ValidarCierre(IdentidadLote lote, OpcionesLote? opciones) =>
        opciones?.CierreEjercicio == true && !string.Equals(lote.MotorPropietario, OpcionesLote.MotorCierreEjercicio, StringComparison.Ordinal)
            ? new Error("Asientos.CierreNoPermitido", "Este lote no puede grabarse en un trimestre cerrado.", lote.ToString(),
                "Solo el cierre del ejercicio (regularización, cierre y apertura) graba sus asientos sin mirar el trimestre cerrado.",
                "Fecha el asiento en el trimestre activo.")
            : null;

    private static Error SinMotivo(string registro) =>
        new("Asientos.SinMotivo", "Hay que indicar el motivo de la operación.", registro, "Toda modificación queda registrada con su motivo.", "Escribe el motivo.");

    private static Error NoExiste(int idAsiento) =>
        new("Asientos.NoExiste", "El asiento ya no existe.", $"Asiento {idAsiento}", "Otra persona lo ha borrado.", "Vuelve a cargar los asientos.");

    private static Error AutomaticoNoManual(Asiento asiento) =>
        new("Asientos.Automatico", "Este asiento es automático y no se cambia ni se borra como uno manual.", asiento.ToString(),
            $"Lo generó {asiento.Lote} y solo ese proceso lo regenera o lo borra.",
            "Corrige el documento de origen y vuelve a generar su asiento; para corregir cuentas o importes a mano, usa «Modificar el asiento…» del Diario.");

    private static ConflictoConcurrenciaException Conflicto(Asiento asiento) =>
        new(new Error("Datos.ConflictoVersion",
            "No he guardado tus cambios porque otra persona ha modificado este asiento mientras lo editabas.", asiento.ToString(),
            "La versión del asiento ya no es la que leíste.", "Vuelve a abrir el asiento y repite tus cambios."));
}
