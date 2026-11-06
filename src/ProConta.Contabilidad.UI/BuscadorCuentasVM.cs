using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Cuentas;
using ProConta.Nucleo.Contexto;
using ProConta.Nucleo.Resultados;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI;

/// <summary>
/// Buscador de cuentas del ejercicio de trabajo (contabilidad/cuenta-contable 2026-11-06), común a todas las pantallas.
/// Cada pantalla lo limita a sus grupos (la cuenta de gasto, al 6) y el operador puede quitar el filtro con una casilla.
/// «Crear subcuenta» elige la cuenta de tres cifras en un desplegable (código, nombre y descripción; por defecto, la de
/// la seleccionada) y después pide el código, que propone el siguiente libre, el nombre, la descripción y el centro de
/// coste por defecto.
/// </summary>
public sealed class BuscadorCuentasVM : ModeloVista
{
    private readonly ICatalogoCuentas _cuentas;
    private readonly ICatalogoCentrosCoste? _centrosCoste;
    private readonly ContextoSociedadEjercicio _trabajo;
    private List<CuentaContable> _todas = [];
    private string _texto;
    private bool _soloGrupos;
    private IReadOnlyList<CuentaContable> _resultados = [];
    private CuentaContable? _seleccionada;
    private string _estado = "Leyendo el plan de cuentas…";

    public BuscadorCuentasVM(ICatalogoCuentas cuentas, ContextoSociedadEjercicio trabajo, IReadOnlyList<string> grupos, string? texto = null,
        ICatalogoCentrosCoste? centrosCoste = null)
    {
        _cuentas = cuentas;
        _centrosCoste = centrosCoste;
        _trabajo = trabajo;
        Grupos = grupos;
        _soloGrupos = grupos.Count > 0;
        _texto = (texto ?? "").Trim();
    }

    public IReadOnlyList<string> Grupos { get; }
    public bool HayGrupos => Grupos.Count > 0;
    public string TextoGrupos => Grupos.Count == 1 ? $"Solo el grupo {Grupos[0]}" : $"Solo los grupos {string.Join(", ", Grupos)}";
    public int Ejercicio => _trabajo.Ejercicio;

    public string Texto { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public bool SoloGrupos { get => _soloGrupos; set { if (Fijar(ref _soloGrupos, value)) Filtrar(); } }
    public IReadOnlyList<CuentaContable> Resultados { get => _resultados; private set => Fijar(ref _resultados, value); }
    public CuentaContable? Seleccionada { get => _seleccionada; set { if (Fijar(ref _seleccionada, value)) Avisar(nameof(CuentaPropuesta)); } }
    public string Estado { get => _estado; private set => Fijar(ref _estado, value); }

    /// <summary>Centros de coste imputables de la sociedad, para el centro de coste por defecto de la subcuenta.</summary>
    public IReadOnlyList<CentroCoste> CentrosCoste { get; private set; } = [];

    /// <summary>Lee las cuentas del ejercicio (y los centros de coste) y aplica el texto inicial.</summary>
    public async Task CargarAsync()
    {
        _todas = (await _cuentas.ListarAsync(_trabajo)).ToList();
        if (_centrosCoste is not null)
            CentrosCoste = (await _centrosCoste.ListarAsync(_trabajo.IdSociedad)).Where(c => c.EstaActivo && c.EsImputable)
                .OrderBy(c => c.RutaJerarquica, StringComparer.Ordinal).ToList();
        Filtrar();
    }

    private void Filtrar()
    {
        Resultados = BuscadorCuentas.Filtrar(_todas, _texto, _soloGrupos ? Grupos : null);
        Seleccionada = Resultados.FirstOrDefault(c => c.Codigo == _texto.Trim()) ?? Resultados.FirstOrDefault();
        Estado = Resultados.Count == 0
            ? _soloGrupos && HayGrupos ? $"Ninguna cuenta del {(Grupos.Count == 1 ? "grupo " + Grupos[0] : "filtro")} coincide. Quita «{TextoGrupos}» o crea la subcuenta." : "Ninguna cuenta coincide. Puedes crear la subcuenta."
            : Resultados.Count == 1 ? "1 cuenta" : $"{Resultados.Count} cuentas";
    }

    /// <summary>
    /// Cuentas de tres cifras del ejercicio en las que se puede crear una subcuenta: activas y, con el filtro puesto, de
    /// los grupos de la pantalla.
    /// </summary>
    public IReadOnlyList<CuentaContable> CuentasPadre =>
        _todas.Where(c => c.Codigo.Length == 3 && c.Codigo.All(char.IsAsciiDigit) && c.EstaActiva && (!_soloGrupos || EnGrupos(c.Codigo)))
            .OrderBy(c => c.Codigo, StringComparer.Ordinal).ToList();

    /// <summary>
    /// Cuenta de tres cifras que se propone para crear una subcuenta: la de la seleccionada si es de los grupos de la
    /// pantalla (con el filtro quitado, una 572 no se propone para una cuenta de gasto), la del texto escrito si empieza
    /// por tres cifras, o vacía.
    /// </summary>
    public string CuentaPropuesta =>
        _seleccionada is { Codigo.Length: >= 3 } s && EnGrupos(s.Codigo) ? s.Codigo[..3]
        : _texto.Length >= 3 && _texto[..3].All(char.IsAsciiDigit) ? _texto[..3]
        : Grupos.FirstOrDefault(g => g.Length == 3) ?? "";

    /// <summary>Sin grupos, cualquiera; con grupos, la que empieza por uno de ellos (aunque se haya quitado el filtro).</summary>
    private bool EnGrupos(string codigo) => !HayGrupos || Grupos.Any(g => codigo.StartsWith(g, StringComparison.Ordinal));

    /// <summary>Comprueba la cuenta de tres cifras y devuelve el siguiente código libre de subcuenta, o el error.</summary>
    public async Task<Resultado<string>> SiguienteCodigoAsync(string cuenta)
    {
        var c = (cuenta ?? "").Trim();
        if (c.Length != 3 || !c.All(char.IsAsciiDigit))
            return Resultado<string>.Fallo(new Error("Cuentas.CuentaNoValida", "Elige la cuenta de tres cifras en la que va la subcuenta.", null, null, "Por ejemplo, 629."));
        if (_soloGrupos && HayGrupos && !Grupos.Any(g => c.StartsWith(g, StringComparison.Ordinal) || g.StartsWith(c, StringComparison.Ordinal)))
            return Resultado<string>.Fallo(new Error("Cuentas.FueraDeGrupo", $"La cuenta {c} no es del {TextoGrupos.ToLowerInvariant().Replace("solo ", "")}.", null, null,
                $"Elige una cuenta del grupo o quita «{TextoGrupos}» en el buscador."));
        var siguiente = await _cuentas.SiguienteSubcuentaAsync(_trabajo, c);
        return siguiente is null
            ? Resultado<string>.Fallo(new Error("Cuentas.SinCodigos", $"No quedan códigos libres en la cuenta {c}.", null, null, "Escribe el código a mano o usa otra cuenta."))
            : Resultado<string>.Correcto(siguiente);
    }

    /// <summary>
    /// Da de alta la subcuenta en el ejercicio de trabajo (bajo la cuenta de tres cifras si existe), con su descripción
    /// y su centro de coste por defecto, y la elige.
    /// </summary>
    public async Task<Resultado<CuentaContable>> CrearAsync(string cuenta, string codigo, string nombre, string? descripcion = null, int? idCentroCoste = null)
    {
        var c = (cuenta ?? "").Trim();
        var cod = (codigo ?? "").Trim();
        if (!cod.StartsWith(c, StringComparison.Ordinal) || cod.Length <= c.Length)
            return Resultado<CuentaContable>.Fallo(new Error("Cuentas.CodigoFueraDeCuenta", $"El código de la subcuenta tiene que empezar por {c} y ser más largo.", null, null,
                "Corrige el código o vuelve al paso anterior para elegir otra cuenta."));
        var padre = await _cuentas.ResolverAsync(_trabajo, c);
        var alta = await _cuentas.AltaAsync(_trabajo, new AltaCuenta(cod, nombre, padre.EsCorrecto ? padre.Valor.IdCuentaContable : null,
            IdCentroCostePredeterminado: idCentroCoste, Descripcion: descripcion));
        if (!alta.EsCorrecto) return alta;
        _todas.Add(alta.Valor);
        _texto = cod;
        Avisar(nameof(Texto));
        Filtrar();
        Seleccionada = alta.Valor;
        return alta;
    }
}
