using System.Globalization;
using System.Text;
using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.Facturas;

/// <summary>PDF de la carpeta de compras que aún no está registrado.</summary>
public sealed record PdfPendiente(string Ruta, string Carpeta, string Md5);

/// <summary>Un campo en el modo «Probar»: lo grabado, lo leído ahora y si coinciden. <c>SinGrabar</c>: no había nada grabado.</summary>
public sealed record ComparacionCampo(string Campo, string Grabado, string Leido, bool Igual, bool SinGrabar);

/// <summary>Resultado de importar un PDF: la factura registrada o el motivo por el que no se registró.</summary>
public sealed record ImportacionPdf(string Ruta, FacturaCompra? Factura, Error? Error)
{
    public bool Registrada => Factura is not null;
}

/// <summary>
/// Facturas de compra de una empresa a partir de su carpeta de PDF (compras/reconocimiento 2026-11-05 11:24):
/// <c>&lt;carpeta de documentos de la empresa&gt;\&lt;ejercicio&gt;\Proveedores\&lt;carpeta del proveedor&gt;\*.pdf</c>.
/// Un PDF es nuevo si su MD5 no está en FacturasCompra. Leer el PDF, el OCR y el cambio de divisa se hacen sin conexión
/// abierta (RF-TEC-01); la inserción comprueba otra vez el MD5 en su transacción, así que repetir no duplica.
/// </summary>
public sealed class ServicioFacturasCompra(
    IBaseDatos bd,
    ServicioReconocimientoFacturaCompra reconocimiento,
    IReloj? reloj = null)
{
    private readonly IReloj _reloj = reloj ?? RelojSistema.Instancia;

    /// <summary>Carpeta de compras del ejercicio, o el error que explica por qué no la hay.</summary>
    public async Task<Resultado<string>> CarpetaComprasAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default)
    {
        var (nombre, raiz) = await bd.EjecutarAsync(s => RepositorioFacturasCompra.EmpresaAsync(s, ctx.IdSociedad), cancelacion);
        if (string.IsNullOrWhiteSpace(raiz))
            return Resultado<string>.Fallo(new Error("Compras.SinCarpetaDocumentos",
                $"La empresa {nombre ?? ctx.IdSociedad} no tiene carpeta de documentos, y es donde busco los PDF de compra.", ctx.ToString(), null,
                "Escríbela en Configuración › Parámetros y tablas › Empresas (grupo «Rutas y bases de datos») y vuelve a buscar los PDF."));
        var carpeta = Path.Combine(RaizEmpresa(raiz), ctx.Ejercicio.ToString(CultureInfo.InvariantCulture), "Proveedores");
        if (!Directory.Exists(carpeta))
            return Resultado<string>.Fallo(new Error("Compras.CarpetaNoExiste", $"No encuentro la carpeta de compras. Ruta intentada: {carpeta}.", ctx.ToString(),
                "La carpeta no existe o la unidad compartida no está conectada.", "Comprueba que la carpeta compartida esté conectada o crea la carpeta del ejercicio."));
        return Resultado<string>.Correcto(carpeta);
    }

    /// <summary>
    /// Carpeta de la empresa: Sociedades.RutaDocumentos tal cual si es una ruta completa; si es solo un nombre o una ruta
    /// relativa, dentro de la carpeta de datos, que es la que contiene la BD compartida (compras/reconocimiento 2026-11-05 11:24).
    /// </summary>
    internal string RaizEmpresa(string rutaDocumentos)
    {
        var ruta = rutaDocumentos.Trim();
        return Path.IsPathFullyQualified(ruta) ? ruta : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(bd.Ruta)) ?? string.Empty, ruta));
    }

    /// <summary>PDF de las carpetas de proveedor cuyo MD5 no está registrado, en orden de carpeta y fichero.</summary>
    public async Task<Resultado<IReadOnlyList<PdfPendiente>>> BuscarPdfNuevosAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default)
    {
        var carpeta = await CarpetaComprasAsync(ctx, cancelacion);
        if (!carpeta.EsCorrecto) return Resultado<IReadOnlyList<PdfPendiente>>.Fallo(carpeta.Errores);
        var registrados = await bd.EjecutarAsync(RepositorioFacturasCompra.Md5RegistradosAsync, cancelacion);
        var nuevos = new List<PdfPendiente>();
        var vistos = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ruta in EnumerarPdf(carpeta.Valor))
        {
            cancelacion.ThrowIfCancellationRequested();
            var md5 = ServicioReconocimientoFacturaCompra.CalcularMd5(ruta);
            // Dos copias idénticas en carpetas distintas son un solo PDF: se ofrece la primera.
            if (registrados.Contains(md5) || !vistos.Add(md5)) continue;
            nuevos.Add(new PdfPendiente(ruta, FacturaCompra.CarpetaDe(ruta), md5));
        }
        return Resultado<IReadOnlyList<PdfPendiente>>.Correcto(nuevos);
    }

    /// <summary>Todos los PDF bajo las carpetas de proveedor (cada carpeta de primer nivel es un proveedor).</summary>
    internal static IEnumerable<string> EnumerarPdf(string carpetaProveedores) =>
        Directory.EnumerateDirectories(carpetaProveedores)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.pdf", new EnumerationOptions { RecurseSubdirectories = true, MatchCasing = MatchCasing.CaseInsensitive })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase));

    /// <summary>Contexto del reconocimiento para un PDF: configuración de la empresa, NIF conocidos, proveedor de la carpeta e histórico.</summary>
    public Task<(ContextoProcesamiento Contexto, ProveedorCarpeta? Proveedor)> CargarContextoAsync(
        ContextoSociedadEjercicio ctx, string rutaPdf, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var configuracion = await RepositorioFacturasCompra.ConfiguracionAsync(s, ctx.IdSociedad);
            var proveedores = await RepositorioFacturasCompra.ProveedoresAsync(s, ctx.IdSociedad, ctx.IdSociedadEjercicio);
            var historicas = await RepositorioFacturasCompra.CarpetasHistoricasAsync(s, ctx.IdSociedad);
            var alias = ResolverCarpetas(historicas, proveedores);

            var elegido = ElegirCarpeta(rutaPdf, alias);
            var numeros = (IReadOnlyList<string>)[];
            var completas = 0;
            ProveedorCarpeta? proveedor = null;
            if (elegido is not null)
            {
                var codigos = elegido.Proveedor?.Codigos.ToList() ?? (elegido.Cuenta is { } c ? [c] : []);
                (numeros, completas) = await RepositorioFacturasCompra.HistoricoProveedorAsync(s, ctx.IdSociedad, elegido.Proveedor?.IdProveedor, codigos);
                proveedor = new ProveedorCarpeta(elegido.Proveedor?.IdProveedor, elegido.Proveedor?.CuentaPara(elegido.Cuenta) ?? elegido.Cuenta, Vacio(elegido.Nif), elegido.Carpeta);
            }

            var carpetaPdf = FacturaCompra.CarpetaDe(rutaPdf);
            var contexto = new ContextoProcesamiento
            {
                Configuracion = configuracion,
                FacturasAceptadasProveedor = completas,
                Plantillas = carpetaPdf.Length == 0 ? [] : await RepositorioPlantillasCompra.ListarAsync(s, ctx.IdSociedad, carpetaPdf),
                Reconocimiento = new ContextoReconocimientoFacturaCompra
                {
                    NifsProveedoresConocidos = proveedores.Select(p => p.Nif).Where(n => n.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    NifsSociedades = await RepositorioFacturasCompra.NifsEmpresasAsync(s),
                    NifsPorAliasProveedor = alias.Where(a => !string.IsNullOrEmpty(a.Nif))
                        .GroupBy(a => a.Carpeta, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First().Nif!, StringComparer.OrdinalIgnoreCase),
                    NumerosFacturaProveedor = numeros,
                    SociedadTieneVies = configuracion.SociedadTieneVies
                }
            };
            return (contexto, proveedor);
        }, cancelacion);

    /// <summary>Lee el PDF sin registrar nada (modo «Probar» y paso previo de <see cref="ImportarAsync"/>).</summary>
    public async Task<(FacturaReconocida Reconocida, FacturaCompra Factura)> ProbarAsync(
        ContextoSociedadEjercicio ctx, string rutaPdf, CancellationToken cancelacion = default)
    {
        var (contexto, proveedor) = await CargarContextoAsync(ctx, rutaPdf, cancelacion);
        var reconocida = await reconocimiento.ReconocerAsync(rutaPdf, contexto, cancelacion);
        var factura = FacturaCompra.DesdeReconocimiento(reconocida, ctx, proveedor);
        // Sin proveedor por la carpeta, el del NIF leído si solo hay uno en la empresa (como hace la ficha al guardar).
        if (factura.IdProveedor is null && factura.NifProveedor is { Length: > 0 } nif)
        {
            var conNif = (await ProveedoresAsync(ctx, cancelacion)).Where(p => MismoNif(p.Nif, nif)).Select(p => p.IdProveedor).Distinct().ToList();
            if (conNif.Count == 1) factura.IdProveedor = conNif[0];
        }
        await AsignarImputacionAsync(factura, reconocida.Texto, cancelacion);
        return (reconocida, factura);
    }

    /// <summary>
    /// Cuenta de gasto y centro de coste de una factura leída (compras/pantallas 2026-11-06 17:02): primero los textos clave
    /// de la empresa que aparezcan en el contenido o en la ruta del PDF (gana el más largo; con empate no se asigna y se
    /// avisa); si no, los del proveedor por defecto; el centro, por último, el de la cuenta. Lo que ya tiene la factura no se toca.
    /// </summary>
    internal Task AsignarImputacionAsync(FacturaCompra factura, string? texto, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var ctx = factura.Ejercicio;
            var textos = await CatalogoTextosClaveCompra.ListarAsync(s, ctx);
            var activos = new List<TextoClaveCompra>(textos.Count);
            foreach (var t in textos)
                activos.Add(t.IdCentroCoste is int c && !await RepositorioFacturasCompra.CentroImputableAsync(s, ctx.IdSociedad, c) ? t with { IdCentroCoste = null } : t);
            var porTexto = ReglasTextosClave.Elegir(activos, texto, factura.RutaPdf);
            var (cuentaProveedor, centroProveedor) = await RepositorioFacturasCompra.ImputacionDefectoAsync(s, ctx, factura.IdProveedor);
            if (string.IsNullOrWhiteSpace(factura.CuentaGasto))
                factura.CuentaGasto = porTexto.Cuenta ?? (porTexto.EmpateCuenta ? null : cuentaProveedor);
            if (factura.IdCentroCoste is null)
                factura.IdCentroCoste = porTexto.IdCentroCoste ?? (porTexto.EmpateCentro ? null : centroProveedor
                    ?? (factura.CuentaGasto is { Length: > 0 } cuenta ? await RepositorioFacturasCompra.CentroDeCuentaAsync(s, ctx, cuenta) : null));
            factura.AnadirAvisos(porTexto.Avisos);
            return true;
        }, cancelacion);

    private static bool MismoNif(string a, string b)
    {
        static string Compacto(string t) => new(t.Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        return Compacto(a) == Compacto(b);
    }

    /// <summary>Reconoce un PDF y lo registra con sus desgloses en una sola transacción. Un PDF ya registrado no crea otra factura.</summary>
    public async Task<ImportacionPdf> ImportarAsync(ContextoSociedadEjercicio ctx, string rutaPdf, CancellationToken cancelacion = default)
    {
        FacturaCompra factura;
        try
        {
            (_, factura) = await ProbarAsync(ctx, rutaPdf, cancelacion);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ExcepcionDatos)
        {
            return new ImportacionPdf(rutaPdf, null, new Error("Compras.PdfIlegible", $"No he podido leer el PDF: {ex.Message}", Path.GetFileName(rutaPdf),
                null, "Comprueba que el fichero se abre con un visor de PDF y que la carpeta compartida esté conectada.", ex.GetType().Name));
        }
        return await RegistrarAsync(factura, cancelacion);
    }

    /// <summary>Registra una factura ya leída (también la que el operador ha corregido en la pantalla).</summary>
    public async Task<ImportacionPdf> RegistrarAsync(FacturaCompra factura, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(factura);
        var validacion = factura.Validar();
        if (!validacion.EsCorrecto) return new ImportacionPdf(factura.RutaPdf, null, validacion.Error);
        var ahora = _reloj.Ahora;
        var id = await bd.EnTransaccionAsync(s => RepositorioFacturasCompra.InsertarAsync(s, factura, ahora), cancelacion);
        if (!id.EsCorrecto) return new ImportacionPdf(factura.RutaPdf, null, id.Error);
        factura.IdRegistro = id.Valor;
        factura.FechaHoraRegistro = ahora;
        return new ImportacionPdf(factura.RutaPdf, factura, null);
    }

    /// <summary>Busca los PDF nuevos y los importa uno a uno; un PDF que falla no detiene a los demás.</summary>
    public async Task<Resultado<IReadOnlyList<ImportacionPdf>>> ImportarNuevosAsync(
        ContextoSociedadEjercicio ctx, IProgress<ImportacionPdf>? progreso = null, CancellationToken cancelacion = default)
    {
        var nuevos = await BuscarPdfNuevosAsync(ctx, cancelacion);
        if (!nuevos.EsCorrecto) return Resultado<IReadOnlyList<ImportacionPdf>>.Fallo(nuevos.Errores);
        var resultado = new List<ImportacionPdf>();
        foreach (var pdf in nuevos.Valor)
        {
            cancelacion.ThrowIfCancellationRequested();
            var r = await ImportarAsync(ctx, pdf.Ruta, cancelacion);
            resultado.Add(r);
            progreso?.Report(r);
        }
        return Resultado<IReadOnlyList<ImportacionPdf>>.Correcto(resultado);
    }

    public Task<FacturaCompra?> CargarAsync(int idRegistro, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var f = await RepositorioFacturasCompra.CargarAsync(s, idRegistro);
            if (f is null || f.RutasGuardadas.Count == 0) return f;
            var raiz = await RaizAsync(s, f.Ejercicio.IdSociedad);
            f.RutaPdf = LocalizarPdf(f.RutasGuardadas, raiz);
            return f;
        }, cancelacion);

    /// <summary>Facturas de compra del ejercicio para la lista de la pantalla, con el PDF localizado.</summary>
    public Task<IReadOnlyList<FilaFacturaCompra>> ListarAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(async s =>
        {
            var filas = await RepositorioFacturasCompra.ListarAsync(s, ctx);
            var raiz = await RaizAsync(s, ctx.IdSociedad);
            foreach (var f in filas.Where(f => f.RutasGuardadas.Count > 0)) f.RutaPdf = LocalizarPdf(f.RutasGuardadas, raiz);
            return filas;
        }, cancelacion);

    private async Task<string?> RaizAsync(ISesionDatos s, string idSociedad)
    {
        var (_, raiz) = await RepositorioFacturasCompra.EmpresaAsync(s, idSociedad);
        return string.IsNullOrWhiteSpace(raiz) ? null : RaizEmpresa(raiz);
    }

    /// <summary>
    /// PDF de una factura ya registrada: la primera ruta grabada que exista; si ninguna existe (el ERP guardaba rutas
    /// completas de unidades que han cambiado, p. ej. D:\SynologyBox\2025\Proveedores\…), la misma factura dentro de la
    /// carpeta de la empresa: lo que va desde «&lt;ejercicio&gt;\Proveedores\» se busca en «&lt;raíz&gt;\&lt;ejercicio&gt;\Proveedores\».
    /// No cambia lo grabado. Sin ninguna, la primera ruta grabada (la fila sale como «PDF no encontrado»).
    /// </summary>
    internal static string LocalizarPdf(IReadOnlyList<string> guardadas, string? raiz)
    {
        if (guardadas.Count == 0) return string.Empty;
        if (guardadas.FirstOrDefault(File.Exists) is { } existente) return existente;
        if (raiz is not null)
            foreach (var ruta in guardadas)
                if (Reubicar(ruta, raiz) is { } nueva && File.Exists(nueva)) return nueva;
        return guardadas[0];
    }

    /// <summary>
    /// La ruta grabada dentro de otra raíz: busca el último tramo «&lt;año&gt;\Proveedores» (sin distinguir mayúsculas) y
    /// conserva lo que hay a partir de él. Null si la ruta no sigue esa estructura.
    /// </summary>
    internal static string? Reubicar(string ruta, string raiz)
    {
        var tramos = ruta.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = tramos.Length - 3; i >= 0; i--)
        {
            if (tramos[i].Length == 4 && tramos[i].All(char.IsAsciiDigit) && tramos[i + 1].Equals("Proveedores", StringComparison.OrdinalIgnoreCase))
                return Path.Combine([raiz, tramos[i], "Proveedores", .. tramos[(i + 2)..]]);
        }
        return null;
    }

    /// <summary>Proveedores de la empresa con su tercero, su NIF y su nombre (buscar por NIF y abrir la ficha).</summary>
    public Task<IReadOnlyList<ProveedorFicha>> ProveedoresAsync(ContextoSociedadEjercicio ctx, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync(s => RepositorioFacturasCompra.ProveedoresFichaAsync(s, ctx.IdSociedad), cancelacion);

    /// <summary>
    /// Guarda lo que el operador ha revisado o corregido en una factura sin contabilizar. Con proveedor, número, fecha e
    /// importes que cuadran, deja de pedir revisión. Si otro operador la cambió mientras tanto, no se pisa.
    /// </summary>
    /// <remarks><paramref name="elegidos"/>: dónde eligió el operador cada campo en el visor, para que la plantilla aprenda de ahí.</remarks>
    public async Task<Resultado> GuardarAsync(FacturaCompra factura, IReadOnlyDictionary<string, PuntoDocumento>? elegidos = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(factura);
        if (factura.IdRegistro <= 0) throw new ArgumentException("Solo se guardan facturas ya registradas.", nameof(factura));
        var validacion = factura.Validar();
        if (!validacion.EsCorrecto) return validacion;
        if (factura.Lista) factura.RequiereRevision = false;
        var r = await bd.EnTransaccionAsync(s => RepositorioFacturasCompra.ActualizarAsync(s, factura), cancelacion);
        if (!r.EsCorrecto) return Resultado.Fallo(r.Errores);
        factura.Version = r.Valor;
        await AprenderAsync(factura, elegidos, cancelacion);
        return Resultado.Correcto();
    }

    /// <summary>
    /// Enseña las plantillas con los valores de una factura sin cambiar sus datos: para las contabilizadas, que no se
    /// modifican desde aquí hasta la entrega 3 (compras/pantallas 2026-11-05 20:29).
    /// </summary>
    public Task EnsenarAsync(FacturaCompra factura, IReadOnlyDictionary<string, PuntoDocumento>? elegidos = null, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(factura);
        return AprenderAsync(factura, elegidos, cancelacion);
    }

    /// <summary>
    /// «Separar facturas» (compras/pantallas 2026-11-05 20:29): el PDF tiene dos facturas y la segunda empieza en
    /// <paramref name="paginaSegunda"/>. El original pasa a .BAK, la primera parte se queda con el nombre original y en el
    /// mismo registro (se reconoce de nuevo; se conservan la imputación y las notas) y la segunda se escribe con
    /// <paramref name="nombreSegunda"/> y se registra como factura nueva. No se hace con facturas contabilizadas.
    /// </summary>
    public async Task<Resultado<ImportacionPdf>> SepararAsync(ContextoSociedadEjercicio ctx, FacturaCompra factura, int paginaSegunda, string nombreSegunda, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(factura);
        Resultado<ImportacionPdf> Fallo(string codigo, string mensaje, string? accion = null) =>
            Resultado<ImportacionPdf>.Fallo(new Error(codigo, mensaje, factura.Rotulo, null, accion));
        if (factura.Contabilizada) return Fallo("Compras.SepararContabilizada", "La factura está contabilizada y no se separa desde aquí.", "Deshaz antes su contabilización.");
        var ruta = factura.RutaPdf;
        if (!File.Exists(ruta)) return Fallo("Compras.PdfNoEncontrado", $"No encuentro el PDF. Ruta intentada: {ruta}.");
        var nombre = (nombreSegunda ?? "").Trim();
        if (!nombre.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) nombre += ".pdf";
        if (nombre.Length <= 4 || nombre.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || nombre.Contains('\\') || nombre.Contains('/'))
            return Fallo("Compras.NombreNoValido", $"«{nombre}» no vale como nombre de fichero.", "Escribe solo el nombre, sin carpeta ni caracteres como \\ / : * ? \" < > |.");
        var carpeta = Path.GetDirectoryName(ruta)!;
        var ruta2 = Path.Combine(carpeta, nombre);
        if (File.Exists(ruta2) || string.Equals(ruta2, ruta, StringComparison.OrdinalIgnoreCase))
            return Fallo("Compras.NombreExiste", $"Ya hay un fichero {nombre} en la carpeta.", "Elige otro nombre para la segunda factura.");

        byte[] primera, segunda;
        try
        {
            var bytes = await File.ReadAllBytesAsync(ruta, cancelacion);
            var paginas = Integraciones.DivisorPdf.ContarPaginas(bytes);
            if (paginas < 2) return Fallo("Compras.UnaPagina", "El PDF solo tiene una página: no hay nada que separar.");
            if (paginaSegunda < 2 || paginaSegunda > paginas)
                return Fallo("Compras.PaginaNoValida", $"La segunda factura tiene que empezar entre la página 2 y la {paginas}.");
            (primera, segunda) = Integraciones.DivisorPdf.Dividir(bytes, paginaSegunda);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not ArgumentOutOfRangeException)
        {
            return Fallo("Compras.PdfIlegible", $"No he podido partir el PDF: {ex.Message}");
        }

        var bak = Path.ChangeExtension(ruta, ".BAK");
        for (var n = 2; File.Exists(bak); n++) bak = Path.ChangeExtension(ruta, $".{n}.BAK");
        var temporal = ruta + ".separando";
        try
        {
            await File.WriteAllBytesAsync(temporal, primera, cancelacion);
            await File.WriteAllBytesAsync(ruta2, segunda, cancelacion);
            File.Move(ruta, bak);
            File.Move(temporal, ruta);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Se deja la carpeta como estaba.
            if (!File.Exists(ruta) && File.Exists(bak)) File.Move(bak, ruta);
            if (File.Exists(temporal)) File.Delete(temporal);
            if (File.Exists(ruta2)) File.Delete(ruta2);
            return Fallo("Compras.SepararCarpeta", $"No he podido escribir en la carpeta: {ex.Message}", "Comprueba que el PDF no esté abierto en otro programa y vuelve a intentarlo.");
        }

        // La primera parte sigue en el mismo registro con su nueva lectura; la imputación y las notas se conservan.
        var (_, leida) = await ProbarAsync(ctx, ruta, cancelacion);
        leida.IdRegistro = factura.IdRegistro;
        leida.Version = factura.Version;
        leida.CuentaGasto = factura.CuentaGasto;
        leida.IdCentroCoste = factura.IdCentroCoste;
        leida.CuentaFormaPago = factura.CuentaFormaPago;
        leida.IdFormaPago = factura.IdFormaPago;
        leida.Notas = factura.Notas;
        leida.Operacion = factura.Operacion;
        leida.Producto = factura.Producto;
        // Es una lectura nueva: queda pendiente de revisar aunque la factura se hubiera guardado antes.
        leida.RequiereRevision = true;
        var reemplazo = await bd.EnTransaccionAsync(s => RepositorioFacturasCompra.ReemplazarPdfAsync(s, leida), cancelacion);
        if (!reemplazo.EsCorrecto) return Resultado<ImportacionPdf>.Fallo(reemplazo.Errores);
        return Resultado<ImportacionPdf>.Correcto(await ImportarAsync(ctx, ruta2, cancelacion));
    }

    /// <summary>
    /// Asiento de la factura, si está contabilizada. Las del ERP se encuentran por el apunte del proveedor con su número de
    /// factura y la ruta del PDF; las de ProContaVF, por FacturasCompra.IdAsientoContable.
    /// </summary>
    public Task<AsientoFactura?> AsientoAsync(ContextoSociedadEjercicio ctx, FacturaCompra factura, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(factura);
        var idSe = factura.Ejercicio.IdSociedadEjercicio > 0 ? factura.Ejercicio.IdSociedadEjercicio : ctx.IdSociedadEjercicio;
        if (factura.IdAsientoContable is int propio)
            return bd.EjecutarAsync(s => RepositorioFacturasCompra.AsientoPorIdAsync(s, idSe, propio), cancelacion);
        if (!factura.Contabilizada || string.IsNullOrWhiteSpace(factura.NumeroFactura) || string.IsNullOrWhiteSpace(factura.CuentaContableProveedor))
            return Task.FromResult<AsientoFactura?>(null);
        return bd.EjecutarAsync(s => RepositorioFacturasCompra.AsientoErpAsync(s, idSe, factura.NumeroFactura.Trim(), factura.CuentaContableProveedor.Trim()), cancelacion);
    }

    /// <summary>Máximo de plantillas por proveedor y campo (formatos distintos de factura).</summary>
    internal const int MaxPlantillasPorCampo = 5;

    /// <summary>
    /// Guardar enseña (compras/reconocimiento 2026-11-05 18:42): los valores guardados de NIF, número, fecha y total son los
    /// buenos. Cada plantilla del proveedor que en este PDF lee el valor bueno suma un acierto; la que lee otro se descarta;
    /// si ninguna lo lee, se aprende una nueva. Un fallo al leer el PDF no impide guardar: simplemente no se aprende.
    /// </summary>
    internal async Task AprenderAsync(FacturaCompra factura, IReadOnlyDictionary<string, PuntoDocumento>? elegidos, CancellationToken cancelacion)
    {
        var carpeta = FacturaCompra.CarpetaDe(factura.RutaPdf);
        if (carpeta.Length == 0 || !File.Exists(factura.RutaPdf)) return;
        DocumentoFacturaExtraido documento;
        try { documento = reconocimiento.Extraer(factura.RutaPdf); }
        catch (Exception ex) when (ex is not OperationCanceledException and not ExcepcionDatos) { return; }
        var valores = ValoresGuardados(factura);
        if (valores.Count == 0) return;
        var ahora = _reloj.Ahora;
        await bd.EnTransaccionAsync(async s =>
        {
            var existentes = await RepositorioPlantillasCompra.ListarAsync(s, factura.Ejercicio.IdSociedad, carpeta);
            foreach (var (campo, valor) in valores)
            {
                var delCampo = existentes.Where(p => p.Campo == campo).ToList();
                // Si el operador eligió en el visor dónde está el valor, manda ese sitio: las plantillas que leen de otro se quitan
                // aunque hoy acierten (por ejemplo, una fecha de vencimiento que coincide con la de la factura).
                var elegida = elegidos?.GetValueOrDefault(campo) is { } punto ? PlantillasReconocimiento.Aprender(documento, campo, valor, punto, soloAhi: true) : null;
                var acertada = false;
                foreach (var p in delCampo)
                {
                    var leido = PlantillasReconocimiento.Leer(documento, p);
                    if (leido is null) continue;
                    var vale = PlantillasReconocimiento.Coincide(campo, leido, valor) && (elegida is null || PlantillasReconocimiento.MismoSitio(p, elegida));
                    if (vale) { await RepositorioPlantillasCompra.AcertarAsync(s, p.IdPlantilla, ahora); acertada = true; }
                    else await RepositorioPlantillasCompra.BorrarAsync(s, p.IdPlantilla);
                }
                if (acertada || (elegida ?? PlantillasReconocimiento.Aprender(documento, campo, valor)) is not { } nueva) continue;
                await RepositorioPlantillasCompra.InsertarAsync(s, factura.Ejercicio.IdSociedad, carpeta, nueva, ahora);
                var sobrantes = (await RepositorioPlantillasCompra.ListarAsync(s, factura.Ejercicio.IdSociedad, carpeta))
                    .Where(p => p.Campo == campo).Skip(MaxPlantillasPorCampo).ToList();
                foreach (var p in sobrantes) await RepositorioPlantillasCompra.BorrarAsync(s, p.IdPlantilla);
            }
            return true;
        }, cancelacion);
    }

    /// <summary>Valores guardados que sirven de verdad para las plantillas; en moneda extranjera, el total en esa moneda.</summary>
    internal static IReadOnlyList<(string Campo, string Valor)> ValoresGuardados(FacturaCompra f)
    {
        var valores = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(f.NifProveedor)) valores.Add((PlantillasReconocimiento.Nif, f.NifProveedor));
        if (!string.IsNullOrWhiteSpace(f.NumeroFactura)) valores.Add((PlantillasReconocimiento.Numero, f.NumeroFactura));
        if (f.Fecha is { } fecha) valores.Add((PlantillasReconocimiento.Fecha, fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        var total = f.Divisa != "EUR" ? f.ImporteTotalDivisa ?? f.ImporteTotal : f.ImporteTotal;
        if (total is { } t) valores.Add((PlantillasReconocimiento.Total, t.ToString("0.00", CultureInfo.InvariantCulture)));
        return valores;
    }

    /// <summary>
    /// Modo «Probar»: compara campo a campo lo grabado con lo que el motor lee ahora del mismo PDF. Los números se comparan
    /// limpios (solo letras y dígitos), las fechas por día y los importes al céntimo. No escribe nada.
    /// </summary>
    public static IReadOnlyList<ComparacionCampo> Comparar(FacturaCompra grabada, FacturaCompra leida)
    {
        ArgumentNullException.ThrowIfNull(grabada);
        ArgumentNullException.ThrowIfNull(leida);
        static string Imp(decimal? v) => v is { } x ? x.ToString("N2", CultureInfo.GetCultureInfo("es-ES")) : "";
        static string Fec(DateTime? v) => v is { } x ? x.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) : "";
        static string Num(string? v) => new((v ?? "").Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
        static string Nif(string? v) => MotorReconocimientoFacturaCompra.CompactarNif(v);
        // Al céntimo: el ERP grabó algunos importes con cuatro decimales (14,0154 frente a 14,02 leído).
        static decimal? Cent(decimal? v) => v is { } x ? MotorReconocimientoFacturaCompra.Redondear(x) : null;
        ComparacionCampo C(string campo, string g, string l, bool igual) => new(campo, g, l, igual, g.Length == 0);
        return
        [
            C("NIF", grabada.NifProveedor ?? "", leida.NifProveedor ?? "", Nif(grabada.NifProveedor) == Nif(leida.NifProveedor)),
            C("Número", grabada.NumeroFactura, leida.NumeroFactura, Num(grabada.NumeroFactura) == Num(leida.NumeroFactura)),
            C("Fecha", Fec(grabada.Fecha), Fec(leida.Fecha), grabada.Fecha?.Date == leida.Fecha?.Date),
            C("Base", Imp(grabada.BaseImponible), Imp(leida.BaseImponible), Cent(grabada.BaseImponible) == Cent(leida.BaseImponible)),
            C("IVA", Imp(grabada.CuotaIva), Imp(leida.CuotaIva), Cent(grabada.CuotaIva) == Cent(leida.CuotaIva)),
            C("Total", Imp(grabada.ImporteTotal), Imp(leida.ImporteTotal), Cent(grabada.ImporteTotal) == Cent(leida.ImporteTotal))
        ];
    }

    /// <summary>Carpeta histórica con su cuenta o proveedor y el NIF del catálogo de terceros.</summary>
    internal sealed record AliasCarpeta(string Carpeta, string? Cuenta, ProveedorEmpresa? Proveedor, string? Nif);

    /// <summary>
    /// Une cada carpeta histórica con su proveedor: por IdProveedor si la factura lo tiene, o por el código de cuenta en
    /// cualquier ejercicio de la empresa. El NIF sale del tercero (el NIF grabado en las facturas del ERP no es fiable).
    /// </summary>
    internal static List<AliasCarpeta> ResolverCarpetas(
        IEnumerable<(string Carpeta, string? Cuenta, int? IdProveedor)> historicas, IReadOnlyList<ProveedorEmpresa> proveedores)
    {
        var porId = proveedores.ToDictionary(p => p.IdProveedor);
        var porCodigo = proveedores.SelectMany(p => p.Codigos.Select(c => (c, p)))
            .GroupBy(x => x.c, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.p.IdProveedor).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().p, StringComparer.OrdinalIgnoreCase);
        var lista = new List<AliasCarpeta>();
        foreach (var (carpeta, cuenta, idProveedor) in historicas)
        {
            if (carpeta.Length == 0) continue;
            var p = idProveedor is { } id && porId.TryGetValue(id, out var pi) ? pi
                : cuenta is not null && porCodigo.TryGetValue(cuenta, out var pc) ? pc : null;
            lista.Add(new AliasCarpeta(carpeta, cuenta, p, Vacio(p?.Nif)));
        }
        return lista;
    }

    /// <summary>
    /// Carpeta histórica que corresponde a la ruta, como el ERP: coincidencia exacta (1) o una contenida en la otra (0,85)
    /// con algún tramo de la ruta, sin acentos ni signos; vale desde 0,75 y si no empatan dos proveedores distintos.
    /// </summary>
    internal static AliasCarpeta? ElegirCarpeta(string rutaPdf, IReadOnlyList<AliasCarpeta> alias)
    {
        var partes = rutaPdf.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries).Select(Normalizar).Where(p => p.Length >= 3).ToArray();
        if (partes.Length == 0) return null;
        var candidatos = alias
            .Select(a => (Alias: a, Puntos: partes.Max(p => Puntuar(p, Normalizar(a.Carpeta)))))
            .Where(c => c.Puntos >= 0.75m).OrderByDescending(c => c.Puntos).ToList();
        if (candidatos.Count == 0) return null;
        if (candidatos.Count > 1 && candidatos[0].Puntos == candidatos[1].Puntos && Clave(candidatos[0].Alias) != Clave(candidatos[1].Alias)) return null;
        return candidatos[0].Alias;
    }

    private static string Clave(AliasCarpeta a) => a.Proveedor is { } p ? $"P{p.IdProveedor}" : $"C{a.Cuenta?.ToUpperInvariant()}";

    private static decimal Puntuar(string ruta, string alias)
    {
        if (alias.Length == 0) return 0m;
        if (ruta == alias) return 1m;
        return ruta.Contains(alias, StringComparison.Ordinal) || alias.Contains(ruta, StringComparison.Ordinal) ? 0.85m : 0m;
    }

    private static string Normalizar(string valor)
    {
        var sb = new StringBuilder();
        foreach (var c in valor.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    private static string? Vacio(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
