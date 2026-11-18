using System.Globalization;
using ProConta.Facturacion.Programadas;
using ProConta.Inicio.Salud;
using static ProConta.Inicio.Salud.CalendarioFiscal;

namespace ProConta.Inicio.Bloques;

/// <summary>Qué es una entrada de la agenda.</summary>
public enum TipoAgenda { Fiscal, CambioIva, Ejercicio, Programadas }

/// <summary>Una fecha de la agenda: qué pasa, el detalle (tooltip), el importe si lo tiene y la pantalla que la abre.</summary>
public sealed record EntradaAgenda(DateTime Fecha, TipoAgenda Tipo, string Texto, string Detalle, decimal? Importe, DestinoSalud Destino);

/// <summary>
/// Bloque «Agenda» de Inicio (pantalla-de-inicio 2026-11-15 16:37): los próximos vencimientos en orden de fecha, de hoy a
/// <see cref="Dias"/> días, solo lectura y sin eventos propios: los plazos fiscales de <see cref="CalendarioFiscal"/> (fin del plazo
/// del 303 de cada trimestre y del 390, cambios de tipo de IVA y el 31/12 si falta crear el ejercicio siguiente) y las emisiones de
/// las facturas programadas activas, agrupadas por día con su número y su importe total. Las programadas con vencimientos
/// anteriores a hoy sin generar salen hoy, como pendientes de generar. Lo que hay que hacer, y si ya está hecho, se ve en Salud.
/// </summary>
public static class CalculoAgenda
{
    public const int Dias = 90;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-ES");

    private static readonly DestinoSalud Programadas = new(PantallaSalud.Programadas, "Ventas › Facturas programadas");

    public static IReadOnlyList<EntradaAgenda> Calcular(DateTime hoy, IReadOnlySet<int> ejercicios, IReadOnlyList<VencimientoProgramado> programadas,
        IReadOnlyList<CambioTipoIva>? cambios = null, ModelosInicio? modelos = null)
    {
        modelos ??= ModelosInicio.PorDefecto;
        hoy = hoy.Date;
        var hasta = hoy.AddDays(Dias);
        bool Dentro(DateTime d) => d >= hoy && d <= hasta;
        var r = new List<EntradaAgenda>();
        foreach (var anio in new[] { hoy.Year - 1, hoy.Year }.Where(ejercicios.Contains))
        {
            foreach (var def in ProConta.Contabilidad.Modelos.DefinicionModelo.Todos.Where(d => modelos.Modelos.Contains(d.Id)))
                foreach (var periodo in ProConta.Contabilidad.Modelos.PlazosModelos.Periodos(def.Id))
                {
                    var (inicio, fin) = ProConta.Contabilidad.Modelos.PlazosModelos.Plazo(def.Id, anio, periodo);
                    if (!Dentro(fin) || modelos.Presentada(def.Id, anio, periodo) is not null) continue;
                    var nombre = periodo == "0A" ? $"de {anio}" : periodo is "4T" or "3P" || anio != hoy.Year ? $"del {periodo} de {anio}" : $"del {periodo}";
                    var texto = def.Id == "390" ? $"Último día del resumen anual 390 de {anio}" : $"Último día del {def.Id} {nombre}";
                    r.Add(new EntradaAgenda(fin, TipoAgenda.Fiscal, texto,
                        $"Plazo de presentación del modelo {def.Id} ({def.Nombre}) {nombre}: del {inicio:dd/MM} al {fin:dd/MM/yyyy}" +
                        (fin != ProConta.Contabilidad.Modelos.PlazosModelos.PlazoLegal(def.Id, anio, periodo).Hasta ? " (el último día pasa al siguiente hábil)" : "") +
                        ". Doble clic: abre Modelos tributarios en ese periodo.",
                        null, DestinoSalud.Modelos(def.Id, periodo)));
                }
        }
        foreach (var c in cambios ?? CambiosTipoIva)
        {
            if (!Dentro(c.Desde)) continue;
            r.Add(new EntradaAgenda(c.Desde, TipoAgenda.CambioIva, $"{Mayuscula(c.Que)} pasa al {c.TipoNuevo} de IVA",
                $"Desde el {c.Desde:dd/MM/yyyy} {c.Que} lleva un {c.TipoNuevo} de IVA ({c.Origen}). Las ventas con entrada anterior se facturan antes, con el tipo de antes: Salud avisa de las que quedan.",
                null, new DestinoSalud(PantallaSalud.VentasPorFacturar, "Ventas › Ventas por facturar")));
        }
        var finAnio = new DateTime(hoy.Year, 12, 31);
        if (!ejercicios.Contains(hoy.Year + 1) && Dentro(finAnio))
            r.Add(new EntradaAgenda(finAnio, TipoAgenda.Ejercicio, $"Crear el ejercicio {hoy.Year + 1} antes de fin de año",
                $"La sociedad aún no tiene el ejercicio {hoy.Year + 1}: hace falta para contabilizar o facturar desde el 1/1/{hoy.Year + 1}.", null,
                new DestinoSalud(PantallaSalud.Ejercicios, "Configuración › Parámetros y tablas › Ejercicios")));

        var atrasadas = programadas.Where(p => p.Fecha.Date < hoy).ToList();
        if (atrasadas.Count > 0)
            r.Add(Grupo(hoy, atrasadas, $"{N(atrasadas.Count, "factura programada pendiente", "facturas programadas pendientes")} de generar (desde el {atrasadas.Min(p => p.Fecha):dd/MM})"));
        foreach (var g in programadas.Where(p => Dentro(p.Fecha.Date)).GroupBy(p => p.Fecha.Date))
        {
            var lista = g.ToList();
            r.Add(Grupo(g.Key, lista, lista.Count == 1 ? $"Se emite la factura programada {lista[0].Rotulo} ({lista[0].Cliente})" : $"Se emiten {lista.Count.ToString(Es)} facturas programadas"));
        }
        return r.OrderBy(e => e.Fecha).ThenBy(e => e.Tipo).ToList();
    }

    private static EntradaAgenda Grupo(DateTime fecha, IReadOnlyList<VencimientoProgramado> lista, string texto)
    {
        var clientes = lista.Select(p => p.Cliente).Distinct().ToList();
        var detalle = $"{string.Join(", ", clientes.Take(6))}{(clientes.Count > 6 ? $" y {clientes.Count - 6} más" : "")}. Importe total con IVA de sus plantillas: {Imp(lista.Sum(p => p.Importe))}.";
        return new EntradaAgenda(fecha, TipoAgenda.Programadas, texto, detalle, lista.Sum(p => p.Importe), Programadas);
    }

    private static string Mayuscula(string s) => s.Length == 0 ? s : char.ToUpper(s[0], Es) + s[1..];
}
