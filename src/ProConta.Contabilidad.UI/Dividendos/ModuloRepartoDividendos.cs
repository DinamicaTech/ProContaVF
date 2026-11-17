using System.Windows;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.Dividendos;

/// <summary>Diálogos de la pantalla con los mensajes comunes; el motivo y el error, los mismos del Mayor.</summary>
public sealed class DialogosRepartoDividendos(Func<Window?> propietario) : IDialogosRepartoDividendos
{
    private readonly Mayor.DialogosMayor _comunes = new(propietario);

    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, detalles, opciones);

    public string? PedirMotivo(string titulo, string explicacion) => _comunes.PedirMotivo(titulo, explicacion);

    public void Error(string titulo, string mensaje) => _comunes.Error(titulo, mensaje);

    public string? PedirHora(string titulo, string explicacion, string propuesta)
    {
        var d = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 460, SizeToContent = SizeToContent.Height };
        var panel = new System.Windows.Controls.StackPanel();
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new System.Windows.Controls.TextBlock { Text = "Hora de la junta", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var hora = new System.Windows.Controls.TextBox { Text = propuesta, MaxLength = 5, Width = 80, HorizontalAlignment = HorizontalAlignment.Left,
            ToolTip = "Hora a la que se reúne la junta (hh:mm), por ejemplo 11:00; en el acta sale como «11,00 horas»" };
        panel.Children.Add(hora);
        d.Content = panel;
        var aceptar = new System.Windows.Controls.Button { Content = "Generar el acta", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0),
            ToolTip = "Genera el PDF del acta con esta hora" };
        var cancelar = new System.Windows.Controls.Button { Content = "Cancelar", IsCancel = true, ToolTip = "Cierra sin generar nada" };
        aceptar.Click += (_, _) => d.DialogResult = true;
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new System.Windows.Controls.StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => { hora.Focus(); hora.SelectAll(); };
        return d.ShowDialog() == true ? hora.Text.Trim() : null;
    }
}

/// <summary>Reparto de dividendos dentro del contenedor (contabilidad/procesos/aplicacion-resultado): una sola pestaña por sesión.</summary>
public sealed class ModuloRepartoDividendos(ContextoRepartoDividendos contexto, IAnfitrionPestanas anfitrion, IDialogosRepartoDividendos dialogos)
{
    private RepartoDividendosVM? _vm;

    /// <summary>Abre (o activa) la pantalla.</summary>
    public void Abrir()
    {
        if (_vm is not null)
        {
            anfitrion.Activar(_vm);
            _ = _vm.CargarAsync(_vm.Seleccion?.IdReparto);
            return;
        }
        _vm = new RepartoDividendosVM(contexto, dialogos, () => _vm = null);
        anfitrion.Abrir(_vm);
        _ = _vm.CargarAsync();
    }
}
