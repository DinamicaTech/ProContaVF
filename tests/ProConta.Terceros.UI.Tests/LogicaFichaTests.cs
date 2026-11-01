using ProConta.Terceros.Contactos;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;
using ProConta.Terceros.UI.Logica;

namespace ProConta.Terceros.UI.Tests;

public class GestorFichasTests
{
    private static FichaAbierta F(int clave, long orden, bool cambios = false) => new(clave, orden, cambios);

    [Fact]
    public void UnaFichaYaAbiertaSoloSeActiva() =>
        Assert.Equal(new DecisionApertura(AccionApertura.Activar, 7), GestorFichas.Decidir([F(7, 1)], 7));

    [Fact]
    public void ConSitioSeAbreOtra()
    {
        Assert.Equal(AccionApertura.Abrir, GestorFichas.Decidir([F(1, 1), F(2, 2), F(3, 3), F(4, 4)], 9).Accion);
        Assert.Equal(AccionApertura.Abrir, GestorFichas.Decidir([], null).Accion);
    }

    [Fact]
    public void LaSextaSustituyeALaMasAntiguaSinCambios()
    {
        var abiertas = new[] { F(10, 1, cambios: true), F(11, 2), F(12, 3), F(13, 4, cambios: true), F(14, 5) };
        Assert.Equal(new DecisionApertura(AccionApertura.Sustituir, 11), GestorFichas.Decidir(abiertas, 99));
        Assert.Equal(new DecisionApertura(AccionApertura.Sustituir, 11), GestorFichas.Decidir(abiertas, null)); // también una ficha nueva
    }

    [Fact]
    public void SiTodasTienenCambiosNoSeAbre() =>
        Assert.Equal(AccionApertura.Rechazar, GestorFichas.Decidir(Enumerable.Range(1, 5).Select(i => F(i, i, cambios: true)).ToList(), 99).Accion);
}

public class CambiosFichaTests
{
    private static Tercero Base()
    {
        var t = new Tercero { TipoPersona = TipoPersona.Juridica, RazonSocial = "Talleres Sintéticos, S.L.", Pais = "724", CodigoPostal = "46980" };
        t.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Email, Valor = "facturas@ejemplo.test", EnvioFacturas = true });
        t.Roles.Cliente = new RolCliente();
        t.AsignarPerfil(new Perfil(1, "Proveedor homologado", null, true));
        return t;
    }

    [Fact]
    public void SinTocarNadaNoHayCambios() => Assert.Empty(CambiosFicha.Comparar(Base(), Base().Clonar()));

    [Fact]
    public void CadaCambioSeCuentaEnSuPestana()
    {
        var original = Base();
        var editada = original.Clonar();
        editada.RazonSocial = "Talleres Sintéticos Nuevos, S.L.";
        editada.CodigoPostal = "46988";
        editada.DatosContacto[0].EnvioFacturas = false;
        editada.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Telefono, Valor = "961 234 567" });
        editada.Roles.Cliente!.Activo = false;
        editada.RetirarPerfil(1);
        editada.NotasInternas = "Revisar tarifas";
        var cambios = CambiosFicha.Comparar(original, editada);
        Assert.Equal(7, cambios.Count);
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Identidad && c.Descripcion == "Razón social");
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Identidad && c.Descripcion.Contains("CP"));
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Contacto && c.Descripcion.StartsWith("Email"));
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Contacto && c.Descripcion.StartsWith("Teléfono añadido"));
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Roles && c.Descripcion == "Rol Cliente desactivado");
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Perfiles && c.Descripcion.StartsWith("Perfil retirado"));
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Notas && c.Descripcion == "Notas internas");
    }

    [Fact]
    public void DeshacerAManoElCambioLoQuitaDeLaLista()
    {
        var original = Base();
        var editada = original.Clonar();
        editada.RazonSocial = "Otra";
        Assert.Single(CambiosFicha.Comparar(original, editada));
        editada.RazonSocial = "Talleres Sintéticos, S.L. ";
        Assert.Empty(CambiosFicha.Comparar(original, editada)); // los espacios de los extremos no cuentan
    }

    [Fact]
    public void CerrarUnaRelacionYCambiarUnaCuentaSonCambios()
    {
        var original = Base();
        original.CuentasRoles = new CuentasRolesFicha("T01") { Cliente = 5 };
        var relacion = new RelacionContacto { IdTerceroPersona = 3, NombrePersona = "Lucía", NombreOrganizacion = "Talleres" };
        typeof(RelacionContacto).GetProperty(nameof(RelacionContacto.IdRelacionContacto))!.SetValue(relacion, 8);
        original.Relaciones.Add(relacion);
        var editada = original.Clonar();
        editada.Relaciones[0].FechaFin = new DateTime(2026, 11, 1);
        editada.CuentasRoles!.Cliente = 6;
        var cambios = CambiosFicha.Comparar(original, editada);
        Assert.Contains(cambios, c => c.Descripcion.StartsWith("Relación cerrada"));
        Assert.Contains(cambios, c => c.Pestana == PestanaFicha.Roles && c.Descripcion == "Cuenta de cliente");
    }
}

public class PastillaDestinatariosTests
{
    private static DatoContacto Email(string v, bool activo = true, bool facturas = false, bool admin = false) =>
        new() { Tipo = TipoDatoContacto.Email, Valor = v, Activo = activo, EnvioFacturas = facturas, EnviosAdministrativos = admin };

    [Fact]
    public void MuestraLosMarcadosDeCadaFinalidad()
    {
        var lineas = PastillaDestinatarios.Calcular([Email("facturas@ejemplo.test", facturas: true), Email("admin@ejemplo.test", admin: true), Email("viejo@ejemplo.test", activo: false, facturas: true)]);
        Assert.True(lineas[0].Correcta);
        Assert.Equal("facturas@ejemplo.test", lineas[0].Texto);
        Assert.Equal("admin@ejemplo.test", lineas[1].Texto);
    }

    [Fact]
    public void SeActualizaAlEditar()
    {
        var a = Email("a@ejemplo.test");
        var b = Email("b@ejemplo.test");
        var datos = new List<DatoContacto> { a };
        Assert.True(PastillaDestinatarios.Calcular(datos)[0].Correcta); // un único email activo: ese
        datos.Add(b);
        var ambigua = PastillaDestinatarios.Calcular(datos)[0];
        Assert.False(ambigua.Correcta);
        Assert.Contains("ninguno marcado", ambigua.Texto);
        b.EnvioFacturas = true;
        Assert.Equal("b@ejemplo.test", PastillaDestinatarios.Calcular(datos)[0].Texto);
    }

    [Fact]
    public void SinEmailsExplicaQueElDeLaFichaAntiguaNoSeUsa()
    {
        var linea = PastillaDestinatarios.Calcular([], "antiguo@ejemplo.test")[0];
        Assert.False(linea.Correcta);
        Assert.Contains("antiguo@ejemplo.test", linea.Texto);
    }
}

public class ValidacionFichaTests
{
    private sealed record Dom(string Etiqueta, string? Pais, string? CodigoPostal, string? Direccion, string? TipoDomicilio, bool EsPrincipal) : IDomicilioEditable;

    [Fact]
    public void ElCodigoPostalEspanolTieneCincoDigitos()
    {
        Assert.NotNull(ValidacionFicha.ErrorCodigoPostal("724", "4698", "Fiscal"));
        Assert.Null(ValidacionFicha.ErrorCodigoPostal("724", "46980", "Fiscal"));
        Assert.Null(ValidacionFicha.ErrorCodigoPostal("250", "F-75001", "Fiscal")); // fuera de España no se comprueba
    }

    [Fact]
    public void LosErroresSeAsignanASuCampoYPestana()
    {
        var t = new Tercero { TipoPersona = TipoPersona.Juridica };
        t.DatosContacto.Add(new DatoContacto { Tipo = TipoDatoContacto.Email, Valor = "administracion@talleresalbora" });
        var dom = new Dom("Fiscal", "724", "4698", "Calle", "FISCAL", false);
        var errores = ValidacionFicha.Revisar(t, [dom]);
        Assert.Contains(errores, e => e.Pestana == PestanaFicha.Identidad && e.Campo == "RazonSocial" && ReferenceEquals(e.Objeto, t));
        Assert.Contains(errores, e => e.Pestana == PestanaFicha.Identidad && e.Campo == "CodigoPostal" && ReferenceEquals(e.Objeto, dom));
        Assert.Contains(errores, e => e.Pestana == PestanaFicha.Contacto && e.Campo == "Valor" && e.Mensaje.Contains("administracion@talleresalbora"));
    }

    [Theory]
    [InlineData("01/03/2021", 2021, 3, 1)]
    [InlineData("1-3-2021", 2021, 3, 1)]
    [InlineData("01032021", 2021, 3, 1)]
    public void LeeFechasEscritasDeVariasFormas(string texto, int a, int m, int d)
    {
        Assert.True(ValidacionFicha.LeerFecha(texto, out var f));
        Assert.Equal(new DateTime(a, m, d), f);
    }

    [Fact]
    public void UnaFechaMalEscritaSeRechazaYUnaVaciaNo()
    {
        Assert.False(ValidacionFicha.LeerFecha("31/02/2026", out _));
        Assert.True(ValidacionFicha.LeerFecha("", out var vacia));
        Assert.Null(vacia);
    }
}
