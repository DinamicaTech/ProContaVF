using System.Globalization;

namespace ProConta.Nucleo.Importes;

/// <summary>
/// Importe monetario: Decimal redondeado a dos decimales con AwayFromZero (RF-FAC-02).
/// Toda operación devuelve un importe ya redondeado; para calcular sin redondeo intermedio
/// se trabaja con decimal y se convierte al final con <see cref="Redondear(decimal)"/>.
/// </summary>
public readonly struct Importe : IEquatable<Importe>, IComparable<Importe>
{
    public const int Decimales = 2;

    public static readonly Importe Cero = new(0m);

    private Importe(decimal valorRedondeado) => Valor = valorRedondeado;

    /// <summary>Valor ya redondeado a dos decimales.</summary>
    public decimal Valor { get; }

    /// <summary>Redondeo monetario del proyecto: dos decimales, AwayFromZero.</summary>
    public static decimal RedondearValor(decimal valor) =>
        Math.Round(valor, Decimales, MidpointRounding.AwayFromZero);

    public static Importe Redondear(decimal valor) => new(RedondearValor(valor));

    /// <summary>Suma importes ya redondeados (redondear por línea y después sumar).</summary>
    public static Importe Sumar(IEnumerable<Importe> importes) =>
        new(importes.Aggregate(0m, (total, i) => total + i.Valor));

    /// <summary>Aplica un porcentaje (21 = 21 %) y redondea el resultado.</summary>
    public Importe Porcentaje(decimal porcentaje) => Redondear(Valor * porcentaje / 100m);

    public bool EsCero => Valor == 0m;
    public bool EsNegativo => Valor < 0m;
    public Importe Absoluto() => new(Math.Abs(Valor));

    public static Importe operator +(Importe a, Importe b) => new(a.Valor + b.Valor);
    public static Importe operator -(Importe a, Importe b) => new(a.Valor - b.Valor);
    public static Importe operator -(Importe a) => new(-a.Valor);
    public static Importe operator *(Importe a, decimal factor) => Redondear(a.Valor * factor);
    public static Importe operator *(decimal factor, Importe a) => Redondear(a.Valor * factor);
    public static bool operator ==(Importe a, Importe b) => a.Valor == b.Valor;
    public static bool operator !=(Importe a, Importe b) => a.Valor != b.Valor;
    public static bool operator <(Importe a, Importe b) => a.Valor < b.Valor;
    public static bool operator >(Importe a, Importe b) => a.Valor > b.Valor;
    public static bool operator <=(Importe a, Importe b) => a.Valor <= b.Valor;
    public static bool operator >=(Importe a, Importe b) => a.Valor >= b.Valor;

    public static implicit operator decimal(Importe importe) => importe.Valor;

    public bool Equals(Importe other) => Valor == other.Valor;
    public override bool Equals(object? obj) => obj is Importe otro && Equals(otro);
    public override int GetHashCode() => Valor.GetHashCode();
    public int CompareTo(Importe other) => Valor.CompareTo(other.Valor);

    /// <summary>Texto invariante con dos decimales ("1234.50"), para registros y pruebas.</summary>
    public override string ToString() => Valor.ToString("0.00", CultureInfo.InvariantCulture);
}
