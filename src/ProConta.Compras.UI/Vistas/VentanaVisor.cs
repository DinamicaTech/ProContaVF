using System.Windows;
using System.Windows.Data;

namespace ProConta.Compras.UI.Vistas;

/// <summary>Visor del PDF desacoplado (para llevarlo a la segunda pantalla); el PDF se ajusta al tamaño de la ventana.</summary>
public sealed class VentanaVisor : Window
{
    public VentanaVisor()
    {
        Title = "Visor del PDF · ProContaVF";
        Width = 900;
        Height = 1000;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - Width - 40;
        Top = SystemParameters.WorkArea.Top + 40;
        ShowInTaskbar = true;
        Visor = new VisorPdf();
        Visor.SetBinding(VisorPdf.RutaProperty, new Binding("RutaPdf"));
        SetBinding(TitleProperty, new Binding("TituloVisor") { StringFormat = "{0} · Visor del PDF · ProContaVF" });
        Content = Visor;
        if (TryFindResource("Ventana.ProConta") is Style estilo) Style = estilo;
    }

    public VisorPdf Visor { get; }
}
