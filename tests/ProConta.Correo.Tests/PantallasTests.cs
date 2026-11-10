using System.IO;
using System.Windows;
using ProConta.Correo.UI.VistaModelos;
using ProConta.Correo.UI.Vistas;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Controles;

namespace ProConta.Correo.Tests;

/// <summary>Diálogos que no abren ventanas: anotan lo que se pide.</summary>
internal sealed class DialogosFalsos : IDialogosCorreo
{
    public int Respuesta { get; set; }
    public List<string> Preguntas { get; } = [];
    public int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones) { Preguntas.Add(titulo); return Respuesta; }
    public IReadOnlyList<string> ElegirFicheros() => [];
    public void AbrirArchivo(string ruta) { }
    public string? PedirTexto(string titulo, string explicacion, string etiqueta, string textoBoton) => "motivo";
}

/// <summary>
/// Compositor sin ventanas (validación junto a cada campo, cancelar no envía) y humo de las vistas: se construyen y miden
/// con los recursos de ProConta.UI.Base, así que un recurso que falte en el XAML falla aquí y no al abrirlas.
/// </summary>
public sealed class PantallasTests
{
    private static CompositorCorreoVM Compositor(MensajeNuevo m) =>
        new(new ServicioCorreo(new BdNula(), new TransporteProgramado()), new DialogosFalsos(), new Actor("Prueba"), m, "Demo <demo@example.invalid>", "Demo · 2026", null, null);

    [Fact]
    public async Task El_compositor_marca_los_errores_junto_a_cada_campo_y_no_envia()
    {
        var vm = Compositor(new MensajeNuevo { IdSociedadEjercicio = 1, Asunto = "Factura {NumeroFactura}" });
        vm.Para = "cliente@example";
        vm.MostrarCc = true;
        vm.Cc = "otro@example.invalid";
        await vm.EnviarAsync();
        Assert.NotNull(vm.ErrorPara);
        Assert.Null(vm.ErrorCc);
        Assert.NotNull(vm.AvisoPlantilla);
        Assert.NotNull(vm.ResumenErrores);
        Assert.Equal(FaseCompositor.Redactando, vm.Fase);
        Assert.Null(vm.IdMensaje);
    }

    [Fact]
    public void El_compositor_construye_el_mensaje_con_cc_cco_y_html()
    {
        var vm = Compositor(new MensajeNuevo { IdSociedadEjercicio = 1, Asunto = "Hola", CuerpoHtml = "<p>Uno</p><p>Dos</p>",
            Destinatarios = [new(TipoDestinatario.Para, "a@example.invalid", "Cliente A")] });
        Assert.Equal("a@example.invalid", vm.Para);
        Assert.Equal("Uno\n\nDos", vm.Cuerpo.Replace("\r\n", "\n"));
        vm.MostrarCco = true;
        vm.Cco = "oculto@example.invalid";
        var m = vm.Construir();
        Assert.Equal("Cliente A", m.Destinatarios.Single(d => d.Tipo == TipoDestinatario.Para).Nombre);
        Assert.Single(m.Destinatarios, d => d.Tipo == TipoDestinatario.Cco);
        Assert.Equal("<p>Uno</p><p>Dos</p>", m.CuerpoHtml);
        Assert.True(vm.TieneCambios);
        Assert.False(vm.PuedeCerrarSinPreguntar);
    }

    [Fact]
    public void Las_vistas_se_construyen_con_los_recursos_de_la_aplicacion() => EnSta(() =>
    {
        if (Application.Current is null) _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var recursos = new ResourceDictionary { Source = new Uri("pack://application:,,,/ProConta.UI.Base;component/Themes/ProConta.xaml", UriKind.Absolute) };
        Application.Current!.Resources.MergedDictionaries.Add(recursos);
        var compositor = Compositor(new MensajeNuevo { IdSociedadEjercicio = 1, Asunto = "Hola", Adjuntos = [new AdjuntoNuevo("a.pdf", [1, 2, 3])] });
        var vista = new VistaCompositorCorreo { DataContext = compositor };
        vista.Measure(new Size(800, 600));
        var ventana = new VentanaCompositorCorreo(compositor, new DialogosFalsos());
        Assert.NotNull(ventana.Botones);
        var contexto = new ContextoCorreo(new ContextoSociedadEjercicio("C01", 1, 2026), "Demo", new Actor("Prueba"), _ => { });
        var registro = new RegistroCorreoVM(new ServicioCorreo(new BdNula(), new TransporteProgramado()), new DialogosFalsos(), contexto, () => Task.CompletedTask,
            new FiltroRegistro(IdLote: 1), "Lote 1", ["F-3: sin email"]);
        var vistaRegistro = new VistaRegistroCorreo { DataContext = registro };
        vistaRegistro.Measure(new Size(1200, 700));
        _ = new VentanaResumenCorreo(registro);
    });

    private static void EnSta(Action accion)
    {
        Exception? error = null;
        var hilo = new Thread(() => { try { accion(); } catch (Exception ex) { error = ex; } });
        hilo.SetApartmentState(ApartmentState.STA);
        hilo.Start();
        hilo.Join();
        if (error is not null) throw new Xunit.Sdk.XunitException(error.ToString());
    }
}

/// <summary>BD que no debe usarse: las pruebas de pantallas no llegan a la cola.</summary>
internal sealed class BdNula : ProConta.Datos.Persistencia.IBaseDatos
{
    public string Ruta => "nula";
    public Task<T> EjecutarAsync<T>(Func<ProConta.Datos.Persistencia.ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) => throw new InvalidOperationException("Sin BD en esta prueba.");
    public Task<T> EnTransaccionAsync<T>(Func<ProConta.Datos.Persistencia.ISesionDatos, Task<T>> trabajo, CancellationToken cancelacion = default) => throw new InvalidOperationException("Sin BD en esta prueba.");
}
