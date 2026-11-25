using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.UI.Vistas;

/// <summary>Diálogo «Conciliar plataformas» de Líneas N43 (norma43/conciliacion/plataformas).</summary>
public sealed partial class DialogosLineasN43
{
    /// <summary>«Conciliar plataformas»: cruces marcados con un clic, lo que no cruza y las reservas sin cobro; «Conciliar» enlaza los marcados.</summary>
    public bool ConciliarPlataformas(CobrosPlataformasN43VM vm) => CrearConciliarPlataformas(vm, Activa()).ShowDialog() == true;

    /// <summary>Construye el diálogo sin abrirlo (también para capturas fuera de pantalla).</summary>
    public static DialogoProConta CrearConciliarPlataformas(CobrosPlataformasN43VM vm, Window? propietario)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var d = Dialogo("Conciliar plataformas", propietario, vm);
        d.Width = 1100;
        d.Height = 680;
        var (panel, apagado) = Cabecera(d, vm.Cabecera, vm.Periodo, vm.Criterios);
        var linea = (Brush)d.FindResource("Pincel.Linea");
        var mono = (FontFamily)d.FindResource("Fuente.Mono");

        void Seccion(string titulo) => panel.Children.Add(new Border
        {
            BorderBrush = linea, BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0, 14, 0, 4), Padding = new Thickness(0, 0, 0, 4),
            Child = new TextBlock { Text = titulo, Style = (Style)d.FindResource("Texto.Seccion") },
        });
        StackPanel Lista(IEnumerable<string> textos)
        {
            var p = new StackPanel();
            foreach (var t in textos)
                p.Children.Add(new TextBlock { Text = t, FontFamily = mono, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = t });
            return p;
        }

        if (vm.Cruces.Count > 0)
        {
            Seccion($"Cruces ({vm.Cruces.Count})");
            var titulos = Rejilla([22, 420, double.NaN]);
            Celda(d, titulos, 1, "Cobros del banco", color: apagado).FontSize = (double)d.FindResource("Letra.Pequena");
            Celda(d, titulos, 2, "Facturas de las reservas", color: apagado).FontSize = (double)d.FindResource("Letra.Pequena");
            panel.Children.Add(titulos);
            foreach (var c in vm.Cruces)
            {
                var contenido = new StackPanel();
                contenido.Children.Add(new TextBlock { Text = c.Titulo, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 2) });
                var cuerpo = Rejilla([420, double.NaN]);
                var cobros = Lista(c.Cobros);
                var facturas = Lista(c.Facturas);
                Grid.SetColumn(facturas, 1);
                cuerpo.Children.Add(cobros);
                cuerpo.Children.Add(facturas);
                contenido.Children.Add(cuerpo);
                // La fila entera es la casilla: un clic la marca o desmarca.
                var casilla = new CheckBox
                {
                    Content = contenido, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top,
                    Padding = new Thickness(6, 3, 0, 6), ToolTip = c.Ayuda, DataContext = c,
                };
                casilla.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(CrucePlataformaN43VM.Marcado)) { Mode = BindingMode.TwoWay });
                contenido.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(FrameworkElement.ActualWidth)) { Source = panel, Converter = new Restar(30) });
                panel.Children.Add(casilla);
            }
        }
        if (vm.SinCruzar.Count > 0)
        {
            Seccion($"Cobros que no cruzan ({vm.SinCruzar.Count}): concílialos a mano");
            foreach (var s in vm.SinCruzar)
            {
                var p = new StackPanel { Margin = new Thickness(0, 2, 0, 4) };
                p.Children.Add(new TextBlock { Text = s.Texto, FontFamily = mono });
                p.Children.Add(new TextBlock { Text = s.Detalle, Foreground = apagado, TextWrapping = TextWrapping.Wrap });
                panel.Children.Add(p);
            }
        }
        if (vm.SinCobro.Count > 0)
        {
            Seccion($"Reservas facturadas sin cobro ({vm.SinCobro.Count})");
            foreach (var s in vm.SinCobro)
                panel.Children.Add(new TextBlock { Text = $"{s.Texto} · {s.Detalle}", FontFamily = mono, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = s.Detalle });
        }
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Botones(d, nameof(CobrosPlataformasN43VM.Resumen), nameof(CobrosPlataformasN43VM.HayMarcados), "Conciliar",
            "Enlaza los cobros marcados con las facturas de sus reservas (quedan cobradas) y pone la 430 de la plataforma a las líneas sin contabilizar",
            "Cierra sin conciliar nada");
        return d;
    }
}
