using System.Windows;
using System.Windows.Controls;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Resultados;
using ProConta.Seguridad.Permisos;
using ProConta.Terceros.UI.Logica;

namespace ProConta.App;

/// <summary>
/// Conecta el rol «Operador ProConta» de la ficha del tercero con el servicio de operadores, como el operador de la sesión
/// (seguridad/pantallas 2026-11-24).
/// </summary>
internal sealed class OperadoresFichaTercero(ServicioOperadores operadores, int idOperadorSesion, Actor actor, Func<Window?> propietario) : IOperadoresFicha
{
    public Task<bool> PuedeGestionarAsync() => operadores.PuedeGestionarAsync(idOperadorSesion);

    public async Task<EstadoOperadorFicha?> ObtenerAsync(int idTercero) =>
        await operadores.RolDeTerceroAsync(idTercero) is { } r ? new EstadoOperadorFicha(r.IdOperador, r.NombreAcceso, r.Activo, r.TieneContrasena, r.Grupos) : null;

    public async Task<IReadOnlyList<GrupoOperadorFicha>> GruposAsync() =>
        (await operadores.ListarGruposAsync()).Select(g => new GrupoOperadorFicha(g.IdGrupo, g.Nombre, g.Descripcion)).ToList();

    public Task<Resultado> GuardarAsync(int idTercero, bool activo, IReadOnlyCollection<int> grupos, bool soloComprobar) =>
        operadores.GuardarRolAsync(idTercero, activo, grupos, idOperadorSesion, actor, soloComprobar);

    public async Task<string?> PasarOperadorAsync(int idTercero, string nombreTercero)
    {
        var candidatos = (await operadores.ListarConTerceroAsync()).Where(x => x.Operador.IdTercero != idTercero).ToList();
        if (candidatos.Count == 0) return "No hay ningún operador en otra ficha que pasar a esta.";
        var elegido = VentanaPasarOperador.Elegir(propietario(), nombreTercero, candidatos);
        if (elegido is not { } op) return null;
        var r = await operadores.PasarATerceroAsync(op.Operador.IdOperador, idTercero, idOperadorSesion, actor);
        if (!r.EsCorrecto) return $"No he pasado el operador: {r.Error!.Mensaje} {r.Error.AccionCorrectora}";
        return $"Hecho: el operador que estaba en la ficha {op.Operador.IdTercero} ({op.NombreTercero}) entra ahora con esta ficha, con su contraseña y sus grupos. " +
            $"Esa ficha sigue como estaba: si estaba repetida, desactívala.";
    }

    public async Task<string?> CambiarContrasenaAsync(int idOperador, string nombreAcceso)
    {
        var contrasena = VentanaContrasenaOperador.Pedir(propietario(), nombreAcceso);
        if (contrasena is null) return null;
        var r = await operadores.EstablecerContrasenaAsync(idOperador, contrasena, idOperadorSesion, actor);
        if (!r.EsCorrecto) return $"No he cambiado la contraseña: {r.Error!.Mensaje} {r.Error.AccionCorrectora}";
        return contrasena.Length == 0
            ? $"Hecho: {nombreAcceso} ya no tiene contraseña y entra con la contraseña vacía."
            : $"Hecho: he cambiado la contraseña de {nombreAcceso}. La nueva vale desde su próxima entrada.";
    }
}

/// <summary>Pide la contraseña nueva de un operador dos veces. Vacía la quita (la contraseña es opcional y sin requisitos).</summary>
internal sealed class VentanaContrasenaOperador : Window
{
    private readonly PasswordBox _clave = new() { Width = 280, ToolTip = "Contraseña nueva. Déjala vacía para que entre sin contraseña. Cualquier contraseña vale" };
    private readonly PasswordBox _repetida = new() { Width = 280, ToolTip = "Escribe otra vez la misma contraseña (vacía si la quitas)" };
    private readonly TextBlock _error = new() { Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Pincel.RojoError"), TextWrapping = TextWrapping.Wrap, MaxWidth = 300 };

    private VentanaContrasenaOperador(string nombre)
    {
        Title = $"ProContaVF · Contraseña de {nombre}";
        Style = (Style)Application.Current.FindResource("Ventana.ProConta");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Contraseña de {nombre}", Style = (Style)FindResource("Texto.Titulo") });
        panel.Children.Add(new TextBlock { Text = "Es opcional: vacía, entra con la contraseña vacía. Vale desde su próxima entrada.", TextWrapping = TextWrapping.Wrap, MaxWidth = 300,
            Margin = new Thickness(0, 8, 0, 14), Foreground = (System.Windows.Media.Brush)FindResource("Pincel.Apagado") });
        Campo(panel, "Contraseña nueva", _clave);
        Campo(panel, "Repite la contraseña", _repetida);
        panel.Children.Add(_error);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancelar = new Button { Content = "Cancelar", MinWidth = 90, IsCancel = true, ToolTip = "Cierra sin cambiar la contraseña" };
        var guardar = new Button { Content = "Guardar", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, Style = (Style)FindResource("Boton.Primario"),
            ToolTip = "Guarda la contraseña nueva (o la quita si está vacía)" };
        guardar.Click += (_, _) =>
        {
            if (_clave.Password != _repetida.Password) { _error.Text = "Las dos contraseñas no coinciden. Escríbelas otra vez."; return; }
            DialogResult = true;
        };
        botones.Children.Add(cancelar);
        botones.Children.Add(guardar);
        panel.Children.Add(botones);
        Content = panel;
        Loaded += (_, _) => _clave.Focus();
    }

    private void Campo(Panel panel, string etiqueta, Control control)
    {
        panel.Children.Add(new TextBlock { Text = etiqueta, Style = (Style)FindResource("Etiqueta.Campo") });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.Margin = new Thickness(0, 0, 0, 12);
        panel.Children.Add(control);
    }

    /// <summary>La contraseña escrita ("" para quitarla), o null si se cancela.</summary>
    public static string? Pedir(Window? propietario, string nombre)
    {
        var v = new VentanaContrasenaOperador(nombre);
        if (propietario is not null) v.Owner = propietario;
        else v.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return v.ShowDialog() == true ? v._clave.Password : null;
    }
}

/// <summary>Elige el operador que se pasa a la ficha abierta.</summary>
internal sealed class VentanaPasarOperador : Window
{
    private readonly ListBox _lista = new() { Width = 420, Height = 200, ToolTip = "Operador que pasa a esta ficha: su nombre de entrada y la ficha en la que está ahora" };

    private VentanaPasarOperador(string nombreTercero, IReadOnlyList<(Operador Operador, string NombreTercero)> candidatos)
    {
        Title = "ProContaVF · Pasar un operador a esta ficha";
        Style = (Style)Application.Current.FindResource("Ventana.ProConta");
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = $"Pasar un operador a {nombreTercero}", Style = (Style)FindResource("Texto.Titulo") });
        panel.Children.Add(new TextBlock
        {
            Text = "Elige el operador que es esta persona. Conserva su número, su contraseña y sus grupos; a partir de ahora entra con el nombre de esta ficha. La ficha que deja no se cambia.",
            TextWrapping = TextWrapping.Wrap, MaxWidth = 420, Margin = new Thickness(0, 8, 0, 14), Foreground = (System.Windows.Media.Brush)FindResource("Pincel.Apagado")
        });
        foreach (var c in candidatos)
            _lista.Items.Add(new ListBoxItem
            {
                Content = $"{c.Operador.Nombre}{(c.Operador.Activo ? "" : " (desactivado)")} · ficha {c.Operador.IdTercero} {c.NombreTercero}",
                Tag = c
            });
        if (_lista.Items.Count > 0) _lista.SelectedIndex = 0;
        panel.Children.Add(_lista);

        var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancelar = new Button { Content = "Cancelar", MinWidth = 90, IsCancel = true, ToolTip = "Cierra sin cambiar nada" };
        var pasar = new Button { Content = "Pasar aquí", MinWidth = 90, Margin = new Thickness(8, 0, 0, 0), IsDefault = true, Style = (Style)FindResource("Boton.Primario"),
            ToolTip = "Pasa el operador elegido a esta ficha al momento" };
        pasar.Click += (_, _) => { if (_lista.SelectedItem is not null) DialogResult = true; };
        _lista.MouseDoubleClick += (_, _) => { if (_lista.SelectedItem is not null) DialogResult = true; };
        botones.Children.Add(cancelar);
        botones.Children.Add(pasar);
        panel.Children.Add(botones);
        Content = panel;
        Loaded += (_, _) => _lista.Focus();
    }

    public static (Operador Operador, string NombreTercero)? Elegir(Window? propietario, string nombreTercero, IReadOnlyList<(Operador Operador, string NombreTercero)> candidatos)
    {
        var v = new VentanaPasarOperador(nombreTercero, candidatos);
        if (propietario is not null) v.Owner = propietario;
        else v.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return v.ShowDialog() == true && v._lista.SelectedItem is ListBoxItem { Tag: ValueTuple<Operador, string> c } ? c : null;
    }
}
