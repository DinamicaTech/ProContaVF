using System.Windows.Input;

namespace ProConta.UI.Base.Comandos;

/// <summary>Comando de presentación con una acción y una condición opcional.</summary>
public sealed class ComandoDelegado : ICommand
{
    private readonly Action<object?> _ejecutar;
    private readonly Func<object?, bool>? _puede;

    public ComandoDelegado(Action<object?> ejecutar, Func<object?, bool>? puedeEjecutar = null)
    {
        _ejecutar = ejecutar ?? throw new ArgumentNullException(nameof(ejecutar));
        _puede = puedeEjecutar;
    }

    public ComandoDelegado(Action ejecutar, Func<bool>? puedeEjecutar = null)
        : this(_ => ejecutar(), puedeEjecutar is null ? null : _ => puedeEjecutar()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _puede?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _ejecutar(parameter);
}
