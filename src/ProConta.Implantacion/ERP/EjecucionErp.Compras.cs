using System.Globalization;
using ProConta.Implantacion.ContaSol;
using ProConta.Nucleo.Identificacion;
using ProConta.Nucleo.Importes;

namespace ProConta.Implantacion.ERP;

// Fase 4: facturas de compra con sus desgloses, proveedor, PDF relativo a la raíz, asiento y libro F_IVS; valores por defecto
// del proveedor según su última factura y textos clave (PalabrasClaveTipoGasto, PalabrasClaveSubDepartamento).
internal sealed partial class EjecucionErp
{
    private const string Fase4 = "4 compras";

    /// <summary>Columnas del ERP que no se migran (implantacion/migracion-erp, bloque 14): calculadas u obsoletas.</summary>
    private static readonly HashSet<string> NoMigradasCompra = new(StringComparer.OrdinalIgnoreCase)
    {
        "IdRegistro", "ArchivoOriginal", "Duplicada", "Carpeta", "NombreFichero", "IVASuplido", "IdRegistroFacturaImportacionSuplido", "IdRegistroFacturaTransporte",
        "TextoAsiento", "ASIAPURectificativa", "IdRegistroIVARectificativa", "LineaN43", "IdRegistroIVA", "IdSociedad",
    };

    /// <summary>Columnas que el mapa nombra: se migran aunque tengan el mismo valor en todas las filas (criterio general 1).</summary>
    private static readonly HashSet<string> NombradasCompra = new(StringComparer.OrdinalIgnoreCase)
    {
        "ImporteBI0", "ImporteBI1", "ImporteBI2", "ImporteBI3", "ImporteBI4", "TipoIVA1", "TipoIVA2", "TipoIVA3", "TipoIVA4", "Archivo", "DIAAPU", "FECAPU", "ASIAPU",
        "ImporteDolares", "TipoOperacion", "TipoProducto", "TipoRetencion", "Exportada", "FechaHoraExportacion", "Sociedad", "Ejercicio", "CuentaContableProveedor",
        "NumeroFactura", "Fecha", "ImporteBI", "ImporteIVA", "ImporteTotal", "NIF", "MD5",
    };

    private async Task ComprasAsync()
    {
        var est = Est(Fase4, "FacturasCompra");
        var estDesgloses = Est(Fase4, "DesglosesFacturaCompra (de ImporteBI0-4)");
        var compras = o.Tabla("FacturasCompra");
        est.Leidas = compras.Count;
        var destino = (await _bd.EjecutarAsync(s => Task.FromResult(s.LeerEsquema().Tabla("FacturasCompra")!)));
        var columnasDestino = destino.Columnas.ToDictionary(c => c.Nombre, c => c, StringComparer.OrdinalIgnoreCase);
        var constantes = ReglasErp.CamposConstantes(compras).Where(c => !NombradasCompra.Contains(c) && columnasDestino.TryGetValue(c, out var col) && col.AdmiteNulos).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (constantes.Count > 0)
            Anotar(Fase4, "FacturasCompra", null, string.Join(", ", constantes), "Erp.CamposConstantes", Severidad.Informativa,
                $"No migro los campos con el mismo valor en todas las facturas (criterio general 1): {string.Join(", ", constantes)}.");
        var copiar = compras.Count == 0 ? [] : compras[0].Campos.Where(c => columnasDestino.ContainsKey(c) && !NoMigradasCompra.Contains(c) && !constantes.Contains(c)).ToList();

        var porNif = (await ConsultarAsync("SELECT p.IdProveedor, t.NIF FROM Proveedores AS p INNER JOIN Terceros AS t ON p.IdTercero = t.IdTercero WHERE t.NIF IS NOT NULL",
                f => (Id: f.Entero("IdProveedor"), Nif: Nif.Normalizar(f.Texto("NIF")))))
            .Where(x => x.Nif.Length > 0).GroupBy(x => x.Nif).Where(g => g.Select(x => x.Id).Distinct().Count() == 1).ToDictionary(g => g.Key, g => g.First().Id);

        var carpetasErp = await CarpetasComprasAsync();
        var preparadas = new List<(Fila Erp, object?[] Valores, List<object?[]> Desgloses, int? Proveedor)>();
        var campos = copiar.Concat(["IdSociedadEjercicio", "IdProveedor", "IdRegistroIVA", "IdAsientoContable", "IdCentroCoste", "Divisa", "ImporteTotalDivisa", "Duplicada",
            "RequiereRevisionImportacion", "FuenteImportacion", "Version"]).ToList();
        foreach (var c in compras.OrderBy(c => c.E("IdRegistro")))
        {
            cancelacion.ThrowIfCancellationRequested();
            var idErp = c.E("IdRegistro") ?? 0;
            var clave = Txt(idErp);
            var anio = c.E("Ejercicio") ?? c.F("Fecha")?.Year ?? 0;
            if (c.T("Sociedad") != Sociedad) { Excluir(Fase4, "FacturasCompra", clave, $"La factura de compra {idErp} es de la sociedad {c.T("Sociedad")}, que no se migra."); continue; }
            if (!_ejercicios.TryGetValue(anio, out var ej)) { Excluir(Fase4, "FacturasCompra", clave, $"El ejercicio {anio} de la factura de compra {idErp} no existe en ProContaVF."); continue; }

            var valores = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var col in copiar) valores[col] = c.Valor(col);
            // Ruta del PDF relativa a la carpeta de compras del ERP de su ejercicio (compras/factura-compra 2026-11-11 17:33).
            if (c.T("Archivo") is { } archivo)
            {
                var carpetaErp = carpetasErp.GetValueOrDefault(anio);
                var relativa = carpetaErp is null ? null : ReglasErp.RutaRelativa(archivo, carpetaErp);
                if (relativa is null)
                {
                    Anotar(Fase4, "FacturasCompra", clave, "Archivo", "Erp.RutaFueraDeCarpeta", Severidad.Advertencia,
                        $"El PDF de la factura de compra {idErp} no está en la carpeta de compras del ERP de {anio} ({carpetaErp ?? "sin carpeta"}): conservo la ruta completa.", archivo);
                    Est(Fase4, "FacturasCompra (PDF con ruta completa)").Cargadas++;
                }
                else Est(Fase4, "FacturasCompra (PDF con ruta relativa)").Cargadas++;
                valores["Archivo"] = relativa ?? archivo;
            }
            // Proveedor por su cuenta (mapa de ContaSol) o, si no, por el NIF.
            var cuentaProv = c.T("CuentaContableProveedor");
            int? proveedor = null;
            if (cuentaProv is not null)
                foreach (var a in _ejercicios.Keys.OrderByDescending(a => a == anio ? int.MaxValue : a))
                    if (MapaCs(a, TiposMapa.Proveedor).TryGetValue(cuentaProv, out var idp)) { proveedor = idp; break; }
            if (proveedor is null && Nif.Normalizar(c.T("NIF")) is { Length: > 0 } nif && porNif.TryGetValue(nif, out var porElNif)) proveedor = porElNif;
            if (proveedor is null)
                ClaveNula(Fase4, "FacturasCompra", clave, "CuentaContableProveedor", cuentaProv ?? c.T("NIF"), $"No encuentro el proveedor {cuentaProv} (NIF {c.T("NIF")}) de la factura de compra {idErp}: queda sin proveedor.");
            // Asiento (DIAAPU+ASIAPU del ejercicio) y registro del libro (F_IVS).
            int? asiento = null;
            if (c.E("ASIAPU") is int asi and > 0)
            {
                asiento = AsientoCs(anio, c.E("DIAAPU"), asi);
                if (asiento is null) ClaveNula(Fase4, "FacturasCompra", clave, "ASIAPU", $"{c.E("DIAAPU") ?? 1}:{asi}", $"El asiento {asi} de {anio} de la factura de compra {idErp} no está en lo importado de ContaSol.");
            }
            int? registro = null;
            if (c.E("IdRegistroIVA") is int iva and > 0)
            {
                registro = MapaCs(anio, TiposMapa.IvaSoportado).TryGetValue(Txt(iva), out var ir) ? ir : null;
                if (registro is null)
                    ClaveNula(Fase4, "FacturasCompra", clave, "IdRegistroIVA", iva, o.RegistrosSoportado.Contains((anio, iva))
                        ? $"El registro {iva} de F_IVS {anio} de la factura de compra {idErp} no está en lo importado de ContaSol."
                        : $"El registro {iva} no está en F_IVS {anio}: la factura de compra {idErp} queda sin registro del libro.");
            }
            var centroClave = ReglasErp.ClaveCentro(c.E("IdDepartamento"), c.E("IdSubDepartamento"));
            var centro = Centro(centroClave);
            if (centroClave is not null && centro is null)
                ClaveNula(Fase4, "FacturasCompra", clave, "IdSubDepartamento", centroClave, $"El centro {centroClave} de la factura de compra {idErp} no existe.");
            if (valores.ContainsKey("TipoRetencion"))
            {
                var tr = c.E("TipoRetencion");
                valores["TipoRetencion"] = ReglasErp.ClaseRetencionCompra(tr);
                if (tr is int x and not 0 and not 1 and not 5)
                    Anotar(Fase4, "FacturasCompra", clave, "TipoRetencion", "Erp.RetencionDesconocida", Severidad.Advertencia, $"Tipo de retención {x} desconocido en la factura de compra {idErp}: queda sin clase.");
            }
            if (valores.ContainsKey("TipoProducto")) valores["TipoProducto"] = c.E("TipoProducto") is int tp and (1 or 2) ? tp : null;
            if (valores.ContainsKey("TipoOperacion")) valores["TipoOperacion"] = c.E("TipoOperacion") ?? 0;
            if (valores.ContainsKey("FechaVencimiento")) valores["FechaVencimiento"] = c.T("FechaVencimiento");
            var dolares = c.Dec("ImporteDolares");
            var fila = copiar.Select(col => valores[col]).Concat(new object?[]
            {
                ej.Id, proveedor, registro, asiento, centro, dolares != 0m ? "USD" : "EUR", dolares != 0m ? dolares : c.D("ImporteTotal"), false, false, "ERP", 0,
            }).ToArray();
            // Desgloses: ImporteBI0 (exento) e ImporteBI1-4 con su TipoIVA1-4.
            var desgloses = new List<object?[]>();
            var orden = 1;
            for (var i = 0; i <= 4; i++)
            {
                var b = c.Dec($"ImporteBI{i}");
                if (b == 0m) continue;
                var tipo = i == 0 ? 0m : c.Dec($"TipoIVA{i}");
                if (tipo is > 0m and < 1m) tipo = Math.Round(tipo * 100m, 2); // algunos ejercicios guardan el tipo como fracción (0,21)
                desgloses.Add([null, (short)orden++, tipo, b, Importe.RedondearValor(b * tipo / 100m), null, null]);
            }
            var cuota = desgloses.Sum(d => (decimal)d[4]!);
            if (desgloses.Count > 0 && Math.Abs(cuota - c.Dec("ImporteIVA")) > 0.02m)
                Anotar(Fase4, "FacturasCompra", clave, "ImporteIVA", "Erp.CuotaDistinta", Severidad.Informativa,
                    $"La factura de compra {idErp} tiene {c.Dec("ImporteIVA"):0.00} € de IVA y sus desgloses suman {cuota:0.00} €.");
            preparadas.Add((c, fila, desgloses, proveedor));
        }
        var ids = await MasivaAsync("FacturasCompra", [.. campos], preparadas.Select(p => p.Valores).ToList(), "IdRegistro");
        var desglosesFilas = new List<object?[]>();
        for (var i = 0; i < ids.Count; i++)
        {
            Mapear(TiposErp.Compra, Txt(preparadas[i].Erp.E("IdRegistro")), ids[i]);
            foreach (var d in preparadas[i].Desgloses) { d[0] = ids[i]; desglosesFilas.Add(d); }
        }
        await MasivaAsync("DesglosesFacturaCompra", ["IdRegistro", "Orden", "TipoIVA", "BaseImponible", "CuotaIVA", "Confianza", "Evidencia"], desglosesFilas);
        est.Cargadas = ids.Count;
        estDesgloses.Cargadas = desglosesFilas.Count;

        await DefectosProveedorAsync(preparadas.Where(p => p.Proveedor is not null).Select(p => (p.Erp, p.Proveedor!.Value)).ToList());
        await TextosClaveAsync();
    }

    /// <summary>
    /// Carpeta de compras del ERP de cada ejercicio (Sociedades.RutaFacturasAExportar, p. ej. D:\SynologyBox\2026\Proveedores) y el
    /// parámetro nuevo de cada ejercicio: esa carpeta relativa a la raíz de documentos (2026\Proveedores). La carpeta de documentos
    /// de la empresa no la pone la migración: la pone el propietario.
    /// </summary>
    private async Task<Dictionary<int, string>> CarpetasComprasAsync()
    {
        var r = new Dictionary<int, string>();
        var est = Est(Fase4, "Sociedades (carpeta de compras del ejercicio)");
        foreach (var s in o.Tabla("Sociedades").Where(s => s.B("Activa") && s.T("Codigo") == Sociedad))
        {
            if (s.E("Ejercicio") is not int anio || s.T("RutaFacturasAExportar") is not { } carpeta) continue;
            est.Leidas++;
            r[anio] = carpeta.TrimEnd('\\', '/');
            if (!_ejercicios.TryGetValue(anio, out var ej)) continue;
            var relativa = ReglasErp.RutaRelativa(carpeta.TrimEnd('\\', '/'), op.RaizDocumentos);
            if (relativa is null)
            {
                Anotar(Fase4, "Sociedades", Txt(anio), "RutaFacturasAExportar", "Erp.CarpetaFueraDeRaiz", Severidad.Advertencia,
                    $"La carpeta de compras del ERP de {anio} ({carpeta}) no está bajo {op.RaizDocumentos}: dejo el parámetro del ejercicio por defecto ({anio}\\Proveedores).", carpeta);
                continue;
            }
            await EjecutarAsync("UPDATE SociedadesEjercicios SET CarpetaFacturasCompra = ? WHERE IdSociedadEjercicio = ?", relativa, ej.Id);
            est.Cargadas++;
        }
        return r;
    }

    /// <summary>Producto, cuenta de gasto y centro por defecto del proveedor: los de su última factura (compras 2026-11-06).</summary>
    private async Task DefectosProveedorAsync(IReadOnlyList<(Fila Erp, int Proveedor)> compras)
    {
        var est = Est(Fase4, "CuentasProveedoresSociedad (valores por defecto)");
        foreach (var g in compras.GroupBy(c => c.Proveedor))
        {
            var ultima = g.OrderBy(c => c.Erp.F("Fecha")).ThenBy(c => c.Erp.E("IdRegistro")).Last().Erp;
            var producto = ultima.E("TipoProducto") is int tp and (1 or 2) ? (byte?)tp : null;
            var gasto = Identidad(ultima.T("ConceptoGasto"));
            var centro = Centro(ReglasErp.ClaveCentro(ultima.E("IdDepartamento"), ultima.E("IdSubDepartamento")));
            var n = await EjecutarAsync("UPDATE CuentasProveedoresSociedad SET TipoProductoDefecto = ?, IdIdentidadGastoDefecto = ?, IdCentroCosteDefecto = ? WHERE IdProveedor = ? AND IdSociedad = ?",
                producto, gasto, centro, g.Key, Sociedad);
            est.Leidas++;
            if (n > 0) est.Cargadas++;
        }
    }

    /// <summary>TextosClaveCompra: un mismo texto en las dos tablas es una sola fila con la cuenta y el centro.</summary>
    private async Task TextosClaveAsync()
    {
        var estGasto = Est(Fase4, "PalabrasClaveTipoGasto");
        var estSub = Est(Fase4, "PalabrasClaveSubDepartamento");
        var textos = new Dictionary<string, (int? Identidad, int? Centro)>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in o.Tabla("PalabrasClaveTipoGasto"))
        {
            estGasto.Leidas++;
            if (ReglasErp.Recortar(p.T("PalabraClave"), 255) is not { } texto) { Excluir(Fase4, "PalabrasClaveTipoGasto", p.T("TipoGasto") ?? "", "Palabra clave vacía."); continue; }
            var identidad = Identidad(p.T("TipoGasto"));
            if (identidad is null) ClaveNula(Fase4, "PalabrasClaveTipoGasto", texto, "TipoGasto", p.T("TipoGasto"), $"La cuenta de gasto {p.T("TipoGasto")} del texto «{texto}» no existe.");
            textos[texto] = (identidad, textos.GetValueOrDefault(texto).Centro);
            estGasto.Cargadas++;
        }
        foreach (var p in o.Tabla("PalabrasClaveSubDepartamento"))
        {
            estSub.Leidas++;
            if (ReglasErp.Recortar(p.T("PalabraClave"), 255) is not { } texto) { Excluir(Fase4, "PalabrasClaveSubDepartamento", Txt(p.E("IdSubDepartamento")), "Palabra clave vacía."); continue; }
            var clave = ReglasErp.ClaveCentro(p.E("IdDepartamento"), p.E("IdSubDepartamento"));
            var centro = Centro(clave);
            if (centro is null) ClaveNula(Fase4, "PalabrasClaveSubDepartamento", texto, "IdSubDepartamento", clave, $"El centro {clave} del texto «{texto}» no existe.");
            textos[texto] = (textos.GetValueOrDefault(texto).Identidad, centro);
            estSub.Cargadas++;
        }
        var existentes = (await ConsultarAsync("SELECT IdTextoClave, Texto FROM TextosClaveCompra WHERE IdSociedad = ?", f => (Id: f.Entero("IdTextoClave"), Texto: (f.Texto("Texto") ?? "").Trim()), Sociedad))
            .GroupBy(x => x.Texto, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);
        await EnLotesAsync(textos, async (s, t) =>
        {
            if (existentes.TryGetValue(t.Key, out var id))
                await s.EjecutarAsync("UPDATE TextosClaveCompra SET IdIdentidadContable = ?, IdCentroCoste = ? WHERE IdTextoClave = ?", t.Value.Identidad, t.Value.Centro, id);
            else
            {
                await s.EjecutarAsync("INSERT INTO TextosClaveCompra (IdSociedad, Texto, IdIdentidadContable, IdCentroCoste) VALUES (?, ?, ?, ?)", Sociedad, t.Key, t.Value.Identidad, t.Value.Centro);
                id = await s.UltimoIdAsync();
            }
            Mapear(TiposErp.TextoClave, t.Key, id);
        });
        Est(Fase4, "TextosClaveCompra (destino)").Cargadas = textos.Count;
    }
}
