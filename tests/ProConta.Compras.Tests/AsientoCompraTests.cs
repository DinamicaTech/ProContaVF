using ProConta.Compras.Facturas;
using ProConta.Contabilidad.Asientos;
using ProConta.Nucleo.Contexto;

namespace ProConta.Compras.Tests;

/// <summary>Asiento de una factura de compra sin BD (compras/factura-compra/contabilizacion).</summary>
public sealed class AsientoCompraTests
{
    private static readonly ContextoSociedadEjercicio Ctx = new("T01", 9001, 2026);
    private static readonly DateTime Hoy = new(2026, 11, 7);

    private static readonly CuentasCompra Cuentas = new("4100001", "6290000", 3,
        new Dictionary<decimal, string> { [21m] = "4720021", [10m] = "4720010", [0m] = "4720001" },
        Retencion: null, AutoSoportado: null, AutoRepercutido: null, Importacion: "4724001", Suplido: "5550010",
        RedondeoGastos: "6590000", RedondeoIngresos: "7590000", TipoGeneral: 21m);

    private static FacturaCompra Factura(decimal total, params (decimal Tipo, decimal Base, decimal Cuota)[] desgloses)
    {
        var f = new FacturaCompra
        {
            IdRegistro = 5, Ejercicio = Ctx, RutaPdf = @"X:\f.pdf", Md5 = new string('a', 32), IdProveedor = 7, CuentaContableProveedor = "4100001",
            NumeroFactura = "FV1", Fecha = new DateTime(2026, 10, 2), Concepto = "Ficticio S.L. Fact: FV1", ImporteTotal = total,
            BaseImponible = desgloses.Sum(d => d.Base), CuotaIva = desgloses.Sum(d => d.Cuota), CuentaGasto = "6290000", IdCentroCoste = 3
        };
        short orden = 1;
        foreach (var d in desgloses) f.Desgloses.Add(new DesgloseFacturaCompra(orden++, d.Tipo, d.Base, d.Cuota));
        return f;
    }

    private static IEnumerable<(string Cuenta, decimal Debe, decimal Haber)> Lineas(Asiento a) =>
        a.Apuntes.Select(x => (x.CodigoCuenta, x.Debe.Valor, x.Haber.Valor));

    [Fact]
    public void Nacional_con_dos_tipos_y_retencion_de_alquiler()
    {
        var f = Factura(157m, (21m, 100m, 21m), (10m, 50m, 5m));
        f.PorcentajeRetencion = 19m;
        f.ImporteRetencion = 19m;
        f.ClaseRetencion = ClaseRetencion.Arrendamientos;
        var p = AsientoCompra.Construir(f, Ctx, Cuentas with { Retencion = "4751000" }, Hoy)!;
        Assert.Equal(Hoy, p.Asiento.FechaContable);
        Assert.Equal(
            [("4100001", 0m, 157m), ("4751000", 0m, 19m), ("6290000", 100m, 0m), ("4720021", 21m, 0m), ("6290000", 50m, 0m), ("4720010", 5m, 0m)],
            Lineas(p.Asiento));
        Assert.All(p.Asiento.Apuntes.Where(a => a.CodigoCuenta == "6290000"), a => Assert.Equal(3, a.IdCentroCoste));
        Assert.Equal(7, p.Asiento.Apuntes[0].IdProveedor);
        Assert.Equal((150m, 26m, 0m), (p.Libro.Base, p.Libro.CuotaSoportada, p.Libro.CuotaAutorrepercutida));
        Assert.True(ReglasAsiento.ValidarYCuadrar(p.Asiento, PoliticaCuadre.Estricta).EsCorrecto);
    }

    [Fact]
    public void Inversion_del_sujeto_pasivo_autorrepercute_el_tipo_general_en_soportado_y_repercutido()
    {
        var f = Factura(1000m, (0m, 1000m, 0m));
        f.InversionSujetoPasivo = true;
        var p = AsientoCompra.Construir(f, Ctx, Cuentas with { AutoSoportado = "4721021", AutoRepercutido = "4771021" }, Hoy)!;
        Assert.Equal([("4100001", 0m, 1000m), ("6290000", 1000m, 0m), ("4721021", 210m, 0m), ("4771021", 0m, 210m)], Lineas(p.Asiento));
        Assert.Equal((1000m, 0m, 210m, 210m), (p.Libro.Base, p.Libro.CuotaSoportada, p.Libro.CuotaAutorrepercutida, p.Libro.CuotaDeducible));
    }

    [Fact]
    public void Actividad_sin_IVA_deducible_lleva_la_cuota_al_gasto_y_al_libro_sin_deducir()
    {
        var f = Factura(121m, (21m, 100m, 21m));
        var p = AsientoCompra.Construir(f, Ctx, Cuentas with { IvaNoDeducible = true }, Hoy)!;
        Assert.Equal([("4100001", 0m, 121m), ("6290000", 121m, 0m)], Lineas(p.Asiento));
        Assert.Equal((100m, 21m, 0m), (p.Libro.Base, p.Libro.CuotaSoportada, p.Libro.CuotaDeducible));
        Assert.Equal(0m, Assert.Single(p.Libro.Tramos).PorcentajeDeducible);
        Assert.True(ReglasAsiento.ValidarYCuadrar(p.Asiento, PoliticaCuadre.Estricta).EsCorrecto);
    }

    [Fact]
    public void Inversion_del_sujeto_pasivo_sin_IVA_deducible_autorrepercute_contra_el_gasto()
    {
        var f = Factura(1000m, (0m, 1000m, 0m));
        f.InversionSujetoPasivo = true;
        var p = AsientoCompra.Construir(f, Ctx, Cuentas with { AutoRepercutido = "4771021", IvaNoDeducible = true }, Hoy)!;
        Assert.Equal([("4100001", 0m, 1000m), ("6290000", 1000m, 0m), ("6290000", 210m, 0m), ("4771021", 0m, 210m)], Lineas(p.Asiento));
        Assert.Equal((210m, 0m), (p.Libro.CuotaAutorrepercutida, p.Libro.CuotaDeducible));
    }

    [Fact]
    public void Intracomunitaria_va_a_472_y_477_al_tipo_general()
    {
        var f = Factura(200m, (0m, 200m, 0m));
        f.Operacion = OperacionCompra.Intracomunitaria;
        var p = AsientoCompra.Construir(f, Ctx, Cuentas with { AutoSoportado = "4720221", AutoRepercutido = "4770221" }, Hoy)!;
        Assert.Equal([("4100001", 0m, 200m), ("6290000", 200m, 0m), ("4720221", 42m, 0m), ("4770221", 0m, 42m)], Lineas(p.Asiento));
    }

    [Fact]
    public void Importacion_desglosa_el_total_al_tipo_general_sin_477()
    {
        var f = Factura(121m, (0m, 121m, 0m));
        f.Operacion = OperacionCompra.Importacion;
        var p = AsientoCompra.Construir(f, Ctx, Cuentas, Hoy)!;
        Assert.Equal([("4100001", 0m, 121m), ("6290000", 100m, 0m), ("4724001", 21m, 0m)], Lineas(p.Asiento));
        Assert.Equal((100m, 21m), (p.Libro.Base, p.Libro.CuotaSoportada));
    }

    [Fact]
    public void Un_descuadre_de_hasta_tres_centimos_se_ajusta_a_la_cuenta_de_redondeo()
    {
        // 100 + 21,02 = 121,02 frente a un total de 121: sobra Debe → ingresos por redondeo.
        var p = AsientoCompra.Construir(Factura(121m, (21m, 100m, 21.02m)), Ctx, Cuentas, Hoy)!;
        Assert.Equal(0.02m, p.Descuadre);
        Assert.Equal(("7590000", 0m, 0.02m), Lineas(p.Asiento).Last());
        Assert.Equal(p.Asiento.TotalDebe, p.Asiento.TotalHaber);
        var falta = AsientoCompra.Construir(Factura(121.03m, (21m, 100m, 21m)), Ctx, Cuentas, Hoy)!;
        Assert.Equal(("6590000", 0.03m, 0m), Lineas(falta.Asiento).Last());
    }

    [Fact]
    public void Un_descuadre_mayor_solo_se_ajusta_con_motivo_y_se_explica()
    {
        var f = Factura(125m, (21m, 100m, 21m));
        Assert.Null(AsientoCompra.Construir(f, Ctx, Cuentas, Hoy));
        Assert.False(f.Cuadra);
        Assert.Contains("Faltan 4,00", AsientoCompra.ExplicarDescuadre(f).Replace('.', ','));
        var p = AsientoCompra.Construir(f, Ctx, Cuentas, Hoy, "Portes sin IVA no desglosados")!;
        var ajuste = p.Asiento.Apuntes.Last();
        Assert.Equal(("6590000", 4m), (ajuste.CodigoCuenta, ajuste.Debe.Valor));
        Assert.Equal("Ajuste manual: Portes sin IVA no desglosados · Fra. FV1", ajuste.Descripcion);
        var cuota = Factura(121m, (21m, 100m, 25m));
        Assert.Contains("le corresponden 21", AsientoCompra.ExplicarDescuadre(cuota));
    }

    [Fact]
    public void Las_descripciones_llevan_el_nombre_del_proveedor_y_el_numero_y_el_concepto_abreviado()
    {
        var f = Factura(157m, (21m, 100m, 21m), (10m, 50m, 5m));
        f.NumeroFactura = "F2026125";
        f.PorcentajeRetencion = 19m;
        f.ImporteRetencion = 19m;
        f.ClaseRetencion = ClaseRetencion.Arrendamientos;
        var cuentas = Cuentas with { Retencion = "4751000", NombreProveedor = "Instalaciones García", NombreGasto = "Trabajos realizados por otras empresas" };
        var a = AsientoCompra.Construir(f, Ctx, cuentas, Hoy)!.Asiento;
        string D(string cuenta, int n = 0) => a.Apuntes.Where(x => x.CodigoCuenta == cuenta).ElementAt(n).Descripcion!;
        Assert.Equal("Fra. F2026125 · Instalaciones García", D("4100001"));
        Assert.Equal("Trabajos realizados por otras empresas 21% · Instalaciones García · Fra. F2026125", D("6290000"));
        Assert.Equal("IVA soportado 21% · Instalaciones García · Fra. F2026125", D("4720021"));
        Assert.Equal("Retención 19% · Instalaciones García · Fra. F2026125", D("4751000"));

        // Con concepto abreviado: sustituye al nombre de la cuenta de gasto y va al final en el resto.
        f.ConceptoAbreviado = "Reparación caldera";
        a = AsientoCompra.Construir(f, Ctx, cuentas, Hoy)!.Asiento;
        Assert.Equal("Fra. F2026125 · Instalaciones García · Reparación caldera", D("4100001"));
        Assert.Equal("Reparación caldera 10% · Instalaciones García · Fra. F2026125", D("6290000", 1));
        Assert.Equal("IVA soportado 10% · Instalaciones García · Fra. F2026125 · Reparación caldera", D("4720010"));

        // Si no cabe, se recorta el concepto y no el nombre ni el número.
        f.ConceptoAbreviado = new string('x', 300);
        var largo = AsientoCompra.Construir(f, Ctx, cuentas, Hoy)!.Asiento.Apuntes.First().Descripcion!;
        Assert.Equal(ReglasAsiento.LongitudDescripcion, largo.Length);
        Assert.StartsWith("Fra. F2026125 · Instalaciones García · xxx", largo);
    }

    [Fact]
    public void Un_abono_pasa_los_importes_al_lado_contrario()
    {
        var p = AsientoCompra.Construir(Factura(-121m, (21m, -100m, -21m)), Ctx, Cuentas, Hoy)!;
        Assert.Equal([("4100001", 121m, 0m), ("6290000", 0m, 100m), ("4720021", 0m, 21m)], Lineas(p.Asiento));
    }

    [Fact]
    public void El_contra_asiento_cambia_debe_y_haber_y_el_libro_se_niega()
    {
        var p = AsientoCompra.Construir(Factura(121m, (21m, 100m, 21m)), Ctx, Cuentas, Hoy)!;
        var contra = AsientoCompra.Contra(p.Asiento, Ctx, Hoy, "Factura de compra FV1");
        Assert.Equal([("4100001", 121m, 0m), ("6290000", 0m, 100m), ("4720021", 0m, 21m)], Lineas(contra));
        Assert.Equal(TipoAsiento.Rectificacion, contra.Tipo);
        Assert.Equal((-121m, -100m, -21m), (p.Libro.Negado().Total, p.Libro.Negado().Base, p.Libro.Negado().CuotaSoportada));
    }

    [Fact]
    public void Cada_contra_asiento_de_un_trimestre_cerrado_abre_otro_lote()
    {
        Assert.Equal("5", AsientoCompra.Lote(5, 0).ClaveEjecucion);
        Assert.Equal("5.2", AsientoCompra.Lote(5, 2).ClaveEjecucion);
        Assert.Equal("compras/factura-compra", AsientoCompra.Lote(5, 0).MotorPropietario);
    }

    [Fact]
    public void El_trimestre_cerrado_se_compara_con_el_trimestre_activo_del_ejercicio_de_la_factura()
    {
        var config = new ConfiguracionCompras(new Dictionary<decimal, string>(), null, [4m, 10m, 21m, 5m], 3, null, null, null, null, null, null, null, null, null, null, null, null);
        Assert.Equal(21m, config.TipoGeneralEfectivo);
        Assert.True(ContabilizadorCompras.LoteCerrado(new EstadoExportacionCompra(0, 1, null, 2, 2026), config, 2026, Hoy));
        Assert.False(ContabilizadorCompras.LoteCerrado(new EstadoExportacionCompra(0, 1, null, 3, 2026), config, 2026, Hoy));
        Assert.False(ContabilizadorCompras.LoteCerrado(new EstadoExportacionCompra(0, 1, null, 4, 2026), config, 2026, Hoy));
        Assert.True(ContabilizadorCompras.LoteCerrado(new EstadoExportacionCompra(0, 1, null, 4, 2025), config, 2026, Hoy));
        Assert.False(ContabilizadorCompras.LoteCerrado(new EstadoExportacionCompra(0, null, null, null, null), config, 2026, Hoy));
        // En enero se puede seguir con el ejercicio anterior: cuenta su trimestre activo, no el año de hoy.
        Assert.False(ContabilizadorCompras.LoteCerrado(new EstadoExportacionCompra(0, 1, null, 3, 2025), config, 2025, new DateTime(2026, 1, 8)));
        Assert.Equal(19m, (config with { TipoGeneral = 19m }).TipoGeneralEfectivo);
    }

    [Fact]
    public void Las_notas_y_la_forma_de_pago_no_afectan_al_asiento_y_el_centro_de_coste_si()
    {
        var a = Factura(121m, (21m, 100m, 21m));
        var b = Factura(121m, (21m, 100m, 21m));
        b.Notas = "Revisada";
        b.IdFormaPago = 4;
        Assert.False(FacturaCompra.AfectaAlAsiento(a, b));
        b.IdCentroCoste = 9;
        Assert.True(FacturaCompra.AfectaAlAsiento(a, b));
        var c = Factura(121m, (21m, 100m, 21m));
        c.Fecha = c.Fecha!.Value.AddDays(1);
        Assert.True(FacturaCompra.AfectaAlAsiento(a, c));
        Assert.True(FacturaCompra.AfectaAlAsiento(a, Factura(242m, (21m, 200m, 42m))));
    }

    [Fact]
    public void El_asiento_lleva_la_fecha_de_la_factura_o_el_dia_1_del_trimestre_activo()
    {
        // Trimestre 3 activo, a 5 de octubre.
        Assert.Equal(new DateTime(2026, 9, 20), TrimestreContable.FechaAsiento(new DateTime(2026, 9, 20), 2026, 3));
        Assert.Equal(new DateTime(2026, 10, 3), TrimestreContable.FechaAsiento(new DateTime(2026, 10, 3), 2026, 3));
        Assert.Equal(new DateTime(2026, 7, 1), TrimestreContable.FechaAsiento(new DateTime(2026, 6, 10), 2026, 3));
        // Una factura del año anterior, con el primer trimestre activo, va al 1 de enero.
        Assert.Equal(new DateTime(2026, 1, 1), TrimestreContable.FechaAsiento(new DateTime(2025, 12, 28), 2026, 1));
        Assert.Equal(4, TrimestreContable.Activo(null, 2025, new DateTime(2026, 1, 8)));
        Assert.Equal(3, TrimestreContable.Activo(3, 2026, Hoy));
        Assert.Equal(4, TrimestreContable.Activo(null, 2026, Hoy));
    }
}
