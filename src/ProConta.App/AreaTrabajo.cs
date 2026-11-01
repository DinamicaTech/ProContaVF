using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.App;

/// <summary>Pestaña del área de trabajo tal como la pinta la tira de pestañas.</summary>
public sealed class PestanaContenedor : INotifyPropertyChanged
{
    internal PestanaContenedor(IPestanaTrabajo pantalla, bool cerrable, AreaTrabajo area)
    {
        Pantalla = pantalla;
        Cerrable = cerrable;
        ActivarCommand = new ComandoDelegado(() => area.Activar(pantalla));
        CerrarCommand = new ComandoDelegado(() => area.PedirCierre(this));
        pantalla.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IPestanaTrabajo.Titulo) or nameof(IPestanaTrabajo.Ayuda) or nameof(IPestanaTrabajo.TieneCambios) or nameof(IPestanaTrabajo.GuardadoFallido) or "")
            {
                Avisar(nameof(Titulo)); Avisar(nameof(Ayuda)); Avisar(nameof(ConCambios)); Avisar(nameof(ConFallo));
                area.EstadoCambiado();
            }
        };
    }

    public IPestanaTrabajo Pantalla { get; }
    public bool Cerrable { get; }
    public string Titulo => Pantalla.Titulo;
    public string Ayuda => Pantalla.Ayuda + (Cerrable ? ". Clic central o Ctrl+F4 para cerrar" : "");
    public Geometry? Icono => Application.Current.TryFindResource("Icono." + Pantalla.Icono) as Geometry;
    public bool ConCambios => Pantalla.TieneCambios && !Pantalla.GuardadoFallido;
    public bool ConFallo => Pantalla.GuardadoFallido;

    private bool _activa;
    public bool Activa { get => _activa; set { if (_activa == value) return; _activa = value; Avisar(); } }

    public ICommand ActivarCommand { get; }
    public ICommand CerrarCommand { get; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Avisar([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>
/// Área de trabajo con pestañas del contenedor (plataforma/contenedor): cada pantalla conserva su vista mientras está
/// abierta; las pestañas con cambios muestran un punto y las que no se pudieron guardar, un triángulo; al cerrar una
/// con cambios se ofrece guardar, descartar o seguir editando.
/// </summary>
public sealed class AreaTrabajo : IAnfitrionPestanas, INotifyPropertyChanged
{
    private readonly Grid _vistas;
    private readonly Action<string> _avisar;

    public AreaTrabajo(Grid contenedorVistas, Action<string> avisar)
    {
        _vistas = contenedorVistas;
        _avisar = avisar;
    }

    public ObservableCollection<PestanaContenedor> Lista { get; } = [];

    public IReadOnlyList<IPestanaTrabajo> Pestanas => Lista.Select(p => p.Pantalla).ToList();
    public IPestanaTrabajo? Activa => Lista.FirstOrDefault(p => p.Activa)?.Pantalla;

    public string Resumen
    {
        get
        {
            var conCambios = Lista.Count(p => p.Pantalla.TieneCambios);
            var texto = Lista.Count == 1 ? "1 pantalla abierta" : $"{Lista.Count} pantallas abiertas";
            return conCambios == 0 ? texto : $"{texto} · {conCambios} con cambios sin guardar";
        }
    }

    public bool HayCambios => Lista.Any(p => p.Pantalla.TieneCambios);

    internal void AbrirFija(IPestanaTrabajo pantalla) => Anadir(pantalla, cerrable: false, posicion: null);

    public void Abrir(IPestanaTrabajo pestana, IPestanaTrabajo? sustituir = null)
    {
        int? posicion = null;
        if (sustituir is not null && Buscar(sustituir) is { } vieja)
        {
            posicion = Lista.IndexOf(vieja);
            Quitar(vieja);
        }
        Anadir(pestana, cerrable: true, posicion);
    }

    private void Anadir(IPestanaTrabajo pantalla, bool cerrable, int? posicion)
    {
        var p = new PestanaContenedor(pantalla, cerrable, this);
        if (posicion is int i && i <= Lista.Count) Lista.Insert(i, p); else Lista.Add(p);
        var vista = (FrameworkElement)pantalla.Vista;
        vista.Visibility = Visibility.Collapsed;
        _vistas.Children.Add(vista);
        Activar(pantalla);
    }

    public void Activar(IPestanaTrabajo pestana)
    {
        foreach (var p in Lista)
        {
            p.Activa = ReferenceEquals(p.Pantalla, pestana);
            ((FrameworkElement)p.Pantalla.Vista).Visibility = p.Activa ? Visibility.Visible : Visibility.Collapsed;
        }
        EstadoCambiado();
    }

    public void Cerrar(IPestanaTrabajo pestana)
    {
        if (Buscar(pestana) is { } p) Quitar(p);
    }

    private void Quitar(PestanaContenedor p)
    {
        var indice = Lista.IndexOf(p);
        var eraActiva = p.Activa;
        Lista.Remove(p);
        _vistas.Children.Remove((UIElement)p.Pantalla.Vista);
        p.Pantalla.AlCerrar();
        if (eraActiva && Lista.Count > 0) Activar(Lista[Math.Clamp(indice - 1, 0, Lista.Count - 1)].Pantalla);
        EstadoCambiado();
    }

    public void Avisar(string texto) => _avisar(texto);

    private PestanaContenedor? Buscar(IPestanaTrabajo pantalla) => Lista.FirstOrDefault(p => ReferenceEquals(p.Pantalla, pantalla));

    /// <summary>Cierre pedido por el operador: si hay cambios, pregunta qué hacer con ellos.</summary>
    internal async void PedirCierre(PestanaContenedor p)
    {
        if (!p.Cerrable) return;
        if (!p.Pantalla.TieneCambios)
        {
            Quitar(p);
            return;
        }
        Activar(p.Pantalla);
        var opcion = ServicioMensajes.Preguntar(Application.Current.MainWindow, $"Cambios sin guardar en {p.Titulo}",
            "Si cierras la pestaña sin guardar, pierdes estos cambios:", p.Pantalla.DescribirCambios(),
            new OpcionMensaje("Seguir editando", "No cierra la pestaña", Cancelar: true),
            new OpcionMensaje("Descartar cambios y cerrar", "Cierra la pestaña sin guardar: los cambios se pierden", Peligro: true),
            new OpcionMensaje("Guardar y cerrar", "Guarda los cambios y cierra la pestaña", Principal: true));
        if (opcion == 1)
        {
            p.Pantalla.Descartar();
            Quitar(p);
        }
        else if (opcion == 2 && await p.Pantalla.GuardarAsync())
        {
            Quitar(p);
        }
    }

    /// <summary>
    /// Salida de la aplicación: con cambios pendientes ofrece guardarlos todos, salir sin guardar o seguir trabajando.
    /// Devuelve true si se puede salir.
    /// </summary>
    internal async Task<bool> PuedeSalirAsync()
    {
        var conCambios = Lista.Where(p => p.Pantalla.TieneCambios).ToList();
        if (conCambios.Count == 0) return true;
        var opcion = ServicioMensajes.Preguntar(Application.Current.MainWindow,
            conCambios.Count == 1 ? "Una pantalla con cambios sin guardar" : $"{conCambios.Count} pantallas con cambios sin guardar",
            "Si sales sin guardar, pierdes estos cambios:",
            conCambios.SelectMany(p => p.Pantalla.DescribirCambios().Select(c => $"{p.Titulo} · {c}")).ToList(),
            new OpcionMensaje("Seguir trabajando", "No sale de ProContaVF", Cancelar: true),
            new OpcionMensaje("Salir sin guardar", "Sale y pierde los cambios pendientes", Peligro: true),
            new OpcionMensaje("Guardar todo y salir", "Guarda los cambios de cada pantalla y sale si todo se ha guardado", Principal: true));
        if (opcion == 1) return true;
        if (opcion != 2) return false;
        foreach (var p in conCambios)
        {
            Activar(p.Pantalla);
            if (!await p.Pantalla.GuardarAsync())
            {
                _avisar($"No salgo: los cambios de {p.Titulo} no se han podido guardar. Corrígelos o descártalos y vuelve a salir.");
                return false;
            }
        }
        return true;
    }

    internal void EstadoCambiado()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Resumen)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HayCambios)));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Pestaña Inicio: fija, sin cambios que guardar.</summary>
internal sealed class PestanaInicio(FrameworkElement vista) : IPestanaTrabajo
{
    public string Titulo => "Inicio";
    public string Ayuda => "Página de inicio";
    public string Icono => "layout";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => vista;
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() { }
#pragma warning disable CS0067 // la pestaña Inicio no cambia
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
}
