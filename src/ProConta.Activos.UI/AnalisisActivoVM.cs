using ProConta.Activos.Financieros;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Graficos;
using ProConta.UI.Base.Modelos;

namespace ProConta.Activos.UI;

/// <summary>Una fila de la pestaña Lotes del análisis.</summary>
public sealed record FilaLoteAnalisis(string Comprado, string Compradas, string Quedan, string Precio, string Coste, string Valor, string Plusvalia, string Porcentaje,
    string Anual, string Tiempo, string Tono, bool Vendido);

/// <summary>Una señal de la proyección con su tono para colorear la lectura.</summary>
public sealed record SenalAnalisis(string Titulo, string Lectura, string Explicacion, string Detalle, string Tono);

/// <summary>Una fila de los escenarios: nombre y valor a 1, 3 y 5 años con lo que suponen.</summary>
public sealed record FilaEscenario(string Nombre, string Ayuda, IReadOnlyList<CeldaEscenario> Celdas);

/// <summary>Valor de un escenario en un plazo, con la ganancia o pérdida y la rentabilidad anual equivalente.</summary>
public sealed record CeldaEscenario(string Valor, string Diferencia, string Tono);

/// <summary>Un nivel de soporte o resistencia del mes.</summary>
public sealed record NivelPivote(string Nombre, string Precio, string Ayuda, bool EsPivote);

/// <summary>
/// Análisis de un activo financiero dentro de su ficha (ergonomía de activos 2026-11-26: un solo sitio por activo): cifras, cotización histórica con el
/// precio medio de compra y las operaciones, lotes a coste medio ponderado y proyección (señales técnicas en frases y escenarios), todo de solo lectura.
/// Las operaciones están en el apartado Operaciones de la ficha, donde se cambian.
/// </summary>
public sealed class AnalisisActivoVM : ModeloVista
{
    public static readonly IReadOnlyList<string> Periodos = ["6M", "1A", "3A", "5A", "Máx."];

    private string _periodo = "3A";
    private DatosTemporales? _datos;

    internal AnalisisActivoVM(ActivoDeCartera activo, string? periodo = null)
    {
        Activo = activo;
        if (periodo is not null) _periodo = periodo;
        Cifras = CrearCifras();
        Lotes = CrearLotes();
        CrearProyeccion();
        PintarCotizacion();
    }

    public ActivoDeCartera Activo { get; }
    private PosicionFinanciera P => Activo.Posicion;
    private bool EsFondo => TextosCartera.Clase(P) == "Fondo";

    public string Nombre => P.Instrumento.Nombre.Length > 0 ? P.Instrumento.Nombre : Activo.Nombre;
    public string Clase => TextosCartera.Clase(P);
    public string Meta => string.Join(" · ", new[]
    {
        Activo.Codigo, P.Instrumento.Isin.Length > 0 ? $"ISIN {P.Instrumento.Isin}" : null, P.Instrumento.TickerYahoo, P.Instrumento.Bolsa,
        P.Instrumento.Moneda.Equals("EUR", StringComparison.OrdinalIgnoreCase) ? null : $"cotiza en {P.Instrumento.Moneda} (se pasa a euros con el cambio del BCE)",
        Activo.FechaBaja is DateTime b ? $"de baja desde el {Formatos.Fecha(b)}" : null,
    }.Where(t => !string.IsNullOrEmpty(t)));

    /// <summary>Si tiene cotizaciones (si no, solo Lotes y Operaciones).</summary>
    public bool Cotiza => P.Historico.Count > 0;

    public IReadOnlyList<CifraCartera> Cifras { get; }

    private IReadOnlyList<CifraCartera> CrearCifras()
    {
        var nota = P.Precio is null ? "a coste: no cotiza"
            : $"{(EsFondo ? "valor liquidativo" : "cotización")} del {Formatos.Fecha(P.FechaPrecio!.Value)} · {P.OrigenPrecio}";
        var ventas = P.Ventas.Count;
        return
        [
            new("Participaciones", TextosCartera.Participaciones(P.Participaciones), $"{P.LotesVivos} {(P.LotesVivos == 1 ? "lote vivo" : "lotes vivos")}", "Participaciones que quedan en cartera"),
            new("Coste", Formatos.Importe(P.Coste), P.PrecioMedio is decimal pm ? $"precio medio {TextosCartera.Num(pm, 3)} €" : "sin participaciones",
                "Lo que costó lo que queda, comisiones incluidas"),
            new("Valor", Formatos.Importe(P.Valor), nota, "Participaciones por la última cotización en euros"),
            new("Plusvalía latente", TextosCartera.Signo(P.Plusvalia), TextosCartera.Pct(P.PlusvaliaPorcentaje), "Valor menos coste de lo que queda", TextosCartera.Tono(P.Plusvalia)),
            new("Rentabilidad anual", TextosCartera.Pct(P.RentabilidadAnual), P.RentabilidadAnual is null ? "con menos de un año no se calcula" : "TIR desde la primera compra",
                "Rentabilidad media por año, teniendo en cuenta cuándo entró y salió cada euro", TextosCartera.Tono(P.RentabilidadAnual)),
            new("Resultado de ventas", TextosCartera.Signo(P.ResultadoVentas), ventas == 0 ? "sin ventas" : $"{ventas} {(ventas == 1 ? "venta" : "ventas")} · 766 / 666",
                "Importe cobrado menos el coste medio ponderado de lo vendido", TextosCartera.Tono(P.ResultadoVentas)),
        ];
    }

    // ---------- Cotización ----------

    public string Periodo { get => _periodo; set { if (Fijar(ref _periodo, value ?? "3A")) PintarCotizacion(); } }
    public DatosTemporales? DatosCotizacion { get => _datos; private set => Fijar(ref _datos, value); }
    public string NombrePrecio => EsFondo ? "Valor liquidativo" : "Cotización";
    public string ExplicacionCotizacion => (EsFondo ? "Valor liquidativo diario" : "Cierre diario en bolsa") + " en euros · la línea fina a trazos es el tiempo sin participaciones";
    public string OrigenCotizacion => P.Precio is null ? "" :
        $"Último precio: {TextosCartera.Num(P.Precio.Value, 4)} € del {Formatos.Fecha(P.FechaPrecio!.Value)} ({P.OrigenPrecio}). " +
        (EsFondo ? "Los fondos publican un único valor al día. " : "") +
        "El cierre de cada día se guarda para dibujar el histórico; el precio del día sale de EODHD la primera vez que se abre esta pantalla y después de Yahoo Finance.";
    public Func<double, string> FormatoEje { get; } = v => TextosCartera.Num((decimal)v, Math.Abs(v) < 20 ? 2 : 0) + " €";

    private void PintarCotizacion()
    {
        if (!Cotiza) return;
        var desde = _periodo switch
        {
            "6M" => P.Hoy.AddMonths(-6),
            "1A" => P.Hoy.AddYears(-1),
            "3A" => P.Hoy.AddYears(-3),
            "5A" => P.Hoy.AddYears(-5),
            _ => P.Historico.Primera ?? P.Hoy,
        };
        var puntos = P.Historico.Desde(desde).ToList();
        var paso = Math.Max(1, (int)Math.Ceiling(puntos.Count / 700.0));
        if (paso > 1) puntos = puntos.Where((_, i) => i % paso == 0 || i == puntos.Count - 1).ToList();
        if (puntos.Count < 2) { DatosCotizacion = null; return; }
        var fechas = puntos.Select(p => p.Fecha).ToList();
        var tenia = fechas.Select(f => P.ParticipacionesEn(f) > 0m).ToArray();
        // Con participaciones, línea normal; sin ellas, fina y a trazos. El punto del cambio va en las dos para que no quede un hueco.
        var con = puntos.Select((p, i) => tenia[i] || (i > 0 && tenia[i - 1]) || (i + 1 < tenia.Length && tenia[i + 1]) ? (double?)(double)p.Precio : null).ToList();
        var sin = puntos.Select((p, i) => !tenia[i] || (i > 0 && !tenia[i - 1]) || (i + 1 < tenia.Length && !tenia[i + 1]) ? (double?)(double)p.Precio : null).ToList();
        var medio = fechas.Select(f => P.PrecioMedioEn(f) is decimal m ? (double?)(double)m : null).ToList();
        var ops = P.Inversion.Operaciones.Where(o => o.Fecha >= fechas[0] && o.Fecha <= fechas[^1] && (o.EsCompra || o.Tipo == OperacionDeInversion.TipoVenta))
            .Select(o => (o, Indice: fechas.FindIndex(f => f >= o.Fecha.Date))).Where(x => x.Indice >= 0).ToList();
        ContenidoTooltip Tip(int i)
        {
            var filas = new List<FilaTooltip> { new(EsFondo ? "Valor liquidativo" : "Cierre", TextosCartera.Num(puntos[i].Precio, 4) + " €", PaletaGraficos.Serie1, true) };
            if (medio[i] is double m)
                filas.Add(new FilaTooltip("Precio medio de compra", $"{TextosCartera.Num((decimal)m, 3)} € · {TextosCartera.Pct(puntos[i].Precio / (decimal)m - 1m)}", PaletaGraficos.Serie2));
            else filas.Add(new FilaTooltip("Sin participaciones", ""));
            foreach (var (o, _) in ops.Where(x => x.Indice == i))
                filas.Add(new FilaTooltip(o.EsCompra ? "▲ Compra" : "▼ Venta", $"{TextosCartera.Participaciones(o.Participaciones)} part. · {Formatos.Importe(o.ImporteTotalEur)}"));
            return new ContenidoTooltip(fechas[i].ToString("d 'de' MMMM 'de' yyyy", Formatos.Cultura), filas);
        }
        DatosCotizacion = new DatosTemporales(fechas,
            [
                new SerieTemporal(NombrePrecio, PaletaGraficos.Serie1, con, Grosor: 2.2),
                new SerieTemporal("Sin participaciones", PaletaGraficos.Serie1, sin, Discontinua: true, Grosor: 1.2),
                new SerieTemporal("Precio medio de compra", PaletaGraficos.Serie2, medio, Discontinua: true, Grosor: 1.5),
            ],
            Tip, Marcas: ops.Select(x => new MarcaTemporal(x.Indice, x.o.EsCompra)).ToList(), Descripcion: $"Cotización histórica de {Nombre} en euros");
    }

    // ---------- Lotes ----------

    public IReadOnlyList<FilaLoteAnalisis> Lotes { get; }

    private IReadOnlyList<FilaLoteAnalisis> CrearLotes() => P.Lotes.Select(l =>
    {
        if (l.Vivas <= 0m)
            return new FilaLoteAnalisis(Formatos.Fecha(l.Fecha), TextosCartera.Participaciones(l.Compradas), "0", TextosCartera.Num(l.CosteUnitario, 3), "", "", "vendido entero", "", "", "", "", true);
        var valor = P.ValorLote(l);
        var plus = valor - l.CosteVivo;
        return new FilaLoteAnalisis(Formatos.Fecha(l.Fecha), TextosCartera.Participaciones(l.Compradas), TextosCartera.Participaciones(l.Vivas), TextosCartera.Num(l.CosteUnitario, 3),
            Formatos.Numero(l.CosteVivo), Formatos.Numero(valor), (plus > 0 ? "+" : "") + Formatos.Numero(plus), TextosCartera.Pct(l.CosteVivo > 0m ? plus / l.CosteVivo : null),
            TextosCartera.Pct(P.RentabilidadAnualLote(l)), TextosCartera.Tiempo(l.Fecha, P.Hoy), TextosCartera.Tono(plus), false);
    }).ToList();

    // ---------- Proyección ----------

    public IReadOnlyList<SenalAnalisis> Senales { get; private set; } = [];
    public string Valoracion { get; private set; } = "";
    public string TonoValoracion { get; private set; } = "";
    public string ExplicacionValoracion { get; private set; } = "";
    public bool HaySenales => Senales.Count > 0;
    public string TituloPivotes { get; private set; } = "";
    public IReadOnlyList<NivelPivote> Pivotes { get; private set; } = [];
    public bool HayPivotes => Pivotes.Count > 0;
    public string ExplicacionEscenarios { get; private set; } = "";
    public IReadOnlyList<FilaEscenario> Escenarios { get; private set; } = [];
    public bool HayEscenarios => Escenarios.Count > 0;
    public string SinProyeccion { get; private set; } = "";

    private void CrearProyeccion()
    {
        if (!Cotiza) return;
        var valor = P.Viva ? P.Valor : 0m;
        var a = AnalisisTecnico.Analizar(P.Historico, valor > 0m ? valor : 1000m);
        Senales = a.Senales.Select(s => new SenalAnalisis(s.Titulo, s.Lectura, s.Explicacion, s.Detalle + (s.Puntuacion is int p ? $" · puntuación {(p > 0 ? "+" : "")}{p}" : " · solo informa, no puntúa"),
            s.Puntuacion switch { > 0 => "Bueno", < 0 => "Malo", _ => "" })).ToList();
        Valoracion = a.Valoracion;
        TonoValoracion = a.Valoracion switch { "Favorable" => "Bueno", "Desfavorable" => "Malo", _ => "" };
        ExplicacionValoracion = a.PuntuacionMedia is decimal m
            ? $"Media de las señales que puntúan: {(m > 0 ? "+" : "")}{TextosCartera.Num(m, 2)} en una escala de −2 a +2 (favorable desde +{TextosCartera.Num(AnalisisTecnico.UmbralValoracion, 1)}, desfavorable desde −{TextosCartera.Num(AnalisisTecnico.UmbralValoracion, 1)}). Orientativo: resume cómo se ha movido el precio, no adivina el futuro."
            : $"Hacen falta al menos {AnalisisTecnico.MinimoCierres} cotizaciones diarias para leer las señales.";
        if (a.Pivotes is { } pv)
        {
            TituloPivotes = $"Soportes y resistencias de {P.Hoy.ToString("MMMM", Formatos.Cultura)} (con los precios de {pv.Mes.ToString("MMMM", Formatos.Cultura)})";
            static string E(decimal v) => TextosCartera.Num(v, v < 20 ? 3 : 2) + " €";
            Pivotes =
            [
                new("S3", E(pv.S3), "Tercer soporte: suelo muy fuerte", false), new("S2", E(pv.S2), "Segundo soporte", false),
                new("S1", E(pv.S1), "Primer soporte: precio donde suele frenar una bajada", false),
                new("Pivote", E(pv.Pivote), "Precio de equilibrio del mes: por encima, el tono es alcista; por debajo, bajista", true),
                new("R1", E(pv.R1), "Primera resistencia: precio donde suele frenar una subida", false), new("R2", E(pv.R2), "Segunda resistencia", false),
                new("R3", E(pv.R3), "Tercera resistencia: techo muy fuerte", false),
            ];
        }
        if (a.TieneProyeccion && valor > 0m)
        {
            ExplicacionEscenarios = $"En los últimos {TextosCartera.Num(a.AniosHistorico, 1)} años ha rendido de media un {TextosCartera.Num(a.RentabilidadHistorica!.Value * 100m, 1)} % al año, " +
                $"con altibajos de unos {TextosCartera.Num(a.Volatilidad!.Value * 100m, 0)} puntos arriba o abajo. Con eso salen tres escenarios sobre el valor de hoy ({Formatos.Importe(valor)}):";
            Escenarios = a.Escenarios.Select(e => new FilaEscenario(e.Nombre,
                e.Desviaciones switch { < 0 => "Lo que saldría con una racha mala de las habituales", > 0 => "Lo que saldría con una racha buena de las habituales", _ => "Si sigue rindiendo como de media" },
                e.Valores.Select(v => new CeldaEscenario(TextosCartera.Num(v.Valor, 0) + " €",
                    $"{(v.Valor - valor > 0 ? "+" : "")}{TextosCartera.Num(v.Valor - valor, 0)} € · {TextosCartera.Pct(v.RentabilidadAnual)} al año", TextosCartera.Tono(v.Valor - valor))).ToList())).ToList();
        }
        else SinProyeccion = valor <= 0m ? "Ya no hay participaciones: no hay posición que proyectar."
            : $"Hace falta al menos un año de cotizaciones para estimar la rentabilidad y los altibajos (hay {TextosCartera.Num(a.AniosHistorico, 1)} años).";
    }
}
