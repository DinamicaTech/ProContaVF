using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Auditoria;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Implantacion.Tests;

/// <summary>Importación de dos ejercicios de ContaSol sintéticos en una BD de pruebas (nunca la real).</summary>
[Trait("Categoria", "Access")]
public sealed class ImportadorAccessTests : IDisposable
{
    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly string _carpeta = BdPruebas.CarpetaTemporal();
    private readonly Actor _actor = new("operador.prueba");
    private readonly IReloj _reloj = new RelojFijo(new DateTime(2026, 11, 10, 18, 0, 0));

    public void Dispose()
    {
        _copia.Dispose();
        try { Directory.Delete(_carpeta, true); } catch (IOException) { }
    }

    private IBaseDatos Bd => _copia.Bd();

    private ImportadorContaSol Importador()
    {
        var cuentas = new CatalogoCuentasAccess(Bd);
        return new ImportadorContaSol(Bd, new MotorAsientosAccess(Bd), new CatalogoCentrosCosteAccess(Bd), new RepositorioTercerosAccess(Bd, _reloj),
            new ServicioRoles(Bd, cuentas), reloj: _reloj);
    }

    private static readonly (string, string)[] Plan =
    [
        ("100", "Capital"), ("1000000", "Capital social"), ("400", "Proveedores"), ("4000001", "Proveedor sintético"), ("430", "Clientes"), ("4300001", "Cliente sintético"),
        ("472", "IVA soportado"), ("4720000", "IVA soportado 21"), ("477", "IVA repercutido"), ("4770000", "IVA repercutido 21"), ("572", "Bancos"), ("5720001", "Banco sintético"),
        ("600", "Compras"), ("6000001", "Compras sintéticas"), ("700", "Ventas"), ("7000001", "Ventas sintéticas"),
    ];

    private static ContaSolSintetico Ejercicio2024(decimal venta = 100m, bool conCompra = true)
    {
        var o = new ContaSolSintetico { Ejercicio = 2024 };
        o.Cuentas.AddRange(Plan);
        o.Centros.Add((3, 0, "Inmuebles"));
        o.Centros.Add((3, 318, "Local sintético"));
        o.Asiento(1, new DateTime(2024, 1, 1), "Asiento de apertura", ("5720001", 1000m, 0m), ("1000000", 0m, 1000m));
        o.Asiento(2, new DateTime(2024, 2, 1), "Venta sintética", ("4300001", venta * 1.21m, 0m), ("7000001", 0m, venta), ("4770000", 0m, venta * 0.21m));
        if (conCompra)
        {
            o.Apuntes.Add(new ContaSolSintetico.Linea(3, 1, new DateTime(2024, 4, 2), "6000001", 100m, true, "Compra sintética", Dep: 3, Sub: 318));
            o.Apuntes.Add(new ContaSolSintetico.Linea(3, 2, new DateTime(2024, 4, 2), "4720000", 21m, true, "Compra sintética", TipoIva: "S", CodigoIva: 7));
            o.Apuntes.Add(new ContaSolSintetico.Linea(3, 3, new DateTime(2024, 4, 2), "4000001", -121m, true, "Compra sintética"));
        }
        o.IvaSoportado.Add((7, new DateTime(2024, 4, 2), "4000001", "A11111111", 100m, 21m, 21m));
        o.Terceros.Add((true, "4300001", "Cliente antiguo", "A22222222"));
        o.Terceros.Add((false, "4000001", "Proveedor sintético", "A11111111"));
        return o;
    }

    private static ContaSolSintetico Ejercicio2025()
    {
        var o = new ContaSolSintetico { Ejercicio = 2025 };
        o.Cuentas.AddRange(Plan);
        o.Asiento(1, new DateTime(2025, 1, 1), "Asiento de apertura", ("5720001", 1000m, 0m), ("1000000", 0m, 1000m));
        o.Asiento(2, new DateTime(2025, 3, 1), "Venta a la nueva titular", ("4300001", 242m, 0m), ("7000001", 0m, 200m), ("4770000", 0m, 42m));
        // La subcuenta 4300001 ha cambiado de titular (otro NIF).
        o.Terceros.Add((true, "4300001", "Cliente nuevo", "A33333333"));
        return o;
    }

    private async Task<IReadOnlyList<ResumenEjercicio>> ImportarAsync(params string[] rutas)
    {
        var imp = Importador();
        var planes = await imp.AnalizarAsync(rutas);
        Assert.True(planes.EsCorrecto, planes.Error?.ToString());
        return await imp.ImportarAsync(planes.Valor, _actor);
    }

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p)));

    [Fact]
    public async Task ImportaVariosEjerciciosSinDuplicarYEnEjercicioCerrado()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9000, "T01", 2024, cerrado: true);
        var r2024 = await Ejercicio2024().CrearAsync(_carpeta);
        var r2025 = await Ejercicio2025().CrearAsync(_carpeta);

        var primera = await ImportarAsync(r2025, r2024);
        Assert.Equal([2024, 2025], primera.Select(r => r.Ejercicio));
        Assert.All(primera, r => Assert.True(r.Bloqueantes == 0, string.Join(Environment.NewLine, r.Incidencias.Where(i => i.Severidad == Severidad.Bloqueante).Select(i => i.Explicacion))));
        Assert.Equal(3, primera[0].AsientosGrabados);
        Assert.Equal(2, primera[1].AsientosGrabados);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE TotalDebe <> TotalHaber"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE IdSociedadEjercicio = 9000 AND TipoAsiento = ?", (byte)TipoAsiento.Apertura));

        // Carga masiva: textos con acentos, una revisión por asiento y un ALTA por apunte en el registro de cambios.
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM Apuntes WHERE Descripcion = 'Compra sintética'"));
        Assert.Equal(5, await ContarAsync("SELECT COUNT(*) FROM RevisionesAsientos"));
        Assert.Equal(await ContarAsync("SELECT COUNT(*) FROM Apuntes"), await ContarAsync("SELECT COUNT(*) FROM RegistroCambiosApuntes WHERE Operacion = 'ALTA'"));

        // Caché de saldos recalculada al final de la fase de asientos.
        Assert.Equal(1000, await ContarAsync("SELECT SaldoActual FROM CuentasContables WHERE IdSociedadEjercicio = 9000 AND Codigo = '5720001'"));
        Assert.Equal(-121, await ContarAsync("SELECT SaldoActual FROM CuentasContables WHERE IdSociedadEjercicio = 9000 AND Codigo = '4000001'"));

        // Negativo al lado contrario y centro de coste del apunte.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Apuntes AS ap INNER JOIN Asientos AS a ON ap.IdAsiento = a.IdAsiento WHERE a.IdSociedadEjercicio = 9000 AND ap.CuentaContable = '4000001' AND ap.ImporteHaber = 121"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Apuntes AS ap INNER JOIN CentrosCoste AS c ON ap.IdCentroCoste = c.IdCentroCoste WHERE c.RutaJerarquica = '3/318'"));

        // Una cuenta por código: la misma identidad en los dos ejercicios.
        var mapas = new MapasContaSol(Bd);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CodigosContablesReservados WHERE IdSociedad = 'T01' AND Codigo = '4300001'"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM CuentasContables WHERE Codigo = '4300001'"));
        Assert.Equal(3, (await mapas.LeerAsync("T01", 2024, TiposMapa.Asiento)).Count);

        // Libro de IVA enlazado con su asiento.
        var idIva = (await mapas.LeerAsync("T01", 2024, TiposMapa.IvaSoportado))["7"];
        var idAsientoCompra = (await mapas.LeerAsync("T01", 2024, TiposMapa.Asiento))["1:3"];
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM IVASoportado WHERE IdIVASoportado = ? AND IdAsiento = ?", idIva, idAsientoCompra));

        // Subcuenta reasignada: dos terceros y la cuenta solo en el más reciente.
        var clientes = await mapas.LeerAsync("T01", 2024, TiposMapa.Cliente);
        var clientes2025 = await mapas.LeerAsync("T01", 2025, TiposMapa.Cliente);
        Assert.NotEqual(clientes["4300001"], clientes2025["4300001"]);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CuentasClientesSociedad AS c INNER JOIN CodigosContablesReservados AS r ON c.IdIdentidadContable = r.IdIdentidadContable WHERE r.Codigo = '4300001'"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CuentasClientesSociedad AS c INNER JOIN CodigosContablesReservados AS r ON c.IdIdentidadContable = r.IdIdentidadContable WHERE r.Codigo = '4300001' AND c.IdCliente = ?", clientes2025["4300001"]));

        // Repetir: nada cambia y no se duplica.
        var asientos = await ContarAsync("SELECT COUNT(*) FROM Asientos");
        var idsAntes = await mapas.LeerAsync("T01", 2024, TiposMapa.Asiento);
        var segunda = await ImportarAsync(r2024, r2025);
        Assert.Equal(idsAntes.OrderBy(m => m.Key), (await mapas.LeerAsync("T01", 2024, TiposMapa.Asiento)).OrderBy(m => m.Key));
        Assert.All(segunda, r => Assert.Equal(0, r.AsientosGrabados));
        Assert.Equal(5, segunda.Sum(r => r.AsientosSinCambios));
        Assert.Equal(asientos, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM IVASoportado"));
        Assert.Equal(idIva, (await mapas.LeerAsync("T01", 2024, TiposMapa.IvaSoportado))["7"]);
        // Cliente antiguo, cliente nuevo de la subcuenta reasignada y proveedor.
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM Terceros"));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM CuentasClientesSociedad AS c INNER JOIN CodigosContablesReservados AS r ON c.IdIdentidadContable = r.IdIdentidadContable WHERE r.Codigo = '4300001' AND c.IdCliente = ?", clientes2025["4300001"]));
    }

    [Fact]
    public async Task ReimportarSustituyeLoCambiadoYBorraLoQueYaNoEsta()
    {
        var antes = await Ejercicio2024().CrearAsync(_carpeta);
        Assert.All(await ImportarAsync(antes), r => Assert.Equal(0, r.Bloqueantes));
        var despues = await Ejercicio2024(venta: 200m, conCompra: false).CrearAsync(_carpeta);
        var r = Assert.Single(await ImportarAsync(despues));
        Assert.Equal((1, 1, 1), (r.AsientosGrabados, r.AsientosSinCambios, r.AsientosBorrados));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
        Assert.Equal(242, await ContarAsync("SELECT SUM(TotalDebe) FROM Asientos WHERE TipoAsiento <> ?", (byte)TipoAsiento.Apertura));
        // El registro de IVA sigue (está en F_IVS) pero ya sin asiento.
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM IVASoportado WHERE IdAsiento IS NULL"));
        // Las bajas quedan en el registro de cambios: la venta cambiada y la compra que ya no está.
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM RegistroCambiosApuntes WHERE Operacion = 'BAJA' AND Campo = 'Asiento'"));
    }

    [Fact]
    public async Task OtraEmpresaConElMismoCodigoNoSeImporta()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Otra sociedad", nif: "A87654321");
        var ruta = await Ejercicio2024().CrearAsync(_carpeta);
        var r = Assert.Single(await ImportarAsync(ruta));
        Assert.Equal(EstadosImportacion.Fallida, r.Estado);
        Assert.Contains(r.Incidencias, i => i.Codigo == "ContaSol.EmpresaDistinta");
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos"));
    }
}
