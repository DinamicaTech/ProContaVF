using System.Globalization;
using ProConta.Datos.Persistencia;
using ProConta.Documentos;
using ProConta.Pruebas.Access;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace ProConta.Documentos.Tests;

/// <summary>
/// Gestor documental contra una copia de pruebas con datos sintéticos y una carpeta de documentos temporal (documentos 2026-11-18). Criterio de
/// aceptación: los ficheros de la maqueta entran en la bandeja con su propuesta, los listos se archivan de una vez en su carpeta, cada documento
/// sale una sola vez aunque tenga varios vínculos, desvincular no borra el fichero y una versión nueva conserva la anterior.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class DocumentosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly string _raiz = BdPruebas.CarpetaTemporal();
    private readonly string _origen = BdPruebas.CarpetaTemporal();

    public void Dispose()
    {
        _copia.Dispose();
        foreach (var c in new[] { _raiz, _origen }) try { Directory.Delete(c, true); } catch (IOException) { }
    }

    private IBaseDatos Bd => _copia.Bd();
    private ServicioDocumentos Servicio => new(Bd, "operador.prueba");
    private async Task<int> ContarAsync(string sql, params object?[] p) => Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync(sql, p)), CultureInfo.InvariantCulture);

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("UPDATE Sociedades SET RutaDocumentos = ? WHERE IdSociedad = 'T01'", _raiz);
            await s.EjecutarAsync("INSERT INTO PresentacionesAeat (IdSociedad, Modelo, Ejercicio, Periodo, Entorno, FechaHora, Csv, Justificante, Importe, Resultado, Fichero) " +
                                  "VALUES ('T01', '303', 2026, '3T', 'PRUEBAS', ?, 'ABCDEFGH12345678', '3036000234567', 0, 0, '')", new DateTime(2026, 10, 20, 10, 0, 0));
            await s.EjecutarAsync("INSERT INTO Terceros (TipoPersona, Nombre, NIF, Activo, FechaAlta, Version) VALUES ('JURIDICA', 'Seguros Atalaya SA', 'A22222222', True, ?, 0)", DateTime.Today);
            return true;
        });
    }

    private string Pdf(string nombre, params string[] lineas)
    {
        var b = new PdfDocumentBuilder();
        var fuente = b.AddStandard14Font(Standard14Font.Helvetica);
        var pagina = b.AddPage(PageSize.A4);
        var y = 780.0;
        foreach (var l in lineas) { pagina.AddText(l, 10, new PdfPoint(40, y), fuente); y -= 16; }
        var ruta = Path.Combine(_origen, nombre);
        File.WriteAllBytes(ruta, b.Build());
        return ruta;
    }

    [Fact]
    public async Task Alta_en_bandeja_archivar_vincular_versiones_y_eliminar()
    {
        await PrepararAsync();
        var justificante = Pdf("Justificante_303_3T_2026.pdf", "Agencia Tributaria", "Justificante de presentacion", "Numero de justificante: 3036000234567");
        var poliza = Pdf("Poliza_seguro_local.pdf", "Poliza de seguro del local", "Asegurador: SEGUROS ATALAYA SA, NIF A22222222", "Fecha de efecto 10/01/2026");
        var imagen = Path.Combine(_origen, "IMG_2201.jpg");
        File.WriteAllBytes(imagen, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);

        // Añadir: los tres entran en la bandeja con su propuesta; repetir el mismo fichero no lo duplica.
        var r = await Servicio.AnadirABandejaAsync("T01", [justificante, poliza, imagen, justificante], moverOriginal: false);
        Assert.True(r.EsCorrecto);
        Assert.Equal(3, r.Valor.Anadidos.Count);
        Assert.Single(r.Valor.Repetidos);
        Assert.Empty(r.Valor.Errores);
        Assert.True(File.Exists(justificante), "Sin «mover», el original se queda donde estaba.");
        var bandeja = await Servicio.ListarAsync("T01", EstadosDocumento.PorClasificar);
        Assert.Equal(3, bandeja.Count);
        var j = bandeja.Single(d => d.Vigente!.NombreOriginal == "Justificante_303_3T_2026.pdf");
        Assert.True(j.Listo);
        Assert.Equal(Confianza.Alta, j.Confianza);
        Assert.Equal("303|2026|3T", Assert.Single(j.Vinculos).Clave);
        Assert.True(File.Exists(j.Vigente!.RutaCompleta));
        Assert.StartsWith(@"Documentos\Por clasificar\", j.Vigente.Ruta);
        var p = bandeja.Single(d => d.Vigente!.NombreOriginal == "Poliza_seguro_local.pdf");
        Assert.True(p.Listo);
        var i = bandeja.Single(d => d.Vigente!.NombreOriginal == "IMG_2201.jpg");
        Assert.False(i.Listo);

        // Volver a añadir uno ya en la bandeja: se avisa y no se duplica.
        var otra = await Servicio.AnadirABandejaAsync("T01", [poliza], moverOriginal: false);
        Assert.Empty(otra.Valor.Anadidos);
        Assert.Contains("Por clasificar", Assert.Single(otra.Valor.Repetidos));

        // Archivar los listos: el de la imagen se queda y dice qué le falta.
        var a = await Servicio.ArchivarAsync(bandeja.Select(d => d.IdDocumento).ToList());
        Assert.True(a.EsCorrecto);
        Assert.Equal(2, a.Valor.Archivados);
        Assert.Contains("falta el tipo, la fecha y a qué se vincula", Assert.Single(a.Valor.NoListos));
        var archivados = await Servicio.ListarAsync("T01", EstadosDocumento.Archivado);
        Assert.Equal(2, archivados.Count);
        var ja = archivados.Single(d => d.IdDocumento == j.IdDocumento);
        Assert.Equal(@"Documentos\2026\Modelos\303 3T 2026 · justificante.pdf", ja.Vigente!.Ruta);
        Assert.True(File.Exists(Path.Combine(_raiz, ja.Vigente.Ruta)));
        Assert.False(File.Exists(j.Vigente.RutaCompleta), "El fichero sale de «Por clasificar».");
        var pa = archivados.Single(d => d.IdDocumento == p.IdDocumento);
        Assert.StartsWith(@"Documentos\Contratos\", pa.Vigente!.Ruta);

        // Completar la imagen, vincularla a dos sitios y archivarla: sale una sola vez.
        Assert.True((await Servicio.ActualizarAsync(i.IdDocumento, "CERTIFICADO", "Certificado al corriente", new DateTime(2026, 10, 2), null)).EsCorrecto);
        var empresa = ServicioDocumentos.Empresa("T01", "Sociedad sintética");
        Assert.True((await Servicio.VincularAsync([i.IdDocumento], [empresa, Vinculo.Modelo("303", 2026, "3T")], reemplazar: false)).EsCorrecto);
        Assert.Equal(1, (await Servicio.ArchivarAsync([i.IdDocumento])).Valor.Archivados);
        var del303 = await Servicio.DeAsync("T01", Vinculo.Modelo("303", 2026, "3T"));
        Assert.Equal(2, del303.Count);
        Assert.Single(del303, d => d.IdDocumento == i.IdDocumento);
        Assert.Single(await Servicio.DeAsync("T01", empresa));

        // Desvincular no borra el fichero.
        var ruta = (await Servicio.LeerAsync(i.IdDocumento))!.Vigente!.RutaCompleta;
        Assert.True((await Servicio.DesvincularAsync(i.IdDocumento, Vinculo.Modelo("303", 2026, "3T"))).EsCorrecto);
        Assert.Single(await Servicio.DeAsync("T01", Vinculo.Modelo("303", 2026, "3T")));
        Assert.True(File.Exists(ruta));

        // Una versión nueva conserva la anterior.
        var firmado = Pdf("firmado.pdf", "Certificado firmado");
        var v = await Servicio.NuevaVersionAsync(i.IdDocumento, firmado, "Versión firmada");
        Assert.True(v.EsCorrecto, v.Error?.TextoOperador());
        Assert.Equal(2, v.Valor.NumeroVersion);
        var versiones = await Servicio.VersionesAsync(i.IdDocumento);
        Assert.Equal([2, 1], versiones.Select(x => x.NumeroVersion));
        Assert.True(File.Exists(versiones[1].RutaCompleta));
        Assert.True(File.Exists(versiones[0].RutaCompleta));
        Assert.Equal(2, (await Servicio.LeerAsync(i.IdDocumento))!.NumeroVersiones);
        Assert.False((await Servicio.NuevaVersionAsync(i.IdDocumento, firmado, null)).EsCorrecto);

        // Eliminar: no se puede con vínculos; sin ellos, sí.
        var e = await Servicio.EliminarAsync([i.IdDocumento]);
        Assert.False(e.EsCorrecto);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Documentos WHERE IdDocumento = ?", i.IdDocumento));
        Assert.True((await Servicio.VincularAsync([i.IdDocumento], [], reemplazar: true)).EsCorrecto);
        Assert.Equal(1, (await Servicio.EliminarAsync([i.IdDocumento])).Valor);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM DocumentosVersiones WHERE IdDocumento = ?", i.IdDocumento));
    }

    [Fact]
    public async Task Soltar_en_una_ficha_vincula_y_archiva_si_esta_completo()
    {
        await PrepararAsync();
        var poliza = Pdf("poliza.pdf", "Poliza de seguro del local", "NIF A22222222", "Fecha 10/01/2026");
        var activo = new Vinculo(TiposEntidad.Activo, 55, null, "Local Calle Mayor 12");
        var r = await Servicio.AnadirABandejaAsync("T01", [poliza], moverOriginal: true, [activo], archivarSiListo: true);
        Assert.True(r.EsCorrecto);
        Assert.False(File.Exists(poliza), "Con «mover», el original va a la papelera.");
        var d = Assert.Single(await Servicio.DeAsync("T01", activo));
        Assert.Equal(EstadosDocumento.Archivado, d.Estado);
        Assert.Equal(2, d.Vinculos.Count);
    }

    [Fact]
    public async Task Productor_guarda_sin_sobrescribir_y_registra_ficheros_existentes()
    {
        await PrepararAsync();
        var factura = new Vinculo(TiposEntidad.FacturaVenta, 10, null, "F2026-0001 · Cliente");
        var sol = new SolicitudDocumento("T01", "FACTURA_VENTA", "F2026-0001 Cliente", @"2026\Clientes", "F2026-0001 Cliente: X.pdf", new DateTime(2026, 1, 5), 2026, "FACTURA_VENTA", [factura]);
        var g1 = await Servicio.GuardarAsync(sol, [1, 2, 3]);
        Assert.True(g1.EsCorrecto, g1.Error?.TextoOperador());
        Assert.Equal(@"2026\Clientes\F2026-0001 Cliente X.pdf", g1.Valor.Ruta);
        // El mismo contenido otra vez: el mismo documento.
        var g2 = await Servicio.GuardarAsync(sol, [1, 2, 3]);
        Assert.True(g2.Valor.YaExistia);
        Assert.Equal(g1.Valor.IdDocumento, g2.Valor.IdDocumento);
        // Otro contenido con el mismo nombre y sin «nueva versión»: otro fichero con « (2)».
        var g3 = await Servicio.GuardarAsync(sol, [4, 5, 6]);
        Assert.Equal(@"2026\Clientes\F2026-0001 Cliente X (2).pdf", g3.Valor.Ruta);
        // Nueva versión del primero.
        var g4 = await Servicio.GuardarAsync(sol with { NuevaVersionDe = g1.Valor.IdDocumento }, [7, 8, 9]);
        Assert.Equal(g1.Valor.IdDocumento, g4.Valor.IdDocumento);
        Assert.Equal(2, g4.Valor.NumeroVersion);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(_raiz, g1.Valor.Ruta)));
        // Registrar un fichero que ya está en su sitio, dos veces: un solo documento.
        var carpeta = Path.Combine(_raiz, "2026", "Proveedores");
        Directory.CreateDirectory(carpeta);
        var pdf = Path.Combine(carpeta, "compra.pdf");
        File.WriteAllBytes(pdf, [9, 9, 9]);
        var compra = new SolicitudDocumento("T01", "FACTURA_COMPRA", "Compra", "", "compra.pdf", null, 2026, "FACTURA_COMPRA", [new Vinculo(TiposEntidad.FacturaCompra, 3, null, "R1")]);
        var r1 = await Servicio.RegistrarAsync(compra, pdf);
        var r2 = await Servicio.RegistrarAsync(compra, pdf);
        Assert.Equal(@"2026\Proveedores\compra.pdf", r1.Valor.Ruta);
        Assert.True(r2.Valor.YaExistia);
        Assert.Equal(r1.Valor.IdDocumento, r2.Valor.IdDocumento);
        Assert.True(File.Exists(pdf));
    }

    [Fact]
    public async Task Sincronizar_sigue_al_fichero_que_mueve_otro_modulo()
    {
        await PrepararAsync();
        var raiz = Path.Combine(_raiz, "2026", "Proveedores");
        Directory.CreateDirectory(raiz);
        var pdf = Path.Combine(raiz, "fac.pdf");
        File.WriteAllBytes(pdf, [1, 1, 1]);
        var clave = new Vinculo(TiposEntidad.FacturaCompra, 7, null, "F1 · Proveedor");
        SolicitudDocumento Sol(int prov) => new("T01", "FACTURA_COMPRA", "Factura F1", "", "fac.pdf", new DateTime(2026, 3, 1), 2026, "FACTURA_COMPRA",
            [clave, new Vinculo(TiposEntidad.Tercero, prov, null, $"Proveedor {prov}")]);
        var r1 = await Servicio.SincronizarAsync(Sol(1), pdf, clave);
        Assert.True(r1.EsCorrecto, r1.Error?.TextoOperador());
        // Compras lo mueve a la carpeta del proveedor y le cambia el proveedor: misma versión con la ruta nueva, y el tercero cambia.
        var movido = Path.Combine(raiz, "Proveedor 2", "fac.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(movido)!);
        File.Move(pdf, movido);
        var r2 = await Servicio.SincronizarAsync(Sol(2), movido, clave);
        Assert.Equal((r1.Valor.IdDocumento, 1), (r2.Valor.IdDocumento, r2.Valor.NumeroVersion));
        var d = Assert.Single(await Servicio.DeAsync("T01", clave));
        Assert.Equal(Path.Combine("2026", "Proveedores", "Proveedor 2", "fac.pdf"), d.Vigente!.Ruta);
        Assert.Equal([2], d.Vinculos.Where(v => v.TipoEntidad == TiposEntidad.Tercero).Select(v => v.IdEntidad!.Value));
        // Otro contenido en el mismo sitio (la factura partida): versión nueva.
        File.WriteAllBytes(movido, [2, 2]);
        var r3 = await Servicio.SincronizarAsync(Sol(2), movido, clave);
        Assert.Equal((r1.Valor.IdDocumento, 2), (r3.Valor.IdDocumento, r3.Valor.NumeroVersion));
    }

    [Fact]
    public async Task Sin_carpeta_de_documentos_lo_dice()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T02", "Sin carpeta");
        var r = await Servicio.AnadirABandejaAsync("T02", [Pdf("a.pdf", "x")], false);
        Assert.False(r.EsCorrecto);
        Assert.Contains("Carpeta de documentos", r.Error!.TextoOperador());
    }

    [Fact]
    public async Task Buscador_de_destinos()
    {
        await PrepararAsync();
        var r = await Servicio.BuscarDestinosAsync("T01", "303 3t 2026");
        var m = Assert.Single(r);
        Assert.Contains("Presentado", m.Detalle);
        var antiguo = Assert.Single(await Servicio.BuscarDestinosAsync("T01", "303 4t 2024"));
        Assert.Contains("Sin presentación", antiguo.Detalle);
        Assert.Contains(await Servicio.BuscarDestinosAsync("T01", "atalaya"), d => d.Vinculo.TipoEntidad == TiposEntidad.Tercero);
        Assert.Contains(await Servicio.BuscarDestinosAsync("T01", "2026"), d => d.Vinculo.TipoEntidad == TiposEntidad.Ejercicio);
        Assert.Contains(await Servicio.BuscarDestinosAsync("T01", ""), d => d.Vinculo.TipoEntidad == TiposEntidad.Empresa);
    }
}
