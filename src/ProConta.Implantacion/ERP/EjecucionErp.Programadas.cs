using System.Globalization;
using ProConta.Facturacion.Programadas;
using ProConta.Implantacion.ContaSol;

namespace ProConta.Implantacion.ERP;

// Fase 3: facturas programadas activas, con sus líneas, sin generar nada.
internal sealed partial class EjecucionErp
{
    private const string Fase3 = "3 programadas";

    private async Task ProgramadasAsync()
    {
        var est = Est(Fase3, "FacturasProgramadas");
        var estLineas = Est(Fase3, "LineasFacturaProgramada");
        var todas = o.Tabla("FacturasProgramadas");
        est.Leidas = todas.Count;
        estLineas.Leidas = o.Tabla("LineasFacturaProgramada").Count;
        var lineasPor = o.Tabla("LineasFacturaProgramada").GroupBy(l => l.E("Id") ?? -1).ToDictionary(g => g.Key, g => g.OrderBy(l => l.E("NumLinea") ?? 0).ToList());
        var anio = UltimoEjercicio;
        var idEj = _ejercicios[anio].Id;
        if (!_series.TryGetValue(anio, out var serie))
            serie = (await ConsultarAsync("SELECT IdSerie FROM SeriesFacturacion WHERE IdSociedadEjercicio = ? AND EsRectificativa = False ORDER BY EsPredeterminada, IdSerie", f => f.Entero("IdSerie"), idEj)).LastOrDefault();
        if (serie == 0) { Anotar(Fase3, "FacturasProgramadas", null, null, "Erp.SinSerie", Severidad.Bloqueante, $"El ejercicio {anio} no tiene serie de facturación: no puedo cargar las programadas."); return; }

        foreach (var p in todas.OrderBy(p => p.E("Id")))
        {
            cancelacion.ThrowIfCancellationRequested();
            var id = p.E("Id") ?? 0;
            var clave = Txt(id);
            var lineasErp = lineasPor.GetValueOrDefault(id) ?? [];
            if (!p.B("Activa")) { Excluir(Fase3, "FacturasProgramadas", clave, $"La programada {id} está inactiva en el ERP: no se migra.", Severidad.Informativa); estLineas.Excluidas += lineasErp.Count; continue; }
            if (p.F("Fecha") is not DateTime proxima) { Excluir(Fase3, "FacturasProgramadas", clave, $"La programada {id} no tiene fecha de próxima generación."); estLineas.Excluidas += lineasErp.Count; continue; }
            int? idCliente = null;
            if (p.E("IdCliente") is int pc && (idCliente = Mapa(TiposErp.Cliente, Txt(pc))) is null)
                ClaveNula(Fase3, "FacturasProgramadas", clave, "IdCliente", pc, $"El cliente {pc} de la programada {id} no está en Personas: queda sin cliente.");
            var meses = p.E("PeriodicidadMeses") ?? 1;
            var periodicidad = Periodicidad.Todas.FirstOrDefault(x => x.Meses == meses && !x.EsPuntual);
            if (periodicidad is null)
            {
                Anotar(Fase3, "FacturasProgramadas", clave, "PeriodicidadMeses", "Erp.PeriodicidadDesconocida", Severidad.Advertencia, $"La programada {id} se repite cada {meses} meses, que no es una periodicidad del programa: la dejo mensual.");
                periodicidad = Periodicidad.Mensual;
            }
            // El programa calcula cada emisión desde la primera y el número de generadas: se lleva el origen hacia atrás las
            // facturas ya generadas para que la próxima sea la fecha del ERP y el periodo, el del ERP.
            var generadas = Math.Max(0, p.E("FacturasGeneradas") ?? 0);
            var atras = -generadas * periodicidad.Meses;
            var inicio = CalendarioProgramacion.Desplazar(proxima, atras);
            DateTime? periodoDesde = p.F("FechaInicio") is DateTime fi ? CalendarioProgramacion.Desplazar(fi, atras) : null;
            DateTime? periodoHasta = p.F("FechaFin") is DateTime ff ? CalendarioProgramacion.Desplazar(ff, atras) : null;
            var repeticiones = p.E("NumeroRepeticiones") is int nr and > 0 ? nr : (int?)null;

            var lineas = new List<(Facturacion.Facturas.LineaFacturaVenta Linea, int? Concepto)>();
            decimal tipoRetencion = 0m;
            foreach (var l in lineasErp)
            {
                var ic = l.E("IdConcepto");
                var concepto = ic is int c && _conceptosErp.ContainsKey(c) ? new ConceptoLineaErp(CuentaConcepto(c), DepartamentoConcepto(c)) : null;
                if (ic is int c2 && !_conceptosErp.ContainsKey(c2))
                    ClaveNula(Fase3, "LineasFacturaProgramada", $"{id}:{l.E("NumLinea")}", "IdConcepto", c2, $"El concepto {c2} de la programada {id} no existe.");
                var t = ReglasErp.Transformar(LineaErp(l), concepto, concepto is null ? null : Centro(ReglasErp.ClaveCentro(concepto.Departamento)));
                if (t.TipoRetencion is decimal tr) { tipoRetencion = tr; continue; }
                var idConcepto = ic is int c3 ? Mapa(TiposErp.Concepto, Txt(c3)) : null;
                var linea = t.Linea!;
                var total = linea.ImporteTotal.Valor;
                var regenerada = LineasProgramada.Copiar(linea);
                if (regenerada.ImporteTotal.Valor != total)
                    Anotar(Fase3, "LineasFacturaProgramada", $"{id}:{l.E("NumLinea")}", "Precio", "Erp.RedondeoProgramada", Severidad.Advertencia,
                        $"La línea {l.E("NumLinea")} de la programada {id} sumaba {total:0.00} € en el ERP y al generarla saldrá {regenerada.ImporteTotal.Valor:0.00} € (precio sin IVA redondeado a 4 decimales).");
                lineas.Add((linea, idConcepto));
            }

            var notas = ReglasErp.UnirNotas(p.T("Notas"), p.T("NotasInternas"),
                p.T("Referencia1") is { } ipc ? $"Revisión del IPC (ERP, Referencia1): {ipc}." : null,
                p.F("FechaPrimerVencimiento") is DateTime pv ? $"Primer vencimiento en el ERP: {pv:dd/MM/yyyy} (el día de pago se pone a mano)." : null,
                $"Programada {id} del ERP.");
            var ahora = Ahora;
            var idProgramada = await _bd.EnTransaccionAsync(async s =>
            {
                await s.EjecutarAsync(
                    "INSERT INTO FacturasProgramadas (IdSociedadEjercicio, IdCliente, IdSerie, TipoFactura, FechaInicio, FechaFin, DiaEmision, ProximaFechaEmision, TipoRetencion, " +
                    "Descripcion, NotasInternas, Activa, FechaAlta, OperadorAlta, Historial, EnviarPorEmail, PeriodoDesde, PeriodoHasta, NumeroRepeticiones, FacturasGeneradas, AlGenerar, Version, Periodicidad) " +
                    "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    idEj, idCliente, serie, "F1", inicio, null, (byte)proxima.Day, proxima, (float)tipoRetencion,
                    ReglasErp.Recortar(p.T("DescripcionFactura") ?? p.T("Descripcion"), 255), notas, true, p.F("FechaCreacion") ?? ahora.Date, Operador,
                    $"{ahora:dd/MM/yyyy HH:mm} · {Operador} · Migrada del ERP (programada {id}, {generadas} facturas generadas).", false, periodoDesde, periodoHasta, repeticiones, generadas,
                    "BORRADOR", 0, periodicidad.Codigo);
                var nuevo = await s.UltimoIdAsync();
                var n = 1;
                foreach (var (l, c) in lineas)
                    await s.EjecutarAsync(
                        "INSERT INTO LineasFacturasProgramadas (IdFacturaProgramada, NumeroLinea, IdConcepto, Nombre, Descripcion, NotasInternas, Cantidad, PrecioUnitario, DescuentoPorcentaje, " +
                        "DescuentoImporte, Impuesto, ClaveRegimen, CalificacionOperacion, CausaExencion, TipoIVA, TipoRecargoEquivalencia, EsSuplido, CuentaContable, IdDepartamento, IdCentroCoste, Activa) " +
                        "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                        nuevo, (short)n++, c, l.Nombre.Length == 0 ? null : l.Nombre, l.Descripcion.Length == 0 ? "-" : l.Descripcion, l.NotasInternas, l.Cantidad, l.PrecioUnitario, 0m, 0m,
                        l.Impuesto, l.ClaveRegimen, l.CalificacionOperacion, l.CausaExencion, l.TipoIva, 0m, false, ReglasErp.Recortar(l.CuentaContable, 7), null, l.IdCentroCoste, true);
                return nuevo;
            }, cancelacion);
            Mapear(TiposErp.Programada, clave, idProgramada);
            est.Cargadas++;
            estLineas.Cargadas += lineas.Count;
        }
    }
}
