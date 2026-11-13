using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Contabilidad.Asientos;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Contabilidad.Plantillas;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Importes;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Asientos;

/// <summary>Qué se está haciendo en la pantalla.</summary>
public enum ModoAsientoManual
{
    /// <summary>Asiento nuevo, desde cero o con una plantilla sin origen.</summary>
    Nuevo,
    /// <summary>Corrección de un apunte del Mayor con una plantilla de origen Apunte.</summary>
    Correccion,
    /// <summary>Modificación de un asiento manual que ya existe.</summary>
    Modificar,
}

/// <summary>Una opción del desplegable «Plantilla»: «Sin plantilla» o una plantilla de usuario.</summary>
public sealed record OpcionPlantillaVM(PlantillaAsiento? Plantilla)
{
    public string Texto => Plantilla?.Nombre ?? "Sin plantilla";
    public string Ayuda => Plantilla?.Descripcion ?? "Asiento hecho a mano, línea a línea, sin partir de ninguna plantilla";
    public override string ToString() => Texto;
}

/// <summary>Centro de coste del desplegable de las líneas (o «Sin centro»).</summary>
public sealed record OpcionCentroVM(CentroCoste? Centro)
{
    public string Texto => Centro is null ? "Sin centro" : $"{Centro.Codigo} · {Centro.Nombre}";
    public override string ToString() => Texto;
}

/// <summary>Un dato de entrada de la plantilla elegida (importe, cuenta, texto o fecha).</summary>
public sealed class DatoPlantillaVM : ModeloVista
{
    private readonly AsientoManualVM _vm;
    private string _valor;

    internal DatoPlantillaVM(AsientoManualVM vm, DatoEntrada dato, string? valor)
    {
        _vm = vm;
        Dato = dato;
        _valor = valor is not null && dato.Tipo == TipoDato.Importe && decimal.TryParse(valor, NumberStyles.Number, CultureInfo.InvariantCulture, out var n) ? Formatos.Numero(n) : valor ?? "";
        BuscarCuentaCommand = new ComandoDelegado(() => _vm.BuscarCuentaDato(this), () => EsCuenta && !_vm.SoloLectura);
    }

    public DatoEntrada Dato { get; }
    public string Etiqueta => Dato.Etiqueta + (Dato.Obligatorio ? " *" : "");
    public bool EsCuenta => Dato.Tipo == TipoDato.Cuenta;
    public bool EsImporte => Dato.Tipo == TipoDato.Importe;
    public string Ayuda => (Dato.Ayuda ?? Dato.Etiqueta) + Dato.Tipo switch
    {
        TipoDato.Cuenta => Dato.FiltroCuenta is { Length: > 0 } f ? $" Solo subcuentas {f}; F3 o la lupa abre el buscador." : " F3 o la lupa abre el buscador de cuentas.",
        TipoDato.Importe => " En negativo, los apuntes van al lado contrario.",
        TipoDato.Fecha => " Escríbela como dd/mm/aaaa.",
        _ => "",
    };

    public string Valor
    {
        get => _valor;
        set { if (Fijar(ref _valor, value ?? "")) { Avisar(nameof(NombreCuenta)); _vm.DatoCambiado(); } }
    }

    /// <summary>Nombre de la cuenta escrita, o por qué no vale.</summary>
    public string NombreCuenta => !EsCuenta || _valor.Trim().Length == 0 ? "" : _vm.NombreCuenta(_valor.Trim()) ?? "No existe en este ejercicio";

    public ICommand BuscarCuentaCommand { get; }

    /// <summary>El valor tal como lo leen las fórmulas (los importes, con punto decimal).</summary>
    internal string? ValorParaFormulas =>
        _valor.Trim().Length == 0 ? null : EsImporte && Formatos.LeerImporte(_valor, out var n) ? n.ToString(CultureInfo.InvariantCulture) : _valor.Trim();
}

/// <summary>Un apunte de la vista previa.</summary>
public sealed record ApuntePrevioVM(string Cuenta, string Nombre, string Concepto, string Debe, string Haber);

/// <summary>Un asiento de la vista previa.</summary>
public sealed record AsientoPrevioVM(string Titulo, IReadOnlyList<ApuntePrevioVM> Apuntes, string TotalDebe, string TotalHaber);

/// <summary>Una línea de la rejilla del asiento.</summary>
public sealed class LineaAsientoVM : ModeloVista
{
    private readonly BloqueAsientoVM _bloque;
    private string _cuenta = "";
    private string _concepto = "";
    private bool _conceptoPropio;
    private string _documento = "";
    private string _debe = "";
    private string _haber = "";
    private decimal _valorDebe;
    private decimal _valorHaber;
    private bool _importeValido = true;
    private OpcionCentroVM? _centro;

    internal LineaAsientoVM(BloqueAsientoVM bloque) => _bloque = bloque;

    internal AsientoManualVM Vm => _bloque.Vm;
    public BloqueAsientoVM Bloque => _bloque;
    public int Numero => _bloque.Lineas.IndexOf(this) + 1;
    public bool SoloLectura => Vm.SoloLectura;
    public bool Editable => !Vm.SoloLectura;

    /// <summary>Si la línea viene de una plantilla con una cuenta «?» que no existe: el nombre con que se creará al guardar.</summary>
    internal string? NombreCuentaNueva { get; set; }

    public string Cuenta
    {
        get => _cuenta;
        set
        {
            var texto = (value ?? "").Trim();
            // «629.1» se completa a 6290001 si esa subcuenta existe (como en el resto de pantallas).
            if (texto.Contains('.') && BuscadorCuentas.ExpandirPunto(texto) is { } expandida && Vm.NombreCuenta(expandida) is not null) texto = expandida;
            if (texto != _cuenta && !string.IsNullOrEmpty(NombreCuentaNueva)) NombreCuentaNueva = null;
            if (Fijar(ref _cuenta, texto)) { Avisar(nameof(NombreCuenta), nameof(CuentaValida), nameof(AyudaCuenta)); _bloque.Cambiado(); }
        }
    }

    public string NombreCuenta => _cuenta.Length == 0 ? "" : Vm.NombreCuenta(_cuenta) ?? (NombreCuentaNueva is { } n ? $"Nueva: {n}" : "No existe en este ejercicio");

    /// <summary>La cuenta existe en el ejercicio (o se creará al guardar, si viene de una plantilla con «?»). Vacía cuenta como válida si la línea no tiene importe.</summary>
    public bool CuentaValida => _cuenta.Length == 0 ? !TieneImporte : Vm.NombreCuenta(_cuenta) is not null || NombreCuentaNueva is not null;

    public string AyudaCuenta => CuentaValida
        ? "Subcuenta del apunte: escribe el código (629.1 vale por 6290001), o F3 o la lupa para el buscador (con «Crear subcuenta»)"
        : _cuenta.Length == 0 ? "Falta la cuenta de este apunte: F3 o la lupa abre el buscador" : $"La cuenta {_cuenta} no existe en este ejercicio: búscala con F3 o la lupa, o créala desde el buscador";

    /// <summary>
    /// Concepto del apunte. Mientras no se cambia a mano, sigue a la descripción del asiento (contabilidad/plantillas-asiento 2026-11-12 20:11):
    /// si cambia la descripción, cambia en las líneas no tocadas. Escribirlo a mano (distinto de la descripción) lo deja fijo.
    /// </summary>
    public string Concepto
    {
        get => _concepto;
        set
        {
            var texto = value ?? "";
            _conceptoPropio = texto != _bloque.Descripcion;
            if (Fijar(ref _concepto, texto)) _bloque.Cambiado();
        }
    }

    /// <summary>El concepto sigue a la descripción del asiento (no se ha escrito a mano).</summary>
    public bool ConceptoHeredado => !_conceptoPropio;

    /// <summary>Pone el concepto desde una plantilla o un asiento grabado: queda fijo salvo que coincida con la descripción.</summary>
    internal void PonerConcepto(string? texto, bool heredado)
    {
        _concepto = texto ?? "";
        _conceptoPropio = !heredado;
        Avisar(nameof(Concepto));
    }

    /// <summary>La descripción del asiento ha cambiado: la línea que la hereda la sigue.</summary>
    internal void DescripcionCambiada(string descripcion)
    {
        if (_conceptoPropio || _concepto == descripcion) return;
        _concepto = descripcion;
        Avisar(nameof(Concepto));
    }
    public string Documento { get => _documento; set { if (Fijar(ref _documento, value ?? "")) _bloque.Cambiado(); } }

    public string Debe { get => _debe; set => FijarImporte(value, debe: true); }
    public string Haber { get => _haber; set => FijarImporte(value, debe: false); }
    public decimal ValorDebe => _valorDebe;
    public decimal ValorHaber => _valorHaber;
    public bool ImporteValido => _importeValido;
    public bool TieneImporte => _valorDebe != 0m || _valorHaber != 0m;
    /// <summary>Sin cuenta ni importe: no se graba (aunque lleve el concepto o el documento copiados de la anterior).</summary>
    public bool Vacia => _cuenta.Length == 0 && !TieneImporte;

    public string AyudaImporte => _importeValido
        ? "Importe en euros (la coma es el decimal); poner importe en un lado borra el del otro. Menú contextual: invertir o «Cuadrar con esta línea»"
        : "Este importe no se entiende: escribe solo el número, por ejemplo 121,50";

    public IReadOnlyList<OpcionCentroVM> Centros => Vm.Centros;
    public OpcionCentroVM? Centro { get => _centro; set { if (Fijar(ref _centro, value)) _bloque.Cambiado(); } }

    /// <summary>Pone los importes ya leídos (desde una plantilla o un asiento grabado), sin pasar por el texto.</summary>
    internal void PonerImportes(decimal debe, decimal haber)
    {
        _valorDebe = Importe.RedondearValor(debe);
        _valorHaber = Importe.RedondearValor(haber);
        _debe = _valorDebe == 0m ? "" : Formatos.Numero(_valorDebe);
        _haber = _valorHaber == 0m ? "" : Formatos.Numero(_valorHaber);
        _importeValido = true;
        Avisar(nameof(Debe), nameof(Haber), nameof(ImporteValido), nameof(AyudaImporte), nameof(CuentaValida), nameof(AyudaCuenta));
        _bloque.Cambiado();
    }

    internal void PonerCentro(int? idCentro) => Centro = Vm.Centros.FirstOrDefault(c => c.Centro?.IdCentroCoste == idCentro) ?? Vm.Centros[0];

    private void FijarImporte(string? texto, bool debe)
    {
        var t = (texto ?? "").Trim();
        decimal valor = 0m;
        var valido = t.Length == 0 || Formatos.LeerImporte(t, out valor);
        if (valido && valor < 0m)
        {
            // Un importe negativo va al lado contrario.
            debe = !debe;
            valor = -valor;
        }
        valor = Importe.RedondearValor(valor);
        if (debe)
        {
            _valorDebe = valido ? valor : 0m;
            _debe = valido && valor != 0m ? Formatos.Numero(valor) : valido ? "" : t;
            if (valido && valor != 0m) { _valorHaber = 0m; _haber = ""; }
        }
        else
        {
            _valorHaber = valido ? valor : 0m;
            _haber = valido && valor != 0m ? Formatos.Numero(valor) : valido ? "" : t;
            if (valido && valor != 0m) { _valorDebe = 0m; _debe = ""; }
        }
        _importeValido = valido;
        Avisar(nameof(Debe), nameof(Haber), nameof(ImporteValido), nameof(AyudaImporte), nameof(CuentaValida), nameof(AyudaCuenta));
        _bloque.Cambiado();
    }

    internal void AvisarNumero() => Avisar(nameof(Numero));
    internal void AvisarCuenta() => Avisar(nameof(NombreCuenta), nameof(CuentaValida), nameof(AyudaCuenta));
    internal void AvisarSoloLectura() => Avisar(nameof(SoloLectura), nameof(Editable));
}

/// <summary>Un asiento de la pantalla: fecha, descripción, líneas y totales. Una plantilla puede proponer varios (se guardan juntos).</summary>
public sealed class BloqueAsientoVM : ModeloVista
{
    private string _fecha;
    private string _descripcion;

    internal BloqueAsientoVM(AsientoManualVM vm, int numero, DateTime fecha, string descripcion)
    {
        Vm = vm;
        Numero = numero;
        _fecha = Formatos.Fecha(fecha);
        _descripcion = descripcion;
    }

    internal AsientoManualVM Vm { get; }
    public int Numero { get; internal set; }
    public string Titulo => Vm.Bloques.Count > 1 ? $"Asiento {Numero} de {Vm.Bloques.Count}" : "Asiento";
    public ObservableCollection<LineaAsientoVM> Lineas { get; } = [];
    public bool SoloLectura => Vm.SoloLectura;

    public string Fecha { get => _fecha; set { if (Fijar(ref _fecha, value ?? "")) { Avisar(nameof(FechaValida), nameof(AyudaFecha)); Vm.Recalcular(); } } }
    public string Descripcion
    {
        get => _descripcion;
        set
        {
            if (!Fijar(ref _descripcion, value ?? "")) return;
            foreach (var l in Lineas) l.DescripcionCambiada(_descripcion);
            Vm.Recalcular();
        }
    }

    /// <summary>Se ve «Quitar este asiento» cuando la pantalla tiene varios (p. ej. una guía de dos pasos en la que aún no toca el segundo).</summary>
    public bool PuedeQuitarse => Vm.Bloques.Count > 1 && !Vm.SoloLectura;

    internal DateTime? FechaLeida => DateTime.TryParseExact(_fecha.Trim(), ["d/M/yyyy", "dd/MM/yyyy", "d/M/yy", "dd/MM/yy"], Formatos.Cultura, DateTimeStyles.None, out var f) ? f.Date : null;

    public bool FechaValida => Vm.ProblemaFecha(this) is null;
    public string AyudaFecha => Vm.ProblemaFecha(this) ?? $"Fecha contable del asiento (dd/mm/aaaa), dentro del ejercicio {Vm.Contexto.Ejercicio} y desde el trimestre activo";

    public decimal SumaDebe => Lineas.Sum(l => l.ValorDebe);
    public decimal SumaHaber => Lineas.Sum(l => l.ValorHaber);
    public string TotalDebe => Formatos.Importe(SumaDebe);
    public string TotalHaber => Formatos.Importe(SumaHaber);
    public decimal Diferencia => SumaDebe - SumaHaber;
    public bool Cuadrado => Diferencia == 0m;
    public string TextoDescuadre => Cuadrado ? "Cuadrado" : $"Descuadre: {Formatos.Importe(Math.Abs(Diferencia))} (falta {(Diferencia > 0 ? "Haber" : "Debe")})";
    public string AyudaDescuadre => Cuadrado
        ? "El Debe y el Haber coinciden"
        : $"El Debe y el Haber no coinciden: faltan {Formatos.Importe(Math.Abs(Diferencia))} en el {(Diferencia > 0 ? "Haber" : "Debe")}. Usa «Cuadrar con esta línea» en el menú contextual de la línea que deba recogerlos";

    internal void Cambiado()
    {
        Avisar(nameof(TotalDebe), nameof(TotalHaber), nameof(Cuadrado), nameof(TextoDescuadre), nameof(AyudaDescuadre));
        Vm.Recalcular();
    }

    internal void Renumerar()
    {
        foreach (var l in Lineas) l.AvisarNumero();
        Cambiado();
    }

    internal void AvisarTitulo() => Avisar(nameof(Titulo), nameof(SoloLectura), nameof(PuedeQuitarse));
    internal void AvisarFecha() => Avisar(nameof(FechaValida), nameof(AyudaFecha));
}

/// <summary>
/// Pantalla de asiento manual (nuevo y modificar), que sustituye a frmAsiento y al asistente frmModificarAsiento del ERP
/// (contabilidad/pantallas y contabilidad/plantillas-asiento, PR 2): arranca en «Sin plantilla»; al elegir una plantilla de usuario
/// enseña su descripción, pide sus datos de entrada y muestra la vista previa (con el apunte de origen o, sin él, con el ejemplo de la
/// plantilla); «Aplicar» copia sus asientos a la rejilla, que se puede cambiar antes de guardar. Guardar está desactivado mientras algo
/// no cuadra o falta, y su ayuda dice qué. Cancelar no escribe nada.
/// </summary>
public sealed class AsientoManualVM : ModeloVista
{
    private readonly IFuenteAsientoManual _fuente;
    private readonly IDialogosAsiento _dialogos;
    private EntornoSociedad _entorno;
    private EntornoSociedad? _entornoPlantilla;
    private Dictionary<string, string> _cuentas = new(StringComparer.OrdinalIgnoreCase);
    private OpcionPlantillaVM? _plantilla;
    private IReadOnlyList<AsientoPrevioVM> _vistaPrevia = [];
    private string? _errorVistaPrevia;
    private string _motivo = "";
    private string _documento = "";
    private bool _cambios;
    private bool _guardando;
    private string? _errorGuardado;
    private Asiento? _original;
    private OrigenApunte? _origen;
    private OrigenSubcuenta? _subcuenta;
    private bool _vistaConEjemplo;
    private PlantillaAsiento? _aplicada;
    private IReadOnlyDictionary<string, string?> _datosAplicados = new Dictionary<string, string?>();
    private bool _cargando = true;

    public AsientoManualVM(IFuenteAsientoManual fuente, IDialogosAsiento dialogos)
    {
        _fuente = fuente;
        _dialogos = dialogos;
        _entorno = new EntornoSociedad(fuente.Contexto);
        Centros = [new OpcionCentroVM(null)];
        AplicarCommand = new ComandoDelegado(Aplicar, () => PuedeAplicar);
        GuardarCommand = new ComandoDelegado(() => _ = GuardarAsync(), () => PuedeGuardar);
        CancelarCommand = new ComandoDelegado(() => Cancelado?.Invoke());
        AnadirLineaCommand = new ComandoDelegado(p => AnadirLinea(p as BloqueAsientoVM ?? Bloques.FirstOrDefault()), _ => !SoloLectura);
        InsertarLineaCommand = new ComandoDelegado(p => { if (p is LineaAsientoVM l) InsertarDespues(l); }, _ => !SoloLectura);
        DuplicarLineaCommand = new ComandoDelegado(p => { if (p is LineaAsientoVM l) Duplicar(l); }, _ => !SoloLectura);
        BorrarLineaCommand = new ComandoDelegado(p => { if (p is LineaAsientoVM l) Borrar(l); }, _ => !SoloLectura);
        InvertirLineaCommand = new ComandoDelegado(p => { if (p is LineaAsientoVM l) Invertir(l); }, _ => !SoloLectura);
        CuadrarConLineaCommand = new ComandoDelegado(p => { if (p is LineaAsientoVM l) CuadrarCon(l); }, _ => !SoloLectura);
        BuscarCuentaLineaCommand = new ComandoDelegado(p => { if (p is LineaAsientoVM l) BuscarCuentaLinea(l); }, _ => !SoloLectura);
        QuitarBloqueCommand = new ComandoDelegado(p => { if (p is BloqueAsientoVM b) QuitarBloque(b); }, _ => !SoloLectura && Bloques.Count > 1);
    }

    public ContextoSociedadEjercicio Contexto => _fuente.Contexto;
    public ModoAsientoManual Modo { get; private set; } = ModoAsientoManual.Nuevo;

    /// <summary>Se ha guardado: los asientos grabados.</summary>
    public event Action<IReadOnlyList<int>>? Guardado;

    /// <summary>Se ha pulsado Cancelar.</summary>
    public event Action? Cancelado;

    // ---------- cabecera ----------

    public string Titulo => Modo switch
    {
        ModoAsientoManual.Modificar => $"Asiento {_original?.IdAsiento}",
        ModoAsientoManual.Correccion => "Corrección de un apunte",
        _ => "Asiento nuevo",
    };

    public string Subtitulo => Modo switch
    {
        ModoAsientoManual.Correccion when _origen is { } o =>
            $"Apunte {o.Apunte.Orden} del asiento {o.Asiento.IdAsiento} ({Formatos.Fecha(o.Asiento.FechaContable)}): {o.Apunte.CodigoCuenta} · " +
            $"{(o.Apunte.Debe.EsCero ? "Haber " + Formatos.Importe(o.Apunte.Haber) : "Debe " + Formatos.Importe(o.Apunte.Debe))} · {o.Apunte.Descripcion ?? o.Asiento.Descripcion}",
        ModoAsientoManual.Correccion when _subcuenta is { } s =>
            $"Subcuenta {s.Cuenta} {s.Nombre} · saldo en el ejercicio {Formatos.Importe(s.Saldo)} · saldo de apertura {Formatos.Importe(s.SaldoApertura)}",
        ModoAsientoManual.Modificar when _original is { } a => $"{Formatos.Fecha(a.FechaContable)} · {a.Descripcion}",
        _ => $"Ejercicio {Contexto.Ejercicio}",
    };

    /// <summary>Un asiento automático se abre en solo lectura: se corrige desde su módulo.</summary>
    public bool SoloLectura { get; private set; }
    public string? AvisoSoloLectura { get; private set; }

    public string Motivo { get => _motivo; set { if (Fijar(ref _motivo, value ?? "")) Recalcular(); } }
    public bool MotivoObligatorio => Modo != ModoAsientoManual.Nuevo;
    public string EtiquetaMotivo => MotivoObligatorio ? "Motivo del cambio *" : "Motivo o nota";
    public string AyudaMotivo => MotivoObligatorio
        ? "Por qué se hace este cambio: es obligatorio y queda como nota del asiento y en su historial"
        : "Nota del asiento (opcional): queda en el asiento y en su historial";

    /// <summary>Documento que se propone en las líneas nuevas.</summary>
    public string Documento { get => _documento; set => Fijar(ref _documento, value ?? ""); }

    // ---------- plantilla ----------

    public ObservableCollection<OpcionPlantillaVM> Plantillas { get; } = [new(null)];

    public OpcionPlantillaVM? Plantilla
    {
        get => _plantilla;
        set { if (Fijar(ref _plantilla, value)) _ = ElegirPlantillaAsync(); }
    }

    public bool HayPlantilla => _plantilla?.Plantilla is not null;
    public string DescripcionPlantilla => _plantilla?.Ayuda ?? "";
    public ObservableCollection<DatoPlantillaVM> Datos { get; } = [];
    public bool HayDatos => Datos.Count > 0;

    public IReadOnlyList<AsientoPrevioVM> VistaPrevia { get => _vistaPrevia; private set => Fijar(ref _vistaPrevia, value); }
    public string? ErrorVistaPrevia { get => _errorVistaPrevia; private set => Fijar(ref _errorVistaPrevia, value); }
    public string TextoVistaPrevia => _vistaConEjemplo
        ? "Ejemplo de la plantilla, con subcuentas e importes ficticios: con «Usar como base» te pido las tuyas y el importe."
        : "Vista previa con tus datos: así quedaría el asiento antes de cambiar nada en la rejilla.";

    /// <summary>Una guía (o un asistente) no corrige nada: se usa como base pidiendo sus subcuentas y su importe.</summary>
    public bool EsGuia => _plantilla?.Plantilla is { Origen: OrigenPlantilla.Ninguno };
    public string TextoAplicar => EsGuia ? "Usar como base" : "Aplicar";

    public bool PuedeAplicar => !SoloLectura && PorQueNoAplicar is null;
    public string AyudaAplicar => PorQueNoAplicar ?? (EsGuia
        ? "Te pido las subcuentas que falten (con el buscador, filtrado por su cuenta) y el importe, y relleno la rejilla; nunca uso las subcuentas del ejemplo"
        : "Copia los asientos de la vista previa a la rejilla; allí los puedes cambiar antes de guardar");

    private string? PorQueNoAplicar => _plantilla?.Plantilla is not { } p ? "Elige primero una plantilla"
        : p.ParteDeUnApunte && _origen is null ? "Esta plantilla corrige un apunte o su asiento: ábrela desde el Mayor, con el apunte elegido, con «Asistente de corrección…»"
        : p.Origen == OrigenPlantilla.Subcuenta && _subcuenta is null ? "Esta plantilla corrige el saldo de una subcuenta: ábrela desde el Mayor, con filas de esa cuenta, con «Asistente de corrección…»"
        : p.Origen != OrigenPlantilla.Ninguno && _errorVistaPrevia is { } e ? "La plantilla no puede proponer el asiento: " + e
        : null;

    public ICommand AplicarCommand { get; }

    // ---------- rejilla ----------

    public ObservableCollection<BloqueAsientoVM> Bloques { get; } = [];
    public IReadOnlyList<OpcionCentroVM> Centros { get; private set; }

    public ICommand AnadirLineaCommand { get; }
    public ICommand InsertarLineaCommand { get; }
    public ICommand DuplicarLineaCommand { get; }
    public ICommand BorrarLineaCommand { get; }
    public ICommand InvertirLineaCommand { get; }
    public ICommand CuadrarConLineaCommand { get; }
    public ICommand BuscarCuentaLineaCommand { get; }
    public ICommand QuitarBloqueCommand { get; }

    // ---------- pie ----------

    public bool TieneCambios => _cambios;
    public bool PuedeGuardar => !_guardando && PorQueNoGuardar is null;
    public string AyudaGuardar => PorQueNoGuardar ?? (Bloques.Count > 1 ? $"Guarda los {Bloques.Count} asientos juntos (Ctrl+G)" : "Guarda el asiento (Ctrl+G)");
    public string? ErrorGuardado { get => _errorGuardado; private set => Fijar(ref _errorGuardado, value); }
    public ICommand GuardarCommand { get; }
    public ICommand CancelarCommand { get; }

    private string? PorQueNoGuardar
    {
        get
        {
            if (SoloLectura) return AvisoSoloLectura ?? "Este asiento es de solo consulta";
            var varios = Bloques.Count > 1;
            foreach (var b in Bloques)
            {
                var cual = varios ? $" del asiento {b.Numero}" : "";
                if (ProblemaFecha(b) is { } f) return f + cual;
                if (string.IsNullOrWhiteSpace(b.Descripcion)) return $"Falta la descripción{cual}";
                var conImporte = b.Lineas.Where(l => !l.Vacia).ToList();
                if (conImporte.FirstOrDefault(l => !l.ImporteValido) is { } mal) return $"El importe de la línea {mal.Numero}{cual} no se entiende";
                if (conImporte.FirstOrDefault(l => !l.CuentaValida || l.Cuenta.Length == 0) is { } sin)
                    return sin.Cuenta.Length == 0 ? $"Falta la cuenta de la línea {sin.Numero}{cual}" : $"La cuenta {sin.Cuenta} de la línea {sin.Numero}{cual} no existe en este ejercicio";
                if (conImporte.FirstOrDefault(l => !l.TieneImporte) is { } cero) return $"La línea {cero.Numero}{cual} no tiene importe: ponlo o bórrala";
                if (conImporte.Count < 2) return $"Faltan apuntes{cual}: un asiento necesita al menos dos";
                if (!b.Cuadrado) return $"No cuadra{cual}: faltan {Formatos.Importe(Math.Abs(b.Diferencia))} en el {(b.Diferencia > 0 ? "Haber" : "Debe")}";
            }
            if (Bloques.Count == 0) return "No hay ningún asiento";
            if (MotivoObligatorio && _motivo.Trim().Length == 0) return "Escribe el motivo del cambio: es obligatorio y queda en el historial";
            return null;
        }
    }

    // ---------- arranque ----------

    /// <summary>Asiento nuevo; con <paramref name="cuenta"/> e <paramref name="importe"/> (desde el Mayor), la primera línea los lleva para dejar a cero ese saldo.</summary>
    public async Task IniciarNuevoAsync(string? cuenta = null, decimal? importe = null)
    {
        Modo = ModoAsientoManual.Nuevo;
        await CargarComunAsync(EsDeAsientoManual);
        var bloque = NuevoBloque(1, FechaPropuesta(DateTime.Today), "");
        if (!string.IsNullOrWhiteSpace(cuenta))
        {
            var l = NuevaLinea(bloque);
            l.Cuenta = cuenta.Trim();
            // El importe de la selección del Mayor (Debe − Haber) al lado contrario: el asiento deja la selección a cero.
            if (importe is { } i && i != 0m) l.PonerImportes(i < 0 ? -i : 0m, i > 0 ? i : 0m);
        }
        else NuevaLinea(bloque);
        NuevaLinea(bloque);
        Bloques.Add(bloque);
        Terminar();
    }

    /// <summary>
    /// Asistente de corrección del Mayor: con un apunte, las correcciones de su asiento, del apunte y de su subcuenta; con solo una cuenta (varias
    /// filas de ella), las de subcuenta. La rejilla empieza vacía hasta aplicar una.
    /// </summary>
    public async Task<Resultado> IniciarCorreccionAsync(int? idApunte, string? cuenta = null)
    {
        Modo = ModoAsientoManual.Correccion;
        if (idApunte is int id)
        {
            var origen = await _fuente.OrigenApunteAsync(id);
            if (!origen.EsCorrecto) return Resultado.Fallo(origen.Errores);
            _origen = origen.Valor;
            cuenta ??= _origen.Apunte.CodigoCuenta;
        }
        if (!string.IsNullOrWhiteSpace(cuenta))
        {
            var sub = await _fuente.OrigenSubcuentaAsync(cuenta.Trim());
            if (sub.EsCorrecto) _subcuenta = sub.Valor;
            else if (_origen is null) return Resultado.Fallo(sub.Errores);
        }
        if (_origen is null && _subcuenta is null)
            return Resultado.Fallo(new Error("Plantillas.SinOrigen", "No sé qué corregir.", null, "Falta el apunte o la subcuenta.", "Elige un apunte, o filas de una sola cuenta, en el Mayor."));
        await CargarComunAsync(p => p.Clase == ClasePlantilla.Correccion && (p.ParteDeUnApunte ? _origen is not null : p.Origen == OrigenPlantilla.Subcuenta && _subcuenta is not null));
        var bloque = NuevoBloque(1, FechaPropuesta(DateTime.Today), "");
        NuevaLinea(bloque);
        NuevaLinea(bloque);
        Bloques.Add(bloque);
        Terminar();
        if (Plantillas.Count > 1) Plantilla = Plantillas[1];
        return Resultado.Correcto();
    }

    /// <summary>Modificación de un asiento que ya existe; uno automático se abre en solo lectura.</summary>
    public async Task<Resultado> IniciarModificarAsync(int idAsiento)
    {
        Modo = ModoAsientoManual.Modificar;
        var asiento = await _fuente.AsientoAsync(idAsiento);
        if (asiento is null)
            return Resultado.Fallo(new Error("Asientos.NoExiste", $"El asiento {idAsiento} ya no existe.", $"Asiento {idAsiento}", "Otra persona lo ha borrado.",
                "Vuelve a cargar el Mayor."));
        _original = asiento;
        await CargarComunAsync(EsDeAsientoManual);
        if (asiento.EsAutomatico)
        {
            SoloLectura = true;
            AvisoSoloLectura = AvisoAutomatico(asiento.Lote!);
        }
        else if (asiento.Contexto.IdSociedadEjercicio != Contexto.IdSociedadEjercicio)
        {
            SoloLectura = true;
            AvisoSoloLectura = $"Este asiento es del ejercicio {asiento.Contexto.Ejercicio} y estás trabajando en {Contexto.Ejercicio}: ábrelo con ese ejercicio de trabajo para cambiarlo.";
        }
        var bloque = NuevoBloque(1, asiento.FechaContable, asiento.Descripcion);
        foreach (var a in asiento.Apuntes.OrderBy(a => a.Orden))
        {
            var l = NuevaLinea(bloque);
            l.Cuenta = a.CodigoCuenta;
            l.PonerConcepto(a.Descripcion ?? asiento.Descripcion, heredado: string.IsNullOrEmpty(a.Descripcion) || a.Descripcion == asiento.Descripcion);
            l.Documento = a.NumeroDocumento ?? "";
            l.PonerImportes(a.Debe.Valor, a.Haber.Valor);
            l.PonerCentro(a.IdCentroCoste);
        }
        Bloques.Add(bloque);
        _motivo = "";
        Terminar();
        return Resultado.Correcto();
    }

    /// <summary>Texto del aviso de un asiento automático: quién lo generó y desde dónde se corrige.</summary>
    public static string AvisoAutomatico(IdentidadLote lote) => lote.MotorPropietario switch
    {
        "compras/factura-compra" => $"Este asiento lo generó la exportación de la factura de compra {lote.ClaveEjecucion}: se corrige desde Compras › Facturas recibidas (deshaz su exportación, corrige la factura y vuelve a exportarla).",
        "facturacion/factura-venta" => $"Este asiento lo generó la contabilización de la factura de venta {lote.ClaveEjecucion}: se corrige desde Ventas › Facturas emitidas (con una rectificativa o deshaciendo su contabilización).",
        OpcionesLote.MotorImportadorContaSol => "Este asiento viene de la importación de ContaSol: se corrige en ContaSol y se vuelve a importar el ejercicio (Configuración › Importar ContaSol).",
        _ => $"Este asiento es automático (lo generó {lote}): se corrige desde el proceso que lo generó, no a mano.",
    };

    /// <summary>En el asiento manual «puro» no se ofrece ninguna corrección: solo guías y asistentes (contabilidad/plantillas-asiento 2026-11-12 20:11).</summary>
    private static bool EsDeAsientoManual(PlantillaAsiento p) => p.Clase is ClasePlantilla.Guia or ClasePlantilla.Asistente && p.Origen == OrigenPlantilla.Ninguno;

    private async Task CargarComunAsync(Func<PlantillaAsiento, bool> ofrecer)
    {
        _entorno = await _fuente.EntornoAsync();
        _cuentas = new Dictionary<string, string>(_entorno.Cuentas ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
        var centros = (await _fuente.CentrosAsync()).Where(c => c.EstaActivo && c.EsImputable).OrderBy(c => c.RutaJerarquica, StringComparer.Ordinal);
        Centros = [new OpcionCentroVM(null), .. centros.Select(c => new OpcionCentroVM(c))];
        var plantillas = await _fuente.PlantillasAsync();
        foreach (var p in plantillas.Where(p => p.Tipo == TipoPlantilla.Usuario && p.Activa && ofrecer(p))) Plantillas.Add(new OpcionPlantillaVM(p));
        _plantilla = Plantillas[0];
        Avisar(nameof(Centros), nameof(Plantilla));
    }

    private void Terminar()
    {
        foreach (var b in Bloques) b.AvisarTitulo();
        _cargando = false;
        _cambios = false;
        Avisar(nameof(Titulo), nameof(Subtitulo), nameof(SoloLectura), nameof(AvisoSoloLectura), nameof(MotivoObligatorio), nameof(EtiquetaMotivo), nameof(AyudaMotivo),
            nameof(TieneCambios), nameof(Motivo));
        foreach (var l in Bloques.SelectMany(b => b.Lineas)) l.AvisarSoloLectura();
        Recalcular(cambio: false);
    }

    /// <summary>Hoy, dentro del ejercicio de trabajo y nunca antes del trimestre activo.</summary>
    internal DateTime FechaPropuesta(DateTime hoy)
    {
        var ejercicio = Contexto.Ejercicio;
        var f = hoy.Year == ejercicio ? hoy.Date : hoy.Year > ejercicio ? new DateTime(ejercicio, 12, 31) : new DateTime(ejercicio, 1, 1);
        var inicio = MotorPlantillas.InicioPeriodo(_entorno);
        return f < inicio ? inicio : f;
    }

    /// <summary>Por qué no vale la fecha del asiento, o null.</summary>
    internal string? ProblemaFecha(BloqueAsientoVM b)
    {
        if (SoloLectura) return null;
        if (b.FechaLeida is not { } f) return "La fecha no se entiende: escríbela como dd/mm/aaaa";
        if (f.Year != Contexto.Ejercicio) return $"La fecha {Formatos.Fecha(f)} no es del ejercicio {Contexto.Ejercicio}";
        var inicio = MotorPlantillas.InicioPeriodo(_entorno);
        return f < inicio ? $"La fecha {Formatos.Fecha(f)} es de un trimestre cerrado: usa el {Formatos.Fecha(inicio)} o una posterior (trimestre activo)" : null;
    }

    // ---------- plantillas ----------

    private async Task ElegirPlantillaAsync()
    {
        Avisar(nameof(HayPlantilla), nameof(DescripcionPlantilla));
        Datos.Clear();
        VistaPrevia = [];
        ErrorVistaPrevia = null;
        _entornoPlantilla = null;
        if (_plantilla?.Plantilla is { } p)
        {
            _entornoPlantilla = p.Datos.Any(d => (d.ValorPropuesto ?? "").Contains("UltimaUsada", StringComparison.OrdinalIgnoreCase))
                ? await _fuente.EntornoAsync(p) : _entorno;
            // Nunca se proponen los datos del ejemplo (sus subcuentas son ficticias): solo los valores propuestos de la plantilla con el origen real.
            var propuestos = MotorPlantillas.ValoresPropuestos(p, OrigenDe(p), _entornoPlantilla);
            foreach (var d in p.Datos.OrderBy(d => d.Orden))
                Datos.Add(new DatoPlantillaVM(this, d, propuestos is not null && propuestos.TryGetValue(d.Nombre, out var v) ? v : null));
            if (MotivoObligatorio && _motivo.Trim().Length == 0 && p.MotivoPropuesto is { } m) { _motivo = m; Avisar(nameof(Motivo)); }
        }
        Avisar(nameof(HayDatos), nameof(EsGuia), nameof(TextoAplicar));
        CalcularVistaPrevia();
    }

    internal void DatoCambiado() => CalcularVistaPrevia();

    private static EjemploPlantilla? EjemploDe(PlantillaAsiento p)
    {
        if (string.IsNullOrWhiteSpace(p.Ejemplo)) return null;
        try { return PlantillasJson.LeerEjemplo(p.Ejemplo); }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or FormatException) { return null; }
    }

    /// <summary>Propone con el origen real (o, sin él, con el ejemplo de la plantilla) y pinta la vista previa; no graba nada.</summary>
    private void CalcularVistaPrevia()
    {
        if (_plantilla?.Plantilla is not { } p) { VistaPrevia = []; ErrorVistaPrevia = null; Recalcular(cambio: false); return; }
        var r = Proponer(p);
        _vistaConEjemplo = false;
        if (!r.EsCorrecto && p.Origen == OrigenPlantilla.Ninguno && EjemploDe(p) is { } ej)
        {
            // Una guía sin sus subcuentas todavía: se enseña su ejemplo ficticio.
            r = MotorPlantillas.Proponer(p, ej.Origen, ej.Datos, ej.Entorno);
            _vistaConEjemplo = r.EsCorrecto;
        }
        Avisar(nameof(TextoVistaPrevia));
        if (r.EsCorrecto)
        {
            var nombres = _vistaConEjemplo ? EjemploDe(p)?.Entorno.Cuentas : _cuentas;
            VistaPrevia = r.Valor.Asientos.Select(a => new AsientoPrevioVM(
                (r.Valor.Asientos.Count > 1 ? $"Asiento {a.NumeroAsiento} · " : "") + $"{Formatos.Fecha(a.Fecha)} · {a.Descripcion}",
                a.Apuntes.Select(x => new ApuntePrevioVM(x.Cuenta, nombres?.GetValueOrDefault(x.Cuenta) ?? x.NombreCuentaNueva ?? "", x.Concepto ?? "",
                    x.AlDebe ? Formatos.Importe(x.Importe) : "", x.AlDebe ? "" : Formatos.Importe(x.Importe))).ToList(),
                Formatos.Importe(a.TotalDebe), Formatos.Importe(a.TotalHaber))).ToList();
            ErrorVistaPrevia = null;
        }
        else
        {
            VistaPrevia = [];
            ErrorVistaPrevia = r.Error!.TextoOperador();
        }
        Avisar(nameof(PuedeAplicar), nameof(AyudaAplicar));
        Recalcular(cambio: false);
    }

    private Resultado<PropuestaPlantilla> Proponer(PlantillaAsiento p)
    {
        var datos = Datos.ToDictionary(d => d.Dato.Nombre, d => d.ValorParaFormulas, StringComparer.OrdinalIgnoreCase);
        return MotorPlantillas.Proponer(p, OrigenDe(p), datos, _entornoPlantilla ?? _entorno);
    }

    /// <summary>Copia los asientos que propone la plantilla a la rejilla (sustituye lo que hubiera).</summary>
    /// <summary>El origen con que se ejecuta la plantilla: el apunte (y su asiento), la subcuenta o nada.</summary>
    private OrigenEjecucion OrigenDe(PlantillaAsiento p) => p.ParteDeUnApunte ? (OrigenEjecucion?)_origen ?? OrigenEjecucion.Ninguno
        : p.Origen == OrigenPlantilla.Subcuenta ? (OrigenEjecucion?)_subcuenta ?? OrigenEjecucion.Ninguno
        : OrigenEjecucion.Ninguno;

    /// <summary>
    /// «Aplicar» (o «Usar como base» en una guía): en una guía, antes pide con el buscador común, filtrado por su cuenta, cada subcuenta que falte
    /// y después el importe; cancelar cualquiera de ellos no cambia la rejilla. Después copia los asientos propuestos a la rejilla.
    /// </summary>
    public void Aplicar()
    {
        if (!PuedeAplicar || _plantilla?.Plantilla is not { } p) return;
        if (EsGuia)
        {
            foreach (var d in Datos.Where(d => d.EsCuenta && d.Valor.Trim().Length == 0).ToList())
            {
                if (ElegirCuenta(null, d.Dato.FiltroCuenta is { Length: > 0 } f ? [f] : [], d.Dato.Etiqueta) is not { } c) return;
                d.Valor = c;
            }
            foreach (var d in Datos.Where(d => d.EsImporte && d.Valor.Trim().Length == 0).ToList())
            {
                if (_dialogos.PedirImporte(d.Dato.Etiqueta, d.Dato.Ayuda ?? d.Dato.Etiqueta) is not { } importe) return;
                d.Valor = Formatos.Numero(importe);
            }
        }
        var r = Proponer(p);
        if (!r.EsCorrecto) { ErrorVistaPrevia = r.Error!.TextoOperador(); return; }
        _aplicada = p;
        _datosAplicados = r.Valor.Datos;
        Bloques.Clear();
        foreach (var a in r.Valor.Asientos)
        {
            var b = NuevoBloque(a.NumeroAsiento, a.Fecha, a.Descripcion);
            foreach (var x in a.Apuntes)
            {
                var l = NuevaLinea(b, copiarAnterior: false);
                l.NombreCuentaNueva = x.NombreCuentaNueva;
                l.Cuenta = x.Cuenta;
                l.PonerConcepto(x.Concepto ?? a.Descripcion, heredado: x.Concepto is null || x.Concepto == a.Descripcion);
                l.Documento = x.Documento ?? "";
                l.PonerImportes(x.Debe, x.Haber);
                l.PonerCentro(x.IdCentroCoste);
            }
            Bloques.Add(b);
        }
        Renumerar();
        Marcar();
    }

    // ---------- cuentas ----------

    /// <summary>Nombre de una cuenta del ejercicio, o null si no existe.</summary>
    public string? NombreCuenta(string codigo) => _cuentas.TryGetValue(codigo.Trim(), out var n) ? n : null;

    internal void BuscarCuentaDato(DatoPlantillaVM dato)
    {
        var grupos = dato.Dato.FiltroCuenta is { Length: > 0 } f ? new[] { f } : [];
        if (ElegirCuenta(dato.Valor, grupos, dato.Dato.Etiqueta) is { } c) dato.Valor = c;
    }

    /// <summary>Abre el buscador común para la cuenta de la línea; si se crea una subcuenta, queda en la lista de cuentas.</summary>
    public void BuscarCuentaLinea(LineaAsientoVM linea)
    {
        if (SoloLectura) return;
        if (ElegirCuenta(linea.Cuenta, [], "Cuenta del apunte") is { } c) linea.Cuenta = c;
    }

    private string? ElegirCuenta(string? texto, IReadOnlyList<string> grupos, string titulo)
    {
        if (_dialogos.ElegirCuenta(texto, grupos, titulo) is not { } elegida) return null;
        if (!_cuentas.ContainsKey(elegida.Codigo))
        {
            _cuentas[elegida.Codigo] = elegida.Nombre;
            foreach (var l in Bloques.SelectMany(b => b.Lineas)) l.AvisarCuenta();
        }
        return elegida.Codigo;
    }

    // ---------- líneas ----------

    private BloqueAsientoVM NuevoBloque(int numero, DateTime fecha, string descripcion) => new(this, numero, fecha, descripcion);

    /// <summary>Línea nueva al final del bloque: concepto y documento se copian de la anterior (o el documento de la cabecera).</summary>
    private LineaAsientoVM NuevaLinea(BloqueAsientoVM b, bool copiarAnterior = true, int? posicion = null)
    {
        var l = new LineaAsientoVM(b);
        l.PonerCentro(null);
        var anterior = posicion is int p && p > 0 ? b.Lineas[p - 1] : b.Lineas.LastOrDefault();
        l.PonerConcepto(b.Descripcion, heredado: true);
        if (copiarAnterior) l.Documento = anterior?.Documento is { Length: > 0 } d ? d : _documento;
        if (posicion is int i && i < b.Lineas.Count) b.Lineas.Insert(i, l);
        else b.Lineas.Add(l);
        return l;
    }

    public void AnadirLinea(BloqueAsientoVM? b)
    {
        if (SoloLectura || b is null) return;
        NuevaLinea(b);
        b.Renumerar();
        Marcar();
    }

    public void InsertarDespues(LineaAsientoVM l)
    {
        if (SoloLectura) return;
        NuevaLinea(l.Bloque, posicion: l.Bloque.Lineas.IndexOf(l) + 1);
        l.Bloque.Renumerar();
        Marcar();
    }

    public void Duplicar(LineaAsientoVM l)
    {
        if (SoloLectura) return;
        var copia = NuevaLinea(l.Bloque, copiarAnterior: false, posicion: l.Bloque.Lineas.IndexOf(l) + 1);
        copia.NombreCuentaNueva = l.NombreCuentaNueva;
        copia.Cuenta = l.Cuenta;
        copia.PonerConcepto(l.Concepto, heredado: l.ConceptoHeredado);
        copia.Documento = l.Documento;
        copia.PonerImportes(l.ValorDebe, l.ValorHaber);
        copia.Centro = l.Centro;
        l.Bloque.Renumerar();
        Marcar();
    }

    /// <summary>Quita un asiento de la pantalla (no se graba nada hasta Guardar); siempre queda al menos uno.</summary>
    public void QuitarBloque(BloqueAsientoVM b)
    {
        if (SoloLectura || Bloques.Count < 2) return;
        Bloques.Remove(b);
        Renumerar();
        Marcar();
    }

    public void Borrar(LineaAsientoVM l)
    {
        if (SoloLectura) return;
        var b = l.Bloque;
        b.Lineas.Remove(l);
        if (b.Lineas.Count == 0) NuevaLinea(b);
        b.Renumerar();
        Marcar();
    }

    /// <summary>Pasa el importe de la línea al lado contrario.</summary>
    public void Invertir(LineaAsientoVM l)
    {
        if (SoloLectura) return;
        l.PonerImportes(l.ValorHaber, l.ValorDebe);
        Marcar();
    }

    /// <summary>Pone en esta línea el importe que hace cuadrar el asiento (al lado que falte).</summary>
    public void CuadrarCon(LineaAsientoVM l)
    {
        if (SoloLectura) return;
        var resto = l.Bloque.Lineas.Where(x => x != l).Sum(x => x.ValorDebe - x.ValorHaber);
        l.PonerImportes(resto < 0 ? -resto : 0m, resto > 0 ? resto : 0m);
        Marcar();
    }

    private void Renumerar()
    {
        var n = 1;
        foreach (var b in Bloques) { b.Numero = n++; b.AvisarTitulo(); b.Renumerar(); }
    }

    private void Marcar()
    {
        if (!_cambios) { _cambios = true; Avisar(nameof(TieneCambios)); }
        Recalcular();
    }

    /// <summary>Recalcula lo que depende de todo (Guardar y su ayuda) y, si es una edición, anota que hay cambios.</summary>
    internal void Recalcular(bool cambio = true)
    {
        if (cambio && !_cargando && !_guardando && !SoloLectura && !_cambios) { _cambios = true; Avisar(nameof(TieneCambios)); }
        foreach (var b in Bloques) b.AvisarFecha();
        Avisar(nameof(PuedeGuardar), nameof(AyudaGuardar));
        CommandManager.InvalidateRequerySuggested();
    }

    // ---------- guardar ----------

    /// <summary>Guarda: corrección o plantilla con varios asientos por el servicio de plantillas (relación, IdLote y punteo); si no, por el motor de asientos.</summary>
    public async Task<bool> GuardarAsync()
    {
        if (!PuedeGuardar) { ErrorGuardado = PorQueNoGuardar; return false; }
        _guardando = true;
        ErrorGuardado = null;
        Recalcular(cambio: false);
        try
        {
            var motivo = _motivo.Trim();
            Resultado<IReadOnlyList<int>> r;
            if (Modo == ModoAsientoManual.Modificar && _original is { } a)
            {
                var b = Bloques[0];
                a.FechaContable = b.FechaLeida!.Value;
                a.Descripcion = b.Descripcion.Trim();
                a.Notas = motivo.Length > 0 ? motivo : a.Notas;
                a.Apuntes.Clear();
                a.Apuntes.AddRange(Apuntes(b));
                var m = await _fuente.ModificarAsync(a, motivo);
                r = m.EsCorrecto ? Resultado<IReadOnlyList<int>>.Correcto([m.Valor.IdAsiento]) : Resultado<IReadOnlyList<int>>.Fallo(m.Errores);
            }
            else if (_aplicada is not null && (Modo == ModoAsientoManual.Correccion || Bloques.SelectMany(x => x.Lineas).Any(l => l.NombreCuentaNueva is not null)))
            {
                // Corrección (relación con el asiento de origen y punteo si hay apunte) o subcuentas «?» por crear: por el servicio de plantillas.
                var deApunte = _aplicada.ParteDeUnApunte ? _origen : null;
                var propuesta = new PropuestaPlantilla(_aplicada, Contexto, Bloques.Select(Propuesto).ToList(), _datosAplicados,
                    deApunte?.Asiento.IdAsiento, deApunte?.Apunte.IdApunte);
                r = await _fuente.EjecutarPlantillaAsync(propuesta, motivo.Length > 0 ? motivo : $"Alta con la plantilla «{_aplicada.Nombre}»");
            }
            else
            {
                var asientos = Bloques.Select(b =>
                {
                    var x = new Asiento(Contexto, b.FechaLeida!.Value, b.Descripcion.Trim())
                    {
                        Notas = motivo.Length > 0 ? motivo : null,
                        IdPlantilla = _aplicada is { IdPlantilla: > 0 } p ? p.IdPlantilla : null,
                    };
                    x.Apuntes.AddRange(Apuntes(b));
                    return x;
                }).ToList();
                r = await _fuente.GuardarNuevosAsync(asientos, motivo.Length > 0 ? motivo : _aplicada is { } p2 ? $"Alta manual con la plantilla «{p2.Nombre}»" : "Alta manual");
            }
            if (!r.EsCorrecto)
            {
                ErrorGuardado = string.Join(" ", r.Errores.Select(e => e.TextoOperador()));
                _dialogos.Error("No he guardado el asiento", ErrorGuardado);
                return false;
            }
            _cambios = false;
            Avisar(nameof(TieneCambios));
            Guardado?.Invoke(r.Valor);
            return true;
        }
        finally
        {
            _guardando = false;
            Recalcular(cambio: false);
        }
    }

    /// <summary>Descarta los cambios: no se graba nada.</summary>
    public void Descartar()
    {
        _cambios = false;
        Avisar(nameof(TieneCambios));
    }

    public IReadOnlyList<string> DescribirCambios() =>
        !_cambios ? [] : [Modo == ModoAsientoManual.Modificar ? $"Cambios sin guardar en el asiento {_original?.IdAsiento}" : $"{Titulo} sin guardar ({Bloques.Sum(b => b.Lineas.Count(l => !l.Vacia))} líneas)"];

    private static IEnumerable<Apunte> Apuntes(BloqueAsientoVM b) =>
        b.Lineas.Where(l => !l.Vacia).Select(l =>
        {
            var x = l.ValorDebe != 0m ? Apunte.AlDebe(l.Cuenta, l.ValorDebe, Nulo(l.Concepto)) : Apunte.AlHaber(l.Cuenta, l.ValorHaber, Nulo(l.Concepto));
            x.NumeroDocumento = Nulo(l.Documento);
            x.IdCentroCoste = l.Centro?.Centro?.IdCentroCoste;
            return x;
        });

    private static AsientoPropuesto Propuesto(BloqueAsientoVM b) =>
        new(b.Numero, b.FechaLeida!.Value, b.Descripcion.Trim(), b.Lineas.Where(l => !l.Vacia).Select(l => new ApuntePropuesto(
            l.Cuenta, l.ValorDebe != 0m, l.ValorDebe != 0m ? l.ValorDebe : l.ValorHaber, Nulo(l.Concepto), Nulo(l.Documento), l.Centro?.Centro?.IdCentroCoste,
            l.NombreCuentaNueva)).ToList());

    private static string? Nulo(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();
}
