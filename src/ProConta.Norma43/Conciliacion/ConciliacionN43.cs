using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Norma43.Conciliacion;

/// <summary>Imputación de una línea con su cuenta del ejercicio del asiento.</summary>
/// <param name="ImporteFirmado">Positivo: la contrapartida va al Debe (cargo); negativo: al Haber (abono).</param>
public sealed record ImputacionConciliada(int IdIdentidadContable, string? Cuenta, string? NombreCuenta, decimal ImporteFirmado, string? Concepto, int? IdCentroCoste);

/// <summary>
/// Propuesta de asiento de una línea N43 (norma43/conciliacion): el asiento que generaría contabilizarla, sin grabar nada, o los motivos por
/// los que no se puede. Se puede corregir el asiento (sus apuntes) antes de pasarla a <see cref="ServicioConciliacionN43.ContabilizarAsync(IReadOnlyList{PropuestaLineaN43}, Actor)"/>.
/// </summary>
/// <param name="IdAsientoActual">Asiento que ya tiene la línea (de ProContaVF o del ERP), si lo tiene.</param>
/// <param name="Regenera">La línea ya está contabilizada por ProContaVF: contabilizarla otra vez sustituye su asiento (mismo número).</param>
public sealed record PropuestaLineaN43(
    int IdLineaExtractoBancario,
    DateTime FechaOperacion,
    DateTime FechaValor,
    string Sentido,
    decimal Importe,
    string? Concepto,
    string? CuentaBanco,
    ContextoSociedadEjercicio? Contexto,
    AsientoPropuesto? Asiento,
    IReadOnlyList<ImputacionConciliada> Imputaciones,
    IReadOnlyList<Error> Errores,
    IReadOnlyList<string> Avisos,
    int? IdAsientoActual = null,
    bool Regenera = false,
    int? IdPlantilla = null)
{
    public bool Contabilizable => Errores.Count == 0 && Asiento is not null && Contexto is not null;

    /// <summary>Importe con signo desde el punto de vista del banco: positivo si entra dinero (abono, el banco al Debe).</summary>
    public decimal ImporteBanco => Sentido == ServicioConciliacionN43.Abono ? Importe : -Importe;

    public string Rotulo => $"Movimiento del {FechaOperacion:dd/MM/yyyy} de {Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €";
}

/// <summary>Resultado de contabilizar o deshacer varias líneas: lo que se hizo y lo que no, con sus motivos.</summary>
public sealed record ResultadoConciliacionN43(IReadOnlyDictionary<int, int> Asientos, IReadOnlyDictionary<int, IReadOnlyList<Error>> Rechazadas)
{
    public int Hechas => Asientos.Count;
}

/// <summary>
/// Conciliación bancaria asistida (norma43/conciliacion): genera con el motor de asientos un asiento por cada línea N43 con cuenta
/// asignada, de la 572 de su cuenta bancaria contra la cuenta (o cuentas, si está repartida) de sus imputaciones. Los apuntes salen de la
/// plantilla de sistema «Movimiento bancario (Norma 43)» (proceso <see cref="ClaveProceso"/>) con el motor de plantillas; primero se
/// proponen, sin grabar nada, y después se contabilizan. Una línea sin cuenta no se contabiliza (no hay cuenta puente). Cada asiento es un
/// lote automático propio de la línea: contabilizarla otra vez lo sustituye con el mismo número, y deshacer lo borra. Las líneas
/// contabilizadas en el ERP (su asiento es de ContaSol) no se tocan.
/// </summary>
public sealed class ServicioConciliacionN43(IBaseDatos bd, IMotorAsientos motor, ServicioPlantillasAsiento plantillas, IReloj? reloj = null)
{
    public ServicioConciliacionN43(IBaseDatos bd) : this(bd, new MotorAsientosAccess(bd), new ServicioPlantillasAsiento(bd)) { }

    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>Motor propietario de los asientos de la conciliación.</summary>
    public const string MotorPropietario = "norma43/conciliacion";
    public const string CodigoProceso = "LINEA_N43";

    /// <summary>Clave de proceso de la plantilla de sistema que da los apuntes.</summary>
    public const string ClaveProceso = "norma43";

    /// <summary>Estado de la fila de ContabilizacionesLineaExtracto mientras el asiento existe.</summary>
    public const string EstadoContabilizada = "CONTABILIZADA";

    /// <summary>Versión de las reglas con que se generó el asiento (ContabilizacionesLineaExtracto.VersionProceso; la migración del ERP usa 1).</summary>
    public const int VersionProceso = 2;

    public const string Cargo = "D";
    public const string Abono = "H";

    public static IdentidadLote Lote(int idLinea) => new(MotorPropietario, CodigoProceso, idLinea.ToString(CultureInfo.InvariantCulture));

    // ---------- proponer ----------

    /// <summary>
    /// Propone el asiento de cada línea, en el orden pedido, sin grabar nada. Las líneas que no existen o son de otra sociedad salen con su
    /// error. <paramref name="idSociedad"/> es la sociedad activa.
    /// </summary>
    public async Task<IReadOnlyList<PropuestaLineaN43>> ProponerAsync(string idSociedad, IReadOnlyCollection<int> idsLineas)
    {
        ArgumentNullException.ThrowIfNull(idsLineas);
        var lineas = await LeerLineasAsync(idsLineas);
        var plantilla = await plantillas.DeProcesoAsync(ClaveProceso, idSociedad) ?? PlantillaConciliacionN43.Predeterminada;
        var ejercicios = new Dictionary<int, EjercicioConciliacion?>();
        var hoy = _reloj.Ahora.Date;
        var r = new List<PropuestaLineaN43>();
        foreach (var id in idsLineas)
        {
            if (!lineas.TryGetValue(id, out var l))
            {
                r.Add(SinLinea(id));
                continue;
            }
            r.Add(await ProponerLineaAsync(idSociedad, l, plantilla, ejercicios, hoy));
        }
        return r;
    }

    private async Task<PropuestaLineaN43> ProponerLineaAsync(string idSociedad, LineaLeida l, PlantillaAsiento plantilla,
        Dictionary<int, EjercicioConciliacion?> ejercicios, DateTime hoy)
    {
        var errores = new List<Error>();
        var avisos = new List<string>();
        var registro = $"Movimiento del {l.FechaOperacion:dd/MM/yyyy} de {l.Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €";
        PropuestaLineaN43 Fin(ContextoSociedadEjercicio? ctx = null, string? banco = null, AsientoPropuesto? asiento = null, IReadOnlyList<ImputacionConciliada>? imp = null,
            bool regenera = false) =>
            new(l.Id, l.FechaOperacion, l.FechaValor, l.Sentido, l.Importe, l.Concepto, banco, ctx, asiento, imp ?? [], errores, avisos, l.IdAsiento, regenera,
                plantilla.IdPlantilla > 0 ? plantilla.IdPlantilla : null);

        if (!string.Equals(l.IdSociedad, idSociedad, StringComparison.OrdinalIgnoreCase))
        {
            errores.Add(new Error("Conciliacion.OtraSociedad", $"Este movimiento es de la sociedad {l.IdSociedad}.", registro,
                "Solo se contabilizan los movimientos de la sociedad activa.", $"Cambia a la sociedad {l.IdSociedad} para contabilizarlo."));
            return Fin();
        }
        if (l.LoteRevertido)
        {
            errores.Add(new Error("Conciliacion.LoteRevertido", "El extracto de este movimiento está revertido.", registro,
                "Los movimientos de un lote revertido no cuentan.", "Vuelve a importar el fichero si el movimiento es bueno."));
            return Fin();
        }

        // Ya contabilizada: por ProContaVF (se regenera) o fuera, en el ERP (no se toca).
        var regenera = false;
        if (l.IdAsiento is int actual)
        {
            if (l.AsientoEsDeLaLinea) regenera = true;
            else
            {
                errores.Add(new Error("Conciliacion.ContabilizadaFuera", $"Este movimiento ya está contabilizado en el asiento {actual}, que no generó ProContaVF.", registro,
                    "Se contabilizó en el ERP (o en ContaSol) y ese asiento no se regenera desde aquí.",
                    "Si hay que corregirlo, hazlo desde el Mayor con una plantilla de corrección o un asiento manual."));
                return Fin();
            }
        }

        // Cuenta bancaria y su 572.
        if (l.IdCuentaBancaria is null)
        {
            errores.Add(new Error("Conciliacion.SinCuentaBancaria", "El extracto de este movimiento no está asociado a ninguna cuenta bancaria de la sociedad.", registro,
                "Sin cuenta bancaria no sé a qué cuenta 572 va el movimiento.", "Asocia la cuenta en Bancos y vuelve a intentarlo."));
            return Fin(regenera: regenera);
        }

        var fechaMovimiento = FechaMovimiento(l);
        var ejercicioMovimiento = fechaMovimiento.Year;
        if (!ejercicios.TryGetValue(ejercicioMovimiento, out var ej))
            ejercicios[ejercicioMovimiento] = ej = await EjercicioAsync(idSociedad, ejercicioMovimiento);
        if (ej is null)
        {
            errores.Add(new Error("Conciliacion.SinEjercicio", $"La sociedad {idSociedad} no tiene el ejercicio {ejercicioMovimiento}.", registro,
                "El asiento va al ejercicio de la fecha del movimiento.", $"Da de alta el ejercicio {ejercicioMovimiento} en Parámetros y tablas › Ejercicios."));
            return Fin(regenera: regenera);
        }
        if (ej.Cerrado)
        {
            errores.Add(new Error("Conciliacion.EjercicioCerrado", $"El ejercicio {ej.Contexto.Ejercicio} está cerrado.", registro,
                "En un ejercicio cerrado no se generan asientos.", "Contabiliza el movimiento con un asiento manual en el ejercicio abierto, si procede."));
            return Fin(ej.Contexto, regenera: regenera);
        }
        var ctx = ej.Contexto;

        var banco = await IdentidadBancoAsync(l.IdCuentaBancaria.Value, fechaMovimiento);
        var cuentaBanco = banco is int ib ? await CuentaAsync(ctx, ib) : null;
        if (banco is null)
            errores.Add(new Error("Conciliacion.BancoSinCuenta", $"La cuenta bancaria {l.Ccc} no tiene cuenta contable (572).", registro,
                "La cuenta 572 de cada cuenta bancaria se configura en Bancos.", "Asígnale su cuenta 572 en Bancos y vuelve a intentarlo."));
        else if (cuentaBanco is null)
            errores.Add(new Error("Conciliacion.BancoNoEstaEnEjercicio", $"La cuenta contable de la cuenta bancaria {l.Ccc} no existe en el ejercicio {ctx.Ejercicio}.", registro,
                "La 572 del banco tiene que estar dada de alta en el ejercicio del movimiento.", $"Dala de alta en el plan de cuentas del ejercicio {ctx.Ejercicio}."));

        // Imputaciones: sin ninguna no hay asiento (no se usa cuenta puente).
        var imputaciones = new List<ImputacionConciliada>();
        foreach (var i in l.Imputaciones)
        {
            var cuenta = await CuentaAsync(ctx, i.IdIdentidad);
            if (cuenta is null)
            {
                var ultima = await UltimoCodigoAsync(i.IdIdentidad);
                errores.Add(new Error("Conciliacion.CuentaNoEstaEnEjercicio",
                    $"La cuenta asignada{(ultima is null ? "" : $" ({ultima})")} no existe en el ejercicio {ctx.Ejercicio}.", registro,
                    "Las cuentas del asiento tienen que estar dadas de alta en el ejercicio del movimiento.",
                    $"Dala de alta en el plan de cuentas del ejercicio {ctx.Ejercicio} o asigna otra cuenta al movimiento."));
            }
            imputaciones.Add(new ImputacionConciliada(i.IdIdentidad, cuenta?.Codigo, cuenta?.Nombre, i.ImporteFirmado, i.Concepto, i.IdCentro ?? cuenta?.IdCentro));
        }
        if (imputaciones.Count == 0)
            errores.Add(new Error("Conciliacion.SinCuenta", "Este movimiento no tiene cuenta asignada.", registro,
                "Sin cuenta de contrapartida no se genera el asiento: no se usa ninguna cuenta puente.", "Asígnale una cuenta y vuelve a contabilizarlo."));
        else
        {
            var esperado = l.Sentido == Cargo ? l.Importe : -l.Importe;
            var reparto = imputaciones.Sum(i => i.ImporteFirmado);
            if (reparto != esperado)
                errores.Add(new Error("Conciliacion.RepartoDescuadrado",
                    $"Las cuentas asignadas suman {Math.Abs(reparto).ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} € y el movimiento es de {l.Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €.",
                    registro, reparto == -esperado ? "El reparto tiene el signo cambiado (cargo y abono intercambiados)." : "El reparto entre cuentas no suma el importe del movimiento.",
                    "Corrige el reparto del movimiento entre sus cuentas."));
        }
        if (errores.Count > 0) return Fin(ctx, cuentaBanco?.Codigo, imp: imputaciones, regenera: regenera);

        // La plantilla da los apuntes (un registro por imputación, agrupando el banco); la fecha la fija la conciliación.
        // Fecha (norma43/conciliacion 2026-11-13 09:45): la del movimiento aunque sea de un trimestre cerrado, porque el asiento de banco no
        // toca el IVA ni las retenciones; solo si lleva una cuenta fiscal del trimestre (p. ej. imputado a la 640) va al trimestre activo.
        var fecha = ReglasAsiento.AfectaTrimestreFiscal(imputaciones.Select(i => i.Cuenta)) is null
            ? fechaMovimiento
            : TrimestreContable.FechaAsiento(fechaMovimiento, ctx.Ejercicio, TrimestreContable.Activo(ej.TrimestreActivo, ctx.Ejercicio, hoy));
        if (fecha != fechaMovimiento) avisos.Add($"El movimiento es de un trimestre cerrado: el asiento va al {fecha:dd/MM/yyyy}, primer día del trimestre activo.");
        if (regenera) avisos.Add($"Ya está contabilizado en el asiento {l.IdAsiento}: contabilizarlo otra vez lo sustituye.");
        var banco572 = cuentaBanco!;
        var registros = imputaciones.Select((i, n) => new RegistroProceso($"{l.Id}.{n + 1}", fechaMovimiento, new Dictionary<string, object?>
        {
            ["CuentaBanco"] = banco572.Codigo,
            ["NombreBanco"] = banco572.Nombre,
            ["Contrapartida"] = i.Cuenta,
            ["NombreContrapartida"] = i.NombreCuenta,
            ["Importe"] = i.ImporteFirmado,
            ["Sentido"] = l.Sentido,
            ["Concepto"] = ConceptoApunte(l, i),
            ["Centro"] = i.IdCentroCoste,
            ["Documento"] = DocumentoBanco(l.Documento),
        })).ToList();
        var entorno = new EntornoSociedad(ctx, TrimestreActivo: ej.TrimestreActivo);
        var propuesta = MotorPlantillas.Proponer(plantilla, new OrigenProceso(ClaveProceso, registros), null, entorno, fecha);
        if (!propuesta.EsCorrecto)
        {
            errores.AddRange(propuesta.Errores);
            return Fin(ctx, banco572.Codigo, imp: imputaciones, regenera: regenera);
        }
        if (propuesta.Valor.Asientos.Count != 1)
        {
            errores.Add(new Error("Conciliacion.PlantillaNoValida", $"La plantilla «{plantilla.Nombre}» genera {propuesta.Valor.Asientos.Count} asientos para un movimiento.", registro,
                "Cada movimiento se contabiliza en un único asiento.", "Corrige la plantilla en el mantenimiento de plantillas (un solo número de asiento y agrupar por cuenta)."));
            return Fin(ctx, banco572.Codigo, imp: imputaciones, regenera: regenera);
        }
        var documento = DocumentoBanco(l.Documento);
        var asiento = propuesta.Valor.Asientos[0] with
        {
            Descripcion = DescripcionAsiento(l),
            Apuntes = propuesta.Valor.Asientos[0].Apuntes.Select(a => a with { Documento = a.Documento ?? documento }).ToList(),
        };
        if (ErrorBanco(asiento, banco572.Codigo, l.Sentido == Abono ? l.Importe : -l.Importe, registro) is { } eb)
        {
            errores.Add(eb);
            return Fin(ctx, banco572.Codigo, imp: imputaciones, regenera: regenera);
        }
        return Fin(ctx, banco572.Codigo, asiento, imputaciones, regenera);
    }

    /// <summary>
    /// Fecha contable del movimiento: la fecha valor, como hacía el ERP (en 589 de sus 6.587 asientos de banco solo coincidía con ella y en 9
    /// solo con la de operación); si la fecha valor es de un año anterior al de la operación (un cargo del 5 de enero con valor del 29 de
    /// diciembre), la de operación, para que el movimiento quede en el ejercicio de su extracto.
    /// </summary>
    public static DateTime FechaMovimiento(DateTime fechaOperacion, DateTime fechaValor) =>
        fechaValor.Year < fechaOperacion.Year ? fechaOperacion.Date : fechaValor.Date;

    private static DateTime FechaMovimiento(LineaLeida l) => FechaMovimiento(l.FechaOperacion, l.FechaValor);

    /// <summary>Descripción del asiento: el concepto completo del banco (o un texto genérico si viene vacío).</summary>
    internal static string DescripcionAsiento(LineaLeida l)
    {
        var texto = string.IsNullOrWhiteSpace(l.Concepto) ? (l.Sentido == Cargo ? "Cargo bancario" : "Abono bancario") : l.Concepto.Trim();
        return texto.Length <= ReglasAsiento.LongitudDescripcion ? texto : texto[..ReglasAsiento.LongitudDescripcion];
    }

    /// <summary>Documento del banco para los apuntes (el número de recibo en las remesas); nada si viene vacío o a ceros.</summary>
    internal static string? DocumentoBanco(string? documento) =>
        string.IsNullOrWhiteSpace(documento) || documento.Trim().All(c => c == '0') ? null : documento.Trim();

    /// <summary>Texto del banco de una imputación (campo Concepto de la plantilla): el suyo, si no el concepto completo del movimiento.</summary>
    private static string ConceptoApunte(LineaLeida l, ImputacionConciliada i) =>
        !string.IsNullOrWhiteSpace(i.Concepto) ? i.Concepto.Trim() : DescripcionAsiento(l);

    /// <summary>
    /// El asiento lleva el movimiento entero en la cuenta del banco: la suma de sus apuntes (Debe − Haber) es el importe del movimiento con
    /// el signo del banco. Así una propuesta corregida a mano no puede contabilizar otro importe ni otro banco.
    /// </summary>
    internal static Error? ErrorBanco(AsientoPropuesto asiento, string cuentaBanco, decimal importeBanco, string registro)
    {
        var neto = asiento.Apuntes.Where(a => a.Cuenta == cuentaBanco).Sum(a => a.Debe - a.Haber);
        return neto == importeBanco ? null
            : new Error("Conciliacion.BancoNoCuadra", $"El asiento no lleva el movimiento entero en la cuenta del banco {cuentaBanco}.", registro,
                $"El banco suma {neto.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} € y el movimiento es de {importeBanco.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €.",
                "Deja en el asiento el importe del movimiento en la cuenta del banco y reparte solo la contrapartida.");
    }

    private static PropuestaLineaN43 SinLinea(int id) =>
        new(id, default, default, "", 0m, null, null, null, null, [],
            [new Error("Conciliacion.NoExiste", $"No encuentro el movimiento {id}.", $"Movimiento {id}", "Se ha borrado o el extracto se ha revertido.", "Vuelve a cargar la lista de movimientos.")], []);

    // ---------- contabilizar ----------

    /// <summary>Propone y contabiliza las líneas indicadas; las que no se pueden contabilizar quedan en <see cref="ResultadoConciliacionN43.Rechazadas"/>.</summary>
    public async Task<ResultadoConciliacionN43> ContabilizarLineasAsync(string idSociedad, IReadOnlyCollection<int> idsLineas, Actor actor) =>
        await ContabilizarAsync(await ProponerAsync(idSociedad, idsLineas), actor);

    /// <summary>
    /// Contabiliza las propuestas (tal cual o con los apuntes corregidos): cada línea en su transacción, con su asiento como lote automático
    /// propio y su fila en ContabilizacionesLineaExtracto. Si una falla, las demás siguen. Antes de grabar se vuelve a comprobar que la línea
    /// sigue igual (sin contabilizar fuera, mismo importe) y que el asiento lleva el movimiento entero en la cuenta del banco.
    /// </summary>
    public async Task<ResultadoConciliacionN43> ContabilizarAsync(IReadOnlyList<PropuestaLineaN43> propuestas, Actor actor)
    {
        ArgumentNullException.ThrowIfNull(propuestas);
        var hechas = new Dictionary<int, int>();
        var rechazadas = new Dictionary<int, IReadOnlyList<Error>>();
        var actuales = await LeerLineasAsync(propuestas.Select(p => p.IdLineaExtractoBancario).ToList());
        foreach (var p in propuestas)
        {
            if (!p.Contabilizable)
            {
                rechazadas[p.IdLineaExtractoBancario] = p.Errores.Count > 0 ? p.Errores
                    : [new Error("Conciliacion.SinPropuesta", "No hay asiento propuesto para este movimiento.", p.Rotulo, null, "Vuelve a proponer el asiento.")];
                continue;
            }
            if (!actuales.TryGetValue(p.IdLineaExtractoBancario, out var l) || l.LoteRevertido || l.Importe != p.Importe || l.Sentido != p.Sentido
                || (l.IdAsiento is not null && !l.AsientoEsDeLaLinea))
            {
                rechazadas[p.IdLineaExtractoBancario] =
                [
                    new Error("Conciliacion.HaCambiado", "El movimiento ha cambiado desde que se propuso su asiento.", p.Rotulo,
                        "Otra persona lo ha contabilizado, revertido o modificado.", "Vuelve a proponer el asiento."),
                ];
                continue;
            }
            var r = await GrabarAsync(p, actor);
            if (r.EsCorrecto) hechas[p.IdLineaExtractoBancario] = r.Valor;
            else rechazadas[p.IdLineaExtractoBancario] = r.Errores;
        }
        return new ResultadoConciliacionN43(hechas, rechazadas);
    }

    private async Task<Resultado<int>> GrabarAsync(PropuestaLineaN43 p, Actor actor)
    {
        var propuesta = p.Asiento!;
        if (ErrorBanco(propuesta, p.CuentaBanco ?? "", p.ImporteBanco, p.Rotulo) is { } eb) return Resultado<int>.Fallo(eb);
        var asiento = new Asiento(p.Contexto!, propuesta.Fecha, propuesta.Descripcion) { IdPlantilla = p.IdPlantilla };
        foreach (var x in propuesta.Apuntes)
        {
            var apunte = x.AlDebe ? Apunte.AlDebe(x.Cuenta, x.Importe, x.Concepto) : Apunte.AlHaber(x.Cuenta, x.Importe, x.Concepto);
            apunte.NumeroDocumento = x.Documento;
            apunte.IdCentroCoste = x.IdCentroCoste;
            asiento.Apuntes.Add(apunte);
        }
        var id = p.IdLineaExtractoBancario;
        var motivo = p.Regenera ? $"Volver a contabilizar el movimiento bancario {id}" : $"Contabilizar el movimiento bancario {id}";
        var r = await motor.ReemplazarLoteAsync(p.Contexto!, Lote(id), [asiento], actor, motivo, new TrabajoLote(
            Despues: async (s, ids) =>
            {
                await s.EjecutarAsync("DELETE FROM ContabilizacionesLineaExtracto WHERE IdLineaExtractoBancario = ?", id);
                await s.EjecutarAsync(
                    "INSERT INTO ContabilizacionesLineaExtracto (IdLineaExtractoBancario, IdAsiento, Estado, VersionProceso, FechaHora, Operador) VALUES (?, ?, ?, ?, ?, ?)",
                    id, ids[0], EstadoContabilizada, VersionProceso, _reloj.Ahora, actor.Nombre);
                return Resultado.Correcto();
            }));
        return r.EsCorrecto ? Resultado<int>.Correcto(r.Valor[0]) : Resultado<int>.Fallo(r.Errores);
    }

    // ---------- deshacer ----------

    /// <summary>
    /// Deshace la contabilización de las líneas: borra su asiento (con el motor, que lo registra con el motivo) y su fila de
    /// ContabilizacionesLineaExtracto; la línea vuelve a estar pendiente. Solo las contabilizadas por ProContaVF y si el trimestre del asiento
    /// está abierto (si no, el motor lo rechaza y lo dice).
    /// </summary>
    public async Task<ResultadoConciliacionN43> DeshacerAsync(string idSociedad, IReadOnlyCollection<int> idsLineas, Actor actor, string motivo)
    {
        ArgumentNullException.ThrowIfNull(idsLineas);
        var hechas = new Dictionary<int, int>();
        var rechazadas = new Dictionary<int, IReadOnlyList<Error>>();
        if (string.IsNullOrWhiteSpace(motivo))
        {
            foreach (var id in idsLineas)
                rechazadas[id] = [new Error("Conciliacion.SinMotivo", "Falta el motivo para deshacer la contabilización.", $"Movimiento {id}",
                    "Borrar un asiento queda registrado con su motivo.", "Escribe por qué deshaces la contabilización.")];
            return new ResultadoConciliacionN43(hechas, rechazadas);
        }
        var lineas = await LeerLineasAsync(idsLineas);
        foreach (var id in idsLineas)
        {
            if (!lineas.TryGetValue(id, out var l) || !string.Equals(l.IdSociedad, idSociedad, StringComparison.OrdinalIgnoreCase))
            {
                rechazadas[id] = SinLinea(id).Errores;
                continue;
            }
            var registro = $"Movimiento del {l.FechaOperacion:dd/MM/yyyy} de {l.Importe.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} €";
            if (l.IdAsiento is not int actual)
            {
                rechazadas[id] = [new Error("Conciliacion.NoContabilizada", "Este movimiento no está contabilizado.", registro, null, "No hay nada que deshacer.")];
                continue;
            }
            if (!l.AsientoEsDeLaLinea || l.IdSociedadEjercicioAsiento is not int idEj || l.EjercicioAsiento is not int anio)
            {
                rechazadas[id] = [new Error("Conciliacion.ContabilizadaFuera", $"El asiento {actual} de este movimiento no lo generó ProContaVF.", registro,
                    "Se contabilizó en el ERP (o en ContaSol) y ese asiento no se borra desde aquí.",
                    "Si hay que corregirlo, hazlo desde el Mayor con una plantilla de corrección o un asiento manual.")];
                continue;
            }
            var ctx = new ContextoSociedadEjercicio(l.IdSociedad, idEj, anio);
            var r = await motor.EliminarLoteAsync(ctx, Lote(id), actor, motivo.Trim(), new TrabajoLote(
                Despues: async (s, _) =>
                {
                    await s.EjecutarAsync("DELETE FROM ContabilizacionesLineaExtracto WHERE IdLineaExtractoBancario = ?", id);
                    return Resultado.Correcto();
                }));
            if (r.EsCorrecto) hechas[id] = actual;
            else rechazadas[id] = r.Errores;
        }
        return new ResultadoConciliacionN43(hechas, rechazadas);
    }

    // ---------- lecturas ----------

    internal sealed record ImputacionLeida(int IdIdentidad, decimal ImporteFirmado, string? Concepto, int? IdCentro);

    internal sealed record LineaLeida(
        int Id, string IdSociedad, bool LoteRevertido, int? IdCuentaBancaria, string Ccc, DateTime FechaOperacion, DateTime FechaValor, string Sentido, decimal Importe,
        string? Documento, string? Concepto, int? IdAsiento, bool AsientoEsDeLaLinea, int? IdSociedadEjercicioAsiento, int? EjercicioAsiento,
        IReadOnlyList<ImputacionLeida> Imputaciones);

    private sealed record EjercicioConciliacion(ContextoSociedadEjercicio Contexto, bool Cerrado, int? TrimestreActivo);

    private sealed record CuentaLeida(string Codigo, string Nombre, int? IdCentro);

    private Task<Dictionary<int, LineaLeida>> LeerLineasAsync(IReadOnlyCollection<int> ids) => bd.EjecutarAsync(async s =>
    {
        var r = new Dictionary<int, LineaLeida>();
        foreach (var bloque in ids.Distinct().Chunk(200))
        {
            var lista = string.Join(",", bloque.Select(i => i.ToString(CultureInfo.InvariantCulture)));
            var filas = await s.ConsultarAsync(
                "SELECT l.IdLineaExtractoBancario, lo.IdSociedad, lo.Estado, e.IdCuentaBancaria, e.Entidad, e.Oficina, e.Cuenta, l.FechaOperacion, l.FechaValor, l.Sentido, " +
                "l.Importe, l.Documento, l.ConceptoCompleto FROM (LineasExtractoBancario AS l INNER JOIN ExtractosBancarios AS e ON l.IdExtractoBancario = e.IdExtractoBancario) " +
                $"INNER JOIN LotesImportacionBancaria AS lo ON e.IdLoteImportacionBancaria = lo.IdLoteImportacionBancaria WHERE l.IdLineaExtractoBancario IN ({lista})",
                f => (Id: f.Entero("IdLineaExtractoBancario"), Sociedad: (f.Texto("IdSociedad") ?? "").Trim(), Revertido: f.Texto("Estado") == ServicioImportacionN43.EstadoRevertido,
                    Cuenta: f.EnteroNulo("IdCuentaBancaria"), Ccc: $"{f.Texto("Entidad")} {f.Texto("Oficina")} {f.Texto("Cuenta")}",
                    Operacion: f.Fecha("FechaOperacion").Date, Valor: f.Fecha("FechaValor").Date, Sentido: (f.Texto("Sentido") ?? "").Trim(), Importe: f.Decimal("Importe"),
                    Documento: Limpio(f.Texto("Documento")), Concepto: Limpio(f.Texto("ConceptoCompleto"))));
            var imputaciones = (await s.ConsultarAsync(
                    "SELECT IdLineaExtractoBancario, IdIdentidadContable, ImporteFirmado, Concepto, IdCentroCoste FROM ImputacionesLineaExtractoBancario " +
                    $"WHERE Estado <> 'DESCARTADA' AND IdLineaExtractoBancario IN ({lista}) ORDER BY IdImputacionLineaExtractoBancario",
                    f => (Linea: f.Entero("IdLineaExtractoBancario"), Imp: new ImputacionLeida(f.Entero("IdIdentidadContable"), f.Decimal("ImporteFirmado"),
                        Limpio(f.Texto("Concepto")), f.EnteroNulo("IdCentroCoste")))))
                .ToLookup(x => x.Linea, x => x.Imp);
            var contabilizadas = (await s.ConsultarAsync(
                    "SELECT c.IdLineaExtractoBancario, c.IdAsiento, a.EsAutomatico, a.MotorPropietario, a.CodigoProceso, a.ClaveEjecucion, a.IdSociedadEjercicio, se.Ejercicio " +
                    "FROM (ContabilizacionesLineaExtracto AS c LEFT JOIN Asientos AS a ON c.IdAsiento = a.IdAsiento) " +
                    "LEFT JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                    $"WHERE c.IdLineaExtractoBancario IN ({lista}) ORDER BY c.IdContabilizacionLineaExtracto DESC",
                    f => (Linea: f.Entero("IdLineaExtractoBancario"), Asiento: f.Entero("IdAsiento"),
                        Propio: !f.EsNulo("EsAutomatico") && f.Logico("EsAutomatico") && f.Texto("MotorPropietario") == MotorPropietario && f.Texto("CodigoProceso") == CodigoProceso
                            && f.Texto("ClaveEjecucion") == f.Entero("IdLineaExtractoBancario").ToString(CultureInfo.InvariantCulture),
                        Existe: !f.EsNulo("EsAutomatico"), Ej: f.EnteroNulo("IdSociedadEjercicio"),
                        Anio: int.TryParse((f.Texto("Ejercicio") ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var a) ? a : (int?)null)))
                .GroupBy(x => x.Linea).ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Existe).First());
            foreach (var f in filas)
            {
                var c = contabilizadas.GetValueOrDefault(f.Id);
                r[f.Id] = new LineaLeida(f.Id, f.Sociedad, f.Revertido, f.Cuenta, f.Ccc, f.Operacion, f.Valor, f.Sentido, f.Importe, f.Documento, f.Concepto,
                    c.Linea == 0 || !c.Existe ? null : c.Asiento, c.Propio, c.Ej, c.Anio, imputaciones[f.Id].ToList());
            }
        }
        return r;
    });

    private Task<EjercicioConciliacion?> EjercicioAsync(string idSociedad, int ejercicio) => bd.EjecutarAsync(async s =>
    {
        var f = await s.ConsultarPrimeroAsync(
            "SELECT IdSociedadEjercicio, EjercicioCerrado, TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedad = ? AND Ejercicio = ?",
            x => (Id: x.Entero("IdSociedadEjercicio"), Cerrado: !x.EsNulo("EjercicioCerrado") && x.Logico("EjercicioCerrado"), Trimestre: x.EnteroNulo("TrimestreActivo")),
            idSociedad, ejercicio.ToString(CultureInfo.InvariantCulture));
        return f.Id == 0 ? null : new EjercicioConciliacion(new ContextoSociedadEjercicio(idSociedad, f.Id, ejercicio), f.Cerrado, f.Trimestre);
    });

    /// <summary>Identidad contable de la 572 configurada para la cuenta bancaria en la fecha del movimiento (o la vigente).</summary>
    private Task<int?> IdentidadBancoAsync(int idCuentaBancaria, DateTime fecha) => bd.EjecutarAsync(async s =>
    {
        var filas = await s.ConsultarAsync(
            "SELECT IdIdentidadContableBanco, FechaDesde, FechaHasta FROM ConfiguracionesContablesCuentaBancaria WHERE IdCuentaBancaria = ? ORDER BY FechaDesde DESC",
            f => (Id: f.Entero("IdIdentidadContableBanco"), Desde: f.FechaNula("FechaDesde"), Hasta: f.FechaNula("FechaHasta")), idCuentaBancaria);
        var enFecha = filas.FirstOrDefault(x => (x.Desde is null || x.Desde.Value.Date <= fecha) && (x.Hasta is null || x.Hasta.Value.Date >= fecha));
        if (enFecha.Id != 0) return (int?)enFecha.Id;
        var vigente = filas.FirstOrDefault(x => x.Hasta is null);
        return vigente.Id != 0 ? vigente.Id : null;
    });

    private Task<CuentaLeida?> CuentaAsync(ContextoSociedadEjercicio ctx, int idIdentidad) => bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
        "SELECT Codigo, Nombre, IdCentroCostePredeterminado FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ? ORDER BY IdCuentaContable",
        f => (CuentaLeida?)new CuentaLeida((f.Texto("Codigo") ?? "").Trim(), (f.Texto("Nombre") ?? "").Trim(), f.EnteroNulo("IdCentroCostePredeterminado")),
        ctx.IdSociedadEjercicio, idIdentidad));

    private Task<string?> UltimoCodigoAsync(int idIdentidad) => bd.EjecutarAsync(async s =>
        (await s.EscalarAsync("SELECT TOP 1 Codigo FROM CuentasContables WHERE IdIdentidadContable = ? ORDER BY IdSociedadEjercicio DESC", idIdentidad)) is string c ? c.Trim() : null);

    private static string? Limpio(string? texto) => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}
