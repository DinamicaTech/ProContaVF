using ProConta.Contabilidad.Asientos;
using ProConta.Nucleo.Contexto;

namespace ProConta.Contabilidad.Plantillas;

/// <summary>Usuario: se ofrece en el asiento manual y en el asistente del Mayor. Sistema: solo la ejecuta su proceso (p. ej. la nómina).</summary>
public enum TipoPlantilla { Usuario, Sistema }

/// <summary>Qué necesita la plantilla para generar el asiento.</summary>
public enum OrigenPlantilla
{
    /// <summary>Nada: solo sus datos de entrada (un asiento manual desde cero).</summary>
    Ninguno,
    /// <summary>Un apunte (y su asiento), p. ej. desde el Mayor.</summary>
    Apunte,
    /// <summary>Uno o varios registros de un proceso, con su clave (p. ej. «nominas»).</summary>
    Proceso,
}

/// <summary>Fecha de los asientos propuestos (siempre editable antes de ejecutar).</summary>
public enum ReglaFecha
{
    /// <summary>Primer día del trimestre activo del ejercicio (1 de enero si no hay trimestre activo), o la fecha del origen si es posterior.</summary>
    TrimestreActivo,
    /// <summary>La fecha del origen (la del apunte o la del registro del proceso).</summary>
    FechaOrigen,
}

/// <summary>Tipo de un dato de entrada.</summary>
public enum TipoDato { Importe, Cuenta, Texto, Fecha }

/// <summary>Dato que la plantilla pide al usuario (o al proceso) antes de proponer el asiento.</summary>
/// <param name="Nombre">Nombre con que lo usan las fórmulas (sin espacios).</param>
/// <param name="ValorPropuesto">Fórmula del valor que se propone (p. ej. <c>Importe</c> o <c>UltimaUsada()</c>).</param>
/// <param name="FiltroCuenta">Prefijo de las cuentas que admite y que ofrece el buscador (p. ej. 551).</param>
public sealed record DatoEntrada(string Nombre, string Etiqueta, TipoDato Tipo, string? ValorPropuesto = null, string? FiltroCuenta = null,
    string? Ayuda = null, bool Obligatorio = true, int Orden = 0);

/// <summary>
/// Línea de la plantilla: un apunte del asiento número <see cref="NumeroAsiento"/>, con fórmulas de cuenta, lado, importe, concepto,
/// centro y condición. Con <see cref="PorCadaApunte"/> se repite por cada apunte del asiento de origen (anular, rehacer), y sus
/// campos (Cuenta, Lado, Importe…) son los de ese apunte.
/// </summary>
/// <param name="Lado">D, H (o SIGNO: positivo al Haber), MISMO o CONTRARIO (al lado del apunte del que sale la línea), o una fórmula que dé uno de ellos.</param>
public sealed record LineaPlantilla(int NumeroAsiento, int Orden, string Cuenta, string Lado, string Importe, string? Concepto = null,
    string? Centro = null, string? Condicion = null, bool PorCadaApunte = false);

/// <summary>Plantilla de asiento (tablas PlantillasAsiento, PlantillasAsientoDatos y PlantillasAsientoLineas).</summary>
public sealed record PlantillaAsiento(
    int IdPlantilla,
    string Nombre,
    string Descripcion,
    TipoPlantilla Tipo,
    OrigenPlantilla Origen,
    IReadOnlyList<DatoEntrada> Datos,
    IReadOnlyList<LineaPlantilla> Lineas,
    string? IdSociedad = null,
    string? ClaveProceso = null,
    ReglaFecha ReglaFecha = ReglaFecha.TrimestreActivo,
    bool AgruparPorCuenta = false,
    bool Activa = true,
    string? MotivoPropuesto = null,
    string? Ejemplo = null,
    int Orden = 0)
{
    /// <summary>Reproduce el asiento de origen (anular, rehacer): no vale para asientos automáticos ni de facturas.</summary>
    public bool ReproduceElAsiento => Lineas.Any(l => l.PorCadaApunte);

    public override string ToString() => $"Plantilla «{Nombre}»";
}

/// <summary>Origen con que se ejecuta una plantilla.</summary>
public abstract record OrigenEjecucion
{
    public static readonly OrigenEjecucion Ninguno = new SinOrigen();
}

/// <summary>Sin origen: solo los datos de entrada.</summary>
public sealed record SinOrigen : OrigenEjecucion;

/// <summary>
/// Un apunte y su asiento completo. <see cref="TieneLibroIva"/>: el asiento tiene registro en el libro de IVA soportado o repercutido
/// (es de una factura).
/// </summary>
public sealed record OrigenApunte(Asiento Asiento, int OrdenApunte, bool TieneLibroIva = false) : OrigenEjecucion
{
    public Apunte Apunte => Asiento.Apuntes.FirstOrDefault(a => a.Orden == OrdenApunte)
        ?? throw new InvalidOperationException($"El asiento no tiene el apunte {OrdenApunte}.");
}

/// <summary>Uno o varios registros de un proceso (p. ej. las nóminas de un mes), cada uno con sus campos.</summary>
public sealed record OrigenProceso(string ClaveProceso, IReadOnlyList<RegistroProceso> Registros) : OrigenEjecucion;

/// <summary>Registro de un proceso: su fecha, su clave (p. ej. el trabajador) y sus campos (Bruto, Neto, IRPF111…).</summary>
public sealed record RegistroProceso(string Clave, DateTime Fecha, IReadOnlyDictionary<string, object?> Campos);

/// <summary>Cuentas y periodo de la sociedad que usan las plantillas, leídos de la BD (o del ejemplo en la vista previa).</summary>
/// <param name="Cuentas">Códigos de cuenta del ejercicio con su nombre, para comprobarlas y nombrar las subcuentas que hay que crear; null = no se comprueban.</param>
/// <param name="UltimasCuentas">Para <c>UltimaUsada()</c>: por nombre de dato, la última cuenta usada con la plantilla.</param>
public sealed record EntornoSociedad(
    ContextoSociedadEjercicio Contexto,
    string? CuentaCuadres = null,
    string? CuentaTesoreria = null,
    int? TrimestreActivo = null,
    IReadOnlyDictionary<string, string>? Cuentas = null,
    IReadOnlyDictionary<string, string>? UltimasCuentas = null);

/// <summary>Apunte propuesto, con la cuenta que hay que crear si no existe (cuentas con «?»).</summary>
public sealed record ApuntePropuesto(string Cuenta, bool AlDebe, decimal Importe, string? Concepto, string? Documento, int? IdCentroCoste,
    string? NombreCuentaNueva = null)
{
    public decimal Debe => AlDebe ? Importe : 0m;
    public decimal Haber => AlDebe ? 0m : Importe;
}

/// <summary>Asiento propuesto (número de asiento dentro de la plantilla, fecha, descripción y apuntes).</summary>
public sealed record AsientoPropuesto(int NumeroAsiento, DateTime Fecha, string Descripcion, IReadOnlyList<ApuntePropuesto> Apuntes)
{
    public decimal TotalDebe => Apuntes.Sum(a => a.Debe);
    public decimal TotalHaber => Apuntes.Sum(a => a.Haber);
    public bool Cuadrado => TotalDebe == TotalHaber;
}

/// <summary>
/// Resultado de «Proponer»: los asientos que generaría la plantilla, sin grabar nada. Se puede editar (en la rejilla del asiento
/// manual) y pasar a «Ejecutar». Lleva el origen para grabar la relación y el punteo.
/// </summary>
public sealed record PropuestaPlantilla(
    PlantillaAsiento Plantilla,
    ContextoSociedadEjercicio Contexto,
    IReadOnlyList<AsientoPropuesto> Asientos,
    IReadOnlyDictionary<string, string?> Datos,
    int? IdAsientoOrigen = null,
    int? IdApunteOrigen = null)
{
    /// <summary>Subcuentas que no existen y que se crearán al ejecutar (código y nombre).</summary>
    public IReadOnlyList<(string Codigo, string Nombre)> CuentasNuevas =>
        Asientos.SelectMany(a => a.Apuntes).Where(a => a.NombreCuentaNueva is not null)
            .GroupBy(a => a.Cuenta).Select(g => (g.Key, g.First().NombreCuentaNueva!)).ToList();
}
