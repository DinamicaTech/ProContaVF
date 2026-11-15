using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Inicio.UI;

/// <summary>Pestaña fija Inicio del contenedor (no se cierra ni tiene cambios que guardar).</summary>
public sealed class PestanaInicio : IPestanaTrabajo
{
    private readonly PantallaInicio _vista;

    public PestanaInicio(InicioVM vm)
    {
        Modelo = vm;
        _vista = new PantallaInicio { DataContext = vm };
    }

    public InicioVM Modelo { get; }
    public string Titulo => "Inicio";
    public string Ayuda => "Inicio: indicadores, facturación y tesorería de la sociedad y el ejercicio de trabajo";
    public string Icono => "layout";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista;
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() { }
#pragma warning disable CS0067 // la pestaña Inicio no cambia de título ni tiene cambios
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067
}

/// <summary>
/// La pantalla de inicio dentro del contenedor (pantalla-de-inicio): crea la pestaña fija y calcula los indicadores al
/// abrirla. Las pantallas que abren los indicadores las conecta la aplicación con <see cref="AccionesInicio"/>.
/// </summary>
public static class ModuloInicio
{
    public static PestanaInicio Crear(ServicioInicio servicio, ContextoInicio contexto, AccionesInicio acciones, Func<Window?> propietario)
    {
        var vm = new InicioVM(servicio, contexto, new DialogosInicio(propietario), acciones);
        var pestana = new PestanaInicio(vm);
        _ = vm.IniciarAsync();
        return pestana;
    }
}

/// <summary>Diálogos reales de la pantalla de inicio.</summary>
public sealed class DialogosInicio(Func<Window?> propietario) : IDialogosInicio
{
    public IReadOnlySet<string>? Personalizar(IReadOnlyList<BloqueInicio> bloques, IReadOnlySet<string> ocultos)
    {
        var d = new DialogoProConta { Title = "Personalizar Inicio", Owner = propietario(), Width = 520, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Elige qué bloques se ven en Inicio. La elección se guarda para tu operador.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var casillas = new List<(BloqueInicio Bloque, CheckBox Casilla)>();
        foreach (var b in bloques)
        {
            var c = new CheckBox { Content = b.Nombre, IsChecked = !ocultos.Contains(b.Clave), ToolTip = b.Ayuda, Margin = new Thickness(0, 0, 0, 8) };
            casillas.Add((b, c));
            panel.Children.Add(c);
        }
        panel.Children.Add(new TextBlock
        {
            Text = "Los ratios, la salud de la contabilidad, la agenda y la ocupación turística llegarán en las próximas fases.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Foreground = (System.Windows.Media.Brush)d.FindResource("Pincel.Apagado"),
        });
        d.Content = panel;
        var aplicar = new Button { Content = "Aplicar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Muestra solo los bloques marcados" };
        var todos = new Button { Content = "Mostrar todos", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Marca todos los bloques (como viene de fábrica)" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aplicar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        todos.Click += (_, _) => { foreach (var (_, c) in casillas) c.IsChecked = true; };
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { todos, cancelar, aplicar } };
        return d.ShowDialog() == true ? casillas.Where(x => x.Casilla.IsChecked != true).Select(x => x.Bloque.Clave).ToHashSet(StringComparer.Ordinal) : null;
    }
}
