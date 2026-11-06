using ProConta.Compras.Facturas;
using ProConta.Compras.UI.VistaModelos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.UI;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;

namespace ProConta.Compras.UI.Tests;

/// <summary>Buscador de cuentas común (contabilidad/cuenta-contable 2026-11-06) y su uso en la cuenta de gasto de la ficha.</summary>
public sealed class BuscadorCuentasVMTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);

    private static CuentaContable C(int id, string codigo, string nombre, bool imputable = true) =>
        new(id, id, 9001, null, codigo, nombre, true, false, imputable, null, 0m, 0m, 0);

    /// <summary>Plan en memoria: lista, resuelve, da de alta y propone el siguiente código.</summary>
    private sealed class PlanFalso : ICatalogoCuentas
    {
        public List<CuentaContable> Cuentas { get; } =
            [C(1, "629", "Otros servicios", imputable: false), C(2, "6290001", "Gastos de viaje") with { Descripcion = "Desplazamientos del personal" }, C(3, "6280011", "Suministros: electricidad"), C(4, "5720017", "Banco Santander"),
             C(5, "6299", "Otros servicios: varios", imputable: false), C(6, "6299001", "Mensajería"), C(7, "62950", "Cuenta de cinco cifras")];

        public Task<IReadOnlyList<CuentaContable>> ListarAsync(ContextoSociedadEjercicio contexto, string? prefijo = null) =>
            Task.FromResult<IReadOnlyList<CuentaContable>>(Cuentas.Where(c => prefijo is null || c.Codigo.StartsWith(prefijo, StringComparison.Ordinal)).ToList());

        public Task<Resultado<CuentaContable>> ResolverAsync(ContextoSociedadEjercicio contexto, string codigo) =>
            Task.FromResult(Cuentas.FirstOrDefault(c => c.Codigo == codigo) is { } c ? Resultado<CuentaContable>.Correcto(c) : Resultado<CuentaContable>.Fallo(new Error("Cuentas.NoExiste", "No existe")));

        public Task<Resultado<CuentaContable>> AltaAsync(ContextoSociedadEjercicio contexto, AltaCuenta alta)
        {
            if (string.IsNullOrWhiteSpace(alta.Nombre)) return Task.FromResult(Resultado<CuentaContable>.Fallo(new Error("Cuentas.NombreNoValido", "Falta el nombre de la cuenta.")));
            var c = C(Cuentas.Count + 1, alta.Codigo, alta.Nombre) with
            {
                IdCuentaPadre = alta.IdCuentaPadre, Descripcion = alta.Descripcion, IdCentroCostePredeterminado = alta.IdCentroCostePredeterminado,
            };
            Cuentas.Add(c);
            return Task.FromResult(Resultado<CuentaContable>.Correcto(c));
        }

        public Task<string?> SiguienteSubcuentaAsync(ContextoSociedadEjercicio contexto, string cuenta) =>
            Task.FromResult(BuscadorCuentas.SiguienteCodigo(cuenta, Cuentas.Select(c => c.Codigo)));

        public Task<CuentaContable?> ObtenerAsync(int idCuentaContable) => throw new NotSupportedException();
        public Task<CuentaContable?> CuentaDeIdentidadAsync(ContextoSociedadEjercicio contexto, int idIdentidadContable) => throw new NotSupportedException();
        public Task<CuentaContable?> UltimaCuentaDeIdentidadAsync(int idIdentidadContable) => throw new NotSupportedException();
        public Task<Resultado<CuentaContable>> AltaEnEjercicioAsync(ContextoSociedadEjercicio contexto, int idIdentidadContable, string? codigo = null, string? nombre = null) => throw new NotSupportedException();
        public Task ReconstruirSaldosAsync(ContextoSociedadEjercicio contexto) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Filtra_por_grupo_con_casilla_y_propone_la_cuenta_de_la_seleccionada()
    {
        var vm = new BuscadorCuentasVM(new PlanFalso(), Ctx, ["6"], "santander");
        await vm.CargarAsync();
        Assert.Empty(vm.Resultados);
        Assert.Contains("Solo el grupo 6", vm.Estado);
        vm.SoloGrupos = false;
        Assert.Equal(["5720017"], vm.Resultados.Select(c => c.Codigo));
        vm.SoloGrupos = true;
        vm.Texto = "desplazamientos";
        Assert.Equal(["6290001"], vm.Resultados.Select(c => c.Codigo));
        vm.Texto = "629.1";
        Assert.Equal("6290001", vm.Seleccionada?.Codigo);
        Assert.Equal("629", vm.CuentaPropuesta);
        // La propuesta es la cuenta más larga de la que cuelga la seleccionada; la lista no enseña las de menos de 7 cifras.
        vm.Texto = "6299";
        Assert.Equal(["6299001"], vm.Resultados.Select(c => c.Codigo));
        Assert.Equal("6299", vm.CuentaPropuesta);

        // Con el filtro quitado, una cuenta de otro grupo no se propone para la subcuenta.
        vm.SoloGrupos = false;
        vm.Texto = "santander";
        Assert.Equal("5720017", vm.Seleccionada?.Codigo);
        Assert.Equal("", vm.CuentaPropuesta);
    }

    [Fact]
    public async Task Crear_subcuenta_propone_el_siguiente_codigo_y_la_elige()
    {
        var plan = new PlanFalso();
        var vm = new BuscadorCuentasVM(plan, Ctx, ["6"], "agua");
        await vm.CargarAsync();
        Assert.Empty(vm.Resultados);

        Assert.Equal("Cuentas.CuentaNoValida", (await vm.SiguienteCodigoAsync("62")).Errores[0].Codigo);
        Assert.Equal("Cuentas.FueraDeGrupo", (await vm.SiguienteCodigoAsync("572")).Errores[0].Codigo);
        var siguiente = await vm.SiguienteCodigoAsync("629");
        Assert.Equal("6290002", siguiente.Valor);

        Assert.Equal("Cuentas.CodigoFueraDeCuenta", (await vm.CrearAsync("629", "6280002", "Agua")).Errores[0].Codigo);
        Assert.False((await vm.CrearAsync("629", "6290002", " ")).EsCorrecto);
        Assert.Equal(["629", "62950", "6299"], vm.CuentasPadre.Select(c => c.Codigo));
        Assert.Equal("Cuentas.CodigoFueraDeCuenta", (await vm.CrearAsync("629", "629001", "Corto")).Errores[0].Codigo);
        var creada = await vm.CrearAsync("629", "6290002", "Suministros: agua", "Agua de la oficina", 3);
        Assert.True(creada.EsCorrecto);
        Assert.Equal((1, "Agua de la oficina", 3), (creada.Valor.IdCuentaPadre, creada.Valor.Descripcion, creada.Valor.IdCentroCostePredeterminado!.Value));
        Assert.Equal("6290002", vm.Seleccionada?.Codigo);
        Assert.Equal(["6290002"], vm.Resultados.Select(c => c.Codigo));
    }

    private static FichaCompraVM Ficha(Func<string, Task<CuentaContable?>> buscar, List<string> textos)
    {
        var f = new FacturaCompra
        {
            IdRegistro = 5, Ejercicio = Ctx, RutaPdf = @"X:\D\f.pdf", Md5 = "0123456789abcdef0123456789abcdef", Carpeta = "Ficticio",
            NifProveedor = "B61465332", NumeroFactura = "FV1", Fecha = new DateTime(2026, 2, 10), CuentaGasto = "6000000", Version = 1,
        };
        var ficha = new FichaCompraVM(f, [], [new("6000000 · Compras", "6000000"), new("6290001 · Gastos de viaje", "6290001")],
            [new CentroCoste(3, "T01", null, "3", "COCHES", true, true, 0, "3")], [], soloLectura: false);
        ficha.BuscarCuenta = t => { textos.Add(t); return buscar(t); };
        return ficha;
    }

    [Fact]
    public async Task La_cuenta_de_gasto_admite_el_punto_y_abre_el_buscador_con_un_codigo_desconocido()
    {
        var textos = new List<string>();
        var ficha = Ficha(_ => Task.FromResult<CuentaContable?>(C(9, "6290002", "Suministros: agua") with { IdCentroCostePredeterminado = 3 }), textos);
        Assert.Equal(("6000000", "Compras"), (ficha.CodigoCuentaGasto, ficha.NombreCuentaGasto));

        ficha.CodigoCuentaGasto = "629.1";
        Assert.Equal(("6290001", "Gastos de viaje"), (ficha.CodigoCuentaGasto, ficha.NombreCuentaGasto));
        Assert.True(ficha.TieneCambios);
        Assert.Empty(textos);

        ficha.CodigoCuentaGasto = "agua";
        Assert.Equal("6290001", ficha.CodigoCuentaGasto);
        // El buscador se abre después de escribir el valor, no dentro del enlace.
        for (var i = 0; i < 100 && ficha.CodigoCuentaGasto != "6290002"; i++) await Task.Delay(20);
        Assert.Equal(["agua"], textos);
        Assert.Equal(("6290002", "Suministros: agua"), (ficha.CodigoCuentaGasto, ficha.NombreCuentaGasto));
        Assert.Contains(ficha.CuentasGasto, c => c.Valor == "6290002");
        Assert.Equal(("6290002", 3), (ficha.Construir().CuentaGasto, ficha.Construir().IdCentroCoste!.Value));

        ficha.CodigoCuentaGasto = "";
        Assert.Null(ficha.Construir().CuentaGasto);
    }

    [Fact]
    public async Task Cancelar_el_buscador_deja_la_cuenta_que_habia()
    {
        var textos = new List<string>();
        var ficha = Ficha(_ => Task.FromResult<CuentaContable?>(null), textos);
        await ficha.BuscarCuentaGastoAsync(null, diferir: false);
        Assert.Equal(["6000000"], textos);
        Assert.Equal("6000000", ficha.CodigoCuentaGasto);
        Assert.False(ficha.TieneCambios);
    }
}
