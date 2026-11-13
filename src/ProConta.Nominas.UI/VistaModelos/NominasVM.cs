using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ProConta.Nominas.Consulta;
using ProConta.Nominas.Contabilizacion;
using ProConta.Nominas.Importacion;
using ProConta.Nominas.Lectura;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.Terceros.Roles;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Nominas.UI.VistaModelos;

/// <summary>Servicios de las pantallas de nóminas.</summary>
public sealed record ServiciosNominas(ConsultaNominas Consulta, ServicioImportacionNominas Importacion, ServicioContabilizacionNominas Contabilizacion,
    ServicioEmpleados Empleados);

/// <summary>Contexto de trabajo de la pantalla Nóminas y lo que abre fuera de ella.</summary>
public sealed record ContextoNominas(ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Abre la ficha de un tercero.</summary>
    public Action<int>? AbrirFicha { get; init; }
    /// <summary>Muestra un asiento grabado.</summary>
    public Func<int, Task>? VerAsiento { get; init; }
    /// <summary>Abre la pantalla de correo recibido.</summary>
    public Action? AbrirCorreoRecibido { get; init; }
}

/// <summary>Qué hacer con un fichero analizado.</summary>
public enum DecisionImportacion { Cancelar, SoloImportar, ImportarYContabilizar }

/// <summary>Cómo se resuelve un trabajador: un tercero candidato, o el alta como empleado nuevo.</summary>
public sealed record EleccionTrabajador(CandidatoEmpleado? Candidato, bool Alta);

/// <summary>Un importe que cambia entre la versión vigente y la nueva de un periodo.</summary>
public sealed record CambioReemplazo(string Trabajador, string Campo, decimal Antes, decimal Ahora);

/// <summary>Diálogos de la pantalla Nóminas (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosNominas
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);
    /// <summary>Elige el Excel de la gestoría; null si se cancela.</summary>
    string? ElegirFichero(string titulo);
    /// <summary>Pide el campo de cada columna sin alias (o ignorarla); null si se cancela.</summary>
    IReadOnlyList<AliasNuevo>? DecidirCabeceras(string nombreFichero, IReadOnlyList<CabeceraSinAlias> cabeceras, IReadOnlyList<CampoNomina> campos);
    /// <summary>Resumen del fichero antes de importarlo: periodo, paga, trabajadores, controles y avisos.</summary>
    DecisionImportacion ConfirmarImportacion(string titulo, IReadOnlyList<(string Etiqueta, string Valor)> datos, IReadOnlyList<string> avisos, int sinResolver);
    /// <summary>Elige cómo resolver un trabajador sin reconocer; null si se cancela.</summary>
    EleccionTrabajador? ResolverTrabajador(string titulo, string explicacion, IReadOnlyList<CandidatoEmpleado> candidatos, string textoAlta);
    /// <summary>Muestra lo que cambia entre versiones y pide el motivo; null si se cancela.</summary>
    string? ConfirmarReemplazo(string titulo, string explicacion, IReadOnlyList<CambioReemplazo> cambios, string? sinCambios, string aviso);
    /// <summary>Pide un motivo obligatorio; null si se cancela.</summary>
    string? PedirMotivo(string titulo, string explicacion, string textoBoton);
}

/// <summary>Filtro de la lista de periodos.</summary>
public enum FiltroNominas { Todas, Pendientes, Contabilizadas }

/// <summary>Fila de la lista de periodos: una importación.</summary>
public sealed class FilaImportacionVM(ImportacionNominaResumen datos) : ModeloVista
{
    public ImportacionNominaResumen Datos { get; } = datos;
    public int Id => Datos.IdImportacion;
    public string Periodo => Datos.TextoPeriodo;
    public DateTime PeriodoOrden => Datos.Mes;
    public string Version => Datos.Version > 1 || !Datos.EsVigente ? $"v{Datos.Version}" : "";
    public string TipoPaga => Datos.TipoPaga == Lectura.TipoPaga.Extra ? "Extra" : "Mensual";
    public string AyudaTipoPaga => Datos.TextoPaga ?? TipoPaga;
    public string Fecha => Formatos.Fecha(Datos.FechaNomina);
    public DateTime FechaOrden => Datos.FechaNomina;
    public string Origen => Datos.Origen switch { OrigenNominas.Correo => "Correo", OrigenNominas.Migracion => "Migración", _ => "Fichero" };
    public string AyudaOrigen => Datos.Origen == OrigenNominas.Migracion
        ? "Histórico migrado de ProConta antiguo, enlazado con los asientos importados de ContaSol"
        : $"Fichero {Datos.NombreFichero} · importado el {Datos.FechaHoraImportacion:dd/MM/yyyy HH:mm}" + (Datos.Operador is { } o ? $" por {o}" : "");
    public string Trabajadores => Formatos.Recuento(Datos.Trabajadores);
    public int TrabajadoresOrden => Datos.Trabajadores;
    public string Bruto => Formatos.ImporteOVacio(Datos.Bruto);
    public decimal BrutoOrden => Datos.Bruto;
    public string SSEmp => Formatos.ImporteOVacio(Datos.SSEmp);
    public decimal SSEmpOrden => Datos.SSEmp;
    public string Irpf => Formatos.ImporteOVacio(Datos.Irpf);
    public decimal IrpfOrden => Datos.Irpf;
    public string Neto => Formatos.ImporteOVacio(Datos.Neto);
    public decimal NetoOrden => Datos.Neto;
    public string Coste => Formatos.ImporteOVacio(Datos.CosteEmpresa);
    public decimal CosteOrden => Datos.CosteEmpresa;
    public string Estado => NominasVM.TextoEstado(Datos.Estado);
    public string ClaseEstado => Datos.Estado;
    public bool Antigua => !Datos.EsVigente;
    public string AyudaEstado => Datos.Estado switch
    {
        ServicioImportacionNominas.EstadoIncidencias => $"{Formatos.Plural(Datos.SinResolver, "trabajador sin resolver", "trabajadores sin resolver")}: resuélvelos (clic derecho en sus nóminas) para poder contabilizar",
        ServicioImportacionNominas.EstadoPendiente => "Importadas y listas para contabilizar (clic derecho › Contabilizar)",
        ServicioImportacionNominas.EstadoContabilizada => $"Contabilizadas en {Formatos.Plural(Datos.ConAsiento, "asiento", "asientos")}" + (Datos.FechaHoraEstado is DateTime f ? $" el {f:dd/MM/yyyy HH:mm}" : ""),
        ServicioImportacionNominas.EstadoReemplazada => "Sustituida por otra versión del mismo periodo y paga; se conserva para la trazabilidad" + (Datos.MotivoEstado is { } m ? $". Motivo: {m}" : ""),
        _ => Datos.MotivoEstado ?? Estado,
    } + (Datos.MotivoEstado is { } mot && Datos.Estado == ServicioImportacionNominas.EstadoPendiente ? $". Último cambio: {mot}" : "");
    public string Asientos => Datos.ConAsiento == 0 ? "" : Formatos.Recuento(Datos.ConAsiento);
}

/// <summary>Fila del detalle: la nómina de un trabajador.</summary>
public sealed class FilaNominaVM(NominaGuardada datos) : ModeloVista
{
    public NominaGuardada Datos { get; } = datos;
    private FilaNomina F => Datos.Fila;
    public int Id => Datos.IdNomina;
    public bool SinResolver => Datos.IdEmpleado is null;
    public string Trabajador => Datos.NombreTercero ?? F.Nombre;
    public string AyudaTrabajador => SinResolver
        ? $"No coincide con ningún empleado de la sociedad (ni por código ni por NIF): clic derecho › Resolver trabajador… «{F.Nombre}»" + (F.Nif is { } n ? $" · NIF {n}" : "")
        : $"«{F.Nombre}» en el fichero" + (F.Nif is { } nif ? $" · NIF {nif}" : "") + ". Clic derecho › Abrir la ficha del empleado";
    public string Codigo => F.CodigoTrabajador ?? "";
    public string Cuenta => Datos.Cuenta465 ?? "";
    public string Bruto => Formatos.ImporteOVacio(F["Bruto"]);
    public decimal BrutoOrden => F["Bruto"];
    public string DtoEspecie => Formatos.ImporteOVacio(F["DtoReEsp"]);
    public string SSEmp => Formatos.ImporteOVacio(F["SSEmp"]);
    public string CuotasObreras => Formatos.ImporteOVacio(F.CuotasObreras);
    public string Autonomo => Formatos.ImporteOVacio(F["Autonomo"]);
    public string Irpf => Formatos.ImporteOVacio(F["IRPF111"]);
    public string Neto => Formatos.ImporteOVacio(F["Neto"]);
    public decimal NetoOrden => F["Neto"];
    public string Coste => Formatos.ImporteOVacio(F["CostEmp"]);
    public bool Cuadra => F.DescuadreNeto == 0m;
    public string Cuadre => Cuadra ? "✓" : Formatos.Importe(F.DescuadreNeto);
    public string AyudaCuadre => Cuadra
        ? $"Bruto + dto. especie − autónomo − IRPF − cuotas obreras (y anticipos, embargos…) = neto {Formatos.Importe(F["Neto"])}"
        : $"El neto del fichero ({Formatos.Importe(F["Neto"])}) no cuadra con el bruto menos las deducciones ({Formatos.Importe(F.NetoCalculado)}): revísalo con la gestoría";
    public string Asiento => Datos.IdAsiento?.ToString() ?? "";
}

/// <summary>
/// Pantalla Nóminas (nominas/pantallas, maqueta validada 2026-11-13): arriba los periodos importados del ejercicio (uno por mes y
/// paga) con sus totales y su estado; abajo las nóminas del periodo elegido, una por trabajador. «Importar fichero…» en la
/// cabecera (o soltando el Excel en la pantalla); contabilizar, deshacer, reemplazar y resolver trabajadores en el menú
/// contextual, con selección múltiple en los periodos.
/// </summary>
public sealed class NominasVM : ModeloVista, IPestanaTrabajo
{
    private readonly ServiciosNominas _s;
    private readonly IDialogosNominas _dialogos;
    private readonly ContextoNominas _contexto;
    private readonly Action? _alCerrar;
    private List<ImportacionNominaResumen> _todas = [];
    private IReadOnlyList<FilaImportacionVM> _seleccion = [];
    private FilaImportacionVM? _actual;
    private FilaNominaVM? _nomina;
    private FiltroNominas _filtro = FiltroNominas.Todas;
    private bool _verAntiguas;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _ocupado;
    private object? _vista;

    public NominasVM(ServiciosNominas servicios, IDialogosNominas dialogos, ContextoNominas contexto, Action? alCerrar = null)
    {
        _s = servicios;
        _dialogos = dialogos;
        _contexto = contexto;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync());
        ImportarCommand = new ComandoDelegado(() => _ = ImportarAsync(), () => !_ocupado);
        ContabilizarCommand = new ComandoDelegado(() => _ = ContabilizarAsync(), () => !_ocupado && MotivoContabilizar is null);
        DeshacerCommand = new ComandoDelegado(() => _ = DeshacerAsync(), () => !_ocupado && MotivoDeshacer is null);
        ReemplazarCommand = new ComandoDelegado(() => _ = ReemplazarAsync(), () => !_ocupado && MotivoReemplazar is null);
        VerAsientosCommand = new ComandoDelegado(() => _ = VerAsientosAsync(), () => _seleccion.Any(f => f.Datos.ConAsiento > 0) && _contexto.VerAsiento is not null);
        AbrirCorreoCommand = new ComandoDelegado(() => _contexto.AbrirCorreoRecibido?.Invoke(),
            () => _seleccion.Count == 1 && _seleccion[0].Datos.Origen == OrigenNominas.Correo && _contexto.AbrirCorreoRecibido is not null);
        ResolverCommand = new ComandoDelegado(() => _ = ResolverAsync(), () => !_ocupado && _nomina is { SinResolver: true } && _actual is { Antigua: false });
        ResolverPrimeroCommand = new ComandoDelegado(() => _ = ResolverPrimeroAsync(), () => !_ocupado && Incidencia is not null);
        AbrirFichaCommand = new ComandoDelegado(() => { if (_nomina?.Datos.IdTercero is int id) _contexto.AbrirFicha?.Invoke(id); },
            () => _nomina?.Datos.IdTercero is not null && _contexto.AbrirFicha is not null);
        VerAsientoCommand = new ComandoDelegado(() => _ = VerAsientoAsync(), () => _nomina?.Datos.IdAsiento is not null && _contexto.VerAsiento is not null);
        FiltrarCommand = new ComandoDelegado(p => { if (p is FiltroNominas f || Enum.TryParse(p as string, out f)) Filtro = f; });
    }

    // ---------- pestaña ----------

    public string Titulo => "Nóminas";
    public string Ayuda => $"Nóminas de {_contexto.RazonSocial}: importación de los Excel de la gestoría, contabilización y trabajadores";
    public string Icono => "coin";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaNominas { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- estado ----------

    public string Subtitulo => $"{_contexto.RazonSocial} · {_contexto.Trabajo.Ejercicio}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string TituloEstado => Estado == EstadoVista.Vacio ? (_todas.Count == 0 ? "Aún no hay nóminas importadas" : "Ningún periodo con este filtro") : "";
    public ObservableCollection<FilaImportacionVM> Importaciones { get; } = [];
    public ObservableCollection<FilaNominaVM> Nominas { get; } = [];
    public IReadOnlyList<FilaImportacionVM> Seleccion => _seleccion;

    public FiltroNominas Filtro
    {
        get => _filtro;
        set { if (Fijar(ref _filtro, value)) { Avisar(nameof(FiltroTodas), nameof(FiltroPendientes), nameof(FiltroContabilizadas)); Mostrar(); } }
    }

    public bool FiltroTodas { get => _filtro == FiltroNominas.Todas; set { if (value) Filtro = FiltroNominas.Todas; } }
    public bool FiltroPendientes { get => _filtro == FiltroNominas.Pendientes; set { if (value) Filtro = FiltroNominas.Pendientes; } }
    public bool FiltroContabilizadas { get => _filtro == FiltroNominas.Contabilizadas; set { if (value) Filtro = FiltroNominas.Contabilizadas; } }

    /// <summary>Muestra también las versiones reemplazadas y las importaciones deshechas.</summary>
    public bool VerAntiguas
    {
        get => _verAntiguas;
        set { if (Fijar(ref _verAntiguas, value)) Mostrar(); }
    }

    /// <summary>Periodo cuyo detalle se ve (el primero de la selección).</summary>
    public FilaImportacionVM? Actual => _actual;
    public bool HayActual => _actual is not null;
    public FilaNominaVM? Nomina
    {
        get => _nomina;
        set { if (Fijar(ref _nomina, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public string TituloDetalle => _actual is not { } a ? "Nóminas" : $"Nóminas de {a.Periodo.ToLowerInvariant()} · {a.TipoPaga.ToLowerInvariant()}" + (a.Version.Length > 0 ? $" ({a.Version})" : "");
    public string PieDetalle => _actual is not { } a ? "" : a.Datos.Origen == OrigenNominas.Migracion
        ? "Histórico migrado de ProConta antiguo, enlazado con los asientos importados de ContaSol."
        : $"Fichero {a.Datos.NombreFichero} · importado el {a.Datos.FechaHoraImportacion:dd/MM/yyyy HH:mm}" + (a.Datos.Operador is { } o ? $" por {o}" : "") +
          (Nominas.All(n => n.Cuadra) ? " · todas las filas cuadran" : $" · {Nominas.Count(n => !n.Cuadra)} fila(s) no cuadran");
    public string TotalBruto => Formatos.Importe(Nominas.Sum(n => n.Datos.Fila["Bruto"]));
    public string TotalNeto => Formatos.Importe(Nominas.Sum(n => n.Datos.Fila["Neto"]));
    public string TotalIrpf => Formatos.Importe(Nominas.Sum(n => n.Datos.Fila["IRPF111"]));
    public string TotalCoste => Formatos.Importe(Nominas.Sum(n => n.Datos.Fila["CostEmp"]));

    public string Resumen
    {
        get
        {
            var vigentes = _todas.Where(i => i.EsVigente).ToList();
            if (vigentes.Count == 0) return "Sin nóminas en el ejercicio";
            return $"{Formatos.Plural(vigentes.Count, "periodo", "periodos")} · bruto {Formatos.Importe(vigentes.Sum(i => i.Bruto))} · IRPF {Formatos.Importe(vigentes.Sum(i => i.Irpf))} · " +
                $"{Formatos.Plural(vigentes.Count(i => i.Estado == ServicioImportacionNominas.EstadoContabilizada), "contabilizado", "contabilizados")}";
        }
    }

    /// <summary>Aviso de la cabecera: el primer periodo con trabajadores sin resolver.</summary>
    public ImportacionNominaResumen? Incidencia => _todas.Where(i => i.Estado == ServicioImportacionNominas.EstadoIncidencias).OrderBy(i => i.Mes).FirstOrDefault();
    public bool HayIncidencia => Incidencia is not null;
    public string TextoIncidencia => Incidencia is not { } i ? "" :
        $"{i.TextoPeriodo}: {Formatos.Plural(i.SinResolver, "trabajador sin resolver", "trabajadores sin resolver")}." +
        (i.Origen == OrigenNominas.Correo ? $" Llegó por correo el {i.FechaHoraImportacion:dd/MM} ({i.NombreFichero})." : $" Fichero {i.NombreFichero}.") +
        " Resuélvelos para poder contabilizar.";

    public ICommand RefrescarCommand { get; }
    public ICommand ImportarCommand { get; }
    public ICommand ContabilizarCommand { get; }
    public ICommand DeshacerCommand { get; }
    public ICommand ReemplazarCommand { get; }
    public ICommand VerAsientosCommand { get; }
    public ICommand AbrirCorreoCommand { get; }
    public ICommand ResolverCommand { get; }
    public ICommand ResolverPrimeroCommand { get; }
    public ICommand AbrirFichaCommand { get; }
    public ICommand VerAsientoCommand { get; }
    public ICommand FiltrarCommand { get; }

    // Por qué no se puede cada acción sobre la selección (null = se puede); los tooltips del menú lo explican.
    public string? MotivoContabilizar => _seleccion.Count == 0 ? "Elige uno o varios periodos"
        : _seleccion.Any(f => f.Datos.Estado == ServicioImportacionNominas.EstadoIncidencias) ? "Resuelve antes los trabajadores sin reconocer"
        : _seleccion.Any(f => f.Datos.Estado != ServicioImportacionNominas.EstadoPendiente) ? "Solo periodos pendientes" : null;
    public string? MotivoDeshacer => _seleccion.Count == 0 ? "Elige uno o varios periodos"
        : _seleccion.Any(f => f.Datos.Estado != ServicioImportacionNominas.EstadoContabilizada) ? "Solo periodos contabilizados" : null;
    public string? MotivoReemplazar => _seleccion.Count != 1 ? "Elige un solo periodo" : _seleccion[0].Antigua ? "Esta versión ya no está vigente" : null;
    public string AyudaContabilizar => MotivoContabilizar ?? "Genera un asiento por trabajador con la plantilla «Nómina» de la sociedad, todos en una operación";
    public string AyudaDeshacer => MotivoDeshacer ?? "En un trimestre abierto borra sus asientos; en uno cerrado los anula con asientos del día 1 del trimestre activo. Las nóminas quedan pendientes";
    public string AyudaReemplazar => MotivoReemplazar ?? "Elige la versión corregida del Excel de este periodo: se ven los cambios, se deshace la contabilización si la tenía y se importa y contabiliza la nueva";

    /// <summary>La vista avisa de la selección de periodos (selección múltiple).</summary>
    public void CambiarSeleccion(IReadOnlyList<FilaImportacionVM> filas)
    {
        _seleccion = filas;
        var actual = filas.FirstOrDefault();
        Avisar(nameof(Seleccion), nameof(AyudaContabilizar), nameof(AyudaDeshacer), nameof(AyudaReemplazar));
        CommandManager.InvalidateRequerySuggested();
        if (actual == _actual) return;
        _actual = actual;
        Avisar(nameof(Actual), nameof(HayActual), nameof(TituloDetalle));
        _ = CargarNominasAsync();
    }

    public async Task CargarAsync(int? seleccionar = null)
    {
        try
        {
            if (_todas.Count == 0) Estado = EstadoVista.Cargando;
            _todas = [.. await _s.Consulta.ImportacionesAsync(_contexto.Trabajo, incluirAntiguas: true)];
            Mostrar(seleccionar ?? _actual?.Id);
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer las nóminas: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    /// <summary>Rellena la lista con el filtro y vuelve a elegir el periodo <paramref name="seleccionar"/> (o el primero).</summary>
    private void Mostrar(int? seleccionar = null)
    {
        seleccionar ??= _actual?.Id;
        Importaciones.Clear();
        foreach (var i in _todas.Where(Visible)) Importaciones.Add(new FilaImportacionVM(i));
        Estado = Importaciones.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
        MensajeEstado = _todas.Count == 0
            ? "Pulsa «Importar fichero…» o suelta aquí el Excel de la gestoría. Los que lleguen por correo se importan solos."
            : Importaciones.Count == 0 ? "Cambia el filtro o marca «Ver versiones reemplazadas»." : "";
        _actual = null;
        var elegida = Importaciones.FirstOrDefault(f => f.Id == seleccionar) ?? Importaciones.FirstOrDefault();
        CambiarSeleccion(elegida is null ? [] : [elegida]);
        Avisar(nameof(Resumen), nameof(TituloEstado), nameof(Incidencia), nameof(HayIncidencia), nameof(TextoIncidencia), nameof(IdsSeleccionados));
    }

    /// <summary>Ids de la selección, para que la vista la restaure al rehacer la lista.</summary>
    public IReadOnlyList<int> IdsSeleccionados => _seleccion.Select(f => f.Id).ToList();

    private bool Visible(ImportacionNominaResumen i) => (_verAntiguas || i.EsVigente) && _filtro switch
    {
        FiltroNominas.Pendientes => i.Estado is ServicioImportacionNominas.EstadoPendiente or ServicioImportacionNominas.EstadoIncidencias,
        FiltroNominas.Contabilizadas => i.Estado == ServicioImportacionNominas.EstadoContabilizada,
        _ => true,
    };

    private async Task CargarNominasAsync()
    {
        var actual = _actual;
        var id = _nomina?.Id;
        Nominas.Clear();
        Nomina = null;
        if (actual is not null)
            try
            {
                var nominas = await _s.Consulta.NominasAsync(actual.Id);
                if (_actual != actual) return;
                foreach (var n in nominas) Nominas.Add(new FilaNominaVM(n));
                Nomina = Nominas.FirstOrDefault(n => n.Id == id) ?? Nominas.FirstOrDefault(n => n.SinResolver) ?? Nominas.FirstOrDefault();
            }
            catch (Exception ex)
            {
                MensajeEstado = $"No he podido leer las nóminas del periodo: {ex.Message}";
            }
        Avisar(nameof(PieDetalle), nameof(TotalBruto), nameof(TotalNeto), nameof(TotalIrpf), nameof(TotalCoste));
    }

    // ---------- importar ----------

    /// <summary>Importa un Excel elegido por el operador, soltado en la pantalla o <paramref name="ruta"/> (en pruebas).</summary>
    public async Task ImportarAsync(string? ruta = null)
    {
        ruta ??= _dialogos.ElegirFichero("Importar las nóminas de la gestoría");
        if (ruta is null) return;
        await OcupadoAsync("No se ha podido importar", async () =>
        {
            var bytes = await File.ReadAllBytesAsync(ruta);
            var nombre = Path.GetFileName(ruta);
            var (analisis, alias) = await AnalizarAsync(bytes, nombre);
            if (analisis is null) return;
            switch (analisis.Veredicto)
            {
                case VeredictoNominas.YaImportado:
                    await CargarAsync(analisis.IdImportacionExistente);
                    Informar("Ya estaba importado", analisis.Explicacion);
                    return;
                case VeredictoNominas.OtraVersion when analisis.IdImportacionExistente is int existente:
                    if (_dialogos.Preguntar("Otra versión de un periodo ya importado", analisis.Explicacion + " ¿Quieres ver qué cambia y reemplazarlo?", null,
                            new OpcionMensaje("Ver los cambios…", "Compara las dos versiones por trabajador y, si aceptas, reemplaza la anterior", Principal: true),
                            new OpcionMensaje("Cancelar", "No importa nada", Cancelar: true)) != 0)
                        return;
                    await ReemplazarConAsync(existente, bytes, nombre, analisis, alias);
                    return;
                case VeredictoNominas.Importable:
                    break;
                default:
                    _dialogos.Preguntar($"No se ha importado «{nombre}»", analisis.Explicacion, analisis.Fichero.Errores.Select(e => e.ToString()).ToList(),
                        new OpcionMensaje("Entendido", "No se ha importado nada; corrige lo que dice el aviso y vuelve a importar", Principal: true, Cancelar: true));
                    return;
            }
            var f = analisis.Fichero;
            var decision = _dialogos.ConfirmarImportacion($"Importar nóminas · {nombre}",
            [
                ("Sociedad (por NIF)", $"{_contexto.RazonSocial}" + (f.NifEmpresa is { } nif ? $" · {nif}" : "")),
                ("Periodo", f.PeriodoDesde is DateTime d && f.PeriodoHasta is DateTime h ? $"{Formatos.Fecha(d)} – {Formatos.Fecha(h)}" : AnalisisNominas.Describir(f)),
                ("Tipo de paga", f.TipoPaga == TipoPaga.Extra ? "Extra" : "Mensual"),
                ("Fecha de la nómina", f.FechaNomina is DateTime fn ? Formatos.Fecha(fn) : ""),
                ("Trabajadores", Formatos.Plural(f.Filas.Count, "fila", "filas")),
                ("Controles", (f.Filas.All(x => x.DescuadreNeto == 0) ? $"Cuadran las {f.Filas.Count} filas" : $"{f.Filas.Count(x => x.DescuadreNeto != 0)} fila(s) no cuadran") +
                    (f.TieneTotal ? " · el total coincide" : "")),
            ],
                [.. f.Advertencias.Select(a => a.ToString()),
                 .. analisis.SinResolver.Select(t => $"Sin resolver: código {t.Fila.CodigoTrabajador ?? "—"} «{t.Fila.Nombre}». No se contabiliza hasta resolverlo.")],
                analisis.SinResolver.Count);
            if (decision == DecisionImportacion.Cancelar) return;
            var r = await _s.Importacion.ImportarAsync(_contexto.Trabajo, bytes, nombre, OrigenNominas.Fichero, _contexto.Actor, alias);
            if (!r.EsCorrecto) { Fallo("No se ha importado", r.Errores); return; }
            var id = r.Valor.IdImportacion;
            await CargarAsync(id);
            _contexto.Avisar(r.Valor.Explicacion);
            if (decision != DecisionImportacion.ImportarYContabilizar || id is not int idImp) return;
            if (!await ResolverTodosAsync(idImp)) return;
            await ContabilizarIdsAsync([idImp]);
        });
    }

    /// <summary>Analiza el fichero y, si trae columnas sin alias, pide su campo y vuelve a analizarlo; null si se cancela.</summary>
    private async Task<(AnalisisNominas? Analisis, IReadOnlyList<AliasNuevo>? Alias)> AnalizarAsync(byte[] bytes, string nombre)
    {
        var analisis = await _s.Importacion.AnalizarAsync(_contexto.Trabajo, bytes);
        IReadOnlyList<AliasNuevo>? alias = null;
        if (analisis.Veredicto == VeredictoNominas.CabecerasSinAlias)
        {
            alias = _dialogos.DecidirCabeceras(nombre, analisis.Fichero.SinAlias, [.. CamposNomina.Todos]);
            if (alias is null) return (null, null);
            analisis = await _s.Importacion.AnalizarAsync(_contexto.Trabajo, bytes, alias);
        }
        return (analisis, alias);
    }

    // ---------- resolver trabajadores ----------

    private async Task ResolverAsync()
    {
        if (_nomina is not { SinResolver: true } n || _actual is not { } a) return;
        await OcupadoAsync("No se ha podido resolver el trabajador", async () =>
        {
            if (await ResolverNominaAsync(n.Datos) is not true) return;
            await CargarAsync(a.Id);
        });
    }

    private async Task ResolverPrimeroAsync()
    {
        if (Incidencia is not { } i) return;
        await OcupadoAsync("No se ha podido resolver el trabajador", async () =>
        {
            if (!await ResolverTodosAsync(i.IdImportacion)) { await CargarAsync(i.IdImportacion); return; }
            await CargarAsync(i.IdImportacion);
            if (await _s.Consulta.ImportacionAsync(i.IdImportacion) is { Estado: ServicioImportacionNominas.EstadoPendiente } &&
                _dialogos.Preguntar("Trabajadores resueltos", $"{i.TextoPeriodo} ya no tiene trabajadores sin resolver. ¿Lo contabilizo ahora?", null,
                    new OpcionMensaje("Contabilizar", "Genera los asientos con la plantilla «Nómina»", Principal: true),
                    new OpcionMensaje("Más tarde", "Queda pendiente de contabilizar", Cancelar: true)) == 0)
                await ContabilizarIdsAsync([i.IdImportacion]);
        });
    }

    /// <summary>Resuelve uno a uno los trabajadores sin reconocer de la importación; false si el operador cancela alguno.</summary>
    private async Task<bool> ResolverTodosAsync(int idImportacion)
    {
        var resueltos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var n in (await _s.Consulta.NominasAsync(idImportacion)).Where(n => n.IdEmpleado is null))
        {
            // Las nóminas con el mismo código se resuelven juntas.
            if (n.Fila.CodigoTrabajador is { Length: > 0 } c && !resueltos.Add(c)) continue;
            if (await ResolverNominaAsync(n) is not true) return false;
        }
        return true;
    }

    /// <summary>Pide al operador cómo resolver el trabajador de la nómina y lo aplica; null si cancela, false si falla.</summary>
    private async Task<bool?> ResolverNominaAsync(NominaGuardada n)
    {
        var candidatos = (await _s.Consulta.CandidatosAsync(_contexto.Trabajo, n)).Candidatos;
        var f = n.Fila;
        var explicacion = $"Código {f.CodigoTrabajador ?? "—"} · «{f.Nombre}»" + (f.Nif is { } nif ? $" · NIF {nif}" : "") + ". " +
            (candidatos.Count == 0 ? "Ningún tercero coincide por NIF ni por nombre." : "Ningún empleado tiene ese código ni ese NIF. Candidatos (nunca se asigna solo):");
        var eleccion = _dialogos.ResolverTrabajador("Resolver trabajador", explicacion, candidatos,
            $"Crea el tercero «{f.Nombre}» con el rol Empleado" + (f.CodigoTrabajador is { } cod ? $", el código {cod}" : "") + " y la siguiente cuenta 465 libre.");
        if (eleccion is null) return null;
        var empleado = eleccion.Candidato is { } cand
            ? await _s.Empleados.AsignarAsync(_contexto.Trabajo, cand.IdTercero, f.CodigoTrabajador)
            : await _s.Empleados.AltaAsync(_contexto.Trabajo, f.Nombre, f.Nif, f.CodigoTrabajador);
        if (!empleado.EsCorrecto) { Fallo("No se ha podido resolver el trabajador", empleado.Errores); return false; }
        var r = await _s.Importacion.ResolverTrabajadorAsync(_contexto.Trabajo, n.IdImportacion, f.Ordinal, empleado.Valor.IdEmpleado);
        if (!r.EsCorrecto) { Fallo("No se ha podido resolver el trabajador", r.Errores); return false; }
        _contexto.Avisar($"«{f.Nombre}» resuelto: {empleado.Valor.Nombre}" + (empleado.Valor.Cuenta is { } cta ? $", cuenta {cta}." : "."));
        return true;
    }

    // ---------- contabilizar y deshacer ----------

    private Task ContabilizarAsync() => OcupadoAsync("No se ha podido contabilizar", () => ContabilizarIdsAsync(_seleccion.Select(f => f.Id).ToList()));

    private async Task ContabilizarIdsAsync(IReadOnlyList<int> ids)
    {
        var hechos = new List<string>();
        foreach (var id in ids)
        {
            var r = await _s.Contabilizacion.ContabilizarAsync(id, _contexto.Actor);
            if (!r.EsCorrecto)
            {
                await CargarAsync(id);
                Fallo("No se ha podido contabilizar", r.Errores, hechos.Count == 0 ? null : "Sí se han contabilizado: " + string.Join("; ", hechos) + ".");
                return;
            }
            hechos.Add($"{Formatos.Plural(r.Valor.Asientos.Count, "asiento", "asientos")} del {Formatos.Fecha(r.Valor.FechaAsiento)}" +
                (r.Valor.Avisos.Count > 0 ? $" ({string.Join(" ", r.Valor.Avisos)})" : ""));
        }
        await CargarAsync(ids.FirstOrDefault());
        _contexto.Avisar("Contabilizado: " + string.Join("; ", hechos) + ".");
    }

    private async Task DeshacerAsync()
    {
        var periodos = _seleccion.ToList();
        if (periodos.Count == 0) return;
        var asientos = periodos.Sum(p => p.Datos.ConAsiento);
        var motivo = _dialogos.PedirMotivo("Deshacer la contabilización",
            string.Join(", ", periodos.Select(p => $"{p.Periodo} · {p.TipoPaga.ToLowerInvariant()}")) + ". " +
            $"Si su trimestre está abierto se borran sus {Formatos.Plural(asientos, "asiento", "asientos")}; si está cerrado (o son asientos migrados), no se tocan y se anulan con " +
            "asientos del día 1 del trimestre activo. Las nóminas quedan importadas y pendientes, y se pueden volver a contabilizar. El motivo queda en la auditoría.",
            "Deshacer");
        if (motivo is null) return;
        await OcupadoAsync("No se ha podido deshacer", async () =>
        {
            var hechos = new List<string>();
            foreach (var p in periodos)
            {
                var r = await _s.Contabilizacion.DeshacerAsync(p.Id, _contexto.Actor, motivo);
                if (!r.EsCorrecto)
                {
                    await CargarAsync(p.Id);
                    Fallo($"No se ha podido deshacer {p.Periodo.ToLowerInvariant()}", r.Errores, hechos.Count == 0 ? null : string.Join(" ", hechos));
                    return;
                }
                hechos.Add($"{p.Periodo}: {r.Valor.Explicacion}");
            }
            await CargarAsync(periodos[0].Id);
            _contexto.Avisar(string.Join(" ", hechos));
        });
    }

    // ---------- reemplazar ----------

    private async Task ReemplazarAsync()
    {
        if (_seleccion is not [var periodo]) return;
        var ruta = _dialogos.ElegirFichero($"Versión corregida de {periodo.Periodo.ToLowerInvariant()} ({periodo.TipoPaga.ToLowerInvariant()})");
        if (ruta is null) return;
        await OcupadoAsync("No se ha podido reemplazar", async () =>
        {
            var bytes = await File.ReadAllBytesAsync(ruta);
            var nombre = Path.GetFileName(ruta);
            var (analisis, alias) = await AnalizarAsync(bytes, nombre);
            if (analisis is null) return;
            if (analisis.Veredicto != VeredictoNominas.OtraVersion || analisis.IdImportacionExistente != periodo.Id)
            {
                var texto = analisis.Veredicto switch
                {
                    VeredictoNominas.YaImportado when analisis.IdImportacionExistente == periodo.Id => "Es el mismo fichero que ya está importado: no hay nada que reemplazar.",
                    VeredictoNominas.Importable => $"«{nombre}» es de {AnalisisNominas.Describir(analisis.Fichero)}, no de {periodo.Periodo.ToLowerInvariant()}: impórtalo con «Importar fichero…».",
                    _ => analisis.Explicacion,
                };
                Informar("No es otra versión de este periodo", texto);
                return;
            }
            await ReemplazarConAsync(periodo.Id, bytes, nombre, analisis, alias);
        });
    }

    /// <summary>Enseña los cambios por trabajador, pide el motivo y reemplaza (deshace, importa y contabiliza).</summary>
    private async Task ReemplazarConAsync(int idAnterior, byte[] bytes, string nombre, AnalisisNominas analisis, IReadOnlyList<AliasNuevo>? alias)
    {
        var anterior = await _s.Consulta.ImportacionAsync(idAnterior);
        if (anterior is null) return;
        var antes = await _s.Consulta.NominasAsync(idAnterior);
        var (cambios, iguales) = Comparar(antes.Select(n => n.Fila).ToList(), analisis.Fichero.Filas);
        var aviso = anterior.Estado == ServicioImportacionNominas.EstadoContabilizada
            ? $"Se deshace su contabilización ({Formatos.Plural(anterior.ConAsiento, "asiento", "asientos")}): si su trimestre está cerrado, no se tocan y se anulan con asientos del día 1 del trimestre activo, " +
              "donde irán también los nuevos. La versión anterior se conserva como «reemplazada»."
            : "La versión anterior no estaba contabilizada; se conserva como «reemplazada».";
        var motivo = _dialogos.ConfirmarReemplazo($"Reemplazar {anterior.TextoPeriodo.ToLowerInvariant()}",
            $"«{nombre}» es otra versión del mismo periodo y la misma paga, con contenido distinto.", cambios,
            iguales.Count == 0 ? null : string.Join(", ", iguales) + ": sin cambios.", aviso);
        if (motivo is null) return;
        var r = await _s.Contabilizacion.ReemplazarAsync(_contexto.Trabajo, idAnterior, bytes, nombre, OrigenNominas.Fichero, _contexto.Actor, motivo, alias);
        if (!r.EsCorrecto) { await CargarAsync(idAnterior); Fallo("No se ha podido reemplazar", r.Errores); return; }
        await CargarAsync(r.Valor.Importacion.IdImportacion);
        var texto = $"{anterior.TextoPeriodo} reemplazado." + (r.Valor.Deshecha is { } d ? " " + d.Explicacion : "") +
            (r.Valor.Contabilizada is { } c ? $" La nueva versión está contabilizada en {Formatos.Plural(c.Asientos.Count, "asiento", "asientos")} del {Formatos.Fecha(c.FechaAsiento)}." :
             r.Valor.ErroresContabilizacion.Count > 0 ? " La nueva versión no se ha podido contabilizar: " + string.Join(" ", r.Valor.ErroresContabilizacion.Select(e => e.TextoOperador())) :
             " La nueva versión tiene trabajadores sin resolver: resuélvelos y contabiliza.");
        _contexto.Avisar(texto);
        if (r.Valor.ErroresContabilizacion.Count > 0) Fallo("Reemplazado, pero sin contabilizar", r.Valor.ErroresContabilizacion);
    }

    private static readonly (string Campo, string Texto)[] CamposComparados =
        [("Bruto", "Bruto"), ("DtoReEsp", "Dto. especie"), ("SSEmp", "SS empresa"), ("TC1", "TC1"), ("Autonomo", "Autónomo"), ("IRPF111", "IRPF"), ("Neto", "Neto"), ("CostEmp", "Coste empresa")];

    /// <summary>Cambios de importe por trabajador (por código, o por nombre si no hay código) entre dos versiones; y quién no cambia.</summary>
    internal static (IReadOnlyList<CambioReemplazo> Cambios, IReadOnlyList<string> Iguales) Comparar(IReadOnlyList<FilaNomina> antes, IReadOnlyList<FilaNomina> ahora)
    {
        static string Clave(FilaNomina f) => f.CodigoTrabajador is { Length: > 0 } c ? c : f.Nombre.Trim().ToUpperInvariant();
        var a = antes.GroupBy(Clave).ToDictionary(g => g.Key, g => g.First());
        var b = ahora.GroupBy(Clave).ToDictionary(g => g.Key, g => g.First());
        var cambios = new List<CambioReemplazo>();
        var iguales = new List<string>();
        foreach (var clave in a.Keys.Union(b.Keys))
        {
            var x = a.GetValueOrDefault(clave);
            var y = b.GetValueOrDefault(clave);
            var nombre = (y ?? x)!.Nombre;
            if (x is null) { cambios.Add(new(nombre, "Nuevo en esta versión", 0m, y!["Neto"])); continue; }
            if (y is null) { cambios.Add(new(nombre, "Ya no está en esta versión", x["Neto"], 0m)); continue; }
            var distintos = CamposComparados.Where(c => x[c.Campo] != y[c.Campo]).Select(c => new CambioReemplazo(nombre, c.Texto, x[c.Campo], y[c.Campo])).ToList();
            if (distintos.Count == 0) iguales.Add(nombre); else cambios.AddRange(distintos);
        }
        return (cambios, iguales);
    }

    // ---------- ver ----------

    private async Task VerAsientosAsync()
    {
        if (_contexto.VerAsiento is not { } ver) return;
        foreach (var p in _seleccion.Take(5))
            foreach (var id in (await _s.Consulta.NominasAsync(p.Id)).Select(n => n.IdAsiento).OfType<int>().Distinct().Take(12))
                await ver(id);
    }

    private async Task VerAsientoAsync()
    {
        if (_nomina?.Datos.IdAsiento is int id && _contexto.VerAsiento is { } ver) await ver(id);
    }

    // ---------- comunes ----------

    public static string TextoEstado(string estado) => estado switch
    {
        ServicioImportacionNominas.EstadoPendiente => "Pendiente",
        ServicioImportacionNominas.EstadoIncidencias => "Con incidencias",
        ServicioImportacionNominas.EstadoContabilizada => "Contabilizada",
        ServicioImportacionNominas.EstadoReemplazada => "Reemplazada",
        ServicioImportacionNominas.EstadoDeshecha => "Deshecha",
        _ => estado,
    };

    private async Task OcupadoAsync(string tituloError, Func<Task> trabajo)
    {
        if (_ocupado) return;
        _ocupado = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            await trabajo();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Informar("No he podido leer el fichero", $"{ex.Message} Comprueba que el fichero existe y que no lo tiene abierto Excel, y vuelve a intentarlo.");
        }
        catch (Exception ex)
        {
            Informar(tituloError, $"{ex.Message} Actualiza (F5) y vuelve a intentarlo.");
        }
        finally
        {
            _ocupado = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void Informar(string titulo, string mensaje, IReadOnlyList<string>? detalles = null) =>
        _dialogos.Preguntar(titulo, mensaje, detalles, new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));

    private void Fallo(string titulo, IReadOnlyList<Error> errores, string? extra = null) =>
        Informar(titulo, string.Join(" ", errores.Take(1).Select(e => e.TextoOperador())) + (extra is null ? "" : " " + extra),
            errores.Count > 1 ? errores.Skip(1).Select(e => e.TextoOperador()).ToList() : null);
}
