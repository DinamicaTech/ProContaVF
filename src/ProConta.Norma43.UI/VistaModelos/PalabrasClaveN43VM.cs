using System.Collections.ObjectModel;
using ProConta.Norma43.Asignacion;
using ProConta.Norma43.Conciliacion;
using ProConta.Norma43.Lineas;
using ProConta.Nucleo.Contexto;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Modelos;

namespace ProConta.Norma43.UI.VistaModelos;

/// <summary>Palabra clave existente que encaja con la línea, en la lista del diálogo.</summary>
public sealed class PalabraExistenteN43VM(PalabraClaveLineaN43 p) : ModeloVista
{
    public PalabraClaveLineaN43 Datos { get; } = p;
    public string Texto => Datos.PalabraClave2 is { } dos ? $"«{Datos.PalabraClave}» y «{dos}»" : $"«{Datos.PalabraClave}»";
    public string Cuenta => Datos.Cuenta is null ? (Datos.EsRemesa ? "Remesa (sin cuenta)" : "Sin cuenta") : $"{Datos.Cuenta} · {Datos.NombreCuenta}";
    public string Alcance => string.Join(" · ", new[]
    {
        Datos.Entidad is { } e ? $"solo banco {e}" : "cualquier banco",
        ReglasPalabrasClave.SentidoDeLinea(Datos.Sentido) switch { "D" => "solo cargos", "H" => "solo abonos", _ => "cargos y abonos" },
        Datos.EsRemesa ? "remesa" : null,
        Datos.Usos > 0 ? $"asignó {Datos.Usos} línea(s)" : "aún no ha asignado ninguna",
    }.Where(t => t is not null));
}

/// <summary>
/// Palabras clave de una línea (norma43/pantallas, requisito del 2026-10-29 16:37): el concepto completo a la vista para elegir el criterio
/// seleccionando un trozo literal (no se escribe a mano), las palabras clave que ya encajan con la línea para editarlas o borrarlas, y una
/// nueva. Guardar no cambia ninguna línea: al cerrar, la pantalla ofrece aplicarlas.
/// </summary>
public sealed class PalabrasClaveN43VM : ModeloVista
{
    private readonly ServicioLineasN43 _servicio;
    private readonly IDialogosLineasN43 _dialogos;
    private readonly string _idSociedad;
    private readonly ContextoSociedadEjercicio _ejercicio;
    private int? _editando;
    private string _palabra = "";
    private string _palabra2 = "";
    private bool _soloBanco = true;
    private bool _soloSentido = true;
    private bool _remesa;
    private CuentaElegidaN43? _cuenta;
    private string? _error;

    public PalabrasClaveN43VM(ServicioLineasN43 servicio, IDialogosLineasN43 dialogos, string idSociedad, LineaN43 linea, ContextoSociedadEjercicio ejercicio)
    {
        _servicio = servicio;
        _dialogos = dialogos;
        _idSociedad = idSociedad;
        _ejercicio = ejercicio;
        Linea = linea;
    }

    public LineaN43 Linea { get; }
    /// <summary>Texto en que se elige el criterio: el concepto completo y, si los hay, referencias y documento.</summary>
    public string Concepto => Linea.TextoBusqueda;
    public string Cabecera => $"{Linea.Rotulo} · {Linea.NombreBanco} · {(Linea.EsCargo ? "cargo" : "abono")}";
    public ObservableCollection<PalabraExistenteN43VM> Existentes { get; } = [];
    public bool SinExistentes => Existentes.Count == 0;
    /// <summary>Se ha creado o corregido alguna palabra clave: la pantalla ofrece aplicarlas. Borrar no cuenta (las líneas conservan su cuenta).</summary>
    public bool HuboCambios { get; private set; }

    public bool Editando => _editando is not null;
    public string TituloFormulario => _editando is null ? "Palabra clave nueva" : "Corregir la palabra clave";
    public string Palabra { get => _palabra; private set { if (Fijar(ref _palabra, value)) Error = null; } }
    public string Palabra2 { get => _palabra2; private set { if (Fijar(ref _palabra2, value)) Error = null; } }
    public bool SoloBanco { get => _soloBanco; set { if (Fijar(ref _soloBanco, value)) Error = null; } }
    public bool SoloSentido { get => _soloSentido; set { if (Fijar(ref _soloSentido, value)) Error = null; } }
    public bool Remesa { get => _remesa; set { if (Fijar(ref _remesa, value)) Error = null; } }
    public string TextoSoloBanco => $"Solo este banco ({Linea.Entidad})";
    public string TextoSoloSentido => Linea.EsCargo ? "Solo cargos" : "Solo abonos";
    public CuentaElegidaN43? Cuenta { get => _cuenta; private set { if (Fijar(ref _cuenta, value)) { Avisar(nameof(TextoCuenta)); Error = null; } } }
    public string TextoCuenta => _cuenta?.Texto ?? "Sin elegir";
    public string? Error { get => _error; private set { if (Fijar(ref _error, value)) Avisar(nameof(HayError)); } }
    public bool HayError => _error is not null;
    public string? Mensaje { get; private set; }

    public async Task CargarAsync()
    {
        var lista = await _servicio.PalabrasQueEncajanAsync(_idSociedad, Linea);
        Existentes.Clear();
        foreach (var p in lista) Existentes.Add(new PalabraExistenteN43VM(p));
        Avisar(nameof(SinExistentes));
    }

    /// <summary>
    /// Usa el texto seleccionado en el concepto como palabra clave (o como segunda palabra). Tiene que ser un trozo del concepto tal cual:
    /// si no lo es (o está vacío), lo dice y no cambia nada.
    /// </summary>
    public bool UsarSeleccion(string? seleccion, bool segunda)
    {
        var t = (seleccion ?? "").Trim();
        if (t.Length == 0) { Error = "Selecciona con el ratón un trozo del concepto (el que se repite en estos movimientos) y vuelve a pulsar el botón."; return false; }
        if (Concepto.IndexOf(t, StringComparison.Ordinal) < 0) { Error = "El texto elegido no está tal cual en el concepto: selecciónalo en el recuadro del concepto."; return false; }
        if (segunda) Palabra2 = t; else Palabra = t;
        return true;
    }

    public void QuitarSegunda() => Palabra2 = "";

    public void ElegirCuenta()
    {
        var c = _dialogos.ElegirCuenta(_ejercicio, $"Cuenta de la palabra clave «{(Palabra.Length > 0 ? Palabra : "nueva")}»", _cuenta?.Codigo ?? Linea.CuentaDestino);
        if (c is not null) Cuenta = c;
    }

    public void Nueva()
    {
        _editando = null;
        Palabra = "";
        Palabra2 = "";
        SoloBanco = true;
        SoloSentido = true;
        Remesa = false;
        Cuenta = null;
        Error = null;
        Avisar(nameof(Editando), nameof(TituloFormulario));
    }

    public void Editar(PalabraExistenteN43VM p)
    {
        _editando = p.Datos.Id;
        Palabra = p.Datos.PalabraClave;
        Palabra2 = p.Datos.PalabraClave2 ?? "";
        SoloBanco = p.Datos.Entidad is not null;
        SoloSentido = p.Datos.Sentido is not null;
        Remesa = p.Datos.EsRemesa;
        Cuenta = p.Datos.IdIdentidadContable is int id ? new CuentaElegidaN43(id, p.Datos.Cuenta ?? "", p.Datos.NombreCuenta ?? "") : null;
        Error = null;
        Avisar(nameof(Editando), nameof(TituloFormulario));
    }

    /// <summary>Lo que se guardaría con el formulario.</summary>
    public EdicionPalabraClaveN43 Edicion => new(_editando, SoloBanco ? Linea.Entidad : null,
        // Convenio de PalabrasClaveBancarias: el sentido es el de la 572 (H para los cargos de la línea, D para los abonos).
        SoloSentido ? (Linea.EsCargo ? ServicioConciliacionN43.Abono : ServicioConciliacionN43.Cargo) : null,
        Palabra, Palabra2.Length == 0 ? null : Palabra2, _cuenta?.IdIdentidadContable, Remesa ? ReglasPalabrasClave.EspecialRemesa : null);

    public async Task<bool> GuardarAsync()
    {
        if (ServicioLineasN43.ValidarPalabra(Linea, Edicion) is { } e) { Error = e.TextoOperador(); return false; }
        var r = await _servicio.GuardarPalabraAsync(_idSociedad, Linea, Edicion);
        if (!r.EsCorrecto) { Error = string.Join(" ", r.Errores.Select(x => x.TextoOperador())); return false; }
        HuboCambios = true;
        Mensaje = _editando is null ? "Palabra clave creada." : "Palabra clave corregida.";
        Avisar(nameof(Mensaje));
        await CargarAsync();
        Nueva();
        return true;
    }

    public async Task<bool> BorrarAsync(PalabraExistenteN43VM p)
    {
        if (_dialogos.Preguntar("Borrar la palabra clave", $"¿Borro la palabra clave {p.Texto} ({p.Cuenta})? Las líneas que ya asignó conservan su cuenta.", null,
                new OpcionMensaje("Borrar", "La borra (o la desactiva si ya asignó alguna línea)", Peligro: true),
                new OpcionMensaje("Cancelar", "No cambia nada", Principal: true, Cancelar: true)) != 0) return false;
        var r = await _servicio.BorrarPalabraAsync(_idSociedad, p.Datos.Id);
        if (!r.EsCorrecto) { Error = string.Join(" ", r.Errores.Select(x => x.TextoOperador())); return false; }
        Mensaje = r.Valor;
        Avisar(nameof(Mensaje));
        if (_editando == p.Datos.Id) Nueva();
        await CargarAsync();
        return true;
    }
}
