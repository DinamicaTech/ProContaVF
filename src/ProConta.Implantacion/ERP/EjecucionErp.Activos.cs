using System.Globalization;
using ProConta.Implantacion.ContaSol;

namespace ProConta.Implantacion.ERP;

// Fase 6: Inmuebles → Activos (con su departamento y subdepartamento como centros de coste), PlantillasInmuebles →
// PlantillasCuentasActivo y CuentasInmuebles → CuentasActivo.
internal sealed partial class EjecucionErp
{
    private const string Fase6 = "6 activos";

    /// <summary>Tipo de inmueble del ERP → código de TiposActivo (los que no existen se crean).</summary>
    private static readonly Dictionary<int, (string Codigo, string Nombre, string Categoria, bool Amortizable)> TiposActivoErp = new()
    {
        [1] = ("INMUEBLE", "Inmueble", "INMUEBLE", true),
        [2] = ("LOCAL", "Local", "INMUEBLE", true),
        [3] = ("VEHICULO", "Vehículo", "MOVIL", true),
        [4] = ("TECNICO", "Activo técnico", "MATERIAL", true),
        [5] = ("PARKING", "Parking", "INMUEBLE", true),
        [6] = ("INVERSION", "Inversión financiera", "FINANCIERO", false),
        // Sin fila en TiposInmueble del ERP; nombres del propietario (2026-11-11 17:19).
        [7] = ("TURISTICO", "Piso turístico", "INMUEBLE", true),
        [9] = ("PARTICULAR", "Piso particular (Marta)", "INMUEBLE", true),
    };

    private async Task ActivosAsync()
    {
        // Tipos.
        var estTipos = Est(Fase6, "TiposInmueble");
        var tipos = (await ConsultarAsync("SELECT IdTipoActivo, Codigo, EsAmortizablePredeterminado FROM TiposActivo", f => (Id: f.Entero("IdTipoActivo"), Codigo: (f.Texto("Codigo") ?? "").Trim(), Amort: f.Logico("EsAmortizablePredeterminado"))))
            .ToDictionary(x => x.Codigo, x => (x.Id, x.Amort), StringComparer.OrdinalIgnoreCase);
        var tipoDe = new Dictionary<int, (int Id, bool Amort)>();
        foreach (var t in o.Tabla("TiposInmueble"))
        {
            estTipos.Leidas++;
            var idErp = t.E("IdTipo") ?? 0;
            var (codigo, nombre, categoria, amort) = TiposActivoErp.TryGetValue(idErp, out var x) ? x : ($"ERP{idErp}", t.T("Descripcion") ?? $"Tipo {idErp}", "MATERIAL", true);
            if (!tipos.TryGetValue(codigo, out var tipo))
            {
                tipo = (await InsertarAsync("INSERT INTO TiposActivo (Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo) VALUES (?, ?, ?, ?, True)", codigo, nombre, categoria, amort), amort);
                tipos[codigo] = tipo;
                Anotar(Fase6, "TiposInmueble", Txt(idErp), null, "Erp.TipoActivoCreado", Severidad.Informativa, $"He creado el tipo de activo {codigo} ({nombre}) para el tipo de inmueble {idErp} del ERP.");
            }
            tipoDe[idErp] = tipo;
            Mapear(TiposErp.TipoActivo, Txt(idErp), tipo.Id);
            estTipos.Cargadas++;
        }
        // Tipos usados por inmuebles o plantillas que no están en TiposInmueble: se crean para no perder sus activos.
        foreach (var idErp in o.Tabla("Inmuebles").Select(i => i.E("Tipo")).Concat(o.Tabla("PlantillasInmuebles").Select(p => p.E("Tipo"))).OfType<int>().Distinct().Where(t => !tipoDe.ContainsKey(t)).Order())
        {
            var conocido = TiposActivoErp.TryGetValue(idErp, out var x);
            var (codigo, nombre, categoria, amort) = conocido ? x : ($"ERP{idErp}", $"Tipo {idErp} del ERP", "INMUEBLE", true);
            if (!tipos.TryGetValue(codigo, out var tipo))
                tipos[codigo] = tipo = (await InsertarAsync("INSERT INTO TiposActivo (Codigo, Nombre, Categoria, EsAmortizablePredeterminado, EstaActivo) VALUES (?, ?, ?, ?, True)", codigo, nombre, categoria, amort), amort);
            else await EjecutarAsync("UPDATE TiposActivo SET Nombre = ? WHERE IdTipoActivo = ?", nombre, tipo.Id);
            tipoDe[idErp] = tipo;
            Mapear(TiposErp.TipoActivo, Txt(idErp), tipo.Id);
            Anotar(Fase6, "TiposInmueble", Txt(idErp), "IdTipo", conocido ? "Erp.TipoActivoCreado" : "Erp.TipoInmuebleSinDescripcion", conocido ? Severidad.Informativa : Severidad.Advertencia,
                $"El tipo de inmueble {idErp} no está en TiposInmueble del ERP: he creado el tipo de activo {codigo} «{nombre}»{(conocido ? "" : "; revisa su nombre")}.");
        }

        // Plantillas de cuentas: Id dentro de cada tipo; la subcuenta XXXXX genera una por activo.
        var estPl = Est(Fase6, "PlantillasInmuebles");
        var plantillas = (await ConsultarAsync("SELECT IdPlantillaCuentaActivo, IdTipoActivo, RolCuenta FROM PlantillasCuentasActivo", f => (Id: f.Entero("IdPlantillaCuentaActivo"), Tipo: f.Entero("IdTipoActivo"), Rol: (f.Texto("RolCuenta") ?? "").Trim())))
            .ToDictionary(x => (x.Tipo, x.Rol), x => x.Id);
        var plantillaDe = new Dictionary<(int Tipo, int Id), int>();
        foreach (var p in o.Tabla("PlantillasInmuebles"))
        {
            estPl.Leidas++;
            var tipoErp = p.E("Tipo") ?? 0;
            var idErp = p.E("Id") ?? 0;
            var clave = $"{tipoErp}:{idErp}";
            if (!tipoDe.TryGetValue(tipoErp, out var tipo)) { Excluir(Fase6, "PlantillasInmuebles", clave, $"La plantilla {idErp} es del tipo de inmueble {tipoErp}, que no existe en TiposInmueble."); continue; }
            var rol = $"P{idErp:00}";
            var nombre = ReglasErp.Recortar(p.T("Nombre"), 255) ?? rol;
            if (plantillas.TryGetValue((tipo.Id, rol), out var id))
                await EjecutarAsync("UPDATE PlantillasCuentasActivo SET PrefijoCuenta = ?, PlantillaNombre = ?, Orden = ?, EstaActiva = True WHERE IdPlantillaCuentaActivo = ?", p.T("SubCuenta") ?? "", nombre, (short)idErp, id);
            else plantillas[(tipo.Id, rol)] = id = await InsertarAsync(
                "INSERT INTO PlantillasCuentasActivo (IdTipoActivo, RolCuenta, PrefijoCuenta, PlantillaNombre, Orden, EsObligatoria, EstaActiva) VALUES (?, ?, ?, ?, ?, False, True)",
                tipo.Id, rol, p.T("SubCuenta") ?? "", nombre, (short)idErp);
            plantillaDe[(tipoErp, idErp)] = id;
            Mapear(TiposErp.PlantillaActivo, clave, id);
            estPl.Cargadas++;
        }

        // Activos.
        var est = Est(Fase6, "Inmuebles");
        var inmuebles = o.Tabla("Inmuebles");
        est.Leidas = inmuebles.Count;
        var codigos = (await ConsultarAsync("SELECT Codigo FROM Activos WHERE IdSociedad = ?", f => (f.Texto("Codigo") ?? "").Trim(), Sociedad)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var tipoInmueble = new Dictionary<int, int>();
        var ahora = Ahora;
        foreach (var i in inmuebles.OrderBy(i => i.E("Id")))
        {
            var idErp = i.E("Id") ?? 0;
            var clave = Txt(idErp);
            var tipoErp = i.E("Tipo") ?? 0;
            if (!tipoDe.TryGetValue(tipoErp, out var tipo)) { Excluir(Fase6, "Inmuebles", clave, $"El inmueble {idErp} es del tipo {tipoErp}, que no existe en TiposInmueble."); continue; }
            var codigo = ReglasErp.Recortar(i.T("Alias"), 30) ?? $"ERP{idErp}";
            if (codigos.Contains(codigo)) codigo = ReglasErp.Recortar($"{codigo}-{idErp}", 30)!;
            codigos.Add(codigo);
            var departamento = i.E("Departamento");
            var sub = i.E("SubDepartamento");
            int? idDep = departamento is > 0 ? Centro(ReglasErp.ClaveCentro(Txt(departamento))) : null;
            int? idSub = sub is > 0 ? Centro(ReglasErp.ClaveCentro(Txt(sub))) : null;
            if (departamento is > 0 && idDep is null) ClaveNula(Fase6, "Inmuebles", clave, "Departamento", departamento, $"El departamento {departamento} del inmueble {idErp} no es un centro de coste.");
            if (sub is > 0 && idSub is null) ClaveNula(Fase6, "Inmuebles", clave, "SubDepartamento", sub, $"El subdepartamento {sub} del inmueble {idErp} no es un centro de coste.");
            int? compra = i.E("IdFacturaCompra") is int fc and > 0 ? Mapa(TiposErp.Compra, Txt(fc)) : null;
            if (i.E("IdFacturaCompra") is int fc2 and > 0 && compra is null) ClaveNula(Fase6, "Inmuebles", clave, "IdFacturaCompra", fc2, $"La factura de compra {fc2} del inmueble {idErp} no se ha migrado.");
            var coste = i.D("CosteCompra") ?? i.D("ValorCompra") ?? 0m;
            var amortizado = i.Dec("ImporteAmortizado");
            string? D(string etiqueta, string campo, string formato = "0.00") => i.D(campo) is decimal v && v != 0m ? $"{etiqueta}: {v.ToString(formato, CultureInfo.GetCultureInfo("es-ES"))}" : null;
            var descripcion = ReglasErp.UnirNotas(
                i.T("RefCatastral") is { } rc ? $"Referencia catastral: {rc}" : null, D("Valor de compra", "ValorCompra"), D("Valor catastral del suelo", "ValorCatastroSuelo"),
                D("Valor catastral del inmueble", "ValorCatastroInmueble"), D("Valor catastral total", "ValorCatastroTotal"), D("% del inmueble", "PorcentajeInmueble"),
                D("% de amortización", "PorcentajeAmortizacion"), D("Precio del alquiler", "PrecioAlquiler"), i.F("FechaInicioAlquiler") is DateTime fa ? $"Inicio del alquiler: {fa:dd/MM/yyyy}" : null,
                D("Tributo metropolitano", "TributoMetropolitano"));
            var idActivo = await InsertarAsync(
                "INSERT INTO Activos (IdSociedad, IdTipoActivo, Codigo, Nombre, AliasActivo, Descripcion, FechaAdquisicion, FechaAlta, Estado, EsAmortizable, CosteAdquisicionActual, " +
                "AmortizacionAcumulada, DeterioroAcumulado, ValorContable, EstadoCalculo, IdDepartamento, IdSubDepartamento, IdFacturaCompra, ReferenciaMigracion, Version) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
                Sociedad, tipo.Id, codigo, ReglasErp.Recortar(i.T("Nombre"), 150) ?? codigo, ReglasErp.Recortar(i.T("Alias"), 100), descripcion, i.F("FechaCompra"), i.F("FechaCompra") ?? ahora.Date,
                (byte)1, tipo.Amort, coste, amortizado, 0m, coste - amortizado, (byte)0, idDep, idSub, compra, $"ERP:Inmuebles:{idErp}", 0);
            Mapear(TiposErp.Activo, clave, idActivo);
            tipoInmueble[idErp] = tipoErp;
            est.Cargadas++;
        }

        // Subcuentas ya generadas por activo y plantilla.
        var estCu = Est(Fase6, "CuentasInmuebles");
        var filas = new List<object?[]>();
        foreach (var c in o.Tabla("CuentasInmuebles"))
        {
            estCu.Leidas++;
            var inm = c.E("IdInmueble") ?? 0;
            var clave = $"{inm}:{c.E("IdPlantilla")}";
            if (Mapa(TiposErp.Activo, Txt(inm)) is not int activo) { Excluir(Fase6, "CuentasInmuebles", clave, $"La cuenta {c.T("Cuenta")} es de un inmueble ({inm}) que no se ha migrado."); continue; }
            if (!plantillaDe.TryGetValue((tipoInmueble[inm], c.E("IdPlantilla") ?? 0), out var plantilla))
            { Excluir(Fase6, "CuentasInmuebles", clave, $"La plantilla {c.E("IdPlantilla")} del tipo {tipoInmueble[inm]} de la cuenta {c.T("Cuenta")} no existe."); continue; }
            if (Identidad(c.T("Cuenta")) is not int identidad) { Excluir(Fase6, "CuentasInmuebles", clave, $"La cuenta {c.T("Cuenta")} del inmueble {inm} no está en el plan contable importado de ContaSol."); continue; }
            filas.Add([activo, plantilla, identidad, ahora]);
        }
        var unicas = filas.GroupBy(f => ((int)f[0]!, (int)f[1]!)).Select(g => g.First()).ToList();
        if (unicas.Count < filas.Count)
            Anotar(Fase6, "CuentasInmuebles", null, null, "Erp.Excluido", Severidad.Advertencia, $"{filas.Count - unicas.Count} cuentas repetidas (mismo activo y plantilla) no se migran: queda la primera.");
        estCu.Excluidas += filas.Count - unicas.Count;
        await MasivaAsync("CuentasActivo", ["IdActivo", "IdPlantillaCuentaActivo", "IdIdentidadContable", "FechaHoraCreacion"], unicas);
        estCu.Cargadas = unicas.Count;
    }
}
