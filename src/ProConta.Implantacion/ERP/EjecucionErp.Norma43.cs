using System.Globalization;
using ProConta.Implantacion.ContaSol;

namespace ProConta.Implantacion.ERP;

// Fase 5: Norma 43 histórica: cuentas de origen (N43EntidadesBancarias), palabras clave, lotes, extractos y líneas con su
// imputación y el asiento de cada línea.
internal sealed partial class EjecucionErp
{
    private const string Fase5 = "5 norma43";

    private async Task Norma43Async()
    {
        var cuentas = await CuentasBancariasAsync();
        await PalabrasClaveN43Async();

        // Lotes (y uno más para los extractos que en el ERP no tienen lote).
        var estLotes = Est(Fase5, "N43LotesImportacion");
        var lotes = o.Tabla("N43LotesImportacion");
        estLotes.Leidas = lotes.Count;
        var cabeceras = o.Tabla("N43Cabeceras").Where(c => c.T("CodigoEmpresa") == Sociedad).ToList();
        foreach (var c in o.Tabla("N43Cabeceras").Where(c => c.T("CodigoEmpresa") != Sociedad))
            Excluir(Fase5, "N43Cabeceras", Txt(c.E("IdRegistro")), $"El extracto {c.E("IdRegistro")} es de la empresa {c.T("CodigoEmpresa")}, que no se migra.");
        var sinLote = cabeceras.Where(c => c.E("IdLote") is not int l || !lotes.Any(x => x.E("IdLote") == l)).ToList();
        var lineasErp = o.Tabla("N43Lineas").GroupBy(l => l.E("IdRegistro") ?? -1).ToDictionary(g => g.Key, g => g.OrderBy(l => l.E("NumLinea") ?? 0).ToList());
        var ahora = Ahora;
        var filasLote = lotes.Select(l => new object?[]
        {
            Sociedad, "NORMA43", "ERP", ReglasErp.Recortar(Path.GetFileName(l.T("Fichero") ?? $"lote{l.E("IdLote")}"), 255) ?? $"lote{l.E("IdLote")}", ReglasErp.Recortar(l.T("Fichero"), 255),
            $"ERP-N43-LOTE-{l.E("IdLote")}", 0, "ERP", "IMPORTADO",
            (l.F("Fecha") ?? ahora.Date) + (l.FH("Hora") is DateTime h ? h.TimeOfDay : TimeSpan.Zero), null, null,
            0, cabeceras.Count(c => c.E("IdLote") == l.E("IdLote")), cabeceras.Where(c => c.E("IdLote") == l.E("IdLote")).Sum(c => lineasErp.GetValueOrDefault(c.E("IdRegistro") ?? -1)?.Count ?? 0), 0, 0,
        }).ToList();
        if (sinLote.Count > 0)
        {
            filasLote.Add([Sociedad, "NORMA43", "ERP", "Extractos del ERP sin lote", null, "ERP-N43-SIN-LOTE", 0, "ERP", "IMPORTADO", ahora, null, null, 0, sinLote.Count,
                sinLote.Sum(c => lineasErp.GetValueOrDefault(c.E("IdRegistro") ?? -1)?.Count ?? 0), 0, 0]);
            Anotar(Fase5, "N43Cabeceras", null, "IdLote", "Erp.ClaveNula", Severidad.Advertencia,
                $"{sinLote.Count} extractos del ERP no tienen lote (o su lote no existe): los agrupo en un lote «Extractos del ERP sin lote».",
                string.Join(", ", sinLote.Select(c => $"{c.E("IdRegistro")}:{c.E("IdLote")}")));
        }
        var idsLote = await MasivaAsync("LotesImportacionBancaria",
        [
            "IdSociedad", "Formato", "VersionEsquema", "NombreFichero", "RutaOrigen", "HashSha256Contenido", "TamanoBytes", "Codificacion", "Estado", "FechaHoraInicio",
            "FechaHoraFin", "NumeroRegistrosDeclarado", "NumeroRegistrosLeido", "NumeroExtractos", "NumeroMovimientos", "NumeroErrores", "NumeroAdvertencias",
        ], filasLote, "IdLoteImportacionBancaria");
        var loteDe = new Dictionary<int, int>();
        for (var i = 0; i < lotes.Count; i++) { loteDe[lotes[i].E("IdLote") ?? -1] = idsLote[i]; Mapear(TiposErp.LoteN43, Txt(lotes[i].E("IdLote")), idsLote[i]); }
        int? loteSinLote = sinLote.Count > 0 ? idsLote[^1] : null;
        if (loteSinLote is int ls) Mapear(TiposErp.LoteN43, "SIN-LOTE", ls);
        estLotes.Cargadas = lotes.Count;

        // Extractos.
        var estCab = Est(Fase5, "N43Cabeceras");
        estCab.Leidas = o.Tabla("N43Cabeceras").Count;
        var ordinal = new Dictionary<int, int>();
        var extractos = new List<(Fila Erp, object?[] Fila)>();
        foreach (var c in cabeceras.OrderBy(c => c.E("IdRegistro")))
        {
            var lote = c.E("IdLote") is int l && loteDe.TryGetValue(l, out var il) ? il : loteSinLote!.Value;
            ordinal[lote] = ordinal.GetValueOrDefault(lote) + 1;
            var coord = (c.T("Entidad") ?? "", c.T("Oficina") ?? "", c.T("Cuenta") ?? "");
            int? cuenta = cuentas.TryGetValue(coord, out var ic) ? ic : null;
            if (cuenta is null) ClaveNula(Fase5, "N43Cabeceras", Txt(c.E("IdRegistro")), "Cuenta", $"{coord.Item1} {coord.Item2} {coord.Item3}", $"La cuenta {coord.Item1} {coord.Item2} {coord.Item3} del extracto {c.E("IdRegistro")} no está en N43EntidadesBancarias.");
            var lineas = (lineasErp.GetValueOrDefault(c.E("IdRegistro") ?? -1) ?? []).Where(x => !x.B("Eliminar")).ToList();
            var debe = lineas.Where(x => x.T("DebeHaber") == "1").Sum(ImporteLinea);
            var haber = lineas.Where(x => x.T("DebeHaber") == "2").Sum(ImporteLinea);
            var inicial = (ReglasErp.ImporteN43(c.T("SaldoInicial")) ?? 0m) * (c.T("DebeHaber") == "1" ? -1 : 1);
            var final = ReglasErp.ImporteN43(c.T("SaldoFinal")) ?? 0m;
            var desde = c.F("FechaInicioReal") ?? ReglasErp.FechaN43(c.T("FechaInicio")) ?? lineas.Select(LineaFecha).Min() ?? ahora.Date;
            var hasta = c.F("FechaFinReal") ?? ReglasErp.FechaN43(c.T("FechaFin")) ?? desde;
            extractos.Add((c, new object?[]
            {
                lote, ordinal[lote], cuenta, coord.Item1, coord.Item2, coord.Item3, c.T("Divisa") == "978" || c.T("Divisa") is null ? "EUR" : c.T("Divisa"), desde, hasta,
                c.T("DebeHaber") == "1" ? "D" : "H", Math.Abs(inicial), c.T("Modalidad"), ReglasErp.Recortar(c.T("Nombre"), 255),
                int.TryParse(c.T("NumApuntesDebe"), out var nd) ? nd : 0, c.Dec("TotalImporteDebeEuros"), int.TryParse(c.T("NumApuntesHaber"), out var nh) ? nh : 0, c.Dec("TotalImporteHaberEuros"),
                lineas.Count(x => x.T("DebeHaber") == "1"), debe, lineas.Count(x => x.T("DebeHaber") == "2"), haber, c.T("CodigoSeguridad"), final,
                Math.Abs(debe - c.Dec("TotalImporteDebeEuros")) < 0.005m && Math.Abs(haber - c.Dec("TotalImporteHaberEuros")) < 0.005m ? "CUADRADO" : "DESCUADRADO",
                $"(ERP N43Cabeceras {c.E("IdRegistro")})", $"(ERP N43Cabeceras {c.E("IdRegistro")})",
            }));
        }
        var idsExt = await MasivaAsync("ExtractosBancarios",
        [
            "IdLoteImportacionBancaria", "OrdinalExtracto", "IdCuentaBancaria", "Entidad", "Oficina", "Cuenta", "Divisa", "FechaInicio", "FechaFin", "SentidoSaldoInicial",
            "SaldoInicial", "Modalidad", "Nombre", "NumeroApuntesDebeDeclarado", "TotalDebeDeclarado", "NumeroApuntesHaberDeclarado", "TotalHaberDeclarado",
            "NumeroApuntesDebeCalculado", "TotalDebeCalculado", "NumeroApuntesHaberCalculado", "TotalHaberCalculado", "CodigoSeguridad", "SaldoFinal", "EstadoCuadre",
            "TextoCabeceraOriginal", "TextoCierreOriginal",
        ], extractos.Select(e => e.Fila).ToList(), "IdExtractoBancario");
        for (var i = 0; i < idsExt.Count; i++) Mapear(TiposErp.ExtractoN43, Txt(extractos[i].Erp.E("IdRegistro")), idsExt[i]);
        estCab.Cargadas = idsExt.Count;
        foreach (var (e, _) in extractos.Where(x => (string)x.Fila[23]! == "DESCUADRADO"))
            Anotar(Fase5, "N43Cabeceras", Txt(e.E("IdRegistro")), "TotalImporteDebeEuros", "Erp.ExtractoDescuadrado", Severidad.Informativa,
                $"Las líneas del extracto {e.E("IdRegistro")} no suman lo que declara su cabecera.");

        // Líneas, con su imputación (cuenta asignada y palabra clave) y su asiento.
        var estLin = Est(Fase5, "N43Lineas");
        estLin.Leidas = o.Tabla("N43Lineas").Count;
        var lineasPrep = new List<(Fila Erp, object?[] Fila, int Extracto)>();
        for (var i = 0; i < extractos.Count; i++)
        {
            var cab = extractos[i].Erp;
            var orden = 0;
            foreach (var l in lineasErp.GetValueOrDefault(cab.E("IdRegistro") ?? -1) ?? [])
            {
                if (l.B("Eliminar")) { Excluir(Fase5, "N43Lineas", Txt(l.E("NumLinea")), $"La línea {l.E("NumLinea")} está marcada para eliminar en el ERP: no se migra."); continue; }
                var fecha = LineaFecha(l) ?? (DateTime)extractos[i].Fila[7]!;
                lineasPrep.Add((l, new object?[]
                {
                    idsExt[i], ++orden, l.E("NumLinea") ?? 0, l.T("Oficina"), fecha, ReglasErp.FechaN43(l.T("FechaValor")) ?? fecha, l.T("ConceptoComun"), l.T("ConceptoPropio"),
                    l.T("DebeHaber") == "1" ? "D" : "H", ImporteLinea(l), l.T("Documento"), l.T("Referencia1"), l.T("Referencia2"), ReglasErp.Recortar(l.T("ConceptoCompleto"), 255),
                    l.T("EntidadOficina"), $"(ERP N43Lineas {l.E("NumLinea")})",
                }, idsExt[i]));
            }
        }
        foreach (var huerfana in o.Tabla("N43Lineas").Where(l => !cabeceras.Any(c => c.E("IdRegistro") == l.E("IdRegistro"))))
            Excluir(Fase5, "N43Lineas", Txt(huerfana.E("NumLinea")), $"La línea {huerfana.E("NumLinea")} es de un extracto que no se migra.");
        var idsLin = await MasivaAsync("LineasExtractoBancario",
        [
            "IdExtractoBancario", "OrdinalMovimiento", "NumeroLineaFisica", "OficinaOrigen", "FechaOperacion", "FechaValor", "ConceptoComun", "ConceptoPropio", "Sentido", "Importe",
            "Documento", "Referencia1", "Referencia2", "ConceptoCompleto", "EntidadOficinaDestino", "TextoOriginal",
        ], lineasPrep.Select(l => l.Fila).ToList(), "IdLineaExtractoBancario");
        estLin.Cargadas = idsLin.Count;

        var imputaciones = new List<object?[]>();
        var contabilizaciones = new List<object?[]>();
        var estImp = Est(Fase5, "N43Lineas → imputaciones");
        var estAsi = Est(Fase5, "N43Lineas → asientos");
        for (var i = 0; i < idsLin.Count; i++)
        {
            var l = lineasPrep[i].Erp;
            var num = Txt(l.E("NumLinea"));
            Mapear(TiposErp.LineaN43, num, idsLin[i]);
            var importe = ImporteLinea(l);
            if (l.T("CuentaContable") is { } cuenta)
            {
                int? palabra = l.E("IdPalabraClave") is int ip and > 0 ? Mapa(TiposErp.PalabraN43, Txt(ip)) : null;
                if (l.E("IdPalabraClave") is int ip2 and > 0 && palabra is null)
                    ClaveNula(Fase5, "N43Lineas", num, "IdPalabraClave", ip2, $"La palabra clave {ip2} de la línea {num} no existe.");
                if (Identidad(cuenta) is int identidad)
                {
                    imputaciones.Add([idsLin[i], identidad, "CONTRAPARTIDA", l.T("DebeHaber") == "1" ? importe : -importe, palabra is null ? "ERP" : "PALABRA_CLAVE", null, "CONFIRMADA",
                        ReglasErp.Recortar(l.T("ConceptoCompleto"), 255), null, palabra]);
                    estImp.Cargadas++;
                }
                else ClaveNula(Fase5, "N43Lineas", num, "CuentaContable", cuenta, $"La cuenta {cuenta} de la línea {num} no está en el plan contable importado: queda sin imputación.");
            }
            var anio = l.E("Ejercicio") ?? l.F("FECAPU")?.Year ?? (lineasPrep[i].Fila[4] as DateTime?)?.Year ?? 0;
            if (l.E("ASIAPU") is int asi and > 0)
            {
                if (AsientoCs(anio, l.E("DIAAPU"), asi) is int idAsiento)
                {
                    contabilizaciones.Add([idsLin[i], idAsiento, "CONTABILIZADA", 1, l.F("FechaExportacion") ?? l.F("FECAPU") ?? ahora, "ERP"]);
                    estAsi.Cargadas++;
                }
                else ClaveNula(Fase5, "N43Lineas", num, "ASIAPU", $"{anio} {l.E("DIAAPU") ?? 1}:{asi}", $"El asiento {asi} de {anio} de la línea bancaria {num} no está en lo importado de ContaSol.");
            }
        }
        await MasivaAsync("ImputacionesLineaExtractoBancario",
            ["IdLineaExtractoBancario", "IdIdentidadContable", "Rol", "ImporteFirmado", "Origen", "Confianza", "Estado", "Concepto", "IdCentroCoste", "IdPalabraClaveBancaria"], imputaciones);
        await MasivaAsync("ContabilizacionesLineaExtracto", ["IdLineaExtractoBancario", "IdAsiento", "Estado", "VersionProceso", "FechaHora", "Operador"], contabilizaciones);

        // FacturasCompra.LineaN43 → la línea bancaria nueva.
        var compras = o.Tabla("FacturasCompra").Where(c => c.E("LineaN43") is int ln and > 0).ToList();
        await EnLotesAsync(compras, async (s, c) =>
        {
            if (Mapa(TiposErp.Compra, Txt(c.E("IdRegistro"))) is not int idCompra) return;
            if (Mapa(TiposErp.LineaN43, Txt(c.E("LineaN43"))) is int idLinea)
                await s.EjecutarAsync("UPDATE FacturasCompra SET LineaN43 = ? WHERE IdRegistro = ?", idLinea, idCompra);
            else ClaveNula(Fase5, "FacturasCompra", Txt(c.E("IdRegistro")), "LineaN43", c.E("LineaN43"), $"La línea bancaria {c.E("LineaN43")} de la factura de compra {c.E("IdRegistro")} no se ha migrado.");
        });
    }

    private static decimal ImporteLinea(Fila l) => l.D("ImporteEuros") ?? ReglasErp.ImporteN43(l.T("Importe")) ?? 0m;

    /// <summary>
    /// Fecha de operación de la línea: la del registro 22 (FechaOperacion, AAMMDD). El campo Fecha del ERP es la fecha valor
    /// (en las 641 líneas de la copia del ERP con fechas distintas coincide siempre con FechaValor), así que solo se usa si falta.
    /// </summary>
    private static DateTime? LineaFecha(Fila l) => ReglasErp.FechaN43(l.T("FechaOperacion")) ?? l.F("Fecha");

    /// <summary>Cuentas de origen: N43EntidadesBancarias → CuentasBancarias y su configuración contable (572, 555 puente y 555 split).</summary>
    private async Task<Dictionary<(string, string, string), int>> CuentasBancariasAsync()
    {
        var est = Est(Fase5, "N43EntidadesBancarias");
        var r = new Dictionary<(string, string, string), int>();
        var existentes = (await ConsultarAsync("SELECT IdCuentaBancaria, EntidadOriginal, Oficina, Cuenta FROM CuentasBancarias WHERE IdSociedad = ?",
            f => (Id: f.Entero("IdCuentaBancaria"), Clave: ((f.Texto("EntidadOriginal") ?? "").Trim(), (f.Texto("Oficina") ?? "").Trim(), (f.Texto("Cuenta") ?? "").Trim())), Sociedad))
            .GroupBy(x => x.Clave).ToDictionary(g => g.Key, g => g.First().Id);
        foreach (var e in o.Tabla("N43EntidadesBancarias"))
        {
            est.Leidas++;
            var clave = (e.T("Entidad") ?? "", e.T("Oficina") ?? "", e.T("Cuenta") ?? "");
            var texto = $"{clave.Item1} {clave.Item2} {clave.Item3}";
            if (e.T("CodigoEmpresa") != Sociedad) { Excluir(Fase5, "N43EntidadesBancarias", texto, $"La cuenta {texto} es de la empresa {e.T("CodigoEmpresa")}."); continue; }
            if (!existentes.TryGetValue(clave, out var id))
                existentes[clave] = id = await InsertarAsync("INSERT INTO CuentasBancarias (IdSociedad, IdEntidadFinanciera, EntidadOriginal, Oficina, Cuenta, Iban, Divisa, Nombre, Activa) VALUES (?, ?, ?, ?, ?, ?, ?, ?, True)",
                    Sociedad, null, clave.Item1, clave.Item2, clave.Item3, null, e.T("Divisa") is null or "978" ? "EUR" : e.T("Divisa"), ReglasErp.Recortar(e.T("Nombre"), 100));
            else await EjecutarAsync("UPDATE CuentasBancarias SET Nombre = ? WHERE IdCuentaBancaria = ?", ReglasErp.Recortar(e.T("Nombre"), 100), id);
            r[clave] = id;
            Mapear(TiposErp.CuentaBancaria, texto, id);
            est.Cargadas++;
            if (Identidad(e.T("CuentaContable")) is not int banco)
            {
                ClaveNula(Fase5, "N43EntidadesBancarias", texto, "CuentaContable", e.T("CuentaContable"), $"La cuenta {texto} no tiene una 572 del plan contable importado: queda sin configuración contable.");
                continue;
            }
            var puente = Identidad(e.T("Cuenta555Temporal"));
            var split = Identidad(e.T("Cuenta555Split"));
            if (e.T("Cuenta555Temporal") is { } p1 && puente is null) ClaveNula(Fase5, "N43EntidadesBancarias", texto, "Cuenta555Temporal", p1, $"La cuenta puente {p1} de {texto} no existe.");
            if (e.T("Cuenta555Split") is { } p2 && split is null) ClaveNula(Fase5, "N43EntidadesBancarias", texto, "Cuenta555Split", p2, $"La cuenta split {p2} de {texto} no existe.");
            if (await EjecutarAsync("UPDATE ConfiguracionesContablesCuentaBancaria SET IdIdentidadContableBanco = ?, IdIdentidadCuentaPuente = ?, IdIdentidadSplit = ? WHERE IdCuentaBancaria = ? AND FechaHasta IS NULL",
                    banco, puente, split, id) == 0)
                await EjecutarAsync("INSERT INTO ConfiguracionesContablesCuentaBancaria (IdCuentaBancaria, IdIdentidadContableBanco, IdIdentidadCuentaPuente, IdIdentidadSplit, IdIdentidadGastosDivisa, FechaDesde) VALUES (?, ?, ?, ?, ?, ?)",
                    id, banco, puente, split, null, new DateTime(2000, 1, 1));
        }
        return r;
    }

    private async Task PalabrasClaveN43Async()
    {
        var est = Est(Fase5, "N43PalabrasClave");
        var filas = new List<(Fila Erp, object?[] Fila)>();
        foreach (var p in o.Tabla("N43PalabrasClave"))
        {
            est.Leidas++;
            var clave = Txt(p.E("IdPalabraClave"));
            if (p.T("CodigoEmpresa") != Sociedad) { Excluir(Fase5, "N43PalabrasClave", clave, $"La palabra clave {clave} es de la empresa {p.T("CodigoEmpresa")}."); continue; }
            if (ReglasErp.Recortar(p.T("PalabraClave"), 255) is not { } palabra) { Excluir(Fase5, "N43PalabrasClave", clave, $"La palabra clave {clave} está vacía."); continue; }
            var identidad = Identidad(p.T("CuentaContable"));
            if (p.T("CuentaContable") is { } cc && identidad is null) ClaveNula(Fase5, "N43PalabrasClave", clave, "CuentaContable", cc, $"La cuenta {cc} de la palabra clave «{palabra}» no existe.");
            filas.Add((p, new object?[] { Sociedad, ReglasErp.Recortar(p.T("EntidadBancaria"), 4), p.T("DebeHaber")?[..1], palabra, ReglasErp.Recortar(p.T("PalabraClave2"), 255), identidad, p.E("Especial"), true }));
        }
        var ids = await MasivaAsync("PalabrasClaveBancarias", ["IdSociedad", "Entidad", "Sentido", "PalabraClave", "PalabraClave2", "IdIdentidadContable", "Especial", "Activa"],
            filas.Select(f => f.Fila).ToList(), "IdPalabraClaveBancaria");
        for (var i = 0; i < ids.Count; i++) Mapear(TiposErp.PalabraN43, Txt(filas[i].Erp.E("IdPalabraClave")), ids[i]);
        est.Cargadas = ids.Count;
    }
}
