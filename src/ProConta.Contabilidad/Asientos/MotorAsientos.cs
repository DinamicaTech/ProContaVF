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
public sealed record OpcionesLote(bool LibroExterno = false, bool DiferirSaldos = false)
{
    public static readonly OpcionesLote Normal = new();

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

    /// <summary>Borra un asiento manual (con la versión leída y motivo).</summary>
    Task<Resultado> EliminarManualAsync(int idAsiento, int versionLeida, Actor actor, string motivo);

    /// <summary>
    /// Sustituye todos los asientos de un lote automático por los indicados, en una sola transacción:
    /// regenerar no duplica, y si uno falla no se guarda ninguno.
    /// </summary>
    Task<Resultado<IReadOnlyList<int>>> ReemplazarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, IReadOnlyList<Asiento> asientos, Actor actor, string motivo,
        TrabajoLote? enLaTransaccion = null, OpcionesLote? opciones = null);

    /// <summary>Borra los asientos de un lote automático; devuelve cuántos.</summary>
    Task<Resultado<int>> EliminarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, Actor actor, string motivo, TrabajoLote? enLaTransaccion = null,
        OpcionesLote? opciones = null);

    /// <summary>Cambia de cuenta varios apuntes manuales en una operación (mismo IdLote en el registro).</summary>
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

    /// <summary>Cambia el concepto de apuntes manuales (vacío: el del asiento). Nunca en automáticos ni en periodos cerrados.</summary>
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
        "IdAsiento, IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, Notas, EstadoAsiento, Version, EsAutomatico, MotorPropietario, CodigoProceso, ClaveEjecucion";
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
            await BorrarAsync(s, actual, actor, motivo.Trim(), Guid.NewGuid());
            await ActualizarSaldosAsync(s, actual.Contexto, actual.Apuntes.Select(a => a.CodigoCuenta));
            return Resultado.Correcto();
        });

    public Task<Resultado<IReadOnlyList<int>>> ReemplazarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, IReadOnlyList<Asiento> asientos, Actor actor, string motivo,
        TrabajoLote? enLaTransaccion = null, OpcionesLote? opciones = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            var externo = (opciones ?? OpcionesLote.Normal).LibroExterno;
            var diferir = (opciones ?? OpcionesLote.Normal).DiferirSaldos;
            var error = ValidarLote(lote, motivo) ?? ValidarExterno(lote, externo) ?? asientos.Where(a => a.Contexto != contexto).Select(a => new Error("Asientos.LoteOtroEjercicio",
                "Todos los asientos del lote deben ser del mismo ejercicio.", a.ToString(), "El lote es de un ejercicio.", "Genera un lote por ejercicio.")).FirstOrDefault();
            if (error is not null) return Resultado<IReadOnlyList<int>>.Fallo(error);
            var abierto = await ComprobarEjercicioAbiertoAsync(s, contexto, externo);
            if (!abierto.EsCorrecto) return Resultado<IReadOnlyList<int>>.Fallo(abierto.Errores);

            var idLote = Guid.NewGuid();
            var anteriores = await IdsDelLoteAsync(s, contexto, lote);
            if (enLaTransaccion?.Antes is { } antes) await antes(s, anteriores);
            // Los asientos que ya tiene el lote se reutilizan en orden (cambian sus apuntes, no su número), y solo se
            // borran los que sobran o se crean los que faltan (contabilidad/asientos 2026-11-07).
            var cuentas = new List<string>();
            var reutilizados = new List<Asiento>();
            foreach (var (id, i) in anteriores.Select((id, i) => (id, i)))
            {
                var viejo = (await CargarAsync(s, id))!;
                var trimestre = externo ? Resultado.Correcto() : await ComprobarTrimestreAbiertoAsync(s, viejo);
                if (!trimestre.EsCorrecto) return Resultado<IReadOnlyList<int>>.Fallo(trimestre.Errores);
                cuentas.AddRange(viejo.Apuntes.Select(a => a.CodigoCuenta));
                if (i < asientos.Count) reutilizados.Add(viejo);
                else await BorrarAsync(s, viejo, actor, motivo.Trim(), idLote);
            }
            var nuevos = new List<int>();
            var errores = new List<Error>();
            for (var i = 0; i < asientos.Count; i++)
            {
                var asiento = asientos[i];
                asiento.IdAsiento = i < reutilizados.Count ? reutilizados[i].IdAsiento : 0;
                if (i < reutilizados.Count) asiento.Version = reutilizados[i].Version;
                asiento.Lote = lote;
                var guardado = await GuardarInternoAsync(s, asiento, actor, motivo.Trim(), idLote, manual: false, externo, diferir);
                if (guardado.EsCorrecto) nuevos.Add(guardado.Valor);
                else errores.AddRange(guardado.Errores);
            }
            if (errores.Count > 0) return Resultado<IReadOnlyList<int>>.Fallo(errores);
            if (enLaTransaccion?.Despues is { } despues)
            {
                var hecho = await despues(s, nuevos);
                if (!hecho.EsCorrecto) return Resultado<IReadOnlyList<int>>.Fallo(hecho.Errores);
            }
            if (!diferir) await ActualizarSaldosAsync(s, contexto, cuentas);
            return Resultado<IReadOnlyList<int>>.Correcto(nuevos);
        });

    public Task<Resultado<int>> EliminarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, Actor actor, string motivo, TrabajoLote? enLaTransaccion = null,
        OpcionesLote? opciones = null) =>
        bd.EnTransaccionAsync(async s =>
        {
            var externo = (opciones ?? OpcionesLote.Normal).LibroExterno;
            var error = ValidarLote(lote, motivo) ?? ValidarExterno(lote, externo);
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
                var trimestre = externo ? Resultado.Correcto() : await ComprobarTrimestreAbiertoAsync(s, viejo);
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
            foreach (var idApunte in idsApunte.Distinct())
            {
                var fila = await s.ConsultarPrimeroAsync(
                    "SELECT ap.IdAsiento, ap.Orden, ap.CuentaContable, a.IdSociedadEjercicio, a.EsAutomatico FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE ap.IdApunte = ?",
                    f => (IdAsiento: f.Entero("IdAsiento"), Orden: f.Entero("Orden"), Cuenta: f.Texto("CuentaContable") ?? "", Ejercicio: f.Entero("IdSociedadEjercicio"), Automatico: f.Logico("EsAutomatico")),
                    idApunte);
                if (fila.IdAsiento == 0 || fila.Ejercicio != contexto.IdSociedadEjercicio)
                    return Resultado<Guid>.Fallo(new Error("Asientos.ApunteNoEncontrado", $"El apunte {idApunte} no existe en este ejercicio.", $"Apunte {idApunte}",
                        "Se ha borrado o es de otro ejercicio.", "Vuelve a cargar los apuntes."));
                if (fila.Automatico)
                    return Resultado<Guid>.Fallo(new Error("Asientos.Automatico", $"El apunte {idApunte} es de un asiento automático y no se cambia a mano.", $"Asiento {fila.IdAsiento}",
                        "Los asientos automáticos solo los regenera el proceso que los generó.", "Corrige el documento de origen y vuelve a generar su asiento."));
                if (fila.Cuenta == cuenta.Valor.Codigo) continue;
                if (!anteriores.ContainsKey(fila.IdAsiento))
                {
                    var asiento = (await CargarAsync(s, fila.IdAsiento))!;
                    var trimestre = await ComprobarTrimestreAbiertoAsync(s, asiento);
                    if (!trimestre.EsCorrecto) return Resultado<Guid>.Fallo(trimestre.Errores);
                    anteriores[fila.IdAsiento] = asiento;
                }
                await s.EjecutarAsync("UPDATE Apuntes SET CuentaContable = ? WHERE IdApunte = ?", cuenta.Valor.Codigo, idApunte);
                await RegistrarCambioAsync(s, idLote, actor, motivo.Trim(), fila.IdAsiento, idApunte, fila.Orden, "MODIFICACION", "CuentaContable", fila.Cuenta, cuenta.Valor.Codigo);
                afectadas.Add(fila.Cuenta);
            }
            foreach (var (idAsiento, anterior) in anteriores)
            {
                await s.EjecutarAsync("UPDATE Asientos SET Version = Version + 1, OperadorModificacion = ?, FechaHoraModificacion = ? WHERE IdAsiento = ?",
                    actor.ToString(), _reloj.Ahora, idAsiento);
                await RevisionAsync(s, idAsiento, OperacionRevision.Modificacion, motivo.Trim(), actor, anterior, (await CargarAsync(s, idAsiento))!);
            }
            await ActualizarSaldosAsync(s, contexto, afectadas);
            return Resultado<Guid>.Correcto(idLote);
        });

    /// <summary>Lo que hace falta de un apunte para las operaciones del Mayor.</summary>
    private sealed record ApunteOperacion(int IdApunte, int IdAsiento, int Orden, string? Descripcion, string? Documento, bool Punteado, bool Automatico);

    private static async Task<Resultado<IReadOnlyList<ApunteOperacion>>> LeerApuntesAsync(ISesionDatos s, IReadOnlyCollection<int> idsApunte)
    {
        var lista = new List<ApunteOperacion>();
        foreach (var idApunte in idsApunte.Distinct())
        {
            var fila = await s.ConsultarPrimeroAsync(
                "SELECT ap.IdApunte, ap.IdAsiento, ap.Orden, ap.Descripcion, ap.NumeroDocumento, ap.Punteado, a.EsAutomatico FROM Apuntes AS ap " +
                "INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE ap.IdApunte = ?",
                f => new ApunteOperacion(f.Entero("IdApunte"), f.Entero("IdAsiento"), f.Entero("Orden"), f.Texto("Descripcion"), f.Texto("NumeroDocumento"),
                    !f.EsNulo("Punteado") && f.Logico("Punteado"), !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico")),
                idApunte);
            if (fila is null)
                return Resultado<IReadOnlyList<ApunteOperacion>>.Fallo(new Error("Asientos.ApunteNoEncontrado", $"El apunte {idApunte} ya no existe.", $"Apunte {idApunte}",
                    "Se ha borrado, o su asiento se ha regenerado después de cargar el Mayor.", "Vuelve a cargar el Mayor y repite la operación."));
            lista.Add(fila);
        }
        return lista.Count == 0
            ? Resultado<IReadOnlyList<ApunteOperacion>>.Fallo(new Error("Asientos.SinApuntes", "No hay ningún apunte elegido.", "Mayor", null, "Elige una o varias filas."))
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
        bd.EnTransaccionAsync(async s =>
        {
            if (string.IsNullOrWhiteSpace(motivo)) return Resultado<Guid>.Fallo(SinMotivo("Cambio de concepto"));
            var nuevo = string.IsNullOrWhiteSpace(concepto) ? null : concepto.Trim();
            if (nuevo is { Length: > 255 })
                return Resultado<Guid>.Fallo(new Error("Asientos.ConceptoLargo", "El concepto tiene más de 255 caracteres.", nuevo[..30] + "…",
                    "El concepto del apunte admite hasta 255 caracteres.", "Acórtalo."));
            var leidos = await LeerApuntesAsync(s, idsApunte);
            if (!leidos.EsCorrecto) return Resultado<Guid>.Fallo(leidos.Errores);
            if (leidos.Valor.FirstOrDefault(a => a.Automatico) is { } auto)
                return Resultado<Guid>.Fallo(new Error("Asientos.Automatico", $"El apunte {auto.Orden} del asiento {auto.IdAsiento} es de un asiento automático y su concepto no se cambia a mano.",
                    $"Asiento {auto.IdAsiento}", "Los asientos automáticos los regenera el proceso que los generó, y el cambio se perdería.",
                    "Corrige el concepto en el documento de origen (la factura) y vuelve a generar su asiento."));
            var idLote = Guid.NewGuid();
            var cambiados = new Dictionary<int, Asiento>();
            foreach (var a in leidos.Valor.Where(a => !string.Equals(a.Descripcion?.Trim(), nuevo, StringComparison.Ordinal)))
            {
                if (!cambiados.ContainsKey(a.IdAsiento))
                {
                    var asiento = (await CargarAsync(s, a.IdAsiento))!;
                    var abierto = await ComprobarEjercicioAbiertoAsync(s, asiento.Contexto);
                    if (!abierto.EsCorrecto) return Resultado<Guid>.Fallo(abierto.Errores);
                    var trimestre = await ComprobarTrimestreAbiertoAsync(s, asiento);
                    if (!trimestre.EsCorrecto) return Resultado<Guid>.Fallo(trimestre.Errores);
                    cambiados[a.IdAsiento] = asiento;
                }
                await s.EjecutarAsync("UPDATE Apuntes SET Descripcion = ? WHERE IdApunte = ?", nuevo, a.IdApunte);
                await RegistrarCambioAsync(s, idLote, actor, motivo.Trim(), a.IdAsiento, a.IdApunte, a.Orden, "MODIFICACION", "Descripcion", a.Descripcion, nuevo);
            }
            await CerrarModificacionAsync(s, cambiados, actor, motivo.Trim());
            return Resultado<Guid>.Correcto(idLote);
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
    private async Task<Resultado<int>> GuardarInternoAsync(ISesionDatos s, Asiento asiento, Actor actor, string motivo, Guid idLote, bool manual, bool externo = false, bool diferirSaldos = false)
    {
        var reglas = ReglasAsiento.ValidarYCuadrar(asiento, _politica);
        if (!reglas.EsCorrecto) return Resultado<int>.Fallo(reglas.Errores);
        var abierto = await ComprobarEjercicioAbiertoAsync(s, asiento.Contexto, externo);
        if (!abierto.EsCorrecto) return Resultado<int>.Fallo(abierto.Errores);
        var trimestre = externo ? Resultado.Correcto() : await ComprobarTrimestreAbiertoAsync(s, asiento);
        if (!trimestre.EsCorrecto) return Resultado<int>.Fallo(trimestre.Errores);

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
            if (manual && anterior.EsAutomatico) return Resultado<int>.Fallo(AutomaticoNoManual(anterior));
            var trimestreAnterior = externo ? Resultado.Correcto() : await ComprobarTrimestreAbiertoAsync(s, anterior);
            if (!trimestreAnterior.EsCorrecto) return Resultado<int>.Fallo(trimestreAnterior.Errores);
            if (anterior.Contexto.IdSociedadEjercicio != asiento.Contexto.IdSociedadEjercicio)
                return Resultado<int>.Fallo(new Error("Asientos.CambioEjercicio", "Un asiento no se puede mover a otro ejercicio.", asiento.ToString(),
                    "El ejercicio forma parte de la identidad del asiento.", "Bórralo y créalo en el ejercicio correcto, o usa la plantilla de rectificación."));
        }

        var ahora = _reloj.Ahora;
        int id;
        if (anterior is null)
        {
            await s.EjecutarAsync(
                "INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, Notas, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, " +
                "OperadorContabilizacion, FechaHoraContabilizacion, TotalDebe, TotalHaber, Version, EsAutomatico, MotorPropietario, CodigoProceso, ClaveEjecucion) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                asiento.Contexto.IdSociedadEjercicio, asiento.FechaContable, asiento.Descripcion.Trim(), (byte)asiento.Tipo, asiento.Notas, (byte)EstadoAsiento.Contabilizado,
                actor.ToString(), ahora, actor.ToString(), ahora, asiento.TotalDebe, asiento.TotalHaber, 1, asiento.EsAutomatico,
                asiento.Lote?.MotorPropietario, asiento.Lote?.CodigoProceso, asiento.Lote?.ClaveEjecucion);
            id = await s.UltimoIdAsync();
        }
        else
        {
            id = asiento.IdAsiento;
            await s.ActualizarConVersionAsync(
                "UPDATE Asientos SET FechaContable = ?, Descripcion = ?, TipoAsiento = ?, Notas = ?, OperadorModificacion = ?, FechaHoraModificacion = ?, " +
                "TotalDebe = ?, TotalHaber = ?, Version = Version + 1 WHERE IdAsiento = ? AND Version = ?",
                asiento.ToString(),
                asiento.FechaContable, asiento.Descripcion.Trim(), (byte)asiento.Tipo, asiento.Notas, actor.ToString(), ahora,
                asiento.TotalDebe, asiento.TotalHaber, id, asiento.Version);
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
        var saldos = (await bd.EjecutarAsync(s => s.ConsultarAsync(
                "SELECT ap.CuentaContable, SUM(IIF(ap.ImporteDebe IS NULL, 0, ap.ImporteDebe)) - SUM(IIF(ap.ImporteHaber IS NULL, 0, ap.ImporteHaber)) AS Saldo, COUNT(*) AS N " +
                "FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = ? GROUP BY ap.CuentaContable",
                f => (Cuenta: (f.Texto("CuentaContable") ?? "").Trim(), Saldo: f.DecimalNulo("Saldo") ?? 0m, N: f.Entero("N")), contexto.IdSociedadEjercicio)))
            .ToDictionary(x => x.Cuenta, x => (x.Saldo, x.N), StringComparer.Ordinal);
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
    /// Protección del trimestre cerrado (contabilidad/procesos 2026-11-07): con un trimestre activo configurado en el
    /// ejercicio (SociedadesEjercicios.TrimestreActivo), ningún módulo crea, cambia ni borra un asiento fechado antes de
    /// él; se corrige con un contra-asiento en el trimestre activo. Sin trimestre configurado no se comprueba.
    /// </summary>
    private static async Task<Resultado> ComprobarTrimestreAbiertoAsync(ISesionDatos s, Asiento asiento)
    {
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
            $"El trimestre {cerrado} de {contexto.Ejercicio} está cerrado: sus asientos no se crean, cambian ni borran.", registro,
            $"El trimestre activo es {NombreTrimestre(activo)}; lo de un trimestre cerrado ya puede estar en la declaración del IVA.",
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

    private static Error SinMotivo(string registro) =>
        new("Asientos.SinMotivo", "Hay que indicar el motivo de la operación.", registro, "Toda modificación queda registrada con su motivo.", "Escribe el motivo.");

    private static Error NoExiste(int idAsiento) =>
        new("Asientos.NoExiste", "El asiento ya no existe.", $"Asiento {idAsiento}", "Otra persona lo ha borrado.", "Vuelve a cargar los asientos.");

    private static Error AutomaticoNoManual(Asiento asiento) =>
        new("Asientos.Automatico", "Este asiento es automático y no se puede cambiar a mano.", asiento.ToString(),
            $"Lo generó {asiento.Lote} y solo ese proceso lo regenera.", "Corrige el documento de origen y vuelve a generar su asiento.");

    private static ConflictoConcurrenciaException Conflicto(Asiento asiento) =>
        new(new Error("Datos.ConflictoVersion",
            "No he guardado tus cambios porque otra persona ha modificado este asiento mientras lo editabas.", asiento.ToString(),
            "La versión del asiento ya no es la que leíste.", "Vuelve a abrir el asiento y repite tus cambios."));
}
