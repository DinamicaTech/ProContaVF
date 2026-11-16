using ProConta.Mantenimiento;
using ProConta.Mantenimiento.UI;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.UI.Base.Tests;

/// <summary>Campos de cuenta de la pantalla de Parámetros (plataforma/mantenimiento 2026-11-12): solo se eligen en el buscador (propietario 2026-11-12 12:16).</summary>
public class CamposCuentaMantenimientoTests
{
    private static readonly ContextoMantenimiento Ctx = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("prueba"));

    private sealed class Proveedor : ProveedorBase
    {
        public override string Clave => "prueba";
        public override string Titulo => "Prueba";
        public override string Categoria => "General";
        public override string Finalidad => "Prueba de campos de cuenta.";
        public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Compartido;
        public override string NombreRegistro => "registro";
        public override IReadOnlyList<CampoMantenimiento> Campos =>
            [new("Cuenta", "Cuenta", TipoCampo.Cuenta) { Ayuda = "Cuenta de prueba.", GruposCuenta = ["472"] }];
        public override IReadOnlyList<ColumnaLista> Columnas => [new("Cuenta", "Cuenta")];
        public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto) =>
            Task.FromResult<IReadOnlyList<RegistroMantenimiento>>([new RegistroMantenimiento(1, new Dictionary<string, object?> { ["Cuenta"] = null })]);
        public override Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
            Task.FromResult(Resultado<RegistroMantenimiento>.Correcto(registro));
    }

    private sealed class Selector : ISelectorCuentas
    {
        public string? Elegida { get; set; }
        public CampoMantenimiento? Pedido { get; private set; }
        public Task<string?> ElegirAsync(CampoMantenimiento campo, string? texto, ContextoMantenimiento contexto) { Pedido = campo; return Task.FromResult(Elegida); }
    }

    private static async Task<(CampoEdicionVM Campo, Selector Selector)> AbrirAsync()
    {
        var selector = new Selector();
        var pantalla = new PantallaMantenimientoVM(new ServicioMantenimiento([new Proveedor()], () => Task.FromResult(true)), Ctx, (_, _, _, _) => 0, _ => { }, _ => new object())
            { Cuentas = selector };
        await pantalla.IniciarAsync();
        return (pantalla.Grupos.SelectMany(g => g.Campos).Single(), selector);
    }

    [Fact]
    public async Task LaCuentaSeEligeEnElBuscadorConSusGrupos()
    {
        var (campo, selector) = await AbrirAsync();
        Assert.True(campo.PuedeBuscarCuenta);
        Assert.DoesNotContain("caracteres", campo.Ayuda);
        selector.Elegida = "4720001";
        await campo.BuscarCuentaAsync();
        Assert.Equal(["472"], selector.Pedido!.GruposCuenta);
        Assert.Equal("4720001", campo.Texto);
    }

    private sealed class Tipos : ProveedorBase
    {
        public override string Clave => "tipos";
        public override string Titulo => "Tipos";
        public override string Categoria => "General";
        public override string Finalidad => "Tabla 1:N de prueba.";
        public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Compartido;
        public override string NombreRegistro => "tipo";
        public override IReadOnlyList<CampoMantenimiento> Campos => [new("Nombre", "Nombre") { Ayuda = "Nombre del tipo." }];
        public override IReadOnlyList<ColumnaLista> Columnas => [new("Nombre", "Nombre")];
        public override string TituloRegistro(RegistroMantenimiento r) => r.Texto("Nombre") ?? "";
        public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto) =>
            Task.FromResult<IReadOnlyList<RegistroMantenimiento>>([new RegistroMantenimiento((byte)1, new Dictionary<string, object?> { ["Nombre"] = "Transferencia" }),
                new RegistroMantenimiento((byte)2, new Dictionary<string, object?> { ["Nombre"] = "Recibo" })]);
        public override Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
            Task.FromResult(Resultado<RegistroMantenimiento>.Correcto(registro));
    }

    private sealed class ConReferencia : ProveedorBase
    {
        public override string Clave => "conreferencia";
        public override string Titulo => "Con referencia";
        public override string Categoria => "General";
        public override string Finalidad => "Prueba de referencias.";
        public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Compartido;
        public override string NombreRegistro => "registro";
        public override IReadOnlyList<CampoMantenimiento> Campos => [new("IdTipo", "Tipo", TipoCampo.Referencia) { Referencia = "tipos", Ayuda = "Tipo de prueba." }];
        public override IReadOnlyList<ColumnaLista> Columnas => [new("IdTipo", "Tipo")];
        public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto) =>
            Task.FromResult<IReadOnlyList<RegistroMantenimiento>>([new RegistroMantenimiento(1, new Dictionary<string, object?> { ["IdTipo"] = 2 })]);
        public override Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
            Task.FromResult(Resultado<RegistroMantenimiento>.Correcto(registro));
    }

    [Fact]
    public async Task LaReferenciaMuestraElTituloYSeEligeEnSuTabla()
    {
        var pantalla = new PantallaMantenimientoVM(new ServicioMantenimiento([new ConReferencia(), new Tipos()], () => Task.FromResult(true)), Ctx, (_, _, _, _) => 0, _ => { }, _ => new object());
        IProveedorMantenimiento? abierto = null;
        object? claveActual = null;
        pantalla.ElegirRegistro = async (p, clave) =>
        {
            abierto = p; claveActual = clave;
            return (await p.ListarAsync(Ctx))[0];
        };
        await pantalla.IniciarAsync("conreferencia");
        var campo = pantalla.Grupos.SelectMany(g => g.Campos).Single();
        for (var i = 0; i < 20 && campo.Texto != "Recibo"; i++) await Task.Delay(10);
        Assert.Equal("Recibo", campo.Texto);
        Assert.True(campo.PuedeElegirReferencia);
        Assert.DoesNotContain("caracteres", campo.Ayuda);

        await campo.ElegirReferenciaAsync();
        Assert.Equal("tipos", abierto!.Clave);
        Assert.Equal(2, claveActual);
        Assert.Equal("Transferencia", campo.Texto);
        Assert.True(pantalla.TieneCambios);

        campo.VaciarReferencia();
        Assert.Equal("", campo.Texto);
    }

    [Fact]
    public async Task CancelarNoCambiaYSuprVacia()
    {
        var (campo, selector) = await AbrirAsync();
        selector.Elegida = "4720001";
        await campo.BuscarCuentaAsync();
        selector.Elegida = null;
        await campo.BuscarCuentaAsync();
        Assert.Equal("4720001", campo.Texto);
        campo.VaciarCuenta();
        Assert.Equal("", campo.Texto);
    }
}
