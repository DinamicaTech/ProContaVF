using System.IO;
using System.Windows;
using System.Windows.Controls;
using ProConta.Plataforma.Configuracion;
using ProConta.UI.Base.Controles;

namespace ProConta.App;

/// <summary>
/// Configuración › «Actualizar la copia de pruebas…» (plataforma/entorno-pruebas): solo en producción. Pregunta la carpeta de pruebas
/// y la dirección del desvío de correo, enseña lo que va a hacer, y hace el refresco en segundo plano sin tocar producción.
/// </summary>
public partial class VentanaPrincipal
{
    private readonly InfoEntorno _entorno;
    private bool _refrescando;

    private async Task ActualizarCopiaPruebasAsync()
    {
        if (_entorno.EsPruebas || _refrescando) return;
        var almacen = new AlmacenConfiguracionLocalJson();
        EntornoPruebasLocal? registrada = null;
        try { registrada = almacen.Leer()?.Pruebas; } catch (Exception ex) when (ex is InvalidDataException or IOException) { }
        var mismaProduccion = registrada is not null && string.Equals(Path.GetFullPath(registrada.RutaProduccion), Path.GetFullPath(_rutaBd), StringComparison.OrdinalIgnoreCase);
        var carpetaPropuesta = mismaProduccion ? Path.GetDirectoryName(registrada!.RutaBaseDatos)! : RefrescoPruebas.CarpetaPruebasPorDefecto(_rutaBd);

        if (PedirDatos(carpetaPropuesta, registrada?.DesviarCorreoA) is not { } datos) return;
        var (carpeta, desvio) = datos;

        var refresco = new RefrescoPruebas();
        var plan = await refresco.PlanificarAsync(_rutaBd, carpeta);
        if (!plan.EsCorrecto)
        {
            InformarError("No puedo actualizar la copia de pruebas", plan.Error!);
            return;
        }
        var p = plan.Valor;
        var detalles = new List<string> { $"Copia de pruebas: {p.BdPruebas}" };
        detalles.AddRange(p.Carpetas.Select(c => $"Carpeta {c.Origen} → {c.Destino}"));
        detalles.Add($"Todo el correo de la copia saldrá solo a {desvio}; sin recepción, salvo la cuenta de pruebas que tuviera la copia anterior.");
        detalles.Add("VeriFactu desactivado; se conservan el token de Avaibook y las credenciales de Textract.");
        if (p.HayCopiaAnterior) detalles.Add($"La copia anterior se guarda en {Path.Combine(p.CarpetaPruebas, RefrescoPruebas.CarpetaCopiasPruebas)}, y lo que hayas cambiado en pruebas se pierde.");
        detalles.Add("En producción no se cambia nada.");
        var ok = ServicioMensajes.Preguntar(this, "Actualizar la copia de pruebas",
            "Voy a sustituir la copia de pruebas por una imagen de producción tal como está ahora. Para que la copia salga coherente, no grabes nada en producción mientras tanto.",
            detalles,
            new OpcionMensaje("Cancelar", "No cambia nada", Cancelar: true),
            new OpcionMensaje("Actualizar la copia", "Copia la base de datos y las carpetas de producción a la carpeta de pruebas y prepara la copia", Principal: true));
        if (ok != 1) return;

        _refrescando = true;
        Avisar("Actualizando la copia de pruebas…");
        var progreso = new Progress<string>(Avisar);
        try
        {
            var r = await Task.Run(() => refresco.EjecutarAsync(p, desvio, progreso));
            if (!r.EsCorrecto)
            {
                InformarError("No he podido actualizar la copia de pruebas", r.Error!);
                return;
            }
            var informe = r.Valor;
            var registro = SelectorEntorno.Registrar(almacen, new EntornoPruebasLocal(informe.BdPruebas, _rutaBd, desvio));
            var resumen = new List<string>
            {
                $"Ficheros copiados: {informe.FicherosCopiados}; borrados de pruebas porque ya no están en producción: {informe.FicherosBorrados}.",
                $"Rutas cambiadas a las carpetas de pruebas: {informe.RutasCambiadas}.",
                informe.CuentasPruebasConservadas.Count > 0
                    ? $"Cuenta de correo de pruebas conservada: {string.Join(", ", informe.CuentasPruebasConservadas)}."
                    : "No había cuenta de correo de pruebas: la recepción queda apagada. Puedes dar de alta la de pruebas en la copia y se conservará en los próximos refrescos.",
            };
            if (informe.MensajesCancelados > 0) resumen.Add($"Envíos pendientes de producción cancelados en la copia: {informe.MensajesCancelados}.");
            if (!registro.EsCorrecto) resumen.Add(App.Explicar(registro.Error!));
            Informar("Copia de pruebas actualizada",
                "La copia de pruebas ya es una imagen de producción. La próxima vez que abras ProContaVF podrás elegir el entorno de pruebas antes de entrar.", resumen);
            Avisar("Copia de pruebas actualizada.");
        }
        finally
        {
            _refrescando = false;
        }
    }

    /// <summary>Carpeta de pruebas y dirección del desvío; null si cancela.</summary>
    private (string Carpeta, string Desvio)? PedirDatos(string carpeta, string? desvio)
    {
        var cajaCarpeta = new TextBox { Text = carpeta, Margin = new Thickness(0, 4, 0, 12), ToolTip = "Carpeta donde se guarda la copia de pruebas: la BD y la réplica de las carpetas de producción" };
        var cajaDesvio = new TextBox { Text = desvio ?? "", Width = 320, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = "En pruebas, todo el correo sale solo a esta dirección, para que no le llegue nada a clientes ni a la gestoría" };
        var panel = new StackPanel { Margin = new Thickness(20, 12, 20, 12), Width = 560 };
        panel.Children.Add(new TextBlock { Text = "Se copia la base de datos de producción y sus carpetas (documentos, nóminas, ficheros N43…) a esta carpeta, sustituyendo la copia anterior.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBlock { Text = "Carpeta de pruebas" });
        panel.Children.Add(cajaCarpeta);
        panel.Children.Add(new TextBlock { Text = "Desviar todo el correo de pruebas a" });
        panel.Children.Add(cajaDesvio);
        var aceptar = new Button { Content = "Continuar", IsDefault = true, MinWidth = 100, Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("Boton.Primario"), ToolTip = "Comprueba los datos y enseña lo que se va a hacer, sin cambiar nada todavía" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, MinWidth = 90, ToolTip = "No cambia nada" };
        var dialogo = new DialogoProConta { Title = "Actualizar la copia de pruebas", Owner = this, SizeToContent = SizeToContent.WidthAndHeight, Content = panel,
            Botones = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { cancelar, aceptar } } };
        aceptar.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(cajaCarpeta.Text) || !cajaDesvio.Text.Contains('@'))
            {
                ServicioMensajes.Preguntar(dialogo, "Faltan datos", "Indica la carpeta de pruebas y una dirección de correo válida para el desvío.", null,
                    new OpcionMensaje("Entendido", "Vuelve al formulario", Principal: true, Cancelar: true));
                return;
            }
            dialogo.DialogResult = true;
        };
        cancelar.Click += (_, _) => dialogo.DialogResult = false;
        return dialogo.ShowDialog() == true ? (cajaCarpeta.Text.Trim(), cajaDesvio.Text.Trim()) : null;
    }

    private void InformarError(string titulo, ProConta.Nucleo.Resultados.Error error) =>
        Informar(titulo, error.Mensaje, new[] { error.RegistroAfectado is null ? null : "Afecta a: " + error.RegistroAfectado,
            error.Causa is null ? null : "Causa: " + error.Causa, error.AccionCorrectora is null ? null : "Qué hacer: " + error.AccionCorrectora }.OfType<string>().ToList());
}
