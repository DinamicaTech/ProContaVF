using System.Globalization;
using System.Xml.Linq;

namespace ProConta.Contabilidad.CuentasAnuales;

/// <summary>
/// DEPOSITO.XBRL de las cuentas anuales del modelo pymes (contabilidad/procesos/cuentas-anuales): el balance y la cuenta de pérdidas y
/// ganancias del ejercicio y del anterior en XBRL 2.1 con la taxonomía PGC 2007 del ICAC (v1.7.1, 2025-01-01, esquema
/// pgc07-pymes-completo.xsd), como pide el anexo II de la Resolución de 26/05/2025, para importarlo en el programa D2 del Colegio de Registradores.
/// Los importes llevan el signo del modelo (lo que el modelo pone entre paréntesis, en negativo) y dos decimales. La hoja de identificación,
/// la aplicación del resultado, la plantilla y el resto de datos del depósito se completan en D2; la memoria va aparte como MEMORIA.PDF.
/// </summary>
public static class XbrlCuentasAnuales
{
    public const string Esquema = "https://www.icac.gob.es/sites/default/files/pgc2007/v171/pgc07-pymes-completo.xsd";
    public const string NombreFichero = "DEPOSITO.XBRL";

    /// <summary>Esquema del identificador de la entidad (su NIF) en los contextos.</summary>
    public const string EsquemaNif = "http://www.icac.meh.es/xbrl/nif";

    private static readonly XNamespace Xbrli = "http://www.xbrl.org/2003/instance";
    private static readonly XNamespace Link = "http://www.xbrl.org/2003/linkbase";
    private static readonly XNamespace XLink = "http://www.w3.org/1999/xlink";
    private static readonly XNamespace Iso4217 = "http://www.xbrl.org/2003/iso4217";

    /// <summary>Espacios de nombres de la taxonomía por prefijo.</summary>
    public static readonly IReadOnlyDictionary<string, XNamespace> Prefijos = new Dictionary<string, XNamespace>
    {
        ["pgc-07-c-bs"] = "http://www.icac.meh.es/es/fr/gaap/pgc07/comun-base/2025-01-01",
        ["pgc-07-c-ap"] = "http://www.icac.meh.es/es/fr/gaap/pgc07/comun-abreviadopymes/2025-01-01",
    };

    /// <summary>
    /// Elemento de la taxonomía de cada clave del modelo (sacado de las etiquetas «code» de la taxonomía). La 21350, «Reserva de capitalización»,
    /// no tiene elemento en la v1.7.1: si tuviera importe, no se puede exportar (no ocurre: la correspondencia no le lleva ninguna cuenta).
    /// </summary>
    public static readonly IReadOnlyDictionary<string, (string Prefijo, string Elemento)> Elementos = new Dictionary<string, (string, string)>
    {
        ["10000"] = ("pgc-07-c-bs", "TotalActivo"),
        ["11000"] = ("pgc-07-c-bs", "ActivoNoCorriente"),
        ["11100"] = ("pgc-07-c-bs", "ActivoNoCorrienteInmovilizadoIntangible"),
        ["11200"] = ("pgc-07-c-bs", "ActivoNoCorrienteInmovilizadoMaterial"),
        ["11300"] = ("pgc-07-c-bs", "ActivoNoCorrienteInversionesInmobiliarias"),
        ["11400"] = ("pgc-07-c-bs", "ActivoNoCorrienteInversionesEmpresasGrupoEmpresasAsociadasLargoPlazo"),
        ["11500"] = ("pgc-07-c-bs", "ActivoNoCorrienteInversionesFinancierasLargoPlazo"),
        ["11600"] = ("pgc-07-c-bs", "ActivoNoCorrienteActivosImpuestoDiferido"),
        ["11700"] = ("pgc-07-c-bs", "ActivoNoCorrienteDeudasComercialesNoCorriente"),
        ["12000"] = ("pgc-07-c-bs", "ActivoCorriente"),
        ["12200"] = ("pgc-07-c-bs", "ActivoCorrienteExistencias"),
        ["12300"] = ("pgc-07-c-bs", "ActivoCorrienteDeudoresComercialesOtrasCuentasCobrar"),
        ["12370"] = ("pgc-07-c-bs", "ActivoCorrienteDeudoresComercialesOtrasCuentasCobrarAccionistasDesembolsosExigidos"),
        ["12380"] = ("pgc-07-c-bs", "ActivoCorrienteDeudoresComercialesOtrasCuentasCobrarClientesVentasPrestacionesServicios"),
        ["12381"] = ("pgc-07-c-bs", "ActivoCorrienteDeudoresComercialesOtrasCuentasCobrarClientesVentasPrestacionesServiciosLargoPlazo"),
        ["12382"] = ("pgc-07-c-bs", "ActivoCorrienteDeudoresComercialesOtrasCuentasCobrarClientesVentasPrestacionesServiciosCortoPlazo"),
        ["12390"] = ("pgc-07-c-ap", "ActivoCorrienteDeudoresComercialesOtrasCuentasCobrarOtrosDeudores"),
        ["12400"] = ("pgc-07-c-bs", "ActivoCorrienteInversionesEmpresasGrupoEmpresasAsociadasCortoPlazo"),
        ["12500"] = ("pgc-07-c-bs", "ActivoCorrienteInversionesFinancierasCortoPlazo"),
        ["12600"] = ("pgc-07-c-bs", "ActivoCorrientePeriodificacionesCortoPlazo"),
        ["12700"] = ("pgc-07-c-bs", "ActivoCorrienteEfectivoOtrosActivosLiquidosEquivalentes"),
        ["20000"] = ("pgc-07-c-bs", "PatrimonioNeto"),
        ["21000"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropios"),
        ["21100"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosCapital"),
        ["21110"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosCapitalEscriturado"),
        ["21120"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosCapitalNoExigido"),
        ["21200"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosPrimaEmision"),
        ["21300"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosReservas"),
        ["21360"] = ("pgc-07-c-ap", "PatrimonioNetoFondosPropiosReservasOtrasReservas"),
        ["21400"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosAccionesParticipacionesPatrimonioPropias"),
        ["21500"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosResultadosEjerciciosAnteriores"),
        ["21600"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosOtrasAportacionesSocios"),
        ["21700"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosResultadoEjercicio"),
        ["21800"] = ("pgc-07-c-bs", "PatrimonioNetoFondosPropiosDividendoCuenta"),
        ["22000"] = ("pgc-07-c-bs", "PatrimonioNetoAjustesCambioValor"),
        ["23000"] = ("pgc-07-c-bs", "PatrimonioNetoSubvencionesDonacionesLegadosRecibidos"),
        ["30000"] = ("pgc-07-c-bs", "PatrimonioNetoPasivoTotal"),
        ["31000"] = ("pgc-07-c-bs", "PasivoNoCorriente"),
        ["31100"] = ("pgc-07-c-bs", "PasivoNoCorrienteProvisionesLargoPlazo"),
        ["31200"] = ("pgc-07-c-bs", "PasivoNoCorrienteDeudasLargoPlazo"),
        ["31220"] = ("pgc-07-c-bs", "PasivoNoCorrienteDeudasLargoPlazoDeudasEntidadesCredito"),
        ["31230"] = ("pgc-07-c-bs", "PasivoNoCorrienteDeudasLargoPlazoAcreedoresArrendamientoFinanciero"),
        ["31290"] = ("pgc-07-c-ap", "PasivoNoCorrienteDeudasLargoPlazoOtrasDeudas"),
        ["31300"] = ("pgc-07-c-bs", "PasivoNoCorrienteDeudasEmpresasGrupoEmpresasAsociadasLargoPlazo"),
        ["31400"] = ("pgc-07-c-bs", "PasivoNoCorrientePasivosImpuestoDiferido"),
        ["31500"] = ("pgc-07-c-bs", "PasivoNoCorrientePeriodificacionesLargoPlazo"),
        ["31600"] = ("pgc-07-c-bs", "PasivoNoCorrienteAcreedoresComercialesNoCorrientes"),
        ["31700"] = ("pgc-07-c-bs", "PasivoNoCorrienteDeudaCaracteristicasEspecialesLargoPlazo"),
        ["32000"] = ("pgc-07-c-bs", "PasivoCorriente"),
        ["32200"] = ("pgc-07-c-bs", "PasivoCorrienteProvisionesCortoPlazo"),
        ["32300"] = ("pgc-07-c-bs", "PasivoCorrienteDeudasCortoPlazo"),
        ["32320"] = ("pgc-07-c-bs", "PasivoCorrienteDeudasCortoPlazoDeudasEntidadesCredito"),
        ["32330"] = ("pgc-07-c-bs", "PasivoCorrienteDeudasCortoPlazoAcreedoresArrendamientoFinanciero"),
        ["32390"] = ("pgc-07-c-ap", "PasivoCorrienteDeudasCortoPlazoOtrasDeudas"),
        ["32400"] = ("pgc-07-c-bs", "PasivoCorrienteDeudasEmpresasGrupoEmpresasAsociadas"),
        ["32500"] = ("pgc-07-c-bs", "PasivoCorrienteAcreedoresComercialesOtrasCuentasPagar"),
        ["32580"] = ("pgc-07-c-bs", "PasivoCorrienteAcreedoresComercialesOtrasCuentasPagarProveedores"),
        ["32581"] = ("pgc-07-c-bs", "PasivoCorrienteAcreedoresComercialesOtrasCuentasPagarProveedoresLargoPlazo"),
        ["32582"] = ("pgc-07-c-bs", "PasivoCorrienteAcreedoresComercialesOtrasCuentasPagarProveedoresCortoPlazo"),
        ["32590"] = ("pgc-07-c-ap", "PasivoCorrienteAcreedoresComercialesOtrasCuentasPagarOtrosAcreedores"),
        ["32600"] = ("pgc-07-c-bs", "PasivoCorrientePeriodificacionesCortoPlazo"),
        ["32700"] = ("pgc-07-c-bs", "PasivoCorrienteDeudasCaracteristicasEspecialesCortoPlazo"),
        ["40100"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasImporteNetoCifraNegocios"),
        ["40200"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasVariacionExistenciasProductosTerminadosProductosCursoFabricacion"),
        ["40300"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasTrabajosRealizadosEmpresaActivo"),
        ["40400"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasAprovisionamientos"),
        ["40500"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasOtrosIngresosExplotacion"),
        ["40600"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasGestionPersonal"),
        ["40700"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasOtrosGastosExplotacion"),
        ["40800"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasAmortizacionInmovilizado"),
        ["40900"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasImputacionSubvencionesInmovilizadoNoFinancieroOtras"),
        ["41000"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasExcesosProvisiones"),
        ["41100"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasDeterioroResultadoEnajenacionesInmovilizado"),
        ["41300"] = ("pgc-07-c-bs", "PerdidasGananciasOtrosResultados"),
        ["41400"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasIngresosFinancieros"),
        ["41430"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasIngresosFinancierosImputacionSubvencionesDonacionesLegadosCaracterFinanciero"),
        ["41490"] = ("pgc-07-c-ap", "PerdidasGananciasOperacionesContinuadasIngresosFinancierosOtrosIngresosFinancieros"),
        ["41500"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasGastosFinancieros"),
        ["41600"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasVariacionValorRazonableInstrumentosFinancieros"),
        ["41700"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasDiferenciasCambio"),
        ["41800"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasDeterioroResultadoEnajenacionesInstrumentosFinancieros"),
        ["41900"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasImpuestosSobreBeneficios"),
        ["42100"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasOtrosIngresosGastosCaracterFinanciero"),
        ["42110"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasOtrosIngresosGastosCaracterFinancieroIncorporacionActivoGastosFinancieros"),
        ["42120"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasOtrosIngresosGastosCaracterFinancieroIngresosFinancierosDerivadosConveniosAcreedores"),
        ["42130"] = ("pgc-07-c-bs", "PerdidasGananciasOperacionesContinuadasOtrosIngresosGastosCaracterFinancieroRestoIngresosGastos"),
        ["49100"] = ("pgc-07-c-bs", "PerdidasGananciasResultadoExplotacion"),
        ["49200"] = ("pgc-07-c-bs", "PerdidasGananciasResultadoFinanciero"),
        ["49300"] = ("pgc-07-c-bs", "PerdidasGananciasResultadoAntesImpuestos"),
        ["49500"] = ("pgc-07-c-bs", "PerdidasGananciasResultadoEjercicio"),
    };

    /// <summary>El fichero XBRL de unas cuentas, en UTF-8. Lanza si hay errores en las cuentas o una partida con importe sin elemento.</summary>
    public static byte[] Generar(CuentasAnualesPymes c)
    {
        if (c.TieneErrores)
            throw new InvalidOperationException("Las cuentas tienen errores que el Registro no admitiría (se explican en la pantalla): corrígelos antes de exportarlas para D2.");
        var raiz = new XElement(Xbrli + "xbrl",
            new XAttribute(XNamespace.Xmlns + "xbrli", Xbrli), new XAttribute(XNamespace.Xmlns + "link", Link), new XAttribute(XNamespace.Xmlns + "xlink", XLink),
            new XAttribute(XNamespace.Xmlns + "iso4217", Iso4217),
            Prefijos.Select(p => new XAttribute(XNamespace.Xmlns + p.Key, p.Value)),
            new XElement(Link + "schemaRef", new XAttribute(XLink + "type", "simple"), new XAttribute(XLink + "href", Esquema)));
        var ejercicios = c.Anterior is null ? new[] { c.Actual } : new[] { c.Actual, c.Anterior };
        foreach (var e in ejercicios)
        {
            raiz.Add(Contexto(c.Nif, Id(e, "Balance"), null, new DateTime(e.Ejercicio, 12, 31)));
            raiz.Add(Contexto(c.Nif, Id(e, "PYG"), new DateTime(e.Ejercicio, 1, 1), new DateTime(e.Ejercicio, 12, 31)));
        }
        raiz.Add(new XElement(Xbrli + "unit", new XAttribute("id", "euro"), new XElement(Xbrli + "measure", "iso4217:EUR")));
        foreach (var p in ModeloPymes.Activo.Concat(ModeloPymes.PatrimonioNetoYPasivo).Concat(ModeloPymes.PerdidasYGanancias))
        {
            if (!Elementos.TryGetValue(p.Clave, out var el))
            {
                if (ejercicios.Any(e => e[p.Clave] != 0m))
                    throw new InvalidOperationException($"La partida {p.Texto} (clave {p.Clave}) tiene importe y la taxonomía del ICAC no tiene dónde ponerlo: lleva su importe a otra partida del modelo.");
                continue;
            }
            foreach (var e in ejercicios)
                raiz.Add(new XElement(Prefijos[el.Prefijo] + el.Elemento,
                    new XAttribute("contextRef", Id(e, p.Estado == EstadoCuentas.PerdidasYGanancias ? "PYG" : "Balance")),
                    new XAttribute("unitRef", "euro"), new XAttribute("decimals", "2"),
                    e[p.Clave].ToString("0.00", CultureInfo.InvariantCulture)));
        }
        var doc = new XDocument(new XDeclaration("1.0", "UTF-8", null), raiz);
        using var m = new MemoryStream();
        using (var w = System.Xml.XmlWriter.Create(m, new System.Xml.XmlWriterSettings { Indent = true, Encoding = new System.Text.UTF8Encoding(false) }))
            doc.Save(w);
        return m.ToArray();
    }

    private static string Id(CifrasEjercicio e, string tipo) => $"Y{e.Ejercicio}_{tipo}";

    private static XElement Contexto(string nif, string id, DateTime? desde, DateTime hasta) =>
        new(Xbrli + "context", new XAttribute("id", id),
            new XElement(Xbrli + "entity", new XElement(Xbrli + "identifier", new XAttribute("scheme", EsquemaNif), nif)),
            new XElement(Xbrli + "period", desde is DateTime d
                ? new object[] { new XElement(Xbrli + "startDate", d.ToString("yyyy-MM-dd")), new XElement(Xbrli + "endDate", hasta.ToString("yyyy-MM-dd")) }
                : new object[] { new XElement(Xbrli + "instant", hasta.ToString("yyyy-MM-dd")) }));
}
