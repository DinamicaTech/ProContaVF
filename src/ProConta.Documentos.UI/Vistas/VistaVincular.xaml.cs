using System.Windows.Controls;

namespace ProConta.Documentos.UI.Vistas;

/// <summary>Contenido del diálogo «Vincular a…»: el buscador recibe el foco al abrirse.</summary>
public partial class VistaVincular : UserControl
{
    public VistaVincular()
    {
        InitializeComponent();
        Loaded += (_, _) => Buscar.Focus();
    }
}
