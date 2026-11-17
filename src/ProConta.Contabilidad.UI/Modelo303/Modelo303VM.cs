using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Input;
using ProConta.Contabilidad.Modelos;
using ProConta.Contabilidad.Presentacion;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Modelo303;

/// <summary>Contexto de la pantalla Modelo 303 y lo que abre fuera de ella.</summary>
public sealed record ContextoModelo303(Servicio303 Servicio, ContextoSociedadEjercicio Trabajo, string RazonSocial, Actor Actor, Action<string> Avisar)
{
    /// <summary>Abre un fichero (el PDF) con el programa del equipo.</summary>
    public Action<string>? AbrirFichero { get; init; }
    /// <summary>Abre la liquidación de IVA en un trimestre (después de presentar, lo liquidado pasa a ser lo presentado).</summary>
    public Action<int>? AbrirLiquidacion { get; init; }
}

/// <summary>Diálogos de la pantalla (en pruebas se sustituyen por respuestas fijas).</summary>
public interface IDialogosModelo303
{
    /// <summary>Ventana de conformidad: el operador ve el PDF y los datos y marca «Conforme». True si confirma la presentación.</summary>
    bool Conformidad(BorradorAeat borrador, Action verPdf);
    /// <summary>Pide el NRC del pago (forma I). Null si se cancela.</summary>
    string? PedirNrc(string titulo, string explicacion);
    void Informar(string titulo, string mensaje, IReadOnlyList<string>? detalles = null);
    void Error(string titulo, string mensaje);
}

/// <summary>Fila de la lista: el 303 de un trimestre.</summary>
public sealed class FilaTrimestre303VM(Trimestre303 datos) : ModeloVista
{
    public Trimestre303 Datos { get; } = datos;
    public int Trimestre => Datos.Trimestre;
    public string Nombre => Iva.CalculoLiquidacionIva.Nombre(Datos.Trimestre);
    public string Fin => Formatos.Fecha(Datos.Fin);
    public string Estado => Datos.Estado switch
    {
        Estado303.EnCurso => "En curso",
        Estado303.SinPresentar => "Sin presentar",
        Estado303.Presentado => "Presentado",
        Estado303.Complementaria => "Complementaria",
        _ => "Desactualizado",
    };
    public string ClaseEstado => Datos.Estado.ToString().ToUpperInvariant();
    public string AyudaEstado => Datos.Estado switch
    {
        Estado303.EnCurso => $"El trimestre termina el {Fin}: el 303 se presenta a partir del día siguiente",
        Estado303.SinPresentar => Datos.Bloqueos.Count > 0 ? $"Sin presentar. Antes hay que resolver: {Datos.Bloqueos[0]}" : "Sin presentar desde el programa: clic derecho › «Presentar…»",
        Estado303.Presentado => $"Presentado el {Datos.Ultima!.FechaHora:dd/MM/yyyy HH:mm} con CSV {Datos.Ultima.Csv}",
        Estado303.Complementaria => $"Rectificado con una complementaria el {Datos.Ultima!.FechaHora:dd/MM/yyyy HH:mm} (CSV {Datos.Ultima.Csv})",
        _ => Datos.Avisos.LastOrDefault() ?? "El 303 calculado ya no coincide con el presentado",
    };
    public string Calculado => Datos.Terminado ? Formatos.Importe(Datos.ResultadoCalculado) : "";
    public string Presentado => Datos.Ultima is { } u ? Formatos.Importe(u.Resultado) : "";
    public string FormaPago => Datos.Ultima?.FormaPago is { } f ? TextoForma(f) : Datos.Terminado ? TextoForma(Datos.FormaPago.ToString()) : "";
    public string Csv => Datos.Ultima?.Csv ?? "";
    public string Justificante => Datos.Ultima?.Justificante ?? "";
    public string Fecha => Datos.Ultima is { } u ? u.FechaHora.ToString("dd/MM/yyyy HH:mm") : "";
    public bool Presentada => Datos.Ultima is not null;

    private static string TextoForma(string f) => f switch
    {
        "U" => "Domiciliación", "I" => "Ingreso (NRC)", "C" => "A compensar", "N" => "Sin resultado", "D" => "Devolución", _ => f,
    };
}

/// <summary>Una casilla del 303 del trimestre elegido.</summary>
public sealed record FilaCasilla303VM(string Casilla, string Concepto, string Importe, bool Total);

/// <summary>
/// Pantalla Contabilidad › Modelo 303 (contabilidad/modelos-tributarios/modelo-303): los cuatro trimestres del ejercicio de trabajo
/// con su estado (en curso, sin presentar, presentado con su CSV, complementaria o desactualizado) y, del elegido, sus casillas, avisos
/// y lo que impide presentarlo. Acción general en la cabecera (Actualizar); menú contextual del trimestre: «Ver el PDF», «Presentar…»,
/// «Presentar complementaria…» y «Abrir el justificante».
/// </summary>
public sealed class Modelo303VM : ModeloVista, IPestanaTrabajo
{
    private readonly ContextoModelo303 _ctx;
    private readonly IDialogosModelo303 _dialogos;
    private readonly Action? _alCerrar;
    private object? _vista;
    private bool _ocupado;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private string _entorno = "";
    private FilaTrimestre303VM? _seleccion;
    private IReadOnlyList<FilaCasilla303VM> _casillas = [];
    private string _textoDetalle = "";

    public Modelo303VM(ContextoModelo303 ctx, IDialogosModelo303 dialogos, Action? alCerrar = null)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        _alCerrar = alCerrar;
        RefrescarCommand = new ComandoDelegado(() => _ = CargarAsync(_seleccion?.Trimestre), () => !_ocupado);
        VerPdfCommand = new ComandoDelegado(() => _ = VerPdfAsync(), () => !_ocupado && _seleccion is { Datos.Terminado: true });
        PresentarCommand = new ComandoDelegado(() => _ = PresentarAsync(false), () => !_ocupado && _seleccion is { Datos.Terminado: true, Presentada: false });
        ComplementariaCommand = new ComandoDelegado(() => _ = PresentarAsync(true), () => !_ocupado && _seleccion is { Presentada: true });
        JustificanteCommand = new ComandoDelegado(() => _ = AbrirJustificanteAsync(), () => !_ocupado && _seleccion is { Presentada: true });
        LiquidacionCommand = new ComandoDelegado(() => _ctx.AbrirLiquidacion?.Invoke(_seleccion!.Trimestre), () => _ctx.AbrirLiquidacion is not null && _seleccion is { Datos.Terminado: true });
    }

    // ---------- pestaña ----------
    public string Titulo => "Modelo 303";
    public string Ayuda => $"Autoliquidación trimestral del IVA de {_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}";
    public string Icono => "doc";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista ??= new PantallaModelo303 { DataContext = this };
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _alCerrar?.Invoke();

    // ---------- comandos ----------
    public ICommand RefrescarCommand { get; }
    public ICommand VerPdfCommand { get; }
    public ICommand PresentarCommand { get; }
    public ICommand ComplementariaCommand { get; }
    public ICommand JustificanteCommand { get; }
    public ICommand LiquidacionCommand { get; }

    // ---------- estado ----------
    public string Subtitulo => $"{_ctx.RazonSocial} · {_ctx.Trabajo.Ejercicio}" + (_entorno.Length > 0 ? $" · {_entorno}" : "");
    public bool EsPruebas { get; private set; }
    public string AyudaEntorno => EsPruebas
        ? "Esta BD es la copia de pruebas: el 303 se presenta en el entorno de pruebas de la AEAT (sin efectos, días laborables de 8:00 a 15:00) y nunca en el real"
        : "Esta BD es la de producción: el 303 se presenta de verdad en la AEAT";
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public ObservableCollection<FilaTrimestre303VM> Trimestres { get; } = [];
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public FilaTrimestre303VM? Seleccion
    {
        get => _seleccion;
        set
        {
            if (!Fijar(ref _seleccion, value)) return;
            Avisar(nameof(Avisos), nameof(HayAvisos), nameof(Bloqueos), nameof(HayBloqueos), nameof(TituloDetalle));
            _ = CargarCasillasAsync();
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string TituloDetalle => _seleccion is null ? "" : $"Casillas del {_seleccion.Nombre} {_ctx.Trabajo.Ejercicio}" +
        (_seleccion.Datos.Terminado ? $" · resultado {Formatos.Importe(_seleccion.Datos.ResultadoAutoliquidacion)}" : " (provisional: el trimestre no ha terminado)");
    public IReadOnlyList<string> Avisos => _seleccion?.Datos.Avisos ?? [];
    public bool HayAvisos => Avisos.Count > 0;
    public IReadOnlyList<string> Bloqueos => _seleccion?.Datos.Bloqueos ?? [];
    public bool HayBloqueos => Bloqueos.Count > 0;
    public IReadOnlyList<FilaCasilla303VM> Casillas { get => _casillas; private set => Fijar(ref _casillas, value); }
    public string TextoDetalle { get => _textoDetalle; private set => Fijar(ref _textoDetalle, value); }

    // ---------- carga ----------
    public async Task CargarAsync(int? trimestre = null)
    {
        try
        {
            if (Trimestres.Count == 0) Estado = EstadoVista.Cargando;
            var entorno = await _ctx.Servicio.Presentacion.EntornoAsync();
            EsPruebas = entorno == EntornoAeat.Pruebas;
            _entorno = EsPruebas ? "entorno de PRUEBAS de la AEAT" : "AEAT real (producción)";
            Avisar(nameof(Subtitulo), nameof(EsPruebas), nameof(AyudaEntorno));
            var lista = await _ctx.Servicio.TrimestresAsync(_ctx.Trabajo);
            Trimestres.Clear();
            foreach (var t in lista) Trimestres.Add(new FilaTrimestre303VM(t));
            Estado = EstadoVista.Contenido;
            var elegido = trimestre is int n ? Trimestres.FirstOrDefault(f => f.Trimestre == n)
                : Trimestres.FirstOrDefault(f => f.Datos.Estado is Estado303.SinPresentar or Estado303.Desactualizado) ?? Trimestres.LastOrDefault(f => f.Datos.Terminado);
            _seleccion = null;
            Seleccion = elegido;
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido calcular el 303: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
    }

    private async Task CargarCasillasAsync()
    {
        var fila = _seleccion;
        Casillas = [];
        TextoDetalle = "";
        if (fila is null) return;
        if (!fila.Datos.Terminado) { TextoDetalle = $"El {fila.Nombre} aún no ha terminado: sus casillas se ven a partir del {fila.Datos.Fin.AddDays(1):dd/MM/yyyy}."; return; }
        try
        {
            var r = await _ctx.Servicio.DeclaracionAsync(_ctx.Trabajo, fila.Trimestre, complementaria: false);
            if (!ReferenceEquals(fila, _seleccion)) return;
            if (!r.EsCorrecto) { TextoDetalle = r.Error!.TextoOperador(); return; }
            var d = r.Valor.Declaracion;
            Casillas = ProConta.Contabilidad.Modelos.Modelo303.Conceptos.Where(c => d.Casilla(c.Casilla) != 0m || c.Casilla is 27 or 45 or 46 or 71)
                .Select(c => new FilaCasilla303VM(c.Casilla.ToString("00"), c.Concepto, c.Casilla == 65 ? $"{d.Casilla(65):0.00} %" : Formatos.Importe(d.Casilla(c.Casilla)),
                    c.Casilla is 27 or 45 or 46 or 69 or 71)).ToList();
            TextoDetalle = $"Forma de pago: {d.FormaPago}" + (d.TipoDeclaracion == 'U' && d.Declarante.Iban is { } iban ? $" en {Nucleo.Identificacion.Ibanes.Agrupar(iban)}" : "") + ".";
        }
        catch (Exception ex)
        {
            TextoDetalle = $"No he podido calcular las casillas: {ex.Message}";
        }
    }

    // ---------- acciones ----------
    private async Task VerPdfAsync()
    {
        if (_seleccion is not { } fila) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeclaracionAsync(_ctx.Trabajo, fila.Trimestre, complementaria: false);
            if (!r.EsCorrecto) { _dialogos.Error("No se puede generar el 303", r.Error!.TextoOperador()); return; }
            var v = await _ctx.Servicio.Presentacion.ValidarAsync(r.Valor.Aeat);
            if (!v.Correcta)
            {
                _dialogos.Error("La AEAT no valida el 303", "La AEAT ha devuelto estos errores (no se ha presentado nada):" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, v.Errores) + Environment.NewLine + Environment.NewLine + "Corrige lo que indica y vuelve a pulsar «Ver el PDF».");
                return;
            }
            AbrirPdf(v.Pdf!, $"303 {_ctx.Trabajo.Ejercicio} {fila.Datos.Periodo} borrador");
            if (v.Avisos.Count > 0) _ctx.Avisar("Avisos de la AEAT: " + string.Join(" · ", v.Avisos));
        }
        catch (Exception ex)
        {
            _dialogos.Error("No he podido obtener el PDF", $"{ex.Message}{Environment.NewLine}{Environment.NewLine}El PDF lo genera el servicio de validación de la AEAT: comprueba la conexión a internet y vuelve a intentarlo.");
        }
        finally { Ocupado = false; }
    }

    private void AbrirPdf(byte[] pdf, string nombre)
    {
        var carpeta = Path.Combine(Path.GetTempPath(), "ProContaVF", "Modelos");
        Directory.CreateDirectory(carpeta);
        var ruta = Path.Combine(carpeta, $"{nombre} {DateTime.Now:HHmmss}.pdf");
        File.WriteAllBytes(ruta, pdf);
        _ctx.AbrirFichero?.Invoke(ruta);
    }

    private async Task PresentarAsync(bool complementaria)
    {
        if (_seleccion is not { } fila) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.DeclaracionAsync(_ctx.Trabajo, fila.Trimestre, complementaria);
            if (!r.EsCorrecto) { _dialogos.Error("No se puede presentar el 303", r.Error!.TextoOperador()); return; }
            var (d, aeat) = r.Valor;
            var propios = await _ctx.Servicio.BloqueosAsync(_ctx.Trabajo, d);
            if (propios.Count > 0)
            {
                _dialogos.Error("No se puede presentar el 303", string.Join(Environment.NewLine + Environment.NewLine, propios));
                return;
            }
            if (d.TipoDeclaracion == 'I')
            {
                var nrc = _dialogos.PedirNrc($"NRC del pago del 303 {fila.Datos.Periodo} {_ctx.Trabajo.Ejercicio}",
                    $"{Servicio303.FueraDePlazoDomiciliacion(d, _ctx.Servicio.Hoy)}{Environment.NewLine}{Environment.NewLine}Importe a pagar: {Formatos.Importe(d.ImporteAIngresar)}. " +
                    "Paga ese importe exacto en tu banco (o en la Sede de la AEAT, «Pagar, aplazar y consultar»), con el NIF de la sociedad, el modelo 303, el ejercicio y el periodo, y escribe el NRC de 22 caracteres que te den.");
                if (nrc is null) return;
                aeat = aeat with { Nrc = nrc.Trim().ToUpperInvariant() };
            }
            var borrador = await _ctx.Servicio.Presentacion.PrepararAsync(_ctx.Trabajo, aeat);
            if (!_dialogos.Conformidad(borrador, () => { if (borrador.Validacion.Pdf is { } pdf) AbrirPdf(pdf, $"303 {_ctx.Trabajo.Ejercicio} {fila.Datos.Periodo} a presentar"); }))
                return;
            var p = await _ctx.Servicio.Presentacion.PresentarAsync(_ctx.Trabajo, borrador, conforme: true, _ctx.Actor);
            if (!p.EsCorrecto)
            {
                _dialogos.Error("No se ha presentado el 303", string.Join(Environment.NewLine + Environment.NewLine, p.Errores.Select(e => e.TextoOperador())));
                return;
            }
            var g = p.Valor.Guardada;
            var detalles = new List<string>
            {
                $"CSV: {g.Csv}", $"Justificante: {g.Justificante}", $"Expediente: {g.Expediente}", $"Fecha y hora: {g.FechaHora:dd/MM/yyyy HH:mm}",
                $"Forma de pago: {g.FormaPago}" + (g.Importe != 0m ? $" · importe {Formatos.Importe(g.Importe)}" : ""),
                g.RutaPdf is { } ruta ? $"Justificante archivado en {ruta}" : "El PDF justificante no se ha podido archivar todavía: «Abrir el justificante» lo vuelve a intentar.",
                "La liquidación de IVA de este trimestre pasa a usar el importe presentado: revísala en Contabilidad › Liquidación de IVA.",
            };
            detalles.AddRange(p.Valor.Avisos);
            _dialogos.Informar(borrador.Entorno == EntornoAeat.Pruebas ? "303 presentado en el entorno de PRUEBAS de la AEAT" : "303 presentado en la AEAT",
                $"La AEAT ha aceptado el 303 del {fila.Datos.Periodo} {_ctx.Trabajo.Ejercicio}{(complementaria ? " (rectificativa)" : "")}.", detalles);
            _ctx.Avisar($"303 {fila.Datos.Periodo} presentado: CSV {g.Csv}.");
        }
        catch (Exception ex)
        {
            _dialogos.Error("No se ha presentado el 303", $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Antes de repetirlo pulsa «Actualizar»: si la AEAT llegó a recibirlo, saldrá como presentado.");
        }
        finally { Ocupado = false; }
        await CargarAsync(fila.Trimestre);
    }

    private async Task AbrirJustificanteAsync()
    {
        if (_seleccion?.Datos.Ultima is not { } u) return;
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.Presentacion.ArchivarJustificanteAsync(_ctx.Trabajo, u);
            if (!r.EsCorrecto) { _dialogos.Error("No se puede abrir el justificante", r.Error!.TextoOperador()); return; }
            _ctx.AbrirFichero?.Invoke(r.Valor);
        }
        finally { Ocupado = false; }
        await CargarAsync(_seleccion?.Trimestre);
    }
}
