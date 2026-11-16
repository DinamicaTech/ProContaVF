using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Dividendos;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;

namespace ProConta.Contabilidad.Tests;

/// <summary>
/// Reparto de dividendos contra Access (contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55): neto propuesto desde lo pagado en la 526,
/// asiento del reparto, lote regenerable (confirmar dos veces no duplica), corrección con motivo y deshacer. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class DividendosAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("operador.prueba");
    private readonly Dictionary<string, int> _identidades = [];
    private static readonly DateTime Fecha = new(2026, 12, 15);

    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioDividendos Servicio(Func<Task<bool>>? puede = null) =>
        new(Bd, new MotorAsientosAccess(Bd), new CatalogoCuentasAccess(Bd), puede, new RelojFijo(new DateTime(2026, 12, 15, 10, 0, 0)));

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p)));

    private Task<int> AsientosDelLoteAsync(int idReparto) => ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ? AND CodigoProceso = ? AND ClaveEjecucion = ?",
        ServicioDividendos.MotorPropietario, ServicioDividendos.CodigoProceso, idReparto.ToString());

    /// <summary>
    /// Dos socios al 50 % con sus 526 y 4751, 200.000 € en reservas voluntarias y 50.000 € pagados a cuenta a cada uno el 30/06. Un tercer socio sin
    /// subcuentas, inactivo, para comprobar que no sale.
    /// </summary>
    private async Task<int[]> PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[] { ("113", "Reservas voluntarias"), ("526", "Dividendo activo a pagar"), ("475", "HP acreedora"), ("572", "Bancos") })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        foreach (var (codigo, nombre) in new[] { ("1130000", "Reservas voluntarias"), ("5260001", "Socia Uno"), ("5260002", "Socio Dos"),
                     ("4751011", "Socia Uno"), ("4751012", "Socio Dos"), ("5720001", "Banco sintético") })
        {
            var c = await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre));
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            _identidades[codigo] = c.Valor.IdIdentidadContable;
        }
        var motor = new MotorAsientosAccess(Bd);
        var reservas = new Asiento(_ctx, new DateTime(2026, 1, 1), "Reservas sintéticas");
        reservas.Apuntes.AddRange([Apunte.AlDebe("5720001", 200000m), Apunte.AlHaber("1130000", 200000m)]);
        var pago1 = new Asiento(_ctx, new DateTime(2026, 6, 30), "Pago a cuenta Socia Uno");
        pago1.Apuntes.AddRange([Apunte.AlDebe("5260001", 50000m), Apunte.AlHaber("5720001", 50000m)]);
        var pago2 = new Asiento(_ctx, new DateTime(2026, 6, 30), "Pago a cuenta Socio Dos");
        pago2.Apuntes.AddRange([Apunte.AlDebe("5260002", 50000m), Apunte.AlHaber("5720001", 50000m)]);
        Assert.True((await motor.GuardarManualesAsync([reservas, pago1, pago2], _actor, "Datos sintéticos")).EsCorrecto);
        return await Bd.EjecutarAsync(async s =>
        {
            var ids = new List<int>();
            foreach (var (nombre, c526, c4751, activo) in new[] { ("Socia Uno", "5260001", "4751011", true), ("Socio Dos", "5260002", "4751012", true), ("Socio Antiguo", null, null, false) })
            {
                await s.EjecutarAsync("INSERT INTO Terceros (Nombre, Activo, Version) VALUES (?, ?, ?)", nombre, true, 0);
                var t = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO Socios (IdTercero, Activo, FechaAlta) VALUES (?, ?, ?)", t, true, new DateTime(2020, 1, 1));
                var socio = await s.UltimoIdAsync();
                await s.EjecutarAsync("INSERT INTO SociosSociedad (IdSocio, IdSociedad, Participacion, IdIdentidadDividendos, IdIdentidadRetencion, Activo) VALUES (?, ?, ?, ?, ?, ?)",
                    socio, "T01", 50m, c526 is null ? null : _identidades[c526], c4751 is null ? null : _identidades[c4751], activo);
                ids.Add(socio);
            }
            return ids.ToArray();
        });
    }

    [Fact]
    public async Task Propone_lo_pagado_y_graba_el_asiento_del_reparto_como_el_de_2025()
    {
        var socios = await PrepararAsync();
        var servicio = Servicio();
        var datos = await servicio.DatosAsync(_ctx, Fecha);
        Assert.Equal(2, datos.Socios.Count);
        Assert.All(datos.Socios, s => { Assert.Equal(50000m, s.Pagado); Assert.Equal(50000m, s.NetoPropuesto); Assert.True(s.Incluido); Assert.Null(s.Error); });
        Assert.Equal(new ConfiguracionDividendos(19m, "1130000"), datos.Configuracion);

        var netos = datos.Socios.Select(s => new NetoSocio(s.IdSocio, s.NetoPropuesto)).ToList();
        var vista = (await servicio.ProponerAsync(_ctx, Fecha, netos)).Valor;
        Assert.Empty(vista.Avisos);
        var r = await servicio.ConfirmarAsync(_ctx, Fecha, netos, null, null, _actor, vista);
        Assert.True(r.EsCorrecto, r.Error?.TextoOperador());
        Assert.Equal(1, await AsientosDelLoteAsync(r.Valor.IdReparto));

        var apuntes = await Bd.EjecutarAsync(s => s.ConsultarAsync("SELECT CuentaContable, ImporteDebe, ImporteHaber FROM Apuntes WHERE IdAsiento = ? ORDER BY Orden",
            f => ((f.Texto("CuentaContable") ?? "").Trim(), f.DecimalNulo("ImporteDebe") ?? 0m, f.DecimalNulo("ImporteHaber") ?? 0m), r.Valor.IdAsiento));
        Assert.Equal([("1130000", 123456.80m, 0m), ("5260001", 0m, 61728.40m), ("5260001", 11728.40m, 0m), ("4751011", 0m, 11728.40m),
            ("5260002", 0m, 61728.40m), ("5260002", 11728.40m, 0m), ("4751012", 0m, 11728.40m)], apuntes);
        Assert.Equal(apuntes.Sum(a => a.Item2), apuntes.Sum(a => a.Item3));

        var guardado = Assert.Single(await servicio.RepartosAsync(_ctx));
        Assert.Equal((100000m, 123456.80m, 23456.80m, r.Valor.IdAsiento), (guardado.Neto, guardado.Bruto, guardado.Retencion, guardado.IdAsiento));
        Assert.Equal(socios[..2].Order(), guardado.Socios.Select(s => s.IdSocio).Order());
        // Después del reparto, las 526 quedan a cero: lo pagado a cuenta ya es el neto del reparto.
        var despues = await servicio.DatosAsync(_ctx, Fecha);
        Assert.All(despues.Socios, s => Assert.Equal(0m, s.Pagado));
    }

    [Fact]
    public async Task Confirmar_dos_veces_corrige_el_mismo_lote_sin_duplicar_y_pide_motivo()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var datos = await servicio.DatosAsync(_ctx, Fecha);
        var netos = datos.Socios.Select(s => new NetoSocio(s.IdSocio, 50000m)).ToList();
        var r = await servicio.ConfirmarAsync(_ctx, Fecha, netos, null, null, _actor);
        Assert.True(r.EsCorrecto, r.Error?.TextoOperador());
        var id = r.Valor.IdReparto;

        Assert.Equal("Dividendos.SinMotivo", (await servicio.ConfirmarAsync(_ctx, Fecha, netos, id, " ", _actor)).Error!.Codigo);
        // Al corregir, lo propuesto es lo del reparto guardado y lo pagado no cuenta el propio reparto.
        var otra = await servicio.DatosAsync(_ctx, Fecha, id);
        Assert.All(otra.Socios, s => { Assert.Equal(50000m, s.Pagado); Assert.Equal(50000m, s.NetoPropuesto); });
        var corregido = await servicio.ConfirmarAsync(_ctx, Fecha, [netos[0] with { Neto = 40000m }, netos[1] with { Neto = 60000m }], id, "Neto acordado en la junta", _actor);
        Assert.True(corregido.EsCorrecto, corregido.Error?.TextoOperador());
        Assert.Equal(id, corregido.Valor.IdReparto);
        Assert.Equal(r.Valor.IdAsiento, corregido.Valor.IdAsiento);
        Assert.Equal(1, await AsientosDelLoteAsync(id));
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM RepartosDividendos"));
        Assert.Equal(2, await ContarAsync("SELECT COUNT(*) FROM RepartosDividendosSocios"));
        var g = Assert.Single(await servicio.RepartosAsync(_ctx));
        Assert.Equal("Neto acordado en la junta", g.Motivo);
        Assert.Equal(CalculoDividendos.Bruto(40000m, 19m) + CalculoDividendos.Bruto(60000m, 19m), g.Bruto);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ?", ServicioDividendos.MotorPropietario));
    }

    [Fact]
    public async Task Deshacer_borra_el_asiento_y_el_reparto_con_motivo()
    {
        await PrepararAsync();
        var servicio = Servicio();
        var netos = (await servicio.DatosAsync(_ctx, Fecha)).Socios.Select(s => new NetoSocio(s.IdSocio, 50000m)).ToList();
        var r = await servicio.ConfirmarAsync(_ctx, Fecha, netos, null, null, _actor);
        Assert.True(r.EsCorrecto, r.Error?.TextoOperador());
        Assert.Equal("Dividendos.SinMotivo", (await servicio.DeshacerAsync(_ctx, r.Valor.IdReparto, "", _actor)).Error!.Codigo);
        var d = await servicio.DeshacerAsync(_ctx, r.Valor.IdReparto, "Prueba de deshacer", _actor);
        Assert.True(d.EsCorrecto, d.Error?.TextoOperador());
        Assert.Equal(0, await AsientosDelLoteAsync(r.Valor.IdReparto));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM RepartosDividendos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM RepartosDividendosSocios"));
        Assert.All((await servicio.DatosAsync(_ctx, Fecha)).Socios, s => Assert.Equal(50000m, s.Pagado));
    }

    [Fact]
    public async Task Sin_permiso_o_fuera_del_ejercicio_no_se_graba_nada()
    {
        await PrepararAsync();
        var netos = (await Servicio().DatosAsync(_ctx, Fecha)).Socios.Select(s => new NetoSocio(s.IdSocio, 50000m)).ToList();
        Assert.Equal("Dividendos.SinPermiso", (await Servicio(() => Task.FromResult(false)).ConfirmarAsync(_ctx, Fecha, netos, null, null, _actor)).Error!.Codigo);
        Assert.Equal("Dividendos.FechaFueraDelEjercicio", (await Servicio().ConfirmarAsync(_ctx, new DateTime(2027, 1, 5), netos, null, null, _actor)).Error!.Codigo);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM RepartosDividendos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ?", ServicioDividendos.MotorPropietario));
    }

    [Fact]
    public async Task Si_el_asiento_no_se_graba_no_queda_la_fila_del_reparto()
    {
        await PrepararAsync();
        // Con el ejercicio cerrado, el motor rechaza el asiento después de dar de alta la fila del reparto: la fila se borra.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET EjercicioCerrado = True WHERE IdSociedadEjercicio = 9001"));
        var netos = (await Servicio().DatosAsync(_ctx, Fecha)).Socios.Select(s => new NetoSocio(s.IdSocio, 50000m)).ToList();
        var r = await Servicio().ConfirmarAsync(_ctx, Fecha, netos, null, null, _actor);
        Assert.False(r.EsCorrecto);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM RepartosDividendos"));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = ?", ServicioDividendos.MotorPropietario));
    }
}
