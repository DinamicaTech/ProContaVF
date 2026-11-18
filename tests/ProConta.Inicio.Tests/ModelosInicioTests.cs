using ProConta.Inicio.Bloques;
using ProConta.Inicio.Salud;

namespace ProConta.Inicio.Tests;

/// <summary>
/// Plazos de los modelos tributarios en Inicio (contabilidad/modelos-tributarios 2026-11-18): la agenda con los modelos de la sociedad
/// sin lo presentado y abriendo Modelos tributarios en su periodo, y las tareas de los demás modelos. Datos sintéticos.
/// </summary>
public sealed class ModelosInicioTests
{
    private static readonly IReadOnlySet<int> Ejercicios = new HashSet<int> { 2026, 2027 };

    [Fact]
    public void LaAgendaLlevaLosModelosDeLaSociedadSinLoPresentado()
    {
        var modelos = new ModelosInicio(new HashSet<string> { "303", "111", "202", "190" },
            [new PresentacionInicio("111", 2026, "3T", new DateTime(2026, 10, 13))], null);
        var agenda = CalculoAgenda.Calcular(new DateTime(2026, 10, 1), Ejercicios, [], [], modelos);
        var fiscal = agenda.Where(e => e.Tipo == TipoAgenda.Fiscal).ToList();
        Assert.Contains(fiscal, e => e.Texto == "Último día del 303 del 3T" && e.Fecha == new DateTime(2026, 10, 20));
        Assert.DoesNotContain(fiscal, e => e.Texto.Contains("111 del 3T"));                         // presentado
        Assert.Contains(fiscal, e => e.Texto == "Último día del 202 del 3P de 2026" && e.Fecha == new DateTime(2026, 12, 21));
        Assert.DoesNotContain(fiscal, e => e.Texto.Contains("390"));                                // no es de la sociedad
        var p202 = fiscal.Single(e => e.Texto.Contains("202 del 3P"));
        Assert.Equal((PantallaSalud.ModelosTributarios, "202", "3P"), (p202.Destino.Pantalla, p202.Destino.Modelo, p202.Destino.Periodo));
        Assert.Contains("siguiente hábil", p202.Detalle);                                         // el 20/12/2026 es domingo
    }

    [Fact]
    public void LosDemasModelosAvisanAntesDelPlazoYVencidosSoloTrasConsultar()
    {
        var sinConsulta = new ModelosInicio(new HashSet<string> { "303", "390", "111", "115" }, [new PresentacionInicio("115", 2026, "3T", new DateTime(2026, 10, 10))], null);
        var antes = RevisionPlazosModelos.Evaluar(Ejercicios, sinConsulta, new DateTime(2026, 10, 10));
        Assert.Equal(["111-2026-3T"], antes.Elementos.Select(e => e.Clave));                      // el 115 está presentado; el 303 lo lleva su revisión
        Assert.Equal(GravedadSalud.Atencion, antes.Elementos[0].Gravedad);
        Assert.Equal(PantallaSalud.ModelosTributarios, antes.Elementos[0].Destino.Pantalla);
        Assert.Empty(RevisionPlazosModelos.Evaluar(Ejercicios, sinConsulta, new DateTime(2026, 10, 3)).Elementos);   // más de 16 días

        // Vencido sin consulta posterior al plazo: no se sabe si lo presentó la gestoría.
        Assert.Empty(RevisionPlazosModelos.Evaluar(Ejercicios, sinConsulta, new DateTime(2026, 10, 25)).Elementos);
        var consultado = sinConsulta with { UltimaConsulta = new DateTime(2026, 10, 24) };
        var vencido = RevisionPlazosModelos.Evaluar(Ejercicios, consultado, new DateTime(2026, 10, 25)).Elementos.Single();
        Assert.Equal(("111-2026-3T", GravedadSalud.Grave), (vencido.Clave, vencido.Gravedad));
        Assert.Contains("24/10/2026", vencido.Condicion);
    }
}
