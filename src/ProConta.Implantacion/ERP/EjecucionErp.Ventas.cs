using System.Globalization;
using ProConta.Contabilidad.CentrosCoste;
using ProConta.Facturacion.Conceptos;
using ProConta.Facturacion.Facturas;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Importes;

namespace ProConta.Implantacion.ERP;

// Fase 2: actividades (CategoriasIVA), conceptos agrupados y facturas de venta validadas con sus líneas, totales recalculados,
// asiento (DOCAPU), registro del libro (F_IVR.FACIVR) y reparto del libro por la 477 de cada actividad.
internal sealed partial class EjecucionErp
{
    private const string Fase2 = "2 ventas";
    private readonly Dictionary<int, int> _series = [];
    private readonly Dictionary<int, (decimal Total, int Numero)> _ventasErp = [];

    private async Task VentasAsync()
    {
        await ActividadesAsync();
        await ConceptosAsync();
        await FacturasVentaAsync();
    }

    // ---------- actividades ----------

    private static readonly Dictionary<int, (string Codigo, string? Serie)> CodigosActividad = new()
    {
        [1] = ("GENERAL", null), [2] = ("TINNOVA", "4725"), [3] = ("VIVIENDAS", "4726"), [4] = ("LOCALES", "4727"), [5] = ("TURISTICO", "4728"),
    };

    private async Task ActividadesAsync()
    {
        var est = Est(Fase2, "CategoriasIVA");
        var existentes = (await ConsultarAsync("SELECT IdActividad, Codigo FROM Actividades WHERE IdSociedad = ?", f => (Id: f.Entero("IdActividad"), Codigo: (f.Texto("Codigo") ?? "").Trim()), Sociedad))
            .ToDictionary(x => x.Codigo, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var porCategoria = new Dictionary<int, int>();
        async Task<int> Grabar(string codigo, string nombre, byte deducible, string? serie)
        {
            if (existentes.TryGetValue(codigo, out var id))
                await EjecutarAsync("UPDATE Actividades SET Nombre = ?, Deducible = ?, SerieCuentaIva = ?, Activa = True WHERE IdActividad = ?", nombre, deducible, serie, id);
            else existentes[codigo] = id = await InsertarAsync("INSERT INTO Actividades (IdSociedad, Codigo, Nombre, Deducible, SerieCuentaIva, Activa) VALUES (?, ?, ?, ?, ?, True)",
                Sociedad, codigo, nombre, deducible, serie);
            return id;
        }
        foreach (var c in o.Tabla("CategoriasIVA"))
        {
            est.Leidas++;
            var id = c.E("IdCategoria") ?? 0;
            var (codigo, serie) = CodigosActividad.TryGetValue(id, out var x) ? x : ($"CAT{id}", null);
            // T-Innova (contabilidad/procesos 2026-11-08) y Alquiler Locales (propietario 2026-11-11 17:19) son deducibles al 100 % aunque el ERP diga 1.
            var deducible = id is 2 or 4 ? (byte)2 : (byte)Math.Clamp(c.E("Deducible") ?? 2, 0, 2);
            var idActividad = await Grabar(codigo, ReglasErp.Recortar(c.T("Descripcion"), 100) ?? codigo, deducible, serie);
            porCategoria[id] = idActividad;
            Mapear(TiposErp.Actividad, Txt(id), idActividad);
            est.Cargadas++;
        }
        var inversion = await Grabar("INVERSION", "Inversión financiera", 1, "4722");
        Mapear(TiposErp.Actividad, "INVERSION", inversion);
        Anotar(Fase2, "CategoriasIVA", "INVERSION", null, "Erp.ActividadNueva", Severidad.Informativa, "He creado la actividad «Inversión financiera» (prorrata, serie 4722) para los centros 4/401 a 4/418.");

        var estCentros = Est(Fase2, "CategoriasIVAPorDepartamento");
        foreach (var d in o.Tabla("CategoriasIVAPorDepartamento"))
        {
            estCentros.Leidas++;
            var clave = ReglasErp.ClaveCentro(Txt(d.E("CODCDP")));
            if (Centro(clave) is not int centro) { Excluir(Fase2, "CategoriasIVAPorDepartamento", Txt(d.E("CODCDP")), $"El centro {clave} no está en ProContaVF."); continue; }
            if (d.E("IdCategoriaIVA") is not int cat || !porCategoria.TryGetValue(cat, out var act))
            {
                ClaveNula(Fase2, "CategoriasIVAPorDepartamento", Txt(d.E("CODCDP")), "IdCategoriaIVA", d.E("IdCategoriaIVA"), $"La categoría de IVA del centro {clave} no existe: queda sin actividad.");
                continue;
            }
            await EjecutarAsync("UPDATE CentrosCoste SET IdActividad = ? WHERE IdCentroCoste = ?", act, centro);
            estCentros.Cargadas++;
        }
        foreach (var (ruta, centro) in _centros.Where(c => c.Key.StartsWith("4/", StringComparison.Ordinal)
                     && int.TryParse(c.Key[2..], out var n) && n is >= 401 and <= 418))
            await EjecutarAsync("UPDATE CentrosCoste SET IdActividad = ? WHERE IdCentroCoste = ?", inversion, centro);
    }

    // ---------- conceptos ----------

    private async Task ConceptosAsync()
    {
        var ctx = Contexto(UltimoEjercicio)!;
        var est = Est(Fase2, "ConceptosCaja");
        var estTipos = Est(Fase2, "TiposConceptosCaja");
        var activos = o.Tabla("ConceptosCaja").Where(c => c.B("Activo")).ToList();
        est.Leidas = o.Tabla("ConceptosCaja").Count;
        est.Excluidas += est.Leidas - activos.Count;
        // 413 y 4752 son obsoletos (propietario 2026-11-11 17:19): no pasan al catálogo; las líneas que los usan conservan su cuenta y su centro.
        foreach (var c in activos.Where(c => c.E("Id") is int id && ReglasErp.ConceptosObsoletos.Contains(id)).ToList())
        {
            activos.Remove(c);
            var lineas = o.Tabla("LineasFacturaVenta").Count(l => l.E("IdConcepto") == c.E("Id"));
            Excluir(Fase2, "ConceptosCaja", Txt(c.E("Id")), $"El concepto {c.E("Id")} «{c.T("Descripcion")}» es obsoleto: no se migra al catálogo. Sus {lineas} líneas de factura se migran sin concepto, con su cuenta y su centro de coste.", Severidad.Informativa);
        }
        Anotar(Fase2, "ConceptosCaja", null, "Activo", "Erp.Excluido", Severidad.Informativa, $"{est.Leidas - activos.Count} conceptos inactivos del ERP no se migran (solo los activos).");

        // Tipos con algún concepto activo.
        var tipos = (await ConsultarAsync("SELECT IdTipoConcepto, Descripcion FROM TiposConcepto", f => (Id: f.Entero("IdTipoConcepto"), Descripcion: (f.Texto("Descripcion") ?? "").Trim())))
            .ToDictionary(x => x.Descripcion, x => x.Id, StringComparer.OrdinalIgnoreCase);
        var tipoDe = new Dictionary<int, int>();
        foreach (var t in o.Tabla("TiposConceptosCaja"))
        {
            estTipos.Leidas++;
            var codigo = t.E("Codigo") ?? -1;
            if (!activos.Any(c => c.E("Tipo") == codigo)) { estTipos.Excluidas++; continue; }
            var desc = ReglasErp.Recortar(t.T("Descripcion"), 100) ?? $"Tipo {codigo}";
            if (!tipos.TryGetValue(desc, out var id))
            {
                id = Convert.ToInt32(await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT MAX(IdTipoConcepto) FROM TiposConcepto")) is { } v and not DBNull ? v : 0, CultureInfo.InvariantCulture) + 1;
                await EjecutarAsync("INSERT INTO TiposConcepto (IdTipoConcepto, Descripcion, Activo) VALUES (?, ?, True)", id, desc);
                tipos[desc] = id;
            }
            tipoDe[codigo] = id;
            Mapear(TiposErp.TipoConcepto, Txt(codigo), id);
            estTipos.Cargadas++;
        }
        Anotar(Fase2, "TiposConceptosCaja", null, null, "Erp.Excluido", Severidad.Informativa, $"{estTipos.Excluidas} tipos de concepto sin conceptos activos no se migran.");

        var porNombre = (await ConsultarAsync("SELECT IdConcepto, Nombre FROM Conceptos WHERE IdSociedad = ?", f => (Id: f.Entero("IdConcepto"), Nombre: (f.Texto("Nombre") ?? "").Trim()), Sociedad))
            .GroupBy(x => x.Nombre, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        async Task<int?> Guardar(string claveMapa, string nombre, string? descripcion, int? tipo, string? cuenta, int? centro, decimal precio, decimal? iva, string notas)
        {
            var previo = Anterior(TiposErp.Concepto, claveMapa) ?? (porNombre.TryGetValue(nombre, out var mismo) ? mismo : 0);
            var concepto = new Concepto(previo, Sociedad, tipo, nombre, descripcion, null, null, null, false, null, centro, precio, iva, true, notas);
            var r = await m.Conceptos.GuardarAsync(concepto, cuenta, ctx);
            if (!r.EsCorrecto && cuenta is not null)
            {
                Anotar(Fase2, "ConceptosCaja", claveMapa, "IdAgrupacion", "Erp.ConceptoSinCuenta", Severidad.Advertencia,
                    $"El concepto «{nombre}» va a la cuenta {cuenta}, que no admite apuntes en {ctx.Ejercicio}: lo dejo sin cuenta ({r.Error!.Mensaje}).");
                r = await m.Conceptos.GuardarAsync(concepto, null, ctx);
            }
            if (!r.EsCorrecto) { Anotar(Fase2, "ConceptosCaja", claveMapa, null, "Erp.ConceptoNoGuardado", Severidad.Bloqueante, r.Error!.TextoOperador()); return null; }
            porNombre[nombre] = r.Valor.IdConcepto;
            return r.Valor.IdConcepto;
        }

        // Agrupados: solo difieren por departamento; cuenta y centro van en la programada (facturacion/conceptos).
        foreach (var grupo in activos.GroupBy(c => ReglasErp.Agrupacion(c.E("Id") ?? 0, c.E("Tipo"), c.E("CodigoTipoIVA"))).Where(g => g.Key is not null))
        {
            var primero = grupo.First();
            var id = await Guardar($"GRUPO:{grupo.Key}", grupo.Key!, null, primero.E("Tipo") is int t && tipoDe.TryGetValue(t, out var tc) ? tc : null, null, null, 0m,
                grupo.Key == "Retención IRPF" ? null : ReglasErp.TipoIvaConcepto(primero.E("CodigoTipoIVA")),
                $"Agrupa los conceptos del ERP {string.Join(", ", grupo.Select(c => c.E("Id")))}; la cuenta y el centro de coste van en cada factura o programada.");
            if (id is null) continue;
            Mapear(TiposErp.Concepto, $"GRUPO:{grupo.Key}", id.Value);
            foreach (var c in grupo) { Mapear(TiposErp.Concepto, Txt(c.E("Id")), id.Value); est.Cargadas++; }
        }
        foreach (var c in activos.Where(c => ReglasErp.Agrupacion(c.E("Id") ?? 0, c.E("Tipo"), c.E("CodigoTipoIVA")) is null))
        {
            var idErp = c.E("Id")!.Value;
            var nombre = ReglasErp.Recortar(c.T("Descripcion"), 255) ?? $"Concepto {idErp}";
            if (porNombre.ContainsKey(nombre) && Anterior(TiposErp.Concepto, Txt(idErp)) is null && activos.Count(x => x.T("Descripcion") == c.T("Descripcion")) > 1)
                nombre = $"{nombre} ({idErp})";
            var cuenta = CuentaConcepto(idErp);
            var id = await Guardar(Txt(idErp), nombre, null, c.E("Tipo") is int t && tipoDe.TryGetValue(t, out var tc) ? tc : null,
                ReglasErp.EsCuentaRetencion(cuenta) ? null : cuenta, Centro(ReglasErp.ClaveCentro(DepartamentoConcepto(idErp))),
                c.Dec("Importe"), ReglasErp.TipoIvaConcepto(c.E("CodigoTipoIVA")), $"Concepto {idErp} del ERP.");
            if (id is null) continue;
            Mapear(TiposErp.Concepto, Txt(idErp), id.Value);
            est.Cargadas++;
        }
    }

    // ---------- facturas de venta ----------

    private sealed record FacturaPreparada(Fila Erp, int Numero, int Anio, FacturaVenta Factura, int? IdAsiento, DateTime? FechaAsiento, int? IdRegistroIva, string Rectificada);

    private async Task FacturasVentaAsync()
    {
        var est = Est(Fase2, "FacturasVenta");
        var estLineas = Est(Fase2, "LineasFacturaVenta");
        var lineasPorFactura = o.Tabla("LineasFacturaVenta").GroupBy(l => l.E("Id") ?? -1).ToDictionary(g => g.Key, g => g.OrderBy(l => l.E("NumLinea") ?? 0).ToList());
        var direcciones = o.Direcciones.GroupBy(d => d.IdPersona).ToDictionary(g => g.Key, g => g.First());
        var emisor = (await ConsultarAsync("SELECT Nombre, CIF, DireccionFiscal, CodigoPostalFiscal, PoblacionFiscal, ProvinciaFiscal FROM Sociedades WHERE IdSociedad = ?",
            f => (Nombre: f.Texto("Nombre"), Cif: f.Texto("CIF"), Dir: f.Texto("DireccionFiscal"), Cp: f.Texto("CodigoPostalFiscal"), Pob: f.Texto("PoblacionFiscal"), Prov: f.Texto("ProvinciaFiscal")), Sociedad)).First();
        // El asiento de la factura es el que tiene el cliente (43) y además ingresos o IVA (7…/477…) con su número; el resto (cobros) no.
        var apuntes = o.ApuntesCliente.GroupBy(a => (a.Ejercicio, a.Documento)).ToDictionary(g => g.Key, g =>
        {
            var cliente = g.Where(a => a.EsCliente).Select(a => (a.Diario, a.Asiento, a.Fecha)).Distinct().ToList();
            var conIngreso = g.Where(a => !a.EsCliente).Select(a => (a.Diario, a.Asiento)).ToHashSet();
            var factura = cliente.Where(a => conIngreso.Contains((a.Diario, a.Asiento))).OrderBy(a => a.Asiento).ToList();
            return factura.Count > 0 ? factura : cliente.OrderBy(a => a.Asiento).ToList();
        });
        var registros = o.RegistrosRepercutido.Where(r => r.Factura is not null).GroupBy(r => (r.Ejercicio, r.Factura!)).ToDictionary(g => g.Key, g => g.ToList());

        var preparadas = new List<FacturaPreparada>();
        est.Leidas = o.Tabla("FacturasVenta").Count;
        estLineas.Leidas = o.Tabla("LineasFacturaVenta").Count;
        foreach (var fv in o.Tabla("FacturasVenta").OrderBy(f => f.E("Id")))
        {
            var numero = fv.E("Id") ?? 0;
            var clave = Txt(numero);
            var lineasErp = lineasPorFactura.GetValueOrDefault(numero) ?? [];
            if (numero == ReglasErp.FacturaFicticia) { Excluir(Fase2, "FacturasVenta", clave, "La 83000 es la factura ficticia que marca el inicio de la numeración de 2026: no se migra.", Severidad.Informativa); estLineas.Excluidas += lineasErp.Count; continue; }
            if (fv.F("Fecha") is not DateTime fecha) { Excluir(Fase2, "FacturasVenta", clave, $"La factura {numero} («{fv.T("Descripcion")}», {fv.Dec("ImporteTotal"):0.00} €) no tiene fecha: no sé a qué ejercicio va."); estLineas.Excluidas += lineasErp.Count; continue; }
            var anio = fecha.Year;
            var ctx = Contexto(anio);
            if (ctx is null) { Excluir(Fase2, "FacturasVenta", clave, $"El ejercicio {anio} de la factura {numero} no existe."); estLineas.Excluidas += lineasErp.Count; continue; }
            _ventasErp[numero] = (fv.Dec("ImporteTotal"), numero);

            var f = new FacturaVenta { FechaFactura = fecha, FechaInicioValidez = fv.F("FechaInicio"), FechaFinValidez = fv.F("FechaFin") };
            // Cliente (pagador) y beneficiario (quien recibe el servicio).
            int? idCliente = null;
            if (fv.E("IdCliente") is int pc)
            {
                idCliente = Mapa(TiposErp.Cliente, Txt(pc));
                if (idCliente is null) ClaveNula(Fase2, "FacturasVenta", clave, "IdCliente", pc, $"El cliente {pc} de la factura {numero} no está en Personas: la factura queda sin cliente (conserva el NIF y el nombre).");
            }
            f.IdCliente = idCliente;
            // IdBeneficiario no se migra: el inmueble se sabe por el centro de coste de la primera línea (propietario 2026-11-11 17:19).

            // Líneas: el centro de cada una sale de su concepto; la que no tiene, el de la primera línea de la factura que lo tenga.
            var transformadas = new List<LineaTransformada>();
            int? centroFactura = lineasErp.Select(l => l.E("IdConcepto") is int c ? Centro(ReglasErp.ClaveCentro(DepartamentoConcepto(c))) : null).FirstOrDefault(c => c is not null);
            foreach (var l in lineasErp)
            {
                var idConcepto = l.E("IdConcepto");
                var concepto = idConcepto is int ic && _conceptosErp.ContainsKey(ic) ? new ConceptoLineaErp(CuentaConcepto(ic), DepartamentoConcepto(ic)) : null;
                if (idConcepto is int ic2 && !_conceptosErp.ContainsKey(ic2))
                    ClaveNula(Fase2, "LineasFacturaVenta", $"{numero}:{l.E("NumLinea")}", "IdConcepto", ic2, $"El concepto {ic2} de la línea {l.E("NumLinea")} de la factura {numero} no existe: la línea va sin cuenta ni centro.");
                var centro = concepto is null ? null : Centro(ReglasErp.ClaveCentro(concepto.Departamento));
                var erp = LineaErp(l);
                var t = ReglasErp.Transformar(erp, concepto, centro ?? centroFactura);
                if (t.Linea is { } ln && !erp.EsCabecera)
                {
                    if (erp.DescuentoPorcentual != 0m || erp.DescuentoAbsoluto != 0m)
                        ln.NotasInternas = ReglasErp.UnirNotas(ln.NotasInternas, $"Descuento informativo del ERP (ya aplicado en el precio): {erp.DescuentoPorcentual:0.##} % / {erp.DescuentoAbsoluto:0.00} €.");
                }
                transformadas.Add(t);
            }
            var retenciones = transformadas.Where(t => t.TipoRetencion is not null).ToList();
            if (retenciones.Select(r => r.TipoRetencion).Distinct().Count() > 1)
                Anotar(Fase2, "LineasFacturaVenta", clave, "Cantidad", "Erp.RetencionesDistintas", Severidad.Advertencia, $"La factura {numero} tiene retenciones de distinto porcentaje: uso la primera.");
            f.TipoRetencion = retenciones.FirstOrDefault()?.TipoRetencion ?? 0m;
            foreach (var t in transformadas.Where(t => t.Linea is not null)) f.AnadirLinea(t.Linea!);
            estLineas.Cargadas += f.Lineas.Count;
            if (retenciones.Count > 0) Est(Fase2, "LineasFacturaVenta (retención → TipoRetencion)").Cargadas += retenciones.Count;

            // Totales recalculados desde las líneas (M-12); se anota donde no coinciden con el ERP.
            var totales = f.Totales;
            var recalculado = ReglasErp.TotalLineas(lineasErp.Select(LineaErp));
            var totalErp = Importe.RedondearValor(fv.Dec("ImporteTotal"));
            var sinRedondear = Importe.RedondearValor(lineasErp.Select(LineaErp).Where(l => !l.EsCabecera).Sum(l => l.Cantidad * l.Precio));
            if (totales.ImporteTotal.Valor != totalErp && sinRedondear == totalErp && Math.Abs(totales.ImporteTotal.Valor - totalErp) <= 0.05m)
                Anotar(Fase2, "FacturasVenta", clave, "ImporteTotal", "Erp.TotalRedondeo", Severidad.Informativa,
                    $"La factura {numero} ({fecha:dd/MM/yyyy}) suma {totales.ImporteTotal.Valor:0.00} € redondeando cada línea y el ERP dice {totalErp:0.00} € (redondea solo el total): " +
                    "queda con el importe de las líneas; su asiento importado de ContaSol puede diferir en ese céntimo.", fv.Dec("ImporteTotal").ToString("0.0000", CultureInfo.InvariantCulture));
            else if (totales.ImporteTotal.Valor != totalErp)
                Anotar(Fase2, "FacturasVenta", clave, "ImporteTotal", "Erp.TotalDistinto", Severidad.Advertencia,
                    $"La factura {numero} ({fecha:dd/MM/yyyy}) suma {totales.ImporteTotal.Valor:0.00} € desde sus líneas y el ERP dice {fv.Dec("ImporteTotal"):0.00} € " +
                    $"(Σ Cantidad × Precio = {recalculado:0.00}): queda con el importe de las líneas.", fv.Dec("ImporteTotal").ToString("0.00", CultureInfo.InvariantCulture));
            if (retenciones.Count > 0 && Math.Abs(totales.Retencion.Valor - retenciones.Sum(r => r.ImporteRetencion)) > 0m)
                Anotar(Fase2, "LineasFacturaVenta", clave, "Precio", "Erp.RetencionRedondeo", Severidad.Informativa,
                    $"La retención de la factura {numero} es {retenciones.Sum(r => r.ImporteRetencion):0.00} € en el ERP y {totales.Retencion.Valor:0.00} € con el {f.TipoRetencion:0.##} % sobre la base.");

            f.Descripcion = ReglasErp.Recortar(fv.T("Descripcion"), 255) ?? f.Lineas.Select(l => l.Descripcion).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? $"Factura {numero}";
            f.NotasInternas = ReglasErp.UnirNotas(fv.T("Notas"), fv.T("NotasInternas"));

            // Asiento (DOCAPU = número) y registro del libro (FACIVR = número) importados de ContaSol, salvo las aún no contabilizadas.
            int? idAsiento = null, idRegistro = null;
            DateTime? fechaAsiento = null;
            if (fecha < ReglasErp.InicioSinContabilizar)
            {
                if (apuntes.TryGetValue((anio, clave), out var asientos) && asientos.Count > 0)
                {
                    if (asientos.Count > 1)
                        Anotar(Fase2, "FacturasVenta", clave, "DOCAPU", "Erp.VariosAsientos", Severidad.Advertencia,
                            $"La factura {numero} aparece en {asientos.Count} asientos de cliente del ERP ({string.Join(", ", asientos.Select(a => $"{a.Diario}:{a.Asiento}"))}): la enlazo con el primero.");
                    var a = asientos[0];
                    idAsiento = AsientoCs(anio, a.Diario, a.Asiento);
                    fechaAsiento = a.Fecha;
                    if (idAsiento is null && _ejercicios.ContainsKey(anio) && MapaCs(anio, TiposMapa.Asiento).Count > 0)
                        ClaveNula(Fase2, "FacturasVenta", clave, "DOCAPU", $"{a.Diario}:{a.Asiento}", $"El asiento {a.Diario}:{a.Asiento} de la factura {numero} no está en lo importado de ContaSol de {anio}.");
                }
                if (registros.TryGetValue((anio, clave), out var regs))
                {
                    if (regs.Count > 1)
                        Anotar(Fase2, "FacturasVenta", clave, "FACIVR", "Erp.VariosRegistrosIva", Severidad.Advertencia, $"La factura {numero} tiene {regs.Count} registros en F_IVR {anio}: enlazo el primero.");
                    idRegistro = MapaCs(anio, TiposMapa.IvaRepercutido).TryGetValue(Txt(regs[0].Codigo), out var ir) ? ir : null;
                    if (idRegistro is null && MapaCs(anio, TiposMapa.IvaRepercutido).Count > 0)
                        ClaveNula(Fase2, "FacturasVenta", clave, "FACIVR", regs[0].Codigo, $"El registro {regs[0].Codigo} del libro de {anio} de la factura {numero} no está en lo importado de ContaSol.");
                }
            }
            var rectificada = fv.E("CodigoFacturaRectificada") is int cr and not 0 ? Txt(cr) : "";
            preparadas.Add(new FacturaPreparada(fv, numero, anio, f, idAsiento, fechaAsiento, idRegistro, rectificada));
        }

        // Series: una por ejercicio, sin prefijo, que continúa la numeración del ERP.
        foreach (var g in preparadas.GroupBy(p => p.Anio))
            _series[g.Key] = await SerieAsync(g.Key, g.Min(p => p.Numero), g.Max(p => p.Numero));
        await SerieRectificativaAsync(UltimoEjercicio);

        var ahora = Ahora;
        var filas = preparadas.Select(p =>
        {
            var f = p.Factura;
            var t = f.Totales;
            // Emisor histórico: la razón social que tenía la empresa ese año (la del F_EMP de ContaSol de ese ejercicio); el NIF, el de la empresa.
            var dir = p.Erp.E("IdCliente") is int pd && direcciones.TryGetValue(pd, out var d) ? d : null;
            var rect = p.Rectificada.Length > 0;
            return new object?[]
            {
                _ejercicios[p.Anio].Id, _series[p.Anio], Txt(p.Numero), f.IdCliente, ReglasErp.Recortar(p.Erp.T("NIF"), 20), ReglasErp.Recortar(p.Erp.T("NombreCliente"), 255),
                dir?.Pais?.ToUpperInvariant() is "ESPAÑA" or "ESPANA" ? "ES" : null,
                ReglasErp.Recortar(emisor.Cif, 20), ReglasErp.Recortar(ReglasErp.RazonSocialDelAnio(_razonesSociales, p.Anio) ?? emisor.Nombre, 255), emisor.Dir, emisor.Cp, emisor.Pob, emisor.Prov,
                f.FechaFactura, f.FechaInicioValidez, f.FechaFinValidez, f.FechaFactura, f.FechaFactura, f.Descripcion, f.NotasInternas,
                t.BaseImponible.Valor, t.Iva.Valor, t.ImporteBruto.Valor, (float)f.TipoRetencion, t.Retencion.Valor, t.ImporteTotal.Valor,
                rect ? "R4" : "F1", rect ? "I" : null, (byte)EstadoFactura.Validada, p.IdAsiento, p.IdAsiento is null ? null : p.FechaAsiento,
                $"{ahora:dd/MM/yyyy HH:mm} · {Operador} · Migrada del ERP (factura {p.Numero}).",
                ReglasErp.Recortar(dir?.Direccion, 255), ReglasErp.Recortar(dir?.CodigoPostal, 10), ReglasErp.Recortar(dir?.Poblacion, 100), ReglasErp.Recortar(dir?.Provincia, 100),
                true, 0, rect ? $"Rectificativa del ERP de la factura {p.Rectificada}." : null,
            };
        }).ToList();
        var ids = await MasivaAsync("Facturas",
        [
            "IdSociedadEjercicio", "IdSerie", "NumeroFactura", "IdCliente", "CIF", "Nombre", "PaisCliente", "NIFEmisor", "NombreEmisor", "DireccionEmisor", "CodigoPostalEmisor",
            "PoblacionEmisor", "ProvinciaEmisor", "FechaFactura", "FechaInicioValidez", "FechaFinValidez", "FechaCreacion", "FechaHoraEmision", "Descripcion", "NotasInternas",
            "BaseImponible", "IVA", "ImporteBruto", "TipoRetencion", "Retencion", "ImporteTotal", "TipoFactura", "TipoRectificativa", "EstadoFactura", "IdAsientoContable",
            "FechaExportacionContable", "Historial", "DireccionCliente", "CodigoPostalCliente", "PoblacionCliente", "ProvinciaCliente", "SinVeriFactu", "Version", "MotivoRectificacion",
        ], filas, "IdFactura");
        for (var i = 0; i < preparadas.Count; i++) Mapear(TiposErp.Factura, Txt(preparadas[i].Numero), ids[i]);
        est.Cargadas = ids.Count;

        var lineas = new List<object?[]>();
        for (var i = 0; i < preparadas.Count; i++)
            foreach (var l in preparadas[i].Factura.Lineas)
                lineas.Add([ids[i], (short)l.NumeroLinea, null, l.Descripcion.Length == 0 ? "-" : l.Descripcion, l.NotasInternas, l.Fecha, l.Cantidad, l.PrecioUnitario, 0m, 0m,
                    l.BaseImponible.Valor, l.Impuesto, l.ClaveRegimen, l.CalificacionOperacion, l.CausaExencion, l.TipoIva, l.CuotaIva.Valor, 0m, 0m, false, l.ImporteTotal.Valor,
                    l.Nombre.Length == 0 ? null : l.Nombre, ReglasErp.Recortar(l.CuentaContable, 7), null, l.IdCentroCoste]);
        await MasivaAsync("LineasFactura",
        [
            "IdFactura", "NumeroLinea", "IdConcepto", "Descripcion", "NotasInternas", "Fecha", "Cantidad", "PrecioUnitario", "DescuentoPorcentaje", "DescuentoImporte",
            "BaseImponible", "Impuesto", "ClaveRegimen", "CalificacionOperacion", "CausaExencion", "TipoIVA", "CuotaIVA", "TipoRecargoEquivalencia", "CuotaRecargoEquivalencia",
            "EsSuplido", "ImporteTotal", "Nombre", "CuentaContable", "IdDepartamento", "IdCentroCoste",
        ], lineas);

        // Rectificativas: la original, si se migró y no la rectifica ya otra.
        var usadas = new HashSet<int>();
        await EnLotesAsync(preparadas.Select((p, i) => (p, i)).Where(x => x.p.Rectificada.Length > 0), async (s, x) =>
        {
            var original = Mapa(TiposErp.Factura, x.p.Rectificada);
            if (original is not int o2 || !usadas.Add(o2))
            {
                ClaveNula(Fase2, "FacturasVenta", Txt(x.p.Numero), "CodigoFacturaRectificada", x.p.Rectificada,
                    original is null ? $"La factura rectificada {x.p.Rectificada} no se ha migrado: la rectificativa {x.p.Numero} queda sin enlace." : $"La factura {x.p.Rectificada} ya la rectifica otra: la {x.p.Numero} queda sin enlace.");
                return;
            }
            await s.EjecutarAsync("UPDATE Facturas SET IdFacturaRectificada = ? WHERE IdFactura = ?", o2, ids[x.i]);
        });

        await EnlazarLibroAsync(preparadas, ids);
    }

    private static LineaVentaErp LineaErp(Fila l) => new(l.E("NumLinea") ?? 0, l.E("IdConcepto"), l.T("Descripcion"), l.Dec("Cantidad"), l.Dec("Precio"), l.Dec("TipoIVA"),
        l.F("Fecha"), l.B("EsCabecera"), l.B("EsDescuento"), l.Dec("DescuentoPorcentual"), l.Dec("DescuentoAbsoluto"), l.T("DescripcionCompleta"));

    /// <summary>Serie del ejercicio sin prefijo (los números del ERP); la siguiente factura continúa la numeración.</summary>
    private async Task<int> SerieAsync(int anio, int desde, int hasta)
    {
        var idEj = _ejercicios[anio].Id;
        var existente = (await ConsultarAsync("SELECT IdSerie, RangoDesde, UltimoNumero FROM SeriesFacturacion WHERE IdSociedadEjercicio = ? AND EsRectificativa = False AND (Prefijo = '' OR Prefijo = ' ' OR Prefijo IS NULL)",
            f => (Id: f.Entero("IdSerie"), Desde: f.Entero("RangoDesde"), Ultimo: f.EnteroNulo("UltimoNumero")), idEj)).FirstOrDefault();
        var rangoHasta = Math.Max(hasta, 99999);
        int id;
        if (existente.Id != 0)
        {
            await EjecutarAsync("UPDATE SeriesFacturacion SET RangoDesde = ?, UltimoNumero = ? WHERE IdSerie = ?", Math.Min(existente.Desde, desde), Math.Max(existente.Ultimo ?? 0, hasta), existente.Id);
            id = existente.Id;
        }
        else
        {
            var predeterminada = Convert.ToInt32(await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM SeriesFacturacion WHERE IdSociedadEjercicio = ? AND EsPredeterminada = True", idEj)), CultureInfo.InvariantCulture) == 0;
            const string sql = "INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, UltimoNumero, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, False, ?)";
            try { id = await InsertarAsync(sql, idEj, $"Facturas {anio} (ERP)", desde, rangoHasta, "", hasta, predeterminada); }
            catch (Exception)
            {
                // Si el campo no admite cadenas vacías, un espacio: la numeración recorta el prefijo.
                id = await InsertarAsync(sql, idEj, $"Facturas {anio} (ERP)", desde, rangoHasta, " ", hasta, predeterminada);
            }
            Anotar(Fase2, "FacturasVenta", Txt(anio), null, "Erp.SerieCreada", Severidad.Informativa,
                $"He creado la serie de {anio} sin prefijo, del {desde} al {rangoHasta}, con el último número {hasta}.");
        }
        Mapear(TiposErp.Serie, Txt(anio), id);
        return id;
    }

    /// <summary>
    /// Serie de rectificativas del ejercicio en curso, como la creó el propietario en la copia erp-02 (2026-11-11): prefijo R y la
    /// misma numeración que las facturas normales. Si el ejercicio ya tiene una serie rectificativa, no se toca.
    /// </summary>
    private async Task SerieRectificativaAsync(int anio)
    {
        if (!_ejercicios.TryGetValue(anio, out var ej) || !_series.TryGetValue(anio, out var normal)) return;
        if (Convert.ToInt32(await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT COUNT(*) FROM SeriesFacturacion WHERE IdSociedadEjercicio = ? AND EsRectificativa = True", ej.Id)), CultureInfo.InvariantCulture) > 0) return;
        var (desde, hasta) = (await ConsultarAsync("SELECT RangoDesde, RangoHasta FROM SeriesFacturacion WHERE IdSerie = ?", f => (f.Entero("RangoDesde"), f.Entero("RangoHasta")), normal)).First();
        var id = await InsertarAsync("INSERT INTO SeriesFacturacion (IdSociedadEjercicio, Descripcion, RangoDesde, RangoHasta, Prefijo, UltimoNumero, EsRectificativa, EsPredeterminada) VALUES (?, ?, ?, ?, ?, ?, True, False)",
            ej.Id, $"Facturas {anio} Rectificativas", desde, hasta, "R", null);
        Mapear(TiposErp.Serie, $"R{anio}", id);
        Anotar(Fase2, "FacturasVenta", $"R{anio}", null, "Erp.SerieCreada", Severidad.Informativa, $"He creado la serie de rectificativas de {anio}: prefijo R, del {desde} al {hasta}.");
    }

    // ---------- libro de IVA repercutido ----------

    /// <summary>
    /// Enlaza cada registro del libro importado de ContaSol con su factura (IVARepercutido.IdFactura), lo lleva al trimestre de la
    /// fecha de su asiento (F_APU.FECAPU) y reparte sus tramos por la 477 de la actividad de las líneas (propietario 2026-11-11, a),
    /// conservando sus totales.
    /// </summary>
    private async Task EnlazarLibroAsync(IReadOnlyList<FacturaPreparada> preparadas, IReadOnlyList<int> ids)
    {
        var est = Est(Fase2, "F_IVR (enlace y reparto por actividad)");
        var conRegistro = preparadas.Select((p, i) => (p, Id: ids[i])).Where(x => x.p.IdRegistroIva is not null).ToList();
        est.Leidas = o.RegistrosRepercutido.Count;
        var actividades = await new LectorActividadesIva(_bd).PorCentroAsync(Sociedad);
        var registros = new Dictionary<int, (int IdEj, DateTime Fecha, int Periodo)>();
        foreach (var trozo in conRegistro.Select(x => x.p.IdRegistroIva!.Value).Chunk(200))
            foreach (var r in await ConsultarAsync($"SELECT r.IdIVARepercutido, r.IdSociedadEjercicio, r.FechaHoraRegistroContable, p.Numero FROM IVARepercutido AS r INNER JOIN PeriodosIVA AS p ON r.IdPeriodoIVA = p.IdPeriodoIVA WHERE r.IdIVARepercutido IN ({Lista(trozo)})",
                         f => (Id: f.Entero("IdIVARepercutido"), IdEj: f.Entero("IdSociedadEjercicio"), Fecha: f.Fecha("FechaHoraRegistroContable"), Periodo: Convert.ToInt32(f.Valor("Numero"), CultureInfo.InvariantCulture))))
                registros[r.Id] = (r.IdEj, r.Fecha, r.Periodo);
        var desgloses = new Dictionary<int, List<(string? Clave, string Calif, string? Causa, decimal Tipo, decimal Base, decimal Cuota, decimal TipoRe, decimal CuotaRe)>>();
        foreach (var trozo in registros.Keys.Chunk(200))
            foreach (var d in await ConsultarAsync($"SELECT IdIVARepercutido, ClaveRegimen, CalificacionOperacion, CausaExencion, TipoIVA, BaseImponible, CuotaRepercutida, TipoRecargoEquivalencia, CuotaRecargoEquivalencia FROM DesglosesIVARepercutido WHERE IdIVARepercutido IN ({Lista(trozo)}) ORDER BY IdIVARepercutido, Orden",
                         f => (Id: f.Entero("IdIVARepercutido"), Clave: f.Texto("ClaveRegimen"), Calif: f.Texto("CalificacionOperacion") ?? "S1", Causa: f.Texto("CausaExencion"),
                             Tipo: f.Decimal("TipoIVA"), Base: f.Decimal("BaseImponible"), Cuota: f.Decimal("CuotaRepercutida"), TipoRe: f.Decimal("TipoRecargoEquivalencia"), CuotaRe: f.Decimal("CuotaRecargoEquivalencia"))))
                (desgloses.TryGetValue(d.Id, out var l) ? l : desgloses[d.Id] = []).Add((d.Clave, d.Calif, d.Causa, d.Tipo, d.Base, d.Cuota, d.TipoRe, d.CuotaRe));

        // La 477 general de cada asiento y la más usada de cada ejercicio (en ContaSol todo el IVA va a una sola 477).
        var general = new Dictionary<int, string>();
        foreach (var trozo in conRegistro.Where(x => x.p.IdAsiento is not null).Select(x => x.p.IdAsiento!.Value).Distinct().Chunk(200))
            foreach (var a in await ConsultarAsync($"SELECT IdAsiento, CuentaContable FROM Apuntes WHERE IdAsiento IN ({Lista(trozo)}) AND CuentaContable LIKE '477%'",
                         f => (Id: f.Entero("IdAsiento"), Cuenta: (f.Texto("CuentaContable") ?? "").Trim())))
                general.TryAdd(a.Id, a.Cuenta);
        var generalAnio = conRegistro.Where(x => x.p.IdAsiento is int a && general.ContainsKey(a)).GroupBy(x => x.p.Anio)
            .ToDictionary(g => g.Key, g => g.GroupBy(x => general[x.p.IdAsiento!.Value]).OrderByDescending(c => c.Count()).First().Key);
        var cuentasIva = new Dictionary<(int IdEj, string Codigo), int?>();

        var periodos = new Dictionary<(int IdEj, int Numero), int>();
        foreach (var (p, idFactura) in conRegistro)
        {
            cancelacion.ThrowIfCancellationRequested();
            var idReg = p.IdRegistroIva!.Value;
            if (!registros.TryGetValue(idReg, out var reg)) continue;
            var ctx = Contexto(p.Anio)!;
            // Fecha del libro = la del asiento; el trimestre, el suyo.
            DateTime? nuevaFecha = p.FechaAsiento is DateTime fa && fa.Year == p.Anio && fa.Date != reg.Fecha.Date ? fa : null;
            int? nuevoPeriodo = null;
            if (nuevaFecha is DateTime nf && (nf.Month - 1) / 3 + 1 is var tri && tri != reg.Periodo)
            {
                if (!periodos.TryGetValue((reg.IdEj, tri), out var idPeriodo))
                    periodos[(reg.IdEj, tri)] = idPeriodo = await PeriodoIvaAsync(reg.IdEj, p.Anio, tri);
                nuevoPeriodo = idPeriodo;
                Anotar(Fase2, "F_IVR", Txt(p.Numero), "FECIVR", "Erp.LibroAlTrimestreDelAsiento", Severidad.Informativa,
                    $"El registro del libro de la factura {p.Numero} pasa al {tri}T de {p.Anio}, el de la fecha de su asiento ({nf:dd/MM/yyyy}).", reg.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
            }
            // Reparto por actividad con la regla de la contabilización nueva (CuentasIva, organizacion/sociedades-ejercicios
            // 2026-11-13 20:55): cada tipo del registro, entre las 477 de sus líneas (477 + dígito de la serie + porcentaje con tres
            // cifras; exentas en 000). La 477 histórica del asiento solo queda para un tipo con decimales, que no tiene cuenta calculable.
            var cuentaHistorica = p.IdAsiento is int ia && general.TryGetValue(ia, out var g) ? g : generalAnio.GetValueOrDefault(p.Anio);
            var filas = new List<object?[]>();
            if (desgloses.TryGetValue(idReg, out var tramos))
            {
                var orden = 1;
                foreach (var grupo in tramos.GroupBy(t => t.Tipo))
                {
                    if ((CuentasIva.Repercutido(grupo.Key) ?? cuentaHistorica) is not { } cuentaTipo)
                    {
                        Anotar(Fase2, "F_IVR", Txt(p.Numero), "TipoIVA", "Erp.SinCuentaIva", Severidad.Advertencia,
                            $"El tramo del {grupo.Key:0.##} % de la factura {p.Numero} no tiene cuenta: {CuentasIva.MotivoSinCuenta(grupo.Key)} Se queda sin repartir por actividad.");
                        continue;
                    }
                    var b = grupo.Sum(t => t.Base);
                    var c = grupo.Sum(t => t.Cuota);
                    var primero = grupo.First();
                    var partes = p.Factura.Lineas.Where(l => !l.EsSuplido && l.TipoIva == grupo.Key && l.Cantidad != 0m)
                        .Select(l => (Cuenta: l.IdCentroCoste is int cc && actividades.TryGetValue(cc, out var act)
                            && CuentasIva.Repercutido(grupo.Key, act.Serie) is { } deActividad ? deActividad : cuentaTipo, Base: l.BaseImponible.Valor)).ToList();
                    var reparto = ReglasErp.Repartir(b, c, partes.Count > 0 ? partes : [(cuentaTipo, b)]);
                    foreach (var (cuenta, baseParte, cuotaParte) in reparto)
                    {
                        if (!cuentasIva.TryGetValue((ctx.IdSociedadEjercicio, cuenta), out var idCuenta))
                            cuentasIva[(ctx.IdSociedadEjercicio, cuenta)] = idCuenta = await CuentaIvaAsync(ctx, cuenta, grupo.Key, p.Numero);
                        filas.Add([idReg, orden++, primero.Clave, primero.Calif, primero.Causa, baseParte, grupo.Key, cuotaParte,
                            grupo.Count() == 1 ? primero.TipoRe : 0m, grupo.Count() == 1 ? primero.CuotaRe : 0m, idCuenta]);
                    }
                }
            }
            await _bd.EnTransaccionAsync(async s =>
            {
                await s.EjecutarAsync("UPDATE IVARepercutido SET IdFactura = ? WHERE IdIVARepercutido = ?", idFactura, idReg);
                if (nuevaFecha is DateTime fecha && nuevoPeriodo is int periodo)
                    await s.EjecutarAsync("UPDATE IVARepercutido SET FechaHoraRegistroContable = ?, IdPeriodoIVA = ? WHERE IdIVARepercutido = ?", fecha, periodo, idReg);
                else if (nuevaFecha is not null)
                    await s.EjecutarAsync("UPDATE IVARepercutido SET FechaHoraRegistroContable = ? WHERE IdIVARepercutido = ?", nuevaFecha, idReg);
                if (filas.Count > 0)
                {
                    await s.EjecutarAsync("DELETE FROM DesglosesIVARepercutido WHERE IdIVARepercutido = ?", idReg);
                    foreach (var fila in filas)
                        await s.EjecutarAsync("INSERT INTO DesglosesIVARepercutido (IdIVARepercutido, Orden, ClaveRegimen, CalificacionOperacion, CausaExencion, BaseImponible, TipoIVA, CuotaRepercutida, " +
                            "TipoRecargoEquivalencia, CuotaRecargoEquivalencia, IdCuentaIVARepercutido) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)", fila);
                }
                return true;
            }, cancelacion);
            est.Cargadas++;
        }
    }

    private async Task<int> PeriodoIvaAsync(int idEj, int anio, int trimestre)
    {
        var id = await _bd.EjecutarAsync(s => s.EscalarAsync("SELECT IdPeriodoIVA FROM PeriodosIVA WHERE IdSociedadEjercicio = ? AND Numero = ?", idEj, (byte)trimestre));
        if (id is not null and not DBNull) return Convert.ToInt32(id, CultureInfo.InvariantCulture);
        var inicio = new DateTime(anio, (trimestre - 1) * 3 + 1, 1);
        return await InsertarAsync("INSERT INTO PeriodosIVA (IdSociedadEjercicio, Numero, FechaInicio, FechaFin, Estado) VALUES (?, ?, ?, ?, ?)",
            idEj, (byte)trimestre, inicio, inicio.AddMonths(3).AddDays(-1), "ABIERTO");
    }

    /// <summary>Cuenta 477 del ejercicio; si es la de una actividad y no existe, se crea como al contabilizar (propietario 2026-11-07).</summary>
    private async Task<int?> CuentaIvaAsync(Nucleo.Contexto.ContextoSociedadEjercicio ctx, string codigo, decimal tipo, int factura)
    {
        var r = await m.Cuentas.ResolverAsync(ctx, codigo);
        if (r.EsCorrecto) return r.Valor.IdCuentaContable;
        // Un código ya usado en otro ejercicio es la misma identidad contable: se da de alta en este (reserva histórica de códigos).
        var alta = Identidad(codigo) is int identidad
            ? await m.Cuentas.AltaEnEjercicioAsync(ctx, identidad, codigo, $"IVA repercutido del {tipo:0.##} % (actividad)")
            : await m.Cuentas.AltaSubcuentaAsync(ctx, codigo, $"IVA repercutido del {tipo:0.##} % (actividad)");
        if (alta.EsCorrecto)
        {
            Anotar(Fase2, "F_IVR", codigo, null, "Erp.CuentaIvaCreada", Severidad.Informativa, $"He creado la cuenta {codigo} en {ctx.Ejercicio} para el libro de IVA por actividad.");
            return alta.Valor.IdCuentaContable;
        }
        Anotar(Fase2, "F_IVR", Txt(factura), null, "Erp.CuentaIvaNoCreada", Severidad.Advertencia, $"No he podido crear la cuenta {codigo} en {ctx.Ejercicio}: el tramo queda sin cuenta ({alta.Error!.Mensaje}).");
        return null;
    }
}
