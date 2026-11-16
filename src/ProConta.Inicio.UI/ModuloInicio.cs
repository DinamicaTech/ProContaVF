using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ProConta.UI.Base.Contenedor;
using ProConta.Inicio.Salud;
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
    public static PestanaInicio Crear(ServicioInicio servicio, ContextoInicio contexto, AccionesInicio acciones, Func<Window?> propietario, ServicioSalud? salud = null,
        ProConta.Inicio.Bloques.ServicioBloques? bloques = null)
    {
        var vm = new InicioVM(servicio, contexto, new DialogosInicio(propietario), acciones, salud: salud, bloques: bloques);
        var pestana = new PestanaInicio(vm);
        _ = vm.IniciarAsync();
        return pestana;
    }
}

/// <summary>Diálogos reales de la pantalla de inicio.</summary>
public sealed class DialogosInicio(Func<Window?> propietario) : IDialogosInicio
{
    public EleccionInicio? Personalizar(IReadOnlyList<BloqueInicio> bloques, IReadOnlySet<string> ocultos, IReadOnlyList<CuentaBancoInicio> bancos)
    {
        var d = new DialogoProConta { Title = "Personalizar Inicio", Owner = propietario(), Width = 560, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Elige qué bloques se ven en Inicio. La elección se guarda para tu operador.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var casillas = new List<(BloqueInicio Bloque, CheckBox Casilla)>();
        foreach (var b in bloques)
        {
            var c = new CheckBox { Content = b.Nombre, IsChecked = !ocultos.Contains(b.Clave), ToolTip = b.Ayuda, Margin = new Thickness(0, 0, 0, 8) };
            casillas.Add((b, c));
            panel.Children.Add(c);
        }
        var marcas = new List<(CuentaBancoInicio Banco, CheckBox Casilla)>();
        if (bancos.Count > 0)
        {
            panel.Children.Add(new TextBlock { Text = "Cuentas de inversión", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 4) });
            panel.Children.Add(new TextBlock
            {
                Text = "Marca las cuentas bancarias que no son tesorería operativa (por ejemplo, la de un bróker). No suman en el indicador de tesorería ni en su gráfico, " +
                       "se ven aparte bajo «Inversiones» y en los ratios cuentan como realizable, no como disponible. La marca se guarda para tu operador y esta sociedad.",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), Foreground = (System.Windows.Media.Brush)d.FindResource("Pincel.Apagado"),
            });
            foreach (var b in bancos)
            {
                var c = new CheckBox
                {
                    Content = $"{b.Cuenta} · {b.Nombre} — no es tesorería operativa (inversión)", IsChecked = b.Inversion, Margin = new Thickness(0, 0, 0, 6),
                    ToolTip = $"Marcada, la cuenta {b.Cuenta} ({b.Nombre}) sale de la tesorería operativa y se muestra aparte como inversión",
                };
                marcas.Add((b, c));
                panel.Children.Add(c);
            }
        }
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 640 };
        var aplicar = new Button { Content = "Aplicar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Muestra solo los bloques marcados" };
        var todos = new Button { Content = "Mostrar todos", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Marca todos los bloques (como viene de fábrica)" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        aplicar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        todos.Click += (_, _) => { foreach (var (_, c) in casillas) c.IsChecked = true; };
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { todos, cancelar, aplicar } };
        if (d.ShowDialog() != true) return null;
        return new EleccionInicio(
            casillas.Where(x => x.Casilla.IsChecked != true).Select(x => x.Bloque.Clave).ToHashSet(StringComparer.Ordinal),
            marcas.Where(x => x.Casilla.IsChecked == true).Select(x => x.Banco.Cuenta).ToHashSet(StringComparer.Ordinal));
    }

    public void Copiar(string texto)
    {
        try { Clipboard.SetText(texto); }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Otro programa tiene el portapapeles abierto: se reintenta una vez.
            try { Clipboard.SetText(texto); } catch (System.Runtime.InteropServices.ExternalException) { }
        }
    }

    public void PorQue(PorQueSaludVM porQue)
    {
        var d = new DialogoProConta { Title = porQue.Titulo, Owner = propietario(), Width = 600, SizeToContent = SizeToContent.Height };
        var apagado = (System.Windows.Media.Brush)d.FindResource("Pincel.Apagado");
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = $"Lo detecta {porQue.Nodo}", Foreground = apagado, Margin = new Thickness(0, 0, 0, 10) });
        panel.Children.Add(new TextBlock { Text = porQue.Explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        void Bloque(string titulo, string texto)
        {
            panel.Children.Add(new TextBlock { Text = titulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 3) });
            panel.Children.Add(new TextBlock { Text = texto, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        }
        Bloque("Por qué aparece", porQue.Condicion);
        Bloque("Cómo se resuelve", porQue.ComoSeResuelve);
        panel.Children.Add(new TextBlock { Text = porQue.Destino, Foreground = apagado, TextWrapping = TextWrapping.Wrap });
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560 };
        var botones = new StackPanel { Orientation = Orientation.Horizontal };
        var copiar = new Button { Content = "Copiar el texto", Margin = new Thickness(0, 0, 8, 0), ToolTip = "Copia al portapapeles el título, la explicación y dónde se resuelve" };
        copiar.Click += (_, _) => Copiar(porQue.TextoCopiar + Environment.NewLine + "Por qué aparece: " + porQue.Condicion + Environment.NewLine + "Cómo se resuelve: " + porQue.ComoSeResuelve);
        botones.Children.Add(copiar);
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, ToolTip = "Cierra esta ventana" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        botones.Children.Add(cerrar);
        if (porQue.Abrir is { } abrir)
        {
            var ir = new Button { Content = "Abrir la pantalla", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = porQue.Destino };
            ir.Click += (_, _) => { d.DialogResult = true; };
            botones.Children.Add(ir);
            d.Botones = botones;
            if (d.ShowDialog() == true) abrir.Execute(null);
            return;
        }
        cerrar.IsDefault = true;
        d.Botones = botones;
        d.ShowDialog();
    }

    public void VerRatio(FichaRatioVM ficha)
    {
        var d = new DialogoProConta
        {
            Title = ficha.Titulo, Owner = propietario(), Width = 780, Height = 600, MinWidth = 520, MinHeight = 360, SizeToContent = SizeToContent.Manual, Redimensionable = true,
        };
        d.Content = new FichaRatio { DataContext = ficha };
        ficha.Cerrar = () => d.DialogResult = false;
        var cerrar = new Button { Content = "Cerrar", Style = (Style)d.FindResource("Boton.Primario"), IsCancel = true, IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Cierra la ficha" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        var botones = new StackPanel { Orientation = Orientation.Horizontal };
        if (ficha.AbrirBalanceCommand is { } balance)
            botones.Children.Add(new Button
            {
                Content = "Abrir el balance", Command = balance,
                ToolTip = "Abre en Excel el balance de situación del ejercicio de trabajo (Informes › Balance de situación), con las cifras de las que salen los ratios",
            });
        botones.Children.Add(cerrar);
        d.Botones = botones;
        d.ShowDialog();
    }
}
