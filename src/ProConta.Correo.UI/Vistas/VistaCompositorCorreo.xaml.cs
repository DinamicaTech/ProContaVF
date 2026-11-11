using System.Windows.Controls;

namespace ProConta.Correo.UI.Vistas;

/// <summary>Contenido del compositor de correo (maqueta sources/maquetas/Correo).</summary>
public partial class VistaCompositorCorreo : UserControl
{
    public VistaCompositorCorreo() => InitializeComponent();

    /// <summary>Lleva el foco al campo Para.</summary>
    public void Enfocar() => CampoPara.Focus();
}
