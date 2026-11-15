using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Inicio;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Inicio.Tests;

/// <summary>Lecturas de Inicio sobre una copia de la maestra con datos sintéticos (2025 sin facturas, 2026 con facturas).</summary>
[Trait("Categoria", "Access")]
public sealed class FuenteInicioAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _c2025 = new("T01", 9001, 2025);
    private readonly ContextoSociedadEjercicio _c2026 = new("T01", 9002, 2026);
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2025);
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2026);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[]
                 {
                     ("1000000", "Capital sintético"), ("5720001", "Banco sintético"), ("7000001", "Ventas sintéticas"), ("4300001", "Cliente sintético"),
                     ("6290001", "Otros servicios"), ("4770021", "IVA repercutido 21"), ("4720021", "IVA soportado 21"), ("4000001", "Proveedor sintético"),
                     ("6300001", "Impuesto sobre beneficios"), ("4750001", "Hacienda acreedora por IVA"),
                 })
        {
            var alta = await cuentas.AltaAsync(_c2025, new AltaCuenta(codigo, nombre));
            Assert.True(alta.EsCorrecto, $"{codigo}: {alta.Error}");
            // La misma cuenta en 2026 (los códigos se reservan por sociedad).
            var copia = await cuentas.AltaEnEjercicioAsync(_c2026, alta.Valor.IdIdentidadContable);
            Assert.True(copia.EsCorrecto, $"{codigo} en 2026: {copia.Error}");
        }
        var motor = new MotorAsientosAccess(Bd);
        async Task Asiento(ContextoSociedadEjercicio ctx, DateTime fecha, string texto, params Apunte[] apuntes)
        {
            var a = new Asiento(ctx, fecha, texto);
            a.Apuntes.AddRange(apuntes);
            var r = await motor.GuardarManualAsync(a, _actor);
            Assert.True(r.EsCorrecto, r.Error?.ToString());
        }
        // 2025, sin facturas: la facturación sale del grupo 70.
        await Asiento(_c2025, new DateTime(2025, 1, 2), "Aportación", Apunte.AlDebe("5720001", 5000m), Apunte.AlHaber("1000000", 5000m));
        await Asiento(_c2025, new DateTime(2025, 3, 15), "Venta", Apunte.AlDebe("4300001", 1210m), Apunte.AlHaber("7000001", 1000m), Apunte.AlHaber("4770021", 210m));
        await Asiento(_c2025, new DateTime(2025, 3, 20), "Gasto", Apunte.AlDebe("6290001", 100m), Apunte.AlDebe("4720021", 21m), Apunte.AlHaber("4000001", 121m));
        await Asiento(_c2025, new DateTime(2025, 3, 31), "Liquidación IVA 1T", Apunte.AlDebe("4770021", 210m), Apunte.AlHaber("4720021", 21m), Apunte.AlHaber("4750001", 189m));
        await Asiento(_c2025, new DateTime(2025, 12, 30), "Impuesto", Apunte.AlDebe("6300001", 50m), Apunte.AlHaber("5720001", 50m));
        // 2026: una venta en el 4T y una factura validada (con un suplido que no cuenta) y un borrador (no cuenta).
        await Asiento(_c2026, new DateTime(2026, 10, 5), "Venta", Apunte.AlDebe("4300001", 2420m), Apunte.AlHaber("7000001", 2000m), Apunte.AlHaber("4770021", 420m));
        // El trimestre activo se fija después de grabar: con él, el motor no deja asientos con IVA en trimestres anteriores.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 4 WHERE IdSociedadEjercicio IN (9001, 9002)"));
        await Bd.EjecutarAsync(async s =>
        {
            foreach (var (id, nombre) in new[] { ((byte)1, "Validada"), ((byte)3, "Borrador") })
                if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM EstadosFactura WHERE IdEstado = ?", id)) == 0)
                    await s.EjecutarAsync("INSERT INTO EstadosFactura (IdEstado, Nombre, EsModificable, EsTerminal) VALUES (?, ?, ?, ?)", id, nombre, id == 3, false);
            if (Convert.ToInt32(await s.EscalarAsync("SELECT COUNT(*) FROM TiposFactura WHERE TipoFactura = 'F1'")) == 0)
                await s.EjecutarAsync("INSERT INTO TiposFactura (TipoFactura, Descripcion, EsRectificativa) VALUES ('F1', 'Factura', False)");
            await s.EjecutarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, ?)",
                9002, "Facturas 2026", 1, 99999, "S2026", false, true);
            var serie = Convert.ToInt32(await s.EscalarAsync("SELECT MAX(IdSerie) FROM SeriesFacturacion WHERE IdSociedadEjercicio = 9002"));
            foreach (var (numero, estado, fecha) in new[] { ("S20260001", (byte)1, new DateTime(2026, 10, 5)), ((string?)null, (byte)3, new DateTime(2026, 10, 9)) })
            {
                await s.EjecutarAsync("INSERT INTO Facturas (IdSociedadEjercicio, IdSerie, NumeroFactura, Descripcion, EstadoFactura, TipoFactura, FechaCreacion, FechaFactura, BaseImponible) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    9002, serie, (object?)numero ?? DBNull.Value, "Sintética", estado, "F1", DateTime.Now, fecha, 2000m);
                var id = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO LineasFactura (IdFactura, NumeroLinea, Descripcion, BaseImponible, EsSuplido) VALUES (?, ?, ?, ?, ?)", id, (short)1, "Alquiler", 2000m, false);
                await s.EjecutarAsync("INSERT INTO LineasFactura (IdFactura, NumeroLinea, Descripcion, BaseImponible, EsSuplido) VALUES (?, ?, ?, ?, ?)", id, (short)2, "Suplido", 55m, true);
            }
            return 0;
        });
    }

    [Fact]
    public async Task LeeEjerciciosMovimientosVentasCarteraEIva()
    {
        await PrepararAsync();
        var fuente = new FuenteInicioAccess(Bd);

        var ejercicios = await fuente.EjerciciosAsync("T01");
        Assert.Equal([2026, 2025], ejercicios.Select(e => e.Ejercicio));
        Assert.Equal(4, ejercicios[0].TrimestreActivo);

        var d2025 = await fuente.DatosAsync("T01", ejercicios[1]);
        Assert.True(d2025.VentasDeContabilidad);
        Assert.Equal(1000m, d2025.Ventas.Sum(v => v.Base));
        Assert.Equal(d2025.Movimientos.Sum(m => m.Debe), d2025.Movimientos.Sum(m => m.Haber));
        Assert.Equal(1210m, d2025.Cartera.Single().Debe);
        // La liquidación (mueve la 4750) no entra en el IVA.
        Assert.Equal(2, d2025.Iva.Count);
        Assert.Equal("Cliente sintético", d2025.Nombres["4300001"]);

        var d2026 = await fuente.DatosAsync("T01", ejercicios[0]);
        Assert.False(d2026.VentasDeContabilidad);
        Assert.Equal(2000m, d2026.Ventas.Single().Base);
        Assert.Empty(await fuente.BancosAsync("T01", ejercicios[0]));

        // Panel completo de 2025 (terminado): resultado sin la 630 y facturación del grupo 70.
        var servicio = new ServicioInicio(fuente, () => new DateTime(2026, 11, 15));
        var p2025 = await servicio.CalcularAsync("T01", 2025, null);
        Assert.Equal(900m, p2025.Resultado.Valor);
        Assert.Equal(1000m, p2025.Facturacion.Valor);
        Assert.True(p2025.FacturacionDeContabilidad);
        Assert.Equal(5000m - 50m, p2025.Tesoreria.Valor);
        Assert.Equal(1210m, p2025.PendienteCobro.Valor);
        Assert.Equal(121m, p2025.PendientePago.Valor);
        Assert.Equal(4, p2025.Iva.Trimestre);
        Assert.Equal(0m, p2025.Iva.Neto);

        var p2026 = await servicio.CalcularAsync("T01", 2026, 2025);
        Assert.Equal(2000m, p2026.Facturacion.Valor);
        Assert.Equal(1000m, p2026.Facturacion.Comparado);
        Assert.Equal(420m, p2026.Iva.Repercutido);
        Assert.Equal(2025, p2026.Comparado);
    }
}
