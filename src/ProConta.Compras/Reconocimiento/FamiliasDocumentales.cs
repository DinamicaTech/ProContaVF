using System.Text.RegularExpressions;
using static ProConta.Compras.Reconocimiento.MotorReconocimientoFacturaCompra;

namespace ProConta.Compras.Reconocimiento;

/// <summary>
/// Reglas de familias documentales concretas del motor VB (proveedores con formatos especiales), separadas de las
/// reglas genéricas para que se vean y se prueben aparte. La familia se reconoce por un fragmento de la ruta
/// (normalmente la carpeta del proveedor); cada regla exige además su evidencia en el documento antes de cambiar
/// importes. Se conservan tal cual del motor VB para no perder el entrenamiento (compras/reconocimiento).
/// </summary>
internal static class FamiliasDocumentales
{
    /// <summary>La ruta contiene el segmento, sin distinguir mayúsculas.</summary>
    public static bool EsRutaDe(DocumentoFacturaExtraido documento, string segmento)
        => (documento.Ruta ?? string.Empty).Contains(segmento, StringComparison.OrdinalIgnoreCase);

    private static bool EsRutaDeAlguna(DocumentoFacturaExtraido documento, params string[] segmentos)
        => segmentos.Any(s => EsRutaDe(documento, s));

    /// <summary>Proveedores cuyo NIF está en una cabecera gráfica (bitmap): sin NIF leído, piden OCR para el NIF.</summary>
    public static bool NifEnCabeceraGrafica(DocumentoFacturaExtraido documento) => EsRutaDe(documento, "Lavandería Bellamar");

    /// <summary>Documentos escaneados donde, sin histórico del proveedor, es preferible dejar el número vacío.</summary>
    public static bool NumeroSoloConHistorico(DocumentoFacturaExtraido documento)
        => EsRutaDeAlguna(documento, "Ferromar", "NavalChicolino", "ABC");

    /// <summary>Familias donde el cierre textual («Total…») es más fiable que la reconciliación global.</summary>
    public static bool PriorizaTotalInequivoco(DocumentoFacturaExtraido documento) => EsRutaDeAlguna(documento,
        "BañonYSanchez", "BanonYSanchez", "YSanchez", "DiscosDesbastePersianasAV312", "BateriaMakita", "Daspicuore-FabricaCortinas",
        "Duerto", "ebay", "KTuin", "ManoAMano", "PCComponentes", "PDFEscape", "PixarPrinting", "Registro Propiedad-Gava", "Renta4",
        "SuperAceUPDF", "Varios");

    /// <summary>Familias con un total visual claramente alineado, más fiable que el orden del texto.</summary>
    public static bool UsaTotalGeometrico(DocumentoFacturaExtraido documento)
        => EsRutaDeAlguna(documento, "AiguesBCN", "Iberdrola", "Naturgy", "Repsol_llum", "Amazon");

    /// <summary>Importes de las familias que los resuelven por su cuenta. Devuelve true si ya los ha fijado.</summary>
    public static bool ImportesPorFamilia(DocumentoFacturaExtraido documento, string text, string[] lines, ResultadoReconocimientoFacturaCompra result)
    {
        if (EsRutaDe(documento, "YSanchez"))
        {
            // Bañón y Sánchez: líneas con seis decimales, base con cuatro y total con dos.
            var lineaResumen = lines.FirstOrDefault(l => QuitarDiacriticos(l).Contains("BASE IMPONIBLE", StringComparison.OrdinalIgnoreCase) &&
                                                         QuitarDiacriticos(l).Contains("TOTAL", StringComparison.OrdinalIgnoreCase));
            var importesResumen = ExtraerImportesMonetarios(lineaResumen ?? string.Empty);
            if (importesResumen.Count >= 3)
            {
                result.BaseImponible = importesResumen[0];
                result.CuotaIva = importesResumen[^2];
                result.ImporteTotal = importesResumen[^1];
                result.ConfianzaImportes = 0.96m;
                return true;
            }
            var importesBanon = ExtraerImportes(text).Distinct().ToArray();
            var pareja = (from @base in importesBanon
                          let totalCalculado = Redondear(@base * 1.21m)
                          from total in importesBanon
                          where @base > 0m && total > @base && Math.Abs(total - totalCalculado) <= 0.02m
                          orderby total
                          select (Base: @base, Total: total)).FirstOrDefault();
            if (pareja.Total != 0m)
            {
                result.BaseImponible = pareja.Base;
                result.CuotaIva = pareja.Total - pareja.Base;
                result.ImporteTotal = pareja.Total;
                result.ConfianzaImportes = 0.92m;
                return true;
            }
        }

        if (EsRutaDeAlguna(documento, "ManoAMano", "PDFEscape", "PixarPrinting"))
        {
            var importesEspeciales = ExtraerImportes(text);
            if (importesEspeciales.Count > 0)
            {
                result.ImporteTotal = importesEspeciales.OrderByDescending(i => Math.Abs(i)).First();
                result.ConfianzaImportes = 0.88m;
                return true;
            }
        }

        if (EsRutaDe(documento, "Booking"))
        {
            var comisionTotal = BuscarImporteEtiquetado(lines, ["comision total"]);
            if (comisionTotal.HasValue)
            {
                result.ImporteTotal = comisionTotal;
                result.BaseImponible = comisionTotal;
                result.CuotaIva = 0m;
                result.ConfianzaImportes = 0.96m;
                return true;
            }
        }

        if (EsRutaDe(documento, "LIDL"))
        {
            var resumenLidl = BuscarResumenLidl(documento);
            if (resumenLidl.HasValue)
            {
                result.BaseImponible = resumenLidl.Value.Base;
                result.CuotaIva = resumenLidl.Value.Cuota;
                result.ImporteTotal = resumenLidl.Value.Total;
                result.ConfianzaImportes = 0.97m;
                return true;
            }
        }

        if (EsRutaDe(documento, "TheOptionNetworking"))
        {
            var totalOption = BuscarImporteEtiquetado(lines, ["total"]);
            if (totalOption.HasValue)
            {
                result.ImporteTotal = totalOption;
                result.ConfianzaImportes = 0.96m;
                return true;
            }
        }

        if (EsRutaDeAlguna(documento, "NavalChicolino", "AluminiosCasas"))
        {
            var importesPrimeraPagina = ExtraerImportes(documento.Paginas.FirstOrDefault()?.Texto ?? string.Empty);
            if (importesPrimeraPagina.Count > 0)
            {
                result.ImporteTotal = importesPrimeraPagina.OrderByDescending(i => Math.Abs(i)).First();
                result.ConfianzaImportes = 0.84m;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Base y cuota por familia, sin tocar el total ya reconocido (tal cual el CompletarBaseEiva del motor VB).
    /// Devuelve true cuando la familia ha decidido (aunque deje base y cuota vacías).
    /// </summary>
    public static bool CompletarBaseEIvaPorFamilia(DocumentoFacturaExtraido documento, string texto, ResultadoReconocimientoFacturaCompra resultado,
        ContextoReconocimientoFacturaCompra contexto, ICollection<string> warnings)
    {
        var total = resultado.ImporteTotal!.Value;

        if (EsRutaDe(documento, "KTuin"))
        {
            // KTuin no desglosa base e IVA: se deja para revisión.
            resultado.BaseImponible = null;
            resultado.CuotaIva = null;
            return true;
        }

        if (EsOperacionIntracomunitariaConVies(documento, contexto))
        {
            resultado.BaseImponible = total;
            resultado.CuotaIva = 0m;
            warnings.Add("Proveedor comunitario y sociedad inscrita en VIES: se contabiliza BI = Total e IVA = 0; revise el IVA mostrado por el emisor.");
            return true;
        }

        if (EsRutaDe(documento, "Airbnb"))
        {
            resultado.BaseImponible = total;
            resultado.CuotaIva = 0m;
            return true;
        }

        if (EsRutaDe(documento, "AiguesBCN"))
        {
            var cuotaAgua = BuscarCuotaAigues(texto);
            if (cuotaAgua.HasValue) AsignarBaseDesdeCuota(resultado, cuotaAgua.Value);
            return true;
        }

        if (EsRutaDe(documento, "Movistar"))
        {
            var cuotaMovistar = BuscarCuotaSobreBase(texto);
            if (cuotaMovistar.HasValue) AsignarBaseDesdeCuota(resultado, cuotaMovistar.Value);
            return true;
        }

        if (EsRutaDe(documento, "Naturgy"))
        {
            var tipoFactura = BuscarTipoIvaPrincipal(texto, documento);
            if (tipoFactura.HasValue) AsignarBaseDesdeTipo(resultado, tipoFactura.Value);
            return true;
        }

        if (EsRutaDe(documento, "Duerto"))
        {
            var cuotaExplicita = BuscarImporteEtiquetadoMonetario(texto.Split('\n', StringSplitOptions.RemoveEmptyEntries), ["iva 21%", "iva 21 %"]);
            if (cuotaExplicita.HasValue)
            {
                AsignarBaseDesdeCuota(resultado, cuotaExplicita.Value);
                return true;
            }
        }

        var lineas = texto.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        decimal? baseFormatoEspecial = null;
        if (EsRutaDe(documento, "LG"))
        {
            foreach (var pagina in documento.Paginas)
            {
                var etiquetaSubtotal = pagina.Palabras.FirstOrDefault(p => QuitarDiacriticos(p.Texto).Equals("Subtotal", StringComparison.OrdinalIgnoreCase));
                if (etiquetaSubtotal is null) continue;
                baseFormatoEspecial = pagina.Palabras
                    .Where(p => p.X > etiquetaSubtotal.X && Math.Abs(p.Y - etiquetaSubtotal.Y) <= 6d)
                    .OrderBy(p => p.X)
                    .Select(p => InterpretarImporte(p.Texto))
                    .FirstOrDefault(i => i.HasValue);
                if (baseFormatoEspecial.HasValue) break;
            }
            var baseEsperada21 = total / 1.21m;
            var candidata21 = ExtraerImportes(texto)
                .Where(i => Math.Abs(i - baseEsperada21) <= 0.02m)
                .OrderBy(i => Math.Abs(i - baseEsperada21))
                .FirstOrDefault();
            if (candidata21 != 0m) baseFormatoEspecial = candidata21;
        }
        else if (EsRutaDe(documento, "PCComponentes"))
        {
            var indiceCabecera = Array.FindIndex(lineas, l => QuitarDiacriticos(l).Contains("Base Imponible", StringComparison.OrdinalIgnoreCase));
            if (indiceCabecera >= 0 && indiceCabecera + 1 < lineas.Length)
            {
                var importes = ExtraerImportes(lineas[indiceCabecera + 1]);
                baseFormatoEspecial = importes.Count > 0 ? importes[0] : 0m;
            }
        }
        else if (EsRutaDe(documento, "Renta4"))
        {
            var coincidenciaBase = Regex.Match(texto, @"BASE\s*\(EUR\)\s*:\s*([-+]?[0-9]+(?:[,.][0-9]+)?)", RegexOptions.IgnoreCase);
            if (coincidenciaBase.Success) baseFormatoEspecial = InterpretarNumeroFlexible(coincidenciaBase.Groups[1].Value);
        }
        if (baseFormatoEspecial.HasValue && baseFormatoEspecial.Value != 0m)
        {
            resultado.BaseImponible = baseFormatoEspecial;
            resultado.CuotaIva = total - baseFormatoEspecial.Value;
            return true;
        }
        var baseExplicita = BuscarImporteEtiquetadoMonetario(lineas, ["total (imp. excl", "total imp. excl", "base imponible", "base (eur)", "importe total"]);
        if (baseExplicita.HasValue &&
            EsRutaDeAlguna(documento, "Daspicuore-FabricaCortinas", "LG", "PCComponentes", "Renta4", "EnfriadorAgua", "Iberdrola"))
        {
            resultado.BaseImponible = baseExplicita;
            resultado.CuotaIva = total - baseExplicita.Value;
            return true;
        }

        if (EsRutaDeAlguna(documento, "PixarPrinting", "AbonoYouGet"))
        {
            resultado.BaseImponible = total;
            resultado.CuotaIva = 0m;
            return true;
        }

        if (resultado.BaseImponible.HasValue && resultado.CuotaIva.HasValue) return true;

        if (EsRutaDe(documento, "Amazon"))
        {
            if (Regex.IsMatch(QuitarDiacriticos(texto), @"total\s+iva\s+(?:€\s*)?0(?:[,.]0+)?", RegexOptions.IgnoreCase))
            {
                AsignarBaseDesdeTipo(resultado, 0m);
                return true;
            }
            AsignarBaseDesdeTipo(resultado, BuscarTipoIvaPrincipal(texto, documento) ?? 0m);
            return true;
        }

        if (EsRutaDeAlguna(documento, "AluminiosCasas", "Iberdrola", "NavalChicolino", "Repsol_llum", "TheOptionNetworking"))
        {
            var tipo = BuscarTipoIvaPrincipal(texto, documento);
            if (tipo.HasValue) AsignarBaseDesdeTipo(resultado, tipo.Value);
        }
        return true;
    }

    /// <summary>Factura comunitaria que se contabiliza por inversión del sujeto pasivo (sociedad con VIES y familia configurada).</summary>
    private static bool EsOperacionIntracomunitariaConVies(DocumentoFacturaExtraido documento, ContextoReconocimientoFacturaCompra contexto)
        => contexto.SociedadTieneVies &&
           EsRutaDeAlguna(documento, "Eminza", "LightRoom", "VRBO", "20250114TornillosPersianasAV312", "20250726RadioCoche");
}
