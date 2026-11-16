using ProConta.Norma43.Busqueda;
using ProConta.Norma43.Lineas;
using ProConta.Norma43.UI.VistaModelos;

namespace ProConta.Norma43.Tests;

/// <summary>Búsqueda por importe (norma43/busqueda-por-importe) sin base de datos: criterios, orden, premarcado y marcado en el diálogo.</summary>
public sealed class BusquedaImporteN43Tests
{
    private static readonly DateTime Banco = new(2026, 3, 15);

    private static LineaN43 L(int id, decimal importe, bool cargo = true, DateTime? fecha = null, bool cerrado = false, int? asiento = null, bool propio = true) =>
        new(id, 1, 1, "Banco uno", "0049 1500 0012345678", "0049", fecha ?? Banco, fecha ?? Banco, 2026, cerrado, cargo ? "D" : "H", importe, "02", "MOVIMIENTO FICTICIO",
            null, null, null, "5720001", "Banco uno", [], asiento, propio, null);

    private static DocumentoN43 C(int id, decimal importe, int diasAntes, int? linea = null) =>
        new(TipoDocumentoN43.Compra, id, $"C-{id}", Banco.AddDays(-diasAntes), importe, "A00000000", "Proveedor ficticio", "Compra ficticia", linea);

    private static DocumentoN43 V(int id, decimal importe, int diasAntes, int? linea = null) =>
        new(TipoDocumentoN43.Venta, id, $"V-{id}", Banco.AddDays(-diasAntes), importe, "B00000000", "Cliente ficticio", "Venta ficticia", linea);

    [Fact]
    public void Un_cargo_busca_compras_de_su_importe_a_180_dias_sin_enlazar_con_otra_linea()
    {
        var docs = new[]
        {
            C(1, 100m, 3), C(2, 100.02m, -180), C(3, 99.98m, 180), C(4, 100.03m, 0), C(5, 100m, 181), C(6, 100m, -181),
            C(7, 100m, 1, linea: 99), C(8, 100m, 400, linea: 1), V(9, 100m, 0),
        };
        var r = ReglasBusquedaImporteN43.Elegir(L(1, 100m), docs);
        // ±0,02 € y ±180 días; la enlazada con otra línea y la venta no salen; la ya enlazada con esta sale aunque esté fuera de plazo.
        Assert.Equal([1, 2, 3, 8], r.Select(c => c.Documento.Id).OrderBy(i => i));
        Assert.Equal(1, r[0].Documento.Id); // la más cercana en fecha, primero
        Assert.True(r.Single(c => c.Documento.Id == 8).Enlazada);
        Assert.Equal(0.02m, r.Single(c => c.Documento.Id == 2).Diferencia);
        Assert.Equal(-180, r.Single(c => c.Documento.Id == 2).Dias); // factura posterior al movimiento
    }

    [Fact]
    public void Un_abono_busca_ventas_de_hasta_270_dias_antes_o_5_despues()
    {
        var docs = new[] { V(1, 250m, 270), V(2, 250m, -5), V(3, 250m, 271), V(4, 250m, -6), V(5, 250m, 30), C(6, 250m, 0) };
        var r = ReglasBusquedaImporteN43.Elegir(L(1, 250m, cargo: false), docs);
        Assert.Equal([2, 5, 1], r.Select(c => c.Documento.Id));
    }

    [Fact]
    public void Como_mucho_salen_10_candidatas_por_linea_las_mas_cercanas_en_fecha()
    {
        var docs = Enumerable.Range(1, 15).Select(i => C(i, 50m, 16 - i)).ToList();
        var r = ReglasBusquedaImporteN43.Elegir(L(1, 50m), docs);
        Assert.Equal(ReglasBusquedaImporteN43.Maximo, r.Count);
        Assert.Equal(Enumerable.Range(6, 10).Reverse(), r.Select(c => c.Documento.Id));
    }

    [Fact]
    public void Vienen_marcadas_las_enlazadas_y_las_de_0_o_1_dia_y_una_factura_solo_en_una_linea()
    {
        var l1 = L(1, 100m);
        var l2 = L(2, 100m, fecha: Banco.AddDays(1));
        var docs = new[] { C(1, 100m, 1), C(2, 100m, 2), C(3, 100m, 40, linea: 2), C(4, 100m, 0) };
        var busquedas = new[] { new BusquedaLineaN43(l1, ReglasBusquedaImporteN43.Elegir(l1, docs)), new BusquedaLineaN43(l2, ReglasBusquedaImporteN43.Elegir(l2, docs)) };
        var marcadas = ReglasBusquedaImporteN43.Premarcadas(busquedas);
        // C-4 (0 días de la 1, 1 de la 2) y C-1 (1 día de la 1, 2 de la 2) van con la 1; C-2 (2 días) no se marca; C-3 ya estaba con la 2.
        Assert.Equal(new HashSet<(int, TipoDocumentoN43, int)>
        {
            (1, TipoDocumentoN43.Compra, 4), (1, TipoDocumentoN43.Compra, 1), (2, TipoDocumentoN43.Compra, 3),
        }, marcadas.ToHashSet());
    }

    [Fact]
    public void En_el_dialogo_marcar_una_factura_en_otra_linea_la_desmarca_en_la_primera_y_los_cambios_son_lo_distinto()
    {
        var l1 = L(1, 100m);
        var l2 = L(2, 100m, fecha: Banco.AddDays(20));
        var docs = new[] { C(1, 100m, 0), C(2, 100m, 10, linea: 2) };
        var vm = new BusquedaImporteN43VM([new BusquedaLineaN43(l1, ReglasBusquedaImporteN43.Elegir(l1, docs)), new BusquedaLineaN43(l2, ReglasBusquedaImporteN43.Elegir(l2, docs))], null);
        var c1EnL1 = vm.Grupos[0].Candidatos.Single(c => c.Datos.Documento.Id == 1);
        var c1EnL2 = vm.Grupos[1].Candidatos.Single(c => c.Datos.Documento.Id == 1);
        var c2EnL2 = vm.Grupos[1].Candidatos.Single(c => c.Datos.Documento.Id == 2);
        Assert.True(c1EnL1.Marcado);
        Assert.False(c1EnL2.Marcado);
        Assert.True(c2EnL2.Marcado);
        Assert.Equal([new CambioEnlaceN43(1, TipoDocumentoN43.Compra, 1, true)], vm.Cambios());

        c1EnL2.Alternar(); // un clic: pasa a la línea 2
        Assert.False(c1EnL1.Marcado);
        c2EnL2.Alternar(); // desmarcar la enlazada la desenlaza
        Assert.Equal(new[] { new CambioEnlaceN43(2, TipoDocumentoN43.Compra, 1, true), new CambioEnlaceN43(2, TipoDocumentoN43.Compra, 2, false) }.ToHashSet(), vm.Cambios().ToHashSet());
        Assert.Equal("Se enlaza 1 factura y se desenlaza 1 factura.", vm.Resumen);
    }

    [Fact]
    public void Buscar_por_importe_vale_con_una_o_varias_lineas_tambien_contabilizadas_pero_no_en_un_ejercicio_cerrado()
    {
        Assert.NotNull(AccionesLineasN43.Evaluar([]).BuscarPorImporte);
        Assert.Null(AccionesLineasN43.Evaluar([L(1, 10m), L(2, 20m, cargo: false), L(3, 30m, asiento: 7, propio: false)]).BuscarPorImporte);
        Assert.Contains("cerrado", AccionesLineasN43.Evaluar([L(1, 10m, cerrado: true)]).BuscarPorImporte);
    }
}
