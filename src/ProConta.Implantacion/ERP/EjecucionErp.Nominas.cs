using System.Globalization;
using ProConta.Contabilidad.Plantillas;
using ProConta.Implantacion.ContaSol;
using ProConta.Nominas.Contabilizacion;
using ProConta.Nominas.Importacion;
using ProConta.Nominas.Lectura;
using ProConta.Terceros.Roles;

namespace ProConta.Implantacion.ERP;

// Fase 7: nóminas (nominas 2026-11-13, implantacion/migracion-erp M-03). PlantillasApuntes → líneas de la plantilla de sistema «Nómina» de la
// sociedad; AliasCamposF_NOM → AliasCamposNomina; los trabajadores de F_NOM → rol Empleado con su código y su cuenta 465; F_NOM → una
// importación (origen MIGRACION) por fecha y tipo de paga, ya contabilizada y enlazada con los asientos importados de ContaSol (ASIAPU).
// Control de paridad: lo que propone la plantilla con los importes de F_NOM frente al asiento de ContaSol (cuentas, lados e importes).
internal sealed partial class EjecucionErp
{
    private const string Fase7 = "7 nominas";

    /// <summary>Columnas de F_NOM con otro nombre en Nominas.</summary>
    private static readonly IReadOnlyDictionary<string, string> ColumnasFNom = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["TotalFLC_FPHIB"] = "TotalFLC-FPHIB",
    };

    private sealed record NominaErp(Fila Fila, int Anio, DateTime Fecha, string Tipo, string Codigo);

    private async Task NominasAsync()
    {
        var sociedadErp = int.TryParse(Sociedad, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : -1;
        var plantilla = await PlantillaNominaAsync(sociedadErp);
        await AliasNominaAsync();

        // F_NOM de la sociedad: su IdSociedad es la fila de Sociedades del ERP (una por ejercicio).
        var ejerciciosErp = o.Tabla("Sociedades").Where(s => s.E("IdSociedad") is not null)
            .ToDictionary(s => s.E("IdSociedad")!.Value, s => (Codigo: s.T("Codigo"), Anio: s.E("Ejercicio") ?? 0));
        var est = Est(Fase7, "F_NOM");
        var nominas = new List<NominaErp>();
        foreach (var f in o.Tabla("F_NOM"))
        {
            est.Leidas++;
            var clave = $"{f.T("IdTrabajador")} {f.F("Fecha"):yyyy-MM-dd}";
            if (!ejerciciosErp.TryGetValue(f.E("IdSociedad") ?? -1, out var ej) || ej.Codigo != Sociedad)
            {
                Excluir(Fase7, "F_NOM", clave, $"La nómina es de la sociedad {f.E("IdSociedad")} del ERP, que no es {Sociedad} (o no existe).", Severidad.Informativa);
                continue;
            }
            if (f.F("Fecha") is not DateTime fecha || f.T("IdTrabajador") is not { } codigo)
            {
                Excluir(Fase7, "F_NOM", clave, "La nómina no tiene fecha o código de trabajador.");
                continue;
            }
            if (!_ejercicios.ContainsKey(fecha.Year))
            {
                Excluir(Fase7, "F_NOM", clave, $"El ejercicio {fecha.Year} no está en ProContaVF (no se importó de ContaSol).");
                continue;
            }
            var extra = string.Equals(f.T("TipoPaga"), "EXTRA", StringComparison.OrdinalIgnoreCase) || f.B("PagaExtra");
            nominas.Add(new NominaErp(f, fecha.Year, fecha, extra ? "EXTRA" : "MENSUAL", codigo));
        }

        var empleados = await EmpleadosNominaAsync(nominas);

        // Una importación por ejercicio, fecha y tipo de paga, contabilizada con los asientos de ContaSol.
        var importaciones = Est(Fase7, "ImportacionesNomina");
        var columnas = CamposNomina.Importes.Select(c => c.Nombre).ToList();
        var paridad = (Iguales: 0, Distintas: 0, SinComparar: 0);
        foreach (var g in nominas.GroupBy(x => (x.Anio, x.Fecha, x.Tipo)).OrderBy(g => g.Key.Fecha))
        {
            var ctx = Contexto(g.Key.Anio)!;
            var desde = new DateTime(g.Key.Fecha.Year, g.Key.Fecha.Month, 1);
            var huella = $"MIGRACION {g.Key.Fecha:yyyy-MM-dd} {g.Key.Tipo}";
            var sinAsiento = new List<string>();
            var filas = g.OrderBy(x => x.Codigo, StringComparer.Ordinal).Select((x, i) =>
            {
                var asiento = x.Fila.E("ASIAPU") is int asi and > 0 ? AsientoCs(x.Anio, 1, asi) : null;
                if (asiento is null)
                {
                    var texto = x.Fila.E("ASIAPU") is int a2 and > 0
                        ? $"El asiento {a2} de {x.Anio} de la nómina de {x.Fila.T("Trabajador")} no está en lo importado de ContaSol."
                        : $"La nómina de {x.Fila.T("Trabajador")} del {x.Fecha:dd/MM/yyyy} no tiene asiento en ProConta antiguo.";
                    sinAsiento.Add(texto);
                    ClaveNula(Fase7, "F_NOM", $"{x.Codigo} {x.Fecha:yyyy-MM-dd}", "ASIAPU", x.Fila.E("ASIAPU"), texto);
                }
                return (Nomina: x, Ordinal: i + 1, Asiento: asiento);
            }).ToList();

            var idImportacion = await InsertarAsync(
                "INSERT INTO ImportacionesNomina (IdSociedadEjercicio, NombreFichero, Huella, Origen, TextoPaga, TipoPaga, PeriodoDesde, PeriodoHasta, FechaNomina, Estado, " +
                "Incidencias, FechaHoraImportacion, Operador, FechaHoraEstado, OperadorEstado, MotivoEstado) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                ctx.IdSociedadEjercicio, $"F_NOM {g.Key.Fecha:yyyy-MM-dd} {g.Key.Tipo}", huella, OrigenNominas.Migracion.ToString().ToUpperInvariant(), "Migrado de ProConta antiguo (F_NOM)", g.Key.Tipo,
                desde, desde.AddMonths(1).AddDays(-1), g.Key.Fecha, ServicioImportacionNominas.EstadoContabilizada,
                sinAsiento.Count == 0 ? null : string.Join("\n", sinAsiento), Ahora, Operador, Ahora, Operador, "Histórico de ProConta antiguo, contabilizado en ContaSol.");
            Mapear(TiposErp.ImportacionNomina, huella, idImportacion);
            importaciones.Cargadas++;
            importaciones.Leidas++;

            var sql = "INSERT INTO Nominas (IdImportacionNomina, Ordinal, IdEmpleado, CodigoTrabajador, NombreOrigen, NifOrigen, TipoPaga, FechaNomina, IdAsiento, " +
                string.Join(", ", columnas) + ") VALUES (" + string.Join(", ", Enumerable.Repeat("?", 9 + columnas.Count)) + ")";
            await EnLotesAsync(filas, async (s, x) =>
            {
                var f = x.Nomina.Fila;
                object?[] valores = [idImportacion, x.Ordinal, empleados.GetValueOrDefault(x.Nomina.Codigo), ReglasErp.Recortar(x.Nomina.Codigo, 30),
                    ReglasErp.Recortar(f.T("Trabajador"), 150) ?? x.Nomina.Codigo, ReglasErp.Recortar(f.T("DNI"), 20), x.Nomina.Tipo, x.Nomina.Fecha, x.Asiento,
                    .. columnas.Select(c => (object?)f.D(ColumnasFNom.GetValueOrDefault(c, c)))];
                await s.EjecutarAsync(sql, valores);
            });
            est.Cargadas += filas.Count;

            // Paridad: la plantilla con los importes de F_NOM frente al asiento de ContaSol. Si ProConta antiguo juntó varias nóminas en
            // un asiento (p. ej. enero–junio de 2023), se compara la suma de sus propuestas con ese asiento.
            foreach (var g2 in filas.GroupBy(x => x.Asiento))
            {
                if (plantilla is null || g2.Key is not int idAsiento) { paridad.SinComparar += g2.Count(); continue; }
                var juntas = g2.Select(x => x.Nomina).ToList();
                var diferencia = await DiferenciaParidadAsync(plantilla, ctx, juntas, idAsiento);
                if (diferencia is null) { paridad.Iguales += juntas.Count; continue; }
                paridad.Distintas += juntas.Count;
                var quienes = string.Join(", ", juntas.Select(n => n.Fila.T("Trabajador") ?? n.Codigo));
                Anotar(Fase7, "F_NOM", string.Join(" ", juntas.Select(n => $"{n.Codigo} {n.Fecha:yyyy-MM-dd}")), null, "Nominas.Paridad", Severidad.Advertencia,
                    $"La plantilla «Nómina» no reproduce el asiento {idAsiento} del {juntas[0].Fecha:dd/MM/yyyy} " +
                    (juntas.Count == 1 ? $"de {quienes}" : $"que junta {juntas.Count} nóminas ({quienes})") + $": {diferencia}");
            }
        }
        Anotar(Fase7, "F_NOM", null, null, "Nominas.ParidadResumen", Severidad.Informativa,
            $"Paridad de la plantilla «Nómina» con los asientos de ProConta antiguo: {paridad.Iguales} iguales, {paridad.Distintas} distintos y {paridad.SinComparar} sin comparar " +
            "(sin asiento importado o sin plantilla).");
    }

    // ---------- plantilla ----------

    /// <summary>Carga las líneas de PlantillasApuntes de la sociedad en su copia de la plantilla de sistema «Nómina» (la crea si no la tiene).</summary>
    private async Task<PlantillaAsiento?> PlantillaNominaAsync(int sociedadErp)
    {
        var est = Est(Fase7, "PlantillasApuntes");
        var antiguas = o.Tabla("PlantillasApuntes").Where(p => p.E("IdSociedad") == sociedadErp).ToList();
        est.Leidas = antiguas.Count;
        foreach (var otra in o.Tabla("PlantillasApuntes").Where(p => p.E("IdSociedad") != sociedadErp))
            Excluir(Fase7, "PlantillasApuntes", $"{otra.E("IdSociedad")}/{otra.E("Orden")}", $"La línea es de la sociedad {otra.E("IdSociedad")} del ERP.", Severidad.Informativa);
        if (antiguas.Count == 0)
        {
            Anotar(Fase7, "PlantillasApuntes", null, null, "Nominas.SinPlantilla", Severidad.Advertencia,
                $"ProConta antiguo no tiene plantilla de nóminas para la sociedad {Sociedad}: la plantilla «Nómina» queda sin líneas y no se podrá contabilizar hasta definirlas.");
            return null;
        }
        var (lineas, errores) = PlantillaNomina.Convertir(antiguas.Select(p => new PlantillaNomina.LineaAntigua(p.E("Orden") ?? 0, p.T("FormulaDescripcion"),
            p.T("FormulaCuenta"), p.T("DH"), p.T("FormulaImporte"), p.T("Condicion"))));
        if (errores.Count > 0)
        {
            foreach (var e in errores) Anotar(Fase7, "PlantillasApuntes", Sociedad, null, "Nominas.FormulaNoConvertida", Severidad.Advertencia, e);
            Anotar(Fase7, "PlantillasApuntes", Sociedad, null, "Nominas.PlantillaNoCargada", Severidad.Advertencia,
                "La plantilla de nóminas no se ha cargado porque alguna línea no se ha podido convertir: corrígela y repite la migración.");
            return null;
        }
        var agrupar = antiguas.Any(p => p.B("AgruparApuntes"));
        var id = await new ServicioContabilizacionNominas(_bd).CargarPlantillaAsync(Sociedad, lineas, agrupar);
        est.Cargadas = lineas.Count;
        Anotar(Fase7, "PlantillasApuntes", Sociedad, null, "Nominas.PlantillaCargada", Severidad.Informativa,
            $"Plantilla «Nómina» de la sociedad {Sociedad}: {lineas.Count} líneas cargadas de ProConta antiguo.");
        return new PlantillaAsiento(id, PlantillaNomina.Nombre, PlantillaNomina.Descripcion, TipoPlantilla.Sistema, OrigenPlantilla.Proceso, [], lineas, Sociedad,
            PlantillaNomina.ClaveProceso, ReglaFecha.FechaOrigen, agrupar, Clase: ClasePlantilla.Proceso);
    }

    // ---------- alias ----------

    private async Task AliasNominaAsync()
    {
        var est = Est(Fase7, "AliasCamposF_NOM");
        var existentes = (await ConsultarAsync("SELECT Cabecera FROM AliasCamposNomina WHERE IdSociedad = ?", f => f.Texto("Cabecera") ?? "", Sociedad))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var a in o.Tabla("AliasCamposF_NOM"))
        {
            est.Leidas++;
            var clave = $"{a.T("Empresa")}/{a.T("Origen")}";
            if (a.T("Empresa") != Sociedad) { Excluir(Fase7, "AliasCamposF_NOM", clave, $"El alias es de la sociedad {a.T("Empresa")}.", Severidad.Informativa); continue; }
            var cabecera = CamposNomina.NormalizarCabecera(a.T("Origen"));
            if (cabecera.Length == 0) { Excluir(Fase7, "AliasCamposF_NOM", clave, "El alias no tiene cabecera."); continue; }
            var destino = a.T("Destino");
            var campo = destino is null ? null : CamposNomina.Buscar(ColumnasFNom.FirstOrDefault(c => c.Value == destino).Key ?? destino)?.Nombre;
            // Los campos de identificación (Trabajador, IdTrabajador, DNI…) ya los reconoce el lector: no se guardan como alias de importe.
            if (campo is null && destino is not null && CamposNomina.AliasPorDefecto.ContainsKey(cabecera)) { Excluir(Fase7, "AliasCamposF_NOM", clave, "El lector ya reconoce esta cabecera.", Severidad.Informativa); continue; }
            if (campo is null && destino is not null)
                Anotar(Fase7, "AliasCamposF_NOM", clave, "Destino", "Nominas.AliasSinCampo", Severidad.Informativa,
                    $"El campo «{destino}» no existe en las nóminas nuevas: la columna «{a.T("Origen")}» se ignorará al importar.");
            if (existentes.Contains(cabecera)) { Excluir(Fase7, "AliasCamposF_NOM", clave, "La sociedad ya tiene ese alias en ProContaVF: se conserva.", Severidad.Informativa); continue; }
            await EjecutarAsync("INSERT INTO AliasCamposNomina (IdSociedad, Cabecera, Campo) VALUES (?, ?, ?)", Sociedad, ReglasErp.Recortar(cabecera, 100), campo);
            existentes.Add(cabecera);
            est.Cargadas++;
        }
    }

    // ---------- empleados ----------

    /// <summary>
    /// Los trabajadores de F_NOM como rol Empleado: el que ya lo es por su código se conserva; si no, el tercero con el mismo DNI o, si
    /// solo hay uno, con el mismo nombre; si no hay ninguno, se da de alta como persona física. Su cuenta 465 es la de su última nómina.
    /// </summary>
    private async Task<Dictionary<string, int>> EmpleadosNominaAsync(IReadOnlyList<NominaErp> nominas)
    {
        var est = Est(Fase7, "Trabajadores");
        var r = new Dictionary<string, int>(StringComparer.Ordinal);
        if (nominas.Count == 0) return r;
        var ctx = Contexto(UltimoEjercicio)!;
        var servicio = new ServicioEmpleados(_bd, m.Cuentas, m.Terceros, actor);
        foreach (var g in nominas.GroupBy(x => x.Codigo))
        {
            est.Leidas++;
            var ultima = g.OrderByDescending(x => x.Fecha).First().Fila;
            var nombre = ultima.T("Trabajador") ?? g.Key;
            var dni = g.Select(x => x.Fila.T("DNI")).FirstOrDefault(d => d is not null);
            var identidad = Identidad(ultima.T("CuentaContable"));
            if (identidad is null)
                Anotar(Fase7, "F_NOM", g.Key, "CuentaContable", "Nominas.Sin465", Severidad.Advertencia,
                    $"La cuenta {ultima.T("CuentaContable")} de {nombre} no está en lo importado de ContaSol: se le crea una 465 nueva.");
            var busqueda = await _bd.EjecutarAsync(s => ServicioEmpleados.BuscarAsync(s, Sociedad, g.Key, dni, nombre), cancelacion);
            int? idTercero = null;
            if (busqueda.IdEmpleado is int ya)
            {
                r[g.Key] = ya;
                Mapear(TiposErp.Empleado, g.Key, ya);
                est.Cargadas++;
                continue;
            }
            var mismoNif = busqueda.Candidatos.Where(c => c.Motivo == "mismo NIF").ToList();
            var (nombrePila, ap1, ap2) = ServicioEmpleados.PartirNombre(nombre);
            var completo = ServicioEmpleados.Palabras($"{nombrePila} {ap1} {ap2}");
            var mismoNombre = busqueda.Candidatos.Where(c => ServicioEmpleados.Palabras(c.Nombre).SetEquals(completo)).ToList();
            if (mismoNif.Count == 1) idTercero = mismoNif[0].IdTercero;
            else if (mismoNombre.Count == 1) idTercero = mismoNombre[0].IdTercero;
            var alta = idTercero is int t
                ? await servicio.AsignarAsync(ctx, t, g.Key, identidad)
                : await servicio.AltaAsync(ctx, nombre, dni, g.Key, identidad);
            if (!alta.EsCorrecto)
            {
                Anotar(Fase7, "F_NOM", g.Key, "IdTrabajador", "Nominas.EmpleadoNoCreado", Severidad.Advertencia,
                    $"No he podido dar el rol Empleado a {nombre}: {string.Join(" ", alta.Errores.Select(e => e.Mensaje))} Sus nóminas quedan sin empleado.");
                continue;
            }
            r[g.Key] = alta.Valor.IdEmpleado;
            Mapear(TiposErp.Empleado, g.Key, alta.Valor.IdEmpleado);
            est.Cargadas++;
            Anotar(Fase7, "F_NOM", g.Key, "IdTrabajador", "Nominas.Empleado", Severidad.Informativa, idTercero is null
                ? $"{nombre} (código {g.Key}) se da de alta como tercero con el rol Empleado y la cuenta {alta.Valor.Cuenta}."
                : $"{nombre} (código {g.Key}) es el tercero {idTercero} ({(mismoNif.Count == 1 ? "mismo DNI" : "mismo nombre")}): se le da el rol Empleado con la cuenta {alta.Valor.Cuenta}.");
        }
        return r;
    }

    // ---------- paridad ----------

    /// <summary>Diferencias entre lo que propone la plantilla con los importes de F_NOM y el asiento de ContaSol (cuentas, lados e importes), o null si coinciden.</summary>
    private async Task<string?> DiferenciaParidadAsync(PlantillaAsiento plantilla, Nucleo.Contexto.ContextoSociedadEjercicio ctx, IReadOnlyList<NominaErp> nominas, int idAsiento)
    {
        var propuestos = new List<(string Cuenta, decimal Saldo)>();
        foreach (var x in nominas)
        {
            var f = x.Fila;
            var importes = CamposNomina.Importes.ToDictionary(c => c.Nombre, c => f.Dec(ColumnasFNom.GetValueOrDefault(c.Nombre, c.Nombre)), StringComparer.OrdinalIgnoreCase);
            var registro = PlantillaNomina.Registro(new NominaContable(x.Codigo, f.T("Trabajador") ?? x.Codigo, f.T("CuentaContable") ?? "", x.Fecha, importes, x.Codigo, f.T("DNI"), x.Tipo));
            var propuesta = MotorPlantillas.Proponer(plantilla, new OrigenProceso(PlantillaNomina.ClaveProceso, [registro]), null, new EntornoSociedad(ctx), x.Fecha);
            if (!propuesta.EsCorrecto) return "la plantilla no genera el asiento (" + string.Join(" ", propuesta.Errores.Select(e => e.Mensaje)) + ")";
            propuestos.AddRange(propuesta.Valor.Asientos.SelectMany(a => a.Apuntes).Select(a => (a.Cuenta, a.Debe - a.Haber)));
        }
        var esperado = Sumar(propuestos);
        var real = Sumar(await ConsultarAsync("SELECT CuentaContable, ImporteDebe, ImporteHaber FROM Apuntes WHERE IdAsiento = ?",
            r => ((r.Texto("CuentaContable") ?? "").Trim(), (r.DecimalNulo("ImporteDebe") ?? 0m) - (r.DecimalNulo("ImporteHaber") ?? 0m)), idAsiento));
        var cuentas = esperado.Keys.Union(real.Keys).Order(StringComparer.Ordinal).ToList();
        var distintas = cuentas.Where(c => esperado.GetValueOrDefault(c) != real.GetValueOrDefault(c)).ToList();
        return distintas.Count == 0 ? null : string.Join("; ", distintas.Select(c =>
            $"{c} propone {PlantillaNomina.Euros(esperado.GetValueOrDefault(c))} y el asiento tiene {PlantillaNomina.Euros(real.GetValueOrDefault(c))}")) + " (saldo Debe − Haber por cuenta).";

        static Dictionary<string, decimal> Sumar(IEnumerable<(string Cuenta, decimal Saldo)> apuntes) =>
            apuntes.GroupBy(a => a.Cuenta, StringComparer.Ordinal).Select(g => (g.Key, Saldo: g.Sum(a => a.Saldo))).Where(g => g.Saldo != 0m)
                .ToDictionary(g => g.Key, g => g.Saldo, StringComparer.Ordinal);
    }
}
