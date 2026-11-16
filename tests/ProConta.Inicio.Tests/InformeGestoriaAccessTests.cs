using System.IO;
using System.Globalization;
using System.Text;
using ProConta.Contabilidad.Iva;
using ProConta.Datos.Esquema;
using ProConta.Datos.Persistencia;
using ProConta.Datos.Persistencia.Access;
using ProConta.Informes.Excel;
using ProConta.Inicio.Gestoria;
using ProConta.Nucleo.Auditoria;
using ProConta.Nucleo.Contexto;

namespace ProConta.Inicio.Tests;

/// <summary>
/// Informe para la gestoría sobre una copia de la BD de pruebas (solo en el equipo del propietario). PROCONTA_BD_GESTORIA apunta
/// a la copia; se actualiza su esquema (como al abrirla con el programa), se compone el informe del ejercicio
/// PROCONTA_GESTORIA_EJERCICIO (2026 por defecto) y del trimestre PROCONTA_GESTORIA_TRIMESTRE (3 por defecto), y se guarda el
/// libro y un resumen con las líneas de IVA en la carpeta de la copia. Sin la variable, la prueba no hace nada.
/// </summary>
[Trait("Categoria", "Access")]
public sealed class InformeGestoriaAccessTests
{
    [Fact]
    public async Task GeneraElLibroConLaCopiaDePruebas()
    {
        if (Environment.GetEnvironmentVariable("PROCONTA_BD_GESTORIA") is not { Length: > 0 } ruta || !File.Exists(ruta)) return;
        var anio = int.TryParse(Environment.GetEnvironmentVariable("PROCONTA_GESTORIA_EJERCICIO"), out var a) ? a : 2026;
        var trimestre = int.TryParse(Environment.GetEnvironmentVariable("PROCONTA_GESTORIA_TRIMESTRE"), out var t) ? t : 3;
        var carpeta = Path.GetDirectoryName(ruta)!;
        var copias = Path.Combine(carpeta, "copias-esquema");
        Directory.CreateDirectory(copias);
        var actualizado = await new ActualizadorEsquema(new Actor("prueba.gestoria")).ActualizarAsync(ruta, copias);
        Assert.True(actualizado.EsCorrecto, actualizado.Error?.ToString());

        IBaseDatos bd = new BaseDatosAccess(ruta);
        var (ctx, razon) = await bd.EjecutarAsync(async s =>
        {
            var fila = await s.ConsultarPrimeroAsync(
                "SELECT se.IdSociedadEjercicio, se.IdSociedad, so.Nombre FROM SociedadesEjercicios AS se INNER JOIN Sociedades AS so ON se.IdSociedad = so.IdSociedad " +
                "WHERE se.Ejercicio = ? AND EXISTS (SELECT a.IdAsiento FROM Asientos AS a WHERE a.IdSociedadEjercicio = se.IdSociedadEjercicio)",
                f => (Id: f.Entero("IdSociedadEjercicio"), Sociedad: f.Texto("IdSociedad")!, Nombre: f.Texto("Nombre") ?? ""), anio.ToString(CultureInfo.InvariantCulture));
            return (new ContextoSociedadEjercicio(fila.Sociedad, fila.Id, anio), fila.Nombre);
        });
        var ahora = DateTime.Now;
        var inf = await new ComposicionInformeGestoria(bd).ComponerAsync(ctx, razon, trimestre, ahora);
        var libro = Path.Combine(carpeta, InformeGestoria.NombreFichero(anio, trimestre, ahora));
        ExportadorInformeGestoriaExcel.Exportar(inf, libro);
        Assert.True(File.Exists(libro));

        var es = CultureInfo.GetCultureInfo("es-ES");
        var r = new StringBuilder();
        r.AppendLine($"Libro: {libro}");
        r.AppendLine($"Prorrata provisional {inf.ProrrataProvisional:P2} · real {inf.Prorrata.Real:P0} ({inf.Prorrata.Numerador.ToString("N2", es)} / {inf.Prorrata.Denominador.ToString("N2", es)} = {inf.Prorrata.Cociente:P4})");
        for (var q = 1; q <= 4; q++)
            r.AppendLine($"{q}T devengado {inf.Iva303.TotalDevengado(q).ToString("N2", es)} · deducible {inf.Iva303.TotalDeducible(q).ToString("N2", es)} · resultado {inf.Iva303.Resultado(q).ToString("N2", es)} · regularización {inf.Prorrata.Regularizacion(q).ToString("N2", es)}");
        r.AppendLine($"Regularización estimada total {inf.Prorrata.Regularizacion(0).ToString("N2", es)}");
        foreach (var c in ControlesGestoria.Calcular(inf)) r.AppendLine($"[{c.Estado}] {c.Nombre}: {c.Valor.ToString("N2", es)} / {c.Referencia?.ToString("N2", es)} · {c.Nota}");
        File.WriteAllText(Path.Combine(carpeta, $"InformeGestoria_{anio}-{trimestre}T_resumen.txt"), r.ToString(), Encoding.UTF8);
        // Las líneas de IVA, para comparar apunte a apunte con el libro anterior (se quedan en la carpeta de la copia, fuera del repositorio).
        var csv = new StringBuilder("Fecha;Trim;Asiento;Sentido;Operacion;ISP;Tipo;Centro;Departamento;Actividad;Deduccion;Base;Cuota;CuentaIva;Aviso\n");
        foreach (var l in inf.LineasIva)
            csv.AppendLine(string.Join(";", l.Fecha.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), l.Trimestre, l.IdAsiento, l.Sentido, l.Operacion, l.Isp, l.TipoIva.ToString(CultureInfo.InvariantCulture),
                l.Centro, l.Departamento, l.Actividad, l.Deduccion, l.Base.ToString(CultureInfo.InvariantCulture), l.Cuota.ToString(CultureInfo.InvariantCulture), l.CuentaIva, l.Aviso));
        File.WriteAllText(Path.Combine(carpeta, $"InformeGestoria_{anio}_lineas.csv"), csv.ToString(), Encoding.UTF8);
        Console.WriteLine(r.ToString());

        // Invariantes: el año es la suma de los trimestres y cada línea tiene trimestre del ejercicio o aviso.
        Assert.Equal(Enumerable.Range(1, 4).Sum(inf.Iva303.Resultado), inf.Iva303.Resultado(0));
        Assert.All(inf.LineasIva.Where(l => l.Trimestre == 0), l => Assert.Equal(TextosIva.AvisoFueraEjercicio, l.Aviso));
    }
}
