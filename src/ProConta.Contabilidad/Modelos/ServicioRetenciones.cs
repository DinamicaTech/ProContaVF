using System.Globalization;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Presentacion;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Modelos;

/// <summary>
/// Un trimestre del 111 o del 115 para la pantalla de modelos: su estado (los mismos que el 303), el total de retenciones calculado,
/// el resultado a ingresar, la forma de pago, la última presentación del entorno de la BD y lo que impide presentar o conviene revisar.
/// </summary>
public sealed record TrimestreRetenciones(int Trimestre, DateTime Fin, Estado303 Estado, decimal TotalCalculado, decimal Resultado, char FormaPago,
    PresentacionGuardada? Ultima, IReadOnlyList<PresentacionGuardada> Presentaciones, IReadOnlyList<string> Bloqueos, IReadOnlyList<string> Avisos)
{
    public string Periodo => $"{Trimestre}T";
    public bool Terminado => Estado != Estado303.EnCurso;
    public bool SePuedePresentar => Terminado && Bloqueos.Count == 0;
}

/// <summary>Un documento con retención que no puede entrar en el modelo tal como está (sin contabilizar o sin clase de retención).</summary>
public sealed record DocumentoRetencionPendiente(ClaseRetencionModelo? Clase, int Trimestre, string Documento, bool SinContabilizar, decimal Retencion);

/// <summary>Un apunte de una subcuenta 4751 del ejercicio (para comprobar lo que espera el aviso de salud S4).</summary>
public sealed record ApunteRetencion(int IdAsiento, string Cuenta, DateTime Fecha, bool Apertura, decimal Debe, decimal Haber);

/// <summary>Lo que se lee de la BD para los modelos de retenciones de un ejercicio.</summary>
public sealed record LecturaRetenciones(IReadOnlyList<PercepcionRetencion> Percepciones, IReadOnlyList<DocumentoRetencionPendiente> Pendientes,
    IReadOnlyList<string> AvisosDocumentos, IReadOnlyList<ApunteRetencion> Apuntes4751, IReadOnlyDictionary<int, ClaseRetencionModelo> AsientosDocumentos)
{
    public IEnumerable<PercepcionRetencion> DelTrimestre(int trimestre) => Percepciones.Where(p => (p.Fecha.Month + 2) / 3 == trimestre);
}

/// <summary>
/// Modelos 111 y 115 de cada trimestre (contabilidad/modelos-tributarios/modelo-111 y modelo-115), con el mismo contrato que
/// <see cref="Servicio303"/> para la pantalla única de modelos tributarios: el estado de los cuatro trimestres, la declaración con sus
/// casillas (<see cref="Modelo111"/>, <see cref="Modelo115"/>) y su fichero (<see cref="FicheroRetenciones"/>), que la pieza común valida
/// y presenta (<see cref="ServicioPresentacionAeat"/>).
/// <list type="bullet">
/// <item>Trabajo (111, 01-06): las nóminas de las importaciones vigentes del ejercicio (sin las reemplazadas ni las deshechas), por la
/// fecha de la nómina. Perceptores: empleados distintos con percepciones. Dinerario = bruto e IRPF111; con ingreso a cuenta por
/// retribución en especie (InRetEsp), el valor en especie (−DtoReEsp) y el ingreso a cuenta van en 04-06 y el bruto sin la especie en 02.</item>
/// <item>Profesionales (111, 07-09) y arrendamientos (115): las facturas de compra no duplicadas con retención, por la clase de
/// retención de la factura (TipoRetencion 1 servicios, 2 alquileres) y la fecha de su asiento (la de la factura si aún no lo tiene).
/// Base: la base imponible de la factura; perceptor: el proveedor (NIF, o su cuenta).</item>
/// <item>No se ofrece presentar si hay facturas del trimestre con retención sin clase o sin contabilizar; se avisa de las nóminas sin
/// contabilizar y de lo que las 4751 no cuadran con el modelo (lo que el aviso de salud S4 espera pagar).</item>
/// </list>
/// </summary>
public sealed class ServicioRetenciones(IBaseDatos bd, ServicioPresentacionAeat presentacion, string modelo, IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    public static ServicioRetenciones Modelo111(IBaseDatos bd, ServicioPresentacionAeat presentacion, IReloj? reloj = null) =>
        new(bd, presentacion, Modelos.Modelo111.Modelo, reloj);

    public static ServicioRetenciones Modelo115(IBaseDatos bd, ServicioPresentacionAeat presentacion, IReloj? reloj = null) =>
        new(bd, presentacion, Modelos.Modelo115.Modelo, reloj);

    public string Modelo { get; } = modelo is Modelos.Modelo111.Modelo or Modelos.Modelo115.Modelo ? modelo
        : throw new ArgumentException($"El modelo {modelo} no es de retenciones (111 o 115).", nameof(modelo));

    public ServicioPresentacionAeat Presentacion => presentacion;

    public DateTime Hoy => _reloj.Ahora.Date;

    private bool Es111 => Modelo == Modelos.Modelo111.Modelo;

    /// <summary>Conceptos de las casillas del modelo (para la pantalla y la comparación con el PDF).</summary>
    public IReadOnlyList<(int Casilla, string Concepto)> Conceptos => Es111 ? Modelos.Modelo111.Conceptos : Modelos.Modelo115.Conceptos;

    /// <summary>Casillas que son números de perceptores.</summary>
    public IReadOnlySet<int> CasillasPerceptores => Es111 ? Modelos.Modelo111.CasillasPerceptores : Modelos.Modelo115.CasillasPerceptores;

    private bool EsDelModelo(ClaseRetencionModelo? clase) => clase is null || (clase == ClaseRetencionModelo.Arrendamientos) != Es111;

    /// <summary>Calcula el modelo de un trimestre con las percepciones dadas (sin BD).</summary>
    public DeclaracionRetenciones Calcular(IEnumerable<PercepcionRetencion> percepciones, int ejercicio, int trimestre, Declarante303 declarante,
        RectificativaRetenciones? rectificativa = null, bool conNrc = false) => Es111
        ? Modelos.Modelo111.Calcular(percepciones, ejercicio, trimestre, declarante, rectificativa, conNrc)
        : Modelos.Modelo115.Calcular(percepciones, ejercicio, trimestre, declarante, rectificativa, conNrc);

    /// <summary>Los cuatro trimestres del ejercicio con su estado.</summary>
    public async Task<IReadOnlyList<TrimestreRetenciones>> TrimestresAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default)
    {
        var lectura = await LeerAsync(ctx, c);
        var entorno = await presentacion.EntornoAsync(c);
        var todas = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c)).Where(p => p.Entorno == ServiciosAeat.Codigo(entorno)).ToList();
        var declarante = await DeclaranteAsync(ctx);
        var r = new List<TrimestreRetenciones>();
        for (var t = 1; t <= 4; t++)
        {
            var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, t);
            var del = todas.Where(p => p.Periodo == $"{t}T").OrderBy(p => p.FechaHora).ThenBy(p => p.Id).ToList();
            var ultima = del.LastOrDefault();
            var d = Declarar(lectura, ctx.Ejercicio, t, declarante, Rectificativa(ultima), Hoy);
            var bloqueos = new List<string>();
            var avisos = new List<string>();
            if (Hoy > fin)
            {
                bloqueos.AddRange(Bloqueos(lectura, t));
                bloqueos.AddRange(d.Errores);
                avisos.AddRange(d.Avisos);
                avisos.AddRange(Avisos(lectura, t));
                if (FueraDePlazoDomiciliacion(d, Hoy) is { } dom) avisos.Add(dom);
            }
            // El estado compara el total del trimestre con el total presentado.
            var estado = Hoy <= fin ? Estado303.EnCurso
                : ultima is null ? Estado303.SinPresentar
                : ultima.Resultado != d.TotalRetenciones ? Estado303.Desactualizado
                : ultima.ComplementariaDe is not null ? Estado303.Complementaria
                : Estado303.Presentado;
            if (estado == Estado303.Desactualizado)
                avisos.Add(ultima!.Resultado < d.TotalRetenciones
                    ? $"Se presentó con un total de {Euros(ultima.Resultado)} y ahora da {Euros(d.TotalRetenciones)}: si la diferencia es correcta, presenta una complementaria."
                    : $"Se presentó con un total de {Euros(ultima.Resultado)} y ahora da {Euros(d.TotalRetenciones)}, menos: una complementaria no puede bajarlo; si la diferencia es correcta, pide la rectificación en la Sede de la AEAT.");
            r.Add(new TrimestreRetenciones(t, fin, estado, d.TotalRetenciones, d.Resultado, d.TipoDeclaracion, ultima, del, bloqueos, avisos));
        }
        return r;
    }

    /// <summary>Las percepciones del trimestre que forman el modelo (para enseñar de dónde sale cada casilla).</summary>
    public async Task<IReadOnlyList<PercepcionRetencion>> PercepcionesAsync(ContextoSociedadEjercicio ctx, int trimestre, CancellationToken c = default) =>
        [.. (await LeerAsync(ctx, c)).DelTrimestre(trimestre).Where(p => EsDelModelo(p.Clase)).OrderBy(p => p.Fecha)];

    /// <summary>
    /// La declaración del trimestre con sus casillas y su fichero. Con <paramref name="complementaria"/>, una complementaria de la
    /// última presentada desde el programa (su justificante y su total van en la declaración).
    /// </summary>
    /// <param name="nrc">NRC del pago cuando sale a ingresar fuera del plazo de domiciliación (forma I); se ignora si se domicilia.</param>
    public async Task<Resultado<(DeclaracionRetenciones Declaracion, DeclaracionAeat Aeat)>> DeclaracionAsync(ContextoSociedadEjercicio ctx, int trimestre, bool complementaria,
        string? nrc = null, CancellationToken c = default)
    {
        var registro = $"Modelo {Modelo} {trimestre}T {ctx.Ejercicio}";
        Resultado<(DeclaracionRetenciones, DeclaracionAeat)> Fallo(string codigo, string mensaje, string? causa = null, string? solucion = null) =>
            Resultado<(DeclaracionRetenciones, DeclaracionAeat)>.Fallo(new Error($"Modelo{Modelo}.{codigo}", mensaje, registro, causa, solucion));
        if (trimestre is < 1 or > 4) return Fallo("Trimestre", "El trimestre va de 1 a 4.");
        var fin = CalculoLiquidacionIva.FinTrimestre(ctx.Ejercicio, trimestre);
        if (Hoy <= fin)
            return Fallo("EnCurso", $"El {trimestre}T de {ctx.Ejercicio} aún no ha terminado.", $"El {Modelo} se presenta cuando el trimestre ha terminado.",
                $"Vuelve a partir del {fin.AddDays(1):dd/MM/yyyy}.");
        var lectura = await LeerAsync(ctx, c);
        var entorno = await presentacion.EntornoAsync(c);
        PresentacionGuardada? anterior = null;
        if (complementaria)
        {
            anterior = (await presentacion.PresentadasAsync(ctx.IdSociedad, Modelo, ctx.Ejercicio, c))
                .Where(p => p.Periodo == $"{trimestre}T" && p.Entorno == ServiciosAeat.Codigo(entorno)).OrderBy(p => p.FechaHora).ThenBy(p => p.Id).LastOrDefault();
            if (anterior is null)
                return Fallo("SinAnterior", $"No consta presentado el {trimestre}T de {ctx.Ejercicio} desde el programa.",
                    "Una complementaria necesita el justificante y el resultado de la presentación anterior.",
                    "Si lo presentó la gestoría, pídele el justificante; la complementaria con justificante externo aún no está disponible.");
        }
        var declarante = await DeclaranteAsync(ctx);
        var d = Declarar(lectura, ctx.Ejercicio, trimestre, declarante, Rectificativa(anterior), Hoy);
        // Con importes negativos el fichero no se puede escribir: los errores dicen qué corregir.
        if (d.Casillas.Values.Any(v => v < 0m) && !(Es111 && d.Casilla(29) < 0m))
            return Fallo("Negativo", $"El {Modelo} del {trimestre}T no se puede generar.", string.Join(" ", d.Errores), "Corrige lo que se indica y vuelve a intentarlo.");
        var fichero = FicheroRetenciones.Generar(d, ProgramaAeat.Actual);
        var resumen = new List<(string, string)>();
        if (Es111)
        {
            resumen.Add(($"Trabajo: {d.Casilla(1):0} perceptores (01), percepciones (02)", Euros(d.Casilla(2))));
            resumen.Add(("Trabajo: retenciones (03)", Euros(d.Casilla(3))));
            if (d.Casilla(6) != 0m) resumen.Add(("Trabajo en especie: ingresos a cuenta (06)", Euros(d.Casilla(6))));
            if (d.Casilla(9) != 0m || d.Casilla(8) != 0m) resumen.Add(($"Profesionales: {d.Casilla(7):0} perceptores (07), retenciones (09)", Euros(d.Casilla(9))));
            resumen.Add(("Suma de retenciones (28)", Euros(d.Casilla(28))));
            if (d.Casilla(29) != 0m) resumen.Add(("Resultado de anteriores declaraciones (29)", Euros(d.Casilla(29))));
            resumen.Add(("Resultado a ingresar (30)", Euros(d.Casilla(30))));
        }
        else
        {
            resumen.Add(($"Arrendadores (01): {d.Casilla(1):0}; base de las retenciones (02)", Euros(d.Casilla(2))));
            resumen.Add(("Retenciones (03)", Euros(d.Casilla(3))));
            if (d.Casilla(4) != 0m) resumen.Add(("Resultado de anteriores declaraciones (04)", Euros(d.Casilla(4))));
            resumen.Add(("Resultado a ingresar (05)", Euros(d.Casilla(5))));
        }
        var aeat = new DeclaracionAeat(Modelo, ctx.Ejercicio, d.Periodo, fichero, declarante.Nif, declarante.RazonSocial, d.TipoDeclaracion, d.ImporteAIngresar,
            d.TotalRetenciones, declarante.Iban, anterior?.Justificante, resumen, FicheroRetenciones.LeerNif) { Nrc = d.TipoDeclaracion == 'I' ? nrc : null };
        return Resultado<(DeclaracionRetenciones, DeclaracionAeat)>.Correcto((d, aeat));
    }

    /// <summary>Lo que impide presentar la declaración, además de lo que diga la AEAT.</summary>
    public async Task<IReadOnlyList<string>> BloqueosAsync(ContextoSociedadEjercicio ctx, DeclaracionRetenciones d, CancellationToken c = default)
    {
        var r = new List<string>(Bloqueos(await LeerAsync(ctx, c), d.Trimestre));
        r.AddRange(d.Errores);
        return r;
    }

    /// <summary>
    /// La AEAT admite domiciliar el 111 y el 115 hasta 5 días antes del fin del plazo: del 1 al 15 del mes siguiente al trimestre (el
    /// 4T, del 1 al 15 de enero, porque el plazo acaba el 20). Fuera de él, lo que sale a ingresar se paga antes y se presenta con el NRC
    /// (forma I). Devuelve la explicación, o null si se puede domiciliar o no sale a ingresar.
    /// </summary>
    public static string? FueraDePlazoDomiciliacion(DeclaracionRetenciones d, DateTime hoy)
    {
        if (d.TipoDeclaracion is not ('U' or 'I')) return null;
        var (desde, hasta) = PlazoDomiciliacion(d.Ejercicio, d.Trimestre);
        if (hoy.Date >= desde && hoy.Date <= hasta) return null;
        return $"El {d.Modelo} del {d.Periodo} sale a ingresar ({Euros(d.Resultado)}) y el plazo para domiciliarlo " + (hoy.Date < desde ? $"empieza el {desde:dd/MM/yyyy}" : $"terminó el {hasta:dd/MM/yyyy}") +
            ": antes de presentar hay que pagarlo en el banco o en la Sede de la AEAT y escribir el NRC que den.";
    }

    public static (DateTime Desde, DateTime Hasta) PlazoDomiciliacion(int ejercicio, int trimestre) => trimestre == 4
        ? (new DateTime(ejercicio + 1, 1, 1), new DateTime(ejercicio + 1, 1, 15))
        : (new DateTime(ejercicio, 3 * trimestre + 1, 1), new DateTime(ejercicio, 3 * trimestre + 1, 15));

    // ---------- cálculo y comprobaciones (sin BD) ----------

    private DeclaracionRetenciones Declarar(LecturaRetenciones l, int ejercicio, int trimestre, Declarante303 declarante, RectificativaRetenciones? rect, DateTime hoy)
    {
        var del = l.DelTrimestre(trimestre).ToList();
        var d = Calcular(del, ejercicio, trimestre, declarante, rect);
        return FueraDePlazoDomiciliacion(d, hoy) is not null ? Calcular(del, ejercicio, trimestre, declarante, rect, conNrc: true) : d;
    }

    internal IReadOnlyList<string> Bloqueos(LecturaRetenciones l, int trimestre)
    {
        var r = new List<string>();
        var del = l.Pendientes.Where(p => p.Trimestre == trimestre && EsDelModelo(p.Clase)).ToList();
        var sinClase = del.Where(p => p.Clase is null).ToList();
        if (sinClase.Count > 0)
            r.Add($"Hay {Cuantas(sinClase.Count, "factura")} de compra del {trimestre}T con retención que no dice si es de servicios profesionales (111) o de alquileres (115) " +
                $"({Ejemplos(sinClase.Select(p => p.Documento))}): elige el tipo de retención en Compras › Facturas recibidas.");
        var sinContabilizar = del.Where(p => p.Clase is not null && p.SinContabilizar).ToList();
        if (sinContabilizar.Count > 0)
            r.Add($"Hay {Cuantas(sinContabilizar.Count, "factura")} de compra del {trimestre}T con retención sin contabilizar ({Ejemplos(sinContabilizar.Select(p => p.Documento))}, " +
                $"{Euros(sinContabilizar.Sum(p => p.Retencion))} retenidos): contabilízalas desde Compras › Facturas recibidas para que entren en el {Modelo}.");
        return r;
    }

    internal IReadOnlyList<string> Avisos(LecturaRetenciones l, int trimestre)
    {
        var r = new List<string>(l.AvisosDocumentos.Where(a => a.StartsWith($"{trimestre}T|", StringComparison.Ordinal) && EsDelModeloAviso(a)).Select(a => a.Split('|', 3)[2]));
        r.AddRange(CuadreS4(l, trimestre));
        return r;
    }

    /// <summary>Los avisos de documentos llevan «nT|clase|texto»; la clase decide a qué modelo van.</summary>
    private bool EsDelModeloAviso(string aviso)
    {
        var partes = aviso.Split('|', 3);
        return partes.Length == 3 && Enum.TryParse<ClaseRetencionModelo>(partes[1], out var clase) && EsDelModelo(clase);
    }

    /// <summary>
    /// Lo que el aviso de salud S4 espera ingresar por el trimestre en las 4751 de este modelo: los abonos menos los cargos del trimestre
    /// sin la apertura, sumando de nuevo los cargos del primer mes (S4 los cuenta como el pago del trimestre anterior). Se compara con el
    /// total del modelo en las subcuentas que usan sus documentos; si el 111 y el 115 comparten subcuenta, con la suma de los dos. Las
    /// subcuentas 4751 con abonos que no vienen de nóminas ni de facturas se avisan en el 111.
    /// </summary>
    internal IReadOnlyList<string> CuadreS4(LecturaRetenciones l, int trimestre)
    {
        var r = new List<string>();
        var primerMes = 3 * trimestre - 2;
        decimal EsperaS4(string cuenta) => l.Apuntes4751.Where(a => a.Cuenta == cuenta && !a.Apertura && (a.Fecha.Month + 2) / 3 == trimestre)
            .Sum(a => a.Haber - a.Debe + (a.Fecha.Month == primerMes ? a.Debe : 0m));
        HashSet<string> Cuentas(Func<ClaseRetencionModelo, bool> clase) => l.Apuntes4751
            .Where(a => l.AsientosDocumentos.TryGetValue(a.IdAsiento, out var c) && clase(c)).Select(a => a.Cuenta).ToHashSet(StringComparer.Ordinal);
        var de111 = Cuentas(c => c != ClaseRetencionModelo.Arrendamientos);
        var de115 = Cuentas(c => c == ClaseRetencionModelo.Arrendamientos);
        var del = l.DelTrimestre(trimestre).ToList();
        var total111 = del.Where(p => p.Clase != ClaseRetencionModelo.Arrendamientos).Sum(p => p.Retencion + p.IngresoCuenta);
        var total115 = del.Where(p => p.Clase == ClaseRetencionModelo.Arrendamientos).Sum(p => p.Retencion);
        var compartidas = de111.Overlaps(de115);
        var cuentas = compartidas ? de111.Union(de115).ToHashSet() : Es111 ? de111 : de115;
        var total = compartidas ? total111 + total115 : Es111 ? total111 : total115;
        var que = compartidas ? "el 111 y el 115" : $"el {Modelo}";
        var espera = cuentas.Sum(EsperaS4);
        if (Math.Abs(espera - total) > 0.05m)
            r.Add($"Las 4751 de {que} ({(cuentas.Count == 0 ? "ninguna" : string.Join(", ", cuentas.Order()))}) recogen {Euros(espera)} en el {trimestre}T y {que} da {Euros(total)} " +
                $"(diferencia {Euros(espera - total)}): el aviso S4 esperará pagar lo de las 4751. Busca un asiento manual a la 4751, una nómina o factura contabilizada con otra " +
                "retención o un documento sin contabilizar, y corrígelo antes de presentar.");
        if (Es111)
        {
            var otras = l.Apuntes4751.Select(a => a.Cuenta).Distinct().Where(x => !de111.Contains(x) && !de115.Contains(x))
                .Select(x => (Cuenta: x, Importe: l.Apuntes4751.Where(a => a.Cuenta == x && !a.Apertura && (a.Fecha.Month + 2) / 3 == trimestre).Sum(a => a.Haber)))
                .Where(x => x.Importe != 0m).OrderBy(x => x.Cuenta).ToList();
            if (otras.Count > 0)
                r.Add($"{string.Join(", ", otras.Select(o => $"{o.Cuenta} ({Euros(o.Importe)})"))} recibe{(otras.Count == 1 ? "" : "n")} retenciones en el {trimestre}T sin nóminas ni facturas " +
                    "de compra con retención: no entran en el 111 ni en el 115. Si son retenciones de dividendos van en el 123; si son del trabajo o de profesionales contabilizadas a mano, " +
                    "faltan en el modelo: regístralas como nómina o factura de compra.");
        }
        return r;
    }

    // ---------- lecturas ----------

    private static RectificativaRetenciones? Rectificativa(PresentacionGuardada? anterior) =>
        anterior is null ? null : new RectificativaRetenciones(anterior.Justificante, anterior.Resultado);

    private Task<Declarante303> DeclaranteAsync(ContextoSociedadEjercicio ctx) => bd.EjecutarAsync(async s =>
    {
        var conIban = s.LeerEsquema().ExisteColumna("Sociedades", "IbanDomiciliacion");
        return await s.ConsultarPrimeroAsync($"SELECT Nombre, CIF{(conIban ? ", IbanDomiciliacion" : "")} FROM Sociedades WHERE IdSociedad = ?",
            f => new Declarante303((f.Texto("CIF") ?? "").Trim().ToUpperInvariant(), (f.Texto("Nombre") ?? "").Trim(), conIban ? f.Texto("IbanDomiciliacion")?.Trim() : null),
            ctx.IdSociedad) ?? new Declarante303("", "", null);
    });

    /// <summary>Nóminas, facturas de compra con retención y apuntes de las 4751 del ejercicio.</summary>
    public Task<LecturaRetenciones> LeerAsync(ContextoSociedadEjercicio ctx, CancellationToken c = default) => bd.EjecutarAsync(async s =>
    {
        var esquema = s.LeerEsquema();
        var percepciones = new List<PercepcionRetencion>();
        var pendientes = new List<DocumentoRetencionPendiente>();
        var avisos = new List<string>();
        var asientos = new Dictionary<int, ClaseRetencionModelo>();
        int Trimestre(DateTime f) => (f.Month + 2) / 3;

        // Nóminas de las importaciones vigentes.
        if (esquema.ExisteTabla("Nominas") && esquema.ExisteTabla("ImportacionesNomina"))
        {
            var nominas = await s.ConsultarAsync(
                "SELECT n.IdNomina, n.IdEmpleado, n.CodigoTrabajador, n.NombreOrigen, n.NifOrigen, n.FechaNomina, n.TipoPaga, n.Bruto, n.DtoReEsp, n.InRetEsp, n.Autonomo, n.IRPF111, n.IdAsiento " +
                "FROM Nominas AS n INNER JOIN ImportacionesNomina AS i ON n.IdImportacionNomina = i.IdImportacionNomina " +
                "WHERE i.IdSociedadEjercicio = ? AND i.Estado <> 'REEMPLAZADA' AND i.Estado <> 'DESHECHA' ORDER BY n.FechaNomina, n.IdNomina",
                f => (Id: f.Entero("IdNomina"), Empleado: f.EnteroNulo("IdEmpleado"), Codigo: f.Texto("CodigoTrabajador"), Nombre: f.Texto("NombreOrigen"), Nif: f.Texto("NifOrigen"),
                    Fecha: f.Fecha("FechaNomina"), Paga: f.Texto("TipoPaga"), Bruto: f.DecimalNulo("Bruto") ?? 0m, DtoEsp: f.DecimalNulo("DtoReEsp") ?? 0m,
                    InEsp: f.DecimalNulo("InRetEsp") ?? 0m, Autonomo: f.DecimalNulo("Autonomo") ?? 0m, Irpf: f.DecimalNulo("IRPF111") ?? 0m, Asiento: f.EnteroNulo("IdAsiento")),
                ctx.IdSociedadEjercicio);
            var sinAsiento = new Dictionary<int, int>();
            foreach (var n in nominas.Where(n => n.Fecha.Year == ctx.Ejercicio))
            {
                var perceptor = n.Empleado is int e ? $"E{e}" : FirstNoVacio(n.Nif, n.Codigo, n.Nombre) ?? $"N{n.Id}";
                var (dinerario, retencion, especie, ingreso) = DesgloseNomina(n.Bruto, n.DtoEsp, n.Autonomo, n.InEsp, n.Irpf);
                percepciones.Add(new PercepcionRetencion(ClaseRetencionModelo.Trabajo, perceptor, n.Fecha, dinerario, retencion, especie, ingreso,
                    $"Nómina {n.Paga?.Trim().ToLowerInvariant()} del {n.Fecha:dd/MM/yyyy}"));
                if (n.Asiento is int a) asientos[a] = ClaseRetencionModelo.Trabajo;
                else sinAsiento[Trimestre(n.Fecha)] = sinAsiento.GetValueOrDefault(Trimestre(n.Fecha)) + 1;
            }
            foreach (var (t, cuantas) in sinAsiento)
                avisos.Add($"{t}T|{ClaseRetencionModelo.Trabajo}|Hay {Cuantas(cuantas, "nómina")} del {t}T sin contabilizar: el 111 ya las incluye, pero la 4751 no las tendrá " +
                    "hasta contabilizarlas desde Nóminas.");
        }

        // Facturas de compra con retención.
        if (esquema.ExisteTabla("FacturasCompra") && esquema.ExisteColumna("FacturasCompra", "ImporteRetencion"))
        {
            var conAsiento = esquema.ExisteColumna("FacturasCompra", "IdAsientoContable");
            var facturas = await s.ConsultarAsync(
                "SELECT fc.IdRegistro, fc.NumeroFactura, fc.Fecha, fc.NIF, fc.IdProveedor, fc.CuentaContableProveedor, fc.ImporteBI, fc.ImporteRetencion, fc.PorcentajeRetencion, " +
                "fc.TipoRetencion, fc.Exportada" + (conAsiento ? ", fc.IdAsientoContable, a.FechaContable FROM FacturasCompra AS fc LEFT JOIN Asientos AS a ON fc.IdAsientoContable = a.IdAsiento "
                    : " FROM FacturasCompra AS fc ") +
                "WHERE fc.IdSociedadEjercicio = ? AND (fc.Duplicada = False OR fc.Duplicada IS NULL) AND (fc.ImporteRetencion <> 0 OR fc.PorcentajeRetencion <> 0) ORDER BY fc.Fecha, fc.IdRegistro",
                f => (Id: f.Entero("IdRegistro"), Numero: f.Texto("NumeroFactura"), Fecha: f.FechaNula("Fecha"), Nif: f.Texto("NIF"), Proveedor: f.EnteroNulo("IdProveedor"),
                    Cuenta: f.Texto("CuentaContableProveedor"), Base: f.DecimalNulo("ImporteBI"), Retencion: f.DecimalNulo("ImporteRetencion"),
                    Pct: f.DecimalNulo("PorcentajeRetencion"), Tipo: f.EnteroNulo("TipoRetencion"), Exportada: !f.EsNulo("Exportada") && f.Logico("Exportada"),
                    Asiento: conAsiento ? f.EnteroNulo("IdAsientoContable") : null, FechaAsiento: conAsiento ? f.FechaNula("FechaContable") : null),
                ctx.IdSociedadEjercicio);
            foreach (var f0 in facturas)
            {
                // Las facturas migradas del ERP traen el porcentaje y no el importe: la retención es la base por el porcentaje, redondeada por factura.
                var fc = f0 with { Retencion = f0.Retencion is decimal ret && ret != 0m ? ret
                    : f0.Pct is > 0m && f0.Base is decimal bb ? decimal.Round(bb * f0.Pct.Value / 100m, 2, MidpointRounding.AwayFromZero) : 0m };
                if (fc.Retencion == 0m) continue;
                var fecha = fc.FechaAsiento ?? fc.Fecha ?? new DateTime(ctx.Ejercicio, 1, 1);
                if (fecha.Year != ctx.Ejercicio) fecha = fecha.Year < ctx.Ejercicio ? new DateTime(ctx.Ejercicio, 1, 1) : new DateTime(ctx.Ejercicio, 12, 31);
                var t = Trimestre(fecha);
                var documento = $"{(string.IsNullOrWhiteSpace(fc.Numero) ? $"registro {fc.Id}" : fc.Numero!.Trim())} del {(fc.Fecha ?? fecha):dd/MM/yyyy}";
                ClaseRetencionModelo? clase = fc.Tipo switch { 1 => ClaseRetencionModelo.Profesionales, 2 => ClaseRetencionModelo.Arrendamientos, _ => null };
                var contabilizada = fc.Exportada && (fc.Asiento is not null || !conAsiento);
                if (clase is null || !contabilizada)
                {
                    pendientes.Add(new DocumentoRetencionPendiente(clase, t, documento, !contabilizada, fc.Retencion.Value));
                    if (clase is null) continue;
                    if (!contabilizada) continue;
                }
                var baseRet = fc.Base ?? (fc.Pct is > 0m ? decimal.Round(fc.Retencion.Value * 100m / fc.Pct.Value, 2, MidpointRounding.AwayFromZero) : 0m);
                // El proveedor de la ficha antes que el NIF leído de la factura (el reconocimiento a veces lee otro NIF del documento).
                var perceptor = fc.Proveedor is int idp ? $"P{idp}" : FirstNoVacio(fc.Nif?.Replace(" ", "").ToUpperInvariant(), fc.Cuenta) ?? $"F{fc.Id}";
                percepciones.Add(new PercepcionRetencion(clase.Value, perceptor, fecha, baseRet, fc.Retencion.Value, Documento: $"Factura {documento}"));
                if (fc.Asiento is int a) asientos[a] = clase.Value;
                if (fc.Pct is > 0m && fc.Base is decimal b && Math.Abs(decimal.Round(b * fc.Pct.Value / 100m, 2, MidpointRounding.AwayFromZero) - fc.Retencion.Value) > 0.05m)
                    avisos.Add($"{t}T|{clase}|La retención de la factura {documento} ({Euros(fc.Retencion.Value)}) no es el {fc.Pct.Value:0.##} % de su base ({Euros(b)}): revisa la factura.");
            }
        }

        // Apuntes de las 4751 del ejercicio, sin la regularización ni el cierre.
        var apuntes = await s.ConsultarAsync(
            "SELECT p.IdAsiento, p.CuentaContable, a.FechaContable, a.TipoAsiento, p.ImporteDebe, p.ImporteHaber FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.IdSociedadEjercicio = ? AND a.EstadoAsiento = 1 AND p.CuentaContable LIKE '4751%' AND (a.TipoAsiento IS NULL OR a.TipoAsiento NOT IN (2, 3))",
            f => new ApunteRetencion(f.Entero("IdAsiento"), (f.Texto("CuentaContable") ?? "").Trim(), f.Fecha("FechaContable"), f.EnteroNulo("TipoAsiento") == 1,
                f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m),
            ctx.IdSociedadEjercicio);
        return new LecturaRetenciones(percepciones, pendientes, avisos, apuntes, asientos);
    }, c);

    /// <summary>
    /// Reparto de una nómina en el 111 (como lo declara la gestoría): especie = la cuota de autónomo pagada por la empresa (Autonomo);
    /// dinerario = bruto + descuento por retribución en especie (DtoReEsp, en negativo) − especie. La retención de la nómina (IRPF111,
    /// lo que va a la 4751) se reparte: con ingreso a cuenta propio (InRetEsp), ese es el de la especie y el resto la retención dineraria;
    /// sin él, la parte de la especie es IRPF111 × especie / (dinerario + especie), redondeada a céntimos en cada nómina. Así la suma de
    /// 03 y 06 es siempre lo retenido en la nómina.
    /// </summary>
    public static (decimal Dinerario, decimal Retencion, decimal Especie, decimal IngresoCuenta) DesgloseNomina(decimal bruto, decimal dtoReEsp, decimal autonomo, decimal inRetEsp,
        decimal irpf)
    {
        var especie = autonomo;
        var dinerario = bruto + dtoReEsp - especie;
        decimal ingreso;
        if (inRetEsp != 0m) ingreso = inRetEsp;
        else if (especie > 0m && dinerario + especie > 0m) ingreso = decimal.Round(irpf * especie / (dinerario + especie), 2, MidpointRounding.AwayFromZero);
        else ingreso = 0m;
        return (dinerario, irpf - ingreso, especie, ingreso);
    }

    private static string? FirstNoVacio(params string?[] valores) => valores.Select(v => v?.Trim()).FirstOrDefault(v => !string.IsNullOrEmpty(v));

    private static string Cuantas(int n, string que) => $"{n} {que}{(n == 1 ? "" : "s")}";

    private static string Ejemplos(IEnumerable<string> docs)
    {
        var l = docs.ToList();
        return string.Join(", ", l.Take(5)) + (l.Count > 5 ? "…" : "");
    }

    private static string Euros(decimal v) => CalculoRetenciones.Euros(v);
}
