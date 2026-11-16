using System.Globalization;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nominas.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros;
using ProConta.Terceros.Roles;

namespace ProConta.Nominas.Tests;

/// <summary>Importación de nóminas, alias, empleados y reemplazo contra una copia de pruebas con datos inventados.</summary>
[Trait("Categoria", "Access")]
public sealed class ImportacionNominasAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Actor _actor = new("Pruebas");

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private ServicioImportacionNominas Servicio => new(Bd);
    private ServicioEmpleados Empleados => new(Bd, new CatalogoCuentasAccess(Bd), new RepositorioTercerosAccess(Bd), _actor);

    private async Task EmpresaAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética", "B12345674");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        await DatosSinteticos.SociedadAsync(Bd, "T02", "Otra sociedad sintética", "B87654321");
        await DatosSinteticos.EjercicioAsync(Bd, 9101, "T02", 2026);
    }

    private Task<int> ContarAsync(string sql, params object?[] p) => Bd.EjecutarAsync(async s => Convert.ToInt32(await s.EscalarAsync(sql, p), CultureInfo.InvariantCulture));
    private Task<string?> EstadoAsync(int id) => Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT Estado FROM ImportacionesNomina WHERE IdImportacionNomina = ?", f => f.Texto("Estado"), id));

    private async Task<ResultadoImportacionNominas> ImportarAsync(ResumenSintetico r, string nombre = "octubre.xlsx", int? reemplazar = null, string? motivo = null,
        IReadOnlyList<AliasNuevo>? alias = null)
    {
        var res = await Servicio.ImportarAsync(_ctx, r.Xlsx(), nombre, OrigenNominas.Fichero, _actor, alias, reemplazar, motivo: motivo);
        Assert.True(res.EsCorrecto, res.Error?.ToString());
        return res.Valor;
    }

    [Fact]
    public async Task Importa_las_nominas_con_trabajadores_por_resolver_y_el_mismo_fichero_no_se_duplica()
    {
        await EmpresaAsync();
        var r = await ImportarAsync(ResumenSintetico.Octubre());
        Assert.Equal(VeredictoNominas.Importable, r.Veredicto);
        Assert.Equal(ServicioImportacionNominas.EstadoIncidencias, r.Estado);
        Assert.Equal(3, await ContarAsync("SELECT COUNT(*) FROM Nominas WHERE IdImportacionNomina = ? AND IdEmpleado IS NULL", r.IdImportacion));
        Assert.Equal(1271m, await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT Neto FROM Nominas WHERE CodigoTrabajador = '000903'", f => f.Decimal("Neto"))));
        Assert.Equal(new DateTime(2026, 10, 31), await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT FechaNomina FROM ImportacionesNomina", f => f.Fecha("FechaNomina"))));

        var otra = await ImportarAsync(ResumenSintetico.Octubre(), "octubre-otra-vez.xlsx");
        Assert.Equal(VeredictoNominas.YaImportado, otra.Veredicto);
        Assert.Equal(r.IdImportacion, otra.IdImportacion);
        Assert.Equal(1, await ContarAsync("SELECT COUNT(*) FROM ImportacionesNomina"));
    }

    [Fact]
    public async Task Resolver_los_trabajadores_crea_su_465_y_el_mes_siguiente_se_reconocen_solos()
    {
        await EmpresaAsync();
        var r = await ImportarAsync(ResumenSintetico.Octubre());
        var analisis = r.Analisis;
        foreach (var t in analisis.Trabajadores)
        {
            Assert.Empty(t.Resolucion.Candidatos);
            var alta = await Empleados.AltaAsync(_ctx, t.Fila.Nombre, t.Fila.Nif, t.Fila.CodigoTrabajador);
            Assert.True(alta.EsCorrecto, alta.Error?.ToString());
            Assert.StartsWith("465", alta.Valor.Cuenta);
            Assert.True((await Servicio.ResolverTrabajadorAsync(_ctx, r.IdImportacion!.Value, t.Fila.Ordinal, alta.Valor.IdEmpleado)).EsCorrecto);
        }
        Assert.Equal(ServicioImportacionNominas.EstadoPendiente, await EstadoAsync(r.IdImportacion!.Value));
        Assert.Equal(3, (await Empleados.ListarAsync(_ctx)).Select(e => e.Cuenta).Distinct().Count());
        Assert.Equal("Eva", (await new RepositorioTercerosAccess(Bd).ObtenerAsync((await Empleados.ListarAsync(_ctx)).First(e => e.CodigoTrabajador == "000903").IdTercero))!.NombrePersona);

        // Uno de ellos está de baja en la sociedad (rol Empleado desactivado en la ficha): se sigue reconociendo, con aviso.
        await Bd.EjecutarAsync(s => s.EjecutarAsync("UPDATE EmpleadosSociedad SET Activo = False WHERE CodigoTrabajador = '000903'"));
        var noviembre = ResumenSintetico.Octubre();
        noviembre.Periodo = "DEL 01/11/2026 AL 30/11/2026";
        var n = await ImportarAsync(noviembre, "noviembre.xlsx");
        Assert.Equal(ServicioImportacionNominas.EstadoPendiente, n.Estado);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM Nominas WHERE IdImportacionNomina = ? AND IdEmpleado IS NULL", n.IdImportacion));
        Assert.Single(n.Analisis.DeBaja);
        Assert.Contains("de baja en la sociedad", n.Explicacion);
    }

    [Fact]
    public async Task Un_tercero_con_el_mismo_NIF_es_candidato_pero_no_se_asigna_solo()
    {
        await EmpresaAsync();
        var t = new Tercero { TipoPersona = TipoPersona.Fisica, NombrePersona = "Ana", PrimerApellido = "Ficticio", SegundoApellido = "Uno", Nif = "00000000T" };
        Assert.True((await new RepositorioTercerosAccess(Bd).GuardarAsync(t, _actor)).EsCorrecto);
        var r = ResumenSintetico.Octubre();
        r.Cabeceras.Insert(1, "N.I.F.");
        r.Trabajadores[0].Valores["N.I.F."] = "00000000T";
        var a = await Servicio.AnalizarAsync(_ctx, r.Xlsx());
        var ana = a.Trabajadores[0].Resolucion;
        Assert.False(ana.Resuelto);
        Assert.Equal("mismo NIF", Assert.Single(ana.Candidatos).Motivo);
    }

    [Fact]
    public async Task Otra_version_del_mismo_mes_pide_reemplazar_con_motivo()
    {
        await EmpresaAsync();
        var v1 = await ImportarAsync(ResumenSintetico.Octubre());
        var corregido = new ResumenSintetico()
            .Trabajador("000901", "FICTICIO UNO, ANA", 2300m, dtoEspecie: -40m, autonomo: 1300m)
            .Trabajador("000902", "FICTICIO DOS, LUIS", 2400m, dtoEspecie: -120m, autonomo: 1300m, irpf: 780m)
            .Trabajador("000903", "INVENTADA TRES, EVA", 1250m, ssEmp: -1050m, cuotas: 90m);
        var a = await Servicio.AnalizarAsync(_ctx, corregido.Xlsx());
        Assert.Equal(VeredictoNominas.OtraVersion, a.Veredicto);
        Assert.Equal(v1.IdImportacion, a.IdImportacionExistente);

        var sinMotivo = await Servicio.ImportarAsync(_ctx, corregido.Xlsx(), "octubre-v2.xlsx", OrigenNominas.Fichero, _actor, reemplazar: v1.IdImportacion);
        Assert.Equal("Nominas.MotivoObligatorio", sinMotivo.Error?.Codigo);

        var v2 = await ImportarAsync(corregido, "octubre-v2.xlsx", v1.IdImportacion, "La gestoría corrige la baja.");
        Assert.Equal(VeredictoNominas.Importable, v2.Veredicto);
        Assert.Equal(ServicioImportacionNominas.EstadoReemplazada, await EstadoAsync(v1.IdImportacion!.Value));
        Assert.Equal(v1.IdImportacion, await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT IdImportacionSustituida FROM ImportacionesNomina WHERE IdImportacionNomina = ?",
            f => f.EnteroNulo("IdImportacionSustituida"), v2.IdImportacion)));
    }

    [Fact]
    public async Task Rechaza_el_fichero_de_otra_sociedad_o_de_otro_ejercicio_diciendo_cual()
    {
        await EmpresaAsync();
        var otraSociedad = ResumenSintetico.Octubre();
        otraSociedad.Empresa = "Empresa: 8888-OTRA SINTETICA SL NIF: B87654321";
        var a = await Servicio.AnalizarAsync(_ctx, otraSociedad.Xlsx());
        Assert.Equal(VeredictoNominas.OtraSociedad, a.Veredicto);
        Assert.Contains("T02", a.Explicacion);

        var otroAnio = ResumenSintetico.Octubre();
        otroAnio.Periodo = "DEL 01/12/2025 AL 31/12/2025";
        var b = await Servicio.AnalizarAsync(_ctx, otroAnio.Xlsx());
        Assert.Equal(VeredictoNominas.OtroEjercicio, b.Veredicto);
        Assert.Contains("2025", b.Explicacion);
        Assert.Equal(0, await ContarAsync("SELECT COUNT(*) FROM ImportacionesNomina"));
    }

    [Fact]
    public async Task Una_cabecera_nueva_se_decide_una_vez_y_queda_guardada_para_la_sociedad()
    {
        await EmpresaAsync();
        var r = ResumenSintetico.Octubre();
        r.Cabeceras.Add("Enfermedad/ accidente IT");
        r.Trabajadores[2].Valores["Enfermedad/ accidente IT"] = 15.0;
        Assert.Equal(VeredictoNominas.CabecerasSinAlias, (await Servicio.AnalizarAsync(_ctx, r.Xlsx())).Veredicto);

        var hecho = await ImportarAsync(r, alias: [new AliasNuevo("Enfermedad/ accidente IT", "EnfermedadAccidente")]);
        Assert.Equal(VeredictoNominas.Importable, hecho.Veredicto);
        Assert.Equal(15m, await Bd.EjecutarAsync(s => s.ConsultarPrimeroAsync("SELECT EnfermedadAccidente FROM Nominas WHERE CodigoTrabajador = '000903'", f => f.Decimal("EnfermedadAccidente"))));
        Assert.Equal("EnfermedadAccidente", (await Bd.EjecutarAsync(s => ServicioImportacionNominas.AliasAsync(s, "T01")))["ENFERMEDAD/ ACCIDENTE IT"]);
    }
}
