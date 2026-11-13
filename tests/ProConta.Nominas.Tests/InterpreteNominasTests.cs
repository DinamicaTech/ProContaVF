using ProConta.Nominas.Lectura;
using ProConta.Terceros.Roles;

namespace ProConta.Nominas.Tests;

public sealed class InterpreteNominasTests
{
    [Fact]
    public void Lee_metadatos_cabeceras_codigo_y_filas_del_resumen_de_la_gestoria()
    {
        var f = InterpreteNominas.Interpretar(ResumenSintetico.Octubre().Celdas());
        Assert.True(f.EsValido, string.Join(" ", f.Errores));
        Assert.Equal("9999", f.CodigoEmpresa);
        Assert.Equal("B12345674", f.NifEmpresa);
        Assert.Equal(TipoPaga.Mensual, f.TipoPaga);
        Assert.Equal(new DateTime(2026, 10, 1), f.PeriodoDesde);
        Assert.Equal(new DateTime(2026, 10, 31), f.PeriodoHasta);
        Assert.Equal(new DateTime(2026, 10, 31), f.FechaNomina);
        Assert.True(f.TieneTotal);
        Assert.Equal(3, f.Filas.Count);
        Assert.Equal(["000901", "000902", "000903"], f.Filas.Select(x => x.CodigoTrabajador));
        var eva = f.Filas[2];
        Assert.Equal("INVENTADA TRES, EVA", eva.Nombre);
        Assert.Equal(1450m, eva["Bruto"]);
        Assert.Equal(460m, eva["SSEmp"]);
        Assert.Equal(92m, eva.CuotasObreras);
        Assert.Equal(1271m, eva["Neto"]);
        Assert.Equal(0m, eva.DescuadreNeto);
        Assert.Equal(-40m, f.Filas[0]["DtoReEsp"]);
        Assert.Empty(f.Advertencias);
    }

    [Fact]
    public void La_paga_extra_y_la_fecha_de_cobro_de_la_variante_de_24_columnas()
    {
        var r = new ResumenSintetico { Paga = "PAGA EXTRA", Periodo = "DEL 01/07/2026 AL 31/07/2026" };
        r.Cabeceras.InsertRange(1, ["N.I.F.", "TIPO PAGA", "FECHA COBRO"]);
        var cobro = new DateTime(2026, 7, 15).ToOADate();
        r.Trabajador("000901", "FICTICIO UNO, ANA", 960m, extra: new() { ["N.I.F."] = "00000000T", ["TIPO PAGA"] = "EXTRA", ["FECHA COBRO"] = cobro });
        var f = InterpreteNominas.Interpretar(r.Celdas());
        Assert.True(f.EsValido, string.Join(" ", f.Errores));
        Assert.Equal(TipoPaga.Extra, f.TipoPaga);
        Assert.Equal(new DateTime(2026, 7, 15), f.FechaNomina);
        Assert.Equal("00000000T", f.Filas[0].Nif);
    }

    [Fact]
    public void Sin_AL_el_periodo_acaba_el_ultimo_dia_del_mes()
    {
        var f = InterpreteNominas.Interpretar(new ResumenSintetico { Periodo = "DEL 01/02/2026" }.Trabajador("1", "A, B", 1000m).Celdas());
        Assert.Equal(new DateTime(2026, 2, 28), f.FechaNomina);
    }

    [Fact]
    public void Si_las_filas_no_suman_el_total_de_la_empresa_es_un_error()
    {
        var r = ResumenSintetico.Octubre();
        r.AjusteTotal["BRUTO"] = 100m;
        var f = InterpreteNominas.Interpretar(r.Celdas());
        Assert.False(f.EsValido);
        Assert.Contains(f.Errores, e => e.Texto.Contains("no da el total de la empresa"));
    }

    [Fact]
    public void Un_neto_que_no_cuadra_es_una_advertencia_con_su_fila()
    {
        var f = InterpreteNominas.Interpretar(new ResumenSintetico().Trabajador("1", "A, B", 1000m, irpf: 100m, neto: 950m).Celdas());
        Assert.True(f.EsValido);
        var a = Assert.Single(f.Advertencias);
        Assert.Equal(10, a.Fila);
        Assert.Contains("no cuadra", a.Texto);
    }

    [Fact]
    public void Una_cabecera_desconocida_queda_por_decidir_y_el_alias_de_la_sociedad_la_resuelve()
    {
        var r = ResumenSintetico.Octubre();
        r.Cabeceras.Add("PLUS RARO");
        r.Trabajadores[0].Valores["PLUS RARO"] = 12.5;
        var f = InterpreteNominas.Interpretar(r.Celdas());
        var sin = Assert.Single(f.SinAlias);
        Assert.Equal("PLUS RARO", sin.Cabecera);
        Assert.Equal("12.5", sin.Ejemplo);
        Assert.False(f.EsValido);

        var conAlias = InterpreteNominas.Interpretar(r.Celdas(), new Dictionary<string, string?> { ["PLUS RARO"] = null });
        Assert.True(conAlias.EsValido, string.Join(" ", conAlias.Errores));
        var mapeado = InterpreteNominas.Interpretar(r.Celdas(), new Dictionary<string, string?> { ["PLUS RARO"] = "EnfermedadAccidente" });
        Assert.Equal(12.5m, mapeado.Filas[0]["EnfermedadAccidente"]);
    }

    [Fact]
    public void Sin_cabeceras_reconocibles_lo_explica()
    {
        var f = InterpreteNominas.Interpretar([["hola", "adiós"], [1.0, 2.0]]);
        Assert.Contains("fila de cabeceras", Assert.Single(f.Errores).Texto);
    }

    [Fact]
    public void Un_importe_que_no_es_numero_es_un_error_de_su_fila()
    {
        var r = new ResumenSintetico().Trabajador("1", "A, B", 1000m);
        r.Trabajadores[0].Valores["IRPF 111"] = "mucho";
        r.ConTotal = false;
        var f = InterpreteNominas.Interpretar(r.Celdas());
        Assert.Contains(f.Errores, e => e.Fila == 10 && e.Texto.Contains("no es un importe"));
    }

    [Theory]
    [InlineData("1.234,56", 1234.56)]
    [InlineData("1234.56", 1234.56)]
    [InlineData("-44,77 €", -44.77)]
    [InlineData("", 0)]
    public void Importes_en_texto(string texto, double esperado) =>
        Assert.Equal((decimal)esperado, InterpreteNominas.Importe(texto));

    [Fact]
    public void El_lector_abre_un_xlsx_con_ExcelDataReader()
    {
        var f = LectorExcelNominas.Leer(ResumenSintetico.Octubre().Xlsx());
        Assert.True(f.EsValido, string.Join(" ", f.Errores));
        Assert.Equal(3, f.Filas.Count);
        Assert.Equal(780m, f.Filas[1]["IRPF111"]);
    }

    [Fact]
    public void Un_fichero_que_no_es_Excel_se_rechaza_con_su_motivo()
    {
        var f = LectorExcelNominas.Leer("esto no es un excel"u8.ToArray());
        Assert.Contains("No puedo leer el fichero como Excel", Assert.Single(f.Errores).Texto);
    }

    [Theory]
    [InlineData("NAVARRO GIL, LUCIA", "Lucia", "Navarro", "Gil")]
    [InlineData("VAN DER BERG LOPEZ, ANA MARIA", "Ana Maria", "Van Der Berg", "Lopez")]
    [InlineData("PEREZ, JUAN", "Juan", "Perez", null)]
    [InlineData("Juan Sin Coma", "Juan Sin Coma", null, null)]
    public void El_nombre_de_la_gestoria_se_parte_en_nombre_y_apellidos(string texto, string nombre, string? ap1, string? ap2) =>
        Assert.Equal((nombre, ap1, ap2), ServicioEmpleados.PartirNombre(texto));
}
