using System.Text;
using ProConta.Contabilidad.Impuestos;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Datos fiscales de la AEAT en el Impuesto sobre Sociedades (contabilidad/procesos/impuesto-sociedades), sin BD: el lector de la página de
/// la Sede guardada como HTML (tablas por identificador y columnas por cabecera), la asignación de cada retención a su 473 y lo que cambia
/// en la propuesta (se deduce lo que conoce la AEAT, lo escrito manda y se avisa de cada diferencia). Página sintética con la estructura de
/// la real; NIF y nombres inventados.
/// </summary>
public sealed class DatosFiscalesAeatTests
{
    /// <summary>Página sintética con la estructura de la consulta de datos fiscales del IS de la Sede.</summary>
    public static string Html(int ejercicio, string nif, IReadOnlyList<(string Periodo, string Importe)> pagos,
        IReadOnlyList<(string Nif, string Nombre, string Retencion)> alquileres, string? bases, string? presentado,
        IReadOnlyList<(string Nombre, string Retencion)>? capital = null, bool viviendas = true)
    {
        var h = new StringBuilder();
        h.Append("<!DOCTYPE html><html><head><meta http-equiv=\"content-type\" content=\"text/html; charset=ISO-8859-15\"><script>var x='<table id=\"TablaFalsa\">';</script></head><body>");
        h.Append($"<h1>Consulta de Datos Fiscales Impuesto Sociedades {ejercicio}</h1>");
        h.Append($"<table class=\" celdasConBorde\" id=\"TablaNif\"><tbody><tr><td><strong>NIF: </strong><br> {nif}</td><td><strong>Nombre:</strong><br> <span>SOCIEDAD SINTETICA SL</span></td></tr></tbody></table>");
        h.Append("<table id=\"TablamD2\"><tr><th>Código</th><th>Código de Carácter</th><th>Carácter</th></tr><tr></tr>");
        if (viviendas) h.Append("<tr><td>MD20001</td><td>00038</td><td>Entidad dedicada al arrend. de viviendas</td></tr>");
        h.Append("<tr><td>MD20002</td><td>00073</td><td>Opci&oacute;n del 0,7%</td></tr></table>");
        h.Append("<table id=\"TablaPf\"><thead><tr><th>Código</th><th>Modelo</th><th>Tipo</th><th>Periodo</th><th>Delegación</th><th>Justificante</th><th>Importe</th><th>Casilla M200</th></tr></thead><tbody><tr></tr>");
        var i = 1;
        foreach (var (periodo, importe) in pagos)
            h.Append($"<tr class=\"fila_par\"><td class=\" texto_izq\">PF{i++:00000}</td><td>202-IS pagos fraccionados</td><td>Ingreso</td><td>{periodo}</td><td>08</td><td>1</td><td class=\" texto_der\">{importe}</td><td>00603</td></tr>");
        h.Append("<tr><td></td><td>TOTAL</td><td>0,00</td><td></td></tr></tbody></table>");
        h.Append("<table id=\"TablaCin1\"><tr><th>Código</th><th>Clave</th><th>Importe Calculado</th><th>Percepción</th><th>Retenciones</th></tr>");
        h.Append("<tr><td>CIN0001</td><td>A00-Empleados</td><td>9.999,00</td><td>9.000,00</td><td>378,00</td></tr></table>");
        h.Append("<table id=\"TablaRe0\"><tr><th>Código</th><th>NIF Emisor</th><th>Nombre Emisor</th><th>NIF Declarante</th><th>Nombre Declarante</th><th>Clave</th><th>Tipo</th><th>Importe Íntegro</th><th>Penalización</th><th>Retenciones</th><th>Gastos Deducibles</th></tr><tr></tr>");
        i = 1;
        foreach (var (nombre, ret) in capital ?? [])
            h.Append($"<tr><td>RE{i++:00000}</td><td></td><td>CODIGO:</td><td>A00000000</td><td><span class=\"notraducir\">{nombre}   </span></td><td>C14-Otros</td><td>Dineraria</td><td>1.000,00</td><td></td><td>{ret}</td><td></td></tr>");
        h.Append("<tr><td></td><td>TOTAL</td><td>0,00</td><td></td></tr></table>");
        h.Append("<table id=\"TablaAr\"><tr><th>Código</th><th>NIF Pagador</th><th>Nombre Pagador</th><th>Referencia Catastral</th><th>Clave</th><th>Ingresos</th><th>Retenciones</th></tr><tr></tr>");
        i = 1;
        foreach (var (n, nombre, ret) in alquileres)
            h.Append($"<tr><td>AR{i++:00000}</td><td>{n}</td><td><span>{nombre}</span></td><td>0000000XX0000X0000XX</td><td>Dineraria</td><td>10.000,00</td><td>{ret}</td></tr>");
        h.Append("<tr><td></td><td>Casilla M200</td><td>00267</td><td>01787</td></tr></table>");
        h.Append("<table id=\"TablaCbi\"><tr><th>Código</th><th>Ejercicio</th><th>Importe Pendiente de Aplicación o Generada en el período</th></tr><tr></tr>");
        h.Append($"<tr><td>CBI0001</td><td>2019</td><td> </td></tr><tr><td>CBI0002</td><td>2020</td><td>{bases ?? ""}</td></tr><tr><td></td><td>TOTAL</td><td></td></tr></table>");
        if (presentado is not null)
            h.Append("<table id=\"TablaDec\"><tr><th>Código</th><th>Número Justificante</th><th>Fecha de Presentación</th><th>Importe Consignado 621</th><th>Importe Consignado 622</th></tr><tr></tr>" +
                     "<tr><td>DEC0001</td><td>1</td><td>01-07-2026</td><td>9,99</td><td></td></tr>" +
                     $"<tr><td>DEC0002</td><td>2</td><td>24-07-2026</td><td>{presentado}</td><td></td></tr><tr></tr><tr><td></td><td>Casilla M200</td><td>00621</td><td>00622</td></tr></table>");
        h.Append("</body></html>");
        return h.ToString();
    }

    private static DatosFiscalesAeat Ejemplo() => LectorDatosFiscalesAeat.LeerHtml(Html(2025, "B12345674",
        [("2T", "1.250,50"), ("3T", "1.250,50")],
        [("X0000001X", "INMOBILIARIA ALFA SL", "5.852,00"), ("00000002Y", "GARCIA PEREZ JUAN", "957,07")],
        bases: "3.000,00", presentado: "-5.882,94", capital: [("BANCO BETA SA", "354,19"), ("BANCO BETA SA", "45,81")]));

    [Fact]
    public void LeeLasTablasPorSuIdentificadorYSusColumnasPorLaCabecera()
    {
        var d = Ejemplo();
        Assert.Equal(2025, d.Ejercicio);
        Assert.Equal("B12345674", d.Nif);
        Assert.Equal(
            [("202", null, "Pago fraccionado 2T", 1250.50m), ("202", null, "Pago fraccionado 3T", 1250.50m), ("193", "A00000000", "BANCO BETA SA", 354.19m),
                ("193", "A00000000", "BANCO BETA SA", 45.81m), ("180", "X0000001X", "INMOBILIARIA ALFA SL", 5852.00m), ("180", "00000002Y", "GARCIA PEREZ JUAN", 957.07m)],
            d.Retenciones.Select(r => (r.Modelo, r.Nif, r.Nombre, r.Importe)).ToList());
        // El 190 lo presenta la propia sociedad: no son retenciones que le hayan hecho.
        Assert.DoesNotContain(d.Retenciones, r => r.Importe == 378m);
        Assert.Equal(3000m, d.BasesNegativas);
        Assert.Equal(-5882.94m, d.ResultadoPresentado);
        Assert.True(d.ArrendamientoViviendas);
        Assert.Empty(d.Avisos);
    }

    [Fact]
    public void LeeElFicheroEnIso88591YSinTablasLoDice()
    {
        var bytes = Encoding.Latin1.GetBytes(Html(2025, "B12345674", [], [("X0000001X", "MUÑOZ ÁLVAREZ", "10,00")], null, null, viviendas: false));
        var d = LectorDatosFiscalesAeat.Leer(bytes);
        Assert.Equal("MUÑOZ ÁLVAREZ", Assert.Single(d.Retenciones).Nombre);
        Assert.Equal(0m, d.BasesNegativas);
        Assert.Null(d.ResultadoPresentado);
        Assert.False(d.ArrendamientoViviendas);

        var otra = LectorDatosFiscalesAeat.LeerHtml("<html><body><p>Hola</p></body></html>");
        Assert.Null(otra.Ejercicio);
        Assert.Empty(otra.Retenciones);
        Assert.Contains("datos fiscales", Assert.Single(otra.Avisos));
    }

    private static readonly IReadOnlyList<SaldoCuentaImpuesto> Cuentas473 =
    [
        new("4730001", "RETENCIONES ALQUILER ALFA", 6477.85m, 10, ["INMOBILIARIA ALFA, SLU", "JUAN"]),
        new("4730002", "PAGO A CTA. IMP.SOCIEDADES", 2501.00m, 2, []),
        new("4730004", "RETENCIONES BANCO BETA", 380.00m, 2, []),
        new("4730105", "RETENCION FONDO GAMMA", 20.00m, 1, []),
        new("4735079", "RETENCIONES LOCAL 2", 1668.33m, 10, ["JUAN", "OTRO INQUILINO"]),
    ];

    [Fact]
    public void AsignaCadaRetencionASu473()
    {
        var a = LectorDatosFiscalesAeat.Asignar(Ejemplo().Retenciones, Cuentas473);
        Assert.Equal(["4730002", "4730002", "4730004", "4730004", "4730001", null], a.Select(r => r.Cuenta));
        // Si las palabras empatan y no coincide el saldo, se queda sin asignar; con el mismo saldo que una sola 473, va a ella.
        var porSaldo = LectorDatosFiscalesAeat.Asignar([new("180", null, "SIN NOMBRE CONOCIDO", 1668.33m)], Cuentas473);
        Assert.Equal("4735079", Assert.Single(porSaldo).Cuenta);
        // Lo ya asignado se respeta.
        Assert.Equal("4735079", LectorDatosFiscalesAeat.Asignar([new("180", null, "GARCIA PEREZ JUAN", 957.07m, "4735079")], Cuentas473)[0].Cuenta);
    }

    private static DatosImpuestoSociedades Datos(decimal resultado) =>
        new(2025, resultado, 0m, false, Cuentas473, [], 0m, ParametrosImpuestoSociedades.Defecto, []);

    [Fact]
    public void ConDatosFiscalesSeDeduceLoQueConoceLaAeatYLoEscritoManda()
    {
        var aeat = LectorDatosFiscalesAeat.Asignar(Ejemplo().Retenciones, Cuentas473);
        var p = CalculoImpuestoSociedades.Calcular(Datos(40000m), new([], RetencionesAeat: aeat, BasesNegativasAeat: 3000m, ResultadoPresentado: 0m,
            Deducir: [new("4735079", 1668.33m)]));
        Assert.Equal(
            [("4730001", 6477.85m, (decimal?)5852.00m, 5852.00m, 5852.00m), ("4730002", 2501.00m, 2501.00m, 2501.00m, 2501.00m), ("4730004", 380.00m, 400.00m, 400.00m, 400.00m),
                ("4730105", 20.00m, 0m, 0m, 0m), ("4735079", 1668.33m, 0m, 0m, 1668.33m)],
            p.Retenciones.Select(r => (r.Cuenta, r.Saldo, r.SegunAeat, r.Propuesto, r.Deducido)).ToList());
        Assert.Equal(10421.33m, p.TotalRetenciones);
        Assert.Equal(11047.18m, p.TotalSaldos473);
        Assert.Equal(10000m, p.CuotaLiquida);
        Assert.Equal(-421.33m, p.Resultado);
        Assert.Equal(p.TotalDebe, p.TotalHaber);
        Assert.DoesNotContain(p.Lineas, l => l.Cuenta == "4730105");
        Assert.Contains(p.Avisos, x => x.Contains("GARCIA PEREZ JUAN") && x.Contains("sin cuenta 473"));
        Assert.Contains(p.Avisos, x => x.Contains("4730001") && x.Contains("625,85 €") && x.Contains("se queda en la cuenta"));
        Assert.Contains(p.Avisos, x => x.Contains("Has escrito 1.668,33 €") && x.Contains("4735079"));
        Assert.Contains(p.Avisos, x => x.Contains("4730004") && x.Contains("quedará con saldo acreedor"));
        Assert.Contains(p.Avisos, x => x.Contains("declaración presentada") && x.Contains("-421,33 €"));
        Assert.DoesNotContain(p.Avisos, x => x.Contains("Sin datos fiscales"));

        var bases = CalculoImpuestoSociedades.Calcular(Datos(40000m), new([], BasesNegativas: 5000m, RetencionesAeat: aeat, BasesNegativasAeat: 3000m));
        Assert.Contains(bases.Avisos, x => x.Contains("solo tiene pendientes 3.000,00 €"));
    }

    [Fact]
    public void SinDatosFiscalesSeDeduceElSaldoYSeAvisa()
    {
        var p = CalculoImpuestoSociedades.Calcular(Datos(40000m), EntradaImpuestoSociedades.Vacia);
        Assert.Equal(p.TotalSaldos473, p.TotalRetenciones);
        Assert.All(p.Retenciones, r => Assert.Null(r.SegunAeat));
        Assert.Contains(p.Avisos, x => x.Contains("Sin datos fiscales de la AEAT"));
        Assert.NotNull(CalculoImpuestoSociedades.Validar(new([], Deducir: [new("4730001", -1m)])));
        Assert.NotNull(CalculoImpuestoSociedades.Validar(new([], RetencionesAeat: [new("180", null, "X", -1m)])));
    }
}
