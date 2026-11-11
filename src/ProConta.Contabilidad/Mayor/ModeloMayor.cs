namespace ProConta.Contabilidad.Mayor;

/// <summary>Ejercicio de la sociedad que el Mayor puede mostrar (SociedadesEjercicios).</summary>
/// <param name="Cerrado">EjercicioCerrado: el Mayor lo muestra en solo lectura.</param>
/// <param name="TieneAsientos">Tiene algún asiento; los ejercicios vacíos no se ofrecen (salvo el de trabajo).</param>
public sealed record EjercicioMayor(int IdSociedadEjercicio, int Ejercicio, bool Cerrado, bool TieneAsientos = true);

/// <summary>Vista de los movimientos (contabilidad/mayor 2026-10-31 08:43).</summary>
public enum VistaMayor
{
    /// <summary>Un apunte por fila.</summary>
    Normal,
    /// <summary>Una fila por cuenta y factura (sin factura, por asiento): la factura y sus cobros juntos.</summary>
    Compacto,
    /// <summary>Como Compacto, pero sin las facturas que netean a cero (±0,05 €).</summary>
    SoloPendientes,
    /// <summary>Una fila por cuenta, fecha, factura (o asiento) y lado: las líneas de una misma factura y cuenta juntas.</summary>
    Agrupar700
}

/// <summary>Una línea de un asiento: lo que hace falta para decidir la contrapartida de los apuntes de la vista.</summary>
public sealed record LineaContrapartida(int IdAsiento, int Orden, string Cuenta, decimal Debe, decimal Haber)
{
    public decimal Importe => Debe - Haber;
}

/// <summary>
/// Apunte tal como lo lee el Mayor: el apunte, la cabecera de su asiento (fecha contable, tipo, notas, alta y último cambio)
/// y la fecha de la factura del libro de IVA, si el asiento tiene registro de IVA.
/// </summary>
public sealed record ApunteMayor(
    int IdApunte, int IdAsiento, int Orden, int IdSociedadEjercicio, int Ejercicio, DateTime Fecha, byte TipoAsiento,
    string Cuenta, decimal Debe, decimal Haber, string Concepto, string? Documento, string? Notas,
    DateTime? FechaDocumento, DateTime? GrabadoEl, DateTime? ModificadoEl)
{
    public decimal Importe => Debe - Haber;
    public bool EsApertura => TipoAsiento == TiposAsientoMayor.Apertura;
}

/// <summary>Tipos de asiento que trata el Mayor (columna TipoAsiento, valores del motor anterior).</summary>
public static class TiposAsientoMayor
{
    public const byte Apertura = 1;
    public const byte Cierre = 3;
}

/// <summary>
/// Lo que se pide a la fuente: los apuntes de unas cuentas (código exacto o código corto: todas las subcuentas que empiezan por
/// él) o de unos números de factura (en todas las cuentas), en unos ejercicios. Los asientos de cierre nunca se leen.
/// </summary>
/// <param name="Cuentas">Códigos de la vista; vacío si se buscan facturas en todas las cuentas.</param>
/// <param name="Exactas">Los códigos que son subcuentas exactas (el resto se tratan como prefijo).</param>
/// <param name="Facturas">Números de factura normalizados (<see cref="Facturas.Normalizar"/>) que se buscan en todas las cuentas.</param>
public sealed record SolicitudMayor(
    IReadOnlyList<string> Cuentas, IReadOnlySet<string> Exactas, IReadOnlyList<EjercicioMayor> Ejercicios, IReadOnlyList<string>? Facturas = null)
{
    public bool BuscaFacturas => Facturas is { Count: > 0 };

    /// <summary>¿El apunte de esta cuenta entra en la vista?</summary>
    public bool Incluye(string cuenta) =>
        Cuentas.Any(c => Exactas.Contains(c) ? string.Equals(c, cuenta, StringComparison.Ordinal) : cuenta.StartsWith(c, StringComparison.Ordinal));
}

/// <summary>Lo que devuelve la fuente para una solicitud: apuntes de la vista, todas las líneas de sus asientos y nombres de cuentas.</summary>
public sealed record DatosMayor(
    IReadOnlyList<ApunteMayor> Apuntes, IReadOnlyList<LineaContrapartida> Lineas, IReadOnlyDictionary<string, string> Nombres)
{
    public static readonly DatosMayor Vacio = new([], [], new Dictionary<string, string>());
}

/// <summary>Cuenta del selector con su saldo y apuntes en los ejercicios elegidos (no solo en el activo).</summary>
/// <param name="SaldoSinApertura">Saldo de los movimientos del periodo, sin la apertura.</param>
/// <param name="Saldo">Saldo con la apertura del primer ejercicio elegido.</param>
/// <param name="Apuntes">Apuntes del periodo (con la apertura del primer ejercicio, sin cierres).</param>
/// <param name="EsSubcuenta">Admite apuntes (imputable); las cuentas de grupo muestran todas sus subcuentas.</param>
public sealed record CuentaMayor(string Codigo, string Nombre, decimal SaldoSinApertura, decimal Saldo, int Apuntes, bool EsSubcuenta)
{
    public string Etiqueta => Nombre.Length > 0 ? $"{Codigo} {Nombre}" : Codigo;
}

/// <summary>Sumas de una cuenta en un ejercicio, por tipo de asiento (lo que lee la fuente para el selector).</summary>
public sealed record SumaCuenta(string Cuenta, int IdSociedadEjercicio, byte TipoAsiento, decimal Debe, decimal Haber, int Apuntes);

/// <summary>Cuenta del plan de un ejercicio (CuentasContables).</summary>
public sealed record PlanCuenta(string Codigo, string Nombre, bool EsImputable, int IdSociedadEjercicio);

/// <summary>Asiento completo para verlo en solo lectura desde el Mayor.</summary>
public sealed record AsientoMayor(
    int IdAsiento, int Ejercicio, DateTime Fecha, string Descripcion, byte TipoAsiento, string? Notas, bool EsAutomatico, string? Origen,
    DateTime? GrabadoEl, string? GrabadoPor, DateTime? ModificadoEl, string? ModificadoPor, IReadOnlyList<LineaAsientoMayor> Lineas)
{
    public decimal TotalDebe => Lineas.Sum(l => l.Debe);
    public decimal TotalHaber => Lineas.Sum(l => l.Haber);

    public string TextoTipo => TipoAsiento switch
    {
        1 => "Apertura",
        2 => "Regularización",
        3 => "Cierre",
        4 => "Rectificación",
        _ => EsAutomatico ? "Automático" : "Manual"
    };
}

/// <summary>Línea de un asiento para la ventana de solo lectura.</summary>
public sealed record LineaAsientoMayor(int Orden, string Cuenta, string NombreCuenta, string Concepto, string? Documento, decimal Debe, decimal Haber);

/// <summary>Cambio de un apunte del registro de cambios (RegistroCambiosApuntes), para el historial del apunte.</summary>
public sealed record CambioApunteMayor(DateTime FechaHora, string Operador, string Motivo, string Operacion, string Campo, string? Antes, string? Despues, string Lote);
