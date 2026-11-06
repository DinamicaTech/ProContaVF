using ProConta.Compras.Integraciones;
using ProConta.Compras.Proveedores;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Compras.Tests;

/// <summary>Respuestas de VNifV2 (sin llamar a la AEAT).</summary>
public sealed class ConsultaNifAeatTests
{
    private const string Identificado = """
        <env:Envelope xmlns:env="http://schemas.xmlsoap.org/soap/envelope/"><env:Body>
        <VNifV2Sal:VNifV2Sal xmlns:VNifV2Sal="http://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/burt/jdit/ws/VNifV2Sal.xsd">
        <VNifV2Sal:Contribuyente><VNifV2Sal:Nif>B00000000</VNifV2Sal:Nif><VNifV2Sal:Nombre>EMISOR FICTICIO SL</VNifV2Sal:Nombre><VNifV2Sal:Resultado>IDENTIFICADO</VNifV2Sal:Resultado></VNifV2Sal:Contribuyente>
        </VNifV2Sal:VNifV2Sal></env:Body></env:Envelope>
        """;

    [Fact]
    public void Identificado_devuelve_la_razon_social()
    {
        var r = ConsultaNifAeatVNif.Interpretar(Identificado, 200);
        Assert.Equal((true, true, "EMISOR FICTICIO SL"), (r.Consultado, r.Identificado, r.RazonSocial));
    }

    [Fact]
    public void No_identificado_no_confirma_y_explica()
    {
        var r = ConsultaNifAeatVNif.Interpretar(Identificado.Replace(">IDENTIFICADO<", ">NO IDENTIFICADO<"), 200);
        Assert.False(r.Identificado);
        Assert.Null(r.RazonSocial);
        Assert.Contains("NO IDENTIFICADO", r.Motivo);
    }

    [Fact]
    public void Un_fallo_soap_o_una_respuesta_rara_queda_sin_consultar()
    {
        var fallo = """<s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/"><s:Body><s:Fault><faultcode>s:Client</faultcode><faultstring>Certificado no válido</faultstring></s:Fault></s:Body></s:Envelope>""";
        Assert.False(ConsultaNifAeatVNif.Interpretar(fallo, 500).Consultado);
        Assert.False(ConsultaNifAeatVNif.Interpretar("<html>", 502).Consultado);
    }

    [Fact]
    public void La_peticion_lleva_el_nif_y_el_nombre_o_el_nif_si_no_hay_nombre()
    {
        var p = ConsultaNifAeatVNif.Peticion("B00000000", null);
        Assert.Contains("<vnif:Nif>B00000000</vnif:Nif>", p);
        Assert.Contains("<vnif:Nombre>B00000000</vnif:Nombre>", p);
        Assert.Contains("VNifV2Ent", p);
    }
}

/// <summary>Alta de proveedor desde la factura contra una copia de pruebas con datos sintéticos.</summary>
[Trait("Categoria", "Access")]
public sealed class AltaProveedorAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private sealed class AeatFija(ResultadoNifAeat r) : IConsultaNifAeat
    {
        public Task<ResultadoNifAeat> ConsultarAsync(string idSociedad, string nif, string? nombre, CancellationToken cancelacion = default) => Task.FromResult(r);
    }

    private ServicioAltaProveedorCompra Servicio(IConsultaNifAeat? aeat = null)
    {
        var cuentas = new CatalogoCuentasAccess(Bd);
        return new ServicioAltaProveedorCompra(Bd, new RepositorioTercerosAccess(Bd), new ServicioRoles(Bd, cuentas), cuentas, aeat, new Actor("Pruebas"));
    }

    private async Task EmpresaAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", Documentos.NifSociedad);
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
    }

    [Fact]
    public async Task Crea_el_tercero_proveedor_con_la_siguiente_410_y_un_segundo_alta_del_mismo_nif_no_lo_duplica()
    {
        await EmpresaAsync();
        var cuentas = new CatalogoCuentasAccess(Bd);
        Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta("410", "Acreedores", EsImputable: false))).EsCorrecto);
        var servicio = Servicio();

        var r = await servicio.AltaAsync(_ctx, new DatosAltaProveedor(Documentos.NifProveedor, "Emisor Ficticio S.L.", Email: "ficticio@example.com"));
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        Assert.Equal("4100001", r.Valor.Cuenta);
        Assert.False(r.Valor.TerceroExistente);
        var tercero = (await new RepositorioTercerosAccess(Bd).ObtenerAsync(r.Valor.IdTercero))!;
        Assert.Equal((TipoPersona.Juridica, "Emisor Ficticio S.L.", "ficticio@example.com"), (tercero.TipoPersona!.Value, tercero.RazonSocial!, tercero.Email!));
        var roles = await new ServicioRoles(Bd, cuentas).CuentasAsync(r.Valor.IdTercero, _ctx);
        Assert.Equal("4100001", roles.Acreedor!.Codigo);
        Assert.Null(roles.Proveedor);

        var otra = await servicio.AltaAsync(_ctx, new DatosAltaProveedor(Documentos.NifProveedor, "Otro nombre"));
        Assert.True(otra.EsCorrecto, otra.Error?.ToString());
        Assert.Equal((r.Valor.IdProveedor, "4100001", true), (otra.Valor.IdProveedor, otra.Valor.Cuenta, otra.Valor.TerceroExistente));
        Assert.Equal(1, Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM Terceros"))));
    }

    [Fact]
    public async Task Sin_razon_social_o_con_un_nif_no_valido_no_crea_nada()
    {
        await EmpresaAsync();
        var servicio = Servicio();
        Assert.Equal("AltaProveedor.SinRazonSocial", (await servicio.AltaAsync(_ctx, new DatosAltaProveedor(Documentos.NifProveedor, " "))).Error!.Codigo);
        Assert.False((await servicio.AltaAsync(_ctx, new DatosAltaProveedor("B1234", "Nombre"))).EsCorrecto);
        Assert.Equal(0, Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM Terceros"))));
    }

    [Fact]
    public async Task Sin_servicio_de_la_aeat_o_con_un_nif_extranjero_no_se_consulta()
    {
        await EmpresaAsync();
        Assert.False((await Servicio().ConsultarAsync(_ctx, Documentos.NifProveedor, null)).Consultado);
        var aeat = Servicio(new AeatFija(new ResultadoNifAeat(true, true, "EMISOR FICTICIO SL", null)));
        Assert.True((await aeat.ConsultarAsync(_ctx, Documentos.NifProveedor, null)).Identificado);
        Assert.False((await aeat.ConsultarAsync(_ctx, "FR12345678901", null)).Consultado);
    }
}
