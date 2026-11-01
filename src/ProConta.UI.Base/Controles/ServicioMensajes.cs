using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ProConta.UI.Base.Controles;

/// <summary>Botón de un mensaje: el texto nombra la acción real (docs/plataforma/ui-base/mensajes.md).</summary>
/// <param name="Texto">Verbo de la acción real («Guardar y cerrar», «Recargar la ficha»).</param>
/// <param name="Ayuda">Tooltip: qué hace exactamente.</param>
/// <param name="Principal">Botón principal (Intro); una acción destructiva nunca lo es.</param>
/// <param name="Peligro">Acción destructiva (descartar, borrar), en rojo.</param>
/// <param name="Cancelar">La elige Esc o el aspa.</param>
public sealed record OpcionMensaje(string Texto, string Ayuda, bool Principal = false, bool Peligro = false, bool Cancelar = false);

/// <summary>
/// Servicio común de mensajes que bloquean y piden una decisión: título breve, explicación, lista opcional de
/// detalles y botones con verbos. Devuelve el índice de la opción elegida (la de cancelar si se cierra con Esc).
/// </summary>
public static class ServicioMensajes
{
    public static int Preguntar(Window? propietario, string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones)
    {
        var dialogo = new DialogoProConta { Title = titulo, Owner = propietario ?? Application.Current?.MainWindow, Width = 640 };
        if (dialogo.Owner is null) dialogo.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var cuerpo = new StackPanel();
        cuerpo.Children.Add(new TextBlock { Text = mensaje, TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        if (detalles is { Count: > 0 })
        {
            var lista = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            foreach (var d in detalles.Take(12))
                lista.Children.Add(new TextBlock { Text = "· " + d, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)dialogo.FindResource("Pincel.Texto2"), Margin = new Thickness(0, 2, 0, 0) });
            if (detalles.Count > 12)
                lista.Children.Add(new TextBlock { Text = $"· y {detalles.Count - 12} más", Foreground = (Brush)dialogo.FindResource("Pincel.Apagado") });
            cuerpo.Children.Add(new Border
            {
                Child = lista, Padding = new Thickness(12, 8, 12, 10), Margin = new Thickness(0, 6, 0, 0), CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1), BorderBrush = (Brush)dialogo.FindResource("Pincel.Linea"), Background = (Brush)dialogo.FindResource("Pincel.Fondo")
            });
        }
        dialogo.Content = cuerpo;

        var elegida = Array.FindIndex(opciones, o => o.Cancelar);
        var botones = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < opciones.Length; i++)
        {
            var o = opciones[i];
            var indice = i;
            var boton = new Button
            {
                Content = o.Texto, ToolTip = o.Ayuda, Margin = new Thickness(8, 0, 0, 0), IsDefault = o.Principal, IsCancel = o.Cancelar,
                Style = (Style)dialogo.FindResource(o.Principal ? "Boton.Primario" : o.Peligro ? "Boton.Peligro" : "Boton")
            };
            boton.Click += (_, _) => { elegida = indice; dialogo.DialogResult = true; };
            botones.Children.Add(boton);
        }
        dialogo.Botones = botones;
        return dialogo.ShowDialog() == true ? elegida : Array.FindIndex(opciones, o => o.Cancelar);
    }
}
