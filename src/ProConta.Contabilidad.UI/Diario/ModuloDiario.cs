using System.ComponentModel;
using ProConta.Contabilidad.Diario;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Contenedor;

namespace ProConta.Contabilidad.UI.Diario;

/// <summary>Pestaña Contabilidad › Diario del contenedor. Sus cambios pendientes son los de la ficha del apunte (Guardar: Ctrl+G).</summary>
public sealed class PestanaDiario : IPestanaTrabajo
{
    private readonly PantallaDiario _vista;
    private readonly Action _cerrada;
    private bool _guardadoFallido;

    internal PestanaDiario(DiarioVM vm, Action cerrada)
    {
        Modelo = vm;
        _cerrada = cerrada;
        _vista = new PantallaDiario { DataContext = vm };
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(DiarioVM.TieneCambios)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TieneCambios)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Ayuda)));
        };
    }

    public DiarioVM Modelo { get; }
    public event PropertyChangedEventHandler? PropertyChanged;
    public string Titulo => "Diario";
    public string Ayuda => Modelo.TieneCambios ? "Diario: cambios sin guardar en la ficha del apunte" : $"Diario: todos los apuntes del ejercicio {Modelo.EjercicioTrabajo}, por asiento o en lista";
    public string Icono => "list";
    public bool TieneCambios => Modelo.TieneCambios;
    public bool GuardadoFallido => _guardadoFallido;
    public object Vista => _vista;
    public IReadOnlyList<string> DescribirCambios() => Modelo.DescribirCambios();

    public async Task<bool> GuardarAsync()
    {
        var ok = await Modelo.GuardarFichaAsync();
        _guardadoFallido = !ok;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GuardadoFallido)));
        return ok;
    }

    public void Descartar() => Modelo.DescartarFicha();
    public void AlCerrar() => _cerrada();
}

/// <summary>
/// El Diario dentro del contenedor (contabilidad/diario): una sola pestaña por sesión. Conecta la pantalla con la de asiento manual
/// (nuevo, abrir, modificar, asistente de corrección y usar como base), con el Mayor y con el origen de los asientos automáticos, y la
/// refresca cuando se guarda un asiento.
/// </summary>
public sealed class ModuloDiario(ContextoDiario contexto, IAnfitrionPestanas anfitrion, IDialogosDiario dialogos)
{
    private PestanaDiario? _pestana;
    private Asientos.ModuloAsientos? _asientos;

    /// <summary>Pantalla de asiento manual; después de guardar en ella, el Diario abierto se vuelve a leer.</summary>
    public Asientos.ModuloAsientos? Asientos
    {
        get => _asientos;
        set
        {
            _asientos = value;
            if (value is not null) value.Guardado += _ => Refrescar();
        }
    }

    public Mayor.ModuloMayor? Mayor { get; set; }

    /// <summary>Convierte HTML en PDF (el motor de informes, informes/reporting); sin él, «Exportar…» solo ofrece Excel.</summary>
    public Func<string, byte[]>? HtmlAPdf { get; set; }
    public Action<string>? AbrirFichero { get; set; }

    /// <summary>Por qué no se puede abrir el origen de un asiento automático (null: se puede) y cómo abrirlo; los conecta la aplicación.</summary>
    public Func<AsientoDiario, string?>? PorQueNoAbrirOrigen { get; set; }
    public Action<AsientoDiario>? AbrirOrigen { get; set; }

    /// <summary>Vuelve a leer el Diario si está abierto (otra pantalla ha cambiado asientos).</summary>
    public void Refrescar()
    {
        if (_pestana is not null) _ = _pestana.Modelo.CargarAsync();
    }

    public void Abrir()
    {
        if (_pestana is not null)
        {
            anfitrion.Activar(_pestana);
            _ = _pestana.Modelo.CargarAsync();
            return;
        }
        var vm = new DiarioVM(contexto, dialogos)
        {
            HtmlAPdf = HtmlAPdf,
            AbrirFichero = AbrirFichero,
            PorQueNoAbrirOrigen = a => PorQueNoAbrirOrigen is { } f ? f(a) : "Todavía no se puede abrir este origen desde aquí: ábrelo desde su pantalla",
            AbrirOrigen = AbrirOrigen is null ? null : a => AbrirOrigen(a),
        };
        if (Mayor is { } mayor) vm.AbrirMayor = (cuentas, ejercicios) => mayor.Abrir(cuentas, ejercicios);
        if (_asientos is { } asientos)
        {
            ContextoSociedadEjercicio Ejercicio(AsientoDiario a) => new(contexto.Trabajo.IdSociedad, a.Ejercicio.IdSociedadEjercicio, a.Ejercicio.Ejercicio);
            vm.NuevoAsiento = () => _ = asientos.AbrirNuevoAsync();
            vm.AbrirAsiento = (a, modificar) => _ = asientos.AbrirModificarAsync(a.IdAsiento, corregirAutomatico: modificar && a.EsAutomatico, Ejercicio(a));
            vm.AsistenteCorreccion = l => _ = asientos.AbrirCorreccionAsync(l.IdApunte, l.Cuenta);
            vm.UsarComoBase = a => _ = asientos.AbrirNuevoDesdeAsync(a.IdAsiento);
        }
        _pestana = new PestanaDiario(vm, () => _pestana = null);
        anfitrion.Abrir(_pestana);
        _ = vm.IniciarAsync();
    }
}
