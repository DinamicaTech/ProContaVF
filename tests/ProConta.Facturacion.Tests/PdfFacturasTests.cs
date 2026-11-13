using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Facturacion.Documentos;
using ProConta.Facturacion.Facturas;
using ProConta.Informes;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.Sociedades;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Facturacion.Tests;

/// <summary>Modelo y ruta del PDF, y plantillas reales de FastReport, sin BD (facturacion/factura-venta 2026-11-05).</summary>
public sealed class PdfFacturasTests
{
    private static FacturaVenta Factura()
    {
        var f = new FacturaVenta { Descripcion = "Alquiler del local", TipoRetencion = 19m, FechaFactura = new DateTime(2026, 8, 3), NumeroFactura = "F2026/0007", FechaInicioValidez = new DateTime(2026, 8, 1), FechaFinValidez = new DateTime(2026, 8, 31) };
        var renta = new LineaFacturaVenta { Nombre = "Renta", Descripcion = "Local 18", PrecioUnitario = 1000m, CuentaContable = "7520001" };
        renta.Recalcular();
        f.AnadirLinea(renta);
        var otra = new LineaFacturaVenta { Nombre = "Limpieza", PrecioUnitario = 100m, TipoIva = 10m, DescuentoPorcentaje = 10m };
        otra.Recalcular();
        f.AnadirLinea(otra);
        var suplido = new LineaFacturaVenta { Nombre = "IBI", PrecioUnitario = 50m, EsSuplido = true };
        suplido.Recalcular();
        f.AnadirLinea(suplido);
        return f;
    }

    private static InstantaneaCliente Cliente => new("B87654323", "Inquilina", "Inquilina Sintética SL", "ES", null, "Calle Mayor 1", "08001", "Barcelona", null, null);
    private static DomicilioEmisor Emisor => new("Calle Sintética 1", "08002", "Barcelona", "Barcelona");

    [Fact]
    public void ElModeloLlevaLosImportesDeLaFacturaYUnaFilaPorTipoDeIva()
    {
        var f = Factura();
        var (d, lineas, resumen) = ConstructorPdfFactura.Construir(f, Cliente, "Sociedad sintética SL", "B12345674", Emisor, "Transferencia");
        Invariantes.Totales(f.Totales);
        Assert.Equal("FACTURA", d.Titulo);
        Assert.Equal("", d.Aviso);
        Assert.Equal("Número: F2026/0007", d.Numero);
        Assert.Equal("Inquilina Sintética SL", d.ClienteNombre);
        Assert.Contains("Calle Sintética 1", d.EmisorDomicilio);
        Assert.Contains("08002 Barcelona", d.EmisorDomicilio);
        Assert.Equal("Periodo: 01/08/2026 – 31/08/2026", d.Periodo);
        Assert.Equal("1.151,90 €", d.Total.Replace(' ', ' '));
        Assert.Equal(3, lineas.Count);
        Assert.Equal("Supl.", lineas[2].Iva);
        Assert.Equal("10 %", lineas[1].Descuento);
        Assert.Equal(["Base imponible", "IVA 21 % sobre 1.000,00 €", "IVA 10 % sobre 90,00 €", "Suplidos", "Retención 19 %"], resumen.Select(r => r.Etiqueta.Replace(' ', ' ')));
        Assert.Equal("-207,10 €", resumen[^1].Importe.Replace(' ', ' '));
    }

    [Fact]
    public void UnaLineaDelErpMuestraSuImporteSinElFalsoDescuento()
    {
        var f = new FacturaVenta { Descripcion = "Alquiler", NumeroFactura = "83193", FechaFactura = new DateTime(2026, 8, 3) };
        var l = new LineaFacturaVenta { Nombre = "Alquiler", Cantidad = 1m, PrecioUnitario = 1212.42m, DescuentoImporte = -1212.42m, TipoIva = 21m };
        l.CargarImportes(1002m, 210.42m, 0m, 1212.42m);
        f.Lineas.Add(l);
        var (_, lineas, _) = ConstructorPdfFactura.Construir(f, Cliente, "E", "N", Emisor, null);
        var linea = Assert.Single(lineas);
        Assert.Equal("", linea.Descuento);
        Assert.Equal("1.002,00", linea.Precio);
        Assert.Equal("1.002,00", linea.Base);
        Assert.Equal("1.212,42", linea.Total);
    }

    [Fact]
    public void UnBorradorEsProforma()
    {
        var f = new FacturaVenta { Descripcion = "Prueba" };
        var (d, _, _) = ConstructorPdfFactura.Construir(f, Cliente, "E", "N", Emisor, null);
        Assert.Equal("FACTURA PROFORMA", d.Titulo);
        Assert.Contains("sin validez fiscal", d.Aviso);
        Assert.StartsWith("Sin número", d.Numero);
    }

    [Theory]
    [InlineData(2026, "F2026/0007", "Inquilina: Sintética? SL.", @"2026\FacturasVenta\F2026 0007-Inquilina Sintética SL.pdf")]
    [InlineData(2025, "083240", "José Luis", @"2025\FacturasVenta\083240-José Luis.pdf")]
    public void LaRutaSigueElPatronYNoLlevaCaracteresInvalidos(int ejercicio, string numero, string razon, string esperada) =>
        Assert.Equal(esperada, ConstructorPdfFactura.RutaRelativa(ejercicio, numero, razon));

    [Fact]
    public void UnaRazonSocialLargaNoHaceLaRutaInvalida() =>
        Assert.True(ConstructorPdfFactura.RutaRelativa(2026, "1", new string('x', 400)).Length < 200);

    [Theory]
    [InlineData(PlantillaFactura.Factura)]
    [InlineData(PlantillaFactura.Proforma)]
    public void LasPlantillasGeneranUnPdf(PlantillaFactura plantilla)
    {
        var (d, l, r) = ConstructorPdfFactura.Construir(Factura(), Cliente, "Sociedad sintética SL", "B12345674", Emisor, "Transferencia");
        var pdf = new GeneradorPdfFastReport().Generar(plantilla, d, l, r);
        Assert.True(pdf.Length > 1000);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var salida = Environment.GetEnvironmentVariable("PROCONTA_PDF_PRUEBA");
        if (!string.IsNullOrEmpty(salida)) File.WriteAllBytes(Path.Combine(salida, plantilla + ".pdf"), pdf);
    }
}

/// <summary>Validar sin VeriFactu y PDF contra una copia de pruebas con datos sintéticos.</summary>
[Trait("Categoria", "Access")]
public sealed class PdfFacturasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly RelojFijo _reloj = new(new DateTime(2026, 3, 10, 10, 0, 0));
    private readonly string _raiz = BdPruebas.CarpetaTemporal();
    private int _idCliente;
    private int _idTercero;

    public void Dispose()
    {
        _copia.Dispose();
        try { Directory.Delete(_raiz, true); } catch (IOException) { /* se limpia con la carpeta de pruebas */ }
    }

    private IBaseDatos Bd => _copia.Bd();
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private ServicioRoles Roles => new(Bd, Cuentas);

    private ServicioFacturas Facturas()
    {
        var sociedades = new ServicioSociedadesAccess(Bd);
        return new ServicioFacturas(Bd, sociedades, new RepositorioTercerosAccess(Bd, _reloj), Roles, Cuentas,
            new ContabilizadorVentas(Bd, new MotorAsientosAccess(Bd, reloj: _reloj), Cuentas, Roles, sociedades, _reloj), reloj: _reloj);
    }

    private ServicioPdfFacturas Pdf() => new(Bd, Facturas(), new ServicioSociedadesAccess(Bd), new GeneradorPdfFastReport(), reloj: _reloj);

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética SL");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET DireccionFiscal = ?, CodigoPostalFiscal = ?, PoblacionFiscal = ?, RutaDocumentos = ? WHERE IdSociedad = ?", "Calle Sintética 1", "08001", "Barcelona", _raiz, "T01");
            await s.EjecutarAsync("UPDATE SociedadesEjercicios SET Nombre = ? WHERE IdSociedadEjercicio = ?", "Sociedad sintética SL (2026)", 9001);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = ?", "F1")) == 0)
                await s.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES (?, ?, ?)", "F1", "Factura", false);
            foreach (var (id, nombre) in new[] { ((byte)0, "Pendiente de validar"), ((byte)1, "Validada"), ((byte)2, "Rectificada"), ((byte)3, "Borrador"), ((byte)4, "Rectificación pendiente") })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM EstadosFactura WHERE IdEstado = ?", id)) == 0)
                    await s.EjecutarAsync("INSERT INTO EstadosFactura (IdEstado, Nombre, EsModificable, EsTerminal) VALUES (?, ?, ?, ?)", id, nombre, id == 3, id == 2);
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)", 9001, "Facturas 2026", 1, 99999, "F2026", false, true);
            return 0;
        });
        foreach (var (codigo, nombre) in new[] { ("4300001", "Cliente sintético"), ("7520001", "Ingresos"), ("4770021", "IVA repercutido 21") })
            Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        var t = await new RepositorioTercerosAccess(Bd, _reloj).GuardarAsync(new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Inquilina Sintética SL", Nif = "B87654323", Direccion = "Calle Mayor 1", CodigoPostal = "08001", Poblacion = "Barcelona", Pais = "724" }, _actor);
        Assert.True(t.EsCorrecto, t.Error?.ToString());
        _idTercero = t.Valor.IdTercero;
        _idCliente = (await Roles.AsegurarClienteAsync(_idTercero)).Valor;
        Assert.True((await Roles.AsignarCuentaAsync(ClaseCuentaRol.Cliente, _idCliente, "T01", (await Cuentas.ResolverAsync(_ctx, "4300001")).Valor.IdIdentidadContable)).EsCorrecto);
    }

    private async Task<FacturaVenta> BorradorAsync(ServicioFacturas servicio)
    {
        var f = (await servicio.NuevoBorradorAsync(_ctx)).Valor;
        f.IdCliente = _idCliente;
        f.Descripcion = "Alquiler";
        var l = new LineaFacturaVenta { Nombre = "Renta", PrecioUnitario = 1000m, CuentaContable = "7520001" };
        l.Recalcular();
        f.AnadirLinea(l);
        var r = await servicio.GuardarBorradorAsync(f, _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        return r.Valor;
    }

    [Fact]
    public async Task SinVeriFactuValidaCongelaElDomicilioYElPdfSeArchivaUnaVez()
    {
        await PrepararAsync();
        var servicio = Facturas();
        Assert.False(await servicio.VeriFactuActivoAsync());
        var b = await BorradorAsync(servicio);

        var proforma = await Pdf().ObtenerAsync(b.IdFactura, _actor);
        Assert.True(proforma.EsCorrecto, proforma.Error?.ToString());
        Assert.True(proforma.Valor.EsProforma);
        Assert.False(Directory.Exists(Path.Combine(_raiz, "2026")));

        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(v.Validada, string.Join(" | ", v.Incidencias.Select(i => i.Mensaje)));
        Assert.True(v.Contabilizada);
        Assert.True(v.Factura!.SinVeriFactu);
        Assert.Equal(EstadoFactura.Validada, v.Factura.Estado);
        Assert.Equal("Calle Sintética 1", v.Factura.DireccionEmisor);
        Assert.Equal("F20260001", v.Factura.NumeroFactura);

        var pdf = await Pdf().ObtenerAsync(b.IdFactura, _actor);
        Assert.True(pdf.EsCorrecto, pdf.Error?.ToString());
        Assert.True(pdf.Valor.Generado);
        Assert.Equal(Path.Combine(_raiz, "2026", "FacturasVenta", "F20260001-Inquilina Sintética SL.pdf"), pdf.Valor.Ruta);
        Assert.True(File.Exists(pdf.Valor.Ruta));
        Assert.Equal(Path.Combine("2026", "FacturasVenta", "F20260001-Inquilina Sintética SL.pdf"),
            await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT PDFFactura FROM Facturas WHERE IdFactura = ?", b.IdFactura)));

        // Abrirlo otra vez no lo regenera; regenerar no cambia número, versión ni importes.
        var version = (await servicio.ObtenerAsync(b.IdFactura))!.Version;
        Assert.False((await Pdf().ObtenerAsync(b.IdFactura, _actor)).Valor.Generado);
        Assert.True((await Pdf().ObtenerAsync(b.IdFactura, _actor, regenerar: true)).Valor.Generado);
        var despues = (await servicio.ObtenerAsync(b.IdFactura))!;
        Assert.Equal("F20260001", despues.NumeroFactura);
        Assert.Equal(version, despues.Version);
        Assert.Contains(despues.LineasHistorial, h => h.Contains("PDF regenerado"));
        await Invariantes.EnBaseDeDatosAsync(Bd, servicio);
    }

    [Fact]
    public async Task ConVeriFactuActivadoOSinDomicilioNoSeValidaNiSeNumera()
    {
        await PrepararAsync();
        var servicio = Facturas();
        var b = await BorradorAsync(servicio);

        await Bd.EjecutarAsync(async s =>
        {
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM Configuracion")) == 0)
                return await s.EjecutarAsync("INSERT INTO Configuracion (IdConfiguracion, VeriFactuActivo) VALUES (?, ?)", (byte)1, true);
            return await s.EjecutarAsync("UPDATE Configuracion SET VeriFactuActivo = ?", true);
        });
        var activo = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.False(activo.Validada);
        Assert.Contains(activo.Incidencias, i => i.Codigo == "Facturas.VeriFactuNoDisponible");
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Configuracion SET VeriFactuActivo = ?", false));

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET DireccionFiscal = NULL WHERE IdSociedad = ?", "T01"));
        var sinDomicilio = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.False(sinDomicilio.Validada);
        Assert.Contains(sinDomicilio.Incidencias, i => i.Codigo == "Facturas.EmisorSinDomicilio");
        Assert.Null((await servicio.ObtenerAsync(b.IdFactura))!.NumeroFactura);
    }

    [Fact]
    public async Task ElPdfUsaLosDatosCongeladosYExplicaSiFaltaLaCarpeta()
    {
        await PrepararAsync();
        var servicio = Facturas();
        var b = await BorradorAsync(servicio);
        var v = await servicio.ValidarAsync(b.IdFactura, b.Version, _actor);
        Assert.True(v.Validada);

        // Cambiar después la ficha del cliente no cambia el nombre del fichero: sale de la instantánea.
        var t = (await new RepositorioTercerosAccess(Bd, _reloj).ObtenerAsync(_idTercero))!;
        t.RazonSocial = "Otro Nombre SL";
        Assert.True((await new RepositorioTercerosAccess(Bd, _reloj).GuardarAsync(t, _actor)).EsCorrecto);
        var pdf = await Pdf().ObtenerAsync(b.IdFactura, _actor);
        Assert.Contains("Inquilina Sintética SL", pdf.Valor.Ruta);

        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = NULL WHERE IdSociedad = ?", "T01"));
        var sinCarpeta = await Pdf().ObtenerAsync(b.IdFactura, _actor, regenerar: true);
        Assert.False(sinCarpeta.EsCorrecto);
        Assert.Equal("Facturas.SinCarpetaDocumentos", sinCarpeta.Error!.Codigo);
    }
}
