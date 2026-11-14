using System.Windows.Input;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Contabilidad.Diario;
using ProConta.UI.Base.Comandos;
using ProConta.UI.Base.Formato;
using ProConta.UI.Base.Modelos;

namespace ProConta.Contabilidad.UI.Diario;

/// <summary>Qué es una fila de la rejilla del Diario.</summary>
public enum TipoFilaDiario
{
    /// <summary>Un apunte.</summary>
    Apunte,
    /// <summary>La línea de cabecera de un asiento (vista «Por asiento»), con su total y el botón de plegar.</summary>
    Asiento,
    /// <summary>La línea de totales de un mes (vista «Por asiento» con «Totales por mes»).</summary>
    TotalMes,
}

/// <summary>
/// Fila de la rejilla del Diario (contabilidad/diario): un apunte, la cabecera de un asiento o el total de un mes. Las columnas son las de
/// <see cref="ColumnasDiario"/>; cada una tiene su texto y, para ordenar la lista, su valor.
/// </summary>
public sealed class FilaDiarioVM : ModeloVista
{
    private readonly ApunteDiario? _apunte;
    private readonly TotalMesDiario? _mes;
    private readonly string _textoCabecera = "";
    private bool _plegado;

    /// <summary>Fila de apunte. <paramref name="agrupada"/>: va bajo la cabecera de su asiento (no repite número, fecha ni origen).</summary>
    public FilaDiarioVM(ApunteDiario apunte, bool agrupada, bool coincide, bool apagada)
    {
        Tipo = TipoFilaDiario.Apunte;
        _apunte = apunte;
        Asiento = apunte.Asiento;
        Agrupada = agrupada;
        Coincide = coincide;
        Apagada = apagada;
    }

    /// <summary>Cabecera de un asiento, con las notas que la acompañan (trimestre cerrado, modificado a mano, cuántos cumplen el filtro).</summary>
    public FilaDiarioVM(AsientoDiario asiento, bool plegado, string notas)
    {
        Tipo = TipoFilaDiario.Asiento;
        Asiento = asiento;
        _plegado = plegado;
        _textoCabecera = asiento.Descripcion + notas;
    }

    public FilaDiarioVM(TotalMesDiario mes)
    {
        Tipo = TipoFilaDiario.TotalMes;
        _mes = mes;
        _textoCabecera = $"Total de {ExportacionDiario.NombreMes(mes.Mes)} de {mes.Ejercicio} · {Formatos.Plural(mes.Asientos, "asiento", "asientos")}";
    }

    public TipoFilaDiario Tipo { get; }
    public AsientoDiario? Asiento { get; }
    public ApunteDiario? Apunte => _apunte;
    public bool EsApunte => Tipo == TipoFilaDiario.Apunte;
    public bool EsCabecera => Tipo == TipoFilaDiario.Asiento;
    public bool EsTotalMes => Tipo == TipoFilaDiario.TotalMes;
    public bool Agrupada { get; }
    /// <summary>El apunte cumple el filtro de apuntes (se resalta en la vista por asiento).</summary>
    public bool Coincide { get; }
    /// <summary>El apunte no cumple el filtro, pero se ve porque su asiento sí (apagado).</summary>
    public bool Apagada { get; }
    public bool Plegado { get => _plegado; set { if (Fijar(ref _plegado, value)) Avisar(nameof(AyudaPlegar), nameof(SignoPlegar)); } }
    public string SignoPlegar => _plegado ? "▸" : "▾";
    public string AyudaPlegar => (_plegado ? "Despliega" : "Pliega") + " los apuntes de este asiento";

    /// <summary>Clave de la fila para conservar la selección al reconstruir la rejilla.</summary>
    public string Clave => Tipo switch { TipoFilaDiario.Apunte => "p" + _apunte!.IdApunte, TipoFilaDiario.Asiento => "a" + Asiento!.IdAsiento, _ => "m" + _mes!.Ejercicio + "-" + _mes.Mes };

    private string Celda(string clave) => _apunte is { } a ? ColumnasDiario.De(clave).Texto(a) : "";

    public string NumeroAsiento => Tipo == TipoFilaDiario.TotalMes || (EsApunte && Agrupada) ? "" : Asiento!.IdAsiento.ToString(Formatos.Cultura);
    public int AsientoOrden => Asiento?.IdAsiento ?? 0;
    public string IdApunte => Celda(ColumnasDiario.IdApunte);
    public int IdApunteOrden => _apunte?.IdApunte ?? 0;
    public string Fecha => Tipo == TipoFilaDiario.TotalMes || (EsApunte && Agrupada) ? "" : Formatos.Fecha(Asiento!.Fecha);
    public DateTime FechaOrden => Asiento?.Fecha ?? default;
    public string Origen => Tipo == TipoFilaDiario.TotalMes || (EsApunte && Agrupada) ? "" : Asiento!.Origen;
    public string OrigenOrden => Asiento?.Origen ?? "";
    public string AyudaOrigen => Asiento is not { } a ? "" : !a.EsAutomatico ? "Asiento manual"
        : $"Asiento automático de {a.Origen} ({a.Referencia}): las cuentas y los importes se cambian con «Modificar el asiento…», salvo los apuntes de la 572"
          + (a.ConLibroIva ? " y, por ser de una factura, las líneas de IVA y los importes" : "");
    public string Cuenta => _apunte?.Cuenta ?? "";
    public string NombreCuenta => _apunte?.NombreCuenta ?? "";
    public string Concepto => _apunte?.Concepto ?? _textoCabecera;
    public string AyudaConcepto => _apunte is { } a ? a.Concepto : Asiento is { Notas: { } n } ? $"{_textoCabecera}\nNotas: {n}" : _textoCabecera;
    public string Documento => _apunte?.Documento ?? "";
    public decimal DebeOrden => _apunte?.Debe ?? Asiento?.TotalDebe ?? _mes?.Debe ?? 0m;
    public decimal HaberOrden => _apunte?.Haber ?? Asiento?.TotalHaber ?? _mes?.Haber ?? 0m;
    public string Debe => _apunte is { } a ? (a.Debe == 0m ? "" : Formatos.Numero(a.Debe)) : Formatos.Numero(DebeOrden);
    public string Haber => _apunte is { } a ? (a.Haber == 0m ? "" : Formatos.Numero(a.Haber)) : Formatos.Numero(HaberOrden);
    public string Centro => _apunte?.CentroCoste ?? "";
    public string Punteado => _apunte is { Punteado: true } ? "✓" : "";
    public bool PunteadoOrden => _apunte?.Punteado ?? false;
    public string Bloqueos => _apunte is { } a ? string.Concat(a.Bloqueos.Select(b => b.Icono)) : "";
    public string AyudaBloqueos => _apunte is { } a && a.Bloqueos.Count > 0
        ? string.Join("\n", a.Bloqueos.Select(b => $"{b.Icono} {b.Ayuda}"))
        : "Sin bloqueos: la cuenta y el importe se cambian con «Modificar el asiento…»";
    public string Orden => Celda(ColumnasDiario.Orden);
    public int OrdenOrden => _apunte?.Orden ?? 0;
    public string Contrapartida => Celda(ColumnasDiario.Contrapartida);
    public string AyudaContrapartida => _apunte is { CuentasContrapartida.Count: > 1 } a ? "Varias: " + string.Join(", ", a.CuentasContrapartida) : "";
    public string Vencimiento => Celda(ColumnasDiario.Vencimiento);
    public DateTime VencimientoOrden => _apunte?.Vencimiento ?? default;
    public string FechaDocumento => Celda(ColumnasDiario.FechaDocumento);
    public DateTime FechaDocumentoOrden => _apunte?.Asiento.FechaDocumento ?? default;
    public string Grabado => Celda(ColumnasDiario.Grabado);
    public DateTime GrabadoOrden => _apunte?.Asiento.GrabadoEl ?? default;
    public string Modificado => Celda(ColumnasDiario.Modificado);
    public DateTime ModificadoOrden => _apunte?.Asiento.ModificadoEl ?? default;
    public string Operador => Celda(ColumnasDiario.Operador);

    /// <summary>El punteo del apunte ha cambiado sin volver a leer la BD (F4).</summary>
    internal void AvisarPunteo() => Avisar(nameof(Punteado), nameof(PunteadoOrden));
}

/// <summary>Una acción del menú contextual: su texto, si se puede y, si no, por qué (en el tooltip).</summary>
public sealed class AccionDiarioVM : ModeloVista
{
    private readonly string _descripcion;
    private string _texto;
    private string? _motivo;

    public AccionDiarioVM(string texto, string descripcion, Action ejecutar)
    {
        _texto = texto;
        _descripcion = descripcion;
        Comando = new ComandoDelegado(ejecutar, () => _motivo is null);
    }

    public string Texto { get => _texto; private set => Fijar(ref _texto, value); }
    public bool Habilitada => _motivo is null;
    /// <summary>Lo que hace la acción o, si está desactivada, por qué.</summary>
    public string Ayuda => _motivo is null ? _descripcion : $"{_motivo} ({_descripcion})";
    public string? Motivo => _motivo;
    public ICommand Comando { get; }

    /// <summary>Pone el texto de la acción y por qué no se puede (null: se puede).</summary>
    public void Poner(string texto, string? motivo)
    {
        Texto = texto;
        if (_motivo == motivo) return;
        _motivo = motivo;
        Avisar(nameof(Habilitada), nameof(Ayuda), nameof(Motivo));
    }
}

/// <summary>Etiqueta de un filtro puesto, con su comando de quitar.</summary>
public sealed record FichaFiltroDiario(string Texto, string Ayuda, ICommand Quitar);

/// <summary>Opción de un desplegable de filtro o de la ficha (valor y texto).</summary>
public sealed record OpcionDiario<T>(T Valor, string Texto, string Ayuda = "")
{
    public override string ToString() => Texto;
}

/// <summary>Un dato de la ficha del asiento (etiqueta y valor).</summary>
public sealed record DatoFichaDiario(string Etiqueta, string Valor);

/// <summary>Una línea de la tabla de apuntes de la ficha; clic: selecciona ese apunte en la rejilla.</summary>
public sealed record LineaFichaDiario(ApunteDiario Apunte, bool Seleccionada)
{
    public string Cuenta => Apunte.Cuenta;
    public string Nombre => Apunte.NombreCuenta;
    public string Debe => Apunte.Debe == 0m ? "" : Formatos.Numero(Apunte.Debe);
    public string Haber => Apunte.Haber == 0m ? "" : Formatos.Numero(Apunte.Haber);
    public string Ayuda => $"Apunte {Apunte.Orden} · {Apunte.Concepto}" + (Apunte.Documento is { } d ? $" · {d}" : "") + "\nClic: seleccionarlo en la rejilla";
}

/// <summary>Suma de los apuntes seleccionados de una cuenta (ficha con varios asientos).</summary>
public sealed record SumaCuentaDiario(string Cuenta, string Nombre, decimal ValorDebe, decimal ValorHaber, int Apuntes)
{
    public string Debe => Formatos.Numero(ValorDebe);
    public string Haber => Formatos.Numero(ValorHaber);
    public string Saldo => Formatos.Numero(ValorDebe - ValorHaber);
}

/// <summary>Una entrada del historial de cambios del asiento: una operación (un lote) con su fecha, operador y motivo.</summary>
public sealed record CambioFichaDiario(DateTime FechaHora, string Operador, string Texto, string Detalle)
{
    public string Cuando => FechaHora.ToString("dd/MM/yyyy HH:mm", Formatos.Cultura);
}

/// <summary>Formato de la exportación del Diario.</summary>
public enum FormatoExportacionDiario { Pdf, Excel }

/// <summary>Centro de coste elegible en la ficha o en «Asignar centro de coste…» (null: sin centro).</summary>
public sealed record CentroDiario(CentroCoste? Centro)
{
    public int? Id => Centro?.IdCentroCoste;
    public string Texto => Centro is null ? "Sin centro" : $"{Centro.Codigo} · {Centro.Nombre}";
    public override string ToString() => Texto;
}
