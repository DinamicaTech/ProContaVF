using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ProConta.UI.Muestra;

/// <summary>Apunte del mayor de muestra (datos inventados, sin datos reales).</summary>
public sealed class ApunteMuestra : INotifyPropertyChanged
{
    public string Cuenta { get; init; } = "";
    public DateOnly Fecha { get; init; }
    public string Asiento { get; init; } = "";
    public string Concepto { get; init; } = "";
    public string Documento { get; init; } = "";
    public decimal Debe { get; init; }
    public decimal Haber { get; init; }
    /// <summary>Saldo acumulado por fecha: no se recalcula al ordenar ni al mover columnas.</summary>
    public decimal Saldo { get; init; }
    private bool _punteado;
    public bool Punteado { get => _punteado; set { _punteado = value; Avisar(); Avisar(nameof(PunteadoTexto)); } }
    public string PunteadoTexto => Punteado ? "✓" : "";
    public string Contrapartida { get; init; } = "";
    public string DescripcionContrapartida { get; init; } = "";
    public string Notas { get; init; } = "";
    public DateOnly? FechaDocumento { get; init; }
    public DateOnly GrabadoEl { get; init; }
    public DateOnly? ModificadoEl { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Avisar([CallerMemberName] string? p = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
}

/// <summary>Cuenta reciente del panel lateral.</summary>
public sealed record CuentaReciente(string Codigo, string Descripcion);

internal static class DatosMuestra
{
    public static readonly CuentaReciente[] Recientes =
    {
        new("43000012", "Talleres Albora (cliente)"),
        new("40000031", "Talleres Albora (proveedor)"),
        new("5720001", "Banco · cuenta principal"),
        new("4720001", "Hacienda Pública, IVA soportado"),
        new("6290001", "Otros servicios")
    };

    /// <summary>Genera n apuntes de una cuenta de cliente con su saldo acumulado por fecha.</summary>
    public static List<ApunteMuestra> Apuntes(int n, string cuenta = "43000012")
    {
        var r = new Random(43);
        var lista = new List<ApunteMuestra>(n);
        var fecha = new DateOnly(2026, 1, 1);
        decimal saldo = 0;
        string[] notas = { "", "", "", "Revisado con el cliente por teléfono", "", "Pendiente de confirmar el cobro", "" };
        for (var i = 0; i < n; i++)
        {
            var esFactura = i % 2 == 0;
            var imp = Math.Round((decimal)(r.NextDouble() * 2400 + 60), 2);
            var doc = $"F-2026-{100 + i / 2:0000}";
            var debe = i == 0 ? 1210m : esFactura ? imp : 0;
            var haber = i == 0 ? 0 : esFactura ? 0 : lista[^1].Debe;
            saldo += debe - haber;
            var grab = fecha.AddDays(i % 3);
            lista.Add(new ApunteMuestra
            {
                Cuenta = cuenta,
                Fecha = fecha,
                Asiento = i == 0 ? "A-2026-00001" : $"A-2026-{30 + i * 7:00000}",
                Concepto = i == 0 ? "Asiento de apertura" : esFactura ? $"Factura emitida {doc} · Talleres Albora, S.L." : $"Cobro factura {lista[^1].Documento}",
                Documento = i == 0 ? "" : esFactura ? doc : lista[^1].Documento,
                Debe = debe,
                Haber = haber,
                Saldo = saldo,
                Punteado = r.NextDouble() < 0.4,
                Contrapartida = i == 0 ? "—" : esFactura ? "7050001" : "5720001",
                DescripcionContrapartida = i == 0 ? "Asiento de apertura" : esFactura ? "Prestaciones de servicios" : "Banco · cuenta principal",
                Notas = notas[i % notas.Length],
                FechaDocumento = esFactura && i > 0 ? fecha : null,
                GrabadoEl = i % 97 == 5 ? fecha.AddMonths(1) : grab,
                ModificadoEl = i % 13 == 4 ? grab.AddDays(20) : null
            });
            if (!esFactura || i == 0) fecha = fecha.AddDays(r.Next(1, 4));
        }
        return lista;
    }
}
