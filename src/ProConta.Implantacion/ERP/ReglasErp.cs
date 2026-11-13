using System.Globalization;
using ProConta.Facturacion.Facturas;
using ProConta.Nucleo.Importes;
using ProConta.Terceros.Contactos;

namespace ProConta.Implantacion.ERP;

/// <summary>Línea de venta del ERP tal como se lee (LineasFacturaVenta o LineasFacturaProgramada).</summary>
public sealed record LineaVentaErp(int NumLinea, int? IdConcepto, string? Descripcion, decimal Cantidad, decimal Precio, decimal TipoIva, DateTime? Fecha,
    bool EsCabecera, bool EsDescuento, decimal DescuentoPorcentual, decimal DescuentoAbsoluto, string? DescripcionCompleta);

/// <summary>Lo que el ERP dice del concepto de una línea: su cuenta (vía Agrupaciones) y su departamento.</summary>
public sealed record ConceptoLineaErp(string? Cuenta, string? Departamento);

/// <summary>Una línea ya transformada al modelo nuevo, o la retención que la línea representaba.</summary>
public sealed record LineaTransformada(LineaFacturaVenta? Linea, decimal? TipoRetencion, decimal ImporteRetencion, string? CuentaRetencion);

/// <summary>
/// Reglas sin BD de la migración del ERP (implantacion/migracion-erp): conversiones de campos y recálculo de totales. Las
/// cubren las pruebas unitarias con datos inventados.
/// </summary>
public static class ReglasErp
{
    /// <summary>
    /// Campos de configuración que quedan en SociedadesEjercicios y su columna en la tabla Sociedades del ERP
    /// (organizacion/sociedades-ejercicios 2026-11-13 20:55). Los que el ERP no tiene (tipo general, importación y redondeo) se
    /// leen si aparecen; si no, se quedan vacíos.
    /// </summary>
    public static readonly IReadOnlyList<(string Campo, string Origen)> CamposConfiguracionEjercicio =
    [
        ("FormaPagoDefecto", "FormaPagoDefecto"), ("TrimestreActivo", "TrimestreActivo"), ("TipoIVAGeneral", "TipoIVAGeneral"),
        ("CuentaSuplido", "CuentaSuplido"), ("CuentaRetencionServicios", "CuentaRetencionServicios"), ("CuentaRetencionAlquileres", "CuentaRetencionAlquileres"),
        ("CuentaIVAIntracomunitarioRepercutidoServicios", "CuentaIVAIntracomunitarioRepercutidoServicios"),
        ("CuentaIVAIntracomunitarioRepercutidoBienes", "CuentaIVAIntracomunitarioRepercutidoBienes"),
        ("CuentaIVAIntracomunitarioSoportadoServicios", "CuentaIVAIntracomunitarioSoportadoServicios"),
        ("CuentaIVAIntracomunitarioSoportadoBienes", "CuentaIVAIntracomunitarioSoportadoBienes"),
        ("CuentaIVAInversionSujetoPasivoRepercutido", "CuentaIVAInversionSujetoPasivoRepercutido"),
        ("CuentaIVAInversionSujetoPasivoSoportado", "CuentaIVAInversionSujetoPasivoSoportado"),
        ("CuentaIVAImportacion", "CuentaIVAImportacion"), ("CuentaRedondeoGastos", "CuentaRedondeoGastos"), ("CuentaRedondeoIngresos", "CuentaRedondeoIngresos"),
        ("PorcentajeProrrataAñoAnterior", "PorcentajeProrrataAñoAnterior"),
    ];

    /// <summary>Cuenta de IVA de importación del ejercicio en curso (propietario 2026-11-13 23:23): 4724 + el 21 % con tres cifras.</summary>
    public const string CuentaIvaImportacionEjercicioEnCurso = "4724021";

    private static readonly HashSet<string> CamposNumericos = ["FormaPagoDefecto", "TrimestreActivo", "TipoIVAGeneral", "PorcentajeProrrataAñoAnterior"];

    /// <summary>
    /// Fila del ERP de la que sale la configuración de un ejercicio: la suya o, si el ERP no tiene ese ejercicio, la del más
    /// cercano (a igual distancia, el posterior). Null si el ERP no tiene ninguna.
    /// </summary>
    public static (int Anio, Fila Fila)? ConfiguracionDeEjercicio(IReadOnlyDictionary<int, Fila> porAnio, int anio)
    {
        if (porAnio.TryGetValue(anio, out var propia)) return (anio, propia);
        if (porAnio.Count == 0) return null;
        var cercano = porAnio.Keys.OrderBy(a => Math.Abs(a - anio)).ThenByDescending(a => a).First();
        return (cercano, porAnio[cercano]);
    }

    /// <summary>
    /// Razón social de la empresa en un año: la de ese año o, si no la hay, la del año más cercano (a igual distancia, el
    /// posterior). Null si no hay ninguna.
    /// </summary>
    public static string? RazonSocialDelAnio(IReadOnlyDictionary<int, string> porAnio, int anio) =>
        porAnio.TryGetValue(anio, out var propia) ? propia
        : porAnio.Count == 0 ? null
        : porAnio[porAnio.Keys.OrderBy(a => Math.Abs(a - anio)).ThenByDescending(a => a).First()];

    /// <summary>
    /// Prorrata del año anterior en porcentaje (14 = 14 %; organizacion/sociedades-ejercicios 2026-11-13 22:45). El ERP la
    /// guarda en tanto por uno (0,14): un valor de 0 a 1 se multiplica por 100; uno mayor ya es un porcentaje. Dos decimales.
    /// </summary>
    public static decimal ProrrataEnPorcentaje(decimal valor) => Math.Round(valor is >= 0m and <= 1m ? valor * 100m : valor, 2);

    /// <summary>
    /// Valores que la migración escribe en el ejercicio: los no vacíos del ERP. La cuenta de suplidos es CuentaSuplido o, si está
    /// vacía, la «Cuenta de suplido con IVA» del ERP (5550002; propietario 2026-11-13 20:40). Los porcentajes (SINGLE) se
    /// redondean a 4 decimales para no arrastrar el error del tipo de coma flotante.
    /// </summary>
    public static IReadOnlyList<(string Campo, object Valor)> ValoresConfiguracionEjercicio(Fila erp)
    {
        var r = new List<(string, object)>();
        foreach (var (campo, origen) in CamposConfiguracionEjercicio)
        {
            if (campo == "CuentaSuplido")
            {
                if ((erp.T("CuentaSuplido") ?? erp.T("CuentaSuplidoIVA")) is { } suplido) r.Add((campo, Recortar(suplido, 255)!));
                continue;
            }
            if (!CamposNumericos.Contains(campo))
            {
                if (erp.T(origen) is { } texto) r.Add((campo, Recortar(texto, 10)!));
                continue;
            }
            switch (campo)
            {
                case "FormaPagoDefecto" when erp.E(origen) is int fp and >= 0 and <= 255: r.Add((campo, fp)); break;
                case "TrimestreActivo" when erp.E(origen) is int t and >= 1 and <= 4: r.Add((campo, t)); break;
                case "TipoIVAGeneral" when erp.D(origen) is decimal d: r.Add((campo, (float)Math.Round(d, 4))); break;
                case "PorcentajeProrrataAñoAnterior" when erp.D(origen) is decimal d: r.Add((campo, (float)ProrrataEnPorcentaje(d))); break;
            }
        }
        return r;
    }

    /// <summary>Factura ficticia que marca el inicio de la numeración del ejercicio 2026 (propietario 2026-11-11 12:15): no se migra.</summary>
    public const int FacturaFicticia = 83000;

    /// <summary>Conceptos del ERP obsoletos que no pasan al catálogo (propietario 2026-11-11 17:19).</summary>
    public static readonly IReadOnlySet<int> ConceptosObsoletos = new HashSet<int> { 413, 4752 };

    /// <summary>Personas del ERP que son inmuebles (ya van como activos), no terceros (propietario 2026-11-11 17:19).</summary>
    public static bool EsInmueblePersona(int idPersona) => idPersona is >= 100 and <= 125;

    /// <summary>Las facturas de esta fecha en adelante aún no están contabilizadas en el ERP: entran sin asiento ni registro del libro.</summary>
    public static readonly DateTime InicioSinContabilizar = new(2026, 10, 1);

    /// <summary>Total del ERP recalculado (M-12): Σ Cantidad × Precio de las líneas que no son de cabecera, redondeando cada línea.</summary>
    public static decimal TotalLineas(IEnumerable<LineaVentaErp> lineas) =>
        lineas.Where(l => !l.EsCabecera).Sum(l => Importe.RedondearValor(l.Cantidad * l.Precio));

    /// <summary>Una cuenta 473 es de retenciones: la línea que la usa es la retención de la factura, no una línea de base.</summary>
    public static bool EsCuentaRetencion(string? cuenta) => cuenta?.Trim() is { } c && c.StartsWith("473", StringComparison.Ordinal);

    /// <summary>Clave del centro de coste nuevo de un departamento del ERP (318 → «3/318»; 3 → «3»).</summary>
    public static string? ClaveCentro(string? departamento)
    {
        var d = departamento?.Trim();
        if (string.IsNullOrEmpty(d) || !d.All(char.IsAsciiDigit) || d == "0") return null;
        return d.Length == 1 ? d : $"{d[0]}/{d}";
    }

    /// <summary>Clave del centro de un departamento y subdepartamento (Inmuebles, PalabrasClaveSubDepartamento, FacturasCompra).</summary>
    public static string? ClaveCentro(int? departamento, int? subdepartamento) =>
        subdepartamento is > 0 ? ClaveCentro(subdepartamento.Value.ToString(CultureInfo.InvariantCulture))
        : departamento is > 0 ? ClaveCentro(departamento.Value.ToString(CultureInfo.InvariantCulture)) : null;

    /// <summary>
    /// Convierte una línea del ERP a una línea nueva. El precio del ERP ya lleva IVA y descuento: el total de la línea es
    /// Cantidad × Precio y la base y la cuota salen de él. Una línea de cabecera solo conserva su descripción. Una línea con
    /// concepto de retención (473) no es una línea: su porcentaje es la cantidad (0,19 → 19 %) y su importe, el total en positivo.
    /// </summary>
    public static LineaTransformada Transformar(LineaVentaErp l, ConceptoLineaErp? concepto, int? idCentroCoste)
    {
        var descripcion = Recortar(l.Descripcion ?? l.DescripcionCompleta ?? "", 255) ?? "";
        if (EsCuentaRetencion(concepto?.Cuenta) && !l.EsCabecera)
        {
            var importe = -Importe.RedondearValor(l.Cantidad * l.Precio);
            return new LineaTransformada(null, Math.Round(Math.Abs(l.Cantidad) * 100m, 2), importe, concepto!.Cuenta!.Trim());
        }
        var linea = new LineaFacturaVenta
        {
            Nombre = descripcion,
            Descripcion = descripcion,
            NotasInternas = l.DescripcionCompleta is { } dc && dc.Trim() != descripcion.Trim() ? Recortar(dc, 4000) : null,
            Fecha = l.Fecha,
            TipoIva = l.EsCabecera ? 0m : l.TipoIva,
            CausaExencion = !l.EsCabecera && l.TipoIva == 0m ? "E1" : null,
            CuentaContable = l.EsCabecera ? null : concepto?.Cuenta?.Trim(),
            IdCentroCoste = l.EsCabecera ? null : idCentroCoste,
        };
        if (l.EsCabecera)
        {
            linea.Cantidad = 0m;
            linea.PrecioUnitario = 0m;
            linea.Recalcular();
            return new LineaTransformada(linea, null, 0m, null);
        }
        var cantidad = l.Cantidad;
        var total = Importe.RedondearValor(cantidad * l.Precio);
        linea.Cantidad = cantidad == 0m ? 1m : cantidad;
        linea.FijarTotal(total);
        // FijarTotal deja el precio unitario sin IVA (base / cantidad); se conserva la cantidad del ERP.
        if (cantidad == 0m) { linea.Cantidad = 0m; }
        return new LineaTransformada(linea, null, 0m, null);
    }

    /// <summary>Concepto agrupado del catálogo nuevo (facturacion/conceptos), o null si el concepto del ERP se migra tal cual.</summary>
    public static string? Agrupacion(int id, int? tipo, int? codigoTipoIva) => tipo switch
    {
        124 when id is 4728 or 4729 => "Alquiler piso turístico",
        124 when codigoTipoIva == 2 => "Alquiler mensual piso",
        124 when codigoTipoIva == 6 => "Alquiler mensual local",
        125 => "Retención IRPF",
        126 => "Residuos comerciales",
        _ => null,
    };

    /// <summary>Tipo de IVA de ConceptosCaja.CodigoTipoIVA (2 exento, 6 general).</summary>
    public static decimal? TipoIvaConcepto(int? codigo) => codigo switch { 2 => 0m, 6 => 21m, _ => null };

    /// <summary>Dato de contacto de DatosContacto: 4 móvil, 5 email, el resto teléfono fijo.</summary>
    public static (TipoDatoContacto Tipo, string Etiqueta) TipoContacto(int? tipo, string? descripcion) => tipo switch
    {
        5 => (TipoDatoContacto.Email, descripcion ?? "Email"),
        4 => (TipoDatoContacto.Telefono, descripcion ?? "Móvil"),
        3 => (TipoDatoContacto.Telefono, descripcion ?? "Fax"),
        _ => (TipoDatoContacto.Telefono, descripcion ?? "Fijo"),
    };

    /// <summary>Notas y Mas Notas en un solo texto.</summary>
    public static string? UnirNotas(params string?[] partes)
    {
        var p = partes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
        return p.Count == 0 ? null : string.Join(Environment.NewLine, p);
    }

    /// <summary>Ruta relativa a la raíz de documentos (D:\SynologyBox\2025\… → 2025\…); null si la ruta no está bajo la raíz.</summary>
    public static string? RutaRelativa(string? ruta, string raiz)
    {
        if (string.IsNullOrWhiteSpace(ruta)) return null;
        var r = ruta.Trim();
        var b = raiz.Trim().TrimEnd('\\', '/');
        if (b.Length == 0 || !r.StartsWith(b, StringComparison.OrdinalIgnoreCase) || r.Length <= b.Length || r[b.Length] is not ('\\' or '/')) return null;
        return r[(b.Length + 1)..];
    }

    /// <summary>TipoRetencion de compras del ERP (1 servicios, 5 arrendamientos) → ClaseRetencion nueva (1 profesionales, 2 arrendamientos).</summary>
    public static byte? ClaseRetencionCompra(int? tipo) => tipo switch { 1 => 1, 5 => 2, _ => null };

    /// <summary>Importe de Norma 43 (14 cifras con dos decimales implícitos).</summary>
    public static decimal? ImporteN43(string? texto) =>
        long.TryParse(texto?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n / 100m : null;

    /// <summary>Fecha de Norma 43 (AAMMDD).</summary>
    public static DateTime? FechaN43(string? texto) =>
        DateTime.TryParseExact(texto?.Trim(), "yyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    /// <summary>Campos con el mismo valor en todos los registros (criterio general 1: solo en tablas de más de tres).</summary>
    public static IReadOnlyList<string> CamposConstantes(IReadOnlyList<Fila> filas)
    {
        if (filas.Count <= 3) return [];
        return filas[0].Campos.Where(c =>
        {
            var primero = Convert.ToString(filas[0].Valor(c), CultureInfo.InvariantCulture);
            return filas.All(f => Convert.ToString(f.Valor(c), CultureInfo.InvariantCulture) == primero);
        }).ToList();
    }

    /// <summary>Número de factura del ERP (Id) como texto, para Facturas.NumeroFactura y los enlaces por DOCAPU/FACIVR.</summary>
    public static string Numero(int id) => id.ToString(CultureInfo.InvariantCulture);

    public static string? Recortar(string? texto, int maximo) =>
        string.IsNullOrWhiteSpace(texto) ? null : texto.Trim() is var t && t.Length > maximo ? t[..maximo] : texto.Trim();

    /// <summary>
    /// Reparte un tramo del libro (base y cuota de un tipo) entre las 477 de las actividades en proporción a las bases de las
    /// líneas de cada una (decisión del propietario 2026-11-11, opción a). Conserva exactamente los totales: el resto de redondeo va
    /// a la parte mayor.
    /// </summary>
    public static IReadOnlyList<(string Cuenta, decimal Base, decimal Cuota)> Repartir(decimal @base, decimal cuota, IReadOnlyList<(string Cuenta, decimal Base)> partes)
    {
        var validas = partes.Where(p => !string.IsNullOrWhiteSpace(p.Cuenta)).GroupBy(p => p.Cuenta).Select(g => (Cuenta: g.Key, Base: g.Sum(x => x.Base))).ToList();
        if (validas.Count == 0) return [];
        if (validas.Count == 1) return [(validas[0].Cuenta, @base, cuota)];
        var suma = validas.Sum(p => p.Base);
        var r = new List<(string Cuenta, decimal Base, decimal Cuota)>();
        foreach (var p in validas)
        {
            var peso = suma == 0m ? 1m / validas.Count : p.Base / suma;
            r.Add((p.Cuenta, Importe.RedondearValor(@base * peso), Importe.RedondearValor(cuota * peso)));
        }
        var mayor = r.Select((x, i) => (x, i)).OrderByDescending(x => Math.Abs(x.x.Base)).First().i;
        r[mayor] = (r[mayor].Cuenta, r[mayor].Base + (@base - r.Sum(x => x.Base)), r[mayor].Cuota + (cuota - r.Sum(x => x.Cuota)));
        return r;
    }
}
