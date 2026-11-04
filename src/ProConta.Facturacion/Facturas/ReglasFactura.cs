using System.Globalization;

namespace ProConta.Facturacion.Facturas;

/// <summary>
/// Problema que impide validar una factura (error) o que hay que confirmar (aviso). <see cref="Campo"/> dice dónde
/// corregirlo («Cliente», «Descripcion», «Linea3.Nombre»…) y <see cref="Accion"/> qué ofrecer («AsignarCuentaCliente»…).
/// </summary>
public sealed record Incidencia(string Codigo, string Mensaje, string? Campo = null, bool EsAviso = false, string? Accion = null)
{
    public const string ImporteCero = "IMPORTE_CERO";
}

/// <summary>Serie de facturación de un ejercicio (tabla SeriesFacturacion).</summary>
public sealed record Serie(
    int IdSerie,
    int IdSociedadEjercicio,
    string Descripcion,
    string Prefijo,
    int RangoDesde,
    int RangoHasta,
    int? UltimoNumero,
    bool EsRectificativa,
    bool EsPredeterminada)
{
    public int Siguiente => (UltimoNumero ?? RangoDesde - 1) + 1;
    public bool Agotada => Siguiente > RangoHasta;
    public bool EnUso => UltimoNumero is not null;

    /// <summary>Texto para listas: «F2026 · Facturas 2026 (predeterminada)».</summary>
    public string Rotulo => $"{Prefijo.Trim()} · {Descripcion.Trim()}{(EsPredeterminada ? " (predeterminada)" : "")}{(EsRectificativa ? " (rectificativas)" : "")}";
}

/// <summary>Reglas sin BD de la factura de venta (RF-FAC-01, 04, 07).</summary>
public static class ReglasFactura
{
    public const int LongitudNombreLinea = 255;
    public const int LongitudMotivo = 255;
    public const int CifrasPorDefecto = 4;

    /// <summary>
    /// Validación previa a la emisión: devuelve todos los errores y avisos a la vez y no cambia la factura. Las
    /// comprobaciones que necesitan la BD (cuentas, serie, ejercicio) las añade el servicio.
    /// </summary>
    public static IReadOnlyList<Incidencia> ValidarParaEmision(FacturaVenta f, DateTime? ultimaFechaSerie)
    {
        var r = new List<Incidencia>();
        if (f.Estado != EstadoFactura.Borrador)
            r.Add(new("Facturas.NoEsBorrador", $"Solo se valida un borrador y esta factura está {TextosEstado.Texto(f.Estado).ToLowerInvariant()}."));
        if (f.IdCliente is null)
            r.Add(new("Facturas.SinCliente", "Falta el cliente. Elige uno con «Buscar cliente» (F3).", "Cliente"));
        if (string.IsNullOrWhiteSpace(f.Descripcion))
            r.Add(new("Facturas.SinDescripcion", "Falta la descripción general. Es la que se comunica a la AEAT: resume qué se factura.", "Descripcion"));
        if (string.IsNullOrWhiteSpace(f.TipoFactura))
            r.Add(new("Facturas.SinTipo", "Falta el tipo de factura.", "TipoFactura"));
        if (f.EsRectificativa && string.IsNullOrWhiteSpace(f.MotivoRectificacion))
            r.Add(new("Facturas.SinMotivo", "Falta el motivo de la rectificación. Explica por qué se anula la factura original.", "MotivoRectificacion"));
        else if (f.MotivoRectificacion is { } m && m.Trim().Length > LongitudMotivo)
            r.Add(new("Facturas.MotivoLargo", $"El motivo de la rectificación tiene más de {LongitudMotivo} caracteres; acórtalo.", "MotivoRectificacion"));
        if (f.Lineas.Count == 0)
            r.Add(new("Facturas.SinLineas", "La factura no tiene líneas. Añade al menos una con «Añadir línea».", "Lineas"));
        if (f.FechaInicioValidez is { } d && f.FechaFinValidez is { } h && d.Date > h.Date)
            r.Add(new("Facturas.PeriodoAlReves", $"El periodo facturado termina ({h:dd/MM/yyyy}) antes de empezar ({d:dd/MM/yyyy}). Revisa «Desde» y «Hasta».", "FechaFinValidez"));
        if (f.TipoRetencion is < 0m or > 100m)
            r.Add(new("Facturas.RetencionFueraDeRango", $"La retención debe estar entre 0 y 100 % y es {f.TipoRetencion.ToString(CultureInfo.CurrentCulture)} %.", "TipoRetencion"));
        if (f.FechaFactura is { } fecha && ultimaFechaSerie is { } ultima && fecha.Date < ultima.Date)
            r.Add(new("Facturas.FechaAnteriorSerie",
                $"La fecha de factura ({fecha:dd/MM/yyyy}) es anterior a la de la última factura numerada de la serie ({ultima:dd/MM/yyyy}). Usa esa fecha o una posterior.",
                "FechaFactura"));

        foreach (var l in f.Lineas)
        {
            var donde = $"La línea {l.NumeroLinea}";
            var campo = $"Linea{l.NumeroLinea}";
            if (string.IsNullOrWhiteSpace(l.Nombre))
                r.Add(new("Facturas.LineaSinNombre", $"{donde} no tiene nombre. Escribe qué se factura en ella.", campo + ".Nombre"));
            else if (l.Nombre.Trim().Length > LongitudNombreLinea)
                r.Add(new("Facturas.LineaNombreLargo", $"{donde} tiene un nombre de más de {LongitudNombreLinea} caracteres; acórtalo.", campo + ".Nombre"));
            foreach (var (valor, nombre, c) in new[] { (l.TipoIva, "IVA", "TipoIva"), (l.TipoRecargo, "recargo de equivalencia", "TipoRecargo"), (l.DescuentoPorcentaje, "descuento", "DescuentoPorcentaje") })
                if (valor is < 0m or > 100m)
                    r.Add(new("Facturas.PorcentajeFueraDeRango", $"{donde} tiene un {nombre} del {valor.ToString(CultureInfo.CurrentCulture)} %: debe estar entre 0 y 100.", $"{campo}.{c}"));
            if (string.IsNullOrWhiteSpace(l.Impuesto) || string.IsNullOrWhiteSpace(l.CalificacionOperacion))
                r.Add(new("Facturas.LineaSinDesgloseFiscal", $"{donde} no tiene impuesto o calificación fiscal.", campo));
            var exentaONoSujeta = !string.IsNullOrWhiteSpace(l.CausaExencion) || l.CalificacionOperacion is "N1" or "N2";
            if (!l.EsSuplido && exentaONoSujeta && (l.TipoIva != 0m || l.TipoRecargo != 0m))
                r.Add(new("Facturas.ExentaConTipo", $"{donde} es exenta o no sujeta y lleva IVA. Pon el IVA a 0 o cambia su calificación.", $"{campo}.TipoIva"));
            if (!l.EsSuplido && string.IsNullOrWhiteSpace(l.CuentaContable))
                r.Add(new("Facturas.LineaSinCuenta", $"{donde} no tiene cuenta de ingreso. Escríbela o elige un concepto que la tenga.", $"{campo}.CuentaContable"));
        }

        if (f.Lineas.Count > 0 && f.Totales.ImporteTotal.EsCero)
            r.Add(new(Incidencia.ImporteCero, "El total de la factura es 0,00 €. Comprueba que no falte ningún precio o línea; si es correcto, confirma la validación.",
                null, EsAviso: true));
        return r;
    }

    /// <summary>
    /// Campos que se pueden cambiar en una factura que ya no es borrador (RF-FAC-07): sus notas internas y, por los
    /// servicios, la referencia contable, el PDF y el historial.
    /// </summary>
    public static bool CampoModificable(FacturaVenta f, string campo) =>
        f.EsBorrador || campo is nameof(FacturaVenta.NotasInternas) or nameof(FacturaVenta.IdAsientoContable)
            or nameof(FacturaVenta.FechaExportacionContable) or nameof(FacturaVenta.Historial);

    /// <summary>
    /// Número fiscal: prefijo de la serie y correlativo. Las cifras se toman del último número de la serie si existe
    /// (para continuar el formato de las facturas ya emitidas); si no, cuatro.
    /// </summary>
    public static string ComponerNumero(Serie serie, int numero, string? ultimoNumeroEmitido)
    {
        var prefijo = serie.Prefijo.Trim();
        var cifras = CifrasPorDefecto;
        if (!string.IsNullOrWhiteSpace(ultimoNumeroEmitido) && ultimoNumeroEmitido.Trim().StartsWith(prefijo, StringComparison.Ordinal))
        {
            var resto = ultimoNumeroEmitido.Trim()[prefijo.Length..];
            var final = new string(resto.Reverse().TakeWhile(char.IsAsciiDigit).Reverse().ToArray());
            if (final.Length > 0)
            {
                cifras = final.Length;
                prefijo += resto[..^final.Length];
            }
        }
        return prefijo + numero.ToString(new string('0', Math.Max(1, cifras)), CultureInfo.InvariantCulture);
    }

    /// <summary>Tipo de la rectificativa (decisión histórica): R5 si la original es simplificada (F2), R4 en el resto.</summary>
    public static string TipoRectificativaDe(string tipoOriginal) => tipoOriginal == "F2" ? "R5" : "R4";
}
