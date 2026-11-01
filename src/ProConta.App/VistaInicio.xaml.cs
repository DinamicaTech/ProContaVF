using System.Windows;
using System.Windows.Controls;
using ProConta.Seguridad.Acceso;

namespace ProConta.App;

/// <summary>Contenido de la pestaña Inicio: saludo, contexto de trabajo y lo que trae la versión.</summary>
public partial class VistaInicio : UserControl
{
    public VistaInicio(ContextoAcceso contexto)
    {
        InitializeComponent();
        var nombre = contexto.Sesion.NombreOperador;
        TextoSaludo.Text = $"Hola, {nombre.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()}";
        TextoContexto.Text = $"Trabajas en {contexto.RazonSocial} · Ejercicio {contexto.Contexto.Ejercicio} ({(contexto.EjercicioCerrado ? "cerrado" : "abierto")}).";
        AvisoCerrado.Visibility = contexto.EjercicioCerrado ? Visibility.Visible : Visibility.Collapsed;
    }
}
