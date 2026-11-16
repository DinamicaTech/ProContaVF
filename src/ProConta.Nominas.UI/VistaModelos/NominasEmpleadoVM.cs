using System.Collections.ObjectModel;
using System.Windows.Input;
using ProConta.Nominas.Consulta;
using ProConta.Nominas.Importacion;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Nominas.UI.VistaModelos;

/// <summary>Una nómina del empleado en la pestaña Nóminas de su ficha.</summary>
public sealed class FilaNominaEmpleadoVM(NominaEmpleado datos) : ModeloVista
{
    public NominaEmpleado Datos { get; } = datos;
    private Lectura.FilaNomina F => Datos.Nomina.Fila;
    public int IdImportacion => Datos.Importacion.IdImportacion;
    public int? IdAsiento => Datos.Nomina.IdAsiento;
    public int Ejercicio => Datos.Ejercicio;
    public string Periodo => Datos.Importacion.TextoPeriodo;
    public DateTime PeriodoOrden => Datos.Importacion.Mes;
    public string TipoPaga => F.TipoPaga == Lectura.TipoPaga.Extra ? "Extra" : "Mensual";
    public string AyudaTipoPaga => Datos.Importacion.TextoPaga ?? TipoPaga;
    public string Fecha => Formatos.Fecha(F.FechaNomina);
    public DateTime FechaOrden => F.FechaNomina;
    public string Bruto => Formatos.ImporteOVacio(F["Bruto"]);
    public decimal BrutoOrden => F["Bruto"];
    public string DtoEspecie => Formatos.ImporteOVacio(F["DtoReEsp"]);
    public string Autonomo => Formatos.ImporteOVacio(F["Autonomo"]);
    public string Irpf => Formatos.ImporteOVacio(F["IRPF111"]);
    public decimal IrpfOrden => F["IRPF111"];
    public string CuotasObreras => Formatos.ImporteOVacio(F.CuotasObreras);
    public string Neto => Formatos.ImporteOVacio(F["Neto"]);
    public decimal NetoOrden => F["Neto"];
    public string Coste => Formatos.ImporteOVacio(F["CostEmp"]);
    public string Asiento => IdAsiento?.ToString() ?? "";
    public string Estado => NominasVM.TextoEstado(Datos.Importacion.Estado);
    public string ClaseEstado => Datos.Importacion.Estado;
    public string AyudaEstado => Datos.Importacion.Origen == OrigenNominas.Migracion
        ? "Histórico migrado de ProConta antiguo"
        : $"Fichero {Datos.Importacion.NombreFichero} · importado el {Datos.Importacion.FechaHoraImportacion:dd/MM/yyyy HH:mm}";
}

/// <summary>Opción del filtro de años de la pestaña (cada ejercicio con nóminas, y «Todos»).</summary>
public sealed class OpcionAnyoVM(string texto, int? ejercicio, Action<OpcionAnyoVM> elegir) : ModeloVista
{
    private bool _marcado;
    public string Texto { get; } = texto;
    public int? Ejercicio { get; } = ejercicio;
    public bool Marcado
    {
        get => _marcado;
        set { if (Fijar(ref _marcado, value) && value) elegir(this); }
    }
}

/// <summary>
/// Pestaña «Nóminas» de la ficha del tercero (nominas/pantallas, maqueta validada 2026-11-13): solo si es empleado de la sociedad
/// activa. Código en la gestoría, cuenta 465, primera nómina, totales del año (para el certificado de retenciones y el 190) y
/// sus nóminas con su asiento.
/// </summary>
public sealed class NominasEmpleadoVM : ModeloVista
{
    private readonly ContextoNominas _contexto;
    private readonly Action<int>? _abrirPeriodo;
    private readonly IReadOnlyList<NominaEmpleado> _todas;
    private OpcionAnyoVM? _anyo;
    private FilaNominaEmpleadoVM? _actual;

    public NominasEmpleadoVM(EmpleadoNominas empleado, IReadOnlyList<NominaEmpleado> nominas, ContextoNominas contexto, Action<int>? abrirPeriodo)
    {
        Empleado = empleado;
        _todas = nominas;
        _contexto = contexto;
        _abrirPeriodo = abrirPeriodo;
        foreach (var e in nominas.Select(n => n.Ejercicio).Distinct().OrderBy(e => e)) Anyos.Add(new OpcionAnyoVM(e.ToString(), e, Elegir));
        if (Anyos.Count > 1) Anyos.Add(new OpcionAnyoVM("Todos", null, Elegir));
        (Anyos.FirstOrDefault(a => a.Ejercicio == contexto.Trabajo.Ejercicio) ?? Anyos.LastOrDefault(a => a.Ejercicio is not null))?.Marcado = true;
        if (_anyo is null) Mostrar();
        VerAsientoCommand = new ComandoDelegado(() => { if (_actual?.IdAsiento is int id) _ = _contexto.VerAsiento?.Invoke(id); },
            () => _actual?.IdAsiento is not null && _contexto.VerAsiento is not null);
        AbrirPeriodoCommand = new ComandoDelegado(() => { if (_actual is { } a) _abrirPeriodo?.Invoke(a.IdImportacion); },
            () => _actual is { } a && a.Ejercicio == _contexto.Trabajo.Ejercicio && _abrirPeriodo is not null);
    }

    /// <summary>Crea el contenido de la pestaña para el tercero, o null si no es empleado de la sociedad activa.</summary>
    public static async Task<object?> CrearAsync(ConsultaNominas consulta, ContextoNominas contexto, int idTercero, Action<int>? abrirPeriodo)
    {
        if (await consulta.EmpleadoAsync(contexto.Trabajo, idTercero) is not { } empleado) return null;
        var nominas = await consulta.DeEmpleadoAsync(contexto.Trabajo, empleado.IdEmpleado);
        return new Vistas.VistaNominasEmpleado { DataContext = new NominasEmpleadoVM(empleado, nominas, contexto, abrirPeriodo) };
    }

    public EmpleadoNominas Empleado { get; }
    public string Titulo => $"Empleado en {_contexto.RazonSocial}" + (Empleado.Activo ? "" : " (de baja)");
    public string Codigo => Empleado.CodigoTrabajador ?? "—";
    public string Cuenta => Empleado.Cuenta465 ?? "—";
    public string NombreCuenta => Empleado.NombreCuenta465 ?? "";
    public string PrimeraNomina => Empleado.PrimeraNomina is DateTime f ? Formatos.Fecha(f) : "—";
    public ObservableCollection<OpcionAnyoVM> Anyos { get; } = [];
    public bool HayAnyos => Anyos.Count > 1;
    public ObservableCollection<FilaNominaEmpleadoVM> Filas { get; } = [];
    public FilaNominaEmpleadoVM? Actual
    {
        get => _actual;
        set { if (Fijar(ref _actual, value)) CommandManager.InvalidateRequerySuggested(); }
    }

    public ICommand VerAsientoCommand { get; }
    public ICommand AbrirPeriodoCommand { get; }

    private string Sufijo => _anyo?.Ejercicio is int e ? e.ToString() : Anyos.Count > 1 ? $"{Anyos[0].Texto}–{Anyos[^2].Texto}" : "";
    private decimal Suma(string campo) => Filas.Sum(f => f.Datos.Nomina.Fila[campo]);
    public string TituloBruto => $"Bruto · {Sufijo}";
    public string TituloIrpf => $"IRPF retenido · {Sufijo}";
    public string TituloNeto => $"Neto · {Sufijo}";
    public string TituloCoste => $"Coste empresa · {Sufijo}";
    public string TotalBruto => Formatos.Importe(Suma("Bruto"));
    public string TotalIrpf => Formatos.Importe(Suma("IRPF111"));
    public string TotalNeto => Formatos.Importe(Suma("Neto"));
    public string TotalCoste => Formatos.Importe(Suma("CostEmp"));
    public string Resumen => Formatos.Plural(Filas.Count, "nómina", "nóminas");
    public string TituloVacio => _todas.Count == 0 ? "Aún no tiene nóminas en la sociedad" : "";

    private void Elegir(OpcionAnyoVM opcion)
    {
        _anyo = opcion;
        foreach (var a in Anyos.Where(a => a != opcion)) a.Marcado = false;
        Mostrar();
    }

    private void Mostrar()
    {
        Filas.Clear();
        foreach (var n in _todas.Where(n => _anyo?.Ejercicio is not int e || n.Ejercicio == e)) Filas.Add(new FilaNominaEmpleadoVM(n));
        Actual = Filas.FirstOrDefault();
        Avisar(nameof(TituloBruto), nameof(TituloIrpf), nameof(TituloNeto), nameof(TituloCoste), nameof(TotalBruto), nameof(TotalIrpf), nameof(TotalNeto), nameof(TotalCoste),
            nameof(Resumen), nameof(TituloVacio));
    }
}
