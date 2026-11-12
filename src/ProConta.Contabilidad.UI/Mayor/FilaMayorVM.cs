using System.Text;
using ProConta.Contabilidad.Mayor;
using ProConta.UI.Base.Formato;

namespace ProConta.Contabilidad.UI.Mayor;

/// <summary>Una fila de la tabla del Mayor tal como la pinta la rejilla: valores para ordenar y exportar, textos y ayudas.</summary>
public sealed class FilaMayorVM(FilaMayor f, string cabeceraEjercicio)
{
    public FilaMayor Fila { get; } = f;

    /// <summary>Separador de ejercicio (agrupación de la tabla cuando está en orden de fecha).</summary>
    public string CabeceraEjercicio { get; } = cabeceraEjercicio;

    public string Cuenta => Fila.Cuenta;
    public string NombreCuenta => Fila.NombreCuenta;
    public string AyudaCuenta => Fila.NombreCuenta.Length > 0 ? $"{Fila.Cuenta} {Fila.NombreCuenta}" : Fila.Cuenta;
    public DateTime Fecha => Fila.Fecha;
    public int Asiento => Fila.IdAsiento;
    public string Concepto => Fila.Concepto;
    public string? Documento => Fila.Documento;
    public decimal Debe => Fila.Debe;
    public decimal Haber => Fila.Haber;
    public decimal Saldo => Fila.Saldo;
    public string Contrapartida => Fila.Contrapartida;
    public string NombreContrapartida => Fila.NombreContrapartida;
    public string? Notas => Fila.Notas;
    public DateTime? FechaDocumento => Fila.FechaDocumento;
    public DateTime? GrabadoEl => Fila.GrabadoEl;
    public DateTime? ModificadoEl => Fila.ModificadoEl;
    public int Ejercicio => Fila.Ejercicio;
    public string GrupoSaldo => Fila.GrupoSaldo;
    public bool EsApertura => Fila.EsApertura;
    public bool AperturaContinua => Fila.AperturaContinua;
    public bool HayAvisoApertura => Fila.AvisoApertura is not null;
    public bool Punteado => Fila.Punteado;
    public string TextoPunteo => Fila.Punteado ? "✓" : Fila.PunteoParcial ? "½" : "";
    public string AyudaPunteo => Fila.Punteado ? (Fila.NumeroApuntes > 1 ? "Todos sus apuntes están punteados" : "Punteado") + " · F4: quitar el punteo"
        : Fila.PunteoParcial ? $"Solo algunos de sus {Fila.NumeroApuntes} apuntes están punteados · F4: puntearlos todos" : "Sin puntear · F4: puntear";

    public string AyudaFecha => Fila.NumeroApuntes > 1
        ? $"Fecha contable del primer apunte de los {Fila.NumeroApuntes} que resume esta fila"
        : "Fecha contable del asiento (decide el periodo y el trimestre de IVA)";

    public string AyudaAsiento => Fila.IdsAsiento.Count > 1
        ? $"Asientos {string.Join(", ", Fila.IdsAsiento.Take(12))}{(Fila.IdsAsiento.Count > 12 ? "…" : "")} · Intro o doble clic: ver el primero"
        : $"Asiento {Fila.IdAsiento}{(Fila.EjercicioCerrado ? " · ejercicio cerrado, solo consulta" : "")} · Intro o doble clic: verlo";

    public string AyudaSaldo
    {
        get
        {
            if (Fila.AvisoApertura is { } aviso) return aviso;
            if (Fila.AperturaContinua)
                return $"Apertura de {Fila.Ejercicio}: se ve, pero no suma; el saldo continúa el de {Fila.Ejercicio - 1}. {ReglaSaldo.Descripcion(Fila.Saldo)}";
            return ReglaSaldo.Descripcion(Fila.Saldo) + " acumulado por fecha: al ordenar por otra columna no se recalcula";
        }
    }

    public string AyudaContrapartida
    {
        get
        {
            if (Fila.EsApertura) return "El asiento de apertura no tiene contrapartida: recoge los saldos de cierre del ejercicio anterior";
            var sb = new StringBuilder();
            sb.Append(Fila.CuentasContrapartida.Count switch
            {
                0 => "Sin contrapartida",
                1 => $"Doble clic: abrir el mayor de la {Fila.CuentasContrapartida[0]}",
                _ => "Varias contrapartidas (sin contar el IVA) · clic derecho: ir a una de ellas",
            });
            foreach (var d in Fila.DetalleContrapartida.Take(15))
            {
                sb.Append('\n').Append(d.Cuenta);
                if (NombreDe?.Invoke(d.Cuenta) is { Length: > 0 } n) sb.Append(' ').Append(n);
                sb.Append(" · ").Append(Formatos.Importe(d.Importe));
            }
            if (Fila.DetalleContrapartida.Count > 15) sb.Append("\n… y ").Append(Fila.DetalleContrapartida.Count - 15).Append(" más");
            return sb.ToString();
        }
    }

    /// <summary>Nombre de las cuentas para la ayuda de la contrapartida (lo pone el modelo de vista).</summary>
    internal Func<string, string>? NombreDe { get; init; }
}
