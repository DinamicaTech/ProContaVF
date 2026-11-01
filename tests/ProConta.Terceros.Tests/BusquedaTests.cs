using ProConta.Datos.Esquema.Migraciones;
using ProConta.Nucleo.Auditoria;
using ProConta.Pruebas.Access;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Contactos;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;

namespace ProConta.Terceros.Tests;

public class IndiceBusquedaTests
{
    private static EntradaIndiceTercero Entrada(int id, string nombre, TipoPersona? tipo = TipoPersona.Juridica, bool activo = true, params (string Fuente, string Valor)[] datos)
    {
        var e = new EntradaIndiceTercero { IdTercero = id, Nombre = nombre, Tipo = tipo, Activo = activo };
        e.Anadir("Nombre", nombre);
        e.Anadir("IdTercero", id.ToString());
        foreach (var (f, v) in datos) e.Anadir(f, v);
        return e;
    }

    private static IndiceTerceros Indice()
    {
        var albora = Entrada(1, "Talleres Albora, S.L.", TipoPersona.Juridica, true,
            ("Nombre comercial", "Talleres Albora"), ("NIF", "B12345674"), ("Teléfono", "961 234 567"), ("Email", "facturas@talleresalbora.example"),
            ("Domicilio", "Calle del Metal, 14 46980 Paterna Valencia"), ("Persona de contacto", "Lucía Ferrer Casals"), ("Perfil", "Proveedor homologado"),
            ("Cuenta contable", "4300012"), ("IdCliente", "4300"), ("IdProveedor", "31"));
        albora.Roles.Add(new RolResultado(RolBusqueda.Cliente, 4300, true));
        albora.Roles.Add(new RolResultado(RolBusqueda.Proveedor, 31, true));
        albora.Perfiles.Add((7, "Proveedor homologado"));
        var lucia = Entrada(2, "Lucía Ferrer Casals", TipoPersona.Fisica, true, ("NIF", "12345678Z"), ("Organización", "Talleres Albora, S.L."), ("IdContacto", "9"));
        lucia.Roles.Add(new RolResultado(RolBusqueda.Contacto, 9, true));
        var gemelo = Entrada(3, "Otra Sociedad con el mismo NIF, S.L.", TipoPersona.Juridica, true, ("NIF", "B12345674"));
        var baja = Entrada(4, "Ferretería de baja, S.L.", TipoPersona.Juridica, false);
        var antiguo = Entrada(5, "Tercero antiguo sin clasificar", null, true);
        return new IndiceTerceros([albora, lucia, gemelo, baja, antiguo]);
    }

    private static int[] Ids(ResultadoBusquedaTerceros r) => r.Filas.Select(f => f.IdTercero).ToArray();

    [Theory]
    [InlineData("albora", new[] { 1, 2 })]               // nombre y organización de la persona relacionada
    [InlineData("B-12345674", new[] { 3, 1 })]            // NIF con guion: salen los dos con el mismo NIF
    [InlineData("961234567", new[] { 1 })]                // teléfono escrito sin espacios
    [InlineData("facturas@talleres", new[] { 1 })]        // email
    [InlineData("paterna", new[] { 1 })]                  // domicilio
    [InlineData("homologado", new[] { 1 })]               // perfil
    [InlineData("4300012", new[] { 1 })]                  // cuenta contable
    [InlineData("12345678z", new[] { 2 })]                // NIF en minúsculas
    [InlineData("lucia", new[] { 2, 1 })]                 // sin tilde: la persona y la organización en la que es contacto
    public void CadaFuenteEncuentraSuTercero(string texto, int[] esperados) =>
        Assert.Equal(esperados.OrderBy(x => x), Ids(Indice().Buscar(new FiltroTerceros(texto))).OrderBy(x => x));

    [Fact]
    public void ConVariasPalabrasDebenAparecerTodasEnCualquierDato()
    {
        Assert.Equal([1], Ids(Indice().Buscar(new FiltroTerceros("ferrer paterna"))));
        Assert.Empty(Indice().Buscar(new FiltroTerceros("ferrer madrid")).Filas);
    }

    [Fact]
    public void CadaResultadoDiceEnQueHaCoincidido()
    {
        var fila = Indice().Buscar(new FiltroTerceros("ferrer paterna")).Filas.Single();
        Assert.Contains(fila.Coincidencias, c => c.Fuente == "Persona de contacto" && c.Valor == "Lucía Ferrer Casals");
        Assert.Contains(fila.Coincidencias, c => c.Fuente == "Domicilio");
        Assert.Equal(2, fila.Roles.Count);
    }

    [Fact]
    public void FiltrosPorTipoRolPerfilEInactivos()
    {
        var i = Indice();
        Assert.Equal([2], Ids(i.Buscar(new FiltroTerceros(Tipo: TipoPersona.Fisica))));
        Assert.Equal([5], Ids(i.Buscar(new FiltroTerceros(SinClasificar: true))));
        Assert.Equal([2], Ids(i.Buscar(new FiltroTerceros(Roles: new HashSet<RolBusqueda> { RolBusqueda.Contacto }))));
        Assert.Equal([1], Ids(i.Buscar(new FiltroTerceros(IdPerfil: 7))));
        Assert.DoesNotContain(4, Ids(i.Buscar(new FiltroTerceros("ferreteria"))));
        Assert.Equal([4], Ids(i.Buscar(new FiltroTerceros("ferreteria", IncluirInactivos: true))));

        var r = i.Buscar(new FiltroTerceros(Tipo: TipoPersona.Fisica));
        Assert.Equal(4, r.Recuentos.Todos); // sin el inactivo
        Assert.Equal(2, r.Recuentos.Juridicas);
        Assert.Equal(1, r.Recuentos.SinClasificar);
        Assert.Equal(1, r.Recuentos.Clientes);
    }

    [Fact]
    public void ElMaximoRecortaPeroDiceCuantosHay()
    {
        var r = Indice().Buscar(new FiltroTerceros(Maximo: 2));
        Assert.Equal(2, r.Filas.Count);
        Assert.Equal(4, r.Total);
        Assert.True(r.Recortado);
    }
}

public class ModeloFichaTests
{
    [Fact]
    public void ClonarCopiaTodoSinCompartirListas()
    {
        var t = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Sintética SL" };
        var grupo = new ContactoLibre { Nombre = "Ana" };
        t.ContactosLibres.Add(grupo);
        t.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Email, Valor = "a@ejemplo.test", ContactoLibre = grupo });
        t.Roles.Cliente = new RolCliente { Notas = "x" };
        t.AsignarPerfil(new Perfil(1, "Uno", null, true));
        var c = t.Clonar();
        c.Roles.Cliente!.Notas = "y";
        c.DatosContacto[0].Valor = "b@ejemplo.test";
        Assert.Equal("x", t.Roles.Cliente.Notas);
        Assert.Equal("a@ejemplo.test", t.DatosContacto[0].Valor);
        Assert.Same(c.ContactosLibres[0], c.DatosContacto[0].ContactoLibre);
        Assert.NotSame(grupo, c.ContactosLibres[0]);
        Assert.Single(c.Perfiles);
    }

    [Fact]
    public void AsignarYRetirarPerfilesEsIdempotente()
    {
        var t = new Tercero();
        var p = new Perfil(1, "Uno", null, true);
        t.AsignarPerfil(p);
        t.AsignarPerfil(p);
        Assert.Single(t.Perfiles);
        t.RetirarPerfil(1);
        Assert.Empty(t.Perfiles); // nueva y retirada antes de guardar: no deja rastro
    }

    [Fact]
    public void RelacionesSeValidan()
    {
        var t = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Sintética SL" };
        t.Relaciones.Add(new RelacionContacto { IdTerceroPersona = 5, IdTerceroOrganizacion = 0, FechaInicio = new(2026, 1, 10), FechaFin = new(2025, 1, 1) });
        Assert.Equal("Relaciones.FechasInvertidas", t.Validar(null).Error!.Codigo);
        t.Relaciones[0].FechaFin = null;
        t.Relaciones[0].TipoRelacion = " ";
        Assert.Equal("Relaciones.SinTipo", t.Validar(null).Error!.Codigo);
    }

    [Fact]
    public void LaMigracionDeCuentasEligeLaPredeterminadaDelEjercicioMasReciente()
    {
        MigracionesTerceros.FilaCuentaEjercicio F(int id, int rol, int identidad, string soc, string ej, bool pred) => new(id, rol, id, identidad, soc, ej, pred);
        var ganadoras = MigracionesTerceros.Ganadoras(
        [
            F(1, 10, 100, "T01", "2025", true), F(2, 10, 101, "T01", "2026", false), F(3, 10, 102, "T01", "2026", true),
            F(4, 10, 200, "T02", "2024", true), F(5, 11, 300, "T01", "2026", false),
        ]).OrderBy(g => g.IdRol).ThenBy(g => g.IdSociedad).ToList();
        Assert.Equal([(10, "T01", 102), (10, "T02", 200), (11, "T01", 300)], ganadoras);
    }
}

[Trait("Categoria", "Access")]
public sealed class BusquedaAccessTests : IDisposable
{
    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly Actor _actor = new("operador.prueba");

    public void Dispose() => _copia.Dispose();

    [Fact]
    public async Task ElBuscadorEncuentraPorCadaFuenteDeLaBd()
    {
        var bd = _copia.Bd();
        var repo = new RepositorioTercerosAccess(bd);
        var org = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Talleres Sintéticos, S.L.", NombreComercial = "Sintéticos", Nif = "B12345674", Poblacion = "Paterna" };
        org.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Telefono, Valor = "961 234 567" });
        org.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Email, Valor = "facturas@sinteticos.example" });
        org.ContactosLibres.Add(new ContactoLibre { Nombre = "Jordi Pons", Departamento = "Almacén" });
        org.Domicilios.Add(new Domicilio(0, "ENVIO", "Calle de la Forja, 3", "46980", "Paterna", "Valencia", "ESP"));
        org.Roles.Cliente = new RolCliente();
        var idOrg = (await repo.GuardarAsync(org, _actor)).Valor.IdTercero;
        var persona = new Tercero { TipoPersona = TipoPersona.Fisica, NombrePersona = "Lucía", PrimerApellido = "Ferrer", Nif = "12345678Z" };
        persona.Relaciones.Add(new RelacionContacto { IdTerceroOrganizacion = idOrg, NombreOrganizacion = "Talleres Sintéticos, S.L.", NombrePersona = "Lucía Ferrer" });
        var idPersona = (await repo.GuardarAsync(persona, _actor)).Valor.IdTercero;
        var gemelo = await repo.GuardarAsync(new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Gemela, S.L.", Nif = "B12345674" }, _actor);
        Assert.True(gemelo.EsCorrecto, gemelo.Error?.ToString());
        var idCliente = (await new ServicioRoles(bd, new Contabilidad.Cuentas.CatalogoCuentasAccess(bd)).ObtenerAsync(idOrg)).IdCliente!.Value;

        var buscador = new BuscadorTerceros(bd);
        async Task<int[]> Buscar(string texto) => (await buscador.BuscarAsync(new FiltroTerceros(texto))).Filas.Select(f => f.IdTercero).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { idOrg, gemelo.Valor.IdTercero }.Order(), await Buscar("b12345674"));
        Assert.Equal([idOrg], await Buscar("sinteticos 961234567"));
        Assert.Equal([idOrg], await Buscar("forja"));
        Assert.Equal([idOrg], await Buscar("jordi"));
        Assert.Equal([idOrg], await Buscar("facturas@sinteticos"));
        Assert.Contains(idOrg, await Buscar(idCliente.ToString()));
        Assert.Equal(new[] { idOrg, idPersona }.Order(), await Buscar("ferrer"));
        var fila = (await buscador.BuscarAsync(new FiltroTerceros("ferrer"))).Filas.Single(f => f.IdTercero == idOrg);
        Assert.Contains(fila.Coincidencias, c => c.Fuente == "Persona de contacto");
        Assert.Contains(fila.Roles, r => r.Rol == RolBusqueda.Cliente && r.Id == idCliente);
    }
}
