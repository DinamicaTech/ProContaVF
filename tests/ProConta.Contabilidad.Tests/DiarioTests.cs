using ProConta.Contabilidad.Diario;
using ProConta.Contabilidad.Mayor;

namespace ProConta.Contabilidad.Tests;

/// <summary>El Diario sin BD (contabilidad/diario): filtros, sumas, bloqueos, apertura desactualizada y exportación.</summary>
public class DiarioTests
{
    private static readonly EjercicioDiario E2025 = new(9000, 2025, false, null);
    private static readonly EjercicioDiario E2026 = new(9001, 2026, false, 4);
    private static int _id;

    private static AsientoDiario Asiento(int id, EjercicioDiario ejercicio, DateTime fecha, string descripcion, string? motor, bool libroIva, params (string Cuenta, decimal Debe, decimal Haber, string? Doc)[] lineas)
    {
        var a = new AsientoDiario
        {
            IdAsiento = id, Ejercicio = ejercicio, Fecha = fecha, Descripcion = descripcion, EsAutomatico = motor is not null, MotorPropietario = motor, ClaveEjecucion = motor is null ? null : "1",
            ConLibroIva = libroIva, Version = 1,
        };
        var orden = 0;
        return a.Con(lineas.Select(l => new ApunteDiario
        {
            IdApunte = ++_id, Orden = ++orden, Cuenta = l.Cuenta, NombreCuenta = "Cuenta " + l.Cuenta, Debe = l.Debe, Haber = l.Haber, Documento = l.Doc,
        }).ToArray());
    }

    /// <summary>Tres asientos de 2026: una venta de febrero (trimestre cerrado, con libro de IVA), su cobro por banco y un manual de noviembre.</summary>
    private static DatosDiario Datos() => new([E2026],
    [
        Asiento(10, E2026, new DateTime(2026, 2, 3), "Factura F26-0001 · Cliente sintético", "facturacion/factura-venta", true,
            ("4300001", 121m, 0m, "F26-0001"), ("7050001", 0m, 100m, "F26-0001"), ("4770021", 0m, 21m, "F26-0001")),
        Asiento(11, E2026, new DateTime(2026, 2, 9), "Cobro F26-0001", "norma43/conciliacion", false, ("5720001", 121m, 0m, "F26-0001"), ("4300001", 0m, 121m, "F26-0001")),
        Asiento(14, E2026, new DateTime(2026, 11, 5), "Ingreso sin identificar", null, false, ("5720002", 350m, 0m, null), ("5550001", 0m, 350m, null)),
    ]);

    [Fact]
    public void SinFiltroEntranTodosLosAsientosCompletosYCuadran()
    {
        var vista = CalculadoraDiario.Filtrar(Datos(), new FiltroDiario());
        Assert.Equal((3, 7, 7), (vista.NumeroAsientos, vista.ApuntesAsientos, vista.NumeroApuntes));
        Assert.Equal(vista.DebeAsientos, vista.HaberAsientos);                 // Debe = Haber
        Assert.Equal(592m, vista.DebeAsientos);
        Assert.Equal([10, 11, 14], vista.Asientos.Select(a => a.Asiento.IdAsiento));
        var meses = vista.TotalesPorMes();
        Assert.Equal([(2, 2, 242m), (11, 1, 350m)], meses.Select(m => (m.Mes, m.Asientos, m.Debe)));
        Assert.All(meses, m => Assert.Equal(m.Debe, m.Haber));
    }

    [Fact]
    public void ElFiltroDeApuntesDejaElAsientoCompletoYMarcaLosQueCoinciden()
    {
        var vista = CalculadoraDiario.Filtrar(Datos(), new FiltroDiario { Cuenta = "430" });
        Assert.Equal([10, 11], vista.Asientos.Select(a => a.Asiento.IdAsiento));
        Assert.Equal((2, 121m, 121m), (vista.NumeroApuntes, vista.DebeApuntes, vista.HaberApuntes));   // lista: solo los de la 430
        Assert.Equal(5, vista.ApuntesAsientos);                                                        // por asiento: completos
        Assert.Equal(vista.DebeAsientos, vista.HaberAsientos);
        Assert.All(vista.Asientos, a => Assert.Single(a.Coinciden));
    }

    [Theory]
    [InlineData("f26-0001", 5)]          // documento, sin mayúsculas
    [InlineData("identificar", 2)]       // descripción del asiento (el concepto heredado)
    [InlineData("#11", 2)]               // número de asiento
    [InlineData("121,00", 3)]            // importe
    [InlineData("cuenta 5720002", 1)]    // nombre de la cuenta: todas las palabras
    [InlineData("zzz", 0)]
    public void ElTextoBuscaEnDescripcionConceptoDocumentoCuentaEImporte(string texto, int apuntes) =>
        Assert.Equal(apuntes, CalculadoraDiario.Filtrar(Datos(), new FiltroDiario { Texto = texto }).NumeroApuntes);

    [Fact]
    public void PeriodoOrigenPunteoYCentroFiltran()
    {
        var datos = Datos();
        datos.Asientos[1].Apuntes[0].Punteado = true;
        Assert.Equal([14], Ids(new FiltroDiario { Desde = new DateTime(2026, 10, 1), Hasta = new DateTime(2026, 12, 31) }));
        Assert.Equal([10], Ids(new FiltroDiario { Origen = OrigenesDiario.Ventas }));
        Assert.Equal([14], Ids(new FiltroDiario { Origen = OrigenesDiario.Manual }));
        Assert.Equal([11], Ids(new FiltroDiario { Punteado = true }));
        Assert.Equal(6, CalculadoraDiario.Filtrar(datos, new FiltroDiario { Punteado = false }).NumeroApuntes);
        Assert.Empty(Ids(new FiltroDiario { IdCentroCoste = 3 }));
        IEnumerable<int> Ids(FiltroDiario f) => CalculadoraDiario.Filtrar(datos, f).Asientos.Select(a => a.Asiento.IdAsiento);
    }

    [Fact]
    public void OrigenContrapartidaYBloqueosDeCadaApunte()
    {
        var datos = Datos();
        var (venta, cobro, manual) = (datos.Asientos[0], datos.Asientos[1], datos.Asientos[2]);
        Assert.Equal((OrigenesDiario.Ventas, OrigenesDiario.Bancos, OrigenesDiario.Manual), (venta.Origen, cobro.Origen, manual.Origen));
        Assert.Equal(OrigenesDiario.ContaSol, OrigenesDiario.De(true, "implantacion/importador-contasol"));
        Assert.Equal(OrigenesDiario.Procesos, OrigenesDiario.De(true, "otro/motor"));

        // Contrapartida: la del lado contrario sin contar el IVA, como en el Mayor.
        Assert.Equal("7050001", venta.Apuntes[0].Contrapartida);
        Assert.Equal("4300001", venta.Apuntes[2].Contrapartida);
        Assert.Equal("5550001", manual.Apuntes[0].Contrapartida);

        // La venta es de febrero y el trimestre activo es el 4.º: con IVA, su periodo está cerrado.
        Assert.True(venta.TrimestreCerrado);
        Assert.NotNull(venta.BloqueoPeriodo);
        Assert.Contains("🔒", venta.Apuntes[0].Bloqueos.Select(b => b.Icono));
        Assert.Contains("🧾", venta.Apuntes[2].Bloqueos.Select(b => b.Icono));
        // El cobro no lleva cuentas fiscales: se puede tocar aunque sea de febrero, salvo su apunte de banco.
        Assert.False(cobro.TrimestreCerrado);
        Assert.Equal(["🏦"], cobro.Apuntes[0].Bloqueos.Select(b => b.Icono));
        Assert.NotNull(cobro.Apuntes[0].BloqueoCuenta);
        Assert.Null(cobro.Apuntes[1].BloqueoCuenta);
        // Un manual no tiene bloqueos, ni siquiera en su 572.
        Assert.Empty(manual.Apuntes[0].Bloqueos);
        Assert.Null(manual.Apuntes[0].BloqueoCuenta);

        // Ejercicio cerrado: todo bloqueado.
        var cerrado = Asiento(1, E2025 with { Cerrado = true }, new DateTime(2025, 5, 1), "Viejo", null, false, ("5720001", 1m, 0m, null), ("7050001", 0m, 1m, null));
        Assert.Contains("cerrado", cerrado.BloqueoPeriodo);
    }

    [Fact]
    public void LaAperturaDesactualizadaDiceLasCuentasYLaDiferencia()
    {
        const byte apertura = TiposAsientoMayor.Apertura, cierre = TiposAsientoMayor.Cierre;
        AsientoDiario Tipo(AsientoDiario a, byte tipo) => new AsientoDiario
        {
            IdAsiento = a.IdAsiento, Ejercicio = a.Ejercicio, Fecha = a.Fecha, Descripcion = a.Descripcion, TipoAsiento = tipo,
        }.Con(a.Apuntes.Select(l => new ApunteDiario { IdApunte = l.IdApunte, Orden = l.Orden, Cuenta = l.Cuenta, NombreCuenta = l.NombreCuenta, Debe = l.Debe, Haber = l.Haber }).ToArray());

        // 2025: banco 1.000 contra capital, una venta de 200 al banco (sin regularizar) y el asiento de cierre, que no cuenta.
        var a2025 = new[]
        {
            Asiento(1, E2025, new DateTime(2025, 1, 1), "Aportación", null, false, ("5720001", 1000m, 0m, null), ("1000001", 0m, 1000m, null)),
            Asiento(2, E2025, new DateTime(2025, 6, 1), "Venta", null, false, ("5720001", 200m, 0m, null), ("7050001", 0m, 200m, null)),
            Tipo(Asiento(3, E2025, new DateTime(2025, 12, 31), "Cierre", null, false, ("1000001", 1000m, 0m, null), ("1290001", 200m, 0m, null), ("5720001", 0m, 1200m, null)), cierre),
        };
        AsientoDiario Abre(decimal banco, decimal resultado) => Tipo(Asiento(4, E2026, new DateTime(2026, 1, 1), "Apertura", null, false,
            ("5720001", banco, 0m, null), ("1000001", 0m, 1000m, null), ("1290001", 0m, resultado, null)), apertura);

        // Apertura correcta: el banco con 1.200 y el resultado de 200 (aunque 2025 no esté regularizado).
        Assert.Empty(CalculadoraDiario.DiferenciasApertura(new DatosDiario([E2025, E2026], [.. a2025, Abre(1200m, 200m)]), E2025, E2026));

        // Después de la apertura se añade un gasto de 50 en 2025: el banco y el resultado ya no coinciden.
        var gasto = Asiento(5, E2025, new DateTime(2025, 7, 1), "Gasto tardío", null, false, ("6290001", 50m, 0m, null), ("5720001", 0m, 50m, null));
        var diferencias = CalculadoraDiario.DiferenciasApertura(new DatosDiario([E2025, E2026], [.. a2025, gasto, Abre(1200m, 200m)]), E2025, E2026);
        Assert.Equal(["129", "5720001"], diferencias.Select(d => d.Cuenta));
        var banco = diferencias.Single(d => d.Cuenta == "5720001");
        Assert.Equal((1150m, 1200m, 50m), (banco.SaldoCierre, banco.SaldoApertura, banco.Diferencia));
        Assert.Equal(-50m, diferencias.Single(d => d.Cuenta == "129").Diferencia);

        // Sin asiento de apertura no hay nada que comparar.
        Assert.Empty(CalculadoraDiario.DiferenciasApertura(new DatosDiario([E2025, E2026], a2025), E2025, E2026));
    }

    [Fact]
    public void ElHtmlDelPdfLlevaLasColumnasPedidasLosAsientosCompletosYLosTotales()
    {
        var vista = CalculadoraDiario.Filtrar(Datos(), new FiltroDiario { Cuenta = "430" });
        var columnas = new[] { ColumnasDiario.Asiento, ColumnasDiario.Fecha, ColumnasDiario.Cuenta, ColumnasDiario.Concepto, ColumnasDiario.Debe, ColumnasDiario.Haber }.Select(ColumnasDiario.De).ToList();
        var cabecera = new CabeceraExportacionDiario("Sociedad <sintética>", "2026", "del 01/01/2026 al 31/12/2026", "Cuenta: 430", PorAsiento: true, TotalesPorMes: true, new DateTime(2026, 11, 14, 22, 0, 0));

        var html = ExportacionDiario.Html(cabecera, columnas, vista);
        Assert.Contains("Sociedad &lt;sintética&gt;", html);            // el texto va escapado
        Assert.Equal(6, Veces(html, "<th>") + Veces(html, "<th class"));
        Assert.DoesNotContain("Documento", html);
        Assert.Equal(2, Veces(html, "<tr class=\"g\">"));                    // una cabecera por asiento
        Assert.Contains("4770021", html);                                    // el asiento sale completo, no solo la 430
        Assert.Contains("Total de febrero de 2026 · 2 asientos", html);
        Assert.Contains("Total · 2 asientos · 5 apuntes", html);
        Assert.Contains("242,00", html);

        var lista = ExportacionDiario.Html(cabecera with { PorAsiento = false }, columnas, vista);
        Assert.DoesNotContain("4770021", lista);                             // en lista, solo los apuntes que cumplen el filtro
        Assert.Contains("Total · 2 apuntes · saldo 0,00", lista);
        Assert.Equal(2, ExportacionDiario.Apuntes(vista, porAsiento: false).Count());
        Assert.Equal(5, ExportacionDiario.Apuntes(vista, porAsiento: true).Count());

        static int Veces(string texto, string trozo) => texto.Split(trozo).Length - 1;
    }

    [Fact]
    public void LasColumnasDeFabricaSonLasDelNodoYCadaValorTieneSuTipo()
    {
        Assert.Equal(["Asiento", "Id apunte", "Fecha", "Origen", "Subcuenta", "Descripción", "Concepto", "Documento", "Debe", "Haber", "Centro de coste", "Punteado", "Bloqueos"],
            ColumnasDiario.Todas.Where(c => !c.OcultaDeFabrica).Select(c => c.Titulo));
        Assert.Equal(["Orden", "Contrapartida", "Vencimiento", "Fecha del documento", "Grabado el", "Modificado el", "Operador"],
            ColumnasDiario.Todas.Where(c => c.OcultaDeFabrica).Select(c => c.Titulo));
        var apunte = Datos().Asientos[0].Apuntes[0];
        Assert.IsType<decimal>(ColumnasDiario.De(ColumnasDiario.Debe).Valor(apunte));     // importes en decimal, nunca double
        Assert.Null(ColumnasDiario.De(ColumnasDiario.Haber).Valor(apunte));
        Assert.Equal("121,00", ColumnasDiario.De(ColumnasDiario.Debe).Texto(apunte));
        Assert.Equal("03/02/2026", ColumnasDiario.De(ColumnasDiario.Fecha).Texto(apunte));
        Assert.Equal("", ColumnasDiario.De(ColumnasDiario.Punteado).Texto(apunte));
    }
}
