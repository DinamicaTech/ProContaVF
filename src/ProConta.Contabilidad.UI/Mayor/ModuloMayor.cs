using System.ComponentModel;
using System.Windows;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Mayor;

/// <summary>Pestaña Contabilidad › Mayor del contenedor. Solo consulta: nunca tiene cambios pendientes.</summary>
public sealed class PestanaMayor : IPestanaTrabajo
{
    private readonly PantallaMayor _vista;

    internal PestanaMayor(MayorVM vm, Action cerrada)
    {
        Modelo = vm;
        _cerrada = cerrada;
        _vista = new PantallaMayor { DataContext = vm };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MayorVM.Titulo) or nameof(MayorVM.TextoCuenta))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Titulo)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ayuda)));
            }
        };
    }

    private readonly Action _cerrada;
    public MayorVM Modelo { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Titulo => Modelo.Titulo;
    public string Ayuda => Modelo.Titulo == "Mayor" ? "Mayor de contabilidad: elige una cuenta" : $"{Modelo.Titulo}: {Modelo.TextoCuenta.Replace("  ", " ")} · {Modelo.TextoEjercicios}";
    public string Icono => "book";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista;
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _cerrada();
}

/// <summary>
/// El Mayor dentro del contenedor: abre (o activa) la pestaña Contabilidad › Mayor con unas cuentas, y crea el Mayor embebido
/// para otras pantallas (ficha de terceros), que no tiene recientes y abre en la pestaña del Mayor lo que sale de sus cuentas.
/// </summary>
public sealed class ModuloMayor(ContextoMayor contexto, IAnfitrionPestanas anfitrion, IDialogosMayor dialogos)
{
    private PestanaMayor? _pestana;

    /// <summary>Abre la pestaña del Mayor (vacía o con unas cuentas).</summary>
    public void Abrir(IReadOnlyList<string>? cuentas = null)
    {
        if (_pestana is null)
        {
            var vm = new MayorVM(contexto, dialogos, anfitrion.Avisar);
            _pestana = new PestanaMayor(vm, () => _pestana = null);
            anfitrion.Abrir(_pestana);
        }
        else anfitrion.Activar(_pestana);
        if (cuentas is { Count: > 0 }) _ = _pestana.Modelo.VerCuentasAsync(cuentas);
    }

    /// <summary>Abre la pestaña del Mayor buscando unas facturas en todas las cuentas.</summary>
    public void BuscarFacturas(IReadOnlyList<string> documentos)
    {
        Abrir();
        _ = BuscarAsync(_pestana!.Modelo, documentos);
    }

    private static async Task BuscarAsync(MayorVM vm, IReadOnlyList<string> documentos)
    {
        await vm.IniciarAsync();
        await vm.BuscarFacturasAsync(documentos);
    }

    /// <summary>Mayor embebido de unas cuentas (sin recientes): el mismo control y la misma sesión que el completo.</summary>
    public FrameworkElement CrearEmbebido(IReadOnlyList<string> cuentas)
    {
        var vm = new MayorVM(contexto, dialogos, anfitrion.Avisar, cuentas)
        {
            AbrirEnMayor = c => Abrir(c),
            BuscarEnMayor = BuscarFacturas,
        };
        return new PantallaMayor { DataContext = vm };
    }
}
