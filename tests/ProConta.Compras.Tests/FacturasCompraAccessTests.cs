using System.Text;
using ProConta.Compras.Facturas;
using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using static ProConta.Compras.Tests.Documentos;

namespace ProConta.Compras.Tests;

/// <summary>
/// Registro de facturas de compra contra una copia de pruebas con datos sintéticos: estructura nueva, duplicados por MD5,
/// inserción atómica de los desgloses, contexto del proveedor desde su carpeta y búsqueda de PDF nuevos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class FacturasCompraAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly string _raiz = BdPruebas.CarpetaTemporal();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly RelojFijo _reloj = new(new DateTime(2026, 3, 10, 10, 0, 0));

    public void Dispose()
    {
        _copia.Dispose();
        try { Directory.Delete(_raiz, true); } catch (IOException) { }
    }

    private IBaseDatos Bd => _copia.Bd();

    private sealed class RelojFijo(DateTime ahora) : IReloj
    {
        public DateTime Ahora => ahora;
    }

    /// <summary>Lee siempre el mismo texto, sea cual sea el PDF.</summary>
    private sealed class ExtractorFijo(string texto) : IExtractorDocumentoFacturaCompra
    {
        public DocumentoFacturaExtraido Extraer(string rutaPdf) => Texto(texto, rutaPdf);
    }

    private static readonly string Completa = $"""
        Emisor Ficticio S.L. NIF: {NifProveedor}
        Número de factura: FV260123
        Fecha de factura: 10/02/2026
        IVA 21% 100,00 21,00
        IVA 10% 50,00 5,00
        Total factura 176,00
        """;

    private ServicioFacturasCompra Servicio(string texto = "") =>
        new(Bd, new ServicioReconocimientoFacturaCompra(new ExtractorFijo(texto.Length == 0 ? Completa : texto)), _reloj);

    private async Task<int> Contar(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)));

    /// <summary>Empresa T01 con su carpeta de documentos, ejercicio 2026 y un proveedor con cuenta 4000001 y su NIF.</summary>
    private async Task<int> EmpresaConProveedorAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", NifSociedad);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ? WHERE IdSociedad = ?", _raiz, "T01"));
        return await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, NIF) VALUES (?, ?, ?)", "Emisor Ficticio S.L.", true, NifProveedor);
            var idTercero = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO Proveedores (IdTercero, Activo) VALUES (?, ?)", idTercero, true);
            var idProveedor = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO IdentidadesContables (IdSociedad, Naturaleza, FechaAlta) VALUES (?, ?, ?)", "T01", (byte)0, DateTime.Today);
            var identidad = await s.UltimoIdAsync();
            await s.EjecutarAsync("INSERT INTO CuentasContables (IdIdentidadContable, IdSociedadEjercicio, Codigo, Nombre, EstaActiva, EstaBloqueada, EsImputable, SaldoInicial, SaldoActual, NumeroApuntes) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                identidad, 9001, "4000001", "Emisor Ficticio S.L.", true, false, true, 0m, 0m, 0);
            await s.EjecutarAsync("INSERT INTO CuentasProveedoresSociedad (IdProveedor, IdSociedad, Clase, IdIdentidadContable) VALUES (?, ?, ?, ?)",
                idProveedor, "T01", "PROVEEDOR", identidad);
            return idProveedor;
        });
    }

    /// <summary>Factura antigua del ERP de la carpeta «Ficticio», con un NIF grabado erróneo y sin IdProveedor.</summary>
    private Task FacturaAntiguaAsync(string numero, DateTime fecha, string md5) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO FacturasCompra (Sociedad, Ejercicio, CuentaContableProveedor, NumeroFactura, Fecha, ImporteTotal, Carpeta, NIF, MD5, Pagada, Exportada, Duplicada, InversionSujetoPasivo) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", "T01", "2026", "4000001", numero, fecha, 10m, "Ficticio", "A00000000", md5, false, false, false, false));

    private string Pdf(string carpeta, string nombre, string contenido)
    {
        var dir = Path.Combine(_raiz, "2026", "Proveedores", carpeta);
        Directory.CreateDirectory(dir);
        var ruta = Path.Combine(dir, nombre);
        File.WriteAllBytes(ruta, Encoding.ASCII.GetBytes("%PDF-1.4 " + contenido));
        return ruta;
    }

    [Fact]
    public async Task La_migracion_crea_la_estructura_nueva()
    {
        var esquema = await Bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        foreach (var c in new[] { "IdSociedadEjercicio", "IdProveedor", "Divisa", "ImporteTotalDivisa", "TipoCambioAplicado", "FechaTipoCambio",
                     "IdRegistroFacturaRectificada", "ConfianzaImportacion", "RequiereRevisionImportacion", "LOGImportacion", "Version" })
            Assert.True(esquema.ExisteColumna("FacturasCompra", c), c);
        Assert.True(esquema.ExisteTabla("DesglosesFacturaCompra"));
        Assert.True(esquema.ExisteTabla("ConfiguracionReconocimientoFacturasCompra"));
    }

    [Fact]
    public async Task Registra_cabecera_y_desgloses_con_proveedor_de_la_carpeta_y_no_duplica_el_mismo_pdf()
    {
        var idProveedor = await EmpresaConProveedorAsync();
        await FacturaAntiguaAsync("FV260098", new DateTime(2026, 1, 10), "11111111111111111111111111111111");
        var ruta = Pdf("Ficticio", "factura.pdf", "uno");
        var servicio = Servicio();

        var r = await servicio.ImportarAsync(_ctx, ruta);
        Assert.True(r.Registrada, r.Error?.ToString());
        var f = await servicio.CargarAsync(r.Factura!.IdRegistro);
        Assert.NotNull(f);
        Assert.Equal((idProveedor, "4000001", NifProveedor), (f!.IdProveedor!.Value, f.CuentaContableProveedor!, f.NifProveedor!));
        Assert.Equal(("FV260123", new DateTime(2026, 2, 10), 176m), (f.NumeroFactura, f.Fecha!.Value, f.ImporteTotal!.Value));
        Assert.Equal([10m, 21m], f.Desgloses.Select(d => d.TipoIva));
        Assert.Equal(f.BaseImponible, f.Desgloses.Sum(d => d.BaseImponible));
        Assert.Equal(f.CuotaIva, f.Desgloses.Sum(d => d.CuotaIva));
        Assert.Equal(ServicioReconocimientoFacturaCompra.CalcularMd5(ruta), f.Md5);
        Assert.Equal(9001, f.Ejercicio.IdSociedadEjercicio);
        Assert.Contains("\"version\":1", f.Log);

        // El mismo PDF con otro nombre no crea otra factura.
        var copia = Pdf("Ficticio", "copia.pdf", "uno");
        var otra = await servicio.ImportarAsync(_ctx, copia);
        Assert.False(otra.Registrada);
        Assert.Equal("Compras.Duplicada", otra.Error!.Codigo);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM FacturasCompra WHERE IdProveedor = ?", idProveedor));
    }

    [Fact]
    public async Task Un_fallo_tras_insertar_no_deja_cabecera_ni_desgloses()
    {
        await EmpresaConProveedorAsync();
        var (_, factura) = await Servicio().ProbarAsync(_ctx, Pdf("Ficticio", "f.pdf", "dos"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bd.EnTransaccionAsync<int>(async s =>
        {
            var id = await RepositorioFacturasCompra.InsertarAsync(s, factura, _reloj.Ahora);
            Assert.True(id.EsCorrecto);
            Assert.Equal(2, Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM DesglosesFacturaCompra WHERE IdRegistro = ?", id.Valor)));
            throw new InvalidOperationException("Corte simulado");
        }));
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM FacturasCompra WHERE MD5 = ?", factura.Md5));
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM DesglosesFacturaCompra"));
    }

    [Fact]
    public async Task El_contexto_usa_el_nif_del_tercero_y_los_numeros_del_historico()
    {
        await EmpresaConProveedorAsync();
        await FacturaAntiguaAsync("FV260045", new DateTime(2026, 1, 5), "22222222222222222222222222222222");
        await FacturaAntiguaAsync("FV260098", new DateTime(2026, 1, 20), "33333333333333333333333333333333");
        var (contexto, proveedor) = await Servicio().CargarContextoAsync(_ctx, Path.Combine(_raiz, "2026", "Proveedores", "Ficticio", "x.pdf"));
        Assert.Equal(NifProveedor, contexto.Reconocimiento.NifsPorAliasProveedor["Ficticio"]);
        Assert.Equal(["FV260098", "FV260045"], contexto.Reconocimiento.NumerosFacturaProveedor);
        Assert.Equal(2, contexto.FacturasAceptadasProveedor);
        Assert.Contains(NifSociedad, contexto.Reconocimiento.NifsSociedades);
        Assert.Equal("4000001", proveedor!.CuentaContable);
    }

    [Fact]
    public async Task Buscar_pdf_nuevos_omite_los_registrados_y_las_copias_identicas()
    {
        await EmpresaConProveedorAsync();
        var registrado = Pdf("Ficticio", "a.pdf", "registrado");
        await FacturaAntiguaAsync("FV1", new DateTime(2026, 1, 5), ServicioReconocimientoFacturaCompra.CalcularMd5(registrado));
        var nuevo = Pdf("Ficticio", "b.pdf", "nuevo");
        Pdf("Otro", "b-copia.pdf", "nuevo");

        var r = await Servicio().BuscarPdfNuevosAsync(_ctx);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal([nuevo], r.Valor.Select(p => p.Ruta));
        Assert.Equal("Ficticio", r.Valor[0].Carpeta);
    }

    [Fact]
    public async Task Sin_carpeta_de_documentos_explica_donde_escribirla()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", NifSociedad);
        var r = await Servicio().BuscarPdfNuevosAsync(_ctx);
        Assert.Equal("Compras.SinCarpetaDocumentos", r.Error!.Codigo);
        Assert.Contains("Empresas", r.Error.AccionCorrectora);
    }
}
