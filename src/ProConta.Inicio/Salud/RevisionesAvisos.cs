using static ProConta.Inicio.Salud.CalendarioFiscal;

namespace ProConta.Inicio.Salud;

/// <summary>Reglas comunes de los bancos en la salud.</summary>
internal static class ReglasBancos
{
    /// <summary>
    /// Un banco está en uso si su cuenta está activa, tiene cuenta contable y, en el ejercicio de trabajo, tiene apuntes o saldo:
    /// así una cuenta cerrada hace años con extractos viejos no da avisos.
    /// </summary>
    public static bool EnUso(BancoSalud b) => b.Activa && b.Cuenta is { Length: > 0 } && (b.ApuntesEnEjercicio || b.SaldoContableCorte != 0);

    public static readonly DestinoSalud Lineas = new(PantallaSalud.LineasN43, "Bancos › Líneas N43");
    public static readonly DestinoSalud Cuentas = new(PantallaSalud.Bancos, "Bancos › Cuentas bancarias");
}

/// <summary>
/// Aviso 4: un banco no cuadra (norma43). El saldo contable de su cuenta a la fecha del último extracto N43 no es el saldo final
/// de ese extracto. Grave. Solo bancos en uso con extractos (las cuentas de inversión sin extractos no se miran) y cuyo último
/// extracto cuadra por dentro: si está descuadrado (por ejemplo, un fichero de remesas), su saldo final no es el de la cuenta.
/// </summary>
public sealed class RevisionBancosCuadre : IRevisionSalud
{
    public string Clave => "bancos-cuadre";
    public string Nombre => "Los bancos cuadran con su último extracto";
    public string Nodo => "norma43";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) => Evaluar(await fuente.BancosAsync(ctx.IdSociedad, ctx, c));

    public static HallazgosSalud Evaluar(IReadOnlyList<BancoSalud> bancos)
    {
        var elementos = new List<ElementoSalud>();
        var revisados = 0;
        foreach (var b in bancos.Where(ReglasBancos.EnUso))
        {
            if (b.UltimoExtracto is not DateTime f || b.SaldoExtracto is not decimal banco || b.SaldoContableExtracto is not decimal contable || !b.ExtractoCuadrado) continue;
            revisados++;
            var dif = Math.Round(contable - banco, 2, MidpointRounding.AwayFromZero);
            if (dif == 0) continue;
            elementos.Add(new ElementoSalud($"banco-cuadre-{b.Cuenta}", TipoSalud.Aviso, GravedadSalud.Grave, $"{b.Nombre} no cuadra con el extracto",
                $"El saldo contable de {b.Cuenta} al {Fecha(f)} es {Imp(Math.Abs(dif))} {(dif > 0 ? "mayor" : "menor")} que el saldo final del extracto N43 de esa fecha " +
                $"(banco {Imp(banco)}, contabilidad {Imp(contable)}).",
                null, ReglasBancos.Lineas, "norma43",
                $"La suma de los apuntes contabilizados de {b.Cuenta} hasta el {Fecha(f)} (fecha final del último extracto importado de {b.Nombre}) no es igual al saldo final de ese extracto.",
                "Busca en Líneas N43 los movimientos sin asiento o con un importe distinto, y en el Mayor de la cuenta los apuntes que no están en el banco. " +
                "El aviso desaparece cuando los dos saldos coinciden."));
        }
        return elementos.Count > 0 || revisados == 0 ? new HallazgosSalud(elementos, [], [], [])
            : HallazgosSalud.Correcta($"Los {revisados} bancos con extractos cuadran con el saldo final de su último extracto", "norma43");
    }
}

/// <summary>
/// Aviso 5: movimientos bancarios sin asiento (norma43/conciliacion). En main no hay marca de «conciliado»: un movimiento está
/// conciliado cuando tiene asiento (ContabilizacionesLineaExtracto). Número por banco y fecha del más antiguo. Con la fila
/// «Bancos conciliados» de «Estado de los periodos».
/// </summary>
public sealed class RevisionBancosSinAsiento : IRevisionSalud
{
    public string Clave => "bancos-sin-asiento";
    public string Nombre => "Movimientos bancarios con asiento";
    public string Nodo => "norma43/conciliacion";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.LineasBancoAsync(ctx.IdSociedad, c), ctx.Ejercicio.Ejercicio, ctx.Hoy);

    public static HallazgosSalud Evaluar(IReadOnlyList<MesBanco> meses, int ejercicio, DateTime hoy)
    {
        var elementos = new List<ElementoSalud>();
        var pendientes = meses.Where(m => m.SinAsiento > 0).GroupBy(m => m.Banco)
            .Select(g => (Banco: g.Key, N: g.Sum(m => m.SinAsiento), Antigua: g.Min(m => m.MasAntiguaSinAsiento))).OrderByDescending(x => x.N).ToList();
        if (pendientes.Count > 0)
        {
            var total = pendientes.Sum(p => p.N);
            var antigua = pendientes.Min(p => p.Antigua);
            var porBanco = string.Join(", ", pendientes.Select(p => $"{p.Banco} {p.N}"));
            elementos.Add(new ElementoSalud("bancos-sin-asiento", TipoSalud.Aviso, GravedadSalud.Atencion, $"{N(total, "movimiento bancario", "movimientos bancarios")} sin asiento",
                $"{porBanco}." + (antigua is DateTime a ? $" El más antiguo es del {Fecha(a)}." : ""), null, ReglasBancos.Lineas, "norma43/conciliacion",
                "Hay movimientos de extractos N43 importados (de lotes no revertidos) que no tienen asiento contable. En ProContaVF un movimiento está conciliado cuando tiene su asiento.",
                "En Líneas N43, filtra los pendientes, asigna la cuenta a cada movimiento y contabilízalo. El aviso desaparece cuando todos tienen asiento.") { Cantidad = total });
        }
        return new HallazgosSalud(elementos, [], [], meses.Count == 0 ? [] : [Fila(meses, ejercicio, hoy)]);
    }

    private static FilaPeriodos Fila(IReadOnlyList<MesBanco> meses, int ejercicio, DateTime hoy)
    {
        var celdas = Enumerable.Range(1, 12).Select(m =>
        {
            var nombre = RevisionPlazosIva.Mes(m);
            var delMes = meses.Where(x => x.Anio == ejercicio && x.Mes == m).ToList();
            var lineas = delMes.Sum(x => x.Lineas);
            var sin = delMes.Sum(x => x.SinAsiento);
            var inicio = new DateTime(ejercicio, m, 1);
            var enCurso = hoy.Year == ejercicio && hoy.Month == m;
            if (inicio > hoy) return new CeldaPeriodo(EstadoPeriodo.NoToca, $"Bancos · {nombre}: todavía no toca");
            if (lineas == 0) return new CeldaPeriodo(enCurso ? EstadoPeriodo.EnCurso : EstadoPeriodo.NoToca, $"Bancos · {nombre}: sin movimientos importados");
            if (sin == 0) return new CeldaPeriodo(EstadoPeriodo.Hecho, $"Bancos · {nombre}: los {lineas} movimientos tienen asiento");
            var detalle = string.Join(", ", delMes.Where(x => x.SinAsiento > 0).Select(x => $"{x.Banco} {x.SinAsiento}"));
            return new CeldaPeriodo(enCurso ? EstadoPeriodo.EnCurso : EstadoPeriodo.Incompleto, $"Bancos · {nombre}: {sin} de {lineas} movimientos sin asiento ({detalle})");
        }).ToList();
        return new FilaPeriodos("Bancos conciliados", celdas, ReglasBancos.Lineas, "norma43/conciliacion", 1);
    }
}

/// <summary>
/// Aviso 6: extracto antiguo (norma43). El último extracto de un banco en uso es de hace más de 40 días (los extractos se obtienen
/// cada mes); sin las cuentas de inversión y solo si el banco ha tenido movimiento (apuntes en el Mayor o movimientos de extracto) en
/// los últimos 12 meses: una cuenta parada no avisa (decisión 2026-11-15 15:18 de pantalla-de-inicio).
/// </summary>
public sealed class RevisionExtractoAntiguo : IRevisionSalud
{
    public const int Dias = 40;
    public const int MesesSinMovimiento = 12;
    public string Clave => "bancos-extracto";
    public string Nombre => "Extractos bancarios al día";
    public string Nodo => "norma43";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.BancosAsync(ctx.IdSociedad, ctx, c), ctx.Inversiones, ctx.Hoy);

    public static HallazgosSalud Evaluar(IReadOnlyList<BancoSalud> bancos, IReadOnlySet<string> inversiones, DateTime hoy)
    {
        var elementos = new List<ElementoSalud>();
        foreach (var b in bancos.Where(b => ReglasBancos.EnUso(b) && !inversiones.Contains(b.Cuenta!)))
        {
            if (b.UltimoExtracto is not DateTime f) continue;
            var dias = (hoy.Date - f.Date).Days;
            if (dias <= Dias || b.UltimoMovimiento is not DateTime m || m.Date < hoy.Date.AddMonths(-MesesSinMovimiento)) continue;
            elementos.Add(new ElementoSalud($"banco-extracto-{b.Cuenta}", TipoSalud.Aviso, GravedadSalud.Atencion, $"Último extracto de {b.Nombre} de hace {dias} días",
                $"El último fichero N43 importado de {b.Nombre} ({b.Cuenta}) llega hasta el {Fecha(f)}.", null, ReglasBancos.Cuentas, "norma43",
                $"La fecha final del último extracto N43 importado de {b.Nombre} tiene más de {Dias} días. Las cuentas marcadas como inversión no se miran, " +
                    $"ni las que no han tenido movimiento (apuntes en el Mayor o movimientos de extracto) en los últimos {MesesSinMovimiento} meses.",
                "Descarga del banco el fichero Norma 43 (cuaderno 43) de los días que faltan e impórtalo en Bancos › Cuentas bancarias. El aviso desaparece con el extracto al día."));
        }
        return new HallazgosSalud(elementos, [], [], []);
    }
}

/// <summary>Aviso 7: facturas de compra del ejercicio sin contabilizar (compras). Los PDF nuevos de la carpeta de proveedores no se cuentan (ver Hand-over).</summary>
public sealed class RevisionComprasPendientes : IRevisionSalud
{
    public string Clave => "compras-pendientes";
    public string Nombre => "Facturas de compra contabilizadas";
    public string Nodo => "compras";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.ComprasPendientesAsync(ctx.IdSociedad, ctx.Ejercicio, c));

    public static HallazgosSalud Evaluar(ComprasPendientes p)
    {
        if (p.Numero == 0) return HallazgosSalud.Nada;
        return HallazgosSalud.De(new ElementoSalud("compras-pendientes", TipoSalud.Aviso, GravedadSalud.Atencion, $"{N(p.Numero, "factura de compra", "facturas de compra")} sin contabilizar",
            $"Total {Imp(p.Total)}." + (p.MasAntigua is DateTime f ? $" La más antigua es del {Fecha(f)}." : ""),
            null, new DestinoSalud(PantallaSalud.FacturasCompra, "Compras › Facturas recibidas"), "compras",
            "Hay facturas de compra del ejercicio registradas y sin contabilizar (sin marcar como duplicadas).",
            "En Compras › Facturas recibidas, filtra las pendientes, revísalas y contabilízalas (F12). El aviso desaparece cuando no queda ninguna.") { Cantidad = p.Numero, Importe = p.Total });
    }
}

/// <summary>Aviso 8: facturas de venta en borrador desde hace más de 7 días (facturacion/factura-venta).</summary>
public sealed class RevisionBorradoresVenta : IRevisionSalud
{
    public const int Dias = 7;
    public string Clave => "ventas-borradores";
    public string Nombre => "Borradores de factura de venta recientes";
    public string Nodo => "facturacion/factura-venta";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.BorradoresVentaAsync(ctx.Ejercicio, c), ctx.Hoy);

    public static HallazgosSalud Evaluar(IReadOnlyList<DateTime> creacion, DateTime hoy)
    {
        var viejos = creacion.Where(f => (hoy.Date - f.Date).Days > Dias).OrderBy(f => f).ToList();
        if (viejos.Count == 0) return HallazgosSalud.Nada;
        var fechas = viejos.Select(f => f.ToString("dd/MM", System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToList();
        var lista = fechas.Count == 1 ? $"del {fechas[0]}" : fechas.Count <= 5 ? $"del {string.Join(", ", fechas.Take(fechas.Count - 1))} y el {fechas[^1]}" : $"desde el {fechas[0]}";
        return HallazgosSalud.De(new ElementoSalud("ventas-borradores", TipoSalud.Aviso, GravedadSalud.Aviso,
            $"{N(viejos.Count, "factura de venta en borrador", "facturas de venta en borrador")} desde hace más de {Dias} días",
            $"{(viejos.Count == 1 ? "Borrador sin número" : "Borradores sin número")} {lista}.", null, new DestinoSalud(PantallaSalud.FacturasVenta, "Ventas › Facturas emitidas"),
            "facturacion/factura-venta", $"Hay facturas de venta del ejercicio en borrador creadas hace más de {Dias} días.",
            "En Ventas › Facturas emitidas, emite los borradores que procedan y borra los que no. El aviso desaparece cuando no queda ninguno de más de 7 días.") { Cantidad = viejos.Count });
    }
}

/// <summary>Aviso 9: clientes con saldo acreedor (contabilidad/mayor): subcuentas 430 a 436 con saldo al haber a la fecha de corte.</summary>
public sealed class RevisionClientesAcreedores : IRevisionSalud
{
    public string Clave => "clientes-acreedores";
    public string Nombre => "Clientes sin saldo acreedor";
    public string Nodo => "contabilidad/mayor";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.SaldosAsync(ctx.Ejercicio, "430", "436", ctx.Corte, c), await fuente.CuadradasAsync(ctx.Ejercicio, c));

    /// <param name="cuadradas">Subcuentas marcadas «Cuadrada» en el Mayor: no cuentan si su saldo no ha cambiado (saldo justificado).</param>
    public static HallazgosSalud Evaluar(IReadOnlyDictionary<string, decimal> saldos, IReadOnlyDictionary<string, decimal?>? cuadradas = null)
    {
        var acreedores = saldos.Where(s => Math.Round(s.Value, 2) < 0 && !SaldoJustificado.Es(s.Key, s.Value, cuadradas)).OrderBy(s => s.Value).ToList();
        if (acreedores.Count == 0) return HallazgosSalud.Nada;
        var total = -acreedores.Sum(s => s.Value);
        var cuentas = acreedores.Select(s => s.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
        return HallazgosSalud.De(new ElementoSalud("clientes-acreedores", TipoSalud.Aviso, GravedadSalud.Atencion, $"{N(acreedores.Count, "cliente", "clientes")} con saldo acreedor",
            $"Subcuentas 430 a 436 con saldo al haber: cobros duplicados, anticipos o facturas sin registrar (total {Imp(total)}).",
            null, DestinoSalud.Mayor(cuentas, $"{N(acreedores.Count, "cliente", "clientes")} con saldo acreedor"), "contabilidad/mayor",
            "Alguna subcuenta de clientes (430 a 436) tiene saldo acreedor a la fecha de corte, con los asientos contabilizados. " + SaldoJustificado.Texto,
            "Abre el Mayor de esos clientes y registra la factura que falta, devuelve el cobro duplicado o reclasifica el anticipo (438). El aviso desaparece cuando ninguno tiene saldo acreedor.") { Cantidad = acreedores.Count, Importe = total });
    }
}

/// <summary>
/// Aviso 10: partidas pendientes de aplicación (contabilidad/mayor): subcuentas 555 con saldo a la fecha de corte, sin las que
/// Norma 43 usa como banco. Son las que dejaban negativo el pasivo corriente de los ratios.
/// </summary>
public sealed class RevisionPartidasPendientes : IRevisionSalud
{
    public string Clave => "partidas-555";
    public string Nombre => "Partidas pendientes de aplicación";
    public string Nodo => "contabilidad/mayor";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var saldos = await fuente.SaldosAsync(ctx.Ejercicio, "555", "555", ctx.Corte, c);
        var bancos = (await fuente.BancosAsync(ctx.IdSociedad, ctx, c)).Where(b => b.Cuenta is not null).Select(b => b.Cuenta!).ToHashSet(StringComparer.Ordinal);
        return Evaluar(saldos, bancos, await fuente.CuadradasAsync(ctx.Ejercicio, c));
    }

    /// <param name="cuadradas">Subcuentas marcadas «Cuadrada» en el Mayor: no cuentan si su saldo no ha cambiado (saldo justificado).</param>
    public static HallazgosSalud Evaluar(IReadOnlyDictionary<string, decimal> saldos, IReadOnlySet<string> bancos, IReadOnlyDictionary<string, decimal?>? cuadradas = null)
    {
        var conSaldo = saldos.Where(s => Math.Round(s.Value, 2) != 0 && !bancos.Contains(s.Key) && !SaldoJustificado.Es(s.Key, s.Value, cuadradas)).OrderBy(s => s.Key, StringComparer.Ordinal).ToList();
        if (conSaldo.Count == 0) return HallazgosSalud.Nada;
        var detalle = string.Join(", ", conSaldo.Take(4).Select(s => $"{s.Key} {Imp(Math.Abs(s.Value))} {(s.Value > 0 ? "deudor" : "acreedor")}")) + (conSaldo.Count > 4 ? "…" : "");
        return HallazgosSalud.De(new ElementoSalud("partidas-555", TipoSalud.Aviso, GravedadSalud.Atencion, $"{N(conSaldo.Count, "partida pendiente de aplicación", "partidas pendientes de aplicación")} con saldo",
            $"{detalle}. Mientras no se apliquen, el pasivo corriente puede salir negativo y los ratios de liquidez, «sin datos».",
            null, DestinoSalud.Mayor(conSaldo.Select(s => s.Key).ToList(), "partidas pendientes de aplicación con saldo"), "contabilidad/mayor",
            "Alguna subcuenta 555 (partidas pendientes de aplicación) tiene saldo a la fecha de corte. No cuentan las 555 que Norma 43 usa como banco. " + SaldoJustificado.Texto,
            "Abre el Mayor de esas subcuentas y traspasa cada partida a su cuenta definitiva. El aviso desaparece cuando quedan a cero.") { Cantidad = conSaldo.Count, Importe = conSaldo.Sum(s => s.Value) });
    }
}

/// <summary>
/// «Facturas recurrentes sin recibir» (compras/facturas-recurrentes-pendientes 2026-11-23 18:55): facturas de proveedores mensuales, bimestrales o
/// trimestrales cuya fecha esperada (la de su última factura más su periodo) ya ha pasado sin que esté registrada, sin las descartadas con
/// «No llegará». Atención; abre Compras › Facturas recurrentes pendientes.
/// </summary>
public sealed class RevisionFacturasRecurrentes : IRevisionSalud
{
    public string Clave => "facturas-recurrentes";
    public string Nombre => "Facturas recurrentes de proveedores recibidas";
    public string Nodo => "compras/facturas-recurrentes-pendientes";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.FacturasRecurrentesPendientesAsync(ctx.IdSociedad, ctx.Hoy, c));

    public static HallazgosSalud Evaluar(IReadOnlyList<ProConta.Compras.Recurrentes.FacturaRecurrentePendiente> pendientes)
    {
        var vivas = pendientes.Where(p => !p.Descartada).ToList();
        if (vivas.Count == 0) return HallazgosSalud.Nada;
        var importe = vivas.Sum(p => p.ImporteEstimado);
        var proveedores = vivas.GroupBy(p => p.IdProveedor).Select(g => $"{g.First().Proveedor} ({g.Count()})").ToList();
        var detalle = string.Join(", ", proveedores.Take(3)) + (proveedores.Count > 3 ? $" y {N(proveedores.Count - 3, "proveedor más", "proveedores más")}" : "");
        return HallazgosSalud.De(new ElementoSalud("facturas-recurrentes", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"{N(vivas.Count, "factura recurrente sin recibir", "facturas recurrentes sin recibir")}",
            $"{detalle}. Importe estimado {Imp(importe)} (el de la última factura de cada proveedor).",
            null, new DestinoSalud(PantallaSalud.FacturasRecurrentes, "Compras › Facturas recurrentes pendientes"), "compras/facturas-recurrentes-pendientes",
            "Un proveedor con al menos 3 facturas seguidas cada 1, 2 o 3 meses en los últimos 12 meses (o con esa periodicidad fijada en su ficha) no tiene " +
            "registrada la factura que tocaba: la fecha de su última factura más su periodo ya ha pasado. No cuentan los periodos marcados «No llegará» ni los " +
            "proveedores con «No controlar» en su ficha.",
            "Cuando llegue el PDF, regístralo en Compras › Facturas recibidas y el periodo desaparece. Si esa factura no va a llegar, márcala «No llegará» en " +
            "Compras › Facturas recurrentes pendientes; si el proveedor no factura con regularidad, pon «No controlar» en su ficha (Roles › Proveedor › Facturas periódicas).")
        { Cantidad = vivas.Count, Importe = importe });
    }
}
