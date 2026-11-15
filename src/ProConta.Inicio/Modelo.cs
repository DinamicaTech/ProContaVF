namespace ProConta.Inicio;

// ---------------------------------------------------------------- Lo que se lee de la BD

/// <summary>Ejercicio de la sociedad (SociedadesEjercicios) con lo que necesita Inicio: trimestre activo y prorrata del año anterior.</summary>
public sealed record EjercicioSociedad(int IdSociedadEjercicio, int Ejercicio, int? TrimestreActivo = null, decimal? Prorrata = null);

/// <summary>Suma de los apuntes contabilizados de una cuenta en un día y tipo de asiento (sin los cierres).</summary>
public sealed record MovimientoDia(DateTime Fecha, string Cuenta, byte TipoAsiento, decimal Debe, decimal Haber)
{
    public decimal Saldo => Debe - Haber;
}

/// <summary>Apunte contabilizado de un cliente (430–436) con su vencimiento, para la antigüedad de lo pendiente de cobro.</summary>
public sealed record ApunteCartera(string Cuenta, DateTime Fecha, DateTime? Vencimiento, decimal Debe, decimal Haber);

/// <summary>Base imponible de una línea de venta con su centro de coste (de una factura validada o, sin facturas, del grupo 70).</summary>
public sealed record LineaVenta(DateTime Fecha, decimal Base, int? IdCentroCoste);

/// <summary>Apunte de IVA (472 o 477) contabilizado, fuera de los asientos de liquidación (los que mueven la 4700 o la 4750).</summary>
public sealed record ApunteIva(DateTime Fecha, string Cuenta, decimal Debe, decimal Haber);

/// <summary>Cuenta bancaria de Norma 43 con su cuenta contable y la fecha final de su último extracto.</summary>
public sealed record BancoN43(string Nombre, string? CuentaContable, DateTime? UltimoExtracto);

/// <summary>Actividad de IVA tal como la usa Inicio: nombre, deducibilidad (0 no, 1 prorrata, 2 total) y serie de 472.</summary>
public sealed record ActividadInicio(string Nombre, byte Deducible, string? Serie);

/// <summary>Todo lo que Inicio lee de un ejercicio.</summary>
/// <param name="VentasDeContabilidad">El ejercicio no tiene facturas de venta validadas y las ventas salen del grupo 70.</param>
public sealed record DatosEjercicio(
    EjercicioSociedad Ejercicio, IReadOnlyList<MovimientoDia> Movimientos, IReadOnlyList<LineaVenta> Ventas, bool VentasDeContabilidad,
    IReadOnlyList<ApunteCartera> Cartera, IReadOnlyList<ApunteIva> Iva, IReadOnlyDictionary<string, string> Nombres)
{
    public bool HayMovimientos => Movimientos.Count > 0 || Ventas.Count > 0;

    public static DatosEjercicio Vacio(EjercicioSociedad e) => new(e, [], [], false, [], [], new Dictionary<string, string>());
}

/// <summary>Lecturas de Inicio. Solo lee: ni escribe en la BD ni cambia el esquema.</summary>
public interface IFuenteInicio
{
    Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken cancelacion = default);
    Task<DatosEjercicio> DatosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default);
    Task<IReadOnlyList<BancoN43>> BancosAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken cancelacion = default);

    /// <summary>Actividad de IVA de cada centro de coste (heredada del nodo superior si no tiene propia).</summary>
    Task<IReadOnlyDictionary<int, ActividadInicio>> ActividadesPorCentroAsync(string idSociedad, CancellationToken cancelacion = default);

    /// <summary>Actividades de la sociedad (para la deducibilidad de las series de 472).</summary>
    Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken cancelacion = default);
}

// ---------------------------------------------------------------- Lo que se muestra

/// <summary>
/// Un indicador de la cabecera: valor a la fecha de corte, el del ejercicio comparado a la misma fecha, la proyección de
/// cierre (solo facturación y resultado) con el total del comparado completo, y la evolución para el minigráfico.
/// </summary>
public sealed record Indicador(decimal Valor, decimal? Comparado, IReadOnlyList<decimal?> Evolucion, decimal? Proyeccion = null, decimal? TotalComparado = null)
{
    /// <summary>Variación porcentual sobre el comparado; null sin comparado o si es cero.</summary>
    public decimal? Variacion => Comparado is decimal c && c != 0 ? (Valor - c) / Math.Abs(c) * 100m : null;
}

/// <summary>Facturación por actividad: acumulado a la fecha de corte y el del comparado a la misma fecha.</summary>
public sealed record FacturacionActividad(string Actividad, decimal Actual, decimal? Comparado);

/// <summary>Gasto por naturaleza (cuenta de 3 dígitos, o «Resto»): acumulado y comparado a la misma fecha.</summary>
public sealed record GastoNaturaleza(string Codigo, string Nombre, decimal Actual, decimal? Comparado)
{
    public string Texto => Codigo.Length == 0 ? Nombre : string.IsNullOrWhiteSpace(Nombre) ? Codigo : $"{Codigo} · {Nombre}";
}

/// <summary>Tramo de antigüedad de lo pendiente de cobro.</summary>
public sealed record TramoAntiguedad(string Etiqueta, decimal Importe);

/// <summary>Cliente con saldo pendiente: subcuenta, nombre, saldo y lo que tiene más de 30 días.</summary>
public sealed record ClientePendiente(string Cuenta, string Nombre, decimal Saldo, decimal MasDe30Dias);

/// <summary>Saldo de tesorería a fin de un mes (o a la fecha de corte, el último). Null si no hay datos de ese ejercicio.</summary>
public sealed record PuntoTesoreria(DateTime Fecha, string Etiqueta, decimal? Saldo, bool EsCorte);

/// <summary>Saldo de un banco a la fecha de corte con la fecha de su último extracto N43.</summary>
public sealed record SaldoBanco(string Nombre, string? Cuenta, decimal Saldo, DateTime? UltimoExtracto);

/// <summary>IVA estimado del trimestre activo: repercutido (477) menos soportado deducible (472) hasta la fecha de corte.</summary>
public sealed record IvaTrimestre(int Trimestre, DateTime Desde, DateTime Hasta, decimal Repercutido, decimal Soportado, decimal? Comparado)
{
    public decimal Neto => Repercutido - Soportado;
}

/// <summary>Todo lo que pinta la pantalla de inicio para un ejercicio, un ejercicio comparado (o ninguno) y una fecha de corte.</summary>
public sealed record PanelInicio
{
    public required int Ejercicio { get; init; }
    public int? Comparado { get; init; }
    public required DateTime FechaCorte { get; init; }
    public DateTime? FechaComparada { get; init; }
    /// <summary>La fecha de corte es el 31/12: el ejercicio está terminado y no hay proyección.</summary>
    public required bool Terminado { get; init; }
    public required bool HayMovimientos { get; init; }

    public required Indicador Facturacion { get; init; }
    public required Indicador Resultado { get; init; }
    public required Indicador Tesoreria { get; init; }
    public required Indicador PendienteCobro { get; init; }
    public required Indicador PendientePago { get; init; }
    public required IvaTrimestre Iva { get; init; }
    public required IReadOnlyList<decimal?> EvolucionIva { get; init; }

    /// <summary>Facturación de cada mes del ejercicio (null en los meses posteriores a la fecha de corte).</summary>
    public required IReadOnlyList<decimal?> FacturacionMensual { get; init; }
    /// <summary>Lo que falta de ejercicio según la proyección, repartido por meses (null donde no hay).</summary>
    public required IReadOnlyList<decimal?> ProyeccionMensual { get; init; }
    /// <summary>Facturación de cada mes del ejercicio comparado completo; null sin comparado.</summary>
    public IReadOnlyList<decimal>? FacturacionComparadaMensual { get; init; }
    public required bool FacturacionDeContabilidad { get; init; }
    public bool ComparadoDeContabilidad { get; init; }
    public required IReadOnlyList<FacturacionActividad> Actividades { get; init; }

    public required IReadOnlyList<decimal?> IngresosMensuales { get; init; }
    public required IReadOnlyList<decimal?> GastosMensuales { get; init; }
    public required IReadOnlyList<GastoNaturaleza> Gastos { get; init; }

    public required IReadOnlyList<TramoAntiguedad> Antiguedad { get; init; }
    public required IReadOnlyList<ClientePendiente> Clientes { get; init; }
    /// <summary>Algún apunte de clientes tiene vencimiento (si no, la antigüedad es por la fecha del apunte).</summary>
    public required bool HayVencimientos { get; init; }

    public required IReadOnlyList<PuntoTesoreria> TesoreriaMensual { get; init; }
    public required IReadOnlyList<SaldoBanco> Bancos { get; init; }
    /// <summary>Saldo de las 572 que no son de ninguna cuenta bancaria de Norma 43.</summary>
    public required decimal OtrasCuentasTesoreria { get; init; }
}
