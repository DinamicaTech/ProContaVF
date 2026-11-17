namespace ProConta.Inicio.Salud;

/// <summary>Ventas por facturar pendientes (sin factura) o con la factura aún en borrador, con su importe con IVA.</summary>
public sealed record VentasPendientes(int Pendientes, decimal ImportePendiente, int EnBorrador, decimal ImporteEnBorrador)
{
    public int Total => Pendientes + EnBorrador;
    public decimal Importe => ImportePendiente + ImporteEnBorrador;
}

/// <summary>Asiento de liquidación de IVA (con apuntes en la 4700 o la 4750) contabilizado.</summary>
public sealed record LiquidacionIva(int Ejercicio, DateTime Fecha);

/// <summary>
/// Cuenta bancaria de Norma 43 para la salud: su cuenta contable, el último extracto (fecha y saldo final), el saldo contable de
/// su cuenta a esa fecha y a la fecha de corte, si tiene apuntes en el ejercicio de trabajo y si el último extracto cuadra por
/// dentro (EstadoCuadre; los ficheros de remesas, por ejemplo, salen descuadrados y su saldo final no es un saldo de cuenta).
/// <paramref name="UltimoMovimiento"/>: la fecha más reciente entre sus apuntes contabilizados en el Mayor (cualquier ejercicio, sin
/// apertura, regularización ni cierre) y los
/// movimientos de sus extractos N43 (de lotes no revertidos); null si no tiene ninguno.
/// </summary>
public sealed record BancoSalud(
    string Nombre, string? Cuenta, bool Activa, DateTime? UltimoExtracto, decimal? SaldoExtracto, decimal? SaldoContableExtracto, decimal SaldoContableCorte,
    bool ApuntesEnEjercicio, bool ExtractoCuadrado = true, DateTime? UltimoMovimiento = null);

/// <summary>Líneas bancarias de una cuenta en un mes: todas y las que no tienen asiento, con la fecha de la más antigua sin asiento.</summary>
public sealed record MesBanco(string Banco, string? Cuenta, int Anio, int Mes, int Lineas, int SinAsiento, DateTime? MasAntiguaSinAsiento);

/// <summary>Facturas de compra del ejercicio sin contabilizar, con su total y la fecha de la más antigua.</summary>
public sealed record ComprasPendientes(int Numero, decimal Total, DateTime? MasAntigua);

/// <summary>Asiento del ejercicio (contabilizado o en borrador) cuyo debe no es igual al haber.</summary>
public sealed record AsientoDescuadrado(int IdAsiento, DateTime Fecha, string? Descripcion, decimal Debe, decimal Haber)
{
    public decimal Diferencia => Debe - Haber;
}

/// <summary>Factura validada (o rectificada) con su serie, para buscar huecos en la numeración.</summary>
public sealed record NumeroFactura(int IdSerie, string Serie, string Numero);

/// <summary>
/// Totales de un trimestre: cuotas de los libros de IVA y saldo de las cuentas 477 y 472 (sin los asientos de liquidación). En la
/// 477 no entra el IVA autorrepercutido (ver <see cref="CuadreLibrosIva"/>).
/// </summary>
public sealed record IvaLibrosTrimestre(int Trimestre, decimal LibroRepercutido, decimal Cuentas477, decimal LibroSoportado, decimal Cuentas472, int ApuntesLibros);

/// <summary>Apunte de una 472 o 477 para el cuadre con los libros; <paramref name="DeFacturaRecibida"/>: su asiento tiene registro en el libro de soportado.</summary>
public sealed record ApunteLibroIva(DateTime Fecha, string Cuenta, decimal Debe, decimal Haber, bool DeFacturaRecibida);

/// <summary>
/// Cuadre trimestral de los libros de IVA con las cuentas (decisión 2026-11-15 15:18 de pantalla-de-inicio). Repercutido: cuotas
/// del libro de repercutido frente a las 477 sin el IVA autorrepercutido, que son los apuntes de la 477 en asientos con registro
/// en el libro de soportado (una factura recibida no lleva IVA repercutido; la marca de inversión del sujeto pasivo y la cuota
/// autorrepercutida del libro no sirven: las facturas importadas de ContaSol no las traen). Soportado: cuotas del libro de
/// soportado frente a las 472, todas (la parte deducible del autorrepercutido está en el libro y en la 472).
/// </summary>
public static class CuadreLibrosIva
{
    public static IReadOnlyList<IvaLibrosTrimestre> Trimestres(IReadOnlyList<(DateTime Fecha, decimal Cuota)> repercutido, IReadOnlyList<(DateTime Fecha, decimal Cuota)> soportado,
        IReadOnlyList<ApunteLibroIva> apuntes) =>
        Enumerable.Range(1, 4).Select(q =>
        {
            bool En(DateTime d) => CalculoInicio.TrimestreDe(d) == q;
            return new IvaLibrosTrimestre(q,
                repercutido.Where(x => En(x.Fecha)).Sum(x => x.Cuota),
                apuntes.Where(x => En(x.Fecha) && !x.DeFacturaRecibida && x.Cuenta.StartsWith("477", StringComparison.Ordinal)).Sum(x => x.Haber - x.Debe),
                soportado.Where(x => En(x.Fecha)).Sum(x => x.Cuota),
                apuntes.Where(x => En(x.Fecha) && x.Cuenta.StartsWith("472", StringComparison.Ordinal)).Sum(x => x.Debe - x.Haber),
                repercutido.Count(x => En(x.Fecha)) + soportado.Count(x => En(x.Fecha)));
        }).ToList();
}

/// <summary>Cliente o proveedor con apuntes en el ejercicio y sin NIF en su ficha.</summary>
public sealed record TerceroSinNif(int IdTercero, string Nombre, string Cuenta, bool EsCliente);

/// <summary>Subcuenta con apuntes en el ejercicio que no existe en su plan de cuentas o está dada de baja (inactiva).</summary>
public sealed record CuentaConProblema(string Cuenta, int Apuntes, bool Existe);

/// <summary>Importación de nóminas del ejercicio (sin las reemplazadas ni deshechas): mes, estado y tipo de paga.</summary>
public sealed record NominaMes(int Anio, int Mes, string Estado, string? TipoPaga);

/// <summary>Asiento de amortización contabilizado: carga una 68 y abona una 28 (ni apertura, ni regularización, ni cierre).</summary>
public sealed record AsientoAmortizacion(int Ejercicio, DateTime Fecha, decimal Importe);

/// <summary>
/// Apunte contabilizado (sin los de cierre) de una subcuenta para las revisiones de las incidencias con la gestoría (S1 a S4 y
/// S9): asiento, tipo (1 apertura, 2 regularización), fecha, cuenta e importes.
/// </summary>
public sealed record ApunteCuenta(int Ejercicio, int IdAsiento, DateTime Fecha, byte TipoAsiento, string Cuenta, decimal Debe, decimal Haber)
{
    public decimal Saldo => Debe - Haber;
}

/// <summary>Cuenta de gasto o ingreso con apuntes sin centro de coste en el ejercicio (S7): número de apuntes e importe (debe − haber).</summary>
public sealed record CuentaSinCentro(string Cuenta, int Apuntes, decimal Importe);

/// <summary>Cliente o proveedor con apuntes en el ejercicio y su NIF tal como está en la ficha (S6).</summary>
/// <param name="TipoIdentificacion">Terceros.TipoIdentificacion (vacío, NIF, CIF, NIE o DNI: identificación española).</param>
/// <param name="Pais">País del domicilio de la ficha (Terceros.Pais), si lo tiene.</param>
public sealed record TerceroNif(int IdTercero, string Nombre, string Cuenta, bool EsCliente, string Nif, string? TipoIdentificacion = null, string? Pais = null);

/// <summary>
/// Lecturas de la salud de la contabilidad. Solo lee: ni escribe en la BD ni cambia el esquema (el histórico de la tendencia va
/// aparte, en <see cref="IHistoricoSalud"/>). Las lecturas de la fase 4 tienen una implementación vacía por defecto.
/// </summary>
public interface IFuenteSalud
{
    Task<IReadOnlyList<EjercicioSociedad>> EjerciciosAsync(string idSociedad, CancellationToken c = default);
    Task<VentasPendientes> VentasPendientesAsync(string idSociedad, DateTime antesDe, CancellationToken c = default);
    Task<IReadOnlyList<ApunteIva>> IvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyList<ActividadInicio>> ActividadesAsync(string idSociedad, CancellationToken c = default);
    Task<IReadOnlyList<LiquidacionIva>> LiquidacionesIvaAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default);
    Task<IReadOnlyList<BancoSalud>> BancosAsync(string idSociedad, ContextoSalud contexto, CancellationToken c = default);
    Task<IReadOnlyList<MesBanco>> LineasBancoAsync(string idSociedad, CancellationToken c = default);
    Task<ComprasPendientes> ComprasPendientesAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyList<DateTime>> BorradoresVentaAsync(EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyDictionary<string, decimal>> SaldosAsync(EjercicioSociedad ejercicio, string prefijoDesde, string prefijoHasta, DateTime hasta, CancellationToken c = default);
    Task<(int Asientos, IReadOnlyList<AsientoDescuadrado> Descuadrados)> CuadreDiarioAsync(EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyList<NumeroFactura>> NumerosFacturasAsync(EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyList<IvaLibrosTrimestre>> LibrosIvaAsync(EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyList<TerceroSinNif>> TercerosSinNifAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyList<CuentaConProblema>> CuentasConProblemaAsync(EjercicioSociedad ejercicio, CancellationToken c = default);
    Task<IReadOnlyList<NominaMes>> NominasAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default);

    /// <summary>Asientos de amortización (68 contra 28) de los ejercicios pedidos.</summary>
    Task<IReadOnlyList<AsientoAmortizacion>> AmortizacionesAsync(IReadOnlyList<EjercicioSociedad> ejercicios, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<AsientoAmortizacion>>([]);

    /// <summary>
    /// Periodicidad de la amortización configurada en el ejercicio (Parámetros › Ejercicios, contabilidad/procesos/amortizacion-de-activos):
    /// ANUAL, TRIMESTRAL o MENSUAL; null si no está configurada.
    /// </summary>
    Task<string?> PeriodicidadAmortizacionAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult<string?>(null);

    /// <summary>
    /// Umbral de los céntimos residuales del ejercicio (Parámetros › Ejercicios, contabilidad/procesos/centimos-residuales); null si no
    /// está configurado (se usa 1,00 €).
    /// </summary>
    Task<decimal?> UmbralCentimosResidualesAsync(EjercicioSociedad ejercicio, CancellationToken c = default) => Task.FromResult<decimal?>(null);

    /// <summary>Activos de la sociedad amortizables, propios (no ajenos) y sin dar de baja.</summary>
    Task<int> ActivosAmortizablesAsync(string idSociedad, CancellationToken c = default) => Task.FromResult(0);

    // ---- Incidencias con la gestoría (contabilidad 2026-11-15 21:10): implementación vacía por defecto.

    /// <summary>Apuntes contabilizados, sin los de cierre, de las subcuentas que empiezan por alguno de los prefijos, en los ejercicios pedidos.</summary>
    Task<IReadOnlyList<ApunteCuenta>> ApuntesCuentasAsync(IReadOnlyList<EjercicioSociedad> ejercicios, IReadOnlyList<string> prefijos, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<ApunteCuenta>>([]);

    /// <summary>Subcuentas marcadas «Cuadrada» en el Mayor en el ejercicio, con el saldo que tenían al marcarlas (null si no se guardó).</summary>
    Task<IReadOnlyDictionary<string, decimal?>> CuadradasAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyDictionary<string, decimal?>>(new Dictionary<string, decimal?>());

    /// <summary>Cuentas de los grupos 6 y 7 con apuntes contabilizados sin centro de coste (sin regularización ni cierre).</summary>
    Task<IReadOnlyList<CuentaSinCentro>> CuentasSinCentroAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<CuentaSinCentro>>([]);

    /// <summary>Clientes y proveedores con apuntes en el ejercicio que tienen NIF en su ficha.</summary>
    Task<IReadOnlyList<TerceroNif>> TercerosConNifAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<TerceroNif>>([]);

    /// <summary>Apuntes contabilizados de los clientes (430 a 436) del ejercicio, con su vencimiento.</summary>
    Task<IReadOnlyList<ApunteCartera>> CarteraAsync(EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<ApunteCartera>>([]);

    /// <summary>Facturas de compra del ejercicio, como las lista Compras › Facturas recibidas.</summary>
    Task<IReadOnlyList<ProConta.Compras.Facturas.FilaFacturaCompra>> FacturasCompraAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<ProConta.Compras.Facturas.FilaFacturaCompra>>([]);

    /// <summary>Facturas de comisión de las plataformas del ejercicio exportadas y sin compensar (facturacion/reservas/compensacion-comision).</summary>
    Task<IReadOnlyList<ProConta.Compras.Facturas.ComisionPendiente>> ComisionesSinCompensarAsync(string idSociedad, EjercicioSociedad ejercicio, CancellationToken c = default) =>
        Task.FromResult<IReadOnlyList<ProConta.Compras.Facturas.ComisionPendiente>>([]);
}
