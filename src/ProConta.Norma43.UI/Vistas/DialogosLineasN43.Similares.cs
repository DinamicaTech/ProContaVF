using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.UI.Vistas;

public sealed partial class DialogosLineasN43
{
    /// <summary>
    /// «Movimientos similares» (norma43/movimientos-similares): el concepto de la línea y debajo los movimientos parecidos, del más
    /// parecido al menos, cada uno con «Asignar» (cierra y pone su cuenta), copiar la cuenta, ver su asiento (también con doble clic) e ir a
    /// la línea (cierra y la selecciona en la lista).
    /// </summary>
    public EleccionSimilarN43? MovimientosSimilares(MovimientosSimilaresN43VM vm)
    {
        var d = CrearMovimientosSimilares(vm, Activa());
        d.ShowDialog();
        return vm.Eleccion;
    }

    /// <summary>Construye el diálogo «Movimientos similares» sin abrirlo (también para capturas fuera de pantalla).</summary>
    public static DialogoProConta CrearMovimientosSimilares(MovimientosSimilaresN43VM vm, Window? propietario)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var d = new DialogoProConta
        {
            Title = vm.Titulo, Owner = propietario, Width = 1240, Height = 680, MinWidth = 820, MinHeight = 360,
            SizeToContent = SizeToContent.Manual, Redimensionable = true, DataContext = vm,
        };
        vm.Cerrar += () => d.DialogResult = true;
        Style S(string k) => (Style)d.FindResource(k);
        var apagado = (Brush)d.FindResource("Pincel.Apagado");
        var linea = (Brush)d.FindResource("Pincel.Linea");
        var verde = (Brush)d.FindResource("Pincel.Verde");
        var rojo = (Brush)d.FindResource("Pincel.RojoError");
        var mono = (FontFamily)d.FindResource("Fuente.Mono");
        // %, fecha, concepto, importe, cuenta, descripción, asiento, botones.
        double[] anchos = [64, 88, double.NaN, 100, 84, 210, 60, 330];

        Grid Fila()
        {
            var g = new Grid();
            foreach (var a in anchos)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = double.IsNaN(a) ? new GridLength(1, GridUnitType.Star) : new GridLength(a) });
            return g;
        }
        TextBlock Celda(Grid g, int col, string texto, bool numero = false, bool fuenteMono = false, Brush? color = null, string? ayuda = null)
        {
            var t = new TextBlock
            {
                Text = texto, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
                TextAlignment = numero ? TextAlignment.Right : TextAlignment.Left, ToolTip = ayuda,
            };
            if (fuenteMono) t.FontFamily = mono;
            if (color is not null) t.Foreground = color;
            Grid.SetColumn(t, col);
            g.Children.Add(t);
            return t;
        }
        Button Boton(string texto, ICommand comando, string ayuda, bool primario = false)
        {
            var b = new Button
            {
                Content = texto, Command = comando, ToolTip = ayuda, Style = S(primario ? "Boton.Primario" : "Boton.Pequeno"), Margin = new Thickness(0, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(8, 2, 8, 2),
            };
            ToolTipService.SetShowOnDisabled(b, true);
            return b;
        }

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Concepto de la línea", Style = S("Texto.Seccion") });
        panel.Children.Add(new TextBox
        {
            Text = vm.Concepto, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Style = S("Campo"), Margin = new Thickness(0, 4, 0, 6),
            ToolTip = "Concepto del banco: puedes seleccionarlo y copiarlo",
        });
        panel.Children.Add(new TextBlock { Text = vm.Explicacion, Foreground = apagado, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(new TextBlock
        {
            Text = vm.Alcance, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10),
            Foreground = vm.MotivoAsignar is null ? (Brush)d.FindResource("Pincel.Tinta") : rojo,
        });

        if (!vm.HayResultados)
            panel.Children.Add(new TextBlock { Text = vm.TextoSinResultados, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) });
        else
        {
            var titulos = Fila();
            titulos.Margin = new Thickness(6, 0, 0, 2);
            string[] nombres = ["Parecido", "Fecha", "Concepto", "Importe", "Cuenta", "Descripción de la cuenta", "Asiento", ""];
            for (var i = 0; i < nombres.Length; i++)
                Celda(titulos, i, nombres[i], numero: i is 3 or 6, color: apagado).FontSize = (double)d.FindResource("Letra.Pequena");
            panel.Children.Add(new Border { BorderBrush = linea, BorderThickness = new Thickness(0, 0, 0, 1), Child = titulos, Padding = new Thickness(0, 0, 0, 4) });
            foreach (var r in vm.Resultados)
            {
                var g = Fila();
                Celda(g, 0, r.Porcentaje, fuenteMono: true, color: r.Destacado ? verde : apagado, ayuda: "Palabras del concepto en común con la línea").FontWeight =
                    r.Destacado ? FontWeights.SemiBold : FontWeights.Normal;
                Celda(g, 1, r.Fecha, fuenteMono: true);
                Celda(g, 2, r.Concepto, ayuda: r.Ayuda);
                Celda(g, 3, r.Importe, numero: true, fuenteMono: true, color: r.EsCargo ? rojo : null);
                Celda(g, 4, r.Cuenta, fuenteMono: true);
                Celda(g, 5, r.NombreCuenta, ayuda: r.NombreCuenta);
                Celda(g, 6, r.Asiento, numero: true, fuenteMono: true);
                var botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                botones.Children.Add(Boton("Asignar", r.AsignarCommand, r.AyudaAsignar, primario: true));
                var copiar = new Button
                {
                    Content = "Copiar", ToolTip = r.AyudaCopiar, Style = S("Boton.Pequeno"), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                    Padding = new Thickness(8, 2, 8, 2),
                };
                copiar.Click += (_, _) =>
                {
                    try { Clipboard.SetText(r.Cuenta); copiar.Content = "Copiada"; }
                    catch (System.Runtime.InteropServices.ExternalException) { copiar.Content = "Reintenta"; }
                };
                botones.Children.Add(copiar);
                botones.Children.Add(Boton("Asiento", r.VerAsientoCommand, r.AyudaVerAsiento));
                botones.Children.Add(Boton("Ir a la línea", r.IrCommand, r.AyudaIr));
                Grid.SetColumn(botones, 7);
                g.Children.Add(botones);
                var fila = new Border
                {
                    Child = g, Padding = new Thickness(6, 3, 0, 3), BorderBrush = linea, BorderThickness = new Thickness(0, 0, 0, 1), Background = Brushes.Transparent,
                };
                fila.MouseLeftButtonDown += (_, e) =>
                {
                    if (e.ClickCount == 2 && r.VerAsientoCommand.CanExecute(null)) { r.VerAsientoCommand.Execute(null); e.Handled = true; }
                };
                panel.Children.Add(fila);
            }
        }
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        var resumen = new TextBlock { Text = vm.Resumen, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0), Foreground = apagado };
        var cerrar = new Button { Content = "Cerrar", IsCancel = true, IsDefault = true, ToolTip = "Cierra sin cambiar nada" };
        cerrar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { resumen, cerrar } };
        return d;
    }
}
