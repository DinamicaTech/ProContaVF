using System.Windows;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Correo.UI.VistaModelos;

/// <summary>Contexto de trabajo de las pantallas de correo.</summary>
public sealed record ContextoCorreo(ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar);

/// <summary>
/// Correo dentro del contenedor (correo/pantallas): la pestaña «Correo enviado», el compositor modal y los lotes ciegos con
/// su resumen modal. Lo usan las demás áreas a través de <see cref="IEnvioCorreoInteractivo"/>; el coordinador procesa la
/// cola cada minuto mientras la aplicación está abierta.
/// </summary>
public sealed class ModuloCorreo : IEnvioCorreoInteractivo
{
    private readonly ServicioCorreo _servicio;
    private readonly CoordinadorCorreo _coordinador;
    private readonly ContextoCorreo _contexto;
    private readonly IAnfitrionPestanas _anfitrion;
    private readonly IDialogosCorreo _dialogos;
    private readonly Func<Window?> _ventana;
    private RegistroCorreoVM? _registro;

    public ModuloCorreo(ServicioCorreo servicio, CoordinadorCorreo coordinador, ContextoCorreo contexto, IAnfitrionPestanas anfitrion, IDialogosCorreo dialogos, Func<Window?> ventana)
    {
        _servicio = servicio;
        _coordinador = coordinador;
        _contexto = contexto;
        _anfitrion = anfitrion;
        _dialogos = dialogos;
        _ventana = ventana;
        _coordinador.Procesados += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => _ = _registro?.CargarAsync());
    }

    /// <summary>Abre (o activa) el origen de un mensaje: lo conectan las áreas (Factura → su editor).</summary>
    public Action<string, int>? AbrirOrigen { get; set; }

    public ServicioCorreo Servicio => _servicio;

    public void AbrirRegistro(string? tipoOrigen = null, int? idOrigen = null, string? referencia = null)
    {
        if (tipoOrigen is not null && idOrigen is int id)
        {
            // Correo de un origen concreto: resumen modal, sin tocar la pestaña general.
            MostrarModal(new FiltroRegistro(TipoOrigen: tipoOrigen, IdOrigen: id), $"Correo de {referencia ?? tipoOrigen}", []);
            return;
        }
        if (_registro is not null)
        {
            _anfitrion.Activar(_registro);
            _ = _registro.CargarAsync();
            return;
        }
        _registro = new RegistroCorreoVM(_servicio, _dialogos, _contexto, Pasada, new FiltroRegistro(IdSociedadEjercicio: _contexto.Trabajo.IdSociedadEjercicio),
            abrirOrigen: AbrirOrigen, alCerrar: () => _registro = null);
        _anfitrion.Abrir(_registro);
        _ = _registro.CargarAsync();
    }

    public async Task<bool> ComponerAsync(MensajeNuevo borrador, string? aviso = null)
    {
        var cuenta = await _servicio.Cuentas.ObtenerPorEjercicioAsync(borrador.IdSociedadEjercicio);
        if (cuenta is null || !cuenta.Activa)
        {
            _dialogos.Preguntar("Falta la cuenta de correo",
                cuenta is null ? "El ejercicio no tiene cuenta de correo, y es la que envía." : $"La cuenta de correo {cuenta.Direccion} está desactivada.", null,
                new ProConta.UI.Base.Controles.OpcionMensaje("Entendido", "Configúrala en Configuración › Parámetros y tablas › Cuenta de correo y vuelve a enviar", Principal: true, Cancelar: true));
            return false;
        }
        var remitente = string.IsNullOrWhiteSpace(cuenta.NombreVisible) ? cuenta.Direccion : $"{cuenta.NombreVisible} <{cuenta.Direccion}>";
        var vm = new CompositorCorreoVM(_servicio, _dialogos, _contexto.Actor, borrador, remitente,
            $"{_contexto.RazonSocial} · ejercicio {_contexto.Trabajo.Ejercicio}", aviso, await _servicio.DesvioAsync());
        var ventana = new Vistas.VentanaCompositorCorreo(vm, _dialogos) { Owner = _ventana() };
        ventana.ShowDialog();
        if (vm.IdMensaje is null) return false;
        _ = _registro?.CargarAsync();
        _contexto.Avisar(vm.Fase == FaseCompositor.Enviado ? "Correo enviado: aceptado por el servidor." : "Correo guardado en el registro de correo.");
        return true;
    }

    public async Task EnviarLoteAsync(int idSociedadEjercicio, string descripcion, string origen, IReadOnlyList<PreparacionLote> mensajes)
    {
        var cuenta = await _servicio.Cuentas.ObtenerPorEjercicioAsync(idSociedadEjercicio);
        if (cuenta is null || !cuenta.Activa)
        {
            _dialogos.Preguntar("Falta la cuenta de correo", cuenta is null ? "El ejercicio no tiene cuenta de correo, y es la que envía." : $"La cuenta de correo {cuenta.Direccion} está desactivada.", null,
                new ProConta.UI.Base.Controles.OpcionMensaje("Entendido", "Configúrala en Configuración › Parámetros y tablas › Cuenta de correo y vuelve a enviar: no se ha enviado nada", Principal: true, Cancelar: true));
            return;
        }
        var lote = await _servicio.CrearLoteAsync(idSociedadEjercicio, descripcion, origen, _contexto.Actor);
        var noPreparados = new List<string>();
        var encolados = new List<int>();
        var hechos = 0;
        foreach (var p in mensajes)
        {
            hechos++;
            _contexto.Avisar($"Preparando el correo {hechos} de {mensajes.Count} ({p.Referencia})…");
            try
            {
                var preparado = await p.Preparar();
                if (!preparado.EsCorrecto) { noPreparados.Add($"{p.Referencia}: {Explicar(preparado.Errores)}"); continue; }
                var e = await _servicio.EncolarAsync(preparado.Valor, _contexto.Actor, lote);
                if (e.EsCorrecto) encolados.Add(e.Valor); else noPreparados.Add($"{p.Referencia}: {Explicar(e.Errores)}");
            }
            catch (Exception ex)
            {
                noPreparados.Add($"{p.Referencia}: no he podido prepararlo ({ex.Message}).");
            }
        }
        hechos = 0;
        foreach (var id in encolados)
        {
            hechos++;
            _contexto.Avisar($"Enviando el correo {hechos} de {encolados.Count}…");
            try { await _servicio.EnviarAhoraAsync(id); }
            catch (Exception) { /* sigue en la cola: lo enviará el coordinador */ }
        }
        _ = _registro?.CargarAsync();
        _contexto.Avisar($"Lote de correo terminado: {encolados.Count} {(encolados.Count == 1 ? "mensaje" : "mensajes")}{(noPreparados.Count > 0 ? $", {noPreparados.Count} sin preparar" : "")}.");
        MostrarModal(new FiltroRegistro(IdLote: lote), $"Lote {lote}: {descripcion}", noPreparados);
    }

    private void MostrarModal(FiltroRegistro filtro, string titulo, IReadOnlyList<string> noPreparados)
    {
        var vm = new RegistroCorreoVM(_servicio, _dialogos, _contexto, Pasada, filtro, titulo, noPreparados, AbrirOrigen);
        var ventana = new Vistas.VentanaResumenCorreo(vm) { Owner = _ventana() };
        _ = vm.CargarAsync();
        ventana.ShowDialog();
    }

    private Task Pasada() => _coordinador.PasadaAsync();

    private static string Explicar(IReadOnlyList<Nucleo.Resultados.Error> errores) =>
        string.Join(" ", errores.Select(e => e.Mensaje + (e.AccionCorrectora is { } a ? " " + a : "")));
}
