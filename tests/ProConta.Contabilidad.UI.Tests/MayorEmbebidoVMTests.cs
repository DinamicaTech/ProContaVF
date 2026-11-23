using ProConta.Contabilidad.Mayor;
using ProConta.Contabilidad.UI.Mayor;
using ProConta.UI.Base.Preferencias;

namespace ProConta.Contabilidad.UI.Tests;

/// <summary>
/// Mayor embebido en la ficha del tercero (terceros/pantallas 2026-11-23): panel con las subcuentas del tercero y su papel, mayor
/// fusionado de las elegidas y navegación a la contrapartida dentro del embebido, sin añadirla al panel. Datos inventados.
/// </summary>
public class MayorEmbebidoVMTests
{
    private static FuenteMayorMemoria Fuente()
    {
        var f = new FuenteMayorMemoria("S01").Ejercicio(1, 2026);
        f.Cuenta(1, "4000001", "Proveedor de prueba").Cuenta(1, "4100001", "Proveedor de prueba").Cuenta(1, "6000001", "Compras")
         .Cuenta(1, "6290001", "Servicios").Cuenta(1, "5720001", "Banco de prueba");
        var d = new DateTime(2026, 1, 10);
        f.Asiento(new(1, 1, d, "Factura de mercaderías", [new("6000001", 100m, 0m), new("4000001", 0m, 100m)]));
        f.Asiento(new(2, 1, d.AddDays(5), "Factura de servicios", [new("6290001", 50m, 0m), new("4100001", 0m, 50m)]));
        f.Asiento(new(3, 1, d.AddDays(20), "Pago de mercaderías", [new("4000001", 100m, 0m), new("5720001", 0m, 100m)]));
        return f;
    }

    private static MayorVM Embebido(List<IReadOnlyList<string>> abiertas)
    {
        var papeles = new Dictionary<string, string> { ["4000001"] = "Proveedor", ["4100001"] = "Acreedor" };
        return new MayorVM(new ContextoMayor(Fuente(), "S01", 2026, "Sociedad de prueba"), new SinDialogos(), _ => { }, ["4000001", "4100001"],
            new PreferenciasMemoria(), papeles) { AbrirEnMayor = abiertas.Add };
    }

    [Fact]
    public async Task ElPanelTieneLasSubcuentasDelTerceroConSuPapelYLaVistaLasFusiona()
    {
        var vm = Embebido([]);
        await vm.IniciarAsync();
        Assert.Equal(["4000001 Proveedor", "4100001 Acreedor"], vm.CuentasFijas.Select(c => $"{c.Codigo} {c.Papel}"));
        Assert.Equal("Proveedor de prueba", vm.CuentasFijas[0].Nombre);
        Assert.Equal(["4000001", "4100001"], vm.CuentasVista);
        Assert.Equal(-50m, vm.Saldo);

        await vm.VerCuentasTerceroAsync([vm.CuentasFijas[1]]);
        Assert.Equal(["4100001"], vm.CuentasVista);
        Assert.Equal(-50m, vm.Saldo);
    }

    [Fact]
    public async Task LaContrapartidaSeVeEnElEmbebidoSinEntrarEnElPanel()
    {
        var abiertas = new List<IReadOnlyList<string>>();
        var vm = Embebido(abiertas);
        await vm.IniciarAsync();
        await vm.IrACuentaAsync("6000001");
        Assert.Equal(["6000001"], vm.CuentasVista);
        Assert.Empty(abiertas);
        Assert.Equal(2, vm.CuentasFijas.Count);
        Assert.True(vm.AbrirEnMayorCommand.CanExecute(null));
        vm.AbrirEnMayorCommand.Execute(null);
        Assert.Equal(["6000001"], Assert.Single(abiertas));
    }

    private sealed class PreferenciasMemoria : IPreferenciasOperador
    {
        private readonly Dictionary<string, object?> _valores = [];
        public T? Leer<T>(string clave) => _valores.TryGetValue(clave, out var v) ? (T?)v : default;
        public void Guardar<T>(string clave, T valor) => _valores[clave] = valor;
        public void Borrar(string clave) => _valores.Remove(clave);
    }

    private sealed class SinDialogos : IDialogosMayor
    {
        public string? ElegirCuenta(SelectorCuentasVM selector, string titulo) => null;
        public void VerAsiento(AsientoMayor asiento, Action<string> abrirMayor) { }
        public void VerHistorial(FilaMayor fila, IReadOnlyList<CambioApunteMayor> cambios) { }
        public void Error(string titulo, string mensaje) => throw new InvalidOperationException(mensaje);
        public (string Valor, string Motivo)? PedirTexto(string titulo, string explicacion, string etiqueta, string? inicial, int longitud, bool pideMotivo, bool valorObligatorio) => null;
        public string? PedirMotivo(string titulo, string explicacion) => null;
        public (bool Cuadrada, string Notas)? EditarCuadre(string cuenta, bool cuadrada, string? notas, string informacion) => null;
    }
}
