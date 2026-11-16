using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Roles;

namespace ProConta.Terceros.Tests;

/// <summary>
/// Rol Socio (terceros/roles, derivado de contabilidad/procesos/aplicacion-resultado 2026-11-16 17:55): alta con su % de participación, subcuentas 526 y
/// 4751 creadas al guardar si faltan, historial de los cambios del % y quitar el rol. Datos sintéticos.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class SociosAccessTests : IDisposable
{
    private sealed class RelojFijo(DateTime ahora) : IReloj { public DateTime Ahora => ahora; }

    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9002, 2026);

    public void Dispose() => _copia.Dispose();

    private IBaseDatos Bd => _copia.Bd();
    private RepositorioTercerosAccess Repo => new(Bd);
    private CatalogoCuentasAccess Cuentas => new(Bd);
    private ServicioRoles Roles => new(Bd, Cuentas);
    private SubcuentasRoles Sub => new(Bd, Cuentas, new RelojFijo(new DateTime(2026, 11, 16, 10, 0, 0)));

    private async Task PrepararAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9002, "T01", 2026);
        foreach (var (codigo, nombre) in new[] { ("526", "Dividendo activo a pagar"), ("475", "HP acreedora") })
            Assert.True((await Cuentas.AltaAsync(_ctx, new AltaCuenta(codigo, nombre, EsImputable: false))).EsCorrecto);
        // Como en la copia de pruebas: ya existen subcuentas de socios y una 4751 de otra retención.
        foreach (var (codigo, nombre) in new[] { ("5260001", "Socia anterior"), ("4751000", "Retenciones profesionales"), ("4751011", "Socia anterior") })
            Assert.True((await Cuentas.AltaSubcuentaAsync(_ctx, codigo, nombre)).EsCorrecto);
    }

    private async Task<Tercero> Guardar(Tercero t)
    {
        var r = await Repo.GuardarAsync(t.Clonar(), _actor);
        Assert.True(r.EsCorrecto, r.Error?.ToString());
        return r.Valor;
    }

    /// <summary>Lo que hace la ficha al guardar: crea las subcuentas que faltan del rol y las asigna antes de guardar.</summary>
    private async Task<Tercero> GuardarComoLaFicha(Tercero t)
    {
        foreach (var clase in SubcuentasRoles.QueCrear(t.Roles, t.CuentasRoles))
        {
            var c = await Sub.CrearAsync(clase, t.NombreCalculado(), _ctx);
            Assert.True(c.EsCorrecto, c.Error?.ToString());
            t.CuentasRoles!.Fijar(clase, c.Valor.IdIdentidadContable);
        }
        return await Guardar(t);
    }

    private static Tercero Ficha(Tercero t, string idSociedad)
    {
        var copia = t.Clonar();
        copia.CuentasRoles ??= new CuentasRolesFicha(idSociedad);
        return copia;
    }

    [Fact]
    public async Task Dar_el_rol_Socio_crea_sus_subcuentas_526_y_4751_y_guarda_su_participacion()
    {
        await PrepararAsync();
        var t = new Tercero { TipoPersona = TipoPersona.Fisica, NombrePersona = "Lucía", PrimerApellido = "Sintética", CuentasRoles = new CuentasRolesFicha("T01") };
        t.Roles.Socio = new RolSocio("T01") { Participacion = 50m };
        Assert.Equal([ClaseCuentaRol.Dividendos, ClaseCuentaRol.RetencionDividendos], SubcuentasRoles.QueCrear(t.Roles, t.CuentasRoles));
        var g = await GuardarComoLaFicha(t);

        Assert.NotNull(g.Roles.Socio?.IdSocio);
        Assert.Equal(50m, g.Roles.Socio!.Participacion);
        var cuentas = await Roles.CuentasAsync(g.IdTercero, _ctx);
        Assert.Equal("5260002", cuentas.Dividendos?.Codigo);
        Assert.Equal("4751012", cuentas.RetencionDividendos?.Codigo);
        Assert.Equal("Lucía Sintética", cuentas.Dividendos?.Nombre);
        // Ya no falta ninguna.
        Assert.Empty(SubcuentasRoles.QueCrear(g.Roles, g.CuentasRoles));
        // El reparto y el pago de dividendos los leen así.
        var socio = Assert.Single(await new ProConta.Contabilidad.Dividendos.ConsultaSocios(Bd).ActivosAsync("T01", 9002));
        Assert.Equal(("Lucía Sintética", 50m, "5260002", "4751012"), (socio.Nombre, socio.Participacion, socio.Dividendos?.Codigo, socio.Retencion?.Codigo));
        // Y el buscador lo enseña con su rol.
        var r = await new BuscadorTerceros(Bd).BuscarAsync(new FiltroTerceros(Roles: new HashSet<RolBusqueda> { RolBusqueda.Socio }));
        Assert.Contains(r.Filas, f => f.IdTercero == g.IdTercero && f.Roles.Any(x => x.Rol == RolBusqueda.Socio));
        Assert.Equal(1, r.Recuentos.Socios);
    }

    [Fact]
    public async Task Usa_las_subcuentas_que_ya_tiene_y_guarda_en_el_historial_los_cambios_del_porcentaje()
    {
        await PrepararAsync();
        var t = new Tercero { TipoPersona = TipoPersona.Fisica, NombrePersona = "Socia", PrimerApellido = "Anterior", CuentasRoles = new CuentasRolesFicha("T01") };
        t.Roles.Socio = new RolSocio("T01") { Participacion = 60m };
        t.CuentasRoles.Dividendos = (await Roles.BuscarPorCodigoAsync(ClaseCuentaRol.Dividendos, "5260001", _ctx)).Valor.IdIdentidadContable;
        t.CuentasRoles.RetencionDividendos = (await Roles.BuscarPorCodigoAsync(ClaseCuentaRol.RetencionDividendos, "4751011", _ctx)).Valor.IdIdentidadContable;
        Assert.Empty(SubcuentasRoles.QueCrear(t.Roles, t.CuentasRoles));
        var g = await Guardar(t);

        var cambio = Ficha(g, "T01");
        cambio.Roles.Socio!.Participacion = 50m;
        g = await Guardar(cambio);
        Assert.Equal(50m, (await Roles.SocioAsync(g.IdTercero, "T01"))!.Participacion);
        var historial = await Repo.HistorialAsync(g.IdTercero);
        Assert.Contains(historial, h => h.Entidad == "Socio" && h.Campo == "Participacion T01" && h.ValorAntiguo == "60 %" && h.ValorNuevo == "50 %");
        Assert.Contains(historial, h => h.Entidad == "Socio" && h.Campo == "Cuenta526 T01");

        // Un % fuera de 0-100 no se guarda.
        var mal = Ficha(g, "T01");
        mal.Roles.Socio!.Participacion = 120m;
        Assert.Equal("Socios.ParticipacionNoValida", (await Repo.GuardarAsync(mal, _actor)).Error!.Codigo);
    }

    [Fact]
    public async Task Quitar_el_rol_deja_de_listarlo_y_conserva_las_subcuentas_con_apuntes()
    {
        await PrepararAsync();
        var t = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Socia Sintética S.L.", CuentasRoles = new CuentasRolesFicha("T01") };
        t.Roles.Socio = new RolSocio("T01") { Participacion = 100m };
        var g = await GuardarComoLaFicha(t);
        var quitar = Ficha(g, "T01");
        quitar.Roles.Socio!.Activo = false;
        var sin = await Guardar(quitar);
        Assert.False(sin.Roles.EsSocio);
        Assert.Empty(await new ProConta.Contabilidad.Dividendos.ConsultaSocios(Bd).ActivosAsync("T01", 9002));
        // Sin apuntes, las subcuentas del rol quitado se borran (como las de los demás roles).
        var quitadas = SubcuentasRoles.QueEliminar(g, sin);
        Assert.Equal([ClaseCuentaRol.Dividendos, ClaseCuentaRol.RetencionDividendos], quitadas.Select(q => q.Clase));
        foreach (var q in quitadas)
            Assert.Equal(TipoCambioSubcuenta.Eliminada, (await Sub.EliminarAsync(sin.IdTercero, q, _ctx, _actor)).Tipo);
        var ids = await Roles.IdentidadesAsync(sin.IdTercero, "T01");
        Assert.Null(ids.Dividendos);
        Assert.Null(ids.RetencionDividendos);
        // Volver a darlo lo reactiva con el mismo número (la ficha vuelve a leer sus cuentas tras borrar las subcuentas).
        sin.CuentasRoles = ids;
        var otra = Ficha(sin, "T01");
        otra.Roles.Socio!.Activo = true;
        var vuelta = await Guardar(otra);
        Assert.Equal(g.Roles.Socio!.IdSocio, vuelta.Roles.Socio!.IdSocio);
    }
}
