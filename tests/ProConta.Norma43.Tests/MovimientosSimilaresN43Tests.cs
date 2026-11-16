using ProConta.Norma43.Lineas;
using ProConta.Norma43.Similares;
using ProConta.Norma43.UI.VistaModelos;

namespace ProConta.Norma43.Tests;

/// <summary>Cálculo de «Movimientos similares» (norma43/movimientos-similares) y su diálogo, sin BD.</summary>
public sealed class MovimientosSimilaresN43Tests
{
    private static LineaN43 L(int id, string concepto, int ejercicio = 2026, int? especial = null, int? origen = null, int? desglose = null,
        int? identidad = null, bool cerrado = false) =>
        new(id, 1, 1, "Banco uno", "0049 1500 0012345678", "0049", new DateTime(ejercicio, 3, 1), new DateTime(ejercicio, 3, 1), ejercicio, cerrado, "D", 10m,
            "02", concepto, null, null, null, "5720001", "Banco uno",
            identidad is int i ? [new DestinoLineaN43(i, "6280001", "Suministros", 10m, "MANUAL", null, null)] : [], null, false, null,
            Especial: especial, IdExtractoDesglose: desglose, IdLineaOrigen: origen);

    private static CandidatoSimilarN43 C(int id, string concepto, int identidad = 10, string cuenta = "6280001", int ejercicio = 2026, int dia = 1, int? asiento = null) =>
        new(id, new DateTime(ejercicio, 2, dia), ejercicio, true, 10m, concepto, identidad, cuenta, $"Cuenta {cuenta}", asiento);

    [Fact]
    public void Las_palabras_quitan_numeros_acentos_y_palabras_cortas()
    {
        Assert.Equal(["recibo", "energia", "electrica", "espana", "ref"],
            ReglasSimilaresN43.Palabras("RECIBO 2026/03 ENERGÍA ELÉCTRICA DE ESPAÑA REF:R20260312 Nº 45 recibo"));
        Assert.Empty(ReglasSimilaresN43.Palabras("12/03 45,00 AB"));
        Assert.Empty(ReglasSimilaresN43.Palabras(null));
    }

    [Fact]
    public void Una_palabra_cortada_por_el_banco_cuenta_como_la_completa_con_cuatro_letras_o_mas()
    {
        Assert.True(ReglasSimilaresN43.MismaPalabra("electrica", "electr"[..4]));
        Assert.True(ReglasSimilaresN43.MismaPalabra("suministros", "suminis"));
        Assert.False(ReglasSimilaresN43.MismaPalabra("pago", "pag"));
        Assert.False(ReglasSimilaresN43.MismaPalabra("cargo", "recargo"));
        // El concepto truncado por el banco se parece del todo al completo.
        Assert.Equal(100, ReglasSimilaresN43.Similitud("RECIBO ELECTRICA FICTICIA SUMINIS", "RECIBO ELECTRICA FICTICIA SUMINISTRO 0326"));
    }

    [Fact]
    public void La_similitud_es_la_parte_comun_de_las_palabras_y_no_le_afectan_referencias_ni_fechas()
    {
        // Mismas palabras, otra referencia y otra fecha: iguales (Ratcliff/Obershelp por caracteres daba menos del 100 %).
        Assert.Equal(100, ReglasSimilaresN43.Similitud("RECIBO SEGUROS FICTICIOS POLIZA 88123 03/2026", "RECIBO SEGUROS FICTICIOS POLIZA 99541 11/2025"));
        // 2 compartidas de 4 distintas: 50 %.
        Assert.Equal(50, ReglasSimilaresN43.Similitud("TRANSFERENCIA CLIENTE ALFA", "TRANSFERENCIA CLIENTE BETA"));
        Assert.Equal(0, ReglasSimilaresN43.Similitud("COMPRA TARJETA GASOLINERA", "RECIBO AGUA MUNICIPAL"));
        Assert.Equal(0, ReglasSimilaresN43.Similitud("", "RECIBO"));
    }

    [Fact]
    public void La_lista_va_por_porcentaje_sin_la_propia_linea_desde_el_20_y_en_tres_ejercicios()
    {
        var linea = L(1, "RECIBO LUZ FICTICIA HOGAR");
        var candidatos = new[]
        {
            C(1, "RECIBO LUZ FICTICIA HOGAR"),                  // la propia línea: fuera
            C(2, "RECIBO LUZ FICTICIA", dia: 3),                 // 75 %
            C(3, "RECIBO LUZ FICTICIA HOGAR", dia: 2),           // 100 %
            C(4, "RECIBO AGUA MUNICIPAL"),                       // 1 de 6: 17 %, fuera
            C(5, "RECIBO LUZ FICTICIA HOGAR", ejercicio: 2023),  // fuera del rango 2024-2026
            C(6, "RECIBO LUZ FICTICIA HOGAR", ejercicio: 2024),  // dentro
            C(7, "RECIBO GAS"),                                  // 1 de 5: 20 %, dentro por poco
        };
        var r = ReglasSimilaresN43.Buscar(linea, candidatos);
        Assert.Equal([3, 6, 2, 7], r.Select(x => x.Linea.IdLinea));
        Assert.Equal([100, 100, 75, 20], r.Select(x => x.Porcentaje));
        Assert.Equal((2024, 2026), ReglasSimilaresN43.Rango(2026));
        Assert.Equal(2, ReglasSimilaresN43.Buscar(linea, candidatos, maximo: 2).Count);
    }

    [Fact]
    public void El_avance_da_tres_desde_el_50_con_otra_cuenta_y_sin_repetirla()
    {
        var linea = L(1, "RECIBO LUZ FICTICIA HOGAR", identidad: 10);
        var candidatos = new[]
        {
            C(2, "RECIBO LUZ FICTICIA HOGAR", identidad: 10),               // la cuenta que ya tiene: fuera
            C(3, "RECIBO LUZ FICTICIA HOGAR", identidad: 20, cuenta: "6280002", dia: 5),
            C(4, "RECIBO LUZ FICTICIA", identidad: 20, cuenta: "6280002"),  // misma cuenta que la 3, menos parecida: fuera
            C(5, "RECIBO LUZ FICTICIA", identidad: 30, cuenta: "6280003"),
            C(6, "RECIBO LUZ", identidad: 40, cuenta: "6280004"),           // 50 %
            C(7, "RECIBO LUZ", identidad: 50, cuenta: "6280005", dia: 9),   // 50 %, más reciente, pero ya hay tres
            C(8, "RECIBO", identidad: 60, cuenta: "6280006"),               // 25 %: fuera
        };
        var a = ReglasSimilaresN43.Avance(linea, candidatos);
        Assert.Equal([3, 5, 7], a.Select(x => x.Linea.IdLinea));
    }

    [Fact]
    public void Las_remesas_los_recibos_y_los_conceptos_sin_palabras_no_se_comparan()
    {
        Assert.NotNull(ReglasSimilaresN43.Motivo(L(1, "REMESA RECIBOS", especial: 5)));
        Assert.NotNull(ReglasSimilaresN43.Motivo(L(1, "DEVOLUCION RECIBOS", especial: 6)));
        Assert.NotNull(ReglasSimilaresN43.Motivo(L(1, "REMESA RECIBOS", desglose: 7)));
        Assert.NotNull(ReglasSimilaresN43.Motivo(L(1, "RECIBO CLIENTE", origen: 3)));
        Assert.NotNull(ReglasSimilaresN43.Motivo(L(1, "12345 AB")));
        Assert.Null(ReglasSimilaresN43.Motivo(L(1, "RECIBO LUZ")));
        Assert.Empty(ReglasSimilaresN43.Buscar(L(1, "REMESA RECIBOS", especial: 5), [C(2, "REMESA RECIBOS")]));
    }

    [Fact]
    public void El_dialogo_dice_a_quien_se_asigna_y_devuelve_lo_elegido()
    {
        var linea = L(1, "RECIBO LUZ FICTICIA");
        var r = ReglasSimilaresN43.Buscar(linea, [C(2, "RECIBO LUZ FICTICIA", asiento: 77), C(3, "RECIBO LUZ")]);
        var vm = new MovimientosSimilaresN43VM(linea, [linea, L(5, "OTRO")], r, null, _ => Task.CompletedTask);
        Assert.Contains("2 líneas seleccionadas", vm.Alcance);
        Assert.Equal("2 movimientos parecidos", vm.Resumen);
        Assert.True(vm.Resultados[0].PuedeVerAsiento);
        Assert.False(vm.Resultados[1].PuedeVerAsiento);
        var cerrado = false;
        vm.Cerrar += () => cerrado = true;
        vm.Resultados[0].AsignarCommand.Execute(null);
        Assert.True(cerrado);
        Assert.Equal((AccionSimilarN43.Asignar, 2), (vm.Eleccion!.Accion, vm.Eleccion.Resultado.Linea.IdLinea));

        // Con un motivo (ejercicio cerrado…), «Asignar» está desactivado y lo explica.
        var bloqueado = new MovimientosSimilaresN43VM(linea, [linea], r, "El ejercicio 2025 está cerrado.", null);
        Assert.False(bloqueado.Resultados[0].AsignarCommand.CanExecute(null));
        Assert.Equal("El ejercicio 2025 está cerrado.", bloqueado.Resultados[0].AyudaAsignar);
        Assert.False(bloqueado.Resultados[0].PuedeVerAsiento);
        Assert.True(bloqueado.Resultados[0].IrCommand.CanExecute(null));
    }
}
