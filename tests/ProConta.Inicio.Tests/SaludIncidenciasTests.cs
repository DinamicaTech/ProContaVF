using ProConta.Compras.Facturas;
using ProConta.Inicio;
using ProConta.Inicio.Salud;

namespace ProConta.Inicio.Tests;

/// <summary>Avisos de salud S1 a S10 (incidencias con la gestoría, contabilidad 2026-11-15 21:10), sin BD: casos que disparan y que no.</summary>
public sealed class SaludIncidenciasTests
{
    private static readonly DateTime Corte = new(2026, 11, 15);

    private static ApunteCuenta A(string cuenta, DateTime fecha, decimal debe, decimal haber, byte tipo = 0, int asiento = 0, int ejercicio = 0) =>
        new(ejercicio == 0 ? fecha.Year : ejercicio, asiento == 0 ? fecha.GetHashCode() : asiento, fecha, tipo, cuenta, debe, haber);

    private static ElementoSalud Unico(HallazgosSalud h) => Assert.Single(h.Elementos);

    // ---------------- S1 saldos inmóviles

    [Fact]
    public void S1_AvisaDeUnaCuentaPuenteConSaldoSinMoverseEnDoceMeses()
    {
        var apuntes = new[]
        {
            A("4708000", new(2026, 1, 1), 1000m, 0m, tipo: 1),                 // solo apertura: subvención pendiente
            A("1800001", new(2026, 1, 1), 0m, 600m, tipo: 1),                   // fianza: no entra
            A("5550001", new(2025, 3, 10), 200m, 0m, ejercicio: 2025),          // último apunte hace más de 12 meses
            A("5550001", new(2026, 1, 1), 200m, 0m, tipo: 1),
            A("5550002", new(2026, 10, 1), 50m, 0m),                            // reciente: no
            A("4751001", new(2026, 1, 1), 0m, 300m, tipo: 1),                   // retenciones: las vigila S4
            A("5720001", new(2026, 1, 1), 900m, 0m, tipo: 1),                   // no es puente
        };
        var e = Unico(RevisionSaldosInmoviles.Evaluar(apuntes, 2026, Corte, new HashSet<string>()));
        Assert.Equal(GravedadSalud.Aviso, e.Gravedad);
        Assert.Equal(["4708000", "5550001"], e.Destino.Cuentas);
        Assert.Equal(1200m, e.Importe);
    }

    [Fact]
    public void S1_NoCuentaLasSubcuentasCuadradasConElMismoSaldoNiLosBancos()
    {
        var apuntes = new[] { A("4708000", new(2026, 1, 1), 1000m, 0m, tipo: 1), A("5550009", new(2026, 1, 1), 10m, 0m, tipo: 1) };
        var cuadradas = new Dictionary<string, decimal?> { ["4708000"] = 1000m };
        var h = RevisionSaldosInmoviles.Evaluar(apuntes, 2026, Corte, new HashSet<string> { "5550009" }, cuadradas);
        Assert.Empty(h.Elementos);
        Assert.Single(h.Correctas);
        // Si el saldo ha cambiado desde que se marcó, vuelve a avisar.
        cuadradas["4708000"] = 900m;
        Assert.Single(RevisionSaldosInmoviles.Evaluar(apuntes, 2026, Corte, new HashSet<string> { "5550009" }, cuadradas).Elementos);
    }

    // ---------------- S2 IVA a compensar ya usado

    [Fact]
    public void S2_AvisaSiUnaLiquidacionAIngresarNoAplicaElCreditoDeLa4700()
    {
        var apuntes = new[]
        {
            A("4700000", new(2026, 1, 1), 626.91m, 0m, tipo: 1),
            A("4750000", new(2026, 3, 31), 0m, 1500m, asiento: 10),   // 1T a ingresar, sin abonar la 4700
        };
        var e = Unico(RevisionIvaCompensarUsado.Evaluar(apuntes, 2026, Corte));
        Assert.Equal(GravedadSalud.Atencion, e.Gravedad);
        Assert.Equal(626.91m, e.Importe);
    }

    [Fact]
    public void S2_NoAvisaSiLaLiquidacionCompensaElCredito()
    {
        var apuntes = new[]
        {
            A("4700000", new(2026, 1, 1), 626.91m, 0m, tipo: 1),
            A("4750000", new(2026, 3, 31), 0m, 873.09m, asiento: 10),
            A("4700000", new(2026, 3, 31), 0m, 626.91m, asiento: 10),
        };
        Assert.Empty(RevisionIvaCompensarUsado.Evaluar(apuntes, 2026, Corte).Elementos);
    }

    // ---------------- S3 liquidación incompleta

    [Fact]
    public void S3_AvisaSiLas472o477NoQuedanACeroTrasLaLiquidacion()
    {
        var apuntes = new[]
        {
            A("4770001", new(2026, 2, 10), 0m, 1000m, asiento: 1),
            A("4720001", new(2026, 2, 12), 300m, 0m, asiento: 2),
            A("4720001", new(2026, 3, 20), 50m, 0m, asiento: 3),           // factura que la liquidación no recoge
            // Liquidación del 1T con fecha del plazo (15 de abril).
            A("4770001", new(2026, 4, 15), 1000m, 0m, asiento: 9),
            A("4720001", new(2026, 4, 15), 0m, 300m, asiento: 9),
            A("4750000", new(2026, 4, 15), 0m, 700m, asiento: 9),
            A("4770001", new(2026, 5, 10), 0m, 400m, asiento: 4),           // 2T: no cuenta en el 1T
        };
        var e = Unico(RevisionLiquidacionIncompleta.Evaluar(apuntes, 2026, Corte));
        Assert.Equal(["4720001"], e.Destino.Cuentas);
        Assert.Contains("1T", e.Titulo);
    }

    [Fact]
    public void S3_LiquidacionCompletaVaAComprobacionesCorrectas()
    {
        var apuntes = new[]
        {
            A("4770001", new(2026, 2, 10), 0m, 1000m, asiento: 1), A("4720001", new(2026, 2, 12), 300m, 0m, asiento: 2),
            A("4770001", new(2026, 3, 31), 1000m, 0m, asiento: 9), A("4720001", new(2026, 3, 31), 0m, 300m, asiento: 9), A("4750000", new(2026, 3, 31), 0m, 700m, asiento: 9),
        };
        var h = RevisionLiquidacionIncompleta.Evaluar(apuntes, 2026, Corte);
        Assert.Empty(h.Elementos);
        Assert.Single(h.Correctas);
    }

    // ---------------- S4 retenciones

    [Fact]
    public void S4_AvisaSiLoPagadoNoEsLoPendienteAlCierreDelTrimestre()
    {
        var apuntes = new[]
        {
            A("4751001", new(2026, 1, 1), 0m, 500m, tipo: 1), A("4751001", new(2026, 1, 20), 500m, 0m),        // 4T 2025: cuadra
            A("4751001", new(2026, 2, 1), 0m, 1164.21m), A("4751001", new(2026, 4, 20), 1000m, 0m),             // 1T: pagado de menos
            A("4751001", new(2026, 5, 1), 0m, 300m), A("4751001", new(2026, 7, 20), 300m, 0m),                         // 2T: arrastra la diferencia
        };
        var e = Unico(RevisionRetenciones.Evaluar(apuntes, 2026, Corte));
        // La diferencia del 1T se arrastra al 2T y al 3T, pero solo se señala donde aparece.
        Assert.Equal("Retenciones sin cuadrar: 1T 2026", e.Titulo);
        Assert.Equal(GravedadSalud.Atencion, e.Gravedad);
    }

    [Fact]
    public void S4_RetencionesPagadasPorSuImporteVanAComprobacionesCorrectas()
    {
        var apuntes = new[]
        {
            A("4751001", new(2026, 2, 1), 0m, 400m), A("4751001", new(2026, 4, 20), 400m, 0m),
            A("4751001", new(2026, 5, 1), 0m, 250m), A("4751001", new(2026, 7, 20), 250m, 0m),
            A("4751001", new(2026, 8, 1), 0m, 120m), A("4751001", new(2026, 10, 20), 120m, 0m),
        };
        var h = RevisionRetenciones.Evaluar(apuntes, 2026, Corte);
        Assert.Empty(h.Elementos);
        Assert.Single(h.Correctas);
    }

    // ---------------- S5 a S7

    [Fact]
    public void S5_La4009ConSaldoPasadoMarzoAvisa()
    {
        var saldos = new Dictionary<string, decimal> { ["4009000"] = -350m };
        Assert.Single(RevisionPendientesRecibir.Evaluar(saldos, Corte).Elementos);
        Assert.Empty(RevisionPendientesRecibir.Evaluar(saldos, new DateTime(2026, 2, 15)).Elementos);
        Assert.Empty(RevisionPendientesRecibir.Evaluar(new Dictionary<string, decimal> { ["4009000"] = 0m }, Corte).Elementos);
    }

    [Theory]
    [InlineData("12345678Z", false)]   // DNI válido
    [InlineData("ES12345678Z", false)] // con prefijo
    [InlineData("12345678A", true)]    // letra mal
    [InlineData("IE6388047V", false)]  // VAT de otro país: no se valida
    [InlineData("B1234567", true)]     // CIF incompleto
    public void S6_DetectaLosNifEspanolesQueNoValidan(string nif, bool invalido) => Assert.Equal(invalido, RevisionNifInvalido.Invalido(nif));

    [Fact]
    public void S6_AvisaConLosTercerosDeNifInvalido()
    {
        var e = Unico(RevisionNifInvalido.Evaluar([new(1, "Proveedor sintético", "4000001", false, "12345678A"), new(2, "Cliente sintético", "4300001", true, "12345678Z"),
            new(3, "Proveedor extranjero", "4000002", false, "9827384L", Pais: "Irlanda"), new(4, "Con VAT", "4000003", false, "5211923916", TipoIdentificacion: "VAT"),
            new(5, "Otro sin país", "4000004", false, "5211923916", TipoIdentificacion: "OTRO"), new(6, "Otro en España", "4000005", false, "33967662", TipoIdentificacion: "OTRO", Pais: "ES")]));
        Assert.Equal(PantallaSalud.Terceros, e.Destino.Pantalla);
        Assert.Equal(2, e.Cantidad);
    }

    [Fact]
    public void S7_AvisaDeLosGastosEIngresosSinCentro()
    {
        var e = Unico(RevisionSinCentro.Evaluar([new("6290001", 3, 120m), new("7520001", 2, -900m)]));
        Assert.Equal(5, e.Cantidad);
        Assert.Equal(1020m, e.Importe);
        Assert.Single(RevisionSinCentro.Evaluar([]).Correctas);
    }

    // ---------------- S8 deuda antigua

    [Fact]
    public void S8_AvisaDeLaParteDeLaDeudaDeMasDeSeisMeses()
    {
        var cartera = new[]
        {
            new ApunteCartera("4300001", new(2026, 1, 10), null, 1000m, 0m),
            new ApunteCartera("4300001", new(2026, 3, 10), null, 0m, 400m),          // el cobro salda lo más antiguo
            new ApunteCartera("4300001", new(2026, 10, 1), null, 300m, 0m),
            new ApunteCartera("4300002", new(2026, 10, 1), null, 500m, 0m),          // reciente
            new ApunteCartera("4360001", new(2025, 1, 1), null, 800m, 0m),           // dudoso cobro: no cuenta
        };
        var e = Unico(RevisionDeudaAntigua.Evaluar(cartera, Corte));
        Assert.Equal(["4300001"], e.Destino.Cuentas);
        Assert.Equal(600m, e.Importe);
        Assert.Empty(RevisionDeudaAntigua.Evaluar(cartera, Corte, new Dictionary<string, decimal?> { ["4300001"] = 900m }).Elementos);
    }

    // ---------------- S9 Seguridad Social

    [Fact]
    public void S9_AvisaDeCuotasSinPagarDeMasDeUnMesYDePrestacionesParadas()
    {
        var apuntes = new[]
        {
            A("4760001", new(2026, 9, 30), 0m, 800m), A("4760001", new(2026, 10, 31), 0m, 800m),   // septiembre sin pagar
            A("4710001", new(2026, 5, 31), 300m, 0m),                                               // prestación de mayo sin cobrar
        };
        var e = Unico(RevisionSeguridadSocial.Evaluar(apuntes, 2026, Corte));
        Assert.Equal(["4710001", "4760001"], e.Destino.Cuentas);
    }

    [Fact]
    public void S9_LaCuotaDelMesPasadoPendienteNoAvisa()
    {
        var apuntes = new[] { A("4760001", new(2026, 9, 30), 0m, 800m), A("4760001", new(2026, 10, 30), 800m, 0m), A("4760001", new(2026, 10, 31), 0m, 810m) };
        Assert.Empty(RevisionSeguridadSocial.Evaluar(apuntes, 2026, Corte).Elementos);
    }

    // ---------------- S10 compras duplicadas

    private static FilaFacturaCompra F(int id, string numero, DateTime fecha, decimal total, int proveedor = 7, bool duplicada = false) =>
        new() { IdRegistro = id, IdProveedor = proveedor, Proveedor = "Proveedor sintético", NumeroFactura = numero, Fecha = fecha, ImporteTotal = total, Duplicada = duplicada };

    [Fact]
    public void S10_AvisaDeFacturasDelMismoProveedorEImporteEnPocosDias()
    {
        var e = Unico(RevisionComprasDuplicadas.Evaluar(
        [
            F(1, "A-1", new(2026, 3, 1), 121m), F(2, "A-2", new(2026, 3, 4), 121m),     // posible duplicada
            F(3, "A-3", new(2026, 4, 1), 121m),                                           // a un mes: no
            F(4, "A-1", new(2026, 3, 2), 121m),                                           // mismo número: no es este caso
            F(5, "B-1", new(2026, 3, 2), 121m, proveedor: 8),                             // otro proveedor
            F(6, "A-9", new(2026, 3, 3), 121m, duplicada: true),                          // ya marcada
        ]));
        Assert.Equal(GravedadSalud.Aviso, e.Gravedad);
        Assert.Equal(2, e.Cantidad);
    }

    [Fact]
    public void ComisionesSinCompensar_AvisaPorPlataformaYSuBotonLasCompensa()
    {
        Assert.Empty(RevisionComisionesSinCompensar.Evaluar([]).Elementos);
        var e = Unico(RevisionComisionesSinCompensar.Evaluar(
        [
            new ProConta.Compras.Facturas.ComisionPendiente(1, "Plataforma A", "A-1", new(2026, 1, 25), 66.19m),
            new ProConta.Compras.Facturas.ComisionPendiente(2, "Plataforma A", "A-2", new(2026, 2, 24), 33.81m),
            new ProConta.Compras.Facturas.ComisionPendiente(3, "Plataforma B", "B-1", new(2026, 2, 16), 50m),
        ]));
        Assert.Equal(GravedadSalud.Atencion, e.Gravedad);
        Assert.Equal(3, e.Cantidad);
        Assert.Equal(150m, e.Importe);
        Assert.Equal(PantallaSalud.CompensarComisiones, e.Destino.Pantalla);
        Assert.Contains("Plataforma A: 2 facturas", e.Explicacion, StringComparison.Ordinal);
        Assert.Contains("comisiones-sin-compensar", ServicioSalud.Todas.Select(r => r.Clave));
    }

    [Fact]
    public void FacturasRecurrentes_AvisaConElNumeroYElImporteEstimadoSinLasDescartadas()
    {
        Assert.Empty(RevisionFacturasRecurrentes.Evaluar([]).Elementos);
        static ProConta.Compras.Recurrentes.FacturaRecurrentePendiente P(int prov, string nombre, int mes, decimal importe, bool descartada = false) =>
            new(prov, 100 + prov, nombre, ProConta.Terceros.Roles.PeriodicidadFacturas.Mensual, false, new(2026, mes, 1), new(2026, mes, 5), importe,
                new(2026, 8, 5), 1000 + prov, descartada);
        var e = Unico(RevisionFacturasRecurrentes.Evaluar([P(1, "Luz", 10, 80m), P(1, "Luz", 11, 80m), P(2, "Gestoría", 11, 121m), P(2, "Gestoría", 10, 121m, descartada: true)]));
        Assert.Equal(GravedadSalud.Atencion, e.Gravedad);
        Assert.Equal(3, e.Cantidad);
        Assert.Equal(281m, e.Importe);
        Assert.Equal("3 facturas recurrentes sin recibir", e.Titulo);
        Assert.Equal(PantallaSalud.FacturasRecurrentes, e.Destino.Pantalla);
        Assert.Equal("compras/facturas-recurrentes-pendientes", e.Nodo);
        Assert.Contains("Luz (2)", e.Explicacion, StringComparison.Ordinal);
        Assert.Empty(RevisionFacturasRecurrentes.Evaluar([P(2, "Gestoría", 10, 121m, descartada: true)]).Elementos);
        Assert.Contains("facturas-recurrentes", ServicioSalud.Todas.Select(r => r.Clave));
    }

    // ---------------- registro y saldo justificado en los avisos que ya existían

    [Fact]
    public void LasRevisionesNuevasEstanEnTodasYCadaUnaConSuNodo()
    {
        var claves = ServicioSalud.Todas.Select(r => r.Clave).ToList();
        Assert.Equal(claves.Count, claves.Distinct().Count());
        foreach (var c in new[] { "saldos-inmoviles", "iva-compensar-usado", "liquidacion-incompleta", "retenciones-4751", "pendientes-recibir", "nif-invalido", "sin-centro", "deuda-antigua", "seguridad-social", "compras-duplicadas" })
            Assert.Contains(c, claves);
    }

    [Fact]
    public void Los555YLosClientesAcreedoresCuadradosNoAvisan()
    {
        var cuadradas = new Dictionary<string, decimal?> { ["5550001"] = 40m, ["4300001"] = -25m };
        Assert.Empty(RevisionPartidasPendientes.Evaluar(new Dictionary<string, decimal> { ["5550001"] = 40m }, new HashSet<string>(), cuadradas).Elementos);
        Assert.Empty(RevisionClientesAcreedores.Evaluar(new Dictionary<string, decimal> { ["4300001"] = -25m }, cuadradas).Elementos);
        Assert.Single(RevisionClientesAcreedores.Evaluar(new Dictionary<string, decimal> { ["4300001"] = -30m }, cuadradas).Elementos);
    }

    [Fact]
    public async Task UnaFuenteSinLasLecturasNuevasNoRompeLaRevision()
    {
        var servicio = new ServicioSalud(new FuenteSaludFija(), hoy: () => Corte);
        var informe = await servicio.RevisarAsync("T01", 2026);
        Assert.DoesNotContain(informe.Revisiones, r => r.Error is not null);
    }
}
