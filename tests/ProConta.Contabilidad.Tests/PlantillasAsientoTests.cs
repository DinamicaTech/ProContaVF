using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Esquema.Migraciones;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;
using ProConta.Nucleo.Resultados;

namespace ProConta.Contabilidad.Tests;

/// <summary>Evaluador de fórmulas de las plantillas (contabilidad/plantillas-asiento 2026-11-12 11:15, «Fórmulas»).</summary>
public class FormulasPlantillaTests
{
    private static ValorFormula E(string formula, params (string Nombre, object? Valor)[] nombres)
    {
        var d = nombres.ToDictionary(n => n.Nombre, n => n.Valor switch
        {
            decimal x => ValorFormula.Numero(x),
            int i => ValorFormula.Numero(i),
            bool b => ValorFormula.Logico(b),
            null => ValorFormula.Vacio,
            _ => ValorFormula.Texto(n.Valor.ToString()),
        }, StringComparer.OrdinalIgnoreCase);
        return Formula.Evaluar(formula, new EntornoDiccionario(d, () => "5510007"));
    }

    [Fact]
    public void AritmeticaEnDecimalConPrecedenciaYParentesis()
    {
        Assert.Equal(0.3m, E("0,1 + 0.2").Dato);
        Assert.Equal(14m, E("2 + 3 * 4").Dato);
        Assert.Equal(20m, E("(2 + 3) × 4").Dato);
        Assert.Equal(2.5m, E("5 ÷ 2").Dato);
        Assert.Equal(-7m, E("-(3 + 4)").Dato);
        Assert.Equal(1573m, E("Bruto - SSTrab - IRPF111", ("Bruto", 2000m), ("SSTrab", 127m), ("IRPF111", 300m)).Dato);
        Assert.IsType<decimal>(E("1 / 3").Dato);
    }

    [Fact]
    public void RedondearEsMitadHaciaArribaADosDecimales()
    {
        Assert.Equal(2.35m, E("Redondear(2,345)").Dato);
        Assert.Equal(-2.35m, E("Redondear(-2,345)").Dato);
        Assert.Equal(33.33m, E("Redondear(100 / 3)").Dato);
        Assert.Equal(5m, E("Abs(-5)").Dato);
    }

    [Fact]
    public void SiComparacionesYTextos()
    {
        Assert.Equal(121m, E("Si(Lado = \"d\"; Importe; -Importe)", ("Lado", "D"), ("Importe", 121m)).Dato);
        Assert.Equal(-121m, E("Si(Lado = \"D\"; Importe; -Importe)", ("Lado", "H"), ("Importe", 121m)).Dato);
        Assert.Equal(true, E("Importe >= 100 Y Importe <> 0", ("Importe", 121m)).Dato);
        Assert.Equal(true, E("No (Importe < 100) O Falso", ("Importe", 121m)).Dato);
        Assert.Equal("Anulación: Factura \"A\"", E("\"Anulación: \" & Concepto", ("Concepto", "Factura \"A\"")).Dato);
        Assert.Equal("Cuenta 4300001", E("\"Cuenta \" + Cuenta", ("Cuenta", "4300001")).Dato);
        Assert.Equal("Total 121,50", E("\"Total \" & Importe", ("Importe", 121.5m)).Dato);
        Assert.Equal("4760012", E("Subcuenta(\"4760?\"; CuentaTrabajador)", ("CuentaTrabajador", "4650012")).Dato);
        Assert.Equal("5510007", E("UltimaUsada()").Dato);
    }

    [Theory]
    [InlineData("Importe +", "incompleta")]
    [InlineData("Desconocido * 2", "Desconocido")]
    [InlineData("1 / 0", "cero")]
    [InlineData("\"sin cerrar", "comilla")]
    [InlineData("Raiz(4)", "Raiz")]
    [InlineData("Si(1; 2)", "Si necesita 3")]
    [InlineData("(1 + 2", "falta")]
    public void LosErroresDicenQueFallaYComoCorregirlo(string formula, string causa)
    {
        var e = Assert.Throws<ErrorFormula>(() => E(formula, ("Importe", 1m)));
        Assert.Contains(causa, e.Causa + e.Message);
        Assert.False(string.IsNullOrWhiteSpace(e.Accion));
    }

    [Fact]
    public void CuentaConInterrogacionSeCompletaConLosUltimosDigitos()
    {
        Assert.Equal("4760012", CuentasPlantilla.Completar("4760?", "4650012"));
        Assert.Equal("476012", CuentasPlantilla.Completar("476?", "465012"));
        Assert.Equal("5700000", CuentasPlantilla.Completar("5700000", "4650012"));
    }
}

/// <summary>Catálogo inicial y motor de plantillas sin BD (contabilidad/plantillas-asiento, aceptación).</summary>
public class MotorPlantillasTests
{
    private static readonly IReadOnlyList<PlantillaAsiento> _catalogo = PlantillasJson.LeerCatalogo(SemillaPlantillasAsiento.Json);

    private static PlantillaAsiento P(string nombre) => _catalogo.Single(p => p.Nombre == nombre);

    private static IEnumerable<(string Cuenta, decimal Debe, decimal Haber, string? Concepto)> Lineas(AsientoPropuesto a) =>
        a.Apuntes.Select(x => (x.Cuenta, x.Debe, x.Haber, x.Concepto));

    private static PropuestaPlantilla Vista(string nombre)
    {
        var r = ServicioPlantillasAsiento.VistaPrevia(P(nombre));
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        Assert.All(r.Valor.Asientos, a => Assert.True(a.Cuadrado, $"{nombre}: asiento {a.NumeroAsiento} descuadrado"));
        Assert.All(r.Valor.Asientos, a => Assert.True(a.Apuntes.Count >= 2));
        return r.Valor;
    }

    [Fact]
    public void ElCatalogoInicialTieneSeisPlantillasDeUsuarioYLaNominaDeSistemaConUnSoloNombreCadaUna()
    {
        Assert.Equal(
            ["Anular asiento", "Anular y rehacer con otra cuenta", "Pasar saldo a otra cuenta", "Ajustar saldo inicial contra la 113", "Pago por tesorería",
             "Pago por un socio", "Nómina"],
            _catalogo.Select(p => p.Nombre));
        Assert.Equal(_catalogo.Count, _catalogo.Select(p => p.Nombre).Distinct().Count());
        Assert.All(_catalogo.Take(6), p => Assert.Equal((TipoPlantilla.Usuario, OrigenPlantilla.Apunte), (p.Tipo, p.Origen)));
        var nomina = P("Nómina");
        Assert.Equal((TipoPlantilla.Sistema, OrigenPlantilla.Proceso, "nominas", 0), (nomina.Tipo, nomina.Origen, nomina.ClaveProceso, nomina.Lineas.Count));
        Assert.All(_catalogo, p => Assert.True(MotorPlantillas.Validar(p).EsCorrecto, p.Nombre));
        Assert.All(_catalogo, p => Assert.False(string.IsNullOrWhiteSpace(p.Descripcion)));
        Assert.All(_catalogo, p => Assert.False(string.IsNullOrWhiteSpace(p.Ejemplo)));
        // «Modificar apunte» no es una plantilla, y los nombres cruzados del ERP («Contra-apunte», «Nuevo apunte con otra cuenta») desaparecen.
        Assert.DoesNotContain(_catalogo, p => p.Nombre is "Modificar apunte" or "Contra-apunte" or "Nuevo apunte con otra cuenta");
    }

    [Fact]
    public void AnularAsientoInvierteTodosLosApuntesEnElTrimestreActivo()
    {
        var a = Assert.Single(Vista("Anular asiento").Asientos);
        Assert.Equal(new DateTime(2026, 4, 1), a.Fecha);
        Assert.Equal([("6220001", 0m, 242m, "Anulación: Reparación caldera Ejemplo, S.L."), ("5720001", 242m, 0m, "Anulación: Reparación caldera Ejemplo, S.L.")], Lineas(a));
        Assert.All(a.Apuntes, x => Assert.Equal("F-2026-015", x.Documento));
        Assert.Equal("Anulación: Reparación caldera Ejemplo, S.L.", a.Descripcion);
    }

    [Fact]
    public void AnularYRehacerGeneraElInversoYElOriginalConLaCuentaNueva()
    {
        var v = Vista("Anular y rehacer con otra cuenta");
        Assert.Equal(2, v.Asientos.Count);
        Assert.Equal([("6220001", 0m, 242m, "Anulación: Reparación caldera Ejemplo, S.L."), ("5720001", 242m, 0m, "Anulación: Reparación caldera Ejemplo, S.L.")], Lineas(v.Asientos[0]));
        Assert.Equal([("6290001", 242m, 0m, "Rehecho: Reparación caldera Ejemplo, S.L."), ("5720001", 0m, 242m, "Rehecho: Reparación caldera Ejemplo, S.L.")], Lineas(v.Asientos[1]));
        Assert.All(v.Asientos, a => Assert.Equal(new DateTime(2026, 4, 1), a.Fecha));
    }

    [Fact]
    public void PasarSaldoLlevaLaCuentaAlLadoContrarioYLaNuevaAlMismoLado()
    {
        var a = Assert.Single(Vista("Pasar saldo a otra cuenta").Asientos);
        Assert.Equal([("4300001", 0m, 500m, "Traspaso de saldo: Saldo inicial Cliente Ejemplo A"), ("4300002", 500m, 0m, "Traspaso de saldo: Saldo inicial Cliente Ejemplo A")], Lineas(a));
        Assert.Equal(new DateTime(2026, 4, 1), a.Fecha);
    }

    [Fact]
    public void AjusteDeSaldoInicialVaContraLa113PositivoAlHaber()
    {
        var a = Assert.Single(Vista("Ajustar saldo inicial contra la 113").Asientos);
        Assert.Equal([("4300003", 0m, 150m, "Regularización saldo inicial"), ("1130000", 150m, 0m, "Regularización saldo inicial cliente 4300003")], Lineas(a));

        // En negativo (la contabilidad tiene menos saldo que el real) la cuenta se carga y la 113 se abona.
        var ej = PlantillasJson.LeerEjemplo(P("Ajustar saldo inicial contra la 113").Ejemplo!);
        var r = MotorPlantillas.Proponer(P("Ajustar saldo inicial contra la 113"), ej.Origen, new Dictionary<string, string?> { ["ImporteAjuste"] = "-20,50" }, ej.Entorno);
        Assert.Equal([("4300003", 20.5m, 0m, "Regularización saldo inicial"), ("1130000", 0m, 20.5m, "Regularización saldo inicial cliente 4300003")], Lineas(r.Valor.Asientos[0]));
    }

    [Fact]
    public void PagoPorTesoreriaVaContraLa570YConservaLaFechaDelApunteSiEsPosterior()
    {
        var a = Assert.Single(Vista("Pago por tesorería").Asientos);
        Assert.Equal([("4000001", 121m, 0m, "Pago por tesorería: Factura F-77 Suministros Ejemplo, S.A. (ejemplo)"), ("5700000", 0m, 121m, "Pago por tesorería: Factura F-77 Suministros Ejemplo, S.A. (ejemplo)")], Lineas(a));
        Assert.Equal(new DateTime(2026, 5, 6), a.Fecha);
        Assert.All(a.Apuntes, x => Assert.Equal("F-77", x.Documento));
        Assert.Equal("Pasamos pago a caja/tesorería", P("Pago por tesorería").MotivoPropuesto);
    }

    [Fact]
    public void PagoPorUnSocioVaContraLaSubcuenta551PropuestaLaUltimaUsada()
    {
        var a = Assert.Single(Vista("Pago por un socio").Asientos);
        Assert.Equal([("4000001", 121m, 0m, "Pago por un socio: Factura F-77 Suministros Ejemplo, S.A. (ejemplo)"), ("5510001", 0m, 121m, "Pagado por socio: Factura F-77 Suministros Ejemplo, S.A. (ejemplo)")], Lineas(a));

        var p = P("Pago por un socio");
        var ej = PlantillasJson.LeerEjemplo(p.Ejemplo!);
        Assert.Equal("5510001", MotorPlantillas.ValoresPropuestos(p, ej.Origen, ej.Entorno)["CuentaSocio"]);
        var fuera = MotorPlantillas.Proponer(p, ej.Origen, new Dictionary<string, string?> { ["CuentaSocio"] = "4000001" }, ej.Entorno);
        Assert.Equal("Plantillas.CuentaFueraDeFiltro", fuera.Error!.Codigo);
        var sinSocio = MotorPlantillas.Proponer(p, ej.Origen, null, ej.Entorno with { UltimasCuentas = null });
        Assert.Equal("Plantillas.FaltaDato", sinSocio.Error!.Codigo);
        Assert.Contains("551", sinSocio.Error.AccionCorrectora);
    }

    [Fact]
    public void LaNominaDeSistemaNoTieneVistaPreviaHastaQueTengaLineas()
    {
        var r = ServicioPlantillasAsiento.VistaPrevia(P("Nómina"));
        Assert.Equal("Plantillas.SinLineas", r.Error!.Codigo);
        Assert.Contains("PlantillasApuntes", r.Error.Causa);
    }

    [Fact]
    public void ConFacturaOAsientoAutomaticoNoSeAnulaNiSeRehaceYElAvisoRemiteALaFactura()
    {
        var ej = PlantillasJson.LeerEjemplo(P("Pago por tesorería").Ejemplo!); // asiento de factura (libro de IVA)
        foreach (var nombre in new[] { "Anular asiento", "Anular y rehacer con otra cuenta" })
        {
            var r = MotorPlantillas.Proponer(P(nombre), ej.Origen, new Dictionary<string, string?> { ["NuevaCuenta"] = "6280001" }, ej.Entorno);
            Assert.Equal("Plantillas.AsientoDeFactura", r.Error!.Codigo);
            Assert.Contains("desde la factura", r.Error.AccionCorrectora);
        }
        // Pagar por caja una factura sí se puede: no toca su libro de IVA.
        Assert.True(MotorPlantillas.Proponer(P("Pago por tesorería"), ej.Origen, null, ej.Entorno).EsCorrecto);

        var auto = PlantillasJson.LeerEjemplo(P("Anular asiento").Ejemplo!.Replace("\"apuntes\"", "\"automatico\": true, \"apuntes\""));
        Assert.Equal("Plantillas.AsientoAutomatico", MotorPlantillas.Proponer(P("Anular asiento"), auto.Origen, null, auto.Entorno).Error!.Codigo);
    }

    [Fact]
    public void SinCuentaDeCuadresOTesoreriaElErrorDiceDondeSeConfigura()
    {
        var ej = PlantillasJson.LeerEjemplo(P("Ajustar saldo inicial contra la 113").Ejemplo!);
        var r = MotorPlantillas.Proponer(P("Ajustar saldo inicial contra la 113"), ej.Origen, null, ej.Entorno with { CuentaCuadres = null });
        Assert.Equal("Plantillas.SinCuentaCuadres", r.Error!.Codigo);
        Assert.Contains("Parámetros › Empresas", r.Error.AccionCorrectora);

        var t = PlantillasJson.LeerEjemplo(P("Pago por tesorería").Ejemplo!);
        var r2 = MotorPlantillas.Proponer(P("Pago por tesorería"), t.Origen, null, t.Entorno with { CuentaTesoreria = null });
        Assert.Equal("Plantillas.SinCuentaTesoreria", r2.Error!.Codigo);
        Assert.Contains("Tesorería (570)", r2.Error.AccionCorrectora);
    }

    [Fact]
    public void FechaPrimerDiaDelTrimestreActivoOEneroSinTrimestreYEditable()
    {
        var p = P("Anular asiento");
        var ej = PlantillasJson.LeerEjemplo(p.Ejemplo!);
        // Sin trimestre activo, el 1 de enero; pero el apunte de origen (10/02) es posterior, así que se usa su fecha.
        Assert.Equal(new DateTime(2026, 1, 1), MotorPlantillas.InicioPeriodo(ej.Entorno with { TrimestreActivo = null }));
        Assert.Equal(new DateTime(2026, 2, 10), MotorPlantillas.Proponer(p, ej.Origen, null, ej.Entorno with { TrimestreActivo = null }).Valor.Asientos[0].Fecha);
        Assert.Equal(new DateTime(2026, 10, 1), MotorPlantillas.Proponer(p, ej.Origen, null, ej.Entorno with { TrimestreActivo = 4 }).Valor.Asientos[0].Fecha);
        Assert.Equal(new DateTime(2026, 6, 30), MotorPlantillas.Proponer(p, ej.Origen, null, ej.Entorno, new DateTime(2026, 6, 30)).Valor.Asientos[0].Fecha);
    }

    [Fact]
    public void DatosQueFaltanOCuentasQueNoExistenDicenQueHacer()
    {
        var p = P("Anular y rehacer con otra cuenta");
        var ej = PlantillasJson.LeerEjemplo(p.Ejemplo!);
        var falta = MotorPlantillas.Proponer(p, ej.Origen, new Dictionary<string, string?>(), ej.Entorno);
        Assert.Equal("Plantillas.FaltaDato", falta.Error!.Codigo);
        Assert.Contains("Nueva cuenta", falta.Error.Mensaje);
        var noExiste = MotorPlantillas.Proponer(p, ej.Origen, new Dictionary<string, string?> { ["NuevaCuenta"] = "6999999" }, ej.Entorno);
        Assert.Equal("Plantillas.CuentaNoExiste", noExiste.Error!.Codigo);
        Assert.Contains("Cuentas contables", noExiste.Error.AccionCorrectora);
        var cero = MotorPlantillas.Proponer(P("Pago por tesorería"), PlantillasJson.LeerEjemplo(P("Pago por tesorería").Ejemplo!).Origen,
            new Dictionary<string, string?> { ["ImportePago"] = "0" }, PlantillasJson.LeerEjemplo(P("Pago por tesorería").Ejemplo!).Entorno);
        Assert.Equal("Plantillas.SinApuntes", cero.Error!.Codigo);
        Assert.Equal("Plantillas.FaltaApunte", MotorPlantillas.Proponer(p, OrigenEjecucion.Ninguno, null, ej.Entorno).Error!.Codigo);
    }

    /// <summary>Plantilla de proceso como la de nóminas: cuenta del trabajador, cuentas con «?», condición y agrupación por cuenta.</summary>
    [Fact]
    public void PlantillaDeProcesoConInterrogacionCondicionYAgrupacion()
    {
        var nomina = P("Nómina") with
        {
            Lineas =
            [
                new(1, 0, "CuentaTrabajador", "H", "Neto", "\"Nómina \" & Clave"),
                new(1, 1, "\"6400000\"", "D", "Bruto", "\"Sueldos \" & Clave"),
                new(1, 2, "\"6420000\"", "D", "SSEmp", "\"S.S. empresa\""),
                new(1, 3, "\"4751000\"", "H", "IRPF111", "\"IRPF\"", Condicion: "IRPF111 > 0"),
                new(1, 4, "\"476?\"", "H", "SSEmp + SSTrab", "\"Seguridad Social\""),
            ],
        };
        Assert.True(MotorPlantillas.Validar(nomina).EsCorrecto);
        var ctx = new ContextoSociedadEjercicio("T01", 1, 2026);
        var cuentasExistentes = new Dictionary<string, string> { ["4650001"] = "Trabajador Uno", ["4650002"] = "Trabajador Dos", ["6400000"] = "", ["6420000"] = "", ["4751000"] = "", ["4760001"] = "" };
        var entorno = new EntornoSociedad(ctx, Cuentas: cuentasExistentes);
        RegistroProceso R(string clave, string cuenta, decimal bruto, decimal irpf) => new(clave, new DateTime(2026, 1, 31), new Dictionary<string, object?>
        {
            ["CuentaTrabajador"] = cuenta, ["Bruto"] = bruto, ["SSEmp"] = 640m, ["SSTrab"] = 127m, ["IRPF111"] = irpf, ["Neto"] = bruto - 127m - irpf,
        });
        var origen = new OrigenProceso("nominas", [R("Uno", "4650001", 2000m, 300m), R("Dos", "4650002", 1500m, 0m)]);

        var porTrabajador = MotorPlantillas.Proponer(nomina, origen, null, entorno);
        Assert.True(porTrabajador.EsCorrecto, string.Join(" | ", porTrabajador.Errores));
        Assert.Equal(2, porTrabajador.Valor.Asientos.Count);
        Assert.All(porTrabajador.Valor.Asientos, a => Assert.True(a.Cuadrado));
        Assert.All(porTrabajador.Valor.Asientos, a => Assert.Equal(new DateTime(2026, 1, 31), a.Fecha));
        Assert.Equal(["4650001", "6400000", "6420000", "4751000", "4760001"], porTrabajador.Valor.Asientos[0].Apuntes.Select(x => x.Cuenta));
        Assert.Equal(["4650002", "6400000", "6420000", "4760002"], porTrabajador.Valor.Asientos[1].Apuntes.Select(x => x.Cuenta)); // sin IRPF: no hay línea
        Assert.Equal(("4760002", "Trabajador Dos"), Assert.Single(porTrabajador.Valor.CuentasNuevas));

        var agrupada = MotorPlantillas.Proponer(nomina with { AgruparPorCuenta = true }, origen, null, entorno);
        var unico = Assert.Single(agrupada.Valor.Asientos);
        Assert.True(unico.Cuadrado);
        Assert.Equal(3500m, unico.Apuntes.Single(x => x.Cuenta == "6400000").Debe);
        Assert.Equal(1280m, unico.Apuntes.Single(x => x.Cuenta == "6420000").Debe);
        Assert.Equal(2, unico.Apuntes.Count(x => x.Cuenta.StartsWith("465")));
    }

    [Fact]
    public async Task ProponerNoEscribeYUnaPlantillaDeSistemaNoSeEjecutaAMano()
    {
        var motor = new MotorQueNoDebeUsarse();
        var servicio = new ServicioPlantillasAsiento(new BdQueNoDebeUsarse(), motor, null!);
        var nomina = P("Nómina");
        var propuesta = new PropuestaPlantilla(nomina, new ContextoSociedadEjercicio("T01", 1, 2026),
            [new AsientoPropuesto(1, new DateTime(2026, 1, 31), "Nómina", [new("4650001", false, 10m, null, null, null), new("6400000", true, 10m, null, null, null)])],
            new Dictionary<string, string?>());
        var r = await servicio.EjecutarAsync(propuesta, "Prueba", new Actor("prueba"));
        Assert.Equal("Plantillas.SistemaManual", r.Error!.Codigo);
        Assert.Contains("Nóminas", r.Error.AccionCorrectora);

        var anular = Vista("Anular asiento");
        var sinMotivo = await servicio.EjecutarAsync(anular with { IdApunteOrigen = null }, "  ", new Actor("prueba"));
        Assert.Equal("Plantillas.SinMotivo", sinMotivo.Error!.Codigo);
        var inicial = await servicio.EjecutarAsync(anular with { IdApunteOrigen = null }, "**Escribe aquí el motivo", new Actor("prueba"));
        Assert.Equal("Plantillas.SinMotivo", inicial.Error!.Codigo);
        Assert.Equal(0, motor.Llamadas);
    }

    [Fact]
    public void UnaFormulaMalEscritaSeExplica()
    {
        var mala = P("Pago por tesorería") with { Lineas = [new(1, 1, "Cuenta", "CONTRARIO", "ImportePago *", null), new(1, 2, "CuentaTesoreria", "Lateral", "ImportePago", null)] };
        var v = MotorPlantillas.Validar(mala);
        Assert.Contains(v.Errores, e => e.Codigo == "Plantillas.FormulaNoValida" && e.AccionCorrectora!.Contains("mantenimiento de plantillas"));
        var mismo = P("Pago por tesorería") with { Origen = OrigenPlantilla.Ninguno, Datos = [], Lineas = [new(1, 1, "\"5700000\"", "MISMO", "10", null), new(1, 2, "\"4000001\"", "D", "10", null)] };
        var r = MotorPlantillas.Proponer(mismo, OrigenEjecucion.Ninguno, null, new EntornoSociedad(new ContextoSociedadEjercicio("T01", 1, 2026)));
        Assert.Equal("Plantillas.LadoNoValido", r.Error!.Codigo);
    }

    private sealed class BdQueNoDebeUsarse : IBaseDatos
    {
        public string Ruta => "sin-bd";
        public Task<T> EjecutarAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) => throw new InvalidOperationException("No debe tocar la BD.");
        public Task<T> EnTransaccionAsync<T>(Func<ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) => throw new InvalidOperationException("No debe tocar la BD.");
    }

    private sealed class MotorQueNoDebeUsarse : IMotorAsientos
    {
        public int Llamadas { get; private set; }
        private T No<T>() { Llamadas++; throw new InvalidOperationException("No debe grabar nada."); }
        public Task<Asiento?> ObtenerAsync(int idAsiento) => No<Task<Asiento?>>();
        public Task<Resultado<Asiento>> GuardarManualAsync(Asiento asiento, Actor actor, string? motivo = null) => No<Task<Resultado<Asiento>>>();
        public Task<Resultado<IReadOnlyList<int>>> GuardarManualesAsync(IReadOnlyList<Asiento> asientos, Actor actor, string motivo, OpcionesAltaManual? opciones = null) => No<Task<Resultado<IReadOnlyList<int>>>>();
        public Task<Resultado> EliminarManualAsync(int idAsiento, int versionLeida, Actor actor, string motivo) => No<Task<Resultado>>();
        public Task<Resultado<IReadOnlyList<int>>> ReemplazarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, IReadOnlyList<Asiento> asientos, Actor actor, string motivo,
            TrabajoLote? enLaTransaccion = null, OpcionesLote? opciones = null) => No<Task<Resultado<IReadOnlyList<int>>>>();
        public Task<Resultado<int>> EliminarLoteAsync(ContextoSociedadEjercicio contexto, IdentidadLote lote, Actor actor, string motivo, TrabajoLote? enLaTransaccion = null,
            OpcionesLote? opciones = null) => No<Task<Resultado<int>>>();
        public Task<Resultado<Guid>> CambiarCuentaApuntesAsync(ContextoSociedadEjercicio contexto, IReadOnlyCollection<int> idsApunte, string nuevaCuenta, Actor actor, string motivo) => No<Task<Resultado<Guid>>>();
        public Task<Resultado<Guid>> PuntearApuntesAsync(IReadOnlyCollection<int> idsApunte, bool punteado, Actor actor, string? motivo = null) => No<Task<Resultado<Guid>>>();
        public Task<Resultado<Guid>> AsignarNumeroDocumentoAsync(IReadOnlyCollection<int> idsApunte, string? numero, Actor actor, string motivo) => No<Task<Resultado<Guid>>>();
        public Task<Resultado<Guid>> CambiarConceptoApuntesAsync(IReadOnlyCollection<int> idsApunte, string? concepto, Actor actor, string motivo) => No<Task<Resultado<Guid>>>();
        public Task<IReadOnlyList<CambioApunte>> RegistroCambiosAsync(int idAsiento) => No<Task<IReadOnlyList<CambioApunte>>>();
        public Task RecalcularSaldosAsync(ContextoSociedadEjercicio contexto) => No<Task>();
        public Task<Resultado<ResultadoCargaLibro>> CargarLibroExternoAsync(ContextoSociedadEjercicio contexto, string motorPropietario, string codigoProceso,
            IReadOnlyList<AsientoExterno> asientos, Actor actor, string motivo, Func<ISesionDatos, IReadOnlyList<int>, Task>? antesDeBorrar = null,
            CancellationToken cancelacion = default) => No<Task<Resultado<ResultadoCargaLibro>>>();
    }
}
