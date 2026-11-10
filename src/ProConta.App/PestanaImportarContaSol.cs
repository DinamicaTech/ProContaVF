using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Auditoria;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Tablas;

namespace ProConta.App;

/// <summary>Una BD de ContaSol de la lista: su ejercicio, lo que se ha simulado o importado y sus incidencias.</summary>
internal sealed class FilaOrigenContaSol(string ruta) : INotifyPropertyChanged
{
    private string _estado = "Sin analizar";
    private string _resumen = "";
    public string Ruta { get; } = ruta;
    public string Fichero => Path.GetFileName(Ruta);
    public PlanEjercicio? Plan { get; set; }
    public string Ejercicio => Plan?.Ejercicio.ToString() ?? "";
    public string Estado { get => _estado; set { _estado = value; Cambio(); } }
    public string Resumen { get => _resumen; set { _resumen = value; Cambio(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refrescar() => Cambio(nameof(Ejercicio));
    private void Cambio([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>Una incidencia para la tabla de la pantalla.</summary>
internal sealed record FilaIncidenciaContaSol(int Ejercicio, string Gravedad, string Fase, string Tabla, string? Clave, string Explicacion, string Codigo);

/// <summary>
/// Pestaña «Importar ContaSol» (implantacion/importador-contasol, opción A del propietario 2026-11-10): se eligen las BD de
/// ContaSol (una por ejercicio), se simula (analiza sin escribir) y se importa con avance y cancelación; al terminar,
/// el resumen de cada ejercicio y sus incidencias. Las acciones están en el menú contextual (clic derecho).
/// </summary>
internal sealed class PestanaImportarContaSol : IPestanaTrabajo
{
    private readonly ImportadorContaSol _importador;
    private readonly Actor _actor;
    private readonly Func<Task<bool>> _puedeImportar;
    private readonly Func<Window?> _ventana;
    private readonly Action<string> _avisar;
    private readonly ObservableCollection<FilaOrigenContaSol> _origenes = [];
    private readonly ObservableCollection<FilaIncidenciaContaSol> _incidencias = [];
    private readonly TextBlock _avance = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
    private readonly ProgressBar _barra = new() { Height = 6, Minimum = 0, Maximum = 1, Visibility = Visibility.Collapsed };
    private readonly TablaProConta _tablaOrigenes;
    private CancellationTokenSource? _cancelacion;
    private readonly Grid _vista;

    public PestanaImportarContaSol(ImportadorContaSol importador, Actor actor, Func<Task<bool>> puedeImportar, Func<Window?> ventana, Action<string> avisar)
    {
        (_importador, _actor, _puedeImportar, _ventana, _avisar) = (importador, actor, puedeImportar, ventana, avisar);
        _vista = new Grid { Margin = new Thickness(20) };
        _vista.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _vista.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _vista.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _vista.RowDefinitions.Add(new RowDefinition { Height = new GridLength(2, GridUnitType.Star) });

        var cabecera = new StackPanel();
        cabecera.Children.Add(new TextBlock { Text = "Importar ContaSol", Style = Estilo("Letra.TituloPantalla") });
        cabecera.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10), Style = Estilo("Letra.Secundaria"),
            Text = "Elige las BD de ContaSol (una por ejercicio, p. ej. 0022026.accdb) con clic derecho en la tabla. «Simular» lo comprueba todo sin escribir; " +
                   "«Importar» graba cada ejercicio en orden. Repetir la importación de un ejercicio sustituye lo importado antes sin duplicar.",
        });
        _vista.Children.Add(cabecera);

        _tablaOrigenes = Tabla("implantacion.contasol.origenes", "BD de ContaSol", _origenes, DataGridSelectionMode.Extended,
            ("Fichero", nameof(FilaOrigenContaSol.Fichero), 220), ("Ejercicio", nameof(FilaOrigenContaSol.Ejercicio), 80), ("Estado", nameof(FilaOrigenContaSol.Estado), 140),
            ("Resumen", nameof(FilaOrigenContaSol.Resumen), 600));
        _tablaOrigenes.ToolTip = "Clic derecho: añadir BD, quitar, simular, importar o cancelar";
        _tablaOrigenes.ContextMenu = Menu(
            ("Añadir BD de ContaSol…", "Elige uno o varios ficheros accdb de ContaSol", (Action)Anadir, () => _cancelacion is null),
            ("Quitar de la lista", "Quita las BD seleccionadas de la lista (no borra nada)", Quitar, () => _cancelacion is null && _tablaOrigenes.SelectedItems.Count > 0),
            ("Simular", "Lee y comprueba las BD sin escribir nada en ProContaVF", () => _ = SimularAsync(), () => _cancelacion is null && _origenes.Count > 0),
            ("Importar", "Graba en ProContaVF los ejercicios de la lista, en orden", () => _ = ImportarAsync(), () => _cancelacion is null && _origenes.Count > 0),
            ("Cancelar", "Detiene la importación al acabar el asiento en curso; lo grabado se conserva", () => _cancelacion?.Cancel(), () => _cancelacion is not null));
        Grid.SetRow(_tablaOrigenes, 1);
        _vista.Children.Add(_tablaOrigenes);

        var avance = new StackPanel();
        avance.Children.Add(_avance);
        avance.Children.Add(_barra);
        avance.Children.Add(new TextBlock { Text = "Incidencias", Style = Estilo("Letra.Subtitulo"), Margin = new Thickness(0, 8, 0, 6) });
        Grid.SetRow(avance, 2);
        _vista.Children.Add(avance);

        var tablaIncidencias = Tabla("implantacion.contasol.incidencias", "Incidencias de la importación de ContaSol", _incidencias, DataGridSelectionMode.Extended,
            ("Ejercicio", nameof(FilaIncidenciaContaSol.Ejercicio), 70), ("Gravedad", nameof(FilaIncidenciaContaSol.Gravedad), 100), ("Fase", nameof(FilaIncidenciaContaSol.Fase), 90),
            ("Tabla", nameof(FilaIncidenciaContaSol.Tabla), 90), ("Clave", nameof(FilaIncidenciaContaSol.Clave), 110), ("Explicación", nameof(FilaIncidenciaContaSol.Explicacion), 700));
        tablaIncidencias.ToolTip = "Bloqueante: el registro no se ha importado. Advertencia: se ha importado con un ajuste. Informativa: solo para saberlo.";
        Grid.SetRow(tablaIncidencias, 3);
        _vista.Children.Add(tablaIncidencias);
        _avance.Text = "Sin BD elegidas.";
    }

    public string Titulo => "Importar ContaSol";
    public string Ayuda => "Importación de la contabilidad de ContaSol por ejercicios";
    public string Icono => "dl";
    public bool TieneCambios => false;
    public bool GuardadoFallido => false;
    public object Vista => _vista;
    public IReadOnlyList<string> DescribirCambios() => [];
    public Task<bool> GuardarAsync() => Task.FromResult(true);
    public void Descartar() { }
    public void AlCerrar() => _cancelacion?.Cancel();
#pragma warning disable CS0067 // la pestaña no tiene cambios pendientes
    public event PropertyChangedEventHandler? PropertyChanged;
#pragma warning restore CS0067

    private static Style Estilo(string clave) => (Style)Application.Current.FindResource(clave);

    private static TablaProConta Tabla<T>(string clave, string titulo, ObservableCollection<T> filas, DataGridSelectionMode seleccion, params (string Titulo, string Campo, double Ancho)[] columnas)
    {
        var tabla = new TablaProConta { ClavePreferencias = clave, TituloExportacion = titulo, IsReadOnly = true, AutoGenerateColumns = false, SelectionMode = seleccion, ItemsSource = filas };
        foreach (var (t, campo, ancho) in columnas)
            tabla.Columns.Add(new DataGridTextColumn { Header = t, Binding = new Binding(campo), Width = ancho, SortMemberPath = campo });
        return tabla;
    }

    private static ContextMenu Menu(params (string Texto, string Ayuda, Action Accion, Func<bool> Disponible)[] opciones)
    {
        var menu = new ContextMenu();
        foreach (var (texto, ayuda, accion, disponible) in opciones)
        {
            var item = new MenuItem { Header = texto, ToolTip = ayuda };
            item.Click += (_, _) => accion();
            menu.Items.Add(item);
            menu.Opened += (_, _) => item.IsEnabled = disponible();
        }
        return menu;
    }

    private void Anadir()
    {
        var dialogo = new Microsoft.Win32.OpenFileDialog
        {
            Title = "BD de ContaSol (una por ejercicio)", Filter = "BD de ContaSol (*.accdb;*.mdb)|*.accdb;*.mdb", Multiselect = true, CheckFileExists = true,
        };
        if (dialogo.ShowDialog(_ventana()) != true) return;
        foreach (var ruta in dialogo.FileNames.Where(r => _origenes.All(o => !string.Equals(o.Ruta, r, StringComparison.OrdinalIgnoreCase))))
            _origenes.Add(new FilaOrigenContaSol(ruta));
        _avance.Text = $"{_origenes.Count} BD en la lista. Clic derecho › Simular para comprobarlas.";
    }

    private void Quitar()
    {
        foreach (var fila in _tablaOrigenes.SelectedItems.Cast<FilaOrigenContaSol>().ToList()) _origenes.Remove(fila);
    }

    private IProgress<ProgresoImportacion> Progreso() => new Progress<ProgresoImportacion>(p =>
    {
        _avance.Text = p.Texto;
        _barra.Visibility = Visibility.Visible;
        _barra.IsIndeterminate = p.Total == 0;
        _barra.Value = p.Total == 0 ? 0 : (double)p.Hechos / p.Total;
    });

    private void MostrarIncidencias(IEnumerable<(int Ejercicio, Incidencia Incidencia)> incidencias)
    {
        _incidencias.Clear();
        foreach (var (ejercicio, i) in incidencias.OrderByDescending(x => x.Incidencia.Severidad).ThenBy(x => x.Ejercicio))
            _incidencias.Add(new FilaIncidenciaContaSol(ejercicio, i.Severidad switch { Severidad.Bloqueante => "Bloqueante", Severidad.Advertencia => "Advertencia", _ => "Informativa" },
                i.Fase, i.Tabla, i.Clave, i.Explicacion, i.Codigo));
    }

    /// <summary>Analiza las BD de la lista sin escribir nada. Devuelve los planes, o null si alguna BD no se puede leer.</summary>
    private async Task<IReadOnlyList<PlanEjercicio>?> AnalizarAsync(CancellationToken cancelacion)
    {
        var r = await _importador.AnalizarAsync(_origenes.Select(o => o.Ruta).ToList(), Progreso(), cancelacion);
        if (!r.EsCorrecto)
        {
            _avance.Text = "No he podido leer todas las BD: " + string.Join(" ", r.Errores.Select(e => e.TextoOperador()));
            return null;
        }
        foreach (var plan in r.Valor)
        {
            var fila = _origenes.First(o => string.Equals(Path.GetFullPath(o.Ruta), plan.Origen.Ruta, StringComparison.OrdinalIgnoreCase));
            fila.Plan = plan;
            fila.Refrescar();
            var bloqueantes = plan.Incidencias.Count(i => i.Severidad == Severidad.Bloqueante);
            fila.Estado = bloqueantes > 0 ? $"Simulado: {bloqueantes} bloqueantes" : "Simulado";
            fila.Resumen = $"{plan.Cuentas.Count} cuentas, {plan.Asientos.Count} asientos, {plan.Apuntes} apuntes, {plan.Registros.Count} registros de IVA, {plan.Terceros.Count} terceros, " +
                           $"{plan.Incidencias.Count(i => i.Severidad == Severidad.Advertencia)} advertencias.";
        }
        // La tabla queda en el orden en que se importará.
        var ordenadas = _origenes.OrderBy(o => o.Plan?.Ejercicio ?? int.MaxValue).ToList();
        _origenes.Clear();
        foreach (var o in ordenadas) _origenes.Add(o);
        return r.Valor;
    }

    private async Task EjecutarAsync(Func<CancellationToken, Task> trabajo)
    {
        _cancelacion = new CancellationTokenSource();
        try
        {
            await trabajo(_cancelacion.Token);
        }
        catch (OperationCanceledException)
        {
            _avance.Text = "Cancelado.";
        }
        catch (Exception ex)
        {
            _avance.Text = $"Se ha detenido por un error: {ex.Message}";
        }
        finally
        {
            _cancelacion.Dispose();
            _cancelacion = null;
            _barra.Visibility = Visibility.Collapsed;
        }
    }

    private Task SimularAsync() => EjecutarAsync(async c =>
    {
        var planes = await AnalizarAsync(c);
        if (planes is null) return;
        MostrarIncidencias(planes.SelectMany(p => p.Incidencias.Select(i => (p.Ejercicio, i))));
        var bloqueantes = _incidencias.Count(i => i.Gravedad == "Bloqueante");
        _avance.Text = $"Simulación terminada: {planes.Count} ejercicios, {planes.Sum(p => p.Asientos.Count)} asientos y {bloqueantes} incidencias bloqueantes. No se ha escrito nada.";
    });

    private Task ImportarAsync() => EjecutarAsync(async c =>
    {
        if (!await _puedeImportar())
        {
            _avisar("No tienes permiso para importar: hace falta el de editar Parámetros y tablas.");
            return;
        }
        var planes = await AnalizarAsync(c);
        if (planes is null) return;
        var bloqueantes = planes.Sum(p => p.Incidencias.Count(i => i.Severidad == Severidad.Bloqueante));
        var detalles = planes.Select(p => $"{p.Ejercicio}: {p.Asientos.Count} asientos, {p.Incidencias.Count(i => i.Severidad == Severidad.Bloqueante)} incidencias bloqueantes").ToList();
        var opcion = ServicioMensajes.Preguntar(_ventana(), "Importar ContaSol",
            $"Voy a importar {planes.Count} ejercicios en esta BD de ProContaVF, en orden. Lo que ya se importó de esos ejercicios se sustituye sin duplicar." +
            (bloqueantes > 0 ? $" Hay {bloqueantes} incidencias bloqueantes: esos registros no se importan." : ""),
            detalles,
            new OpcionMensaje("Importar", "Graba los ejercicios en ProContaVF", Principal: true),
            new OpcionMensaje("No importar", "Vuelve a la pantalla sin escribir nada", Cancelar: true));
        if (opcion != 0) return;
        var resumenes = await _importador.ImportarAsync(planes, _actor, Progreso(), c);
        foreach (var r in resumenes)
        {
            var fila = _origenes.FirstOrDefault(o => o.Plan?.Ejercicio == r.Ejercicio);
            if (fila is null) continue;
            fila.Estado = r.Estado switch
            {
                EstadosImportacion.Completada => "Importado",
                EstadosImportacion.ConIncidencias => "Importado con incidencias",
                EstadosImportacion.Cancelada => "Cancelado",
                _ => "No importado",
            };
            fila.Resumen = r.Texto;
        }
        MostrarIncidencias(resumenes.SelectMany(r => r.Incidencias.Select(i => (r.Ejercicio, i))));
        _avance.Text = $"Importación terminada: {resumenes.Count(r => r.Estado == EstadosImportacion.Completada)} de {resumenes.Count} ejercicios sin incidencias bloqueantes.";
        _avisar("Importación de ContaSol terminada.");
    });
}
