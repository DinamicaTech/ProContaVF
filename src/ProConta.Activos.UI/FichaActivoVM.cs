using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Input;
using ProConta.Activos.Gestion;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Contenedor;
using ProConta.UI.Base.Controles;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Graficos;
using ProConta.UI.Base.Modelos;
using ProConta.UI.Base.Tablas;

namespace ProConta.Activos.UI;

/// <summary>Un indicador de la cabecera de la ficha: título, valor y una línea de explicación.</summary>
public sealed record IndicadorFicha(string Titulo, string Valor, string Detalle, bool Negativo = false);

/// <summary>Una fila de la tabla de la cuenta de resultados (también los títulos de grupo, los totales y el resultado).</summary>
public sealed class FilaCuentaVM(string clase, string cuenta, string concepto, IReadOnlyList<decimal>? meses, decimal? importe, decimal? anterior, decimal ingresos,
    ICommand? mayor = null, string? ayuda = null)
{
    /// <summary>GRUPO, FILA, CALCULADA, TOTAL o RESULTADO: el estilo de la fila.</summary>
    public string Clase { get; } = clase;
    public string Cuenta { get; } = cuenta;
    public string Concepto { get; } = concepto;
    public IReadOnlyList<decimal>? MesesValores { get; } = meses;
    public decimal? ImporteValor { get; } = importe;
    public decimal? AnteriorValor { get; } = anterior;
    public string Importe => importe is decimal v ? Formatos.Importe(v) : "";
    public string Anterior => anterior is decimal v ? Formatos.Importe(v) : "";
    public string Diferencia => importe is decimal v && anterior is decimal a ? Formatos.Importe(v - a) : "";
    public string Porcentaje => importe is decimal v && ingresos != 0m && Clase != "GRUPO"
        ? (v / ingresos * 100m).ToString("0.0", Formatos.Cultura) + " %" : "";
    public string Mes(int i) => meses is null || meses[i] == 0m ? "" : Formatos.Numero(meses[i]);
    public string M1 => Mes(0); public string M2 => Mes(1); public string M3 => Mes(2); public string M4 => Mes(3);
    public string M5 => Mes(4); public string M6 => Mes(5); public string M7 => Mes(6); public string M8 => Mes(7);
    public string M9 => Mes(8); public string M10 => Mes(9); public string M11 => Mes(10); public string M12 => Mes(11);
    public bool Negativo => importe < 0m && Clase is "RESULTADO" or "TOTAL";
    public ICommand? MayorCommand { get; } = mayor;
    public string? Ayuda { get; } = ayuda ?? (mayor is null ? null : $"Doble clic: el Mayor de la subcuenta {cuenta}");
}

/// <summary>Una subcuenta del apartado Subcuentas.</summary>
public sealed record FilaSubcuentaVM(string Codigo, string Nombre, string Papel, string Saldo, bool EnEjercicio, ICommand? MayorCommand)
{
    public string Ayuda => EnEjercicio ? $"Doble clic: el Mayor de la {Codigo}" : $"No existe en el ejercicio de trabajo (es su código de un ejercicio anterior): «Generar las que falten…» la da de alta";
}

/// <summary>Una fila de la tabla de amortización.</summary>
public sealed record FilaAmortizacionVM(string Ejercicio, string Dias, string Cuota, string Acumulado, string Pendiente, string Estado, string ClaseEstado, string Ayuda);

/// <summary>
/// Ficha de un activo en su pestaña (activos 2026-11-17 18:50): cabecera con nombre, código, tipo, estado, centro y actividad; indicadores del ejercicio de
/// trabajo y los apartados Datos (con Guardar y Cancelar al pie), Cuenta de resultados, Facturas, Ventas, Subcuentas y Amortización. Un activo ajeno solo tiene Datos.
/// </summary>
public sealed partial class FichaActivoVM : ModeloVista, IPestanaTrabajo, INotifyDataErrorInfo
{
    private static readonly CultureInfo Es = Formatos.Cultura;
    private static readonly string[] Meses = ["Ene", "Feb", "Mar", "Abr", "May", "Jun", "Jul", "Ago", "Sep", "Oct", "Nov", "Dic"];

    private readonly ModuloActivos _modulo;
    private object? _vista;
    private FichaActivo? _ficha;
    private DatosActivo? _original;
    private EstadoVista _estado = EstadoVista.Cargando;
    private string _mensajeEstado = "";
    private bool _ocupado;
    private bool _guardadoFallido;
    private ApartadoFicha _apartado;
    private Dictionary<string, string> _errores = [];
    private bool _intentoGuardar;
    private bool _cargando;

    internal FichaActivoVM(ModuloActivos modulo, int idActivo, long orden)
    {
        _modulo = modulo;
        IdActivo = idActivo;
        OrdenApertura = orden;
        GuardarCommand = new ComandoDelegado(() => _ = GuardarAsync(), () => !_ocupado && TieneCambios);
        CancelarCommand = new ComandoDelegado(Descartar, () => !_ocupado && TieneCambios);
        RefrescarCommand = new ComandoDelegado(() => _ = RefrescarAsync(), () => !_ocupado);
        ElegirCentroCommand = new ComandoDelegado(() => _ = ElegirCentroAsync(), () => !_ocupado && !EsAjeno);
        MayorCommand = new ComandoDelegado(() => _ = MayorAsync(null, []), () => _modulo.Contexto.AbrirMayor is not null && _ficha is { Datos.EsAjeno: false });
        CalcularConstruccionCommand = new ComandoDelegado(CalcularConstruccion, () => EsInmueble);
        ExportarCuentaCommand = new ComandoDelegado(ExportarCuenta, () => _cuenta is not null);
        GenerarSubcuentasCommand = new ComandoDelegado(() => _ = GenerarAsync(), () => !_ocupado && _ficha is { Datos.EsAjeno: false, DeBaja: false });
        AbrirAmortizacionCommand = new ComandoDelegado(() => _modulo.Contexto.AbrirAmortizacion?.Invoke(), () => _modulo.Contexto.AbrirAmortizacion is not null);
    }

    public int IdActivo { get; }
    public long OrdenApertura { get; }

    // ---------- pestaña ----------
    public string Titulo => _ficha is null ? "Activo" : (_ficha.Datos.NombreCorto ?? _ficha.Datos.Codigo);
    public string Ayuda => _ficha is null ? "Ficha de activo" : $"{_ficha.Datos.Nombre}" + (TieneCambios ? " · cambios sin guardar" : "");
    public string Icono => "layers";
    public bool GuardadoFallido { get => _guardadoFallido; private set => Fijar(ref _guardadoFallido, value); }
    public object Vista => _vista ??= new Vistas.VistaFichaActivo { DataContext = this };
    public void AlCerrar() => _modulo.FichaCerrada(this);

    public bool TieneCambios => _original is not null && !_cargando && Construir() is var d && d != _original;

    public IReadOnlyList<string> DescribirCambios() => TieneCambios ? [$"Datos de {Titulo} sin guardar"] : [];

    public void Descartar()
    {
        if (_original is not null) Poner(_original);
        _intentoGuardar = false;
        Revalidar();
        Cambio();
    }

    // ---------- comandos ----------
    public ICommand GuardarCommand { get; }
    public ICommand CancelarCommand { get; }
    public ICommand RefrescarCommand { get; }
    public ICommand ElegirCentroCommand { get; }
    public ICommand MayorCommand { get; }
    public ICommand CalcularConstruccionCommand { get; }
    public ICommand ExportarCuentaCommand { get; }
    public ICommand GenerarSubcuentasCommand { get; }
    public ICommand AbrirAmortizacionCommand { get; }

    // ---------- estado ----------
    public EstadoVista Estado { get => _estado; private set => Fijar(ref _estado, value); }
    public string MensajeEstado { get => _mensajeEstado; private set => Fijar(ref _mensajeEstado, value); }
    public bool Ocupado { get => _ocupado; private set { if (Fijar(ref _ocupado, value)) CommandManager.InvalidateRequerySuggested(); } }

    public ApartadoFicha Apartado
    {
        get => _apartado;
        set
        {
            if (_ficha is { Datos.EsAjeno: true }) value = ApartadoFicha.Datos;
            if (!Fijar(ref _apartado, value)) return;
            Avisar(nameof(IndiceApartado), nameof(EnDatos));
            _ = CargarApartadoAsync();
        }
    }

    /// <summary>Índice de la pestaña del apartado (para el TabControl).</summary>
    public int IndiceApartado { get => (int)_apartado; set => Apartado = (ApartadoFicha)Math.Clamp(value, 0, 5); }
    public bool EnDatos => _apartado == ApartadoFicha.Datos;

    // ---------- cabecera ----------
    public string Nombre => _ficha?.Datos.Nombre ?? "";
    public string Codigo => _ficha?.Datos.Codigo ?? "";
    public string TipoTexto => _ficha?.Tipo ?? "";
    public string EstadoTexto => _ficha?.Estado ?? "";
    public string ClaseEstado => _ficha is null ? "EN_USO" : _ficha.Datos.EsAjeno ? "AJENO" : _ficha.DeBaja ? "BAJA" : "EN_USO";
    public bool Propio => _ficha is { Datos.EsAjeno: false };
    public string CentroTexto => _ficha?.Centro ?? "sin centro";
    public string ActividadTexto => _ficha?.Actividad ?? "sin actividad (usa la general)";
    public IReadOnlyList<IndicadorFicha> Indicadores { get; private set; } = [];

    // ---------- carga ----------
    public async Task CargarAsync()
    {
        try
        {
            Estado = EstadoVista.Cargando;
            Ocupado = true;
            if (!await _modulo.Contexto.Servicio.PuedeAsync())
            {
                Estado = EstadoVista.SinPermiso;
                MensajeEstado = "No tienes permiso para ver ni modificar los activos. Pídeselo a quien administra los permisos (acción «Activos»).";
                return;
            }
            _tipos = await _modulo.Contexto.Servicio.TiposAsync();
            _ejercicios = await _modulo.Contexto.Servicio.EjerciciosAsync(_modulo.Contexto.Trabajo.IdSociedad);
            await LeerFichaAsync();
            if (_ficha is null)
            {
                Estado = EstadoVista.Error;
                MensajeEstado = "Este activo ya no existe (puede que lo haya borrado otra persona). Cierra la pestaña y actualiza el listado.";
                return;
            }
            Estado = EstadoVista.Contenido;
            await IndicadoresAsync();
            if (_ficha.Datos.EsAjeno) _apartado = ApartadoFicha.Datos;
            Avisar(nameof(Apartado), nameof(IndiceApartado), nameof(EnDatos));
            await CargarApartadoAsync();
        }
        catch (Exception ex)
        {
            Estado = EstadoVista.Error;
            MensajeEstado = $"No he podido leer el activo: {ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.";
        }
        finally { Ocupado = false; }
    }

    private async Task RefrescarAsync()
    {
        if (TieneCambios && _modulo.Dialogos.Preguntar("Cambios sin guardar", "Actualizar vuelve a leer el activo y perderías los cambios que no has guardado.", null,
                new OpcionMensaje("Descartar y actualizar", "Pierde los cambios y lee el activo de nuevo"), new OpcionMensaje("Seguir editando", "No hace nada", Principal: true, Cancelar: true)) != 0)
            return;
        _cuenta = null; _subcuentas = null; _amortizacion = null; _compras = null; _ventas = null;
        await CargarAsync();
    }

    private async Task LeerFichaAsync()
    {
        _ficha = await _modulo.Contexto.Servicio.FichaAsync(_modulo.Contexto.Trabajo, IdActivo);
        if (_ficha is null) return;
        _original = _ficha.Datos;
        Poner(_ficha.Datos);
        // Lo que se ve, leído de nuevo, es la referencia de «sin cambios» (los importes se enseñan a céntimos y el % de construcción a dos decimales).
        _original = Construir();
        _intentoGuardar = false;
        Revalidar();
        Avisar(nameof(Nombre), nameof(Codigo), nameof(TipoTexto), nameof(EstadoTexto), nameof(ClaseEstado), nameof(Propio), nameof(CentroTexto), nameof(ActividadTexto),
            nameof(Titulo), nameof(Ayuda), nameof(MotivoBloqueo), nameof(TipoEditable), nameof(AvisoAjeno), nameof(Tipos));
        Cambio();
    }

    private async Task IndicadoresAsync()
    {
        if (_ficha is not { } f || f.Datos.EsAjeno) { Indicadores = []; Avisar(nameof(Indicadores)); return; }
        var ej = _modulo.Contexto.Trabajo.Ejercicio;
        var r = await _modulo.Contexto.Servicio.CuentaResultadosAsync(_modulo.Contexto.Trabajo.IdSociedad, IdActivo, ej, false, false);
        var c = r?.Actual;
        var prevista = c?.Filas.Where(x => x.Tipo == TipoFilaResultado.AmortizacionPrevista).Sum(x => x.Total) ?? 0m;
        var pct = f.Amortizable > 0m ? Math.Round(f.Amortizado / f.Amortizable * 100m, 0) : 0m;
        Indicadores =
        [
            new("Coste de compra", Formatos.Importe(f.Datos.Coste), f.Datos.FechaCompra is DateTime fc ? $"comprado el {Formatos.Fecha(fc)}" : "sin fecha de compra"),
            new("Amortizado", Formatos.Importe(f.Amortizado), f.Amortizable > 0m ? $"{pct.ToString("0", Es)} % de {Formatos.Importe(f.Amortizable)}" : "sin plan de amortización"),
            new($"Ingresos de {ej}", Formatos.Importe(c?.Ingresos ?? 0m), "grupo 7 de sus subcuentas y su centro"),
            new($"Gastos de {ej}", Formatos.Importe(c?.Gastos ?? 0m), prevista > 0m ? "con la amortización prevista" : "grupo 6 de sus subcuentas y su centro"),
            new($"Resultado de {ej}", Formatos.Importe(c?.Resultado ?? 0m), c?.Rentabilidad(f.Datos.Coste) is decimal ren && (c?.Ingresos ?? 0m) != 0m
                ? $"rentabilidad sobre el coste: {(ren * 100m).ToString("0.0", Es)} %" : "sin ingresos propios", (c?.Resultado ?? 0m) < 0m),
        ];
        Avisar(nameof(Indicadores));
    }

    private async Task CargarApartadoAsync()
    {
        if (_ficha is null) return;
        try
        {
            switch (_apartado)
            {
                case ApartadoFicha.CuentaResultados when _cuenta is null: await CargarCuentaAsync(); break;
                case ApartadoFicha.Facturas when _compras is null: await CargarComprasAsync(); break;
                case ApartadoFicha.Ventas when _ventas is null: await CargarVentasAsync(); break;
                case ApartadoFicha.Subcuentas when _subcuentas is null: await CargarSubcuentasAsync(); break;
                case ApartadoFicha.Amortizacion when _amortizacion is null: await CargarAmortizacionAsync(); break;
            }
        }
        catch (Exception ex)
        {
            _modulo.Dialogos.Error("No he podido leer este apartado", $"{ex.Message} Pulsa Actualizar (F5) para volver a intentarlo.");
        }
    }

    // ================= Datos =================

    private IReadOnlyList<TipoActivo> _tipos = [];
    private IReadOnlyList<EjercicioSociedad> _ejercicios = [];
    private string _codigo = "", _nombre = "", _nombreCorto = "", _notas = "", _fechaCompra = "", _coste = "", _referencia = "", _suelo = "", _construccion = "",
        _porcentajeConstruccion = "", _alquiler = "", _inicioAlquiler = "", _tributo = "", _inicioAmortizacion = "", _base = "", _residual = "", _porcentajeAnual = "";
    private TipoActivo? _tipo;
    private bool _esAjeno, _seAmortiza;
    private int? _idCentro;
    private string? _textoCentro;

    public IReadOnlyList<TipoActivo> Tipos => _tipos;
    public string? MotivoBloqueo => _ficha?.MotivoBloqueoTipo;
    public bool TipoEditable => MotivoBloqueo is null;
    public string AvisoAjeno => "Este activo no es de la sociedad. Se guarda solo para tener su información: no tiene subcuentas, centro de coste ni amortización, y no genera ningún asiento.";

    public string CodigoActivo { get => _codigo; set => Editar(ref _codigo, value); }
    public string NombreActivo { get => _nombre; set => Editar(ref _nombre, value); }
    public string NombreCorto { get => _nombreCorto; set => Editar(ref _nombreCorto, value); }
    public string Notas { get => _notas; set => Editar(ref _notas, value); }
    public TipoActivo? Tipo
    {
        get => _tipo;
        set { if (Editar(ref _tipo, value)) Avisar(nameof(EsInmueble), nameof(VerInmueble)); }
    }
    public bool EsAjeno
    {
        get => _esAjeno;
        set { if (Editar(ref _esAjeno, value)) Avisar(nameof(VerAmortizacion), nameof(VerCompraPropia), nameof(VerInmueble)); }
    }
    public string FechaCompra { get => _fechaCompra; set => Editar(ref _fechaCompra, value); }
    public string Coste { get => _coste; set => Editar(ref _coste, value); }
    public string Centro => _textoCentro ?? (_idCentro is null ? "sin centro: elígelo" : "");
    public string Referencia { get => _referencia; set => Editar(ref _referencia, value); }
    public string Suelo { get => _suelo; set { if (Editar(ref _suelo, value)) CalcularConstruccion(); } }
    public string Construccion { get => _construccion; set { if (Editar(ref _construccion, value)) CalcularConstruccion(); } }
    public string PorcentajeConstruccion { get => _porcentajeConstruccion; set => Editar(ref _porcentajeConstruccion, value); }
    public string Alquiler { get => _alquiler; set => Editar(ref _alquiler, value); }
    public string InicioAlquiler { get => _inicioAlquiler; set => Editar(ref _inicioAlquiler, value); }
    public string Tributo { get => _tributo; set => Editar(ref _tributo, value); }
    public bool SeAmortiza { get => _seAmortiza; set { if (Editar(ref _seAmortiza, value)) Avisar(nameof(VerPlan)); } }
    public string InicioAmortizacion { get => _inicioAmortizacion; set => Editar(ref _inicioAmortizacion, value); }
    public string Base { get => _base; set { if (Editar(ref _base, value)) Avisar(nameof(CuotaAnual)); } }
    public string Residual { get => _residual; set => Editar(ref _residual, value); }
    public string PorcentajeAnual { get => _porcentajeAnual; set { if (Editar(ref _porcentajeAnual, value)) Avisar(nameof(CuotaAnual)); } }

    public bool EsInmueble => _tipo?.EsInmueble == true;
    public bool VerInmueble => EsInmueble;
    public bool VerCompraPropia => !_esAjeno;
    public bool VerAmortizacion => !_esAjeno;
    public bool VerPlan => _seAmortiza && !_esAjeno;
    public string CuotaAnual => Construir().CuotaAnual is var c && c > 0m ? Formatos.Importe(c) : "";
    public string PieDatos => _errores.Count > 0 && _intentoGuardar ? $"Revisa {Formatos.Plural(_errores.Count, "dato", "datos")} marcados en rojo: el aviso de cada uno dice cómo arreglarlo."
        : TieneCambios ? "Hay cambios sin guardar." : "Sin cambios.";

    private bool Editar<T>(ref T campo, T valor, [System.Runtime.CompilerServices.CallerMemberName] string? propiedad = null)
    {
        if (!Fijar(ref campo, valor, propiedad)) return false;
        if (!_cargando) { Revalidar(); Cambio(); }
        return true;
    }

    private void Cambio()
    {
        Avisar(nameof(TieneCambios), nameof(Ayuda), nameof(PieDatos), nameof(CuotaAnual));
        CommandManager.InvalidateRequerySuggested();
    }

    private void Poner(DatosActivo d)
    {
        _cargando = true;
        try
        {
            CodigoActivo = d.Codigo;
            NombreActivo = d.Nombre;
            NombreCorto = d.NombreCorto ?? "";
            Notas = d.Notas ?? "";
            Tipo = _tipos.FirstOrDefault(t => t.IdTipoActivo == d.IdTipoActivo);
            EsAjeno = d.EsAjeno;
            FechaCompra = TextoFecha(d.FechaCompra);
            Coste = Formatos.Numero(d.Coste);
            _idCentro = d.IdCentroCoste;
            _textoCentro = d.IdCentroCoste is null ? null : _ficha?.Centro;
            var i = d.Inmueble ?? new DatosInmueble();
            Referencia = i.ReferenciaCatastral ?? "";
            _suelo = TextoImporte(i.ValorSuelo); _construccion = TextoImporte(i.ValorConstruccion);
            Avisar(nameof(Suelo), nameof(Construccion));
            PorcentajeConstruccion = i.PorcentajeConstruccion is decimal p ? (p * 100m).ToString("0.##", Es) : "";
            Alquiler = TextoImporte(i.AlquilerMensual);
            InicioAlquiler = TextoFecha(i.InicioAlquiler);
            Tributo = TextoImporte(i.TributoMetropolitano);
            SeAmortiza = d.SeAmortiza;
            InicioAmortizacion = TextoFecha(d.InicioAmortizacion);
            Base = Formatos.Numero(d.BaseAmortizable);
            Residual = Formatos.Numero(d.ValorResidual);
            PorcentajeAnual = (d.PorcentajeAnual * 100m).ToString("0.####", Es);
            Avisar(nameof(Centro));
        }
        finally { _cargando = false; }
    }

    /// <summary>Los datos tal como están escritos (lo que no se puede leer se toma como cero o vacío; sus errores salen en <see cref="Revalidar"/>).</summary>
    private DatosActivo Construir()
    {
        var b = _original ?? new DatosActivo();
        var inmueble = _tipo?.EsInmueble == true
            ? new DatosInmueble(Vacio(_referencia), Importe(_suelo), Importe(_construccion), Porcentaje(_porcentajeConstruccion), Importe(_alquiler), Fecha(_inicioAlquiler), Importe(_tributo))
            : b.Inmueble;
        return b with
        {
            Codigo = _codigo.Trim(), Nombre = _nombre.Trim(), NombreCorto = Vacio(_nombreCorto), Notas = Vacio(_notas),
            IdTipoActivo = _tipo?.IdTipoActivo ?? 0, CodigoTipo = _tipo?.Codigo ?? "", EsAjeno = _esAjeno,
            FechaCompra = Fecha(_fechaCompra), Coste = Importe(_coste) ?? 0m, IdCentroCoste = _esAjeno ? b.IdCentroCoste : _idCentro, Inmueble = inmueble,
            SeAmortiza = _seAmortiza, InicioAmortizacion = Fecha(_inicioAmortizacion), BaseAmortizable = Importe(_base) ?? 0m, ValorResidual = Importe(_residual) ?? 0m,
            PorcentajeAnual = Porcentaje(_porcentajeAnual) ?? 0m,
        };
    }

    private void Revalidar()
    {
        var e = new Dictionary<string, string>();
        void Leer(string campo, string texto, Func<string, bool> lee, string mensaje) { if (texto.Trim().Length > 0 && !lee(texto)) e.TryAdd(campo, mensaje); }
        Leer(nameof(FechaCompra), _fechaCompra, t => Fecha(t) is not null, "Escribe la fecha como dd/mm/aaaa (por ejemplo 13/02/2023).");
        Leer(nameof(Coste), _coste, t => Importe(t) is not null, "Escribe un importe (por ejemplo 125.000,00).");
        Leer(nameof(Suelo), _suelo, t => Importe(t) is not null, "Escribe un importe, el del recibo del IBI.");
        Leer(nameof(Construccion), _construccion, t => Importe(t) is not null, "Escribe un importe, el del recibo del IBI.");
        Leer(nameof(PorcentajeConstruccion), _porcentajeConstruccion, t => Porcentaje(t) is not null, "Escribe un porcentaje entre 0 y 100 (por ejemplo 48,51).");
        Leer(nameof(Alquiler), _alquiler, t => Importe(t) is not null, "Escribe un importe mensual.");
        Leer(nameof(InicioAlquiler), _inicioAlquiler, t => Fecha(t) is not null, "Escribe la fecha como dd/mm/aaaa.");
        Leer(nameof(Tributo), _tributo, t => Importe(t) is not null, "Escribe un importe.");
        Leer(nameof(InicioAmortizacion), _inicioAmortizacion, t => Fecha(t) is not null, "Escribe la fecha como dd/mm/aaaa.");
        Leer(nameof(Base), _base, t => Importe(t) is not null, "Escribe un importe.");
        Leer(nameof(Residual), _residual, t => Importe(t) is not null, "Escribe un importe (lo normal es 0).");
        Leer(nameof(PorcentajeAnual), _porcentajeAnual, t => Porcentaje(t) is not null, "Escribe un porcentaje entre 0 y 100 (por ejemplo 3).");
        foreach (var x in ReglasFichaActivo.Validar(Construir()))
            e.TryAdd(x.Campo switch
            {
                nameof(DatosActivo.Codigo) => nameof(CodigoActivo), nameof(DatosActivo.Nombre) => nameof(NombreActivo), nameof(DatosActivo.IdTipoActivo) => nameof(Tipo),
                nameof(DatosActivo.IdCentroCoste) => nameof(Centro), nameof(DatosActivo.BaseAmortizable) => nameof(Base), nameof(DatosActivo.ValorResidual) => nameof(Residual),
                "Inmueble.ReferenciaCatastral" => nameof(Referencia), "Inmueble.ValorSuelo" => nameof(Suelo), "Inmueble.ValorConstruccion" => nameof(Construccion),
                "Inmueble.PorcentajeConstruccion" => nameof(PorcentajeConstruccion), "Inmueble.AlquilerMensual" => nameof(Alquiler), "Inmueble.TributoMetropolitano" => nameof(Tributo),
                var c => c,
            }, x.Texto);
        var antes = _errores.Keys.Concat(e.Keys).Distinct().ToList();
        _errores = e;
        foreach (var c in antes) ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(c));
        Avisar(nameof(HasErrors), nameof(PieDatos));
    }

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
    public bool HasErrors => _errores.Count > 0;
    public IEnumerable GetErrors(string? propertyName) => propertyName is not null && _errores.TryGetValue(propertyName, out var e) ? new[] { e } : Array.Empty<string>();

    private void CalcularConstruccion()
    {
        if (_cargando) return;
        if (ReglasFichaActivo.PorcentajeConstruccion(Importe(_suelo), Importe(_construccion)) is decimal p)
            PorcentajeConstruccion = (p * 100m).ToString("0.##", Es);
    }

    private async Task ElegirCentroAsync()
    {
        try
        {
            var centros = await _modulo.Contexto.Servicio.CentrosAsync(_modulo.Contexto.Trabajo.IdSociedad);
            var c = _modulo.Dialogos.ElegirCentro("Centro de coste del activo",
                "En este centro se apuntan sus gastos e ingresos y de él hereda la actividad de IVA. Elige uno que no use otro activo: si lo comparten, su cuenta de resultados solo cuenta sus subcuentas.",
                centros.Where(x => x.EstaActivo && x.EsImputable).ToList(), _idCentro, soloImputables: true);
            if (c is null) return;
            _idCentro = c.IdCentroCoste;
            _textoCentro = c.Texto;
            Avisar(nameof(Centro));
            Revalidar();
            Cambio();
        }
        catch (Exception ex) { _modulo.Dialogos.Error("No he podido leer los centros de coste", $"{ex.Message} Vuelve a intentarlo."); }
    }

    public async Task<bool> GuardarAsync()
    {
        if (_ficha is null || !TieneCambios) return true;
        _intentoGuardar = true;
        Revalidar();
        if (_errores.Count > 0)
        {
            Apartado = ApartadoFicha.Datos;
            _modulo.Dialogos.Error("Hay datos que revisar", string.Join(Environment.NewLine, _errores.Values.Distinct()));
            GuardadoFallido = true;
            return false;
        }
        var d = Construir();
        Ocupado = true;
        try
        {
            var cambio = await _modulo.Contexto.Servicio.ProponerCambioTipoAsync(_modulo.Contexto.Trabajo, d);
            if (!cambio.EsCorrecto) return Fallo("No se puede guardar", cambio.Errores.Select(e => e.TextoOperador()));
            if (cambio.Valor is { HayAlgo: true } c && !ConfirmarCambio(c, d)) return false;
            var r = await _modulo.Contexto.Servicio.GuardarAsync(_modulo.Contexto.Trabajo, d, _ficha.Version);
            if (!r.EsCorrecto) return Fallo("No se ha guardado el activo", r.Errores.Select(e => e.TextoOperador()));
            GuardadoFallido = false;
            _modulo.Contexto.Avisar($"Activo {d.Codigo} guardado.");
            if (r.Valor.Count > 0)
                _modulo.Dialogos.Error("Guardado, con avisos", "Los datos se han guardado, pero con las subcuentas ha pasado esto:" + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, r.Valor) + Environment.NewLine + Environment.NewLine + "Revísalo en el apartado Subcuentas.");
            _cuenta = null; _subcuentas = null; _amortizacion = null; _compras = null; _ventas = null;
            await LeerFichaAsync();
            await IndicadoresAsync();
            await CargarApartadoAsync();
            _modulo.Cambiado();
            return true;
        }
        catch (Exception ex)
        {
            return Fallo("No se ha guardado el activo", [$"{ex.Message} Tus cambios siguen en la ficha: vuelve a pulsar Guardar."]);
        }
        finally { Ocupado = false; }
    }

    private bool Fallo(string titulo, IEnumerable<string> errores)
    {
        GuardadoFallido = true;
        _modulo.Dialogos.Error(titulo, string.Join(Environment.NewLine + Environment.NewLine, errores));
        return false;
    }

    private bool ConfirmarCambio(CambioTipoActivo c, DatosActivo d)
    {
        var detalles = new List<string>();
        detalles.AddRange(c.Borrar.Select(s => $"Se borra la {s.Codigo} «{s.Nombre}»: nunca ha tenido apuntes."));
        detalles.AddRange(c.Desvincular.Select(s => $"Se desvincula la {s.Codigo} «{s.Nombre}»: tiene apuntes antiguos y se queda en el plan de cuentas, pero ya no es del activo."));
        if (c.QuitarCentroYPlan) detalles.Add("Se le quita el centro de coste y se desactiva su plan de amortización.");
        if (c.GenerarNuevas) detalles.Add($"Se generan las subcuentas de la plantilla de «{_tipo?.Nombre}».");
        var titulo = c.QuitarCentroYPlan ? "Marcar el activo como ajeno" : _original?.EsAjeno == true && !d.EsAjeno ? "Pasar el activo a la sociedad" : $"Cambiar el tipo a «{_tipo?.Nombre}»";
        return _modulo.Dialogos.Preguntar(titulo, "Al guardar se hará esto con sus subcuentas. No se graba ningún asiento.", detalles,
            new OpcionMensaje("Guardar", "Guarda los datos y hace estos cambios en sus subcuentas", Principal: true),
            new OpcionMensaje("Cancelar", "No guarda nada; tus cambios siguen en la ficha", Cancelar: true)) == 0;
    }

    private async Task MayorAsync(IReadOnlyList<string>? cuentas, IReadOnlyList<int> ejercicios)
    {
        if (_modulo.Contexto.AbrirMayor is not { } abrir) return;
        try
        {
            cuentas ??= await _modulo.Contexto.Servicio.CuentasDeActivosAsync(_modulo.Contexto.Trabajo, [IdActivo]);
            if (cuentas.Count == 0) { _modulo.Dialogos.Error("Sin subcuentas", "Este activo no tiene subcuentas en el ejercicio de trabajo. Ve a Subcuentas › «Generar las que falten…»."); return; }
            abrir(cuentas, ejercicios);
        }
        catch (Exception ex) { _modulo.Dialogos.Error("No he podido abrir el Mayor", $"{ex.Message} Vuelve a intentarlo."); }
    }

    // ================= Cuenta de resultados =================

    private ResultadosDeActivo? _cuenta;
    private int? _ejercicioCuenta;
    private bool _porMeses, _comparar = true, _generales;

    public IReadOnlyList<int> Ejercicios => _ejercicios.Select(e => e.Ejercicio).ToList();
    public int EjercicioCuenta
    {
        get => _ejercicioCuenta ?? _modulo.Contexto.Trabajo.Ejercicio;
        set { if (value != EjercicioCuenta) { _ejercicioCuenta = value; Avisar(nameof(EjercicioCuenta), nameof(EjercicioAnterior), nameof(TextoComparar)); _ = RecargarCuentaAsync(); } }
    }
    public int EjercicioAnterior => EjercicioCuenta - 1;
    public string TextoComparar => $"Comparar con {EjercicioAnterior}";
    public bool PorMeses { get => _porMeses; set { if (Fijar(ref _porMeses, value)) Avisar(nameof(EnResumen)); } }
    public bool EnResumen { get => !_porMeses; set => PorMeses = !value; }
    public bool Comparar { get => _comparar; set { if (Fijar(ref _comparar, value)) _ = RecargarCuentaAsync(); } }
    public bool AnadirGenerales { get => _generales; set { if (Fijar(ref _generales, value)) _ = RecargarCuentaAsync(); } }
    public bool VerAnterior => _cuenta?.Anterior is not null;
    public IReadOnlyList<FilaCuentaVM> FilasCuenta { get; private set; } = [];
    public DatosGrafico? Grafico { get; private set; }
    public string AvisoCuenta { get; private set; } = "";
    public string AyudaGenerales => "Suma su parte de los gastos generales de la sociedad (los mismos del Impuesto sobre Sociedades: grupo 6 sin centro o de la actividad general, " +
        "con financieros y amortizaciones), en proporción a sus ingresos ordinarios (cuentas 70 a 75) sobre los de toda la sociedad. Un activo sin ingresos no recibe nada.";
    public string TextoGenerales => _cuenta?.Generales is { } g && _generales
        ? $"Gastos generales de la sociedad: {Formatos.Importe(g.GastosGenerales)} · sus ingresos ordinarios son el " +
          $"{(g.IngresosOrdinariosSociedad > 0m ? (g.IngresosOrdinariosActivo / g.IngresosOrdinariosSociedad * 100m).ToString("0.00", Es) : "0")} % de los de la sociedad " +
          $"({Formatos.Importe(g.IngresosOrdinariosActivo)} de {Formatos.Importe(g.IngresosOrdinariosSociedad)}) → le tocan {Formatos.Importe(g.Parte)}."
        : "";
    public bool HayTextoGenerales => TextoGenerales.Length > 0;

    private async Task RecargarCuentaAsync()
    {
        if (_ficha is null) return;
        try { await CargarCuentaAsync(); }
        catch (Exception ex) { _modulo.Dialogos.Error("No he podido calcular la cuenta de resultados", $"{ex.Message} Pulsa Actualizar (F5) para volver a intentarlo."); }
    }

    private async Task CargarCuentaAsync()
    {
        Ocupado = true;
        try
        {
            _cuenta = await _modulo.Contexto.Servicio.CuentaResultadosAsync(_modulo.Contexto.Trabajo.IdSociedad, IdActivo, EjercicioCuenta, _comparar, _generales);
        }
        finally { Ocupado = false; }
        var c = _cuenta?.Actual;
        if (c is null) { FilasCuenta = []; Grafico = null; Avisar(nameof(FilasCuenta), nameof(Grafico)); return; }
        var filas = new List<FilaCuentaVM>();
        var anterior = _cuenta!.Anterior;
        var comparadas = CalculoCuentaResultados.Comparar(c, anterior);
        decimal? Ant(decimal v) => anterior is null ? null : v;
        void Grupo(bool ingresos)
        {
            filas.Add(new FilaCuentaVM("GRUPO", "", ingresos ? "Ingresos" : "Gastos", null, null, null, c.Ingresos));
            var deGrupo = comparadas.Where(f => f.EsIngreso == ingresos).ToList();
            if (deGrupo.Count == 0)
                filas.Add(new FilaCuentaVM("CALCULADA", "", ingresos ? "Este activo no tiene ingresos propios en el año." : "Este activo no tiene gastos en el año.", null, null, null, c.Ingresos));
            foreach (var f in deGrupo)
            {
                var calculada = f.Tipo != TipoFilaResultado.Subcuenta;
                var ayuda = f.Tipo switch
                {
                    TipoFilaResultado.AmortizacionPrevista => "Lo que propondrá Contabilidad › Amortización de activos para el año y aún no está contabilizado. Se suma al resultado para que la cifra no engañe.",
                    TipoFilaResultado.GastosGenerales => AyudaGenerales,
                    _ => null,
                };
                filas.Add(new FilaCuentaVM(calculada ? "CALCULADA" : "FILA", f.Cuenta, f.Nombre, f.Meses, f.Actual, f.Anterior, c.Ingresos,
                    calculada ? null : new ComandoDelegado(() => _ = MayorAsync([f.Cuenta], [EjercicioCuenta])), ayuda));
            }
            var meses = Enumerable.Range(0, 12).Select(m => ingresos ? c.IngresosMes(m) : c.GastosMes(m)).ToList();
            filas.Add(new FilaCuentaVM("TOTAL", "", ingresos ? "Total de ingresos" : "Total de gastos", meses, ingresos ? c.Ingresos : c.Gastos,
                Ant(ingresos ? anterior?.Ingresos ?? 0m : anterior?.Gastos ?? 0m), c.Ingresos));
        }
        Grupo(true);
        Grupo(false);
        filas.Add(new FilaCuentaVM("RESULTADO", "", "Resultado del activo", Enumerable.Range(0, 12).Select(m => c.IngresosMes(m) - c.GastosMes(m)).ToList(), c.Resultado,
            Ant(anterior?.Resultado ?? 0m), c.Ingresos));
        FilasCuenta = filas;
        var ingresosMes = Enumerable.Range(0, 12).Select(m => (decimal?)c.IngresosMes(m)).ToList();
        var gastosMes = Enumerable.Range(0, 12).Select(m => (decimal?)c.GastosMesSinAmortizacion(m)).ToList();
        Grafico = new DatosGrafico(Meses, [new SerieGrafico("Ingresos", PaletaGraficos.Serie1, ingresosMes), new SerieGrafico("Gastos", PaletaGraficos.Serie2, gastosMes)],
            i => new ContenidoTooltip(Meses[i], [new FilaTooltip("Ingresos", Formatos.Importe(ingresosMes[i] ?? 0m), PaletaGraficos.Serie1),
                new FilaTooltip("Gastos", Formatos.Importe(gastosMes[i] ?? 0m), PaletaGraficos.Serie2),
                new FilaTooltip("Diferencia", Formatos.Importe((ingresosMes[i] ?? 0m) - (gastosMes[i] ?? 0m)), Destacada: true)]),
            Descripcion: $"Ingresos y gastos de {c.Ejercicio} por mes, sin la amortización");
        var hoy = DateTime.Today;
        AvisoCuenta = c.Ejercicio == hoy.Year
            ? $"Ejercicio en curso: cifras hasta hoy, {Formatos.Fecha(hoy)}." + (c.Filas.Any(f => f.Tipo == TipoFilaResultado.AmortizacionPrevista)
                ? " La amortización se contabiliza desde Procesos contables; mientras no esté contabilizada se muestra la prevista." : "")
            : c.Filas.Any(f => f.Tipo == TipoFilaResultado.AmortizacionPrevista) ? $"La amortización de {c.Ejercicio} aún no está contabilizada del todo: se muestra la prevista." : "";
        Avisar(nameof(FilasCuenta), nameof(Grafico), nameof(AvisoCuenta), nameof(VerAnterior), nameof(TextoGenerales), nameof(HayTextoGenerales), nameof(Ejercicios));
    }

    private void ExportarCuenta()
    {
        if (_cuenta?.Actual is not { } c) return;
        var ruta = _modulo.Dialogos.GuardarExcel($"Cuenta de resultados {Codigo} {c.Ejercicio}.xlsx");
        if (ruta is null) return;
        try
        {
            var columnas = new List<ColumnaExportable>
            {
                new("Subcuenta", f => ((FilaCuentaVM)f).Cuenta),
                new("Concepto", f => ((FilaCuentaVM)f).Concepto),
            };
            for (var m = 0; m < 12; m++)
            {
                var mes = m;
                columnas.Add(new(Meses[mes], f => ((FilaCuentaVM)f).MesesValores is { } v ? v[mes] : null));
            }
            columnas.Add(new(c.Ejercicio.ToString(CultureInfo.InvariantCulture), f => ((FilaCuentaVM)f).ImporteValor));
            if (_cuenta.Anterior is not null)
            {
                columnas.Add(new((c.Ejercicio - 1).ToString(CultureInfo.InvariantCulture), f => ((FilaCuentaVM)f).AnteriorValor));
                columnas.Add(new("Diferencia", f => ((FilaCuentaVM)f).ImporteValor is decimal v && ((FilaCuentaVM)f).AnteriorValor is decimal a ? v - a : null));
            }
            ExportadorExcel.Exportar(ruta, columnas, FilasCuenta, $"Resultados {c.Ejercicio}",
                $"Cuenta de resultados de {Nombre} ({Codigo}) · {c.Ejercicio}" + (_generales ? " · con su parte de gastos generales" : ""));
            _modulo.Dialogos.AbrirFichero(ruta);
        }
        catch (Exception ex) { _modulo.Dialogos.Error("No he podido guardar el Excel", $"{ex.Message} Si el fichero está abierto en Excel, ciérralo y vuelve a intentarlo."); }
    }

    // ================= Subcuentas =================

    private SubcuentasDeActivo? _subcuentas;
    public IReadOnlyList<FilaSubcuentaVM> Subcuentas { get; private set; } = [];
    public IReadOnlyList<string> QueFaltan { get; private set; } = [];
    public bool HayQueFaltan => QueFaltan.Count > 0;
    public string TextoQueFaltan => QueFaltan.Count == 1 ? "A este activo le falta una subcuenta de su plantilla en el ejercicio de trabajo:"
        : $"A este activo le faltan {QueFaltan.Count} subcuentas de su plantilla en el ejercicio de trabajo:";
    public string TextoPlantilla => $"Las subcuentas salen de la plantilla del tipo «{_ficha?.Tipo}». El papel dice para qué usa el programa cada una.";

    private async Task CargarSubcuentasAsync()
    {
        _subcuentas = await _modulo.Contexto.Servicio.SubcuentasAsync(_modulo.Contexto.Trabajo, IdActivo);
        Subcuentas = _subcuentas.Subcuentas.Select(s => new FilaSubcuentaVM(s.Codigo ?? "", s.Nombre, s.Papel, s.EnEjercicio ? Formatos.Importe(s.Saldo) : "",
            s.EnEjercicio, s.EnEjercicio && s.Codigo is { } c ? new ComandoDelegado(() => _ = MayorAsync([c], [])) : null)).ToList();
        QueFaltan = _subcuentas.QueFaltan.Select(o => o.Texto).ToList();
        Avisar(nameof(Subcuentas), nameof(QueFaltan), nameof(HayQueFaltan), nameof(TextoQueFaltan), nameof(TextoPlantilla));
    }

    private async Task GenerarAsync()
    {
        if (!await _modulo.Contexto.Servicio.PuedeAsync()) { _modulo.SinPermiso(); return; }
        Ocupado = true;
        bool cambiado;
        try { cambiado = await _modulo.GenerarSubcuentasAsync([IdActivo], $"Subcuentas de {Codigo}"); }
        finally { Ocupado = false; }
        if (!cambiado) return;
        await CargarSubcuentasAsync();
        _cuenta = null; _amortizacion = null;
        _modulo.Cambiado();
    }

    // ================= Amortización =================

    private AmortizacionDeActivo? _amortizacion;
    public IReadOnlyList<FilaAmortizacionVM> FilasAmortizacion { get; private set; } = [];
    public string FrasePlan { get; private set; } = "";

    private async Task CargarAmortizacionAsync()
    {
        _amortizacion = await _modulo.Contexto.Servicio.AmortizacionAsync(_modulo.Contexto.Trabajo, IdActivo);
        var a = _amortizacion;
        if (a.Plan is not { } p)
        {
            FrasePlan = _ficha?.Datos.SeAmortiza == true ? "No tiene plan de amortización: escribe el % anual y la base en Datos y guarda."
                : "Este activo no se amortiza (en Datos, «Se amortiza» está desmarcado).";
            FilasAmortizacion = [];
        }
        else
        {
            var termina = TablaAmortizacionActivo.TerminaEn(a.Filas);
            var construccion = _ficha?.Datos.Inmueble?.PorcentajeConstruccion is decimal pc && _ficha.Datos.EsInmueble && _ficha.Datos.Coste > 0m
                ? $" (el {(pc * 100m).ToString("0.##", Es)} % del coste, que es la construcción)" : "";
            FrasePlan = $"Se amortiza el {(p.PorcentajeAnual * 100m).ToString("0.##", Es)} % al año sobre {Formatos.Importe(p.Base)}{construccion}, desde el {Formatos.Fecha(p.FechaInicio)}" +
                        (p.ValorResidual > 0m ? $", con un valor residual de {Formatos.Importe(p.ValorResidual)}" : "") + ". " +
                        (p.FechaBaja is DateTime b ? $"Dejó de amortizarse al darse de baja el {Formatos.Fecha(b)}." : termina is int t ? $"Terminará de amortizarse en {t}." : "") +
                        (a.SeAmortiza ? "" : " Su plan está desactivado: el proceso no lo amortiza.");
            FilasAmortizacion = a.Filas.Select(f => new FilaAmortizacionVM(f.Ejercicio.ToString(CultureInfo.InvariantCulture), $"{f.Dias} de {f.DiasAno}", Formatos.Importe(f.Cuota),
                Formatos.Importe(f.Acumulado), Formatos.Importe(f.Pendiente), f.Estado switch
                {
                    EstadoAmortizacionEjercicio.Contabilizada => "Contabilizada",
                    EstadoAmortizacionEjercicio.Calculada => "Calculada",
                    EstadoAmortizacionEjercicio.PendienteDeContabilizar => "Pendiente de contabilizar",
                    _ => "Prevista",
                }, f.Estado.ToString().ToUpperInvariant(), f.Estado switch
                {
                    EstadoAmortizacionEjercicio.Contabilizada => "Lo cargado de verdad en su subcuenta de gasto por amortización ese año, y su amortización acumulada al terminarlo.",
                    EstadoAmortizacionEjercicio.Calculada => "Año anterior a los datos que tiene el programa: calculado con el plan.",
                    EstadoAmortizacionEjercicio.PendienteDeContabilizar => "Lo que toca este año según el plan; se contabiliza desde Contabilidad › Amortización de activos.",
                    _ => "Proyección del plan para los años siguientes.",
                })).ToList();
        }
        Avisar(nameof(FilasAmortizacion), nameof(FrasePlan));
    }

    // ---------- lectura de textos ----------
    private static string TextoFecha(DateTime? f) => f is DateTime d ? Formatos.Fecha(d) : "";
    private static string TextoImporte(decimal? v) => v is decimal d ? Formatos.Numero(d) : "";
    private static string? Vacio(string? t) => string.IsNullOrWhiteSpace(t) ? null : t.Trim();

    internal static decimal? Importe(string? t) => string.IsNullOrWhiteSpace(t) ? null : Formatos.LeerImporte(t, out var v) ? v : null;

    /// <summary>Un porcentaje escrito (3 o 3 %) en tanto por uno; null si no se lee o no está entre 0 y 100.</summary>
    internal static decimal? Porcentaje(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        var limpio = t.Replace("%", "").Trim();
        if (!decimal.TryParse(limpio.Replace('.', ','), NumberStyles.Number, Es, out var v) || v < 0m || v > 100m) return null;
        return Math.Round(v / 100m, 6, MidpointRounding.AwayFromZero);
    }

    internal static DateTime? Fecha(string? t)
    {
        if (string.IsNullOrWhiteSpace(t)) return null;
        string[] formatos = ["d/M/yyyy", "d/M/yy", "d-M-yyyy", "d.M.yyyy", "ddMMyyyy"];
        return DateTime.TryParseExact(t.Trim(), formatos, Es, DateTimeStyles.None, out var f) ? f.Date : null;
    }
}
