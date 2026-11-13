using System.Globalization;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Correo;
using ProConta.Datos.Persistencia;
using ProConta.Nominas.Consulta;
using ProConta.Nominas.Contabilizacion;
using ProConta.Nominas.Correo;
using ProConta.Nominas.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Nominas.Tests;

/// <summary>
/// Consultas de las pantallas de nóminas (periodos con sus totales y versiones, nóminas del periodo, pestaña del empleado) y el
/// Excel recibido por correo, contra una copia de pruebas con datos inventados (nominas/pantallas, correo/recepcion).
/// </summary>
[Trait("Categoria", "Access")]
public sealed class PantallasNominasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");
    private readonly string _carpeta = BdPruebas.CarpetaTemporal();

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioImportacionNominas Importacion => new(Bd);
    private ServicioEmpleados Empleados => new(Bd, new CatalogoCuentasAccess(Bd), new RepositorioTercerosAccess(Bd), _actor);
    private ServicioContabilizacionNominas Contabilizacion => new(Bd, new MotorAsientosAccess(Bd), new ServicioPlantillasAsiento(Bd), new CatalogoCuentasAccess(Bd), Importacion);
    private ConsultaNominas Consulta => new(Bd);

    private async Task EmpresaAsync(bool conPlantilla = true)
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", "B12345674");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE SociedadesEjercicios SET TrimestreActivo = 4 WHERE IdSociedadEjercicio = 9001"));
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
            await Contabilizacion.CargarPlantillaAsync("T01", lineas);
        }
    }

    /// <summary>Da de alta como empleados de la sociedad a los trabajadores del resumen (por su código).</summary>
    private async Task EmpleadosAsync(ResumenSintetico r)
    {
        foreach (var (codigo, nombre, _) in r.Trabajadores)
            Assert.True((await Empleados.AltaAsync(_ctx, nombre, null, codigo)).EsCorrecto);
    }

    private async Task<int> ImportarYContabilizarAsync(ResumenSintetico r, string nombre)
    {
        var imp = await Importacion.ImportarAsync(_ctx, r.Xlsx(), nombre, OrigenNominas.Fichero, _actor);
        Assert.True(imp.EsCorrecto, imp.Error?.ToString());
        var id = imp.Valor.IdImportacion!.Value;
        var c = await Contabilizacion.ContabilizarAsync(id, _actor);
        Assert.True(c.EsCorrecto, string.Join(" | ", c.Errores));
        return id;
    }

    private static ResumenSintetico Septiembre()
    {
        var r = ResumenSintetico.Octubre();
        r.Periodo = "DEL 01/09/2026 AL 30/09/2026";
        return r;
    }

    private static ResumenSintetico OctubreCorregido() => new ResumenSintetico()
        .Trabajador("000901", "FICTICIO UNO, ANA", 2300m, dtoEspecie: -40m, autonomo: 1300m)
        .Trabajador("000902", "FICTICIO DOS, LUIS", 2400m, dtoEspecie: -120m, autonomo: 1300m, irpf: 780m)
        .Trabajador("000903", "INVENTADA TRES, EVA", 1500m, ssEmp: 470m, cuotas: 95m, irpf: 90m);

    [Fact]
    public async Task Lista_los_periodos_con_sus_totales_y_numera_las_versiones_reemplazadas()
    {
        await EmpresaAsync();
        await EmpleadosAsync(ResumenSintetico.Octubre());
        var v1 = await ImportarYContabilizarAsync(ResumenSintetico.Octubre(), "octubre.xlsx");

        var p = Assert.Single(await Consulta.ImportacionesAsync(_ctx));
        Assert.Equal(v1, p.IdImportacion);
        Assert.Equal("Octubre 2026", p.TextoPeriodo);
        Assert.Equal((3, 0, 3, 1), (p.Trabajadores, p.SinResolver, p.ConAsiento, p.Version));
        Assert.Equal(6150m, p.Bruto);
        Assert.Equal(ServicioImportacionNominas.EstadoContabilizada, p.Estado);

        var r = await Contabilizacion.ReemplazarAsync(_ctx, v1, OctubreCorregido().Xlsx(), "octubre-v2.xlsx", OrigenNominas.Fichero, _actor, "La gestoría corrige a Eva.");
        Assert.True(r.EsCorrecto, string.Join(" | ", r.Errores));
        var v2 = r.Valor.Importacion.IdImportacion!.Value;

        var vigente = Assert.Single(await Consulta.ImportacionesAsync(_ctx));
        Assert.Equal((v2, 2, v1), (vigente.IdImportacion, vigente.Version, vigente.IdImportacionSustituida));
        Assert.Equal(6200m, vigente.Bruto);
        var todas = await Consulta.ImportacionesAsync(_ctx, incluirAntiguas: true);
        Assert.Equal([v2, v1], todas.Select(i => i.IdImportacion));
        Assert.False(todas[1].EsVigente);
        Assert.Equal("La gestoría corrige a Eva.", todas[1].MotivoEstado);
        Assert.Equal(2, (await Consulta.ImportacionAsync(v2))!.Version);

        var nominas = await Consulta.NominasAsync(v2);
        Assert.Equal(["000901", "000902", "000903"], nominas.Select(n => n.Fila.CodigoTrabajador));
        Assert.All(nominas, n =>
        {
            Assert.NotNull(n.IdEmpleado);
            Assert.NotNull(n.IdAsiento);
            Assert.StartsWith("465", n.Cuenta465);
            Assert.False(string.IsNullOrEmpty(n.NombreTercero));
        });
        Assert.Equal(1500m, nominas[2].Fila["Bruto"]);
    }

    [Fact]
    public async Task La_pestana_del_empleado_ve_su_codigo_su_465_y_sus_nominas_vigentes()
    {
        await EmpresaAsync();
        await EmpleadosAsync(ResumenSintetico.Octubre());
        await ImportarYContabilizarAsync(Septiembre(), "septiembre.xlsx");
        var octubre = await ImportarYContabilizarAsync(ResumenSintetico.Octubre(), "octubre.xlsx");
        var idTercero = await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync(
            "SELECT e.IdTercero FROM Empleados AS e INNER JOIN EmpleadosSociedad AS es ON e.IdEmpleado = es.IdEmpleado WHERE es.CodigoTrabajador = '000902'", f => f.Entero("IdTercero")));

        var empleado = await Consulta.EmpleadoAsync(_ctx, idTercero);
        Assert.NotNull(empleado);
        Assert.Equal("000902", empleado.CodigoTrabajador);
        Assert.StartsWith("465", empleado.Cuenta465);
        Assert.Equal(new DateTime(2026, 9, 30), empleado.PrimeraNomina);
        Assert.True(empleado.Activo);

        var nominas = await Consulta.DeEmpleadoAsync(_ctx, empleado.IdEmpleado);
        Assert.Equal([new DateTime(2026, 10, 31), new DateTime(2026, 9, 30)], nominas.Select(n => n.Nomina.Fila.FechaNomina));
        Assert.All(nominas, n => Assert.Equal((2026, 2400m), (n.Ejercicio, n.Nomina.Fila["Bruto"])));
        Assert.All(nominas, n => Assert.NotNull(n.Nomina.IdAsiento));

        // Tras deshacer octubre, la nómina sigue (pendiente) pero sin asiento; tras reemplazarla, solo se ve la vigente.
        Assert.True((await Contabilizacion.DeshacerAsync(octubre, _actor, "Prueba")).EsCorrecto);
        Assert.Null((await Consulta.DeEmpleadoAsync(_ctx, empleado.IdEmpleado))[0].Nomina.IdAsiento);
        Assert.True((await Contabilizacion.ReemplazarAsync(_ctx, octubre, OctubreCorregido().Xlsx(), "octubre-v2.xlsx", OrigenNominas.Fichero, _actor, "Corrección")).EsCorrecto);
        Assert.Equal(2, (await Consulta.DeEmpleadoAsync(_ctx, empleado.IdEmpleado)).Count);

        Assert.Null(await Consulta.EmpleadoAsync(_ctx, 987654));
    }

    // ---------- recibido por correo ----------

    /// <summary>Un adjunto recibido de verdad en la BD (la importación lo enlaza por clave ajena).</summary>
    private Task<int> AdjuntoAsync(string nombre) => Bd.EnTransaccionAsync(async s =>
    {
        await s.EjecutarAsync("INSERT INTO CuentasCorreo (IdSociedadEjercicio, Activa, Direccion, PuertoSmtp, SeguridadSmtp, PuertoPop3, SeguridadPop3, EliminarTrasProcesar, " +
            "IntervaloRecepcionMinutos, TiempoEsperaSegundos, Version) VALUES (9001, True, 'nominas@example.invalid', 587, 'StartTls', 995, 'Ssl', False, 15, 30, 1)");
        var cuenta = await s.UltimoIdAsync();
        await s.EjecutarAsync("INSERT INTO MensajesRecibidosCorreo (IdCuentaCorreo, IdSociedadEjercicio, Uidl, FechaRecepcion, TamanoBytes, Huella, RutaEml, Estado, " +
            "EliminadoServidor, Version) VALUES (?, 9001, 'u1', ?, 100, 'h', 'correo\\u1.eml', 'Procesando', False, 1)", cuenta, new DateTime(2026, 10, 31, 9, 0, 0));
        var mensaje = await s.UltimoIdAsync();
        await s.EjecutarAsync("INSERT INTO AdjuntosRecibidosCorreo (IdMensajeRecibido, Ordinal, Nombre, TamanoBytes, Huella, RutaRelativa, Tipo, Estado) " +
            "VALUES (?, 1, ?, 100, 'h', ?, 'Nomina', 'Pendiente')", mensaje, nombre, "correo\\" + nombre);
        return await s.UltimoIdAsync();
    });

    private async Task<ResultadoNominasRecibidas> RecibirAsync(ResumenSintetico r, string nombre)
    {
        var ruta = Path.Combine(_carpeta, nombre);
        await File.WriteAllBytesAsync(ruta, r.Xlsx());
        var id = await AdjuntoAsync(nombre);
        return await new ProcesadorNominasCorreo(Importacion, Contabilizacion).ProcesarAsync(_ctx, ruta, nombre, id, _actor);
    }

    [Fact]
    public async Task Por_correo_se_importa_y_se_contabiliza_si_reconoce_a_todos_y_no_se_duplica()
    {
        await EmpresaAsync();
        await EmpleadosAsync(ResumenSintetico.Octubre());

        var r = await RecibirAsync(ResumenSintetico.Octubre(), "nominas-octubre.xlsx");
        Assert.Equal(EstadoAdjuntoRecibido.Procesado, r.Estado);
        Assert.Contains("Contabilizadas en 3 asiento(s) del 31/10/2026", r.Explicacion);
        var p = Assert.Single(await Consulta.ImportacionesAsync(_ctx));
        Assert.Equal((OrigenNominas.Correo, ServicioImportacionNominas.EstadoContabilizada), (p.Origen, p.Estado));
        Assert.NotNull(p.IdAdjuntoRecibido);

        var otra = await RecibirAsync(ResumenSintetico.Octubre(), "nominas-octubre-reenvio.xlsx");
        Assert.Equal(EstadoAdjuntoRecibido.Procesado, otra.Estado);
        Assert.Single(await Consulta.ImportacionesAsync(_ctx));
    }

    [Fact]
    public async Task Por_correo_queda_para_revisar_si_falta_un_trabajador_o_no_se_puede_contabilizar()
    {
        await EmpresaAsync(conPlantilla: false);
        var octubre = ResumenSintetico.Octubre();
        foreach (var (codigo, nombre, _) in octubre.Trabajadores.Take(2))
            Assert.True((await Empleados.AltaAsync(_ctx, nombre, null, codigo)).EsCorrecto);

        var r = await RecibirAsync(octubre, "nominas-octubre.xlsx");
        Assert.Equal(EstadoAdjuntoRecibido.Revisar, r.Estado);
        Assert.Contains("Resolver trabajador", r.Explicacion);
        var p = Assert.Single(await Consulta.ImportacionesAsync(_ctx));
        Assert.Equal((ServicioImportacionNominas.EstadoIncidencias, 1), (p.Estado, p.SinResolver));

        // Septiembre, con todos reconocidos pero sin plantilla «Nómina»: se importa y queda pendiente, para revisar.
        Assert.True((await Empleados.AltaAsync(_ctx, "INVENTADA TRES, EVA", null, "000903")).EsCorrecto);
        var s = await RecibirAsync(Septiembre(), "nominas-septiembre.xlsx");
        Assert.Equal(EstadoAdjuntoRecibido.Revisar, s.Estado);
        Assert.Contains("No se han podido contabilizar", s.Explicacion);
        Assert.Contains(await Consulta.ImportacionesAsync(_ctx), i => i.Estado == ServicioImportacionNominas.EstadoPendiente && i.Origen == OrigenNominas.Correo);
    }
}
