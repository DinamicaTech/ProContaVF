using ProConta.Mantenimiento;
using ProConta.Mantenimiento.UI;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.UI.Base.Tests;

/// <summary>
/// Pestañas, nombre de la cuenta y botón «Auto» de la pantalla de Parámetros (plataforma/mantenimiento 2026-11-18, propietario 2026-11-18
/// 04:59 y 05:10).
/// </summary>
public class AutoCuentasMantenimientoTests
{
    private static readonly ContextoMantenimiento Ctx = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("prueba"));
    private static readonly ContextoSociedadEjercicio Siguiente = new("T01", 9002, 2027);

    private sealed class Proveedor : ProveedorBase
    {
        public override string Clave => "ejercicios";
        public override string Titulo => "Ejercicios";
        public override string Categoria => "General";
        public override string Finalidad => "Prueba de cuentas por pestañas.";
        public override AmbitoMantenimiento Ambito => AmbitoMantenimiento.Compartido;
        public override string NombreRegistro => "ejercicio";
        public override IReadOnlyList<GrupoCampos> Grupos => [new("a", "Uno"), new("b", "Dos"), new("c", "Tres"), new("d", "Cuatro")];
        public override IReadOnlyList<CampoMantenimiento> Campos =>
        [
            new("Nombre", "Nombre") { Grupo = "a", Ayuda = "Nombre." },
            new("Acreedora", "A ingresar", TipoCampo.Cuenta) { Grupo = "b", Ayuda = "Hacienda acreedora.", GruposCuenta = ["475"], CuentaHabitual = "4750000", NombreCuentaHabitual = "Hacienda Pública, acreedora por IVA",
                Explicacion = [new("Para qué sirve", "Recibe lo que sale a pagar.")] },
            new("Deudora", "A compensar", TipoCampo.Cuenta) { Grupo = "b", Ayuda = "Hacienda deudora.", GruposCuenta = ["470"], CuentaHabitual = "4700000", NombreCuentaHabitual = "Hacienda Pública, deudora por IVA" },
            new("Retencion", "Retención de servicios", TipoCampo.Cuenta) { Grupo = "c", Ayuda = "Retención.", GruposCuenta = ["4751"], PalabrasCuenta = [["profesional"]] },
            new("Suplido", "Suplido", TipoCampo.Cuenta) { Grupo = "d", Ayuda = "Suplido.", GruposCuenta = ["55"], PalabrasCuenta = [["suplido"]] },
        ];
        public override IReadOnlyList<ColumnaLista> Columnas => [new("Nombre", "Nombre")];
        public override Task<IReadOnlyList<RegistroMantenimiento>> ListarAsync(ContextoMantenimiento contexto) =>
            Task.FromResult<IReadOnlyList<RegistroMantenimiento>>([new RegistroMantenimiento(9002, new Dictionary<string, object?> { ["Nombre"] = "2027", ["Suplido"] = "5550002" })]);
        public override Task<Resultado<RegistroMantenimiento>> GuardarAsync(RegistroMantenimiento registro, ContextoMantenimiento contexto) =>
            Task.FromResult(Resultado<RegistroMantenimiento>.Correcto(registro));
        // Las cuentas del registro se eligen en el plan de su propio ejercicio, no en el de trabajo.
        public override ContextoSociedadEjercicio? EjercicioDeCuentas(RegistroMantenimiento registro, ContextoMantenimiento contexto) => Siguiente;
    }

    private sealed class Selector : ISelectorCuentas
    {
        public List<CuentaPlan> Plan { get; } =
        [
            new("4750000", "Hacienda Pública, acreedora por IVA"), new("4751002", "H.P. retenciones profesionales"), new("4751003", "H.P. retenciones profesionales agentes"),
            new("5550002", "Suplidos con IVA"),
        ];
        public List<(int Ejercicio, string Codigo, string Nombre)> Creadas { get; } = [];
        public string? Fallar { get; set; }
        /// <summary>Textos de los avisos que enseña la pantalla.</summary>
        public List<string> Mensajes { get; } = [];
        public ContextoMantenimiento? Pedido { get; private set; }
        public Task<string?> ElegirAsync(CampoMantenimiento campo, string? texto, ContextoMantenimiento contexto) { Pedido = contexto; return Task.FromResult<string?>(null); }
        public Task<IReadOnlyList<CuentaPlan>> SubcuentasAsync(ContextoMantenimiento contexto) => Task.FromResult<IReadOnlyList<CuentaPlan>>([.. Plan]);
        public Task<Resultado> CrearSubcuentaAsync(ContextoMantenimiento contexto, string codigo, string nombre)
        {
            if (codigo == Fallar) return Task.FromResult(Resultado.Fallo(new Error("Cuentas.CodigoReservado", "Reservado.", $"Cuenta {codigo}")));
            Creadas.Add((contexto.Trabajo.Ejercicio, codigo, nombre));
            Plan.Add(new(codigo, nombre));
            return Task.FromResult(Resultado.Correcto());
        }
    }

    private static async Task<(PantallaMantenimientoVM Pantalla, Selector Selector, List<IReadOnlyList<string>?> Detalles)> AbrirAsync()
    {
        var selector = new Selector();
        var detalles = new List<IReadOnlyList<string>?>();
        var pantalla = new PantallaMantenimientoVM(new ServicioMantenimiento([new Proveedor()], () => Task.FromResult(true)), Ctx,
            (_, mensaje, detalle, _) => { selector.Mensajes.Add(mensaje); detalles.Add(detalle); return 0; }, _ => { }, _ => new object()) { Cuentas = selector };
        await pantalla.IniciarAsync();
        await pantalla.RecargarPlanAsync();
        return (pantalla, selector, detalles);
    }

    private static CampoEdicionVM Campo(PantallaMantenimientoVM p, string clave) => p.Grupos.SelectMany(g => g.Campos).Single(c => c.Clave == clave);

    [Fact]
    public async Task ConCuatroGruposVaEnPestanasYLasCuentasMuestranSuNombre()
    {
        var (p, _, _) = await AbrirAsync();
        Assert.True(p.UsaPestanas);
        Assert.Equal("Suplidos con IVA", Campo(p, "Suplido").NombreCuenta);
        Assert.Equal("Asignada", Campo(p, "Suplido").EstadoCuenta);
        Assert.Equal("Sin asignar", Campo(p, "Acreedora").EstadoCuenta);
        Assert.Equal(3, p.CuentasSinAsignar);
        Assert.True(Campo(p, "Acreedora").TieneExplicacion);
        // El buscador (Ctrl+K) o un error llevan a la pestaña del campo.
        p.CampoEnfocado = "Retencion";
        Assert.Equal(2, p.Pestana);
    }

    [Fact]
    public async Task AutoAsignaLaHabitualCreaLaQueFaltaYDejaLaDudosa()
    {
        var (p, s, detalles) = await AbrirAsync();
        Assert.True(p.PuedeAuto);
        await p.AutoAsync();
        Assert.Equal("4750000", Campo(p, "Acreedora").Texto);
        Assert.Equal("4700000", Campo(p, "Deudora").Texto);
        Assert.Equal([(2027, "4700000", "Hacienda Pública, deudora por IVA")], s.Creadas);
        Assert.Equal("", Campo(p, "Retencion").Texto);
        Assert.Equal("5550002", Campo(p, "Suplido").Texto);
        Assert.Contains(detalles[0]!, d => d.Contains("Retención de servicios") && d.Contains("4751002, 4751003"));
        Assert.Equal("Hacienda Pública, deudora por IVA", Campo(p, "Deudora").NombreCuenta);
        Assert.True(p.TieneCambios);
        Assert.Equal(1, p.CuentasSinAsignar);
    }

    [Fact]
    public async Task LoQueSeDaDeAltaVaPrimeroEnLaPropuestaYUnFalloSeExplicaBien()
    {
        var (p, s, detalles) = await AbrirAsync();
        s.Fallar = "4700000";
        await p.AutoAsync();
        Assert.StartsWith("Dos · A compensar → 4700000", detalles[0]![0]);
        Assert.Contains("pero una no se ha podido dar de alta", s.Mensajes[^1]);
        Assert.Equal("", Campo(p, "Deudora").Texto);
    }

    [Fact]
    public async Task ElBuscadorSeAbreEnElPlanDelEjercicioDelRegistro()
    {
        var (p, s, _) = await AbrirAsync();
        await Campo(p, "Acreedora").BuscarCuentaAsync();
        Assert.Equal(2027, s.Pedido!.Trabajo.Ejercicio);
    }
}
