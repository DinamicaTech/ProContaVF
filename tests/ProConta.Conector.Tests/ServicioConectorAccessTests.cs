using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

// Las pruebas contra Access se ejecutan de una en una: una sola copia de ACE por proceso.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ProConta.Conector.Tests;

/// <summary>Conector de Claude Desktop contra una copia de la BD de pruebas (plataforma/conector-claude).</summary>
[Trait("Categoria", "Access")]
public sealed class ServicioConectorAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioConector Servicio() => new(Bd, "operador.prueba", () => new DateTime(2026, 11, 16));

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("6220001", "Reparaciones sintéticas"), ("6290001", "Otros servicios"), ("5720001", "Banco sintético"), ("4720021", "IVA soportado 21") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
    }

    private async Task<Asiento> ManualAsync(string debe = "6220001", decimal importe = 121m)
    {
        var a = new Asiento(_ctx, new DateTime(2026, 1, 20), "Reparación sintética");
        a.Apuntes.AddRange([Apunte.AlDebe(debe, importe), Apunte.AlHaber("5720001", importe)]);
        var r = await new MotorAsientosAccess(Bd).GuardarManualAsync(a, new Actor("operador.prueba"));
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        return r.Valor;
    }

    private Task<int> ContarAsync(string sql, params object?[] p) =>
        Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private Task<int> AsientosAsync() => ContarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = 9001");

    private static PeticionPropuesta Correccion(int origen) => new("T01", 2026, "La reparación era de otros servicios", [
        new AsientoPedido(new DateTime(2026, 1, 20), "Anula el asiento de la reparación", [new ApuntePedido("5720001", 121m, null), new ApuntePedido("6220001", null, 121m)], Anula: true),
        new AsientoPedido(new DateTime(2026, 1, 20), "Reparación corregida", [new ApuntePedido("6290001", 100m, null), new ApuntePedido("4720021", 21m, null), new ApuntePedido("5720001", null, 121m)]),
    ], origen);

    [Fact]
    public async Task LeeSociedadesAsientosCuentasYMayor()
    {
        await PrepararAsync();
        var origen = await ManualAsync();
        var s = Servicio();

        var sociedades = await s.SociedadesAsync();
        Assert.Contains(sociedades, x => x.IdSociedad == "T01" && x.Ejercicios.Any(e => e.Ejercicio == 2026));

        var buscados = await s.BuscarAsientosAsync("T01", 2026, cuenta: "622");
        Assert.True(buscados.EsCorrecto);
        Assert.Equal(origen.IdAsiento, Assert.Single(buscados.Valor).Numero);

        var detalle = await s.VerAsientoAsync(origen.IdAsiento);
        Assert.True(detalle.EsCorrecto);
        Assert.Equal(("Reparaciones sintéticas", false, false), (detalle.Valor.Apuntes[0].NombreCuenta, detalle.Valor.DeFactura, detalle.Valor.Automatico));

        var cuentas = await s.BuscarCuentasAsync("T01", 2026, "reparac");
        Assert.Equal("6220001", Assert.Single(cuentas.Valor).Codigo);

        var mayor = await s.MayorAsync("T01", 2026, "6220001");
        Assert.True(mayor.EsCorrecto, string.Join(" | ", mayor.Errores));
        Assert.Equal(121m, mayor.Valor.SaldoActual);
        Assert.Single(mayor.Valor.Movimientos);

        Assert.False((await s.BuscarAsientosAsync("T01", 1999)).EsCorrecto);
    }

    [Fact]
    public async Task ProponerValidaSinGrabarYGrabarCreaLaCorreccionRelacionada()
    {
        await PrepararAsync();
        var origen = await ManualAsync();
        var s = Servicio();
        var antes = await AsientosAsync();

        var propuesta = await s.ProponerAsync(Correccion(origen.IdAsiento));
        Assert.True(propuesta.EsCorrecto, string.Join(" | ", propuesta.Errores));
        Assert.Equal(antes, await AsientosAsync()); // validar no deja nada grabado
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM RelacionesAsientos"));

        var grabado = await s.GrabarAsync(propuesta.Valor.Numero);
        Assert.True(grabado.EsCorrecto, string.Join(" | ", grabado.Errores));
        Assert.Equal(2, grabado.Valor.Count);
        Assert.Equal(antes + 2, await AsientosAsync());
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM RelacionesAsientos WHERE IdAsientoOrigen = ?", origen.IdAsiento));

        var motor = new MotorAsientosAccess(Bd);
        foreach (var id in grabado.Valor)
        {
            var a = (await motor.ObtenerAsync(id))!;
            Assert.Equal(TipoAsiento.Rectificacion, a.Tipo);
            Assert.Equal(a.TotalDebe, a.TotalHaber); // invariante: Debe = Haber
            Assert.Contains(ServicioConector.MarcaAsistente, a.Notas);
        }
        Assert.All((await motor.ObtenerAsync(origen.IdAsiento))!.Apuntes, a => Assert.True(a.Punteado));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE IdAsiento = ? AND OperadorCreacion = ?", grabado.Valor[0], "operador.prueba · asistente IA"));

        // Grabar dos veces no duplica.
        Assert.False((await s.GrabarAsync(propuesta.Valor.Numero)).EsCorrecto);
        Assert.Equal(antes + 2, await AsientosAsync());
    }

    [Fact]
    public async Task UnAsientoNuevoSinOrigenEsOrdinario()
    {
        await PrepararAsync();
        var s = Servicio();
        var propuesta = await s.ProponerAsync(new PeticionPropuesta("T01", 2026, "Comisión bancaria de enero", [
            new AsientoPedido(new DateTime(2026, 1, 31), "Comisión bancaria", [new ApuntePedido("6290001", 12.5m, null), new ApuntePedido("5720001", null, 12.5m)])]));
        Assert.True(propuesta.EsCorrecto, string.Join(" | ", propuesta.Errores));
        var grabado = await s.GrabarAsync(propuesta.Valor.Numero);
        Assert.True(grabado.EsCorrecto, string.Join(" | ", grabado.Errores));
        Assert.Equal(TipoAsiento.Ordinario, (await new MotorAsientosAccess(Bd).ObtenerAsync(grabado.Valor[0]))!.Tipo);
    }

    [Theory]
    [InlineData("descuadre")]
    [InlineData("cuenta")]
    [InlineData("cerrado")]
    [InlineData("sinMotivo")]
    [InlineData("dosLados")]
    public async Task UnaPropuestaNoValidaDevuelveElErrorYNoGraba(string caso)
    {
        await PrepararAsync();
        if (caso == "cerrado")
            await Bd.EjecutarAsync(x => x.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = ? WHERE IdSociedadEjercicio = 9001", true));
        var apuntes = caso switch
        {
            "descuadre" => new[] { new ApuntePedido("6290001", 10m, null), new ApuntePedido("5720001", null, 9m) },
            "cuenta" => [new ApuntePedido("6299999", 10m, null), new ApuntePedido("5720001", null, 10m)],
            "dosLados" => [new ApuntePedido("6290001", 10m, 10m), new ApuntePedido("5720001", null, 10m)],
            _ => [new ApuntePedido("6290001", 10m, null), new ApuntePedido("5720001", null, 10m)],
        };
        var antes = await AsientosAsync();
        var r = await Servicio().ProponerAsync(new PeticionPropuesta("T01", 2026, caso == "sinMotivo" ? "" : "Prueba de validación", [new AsientoPedido(new DateTime(2026, 2, 1), "Prueba", apuntes)]));
        Assert.False(r.EsCorrecto);
        Assert.NotEmpty(r.Errores);
        Assert.All(r.Errores, e => Assert.False(string.IsNullOrWhiteSpace(e.AccionCorrectora)));
        Assert.Equal(antes, await AsientosAsync());
    }

    [Fact]
    public async Task SiElOrigenCambiaDespuesDeProponerNoSeGraba()
    {
        await PrepararAsync();
        var origen = await ManualAsync();
        var s = Servicio();
        var propuesta = await s.ProponerAsync(Correccion(origen.IdAsiento));
        Assert.True(propuesta.EsCorrecto, string.Join(" | ", propuesta.Errores));

        origen.Descripcion = "Reparación sintética (revisada)";
        Assert.True((await new MotorAsientosAccess(Bd).GuardarManualAsync(origen, new Actor("otro"), "Cambio de descripción")).EsCorrecto);

        var antes = await AsientosAsync();
        var r = await s.GrabarAsync(propuesta.Valor.Numero);
        Assert.False(r.EsCorrecto);
        Assert.Equal("Conector.OrigenCambiado", r.Errores[0].Codigo);
        Assert.Equal(antes, await AsientosAsync());
    }

    [Fact]
    public async Task UnAsientoAutomaticoNoSeCorrigeConAsientosSueltos()
    {
        await PrepararAsync();
        var a = new Asiento(_ctx, new DateTime(2026, 1, 15), "Asiento de un proceso");
        a.Apuntes.AddRange([Apunte.AlDebe("6220001", 121m), Apunte.AlHaber("5720001", 121m)]);
        var lote = await new MotorAsientosAccess(Bd).ReemplazarLoteAsync(_ctx, new IdentidadLote("PRUEBA", "PROCESO", "1"), [a], new Actor("proceso", true), "Lote de prueba");
        Assert.True(lote.EsCorrecto, string.Join(" | ", lote.Errores));

        var r = await Servicio().ProponerAsync(Correccion(lote.Valor[0]));
        Assert.False(r.EsCorrecto);
        Assert.Equal("Conector.OrigenNoManual", r.Errores[0].Codigo);
        Assert.True((await Servicio().VerAsientoAsync(lote.Valor[0])).Valor.Automatico);
    }

    [Fact]
    public async Task UnAsientoImportadoDeContaSolSinIvaSeCorrigeComoUnoManual()
    {
        await PrepararAsync();
        var a = new Asiento(_ctx, new DateTime(2026, 1, 20), "Asiento importado");
        a.Apuntes.AddRange([Apunte.AlDebe("6220001", 121m), Apunte.AlHaber("5720001", 121m)]);
        var lote = await new MotorAsientosAccess(Bd).ReemplazarLoteAsync(_ctx, new IdentidadLote(OpcionesLote.MotorImportadorContaSol, "CONTASOL", "2026-1"), [a],
            new Actor("importador", true), "Importación de prueba", opciones: new OpcionesLote(LibroExterno: true));
        Assert.True(lote.EsCorrecto, string.Join(" | ", lote.Errores));

        var s = Servicio();
        var propuesta = await s.ProponerAsync(Correccion(lote.Valor[0]));
        Assert.True(propuesta.EsCorrecto, string.Join(" | ", propuesta.Errores));
        Assert.True((await s.GrabarAsync(propuesta.Valor.Numero)).EsCorrecto);
    }

    [Fact]
    public async Task UnaPropuestaQueNoExisteNoGraba()
    {
        await PrepararAsync();
        var r = await Servicio().GrabarAsync(99);
        Assert.False(r.EsCorrecto);
        Assert.Equal("Conector.PropuestaNoExiste", r.Errores[0].Codigo);
    }
}
