using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Terceros.Roles;

/// <summary>Cambio de la subcuenta de un rol detectado al guardar la ficha: de qué identidad contable a cuál.</summary>
public sealed record SubcuentaCambiada(ClaseCuentaRol Clase, int IdRol, string IdSociedad, int IdIdentidadAntigua, int IdIdentidadNueva);

/// <summary>Asiento que no se puede pasar a la subcuenta nueva, con el motivo para el operador.</summary>
public sealed record AsientoNoMovible(int IdAsiento, int Ejercicio, DateTime Fecha, string Motivo);

/// <summary>Lo que se pasa en un ejercicio abierto: los apuntes de la subcuenta antigua y los asientos que no se pueden tocar.</summary>
public sealed record EjercicioCambioSubcuenta(ContextoSociedadEjercicio Contexto, string CodigoAntiguo, string CodigoNuevo, bool CrearNueva,
    IReadOnlyList<int> Apuntes, IReadOnlyList<int> Asientos, IReadOnlyList<AsientoNoMovible> NoMovibles, IReadOnlyList<int> FacturasCompra);

/// <summary>Propuesta del cambio de subcuenta de un rol: qué se pasa, qué no y por qué, para enseñarla antes de confirmar.</summary>
public sealed record PropuestaCambioSubcuenta(SubcuentaCambiada Cambio, string CodigoAntiguo, string CodigoNuevo, bool Compartida,
    IReadOnlyList<EjercicioCambioSubcuenta> Ejercicios, IReadOnlyList<int> LineasReimputar, IReadOnlyList<int> LineasSinReimputar, IReadOnlyList<string> Avisos)
{
    public int NumeroApuntes => Ejercicios.Sum(e => e.Apuntes.Count);
    public int NumeroAsientos => Ejercicios.Sum(e => e.Asientos.Count);
    public IReadOnlyList<AsientoNoMovible> NoMovibles => Ejercicios.SelectMany(e => e.NoMovibles).ToList();
    public int NumeroFacturasCompra => Ejercicios.Sum(e => e.FacturasCompra.Count);

    /// <summary>¿Hay algo que pasar o que explicar? Si no, el cambio solo vale para lo nuevo y no se pregunta.</summary>
    public bool HayQueDecidir => NumeroApuntes > 0 || LineasReimputar.Count > 0 || NumeroFacturasCompra > 0 || NoMovibles.Count > 0;

    /// <summary>Resumen de la pregunta: cuánto se pasa y de qué ejercicios.</summary>
    public string Resumen
    {
        get
        {
            var partes = new List<string>();
            var ejercicios = Ejercicios.Where(e => e.Apuntes.Count > 0).Select(e => e.Contexto.Ejercicio.ToString(CultureInfo.InvariantCulture)).ToList();
            if (NumeroApuntes > 0)
                partes.Add($"{Plural(NumeroApuntes, "apunte", "apuntes")} de {Plural(NumeroAsientos, "asiento", "asientos")} de {Unir(ejercicios)}");
            if (LineasReimputar.Count > 0) partes.Add(Plural(LineasReimputar.Count, "movimiento N43 imputado", "movimientos N43 imputados"));
            if (NumeroFacturasCompra > 0) partes.Add(Plural(NumeroFacturasCompra, "factura de compra", "facturas de compra"));
            return partes.Count == 0 ? "No hay apuntes que se puedan pasar." : $"Se pasarían a la {CodigoNuevo}: {Unir(partes)}.";
        }
    }

    /// <summary>Detalle para el diálogo: alcance, asientos que no se pasan y avisos.</summary>
    public IReadOnlyList<string> Detalles
    {
        get
        {
            var d = new List<string>
            {
                Compartida
                    ? $"La {CodigoAntiguo} la usa también otro tercero: solo se pasan los apuntes de las facturas de este tercero, de sus cobros o pagos enlazados y los que llevan su código de cliente o proveedor."
                    : $"La {CodigoAntiguo} es solo de este tercero: se pasan todos sus apuntes de los ejercicios abiertos."
            };
            foreach (var e in Ejercicios.Where(e => e.CrearNueva))
                d.Add($"La {e.CodigoNuevo} no existe en {e.Contexto.Ejercicio}: la crearé con el mismo código.");
            foreach (var n in NoMovibles)
                d.Add($"No se pasa el asiento {n.IdAsiento} del {n.Fecha:dd/MM/yyyy}: {n.Motivo}");
            if (LineasSinReimputar.Count > 0)
                d.Add($"{Plural(LineasSinReimputar.Count, "movimiento N43 sigue imputado", "movimientos N43 siguen imputados")} a la {CodigoAntiguo} porque su asiento no se pasa.");
            d.AddRange(Avisos);
            return d;
        }
    }

    internal static string Plural(int n, string uno, string varios) => n == 1 ? $"1 {uno}" : $"{n} {varios}";

    internal static string Unir(IReadOnlyList<string> partes) => partes.Count switch
    {
        0 => "",
        1 => partes[0],
        _ => string.Join(", ", partes.Take(partes.Count - 1)) + " y " + partes[^1]
    };
}

/// <summary>Lo que ha hecho el cambio, con los textos para el aviso de la ficha.</summary>
public sealed record ResultadoCambioSubcuenta(int Apuntes, int Asientos, int Lineas, int FacturasCompra, IReadOnlyList<string> Textos);

/// <summary>
/// Cambiar la subcuenta del tercero (terceros/roles/cambio-subcuenta): al guardar la ficha con otra subcuenta en el rol Cliente
/// (o su retención) o Proveedor (proveedor o acreedor), pasa a la nueva, en los ejercicios abiertos de la sociedad, los apuntes
/// de la antigua: todos si la antigua es solo de este tercero, o los de sus facturas de venta y compra, sus asientos de banco
/// enlazados y los que llevan su código de cliente o proveedor si la comparte otro. También en un trimestre cerrado si el asiento
/// no lleva retención (4751) y la nueva es del mismo grupo 4XX (contabilidad/asientos 2026-11-14 23:30). Con ellos pasan las
/// imputaciones N43 de sus movimientos y la cuenta de proveedor de sus facturas de compra, en la misma transacción. Lo que no se
/// puede pasar se queda y se explica. Si la antigua era solo suya, al final se borra con la regla de los 5 años de los roles.
/// </summary>
public sealed class CambioSubcuentaTercero(IBaseDatos bd, ICatalogoCuentas cuentas, IMotorAsientos motor, IAsientosBancoEnlazados? banco = null, IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;
    private readonly IAsientosBancoEnlazados _banco = banco ?? SinAsientosBanco.Instancia;

    /// <summary>Clases de cuenta cuyos apuntes salen de facturas y cobros o pagos: las que se pasan al cambiar la subcuenta.</summary>
    public static bool SeTraslada(ClaseCuentaRol clase) =>
        clase is ClaseCuentaRol.Cliente or ClaseCuentaRol.Retencion or ClaseCuentaRol.Proveedor or ClaseCuentaRol.Acreedor;

    /// <summary>
    /// Subcuentas de rol que cambian de una a otra entre <paramref name="antes"/> (como está guardada) y <paramref name="ahora"/>,
    /// en la misma sociedad: las de los roles que ya existían con cuenta y siguen activos. Quitar o poner una cuenta no es un cambio.
    /// </summary>
    public static IReadOnlyList<SubcuentaCambiada> QueCambiar(Tercero? antes, Tercero ahora)
    {
        if (antes is null || antes.EsNuevo || antes.CuentasRoles is not { } a || ahora.CuentasRoles is not { } n
            || !string.Equals(a.IdSociedad, n.IdSociedad, StringComparison.OrdinalIgnoreCase)) return [];
        var r = new List<SubcuentaCambiada>();
        void Ver(ClaseCuentaRol clase, bool activo, int? idRol)
        {
            if (!activo || idRol is not int id || a.De(clase) is not int vieja || n.De(clase) is not int nueva || vieja == nueva) return;
            r.Add(new SubcuentaCambiada(clase, id, n.IdSociedad, vieja, nueva));
        }
        Ver(ClaseCuentaRol.Cliente, ahora.Roles.EsCliente, ahora.Roles.Cliente?.IdCliente);
        Ver(ClaseCuentaRol.Retencion, ahora.Roles.EsCliente, ahora.Roles.Cliente?.IdCliente);
        Ver(ClaseCuentaRol.Proveedor, ahora.Roles.EsProveedor, ahora.Roles.Proveedor?.IdProveedor);
        Ver(ClaseCuentaRol.Acreedor, ahora.Roles.EsProveedor, ahora.Roles.Proveedor?.IdProveedor);
        return r;
    }

    /// <summary>
    /// ¿Se puede pasar en un trimestre cerrado el apunte de <paramref name="antigua"/> a <paramref name="nueva"/> de un asiento con estas cuentas?
    /// Null si sí; si no, el motivo. Misma regla que el motor (contabilidad/asientos 2026-11-14 23:30): un asiento sin cuentas fiscales
    /// siempre; con ellas, solo el cambio a otra subcuenta del mismo grupo 4XX y sin retención (4751).
    /// </summary>
    public static string? BloqueoTrimestreCerrado(IReadOnlyCollection<string> cuentasAsiento, string antigua, string nueva)
    {
        var fiscal = ReglasAsiento.AfectaTrimestreFiscal(cuentasAsiento);
        if (fiscal is null) return null;
        if (cuentasAsiento.Any(c => c.Trim().StartsWith(ReglasAsiento.PrefijoRetenciones, StringComparison.Ordinal)))
            return "es de un trimestre cerrado y lleva retención (4751), que ya está en su declaración. Corrígelo con un asiento de reclasificación en el trimestre activo.";
        if (!ReglasAsiento.EsCambioDeSubcuentaDeTercero(antigua, nueva))
            return $"es de un trimestre cerrado, lleva la cuenta fiscal {fiscal} y la {nueva} no es del mismo grupo que la {antigua}. Corrígelo con un asiento de reclasificación en el trimestre activo.";
        return null;
    }

    /// <summary>Calcula qué se pasaría, sin cambiar nada.</summary>
    public async Task<Resultado<PropuestaCambioSubcuenta>> ProponerAsync(SubcuentaCambiada c)
    {
        if (!SeTraslada(c.Clase))
            return Resultado<PropuestaCambioSubcuenta>.Fallo(new Error("CambioSubcuenta.ClaseNoAdmitida", $"La {ServicioRoles.Describir(c.Clase)} no se traslada.", null));
        var ultimaAntigua = await cuentas.UltimaCuentaDeIdentidadAsync(c.IdIdentidadAntigua);
        var ultimaNueva = await cuentas.UltimaCuentaDeIdentidadAsync(c.IdIdentidadNueva);
        if (ultimaNueva is null)
            return Resultado<PropuestaCambioSubcuenta>.Fallo(new Error("CambioSubcuenta.SinCuentaNueva", "La subcuenta nueva no existe en ningún ejercicio.",
                $"Identidad {c.IdIdentidadNueva}", "Se ha borrado mientras editabas la ficha.", "Vuelve a elegir la subcuenta en la pestaña Roles."));
        var codigoAntiguo = ultimaAntigua?.Codigo.Trim() ?? "";
        var codigoNuevo = ultimaNueva.Codigo.Trim();

        return await bd.EjecutarAsync(async s =>
        {
            var compartida = await SubcuentasRoles.OtrosUsosAsync(s, new SubcuentasRoles.SubcuentaQuitada(c.Clase, c.IdRol, c.IdIdentidadAntigua)) > 0;
            var abiertos = await s.ConsultarAsync(
                "SELECT IdSociedadEjercicio, Ejercicio, TrimestreActivo FROM SociedadesEjercicios WHERE IdSociedad = ? AND EjercicioCerrado = False ORDER BY Ejercicio",
                f => (Contexto: new ContextoSociedadEjercicio(c.IdSociedad, f.Entero("IdSociedadEjercicio"), int.Parse((f.Texto("Ejercicio") ?? "0").Trim(), CultureInfo.InvariantCulture)),
                    Trimestre: f.EnteroNulo("TrimestreActivo")),
                c.IdSociedad);
            var deCliente = ServicioRoles.EsDeCliente(c.Clase);

            // Con la subcuenta compartida solo cuenta lo de este tercero: sus facturas, sus cobros o pagos enlazados y los apuntes con su código.
            HashSet<int>? permitidos = null;
            IReadOnlyList<MovimientoBancoTercero> movimientos;
            if (compartida)
            {
                var facturas = deCliente
                    ? await s.ConsultarAsync("SELECT IdFactura AS Id, IdAsientoContable, IdAsientoAnulado FROM Facturas WHERE IdCliente = ?",
                        f => (Id: f.Entero("Id"), A: f.EnteroNulo("IdAsientoContable"), B: f.EnteroNulo("IdAsientoAnulado")), c.IdRol)
                    : await s.ConsultarAsync("SELECT IdRegistro AS Id, IdAsientoContable, IdAsientoAnulado FROM FacturasCompra WHERE IdProveedor = ?",
                        f => (Id: f.Entero("Id"), A: f.EnteroNulo("IdAsientoContable"), B: f.EnteroNulo("IdAsientoAnulado")), c.IdRol);
                var ids = facturas.Select(f => f.Id).ToList();
                movimientos = await _banco.MovimientosDeSubcuentaAsync(c.IdIdentidadAntigua, deCliente ? ids : [], deCliente ? [] : ids);
                permitidos = [.. facturas.SelectMany(f => new[] { f.A, f.B }).OfType<int>(), .. movimientos.SelectMany(m => m.Asientos)];
            }
            else movimientos = await _banco.MovimientosDeSubcuentaAsync(c.IdIdentidadAntigua, null, null);

            var ejercicios = new List<EjercicioCambioSubcuenta>();
            var avisos = new List<string>();
            var movibles = new HashSet<int>();
            var bloqueados = new HashSet<int>();
            foreach (var (ctx, trimestre) in abiertos)
            {
                var vieja = await s.ConsultarPrimeroAsync("SELECT Codigo, SaldoInicial FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?",
                    f => (Codigo: (f.Texto("Codigo") ?? "").Trim(), Saldo: f.DecimalNulo("SaldoInicial") ?? 0m), ctx.IdSociedadEjercicio, c.IdIdentidadAntigua);
                if (string.IsNullOrEmpty(vieja.Codigo)) continue;
                var nueva = (await s.EscalarAsync("SELECT Codigo FROM CuentasContables WHERE IdSociedadEjercicio = ? AND IdIdentidadContable = ?",
                    ctx.IdSociedadEjercicio, c.IdIdentidadNueva) as string)?.Trim();
                var crear = string.IsNullOrEmpty(nueva);
                nueva = crear ? codigoNuevo : nueva!;

                var filas = await s.ConsultarAsync(
                    "SELECT ap.IdApunte, ap.IdAsiento, ap.IdCliente, ap.IdProveedor, a.FechaContable FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento " +
                    "WHERE a.IdSociedadEjercicio = ? AND ap.CuentaContable = ?",
                    f => (Apunte: f.Entero("IdApunte"), Asiento: f.Entero("IdAsiento"), Cliente: f.EnteroNulo("IdCliente"), Proveedor: f.EnteroNulo("IdProveedor"),
                        Fecha: f.Fecha("FechaContable").Date),
                    ctx.IdSociedadEjercicio, vieja.Codigo);
                if (permitidos is not null)
                    filas = filas.Where(f => permitidos.Contains(f.Asiento) || (deCliente ? f.Cliente : f.Proveedor) == c.IdRol).ToList();

                // Trimestre cerrado: solo los asientos que el motor deja cambiar (sin 4751 y misma subcuenta 4XX si llevan cuentas fiscales).
                var noMovibles = new List<AsientoNoMovible>();
                if (trimestre is >= 1 and <= 4)
                {
                    var inicio = TrimestreContable.Inicio(ctx.Ejercicio, trimestre.Value);
                    foreach (var g in filas.Where(f => f.Fecha < inicio).GroupBy(f => f.Asiento))
                    {
                        var cuentasAsiento = await s.ConsultarAsync("SELECT CuentaContable FROM Apuntes WHERE IdAsiento = ?", f => (f.Texto("CuentaContable") ?? "").Trim(), g.Key);
                        if (BloqueoTrimestreCerrado(cuentasAsiento, vieja.Codigo, nueva) is { } motivo)
                            noMovibles.Add(new AsientoNoMovible(g.Key, ctx.Ejercicio, g.First().Fecha, motivo));
                    }
                }
                var fuera = noMovibles.Select(n => n.IdAsiento).ToHashSet();
                var pasan = filas.Where(f => !fuera.Contains(f.Asiento)).ToList();
                movibles.UnionWith(pasan.Select(f => f.Asiento));
                bloqueados.UnionWith(fuera);

                // La cuenta de proveedor de sus facturas de compra del ejercicio: las no exportadas y las de asientos que pasan.
                var compras = new List<int>();
                if (!deCliente)
                {
                    var asientosQuePasan = pasan.Select(f => f.Asiento).ToHashSet();
                    compras.AddRange((await s.ConsultarAsync(
                            "SELECT IdRegistro, Exportada, IdAsientoContable FROM FacturasCompra WHERE IdSociedadEjercicio = ? AND IdProveedor = ? AND CuentaContableProveedor = ?",
                            f => (Id: f.Entero("IdRegistro"), Exportada: !f.EsNulo("Exportada") && f.Logico("Exportada"), Asiento: f.EnteroNulo("IdAsientoContable")),
                            ctx.IdSociedadEjercicio, c.IdRol, vieja.Codigo))
                        .Where(f => !f.Exportada || (f.Asiento is int a && asientosQuePasan.Contains(a))).Select(f => f.Id));
                }
                if (!compartida && vieja.Saldo != 0m)
                    avisos.Add($"La {vieja.Codigo} tiene un saldo inicial de {vieja.Saldo.ToString("N2", CultureInfo.GetCultureInfo("es-ES"))} € en {ctx.Ejercicio} que no sale de apuntes: no lo paso; si toca, llévalo a la {nueva} con un asiento.");
                if (filas.Count == 0 && compras.Count == 0) continue;
                ejercicios.Add(new EjercicioCambioSubcuenta(ctx, vieja.Codigo, nueva, crear && pasan.Count > 0,
                    pasan.Select(f => f.Apunte).Distinct().ToList(), pasan.Select(f => f.Asiento).Distinct().ToList(), noMovibles, compras));
            }

            // Un movimiento imputado a la antigua pasa con sus asientos: si alguno no se pasa (bloqueado o en un ejercicio cerrado), sigue como estaba.
            var imputados = movimientos.Where(m => m.ImputadoALaAntigua).ToList();
            var asientosAbiertos = new HashSet<int>();
            var deMovimientos = imputados.SelectMany(m => m.Asientos).Distinct().ToList();
            foreach (var bloque in deMovimientos.Chunk(200))
                asientosAbiertos.UnionWith(await s.ConsultarAsync(
                    $"SELECT a.IdAsiento FROM Asientos AS a INNER JOIN SociedadesEjercicios AS se ON a.IdSociedadEjercicio = se.IdSociedadEjercicio " +
                    $"WHERE se.EjercicioCerrado = False AND a.IdAsiento IN ({string.Join(",", bloque.Select(i => i.ToString(CultureInfo.InvariantCulture)))})",
                    f => f.Entero("IdAsiento")));
            var reimputar = imputados.Where(m => m.Asientos.All(a => asientosAbiertos.Contains(a) && !bloqueados.Contains(a))).Select(m => m.IdLinea).ToList();
            var sinReimputar = imputados.Select(m => m.IdLinea).Except(reimputar).ToList();

            return Resultado<PropuestaCambioSubcuenta>.Correcto(new PropuestaCambioSubcuenta(c, codigoAntiguo, codigoNuevo, compartida, ejercicios, reimputar, sinReimputar, avisos));
        });
    }

    /// <summary>
    /// Hace el cambio propuesto, ejercicio a ejercicio (cada uno en su transacción, con su registro de cambios): crea la subcuenta nueva si
    /// falta, pasa los apuntes con el motor y, en la misma transacción, las imputaciones N43 de los movimientos de esos asientos y la cuenta
    /// de las facturas de compra. Los movimientos sin asiento se reimputan al final. Si la antigua era solo de este tercero, después se
    /// borra si no tiene movimientos en los últimos 5 años (terceros/roles 2026-11-16 10:52).
    /// </summary>
    public async Task<ResultadoCambioSubcuenta> EjecutarAsync(PropuestaCambioSubcuenta p, string nombreTercero, Actor actor, ContextoSociedadEjercicio trabajo)
    {
        var c = p.Cambio;
        var textos = new List<string>();
        var motivo = Recortar($"Cambio de subcuenta del tercero {nombreTercero.Trim()}: {p.CodigoAntiguo} → {p.CodigoNuevo}");
        var pendientes = p.LineasReimputar.ToHashSet();
        var movimientos = pendientes.Count == 0 ? [] : await _banco.MovimientosDeSubcuentaAsync(c.IdIdentidadAntigua, null, null);
        int apuntes = 0, asientos = 0, lineas = 0, compras = 0;
        var hechos = new List<int>();
        var palabrasHechas = false;

        foreach (var e in p.Ejercicios)
        {
            if (e.CrearNueva)
            {
                var alta = await cuentas.AltaEnEjercicioAsync(e.Contexto, c.IdIdentidadNueva);
                if (!alta.EsCorrecto)
                {
                    textos.Add($"No he podido crear la {e.CodigoNuevo} en {e.Contexto.Ejercicio}, así que sus apuntes siguen en la {e.CodigoAntiguo}: {alta.Error!.Mensaje} {alta.Error.AccionCorrectora}".TrimEnd());
                    continue;
                }
            }
            var asientosEjercicio = e.Asientos.ToHashSet();
            var lineasEjercicio = movimientos.Where(m => pendientes.Contains(m.IdLinea) && m.Asientos.Count > 0 && m.Asientos.All(asientosEjercicio.Contains))
                .Select(m => m.IdLinea).ToList();
            var n = 0;
            var palabras = !p.Compartida && !palabrasHechas;
            async Task<Resultado> Despues(ISesionDatos s)
            {
                n = await _banco.ReimputarAsync(s, lineasEjercicio, c.IdIdentidadAntigua, c.IdIdentidadNueva, palabras);
                await PasarFacturasCompraAsync(s, e.FacturasCompra, e.CodigoNuevo);
                return Resultado.Correcto();
            }
            Resultado r;
            if (e.Apuntes.Count > 0)
                r = await motor.PasarApuntesDeTerceroAsync(e.Contexto, e.Apuntes, e.CodigoNuevo, actor, motivo, Despues);
            else
                r = await bd.EnTransaccionAsync(Despues);
            if (!r.EsCorrecto)
            {
                textos.Add($"No he pasado los apuntes de {e.Contexto.Ejercicio}: {r.Error!.Mensaje} {r.Error.AccionCorrectora}".TrimEnd());
                continue;
            }
            palabrasHechas |= palabras;
            pendientes.ExceptWith(lineasEjercicio);
            apuntes += e.Apuntes.Count;
            asientos += e.Asientos.Count;
            lineas += n;
            compras += e.FacturasCompra.Count;
            if (e.Apuntes.Count > 0) hechos.Add(e.Contexto.Ejercicio);
        }

        // Movimientos sin asiento (aún sin contabilizar): solo cambia su imputación.
        var sinAsiento = movimientos.Where(m => pendientes.Contains(m.IdLinea) && m.Asientos.Count == 0).Select(m => m.IdLinea).ToList();
        if (sinAsiento.Count > 0 || (!p.Compartida && !palabrasHechas))
        {
            var n = await bd.EnTransaccionAsync(async s => await _banco.ReimputarAsync(s, sinAsiento, c.IdIdentidadAntigua, c.IdIdentidadNueva, !p.Compartida && !palabrasHechas));
            lineas += n;
        }

        if (apuntes > 0)
            textos.Insert(0, $"He pasado {PropuestaCambioSubcuenta.Plural(apuntes, "apunte", "apuntes")} de {PropuestaCambioSubcuenta.Plural(asientos, "asiento", "asientos")} " +
                $"de la {p.CodigoAntiguo} a la {p.CodigoNuevo} ({PropuestaCambioSubcuenta.Unir(hechos.Select(h => h.ToString(CultureInfo.InvariantCulture)).ToList())}).");
        if (lineas > 0) textos.Add($"{PropuestaCambioSubcuenta.Plural(lineas, "movimiento N43 queda imputado", "movimientos N43 quedan imputados")} a la {p.CodigoNuevo}.");
        if (compras > 0) textos.Add($"{PropuestaCambioSubcuenta.Plural(compras, "factura de compra lleva", "facturas de compra llevan")} ya la {p.CodigoNuevo}.");
        if (p.NoMovibles.Count > 0)
            textos.Add($"{PropuestaCambioSubcuenta.Plural(p.NoMovibles.Count, "asiento sigue", "asientos siguen")} en la {p.CodigoAntiguo} (trimestre cerrado con retención o cuenta de otro grupo): " +
                string.Join(", ", p.NoMovibles.Select(x => x.IdAsiento.ToString(CultureInfo.InvariantCulture))) + ". Corrígelos con un asiento de reclasificación en el trimestre activo.");

        if (!p.Compartida)
        {
            var sub = new SubcuentasRoles(bd, cuentas, _reloj);
            textos.Add(await sub.EliminarAntiguaAsync(c.Clase, c.IdIdentidadAntigua, trabajo));
        }
        else textos.Add($"La {p.CodigoAntiguo} se conserva porque la usa otro tercero o rol.");
        return new ResultadoCambioSubcuenta(apuntes, asientos, lineas, compras, textos);
    }

    /// <summary>Compras guarda en cada factura la cuenta del proveedor con que se contabiliza: pasa a la nueva para que regenerar no vuelva a la antigua.</summary>
    private static async Task PasarFacturasCompraAsync(ISesionDatos s, IReadOnlyCollection<int> registros, string codigoNuevo)
    {
        foreach (var bloque in registros.Distinct().Chunk(200))
            await s.EjecutarAsync($"UPDATE FacturasCompra SET CuentaContableProveedor = ? WHERE IdRegistro IN ({string.Join(",", bloque.Select(i => i.ToString(CultureInfo.InvariantCulture)))})",
                codigoNuevo);
    }

    private static string Recortar(string texto) => texto.Length <= ReglasAsiento.LongitudDescripcion ? texto : texto[..ReglasAsiento.LongitudDescripcion];
}
