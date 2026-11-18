namespace ProConta.Inicio.Salud;

/// <summary>Gravedad de un elemento de salud. Se muestra siempre con icono y texto, nunca solo con color.</summary>
public enum GravedadSalud { Grave, Atencion, Aviso }

/// <summary>Tarea con plazo (algo que toca hacer) o aviso (algo que corregir).</summary>
public enum TipoSalud { Tarea, Aviso }

/// <summary>
/// Pantalla donde se resuelve un elemento. <see cref="Ninguna"/>: todavía no hay pantalla en el contenedor (el tooltip lo dice y
/// no se abre nada).
/// </summary>
public enum PantallaSalud { Ninguna, VentasPorFacturar, Ejercicios, LineasN43, Bancos, FacturasCompra, FacturasVenta, Mayor, Diario, Terceros, Nominas, Programadas, LiquidacionIva,
    /// <summary>Contabilidad › Procesos contables › Fin de ejercicio › Amortización de activos (contabilidad/procesos/amortizacion-de-activos).</summary>
    Amortizacion,
    /// <summary>Contabilidad › Procesos contables › Fin de ejercicio › Céntimos residuales (contabilidad/procesos/centimos-residuales).</summary>
    CentimosResiduales,
    /// <summary>No abre una pantalla: compensa las comisiones de las plataformas pendientes (facturacion/reservas/compensacion-comision).</summary>
    CompensarComisiones,
    /// <summary>Contabilidad › Modelos tributarios, en el periodo del destino (contabilidad/modelos-tributarios).</summary>
    ModelosTributarios }

/// <summary>Dónde se resuelve: la pantalla, su nombre en el menú y, para el Mayor, las cuentas con las que se abre.</summary>
public sealed record DestinoSalud(PantallaSalud Pantalla, string Nombre, IReadOnlyList<string>? Cuentas = null, string? Modelo = null, string? Periodo = null)
{
    /// <summary>Contabilidad › Modelos tributarios con el periodo de un modelo marcado («303», «4T»).</summary>
    public static DestinoSalud Modelos(string modelo, string periodo) => new(PantallaSalud.ModelosTributarios, "Contabilidad › Modelos tributarios", null, modelo, periodo);

    public static DestinoSalud SinPantalla(string nombre) => new(PantallaSalud.Ninguna, nombre);
    public static DestinoSalud Mayor(IReadOnlyList<string> cuentas, string detalle) => new(PantallaSalud.Mayor, $"Contabilidad › Mayor ({detalle})", cuentas);
}

/// <summary>
/// Una tarea o un aviso de la salud de la contabilidad: título, explicación con las cifras, plazo si lo tiene, pantalla donde se
/// resuelve, nodo que lo detecta, condición por la que aparece y cómo se resuelve («¿Por qué aparece?»). No se pospone ni se
/// silencia: desaparece solo cuando deja de cumplirse su condición.
/// </summary>
public sealed record ElementoSalud(
    string Clave, TipoSalud Tipo, GravedadSalud Gravedad, string Titulo, string Explicacion, DateTime? Plazo, DestinoSalud Destino,
    string Nodo, string Condicion, string ComoSeResuelve)
{
    /// <summary>
    /// Número de cosas que cuenta el elemento (movimientos, facturas, subcuentas…) para la tendencia «desde el lunes»; null si el
    /// elemento no cuenta nada (entonces cuenta como 1).
    /// </summary>
    public int? Cantidad { get; init; }

    /// <summary>Importe asociado al elemento, si lo tiene (se guarda en el histórico de la tendencia).</summary>
    public decimal? Importe { get; init; }

    /// <summary>Días que faltan hasta el plazo (negativo si está vencido); null sin plazo.</summary>
    public int? DiasHastaPlazo(DateTime hoy) => Plazo is DateTime p ? (p.Date - hoy.Date).Days : null;
}

/// <summary>Tarea hecha en el trimestre (solo las que se pueden detectar, como el 303 con su asiento de liquidación).</summary>
public sealed record HechaSalud(string Clave, string Titulo, string Explicacion, DestinoSalud Destino, string Nodo);

/// <summary>Una presentación guardada de un modelo (desde el programa o traída por «Consultar a la AEAT»).</summary>
public sealed record PresentacionInicio(string Modelo, int Ejercicio, string Periodo, DateTime FechaHora);

/// <summary>
/// Lo que Inicio sabe de los modelos tributarios de la sociedad (contabilidad/modelos-tributarios 2026-11-18): los modelos que salen en
/// los plazos y los avisos («Modelos de la sociedad…»; sin elegir, el 303 y el 390 más los que tienen presentaciones), lo presentado y
/// cuándo se consultó por última vez a la AEAT.
/// </summary>
public sealed record ModelosInicio(IReadOnlySet<string> Modelos, IReadOnlyList<PresentacionInicio> Presentadas, DateTime? UltimaConsulta)
{
    public static ModelosInicio PorDefecto { get; } = new(new HashSet<string> { "303", "390" }, [], null);

    public PresentacionInicio? Presentada(string modelo, int ejercicio, string periodo) =>
        Presentadas.Where(p => p.Modelo == modelo && p.Ejercicio == ejercicio && p.Periodo == periodo).OrderBy(p => p.FechaHora).LastOrDefault();

    /// <summary>Lee los modelos de la sociedad, sus presentaciones y la última consulta (esquema 73; sin él, lo de siempre).</summary>
    public static async Task<ModelosInicio> LeerAsync(ProConta.Datos.Persistencia.IBaseDatos bd, string idSociedad, CancellationToken c = default)
    {
        var modelos = await ProConta.Contabilidad.Modelos.ServicioModelosTributarios.ParaInicioAsync(bd, idSociedad);
        return await bd.EjecutarAsync(async s =>
        {
            var esquema = s.LeerEsquema();
            IReadOnlyList<PresentacionInicio> presentadas = esquema.ExisteTabla("PresentacionesAeat")
                ? await s.ConsultarAsync("SELECT Modelo, Ejercicio, Periodo, FechaHora FROM PresentacionesAeat WHERE IdSociedad = ?",
                    f => new PresentacionInicio((f.Texto("Modelo") ?? "").Trim(), Convert.ToInt32(f.Valor("Ejercicio"), System.Globalization.CultureInfo.InvariantCulture),
                        (f.Texto("Periodo") ?? "").Trim(), f.Fecha("FechaHora")), idSociedad)
                : [];
            var ultima = esquema.ExisteColumna("Sociedades", "UltimaConsultaModelosAeat")
                ? await s.EscalarAsync("SELECT UltimaConsultaModelosAeat FROM Sociedades WHERE IdSociedad = ?", idSociedad) as DateTime? : null;
            return new ModelosInicio(modelos, presentadas, ultima);
        }, c);
    }
}

/// <summary>Comprobación que se ha hecho y está bien («Comprobaciones correctas»).</summary>
public sealed record ComprobacionCorrecta(string Texto, string Nodo);

/// <summary>Estado de una celda de «Estado de los periodos».</summary>
public enum EstadoPeriodo { NoToca, Hecho, Incompleto, ConErrores, EnCurso }

/// <summary>Celda de un mes (o de un trimestre) con su estado y el texto del tooltip.</summary>
public sealed record CeldaPeriodo(EstadoPeriodo Estado, string Ayuda);

/// <summary>Fila de «Estado de los periodos»: 12 celdas mensuales o 4 trimestrales, y la pantalla que abre el doble clic.</summary>
public sealed record FilaPeriodos(string Nombre, IReadOnlyList<CeldaPeriodo> Celdas, DestinoSalud Destino, string Nodo, int Orden)
{
    public bool Trimestral => Celdas.Count == 4;
}

/// <summary>Lo que encuentra una revisión.</summary>
public sealed record HallazgosSalud(
    IReadOnlyList<ElementoSalud> Elementos, IReadOnlyList<HechaSalud> Hechas, IReadOnlyList<ComprobacionCorrecta> Correctas, IReadOnlyList<FilaPeriodos> Periodos)
{
    public static HallazgosSalud Nada { get; } = new([], [], [], []);
    public static HallazgosSalud De(params ElementoSalud[] elementos) => new(elementos, [], [], []);
    public static HallazgosSalud Correcta(string texto, string nodo) => new([], [], [new ComprobacionCorrecta(texto, nodo)], []);
}

/// <summary>Resultado de una revisión con lo que ha tardado; con <see cref="Error"/> si ha fallado (sus hallazgos, vacíos).</summary>
public sealed record ResultadoRevision(string Clave, string Nombre, string Nodo, HallazgosSalud Hallazgos, TimeSpan Duracion, string? Error = null);

/// <summary>Sociedad, ejercicio de trabajo y fecha de la revisión.</summary>
/// <param name="Inversiones">Cuentas contables de los bancos marcados como inversión en «Personalizar…».</param>
public sealed record ContextoSalud(string IdSociedad, EjercicioSociedad Ejercicio, IReadOnlyList<EjercicioSociedad> Ejercicios, DateTime Hoy, IReadOnlySet<string> Inversiones)
{
    /// <summary>Fecha de corte: hoy si es el ejercicio en curso; si no, el 31/12 del ejercicio.</summary>
    public DateTime Corte => CalculoInicio.FechaCorte(Ejercicio.Ejercicio, Hoy);

    public EjercicioSociedad? Anterior => Ejercicios.FirstOrDefault(e => e.Ejercicio == Ejercicio.Ejercicio - 1);
}

/// <summary>Una revisión de salud: pieza pequeña, independiente y de solo lectura que declara el nodo dueño de su dato.</summary>
public interface IRevisionSalud
{
    string Clave { get; }
    string Nombre { get; }
    string Nodo { get; }
    Task<HallazgosSalud> RevisarAsync(IFuenteSalud fuente, ContextoSalud contexto, CancellationToken cancelacion);
}

/// <summary>El resultado de todas las revisiones, con la hora en que se hizo.</summary>
public sealed record InformeSalud(DateTime Hora, IReadOnlyList<ResultadoRevision> Revisiones)
{
    /// <summary>Variación de cada elemento respecto al lunes de la semana en curso, por su clave (solo los que tienen dato y cambian).</summary>
    public IReadOnlyDictionary<string, int> Tendencias { get; init; } = new Dictionary<string, int>();

    public IReadOnlyList<ElementoSalud> Elementos => Revisiones.SelectMany(r => r.Hallazgos.Elementos).ToList();
    public IReadOnlyList<HechaSalud> Hechas => Revisiones.SelectMany(r => r.Hallazgos.Hechas).ToList();
    public IReadOnlyList<ComprobacionCorrecta> Correctas => Revisiones.SelectMany(r => r.Hallazgos.Correctas).ToList();
    public IReadOnlyList<FilaPeriodos> Periodos => Revisiones.SelectMany(r => r.Hallazgos.Periodos).OrderBy(f => f.Orden).ToList();
    public TimeSpan Duracion => Revisiones.Count == 0 ? TimeSpan.Zero : Revisiones.Max(r => r.Duracion);
}
