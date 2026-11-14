using ProConta.Implantacion.ContaSol;

namespace ProConta.Implantacion.ERP;

// Fase 8: reservas de pisos turísticos (facturacion/reservas 2026-11-14 11:45). ReservasPisosTuristicos → VentasPorFacturar de la
// sociedad, como ventas ya facturadas en el ERP (FacturadaFuera) si tienen IdFactura, sin enlazarlas a la factura migrada (su
// IdFactura no es fiable); las que no lo tienen quedan pendientes de facturar. El importe es lo que pagó el huésped: hasta 11/2025
// el ERP guardaba en ImporteBruto lo del huésped y en ImporteNeto lo liquidado; desde 12/2025, al revés, así que se toma el mayor.
// El piso no se asigna (el ERP lo guarda como texto libre): queda en las notas. Los orígenes se crean si faltan, con el cliente
// de la persona del ERP que se llama como el origen (como hacía el ERP).
internal sealed partial class EjecucionErp
{
    private const string Fase8 = "8 reservas";

    private async Task ReservasAsync()
    {
        var reservas = o.Tabla("ReservasPisosTuristicos");
        var est = Est(Fase8, "ReservasPisosTuristicos");
        est.Leidas = reservas.Count;
        if (reservas.Count == 0) return;

        // Lo cargado por una ejecución anterior se sustituye (lo ya facturado en ProContaVF se conserva).
        var borradas = await EjecutarAsync("DELETE FROM VentasPorFacturar WHERE IdSociedad = ? AND Fuente = 'ERP' AND IdFactura IS NULL", Sociedad);
        if (borradas > 0)
            Anotar(Fase8, "VentasPorFacturar", null, null, "Erp.Sustituido", Severidad.Informativa, $"Borradas {borradas} reservas cargadas por la ejecución anterior para volver a cargarlas.");
        var conservadas = (await ConsultarAsync("SELECT IdExterno FROM VentasPorFacturar WHERE IdSociedad = ? AND Fuente = 'ERP'", f => f.Texto("IdExterno") ?? "", Sociedad)).ToHashSet();

        var origenes = await OrigenesReservasAsync(reservas);
        var ahora = Ahora;
        foreach (var r in reservas.OrderBy(r => r.F("FechaInicio")).ThenBy(r => r.E("Id")))
        {
            cancelacion.ThrowIfCancellationRequested();
            var id = r.E("Id") ?? 0;
            var clave = Txt(id);
            if (conservadas.Contains(clave)) { est.Excluidas++; continue; }
            if (r.F("FechaInicio") is not DateTime entrada) { Excluir(Fase8, "ReservasPisosTuristicos", clave, $"La reserva {id} no tiene fecha de entrada: no sé en qué mes va."); continue; }
            var textoOrigen = r.T("Origen") ?? "Directa";
            if (!origenes.TryGetValue(textoOrigen, out var idOrigen)) { Excluir(Fase8, "ReservasPisosTuristicos", clave, $"La reserva {id} tiene el origen «{textoOrigen}», que no he podido crear."); continue; }

            var neto = r.Dec("ImporteNeto");
            var bruto = r.D("ImporteBruto");
            var comision = r.D("GastosIntermediario");
            var importe = Math.Round(bruto is decimal b && b > neto ? b : neto, 2, MidpointRounding.AwayFromZero);
            var noches = r.E("Noches") ?? 0;
            DateTime? salida = noches is > 0 and <= 365 ? entrada.AddDays(noches) : null;
            if (noches > 365)
                Anotar(Fase8, "ReservasPisosTuristicos", clave, "Noches", "Erp.NochesNoValidas", Severidad.Advertencia,
                    $"La reserva {id} del {entrada:dd/MM/yyyy} tiene {noches} noches en el ERP: la cargo sin fecha de salida y con cantidad 1.", noches.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var facturada = (r.E("IdFactura") ?? 0) > 0;
            var notas = ReglasErp.UnirNotas(r.T("Alojamiento") is { } aloj ? $"Alojamiento en el ERP: {aloj}." : null,
                r.T("Detalles"), r.T("Referencia") is { } refe ? $"Referencia en el ERP: {refe}." : null,
                facturada ? $"Facturada en el ERP (IdFactura {r.E("IdFactura")}, sin comprobar)." : null, $"Reserva {id} del ERP.");
            await EjecutarAsync(
                "INSERT INTO VentasPorFacturar (IdSociedad, IdOrigenVenta, FechaReferencia, FechaFin, Cantidad, Importe, Comision, Referencia, Titular, Anulada, FacturadaFuera, " +
                "Fuente, IdExterno, Notas, FechaAlta, OperadorAlta, FechaModificacion, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, False, ?, 'ERP', ?, ?, ?, ?, ?, 0)",
                Sociedad, idOrigen, entrada, salida, (decimal)(noches is > 0 and <= 365 ? noches : 1), importe, comision is decimal c ? Math.Round(c, 2, MidpointRounding.AwayFromZero) : null,
                ReglasErp.Recortar(r.T("CodigoReserva"), 60), ReglasErp.Recortar(r.T("Huesped"), 150), facturada, clave, notas, ahora, Operador, ahora);
            est.Cargadas++;
            if (!facturada)
                Anotar(Fase8, "ReservasPisosTuristicos", clave, "IdFactura", "Erp.ReservaSinFacturar", Severidad.Informativa,
                    $"La reserva {id} del {entrada:dd/MM/yyyy} no estaba facturada en el ERP: queda pendiente de facturar (asígnale el piso y el concepto).");
        }
    }

    /// <summary>Origen de cada texto «Origen» de las reservas: el de la sociedad con ese código o nombre o, si no hay, uno nuevo.</summary>
    private async Task<Dictionary<string, int>> OrigenesReservasAsync(IReadOnlyList<Fila> reservas)
    {
        var existentes = await ConsultarAsync("SELECT IdOrigenVenta, Codigo, Nombre, TextosImportacion FROM OrigenesVenta WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdOrigenVenta"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Nombre: f.Texto("Nombre") ?? "", Textos: f.Texto("TextosImportacion") ?? ""), Sociedad);
        var personas = o.Tabla("Personas").Where(p => p.T("Nombre") is not null).GroupBy(p => p.T("Nombre")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().E("IdPersona"), StringComparer.OrdinalIgnoreCase);
        var r = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var texto in reservas.Select(x => x.T("Origen") ?? "Directa").Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var codigo = CodigoOrigen(texto);
            var hallado = existentes.FirstOrDefault(e => e.Codigo.Equals(codigo, StringComparison.OrdinalIgnoreCase) || e.Nombre.Equals(texto, StringComparison.OrdinalIgnoreCase)
                || e.Textos.Split(';', StringSplitOptions.TrimEntries).Contains(texto, StringComparer.OrdinalIgnoreCase));
            if (hallado.Id != 0) { r[texto] = hallado.Id; continue; }
            int? cliente = personas.TryGetValue(texto, out var idPersona) && idPersona is int ip ? Mapa(TiposErp.Cliente, Txt(ip)) : null;
            var nombre = codigo switch { "AIRBNB" => "Airbnb", "BOOKING" => "Booking", "DIRECTA" => "Directa", _ => texto };
            var textos = codigo switch { "AIRBNB" => "Airbnb", "BOOKING" => "Booking.com;Booking", _ => null };
            r[texto] = await InsertarAsync("INSERT INTO OrigenesVenta (IdSociedad, Codigo, Nombre, IdCliente, TextosImportacion, EsDirecta, Activo) VALUES (?, ?, ?, ?, ?, ?, True)",
                Sociedad, codigo, nombre, cliente, textos, codigo == "DIRECTA");
            Anotar(Fase8, "OrigenesVenta", codigo, "IdCliente", cliente is null ? "Erp.OrigenSinCliente" : "Erp.OrigenCreado", cliente is null ? Severidad.Advertencia : Severidad.Informativa,
                cliente is null ? $"He creado el origen de venta «{nombre}» sin cliente: pónselo en Parámetros y tablas › Orígenes de venta antes de facturar sus reservas."
                                : $"He creado el origen de venta «{nombre}» con el cliente de la persona «{texto}» del ERP.");
        }
        return r;
    }

    private static string CodigoOrigen(string texto)
    {
        var t = texto.Trim().ToUpperInvariant();
        if (t.Contains("AIRBNB", StringComparison.Ordinal)) return "AIRBNB";
        if (t.Contains("BOOKING", StringComparison.Ordinal)) return "BOOKING";
        if (t.Contains("DIRECT", StringComparison.Ordinal)) return "DIRECTA";
        var limpio = new string(t.Where(char.IsAsciiLetterOrDigit).ToArray());
        return limpio.Length == 0 ? "OTRO" : limpio.Length <= 20 ? limpio : limpio[..20];
    }
}
