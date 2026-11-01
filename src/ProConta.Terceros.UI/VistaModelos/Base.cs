using System.Collections;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Organizacion.Geografia;
using ProConta.Terceros.Busqueda;
using ProConta.Terceros.Perfiles;
using ProConta.Terceros.Roles;
using ProConta.UI.Base.Controles;

namespace ProConta.Terceros.UI.VistaModelos;

/// <summary>Modelo de vista con avisos de cambio de propiedad.</summary>
public abstract class Observable : INotifyPropertyChanged
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

/// <summary>
/// Parte editable de la ficha (un domicilio, un email, una relación…): sus errores los calcula la ficha y se ven en el
/// campo (borde rojo, icono y tooltip) a través de <see cref="INotifyDataErrorInfo"/>.
/// </summary>
public abstract class ParteFicha(FichaTerceroVM ficha) : Observable, INotifyDataErrorInfo
{
    protected FichaTerceroVM Ficha { get; } = ficha;

    /// <summary>Objeto del modelo al que se refieren los errores (la clave de <see cref="Logica.ErrorFicha.Objeto"/>).</summary>
    public abstract object Modelo { get; }

    /// <summary>Propiedades que pueden tener error.</summary>
    protected abstract IEnumerable<string> CamposConError { get; }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasErrors => CamposConError.Any(c => Ficha.ErrorVisible(Modelo, c) is not null);

    public IEnumerable GetErrors(string? propertyName) =>
        propertyName is not null && Ficha.ErrorVisible(Modelo, propertyName) is { } e ? new[] { e } : Array.Empty<string>();

    /// <summary>Errores que solo conoce esta parte (una fecha mal escrita, una cuenta que no existe).</summary>
    internal virtual IEnumerable<Logica.ErrorFicha> ErroresPropios() => [];

    internal void RefrescarErrores()
    {
        foreach (var c in CamposConError) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(c));
    }

    /// <summary>El operador ha cambiado algo: la ficha recalcula cambios, errores y destinatarios.</summary>
    protected void Cambiado(string campo)
    {
        Ficha.Tocar(Modelo, campo);
        Ficha.Modificado();
    }
}

/// <summary>Servicios que usan las pantallas de Terceros (los motores y el contexto de trabajo).</summary>
public sealed record ServiciosTerceros(
    IRepositorioTerceros Repositorio,
    IBuscadorTerceros Buscador,
    ICatalogoPerfiles Perfiles,
    ServicioRoles Roles,
    ICatalogoCuentas Cuentas,
    Func<Task<CatalogoGeografico>> Geografia,
    ContextoSociedadEjercicio Contexto,
    string NombreSociedad,
    Actor Actor);

/// <summary>Diálogos que piden una decisión al operador (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogos
{
    /// <summary>Devuelve el índice de la opción elegida.</summary>
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);

    /// <summary>Buscador de terceros en una ventana modal; null si se cancela.</summary>
    TerceroEncontrado? ElegirTercero(string titulo, string explicacion, FiltroTerceros filtro);

    /// <summary>Pide nombre y descripción de un perfil nuevo; null si se cancela.</summary>
    (string Nombre, string? Descripcion)? PedirPerfilNuevo(string? nombrePropuesto);
}
