using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProConta.UI.Base.Formato;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;

namespace ProConta.Contabilidad.UI.Asientos;

/// <summary>Pestaña del contenedor con la pantalla de asiento manual.</summary>
public sealed class PestanaAsientoManual : IPestanaTrabajo
{
    private readonly PantallaAsientoManual _vista;
    private bool _guardadoFallido;

    internal PestanaAsientoManual(AsientoManualVM vm)
    {
        Modelo = vm;
        _vista = new PantallaAsientoManual { DataContext = vm };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(AsientoManualVM.TieneCambios) or nameof(AsientoManualVM.Titulo))
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TieneCambios)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Titulo)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ayuda)));
            }
        };
    }

    public AsientoManualVM Modelo { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Titulo => Modelo.Titulo;
    public string Ayuda => Modelo.TieneCambios ? $"{Modelo.Titulo}: cambios sin guardar" : $"{Modelo.Titulo}: {Modelo.Subtitulo}";
    public string Icono => "edit";
    public bool TieneCambios => Modelo.TieneCambios;
    public bool GuardadoFallido => _guardadoFallido;
    public object Vista => _vista;
    public IReadOnlyList<string> DescribirCambios() => Modelo.DescribirCambios();

    public async Task<bool> GuardarAsync()
    {
        var ok = await Modelo.GuardarAsync();
        _guardadoFallido = !ok;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GuardadoFallido)));
        return ok;
    }

    public void Descartar() => Modelo.Descartar();
    public void AlCerrar() { }
}

/// <summary>Diálogos de la pantalla de asiento manual: el buscador común de cuentas (con «Crear subcuenta») y los avisos de error.</summary>
public sealed class DialogosAsiento(ICatalogoCuentas cuentas, ICatalogoCentrosCoste centros, ContextoSociedadEjercicio contexto, Func<Window?> propietario) : IDialogosAsiento
{
    public (string Codigo, string Nombre)? ElegirCuenta(string? texto, IReadOnlyList<string> grupos, string titulo)
    {
        var vm = new BuscadorCuentasVM(cuentas, contexto, grupos, texto, centros);
        return DialogoCuentas.Elegir(propietario(), vm, titulo) is { } c ? (c.Codigo, c.Nombre) : null;
    }

    public void Error(string titulo, string mensaje) =>
        ServicioMensajes.Preguntar(propietario(), titulo, mensaje, null, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));

    public decimal? PedirImporte(string titulo, string explicacion)
    {
        var d = new DialogoProConta { Title = titulo, Owner = propietario(), Width = 560, SizeToContent = SizeToContent.Height };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = explicacion, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var valor = new TextBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Style = (Style)d.FindResource("Campo.Importe"),
            ToolTip = "Importe en euros: escribe solo el número (la coma es el decimal), por ejemplo 1.210,00" };
        panel.Children.Add(valor);
        var aviso = new TextBlock { Foreground = (Brush)d.FindResource("Pincel.RojoError"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed };
        panel.Children.Add(aviso);
        d.Content = panel;
        decimal leido = 0m;
        var aceptar = new Button { Content = "Aceptar", Style = (Style)d.FindResource("Boton.Primario"), IsDefault = true, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Usa este importe" };
        var cancelar = new Button { Content = "Cancelar", IsCancel = true, ToolTip = "Vuelve sin rellenar la rejilla" };
        aceptar.Click += (_, _) =>
        {
            if (!Formatos.LeerImporte(valor.Text, out leido) || leido == 0m)
            {
                aviso.Text = "Escribe un importe distinto de 0, por ejemplo 1210,00.";
                aviso.Visibility = Visibility.Visible;
                valor.Focus();
                return;
            }
            d.DialogResult = true;
        };
        cancelar.Click += (_, _) => d.DialogResult = false;
        d.Botones = new StackPanel { Orientation = Orientation.Horizontal, Children = { cancelar, aceptar } };
        d.Loaded += (_, _) => valor.Focus();
        return d.ShowDialog() == true ? leido : null;
    }
}

/// <summary>
/// La pantalla de asiento manual dentro del contenedor (contabilidad/pantallas, PR 2 de contabilidad/plantillas-asiento): abre un asiento
/// nuevo (desde el menú o desde el Mayor con la cuenta y el importe de la selección), el asistente de corrección de un apunte o un asiento
/// para modificarlo, cada uno en su pestaña. Al guardar cierra la pestaña, avisa en la barra de estado y llama a <see cref="Guardado"/>.
/// </summary>
public sealed class ModuloAsientos(Func<IFuenteAsientoManual> fuente, IAnfitrionPestanas anfitrion, IDialogosAsiento dialogos)
{
    /// <summary>Después de guardar (para refrescar el Mayor).</summary>
    public event Action<IReadOnlyList<int>>? Guardado;

    public Task AbrirNuevoAsync(string? cuenta = null, decimal? importe = null) =>
        AbrirAsync(async vm => { await vm.IniciarNuevoAsync(cuenta, importe); return ProConta.Nucleo.Resultados.Resultado.Correcto(); });

    /// <summary>Asistente de corrección: con un apunte (y su cuenta), o solo con una subcuenta si la selección del Mayor es de varias filas de una cuenta.</summary>
    public Task AbrirCorreccionAsync(int? idApunte, string? cuenta) => AbrirAsync(vm => vm.IniciarCorreccionAsync(idApunte, cuenta));

    public Task AbrirModificarAsync(int idAsiento)
    {
        if (anfitrion.Pestanas.OfType<PestanaAsientoManual>().FirstOrDefault(p => p.Modelo.Modo == ModoAsientoManual.Modificar && p.Modelo.Titulo == $"Asiento {idAsiento}") is { } abierta)
        {
            anfitrion.Activar(abierta);
            return Task.CompletedTask;
        }
        return AbrirAsync(vm => vm.IniciarModificarAsync(idAsiento));
    }

    private async Task AbrirAsync(Func<AsientoManualVM, Task<ProConta.Nucleo.Resultados.Resultado>> iniciar)
    {
        var vm = new AsientoManualVM(fuente(), dialogos);
        var r = await iniciar(vm);
        if (!r.EsCorrecto)
        {
            dialogos.Error("No puedo abrir el asiento", string.Join(" ", r.Errores.Select(e => e.TextoOperador())));
            return;
        }
        var pestana = new PestanaAsientoManual(vm);
        vm.Guardado += ids =>
        {
            anfitrion.Cerrar(pestana);
            anfitrion.Avisar(ids.Count == 1 ? $"Asiento {ids[0]} guardado" : $"Asientos {string.Join(", ", ids)} guardados");
            Guardado?.Invoke(ids);
        };
        vm.Cancelado += () =>
        {
            vm.Descartar();
            anfitrion.Cerrar(pestana);
        };
        anfitrion.Abrir(pestana);
    }
}
