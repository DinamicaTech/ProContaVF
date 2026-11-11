using System.Globalization;
using ProConta.Compras.Reconocimiento;
using ProConta.Correo;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Facturas;

/// <summary>
/// Factura de compra recibida por correo (compras/reconocimiento 2026-11-10 19:40, derivado de correo/recepcion): el PDF se
/// copia a la carpeta raíz «&lt;ejercicio&gt;\Proveedores», se reconoce y se registra como cualquier PDF de la carpeta (y se mueve
/// a la de su proveedor); si todos los campos alcanzan el 95 % de confianza, tiene proveedor y no pide revisión, se contabiliza
/// con la exportación masiva. Un PDF ya registrado (mismo MD5) no crea otra factura: se continúa con la que hay.
/// </summary>
public sealed class ProcesadorFacturasCompraCorreo(IBaseDatos bd, ServicioFacturasCompra facturas, ContabilizadorCompras contabilizador) : IProcesadorFacturaCompraRecibida
{
    /// <summary>Confianza mínima de cada campo para contabilizar sin revisión (correo/recepcion 2026-11-10 19:40).</summary>
    public const decimal UmbralAutomatico = 0.95m;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    public async Task<ResultadoFacturaRecibida> ProcesarAsync(ContextoSociedadEjercicio ctx, string rutaPdf, string nombre, Actor actor, CancellationToken cancelacion = default)
    {
        var md5 = ServicioReconocimientoFacturaCompra.CalcularMd5(rutaPdf);
        var existente = await bd.EjecutarAsync(s => RepositorioFacturasCompra.BuscarPorMd5Async(s, md5), cancelacion);
        FacturaCompra? factura = existente.IdRegistro > 0 ? await facturas.CargarAsync(existente.IdRegistro, cancelacion) : null;
        if (factura is null)
        {
            var carpeta = await facturas.CarpetaComprasAsync(ctx, cancelacion);
            if (!carpeta.EsCorrecto)
                return new ResultadoFacturaRecibida(EstadoAdjuntoRecibido.Error, null, null, carpeta.Error!.Mensaje + " " + carpeta.Error.AccionCorrectora, []);
            var destino = Path.Combine(carpeta.Valor, NombreLibre(carpeta.Valor, nombre));
            File.Copy(rutaPdf, destino);
            File.SetAttributes(destino, FileAttributes.Normal);
            FacturaCompra leida;
            try
            {
                (_, leida) = await facturas.ProbarAsync(ctx, destino, altaAutomatica: true, cancelacion);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not ExcepcionDatos)
            {
                return new ResultadoFacturaRecibida(EstadoAdjuntoRecibido.Error, null, null,
                    $"No he podido leer el PDF ({ex.Message}). Lo he dejado en {carpeta.Valor} para que lo recoja el escaneo de Facturas recibidas.", []);
            }
            leida.OrigenDocumento = OrigenesDocumento.Email;
            var registrada = await facturas.RegistrarAsync(leida, cancelacion);
            if (registrada.Factura is null)
                return new ResultadoFacturaRecibida(EstadoAdjuntoRecibido.Error, null, null,
                    $"No se ha registrado: {registrada.Error?.Mensaje} {registrada.Error?.AccionCorrectora} El PDF está en {carpeta.Valor}.", []);
            factura = registrada.Factura;
            await facturas.MoverDesdeRaizAsync(factura, cancelacion);
        }

        var campos = Campos(factura);
        var estado = await contabilizador.EstadoAsync(factura.IdRegistro);
        if (estado.IdAsiento is int yaContabilizada)
            return new ResultadoFacturaRecibida(EstadoAdjuntoRecibido.Procesado, factura.IdRegistro, yaContabilizada,
                $"{Rotulo(factura)}: ya estaba registrada y contabilizada (asiento {yaContabilizada}).", campos);

        var motivos = MotivosRevision(factura, campos);
        if (motivos.Count > 0)
            return new ResultadoFacturaRecibida(EstadoAdjuntoRecibido.Revisar, factura.IdRegistro, null,
                $"{Rotulo(factura)}: registrada en Facturas recibidas, sin contabilizar porque {string.Join("; ", motivos)}. Revísala allí y contabilízala.", campos);

        var exportada = await contabilizador.ExportarAsync(factura, actor, new OpcionesExportacion(Masiva: true));
        if (!exportada.EsCorrecto || exportada.Valor.IdAsiento is null)
            return new ResultadoFacturaRecibida(EstadoAdjuntoRecibido.Revisar, factura.IdRegistro, null,
                $"{Rotulo(factura)}: registrada, pero no se ha podido contabilizar: {exportada.Error?.Mensaje ?? exportada.Valor.Aviso} Revísala en Facturas recibidas.", campos);
        return new ResultadoFacturaRecibida(EstadoAdjuntoRecibido.Procesado, factura.IdRegistro, exportada.Valor.IdAsiento,
            $"{Rotulo(factura)}: registrada y contabilizada (asiento {exportada.Valor.IdAsiento}).", campos);
    }

    /// <summary>
    /// Por qué una factura recibida no se contabiliza sola: algún campo por debajo del 95 % (o sin confianza), sin proveedor o
    /// con el reconocimiento pidiendo revisión. Vacío: se contabiliza.
    /// </summary>
    internal static IReadOnlyList<string> MotivosRevision(FacturaCompra factura, IReadOnlyList<CampoReconocido> campos)
    {
        var bajos = campos.Where(c => c.Confianza is not decimal k || k < UmbralAutomatico).Select(c => c.Campo).ToList();
        var motivos = new List<string>();
        if (bajos.Count > 0) motivos.Add($"la confianza está por debajo del 95 % en {string.Join(", ", bajos)}");
        if (factura.IdProveedor is null) motivos.Add("no he identificado al proveedor");
        if (factura.RequiereRevision) motivos.Add("el reconocimiento pide revisarla");
        return motivos;
    }

    /// <summary>Campos reconocidos con su confianza (del log del reconocimiento).</summary>
    internal static IReadOnlyList<CampoReconocido> Campos(FacturaCompra f)
    {
        var c = ConfianzaCampos.DesdeLog(f.Log);
        return
        [
            new("NIF del proveedor", f.NifProveedor ?? "(sin leer)", c.Nif),
            new("Número", string.IsNullOrWhiteSpace(f.NumeroFactura) ? "(sin leer)" : f.NumeroFactura, c.Numero),
            new("Fecha", f.Fecha?.ToString("dd/MM/yyyy", Es) ?? "(sin leer)", c.Fecha),
            new("Importes", f.ImporteTotal is decimal t ? $"total {t.ToString("#,##0.00", Es)} €" + (f.BaseImponible is decimal b ? $", base {b.ToString("#,##0.00", Es)} €" : "") : "(sin leer)", c.Importes),
        ];
    }

    private static string Rotulo(FacturaCompra f) =>
        $"Factura {(string.IsNullOrWhiteSpace(f.NumeroFactura) ? "sin número" : f.NumeroFactura)}{(f.NombreProveedorAeat is { } p ? $" de {p}" : "")}";

    /// <summary>El nombre del fichero si está libre en la carpeta; si no, con « (2)», « (3)»…</summary>
    private static string NombreLibre(string carpeta, string nombre)
    {
        var invalidos = Path.GetInvalidFileNameChars();
        var limpio = new string(nombre.Select(c => invalidos.Contains(c) ? '_' : c).ToArray()).Trim(' ', '.');
        if (limpio.Length == 0) limpio = "factura.pdf";
        if (!limpio.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) limpio += ".pdf";
        var candidato = limpio;
        for (var n = 2; File.Exists(Path.Combine(carpeta, candidato)); n++) candidato = $"{Path.GetFileNameWithoutExtension(limpio)} ({n}).pdf";
        return candidato;
    }
}
