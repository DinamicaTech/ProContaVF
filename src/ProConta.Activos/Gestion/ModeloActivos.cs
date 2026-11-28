using ProConta.Activos.Subcuentas;
using ProConta.Contabilidad.Amortizacion;

namespace ProConta.Activos.Gestion;

/// <summary>Tipo de activo (TiposActivo) con su clase del listado.</summary>
public sealed record TipoActivo(int IdTipoActivo, string Codigo, string Nombre, bool AmortizablePredeterminado)
{
    public bool EsInmueble => TiposActivoPgc.EsInmueble(Codigo);
    public bool EsInversion => TiposActivoPgc.EsInversion(Codigo);
    public string Clase => ReglasFichaActivo.Clase(Codigo);

    /// <summary>% anual habitual del tipo, en tanto por uno, para proponerlo en el alta (tabla de coeficientes simplificada).</summary>
    public decimal PorcentajeHabitual => Codigo switch
    {
        TiposActivoPgc.Vehiculo => 0.16m,
        TiposActivoPgc.Instalacion => 0.10m,
        TiposActivoPgc.Aplicacion => 0.33m,
        _ when EsInversion => 0m,
        _ => 0.03m,
    };
}

/// <summary>Centro de coste de la sociedad para elegirlo en la ficha o como padre de uno nuevo.</summary>
/// <summary>Cuántos activos propios hay de un tipo y el centro del último dado de alta.</summary>
public sealed record UsoTipoActivo(int IdTipoActivo, int Activos, int? IdCentroUltimo);

public sealed record CentroParaActivo(int IdCentroCoste, string Codigo, string Nombre, int? IdPadre, int Nivel, bool EsImputable, bool EstaActivo)
{
    public string Texto => $"{Codigo} {Nombre}".Trim();
}

/// <summary>Ejercicio de la sociedad.</summary>
public sealed record EjercicioSociedad(int IdSociedadEjercicio, int Ejercicio);

/// <summary>Una fila del listado de activos con las cifras del ejercicio de trabajo.</summary>
/// <param name="Amortizable">Base − valor residual de su plan (0 sin plan).</param>
/// <param name="Amortizado">Saldo acreedor de su amortización acumulada en el ejercicio de trabajo.</param>
/// <param name="Gastos">Gastos del ejercicio de trabajo, con la amortización prevista.</param>
/// <param name="SubcuentasQueFaltan">Líneas activas de su plantilla sin subcuenta en el ejercicio de trabajo.</param>
/// <param name="UltimoMesConIngresos">Último mes (1 a 12) del ejercicio de trabajo con ingresos del activo; null sin ingresos.</param>
public sealed record ActivoListado(int IdActivo, string Codigo, string Nombre, string? NombreCorto, int IdTipoActivo, string CodigoTipo, string Tipo, int? IdCentroCoste,
    string Centro, DateTime? FechaCompra, decimal Coste, decimal Amortizable, decimal Amortizado, decimal Ingresos, decimal Gastos, bool EsAjeno, DateTime? FechaBaja,
    int SubcuentasQueFaltan, int? UltimoMesConIngresos = null)
{
    public decimal Resultado => Ingresos - Gastos;
    public bool DeBaja => FechaBaja is not null;
    public string Clase => ReglasFichaActivo.Clase(CodigoTipo);

    /// <summary>Lo amortizado sobre lo amortizable, en tanto por uno; null sin plan.</summary>
    public decimal? PorcentajeAmortizado => Amortizable > 0m ? Math.Min(1m, Math.Max(0m, Amortizado / Amortizable)) : null;

    /// <summary>En uso, Inactivo (con fecha de baja; activos 2026-11-24 09:27), Ajeno o Falta alguna subcuenta.</summary>
    public string Estado => EsAjeno ? "Ajeno" : DeBaja ? "Inactivo" : SubcuentasQueFaltan > 0 ? "Falta alguna subcuenta" : "En uso";
}

/// <summary>La ficha del activo: sus datos editables y lo que se enseña de solo lectura.</summary>
/// <param name="MotivoBloqueoTipo">Por qué no se pueden cambiar el tipo ni la marca de ajeno; null si se puede.</param>
public sealed record FichaActivo(DatosActivo Datos, int Version, string Tipo, string? Centro, string? Actividad, DateTime? FechaBaja, string? MotivoBloqueoTipo,
    decimal Amortizable, decimal Amortizado, IReadOnlyList<SubcuentaDelActivo> Subcuentas)
{
    public bool DeBaja => FechaBaja is not null;
    public string Estado => Datos.EsAjeno ? "Ajeno" : DeBaja ? "Inactivo" : "En uso";
}

/// <summary>Una subcuenta del activo en el apartado Subcuentas: código en el ejercicio de trabajo, nombre, papel (texto del rol) y saldo.</summary>
public sealed record SubcuentaFicha(int IdSubcuentaActivo, string? Codigo, string Nombre, string Papel, decimal Saldo, bool EnEjercicio);

/// <summary>Las subcuentas del activo y las líneas de su plantilla que le faltan (la propuesta de «Generar las que falten…»).</summary>
public sealed record SubcuentasDeActivo(IReadOnlyList<SubcuentaFicha> Subcuentas, IReadOnlyList<OperacionSubcuenta> Propuesta)
{
    public IReadOnlyList<OperacionSubcuenta> QueFaltan => Propuesta.Where(o => o.Accion != AccionSubcuenta.Renombrar).ToList();
}

/// <summary>El apartado Amortización: el plan (null si no tiene) y la tabla por ejercicio.</summary>
public sealed record AmortizacionDeActivo(PlanAmortizacion? Plan, bool SeAmortiza, IReadOnlyList<FilaTablaAmortizacion> Filas);

/// <summary>Cuenta de resultados de un ejercicio con la del anterior, si se pide.</summary>
public sealed record ResultadosDeActivo(CuentaResultados Actual, CuentaResultados? Anterior, GastosGeneralesAnual? Generales);

/// <summary>Los gastos generales que se han repartido: total de la sociedad, sus ingresos ordinarios y la parte del activo.</summary>
public sealed record GastosGeneralesAnual(decimal GastosGenerales, decimal IngresosOrdinariosSociedad, decimal IngresosOrdinariosActivo, decimal Parte);

/// <summary>
/// Alta paso a paso: los datos y el centro (uno que existe, o uno nuevo bajo un padre con el nombre corto del activo). Un activo financiero lleva además
/// su instrumento (por ISIN) y su primera compra (activos/activos-financieros 2026-11-23 20:32).
/// </summary>
public sealed record AltaActivo(DatosActivo Datos, int? IdCentroPadreNuevo = null, Financieros.DatosInstrumento? Instrumento = null, Financieros.CompraInversion? Compra = null);

/// <summary>Activos de un centro de coste, para abrir la pantalla desde el Impuesto sobre Sociedades.</summary>
public sealed record ActivosDeCentro(int? IdCentroCoste, string TextoCentro, IReadOnlyList<ActivoListado> Activos);
