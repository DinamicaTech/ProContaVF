using System.Windows;
using System.Windows.Controls;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Organizacion.Sociedades;
using ProConta.Seguridad.Acceso;
using ProConta.UI.Base.Controles;

namespace ProConta.App;

/// <summary>
/// «Cambiar de ejercicio» (plataforma/contenedor 2026-11-28): la sociedad y el ejercicio de trabajo, con las mismas listas que la
/// pantalla de acceso, sin volver a pedir operador ni contraseña. Propone la sociedad y el ejercicio actuales.
/// </summary>
internal static class DialogoCambioEjercicio
{
    private sealed record Opcion<T>(T Valor, string Texto)
    {
        public override string ToString() => Texto;
    }

    /// <summary>Sociedad y ejercicio elegidos, o null si cancela o no hay nada que elegir (ya se ha avisado).</summary>
    public static async Task<(string IdSociedad, int IdSociedadEjercicio)?> ElegirAsync(Window propietario, ServicioAcceso acceso, SesionAcceso sesion,
        ContextoSociedadEjercicio actual)
    {
        var sociedades = await acceso.ListarSociedadesAsync(sesion);
        if (!sociedades.EsCorrecto) { Avisar(propietario, sociedades.Error!); return null; }
        if (sociedades.Valor.Sociedades.Count == 0)
        {
            Avisar(propietario, new Error("Acceso.SinSociedades", "No tienes acceso a ninguna sociedad.", null,
                "Tu operador no está en ningún grupo con acceso a sociedades.", "Pide a un administrador que te dé acceso."));
            return null;
        }

        var comboSociedad = new ComboBox { Width = 340, Margin = new Thickness(0, 4, 0, 12), DisplayMemberPath = "Texto",
            ToolTip = "Sociedades a las que tienes acceso" };
        var comboEjercicio = new ComboBox { Width = 140, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4), DisplayMemberPath = "Texto",
            ToolTip = "Ejercicio de trabajo; un ejercicio cerrado se abre solo para consultar" };
        var resumen = new TextBlock { Foreground = (System.Windows.Media.Brush)Application.Current.FindResource("Pincel.Texto2"), Margin = new Thickness(0, 0, 0, 4) };
        var panel = new StackPanel { Margin = new Thickness(20, 12, 20, 12), Width = 380 };
        panel.Children.Add(new TextBlock { Text = "Se cierran las pestañas abiertas y se abre Inicio del ejercicio que elijas.", TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Sociedad" });
        panel.Children.Add(comboSociedad);
        panel.Children.Add(new TextBlock { Text = "Ejercicio" });
        panel.Children.Add(comboEjercicio);
        panel.Children.Add(resumen);

        var aceptar = new Button { Content = "Cambiar", IsDefault = true, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0),
            Style = (Style)Application.Current.FindResource("Boton.Primario"),
            ToolTip = "Cierra las pestañas abiertas (pregunta antes si alguna tiene cambios) y abre el ejercicio elegido" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, ToolTip = "Sigue en el ejercicio actual sin cambiar nada" };
        var dialogo = new DialogoProConta { Title = "Cambiar de ejercicio", Owner = propietario, SizeToContent = SizeToContent.WidthAndHeight, Content = panel,
            Botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancelar, aceptar } } };

        void Resumir()
        {
            resumen.Text = comboEjercicio.SelectedItem is Opcion<SociedadEjercicio> o
                ? o.Valor.IdSociedadEjercicio == actual.IdSociedadEjercicio ? "Es el ejercicio en el que ya estás."
                : o.Valor.Cerrado ? "Ejercicio cerrado: solo consulta." : "Ejercicio abierto."
                : "";
            aceptar.IsEnabled = comboEjercicio.SelectedItem is Opcion<SociedadEjercicio> e && e.Valor.IdSociedadEjercicio != actual.IdSociedadEjercicio;
        }

        async Task CargarEjerciciosAsync()
        {
            comboEjercicio.ItemsSource = null;
            if (comboSociedad.SelectedItem is not Opcion<Sociedad> s) { Resumir(); return; }
            var r = await acceso.ListarEjerciciosAsync(sesion, s.Valor.IdSociedad);
            if (!r.EsCorrecto) { Avisar(dialogo, r.Error!); Resumir(); return; }
            if (r.Valor.Ejercicios.Count == 0)
            {
                Avisar(dialogo, new Error("Acceso.SinEjercicios", "Esta sociedad no tiene ejercicios.", s.Valor.RazonSocial,
                    "No hay ningún ejercicio dado de alta para ella.", "Elige otra sociedad o crea su ejercicio en Parámetros y tablas › Ejercicios."));
                Resumir();
                return;
            }
            var opciones = r.Valor.Ejercicios.Select(x => new Opcion<SociedadEjercicio>(x, x.Cerrado ? $"{x.Ejercicio} · cerrado" : x.Ejercicio.ToString())).ToList();
            comboEjercicio.ItemsSource = opciones;
            // En la sociedad actual se propone el ejercicio actual; en otra, el último en el que entró el operador.
            comboEjercicio.SelectedItem = opciones.FirstOrDefault(o => o.Valor.IdSociedadEjercicio == actual.IdSociedadEjercicio)
                ?? opciones.FirstOrDefault(o => o.Valor.IdSociedadEjercicio == r.Valor.IdPropuesto);
            Resumir();
        }

        var lista = sociedades.Valor.Sociedades.Select(s => new Opcion<Sociedad>(s, $"{s.RazonSocial}  ({s.IdSociedad})")).ToList();
        comboSociedad.ItemsSource = lista;
        comboSociedad.SelectedItem = lista.FirstOrDefault(o => string.Equals(o.Valor.IdSociedad, actual.IdSociedad, StringComparison.OrdinalIgnoreCase));
        await CargarEjerciciosAsync();
        comboSociedad.SelectionChanged += async (_, _) => await CargarEjerciciosAsync();
        comboEjercicio.SelectionChanged += (_, _) => Resumir();
        aceptar.Click += (_, _) => dialogo.DialogResult = true;
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        dialogo.Loaded += (_, _) => comboEjercicio.Focus();

        if (dialogo.ShowDialog() != true || comboSociedad.SelectedItem is not Opcion<Sociedad> sociedad || comboEjercicio.SelectedItem is not Opcion<SociedadEjercicio> ejercicio)
            return null;
        return (sociedad.Valor.IdSociedad, ejercicio.Valor.IdSociedadEjercicio);
    }

    private static void Avisar(Window propietario, Error error) =>
        ServicioMensajes.Preguntar(propietario, error.Mensaje, App.Explicar(error), null,
            new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
}
