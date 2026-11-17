using System.Globalization;
using ProConta.Compras.Facturas;
using ProConta.Contabilidad.Balances;
using ProConta.Contabilidad.Iva;
using ProConta.Contabilidad.Ratios;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Facturas;
using ProConta.Informes.Excel;
using ProConta.Inicio.Salud;
using ProConta.Nucleo.Contexto;

namespace ProConta.Inicio.Gestoria;

/// <summary>
/// Compone el informe para la gestoría de un trimestre (informes/informe-gestoria): pide cada cifra al nodo que la calcula y
/// solo la junta con el sello. Vive aquí, con la salud de Inicio, porque es la capa que ya lee de todos los módulos
/// (contabilidad, facturación, compras y bancos) sin crear ciclos: Informes no puede referenciar a Facturación, que lo usa
/// para el PDF de la factura. Solo lee la BD.
/// <list type="bullet">
/// <item>IVA: todas las líneas del ejercicio (los cuatro trimestres), el 303 con la prorrata provisional y la prorrata real.</item>
/// <item>PyG y Situación: los balances de contabilidad/balances con los apuntes hasta el último día del trimestre.</item>
/// <item>Ratios: los de la pantalla de inicio (informes/ratios), con el balance a fin del trimestre y el resultado de los 12 meses.</item>
/// <item>Emitidas y recibidas: hasta el último día del trimestre. La operación e ISP de una recibida son los de su línea de IVA,
/// si la tiene, para que las dos hojas digan lo mismo.</item>
/// </list>
/// </summary>
public sealed class ComposicionInformeGestoria(IBaseDatos bd)
{
    /// <param name="inversiones">Cuentas de bancos marcadas como inversión en la pantalla de inicio (no son disponible).</param>
    public async Task<InformeGestoria> ComponerAsync(
        ContextoSociedadEjercicio ctx, string razonSocial, int trimestre, DateTime ahora, IReadOnlySet<string>? inversiones = null, CancellationToken cancelacion = default)
    {
        if (trimestre is < 1 or > 4) throw new ArgumentOutOfRangeException(nameof(trimestre), trimestre, "El trimestre va de 1 a 4.");
        var fin = InformeGestoria.FinDe(ctx.Ejercicio, trimestre);
        var lector = new LectorLineasIva(bd);
        // El 303 y la prorrata juntos: con la definitiva fijada, el 4T lleva la regularización (contabilidad/procesos/prorrata).
        var (lineas, provisional, _, iva) = await lector.CalcularAsync(ctx, cancelacion);
        var (r303, prorrata) = (iva.Modelo303, iva.Prorrata);

        var balances = new GeneradorBalances(bd);
        var pyg = await balances.GenerarAsync(TipoBalance.PerdidasYGanancias, ctx, razonSocial, cancelacion, fin);
        var situacion = await balances.GenerarAsync(TipoBalance.Situacion, ctx, razonSocial, cancelacion, fin);

        var (ratios, motivo) = await RatiosAsync(ctx, fin, inversiones ?? new HashSet<string>(StringComparer.Ordinal), cancelacion);

        var porAsiento = lineas.Where(l => l.Sentido == SentidoIva.Soportado).GroupBy(l => l.IdAsiento).ToDictionary(g => g.Key, g => g.First());
        var emitidas = (await new ListadoEmitidas(bd).LeerAsync(ctx, fin, cancelacion))
            .Select(e => new FilaEmitida(e.Numero, e.Fecha, e.Trimestre, e.Nif, e.Cliente, e.Descripcion, e.Base, e.Iva, e.Retencion, e.Total, e.Centro, e.Departamento, e.Contabilizada))
            .ToList();
        var recibidas = (await new ListadoRecibidas(bd).LeerAsync(ctx, fin, cancelacion))
            .Select(r =>
            {
                var linea = r.IdAsiento is int a ? porAsiento.GetValueOrDefault(a) : null;
                var operacion = linea?.Operacion.Texto() ?? r.Operacion switch
                {
                    OperacionCompra.Intracomunitaria => TextosIva.Intracomunitaria,
                    OperacionCompra.Importacion => TextosIva.Importacion,
                    _ => TextosIva.Nacional,
                };
                return new FilaRecibida(r.Fecha, r.Trimestre, r.IdAsiento, r.Nif, r.Proveedor, r.Numero, operacion, linea?.Isp ?? r.Isp, r.Base, r.Iva, r.Retencion,
                    r.Centro, r.Area, r.RutaPdf, r.PdfExiste);
            }).ToList();

        var salud = new FuenteSaludAccess(bd);
        var ejercicio = new EjercicioSociedad(ctx.IdSociedadEjercicio, ctx.Ejercicio);
        var cuadre = (await salud.LibrosIvaAsync(ejercicio, cancelacion))
            .Select(q => new CuadreIvaTrimestre(q.Trimestre, q.LibroRepercutido, q.Cuentas477, q.LibroSoportado, q.Cuentas472)).ToList();
        var activos = await salud.ActivosAmortizablesAsync(ctx.IdSociedad, cancelacion);
        var (ultimo, fuera) = await bd.EjecutarAsync(async s =>
        {
            var desde = new DateTime(ctx.Ejercicio, 1, 1);
            var hasta = desde.AddYears(1);
            var u = await s.EscalarAsync("SELECT MAX(FechaContable) FROM Asientos WHERE IdSociedadEjercicio = ? AND EstadoAsiento = 1 AND TipoAsiento <> 3 AND FechaContable >= ? AND FechaContable < ?",
                ctx.IdSociedadEjercicio, desde, hasta);
            var n = await s.EscalarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = ? AND EstadoAsiento <> 2 AND (FechaContable < ? OR FechaContable >= ?)",
                ctx.IdSociedadEjercicio, desde, hasta);
            return (u is DateTime d ? d.Date : (DateTime?)null, Convert.ToInt32(n ?? 0, CultureInfo.InvariantCulture));
        }, cancelacion);

        return new InformeGestoria(razonSocial, ctx.Ejercicio, trimestre, ahora, ultimo, provisional, lineas, r303, prorrata, pyg, situacion, ratios, motivo,
            emitidas, recibidas, cuadre, fuera, activos);
    }

    /// <summary>
    /// Los ratios de la pantalla de inicio con el balance al último día del trimestre (<see cref="CalculadoraInicio.Ratios"/> toma
    /// como fecha del balance el fin del mes anterior al corte: el corte es el día siguiente al del trimestre).
    /// </summary>
    private async Task<(ResultadoRatios? Ratios, string? Motivo)> RatiosAsync(ContextoSociedadEjercicio ctx, DateTime fin, IReadOnlySet<string> inversiones, CancellationToken cancelacion)
    {
        var inicio = new FuenteInicioAccess(bd);
        var situacion = await inicio.ModeloSituacionAsync(cancelacion);
        if (situacion is null) return (null, "la BD no tiene modelo de balance de situación (ModelosBalance); actualiza el esquema.");
        var ejercicios = await inicio.EjerciciosAsync(ctx.IdSociedad, cancelacion);
        var actual = ejercicios.FirstOrDefault(e => e.IdSociedadEjercicio == ctx.IdSociedadEjercicio) ?? new EjercicioSociedad(ctx.IdSociedadEjercicio, ctx.Ejercicio);
        var datos = await inicio.DatosAsync(ctx.IdSociedad, actual, cancelacion);
        DatosEjercicio? anterior = ejercicios.FirstOrDefault(e => e.Ejercicio == ctx.Ejercicio - 1) is { } ea ? await inicio.DatosAsync(ctx.IdSociedad, ea, cancelacion) : null;
        var bancos = (await inicio.BancosAsync(ctx.IdSociedad, actual, cancelacion)).Where(b => b.CuentaContable is { Length: > 0 }).Select(b => b.CuentaContable!)
            .ToHashSet(StringComparer.Ordinal);
        var (r, _) = CalculadoraInicio.Ratios(datos, anterior, fin.AddDays(1), false, inversiones, situacion, null, bancos);
        return (r, null);
    }
}
