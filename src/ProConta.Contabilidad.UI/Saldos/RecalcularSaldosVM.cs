using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Saldos;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Saldos;

/// <summary>Contexto de la pantalla Recalcular saldos y lo que abre fuera de ella.</summary>
public sealed record ContextoRecalcularSaldos(ServicioRecalcularSaldos Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Action<string> Avisar)
{
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
}

/// <summary>Fila de la lista: una subcuenta cuya caché no coincide con sus apuntes.</summary>
public sealed class FilaDiferenciaSaldoVM(DiferenciaSaldo datos)
{
    public DiferenciaSaldo Datos { get; } = datos;
    public string Codigo => Datos.Codigo;
    public string Nombre => Datos.Nombre;
    public string Guardado => Formatos.Importe(Datos.SaldoGuardado);
    public string SegunApuntes => Formatos.Importe(Datos.SaldoApuntes);
    public string Diferencia => Formatos.ImporteOVacio(Datos.Diferencia);
    public string Apuntes => Datos.ApuntesGuardados == Datos.ApuntesReales
        ? Datos.ApuntesReales.ToString(CultureInfo.InvariantCulture)
        : $"{Datos.ApuntesGuardados.ToString(CultureInfo.InvariantCulture)} → {Datos.ApuntesReales.ToString(CultureInfo.InvariantCulture)}";
    public string Ayuda => $"Guardado: {Guardado} con {Formatos.Plural(Datos.ApuntesGuardados, "apunte", "apuntes")}. " +
        $"Según sus apuntes: {SegunApuntes} con {Formatos.Plural(Datos.ApuntesReales, "apunte", "apuntes")}. «Recalcular» deja lo de los apuntes.";
}

/// <summary>Fila del aviso: un código con apuntes que no está en el plan de cuentas del ejercicio.</summary>
public sealed class FilaCuentaSinAltaVM(CuentaSinAlta datos)
{
    public CuentaSinAlta Datos { get; } = datos;
    public string Codigo => Datos.Codigo;
    public string Saldo => Formatos.Importe(Datos.Saldo);
    public string Apuntes => Datos.NumeroApuntes.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// Pantalla Contabilidad › Recalcular saldos (contabilidad/procesos/recalcular-saldos): al abrirla compara, sin grabar, la caché de
/// saldos de las cuentas del ejercicio de trabajo con sus apuntes y lista las que no coinciden y los códigos con apuntes que no
/// están en el plan del ejercicio. «Recalcular», en la cabecera, rehace la caché de todas y vuelve a comparar. No genera asientos:
/// no pide permiso ni motivo y también funciona con el ejercicio cerrado.
/// </summary>
public sealed class RecalcularSaldosVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoRecalcularSaldos _ctx;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private ComparacionSaldosEjercicio? _comparacion;

    public RecalcularSaldosVM(ContextoRecalcularSaldos ctx, Action? alCerrar = null)
    {
        _ctx = ctx;
        _alCerrar = alCerrar;
        CompararCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        RecalcularCommand = new ComandoDelegado(() => _ = RecalcularAsync(), () => !_ocupado && _comparacion is not null);
        MayorCommand = new ComandoDelegado(Mayor, p => _ctx.AbrirMayor is not null && p is FilaDiferenciaSaldoVM);
    }

    // ---------- pestaña ----------
    public string Titulo => "Recalcular saldos";
    public string Ayuda => $"Recalcular los saldos de las subcuentas de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "book";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaRecalcularSaldos { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    void IPestanaTrabajo.Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand CompararCommand { get; }
    public ICommand RecalcularCommand { get; }
    public ICommand MayorCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public IReadOnlyList<FilaDiferenciaSaldoVM> Diferencias { get; private set; } = [];
    public IReadOnlyList<FilaCuentaSinAltaVM> SinAlta { get; private set; } = [];
    public bool HayDiferencias => Diferencias.Count > 0;
    public bool HaySinAlta => SinAlta.Count > 0;

    public string Resumen => _comparacion is not { } c ? ""
        : $"{Formatos.Plural(c.CuentasRevisadas, "cuenta revisada", "cuentas revisadas")}, " +
          (c.Cuadra ? "ninguna con diferencias" : $"{Formatos.Plural(c.Diferencias.Count, "con diferencias", "con diferencias")}");

    public string TextoCuadra => "Los saldos guardados de todas las cuentas del ejercicio coinciden con sus apuntes. No hace falta recalcular, aunque puedes hacerlo.";

    public string TextoSinAlta => "Estos códigos tienen apuntes en el ejercicio pero no están en su plan de cuentas, así que su saldo no se guarda en ninguna cuenta. " +
        "Dalos de alta como subcuentas (por ejemplo, desde el Mayor o el buscador de cuentas) y vuelve a recalcular.";

    // ---------- carga ----------
    /// <summary>Compara la caché con los apuntes, sin grabar nada.</summary>
    public async Task CargarAsync()
    {
        try
        {
            if (_comparacion is null) Estado = EstadoVista.Cargando;
            Ocupado = true;
            Mostrar(await _ctx.Servicio.CompararAsync(_ctx.Trabajo));
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido comparar los saldos con los apuntes: {ex.Message} Pulsa Comparar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private async Task RecalcularAsync()
    {
        var antes = _comparacion?.Diferencias.Count ?? 0;
        Ocupado = true;
        try
        {
            var despues = await _ctx.Servicio.RecalcularAsync(_ctx.Trabajo);
            Mostrar(despues);
            _ctx.Avisar(antes == 0
                ? $"Saldos recalculados: {Formatos.Plural(despues.CuentasRevisadas, "cuenta", "cuentas")}, no había diferencias."
                : $"Saldos recalculados: {Formatos.Plural(antes, "cuenta corregida", "cuentas corregidas")} de {despues.CuentasRevisadas}.");
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido recalcular los saldos: {ex.Message} No se pierde nada: los apuntes no se tocan. Pulsa Comparar (F5) y vuelve a intentarlo; " +
                "si se repite, cierra las demás pantallas o programas que tengan abierta la base de datos.";
        }
        finally { Ocupado = false; }
    }

    private void Mostrar(ComparacionSaldosEjercicio c)
    {
        _comparacion = c;
        Diferencias = c.Diferencias.Select(d => new FilaDiferenciaSaldoVM(d)).ToList();
        SinAlta = c.SinAlta.Select(s => new FilaCuentaSinAltaVM(s)).ToList();
        Estado = EstadoVista.Contenido;
        Avisar(nameof(Diferencias), nameof(SinAlta), nameof(HayDiferencias), nameof(HaySinAlta), nameof(Resumen));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Mayor(object? fila)
    {
        if (_ctx.AbrirMayor is { } abrir && fila is FilaDiferenciaSaldoVM f) abrir([f.Codigo]);
    }
}
