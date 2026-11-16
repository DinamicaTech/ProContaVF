using ProConta.Datos.Esquema.Migraciones;
using ProConta.Norma43.Conciliacion;

namespace ProConta.Norma43.Tests;

/// <summary>
/// Tipos de movimiento (ConceptosN43, norma43/conciliacion 2026-11-13 10:50) con la semilla de la migración y textos con la forma de los
/// extractos reales (Santander 0049, La Caixa 2100); los nombres, NIF y referencias son inventados.
/// </summary>
public class ConceptosN43Tests
{
    private static readonly IReadOnlyList<ConceptoN43> Semilla = MigracionesConciliacionN43.SemillaConceptos
        .Select((r, i) => new ConceptoN43(i + 1, r.Codigo, r.Sentido, r.Entidad, r.Patron, r.PrefijoCuenta, r.Prioridad, r.Concepto, r.ConceptoReducido, r.Frase,
            r.NombreDe == "BANCO" ? NombreConceptoN43.Banco : NombreConceptoN43.Contrapartida))
        .ToList();

    private static ConceptoN43? Tipo(string codigo, string sentido, string entidad, string texto, string? cuenta = null) =>
        ReglasConceptoN43.Elegir(Semilla, new MovimientoConceptoN43(codigo, sentido, entidad, texto, cuenta));

    [Theory]
    // Estándar AEB.
    [InlineData("03", "D", "2100", "B00000000/01.COREEMPRESA FICTICIA SEGURIDAD S.L. ES63000B00000000 B00000000", "Recibo", "Recibo de")]
    [InlineData("15", "H", "2100", "Recibo SEPA Contrato Alquiler - Alquiler mensual Calle Inventada 01/01/2026 - 31/01/2026", "Domiciliación", "Domiciliación de")]
    [InlineData("04", "H", "0049", "00810901.JUAN DIAZ FICTICIO Atrasos 2025", "Transferencia", "Transferencia de")]
    [InlineData("04", "D", "2100", ".8630 56 0010000 01 000027 000001 EMPRESA FIC EMPRESA FICTICIA S.L. PAGO TRANSFER", "Transferencia", "Transferencia a")]
    [InlineData("12", "D", "0049", ".COMPRA INTERNET EN TIENDA FICTICIA, MADRID ES, TARJ. :*000000", "Tarjeta", "Pago con tarjeta a")]
    [InlineData("12", "H", "0049", ".DEVOLUCION COMPRA EN TIENDA FICTICIA, GAVA ES, TARJ. :*000000", "Tarjeta", "Devolución de compra con tarjeta de")]
    [InlineData("14", "D", "2100", "00000000.B00000000000 DETIR000000000 Empresa Ficticia PRES.UN.000000000 IMP.00000000974,00", "Devolución", "Devolución de recibo de")]
    [InlineData("02", "H", "2100", "BSABESBBXXX.CLIENTE FICTICIO, S.L. FACTURA 000001 EMPRESA FICTICIA", "Ingreso", "Ingreso de")]
    [InlineData("17", "D", "2100", ". MANTENIMENT 03600010EIC MANTENIMENT", "Gastos", "Cargo de")]
    [InlineData("09", "D", "0083", ".COMPRA DE 50 ETF FICTICIO A 592,9 EUR", "Valores", "Compra de valores a")]
    [InlineData("99", "D", "2100", "000000000000.EMPRESA FICTICIA S.L. Factura 0001 Obra ficticia", "Varios", "Pago a")]
    // Excepciones del Santander: 00 según el texto y 11 en las compras con tarjeta.
    [InlineData("00", "D", "0049", ".COMPRA INTERNET EN GASOLINERA FICTICIA, MADRID, TARJ. :*000000", "Tarjeta", "Pago con tarjeta a")]
    [InlineData("00", "H", "0049", ".DEVOLUCION COMPRA EN TIENDA FICTICIA, LAS PALMAS, TARJ. :*000000", "Tarjeta", "Devolución de compra con tarjeta de")]
    [InlineData("00", "H", "0049", ".TRANSFERENCIA DE PLATAFORMA FICTICIA S.A., CONCEPTO Reserva.", "Transferencia", "Transferencia de")]
    [InlineData("00", "D", "0049", ".TRANSFERENCIA A FAVOR DE LAVANDERIA FICTICIA CONCEPTO: F14-0000001", "Transferencia", "Transferencia a")]
    [InlineData("00", "D", "0049", ".RECIBO TGSS. COTIZACION 005 R.E.AUTONOMOS PERIODO LIQUIDACION: 11/2025-11/2025", "Recibo", "Recibo de")]
    [InlineData("00", "D", "0049", "B00000000.DOMICILIACION IMPUESTO: 2.025 IVA AUTOLIQUIDACION", "Impuesto", "Pago de impuestos a")]
    [InlineData("06", "D", "0049", "B00000000.DOMICILIACION IMPUESTO: 2.024 RETENCIONES E ING. A CTA. GGEE", "Impuesto", "Pago de impuestos a")]
    [InlineData("11", "D", "0049", ".COMPRA FERRETERIA FICTICIA SL, Gava (Barcelo, TARJETA 0000000000000000 , COMISION 0,00", "Tarjeta", "Pago con tarjeta a")]
    [InlineData("11", "H", "0049", ".DEVOLUCION COMPRA EN SOFTWARE FICTICIO, DUBLIN, TARJETA 0000000000000000 , COMISION 0,00", "Tarjeta", "Devolución de compra con tarjeta de")]
    // 17 con «.COR»: un recibo (impuestos municipales).
    [InlineData("17", "D", "2100", "010000000000.COREAJUNTAMENT FICTICI - I.B.I.URBANA ES10001P0000000G 010000000000 OTHRTAXS2025-IBI", "Recibo", "Recibo de")]
    public void ElTipoSaleDelConceptoComunYDeLasExcepciones(string codigo, string sentido, string entidad, string texto, string tipo, string frase)
    {
        var r = Tipo(codigo, sentido, entidad, texto);
        Assert.NotNull(r);
        Assert.Equal((tipo, frase), (r!.ConceptoReducido, r.Frase));
    }

    [Theory]
    [InlineData("04", "D", "2100", "4650003", "Nómina", "Pago nómina a")]
    [InlineData("04", "D", "0049", "6400000", "Nómina", "Pago nómina a")]
    [InlineData("04", "D", "2100", "5729999", "Traspaso", "Traspaso a")]
    [InlineData("00", "H", "0049", "5729999", "Traspaso", "Traspaso de")]
    [InlineData("17", "D", "2100", "6260003", "Comisión", "Comisión bancaria")]
    public void LaCuentaDeDestinoMandaSobreElCodigo(string codigo, string sentido, string entidad, string cuenta, string tipo, string frase)
    {
        var r = Tipo(codigo, sentido, entidad, ".TEXTO FICTICIO", cuenta);
        Assert.Equal((tipo, frase), (r!.ConceptoReducido, r.Frase));
        // Sin la cuenta (p. ej. en la rejilla antes de asignarla), el tipo es el del código.
        Assert.NotEqual(tipo, Tipo(codigo, sentido, entidad, ".TEXTO FICTICIO", null)?.ConceptoReducido);
    }

    [Theory]
    [InlineData("00", "D", "0049", ".LIQUIDACION DEL CONTRATO 0000000 300")]
    [InlineData("00", "D", "2100", ".COMPRA EN TIENDA FICTICIA TARJ.")] // las reglas del 00 son del Santander
    [InlineData("11", "D", "2100", ".COMPRA EN TIENDA FICTICIA TARJETA 0000")]
    public void SinReglaNoHayTipoNiSeSupone(string codigo, string sentido, string entidad, string texto)
    {
        Assert.Equal(codigo == "11" ? "Cajero" : null, Tipo(codigo, sentido, entidad, texto)?.ConceptoReducido);
        Assert.Equal("Pago a", ReglasConceptoN43.FraseSinTipo("D"));
        Assert.Equal("Cobro de", ReglasConceptoN43.FraseSinTipo("H"));
    }

    [Fact]
    public void ElPatronUsaComodinesYNoDistingueMayusculas()
    {
        var r = new ConceptoN43(1, null, null, null, "*recibo*", null, 1, "c", "Recibo", "Recibo de");
        Assert.True(ReglasConceptoN43.Encaja(r, new("00", "D", "0049", ".RECIBO IBERDROLA")));
        Assert.False(ReglasConceptoN43.Encaja(r, new("00", "D", "0049", ".TRANSFERENCIA")));
        var inicio = r with { PatronTexto = "B0*" };
        Assert.True(ReglasConceptoN43.Encaja(inicio, new("00", "D", "0049", "B00000000.DOMICILIACION")));
        Assert.False(ReglasConceptoN43.Encaja(inicio, new("00", "D", "0049", "XB0")));
    }

    [Fact]
    public void LaDescripcionEsLaFraseYElNombre()
    {
        var recibo = Tipo("03", "D", "2100", ".CORE")!;
        Assert.Equal("Recibo de TELEFONICA FICTICIA SAU", ServicioConciliacionN43.Descripcion(recibo, "D", ["TELEFONICA FICTICIA SAU"], "La Caixa EUR"));
        Assert.Equal("Recibo de Uno y otras", ServicioConciliacionN43.Descripcion(recibo, "D", ["Uno", "Dos"], "La Caixa EUR"));
        Assert.Equal("Recibo de", ServicioConciliacionN43.Descripcion(recibo, "D", [], null));
        var comision = Tipo("17", "D", "2100", ". MANTENIMENT", "6260003")!;
        Assert.Equal("Comisión bancaria La Caixa EUR", ServicioConciliacionN43.Descripcion(comision, "D", ["COMISION LA CAIXA - OTROS"], "La Caixa EUR"));
        Assert.Equal("Cobro de Cliente Ficticio", ServicioConciliacionN43.Descripcion(null, "H", ["Cliente Ficticio"], null));
        Assert.Equal(255, ServicioConciliacionN43.Descripcion(recibo, "D", [new string('X', 400)], null).Length);
    }

    [Fact]
    public void LasFacturasEnlazadasVanAlFinalDelConceptoYAlDocumento()
    {
        const string d = "Transferencia de Juan Diaz";
        Assert.Equal(d, ServicioConciliacionN43.ConceptoConFacturas(d, []));
        Assert.Null(ServicioConciliacionN43.DocumentoFacturas([]));
        Assert.Equal("Transferencia de Juan Diaz · Alquiler mensual Calle Inventada 03/2026",
            ServicioConciliacionN43.ConceptoConFacturas(d, [new("083050", "Alquiler mensual Calle Inventada 03/2026")]));
        Assert.Equal("Transferencia de Juan Diaz · Fra. 083050", ServicioConciliacionN43.ConceptoConFacturas(d, [new("083050", null)]));
        Assert.Equal("Transferencia de Juan Diaz · Fras. 083050, 083051", ServicioConciliacionN43.ConceptoConFacturas(d, [new("083050", "a"), new("083051", "b")]));
        Assert.Equal("083050, 083051", ServicioConciliacionN43.DocumentoFacturas([new("083050", "a"), new("083051", "b")]));
        var largo = ServicioConciliacionN43.ConceptoConFacturas(d, [new("1", new string('Y', 400))]);
        Assert.Equal(255, largo.Length);
        Assert.StartsWith(d + " · YYY", largo);
    }
}
