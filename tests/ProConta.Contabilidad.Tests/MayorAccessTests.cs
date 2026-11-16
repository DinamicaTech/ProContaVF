using System.Diagnostics;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Mayor;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>El Mayor sobre una copia de la maestra con dos ejercicios sintéticos grabados con el motor de asientos.</summary>
[Trait("Categoria", "Access")]
public sealed class MayorAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _e2024 = new("M01", 9101, 2024);
    private readonly ContextoSociedadEjercicio _e2025 = new("M01", 9102, 2025);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "M01", "Sociedad sintética del Mayor");
        await DatosSinteticos.EjercicioAsync(Bd, 9101, "M01", 2024);
        await DatosSinteticos.EjercicioAsync(Bd, 9102, "M01", 2025);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("1000000", "Capital"), ("5720001", "Banco sintético"), ("4300001", "Cliente sintético"), ("7000001", "Ventas"), ("4770021", "IVA repercutido 21") })
        {
            var alta = await cuentas.AltaAsync(_e2024, new AltaCuenta(codigo, nombre));
            Assert.True(alta.EsCorrecto, alta.Error?.ToString());
            // La misma cuenta en 2025: se copia su identidad, como hace la apertura de un ejercicio.
            var copia = await cuentas.AltaEnEjercicioAsync(_e2025, alta.Valor.IdIdentidadContable);
            Assert.True(copia.EsCorrecto, copia.Error?.ToString());
        }
        var motor = new MotorAsientosAccess(Bd);
        async Task Asiento(ContextoSociedadEjercicio ctx, DateTime fecha, string texto, TipoAsiento tipo, params Apunte[] apuntes)
        {
            var a = new Asiento(ctx, fecha, texto) { Tipo = tipo };
            a.Apuntes.AddRange(apuntes);
            var r = await motor.GuardarManualAsync(a, _actor);
            Assert.True(r.EsCorrecto, r.Error?.ToString());
        }
        Apunte Doc(Apunte a, string doc) { a.NumeroDocumento = doc; return a; }
        await Asiento(_e2024, new(2024, 1, 1), "Apertura 2024", TipoAsiento.Apertura, Apunte.AlDebe("5720001", 1000m), Apunte.AlHaber("1000000", 1000m));
        await Asiento(_e2024, new(2024, 2, 15), "Factura F2024001", TipoAsiento.Ordinario,
            Doc(Apunte.AlDebe("4300001", 1210m), "F2024001"), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m));
        await Asiento(_e2024, new(2024, 3, 10), "Cobro 2024001", TipoAsiento.Ordinario, Apunte.AlDebe("5720001", 1000m), Doc(Apunte.AlHaber("4300001", 1000m), "2024001"));
        await Asiento(_e2024, new(2024, 12, 31), "Cierre 2024", TipoAsiento.Cierre,
            Apunte.AlHaber("5720001", 2000m), Apunte.AlHaber("4300001", 210m), Apunte.AlDebe("1000000", 1000m), Apunte.AlDebe("7000001", 1000m), Apunte.AlDebe("4770021", 210m));
        await Asiento(_e2025, new(2025, 1, 1), "Apertura 2025", TipoAsiento.Apertura,
            Apunte.AlDebe("5720001", 2000m), Apunte.AlDebe("4300001", 210m), Apunte.AlHaber("1000000", 1000m), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m));
        await Asiento(_e2025, new(2025, 2, 1), "Cobro resto 2024001", TipoAsiento.Ordinario, Apunte.AlDebe("5720001", 210m), Doc(Apunte.AlHaber("4300001", 210m), "F2024001"));
        // El ejercicio 2024 se cierra después de grabarlo: el Mayor lo enseña en solo consulta.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9101"));
    }

    [Fact]
    public async Task ElMayorDeAccessCoincideConLasReglasDelServicio()
    {
        await PrepararAsync();
        var fuente = new FuenteMayorAccess(Bd);
        var s = new SesionMayor(fuente, "M01");
        await s.IniciarAsync(2025);
        Assert.Equal([2024, 2025], s.Elegidos.Select(e => e.Ejercicio));
        Assert.True(s.HayCerrados);

        await s.VerCuentasAsync(["4300001"]);
        var r = s.Resultado;
        Assert.Equal(4, r.Filas.Count); // factura, cobro, apertura que continúa y cobro del resto; el cierre no
        Assert.True(r.Filas[2].AperturaContinua);
        Assert.Null(r.Filas[2].AvisoApertura);
        Assert.Equal((1210m, 1210m, 0m), (r.TotalDebe, r.TotalHaber, r.Saldo));
        Assert.Equal("7000001", r.Filas[0].Contrapartida);
        Assert.Equal("Ventas", r.Filas[0].NombreContrapartida);
        Assert.True(r.Filas[0].EjercicioCerrado);
        Assert.NotNull(r.Filas[0].GrabadoEl);

        s.CambiarVista(VistaMayor.SoloPendientes);
        Assert.Empty(s.Resultado.Filas);
        s.CambiarVista(VistaMayor.Normal);

        await s.BuscarFacturasAsync(["2024001"]);
        Assert.Equal(["4300001", "4300001", "4300001"], s.Resultado.Filas.Select(f => f.Cuenta));

        await s.VerCuentasAsync(["5"]);
        Assert.Equal(["5720001"], s.Resultado.Filas.Select(f => f.Cuenta).Distinct());
        var banco = s.Catalogo.Single(c => c.Codigo == "5720001");
        Assert.Equal((2210m, 1210m, 3), (banco.Saldo, banco.SaldoSinApertura, banco.Apuntes));

        await s.VerCuentasAsync(["1", "4", "5", "7"]);
        Assert.Equal(s.Resultado.TotalDebe, s.Resultado.TotalHaber);

        var asiento = await fuente.AsientoAsync(r.Filas[0].IdAsiento);
        Assert.NotNull(asiento);
        Assert.Equal(3, asiento!.Lineas.Count);
        Assert.Equal("Cliente sintético", asiento.Lineas[0].NombreCuenta);
        Assert.Equal(asiento.TotalDebe, asiento.TotalHaber);
        var historial = await fuente.HistorialAsync(r.Filas[0].IdAsiento, r.Filas[0].IdsApunte[0], r.Filas[0].Orden);
        Assert.Contains(historial, c => c.Operacion == "ALTA");
    }

    /// <summary>
    /// Rendimiento sobre una copia con datos reales (solo en el equipo del propietario): PROCONTA_BD_MAYOR apunta a la copia de
    /// pruebas. Se abre en solo lectura. Sin la variable, la prueba no hace nada.
    /// </summary>
    [Fact]
    public async Task UnBancoDeMilesDeApuntesCargaEnPocosSegundos()
    {
        if (Environment.GetEnvironmentVariable("PROCONTA_BD_MAYOR") is not { Length: > 0 } ruta || !File.Exists(ruta)) return;
        var bd = new BaseDatosAccess(ruta, ModoApertura.SoloLectura);
        var fuente = new FuenteMayorAccess(bd);
        var idSociedad = (string)(await bd.EjecutarAsync(s => s.EscalarAsync(
            "SELECT TOP 1 se.IdSociedad FROM SociedadesEjercicios AS se INNER JOIN Asientos AS a ON a.IdSociedadEjercicio = se.IdSociedadEjercicio")))!;
        var s = new SesionMayor(fuente, idSociedad);
        await s.IniciarAsync(DateTime.Today.Year, s.Ejercicios.Select(e => e.Ejercicio).ToList());
        await s.ElegirEjerciciosAsync(s.Ejercicios.Select(e => e.Ejercicio).ToList());
        var banco = s.Catalogo.Where(c => c.EsSubcuenta && c.Codigo.StartsWith("57", StringComparison.Ordinal)).OrderByDescending(c => c.Apuntes).First();
        var reloj = Stopwatch.StartNew();
        await s.VerCuentasAsync([banco.Codigo]);
        reloj.Stop();
        Assert.True(s.Resultado.Filas.Count > 1000, $"{banco.Codigo} tiene {s.Resultado.Filas.Count} filas");
        Assert.True(reloj.Elapsed < TimeSpan.FromSeconds(10), $"La carga de {banco.Codigo} ({s.Resultado.Filas.Count} filas) tardó {reloj.Elapsed}.");
        Assert.Equal(s.Resultado.Saldo, s.Resultado.SaldoFinal);
        // El saldo de la tabla es el del selector para los mismos ejercicios.
        Assert.Equal(banco.Saldo, s.Resultado.Saldo);
        Console.WriteLine($"Mayor de {banco.Codigo}: {s.Resultado.Filas.Count} filas en {reloj.ElapsedMilliseconds} ms");
    }
}
