using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.Similares;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.Tests;

/// <summary>«Movimientos similares» (norma43/movimientos-similares) contra una copia de pruebas con datos inventados: qué líneas se comparan y asignar desde el diálogo.</summary>
[Trait("Categoria", "Access")]
public sealed class MovimientosSimilaresN43AccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly Dictionary<string, int> _identidades = [];

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioConciliacionN43 Conciliacion => new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd));

    private static Mov M(int mes, int dia, decimal importe, string concepto) => new(new DateTime(2026, mes, dia), importe, true, concepto, Comun: "02");

    /// <summary>Sociedad T01 (2026), cuentas inventadas, un banco y un extracto con los movimientos; devuelve los Id de las líneas en orden.</summary>
    private async Task<int[]> PrepararAsync(params Mov[] movs)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var catalogo = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("5720001", "Banco uno ficticio"), ("6280001", "Suministros ficticios"), ("6290001", "Otros servicios ficticios"), ("4300001", "Remesas ficticias") })
        {
            var c = await catalogo.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync("INSERT INTO CuentasBancarias (IdSociedad, EntidadOriginal, Oficina, Cuenta, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?)",
                "T01", "0049", "1500", "0012345678", "EUR", "Banco uno ficticio", true);
            var id = await s.UltimoIdAsync();
            return await s.EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, FechaDesde) VALUES (?, ?, ?)",
                id, _identidades["5720001"], new DateTime(2000, 1, 1));
        });
        var bytes = new FicheroSintetico().Cuenta("0049", "1500", "0012345678", movs.Min(m => m.Fecha), movs.Max(m => m.Fecha), 1000m, "EMPRESA FICTICIA", movs).Bytes();
        var r = await new ServicioImportacionN43(Bd).ImportarAsync(_ctx, bytes, "uno.n43", null, _actor);
        Assert.Equal(VeredictoImportacion.Importable, r.Veredicto);
        return (await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT IdLineaExtractoBancario FROM LineasExtractoBancario ORDER BY OrdinalMovimiento", f => f.Entero("IdLineaExtractoBancario")))).ToArray();
    }

    private sealed class Dialogos(Func<MovimientosSimilaresN43VM, EleccionSimilarN43?> elegir) : IDialogosLineasN43
    {
        public MovimientosSimilaresN43VM? Visto { get; private set; }
        public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) => opciones.Length - 1;
        public string? PedirMotivo(string titulo, string explicacion, string textoBoton) => null;
        public CuentaElegidaN43? ElegirCuenta(ContextoSociedadEjercicio ejercicio, string titulo, string? texto) => null;
        public void VerPropuesta(PropuestaLineaN43 propuesta) { }
        public void PalabrasClave(PalabrasClaveN43VM vm) { }
        public EleccionSimilarN43? MovimientosSimilares(MovimientosSimilaresN43VM vm) { Visto = vm; return elegir(vm); }
    }

    private async Task<int[]> PrepararConCuentasAsync()
    {
        var ids = await PrepararAsync(
            M(1, 10, 80.10m, "RECIBO LUZ FICTICIA HOGAR REF 0126"),
            M(2, 10, 82.40m, "RECIBO LUZ FICTICIA HOGAR REF 0226"),
            M(2, 12, 30m, "RECIBO AGUA MUNICIPAL FICTICIA"),
            M(2, 15, 900m, "REMESA RECIBOS FICTICIOS"),
            M(3, 10, 79.95m, "RECIBO LUZ FICTICIA HOGAR REF 0326"));
        var servicio = new ServicioLineasN43(Bd);
        Assert.Single((await servicio.AsignarCuentaAsync("T01", [ids[0]], _identidades["6280001"], _actor)).Hechas);
        Assert.Single((await servicio.AsignarCuentaAsync("T01", [ids[2]], _identidades["6290001"], _actor)).Hechas);
        // La remesa toma su cuenta por una palabra clave especial 5: no se compara.
        await Bd.EjecutarAsync(s => s.EjecutarAsync(
            "INSERT INTO PalabrasClaveBancarias (IdSociedad, Entidad, Sentido, PalabraClave, PalabraClave2, IdIdentidadContable, Especial, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            "T01", "0049", null, "REMESA RECIBOS", null, _identidades["4300001"], ReglasPalabrasClave.EspecialRemesa, true));
        await new AsignadorCuentasN43(Bd).AsignarAsync(_ctx);
        // La de enero, contabilizada: el resultado lleva su asiento.
        var c = await Conciliacion.ContabilizarLineasAsync("T01", [ids[0]], _actor);
        Assert.Equal(1, c.Hechas);
        return ids;
    }

    [Fact]
    public async Task Se_comparan_las_lineas_con_una_cuenta_sin_las_remesas_y_con_su_asiento()
    {
        var ids = await PrepararConCuentasAsync();
        var candidatos = await new ConsultaSimilaresN43(Bd).CandidatosAsync("T01", 2024, 2026);
        Assert.Equal([ids[0], ids[2]], candidatos.Select(c => c.IdLinea).OrderBy(i => i));
        var luz = candidatos.Single(c => c.IdLinea == ids[0]);
        Assert.Equal(("6280001", "Suministros ficticios", 2026), (luz.Cuenta, luz.NombreCuenta, luz.Ejercicio));
        Assert.NotNull(luz.IdAsiento);
        Assert.Null(candidatos.Single(c => c.IdLinea == ids[2]).IdAsiento);
        Assert.Empty(await new ConsultaSimilaresN43(Bd).CandidatosAsync("T01", 2023, 2025));

        var linea = Assert.Single(await new ConsultaLineasN43(Bd).LineasAsync("T01", [2026], [ids[1]]));
        var r = ReglasSimilaresN43.Buscar(linea, candidatos);
        Assert.Equal(ids[0], r[0].Linea.IdLinea);
        Assert.Equal(100, r[0].Porcentaje);
        Assert.Equal(ids[0], Assert.Single(ReglasSimilaresN43.Avance(linea, candidatos)).Linea.IdLinea);
    }

    [Fact]
    public async Task Asignar_desde_el_dialogo_pone_la_cuenta_a_todas_las_seleccionadas()
    {
        var ids = await PrepararConCuentasAsync();
        var dialogos = new Dialogos(vm => new EleccionSimilarN43(AccionSimilarN43.Asignar, vm.Resultados[0].Datos));
        var avisos = new List<string>();
        var pantalla = new LineasN43VM(new ConsultaLineasN43(Bd), new ServicioLineasN43(Bd), Conciliacion, new AsignadorCuentasN43(Bd), dialogos,
            new ContextoLineasN43(_ctx, "Sociedad sintética", _actor, avisos.Add), similares: new ConsultaSimilaresN43(Bd));
        await pantalla.IniciarAsync();
        pantalla.CambiarSeleccion(pantalla.Filas.Where(f => f.Id == ids[1] || f.Id == ids[4]).ToList());
        Assert.True(pantalla.AccionSimilares.Habilitada, pantalla.AccionSimilares.Ayuda);

        await pantalla.MovimientosSimilaresAsync();

        Assert.NotNull(dialogos.Visto);
        Assert.Contains("2 líneas seleccionadas", dialogos.Visto!.Alcance);
        var lineas = await new ConsultaLineasN43(Bd).LineasAsync("T01", [2026], [ids[1], ids[4]]);
        Assert.All(lineas, l => Assert.Equal(("6280001", ServicioLineasN43.OrigenManual), (l.CuentaDestino, Assert.Single(l.Destinos).Origen)));
        Assert.Contains(avisos, a => a.StartsWith("6280001 asignada a 2 líneas", StringComparison.Ordinal));

        // Una remesa no se compara: la opción sale desactivada con su motivo.
        pantalla.CambiarSeleccion(pantalla.Filas.Where(f => f.Id == ids[3]).ToList());
        Assert.False(pantalla.AccionSimilares.Habilitada);
        Assert.Contains("remesa", pantalla.AccionSimilares.Ayuda, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ir_a_la_linea_quita_los_filtros_y_la_selecciona()
    {
        var ids = await PrepararConCuentasAsync();
        var pantalla = new LineasN43VM(new ConsultaLineasN43(Bd), new ServicioLineasN43(Bd), Conciliacion, new AsignadorCuentasN43(Bd),
            new Dialogos(vm => new EleccionSimilarN43(AccionSimilarN43.IrALinea, vm.Resultados[0].Datos)),
            new ContextoLineasN43(_ctx, "Sociedad sintética", _actor, _ => { }), similares: new ConsultaSimilaresN43(Bd));
        await pantalla.IniciarAsync();
        pantalla.TextoFiltro = "0226";
        var fila = Assert.Single(pantalla.Filas);
        pantalla.CambiarSeleccion([fila]);
        pantalla.Actual = fila;

        await pantalla.MovimientosSimilaresAsync();

        Assert.Equal("", pantalla.TextoFiltro);
        Assert.Equal([ids[0]], pantalla.IdsSeleccionados);
        Assert.Equal(ids[0], pantalla.Actual!.Id);
    }
}
