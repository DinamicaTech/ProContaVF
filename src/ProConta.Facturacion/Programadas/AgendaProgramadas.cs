using ProConta.Datos.Persistencia;

namespace ProConta.Facturacion.Programadas;

/// <summary>Un vencimiento de una factura programada activa: su fecha de emisión, el cliente y el importe total de su plantilla.</summary>
public sealed record VencimientoProgramado(int IdFacturaProgramada, string Rotulo, string Cliente, DateTime Fecha, decimal Importe);

/// <summary>
/// Próximos vencimientos de las facturas programadas, para la agenda de Inicio (pantalla-de-inicio 2026-11-15 16:37). Solo lee. Usa
/// la misma programación que «Generar facturas»: desde el siguiente vencimiento sin generar de cada programada activa, como mucho
/// <see cref="ServicioProgramadas.MaximoPorProgramada"/> por programada; los anteriores a hoy son los que faltan por generar.
/// </summary>
public static class AgendaProgramadas
{
    public static Task<IReadOnlyList<VencimientoProgramado>> VencimientosAsync(IBaseDatos bd, string idSociedad, DateTime hasta, CancellationToken cancelacion = default) =>
        bd.EjecutarAsync<IReadOnlyList<VencimientoProgramado>>(async s =>
        {
            var programadas = (await RepositorioProgramadas.ListarAsync(s, idSociedad)).Where(p => p.Activa).ToList();
            if (programadas.Count == 0) return [];
            var nombres = (await s.ConsultarAsync("SELECT c.IdCliente, t.Nombre, t.RazonSocial FROM Clientes AS c INNER JOIN Terceros AS t ON c.IdTercero = t.IdTercero",
                    f => (Id: f.Entero("IdCliente"), Nombre: f.Texto("RazonSocial") is { Length: > 0 } rs ? rs.Trim() : (f.Texto("Nombre") ?? "").Trim())))
                .GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().Nombre);
            var r = new List<VencimientoProgramado>();
            foreach (var p in programadas)
            {
                var cliente = p.Plantilla.IdCliente is int c && nombres.TryGetValue(c, out var n) ? n : "Sin cliente";
                var importe = p.Plantilla.Totales.ImporteTotal.Valor;
                for (var i = p.FacturasGeneradas; i < p.FacturasGeneradas + ServicioProgramadas.MaximoPorProgramada && p.ExisteVencimiento(i) && p.Emision(i)!.Value <= hasta.Date; i++)
                    r.Add(new VencimientoProgramado(p.IdFacturaProgramada, p.Rotulo, cliente, p.Emision(i)!.Value, importe));
            }
            return r.OrderBy(v => v.Fecha).ThenBy(v => v.IdFacturaProgramada).ToList();
        }, cancelacion);
}
