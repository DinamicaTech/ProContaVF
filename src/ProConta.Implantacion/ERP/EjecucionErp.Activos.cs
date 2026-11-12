using System.Globalization;
using ProConta.Activos;
using ProConta.Implantacion.ContaSol;

namespace ProConta.Implantacion.ERP;

// Fase 6: Inmuebles → Activos según el modelo de docs/activos.md (decisiones 2026-11-11 18:21, 2026-11-12 09:47 y 10:00).
// TiposInmueble → TiposActivo del PGC; PlantillasInmuebles → catálogo TiposSubcuentaActivo (con su rol) y líneas
// PlantillasSubcuentasActivo (prefijos corregidos en vehículos, instalaciones y aplicaciones); CuentasInmuebles → SubcuentasActivo
// (se conservan las subcuentas reales); los datos del inmueble → ActivosInmuebles; el % de amortización → PlanesAmortizacionActivo.
// Las inversiones financieras (tipo 6) se dejan para la segunda fase (creative-lab/inversiones-financieras).
internal sealed partial class EjecucionErp
{
    private const string Fase6 = "6 activos";

    private const int TipoErpTecnico = 4;
    private const int TipoErpInversion = 6;
    private const int TipoErpParticular = 9;

    /// <summary>Tipo de inmueble del ERP → código de TiposActivo. El 4 (técnico) se reparte por su subcuenta: 206 aplicación, resto instalación.</summary>
    internal static readonly IReadOnlyDictionary<int, string> TiposActivoErp = new Dictionary<int, string>
    {
        [1] = "VIVIENDA",
        [2] = "LOCAL",
        [3] = "VEHICULO",
        [TipoErpTecnico] = "INSTALACION",
        [5] = "PARKING",
        [7] = "TURISTICO",
        [TipoErpParticular] = "VIVIENDA",
    };

    /// <summary>Línea de PlantillasInmuebles (su Id, igual en todos los tipos del ERP) → rol del tipo de subcuenta.</summary>
    internal static string RolDeLineaErp(int idLinea) => idLinea switch
    {
        3 => RolesSubcuentaActivo.Activo,
        2 => RolesSubcuentaActivo.Terreno,
        9 => RolesSubcuentaActivo.AmortizacionAcumulada,
        24 => RolesSubcuentaActivo.GastoAmortizacion,
        8 => RolesSubcuentaActivo.IngresoAlquiler,
        _ => RolesSubcuentaActivo.Libre,
    };

    /// <summary>Prefijos según el PGC de los roles de amortización para los tipos que el ERP tenía mal (activos, Q9).</summary>
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> PrefijosPgc = new Dictionary<string, IReadOnlyDictionary<string, string>>
    {
        ["VEHICULO"] = new Dictionary<string, string> { [RolesSubcuentaActivo.Activo] = "21800", [RolesSubcuentaActivo.AmortizacionAcumulada] = "28180", [RolesSubcuentaActivo.GastoAmortizacion] = "6810" },
        ["INSTALACION"] = new Dictionary<string, string> { [RolesSubcuentaActivo.Activo] = "21200", [RolesSubcuentaActivo.AmortizacionAcumulada] = "28120", [RolesSubcuentaActivo.GastoAmortizacion] = "6810" },
        ["APLICACION"] = new Dictionary<string, string> { [RolesSubcuentaActivo.Activo] = "20600", [RolesSubcuentaActivo.AmortizacionAcumulada] = "28060", [RolesSubcuentaActivo.GastoAmortizacion] = "6800" },
    };

    /// <summary>Tipo de activo de un técnico del ERP según la subcuenta de su valor: 206 → aplicación informática; si no, instalación técnica.</summary>
    internal static string TipoDeTecnico(string? cuentaValor) => cuentaValor?.Trim().StartsWith("206", StringComparison.Ordinal) == true ? "APLICACION" : "INSTALACION";

    /// <summary>Base amortizable: el coste de compra (o el valor si no hay coste) por el % de construcción, a 2 decimales.</summary>
    internal static decimal BaseAmortizable(decimal? costeCompra, decimal? valorCompra, decimal? porcentajeConstruccion) =>
        Math.Round((costeCompra ?? valorCompra ?? 0m) * (porcentajeConstruccion is > 0m and <= 1m ? porcentajeConstruccion.Value : 1m), 2, MidpointRounding.AwayFromZero);

    /// <summary>Vida útil en meses para un % anual lineal (p. ej. 0,02 → 600).</summary>
    internal static int VidaUtilMeses(decimal porcentajeAnual) => (int)Math.Round(12m / porcentajeAnual, MidpointRounding.AwayFromZero);

    private async Task ActivosAsync()
    {
        // Tipos del PGC: los da de alta el ActualizadorEsquema (versión con 20261112_1030_ACTIVOS_ESTRUCTURA).
        var tipos = (await ConsultarAsync("SELECT IdTipoActivo, Codigo FROM TiposActivo", f => (Id: f.Entero("IdTipoActivo"), Codigo: (f.Texto("Codigo") ?? "").Trim())))
            .ToDictionary(x => x.Codigo, x => x.Id, StringComparer.OrdinalIgnoreCase);
        if (TiposActivoPgc.Todos.FirstOrDefault(t => !tipos.ContainsKey(t)) is { } falta)
            throw new InvalidOperationException($"Falta el tipo de activo {falta}: pasa el ActualizadorEsquema a la BD destino antes de migrar.");
        var estTipos = Est(Fase6, "TiposInmueble");
        foreach (var t in o.Tabla("TiposInmueble"))
        {
            estTipos.Leidas++;
            var idErp = t.E("IdTipo") ?? 0;
            if (TiposActivoErp.TryGetValue(idErp, out var codigo)) { Mapear(TiposErp.TipoActivo, Txt(idErp), tipos[codigo]); estTipos.Cargadas++; }
            else if (idErp == TipoErpInversion) Excluir(Fase6, "TiposInmueble", Txt(idErp), "Las inversiones financieras se migrarán en su segunda fase (creative-lab/inversiones-financieras).", Severidad.Informativa);
            else Excluir(Fase6, "TiposInmueble", Txt(idErp), $"El tipo de inmueble {idErp} («{t.T("Descripcion")}») no tiene tipo de activo en ProContaVF.");
        }

        // Subcuentas ya generadas en el ERP, por inmueble y línea de plantilla.
        var cuentasErp = o.Tabla("CuentasInmuebles")
            .GroupBy(c => c.E("IdInmueble") ?? 0)
            .ToDictionary(g => g.Key, g => g.GroupBy(c => c.E("IdPlantilla") ?? 0).ToDictionary(x => x.Key, x => x.First().T("Cuenta")));

        // Catálogo de tipos de subcuenta: uno por nombre, con el rol de su línea en el ERP.
        var catalogo = (await ConsultarAsync("SELECT IdTipoSubcuentaActivo, Nombre, Rol FROM TiposSubcuentaActivo",
                f => (Id: f.Entero("IdTipoSubcuentaActivo"), Nombre: (f.Texto("Nombre") ?? "").Trim(), Rol: (f.Texto("Rol") ?? "").Trim())))
            .ToDictionary(x => x.Nombre, x => (x.Id, x.Rol), StringComparer.OrdinalIgnoreCase);
        var lineasExistentes = (await ConsultarAsync("SELECT IdPlantillaSubcuentaActivo, IdTipoActivo, IdTipoSubcuentaActivo FROM PlantillasSubcuentasActivo",
                f => (Id: f.Entero("IdPlantillaSubcuentaActivo"), Tipo: f.Entero("IdTipoActivo"), Sub: f.Entero("IdTipoSubcuentaActivo"))))
            .ToDictionary(x => (x.Tipo, x.Sub), x => x.Id);

        // Líneas de plantilla: (tipo de activo, Id de línea del ERP) → línea de PlantillasSubcuentasActivo. El técnico del ERP alimenta instalación y aplicación.
        var estPl = Est(Fase6, "PlantillasInmuebles");
        var plantillaDe = new Dictionary<(int Tipo, int Linea), int>();
        foreach (var p in o.Tabla("PlantillasInmuebles").OrderBy(p => p.E("Tipo")).ThenBy(p => p.E("Id")))
        {
            estPl.Leidas++;
            var tipoErp = p.E("Tipo") ?? 0;
            var idLinea = p.E("Id") ?? 0;
            var clave = $"{tipoErp}:{idLinea}";
            if (tipoErp == TipoErpInversion) { estPl.Excluidas++; continue; } // segunda fase
            if (!TiposActivoErp.TryGetValue(tipoErp, out var codigoTipo)) { Excluir(Fase6, "PlantillasInmuebles", clave, $"La plantilla {idLinea} es del tipo de inmueble {tipoErp}, que no tiene tipo de activo."); continue; }
            var rol = RolDeLineaErp(idLinea);
            var nombre = ReglasErp.Recortar(p.T("Nombre"), 255) ?? $"LÍNEA {idLinea} [TOKEN]";
            if (!catalogo.TryGetValue(nombre, out var sub))
                catalogo[nombre] = sub = (await InsertarAsync("INSERT INTO TiposSubcuentaActivo (Nombre, Descripcion, Rol, EstaActivo) VALUES (?, ?, ?, True)",
                    nombre, ReglasErp.Recortar(p.T("Descripcion"), 255), rol), rol);
            else if (sub.Rol != rol)
                Anotar(Fase6, "PlantillasInmuebles", clave, "Id", "Erp.RolDistinto", Severidad.Advertencia,
                    $"«{nombre}» ya tiene el rol «{RolesSubcuentaActivo.Textos[sub.Rol]}» y esta línea pediría «{RolesSubcuentaActivo.Textos[rol]}»: se queda el primero.");
            var tiposDestino = tipoErp == TipoErpTecnico ? new[] { "INSTALACION", "APLICACION" } : new[] { codigoTipo };
            foreach (var codigo in tiposDestino.Distinct())
            {
                if (tipoErp == TipoErpParticular) continue; // activo ajeno: sin plantilla
                var idTipo = tipos[codigo];
                var prefijo = PrefijosPgc.TryGetValue(codigo, out var corregidos) && corregidos.TryGetValue(sub.Rol, out var pgc) ? pgc : (p.T("SubCuenta") ?? "");
                if (lineasExistentes.TryGetValue((idTipo, sub.Id), out var id))
                    await EjecutarAsync("UPDATE PlantillasSubcuentasActivo SET Prefijo = ?, Orden = ?, EstaActiva = True WHERE IdPlantillaSubcuentaActivo = ?", prefijo, (short)idLinea, id);
                else
                    lineasExistentes[(idTipo, sub.Id)] = id = await InsertarAsync(
                        "INSERT INTO PlantillasSubcuentasActivo (IdTipoActivo, IdTipoSubcuentaActivo, Prefijo, Orden, EstaActiva) VALUES (?, ?, ?, ?, True)",
                        idTipo, sub.Id, prefijo, (short)idLinea);
                plantillaDe[(idTipo, idLinea)] = id;
            }
            Mapear(TiposErp.PlantillaActivo, clave, plantillaDe.Where(x => x.Key.Linea == idLinea && tiposDestino.Select(c => tipos[c]).Contains(x.Key.Tipo)).Select(x => x.Value).FirstOrDefault());
            estPl.Cargadas++;
        }

        // Activos con su ficha de inmueble y su plan de amortización.
        var est = Est(Fase6, "Inmuebles");
        var codigos = (await ConsultarAsync("SELECT Codigo FROM Activos WHERE IdSociedad = ?", f => (f.Texto("Codigo") ?? "").Trim(), Sociedad)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tipoDeActivo = new Dictionary<int, int>();
        var ahora = Ahora;
        var inversiones = 0;
        foreach (var i in o.Tabla("Inmuebles").OrderBy(i => i.E("Id")))
        {
            est.Leidas++;
            var idErp = i.E("Id") ?? 0;
            var clave = Txt(idErp);
            var tipoErp = i.E("Tipo") ?? 0;
            if (tipoErp == TipoErpInversion) { inversiones++; est.Excluidas++; continue; }
            if (!TiposActivoErp.TryGetValue(tipoErp, out var codigoTipo)) { Excluir(Fase6, "Inmuebles", clave, $"El inmueble {idErp} es del tipo {tipoErp}, que no tiene tipo de activo."); continue; }
            if (tipoErp == TipoErpTecnico)
                codigoTipo = TipoDeTecnico(cuentasErp.TryGetValue(idErp, out var cs) && cs.TryGetValue(3, out var valor) ? valor : null);
            var ajeno = tipoErp == TipoErpParticular;
            var idTipo = tipos[codigoTipo];

            var codigo = ReglasErp.Recortar(i.T("Alias"), 30) ?? $"ERP{idErp}";
            if (codigos.Contains(codigo)) codigo = ReglasErp.Recortar($"{codigo}-{idErp}", 30)!;
            codigos.Add(codigo);

            int? centro = null;
            if (!ajeno)
            {
                var departamento = i.E("Departamento");
                var sub = i.E("SubDepartamento");
                centro = Centro(ReglasErp.ClaveCentro(departamento, sub));
                if ((departamento is > 0 || sub is > 0) && centro is null)
                    ClaveNula(Fase6, "Inmuebles", clave, sub is > 0 ? "SubDepartamento" : "Departamento", sub is > 0 ? sub : departamento,
                        $"El {(sub is > 0 ? "subdepartamento" : "departamento")} {(sub is > 0 ? sub : departamento)} del inmueble {idErp} no es un centro de coste.");
                else if (centro is null)
                    Anotar(Fase6, "Inmuebles", clave, "Departamento", "Erp.SinCentro", Severidad.Advertencia, $"El inmueble {idErp} no tiene departamento: queda sin centro de coste.");
            }
            int? compra = i.E("IdFacturaCompra") is int fc and > 0 ? Mapa(TiposErp.Compra, Txt(fc)) : null;
            if (i.E("IdFacturaCompra") is int fc2 and > 0 && compra is null) ClaveNula(Fase6, "Inmuebles", clave, "IdFacturaCompra", fc2, $"La factura de compra {fc2} del inmueble {idErp} no se ha migrado.");

            var coste = i.D("CosteCompra") ?? i.D("ValorCompra") ?? 0m;
            var amortizado = i.Dec("ImporteAmortizado");
            var inmueble = TiposActivoPgc.EsInmueble(codigoTipo);
            string? D(string etiqueta, string campo) => i.D(campo) is decimal v && v != 0m ? $"{etiqueta}: {v.ToString("0.00", CultureInfo.GetCultureInfo("es-ES"))}" : null;
            // Lo que no tiene columna propia (los bienes que no son inmuebles) se conserva como texto.
            var descripcion = inmueble ? null : ReglasErp.UnirNotas(
                i.T("RefCatastral") is { } rc ? $"Referencia catastral: {rc}" : null, D("Valor de compra", "ValorCompra"), D("Coste de compra", "CosteCompra"),
                D("Valor catastral del suelo", "ValorCatastroSuelo"), D("Valor catastral del inmueble", "ValorCatastroInmueble"), D("Valor catastral total", "ValorCatastroTotal"),
                D("Precio del alquiler", "PrecioAlquiler"), i.F("FechaInicioAlquiler") is DateTime fa ? $"Inicio del alquiler: {fa:dd/MM/yyyy}" : null, D("Tributo metropolitano", "TributoMetropolitano"));

            var idActivo = await InsertarAsync(
                "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, AliasActivo, Descripcion, FechaAdquisicion, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, " +
                "AmortizacionAcumulada, DeterioroAcumulado, ValorContable, EstadoCalculo, IdCentroCoste, EsAjeno, IdFacturaCompra, ReferenciaMigracion, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                Sociedad, idTipo, codigo, ReglasErp.Recortar(i.T("Nombre"), 150) ?? codigo, ReglasErp.Recortar(i.T("Alias"), 100), descripcion, i.F("FechaCompra"), i.F("FechaCompra") ?? ahora.Date,
                (byte)1, !ajeno, coste, amortizado, 0m, coste - amortizado, (byte)0, centro, ajeno, compra, $"ERP:Inmuebles:{idErp}", 0);
            Mapear(TiposErp.Activo, clave, idActivo);
            tipoDeActivo[idErp] = idTipo;

            if (inmueble)
                await EjecutarAsync(
                    "INSERT INTO ActivosInmuebles (IdActivo, ReferenciaCatastral, ValorCatastralSuelo, ValorCatastralConstruccion, ValorCatastralTotal, PorcentajeConstruccion, " +
                    "ValorCompra, CosteCompra, PrecioAlquiler, FechaInicioAlquiler, TributoMetropolitano) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                    idActivo, ReglasErp.Recortar(i.T("RefCatastral"), 30), i.D("ValorCatastroSuelo"), i.D("ValorCatastroInmueble"), i.D("ValorCatastroTotal"),
                    i.D("PorcentajeInmueble"), i.D("ValorCompra"), i.D("CosteCompra"), i.D("PrecioAlquiler"), i.F("FechaInicioAlquiler"), i.D("TributoMetropolitano"));

            if (!ajeno && i.D("PorcentajeAmortizacion") is decimal pct and > 0m and <= 1m)
                await EjecutarAsync(
                    "INSERT INTO PlanesAmortizacionActivo (IdActivo, FechaInicio, BaseAmortizable, ValorResidual, VidaUtilMeses, Metodo, EstaActivo, PorcentajeAnual) VALUES (?, ?, ?, 0, ?, 'LINEAL', True, ?)",
                    idActivo, i.F("FechaCompra") ?? ahora.Date, BaseAmortizable(i.D("CosteCompra"), i.D("ValorCompra"), i.D("PorcentajeInmueble")), VidaUtilMeses(pct), pct);
            else if (!ajeno && i.D("PorcentajeAmortizacion") is decimal fuera && fuera != 0m)
                Anotar(Fase6, "Inmuebles", clave, "PorcentajeAmortizacion", "Erp.PorcentajeFuera", Severidad.Advertencia,
                    $"El % de amortización {fuera} del inmueble {idErp} no está entre 0 y 1: queda sin plan de amortización.", fuera.ToString(CultureInfo.InvariantCulture));
            est.Cargadas++;
        }
        if (inversiones > 0)
            Anotar(Fase6, "Inmuebles", null, null, "Erp.SegundaFase", Severidad.Informativa,
                $"{inversiones} inversiones financieras (tipo 6) no se migran: irán en la segunda fase de activos (creative-lab/inversiones-financieras).");

        // Subcuentas ya generadas: se conservan las reales aunque no empiecen por el prefijo corregido.
        var estCu = Est(Fase6, "CuentasInmuebles");
        var filas = new List<object?[]>();
        foreach (var c in o.Tabla("CuentasInmuebles"))
        {
            estCu.Leidas++;
            var inm = c.E("IdInmueble") ?? 0;
            var linea = c.E("IdPlantilla") ?? 0;
            var clave = $"{inm}:{linea}";
            if (Mapa(TiposErp.Activo, Txt(inm)) is not int activo || !tipoDeActivo.TryGetValue(inm, out var idTipo)) { estCu.Excluidas++; continue; } // inversión o inmueble excluido
            if (!plantillaDe.TryGetValue((idTipo, linea), out var plantilla))
            { Excluir(Fase6, "CuentasInmuebles", clave, $"La cuenta {c.T("Cuenta")} del inmueble {inm} es de la línea {linea}, que su tipo de activo no tiene en la plantilla."); continue; }
            if (Identidad(c.T("Cuenta")) is not int identidad) { Excluir(Fase6, "CuentasInmuebles", clave, $"La cuenta {c.T("Cuenta")} del inmueble {inm} no está en el plan contable importado de ContaSol."); continue; }
            filas.Add([activo, plantilla, identidad, ahora]);
        }
        var unicas = filas.GroupBy(f => ((int)f[0]!, (int)f[1]!)).Select(g => g.First()).ToList();
        if (unicas.Count < filas.Count)
            Anotar(Fase6, "CuentasInmuebles", null, null, "Erp.Excluido", Severidad.Advertencia, $"{filas.Count - unicas.Count} cuentas repetidas (mismo activo y plantilla) no se migran: queda la primera.");
        estCu.Excluidas += filas.Count - unicas.Count;
        await MasivaAsync("SubcuentasActivo", ["IdActivo", "IdPlantillaSubcuentaActivo", "IdIdentidadContable", "FechaHoraCreacion"], unicas);
        estCu.Cargadas = unicas.Count;
    }
}
