using ProConta.Contabilidad.Mayor;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Mayor;

/// <summary>Una cuenta en la lista del selector, con sus importes ya formateados para la rejilla.</summary>
public sealed class CuentaSelectorVM(CuentaMayor c)
{
    public CuentaMayor Cuenta { get; } = c;
    public string Codigo => Cuenta.Codigo;
    public string Nombre => Cuenta.Nombre;
    public decimal SaldoSinApertura => Cuenta.SaldoSinApertura;
    public decimal Saldo => Cuenta.Saldo;
    public int Apuntes => Cuenta.Apuntes;
    public string Tipo => Cuenta.EsSubcuenta ? "" : "Grupo";
}

/// <summary>
/// Selector de cuentas del Mayor: búsqueda por código o nombre y los filtros del antiguo (solo subcuentas, con movimientos,
/// con saldo, con saldo sin la apertura), calculados sobre los ejercicios elegidos. Un código corto se puede elegir tal cual
/// para ver todas sus subcuentas juntas.
/// </summary>
public sealed class SelectorCuentasVM : ModeloVista
{
    private readonly IReadOnlyList<CuentaMayor> _catalogo;
    private string _texto = "";
    private bool _soloSubcuentas, _conMovimientos, _conSaldo, _conSaldoSinApertura;
    private IReadOnlyList<CuentaSelectorVM> _resultados = [];

    public SelectorCuentasVM(IReadOnlyList<CuentaMayor> catalogo, FiltroCuentasMayor filtro, string ejercicios)
    {
        _catalogo = catalogo;
        _soloSubcuentas = filtro.SoloSubcuentas;
        _conMovimientos = filtro.ConMovimientos;
        _conSaldo = filtro.ConSaldo;
        _conSaldoSinApertura = filtro.ConSaldoSinApertura;
        Ejercicios = ejercicios;
        Filtrar();
    }

    /// <summary>Ejercicios sobre los que se calculan saldos y apuntes («Ejercicios 2025, 2026»).</summary>
    public string Ejercicios { get; }

    public string Texto { get => _texto; set { if (Fijar(ref _texto, value ?? "")) Filtrar(); } }
    public bool SoloSubcuentas { get => _soloSubcuentas; set { if (Fijar(ref _soloSubcuentas, value)) Filtrar(); } }
    public bool ConMovimientos { get => _conMovimientos; set { if (Fijar(ref _conMovimientos, value)) Filtrar(); } }
    public bool ConSaldo { get => _conSaldo; set { if (Fijar(ref _conSaldo, value)) Filtrar(); } }
    public bool ConSaldoSinApertura { get => _conSaldoSinApertura; set { if (Fijar(ref _conSaldoSinApertura, value)) Filtrar(); } }

    public FiltroCuentasMayor Filtro => new(_soloSubcuentas, _conMovimientos, _conSaldo, _conSaldoSinApertura);

    public IReadOnlyList<CuentaSelectorVM> Resultados { get => _resultados; private set => Fijar(ref _resultados, value); }

    public string Estado => Formatos.Plural(Resultados.Count, "cuenta", "cuentas") + " · saldos y apuntes de " + Ejercicios.ToLowerInvariant() +
                            (Resultados.Count == 0 && _catalogo.Count > 0 ? ". Ninguna cumple los filtros: quita alguno o cambia el texto." : "");

    /// <summary>El texto es un código corto (solo cifras) que no es una subcuenta: se puede ver todo lo que empieza por él.</summary>
    public string? CodigoCorto
    {
        get
        {
            var t = _texto.Trim();
            if (t.Length == 0 || !t.All(char.IsDigit) || SelectorCuentasMayor.EsExacta(t, _catalogo)) return null;
            return _catalogo.Any(c => c.EsSubcuenta && c.Codigo.StartsWith(t, StringComparison.Ordinal)) ? t : null;
        }
    }

    public string TextoCodigoCorto => CodigoCorto is { } c
        ? $"Ver todas las que empiezan por {c} ({_catalogo.Count(x => x.EsSubcuenta && x.Codigo.StartsWith(c, StringComparison.Ordinal))})"
        : "";

    private void Filtrar()
    {
        Resultados = SelectorCuentasMayor.Filtrar(_catalogo, Filtro with { Texto = _texto }).Select(c => new CuentaSelectorVM(c)).ToList();
        Avisar(nameof(Estado), nameof(CodigoCorto), nameof(TextoCodigoCorto));
    }
}
