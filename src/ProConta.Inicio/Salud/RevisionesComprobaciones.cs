using System.Text.RegularExpressions;
using static ProConta.Inicio.Salud.CalendarioFiscal;

namespace ProConta.Inicio.Salud;

/// <summary>Comprobación 11: el Diario cuadra (contabilidad/asientos): debe = haber en todos los asientos del ejercicio (sin los anulados).</summary>
public sealed class RevisionDiarioCuadra : IRevisionSalud
{
    public string Clave => "diario-cuadra";
    public string Nombre => "El Diario cuadra";
    public string Nodo => "contabilidad/asientos";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var (asientos, descuadrados) = await fuente.CuadreDiarioAsync(ctx.Ejercicio, c);
        return Evaluar(asientos, descuadrados, ctx.Ejercicio.Ejercicio);
    }

    public static HallazgosSalud Evaluar(int asientos, IReadOnlyList<AsientoDescuadrado> descuadrados, int ejercicio)
    {
        var mal = descuadrados.Where(d => Math.Round(d.Diferencia, 2) != 0).OrderBy(d => d.Fecha).ToList();
        if (mal.Count == 0)
            return asientos == 0 ? HallazgosSalud.Nada : HallazgosSalud.Correcta($"El Diario cuadra: debe = haber en los {asientos:#,##0} asientos de {ejercicio}", "contabilidad/asientos");
        var detalle = string.Join("; ", mal.Take(3).Select(d => $"el del {Fecha(d.Fecha)}{(string.IsNullOrWhiteSpace(d.Descripcion) ? "" : $" ({d.Descripcion!.Trim()})")}: debe {Imp(d.Debe)}, haber {Imp(d.Haber)}"))
                      + (mal.Count > 3 ? "…" : "");
        return HallazgosSalud.De(new ElementoSalud("diario-cuadra", TipoSalud.Aviso, GravedadSalud.Grave, $"{N(mal.Count, "asiento descuadrado", "asientos descuadrados")} en el Diario",
            $"{(mal.Count == 1 ? "Su debe no es igual a su haber" : "Su debe no es igual a su haber")}: {detalle}.", null,
            new DestinoSalud(PantallaSalud.Diario, "Contabilidad › Diario"), "contabilidad/asientos",
            $"Algún asiento de {ejercicio} (contabilizado o en borrador, sin los anulados) tiene una suma del debe distinta de la del haber.",
            "Busca esos asientos en el Diario por su fecha y corrígelos (la diferencia suele ser un apunte que falta o un importe mal tecleado). El aviso desaparece cuando todos cuadran."));
    }
}

/// <summary>
/// Comprobación 12: numeración de las facturas validadas sin huecos en cada serie (facturacion/factura-venta). Se toma la parte
/// numérica final de cada número y se buscan los que faltan entre el primero y el último de la serie.
/// </summary>
public sealed partial class RevisionNumeracionFacturas : IRevisionSalud
{
    public string Clave => "numeracion-facturas";
    public string Nombre => "Numeración de facturas sin huecos";
    public string Nodo => "facturacion/factura-venta";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) => Evaluar(await fuente.NumerosFacturasAsync(ctx.Ejercicio, c));

    [GeneratedRegex(@"(\d+)\s*$")]
    private static partial Regex Final();

    public static HallazgosSalud Evaluar(IReadOnlyList<NumeroFactura> facturas)
    {
        var elementos = new List<ElementoSalud>();
        var series = 0;
        foreach (var g in facturas.GroupBy(f => (f.IdSerie, f.Serie)))
        {
            // La parte numérica es lo que va detrás del prefijo de la serie (que puede llevar el año: «S2026» + «0001»).
            string SinPrefijo(string n) => g.Key.Serie.Length > 0 && n.StartsWith(g.Key.Serie, StringComparison.OrdinalIgnoreCase) ? n[g.Key.Serie.Length..] : n;
            var numeros = g.Select(f => Final().Match(SinPrefijo(f.Numero))).Where(m => m.Success && m.Groups[1].Value.Length <= 9).Select(m => long.Parse(m.Groups[1].Value)).Distinct().OrderBy(n => n).ToList();
            if (numeros.Count == 0) continue;
            series++;
            var presentes = numeros.ToHashSet();
            var faltan = new List<long>();
            for (var n = numeros[0]; n <= numeros[^1] && faltan.Count <= 50; n++) if (!presentes.Contains(n)) faltan.Add(n);
            if (faltan.Count == 0) continue;
            var nombre = string.IsNullOrWhiteSpace(g.Key.Serie) ? $"{g.Key.IdSerie}" : g.Key.Serie;
            var lista = string.Join(", ", faltan.Take(8)) + (faltan.Count > 8 ? "…" : "");
            elementos.Add(new ElementoSalud($"numeracion-{g.Key.IdSerie}", TipoSalud.Aviso, GravedadSalud.Grave, $"Huecos en la numeración de la serie {nombre}",
                $"{(faltan.Count == 1 ? "Falta el número" : $"Faltan {(faltan.Count > 50 ? "más de 50" : faltan.Count.ToString())} números:")} {lista} (de {numeros[0]} a {numeros[^1]}).", null,
                new DestinoSalud(PantallaSalud.FacturasVenta, "Ventas › Facturas emitidas"), "facturacion/factura-venta",
                $"Entre el primer y el último número de las facturas validadas de la serie {nombre} faltan números. Las facturas validadas no pueden tener huecos en su serie.",
                "En Ventas › Facturas emitidas, busca qué pasó con esos números (una factura borrada o emitida en otra serie) y, si falta una, emítela con ese número o deja constancia. " +
                "El aviso desaparece cuando la serie no tiene huecos."));
        }
        if (elementos.Count == 0 && series > 0)
            return HallazgosSalud.Correcta($"Numeración de las facturas validadas sin huecos en {N(series, "serie", "series")}", "facturacion/factura-venta");
        return new HallazgosSalud(elementos, [], [], []);
    }
}

/// <summary>
/// Comprobación 13: los libros de IVA coinciden con las cuentas (contabilidad/modelos-tributarios), por trimestre hasta la fecha de
/// corte: libro de repercutido (más las cuotas autorrepercutidas del soportado) frente a las 477, y libro de soportado frente a las
/// 472, sin los asientos de liquidación. Solo si el ejercicio tiene libros.
/// </summary>
public sealed class RevisionLibrosIva : IRevisionSalud
{
    public string Clave => "libros-iva";
    public string Nombre => "Libros de IVA y cuentas 477 y 472";
    public string Nodo => "contabilidad/modelos-tributarios";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) => Evaluar(await fuente.LibrosIvaAsync(ctx.Ejercicio, c), ctx.Ejercicio.Ejercicio);

    public static HallazgosSalud Evaluar(IReadOnlyList<IvaLibrosTrimestre> trimestres, int ejercicio)
    {
        var conLibros = trimestres.Where(t => t.ApuntesLibros > 0).OrderBy(t => t.Trimestre).ToList();
        if (conLibros.Count == 0) return HallazgosSalud.Nada;
        var mal = conLibros.Where(t => Math.Round(t.LibroRepercutido - t.Cuentas477, 2) != 0 || Math.Round(t.LibroSoportado - t.Cuentas472, 2) != 0).ToList();
        if (mal.Count == 0)
            return HallazgosSalud.Correcta($"Los libros de IVA coinciden con las cuentas 477 y 472 en {N(conLibros.Count, "trimestre", "trimestres")} de {ejercicio}", "contabilidad/modelos-tributarios");
        string Detalle(IvaLibrosTrimestre t)
        {
            var partes = new List<string>();
            if (Math.Round(t.LibroRepercutido - t.Cuentas477, 2) != 0) partes.Add($"repercutido: libro {Imp(t.LibroRepercutido)}, 477 {Imp(t.Cuentas477)}");
            if (Math.Round(t.LibroSoportado - t.Cuentas472, 2) != 0) partes.Add($"soportado: libro {Imp(t.LibroSoportado)}, 472 {Imp(t.Cuentas472)}");
            return $"{t.Trimestre}T {string.Join("; ", partes)}";
        }
        return HallazgosSalud.De(new ElementoSalud("libros-iva", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"Los libros de IVA no coinciden con las cuentas en el {string.Join(", ", mal.Select(t => $"{t.Trimestre}T"))}",
            string.Join(". ", mal.Select(Detalle)) + ".", null, DestinoSalud.Mayor(["472", "477"], "cuentas 472 y 477"), "contabilidad/modelos-tributarios",
            $"En algún trimestre de {ejercicio}, la suma de las cuotas del libro de IVA repercutido (más las autorrepercutidas) no es la de las 477, o la del soportado no es la de las 472, " +
            "contando solo asientos contabilizados y sin los de liquidación ni regularización.",
            "Abre el Mayor de las 472 y 477 del trimestre y busca los asientos de IVA sin registro en el libro (asientos a mano) o con una cuota distinta. El aviso desaparece cuando coinciden."));
    }
}

/// <summary>Comprobación 14: clientes y proveedores con movimientos en el ejercicio tienen NIF (terceros).</summary>
public sealed class RevisionTercerosNif : IRevisionSalud
{
    public string Clave => "terceros-nif";
    public string Nombre => "Clientes y proveedores con NIF";
    public string Nodo => "terceros";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.TercerosSinNifAsync(ctx.IdSociedad, ctx.Ejercicio, c), ctx.Ejercicio.Ejercicio);

    public static HallazgosSalud Evaluar(IReadOnlyList<TerceroSinNif> sinNif, int ejercicio)
    {
        var terceros = sinNif.GroupBy(t => t.IdTercero).Select(g => g.First()).OrderBy(t => t.Nombre, StringComparer.CurrentCulture).ToList();
        if (terceros.Count == 0)
            return HallazgosSalud.Correcta($"Todos los clientes y proveedores con movimientos en {ejercicio} tienen NIF", "terceros");
        var clientes = terceros.Count(t => t.EsCliente);
        var nombres = string.Join(", ", terceros.Take(4).Select(t => $"{t.Nombre} ({t.Cuenta})")) + (terceros.Count > 4 ? "…" : "");
        return HallazgosSalud.De(new ElementoSalud("terceros-nif", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"{N(terceros.Count, "cliente o proveedor", "clientes y proveedores")} con movimientos sin NIF",
            $"{clientes} {(clientes == 1 ? "cliente" : "clientes")} y {terceros.Count - clientes} {(terceros.Count - clientes == 1 ? "proveedor" : "proveedores")}: {nombres}.", null,
            new DestinoSalud(PantallaSalud.Terceros, "Datos maestros › Terceros"), "terceros",
            $"Algún tercero cuya subcuenta de cliente (430 a 436) o de proveedor (400 a 410) tiene apuntes en {ejercicio} no tiene NIF en su ficha. Sin NIF no sale bien en los modelos 347 y 349 ni en las facturas.",
            "Busca cada tercero en Datos maestros › Terceros, abre su ficha y escribe su NIF. El aviso desaparece cuando todos lo tienen."));
    }
}

/// <summary>Comprobación 15: sin apuntes en subcuentas dadas de baja (inactivas) o que no existen en el plan del ejercicio (contabilidad/cuenta-contable).</summary>
public sealed class RevisionCuentasBaja : IRevisionSalud
{
    public string Clave => "cuentas-baja";
    public string Nombre => "Apuntes en subcuentas activas";
    public string Nodo => "contabilidad/cuenta-contable";

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c) =>
        Evaluar(await fuente.CuentasConProblemaAsync(ctx.Ejercicio, c), ctx.Ejercicio.Ejercicio);

    public static HallazgosSalud Evaluar(IReadOnlyList<CuentaConProblema> cuentas, int ejercicio)
    {
        if (cuentas.Count == 0)
            return HallazgosSalud.Correcta($"Ningún apunte de {ejercicio} va a subcuentas dadas de baja o que no existen", "contabilidad/cuenta-contable");
        var noExisten = cuentas.Where(x => !x.Existe).ToList();
        var baja = cuentas.Where(x => x.Existe).ToList();
        var partes = new List<string>();
        if (noExisten.Count > 0) partes.Add($"no existen en el plan de {ejercicio}: {string.Join(", ", noExisten.Take(5).Select(x => x.Cuenta))}{(noExisten.Count > 5 ? "…" : "")}");
        if (baja.Count > 0) partes.Add($"dadas de baja: {string.Join(", ", baja.Take(5).Select(x => x.Cuenta))}{(baja.Count > 5 ? "…" : "")}");
        var apuntes = cuentas.Sum(x => x.Apuntes);
        return HallazgosSalud.De(new ElementoSalud("cuentas-baja", TipoSalud.Aviso, GravedadSalud.Atencion,
            $"Apuntes en {N(cuentas.Count, "subcuenta dada de baja o que no existe", "subcuentas dadas de baja o que no existen")}",
            $"{N(apuntes, "apunte", "apuntes")} en subcuentas que " + string.Join("; ", partes) + ".", null,
            DestinoSalud.Mayor(cuentas.Select(x => x.Cuenta).Take(40).ToList(), "subcuentas dadas de baja o inexistentes"), "contabilidad/cuenta-contable",
            $"Hay apuntes de {ejercicio} (sin los asientos anulados) en subcuentas que no están en el plan de cuentas del ejercicio o que están inactivas.",
            "Da de alta la subcuenta que falta o vuelve a activarla, o mueve esos apuntes a la subcuenta correcta desde el Mayor. El aviso desaparece cuando no queda ninguno."));
    }
}

/// <summary>
/// Comprobación 16: nóminas contabilizadas hasta el mes anterior (nominas). Solo si la sociedad usa nóminas (alguna importación
/// en el ejercicio o en el anterior). Cuentan las pagas mensuales; las extra, no. Con la fila «Nóminas» de «Estado de los periodos».
/// </summary>
public sealed class RevisionNominas : IRevisionSalud
{
    public const string Contabilizada = "CONTABILIZADA";
    public string Clave => "nominas";
    public string Nombre => "Nóminas contabilizadas";
    public string Nodo => "nominas";

    private static readonly DestinoSalud Destino = new(PantallaSalud.Nominas, "Nóminas");

    public async Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud ctx, CancellationToken c)
    {
        var ejercicios = ctx.Ejercicios.Where(e => e.Ejercicio == ctx.Ejercicio.Ejercicio || e.Ejercicio == ctx.Ejercicio.Ejercicio - 1).ToList();
        return Evaluar(await fuente.NominasAsync(ejercicios, c), ctx.Ejercicio.Ejercicio, ctx.Hoy);
    }

    public static HallazgosSalud Evaluar(IReadOnlyList<NominaMes> nominas, int ejercicio, DateTime hoy)
    {
        if (nominas.Count == 0) return HallazgosSalud.Nada;
        var mensuales = nominas.Where(n => n.Anio == ejercicio && !string.Equals(n.TipoPaga, "EXTRA", StringComparison.OrdinalIgnoreCase)).ToList();
        // Meses que deberían estar contabilizados: hasta el anterior al de hoy (todo el año si el ejercicio ya terminó).
        var hasta = hoy.Year > ejercicio ? 12 : hoy.Year == ejercicio ? hoy.Month - 1 : 0;
        string Estado(int m)
        {
            var delMes = mensuales.Where(n => n.Mes == m).ToList();
            if (delMes.Count == 0) return "falta";
            return delMes.All(n => n.Estado == Contabilizada) ? "ok" : "pendiente";
        }
        var faltan = Enumerable.Range(1, Math.Max(0, hasta)).Where(m => Estado(m) != "ok").ToList();
        var fila = new FilaPeriodos("Nóminas", Enumerable.Range(1, 12).Select(m =>
        {
            var nombre = RevisionPlazosIva.Mes(m);
            var estado = Estado(m);
            var inicio = new DateTime(ejercicio, m, 1);
            if (estado == "ok") return new CeldaPeriodo(EstadoPeriodo.Hecho, $"Nóminas · {nombre}: contabilizadas");
            if (inicio > hoy) return new CeldaPeriodo(EstadoPeriodo.NoToca, $"Nóminas · {nombre}: todavía no toca");
            if (m > hasta) return new CeldaPeriodo(EstadoPeriodo.EnCurso, $"Nóminas · {nombre}: mes en curso" + (estado == "pendiente" ? ", importadas sin contabilizar" : ""));
            return estado == "pendiente"
                ? new CeldaPeriodo(EstadoPeriodo.Incompleto, $"Nóminas · {nombre}: importadas, sin contabilizar o con incidencias")
                : new CeldaPeriodo(EstadoPeriodo.ConErrores, $"Nóminas · {nombre}: no hay nóminas importadas");
        }).ToList(), Destino, "nominas", 2);
        if (hasta <= 0) return new HallazgosSalud([], [], [], [fila]);
        var ultimoMes = RevisionPlazosIva.Mes(hasta);
        if (faltan.Count == 0)
            return new HallazgosSalud([], [], [new ComprobacionCorrecta($"Nóminas contabilizadas hasta {ultimoMes}{(hoy.Year > ejercicio ? $" de {ejercicio}" : "")}", "nominas")], [fila]);
        var lista = string.Join(", ", faltan.Select(m => $"{RevisionPlazosIva.Mes(m)}{(Estado(m) == "pendiente" ? " (importadas sin contabilizar)" : "")}"));
        var elemento = new ElementoSalud("nominas", TipoSalud.Aviso, GravedadSalud.Atencion, $"Nóminas sin contabilizar: {string.Join(", ", faltan.Select(RevisionPlazosIva.Mes))}",
            $"Deberían estar contabilizadas hasta {ultimoMes}; {(faltan.Count == 1 ? "falta" : "faltan")} {lista}.", null, Destino, "nominas",
            $"La sociedad tiene nóminas y algún mes de {ejercicio} hasta el anterior al actual no tiene la nómina mensual importada y contabilizada.",
            "Importa en Nóminas el fichero de la gestoría de ese mes y contabilízalo (o resuelve sus incidencias). El aviso desaparece cuando todos los meses están contabilizados.");
        return new HallazgosSalud([elemento], [], [], [fila]);
    }
}
