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

    /// <summary>Texto con geometría distinto para cada PDF (por el nombre del fichero).</summary>
    private sealed class ExtractorPorFichero(IReadOnlyDictionary<string, string> textos) : IExtractorDocumentoFacturaCompra
    {
        public DocumentoFacturaExtraido Extraer(string rutaPdf) => Maquetado(textos[Path.GetFileName(rutaPdf)], rutaPdf);
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
                     "IdRegistroFacturaRectificada", "ConfianzaImportacion", "RequiereRevisionImportacion", "LOGImportacion", "Version", "IdCentroCoste" })
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
    public async Task Una_carpeta_de_empresa_relativa_esta_dentro_de_la_carpeta_de_la_bd()
    {
        await EmpresaConProveedorAsync();
        Directory.CreateDirectory(Path.Combine(_raiz, "2026", "Proveedores"));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ? WHERE IdSociedad = ?", Path.GetFileName(_raiz), "T01"));
        var r = await Servicio().CarpetaComprasAsync(_ctx);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal(Path.Combine(_raiz, "2026", "Proveedores"), r.Valor);
    }

    [Fact]
    public async Task Sin_carpeta_de_documentos_explica_donde_escribirla()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", NifSociedad);
        var r = await Servicio().BuscarPdfNuevosAsync(_ctx);
        Assert.Equal("Compras.SinCarpetaDocumentos", r.Error!.Codigo);
        Assert.Contains("Empresas", r.Error.AccionCorrectora);
    }

    [Fact]
    public async Task Guardar_aprende_la_plantilla_y_la_siguiente_factura_del_proveedor_sale_al_100()
    {
        await EmpresaConProveedorAsync();
        string Factura(string numero, string fecha, string vence) => $"""
            Emisor Ficticio S.L. NIF: {NifProveedor}
            Número de factura: {numero}
            Emitida el {fecha}   Vence el {vence}
            IVA 21% 100,00 21,00
            Total factura 121,00
            """;
        // La fecha de emisión no lleva rótulo de «fecha», así que el motor duda; el operador la confirma al guardar.
        var servicio = new ServicioFacturasCompra(Bd, new ServicioReconocimientoFacturaCompra(new ExtractorPorFichero(new Dictionary<string, string>
        {
            ["a.pdf"] = Factura("FV260001", "10/02/2026", "10/03/2026"),
            ["b.pdf"] = Factura("FV260002", "12/03/2026", "11/04/2026")
        })), _reloj);
        var a = await servicio.ImportarAsync(_ctx, Pdf("Ficticio", "a.pdf", "a"));
        Assert.True(a.Registrada, a.Error?.ToString());
        var f = (await servicio.CargarAsync(a.Factura!.IdRegistro))!;
        f.Fecha = new DateTime(2026, 2, 10);
        Assert.True((await servicio.GuardarAsync(f)).EsCorrecto);
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM PlantillasReconocimientoCompra WHERE IdSociedad = ? AND Carpeta = ? AND Campo = ? AND Ancla = ?",
            "T01", "Ficticio", "fecha", "emitida el"));

        var (reconocida, leida) = await servicio.ProbarAsync(_ctx, Pdf("Ficticio", "b.pdf", "b"));
        Assert.Equal((new DateTime(2026, 3, 12), 1m), (leida.Fecha!.Value, reconocida.Lectura.ConfianzaFecha));
        Assert.Contains("fecha", reconocida.Lectura.CamposPlantilla);

        // Volver a guardar la misma factura confirma la plantilla (un acierto más), no crea otra.
        Assert.True((await servicio.GuardarAsync(f)).EsCorrecto);
        Assert.Equal(2, await Contar("SELECT Aciertos FROM PlantillasReconocimientoCompra WHERE Campo = ? AND Ancla = ?", "fecha", "emitida el"));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM PlantillasReconocimientoCompra WHERE Campo = ?", "fecha"));

        // Si el operador corrige la fecha a la de vencimiento, la plantilla anterior se descarta y se aprende la nueva.
        f.Fecha = new DateTime(2026, 3, 10);
        Assert.True((await servicio.GuardarAsync(f)).EsCorrecto);
        Assert.Equal(0, await Contar("SELECT COUNT(*) FROM PlantillasReconocimientoCompra WHERE Campo = ? AND Ancla = ?", "fecha", "emitida el"));
        Assert.Equal(1, await Contar("SELECT COUNT(*) FROM PlantillasReconocimientoCompra WHERE Campo = ? AND Ancla = ?", "fecha", "vence el"));
    }

    [Fact]
    public async Task Guardar_cambia_los_datos_y_los_desgloses_y_no_pisa_los_cambios_de_otro_operador()
    {
        await EmpresaConProveedorAsync();
        var servicio = Servicio();
        var r = await servicio.ImportarAsync(_ctx, Pdf("Ficticio", "g.pdf", "guardar"));
        Assert.True(r.Registrada, r.Error?.ToString());
        var f = (await servicio.CargarAsync(r.Factura!.IdRegistro))!;
        var otro = (await servicio.CargarAsync(f.IdRegistro))!;

        f.NumeroFactura = "FV260124";
        f.Operacion = OperacionCompra.Intracomunitaria;
        f.Producto = ProductoCompra.Bienes;
        f.CuentaGasto = "6000000";
        f.Suplido = 4m;
        f.ImporteTotal = 180m;
        f.Notas = "Revisada";
        f.Desgloses.Clear();
        f.Desgloses.Add(new DesgloseFacturaCompra(1, 21m, 150m, 26m));
        var g = await servicio.GuardarAsync(f);
        Assert.True(g.EsCorrecto, g.Error?.ToString());
        Assert.Equal(1, f.Version);

        var leida = (await servicio.CargarAsync(f.IdRegistro))!;
        Assert.Equal(("FV260124", OperacionCompra.Intracomunitaria, ProductoCompra.Bienes, "6000000", 4m, "Revisada"),
            (leida.NumeroFactura, leida.Operacion, leida.Producto!.Value, leida.CuentaGasto!, leida.Suplido!.Value, leida.Notas!));
        Assert.Equal([(21m, 150m, 26m)], leida.Desgloses.Select(d => (d.TipoIva, d.BaseImponible, d.CuotaIva)));
        Assert.Equal(1, leida.Version);
        Assert.False(leida.RequiereRevision);

        // Otro operador la tenía abierta con la versión anterior: no pisa lo guardado.
        otro.NumeroFactura = "PISADO";
        var conflicto = await servicio.GuardarAsync(otro);
        Assert.Equal("Compras.Concurrencia", conflicto.Error!.Codigo);
        Assert.Equal("FV260124", (await servicio.CargarAsync(f.IdRegistro))!.NumeroFactura);
    }

    [Fact]
    public async Task Una_factura_contabilizada_no_se_guarda_desde_la_pantalla()
    {
        await EmpresaConProveedorAsync();
        var servicio = Servicio();
        var r = await servicio.ImportarAsync(_ctx, Pdf("Ficticio", "c.pdf", "contabilizada"));
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE FacturasCompra SET Exportada = ? WHERE IdRegistro = ?", true, r.Factura!.IdRegistro));
        var f = (await servicio.CargarAsync(r.Factura!.IdRegistro))!;
        Assert.True(f.Contabilizada);
        f.Notas = "No debería";
        Assert.Equal("Compras.Contabilizada", (await servicio.GuardarAsync(f)).Error!.Codigo);
    }

    [Fact]
    public async Task La_lista_incluye_las_nuevas_y_las_del_erp_del_ejercicio_con_el_nombre_del_proveedor()
    {
        await EmpresaConProveedorAsync();
        await FacturaAntiguaAsync("FV260098", new DateTime(2026, 1, 10), "44444444444444444444444444444444");
        var servicio = Servicio();
        var r = await servicio.ImportarAsync(_ctx, Pdf("Ficticio", "l.pdf", "lista"));
        var filas = await servicio.ListarAsync(_ctx);
        Assert.Equal(2, filas.Count);
        var nueva = filas.Single(x => x.IdRegistro == r.Factura!.IdRegistro);
        Assert.Equal(("Emisor Ficticio S.L.", "FV260123", 176m), (nueva.Proveedor, nueva.NumeroFactura, nueva.ImporteTotal!.Value));
        Assert.True(nueva.Lista);
        Assert.Contains(filas, x => x.NumeroFactura == "FV260098");
        Assert.Empty(await servicio.ListarAsync(new ContextoSociedadEjercicio("T01", 9002, 2025)));
    }
    [Fact]
    public async Task Separar_deja_la_primera_en_el_mismo_registro_registra_la_segunda_y_guarda_el_original_como_bak()
    {
        await EmpresaConProveedorAsync();
        var dos = Completa.Replace("FV260123", "FV260200").Replace("176,00", "50,00");
        var servicio = new ServicioFacturasCompra(Bd, new ServicioReconocimientoFacturaCompra(new ExtractorPorFichero(new Dictionary<string, string>
        {
            ["doble.pdf"] = Completa,
            ["doble_2.pdf"] = dos,
        })), _reloj);
        var dir = Path.Combine(_raiz, "2026", "Proveedores", "Ficticio");
        Directory.CreateDirectory(dir);
        var ruta = Path.Combine(dir, "doble.pdf");
        File.WriteAllBytes(ruta, PantallaComprasTests.PdfDePaginas("Factura uno", "Factura dos"));
        var r = await servicio.ImportarAsync(_ctx, ruta);
        Assert.True(r.Registrada, r.Error?.ToString());
        var f = (await servicio.CargarAsync(r.Factura!.IdRegistro))!;
        f.Notas = "Se conserva";
        Assert.True((await servicio.GuardarAsync(f)).EsCorrecto);
        f = (await servicio.CargarAsync(f.IdRegistro))!;
        var md5Original = f.Md5;

        Assert.Equal("Compras.NombreExiste", (await servicio.SepararAsync(_ctx, f, 2, "doble")).Error!.Codigo);
        Assert.Equal("Compras.PaginaNoValida", (await servicio.SepararAsync(_ctx, f, 3, "doble_2")).Error!.Codigo);
        var s = await servicio.SepararAsync(_ctx, f, 2, "doble_2");
        Assert.True(s.EsCorrecto, s.Error?.ToString());
        Assert.True(s.Valor!.Registrada, s.Valor.Error?.ToString());

        Assert.True(File.Exists(Path.Combine(dir, "doble.BAK")));
        Assert.False(File.Exists(ruta + ".separando"));
        Assert.Equal(1, Integraciones.DivisorPdf.ContarPaginas(File.ReadAllBytes(ruta)));
        var primera = (await servicio.CargarAsync(f.IdRegistro))!;
        Assert.NotEqual(md5Original, primera.Md5);
        Assert.Equal(ServicioReconocimientoFacturaCompra.CalcularMd5(ruta), primera.Md5);
        Assert.Equal(("FV260123", "Se conserva"), (primera.NumeroFactura, primera.Notas!));
        Assert.True(primera.RequiereRevision);
        var segunda = (await servicio.CargarAsync(s.Valor.Factura!.IdRegistro))!;
        Assert.Equal(("FV260200", 50m), (segunda.NumeroFactura, segunda.ImporteTotal!.Value));

        await Bd.EjecutarAsync(x => x.EjecutarAsync("UPDATE FacturasCompra SET Exportada = ? WHERE IdRegistro = ?", true, segunda.IdRegistro));
        segunda = (await servicio.CargarAsync(segunda.IdRegistro))!;
        Assert.Equal("Compras.SepararContabilizada", (await servicio.SepararAsync(_ctx, segunda, 2, "otra")).Error!.Codigo);
    }
    [Fact]
    public async Task El_asiento_de_una_factura_del_erp_trae_sus_propios_apuntes_con_el_nombre_de_la_cuenta()
    {
        await EmpresaConProveedorAsync();
        await FacturaAntiguaAsync("FV260098", new DateTime(2026, 1, 10), "55555555555555555555555555555555");
        await Bd.EjecutarAsync(async s =>
        {
            const string asiento = "INSERT INTO Asientos (IdSociedadEjercicio, FechaContable, Descripcion, TipoAsiento, EstadoAsiento, OperadorCreacion, FechaHoraCreacion, TotalDebe, TotalHaber, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";
            const string apunte = "INSERT INTO Apuntes (IdAsiento, Orden, CuentaContable, ImporteDebe, ImporteHaber, TipoApunte, GeneradoAutomaticamente, NumeroDocumento, Documento) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)";
            // Otro asiento antes, para que el de la factura no tenga el mismo número que nada más.
            await s.EjecutarAsync(asiento, 9001, new DateTime(2026, 1, 5), "Venta", (byte)1, (byte)1, "prueba", DateTime.Now, 500m, 500m, 1);
            var otro = await s.UltimoIdAsync();
            await s.EjecutarAsync(apunte, otro, (short)1, "4300001", 500m, null, (byte)0, false, "V1", null);
            await s.EjecutarAsync(apunte, otro, (short)2, "7000001", null, 500m, (byte)0, false, "V1", null);
            await s.EjecutarAsync(asiento, 9001, new DateTime(2026, 1, 10), "Emisor Ficticio S.L.", (byte)1, (byte)1, "prueba", DateTime.Now, 10m, 10m, 1);
            var propio = await s.UltimoIdAsync();
            await s.EjecutarAsync(apunte, propio, (short)1, "4000001", null, 10m, (byte)0, false, "FV260098", @"X:\2026\Proveedores\Ficticio\f.pdf");
            await s.EjecutarAsync(apunte, propio, (short)2, "6000000", 10m, null, (byte)0, false, "FV260098", null);
            return true;
        });
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE FacturasCompra SET Exportada = ? WHERE NumeroFactura = ?", true, "FV260098"));
        var servicio = Servicio();
        var fila = (await servicio.ListarAsync(_ctx)).Single(x => x.NumeroFactura == "FV260098");
        var f = (await servicio.CargarAsync(fila.IdRegistro))!;

        var a = await servicio.AsientoAsync(_ctx, f);
        Assert.NotNull(a);
        Assert.Equal("Emisor Ficticio S.L.", a!.Descripcion);
        Assert.Equal([("4000001", "Emisor Ficticio S.L.", 0m, 10m), ("6000000", null, 10m, 0m)],
            a.Apuntes.Select(x => (x.Cuenta, x.NombreCuenta, x.Debe, x.Haber)));
    }
}
