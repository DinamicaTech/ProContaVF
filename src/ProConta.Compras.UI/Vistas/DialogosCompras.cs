using System.Diagnostics;
using System.Windows;
using ProConta.Compras.UI.VistaModelos;
using ProConta.UI.Base.Controles;

namespace ProConta.Compras.UI.Vistas;

/// <summary>Diálogos de compras con los mensajes comunes de ProConta.UI.Base.</summary>
public sealed class DialogosCompras(Func<Window?> ventana) : IDialogosCompras
{
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) =>
        ServicioMensajes.Preguntar(ventana(), titulo, mensaje, detalles, opciones);

    public void Abrir(string ruta)
    {
        try
        {
            Process.Start(new ProcessStartInfo(ruta) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.FileNotFoundException)
        {
            ServicioMensajes.Preguntar(ventana(), "No he podido abrirlo", $"Windows no ha podido abrir {ruta}: {ex.Message}", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
        }
    }
}
