using System.Text;
using ProConta.Facturacion.Facturas;
using ProConta.Implantacion.ERP;
using ProConta.Terceros.Contactos;

namespace ProConta.Implantacion.Tests;

/// <summary>Reglas de la migración del ERP (implantacion/migracion-erp), sin BD y con datos inventados.</summary>
public sealed class ReglasErpTests
{
    private static LineaVentaErp Linea(decimal cantidad, decimal precio, decimal iva = 21m, bool cabecera = false, int num = 1) =>
        new(num, 1, "Servicio de prueba", cantidad, precio, iva, null, cabecera, false, 0m, 0m, null);

    [Fact]
    public void El_total_es_la_suma_de_cantidad_por_precio_sin_las_lineas_de_cabecera()
    {
        var lineas = new[] { Linea(2m, 121m), Linea(0m, 0m, cabecera: true), Linea(1m, 60.5m), Linea(5m, 999m, cabecera: true) };
        Assert.Equal(302.5m, ReglasErp.TotalLineas(lineas));
    }

    [Fact]
    public void El_precio_lleva_iva_y_la_linea_saca_base_y_cuota_conservando_el_total()
    {
        var t = ReglasErp.Transformar(Linea(1m, 907.5m), new ConceptoLineaErp("7050099", "399"), 17);
        var l = t.Linea!;
        Assert.Null(t.TipoRetencion);
        Assert.Equal(750m, l.BaseImponible.Valor);
        Assert.Equal(157.5m, l.CuotaIva.Valor);
        Assert.Equal(907.5m, l.ImporteTotal.Valor);
        Assert.Equal(l.ImporteTotal.Valor, l.BaseImponible.Valor + l.CuotaIva.Valor);
        Assert.Equal("7050099", l.CuentaContable);
        Assert.Equal(17, l.IdCentroCoste);
    }

    [Fact]
    public void Una_linea_exenta_lleva_la_causa_de_exencion()
    {
        var l = ReglasErp.Transformar(Linea(1m, 800m, iva: 0m), null, null).Linea!;
        Assert.Equal("E1", l.CausaExencion);
        Assert.Equal(800m, l.BaseImponible.Valor);
        Assert.Equal(0m, l.CuotaIva.Valor);
    }

    [Fact]
    public void La_linea_de_retencion_pasa_al_tipo_de_retencion_y_el_total_de_la_factura_no_cambia()
    {
        var alquiler = Linea(1m, 907.5m);
        var retencion = Linea(0.19m, -750m, iva: 0m, num: 2);
        var t = ReglasErp.Transformar(retencion, new ConceptoLineaErp("4735099", null), null);
        Assert.Null(t.Linea);
        Assert.Equal(19m, t.TipoRetencion);
        Assert.Equal(142.5m, t.ImporteRetencion);
        Assert.Equal("4735099", t.CuentaRetencion);

        var f = new FacturaVenta { TipoRetencion = t.TipoRetencion!.Value };
        f.AnadirLinea(ReglasErp.Transformar(alquiler, new ConceptoLineaErp("7050099", "399"), null).Linea!);
        var totales = f.Totales;
        // Invariante: bases + cuotas − retención = total, y el total es el del ERP (Σ Cantidad × Precio).
        Assert.Equal(totales.BaseImponible + totales.Iva - totales.Retencion, totales.ImporteTotal);
        Assert.Equal(ReglasErp.TotalLineas([alquiler, retencion]), totales.ImporteTotal.Valor);
    }

    [Fact]
    public void La_linea_de_cabecera_solo_conserva_la_descripcion()
    {
        var l = ReglasErp.Transformar(Linea(3m, 50m, cabecera: true), new ConceptoLineaErp("7050099", "399"), 17).Linea!;
        Assert.Equal(0m, l.ImporteTotal.Valor);
        Assert.Null(l.CuentaContable);
        Assert.Null(l.IdCentroCoste);
        Assert.Equal("Servicio de prueba", l.Descripcion);
    }

    [Theory]
    [InlineData("318", "3/318")]
    [InlineData("105", "1/105")]
    [InlineData("3", "3")]
    [InlineData("0", null)]
    [InlineData("", null)]
    [InlineData("X1", null)]
    public void El_departamento_del_erp_es_el_ultimo_tramo_del_centro_nuevo(string departamento, string? clave) =>
        Assert.Equal(clave, ReglasErp.ClaveCentro(departamento));

    [Fact]
    public void Departamento_y_subdepartamento_dan_el_centro_del_subdepartamento()
    {
        Assert.Equal("3/318", ReglasErp.ClaveCentro(3, 318));
        Assert.Equal("1", ReglasErp.ClaveCentro(1, 0));
        Assert.Null(ReglasErp.ClaveCentro(null, null));
    }

    [Theory]
    [InlineData(4728, 124, 2, "Alquiler piso turístico")]
    [InlineData(4731, 124, 2, "Alquiler mensual piso")]
    [InlineData(4730, 124, 6, "Alquiler mensual local")]
    [InlineData(4724, 125, 2, "Retención IRPF")]
    [InlineData(4757, 126, 6, "Residuos comerciales")]
    [InlineData(412, 55, 6, null)]
    public void Los_conceptos_que_solo_difieren_por_departamento_se_agrupan(int id, int tipo, int iva, string? grupo) =>
        Assert.Equal(grupo, ReglasErp.Agrupacion(id, tipo, iva));

    [Fact]
    public void Conceptos_obsoletos_e_inmuebles_guardados_como_personas_no_se_migran()
    {
        Assert.Contains(413, ReglasErp.ConceptosObsoletos);
        Assert.Contains(4752, ReglasErp.ConceptosObsoletos);
        Assert.True(ReglasErp.EsInmueblePersona(100));
        Assert.True(ReglasErp.EsInmueblePersona(125));
        Assert.False(ReglasErp.EsInmueblePersona(99));
        Assert.False(ReglasErp.EsInmueblePersona(4300001));
    }

    [Fact]
    public void Los_tipos_de_contacto_del_erp()
    {
        Assert.Equal(TipoDatoContacto.Email, ReglasErp.TipoContacto(5, null).Tipo);
        Assert.Equal("Móvil", ReglasErp.TipoContacto(4, null).Etiqueta);
        Assert.Equal((TipoDatoContacto.Telefono, "Fijo"), ReglasErp.TipoContacto(2, null));
        Assert.Equal("Oficina", ReglasErp.TipoContacto(null, "Oficina").Etiqueta);
    }

    [Theory]
    [InlineData(@"D:\SynologyBox\2025\Proveedores\A\f.pdf", @"D:\SynologyBox", @"2025\Proveedores\A\f.pdf")]
    [InlineData(@"d:\synologybox\2025\x.pdf", @"D:\SynologyBox\", @"2025\x.pdf")]
    [InlineData(@"\\servidor\oficina\x.pdf", @"D:\SynologyBox", null)]
    [InlineData(@"D:\SynologyBoxOtra\x.pdf", @"D:\SynologyBox", null)]
    public void La_ruta_del_pdf_queda_relativa_a_la_raiz(string ruta, string raiz, string? relativa) =>
        Assert.Equal(relativa, ReglasErp.RutaRelativa(ruta, raiz));

    [Fact]
    public void Retencion_de_compras_y_norma43()
    {
        Assert.Equal((byte)1, ReglasErp.ClaseRetencionCompra(1));
        Assert.Equal((byte)2, ReglasErp.ClaseRetencionCompra(5));
        Assert.Null(ReglasErp.ClaseRetencionCompra(0));
        Assert.Equal(1012.85m, ReglasErp.ImporteN43("00000000101285"));
        Assert.Equal(new DateTime(2026, 9, 4), ReglasErp.FechaN43("260904"));
        Assert.Null(ReglasErp.FechaN43("xx"));
    }

    [Fact]
    public void Los_campos_constantes_solo_cuentan_en_tablas_de_mas_de_tres_filas()
    {
        Fila F(int a, string b) => Fila.De(("A", a), ("B", b));
        Assert.Empty(ReglasErp.CamposConstantes([F(1, "x"), F(2, "x"), F(3, "x")]));
        Assert.Equal(["B"], ReglasErp.CamposConstantes([F(1, "x"), F(2, "x"), F(3, "x"), F(4, "x")]));
    }

    [Fact]
    public void El_reparto_por_actividad_conserva_base_y_cuota()
    {
        var r = ReglasErp.Repartir(1000m, 210m, [("4778021", 333.33m), ("4776021", 333.33m), ("4778021", 333.34m)]);
        Assert.Equal(2, r.Count);
        Assert.Equal(1000m, r.Sum(x => x.Base));
        Assert.Equal(210m, r.Sum(x => x.Cuota));
        Assert.Single(ReglasErp.Repartir(500m, 105m, [("4770021", 1m)]));
        Assert.Empty(ReglasErp.Repartir(500m, 105m, []));
    }

    [Fact]
    public void Las_direcciones_se_leen_con_su_cabecera_en_windows_1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var csv = "IdPersona;Direccion;CP;Poblaciones.Nombre;Provincias.Nombre;Paises.Nombre\r\n7;CALLE INVENTADA, 1;08000;POBLACIÓN;PROVINCIA;ESPAÑA\r\nx;sin id;;;;\r\n";
        var r = LectorErp.InterpretarDirecciones(Encoding.GetEncoding(1252).GetBytes(csv), "prueba.csv");
        Assert.True(r.EsCorrecto);
        var d = Assert.Single(r.Valor);
        Assert.Equal(7, d.IdPersona);
        Assert.Equal("POBLACIÓN", d.Poblacion);
        Assert.Equal("ESPAÑA", d.Pais);
        Assert.False(LectorErp.InterpretarDirecciones(Encoding.UTF8.GetBytes("Otra;Cabecera\r\n1;2"), "x.csv").EsCorrecto);
    }

    [Fact]
    public void La_fila_lee_numeros_y_fechas_de_forma_tolerante()
    {
        var f = Fila.De(("N", "0042"), ("D", 1.5), ("F", new DateTime(1899, 12, 30)), ("G", new DateTime(2026, 1, 2, 10, 0, 0)), ("V", "  "), ("B", true));
        Assert.Equal(42, f.E("N"));
        Assert.Equal(1.5m, f.D("D"));
        Assert.Null(f.F("F"));
        Assert.Equal(new DateTime(2026, 1, 2), f.F("G"));
        Assert.Null(f.T("V"));
        Assert.True(f.B("B"));
        Assert.Null(f.E("NoExiste"));
    }

    [Fact]
    public void El_iban_de_datos_bancarios_se_compone_con_el_pais_y_el_ccc()
    {
        // Cuentas inventadas con dígitos de control calculados.
        Assert.Equal("ES8700491500050012345678", ReglasErp.IbanDatosBancarios("ES87", "0049", "1500", "05", "0012345678"));
        Assert.Equal("ES8700491500050012345678", ReglasErp.IbanDatosBancarios("ES87", "49", "1500", "5", "12345678"));
        Assert.Equal("ES8700491500050012345678", ReglasErp.IbanDatosBancarios(null, "0049", "1500", "05", "0012345678"));
        Assert.Null(ReglasErp.IbanDatosBancarios("ES88", "0049", "1500", "05", "0012345678"));
        Assert.Null(ReglasErp.IbanDatosBancarios("ES87", null, null, null, null));
    }
}
