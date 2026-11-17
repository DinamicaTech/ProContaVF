using System.Globalization;
using System.Windows.Input;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Activos.UI;

/// <summary>Un tipo de activo para elegirlo en el primer paso, con las cuentas del PGC que usa.</summary>
public sealed record OpcionTipoAlta(TipoActivo Tipo)
{
    public string Nombre => Tipo.Nombre;
    public string Cuentas => Tipo.Codigo switch
    {
        TiposActivoPgc.Vehiculo => "218 · 2818 · 681",
        TiposActivoPgc.Instalacion => "212 · 2812 · 681",
        TiposActivoPgc.Aplicacion => "206 · 2806 · 680",
        _ => "221 · 282 · 682",
    };
}

/// <summary>
/// «Nuevo activo…» en cuatro pasos con lenguaje llano (activos 2026-11-17 18:50): qué es, la compra (con el catastro si es un inmueble y el centro de
/// coste, elegido a mano: uno que existe o uno nuevo bajo un padre), la amortización y un repaso de lo que se va a crear. Al confirmar se graba todo en
/// una transacción; las subcuentas se generan después con su propuesta. No se graba ningún asiento.
/// </summary>
public sealed class AltaActivoVM : ModeloVista
{
    public static readonly IReadOnlyList<string> Pasos = ["Qué es", "La compra", "La amortización", "Repaso"];
    private static readonly CultureInfo Es = Formatos.Cultura;

    private readonly ContextoActivos _ctx;
    private readonly IDialogosActivos _dialogos;
    private int _paso = 1;
    private OpcionTipoAlta? _tipo;
    private bool _esAjeno, _seAmortiza = true, _baseEscrita, _centroNuevo = true, _ocupado;
    private string _nombre = "", _nombreCorto = "", _codigo = "", _fecha = "", _coste = "", _referencia = "", _suelo = "", _construccion = "", _pctConstruccion = "",
        _pctAnual = "", _base = "", _residual = "0", _errores = "";
    private CentroParaActivo? _centro, _padre;
    private int _lineasPlantilla;
    private bool _codigoEscrito;

    public AltaActivoVM(ContextoActivos ctx, IDialogosActivos dialogos)
    {
        _ctx = ctx;
        _dialogos = dialogos;
        SiguienteCommand = new ComandoDelegado(() => _ = SiguienteAsync(), () => !_ocupado);
        AtrasCommand = new ComandoDelegado(() => Paso--, () => _paso > 1 && !_ocupado);
        ElegirCentroCommand = new ComandoDelegado(() => ElegirCentro(nuevo: false));
        ElegirPadreCommand = new ComandoDelegado(() => ElegirCentro(nuevo: true));
    }

    /// <summary>Lo lanza el VM cuando el alta termina bien: la ventana se cierra aceptando.</summary>
    public event EventHandler? Creado;

    public int? IdCreado { get; private set; }
    public IReadOnlyList<OpcionTipoAlta> Tipos { get; private set; } = [];
    private IReadOnlyList<CentroParaActivo> _centros = [];

    public async Task CargarAsync()
    {
        Tipos = (await _ctx.Servicio.TiposAsync()).Select(t => new OpcionTipoAlta(t)).ToList();
        _centros = await _ctx.Servicio.CentrosAsync(_ctx.Trabajo.IdSociedad);
        Tipo = Tipos.FirstOrDefault(t => t.Tipo.Codigo == TiposActivoPgc.Vivienda) ?? Tipos.FirstOrDefault();
        Avisar(nameof(Tipos));
    }

    // ---------- comandos y pasos ----------
    public ICommand SiguienteCommand { get; }
    public ICommand AtrasCommand { get; }
    public ICommand ElegirCentroCommand { get; }
    public ICommand ElegirPadreCommand { get; }

    public int Paso
    {
        get => _paso;
        private set
        {
            if (!Fijar(ref _paso, Math.Clamp(value, 1, 4))) return;
            Errores = "";
            Avisar(nameof(EnPaso1), nameof(EnPaso2), nameof(EnPaso3), nameof(EnPaso4), nameof(TextoSiguiente), nameof(AyudaSiguiente), nameof(TituloPaso), nameof(Repaso),
                nameof(PuedeAtras));
            CommandManager.InvalidateRequerySuggested();
        }
    }
    public bool EnPaso1 => _paso == 1;
    public bool EnPaso2 => _paso == 2;
    public bool EnPaso3 => _paso == 3;
    public bool EnPaso4 => _paso == 4;
    public bool PuedeAtras => _paso > 1;
    public string TituloPaso => $"Paso {_paso} de 4 · {Pasos[_paso - 1]}";
    public string TextoSiguiente => _paso == 4 ? "Crear el activo" : "Siguiente";
    public string AyudaSiguiente => _paso == 4 ? "Graba la ficha, el centro y el plan; después te enseña sus subcuentas para confirmarlas" : "Comprueba este paso y pasa al siguiente";
    public string Errores { get => _errores; private set { if (Fijar(ref _errores, value)) Avisar(nameof(HayErrores)); } }
    public bool HayErrores => _errores.Length > 0;
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    // ---------- paso 1 ----------
    public OpcionTipoAlta? Tipo
    {
        get => _tipo;
        set
        {
            if (!Fijar(ref _tipo, value)) return;
            if (value is not null)
            {
                _pctAnual = (value.Tipo.PorcentajeHabitual * 100m).ToString("0.##", Es);
                _seAmortiza = value.Tipo.AmortizablePredeterminado || value.Tipo.PorcentajeHabitual > 0m;
            }
            Avisar(nameof(EsInmueble), nameof(PorcentajeAnual), nameof(SeAmortiza), nameof(TextoHabitual), nameof(TextoBase));
            ProponerBase();
        }
    }
    public bool EsAjeno { get => _esAjeno; set { if (Fijar(ref _esAjeno, value)) Avisar(nameof(Propio)); } }
    public bool Propio => !_esAjeno;
    public bool EsInmueble => _tipo?.Tipo.EsInmueble == true;

    // ---------- paso 2 ----------
    public string Nombre { get => _nombre; set => Fijar(ref _nombre, value); }
    public string NombreCorto
    {
        get => _nombreCorto;
        set
        {
            if (!Fijar(ref _nombreCorto, value)) return;
            if (!_codigoEscrito) { _codigo = CodigoDe(value); Avisar(nameof(Codigo)); }
        }
    }
    public string Codigo { get => _codigo; set { if (Fijar(ref _codigo, value)) _codigoEscrito = true; } }
    public string FechaCompra { get => _fecha; set => Fijar(ref _fecha, value); }
    public string Coste { get => _coste; set { if (Fijar(ref _coste, value)) ProponerBase(); } }
    public string Referencia { get => _referencia; set => Fijar(ref _referencia, value); }
    public string Suelo { get => _suelo; set { if (Fijar(ref _suelo, value)) CalcularConstruccion(); } }
    public string Construccion { get => _construccion; set { if (Fijar(ref _construccion, value)) CalcularConstruccion(); } }
    public string PorcentajeConstruccion { get => _pctConstruccion; set { if (Fijar(ref _pctConstruccion, value)) ProponerBase(); } }
    public bool CentroNuevo { get => _centroNuevo; set { if (Fijar(ref _centroNuevo, value)) Avisar(nameof(CentroExistente)); } }
    public bool CentroExistente { get => !_centroNuevo; set => CentroNuevo = !value; }
    public string TextoCentro => _centro?.Texto ?? "ninguno elegido";
    public string TextoPadre => _padre?.Texto ?? "ninguno elegido";
    public string AyudaCentroNuevo => $"Se crea un centro con el código «{ReglasFichaActivo.CodigoCentro(_codigo.Length > 0 ? _codigo : "…")}» y el nombre «{(NombreCortoONombre is { Length: > 0 } n ? n : "…")}» dentro del centro que elijas, del que heredará la actividad de IVA.";
    private string NombreCortoONombre => _nombreCorto.Trim().Length > 0 ? _nombreCorto.Trim() : _nombre.Trim();

    private void CalcularConstruccion()
    {
        if (ReglasFichaActivo.PorcentajeConstruccion(FichaActivoVM.Importe(_suelo), FichaActivoVM.Importe(_construccion)) is decimal p)
        {
            _pctConstruccion = (p * 100m).ToString("0.##", Es);
            Avisar(nameof(PorcentajeConstruccion));
            ProponerBase();
        }
    }

    private void ElegirCentro(bool nuevo)
    {
        var c = _dialogos.ElegirCentro(nuevo ? "Centro padre del centro nuevo" : "Centro de coste del activo",
            nuevo ? "El centro nuevo del activo se crea dentro del que elijas (por ejemplo «INMUEBLES» o «TIENDA») y hereda su actividad de IVA."
                  : "Elige el centro donde se apuntarán sus gastos e ingresos. Mejor uno que no use otro activo: si lo comparten, su cuenta de resultados solo cuenta sus subcuentas.",
            _centros.Where(x => x.EstaActivo && (nuevo || x.EsImputable)).ToList(), nuevo ? _padre?.IdCentroCoste : _centro?.IdCentroCoste, soloImputables: !nuevo);
        if (c is null) return;
        if (nuevo) { _padre = c; Avisar(nameof(TextoPadre)); }
        else { _centro = c; Avisar(nameof(TextoCentro)); }
    }

    // ---------- paso 3 ----------
    public bool SeAmortiza { get => _seAmortiza; set => Fijar(ref _seAmortiza, value); }
    public string PorcentajeAnual { get => _pctAnual; set { if (Fijar(ref _pctAnual, value)) Avisar(nameof(TextoCuota)); } }
    public string Base { get => _base; set { if (Fijar(ref _base, value)) { _baseEscrita = true; Avisar(nameof(TextoCuota)); } } }
    public string Residual { get => _residual; set => Fijar(ref _residual, value); }
    public string TextoHabitual => _tipo is null ? "" : $"Para un activo de tipo «{_tipo.Nombre}» lo habitual es el {(_tipo.Tipo.PorcentajeHabitual * 100m).ToString("0.##", Es)} % anual. Puedes cambiarlo.";
    public string TextoBase => EsInmueble ? "Se propone el coste por el % de construcción: el suelo no se amortiza." : "Se propone el coste entero.";
    public string TextoCuota => Construir().CuotaAnual is var c && c > 0m ? $"Cuota de un año completo: {Formatos.Importe(c)}. El primer año cuenta solo los días desde la fecha de inicio." : "";

    private void ProponerBase()
    {
        if (_baseEscrita) return;
        var coste = FichaActivoVM.Importe(_coste) ?? 0m;
        _base = Formatos.Numero(ReglasFichaActivo.BasePropuesta(coste, EsInmueble, FichaActivoVM.Porcentaje(_pctConstruccion)));
        Avisar(nameof(Base), nameof(TextoCuota));
    }

    // ---------- paso 4 ----------
    public IReadOnlyList<string> Repaso
    {
        get
        {
            if (_paso != 4) return [];
            var d = Construir();
            var r = new List<string> { $"Guardar la ficha del activo «{d.Nombre}» ({d.Codigo}), de tipo «{_tipo?.Nombre}»{(d.EsAjeno ? ", marcada como ajena a la sociedad" : "")}." };
            if (d.EsAjeno)
            {
                r.Add("No crear subcuentas, centro de coste ni plan de amortización: es solo informativo.");
            }
            else
            {
                r.Add(_centroNuevo ? $"Crear su centro de coste «{ReglasFichaActivo.CodigoCentro(d.Codigo)} {NombreCortoONombre}» dentro de «{_padre?.Texto}», del que hereda la actividad de IVA."
                    : $"Usar el centro de coste «{_centro?.Texto}».");
                if (d.EsInmueble) r.Add("Guardar sus datos de inmueble (catastro y % de construcción).");
                r.Add(d.SeAmortiza ? $"Dejar preparado su plan de amortización: {(d.PorcentajeAnual * 100m).ToString("0.##", Es)} % anual sobre {Formatos.Importe(d.BaseAmortizable)} desde el {Formatos.Fecha(d.InicioAmortizacion ?? DateTime.Today)}."
                    : "No se amortiza: no se le prepara plan de amortización.");
                r.Add(_lineasPlantilla > 0 ? $"Después, proponerte sus {_lineasPlantilla} subcuentas según la plantilla de su tipo: las verás antes de confirmarlas."
                    : "Su tipo no tiene plantilla de subcuentas: no se le creará ninguna (puedes añadirla en Parámetros y tablas › Plantillas de subcuentas).");
            }
            r.Add("No se graba ningún asiento: el alta contable de la compra sigue viniendo de la factura de compra.");
            return r;
        }
    }

    // ---------- avanzar ----------
    private async Task SiguienteAsync()
    {
        var errores = ErroresDelPaso(_paso);
        if (_paso == 2 && errores.Count == 0 && await _ctx.Servicio.CodigoRepetidoAsync(_ctx.Trabajo.IdSociedad, _codigo, 0))
            errores = [$"Ya hay otro activo con el código «{_codigo.Trim()}» en esta sociedad: elige otro."];
        if (errores.Count > 0) { Errores = string.Join(Environment.NewLine, errores.Select(e => "· " + e)); return; }
        if (_paso == 1 && !_esAjeno && _tipo is not null) _lineasPlantilla = await _ctx.Servicio.LineasPlantillaAsync(_tipo.Tipo.IdTipoActivo);
        if (_paso == 2 && !_baseEscrita) ProponerBase();
        if (_paso < 4) { Paso++; return; }
        await CrearAsync();
    }

    private async Task CrearAsync()
    {
        Ocupado = true;
        try
        {
            var r = await _ctx.Servicio.AltaAsync(_ctx.Trabajo, new AltaActivo(Construir(), !_esAjeno && _centroNuevo ? _padre?.IdCentroCoste : null));
            if (!r.EsCorrecto) { Errores = string.Join(Environment.NewLine, r.Errores.Select(e => "· " + e.TextoOperador())); return; }
            IdCreado = r.Valor;
            Creado?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) { Errores = $"No se ha creado el activo: {ex.Message} No se ha grabado nada; vuelve a pulsar «Crear el activo»."; }
        finally { Ocupado = false; }
    }

    /// <summary>Los errores de un paso, con qué hacer. Los textos mal escritos se dicen aquí; el resto, con las reglas de la ficha.</summary>
    public IReadOnlyList<string> ErroresDelPaso(int paso)
    {
        var e = new List<string>();
        switch (paso)
        {
            case 1:
                if (_tipo is null) e.Add("Elige qué clase de activo es.");
                break;
            case 2:
                if (_fecha.Trim().Length > 0 && FichaActivoVM.Fecha(_fecha) is null) e.Add("Escribe la fecha de compra como dd/mm/aaaa (por ejemplo 13/02/2023).");
                if (_coste.Trim().Length > 0 && FichaActivoVM.Importe(_coste) is null) e.Add("Escribe el coste como un importe (por ejemplo 125.000,00).");
                if (!_esAjeno && _coste.Trim().Length == 0) e.Add("Escribe el coste de compra: el precio más los gastos de compra.");
                foreach (var (t, n) in new[] { (_suelo, "suelo"), (_construccion, "construcción") })
                    if (EsInmueble && t.Trim().Length > 0 && FichaActivoVM.Importe(t) is null) e.Add($"Escribe el valor catastral del {n} como un importe.");
                if (EsInmueble && _pctConstruccion.Trim().Length > 0 && FichaActivoVM.Porcentaje(_pctConstruccion) is null) e.Add("El % de construcción va de 0 a 100.");
                if (!_esAjeno && _centroNuevo && _padre is null) e.Add("Elige dentro de qué centro se crea su centro nuevo («Elegir…»).");
                if (!_esAjeno && !_centroNuevo && _centro is null) e.Add("Elige el centro de coste que ya existe («Elegir…»).");
                e.AddRange(ReglasFichaActivo.Validar(Construir() with { IdCentroCoste = _esAjeno ? null : -1, SeAmortiza = false })
                    .Where(x => x.Campo is not nameof(DatosActivo.IdCentroCoste)).Select(x => x.Texto));
                break;
            case 3:
                if (_esAjeno || !_seAmortiza) break;
                if (FichaActivoVM.Porcentaje(_pctAnual) is null) e.Add("Escribe el % anual entre 0 y 100 (por ejemplo 3).");
                if (FichaActivoVM.Importe(_base) is null) e.Add("Escribe la base que se amortiza como un importe.");
                if (_residual.Trim().Length > 0 && FichaActivoVM.Importe(_residual) is null) e.Add("Escribe el valor residual como un importe (lo normal es 0).");
                if (e.Count == 0)
                    e.AddRange(ReglasFichaActivo.Validar(Construir() with { IdCentroCoste = -1 })
                        .Where(x => x.Campo is nameof(DatosActivo.InicioAmortizacion) or nameof(DatosActivo.PorcentajeAnual) or nameof(DatosActivo.BaseAmortizable)
                            or nameof(DatosActivo.ValorResidual)).Select(x => x.Texto));
                break;
        }
        return e.Distinct().ToList();
    }

    /// <summary>Los datos escritos, como los recibe el servicio.</summary>
    public DatosActivo Construir()
    {
        var inmueble = EsInmueble
            ? new DatosInmueble(_referencia.Trim().Length > 0 ? _referencia.Trim() : null, FichaActivoVM.Importe(_suelo), FichaActivoVM.Importe(_construccion),
                FichaActivoVM.Porcentaje(_pctConstruccion))
            : null;
        var fecha = FichaActivoVM.Fecha(_fecha);
        return new DatosActivo
        {
            Codigo = _codigo.Trim(), Nombre = _nombre.Trim(), NombreCorto = _nombreCorto.Trim().Length > 0 ? _nombreCorto.Trim() : null,
            IdTipoActivo = _tipo?.Tipo.IdTipoActivo ?? 0, CodigoTipo = _tipo?.Tipo.Codigo ?? "", EsAjeno = _esAjeno,
            FechaCompra = fecha, Coste = FichaActivoVM.Importe(_coste) ?? 0m, IdCentroCoste = _esAjeno || _centroNuevo ? null : _centro?.IdCentroCoste, Inmueble = inmueble,
            SeAmortiza = _seAmortiza && !_esAjeno, InicioAmortizacion = fecha, BaseAmortizable = FichaActivoVM.Importe(_base) ?? 0m,
            ValorResidual = FichaActivoVM.Importe(_residual) ?? 0m, PorcentajeAnual = FichaActivoVM.Porcentaje(_pctAnual) ?? 0m,
        };
    }

    /// <summary>Código propuesto a partir del nombre corto: sin espacios ni «/», a 40 caracteres.</summary>
    public static string CodigoDe(string nombreCorto)
    {
        var c = string.Concat((nombreCorto ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)).Replace("/", "-");
        return c.Length <= ReglasFichaActivo.LongitudCodigo ? c : c[..ReglasFichaActivo.LongitudCodigo];
    }
}
