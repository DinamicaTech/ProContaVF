using ProConta.Compras.Facturas;
using ProConta.Compras.Mantenimiento;
using ProConta.Compras.Reconocimiento;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Mantenimiento;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Seguridad;
using ProConta.Pruebas.Access;

namespace ProConta.Compras.Tests;

/// <summary>
/// Claves de AWS cifradas en Integraciones › «AWS · Conexión» (plataforma/mantenimiento/conexion-aws) y su uso desde el reconocimiento
/// de compras, contra una copia de pruebas.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ReconocimientoCompraAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoMantenimiento _c = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("Pruebas"));

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task El_secreto_se_guarda_cifrado_no_vuelve_a_la_pantalla_y_se_conserva_si_se_deja_vacio()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        var aws = new ProveedorConexionAws(bd);
        Assert.Equal(("Integraciones", "AWS · Conexión"), (aws.Categoria, aws.Titulo));
        Assert.Contains(aws.Campos, c => c.Clave == "ClaveAcceso" && c.Explicacion.Count > 0);
        var r = Assert.Single(await aws.ListarAsync(_c));
        Assert.True(r.EsNuevo);
        r["Region"] = "eu-west-1";
        r["ClaveAcceso"] = "AKIAPRUEBA";
        Assert.Contains(aws.Validar(r), e => e.Campo == "Secreto");
        r["Secreto"] = "secreto-de-prueba";
        Assert.Empty(aws.Validar(r));
        var guardado = (await aws.GuardarAsync(r, _c)).Valor!;
        Assert.Equal((null, "Sí"), (guardado["Secreto"], guardado["EstadoSecreto"]));
        var enBd = await bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT SecretoProtegido FROM ConexionesAws WHERE IdSociedad = ?", f => f.Texto("SecretoProtegido"), "T01"));
        Assert.DoesNotContain("secreto-de-prueba", enBd);

        // Volver a guardar con la clave secreta vacía la conserva.
        Assert.Empty(aws.Validar(guardado));
        await aws.GuardarAsync(guardado, _c);

        // El reconocimiento usa las claves de Integraciones y lo dice en «Claves de AWS».
        var rec = new ProveedorReconocimientoCompra(bd);
        var config = Assert.Single(await rec.ListarAsync(_c));
        Assert.DoesNotContain(rec.Campos, c => c.Clave is "RegionAWS" or "ClaveAccesoAWS" or "SecretoAWS");
        Assert.StartsWith("Guardadas (AKIAPRUEBA, eu-west-1)", config.Texto("ConexionAws"));
        config["UsarTextract"] = true;
        await rec.GuardarAsync(config, _c);
        var reconocimiento = await bd.EjecutarAsync(s => RepositorioFacturasCompra.ConfiguracionAsync(s, "T01"));
        Assert.Equal(("eu-west-1", "AKIAPRUEBA", "secreto-de-prueba"),
            (reconocimiento.CredencialesOcr!.Region, reconocimiento.CredencialesOcr.ClaveAcceso, reconocimiento.CredencialesOcr.Secreto));
        Assert.True(reconocimiento.UsarOcr);

        // «Probar la conexión» y «Probar el OCR» prueban lo mismo: las claves guardadas.
        CredencialesOcr? probadas = null;
        Task<(bool, string)> Probar(CredencialesOcr? c, CancellationToken _) { probadas = c; return Task.FromResult((true, "funciona")); }
        Assert.Equal("funciona", (await new ProveedorConexionAws(bd, Probar).EjecutarAsync("probar", guardado, _c)).Valor!.Mensaje);
        Assert.Equal("secreto-de-prueba", probadas!.Secreto);
        probadas = null;
        Assert.Equal("funciona", (await new ProveedorReconocimientoCompra(bd, Probar).EjecutarAsync("probarclaves", config, _c)).Valor!.Mensaje);
        Assert.Equal("AKIAPRUEBA", probadas!.ClaveAcceso);

        await aws.EjecutarAsync("quitarclaves", guardado, _c);
        reconocimiento = await bd.EjecutarAsync(s => RepositorioFacturasCompra.ConfiguracionAsync(s, "T01"));
        Assert.Null(reconocimiento.CredencialesOcr);
        Assert.Equal("No", Assert.Single(await aws.ListarAsync(_c))["EstadoSecreto"]);
        Assert.StartsWith("No hay", Assert.Single(await rec.ListarAsync(_c)).Texto("ConexionAws"));
    }

    [Fact]
    public async Task La_migracion_lleva_las_claves_guardadas_a_ConexionesAws_y_quita_las_columnas_antiguas()
    {
        var bd = _copia.Bd();
        await DatosSinteticos.SociedadAsync(bd, "T01", "Sociedad sintética");
        await DatosSinteticos.SociedadAsync(bd, "T02", "Otra sociedad sintética");
        const string tabla = "ConfiguracionReconocimientoFacturasCompra";
        var secreto = SecretoProtegido.Proteger("secreto-de-prueba");
        // Una BD de antes de la migración: las claves en la configuración del reconocimiento (T02 sin claves).
        await bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync($"ALTER TABLE {tabla} ADD COLUMN RegionAWS TEXT(30)");
            await s.EjecutarAsync($"ALTER TABLE {tabla} ADD COLUMN ClaveAccesoAWS TEXT(128)");
            await s.EjecutarAsync($"ALTER TABLE {tabla} ADD COLUMN SecretoAWSProtegido LONGTEXT");
            await s.EjecutarAsync($"INSERT INTO {tabla} (IdSociedad, Activo, SociedadTieneVies, UsarTextract, UmbralAceptacion, UmbralDiscrepanciaImporte, Version, " +
                "RegionAWS, ClaveAccesoAWS, SecretoAWSProtegido) VALUES ('T01', True, False, True, 0.9, 0.02, 1, 'eu-west-1', 'AKIAPRUEBA', ?)", secreto);
            await s.EjecutarAsync($"INSERT INTO {tabla} (IdSociedad, Activo, SociedadTieneVies, UsarTextract, UmbralAceptacion, UmbralDiscrepanciaImporte, Version) " +
                "VALUES ('T02', True, False, False, 0.9, 0.02, 1)");
            return true;
        });

        foreach (var paso in MigracionesPlataforma.ConexionAws.Pasos)
            await bd.EjecutarAsync(async s =>
            {
                if (!await paso.YaAplicadoAsync(s, s.LeerEsquema())) await paso.AplicarAsync(s, ContextoMigracion.Vacio);
                return true;
            });

        var esquema = await bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        Assert.False(esquema.ExisteColumna(tabla, "RegionAWS") || esquema.ExisteColumna(tabla, "ClaveAccesoAWS") || esquema.ExisteColumna(tabla, "SecretoAWSProtegido"));
        Assert.Equal(1, await bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM ConexionesAws"))));
        var config = await bd.EjecutarAsync(s => RepositorioFacturasCompra.ConfiguracionAsync(s, "T01"));
        Assert.Equal(("eu-west-1", "AKIAPRUEBA", "secreto-de-prueba", true),
            (config.CredencialesOcr!.Region, config.CredencialesOcr.ClaveAcceso, config.CredencialesOcr.Secreto, config.UsarOcr));
        Assert.Null((await bd.EjecutarAsync(s => RepositorioFacturasCompra.ConfiguracionAsync(s, "T02"))).CredencialesOcr);
        foreach (var paso in MigracionesPlataforma.ConexionAws.Pasos)
            Assert.True(await bd.EjecutarAsync(s => paso.YaAplicadoAsync(s, s.LeerEsquema())));
    }
}
