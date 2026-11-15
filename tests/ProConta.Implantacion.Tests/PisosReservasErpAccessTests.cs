using ProConta.Datos.Persistencia;
using ProConta.Implantacion.ERP;
using ProConta.Pruebas.Access;

namespace ProConta.Implantacion.Tests;

/// <summary>Corrección del piso de las reservas del ERP ya migradas (facturacion/reservas 2026-11-15 17:45), con datos inventados.</summary>
[Trait("Categoria", "Access")]
public sealed class PisosReservasErpAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private static readonly DateTime Ahora = new(2026, 11, 15, 18, 0, 0);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private static OrigenErp Erp(params (int Id, string? Alojamiento)[] reservas) => new()
    {
        Ruta = "erp-sintetico.accdb",
        Huella = "0",
        Tablas = new Dictionary<string, IReadOnlyList<Fila>>
        {
            ["ReservasPisosTuristicos"] = reservas.Select(r => Fila.De(("Id", r.Id), ("Alojamiento", r.Alojamiento))).ToList(),
        },
        ApuntesCliente = [],
        RegistrosRepercutido = [],
        RegistrosSoportado = new HashSet<(int, int)>(),
        NoMigradas = new Dictionary<string, int>(),
        Direcciones = [],
    };

    private async Task<int> ActivoAsync(string codigo, string referencia)
    {
        var tipo = Convert.ToInt32(await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT MIN(IdTipoActivo) FROM TiposActivo")));
        return await Bd.EjecutarAsync(async s =>
        {
            await s.EjecutarAsync(
                "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, AmortizacionAcumulada, DeterioroAcumulado, " +
                "ValorContable, EstadoCalculo, EsAjeno, ReferenciaMigracion, Version) VALUES ('T01', ?, ?, ?, ?, 1, True, 0, 0, 0, 0, 0, False, ?, 0)",
                tipo, codigo, $"Piso sintético {codigo}", new DateTime(2022, 1, 1), referencia);
            return await s.UltimoIdAsync();
        });
    }

    private async Task<int> OrigenAsync() => await Bd.EjecutarAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO OrigenesVenta (IdSociedad, Codigo, Nombre, EsDirecta, Activo) VALUES ('T01', 'SINT', 'Origen sintético', False, True)");
        return await s.UltimoIdAsync();
    });

    private Task VentaAsync(int origen, string externo, int? activo = null, int? factura = null) => Bd.EjecutarAsync(s => s.EjecutarAsync(
        "INSERT INTO VentasPorFacturar (IdSociedad, IdOrigenVenta, IdActivo, FechaReferencia, Cantidad, Importe, Anulada, FacturadaFuera, IdFactura, Fuente, IdExterno, FechaAlta, Version) " +
        "VALUES ('T01', ?, ?, ?, 2, 200, False, True, ?, 'ERP', ?, ?, 0)",
        origen, activo, new DateTime(2024, 5, 1), factura, externo, Ahora));

    private async Task<int?> ActivoDe(string externo) =>
        await Bd.EjecutarAsync(s => s.EscalarAsync("SELECT IdActivo FROM VentasPorFacturar WHERE IdExterno = ?", externo)) is int i ? i : null;

    [Fact]
    public async Task Pone_el_piso_por_codigo_o_nombre_respeta_lo_ya_puesto_y_repetirla_no_cambia_nada()
    {
        var playa = await ActivoAsync("PLAYA", "ERP:Inmuebles:47");
        var terraza = await ActivoAsync("TERRAZA", "ERP:Inmuebles:48");
        var origen = await OrigenAsync();
        await VentaAsync(origen, "1");
        await VentaAsync(origen, "2");
        await VentaAsync(origen, "3");
        await VentaAsync(origen, "4");
        await VentaAsync(origen, "5", activo: terraza); // puesto a mano: se respeta aunque la regla diga otro
        await VentaAsync(origen, "6", factura: 999);    // enlazada a una factura de ProContaVF: no se edita
        await VentaAsync(origen, "7");                  // ya no está en el ERP
        var erp = Erp((1, "7685138"), (2, "Apartamento  2 habitaciones y magnífica terraza"), (3, "Apartamento Playa con Piscina"), (4, "Piso sintético"),
            (5, "7685138"), (6, "8340467"));

        var r = await new CorreccionPisosReservasErp(Bd).AplicarAsync(erp, "T01", Ahora);

        Assert.Equal(7, r.Reservas);
        Assert.Equal(1, r.AsignadasPorRegla["código de Booking 7685138"]);
        Assert.Equal(1, r.AsignadasPorRegla["nombre «…magnífica terraza»"]);
        Assert.Equal(1, r.AsignadasPorRegla["nombre «Playa con Piscina…»"]);
        Assert.Equal(1, r.YaTenianPiso);
        Assert.Equal(1, r.FacturadasEnProConta);
        Assert.Equal(2, r.SinPiso.Count);
        Assert.Equal(playa, await ActivoDe("1"));
        Assert.Equal(terraza, await ActivoDe("2"));
        Assert.Equal(playa, await ActivoDe("3"));
        Assert.Null(await ActivoDe("4"));
        Assert.Equal(terraza, await ActivoDe("5"));
        Assert.Null(await ActivoDe("6"));
        Assert.Null(await ActivoDe("7"));

        var otra = await new CorreccionPisosReservasErp(Bd).AplicarAsync(erp, "T01", Ahora);
        Assert.Empty(otra.AsignadasPorRegla);
        Assert.Equal(4, otra.YaTenianPiso);
        Assert.Equal(2, otra.SinPiso.Count);
    }

    [Fact]
    public async Task Sin_el_inmueble_migrado_como_activo_la_reserva_queda_sin_piso_y_dice_por_que()
    {
        var origen = await OrigenAsync();
        await VentaAsync(origen, "1");
        var r = await new CorreccionPisosReservasErp(Bd).AplicarAsync(Erp((1, "8340467")), "T01", Ahora);
        Assert.Empty(r.AsignadasPorRegla);
        Assert.Contains("inmueble 48", Assert.Single(r.SinPiso));
        Assert.Null(await ActivoDe("1"));
    }
}
