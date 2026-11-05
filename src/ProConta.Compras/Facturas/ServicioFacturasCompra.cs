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

            var contexto = new ContextoProcesamiento
            {
                Configuracion = configuracion,
                FacturasAceptadasProveedor = completas,
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
        return (reconocida, FacturaCompra.DesdeReconocimiento(reconocida, ctx, proveedor));
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
    public async Task<Resultado> GuardarAsync(FacturaCompra factura, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(factura);
        if (factura.IdRegistro <= 0) throw new ArgumentException("Solo se guardan facturas ya registradas.", nameof(factura));
        var validacion = factura.Validar();
        if (!validacion.EsCorrecto) return validacion;
        if (factura.Lista) factura.RequiereRevision = false;
        var r = await bd.EnTransaccionAsync(s => RepositorioFacturasCompra.ActualizarAsync(s, factura), cancelacion);
        if (!r.EsCorrecto) return Resultado.Fallo(r.Errores);
        factura.Version = r.Valor;
        return Resultado.Correcto();
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
        ComparacionCampo C(string campo, string g, string l, bool igual) => new(campo, g, l, igual, g.Length == 0);
        return
        [
            C("NIF", grabada.NifProveedor ?? "", leida.NifProveedor ?? "", Nif(grabada.NifProveedor) == Nif(leida.NifProveedor)),
            C("Número", grabada.NumeroFactura, leida.NumeroFactura, Num(grabada.NumeroFactura) == Num(leida.NumeroFactura)),
            C("Fecha", Fec(grabada.Fecha), Fec(leida.Fecha), grabada.Fecha?.Date == leida.Fecha?.Date),
            C("Base", Imp(grabada.BaseImponible), Imp(leida.BaseImponible), grabada.BaseImponible == leida.BaseImponible),
            C("IVA", Imp(grabada.CuotaIva), Imp(leida.CuotaIva), grabada.CuotaIva == leida.CuotaIva),
            C("Total", Imp(grabada.ImporteTotal), Imp(leida.ImporteTotal), grabada.ImporteTotal == leida.ImporteTotal)
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
