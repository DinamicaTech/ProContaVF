namespace ProConta.Contabilidad.Impuestos;

/// <summary>Apunte de una 473 que forma su saldo, con el pagador de su asiento (la primera 43 o 44) y su NIF si el tercero lo tiene.</summary>
/// <param name="Importe">Debe − Haber.</param>
public sealed record ApunteRetencion(int IdAsiento, DateTime Fecha, string Cuenta, string Texto, decimal Importe, string? Pagador = null, string? Nif = null);

/// <summary>
/// Lo que escribe el operador en una fila del contraste de retenciones: el valor para el IS (null: el propuesto), su justificación (obligatoria si
/// no es el de Hacienda) y, en las filas que solo conoce Hacienda, la 473 a la que van.
/// </summary>
public sealed record ValorRetencion(string Clave, decimal? Importe, string? Justificacion = null, string? Cuenta = null);

/// <summary>
/// Fila del contraste de retenciones (contabilidad/procesos/impuesto-sociedades): un pagador (o una 473 sin pagador) con lo que dice Hacienda
/// (null sin datos fiscales), lo contabilizado y sus apuntes, lo propuesto y el valor que se usa en el IS, que va al Haber de su 473.
/// </summary>
public sealed record ContrasteRetencion(string Clave, string Pagador, string? Nif, string? Cuenta, string NombreCuenta, decimal? Hacienda, decimal Contable,
    IReadOnlyList<ApunteRetencion> Apuntes, IReadOnlyList<RetencionAeat> Aeat, decimal Propuesto, decimal Valor, string? Justificacion)
{
    public bool Escrito => Valor != Propuesto;
    public decimal Diferencia => (Hacienda ?? 0m) - Contable;
    public bool SoloHacienda => Apuntes.Count == 0 && Aeat.Count > 0;
    /// <summary>Con datos fiscales, un valor distinto del de Hacienda necesita justificación.</summary>
    public bool FaltaJustificacion => Hacienda is not null && Valor != Hacienda && string.IsNullOrWhiteSpace(Justificacion);
}

/// <summary>
/// Contraste de las retenciones y pagos a cuenta (contabilidad/procesos/impuesto-sociedades 2026-11-17): por cada pagador, lo que dice Hacienda en
/// los datos fiscales frente a lo contabilizado en las 473, con el detalle de sus apuntes, y el valor que se usa en el IS.
/// <list type="bullet">
/// <item>Contabilidad: los apuntes de cada 473 agrupados por el pagador de su asiento (por su NIF si lo tiene); sin pagador, una fila por cuenta.</item>
/// <item>Hacienda: las líneas de los datos fiscales agrupadas por modelo y pagador. Cada grupo va a la fila de su NIF (si el pagador aparece en
/// varias 473, a la de más importe); si no, a la única cuyo pagador comparte más palabras; si no, a la fila sin pagador de la 473 que le asignó el
/// lector; y si no hay, a una fila propia que solo conoce Hacienda.</item>
/// <item>Propuesto: con datos fiscales, lo de Hacienda (0 si no lo conoce); sin ellos, lo contabilizado. Lo escrito manda.</item>
/// </list>
/// </summary>
public static class ContrasteRetenciones
{
    public static IReadOnlyList<ContrasteRetencion> Filas(DatosImpuestoSociedades d, EntradaImpuestoSociedades e)
    {
        var nombres = d.Retenciones.ToDictionary(r => r.Cuenta, r => r.Nombre, StringComparer.Ordinal);
        string NombreCuenta(string? c) => c is null ? "" : nombres.GetValueOrDefault(c, "");
        var apuntes = d.ApuntesRetencion
            ?? d.Retenciones.Where(r => r.Saldo != 0m).Select(r => new ApunteRetencion(0, CalculoImpuestoSociedades.FechaAsiento(d.Ejercicio), r.Cuenta, "Saldo", r.Saldo)).ToList();

        // Contabilidad: (cuenta, pagador).
        var grupos = apuntes.GroupBy(a => (a.Cuenta, Clave: Normal(a.Nif) is { Length: > 0 } n ? n : (a.Pagador ?? "").Trim().ToUpperInvariant()))
            .Select(g => new Grupo(g.Key.Cuenta, g.Key.Clave, g.Select(a => a.Pagador).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? NombreCuenta(g.Key.Cuenta),
                g.Select(a => a.Nif).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)), g.ToList(), g.Any(a => !string.IsNullOrWhiteSpace(a.Pagador))))
            .Where(g => g.Apuntes.Sum(a => a.Importe) != 0m)
            .ToList();

        // Hacienda: (modelo, pagador).
        var aeat = e.RetencionesAeat ?? [];
        var hayAeat = aeat.Count > 0;
        var propias = new List<Grupo>();
        foreach (var ga in aeat.GroupBy(a => (a.Modelo, Clave: Normal(a.Nif) is { Length: > 0 } n ? n : a.Nombre.Trim().ToUpperInvariant())))
        {
            var lineas = ga.ToList();
            var nif = Normal(lineas[0].Nif);
            Grupo? destino = null;
            if (nif.Length > 0)
                destino = grupos.Where(g => Normal(g.Nif) == nif).OrderByDescending(g => g.Apuntes.Sum(a => a.Importe)).FirstOrDefault();
            if (destino is null && lineas[0].Modelo != LectorDatosFiscalesAeat.Modelo202)
            {
                var palabras = LectorDatosFiscalesAeat.Palabras(lineas[0].Nombre);
                var puntos = grupos.Where(g => g.ConPagador && g.Aeat.Count == 0)
                    .Select(g => (g, n: LectorDatosFiscalesAeat.Palabras(g.Pagador).Count(palabras.Contains))).Where(x => x.n > 0).ToList();
                if (puntos.Count > 0)
                {
                    var max = puntos.Max(x => x.n);
                    var mejores = puntos.Where(x => x.n == max).ToList();
                    // Con el mismo pagador en varias 473 gana la de más importe; si son pagadores distintos, no se adivina.
                    if (mejores.Select(x => x.g.Clave).Distinct().Count() == 1)
                        destino = mejores.OrderByDescending(x => x.g.Apuntes.Sum(a => a.Importe)).First().g;
                }
            }
            var cuentaLector = lineas.Select(l => l.Cuenta).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
            if (destino is null && cuentaLector is not null)
                destino = grupos.FirstOrDefault(g => g.Cuenta == cuentaLector && !g.ConPagador)
                    ?? propias.FirstOrDefault(g => g.Cuenta == cuentaLector);
            if (destino is null)
            {
                destino = new Grupo(cuentaLector, $"{lineas[0].Modelo}|{ga.Key.Clave}", lineas[0].Nombre, lineas[0].Nif, [], false);
                propias.Add(destino);
            }
            destino.Aeat.AddRange(lineas);
        }

        var valores = (e.Valores ?? []).GroupBy(v => v.Clave, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
        return grupos.Concat(propias).Select(g =>
            {
                var clave = g.Apuntes.Count > 0 ? $"{g.Cuenta}|{g.Clave}" : $"AEAT|{g.Clave}";
                var escrito = valores.GetValueOrDefault(clave);
                var cuenta = g.Apuntes.Count > 0 ? g.Cuenta : (escrito?.Cuenta is { Length: > 0 } ce ? ce.Trim() : g.Cuenta);
                var contable = g.Apuntes.Sum(a => a.Importe);
                decimal? hacienda = hayAeat ? g.Aeat.Sum(a => a.Importe) : null;
                var propuesto = hacienda ?? Math.Max(0m, contable);
                return new ContrasteRetencion(clave, g.Pagador, g.Nif ?? g.Aeat.Select(a => a.Nif).FirstOrDefault(n => n is not null), cuenta, NombreCuenta(cuenta),
                    hacienda, contable, g.Apuntes.OrderBy(a => a.Fecha).ThenBy(a => a.IdAsiento).ToList(), g.Aeat.ToList(), propuesto, escrito?.Importe ?? propuesto,
                    string.IsNullOrWhiteSpace(escrito?.Justificacion) ? null : escrito!.Justificacion!.Trim());
            })
            .OrderBy(f => f.Cuenta ?? "~", StringComparer.Ordinal).ThenByDescending(f => Math.Max(f.Contable, f.Hacienda ?? 0m)).ToList();
    }

    /// <summary>Por qué no se puede confirmar (null si se puede): valores sin justificar o sin cuenta.</summary>
    public static string? PorQueNo(IReadOnlyList<ContrasteRetencion> filas)
    {
        if (filas.FirstOrDefault(f => f.FaltaJustificacion) is { } sin)
            return $"Justifica en el contraste de retenciones por qué el valor de {sin.Pagador} no es el de Hacienda.";
        if (filas.FirstOrDefault(f => f.Valor != 0m && f.Cuenta is null) is { } sinCuenta)
            return $"Elige en el contraste de retenciones la 473 de {sinCuenta.Pagador}.";
        return null;
    }

    private static string Normal(string? nif) => new((nif ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    private sealed class Grupo(string? cuenta, string clave, string pagador, string? nif, List<ApunteRetencion> apuntes, bool conPagador)
    {
        public string? Cuenta { get; } = cuenta;
        public string Clave { get; } = clave;
        public string Pagador { get; } = pagador;
        public string? Nif { get; } = nif;
        public List<ApunteRetencion> Apuntes { get; } = apuntes;
        public bool ConPagador { get; } = conPagador;
        public List<RetencionAeat> Aeat { get; } = [];
    }
}

/// <summary>Partida del cálculo de la renta de viviendas: un centro y una cuenta con su importe (positivo).</summary>
/// <param name="Clase">Ingreso de viviendas, gasto directo, gasto indirecto u otro ingreso ordinario.</param>
public sealed record PartidaRentaViviendas(string Clase, string Centro, string Cuenta, string NombreCuenta, decimal Importe, int Apuntes)
{
    public const string IngresoViviendas = "Ingreso de viviendas", GastoDirecto = "Gasto directo", GastoIndirecto = "Gasto indirecto",
        OtroIngresoOrdinario = "Otro ingreso ordinario";
}

/// <summary>
/// Renta de viviendas para la bonificación del IS (art. 49.2 LIS; propietario 2026-11-17 12:44): ingresos de los centros de las actividades
/// bonificadas − sus gastos directos − la parte de los gastos indirectos (sin actividad o de la actividad general, con los financieros y las
/// amortizaciones) proporcional a sus ingresos ordinarios (70 a 75) sobre los ingresos ordinarios de toda la sociedad.
/// </summary>
public sealed record RentaViviendasDetalle(decimal Ingresos, decimal GastosDirectos, decimal GastosIndirectos, decimal IngresosOrdinariosViviendas,
    decimal IngresosOrdinariosTotales, IReadOnlyList<PartidaRentaViviendas> Partidas)
{
    public decimal Proporcion => IngresosOrdinariosTotales > 0m ? IngresosOrdinariosViviendas / IngresosOrdinariosTotales : 0m;
    public decimal IndirectosImputados => Math.Round(GastosIndirectos * Proporcion, 2, MidpointRounding.AwayFromZero);
    public decimal Renta => Ingresos - GastosDirectos - IndirectosImputados;
}
