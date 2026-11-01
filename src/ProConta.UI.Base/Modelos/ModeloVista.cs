using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProConta.UI.Base.Modelos;

/// <summary>Modelo de vista con avisos de cambio de propiedad, común a las pantallas de las áreas.</summary>
public abstract class ModeloVista : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Avisar([CallerMemberName] string? propiedad = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propiedad));

    protected void Avisar(params string[] propiedades)
    {
        foreach (var p in propiedades) Avisar(p);
    }

    protected bool Fijar<T>(ref T campo, T valor, [CallerMemberName] string? propiedad = null)
    {
        if (EqualityComparer<T>.Default.Equals(campo, valor)) return false;
        campo = valor;
        Avisar(propiedad);
        return true;
    }
}
