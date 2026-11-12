using System.IO;
using ProConta.Contabilidad.Cuentas;
using ProConta.Datos.Persistencia;
using ProConta.Norma43.Importacion;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Pruebas.Access;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.Tests;

/// <summary>Pantalla Bancos con diálogos simulados contra una copia de pruebas con datos inventados.</summary>
[Trait("Categoria", "Access")]
public sealed class BancosVMAccessTests : IDisposable
{
    private sealed class Dialogos : IDialogosBancos
    {
        public List<string> Avisos { get; } = [];
        public bool AceptarAlta { get; set; } = true;
        public string? Motivo { get; set; } = "Fichero equivocado";
        public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) { Avisos.Add($"{titulo}: {mensaje}"); return 0; }
        public string? ElegirFichero() => null;
        public string? PedirTexto(string titulo, string explicacion, string etiqueta, string textoBoton) => Motivo;
        public bool DarDeAlta(IReadOnlyList<AltaCuentaVM> altas)
        {
            foreach (var a in altas) { a.Nombre = "Banco ficticio de pruebas"; a.Contable = a.Opciones.FirstOrDefault(); }
            return AceptarAlta;
        }
        public void AbrirArchivo(string ruta) { }
    }

    private readonly BdPruebas _copia = BdPruebas.Nueva();
    private readonly ContextoSociedadEjercicio _ctx = new("T01", 9001, 2026);
    private readonly Dialogos _dialogos = new();
    private readonly List<string> _barra = [];
    private IBaseDatos Bd => _copia.Bd();

    public void Dispose() => _copia.Dispose();

    private BancosVM Vm() => new(new ServicioImportacionN43(Bd), new ConsultaBancos(Bd), _dialogos, new ContextoBancos(_ctx, "Sociedad sintética", new Actor("Pruebas"), _barra.Add));

    private async Task<string> FicheroAsync()
    {
        await DatosSinteticos.SociedadAsync(Bd, "T01", "Sociedad sintética");
        await DatosSinteticos.EjercicioAsync(Bd, 9001, "T01", 2026);
        Assert.True((await new CatalogoCuentasAccess(Bd).AltaAsync(_ctx, new AltaCuenta("5720001", "Banco ficticio"))).EsCorrecto);
        var ruta = Path.Combine(BdPruebas.CarpetaTemporal(), "extracto.n43");
        await File.WriteAllBytesAsync(ruta, new FicheroSintetico().Cuenta("0049", "1500", "0012345678", new(2026, 11, 1), new(2026, 11, 7), 1000m, "EMPRESA FICTICIA",
            new Mov(new(2026, 11, 2), 120.50m, true, "RECIBO LUZ FICTICIA"), new Mov(new(2026, 11, 3), 2500m, false, "TRANSFERENCIA CLIENTE")).Bytes());
        return ruta;
    }

    [Fact]
    public async Task Importar_con_una_cuenta_nueva_la_da_de_alta_y_muestra_la_cuenta_con_su_lote()
    {
        var ruta = await FicheroAsync();
        var vm = Vm();
        await vm.CargarAsync();
        Assert.Equal(EstadoVista.Vacio, vm.Estado);

        await vm.ImportarAsync(ruta);
        Assert.Empty(_dialogos.Avisos);
        var c = Assert.Single(vm.Cuentas);
        Assert.Equal(("Banco ficticio de pruebas", "5720001 · Banco ficticio", 3379.50m, 2), (c.Nombre, c.Contable, c.Datos.SaldoBanco!.Value, c.Datos.Movimientos));
        Assert.Contains(_barra, t => t.StartsWith("Importado el lote"));
        for (var i = 0; i < 50 && vm.Lotes.Count == 0; i++) await Task.Delay(20);
        Assert.Equal("extracto.n43", Assert.Single(vm.Lotes).Fichero);

        // Otra vez el mismo fichero: aviso de que ya estaba, sin duplicar.
        await vm.ImportarAsync(ruta);
        Assert.Contains(_dialogos.Avisos, t => t.StartsWith("Ya estaba importado"));
        Assert.Equal(2, vm.Cuentas.Single().Datos.Movimientos);
    }

    [Fact]
    public async Task Cancelar_el_alta_no_importa_nada()
    {
        var ruta = await FicheroAsync();
        _dialogos.AceptarAlta = false;
        var vm = Vm();
        await vm.ImportarAsync(ruta);
        Assert.Empty(vm.Cuentas);
        Assert.Contains(_barra, t => t.Contains("no están dadas de alta"));
    }
}
