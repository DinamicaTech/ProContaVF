using System.Globalization;
using ProConta.Datos.Persistencia;

namespace ProConta.Implantacion.ERP;

/// <summary>Resultado de la pasada que asigna el piso a las reservas del ERP ya migradas.</summary>
public sealed class ResultadoPisosReservasErp
{
    /// <summary>Reservas del ERP en la BD destino (Fuente ERP de la sociedad).</summary>
    public int Reservas { get; set; }

    /// <summary>Asignadas en esta pasada, por la descripción de su regla.</summary>
    public Dictionary<string, int> AsignadasPorRegla { get; } = new(StringComparer.Ordinal);

    /// <summary>Las que ya tenían activo relacionado (de una pasada anterior o puesto a mano): no se tocan.</summary>
    public int YaTenianPiso { get; set; }

    /// <summary>Las enlazadas a una factura de ProContaVF (borrador o validada): no se editan.</summary>
    public int FacturadasEnProConta { get; set; }

    /// <summary>Las que se quedan sin piso, con su motivo.</summary>
    public List<string> SinPiso { get; } = [];

    public string Texto()
    {
        var l = new List<string> { $"Reservas del ERP en la BD: {Reservas}." };
        l.AddRange(AsignadasPorRegla.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"  Asignadas por el {x.Key}: {x.Value}."));
        l.Add($"  Ya tenían activo relacionado: {YaTenianPiso}.");
        if (FacturadasEnProConta > 0) l.Add($"  Enlazadas a una factura de ProContaVF (no se tocan): {FacturadasEnProConta}.");
        l.Add($"  Sin piso: {SinPiso.Count}.");
        l.AddRange(SinPiso.Select(s => $"    {s}"));
        return string.Join(Environment.NewLine, l);
    }
}

/// <summary>
/// Corrección de datos de la migración del ERP para las BD migradas antes del 2026-11-15 (facturacion/reservas 2026-11-15 17:45):
/// pone el activo relacionado a las reservas del ERP (VentasPorFacturar con Fuente ERP) que no lo tienen, con las mismas reglas que
/// la fase 8 (<see cref="ReglasErp.PisosReservas"/>) sobre el texto «Alojamiento» de su fila del ERP (IdExterno = Id). El activo es
/// el que la fase 6 migró del inmueble (ReferenciaMigracion «ERP:Inmuebles:Id»). Idempotente: solo rellena las que no tienen
/// activo y no toca las enlazadas a una factura de ProContaVF. Una migración nueva ya no la necesita.
/// </summary>
public sealed class CorreccionPisosReservasErp(IBaseDatos bd)
{
    public async Task<ResultadoPisosReservasErp> AplicarAsync(OrigenErp origen, string idSociedad, DateTime ahora, CancellationToken cancelacion = default)
    {
        var alojamientos = origen.Tabla("ReservasPisosTuristicos")
            .Where(r => r.E("Id") is not null)
            .GroupBy(r => r.E("Id")!.Value)
            .ToDictionary(g => g.Key.ToString(CultureInfo.InvariantCulture), g => g.First().T("Alojamiento"));
        return await bd.EnTransaccionAsync(async s =>
        {
            var activos = new Dictionary<int, int>();
            foreach (var (id, referencia) in await s.ConsultarAsync("SELECT IdActivo, ReferenciaMigracion FROM Activos WHERE IdSociedad = ? AND ReferenciaMigracion LIKE 'ERP:Inmuebles:%'",
                         f => (f.Entero("IdActivo"), f.Texto("ReferenciaMigracion") ?? ""), idSociedad))
                if (int.TryParse(referencia["ERP:Inmuebles:".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var inmueble))
                    activos.TryAdd(inmueble, id);

            var ventas = await s.ConsultarAsync("SELECT IdVentaPorFacturar, IdExterno, IdActivo, IdFactura, FechaReferencia FROM VentasPorFacturar WHERE IdSociedad = ? AND Fuente = 'ERP'",
                f => (Id: f.Entero("IdVentaPorFacturar"), Externo: f.Texto("IdExterno") ?? "", Activo: f.EnteroNulo("IdActivo"), Factura: f.EnteroNulo("IdFactura"), Entrada: f.Fecha("FechaReferencia")), idSociedad);
            var r = new ResultadoPisosReservasErp { Reservas = ventas.Count };
            foreach (var v in ventas.OrderBy(v => v.Entrada).ThenBy(v => v.Id))
            {
                cancelacion.ThrowIfCancellationRequested();
                if (v.Activo is not null) { r.YaTenianPiso++; continue; }
                if (v.Factura is not null) { r.FacturadasEnProConta++; continue; }
                var reserva = $"Reserva {v.Externo} del ERP del {v.Entrada:dd/MM/yyyy}";
                if (!alojamientos.TryGetValue(v.Externo, out var alojamiento)) { r.SinPiso.Add($"{reserva}: ya no está en el ERP leído."); continue; }
                if (ReglasErp.PisoDeReserva(alojamiento) is not { } regla) { r.SinPiso.Add($"{reserva}: su alojamiento «{alojamiento ?? "(vacío)"}» no casa con ninguna regla."); continue; }
                if (!activos.TryGetValue(regla.IdInmuebleErp, out var activo)) { r.SinPiso.Add($"{reserva}: el inmueble {regla.IdInmuebleErp} del ERP no está migrado como activo."); continue; }
                await s.EjecutarAsync("UPDATE VentasPorFacturar SET IdActivo = ?, FechaModificacion = ?, Version = Version + 1 WHERE IdVentaPorFacturar = ? AND IdActivo IS NULL AND IdFactura IS NULL",
                    activo, ahora, v.Id);
                r.AsignadasPorRegla[regla.Descripcion] = r.AsignadasPorRegla.GetValueOrDefault(regla.Descripcion) + 1;
            }
            return r;
        }, cancelacion);
    }
}
