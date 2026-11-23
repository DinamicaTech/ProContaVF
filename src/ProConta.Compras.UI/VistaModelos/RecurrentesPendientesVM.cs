using System.IO;
using System.Windows.Input;
using ProConta.Compras.Recurrentes;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Compras.UI.VistaModelos;

/// <summary>Una factura recurrente pendiente con su casilla: un clic en la fila la marca o la desmarca.</summary>
public sealed class FilaRecurrenteVM(FacturaRecurrentePendiente datos, Action alMarcar) : ModeloVista
{
    private bool _marcado;

    public FacturaRecurrentePendiente Datos { get; } = datos;
    public string Proveedor => Datos.Proveedor;
    public string Periodicidad => DetectorFacturasRecurrentes.Texto(Datos.Periodicidad) + (Datos.PeriodicidadFijada ? " (ficha)" : "");
    public string Periodo => TextoPeriodo(Datos.Periodo);
    public DateTime PeriodoOrden => Datos.Periodo;
    public string Importe => Formatos.Importe(Datos.ImporteEstimado);
    public decimal ImporteOrden => Datos.ImporteEstimado;
    public string Ultima => Formatos.Fecha(Datos.FechaUltima);
    public DateTime UltimaOrden => Datos.FechaUltima;
    public bool Descartada => Datos.Descartada;
    public string Estado => Datos.Descartada ? "No llegará" : "Pendiente";

    public string AyudaPeriodicidad => Datos.PeriodicidadFijada
        ? "Periodicidad fijada en la ficha del proveedor (Roles › Proveedor › Facturas periódicas)."
        : "Periodicidad deducida de sus facturas: al menos 3 seguidas, desde la más reciente, con este intervalo. Se puede fijar o quitar del control en su ficha.";

    public string AyudaFila =>
        $"{Datos.Proveedor}: su última factura es del {Ultima} ({Importe}); con periodicidad {DetectorFacturasRecurrentes.Texto(Datos.Periodicidad).ToLowerInvariant()}, " +
        $"la siguiente se esperaba el {Formatos.Fecha(Datos.FechaEsperada)} y no está registrada." +
        (Datos.Descartada ? " Marcada «No llegará»: clic derecho › «Volver a esperarla» para que vuelva a contar." : " Doble clic: abrir el PDF de la última factura.");

    public bool Marcado { get => _marcado; set { if (Fijar(ref _marcado, value)) alMarcar(); } }

    /// <summary>«Noviembre 2026».</summary>
    public static string TextoPeriodo(DateTime periodo)
    {
        var t = periodo.ToString("MMMM yyyy", Formatos.Cultura);
        return t.Length == 0 ? t : char.ToUpper(t[0], Formatos.Cultura) + t[1..];
    }
}

/// <summary>
/// Compras › Facturas recurrentes pendientes (compras/facturas-recurrentes-pendientes 2026-11-23 18:55): facturas de proveedores mensuales,
/// bimestrales o trimestrales que deberían haber llegado y no están registradas, con el importe de la última. Solo para revisar: no genera
/// asientos ni registra facturas. Cabecera: Actualizar, Exportar a Excel y «Mostrar descartadas»; menú contextual sobre lo marcado.
/// </summary>
public sealed class RecurrentesPendientesVM : ModeloVista, IPestanaTrabajo
{
    private readonly ModuloCompras _modulo;
    private readonly ServicioFacturasRecurrentes _servicio;
    private object? _vista;
    private IReadOnlyList<FilaRecurrenteVM> _filas = [];
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _mostrarDescartadas;
    private bool _ocupado;
    private bool _marcando;

    internal RecurrentesPendientesVM(ModuloCompras modulo, ServicioFacturasRecurrentes servicio)
    {
        _modulo = modulo;
        _servicio = servicio;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(), () => !_ocupado);
        AbrirPdfCommand = new ComandoDelegado(p => AbrirPdf(p), p => Unica(p) is not null);
        VerFacturaCommand = new ComandoDelegado(p => { if (Unica(p) is { } f) _modulo.AbrirFactura(f.Datos.IdRegistroUltima); }, p => Unica(p) is not null);
        AbrirFichaCommand = new ComandoDelegado(p => { if (Unica(p) is { } f) _modulo.AbrirFichaTercero(f.Datos.IdTercero); }, p => Unica(p) is not null);
        NoLlegaraCommand = new ComandoDelegado(_ => _ = DescartarAsync(), _ => !_ocupado && Marcadas.Any(f => !f.Descartada));
        VolverAEsperarCommand = new ComandoDelegado(_ => _ = DeshacerAsync(), _ => !_ocupado && Marcadas.Any(f => f.Descartada));
    }

    // ---------- pestaña ----------
    public string Titulo => "Recurrentes pendientes";
    public string Ayuda => $"Facturas recurrentes de proveedores pendientes de recibir de {_modulo.Servicios.RazonSocial}";
    public string Icono => "scan";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new Vistas.VistaRecurrentesPendientes { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _modulo.Cerrada(this);

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand AbrirPdfCommand { get; }
    public ICommand VerFacturaCommand { get; }
    public ICommand AbrirFichaCommand { get; }
    public ICommand NoLlegaraCommand { get; }
    public ICommand VolverAEsperarCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_modulo.Servicios.RazonSocial} · facturas de los últimos 12 meses hasta el {Formatos.Fecha(_servicio.Hoy)}";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public bool MostrarDescartadas { get => _mostrarDescartadas; set { if (Fijar(ref _mostrarDescartadas, value)) _ = CargarAsync(); } }

    // ---------- filas ----------
    public IReadOnlyList<FilaRecurrenteVM> Filas { get => _filas; private set { _filas = value; Avisar(nameof(Filas)); } }
    public IReadOnlyList<FilaRecurrenteVM> Marcadas => _filas.Where(f => f.Marcado).ToList();
    public bool TodosMarcados { get => _filas.Count > 0 && _filas.All(f => f.Marcado); set => MarcarTodos(value); }
    private IEnumerable<FilaRecurrenteVM> Pendientes => _filas.Where(f => !f.Descartada);

    public string Recuento => Formatos.Plural(Pendientes.Count(), "factura pendiente", "facturas pendientes")
        + (_filas.Any(f => f.Descartada) ? $" · {Formatos.Plural(_filas.Count(f => f.Descartada), "descartada", "descartadas")}" : "")
        + (Marcadas.Count > 0 ? $" · {Formatos.Plural(Marcadas.Count, "marcada", "marcadas")}" : "");

    public string TotalEstimado => Formatos.Importe(Pendientes.Sum(f => f.Datos.ImporteEstimado));
    public bool HayTotal => Pendientes.Any();

    public async Task CargarAsync()
    {
        if (Ocupado) return;
        try
        {
            if (_filas.Count == 0) Estado = EstadoVista.Cargando;
            Ocupado = true;
            var marcados = _filas.Where(f => f.Marcado).Select(f => (f.Datos.IdProveedor, f.Datos.Periodo)).ToHashSet();
            var lista = await _servicio.PendientesAsync(_modulo.Servicios.Trabajo.IdSociedad, _mostrarDescartadas);
            Filas = lista.Select(d => new FilaRecurrenteVM(d, AlMarcar) { Marcado = marcados.Contains((d.IdProveedor, d.Periodo)) }).ToList();
            Estado = _filas.Count == 0 ? EstadoVista.Vacio : EstadoVista.Contenido;
            MensajeEstado = _filas.Count == 0
                ? "No falta ninguna factura recurrente: todos los proveedores mensuales, bimestrales y trimestrales tienen registrada la de su último periodo." +
                  (_mostrarDescartadas ? "" : " Marca «Mostrar descartadas» para ver las que marcaste «No llegará».")
                : "";
            Avisar(nameof(Subtitulo));
            AlMarcar();
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer las facturas de compra: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private void AlMarcar()
    {
        if (_marcando) return;
        Avisar(nameof(Recuento), nameof(TodosMarcados), nameof(TotalEstimado), nameof(HayTotal));
        CommandManager.InvalidateRequerySuggested();
    }

    private void MarcarTodos(bool marcar)
    {
        _marcando = true;
        foreach (var f in _filas) f.Marcado = marcar;
        _marcando = false;
        AlMarcar();
    }

    /// <summary>Clic derecho en una fila sin marcar: se queda marcada solo ella.</summary>
    public void MarcarSolo(FilaRecurrenteVM fila)
    {
        if (fila.Marcado) return;
        _marcando = true;
        foreach (var f in _filas) f.Marcado = ReferenceEquals(f, fila);
        _marcando = false;
        AlMarcar();
    }

    private FilaRecurrenteVM? Unica(object? p) => p as FilaRecurrenteVM ?? (Marcadas is { Count: 1 } m ? m[0] : null);

    private void AbrirPdf(object? p)
    {
        if (Unica(p) is not { } f) return;
        if (f.Datos.RutaPdfUltima is { Length: > 0 } ruta && File.Exists(ruta))
        {
            _modulo.Dialogos.Abrir(ruta);
            return;
        }
        var opcion = _modulo.Dialogos.Preguntar("No encuentro el PDF",
            f.Datos.RutaPdfUltima is { Length: > 0 } r
                ? $"El PDF de la última factura de {f.Proveedor} ({f.Ultima}) no está en {r}: se ha movido o borrado."
                : $"La última factura de {f.Proveedor} ({f.Ultima}) no tiene PDF guardado.",
            ["En Facturas recibidas puedes ver la factura y, con clic derecho, sustituir o localizar su PDF."],
            new OpcionMensaje("Ver en Facturas recibidas", "Abre Facturas recibidas con esa factura a la vista", Principal: true),
            new OpcionMensaje("Cerrar", "Cierra el aviso"));
        if (opcion == 0) _modulo.AbrirFactura(f.Datos.IdRegistroUltima);
    }

    private async Task DescartarAsync()
    {
        var filas = Marcadas.Where(f => !f.Descartada).ToList();
        if (filas.Count == 0) return;
        Ocupado = true;
        try
        {
            var n = await _servicio.DescartarAsync(_modulo.Servicios.Trabajo.IdSociedad, filas.Select(f => (f.Datos.IdProveedor, f.Datos.Periodo)).ToList(),
                _modulo.Servicios.Operador ?? new Nucleo.Auditoria.Actor(Environment.UserName));
            _modulo.Avisar($"{Formatos.Plural(n, "periodo descartado", "periodos descartados")}: ya no salen como pendientes ni en Inicio. «Mostrar descartadas» los enseña para volver a esperarlos.");
        }
        catch (Exception ex)
        {
            _modulo.Dialogos.Preguntar("No he podido descartarlas", $"{ex.Message}", ["Pulsa Actualizar (F5) y vuelve a intentarlo."],
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }

    private async Task DeshacerAsync()
    {
        var filas = Marcadas.Where(f => f.Descartada).ToList();
        if (filas.Count == 0) return;
        Ocupado = true;
        try
        {
            var n = await _servicio.DeshacerDescarteAsync(_modulo.Servicios.Trabajo.IdSociedad, filas.Select(f => (f.Datos.IdProveedor, f.Datos.Periodo)).ToList());
            _modulo.Avisar($"{Formatos.Plural(n, "periodo vuelve", "periodos vuelven")} a esperarse como pendiente.");
        }
        catch (Exception ex)
        {
            _modulo.Dialogos.Preguntar("No he podido deshacer el descarte", $"{ex.Message}", ["Pulsa Actualizar (F5) y vuelve a intentarlo."],
                new OpcionMensaje("Entendido", "Cierra el aviso", Principal: true));
        }
        finally { Ocupado = false; }
        await CargarAsync();
    }
}
