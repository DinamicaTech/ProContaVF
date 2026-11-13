using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Datos.Persistencia;
using ProConta.Nominas.Contabilizacion;
using ProConta.Nominas.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Nominas.Tests;

/// <summary>
/// Contabilización de nóminas contra una copia de pruebas: plantilla «Nómina» inventada, regla de fecha del trimestre activo, deshacer
/// (borrado o anulación) y reemplazo. Datos inventados.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class ContabilizacionNominasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");

    public void Dispose() => _copia.Dispose();

    private sealed class RelojFijo(DateTime ahora) : IReloj
    {
        public DateTime Ahora => ahora;
    }

    private IBaseDatos Bd => _copia.Bd();
    private ServicioImportacionNominas Importacion => new(Bd);
    private ServicioEmpleados Empleados => new(Bd, new CatalogoCuentasAccess(Bd), new RepositorioTercerosAccess(Bd), _actor);

    private ServicioContabilizacionNominas Servicio() => new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd), new CatalogoCuentasAccess(Bd),
        new ServicioImportacionNominas(Bd), new RelojFijo(new DateTime(2026, 11, 13, 10, 0, 0)));

    private async Task EmpresaAsync(int trimestreActivo, bool conPlantilla = true)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", "B12345674");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await TrimestreAsync(trimestreActivo);
        var cuentas = new CatalogoCuentasAccess(Bd);
        foreach (var (codigo, nombre) in new[]
        {
            ("6400000", "Sueldos y salarios"), ("6420000", "Seguridad Social a cargo de la empresa"), ("4751000", "HP acreedora por retenciones"),
            ("4760000", "Organismos de la Seguridad Social"), ("7550000", "Ingresos por servicios al personal"),
        })
            Assert.True((await cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre))).EsCorrecto);
        if (conPlantilla)
        {
            var (lineas, errores) = PlantillaNomina.Convertir(PlantillaNominaTests.LineasInventadas);
            Assert.Empty(errores);
            await Servicio().CargarPlantillaAsync("T01", lineas);
        }
    }

    private Task TrimestreAsync(int t) =>
        Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = ? WHERE IdSociedadEjercicio = ?", t, 9001));

    private static ResumenSintetico Septiembre()
    {
        var r = ResumenSintetico.Octubre();
        r.Periodo = "DEL 01/09/2026 AL 30/09/2026";
        return r;
    }

    /// <summary>Importa el resumen y da de alta como empleados a los trabajadores que no se reconocen: queda pendiente de contabilizar.</summary>
    private async Task<int> ImportarPendienteAsync(ResumenSintetico r, string nombre = "nominas.xlsx")
    {
        var imp = await Importacion.ImportarAsync(_ctx, r.Xlsx(), nombre, OrigenNominas.Fichero, _actor);
        Assert.True(imp.EsCorrecto, imp.Error?.ToString());
        var id = imp.Valor.IdImportacion!.Value;
        foreach (var t in imp.Valor.Analisis.Trabajadores.Where(t => !t.Resolucion.Resuelto))
        {
            var alta = await Empleados.AltaAsync(_ctx, t.Fila.Nombre, t.Fila.Nif, t.Fila.CodigoTrabajador);
            Assert.True(alta.EsCorrecto, alta.Error?.ToString());
            Assert.True((await Importacion.ResolverTrabajadorAsync(_ctx, id, t.Fila.Ordinal, alta.Valor.IdEmpleado)).EsCorrecto);
        }
        Assert.Equal(ServicioImportacionNominas.EstadoPendiente, await EstadoAsync(id));
        return id;
    }

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));
    private Task<string?> EstadoAsync(int id) => Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT Estado FROM ImportacionesNomina WHERE IdImportacionNomina = ?", f => f.Texto("Estado"), id));

    private async Task<ResultadoContabilizacionNominas> ContabilizarAsync(int id)
    {
        var r = await Servicio().ContabilizarAsync(id, _actor);
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        return r.Valor;
    }

    [Fact]
    public async Task Contabiliza_un_asiento_por_nomina_con_la_fecha_de_cobro_y_crea_la_476_del_autonomo()
    {
        await EmpresaAsync(trimestreActivo: 4);
        var id = await ImportarPendienteAsync(ResumenSintetico.Octubre());

        var p = await Servicio().ProponerAsync(id);
        Assert.True(p.Contabilizable, string.Join(" | ", p.TodosLosErrores));
        Assert.Equal(new DateTime(2026, 10, 31), p.FechaAsiento);
        Assert.Empty(p.Avisos);
        Assert.Equal(2, p.CuentasNuevas.Count(c => c.Codigo.StartsWith("476000", StringComparison.Ordinal) && c.Codigo != "4760000"));

        var r = await ContabilizarAsync(id);
        Assert.Equal(3, r.Asientos.Count);
        Assert.Equal(ServicioImportacionNominas.EstadoContabilizada, await EstadoAsync(id));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Nominas WHERE IdImportacionNomina = ? AND IdAsiento IS NULL", id));
        var motor = new MotorAsientosAccess(Bd);
        foreach (var idAsiento in r.Asientos)
        {
            var a = (await motor.ObtenerAsync(idAsiento))!;
            Assert.Equal(new DateTime(2026, 10, 31), a.FechaContable);
            Assert.Equal(ServicioContabilizacionNominas.Lote(id, 1), a.Lote);
            Assert.Equal(a.TotalDebe, a.TotalHaber);
            Assert.StartsWith("465", a.Apuntes.OrderBy(x => x.Orden).First().CodigoCuenta);
        }
        // Eva: neto 1.271 a su 465; 1.450 de sueldo y 460 de Seguridad Social.
        var eva = (await motor.ObtenerAsync(r.Asientos[2]))!;
        Assert.Equal("INVENTADA TRES, EVA 31/10/2026", eva.Descripcion);
        Assert.Equal(1910m, eva.TotalDebe.Valor);

        var otra = await Servicio().ContabilizarAsync(id, _actor);
        Assert.Equal("Nominas.YaContabilizada", otra.Error?.Codigo);
    }

    [Fact]
    public async Task Nominas_de_un_trimestre_cerrado_van_al_dia_1_del_trimestre_activo()
    {
        await EmpresaAsync(trimestreActivo: 4);
        var id = await ImportarPendienteAsync(Septiembre());
        var r = await ContabilizarAsync(id);
        Assert.Equal(new DateTime(2026, 10, 1), r.FechaAsiento);
        Assert.Contains("30/09/2026", Assert.Single(r.Avisos));
        Assert.Equal(new DateTime(2026, 10, 1), (await new MotorAsientosAccess(Bd).ObtenerAsync(r.Asientos[0]))!.FechaContable);
    }

    [Fact]
    public async Task Deshacer_en_un_trimestre_abierto_borra_los_asientos_y_deja_las_nominas_pendientes()
    {
        await EmpresaAsync(trimestreActivo: 4);
        var id = await ImportarPendienteAsync(ResumenSintetico.Octubre());
        await ContabilizarAsync(id);

        Assert.Equal("Nominas.MotivoObligatorio", (await Servicio().DeshacerAsync(id, _actor, " ")).Error?.Codigo);
        var d = await Servicio().DeshacerAsync(id, _actor, "La gestoría corrige un importe.");
        Assert.True(d.EsCorrecto, string.Join(" | ", d.Errores));
        Assert.False(d.Valor.Anulados);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'nominas'"));
        Assert.Equal(ServicioImportacionNominas.EstadoPendiente, await EstadoAsync(id));
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Nominas WHERE IdImportacionNomina = ? AND IdAsiento IS NOT NULL", id));

        var otra = await ContabilizarAsync(id);
        Assert.Equal(ServicioContabilizacionNominas.Lote(id, 1), (await new MotorAsientosAccess(Bd).ObtenerAsync(otra.Asientos[0]))!.Lote);
    }

    [Fact]
    public async Task Deshacer_en_un_trimestre_cerrado_anula_con_contra_asientos_y_se_puede_volver_a_contabilizar()
    {
        await EmpresaAsync(trimestreActivo: 3);
        var id = await ImportarPendienteAsync(Septiembre());
        var primera = await ContabilizarAsync(id);
        Assert.Equal(new DateTime(2026, 9, 30), primera.FechaAsiento);

        await TrimestreAsync(4);
        var d = await Servicio().DeshacerAsync(id, _actor, "La gestoría corrige septiembre.");
        Assert.True(d.EsCorrecto, string.Join(" | ", d.Errores));
        Assert.True(d.Valor.Anulados);
        Assert.Equal(new DateTime(2026, 10, 1), d.Valor.FechaAnulacion);
        Assert.Equal(3, d.Valor.AsientosAnulacion.Count);
        Assert.Contains("no se han tocado", d.Valor.Explicacion);
        var motor = new MotorAsientosAccess(Bd);
        foreach (var original in primera.Asientos) Assert.NotNull(await motor.ObtenerAsync(original));
        var anulacion = (await motor.ObtenerAsync(d.Valor.AsientosAnulacion[0]))!;
        Assert.Equal(ServicioContabilizacionNominas.LoteAnulacion(id, 1), anulacion.Lote);
        Assert.StartsWith("Anulación: ", anulacion.Descripcion);
        Assert.Equal(ServicioImportacionNominas.EstadoPendiente, await EstadoAsync(id));

        // Saldo cero en cada cuenta entre el original y su anulación.
        var saldos = await Bd.EjecutarAsync(s => s.ConsultarAsync(
            "SELECT p.CuentaContable, SUM(p.ImporteDebe - p.ImporteHaber) AS Saldo FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento " +
            "WHERE a.MotorPropietario = 'nominas' GROUP BY p.CuentaContable", f => f.Decimal("Saldo")));
        Assert.All(saldos, x => Assert.Equal(0m, x));

        var segunda = await ContabilizarAsync(id);
        Assert.Equal(new DateTime(2026, 10, 1), segunda.FechaAsiento);
        Assert.Equal(ServicioContabilizacionNominas.Lote(id, 2), (await motor.ObtenerAsync(segunda.Asientos[0]))!.Lote);
    }

    [Fact]
    public async Task Reemplazar_una_importacion_contabilizada_la_deshace_importa_la_nueva_y_la_contabiliza()
    {
        await EmpresaAsync(trimestreActivo: 4);
        var v1 = await ImportarPendienteAsync(ResumenSintetico.Octubre());
        await ContabilizarAsync(v1);
        var corregido = new ResumenSintetico()
            .Trabajador("000901", "FICTICIO UNO, ANA", 2300m, dtoEspecie: -40m, autonomo: 1300m)
            .Trabajador("000902", "FICTICIO DOS, LUIS", 2400m, dtoEspecie: -120m, autonomo: 1300m, irpf: 780m)
            .Trabajador("000903", "INVENTADA TRES, EVA", 1500m, ssEmp: 470m, cuotas: 95m, irpf: 90m);

        Assert.Equal("Nominas.MotivoObligatorio", (await Servicio().ReemplazarAsync(_ctx, v1, corregido.Xlsx(), "octubre-v2.xlsx", OrigenNominas.Fichero, _actor, ""))
            .Error?.Codigo);
        var r = await Servicio().ReemplazarAsync(_ctx, v1, corregido.Xlsx(), "octubre-v2.xlsx", OrigenNominas.Fichero, _actor, "La gestoría corrige a Eva.");
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        Assert.False(r.Valor.Deshecha!.Anulados);
        Assert.Empty(r.Valor.ErroresContabilizacion);
        Assert.NotNull(r.Valor.Contabilizada);
        Assert.Equal(ServicioImportacionNominas.EstadoReemplazada, await EstadoAsync(v1));
        Assert.Equal(ServicioImportacionNominas.EstadoContabilizada, await EstadoAsync(r.Valor.Importacion.IdImportacion!.Value));
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'nominas'"));
        Assert.Equal(1500m, await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT SUM(p.ImporteDebe) AS S FROM Apuntes AS p INNER JOIN Asientos AS a ON p.IdAsiento = a.IdAsiento WHERE a.MotorPropietario = 'nominas' AND p.CuentaContable = '6400000' " +
            "AND a.Descripcion LIKE 'INVENTADA%'", f => f.Decimal("S"))));
    }

    [Fact]
    public async Task No_contabiliza_sin_plantilla_ni_con_trabajadores_por_resolver()
    {
        await EmpresaAsync(trimestreActivo: 4, conPlantilla: false);
        var imp = await Importacion.ImportarAsync(_ctx, ResumenSintetico.Octubre().Xlsx(), "octubre.xlsx", OrigenNominas.Fichero, _actor);
        var id = imp.Valor.IdImportacion!.Value;
        var codigos = (await Servicio().ContabilizarAsync(id, _actor)).Errores.Select(e => e.Codigo).ToList();
        Assert.Contains("Nominas.Incidencias", codigos);
        Assert.Contains("Nominas.SinPlantilla", codigos);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Asientos WHERE MotorPropietario = 'nominas'"));
    }
}
