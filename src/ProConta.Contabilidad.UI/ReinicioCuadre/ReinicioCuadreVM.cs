using System.Windows.Input;
using ProConta.Contabilidad.Mayor;
using ProConta.Contabilidad.UI.Liquidacion;
using ProConta.Nucleo.Auditoria;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.ReinicioCuadre;

/// <summary>Contexto de la pantalla Reiniciar el cuadre del Mayor y lo que abre fuera de ella.</summary>
public sealed record ContextoReinicioCuadre(IFuenteMayor Fuente, string IdSociedad, int EjercicioTrabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Abre el Mayor de unas subcuentas.</summary>
    public Action<IReadOnlyList<string>>? AbrirMayor { get; init; }
    /// <summary>Abre Parámetros y tablas en una tabla (aquí, «general», donde está el umbral).</summary>
    public Action<string>? AbrirParametros { get; init; }
    /// <summary>Lo llama la pantalla después de grabar (para refrescar el Mayor abierto).</summary>
    public Action? Cambiada { get; init; }
}

/// <summary>Fila de la tabla: una subcuenta que cambia al reiniciar el cuadre.</summary>
public sealed class FilaReinicioCuadreVM(CambioReinicioCuadre datos)
{
    public CambioReinicioCuadre Datos { get; } = datos;
    public string Cuenta => Datos.Cuenta;
    public string Nombre => Datos.Nombre;
    public string Saldo => Formatos.Importe(Datos.Saldo);
    public string Cambio => Datos.Cambio;
    public bool Marcar => Datos.Marcar;
    public string Notas => Datos.TieneNotas ? "Sí" : "";
    public string AyudaCambio => Datos.Marcar
        ? Datos.EstabaMarcada
            ? "Estaba marcada con otro saldo; sigue dentro del umbral y se vuelve a marcar con el saldo actual"
            : "Saldo dentro del umbral: se marca como cuadrada con su saldo actual"
        : "Saldo fuera del umbral (o sin movimientos en el ejercicio): se le quita la casilla «Cuadrada». Sus notas se conservan";
}

/// <summary>
/// Pantalla Contabilidad › Reiniciar el cuadre del Mayor (contabilidad/procesos/reiniciar-cuadre): en el ejercicio de trabajo, marca
/// como cuadradas las subcuentas con movimientos y saldo dentro del umbral (Configuración general, vacío = 0,05 €) y quita la casilla
/// a las demás, sin tocar nunca las notas. Enseña antes lo que cambia y graba exactamente eso con el botón del pie. No hace asientos.
/// </summary>
public sealed class ReinicioCuadreVM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoReinicioCuadre _ctx;
    private readonly IDialogosLiquidacionIva _dialogos;
    private readonly Action? _alCerrar;
    private readonly SesionMayor _sesion;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private PropuestaReinicioCuadre? _propuesta;
    private IReadOnlyList<FilaReinicioCuadreVM> _filas = [];

    public ReinicioCuadreVM(ContextoReinicioCuadre ctx, IDialogosLiquidacionIva dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        _sesion = new SesionMayor(ctx.Fuente, ctx.IdSociedad);
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        ReiniciarCommand = new ComandoDelegado(() => _ = ReiniciarAsync(), () => !_ocupado && PorQueNoReiniciar is null);
        UmbralCommand = new ComandoDelegado(() => _ctx.AbrirParametros?.Invoke("general"), () => _ctx.AbrirParametros is not null);
        MayorCommand = new ComandoDelegado(p => { if (p is FilaReinicioCuadreVM f) _ctx.AbrirMayor?.Invoke([f.Cuenta]); },
            p => _ctx.AbrirMayor is not null && p is FilaReinicioCuadreVM);
    }

    // ---------- pestaña ----------
    public string Titulo => "Reiniciar el cuadre";
    public string Ayuda => $"Reiniciar el cuadre del Mayor de {_ctx.RazonSocial} · {_ctx.EjercicioTrabajo}";
    public string Icono => "book";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaReinicioCuadre { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    void IPestanaTrabajo.Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand ReiniciarCommand { get; }
    public ICommand UmbralCommand { get; }
    public ICommand MayorCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.EjercicioTrabajo}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public PropuestaReinicioCuadre? Propuesta
    {
        get => _propuesta;
        private set
        {
            _propuesta = value;
            Filas = value?.Cambios.Select(c => new FilaReinicioCuadreVM(c)).ToList() ?? [];
            Avisar(nameof(Propuesta), nameof(TextoUmbral), nameof(Resumen), nameof(HayCambios), nameof(SinCambios), nameof(PorQueNoReiniciar));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public IReadOnlyList<FilaReinicioCuadreVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas)); } }
    public bool HayCambios => _filas.Count > 0;
    public bool SinCambios => _propuesta is not null && _filas.Count == 0;

    public string TextoUmbral => _propuesta is null ? "" : $"Umbral: ±{Formatos.Importe(_propuesta.Umbral)}";

    public string Resumen => _propuesta is not { } p ? ""
        : $"Se marcan {Formatos.Recuento(p.SeMarcan)} · se desmarcan {Formatos.Recuento(p.SeDesmarcan)} · siguen cuadradas {Formatos.Recuento(p.SiguenCuadradas)}";

    public string? PorQueNoReiniciar =>
        _propuesta is null ? "Espera a que se calcule la propuesta."
        : _propuesta.Trabajo is null ? $"El ejercicio {_ctx.EjercicioTrabajo} no está en la contabilidad de la sociedad: elige otro ejercicio de trabajo."
        : _filas.Count == 0 ? "No hay nada que cambiar: el cuadre ya está como quedaría al reiniciarlo."
        : null;

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            if (_propuesta is null) Estado = EstadoVista.Cargando;
            Ocupado = true;
            if (_sesion.Ejercicios.Count == 0) await _sesion.IniciarAsync(_ctx.EjercicioTrabajo, [_ctx.EjercicioTrabajo]);
            Propuesta = await _sesion.ProponerReinicioCuadreAsync(_ctx.Actor.Nombre, DateTime.Now);
            Estado = EstadoVista.Contenido;
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer los saldos y los cuadres del Mayor: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private async Task ReiniciarAsync()
    {
        if (_propuesta is not { } p || PorQueNoReiniciar is not null) return;
        var detalles = new List<string>
        {
            $"Se marcan como cuadradas {Formatos.Plural(p.SeMarcan, "subcuenta", "subcuentas")} con saldo de hasta ±{Formatos.Importe(p.Umbral)}, con su saldo actual.",
            $"Se quita la casilla «Cuadrada» a {Formatos.Plural(p.SeDesmarcan, "subcuenta", "subcuentas")}, también a las cuadradas a mano con saldo.",
            "Las notas de todas las cuentas se conservan. No se hace ningún asiento.",
        };
        if (_dialogos.Preguntar($"Reiniciar el cuadre de {p.Trabajo!.Ejercicio}", "Se graban exactamente los cambios de la tabla.", detalles,
                new OpcionMensaje("Reiniciar", "Graba los cambios de la tabla", Principal: true), new OpcionMensaje("Cancelar", "No graba nada", Cancelar: true)) != 0)
            return;
        Ocupado = true;
        try
        {
            await _sesion.GuardarReinicioCuadreAsync(p);
            _ctx.Avisar($"Cuadre del Mayor de {p.Trabajo.Ejercicio} reiniciado: {Formatos.Plural(p.SeMarcan, "subcuenta marcada", "subcuentas marcadas")} y " +
                $"{Formatos.Plural(p.SeDesmarcan, "desmarcada", "desmarcadas")}.");
            _ctx.Cambiada?.Invoke();
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha reiniciado el cuadre", $"{ex.Message} No se ha grabado nada: pulsa Actualizar (F5) y vuelve a intentarlo.");
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }
}
