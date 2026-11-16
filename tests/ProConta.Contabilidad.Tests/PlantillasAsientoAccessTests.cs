using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>Plantillas de asiento contra una copia de la BD de pruebas (contabilidad/plantillas-asiento, PR 1).</summary>
[Trait("Categoria", "Access")]
public sealed class PlantillasAsientoAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2025);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioPlantillasAsiento Servicio() => new(Bd);
    private MotorAsientosAccess Motor() => new(Bd);

    private async Task PrepararAsync(string? cuenta113 = "1130000")
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE Sociedades SET Cuenta113ParaCuadres = ?, Cuenta570Tesoreria = ? WHERE IdSociedad = 'T01'", cuenta113, "5700000"));
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("6220001", "Reparaciones sintéticas"), ("6290001", "Otros servicios"), ("5720001", "Banco sintético"),
                     ("1130000", "Reservas voluntarias"), ("5700000", "Caja"), ("5510001", "Socio sintético"), ("4000001", "Proveedor sintético"), ("4300003", "Cliente sintético"),
                     ("1290000", "Resultado") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
    }

    private async Task<Asiento> AsientoOrigenAsync(string debe = "6220001", string haber = "5720001", decimal importe = 242m, string concepto = "Reparación sintética")
    {
        var a = new Asiento(_ctx, new DateTime(2025, 2, 10), concepto);
        a.Apuntes.AddRange([Apunte.AlDebe(debe, importe, concepto), Apunte.AlHaber(haber, importe, concepto)]);
        a.Apuntes[0].NumeroDocumento = "F-SINT-1";
        var r = await Motor().GuardarManualAsync(a, _actor);
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        await TrimestreActivoAsync(2); // el origen queda en un trimestre ya cerrado, como en la realidad
        return r.Valor;
    }

    private Task TrimestreActivoAsync(int trimestre) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = 9001", (byte)trimestre));

    private Task<int> ContarAsync(string sql, params object?[] p) =>
        Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));

    private async Task<PlantillaAsiento> PlantillaAsync(string nombre) =>
        (await Servicio().ListarAsync("T01", incluirSistema: true)).Single(p => p.Nombre == nombre);

    [Fact]
    public async Task LaMigracionSiembraElCatalogoYSoloOfreceLasDeUsuario()
    {
        await PrepararAsync();
        var usuario = await Servicio().ListarAsync("T01");
        Assert.Equal(["Anular asiento", "Anular y rehacer con otra cuenta", "Pasar saldo a otra cuenta", "Ajustar saldo inicial contra la 113", "Pago por tesorería", "Pago por un socio",
            "Pasar un cliente de cobro dudoso a incobrable"],
            usuario.Select(p => p.Nombre));
        Assert.All(usuario, p => Assert.Equal(TipoPlantilla.Usuario, p.Tipo));
        Assert.Equal(2, (await Servicio().ListarAsync("T01", origen: OrigenPlantilla.Apunte)).Count);
        Assert.Equal(2, (await Servicio().ListarAsync("T01", origen: OrigenPlantilla.Asiento)).Count);
        Assert.Equal(2, (await Servicio().ListarAsync("T01", origen: OrigenPlantilla.Subcuenta)).Count);
        Assert.Equal(ClasePlantilla.Guia, usuario.Single(p => p.Nombre.StartsWith("Pasar un cliente")).Clase);

        var nomina = await Servicio().DeProcesoAsync("nominas", "T01");
        Assert.NotNull(nomina);
        Assert.Equal((TipoPlantilla.Sistema, "Nómina", 0), (nomina!.Tipo, nomina.Nombre, nomina.Lineas.Count));

        // Lo guardado es lo mismo que la semilla: la vista previa sale igual desde la BD.
        var rehacer = usuario.Single(p => p.Nombre == "Anular y rehacer con otra cuenta");
        Assert.Equal((1, 2), (rehacer.Datos.Count, rehacer.Lineas.Count));
        Assert.True(rehacer.Lineas.All(l => l.PorCadaApunte));
        Assert.True(ServicioPlantillasAsiento.VistaPrevia(rehacer).EsCorrecto);
        Assert.All(usuario, p => Assert.True(ServicioPlantillasAsiento.VistaPrevia(p).EsCorrecto, p.Nombre));

        // Cada tabla y campo nuevo lleva su descripción (la recoge el diccionario).
        var esquema = await Bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema()));
        Assert.Contains("SISTEMA", esquema.Tabla("PlantillasAsiento")!.Columna("Tipo")!.Descripcion);
        Assert.False(string.IsNullOrWhiteSpace(esquema.Tabla("Asientos")!.Columna("IdPlantillaAsiento")!.Descripcion));
        Assert.All(esquema.Tabla("PlantillasAsientoLineas")!.Columnas, c => Assert.False(string.IsNullOrWhiteSpace(c.Descripcion), c.Nombre));
    }

    [Fact]
    public async Task AnularYRehacerGrabaDosAsientosEnUnaOperacionRelacionadosYPunteados()
    {
        await PrepararAsync();
        var origen = await AsientoOrigenAsync();
        var plantilla = await PlantillaAsync("Anular y rehacer con otra cuenta");
        var apunte = await Servicio().OrigenApunteAsync(origen.Apuntes[0].IdApunte);
        Assert.True(apunte.EsCorrecto);
        Assert.False(apunte.Valor.TieneLibroIva);

        var propuesta = await Servicio().ProponerAsync(_ctx, plantilla, apunte.Valor, new Dictionary<string, string?> { ["NuevaCuenta"] = "6290001" });
        Assert.True(propuesta.EsCorrecto, string.Join(" | ", propuesta.Errores));
        Assert.Equal(2, propuesta.Valor.Asientos.Count);
        Assert.All(propuesta.Valor.Asientos, a => Assert.Equal(new DateTime(2025, 4, 1), a.Fecha));
        // Proponer (y cancelar después) no escribe nada.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));

        var sinMotivo = await Servicio().EjecutarAsync(propuesta.Valor, "", _actor);
        Assert.Equal("Plantillas.SinMotivo", sinMotivo.Error!.Codigo);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));

        var r = await Servicio().EjecutarAsync(propuesta.Valor, "La reparación era de otro servicio", _actor);
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        Assert.Equal(2, r.Valor.Count);
        foreach (var id in r.Valor)
        {
            var a = (await Motor().ObtenerAsync(id))!;
            Assert.Equal(a.TotalDebe, a.TotalHaber);
            Assert.Equal(plantilla.IdPlantilla, a.IdPlantilla);
            Assert.Equal("La reparación era de otro servicio", a.Notas);
            Assert.Equal(TipoAsiento.Rectificacion, a.Tipo);
            Assert.All(a.Apuntes, x => Assert.True(x.Punteado));
            Assert.All(a.Apuntes, x => Assert.Equal(x.CodigoCuenta.StartsWith("622") || x.CodigoCuenta.StartsWith("629") ? "F-SINT-1" : null, x.NumeroDocumento));
        }
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM RelacionesAsientos WHERE IdAsientoOrigen = ? AND TipoRelacion = 'CORRECCION'", origen.IdAsiento));
        Assert.True((await Motor().ObtenerAsync(origen.IdAsiento))!.Apuntes[0].Punteado);
        // Una sola operación: todo el registro de cambios (altas y punteo del origen) comparte IdLote.
        Assert.Equal(1, await ContarAsync(
            "SELECT COUNT(*) FROM (SELECT DISTINCT IdLote FROM RegistroCambiosApuntes WHERE IdAsiento IN (?, ?) OR (IdAsiento = ? AND Campo = 'Punteado'))",
            r.Valor[0], r.Valor[1], origen.IdAsiento));
        // Saldos: la 622 queda a 0 y la 629 con el gasto.
        var cuentas = new CatalogoCuentasAccess(Bd);
        Assert.Equal(0m, (await cuentas.ResolverAsync(_ctx, "6220001")).Valor.SaldoActual);
        Assert.Equal(242m, (await cuentas.ResolverAsync(_ctx, "6290001")).Valor.SaldoActual);
    }

    [Fact]
    public async Task SiUnAsientoFallaNoSeGrabaNingunoYSinCuentaDeCuadresDiceDondeConfigurarla()
    {
        await PrepararAsync(cuenta113: null);
        var origen = await AsientoOrigenAsync("4300003", "1290000", 150m, "Saldo inicial");
        var apunte = (await Servicio().OrigenApunteAsync(origen.Apuntes[0].IdApunte)).Valor;
        var subcuenta = await Servicio().OrigenSubcuentaAsync(_ctx, "4300003");
        Assert.Equal((150m, 0m), (subcuenta.Valor.Saldo, subcuenta.Valor.SaldoApertura)); // el asiento de prueba no es de apertura
        var ajuste = await Servicio().ProponerAsync(_ctx, await PlantillaAsync("Ajustar saldo inicial contra la 113"), subcuenta.Valor, new Dictionary<string, string?> { ["ImporteAjuste"] = "150" });
        Assert.Equal("Plantillas.SinCuentaCuadres", ajuste.Error!.Codigo);
        Assert.Contains("Parámetros › Empresas", ajuste.Error.AccionCorrectora);

        // Rejilla corregida a mano con una cuenta que no existe en el segundo asiento: no se graba ninguno.
        var rehacer = await Servicio().ProponerAsync(_ctx, await PlantillaAsync("Anular y rehacer con otra cuenta"), apunte,
            new Dictionary<string, string?> { ["NuevaCuenta"] = "6290001" });
        var editada = rehacer.Valor with
        {
            Asientos = [rehacer.Valor.Asientos[0], rehacer.Valor.Asientos[1] with { Apuntes = rehacer.Valor.Asientos[1].Apuntes.Select(x => x with { Cuenta = x.Cuenta == "6290001" ? "6999999" : x.Cuenta }).ToList() }],
        };
        var r = await Servicio().EjecutarAsync(editada, "Prueba de atomicidad", _actor);
        Assert.Equal("Cuentas.NoExiste", r.Error!.Codigo);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM RelacionesAsientos"));
        Assert.False((await Motor().ObtenerAsync(origen.IdAsiento))!.Apuntes[0].Punteado);
    }

    [Fact]
    public async Task UnAsientoAutomaticoNoSeAnulaYLaNominaNoSeEjecutaAMano()
    {
        await PrepararAsync();
        var lote = new Asiento(_ctx, new DateTime(2025, 5, 2), "Factura sintética");
        lote.Apuntes.AddRange([Apunte.AlDebe("6220001", 100m), Apunte.AlHaber("4000001", 100m)]);
        var ids = await Motor().ReemplazarLoteAsync(_ctx, new IdentidadLote("COMPRAS", "EXPORTAR", "FC-1"), [lote], _actor, "Exportación sintética");
        Assert.True(ids.EsCorrecto);
        await TrimestreActivoAsync(2);
        var automatico = (await Motor().ObtenerAsync(ids.Valor[0]))!;
        var origen = (await Servicio().OrigenApunteAsync(automatico.Apuntes[1].IdApunte)).Valor;
        var anular = await Servicio().ProponerAsync(_ctx, await PlantillaAsync("Anular asiento"), origen, null);
        Assert.Equal("Plantillas.AsientoAutomatico", anular.Error!.Codigo);

        // Pagar por caja la deuda de ese asiento sí se puede.
        var pago = await Servicio().ProponerAsync(_ctx, await PlantillaAsync("Pago por tesorería"), origen, null);
        Assert.True(pago.EsCorrecto, string.Join(" | ", pago.Errores));
        Assert.Equal(new DateTime(2025, 5, 2), pago.Valor.Asientos[0].Fecha);

        var nomina = await PlantillaAsync("Nómina");
        var propuesta = pago.Valor with { Plantilla = nomina };
        var r = await Servicio().EjecutarAsync(propuesta, "Intento manual", _actor);
        Assert.Equal("Plantillas.SistemaManual", r.Error!.Codigo);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
    }

    [Fact]
    public async Task PagoPorUnSocioProponeLaUltimaSubcuentaUsada()
    {
        await PrepararAsync();
        var origen = await AsientoOrigenAsync("6220001", "4000001", 121m, "Factura sintética de proveedor");
        var socio = await PlantillaAsync("Pago por un socio");
        var apunte = (await Servicio().OrigenApunteAsync(origen.Apuntes[1].IdApunte)).Valor;
        Assert.Null((await Servicio().ValoresPropuestosAsync(_ctx, socio, apunte))["CuentaSocio"]);

        var propuesta = await Servicio().ProponerAsync(_ctx, socio, apunte, new Dictionary<string, string?> { ["CuentaSocio"] = "5510001" });
        Assert.True(propuesta.EsCorrecto, string.Join(" | ", propuesta.Errores));
        Assert.True((await Servicio().EjecutarAsync(propuesta.Valor, socio.MotivoPropuesto!, _actor)).EsCorrecto);

        var valores = await Servicio().ValoresPropuestosAsync(_ctx, socio, apunte);
        Assert.Equal("5510001", valores["CuentaSocio"]);
        Assert.Equal("121", valores["ImportePago"]);
    }
}
