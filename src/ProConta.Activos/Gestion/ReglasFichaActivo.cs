using ProConta.Contabilidad.Amortizacion;

namespace ProConta.Activos.Gestion;

/// <summary>Datos del inmueble (ActivosInmuebles). Los porcentajes, en tanto por uno (0,4851 = 48,51 %).</summary>
public sealed record DatosInmueble(string? ReferenciaCatastral = null, decimal? ValorSuelo = null, decimal? ValorConstruccion = null, decimal? PorcentajeConstruccion = null,
    decimal? AlquilerMensual = null, DateTime? InicioAlquiler = null, decimal? TributoMetropolitano = null);

/// <summary>
/// Lo que se escribe en el apartado Datos de la ficha y en el alta (activos 2026-11-17 18:50): identificación, compra, inmueble y amortización. El %
/// anual va en tanto por uno, como en PlanesAmortizacionActivo.
/// </summary>
public sealed record DatosActivo
{
    public int IdActivo { get; init; }
    public string Codigo { get; init; } = "";
    public string Nombre { get; init; } = "";
    public string? NombreCorto { get; init; }
    public int IdTipoActivo { get; init; }
    public string CodigoTipo { get; init; } = "";
    public bool EsAjeno { get; init; }
    public string? Notas { get; init; }
    public DateTime? FechaCompra { get; init; }
    public decimal Coste { get; init; }
    public int? IdCentroCoste { get; init; }
    public DatosInmueble? Inmueble { get; init; }
    public bool SeAmortiza { get; init; }
    public DateTime? InicioAmortizacion { get; init; }
    public decimal BaseAmortizable { get; init; }
    public decimal ValorResidual { get; init; }
    public decimal PorcentajeAnual { get; init; }

    public bool EsInmueble => TiposActivoPgc.EsInmueble(CodigoTipo);

    /// <summary>Cuota de un año completo (base × % anual), a céntimos.</summary>
    public decimal CuotaAnual => CalculoAmortizacion.Redondear(BaseAmortizable * PorcentajeAnual);
}

/// <summary>Un dato mal escrito: el campo (nombre de la propiedad de <see cref="DatosActivo"/> o «Inmueble.X») y qué pasa y cómo arreglarlo.</summary>
public sealed record ErrorDatoActivo(string Campo, string Texto);

/// <summary>Una subcuenta del activo con sus apuntes, para decidir qué hacer con ella al cambiar el tipo o la marca de ajeno.</summary>
/// <param name="ApuntesRecientes">Apuntes en el ejercicio de trabajo y los cuatro anteriores.</param>
/// <param name="ApuntesTotales">Apuntes en cualquier ejercicio.</param>
/// <param name="ConSaldoInicial">Alguna de sus cuentas tiene saldo inicial.</param>
public sealed record SubcuentaDelActivo(int IdSubcuentaActivo, int IdIdentidadContable, string? Codigo, string? Nombre, int ApuntesRecientes, int ApuntesTotales,
    bool ConSaldoInicial, IReadOnlyList<int>? EjerciciosRecientes = null);

/// <summary>
/// Lo que se hace al guardar un cambio de tipo o de la marca de ajeno (activos 2026-11-17 18:50): borrar las subcuentas sin ningún apunte, desvincular
/// las que tienen apuntes antiguos (se quedan en el plan de cuentas), generar las de la plantilla del tipo nuevo y, al marcarlo como ajeno, quitarle el
/// centro y desactivar su plan.
/// </summary>
public sealed record CambioTipoActivo(IReadOnlyList<SubcuentaDelActivo> Borrar, IReadOnlyList<SubcuentaDelActivo> Desvincular, bool GenerarNuevas, bool QuitarCentroYPlan)
{
    public bool HayAlgo => Borrar.Count > 0 || Desvincular.Count > 0 || GenerarNuevas || QuitarCentroYPlan;
}

/// <summary>Reglas de la ficha de activo sin base de datos: validación, regla de bloqueo del tipo y del ajeno y cálculos del alta.</summary>
public static class ReglasFichaActivo
{
    public const int LongitudCodigo = 40, LongitudNombre = 255, LongitudNombreCorto = 100, LongitudReferenciaCatastral = 30;

    /// <summary>Años que mira la regla de bloqueo: el ejercicio de trabajo y los cuatro anteriores (propietario 2026-11-17 18:43, C).</summary>
    public const int EjerciciosBloqueo = 5;

    /// <summary>Clase del listado: Inmuebles, Vehículos, Financieros u Otros.</summary>
    public static string Clase(string codigoTipo) => TiposActivoPgc.EsInmueble(codigoTipo) ? "Inmuebles" : codigoTipo == TiposActivoPgc.Vehiculo ? "Vehículos"
        : TiposActivoPgc.EsInversion(codigoTipo) ? "Financieros" : "Otros";

    /// <summary>% de construcción a partir de los valores catastrales (construcción / (suelo + construcción)), a cuatro decimales; null sin valores.</summary>
    public static decimal? PorcentajeConstruccion(decimal? suelo, decimal? construccion)
    {
        var total = (suelo ?? 0m) + (construccion ?? 0m);
        return total > 0m && construccion is decimal c ? Math.Round(c / total, 4, MidpointRounding.AwayFromZero) : null;
    }

    /// <summary>Base que se propone al dar de alta: coste × % de construcción si es un inmueble con ese %; si no, el coste entero. A céntimos.</summary>
    public static decimal BasePropuesta(decimal coste, bool esInmueble, decimal? porcentajeConstruccion) =>
        CalculoAmortizacion.Redondear(coste * (esInmueble && porcentajeConstruccion is > 0m and <= 1m ? porcentajeConstruccion.Value : 1m));

    /// <summary>
    /// Por qué no se puede cambiar el tipo ni la marca de ajeno: si alguna subcuenta del activo tiene apuntes en el ejercicio de trabajo o en los cuatro
    /// anteriores. Null si se puede.
    /// </summary>
    public static string? MotivoBloqueoTipo(int ejercicioTrabajo, IReadOnlyList<SubcuentaDelActivo> subcuentas)
    {
        var conApuntes = subcuentas.Where(s => s.ApuntesRecientes > 0).ToList();
        if (conApuntes.Count == 0) return null;
        var desde = ejercicioTrabajo - EjerciciosBloqueo + 1;
        var ejemplos = string.Join(", ", conApuntes.Take(3).Select(s => $"{s.Codigo ?? "?"} ({(s.ApuntesRecientes == 1 ? "1 apunte" : $"{s.ApuntesRecientes} apuntes")})"));
        return $"El tipo y la marca de ajeno no se pueden cambiar porque sus subcuentas tienen apuntes entre {desde} y {ejercicioTrabajo}: {ejemplos}" +
               (conApuntes.Count > 3 ? $" y {conApuntes.Count - 3} más" : "") +
               ". Solo se permite mientras el activo no se haya usado en los últimos cinco ejercicios (por si te equivocaste al darlo de alta). " +
               "Si de verdad es de otro tipo, da de alta un activo nuevo con el tipo correcto.";
    }

    /// <summary>Qué hacer con las subcuentas al cambiar el tipo o pasarlo a ajeno; null si no cambia ninguna de las dos cosas.</summary>
    public static CambioTipoActivo? PlanificarCambio(DatosActivo antes, DatosActivo despues, IReadOnlyList<SubcuentaDelActivo> subcuentas)
    {
        var cambiaTipo = antes.IdTipoActivo != despues.IdTipoActivo;
        var cambiaAjeno = antes.EsAjeno != despues.EsAjeno;
        if (!cambiaTipo && !cambiaAjeno) return null;
        // Con un cambio de tipo, las subcuentas del tipo anterior sobran; al pasar a ajeno, todas. Al volver de ajeno a propio solo se generan.
        var quitar = cambiaTipo || despues.EsAjeno ? subcuentas : [];
        var borrar = quitar.Where(s => s.ApuntesTotales == 0 && !s.ConSaldoInicial).ToList();
        var desvincular = quitar.Where(s => s.ApuntesTotales > 0 || s.ConSaldoInicial).ToList();
        return new CambioTipoActivo(borrar, desvincular, !despues.EsAjeno, despues.EsAjeno && !antes.EsAjeno);
    }

    /// <summary>Errores de los datos, con qué pasa y cómo arreglarlo. <paramref name="codigoRepetido"/>: ya hay otro activo con ese código en la sociedad.</summary>
    public static IReadOnlyList<ErrorDatoActivo> Validar(DatosActivo d, bool codigoRepetido = false)
    {
        var e = new List<ErrorDatoActivo>();
        var codigo = d.Codigo.Trim();
        if (codigo.Length == 0) e.Add(new(nameof(DatosActivo.Codigo), "Escribe un código corto para el activo (por ejemplo «GavaPoveda»): sirve para buscarlo y es único en la sociedad."));
        else if (codigo.Length > LongitudCodigo) e.Add(new(nameof(DatosActivo.Codigo), $"El código tiene {codigo.Length} caracteres y caben {LongitudCodigo}: acórtalo."));
        else if (codigoRepetido) e.Add(new(nameof(DatosActivo.Codigo), $"Ya hay otro activo con el código «{codigo}» en esta sociedad: elige otro."));
        var nombre = d.Nombre.Trim();
        if (nombre.Length == 0) e.Add(new(nameof(DatosActivo.Nombre), "Escribe el nombre del activo: se usa en el nombre de sus subcuentas."));
        else if (nombre.Length > LongitudNombre) e.Add(new(nameof(DatosActivo.Nombre), $"El nombre tiene {nombre.Length} caracteres y caben {LongitudNombre}: acórtalo."));
        if ((d.NombreCorto ?? "").Trim().Length > LongitudNombreCorto)
            e.Add(new(nameof(DatosActivo.NombreCorto), $"En el nombre corto caben {LongitudNombreCorto} caracteres: acórtalo."));
        if (d.IdTipoActivo <= 0) e.Add(new(nameof(DatosActivo.IdTipoActivo), "Elige el tipo de activo: de él salen sus subcuentas."));
        if (d.Coste < 0m) e.Add(new(nameof(DatosActivo.Coste), "El coste de compra no puede ser negativo: escribe el precio más los gastos de compra."));
        if (!d.EsAjeno)
        {
            if (d.FechaCompra is null) e.Add(new(nameof(DatosActivo.FechaCompra), "Escribe la fecha de compra (dd/mm/aaaa): desde ella se amortiza."));
            if (d.IdCentroCoste is null)
                e.Add(new(nameof(DatosActivo.IdCentroCoste), "Elige su centro de coste con «Elegir…»: de él hereda la actividad de IVA y en él se apuntan sus gastos e ingresos."));
        }
        if (d.Inmueble is { } i)
        {
            if ((i.ReferenciaCatastral ?? "").Trim().Length > LongitudReferenciaCatastral)
                e.Add(new("Inmueble.ReferenciaCatastral", $"La referencia catastral tiene {(i.ReferenciaCatastral ?? "").Trim().Length} caracteres y caben {LongitudReferenciaCatastral} (en el recibo del IBI son 20): revísala."));
            if (i.ValorSuelo < 0m) e.Add(new("Inmueble.ValorSuelo", "El valor catastral del suelo no puede ser negativo."));
            if (i.ValorConstruccion < 0m) e.Add(new("Inmueble.ValorConstruccion", "El valor catastral de la construcción no puede ser negativo."));
            if (i.PorcentajeConstruccion is < 0m or > 1m) e.Add(new("Inmueble.PorcentajeConstruccion", "El % de construcción va de 0 a 100."));
            if (i.AlquilerMensual < 0m) e.Add(new("Inmueble.AlquilerMensual", "El alquiler mensual no puede ser negativo."));
            if (i.TributoMetropolitano < 0m) e.Add(new("Inmueble.TributoMetropolitano", "El tributo metropolitano no puede ser negativo."));
        }
        if (d.SeAmortiza && !d.EsAjeno)
        {
            if (d.InicioAmortizacion is null) e.Add(new(nameof(DatosActivo.InicioAmortizacion), "Escribe desde qué fecha se amortiza (normalmente, la de compra)."));
            if (d.PorcentajeAnual <= 0m || d.PorcentajeAnual > 1m)
                e.Add(new(nameof(DatosActivo.PorcentajeAnual), "Escribe el % anual de amortización, entre 0 y 100 (por ejemplo 3 para un inmueble o 16 para un vehículo)."));
            if (d.BaseAmortizable <= 0m) e.Add(new(nameof(DatosActivo.BaseAmortizable), "Escribe la base que se amortiza (el coste, o coste × % de construcción en un inmueble)."));
            if (d.ValorResidual < 0m) e.Add(new(nameof(DatosActivo.ValorResidual), "El valor residual no puede ser negativo (lo normal es 0)."));
            else if (d.ValorResidual > d.BaseAmortizable && d.BaseAmortizable > 0m)
                e.Add(new(nameof(DatosActivo.ValorResidual), "El valor residual no puede superar la base: con él no quedaría nada que amortizar."));
        }
        return e;
    }

    /// <summary>Vida útil en meses de un % anual lineal (columna obligatoria del plan): 12 / %, redondeado; 0 sin %.</summary>
    public static int VidaUtilMeses(decimal porcentajeAnual) => porcentajeAnual <= 0m ? 0 : (int)Math.Round(12m / porcentajeAnual, MidpointRounding.AwayFromZero);

    /// <summary>Código del centro nuevo de un activo: su código, sin «/» y a 30 caracteres como mucho.</summary>
    public static string CodigoCentro(string codigoActivo)
    {
        var c = codigoActivo.Trim().Replace("/", "-");
        return c.Length <= 30 ? c : c[..30];
    }
}
