using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ProConta.Norma43.Importacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>Contexto de trabajo de la pantalla Bancos.</summary>
public sealed record ContextoBancos(ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar);

/// <summary>Alta propuesta de una cuenta bancaria desconocida, editable en el diálogo de la importación.</summary>
public sealed class AltaCuentaVM(CuentaDesconocida cuenta, IReadOnlyList<CuentaContableBanco> opciones) : ModeloVista
{
    private string _nombre = cuenta.NombreEnFichero ?? "";
    private string _iban = cuenta.IbanPropuesto ?? "";
    private CuentaContableBanco? _contable;

    public CuentaDesconocida Cuenta { get; } = cuenta;
    public string Ccc => Cuenta.Ccc;
    public string Divisa => Cuenta.Divisa;
    public string Nombre { get => _nombre; set => Fijar(ref _nombre, value ?? ""); }
    public string Iban { get => _iban; set => Fijar(ref _iban, value ?? ""); }
    public IReadOnlyList<CuentaContableBanco> Opciones { get; } = opciones;
    public CuentaContableBanco? Contable { get => _contable; set => Fijar(ref _contable, value); }
    public AltaCuentaBancaria Alta => new(Cuenta.Entidad, Cuenta.Oficina, Cuenta.Cuenta, Cuenta.Divisa, Nombre.Trim(), string.IsNullOrWhiteSpace(Iban) ? null : Iban.Trim(),
        Contable?.IdIdentidadContable);
}

/// <summary>Diálogos de la pantalla Bancos (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosBancos
{
    int Preguntar(string titulo, string mensaje, IReadOnlyList<string>? detalles, params OpcionMensaje[] opciones);
    /// <summary>Elige un fichero Norma 43; null si se cancela.</summary>
    string? ElegirFichero();
    /// <summary>Pide un texto obligatorio; null si se cancela.</summary>
    string? PedirTexto(string titulo, string explicacion, string etiqueta, string textoBoton);
    /// <summary>Muestra las cuentas desconocidas para darlas de alta; true si se aceptan (con los datos editados).</summary>
    bool DarDeAlta(IReadOnlyList<AltaCuentaVM> altas);
    void AbrirArchivo(string ruta);
}

/// <summary>Fila de la tabla de cuentas bancarias.</summary>
public sealed class FilaCuentaVM(CuentaBancariaN43 c) : ModeloVista
{
    public CuentaBancariaN43 Datos { get; } = c;
    public int Id => Datos.IdCuentaBancaria;
    public string Nombre => Datos.Nombre ?? "";
    public string Ccc => Datos.Ccc;
    public string Iban => Datos.Iban ?? "";
    public string Contable => Datos.CodigoContable is null ? "" : $"{Datos.CodigoContable} · {Datos.NombreContable}";
    public string SaldoBanco => Datos.SaldoBanco is decimal s ? Formatos.Importe(s) : "";
    public decimal SaldoBancoOrden => Datos.SaldoBanco ?? decimal.MinValue;
    public string FechaSaldo => Datos.FechaSaldoBanco is DateTime f ? Formatos.Fecha(f) : "";
    public string SaldoContable => Datos.SaldoContable is decimal s ? Formatos.Importe(s) : "";
    public decimal SaldoContableOrden => Datos.SaldoContable ?? decimal.MinValue;
    public string Diferencia => Datos.Diferencia is decimal d ? Formatos.ImporteOVacio(d) : "";
    public bool Descuadrada => Datos.Diferencia is decimal d && Math.Round(d, 2) != 0;
    public string AyudaDiferencia => Datos.Diferencia is decimal d
        ? (Descuadrada ? $"El banco dice {SaldoBanco} el {FechaSaldo} y la {Datos.CodigoContable} tiene {SaldoContable} en el ejercicio: faltan movimientos por contabilizar o hay apuntes posteriores" : "Saldo del banco y saldo contable coinciden")
        : Datos.CodigoContable is null ? "La cuenta bancaria no tiene asociada una 572 en este ejercicio" : "Aún no hay extractos importados";
    public string Movimientos => Datos.Movimientos == 0 ? "" : Formatos.Recuento(Datos.Movimientos);
    public int MovimientosOrden => Datos.Movimientos;
    public string Ultimo => Datos.UltimoMovimiento is DateTime f ? Formatos.Fecha(f) : "";
    public DateTime UltimoOrden => Datos.UltimoMovimiento ?? DateTime.MinValue;
    public string Lotes => Datos.Lotes == 0 ? "" : Formatos.Recuento(Datos.Lotes);
}

/// <summary>Fila de la tabla de lotes de la cuenta elegida.</summary>
public sealed class FilaLoteVM(LoteN43 l) : ModeloVista
{
    public LoteN43 Datos { get; } = l;
    public int Id => Datos.IdLote;
    public string Fichero => Datos.NombreFichero;
    public string Origen => Datos.Origen;
    public string Estado => Datos.Revertido ? "Revertido" : "Importado";
    public bool Revertido => Datos.Revertido;
    public string Periodo => $"{Formatos.Fecha(Datos.Desde)} – {Formatos.Fecha(Datos.Hasta)}";
    public DateTime PeriodoOrden => Datos.Hasta;
    public string Importado => Datos.FechaImportacion.ToString("dd/MM/yyyy HH:mm");
    public DateTime ImportadoOrden => Datos.FechaImportacion;
    public string Movimientos => Formatos.Recuento(Datos.Movimientos);
    public int MovimientosOrden => Datos.Movimientos;
    public string Avisos => Datos.Advertencias == 0 ? "" : Formatos.Recuento(Datos.Advertencias);
    public string AyudaEstado => Datos.Revertido
        ? $"Revertido el {Datos.FechaReversion:dd/MM/yyyy HH:mm} por {Datos.OperadorReversion}: {Datos.MotivoReversion}. Sus movimientos no cuentan; para recuperarlo, vuelve a importar el mismo fichero"
        : "Sus movimientos están en las líneas bancarias de la cuenta";
}

/// <summary>
/// Pantalla Bancos (norma43/bancos): una fila por cuenta bancaria de la sociedad con su saldo según el último extracto y según
/// su 572 del ejercicio, el número de movimientos, la fecha del último y sus lotes. «Importar N43…» en la cabecera; las
/// acciones sobre los lotes (revertir, abrir el fichero) en el menú contextual. Si el fichero trae una cuenta desconocida se
/// ofrece darla de alta en ese momento para seguir con la importación.
/// </summary>
public sealed class BancosVM : ModeloVista, IPestanaTrabajo
{
    private readonly ServicioImportacionN43 _servicio;
    private readonly ConsultaBancos _consulta;
    private readonly IDialogosBancos _dialogos;
    private readonly ContextoBancos _contexto;
    private readonly Action? _alCerrar;
    private FilaCuentaVM? _cuenta;
    private FilaLoteVM? _lote;
    private IReadOnlyList<IncidenciaLoteN43> _incidencias = [];
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _ocupado;
    private object? _vista;

    public BancosVM(ServicioImportacionN43 servicio, ConsultaBancos consulta, IDialogosBancos dialogos, ContextoBancos contexto, Action? alCerrar = null)
    {
        _servicio = servicio;
        _consulta = consulta;
        _dialogos = dialogos;
        _contexto = contexto;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(_cuenta?.Id));
        ImportarCommand = new ComandoDelegado(() => _ = ImportarAsync(), () => !_ocupado);
        RevertirCommand = new ComandoDelegado(() => _ = RevertirAsync(), () => !_ocupado && _lote is { Revertido: false });
        AbrirFicheroCommand = new ComandoDelegado(AbrirFichero, () => _lote?.Datos.RutaOrigen is { } r && File.Exists(r));
    }

    // ---------- pestaña ----------

    public string Titulo => "Bancos";
    public string Ayuda => $"Cuentas bancarias de {_contexto.RazonSocial}: saldos, movimientos y extractos Norma 43 importados";
    public string Icono => "bank";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaBancos { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- estado ----------

    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public string TituloEstado => Estado == EstadoVista.Vacio ? "Aún no hay cuentas bancarias" : "";
    public ObservableCollection<FilaCuentaVM> Cuentas { get; } = [];
    public ObservableCollection<FilaLoteVM> Lotes { get; } = [];
    public string Resumen => Cuentas.Count == 0 ? "Sin cuentas bancarias" :
        $"{Formatos.Plural(Cuentas.Count, "cuenta", "cuentas")} · saldo según los bancos {Formatos.Importe(Cuentas.Sum(c => c.Datos.SaldoBanco ?? 0m))} · " +
        $"{Formatos.Plural(Cuentas.Sum(c => c.Datos.Movimientos), "movimiento", "movimientos")}";
    public string TituloLotes => _cuenta is null ? "Extractos importados" : $"Extractos importados de {(_cuenta.Nombre.Length > 0 ? _cuenta.Nombre : _cuenta.Ccc)}";
    public bool SinLotes => _cuenta is not null && Lotes.Count == 0;

    public FilaCuentaVM? Cuenta
    {
        get => _cuenta;
        set
        {
            if (!Fijar(ref _cuenta, value)) return;
            Avisar(nameof(TituloLotes));
            _ = CargarLotesAsync();
        }
    }

    public FilaLoteVM? Lote
    {
        get => _lote;
        set
        {
            if (!Fijar(ref _lote, value)) return;
            Avisar(nameof(HayLote), nameof(DetalleLote));
            _ = CargarIncidenciasAsync(value);
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HayLote => _lote is not null;
    public string DetalleLote => _lote is not { } l ? "" : string.Join("\n", new[]
    {
        $"{l.Origen} · {l.Estado}",
        $"Periodo de esta cuenta: {l.Periodo}",
        $"Importado el {l.Importado}",
        $"{Formatos.Plural(l.Datos.Movimientos, "movimiento", "movimientos")} de esta cuenta" + (l.Datos.MovimientosLote != l.Datos.Movimientos ? $" ({l.Datos.MovimientosLote} en todo el fichero)" : ""),
        l.Datos.RutaOrigen is { } r ? $"Fichero: {r}" : "",
        l.Revertido ? $"Revertido el {l.Datos.FechaReversion:dd/MM/yyyy HH:mm} por {l.Datos.OperadorReversion}. Motivo: {l.Datos.MotivoReversion}" : "",
    }.Where(t => t.Length > 0));
    public IReadOnlyList<IncidenciaLoteN43> Incidencias { get => _incidencias; private set { if (Fijar(ref _incidencias, value)) Avisar(nameof(SinIncidencias)); } }
    public bool SinIncidencias => _incidencias.Count == 0;

    public ICommand RefrescarCommand { get; }
    public ICommand ImportarCommand { get; }
    public ICommand RevertirCommand { get; }
    public ICommand AbrirFicheroCommand { get; }

    public async Task CargarAsync(int? idCuenta = null)
    {
        try
        {
            if (Cuentas.Count == 0) Estado = EstadoVista.Cargando;
            var cuentas = await _consulta.CuentasAsync(_contexto.Trabajo);
            var id = idCuenta ?? _cuenta?.Id;
            Cuentas.Clear();
            foreach (var c in cuentas) Cuentas.Add(new FilaCuentaVM(c));
            Estado = Cuentas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
            MensajeEstado = Cuentas.Count == 0 ? "Importa un extracto Norma 43 (botón «Importar N43…»): sus cuentas se dan de alta en ese momento." : "";
            _cuenta = null;
            Cuenta = Cuentas.FirstOrDefault(c => c.Id == id) ?? Cuentas.FirstOrDefault();
            if (Cuenta is null) { Lotes.Clear(); Lote = null; }
            Avisar(nameof(Resumen), nameof(TituloEstado), nameof(SinLotes));
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer las cuentas bancarias: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    private async Task CargarLotesAsync()
    {
        var cuenta = _cuenta;
        var id = _lote?.Id;
        Lotes.Clear();
        Lote = null;
        if (cuenta is null) { Avisar(nameof(SinLotes)); return; }
        try
        {
            var lotes = await _consulta.LotesAsync(_contexto.Trabajo, cuenta.Id);
            if (_cuenta != cuenta) return;
            foreach (var l in lotes) Lotes.Add(new FilaLoteVM(l));
            Lote = Lotes.FirstOrDefault(l => l.Id == id) ?? Lotes.FirstOrDefault();
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No he podido leer los extractos de la cuenta: {ex.Message}";
        }
        Avisar(nameof(SinLotes));
    }

    private async Task CargarIncidenciasAsync(FilaLoteVM? lote)
    {
        if (lote is null) { Incidencias = []; return; }
        try
        {
            var i = await _consulta.IncidenciasAsync(lote.Id);
            if (_lote == lote) Incidencias = i;
        }
        catch (Exception ex)
        {
            MensajeEstado = $"No he podido leer las incidencias del lote: {ex.Message}";
        }
    }

    // ---------- importar ----------

    /// <summary>Importa un fichero elegido por el operador (o <paramref name="ruta"/>, en pruebas).</summary>
    public async Task ImportarAsync(string? ruta = null)
    {
        ruta ??= _dialogos.ElegirFichero();
        if (ruta is null) return;
        _ocupado = true;
        CommandManager.InvalidateRequerySuggested();
        try
        {
            var bytes = await File.ReadAllBytesAsync(ruta);
            var nombre = Path.GetFileName(ruta);
            var analisis = await _servicio.AnalizarAsync(_contexto.Trabajo, bytes);
            IReadOnlyList<AltaCuentaBancaria>? altas = null;
            if (analisis.Veredicto == VeredictoImportacion.CuentasDesconocidas)
            {
                var opciones = await _consulta.CuentasContablesBancoAsync(_contexto.Trabajo);
                var propuestas = analisis.Desconocidas.Select(d => new AltaCuentaVM(d, opciones)).ToList();
                if (!_dialogos.DarDeAlta(propuestas))
                {
                    _contexto.Avisar($"No se ha importado «{nombre}»: sus cuentas bancarias no están dadas de alta.");
                    return;
                }
                altas = propuestas.Select(p => p.Alta).ToList();
            }
            var r = await _servicio.ImportarAsync(_contexto.Trabajo, bytes, nombre, ruta, _contexto.Actor, altas);
            switch (r.Veredicto)
            {
                case VeredictoImportacion.Importable or VeredictoImportacion.Reactivable:
                {
                    var cuenta = await IdCuentaDeAsync(r);
                    await CargarAsync(cuenta);
                    _contexto.Avisar(r.Explicacion);
                    break;
                }
                case VeredictoImportacion.YaImportado:
                    _dialogos.Preguntar("Ya estaba importado", r.Explicacion, null, new OpcionMensaje("Entendido", "No se ha cambiado nada", Principal: true, Cancelar: true));
                    break;
                default:
                    _dialogos.Preguntar($"No se ha importado «{nombre}»", r.Explicacion, Detalles(r.Analisis),
                        new OpcionMensaje("Entendido", "No se ha importado nada; corrige lo que dice el aviso y vuelve a importar", Principal: true, Cancelar: true));
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialogos.Preguntar("No he podido leer el fichero", $"{ex.Message} Comprueba que el fichero existe y que no lo tiene abierto otro programa, y vuelve a importarlo.", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
        catch (Exception ex)
        {
            _dialogos.Preguntar("No se ha podido importar", $"{ex.Message} No se ha guardado nada. Actualiza (F5) y vuelve a intentarlo.", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
        finally
        {
            _ocupado = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task<int?> IdCuentaDeAsync(ResultadoImportacionN43 r)
    {
        var primero = r.Analisis.Fichero.Extractos.FirstOrDefault();
        if (primero is null) return null;
        var cuentas = await _consulta.CuentasAsync(_contexto.Trabajo);
        return cuentas.FirstOrDefault(c => c.Entidad == primero.Entidad && c.Oficina == primero.Oficina && c.Cuenta == primero.Cuenta)?.IdCuentaBancaria;
    }

    private static IReadOnlyList<string>? Detalles(AnalisisN43 a) => a.Veredicto switch
    {
        VeredictoImportacion.Invalido => a.Fichero.Errores.Select(e => e.Texto).ToList(),
        VeredictoImportacion.Solape => a.Solapes.Select(s => s.Texto).ToList(),
        _ => null,
    };

    // ---------- revertir ----------

    private async Task RevertirAsync()
    {
        if (_lote is not { } lote) return;
        var motivo = _dialogos.PedirTexto("Revertir el lote",
            $"Revertir «{lote.Fichero}» deja sus {lote.Movimientos} movimientos fuera de las líneas bancarias y de los saldos, sin borrar nada: queda anotado con tu nombre, la hora y el motivo. " +
            "Si sus líneas ya tienen cuenta asignada o asiento, no se puede revertir.", "Motivo", "Revertir");
        if (motivo is null) return;
        _ocupado = true;
        try
        {
            var r = await _servicio.RevertirAsync(_contexto.Trabajo, lote.Id, _contexto.Actor, motivo);
            if (r.EsCorrecto)
            {
                await CargarAsync(_cuenta?.Id);
                _contexto.Avisar($"Lote «{lote.Fichero}» revertido. Para recuperarlo, vuelve a importar el mismo fichero.");
            }
            else
                _dialogos.Preguntar("No se puede revertir", r.Error!.TextoOperador(), null, new OpcionMensaje("Entendido", "No se ha cambiado nada", Principal: true, Cancelar: true));
        }
        catch (Exception ex)
        {
            _dialogos.Preguntar("No se ha podido revertir", $"{ex.Message} No se ha cambiado nada. Actualiza (F5) y vuelve a intentarlo.", null,
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true, Cancelar: true));
        }
        finally
        {
            _ocupado = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void AbrirFichero()
    {
        if (_lote?.Datos.RutaOrigen is { } r && File.Exists(r)) _dialogos.AbrirArchivo(r);
    }
}
