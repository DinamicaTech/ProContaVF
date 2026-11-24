using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProConta.Norma43.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Norma43.UI.Vistas;

/// <summary>Diálogo «Compra de activo» / «Venta de activo» de Líneas N43 (activos/activos-financieros).</summary>
public sealed partial class DialogosLineasN43
{
    /// <summary>«Compra de activo» / «Venta de activo»: operaciones sin conciliar, se elige una; «Conciliar» genera el asiento.</summary>
    public bool ConciliarActivo(ActivoN43VM vm) => CrearConciliarActivo(vm, Activa()).ShowDialog() == true;

    /// <summary>Construye el diálogo sin abrirlo (también para capturas fuera de pantalla).</summary>
    public static DialogoProConta CrearConciliarActivo(ActivoN43VM vm, Window? propietario)
    {
        ArgumentNullException.ThrowIfNull(vm);
        var d = Dialogo(vm.TituloDialogo, propietario, vm);
        d.Width = 940;
        d.Height = 460;
        var (panel, apagado) = Cabecera(d, vm.Titulo, vm.Concepto, vm.Explicacion);
        double[] anchos = [24, 90, double.NaN, 84, 110, 110, 70, 100];
        panel.Children.Add(Titulos(d, anchos, ["", "Activo", "Nombre", "Fecha", "Participaciones", "Importe", "", "Diferencia"], apagado, [4, 5, 7]));
        foreach (var f in vm.Filas)
        {
            var g = Rejilla(anchos);
            var elegir = new RadioButton { DataContext = f, VerticalAlignment = VerticalAlignment.Center, ToolTip = f.Ayuda };
            elegir.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, new Binding(nameof(FilaActivoN43VM.Marcada)) { Mode = BindingMode.TwoWay });
            g.Children.Add(elegir);
            Celda(d, g, 1, f.Codigo, mono: true);
            Celda(d, g, 2, f.Nombre);
            Celda(d, g, 3, f.Fecha, mono: true);
            Celda(d, g, 4, f.Participaciones, numero: true, mono: true);
            Celda(d, g, 5, f.Importe, numero: true, mono: true);
            Celda(d, g, 6, f.Origen, color: apagado);
            Celda(d, g, 7, f.Diferencia, numero: true, mono: true);
            g.ToolTip = f.Ayuda;
            g.Background = System.Windows.Media.Brushes.Transparent;
            g.MouseLeftButtonDown += (_, _) => f.Marcada = true;
            panel.Children.Add(g);
        }
        d.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        Botones(d, nameof(ActivoN43VM.Cuadre), nameof(ActivoN43VM.Cuadra), "Conciliar",
            "Genera el asiento de la línea con el importe del banco y deja la operación conciliada", "Cierra sin cambiar nada");
        return d;
    }
}
