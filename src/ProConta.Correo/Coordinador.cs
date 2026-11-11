namespace ProConta.Correo;

/// <summary>
/// Proceso de la cola mientras la aplicación está abierta (correo 2026-11-10 19:40): cada minuto intenta tomar el turno y,
/// si lo consigue, envía los pendientes. Un fallo de una pasada no detiene las siguientes; se avisa al operador una vez.
/// </summary>
public sealed class CoordinadorCorreo : IAsyncDisposable
{
    private readonly ServicioCorreo _servicio;
    private readonly Action<string>? _avisar;
    private readonly TimeSpan _periodo;
    private readonly CancellationTokenSource _fin = new();
    private Task? _bucle;
    private bool _avisadoFallo;

    public CoordinadorCorreo(ServicioCorreo servicio, Action<string>? avisar = null, TimeSpan? periodo = null)
    {
        _servicio = servicio;
        _avisar = avisar;
        _periodo = periodo ?? TimeSpan.FromMinutes(1);
    }

    /// <summary>Se avisa cuando una pasada ha enviado mensajes (para refrescar el registro abierto).</summary>
    public event EventHandler? Procesados;

    public void Iniciar() => _bucle ??= Task.Run(BucleAsync);

    /// <summary>Pide una pasada ya (por ejemplo, tras reintentar desde el registro) sin esperar al minuto.</summary>
    public Task<int> PasadaAsync() => _servicio.ProcesarPendientesAsync(_fin.Token);

    private async Task BucleAsync()
    {
        // Primera pasada a los pocos segundos: el arranque de la aplicación va primero.
        try { await Task.Delay(TimeSpan.FromSeconds(10), _fin.Token); } catch (OperationCanceledException) { return; }
        while (!_fin.IsCancellationRequested)
        {
            try
            {
                if (await _servicio.ProcesarPendientesAsync(_fin.Token) > 0) Procesados?.Invoke(this, EventArgs.Empty);
                _avisadoFallo = false;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex)
            {
                if (!_avisadoFallo) _avisar?.Invoke($"No he podido procesar la cola de correo: {ex.Message}. Lo vuelvo a intentar cada minuto.");
                _avisadoFallo = true;
            }
            try { await Task.Delay(_periodo, _fin.Token); } catch (OperationCanceledException) { return; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        _fin.Cancel();
        if (_bucle is not null) { try { await _bucle; } catch { /* se está cerrando */ } }
        try { await _servicio.SoltarTurnoAsync(); } catch { /* el turno caduca solo */ }
        _fin.Dispose();
    }
}
