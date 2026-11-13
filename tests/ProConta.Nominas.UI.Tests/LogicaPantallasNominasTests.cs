using ProConta.Nominas.Consulta;
using ProConta.Nominas.Importacion;
using ProConta.Nominas.Lectura;
using ProConta.Nominas.UI.VistaModelos;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.Nominas.UI.Tests;

/// <summary>Lógica de las pantallas de nóminas sin ventanas ni BD: cambios entre versiones, filas y pestaña del empleado. Datos inventados.</summary>
public sealed class LogicaPantallasNominasTests
{
    private static readonly ContextoNominas Contexto = new(new ContextoSociedadEjercicio("T01", 9001, 2026), "Sociedad sintética", new Actor("Pruebas"), _ => { });

    private static FilaNomina Fila(string? codigo, string nombre, decimal bruto, decimal neto, decimal irpf = 0, DateTime? fecha = null) =>
        new(1, 1, codigo, nombre, null, TipoPaga.Mensual, fecha ?? new DateTime(2026, 10, 31),
            new Dictionary<string, decimal> { ["Bruto"] = bruto, ["Neto"] = neto, ["IRPF111"] = irpf, ["CostEmp"] = bruto });

    [Fact]
    public void Compara_las_versiones_por_codigo_o_por_nombre_y_dice_quien_no_cambia()
    {
        var antes = new[] { Fila("000901", "FICTICIO UNO, ANA", 2300m, 2300m), Fila("000902", "FICTICIO DOS, LUIS", 2400m, 1620m, 780m), Fila(null, "SIN CODIGO, EVA", 1000m, 1000m) };
        var ahora = new[] { Fila("000901", "FICTICIO UNO, ANA", 2300m, 2300m), Fila("000902", "FICTICIO DOS, LUIS", 2500m, 1700m, 800m), Fila(null, "Sin Codigo, Eva", 1000m, 1000m),
            Fila("000904", "NUEVA CUATRO, RITA", 900m, 900m) };
        var (cambios, iguales) = NominasVM.Comparar(antes, ahora);
        Assert.Equal(["FICTICIO UNO, ANA", "Sin Codigo, Eva"], iguales);
        Assert.Equal(["Bruto", "IRPF", "Neto", "Coste empresa"], cambios.Where(c => c.Trabajador == "FICTICIO DOS, LUIS").Select(c => c.Campo));
        Assert.Contains(cambios, c => c is { Trabajador: "NUEVA CUATRO, RITA", Campo: "Nuevo en esta versión", Antes: 0m, Ahora: 900m });

        var (quitados, _) = NominasVM.Comparar(antes, ahora.Take(1).ToList());
        Assert.Contains(quitados, c => c is { Trabajador: "FICTICIO DOS, LUIS", Campo: "Ya no está en esta versión", Antes: 1620m });
    }

    [Fact]
    public void La_fila_de_una_nomina_avisa_si_no_cuadra_o_no_esta_resuelta()
    {
        var buena = new FilaNominaVM(new NominaGuardada(1, 1, 7, 70, "Ana Ficticia", "4650001", 33, Fila("000901", "FICTICIO UNO, ANA", 2300m, 2300m)));
        Assert.True(buena.Cuadra);
        Assert.Equal("✓", buena.Cuadre);
        Assert.Equal("Ana Ficticia", buena.Trabajador);
        Assert.False(buena.SinResolver);

        var mala = new FilaNominaVM(new NominaGuardada(2, 1, null, null, null, null, null, Fila("000909", "DESCONOCIDA, MAR", 1000m, 990m)));
        Assert.False(mala.Cuadra);
        Assert.True(mala.SinResolver);
        Assert.Equal("DESCONOCIDA, MAR", mala.Trabajador);
        Assert.Contains("Resolver trabajador", mala.AyudaTrabajador);
    }

    private static ImportacionNominaResumen Periodo(int id, DateTime fecha, string estado = ServicioImportacionNominas.EstadoContabilizada) =>
        new(id, new DateTime(fecha.Year, fecha.Month, 1), fecha, TipoPaga.Mensual, null, fecha, OrigenNominas.Fichero, "nominas.xlsx", estado, null, null, 1,
            fecha, "Pruebas", null, null, null, 1, 0, 1, 0, 0, 0, 0, 0);

    [Fact]
    public void La_pestana_del_empleado_empieza_en_el_ejercicio_activo_y_suma_lo_que_se_ve()
    {
        var nominas = new[]
        {
            new NominaEmpleado(new NominaGuardada(3, 3, 7, 70, "Luis", "4650002", 40, Fila("000902", "FICTICIO DOS, LUIS", 2400m, 1620m, 780m, new DateTime(2026, 10, 31))),
                Periodo(3, new DateTime(2026, 10, 31)), 2026),
            new NominaEmpleado(new NominaGuardada(2, 2, 7, 70, "Luis", "4650002", 30, Fila("000902", "FICTICIO DOS, LUIS", 2400m, 1620m, 780m, new DateTime(2026, 9, 30))),
                Periodo(2, new DateTime(2026, 9, 30)), 2026),
            new NominaEmpleado(new NominaGuardada(1, 1, 7, 70, "Luis", "4650002", 10, Fila("000902", "FICTICIO DOS, LUIS", 2000m, 1400m, 600m, new DateTime(2025, 12, 31))),
                Periodo(1, new DateTime(2025, 12, 31)), 2025),
        };
        var vm = new NominasEmpleadoVM(new EmpleadoNominas(7, "000902", "4650002", "FICTICIO DOS, LUIS", new DateTime(2025, 12, 31), true), nominas, Contexto, _ => { });
        Assert.Equal(["2025", "2026", "Todos"], vm.Anyos.Select(a => a.Texto));
        Assert.Equal(2, vm.Filas.Count);
        Assert.Equal("IRPF retenido · 2026", vm.TituloIrpf);
        Assert.Equal("1.560,00", vm.TotalIrpf.Replace(" €", ""));

        vm.Anyos[2].Marcado = true;
        Assert.Equal(3, vm.Filas.Count);
        Assert.False(vm.Anyos[1].Marcado);
        Assert.Equal("Bruto · 2025–2026", vm.TituloBruto);

        vm.Actual = vm.Filas[2];
        Assert.False(vm.AbrirPeriodoCommand.CanExecute(null));
        vm.Actual = vm.Filas[0];
        Assert.True(vm.AbrirPeriodoCommand.CanExecute(null));
    }
}
